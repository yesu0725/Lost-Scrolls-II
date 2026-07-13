using System;
using HarmonyLib;
using LostScrollsII.Companions;
using LostScrollsII.Integration;

namespace LostScrollsII.Ranking
{
    // Server <-> client sync for the duel ladder (docs/Ranking.md). Mirrors
    // ServerGuide's GuidanceSync: RPCs bind on ZNet.Awake, the SERVER owns the
    // authoritative LeaderboardStore, and clients receive a read-only snapshot.
    //
    // Flow:
    //  - A winner's client reports a resolved duel (ReportDuel -> LSII_ReportDuel).
    //  - The server validates + applies it, persists, then broadcasts the whole
    //    table to every client (LSII_LadderPush) and, if the winner climbed into
    //    the top 3, sends a rank event back to the winner's client (LSII_RankEvent)
    //    which fires the dvergr_rank_changed ServerGuide trigger so the reward is
    //    granted to the right player (rewards always target Player.m_localPlayer).
    //  - On every player spawn a client asks the server for the current table.
    public static class LeaderboardSync
    {
        private const string RpcReportDuel = "LSII_ReportDuel";
        private const string RpcLadderPush = "LSII_LadderPush";
        private const string RpcLadderRequest = "LSII_LadderReq";
        private const string RpcRankEvent = "LSII_RankEvent";
        private const string RpcReportParty = "LSII_ReportParty";
        private const string RpcPartyRankEvent = "LSII_PartyRankEvt";
        private const string RpcTourJoin = "LSII_TourJoin";
        private const string RpcTourJoinAck = "LSII_TourJoinAck";
        private const string RpcTourPush = "LSII_TourPush";
        private const string RpcTourRequest = "LSII_TourReq";
        private const string RpcTourMatch = "LSII_TourMatch";
        private const string RpcTourWon = "LSII_TourWon";

        private static bool _bound;

        private static void EnsureRegistered()
        {
            if (_bound) return;
            if (ZRoutedRpc.instance == null) return;
            ZRoutedRpc.instance.Register<string>(RpcReportDuel, OnReportDuel);
            ZRoutedRpc.instance.Register<ZPackage>(RpcLadderPush, OnLadderPush);
            ZRoutedRpc.instance.Register<string>(RpcLadderRequest, OnLadderRequest);
            ZRoutedRpc.instance.Register<string>(RpcRankEvent, OnRankEvent);
            ZRoutedRpc.instance.Register<string>(RpcReportParty, OnReportParty);
            ZRoutedRpc.instance.Register<string>(RpcPartyRankEvent, OnPartyRankEvent);
            ZRoutedRpc.instance.Register<string>(RpcTourJoin, OnTourJoin);
            ZRoutedRpc.instance.Register<string>(RpcTourJoinAck, OnTourJoinAck);
            ZRoutedRpc.instance.Register<ZPackage>(RpcTourPush, OnTourPush);
            ZRoutedRpc.instance.Register<string>(RpcTourRequest, OnTourRequest);
            ZRoutedRpc.instance.Register<string>(RpcTourMatch, OnTourMatch);
            ZRoutedRpc.instance.Register<string>(RpcTourWon, OnTourWon);
            _bound = true;
            Plugin.Log.LogInfo("[ladder] RPCs registered.");
        }

        // ---- Report a resolved duel (client -> server) -----------------------

        public static void ReportDuel(DuelResult result)
        {
            if (result == null || ZRoutedRpc.instance == null) return;
            var server = ZRoutedRpc.instance.GetServerPeerID();
            ZRoutedRpc.instance.InvokeRoutedRPC(server, RpcReportDuel, result.Encode());
        }

        private static void OnReportDuel(long sender, string encoded)
        {
            if (ZNet.instance == null || !ZNet.instance.IsServer()) return;
            var result = DuelResult.Decode(encoded);
            if (result == null) return;

            // A tournament match is decided by the same duel report — resolve it
            // even if the bout isn't ladder-eligible (e.g. pair cooldown).
            TournamentService.NotifyDuelResult(result.WinnerId, result.LoserId);

            if (!LeaderboardStore.ApplyDuel(result,
                    Plugin.RankingKFactor.Value, Plugin.RankingPairCooldown.Value,
                    out var winnerRank, out var winnerRating, out var crossed))
            {
                return;
            }

            Plugin.Log.LogInfo($"[ladder] duel recorded: '{result.WinnerName}' beat '{result.LoserName}' " +
                $"(rank #{winnerRank}, {winnerRating}).");

            // Push the updated table to everyone.
            BroadcastTable();

            // Tell the winner's client to fire the rank-changed trigger (so the
            // reward lands on that player). Only when a real threshold was crossed.
            if (crossed)
            {
                var payload = string.Join("|", new[]
                {
                    result.WinnerCaste.ToString(),
                    winnerRank.ToString(),
                    winnerRating.ToString(),
                    (result.WinnerName ?? string.Empty).Replace('|', '/'),
                    (result.WinnerOwnerName ?? string.Empty).Replace('|', '/'),
                });
                ZRoutedRpc.instance.InvokeRoutedRPC(sender, RpcRankEvent, payload);
            }
        }

        // ---- Report a resolved party duel (client -> server) -----------------

        public static void ReportPartyDuel(PartyDuelResult result)
        {
            if (result == null || ZRoutedRpc.instance == null) return;
            var server = ZRoutedRpc.instance.GetServerPeerID();
            ZRoutedRpc.instance.InvokeRoutedRPC(server, RpcReportParty, result.Encode());
        }

        private static void OnReportParty(long sender, string encoded)
        {
            if (ZNet.instance == null || !ZNet.instance.IsServer()) return;
            var result = PartyDuelResult.Decode(encoded);
            if (result == null) return;

            TournamentService.NotifyPartyResult(result.WinnerOwnerId, result.LoserOwnerId);

            if (!LeaderboardStore.ApplyPartyDuel(result, Plugin.RankingKFactor.Value,
                    out var winnerRank, out var winnerRating, out var crossed))
            {
                return;
            }

            Plugin.Log.LogInfo($"[ladder] party duel recorded: '{result.WinnerOwnerName}' beat " +
                $"'{result.LoserOwnerName}' (party rank #{winnerRank}, {winnerRating}).");

            BroadcastTable();

            if (crossed)
            {
                var payload = string.Join("|", new[]
                {
                    (result.WinnerOwnerName ?? string.Empty).Replace('|', '/'),
                    winnerRank.ToString(),
                    winnerRating.ToString(),
                });
                ZRoutedRpc.instance.InvokeRoutedRPC(sender, RpcPartyRankEvent, payload);
            }
        }

        private static void OnPartyRankEvent(long sender, string payload)
        {
            if (Player.m_localPlayer == null || string.IsNullOrEmpty(payload)) return;
            var p = payload.Split('|');
            if (p.Length < 3) return;
            int.TryParse(p[1], out var rank);
            int.TryParse(p[2], out var rating);
            ServerGuideBridge.RaisePartyRankChanged(p[0], rank, rating);
        }

        // ---- Tournament (docs/Tournaments.md) --------------------------------

        // Subject used for the ServerGuide tournament triggers: caste name for a
        // 1v1 entry, "party" for a party entry (caste < 0).
        private static string TourSubject(int caste)
            => caste < 0 ? "party" : ((DvergrCaste)caste).ToString();

        public static void SendTournamentJoin(string entrantId, long ownerId, string ownerName,
            string label, int caste, int seedRating)
        {
            if (ZRoutedRpc.instance == null) return;
            string S(string s) => (s ?? string.Empty).Replace('|', '/');
            var payload = string.Join("|", new[]
            {
                S(entrantId), ownerId.ToString(), S(ownerName), S(label), caste.ToString(), seedRating.ToString(),
            });
            ZRoutedRpc.instance.InvokeRoutedRPC(ZRoutedRpc.instance.GetServerPeerID(), RpcTourJoin, payload);
        }

        private static void OnTourJoin(long sender, string payload)
        {
            if (ZNet.instance == null || !ZNet.instance.IsServer()) return;
            var p = payload.Split('|');
            if (p.Length < 6) return;
            long.TryParse(p[1], out var ownerId);
            int.TryParse(p[4], out var caste);
            int.TryParse(p[5], out var seed);
            var status = TournamentService.Join(p[0], ownerId, p[2], p[3], caste, seed);
            bool ok = status.StartsWith("Registered", System.StringComparison.Ordinal);
            var ack = string.Join("|", new[] { ok ? "1" : "0", TourSubject(caste), status });
            ZRoutedRpc.instance.InvokeRoutedRPC(sender, RpcTourJoinAck, ack);
        }

        private static void OnTourJoinAck(long sender, string payload)
        {
            if (Player.m_localPlayer == null) return;
            var p = payload.Split('|');
            if (p.Length < 3) return;
            if (MessageHud.instance != null)
                MessageHud.instance.ShowMessage(MessageHud.MessageType.Center, p[2]);
            if (p[0] == "1") ServerGuideBridge.RaiseTournamentJoined(p[1]);
        }

        public static void BroadcastTournament()
        {
            if (ZRoutedRpc.instance == null) return;
            var pkg = new ZPackage();
            pkg.Write(TournamentService.SerializeSnapshot());
            ZRoutedRpc.instance.InvokeRoutedRPC(0L, RpcTourPush, pkg);
        }

        private static void SendTournamentToPeer(long peer)
        {
            if (ZRoutedRpc.instance == null) return;
            var pkg = new ZPackage();
            pkg.Write(TournamentService.SerializeSnapshot());
            ZRoutedRpc.instance.InvokeRoutedRPC(peer, RpcTourPush, pkg);
        }

        private static void OnTourPush(long sender, ZPackage pkg)
        {
            if (ZNet.instance != null && ZNet.instance.IsServer()) return;
            TournamentService.ApplySnapshot(pkg.ReadString());
        }

        public static void RequestTournament()
        {
            if (ZRoutedRpc.instance == null) return;
            if (ZNet.instance != null && ZNet.instance.IsServer()) return;
            ZRoutedRpc.instance.InvokeRoutedRPC(ZRoutedRpc.instance.GetServerPeerID(), RpcTourRequest, string.Empty);
        }

        private static void OnTourRequest(long sender, string _)
        {
            if (ZNet.instance == null || !ZNet.instance.IsServer()) return;
            SendTournamentToPeer(sender);
        }

        // Match + won events are broadcast with the target player's name embedded;
        // each client acts only on its own. Avoids server-side peer lookup and works
        // for the listen host's own player too.
        public static void SendTournamentMatch(string targetName, int round, string opponentLabel, int caste)
        {
            if (ZRoutedRpc.instance == null) return;
            string S(string s) => (s ?? string.Empty).Replace('|', '/');
            var payload = string.Join("|", new[] { S(targetName), round.ToString(), S(opponentLabel), caste.ToString() });
            ZRoutedRpc.instance.InvokeRoutedRPC(0L, RpcTourMatch, payload);
        }

        private static void OnTourMatch(long sender, string payload)
        {
            var lp = Player.m_localPlayer;
            if (lp == null) return;
            var p = payload.Split('|');
            if (p.Length < 4) return;
            if (!string.Equals(p[0], lp.GetPlayerName(), System.StringComparison.Ordinal)) return;
            int.TryParse(p[1], out var round);
            int.TryParse(p[3], out var caste);
            ServerGuideBridge.RaiseTournamentMatch(TourSubject(caste), round, p[2]);
        }

        public static void SendTournamentWon(string targetName, string mode, int bracketSize, int caste)
        {
            if (ZRoutedRpc.instance == null) return;
            string S(string s) => (s ?? string.Empty).Replace('|', '/');
            var payload = string.Join("|", new[] { S(targetName), S(mode), bracketSize.ToString(), caste.ToString() });
            ZRoutedRpc.instance.InvokeRoutedRPC(0L, RpcTourWon, payload);
        }

        private static void OnTourWon(long sender, string payload)
        {
            var lp = Player.m_localPlayer;
            if (lp == null) return;
            var p = payload.Split('|');
            if (p.Length < 4) return;
            if (!string.Equals(p[0], lp.GetPlayerName(), System.StringComparison.Ordinal)) return;
            int.TryParse(p[2], out var bracketSize);
            int.TryParse(p[3], out var caste);
            ServerGuideBridge.RaiseTournamentWon(TourSubject(caste), p[1], bracketSize);
        }

        // ---- Push the table (server -> clients) ------------------------------

        public static void BroadcastTable()
        {
            if (ZRoutedRpc.instance == null) return;
            var pkg = new ZPackage();
            pkg.Write(LeaderboardStore.SerializeSnapshot());
            ZRoutedRpc.instance.InvokeRoutedRPC(0L, RpcLadderPush, pkg);
        }

        public static void SendTableToPeer(long peer)
        {
            if (ZRoutedRpc.instance == null) return;
            var pkg = new ZPackage();
            pkg.Write(LeaderboardStore.SerializeSnapshot());
            ZRoutedRpc.instance.InvokeRoutedRPC(peer, RpcLadderPush, pkg);
        }

        private static void OnLadderPush(long sender, ZPackage pkg)
        {
            // The server is the authority and already has the live table.
            if (ZNet.instance != null && ZNet.instance.IsServer()) return;
            LeaderboardStore.ApplySnapshot(pkg.ReadString());
        }

        // ---- Request the table (client -> server, on spawn) ------------------

        public static void RequestTable()
        {
            if (ZRoutedRpc.instance == null) return;
            if (ZNet.instance != null && ZNet.instance.IsServer()) return; // host already has it
            var server = ZRoutedRpc.instance.GetServerPeerID();
            ZRoutedRpc.instance.InvokeRoutedRPC(server, RpcLadderRequest, string.Empty);
        }

        private static void OnLadderRequest(long sender, string _)
        {
            if (ZNet.instance == null || !ZNet.instance.IsServer()) return;
            SendTableToPeer(sender);
        }

        // ---- Rank-changed event (server -> winner client) --------------------

        private static void OnRankEvent(long sender, string payload)
        {
            if (Player.m_localPlayer == null || string.IsNullOrEmpty(payload)) return;
            var p = payload.Split('|');
            if (p.Length < 5) return;
            int.TryParse(p[0], out var caste);
            int.TryParse(p[1], out var rank);
            int.TryParse(p[2], out var rating);
            ServerGuideBridge.RaiseRankChanged(caste, rank, rating, p[3], p[4]);
        }

        // ---- Lifecycle (mirrors GuidanceSync) --------------------------------

        [HarmonyPatch(typeof(ZNet), nameof(ZNet.Awake))]
        private static class ZNetAwakePatch
        {
            private static void Postfix(ZNet __instance)
            {
                EnsureRegistered();
                if (__instance.IsServer())
                {
                    LeaderboardStore.LoadForCurrentWorld();
                    TournamentService.LoadForCurrentWorld();
                }
            }
        }

        [HarmonyPatch(typeof(ZNet), nameof(ZNet.OnDestroy))]
        private static class ZNetOnDestroyPatch
        {
            private static void Postfix() { _bound = false; }
        }

        // Server pushes the current table to each joining peer.
        [HarmonyPatch(typeof(ZNet), nameof(ZNet.RPC_PeerInfo))]
        private static class PeerInfoPatch
        {
            private static void Postfix(ZNet __instance, ZRpc rpc)
            {
                if (!__instance.IsServer()) return;
                var peer = __instance.GetPeer(rpc);
                if (peer == null) return;
                EnsureRegistered();
                SendTableToPeer(peer.m_uid);
                SendTournamentToPeer(peer.m_uid);
            }
        }

        // Each client asks for the table on spawn (initial + respawn / reconnect).
        [HarmonyPatch(typeof(Player), nameof(Player.OnSpawned))]
        private static class PlayerSpawnedPatch
        {
            private static void Postfix(Player __instance)
            {
                if (__instance != Player.m_localPlayer) return;
                RequestTable();
                RequestTournament();
            }
        }
    }
}
