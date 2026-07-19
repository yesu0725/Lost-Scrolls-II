using System;
using System.Collections.Generic;

namespace LostScrollsII.Ranking
{
    // Serializable model for the persistent, server-authoritative duel ladder
    // (docs/Ranking.md). Kept to plain public fields + List<T> so UnityEngine's
    // JsonUtility can round-trip it with zero external dependencies (it handles
    // [Serializable] classes and List<T> of them, but NOT dictionaries — hence the
    // flat lists, looked up by key in LeaderboardStore).

    // One companion's 1v1 ladder record. Keyed by CompanionId (the stable
    // DE_CompanionId GUID), denormalized with its owner so the row reads as
    // "owner + companion" without a second lookup.
    [Serializable]
    public class CompanionRecord
    {
        public string companionId;
        public long ownerId;
        public string ownerName;
        public string companionName;
        public int caste;

        public int wins;
        public int losses;
        public int winStreak;
        public int bestStreak;

        public int rating = Rating.StartRating;

        // UTC ticks of the last recorded match (decay / tie-break).
        public long lastMatchTicks;

        // Which season this record belongs to. Bumped by de_season_reset.
        public int seasonId = 1;
    }

    // One member of a party at the time of a match (docs/Party-Duels.md). Stored
    // in a PartyRecord's snapshot so the record shows WHICH companions earned it.
    [Serializable]
    public class PartyMemberSnap
    {
        public string companionId;
        public int caste;
        public int level;
    }

    // A party's ladder record. Keyed by ownerId — a party *is* an owner's stable,
    // which is the "owner" half of the record; memberSnapshot is the "companions"
    // half. Team rating is owner-vs-owner Elo.
    [Serializable]
    public class PartyRecord
    {
        public long ownerId;
        public string ownerName;

        // Player-chosen party name (docs/Party-Duels.md). Empty = fall back to the
        // owner's name for display. Set on the owner's client (player ZDO
        // DE_PartyName) and carried in on each reported party duel.
        public string partyName;

        public int wins;
        public int losses;
        public int rating = Rating.StartRating;
        public int bestTeamSize;

        public List<PartyMemberSnap> memberSnapshot = new List<PartyMemberSnap>();

        public long lastMatchTicks;
        public int seasonId = 1;
    }

    // The whole ladder file. Party records (Phase D) live alongside the 1v1
    // companion records; the on-disk schema has carried this list since Phase A.
    [Serializable]
    public class LeaderboardData
    {
        public int seasonId = 1;
        public List<CompanionRecord> companions = new List<CompanionRecord>();
        public List<PartyRecord> parties = new List<PartyRecord>();
    }

    // Wire form of a resolved party duel. Member lists are encoded compactly so the
    // whole result fits one RPC string arg (like DuelResult).
    public class PartyDuelResult
    {
        public long WinnerOwnerId;
        public string WinnerOwnerName;
        public long LoserOwnerId;
        public string LoserOwnerName;
        public int MvpCaste;
        public List<PartyMemberSnap> WinnerMembers = new List<PartyMemberSnap>();
        public List<PartyMemberSnap> LoserMembers = new List<PartyMemberSnap>();
        // Winning party's chosen name (docs/Party-Duels.md), empty = use owner name.
        public string WinnerPartyName;

        // Top-level fields split by '|'; member lists split by ',' with each
        // member's id:caste:level split by ':'. Names are sanitized of the
        // delimiters; companion ids are GUID hex (delimiter-free).
        public string Encode()
        {
            string S(string s) => (s ?? string.Empty).Replace('|', '/').Replace(',', ' ').Replace(':', ' ');
            string Members(List<PartyMemberSnap> ms)
            {
                if (ms == null || ms.Count == 0) return string.Empty;
                var parts = new List<string>(ms.Count);
                foreach (var m in ms) parts.Add($"{m.companionId}:{m.caste}:{m.level}");
                return string.Join(",", parts);
            }
            return string.Join("|", new[]
            {
                WinnerOwnerId.ToString(), S(WinnerOwnerName),
                LoserOwnerId.ToString(),  S(LoserOwnerName),
                MvpCaste.ToString(),
                Members(WinnerMembers), Members(LoserMembers),
                S(WinnerPartyName),
            });
        }

        public static PartyDuelResult Decode(string s)
        {
            if (string.IsNullOrEmpty(s)) return null;
            var p = s.Split('|');
            if (p.Length < 7) return null;
            long.TryParse(p[0], out var wOwner);
            long.TryParse(p[2], out var lOwner);
            int.TryParse(p[4], out var mvp);
            List<PartyMemberSnap> ParseMembers(string raw)
            {
                var list = new List<PartyMemberSnap>();
                if (string.IsNullOrEmpty(raw)) return list;
                foreach (var m in raw.Split(','))
                {
                    var f = m.Split(':');
                    if (f.Length < 3) continue;
                    int.TryParse(f[1], out var caste);
                    int.TryParse(f[2], out var level);
                    list.Add(new PartyMemberSnap { companionId = f[0], caste = caste, level = level });
                }
                return list;
            }
            return new PartyDuelResult
            {
                WinnerOwnerId = wOwner, WinnerOwnerName = p[1],
                LoserOwnerId = lOwner,  LoserOwnerName = p[3],
                MvpCaste = mvp,
                WinnerMembers = ParseMembers(p[5]), LoserMembers = ParseMembers(p[6]),
                WinnerPartyName = p.Length > 7 ? p[7] : string.Empty,
            };
        }
    }

    // Wire form of a single resolved 1v1 duel — everything the server needs to
    // update both records. Encoded to a delimited string for the RPC (ZRoutedRpc's
    // typed Register overloads don't stretch to this many args).
    public class DuelResult
    {
        public string WinnerId;
        public long WinnerOwnerId;
        public string WinnerOwnerName;
        public string WinnerName;
        public int WinnerCaste;

        public string LoserId;
        public long LoserOwnerId;
        public string LoserOwnerName;
        public string LoserName;
        public int LoserCaste;

        // Pipe-delimited; names are sanitized of '|' so decoding is unambiguous.
        public string Encode()
        {
            string S(string s) => (s ?? string.Empty).Replace('|', '/');
            return string.Join("|", new[]
            {
                S(WinnerId), WinnerOwnerId.ToString(), S(WinnerOwnerName), S(WinnerName), WinnerCaste.ToString(),
                S(LoserId),  LoserOwnerId.ToString(),  S(LoserOwnerName),  S(LoserName),  LoserCaste.ToString(),
            });
        }

        public static DuelResult Decode(string s)
        {
            if (string.IsNullOrEmpty(s)) return null;
            var p = s.Split('|');
            if (p.Length < 10) return null;
            long.TryParse(p[1], out var wOwner);
            int.TryParse(p[4], out var wCaste);
            long.TryParse(p[6], out var lOwner);
            int.TryParse(p[9], out var lCaste);
            return new DuelResult
            {
                WinnerId = p[0], WinnerOwnerId = wOwner, WinnerOwnerName = p[2], WinnerName = p[3], WinnerCaste = wCaste,
                LoserId = p[5],  LoserOwnerId = lOwner,  LoserOwnerName = p[7],  LoserName = p[8],  LoserCaste = lCaste,
            };
        }
    }
}
