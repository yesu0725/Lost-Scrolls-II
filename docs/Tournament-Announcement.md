# 🏆 Dvergr Tournaments — Announcement & Mechanics

*The corruption is broken. The Communion Rite is sung. Now let your freed Dvergr prove themselves in the arena.*

**Lost Scrolls II** brings a full, server-run **tournament system** to the world — a bracket of companion duels where the freed Dvergr of every player fight for the title of **Champion**. It's non-lethal, restart-safe, and built entirely on vanilla Valheim assets.

---

## 📣 The Announcement

> **A Tournament of Companions is coming to the server.**
>
> Seal your finest Dvergr into a Communion Totem, sharpen its skills, and answer the call. When the host opens registration, lock your totem in — and when a round is activated, your companion is summoned to the arena to fight.
>
> Win by strength, seeding, and skill. The last companion standing is written into the **Hall of Champions**, and the prize is yours.
>
> **Press `F7` in-game to enter.**

Feel free to copy the block above into Discord, a server message, or an event post.

---

## ⚔️ Mechanics at a Glance

| | |
|---|---|
| **Open the panel** | Press **`F7`** |
| **Formats** | Single elimination · Double elimination · Round robin |
| **Modes** | 1v1 and party (team-vs-team) |
| **Entry** | Lock a companion's **Communion Totem** into a slot (held in escrow, returned later) |
| **Seeding** | By each companion's **ladder rating** (see Rankings) |
| **Combat** | Non-lethal duels — your companion is auto-summoned at **full health** |
| **Level gate** | Host can restrict entry to a specific companion level |
| **Champion** | Written to the **Hall of Champions**; prize granted by the server |
| **Requirement** | Valheim ServerGuide **0.9.0+** for announcements & rewards |

---

## 🎯 How to Enter (players)

1. **Seal your companion** into a Communion Totem at an Incinerator.
2. Press **`F7`** to open the tournament panel. While it's open the game behaves like a menu — the cursor is free and your character won't move or attack.
3. Click **Lock Totem → Enter**. The totem leaves your inventory and is held in escrow by the tournament.
4. For a **party** tournament, lock in up to your maximum party size of totems.

> **Your companion is safe.** The totem is *held*, never consumed. It comes back to you when you **Withdraw**, if an admin **releases** it, if the tournament is **cancelled**, or when the bracket **finishes**. If your bag is full when it's returned, it drops at your feet.

> **Level-gated events.** A host may restrict a bracket to companions of a specific level (e.g. "Level 3 only"). Off-level entries are turned away — level up or seal a different companion.

---

## 🥊 Fighting Your Matches

When an admin **activates a round**:

- Your companion is **summoned automatically beside you**, at **full health**, already in duel mode.
- It fights **only its assigned bracket opponent** — several matches can run at once without companions crossing over.
- Duels are **non-lethal**, exactly like a normal Dvergr Duel.
- On resolution, your companion is **sealed back into its totem, keeping any XP it earned** — win, and the next round summons a stronger version of it.
- Losing knocks you out of the bracket (or, in round robin, just costs you that match). Either way your totem returns when the tournament ends.

Click **View Bracket** to see the full draw round by round, each entrant's **companion name and level**, and **live win–loss standings**.

> ⚠️ **Multiplayer only.** A companion only fights *another player's* companion. Matches summon each companion beside its owner — so the two players in a match need to be near each other. Agree on a meeting place (an arena, a marked ward) before the round is activated.

---

## 🗺️ Bracket Formats

The host picks one format when starting the tournament:

| Format | How it works |
|---|---|
| **Single elimination** | One loss and you're out. Survivors are re-seeded by rating each round and paired highest-vs-lowest; an odd count gives the top seed a bye. |
| **Double elimination** | Out only after **two** losses — a loss in the winners' bracket drops you to the losers' bracket for a second chance. The two bracket winners meet in the grand final. |
| **Round robin** | No elimination — **everyone plays everyone**. Most wins takes the title. Best for small, friendly fields. |

The **View Bracket** standings show where everyone stands at any point, so round robin and double elimination stay easy to follow.

---

## 🛡️ For Admins

Admin controls appear in the `F7` panel **only if you're a server admin** (the server verifies this, so they work from a remote admin client, not just the host). Every setup step is a button:

| Control | What it does |
|---|---|
| **Type: `<format>`** | Cycles the format — single → double → round robin — before you start |
| **Start 1v1** / **Start Party** | Opens registration for that mode, using the chosen bracket type |
| **Begin Bracket** | Closes registration, seeds by rating, builds round one |
| **Activate Round** | Summons every pairing in the current round to fight (each at full health) |
| **Forfeit: `<name>`** | That entrant loses; their opponent advances |
| **Release: `<name>`** | Removes an entrant and returns their totem |
| **Cancel** | Ends the tournament and returns every held totem |

- The **entrant cap** is server-configured (default **4**). Starting a tournament fills up to that many slots.
- The **level gate** (`RequiredEntrantLevel`, `0` = off) is set in config.
- A tournament **survives a server restart** — it resumes at the same phase, format, and round.

---

## 🎁 Rewards & Announcements

Every message and prize is delivered through **Valheim ServerGuide (0.9.0+)**:

- Join confirmations and round pairing notices.
- The **champion announcement** and prize bundle (item / buff / title, all configurable server-side).
- **Discord posts** for every match win and the crowned champion (requires a server webhook).
- The winner is archived into the **Hall of Champions** the server keeps.

The bracket itself runs without ServerGuide, but nothing will *announce or reward* without it. The guidance files ship in the **Lost Scrolls II — Quest** pack.

---

## 📎 See Also

- **Rankings** — the duel & party ladders that seed the bracket (`F6`).
- **Dvergr Duels** — the non-lethal duelling rules matches use.
- **Communion Totems** — how to seal a companion into the totem you enter with.

*Vanilla assets only. No custom models, sounds, or bundles — every fighter is a freed Dvergr.*
