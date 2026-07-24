using HarmonyLib;
using LostScrollsII.Companions;
using UnityEngine;

namespace LostScrollsII.Patches
{
    // While the tournament panel (TournamentRegistration, F7) is open, take input
    // over the way a vanilla menu does: free the mouse cursor so its buttons can be
    // clicked, and block the camera + all player controls underneath it. Two
    // vanilla chokepoints (both confirmed present in the game assembly):
    //
    //   * Player.TakeInput() -> false: the single gate vanilla itself uses when the
    //     inventory/menu is open. Forcing it false stops movement, mouse-look,
    //     attack, block, use/interact, jump, dodge and hotbar keys.
    //   * GameCamera.UpdateMouseCapture(): skipped while open, with the cursor
    //     forced free, so the camera doesn't re-capture/hide the cursor every frame
    //     (it runs in LateUpdate, so a per-frame set from our own Update would lose
    //     the race — patching it is the reliable fix).
    //
    // Our own hotkeys (F7 to toggle closed, Escape to close) and the panel's uGUI
    // buttons don't go through TakeInput, so closing + clicking still work.
    [HarmonyPatch(typeof(Player), nameof(Player.TakeInput))]
    public static class TournamentPanelBlocksInputPatch
    {
        public static void Postfix(ref bool __result)
        {
            if (TournamentRegistration.IsOpen) __result = false;
        }
    }

    // The DEFINITIVE attack/movement gate. PlayerController.FixedUpdate only feeds
    // attack, movement, block, jump, dodge, crouch and run into SetControls when its
    // OWN private TakeInput() returns true, and its LateUpdate gates camera-look on
    // the same method — this is a DIFFERENT method from Player.TakeInput() above,
    // which is why forcing that one false froze the camera but left click-to-attack
    // and WASD fully live. Force this false while the panel is open so nothing
    // reaches SetControls.
    //
    // Done as a postfix on the gate (not by swallowing ZInput below) on purpose:
    // another mod on this server (Valcoin) also prefixes ZInput.GetButton, and when
    // two prefixes hook one method the ordering decides who wins — our ZInput swallow
    // was being out-ordered, so attack still fired. A postfix overrides __result
    // unconditionally and can't be out-ordered.
    [HarmonyPatch(typeof(PlayerController), "TakeInput", new[] { typeof(bool) })]
    public static class TournamentPanelBlocksControllerInputPatch
    {
        public static void Postfix(ref bool __result)
        {
            if (TournamentRegistration.IsOpen) __result = false;
        }
    }

    [HarmonyPatch(typeof(GameCamera), "UpdateMouseCapture")]
    public static class TournamentPanelFreesCursorPatch
    {
        public static bool Prefix()
        {
            if (!TournamentRegistration.IsOpen) return true;
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
            return false; // the panel owns the cursor while it's open
        }
    }

    // Mouse-look in this build isn't fully gated by Player.TakeInput, so the camera
    // still rotates with the mouse. Player.SetMouseLook is the method that applies
    // the look delta to the player (which the camera follows) — skip it entirely
    // while the panel is open so the camera holds still for clicking.
    [HarmonyPatch(typeof(Player), nameof(Player.SetMouseLook))]
    public static class TournamentPanelBlocksLookPatch
    {
        public static bool Prefix()
        {
            return !TournamentRegistration.IsOpen; // false = skip vanilla look
        }
    }

    // Game button actions (attack, block, jump, use, hotbar, …) are read straight
    // from ZInput, NOT gated by Player.TakeInput — so clicking a panel button was
    // also swinging the equipped weapon. Swallow ZInput's button getters while the
    // panel is open, exactly as chat/console do while focused. Our panel's buttons
    // (uGUI EventSystem) and its F7/Escape keys use UnityEngine.Input, so they're
    // unaffected.
    [HarmonyPatch(typeof(ZInput), nameof(ZInput.GetButton), new[] { typeof(string) })]
    public static class TournamentPanelBlocksButtonPatch
    {
        public static bool Prefix(ref bool __result)
        {
            if (!TournamentRegistration.IsOpen) return true;
            __result = false;
            return false;
        }
    }

    [HarmonyPatch(typeof(ZInput), nameof(ZInput.GetButtonDown), new[] { typeof(string) })]
    public static class TournamentPanelBlocksButtonDownPatch
    {
        public static bool Prefix(ref bool __result)
        {
            if (!TournamentRegistration.IsOpen) return true;
            __result = false;
            return false;
        }
    }

    [HarmonyPatch(typeof(ZInput), nameof(ZInput.GetButtonUp), new[] { typeof(string) })]
    public static class TournamentPanelBlocksButtonUpPatch
    {
        public static bool Prefix(ref bool __result)
        {
            if (!TournamentRegistration.IsOpen) return true;
            __result = false;
            return false;
        }
    }
}
