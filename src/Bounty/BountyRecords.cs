using System;
using System.Collections.Generic;

namespace LostScrollsII.Bounty
{
    // Persisted shape of the bounty-hunter ladder (docs/Bounty-Hunting.md, Phase E).
    //
    // Keyed by OWNER, not by companion: bounty hunting is something a player does,
    // unlike the duel ladders where a companion earns the standing. That's also why
    // there's no Elo here — bounties aren't head-to-head, so a cumulative
    // tier-weighted score is the honest measure, and it can't be farmed by beating
    // the same weak opponent repeatedly (the difficulty is baked into the weight).
    [Serializable]
    public class BountyHunterRecord
    {
        public long ownerId;
        public string ownerName;      // snapshot, so an offline hunter still shows
        public int points;            // tier-weighted total — what the ladder sorts by
        public int kills;             // bounties answered with the sword
        public int communes;          // bounties answered with the Rite
        public int bestTier;          // hardest posting ever answered
        public long lastResolvedTicks;// UTC ticks, for tie-breaks
        public int seasonId = 1;

        public int Total => kills + communes;
    }

    [Serializable]
    public class BountyLadderData
    {
        public int seasonId = 1;
        public List<BountyHunterRecord> hunters = new List<BountyHunterRecord>();
    }
}
