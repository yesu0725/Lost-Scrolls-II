using UnityEngine;

namespace LostScrollsII.Bounty
{
    // The bounty difficulty curve (docs/Bounty-Hunting.md, Phase C).
    //
    // This is OUR curve, not BiomeLords'. BiomeLords has a 7-tier HP/damage table,
    // but it is unversioned internal statics with no public API, and it has no
    // pack/minion scaling at all — so calling into it would be fragile and would
    // still leave the minion half unsolved. Instead the shape of its progression is
    // mirrored here (biome order sets the base tier, difficulty compounds per tier)
    // while the numbers live in our own config, where a server admin can tune them.
    //
    // Two levers per tier:
    //  - HEALTH is scaled explicitly on top of the vanilla star level.
    //  - DAMAGE rides on the vanilla star level itself (SetLevel), because vanilla
    //    already scales creature damage per star — including mage spells. Hand-rolling
    //    a damage multiplier would mean patching the attack path, which is a much
    //    bigger risk surface for no real gain.
    public static class BountyTiers
    {
        public const int MinTier = 1;
        public const int MaxTier = 5;

        // Tier 5 is the elite tier — never rolled from a biome alone; it's the
        // rank-gated top posting (Phase H).
        public const int EliteTier = 5;

        // Flavour names used on pins, labels and (later) reward text.
        private static readonly string[] TierNames =
            { "Wanted", "Notorious", "Feared", "Dread", "Accursed" };

        public static string TierName(int tier)
            => TierNames[Mathf.Clamp(tier, MinTier, MaxTier) - 1];

        public static int Clamp(int tier) => Mathf.Clamp(tier, MinTier, MaxTier);

        // Base tier for a biome. Follows the same progression order BiomeLords uses
        // for its own biome tiers, so a bounty in the Plains reads as harder than one
        // in the Black Forest exactly as players already expect from that mod.
        public static int BaseTierFor(Heightmap.Biome biome)
        {
            switch (biome)
            {
                case Heightmap.Biome.BlackForest: return Plugin.BountyTierBlackForest?.Value ?? 1;
                case Heightmap.Biome.Swamp:       return Plugin.BountyTierSwamp?.Value ?? 2;
                case Heightmap.Biome.Mountain:    return Plugin.BountyTierMountain?.Value ?? 3;
                case Heightmap.Biome.Plains:      return Plugin.BountyTierPlains?.Value ?? 4;
                default:                          return MinTier;
            }
        }

        // Health multiplier over the creature's own prefab health, applied AFTER the
        // star level (which SetLevel already applied). Compounding per tier keeps the
        // top tier meaningfully dangerous without making tier 1 unfair.
        public static float HealthMultiplier(int tier)
        {
            float base_ = Plugin.BountyHealthBase?.Value ?? 3f;
            float growth = Plugin.BountyHealthGrowth?.Value ?? 1.6f;
            return base_ * Mathf.Pow(growth, Clamp(tier) - 1);
        }

        // Vanilla star level for the target. Capped low on purpose: stars carry
        // damage scaling, so a high star count makes a target that one-shots players
        // while looking ordinary. The bulk of a tier's difficulty comes from health +
        // minion count instead.
        //
        // Spread linearly across the tier range so the tiers actually differ on
        // screen. Note vanilla shows level 1 as NO stars (level 2 = one star, level 3
        // = two, and EnemyHud has no rect beyond that) — which is exactly why the
        // tier is also shown as a name badge; see BountyNameBadgePatch.
        public static int StarLevel(int tier)
        {
            int max = Mathf.Max(1, Plugin.BountyMaxStarLevel?.Value ?? 3);
            float f = (Clamp(tier) - MinTier) / (float)(MaxTier - MinTier); // 0..1
            // Floor(x + 0.5) rather than RoundToInt: Mathf rounds halves to even, so
            // tier 4 would land on 2 instead of 3.
            return Mathf.Clamp(Mathf.FloorToInt(1f + f * (max - 1) + 0.5f), 1, max);
        }

        // Minions escorting the target — the half of requirement 2 that has no
        // upstream equivalent anywhere, so it's wholly ours.
        public static int MinionCount(int tier)
        {
            int base_ = Plugin.BountyMinionsBase?.Value ?? 1;
            int perTier = Plugin.BountyMinionsPerTier?.Value ?? 1;
            int max = Plugin.BountyMaxMinions?.Value ?? 6;
            return Mathf.Clamp(base_ + (Clamp(tier) - 1) * perTier, 0, Mathf.Max(0, max));
        }

        // Minions are a tier below their master (floored at tier 1) so an escort is
        // a real threat in numbers without each one being a second boss.
        public static int MinionTier(int tier) => Mathf.Max(MinTier, Clamp(tier) - 1);

        // A short human-readable summary, for the console tools and the log.
        public static string Describe(int tier)
            => $"tier {Clamp(tier)} ({TierName(tier)}): " +
               $"{HealthMultiplier(tier):F1}x health, {StarLevel(tier)} star(s), {MinionCount(tier)} minion(s)";
    }
}
