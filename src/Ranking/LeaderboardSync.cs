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
        private const string RpcAdminCmd = "LSII_AdminCmd";
        private const string RpcAdminAck = "LSII_AdminAck";
        private const string RpcSetPartyName = "LSII_SetPartyName";
        private const string RpcTourJoinEsc = "LSII_TourJoinEsc";  // client->server: escrow join (totem payloads)
        private const string RpcTourWithdraw = "LSII_TourWithdraw"; // client->server: withdraw own entry
        private const string RpcTourReturn = "LSII_TourReturn";     // server->all: return totems to owner
        private const string RpcTourSummon = "LSII_TourSummon";     // server->all: summon for a match
        private const string RpcTourReseal = "LSII_TourReseal";     // client->server: updated payload after a match
        private const string RpcAdminChk = "LSII_AdminChk";         // client->server: am I an admin?
        private const string RpcAdminChkResp = "LSII_AdminChkR";    // server->client: yes/no

        // Cached result of the server's authoritative admin check for the local
        // player. ZNet.LocalPlayerIsAdminOrHost() is unreliable on a pure client
        // (the admin list lives server-side), so the tournament UI drives its admin
        // controls off THIS instead — set by asking the server (RequestAdminStatus).
        public static bool LocalIsAdmin { get; private set; }

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
            ZRoutedRpc.instance.Register<string>(RpcAdminCmd, OnAdminCmd);
            ZRoutedRpc.instance.Register<string>(RpcAdminAck, OnAdminAck);
            ZRoutedRpc.instance.Register<string>(RpcSetPartyName, OnSetPartyName);
            ZRoutedRpc.instance.Register<ZPackage>(RpcTourJoinEsc, OnTourJoinEsc);
            ZRoutedRpc.instance.Register<string>(RpcTourWithdraw, OnTourWithdraw);
            ZRoutedRpc.instance.Register<ZPackage>(RpcTourReturn, OnTourReturn);
            ZRoutedRpc.instance.Register<ZPackage>(RpcTourSummon, OnTourSummon);
            ZRoutedRpc.instance.Register<ZPackage>(RpcTourReseal, OnTourReseal);
            ZRoutedRpc.instance.Register<string>(RpcAdminChk, OnAdminChk);
            ZRoutedRpc.instance.Register<string>(RpcAdminChkResp, OnAdminChkResp);
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
                var partyName = LeaderboardStore.FindParty(result.WinnerOwnerId)?.partyName;
                var payload = string.Join("|", new[]
                {
                    (result.WinnerOwnerName ?? string.Empty).Replace('|', '/'),
                    winnerRank.ToString(),
                    winnerRating.ToString(),
                    (partyName ?? string.Empty).Replace('|', '/'),
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
            var partyName = p.Length > 3 ? p[3] : null;
            ServerGuideBridge.RaisePartyRankChanged(p[0], rank, rating, partyName);
            // Reaching #1 also fires the dedicated "new champion" trigger (Discord).
            if (rank == 1) ServerGuideBridge.RaisePartyRankFirst(p[0], rating, partyName);
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

        // ---- Admin actions (client -> server, admin-gated) -------------------
        //
        // Admin-only tournament controls (start/begin/cancel/forfeit, and the
        // escrow release + activate-round added in later phases) reach the
        // authoritative server through one pipe-delimited command string. The
        // server re-verifies the sender is a real admin via the vanilla admin list
        // (ZNet.IsAdmin) — the UI/console gate isn't trusted, mirroring
        // ServerGuide's GuidanceSync admin RPCs. The listen host is authoritative
        // and inherently admin, so it runs the action directly with no round-trip.
        public static void SendAdminCommand(string cmd)
        {
            if (string.IsNullOrEmpty(cmd)) return;
            if (ZNet.instance != null && ZNet.instance.IsServer())
            {
                ShowLocal(ExecuteAdmin(cmd));
                return;
            }
            if (ZRoutedRpc.instance == null) return;
            ZRoutedRpc.instance.InvokeRoutedRPC(ZRoutedRpc.instance.GetServerPeerID(), RpcAdminCmd, cmd);
        }

        private static void OnAdminCmd(long sender, string cmd)
        {
            if (ZNet.instance == null || !ZNet.instance.IsServer()) return;

            var peer = ZNet.instance.GetPeer(sender);
            var hostName = peer?.m_socket?.GetHostName();
            if (string.IsNullOrEmpty(hostName) || !ZNet.instance.IsAdmin(hostName))
            {
                Plugin.Log.LogWarning($"[tourney] non-admin sender ({sender}, host='{hostName}') tried admin cmd '{cmd}' — denied.");
                ZRoutedRpc.instance.InvokeRoutedRPC(sender, RpcAdminAck, "Only a server admin can do that.");
                return;
            }

            var status = ExecuteAdmin(cmd);
            if (!string.IsNullOrEmpty(status))
                ZRoutedRpc.instance.InvokeRoutedRPC(sender, RpcAdminAck, status);
        }

        private static void OnAdminAck(long sender, string msg) => ShowLocal(msg);

        // Ask the server (authoritative) whether the local player is an admin, and
        // cache it in LocalIsAdmin. The listen host is always admin. Called on spawn
        // and whenever the tournament UI opens.
        public static void RequestAdminStatus()
        {
            if (ZNet.instance != null && ZNet.instance.IsServer()) { LocalIsAdmin = true; return; }
            if (ZRoutedRpc.instance == null) return;
            ZRoutedRpc.instance.InvokeRoutedRPC(ZRoutedRpc.instance.GetServerPeerID(), RpcAdminChk, string.Empty);
        }

        private static void OnAdminChk(long sender, string _)
        {
            if (ZNet.instance == null || !ZNet.instance.IsServer()) return;
            var peer = ZNet.instance.GetPeer(sender);
            var hostName = peer?.m_socket?.GetHostName();
            bool ok = !string.IsNullOrEmpty(hostName) && ZNet.instance.IsAdmin(hostName);
            Plugin.Log.LogInfo($"[tourney] admin check for sender {sender} (host='{hostName}') => {ok}.");
            ZRoutedRpc.instance.InvokeRoutedRPC(sender, RpcAdminChkResp, ok ? "1" : "0");
        }

        private static void OnAdminChkResp(long sender, string v)
        {
            LocalIsAdmin = v == "1";
            Plugin.Log.LogInfo($"[tourney] server says local player admin = {LocalIsAdmin}.");
        }

        // ---- Party name (any player names their OWN party) -------------------

        public static void SendPartyName(string name)
        {
            var lp = Player.m_localPlayer;
            if (lp == null) return;
            string S(string s) => (s ?? string.Empty).Replace('|', '/');
            var payload = string.Join("|", new[] { lp.GetPlayerID().ToString(), S(lp.GetPlayerName()), S(name) });
            if (ZNet.instance != null && ZNet.instance.IsServer()) { ApplyPartyName(payload); return; }
            if (ZRoutedRpc.instance == null) return;
            ZRoutedRpc.instance.InvokeRoutedRPC(ZRoutedRpc.instance.GetServerPeerID(), RpcSetPartyName, payload);
        }

        private static void OnSetPartyName(long sender, string payload)
        {
            if (ZNet.instance == null || !ZNet.instance.IsServer()) return;
            ApplyPartyName(payload);
        }

        private static void ApplyPartyName(string payload)
        {
            var p = (payload ?? string.Empty).Split('|');
            if (p.Length < 3) return;
            long.TryParse(p[0], out var ownerId);
            LeaderboardStore.SetPartyName(ownerId, p[1], p[2]);
            BroadcastTable();
        }

        // ---- Tournament escrow registration (docs/Tournaments.md) ------------
        //
        // A player enters by locking a companion's Communion Totem into a slot: the
        // client serializes the totem(s) and sends them here; the server holds the
        // escrow on the entrant. A ZPackage is used (not a pipe string) because the
        // payloads are arbitrary base64. The client removes the item(s) optimistically
        // when it sends; if the server rejects the join it returns them via RpcTourReturn.
        public static void SendTournamentJoinEscrow(string entrantId, long ownerId, string ownerName,
            string label, int caste, int seedRating, System.Collections.Generic.List<string> payloads)
        {
            if (ZRoutedRpc.instance == null) return;
            var pkg = new ZPackage();
            pkg.Write(entrantId ?? string.Empty);
            pkg.Write(ownerId);
            pkg.Write(ownerName ?? string.Empty);
            pkg.Write(label ?? string.Empty);
            pkg.Write(caste);
            pkg.Write(seedRating);
            pkg.Write(payloads != null ? payloads.Count : 0);
            if (payloads != null) foreach (var p in payloads) pkg.Write(p ?? string.Empty);

            if (ZNet.instance != null && ZNet.instance.IsServer()) { HandleJoinEscrow(0L, pkg); return; }
            ZRoutedRpc.instance.InvokeRoutedRPC(ZRoutedRpc.instance.GetServerPeerID(), RpcTourJoinEsc, pkg);
        }

        private static void OnTourJoinEsc(long sender, ZPackage pkg) => HandleJoinEscrow(sender, pkg);

        private static void HandleJoinEscrow(long sender, ZPackage pkg)
        {
            if (ZNet.instance == null || !ZNet.instance.IsServer()) return;
            pkg.SetPos(0);
            var entrantId = pkg.ReadString();
            var ownerId = pkg.ReadLong();
            var ownerName = pkg.ReadString();
            var label = pkg.ReadString();
            var caste = pkg.ReadInt();
            var seed = pkg.ReadInt();
            int n = pkg.ReadInt();
            var payloads = new System.Collections.Generic.List<string>(n);
            for (int i = 0; i < n; i++) payloads.Add(pkg.ReadString());

            string totemPayload = caste >= 0 && payloads.Count > 0 ? payloads[0] : null;
            var teamPayloads = caste < 0 ? payloads : null;
            var status = TournamentService.Join(entrantId, ownerId, ownerName, label, caste, seed, totemPayload, teamPayloads);
            bool ok = status.StartsWith("Registered", System.StringComparison.Ordinal);

            // Ack (message + dvergr_tournament_joined) via the existing path.
            var ack = string.Join("|", new[] { ok ? "1" : "0", TourSubject(caste), status });
            if (sender == 0L) ShowLocal(status);
            else ZRoutedRpc.instance.InvokeRoutedRPC(sender, RpcTourJoinAck, ack);
            if (ok && sender == 0L) ServerGuideBridge.RaiseTournamentJoined(TourSubject(caste));

            // Rejected: the client already removed the totem(s) — hand them back.
            if (!ok && payloads.Count > 0)
                ReturnEscrowToOwners(new System.Collections.Generic.Dictionary<long, System.Collections.Generic.List<string>>
                {
                    { ownerId, payloads },
                });
        }

        public static void SendTournamentWithdraw()
        {
            var lp = Player.m_localPlayer;
            if (lp == null || ZRoutedRpc.instance == null) return;
            if (ZNet.instance != null && ZNet.instance.IsServer()) { HandleWithdraw(0L, lp.GetPlayerID().ToString()); return; }
            ZRoutedRpc.instance.InvokeRoutedRPC(ZRoutedRpc.instance.GetServerPeerID(), RpcTourWithdraw, lp.GetPlayerID().ToString());
        }

        private static void OnTourWithdraw(long sender, string ownerIdStr) => HandleWithdraw(sender, ownerIdStr);

        private static void HandleWithdraw(long sender, string ownerIdStr)
        {
            if (ZNet.instance == null || !ZNet.instance.IsServer()) return;
            long.TryParse(ownerIdStr, out var ownerId);
            var payloads = TournamentService.Withdraw(ownerId, out var status);
            if (sender == 0L) ShowLocal(status);
            else ZRoutedRpc.instance.InvokeRoutedRPC(sender, RpcAdminAck, status);
            if (payloads.Count > 0)
                ReturnEscrowToOwners(new System.Collections.Generic.Dictionary<long, System.Collections.Generic.List<string>>
                {
                    { ownerId, payloads },
                });
        }

        // Server -> all: give escrowed totem(s) back to their owner. Broadcast with
        // the owner id embedded (like the tournament match/won events); only the
        // matching client rebuilds the items, so no server-side peer lookup is needed.
        public static void ReturnEscrowToOwners(System.Collections.Generic.Dictionary<long, System.Collections.Generic.List<string>> byOwner)
        {
            if (ZRoutedRpc.instance == null || byOwner == null) return;
            foreach (var kv in byOwner)
            {
                if (kv.Value == null || kv.Value.Count == 0) continue;
                var pkg = new ZPackage();
                pkg.Write(kv.Key);
                pkg.Write(kv.Value.Count);
                foreach (var p in kv.Value) pkg.Write(p ?? string.Empty);
                ZRoutedRpc.instance.InvokeRoutedRPC(0L, RpcTourReturn, pkg);
            }
        }

        private static void OnTourReturn(long sender, ZPackage pkg)
        {
            var lp = Player.m_localPlayer;
            if (lp == null) return;
            pkg.SetPos(0);
            var ownerId = pkg.ReadLong();
            if (ownerId != lp.GetPlayerID()) return; // not mine
            int n = pkg.ReadInt();
            int restored = 0;
            for (int i = 0; i < n; i++)
            {
                var payload = pkg.ReadString();
                var item = Companions.TotemConversionService.BuildTotemFromPayload(payload);
                if (item == null) continue;
                if (!lp.GetInventory().AddItem(item))
                {
                    // Inventory full — drop the rebuilt totem at the player's feet.
                    var drop = ItemDrop.DropItem(item, 1, lp.transform.position + UnityEngine.Vector3.up, lp.transform.rotation);
                    if (drop == null) continue;
                }
                restored++;
            }
            if (restored > 0 && MessageHud.instance != null)
                MessageHud.instance.ShowMessage(MessageHud.MessageType.Center,
                    restored == 1 ? "Your companion totem is returned." : $"{restored} companion totems returned.");
        }

        // ---- Tournament auto-summon + reseal (docs/Tournaments.md, Phase 5) ---
        //
        // On "activate round" the server tells each participant's owner client to
        // summon its escrowed companion(s) for the pairing and enter duel mode
        // against the assigned opponent. Broadcast with the owner id embedded; only
        // the matching client summons (no server-side peer lookup, works for the
        // listen host too). The client tags each summoned companion so it reseals +
        // despawns when the match resolves (TournamentCombatant).
        public static void SummonForMatch(long ownerId, string entrantId, string mode,
            string opponentEntrantId, string opponentLabel, int round,
            System.Collections.Generic.List<string> payloads)
        {
            if (ZRoutedRpc.instance == null) return;
            var pkg = new ZPackage();
            pkg.Write(ownerId);
            pkg.Write(entrantId ?? string.Empty);
            pkg.Write(mode ?? "1v1");
            pkg.Write(opponentEntrantId ?? string.Empty);
            pkg.Write(opponentLabel ?? string.Empty);
            pkg.Write(round);
            pkg.Write(payloads != null ? payloads.Count : 0);
            if (payloads != null) foreach (var p in payloads) pkg.Write(p ?? string.Empty);
            ZRoutedRpc.instance.InvokeRoutedRPC(0L, RpcTourSummon, pkg);
        }

        private static void OnTourSummon(long sender, ZPackage pkg)
        {
            var lp = Player.m_localPlayer;
            if (lp == null) return;
            pkg.SetPos(0);
            var ownerId = pkg.ReadLong();
            if (ownerId != lp.GetPlayerID()) return; // not my companion
            var entrantId = pkg.ReadString();
            var mode = pkg.ReadString();
            var opponentEntrantId = pkg.ReadString();
            var opponentLabel = pkg.ReadString();
            var round = pkg.ReadInt();
            int n = pkg.ReadInt();
            var payloads = new System.Collections.Generic.List<string>(n);
            for (int i = 0; i < n; i++) payloads.Add(pkg.ReadString());
            Companions.TournamentClient.SummonForMatch(lp, entrantId, mode, opponentEntrantId, opponentLabel, round, payloads);
        }

        // Client -> server: an updated escrow payload set after a match (the winner
        // reseals its leveled-up companion; losers reseal their current state).
        public static void SendReseal(string entrantId, System.Collections.Generic.List<string> payloads)
        {
            if (ZRoutedRpc.instance == null) return;
            var pkg = new ZPackage();
            pkg.Write(entrantId ?? string.Empty);
            pkg.Write(payloads != null ? payloads.Count : 0);
            if (payloads != null) foreach (var p in payloads) pkg.Write(p ?? string.Empty);
            if (ZNet.instance != null && ZNet.instance.IsServer()) { HandleReseal(pkg); return; }
            ZRoutedRpc.instance.InvokeRoutedRPC(ZRoutedRpc.instance.GetServerPeerID(), RpcTourReseal, pkg);
        }

        private static void OnTourReseal(long sender, ZPackage pkg) => HandleReseal(pkg);

        private static void HandleReseal(ZPackage pkg)
        {
            if (ZNet.instance == null || !ZNet.instance.IsServer()) return;
            pkg.SetPos(0);
            var entrantId = pkg.ReadString();
            int n = pkg.ReadInt();
            var payloads = new System.Collections.Generic.List<string>(n);
            for (int i = 0; i < n; i++) payloads.Add(pkg.ReadString());
            TournamentService.UpdateEscrow(entrantId, payloads);
        }

        private static void ShowLocal(string msg)
        {
            if (!string.IsNullOrEmpty(msg) && MessageHud.instance != null)
                MessageHud.instance.ShowMessage(MessageHud.MessageType.Center, msg);
        }

        // Runs on the server/host only. Parses the admin command and routes to the
        // authoritative TournamentService. Returns a status line for the admin.
        // (release/activate cases are added in the escrow phases.)
        private static string ExecuteAdmin(string cmd)
        {
            var p = (cmd ?? string.Empty).Split('|');
            switch (p.Length > 0 ? p[0] : string.Empty)
            {
                case "start":
                {
                    var mode = p.Length > 1 ? p[1] : "1v1";
                    int size = 0; if (p.Length > 2) int.TryParse(p[2], out size);
                    return TournamentService.Start(mode, size);
                }
                case "begin":   return TournamentService.Begin();
                case "cancel":  return TournamentService.Cancel();
                case "forfeit": return p.Length > 1 ? TournamentService.Forfeit(p[1]) : "Usage: forfeit <name>.";
                case "release":
                {
                    if (p.Length < 2) return "Usage: release <entrant name>.";
                    var payloads = TournamentService.ReleaseEntrant(p[1], out var ownerId, out var status);
                    if (payloads.Count > 0)
                        ReturnEscrowToOwners(new System.Collections.Generic.Dictionary<long, System.Collections.Generic.List<string>>
                        {
                            { ownerId, payloads },
                        });
                    return status;
                }
                case "activate": return TournamentService.ActivateCurrentRound();
                default:        return $"Unknown admin action '{(p.Length > 0 ? p[0] : "")}'.";
            }
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
            // Reaching #1 also fires the dedicated "new champion" trigger (Discord).
            if (rank == 1) ServerGuideBridge.RaiseRankFirst(caste, rating, p[3], p[4]);
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
                RequestAdminStatus();
            }
        }
    }
}
