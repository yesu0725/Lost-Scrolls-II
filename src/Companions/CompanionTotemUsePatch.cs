using HarmonyLib;
using LostScrollsII.Companions;
using UnityEngine;

namespace LostScrollsII.Patches
{
    // Summoning a companion back from its totem (docs/Companion-Totems.md).
    //
    // Humanoid.UseItem is the single entry point for "use this item" — it's what
    // both a hotbar-number press (Player.UseHotbarItem) and the inventory "Use"
    // right-click funnel through, for ANY item type (verified against the
    // assembly). So intercepting it here catches every way a player might use the
    // totem. A companion totem isn't consumable/equippable, so vanilla would just
    // print "$msg_useonwhat"; we take over instead: spawn the companion where the
    // player is looking, consume one totem, and skip the vanilla handling.
    [HarmonyPatch(typeof(Humanoid), nameof(Humanoid.UseItem))]
    public static class CompanionTotemUsePatch
    {
        public static bool Prefix(Humanoid __instance, Inventory inventory, ItemDrop.ItemData item)
        {
            if (__instance != Player.m_localPlayer) return true;
            if (!TotemConversionService.IsCompanionTotem(item)) return true;

            var player = __instance as Player;
            var inv = inventory ?? __instance.m_inventory;
            if (player == null || inv == null || !inv.ContainsItem(item)) return true;

            if (TotemConversionService.TrySummon(player, item))
            {
                inv.RemoveItem(item, 1);
                player.Message(MessageHud.MessageType.Center, "The totem cracks — your companion returns.");
            }
            else
            {
                player.Message(MessageHud.MessageType.Center, "There is no room here to summon your companion.");
            }
            return false; // handled — never fall through to vanilla item use
        }
    }
}
