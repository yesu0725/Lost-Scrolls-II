# Rankings

Every duel your companion wins is recorded on a persistent, **server-wide ladder**. There are two boards: one for 1v1 [duels](Dvergr-Duels) and one for party (team) duels.

## The ranking board — `F6`

Press **`F6`** to open the ranking board. It shows both ladders side by side:

- **Duel Ladder** — rank, rating, companion name, its owner and caste, and win/loss record.
- **Party Ladder** — rank, rating, party name (and owner), record, and team size.

It's read-only — close it with **Escape**, like reading a runestone.

## How rating works

Ratings use **Elo**, the chess rating system. Everyone starts at **1000**.

- Beat a **stronger** companion and you gain more; beat a weaker one and you gain less.
- Losing costs rating, but a companion is never killed or deleted — duels are non-lethal.
- Your companion's rank appears on its **floating name tag** (e.g. `#3`) once it has one.

**Anti-farming:** the same two companions can only move each other's rating once every few minutes. You can keep duelling a friend — the wins and losses still count — but the rating stops moving, so you can't pad it with repeat matches.

## Your companion keeps its record

A record follows the **companion**, not the slot it's in. Its identity survives being sealed into a [Communion Totem](Communion-Totems) and summoned back, and survives relogs and server restarts.

## Party names

Name your team so it shows on the party ladder and in announcements:

```
de_party_name The Ironhands
```

The name is stored on the server and persists across relogs. If you never set one, your player name is used.

## Seasons

An admin can reset the boards with `de_season_reset`. The outgoing ladder is archived and everyone starts fresh at 1000, so a long-running server can run competitive seasons.

## Requirement — Valheim ServerGuide

> **Install [Valheim ServerGuide](https://thunderstore.io/c/valheim/p/TaegukGaming/ValheimServerGuide/) (0.9.0+) for the ranking features to actually announce anything.**

ServerGuide is the messaging and reward engine. Without it the ladders still record and `F6` still shows the standings — but **nothing announces or rewards them**: no rank-up messages, no "new #1" celebration, no milestone rewards, and no Discord posts.

The guidance files that produce those messages come bundled in the **Lost Scrolls II — Quest** pack (installed to `BepInEx/config/ValheimServerGuide/LostScrollsII/`).

## Discord announcements (optional)

If your server admin sets `DiscordWebhookUrl` in the ServerGuide config, the server posts:

- every **duel win** (1v1 and party, including tournament matches),
- every **new #1** on either ladder,
- the **tournament champion** (see [Tournaments](Tournaments)).

With no webhook configured, the in-game messages still fire and the Discord step is simply skipped.

## See also

- **[Dvergr Duels](Dvergr-Duels)** — how duelling works, and the party duels that feed the party ladder.
- **[Tournaments](Tournaments)** — bracket competitions seeded from these ratings.
