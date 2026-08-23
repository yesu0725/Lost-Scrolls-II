using UnityEngine;

namespace LostScrollsII.Economy
{
    // The two currencies a tournament or a duel invite can be staked in
    // (docs/Wagers.md). Deliberately only two, and deliberately never mixed
    // within one event: a Valcoin tournament pays Valcoins, a Coin tournament
    // pays Coins.
    public enum WagerCurrency
    {
        None = 0,
        Coins = 1,    // vanilla `Coins` item, held in the player's inventory
        Valcoin = 2,  // Valheim Donations' server-side ledger (see ValcoinBridge)
    }

    // Shared currency helpers: naming, the configured stakes, and the vanilla-coin
    // inventory operations.
    //
    // The split between the two currencies runs through everything downstream, so
    // it is worth stating once here:
    //
    //   Coins are ITEMS. They live in a player's inventory, which only that
    //   player's client can modify — so a Coin stake is taken CLIENT-side before
    //   the join RPC is sent, and paid back / paid out by telling a client to add
    //   them (LSII_WagerPay). The server records only the amount owed.
    //
    //   Valcoins are a LEDGER. They live on the donations backend, so a Valcoin
    //   stake is taken SERVER-side and asynchronously (ValcoinBridge), and the
    //   join is only accepted once the charge actually settles.
    //
    // Every wager path therefore has a synchronous branch and an asynchronous one.
    // Mixing them up is the easy mistake here; WagerService owns that distinction
    // so nothing above it has to.
    public static class Wager
    {
        public const string CoinPrefab = "Coins";

        private static string _coinSharedName;

        public static string Key(WagerCurrency c) => c == WagerCurrency.Valcoin ? "valcoin" : "coins";

        public static WagerCurrency Parse(string s)
        {
            if (string.IsNullOrEmpty(s)) return WagerCurrency.None;
            switch (s.ToLowerInvariant())
            {
                case "valcoin": case "valcoins": return WagerCurrency.Valcoin;
                case "coins": case "coin": return WagerCurrency.Coins;
                default: return WagerCurrency.None;
            }
        }

        public static string Display(WagerCurrency c)
            => c == WagerCurrency.Valcoin ? "Valcoins" : "Coins";

        // ---- configured stakes ------------------------------------------------

        public static int TournamentFee(WagerCurrency c) => c == WagerCurrency.Valcoin
            ? Mathf.Max(0, Plugin.TournamentValcoinFee.Value)
            : Mathf.Max(0, Plugin.TournamentCoinFee.Value);

        // The champion's purse. For Coins this is what the mod actually grants.
        // For Valcoins it is DISPLAY ONLY: the payout is minted through the
        // donations mod's own quest table (`VC.Q.ls_tournament_prize` in
        // valcoin_quests.yaml), which is the one place allowed to price a Valcoin
        // reward. Keep the two numbers in step — see docs/Wagers.md.
        public static int TournamentPrize(WagerCurrency c) => c == WagerCurrency.Valcoin
            ? Mathf.Max(0, Plugin.TournamentValcoinPrize.Value)
            : Mathf.Max(0, Plugin.TournamentCoinPrize.Value);

        public static int DuelStake(WagerCurrency c) => c == WagerCurrency.Valcoin
            ? Mathf.Max(0, Plugin.DuelValcoinStake.Value)
            : Mathf.Max(0, Plugin.DuelCoinStake.Value);

        // ---- vanilla Coins (client-side, inventory items) ---------------------

        // Resolved by SHARED NAME for the same reason the wisp count is: an item
        // instance loaded from a ZDO can have a null m_dropPrefab.
        public static string CoinSharedName()
        {
            if (!string.IsNullOrEmpty(_coinSharedName)) return _coinSharedName;
            var prefab = ObjectDB.instance != null ? ObjectDB.instance.GetItemPrefab(CoinPrefab) : null;
            var shared = prefab != null ? prefab.GetComponent<ItemDrop>()?.m_itemData?.m_shared : null;
            if (shared != null) _coinSharedName = shared.m_name;
            return _coinSharedName;
        }

        public static int CoinsHeld(Player player)
        {
            var name = CoinSharedName();
            if (player == null || string.IsNullOrEmpty(name)) return 0;
            var inv = player.GetInventory();
            return inv == null ? 0 : inv.CountItems(name);
        }

        // Takes `amount` Coins from the player. All-or-nothing: returns false and
        // takes nothing if they are short.
        public static bool TryTakeCoins(Player player, int amount)
        {
            if (player == null || amount <= 0) return false;
            var name = CoinSharedName();
            if (string.IsNullOrEmpty(name)) return false;
            var inv = player.GetInventory();
            if (inv == null || inv.CountItems(name) < amount) return false;
            inv.RemoveItem(name, amount);
            return true;
        }

        // Gives `amount` Coins to the player, dropping at their feet if the pack is
        // full — a refund or a purse must never evaporate because of inventory space.
        //
        // Paid out in MAX-STACK chunks rather than one oversized stack: a tournament
        // purse can be the whole stack cap, and handing Inventory an item whose
        // m_stack exceeds m_maxStackSize is asking for a silently clamped payout.
        public static void GiveCoins(Player player, int amount)
        {
            if (player == null || amount <= 0) return;
            var prefab = ObjectDB.instance != null ? ObjectDB.instance.GetItemPrefab(CoinPrefab) : null;
            var drop = prefab != null ? prefab.GetComponent<ItemDrop>() : null;
            if (drop == null)
            {
                Plugin.Log.LogWarning($"[wager] '{CoinPrefab}' prefab missing — cannot pay {amount} coins.");
                return;
            }

            int max = Mathf.Max(1, drop.m_itemData.m_shared.m_maxStackSize);
            var inv = player.GetInventory();
            int left = amount;

            while (left > 0)
            {
                int chunk = Mathf.Min(left, max);
                left -= chunk;

                var item = drop.m_itemData.Clone();
                item.m_dropPrefab = prefab;
                item.m_stack = chunk;
                item.m_quality = 1;
                item.m_worldLevel = (byte)Game.m_worldLevel;

                if (inv == null || !inv.AddItem(item))
                    ItemDrop.DropItem(item, chunk, player.transform.position + Vector3.up, player.transform.rotation);
            }
        }
    }
}
