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
                __result += $"\n<color=yellow>Hold [{BlockKeyLabel()}] — Communion</color>";
            }
        }

        // The player's actual Block binding (mouse/key/gamepad) as a display string,
        // so the recruit hint names the real key. GetBoundKeyString returns a
        // localization token (e.g. "$button_mouse1"), and the crosshair hover text
        // is NOT run through Localization (unlike the floating name), so we localize
        // it ourselves. Falls back to the word "Block" if it can't resolve to a
        // clean label — never show a raw "$…" token to the player.
        private static string BlockKeyLabel()
        {
            try
            {
                if (ZInput.instance != null)
                {
                    var s = ZInput.instance.GetBoundKeyString("Block");
                    if (!string.IsNullOrEmpty(s) && Localization.instance != null)
                        s = Localization.instance.Localize(s);
                    if (!string.IsNullOrEmpty(s) && !s.Contains("$")) return s;
                }
            }
            catch { }
            return "Block";
        }
    }
}
