using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace LostScrollsII.Companions
{
    // Companion travel through the two portal modes InterServerPortal adds on top
    // of vanilla tag-pairing (E:\Valheim Modding\InterServerPortal).
    //
    // Neither mode goes through the vanilla teleport, so CompanionPortalPatch —
    // which postfixes TeleportWorld.Teleport — never sees them: InterServerPortal
    // prefixes that same method and returns false for any flagged portal, driving
    // the crossing itself. So each mode needs its own hook, and they are genuinely
    // different problems:
    //
    //  * NETWORK mode is a same-world hop. It ends in an ordinary
    //    Player.TeleportTo, so the companions can simply be moved to the exit —
    //    the existing vanilla-portal code path, hung off a different call.
    //
    //  * INTER-SERVER mode leaves the world entirely (Game.Logout -> start scene
    //    -> join another world). A companion is a ZDO in the world being left, so
    //    there is nothing to "move": the only thing that crosses is the player's
    //    character file. The mod already has the answer for that — the Communion
    //    Totem — so a crossing seals each follower into a totem in the pack and
    //    summons them back on arrival. Vanilla assets only, and if the destination
    //    does not run this mod the player simply keeps the totems.
    //
    // Everything here is a SOFT dependency, applied by reflection at startup and
    // silently skipped when InterServerPortal is not installed.
    internal static class InterServerPortalBridge
    {
        private const string NetworkControllerType = "InterServerPortal.Hub.NetworkController";
        private const string WorldSwitcherType = "InterServerPortal.Core.WorldSwitcher";

        public static bool Present { get; private set; }

        public static void ApplyPatches(Harmony harmony)
        {
            if (harmony == null) return;

            var network = AccessTools.TypeByName(NetworkControllerType);
            var switcher = AccessTools.TypeByName(WorldSwitcherType);
            if (network == null && switcher == null)
            {
                Plugin.Log.LogInfo("[portal] InterServerPortal not present — network/inter-server companion transfer inactive.");
                return;
            }

            var self = typeof(InterServerPortalBridge);
            const BindingFlags Any = BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public;

            // Same-world network hop: NetworkController.Travel ends in
            // Player.TeleportTo, exactly like the vanilla paired teleport, so the
            // destination is readable off the player afterwards. Its second
            // parameter is an internal struct, so it is matched by name + arity
            // rather than by signature.
            MethodInfo travel = null;
            if (network != null)
            {
                foreach (var m in network.GetMethods(Any))
                {
                    if (m.Name == "Travel" && m.GetParameters().Length == 2) { travel = m; break; }
                }
            }
            if (travel != null)
            {
                harmony.Patch(travel,
                    prefix: new HarmonyMethod(self.GetMethod(nameof(NetworkTravelPrefix), Any)),
                    postfix: new HarmonyMethod(self.GetMethod(nameof(NetworkTravelPostfix), Any)));
            }
            else if (network != null)
            {
                Plugin.Log.LogWarning("[portal] InterServerPortal found but NetworkController.Travel was not — network-portal companion transfer inactive.");
            }

            // World switch: WorldSwitcher.Leave is the commit point — every
            // validation, the destination check and the Discord notice have already
            // run, and Game.Logout(save: true) happens on the very next line, so a
            // totem added to the pack here is written into the character file that
            // travels with the player.
            var leave = switcher != null ? AccessTools.Method(switcher, "Leave") : null;
            if (leave != null)
            {
                harmony.Patch(leave,
                    prefix: new HarmonyMethod(self.GetMethod(nameof(WorldSwitchLeavePrefix), Any)));
            }
            else if (switcher != null)
            {
                Plugin.Log.LogWarning("[portal] InterServerPortal found but WorldSwitcher.Leave was not — inter-server companion transfer inactive.");
            }

            Present = true;
            Plugin.Log.LogInfo("[portal] InterServerPortal detected — companions follow through network and inter-server portals.");
        }

        // ---- Network mode (same world) ---------------------------------------

        // Same cargo rule as a vanilla wood portal: a follower carrying something
        // non-teleportable blocks the crossing, because it would be dragged
        // through. Portals that allow everything (stone) are unaffected.
        private static bool NetworkTravelPrefix(TeleportWorld source)
        {
            try
            {
                var player = Player.m_localPlayer;
                if (player == null || source == null) return true;
                if (source.m_allowAllItems) return true;

                if (CompanionTeleport.TryFindPortalBlocker(player, out var companionName, out var itemName))
                {
                    player.Message(MessageHud.MessageType.Center,
                        $"{companionName} is carrying {itemName} — you can't take it through the portal.");
                    return false;
                }
            }
            catch (System.Exception e)
            {
                Plugin.Log.LogError($"[portal] Companion cargo check failed: {e}");
            }
            return true;   // never block the player's crossing because WE failed
        }

        private static void NetworkTravelPostfix()
        {
            try
            {
                var player = Player.m_localPlayer;
                // Travel() bails out on its own for a locked/refused crossing, in
                // which case no teleport was started and m_teleportTargetPos still
                // holds a stale destination — IsTeleporting tells the two apart.
                if (player == null || !player.IsTeleporting()) return;
                CompanionTeleport.FollowOwnerThroughPortal(player);
            }
            catch (System.Exception e)
            {
                Plugin.Log.LogError($"[portal] Bringing companions through a network portal failed: {e}");
            }
        }

        // ---- Inter-server mode (different world) -----------------------------

        // Everything here is wrapped: an exception thrown from a Harmony prefix
        // propagates to the caller and the original method never runs, so a fault
        // in OUR sealing would abort InterServerPortal's world switch entirely —
        // which is exactly what a "Collection was modified" bug in Followers() did.
        // A companion left behind is a far better failure than a crossing that
        // silently doesn't happen.
        private static void WorldSwitchLeavePrefix()
        {
            try
            {
                CompanionCrossing.SealFollowersForCrossing(Player.m_localPlayer);
            }
            catch (System.Exception e)
            {
                Plugin.Log.LogError($"[portal] Sealing companions for the crossing failed: {e}");
            }
        }
    }

    // Seals a player's followers into Communion Totems for an inter-server
    // crossing and brings them back on the other side.
    //
    // The pending list is static on purpose: a world switch reloads the scene but
    // stays in the same process, which is the same trick InterServerPortal itself
    // uses to carry its own switch intent across the reload.
    internal static class CompanionCrossing
    {
        // Companion ids sealed for the crossing currently in flight, in the order
        // they were sealed, and when that was — a stale list (a switch that never
        // completed, a session that carried on for hours) must not resurrect
        // companions out of nowhere later.
        private static readonly List<string> _pending = new List<string>();
        private static float _pendingRealtime = -1f;
        private const float PendingTimeoutSeconds = 900f;

        public static bool HasPending => _pending.Count > 0 && _pendingRealtime >= 0f && !Expired;

        private static bool Expired =>
            _pendingRealtime < 0f || Time.realtimeSinceStartup - _pendingRealtime > PendingTimeoutSeconds;

        public static void SealFollowersForCrossing(Player owner)
        {
            _pending.Clear();
            _pendingRealtime = -1f;
            if (owner == null) return;

            var inv = owner.GetInventory();
            if (inv == null) return;

            int stranded = 0;
            foreach (var comp in CompanionTeleport.Followers(owner))
            {
                var totem = TotemConversionService.CreateTotem(comp);
                if (totem == null) { stranded++; continue; }

                // Never drop the totem on the ground here — the ground is in the
                // world the player is about to leave, so a full pack means the
                // companion stays behind (recoverable) rather than the totem being
                // abandoned (not).
                if (!inv.AddItem(totem)) { stranded++; continue; }

                _pending.Add(TotemConversionService.CompanionIdOf(totem) ?? string.Empty);
                // Stamped as each one is added, not after the loop, so a seal that
                // fails part-way still leaves a well-formed (and expirable) pending
                // list for the arrival to summon.
                _pendingRealtime = Time.realtimeSinceStartup;
                TotemConversionService.PlaySealVfx(comp.transform.position + Vector3.up);
                comp.DespawnToTotem();
            }

            if (_pending.Count > 0)
            {
                owner.Message(MessageHud.MessageType.Center,
                    _pending.Count == 1
                        ? "Your companion is sealed into a totem for the crossing."
                        : $"{_pending.Count} companions are sealed into totems for the crossing.");
                Plugin.Log.LogInfo($"[portal] Sealed {_pending.Count} companion(s) into totems for an inter-server crossing.");
            }
            if (stranded > 0)
            {
                Plugin.Log.LogWarning($"[portal] {stranded} companion(s) could not be sealed (no room in the pack) and stay behind.");
                owner.Message(MessageHud.MessageType.Center,
                    "Your pack is full — some companions stay behind.");
            }
        }

        // Summons everything sealed for the crossing back beside the player. Called
        // once the destination world is up (see InterServerArrival).
        public static void SummonPending(Player owner)
        {
            if (owner == null) return;
            if (Expired) { _pending.Clear(); _pendingRealtime = -1f; return; }
            if (_pending.Count == 0) return;

            var inv = owner.GetInventory();
            if (inv == null) return;

            var ids = new List<string>(_pending);
            _pending.Clear();
            _pendingRealtime = -1f;

            int summoned = 0;
            foreach (var id in ids)
            {
                var totem = FindTotem(inv, id);
                if (totem == null) continue;

                var pos = owner.transform.position + SpreadOffset(summoned);
                if (TotemConversionService.SummonAt(owner, totem, pos) == null) continue;
                inv.RemoveItem(totem, 1);
                summoned++;
            }

            if (summoned > 0)
            {
                owner.Message(MessageHud.MessageType.Center,
                    summoned == 1 ? "Your companion steps out of its totem." : $"{summoned} companions step out of their totems.");
                Plugin.Log.LogInfo($"[portal] Summoned {summoned} companion(s) after an inter-server crossing.");
            }
        }

        // Match on the stable ladder id when there is one, so the right totem is
        // opened even when the pack holds several. Legacy totems predate the id and
        // fall back to "the first companion totem in the pack".
        private static ItemDrop.ItemData FindTotem(Inventory inv, string companionId)
        {
            ItemDrop.ItemData fallback = null;
            foreach (var item in inv.GetAllItems())
            {
                if (!TotemConversionService.IsCompanionTotem(item)) continue;
                var id = TotemConversionService.CompanionIdOf(item);
                if (!string.IsNullOrEmpty(companionId) && id == companionId) return item;
                if (fallback == null && string.IsNullOrEmpty(companionId)) fallback = item;
            }
            return fallback;
        }

        private static Vector3 SpreadOffset(int index)
        {
            float angle = 2.399963f * index;      // golden angle, even spacing
            float radius = 1.2f + 0.5f * index;
            return new Vector3(Mathf.Cos(angle) * radius, 0f, Mathf.Sin(angle) * radius);
        }
    }

    // Watches for the local player appearing in the destination world after an
    // inter-server crossing and releases the sealed companions.
    //
    // A poll rather than a patch: the arrival spans a full scene reload and a fresh
    // Player spawn, and this component lives on the plugin GameObject (which BepInEx
    // keeps across scenes), so there is no ordering to get wrong. It costs a null
    // check twice a second and does nothing at all unless a crossing is in flight.
    public class InterServerArrival : MonoBehaviour
    {
        private const float SettleSeconds = 3f;
        private const float PollInterval = 0.5f;
        private float _pollTimer;
        private float _settleTimer = -1f;

        private void Update()
        {
            if (!CompanionCrossing.HasPending) { _settleTimer = -1f; return; }

            _pollTimer += Time.deltaTime;
            if (_pollTimer < PollInterval) return;
            _pollTimer = 0f;

            var player = Player.m_localPlayer;
            if (player == null || ZNetScene.instance == null || ObjectDB.instance == null
                || player.IsTeleporting() || player.IsDead())
            {
                _settleTimer = -1f;
                return;
            }

            // Let the arrival zone finish loading before spawning creatures into it.
            if (_settleTimer < 0f) _settleTimer = 0f;
            _settleTimer += PollInterval;
            if (_settleTimer < SettleSeconds) return;

            _settleTimer = -1f;
            CompanionCrossing.SummonPending(player);
        }
    }
}
