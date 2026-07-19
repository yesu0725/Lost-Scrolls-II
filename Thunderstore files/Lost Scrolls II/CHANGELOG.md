# Changelog

## 0.4.0

**Competitive UI + escrow tournaments** — the ranking and tournament systems now have real in-game screens, and tournament entry works by locking a companion's totem into a slot.

- **Ranking board (`F6`)** — a read-only screen showing both the **duel ladder** and the **party ladder** (rank, rating, owner, W/L, team size). `de_ladder` / `de_party_ladder` still work.
- **Tournament panel (`F7`)** — status, entrants, and one-click controls: **Lock Totem → Enter**, **Withdraw**, **View Bracket**. Opens like a proper menu: mouse cursor free, camera and player actions blocked while it's up.
- **Enter by totem** — you register a companion by locking its **Communion Totem** into a slot. The totem is held (escrowed) for the tournament; when the admin activates a round your companion is **summoned automatically** to fight its assigned opponent, then **resealed back into the totem** (keeping any XP it gained). Totems are always returned — on withdraw, admin release, cancel, or when the tournament ends.
- **Assigned pairings** — a summoned companion only fights its own bracket opponent, so several matches can run at once without crossing over.
- **Admin controls (admins only)** — every setup command as a button: Start 1v1 / Start Party / Begin / **Activate Round** / Cancel, plus per-entrant **Forfeit** and **Release**. Admin actions are verified by the server, so they now work from a remote admin client (not just the host). New console subcommands: `de_tournament withdraw | activate | release <name>`.
- **Party names** — name your team with `de_party_name <name>`; it shows on the party ladder, the ranking board, and in announcements.
- **Discord announcements** (via the optional ServerGuide mod, with a webhook configured): every **duel win** (1v1, party, and tournament matches), every **new #1** on either ladder, and the **tournament champion**.

> **Rankings & tournaments require [Valheim ServerGuide](https://thunderstore.io/c/valheim/p/TaegukGaming/ValheimServerGuide/) 0.9.0+**, which delivers every message and reward these systems produce — rank-up and "new #1" announcements, tournament join/pairing/champion messages, the prize bundles, and the Discord posts. Without it the ladders still record and `F6` / `de_ladder` still show the standings, but nothing announces or rewards them. The guidance files come bundled in the **Lost Scrolls II — Quest** pack. (Discord additionally needs `DiscordWebhookUrl` set on the server.)

## 0.3.0

**Competitive suite** — duel rankings, party duels, party rankings, and tournaments — plus map improvements and compatibility fixes.

- **Duel ladder (1v1)** — every companion-vs-companion duel win is recorded on a persistent, server-authoritative ladder. Each companion has an Elo rating (shown as a rank on its name tag), with wins/losses, streaks, and seasons. `de_ladder` shows the standings; `de_season_reset` (host) starts a new season.
- **Party duels** — gather your nearby Follow-stance companions into a team (`K`) and fight another player's team. Non-lethal (members are benched, not killed); win by attrition, with team-size-scaled XP.
- **Party ladder** — a separate persistent ladder for team duels, keyed by owner with the companions that fought for it (`de_party_ladder`).
- **Tournaments** — a server-run bracket for both 1v1 and party formats (`de_tournament start|join|begin|bracket|forfeit|cancel`). Re-seeded single elimination, ordinary player-run matches, a champion prize, and a Hall of Champions (`de_champions`).
- **Map pins** — companion pins now use the player icon, tinted and smaller, so allies are easy to tell apart from your own marker (configurable color/size).
- **Death markers** — when one of your companions dies (with items or not), a named skull marker is dropped on your map where it fell.
- **Fixes** — the duel "double win" (a win could be announced/counted twice) is fixed; and the companion pack no longer conflicts with **BiomeLords'** chest-UI positioning (it now defers to BiomeLords, with a manual toggle).

Story, rankings and tournament guidance, and the Companion Handbook are delivered through the optional companion mod **Valheim ServerGuide** (bundled in the "Quest" pack).

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
