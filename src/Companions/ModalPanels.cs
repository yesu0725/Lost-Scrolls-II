namespace LostScrollsII.Companions
{
    // Which of our full-screen panels currently owns input.
    //
    // The input-capture patches (TournamentPanelInputPatches) are deliberately shared
    // rather than duplicated per panel: they encode two fixes that were only found by
    // testing on a live server — that PlayerController.TakeInput is a *different*
    // method from Player.TakeInput (forcing only the latter froze the camera but left
    // click-to-attack and WASD live), and that the ZInput gate must be a postfix
    // because another mod on the server out-orders a prefix. A second copy of that
    // logic would drift from those fixes the first time one of them was revisited.
    //
    // Any new modal panel should be added here rather than growing its own patch set.
    public static class ModalPanels
    {
        // Renaming a companion counts. It is not a full-screen panel, but it wants
        // exactly the same thing: every bind dead until the player is done typing.
        // It used to carry its own ZInput prefixes (CompanionTypingInputPatch, now
        // deleted) and they silently did nothing on a live server — a prefix that
        // returns false is skipped when another mod's prefix returns false first,
        // which is the very reason the patches here are postfixes. Two copies of one
        // gate is how that fix got lost; there is now one.
        public static bool AnyOpen =>
            TournamentRegistration.IsOpen
            || Bounty.BountyBoardPanel.IsOpen
            || CompanionInventoryGui.IsTyping;
    }
}
