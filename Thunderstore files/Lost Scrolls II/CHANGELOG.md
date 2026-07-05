# Changelog

## 0.2.0

**Companion inventory** — each recruited Dvergr now carries its own pack.

- **Own 4×2 inventory**, opened with `Y` while hovering your companion — a chest-style panel (your inventory + crafting + weight readout) that also carries a rename field and a live HP readout.
- **Picks up loot** it already carries (empty pack = nothing); **combat takes priority** over gathering.
- **Eats food** one at a time for a temporary max-HP boost (with a fed icon), **drinks a health mead** when hurt (below 35% HP, until 90%), and **drinks poison/fire/frost resist meads** for the matching resistance (shown as an icon above it).
- **150 weight cap** — over it the companion stops picking up and won't attack (but still moves), with an encumbered icon.
- **Drops its whole pack on death**, and its pack **rides along when sealed into a Communion Totem** (restored on summon).
- **Wood portals** won't send you if a following companion is carrying a non-teleportable item (with a message naming the ally + item).
- **ComfyQuickSlots compatible** — the pack panel no longer hides the extra inventory row.

## 0.1.0

Initial release.

- **Ally recruitment** via the Communion Rite — subdue a Dvergr and press the Communion key (`G`) to free and recruit it. Rogue, Fire Mage, Ice Mage, and Support Mage castes, auto-detected. Recruit state persists across relogs and server restarts.
- **Ally leveling** — companions earn biome-/toughness-scaled kill XP from 1 to 10 on a rising curve, gaining health and damage plus per-caste bonuses. Gold `★N` level badge and a hover readout.
- **Commands** — feed/heal with a health mead (`G`, any player), cycle Follow/Guard/Standby stance (`E`), rename (`Y`), and private per-player minimap pins.
- **Chores** — assign allies by caste to vanilla stations (`H`): Fire Mage smelting, Ice Mage refining, Support Mage cooking/brewing/farming/animal care, Rogue hauling. Persists across relogs and keeps working while you're away.
- **Dvergr duels** — non-lethal, multiplayer-only companion-vs-companion sparring (`J`) for bonus XP, with PvP immunity and auto stand-down.
- **Communion Totems** — seal Follow-stance companions into carriable totems at an Incinerator using Wisps, and summon them back with level/XP/name intact.
- **Travel** — Follow-stance allies board ships and teleport through portals with their owner.
- **Ownership & threat** — the recruiting player owns the companion; commands are owner-only (feeding is shared). Guard treats non-owners as threats; attacking a companion draws retaliation; a butcher-knife strike turns it feral.

Vanilla assets only — no custom models, textures, sounds, or asset bundles.

Story and an in-game Companion Handbook are delivered through the optional companion mod **Valheim ServerGuide**.
