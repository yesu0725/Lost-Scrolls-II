using HarmonyLib;
using LostScrollsII.Companions;

namespace LostScrollsII.Patches
{
    // While the player is typing in the companion inventory panel's rename field,
    // swallow vanilla game button actions so keystrokes only edit the name.
    //
    // Our own hotkeys are already gated in Plugin.Update (via IsTyping), but the
    // game reacts to raw button binds through ZInput independently — notably
    // InventoryGui.Update closes the open container on the "Use" bind (E) and the
    // "Inventory" bind. Our injected field is a plain GuiInputField the game's input
    // gate (chat/console/TextInput) knows nothing about, so those still fired and
    // pressing E closed the panel mid-rename. Chat and the console suppress ZInput
    // the same way while focused; we mirror that, but only for the brief moment the
    // name field has focus.
    [HarmonyPatch(typeof(ZInput), nameof(ZInput.GetButtonDown))]
    public static class CompanionTypingButtonDownPatch
    {
        public static bool Prefix(ref bool __result)
        {
            if (!CompanionInventoryGui.IsTyping) return true;
            __result = false;
            return false; // discrete button presses do nothing while renaming
        }
    }
}
