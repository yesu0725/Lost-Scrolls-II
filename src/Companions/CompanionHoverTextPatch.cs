using HarmonyLib;
using LostScrollsII.Companions;

namespace LostScrollsII.Patches
{
    // Crosshair hover tooltip for a recruited companion. A base creature's
    // Character.GetHoverText returns "" (nothing shows when you look at it), so a
    // postfix here adds the ally's current stance plus the stance/rename key
    // hints. GetHoverText is declared on Character and NOT overridden by Humanoid
    // (verified against the assembly), so a Dvergr resolves to this method.
    //
    // Owner-only: the commands are owner-gated, so a non-owner just sees the
    // floating name/badge (from GetHoverName) and no command hints here.
    [HarmonyPatch(typeof(Character), nameof(Character.GetHoverText))]
    public static class CompanionHoverTextPatch
    {
        public static void Postfix(Character __instance, ref string __result)
        {
            var companion = __instance.GetComponent<DvergrCompanion>();
            if (companion == null) return;
            if (!companion.IsOwner(Player.m_localPlayer)) return;

            string stance =
                companion.Stance == CompanionStance.Guard ? "Guard" :
                companion.Stance == CompanionStance.Standby ? "Standby" : "Follow";

            __result +=
                $"\n<color=#9FD0FF>Stance: {stance}</color>" +
                $"\n<color=yellow>[{Plugin.StanceCycleKey.Value}] Cycle stance</color>" +
                $"\n<color=yellow>[{Plugin.InventoryKey.Value}] Inventory / rename</color>";

            // Dead Raiser sealing (SealingRite). Only advertised once the staff is
            // actually equipped — otherwise every companion would carry a hint for a
            // rite the player has no way to perform. Once it IS equipped, show either
            // the go-ahead or the one thing still missing, so the requirements are
            // discoverable without the docs.
            if (SealingRite.HasStaffEquipped(Player.m_localPlayer))
            {
                if (SealingRite.CanSeal(Player.m_localPlayer, companion, out var why))
                    __result += $"\n<color=#C8A2FF>Hold [Block] — Seal into a totem ({SealingRite.SealSeconds(Player.m_localPlayer):0.0}s)</color>";
                else if (!string.IsNullOrEmpty(why))
                    __result += $"\n<color=#BB8888>Cannot seal: {why}</color>";
            }

            // Only advertise the chore recall while the ally is actually on a chore.
            if (__instance.GetComponent<ChoreAI>()?.IsAssigned == true)
                __result += $"\n<color=yellow>[{Plugin.ChoreAssignKey.Value}] Recall from chore</color>";

            // Setting 4: surface the duel key when a duel is actually possible —
            // i.e. another player's companion is in range to fight. Show the
            // stand-down hint instead while already dueling.
            if (companion.DuelMode)
                __result += $"\n<color=yellow>[{Plugin.DuelSelectKey.Value}] Stand down from duel</color>";
            else if (companion.PartyDuelMode)
                __result += $"\n<color=yellow>[{Plugin.PartyDuelKey.Value}] Stand your party down</color>";
            else if (companion.HasPotentialDuelRivalNearby())
                __result +=
                    $"\n<color=yellow>[{Plugin.DuelSelectKey.Value}] Duel a rival companion nearby</color>" +
                    $"\n<color=yellow>[{Plugin.PartyDuelKey.Value}] Party duel (gather your team)</color>";
        }
    }
}
