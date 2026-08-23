using System.Collections.Generic;
using HarmonyLib;
using LostScrollsII.Companions;
using UnityEngine;
using Random = UnityEngine.Random;

namespace LostScrollsII.Bounty
{
    // Spawns a bounty target and its escort (docs/Bounty-Hunting.md, Phase C).
    //
    // Vanilla assets only, per the project's hard constraint: a bounty target is an
    // ordinary Dvergr prefab with new behaviour attached (BountyTarget), exactly as
    // every recruitable Dvergr is. Nothing new is modelled or registered.
    public static class BountySpawner
    {
        // The Dvergr family bounties are drawn from. Weighted by picking the mage
        // castes more often at higher tiers (a mage escort is far more dangerous),
        // but always vanilla prefabs.
        private static readonly string[] MeleePrefabs = { "Dverger" };
        private static readonly string[] MagePrefabs =
            { "DvergerMageFire", "DvergerMageIce", "DvergerMageSupport" };

        public class SpawnedBounty
        {
            public string BountyId;
            public int Tier;
            public Heightmap.Biome Biome;
            public Vector3 Position;
            public GameObject Target;
            public readonly List<GameObject> Minions = new List<GameObject>();
        }

        // Spawn a complete bounty (target + escort) at a position. Returns null if
        // the prefabs can't be resolved or the world isn't ready.
        //
        // NOTE: this instantiates immediately, so the caller must be somewhere the
        // zone is loaded. A server posting a bounty into an unloaded zone is Phase H's
        // problem (the board posts a LOCATION; the creatures are spawned when a hunter
        // gets there) — keeping that split means we never rely on creatures simulating
        // in an unloaded zone, which Valheim doesn't do anyway.
        public static SpawnedBounty Spawn(string bountyId, int tier, Vector3 pos, Heightmap.Biome biome)
        {
            if (ZNetScene.instance == null) return null;
            tier = BountyTiers.Clamp(tier);

            var result = new SpawnedBounty
            {
                BountyId = bountyId,
                Tier = tier,
                Biome = biome,
                Position = pos,
            };

            result.Target = SpawnOne(bountyId, tier, pos, minion: false, tierForPick: tier);
            if (result.Target == null)
            {
                Plugin.Log.LogWarning($"[bounty] could not spawn target for '{bountyId}' — no Dvergr prefab.");
                return null;
            }

            int minions = BountyTiers.MinionCount(tier);
            float ring = Mathf.Max(2f, Plugin.BountyMinionRingRadius?.Value ?? 6f);
            for (int i = 0; i < minions; i++)
            {
                float a = Mathf.PI * 2f * i / Mathf.Max(1, minions) + Random.value * 0.4f;
                var mpos = pos + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * ring;
                mpos.y = GroundHeight(mpos, pos.y);

                var m = SpawnOne(bountyId, BountyTiers.MinionTier(tier), mpos, minion: true, tierForPick: tier);
                if (m != null) result.Minions.Add(m);
            }

            Plugin.Log.LogInfo($"[bounty] spawned '{bountyId}' — {BountyTiers.Describe(tier)} in {biome} " +
                $"at ({pos.x:F0}, {pos.z:F0}); escort {result.Minions.Count}/{minions}.");
            return result;
        }

        private static GameObject SpawnOne(string bountyId, int tier, Vector3 pos, bool minion, int tierForPick)
        {
            var prefab = ZNetScene.instance.GetPrefab(PickPrefab(tierForPick, minion));
            if (prefab == null) prefab = ZNetScene.instance.GetPrefab("Dverger");
            if (prefab == null) return null;

            var go = Object.Instantiate(prefab, pos, Quaternion.Euler(0f, Random.value * 360f, 0f));

            // Stamp the ZDO BEFORE anything reads it: BountyTarget.Awake and the
            // restore patch both derive everything from these fields.
            BountyTarget.Stamp(go, bountyId, minion ? tier : tier, minion);

            // Vanilla star level carries the damage half of the scaling.
            var character = go.GetComponent<Character>();
            if (character != null) character.SetLevel(BountyTiers.StarLevel(tier));

            if (go.GetComponent<BountyTarget>() == null) go.AddComponent<BountyTarget>();
            return go;
        }

        // Higher-tier bounties lead mage escorts more often; tier 1 is plain Dvergr.
        private static string PickPrefab(int tier, bool minion)
        {
            float mageChance = Mathf.Clamp01((BountyTiers.Clamp(tier) - 1) * 0.25f);
            if (minion) mageChance *= 0.6f; // escorts skew melee so a camp isn't all casters
            return Random.value < mageChance
                ? MagePrefabs[Random.Range(0, MagePrefabs.Length)]
                : MeleePrefabs[Random.Range(0, MeleePrefabs.Length)];
        }

        // Put a minion on the ground rather than at the target's exact height —
        // uses the loaded terrain when available, falling back to the target's y.
        private static float GroundHeight(Vector3 p, float fallback)
        {
            if (ZoneSystem.instance != null && ZoneSystem.instance.GetSolidHeight(p, out float h)) return h;
            if (WorldGenerator.instance != null) return WorldGenerator.instance.GetHeight(p.x, p.z);
            return fallback;
        }
    }

    // A bounty creature's behaviour component is runtime-only — only the ZDO fields
    // survive a relog — so every Dvergr that spawns is checked for a bounty stamp and
    // rebuilt, exactly as CompanionRestorePatch rebuilds freed companions.
    [HarmonyPatch(typeof(MonsterAI), nameof(MonsterAI.Start))]
    public static class BountyTargetRestorePatch
    {
        public static void Postfix(MonsterAI __instance)
        {
            var go = __instance != null ? __instance.gameObject : null;
            if (go == null || go.GetComponent<BountyTarget>() != null) return;

            var znv = go.GetComponent<ZNetView>();
            if (znv == null || !znv.IsValid()) return;
            if (string.IsNullOrEmpty(znv.GetZDO().GetString(BountyTarget.ZdoKeyBountyId, null))) return;

            // A bounty creature that was freed by the Communion Rite is an ally now —
            // never re-arm it as a bounty.
            if (go.GetComponent<DvergrCompanion>() != null) return;

            go.AddComponent<BountyTarget>();
        }
    }
}
