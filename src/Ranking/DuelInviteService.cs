using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using LostScrollsII.Economy;
using LostScrollsII.Integration;
using UnityEngine;

namespace LostScrollsII.Ranking
{
    // Server-authoritative board of staked 1v1 challenges (docs/Wagers.md).
    //
    // A player posts an invite by staking (10 Valcoins or 100 Coins) and locking a
    // companion's Communion Totem into it. Anyone may accept by matching the stake
    // and locking their own. Both totems are held; when BOTH owners mark themselves
    // ready — wherever in the world they have agreed to meet — the two companions
    // are summoned there in duel mode and fight. The winner takes both stakes and
    // both totems come home.
    //
    // Relationship to the tournament runner: the two are separate on purpose. They
    // share the escrow/summon/reseal plumbing (LeaderboardSync + TournamentClient,
    // which is context-tagged so it can drive either), but nothing else — an invite
    // has no bracket, no rounds, no seeding, and a completely different lifetime.
    // Folding invites into TournamentState would have meant a "tournament" with one
    // match and a pile of unused fields.
    //
    // Every player may hold ONE invite at a time, as poster or challenger ("no dual
    // invites"), but any number of players may have one open simultaneously.
    //
    // The result arrives through the SAME duel report the ladder and the tournament
    // use (NotifyDuelResult), so an invite duel is an ordinary duel underneath.
    public static class DuelInviteService
    {
        private static DuelInviteBoard _board = new DuelInviteBoard();     // server authoritative
        private static DuelInviteBoard _snapshot = new DuelInviteBoard();  // client view

        public static DuelInviteBoard Snapshot => _snapshot;

        public static IEnumerable<DuelInvite> All
            => (_snapshot?.invites ?? new List<DuelInvite>()).Where(i => i != null);

        /// The invite the local/queried player is tied up in, from the client view.
        public static DuelInvite MineIn(long ownerId) => _snapshot?.ForOwner(ownerId);

        public static DuelInvite FindByEntrant(string entrantId) => _snapshot?.ForEntrant(entrantId);

        // ---- persistence ------------------------------------------------------

        private static string BoardPath()
            => Path.Combine(LeaderboardStore.DataDir(), $"duelinvites.{LeaderboardStore.WorldTag()}.json");

        public static void LoadForCurrentWorld()
        {
            try
            {
                var path = BoardPath();
                _board = File.Exists(path)
                    ? (CompetitiveJson.ReadInvites(File.ReadAllText(path)) ?? new DuelInviteBoard())
                    : new DuelInviteBoard();
                if (_board.invites == null) _board.invites = new List<DuelInvite>();
                Plugin.Log.LogInfo($"[invite] resumed {_board.invites.Count} duel invite(s).");
            }
            catch (Exception e) { _board = new DuelInviteBoard(); Plugin.Log.LogWarning($"[invite] load failed: {e.Message}"); }
            _snapshot = _board;
        }

        private static void Save()
        {
            try { File.WriteAllText(BoardPath(), CompetitiveJson.Write(_board, true)); }
            catch (Exception e) { Plugin.Log.LogWarning($"[invite] save failed: {e.Message}"); }
        }

        public static string SerializeSnapshot() => CompetitiveJson.Write(_board ?? new DuelInviteBoard());

        public static void ApplySnapshot(string json)
        {
            try
            {
                var b = CompetitiveJson.ReadInvites(json) ?? new DuelInviteBoard();
                if (b.invites == null) b.invites = new List<DuelInvite>();
                _snapshot = b;
            }
            catch (Exception e) { Plugin.Log.LogWarning($"[invite] snapshot apply failed: {e.Message}"); }
        }

        // ---- posting ----------------------------------------------------------

        // Post a challenge. Asynchronous for the same reason tournament entry is: a
        // Valcoin stake is a remote ledger call and the invite must not appear on
        // the board until it has actually settled.
        public static void Post(WagerCurrency currency, long hostId, string hostName,
            string entrantId, string label, int level, int caste, string payload,
            int clientPaidCoins, Action<bool, string> done)
        {
            if (!WagerService.IsAvailable(currency, out var why)) { done?.Invoke(false, why); return; }
            if (string.IsNullOrEmpty(entrantId)) { done?.Invoke(false, "That totem has no ladder identity and can't be staked."); return; }
            if (string.IsNullOrEmpty(payload)) { done?.Invoke(false, "No companion totem was locked in."); return; }

            var existing = _board.ForOwner(hostId);
            if (existing != null)
            {
                done?.Invoke(false, existing.hostId == hostId
                    ? "You already have a duel invite posted — withdraw it first."
                    : "You have already accepted someone else's duel invite.");
                return;
            }

            int stake = Wager.DuelStake(currency);
            WagerService.Charge(currency, hostName, stake, ValcoinBridge.SkuDuelStake,
                "posted a duel invite", clientPaidCoins, (ok, msg) =>
                {
                    if (!ok) { done?.Invoke(false, msg ?? "The stake could not be taken."); return; }

                    // The player may have been drawn into someone else's invite while
                    // the ledger was answering; one invite per player is the rule, so
                    // refund rather than post a second.
                    if (_board.ForOwner(hostId) != null)
                    {
                        WagerService.RollBackOwnCharge(currency, hostId, hostName, stake, "you were already in another duel invite");
                        done?.Invoke(false, "You joined another duel invite while your stake was being processed — it is returned.");
                        return;
                    }

                    var invite = new DuelInvite
                    {
                        id = Guid.NewGuid().ToString("N").Substring(0, 12),
                        currency = Wager.Key(currency),
                        stake = stake,
                        phase = "open",
                        hostId = hostId,
                        hostName = hostName ?? string.Empty,
                        hostEntrantId = entrantId,
                        hostLabel = label ?? string.Empty,
                        hostLevel = level,
                        hostCaste = caste,
                        hostPayload = payload,
                        createdTicks = DateTime.UtcNow.Ticks,
                    };
                    _board.invites.Add(invite);
                    Save();
                    Broadcast();

                    ServerGuideBridge.AnnounceDiscord(
                        $"📣 **{hostName}** has posted a duel invite — **{label}**" +
                        (level > 0 ? $" (Lv{level})" : "") +
                        $" · stake **{stake} {Wager.Display(currency)}**, winner takes **{stake * 2}**. " +
                        "Accept it from the Tournament panel (F7).");
                    Plugin.Log.LogInfo($"[invite] {hostName} posted '{invite.id}' ({stake} {invite.currency}).");

                    done?.Invoke(true, $"Duel invite posted — {stake} {Wager.Display(currency)} staked. " +
                                       "The winner takes both stakes.");
                });
        }

        // Accept someone else's open invite, matching their stake.
        public static void Accept(string inviteId, long oppId, string oppName,
            string entrantId, string label, int level, int caste, string payload,
            int clientPaidCoins, Action<bool, string> done)
        {
            var invite = _board.Find(inviteId);
            if (invite == null || invite.phase != "open") { done?.Invoke(false, "That duel invite is no longer open."); return; }
            if (invite.hostId == oppId) { done?.Invoke(false, "You cannot accept your own invite."); return; }
            if (_board.ForOwner(oppId) != null) { done?.Invoke(false, "You are already tied up in a duel invite."); return; }
            if (string.IsNullOrEmpty(entrantId)) { done?.Invoke(false, "That totem has no ladder identity and can't be staked."); return; }
            if (string.IsNullOrEmpty(payload)) { done?.Invoke(false, "No companion totem was locked in."); return; }

            var currency = Wager.Parse(invite.currency);
            if (!WagerService.IsAvailable(currency, out var why)) { done?.Invoke(false, why); return; }

            int stake = invite.stake;
            WagerService.Charge(currency, oppName, stake, ValcoinBridge.SkuDuelStake,
                "accepted a duel invite", clientPaidCoins, (ok, msg) =>
                {
                    if (!ok) { done?.Invoke(false, msg ?? "The stake could not be taken."); return; }

                    // Re-check: the invite may have been taken or withdrawn while the
                    // ledger was answering. If so, give the stake straight back.
                    // (Read it back off the board, not through the captured
                    // reference — a withdrawn invite is REMOVED, not mutated.)
                    invite = _board.Find(inviteId);
                    if (invite == null || invite.phase != "open" || _board.ForOwner(oppId) != null)
                    {
                        WagerService.RollBackOwnCharge(currency, oppId, oppName, stake, "the invite was already taken");
                        done?.Invoke(false, "That duel invite was taken while your stake was being processed.");
                        return;
                    }

                    invite.phase = "matched";
                    invite.oppId = oppId;
                    invite.oppName = oppName ?? string.Empty;
                    invite.oppEntrantId = entrantId;
                    invite.oppLabel = label ?? string.Empty;
                    invite.oppLevel = level;
                    invite.oppCaste = caste;
                    invite.oppPayload = payload;
                    Save();
                    Broadcast();

                    ServerGuideBridge.AnnounceDiscord(
                        $"🤝 **{oppName}** has accepted **{invite.hostName}**'s duel invite — " +
                        $"**{invite.hostLabel}** vs **{label}** for **{invite.stake * 2} {Wager.Display(currency)}**. " +
                        "The two of them choose where to fight; the duel starts once both are ready.");

                    done?.Invoke(true, $"Challenge accepted — meet {invite.hostName} and press Ready to Fight.");
                });
        }

        // Both sides must say "ready" wherever they have met up. Only then are the
        // companions summoned, so nobody is dragged into a fight on the other
        // player's terrain.
        public static string SetReady(string inviteId, long ownerId)
        {
            var invite = _board.Find(inviteId);
            if (invite == null) return "that duel invite no longer exists.";
            if (invite.phase == "running") return "the duel is already under way.";
            if (invite.phase != "matched") return "nobody has accepted that invite yet.";

            if (invite.hostId == ownerId) invite.hostReady = true;
            else if (invite.oppId == ownerId) invite.oppReady = true;
            else return "you are not part of that duel.";

            if (!invite.hostReady || !invite.oppReady)
            {
                Save();
                Broadcast();
                var waitingFor = invite.hostId == ownerId ? invite.oppName : invite.hostName;
                return $"Ready. Waiting for {waitingFor}.";
            }

            invite.phase = "running";
            Save();
            Broadcast();

            // Summon both companions beside their own owners, each locked onto the
            // other so a nearby unrelated duel can't be cross-targeted.
            LeaderboardSync.SummonForMatch(invite.hostId, invite.hostEntrantId, "1v1",
                invite.oppEntrantId, invite.oppLabel, 0, new List<string> { invite.hostPayload },
                LeaderboardSync.ContextInvite);
            LeaderboardSync.SummonForMatch(invite.oppId, invite.oppEntrantId, "1v1",
                invite.hostEntrantId, invite.hostLabel, 0, new List<string> { invite.oppPayload },
                LeaderboardSync.ContextInvite);

            ServerGuideBridge.AnnounceDiscord(
                $"🥊 **The staked duel begins:** {invite.hostLabel} ({invite.hostName}) vs " +
                $"{invite.oppLabel} ({invite.oppName}) — **{invite.stake * 2} {Wager.Display(Wager.Parse(invite.currency))}** on the line.");
            return "Both fighters are ready — the duel begins!";
        }

        // Withdraw an invite: the poster cancels an unaccepted one, or either side
        // pulls out of a matched-but-not-started one. Everyone gets their stake and
        // their totem back. Refused once the duel is running — at that point the
        // wager is live and only a result settles it.
        public static List<KeyValuePair<long, string>> Withdraw(string inviteId, long ownerId, out string status)
        {
            var refundPayloads = new List<KeyValuePair<long, string>>();
            status = "that duel invite no longer exists.";
            var invite = _board.Find(inviteId);
            if (invite == null) return refundPayloads;
            if (!invite.Involves(ownerId)) { status = "that is not your duel invite."; return refundPayloads; }
            if (invite.phase == "running") { status = "the duel is already under way — fight it out."; return refundPayloads; }

            var currency = Wager.Parse(invite.currency);

            if (invite.hostId == ownerId)
            {
                // The poster pulls out: the whole invite is torn down and BOTH sides
                // are made whole (the challenger did nothing wrong).
                if (!string.IsNullOrEmpty(invite.hostPayload)) refundPayloads.Add(new KeyValuePair<long, string>(invite.hostId, invite.hostPayload));
                WagerService.Refund(currency, invite.hostId, invite.hostName, invite.stake, "withdrew a duel invite");
                if (invite.phase == "matched")
                {
                    if (!string.IsNullOrEmpty(invite.oppPayload)) refundPayloads.Add(new KeyValuePair<long, string>(invite.oppId, invite.oppPayload));
                    WagerService.Refund(currency, invite.oppId, invite.oppName, invite.stake, "the poster withdrew");
                }
                _board.invites.Remove(invite);
                status = "Duel invite withdrawn — your stake and totem are returned.";
                ServerGuideBridge.AnnounceDiscord($"🚫 **{invite.hostName}** withdrew their duel invite. All stakes returned.");
            }
            else
            {
                // The challenger backs out: the invite goes back on the board so
                // somebody else can take it.
                if (!string.IsNullOrEmpty(invite.oppPayload)) refundPayloads.Add(new KeyValuePair<long, string>(invite.oppId, invite.oppPayload));
                WagerService.Refund(currency, invite.oppId, invite.oppName, invite.stake, "backed out of a duel");
                var name = invite.oppName;
                invite.phase = "open";
                invite.oppId = 0L; invite.oppName = ""; invite.oppEntrantId = ""; invite.oppLabel = "";
                invite.oppPayload = ""; invite.oppLevel = 0; invite.oppCaste = 0;
                invite.hostReady = false; invite.oppReady = false;
                status = "You have backed out — your stake and totem are returned.";
                ServerGuideBridge.AnnounceDiscord($"↩️ **{name}** backed out of **{invite.hostName}**'s duel invite — it is open again.");
            }

            Save();
            Broadcast();
            return refundPayloads;
        }

        // ---- result intake ----------------------------------------------------

        // Called from LeaderboardSync alongside the tournament's own hook, on the
        // same duel report. Returns true if this result settled an invite.
        public static bool NotifyDuelResult(string winnerEntrantId, string loserEntrantId)
        {
            var invite = _board.ForEntrant(winnerEntrantId);
            if (invite == null || invite.phase != "running") return false;
            if (!invite.HasEntrant(loserEntrantId)) return false;

            bool hostWon = invite.IsHostEntrant(winnerEntrantId);
            long winnerOwner = hostWon ? invite.hostId : invite.oppId;
            string winnerName = hostWon ? invite.hostName : invite.oppName;
            string winnerLabel = hostWon ? invite.hostLabel : invite.oppLabel;
            string loserName = hostWon ? invite.oppName : invite.hostName;
            string loserLabel = hostWon ? invite.oppLabel : invite.hostLabel;

            var currency = Wager.Parse(invite.currency);
            int purse = invite.stake * 2;

            // The purse is exactly the two stakes that were collected — a transfer,
            // not a mint — so it is paid directly rather than through the donations
            // quest table the tournament prize has to use.
            WagerService.Pay(currency, winnerOwner, winnerName, purse,
                ValcoinBridge.SkuDuelPurse, "won a staked duel");

            ServerGuideBridge.AnnounceDiscord(
                $"🏆 **{winnerName}** wins the staked duel — **{winnerLabel}** defeats **{loserLabel}** ({loserName}) " +
                $"and takes **{purse} {Wager.Display(currency)}**.");
            Plugin.Log.LogInfo($"[invite] '{invite.id}' settled: {winnerName} takes {purse} {invite.currency}.");

            // The companions are resealed and despawned by their own clients
            // (TournamentClient sees the invite leave "running"); the totems come
            // back through the normal escrow return once those reseals land.
            invite.phase = "complete";
            invite.completedTicks = DateTime.UtcNow.Ticks;
            Save();
            Broadcast();
            return true;
        }

        // A client resealing its companion after an invite duel. Mirrors
        // TournamentService.UpdateEscrow.
        public static bool UpdateEscrow(string entrantId, List<string> payloads)
        {
            if (payloads == null || payloads.Count == 0) return false;
            var invite = _board.ForEntrant(entrantId);
            if (invite == null) return false;
            if (invite.IsHostEntrant(entrantId)) invite.hostPayload = payloads[0];
            else invite.oppPayload = payloads[0];
            Save();
            return true;
        }

        // ---- upkeep (server, ~1 Hz) ------------------------------------------

        // Returns both totems on a completed invite, and expires an open one that
        // nobody ever accepted (refunding the poster). Completed invites are cleared
        // one tick after resolution so the resealed payloads written by the two
        // clients are the ones handed back.
        // Matches TournamentService's grace, and for the same reason.
        private const double CompletionGraceSeconds = 6.0;

        public static void Tick()
        {
            if (_board?.invites == null) return;
            bool dirty = false;

            foreach (var invite in _board.invites.ToList())
            {
                if (invite == null) { _board.invites.Remove(invite); dirty = true; continue; }

                if (invite.phase == "complete")
                {
                    // Hold briefly so both clients can reseal their live companions
                    // first — otherwise the winner gets its pre-duel state back.
                    if (invite.completedTicks > 0 &&
                        (DateTime.UtcNow - new DateTime(invite.completedTicks, DateTimeKind.Utc)).TotalSeconds < CompletionGraceSeconds)
                        continue;

                    var byOwner = new Dictionary<long, List<string>>();
                    void Add(long owner, string payload)
                    {
                        if (owner == 0L || string.IsNullOrEmpty(payload)) return;
                        if (!byOwner.TryGetValue(owner, out var l)) { l = new List<string>(); byOwner[owner] = l; }
                        l.Add(payload);
                    }
                    Add(invite.hostId, invite.hostPayload);
                    Add(invite.oppId, invite.oppPayload);
                    LeaderboardSync.ReturnEscrowToOwners(byOwner);
                    _board.invites.Remove(invite);
                    dirty = true;
                    continue;
                }

                if (invite.phase != "open") continue;
                float minutes = Plugin.WageredRegistrationMinutes.Value;
                if (minutes <= 0f || invite.createdTicks <= 0) continue;
                var age = DateTime.UtcNow - new DateTime(invite.createdTicks, DateTimeKind.Utc);
                if (age.TotalMinutes < minutes) continue;

                var currency = Wager.Parse(invite.currency);
                WagerService.Refund(currency, invite.hostId, invite.hostName, invite.stake, "the duel invite expired");
                if (!string.IsNullOrEmpty(invite.hostPayload))
                    LeaderboardSync.ReturnEscrowToOwners(new Dictionary<long, List<string>>
                    {
                        { invite.hostId, new List<string> { invite.hostPayload } },
                    });
                _board.invites.Remove(invite);
                dirty = true;
                ServerGuideBridge.AnnounceDiscord($"⌛ **{invite.hostName}**'s duel invite expired unanswered — stake and totem returned.");
            }

            if (dirty) { Save(); Broadcast(); }
        }

        private static void Broadcast() => LeaderboardSync.BroadcastInvites();
    }
}
