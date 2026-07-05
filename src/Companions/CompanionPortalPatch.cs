using HarmonyLib;
using UnityEngine;

namespace LostScrollsII.Companions
{
    // Bring a player's Follow-stance companions along when they step through a
    // portal. The only requirement is that a companion is in the Follow stance and
    // owned by the teleporting player (chore / duel / feral / Guard / Standby allies
    // stay put — they aren't "following" you).
    //
    // Hook: TeleportWorld.Teleport(Player) is what a portal calls when a player uses
    // it; it routes through Player.TeleportTo, which records the destination on the
    // player (m_teleportTargetPos/Rot). A postfix reads that destination and moves
    // the owner's following companions to it. Vanilla assets only — reuses the
    // portal's own teleport, no new prefabs.
    [HarmonyPatch(typeof(TeleportWorld), nameof(TeleportWorld.Teleport))]
    public static class CompanionPortalPatch
    {
        // __runOriginal is false when a prefix (the block below) skipped the vanilla
        // teleport — in that case the player didn't go through, so don't drag
        // companions anywhere.
        public static void Postfix(Player player, bool __runOriginal)
        {
            if (!__runOriginal) return;

            // Only when the portal actually teleported this player (a portal with no
            // connected target does nothing), and only for the local client's own
            // player — each client brings its own companions.
            if (player == null || player != Player.m_localPlayer) return;
            if (!player.IsTeleporting()) return;

            CompanionTeleport.FollowOwnerThroughPortal(player);
        }
    }

    // Blocks a WOOD portal (portal_wood only, per requirement) when one of the
    // player's following companions is carrying a non-teleportable item — even if
    // the player's own inventory is clean — since that companion would be dragged
    // through. Notifies the player which ally is holding what.
    [HarmonyPatch(typeof(TeleportWorld), nameof(TeleportWorld.Teleport))]
    public static class CompanionPortalBlockPatch
    {
        private static float _lastMsg = -999f;

        public static bool Prefix(TeleportWorld __instance, Player player)
        {
            if (player == null || player != Player.m_localPlayer) return true;

            // Scope strictly to the wood portal (its GameObject is "portal_wood(Clone)").
            if (__instance == null ||
                __instance.name.IndexOf("portal_wood", System.StringComparison.OrdinalIgnoreCase) < 0)
                return true;

            if (CompanionTeleport.TryFindPortalBlocker(player, out var companionName, out var itemName))
            {
                // Throttle — Teleport can be retried repeatedly while the player
                // stands in the portal, so don't spam the notification.
                if (Time.time - _lastMsg > 2.5f)
                {
                    _lastMsg = Time.time;
                    player.Message(MessageHud.MessageType.Center,
                        $"{companionName} is carrying {itemName} — you can't take it through the portal.");
                }
                return false; // skip the vanilla teleport
            }
            return true;
        }
    }

    internal static class CompanionTeleport
    {
        // Sanity bound: only companions loaded near the owner (i.e. actually
        // following) are brought. Loaded companions are within a couple of zones of
        // the player anyway, so this rarely excludes anything.
        private const float GatherRange = 60f;

        // Is any of the owner's would-be-teleporting companions (Follow, owned,
        // in range) carrying a non-teleportable item? Returns the first offender's
        // display name + the localized item name so the player can be told exactly
        // what's blocking the portal.
        public static bool TryFindPortalBlocker(Player owner, out string companionName, out string itemName)
        {
            companionName = null;
            itemName = null;
            if (owner == null) return false;
            long ownerId = owner.GetPlayerID();
            var from = owner.transform.position;

            foreach (var comp in DvergrCompanion.All)
            {
                if (comp == null) continue;
                if (comp.Stance != CompanionStance.Follow) continue;
                if (comp.OwnerId == 0L || comp.OwnerId != ownerId) continue;
                if (comp.ChoreActive || comp.DuelMode || comp.IsFeral) continue;

                var ch = comp.GetComponent<Character>();
                if (ch == null || ch.IsDead()) continue;
                if (Vector3.Distance(ch.transform.position, from) > GatherRange) continue;

                var inv = comp.GetComponent<CompanionInventory>();
                var inventory = inv != null ? inv.Inventory : null;
                if (inventory == null) continue;

                foreach (var item in inventory.GetAllItems())
                {
                    if (item?.m_shared == null || item.m_shared.m_teleportable) continue;
                    companionName = comp.DisplayName;
                    itemName = Localization.instance != null
                        ? Localization.instance.Localize(item.m_shared.m_name) : item.m_shared.m_name;
                    return true;
                }
            }
            return false;
        }

        public static void FollowOwnerThroughPortal(Player owner)
        {
            var dest = owner.m_teleportTargetPos;
            var rot = owner.m_teleportTargetRot;
            var from = owner.m_teleportFromPos;
            long ownerId = owner.GetPlayerID();

            int moved = 0;
            foreach (var comp in DvergrCompanion.All)
            {
                if (comp == null) continue;
                if (comp.Stance != CompanionStance.Follow) continue;          // Follow only
                if (comp.OwnerId == 0L || comp.OwnerId != ownerId) continue;   // strictly this player's own
                if (comp.ChoreActive || comp.DuelMode || comp.IsFeral) continue; // not "following" right now

                var ch = comp.GetComponent<Character>();
                if (ch == null || ch.IsDead()) continue;
                if (Vector3.Distance(ch.transform.position, from) > GatherRange) continue;

                TeleportCompanion(ch, dest + SpreadOffset(moved), rot);
                moved++;
            }

            if (moved > 0)
                Plugin.Log.LogInfo($"[portal] Brought {moved} companion(s) through the portal for {owner.GetPlayerName()}.");
        }

        // Move a companion to the destination. Commit the position onto its ZDO
        // (after claiming ownership) as well as the live transform, so it survives
        // the zone change: the old instance unloads, and the ZDO — now parked at the
        // destination — re-instantiates the companion there once that zone loads.
        private static void TeleportCompanion(Character ch, Vector3 pos, Quaternion rot)
        {
            var nview = ch.GetComponent<ZNetView>();
            if (nview != null && nview.IsValid())
            {
                nview.ClaimOwnership();
                nview.GetZDO().SetPosition(pos);
            }

            ch.transform.SetPositionAndRotation(pos, rot);
            if (ch.m_body != null)
            {
                ch.m_body.position = pos;
                ch.m_body.rotation = rot;
            }
        }

        // Spread companions in a small ring around the exit so they don't stack on
        // the player (and on each other) at the destination portal.
        private static Vector3 SpreadOffset(int index)
        {
            float angle = 2.399963f * index;          // golden angle, even spacing
            float radius = 1.2f + 0.5f * index;        // grows gently with count
            return new Vector3(Mathf.Cos(angle) * radius, 0f, Mathf.Sin(angle) * radius);
        }
    }
}
