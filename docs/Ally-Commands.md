# Ally Commands — Feed, Stance, Rename, Hover, Map Pins & Travel

Feature adds (post-Phase 6): companion-care/control mechanics that didn't fit neatly into Recruitment, Leveling, or Chores — **feed/heal**, **stance**, **rename**, the **crosshair hover tooltip**, the **capability voice lines**, **minimap pins**, and **travelling with the owner** (ships + portals). Most are hover + hotkey interactions, consistent with every other mechanic in this mod (no new UI assets). Default keys: feed `G`, stance `E`, rename `Y` (all configurable). Ship riding has its own doc — see [Ship-Riding.md](Ship-Riding.md).

## Feed / Heal (revised — now mead-based, not generic food)

- **Key**: `CommunionKey` (default `G`) — **not** a separate key. Originally this had its own `FeedKey` (`F`), but since the Communion key has no further use once a Dvergr is already recruited, the same key now does double duty: subdued+unrecruited → Communion, already-recruited → Feed. See [Ally-Recruitment.md](Ally-Recruitment.md).
- **What it consumes**: specifically a **health mead potion**, not any food item. Detected by *behavior*, not by name: any `Consumable` whose `m_consumeStatusEffect` is an `SE_Stats` that restores health (`m_healthUpFront + m_healthOverTime > 0`). See `MeadFeedingService.HealthMeadRestoreAmount`.
- **Heal amount — read live from the potion's own data, not hardcoded**: the potion's real heal total (`m_healthUpFront + m_healthOverTime` off its consume status effect) is read at the moment of feeding, expressed as a fraction of the *player's* max health, then that same fraction is applied to the *companion's* max health. This keeps the heal proportionate across wildly different companion health pools at different levels, while reflecting each potion tier's actual real value rather than a guessed flat number.
- **Feedback VFX**: feeding plays the potion's *own* consume effect (`m_consumeStatusEffect.m_startEffects`) on the companion — the same healing burst you see when you drink the mead yourself — parented to the Dvergr. Vanilla-assets-only: it reuses the potion's existing effect list, no authored VFX.
- **NOT owner-gated — anyone can feed anyone's companion.** Unlike stance/rename/chore/duel (which stay owner-only), the heal is deliberately shared: any player may offer a mead to any recruited Dvergr — a friend can top up your ally, or heal a duel loser. The success line reads *"Your ally drinks deep."* for your own, or *"&lt;name&gt; drinks deep."* for someone else's.
- **Cross-client heal via `Character.Heal`, not `SetHealth`.** `Character.SetHealth` writes health only on the ZDO owner (no RPC fallback — confirmed against the decompiled assembly), so healing someone else's companion — or a duel loser whose ZDO the winner's client claimed during the subdue — silently did nothing. `MeadFeedingService.TryFeed` now calls `Character.Heal(amount, true)`, which **routes to the ZDO owner over `RPC_Heal`** when the feeder isn't the owner. It does **not** claim ownership (stealing the ZDO would strand the companion's follow AI on the feeder's client); `Heal` clamps to max HP and shows the heal number.
- **Implementation**: `MeadFeedingService.TryFeed`, called from `Plugin.HandleCommunionInput`.

### Bug fix: feeding never worked (wrong heal source)

**Root cause (verified against the real assembly metadata via reflection, not guessed):** healing meads do **not** use `m_food`. That field is only for *food* items (the HP/stamina/regen you gain from eating). A mead's `m_food` is `0`; its heal is delivered by `m_consumeStatusEffect`, an `SE_Stats` whose `m_healthUpFront`/`m_healthOverTime` hold the actual restore amount. The first implementation filtered on `m_food > 0` (so it rejected every real mead) and computed the heal as `m_food / maxHealth` (so it would have healed `0` even if one slipped through). That is why the mead potions did nothing.

The fix stops matching prefab names altogether and identifies a healing mead by what it does — a consumable whose consume effect is an `SE_Stats` that restores health — then heals proportionally from that effect's real values. This works for every mead tier and any modded healing mead. The `SE_Stats` field names (`m_healthUpFront`, `m_healthOverTime`) and `SharedData.m_consumeStatusEffect` were all confirmed against `assembly_valheim.dll`'s actual metadata.

If feeding still doesn't detect a potion you're holding, `MeadFeedingService.TryFeed` logs every Consumable in your inventory with its real prefab name, `m_food`, and consume-effect type to the BepInEx log on a failed attempt — check that log rather than guessing.

## Stance: Follow / Guard / Standby

- **Key**: `StanceCycleKey` (default `E`), configurable. *(`E` is vanilla "Use"; a Dvergr has no interaction, so hovering one and pressing `E` only cycles stance.)*
- **Flow**: press while hovering your companion to cycle Follow → Guard → Standby → Follow.
- **Follow**: `MonsterAI.SetFollowTarget(player)`. Attacks monsters, and players only as governed by the threat rules below.
- **Guard**: clears the follow target, anchors a `MonsterAI.SetPatrolPoint()`, and multiplies `m_alertRange` (×2.5) so it proactively engages threats near its post.
- **Follow**: follows you and fights **only near you** — see the combat leash below.
- **Standby**: fully passive. It acquires **nothing** — no target, never alerted by a passer-by — and **stands still** (`m_randomMoveRange` is zeroed, which collapses `BaseAI`'s idle wander onto its own position, so there is no idle shuffling). The only exception is retaliation: something that actually hurts it, creature or player, is answered. Holds position.
- *(The earlier "Stay" stance was removed; Standby replaces the need for it.)*

### The combat leash (Follow) and how passivity is actually enforced

A Follow companion fights only within **`Companions/FollowEngageRange`** (default
**20 m** — the same radius a workbench covers, so it reads as "the ground around
me") of its owner, measured on both sides: it will not pick a fight with
something far from you, and it **breaks off a chase** the moment the chase has
dragged it more than that from you — protecting you is the job, not hunting. Guard
has no master to stand beside and is not leashed; duel and party-duel modes own
their own targeting; a hostile **player** is exempt (the leash is about chasing
wildlife, not about PvP). The same per-frame tick re-asserts the follow target
when a Follow ally has none, which is what makes a **relogged** companion walk
back to you — vanilla does not persist `MonsterAI.m_follow`.

Breaking off goes through **`DvergrCompanion.StandDown()`**, which drops the target
but clears the AI's *alerted* flag at most once per stand-down. That matters more
than it looks: `BaseAI.SetAlerted` is edge-triggered and spawns `m_alertedEffects`
— the Dvergr's alert shout — on every false→true flip, while vanilla re-asserts
`true` from `MonsterAI.UpdateAI` and from `OnDamaged` on **every hit taken**.
Clearing it on a repeating tick therefore made an ally that couldn't fight back
shout once per incoming blow. **Never write `SetAlerted` from a tick.**

All of this — the leash, Standby, and chore passivity — runs through one method,
`DvergrCompanion.AllowsCombatTarget`, enforced in the two places vanilla splits
the decision: a postfix on `BaseAI.CanSenseTarget` for **acquisition**, and a
per-frame target drop (ZDO owner only) for **retention**, because
`MonsterAI.UpdateTarget` keeps a locked target between its own throttled scans.

> **Do not go back to `m_alertRange` for this.** The original implementation set
> `m_alertRange = 0` to make an ally passive and it did nothing at all: targets
> are acquired through `m_viewRange`/`m_hearRange`, and the one place
> `m_alertRange` leashes a target is gated on `m_character.IsTamed()` — a freed
> Dvergr is not tamed. `CanSenseTarget` was also chosen over `BaseAI.IsEnemy`
> deliberately: `IsEnemy` is symmetric and is read by `HaveFriendInRange`, so
> suppressing it would have made a passive Support mage start **healing
> greydwarves**. `CanSenseTarget` is one-directional, so other creatures can
> still see and attack a passive ally — which is the route by which it gets
> provoked in the first place.
- **Still a cycle, not a menu.** An in-world stance-selection menu was considered but kept as the lightweight `E`-cycle, consistent with discarding the chore menu (the cycle already reaches all three stances in two presses). Revisit only if cycling proves clumsy in play.

## Hover tooltip (crosshair)

- Looking at **your** companion shows a crosshair tooltip with its current **Stance** and the command hints `[E] Cycle stance` and `[Y] Rename`. Implemented as a `Character.GetHoverText` postfix (`CompanionHoverTextPatch`); `GetHoverText` is declared on `Character` and not overridden by `Humanoid`, so a Dvergr resolves to it. Owner-only — another player's companion shows just its floating name/badge.
- Separate from the floating name above the creature (that's `GetHoverName`, which carries the custom name, an **owner name tag** `(OwnerName)`, and the `★N` level badge — `CompanionNameBadgePatch`). The owner tag identifies whose ally is whose, which matters for cross-player duels; owner name is persisted on ZDO `DE_OwnerName` so it shows even when the owner is offline.

## Rename

- **Key**: `RenameKey` (default `Y`), configurable. Hover **your** companion and press `Y` → the **vanilla text-input box** opens (the same one used for signs and tamed-animal renaming), prefilled with the current name.
- **How it works**: `DvergrCompanion` implements `TextReceiver`; `Plugin.HandleRenameInput` calls `TextInput.instance.RequestText(companion, "Rename companion", 24)`. Confirming calls `SetText` → stores the name on the companion's ZDO (`DE_Name`) and the ally acknowledges it ("I'll answer to … now.").
- **Where the name shows**: the floating name + badge above the creature (`GetHoverName`), the crosshair tooltip, and the **chore claim tooltip** on stations/smelters (*"&lt;name&gt; is already working here."* via `ChoreAI.WorkerName` → `DisplayName`).
- **Persists** across relog/zone reload (ZDO-stored, read in `Awake`). Unnamed companions fall back to the localized creature name ("Dvergr").
- **Owner-only**, like every other command. Input is ignored while a text field (rename box / chat / console) has focus, so letters typed into a name don't fire other hotkeys.

## Voiced identity: capability lines replace vanilla Dvergr chatter

- **Vanilla chatter silenced on recruit.** A wild Dvergr's ambient barks ("intruder!", grumbles, greets/goodbyes, aggravated lines) are driven by the `NpcTalk` component. On the freed state (`CommunionService.ApplyFreedState`, shared by recruit / relog-restore / `de_spawn`) that component is **disabled**, so a companion no longer talks like a hostile camp Dvergr.
- **Replaced with stance + caste "what I can do" lines.** `DvergrCompanion.AnnounceCapability()` shows a vanilla NPC speech bubble (`Chat.SetNpcText`) describing what the ally can do **in its current stance**, e.g. Follow → *"I'll follow and fight at your side. Set me to tend smelters, kilns and forges."*, Guard → *"I'll hold this ground. Or set me to …"*, Standby → *"I'll wait here, quietly. Set me to … when you're ready."* The skill phrase is caste-specific (Fire→smelting, Ice→refineries, Support→farm/cook/brew/beasts, Rogue→haul). Spoken **on recruit** and on **every stance change**, so the line always matches the companion's posture. Vanilla-assets-only (reuses the NPC speech system; no authored audio/text assets).

### Ownership & threat behavior

- **Owner**: the recruiting player is recorded as owner (`DvergrCompanion` ZDO `DE_Owner` = `Player.GetPlayerID()`). Commanding it — chore, stance, rename, duel — is **owner-only**; another player gets "This companion answers to another." **Feeding is the exception: any player may feed any companion** (see the Feed / Heal section). (Companions recruited before this change have no owner and accept anyone, for save compatibility.)
- **Selective hostility to players** is injected via a `BaseAI.IsEnemy` postfix (`CompanionIsEnemyPatch`) keyed off `DvergrCompanion.IsHostileTo`, so vanilla targeting then does the rest — no faction hacks:
  - **Guard**: every non-owner player is a threat.
  - **Follow**: only players the **owner attacked** (timed, 30s), wired from a `Character.Damage` prefix (`CompanionDamagePatch`).
  - **Any stance (incl. Standby/chores)**: whatever **attacks the companion** is retaliated against (timed, 30 s). A player goes through `Retaliate`; a **creature** attacker is marked hostile by `CompanionDamagePatch` (excluding friendly fire from the same owner's other allies, so a stray cleave can't set two companions on each other). That mark is what lets a passive ally fight back at all — it acquires nothing on its own.
  - **Butcher-knife betrayal**: if any player (owner included) strikes a companion with a **butcher knife** (`KnifeButcher`), it goes **feral** — `GoFeral` makes `IsHostileTo` return true for *every* player, permanently (until it dies), not timed. A deliberate release action. Detected in `CompanionDamagePatch` by the attacker's equipped weapon name. (A duel-mode companion is immune to player hits, so this only applies outside duel mode — see [Duel-Arena.md](Duel-Arena.md).)
  - The **owner is never** a threat (unless the companion has gone feral).
- **Busy guard**: stance changes are blocked (with a message) while the companion is chore-assigned ([Ally-Chores.md](Ally-Chores.md)) or actively dueling ([Duel-Arena.md](Duel-Arena.md)), since both of those already drive the companion's `MonsterAI` directly.
- **Persistence**: the stance is written to the companion's ZDO (`DE_Stance`) and re-applied by `DvergrCompanion.Awake`, so an ally left on **Guard** or **Standby** is still holding that post after a relog, a server restart or a zone reload. It used to reset to Follow because the stance lived only in memory and the component is rebuilt from scratch on every spawn (same shape as the old "communed Dvergr reverts to uncommuned" bug — see [Ally-Recruitment.md](Ally-Recruitment.md)). Guard/Standby re-anchor their patrol point at the position the ZDO restored them to, which is the post they were left on; a Follow ally's follow target is *not* persisted by vanilla, so `Update()` re-acquires the master as it already did.
- **Reads as "On chore" while working.** The crosshair tooltip shows `DvergrCompanion.StanceLabel`, which reports **On chore** for an assigned worker instead of whichever stance it held when you assigned it. The stance underneath is inert until the chore ends (stance changes are refused while it works), so showing it only invites the question of why the key does nothing.
- **Implementation**: `DvergrCompanion.SetStance()` (persist + announce) calls `ApplyStanceToAi()` for the AI half — alert range, follow target, patrol anchor. `Awake` calls `ApplyStanceToAi()` alone, so a reload doesn't replay the capability bark or rewrite the ZDO. Cycling is `Plugin.HandleStanceCycleInput`.

## Minimap pins — find your companions

- **A live map pin at each of your own companions.** `CompanionMapPins` (a
  component on the plugin GameObject) maintains one vanilla minimap pin per
  companion the **local player owns**, refreshed to the companion's world position
  ~4×/second.
- **A pin outlives the companion's zone.** Pins used to be keyed on the live
  `DvergrCompanion` and dropped the moment it left `DvergrCompanion.All` — which is
  to say, the moment you walked far enough away for its zone to unload. That is
  exactly backwards: an ally left tending a smelter at home is the one you want to
  find from across the map, and it was the only one guaranteed to have no pin. The
  tracker now keys on the stable **companion id** and remembers where each ally was
  last seen, so **distance never removes a pin**. A pin goes only when the companion
  is really gone: it **died** (the death marker takes over), it was **sealed into a
  totem**, or it stopped being ours. Summoning one back out of its totem restores
  its pin — seeing it again un-forgets it.
  - **Keyed on the stable `DE_CompanionId`, with a ZDOID fallback.** That id
    arrived with the duel ladders, so an ally freed before them carries none until
    `CommunionService.RestoreCompanion` backfills it on the next spawn — and keying
    strictly on it meant those companions got **no pin at all** (only the newest
    recruit showed). The fallback is unique and stable while the world is loaded,
    which is enough for a pin and deliberately not enough to save: a ZDOID doesn't
    survive a reload, so a written one would come back as a ghost pin. Session-only
    keys are excluded from the file, and an entry is retired when the real id
    arrives so nothing shows twice.
  - Remembered positions are written to
    `BepInEx/config/LostScrollsII/pins.<world>.<playerId>.txt`, keyed by world
    **and** player so one character's allies never show on another's map, so they
    survive a relog too. Coordinates use `InvariantCulture` — the rule the
    competitive stores learned the hard way, since a comma-decimal locale would
    write numbers the next session can't read. The file is disposable: losing it
    costs one pin that reappears the next time you see that ally.
- **Private by design.** Map pins are entirely **client-side**, so pinning only
  companions where `DvergrCompanion.OwnerId == the local player` means **another
  player never sees your companions on their map**, and you never see theirs.
  Unowned/legacy allies (no recorded owner) are deliberately **not** pinned.
- **Transient in the map file.** Pins are added with `save = false`, so nothing is
  written to Valheim's own map save; the remembered positions above are ours and
  live in our own file. They rebuild cleanly when the `Minimap` is recreated
  (entering/leaving a world). The pin label follows the companion's display name
  (renames included).
- **Player-icon look (2026-07-13).** Live companion pins use the vanilla **player
  pin sprite**, then tint + shrink its icon element (`StylePin`) so allies read as
  small players but stay distinct from your own marker. `Config` (`Companions`):
  `CompanionPinColor` (hex, default `FFB84D`) and `CompanionPinScale` (default 0.7),
  alongside `ShowMapPins` (default on). *(The old `MapPinIcon` 0-4 setting was
  removed.)*
- Vanilla-assets-only: reuses `Minimap.AddPin` / the stock pin sprites — no custom
  icons.

## Death markers — where an ally fell

- **When one of your companions dies** (whether or not its pack held items), a
  persistent **skull** pin (vanilla `PinType.Death`) labelled with the companion's
  name is dropped on your map at the death position.
- **Owner-only + client-side**, like the live pins: added from the
  `Character.OnDeath` patch on the owner's client (`CompanionMapPins.AddDeathMarker`),
  gated to the local player being the owner, so other players don't see it.
- **Persistent** (`save = true`) — it stays like a tombstone marker until you click
  it away. Config `ShowDeathMarker` (default on).

## Resting at camp mends your allies

Sit by a campfire (or stand under a roof with one lit) and every **Follow**-stance
companion at your side regenerates health, reaching full in
`Companions/RestedHealSeconds` (default **120 s**). Allies must be within
`RestedHealRadius` (default **10 m**, the radius vanilla itself uses when working
out a shelter's comfort). Chore, Guard, Standby, dueling and feral allies are not
mended — this is for the ones camped with you.

- **The signal is the vanilla `Resting` status effect, not `Rested`.** They are
  different things and the distinction is the whole design: `Resting` is the LIVE
  state (`Player.UpdateEnvStatusEffects` adds it while near a fire and either
  sitting or sheltered, and removes it the instant you get up), while `Rested` is
  the lingering buff that state accrues, which survives five-plus minutes of
  travelling. Keying off `Rested` would have healed allies halfway across the map;
  keying off `Resting` makes the healing start and stop exactly with the camp.
- **Runs on the owner's client** (`CompanionRestedHeal`, a component on the plugin
  GameObject, ticking every 2 s to match vanilla's own creature-regen cadence).
  That is deliberate: the rest state is computed locally and isn't reliably
  replicated, so the owner's machine is the only place it can be read honestly.
  Healing from there is safe because `Character.Heal` routes to the companion's ZDO
  owner over `RPC_Heal` and clamps to max HP — the same reason mead feeding was
  moved onto it.
- A **Resting** status icon (the vanilla effect's own sprite) appears above the
  ally's health bar while it mends. Like the map pins it is owner-side, so other
  players don't see it.
- The heal is a fraction of the ally's **own** pool, so a level-10 companion with a
  bigger pool still takes the same time to mend.

## Travelling with the owner — ships & portals

Two ways a **Follow**-stance ally comes along when the owner moves between places.
Both are gated on Follow only (a chore / duel / feral / Guard / Standby ally stays
where it is) and use vanilla mechanics — no new assets.

- **Ships.** Follow companions board the owner's ship through a ladder and then walk
  the deck freely; they avoid water otherwise. Full detail in
  [Ship-Riding.md](Ship-Riding.md).
- **Portals.** When the owner steps through a portal, every companion **in Follow
  stance, owned by that player, and loaded nearby** is teleported to the destination
  and keeps following. Implemented by `CompanionPortalPatch`, a postfix on
  `TeleportWorld.Teleport(Player)` (portals route through `Player.TeleportTo`, which
  records the destination on the player as `m_teleportTargetPos`/`m_teleportTargetRot`).
  - Each companion's **ZDO position is committed** to the destination after
    `ClaimOwnership`, so it survives the zone change: the old instance unloads during
    the portal fade and the ZDO re-instantiates the ally at the exit once that zone
    loads. They're spread in a small ring so they don't stack on the player.
  - **Owner-scoped and client-local:** the patch only acts for the local player, and
    only moves companions whose `DE_Owner` matches — so another player portalling
    never drags your allies, and you only bring your own.
  - Logs `[portal] Brought N companion(s) through the portal…` when it fires.
  - The only requirement is Follow — this replaces the removed `LadderClimbAI`
    ladder-climbing experiment as the reliable "get the ally to where I am" path.

### InterServerPortal's two extra modes

[InterServerPortal](../../InterServerPortal) flags a portal into **Network** mode
(same world, pick a destination from a menu) or **Inter-server** mode (a different
world entirely). **Neither runs the vanilla teleport** — that mod prefixes
`TeleportWorld.Teleport` and returns false for any flagged portal, driving the
crossing itself — so `CompanionPortalPatch` never saw them and allies were left
behind. `PortalTransferPatches.cs` handles both, as a **soft dependency** applied
by reflection at startup and skipped silently when the mod isn't installed.

- **Network mode** ends in an ordinary `Player.TeleportTo`, so a postfix on
  `NetworkController.Travel` reuses the same move-to-the-exit code as a vanilla
  portal. `IsTeleporting()` distinguishes a real crossing from a `Travel` call that
  bailed out (locked portal, refused toll), where `m_teleportTargetPos` would still
  hold a stale destination. A prefix applies the wood-portal cargo rule: a follower
  carrying a non-teleportable item blocks the crossing with the same message.
- **Inter-server mode** cannot teleport anything. It leaves the world entirely
  (`Game.Logout` → start scene → join another world), and a companion is a **ZDO in
  the world being left** — the only thing that crosses is the player's character
  file. So the crossing **seals each follower into a Communion Totem**
  ([Companion-Totems.md](Companion-Totems.md)) via a prefix on
  `WorldSwitcher.Leave` — the commit point, one line before the saving logout — and
  `InterServerArrival` summons them back beside the player once the destination
  world is up. If the pack is full the companion **stays behind** rather than the
  totem being dropped in a world you are about to leave: an ally left in the origin
  world is recoverable, an abandoned totem is not. On a destination that doesn't run
  this mod the player simply keeps the totems.
- All three paths share one definition of who travels — `CompanionTeleport.Followers`
  (this player's own, Follow stance, free of chore/duel/feral, alive, loaded nearby)
  — so they can never disagree about it.

## Needs In-Game Verification

- Whether `SetPatrolPoint()` actually keeps a companion anchored, or whether it still wanders within some radius (untested — name suggests "patrol" which may imply roaming between points, not a hard anchor).
- Whether widening `m_alertRange` for Guard has any side effects beyond detection radius (e.g., interaction with vanilla's own threat-assessment logic) that weren't visible from static analysis of the assembly.
- Whether the behavior-based mead detection (`HealthMeadRestoreAmount`: consumable + `SE_Stats` consume effect with health restore) matches the real potions in-game. Much more robust than the old name filter, but still unconfirmed in a live session.
- Whether the flat per-potion heal feels right on higher-level companions (their max-health pool grows with level, so a fixed potion heal is proportionally smaller — by design, but unverified in play).
- Whether the **minimap pins** track smoothly and clear on despawn, and that in multiplayer each player only ever sees their **own** companions' pins ([Testing.md](Testing.md) §14).
- Whether **portal follow** actually lands the ally at the destination after the zone finishes loading (ZDO position commit vs. zone unload timing), only brings Follow-stance own companions, and behaves in multiplayer ([Testing.md](Testing.md) §15).
- Whether the **combat leash** settles an ally beside its master rather than oscillating at the 10 m boundary, and whether chore/Standby passivity now truly holds ([Testing.md](Testing.md) §29).
- Whether **InterServerPortal** travel works in both modes — and in particular that an inter-server crossing never loses a totem ([Testing.md](Testing.md) §30).
- Whether the **rest heal** reads the camp correctly in practice — that it starts within a second or two of sitting down, stops on standing up, and that 120 s to full feels right rather than trivialising food and mead ([Testing.md](Testing.md) §31).
