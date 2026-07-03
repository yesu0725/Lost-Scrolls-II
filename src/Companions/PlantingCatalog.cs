using System.Collections.Generic;
using UnityEngine;

namespace LostScrollsII.Companions
{
    // Maps a seed/plantable item prefab name -> the sapling prefab that item plants.
    // Built once from ZNetScene: any prefab that has BOTH a Plant (it grows) and a
    // Piece (it's placeable) is a "sapling"; that Piece's resource requirement names
    // the seed item which plants it. This lets the farm chore plant WHATEVER seed is
    // in the chest, of ANY crop type, without hardcoding crop→seed pairs — satisfying
    // "work on all plants regardless of type" (docs/Ally-Chores.md → Farming).
    public static class PlantingCatalog
    {
        private static Dictionary<string, GameObject> s_map;

        // Lazily build once the prefab list exists. If ZNetScene isn't ready yet we
        // leave it unbuilt and retry on the next call (harmless — the farm chore just
        // won't plant until the scene is up).
        private static void EnsureBuilt()
        {
            if (s_map != null) return;
            if (ZNetScene.instance == null) return;

            var map = new Dictionary<string, GameObject>();
            foreach (var go in ZNetScene.instance.m_prefabs)
            {
                if (go == null) continue;
                var plant = go.GetComponent<Plant>();
                var piece = go.GetComponent<Piece>();
                if (plant == null || piece == null || piece.m_resources == null) continue;

                foreach (var req in piece.m_resources)
                {
                    if (req == null || req.m_resItem == null) continue;
                    var seedName = req.m_resItem.name; // Component.name == prefab GameObject name
                    if (!string.IsNullOrEmpty(seedName) && !map.ContainsKey(seedName))
                        map[seedName] = go;
                }
            }

            s_map = map;
            Plugin.Log.LogInfo($"[farm] planting catalog built: {s_map.Count} seed→sapling entries.");
        }

        // True if `seedPrefabName` (a chest item's prefab name) can be planted; if so
        // `sapling` is the prefab to instantiate.
        public static bool TryGetSapling(string seedPrefabName, out GameObject sapling)
        {
            sapling = null;
            if (string.IsNullOrEmpty(seedPrefabName)) return false;
            EnsureBuilt();
            return s_map != null && s_map.TryGetValue(seedPrefabName, out sapling);
        }
    }
}
