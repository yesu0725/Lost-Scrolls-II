# Lost Scrolls II

**Free the fallen instead of fighting them.** Subdue a corrupted Dvergr, perform the Communion Rite, and it becomes a lasting companion — one that fights beside you, levels up, tends your workstations, sails your ships, spars with other players' companions, and follows you through portals.

A spiritual sequel to the deprecated mod **Lost Scrolls** (TaegukGaming), rebuilt from scratch as a new **vanilla-assets-only** Valheim mod. Every recruitable Dvergr is an existing vanilla creature with new behavior — no custom models, textures, sounds, or asset bundles.

## Features

- **Recruit companions** — perform the Communion Rite on a subdued Dvergr to free and recruit it. Rogue, Fire Mage, Ice Mage, and Support Mage castes.
- **Level them up** — companions earn XP (1→10) and grow stronger, with per-caste bonuses. Progress saves on the creature and survives relogs.
- **Command them** — hover + hotkey: feed/heal (`G`), cycle Follow/Guard/Standby stance (`E`), open its inventory + rename (`Y`), and see them on your private minimap.
- **Give them a pack** — each companion carries its own 4×2 inventory (opens like a chest). It picks up loot it already carries, eats food for a temporary HP boost, drinks health/resistance meads, and gets encumbered if overloaded. Its pack rides along through totems and drops on death.
- **Put them to work** — assign allies to vanilla stations by caste: smelting, refining, cooking, brewing, farming (plant + harvest), animal care, and hauling.
- **Duel** — non-lethal companion-vs-companion sparring between players for bonus XP (`J`), or gather a **party** and fight team-vs-team (`K`).
- **Rankings** — every duel win feeds a persistent, server-wide **duel ladder** (Elo rating shown on the companion's name), with a separate **party ladder** for team fights. Open the **ranking board with `F6`** to read both. Name your team with `de_party_name`, and seasons reset the boards.
- **Tournaments** — open the **tournament panel with `F7`**: enter by locking a companion's **Communion Totem** into a slot, and it's held safely until the bracket is done. When an admin starts a round your companion is **summoned automatically** to fight its assigned opponent, then sealed back into its totem with any XP it earned. Single-elimination brackets for 1v1 or party, a champion prize, and a Hall of Champions. Admins get every control as a button — start, begin, activate the round, forfeit, release a totem, cancel.
- **Discord announcements** *(optional)* — duel wins, a new #1 on either ladder, and tournament champions can be posted to your server's Discord (needs the ServerGuide companion mod + a webhook).
- **Communion Totems** — seal a companion into a carriable totem (at an Incinerator, using Wisps) and summon it back later, level intact.
- **Travel together** — Follow-stance allies board your ship and teleport through portals with you.
- **Find & remember them** — companions show as tinted player-icon pins on your private map, and leave a named death marker where they fall.

## Setup

1. Install via your mod manager (r2modman / Thunderstore Mod Manager). BepInEx is pulled in automatically.
2. Launch Valheim and load a world.
3. Fight a Dvergr down to low health *without killing it*, hover it, and press **`G`** to perform Communion. It's now your ally.

Install on the server and on every client in multiplayer. Some features (duels) are multiplayer by design.

## Companion mod — required for rankings & tournaments

> **Install [Valheim ServerGuide](https://thunderstore.io/c/valheim/p/TaegukGaming/ValheimServerGuide/) (0.9.0 or newer) if you want the ranking and tournament features.**

Lost Scrolls II uses ServerGuide as its messaging and reward engine. **Without it the ranking and tournament systems are effectively silent** — no rank-up announcements, no milestone or champion rewards, no tournament join/pairing/result messages, and no Discord posts. Also install the guidance files (easiest: use the **Lost Scrolls II — Quest** pack, which bundles them).

To be precise about what still works without ServerGuide: the ladders themselves keep recording, and `F6` / `de_ladder` still show the standings — you just lose everything that *tells* players about it.

ServerGuide also delivers the mod's **story** and the in-game **Companion Handbook**. All the non-competitive gameplay (recruiting, leveling, chores, totems, travel) works fully on its own.

**Discord announcements** additionally need the **server** to set `DiscordWebhookUrl` in the ServerGuide config.

## Documentation

Full feature guides are on the [GitHub Wiki](https://github.com/yesu0725/Lost-Scrolls-II/wiki).

## Disclaimer

This mod is **created using AI**. No other mods were copied during the process. All feature ideas come from the uploader. If any features or ideas look similar to other mods, these are not intentional.

This mod is **free to use as is**. Voluntary support is appreciated.

---

**Version:** 0.4.0
**Source / issues / wiki:** https://github.com/yesu0725/Lost-Scrolls-II
