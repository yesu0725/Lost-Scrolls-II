# Tournaments

A tournament is a companion-duel **bracket** run by the server, for both 1v1 and party formats. Entrants are seeded from their [ladder rating](Rankings), and the host chooses how the bracket plays out — **single elimination, double elimination, or round robin**.

## The tournament panel — `F7`

Press **`F7`** to open the tournament panel. It shows the current tournament's status, its format, who's entered, and your controls. While it's open it behaves like a normal menu — the mouse cursor is free, and your character won't move, look around, or swing a weapon.

Close it with **Escape** or `F7` again.

## Entering — lock a totem into a slot

You enter by **locking a companion's [Communion Totem](Communion-Totems) into a slot**:

1. Seal the companion you want to field into a totem at an Incinerator (see [Communion Totems](Communion-Totems)).
2. With the totem in your inventory, open the panel (`F7`) and click **Lock Totem → Enter**.
3. The totem **leaves your inventory** and is held by the tournament until it's over.

For a **party** tournament, up to your maximum party size of totems are locked in at once.

> **Level-gated events.** A host can restrict a tournament to companions of a specific level (for example, a "Level 3 only" bracket). If your companion doesn't match, the entry is turned away — level up or seal a different companion.

> Your companion is safe. The totem is held in escrow, not consumed — it comes back to you on withdraw, if an admin releases it, if the tournament is cancelled, or when the bracket finishes. If your inventory is full when it's returned, it drops at your feet.

**Changed your mind?** Click **Withdraw** before the bracket begins and your totem is returned immediately.

## Fighting your matches

When an admin **activates a round**, your companion is **summoned automatically** beside you, **at full health**, already in duel mode, matched against its assigned bracket opponent.

- It fights **only** its assigned opponent — several matches can run at the same time without companions crossing over.
- It enters every match **fully healed**, so damage from an earlier round never carries over.
- Duels are non-lethal, exactly as in a normal [duel](Dvergr-Duels).
- When the match resolves, your companion is **sealed back into its totem**, keeping any XP it earned. Win, and the next round summons the stronger version of it.
- Losing knocks you out of the bracket (or, in round robin, just costs you the match) — either way your totem is returned when the tournament ends.

Click **View Bracket** on the panel to see the full draw round by round, each entrant's **companion name and level**, and the **live standings** (win–loss records, updated as matches resolve).

## Bracket formats

The host picks one format when they start the tournament:

| Format | How it works |
|---|---|
| **Single elimination** | One loss and you're out. Survivors are re-seeded by rating each round and paired highest-vs-lowest; an odd number gives the top seed a bye. |
| **Double elimination** | You're only out after **two** losses — a loss in the winners' bracket drops you into the losers' bracket for a second chance. The two bracket winners meet in the grand final. |
| **Round robin** | No elimination — **everyone plays everyone**. The champion is whoever has the most wins when the schedule is done. Best for small, friendly fields. |

The **View Bracket** standings show where everyone stands at any point, so round robin and double elimination are easy to follow.

## Winning

The last companion standing — or the most wins, in round robin — is the **champion**. The win is announced, the prize is granted (configured by your server), and the name is written into the **Hall of Champions** for the server to keep.

## For admins

Admin controls appear in the panel **only if you're a server admin** (the server verifies this, so they work from a remote admin client, not just the host). Every setup step is a button:

| Control | What it does |
|---|---|
| **Type: `<format>`** | Cycles the format — single elimination → double elimination → round robin — before you start |
| **Start 1v1** / **Start Party** | Opens registration for that format, using the chosen bracket type |
| **Begin Bracket** | Closes registration, seeds by rating, builds the first round |
| **Activate Round** | Summons every pairing in the current round to fight (each at full health) |
| **Forfeit: `<name>`** | That entrant loses the match; their opponent advances |
| **Release: `<name>`** | Removes an entrant and returns their totem |
| **Cancel** | Ends the tournament and returns every held totem |

The **entrant cap** is set by the server (default **4**). Starting a tournament fills up to that many slots.

## Requirement — Valheim ServerGuide

> **Install [Valheim ServerGuide](https://thunderstore.io/c/valheim/p/TaegukGaming/ValheimServerGuide/) (0.9.0+) for tournaments to announce and reward anything.**

The bracket itself runs without it, but ServerGuide delivers every message and prize: join confirmations, round pairing notices, the champion announcement, the prize bundle, and Discord posts. The guidance files come bundled in the **Lost Scrolls II — Quest** pack.

## Notes

- Tournaments are **multiplayer** — a companion only fights another player's companion. You need at least two players (four for a full bracket).
- A tournament **survives a restart**: it resumes at the same phase, format and round.
- Companions are summoned **beside their owners**, so the two players in a match need to be near each other for the duel to happen — agree on a meeting place (or watch the stream, if it's a streamed event).

## See also

- **[Rankings](Rankings)** — the ladders that seed the bracket.
- **[Dvergr Duels](Dvergr-Duels)** — the duelling rules matches use.
- **[Communion Totems](Communion-Totems)** — how to seal a companion into the totem you enter with.
