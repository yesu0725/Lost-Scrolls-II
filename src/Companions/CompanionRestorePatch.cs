using HarmonyLib;

namespace LostScrollsII.Companions
{
    // Fixes "communed Dvergr reverts to uncommuned ([G] Communion) after relog".
    // A recruited Dvergr's freed state (player faction, calmed AI, the
    // DvergrCompanion behavior component) is runtime-only and not persisted by
    // vanilla — only our DE_Recruited ZDO flag survives. Every Dvergr that
    // re-spawns therefore comes back as a plain hostile creature. MonsterAI.Start
    // runs once per spawn AFTER ZNetView has registered the ZDO, so it is the
    // safe place to read that flag and rebuild the companion.
    [HarmonyPatch(typeof(MonsterAI), nameof(MonsterAI.Start))]
    public static class CompanionRestorePatch
    {
        public static void Postfix(MonsterAI __instance)
        {
            CommunionService.RestoreCompanion(__instance.GetComponent<Character>());
        }
    }
}
