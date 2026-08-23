using HarmonyLib;
using UnityEngine;

namespace LostScrollsII.Bounty
{
    // Server <-> client sync for bounty hunting (docs/Bounty-Hunting.md, Phase A).
    // Mirrors LeaderboardSync/GuidanceSync: RPCs bind on ZNet.Awake, the SERVER is
    // the authority, and clients only ever receive what it pushes.
    //
    // Phase A carries exactly one fact: whether the server is running the bounty
    // feature at all. A client cannot work this out for itself — the triple
    // dependency gate is about the SERVER's plugin set, not the client's — so the
    // server states it, and the client's UI (Phase F) renders either the live board
    // or the static teaser off this flag.
    //
    // Flow:
    //  - Server pushes the flag to each peer as it joins (RPC_PeerInfo).
    //  - Each client also asks on spawn (initial + respawn / reconnect), so a client
    //    that somehow missed the join push still converges.
    public static class BountySync
    {
        private const string RpcBountyActive = "LSII_BountyActive";  // server->client: feature on/off
        private const string RpcBountyRequest = "LSII_BountyReq";    // client->server: tell me again
        private const string RpcBountyReport = "LSII_BountyRep";     // client->server: I answered a bounty
        private const string RpcBountyPush = "LSII_BountyPush";      // server->all: the hunter ladder
        private const string RpcBountyLadderReq = "LSII_BountyLadReq"; // client->server: send the ladder
        private const string RpcBoardPush = "LSII_BoardPush";        // server->all: the Wanted Board
        private const string RpcBoardReq = "LSII_BoardReq";          // client->server: send the board
        private const string RpcBoardAccept = "LSII_BoardAccept";    // client->server: I'll take that one
        private const string RpcBoardAbandon = "LSII_BoardAband";    // client->server: drop my posting
        private const string RpcBoardSpawned = "LSII_BoardSpawned";  // client->server: creatures are up
        private const string RpcBoardAck = "LSII_BoardAck";          // server->client: status line
        private const string RpcBoardCommission = "LSII_BoardComm";  // client->server: the warden sent me

        // What the server says about bounty hunting. Meaningful on clients only —
        // the server/host answers from BountyFeatureGate.IsEnabled directly. Defaults
        // to false so a client shows the teaser until told otherwise, rather than
        // flashing a live board it may not be entitled to.
        public static bool FeatureActive { get; private set; }

        private static bool _bound;

        private static void EnsureRegistered()
        {
            if (_bound) return;
            if (ZRoutedRpc.instance == null) return;
            ZRoutedRpc.instance.Register<string>(RpcBountyActive, OnBountyActive);
            ZRoutedRpc.instance.Register<string>(RpcBountyRequest, OnBountyRequest);
            ZRoutedRpc.instance.Register<string>(RpcBountyReport, OnBountyReport);
            ZRoutedRpc.instance.Register<ZPackage>(RpcBountyPush, OnBountyPush);
            ZRoutedRpc.instance.Register<string>(RpcBountyLadderReq, OnBountyLadderRequest);
            ZRoutedRpc.instance.Register<ZPackage>(RpcBoardPush, OnBoardPush);
            ZRoutedRpc.instance.Register<string>(RpcBoardReq, OnBoardRequest);
            ZRoutedRpc.instance.Register<string>(RpcBoardAccept, OnBoardAccept);
            ZRoutedRpc.instance.Register<string>(RpcBoardAbandon, OnBoardAbandon);
            ZRoutedRpc.instance.Register<string>(RpcBoardSpawned, OnBoardSpawned);
            ZRoutedRpc.instance.Register<string>(RpcBoardAck, OnBoardAck);
            ZRoutedRpc.instance.Register<string>(RpcBoardCommission, OnBoardCommission);
            _bound = true;
            Plugin.Log.LogInfo("[bounty] RPCs registered.");
        }

        // ---- Push the feature flag (server -> clients) ------------------------

        public static void SendFeatureStateToPeer(long peer)
        {
            if (ZRoutedRpc.instance == null) return;
            if (!BountyFeatureGate.IsServerAuthority) return;
            ZRoutedRpc.instance.InvokeRoutedRPC(peer, RpcBountyActive,
                BountyFeatureGate.IsEnabled ? "1" : "0");
        }

        public static void BroadcastFeatureState()
        {
            if (ZRoutedRpc.instance == null) return;
            if (!BountyFeatureGate.IsServerAuthority) return;
            ZRoutedRpc.instance.InvokeRoutedRPC(0L, RpcBountyActive,
                BountyFeatureGate.IsEnabled ? "1" : "0");
        }

        private static void OnBountyActive(long sender, string value)
        {
            // The server is the authority and already knows; ignore any echo.
            if (BountyFeatureGate.IsServerAuthority) return;
            bool active = value == "1";
            if (active == FeatureActive) return;
            FeatureActive = active;
            Plugin.Log.LogInfo($"[bounty] server reports bounty hunting {(active ? "ACTIVE" : "unavailable")}.");
        }

        // ---- Request the flag (client -> server, on spawn) --------------------

        public static void RequestFeatureState()
        {
            if (ZRoutedRpc.instance == null) return;
            if (BountyFeatureGate.IsServerAuthority) return; // host already knows
            ZRoutedRpc.instance.InvokeRoutedRPC(
                ZRoutedRpc.instance.GetServerPeerID(), RpcBountyRequest, string.Empty);
        }

        private static void OnBountyRequest(long sender, string _)
        {
            if (!BountyFeatureGate.IsServerAuthority) return;
            SendFeatureStateToPeer(sender);
        }

        // ---- Report an answered bounty (client -> server) --------------------
        //
        // Resolution happens on the hunter's own client (that's where the kill and
        // the Communion Rite are observed), but the LADDER is server-authoritative,
        // so the client reports and the server decides — a modded client can't write
        // its own standing.

        public static void ReportResolution(long ownerId, string ownerName, int tier, string method,
            string bountyId = null)
        {
            if (ZRoutedRpc.instance == null) return;
            string S(string s) => (s ?? string.Empty).Replace('|', '/');
            var payload = string.Join("|", new[]
            {
                ownerId.ToString(), S(ownerName), tier.ToString(), S(method), S(bountyId),
            });

            if (BountyFeatureGate.IsServerAuthority) { ApplyReport(payload); return; }
            ZRoutedRpc.instance.InvokeRoutedRPC(
                ZRoutedRpc.instance.GetServerPeerID(), RpcBountyReport, payload);
        }

        private static void OnBountyReport(long sender, string payload) => ApplyReport(payload);

        private static void ApplyReport(string payload)
        {
            if (!BountyFeatureGate.IsServerAuthority) return;
            // The feature gate is re-checked here rather than trusted from the client:
            // this is the path that writes persistent standings.
            if (!BountyFeatureGate.IsEnabled) return;

            var p = (payload ?? string.Empty).Split('|');
            if (p.Length < 4) return;
            long.TryParse(p[0], out var ownerId);
            int.TryParse(p[2], out var tier);

            if (!BountyLeaderboardStore.ApplyResolution(ownerId, p[1], tier, p[3],
                    out var rank, out var points))
            {
                return;
            }

            Plugin.Log.LogInfo($"[bounty] ladder: '{p[1]}' answered a tier {tier} bounty ({p[3]}) " +
                $"— {points} points, rank #{rank}.");
            BroadcastLadder();

            // An answered posting leaves the board for good; the next EnsurePostings
            // tick replaces it. Done server-side off the same report, so a client
            // can't clear a posting it didn't finish.
            if (p.Length > 4 && !string.IsNullOrEmpty(p[4]) && BountyBoardStore.Remove(p[4]))
                BroadcastBoard();
        }

        // ---- Push the hunter ladder (server -> clients) ----------------------

        public static void BroadcastLadder()
        {
            if (ZRoutedRpc.instance == null || !BountyFeatureGate.IsServerAuthority) return;
            var pkg = new ZPackage();
            pkg.Write(BountyLeaderboardStore.SerializeSnapshot());
            ZRoutedRpc.instance.InvokeRoutedRPC(0L, RpcBountyPush, pkg);
        }

        public static void SendLadderToPeer(long peer)
        {
            if (ZRoutedRpc.instance == null || !BountyFeatureGate.IsServerAuthority) return;
            var pkg = new ZPackage();
            pkg.Write(BountyLeaderboardStore.SerializeSnapshot());
            ZRoutedRpc.instance.InvokeRoutedRPC(peer, RpcBountyPush, pkg);
        }

        private static void OnBountyPush(long sender, ZPackage pkg)
        {
            if (BountyFeatureGate.IsServerAuthority) return; // host already has the live table
            BountyLeaderboardStore.ApplySnapshot(pkg.ReadString());
        }

        public static void RequestLadder()
        {
            if (ZRoutedRpc.instance == null || BountyFeatureGate.IsServerAuthority) return;
            ZRoutedRpc.instance.InvokeRoutedRPC(
                ZRoutedRpc.instance.GetServerPeerID(), RpcBountyLadderReq, string.Empty);
        }

        private static void OnBountyLadderRequest(long sender, string _)
        {
            if (!BountyFeatureGate.IsServerAuthority) return;
            SendLadderToPeer(sender);
        }

        // ---- The Wanted Board (docs/Bounty-Hunting.md, Phase F) ---------------
        //
        // The server owns the postings; a client can only ASK to accept one. Doing it
        // the other way round would let a modded client take a posting another player
        // already holds, or hand itself an unlimited number.

        public static void BroadcastBoard()
        {
            if (ZRoutedRpc.instance == null || !BountyFeatureGate.IsServerAuthority) return;
            var pkg = new ZPackage();
            pkg.Write(BountyBoardStore.SerializeSnapshot());
            ZRoutedRpc.instance.InvokeRoutedRPC(0L, RpcBoardPush, pkg);
        }

        public static void SendBoardToPeer(long peer)
        {
            if (ZRoutedRpc.instance == null || !BountyFeatureGate.IsServerAuthority) return;
            var pkg = new ZPackage();
            pkg.Write(BountyBoardStore.SerializeSnapshot());
            ZRoutedRpc.instance.InvokeRoutedRPC(peer, RpcBoardPush, pkg);
        }

        private static void OnBoardPush(long sender, ZPackage pkg)
        {
            if (BountyFeatureGate.IsServerAuthority) return;
            BountyBoardStore.ApplySnapshot(pkg.ReadString());
        }

        public static void RequestBoard()
        {
            if (ZRoutedRpc.instance == null || BountyFeatureGate.IsServerAuthority) return;
            ZRoutedRpc.instance.InvokeRoutedRPC(
                ZRoutedRpc.instance.GetServerPeerID(), RpcBoardReq, string.Empty);
        }

        private static void OnBoardRequest(long sender, string _)
        {
            if (!BountyFeatureGate.IsServerAuthority) return;
            SendBoardToPeer(sender);
        }

        public static void SendAccept(string postingId)
        {
            var lp = Player.m_localPlayer;
            if (lp == null || ZRoutedRpc.instance == null) return;
            string S(string s) => (s ?? string.Empty).Replace('|', '/');
            var payload = string.Join("|", new[]
            {
                lp.GetPlayerID().ToString(), S(lp.GetPlayerName()), S(postingId),
            });
            if (BountyFeatureGate.IsServerAuthority) { ShowLocal(ApplyAccept(payload)); return; }
            ZRoutedRpc.instance.InvokeRoutedRPC(
                ZRoutedRpc.instance.GetServerPeerID(), RpcBoardAccept, payload);
        }

        private static void OnBoardAccept(long sender, string payload)
        {
            if (!BountyFeatureGate.IsServerAuthority) return;
            var status = ApplyAccept(payload);
            if (!string.IsNullOrEmpty(status))
                ZRoutedRpc.instance.InvokeRoutedRPC(sender, RpcBoardAck, status);
        }

        private static string ApplyAccept(string payload)
        {
            if (!BountyFeatureGate.IsEnabled) return "Bounty hunting isn't active here.";
            var p = (payload ?? string.Empty).Split('|');
            if (p.Length < 3) return null;
            long.TryParse(p[0], out var ownerId);
            var status = BountyBoardStore.Accept(ownerId, p[1], p[2]);
            BroadcastBoard();
            return status;
        }

        public static void SendAbandon()
        {
            var lp = Player.m_localPlayer;
            if (lp == null || ZRoutedRpc.instance == null) return;
            var payload = lp.GetPlayerID().ToString();
            if (BountyFeatureGate.IsServerAuthority) { ShowLocal(ApplyAbandon(payload)); return; }
            ZRoutedRpc.instance.InvokeRoutedRPC(
                ZRoutedRpc.instance.GetServerPeerID(), RpcBoardAbandon, payload);
        }

        private static void OnBoardAbandon(long sender, string payload)
        {
            if (!BountyFeatureGate.IsServerAuthority) return;
            var status = ApplyAbandon(payload);
            if (!string.IsNullOrEmpty(status))
                ZRoutedRpc.instance.InvokeRoutedRPC(sender, RpcBoardAck, status);
        }

        private static string ApplyAbandon(string payload)
        {
            long.TryParse(payload, out var ownerId);
            var status = BountyBoardStore.Abandon(ownerId);
            BroadcastBoard();
            return status;
        }

        // The hunter's client instantiates the creatures when it arrives (Valheim
        // doesn't simulate unloaded zones, so only a client that's actually there
        // can). It tells the server so the board records it and a second arrival
        // doesn't spawn a duplicate camp.
        public static void SendSpawned(string postingId)
        {
            if (ZRoutedRpc.instance == null) return;
            if (BountyFeatureGate.IsServerAuthority) { ApplySpawned(postingId); return; }
            ZRoutedRpc.instance.InvokeRoutedRPC(
                ZRoutedRpc.instance.GetServerPeerID(), RpcBoardSpawned, postingId ?? string.Empty);
        }

        private static void OnBoardSpawned(long sender, string postingId) => ApplySpawned(postingId);

        private static void ApplySpawned(string postingId)
        {
            if (!BountyFeatureGate.IsServerAuthority) return;
            BountyBoardStore.MarkSpawned(postingId);
            BroadcastBoard();
        }

        // The Haldor dialogue granted the commission (Phase G). The client asks the
        // server for its first posting, passing where it is so the posting lands
        // somewhere it can actually walk to.
        public static void SendCommissionRequest(Vector3 near)
        {
            var lp = Player.m_localPlayer;
            if (lp == null || ZRoutedRpc.instance == null) return;
            string S(string s) => (s ?? string.Empty).Replace('|', '/');
            var payload = string.Join("|", new[]
            {
                lp.GetPlayerID().ToString(), S(lp.GetPlayerName()),
                near.x.ToString(System.Globalization.CultureInfo.InvariantCulture),
                near.z.ToString(System.Globalization.CultureInfo.InvariantCulture),
            });
            if (BountyFeatureGate.IsServerAuthority) { ShowLocal(ApplyCommission(payload)); return; }
            ZRoutedRpc.instance.InvokeRoutedRPC(
                ZRoutedRpc.instance.GetServerPeerID(), RpcBoardCommission, payload);
        }

        private static void OnBoardCommission(long sender, string payload)
        {
            if (!BountyFeatureGate.IsServerAuthority) return;
            var status = ApplyCommission(payload);
            if (!string.IsNullOrEmpty(status))
                ZRoutedRpc.instance.InvokeRoutedRPC(sender, RpcBoardAck, status);
        }

        private static string ApplyCommission(string payload)
        {
            if (!BountyFeatureGate.IsEnabled) return "Bounty hunting isn't active here.";
            var p = (payload ?? string.Empty).Split('|');
            if (p.Length < 4) return null;
            long.TryParse(p[0], out var ownerId);
            float.TryParse(p[2], System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out float x);
            float.TryParse(p[3], System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out float z);

            var status = BountyBoardStore.CreateTutorial(ownerId, p[1], new Vector3(x, 0f, z), out _);
            BroadcastBoard();
            return status;
        }

        private static void OnBoardAck(long sender, string msg) => ShowLocal(msg);

        private static void ShowLocal(string msg)
        {
            if (!string.IsNullOrEmpty(msg) && MessageHud.instance != null)
                MessageHud.instance.ShowMessage(MessageHud.MessageType.Center, msg);
        }

        // ---- Lifecycle (mirrors LeaderboardSync) -----------------------------

        [HarmonyPatch(typeof(ZNet), nameof(ZNet.Awake))]
        private static class ZNetAwakePatch
        {
            private static void Postfix(ZNet __instance)
            {
                EnsureRegistered();

                // Entering a world starts with nothing paid out yet. Cleared on EVERY
                // instance, not just the server: resolution is observed client-side, so
                // the client's own latch is what actually prevents a double payout, and
                // it would otherwise grow for the life of the process.
                BountyService.ResetSession();

                // ZNet now exists, so the server half of the gate can be evaluated —
                // this is the first point where the decision is meaningful to log.
                if (!__instance.IsServer()) return;
                BountyFeatureGate.LogDecisionOnce();
                BountyLeaderboardStore.LoadForCurrentWorld();
                BountyBoardStore.LoadForCurrentWorld();
            }
        }

        [HarmonyPatch(typeof(ZNet), nameof(ZNet.OnDestroy))]
        private static class ZNetOnDestroyPatch
        {
            private static void Postfix()
            {
                _bound = false;
                // Leaving a world clears what that server told us, so a client can't
                // carry a stale "bounties are live" into the next server it joins.
                FeatureActive = false;
            }
        }

        // Server tells each joining peer whether the feature is live.
        [HarmonyPatch(typeof(ZNet), nameof(ZNet.RPC_PeerInfo))]
        private static class PeerInfoPatch
        {
            private static void Postfix(ZNet __instance, ZRpc rpc)
            {
                if (!__instance.IsServer()) return;
                var peer = __instance.GetPeer(rpc);
                if (peer == null) return;
                EnsureRegistered();
                SendFeatureStateToPeer(peer.m_uid);
                SendLadderToPeer(peer.m_uid);
                SendBoardToPeer(peer.m_uid);
            }
        }

        // Each client asks on spawn (initial + respawn / reconnect).
        [HarmonyPatch(typeof(Player), nameof(Player.OnSpawned))]
        private static class PlayerSpawnedPatch
        {
            private static void Postfix(Player __instance)
            {
                if (__instance != Player.m_localPlayer) return;
                RequestFeatureState();
                RequestLadder();
                RequestBoard();
            }
        }
    }
}
