using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using LostScrollsII.Ranking;
using UnityEngine;

namespace LostScrollsII.Bounty
{
    // The persistent bounty-hunter ladder (docs/Bounty-Hunting.md, Phase E).
    //
    // Same authority model as the duel ladder: the SERVER/host owns the data and the
    // file; pure clients hold a read-only snapshot pushed over BountySync. All
    // mutation is server-only, so a modded client can't write its own standing.
    //
    // Serialization goes through CompetitiveJson, NOT UnityEngine.JsonUtility —
    // that one silently drops List<[Serializable] class> fields on Valheim's current
    // runtime, which is exactly how the tournament board shipped showing zero
    // entrants on every client. Using the hand-rolled writer from day one here
    // avoids paying for that lesson twice.
    public static class BountyLeaderboardStore
    {
        private static BountyLadderData _data;                                   // server truth
        private static BountyLadderData _snapshot = new BountyLadderData();      // client view
        private static string _loadedWorld;

        public static BountyLadderData Snapshot => _snapshot;

        private static string StorePath(string world)
            => Path.Combine(LeaderboardStore.DataDir(), $"bounty.{Sanitize(world)}.json");

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
                    _data = CompetitiveJson.ReadBountyLadder(File.ReadAllText(path)) ?? new BountyLadderData();
                    if (_data.hunters == null) _data.hunters = new List<BountyHunterRecord>();
                    Plugin.Log.LogInfo($"[bounty] loaded {_data.hunters.Count} hunter record(s) for world '{world}'.");
                }
                else
                {
                    _data = new BountyLadderData();
                    Plugin.Log.LogInfo($"[bounty] no bounty ladder for world '{world}' — starting fresh.");
                }
            }
            catch (Exception e)
            {
                _data = new BountyLadderData();
                Plugin.Log.LogWarning($"[bounty] failed to load '{path}': {e.Message}. Starting fresh.");
            }
            _snapshot = _data; // the host reads the live table directly
        }

        public static void Save()
        {
            if (_data == null) return;
            try
            {
                File.WriteAllText(StorePath(_loadedWorld ?? CurrentWorldName()),
                    CompetitiveJson.Write(_data, true));
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning($"[bounty] ladder save failed: {e.Message}");
            }
        }

        // ---- Snapshot sync ----------------------------------------------------

        public static string SerializeSnapshot() => CompetitiveJson.Write(_data ?? new BountyLadderData());

        public static void ApplySnapshot(string json)
        {
            try
            {
                var d = CompetitiveJson.ReadBountyLadder(json) ?? new BountyLadderData();
                if (d.hunters == null) d.hunters = new List<BountyHunterRecord>();
                _snapshot = d;
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning($"[bounty] failed to apply ladder snapshot: {e.Message}");
            }
        }

        // ---- Mutation (server only) ------------------------------------------

        // Record one answered bounty. Points are TIER-WEIGHTED, so hunting harder
        // postings outruns grinding easy ones — the difficulty is baked into the
        // score rather than needing a separate anti-farm rule.
        public static bool ApplyResolution(long ownerId, string ownerName, int tier, string method,
            out int rank, out int points)
        {
            rank = 0;
            points = 0;
            if (_data == null) _data = new BountyLadderData();
            if (ownerId == 0L) return false;

            var rec = _data.hunters.FirstOrDefault(h => h != null && h.ownerId == ownerId);
            if (rec == null)
            {
                rec = new BountyHunterRecord { ownerId = ownerId, seasonId = _data.seasonId };
                _data.hunters.Add(rec);
            }

            if (!string.IsNullOrEmpty(ownerName)) rec.ownerName = ownerName;

            int per = Mathf.Max(1, Plugin.BountyPointsPerTier?.Value ?? 10);
            int gained = per * BountyTiers.Clamp(tier);
            rec.points += gained;

            if (method == BountyService.MethodCommuned) rec.communes++;
            else rec.kills++;

            if (tier > rec.bestTier) rec.bestTier = BountyTiers.Clamp(tier);
            rec.lastResolvedTicks = DateTime.UtcNow.Ticks;

            Save();

            rank = RankOf(ownerId);
            points = rec.points;
            return true;
        }

        // ---- Queries (client or host, off the snapshot) ----------------------

        public static List<BountyHunterRecord> Ranked()
        {
            var src = _snapshot?.hunters ?? new List<BountyHunterRecord>();
            return src.Where(h => h != null)
                      .OrderByDescending(h => h.points)
                      .ThenByDescending(h => h.bestTier)
                      .ThenBy(h => h.ownerName ?? "")
                      .ToList();
        }

        public static int RankOf(long ownerId)
        {
            if (ownerId == 0L) return 0;
            var ranked = Ranked();
            for (int i = 0; i < ranked.Count; i++)
                if (ranked[i].ownerId == ownerId) return i + 1;
            return 0;
        }

        public static BountyHunterRecord Find(long ownerId)
            => (_snapshot?.hunters ?? new List<BountyHunterRecord>())
                .FirstOrDefault(h => h != null && h.ownerId == ownerId);

        // ---- Season -----------------------------------------------------------

        // Archives the board and starts a new season, mirroring de_season_reset on
        // the duel ladder so a long-lived server's standings don't ossify.
        public static int SeasonReset()
        {
            if (_data == null) return 0;
            int archived = _data.hunters.Count;
            try
            {
                var world = _loadedWorld ?? CurrentWorldName();
                var archivePath = Path.Combine(LeaderboardStore.DataDir(),
                    $"bounty.{Sanitize(world)}.season{_data.seasonId}.json");
                File.WriteAllText(archivePath, CompetitiveJson.Write(_data, true));
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning($"[bounty] season archive failed: {e.Message}");
            }

            _data.seasonId++;
            _data.hunters.Clear();
            Save();
            _snapshot = _data;
            return archived;
        }
    }
}
