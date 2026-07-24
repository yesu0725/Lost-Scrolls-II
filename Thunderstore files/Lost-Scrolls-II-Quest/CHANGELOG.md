# Changelog

## 0.5.0

- Updated to depend on **Lost Scrolls II 0.5.0** — tournament formats (single/double elimination, round robin), optional level-gated events, companion level shown on the bracket, live win–loss standings, an entrant cap, full-heal-on-summon, and important fixes to how ranking/tournament data is saved and synced. See the base mod's changelog for detail.
- **Updated `guidance.rankings.yaml` and `guidance.tournaments.yaml`** — rewritten around the in-game **UI**: the ranking pages now point players to the **`F6`** ranking board (instead of console commands), and the tournament pages describe the current **`F7`** panel flow — sealing a companion into a Communion Totem, **Lock Totem → Enter**, automatic summon at full health, and **View Bracket** for pairings, levels and standings.
- **New guidance page** — *"Entering a Tournament"* teaches the whole `F7` entry flow the first time a player frees a companion.
- Story and Companion Handbook content unchanged; still depends on **ValheimServerGuide 0.9.0+**.

## 0.4.0

- Updated to depend on **Lost Scrolls II 0.4.0** (in-game ranking board `F6`, tournament panel `F7`, totem-based tournament entry with auto-summon, party names, admin controls) and on **ValheimServerGuide 0.9.0**, which adds the message templating and Discord support these guidance files rely on.
- **Moved into their own subfolder** — the bundled guidance now installs to `BepInEx/config/ValheimServerGuide/**LostScrollsII/**` instead of the top level, so it stays separate from any guidance your server already runs and is easy to remove. ServerGuide loads its config folder recursively (0.8.0+), so nothing else changes.
- **New guidance file**:
  - `guidance.duels.yaml` — broadcasts **every duel win** (1v1 and party, tournament matches included) to server chat and Discord.
- **Updated** `guidance.rankings.yaml` — added "new #1" entries for both ladders (a companion or party reaching the top of the board), with Discord announcements.
- **Updated** `guidance.tournaments.yaml` — the champion announcement now posts a fully templated message to Discord.
- Story and Companion Handbook content unchanged.

> ⚠️ **Upgrading from 0.3.0:** delete the old **top-level** `guidance.lost-scrolls.yaml`, `guidance.companions.yaml`, `guidance.rankings.yaml` and `guidance.tournaments.yaml` from `BepInEx/config/ValheimServerGuide/`. Mod managers don't delete files a new version stopped shipping, so leaving them loads the guidance twice (duplicate ids).

> **Rankings & tournaments require ValheimServerGuide 0.9.0+** — these guidance files are what announces and rewards them (rank-ups, "new #1", tournament pairings, champion prizes, Discord). Without ServerGuide the ladders still record and `F6` still shows the standings, but nothing announces or rewards them.

> **Discord is optional.** These announcements only post if the **server** sets `DiscordWebhookUrl` in the ServerGuide config. Without it the in-game chat/messages still fire and the webhook step is skipped.

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
