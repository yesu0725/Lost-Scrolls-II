namespace LostScrollsII.Companions
{
    // Caste-gating for chores (docs/Ally-Chores.md). The Smelter, Blast Furnace,
    // Charcoal Kiln, Eitr Refinery and Spinning Wheel all share the vanilla
    // `Smelter` component, so one ChoreAI drives them all — but the approved
    // caste mapping splits them by theme: the heat/smelting stations are the
    // Fire Mage's, the grinding/refining ones are the Ice Mage's. We tell them
    // apart by prefab name (lower-cased, `(Clone)` is harmless to Contains).
    public static class ChoreRules
    {
        // The caste allowed to tend a station, or null if unmapped (any caste).
        //
        // REFINING IS TESTED FIRST. The tokens are substrings of a prefab name, and
        // "smelter" is the loosest of them — any refining station whose prefab
        // happened to contain it would be claimed by the Fire Mage before the Ice
        // Mage's own token was ever looked at. Ordering the specific side first
        // makes the split independent of how the prefabs happen to be named.
        public static DvergrCaste? RequiredCaste(Smelter station)
        {
            var n = station != null ? station.name.ToLowerInvariant() : string.Empty;

            if (n.Contains("eitr") || n.Contains("refinery") || n.Contains("spinning")
                || n.Contains("windmill") || n.Contains("grind"))
                return DvergrCaste.IceMage;     // Refining

            if (n.Contains("smelter") || n.Contains("blast") || n.Contains("furnace")
                || n.Contains("charcoal") || n.Contains("kiln"))
                return DvergrCaste.FireMage;    // Smelting

            return null;
        }

        // One line per distinct station prefab the chore system meets, so a caste
        // that ends up on the wrong station is diagnosable from the log instead of
        // from guessing at substring matches.
        private static readonly System.Collections.Generic.HashSet<string> s_logged =
            new System.Collections.Generic.HashSet<string>();

        public static void LogMapping(Smelter station)
        {
            if (station == null) return;
            var name = station.name;
            if (!s_logged.Add(name)) return;

            var caste = RequiredCaste(station);
            Plugin.Log.LogInfo($"[chore] station '{name}' -> {(caste.HasValue ? caste.Value.ToString() : "any caste")}");
        }

        // The caste allowed to work a chore domain. Smelter-family stations split
        // by theme (see RequiredCaste above); provisioning and the fields are the
        // Support Mage's; the herds are the Rogue's — husbandry is no longer just
        // feeding, it includes culling the surplus, which is butcher's work rather
        // than a mage's (docs/Ally-Chores.md).
        // NULLABLE, and every domain named. Smelter is the one domain with no
        // single answer — the Fire/Ice split is decided per STATION, by
        // RequiredCaste(Smelter) — so it returns null rather than a plausible-looking
        // default. An earlier version fell through `default: SupportMage`, which
        // would have quietly claimed the smelters for the wrong caste the first time
        // anything asked about the domain instead of the station.
        public static DvergrCaste? RequiredCaste(ChoreAI.ChoreKind kind)
        {
            switch (kind)
            {
                case ChoreAI.ChoreKind.Husbandry:
                case ChoreAI.ChoreKind.Haul:
                    return DvergrCaste.Rogue;

                case ChoreAI.ChoreKind.Provisioning:
                case ChoreAI.ChoreKind.Farm:
                    return DvergrCaste.SupportMage;

                case ChoreAI.ChoreKind.Smelter:  // per-station: see RequiredCaste(Smelter)
                default:
                    return null;
            }
        }

        public static string DisplayName(DvergrCaste caste) => caste.Display();
    }
}
