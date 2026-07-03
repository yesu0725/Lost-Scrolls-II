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
        public static DvergrCaste? RequiredCaste(Smelter station)
        {
            var n = station != null ? station.name.ToLowerInvariant() : string.Empty;

            if (n.Contains("smelter") || n.Contains("blast") || n.Contains("charcoal"))
                return DvergrCaste.FireMage;   // Smelting

            if (n.Contains("eitr") || n.Contains("spinning") || n.Contains("windmill"))
                return DvergrCaste.IceMage;     // Refining

            return null;
        }

        public static string DisplayName(DvergrCaste caste) => caste.Display();
    }
}
