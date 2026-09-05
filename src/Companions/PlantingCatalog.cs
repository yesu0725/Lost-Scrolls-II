using System.Collections.Generic;
using UnityEngine;

namespace LostScrollsII.Companions
{
    // Maps a seed/plantable item -> the sapling prefab that item plants, and back.
    // Built once from ZNetScene: any prefab that has BOTH a Plant (it grows) and a
    // Piece (it's placeable) is a "sapling"; that Piece's resource requirement names
    // the seed item which plants it. This lets the farm chore plant WHATEVER seed is
    // available, of ANY crop type, without hardcoding crop→seed pairs — satisfying
    // "work on all plants regardless of type" (docs/Ally-Chores.md → Farming).
    //
    // EVERY LOOKUP IS BUILT HERE, INCLUDING THE SHARED NAMES.
    //
    // The shared-name direction used to be resolved on demand through
    // `ObjectDB.instance.GetItemPrefab(...)`, and that was the reason seeds carried
    // in a companion's PACK stayed invisible to the farmer while the same seeds in a
    // chest were found: a pack rebuilt from its ZDO hands back items whose
    // m_dropPrefab is null or "(Clone)"-suffixed, so the shared name is the only
    // usable key — and the ObjectDB round-trip that produced it is not dependable
    // everywhere the chore runs (a dedicated server being the case that matters).
    //
    // The seed's ItemDrop is already in hand while the catalog is being built
    // (`req.m_resItem`), so both directions are captured there and no lookup is
    // needed later at all.
    public static class PlantingCatalog
    {
        private static Dictionary<string, GameObject> s_byPrefabName;
        private static Dictionary<string, GameObject> s_bySharedName;
        private static Dictionary<GameObject, string> s_prefabNameOf;
        private static Dictionary<GameObject, string> s_sharedNameOf;

        // Lazily build once the prefab list exists. If ZNetScene isn't ready yet we
        // leave it unbuilt and retry on the next call (harmless — the farm chore just
        // won't plant until the scene is up).
        private static void EnsureBuilt()
        {
            if (s_byPrefabName != null) return;
            if (ZNetScene.instance == null) return;

            var byPrefab = new Dictionary<string, GameObject>();
            var byShared = new Dictionary<string, GameObject>();
            var prefabOf = new Dictionary<GameObject, string>();
            var sharedOf = new Dictionary<GameObject, string>();

            foreach (var go in ZNetScene.instance.m_prefabs)
            {
                if (go == null) continue;
                var plant = go.GetComponent<Plant>();
                var piece = go.GetComponent<Piece>();
                if (plant == null || piece == null || piece.m_resources == null || piece.m_resources.Length == 0) continue;

                // ONLY THE FIRST resource. Every vanilla sapling costs exactly one
                // item and that item is its seed, so the first entry is the seed by
                // construction. Mapping all of them would let a modded plant that
                // also costs, say, Wood register "Wood" as a seed — and the farmer
                // would then plant trees out of your firewood.
                var req = piece.m_resources[0];
                if (req == null || req.m_resItem == null) continue;

                var seedPrefabName = req.m_resItem.name; // Component.name == prefab GameObject name
                if (string.IsNullOrEmpty(seedPrefabName)) continue;

                if (!byPrefab.ContainsKey(seedPrefabName))
                {
                    byPrefab[seedPrefabName] = go;
                    if (!prefabOf.ContainsKey(go)) prefabOf[go] = seedPrefabName;
                }

                var shared = req.m_resItem.m_itemData != null && req.m_resItem.m_itemData.m_shared != null
                    ? req.m_resItem.m_itemData.m_shared.m_name
                    : null;
                if (!string.IsNullOrEmpty(shared))
                {
                    if (!byShared.ContainsKey(shared)) byShared[shared] = go;
                    if (!sharedOf.ContainsKey(go)) sharedOf[go] = shared;
                }
            }

            s_byPrefabName = byPrefab;
            s_bySharedName = byShared;
            s_prefabNameOf = prefabOf;
            s_sharedNameOf = sharedOf;

            Plugin.Log.LogInfo($"[farm] planting catalog built: {s_byPrefabName.Count} seed→sapling entries " +
                               $"({s_bySharedName.Count} by shared name).");

            // The full census, once. "Some of my seeds aren't recognised" is
            // otherwise unanswerable without guessing at prefab names, and what
            // counts as plantable is decided by the game's data, not by us.
            foreach (var pair in s_byPrefabName)
                Plugin.Log.LogInfo($"[farm]   {pair.Key} -> {pair.Value.name}");
        }

        // True if `seedPrefabName` (an item's prefab name) can be planted; if so
        // `sapling` is the prefab to instantiate.
        public static bool TryGetSapling(string seedPrefabName, out GameObject sapling)
        {
            sapling = null;
            if (string.IsNullOrEmpty(seedPrefabName)) return false;
            EnsureBuilt();
            return s_byPrefabName != null && s_byPrefabName.TryGetValue(seedPrefabName, out sapling);
        }

        // The sapling a seed with this SHARED name plants, or null. The dependable
        // direction for an item that came out of an inventory (see the class note).
        public static GameObject SaplingForSharedName(string sharedName)
        {
            if (string.IsNullOrEmpty(sharedName)) return null;
            EnsureBuilt();

            GameObject sapling = null;
            return s_bySharedName != null && s_bySharedName.TryGetValue(sharedName, out sapling)
                ? sapling : null;
        }

        // Which seed item plants this sapling — the reverse lookups the farm chore
        // needs once it has decided the field's crop and gone looking for its seed.
        public static string SeedFor(GameObject sapling)
        {
            if (sapling == null) return null;
            EnsureBuilt();

            string name = null;
            return s_prefabNameOf != null && s_prefabNameOf.TryGetValue(sapling, out name) ? name : null;
        }

        public static string SeedSharedName(GameObject sapling)
        {
            if (sapling == null) return null;
            EnsureBuilt();

            string name = null;
            return s_sharedNameOf != null && s_sharedNameOf.TryGetValue(sapling, out name) ? name : null;
        }
    }
}
