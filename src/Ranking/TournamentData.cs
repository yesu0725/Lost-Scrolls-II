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
        public int level;        // 1v1: the sealed companion's level (0 = unknown/party)

        // Double elimination (feature #3): number of losses so far. Single/round-robin
        // never touch this beyond 0/1.
        public int losses;

        // Win count — only meaningfully used by round robin (champion = most wins),
        // but tracked generically since it's cheap.
        public int wins;

        // Escrow (docs/Tournaments.md — escrow & auto-summon). The companion(s) are
        // sealed into their Communion Totem(s) at registration and held here for the
        // whole tournament: the server summons them for a match and reseals them
        // afterward. Each payload is a totem's serialized m_customData (the same
        // format TotemConversionService writes). 1v1 uses `totemPayload`; a party
        // uses `teamPayloads` (one per member, up to MaxPartySize).
        public string totemPayload;
        public List<string> teamPayloads = new List<string>();

        // Wagered tournaments (docs/Wagers.md). The stake this entrant actually
        // paid, recorded so a cancelled or unfilled tournament can hand back
        // exactly what it took. 0 on the free admin-run tournaments.
        public int feePaid;
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
        public int aLevel;
        public int bLevel;
        public string winnerId; // "" until decided

        // Double elimination (feature #3): which pool this match belongs to.
        // Single/round-robin always use "W".
        public string bracket = "W"; // "W" | "L" | "GF" (grand final)

        // Wagered tournaments: the two owners agree WHERE to fight by both marking
        // themselves ready wherever they have met up (docs/Wagers.md). The server
        // summons both companions only once both flags are set, so nothing is
        // dragged into the world before the players are actually standing together.
        // `activated` stops a second summon if a ready flag is re-sent.
        public bool aReady;
        public bool bReady;
        public bool activated;
    }

    // One tournament. Several can now run at once — see TournamentBook — so each
    // carries the `key` identifying its slot.
    [Serializable]
    public class TournamentState
    {
        // Slot id, and the only handle every API takes:
        //   ""        the free admin-run tournament (the original behaviour;
        //             any mode, any format, any size)
        //   "coins"   the Coin-staked player tournament
        //   "valcoin" the Valcoin-staked player tournament
        // Exactly one tournament may occupy each slot at a time, which is what
        // enforces "one Valcoin and one Coin tournament at a time".
        public string key = "";

        // Wager fields (docs/Wagers.md). currency is "" on the free tournament.
        public string currency = "";
        public int entryFee;
        public int prize;
        public long hostId;          // the player who started (and paid to open) it
        public string hostName = "";
        public long openedTicks;     // UTC ticks registration opened, for expiry

        // The host's opening fee, held until they claim their own slot. Opening a
        // tournament and entering it cost one fee, not two: the host still locks a
        // totem like everyone else, but that entry is paid for from here. If they
        // never enter (or the event is cancelled first) this is what gets refunded.
        public int hostCredit;

        // UTC ticks the bracket finished. Escrowed totems are NOT handed back the
        // instant a tournament completes: the winning client still has to reseal its
        // live companion (with the XP it just earned) and report that payload back.
        // Returning immediately would hand the champion a totem holding its
        // pre-final state. Tick() waits a short grace after this before returning
        // anything, which is long enough for the 1 Hz reseal to land.
        public long completedTicks;

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

        // "single" | "double" | "round_robin" (feature #3). Defaults to "single" so
        // any pre-existing saved state (missing this field) resumes as before.
        public string eliminationType = "single";

        // Round-robin only: total scheduled rounds (circle-method schedule length)
        // so ResolveMatch knows when the whole schedule — not just one round — is
        // exhausted. Unused by single/double.
        public int totalRounds;

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

    // Every tournament currently in existence, keyed by slot. Persisted and pushed
    // to clients as one document so a client sees all of them in a single snapshot
    // (the panel lists them side by side).
    //
    // This replaced a single persisted TournamentState. A file written by an older
    // build still loads: the reader falls back to treating a bare tournament object
    // as the one free-slot tournament (see CompetitiveJson.ReadTournamentBook).
    [Serializable]
    public class TournamentBook
    {
        public List<TournamentState> tournaments = new List<TournamentState>();

        public TournamentState Get(string key)
            => tournaments.Find(t => t != null && (t.key ?? "") == (key ?? ""));

        // The tournament an entrant id belongs to, across every slot. Used by the
        // result-intake and the client-side combatant driver, neither of which
        // knows which slot a companion was entered in.
        public TournamentState ForEntrant(string entrantId)
        {
            if (string.IsNullOrEmpty(entrantId)) return null;
            foreach (var t in tournaments)
                if (t != null && t.Find(entrantId) != null) return t;
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
