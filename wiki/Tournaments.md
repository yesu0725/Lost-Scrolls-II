# Tournaments

A tournament is a **single-elimination bracket** for companion duels, run by the server. It works for both 1v1 and party formats, and seeds entrants from their [ladder rating](Rankings).

## The tournament panel — `F7`

Press **`F7`** to open the tournament panel. It shows the current tournament's status, who's entered, and your controls. While it's open it behaves like a normal menu — the mouse cursor is free, and your character won't move, look around, or swing a weapon.

Close it with **Escape** or `F7` again.

## Entering — lock a totem into a slot

You enter by **locking a companion's [Communion Totem](Communion-Totems) into a slot**:

1. Seal the companion you want to field into a totem at an Incinerator (see [Communion Totems](Communion-Totems)).
2. With the totem in your inventory, open the panel (`F7`) and click **Lock Totem → Enter**.
3. The totem **leaves your inventory** and is held by the tournament until it's over.

For a **party** tournament, up to your maximum party size of totems are locked in at once.

> Your companion is safe. The totem is held in escrow, not consumed — it comes back to you on withdraw, if an admin releases it, if the tournament is cancelled, or when the bracket finishes. If your inventory is full when it's returned, it drops at your feet.

**Changed your mind?** Click **Withdraw** before the bracket begins and your totem is returned immediately.

## Fighting your matches

When an admin **activates a round**, your companion is **summoned automatically** beside you, already in duel mode, matched against its assigned bracket opponent.

- It fights **only** its assigned opponent — several matches can run at the same time without companions crossing over.
- Duels are non-lethal, exactly as in a normal [duel](Dvergr-Duels).
- When the match resolves, your companion is **sealed back into its totem**, keeping any XP it earned. Win, and the next round summons the stronger version of it.
- Losing eliminates you from the bracket, but your totem is still returned when the tournament ends.

Click **View Bracket** (or run `de_tournament bracket`) to see the full draw, round by round.

## Winning

The last companion standing is the **champion**. The win is announced, the prize is granted (configured by your server), and the name is written into the **Hall of Champions** — see past winners with `de_champions`.

## For admins

Admin controls appear in the panel **only if you're a server admin** (the server verifies this, so they work from a remote admin client, not just the host). Every setup command is a button:

| Control | What it does |
|---|---|
| **Start 1v1** / **Start Party** | Opens registration for that format |
| **Begin Bracket** | Closes registration, seeds by rating, builds round 1 |
| **Activate Round** | Summons every pairing in the current round to fight |
| **Forfeit: `<name>`** | That entrant loses; their opponent advances |
| **Release: `<name>`** | Removes an entrant and returns their totem |
| **Cancel** | Ends the tournament and returns every held totem |

The same actions exist as console commands: `de_tournament start <1v1|party> [size] | join | withdraw | begin | activate | bracket | forfeit <name> | release <name> | cancel`.

Seeding is **re-done each round** — survivors are re-sorted by rating and paired highest-vs-lowest. With an odd number of entrants, the top seed gets a bye.

## Requirement — Valheim ServerGuide

> **Install [Valheim ServerGuide](https://thunderstore.io/c/valheim/p/TaegukGaming/ValheimServerGuide/) (0.9.0+) for tournaments to announce and reward anything.**

The bracket itself runs without it, but ServerGuide delivers every message and prize: join confirmations, round pairing notices, the champion announcement, the prize bundle, and Discord posts. The guidance files come bundled in the **Lost Scrolls II — Quest** pack.

## Notes

- Tournaments are **multiplayer** — a companion only fights another player's companion. You need at least two players (four for a full bracket).
- A tournament **survives a restart**: it resumes at the same phase and round.
- Matches are player-run in the world; there's no automatic teleport to an arena, so agree on a meeting place.

## See also

- **[Rankings](Rankings)** — the ladders that seed the bracket.
- **[Dvergr Duels](Dvergr-Duels)** — the duelling rules matches use.
- **[Communion Totems](Communion-Totems)** — how to seal a companion into the totem you enter with.
