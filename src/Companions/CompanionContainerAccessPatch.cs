using HarmonyLib;
using LostScrollsII.Companions;

namespace LostScrollsII.Patches
{
    // The companion's storage Container lives on the same GameObject as the
    // creature so it can share the creature's ZNetView (for ZDO persistence and
    // sync). That makes it a competing Hoverable/Interactable: without these
    // patches, looking at a companion would show the container's "[E] Open" hint
    // and pressing the interact key would open its bag with NO owner check — and
    // clash with the stance key. We suppress both vanilla paths so the ONLY way in
    // is the owner-gated Y handler (Plugin.HandleInventoryInput), which opens the
    // same panel via InventoryGui.Show.
    [HarmonyPatch(typeof(Container), nameof(Container.GetHoverText))]
    public static class CompanionContainerHoverTextPatch
    {
        public static bool Prefix(Container __instance, ref string __result)
        {
            if (__instance.GetComponent<DvergrCompanion>() == null) return true; // real chest
            __result = string.Empty; // companion hover text comes from the Character patch instead
            return false;
        }
    }

    [HarmonyPatch(typeof(Container), nameof(Container.GetHoverName))]
    public static class CompanionContainerHoverNamePatch
    {
        public static bool Prefix(Container __instance, ref string __result)
        {
            if (__instance.GetComponent<DvergrCompanion>() == null) return true;
            __result = string.Empty;
            return false;
        }
    }

    [HarmonyPatch(typeof(Container), nameof(Container.Interact))]
    public static class CompanionContainerInteractPatch
    {
        // Refuse the vanilla interact entirely for companion bags — opening is
        // owner-gated through our own key handler.
        public static bool Prefix(Container __instance, ref bool __result)
        {
            if (__instance.GetComponent<DvergrCompanion>() == null) return true;
            __result = false;
            return false;
        }
    }
}
