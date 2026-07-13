using HarmonyLib;
using LostScrollsII.Companions;

namespace LostScrollsII.Patches
{
    // Replaces the vanilla star display with a custom level badge for recruited
    // companions. Vanilla's EnemyHud only has two star rects (m_level2/m_level3),
    // so it can never show more than 2 stars — useless for a 1–10 level range
    // (verified against the assembly). Instead we:
    //   1. Append a compact gold "★N" badge to the floating name (GetHoverName),
    //      which EnemyHud renders above the creature. Single-star-plus-number is
    //      visually distinct from vanilla's row of star sprites and reads at any
    //      level 1–10.
    //   2. Hide vanilla's own star rects on companion huds so the saturated
    //      "2 stars for a level-7 ally" never appears alongside the badge.
    // Text-only + toggling existing rects — no custom assets, so this stays
    // within the vanilla-assets-only constraint.

    // 1. The badge itself, on the name EnemyHud reads from. GetHoverName is
    //    declared on Character and only overridden by Player (not Humanoid), so a
    //    Dvergr (a Humanoid) resolves to this method — the patch catches it.
    [HarmonyPatch(typeof(Character), nameof(Character.GetHoverName))]
    public static class CompanionNameBadgePatch
    {
        public static void Postfix(Character __instance, ref string __result)
        {
            var companion = __instance.GetComponent<DvergrCompanion>();
            if (companion == null) return;

            // A player-given name replaces the vanilla creature name; otherwise
            // keep whatever vanilla produced. The badge is then appended either way.
            // Appended text contains no $tokens, so the Localize() pass EnemyHud
            // runs afterward leaves it intact; the rich-text color renders in TMP.
            if (companion.HasCustomName) __result = companion.DisplayName;

            // Owner name tag (req 4) — identifies whose ally this is at a glance,
            // which matters now that duels are between different players' companions.
            var owner = companion.OwnerName;
            if (!string.IsNullOrEmpty(owner))
                __result += $"  <color=#B0B0B0>({owner})</color>";

            __result += $"  <color=#FFD24A>★{companion.Level}</color>";

            // Optional ladder rank (docs/Ranking.md) — read off the client snapshot.
            if (Plugin.ShowRankOnNameTag != null && Plugin.ShowRankOnNameTag.Value)
            {
                var rank = Ranking.LeaderboardStore.RankOf(companion.CompanionId);
                if (rank > 0) __result += $"  <color=#7FD0FF>#{rank}</color>";
            }
        }
    }

    // 2. Suppress vanilla's star rects on companion huds. UpdateHuds re-evaluates
    //    them from GetLevel() every frame, so this postfix re-hides them every
    //    frame for companions only.
    [HarmonyPatch(typeof(EnemyHud), "UpdateHuds")]
    public static class CompanionHideVanillaStarsPatch
    {
        public static void Postfix(EnemyHud __instance)
        {
            if (__instance == null || __instance.m_huds == null) return;

            foreach (var kv in __instance.m_huds)
            {
                var character = kv.Key;
                if (character == null) continue;
                if (character.GetComponent<DvergrCompanion>() == null) continue;

                var hud = kv.Value;
                if (hud.m_level2 != null) hud.m_level2.gameObject.SetActive(false);
                if (hud.m_level3 != null) hud.m_level3.gameObject.SetActive(false);
            }
        }
    }
}
