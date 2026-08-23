using System;
using System.Reflection;
using BepInEx.Bootstrap;

namespace LostScrollsII.Economy
{
    // Soft-dependency bridge to Valheim Donations' server-side wallet API
    // (`ValcoinWallet` in that plugin — see docs/Wagers.md).
    //
    // WHY REFLECTION, not an assembly reference: Lost Scrolls II ships as a
    // standalone gameplay mod. A hard reference would make the donations plugin a
    // load-time requirement for everyone, and the whole bounty feature already
    // establishes the pattern that donations is optional and probed at runtime.
    // Every member is looked up once and cached; if the type or a method is
    // missing (donations absent, or an older build without the wallet API), the
    // bridge simply reports unavailable and the Valcoin option is refused with a
    // clear reason instead of throwing.
    //
    // WHAT THIS DOES NOT DO: it never reads or writes a balance itself, and it
    // never decides an amount that is *credited*. Charges name an amount because a
    // wager stake has to; the champion's Valcoin purse is minted through the
    // donations mod's own quest table instead (see Wager.TournamentPrize), which
    // keeps this mod's long-standing rule — it does not price Valcoin rewards.
    // Credit() here is used only to hand back a stake this bridge itself took.
    //
    // Everything is SERVER-SIDE. The donations API refuses off-server anyway, but
    // callers should already be on the authoritative path (WagerService).
    public static class ValcoinBridge
    {
        private const string DonationsGuid = "com.taeguk.valheimdonations";
        private const string WalletTypeName = "ValcoinWallet";   // global namespace in that assembly

        // Sku suffixes (the donations side namespaces them with "eco_"). These end
        // up in its ledger, so keep them stable and readable.
        public const string SkuTournamentEntry = "ls_tourney_entry";
        public const string SkuDuelStake = "ls_duel_stake";
        public const string SkuRefund = "ls_wager_refund";
        public const string SkuDuelPurse = "ls_duel_purse";

        private static bool _probed;
        private static Type _wallet;
        private static MethodInfo _charge;
        private static MethodInfo _credit;
        private static PropertyInfo _ready;
        private static PropertyInfo _reason;

        // True when the donations plugin is loaded, exposes the wallet API, and
        // reports itself ready (server + backend configured).
        public static bool Available
        {
            get
            {
                Probe();
                if (_wallet == null || _charge == null || _credit == null) return false;
                try { return _ready == null || (bool)_ready.GetValue(null); }
                catch { return false; }
            }
        }

        // A player-safe explanation of why Valcoin wagers are off, or null when
        // they are available.
        public static string UnavailableReason
        {
            get
            {
                Probe();
                if (_wallet == null || _charge == null || _credit == null)
                    return "Valcoin wagers need the Valheim Donations mod on this server.";
                try
                {
                    if (_ready != null && !(bool)_ready.GetValue(null))
                        return _reason?.GetValue(null) as string
                               ?? "Valcoin wagers are not available on this server right now.";
                }
                catch { return "Valcoin wagers are not available on this server right now."; }
                return null;
            }
        }

        // Debit a stake. `done(ok, message)` fires once the remote ledger answers —
        // NEVER synchronously on success, so callers must treat the wager as
        // unsettled until it arrives (a prize paid before the charge lands is a
        // prize nobody paid for).
        public static void Charge(string playerName, string sku, int coins, string reason, Action<bool, string> done)
        {
            if (!Available) { done?.Invoke(false, UnavailableReason); return; }
            Invoke(_charge, playerName, sku, coins, reason, done);
        }

        // Hand a stake back (refund), or pay a duel purse that was itself collected
        // from the other duelist's stake — no coins are created either way.
        public static void Credit(string playerName, string sku, int coins, string reason, Action<bool, string> done)
        {
            if (!Available) { done?.Invoke(false, UnavailableReason); return; }
            Invoke(_credit, playerName, sku, coins, reason, done);
        }

        private static void Invoke(MethodInfo m, string playerName, string sku, int coins,
            string reason, Action<bool, string> done)
        {
            try
            {
                m.Invoke(null, new object[] { playerName, sku, coins, reason, done });
            }
            catch (Exception e)
            {
                var inner = e.InnerException ?? e;
                Plugin.Log.LogWarning($"[wager] Valcoin call failed ({sku}, {coins} for {playerName}): {inner.Message}");
                done?.Invoke(false, "The Valcoin ledger could not be reached.");
            }
        }

        private static void Probe()
        {
            if (_probed) return;
            _probed = true;
            try
            {
                if (!Chainloader.PluginInfos.ContainsKey(DonationsGuid))
                {
                    // The GUID is the documented one, but tolerate a renamed build
                    // the way BountyFeatureGate does — look for the plugin by name.
                    foreach (var kv in Chainloader.PluginInfos)
                    {
                        var name = kv.Value?.Metadata?.Name ?? string.Empty;
                        if (name.IndexOf("donation", StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            Bind(kv.Value.Instance?.GetType().Assembly);
                            break;
                        }
                    }
                }
                else
                {
                    Bind(Chainloader.PluginInfos[DonationsGuid]?.Instance?.GetType().Assembly);
                }

                Plugin.Log.LogInfo(_wallet != null
                    ? "[wager] Valcoin wallet API found — Valcoin wagers are available."
                    : "[wager] No Valcoin wallet API found — Valcoin wagers will be refused (Coins still work).");
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning($"[wager] Valcoin bridge probe failed: {e.Message}");
            }
        }

        private static void Bind(Assembly asm)
        {
            if (asm == null) return;
            _wallet = asm.GetType(WalletTypeName, false);
            if (_wallet == null) return;

            var sig = new[] { typeof(string), typeof(string), typeof(int), typeof(string), typeof(Action<bool, string>) };
            _charge = _wallet.GetMethod("Charge", BindingFlags.Public | BindingFlags.Static, null, sig, null);
            _credit = _wallet.GetMethod("Credit", BindingFlags.Public | BindingFlags.Static, null, sig, null);
            _ready = _wallet.GetProperty("Ready", BindingFlags.Public | BindingFlags.Static);
            _reason = _wallet.GetProperty("UnavailableReason", BindingFlags.Public | BindingFlags.Static);

            if (_charge == null || _credit == null)
            {
                Plugin.Log.LogWarning("[wager] Found ValcoinWallet but not its Charge/Credit(string,string,int,string,Action<bool,string>) " +
                    "methods — the donations plugin is probably older than this build. Valcoin wagers stay off.");
                _wallet = null;
            }
        }
    }
}
