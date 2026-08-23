using System;
using BepInEx.Bootstrap;
using LostScrollsII.Companions;

namespace LostScrollsII.Bounty
{
    // The single early-out for the whole bounty-hunting feature (docs/Bounty-Hunting.md).
    //
    // Bounty hunting is deliberately SERVER-ONLY and TRIPLE-GATED: it exists only
    // where BiomeLords, ServerGuide and Valheim Donations are all loaded together,
    // and only on the authoritative server/host. Every other bounty type routes its
    // "should I do anything at all?" question through here rather than repeating the
    // checks, so there is exactly one place to reason about (and one place to log).
    //
    // Two different questions, deliberately separate:
    //  - IsEnabled          — am I the authority that runs bounties? (server-side)
    //  - AvailableToLocalPlayer — should the local player see a live board?
    //    A pure client can't answer that itself (it can't see the server's plugin
    //    set), so it uses what the server pushed over BountySync. A client on a
    //    non-qualifying server gets the static teaser panel instead (Phase F).
    public static class BountyFeatureGate
    {
        private const string ServerGuideGuid = "com.valheimserverguide";
        private const string DonationsGuid = "com.taeguk.valheimdonations";

        // -1 unknown, 0 absent, 1 present. The plugin set is fixed for the session,
        // so each probe runs once.
        private static int _serverGuide = -1;
        private static int _donations = -1;

        public static bool ServerGuideLoaded => Probe(ref _serverGuide, ServerGuideGuid, "serverguide");
        public static bool DonationsLoaded => Probe(ref _donations, DonationsGuid, "valheimdonations");

        // BiomeLords is detected by the method that already owns that question for
        // the container-panel hand-off, so the two can never disagree about whether
        // it's loaded. (Its GUID is mixed-case — `com.taeguk.BiomeLords` — which is
        // why a case-insensitive probe is used rather than a plain ContainsKey.)
        public static bool BiomeLordsLoaded => ContainerPanelPositioner.BiomeLordsLoaded();

        public static bool DependenciesPresent =>
            BiomeLordsLoaded && ServerGuideLoaded && DonationsLoaded;

        // True only on the instance that owns bounty state. ZNet may not exist yet
        // (called before a world is loaded), which reads as "not the authority".
        public static bool IsServerAuthority =>
            ZNet.instance != null && ZNet.instance.IsServer();

        private static bool ConfigEnabled =>
            Plugin.BountyEnabled == null || Plugin.BountyEnabled.Value;

        // The authoritative gate: the server runs the bounty system only when every
        // dependency is present, the admin hasn't switched it off, and this instance
        // is actually the server/host.
        public static bool IsEnabled => ConfigEnabled && DependenciesPresent && IsServerAuthority;

        // What the local player's UI should believe. On the server/host that's the
        // real gate; on a pure client it's whatever the server told us (see
        // BountySync). Never assume a client can infer this locally — it can't see
        // the server's plugin list, and its own may differ.
        public static bool AvailableToLocalPlayer =>
            IsServerAuthority ? IsEnabled : BountySync.FeatureActive;

        // Logged once per session, on the first evaluation after ZNet exists, so a
        // "why is there no bounty board?" report can be answered straight from the
        // BepInEx log without adding instrumentation.
        private static bool _logged;
        public static void LogDecisionOnce()
        {
            if (_logged) return;
            _logged = true;
            try
            {
                Plugin.Log.LogInfo($"[bounty] feature gate: {(IsEnabled ? "ON" : "OFF")} " +
                    $"(BiomeLords={BiomeLordsLoaded}, ServerGuide={ServerGuideLoaded}, " +
                    $"Donations={DonationsLoaded}, isServer={IsServerAuthority}, config={ConfigEnabled}). " +
                    "All four must be true for bounty hunting to run.");
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning($"[bounty] gate logging failed: {e.Message}");
            }
        }

        private static bool Probe(ref int cache, string guid, string nameFragment)
        {
            if (cache < 0)
            {
                cache = 0;
                foreach (var kv in Chainloader.PluginInfos)
                {
                    var key = kv.Key ?? string.Empty;
                    var name = kv.Value?.Metadata?.Name ?? string.Empty;
                    if (string.Equals(key, guid, StringComparison.OrdinalIgnoreCase)
                        || key.IndexOf(nameFragment, StringComparison.OrdinalIgnoreCase) >= 0
                        || name.IndexOf(nameFragment, StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        cache = 1;
                        break;
                    }
                }
            }
            return cache == 1;
        }
    }
}
