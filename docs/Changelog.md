# Changelog

A consolidated record of feature batches and fixes, newest first. Each entry
points to the feature doc that carries the full detail. **Everything below builds
clean but is verified in-game only where a test in [Testing.md](Testing.md) is
marked passed** — assume "unverified in a live session" otherwise.

---

## Inter-server crossing threw, and the switch never happened — released 0.10.0 (2026-08-30)  ✅ VERIFIED

Reported from a live attempt: `InvalidOperationException: Collection was modified;
enumeration operation may not execute` out of `CompanionTeleport.Followers`, the
player stayed in the world, and a following Dvergr despawned and then reappeared.

**Root cause.** `Followers` was a lazy `yield return` iterator walking
`DvergrCompanion.All` — a **static HashSet** kept in sync by the component's
`OnEnable`/`OnDisable`. The inter-server crossing seals each follower as it walks
that sequence, and sealing calls `DespawnToTotem()`, which destroys the
GameObject, which fires `OnDisable`, which removes the entry from the very set
being enumerated. The next `MoveNext` threw. `Followers` now builds and returns a
materialised `List<DvergrCompanion>` snapshot, so no caller — present or future —
can mutate the registry out from under it.

**Why the switch didn't happen.** The throw came from a Harmony **prefix**, and an
exception in a prefix propagates to the caller instead of the original method
running: `WorldSwitcher.Leave()` never executed, so `Game.Logout` was never called.
Our optional feature had taken the host mod's core flow down with it. All three
bridge hooks are now wrapped in try/catch that logs and continues — a companion
left behind is a far better failure than a crossing that silently doesn't happen.

**Why the Dvergr came back.** The companions sealed before the throw were real, and
`InterServerArrival` correctly saw pending work a few seconds later and summoned
them — in the same world, since the crossing never happened. That recovery is
working as designed; it was only visible because of the bug above. One related
weakness is fixed alongside it: the pending list's timestamp was written *after*
the seal loop, so a loop that died part-way left `HasPending` true with no expiry.
It is now stamped as each companion is added.

---

## A companion barked its alert voice on every hit taken — released 0.10.0 (2026-08-30)  ✅ VERIFIED

Reported as "when the Dvergr attacks I hear multiple voices". The mechanism is in
`BaseAI.SetAlerted`, which acts **only on a transition** — and on a false→true
transition it does two things that make noise: `m_alertedEffects.Create(...)` (the
Dvergr's alert shout) and `m_animator.SetBool("alert", true)`.

Vanilla writes that flag `true` from places that run **continuously**:
`MonsterAI.UpdateAI` sets it every AI tick while the creature can see its target
(MonsterAI:511), `MonsterAI.OnDamaged` sets it on **every hit taken**, and
`Character` alerts nearby AI besides. Vanilla gets away with this because nothing
ever sets it back to false mid-fight — it latches on and the bark plays once.

Two places in this mod wrote it `false` **every frame**, which unlatched it and let
every one of those vanilla writers re-fire the bark:

* `DvergrCompanion.Update`'s new stance/leash target drop — added in the combat
  leash batch earlier the same day.
* `DvergrCompanion.ApplyEncumbrance` — pre-existing. `CompanionInventoryAI` calls it
  every frame while a pack is over the cap (deliberately, so an overloaded ally
  can't re-acquire between 1 Hz ticks), and it cleared the alert flag on each call.

Either way an ally that was taking hits while not allowed to fight back — lagging
past the follow leash, working a chore, on Standby, or overloaded — shouted once
per incoming hit.

Fixed with a latch: both paths now go through `DvergrCompanion.StandDown()`, which
always drops the target but clears the alerted flag **at most once per stand-down**.
The latch resets when the ally legitimately holds a target again, when a load drops
back under the cap, and on a deliberate stance change. Vanilla's own 30 s
no-contact timeout in `MonsterAI.UpdateTarget` finishes calming it down, so nothing
is lost by not writing the flag ourselves — the ally just keeps the alert pose for
a few seconds after breaking off.

**The rule this leaves behind: never write `SetAlerted` on a repeating tick.** It is
an edge-triggered, effect-spawning setter, not a state you can safely re-assert.

---

## Resting at camp mends your companions — released 0.10.0 (2026-08-30)  ✅ VERIFIED

Sit by a campfire, or stand under a roof with one lit, and every **Follow**-stance
ally within `Companions/RestedHealRadius` (default 10 m) regenerates health,
reaching full in `Companions/RestedHealSeconds` (default 120 s). Chore, Guard,
Standby, dueling and feral allies are left alone — this is for the ones camped with
you. A vanilla **Resting** status icon shows above the ally's health bar while it
mends. ([Ally-Commands.md](Ally-Commands.md), [Testing.md](Testing.md) §31.)

**The design decision worth recording is which status effect it keys off.** Vanilla
has two and they are easy to confuse: **`Resting`** is the LIVE state
(`Player.UpdateEnvStatusEffects` adds it while near a fire and either sitting or
sheltered, and removes it the instant you stand up), while **`Rested`** is the
lingering buff that state accrues and which survives five-plus minutes of
travelling. `Rested` is the one players talk about, but keying off it would have
kept healing allies halfway across the map. `Resting` makes the healing start and
stop exactly with the camp, which is what "while the master is resting" means.

It runs on the **owner's client** (`CompanionRestedHeal`, on the plugin GameObject,
ticking every 2 s to match `BaseAI`'s own regen cadence) because the rest state is
computed locally and is not reliably replicated — the owner's machine is the only
place the condition can be read honestly. Healing from there is safe because
`Character.Heal` routes to the companion's ZDO owner over `RPC_Heal` and clamps to
max HP, the same reason mead feeding was moved onto it. The heal is a fraction of
the ally's own pool, so a level-10 companion takes the same time to mend as a
fresh one. Also in this batch: the Follow combat leash's default moved **10 m →
20 m**, a workbench's build radius.

---

## Totem persistence, the companion combat leash, and InterServerPortal travel — released 0.10.0 (2026-08-30)  ✅ VERIFIED

Three bug fixes and one feature, all from in-game reports. **All verified in a live
session 2026-08-31** ([Testing.md](Testing.md) §28–§30) and shipped in 0.10.0.

**1. A sealed companion turned back into a Fuling Totem after a relog.** The
per-instance `SharedData` clone that gives a Communion Totem its name,
description and — crucially — `m_maxStackSize = 1` was re-applied on the two
`ItemDrop.LoadFromZDO` overloads only. **The player's inventory and every
`Container` do not load that way**: `Inventory.Load` rebuilds each item by
instantiating its prefab, so a saved totem came back with the stock Fuling Totem
data. The name reset was the visible half; the stack cap coming back was the
dangerous half, since Valheim stacks by shared **name** and ignores
`m_customData` — two sealed companions in one slot would have merged and one
would have been lost.

The re-apply therefore had to move **before** the stacking decision, not after
the load. `Inventory.Load` → `AddItem(name, …, customData, …)` writes
`m_customData` and then calls the private `AddItem(ItemData, int, int, int)`,
which is where the "same shared name → merge into that slot" check lives; a
prefix there is the first point at which the item is both identifiable as a
companion totem and still un-stacked. A postfix on `Inventory.Load` sweeps the
finished list as a catch-all. (The class comment in `TotemConversionService`
pointed at a `GoblinTotemStackPatch` that was never written — the safeguard it
described did not exist.)

**2–4. Companions fought when they should not have.** The root cause of all three
reports was one wrong assumption in the original code: it set
`MonsterAI.m_alertRange = 0` to make an ally "passive". **That does nothing.**
Vanilla acquires targets through `BaseAI.FindEnemy` → `CanSenseTarget`, which
reads `m_viewRange`/`m_hearRange`; the single place `m_alertRange` leashes a
target is inside `MonsterAI.UpdateTarget` behind `m_character.IsTamed()`, and a
freed Dvergr is **not** tamed. So chore workers and Standby allies had been
chasing creatures the whole time.

The rule now lives in one method, `DvergrCompanion.AllowsCombatTarget`, enforced
in the two places vanilla splits the decision:

* **Acquisition** — a postfix on `BaseAI.CanSenseTarget`. Hooked there rather
  than on `BaseAI.IsEnemy` deliberately: `IsEnemy` is symmetric and is also read
  by `HaveFriendInRange`, so suppressing it would have made a passive Support
  mage treat nearby greydwarves as **friends** to heal. `CanSenseTarget` is
  one-directional by construction, which also leaves other creatures free to
  attack a passive ally — the route by which it can be provoked.
* **Retention** — a per-frame drop in `Update` (ZDO owner only), because
  `UpdateTarget` keeps a locked target between its own throttled scans. This is
  what actually breaks off a chase mid-stride.

What the rule says: a **Follow** ally may only fight within
`Companions/FollowEngageRange` (default **20 m**, a workbench's build radius) of
its master, measured both ways — it will not set off after something far from the master, and it abandons
a chase the moment the chase has dragged it out of that radius (req 2). A
**chore worker** (req 3) and a **Standby** ally (req 4) acquire nothing at all;
they fight only what has actually hurt them, which is now recorded for creature
attackers too (`CompanionDamagePatch` marks a non-player attacker hostile, with
same-owner friendly fire excluded so a stray cleave cannot set two allies on each
other). Standby also **stops wandering**: `m_randomMoveRange` is zeroed, which
collapses `BaseAI`'s idle wander target onto the ally's own position — restored
whenever it has a live target, since the same field doubles as the circling
radius while charging an attack. A hostile **player** is exempt from the leash;
the leash is about chasing wildlife, not about PvP. Same tick re-asserts the
follow target for a Follow ally that has none, which is why a relogged companion
now walks back to its master (vanilla does not persist `m_follow`).

**5. Companions now travel through InterServerPortal's two extra portal modes**
(`E:\Valheim Modding\InterServerPortal`). Neither goes through the vanilla
teleport: that mod prefixes `TeleportWorld.Teleport` and returns false for any
flagged portal, so `CompanionPortalPatch` never saw them. The two modes are
genuinely different problems:

* **Network** (same world) ends in an ordinary `Player.TeleportTo`, so a postfix
  on `NetworkController.Travel` reuses the existing move-to-the-exit path. A
  prefix applies the same non-teleportable-cargo block a wood portal gives.
* **Inter-server** leaves the world entirely (`Game.Logout` → start scene → join
  another world). A companion is a ZDO in the world being left, so there is
  nothing to move — the only thing that crosses is the character file. So the
  crossing **seals each follower into a Communion Totem** (a prefix on
  `WorldSwitcher.Leave`, the commit point, one line before the saving logout) and
  a poll on the far side summons them back beside the player once the destination
  world is up. A full pack leaves the companion behind rather than dropping the
  totem in a world you are about to leave — the ally is recoverable, an abandoned
  totem is not. On a destination without this mod the player simply keeps the
  totems.

All of it is a **soft dependency**, patched by reflection at startup and silently
skipped when InterServerPortal is absent. The "who travels" test is now a single
`CompanionTeleport.Followers` used by every portal path, so vanilla, network and
inter-server can never disagree about it.

**Build/deploy:** the client-side deploy targets were cut down to the **one** Gale
`HB Test` profile. The raw game-install `BepInEx\plugins\LostScrollsII` folder
and both r2modman profiles are no longer written to (and their stale copies were
deleted) — several builds of the same DLL across profiles made it impossible to
tell which one was under test. The dedicated-server target is unchanged.

---

## The bounty gate never opened — released 0.9.1 (2026-08-25)  ✅ VERIFIED

A player reported finishing Haldor's conversation and getting no bounty pinned on
the map. The mod side was healthy — the feature gate logged `ON`, the board held
three postings — so the break was upstream, in ServerGuide.

**Root cause: a reward on a node dialogue choice was silently discarded.** The
commission grants `set_player_key: LS_BountyStart` from a choice inside
`conversation.nodes:`. Those parse into `NodeChoiceSpec`, which had **no
`Rewards` property at all**, and ServerGuide's loader runs
`IgnoreUnmatchedProperties()` — so the block was thrown away while the file was
still being read, with nothing in the log. Even had it parsed,
`OnNodeChoiceSelected` never called `RewardDispatcher`. Only the *flat*
`conversation.choices:` path (`ChoiceSpec` → `OnChoiceSelected`) ever granted a
choice reward. `git log -S` confirmed the property had been absent since
ServerGuide v0.1.0, so this had never worked — §23g was presumably verified with
the `de_bounty_commission` console tool rather than through Haldor.

The bitter part: `guidance.bounty.yaml`'s own header comment had diagnosed half
of it ("node conversations end through `OnNodeConversationEnd`, which never calls
`RewardDispatcher` … the reward must sit on a ChoiceSpec") and then put the
reward on a *node* choice, which is a different class. Right reasoning, wrong
object.

**Fixed in ServerGuide 0.15.0**, not here: `NodeChoiceSpec.Rewards` plus a
`GrantChoiceRewards` call placed *before* the `goto_node`/`goto` branch, so a
reward works whether the choice ends the conversation or carries on. Rewards are
latched once per open conversation — node trees may legitimately loop, and
without that a loop through a rewarding choice would pay every pass. Recorded as
**invariant 20** in ServerGuide's `CLAUDE.md`: the two conversation shapes must
stay at feature parity. It is invariant 16 (the `label:`/`text:` synonym) with
worse consequences — a missing label is visible the moment you look at the
button, a missing reward is invisible forever.

**Second bug, found while packaging:** every place we named the key was wrong.
ServerGuide's `npc_conversation` trigger moved from hold-`E` to **`Shift + E`** in
its 0.14.0, but `BountyBoardPanel`'s locked teaser, `guidance.bounty.yaml`, the
wiki page and Testing.md all still said hold-`E`. Only the dedicated server's copy
had been hand-corrected, which is how it survived. The board is the *only*
in-game explanation of how to start bounty hunting, so this was the difference
between a discoverable feature and a dead end.

**Why 0.9.1 and not a re-cut of 0.9.0:** 0.9.0 was already published
(2026-08-23 16:05 UTC) and Thunderstore never allows a version to be replaced.
Check the live listing before assuming a built zip can still go up.

Also in this cut: the Quest pack's ServerGuide floor is now a hard
**0.15.0** pin, both package README floors were raised to match, and the LSII
GitHub wiki was synced — it had been stuck at 0.7.0, missing `Bounty-Hunting`
and `Wagers` entirely.

---

## Field sealing + wagered tournaments & duel invites — released 0.9.0 (2026-08-23)  ⬜ UNVERIFIED

Three additions, one of which reaches outside this mod. Full design in
[Wagers.md](Wagers.md) and [Companion-Totems.md](Companion-Totems.md); test plans
in [Testing.md](Testing.md) §24–§26.

**1. Seal a companion in the field with a Dead Raiser.** The Incinerator ritual is
unchanged; this is the portable counterpart. Equip a **Dead Raiser**
(`StaffSkeleton`), hold a **Wisp**, have **Blood Magic 20+**, and hold Block on
your own **Follow-stance** companion. The channel runs **5 s at skill 20 down to
2 s at skill 100** and breaks on the same conditions as the Communion Rite
(release, distance, damage) plus its own: unequip the staff or lose the wisp. It
is a sibling class to `CommunionRite`, not a mode inside it — they share only the
input idiom and the accelerating Wishbone ping, and every fail condition, message
and outcome differs. The totem it produces is built by the same
`TotemConversionService`, so a field-sealed companion is indistinguishable from
an Incinerator-sealed one.

**2. Player-started, staked tournaments.** Anyone can open one for **100 Coins**
or **10 Valcoins**; that fee also pays the host's own entry, so nobody is charged
twice. Fixed **4-player** single-elimination bracket that **begins by itself** the
moment it fills, entrants paying the same fee; champion takes **999 Coins** or
**100 Valcoins**. **One Coin and one Valcoin tournament at a time** — enforced by
turning `TournamentService`'s single state into a *book* of slots (`""` free /
`"coins"` / `"valcoin"`), which an older save file still loads into the free slot.
An unfilled or cancelled bracket refunds **every stake and every totem**.

The interesting design point is **ready-up**. The free admin tournament summons
every companion beside its owner wherever they happen to be standing, which is
wrong for a player-run event — it drops one duelist into the other's terrain. So a
wagered pairing carries `aReady`/`bReady`: the two owners agree a venue however
they like, walk there, and each presses **Ready to Fight**; only then are both
companions summoned *there*, at full health, locked onto each other. That is what
"the players decide the location" means, and it is stated on the panel, the board
and the bracket announcement.

**3. Staked duel invites.** Post a challenge for **100 Coins / 10 Valcoins** plus a
sealed companion; anyone may accept by matching it; the winner takes both stakes.
**One invite per player at a time** (as poster *or* challenger), any number of
players at once. Deliberately its own flat two-sided record rather than a
one-match tournament — no bracket, no rounds, no seeding, and a different
lifetime. It shares the escrow/summon/reseal plumbing via a context tag on
`TournamentCombatant`, and resolves off the **same duel report** the ladder
already receives, so it is an ordinary duel underneath.

**Discord.** Every server-wide beat — tournament opened, player entered, bracket
drawn, duel started, duel won, who's next, champion, end-of-event summary, and the
whole invite lifecycle — posts **straight to the ServerGuide webhook** from the
server via the public `DiscordAnnouncer.AnnounceRaw`. Going direct means **no new
trigger types, no new template variables and no ServerGuide release**: these are
server-wide facts, and routing them through a per-player `type: discord` reward
would have meant inventing a trigger per beat and firing it on an arbitrary
client.

**The Valcoin problem, and what it cost.** This mod has never named a Valcoin
amount — payouts go through a `VC.Q.<id>` key that Valheim Donations prices from
its own config. But there was **no debit path at all**, and one cannot be faked:
the ledger is backend-authoritative and a local `CoinManager` deduction is
reverted the next time any backend response syncs a balance. So a small
server-side API was added **to the donations plugin** (`ValcoinWallet.Charge` /
`Credit`), wrapping endpoints that already existed — `/api/spend` for the debit
(its sku is regex-validated, not catalog-validated, so `eco_ls_tourney_entry`
works as-is) and `/api/admin/grant` for the credit (whose own docstring names
"event prizes, refunds"). Lost Scrolls II calls it **by reflection**
(`ValcoinBridge`), so donations stays optional: absent it, Valcoin wagers are
refused with a clear reason and **Coin wagers keep working**.

The guardrail is preserved where it matters. A duel purse is *exactly* the two
stakes collected, so it is simply moved — nothing is created. Only the tournament
purse exceeds what the entries collect, and that surplus is **minted by the
donations mod**, from its own `valcoin_quests.yaml`; `TournamentValcoinPrize` on
this side is display-only.

**Menu buttons (2026-08-23).** Every full-screen panel was hotkey-only, which is
not discoverable — a player who never reads the config or the wiki had no way to
learn the ranking board existed. The single Bounty Board button on the inventory
screen became a **row of three** (Rankings / Tournaments / Bounty Board) in a new
[`InventoryMenuBar`](../src/Companions/InventoryMenuBar.cs), which replaces
`BountyInventoryButtonPatch` and generalises it: the entries are a table, so the
layout is derived from the count and a fourth panel is one line. The function keys
still work. Config `Interface/MenuBarOffset` moves the row; the old
`Bounty/InventoryButtonOffset` still wins when it has been changed from its
default, so an existing tweak isn't silently lost.

**Requires server setup for Valcoin wagers:** rebuild/deploy the donations plugin,
and add an `ls_tournament_prize` quest to `valcoin_quests.yaml`. Coin wagers need
nothing. New guidance file `guidance.wagers.yaml` (Quest pack). New config section
**`Wagers`**; new `Recruitment` keys for the sealing rite.

---

## Bounty hunting (Phases A–I) — released 0.8.0 (2026-08-24)  ✅ VERIFIED (A–H)

A server-only, triple-gated bounty system: hardened, **auto-hostile** Dvergr posted
on a **Wanted Board**, answerable by sword **or** by the Communion Rite, paying
ServerGuide-authored item bundles and a rank-scaled **chance** at Valcoin. Built and
verified phase by phase — see [Bounty-Hunting.md](Bounty-Hunting.md) for the full
design and [Testing.md](Testing.md) §23–§23h for what was tested.

Highlights, and the decisions behind them:

- **Triple gate + server-only** (`BountyFeatureGate`): BiomeLords + ServerGuide +
  Valheim Donations, all loaded, on a server/host. All source is isolated under
  `src/Bounty/` so the gate is one early-out. Clients are *told* whether the feature
  is live (`BountySync`) because they can't see the server's plugin set.
- **Land-only placement** (`BountyLocationSampler`): read from `WorldGenerator`, which
  is procedural and answers for unloaded zones — a raycast would only work near a
  loaded player. Validated against **three rings out to 80 m**, because a single
  ring passes on any islet wider than twice its radius.
- **Our own difficulty curve** (`BountyTiers`): BiomeLords has no callable scaling API
  and **no minion scaling at all**, so both are ours. Health is scaled explicitly;
  **damage rides on the vanilla star level**, since vanilla already scales creature
  damage per star and patching the attack path would buy nothing.
- **Rewards contain no loot table in C#**: resolution fires `dvergr_bounty_resolved`
  carrying the tier, and `guidance.bounty-rewards.yaml` decides what that's worth —
  the same split the ranking and tournament systems already use.
- **Valcoin is reward-only, by construction**: the chance is rolled in-mod (ServerGuide
  has no numeric rank filter and no random reward), and only on success does a second
  trigger set the `VC.Q.*` key that Valheim Donations reads. **No coin amount ever
  leaves this mod**, satisfying that project's "no selling power" guardrail.
- **Two separate rank levers**: bounty-ladder standing bumps the **reward tier**;
  duel/party rank drives the **Valcoin chance** *and* gates the **Accursed** tier.
  Access is the stronger pull toward duels, which is the point of the feature.
- **Quest gate without a new API** (`BountyQuestGate`): Haldor's `npc_conversation`
  grants a stock `set_player_key`; the mod watches for it, posts the commission, and
  unlocks the board when that bounty resolves — an event the mod already observes
  because it posted the bounty itself. ServerGuide's missing reverse-query API is
  therefore a non-issue rather than a blocker.
- **Serializer discipline**: every new store uses `CompetitiveJson` from day one, never
  `JsonUtility` (which silently drops list fields on this runtime — the bug that once
  made tournaments show zero entrants). Float support was added with
  `InvariantCulture`, since a comma-decimal locale would corrupt every coordinate.

Fixes made in response to live testing: tier stars were invisible below tier 3
(vanilla renders level 1 as *no* stars) → tier badge on the name; season resets
refused a remote admin (gated on *being* the server, not on being an admin) → both
bounty **and** duel-ladder resets moved onto the admin-authenticated RPC; the
inventory button needed several passes to place (settled at top-centre with
`LayoutElement.ignoreLayout`, since the container's layout group was overriding it).

**Packaging:** base + Quest both at **0.8.0**; the Quest pack now bundles **eight**
guidance files. New wiki page `wiki/Bounty-Hunting.md`.

> **ServerGuide 0.14.0 was cut alongside this release**, carrying the `tier:` filter
> and the two `dvergr_bounty_*` triggers (plus two NPC-dialogue fixes of its own).
> Upload order: ServerGuide 0.14.0 → base 0.8.0 → Quest 0.8.0
> ([Publishing.md](Publishing.md)).

## Movable chest/storage UI — released 0.7.0 (2026-08-03)  ⬜ UNVERIFIED

The shared container panel (`InventoryGui.m_container` — vanilla chests **and** the
companion pack) is now **placed by config and moved by dragging**, replacing the old
"measure the player inventory and shift by the extra rows" fix that inferred what
ComfyQuickSlots and friends had done. New
[`ContainerPanelPositioner`](../src/Companions/ContainerPanelPositioner.cs); full
detail in [Ally-Inventory.md](Ally-Inventory.md), test plan in
[Testing.md](Testing.md) §16e2.

- **Drag it anywhere** — grab any empty part of the panel with the left mouse button
  and drop it where you want. A transparent `Image` stretched over the panel as its
  **first child** is the grab surface: every real widget (item slots, Take All, the
  companion name field) is a later sibling, so it stays in front and keeps its own
  clicks. A faint white wash on hover marks grabbable space.
- **Position config** — `Interface/ContainerPanelOffset`, an `"x,y"` pixel offset from
  the game's own position. Dropping the panel writes the new value there, so it
  survives a relog; `"auto"` (the default) means **two inventory rows below**, which
  clears the extra rows slot mods add. `de_container_reset` restores `"auto"`.
- **Off-screen guard** — at least 48 px of the panel always stays on screen, so a wild
  drag or a config from another resolution can't lose it.
- **BiomeLords** ships the same feature for the same panel, so when it's loaded this
  whole system disables itself (no default offset, no drag surface) and leaves the
  panel to it. `Interface/MoveContainerPanel = false` is the manual off-switch. Logged
  once as `[inventory] container-panel positioning: ON/OFF (config=…, BiomeLords=…)`.
- **Removed** — `Companions/AdjustContainerPanel`, the CQS row-counting shift, and the
  BiomeLords check inside `CompanionInventoryGui` (which no longer touches panel
  placement at all).

**Packaging (0.7.0):** version bumped across `csproj` / `Plugin.cs` / the base
`manifest.json`, and the Quest pack's dependency moved to
`TaegukGaming-Lost_Scrolls_II-0.7.0`. The Quest pack itself is **already at 0.7.0** for
the Bog Witch rites above — the two now line up. Both package `CHANGELOG`/`README`
updated, both zips rebuilt. **ServerGuide is untouched** (stays 0.9.0), so upload order
is just base 0.7.0 → Quest 0.7.0. See [Publishing.md](Publishing.md).

## Bog Witch Dvergr rites — Quest pack 0.7.0 (2026-07-27)  ⬜ UNVERIFIED

A new bundled guidance file, `guidance.bogwitch-rite.yaml`, gives players a way to find
and free their first Dvergr companions **without traveling to the Mistlands** — a
two-stage weekly quest chain delivered by the **Bog Witch** trader (from the
`ProfMags-TraderOverhaul` mod). Quest-pack-only content: no base-mod or ServerGuide code
changes, built entirely from ServerGuide's existing `npc_conversation` / `kill` /
`spawn_creature` building blocks. Base mod stays at 0.6.0, ServerGuide stays at 0.9.0.
Full detail in [ServerGuide-Integration.md](ServerGuide-Integration.md) and
[Ally-Recruitment.md](Ally-Recruitment.md); test plan in
[Testing.md](Testing.md) §22.

- **"An Echo in the Mire" → "The Rite of Waking"** (`ls_bogwitch_echo_intro` /
  `ls_bogwitch_echo_rite`) — talk to the Bog Witch, then kill 2 `Draugr_Elite` in the
  Swamp. A wild, untamed `Dverger` (no staff → **Rogue** caste on recruit) spawns beside
  the player via a `spawn_creature` reward, plus 20 Coins. Weekly cooldown.
- **"The Deeper Echo" → "The Rite of the Unseen Hand"** (`ls_bogwitch_mage_intro` /
  `ls_bogwitch_mage_rite`) — unlocks only after the rite above has fired at least once.
  Kill 2 `Wraith` (a **night-only** Swamp spawn) to spawn a wild `DvergerMage`, plus 30
  Coins. Caste (Fire/Ice/Support) is whatever staff vanilla's own spawn logic randomly
  equips it with — ServerGuide's `spawn_creature` reward can't force a specific one, so
  this matches the same odds as meeting one naturally in the Mistlands. Weekly cooldown.
- **Lore tie-in, not a new lore file** — both rites echo the existing Sunken Crypts beat
  in `guidance.lost-scrolls.yaml` (`ls_beat_swamp_crypt`: "a rite... not of binding, but
  of waking") in the Bog Witch's own folksier voice, without duplicating that text.
- **Soft dependency** — "BogWitch" is not a standalone mod; on a server without
  `ProfMags-TraderOverhaul`, the prefab never exists and this content simply never
  triggers. No error, no crash, no effect on any other guidance.

## Communion Rite reworked into a channeled struggle — released 0.6.0 (2026-07-27)  ⬜ UNVERIFIED

Recruiting a Dvergr is no longer an instant keypress — it's a **channeled rite** you
have to hold and see through while the corruption fights back. Base-mod only (gameplay
DLL); no ServerGuide/guidance changes, so ServerGuide stays at 0.9.0 and the Quest
pack's bundled guidance is unchanged. Builds clean; not yet verified in a live session.
Full detail in [Ally-Recruitment.md](Ally-Recruitment.md); new driver
`src/Companions/CommunionRite.cs`.

- **Channeled, not instant** — hold the recruit input on a subdued Dvergr (≤20% HP)
  for `CommunionChannelSeconds` (default 5 s). A few center-message "the corruption
  writhes…" beats play during the channel; success runs the existing
  `CommunionService.TryRecruit` (+ one small spawn poof), and the whole thing is driven
  by a `CommunionRite` MonoBehaviour on the plugin GameObject.
- **Moved from `G` to the Block button** — the rite begins whenever **Block is held**
  (`ZInput.GetButton("Block")`, keyed off *held* not the press down-edge, since you
  often block continuously through the fight) with the crosshair on a subdued Dvergr.
  We only *read* the input, so the shield still raises and **blocking and dodging keep
  working** through the rite (dodge shares the Block button; a `ReleaseGraceSeconds`
  ≈ 0.5 s grace forgives the brief release a roll causes). `G`/`CommunionKey` is now
  **Feed-only** on an existing companion (`HandleCommunionInput` split into
  `HandleFeedInput` + `TryBeginCommune`).
- **It can fail** — release Block past the grace window, stray past
  `CommunionMaxDistance` (4 m), take damage (`CommunionBreakOnDamage`, default on — a
  *blocked* hit deals none), or the target dies/unloads → the shadow reclaims the
  Dvergr (it re-aggravates and must be re-subdued). Also fixed a latent bug where
  `Fail()` read `_player` after `Reset()` had nulled it.
- **Minimal effect: an accelerating Wishbone ping** — the vanilla Wishbone ripple
  (`SE_Finder.m_pingEffectNear`, played directly rather than via the status effect,
  which only pulses near buried treasure) pulses on **both the Dvergr and the player**,
  its interval lerping from 1.0 s → 0.28 s as the rite nears completion — the
  quickening ping is the progress cue (an earlier progress bar and per-lash smoke were
  removed). Font note: the discarded bar used ASCII (`[|||---]`) after the serif font
  showed `▮/▯` as empty boxes.
- **New config** (`Recruitment`): `CommunionChannelSeconds`, `CommunionMaxDistance`,
  `CommunionBreakOnDamage`. `CommunionKey` retained (Feed).

---

## Tournament announcement doc + banner prompt (2026-07-26)  📄 DOCS ONLY

Player-facing / promotional material — no code change.

- **[Tournament-Announcement.md](Tournament-Announcement.md)** — a copy-paste-ready
  announcement block (for Discord / server posts) plus a mechanics summary of the
  tournament system (`F7` panel, totem-escrow entry, auto-summon at full health,
  level gate, the three elimination formats, entrant cap, Hall of Champions, and the
  ServerGuide 0.9.0+ requirement). Pinned to the 0.5.0 feature set; deliberately
  carries **no tournament-name field**. Reflects the system as built-but-unverified.
- **Banner image prompt** — an AI image-generation prompt (delivered in chat, not
  committed) for a wide 16:9 tournament banner: an action scene of two Dvergr parties
  battling, matched to the reference art in `docs/Dvergr_mage*.webp` /
  `docs/Dvergr2star.webp`, no title/text overlay.

---

## Tournament formats + level gate + standings + serializer fix — released 0.5.0 (2026-07-24)  ⬜ MOSTLY UNVERIFIED

A tournament feature batch on top of the escrow system, plus several bug fixes —
one of which (the serializer) was masking the tournament system entirely on the
dedicated server. Builds clean; the tournament-flow items still need a live
multi-player pass ([Testing.md](Testing.md) §21), but the serializer, relog-NRE
and input-block fixes were **verified against the dedicated server** this session.

**Features**
- **Three elimination formats** — `TournamentState.eliminationType` (`single` /
  `double` / `round_robin`). `TournamentService.Start(mode, size, eliminationType)`
  validates it; `ResolveMatch` dispatches to `ResolveSingleMatch` (unchanged),
  `ResolveRoundRobinMatch` (no elimination, champion = most wins; `BuildRoundRobinSchedule`
  circle method) or `ResolveDoubleMatch` (loss-counted, eliminate at 2; pools recomputed
  each round from loss counts). **Simplified:** double-elim grand final is single-game
  (no bracket reset); DE seeding for non-power-of-two counts is approximate; round-robin
  tiebreak is seed-rating then name (no head-to-head).
- **Format selector in the F7 panel** — a **Type** button cycles single→double→round_robin;
  the chosen type is passed to the start command ([TournamentRegistration.cs](../src/Companions/TournamentRegistration.cs)).
- **Level-gated entry** — config `Tournaments/RequiredEntrantLevel` (0 = off). The totem's
  level is threaded from `LockTotem` → `SendTournamentJoinEscrow` → `Join`, which rejects a
  mismatch (1v1 only; party ungated).
- **Name + level on the bracket** — `TournamentEntrant.level` + `TournamentMatch.aLevel/bLevel`,
  shown on `TournamentBoard` and the `F7` status.
- **Live standings** — `TournamentBoard.AppendStandings` renders per-entrant W–L, sorted per
  format, with an eliminated/champion tag. Single-elim losers now increment `losses` too
  (was double-elim only) so the record reads correctly.
- **Entrant cap** — config `Tournaments/MaxEntrants` (default 4); `Start` clamps `size` to it,
  and `size <= 0` now means "use the cap".
- **Full heal on summon** — `TournamentClient.SummonForMatch` calls
  `Character.Heal(GetMaxHealth(), true)` right after the companion is summoned.

**Fixes**
- **JSON serializer replaced ([CompetitiveJson.cs](../src/Ranking/CompetitiveJson.cs)).**
  `UnityEngine.JsonUtility` on Valheim's current Unity 6 runtime **silently drops
  `List<[Serializable] class>` fields** of plugin types — scalars survived, the entrant/match
  and ladder-record lists vanished. Both the per-world save file **and** the server→client
  snapshot push were affected, so the tournament showed **0 entrants on every client forever**
  (and wouldn't survive a restart), and the ladders were at the same risk. Diagnosed with a
  boot-time round-trip self-test on the dedicated server (`jsonHasLists=False`), then fixed
  with a hand-rolled writer/parser and every `JsonUtility` call site swapped over
  ([TournamentService.cs](../src/Ranking/TournamentService.cs),
  [LeaderboardStore.cs](../src/Ranking/LeaderboardStore.cs)). The self-test stays as a
  boot-time regression guard. **Verified: `jsonHasLists=True, entrants=1, matches=1`.**
- **F7 panel relog NRE** — the panel clones `InventoryGui` widgets, but `InventoryGui` is
  rebuilt on every world load while the panel root is `DontDestroyOnLoad`; after a relog the
  clone sources were destroyed objects. `EnsureBuilt` now detects stale sources and rebuilds,
  with defensive null-guards in `AddButton`/`Rebuild`.
- **Attack/movement leaked through the open panel** — the real gate for attack + movement is
  `PlayerController.TakeInput()` (private, distinct from `Player.TakeInput()`), which the old
  patches never touched; a competing mod (Valcoin) was also out-ordering the `ZInput` swallow.
  Fixed with a **postfix on `PlayerController.TakeInput`** → false while the panel is open
  ([TournamentPanelInputPatches.cs](../src/Companions/TournamentPanelInputPatches.cs)) — a
  postfix can't be out-ordered.
- **View Bracket freshness** — `TournamentBoard.Open` now calls `LeaderboardSync.RequestTournament()`
  and waits briefly before rendering, so it can't show a stale snapshot.

**Guidance (Quest pack)** — `guidance.rankings.yaml` + `guidance.tournaments.yaml` rewritten to be
**UI-driven** (F6 ranking board, F7 tournament panel) with **all console commands removed**; new
*"Entering a Tournament"* how-to page. Synced to the ServerGuide `examples/`, the Quest pack, the
dedicated server, and the test profile.

**Packaging** — bumped to **0.5.0** (csproj/Plugin/both manifests + the Quest pack's
`TaegukGaming-Lost_Scrolls_II-0.5.0` dep). **No ServerGuide release needed** — the guidance uses only
existing 0.9.0 triggers/templating. Upload order: base 0.5.0 → Quest 0.5.0 ([Publishing.md](Publishing.md)).

---

## Competitive UI + escrow tournaments + Discord + party naming — released 0.4.0 (2026-07-19)  ⬜ UNVERIFIED

A player-facing UI layer over the whole competitive suite, plus Discord
broadcasting, party naming, and a re-worked (escrow-based) tournament entry.
Builds clean (both projects); **unverified in a live session** — see
[Testing.md](Testing.md) §21.

Packaged as **Lost Scrolls II 0.4.0** + **Lost Scrolls II Quest 0.4.0**, alongside
**ValheimServerGuide 0.8.0** (which carries the reward templating + the two new
`*_rank_first` triggers this release depends on). Upload order: ServerGuide → base →
Quest ([Publishing.md](Publishing.md)).

**Panel input takeover (post-batch fixes).** The `F7` panel now behaves like a real
modal menu; each of these was a separate in-game report:
- `Player.TakeInput` → false (blocks player controls),
- `Player.SetMouseLook` → skipped (**camera** no longer rotates — `TakeInput` alone
  didn't gate mouse-look in this build),
- `ZInput.GetButton` / `GetButtonDown` / `GetButtonUp` → swallowed (**attacks** no
  longer fire when clicking panel buttons — these read straight from `ZInput`),
- `GameCamera.UpdateMouseCapture` → skipped + cursor forced free (clickable buttons).
Our F7/Escape keys and the uGUI buttons use `UnityEngine.Input`, so they still work.

**Admin detection fix.** The panel's admin controls were invisible to a real admin on
a dedicated server because `ZNet.LocalPlayerIsAdminOrHost()` is unreliable on a pure
client. The panel now asks the **server** (`LSII_AdminChk` → authoritative
`ZNet.IsAdmin`) and caches the answer in `LeaderboardSync.LocalIsAdmin`, refreshing
the panel when the reply lands; the listen host short-circuits to true.

**Packaging & docs (0.4.0 cut).**
- **Guidance moved to a per-mod subfolder** — the Quest pack now installs its YAMLs to
  `config/ValheimServerGuide/`**`LostScrollsII/`** instead of the top level, matching the
  convention already used on the live server (`BiomeLords/`, `LivingWorld/`, …). Relies
  on ServerGuide's recursive config loading (0.8.0+); the deployed test copies were
  moved to match. **Upgraders must delete the old flat copies** or the guidance loads
  twice (duplicate ids) — called out in the Quest README/CHANGELOG and Publishing.md.
- **ServerGuide requirement documented** — both package READMEs and CHANGELOGs now state
  plainly that rankings/tournaments need **ValheimServerGuide 0.9.0+** to announce or
  reward anything (the ladders still record and `F6`/`de_ladder` still read without it).
- **READMEs refreshed** — the base README covers the `F6`/`F7` screens, totem entry and
  Discord; the Quest README was two versions stale (listed 2 of 5 guidance files, wrong
  ServerGuide dep, version footer stuck at 0.2.0).
- **New wiki pages** — `wiki/Rankings.md` and `wiki/Tournaments.md` (the competitive
  suite had no player-facing docs at all), linked from `Home.md`, whose "only one panel"
  claim and "works fully without ServerGuide" line were corrected.
- **`docs/ServerGuide-Integration.md`** — documented the eight competitive triggers, their
  subjects/variables, why `*_rank_first` exists instead of a numeric `rank:` filter, and
  the 0.9.0 reward-templating fix.
- **ServerGuide release deliberately not cut here** — that project had concurrent
  unfinished work (a new `RunePanel` display mode) in its tree; it owns its own 0.9.0
  release, which must be published **before** these two packages.

- **Ranking board UI (`F6`)** — a read-only rune-panel view of the duel + party
  ladders any player can open (`RankingBoard`, vanilla `TextViewer`). `de_ladder` /
  `de_party_ladder` still work. Config `Ranking/RankingUiKey` (default `F6`).
- **Tournament panel (`F7`)** — an interactive panel (`TournamentRegistration`, a
  self-contained cloned-widget Canvas) showing status/entrants and the controls:
  **Lock Totem → Enter**, **Withdraw**, **View Bracket**, and admin **Start /
  Begin / Activate Round / Cancel**. Config `Tournaments/TournamentUiKey` (`F7`).
- **Escrow tournaments** — you enter by locking a companion's **Communion Totem**
  into a slot: it leaves your inventory and is held (escrowed) as a serialized
  payload on the entrant. On **Activate Round** the server auto-**summons** each
  pairing's companion(s) beside their owner in duel mode against the **assigned
  opponent** (`DE_DuelVs`-style `DuelOpponentId`/`Owner` + a `MatchesDuelAssignment`
  gate in `CompanionIsEnemyPatch`, so simultaneous matches don't cross-target); on
  resolve they **reseal + despawn** back into escrow (winner keeps leveled-up
  state). Totems are **returned** on reject/withdraw/**admin release**/cancel/
  complete. Console `de_tournament` gains `join` (now escrow, auto-seals the hovered
  companion / nearby Follow allies), `withdraw`, `activate`, `release <name>`.
- **Admin RPC** — a new admin-authenticated `LSII_AdminCmd` (server re-verifies
  `ZNet.IsAdmin`, mirroring ServerGuide's admin RPCs) so admin controls work from a
  remote client, not just the host.
- **Discord announcements** (routed through ServerGuide) — every **duel win**
  (1v1 + party, tournament matches included), every **new #1** on either ladder
  (new dedicated `dvergr_rank_first` / `dvergr_party_rank_first` triggers), and the
  **tournament champion** now post to the server's Discord webhook via `type:
  discord` rewards in `guidance.duels.yaml` / `guidance.rankings.yaml` /
  `guidance.tournaments.yaml`. Requires `DiscordWebhookUrl` set on the server.
- **Reward templating fix (ServerGuide)** — `chat_message`/`discord` reward
  messages now expand the firing entry's full token set (`{companionName}`,
  `{rank}`, `{winSize}`, `{mode}`, …), not just `{player_name}`.
- **Party naming** — `de_party_name <name>` (or the party registration path) names
  your party; stored in the persistent `PartyRecord` and shown on the party ladder,
  the F6 board, and in party announcements (`{partyName}`).

## Companion map pins reworked + death markers (2026-07-13)

- **Live pins** now use the vanilla **player icon**, **tinted** and **scaled down**
  so allies read as little players but stay distinct from your own marker. New
  config `Companions/CompanionPinColor` (hex, default `FFB84D`) and
  `CompanionPinScale` (default `0.7`). The old `MapPinIcon` (0-4) config is removed.
- **Death markers**: when one of your companions dies — with items or not — a
  persistent **skull** pin labelled with the companion's name is dropped on your
  map at the spot it fell (`CompanionMapPins.AddDeathMarker`, hooked in the
  `Character.OnDeath` patch, owner-gated + client-side like the live pins). Config
  `Companions/ShowDeathMarker` (default on).

## BiomeLords container-UI hardening (2026-07-13)

Follow-up to the earlier BiomeLords fix, which was reported still not working.

- The container-panel shift now goes through one `ShouldAdjustContainer()` gate:
  **off** when the user disables it *or* when BiomeLords is present (unchanged
  intent, single code path). BiomeLords is matched by `biomelord` in its plugin id
  (`com.taeguk.BiomeLords`) or name.
- Added a **manual off-switch** — `Companions/AdjustContainerPanel` (default on) —
  as a guaranteed workaround if detection ever misses.
- Added a **one-time diagnostic log** (`[inventory] container-panel shift: ON/OFF …`)
  so the BepInEx log states exactly what LSII decided, to pinpoint any residual
  conflict. (The deployed post-fix DLL already defers to BiomeLords; if the issue
  persists after a clean relaunch, this log confirms whether LSII is even touching
  the panel.)

---

## BiomeLords container-UI compatibility (2026-07-13)

Fixed a conflict where BiomeLords' "move chest UI" setting stopped working while
Lost Scrolls II was enabled.

- Root cause: our companion-pack panel shifts `InventoryGui.m_container` down to
  clear mod-added inventory rows (the CQS/BiomeLords technique) every frame it's
  open. BiomeLords repositions the **same** panel, so the two fought and BiomeLords'
  setting was overridden.
- Fix: `CompanionInventoryGui` now **detects BiomeLords** (scans `Chainloader.PluginInfos`
  for a `biomelord` id/name, cached) and **skips its own container shift entirely**
  when BiomeLords is loaded — the companion pack uses that same panel, so BiomeLords'
  own repositioning already covers it. Without BiomeLords, behavior is unchanged.
- Also: the local **dedicated-server** build now deploys the DLL to
  `…\BepInEx\plugins\TaegukGaming-Lost_Scrolls_II` (was `…\plugins\LostScrollsII`);
  the stale old folder was removed to avoid a double-load.

---

## Tournaments — Phase E (2026-07-09)

Server-authoritative bracket runner for both 1v1 and party, completing the
competitive suite. Builds clean; unverified — see [Tournaments.md](Tournaments.md)
+ [Testing.md](Testing.md) §20.

- `TournamentService` + `TournamentState` (persisted per world, resumed on load):
  phases idle→registration→running→complete; re-seeded single elimination with byes.
- **No remote-driven combat** — the server announces pairings and resolves each
  match from the same `LSII_ReportDuel`/`LSII_ReportParty` report the ladders get
  (`NotifyDuelResult`/`NotifyPartyResult`). Tournament fights are ordinary duels.
- Commands: `de_tournament start|join|begin|bracket|forfeit|cancel` + `de_champions`.
  Admin subcommands host-gated; `join` registers the hovered companion (1v1) or the
  player's party (party), seeded from the ladder rating. State syncs to clients.
- Three ServerGuide triggers (`dvergr_tournament_joined` / `_match` / `_won`) fired
  on the relevant player's client (match/won broadcast with target name embedded)
  so rewards land correctly; `guidance.tournaments.yaml` carries the pairing notice
  + champion prize bundle.
- Hall of Champions archive (`champions.<world>.json`, `de_champions`).
- Deviations for robustness: player-run matches (no teleport/auto-start), arena
  ward + teleport not enforced (convention only), admin subcommands host-only.

---

## Party ranking — Phase D (2026-07-09)

Party bouts now feed a persistent ladder on the same store/sync as the 1v1 ladder.
Builds clean; unverified — see [Party-Duels.md](Party-Duels.md) + [Testing.md](Testing.md) §19.

- New `PartyRecord` keyed by **ownerId** with a `memberSnapshot` (companion
  id/caste/level) — the "owner + companions" record. Team Elo (owner-vs-owner),
  W/L, best team size, season; stored in the per-world JSON `parties` list.
- Report-once-per-match: every surviving winner runs `AwardPartyWin`, but a static
  owner-pair latch dedups the ladder report + ServerGuide event (all winners share
  the owner/authority client). Rosters are accumulated over the bout (`ScanParty`)
  so both teams are known even after the losers are benched.
- Server path `LSII_ReportParty` → `ApplyPartyDuel` → broadcast → top-3 climb sends
  `LSII_PartyRankEvt` → `dvergr_party_rank_changed`; the win fires
  `dvergr_party_duel_won` locally with `winSize`/`opponentOwner`/`mvpCaste`.
- `de_party_ladder` command + party Codex pages (reference, per-win notice, top-3
  milestone) in `guidance.rankings.yaml`.

---

## Party duels — Phase C (2026-07-09)

Team-vs-team companion sparring, built on the verified 1v1 duel spine. Mechanics
only — party bouts don't feed a ladder yet (Phase D). Builds clean; unverified —
see [Party-Duels.md](Party-Duels.md) + [Testing.md](Testing.md) §18.

- New `DE_PartyDuel` ZDO flag + `PartyDuelMode`/`InAnyDuelMode` on `DvergrCompanion`
  (replicated, spawn-cleared, mutually exclusive with 1v1 duel).
- `CompanionIsEnemyPatch` generalized: enemy only to another player's duelist in
  the **same** mode with a different non-zero owner — scales 1v1 and N-vs-M.
- `CompanionDamagePatch` floor branches: 1v1 credits the striker (`ResolveSubdue`),
  party **benches** the member (`ResolvePartySubdue`) reusing the `_duelResolved`
  latch, and the match plays on.
- `TickPartyDuel` (authority-gated): owner leash / forfeit, nearest-enemy targeting,
  and win-by-attrition with **team-size-scaled XP**.
- New `K` party key (`Duels/PartyDuelKey`, `MaxPartySize` = 4) gathers nearby free
  Follow-stance allies into a team and toggles them down; owner-gated + `[K]` hint.

---

## Duel ladder — Phase A + B (2026-07-09)

The 1v1 duel ranking, built end-to-end (foundations + ladder). Both projects build
clean; unverified in a live session — see [Ranking.md](Ranking.md) + [Testing.md](Testing.md) §17.

- **Foundations (Phase A):** a stable `DE_CompanionId` GUID (recruit → ZDO →
  carried through the Communion Totem); `Rating` (Elo, start 1000); a
  server-authoritative `LeaderboardStore` persisting **one JSON file per world**
  next to the save (`UnityEngine.JsonUtility`; added the `JSONSerializeModule`
  reference); a `LeaderboardSync` RPC layer mirroring ServerGuide's `GuidanceSync`;
  and the `dvergr_rank_changed` ServerGuide bridge trigger + templating vars.
- **1v1 ladder (Phase B):** `AwardDuelWin` reports each decided bout to the server,
  which applies Elo (with a per-pair anti-farm cooldown), persists, broadcasts the
  table, and fires `dvergr_rank_changed` on the winner's client when it climbs into
  the top 3. Records key on companion id + owner (W/L, streaks, rating, season).
- **Display/UX:** `de_ladder [caste] [count]` and admin `de_season_reset` commands;
  an optional `#rank` on the companion name tag (`Ranking.ShowRankOnNameTag`); and a
  ServerGuide `guidance.rankings.yaml` Codex page + top-3 milestone reward, deployed
  to the examples, Quest pack, and test profile.
- New config section `Ranking` (`EloKFactor`, `PairCooldownSeconds`, `ShowRankOnNameTag`).

---

## Competitive suite design + duel double-win fix (2026-07-08)

Design docs for a planned four-part competitive suite, plus a prerequisite bug fix
shipped first because the point systems depend on a clean single win event.

- **Bug fix (code):** the duel **double-win**. Capping a subdued duelist's HP at the
  5% floor left it sitting on the threshold; vanilla `Character`/`MonsterAI` regen
  ticked it back above the floor within a frame or two — while the winner was still
  swinging and before `ExitDuelMode` replicated — so a second hit re-fired the win
  (double announcement, and would double-award ranking points). Fixed with an
  in-memory idempotency latch: subdue now routes through the idempotent
  `DvergrCompanion.ResolveSubdue(winner)`, and `CompanionDamagePatch` swallows further
  hits on an already-resolved duelist (`IsDuelResolved`). See [Duel-Arena.md](Duel-Arena.md).
- **Design (docs only):** [Ranking.md](Ranking.md), [Party-Duels.md](Party-Duels.md),
  [Tournaments.md](Tournaments.md) — 1v1 ladder, party duels, party ladder, and a
  tournament bracket runner. All server-authoritative, persisted to a **JSON file next
  to the world save**, synced over a new `LeaderboardSync` RPC, keyed on a new stable
  `DE_CompanionId` GUID + owner, rated by **Elo** with seasons, and delivered/​rewarded
  through ServerGuide (new `dvergr_*` triggers + the existing `RewardDispatcher`).
- **Build order:** Phase A (foundations) + Phase B (1v1 ladder) end-to-end, verify in a
  2-player session, then party (C/D) and tournaments (E).

---

## Companion Handbook: inventory guidance (2026-07-08)

Documentation/packaging only — no code change. Brought the ServerGuide **Companion
Handbook** (`guidance.companions.yaml`) up to date with the
[companion inventory system](Ally-Inventory.md).

- Added a **"Your Companion's Pack"** guidance (`ls_companion_inventory`, fires on
  `dvergr_recruited`, `requires: [ls_companion_commands]`): opening the 8-slot pack
  with `[Y]`, auto-pickup of matching loot, self-feeding (food → temporary max-HP),
  health-mead sipping and poison/fire/frost resist meads, the **150 weight cap**
  (overloaded allies won't fight), death-drop, and totem carry-over.
- Fixed the stale `[Y]` description in **"Commanding Your Companion"** — `Y` now opens
  the pack (which holds the rename field) rather than only renaming.
- Applied to both copies (the sibling ServerGuide `examples/` source and the
  `Lost-Scrolls-II-Quest` Thunderstore pack), and **rebuilt `Lost_Scrolls_II_Quest_0.2.0.zip`
  in place — no version bump**. Updated the Quest pack's own `CHANGELOG.md` 0.2.0 entry
  to note the handbook change.

---

## Companion inventory system (2026-07-05)

Full detail in [Ally-Inventory.md](Ally-Inventory.md); test checklist in
[Testing.md §16](Testing.md). Builds clean; **entirely unverified in a live session.**

- **Per-companion 4×2 (8-slot) inventory** on every recruited Dvergr, built on a
  vanilla `Container` placed on the creature and sharing its `ZNetView` — so ZDO
  persistence, cross-client sync and a **chest-identical UI** (`InventoryGui.Show`:
  player inventory + crafting + the container **total-weight readout**) all come for
  free (`CompanionInventory`).
- **`Y` opens the pack** (the key formerly used only for rename). The panel also
  carries a **rename field** — a clone of the vanilla `TextInput` `GuiInputField`
  reparented into the container panel — so the one key both opens the inventory and
  renames (req 3). `CompanionContainerAccessPatch` suppresses the vanilla
  `[E] Open` hover/interact for companion containers so the bag stays **owner-gated**.
- **Pickup** — a radius sweep pulls in loose `ItemDrop`s of **types the companion
  already carries**; an **empty pack collects nothing**; **combat takes priority**
  (no gathering while alerted/targeting), and pickup is suspended on a chore or duel.
- **Food** — eats **one at a time**; grants a temporary **max-HP buff** equal to the
  food's `m_food` for its burn time, decaying over the back half (delta-based so it
  never compounds); shows a **fed status icon** (the food's own sprite).
- **Meads** — **health mead** sipped below **35%** HP and stopped above **90%**;
  **poison/fire/frost resist meads** drunk on sight (applying the same `StatusEffect`
  the player gets) and re-drunk while any remain. Classified by the consume effect's
  behavior (`CompanionConsumables`), so stamina meads are ignored.
- **Weight cap 150** — over the cap the companion stops picking up and **won't attack**
  (target dropped each tick) but can still **move**, and shows the vanilla
  **Encumbered** status icon. The panel shows the pack's total weight (req 14).
- Behavior runs on the companion's **ZDO-owner client** only (`CompanionInventoryAI`,
  1 Hz), matching the chore system. Status icons are real game sprites injected into
  the `EnemyHud` element (`CompanionStatusIconPatch`).

**Feedback pass (2026-07-05, after first live test):** §16b (pickup) passed; the rest
got fixes.
- **Name field moved** out from under the panel title to the top-left (was overlapping).
- **Encumbrance now actually stops attacks** — enforced **every frame** via
  `DvergrCompanion.ApplyEncumbrance` (drop target + go passive, alert range 0), instead
  of only on the 1 Hz tick where the AI re-acquired between ticks.
- **HP readout added** to the pack panel next to the name (`HP cur / max`, gold while
  fed) so the food max-HP buff is verifiable.
- **Health mead latch** — it now keeps sipping across the whole **35% → 90%** window
  instead of stopping after the first gulp.
- **Resistance is now shown** — active resist effects render as icons above the
  companion **and** in the pack panel (icons + names), so you can confirm which mead's
  resistance is live (`CompanionConsumables.ActiveResistEffects`). The resistance
  itself applies through the shared `SEMan.ModifyDamageMods` path that runs for every
  `Character` in `RPC_Damage`, so it genuinely reduces that damage type.

**Feedback pass 2 (2026-07-05):**
- **Resist status removed from the pack panel** — it now shows only above the
  companion in-world (the panel keeps just the name field + HP readout).
- **Pack drops on death** — a companion that dies spills its whole pack to the ground
  (`CompanionDeathDropPatch`, owner-gated, `Character.OnDeath`). Sealing into a totem
  destroys the creature directly (not via death), so it doesn't drop — those items
  ride the totem instead.
- **Name-field typing suppresses hotkeys** — `Plugin.Update` now bails while the
  injected rename field is focused (`CompanionInventoryGui.IsTyping`), so typed letters
  don't fire stance/feed/chore/etc. bindings. **Follow-up:** that only covered *our*
  keys; the game still reacted to raw binds through `ZInput` (e.g. `InventoryGui.Update`
  closes the container on the `"Use"` bind = E). A `ZInput.GetButtonDown` prefix now
  swallows vanilla button actions while the field is focused (mirroring how chat/console
  suppress input), so typing E no longer closes the panel
  (`CompanionTypingButtonDownPatch`).
- **ComfyQuickSlots compatibility** — with CQS active the player inventory gains a 5th
  (armor/quickslot) row that extended down behind the pack panel. While our pack is
  open we now push the shared container panel (`InventoryGui.m_container`) down by the
  extra rows' height (`(GetHeight() − 4) × elementSpace` + a small clearance), so that
  row stays visible — the same `m_container` shift BiomeLords uses. Restored when our
  panel closes, so vanilla chests are untouched. Driven purely by row count, so it also
  covers other mods that grow the player grid downward.
- **Totems carry the pack** — the sealed companion's inventory is serialized into the
  totem's `m_customData` (`DE_TotemInv`) and restored into the summoned companion
  (`TotemConversionService`), so items survive seal → summon.
- **Wood portal blocks prohibited companion cargo** — if a **Follow** companion that
  would teleport with you carries a non-teleportable item, the **`portal_wood`** refuses
  to send you (even with a clean personal inventory), with a message naming the ally +
  item (`CompanionPortalBlockPatch`). Scoped to `portal_wood`; other portals unaffected.

---

## Repo publish + Thunderstore packaging (2026-07-03)

Full detail and rebuild/upload steps now live in [Publishing.md](Publishing.md).

- **Working directory renamed** `Dvergr Expanded` → `Lost Scrolls II` to finally match
  the mod identity (the earlier rename only touched the GUID/namespace/assembly, not the
  folder). Build artifacts (`bin/`/`obj/`) regenerate under the new path; no source change.
  Because Windows locks the folder while a session holds it open, the actual rename runs
  from the parent via the helper `E:\Valheim Modding\Rename to Lost Scrolls II.ps1`.
- **Published to GitHub** as a new public repository:
  `https://github.com/yesu0725/Lost-Scrolls-II` (created via the GitHub API — no `gh` CLI
  on the machine). `bin/`, `obj/`, and `Thunderstore files/*.zip` are gitignored.
- **Wiki authored** (`wiki/` folder + pushed to the GitHub Wiki backing repo, modeled on
  the sibling Valheim-ServerGuard wiki): a `Home` overview plus one player-facing page per
  feature (recruiting, leveling, commands, chores, totems, duels) and a spoiler-free
  `The-Story`. No lore reveal — the gospel allegory, the mirror thesis, and the
  author-only roadmap stay out of all wiki copy.
- **Two Thunderstore packages** under `Thunderstore files/`:
  - **`Lost Scrolls II`** (base) — `manifest.json` (name `Lost_Scrolls_II`, v0.1.0),
    `icon.png` (256×256, author-supplied), `README.md`, `CHANGELOG.md`, and the built
    `LostScrollsII.dll`. Dependency: BepInEx only; **ServerGuide is optional** here
    (narrative delivery only), not a hard dependency. Gameplay-only edition.
  - **`Lost-Scrolls-II-Quest`** (complete pack) — a **content pack** that does **not**
    bundle the DLL; it ships `icon.png` + the two guidance YAMLs under
    `config/ValheimServerGuide/` (Thunderstore routes `config/` → `BepInEx/config/`, where
    ServerGuide auto-merges them — no manual setup). Hard dependencies:
    `denikson-BepInExPack_Valheim-5.4.2333`, `TaegukGaming-Lost_Scrolls_II-0.1.0` (the base
    mod), and `TaegukGaming-ValheimServerGuide-0.7.1`. The single-player-ready edition.
- **Upload order:** publish the base `Lost Scrolls II` package **first** — Thunderstore
  validates the Quest pack's `TaegukGaming-Lost_Scrolls_II-0.1.0` dependency at publish
  time, so the base must exist on Thunderstore before the Quest upload validates.
- Both upload zips (`Lost_Scrolls_II_0.1.0.zip`, `Lost_Scrolls_II_Quest_0.1.0.zip`) are
  built with all required files at the archive root (gitignored, rebuilt on demand).

## Companion Handbook — in-game gameplay guidance (2026-07-03)

- **New `guidance.companions.yaml`** (own `category: Companions` in the F3 Codex,
  separate from the mythic lore) teaching players how to actually use their
  companions for **chores** and **adventures**, via ServerGuide's existing triggers:
  - **First recruit** → a command-key reference card (stance `E`, rename `Y`, feed/heal
    `G`, duel `J`, chore/recall `H`; default keys).
  - **Per-caste chore tips** on freeing each caste (`dvergr_recruited` + caste filter):
    Fire Mage → smelters/furnaces/kilns/forges; Ice Mage → Eitr Refinery/Spinning
    Wheel; Support Mage → farm/cook/brew/tame; Rogue → haul — each with the chest
    placement and `[H]` assign/recall flow.
  - **Adventure tips**: `ship_sailed` → ship-riding, `portal_used` → portal-follow
    (both gated `requires: [ls_companion_commands]` so they only fire after a recruit),
    `dvergr_level_up` → leveling, and `build` (piece `incinerator`) → the Communion
    Totem sealing/summon tip.
  - All `once` per character, re-readable in the Codex. Deployed to the test profile;
    no id collisions with the lore file. See [Testing.md](Testing.md) §10d.

## Lore rework: biome descent + scripture + veteran-safe triggers (2026-07-03)

- **Reworked the story from 6 "Acts" into a biome-by-biome descent.** As the player
  travels Meadows → Ashlands, reflective beats fire at distinct in-game locations
  (`ls_beat_*`). The through-line is a **mirror held up to the player**: the corrupted,
  toiling Dvergr are humanity — slaves of a path they think they chose, in rebellion
  against the Creator without knowing it, following a purpose that leads nowhere; Damon
  only *showed the world the road it already wanted*. The freed companions doing your
  chores are meant to look like us (the Mistlands "chore mirror" beat makes it
  explicit). Ends hopeless-but-not-sealed: one held-back light in the Ashlands finale.
- **Scripture woven in verbatim, never cited** (no book/chapter/verse) — it reads as
  the world's own ancient voice. Verses chosen per theme (astray/own-way, servant of
  sin, creature-not-Creator, wages of death, bondage of corruption, no hope, light in
  the shadow of death, etc.).
- **`distance` triggers, NOT `location_entered` — the veteran-safe choice.**
  `location_entered` burns a one-shot `loc_<name>` dedup key for *every* location a
  player nears, regardless of guidance, persisted per character — so **players already
  on the server would never see the beats**. `distance` only burns its key when a
  matching guidance is in range, so this fresh config fires for veterans and new
  characters alike. Same proximity-to-named-location behavior; logs `[distance]` names
  at Info level for confirmation.
- **Starting the lore + returning players.** Everything begins at `StartTemple` (spawn
  stones). New characters trigger the opening on spawn. Players already elsewhere on
  the server get `ls_call_to_start` — a raven nudge ~1 min after login that repeats on
  a 30-min cooldown until they reach the stones (`stop_when` the opening beat).
- **Ordering via directional text, not chains.** Because location dedup is one-shot, a
  chain step waiting on an already-passed location would stall — so the beats are
  independent entries, and each one's closing line points to the next landmark/biome,
  walking the arc in order by following the words.
- Per-caste recruit voices kept, reworded to carry *"the truth shall make you free" /
  "free indeed."* The recruit-order guide (`ls_guide_recruit_order`) is unchanged.
  Ashlands location names (`Charred*`) are wildcarded pending `[distance]`-log
  confirmation. Redeployed to the test profile. See [Testing.md](Testing.md) §10b.

## Story: recruit-order guide + Lost Scrolls chains deployed to test profile (2026-07-03)

- **New tracked guide `ls_guide_recruit_order` ("The Order of Communion").** A plainly
  worded, step-tracked ServerGuide quest — separate from the mythic act beats — that
  walks the player through freeing the four castes in the intended order: **Rogue →
  Fire Mage → Ice Mage → Support Mage**. It activates on entering the Mistlands, and
  the HUD tracker's hover tooltip always names the next caste to free and how to
  recognize it. It's a chain, so it advances only on the caste it's currently asking
  for; recruiting out of order is still allowed by the mod (recruitment isn't
  order-gated) — the guide just won't advance, which is what teaches the order. Lives
  in `guidance.lost-scrolls.yaml`.
- **Deployed the Lost Scrolls chains to the test r2modman profile.** Copied
  `guidance.lost-scrolls.yaml` into `…/Mod Test Profile/BepInEx/config/ValheimServerGuide/`.
  No manual merge needed — ServerGuide's loader merges every `*.yaml` in that folder;
  no id collisions with the existing config (`ls_*` vs `track_*`/`control_*`). Unblocks
  Testing.md §10b (act chains) and the new §10c (the recruit-order guide).

## PvP/duel batch: heal-after-duel fix + companion-aware aggression (2026-07-03)

- **Fix: a duel loser could not be healed by mead + feeding is now shared.**
  `Character.SetHealth` writes health only on the client that **owns** the target's
  ZDO (no RPC fallback — confirmed against the decompiled assembly). The cross-client
  duel-subdue path runs in the attacker-side `Character.Damage` prefix and calls
  `ExitDuelMode` → `ZNetView.ClaimOwnership` there, so after a duel the **loser's ZDO
  is owned by the winner's client** and the real owner's feed set health into the
  void. Fix: `MeadFeedingService.TryFeed` now heals via **`Character.Heal`**, which
  routes to the ZDO owner over `RPC_Heal` when the feeder isn't the owner — no
  ownership stealing (claiming the ZDO would strand the companion's follow AI on the
  feeder's client). Same change makes **feeding not owner-gated**: any player may feed
  any companion (top up a friend's ally, or heal a duel loser); stance/rename/chore/
  duel stay owner-only. See [Ally-Commands.md](Ally-Commands.md).
- **Setting 1 — a struck companion also turns on the attacker's companions.** When a
  non-owner player hits your companion, it now retaliates against that player **and**
  marks that player's own companions hostile (`MarkOwnersCompanionsHostile`), so it
  fights the aggressor's allies too, not just the player.
- **Setting 2 — the attacked player's companions defend (PvP).** When one player
  attacks another and **both have PvP on**, the attacked player's companions turn on
  the aggressor **and** the aggressor's companions. The prior behavior (the
  *attacker's* Follow companions joining in on the player their owner attacked) is
  unchanged.
- **Setting 3 — duel wins broadcast to chat.** `AwardDuelWin` now sends a
  `Talker.Type.Shout` so *"&lt;winner&gt; (&lt;owner&gt;) wins the duel against
  &lt;loser&gt;!"* reaches every player's chat, alongside the existing bubble + owner
  center message.
- **Setting 4 — `[J]` duel hint.** The companion hover tooltip now shows
  `[J] Duel a rival companion nearby` when another player's companion is within the
  duel-detect range (`[J] Stand down from duel` while already dueling).
- Enabler: `DvergrCompanion.IsHostileTo` was generalized from `Player` to any
  `Character`, so the timed-hostility dictionary can target another player's companion
  and the `BaseAI.IsEnemy` patch honors it. See [Duel-Arena.md](Duel-Arena.md).
- **Verified:** §12–§15 (totems, ship riding, minimap pins, portal follow) passed
  in-game this batch. The four PvP/duel items above build clean but need a two-player
  session (see [Testing.md](Testing.md) §7c/§9).

## Companions follow through portals; ladder climbing removed (2026-07-02)

- **New: Follow-stance companions teleport through a portal with the owner.** When
  the player steps through a portal, every companion that is **in Follow stance and
  owned by that player** (and loaded nearby) is moved to the destination and keeps
  following. The only requirement is Follow — chore / duel / feral / Guard / Standby
  allies stay put.
- Implemented by `CompanionPortalPatch`, a postfix on `TeleportWorld.Teleport(Player)`
  (which routes through `Player.TeleportTo`, recording the destination on the player).
  Each companion's ZDO position is committed to the destination (after claiming
  ownership) so it survives the zone change — the old instance unloads and the ZDO
  re-instantiates it at the exit, spread in a small ring so they don't stack. Vanilla
  assets only — reuses the portal's own teleport.
- **Removed the ladder-climbing experiment (`LadderClimbAI`).** It didn't work in
  practice (the companion wouldn't reliably detect/route to `wood_stepladder`), so it
  was dropped entirely rather than left half-working. Portals cover the "get the ally
  to where I am" need far more reliably.

## Fix: doubled feed-chore hint on the Hen (2026-07-02)

- The `[H] Set companion to feed` hint (and the "already working here" line) showed
  **twice** when hovering a **Hen**. A Hen routes its hover text through **both**
  `Tameable.GetHoverText` and `Character.GetHoverText` for a single display (the
  Tameable text delegates to the Character), so both feed-hint postfixes appended
  the same line — the earlier "only one is ever displayed" assumption was wrong.
- Fixed with an idempotent `ChoreHint.AppendOnce` that skips a line already present
  in the tooltip, used by both feed-hint postfixes. Other animals (single route) are
  unaffected. See [Ally-Chores.md](Ally-Chores.md).

## Recall a companion from its chore with H (2026-07-02)

- **Press `H` on your own companion to unassign its chore** — no need to find/hover
  the station it's tending. Reports "Ally returns to your side"; on a chore-less
  ally it says so, on another player's ally "answers to another." Handled up-front in
  `Plugin.HandleChoreAssignInput` before the station-detection path.
- The companion's crosshair tooltip now shows a `[H] Recall from chore` hint while
  it's assigned (`CompanionHoverTextPatch`). See [Ally-Chores.md](Ally-Chores.md).

## Companion minimap pins (2026-07-02)

- **New: a live minimap pin at each of your own companions.** See the "Minimap
  pins" section of [Ally-Commands.md](Ally-Commands.md).
- New `CompanionMapPins` (component on the plugin GameObject) keeps one vanilla pin
  per companion the **local player owns**, refreshed ~4×/s and removed on despawn.
- **Private:** pins are client-side, so pinning only companions where
  `DvergrCompanion.OwnerId` matches the local player means **other players never see
  your companions** (and you never see theirs). Unowned/legacy allies aren't pinned.
- **Transient** (`save = false`, nothing written to the map file); rebuilds when the
  `Minimap` is recreated. Pin label follows the companion's display name (renames).
- **Config** (`Companions`): `ShowMapPins` (default on), `MapPinIcon` (0-4, default
  3). Vanilla assets only — reuses `Minimap.AddPin` and stock pin sprites.
- Added a `Splatform.dll` reference (for the `PlatformUserID` author arg of
  `Minimap.AddPin`). Builds clean; **unverified in a live session** —
  see [Testing.md](Testing.md) §14.

## Companions ride ships (2026-07-02)

- **New system: Follow-stance companions get aboard the owner's ship.** See
  [Ship-Riding.md](Ship-Riding.md).
- **Board through the ladder:** the ally trails to the hull via normal land Follow,
  then climbs aboard at a boarding `Ladder`'s deck target (`Ladder.m_targetPos`);
  laderless hulls (rafts) board once alongside. Speaks a one-time *"Aboard…"* line.
- **Free to walk around on deck:** once standing on the ship the component leaves it
  alone — vanilla `MonsterAI` keeps following the owner and the ship's platform
  physics carries it, so it walks the deck and fights normally. **No seats, no
  position-locking, no idle suppression.** (An earlier build snapped companions into
  vanilla `Chair` seats and pinned them there; that was removed per request in favor
  of free movement.)
- **Stay aboard:** if it walks off into the water alongside the boat while the owner
  is still aboard, it's lifted back on — never pinned to a spot.
- Implemented by new `ShipRideAI` (attached alongside `DvergrCompanion` in the
  recruit / admin-spawn / restore paths). **Owner-ZDO gated** like chores;
  **transient** (nothing persisted — re-boards on its own after a relog).
- **Avoid water unless riding:** companions now avoid water by default
  (`BaseAI.m_avoidWater = true`, baseline in `ApplyFreedState`); `ShipRideAI` clears
  it only while the owner is aboard a ship, so an ally will swim out to board but
  won't otherwise wander into the sea.
- **Vanilla assets only:** reuses `Ship`/`Ladder` + the ship's platform physics; no
  new prefabs.
- Builds clean; **unverified in a live session** — see [Testing.md](Testing.md) §13.

## Companion totems — seal & summon (2026-07-02)

- **New system: convert companions into carriable items and summon them back.**
  See [Companion-Totems.md](Companion-Totems.md).
- **Sealing** is an Incinerator ritual: gather **Follow**-stance companions at an
  Obliterator, drop in **Wisps** (1:1), pull the lever. Resolves during the vanilla
  5–7 s lighting animation into named **Communion Totems** (stock `GoblinTotem`
  items, one per Wisp;
  `N = min(wisps, followers)`). Surplus companions/wisps are left untouched. When no
  wisps or no followers are present the incinerator works exactly like vanilla.
- **Summoning**: use the totem from the hotbar/inventory (routes through
  `Humanoid.UseItem`) to respawn the companion **where you're looking**, at its
  sealed **level + XP + name**.
- Per-companion state rides on `ItemDrop.ItemData.m_customData` (persisted); the
  tooltip is extended with the sealed name/caste/level.
- **Presentation:** the totem is renamed **"Communion Totem"** with a purpose-based
  description via a **per-instance `SharedData` clone** (real Fuling Totems keep
  their vanilla name; the clone also carries `m_maxStackSize = 1` so companion
  totems never merge). The override is re-applied on load (`LoadFromZDO` postfixes).
- **VFX** (reused vanilla effect prefabs, no new assets): a soul-dissipation burst
  over each companion as it's sealed, and a spawn burst when one is summoned back.
- **Boot fix:** the two `LoadFromZDO` re-apply patches were initially pointed at
  the nested `ItemDrop.ItemData` type and threw a Harmony "Undefined target method"
  at startup — `Save/LoadFromZDO` are static on the **outer `ItemDrop`** class
  (they take an `ItemData` parameter). Retargeted to `typeof(ItemDrop)`; boots clean.
- Builds clean; **unverified in a live session** — see [Testing.md](Testing.md) §12.
  MP path (cross-client incinerator ownership) needs a two-client pass.

## Finale left deliberately open (2026-07-02)

- **Open item #4 addressed — by keeping it open.** The Act 6 epilogue is confirmed
  to stay deliberately ambiguous: it reads as *may or may not* resolve into a final
  confrontation, promising no named villain and no specific boss — consistent with
  [Lore.md](Lore.md)'s "do not pre-commit to a final boss." The in-game line is
  unchanged; author notes in [Lore.md](Lore.md) and [Quest-Script.md](Quest-Script.md)
  now lock in the "poised between closure and a coming threat" requirement.
- A finale is **deferred to a future major update**; its plan is kept in an
  author-only note and intentionally out of all in-game and public-facing text so it
  can't spoil what's planned.

## Caste recruit "voices" finalized (2026-07-02)

- **Open item #3 closed.** The four always-on per-caste recruit lines in
  `guidance.lost-scrolls.yaml` were promoted from DRAFT placeholder to final text,
  grounded in the caste restoration identities in [Lore.md](Lore.md):
  - Rogue — *"Wariness outlives the shadow. What once ruled by fear now guards by choice — at your side, and watchful still."*
  - Fire — *"The fire answers gently now — a forge remembered, not a pyre. What the shadow made wild, the rite made warm."*
  - Ice — *"The cold is a ward again, not a weapon — it keeps what it would once have killed."*
  - Support — *"A thousand winters it gave the rite away and kept none. Strange mercy, to receive it back."*
- **Double-fire resolved (approach c):** the always-on voices coexist with the
  act-gated story beats, differentiated by channel — story beats speak in the
  world's voice (`intro`/`rune`), voices in the raven's (`raven`). To keep the two
  distinct, the Act 4 Fire/Ice beats were moved from `raven` to `rune`. Out-of-order
  recruits still get a voice; the act beats still fire once, in order.
- Text remains freely editable; still unverified in a live session (open item #1).

## Quest-chain placeholder identifiers resolved (2026-07-02)

- **Open item #2 closed.** The `TODO_` item/location placeholders in
  `guidance.lost-scrolls.yaml` are now real vanilla ids, confirmed against
  Valheim's asset tree (`E:\Valheim Modding\ValheimTemplate`):
  - Act 1 scroll fragments → `SurtlingCore` (Burial Chambers) and `WitheredBone`
    (Sunken Crypts), keyed off first pickup in the intended dungeons.
  - Act 2 Sword of Truth → `SwordMistwalker` (the fog-dispelling Mistlands sword).
  - Act 3 "first corrupted Dvergr" beat → `location_entered` on
    `Mistlands_DvergrTownEntrance*` — **changed from a `kill` trigger** to fit the
    free-don't-kill theme (fires on nearing a Dvergr camp, not on killing one).
  - Act 4.3 stronghold → `location_entered` on `Mistlands_DvergrBossEntrance1` —
    **changed from the unsupported `location` type**, which matched no dispatcher
    case and would have silently never fired.
- Doc caveats updated in [Quest-Script.md](Quest-Script.md),
  [ServerGuide-Integration.md](ServerGuide-Integration.md), and
  [Development-Phases.md](Development-Phases.md). Chains still unverified in a live
  session (open item #1, deliberately skipped for now).

## Duel mode rework + butcher-knife betrayal (2026-07-02)

- **Duels are now a "mode," not a scripted 1v1.** `DuelController` and its
  `Character.ApplyDamage` prefix are **removed**. The owner toggles duel mode on
  their own companion (`J`); it then fights **only** other players' duel-mode
  companions and ignores everyone else, driven by vanilla `MonsterAI` through a
  rewritten `BaseAI.IsEnemy` patch. Specifics:
  - **req 1** owner-only entry; **req 2** ignores/immune to players & creatures
    while dueling (only rival duelists); **req 3** auto-stands-down (with a
    notification) when no rival remains, plus a wait timeout if none ever appears;
    **req 5** stands down if the owner logs out or leaves ~40m vision range;
    **req 6** players can't damage a duel-mode companion even with PvP on.
  - **req 4** every companion's floating name now shows `(OwnerName)` before the
    `★N` badge; owner name persisted on ZDO `DE_OwnerName`.
  - Duel state is ZDO-backed (`DE_Duel`) so it replicates across clients; cleared
    on spawn so a relog ends any duel. Driven on the ZDO-owning instance.
  - **Non-lethal** + winner +50 XP now ride on the **confirmed** `Character.Damage(HitData)`
    prefix (Cecil-verified), eliminating the old unverifiable-`ApplyDamage` risk.
- **Butcher-knife betrayal (non-duel):** striking a (non-dueling) companion with a
  `KnifeButcher` turns it **feral** — hostile to all players, owner included
  (`DvergrCompanion.GoFeral`). Deliberate release/betrayal, not timed retaliation.
- Builds clean; **unverified in a live session** (duel path needs two players).
  See [Duel-Arena.md](Duel-Arena.md), [Testing.md](Testing.md) §9.

## Verified in-game — full chore suite (2026-07-02)

- Live-tested and **passed**: §8b Farming (plant + harvest, any type, biome-gated
  planting, Cultivator-on-item-stand trigger), §8c Tamed-animal feeding (Chicken/Hen
  tooltip, claim-by-range, one-mage-per-pen), §8d Provisioning (Fermenter / Cooking
  Station / Stone Oven), §8e Hauling. With §8/§8f/§7d already passed, **Phase 4's
  entire caste-gated chore system (Fire/Ice/Support/Rogue) is now verified** — see
  [Development-Phases.md](Development-Phases.md) and [Testing.md](Testing.md).

## Farm via Cultivator-on-stand; feed claim-by-range; feed tooltip on hens

- **Cultivator on an item stand marks a field.** Place a Cultivator on an `ItemStand`
  and it becomes the farm trigger: its hover shows `[H] Set companion to farm this
  field` and `H` assigns a Support Mage to plant + harvest in the radius around the
  stand. A stable field marker; farm-chore restore prefers it. (Hovering a crop still
  works too.) `ItemStand.GetAttachedItem()` returns the item's prefab name — matched
  against `"Cultivator"`.
- **Feed claim is now by RANGE.** Since one mage feeds a whole pen, the "already
  working here" claim now covers **every** tamed creature within an active feeder's
  work radius (`ChoreAI.FeederCovering`), not just the one animal that was hovered —
  so the tooltip shows it on all pen animals and a second mage can't be assigned to a
  pen that's already tended.
- **Feed tooltip now shows on Chicken/Hen.** Some tamed creatures surface hover text
  through `Character` rather than `Tameable`, so the feed hint is now added on both
  (`CharacterFeedChoreHintPatch` + `TameableChoreHintPatch`, sharing `ChoreHint.FeedLine`);
  whichever Hoverable a creature uses gets the hint. Builds clean; **unverified live**
  (Testing.md §8b/§8c).

## Farm planting is biome-gated

- The farm chore now only plants a crop whose plant **allows the current biome**.
  `Plant.m_biome` (a `Heightmap.Biome` flags mask) is AND-ed with the biome at the
  target (`WorldGenerator.instance.GetBiome`) — the same source vanilla uses to
  forbid placing a plant where it can't grow. Checked both when choosing which seed
  to plant and per candidate spot (biome can vary across the radius). A wrong-biome
  seed voices *"These seeds won't grow in this land."* Each successful plant logs
  `[farm] planted '<sapling>' at <pos> (biome <Biome>)` for verification. Verified
  against the assembly (Mono.Cecil); the per-crop biome masks live in the game's
  Unity assets (not the DLL), so the exact allow-lists need the `[farm]` log /
  live play to confirm — see Testing.md §8b.

## Farming chore now plants + harvests; feed/farm assign tooltips

- **Farming is no longer harvest-only.** The Support-Mage farm chore now also
  **plants**: when nothing is ripe it takes any seed from the chest and plants it on
  free cultivated ground, so a field self-sustains (harvest → chest, seeds → back
  into the ground). Works for **any crop type** via a generic seed→sapling map
  (`src/Companions/PlantingCatalog.cs`, scans `ZNetScene` for `Plant`+`Piece`
  prefabs). Spot search: samples the radius, snaps Y with `ZoneSystem.GetGroundHeight`,
  requires `Heightmap.IsCultivated`, and respects the sapling's `m_growRadius`.
- **Assign tooltips on livestock and crops.** A `Tameable.GetHoverText` postfix adds
  `[H] Set companion to feed` on tamed animals, and a `Pickable.GetHoverText` postfix
  adds `[H] Set companion to farm here` on crops sitting on cultivated ground (not on
  wild berries/branches/stone). Both route through the existing claim-aware
  `ChoreHint`.
- **Confirmed (no change):** one Support Mage feeds *multiple* animals — the feed
  chore sweeps all hungry tamed animals within 10 m of the anchor, one per 5 s tick.
  Documented as intended pen-tending in [Ally-Chores.md](docs/Ally-Chores.md).
- All `Plant`/`Piece`/`Heightmap`/`ZoneSystem` APIs verified via Mono.Cecil before
  coding; builds clean; **unverified in a live session** (Testing.md §8b).

## Bug fix — freed allies attacked the player's build pieces

- A recruited Dvergr would attack **player-built structures** (walls, workbenches,
  etc.). Root cause: Dvergr spawn with `MonsterAI.m_attackPlayerObjects = true`,
  which makes their AI seek `StaticTarget` structures; faction-flipping to `Players`
  doesn't clear it. Fix: `ApplyFreedState` now sets `m_attackPlayerObjects = false`
  and clears `m_targetStatic` on the freed ally, so it stops immediately and never
  re-acquires a building. Re-applied on restore, so it holds across relog. Field
  verified via Mono.Cecil; builds clean; **unverified in a live session**
  (Testing.md §2d).

## Dropped recruit-order + corrupted-camps; added "the corruption awakens"

- **Removed** two features from the prior batch at the user's request: the
  **caste recruit-order gate** (`RecruitProgress` / `DE_RecruitProgress`, deleted)
  and **pre-corrupted camps** (`CorruptionZones` + `CorruptedSpawnPatch`, deleted,
  along with the `Corruption.*` config and the approach-warning loop). Recruitment
  is no longer order-gated; the Rogue→Fire→Ice→Support sequence survives only as the
  **narrative** arc of the ServerGuide story chains.
- **Added — "the corruption awakens"** (`src/Companions/CorruptionAwakensPatch.cs`):
  when an unrecruited Dvergr first becomes aggravated, a short center-screen line
  explains *why* it turns hostile — the corruption sleeping within it has been
  roused. A `BaseAI.SetAggravated` prefix, guarded to a genuine `false→true`
  transition, a real unfreed Dvergr, local-player proximity (≤40 m), and a ~6 s
  throttle so a whole camp waking shows one line. This is the diegetic form of the
  "corruption within" idea and better fits the allegory than seeded camps. See
  [Lore.md](Lore.md) and [Ally-Recruitment.md](Ally-Recruitment.md).
- Builds clean; **unverified in a live session** (Testing.md §1b).

## Bug fix — ally attacked its owner when the owner hit a wild Dvergr

- With a freed companion near an unrecruited Dvergr, attacking the wild one made
  the companion turn on the **owner**. Root cause (decompiled `BaseAI`): a hit
  Dvergr calls `AggravateAllInArea`, which re-aggravates every nearby AI whose
  `m_aggravatable` prefab flag is set — still `true` on our recruited Dvergr — and
  an aggravated neutral Dvergr goes hostile to players. Fix: `ApplyFreedState` now
  clears **`m_aggravatable`** on the freed ally (after the existing
  `SetAggravated(false)`, since that call no-ops once the flag is false), removing
  it from the area-aggravation sweep for good; re-applied on restore so it survives
  relog. Verified against the assembly via Mono.Cecil; builds clean;
  **unverified in a live session**. See [Ally-Recruitment.md](Ally-Recruitment.md)
  and Testing.md §2c.

## Lore finalized as gospel allegory; caste recruit order; corrupted camps

- **Lore rewrite** ([Lore.md](Lore.md)): the story is now an intentional, never-named
  **allegory of the gospel**. Corruption = the world's rebellion/sin; **Damon =
  the adversary** who reigns over a fallen world (never a rematch); the **Sword of
  Truth = the Word**, recovered not invented; **Communion = grace** (free the
  fallen, don't just kill). The **Altar of Communion is dropped from lore** —
  gameplay reward only. Nothing is named directly in-game; the allegory lives in
  the story's structure.
- **Caste recruit order enforced mod-wide**: **Rogue → Fire → Ice → Support**. You
  can't commune a caste until the prior one is freed. Per-player progress on the
  player ZDO (`DE_RecruitProgress`), persists across relog; block message + a
  locked hover line (*"Free a &lt;caste&gt; before this one"*). `DetectCaste` gained a
  quiet overload so the hover path doesn't spam the log. See
  [Ally-Recruitment.md](Ally-Recruitment.md) → "Caste recruit order".
- **Corrupted camps**: a deterministic fraction (`Corruption.Chance`, default 0.4)
  of Dvergr camps are corrupted — their Dvergr are **hostile on sight** (vanilla
  Dvergr are neutral until hit) with an approach warning. Camp corruption is a hash
  of the location's seed-deterministic position, so it's stable per world and
  **works on pregenerated worlds** (Valheim regenerates location instances from the
  seed on load). New: `src/World/CorruptionZones.cs`, `CorruptedSpawnPatch.cs`
  (second `MonsterAI.Start` postfix, skips freed allies). All `ZoneSystem`/
  location API names were verified against the publicized assembly via Mono.Cecil
  before coding. See [Ally-Recruitment.md](Ally-Recruitment.md) → "Corrupted camps".
- Builds clean; **unverified in a live session**.

## Bug fixes — cooking on the Stone Oven

- **Stone Oven cooking-chore `IsFireLit` NRE.** Assigning a cooking chore to a
  **Stone Oven** spammed `CookingStation.IsFireLit` `NullReferenceException`s.
  `IsFireLit` (a private method) walks `m_fireCheckPoints`; the oven is its own
  heat source (`m_requireFire = false`) so vanilla never calls it and leaves
  those points unconfigured. Fix: gate the fire check behind `m_requireFire`,
  exactly like vanilla — switch-less heat stations are treated as always lit.
  See [Ally-Chores.md](Ally-Chores.md) → Cooking Station.
- **Stone Oven food burned (never collected).** The oven cooked food but the
  companion never pulled it. `CookingStation.Interact()` early-outs on stations
  that have an "add food" switch (the oven's door), so it was a no-op there. Fix:
  call **`OnInteract()`** directly (the real worker that fires
  `RPC_RemoveDoneItem`), which collects on every station type. Finished food
  spawns by the oven (vanilla behavior); pair with a haul Rogue to stow it.
  See [Ally-Chores.md](Ally-Chores.md) → Cooking Station ("Why `OnInteract`").

## Companion naming + hover tooltip; stance key moved to `E`

- **Stance key `F` → `E`** (`StanceCycleKey`). `E` is vanilla "Use"; a Dvergr has
  no interaction, so hovering one and pressing `E` only cycles its stance.
- **Rename companions** (`RenameKey`, default `Y`). Opens the vanilla text box
  (`TextInput`/`TextReceiver`); the name is stored on the companion ZDO
  (`DE_Name`) and **persists** across relog. It shows in the floating name +
  `★N` badge, the crosshair tooltip, and the **chore claim tooltip** on
  stations/smelters. Owner-only.
- **Crosshair hover tooltip** on owned companions: current **Stance** plus
  `[E] Cycle stance` and `[Y] Rename` (a `Character.GetHoverText` postfix).
- **Input guard:** all mod hotkeys are suppressed while a text field / chat /
  console has focus, so typing a name doesn't fire stance/feed/chore actions.
- See [Ally-Commands.md](Ally-Commands.md) → Stance / Hover tooltip / Rename.

## Chore assignment now persists across relog / zone reload

- The chore (kind + the target's **world position**) is written to the companion
  ZDO and re-resolved by proximity on spawn (`CommunionService.RestoreCompanion`
  re-adds `ChoreAI`). Position — **not** the target's `ZDOID` — is used on
  purpose: ZDOIDs go through the connection/remap system and don't reliably
  survive a save/load (an earlier ZDOID attempt silently failed to restore). A
  `Vector3` round-trips cleanly and stations don't move.
- Work is gated to the companion's **ZDO owner**, so it runs once and keeps going
  when the assigning player logs out (ownership migrates to whoever still has the
  zone loaded). **Engine limit:** fully-unloaded zones don't simulate — it pauses
  and resumes on reload. Stale records clear after ~60 s.
- See [Ally-Chores.md](Ally-Chores.md) → "Persists across relog".

## Chore claim registry + "already working" tooltip (chore menu discarded)

- The in-world **chore-selection menu was discarded** in favor of a lighter
  approach: `ChoreAI` keeps a static **claim registry** (target → worker). The
  station's hover tooltip shows *"&lt;name&gt; is already working here."* when
  claimed, and a second companion can't be assigned to a claimed station.
  Pressing the chore key on a station your own ally works **toggles it off**; the
  companion search skips allies already busy. See [Ally-Chores.md](Ally-Chores.md).

## Haul choreography — tried, then reverted

- A walk-to-item + pickup-VFX haul was implemented, then **reverted at the user's
  request**. Haul is back to a **radius-sweep**: the Rogue holds its post by the
  chest and pulls items in range straight in (lid opens on deposit). The
  walk-to-item version and its pickup VFX were removed. See
  [Ally-Chores.md](Ally-Chores.md) → Hauling.

## Capability lines replace vanilla Dvergr chatter

- On recruit/restore the vanilla `NpcTalk` chatter is **disabled**, replaced by
  `DvergrCompanion.AnnounceCapability()` — a stance + caste "what I can do" line
  spoken on recruit and on every stance change. See
  [Ally-Commands.md](Ally-Commands.md) → "Voiced identity".

## Earlier mechanics passes (summary)

These predate the batches above and are documented in full in their own files:

- **Recruitment / restore:** Communion Rite, caste detection by equipped staff
  (incl. sheathed slots), relog-restore from the `DE_Recruited` ZDO flag, the
  `m_aggravated`-flag fix for "freed Dvergr still attacks." See
  [Ally-Recruitment.md](Ally-Recruitment.md).
- **Leveling:** level cap 10, rising XP curve, biome-/HP-scaled kill XP,
  player-kill XP, custom `★N` badge. See [Ally-Leveling.md](Ally-Leveling.md).
- **Chores (caste-gated):** Fire→smelting, Ice→refining, Support→provisioning/
  farm/husbandry, Rogue→haul; item-specific voiced blockers, fuel-feeding,
  vertical reach, vanilla add-VFX, passivity while working. See
  [Ally-Chores.md](Ally-Chores.md).
- **Commands / ownership:** mead-based feed/heal, per-player ownership + selective
  threat (owner-only commands, Guard treats others as threats, retaliation),
  reduced jump height. See [Ally-Commands.md](Ally-Commands.md).
- **Feed fix:** meads heal via their consume **status effect** (`SE_Stats`), not
  `m_food`. See [Ally-Commands.md](Ally-Commands.md).
- **Duels:** non-lethal sparring via a `Character.ApplyDamage` prefix. See
  [Duel-Arena.md](Duel-Arena.md).
- **Admin:** `de_spawn <rogue|fire|ice|support> [level]` console command.
- **Project rename:** "Dvergr Expanded" → "Lost Scrolls II" (GUID
  `com.lostscrollsii`, namespace `LostScrollsII`).
