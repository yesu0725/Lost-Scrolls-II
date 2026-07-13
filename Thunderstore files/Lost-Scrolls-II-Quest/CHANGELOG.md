# Changelog

## 0.3.0

- Updated to depend on **Lost Scrolls II 0.3.0**, which adds the **competitive suite** — 1v1 duel rankings, party duels, party rankings, and tournaments — plus player-icon map pins and companion death markers. See the base mod's changelog for detail.
- **New guidance files** bundled into `BepInEx/config/ValheimServerGuide/`:
  - `guidance.rankings.yaml` — duel-ladder + party-ladder reference pages and rank milestones (`category: Rankings`).
  - `guidance.tournaments.yaml` — tournament join/pairing announcements and the champion prize bundle (`category: Tournaments`).
- Story and Companion Handbook content unchanged.

> **Note:** the dynamic values in the new rankings/tournament messages (rank, rating, opponent, round, etc.) require a **ServerGuide build that includes the Lost Scrolls II competitive template variables**. With an older ServerGuide the guidance still fires, but those `{...}` placeholders show literally. Update ServerGuide alongside this pack for full templating.

## 0.2.0

- Updated to depend on **Lost Scrolls II 0.2.0**, which adds the **companion inventory system** (per-companion 4×2 pack, item pickup, food/mead consumption, weight/encumbrance, death-drop, totem carry-over, wood-portal cargo block, ComfyQuickSlots compatibility). See the base mod's changelog for detail.
- **Companion Handbook** (`guidance.companions.yaml`) updated for the inventory system: a new **"Your Companion's Pack"** guidance (opening the 8-slot pack with `[Y]`, auto-pickup, self-feeding, health/resist meads, the 150 weight cap, death-drop and totem carry-over), and corrected the `[Y]` key description in "Commanding Your Companion" (it now opens the pack — which holds the rename field — rather than only renaming).
- Story content unchanged.

## 0.1.0

Initial release of the complete "Quest" pack.

- A content pack that pulls in the **Lost Scrolls II** gameplay mod as a dependency and adds its full in-game narrative, ready for single player — no manual guidance-file setup.
- Requires **Valheim ServerGuide** (`TaegukGaming-ValheimServerGuide-0.7.1`), which delivers the story and Companion Handbook.
- Ships two guidance files into `BepInEx/config/ValheimServerGuide/`:
  - `guidance.lost-scrolls.yaml` — the biome-by-biome story descent (Meadows → Ashlands).
  - `guidance.companions.yaml` — the Companion Handbook (command keys, per-caste chores, adventuring tips).
- Same gameplay as the base Lost Scrolls II package: Communion recruitment, leveling (1→10), caste chores, duels, Communion Totems, and ship/portal travel. Vanilla assets only.

See the base **Lost Scrolls II** package changelog for the underlying mod's feature history.
