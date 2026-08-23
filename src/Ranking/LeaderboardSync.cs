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

        // Wagered events (docs/Wagers.md). Deliberately NOT admin-gated — any
        // player may open a staked tournament, ready up for their match, or post a
        // duel invite; the stake is the gate.
        private const string RpcWagerStart = "LSII_WagerStart";     // client->server: open a staked tournament
        private const string RpcTourReady = "LSII_TourReady";       // client->server: ready for my match
        private const string RpcWagerPay = "LSII_WagerPay";         // server->all: pay Coins to one owner
        private const string RpcWagerPrize = "LSII_WagerPrize";     // server->all: fire a Valcoin purse trigger on one player
        private const string RpcInvite = "LSII_Invite";             // client->server: duel-invite actions
        private const string RpcInvitePush = "LSII_InvitePush";     // server->all: the invite board
        private const string RpcInviteReq = "LSII_InviteReq";       // client->server: send me the board

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
            ZRoutedRpc.instance.Register<string>(RpcWagerStart, OnWagerStart);
            ZRoutedRpc.instance.Register<string>(RpcTourReady, OnTourReady);
            ZRoutedRpc.instance.Register<string>(RpcWagerPay, OnWagerPay);
            ZRoutedRpc.instance.Register<string>(RpcWagerPrize, OnWagerPrize);
            ZRoutedRpc.instance.Register<ZPackage>(RpcInvite, OnInvite);
            ZRoutedRpc.instance.Register<ZPackage>(RpcInvitePush, OnInvitePush);
            ZRoutedRpc.instance.Register<string>(RpcInviteReq, OnInviteRequest);
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

            // A tournament match, or a staked duel invite, is decided by this same
            // duel report — resolve either even if the bout isn't ladder-eligible
            // (e.g. pair cooldown). A companion id belongs to at most one of them.
            TournamentService.NotifyDuelResult(result.WinnerId, result.LoserId);
            DuelInviteService.NotifyDuelResult(result.WinnerId, result.LoserId);

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

        // (The original non-escrow join RPC was removed: registration has been
        // escrow-only since the tournament panel landed, and keeping a second entry
        // point that could not carry a totem payload, a slot key or a stake meant
        // three signatures to keep in step for a path nothing called.)

        private static void OnTourJoinAck(long sender, string payload)
        {
            if (Player.m_localPlayer == null) return;
            var p = payload.Split('|');
            if (p.Length < 3) return;
            if (MessageHud.instance != null)
                MessageHud.instance.ShowMessage(MessageHud.MessageType.TopLeft, p[2]);
            if (p[0] == "1") ServerGuideBridge.RaiseTournamentJoined(p[1]);
        }

        public static void BroadcastTournament()
        {
            if (ZRoutedRpc.instance == null) return;
            var json = TournamentService.SerializeSnapshot();
            Plugin.Log.LogInfo($"[tourney] broadcast: jsonLen={json.Length}, hasEntrants={json.Contains("entrantId")}.");
            var pkg = new ZPackage();
            pkg.Write(json);
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
        public static void SendTournamentJoinEscrow(string key, string entrantId, long ownerId, string ownerName,
            string label, int caste, int seedRating, System.Collections.Generic.List<string> payloads, int level = 0,
            int clientPaidCoins = 0)
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
            pkg.Write(level);
            // Slot key + the Coin stake the client has already removed from its own
            // inventory. Appended AFTER `level` so the reader's existing tolerance
            // for a short payload keeps working.
            pkg.Write(key ?? string.Empty);
            pkg.Write(clientPaidCoins);

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
            int level = 0;
            string key = string.Empty;
            int clientPaidCoins = 0;
            try
            {
                level = pkg.ReadInt();
                key = pkg.ReadString();
                clientPaidCoins = pkg.ReadInt();
            }
            catch { /* older client payload without level / slot key / stake */ }

            string totemPayload = caste >= 0 && payloads.Count > 0 ? payloads[0] : null;
            var teamPayloads = caste < 0 ? payloads : null;

            // Registration is now ASYNCHRONOUS: a Valcoin entry fee is a remote
            // ledger call, so the entrant is only added once the stake settles and
            // the ack has to wait for the same answer. (A Coin entry still resolves
            // in the same frame — the callback just fires immediately.)
            TournamentService.Join(key, entrantId, ownerId, ownerName, label, caste, seed,
                totemPayload, teamPayloads, level, clientPaidCoins, (ok, status) =>
                {
                    Plugin.Log.LogInfo($"[tourney] join from sender={sender} into slot '{key}': owner={ownerId}/'{ownerName}', " +
                        $"label='{label}', level={level}, paidCoins={clientPaidCoins}, payloadLen={(totemPayload?.Length ?? 0)} => \"{status}\" " +
                        $"(entrants now {TournamentService.Get(key)?.entrants?.Count ?? -1}).");

                    var ack = string.Join("|", new[] { ok ? "1" : "0", TourSubject(caste), status ?? string.Empty });
                    if (sender == 0L) ShowLocal(status);
                    else ZRoutedRpc.instance.InvokeRoutedRPC(sender, RpcTourJoinAck, ack);
                    if (ok && sender == 0L) ServerGuideBridge.RaiseTournamentJoined(TourSubject(caste));

                    // Rejected: the client already removed the totem(s) AND any Coin
                    // stake — hand both back.
                    if (!ok)
                    {
                        if (payloads.Count > 0)
                            ReturnEscrowToOwners(new System.Collections.Generic.Dictionary<long, System.Collections.Generic.List<string>>
                            {
                                { ownerId, payloads },
                            });
                        if (clientPaidCoins > 0) SendWagerPayout(ownerId, clientPaidCoins, "entry refunded");
                    }
                });
        }

        public static void SendTournamentWithdraw(string key)
        {
            var lp = Player.m_localPlayer;
            if (lp == null || ZRoutedRpc.instance == null) return;
            var payload = lp.GetPlayerID() + "|" + (key ?? string.Empty);
            if (ZNet.instance != null && ZNet.instance.IsServer()) { HandleWithdraw(0L, payload); return; }
            ZRoutedRpc.instance.InvokeRoutedRPC(ZRoutedRpc.instance.GetServerPeerID(), RpcTourWithdraw, payload);
        }

        private static void OnTourWithdraw(long sender, string payload) => HandleWithdraw(sender, payload);

        private static void HandleWithdraw(long sender, string payload)
        {
            if (ZNet.instance == null || !ZNet.instance.IsServer()) return;
            var p = (payload ?? string.Empty).Split('|');
            long.TryParse(p[0], out var ownerId);
            var key = p.Length > 1 ? p[1] : string.Empty;
            var payloads = TournamentService.Withdraw(key, ownerId, out var status);
            if (sender == 0L) ShowLocal(status);
            else ZRoutedRpc.instance.InvokeRoutedRPC(sender, RpcAdminAck, status);
            if (payloads.Count > 0)
                ReturnEscrowToOwners(new System.Collections.Generic.Dictionary<long, System.Collections.Generic.List<string>>
                {
                    { ownerId, payloads },
                });
        }

        // ---- Wagered events (docs/Wagers.md) ---------------------------------
        //
        // Three client->server actions any player may take (no admin gate), and one
        // server->client payout. They live here rather than in a second RPC layer so
        // there stays one registration list and one lifecycle to reason about.

        // Open a staked tournament. `clientPaidCoins` is the Coin stake the client
        // has already removed from its own inventory (0 for a Valcoin tournament,
        // which the server debits itself).
        public static void SendStartWagered(string currency, int clientPaidCoins)
        {
            var lp = Player.m_localPlayer;
            if (lp == null || ZRoutedRpc.instance == null) return;
            var payload = string.Join("|", new[]
            {
                currency ?? string.Empty,
                lp.GetPlayerID().ToString(),
                (lp.GetPlayerName() ?? string.Empty).Replace('|', '/'),
                clientPaidCoins.ToString(),
            });
            if (ZNet.instance != null && ZNet.instance.IsServer()) { HandleStartWagered(0L, payload); return; }
            ZRoutedRpc.instance.InvokeRoutedRPC(ZRoutedRpc.instance.GetServerPeerID(), RpcWagerStart, payload);
        }

        private static void OnWagerStart(long sender, string payload) => HandleStartWagered(sender, payload);

        private static void HandleStartWagered(long sender, string payload)
        {
            if (ZNet.instance == null || !ZNet.instance.IsServer()) return;
            var p = (payload ?? string.Empty).Split('|');
            if (p.Length < 4) return;
            var currency = Economy.Wager.Parse(p[0]);
            long.TryParse(p[1], out var hostId);
            var hostName = p[2];
            int.TryParse(p[3], out var paidCoins);

            // The Coin stake was taken client-side before the request was sent, so a
            // refusal here has to hand it straight back.
            if (currency == Economy.WagerCurrency.Coins && paidCoins < Economy.Wager.TournamentFee(currency))
            {
                Reply(sender, "You do not have the Coins to open a tournament.");
                if (paidCoins > 0) SendWagerPayout(hostId, paidCoins, "tournament not opened");
                return;
            }

            TournamentService.StartWagered(currency, hostId, hostName, (ok, msg) =>
            {
                Reply(sender, msg);
                if (!ok && paidCoins > 0) SendWagerPayout(hostId, paidCoins, "tournament not opened");
            });
        }

        // "I am ready to fight" for the caller's current tournament match. The
        // pairing is only summoned once BOTH owners have said so, which is what
        // lets the two players agree on where the duel happens.
        public static void SendTournamentReady(string key)
        {
            var lp = Player.m_localPlayer;
            if (lp == null || ZRoutedRpc.instance == null) return;
            var payload = lp.GetPlayerID() + "|" + (key ?? string.Empty);
            if (ZNet.instance != null && ZNet.instance.IsServer()) { HandleReady(0L, payload); return; }
            ZRoutedRpc.instance.InvokeRoutedRPC(ZRoutedRpc.instance.GetServerPeerID(), RpcTourReady, payload);
        }

        private static void OnTourReady(long sender, string payload) => HandleReady(sender, payload);

        private static void HandleReady(long sender, string payload)
        {
            if (ZNet.instance == null || !ZNet.instance.IsServer()) return;
            var p = (payload ?? string.Empty).Split('|');
            long.TryParse(p[0], out var ownerId);
            var key = p.Length > 1 ? p[1] : string.Empty;
            Reply(sender, TournamentService.SetReady(key, ownerId));
        }

        // Server -> all: pay a player vanilla Coins (a purse, a prize, or a refund).
        // Broadcast with the owner id embedded and acted on only by the matching
        // client, exactly like the totem return — Coins are inventory items, so only
        // the owning client can actually add them.
        public static void SendWagerPayout(long ownerId, int amount, string reason)
        {
            if (ZRoutedRpc.instance == null || amount <= 0) return;
            var payload = string.Join("|", new[] { ownerId.ToString(), amount.ToString(), (reason ?? string.Empty).Replace('|', '/') });
            ZRoutedRpc.instance.InvokeRoutedRPC(0L, RpcWagerPay, payload);
        }

        private static void OnWagerPay(long sender, string payload)
        {
            var lp = Player.m_localPlayer;
            if (lp == null) return;
            var p = (payload ?? string.Empty).Split('|');
            if (p.Length < 2) return;
            long.TryParse(p[0], out var ownerId);
            if (ownerId != lp.GetPlayerID()) return;   // not mine
            int.TryParse(p[1], out var amount);
            if (amount <= 0) return;

            Economy.Wager.GiveCoins(lp, amount);
            var reason = p.Length > 2 ? p[2] : string.Empty;
            ShowLocal(string.IsNullOrEmpty(reason) ? $"+{amount} Coins" : $"+{amount} Coins — {reason}");
        }

        // Server -> all: the champion of a VALCOIN tournament fires the prize
        // trigger on their own client, so the guidance entry (and through it the
        // donations mod's quest table) grants the purse to the right player. This
        // mod never states the Valcoin amount that is actually paid.
        public static void SendTournamentPrize(string targetName, string currency, int amount)
        {
            if (ZRoutedRpc.instance == null) return;
            var payload = string.Join("|", new[]
            {
                (targetName ?? string.Empty).Replace('|', '/'), currency ?? string.Empty, amount.ToString(), "tournament",
            });
            ZRoutedRpc.instance.InvokeRoutedRPC(0L, RpcWagerPrize, payload);
        }

        // (A duel invite needs no equivalent: its purse is exactly the two stakes
        // that were collected, so it is paid by moving them — WagerService.Pay,
        // which credits Valcoins through the donations ledger. Nothing is minted,
        // so nothing has to be priced elsewhere. Only the TOURNAMENT purse exceeds
        // what the entry fees collected, which is why that one alone goes through
        // the quest-key path.)

        private static void OnWagerPrize(long sender, string payload)
        {
            var lp = Player.m_localPlayer;
            if (lp == null) return;
            var p = (payload ?? string.Empty).Split('|');
            if (p.Length < 4) return;
            if (!string.Equals(p[0], lp.GetPlayerName(), System.StringComparison.Ordinal)) return;
            int.TryParse(p[2], out var amount);
            ServerGuideBridge.RaiseTournamentPrize(p[1], amount);
        }

        // Status line back to whoever asked — the local host, or a remote client.
        private static void Reply(long sender, string message)
        {
            if (string.IsNullOrEmpty(message)) return;
            if (sender == 0L) { ShowLocal(message); return; }
            if (ZRoutedRpc.instance != null) ZRoutedRpc.instance.InvokeRoutedRPC(sender, RpcAdminAck, message);
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
                MessageHud.instance.ShowMessage(MessageHud.MessageType.TopLeft,
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
        // Which system a summoned companion belongs to, so the client-side driver
        // knows where to look up "is my match over yet?" — the tournament bracket or
        // the duel-invite board. Both use the identical summon/reseal/despawn
        // machinery, and this tag is the only thing that differs.
        public const string ContextTournament = "tourney";
        public const string ContextInvite = "invite";

        public static void SummonForMatch(long ownerId, string entrantId, string mode,
            string opponentEntrantId, string opponentLabel, int round,
            System.Collections.Generic.List<string> payloads, string context = ContextTournament)
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
            pkg.Write(context ?? ContextTournament);
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
            string context = ContextTournament;
            try { context = pkg.ReadString(); } catch { /* pre-invite payload */ }
            Companions.TournamentClient.SummonForMatch(lp, entrantId, mode, opponentEntrantId, opponentLabel, round, payloads, context);
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
            // An entrant id is a stable companion GUID, so it belongs to at most one
            // of the two systems — try the bracket, then the invite board.
            if (TournamentService.FindEntrant(entrantId) != null) TournamentService.UpdateEscrow(entrantId, payloads);
            else DuelInviteService.UpdateEscrow(entrantId, payloads);
        }

        // ---- Duel invites (docs/Wagers.md) -----------------------------------
        //
        // One client->server RPC carrying an action verb, rather than four
        // near-identical ones: post / accept / ready / withdraw all move the same
        // fields (an invite id, a totem payload, a stake), and a single pipe keeps
        // the registration list and the argument order in one place.
        public static void SendInviteAction(string action, string inviteId, string currency,
            string entrantId, string label, int level, int caste, string payload, int clientPaidCoins)
        {
            var lp = Player.m_localPlayer;
            if (lp == null || ZRoutedRpc.instance == null) return;
            var pkg = new ZPackage();
            pkg.Write(action ?? string.Empty);
            pkg.Write(inviteId ?? string.Empty);
            pkg.Write(currency ?? string.Empty);
            pkg.Write(lp.GetPlayerID());
            pkg.Write(lp.GetPlayerName() ?? string.Empty);
            pkg.Write(entrantId ?? string.Empty);
            pkg.Write(label ?? string.Empty);
            pkg.Write(level);
            pkg.Write(caste);
            pkg.Write(payload ?? string.Empty);
            pkg.Write(clientPaidCoins);

            if (ZNet.instance != null && ZNet.instance.IsServer()) { HandleInvite(0L, pkg); return; }
            ZRoutedRpc.instance.InvokeRoutedRPC(ZRoutedRpc.instance.GetServerPeerID(), RpcInvite, pkg);
        }

        private static void OnInvite(long sender, ZPackage pkg) => HandleInvite(sender, pkg);

        private static void HandleInvite(long sender, ZPackage pkg)
        {
            if (ZNet.instance == null || !ZNet.instance.IsServer()) return;
            pkg.SetPos(0);
            var action = pkg.ReadString();
            var inviteId = pkg.ReadString();
            var currency = pkg.ReadString();
            var ownerId = pkg.ReadLong();
            var ownerName = pkg.ReadString();
            var entrantId = pkg.ReadString();
            var label = pkg.ReadString();
            var level = pkg.ReadInt();
            var caste = pkg.ReadInt();
            var payload = pkg.ReadString();
            var paidCoins = pkg.ReadInt();

            // A rejected post/accept has to hand back BOTH what the client already
            // gave up: the totem it removed and any Coin stake it took itself.
            void Reject(string message)
            {
                Reply(sender, message);
                if (!string.IsNullOrEmpty(payload))
                    ReturnEscrowToOwners(new System.Collections.Generic.Dictionary<long, System.Collections.Generic.List<string>>
                    {
                        { ownerId, new System.Collections.Generic.List<string> { payload } },
                    });
                if (paidCoins > 0) SendWagerPayout(ownerId, paidCoins, "stake refunded");
            }

            switch (action)
            {
                case "post":
                    DuelInviteService.Post(Economy.Wager.Parse(currency), ownerId, ownerName,
                        entrantId, label, level, caste, payload, paidCoins,
                        (ok, msg) => { if (ok) Reply(sender, msg); else Reject(msg); });
                    break;

                case "accept":
                    DuelInviteService.Accept(inviteId, ownerId, ownerName,
                        entrantId, label, level, caste, payload, paidCoins,
                        (ok, msg) => { if (ok) Reply(sender, msg); else Reject(msg); });
                    break;

                case "ready":
                    Reply(sender, DuelInviteService.SetReady(inviteId, ownerId));
                    break;

                case "withdraw":
                {
                    var refunds = DuelInviteService.Withdraw(inviteId, ownerId, out var status);
                    Reply(sender, status);
                    var byOwner = new System.Collections.Generic.Dictionary<long, System.Collections.Generic.List<string>>();
                    foreach (var kv in refunds)
                    {
                        if (!byOwner.TryGetValue(kv.Key, out var l)) { l = new System.Collections.Generic.List<string>(); byOwner[kv.Key] = l; }
                        l.Add(kv.Value);
                    }
                    if (byOwner.Count > 0) ReturnEscrowToOwners(byOwner);
                    break;
                }

                default:
                    Reply(sender, $"Unknown invite action '{action}'.");
                    break;
            }
        }

        public static void BroadcastInvites()
        {
            if (ZRoutedRpc.instance == null) return;
            var pkg = new ZPackage();
            pkg.Write(DuelInviteService.SerializeSnapshot());
            ZRoutedRpc.instance.InvokeRoutedRPC(0L, RpcInvitePush, pkg);
        }

        private static void SendInvitesToPeer(long peer)
        {
            if (ZRoutedRpc.instance == null) return;
            var pkg = new ZPackage();
            pkg.Write(DuelInviteService.SerializeSnapshot());
            ZRoutedRpc.instance.InvokeRoutedRPC(peer, RpcInvitePush, pkg);
        }

        private static void OnInvitePush(long sender, ZPackage pkg)
        {
            if (ZNet.instance != null && ZNet.instance.IsServer()) return;
            DuelInviteService.ApplySnapshot(pkg.ReadString());
        }

        public static void RequestInvites()
        {
            if (ZRoutedRpc.instance == null) return;
            if (ZNet.instance != null && ZNet.instance.IsServer()) return;
            ZRoutedRpc.instance.InvokeRoutedRPC(ZRoutedRpc.instance.GetServerPeerID(), RpcInviteReq, string.Empty);
        }

        private static void OnInviteRequest(long sender, string _)
        {
            if (ZNet.instance == null || !ZNet.instance.IsServer()) return;
            SendInvitesToPeer(sender);
        }

        // Every status line here is the answer to something the player did on the
        // F7 panel — a join ack, an admin ack, a refund. They go TOP-LEFT because a
        // centred MessageHud line renders behind that panel's canvas and would be
        // invisible exactly when it matters most. See TournamentRegistration.Msg.
        private static void ShowLocal(string msg)
        {
            if (!string.IsNullOrEmpty(msg) && MessageHud.instance != null)
                MessageHud.instance.ShowMessage(MessageHud.MessageType.TopLeft, msg);
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
                    var eliminationType = p.Length > 3 ? p[3] : "single";
                    return TournamentService.Start(mode, size, eliminationType);
                }
                // Every tournament action now names the slot it applies to. The slot
                // is the LAST argument on each of these so an older command string
                // (no slot) still parses and falls through to the free tournament,
                // which is the only one those commands could ever have meant.
                case "begin":   return TournamentService.Begin(Slot(p, 1));
                case "cancel":  return TournamentService.Cancel(Slot(p, 1));
                case "forfeit": return p.Length > 1 ? TournamentService.Forfeit(Slot(p, 2), p[1]) : "Usage: forfeit <name>.";
                case "release":
                {
                    if (p.Length < 2) return "Usage: release <entrant name>.";
                    var payloads = TournamentService.ReleaseEntrant(Slot(p, 2), p[1], out var ownerId, out var status);
                    if (payloads.Count > 0)
                        ReturnEscrowToOwners(new System.Collections.Generic.Dictionary<long, System.Collections.Generic.List<string>>
                        {
                            { ownerId, payloads },
                        });
                    return status;
                }
                case "activate": return TournamentService.ActivateCurrentRound(Slot(p, 1));
                // Season resets run here rather than in the console command itself:
                // the stores are server-owned, so a remote admin's client can't touch
                // them directly. Routing through this admin-authenticated path is what
                // lets an admin reset a season without being sat at the host.
                case "season":
                {
                    int archived = LeaderboardStore.SeasonReset();
                    BroadcastTable();
                    return $"Duel season reset — archived {archived} record(s).";
                }
                case "bountyseason":
                {
                    int archived = Bounty.BountyLeaderboardStore.SeasonReset();
                    Bounty.BountySync.BroadcastLadder();
                    return $"Bounty season reset — archived {archived} hunter record(s).";
                }
                default:        return $"Unknown admin action '{(p.Length > 0 ? p[0] : "")}'.";
            }
        }

        // Optional trailing slot argument; absent means the free admin tournament.
        private static string Slot(string[] parts, int index)
            => parts != null && parts.Length > index ? parts[index] : string.Empty;

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
                    DuelInviteService.LoadForCurrentWorld();
                    TournamentService.SerializerSelfTest();
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
                SendInvitesToPeer(peer.m_uid);
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
                RequestInvites();
                RequestAdminStatus();
            }
        }
    }
}
