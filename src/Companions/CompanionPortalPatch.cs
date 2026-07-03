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
        public static void Postfix(Player player)
        {
            // Only when the portal actually teleported this player (a portal with no
            // connected target does nothing), and only for the local client's own
            // player — each client brings its own companions.
            if (player == null || player != Player.m_localPlayer) return;
            if (!player.IsTeleporting()) return;

            CompanionTeleport.FollowOwnerThroughPortal(player);
        }
    }

    internal static class CompanionTeleport
    {
        // Sanity bound: only companions loaded near the owner (i.e. actually
        // following) are brought. Loaded companions are within a couple of zones of
        // the player anyway, so this rarely excludes anything.
        private const float GatherRange = 60f;

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
