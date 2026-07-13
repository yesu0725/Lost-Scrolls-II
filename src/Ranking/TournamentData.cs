using System;
using System.Collections.Generic;

namespace LostScrollsII.Ranking
{
    // Serializable model for the tournament bracket runner (docs/Tournaments.md).
    // Persisted to JSON (JsonUtility) and pushed to clients as a read-only snapshot,
    // exactly like the ladder.

    // One registered entrant. For a 1v1 tournament the entrant is a companion
    // (entrantId = companionId, label = companion name). For a party tournament the
    // entrant is an owner (entrantId = ownerId string, label = owner name).
    [Serializable]
    public class TournamentEntrant
    {
        public string entrantId;
        public long ownerId;
        public string ownerName;
        public string label;
        public int caste;        // 1v1: winner caste for triggers; party: -1
        public int seedRating;
        public bool eliminated;
    }

    // One bracket match. A bye is a match whose bId is empty and whose winnerId is
    // set to aId at creation, so it counts as decided immediately.
    [Serializable]
    public class TournamentMatch
    {
        public int round;
        public string aId;
        public string bId;      // "" = bye
        public string aLabel;
        public string bLabel;
        public string winnerId; // "" until decided
    }

    // The whole tournament. Only one is active at a time.
    [Serializable]
    public class TournamentState
    {
        public bool active;
        public string mode = "1v1";        // "1v1" | "party"
        public string phase = "idle";      // idle | registration | running | complete
        public int size;                   // target bracket size, 0 = open until begin
        public int bracketSize;            // actual entrant count at begin
        public int currentRound;
        public List<TournamentEntrant> entrants = new List<TournamentEntrant>();
        public List<TournamentMatch> matches = new List<TournamentMatch>();
        public string championId = "";
        public string championLabel = "";
        public int seasonId = 1;

        public TournamentEntrant Find(string id)
            => entrants.Find(e => e != null && e.entrantId == id);

        // The undecided match in the current round containing both ids (either order).
        public TournamentMatch ActiveMatchBetween(string x, string y)
        {
            foreach (var m in matches)
            {
                if (m.round != currentRound || !string.IsNullOrEmpty(m.winnerId)) continue;
                if ((m.aId == x && m.bId == y) || (m.aId == y && m.bId == x)) return m;
            }
            return null;
        }
    }

    // A Hall-of-Champions entry (one per completed tournament).
    [Serializable]
    public class ChampionRecord
    {
        public string mode;
        public string championLabel;
        public string ownerName;
        public int bracketSize;
        public int seasonId;
        public long dateTicks;
    }

    [Serializable]
    public class ChampionsData
    {
        public List<ChampionRecord> champions = new List<ChampionRecord>();
    }
}
