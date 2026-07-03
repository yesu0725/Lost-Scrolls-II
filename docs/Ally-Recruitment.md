# Ally Recruitment — The Communion Rite

## Concept

Recruiting a Dvergr ally is performed in-world as the **Communion Rite**, not a generic "tame with food" mechanic. This is the gameplay expression of the Lore.md premise: you are freeing a corrupted Dvergr, not domesticating a wild animal.

## Recruitable Roster

The four vanilla Dvergr creature types are all recruitable (see [Technical-Constraints.md](Technical-Constraints.md) for exact prefab names, to be confirmed during implementation):

- Dvergr Rogue
- Dvergr Fire Mage
- Dvergr Ice Mage
- Dvergr Support Mage

Build order introduces these one at a time — see [Development-Phases.md](Development-Phases.md). Rogue first (MVP), remaining three in Phase 7. The **narrative** arc is Rogue → Fire → Ice → Support (the ServerGuide story chains follow it), but recruitment is **not** mechanically gated to that order — a player may free the castes in any order they meet them.

## The corruption awakens (message on aggravation)

Vanilla Dvergr are neutral until attacked. Lost Scrolls II gives that a diegetic, allegorical reason (see [Lore.md](Lore.md) → "The corruption within"): the corruption sleeps *inside* every unfreed Dvergr and **wakes when the Dvergr is roused**, which is why it turns on the player. When an unrecruited Dvergr first becomes aggravated, a short center-screen message names what's happening (one of a small rotating set, e.g. *"Something old stirs in it — the corruption was never truly gone. Roused, it turns on you."*).

**Implementation** (`src/Companions/CorruptionAwakensPatch.cs`):

- A `BaseAI.SetAggravated` **prefix** — the single point every aggravation flows through (direct hits via `OnDamaged`, and the area `AggravateAllInArea` sweep). Fires only on a genuine `false→true` transition (the pre-call `m_aggravated` is still readable in the prefix).
- Only for a **real unfreed Dvergr** (`m_faction == Dverger` and no `DvergrCompanion` component — freed allies are faction `Players` *and* now non-aggravatable, so they never reach here).
- Only when the **local player is nearby** (≤ 40 m) and **throttled** to once per ~6 s, so a whole camp waking together shows one line, not one per Dvergr.

**Needs in-game verification**: that the message fires when you provoke a Dvergr, reads well, and doesn't spam when several aggravate at once.

> **Dropped features (superseded by the above):** an earlier pass added a **caste recruit-order gate** (`RecruitProgress`, `DE_RecruitProgress`) and **pre-corrupted camps** (`CorruptionZones` / `CorruptedSpawnPatch`, hostile-on-sight Dvergr in seed-chosen camps). Both were **removed at the user's request** in favor of this simpler, more on-theme "corruption awakens on provocation" message. Don't reintroduce them.

**Caste is detected at recruit from the Dvergr's equipped staff** (`CommunionService.DetectCaste`), not from the GameObject name. The spawned mage is just `DvergerMage`; the element lives on its **staff** — `DvergerStaffFire` → Fire, `...Ice`/frost → Ice, `...Support`/`Heal`/`Nova`/`Blocker`/`Shield` → Support; no staff → melee **Rogue**. Detection order: all weapon slots **drawn and sheathed** (`GetCurrentWeapon`, `m_rightItem`/`m_leftItem`, and the **`m_hiddenRightItem`/`m_hiddenLeftItem`** sheathed slots) → inventory scan → GameObject-name fallback. The sheathed slots matter because a Dvergr that isn't mid-attack puts its staff away, so `GetCurrentWeapon()` is null at hover-recruit time — checking only that slot silently tagged every mage as Rogue. Per-instance (the creature's own items, not the prefab's random pool), and every signal is logged at recruit (`[recruit] caste … from weapon slot …`, or `[recruit] No staff detected …` which dumps every slot + inventory) so any remaining miss is correctable from the log.

## Proposed Recruit Flow

1. **Subdue, don't kill** — reduce the target Dvergr to a low-HP threshold without landing the killing blow.
2. **Perform Communion** — while subdued, interact with the Sword of Truth equipped (or consume a Communion Scroll) to trigger the rite.
3. **Resolution** — on success, the Dvergr's faction flips to player-allied, it gains a `DvergrCompanion` behavior component, and it begins following the player. On failure (if we want a failure state), the Dvergr flees or resets to hostile.

Open question for design during Phase 2 implementation: should Communion have a success chance, a resource cost (scroll consumed), or both? Default assumption going in: scroll consumed, no RNG failure — keeps it deterministic and friendly to first-time players, revisit if it feels too easy in playtesting.

## Mechanical Notes

- No new creature models — recruiting only changes faction/AI/behavior on the existing vanilla prefab.
- Faction change and companion state must be ZDO-persisted so the ally survives server restarts and reconnects (same persistence pattern ServerGuide uses for player state, but on the creature's ZDO instead of player `m_customData`).
- Recruitment should fire a `dvergr_recruited` event for ServerGuide to pick up narratively — see [ServerGuide-Integration.md](ServerGuide-Integration.md).

### Bug fix: recruited Dvergr kept attacking the player

**Root cause (verified by decompiling the real game assembly, not guessing):** Dvergr are *neutral until aggravated*. Attacking one sets `m_aggravated = true` on its AI, and it is **that flag — not faction — that drives its hostility**. So flipping `m_faction` to `Players` did nothing to calm it, and neither would vanilla `MonsterAI.MakeTame()` (decompiled: it only does `SetTamed(true)` + `SetAlerted(false)` + clears the target; it never touches `m_aggravated`). The freed Dvergr kept hunting whoever aggravated it, especially when communed mid-fight.

The first attempted fix (clearing `m_targetCreature`/alerted/hunt) addressed the symptom but not the aggravated flag, so it didn't hold. `CommunionService.TryRecruit` now calls `ai.SetAggravated(false, BaseAI.AggravatedReason.Damage)` — the actual fix — plus `ai.SetTarget(null)` (proper public method, also clears `m_targetStatic`), `SetAlerted(false)`, and `SetHuntPlayer(false)` to stop the current engagement immediately. The `SetAggravated`/`SetTarget` signatures and the `AggravatedReason.Damage` value were all confirmed against the real assembly's metadata, not assumed. **Still needs in-game confirmation** of the final behavior, but the cause is now correctly identified rather than guessed.

### Bug fix: ally attacked the owner when the owner attacked a wild Dvergr

**Symptom:** with a recruited companion and an unrecruited Dvergr both in range, attacking the wild Dvergr made the companion attack **the owner** (as well as the wild Dvergr).

**Root cause (verified by decompiling `BaseAI`):** when any Dvergr is hit it calls `BaseAI.AggravateAllInArea`, which sweeps every nearby `BaseAI` and calls `SetAggravated(true)` on each one whose `IsAggravatable()` is true. `IsAggravatable()` just returns the **`m_aggravatable` prefab flag**, which is still `true` on our recruited Dvergr — so the freed ally got re-aggravated by the wild one's damage event, and an aggravated (normally-neutral) Dvergr turns hostile to players, including its owner. The earlier recruit fix cleared `m_aggravated` **once**, but nothing stopped it being re-set.

**Fix:** `ApplyFreedState` now sets **`ai.m_aggravatable = false`** on the freed ally (after the existing `SetAggravated(false)` clear — order matters, since `SetAggravated` early-outs once the flag is false). A non-aggravatable ally is skipped by `AggravateAllInArea` entirely and can never re-enter the hostile state, while still fighting wild Dvergr through normal enemy targeting. Re-applied on every spawn via the restore path, so it holds across relog. See Testing.md §2c.

### Bug fix: freed allies attacked the player's build pieces

**Symptom:** a recruited Dvergr attacked **player-built structures** (walls, benches, etc.).

**Root cause:** Dvergr spawn with `MonsterAI.m_attackPlayerObjects = true`, which makes their AI treat `StaticTarget` structures as valid targets. The recruit faction-flip to `Players` never clears it, so a freed Dvergr kept smashing the base.

**Fix:** `ApplyFreedState` sets `ai.m_attackPlayerObjects = false` and clears `ai.m_targetStatic` (drops any structure it's already locked onto). Applied on recruit and re-applied on the restore path, so it holds across relog. See Testing.md §2d.

### Bug fix: recruited Dvergr reverted to uncommuned after relog

**Root cause:** recruitment only changed *runtime* state — `m_faction`, the AI's aggravated flag, and the added `DvergrCompanion` component — none of which vanilla persists. Only our own `DE_Recruited` ZDO flag survived a logout. So on relog/server-restart/chunk-reload the Dvergr re-spawned as a plain hostile creature at its (still low) recruit-time HP, and its hover showed `[G] Communion` again.

**Fix:** `CompanionRestorePatch` (a `MonsterAI.Start` postfix — runs once per spawn, after `ZNetView` has registered the ZDO) calls `CommunionService.RestoreCompanion`, which re-reads `DE_Recruited` and, if set, reconstructs the freed state (player faction, calmed AI via the shared `ApplyFreedState`, and re-attaches `DvergrCompanion`, whose `Awake` restores caste/level/XP from the ZDO). The `DE_Recruited` flag is now the persistent source of truth the docs' Mechanical Notes always called for. **Still needs in-game confirmation**, but the persistence gap is now closed in code.

## Open Questions (resolve before/during Phase 2)

- Exact subdue-threshold mechanic (HP %? a stagger/knockdown state?).
- Whether different castes need different recruit conditions (e.g., Support Mage requires a prior quest step per Lore.md's "rite-keeper" angle).

## Discoverability & Feedback (feature add)

Two indicators were added so the recruit flow is discoverable without reading docs, both via a `Character.GetHoverText()` Harmony postfix (`src/Companions/HoverTextPatch.cs`):

- **Recruit hint**: hovering a subdued, unrecruited Dvergr (per `CommunionService.IsSubduedDvergr`) appends a `[<key>] Communion` line to its hover tooltip, using whatever key `CommunionKey` is currently bound to.
- **Companion indicator**: hovering an already-recruited companion appends `Companion · Lv X (Y% to next)` instead — see [Ally-Leveling.md](Ally-Leveling.md) for where the level/XP% comes from.

**Needs in-game verification**: `GetHoverText()`'s exact return shape (single line? already ends in a newline? rich-text already in use for something else?) was not checked before appending — it might look wrong even though the patch itself binds without error.
