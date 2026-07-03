using System.Collections.Generic;
using LostScrollsII.Companions;
using HarmonyLib;
using UnityEngine;

namespace LostScrollsII.Patches
{
    // Grants XP to recruited Dvergr companions near a kill, per
    // docs/Ally-Leveling.md ("XP sources: kills the ally participates in near
    // the player"). Phase 3 MVP: any recruited companion within range of a
    // non-ally death gets XP — no check yet for whether the companion actually
    // helped land the kill.
    // Corrected during Phase 5: the sibling ValheimServerGuide project's own
    // BossDefeatedTrigger.cs successfully patches Character.OnDeath via nameof,
    // confirming that's the right vanilla method (RPC_OnDeath, used originally
    // here, was a wrong guess that only happened to compile via string-based
    // Harmony targeting). Still flagged for in-game confirmation since it
    // hasn't actually run.
    [HarmonyPatch(typeof(Character), nameof(Character.OnDeath))]
    public static class KillXpPatch
    {
        private const float XpShareRadius = 20f;

        // XP awarded for killing a player, equivalent to the Plains tier cap
        // (per the approved design in docs/Ally-Leveling.md). Flat — players
        // have no biome-creature HP to scale against.
        private const float PlayerKillXp = 50f;

        // Floor on the per-creature HP fraction, so the weakest creatures in a
        // biome still grant a meaningful slice of that biome's cap rather than
        // trending to zero. 0.25 = a minimum of one quarter of the biome cap.
        private const float MinXpFraction = 0.25f;

        public static void Postfix(Character __instance)
        {
            if (__instance == null) return;
            if (__instance.GetComponent<DvergrCompanion>() != null) return; // a dead companion grants nothing

            float xp;
            if (__instance.IsPlayer())
            {
                // The companion (proximity model) killed a player — Plains-tier XP.
                xp = PlayerKillXp;
            }
            else
            {
                // A friendly, non-player Players-faction death (tamed wolf, another
                // ally, etc.) shouldn't feed XP — only hostile creatures do.
                if (__instance.m_faction == Character.Faction.Players) return;
                xp = BiomeScaledXp(__instance);
            }

            if (xp <= 0f) return;

            var nearby = new List<Character>();
            Character.GetCharactersInRange(__instance.transform.position, XpShareRadius, nearby);

            foreach (var character in nearby)
            {
                var companion = character.GetComponent<DvergrCompanion>();
                if (companion != null)
                {
                    companion.AddXp(xp);
                }
            }
        }

        // Per-kill XP = biome cap × (creature max HP ÷ biome reference HP),
        // clamped to [MinXpFraction, 1.0] (approved design in
        // docs/Ally-Leveling.md). The HP is read LIVE from the dying creature,
        // so the value is always exact for this build — including star-creature
        // HP multipliers — rather than a hardcoded per-creature table. Anything
        // at or above the biome's reference HP (its toughest common creature,
        // plus minibosses/bosses) earns the full biome cap.
        private static float BiomeScaledXp(Character dead)
        {
            GetBiomeTier(Heightmap.FindBiome(dead.transform.position), out float cap, out float referenceHp);

            float fraction = referenceHp > 0f ? dead.GetMaxHealth() / referenceHp : 1f;
            fraction = Mathf.Clamp(fraction, MinXpFraction, 1f);

            float xp = Mathf.Round(cap * fraction);

            Plugin.Log.LogInfo($"[xp] Kill in {Heightmap.FindBiome(dead.transform.position)}: " +
                $"'{dead.name}' maxHP={dead.GetMaxHealth():F0} / ref {referenceHp:F0} " +
                $"-> {xp} XP (cap {cap:F0}).");
            return xp;
        }

        // Per-biome cap XP and reference HP (the toughest common creature of the
        // biome, which defines the cap). Approved values in docs/Ally-Leveling.md.
        // Unmapped biomes (DeepNorth/Ocean/None) fall back to Mountain tier so a
        // kill never grants 0.
        private static void GetBiomeTier(Heightmap.Biome biome, out float cap, out float referenceHp)
        {
            switch (biome)
            {
                case Heightmap.Biome.Meadows:     cap = 5f;   referenceHp = 25f;  break;
                case Heightmap.Biome.BlackForest: cap = 12f;  referenceHp = 150f; break;
                case Heightmap.Biome.Swamp:       cap = 22f;  referenceHp = 150f; break;
                case Heightmap.Biome.Mountain:    cap = 35f;  referenceHp = 100f; break;
                case Heightmap.Biome.Plains:      cap = 50f;  referenceHp = 80f;  break;
                case Heightmap.Biome.Mistlands:   cap = 75f;  referenceHp = 200f; break;
                case Heightmap.Biome.AshLands:    cap = 100f; referenceHp = 200f; break;
                default:                          cap = 35f;  referenceHp = 100f; break;
            }
        }
    }
}
