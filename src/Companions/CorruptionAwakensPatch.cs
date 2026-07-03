using HarmonyLib;
using LostScrollsII.Companions;
using UnityEngine;

namespace LostScrollsII.Patches
{
    // Lore beat: when an UNRECRUITED Dvergr first turns hostile (becomes
    // aggravated), surface a short message framing WHY it rages — the corruption
    // sleeping within it has been roused. This is the never-named gospel-allegory
    // idea from docs/Lore.md made diegetic: the Dvergr aren't hostile by nature,
    // they carry a corruption that wakes when provoked.
    //
    // BaseAI.SetAggravated is the single point every aggravation flows through
    // (direct hits via OnDamaged, and the camp-wide AggravateAllInArea sweep). We
    // fire only on a genuine false->true transition (the pre-call m_aggravated is
    // still readable in the prefix), only for a real un-freed Dvergr, only when the
    // local player is nearby, and at most once every few seconds so a cluster all
    // waking at once doesn't spam.
    [HarmonyPatch(typeof(BaseAI), nameof(BaseAI.SetAggravated))]
    public static class CorruptionAwakensPatch
    {
        // Seconds between messages, so a whole camp aggravating together shows one
        // line, not one per Dvergr.
        private const float Cooldown = 6f;
        // Only speak for a Dvergr the local player could plausibly have roused.
        private const float NearRange = 40f;

        private static float _lastShown = -999f;
        private static int _nextLine;

        private static readonly string[] Lines =
        {
            "Something old stirs in it — the corruption was never truly gone. Roused, it turns on you.",
            "You have woken the shadow sleeping in its blood. This is why they rage.",
            "The old corruption rises in it, provoked. It knows only wrath now.",
        };

        public static void Prefix(BaseAI __instance, bool aggro)
        {
            if (!aggro) return;
            // Genuine transition only — the pre-call flag is still false here. If it
            // was already aggravated, SetAggravated would no-op anyway.
            if (__instance.m_aggravated) return;

            var character = __instance.GetComponent<Character>();
            if (character == null || character.m_faction != Character.Faction.Dverger) return;

            // Freed allies are faction Players (so already excluded above) and carry
            // the component — belt-and-suspenders skip.
            if (character.GetComponent<DvergrCompanion>() != null) return;

            var local = Player.m_localPlayer;
            if (local == null || MessageHud.instance == null) return;
            if (Vector3.Distance(local.transform.position, character.transform.position) > NearRange) return;

            if (Time.time - _lastShown < Cooldown) return;
            _lastShown = Time.time;

            var line = Lines[_nextLine % Lines.Length];
            _nextLine++;
            MessageHud.instance.ShowMessage(MessageHud.MessageType.Center, line);
        }
    }
}
