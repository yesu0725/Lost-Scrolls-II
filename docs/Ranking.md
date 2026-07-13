# Duel Ranking System

A persistent, server-authoritative ladder that records the outcome of every
non-lethal companion duel. Each record is keyed to **a companion**, denormalized
with **its owner**, so the ladder reads as "owner + companion" exactly as
requested.

Covers the **1v1** ladder (this doc). The **party** ladder is its sibling — see
[Party-Duels.md](Party-Duels.md).

> **Status: Phase A + B built (2026-07-09), unverified in a live session.** The
> foundations (`DE_CompanionId`, `LeaderboardStore` JSON, `LeaderboardSync` RPC,
> `Rating` Elo, the `dvergr_rank_changed` bridge trigger) and the 1v1 ladder
> (win-report wiring, `de_ladder` / `de_season_reset` commands, name-tag rank, the
> ServerGuide `guidance.rankings.yaml` Codex + milestone) are all implemented and
> build clean. Needs the 2-player pass in [Testing.md](Testing.md) §17 before party
> (Phase C/D) and tournaments (Phase E) are built. Party/tournament sections in the
> sibling docs remain **planned**.

## Why This Needs New Infrastructure

Everything the mod stores today is either transient (the `DE_Duel` flag) or
per-player (companion ZDO fields, ServerGuide's `m_customData`). A ranking must
be **persistent** (survive restarts), **server-authoritative** (one truth for
everyone, not client-side), and **cross-player**. Two new foundations provide
that:

1. **Stable companion identity — `DE_CompanionId`.** A GUID stamped on a
   companion at recruit (`CommunionService.TryRecruit`), persisted on its ZDO and
   **carried through the Communion Totem's `m_customData`** on seal/summon (the
   same channel that already carries level/xp/name/owner — see
   [Companion-Totems.md](Companion-Totems.md)). ZDOIDs do **not** survive
   seal/summon/relog (the chore system already learned this — it re-resolves by
   world position), so a raw ZDOID can't key a durable record. The GUID can.

2. **`LeaderboardStore` — the JSON ladder file.** A server-owned table persisted
   to disk (see [Persistence](#persistence)), synced to clients over a dedicated
   RPC.

## Record Schema (`CompanionRecord`)

| Field | Meaning |
|---|---|
| `companionId` | GUID — the primary key (the "companion" in the record) |
| `ownerId` | recruiting player's `GetPlayerID()` (the "owner") |
| `ownerName` | snapshot of the owner's display name (shown even when offline) |
| `companionName` | snapshot of `DE_Name` / default caste name at last match |
| `caste` | `DvergrCaste` — for caste-filtered ladder views and rewards |
| `wins`, `losses` | raw tallies (shown in the ladder UI) |
| `winStreak`, `bestStreak` | current + all-time streak (drives streak rewards) |
| `rating` | **Elo**, starts at 1000 — the value the ladder is *sorted* by |
| `lastMatch` | UTC ticks — for decay + tie-breaks |
| `seasonId` | which season the record belongs to (see [Seasons](#seasons)) |

Names are **snapshots** so an offline owner or a renamed companion still shows
correctly; they're refreshed on each match report.

## Rating Model — Elo (not raw win count)

Raw win counts reward whoever grinds the most matches. **Elo** rewards *beating
stronger opponents* and enables fair tournament seeding. Both are stored: wins/
losses are displayed, `rating` is what the ladder ranks by.

- Start rating: **1000** (config `Ranking.StartRating`).
- Update: standard Elo, `newR = R + K*(S - E)`, where `S` is 1 for the winner /
  0 for the loser and `E` is the expected score from the rating gap. `K` is
  config `Ranking.KFactor` (default 32).
- Applied **server-side only** so it can't be spoofed by a client.

## Anti-Farm

The core different-owner rule (a duel-mode companion only ever fights *another
player's* duel-mode companion — see [Duel-Arena.md](Duel-Arena.md)) already
blocks solo self-farming. On top of that the store enforces a **per-pair
cooldown** (`Ranking.PairCooldownSeconds`, default 300): repeat wins by the same
`(winnerId, loserId)` pair inside the window still resolve the duel and grant XP
but do **not** move rating, so two friends can't pad the ladder.

## Data Flow (one clean win event)

The double-subdue bug (fixed before this system was built — see
[Duel-Arena.md](Duel-Arena.md) and below) matters here: rating must move **once**
per bout.

1. A duelist is knocked to the 5% floor. `CompanionDamagePatch` calls the
   idempotent `DvergrCompanion.ResolveSubdue(winner)` — guaranteed to fire
   exactly once per bout via the `_duelResolved` latch.
2. `AwardDuelWin` (already the win path) additionally builds a
   `MatchResult { winnerCompanionId, loserCompanionId, winnerOwnerId, loserOwnerId,
   … }` and hands it to `LeaderboardSync.ReportDuel`.
3. `ReportDuel` routes to the **server** (via `ZRoutedRpc`, mirroring
   ServerGuide's `GuidanceSync` pattern). The server validates (different owners,
   pair-cooldown check), applies Elo, updates both records, persists the JSON,
   and broadcasts the changed rows to clients.
4. Where a threshold is crossed (reached #1, entered top 3), the server fires the
   `dvergr_rank_changed` ServerGuide trigger so a guidance entry / reward can
   respond (see [ServerGuide Integration](#serverguide-integration)).

## Persistence

JSON file next to the world save, keyed by world name:

```
<save folder>/LostScrollsII/ladder.<worldName>.json
```

- Loaded on the authority at `ZNet.Awake` (server/host only — pure clients wait
  for the RPC push, exactly like ServerGuide's config sync).
- Saved **debounced** (a few seconds after the last change) so a burst of
  matches doesn't thrash the disk.
- Human-readable and hand-editable for admin correction/backup.

## Display

- **`de_ladder [caste]`** console command — prints the top N
  (`#rank  owner — companion  rating  W/L`), optionally filtered by caste.
- **ServerGuide "Ladder Codex" page** (`guidance.rankings.yaml`,
  `category: Rankings`) — the in-world, spoiler-free presentation, delivered the
  same way the Companion Handbook is.
- **Optional name-tag rank** — append `(#rank)` to a companion's floating name
  (extends the existing owner-name tag; config `Ranking.ShowRankOnNameTag`).

## Seasons

A `seasonId` on every record + an admin command **`de_season_reset`** that
archives the current board (to `ladder.<world>.season<N>.json`) and starts a
fresh one. Keeps a long-lived server's ladder from ossifying and gives
tournaments a recurring cadence. The archived "Hall of Champions" feeds the
tournament doc's history view.

## ServerGuide Integration

New trigger type (added to `ServerGuideBridge` + ServerGuide's
`GuidanceDispatcher`/`TriggerSpec`, following the existing `dvergr_duel_won`
pattern):

| Trigger id | Fired when | Subject | Extra |
|---|---|---|---|
| `dvergr_rank_changed` | A companion crosses a ladder threshold | winner caste | `rank`, `rating`, `companionName`, `ownerName` |

Templating vars `{rank}` and `{rating}` are added so guidance text and rewards
(a milestone buff, a "Ladder Champion" title via the `rename_player` reward) can
hang off it. All rewards flow through ServerGuide's existing `RewardDispatcher`
— no new reward code in Lost Scrolls II.

## Open Questions (resolve after in-game verification)

- Final `KFactor`, `StartRating`, and `PairCooldownSeconds` values — first pass.
- Whether rating decay for inactivity is worth adding (schema has `lastMatch`).
- Whether to show the ladder live above the arena vs. only via command/Codex.
