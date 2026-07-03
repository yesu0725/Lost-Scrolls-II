# Lost Scrolls II

A vanilla-assets-only Valheim mod centered on **recruitable, levelable Dvergr companions**. Free a corrupted Dvergr instead of killing it — perform the Communion Rite and it becomes an ally that fights beside you, levels up, tends your workstations, sails your ships, duels other players' companions, and follows you through portals.

A spiritual sequel to the deprecated Thunderstore mod **Lost Scrolls** (TaegukGaming) — a continuation of its story, rebuilt from the ground up.

## Features

- **Ally recruitment** — the Communion Rite: subdue a Dvergr, then free it (Rogue, Fire Mage, Ice Mage, Support Mage castes).
- **Ally leveling** — biome-/toughness-scaled kill XP, levels 1–10, per-caste bonuses; progress persists on the creature.
- **Commands** — feed/heal, Follow/Guard/Standby stance, rename, private minimap pins, hover tooltips.
- **Chores** — assign allies by caste to vanilla stations: smelting, refining, cooking, brewing, farming, husbandry, hauling.
- **Dvergr duels** — non-lethal, multiplayer-only companion sparring for bonus XP.
- **Communion Totems** — seal an ally into a carriable totem and summon it back, level intact.
- **Travel** — Follow-stance allies board ships and teleport through portals with you.

## Vanilla assets only

Every recruitable Dvergr is an existing vanilla creature with new behavior attached — no custom models, textures, sounds, or asset bundles.

## Documentation

- **Players:** see the [Wiki](https://github.com/yesu0725/Lost-Scrolls-II/wiki) (mirrored in [`wiki/`](wiki/)).
- **Design / internals:** see [`CLAUDE.md`](CLAUDE.md) and the [`docs/`](docs/) folder.

## Building

.NET SDK project targeting `net48`. Point the build at your Valheim install and BepInEx:

```
dotnet build src/LostScrollsII.csproj -c Release -p:VALHEIM_INSTALL="C:\Path\To\Valheim"
```

The build publicizes `assembly_valheim` and deploys the DLL to your BepInEx/r2modman/dedicated-server plugin folders if they exist. See [`src/LostScrollsII.csproj`](src/LostScrollsII.csproj) for the override properties.

## Releases (Thunderstore)

Two packages ship from `Thunderstore files/` (see [`docs/Publishing.md`](docs/Publishing.md) for full detail and rebuild steps):

- **Lost Scrolls II** — the base gameplay mod (companion system; BepInEx dependency only, ServerGuide optional).
- **Lost-Scrolls-II-Quest** — the complete, single-player-ready pack: bundles the story + Companion Handbook and pulls in the base mod and ServerGuide as dependencies.

Publish the base package first — Thunderstore validates the Quest pack's base-mod dependency at publish time.

## Optional companion mod

The story and an in-game Companion Handbook are delivered through the separate mod **Valheim ServerGuide** (a soft dependency). Every gameplay feature works without it.

## Disclaimer

This mod is **created using AI**. No other mods were copied during the process. All feature ideas come from the uploader. This mod is **free to use as is**.
