# Ally Leveling

## Why Not Vanilla 1/2/3-Star

Vanilla's star-tier system is too shallow for a persistent RPG companion — it's binary loot luck, not earned progression. Dvergr allies need their own XP/level track, independent of the vanilla star system.

## Proposed Design

- **XP storage**: tracked as custom int/float data on the ally's own ZDO (mirrors how ServerGuide stores player progress in `m_customData`, but scoped to the creature instead of the player).
- **XP sources**: kills the ally participates in near the player, and chores completed (see [Ally-Chores.md](Ally-Chores.md)).
- **Level-up effect**: scales existing vanilla stat fields the ally already has — health, damage multiplier — rather than granting new gear or abilities. Keeps this 100% vanilla-assets-only (see [Technical-Constraints.md](Technical-Constraints.md)).
- **Per-caste scaling**: leveling should favor each caste's role from Lore.md — Rogue scales damage/speed, Fire/Ice Mage scale spell damage or debuff potency, Support Mage scales heal/buff strength. Exact curves are a balancing task for Phase 3, not decided here.
- **Level-up flavor**: fires a `dvergr_level_up` event so ServerGuide can show a raven/message popup with caste-appropriate flavor text. Visual feedback should reuse a vanilla particle/FX (e.g. an existing buff VFX) rather than anything custom.

## Implementation (Phase 3)

- Companion XP/level persisted on the companion's own ZDO (`DE_Level`, `DE_Xp`), not the player's.
- **Level cap: 10** (`DvergrCompanion.MaxLevel`). Past it, `AddXp` early-returns so XP stops accumulating and the hover reads `Lv 10 (max)`.
- **Rising XP curve** (`DvergrCompanion.XpToNextByLevel`, XP to advance from each level):

  | Level → next | XP | Cumulative |
  |---|---|---|
  | 1→2 | 100 | 100 |
  | 2→3 | 250 | 350 |
  | 3→4 | 450 | 800 |
  | 4→5 | 700 | 1,500 |
  | 5→6 | 1,000 | 2,500 |
  | 6→7 | 1,400 | 3,900 |
  | 7→8 | 1,900 | 5,800 |
  | 8→9 | 2,500 | 8,300 |
  | 9→10 | 3,200 | 11,500 |

- **XP per kill = biome cap × (creature HP ÷ biome reference HP)**, clamped to `[0.25, 1.0]` (`KillXpPatch.BiomeScaledXp`). The biome is read via `Heightmap.FindBiome` at the death position; the creature's HP is read **live** from `Character.GetMaxHealth()` at kill time, so it's always exact for the build — including star-creature HP multipliers — with no hardcoded per-creature table. Any creature at or above its biome's reference HP (the toughest common creature, plus minibosses/bosses) earns the full cap; weaker ones scale down to a floor of 25% of the cap. Awarded to any recruited companion within 20 m of the death (proximity model — no check that it landed the kill).

  | Biome | Cap XP | Reference HP |
  |---|---|---|
  | Meadows | 5 | 25 (Greyling) |
  | Black Forest | 12 | 150 (Greydwarf Brute) |
  | Swamp | 22 | 150 (Draugr Elite) |
  | Mountain | 35 | 100 (Fenring/Wolf) |
  | Plains | 50 | 80 (Fuling) |
  | Mistlands | 75 | 200 (Seeker) |
  | Ashlands | 100 | 200 (Charred Warrior) |
  | *(unmapped → Mountain)* | 35 | 100 |

- **Player kills** grant `PlayerKillXp = 50` flat (Plains-tier; players have no biome-creature HP to scale against), detected via `Character.IsPlayer()`. Non-player Players-faction deaths (tamed wolves, other allies) grant nothing.
- Stat scaling via vanilla `Character.SetLevel(level)` on level-up — reuses vanilla's star-tier scaling rather than reimplementing it. This already scales **health and damage for every caste** (including the mages' staff/spell damage, since the level multiplier applies to their attacks).
- **Per-caste identity bonus** (`DvergrCompanion.ApplyCasteBonus`, layered on top of `SetLevel`): aggressive front castes gain mobility, protective/backline castes gain durability. Damage growth is already covered by `SetLevel`, so this is a secondary flavor trait on the two stat knobs verified safe to touch (`m_runSpeed`/`m_speed`/`m_walkSpeed`, `SetMaxHealth`).

  | Caste | Bonus | Per level | At Lv 10 |
  |---|---|---|---|
  | Rogue | move speed | +3% | +27% |
  | Fire Mage | move speed | +3% | +27% |
  | Ice Mage | max health | +4% | +36% |
  | Support Mage | max health | +4% | +36% |

  Applied fresh each call after `SetLevel` (which resets the scaled stats first), so it never compounds across level-ups or on relog-restore. Base speeds are captured once from the untouched prefab.
- `dvergr_level_up` event is logged and (Phase 5) raised to ServerGuide.
- **Custom level badge instead of vanilla stars**: `SetLevel(level)` still drives stat scaling, but vanilla's `EnemyHud` only has `m_level2`/`m_level3` star rects (verified against the assembly) and so can never show more than 2 stars — useless across 1–10. So `CompanionLevelBadgePatch` (a) appends a compact gold `★N` badge to the companion's floating name via a `Character.GetHoverName` postfix, and (b) hides vanilla's star rects on companion huds each frame via an `EnemyHud.UpdateHuds` postfix. Text + rect-toggle only, no custom assets. The numeric `Lv X` in the crosshair hover text remains as the detailed readout.
- **Visible indicator**: `DvergrCompanion.XpPercentToNextLevel` is surfaced via the hover-text patch (see [Ally-Recruitment.md](Ally-Recruitment.md)), e.g. `Companion · Lv 7 (42% to next)`, switching to `Lv 10 (max)` at the cap.

## Open Questions (resolve during/after in-game verification)

- Whether the biome XP values / level curve feel right in play. The whole curve or the biome table can be scaled with a single multiplier if maxing is too fast or too grindy.
- Whether the kill-XP simplification (any nearby companion gets XP, not just the one that helped) needs tightening — and the related player-kill case, where a player dying near their own companions still feeds them XP (proximity model has no killer attribution).
- Whether vanilla's 2-star render ceiling is acceptable, or levels 4–10 want a different visual treatment beyond the numeric `Lv X`.
- Per-caste leveling differentiation (Rogue/Fire/Ice/Support scaling differently) — deferred to Phase 7 roster expansion.
- Whether allies can lose levels on death/defeat in a duel (current default per [Duel-Arena.md](Duel-Arena.md): no, duels are non-lethal and only grant XP, never remove it).
