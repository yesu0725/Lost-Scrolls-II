using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using LostScrollsII.Economy;
using LostScrollsII.Integration;
using UnityEngine;

namespace LostScrollsII.Ranking
{
    // Server-authoritative tournament bracket runner (docs/Tournaments.md,
    // docs/Wagers.md). Works for both 1v1 and party formats by reusing the existing
    // duel modes: the server owns the bracket, announces pairings, and resolves
    // each match from the SAME duel/party result reports the ladders already
    // receive — it never remote-drives combat.
    //
    // SLOTS. Several tournaments can run at once, one per slot key:
    //   ""        the free admin-run tournament (the original behaviour — any
    //             mode, any format, any size, admin-driven round activation)
    //   "coins"   a Coin-staked player tournament
    //   "valcoin" a Valcoin-staked player tournament
    // One tournament per slot is what enforces "only 1 Valcoin and 1 Coin
    // tournament at a time", and every mutating method therefore takes the slot key
    // as its first argument.
    //
    // WAGERED TOURNAMENTS differ from the free one in four ways, and only these:
    //   * any player can open one, by paying the entry fee (which doubles as their
    //     own entry — they are not charged twice; they still lock a totem to claim
    //     their slot),
    //   * they are a fixed size (Plugin.WageredBracketSize, default 4), 1v1,
    //     single elimination, and BEGIN AUTOMATICALLY the moment they fill,
    //   * a match is summoned only when BOTH owners mark themselves ready, so the
    //     players choose where the duel happens rather than being teleported,
    //   * every stake is refunded if the bracket never fills (expiry or cancel),
    //     and the champion is paid a purse.
    //
    // Authority: the server/host mutates _book; clients hold a read-only snapshot
    // pushed via LeaderboardSync (mirrors the ladder).
    public static class TournamentService
    {
        private static TournamentBook _book = new TournamentBook();      // server authoritative
        private static TournamentBook _snapshot = new TournamentBook();  // client view (host aliases _book)

        public const string FreeSlot = "";
        public const string CoinSlot = "coins";
        public const string ValcoinSlot = "valcoin";

        // ---- read-only views (safe on clients) --------------------------------

        public static TournamentBook Snapshot => _snapshot;

        /// Every tournament a client should render, active ones first.
        public static IEnumerable<TournamentState> All
            => (_snapshot?.tournaments ?? new List<TournamentState>()).Where(t => t != null && t.active);

        public static TournamentState Get(string key) => _snapshot?.Get(key ?? FreeSlot);

        public static TournamentState FindByEntrant(string entrantId) => _snapshot?.ForEntrant(entrantId);

        public static bool AnyActive => All.Any();

        /// The free admin tournament, for the callers that predate slots.
        public static TournamentState Legacy => Get(FreeSlot);

        // Per-match no-show timeout (seconds). 0 disables auto-forfeit.
        public static float MatchTimeoutSeconds = 0f;

        // ---- Persistence ------------------------------------------------------

        private static string StatePath() => Path.Combine(LeaderboardStore.DataDir(), $"tournament.{LeaderboardStore.WorldTag()}.json");
        private static string ChampionsPath() => Path.Combine(LeaderboardStore.DataDir(), $"champions.{LeaderboardStore.WorldTag()}.json");

        public static void LoadForCurrentWorld()
        {
            try
            {
                var path = StatePath();
                if (File.Exists(path))
                {
                    _book = CompetitiveJson.ReadTournamentBook(File.ReadAllText(path)) ?? new TournamentBook();
                    if (_book.tournaments == null) _book.tournaments = new List<TournamentState>();
                    foreach (var t in _book.tournaments)
                    {
                        if (t == null) continue;
                        if (t.entrants == null) t.entrants = new List<TournamentEntrant>();
                        if (t.matches == null) t.matches = new List<TournamentMatch>();
                    }
                    Plugin.Log.LogInfo($"[tourney] resumed {_book.tournaments.Count} tournament slot(s): " +
                        string.Join(", ", _book.tournaments.Select(t => $"'{t.key}':{t.phase}/{t.entrants.Count}")));
                }
                else _book = new TournamentBook();
            }
            catch (Exception e) { _book = new TournamentBook(); Plugin.Log.LogWarning($"[tourney] load failed: {e.Message}"); }
            _snapshot = _book;
        }

        private static void Save()
        {
            try { File.WriteAllText(StatePath(), CompetitiveJson.Write(_book, true)); }
            catch (Exception e) { Plugin.Log.LogWarning($"[tourney] save failed: {e.Message}"); }
        }

        public static string SerializeSnapshot() => CompetitiveJson.Write(_book ?? new TournamentBook());

        // One-shot boot diagnostic: round-trip a populated dummy book through the
        // same serializer Save/Broadcast use, and log whether the tournament,
        // entrant and match lists survive. Guards against a repeat of the Unity 6
        // JsonUtility bug (it silently dropped List<[Serializable] class> fields,
        // so the server registered joins but every client forever saw an empty
        // board) — the hand-rolled CompetitiveJson replaced it; this proves the
        // path at boot, now through the extra nesting level the book adds.
        public static void SerializerSelfTest()
        {
            try
            {
                var probe = new TournamentState { key = "valcoin", currency = "valcoin", active = true, mode = "1v1", phase = "registration", size = 4, entryFee = 10 };
                probe.entrants.Add(new TournamentEntrant { entrantId = "probe-id", ownerId = 42L, ownerName = "probe", label = "Probe", level = 3, feePaid = 10 });
                probe.matches.Add(new TournamentMatch { round = 1, aId = "probe-id", aLabel = "Probe" });
                var book = new TournamentBook();
                book.tournaments.Add(probe);

                var json = CompetitiveJson.Write(book);
                var back = CompetitiveJson.ReadTournamentBook(json);
                var slot = back?.Get("valcoin");
                bool jsonHasLists = json.Contains("entrantId");
                Plugin.Log.LogInfo($"[tourney] serializer self-test: jsonHasLists={jsonHasLists}, " +
                    $"slots={back?.tournaments?.Count ?? -1}, entrants={slot?.entrants?.Count ?? -1}, " +
                    $"matches={slot?.matches?.Count ?? -1}, fee={slot?.entryFee ?? -1}, jsonLen={json.Length}.");
                if (!jsonHasLists || slot == null || slot.entrants.Count != 1 || slot.matches.Count != 1 || slot.entryFee != 10)
                    Plugin.Log.LogWarning($"[tourney] serializer self-test FAILED — data is being dropped. json={json}");
            }
            catch (Exception e) { Plugin.Log.LogWarning($"[tourney] serializer self-test threw: {e}"); }
        }

        public static void ApplySnapshot(string json)
        {
            try
            {
                var b = CompetitiveJson.ReadTournamentBook(json) ?? new TournamentBook();
                if (b.tournaments == null) b.tournaments = new List<TournamentState>();
                foreach (var t in b.tournaments)
                {
                    if (t == null) continue;
                    if (t.entrants == null) t.entrants = new List<TournamentEntrant>();
                    if (t.matches == null) t.matches = new List<TournamentMatch>();
                }
                _snapshot = b;
            }
            catch (Exception e) { Plugin.Log.LogWarning($"[tourney] snapshot apply failed: {e.Message}"); }
        }

        // ---- server-side slot access -----------------------------------------

        private static TournamentState Live(string key) => _book?.Get(key ?? FreeSlot);

        private static TournamentState LiveForEntrant(string entrantId) => _book?.ForEntrant(entrantId);

        private static void Put(TournamentState s)
        {
            if (s == null) return;
            var existing = _book.Get(s.key);
            if (existing != null) _book.tournaments.Remove(existing);
            _book.tournaments.Add(s);
        }

        private static void Drop(string key)
        {
            var existing = _book.Get(key ?? FreeSlot);
            if (existing != null) _book.tournaments.Remove(existing);
        }

        // ---- opening a tournament --------------------------------------------

        // The free, admin-run tournament (the original Start). Always the "" slot.
        public static string Start(string mode, int size, string eliminationType = "single")
        {
            mode = (mode ?? "1v1").ToLowerInvariant();
            if (mode != "1v1" && mode != "party") return "mode must be 1v1 or party.";
            eliminationType = (eliminationType ?? "single").ToLowerInvariant();
            if (eliminationType != "single" && eliminationType != "double" && eliminationType != "round_robin")
                return "elimination type must be single, double, or round_robin.";

            var existing = Live(FreeSlot);
            if (existing != null && existing.active) return "a tournament is already running — cancel it first.";

            // 0/omitted means "use the configured MaxEntrants cap" rather than
            // unlimited — an uncapped bracket no longer makes sense once escrow +
            // auto-summon holds every entrant's companion for the whole run.
            int cap = Mathf.Max(1, Plugin.MaxEntrants.Value);
            int effectiveSize = size <= 0 ? cap : Mathf.Min(size, cap);

            var s = new TournamentState
            {
                key = FreeSlot,
                active = true,
                mode = mode,
                phase = "registration",
                size = effectiveSize,
                eliminationType = eliminationType,
                seasonId = 1,
                openedTicks = DateTime.UtcNow.Ticks,
            };
            Put(s);
            Save();
            Broadcast();

            Announce($"🏟️ A **{mode}** tournament ({DisplayType(eliminationType)}) is open for registration — {effectiveSize} slots. " +
                     "Seal a companion into a Communion Totem and enter from the Tournament panel (F7).");
            return $"Tournament ({mode}, {eliminationType}) open for registration — {effectiveSize} slots.";
        }

        // A player-started, staked tournament (docs/Wagers.md). The host's opening
        // fee IS their entry fee — they still lock a totem to claim their slot, but
        // they are never charged twice. Asynchronous, because a Valcoin charge is a
        // remote ledger call: `done(ok, message)` fires when the stake settles.
        public static void StartWagered(WagerCurrency currency, long hostId, string hostName,
            Action<bool, string> done)
        {
            var key = Wager.Key(currency);
            if (currency != WagerCurrency.Coins && currency != WagerCurrency.Valcoin)
            { done?.Invoke(false, "Unknown currency."); return; }

            if (!WagerService.IsAvailable(currency, out var why)) { done?.Invoke(false, why); return; }

            var existing = Live(key);
            if (existing != null && existing.active)
            { done?.Invoke(false, $"A {Wager.Display(currency)} tournament is already running — only one at a time."); return; }

            int fee = Wager.TournamentFee(currency);
            int size = Mathf.Max(2, Plugin.WageredBracketSize.Value);

            // The stake is taken FIRST. Only a settled charge opens the tournament,
            // so a failed/declined payment can never leave a free bracket behind.
            WagerService.Charge(currency, hostName, fee, ValcoinBridge.SkuTournamentEntry,
                $"opened a {Wager.Display(currency)} tournament", CoinsAlreadyPaid(currency, fee), (ok, msg) =>
                {
                    if (!ok) { done?.Invoke(false, msg ?? "The stake could not be taken."); return; }

                    var s = new TournamentState
                    {
                        key = key,
                        currency = key,
                        entryFee = fee,
                        prize = Wager.TournamentPrize(currency),
                        hostId = hostId,
                        hostName = hostName ?? string.Empty,
                        hostCredit = fee,      // spend it on the host's own entry
                        active = true,
                        mode = "1v1",
                        phase = "registration",
                        size = size,
                        eliminationType = "single",
                        seasonId = 1,
                        openedTicks = DateTime.UtcNow.Ticks,
                    };
                    Put(s);
                    Save();
                    Broadcast();

                    Announce($"🏟️ **{hostName}** has opened a **{size}-player {Wager.Display(currency)} tournament**! " +
                             $"Entry: **{fee} {Wager.Display(currency)}** · Champion's purse: **{s.prize} {Wager.Display(currency)}**. " +
                             $"Seal a companion into a Communion Totem and enter from the Tournament panel (F7) — " +
                             $"the bracket starts the moment it fills.");
                    Plugin.Log.LogInfo($"[tourney] {hostName} opened the {key} tournament ({fee} fee, {size} slots).");

                    done?.Invoke(true, $"Your {Wager.Display(currency)} tournament is open — {size} slots. " +
                                       "Lock your own totem to claim your place; your entry is already paid.");
                });
        }

        // A Coin charge is taken by the CLIENT before it asks the server to act, so
        // by the time we get here the coins are already gone from the inventory and
        // the server is only recording the amount. Valcoin charges are real remote
        // debits and pass 0 here. See WagerService for the full rationale.
        private static int CoinsAlreadyPaid(WagerCurrency c, int fee) => c == WagerCurrency.Coins ? fee : 0;

        // ---- registration -----------------------------------------------------

        // Player registration. entrantId/label/caste/rating are resolved by the
        // caller (client). The companion(s) are escrowed as their Communion Totem
        // payload(s): `totemPayload` for a 1v1 entry, `teamPayloads` for a party.
        //
        // `clientPaidCoins` is what the joining client says it already removed from
        // its own inventory (Coin tournaments only). A Valcoin entry is charged
        // here, asynchronously, which is why the result arrives through `done`
        // rather than as a return value — and why the entrant is only added once
        // the stake has actually settled.
        public static void Join(string key, string entrantId, long ownerId, string ownerName, string label, int caste,
            int seedRating, string totemPayload, List<string> teamPayloads, int level,
            int clientPaidCoins, Action<bool, string> done)
        {
            var s = Live(key);
            if (s == null || !s.active || s.phase != "registration") { done?.Invoke(false, "no tournament is accepting entries."); return; }
            if (string.IsNullOrEmpty(entrantId)) { done?.Invoke(false, "could not identify your entry."); return; }
            if (s.Find(entrantId) != null) { done?.Invoke(false, "already registered."); return; }
            if (s.size > 0 && s.entrants.Count >= s.size) { done?.Invoke(false, "the tournament is full."); return; }

            // One entry per owner (you field one companion / one party).
            if (s.entrants.Any(e => e.ownerId == ownerId)) { done?.Invoke(false, "you already have an entry in this tournament."); return; }

            // Level gate — 1v1 only (caste >= 0). A party entrant fields multiple
            // companions so a single required level doesn't map cleanly onto it;
            // per docs/Tournaments.md this event is 1v1-only, so party is left ungated.
            int required = Plugin.RequiredEntrantLevel.Value;
            if (required > 0 && caste >= 0 && level != required)
            { done?.Invoke(false, $"only Level {required} companions may enter this tournament."); return; }

            // `fromHostCredit` marks the entry as paid out of the host's opening fee
            // rather than a fresh charge, because the two roll back differently.
            void Register(int feePaid, bool fromHostCredit = false)
            {
                // A Valcoin charge is a remote round trip, and the tournament can be
                // cancelled or filled while it is in flight. Re-check before adding —
                // and hand the stake straight back if the slot is gone, or the
                // player has paid for a place that no longer exists.
                var live = Live(key);
                if (live != s || !s.active || s.phase != "registration"
                    || (s.size > 0 && s.entrants.Count >= s.size)
                    || s.entrants.Any(e => e.ownerId == ownerId))
                {
                    if (fromHostCredit)
                    {
                        // Nothing was charged for this attempt — the fee is still the
                        // host's prepaid credit. Put it back on the tournament so they
                        // can try again, or be refunded when it is cancelled. (Paying
                        // it out here would be wrong twice over: they'd get the fee
                        // back AND keep a claim on a slot they still hold.)
                        s.hostCredit = feePaid;
                        done?.Invoke(false, "Your entry could not be placed — your paid slot is still held.");
                        return;
                    }

                    // Only roll back what WE took — a Coin stake was taken by the
                    // client and is refunded by the client-facing layer on any
                    // rejection (see WagerService.RollBackOwnCharge).
                    if (feePaid > 0)
                        WagerService.RollBackOwnCharge(Wager.Parse(s.currency), ownerId, ownerName, feePaid,
                            "the tournament closed before your entry landed");
                    done?.Invoke(false, "The tournament closed before your entry landed — your stake is returned.");
                    return;
                }

                s.entrants.Add(new TournamentEntrant
                {
                    entrantId = entrantId, ownerId = ownerId, ownerName = ownerName,
                    label = label, caste = caste, seedRating = seedRating, level = level,
                    totemPayload = totemPayload ?? string.Empty,
                    teamPayloads = teamPayloads ?? new List<string>(),
                    feePaid = feePaid,
                });
                Save();
                Broadcast();

                if (s.entryFee > 0)
                    Announce($"⚔️ **{ownerName}** enters the {Wager.Display(Wager.Parse(s.currency))} tournament with **{label}**" +
                             (level > 0 ? $" (Lv{level})" : "") +
                             $" — {s.entrants.Count}/{s.size} entered.");

                done?.Invoke(true, $"Registered '{label}' ({s.entrants.Count}" + (s.size > 0 ? $"/{s.size}" : "") + ").");

                // A wagered bracket starts by itself the instant it is full — there
                // is no admin to press Begin on a player-run event.
                if (s.entryFee > 0 && s.size > 0 && s.entrants.Count >= s.size)
                    Begin(key);
            }

            // Free tournament, or the host claiming the slot their opening fee
            // already paid for: nothing to charge.
            if (s.entryFee <= 0) { Register(0); return; }
            if (ownerId == s.hostId && s.hostCredit > 0)
            {
                int credit = s.hostCredit;
                s.hostCredit = 0;
                Register(credit, fromHostCredit: true);
                return;
            }

            var currency = Wager.Parse(s.currency);
            WagerService.Charge(currency, ownerName, s.entryFee, ValcoinBridge.SkuTournamentEntry,
                "tournament entry", clientPaidCoins, (ok, msg) =>
                {
                    if (!ok) { done?.Invoke(false, msg ?? "The entry fee could not be taken."); return; }
                    Register(s.entryFee);
                });
        }

        // Collects all escrowed totem payloads on an entrant (1v1 single + party
        // list) so a caller can return them to the owner.
        public static List<string> PayloadsOf(TournamentEntrant e)
        {
            var list = new List<string>();
            if (e == null) return list;
            if (!string.IsNullOrEmpty(e.totemPayload)) list.Add(e.totemPayload);
            if (e.teamPayloads != null) list.AddRange(e.teamPayloads.Where(p => !string.IsNullOrEmpty(p)));
            return list;
        }

        // A player withdraws their OWN entry during registration. Returns the
        // escrowed payloads to hand back (empty if nothing to withdraw); any stake
        // they paid is refunded at the same time, which is what makes "withdraw
        // your Dvergr and get your coins back" one action rather than two.
        public static List<string> Withdraw(string key, long ownerId, out string status)
        {
            status = "no tournament is accepting entries.";
            var s = Live(key);
            if (s == null || !s.active || s.phase != "registration") return new List<string>();
            var ent = s.entrants.FirstOrDefault(e => e.ownerId == ownerId);
            if (ent == null) { status = "you have no entry to withdraw."; return new List<string>(); }

            var payloads = PayloadsOf(ent);
            s.entrants.Remove(ent);
            RefundEntrant(s, ent, "withdrew from the tournament");
            Save();
            Broadcast();
            status = ent.feePaid > 0
                ? $"Withdrew '{ent.label}'. Your companion totem and {ent.feePaid} {Wager.Display(Wager.Parse(s.currency))} are returned."
                : $"Withdrew '{ent.label}'. Your companion totem is returned.";
            return payloads;
        }

        // Admin releases a specific entrant (by id or owner name), returning its
        // escrowed totem(s). Allowed in registration (removes the entry) or while
        // running (forfeits + frees the totem). Returns the payloads + the owner id.
        public static List<string> ReleaseEntrant(string key, string idOrName, out long ownerId, out string status)
        {
            ownerId = 0L; status = "no tournament is running.";
            var s = Live(key);
            if (s == null || !s.active) return new List<string>();
            var ent = s.entrants.FirstOrDefault(e =>
                e.entrantId == idOrName || string.Equals(e.ownerName, idOrName, StringComparison.OrdinalIgnoreCase)
                || string.Equals(e.label, idOrName, StringComparison.OrdinalIgnoreCase));
            if (ent == null) { status = $"no entrant '{idOrName}'."; return new List<string>(); }

            ownerId = ent.ownerId;
            var payloads = PayloadsOf(ent);

            if (s.phase == "registration")
            {
                s.entrants.Remove(ent);
                // Released before a shot was fired: the stake goes back.
                RefundEntrant(s, ent, "released from the tournament");
            }
            else
            {
                // Running: forfeit any undecided current-round match, then free it.
                // No refund — the bracket is under way and the stake is in play.
                ent.eliminated = true;
                foreach (var m in s.matches)
                {
                    if (m.round != s.currentRound || !string.IsNullOrEmpty(m.winnerId)) continue;
                    if (m.aId == ent.entrantId || m.bId == ent.entrantId)
                    {
                        var winnerId = m.aId == ent.entrantId ? m.bId : m.aId;
                        if (!string.IsNullOrEmpty(winnerId)) ResolveMatch(s, m, winnerId);
                        break;
                    }
                }
                // Clear the escrow so it isn't returned twice at tournament end.
                ent.totemPayload = string.Empty;
                ent.teamPayloads = new List<string>();
            }
            Save();
            Broadcast();
            status = $"Released '{ent.label}' — totem returned to {ent.ownerName}.";
            return payloads;
        }

        // Every still-escrowed payload in one tournament keyed by owner id — used to
        // return all totems when it ends or is cancelled.
        public static Dictionary<long, List<string>> DrainAllEscrow(string key)
        {
            var byOwner = new Dictionary<long, List<string>>();
            var s = Live(key);
            if (s == null) return byOwner;
            foreach (var e in s.entrants)
            {
                var payloads = PayloadsOf(e);
                if (payloads.Count == 0) continue;
                if (!byOwner.TryGetValue(e.ownerId, out var list)) { list = new List<string>(); byOwner[e.ownerId] = list; }
                list.AddRange(payloads);
                e.totemPayload = string.Empty;
                e.teamPayloads = new List<string>();
            }
            return byOwner;
        }

        public static string Begin(string key)
        {
            var s = Live(key);
            if (s == null || !s.active || s.phase != "registration") return "no tournament is in registration.";
            if (s.entrants.Count < 2) return "need at least 2 entrants.";

            s.phase = "running";
            s.bracketSize = s.entrants.Count;
            s.currentRound = 1;
            s.matches.Clear();

            var ids = s.entrants.Select(e => e.entrantId).ToList();
            switch (s.eliminationType)
            {
                case "round_robin": BuildRoundRobinSchedule(s, ids); break;
                case "double": BuildDoubleRound(s, 1); break;
                default: BuildRound(s, ids, 1); break;
            }
            Save();
            Broadcast();

            Announce($"🔔 The {Label(s)} bracket is set — **{s.entrants.Count} entrants**, round 1 begins!" +
                     (s.entryFee > 0
                        ? " Each pair fights wherever they choose: both owners open the Tournament panel (F7) and press **Ready to Fight** once they have met up."
                        : ""));
            AnnounceRoundMatches(s, 1);
            return $"Tournament begun ({s.eliminationType}) — {s.entrants.Count} entrants, round 1.";
        }

        public static string Cancel(string key)
        {
            var s = Live(key);
            if (s == null || !s.active) return "no tournament is running.";

            // Hand every still-escrowed companion totem back to its owner, and
            // refund every stake — a cancelled event must cost nobody anything.
            LeaderboardSync.ReturnEscrowToOwners(DrainAllEscrow(key));
            RefundEveryone(s, "the tournament was cancelled");

            var label = Label(s);
            Drop(key);
            Save();
            Broadcast();
            Announce($"🚫 The {label} has been cancelled — every companion totem and stake has been returned.");
            return "Tournament cancelled — companion totems and stakes returned.";
        }

        // Admin forfeit: whoever `playerName` is in an undecided current-round match
        // loses; the opponent advances.
        public static string Forfeit(string key, string playerName)
        {
            var s = Live(key);
            if (s == null || !s.active || s.phase != "running") return "no tournament round is running.";
            var ent = s.entrants.FirstOrDefault(e =>
                string.Equals(e.ownerName, playerName, StringComparison.OrdinalIgnoreCase));
            if (ent == null) return $"'{playerName}' is not an entrant.";

            foreach (var m in s.matches)
            {
                if (m.round != s.currentRound || !string.IsNullOrEmpty(m.winnerId)) continue;
                if (m.aId == ent.entrantId || m.bId == ent.entrantId)
                {
                    var winnerId = m.aId == ent.entrantId ? m.bId : m.aId;
                    if (string.IsNullOrEmpty(winnerId)) return "opponent not set — cannot forfeit a bye.";
                    Announce($"🏳️ **{ent.ownerName}** forfeits in the {Label(s)}.");
                    ResolveMatch(s, m, winnerId);
                    return $"{playerName} forfeits — opponent advances.";
                }
            }
            return $"{playerName} has no undecided match this round.";
        }

        // Admin "activate duels" for the FREE tournament: for every undecided
        // pairing in the current round, tell both owners' clients to summon their
        // escrowed companion(s) and duel the assigned opponent. Byes are skipped.
        //
        // Wagered tournaments do not use this — their matches activate when both
        // owners press Ready (SetReady), which is what lets the players pick the
        // venue instead of being summoned wherever they happen to be standing.
        public static string ActivateCurrentRound(string key)
        {
            var s = Live(key);
            if (s == null || !s.active || s.phase != "running") return "no tournament round is running.";
            int summoned = 0;
            foreach (var m in s.matches.ToList())
            {
                if (m.round != s.currentRound || !string.IsNullOrEmpty(m.winnerId)) continue;
                if (string.IsNullOrEmpty(m.bId)) continue; // bye
                if (ActivateMatch(s, m)) summoned++;
            }
            Save();
            Broadcast();
            return summoned == 0
                ? "No pairings to activate this round."
                : $"Activated {summoned} match(es) — companions summoned to duel.";
        }

        // A player marks themselves ready for their current match. Once BOTH sides
        // of a pairing are ready the match activates on the spot — the two owners
        // have met up somewhere of their own choosing and the companions appear
        // beside them.
        public static string SetReady(string key, long ownerId)
        {
            var s = Live(key);
            if (s == null || !s.active || s.phase != "running") return "no tournament round is running.";
            var ent = s.entrants.FirstOrDefault(e => e.ownerId == ownerId);
            if (ent == null) return "you are not in this tournament.";

            foreach (var m in s.matches)
            {
                if (m.round != s.currentRound || !string.IsNullOrEmpty(m.winnerId) || m.activated) continue;
                bool isA = m.aId == ent.entrantId;
                bool isB = m.bId == ent.entrantId;
                if (!isA && !isB) continue;
                if (string.IsNullOrEmpty(m.bId)) return "you have a bye this round.";

                if (isA) m.aReady = true; else m.bReady = true;
                Save();
                Broadcast();

                if (m.aReady && m.bReady)
                {
                    ActivateMatch(s, m);
                    Save();
                    Broadcast();
                    return "Both fighters are ready — the duel begins!";
                }

                var other = s.Find(isA ? m.bId : m.aId);
                return $"Ready. Waiting for {other?.ownerName ?? "your opponent"} to be ready too.";
            }
            return "you have no match awaiting a start this round.";
        }

        // Summons both sides of one pairing and announces it. Idempotent via
        // `activated` so a repeated Ready or a second admin Activate can't double
        // -summon a companion.
        private static bool ActivateMatch(TournamentState s, TournamentMatch m)
        {
            if (m == null || m.activated || !string.IsNullOrEmpty(m.winnerId) || string.IsNullOrEmpty(m.bId)) return false;
            var a = s.Find(m.aId);
            var b = s.Find(m.bId);
            if (a == null || b == null) return false;

            m.activated = true;
            LeaderboardSync.SummonForMatch(a.ownerId, a.entrantId, s.mode, b.entrantId, b.label, s.currentRound, PayloadsOf(a));
            LeaderboardSync.SummonForMatch(b.ownerId, b.entrantId, s.mode, a.entrantId, a.label, s.currentRound, PayloadsOf(b));

            Announce($"🥊 **Round {s.currentRound} — the duel is on:** {a.label} ({a.ownerName}) vs {b.label} ({b.ownerName}) " +
                     $"in the {Label(s)}.");
            return true;
        }

        // Updates an entrant's escrow after a match (winner reseals leveled-up state).
        public static void UpdateEscrow(string entrantId, List<string> payloads)
        {
            var s = LiveForEntrant(entrantId);
            var e = s?.Find(entrantId);
            if (e == null || payloads == null || payloads.Count == 0) return;
            if (s.mode == "party") e.teamPayloads = payloads;
            else e.totemPayload = payloads[0];
            Save();
            Plugin.Log.LogInfo($"[tourney] escrow updated for '{e.label}' ({payloads.Count} totem(s)).");
        }

        // The still-active entrant a resealing client should report against.
        public static TournamentEntrant FindEntrant(string entrantId) => LiveForEntrant(entrantId)?.Find(entrantId);

        // ---- Result intake (called from LeaderboardSync on the server) --------
        //
        // Neither reporter knows which slot the fight belongs to, so both search
        // every running tournament for a matching undecided pairing.

        public static void NotifyDuelResult(string winnerCompanionId, string loserCompanionId)
        {
            foreach (var s in _book.tournaments.ToList())
            {
                if (s == null || !s.active || s.phase != "running" || s.mode != "1v1") continue;
                var m = s.ActiveMatchBetween(winnerCompanionId, loserCompanionId);
                if (m == null) continue;
                ResolveMatch(s, m, winnerCompanionId);
                return;
            }
        }

        public static void NotifyPartyResult(long winnerOwnerId, long loserOwnerId)
        {
            foreach (var s in _book.tournaments.ToList())
            {
                if (s == null || !s.active || s.phase != "running" || s.mode != "party") continue;
                var m = s.ActiveMatchBetween(winnerOwnerId.ToString(), loserOwnerId.ToString());
                if (m == null) continue;
                ResolveMatch(s, m, winnerOwnerId.ToString());
                return;
            }
        }

        // ---- periodic upkeep (server, ~1 Hz from TournamentClient) ------------

        // How long after a bracket completes before the escrowed totems go home.
        // Long enough for the winning client's 1 Hz reseal to land (see Complete).
        private const double CompletionGraceSeconds = 6.0;

        // Two jobs: hand back the totems of a finished tournament once the reseals
        // have landed, and auto-cancel a wagered tournament that never filled,
        // refunding every stake. Without the latter a bracket that two people joined
        // and forgot would hold their companions and their coins forever.
        public static void Tick()
        {
            if (_book?.tournaments == null) return;

            foreach (var s in _book.tournaments.ToList())
            {
                if (s == null || !s.active || s.phase != "complete" || s.completedTicks <= 0) continue;
                var since = DateTime.UtcNow - new DateTime(s.completedTicks, DateTimeKind.Utc);
                if (since.TotalSeconds < CompletionGraceSeconds) continue;

                LeaderboardSync.ReturnEscrowToOwners(DrainAllEscrow(s.key));
                s.completedTicks = 0;

                // A finished wagered event frees its slot so the next one can be
                // opened; the free admin tournament stays on screen until an admin
                // clears it, which is the behaviour admins already expect.
                if (s.entryFee > 0) Drop(s.key);
                Save();
                Broadcast();
            }

            float minutes = Plugin.WageredRegistrationMinutes.Value;
            if (minutes <= 0f) return;

            foreach (var s in _book.tournaments.ToList())
            {
                if (s == null || !s.active || s.phase != "registration" || s.entryFee <= 0) continue;
                if (s.openedTicks <= 0) continue;
                var age = DateTime.UtcNow - new DateTime(s.openedTicks, DateTimeKind.Utc);
                if (age.TotalMinutes < minutes) continue;

                Plugin.Log.LogInfo($"[tourney] '{s.key}' expired with {s.entrants.Count}/{s.size} entrants — refunding.");
                Announce($"⌛ The {Label(s)} never filled ({s.entrants.Count}/{s.size}) and has been cancelled. " +
                         "Every stake is refunded and every companion totem returned.");
                LeaderboardSync.ReturnEscrowToOwners(DrainAllEscrow(s.key));
                RefundEveryone(s, "the tournament never filled");
                Drop(s.key);
                Save();
                Broadcast();
            }
        }

        // ---- refunds ----------------------------------------------------------

        private static void RefundEntrant(TournamentState s, TournamentEntrant e, string why)
        {
            if (s == null || e == null || e.feePaid <= 0) return;
            WagerService.Refund(Wager.Parse(s.currency), e.ownerId, e.ownerName, e.feePaid, why);
            e.feePaid = 0;
        }

        // Everyone still holding a stake in this tournament gets it back — including
        // a host who paid to open it but never got round to locking a totem.
        private static void RefundEveryone(TournamentState s, string why)
        {
            if (s == null) return;
            foreach (var e in s.entrants) RefundEntrant(s, e, why);
            if (s.hostCredit > 0)
            {
                WagerService.Refund(Wager.Parse(s.currency), s.hostId, s.hostName, s.hostCredit, why);
                s.hostCredit = 0;
            }
        }

        // ---- Bracket internals -----------------------------------------------

        // Dispatches to the type-specific resolver. Every type shares win-count
        // tracking (only round robin's champion pick actually uses it).
        private static void ResolveMatch(TournamentState s, TournamentMatch m, string winnerId)
        {
            m.winnerId = winnerId;
            var loserId = m.aId == winnerId ? m.bId : m.aId;
            var loser = s.Find(loserId);
            var winnerEnt = s.Find(winnerId);
            if (winnerEnt != null) winnerEnt.wins++;

            if (!string.IsNullOrEmpty(m.bId) && winnerEnt != null)
                Announce($"🏅 **{winnerEnt.label}** ({winnerEnt.ownerName}) defeats **{loser?.label ?? "their rival"}**" +
                         (loser != null ? $" ({loser.ownerName})" : "") +
                         $" in round {m.round} of the {Label(s)}.");

            switch (s.eliminationType)
            {
                case "round_robin": ResolveRoundRobinMatch(s, m, winnerEnt, loser); break;
                case "double": ResolveDoubleMatch(s, m, winnerEnt, loser); break;
                default: ResolveSingleMatch(s, m, winnerEnt, loser); break;
            }
        }

        private static void ResolveSingleMatch(TournamentState s, TournamentMatch m, TournamentEntrant winnerEnt, TournamentEntrant loser)
        {
            if (loser != null) { loser.eliminated = true; loser.losses++; }
            Plugin.Log.LogInfo($"[tourney] round {m.round}: '{winnerEnt?.label}' beat '{loser?.label}'.");
            Save();
            Broadcast();

            // Round complete? Advance or finish.
            if (s.matches.Where(x => x.round == s.currentRound).All(x => !string.IsNullOrEmpty(x.winnerId)))
            {
                var survivors = s.matches.Where(x => x.round == s.currentRound)
                    .Select(x => x.winnerId).Where(id => !string.IsNullOrEmpty(id)).ToList();

                if (survivors.Count <= 1)
                {
                    Complete(s, survivors.Count == 1 ? survivors[0] : winnerEnt?.entrantId);
                    return;
                }

                s.currentRound++;
                BuildRound(s, survivors, s.currentRound);
                Save();
                Broadcast();
                AnnounceRoundMatches(s, s.currentRound);
            }
            else
            {
                AnnounceStillToFight(s);
            }
        }

        // Round robin: nobody is eliminated. Every entrant plays every scheduled
        // round (pre-built at Begin by BuildRoundRobinSchedule); once the last
        // round is decided, the champion is whoever has the most wins (ties broken
        // by seed rating, then name for determinism).
        private static void ResolveRoundRobinMatch(TournamentState s, TournamentMatch m, TournamentEntrant winnerEnt, TournamentEntrant loser)
        {
            Plugin.Log.LogInfo($"[tourney] round {m.round} (round robin): '{winnerEnt?.label}' beat '{loser?.label}'.");
            Save();
            Broadcast();

            if (!s.matches.Where(x => x.round == s.currentRound).All(x => !string.IsNullOrEmpty(x.winnerId)))
            { AnnounceStillToFight(s); return; }

            if (s.currentRound >= s.totalRounds)
            {
                var champ = s.entrants
                    .OrderByDescending(e => e.wins).ThenByDescending(e => e.seedRating).ThenBy(e => e.label)
                    .FirstOrDefault();
                if (champ != null) Complete(s, champ.entrantId);
                return;
            }

            s.currentRound++;
            Save();
            Broadcast();
            AnnounceRoundMatches(s, s.currentRound);
        }

        // Double elimination: an entrant is only eliminated on their 2nd loss.
        // Simplification (deliberate, not a bug): the grand final is a single
        // decisive match — if the once-beaten losers-bracket finalist wins it, they
        // are declared champion outright instead of forcing a "bracket reset".
        private static void ResolveDoubleMatch(TournamentState s, TournamentMatch m, TournamentEntrant winnerEnt, TournamentEntrant loser)
        {
            if (loser != null)
            {
                loser.losses++;
                if (loser.losses >= 2) loser.eliminated = true;
            }
            Plugin.Log.LogInfo($"[tourney] round {m.round} ({m.bracket}): '{winnerEnt?.label}' beat '{loser?.label}' (losses={loser?.losses ?? 0}).");
            Save();
            Broadcast();

            if (!s.matches.Where(x => x.round == s.currentRound).All(x => !string.IsNullOrEmpty(x.winnerId)))
            { AnnounceStillToFight(s); return; }

            if (m.bracket == "GF")
            {
                Complete(s, winnerEnt?.entrantId);
                return;
            }

            s.currentRound++;
            BuildDoubleRound(s, s.currentRound);
            Save();
            Broadcast();
            AnnounceRoundMatches(s, s.currentRound);
        }

        private static void Complete(TournamentState s, string championId)
        {
            s.phase = "complete";
            s.championId = championId;
            var champ = s.Find(championId);
            s.championLabel = champ?.label ?? championId;
            Save();
            Broadcast();

            Plugin.Log.LogInfo($"[tourney] '{s.key}' complete — champion: {s.championLabel}.");
            ArchiveChampion(s, champ);

            if (champ != null)
            {
                LeaderboardSync.SendTournamentWon(champ.ownerName, s.mode, s.bracketSize, champ.caste);
                Announce($"🏆 **{champ.ownerName}** is the champion of the {Label(s)} with **{champ.label}**" +
                         (champ.level > 0 ? $" (Lv{champ.level})" : "") + "!");
                PayChampion(s, champ);
            }

            AnnounceSummary(s, champ);

            // Escrowed totems are handed back by Tick() after a short grace, NOT
            // here: the winner's client still has to reseal its live companion with
            // the XP it just earned and send that payload back (a 1 Hz pass). Draining
            // now would return the champion a totem holding its pre-final state.
            s.completedTicks = DateTime.UtcNow.Ticks;
            Save();
            Broadcast();
        }

        // The champion's purse. Coins are granted by this mod directly. Valcoins are
        // NOT: the amount is minted by the donations mod from its own quest table,
        // triggered on the champion's client — this mod has never priced a Valcoin
        // reward and does not start here (docs/Wagers.md).
        private static void PayChampion(TournamentState s, TournamentEntrant champ)
        {
            if (s.prize <= 0 || champ == null) return;
            var currency = Wager.Parse(s.currency);
            if (currency == WagerCurrency.Coins)
            {
                WagerService.Pay(WagerCurrency.Coins, champ.ownerId, champ.ownerName, s.prize,
                    ValcoinBridge.SkuTournamentEntry, "tournament champion");
            }
            else if (currency == WagerCurrency.Valcoin)
            {
                LeaderboardSync.SendTournamentPrize(champ.ownerName, s.currency, s.prize);
            }
        }

        // Re-seeded single elimination: sort survivors by rating desc; if odd, the
        // top seed byes; pair the rest highest-vs-lowest.
        private static void BuildRound(TournamentState s, List<string> survivorIds, int round)
        {
            var ents = survivorIds.Select(id => s.Find(id)).Where(e => e != null)
                .OrderByDescending(e => e.seedRating).ThenBy(e => e.label).ToList();
            PairPool(s, ents, round, "W");
        }

        // Shared pairing helper (single elim's re-seeded rounds AND each double-elim
        // bracket round): sort by rating desc, top seed byes on an odd count, pair
        // the rest highest-vs-lowest.
        private static void PairPool(TournamentState s, List<TournamentEntrant> ents, int round, string bracket)
        {
            int i = 0;
            if (ents.Count % 2 == 1)
            {
                // Top seed gets a bye (an immediately-decided match).
                var bye = ents[0];
                s.matches.Add(new TournamentMatch
                {
                    round = round, bracket = bracket, aId = bye.entrantId, aLabel = bye.label, aLevel = bye.level,
                    bId = "", bLabel = "(bye)", winnerId = bye.entrantId,
                });
                i = 1;
            }

            int lo = i, hi = ents.Count - 1;
            while (lo < hi)
            {
                var a = ents[lo]; var b = ents[hi];
                s.matches.Add(new TournamentMatch
                {
                    round = round, bracket = bracket, aId = a.entrantId, aLabel = a.label, aLevel = a.level,
                    bId = b.entrantId, bLabel = b.label, bLevel = b.level, winnerId = "",
                });
                lo++; hi--;
            }
        }

        // Round robin: standard circle method. Builds every round of the whole
        // schedule up front (a bye slot is added for an odd entrant count, so total
        // rounds = n or n-1 for even/odd n after padding to even).
        private static void BuildRoundRobinSchedule(TournamentState s, List<string> ids)
        {
            var arr = new List<string>(ids);
            if (arr.Count % 2 == 1) arr.Add(""); // bye slot — whoever draws it sits out that round
            int n = arr.Count;
            int rounds = Mathf.Max(1, n - 1);
            s.totalRounds = rounds;

            for (int r = 1; r <= rounds; r++)
            {
                for (int i = 0; i < n / 2; i++)
                {
                    var aId = arr[i];
                    var bId = arr[n - 1 - i];
                    if (string.IsNullOrEmpty(aId) || string.IsNullOrEmpty(bId)) continue; // bye — nobody plays
                    var a = s.Find(aId);
                    var b = s.Find(bId);
                    if (a == null || b == null) continue;
                    s.matches.Add(new TournamentMatch
                    {
                        round = r, bracket = "W", aId = aId, aLabel = a.label, aLevel = a.level,
                        bId = bId, bLabel = b.label, bLevel = b.level, winnerId = "",
                    });
                }
                // Rotate: keep index 0 fixed, everyone else shifts one seat.
                var last = arr[n - 1];
                for (int i = n - 1; i > 1; i--) arr[i] = arr[i - 1];
                arr[1] = last;
            }
        }

        // Double elimination: pools are recomputed each round from entrant loss
        // counts rather than tracked as separate persisted queues — simpler and
        // self-correcting. wbPool = 0 losses, lbPool = 1 loss (2 losses =
        // eliminated, filtered out entirely). For non-power-of-two entrant counts
        // the pairing/bye handling is a pragmatic approximation of a "real" seeded
        // DE bracket, not tournament-grade — acceptable for this mod's small brackets.
        private static void BuildDoubleRound(TournamentState s, int round)
        {
            var wbPool = s.entrants.Where(e => e.losses == 0 && !e.eliminated)
                .OrderByDescending(e => e.seedRating).ThenBy(e => e.label).ToList();
            var lbPool = s.entrants.Where(e => e.losses == 1 && !e.eliminated)
                .OrderByDescending(e => e.seedRating).ThenBy(e => e.label).ToList();

            if (wbPool.Count == 1 && lbPool.Count == 1)
            {
                var a = wbPool[0]; var b = lbPool[0];
                s.matches.Add(new TournamentMatch
                {
                    round = round, bracket = "GF", aId = a.entrantId, aLabel = a.label, aLevel = a.level,
                    bId = b.entrantId, bLabel = b.label, bLevel = b.level, winnerId = "",
                });
                return;
            }

            if (wbPool.Count + lbPool.Count <= 1)
            {
                // Nobody left to play (guards against a stall) — whoever remains wins.
                var sole = wbPool.Concat(lbPool).FirstOrDefault();
                if (sole != null) Complete(s, sole.entrantId);
                return;
            }

            if (wbPool.Count >= 2) PairPool(s, wbPool, round, "W");
            if (lbPool.Count >= 2) PairPool(s, lbPool, round, "L");
        }

        // ---- announcements ----------------------------------------------------

        private static void AnnounceRoundMatches(TournamentState s, int round)
        {
            var lines = new List<string>();
            foreach (var m in s.matches)
            {
                if (m.round != round) continue;
                var a = s.Find(m.aId);
                if (string.IsNullOrEmpty(m.bId))
                {
                    if (a != null) lines.Add($"• {a.label} ({a.ownerName}) — bye");
                    continue;
                }
                var b = s.Find(m.bId);
                if (a != null) LeaderboardSync.SendTournamentMatch(a.ownerName, round, m.bLabel, a.caste);
                if (b != null) LeaderboardSync.SendTournamentMatch(b.ownerName, round, m.aLabel, b.caste);
                if (a != null && b != null)
                    lines.Add($"• {a.label} ({a.ownerName}) vs {b.label} ({b.ownerName})");
            }
            if (lines.Count == 0) return;
            Announce($"📋 **{Label(s)} — round {round} draw:**\n" + string.Join("\n", lines));
        }

        // After a result, name who is still due to fight this round — the "who's
        // next" beat. Silent when the round is already fully decided (the next
        // round's draw announcement covers that case instead).
        private static void AnnounceStillToFight(TournamentState s)
        {
            var pending = s.matches
                .Where(m => m.round == s.currentRound && string.IsNullOrEmpty(m.winnerId) && !string.IsNullOrEmpty(m.bId))
                .ToList();
            if (pending.Count == 0) return;

            var lines = pending.Select(m =>
            {
                var a = s.Find(m.aId); var b = s.Find(m.bId);
                return $"• {a?.label ?? m.aLabel} ({a?.ownerName}) vs {b?.label ?? m.bLabel} ({b?.ownerName})";
            });
            Announce($"➡️ **Still to fight in round {s.currentRound}** of the {Label(s)}:\n" + string.Join("\n", lines));
        }

        // The end-of-event log: final standings and every result, in one post.
        private static void AnnounceSummary(TournamentState s, TournamentEntrant champ)
        {
            var sb = new System.Text.StringBuilder();
            sb.Append($"📜 **{Label(s)} — final summary**\n");
            sb.Append($"Format: {s.mode} · {DisplayType(s.eliminationType)} · {s.bracketSize} entrants");
            if (s.entryFee > 0)
                sb.Append($" · entry {s.entryFee} {Wager.Display(Wager.Parse(s.currency))} · purse {s.prize} {Wager.Display(Wager.Parse(s.currency))}");
            sb.Append('\n');

            if (champ != null) sb.Append($"Champion: **{champ.label}** ({champ.ownerName})\n");

            sb.Append("\n__Standings__\n");
            foreach (var e in s.entrants.OrderByDescending(e => e.wins).ThenBy(e => e.losses).ThenBy(e => e.label))
                sb.Append($"• {e.label} ({e.ownerName}) — {e.wins}W {e.losses}L{(e.eliminated ? " · out" : "")}\n");

            sb.Append("\n__Results__\n");
            foreach (var m in s.matches.OrderBy(m => m.round))
            {
                if (string.IsNullOrEmpty(m.bId)) { sb.Append($"• R{m.round}: {m.aLabel} — bye\n"); continue; }
                var w = s.Find(m.winnerId);
                sb.Append($"• R{m.round}: {m.aLabel} vs {m.bLabel}" +
                          (w != null ? $" → **{w.label}**" : " → undecided") + "\n");
            }
            Announce(sb.ToString());
        }

        // A human label for a tournament, used in every announcement so the two
        // wagered slots are never confused with each other or with the free one.
        private static string Label(TournamentState s)
        {
            if (s == null) return "tournament";
            var currency = Wager.Parse(s.currency);
            if (currency == WagerCurrency.None) return "server tournament";
            return $"{Wager.Display(currency)} tournament";
        }

        private static string DisplayType(string t)
        {
            switch (t)
            {
                case "double": return "double elimination";
                case "round_robin": return "round robin";
                default: return "single elimination";
            }
        }

        private static void Announce(string message) => ServerGuideBridge.AnnounceDiscord(message);

        // ---- champions archive ------------------------------------------------

        private static void ArchiveChampion(TournamentState s, TournamentEntrant champ)
        {
            if (champ == null) return;
            try
            {
                var path = ChampionsPath();
                ChampionsData data = File.Exists(path)
                    ? CompetitiveJson.ReadChampions(File.ReadAllText(path)) : new ChampionsData();
                if (data.champions == null) data.champions = new List<ChampionRecord>();
                data.champions.Add(new ChampionRecord
                {
                    mode = s.mode, championLabel = champ.label, ownerName = champ.ownerName,
                    bracketSize = s.bracketSize, seasonId = s.seasonId, dateTicks = DateTime.UtcNow.Ticks,
                });
                File.WriteAllText(path, CompetitiveJson.Write(data, true));
            }
            catch (Exception e) { Plugin.Log.LogWarning($"[tourney] champion archive failed: {e.Message}"); }
        }

        public static List<ChampionRecord> LoadChampions()
        {
            try
            {
                var path = ChampionsPath();
                if (!File.Exists(path)) return new List<ChampionRecord>();
                var data = CompetitiveJson.ReadChampions(File.ReadAllText(path));
                return data?.champions ?? new List<ChampionRecord>();
            }
            catch { return new List<ChampionRecord>(); }
        }

        private static void Broadcast() => LeaderboardSync.BroadcastTournament();
    }
}
