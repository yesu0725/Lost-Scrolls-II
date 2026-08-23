using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using LostScrollsII.Ranking;
using UnityEngine;

namespace LostScrollsII.Bounty
{
    // The Wanted Board itself (docs/Bounty-Hunting.md, Phase F).
    //
    // Server-authoritative like the ladders: the server samples locations, owns the
    // postings, and decides who accepted what. Clients hold a pushed snapshot and can
    // only *ask* to accept — otherwise a modded client could hand itself a posting
    // someone else already took.
    //
    // Postings persist per world so the board survives a restart. That's cheap
    // because a posting is only a location + a tier; nothing live is stored.
    public static class BountyBoardStore
    {
        private static BountyBoardData _data;                             // server truth
        private static BountyBoardData _snapshot = new BountyBoardData(); // client view
        private static string _loadedWorld;

        public static BountyBoardData Snapshot => _snapshot;

        private static string StorePath(string world)
            => Path.Combine(LeaderboardStore.DataDir(), $"board.{Sanitize(world)}.json");

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
                    _data = CompetitiveJson.ReadBountyBoard(File.ReadAllText(path)) ?? new BountyBoardData();
                    if (_data.postings == null) _data.postings = new List<BountyPosting>();
                    Plugin.Log.LogInfo($"[bounty] loaded {_data.postings.Count} posting(s) for world '{world}'.");
                }
                else
                {
                    _data = new BountyBoardData();
                    Plugin.Log.LogInfo($"[bounty] no board file for world '{world}' — starting fresh.");
                }
            }
            catch (Exception e)
            {
                _data = new BountyBoardData();
                Plugin.Log.LogWarning($"[bounty] failed to load '{path}': {e.Message}. Starting fresh.");
            }
            _snapshot = _data;
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
                Plugin.Log.LogWarning($"[bounty] board save failed: {e.Message}");
            }
        }

        // ---- Snapshot sync ----------------------------------------------------

        public static string SerializeSnapshot() => CompetitiveJson.Write(_data ?? new BountyBoardData());

        public static void ApplySnapshot(string json)
        {
            try
            {
                var d = CompetitiveJson.ReadBountyBoard(json) ?? new BountyBoardData();
                if (d.postings == null) d.postings = new List<BountyPosting>();
                _snapshot = d;
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning($"[bounty] failed to apply board snapshot: {e.Message}");
            }
        }

        // ---- Board management (server only) ----------------------------------

        // Rotate the board and top it back up. Returns true if anything changed.
        //
        // Two things happen here, in order:
        //  1. OPEN postings older than RefreshHours are retired, so the board turns
        //     over on its own even if nobody hunts — a reason to check back.
        //  2. The board is refilled to the configured number of OPEN postings.
        //
        // ACCEPTED postings never expire: a hunter part-way to their mark must not
        // have it pulled out from under them. Nor does the warden's commission.
        public static bool EnsurePostings()
        {
            if (_data == null) _data = new BountyBoardData();

            int retired = PruneExpired();

            int want = Mathf.Max(0, Plugin.BountyMaxBoardEntries?.Value ?? 3);
            int open = _data.postings.Count(p => p != null && p.IsOpen);
            int added = 0;

            for (int i = open; i < want; i++)
            {
                if (!TryCreatePosting(out var posting)) break;
                _data.postings.Add(posting);
                added++;
            }

            if (added > 0 || retired > 0)
            {
                Save();
                Plugin.Log.LogInfo($"[bounty] board rotated: retired {retired}, posted {added} " +
                    $"({_data.postings.Count} on the board).");
            }
            return added > 0 || retired > 0;
        }

        // Retire stale OPEN postings. Real elapsed time, not in-game days, so the
        // cadence matches how an admin thinks about it ("daily", "twice a day") and
        // survives restarts — postedTicks is UTC and persists with the board.
        private static int PruneExpired()
        {
            float hours = Plugin.BountyRefreshHours?.Value ?? 24f;
            if (hours <= 0f) return 0; // rotation disabled

            long cutoff = DateTime.UtcNow.AddHours(-hours).Ticks;
            return _data.postings.RemoveAll(p =>
                p != null && p.IsOpen && !p.tutorial && p.postedTicks > 0 && p.postedTicks < cutoff);
        }

        private static bool TryCreatePosting(out BountyPosting posting)
        {
            posting = null;
            if (!BountyLocationSampler.TrySample(Vector3.zero,
                    Plugin.BountySearchMinRadius?.Value ?? 500f,
                    Plugin.BountySearchMaxRadius?.Value ?? 6000f,
                    out var pos, out var biome, out var stats))
            {
                Plugin.Log.LogWarning($"[bounty] could not place a posting: {stats}");
                return false;
            }

            int tier = BountyTiers.Clamp(BountyTiers.BaseTierFor(biome));

            // An ELITE posting may replace an ordinary one — the top tier is never
            // reached by biome alone, so this is the only way it appears. Capped at one
            // open at a time: it's meant to be the thing on the board, not a category.
            float eliteChance = Mathf.Clamp01(Plugin.BountyEliteChance?.Value ?? 0.25f);
            bool eliteOpen = _data.postings.Any(p => p != null && p.IsOpen && p.tier >= BountyTiers.EliteTier);
            if (!eliteOpen && eliteChance > 0f && UnityEngine.Random.value < eliteChance)
                tier = BountyTiers.EliteTier;

            posting = new BountyPosting
            {
                id = Guid.NewGuid().ToString("N"),
                tier = tier,
                biome = (int)biome,
                x = pos.x,
                y = pos.y,
                z = pos.z,
                postedTicks = DateTime.UtcNow.Ticks,
            };
            return true;
        }

        public static bool IsElite(BountyPosting p) => p != null && p.tier >= BountyTiers.EliteTier;

        // The warden's commission (Phase G): a guaranteed tier 1 posting created FOR
        // one player and already assigned to them, so there's no chicken-and-egg
        // problem of needing board access to take your first bounty.
        //
        // Sampled near that player rather than anywhere in the world — a first bounty
        // 6 km away isn't a tutorial. The four bounty biomes are common enough that a
        // close sample almost always succeeds; if it doesn't, the radius widens once
        // before giving up.
        public static string CreateTutorial(long ownerId, string ownerName, Vector3 near, out BountyPosting created)
        {
            created = null;
            if (_data == null) _data = new BountyBoardData();
            if (ownerId == 0L) return "Unknown player.";

            // Already holding one (including an unfinished commission) — don't stack.
            var existing = _data.postings.FirstOrDefault(p => p != null && p.acceptedBy == ownerId);
            if (existing != null)
            {
                created = existing;
                return "You already have a bounty to answer.";
            }

            if (!TrySampleNear(near, TutorialMinRadius, TutorialMaxRadius, out var pos, out var biome)
                && !TrySampleNear(near, TutorialMinRadius, TutorialMaxRadius * 3f, out pos, out biome))
            {
                return "The warden has no posting close enough to point you at right now.";
            }

            var posting = new BountyPosting
            {
                id = Guid.NewGuid().ToString("N"),
                // Always the lowest tier: this is someone's first bounty, and the
                // biome it lands in shouldn't decide how hard that is.
                tier = BountyTiers.MinTier,
                biome = (int)biome,
                x = pos.x,
                y = pos.y,
                z = pos.z,
                postedTicks = DateTime.UtcNow.Ticks,
                acceptedBy = ownerId,
                acceptedByName = ownerName,
                tutorial = true,
            };
            _data.postings.Add(posting);
            Save();
            created = posting;
            Plugin.Log.LogInfo($"[bounty] warden's commission posted for '{ownerName}' — {posting.Label} " +
                $"at ({pos.x:F0}, {pos.z:F0}).");
            return $"The warden marks your map: {posting.Label}.";
        }

        private const float TutorialMinRadius = 150f;
        private const float TutorialMaxRadius = 1200f;

        private static bool TrySampleNear(Vector3 near, float min, float max,
            out Vector3 pos, out Heightmap.Biome biome)
            => BountyLocationSampler.TrySample(near, min, max, out pos, out biome, out _);

        public static bool IsTutorial(string postingId)
        {
            var p = Find(postingId);
            return p != null && p.tutorial;
        }

        // A player takes a posting. Enforced server-side so two players can't hold
        // the same one, and one player can't hoard the board.
        public static string Accept(long ownerId, string ownerName, string postingId)
        {
            if (_data == null) return "The board isn't ready.";
            if (ownerId == 0L) return "Unknown player.";

            var posting = Find(postingId);
            if (posting == null) return "That posting is no longer on the board.";
            if (!posting.IsOpen)
            {
                return posting.acceptedBy == ownerId
                    ? "You have already taken that posting."
                    : $"{posting.acceptedByName} has already taken that posting.";
            }

            int cap = Mathf.Max(1, Plugin.BountyMaxActivePerPlayer?.Value ?? 1);
            int held = _data.postings.Count(p => p != null && p.acceptedBy == ownerId);
            if (held >= cap)
                return cap == 1
                    ? "You already have an active bounty. Finish or abandon it first."
                    : $"You already hold {held} bounties (limit {cap}).";

            // Elite postings are rank-gated. Re-checked HERE, server-side, rather than
            // trusting the panel to have hidden it — the panel hides it for looks, this
            // is the actual rule.
            if (IsElite(posting) && !BountyService.IsEliteEligible(ownerId))
            {
                int topN = Plugin.BountyEliteRankTopN?.Value ?? 10;
                return $"Only the realm's finest may answer an {BountyTiers.TierName(posting.tier)} posting " +
                       $"— reach the top {topN} on the duel or party ladder first.";
            }

            posting.acceptedBy = ownerId;
            posting.acceptedByName = ownerName;
            Save();
            return $"Accepted: {posting.Label}.";
        }

        public static string Abandon(long ownerId)
        {
            if (_data == null) return "The board isn't ready.";
            var mine = _data.postings.Where(p => p != null && p.acceptedBy == ownerId).ToList();
            if (mine.Count == 0) return "You have no active bounty.";

            foreach (var p in mine)
            {
                // An abandoned posting goes back on the board rather than being
                // deleted — the location is still valid, and anything already spawned
                // there is still standing.
                p.acceptedBy = 0L;
                p.acceptedByName = null;
            }
            Save();
            return mine.Count == 1 ? "Bounty abandoned." : $"{mine.Count} bounties abandoned.";
        }

        // Called when a bounty is answered — the posting leaves the board for good.
        public static bool Remove(string postingId)
        {
            if (_data == null || string.IsNullOrEmpty(postingId)) return false;
            int removed = _data.postings.RemoveAll(p => p != null && p.id == postingId);
            if (removed > 0) Save();
            return removed > 0;
        }

        public static void MarkSpawned(string postingId)
        {
            var p = Find(postingId);
            if (p == null || p.spawned) return;
            p.spawned = true;
            Save();
        }

        // ---- Queries (client or host, off the snapshot) ----------------------

        public static BountyPosting Find(string postingId)
            => (_snapshot?.postings ?? new List<BountyPosting>())
                .FirstOrDefault(p => p != null && p.id == postingId);

        public static List<BountyPosting> All()
            => (_snapshot?.postings ?? new List<BountyPosting>()).Where(p => p != null).ToList();

        public static List<BountyPosting> OpenPostings()
            => All().Where(p => p.IsOpen).OrderBy(p => p.tier).ToList();

        public static BountyPosting AcceptedBy(long ownerId)
            => ownerId == 0L ? null : All().FirstOrDefault(p => p.acceptedBy == ownerId);

        public static void ClearAll()
        {
            if (_data == null) return;
            _data.postings.Clear();
            Save();
        }
    }
}
