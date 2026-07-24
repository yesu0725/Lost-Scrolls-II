using System.Collections.Generic;
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

            // de_tournament <start|join|begin|bracket|forfeit|cancel> ...
            // Admin subcommands (start/begin/forfeit/cancel) run on the host where
            // the authoritative state lives; join/bracket work on any client.
            Terminal.ConsoleEvent tourney = args =>
            {
                var player = Player.m_localPlayer;
                string sub = args.Length >= 2 ? args[1].ToLowerInvariant() : "";

                bool HostOnly()
                {
                    if (ZNet.instance != null && ZNet.instance.IsServer()) return true;
                    args.Context.AddString("That subcommand must be run on the host/server.");
                    return false;
                }

                switch (sub)
                {
                    case "start":
                    {
                        if (!HostOnly()) return;
                        string mode = args.Length >= 3 ? args[2] : "1v1";
                        int size = 0; if (args.Length >= 4) int.TryParse(args[3], out size);
                        string eliminationType = args.Length >= 5 ? args[4] : "single";
                        args.Context.AddString(Ranking.TournamentService.Start(mode, size, eliminationType));
                        break;
                    }
                    case "begin":
                        if (!HostOnly()) return;
                        args.Context.AddString(Ranking.TournamentService.Begin());
                        break;
                    case "cancel":
                        if (!HostOnly()) return;
                        args.Context.AddString(Ranking.TournamentService.Cancel());
                        break;
                    case "forfeit":
                    {
                        if (!HostOnly()) return;
                        if (args.Length < 3) { args.Context.AddString("Usage: de_tournament forfeit <playerName>"); return; }
                        var name = string.Join(" ", args.Args, 2, args.Length - 2);
                        args.Context.AddString(Ranking.TournamentService.Forfeit(name));
                        break;
                    }
                    case "join":
                        if (player == null) { args.Context.AddString("No local player."); return; }
                        JoinTournament(player, args);
                        break;
                    case "withdraw":
                        if (player == null) { args.Context.AddString("No local player."); return; }
                        Ranking.LeaderboardSync.SendTournamentWithdraw();
                        args.Context.AddString("Withdrawing your entry…");
                        break;
                    case "release":
                    {
                        if (args.Length < 3) { args.Context.AddString("Usage: de_tournament release <entrant name>"); return; }
                        var name = string.Join(" ", args.Args, 2, args.Length - 2);
                        Ranking.LeaderboardSync.SendAdminCommand("release|" + name);
                        break;
                    }
                    case "activate":
                        Ranking.LeaderboardSync.SendAdminCommand("activate");
                        break;
                    case "bracket":
                        PrintBracket(args);
                        break;
                    default:
                        args.Context.AddString("Usage: de_tournament <start 1v1|party [size] [single|double|round_robin] | join | withdraw | begin | activate | bracket | forfeit <name> | release <name> | cancel>");
                        break;
                }
            };
            new Terminal.ConsoleCommand("de_tournament",
                "<start|join|withdraw|begin|activate|bracket|forfeit|release|cancel> - run a companion duel tournament",
                tourney, optionsFetcher: () => new List<string> { "start", "join", "withdraw", "begin", "activate", "bracket", "forfeit", "release", "cancel" });

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
            // Server/host authority only (the store lives there). Admin-gated.
            Terminal.ConsoleEvent seasonReset = args =>
            {
                if (ZNet.instance == null || !ZNet.instance.IsServer())
                {
                    args.Context.AddString("de_season_reset must be run on the host/server.");
                    return;
                }
                int archived = Ranking.LeaderboardStore.SeasonReset();
                Ranking.LeaderboardSync.BroadcastTable();
                args.Context.AddString($"Season reset — archived {archived} record(s).");
            };
            new Terminal.ConsoleCommand("de_season_reset",
                "- archive the duel ladder and start a new season (host only)",
                seasonReset, onlyAdmin: true);
        }

        // Console fallback for entering a tournament — routed through the SAME escrow
        // path the UI uses, so every entrant is held as a Communion Totem the server
        // can auto-summon. 1v1 seals the hovered companion; party seals the player's
        // nearby free Follow-stance companions. The live companions are despawned into
        // escrow (returned as totems when the tournament ends / on withdraw / release).
        private static void JoinTournament(Player player, Terminal.ConsoleEventArgs args)
        {
            var snap = Ranking.TournamentService.Snapshot;
            if (snap == null || !snap.active || snap.phase != "registration")
            { args.Context.AddString("No tournament is accepting entries."); return; }

            long ownerId = player.GetPlayerID();
            string ownerName = player.GetPlayerName();

            if (snap.mode == "party")
            {
                var team = GatherFollowers(player, Mathf.Max(1, Plugin.MaxPartySize.Value));
                if (team.Count == 0)
                { args.Context.AddString("No free Follow-stance allies of yours nearby to enter as a party."); return; }

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
                Ranking.LeaderboardSync.SendTournamentJoinEscrow(ownerId.ToString(), ownerId, ownerName, label, -1, seed, payloads);
                args.Context.AddString($"Sealing and registering your party of {payloads.Count}…");
                return;
            }

            // 1v1: seal + enter the hovered companion.
            var hover = player.GetHoverObject();
            var ch = hover != null ? hover.GetComponentInParent<Character>() : null;
            var comp = ch != null ? ch.GetComponent<DvergrCompanion>() : null;
            if (comp == null) { args.Context.AddString("Hover the companion you want to enter, then run de_tournament join."); return; }
            if (!comp.IsOwner(player)) { args.Context.AddString("That companion isn't yours."); return; }

            string id = comp.EnsureCompanionId();
            var rec = Ranking.LeaderboardStore.Find(id);
            int seed1 = rec != null ? rec.rating : Ranking.Rating.StartRating;
            var totem = TotemConversionService.CreateTotem(comp);
            if (totem == null) { args.Context.AddString("Could not seal that companion (missing totem prefab)."); return; }
            var payload = TotemConversionService.SerializePayload(totem);
            string label1 = comp.DisplayName;
            int caste1 = (int)comp.Caste;
            int level1 = comp.Level;
            comp.DespawnToTotem();
            Ranking.LeaderboardSync.SendTournamentJoinEscrow(id, ownerId, ownerName, label1, caste1, seed1,
                new List<string> { payload }, level1);
            args.Context.AddString($"Sealing and registering '{label1}'…");
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
            var s = Ranking.TournamentService.Snapshot;
            if (s == null || !s.active) { args.Context.AddString("No tournament is running."); return; }
            args.Context.AddString($"=== Tournament ({s.mode}) — {s.phase} ===");

            if (s.phase == "registration")
            {
                args.Context.AddString($"Entrants ({s.entrants.Count}{(s.size > 0 ? "/" + s.size : "")}):");
                foreach (var e in s.entrants) args.Context.AddString($"  - {e.label} ({e.ownerName})  [{e.seedRating}]");
                return;
            }
            if (s.phase == "complete") { args.Context.AddString($"Champion: {s.championLabel}"); return; }

            int maxRound = 0;
            foreach (var m in s.matches) if (m.round > maxRound) maxRound = m.round;
            for (int r = 1; r <= maxRound; r++)
            {
                args.Context.AddString($"-- Round {r} --");
                foreach (var m in s.matches)
                {
                    if (m.round != r) continue;
                    string res = string.IsNullOrEmpty(m.winnerId)
                        ? "pending"
                        : "winner: " + (m.winnerId == m.aId ? m.aLabel : m.bLabel);
                    string b = string.IsNullOrEmpty(m.bId) ? "(bye)" : m.bLabel;
                    args.Context.AddString($"  {m.aLabel} vs {b} — {res}");
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
