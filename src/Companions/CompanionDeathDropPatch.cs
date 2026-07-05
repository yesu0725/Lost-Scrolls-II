using System.Collections.Generic;
using HarmonyLib;
using LostScrollsII.Companions;
using UnityEngine;

namespace LostScrollsII.Patches
{
    // When a recruited Dvergr dies, spill everything in its pack onto the ground so
    // the owner can recover it (feedback item) — the same courtesy a dropped chest
    // gives. Runs on the companion's ZDO owner only, so the items drop exactly once
    // in multiplayer. Vanilla `ItemDrop.DropItem` spawns the normal world pickups.
    [HarmonyPatch(typeof(Character), nameof(Character.OnDeath))]
    public static class CompanionDeathDropPatch
    {
        public static void Prefix(Character __instance)
        {
            if (__instance == null) return;
            if (__instance.GetComponent<DvergrCompanion>() == null) return;

            var inv = __instance.GetComponent<CompanionInventory>();
            var inventory = inv != null ? inv.Inventory : null;
            if (inventory == null || inventory.NrOfItems() == 0) return;

            // Only the owner of the ZDO spills the pack, so the drops aren't
            // duplicated across clients.
            var nview = __instance.GetComponent<ZNetView>();
            if (nview != null && nview.IsValid() && !nview.IsOwner()) return;

            var pos = __instance.transform.position + Vector3.up * 0.5f;
            foreach (var item in new List<ItemDrop.ItemData>(inventory.GetAllItems()))
            {
                if (item == null) continue;
                // Small scatter so the drops don't all stack on one point.
                var offset = new Vector3(Random.Range(-0.6f, 0.6f), 0f, Random.Range(-0.6f, 0.6f));
                ItemDrop.DropItem(item, item.m_stack, pos + offset, Quaternion.identity);
            }
            inventory.RemoveAll();

            Plugin.Log.LogInfo("[inventory] Companion died — spilled its pack to the ground.");
        }
    }
}
