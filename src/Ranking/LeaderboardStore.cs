using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

namespace LostScrollsII.Ranking
{
    // The persistent, server-authoritative duel ladder (docs/Ranking.md).
    //
    // Authority model (mirrors ServerGuide's config sync): the SERVER/host owns
    // the data and the JSON file; pure clients hold a read-only snapshot pushed
    // over LeaderboardSync. All mutation (ApplyDuel, season reset) is server-only.
    //
    // Storage: one JSON file per world under the save-data folder, named by world
    // so multiple worlds on one machine don't collide. UnityEngine.JsonUtility is
    // used so there's no external serializer dependency.
    public static class LeaderboardStore
    {
        // Server/host authoritative table. Null until loaded on ZNet.Awake.
        private static LeaderboardData _data;

        // Client read-only snapshot (what de_ladder / the name-tag rank read on a
        // pure client). On the host this is kept pointed at _data so both paths see
        // the same rows.
        private static LeaderboardData _snapshot = new LeaderboardData();

        // Transient per-pair cooldown (anti-farm): sorted "a|b" -> Time.time when
        // the pair last moved rating. Not persisted — a short window only.
        private static readonly Dictionary<string, float> _pairCooldown = new Dictionary<string, float>();

        private static string _loadedWorld;

        public static LeaderboardData Snapshot => _snapshot;

        // ---- Path -------------------------------------------------------------

        // Public so the tournament store (TournamentService) writes alongside the
        // ladder in the same per-world data folder.
        public static string DataDir()
        {
            string baseDir;
            try { baseDir = Path.Combine(Utils.GetSaveDataPath(FileHelpers.FileSource.Local), "LostScrollsII"); }
            catch { baseDir = Path.Combine(BepInEx.Paths.ConfigPath, "LostScrollsII"); }
            Directory.CreateDirectory(baseDir);
            return baseDir;
        }

        private static string Sanitize(string s)
        {
            if (string.IsNullOrEmpty(s)) return "world";
            foreach (var c in Path.GetInvalidFileNameChars()) s = s.Replace(c, '_');
            return s;
        }

        private static string CurrentWorldName()
        {
            try { if (ZNet.instance != null) return ZNet.instance.GetWorldName(); }
            catch { }
            return "world";
        }

        // Sanitized world tag used in per-world filenames (shared with TournamentService).
        public static string WorldTag() => Sanitize(CurrentWorldName());

        private static string StorePath(string world) => Path.Combine(DataDir(), $"ladder.{Sanitize(world)}.json");

        // ---- Load / save (server) --------------------------------------------

        public static void LoadForCurrentWorld()
        {
            var world = CurrentWorldName();
            _loadedWorld = world;
            var path = StorePath(world);
            try
            {
                if (File.Exists(path))
                {
                    _data = JsonUtility.FromJson<LeaderboardData>(File.ReadAllText(path)) ?? new LeaderboardData();
                    if (_data.companions == null) _data.companions = new List<CompanionRecord>();
                    if (_data.parties == null) _data.parties = new List<PartyRecord>();
                    Plugin.Log.LogInfo($"[ladder] loaded {_data.companions.Count} duel + {_data.parties.Count} party record(s) for world '{world}'.");
                }
                else
                {
                    _data = new LeaderboardData();
                    Plugin.Log.LogInfo($"[ladder] no ladder file for world '{world}' — starting fresh.");
                }
            }
            catch (Exception e)
            {
                _data = new LeaderboardData();
                Plugin.Log.LogWarning($"[ladder] failed to load '{path}': {e.Message}. Starting fresh.");
            }
            _snapshot = _data; // host reads the live table directly
        }

        public static void Save()
        {
            if (_data == null) return;
            try
            {
                File.WriteAllText(StorePath(_loadedWorld ?? CurrentWorldName()), JsonUtility.ToJson(_data, true));
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning($"[ladder] save failed: {e.Message}");
            }
        }

        // ---- Snapshot sync (client) ------------------------------------------

        public static string SerializeSnapshot() => JsonUtility.ToJson(_data ?? new LeaderboardData());

        public static void ApplySnapshot(string json)
        {
            try
            {
                var d = JsonUtility.FromJson<LeaderboardData>(json) ?? new LeaderboardData();
                if (d.companions == null) d.companions = new List<CompanionRecord>();
                if (d.parties == null) d.parties = new List<PartyRecord>();
                _snapshot = d;
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning($"[ladder] failed to apply snapshot: {e.Message}");
            }
        }

        // ---- Queries (client or host, off the snapshot) ----------------------

        // Records sorted best-first (rating desc, then wins, then name).
        public static List<CompanionRecord> Ranked(int caste = -1)
        {
            var src = _snapshot?.companions ?? new List<CompanionRecord>();
            return src.Where(r => r != null && (caste < 0 || r.caste == caste))
                      .OrderByDescending(r => r.rating)
                      .ThenByDescending(r => r.wins)
                      .ThenBy(r => r.companionName ?? "")
                      .ToList();
        }

        // 1-based ladder rank of a companion, or 0 if it has no record yet.
        public static int RankOf(string companionId)
        {
            if (string.IsNullOrEmpty(companionId)) return 0;
            var ranked = Ranked();
            for (int i = 0; i < ranked.Count; i++)
                if (ranked[i].companionId == companionId) return i + 1;
            return 0;
        }

        public static CompanionRecord Find(string companionId)
            => (_snapshot?.companions ?? new List<CompanionRecord>())
                .FirstOrDefault(r => r != null && r.companionId == companionId);

        public static PartyRecord FindParty(long ownerId)
            => (_snapshot?.parties ?? new List<PartyRecord>())
                .FirstOrDefault(r => r != null && r.ownerId == ownerId);

        // Party records sorted best-first.
        public static List<PartyRecord> RankedParties()
        {
            var src = _snapshot?.parties ?? new List<PartyRecord>();
            return src.Where(r => r != null)
                      .OrderByDescending(r => r.rating)
                      .ThenByDescending(r => r.wins)
                      .ThenBy(r => r.ownerName ?? "")
                      .ToList();
        }

        // ---- Mutation (SERVER ONLY) ------------------------------------------

        // Applies a decided 1v1 duel to the authoritative table. Returns true if a
        // record actually changed (false = ignored, e.g. pair-cooldown). Out params
        // report the winner's rank movement so the caller can fire dvergr_rank_changed.
        public static bool ApplyDuel(DuelResult r, int kFactor, float pairCooldownSeconds,
            out int winnerRank, out int winnerRating, out bool crossedThreshold)
        {
            winnerRank = 0; winnerRating = Rating.StartRating; crossedThreshold = false;
            if (_data == null) LoadForCurrentWorld();
            if (r == null || string.IsNullOrEmpty(r.WinnerId) || string.IsNullOrEmpty(r.LoserId)) return false;
            if (r.WinnerId == r.LoserId) return false;
            if (r.WinnerOwnerId == 0L || r.LoserOwnerId == 0L || r.WinnerOwnerId == r.LoserOwnerId) return false;

            var winner = GetOrCreate(r.WinnerId, r.WinnerOwnerId, r.WinnerOwnerName, r.WinnerName, r.WinnerCaste);
            var loser = GetOrCreate(r.LoserId, r.LoserOwnerId, r.LoserOwnerName, r.LoserName, r.LoserCaste);

            int oldRank = RankOfLive(winner.companionId);

            // W/L + streaks always move (they reflect real matches played).
            winner.wins++;
            winner.winStreak++;
            if (winner.winStreak > winner.bestStreak) winner.bestStreak = winner.winStreak;
            loser.losses++;
            loser.winStreak = 0;
            var now = DateTime.UtcNow.Ticks;
            winner.lastMatchTicks = now;
            loser.lastMatchTicks = now;

            // Rating only moves outside the per-pair cooldown (anti friend-farm).
            var pairKey = PairKey(r.WinnerId, r.LoserId);
            bool onCooldown = _pairCooldown.TryGetValue(pairKey, out var last)
                              && (Time.time - last) < pairCooldownSeconds;
            if (!onCooldown)
            {
                Rating.Apply(winner.rating, loser.rating, kFactor, out var nw, out var nl);
                winner.rating = nw;
                loser.rating = nl;
                _pairCooldown[pairKey] = Time.time;
            }

            Save();

            winnerRating = winner.rating;
            winnerRank = RankOfLive(winner.companionId);
            // "Crossed a threshold" = climbed into the top 3 (or improved while in it).
            crossedThreshold = winnerRank >= 1 && winnerRank <= 3 && (oldRank == 0 || winnerRank < oldRank);
            return true;
        }

        // Applies a decided party duel to the authoritative table. Team rating is
        // owner-vs-owner Elo (each party's own rating is the Elo entrant). Returns
        // false if ignored (same owner / missing owners). Out params report the
        // winning party's rank movement for dvergr_party_rank_changed.
        public static bool ApplyPartyDuel(PartyDuelResult r, int kFactor,
            out int winnerRank, out int winnerRating, out bool crossedThreshold)
        {
            winnerRank = 0; winnerRating = Rating.StartRating; crossedThreshold = false;
            if (_data == null) LoadForCurrentWorld();
            if (r == null) return false;
            if (r.WinnerOwnerId == 0L || r.LoserOwnerId == 0L || r.WinnerOwnerId == r.LoserOwnerId) return false;

            var winner = GetOrCreateParty(r.WinnerOwnerId, r.WinnerOwnerName);
            var loser = GetOrCreateParty(r.LoserOwnerId, r.LoserOwnerName);

            // Keep the winner's chosen party name current (empty leaves the last one).
            if (!string.IsNullOrEmpty(r.WinnerPartyName)) winner.partyName = r.WinnerPartyName;

            int oldRank = RankOfLiveParty(winner.ownerId);

            winner.wins++;
            loser.losses++;
            var now = DateTime.UtcNow.Ticks;
            winner.lastMatchTicks = now;
            loser.lastMatchTicks = now;

            if (r.WinnerMembers != null && r.WinnerMembers.Count > 0)
            {
                winner.memberSnapshot = r.WinnerMembers;
                if (r.WinnerMembers.Count > winner.bestTeamSize) winner.bestTeamSize = r.WinnerMembers.Count;
            }
            if (r.LoserMembers != null && r.LoserMembers.Count > 0)
                loser.memberSnapshot = r.LoserMembers;

            Rating.Apply(winner.rating, loser.rating, kFactor, out var nw, out var nl);
            winner.rating = nw;
            loser.rating = nl;

            Save();

            winnerRating = winner.rating;
            winnerRank = RankOfLiveParty(winner.ownerId);
            crossedThreshold = winnerRank >= 1 && winnerRank <= 3 && (oldRank == 0 || winnerRank < oldRank);
            return true;
        }

        // Sets a player's party name (docs/Party-Duels.md). Server-only — the party
        // record is the persistent home for the name (survives relog), created on
        // demand so a player can name their party before its first ranked bout.
        public static void SetPartyName(long ownerId, string ownerName, string name)
        {
            if (ownerId == 0L) return;
            if (_data == null) LoadForCurrentWorld();
            var rec = GetOrCreateParty(ownerId, ownerName);
            rec.partyName = (name ?? string.Empty).Trim();
            Save();
        }

        public static int SeasonReset()
        {
            if (_data == null) LoadForCurrentWorld();
            int archived = _data.companions.Count;

            // Archive the outgoing board next to the live file.
            try
            {
                var path = StorePath(_loadedWorld ?? CurrentWorldName());
                var archive = path.Replace(".json", $".season{_data.seasonId}.json");
                File.WriteAllText(archive, JsonUtility.ToJson(_data, true));
            }
            catch (Exception e) { Plugin.Log.LogWarning($"[ladder] season archive failed: {e.Message}"); }

            var next = _data.seasonId + 1;
            _data = new LeaderboardData { seasonId = next };
            _snapshot = _data;
            _pairCooldown.Clear();
            Save();
            Plugin.Log.LogInfo($"[ladder] season reset: archived {archived} record(s), now season {next}.");
            return archived;
        }

        private static CompanionRecord GetOrCreate(string id, long ownerId, string ownerName, string name, int caste)
        {
            var rec = _data.companions.FirstOrDefault(c => c != null && c.companionId == id);
            if (rec == null)
            {
                rec = new CompanionRecord { companionId = id, rating = Rating.StartRating, seasonId = _data.seasonId };
                _data.companions.Add(rec);
            }
            // Refresh the denormalized snapshot each match so renames / offline
            // owners still display correctly.
            rec.ownerId = ownerId;
            if (!string.IsNullOrEmpty(ownerName)) rec.ownerName = ownerName;
            if (!string.IsNullOrEmpty(name)) rec.companionName = name;
            rec.caste = caste;
            return rec;
        }

        // Rank against the LIVE authoritative table (server-side), independent of
        // the client snapshot the query helpers read.
        private static int RankOfLive(string companionId)
        {
            var ranked = (_data?.companions ?? new List<CompanionRecord>())
                .Where(r => r != null)
                .OrderByDescending(r => r.rating).ThenByDescending(r => r.wins)
                .ToList();
            for (int i = 0; i < ranked.Count; i++)
                if (ranked[i].companionId == companionId) return i + 1;
            return 0;
        }

        private static PartyRecord GetOrCreateParty(long ownerId, string ownerName)
        {
            var rec = _data.parties.FirstOrDefault(p => p != null && p.ownerId == ownerId);
            if (rec == null)
            {
                rec = new PartyRecord { ownerId = ownerId, rating = Rating.StartRating, seasonId = _data.seasonId };
                _data.parties.Add(rec);
            }
            if (!string.IsNullOrEmpty(ownerName)) rec.ownerName = ownerName;
            return rec;
        }

        private static int RankOfLiveParty(long ownerId)
        {
            var ranked = (_data?.parties ?? new List<PartyRecord>())
                .Where(r => r != null)
                .OrderByDescending(r => r.rating).ThenByDescending(r => r.wins)
                .ToList();
            for (int i = 0; i < ranked.Count; i++)
                if (ranked[i].ownerId == ownerId) return i + 1;
            return 0;
        }

        private static string PairKey(string a, string b)
            => string.CompareOrdinal(a, b) <= 0 ? a + "|" + b : b + "|" + a;
    }
}
