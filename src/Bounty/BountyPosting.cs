using System;
using System.Collections.Generic;

namespace LostScrollsII.Bounty
{
    // One posting on the Wanted Board (docs/Bounty-Hunting.md, Phase F).
    //
    // A posting is a LOCATION and a difficulty, not a live creature. The creatures
    // are spawned when a hunter actually reaches the spot — Valheim doesn't simulate
    // unloaded zones, so a bounty spawned across the map would either be frozen or
    // wander off unwatched. Keeping the posting abstract until arrival also means the
    // board survives restarts cleanly: there's nothing live to restore.
    [Serializable]
    public class BountyPosting
    {
        public string id;              // GUID, and the bounty id the spawned creatures carry
        public int tier;
        public int biome;              // Heightmap.Biome as int
        public float x, y, z;

        public long acceptedBy;        // 0 = open to anyone
        public string acceptedByName;
        public bool spawned;           // creatures have been instantiated at least once
        public long postedTicks;
        // The warden's commission (Phase G): answering THIS posting is what opens the
        // board. Marked so a player who stumbles into someone else's bounty first
        // doesn't skip the quest.
        public bool tutorial;

        public bool IsOpen => acceptedBy == 0L;

        public UnityEngine.Vector3 Position => new UnityEngine.Vector3(x, y, z);

        public Heightmap.Biome Biome => (Heightmap.Biome)biome;

        // "Dread bounty (Plains)" — used on the panel, the pin and the console.
        public string Label => $"{BountyTiers.TierName(tier)} bounty ({Biome})";
    }

    [Serializable]
    public class BountyBoardData
    {
        public List<BountyPosting> postings = new List<BountyPosting>();
    }
}
