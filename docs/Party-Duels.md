# Party Duels & Party Ranking

Team-vs-team companion sparring: an owner puts their **party** (their nearby
Follow-stance companions) into party-duel mode to fight **another owner's**
party. Non-lethal, consented, multiplayer-only — the same spirit as the 1v1
[Duel-Arena.md](Duel-Arena.md), scaled to N-vs-M. Includes its own persistent
**party ranking** (this doc), a sibling of the 1v1 [Ranking.md](Ranking.md).

> **Party naming + Discord update (2026-07-17, unverified).** You can **name your
> party** with `de_party_name <name>` (or from the tournament registration panel);
> the name persists in the server-side `PartyRecord` and shows on the party ladder,
> the `F6` ranking board, and in party announcements (`{partyName}`). Every **party
> duel win** and every **new #1** party (`dvergr_party_rank_first`) now **broadcast
> to Discord** through ServerGuide. See [Changelog.md](Changelog.md) (2026-07-17)
> and [Testing.md](Testing.md) §21.

> **Status: Phase C + D built (2026-07-09), unverified.** Party mechanics
> (`DE_PartyDuel`, the `K` key, team-aware `IsEnemy`, bench-on-subdue reusing the
> `ResolveSubdue`/`_duelResolved` latch, win-by-attrition, team-size-scaled XP) and
> the **party ranking** (`PartyRecord`, owner-vs-owner team Elo, `de_party_ladder`,
> `dvergr_party_duel_won` / `dvergr_party_rank_changed`, the party Codex pages) are
> all implemented and build clean — see [Testing.md](Testing.md) §18 (mechanics)
> and §19 (ranking). Party bouts share the same `LeaderboardStore`/`LeaderboardSync`
> the 1v1 ladder uses; the report fires once per match (a static owner-pair latch on
> the winning authority client, since every surviving winner shares that owner).

## Concept

- A **party** is simply **all of one owner's companions** that are in party-duel
  mode. The owner id is the team key — companions of the same owner are
  teammates by definition, so no separate "team object" is needed.
- Two parties with **different, non-zero owners** fight. Everyone else (players,
  creatures, same-owner allies, non-participating companions) is ignored and
  immune, exactly as in 1v1.
- **Team-size cap** (`PartyDuel.MaxPartySize`, default 4) keeps brawls readable
  and bounds the AI load.

## Mechanics (reuses the 1v1 duel spine)

- **State:** a new `DE_PartyDuel` ZDO flag, alongside the existing `DE_Duel`.
  Cleared on spawn (a relog ends any party duel), same as 1v1.
- **Targeting:** extend `CompanionIsEnemyPatch`. In party mode, `IsEnemy(a,b)` is
  true iff **both** are party-duel companions with **different non-zero owners**.
  This is the 1v1 rule minus the single-rival restriction, so it scales to any
  team sizes with no per-target bookkeeping — vanilla `MonsterAI` picks the
  nearest valid enemy.
- **Non-lethal + bench-on-subdue:** the same 5% floor in `CompanionDamagePatch`.
  But a subdued party member is **benched** (leaves party-duel mode, stops
  fighting and can't be targeted) rather than ending the match. Reuses the
  idempotent `ResolveSubdue` latch so each member is benched exactly once (the
  double-subdue guard is essential here — a benched member's HP regen must not
  re-enter it into the fight).
- **Win detection:** the match ends when one side has **no un-benched members
  left**, or when an owner logs out / leaves ~40 m (that owner's team forfeits).
  Winner = the side with survivors. Driven on the match's authority instance
  (the same "gated to the ZDO owner" pattern duels/chores use).
- **Input:** a new party-duel key (config `PartyDuel.Key`, default `K`), pressed
  while hovering **your own** companion, toggles your whole nearby Follow team
  into party-duel mode; press again to stand the team down. Owner-gated like the
  other command keys, and suppressed while typing (same guard as all binds).

## Balance

- **XP:** each surviving winner earns `base × (loserTeamSize / winnerTeamSize)`
  — bigger, riskier fights pay more, but winning by outnumbering pays less per
  head, discouraging zerg parties.
- **Optional rating-based matchmaking:** match by **team rating**, not head
  count, so a pair of level-10s can be paired against a quad of level-5s. First
  pass leaves this manual (owners opt in near each other); the tournament runner
  ([Tournaments.md](Tournaments.md)) uses rating seeding directly.

## Party Ranking

Persistent, server-authoritative, same JSON store and RPC path as the 1v1 ladder
([Ranking.md](Ranking.md)) — a separate table.

### Record Schema (`PartyRecord`)

Keyed by **`ownerId`** — a party *is* an owner's stable, which is the "owner"
half of the record; the participating companions are the other half.

| Field | Meaning |
|---|---|
| `ownerId` | party primary key = the owner |
| `ownerName` | snapshot display name |
| `rating` | **team Elo**, starts at 1000 — sorts the party ladder |
| `wins`, `losses` | raw tallies |
| `memberSnapshot[]` | `companionId + caste + level` of each participant at the last match — records *which* companions earned the standing |
| `bestTeamSize` | largest winning team fielded |
| `lastMatch`, `seasonId` | decay/tie-break + season bucket |

The `memberSnapshot` is what satisfies "the record should have the owner + the
companions": the owner is the key, the snapshot lists the companions.

### Data Flow

Mirrors 1v1: on match resolution the authority builds a
`PartyMatchResult { winnerOwnerId, loserOwnerId, winnerMembers[], loserMembers[] }`
and calls `LeaderboardSync.ReportPartyDuel`. The **server** applies a **team Elo**
update — average winner rating vs. average loser rating, applied to each owner's
party rating — updates both `PartyRecord`s, persists, and broadcasts. Threshold
crossings fire `dvergr_party_rank_changed`.

### Display

- **`de_party_ladder`** console command — `#rank  owner  rating  W/L  (team size)`.
- ServerGuide **party Codex page** (in `guidance.rankings.yaml`).

## ServerGuide Integration

New trigger types (added the same way as `dvergr_duel_won`):

| Trigger id | Fired when | Subject | Extra |
|---|---|---|---|
| `dvergr_party_duel_won` | A party duel resolves | winning owner name | `winSize`, `loserOwner`, `mvpCaste` |
| `dvergr_party_rank_changed` | Party ladder threshold crossed | owner name | `rank`, `rating` |

`mvpCaste` = the winning member that scored the most subdues, available for
templating / a small MVP reward. Rewards flow through ServerGuide's
`RewardDispatcher` as usual.

## Open Questions (resolve after in-game verification)

- Whether benched members should be visually distinct (a status icon) so
  spectators can read the score at a glance — likely reuse the existing
  `CompanionStatusIconPatch`.
- `MaxPartySize` and the XP scaling constant — first-pass values.
- Cross-client authority when the two owners are on different machines (the 1v1
  path already navigates this via `ClaimOwnership`; party needs a 2+ player pass).
