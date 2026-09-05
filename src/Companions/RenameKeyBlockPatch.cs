using HarmonyLib;
using LostScrollsII.Companions;
using UnityEngine;

namespace LostScrollsII.Patches
{
    // Kills raw keyboard polling while the player is typing a companion's name.
    //
    // WHY THIS IS SEPARATE FROM THE ZInput GATE. Vanilla reads its binds through
    // ZInput, and ModalPanels' postfixes cover that — which is what stops `E` from
    // closing the container panel. But a BepInEx mod with its own hotkey almost
    // always reads `UnityEngine.Input.GetKeyDown(someConfiguredKey)` directly, and
    // no amount of ZInput gating touches that. Reported from a live session: with
    // the rename field armed, other mods' hotkeys were still firing on the letters
    // being typed.
    //
    // So the keyboard itself is muted, and ONLY for the length of an explicit
    // rename. This is a blunt instrument aimed at a narrow window: the player has
    // clicked "Rename", the field is armed, and until they click "Save" a keypress
    // means a letter and nothing else.
    //
    // WHAT IS DELIBERATELY NOT BLOCKED:
    //   * mouse buttons — the Save button has to be clickable, and the EventSystem
    //     drives it through GetMouseButton*, not GetKey*;
    //   * `Input.inputString` / the Event queue, which is how TMP_InputField
    //     actually receives characters. Muting GetKey* does not stop typing.
    //
    // Postfixes at Priority.Last, for the same reason the ZInput gate is one: a
    // prefix returning false is skipped the moment another mod's prefix returns
    // false first, and that is exactly how the earlier version of this gate came to
    // do nothing at all on a live server.
    internal static class RenameKeyBlock
    {
        public static bool Active => CompanionInventoryGui.IsTyping;
    }

    [HarmonyPatch(typeof(Input), nameof(Input.GetKeyDown), new[] { typeof(KeyCode) })]
    public static class RenameBlocksKeyDownPatch
    {
        [HarmonyPriority(Priority.Last)]
        public static void Postfix(ref bool __result)
        {
            if (RenameKeyBlock.Active) __result = false;
        }
    }

    [HarmonyPatch(typeof(Input), nameof(Input.GetKey), new[] { typeof(KeyCode) })]
    public static class RenameBlocksKeyPatch
    {
        [HarmonyPriority(Priority.Last)]
        public static void Postfix(ref bool __result)
        {
            if (RenameKeyBlock.Active) __result = false;
        }
    }

    [HarmonyPatch(typeof(Input), nameof(Input.GetKeyUp), new[] { typeof(KeyCode) })]
    public static class RenameBlocksKeyUpPatch
    {
        [HarmonyPriority(Priority.Last)]
        public static void Postfix(ref bool __result)
        {
            if (RenameKeyBlock.Active) __result = false;
        }
    }

    // The string overloads too: `Input.GetKeyDown("e")` is rarer but legal, and a
    // gate that covers only half the API is the kind that looks like it works.
    [HarmonyPatch(typeof(Input), nameof(Input.GetKeyDown), new[] { typeof(string) })]
    public static class RenameBlocksNamedKeyDownPatch
    {
        [HarmonyPriority(Priority.Last)]
        public static void Postfix(ref bool __result)
        {
            if (RenameKeyBlock.Active) __result = false;
        }
    }

    [HarmonyPatch(typeof(Input), nameof(Input.GetKey), new[] { typeof(string) })]
    public static class RenameBlocksNamedKeyPatch
    {
        [HarmonyPriority(Priority.Last)]
        public static void Postfix(ref bool __result)
        {
            if (RenameKeyBlock.Active) __result = false;
        }
    }

    [HarmonyPatch(typeof(Input), nameof(Input.GetKeyUp), new[] { typeof(string) })]
    public static class RenameBlocksNamedKeyUpPatch
    {
        [HarmonyPriority(Priority.Last)]
        public static void Postfix(ref bool __result)
        {
            if (RenameKeyBlock.Active) __result = false;
        }
    }
}
