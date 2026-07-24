# Lost Scrolls II — Quest (Complete Pack)

**The complete, story-included edition of Lost Scrolls II — ready to play in single player.**

This pack bundles the Lost Scrolls II mod together with its full in-game narrative: the biome-by-biome story descent and the Companion Handbook, delivered through **Valheim ServerGuide**. Install this one package and everything is wired up — no manual file copying.

> Prefer just the gameplay, or already run your own ServerGuide guidance? Install the base **Lost Scrolls II** package instead. This "Quest" edition is for players who want the whole experience out of the box.

## What's included

This is a **content pack**: it ships the story and pulls in the gameplay mod and its story engine as dependencies. You don't install three things by hand — installing this one package brings all of it in.

- **The base Lost Scrolls II mod** — the recruitable, levelable Dvergr companions, pulled in as a dependency (see Requirements).
- **The story chains** (`guidance.lost-scrolls.yaml`) — a reflective descent from Meadows to Ashlands, told at landmarks across the world.
- **The Companion Handbook** (`guidance.companions.yaml`) — in-game guidance teaching the command keys, per-caste chores, and how to adventure with your companions.
- **Ladder guidance** (`guidance.rankings.yaml`) — how the duel and party ladders work, rank milestones, and a "new champion" announcement when someone reaches #1.
- **Tournament guidance** (`guidance.tournaments.yaml`) — join confirmations, round pairings, and the champion prize bundle.
- **Duel broadcasts** (`guidance.duels.yaml`) — announces every duel win to server chat and, optionally, Discord.

All five YAML files drop into **`BepInEx/config/ValheimServerGuide/LostScrollsII/`** — their own subfolder, so they stay separate from any guidance your server already runs and are easy to remove. ServerGuide merges every `*.yaml` under its config folder recursively, at any depth.

> **Rankings & tournaments need ServerGuide.** These guidance files *are* what announces and rewards the competitive systems — rank-ups, "new #1", tournament pairings, champion prizes, Discord posts. That's why **ValheimServerGuide 0.9.0+** is a hard dependency of this pack (0.8.0+ is also what makes the subfolder layout load at all). The ladders themselves keep recording without it, but nothing would announce them.

> **Discord is optional.** The announcements only post if the **server** sets `DiscordWebhookUrl` in the ServerGuide config. Without it everything still fires in-game and the webhook step is skipped.

> **Upgrading from 0.3.0?** Delete the old **top-level** `guidance.lost-scrolls.yaml`, `guidance.companions.yaml`, `guidance.rankings.yaml` and `guidance.tournaments.yaml` from `BepInEx/config/ValheimServerGuide/`. Mod managers don't remove files a new version stopped shipping, and leaving them there loads the guidance twice (duplicate ids).

## Features

- **Recruit companions** — perform the Communion Rite on a subdued Dvergr to free and recruit it (Rogue, Fire Mage, Ice Mage, Support Mage).
- **Level them up** — companions earn XP (1→10) and grow stronger, with per-caste bonuses.
- **Command them** — feed/heal (`G`), cycle Follow/Guard/Standby stance (`E`), open its pack + rename (`Y`), private minimap pins and death markers.
- **Put them to work** — assign allies by caste to vanilla stations: smelting, refining, cooking, brewing, farming, animal care, hauling.
- **Communion Totems** — seal a companion into a carriable totem and summon it back, level intact.
- **Travel together** — Follow-stance allies board ships and teleport through portals with you.
- **Duels** — non-lethal companion-vs-companion sparring (`J`), or gather a **party** and fight team-vs-team (`K`).
- **Rankings** — duel wins feed persistent server ladders; read them in-game with **`F6`**, and name your team with `de_party_name`.
- **Tournaments** — open the panel with **`F7`** and enter by locking a companion's Communion Totem into a slot; it's summoned automatically (at full health) for its match and sealed back afterward. Single/double elimination or round robin, with optional level-gated events and a live standings table.

> Duels, ladders and tournaments are **multiplayer features** by design (a companion only spars with *another player's* companion). Single-player installs simply won't use them — everything else works solo.

## Requirements

All installed automatically as dependencies:

- **BepInEx** for Valheim (`denikson-BepInExPack_Valheim`).
- **Lost Scrolls II** — the base gameplay mod (the companion system).
- **Valheim ServerGuide** (`TaegukGaming-ValheimServerGuide-0.9.0`) — delivers the story and handbook.

## Setup

1. Install this package via your mod manager (r2modman / Thunderstore Mod Manager). Both dependencies are pulled in for you.
2. Launch Valheim and start (or continue) a world.
3. The story begins at the sacrificial stones where you first wake; established characters are gently nudged back to the start. Fight a Dvergr down to low health *without killing it*, hover it, and press **`G`** to perform Communion.

## Multiplayer

Works in multiplayer too — install on the server and every client. The bundled guidance files are meant to be the world's story; on a server that already ships its own ServerGuide guidance, use the base **Lost Scrolls II** package instead to avoid overlapping content.

## Documentation

Full feature guides are on the [GitHub Wiki](https://github.com/yesu0725/Lost-Scrolls-II/wiki).

## Disclaimer

This mod is **created using AI**. No other mods were copied during the process. All feature ideas come from the uploader. If any features or ideas look similar to other mods, these are not intentional.

This mod is **free to use as is**. Voluntary support is appreciated.

---

**Version:** 0.5.0
**Source / issues / wiki:** https://github.com/yesu0725/Lost-Scrolls-II
