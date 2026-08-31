# Companion Totems

Convert a recruited companion into a carriable **GoblinTotem** item and summon it
back later. Lets a player "bank" allies they don't want following them around —
their level (and XP progress) is preserved on the item.

All vanilla assets: the totem is the stock `GoblinTotem` trophy, the ritual
reagent is the stock `Wisp` item, and the ritual station is the vanilla
**Incinerator** (Obliterator). No new prefabs, models, or effects.

There are **two ways to seal**, producing an identical totem:

| | **Incinerator ritual** | **Dead Raiser rite** |
|---|---|---|
| Where | at an Obliterator | anywhere |
| Cost | 1 Wisp per companion | 1 Wisp, 1 equipped Dead Raiser |
| Gate | none | Blood Magic 20+ |
| Batch | several companions at once | one at a time |
| Time | the vanilla 5–7 s lever animation | 5 s at Blood Magic 20, down to 2 s at 100 |

## Sealing a companion (creating a totem)

1. Set the companions you want to seal to **Follow** and gather them at an
   Incinerator (within ~15 m).
2. Put **Wisps** into the incinerator — one Wisp per companion you want to seal
   (1:1). Pull the lever.
3. During the vanilla lighting animation (a 5–7 s dramatic pause) the ritual
   resolves: `N = min(Wisps, following companions)`. Each of the N nearest
   companions is sealed and one Wisp is consumed per seal.
4. The resulting **Communion Totems** (stock `GoblinTotem` items, renamed) land in
   the incinerator's own slots (like any incineration result) — take them into your
   inventory.

If there are **fewer Wisps than companions**, the surplus companions are left
untouched (and surplus Wisps are left untouched too). If neither Wisps nor
following companions are present, the incinerator works exactly like vanilla.

Each totem is renamed to **"Communion Totem"** with a purpose-based description
(not the stock "Fuling Totem"), and its tooltip carries the sealed companion's
identity — name, caste, level, and who it was bound to. A **soul-dissipation VFX**
plays over each companion as it is sealed.

## Sealing in the field (the Dead Raiser rite)

The portable counterpart, for a blood-magic practitioner who does not want to walk
an ally back to an Obliterator.

**Requirements** — all four, and all re-checked every frame of the channel:

- a **Dead Raiser** (`StaffSkeleton`) **equipped**,
- at least one **Wisp** in the pack (consumed 1:1, exactly like the Incinerator
  ritual — the wisp is what the soul is bound into),
- **Blood Magic ≥ `SealMinBloodMagic`** (default 20),
- the target is **your own** companion, in **Follow** stance, and free (no chore,
  no duel, not feral).

**Doing it** — hold the vanilla **Block** button with the crosshair on the
companion. The channel length scales with skill: `SealChannelMaxSeconds` (5 s) at
the minimum, falling to `SealChannelMinSeconds` (2 s) at
`SealFullSpeedBloodMagic` (100). The crosshair tooltip shows the exact figure
once the staff is equipped, and the accelerating **Wishbone ping** (the same cue
the Communion Rite uses) marks progress.

**It can fail** — release Block past a short grace (a dodge roll shares the
button and is forgiven), stray past `SealMaxDistance`, take a hit, unequip the
staff, or lose your last wisp. Nothing is consumed on a failure and the companion
is unharmed.

On success the wisp is consumed and a **Communion Totem** goes into your pack —
or drops at your feet if the pack is full, because at that moment the totem *is*
the companion and must never be destroyed.

**Why it is a separate class from the Communion Rite** — `SealingRite` and
`CommunionRite` share only the held-Block input idiom and the ping effect. Every
requirement, fail condition, message and outcome differs, and the two are told
apart purely by what the crosshair is on: an unrecruited subdued Dvergr begins a
Communion, your own Follow-stance companion begins a Sealing. Each helper
early-outs on the other's target, so they can never both fire. With **no staff
equipped** the sealing path is silent — blocking beside an ally in a fight must
never nag.

## Summoning a companion (using a totem)

Place the totem on your hotbar and **press its slot number** (or right-click →
Use in the inventory). The companion spawns **where you are looking**, owned by
the summoning player, at its sealed level/XP and with its name intact, with a
**spawn-burst VFX**. The totem is consumed.

## How it works (implementation)

| Concern | Approach |
|---|---|
| Sealing hook | Harmony prefix on `Incinerator.OnIncinerate` (the Switch callback, fires on the activating client). Returns `false` to take over only when Wisps **and** following companions are present; otherwise defers to vanilla. See [IncineratorConversionPatch](../src/Companions/IncineratorConversionPatch.cs). |
| Drama timing | The takeover mirrors the vanilla `RPC_AnimateLever` + lever effects, waits the same `Random.Range(m_effectDelayMin, m_effectDelayMax)` (5–7 s) window, then resolves — matching the base-game lighting animation. |
| Per-companion data | Stored in `ItemDrop.ItemData.m_customData` (a persisted `Dictionary<string,string>` — survives save/reload/drop/trade) plus `m_crafterName`. See [TotemConversionService](../src/Companions/TotemConversionService.cs). |
| Item name + description | Each totem gets its **own `SharedData` clone** (shallow copy of GoblinTotem's) with name "Communion Totem" + a purpose description — real Fuling Totems keep the vanilla data. Re-applied on **every** load path, because a loaded item is always rebuilt from the prefab and its shared resets to "Fuling Totem" — see the row below. |
| Stat block | Postfix on the static `ItemDrop.ItemData.GetTooltip` appends the per-companion name/caste/level/owner below the description. |
| No stacking | The per-instance `SharedData` clone has `m_maxStackSize = 1` — Valheim stacks by shared name and ignores `m_customData`, so without this two companions would merge and one would be lost. Because the cap is on the *clone*, real Fuling Totems still stack normally. |
| Surviving a relog | The clone must be re-applied on **three** load paths, not one. World drops and container/stand slots come back through the two `ItemDrop.LoadFromZDO` overloads; the **player's inventory and every `Container`** do not — `Inventory.Load` rebuilds each item by instantiating its prefab. Missing that third path was the "my sealed Dvergr is a Fuling Totem again" bug, and the stack cap returning with the name was the dangerous half of it. The re-apply has to run **before** the stacking decision, so it hangs off a **prefix** on the private `Inventory.AddItem(ItemData, int, int, int)` — the first point at which the item is both identifiable (its `m_customData` is set) and still un-stacked — with a postfix on `Inventory.Load` as a catch-all. |
| VFX | Reused vanilla effect prefabs (no new assets), resolved from `ZNetScene` with graceful fallback: a soul-dissipation burst per companion on seal, a spawn burst on summon. |
| Summon hook | Prefix on `Humanoid.UseItem` (the single path for hotbar use and inventory "Use", for any item type). Spawns via the existing `CommunionService.SpawnRecruited(caste, level, owner, pos, xp)`. |
| Aim point | Raycast from the camera (50 m) against terrain/pieces/solids; falls back to a point in front of the player. |
| Field sealing | [SealingRite](../src/Companions/SealingRite.cs), a component on the plugin GameObject driven from `Plugin.Update`'s held-Block branch alongside `TryBeginCommune`. Equipped-staff and wisp checks resolve by **shared name** from ObjectDB (an item loaded from a ZDO can have a null `m_dropPrefab`, which would make a genuinely-equipped staff invisible). Blood Magic is read via `Skills.GetSkillLevel(Skills.SkillType.BloodMagic)`. The totem itself is built by the same `TotemConversionService.CreateTotem`, so both paths produce byte-identical items. |

## Open items / caveats

- **Unverified in a live session** — Incinerator sealing: [Testing.md](Testing.md)
  §12 (passed 2026-07-03). Dead Raiser sealing: §24 (**unverified**).
- **Multiplayer**: the sealing runs on the activating client using the same
  `ClaimOwnership` pattern as the `DE_Duel` replication. Single-player is fully
  authoritative; MP (incinerator owned by another client, companions loaded on a
  different peer) needs a two-client verification pass.
- Sealing only takes **Follow-stance, free** companions (not on a chore, not in a
  duel) — matching "command them to follow first."
