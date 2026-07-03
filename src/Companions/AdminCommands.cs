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
