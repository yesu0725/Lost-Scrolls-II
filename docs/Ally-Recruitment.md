# Ally Recruitment — The Communion Rite

> **The Block button now drives two rites.** Holding Block on an *unrecruited,
> subdued* Dvergr begins the Communion Rite described here; holding it on *your
> own Follow-stance companion* (with a Dead Raiser equipped and a Wisp in your
> pack) begins the **sealing** rite instead — see
> [Companion-Totems.md](Companion-Totems.md). They are separate classes and each
> early-outs on the other's target, so they can never both fire, and the sealing
> path is silent unless the staff is actually equipped.


## Concept

Recruiting a Dvergr ally is performed in-world as the **Communion Rite**, not a generic "tame with food" mechanic. This is the gameplay expression of the Lore.md premise: you are freeing a corrupted Dvergr, not domesticating a wild animal.

## Recruitable Roster

The four vanilla Dvergr creature types are all recruitable (see [Technical-Constraints.md](Technical-Constraints.md) for exact prefab names, to be confirmed during implementation):

- Dvergr Rogue
- Dvergr Fire Mage
- Dvergr Ice Mage
- Dvergr Support Mage

Build order introduces these one at a time — see [Development-Phases.md](Development-Phases.md). Rogue first (MVP), remaining three in Phase 7. The **narrative** arc is Rogue → Fire → Ice → Support (the ServerGuide story chains follow it), but recruitment is **not** mechanically gated to that order — a player may free the castes in any order they meet them.

### Finding a Dvergr before the Mistlands

Corrupted Dvergr naturally spawn only in the Mistlands, but the Communion Rite itself works on **any** Dverger/DvergerMage in the world, however it got there — it's not Mistlands-gated in code. The Quest pack's bundled `guidance.bogwitch-rite.yaml` uses this to give players an early taste: the **Bog Witch** trader offers a weekly rite that spawns a wild, untamed Dverger in the Swamp after killing a couple of Draugr Elite (Rogue caste, matching the intended discovery order), and a second rite — unlocked after the first — spawns a random-caste DvergerMage after a night hunt for Wraith. See [ServerGuide-Integration.md](ServerGuide-Integration.md) and [Testing.md](Testing.md) §22. This is Quest-pack guidance content only; no change to the Communion Rite or recruit mechanics above.

## The corruption awakens (message on aggravation)

Vanilla Dvergr are neutral until attacked. Lost Scrolls II gives that a diegetic, allegorical reason (see [Lore.md](Lore.md) → "The corruption within"): the corruption sleeps *inside* every unfreed Dvergr and **wakes when the Dvergr is roused**, which is why it turns on the player. When an unrecruited Dvergr first becomes aggravated, a short center-screen message names what's happening (one of a small rotating set, e.g. *"Something old stirs in it — the corruption was never truly gone. Roused, it turns on you."*).

**Implementation** (`src/Companions/CorruptionAwakensPatch.cs`):

- A `BaseAI.SetAggravated` **prefix** — the single point every aggravation flows through (direct hits via `OnDamaged`, and the area `AggravateAllInArea` sweep). Fires only on a genuine `false→true` transition (the pre-call `m_aggravated` is still readable in the prefix).
- Only for a **real unfreed Dvergr** (`m_faction == Dverger` and no `DvergrCompanion` component — freed allies are faction `Players` *and* now non-aggravatable, so they never reach here).
- Only when the **local player is nearby** (≤ 40 m) and **throttled** to once per ~6 s, so a whole camp waking together shows one line, not one per Dvergr.

**Needs in-game verification**: that the message fires when you provoke a Dvergr, reads well, and doesn't spam when several aggravate at once.

> **Dropped features (superseded by the above):** an earlier pass added a **caste recruit-order gate** (`RecruitProgress`, `DE_RecruitProgress`) and **pre-corrupted camps** (`CorruptionZones` / `CorruptedSpawnPatch`, hostile-on-sight Dvergr in seed-chosen camps). Both were **removed at the user's request** in favor of this simpler, more on-theme "corruption awakens on provocation" message. Don't reintroduce them.

**Caste is detected at recruit from the Dvergr's equipped staff** (`CommunionService.DetectCaste`), not from the GameObject name. The spawned mage is just `DvergerMage`; the element lives on its **staff** — `DvergerStaffFire` → Fire, `...Ice`/frost → Ice, `...Support`/`Heal`/`Nova`/`Blocker`/`Shield` → Support; no staff → melee **Rogue**. Detection order: all weapon slots **drawn and sheathed** (`GetCurrentWeapon`, `m_rightItem`/`m_leftItem`, and the **`m_hiddenRightItem`/`m_hiddenLeftItem`** sheathed slots) → inventory scan → GameObject-name fallback. The sheathed slots matter because a Dvergr that isn't mid-attack puts its staff away, so `GetCurrentWeapon()` is null at hover-recruit time — checking only that slot silently tagged every mage as Rogue. Per-instance (the creature's own items, not the prefab's random pool), and every signal is logged at recruit (`[recruit] caste … from weapon slot …`, or `[recruit] No staff detected …` which dumps every slot + inventory) so any remaining miss is correctable from the log.

## Recruit Flow — the channeled rite

Recruiting is a **channeled struggle**, not an instant keypress (`src/Companions/CommunionRite.cs`). The corruption fights to keep its hold, so freeing a Dvergr takes nerve and a steady hand.

1. **Subdue, don't kill** — reduce the target Dvergr to `SubdueHealthThreshold` (≤20% HP) without landing the killing blow.
2. **Begin the rite** — **hold the Block button** while aiming at the subdued Dvergr (the crosshair hint reads `Hold [<Block>] — Communion`, using the player's actual Block binding). A center-screen line opens the rite. The rite rides on **Block on purpose**: you keep your shield up and can still block and dodge through the vulnerable channel (dodge shares the Block button and doesn't require releasing it; a brief release is forgiven for `ReleaseGraceSeconds` ≈ 0.5 s so a roll won't shatter the rite). Recruiting is *no longer* on the `G`/`CommunionKey` — that key is now only Feed on an existing companion.
3. **Hold through the struggle** — for `CommunionChannelSeconds` (default 5 s) the corruption **writhes** at intervals (`LashInterval`, ~1.25 s): a rotating dramatic center-message beat ("The corruption writhes — hold fast.", etc.). **Deliberately minimal — no progress bar and no VFX during the channel** (an earlier floating-name/hover meter and per-lash smoke bursts were removed at the user's request).
4. **Resolution**
   - **Success** — the channel completes → the existing `CommunionService.TryRecruit` runs (faction flip, `DvergrCompanion` attached, follows the player), a single small spawn poof plays (`PlaySummonVfx`), and the join message shows.
   - **Failure** — the rite **breaks** and the shadow reclaims the Dvergr (message only, no VFX): it re-aggravates (turns hostile again) and must be re-subdued/survived before another attempt. Fail conditions: **lower your guard** (release Block past the grace window), **stray > `CommunionMaxDistance`** (4 m), **take damage** (if `CommunionBreakOnDamage`, default on — a *blocked* hit deals no damage, so shielding up survives it), or the target dies/unloads.

**Input note**: the rite only *reads* the Block button (`ZInput.GetButton("Block")`) — it never suppresses input, so blocking (shield raise/animation) and dodging behave exactly as vanilla underneath the channel. Begin fires whenever Block is **held** (`CommunionRite.BlockHeld`, not just the press down-edge — you often block continuously through the fight, so the Dvergr can cross the subdue threshold with Block already down) but only when the crosshair is on a subdued, unrecruited Dvergr, so ordinary blocking in combat is unaffected; `TryBeginCommune` no-ops while a rite is already active.

**MP safety**: a `DE_Communing` ZDO flag (`CommunionRite.ZdoKeyCommuning`) locks the target while a rite is in progress — a second player can't channel the same Dvergr, and the channeling client claims the ZDO so the lock and any fail-time re-aggravation replicate. The flag is cleared on every end path (success/fail/cancel).

**Config** (`Recruitment` section): `CommunionChannelSeconds`, `CommunionMaxDistance`, `CommunionBreakOnDamage`.

**Needs in-game verification**: the rite reliably starts on held Block over a subdued Dvergr, the lash beats fire and don't spam, each fail condition actually breaks the rite (esp. the re-aggravation on failure and the take-damage break inside a camp fight), and the MP lock blocks a second channeler. The Sword-of-Truth / Communion-Scroll item gate is still deferred (no resource cost or RNG on top of the channel yet).

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

- **Recruit hint**: hovering a subdued, unrecruited Dvergr (per `CommunionService.IsSubduedDvergr`) appends a `Hold [<Block>] — Communion` line to its hover tooltip, resolving `<Block>` to the player's actual Block binding via `ZInput.GetBoundKeyString("Block")` (localized in-code, with a plain "Block" fallback). There is no progress meter — the channel gives feedback through center-message beats only.
- **Companion indicator**: hovering an already-recruited companion appends `Companion · Lv X (Y% to next)` instead — see [Ally-Leveling.md](Ally-Leveling.md) for where the level/XP% comes from.

**Needs in-game verification**: `GetHoverText()`'s exact return shape (single line? already ends in a newline? rich-text already in use for something else?) was not checked before appending — it might look wrong even though the patch itself binds without error.
