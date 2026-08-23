using HarmonyLib;
using LostScrollsII.Companions;

namespace LostScrollsII.Bounty
{
    // Puts the bounty's tier on its floating name (docs/Bounty-Hunting.md, Phase C).
    //
    // Why a text badge rather than relying on vanilla stars: EnemyHud has only two
    // star rects (m_level2 / m_level3), and vanilla renders level 1 as NO stars at
    // all — so the five bounty tiers collapse into three visuals, two of which show
    // nothing. That's the same wall the companion level badge hit, and this is the
    // same answer: append coloured text to the name EnemyHud already draws.
    //
    // The vanilla stars are deliberately LEFT visible here (unlike on companions,
    // where they're hidden): for a bounty they're accurate — 0-2 stars really does
    // reflect its star level — and they reinforce the danger. The badge carries the
    // tier the stars can't express.
    //
    // Text-only, no custom assets, so this stays inside the vanilla-assets-only rule.
    [HarmonyPatch(typeof(Character), nameof(Character.GetHoverName))]
    public static class BountyNameBadgePatch
    {
        // Warm red for the target, paler for its escort — readable against the
        // world without competing with the companion badge's gold.
        private const string TargetColor = "#FF6B4A";
        private const string EscortColor = "#FFA07A";

        public static void Postfix(Character __instance, ref string __result)
        {
            var bounty = __instance != null ? __instance.GetComponent<BountyTarget>() : null;
            if (bounty == null) return;

            // Freed by the Communion Rite — it's an ally now and wears the companion
            // badge instead. (BountyTarget stands down on recruit but stays attached.)
            if (__instance.GetComponent<DvergrCompanion>() != null) return;

            if (bounty.IsMinion)
            {
                __result += $"  <color={EscortColor}>[Escort]</color>";
                return;
            }

            // Tier number AND name: the numeral makes tiers unambiguous at a glance,
            // the name carries the flavour. ASCII brackets only — the game's serif
            // font renders exotic glyphs as empty boxes (learned the hard way on the
            // discarded Communion progress bar).
            __result += $"  <color={TargetColor}>[T{bounty.Tier} {BountyTiers.TierName(bounty.Tier)} Bounty]</color>";
        }
    }
}
