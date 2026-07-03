namespace LostScrollsII.Companions
{
    // Mirrors docs/Lore.md — the four recruitable Dvergr castes.
    public enum DvergrCaste
    {
        Rogue,
        FireMage,
        IceMage,
        SupportMage
    }

    public static class DvergrCasteExtensions
    {
        // Human-readable caste name for UI/messages (hover text, recruit message,
        // chore-gating prompts). Single source of truth for caste display names.
        public static string Display(this DvergrCaste caste)
        {
            switch (caste)
            {
                case DvergrCaste.FireMage: return "Fire Mage";
                case DvergrCaste.IceMage: return "Ice Mage";
                case DvergrCaste.SupportMage: return "Support Mage";
                default: return "Rogue";
            }
        }
    }
}
