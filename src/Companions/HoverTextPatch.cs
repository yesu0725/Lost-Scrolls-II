using HarmonyLib;
using LostScrollsII.Companions;

namespace LostScrollsII.Patches
{
    // Feature add: visible indicators on hover —
    //   1. A recruited companion's hover text is tagged "(Companion)" plus its
    //      level/XP%, so it's visually obvious it's no longer hostile.
    //   2. A subdued-but-unrecruited Dvergr's hover text gets a
    //      "[<key>] Communion" hint so the recruit action is discoverable
    //      without reading the docs.
    // NEEDS IN-GAME VERIFICATION — Character.GetHoverText()'s exact return
    // shape (single line vs. multi-line, whether it already ends with a
    // newline) is unconfirmed; appending blindly could look wrong even if it
    // doesn't error.
    [HarmonyPatch(typeof(Character), nameof(Character.GetHoverText))]
    public static class HoverTextPatch
    {
        public static void Postfix(Character __instance, ref string __result)
        {
            var companion = __instance.GetComponent<Companions.DvergrCompanion>();
            if (companion != null)
            {
                var progress = companion.IsMaxLevel
                    ? "max"
                    : $"{companion.XpPercentToNextLevel:F0}% to next";
                __result += $"\n<color=yellow>{companion.Caste.Display()}</color> · Lv {companion.Level} ({progress})" +
                    $"\n<color=yellow>[{Plugin.CommunionKey.Value}] Feed</color>";
                return;
            }

            if (Companions.CommunionService.IsSubduedDvergr(__instance))
            {
                __result += $"\n<color=yellow>[{Plugin.CommunionKey.Value}] Communion</color>";
            }
        }
    }
}
