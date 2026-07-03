# Changelog

A consolidated record of feature batches and fixes, newest first. Each entry
points to the feature doc that carries the full detail. **Everything below builds
clean but is verified in-game only where a test in [Testing.md](Testing.md) is
marked passed** — assume "unverified in a live session" otherwise.

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
