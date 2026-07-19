using System;
using BepInEx.Bootstrap;
using LostScrollsII.Companions;

namespace LostScrollsII.Integration
{
    // Optional integration with the sibling ValheimServerGuide mod — see
    // docs/ServerGuide-Integration.md. All calls are no-ops if ServerGuide isn't
    // loaded, so LostScrollsII's own mechanics never depend on it being present.
    //
    // Calls into ServerGuide.Triggers.GuidanceDispatcher are isolated in their own
    // methods (RaiseRecruitedInternal etc.) and only invoked after IsLoaded is
    // confirmed true — .NET JITs a method's IL lazily, so as long as those internal
    // methods are never called when the ServerGuide assembly is absent, referencing
    // its types here doesn't throw even on a machine without ServerGuide installed.
    public static class ServerGuideBridge
    {
        private const string ServerGuideGuid = "com.valheimserverguide";

        public static bool IsLoaded => Chainloader.PluginInfos.ContainsKey(ServerGuideGuid);

        public static void RaiseRecruited(DvergrCaste caste)
        {
            if (!IsLoaded) return;
            try { RaiseRecruitedInternal(caste); }
            catch (Exception e) { Plugin.Log.LogWarning($"ServerGuide integration (dvergr_recruited) failed: {e}"); }
        }

        public static void RaiseLevelUp(DvergrCaste caste, int level)
        {
            if (!IsLoaded) return;
            try { RaiseLevelUpInternal(caste, level); }
            catch (Exception e) { Plugin.Log.LogWarning($"ServerGuide integration (dvergr_level_up) failed: {e}"); }
        }

        public static void RaiseDuelWon(DvergrCaste winner, DvergrCaste loser,
            string winnerName = null, string loserName = null)
        {
            if (!IsLoaded) return;
            try { RaiseDuelWonInternal(winner, loser, winnerName, loserName); }
            catch (Exception e) { Plugin.Log.LogWarning($"ServerGuide integration (dvergr_duel_won) failed: {e}"); }
        }

        // Fired on the winner's client when a companion climbs into the top ranks of
        // the duel ladder (docs/Ranking.md). Subject = winner caste; Extra carries
        // rank/rating/companion/owner for templating and reward gating.
        public static void RaiseRankChanged(int caste, int rank, int rating, string companionName, string ownerName)
        {
            if (!IsLoaded) return;
            try { RaiseRankChangedInternal(caste, rank, rating, companionName, ownerName); }
            catch (Exception e) { Plugin.Log.LogWarning($"ServerGuide integration (dvergr_rank_changed) failed: {e}"); }
        }

        // Fired on the winner's client the moment a companion reaches #1 on the duel
        // ladder (docs/Ranking.md). Distinct from dvergr_rank_changed (top-3) so a
        // guidance entry can attach a "new champion" Discord announcement without a
        // numeric filter. Same Extra fields as RaiseRankChanged.
        public static void RaiseRankFirst(int caste, int rating, string companionName, string ownerName)
        {
            if (!IsLoaded) return;
            try
            {
                RaiseSimple("dvergr_rank_first", ((DvergrCaste)caste).ToString(),
                    new System.Collections.Generic.Dictionary<string, object>
                    {
                        { "rank", 1 },
                        { "rating", rating },
                        { "companionName", companionName ?? string.Empty },
                        { "ownerName", ownerName ?? string.Empty },
                    });
            }
            catch (Exception e) { Plugin.Log.LogWarning($"ServerGuide integration (dvergr_rank_first) failed: {e}"); }
        }

        // Fired on the winner's client the moment a party reaches #1 on the party
        // ladder. Carries the party/owner name for templating.
        public static void RaisePartyRankFirst(string ownerName, int rating, string partyName)
        {
            if (!IsLoaded) return;
            try
            {
                RaiseSimple("dvergr_party_rank_first", ownerName ?? string.Empty,
                    new System.Collections.Generic.Dictionary<string, object>
                    {
                        { "rank", 1 },
                        { "rating", rating },
                        { "ownerName", ownerName ?? string.Empty },
                        { "partyName", string.IsNullOrEmpty(partyName) ? (ownerName ?? string.Empty) : partyName },
                    });
            }
            catch (Exception e) { Plugin.Log.LogWarning($"ServerGuide integration (dvergr_party_rank_first) failed: {e}"); }
        }

        // Fired on the winner's client when a party duel resolves (docs/Party-Duels.md).
        // Subject = winning owner name; Extra carries team size / opponent / MVP.
        public static void RaisePartyDuelWon(string winnerOwnerName, int winSize, string loserOwnerName,
            int mvpCaste, string partyName = null)
        {
            if (!IsLoaded) return;
            try { RaisePartyDuelWonInternal(winnerOwnerName, winSize, loserOwnerName, mvpCaste, partyName); }
            catch (Exception e) { Plugin.Log.LogWarning($"ServerGuide integration (dvergr_party_duel_won) failed: {e}"); }
        }

        // Fired on the winner's client when a party climbs into the top party ranks.
        public static void RaisePartyRankChanged(string ownerName, int rank, int rating, string partyName = null)
        {
            if (!IsLoaded) return;
            try { RaisePartyRankChangedInternal(ownerName, rank, rating, partyName); }
            catch (Exception e) { Plugin.Log.LogWarning($"ServerGuide integration (dvergr_party_rank_changed) failed: {e}"); }
        }

        // Tournament triggers (docs/Tournaments.md). Subject = caste name (1v1) or
        // "party". Fired on the relevant player's client so any reward lands there.
        public static void RaiseTournamentJoined(string subject)
        {
            if (!IsLoaded) return;
            try { RaiseSimple("dvergr_tournament_joined", subject, null); }
            catch (Exception e) { Plugin.Log.LogWarning($"ServerGuide integration (dvergr_tournament_joined) failed: {e}"); }
        }

        public static void RaiseTournamentMatch(string subject, int round, string opponent)
        {
            if (!IsLoaded) return;
            try
            {
                RaiseSimple("dvergr_tournament_match", subject, new System.Collections.Generic.Dictionary<string, object>
                {
                    { "round", round }, { "opponent", opponent ?? string.Empty },
                });
            }
            catch (Exception e) { Plugin.Log.LogWarning($"ServerGuide integration (dvergr_tournament_match) failed: {e}"); }
        }

        public static void RaiseTournamentWon(string subject, string mode, int bracketSize)
        {
            if (!IsLoaded) return;
            try
            {
                RaiseSimple("dvergr_tournament_won", subject, new System.Collections.Generic.Dictionary<string, object>
                {
                    { "mode", mode ?? string.Empty }, { "bracketSize", bracketSize },
                });
            }
            catch (Exception e) { Plugin.Log.LogWarning($"ServerGuide integration (dvergr_tournament_won) failed: {e}"); }
        }

        private static void RaiseRecruitedInternal(DvergrCaste caste)
        {
            ValheimServerGuide.Triggers.GuidanceDispatcher.Raise(new ValheimServerGuide.Triggers.TriggerEvent
            {
                Type = "dvergr_recruited",
                Subject = caste.ToString(),
            });
        }

        private static void RaiseLevelUpInternal(DvergrCaste caste, int level)
        {
            ValheimServerGuide.Triggers.GuidanceDispatcher.Raise(new ValheimServerGuide.Triggers.TriggerEvent
            {
                Type = "dvergr_level_up",
                Subject = $"{caste}:{level}",
            });
        }

        private static void RaiseDuelWonInternal(DvergrCaste winner, DvergrCaste loser,
            string winnerName, string loserName)
        {
            ValheimServerGuide.Triggers.GuidanceDispatcher.Raise(new ValheimServerGuide.Triggers.TriggerEvent
            {
                Type = "dvergr_duel_won",
                Subject = winner.ToString(),
                Extra = new System.Collections.Generic.Dictionary<string, object>
                {
                    { "loserCaste", loser.ToString() },
                    // Reuse the {companionName}/{opponent} templating vars so a duel-won
                    // guidance entry (incl. a Discord broadcast) can name both duelists.
                    { "companionName", winnerName ?? string.Empty },
                    { "opponent", loserName ?? string.Empty },
                },
            });
        }

        private static void RaiseRankChangedInternal(int caste, int rank, int rating, string companionName, string ownerName)
        {
            ValheimServerGuide.Triggers.GuidanceDispatcher.Raise(new ValheimServerGuide.Triggers.TriggerEvent
            {
                Type = "dvergr_rank_changed",
                Subject = ((DvergrCaste)caste).ToString(),
                Extra = new System.Collections.Generic.Dictionary<string, object>
                {
                    { "rank", rank },
                    { "rating", rating },
                    { "companionName", companionName ?? string.Empty },
                    { "ownerName", ownerName ?? string.Empty },
                },
            });
        }

        private static void RaisePartyDuelWonInternal(string winnerOwnerName, int winSize, string loserOwnerName,
            int mvpCaste, string partyName)
        {
            ValheimServerGuide.Triggers.GuidanceDispatcher.Raise(new ValheimServerGuide.Triggers.TriggerEvent
            {
                Type = "dvergr_party_duel_won",
                Subject = winnerOwnerName ?? string.Empty,
                Extra = new System.Collections.Generic.Dictionary<string, object>
                {
                    { "winSize", winSize },
                    { "ownerName", winnerOwnerName ?? string.Empty },
                    { "opponentOwner", loserOwnerName ?? string.Empty },
                    { "mvpCaste", ((DvergrCaste)mvpCaste).ToString() },
                    { "partyName", string.IsNullOrEmpty(partyName) ? (winnerOwnerName ?? string.Empty) : partyName },
                },
            });
        }

        // Generic raise used by the tournament triggers (and any future type-only
        // trigger). Kept internal like the others so the ServerGuide type reference
        // is only JIT'd when ServerGuide is actually present.
        private static void RaiseSimple(string type, string subject, System.Collections.Generic.Dictionary<string, object> extra)
        {
            ValheimServerGuide.Triggers.GuidanceDispatcher.Raise(new ValheimServerGuide.Triggers.TriggerEvent
            {
                Type = type,
                Subject = subject ?? string.Empty,
                Extra = extra,
            });
        }

        private static void RaisePartyRankChangedInternal(string ownerName, int rank, int rating, string partyName)
        {
            ValheimServerGuide.Triggers.GuidanceDispatcher.Raise(new ValheimServerGuide.Triggers.TriggerEvent
            {
                Type = "dvergr_party_rank_changed",
                Subject = ownerName ?? string.Empty,
                Extra = new System.Collections.Generic.Dictionary<string, object>
                {
                    { "rank", rank },
                    { "rating", rating },
                    { "ownerName", ownerName ?? string.Empty },
                    { "partyName", string.IsNullOrEmpty(partyName) ? (ownerName ?? string.Empty) : partyName },
                },
            });
        }
    }
}
