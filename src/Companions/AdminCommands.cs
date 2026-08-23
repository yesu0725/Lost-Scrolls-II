using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using LostScrollsII.Companions;
using UnityEngine;

namespace LostScrollsII.Patches
{
    // Registers admin/debug console commands once the terminal is ready.
    //   de_spawn <rogue|fire|ice|support> [level]
    // Not flagged as a cheat, so it works without `devcommands` — it's an
    // explicit admin tool. Spawns an already-recruited companion owned by the
    // local player, in front of them, at the given level (default 1).
    [HarmonyPatch(typeof(Terminal), nameof(Terminal.InitTerminal))]
    public static class AdminCommands
    {
        private static bool _registered;

        public static void Postfix()
        {
            if (_registered) return;
            _registered = true;

            Terminal.ConsoleEvent spawn = args =>
            {
                var player = Player.m_localPlayer;
                if (player == null) { args.Context.AddString("No local player."); return; }
                if (args.Length < 2)
                {
                    args.Context.AddString("Usage: de_spawn <rogue|fire|ice|support> [level]");
                    return;
                }

                if (!TryParseCaste(args[1], out var caste))
                {
                    args.Context.AddString($"Unknown caste '{args[1]}'. Use rogue|fire|ice|support.");
                    return;
                }

                int level = 1;
                if (args.Length >= 3) int.TryParse(args[2], out level);
                level = Mathf.Clamp(level, 1, DvergrCompanion.MaxLevel);

                var pos = player.transform.position + player.transform.forward * 2f + Vector3.up * 0.5f;
                var go = CommunionService.SpawnRecruited(caste, level, player, pos);
                args.Context.AddString(go != null
                    ? $"Spawned {caste} companion (level {level})."
                    : "Spawn failed — creature prefab not found.");
            };

            new Terminal.ConsoleCommand("de_spawn",
                "<rogue|fire|ice|support> [level] - spawn a recruited Dvergr companion",
                spawn, optionsFetcher: () => new List<string> { "rogue", "fire", "ice", "support" });

            // de_ladder [rogue|fire|ice|support] [count] — print the duel ladder
            // (docs/Ranking.md). Reads the local snapshot, so it works on any
            // client once the server has pushed the table.
            Terminal.ConsoleEvent ladder = args =>
            {
                int caste = -1;
                int count = 10;
                for (int i = 1; i < args.Length; i++)
                {
                    if (TryParseCaste(args[i], out var c)) caste = (int)c;
                    else if (int.TryParse(args[i], out var n)) count = Mathf.Clamp(n, 1, 100);
                }

                var ranked = Ranking.LeaderboardStore.Ranked(caste);
                if (ranked.Count == 0) { args.Context.AddString("Duel ladder is empty."); return; }

                args.Context.AddString($"=== Dvergr Duel Ladder{(caste >= 0 ? $" ({((DvergrCaste)caste).Display()})" : "")} ===");
                for (int i = 0; i < ranked.Count && i < count; i++)
                {
                    var r = ranked[i];
                    var name = string.IsNullOrEmpty(r.companionName) ? "(unnamed)" : r.companionName;
                    var owner = string.IsNullOrEmpty(r.ownerName) ? "?" : r.ownerName;
                    args.Context.AddString($"#{i + 1,-2} {r.rating,5}  {name} ({owner})  {r.wins}W/{r.losses}L");
                }
            };
            new Terminal.ConsoleCommand("de_ladder",
                "[rogue|fire|ice|support] [count] - show the Dvergr duel ladder",
                ladder,
                optionsFetcher: () => new List<string> { "rogue", "fire", "ice", "support" });

            // de_party_ladder [count] — print the party duel ladder (docs/Party-Duels.md).
            Terminal.ConsoleEvent partyLadder = args =>
            {
                int count = 10;
                if (args.Length >= 2 && int.TryParse(args[1], out var n)) count = Mathf.Clamp(n, 1, 100);

                var ranked = Ranking.LeaderboardStore.RankedParties();
                if (ranked.Count == 0) { args.Context.AddString("Party ladder is empty."); return; }

                args.Context.AddString("=== Dvergr Party Duel Ladder ===");
                for (int i = 0; i < ranked.Count && i < count; i++)
                {
                    var r = ranked[i];
                    var owner = string.IsNullOrEmpty(r.ownerName) ? "?" : r.ownerName;
                    var label = string.IsNullOrEmpty(r.partyName) ? owner : $"{r.partyName} ({owner})";
                    int team = r.memberSnapshot != null ? r.memberSnapshot.Count : 0;
                    args.Context.AddString($"#{i + 1,-2} {r.rating,5}  {label}  {r.wins}W/{r.losses}L  (team {team})");
                }
            };
            new Terminal.ConsoleCommand("de_party_ladder",
                "[count] - show the Dvergr party duel ladder", partyLadder);

            // de_party_name <name> — name your party (shown on the party ladder and in
            // announcements). Any player may name their OWN party (docs/Party-Duels.md).
            new Terminal.ConsoleCommand("de_party_name",
                "<name> - name your party of companions", args =>
            {
                if (Player.m_localPlayer == null) { args.Context.AddString("No local player."); return; }
                if (args.Length < 2) { args.Context.AddString("Usage: de_party_name <name>"); return; }
                var name = string.Join(" ", args.Args, 1, args.Length - 1).Trim();
                if (name.Length > 32) name = name.Substring(0, 32);
                Ranking.LeaderboardSync.SendPartyName(name);
                args.Context.AddString($"Your party is now named '{name}'.");
            });

            // de_tournament <start|open|join|ready|begin|bracket|forfeit|cancel> [slot]
            //
            // Several tournaments can run at once, so every subcommand takes an
            // optional trailing SLOT: "coins", "valcoin", or omitted for the free
            // admin-run one. The F7 panel is the primary surface — this is the
            // console equivalent, kept working for admins and for scripting.
            //
            // Admin subcommands go through the admin-authenticated RPC rather than
            // requiring the caller to be sat at the host (see LeaderboardSync).
            Terminal.ConsoleEvent tourney = args =>
            {
                var player = Player.m_localPlayer;
                string sub = args.Length >= 2 ? args[1].ToLowerInvariant() : "";

                // Trailing slot argument, defaulting to the free tournament.
                string SlotAt(int index) => args.Length > index ? args[index].ToLowerInvariant() : "";

                switch (sub)
                {
                    case "start":
                    {
                        string mode = args.Length >= 3 ? args[2] : "1v1";
                        int size = 0; if (args.Length >= 4) int.TryParse(args[3], out size);
                        string eliminationType = args.Length >= 5 ? args[4] : "single";
                        Ranking.LeaderboardSync.SendAdminCommand($"start|{mode}|{size}|{eliminationType}");
                        break;
                    }
                    case "open":
                    {
                        // The PLAYER-facing "open a staked tournament" — not an admin
                        // action; the stake is the gate.
                        if (player == null) { args.Context.AddString("No local player."); return; }
                        if (args.Length < 3) { args.Context.AddString("Usage: de_tournament open <coins|valcoin>"); return; }
                        var currency = Economy.Wager.Parse(args[2]);
                        if (currency == Economy.WagerCurrency.None) { args.Context.AddString("Currency must be coins or valcoin."); return; }
                        int fee = Economy.Wager.TournamentFee(currency);
                        int paid = 0;
                        if (currency == Economy.WagerCurrency.Coins)
                        {
                            if (!Economy.Wager.TryTakeCoins(player, fee))
                            { args.Context.AddString($"You need {fee} Coins to open that."); return; }
                            paid = fee;
                        }
                        Ranking.LeaderboardSync.SendStartWagered(Economy.Wager.Key(currency), paid);
                        args.Context.AddString($"Opening a {Economy.Wager.Display(currency)} tournament...");
                        break;
                    }
                    case "begin":
                        Ranking.LeaderboardSync.SendAdminCommand("begin|" + SlotAt(2));
                        break;
                    case "cancel":
                        Ranking.LeaderboardSync.SendAdminCommand("cancel|" + SlotAt(2));
                        break;
                    case "forfeit":
                    {
                        if (args.Length < 3) { args.Context.AddString("Usage: de_tournament forfeit <playerName> [slot]"); return; }
                        Ranking.LeaderboardSync.SendAdminCommand($"forfeit|{args[2]}|{SlotAt(3)}");
                        break;
                    }
                    case "join":
                        if (player == null) { args.Context.AddString("No local player."); return; }
                        JoinTournament(player, args, SlotAt(2));
                        break;
                    case "ready":
                        if (player == null) { args.Context.AddString("No local player."); return; }
                        Ranking.LeaderboardSync.SendTournamentReady(SlotAt(2));
                        break;
                    case "withdraw":
                        if (player == null) { args.Context.AddString("No local player."); return; }
                        Ranking.LeaderboardSync.SendTournamentWithdraw(SlotAt(2));
                        args.Context.AddString("Withdrawing your entry...");
                        break;
                    case "release":
                    {
                        if (args.Length < 3) { args.Context.AddString("Usage: de_tournament release <entrant name> [slot]"); return; }
                        Ranking.LeaderboardSync.SendAdminCommand($"release|{args[2]}|{SlotAt(3)}");
                        break;
                    }
                    case "activate":
                        Ranking.LeaderboardSync.SendAdminCommand("activate|" + SlotAt(2));
                        break;
                    case "bracket":
                        PrintBracket(args);
                        break;
                    default:
                        args.Context.AddString("Usage: de_tournament <open coins|valcoin | join [slot] | ready [slot] | withdraw [slot] | bracket | " +
                            "start 1v1|party [size] [single|double|round_robin] | begin [slot] | activate [slot] | forfeit <name> [slot] | release <name> [slot] | cancel [slot]>");
                        break;
                }
            };
            new Terminal.ConsoleCommand("de_tournament",
                "<open|join|ready|withdraw|bracket|start|begin|activate|forfeit|release|cancel> - run or enter a companion duel tournament",
                tourney, optionsFetcher: () => new List<string> { "open", "join", "ready", "withdraw", "bracket", "start", "begin", "activate", "forfeit", "release", "cancel" });

            // de_duel_invite <post coins|valcoin | accept <player> | ready | withdraw | list>
            // The console twin of the F7 panel's duel-invite controls.
            Terminal.ConsoleEvent invite = args =>
            {
                var player = Player.m_localPlayer;
                if (player == null) { args.Context.AddString("No local player."); return; }
                string sub = args.Length >= 2 ? args[1].ToLowerInvariant() : "list";
                long me = player.GetPlayerID();

                switch (sub)
                {
                    case "post":
                    {
                        if (args.Length < 3) { args.Context.AddString("Usage: de_duel_invite post <coins|valcoin>"); return; }
                        var currency = Economy.Wager.Parse(args[2]);
                        if (currency == Economy.WagerCurrency.None) { args.Context.AddString("Currency must be coins or valcoin."); return; }
                        PostInviteFromConsole(player, currency, args);
                        break;
                    }
                    case "accept":
                    {
                        if (args.Length < 3) { args.Context.AddString("Usage: de_duel_invite accept <posting player>"); return; }
                        var target = string.Join(" ", args.Args, 2, args.Length - 2);
                        var open = Ranking.DuelInviteService.All.FirstOrDefault(i =>
                            i.phase == "open" && string.Equals(i.hostName, target, System.StringComparison.OrdinalIgnoreCase));
                        if (open == null) { args.Context.AddString($"No open invite from '{target}'."); return; }
                        AcceptInviteFromConsole(player, open, args);
                        break;
                    }
                    case "ready":
                    {
                        var mine = Ranking.DuelInviteService.MineIn(me);
                        if (mine == null) { args.Context.AddString("You have no duel invite."); return; }
                        Ranking.LeaderboardSync.SendInviteAction("ready", mine.id, null, null, null, 0, 0, null, 0);
                        break;
                    }
                    case "withdraw":
                    {
                        var mine = Ranking.DuelInviteService.MineIn(me);
                        if (mine == null) { args.Context.AddString("You have no duel invite."); return; }
                        Ranking.LeaderboardSync.SendInviteAction("withdraw", mine.id, null, null, null, 0, 0, null, 0);
                        break;
                    }
                    default:
                    {
                        var all = Ranking.DuelInviteService.All.ToList();
                        if (all.Count == 0) { args.Context.AddString("No duel invites posted."); return; }
                        args.Context.AddString("=== Duel invites ===");
                        foreach (var i in all)
                        {
                            var cur = Economy.Wager.Display(Economy.Wager.Parse(i.currency));
                            args.Context.AddString($"  {i.hostName} - {i.hostLabel} (Lv{i.hostLevel}) - {i.stake} {cur} " +
                                $"[{i.phase}]{(i.phase == "matched" ? " vs " + i.oppName : "")}");
                        }
                        break;
                    }
                }
            };
            new Terminal.ConsoleCommand("de_duel_invite",
                "<post coins|valcoin | accept <player> | ready | withdraw | list> - stake a companion on a single duel",
                invite, optionsFetcher: () => new List<string> { "post", "accept", "ready", "withdraw", "list" });

            // de_champions — list the Hall of Champions (host/server reads the file).
            new Terminal.ConsoleCommand("de_champions", "- list past tournament champions", args =>
            {
                var champs = Ranking.TournamentService.LoadChampions();
                if (champs.Count == 0) { args.Context.AddString("No champions recorded yet."); return; }
                args.Context.AddString("=== Hall of Champions ===");
                for (int i = champs.Count - 1; i >= 0 && i >= champs.Count - 20; i--)
                {
                    var c = champs[i];
                    args.Context.AddString($"[{c.mode}] {c.championLabel} ({c.ownerName}) — bracket of {c.bracketSize}");
                }
            });

            // de_season_reset — archive the current ladder and start a new season.
            // The store is server-owned, so this goes through the admin-authenticated
            // RPC (the server re-verifies ZNet.IsAdmin from the peer's host name; the
            // listen host runs it directly). Running it locally would have made the
            // command useless to any admin who wasn't sat at the server console.
            Terminal.ConsoleEvent seasonReset = args =>
            {
                Ranking.LeaderboardSync.SendAdminCommand("season");
                args.Context.AddString("Duel season reset requested.");
            };
            new Terminal.ConsoleCommand("de_season_reset",
                "- archive the duel ladder and start a new season (admin)",
                seasonReset, onlyAdmin: true);

            // de_container_reset — put the chest/storage panel back at its default
            // spot (two rows below the player inventory) after dragging it around.
            // Client-side and purely cosmetic, so no admin gate.
            new Terminal.ConsoleCommand("de_container_reset",
                "- move the chest/storage UI back to its default position", args =>
            {
                if (ContainerPanelPositioner.BiomeLordsLoaded())
                {
                    args.Context.AddString("BiomeLords owns the chest UI position — use its own setting.");
                    return;
                }
                if (Plugin.ContainerPanelOffset == null) return;
                Plugin.ContainerPanelOffset.Value = "auto";
                args.Context.AddString("Chest/storage UI position reset to default.");
            });

            // de_bounty_status — read out the bounty feature gate (docs/Bounty-Hunting.md).
            // Deliberately available to any player, not admin-gated: on a client it
            // reports what the SERVER said (the client can't see the server's plugin
            // set), which is exactly the thing a player needs to check when the board
            // is missing. On the server/host it reports each gate condition.
            new Terminal.ConsoleCommand("de_bounty_status",
                "- show whether bounty hunting is active, and why", args =>
            {
                if (Bounty.BountyFeatureGate.IsServerAuthority)
                {
                    args.Context.AddString($"Bounty hunting: {(Bounty.BountyFeatureGate.IsEnabled ? "ACTIVE" : "inactive")} (this is the server/host).");
                    args.Context.AddString($"  BiomeLords : {Bounty.BountyFeatureGate.BiomeLordsLoaded}");
                    args.Context.AddString($"  ServerGuide: {Bounty.BountyFeatureGate.ServerGuideLoaded}");
                    args.Context.AddString($"  Donations  : {Bounty.BountyFeatureGate.DonationsLoaded}");
                    args.Context.AddString($"  Config     : {(Plugin.BountyEnabled == null || Plugin.BountyEnabled.Value)}");
                }
                else
                {
                    args.Context.AddString($"Bounty hunting: {(Bounty.BountySync.FeatureActive ? "ACTIVE on this server" : "not available on this server")}.");
                    args.Context.AddString("  (Reported by the server — it needs BiomeLords, ServerGuide and Valheim Donations.)");
                }
            });

            // de_bounty_sample [count] — Phase B verification tool
            // (docs/Bounty-Hunting.md). Rolls candidate bounty locations through the
            // real sampler and pins each on the local map so placement can be walked
            // to and checked. No creature is spawned and no bounty is posted — this
            // exercises the sampler and the pin lifecycle only.
            new Terminal.ConsoleCommand("de_bounty_sample",
                "[count] - roll candidate bounty locations and pin them (test tool)", args =>
            {
                var player = Player.m_localPlayer;
                if (player == null) { args.Context.AddString("No local player."); return; }

                int count = 5;
                if (args.Length >= 2) int.TryParse(args[1], out count);
                count = Mathf.Clamp(count, 1, 25);

                var found = new List<(Vector3 pos, Heightmap.Biome biome)>();
                int made = Bounty.BountyLocationSampler.SampleMany(
                    Vector3.zero,
                    Plugin.BountySearchMinRadius.Value,
                    Plugin.BountySearchMaxRadius.Value,
                    count, found, out var report);

                args.Context.AddString($"Sampled {made}/{count} bounty location(s):");
                foreach (var line in report.Split('\n'))
                    if (!string.IsNullOrEmpty(line.Trim())) args.Context.AddString(line);

                for (int i = 0; i < found.Count; i++)
                {
                    var (pos, biome) = found[i];
                    float dist = Vector3.Distance(
                        new Vector3(player.transform.position.x, 0f, player.transform.position.z),
                        new Vector3(pos.x, 0f, pos.z));
                    Bounty.BountyMapPin.Show($"test_{i}", $"Test bounty ({biome})", pos);
                    args.Context.AddString($"  pinned #{i}: {biome}, {dist:F0}m from you.");
                }
                if (made > 0)
                    args.Context.AddString("Use de_bounty_sample_clear to remove the test pins.");
            });

            // de_bounty_spawn [tier] [far] — Phase C verification tool
            // (docs/Bounty-Hunting.md). Spawns a real, scaled, auto-hostile bounty
            // target plus its escort. Without "far" it spawns close by so the fight
            // can be tested immediately; with "far" it uses the real land sampler and
            // pins the result, exercising the Phase B + C path together.
            new Terminal.ConsoleCommand("de_bounty_spawn",
                "[tier 1-5] [far] - spawn a scaled bounty target + escort (test tool)", args =>
            {
                var player = Player.m_localPlayer;
                if (player == null) { args.Context.AddString("No local player."); return; }

                int tier = 1;
                if (args.Length >= 2) int.TryParse(args[1], out tier);
                tier = Bounty.BountyTiers.Clamp(tier);
                bool far = args.Length >= 3 && args[2].ToLowerInvariant() == "far";

                Vector3 pos;
                Heightmap.Biome biome;
                if (far)
                {
                    if (!Bounty.BountyLocationSampler.TrySample(Vector3.zero,
                            Plugin.BountySearchMinRadius.Value, Plugin.BountySearchMaxRadius.Value,
                            out pos, out biome, out var stats))
                    {
                        args.Context.AddString($"No valid location: {stats}");
                        return;
                    }
                    args.Context.AddString($"Sampled {biome} at ({pos.x:F0}, {pos.z:F0}) — {stats}");
                    args.Context.AddString("NOTE: that zone is probably not loaded, so nothing will spawn there until you travel to it.");
                }
                else
                {
                    pos = player.transform.position + player.transform.forward * 12f;
                    if (ZoneSystem.instance != null && ZoneSystem.instance.GetSolidHeight(pos, out float h)) pos.y = h;
                    biome = WorldGenerator.instance != null
                        ? WorldGenerator.instance.GetBiome(pos) : Heightmap.Biome.None;
                }

                var id = "test_" + System.Guid.NewGuid().ToString("N").Substring(0, 8);
                var spawned = Bounty.BountySpawner.Spawn(id, tier, pos, biome);
                if (spawned == null) { args.Context.AddString("Spawn failed — see the log."); return; }

                Bounty.BountyMapPin.Show(id, $"{Bounty.BountyTiers.TierName(tier)} bounty ({biome})", pos);
                args.Context.AddString($"Spawned {Bounty.BountyTiers.Describe(tier)} in {biome}.");
                var target = spawned.Target != null ? spawned.Target.GetComponent<Character>() : null;
                if (target != null)
                    args.Context.AddString($"  target: {target.m_name}, {target.GetMaxHealth():F0} HP, level {target.GetLevel()}.");
                args.Context.AddString($"  escort: {spawned.Minions.Count}. Bounty id '{id}'.");
            });

            // de_bounty_chance — Phase D verification tool. The Valcoin roll is
            // invisible in play (it either pays or doesn't), so this prints the
            // computed chance and the standing it was derived from.
            new Terminal.ConsoleCommand("de_bounty_chance",
                "- show your Valcoin payout chance per bounty tier", args =>
            {
                var player = Player.m_localPlayer;
                if (player == null) { args.Context.AddString("No local player."); return; }

                int rank = Bounty.BountyService.BestCompetitiveRank(player);
                args.Context.AddString(rank > 0
                    ? $"Best duel/party standing: #{rank} (bonus reaches to #{Plugin.BountyValcoinRankDepth.Value})."
                    : "You are unranked on both ladders — no rank bonus applies.");
                for (int t = Bounty.BountyTiers.MinTier; t <= Bounty.BountyTiers.MaxTier; t++)
                {
                    float c = Bounty.BountyService.ValcoinChanceFor(player, t);
                    args.Context.AddString($"  tier {t} ({Bounty.BountyTiers.TierName(t)}): {c * 100f:F0}% chance of a Valcoin payout.");
                }
                if (!Bounty.BountyFeatureGate.DonationsLoaded)
                    args.Context.AddString("NOTE: Valheim Donations is not loaded — no coins would actually be paid.");
            });

            // de_bounty_commission — stand in for the Haldor conversation while
            // testing (docs/Bounty-Hunting.md, Phase G). Grants the same player key
            // the dialogue's accept choice grants, so it exercises the identical path
            // without walking to the trader.
            new Terminal.ConsoleCommand("de_bounty_commission",
                "- grant yourself the warden's commission (test tool)", args =>
            {
                var player = Player.m_localPlayer;
                if (player == null) { args.Context.AddString("No local player."); return; }
                if (Bounty.BountyQuestGate.IsUnlocked(player))
                { args.Context.AddString("The board is already open to you."); return; }
                player.AddUniqueKey(Bounty.BountyQuestGate.KeyStartRequested);
                args.Context.AddString("Commission granted — the posting will be marked shortly.");
            });

            // de_bounty_quest_reset — put the gate back to its starting state so the
            // whole quest can be replayed on one character.
            new Terminal.ConsoleCommand("de_bounty_quest_reset",
                "- forget the warden's commission, re-locking the board (test tool)", args =>
            {
                var player = Player.m_localPlayer;
                if (player == null) { args.Context.AddString("No local player."); return; }
                Bounty.BountyQuestGate.Reset(player);
                args.Context.AddString("Bounty quest reset — the board is locked again. " +
                    "(ServerGuide's own once-per-player record of the conversation is separate; " +
                    "use its reset if you want Haldor to offer it again.)");
            });

            // de_bounty_board — list the Wanted Board's postings. Useful when the panel
            // looks empty: this distinguishes "the board really is empty" from "the
            // board didn't sync to this client".
            new Terminal.ConsoleCommand("de_bounty_board",
                "- list the current bounty postings", args =>
            {
                var postings = Bounty.BountyBoardStore.All();
                if (postings.Count == 0)
                {
                    args.Context.AddString(Bounty.BountyFeatureGate.AvailableToLocalPlayer
                        ? "The board is empty right now."
                        : "Bounty hunting isn't active on this server.");
                    return;
                }
                var lp = Player.m_localPlayer;
                args.Context.AddString($"Wanted Board ({postings.Count} posting(s)):");
                foreach (var p in postings)
                {
                    string who = p.IsOpen ? "open" : $"taken by {p.acceptedByName}";
                    string dist = lp != null
                        ? $"{Vector3.Distance(new Vector3(lp.transform.position.x, 0f, lp.transform.position.z), new Vector3(p.x, 0f, p.z)):F0}m"
                        : "?";
                    string tags = (p.spawned ? ", spawned" : "")
                                + (p.tutorial ? ", commission" : "")
                                + (Bounty.BountyBoardStore.IsElite(p) ? ", ELITE" : "");
                    string age = p.postedTicks > 0
                        ? $", {(System.DateTime.UtcNow - new System.DateTime(p.postedTicks)).TotalHours:F1}h old"
                        : "";
                    args.Context.AddString($"  {p.Label} — {dist}, {who}{tags}{age}");
                }
                float refresh = Plugin.BountyRefreshHours.Value;
                args.Context.AddString(refresh > 0f
                    ? $"Unclaimed postings are retired after {refresh:F0}h."
                    : "Board rotation is disabled (RefreshHours = 0).");
            });

            // de_bounty_ladder — the bounty-hunter standings (docs/Bounty-Hunting.md).
            // Reads the replicated snapshot, so it works on a client.
            new Terminal.ConsoleCommand("de_bounty_ladder",
                "[count] - show the bounty hunter standings", args =>
            {
                int count = 10;
                if (args.Length >= 2) int.TryParse(args[1], out count);
                count = Mathf.Clamp(count, 1, 50);

                var hunters = Bounty.BountyLeaderboardStore.Ranked();
                if (hunters.Count == 0)
                {
                    args.Context.AddString("No bounties have been answered yet.");
                    return;
                }
                args.Context.AddString("Bounty hunters — points (felled/freed, best posting):");
                for (int i = 0; i < hunters.Count && i < count; i++)
                {
                    var h = hunters[i];
                    string best = h.bestTier > 0 ? Bounty.BountyTiers.TierName(h.bestTier) : "-";
                    args.Context.AddString($"  #{i + 1,-2} {h.points,5}  {h.ownerName}  " +
                        $"({h.kills} felled / {h.communes} freed, best: {best})");
                }

                var lp = Player.m_localPlayer;
                if (lp != null)
                {
                    int myRank = Bounty.BountyLeaderboardStore.RankOf(lp.GetPlayerID());
                    int bonusAt = Plugin.BountyLeaderboardBonusRank.Value;
                    if (myRank > 0)
                        args.Context.AddString($"You are #{myRank}." + (bonusAt > 0 && myRank <= bonusAt
                            ? " Your standing earns +1 reward tier."
                            : bonusAt > 0 ? $" Reach #{bonusAt} for +1 reward tier." : ""));
                }
            });

            // de_bounty_season_reset — archive the bounty board and start a new season,
            // mirroring de_season_reset. Goes through the admin-authenticated RPC so a
            // remote admin can run it; the server re-verifies admin rights itself.
            new Terminal.ConsoleCommand("de_bounty_season_reset",
                "- archive the bounty ladder and start a new season (admin)", args =>
            {
                Ranking.LeaderboardSync.SendAdminCommand("bountyseason");
                args.Context.AddString("Bounty season reset requested.");
            }, onlyAdmin: true);

            // Companion to the above — drops every test pin.
            new Terminal.ConsoleCommand("de_bounty_sample_clear",
                "- remove the pins placed by de_bounty_sample", args =>
            {
                int n = Bounty.BountyMapPin.Count;
                Bounty.BountyMapPin.ClearAll();
                args.Context.AddString($"Cleared {n} bounty pin(s).");
            });
        }

        // Console fallback for entering a tournament — routed through the SAME escrow
        // path the UI uses, so every entrant is held as a Communion Totem the server
        // can auto-summon. 1v1 seals the hovered companion; party seals the player's
        // nearby free Follow-stance companions. The live companions are despawned into
        // escrow (returned as totems when the tournament ends / on withdraw / release).
        //
        // A Coin entry fee is taken here, client-side, for the same reason the panel
        // takes it there: Coins are inventory items and only this client can remove
        // them. A Valcoin fee is debited server-side and nothing is taken here.
        private static void JoinTournament(Player player, Terminal.ConsoleEventArgs args, string slot)
        {
            var snap = Ranking.TournamentService.Get(slot);
            if (snap == null || !snap.active || snap.phase != "registration")
            { args.Context.AddString("No tournament is accepting entries in that slot."); return; }

            long ownerId = player.GetPlayerID();
            string ownerName = player.GetPlayerName();
            var currency = Economy.Wager.Parse(snap.currency);

            // The host's opening fee already covers their own entry.
            bool hostFree = ownerId == snap.hostId && snap.hostCredit > 0;
            int fee = hostFree ? 0 : snap.entryFee;
            int paid = 0;
            if (currency == Economy.WagerCurrency.Coins && fee > 0)
            {
                if (!Economy.Wager.TryTakeCoins(player, fee))
                { args.Context.AddString($"You need {fee} Coins to enter."); return; }
                paid = fee;
            }

            if (snap.mode == "party")
            {
                var team = GatherFollowers(player, Mathf.Max(1, Plugin.MaxPartySize.Value));
                if (team.Count == 0)
                {
                    if (paid > 0) Economy.Wager.GiveCoins(player, paid);
                    args.Context.AddString("No free Follow-stance allies of yours nearby to enter as a party.");
                    return;
                }

                var payloads = new List<string>();
                foreach (var c in team)
                {
                    var t = TotemConversionService.CreateTotem(c);
                    if (t != null) payloads.Add(TotemConversionService.SerializePayload(t));
                }
                foreach (var c in team) c.DespawnToTotem();

                var prec = Ranking.LeaderboardStore.FindParty(ownerId);
                int seed = prec != null ? prec.rating : Ranking.Rating.StartRating;
                string label = prec != null && !string.IsNullOrEmpty(prec.partyName) ? prec.partyName : ownerName;
                Ranking.LeaderboardSync.SendTournamentJoinEscrow(slot, ownerId.ToString(), ownerId, ownerName, label, -1, seed,
                    payloads, 0, paid);
                args.Context.AddString($"Sealing and registering your party of {payloads.Count}...");
                return;
            }

            // 1v1: seal + enter the hovered companion.
            var hover = player.GetHoverObject();
            var ch = hover != null ? hover.GetComponentInParent<Character>() : null;
            var comp = ch != null ? ch.GetComponent<DvergrCompanion>() : null;
            if (comp == null || !comp.IsOwner(player))
            {
                if (paid > 0) Economy.Wager.GiveCoins(player, paid);
                args.Context.AddString(comp == null
                    ? "Hover the companion you want to enter, then run de_tournament join."
                    : "That companion isn't yours.");
                return;
            }

            string id = comp.EnsureCompanionId();
            var rec = Ranking.LeaderboardStore.Find(id);
            int seed1 = rec != null ? rec.rating : Ranking.Rating.StartRating;
            var totem = TotemConversionService.CreateTotem(comp);
            if (totem == null)
            {
                if (paid > 0) Economy.Wager.GiveCoins(player, paid);
                args.Context.AddString("Could not seal that companion (missing totem prefab).");
                return;
            }
            var payload = TotemConversionService.SerializePayload(totem);
            string label1 = comp.DisplayName;
            int caste1 = (int)comp.Caste;
            int level1 = comp.Level;
            comp.DespawnToTotem();
            Ranking.LeaderboardSync.SendTournamentJoinEscrow(slot, id, ownerId, ownerName, label1, caste1, seed1,
                new List<string> { payload }, level1, paid);
            args.Context.AddString($"Sealing and registering '{label1}'...");
        }

        // Console equivalents of the panel's Post / Accept buttons. Both seal the
        // HOVERED companion (rather than picking one out of the pack for you) so the
        // console path can't stake an ally you didn't mean to.
        private static void PostInviteFromConsole(Player player, Economy.WagerCurrency currency, Terminal.ConsoleEventArgs args)
        {
            if (Ranking.DuelInviteService.MineIn(player.GetPlayerID()) != null)
            { args.Context.AddString("You already have a duel invite — withdraw it first."); return; }
            if (!SealHovered(player, args, out var id, out var label, out var caste, out var level, out var payload)) return;

            int stake = Economy.Wager.DuelStake(currency);
            int paid = 0;
            if (currency == Economy.WagerCurrency.Coins)
            {
                if (!Economy.Wager.TryTakeCoins(player, stake))
                { args.Context.AddString($"You need {stake} Coins to stake that."); return; }
                paid = stake;
            }
            Ranking.LeaderboardSync.SendInviteAction("post", null, Economy.Wager.Key(currency), id, label, level, caste, payload, paid);
            args.Context.AddString($"Posting a duel invite with '{label}'...");
        }

        private static void AcceptInviteFromConsole(Player player, Ranking.DuelInvite invite, Terminal.ConsoleEventArgs args)
        {
            if (!SealHovered(player, args, out var id, out var label, out var caste, out var level, out var payload)) return;

            var currency = Economy.Wager.Parse(invite.currency);
            int paid = 0;
            if (currency == Economy.WagerCurrency.Coins)
            {
                if (!Economy.Wager.TryTakeCoins(player, invite.stake))
                { args.Context.AddString($"You need {invite.stake} Coins to match that stake."); return; }
                paid = invite.stake;
            }
            Ranking.LeaderboardSync.SendInviteAction("accept", invite.id, invite.currency, id, label, level, caste, payload, paid);
            args.Context.AddString($"Accepting {invite.hostName}'s challenge with '{label}'...");
        }

        // Seals the companion the player is looking at into a totem payload and
        // despawns it, ready to be escrowed. Shared by both invite console paths.
        private static bool SealHovered(Player player, Terminal.ConsoleEventArgs args,
            out string id, out string label, out int caste, out int level, out string payload)
        {
            id = null; label = null; caste = 0; level = 0; payload = null;
            var hover = player.GetHoverObject();
            var ch = hover != null ? hover.GetComponentInParent<Character>() : null;
            var comp = ch != null ? ch.GetComponent<DvergrCompanion>() : null;
            if (comp == null) { args.Context.AddString("Hover the companion you want to stake, then run the command."); return false; }
            if (!comp.IsOwner(player)) { args.Context.AddString("That companion isn't yours."); return false; }

            var totem = TotemConversionService.CreateTotem(comp);
            if (totem == null) { args.Context.AddString("Could not seal that companion (missing totem prefab)."); return false; }

            id = comp.EnsureCompanionId();
            label = comp.DisplayName;
            caste = (int)comp.Caste;
            level = comp.Level;
            payload = TotemConversionService.SerializePayload(totem);
            comp.DespawnToTotem();
            return true;
        }

        // The player's nearby, free, Follow-stance companions (nearest-first, capped)
        // — the same gathering rule the [K] party duel uses.
        private static List<DvergrCompanion> GatherFollowers(Player player, int cap)
        {
            long ownerId = player.GetPlayerID();
            var nearby = new List<Character>();
            Character.GetCharactersInRange(player.transform.position, 25f, nearby);
            var team = new List<DvergrCompanion>();
            foreach (var chr in nearby)
            {
                var c = chr.GetComponent<DvergrCompanion>();
                if (c == null || c.OwnerId != ownerId) continue;
                if (c.Stance != CompanionStance.Follow) continue;
                if (chr.GetComponent<ChoreAI>()?.IsAssigned == true) continue;
                if (c.InAnyDuelMode || c.IsFeral) continue;
                team.Add(c);
            }
            team.Sort((a, b) => Vector3.Distance(player.transform.position, a.transform.position)
                .CompareTo(Vector3.Distance(player.transform.position, b.transform.position)));
            if (team.Count > cap) team = team.GetRange(0, cap);
            return team;
        }

        private static void PrintBracket(Terminal.ConsoleEventArgs args)
        {
            var all = Ranking.TournamentService.All.ToList();
            if (all.Count == 0) { args.Context.AddString("No tournament is running."); return; }

            foreach (var s in all)
            {
                string title = string.IsNullOrEmpty(s.currency) ? "free" : s.currency;
                args.Context.AddString($"=== Tournament [{title}] ({s.mode}) - {s.phase} ===");
                if (s.entryFee > 0)
                    args.Context.AddString($"    entry {s.entryFee} / purse {s.prize} / opened by {s.hostName}");

                if (s.phase == "registration")
                {
                    args.Context.AddString($"Entrants ({s.entrants.Count}{(s.size > 0 ? "/" + s.size : "")}):");
                    foreach (var e in s.entrants) args.Context.AddString($"  - {e.label} ({e.ownerName})  [{e.seedRating}]");
                    continue;
                }
                if (s.phase == "complete") { args.Context.AddString($"Champion: {s.championLabel}"); continue; }

                int maxRound = 0;
                foreach (var m in s.matches) if (m.round > maxRound) maxRound = m.round;
                for (int r = 1; r <= maxRound; r++)
                {
                    args.Context.AddString($"-- Round {r} --");
                    foreach (var m in s.matches)
                    {
                        if (m.round != r) continue;
                        string res = !string.IsNullOrEmpty(m.winnerId)
                            ? "winner: " + (m.winnerId == m.aId ? m.aLabel : m.bLabel)
                            : m.activated ? "fighting"
                            : $"pending (ready {(m.aReady ? "Y" : "N")}/{(m.bReady ? "Y" : "N")})";
                        string b = string.IsNullOrEmpty(m.bId) ? "(bye)" : m.bLabel;
                        args.Context.AddString($"  {m.aLabel} vs {b} - {res}");
                    }
                }
            }
        }

        private static bool TryParseCaste(string s, out DvergrCaste caste)
        {
            switch (s.ToLowerInvariant())
            {
                case "rogue": caste = DvergrCaste.Rogue; return true;
                case "fire": case "firemage": caste = DvergrCaste.FireMage; return true;
                case "ice": case "icemage": caste = DvergrCaste.IceMage; return true;
                case "support": case "supportmage": caste = DvergrCaste.SupportMage; return true;
                default: caste = DvergrCaste.Rogue; return false;
            }
        }
    }
}
