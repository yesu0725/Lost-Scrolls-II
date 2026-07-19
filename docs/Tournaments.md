# Tournament System

A server-authoritative bracket runner for companion duels — works for **both**
1v1 ([Duel-Arena.md](Duel-Arena.md)) and **party** ([Party-Duels.md](Party-Duels.md))
formats. Seeds from the ladder ratings ([Ranking.md](Ranking.md)), runs the
actual fights by reusing the existing duel / party-duel modes, and delivers every
prompt and prize through ServerGuide.

> **UI + escrow update (2026-07-17, unverified).** Entry is now a **UI** action:
> press **`F7`** to open the tournament panel and **Lock Totem → Enter** (or
> `de_tournament join` in the console). Registering **escrows** the companion's
> **Communion Totem** (it leaves your inventory); on the admin's **Activate Round**
> the server **auto-summons** each pairing's companion(s) beside their owner in duel
> mode against the **assigned** opponent, then **reseals + despawns** them when the
> match resolves. Totems are returned on withdraw / **admin release** / cancel /
> completion. Admin controls run through an **admin-authenticated RPC** (works from
> a remote admin client, not just the host). The champion + every match win also
> **broadcast to Discord**. See [Changelog.md](Changelog.md) (2026-07-17) and
> [Testing.md](Testing.md) §21 for the full flow and test steps.

> **Status: Phase E built (2026-07-09), unverified.** The bracket runner
> (`TournamentService` + `TournamentState`), the `de_tournament`
> `start`/`join`/`begin`/`bracket`/`forfeit`/`cancel` commands, `de_champions`,
> re-seeded single elimination with byes, per-world JSON persistence + resume,
> client snapshot sync, the three `dvergr_tournament_*` triggers + the
> `guidance.tournaments.yaml` prizes (incl. champion bundle), and a Hall of
> Champions archive are implemented and build clean — see [Testing.md](Testing.md)
> §20. **Deviations from the design below, chosen for robustness:** matches are
> **player-run** (the server announces the pairing and resolves the match from the
> same duel/party report the ladder receives) rather than the server
> teleporting/auto-starting fights; the **Arena Ward and teleport are not enforced**
> (documented convention only); admin subcommands run on the **host/server**
> (no client→server admin RPC yet). Needs a multi-player (ideally 4-entrant)
> session.

## Goals

- **Reuse, don't rebuild.** A tournament match is just a normal (party-)duel that
  the server sets up and watches. No new combat code — the bracket runner drives
  entry into the already-built duel modes and reads the already-built subdue
  result.
- **Vanilla assets only.** No custom arena prefab required. An optional **Arena
  Ward** (a vanilla ward / `PrivateArea` the admin places) marks where matches
  run and where matchmaking pins point, but is never mandatory.
- **Restart-safe.** The bracket state persists to JSON so an event survives a
  server restart mid-round.

## State Machine (server-side, persisted)

```
Idle → Registration → Seeding → Round(1) → Round(2) → … → Complete → Idle
```

Persisted to `<save folder>/LostScrollsII/tournament.<worldName>.json` (same
folder as the ladder). Reloaded on `ZNet.Awake`; an in-progress event resumes at
its current round.

## Lifecycle

1. **Start (admin).** `de_tournament start <1v1|party> [size]` opens
   registration. Announced to all players via a ServerGuide global-scope entry +
   Discord.
2. **Register (players).** Hover your companion (1v1) or your party (party) and
   press the duel / party key **inside the Arena Ward**, or run
   `de_tournament join`. Fires `dvergr_tournament_joined`. Registration closes on
   a timer or when `size` is reached.
3. **Seeding.** Entrants are ordered by ladder `rating`; byes are handed to the
   top seeds when the count isn't a power of two.
4. **Run a match.** The server pairs two entrants, pins/teleports both to the
   arena (ServerGuide `location_pin` / `teleport` reward, or a plain "report to
   the arena" prompt), announces via `dvergr_tournament_match` + Discord, then
   flips both entrants into (party-)duel mode. The existing `CompanionDamagePatch`
   subdue reports the winner up to the runner via the same `ResolveSubdue` path —
   which is why the **double-subdue fix is a hard prerequisite**: a bracket must
   advance on exactly one win event per match.
5. **No-show / forfeit.** An entrant who doesn't engage within a timeout (reuses
   the duel wait-timeout concept) forfeits; the opponent advances.
6. **Advance.** Winner moves to the next round; loser is eliminated. Repeat until
   one entrant remains.
7. **Complete.** The champion triggers `dvergr_tournament_won`; ServerGuide
   grants the prize bundle and the result is written to the ladder store + an
   archived **Hall of Champions** list.

## Admin Commands

| Command | Effect |
|---|---|
| `de_tournament start <1v1\|party> [size]` | open registration |
| `de_tournament join` | register the hovered companion/party (also bindable to the duel key inside the ward) |
| `de_tournament bracket` | print the live bracket |
| `de_tournament cancel` | abort and clear state |

Admin gating re-checks server-side (a modded client can't forge the RPC), the
same defense ServerGuide's admin commands use.

## Rewards (all via ServerGuide)

The champion's `dvergr_tournament_won` entry in `guidance.tournaments.yaml`
(`category: Tournaments`) hangs a **prize bundle** off ServerGuide's existing
`RewardDispatcher`:

- an **item** reward (vanilla prefab),
- a **buff** reward (a temporary vanilla status effect),
- a **title** via the `rename_player` reward (e.g. `Champion of Season N`),
- a **Discord** broadcast of the result.

Runner-up / participation tiers can be added the same way. No reward code lives
in Lost Scrolls II — it only fires the trigger with the templating context.

## ServerGuide Integration

New trigger types (added the same way as `dvergr_duel_won`):

| Trigger id | Fired when | Subject | Extra |
|---|---|---|---|
| `dvergr_tournament_joined` | An entrant registers | caste or `"party"` | `tournamentId` |
| `dvergr_tournament_match` | A match is about to start | caste | `round`, `opponent` |
| `dvergr_tournament_won` | The tournament concludes | champion caste / owner | `tournamentId`, `bracketSize` |

Templating vars `{round}`, `{opponent}`, `{tournamentId}`, `{bracketSize}` are
added for guidance/reward text.

## Suggested Extras (nice-to-have)

- **Spectating is free** — fights are real creatures under vanilla AI, so other
  players just watch. A `de_tournament bracket` printout is enough structure.
- **Wagering** — an optional resource buy-in pooled into the champion's prize
  (would need a small escrow on the store; deferred).
- **Scheduled tournaments** — an optional server config to auto-start an event on
  a cadence, using the same runner.

## Open Questions (resolve after in-game verification)

- Whether teleporting entrants to the arena is desirable or too disruptive vs. a
  soft "report to the arena" pin.
- Round/no-show timeouts — first-pass values.
- Needs a **4+ player** session to properly exercise a multi-round bracket.
