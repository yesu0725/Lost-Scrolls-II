using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using LostScrollsII.Companions;
using LostScrollsII.Integration;
using LostScrollsII.Ranking;
using UnityEngine;

namespace LostScrollsII.Bounty
{
    // Closing a bounty and paying it out (docs/Bounty-Hunting.md, Phase D).
    //
    // A bounty resolves two ways, and BOTH count (requirement 1): the target is
    // killed, or it's freed with the Communion Rite. Escort minions never resolve
    // anything — only the named target does.
    //
    // No loot table lives in this file. Resolution fires a ServerGuide trigger
    // carrying the tier, and a `guidance.bounty-rewards.yaml` entry per tier grants
    // the actual items through ServerGuide's RewardDispatcher — the same "this mod
    // fires events, ServerGuide owns rewards" split the ranking and tournament
    // systems already use.
    //
    // Valcoin is REWARD-ONLY and never leaves this mod as an amount: the chance is
    // rolled here from the player's duel/tournament standing, and on success a second
    // trigger fires whose guidance entry sets the `VC.Q.<id>` player key that Valheim
    // Donations watches. That mod owns the ledger and the payout value, so nothing
    // here can inflate a payout, and nothing here lets Valcoin buy anything back.
    public static class BountyService
    {
        public const string MethodKilled = "killed";
        public const string MethodCommuned = "communed";

        // Bounties already paid out this session, so a double death event or a
        // kill-plus-commune race can't pay twice. Keyed by bounty id.
        private static readonly HashSet<string> _resolved = new HashSet<string>();

        // ---- Entry points ----------------------------------------------------

        // The bounty target died. Called from the OnDeath patch below.
        public static void NotifyKilled(Character dead, Player credit)
            => Resolve(dead, credit, MethodKilled);

        // The bounty target was freed by the Communion Rite instead of killed.
        // Called from CommunionService.TryRecruit's success path.
        public static void NotifyCommuned(Character freed, Player credit)
            => Resolve(freed, credit, MethodCommuned);

        private static void Resolve(Character character, Player credit, string method)
        {
            if (character == null) return;

            var bounty = character.GetComponent<BountyTarget>();
            if (bounty == null) return;

            // Escorts are scenery for the payout — only the named target pays.
            if (bounty.IsMinion) return;

            var id = bounty.BountyId;
            if (string.IsNullOrEmpty(id) || _resolved.Contains(id)) return;
            _resolved.Add(id);

            // The pin's whole job is "the bounty is over there"; it goes the moment
            // that stops being true (requirement 6).
            BountyMapPin.Remove(id);

            int effectiveTier = EffectiveTier(bounty.Tier, credit);
            string tierName = BountyTiers.TierName(effectiveTier);
            string biome = WorldGenerator.instance != null
                ? WorldGenerator.instance.GetBiome(character.transform.position).ToString()
                : string.Empty;

            Plugin.Log.LogInfo($"[bounty] '{id}' resolved by {method} " +
                $"(tier {bounty.Tier} -> effective {effectiveTier}, {biome}) " +
                $"for {(credit != null ? credit.GetPlayerName() : "?")}.");

            // Item rewards: always, authored entirely in guidance YAML.
            ServerGuideBridge.RaiseBountyResolved(effectiveTier, tierName, biome, method);

            // Valcoin: a chance, scaled by the hunter's competitive standing.
            TryAwardValcoin(effectiveTier, tierName, credit);

            // Record it on the bounty ladder. Reported AFTER the reward is decided,
            // so this bounty's own points can't bump the tier it just paid out at.
            // Scored at the BASE tier, not the bonused one — otherwise a high-standing
            // hunter would earn points faster for the same work and run away with the
            // board.
            if (credit != null)
            {
                BountySync.ReportResolution(credit.GetPlayerID(), credit.GetPlayerName(),
                    bounty.Tier, method, id);
            }

            if (credit != null && MessageHud.instance != null && credit == Player.m_localPlayer)
            {
                MessageHud.instance.ShowMessage(MessageHud.MessageType.Center,
                    method == MethodCommuned
                        ? $"The {tierName} bounty is answered — freed, not felled."
                        : $"The {tierName} bounty is answered.");
            }

            // The warden's commission completing is what opens the board (Phase G).
            // Deliberately observed HERE rather than asked of ServerGuide: this mod
            // posted that bounty and sees it resolve, so no reverse-query API is
            // needed and the one-directional bridge stays intact.
            if (credit != null && credit == Player.m_localPlayer && BountyBoardStore.IsTutorial(id))
            {
                BountyQuestGate.Unlock(credit);
                if (MessageHud.instance != null)
                    MessageHud.instance.ShowMessage(MessageHud.MessageType.Center,
                        "The warden will hear of this. The Wanted Board is open to you.");
            }
        }

        // ---- Reward scaling ---------------------------------------------------

        // Requirement 5: a good bounty hunter earns better rewards. Standing on the
        // BOUNTY ladder bumps the effective tier, which is what selects the reward
        // entry — so a better hunter gets a better bundle without any second table.
        private static int EffectiveTier(int baseTier, Player credit)
        {
            int bonus = BountyLeaderboardBonus(credit);
            int cap = Mathf.Max(0, Plugin.BountyMaxTierBonus?.Value ?? 1);
            return BountyTiers.Clamp(baseTier + Mathf.Clamp(bonus, 0, cap));
        }

        // A hunter inside the top N of the bounty ladder gets +1 reward tier. Read off
        // the replicated snapshot, so it works on a client with no round-trip.
        // Unranked hunters (rank 0) get nothing, which is the common case.
        private static int BountyLeaderboardBonus(Player credit)
        {
            if (credit == null) return 0;
            int threshold = Mathf.Max(0, Plugin.BountyLeaderboardBonusRank?.Value ?? 3);
            if (threshold <= 0) return 0;
            int rank = BountyLeaderboardStore.RankOf(credit.GetPlayerID());
            return rank > 0 && rank <= threshold ? 1 : 0;
        }

        // ---- Valcoin (reward-only) -------------------------------------------

        // Requirement 4: the CHANCE of a Valcoin payout rides on the player's duel /
        // tournament standing, which is what pulls bounty hunters toward those
        // systems. Rolled here rather than in YAML because ServerGuide has no numeric
        // rank filter and no random-chance reward.
        private static void TryAwardValcoin(int tier, string tierName, Player credit)
        {
            if (credit == null) return;

            // Without Valheim Donations there is no ledger to credit, and the whole
            // feature is gated on it anyway — but re-check rather than assume, since
            // this is the one path that hands out real currency.
            if (!BountyFeatureGate.DonationsLoaded) return;

            float chance = ValcoinChanceFor(credit, tier);
            if (chance <= 0f) return;

            float roll = Random.value;
            bool won = roll < chance;
            Plugin.Log.LogInfo($"[bounty] valcoin roll for {credit.GetPlayerName()}: " +
                $"{roll:F2} vs chance {chance:F2} (tier {tier}) => {(won ? "PAID" : "no payout")}.");
            if (!won) return;

            ServerGuideBridge.RaiseBountyValcoin(tier, tierName);
        }

        // Base chance, plus a bonus scaled by how high the player stands on either
        // competitive ladder, plus a small per-tier increment. Capped so a payout is
        // never guaranteed.
        public static float ValcoinChanceFor(Player player, int tier)
        {
            float baseChance = Plugin.BountyValcoinBaseChance?.Value ?? 0.10f;
            float perTier = Plugin.BountyValcoinChancePerTier?.Value ?? 0.05f;
            float rankBonus = Plugin.BountyValcoinRankBonus?.Value ?? 0.25f;
            float cap = Plugin.BountyValcoinMaxChance?.Value ?? 0.75f;

            float chance = baseChance + perTier * (BountyTiers.Clamp(tier) - 1);

            // Rank weight: 1.0 at #1, tapering to 0 at RankBonusDepth and below.
            int rank = BestCompetitiveRank(player);
            int depth = Mathf.Max(1, Plugin.BountyValcoinRankDepth?.Value ?? 10);
            if (rank > 0 && rank <= depth)
                chance += rankBonus * (1f - (rank - 1) / (float)depth);

            return Mathf.Clamp(chance, 0f, Mathf.Clamp01(cap));
        }

        // Whether a player may take the elite (top-tier) postings — the second, non-
        // Valcoin incentive to use the duel and tournament systems: rank buys ACCESS
        // here, where the coin roll only changes a chance (docs/Bounty-Hunting.md).
        public static bool IsEliteEligible(long ownerId)
        {
            int topN = Plugin.BountyEliteRankTopN?.Value ?? 10;
            if (topN <= 0) return true; // gate disabled
            int rank = BestCompetitiveRankFor(ownerId);
            return rank > 0 && rank <= topN;
        }

        public static int BestCompetitiveRank(Player player)
            => player == null ? 0 : BestCompetitiveRankFor(player.GetPlayerID());

        // The player's best standing across BOTH competitive ladders — their highest
        // ranked companion on the duel ladder, or their party's rank, whichever is
        // better. 0 = unranked. Reading the replicated snapshot means this works on a
        // client without a server round-trip, and on the server without one either.
        public static int BestCompetitiveRankFor(long ownerId)
        {
            if (ownerId == 0L) return 0;
            int best = 0;

            var ranked = LeaderboardStore.Ranked();
            for (int i = 0; i < ranked.Count; i++)
            {
                if (ranked[i] == null || ranked[i].ownerId != ownerId) continue;
                best = i + 1;
                break; // Ranked() is sorted, so the first match is their best.
            }

            var parties = LeaderboardStore.RankedParties();
            for (int i = 0; i < parties.Count; i++)
            {
                if (parties[i] == null || parties[i].ownerId != ownerId) continue;
                int partyRank = i + 1;
                if (best == 0 || partyRank < best) best = partyRank;
                break;
            }

            return best;
        }

        // Clears the paid-out set — a new session / world starts fresh. (Phase E's
        // persistent store is what will remember completions long-term; this set only
        // guards against double-paying the same live bounty.)
        public static void ResetSession() => _resolved.Clear();
    }

    // A bounty target dying is the ordinary way a bounty closes. Character.OnDeath is
    // the same hook the XP system already uses, so the kill path is proven; this is a
    // separate patch class so neither concern can break the other.
    [HarmonyPatch(typeof(Character), nameof(Character.OnDeath))]
    public static class BountyDeathPatch
    {
        public static void Prefix(Character __instance)
        {
            if (__instance == null || __instance.GetComponent<BountyTarget>() == null) return;
            // Credit the local player: OnDeath runs on the killer's client for a
            // creature it owns, which is the same assumption the kill-XP path makes.
            BountyService.NotifyKilled(__instance, Player.m_localPlayer);
        }
    }
}
