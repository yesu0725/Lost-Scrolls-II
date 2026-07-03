# Companion Totems

Convert a recruited companion into a carriable **GoblinTotem** item and summon it
back later. Lets a player "bank" allies they don't want following them around —
their level (and XP progress) is preserved on the item.

All vanilla assets: the totem is the stock `GoblinTotem` trophy, the ritual
reagent is the stock `Wisp` item, and the ritual station is the vanilla
**Incinerator** (Obliterator). No new prefabs, models, or effects.

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
| Item name + description | Each totem gets its **own `SharedData` clone** (shallow copy of GoblinTotem's) with name "Communion Totem" + a purpose description — real Fuling Totems keep the vanilla data. Re-applied on load via two `ItemDrop.LoadFromZDO` postfixes (the loaded item is rebuilt from the prefab, resetting its shared). |
| Stat block | Postfix on the static `ItemDrop.ItemData.GetTooltip` appends the per-companion name/caste/level/owner below the description. |
| No stacking | The per-instance `SharedData` clone has `m_maxStackSize = 1` — Valheim stacks by shared name and ignores `m_customData`, so without this two companions would merge and one would be lost. Because the cap is on the *clone*, real Fuling Totems still stack normally. |
| VFX | Reused vanilla effect prefabs (no new assets), resolved from `ZNetScene` with graceful fallback: a soul-dissipation burst per companion on seal, a spawn burst on summon. |
| Summon hook | Prefix on `Humanoid.UseItem` (the single path for hotbar use and inventory "Use", for any item type). Spawns via the existing `CommunionService.SpawnRecruited(caste, level, owner, pos, xp)`. |
| Aim point | Raycast from the camera (50 m) against terrain/pieces/solids; falls back to a point in front of the player. |

## Open items / caveats

- **Unverified in a live session** — see [Testing.md](Testing.md) §12.
- **Multiplayer**: the sealing runs on the activating client using the same
  `ClaimOwnership` pattern as the `DE_Duel` replication. Single-player is fully
  authoritative; MP (incinerator owned by another client, companions loaded on a
  different peer) needs a two-client verification pass.
- Sealing only takes **Follow-stance, free** companions (not on a chore, not in a
  duel) — matching "command them to follow first."
