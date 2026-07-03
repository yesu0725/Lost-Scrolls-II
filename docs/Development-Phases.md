# Development Phases

## Phase 0 — Project Setup
**Status:** done

- BepInEx plugin scaffold (`.csproj`, `Plugin.cs`).
- References to Valheim's managed assemblies (publicized `assembly_valheim`), BepInEx, Harmony.
- Build/deploy pipeline targeting local Valheim install, r2modman profile, and dedicated server, mirroring the pattern used in `Valheim ServerGuide`.
- No gameplay code yet.

## Phase 1 — Lore & Quest Script
**Status:** done

- [x] Finalize [Lore.md](Lore.md) content into an actual scene-by-scene/location-by-location script — see [Quest-Script.md](Quest-Script.md) (6 acts, Burial Chambers through the restored Altar).
- [x] Draft the conceptual `guidance.yaml` chain outline for ServerGuide — see [Quest-Chains-Draft.yaml](Quest-Chains-Draft.yaml) (design only, not wired up; Acts 1–2 use trigger types ServerGuide already supports, Acts 3–6 are flagged as blocked on Phase 5's new trigger types).

## Phase 2 — Core Ally MVP
**Status:** code written and compiles against the real game assembly; **not yet verified in-game**

- [x] Single Dvergr type: **Rogue** only.
- [x] Implement Communion Rite recruit flow per [Ally-Recruitment.md](Ally-Recruitment.md) — simplified for MVP: hold a configurable key (default `G`) while hovering a Dvergr-faction creature at or below 20% health to recruit it. The Sword-of-Truth item gate from the lore script is deferred, not yet wired in.
- [x] Basic follow AI on recruit (`MonsterAI.SetFollowTarget`), faction flipped to `Players` so vanilla AI stops treating it as hostile.
- [ ] No leveling, chores, or duels yet — by design, out of scope for this phase.
- [ ] **Needs in-game verification**: recruit flow, follow behavior, and multiplayer ownership/sync have not been tested in a running game session. Treat as unverified until played.

### Implementation notes
- `src/Companions/DvergrCaste.cs`, `DvergrCompanion.cs`, `CommunionService.cs` — recruit logic and companion marker component.
- `src/Plugin.cs` — hotkey-driven recruit trigger using `Player.GetHoverObject()`.
- Companion state (`DE_Recruited`, `DE_Caste`) persisted on the creature's own ZDO.
- All vanilla API calls used here (`Character.Faction`, `GetHealthPercentage`, `MonsterAI.SetFollowTarget`, `Player.GetHoverObject`, `ZDO.Set/GetBool/GetInt`) were confirmed to compile against the real publicized `assembly_valheim.dll` — names are correct, but compiling is not the same as confirming runtime behavior.
- Multiplayer server-authority hardening (per [Technical-Constraints.md](Technical-Constraints.md)) is intentionally deferred — MVP recruit logic runs client-side off the hovering player only and doesn't yet handle ZDO ownership claims or cross-client sync edge cases.

## Phase 3 — Leveling System
**Status:** code written and compiles against the real game assembly; **not yet verified in-game**

- [x] XP tracking per [Ally-Leveling.md](Ally-Leveling.md): companion XP/level persisted on its own ZDO.
- [x] **Level cap 10 with a rising curve** (`XpToNextByLevel` = 100/250/450/700/1000/1400/1900/2500/3200; 11,500 XP total). Reworked from the original flat-100/cap-3 placeholder after feedback (see the second-feedback-pass section below).
- [x] **Biome-/HP-scaled XP** (`KillXpPatch.BiomeScaledXp`): `cap × clamp(creatureMaxHP / biomeReferenceHP, 0.25, 1.0)`, HP read live at kill time. Per-biome caps/references in [Ally-Leveling.md](Ally-Leveling.md). Awarded to any recruited companion within 20m of a hostile death (proximity model — no participation check yet). **Player kills** grant a flat 50 (Plains-tier).
- [x] Stat scaling reuses vanilla's own star-tier system via `Character.SetLevel()`. Per-caste differentiation not yet implemented (uniform for now).
- [x] Level-up fires the `dvergr_level_up` event to ServerGuide (wired in Phase 5).
- [x] **Custom level badge** instead of vanilla stars (`CompanionLevelBadgePatch`): vanilla `EnemyHud` only has two star rects (verified), so a gold `★N` badge is appended to the floating name and the vanilla star rects are hidden on companions. The cap of 10 also keeps `SetLevel` within vanilla's own range concern from before.
- [ ] **Needs in-game verification**: that XP is granted on kill (watch the `[xp]` log), the biome reference HP values are sane (correct from the live log if not), and the `★N` badge renders correctly.

### Implementation notes
- `src/Companions/DvergrCompanion.cs` — `Level`, `Xp`, `AddXp()`, `XpToNextByLevel`, `MaxLevel`, `IsMaxLevel`, `XpPercentToNextLevel`, `ApplyLevelToCharacter()`.
- `src/Companions/KillXpPatch.cs` — Harmony `OnDeath` patch; biome/HP-scaled XP + player-kill XP.
- `src/Companions/CompanionLevelBadgePatch.cs` — `★N` badge + vanilla-star suppression.

## Phase 4 — Chores / Automation
**Status:** ✅ **verified in-game (2026-07-02)** — the full caste-gated chore system (Fire/Ice smelting+refining, Support provisioning/farming/husbandry, Rogue hauling), plus persistence, claim tooltips, farming plant+harvest with biome gating, the Cultivator-on-item-stand field trigger, and feed claim-by-range. See [Testing.md](Testing.md) §7d/§8/§8b–§8f.

Phase 4 started as Smelter-only ore feeding; it has since grown (across later feedback) into a full caste-gated chore system. [Ally-Chores.md](Ally-Chores.md) is the authoritative spec. Current state:

- [x] **Caste-gated assignment** (`H`): the hovered target decides the chore and the required caste (`ChoreRules` + `CommunionService.DetectCaste`). One `ChoreAI` component, mode-based.
- [x] **Smelting** (Fire Mage) — any `Smelter`-family station (smelter/blast furnace/charcoal kiln), ore **and fuel**, capacity-gated, with vanilla add VFX.
- [x] **Refining** (Ice Mage) — the `Smelter`-family eitr refinery / spinning wheel (same code path).
- [x] **Provisioning** (Support Mage) — Fermenter (load base / tap when ready) and Cooking Station (add raw / pull done before it burns / fuel).
- [x] **Farming** (Support Mage) — **plants seeds from a chest AND harvests ripe `Pickable`s** into it, any crop type (seed→sapling via `PlantingCatalog`; plants on free cultivated ground). Assign tooltip on crops. Pick VFX on harvest, place VFX on planting.
- [x] **Husbandry** (Support Mage) — feed hungry tamed animals from a chest; one mage tends the whole pen (all hungry animals in radius). Assign tooltip on tamed livestock.
- [x] **Hauling** (Rogue) — sweep loose ground items into a hovered chest.
- [x] **Voiced blockers** — each worker says what's stopping it (can't reach / no chest / missing input / fire out / exposed brew / chest full) via `Chat.SetNpcText`. 3D detection; ranges bumped (arrival 4.5m, search 8m).
- [x] **Verified in-game (2026-07-02):** every chore above confirmed working, including farming plant+harvest, biome-gated planting, the Cultivator-on-item-stand trigger, Chicken/Hen feed tooltip, feed claim-by-range, and the Stone Oven cooking regression.
- [ ] **Remaining unbuilt (by design):** chore time still grants **no XP**. *(Farm replanting and assignment persistence — both implemented.)*

### Implementation notes
- `src/Companions/ChoreAI.cs` — mode-based chore worker (Smelter/Farm/FeedAnimals/Fermenter/Cooking/Haul).
- `src/Companions/ChoreRules.cs` — station→caste gating + caste display names.
- `src/Companions/CommunionService.cs` — `DetectCaste` (caste from prefab on recruit).
- `src/Plugin.cs` — `ChoreAssignKey`/`ChoreAssignRadius` config; the hover-routing + caste-gated assign handler.

## Phase 5 — ServerGuide Integration
**Status:** code written and compiles in both projects; **not yet verified in-game**

- [x] Added `dvergr_recruited`, `dvergr_level_up`, `dvergr_duel_won` trigger types to ServerGuide's registry (`TriggerSpec.Caste`, `GuidanceDispatcher.MatchesTrigger` cases, `MatchDvergrLevelUp` helper). Built and redeployed ServerGuide clean.
- [x] Wired Phase 2/3 events into those triggers via a new `ServerGuideBridge` (in-process static calls, soft-dependency guarded — see [ServerGuide-Integration.md](ServerGuide-Integration.md)). *(Update: `dvergr_duel_won` now has a live caller — `DvergrCompanion.AwardDuelWin` — since Phase 6's duel mode exists.)*
- [x] Authored the first real `guidance.yaml` chains from the Phase 1 script: `E:\Valheim Modding\Valheim ServerGuide\examples\guidance.lost-scrolls.yaml`. *(Update 2026-07-03: reworked into a biome descent and **deployed** to the test profile alongside a new `guidance.companions.yaml` handbook — see the post-phase batch below.)*
- [x] **Placeholder identifiers resolved (open item #2):** the `TODO_` item/location placeholders are now real vanilla ids confirmed against Valheim's asset tree (`ValheimTemplate`) — `SurtlingCore`, `WitheredBone`, `SwordMistwalker`, `Mistlands_DvergrTownEntrance*`, `Mistlands_DvergrBossEntrance1`. Two trigger types were also corrected in passing: the Act 3 "first corrupted Dvergr" beat changed from `kill` to `location_entered` (fits free-don't-kill), and the Act 4.3 stronghold beat from the unsupported `location` type to `location_entered`. Still unverified live.
- [x] **Bug fix in passing**: Phase 3's kill-XP patch was targeting `Character.RPC_OnDeath` (a string-resolved guess); reading ServerGuide's own `BossDefeatedTrigger.cs` during this phase showed the correct method is `Character.OnDeath`. Fixed and now resolves via `nameof` (compile-time verified, not just string-resolved).
- [ ] **Needs in-game verification**: none of the cross-mod firing (recruit → raven popup, level-up → raven popup) has been observed actually happening in a live session.

### Implementation notes
- ServerGuide: `src/Config/GuidanceConfig.cs`, `src/Triggers/GuidanceDispatcher.cs`.
- Lost Scrolls II: `src/Integration/ServerGuideBridge.cs` (new), `src/Plugin.cs` (soft `BepInDependency`), `src/Companions/CommunionService.cs` and `DvergrCompanion.cs` (call sites), `src/Companions/KillXpPatch.cs` (OnDeath fix), `LostScrollsII.csproj` (new reference to `ValheimServerGuide.dll`).

## Phase 6 — Duel / Arena System (reworked 2026-07-02 into "duel mode")
**Status:** code written and compiles against the real game assembly; **not yet verified in-game** — the duel path is now multiplayer-only, so it needs two players to test

The original scripted 1v1 (`DuelController` + a `Character.ApplyDamage` prefix) has been **removed** and replaced by a companion "duel mode" that rides on vanilla AI. See [Duel-Arena.md](Duel-Arena.md) for the full design.

- [x] **Duel mode**, toggled by the owner (`J`) on their **own** companion (req 1). A duel-mode companion fights **only** other players' duel-mode companions (different, non-zero owner) and ignores/‌is-immune-to everyone else (req 2). This makes duels **multiplayer-only** — a solo player can't self-farm win-XP.
- [x] Targeting via the rewritten `BaseAI.IsEnemy` patch (authoritative: `true` only if both are duel-mode with different non-zero owners). Vanilla `MonsterAI` then does acquisition/pathing/attack — no hand-rolled straight-line movement, so the old "gets stuck on terrain" risk is gone.
- [x] **Auto-stand-down** when no rival remains (or a 60s wait timeout), and when the owner logs out / leaves ~40m vision range — each with a chat notification (reqs 3 & 5). Driven by `DvergrCompanion.TickDuel` on the ZDO-owning instance.
- [x] **Non-lethal + winner +50 XP** folded into the `Character.Damage(HitData)` prefix — a companion-vs-companion blow past the 5% HP floor is capped, loser leaves duel mode, striker is credited. **The old unverifiable `ApplyDamage` risk is eliminated**: `Character.Damage(HitData)` is Cecil-confirmed.
- [x] **PvP immunity** (req 6): the same prefix cancels any player hit on a duel-mode companion, PvP flag or not.
- [x] **Owner name tag** on companion floating names (req 4), persisted on ZDO `DE_OwnerName`.
- [x] Duel state is ZDO-backed (`DE_Duel`, claims ownership on write) so it replicates cross-client; cleared on spawn so a relog ends any duel.
- [x] **No arena structure** — still scoped out; a duel is a direct owner command, not gated on a built zone.
- [ ] **Needs in-game verification (two players):** that `DE_Duel` replicates so two different players' duelists actually pair up and seek each other; that non-lethal/PvP-immunity/auto-stand-down all behave. MP caveat: notifications are driven on the ZDO owner, so on a dedicated (server-owned) headless host they may not display — works on a listen host / owning client.

### Non-duel change (same batch)
- [x] **Butcher-knife betrayal**: striking a (non-dueling) companion with a `KnifeButcher` turns it feral — hostile to all players incl. owner (`DvergrCompanion.GoFeral`, detected in `CompanionDamagePatch`). Not timed retaliation; a deliberate release action.

### Implementation notes
- **Deleted:** `src/Companions/DuelController.cs`, `src/Companions/DuelDamagePatch.cs`.
- `src/Companions/DvergrCompanion.cs` — duel-mode state (ZDO-backed `DuelMode`, `EnterDuelMode`/`ExitDuelMode`/`TickDuel`/`AwardDuelWin`), `GoFeral`/feral hostility, owner-name storage/tag, `SubdueFloorHealthFraction`.
- `src/Companions/CompanionThreatPatches.cs` — rewrote `CompanionIsEnemyPatch` (duel targeting) and `CompanionDamagePatch` (now a `bool` prefix: PvP immunity + non-lethal + butcher-knife + existing retaliation/owner-attack).
- `src/Companions/CompanionLevelBadgePatch.cs` — owner name tag in `GetHoverName`.
- `src/Companions/CommunionService.cs` — persist owner name on recruit / `de_spawn`.
- `src/Plugin.cs` — `HandleDuelInput` is now a single-target owner toggle (was the two-step selection handler); removed `_duelFirstSelection`.
- `ServerGuideBridge.RaiseDuelWon` is now fired from `DvergrCompanion.AwardDuelWin`.

## Feature Batch — UX/QoL additions + project rename (post-Phase 6, pre-Phase 7)
**Status:** code written and compiles against the real game assembly; **not yet verified in-game**

Requested out of band from the phase order — implemented before Phase 7 since these are cross-cutting UX gaps (discoverability, companion care, basic control) rather than new roster content.

- [x] **Project rename**: "Dvergr Expanded" → "Lost Scrolls II". `PluginGuid` → `com.lostscrollsii`, `PluginName` → `Lost Scrolls II`, C# namespace `DvergrExpanded` → `LostScrollsII`, `DvergrExpanded.csproj` → `LostScrollsII.csproj`. Old stale deployed plugin folders (under the old name/GUID) were removed from all three install targets to avoid two copies of the mod loading side by side. The working directory folder was initially left as `Dvergr Expanded` (only the mod's identity changed); it has **since** been renamed to `Lost Scrolls II` to match, and the project published to GitHub at `https://github.com/yesu0725/Lost-Scrolls-II`.
- [x] **Companion indicator + recruit hint** (`HoverTextPatch.cs`): a `Character.GetHoverText()` postfix appends `Companion · Lv X (Y% to next)` to a recruited ally's tooltip, or `[<key>] Communion` to a subdued-but-unrecruited Dvergr's tooltip. See [Ally-Recruitment.md](Ally-Recruitment.md).
- [x] **Level/XP% indicator**: `DvergrCompanion.XpPercentToNextLevel`, surfaced through the same hover patch — no standalone UI, hover text is the only way to see it. See [Ally-Leveling.md](Ally-Leveling.md).
- [x] **Feed/heal** (revised after first feedback — see below): originally `FeedKey` (`F`) + any generic food item; replaced with mead-potion feeding on the `G` key. See [Ally-Commands.md](Ally-Commands.md).
- [x] **Follow/Guard/Standby stance**: `StanceCycleKey` (default `E`) cycles the hovered companion's stance Follow → Guard → Standby. Guard and Standby both anchor a `MonsterAI.SetPatrolPoint()`; Guard additionally widens `m_alertRange`, while Standby zeroes it (fully passive except retaliation). Blocked while chore-assigned or dueling. See [Ally-Commands.md](Ally-Commands.md).
- [ ] **Needs in-game verification**: `GetHoverText()` postfix output formatting (untested whether it looks right); whether `SetPatrolPoint()` actually anchors rather than allowing roam; whether widening `m_alertRange` for Guard has unintended side effects.

### Implementation notes
- New: `src/Companions/HoverTextPatch.cs`.
- Extended: `src/Companions/DvergrCompanion.cs` (`XpPercentToNextLevel`, `CompanionStance` enum, `SetStance`), `src/Plugin.cs` (`StanceCycleKey` config + handler).

## Follow-up fixes (post-feature-batch, first feedback pass)
**Status:** code written and compiles against the real game assembly; **not yet verified in-game**

- [x] **Bug fix — recruited Dvergr kept attacking the player** (took two passes): first pass cleared target/alerted/hunt but the Dvergr *still* attacked, including mid-fight. Decompiling the real assembly revealed the true root cause — Dvergr are neutral until **aggravated**, and `m_aggravated` (not faction, and not cleared by vanilla `MakeTame`) is what drives their hostility. Final fix adds `MonsterAI.SetAggravated(false, BaseAI.AggravatedReason.Damage)` plus `SetTarget(null)`. All signatures verified against assembly metadata. See [Ally-Recruitment.md](Ally-Recruitment.md) "Bug fix" section. **Still needs in-game confirmation.**
- [x] **Feed mechanic redesigned**: replaced the separate `FeedKey` + generic-food version with mead-potion feeding consolidated onto the `CommunionKey` (`G`). ⚠️ **The heal-source and name-filter described in this pass were both wrong and are superseded by the second feedback pass below** — heal was read from `m_shared.m_food` (always 0 on a mead) and meads were matched by prefab name. See the next section for the corrected mechanism.

### Implementation notes
- New: `src/Companions/MeadFeedingService.cs`.
- Changed: `src/Companions/CommunionService.cs` (AI-reset fix), `src/Plugin.cs` (removed `FeedKey`/`FeedHealFraction`, merged feed into `HandleCommunionInput`), `src/Companions/HoverTextPatch.cs` (hover hint now reads "Feed" for companions).
- Renamed: `src/LostScrollsII.csproj` (was `DvergrExpanded.csproj`), namespace across all `.cs` files.

## Follow-up fixes (second feedback pass)
**Status:** code written and compiles against the real game assembly; **not yet verified in-game**

- [x] **Feeding never worked — wrong heal source (fixed)**: meads carry `m_food == 0`; their heal is delivered by `m_consumeStatusEffect` (an `SE_Stats`, `m_healthUpFront + m_healthOverTime`), verified against assembly metadata. `MeadFeedingService` now detects a healing mead by *behavior* (a Consumable whose consume effect restores health) instead of by prefab name, and reads the real heal from that effect.
- [x] **Heal amount made flat**: the companion now heals the **same flat HP the potion gives the player**, not a fraction scaled to the companion's (much larger) max-health pool, which was massively over-healing.
- [x] **Feed VFX**: feeding plays the potion's own consume effect (`m_consumeStatusEffect.m_startEffects`) on the companion — vanilla-assets-only.
- [x] **Recruited Dvergr reverted to uncommuned after relog (fixed)**: recruit state (faction, calmed AI, the `DvergrCompanion` component) wasn't persisted; only the `DE_Recruited` ZDO flag survived. `CompanionRestorePatch` (a `MonsterAI.Start` postfix) re-reads that flag on every spawn and rebuilds the freed companion via `CommunionService.RestoreCompanion`. Recruit and restore now share one `ApplyFreedState`.
- [x] **Leveling overhaul** (cap 10, rising curve, biome/HP-scaled XP, player kills, `★N` badge) — folded into the Phase 3 section above.

### Implementation notes
- New: `src/Companions/CompanionRestorePatch.cs`, `src/Companions/CompanionLevelBadgePatch.cs`.
- Changed: `src/Companions/MeadFeedingService.cs` (consume-effect detection, flat heal, VFX), `src/Companions/CommunionService.cs` (`RestoreCompanion` + shared `ApplyFreedState`), `src/Companions/DvergrCompanion.cs` (cap 10, curve, `IsMaxLevel`), `src/Companions/KillXpPatch.cs` (biome/HP scaling + player kills), `src/Companions/HoverTextPatch.cs` (`Lv X (max)`).
- New doc: [Testing.md](Testing.md) — the persistent in-game test checklist.

## Phase 7 — Full Roster Expansion
**Status:** code-complete (multi-caste recruit/chores/UX, lightweight per-caste leveling, caste lore finalized); **not yet verified in-game**.

- [x] **Recruit** works for all four castes — caste is detected from the prefab (`Dverger`/`DvergerMageFire`/`DvergerMageIce`/`DvergerMageSupport`) via `CommunionService.DetectCaste`, no longer hardcoded to Rogue.
- [x] **Chores** are caste-specialized: Fire→Smelting, Ice→Refining, Support→Provisioning/Farming/Husbandry, Rogue→Hauling (`ChoreRules`).
- [x] **Caste surfaced in UX** — recruit message and hover text name the caste (`DvergrCaste.Display()`); `[recruit]` log records the detected caste.
- [x] **Leveling & duels** already operate on any caste uniformly. Combat *identity* is inherent — each caste is a different vanilla prefab with its own attacks (Fire casts fireballs, Ice frost, Support heals/shields, Rogue melee), so they already fight differently without custom code.
- [x] **Per-caste leveling differentiation** (lightweight) — Rogue/Fire gain +3% move speed/level, Ice/Support gain +4% max health/level, layered on `SetLevel` (`DvergrCompanion.ApplyCasteBonus`). See [Ally-Leveling.md](Ally-Leveling.md).
- [x] **Caste-specific recruit lore (finalized — open item #3)** — four always-on caste "voices" in `guidance.lost-scrolls.yaml`, polished to final text; the `dvergr_recruited` trigger carries `Caste`. Coexist with the act-gated story beats via approach (c): beats speak in the world's voice (intro/rune), voices in the raven's (Act 4 Fire/Ice beats moved raven→rune to keep the two channels distinct).

## Feature Batch — gospel-allegory lore + "corruption awakens" (post-Phase 7)
**Status:** code written and compiles against the real game assembly; **not yet verified in-game**

- [x] **Lore finalized** as a never-named **gospel allegory** ([Lore.md](Lore.md)): corruption = sin/rebellion, Damon = the adversary reigning over a fallen world, Sword of Truth = the Word (recovered, not invented), Communion = grace. Altar of Communion dropped from lore (gameplay reward only).
- [x] **The corruption awakens** ✅ *verified in-game 2026-07-02*: when an unrecruited Dvergr first becomes aggravated, a short center-screen line frames *why* it turns hostile — the corruption sleeping within it has been roused (`src/Companions/CorruptionAwakensPatch.cs`, a `BaseAI.SetAggravated` prefix; fresh-transition + faction + proximity + throttle guarded). This is the diegetic expression of "the corruption within." See [Ally-Recruitment.md](Ally-Recruitment.md) → "The corruption awakens".
- [x] **Bug fix** ✅ *verified 2026-07-02*: a freed ally no longer attacks its owner when the owner attacks a nearby wild Dvergr — `ApplyFreedState` clears `m_aggravatable` so `AggravateAllInArea` can't re-aggravate the ally (§2c). Also verified: freed allies no longer attack build pieces — `m_attackPlayerObjects` cleared (§2d). See [Ally-Recruitment.md](Ally-Recruitment.md).
- [x] **Dropped** (built earlier this batch, then removed at the user's request): the **caste recruit-order gate** (`RecruitProgress`) and **pre-corrupted camps** (`CorruptionZones`/`CorruptedSpawnPatch`). Superseded by the "corruption awakens" message above.
- [x] **Verified in-game (2026-07-02):** the awakening message fires on provoking a Dvergr, reads well, and doesn't spam (Testing.md §1b); the owner-safe fix (§2c) and build-piece fix (§2d) both hold.

## Feature Batch — story/guidance overhaul + Companion Handbook (2026-07-03)
**Status:** authored, deployed, and **verified in-game 2026-07-03** — §10b (biome descent + StartTemple opening + returning-player nudge), §10c (recruit-order guide), §10d (Companion Handbook) all passed (see [Testing.md](Testing.md)).

- [x] **Lore reworked from 6 "Acts" into a biome-by-biome descent** (`guidance.lost-scrolls.yaml`). Reflective beats fire at distinct world locations Meadows→Ashlands; the through-line is a mirror held up to the player (the toiling Dvergr are us — slaves of our own path, in unrecognized rebellion; the chore system is the thesis made playable). **Scripture woven in verbatim, never cited.** `intro` display only at the opening (`StartTemple`) and the Ashlands finale; `rune`/`raven` between. [Lore.md](Lore.md) updated to the descent structure; [Quest-Script.md](Quest-Script.md) / [Quest-Chains-Draft.yaml](Quest-Chains-Draft.yaml) marked superseded.
- [x] **`distance` triggers, not `location_entered`** — chosen so the beats fire for players **already on the server**: `location_entered` burns a one-shot per-location dedup key for every place a player nears regardless of guidance (persisted per character), which would permanently block the beats; `distance` only burns when a matching beat is in range. Beats are **independent entries** (chains would stall on already-passed locations); order carried by directional text. Returning players are nudged back to `StartTemple` by `ls_call_to_start` (a `timed` raven, `stop_when` the opening beat).
- [x] **Recruit-order guide** (`ls_guide_recruit_order`) — a tracked chain (Rogue→Fire→Ice→Support) whose HUD tooltip names the next caste to free; advances only on the asked-for caste (recruitment itself stays un-gated).
- [x] **Companion Handbook** (`guidance.companions.yaml`, `category: Companions`) — gameplay help teaching the command keys (first recruit), per-caste chores (on freeing each caste), and adventure use (ship_sailed / portal_used / dvergr_level_up / build-incinerator). See [Testing.md](Testing.md) §10d.
- [x] **Deployed**: ServerGuide auto-merges every `*.yaml` in `config/ValheimServerGuide/`; both authored files copied there (no manual merge, no id collisions). Ashlands `Charred*` location names wildcarded pending `[distance]`-log confirmation.

## Pending feature requests (not yet built)

Tracked so they aren't lost; raised during feedback, deferred for a focused pass:

- ~~**Chore selection menu**~~ — **discarded** at the user's request. The "prevent two companions on the same chore" requirement it carried was kept and implemented differently: a **claim registry** in `ChoreAI` + a **hover tooltip** that shows *"&lt;name&gt; is already working here."* on a claimed station (and refuses a second worker). No in-world UI panel.
- ~~**Haul choreography**~~ — **reverted at the user's request.** Haul stays a radius-sweep (companion holds its post by the chest and pulls items in range straight in); only the chest-open-on-deposit was kept. The walk-to-item + pickup-VFX version was removed. See [Ally-Chores.md](Ally-Chores.md).
- ~~**Replace vanilla Dvergr chatter**~~ — **done.** `NpcTalk` is disabled on the freed state; `DvergrCompanion.AnnounceCapability()` speaks stance+caste "what I can do" lines on recruit and on stance change. See [Ally-Commands.md](Ally-Commands.md).
- **Stance selection menu** — *decided against for now*: kept as the lightweight `E` cycle (consistent with discarding the chore menu). Revisit only if cycling proves clumsy.
- **Farm replanting** (harvest-only shipped).
- **Persistence**: chore assignment now **persists** (ZDO kind + target world position, re-resolved by proximity on spawn, owner-gated, resumes after relog/zone reload). **Stance** is still in-memory only.

## Phase 8 — Polish & Release

- Balance pass across all four castes and duel matchups.
- Vanilla-asset compliance audit against [Technical-Constraints.md](Technical-Constraints.md).
- Thunderstore packaging (manifest, README, icon — icon/readme images are packaging metadata, not in-game assets, so don't conflict with the vanilla-assets-only rule).
- Playtest pass, including multiplayer/server-authority verification.

## Notes

- Order is deliberate: mechanics before narrative wiring (Phase 5 comes after the events it consumes already exist), and the riskiest/most novel system (duels) is built only after the simpler systems are proven.
- Roster expansion (Phase 7) is intentionally late — better to get the full loop (recruit → level → chore → duel → narrate) right once on a single caste before multiplying it by four.
