using UnityEngine;

namespace LostScrollsII.Bounty
{
    // The quest gate on bounty hunting (docs/Bounty-Hunting.md, Phase G).
    //
    // The Wanted Board stays shut until a player has heard why these Dvergr are
    // hunted and answered one themselves. The narrative half lives entirely in
    // ServerGuide (`guidance.bounty.yaml`, an npc_conversation on Haldor); this class
    // is the mechanical half.
    //
    // HOW THE TWO HALVES TALK, without inventing a new API:
    //
    //   ServerGuide -> mod : the dialogue's final choice grants a stock
    //       `set_player_key` reward, which is just Player.AddUniqueKey. We watch for
    //       that key, act on it, and clear it. Exactly the mechanism Valheim
    //       Donations already uses for its quest bridge — no new trigger type, no
    //       coupling to break.
    //
    //   mod -> ServerGuide : nothing to add. ServerGuide has no reverse-query API
    //       ("has this player finished chain X?"), and this design doesn't need one:
    //       the quest completes when the tutorial bounty is answered, which is an
    //       event the mod already observes because it posted that bounty itself.
    //
    // Both flags are Valheim unique keys, so they persist per character with the save
    // and cost us no storage of our own.
    public static class BountyQuestGate
    {
        // Set by the Haldor dialogue when the player takes the warden's commission.
        public const string KeyStartRequested = "LS_BountyStart";
        // Set by us once that first bounty is answered — this is the real gate.
        public const string KeyUnlocked = "LS_BountyUnlocked";

        // Marks the posting created for the quest, so answering THAT one is what
        // opens the board (rather than any bounty a player might stumble into).
        public const string TutorialPostingTag = "tutorial";

        public static bool IsUnlocked(Player player)
            => player != null && player.HaveUniqueKey(KeyUnlocked);

        public static bool HasPendingStart(Player player)
            => player != null && player.HaveUniqueKey(KeyStartRequested);

        // Called when the quest bounty is answered. Idempotent — a second call is
        // harmless, which matters because resolution can be observed more than once
        // across a session.
        public static void Unlock(Player player)
        {
            if (player == null || player.HaveUniqueKey(KeyUnlocked)) return;
            player.AddUniqueKey(KeyUnlocked);
            // The commission is finished; drop the request flag so a later relog
            // doesn't try to hand out another first bounty.
            player.RemoveUniqueKey(KeyStartRequested);
            Plugin.Log.LogInfo($"[bounty] {player.GetPlayerName()} has completed the warden's commission — the board is open to them.");
        }

        // Consume the dialogue's request flag. Returns true exactly once per grant,
        // so the tutorial posting is created a single time.
        public static bool ConsumeStartRequest(Player player)
        {
            if (player == null || !player.HaveUniqueKey(KeyStartRequested)) return false;
            player.RemoveUniqueKey(KeyStartRequested);
            return true;
        }

        // Admin/debug escape hatch: re-open the commission for a player who somehow
        // lost their posting (abandoned it before it ever spawned, say).
        public static void Reset(Player player)
        {
            if (player == null) return;
            player.RemoveUniqueKey(KeyUnlocked);
            player.RemoveUniqueKey(KeyStartRequested);
        }
    }
}
