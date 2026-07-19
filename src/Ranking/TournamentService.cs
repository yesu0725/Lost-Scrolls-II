using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

namespace LostScrollsII.Ranking
{
    // Server-authoritative tournament bracket runner (docs/Tournaments.md). Works
    // for both 1v1 and party formats by reusing the existing duel modes: the server
    // owns the bracket, announces pairings, and resolves each match from the SAME
    // duel/party result reports the ladders already receive — it never remote-drives
    // combat. Re-seeded single elimination (survivors re-sorted by rating each round,
    // highest vs lowest, top seed byes on an odd count).
    //
    // Authority: the server/host mutates _state; clients hold a read-only snapshot
    // pushed via LeaderboardSync (mirrors the ladder).
    public static class TournamentService
    {
        private static TournamentState _state = new TournamentState();      // server authoritative
        private static TournamentState _snapshot = new TournamentState();   // client view (host aliases _state)

        public static TournamentState Snapshot => _snapshot;
        public static bool Active => _snapshot != null && _snapshot.active;

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
                    _state = JsonUtility.FromJson<TournamentState>(File.ReadAllText(path)) ?? new TournamentState();
                    if (_state.entrants == null) _state.entrants = new List<TournamentEntrant>();
                    if (_state.matches == null) _state.matches = new List<TournamentMatch>();
                    Plugin.Log.LogInfo($"[tourney] resumed state: phase={_state.phase}, entrants={_state.entrants.Count}.");
                }
                else _state = new TournamentState();
            }
            catch (Exception e) { _state = new TournamentState(); Plugin.Log.LogWarning($"[tourney] load failed: {e.Message}"); }
            _snapshot = _state;
        }

        private static void Save()
        {
            try { File.WriteAllText(StatePath(), JsonUtility.ToJson(_state, true)); }
            catch (Exception e) { Plugin.Log.LogWarning($"[tourney] save failed: {e.Message}"); }
        }

        public static string SerializeSnapshot() => JsonUtility.ToJson(_state ?? new TournamentState());

        public static void ApplySnapshot(string json)
        {
            try
            {
                var s = JsonUtility.FromJson<TournamentState>(json) ?? new TournamentState();
                if (s.entrants == null) s.entrants = new List<TournamentEntrant>();
                if (s.matches == null) s.matches = new List<TournamentMatch>();
                _snapshot = s;
            }
            catch (Exception e) { Plugin.Log.LogWarning($"[tourney] snapshot apply failed: {e.Message}"); }
        }

        // ---- Admin control (SERVER ONLY) -------------------------------------

        public static string Start(string mode, int size)
        {
            mode = (mode ?? "1v1").ToLowerInvariant();
            if (mode != "1v1" && mode != "party") return "mode must be 1v1 or party.";
            if (_state.active) return "a tournament is already running — cancel it first.";

            _state = new TournamentState
            {
                active = true,
                mode = mode,
                phase = "registration",
                size = Mathf.Max(0, size),
                seasonId = 1,
            };
            _snapshot = _state;
            Save();
            Broadcast();
            return $"Tournament ({mode}) open for registration" + (size > 0 ? $" — {size} slots." : ".");
        }

        // Player registration. entrantId/label/caste/rating are resolved by the
        // caller (client). The companion(s) are escrowed as their Communion Totem
        // payload(s): `totemPayload` for a 1v1 entry, `teamPayloads` for a party.
        // Returns a status string for the joining client.
        public static string Join(string entrantId, long ownerId, string ownerName, string label, int caste,
            int seedRating, string totemPayload = null, List<string> teamPayloads = null)
        {
            if (!_state.active || _state.phase != "registration") return "no tournament is accepting entries.";
            if (string.IsNullOrEmpty(entrantId)) return "could not identify your entry.";
            if (_state.Find(entrantId) != null) return "already registered.";
            if (_state.size > 0 && _state.entrants.Count >= _state.size) return "the tournament is full.";

            // One entry per owner (you field one companion / one party).
            if (_state.entrants.Any(e => e.ownerId == ownerId)) return "you already have an entry in this tournament.";

            _state.entrants.Add(new TournamentEntrant
            {
                entrantId = entrantId, ownerId = ownerId, ownerName = ownerName,
                label = label, caste = caste, seedRating = seedRating,
                totemPayload = totemPayload ?? string.Empty,
                teamPayloads = teamPayloads ?? new List<string>(),
            });
            Save();
            Broadcast();
            return $"Registered '{label}' ({_state.entrants.Count}" + (_state.size > 0 ? $"/{_state.size}" : "") + ").";
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
        // escrowed payloads to hand back (empty if nothing to withdraw).
        public static List<string> Withdraw(long ownerId, out string status)
        {
            status = "no tournament is accepting entries.";
            if (!_state.active || _state.phase != "registration") return new List<string>();
            var ent = _state.entrants.FirstOrDefault(e => e.ownerId == ownerId);
            if (ent == null) { status = "you have no entry to withdraw."; return new List<string>(); }
            var payloads = PayloadsOf(ent);
            _state.entrants.Remove(ent);
            Save();
            Broadcast();
            status = $"Withdrew '{ent.label}'. Your companion totem is returned.";
            return payloads;
        }

        // Admin releases a specific entrant (by id or owner name), returning its
        // escrowed totem(s). Allowed in registration (removes the entry) or while
        // running (forfeits + frees the totem). Returns the payloads + the owner id.
        public static List<string> ReleaseEntrant(string idOrName, out long ownerId, out string status)
        {
            ownerId = 0L; status = "no tournament is running.";
            if (!_state.active) return new List<string>();
            var ent = _state.entrants.FirstOrDefault(e =>
                e.entrantId == idOrName || string.Equals(e.ownerName, idOrName, StringComparison.OrdinalIgnoreCase)
                || string.Equals(e.label, idOrName, StringComparison.OrdinalIgnoreCase));
            if (ent == null) { status = $"no entrant '{idOrName}'."; return new List<string>(); }

            ownerId = ent.ownerId;
            var payloads = PayloadsOf(ent);

            if (_state.phase == "registration")
            {
                _state.entrants.Remove(ent);
            }
            else
            {
                // Running: forfeit any undecided current-round match, then free it.
                ent.eliminated = true;
                foreach (var m in _state.matches)
                {
                    if (m.round != _state.currentRound || !string.IsNullOrEmpty(m.winnerId)) continue;
                    if (m.aId == ent.entrantId || m.bId == ent.entrantId)
                    {
                        var winnerId = m.aId == ent.entrantId ? m.bId : m.aId;
                        if (!string.IsNullOrEmpty(winnerId)) ResolveMatch(m, winnerId);
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

        // Every still-escrowed payload keyed by owner id — used to return all totems
        // when the tournament ends or is cancelled.
        public static Dictionary<long, List<string>> DrainAllEscrow()
        {
            var byOwner = new Dictionary<long, List<string>>();
            foreach (var e in _state.entrants)
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

        public static string Begin()
        {
            if (!_state.active || _state.phase != "registration") return "no tournament is in registration.";
            if (_state.entrants.Count < 2) return "need at least 2 entrants.";

            _state.phase = "running";
            _state.bracketSize = _state.entrants.Count;
            _state.currentRound = 1;
            _state.matches.Clear();

            var ids = _state.entrants.Select(e => e.entrantId).ToList();
            BuildRound(ids, 1);
            Save();
            Broadcast();
            AnnounceRoundMatches(1);
            return $"Tournament begun — {_state.entrants.Count} entrants, round 1.";
        }

        public static string Cancel()
        {
            if (!_state.active) return "no tournament is running.";
            // Hand every still-escrowed companion totem back to its owner first.
            LeaderboardSync.ReturnEscrowToOwners(DrainAllEscrow());
            _state = new TournamentState();
            _snapshot = _state;
            Save();
            Broadcast();
            return "Tournament cancelled — companion totems returned.";
        }

        // Admin forfeit: whoever `playerName` is in an undecided current-round match
        // loses; the opponent advances.
        public static string Forfeit(string playerName)
        {
            if (!_state.active || _state.phase != "running") return "no tournament round is running.";
            var ent = _state.entrants.FirstOrDefault(e =>
                string.Equals(e.ownerName, playerName, StringComparison.OrdinalIgnoreCase));
            if (ent == null) return $"'{playerName}' is not an entrant.";

            foreach (var m in _state.matches)
            {
                if (m.round != _state.currentRound || !string.IsNullOrEmpty(m.winnerId)) continue;
                if (m.aId == ent.entrantId || m.bId == ent.entrantId)
                {
                    var winnerId = m.aId == ent.entrantId ? m.bId : m.aId;
                    if (string.IsNullOrEmpty(winnerId)) return "opponent not set — cannot forfeit a bye.";
                    ResolveMatch(m, winnerId);
                    return $"{playerName} forfeits — opponent advances.";
                }
            }
            return $"{playerName} has no undecided match this round.";
        }

        // Admin "activate duels": for every undecided pairing in the current round,
        // tell both owners' clients to summon their escrowed companion(s) and duel
        // the assigned opponent (docs/Tournaments.md — escrow & auto-summon). Byes
        // are skipped (already decided).
        public static string ActivateCurrentRound()
        {
            if (!_state.active || _state.phase != "running") return "no tournament round is running.";
            int summoned = 0;
            foreach (var m in _state.matches)
            {
                if (m.round != _state.currentRound || !string.IsNullOrEmpty(m.winnerId)) continue;
                if (string.IsNullOrEmpty(m.bId)) continue; // bye
                var a = _state.Find(m.aId);
                var b = _state.Find(m.bId);
                if (a == null || b == null) continue;
                LeaderboardSync.SummonForMatch(a.ownerId, a.entrantId, _state.mode, b.entrantId, b.label, _state.currentRound, PayloadsOf(a));
                LeaderboardSync.SummonForMatch(b.ownerId, b.entrantId, _state.mode, a.entrantId, a.label, _state.currentRound, PayloadsOf(b));
                summoned++;
            }
            return summoned == 0
                ? "No pairings to activate this round."
                : $"Activated {summoned} match(es) — companions summoned to duel.";
        }

        // Updates an entrant's escrow after a match (winner reseals leveled-up state).
        public static void UpdateEscrow(string entrantId, List<string> payloads)
        {
            var e = _state?.Find(entrantId);
            if (e == null || payloads == null || payloads.Count == 0) return;
            if (_state.mode == "party") e.teamPayloads = payloads;
            else e.totemPayload = payloads[0];
            Save();
            Plugin.Log.LogInfo($"[tourney] escrow updated for '{e.label}' ({payloads.Count} totem(s)).");
        }

        // The still-active entrant id a resealing client should report against, given
        // a companion id it holds (1v1: the id itself; party: the owner entrant).
        public static TournamentEntrant FindEntrant(string entrantId) => _state?.Find(entrantId);

        // ---- Result intake (called from LeaderboardSync on the server) --------

        public static void NotifyDuelResult(string winnerCompanionId, string loserCompanionId)
        {
            if (!_state.active || _state.phase != "running" || _state.mode != "1v1") return;
            var m = _state.ActiveMatchBetween(winnerCompanionId, loserCompanionId);
            if (m == null) return;
            ResolveMatch(m, winnerCompanionId);
        }

        public static void NotifyPartyResult(long winnerOwnerId, long loserOwnerId)
        {
            if (!_state.active || _state.phase != "running" || _state.mode != "party") return;
            var m = _state.ActiveMatchBetween(winnerOwnerId.ToString(), loserOwnerId.ToString());
            if (m == null) return;
            ResolveMatch(m, winnerOwnerId.ToString());
        }

        // ---- Bracket internals -----------------------------------------------

        private static void ResolveMatch(TournamentMatch m, string winnerId)
        {
            m.winnerId = winnerId;
            var loserId = m.aId == winnerId ? m.bId : m.aId;
            var loser = _state.Find(loserId);
            if (loser != null) loser.eliminated = true;

            var winnerEnt = _state.Find(winnerId);
            Plugin.Log.LogInfo($"[tourney] round {m.round}: '{winnerEnt?.label}' beat '{loser?.label}'.");
            Save();
            Broadcast();

            // Round complete? Advance or finish.
            if (_state.matches.Where(x => x.round == _state.currentRound).All(x => !string.IsNullOrEmpty(x.winnerId)))
            {
                var survivors = _state.matches.Where(x => x.round == _state.currentRound)
                    .Select(x => x.winnerId).Where(id => !string.IsNullOrEmpty(id)).ToList();

                if (survivors.Count <= 1)
                {
                    Complete(survivors.Count == 1 ? survivors[0] : winnerId);
                    return;
                }

                _state.currentRound++;
                BuildRound(survivors, _state.currentRound);
                Save();
                Broadcast();
                AnnounceRoundMatches(_state.currentRound);
            }
        }

        private static void Complete(string championId)
        {
            _state.phase = "complete";
            _state.championId = championId;
            var champ = _state.Find(championId);
            _state.championLabel = champ?.label ?? championId;
            Save();
            Broadcast();

            Plugin.Log.LogInfo($"[tourney] complete — champion: {_state.championLabel}.");
            ArchiveChampion(champ);

            if (champ != null)
                LeaderboardSync.SendTournamentWon(champ.ownerName, _state.mode, _state.bracketSize, champ.caste);

            // Return every companion still held in escrow (champion + anyone whose
            // totem wasn't already released) to its owner now the bracket is done.
            LeaderboardSync.ReturnEscrowToOwners(DrainAllEscrow());
        }

        // Re-seeded single elimination: sort survivors by rating desc; if odd, the
        // top seed byes; pair the rest highest-vs-lowest.
        private static void BuildRound(List<string> survivorIds, int round)
        {
            var ents = survivorIds.Select(id => _state.Find(id)).Where(e => e != null)
                .OrderByDescending(e => e.seedRating).ThenBy(e => e.label).ToList();

            int i = 0;
            if (ents.Count % 2 == 1)
            {
                // Top seed gets a bye (an immediately-decided match).
                var bye = ents[0];
                _state.matches.Add(new TournamentMatch
                {
                    round = round, aId = bye.entrantId, aLabel = bye.label,
                    bId = "", bLabel = "(bye)", winnerId = bye.entrantId,
                });
                i = 1;
            }

            int lo = i, hi = ents.Count - 1;
            while (lo < hi)
            {
                var a = ents[lo]; var b = ents[hi];
                _state.matches.Add(new TournamentMatch
                {
                    round = round, aId = a.entrantId, aLabel = a.label,
                    bId = b.entrantId, bLabel = b.label, winnerId = "",
                });
                lo++; hi--;
            }
        }

        private static void AnnounceRoundMatches(int round)
        {
            foreach (var m in _state.matches)
            {
                if (m.round != round || string.IsNullOrEmpty(m.bId)) continue; // skip byes
                var a = _state.Find(m.aId);
                var b = _state.Find(m.bId);
                if (a != null) LeaderboardSync.SendTournamentMatch(a.ownerName, round, m.bLabel, a.caste);
                if (b != null) LeaderboardSync.SendTournamentMatch(b.ownerName, round, m.aLabel, b.caste);
            }
        }

        private static void ArchiveChampion(TournamentEntrant champ)
        {
            if (champ == null) return;
            try
            {
                var path = ChampionsPath();
                ChampionsData data = File.Exists(path)
                    ? JsonUtility.FromJson<ChampionsData>(File.ReadAllText(path)) : new ChampionsData();
                if (data.champions == null) data.champions = new List<ChampionRecord>();
                data.champions.Add(new ChampionRecord
                {
                    mode = _state.mode, championLabel = champ.label, ownerName = champ.ownerName,
                    bracketSize = _state.bracketSize, seasonId = _state.seasonId, dateTicks = DateTime.UtcNow.Ticks,
                });
                File.WriteAllText(path, JsonUtility.ToJson(data, true));
            }
            catch (Exception e) { Plugin.Log.LogWarning($"[tourney] champion archive failed: {e.Message}"); }
        }

        public static List<ChampionRecord> LoadChampions()
        {
            try
            {
                var path = ChampionsPath();
                if (!File.Exists(path)) return new List<ChampionRecord>();
                var data = JsonUtility.FromJson<ChampionsData>(File.ReadAllText(path));
                return data?.champions ?? new List<ChampionRecord>();
            }
            catch { return new List<ChampionRecord>(); }
        }

        private static void Broadcast() => LeaderboardSync.BroadcastTournament();
    }
}
