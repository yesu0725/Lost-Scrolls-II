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

        public static void RaiseDuelWon(DvergrCaste winner, DvergrCaste loser)
        {
            if (!IsLoaded) return;
            try { RaiseDuelWonInternal(winner, loser); }
            catch (Exception e) { Plugin.Log.LogWarning($"ServerGuide integration (dvergr_duel_won) failed: {e}"); }
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

        private static void RaiseDuelWonInternal(DvergrCaste winner, DvergrCaste loser)
        {
            ValheimServerGuide.Triggers.GuidanceDispatcher.Raise(new ValheimServerGuide.Triggers.TriggerEvent
            {
                Type = "dvergr_duel_won",
                Subject = winner.ToString(),
                Extra = new System.Collections.Generic.Dictionary<string, object> { { "loserCaste", loser.ToString() } },
            });
        }
    }
}
