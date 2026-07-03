# Installation

## Requirements

- **Valheim** (current build)
- **BepInEx** for Valheim (`denikson-BepInExPack_Valheim`)

That's it. Lost Scrolls II is a single plugin with no hard third-party dependencies.

## With a mod manager (recommended)

1. Install via **r2modman** or the **Thunderstore Mod Manager** — search for *Lost Scrolls II* and install. BepInEx is pulled in automatically.
2. Launch Valheim through the mod manager.
3. Load a world and start freeing Dvergr. See [Recruiting Companions](Recruiting-Companions).

## Manual install

1. Install the BepInEx pack for Valheim if you haven't already.
2. Drop `LostScrollsII.dll` into `BepInEx/plugins/LostScrollsII/`.
3. Launch the game.

## Multiplayer

Install the mod on the **server** and on **every client** that wants companions. Companion state is stored on the world (on each creature), so it persists across relogs and server restarts. Some features are multiplayer-only by design — see [Dvergr Duels](Dvergr-Duels).

## Optional: the story mod

For the narrative descent and the in-game Companion Handbook, also install **Valheim ServerGuide**. It's a soft dependency — Lost Scrolls II detects it if present and fires story events into it, but every gameplay feature works without it. See [The Story](The-Story).

## Configuration

A config file is generated at `BepInEx/config/com.lostscrollsii.cfg` after the first launch. You can rebind every hotkey and tune a few values there (default keys below). Edit it directly, or use a config-editor mod.

| Setting | Default | What it does |
|---|---|---|
| Communion / Feed key | `G` | Free a subdued Dvergr, or feed/heal a recruited one |
| Chore assign / recall key | `H` | Assign or recall a companion at a workstation |
| Stance cycle key | `E` | Cycle Follow → Guard → Standby |
| Rename key | `Y` | Rename a companion |
| Duel key | `J` | Toggle duel mode on your companion |
| Show map pins | on | Show your own companions on the minimap |

All hotkeys are ignored while a text box, chat, or console has focus, so typing never triggers a command.
