using System.Text;
using UnityEngine;

namespace LostScrollsII.Bounty
{
    // Picks a random LAND location in one of the bounty biomes
    // (docs/Bounty-Hunting.md, Phase B).
    //
    // The hard requirement is that a bounty never lands underwater, on a shoreline,
    // or on a small island — a target the player can't reach on foot is a broken
    // bounty. Two things make that reliable:
    //
    //  1. Everything is read from WorldGenerator, which is PROCEDURAL — it answers
    //     for any coordinate in the world whether or not that zone is loaded. A
    //     physics raycast would only work near a loaded player, which is exactly
    //     what a server-side bounty posting can't rely on.
    //
    //  2. A single point being above water proves nothing (a rock in the sea passes).
    //     So the candidate is validated against THREE concentric rings out to
    //     LandCheckRadius — every sampled point must be dry land and none may be
    //     Ocean. Clearing the outermost ring means the surrounding landmass is at
    //     least ~2x LandCheckRadius across, which is what actually rules out islets
    //     and narrow spits.
    public static class BountyLocationSampler
    {
        // The four biomes bounties are posted in (requirement: BlackForest, Swamp,
        // Mountain, Plains — never Meadows/Mistlands/Ashlands/Ocean).
        public static readonly Heightmap.Biome[] BountyBiomes =
        {
            Heightmap.Biome.BlackForest,
            Heightmap.Biome.Swamp,
            Heightmap.Biome.Mountain,
            Heightmap.Biome.Plains,
        };

        // Points per ring. 8 is a compass rose — enough to catch a spit of land or a
        // coastline cutting through the candidate area without making each attempt
        // expensive (3 rings x 8 + centre = 25 height reads).
        private const int RingPoints = 8;
        private const int Rings = 3;

        public static bool IsBountyBiome(Heightmap.Biome b)
        {
            foreach (var allowed in BountyBiomes) if (b == allowed) return true;
            return false;
        }

        // Try to find one valid bounty location. Samples uniformly by AREA in the
        // ring [minRadius, maxRadius] around origin (sqrt on the radius — otherwise
        // points bunch up near the centre). Returns false if no candidate passed
        // within the attempt budget; `stats` explains why, for the console command
        // and the log.
        public static bool TrySample(Vector3 origin, float minRadius, float maxRadius,
            out Vector3 pos, out Heightmap.Biome biome, out string stats)
        {
            pos = Vector3.zero;
            biome = Heightmap.Biome.None;

            var wg = WorldGenerator.instance;
            if (wg == null)
            {
                stats = "WorldGenerator unavailable (no world loaded).";
                return false;
            }

            int attempts = Mathf.Max(1, Plugin.BountySampleAttempts?.Value ?? 200);
            float landRadius = Mathf.Max(8f, Plugin.BountyLandCheckRadius?.Value ?? 80f);
            float margin = Mathf.Max(0f, Plugin.BountyWaterMargin?.Value ?? 3f);
            float maxVariance = Mathf.Max(1f, Plugin.BountyMaxHeightVariance?.Value ?? 10f);
            float water = WaterLevel();

            if (maxRadius < minRadius) { var t = minRadius; minRadius = maxRadius; maxRadius = t; }

            int rejBiome = 0, rejWater = 0, rejIsland = 0, rejSteep = 0;

            for (int i = 0; i < attempts; i++)
            {
                float angle = Random.value * Mathf.PI * 2f;
                float r = Mathf.Sqrt(Mathf.Lerp(minRadius * minRadius, maxRadius * maxRadius, Random.value));
                float x = origin.x + Mathf.Cos(angle) * r;
                float z = origin.z + Mathf.Sin(angle) * r;

                var candidateBiome = wg.GetBiome(new Vector3(x, 0f, z));
                if (!IsBountyBiome(candidateBiome)) { rejBiome++; continue; }

                float centreHeight = wg.GetHeight(x, z);
                if (centreHeight <= water + margin) { rejWater++; continue; }

                if (!SurroundedByLand(wg, x, z, landRadius, water + margin, out float minH, out float maxH))
                { rejIsland++; continue; }

                // Reject cliff faces / spires: if the ground within the innermost ring
                // swings wildly the "location" isn't somewhere a fight can happen.
                if (maxH - minH > maxVariance) { rejSteep++; continue; }

                pos = new Vector3(x, centreHeight, z);
                biome = candidateBiome;
                stats = $"found after {i + 1} attempt(s) " +
                        $"(rejected: biome {rejBiome}, water {rejWater}, island/coast {rejIsland}, steep {rejSteep}).";
                return true;
            }

            stats = $"no valid location in {attempts} attempts " +
                    $"(rejected: biome {rejBiome}, water {rejWater}, island/coast {rejIsland}, steep {rejSteep}).";
            return false;
        }

        // Every point on every ring must be dry land, and none may be Ocean. Also
        // reports the height spread of the INNERMOST ring so the caller can reject
        // cliffs (the outer rings are allowed to be hilly — that's just terrain).
        private static bool SurroundedByLand(WorldGenerator wg, float cx, float cz,
            float landRadius, float minHeight, out float innerMin, out float innerMax)
        {
            innerMin = float.MaxValue;
            innerMax = float.MinValue;

            for (int ring = 1; ring <= Rings; ring++)
            {
                float r = landRadius * ring / Rings;
                for (int p = 0; p < RingPoints; p++)
                {
                    float a = Mathf.PI * 2f * p / RingPoints;
                    float x = cx + Mathf.Cos(a) * r;
                    float z = cz + Mathf.Sin(a) * r;

                    if (wg.GetHeight(x, z) <= minHeight) return false;
                    if (wg.GetBiome(new Vector3(x, 0f, z)) == Heightmap.Biome.Ocean) return false;

                    if (ring == 1)
                    {
                        float h = wg.GetHeight(x, z);
                        if (h < innerMin) innerMin = h;
                        if (h > innerMax) innerMax = h;
                    }
                }
            }

            if (innerMin > innerMax) { innerMin = innerMax = 0f; } // no inner samples (shouldn't happen)
            return true;
        }

        // ZoneSystem owns the world's water level; fall back to vanilla's constant
        // (30) if it isn't up yet so sampling still behaves sanely.
        public static float WaterLevel()
            => ZoneSystem.instance != null ? ZoneSystem.instance.m_waterLevel : 30f;

        // Convenience for the console command / later phases: sample several
        // locations at once, skipping failures. Returns how many were produced.
        public static int SampleMany(Vector3 origin, float minRadius, float maxRadius, int count,
            System.Collections.Generic.List<(Vector3 pos, Heightmap.Biome biome)> into, out string report)
        {
            var sb = new StringBuilder();
            int made = 0;
            for (int i = 0; i < count; i++)
            {
                if (TrySample(origin, minRadius, maxRadius, out var pos, out var biome, out var stats))
                {
                    into.Add((pos, biome));
                    made++;
                    sb.AppendLine($"  #{made} {biome} at ({pos.x:F0}, {pos.z:F0}) height {pos.y:F1} — {stats}");
                }
                else
                {
                    sb.AppendLine($"  (failed) {stats}");
                }
            }
            report = sb.ToString();
            return made;
        }
    }
}
