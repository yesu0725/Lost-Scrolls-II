using System;

namespace LostScrollsII.Economy
{
    // The one server-side place that takes and pays wager stakes, so the two
    // currencies' very different mechanics (see Wager) stay in one file instead of
    // being re-derived at every call site.
    //
    // Charge()
    //   Coins   — nothing to do here. Vanilla Coins are inventory items and only
    //             the owning client can remove them, so the client takes the stake
    //             before it sends its join/accept and merely REPORTS the amount.
    //             We accept the report and record the debt so it can be refunded.
    //             (Same trust model as the rest of the competitive suite: clients
    //             already report their own duel results. A forged stake cheats a
    //             player out of nothing that isn't refunded to them anyway.)
    //   Valcoin — a real remote debit, asynchronous, and authoritative. The join is
    //             not accepted until the ledger answers. This is the reason every
    //             wager entry point is callback-shaped rather than returning a bool.
    //
    // Payout()/Refund()
    //   Coins   — server tells the owning client to add the items (LSII_WagerPay),
    //             which drops them at the player's feet if the pack is full.
    //   Valcoin — credited through the donations ledger.
    //
    // Everything here must run on the server/host. Nothing in this class is safe
    // to call from a pure client.
    public static class WagerService
    {
        // Is this currency usable for a new wager right now? Coins always are;
        // Valcoins need the donations wallet API present and ready.
        public static bool IsAvailable(WagerCurrency currency, out string reason)
        {
            reason = null;
            if (!Plugin.WagersEnabled.Value)
            {
                reason = "Wagered events are disabled on this server.";
                return false;
            }
            if (currency == WagerCurrency.Valcoin)
            {
                reason = ValcoinBridge.UnavailableReason;
                return reason == null;
            }
            if (currency == WagerCurrency.Coins) return true;
            reason = "Unknown currency.";
            return false;
        }

        // What the UI should tell a player about a currency, which is NOT the same
        // question IsAvailable answers.
        //
        // IsAvailable is server truth. On a pure client it would be wrong in the
        // worst direction: Valcoin wagers are settled entirely server-side, so a
        // client that simply doesn't have the donations mod installed itself would
        // report "unavailable" for a currency the server handles perfectly well.
        // A client therefore stays quiet and lets the server refuse if it must —
        // the only thing it can honestly judge is the master switch.
        public static string ClientHint(WagerCurrency currency)
        {
            if (!Plugin.WagersEnabled.Value) return "Wagered events are disabled.";
            bool isServer = ZNet.instance != null && ZNet.instance.IsServer();
            if (!isServer) return null;
            IsAvailable(currency, out var reason);
            return reason;
        }

        // Take a stake. `done(ok, message)` may fire synchronously (Coins) or later
        // (Valcoin) — callers must handle both and must not treat the wager as
        // settled until it fires with true.
        //
        // `clientReportedPaid` is the amount the joining client says it already
        // removed from its own inventory; only meaningful for Coins.
        public static void Charge(WagerCurrency currency, string playerName, int amount,
            string sku, string reason, int clientReportedPaid, Action<bool, string> done)
        {
            if (amount <= 0) { done?.Invoke(true, null); return; }

            if (!IsAvailable(currency, out var why)) { done?.Invoke(false, why); return; }

            if (currency == WagerCurrency.Coins)
            {
                if (clientReportedPaid < amount)
                {
                    done?.Invoke(false, $"You need {amount} Coins to enter.");
                    return;
                }
                Plugin.Log.LogInfo($"[wager] {playerName} staked {amount} Coins ({sku}).");
                done?.Invoke(true, null);
                return;
            }

            ValcoinBridge.Charge(playerName, sku, amount, reason, (ok, msg) =>
            {
                if (ok) Plugin.Log.LogInfo($"[wager] {playerName} staked {amount} Valcoins ({sku}).");
                else Plugin.Log.LogInfo($"[wager] {playerName} could not stake {amount} Valcoins ({sku}): {msg}");
                done?.Invoke(ok, msg);
            });
        }

        // Pay a player — a purse, a champion's prize, or a refund. Fire and forget:
        // there is nothing sensible to do if a payout fails beyond logging loudly,
        // which is exactly what happens (a lost payout must be visible in the log so
        // an admin can settle it by hand).
        public static void Pay(WagerCurrency currency, long ownerId, string playerName,
            int amount, string sku, string reason)
        {
            if (amount <= 0) return;
            if (currency == WagerCurrency.None)
            {
                // Nothing was ever staked (the free tournament), so there is nothing
                // to pay. Guarded explicitly because falling through would take the
                // Valcoin branch and log a phantom debt.
                Plugin.Log.LogWarning($"[wager] ignoring a {amount} payout to {playerName} with no currency ({reason}).");
                return;
            }

            if (currency == WagerCurrency.Coins)
            {
                Ranking.LeaderboardSync.SendWagerPayout(ownerId, amount, reason);
                Plugin.Log.LogInfo($"[wager] paid {amount} Coins to {playerName} ({reason}).");
                return;
            }

            if (!ValcoinBridge.Available)
            {
                Plugin.Log.LogWarning($"[wager] OWED {amount} Valcoins to {playerName} ({reason}) but the " +
                    "Valcoin wallet is unavailable — settle this by hand.");
                return;
            }

            ValcoinBridge.Credit(playerName, sku, amount, reason, (ok, msg) =>
            {
                if (ok) Plugin.Log.LogInfo($"[wager] paid {amount} Valcoins to {playerName} ({reason}).");
                else Plugin.Log.LogWarning($"[wager] FAILED to pay {amount} Valcoins to {playerName} ({reason}): {msg} " +
                    "— settle this by hand.");
            });
        }

        // Refund is a payout of a stake this system took. Kept as its own entry
        // point so refunds are greppable in the log separately from prizes.
        public static void Refund(WagerCurrency currency, long ownerId, string playerName, int amount, string what)
            => Pay(currency, ownerId, playerName, amount, ValcoinBridge.SkuRefund, $"refund: {what}");

        // Roll back a charge THIS SERVER made, after the request it was for turned
        // out to be impossible (the tournament filled / was cancelled / the player
        // was already committed elsewhere while a remote ledger call was in flight).
        //
        // The distinction from Refund() matters and is easy to get wrong:
        //
        //   Valcoins were debited HERE, so only this code can put them back.
        //   Coins were never taken here at all — the client removed them from its
        //   own inventory before it asked. The client-facing layer refunds those on
        //   any rejection, because it is the only place that knows what the client
        //   actually gave up. Refunding them here TOO pays the player twice.
        //
        // So: whoever took it, refunds it.
        public static void RollBackOwnCharge(WagerCurrency currency, long ownerId, string playerName, int amount, string what)
        {
            if (currency != WagerCurrency.Valcoin) return;
            Refund(currency, ownerId, playerName, amount, what);
        }
    }
}
