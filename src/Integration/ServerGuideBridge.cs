using System;
using BepInEx.Bootstrap;
using LostScrollsII.Companions;

namespace LostScrollsII.Integration
{
    // Optional integration with the sibling ValheimServerGuide mod — see
    // docs/ServerGuide-Integration.md. All calls are no-ops if ServerGuide isn't
    // loaded, so LostScrollsII's own mechanics never depend on it being present.
    //
    // Calls into ServerGuide.Triggers.GuidanceDispatcher are isolated in their own
    // methods (RaiseRecruitedInternal etc.) and only invoked after IsLoaded is
    // confirmed true — .NET JITs a method's IL lazily, so as long as those internal
    // methods are never called when the ServerGuide assembly is absent, referencing
    // its types here doesn't throw even on a machine without ServerGuide installed.
    public static class ServerGuideBridge
    {
        private const string ServerGuideGuid = "com.valheimserverguide";

        public static bool IsLoaded => Chainloader.PluginInfos.ContainsKey(ServerGuideGuid);

        // ---- Direct Discord announcements (SERVER side) ----------------------
        //
        // Competitive events are server-authoritative and mostly have no single
        // "player it happened to" — a bracket draw, a pairing, an end-of-event
        // summary. Routing those through a `type: discord` reward would mean
        // inventing a per-player trigger for each one and firing it on some
        // arbitrary client, which is both awkward and wrong.
        //
        // ServerGuide already exposes exactly the right primitive:
        // DiscordAnnouncer.AnnounceRaw is public, server-side, and reads the
        // webhook URL from the server's own config (it is what the `discord` reward
        // ultimately calls after its RPC hop). So the wager/tournament/duel-invite
        // announcements post straight from the server with no new trigger types, no
        // new template variables, and no ServerGuide release needed.
        //
        // Per-player rewards (a champion's prize, a rank milestone) still go through
        // the trigger + guidance path, because those DO target one player.
        //
        // No-ops with a log line when ServerGuide is absent or the webhook is unset.
        public static void AnnounceDiscord(string message)
        {
            if (!IsLoaded || string.IsNullOrEmpty(message)) return;
            // Only the authoritative instance posts, or every connected client would
            // fire the same webhook.
            if (ZNet.instance == null || !ZNet.instance.IsServer()) return;
            try { AnnounceDiscordInternal(message); }
            catch (Exception e) { Plugin.Log.LogWarning($"ServerGuide integration (discord announce) failed: {e}"); }
        }

        private static void AnnounceDiscordInternal(string message)
        {
            ValheimServerGuide.Discord.DiscordAnnouncer.AnnounceRaw(message);
        }

        // Fired on the CHAMPION's client when a wagered tournament pays out in a
        // currency this mod is not allowed to price (Valcoins). The guidance entry
        // behind it grants the purse by setting the donations mod's
        // `VC.Q.ls_tournament_prize` key, so the amount lives in that server's
        // valcoin_quests.yaml and never travels from here — the same discipline the
        // bounty Valcoin payout follows. Subject = the currency key.
        public static void RaiseTournamentPrize(string currency, int amount)
        {
            if (!IsLoaded) return;
            try
            {
                RaiseSimple("dvergr_tournament_prize", currency ?? string.Empty,
                    new System.Collections.Generic.Dictionary<string, object>
                    {
                        { "mode", currency ?? string.Empty },
                        // Display only — the guidance entry decides what is actually
                        // granted; this just lets the message name a figure.
                        { "bracketSize", amount },
                    });
            }
            catch (Exception e) { Plugin.Log.LogWarning($"ServerGuide integration (dvergr_tournament_prize) failed: {e}"); }
        }

        public static void RaiseRecruited(DvergrCaste caste)
        {
            if (!IsLoaded) return;
            try { RaiseRecruitedInternal(caste); }
            catch (Exception e) { Plugin.Log.LogWarning($"ServerGuide integration (dvergr_recruited) failed: {e}"); }
        }

        public static void RaiseLevelUp(DvergrCaste caste, int level)
        {
            if (!IsLoaded) return;
            try { RaiseLevelUpInternal(caste, level); }
            catch (Exception e) { Plugin.Log.LogWarning($"ServerGuide integration (dvergr_level_up) failed: {e}"); }
        }

        public static void RaiseDuelWon(DvergrCaste winner, DvergrCaste loser,
            string winnerName = null, string loserName = null)
        {
            if (!IsLoaded) return;
            try { RaiseDuelWonInternal(winner, loser, winnerName, loserName); }
            catch (Exception e) { Plugin.Log.LogWarning($"ServerGuide integration (dvergr_duel_won) failed: {e}"); }
        }

        // Fired on the winner's client when a companion climbs into the top ranks of
        // the duel ladder (docs/Ranking.md). Subject = winner caste; Extra carries
        // rank/rating/companion/owner for templating and reward gating.
        public static void RaiseRankChanged(int caste, int rank, int rating, string companionName, string ownerName)
        {
            if (!IsLoaded) return;
            try { RaiseRankChangedInternal(caste, rank, rating, companionName, ownerName); }
            catch (Exception e) { Plugin.Log.LogWarning($"ServerGuide integration (dvergr_rank_changed) failed: {e}"); }
        }

        // Fired on the winner's client the moment a companion reaches #1 on the duel
        // ladder (docs/Ranking.md). Distinct from dvergr_rank_changed (top-3) so a
        // guidance entry can attach a "new champion" Discord announcement without a
        // numeric filter. Same Extra fields as RaiseRankChanged.
        public static void RaiseRankFirst(int caste, int rating, string companionName, string ownerName)
        {
            if (!IsLoaded) return;
            try
            {
                RaiseSimple("dvergr_rank_first", ((DvergrCaste)caste).ToString(),
                    new System.Collections.Generic.Dictionary<string, object>
                    {
                        { "rank", 1 },
                        { "rating", rating },
                        { "companionName", companionName ?? string.Empty },
                        { "ownerName", ownerName ?? string.Empty },
                    });
            }
            catch (Exception e) { Plugin.Log.LogWarning($"ServerGuide integration (dvergr_rank_first) failed: {e}"); }
        }

        // Fired on the winner's client the moment a party reaches #1 on the party
        // ladder. Carries the party/owner name for templating.
        public static void RaisePartyRankFirst(string ownerName, int rating, string partyName)
        {
            if (!IsLoaded) return;
            try
            {
                RaiseSimple("dvergr_party_rank_first", ownerName ?? string.Empty,
                    new System.Collections.Generic.Dictionary<string, object>
                    {
                        { "rank", 1 },
                        { "rating", rating },
                        { "ownerName", ownerName ?? string.Empty },
                        { "partyName", string.IsNullOrEmpty(partyName) ? (ownerName ?? string.Empty) : partyName },
                    });
            }
            catch (Exception e) { Plugin.Log.LogWarning($"ServerGuide integration (dvergr_party_rank_first) failed: {e}"); }
        }

        // Fired on the winner's client when a party duel resolves (docs/Party-Duels.md).
        // Subject = winning owner name; Extra carries team size / opponent / MVP.
        public static void RaisePartyDuelWon(string winnerOwnerName, int winSize, string loserOwnerName,
            int mvpCaste, string partyName = null)
        {
            if (!IsLoaded) return;
            try { RaisePartyDuelWonInternal(winnerOwnerName, winSize, loserOwnerName, mvpCaste, partyName); }
            catch (Exception e) { Plugin.Log.LogWarning($"ServerGuide integration (dvergr_party_duel_won) failed: {e}"); }
        }

        // Fired on the winner's client when a party climbs into the top party ranks.
        public static void RaisePartyRankChanged(string ownerName, int rank, int rating, string partyName = null)
        {
            if (!IsLoaded) return;
            try { RaisePartyRankChangedInternal(ownerName, rank, rating, partyName); }
            catch (Exception e) { Plugin.Log.LogWarning($"ServerGuide integration (dvergr_party_rank_changed) failed: {e}"); }
        }

        // Bounty hunting (docs/Bounty-Hunting.md). Fired on the hunter's own client so
        // ServerGuide's rewards land on the right player. Subject = the effective tier
        // as a string, which the `tier:` YAML filter matches, so one guidance entry per
        // tier carries its own reward bundle and NO loot table lives in this mod.
        public static void RaiseBountyResolved(int tier, string tierName, string biome, string method)
        {
            if (!IsLoaded) return;
            try
            {
                RaiseSimple("dvergr_bounty_resolved", tier.ToString(),
                    new System.Collections.Generic.Dictionary<string, object>
                    {
                        { "tier", tier },
                        { "tierName", tierName ?? string.Empty },
                        { "bountyBiome", biome ?? string.Empty },
                        // "killed" or "communed" — the rite is a legitimate way to close
                        // a bounty (requirement 1), so reward text can acknowledge it.
                        { "method", method ?? string.Empty },
                    });
            }
            catch (Exception e) { Plugin.Log.LogWarning($"ServerGuide integration (dvergr_bounty_resolved) failed: {e}"); }
        }

        // Fired ONLY when the Valcoin payout roll already succeeded — the chance is
        // decided in this mod (off the player's duel/tournament standing), so the
        // guidance entry behind this trigger just grants the coins. Its only reward
        // should be the `set_player_key: VC.Q.<id>` bridge that Valheim Donations
        // reads; that mod owns the actual ledger and the payout amount, so no coin
        // value is ever sent from here (docs/Bounty-Hunting.md, Dependency 3).
        public static void RaiseBountyValcoin(int tier, string tierName)
        {
            if (!IsLoaded) return;
            try
            {
                RaiseSimple("dvergr_bounty_valcoin", tier.ToString(),
                    new System.Collections.Generic.Dictionary<string, object>
                    {
                        { "tier", tier },
                        { "tierName", tierName ?? string.Empty },
                    });
            }
            catch (Exception e) { Plugin.Log.LogWarning($"ServerGuide integration (dvergr_bounty_valcoin) failed: {e}"); }
        }

        // Tournament triggers (docs/Tournaments.md). Subject = caste name (1v1) or
        // "party". Fired on the relevant player's client so any reward lands there.
        public static void RaiseTournamentJoined(string subject)
        {
            if (!IsLoaded) return;
            try { RaiseSimple("dvergr_tournament_joined", subject, null); }
            catch (Exception e) { Plugin.Log.LogWarning($"ServerGuide integration (dvergr_tournament_joined) failed: {e}"); }
        }

        public static void RaiseTournamentMatch(string subject, int round, string opponent)
        {
            if (!IsLoaded) return;
            try
            {
                RaiseSimple("dvergr_tournament_match", subject, new System.Collections.Generic.Dictionary<string, object>
                {
                    { "round", round }, { "opponent", opponent ?? string.Empty },
                });
            }
            catch (Exception e) { Plugin.Log.LogWarning($"ServerGuide integration (dvergr_tournament_match) failed: {e}"); }
        }

        public static void RaiseTournamentWon(string subject, string mode, int bracketSize)
        {
            if (!IsLoaded) return;
            try
            {
                RaiseSimple("dvergr_tournament_won", subject, new System.Collections.Generic.Dictionary<string, object>
                {
                    { "mode", mode ?? string.Empty }, { "bracketSize", bracketSize },
                });
            }
            catch (Exception e) { Plugin.Log.LogWarning($"ServerGuide integration (dvergr_tournament_won) failed: {e}"); }
        }

        private static void RaiseRecruitedInternal(DvergrCaste caste)
        {
            ValheimServerGuide.Triggers.GuidanceDispatcher.Raise(new ValheimServerGuide.Triggers.TriggerEvent
            {
                Type = "dvergr_recruited",
                Subject = caste.ToString(),
            });
        }

        private static void RaiseLevelUpInternal(DvergrCaste caste, int level)
        {
            ValheimServerGuide.Triggers.GuidanceDispatcher.Raise(new ValheimServerGuide.Triggers.TriggerEvent
            {
                Type = "dvergr_level_up",
                Subject = $"{caste}:{level}",
            });
        }

        private static void RaiseDuelWonInternal(DvergrCaste winner, DvergrCaste loser,
            string winnerName, string loserName)
        {
            ValheimServerGuide.Triggers.GuidanceDispatcher.Raise(new ValheimServerGuide.Triggers.TriggerEvent
            {
                Type = "dvergr_duel_won",
                Subject = winner.ToString(),
                Extra = new System.Collections.Generic.Dictionary<string, object>
                {
                    { "loserCaste", loser.ToString() },
                    // Reuse the {companionName}/{opponent} templating vars so a duel-won
                    // guidance entry (incl. a Discord broadcast) can name both duelists.
                    { "companionName", winnerName ?? string.Empty },
                    { "opponent", loserName ?? string.Empty },
                },
            });
        }

        private static void RaiseRankChangedInternal(int caste, int rank, int rating, string companionName, string ownerName)
        {
            ValheimServerGuide.Triggers.GuidanceDispatcher.Raise(new ValheimServerGuide.Triggers.TriggerEvent
            {
                Type = "dvergr_rank_changed",
                Subject = ((DvergrCaste)caste).ToString(),
                Extra = new System.Collections.Generic.Dictionary<string, object>
                {
                    { "rank", rank },
                    { "rating", rating },
                    { "companionName", companionName ?? string.Empty },
                    { "ownerName", ownerName ?? string.Empty },
                },
            });
        }

        private static void RaisePartyDuelWonInternal(string winnerOwnerName, int winSize, string loserOwnerName,
            int mvpCaste, string partyName)
        {
            ValheimServerGuide.Triggers.GuidanceDispatcher.Raise(new ValheimServerGuide.Triggers.TriggerEvent
            {
                Type = "dvergr_party_duel_won",
                Subject = winnerOwnerName ?? string.Empty,
                Extra = new System.Collections.Generic.Dictionary<string, object>
                {
                    { "winSize", winSize },
                    { "ownerName", winnerOwnerName ?? string.Empty },
                    { "opponentOwner", loserOwnerName ?? string.Empty },
                    { "mvpCaste", ((DvergrCaste)mvpCaste).ToString() },
                    { "partyName", string.IsNullOrEmpty(partyName) ? (winnerOwnerName ?? string.Empty) : partyName },
                },
            });
        }

        // Generic raise used by the tournament triggers (and any future type-only
        // trigger). Kept internal like the others so the ServerGuide type reference
        // is only JIT'd when ServerGuide is actually present.
        private static void RaiseSimple(string type, string subject, System.Collections.Generic.Dictionary<string, object> extra)
        {
            ValheimServerGuide.Triggers.GuidanceDispatcher.Raise(new ValheimServerGuide.Triggers.TriggerEvent
            {
                Type = type,
                Subject = subject ?? string.Empty,
                Extra = extra,
            });
        }

        private static void RaisePartyRankChangedInternal(string ownerName, int rank, int rating, string partyName)
        {
            ValheimServerGuide.Triggers.GuidanceDispatcher.Raise(new ValheimServerGuide.Triggers.TriggerEvent
            {
                Type = "dvergr_party_rank_changed",
                Subject = ownerName ?? string.Empty,
                Extra = new System.Collections.Generic.Dictionary<string, object>
                {
                    { "rank", rank },
                    { "rating", rating },
                    { "ownerName", ownerName ?? string.Empty },
                    { "partyName", string.IsNullOrEmpty(partyName) ? (ownerName ?? string.Empty) : partyName },
                },
            });
        }
    }
}
