# Wagered Tournaments & Staked Duel Invites

Two ways for players to put a companion on the line for currency, built on the
existing escrow tournament machinery ([Tournaments.md](Tournaments.md)) and the
existing non-lethal duel ([Duel-Arena.md](Duel-Arena.md)):

1. **Player-started tournaments** — anyone pays a stake to open a fixed-size
   bracket. One Coin tournament and one Valcoin tournament may run at a time,
   alongside the free admin-run one that already existed.
2. **Duel invites** — a posted, staked 1v1 challenge that any player can accept
   by matching the stake. The winner takes both.

Everything is **server-authoritative**, persisted per world, and synced to
clients as a read-only snapshot, exactly like the ladders.

---

## The two currencies, and why they behave differently

| | **Coins** (vanilla `Coins`) | **Valcoins** (Valheim Donations) |
|---|---|---|
| Where they live | the player's inventory | a backend ledger, server-side |
| Who can take them | only the owning client | only the server |
| Charged | client-side, before the request is sent | server-side, asynchronously |
| Paid out | server tells the client to add the items | credited through the donations ledger |
| Needs another mod | no | yes — Valheim Donations |

This split runs through the whole feature and is the single most important thing
to hold in mind when reading the code. It is the reason every wager entry point
is **callback-shaped** rather than returning a `bool`: a Valcoin stake is a
network round trip to a remote ledger, and an entry must not be accepted — nor a
prize paid — until that call has actually settled.

`src/Economy/WagerService.cs` owns that distinction so nothing above it has to.

### Coins are taken on the client, on trust

Vanilla Coins are inventory items, and only the owning client can remove an item
from its own inventory. So the client takes the stake first and **reports** the
amount with its request; the server records it so it can be refunded. That is the
same trust model the rest of the competitive suite already uses (clients report
their own duel results). A forged stake report cheats the player out of nothing
the system wasn't going to refund to them anyway.

### Valcoins required a new bridge

Lost Scrolls II has never touched a Valcoin amount. Until now the only path was
one-way and indirect: a ServerGuide quest sets a `VC.Q.<id>` player key, and
Valheim Donations prices it from its own `valcoin_quests.yaml`. Deliberately —
so a sibling mod can never inflate a payout.

There was no **debit** path at all, and one could not be faked locally: the
ledger is backend-authoritative, and a local `CoinManager` deduction is silently
reverted the next time any backend response syncs the balance.

So a small server-side API was added **to the donations plugin**:
`ValcoinWallet.Charge/Credit` (`valheim-donations/valheim-plugin/ValcoinWallet.cs`).
It is a thin wrapper over endpoints that already existed — `/api/spend` for a
debit (the backend validates the sku by regex, not against a catalog, so an
ecosystem sku like `eco_ls_duel_stake` works as-is) and `/api/admin/grant` for a
credit (whose own docstring names "event prizes, refunds" as the use case, and
for which the plugin already holds the bearer token).

Lost Scrolls II calls it **by reflection** (`src/Economy/ValcoinBridge.cs`), so
donations stays an optional soft dependency. If the plugin is absent, or is an
older build without the wallet API, Valcoin wagers are refused with a clear
message and **Coin wagers keep working**.

> **Deployment note:** Valcoin wagers need the donations plugin rebuilt with
> `ValcoinWallet.cs`. Until then the Valcoin buttons appear but are refused.

### What is a transfer, and what is minted

| Payout | Amount | How |
|---|---|---|
| Duel-invite purse | exactly the two stakes | moved directly (`WagerService.Pay`) — nothing created |
| Coin tournament purse | more than the entry fees collect | granted by this mod (vanilla items, no real-money meaning) |
| Valcoin tournament purse | more than the entry fees collect | **minted by the donations mod**, via the quest key (uncapped — see below) |

The last row is the one that matters. A 4-player Valcoin tournament collects 40
but pays 100, so 60 come from nowhere. This mod does not get to decide that: it
fires `dvergr_tournament_prize`, the guidance entry sets
`VC.Q.ls_tournament_prize`, and the server's own `valcoin_quests.yaml` says what
that is worth. `Wagers/TournamentValcoinPrize` in this mod's config is **display
only** — it is what the panel and Discord announce, and it must be kept in step
with the donations config by hand.

### The quest daily cap, and how it was resolved

Worth knowing, because it silently broke this prize on first deployment.

The donations backend clamps `period: daily` quests to `quest_daily_cap`
(**8 coins/day**), awarding `min(coins, remaining)`. That is right for the
login-habit dailies it was built for and wrong for a prize won by placing first:
a 100-coin purse declared `daily` paid **8 at best, and 0 to a champion who had
already done their dailies that morning**. `period: once` was cap-exempt but pays
a given character only once ever, so it could not carry a repeatable prize either.

Fixed on the donations side (plugin **5.20.0** + backend **0.10.0**) by separating
the two ideas that had been conflated into `period`: `valcoin_quests.yaml` entries
now take a **`capped:`** field, and an entry marked `capped: false` is paid in full
and does not consume the player's daily allowance. `ls_tournament_prize` and the
five `ls_bounty_t*` entries ship uncapped.

**Exempt is not unlimited.** The `UNIQUE(steam64, quest_id, period_key)` dedup
still applies, so an uncapped `daily` quest pays at most **once per UTC day per
quest id** — and for these payouts that dedup, not the coin cap, is what bounds a
fabricated report. Practically: a player can bank the tournament purse once a day,
and one payout per bounty tier per day.

> **Minimum versions.** A plugin sending `capped: false` to a backend below
> 0.10.0 has the field silently ignored and the prize capped again. Both halves
> must move together.

---

## Tournaments

### Slots

`TournamentService` used to hold one tournament. It now holds a **book** of them,
keyed by slot:

| Key | What |
|---|---|
| `""` | the free admin-run tournament — unchanged: any mode, any format, any size, admin-driven round activation |
| `"coins"` | the Coin-staked player tournament |
| `"valcoin"` | the Valcoin-staked player tournament |

One tournament per slot is exactly what enforces *"only one Valcoin and one Coin
tournament at a time"*. Every mutating method takes the slot key as its first
argument. A `tournament.<world>.json` written by an older build still loads: the
reader falls back to treating a bare tournament object as the free slot, so an
in-progress bracket survives the upgrade.

### How a wagered tournament differs from the free one

Only in these four ways:

1. **Anyone can open one** by paying the entry fee. That fee also **pays the
   host's own entry** — they still lock a totem to claim their slot, but they are
   never charged twice. (If they never enter, or it is cancelled first, the held
   fee is refunded.)
2. **Fixed size** (`Wagers/BracketSize`, default 4), 1v1, single elimination, and
   it **begins automatically** the moment it fills. There is no admin to press
   Begin on a player-run event.
3. **Players choose the venue.** A match is summoned only once *both* owners have
   pressed **Ready to Fight** — see below.
4. **Everything is refunded** if the bracket never fills, and the champion is
   paid a purse.

### Ready-up: why matches are not auto-summoned

The free tournament's admin presses "Activate Round" and every companion is
summoned beside its owner, wherever that owner happens to be standing. For a
player-run event that is wrong — it drops one duelist into the other's terrain,
or into a fight, or underwater.

Instead each pairing carries `aReady` / `bReady`. The two owners agree on a venue
however they like (an arena, a field, a rooftop), walk there, and each presses
**Ready to Fight** on the F7 panel. When the second flag lands, the server
summons both companions there at full health, locked onto each other by the
existing `DuelOpponentId` assignment so a simultaneous match can't cross-target.
`activated` makes that idempotent.

This is also what "the players decide the location of the duel" means in the
player-facing text, and it is stated on the panel, on the read-only board, and in
the bracket announcement.

### Lifecycle

```
open (stake taken) → registration → [fills] → running → complete
                          │                      │
                          │                      ├─ each pairing: both Ready → summoned → fought → resolved
                          │                      └─ champion paid, summary posted
                          └─ never fills / cancelled → every stake + totem returned
```

Escrowed totems are **not** handed back the instant a bracket completes. The
winning client still has to reseal its live companion (with the XP it just
earned) and report that payload back — a 1 Hz pass. Returning immediately would
hand the champion a totem holding its pre-final state, so `Tick()` waits a short
grace (`CompletionGraceSeconds`, 6 s) first. The same applies to a settled duel
invite.

---

## Duel invites

A flat, two-sided record (`DuelInvite`) rather than a one-match tournament:

- **Post** — stake, plus a companion's Communion Totem, held by the server.
- **Accept** — any other player matches the stake and locks their own totem.
- **Ready** — both sides, wherever they have met up; then both companions are
  summoned and fight.
- **Settle** — the winner takes both stakes. Both totems come home, the winner's
  resealed with whatever XP it gained.
- **Withdraw** — the poster tears the whole thing down (both sides made whole);
  the challenger backing out puts the invite back on the board for someone else.
  Refused once the duel is running: at that point the wager is live and only a
  result settles it.

**One invite per player at a time**, as poster *or* challenger — that is the
"no dual invites" rule. Any number of players may have one open at once.

An open invite that nobody accepts expires after `Wagers/RegistrationMinutes`,
refunding the poster.

The result arrives through the **same duel report** the ladder and the tournament
already receive, so an invite duel is an ordinary duel underneath — same
non-lethal floor, same subdue latch, same ladder credit.

### Why invites are not tournaments

They share the escrow / summon / reseal plumbing (`LeaderboardSync` +
`TournamentClient`, which is context-tagged so it can drive either), but nothing
else. An invite has no bracket, no rounds, no seeding, and a completely different
lifetime. Folding it into `TournamentState` would have produced a "tournament"
with one match and a pile of permanently-unused fields.

---

## Discord

Every server-wide beat posts **straight to the ServerGuide webhook** from the
server, via `ServerGuideBridge.AnnounceDiscord` →
`DiscordAnnouncer.AnnounceRaw`. That method is public, server-side, and reads the
webhook URL from the server's own config — it is what a `type: discord` reward
ultimately calls anyway, after an RPC hop.

Going direct means **no new trigger types, no new template variables, and no
ServerGuide release** for any of it:

| Event | Posted |
|---|---|
| Tournament opened | who opened it, currency, entry, purse, bracket size |
| Player enters | who, with which companion, and the running count |
| Bracket begins | the full round-1 draw, plus the ready-up instruction |
| Duel starts | both companions and both owners |
| Duel won | winner, loser, round, which tournament |
| Who's next | the pairings still to fight this round |
| Champion | winner and companion |
| Summary | format, entry/purse, final standings, every result |
| Invite posted / accepted / started / won / withdrawn / expired | both sides and the purse |

Per-player rewards (the Valcoin purse, rank milestones) still go through the
trigger + guidance path, because those do target one player.

If `DiscordWebhookUrl` is unset the announcements are a logged no-op and
everything else works.

---

## Config (`Wagers` section)

| Key | Default | Meaning |
|---|---|---|
| `WagersEnabled` | `true` | Master switch. Off leaves only the free admin tournaments. |
| `BracketSize` | `4` | Entrants a wagered tournament needs; it starts automatically when full. |
| `RegistrationMinutes` | `30` | How long a wagered tournament / open invite lives before it is cancelled and refunded. `0` = never expire. |
| `TournamentCoinFee` | `100` | Coins to open, and to enter. |
| `TournamentCoinPrize` | `999` | Coins to the champion. |
| `TournamentValcoinFee` | `10` | Valcoins to open, and to enter. |
| `TournamentValcoinPrize` | `100` | **Display only** — the real amount lives in the donations mod's `valcoin_quests.yaml`. |
| `DuelCoinStake` | `100` | Coins each side stakes on an invite; winner takes both. |
| `DuelValcoinStake` | `10` | Valcoins each side stakes; winner takes both. No Valcoins are created. |

## Server setup

1. Rebuild + deploy **Valheim Donations** with `ValcoinWallet.cs` (Valcoin wagers
   only; Coin wagers need nothing).
2. Add to `BepInEx/config/valcoin_quests.yaml` — note `capped: false`, which is
   what keeps the purse out of the 8-coin daily allowance (needs donations
   plugin 5.20.0 + backend 0.10.0):
   ```yaml
   quests:
     ls_tournament_prize:
       name: "Tournament Champion"
       coins: 100        # keep in step with Wagers/TournamentValcoinPrize
       period: daily     # once per UTC day
       capped: false     # paid in full, doesn't eat the daily allowance
   ```
   The five bounty payouts (`ls_bounty_t1` … `ls_bounty_t5`) belong here too, on
   the same `capped: false` footing. The donations plugin now ships all six in its
   generated template.
3. Set `DiscordWebhookUrl` in the ServerGuide config for the announcements.
4. Deploy `guidance.wagers.yaml` (bundled in the Quest pack).

## Player surfaces

- **F7 panel** — open/enter/withdraw a tournament, ready up, post/accept/withdraw
  a duel invite, plus the free tournament's admin controls. The status text spells
  out fees, purse, bracket size, refunds and the ready-up rule, because this is
  the one place a player is guaranteed to read before spending anything.
- **View Bracket** — the read-only rune board, now rendering every active
  tournament plus the invite board.
- **Console** — `de_tournament open <coins|valcoin> | join [slot] | ready [slot] |
  withdraw [slot] | bracket`, and `de_duel_invite post <coins|valcoin> |
  accept <player> | ready | withdraw | list`.

## Testing

See [Testing.md](Testing.md) §22.
