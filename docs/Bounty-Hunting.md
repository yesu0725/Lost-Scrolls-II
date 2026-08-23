# Bounty Hunting

A server-exclusive endgame loop: hardened, auto-hostile Dvergr are posted as
**bounties** on a dedicated board. Hunting them down (kill **or** commune) pays
out hard-to-obtain crafted/cooked/smelted items always, and a **chance** at
Valcoin scaled by the player's existing duel/tournament standing. Difficulty
(including minion count) scales off a self-owned tier curve informed by
[BiomeLords](#dependency-1-biomelords-scaling)'s biome tiers. A dedicated
**bounty leaderboard** tracks hunters and feeds back into reward quality. Access
is gated behind a short ServerGuide dialogue quest that also carries the lore
reason these Dvergr can't simply be walked up to.

> **Status: COMPLETE — Phases A–H built and verified in-game (2026-08-20/24),
> Phase I (polish + packaging) done. Released as 0.8.0.**
> This doc is the implementation plan, broken into phases per
> [Development-Phases.md](Development-Phases.md) convention. Build order is
> deliberate: foundations and gating first (so a half-built feature never
> leaks to a server missing a dependency), spawning/scaling next, then
> resolution/rewards, then the persistent/UI/narrative layers.
> Phases A–H **passed in-game** ([Testing.md](Testing.md) §23–§23h). All three
> packages are built: ServerGuide 0.14.0 → base 0.8.0 → Quest 0.8.0
> ([Publishing.md](Publishing.md)).

## Hard Requirement: Server-Only, Triple-Gated

This entire feature is invisible unless **all three** are true:

1. [BiomeLords](#dependency-1-biomelords-scaling) is loaded (`com.taeguk.BiomeLords`),
2. [Valheim ServerGuide](#dependency-2-valheim-serverguide) is loaded (`com.valheimserverguide`),
3. [Valheim Donations](#dependency-3-valheim-donations-valcoin) is loaded (`com.taeguk.valheimdonations`),

**and** the local instance is a dedicated server or listen host
(`ZNet.instance.IsServer()`). A pure client connecting to a qualifying server
still gets the full feature (state is pushed to it); a client on a
non-qualifying server, or playing locally/singleplayer, does not — see
[Non-Qualifying Client Experience](#non-qualifying-client-experience) for what
they see instead of nothing. All bounty-hunting source lives isolated under
`src/Bounty/` so the gate is a single early-out, not scattered checks.

## Lore Hook

Lost Scrolls II's mercy-vs-defeat theme ([Lore.md](Lore.md)) already treats
every Dvergr as reachable by the Communion Rite once subdued. Bounty targets
introduce an exception that still respects that theme rather than breaking it:

> Some Dvergr have hardened past the point where the corruption merely holds
> them — they've turned it into a weapon, hunting travelers on the old orders
> that never stopped. They will not be found waiting to be subdued; they hunt
> first. A **Wanted Board** (delivered through a vanilla Trader's dialogue,
> not a new NPC) puts names to them, at Damon's-era survivors' request: bring
> them down by the sword or by the Rite — either way the land is safer, and
> either way is a choice the Rite has always left open.

This keeps the "even the corrupted may be freed" idea intact (req. 1 —
communing a bounty target works exactly like any other subdue-then-Rite) while
explaining *why* these specific Dvergr are aggressive on sight rather than
neutral-until-provoked like every other Dvergr in the mod. A short addition to
[Lore.md](Lore.md) itself is a Phase F task, not required to ship code.

## Dependency Findings (grounding this plan)

### Dependency 1: BiomeLords (scaling)

`E:\Valheim Modding\ValheimBiomeLords` has a 7-tier HP/damage curve
(`Util/TierTable.cs`, `Util/LordBaseStats.cs`) keyed to a per-Lord biome (roughly
Meadows=1 … Ashlands=7), but **no minion/pack-count scaling and no public API**
— the scaling methods are unversioned internal statics, and the only supported
cross-mod pattern in that repo is a soft-dependency +
`Chainloader.PluginInfos.ContainsKey(...)` presence probe (used for
ComfyQuickSlots). **Decision: reimplement our own tier curve and our own
minion-count formula**, informed by BiomeLords' biome-tier numbers for flavor
consistency, and detect BiomeLords' presence only as part of the triple-gate
(never call into its assembly for scaling math).

### Dependency 2: Valheim ServerGuide (quest + delivery)

`E:\Valheim Modding\Valheim ServerGuide` (GUID `com.valheimserverguide`, v0.10.0)
has everything this feature's narrative side needs:

- **`npc_conversation`** trigger (`src/Triggers/NpcConversationTrigger.cs`) —
  holding `E` on a vanilla `Trader` opens a real multi-node dialogue tree
  (`NpcConversationPanel`), distinct from proximity/raven delivery. This is
  what req. 8's "NPC dialogues" maps onto.
- **Chains + gating** — YAML `steps:` for sequential quest steps,
  `requires:`/`stop_when:` for prerequisites, all persisted per-player via
  `ChainState`/`SeenTracker`.
- **No reverse-query API** exists (`ServerGuideBridge` only pushes events in).
  **Decision (avoids needing one):** the quest's final step is "resolve the
  tutorial bounty," an event Lost Scrolls II already observes directly because
  it's the one spawning and tracking that bounty. So the actual *gate* on the
  general Wanted Board is an LSII-owned player flag (`DE_BountyUnlocked`) set
  the moment LSII's own resolution code sees the tutorial bounty close out —
  ServerGuide is used purely to *narrate* the lead-up, never queried for
  completion state. This matches the existing one-directional bridge shape
  exactly (see [ServerGuide-Integration.md](ServerGuide-Integration.md)).
- **All rewards flow through `RewardDispatcher`** — per existing convention
  (see [Ranking.md](Ranking.md), [Tournaments.md](Tournaments.md)), Lost
  Scrolls II fires trigger events with context vars; it never hard-codes a
  loot table in C#. Bounty rewards follow the same rule (see
  [Phase D](#phase-d--resolution--rewards-serverguide-authored)).

### Dependency 3: Valheim Donations (Valcoin)

`E:\Valheim Modding\valheim-donations` (GUID `com.taeguk.valheimdonations`)
treats its own plugin as a **cache** of a balance owned by an external backend
— there is **no direct grant/deduct method safe for a sibling mod to call**
(calling `CoinManager` directly would drift from the ledger of record). The
**sanctioned integration path**, already documented there specifically for
Lost Scrolls II (`docs/ecosystem/lost-scrolls-ii.md`), is a **quest-key
bridge**: a ServerGuide `set_player_key` reward sets `VC.Q.<questId>` on the
player; that mod's `QuestWatcher`/`QuestFlow` picks it up, looks the payout up
server-side in its own `valcoin_quests.yaml` (never trusting a client-supplied
amount), and credits the backend. Its docs **explicitly prohibit** selling
gameplay power (XP, levels, companions, duel wins) for Valcoin — currency
payouts only. **Decision, per this doc's brief: Valcoin is reward-only in
bounty hunting** — nothing in this feature ever lets Valcoin buy back into
spawn odds, tiers, or rewards, satisfying that guardrail by construction.

## Reward Model (how the three "affects rewards" requirements compose)

Three separate levers, none overlapping, all data/config-driven so no loot
table lives in C#:

| Lever | Driven by | Effect |
|---|---|---|
| Item rewards | The bounty's **tier** (biome + roll) | Which `guidance.bounty-rewards.yaml` entry matches — higher tier = better vanilla crafted/cooked/smelted items (req. 3) |
| Bonus item tier | The player's **bounty leaderboard** standing (req. 5) | LSII bumps the *effective* tier by +1 (config-capped) for top-ranked hunters before firing the trigger — same YAML, no new reward code |
| Valcoin chance | The player's **duel/tournament rank** (req. 4) | LSII rolls locally against a rank-scaled chance table; only on success does it fire a second trigger whose only reward is the `set_player_key` Valcoin grant |

## Phased Implementation Plan

### Phase A — Foundations & Gating

**Status: built (2026-08-18), builds clean, unverified in a live session.**

Goal: prove the triple-gate and server-only enforcement end-to-end with zero
gameplay yet.

- [x] `Plugin.cs`: added soft dependencies on **`com.taeguk.BiomeLords`** and
  **`com.taeguk.valheimdonations`** (only ServerGuide had one before — the plan
  originally assumed BiomeLords did too; it was detected via `Chainloader`
  without an attribute). Declaring them soft guarantees they're loaded *before*
  us, so the gate's probe can't race their registration, while leaving the mod
  fully functional without either.
- [x] `src/Bounty/BountyFeatureGate.cs` — the single early-out. Separates two
  questions that are easy to conflate: **`IsEnabled`** (am I the authority that
  runs bounties? — config + all three deps + `IsServer()`) and
  **`AvailableToLocalPlayer`** (should this player see a live board? — the real
  gate on the host, the server-pushed flag on a client). Dependency probes are
  cached per session; **BiomeLords detection delegates to the existing
  `ContainerPanelPositioner.BiomeLordsLoaded()`** so the two can never disagree,
  and because its GUID is mixed-case (`com.taeguk.BiomeLords`) a plain
  `ContainsKey` would miss it — every probe is case-insensitive.
- [x] New config section `Bounty` — master `Bounty.Enabled` (an off-switch on
  top of the gate, never a way to force the feature on). Decision logged once at
  `ZNet.Awake`: `[bounty] feature gate: ON/OFF (BiomeLords=… ServerGuide=…
  Donations=… isServer=… config=…)`.
- [x] `src/Bounty/BountySync.cs` (RPC layer, mirrors `LeaderboardSync`) —
  `LSII_BountyActive` (server→client, pushed on `RPC_PeerInfo`) plus
  `LSII_BountyReq` (client→server on `Player.OnSpawned`, so a client that missed
  the join push still converges). The client flag **defaults to `false` and is
  cleared on `ZNet.OnDestroy`**, so a stale "bounties are live" can't survive a
  server-hop, and a dropped push degrades to the
  [teaser](#non-qualifying-client-experience) rather than a board the player
  isn't entitled to.
- [x] `de_bounty_status` console command — reports the gate. Deliberately **not**
  admin-gated: on a client it reports what the server said, which is exactly what
  a player needs when the board is missing.
- [x] No spawning, no rewards, no UI — this phase only proves a client on a
  qualifying vs. non-qualifying server sees the right sync state.
- [ ] **Needs verification**: the dependency-presence matrix and the client
  flag's behavior across respawn/reconnect/server-hop —
  [Testing.md](Testing.md) §23.

### Phase B — Spawn Location Sampling & Pin Lifecycle

**Status: built (2026-08-19), builds clean, unverified in a live session.**

Goal: pick a valid land location in the right biomes and mark it, with no
target creature yet.

- [x] `src/Bounty/BountyLocationSampler.cs` — samples uniformly **by area** in a
  ring around an origin (sqrt on the radius, or candidates bunch at the centre),
  then validates. Everything is read from **`WorldGenerator`**, which is
  procedural and answers for any coordinate whether or not that zone is loaded —
  a physics raycast would only work near a loaded player, which a server-side
  posting can't rely on.
- [x] **Deviation from the original plan, deliberate:** the plan called for a
  single ~15 m ring. That is too weak for requirement 6 — a 15 m ring passes on
  any islet wider than 30 m. The built version checks **three concentric rings
  out to `Bounty.LandCheckRadius` (default 80 m, 8 points each)**; every point
  must be above `waterLevel + WaterMargin` and none may be `Ocean`. Clearing the
  outermost ring means the surrounding landmass is at least ~160 m across, which
  is what actually rules out islets and narrow spits.
- [x] Added beyond the plan: a **local-slope check** (`Bounty.MaxHeightVariance`,
  default 10 m over the innermost ring) rejecting cliff faces and spires, since
  "always within the forest or land" implies somewhere a fight can actually
  happen; and **`Bounty.SearchMinRadius`/`SearchMaxRadius`** so postings stay out
  of the starting area and well inside the world edge.
- [x] Rejection reasons are **tallied and reported** (`biome / water /
  island-coast / steep`), so a world where placement struggles diagnoses itself
  instead of just failing.
- [x] `src/Bounty/BountyMapPin.cs` — keyed by **bounty id** (not by object), so a
  pin can be removed later from a resolution message alone. Pins are transient
  (`save = false`) so a missed resolution can never orphan a bounty pin into the
  player's saved map; uses the vanilla **Boss** icon tinted red, distinct from the
  companion player-icon pins and the companion death skull. Carries the same
  Minimap-rebuild guard `CompanionMapPins` uses (handles die on world load).
- [x] `de_bounty_sample [count]` / `de_bounty_sample_clear` console commands roll
  candidates through the real sampler and pin them locally — no creature, no
  posting. This is the "ghost bounty" verification the plan called for.
- [ ] **Needs verification**: placement never lands in water / on an islet across
  many rolls in all four biomes, and the pin lifecycle —
  [Testing.md](Testing.md) §23b.

### Phase C — Target Scaling & Spawn (self-owned curve)

**Status: built (2026-08-20), builds clean, unverified in a live session.**

Goal: an aggressive, appropriately scaled Dvergr (+ minions) at the sampled
location.

- [x] `src/Bounty/BountyTiers.cs` — a 5-tier curve owned entirely by this mod
  (see [Dependency 1](#dependency-1-biomelords-scaling)). Biome→base-tier is
  configurable per biome (`Bounty.TierBlackForest/Swamp/Mountain/Plains`,
  defaulting 1/2/3/4 — the same progression order BiomeLords uses, so a Plains
  bounty reads as harder than a Black Forest one exactly as players expect).
  Tier 5 is reserved as the rank-gated **elite** tier (Phase H).
- [x] **Health vs. damage split, chosen deliberately.** Health is scaled
  explicitly (`HealthMultiplierBase` 3× compounding by `HealthMultiplierGrowth`
  1.6 → 3/4.8/7.7/12.3/19.7×). **Damage rides on the vanilla star level**
  (`Character.SetLevel`), because vanilla already scales creature damage per
  star, mage spells included — hand-rolling a damage multiplier would mean
  patching the attack path for no real gain. Star level is capped
  (`MaxStarLevel`, default 3) precisely *because* stars carry damage: the bulk of
  a tier's difficulty is meant to come from health and escort size, not from a
  target that one-shots players.
- [x] Minion count `MinionsBase + (tier-1) × MinionsPerTier`, capped by
  `MaxMinions` (default 1/1/6), spawned in a ring (`MinionRingRadius`) around the
  target and placed on the ground. Escorts are **one tier below** their master so
  a camp is a real threat in numbers without each minion being a second boss, and
  they **skew melee** so a group isn't all casters (req. 2's minion half — no
  upstream equivalent exists, this is wholly ours).
- [x] `src/Bounty/BountyTarget.cs` — the component that keeps a bounty hostile
  and correctly scaled. `SetAggravated(true)` + `SetHuntPlayer(true)` +
  `m_alertRange × AlertRangeMultiplier` make them hunt on sight, the opposite of
  every other Dvergr in the mod (req: "automatically aggressive"). Aggression is
  **re-asserted on a slow 3 s tick** rather than per frame — a safety net against
  vanilla calming them, not an override. `m_randomMoveRange` is tightened to
  `RoamRadius` so a target stays near its posting and the map pin keeps meaning.
- [x] **Survives relog and ownership changes.** Only ZDO fields persist
  (`DE_BountyId`, `DE_BountyTier`, `DE_BountyMinion`, `DE_BountyInit`), so
  `BountyTargetRestorePatch` (a `MonsterAI.Start` postfix, mirroring
  `CompanionRestorePatch`) rebuilds the component on every spawn. The extra health
  multiplier isn't persisted by vanilla either — it's re-applied each load, and
  `DE_BountyInit` distinguishes a **fresh spawn** (fill the enlarged pool) from a
  **reload** (preserve damage already done), so a bounty can't heal to full every
  time its zone reloads.
- [x] No special-casing for communing: these are ordinary Dvergr prefabs, so once
  subdued to ≤20% HP the existing `CommunionRite` channel works unmodified
  (req. 1). `BountyTarget` **stands down** the moment a `DvergrCompanion` appears
  on the creature, so it can never keep forcing a freed ally hostile, and the
  restore patch never re-arms one.
- [x] The **"corruption awakens"** message is suppressed for bounty creatures —
  they're already hardened per the [lore hook](#lore-hook), and it would otherwise
  fire every time an escort spawned.
- [x] `de_bounty_spawn [tier] [far]` console command — spawns a real scaled
  target + escort nearby, or samples a real location first with `far`.
- [x] **Fix after the first live pass (2026-08-20): tiers were invisible.** Vanilla
  renders level 1 as **no stars** and `EnemyHud` has only two star rects, so the
  original mapping (`1 + (tier-1)/2`) showed nothing at all for tiers 1–2 and
  collapsed five tiers into three visuals. Two changes: the star level is now
  spread linearly across the tier range (tiers 1–5 → levels 1/2/2/3/3, i.e.
  0/1/1/2/2 stars), and a **`BountyNameBadgePatch`** appends the tier to the
  floating name — `[T4 Dread Bounty]` on the target, `[Escort]` on minions. This
  is the same wall the companion `★N` badge hit and the same answer; unlike on
  companions the vanilla stars are **left visible**, since for a bounty they're
  accurate and reinforce the danger. ASCII only — the game's serif font renders
  exotic glyphs as empty boxes.
- [x] Phase C **verified in-game (2026-08-20)** apart from the star/tier display
  above, which was fixed in response and needs a short re-check
  ([Testing.md](Testing.md) §23c).

### Phase D — Resolution & Rewards (ServerGuide-authored)

**Status: built (2026-08-21), both projects build clean, unverified in a live
session.** Requires a **ServerGuide rebuild** (new trigger types + `tier:` filter
+ templating vars) — already deployed.

- [x] `src/Bounty/BountyService.cs` — `NotifyKilled` / `NotifyCommuned` both
  resolve a bounty (requirement 1: the Rite pays the same as the sword). Escort
  minions never resolve anything. A `_resolved` id set makes payout **idempotent**,
  so a double death event or a kill/commune race can't pay twice.
- [x] Kill path: `BountyDeathPatch`, a separate `Character.OnDeath` patch class
  from the XP one so neither concern can break the other. Commune path: one call
  at the end of `CommunionService.TryRecruit`, which no-ops for ordinary Dvergr.
- [x] The map pin is removed the moment the bounty resolves (requirement 6).
- [x] **ServerGuide (both new triggers + schema):** `dvergr_bounty_resolved`
  (subject = effective tier) and `dvergr_bounty_valcoin`, a new `TriggerSpec.Tier`
  filter field (`tier:` in YAML, 0 = any), and four new templating vars —
  `{tier}` `{tierName}` `{method}` `{bountyBiome}`. `{bountyBiome}` is deliberately
  **its own token** rather than reusing `{biome}`, which reports the *local
  player's* current biome and is only incidentally the bounty's.
- [x] **`guidance.bounty-rewards.yaml`** (`category: Bounty`) — one reward entry
  per tier plus a reference page, authored to requirement 3 (things that need
  smelting/cooking/crafting: Coal/Bronze → Iron → Silver/Chain → BlackMetal, with
  cooked food, resist meads and Coins). **No loot table exists in C#.**
- [x] **Valcoin, reward-only.** The chance is rolled in the mod (ServerGuide has
  neither a numeric rank filter nor a random-chance reward) from the player's best
  standing across the duel **and** party ladders; only on success does
  `dvergr_bounty_valcoin` fire, whose entry's sole reward is
  `set_player_key: VC.Q.ls_bounty_t<N>` — the sanctioned Donations bridge. **No
  coin amount ever leaves this mod**, so nothing here can inflate a payout, and
  nothing in the feature spends Valcoin.
- [x] Reward-tier bonus from bounty standing (requirement 5) is wired as
  `EffectiveTier`, capped by `Bounty.MaxTierBonus` — it returns 0 until Phase E
  builds the bounty ladder, but the reward path is already shaped for it.
- [x] `de_bounty_chance` console command prints the computed per-tier payout
  chance and the standing behind it (the roll is otherwise invisible).
- [ ] **Needs verification**: [Testing.md](Testing.md) §23d — including a live
  Valcoin grant, which needs the donations backend configured.

> **Server-side setup the coin half needs.** The donations mod's
> `valcoin_quests.yaml` must define ids **`ls_bounty_t1` … `ls_bounty_t5`**.
> A key with no matching entry is silently worth nothing (logged as
> `Unknown quest '<id>'`) — harmless, but silent.

Goal: closing a bounty (kill or commune) grants items always, Valcoin
sometimes, with zero hard-coded loot in C#.

- `src/Bounty/BountyService.cs`: `ResolveBounty(player, tier, method)` called
  from (a) a `Character.OnDeath` hook scoped to tagged bounty targets/minions,
  or (b) the existing `CommunionService.TryRecruit` success path when the
  recruited creature is bounty-tagged.
- Effective tier = base tier + a small, config-capped bonus if the player's
  [bounty leaderboard](#phase-e--bounty-leaderboard) rank clears a threshold
  (req. 5's "affects the amount of rewards").
- `ServerGuideBridge.RaiseBountyResolved(effectiveTier, biome, method)` fires
  **always** — a new `dvergr_bounty_resolved` trigger, subject = tier, vars
  `{tier}` `{biome}` `{method}`. A new `guidance.bounty-rewards.yaml`, one
  entry per tier (`tier:` filter, mirroring the existing `caste:` filter
  pattern), grants the actual vanilla item bundle via `RewardDispatcher` — no
  reward code in Lost Scrolls II, per established convention. **Item choices
  are an authoring task**, not a code task: skew toward vanilla items gated
  behind cooking/smelting/crafting stations, better at higher tiers (exact
  prefab ids to be verified against the asset tree the same way earlier
  placeholder ids were, per [ServerGuide-Integration.md](ServerGuide-Integration.md)).
- Valcoin chance (req. 4, reward-only per the guardrail): `BountyService`
  reads the player's best duel/tournament rating from the existing
  `LeaderboardStore`, rolls against a config table (`Bounty.ValcoinChanceByRankTier`),
  and **only on success** fires a second trigger, `dvergr_bounty_valcoin`
  (subject = tier, var `{tier}`). A matching `guidance.bounty-rewards.yaml`
  entry's only reward is `set_player_key: VC.Q.<tier>` — the sanctioned
  Donations-side bridge. If Valheim Donations isn't present this trigger is
  simply never fired (the whole subsystem is already gated off).

### Phase E — Bounty Leaderboard

**Status: built (2026-08-22), builds clean, unverified in a live session.**

- [x] `src/Bounty/BountyRecords.cs` + `BountyLeaderboardStore.cs` — per-world JSON
  at `<save>/LostScrollsII/bounty.<world>.json`, same authority model as the duel
  ladder (server owns the file and all mutation; clients hold a pushed read-only
  snapshot, so a modded client can't write its own standing).
- [x] **Serialized through `CompetitiveJson` from day one**, never
  `UnityEngine.JsonUtility` — that silently drops `List<[Serializable] class>`
  fields on Valheim's runtime, which is exactly how the tournament board once
  shipped showing zero entrants on every client. Two methods were added to the
  existing serializer rather than writing a second one, so there stays one place
  that knows how these files are written.
- [x] **Keyed by owner, scored not rated.** Bounties aren't head-to-head, so Elo
  would be meaningless; the score is a **tier-weighted point total**
  (`PointsPerTier × tier`), which makes hunting hard postings outrun grinding easy
  ones without needing a separate anti-farm rule. Record carries
  `points / kills / communes / bestTier / lastResolved / seasonId` — kills and
  communes tallied **separately**, since both are legitimate ways to answer a
  posting and the split is worth seeing.
- [x] Reporting mirrors the duel ladder: resolution is observed on the hunter's
  client, which **reports**; the server validates (re-checking the feature gate,
  since this writes persistent standings), applies, saves and broadcasts.
- [x] **Ordering that matters:** the ladder report happens *after* the reward tier
  is decided, so a bounty can't bump the tier it just paid out at; and points are
  scored at the **base** tier, not the bonused one, so a high-standing hunter
  doesn't also earn points faster and run away with the board.
- [x] Requirement 5 is now live: `BountyLeaderboardBonus` gives **+1 reward tier**
  to hunters ranked `Bounty.LeaderboardBonusRank` (default top 3) or better,
  capped by `MaxTierBonus`.
- [x] Display: **`de_bounty_ladder [count]`** (also tells you your own rank and how
  far off the bonus is) and a **Bounty Hunters section on the existing `F6` board**
  rather than a fourth display surface. The section is hidden where bounty hunting
  isn't running, so a server without the feature doesn't advertise an empty board.
- [x] **`de_bounty_season_reset`** archives to `bounty.<world>.season<N>.json` and
  starts a new season, mirroring `de_season_reset`.
- [x] **Fix after the first live pass (2026-08-22): a remote admin couldn't reset
  a season.** The command gated on `IsServerAuthority` — being *the server* — so a
  real admin connected as a client was refused. Both season resets now route
  through the existing **admin-authenticated RPC** (`LSII_AdminCmd`, new `season`
  and `bountyseason` actions): the server re-verifies `ZNet.IsAdmin` from the
  peer's host name and executes there, with the listen host short-circuiting.
  **`de_season_reset` on the duel ladder had the identical flaw** and was fixed in
  the same change — it had been host-console-only since Phase B of the competitive
  suite.
- [x] Phase E **verified in-game (2026-08-22)** apart from the season reset above,
  which was fixed in response and needs a short re-check
  ([Testing.md](Testing.md) §23e).

Goal: a persistent, server-authoritative record of who's hunting well, feeding
back into Phase D's reward bonus.

- `src/Bounty/BountyLeaderboardStore.cs`: same per-world JSON pattern as
  `LeaderboardStore`, reusing the existing hand-rolled `CompetitiveJson`
  serializer from day one (`UnityEngine.JsonUtility` is known to silently drop
  `List<[Serializable]>` fields on this runtime — the tournament system hit
  this in production; no reason to risk it twice). File:
  `<save>/LostScrollsII/bounty.<world>.json`.
- Keyed by **ownerId** (bounty hunting is a player action, not a
  companion-keyed one). Schema: `ownerId`, `ownerName`, `points` (tier-weighted
  total), `kills`, `communes`, `bestTier`, `lastResolved`, `seasonId`.
- Points awarded on every `ResolveBounty` call, weighted by effective tier.
  Reuses the existing `SeasonReset` concept/cadence for a recurring "hunt of
  the season."
- Display: `de_bounty_ladder` console command + a bounty section appended to
  the existing `F6` ranking board (reuse the panel that already shows the
  duel/party ladders, rather than adding a fourth display surface).

### Phase F — Dedicated Bounty UI + Inventory Button

**Status: built (2026-08-23), builds clean, unverified in a live session.**

- [x] **The board itself became real this phase.** The plan had the UI listing
  postings before Phase H created any, so Phase F now owns the posting model and
  board state (`BountyPosting`, `BountyBoardStore`, persisted per world in
  `board.<world>.json`) plus accept/abandon. **Phase H is narrowed** to what's
  actually left: the timed rotation cadence and the rank-gated elite tier.
- [x] **A posting is a location + a tier, not a live creature.** Creatures are
  spawned by the hunter's own client on arrival (`BountyArrivalRadius`, default
  80 m), because Valheim doesn't simulate unloaded zones — a camp instantiated
  across the map would sit frozen or unload immediately. The client tells the
  server it spawned (`DE`-side `MarkSpawned`), so a relog mid-approach or a second
  arrival can't stack a duplicate camp.
- [x] Accept/abandon are **server-decided** (`BountySync` RPCs): a client can only
  *ask*. Otherwise a modded client could take a posting someone else holds or
  exceed `MaxActiveBountyPerPlayer`. An abandoned posting returns to the board
  rather than being deleted — the location is still valid, and anything already
  spawned there is still standing.
- [x] Answering a bounty **removes the posting** off the same resolution report the
  ladder already receives, so a client can't clear a posting it didn't finish.
- [x] `src/Bounty/BountyBoardPanel.cs` — the `F8` panel, built the same way as the
  tournament panel (uGUI canvas, widgets cloned from `InventoryGui`'s weight label
  and Take All button, so no authored assets). Lists open postings with **live
  distances**, your active posting, your standing, and the top three hunters.
- [x] A **"Bounty Board" button in the player inventory** (requirement 7), cloned
  from the inventory's own button so it inherits vanilla styling. Re-created on
  each `InventoryGui.Show`, since InventoryGui is rebuilt every world load.
  **Since 2026-08-23 this lives in [`InventoryMenuBar`](../src/Companions/InventoryMenuBar.cs)**
  (formerly `BountyInventoryButtonPatch`): the same button, now one of a row that
  also carries Rankings and Tournaments, because every panel deserved the same
  discoverability this one got.
- [x] **Placement (settled 2026-08-23): top-centre of the inventory screen**, which
  the user chose after seeing it there. It is parented to the inventory screen root
  (not to any one panel, so "centre" means the middle of the screen and it doesn't
  shift when a panel resizes or another mod adds one), anchored top-centre, with
  `LayoutElement.ignoreLayout = true` so the container's layout group can't move it.
  `Interface.MenuBarOffset` (`"x,y"` pixels) nudges the row, and the resolved parent
  and row width are logged once (`[ui] inventory menu bar: …`). The older
  `Bounty.InventoryButtonOffset` still wins when it has been changed from `"0,0"`,
  so an operator who had already dialled it in doesn't silently lose it.
  **Note it is placed there deliberately, not left to the layout group** — it first
  appeared centred only because that group was overriding the intended corner
  placement, which would have moved again the moment the bar's contents changed.
  *(Rejected on the way: bottom-left of the inventory panel; mirroring the Market
  button onto the mod bar's opposite corner; the player panel's top edge.)*
- [ ] **Needs verification**: [Testing.md](Testing.md) §23f.

Goal: req. 7's dedicated UI, reachable from the player inventory panel.

- `src/Bounty/BountyBoardPanel.cs`: a cloned-widget Canvas panel (same
  technique as `TournamentRegistration`), default key `F8` (next free slot
  after `F6`/`F7`). Shows: open board entries (biome, tier, distance to pin),
  an **Accept** button (locks it as the player's active bounty; cap
  `Bounty.MaxActiveBountyPerPlayer`, default 1), the player's active bounty
  status, and a short bounty-leaderboard snippet.
- Inventory button (req. 7 explicitly): a Harmony patch on `InventoryGui`
  adding a "Bounty Board" button that opens the same panel — this is the only
  new UI-injection point this feature needs; it does **not** touch the
  companion-container panel work already documented in
  [Ally-Inventory.md](Ally-Inventory.md).
- Reuses the input-capture pattern already proven on the `F7` tournament panel
  (`PlayerController.TakeInput` postfix, `SetMouseLook` skip,
  `UpdateMouseCapture` cursor release, `ZInput.GetButton*` swallow) rather than
  re-deriving it.

#### Non-Qualifying Client Experience

The inventory button and `F8` key are **always present** client-side (the UI
code itself doesn't need the gate — only the data behind it does). If
`BountySync`'s pushed `LSII_BountyActive` is false (missing a dependency, or a
non-server context), opening the panel shows a **static info panel** instead
of a live board: "Bounty Hunting is available on servers running the Quest
pack together with BiomeLords and Valheim Donations." No Valcoin mention
needed — this is purely the "way to introduce this to non-server users" req.
10 asked for, at zero functional cost.

### Phase G — Quest Gate & Narrative

**Status: built (2026-08-23), builds clean, unverified in a live session.**

- [x] **`guidance.bounty.yaml`** (`category: Bounty`) — an `npc_conversation` on
  **Haldor**, hold-`[E]` (a short press still opens his store). A short node tree:
  the warden explains that most Dvergr are merely *held* and can be freed, but some
  turned the corruption into a weapon and hunt the roads by choice. Accepting takes
  a commission. **Haldor is free to use** — the server is retiring
  `ProfMags-HaldorBounties` in favour of this system.
- [x] **The two-way handoff needs no new API in either mod:**
  *ServerGuide → mod* is a stock `set_player_key` reward (`LS_BountyStart`) that
  the mod watches for, acts on, and clears — the same bridge Valheim Donations
  already uses, so no custom trigger type is involved. *Mod → gate* is
  `LS_BountyUnlocked`, set when the commission bounty resolves, which this mod
  observes directly **because it posted that bounty itself**. That's what makes
  ServerGuide's missing reverse-query API a non-issue rather than a blocker.
  Both are Valheim unique keys, so they persist per character with the save at no
  storage cost to us.
- [x] `BountyBoardStore.CreateTutorial` — a guaranteed **tier 1** posting (the
  biome it lands in shouldn't decide how hard a player's first bounty is),
  **already assigned** to that player so there's no chicken-and-egg problem of
  needing board access to take it, sampled **near them** (150–1200 m, widening once
  on failure) because a first bounty 6 km away isn't a tutorial.
- [x] The commission **can't be abandoned** — it's the way in, and dropping it
  would strand the player with no route back to the board.
- [x] A **locked board teaches the way in** rather than showing an empty list: it
  names Haldor and the hold-`[E]` interaction, so the feature is discoverable from
  the UI alone.
- [x] A second reference page (`ls_bounty_howto`, gated on the commission) explains
  the loop once the board opens. It owns the *how-to*;
  `guidance.bounty-rewards.yaml` owns the reward bundles — no overlap.
- [x] `de_bounty_commission` / `de_bounty_quest_reset` stand in for the
  conversation and replay the gate while testing.
- [ ] **Needs verification**: [Testing.md](Testing.md) §23g.

> **Authoring note for anyone editing the dialogue:** the reward must sit on a
> **choice** (`ChoiceSpec.Rewards`), not on a node. Node-based conversations end
> through `OnNodeConversationEnd`, which never calls `RewardDispatcher` — a reward
> hung off a node would look right and silently never fire.

### Phase H — Wanted Board Rotation & Rank-Gated Tiers

**Status: built (2026-08-24), builds clean, unverified in a live session.**

Narrowed from the original plan because [Phase F](#phase-f--dedicated-bounty-ui--inventory-button)
took the board itself; what remained is the cadence and the elite gate.

- [x] **Rotation.** `EnsurePostings` now retires **unclaimed** postings older than
  `Bounty.RefreshHours` (default 24) before refilling, so the board turns over on
  its own even if nobody hunts — a reason to check back. **Accepted postings never
  expire**, nor does the warden's commission: a hunter part-way to their mark must
  not have it pulled out from under them. Rotation uses **real elapsed time**
  (`postedTicks` is UTC and persists with the board), so the cadence matches how an
  admin thinks about it and survives restarts. `RefreshHours = 0` disables it.
- [x] **Elite postings.** The top tier is never reached from a biome alone, so an
  elite posting is the only way tier 5 appears: each new posting has a
  `Bounty.EliteChance` (default 0.25) roll, capped at **one open at a time** — it's
  meant to be *the* thing on the board, not a category.
- [x] **Rank gate (requirement 4's second, non-Valcoin half).** Only hunters in the
  top `Bounty.EliteRankTopN` (default 10) of the **duel or party** ladder may accept
  one. This is deliberately a different lever from the coin roll: **rank buys access
  here, where Valcoin rank only changes a chance** — access is the stronger pull
  toward the duel and tournament systems, which is the point of the whole feature.
- [x] **A locked elite posting is shown, not hidden** — as a disabled row, with a
  line naming exactly what would unlock it. Hiding it would hide the reason to go
  and rank up; seeing it locked is the entire purpose of the gate.
- [x] The gate is **re-checked server-side on accept**, not merely enforced by the
  panel: the panel greys it out for looks, `BountyBoardStore.Accept` is the rule.
- [x] `de_bounty_board` now reports each posting's **age**, the rotation window, and
  tags commissions and elite postings.
- [ ] **Needs verification**: [Testing.md](Testing.md) §23h.

> The tier 5 reward bundle already exists in `guidance.bounty-rewards.yaml`
> (`ls_bounty_reward_t5`, the "Accursed" entry with the Discord broadcast) — it was
> authored in Phase D and only becomes reachable now.

### Phase I — Polish, Testing, Packaging

**Status: done (2026-08-24). Released as 0.8.0.**

- [x] Testing sections **§23–§23h** were written per phase as it was built rather
  than in one pass at the end — each was run before the next phase built on it,
  which is how the star-badge, season-reset and button-placement bugs were caught
  while they were still cheap to fix.
- [x] Version **0.7.0 → 0.8.0** across csproj, `Plugin.cs`, and both manifests;
  Quest dep → `TaegukGaming-Lost_Scrolls_II-0.8.0`.
- [x] Both package CHANGELOGs and READMEs written, including a plain statement that
  bounty hunting is a **server feature** and what a server needs to run it.
- [x] Quest pack now bundles **eight** guidance files, all under
  `config/ValheimServerGuide/LostScrollsII/` (the per-mod subfolder convention).
- [x] New player-facing wiki page **`wiki/Bounty-Hunting.md`**, linked from `Home.md`
  (which also now says four screens, not three).
- [x] Both zips rebuilt and structure-verified (`Lost_Scrolls_II_0.8.0.zip`,
  `Lost_Scrolls_II_Quest_0.8.0.zip`).
- [x] **Config left at first-pass values deliberately.** Every knob is documented in
  its config description and in the table below; this project's practice is to tune
  after live play rather than guess twice, and nothing in the live passes suggested
  a value was wrong.
- [x] **ServerGuide 0.14.0 cut (2026-08-24)**, carrying this feature's `tier:` filter
  and the two `dvergr_bounty_*` triggers. The Quest pack's dependency is pinned to it
  **hard** rather than "0.9.0+": on an older ServerGuide the unknown trigger type still
  matches (`default: return true`) and the tier filter is ignored, so **every** tier's
  reward bundle would fire at once. Upload order and detail in
  [Publishing.md](Publishing.md).

## Config Summary (first-pass names, values TBD)

| Key | Purpose |
|---|---|
| `Bounty.Enabled` | master toggle, forced off if the triple-gate fails ✅ *bound (Phase A)* |
| `Bounty.BountyUiKey` | opens the Wanted Board panel (default `F8`) ✅ *(Phase F)* |
| `Bounty.RefreshHours` | how long an unclaimed posting lasts before rotating out ✅ *(Phase H)* |
| `Bounty.MaxBoardEntries` | open postings at once ✅ *(Phase F)* |
| `Bounty.MaxActiveBountyPerPlayer`, `Bounty.ArrivalRadius` | accepted-bounty cap per player; how close a hunter must get before the camp spawns ✅ *(Phase F)* |
| `Bounty.SearchMinRadius`, `Bounty.SearchMaxRadius` | how far from the world centre bounties may be posted ✅ *(Phase B)* |
| `Bounty.LandCheckRadius`, `Bounty.WaterMargin`, `Bounty.MaxHeightVariance`, `Bounty.SampleAttempts` | land-only location sampling ✅ *(Phase B)* |
| `Bounty.TierBlackForest/Swamp/Mountain/Plains` | base tier per eligible biome ✅ *(Phase C)* |
| `Bounty.HealthMultiplierBase`, `Bounty.HealthMultiplierGrowth`, `Bounty.MaxStarLevel` | difficulty curve ✅ *(Phase C)* |
| `Bounty.MinionsBase`, `Bounty.MinionsPerTier`, `Bounty.MaxMinions`, `Bounty.MinionRingRadius` | escort size + placement ✅ *(Phase C)* |
| `Bounty.AlertRangeMultiplier`, `Bounty.RoamRadius` | how far a bounty notices hunters / wanders ✅ *(Phase C)* |
| `Bounty.PointsPerTier`, `Bounty.LeaderboardBonusRank` | bounty-ladder scoring + the rank needed for the reward-tier bump ✅ *(Phase E)* |
| `Bounty.MaxTierBonus` | reward-tier bump for top bounty hunters ✅ *(Phase D)* |
| `Bounty.ValcoinBaseChance`, `Bounty.ValcoinChancePerTier`, `Bounty.ValcoinRankBonus`, `Bounty.ValcoinRankDepth`, `Bounty.ValcoinMaxChance` | duel/tournament rank → Valcoin roll chance ✅ *(Phase D)* |
| `Bounty.EliteChance`, `Bounty.EliteRankTopN` | how often an elite posting appears; the duel/party rank needed to accept one ✅ *(Phase H)* |

## Open Questions (resolve during/after implementation)

- Exact vanilla item ids for the tier reward tables (authoring task, not a
  design blocker).
- Whether "1 active bounty per player" is too restrictive once the board
  rotation (Phase H) is live — may want a small per-player cap >1.
- Whether the tutorial bounty's guaranteed tier-1 spawn ever needs a retry
  path if a player is in a spot with no valid nearby biome (unlikely given
  the four eligible biomes' map coverage, but worth a look during Phase G).
- Final numeric tuning across the whole config table above — first-pass values
  only until a live multi-player session.
