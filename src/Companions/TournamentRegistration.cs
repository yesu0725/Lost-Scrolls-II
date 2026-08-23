using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using LostScrollsII.Economy;
using LostScrollsII.Ranking;

namespace LostScrollsII.Companions
{
    // The interactive competitive panel (docs/Tournaments.md, docs/Wagers.md).
    // Every player has this; it is the one surface for:
    //   * opening a staked tournament (Coins or Valcoins) — any player, not just
    //     admins,
    //   * entering one by locking a companion's Communion Totem into a slot,
    //   * marking yourself ready when you and your opponent have met up,
    //   * posting / accepting / withdrawing a staked duel invite,
    //   * and, for admins only, the free tournament's run controls.
    //
    // Because it is now the place a player commits real currency, the status text
    // deliberately spells out the whole flow — fees, purse, bracket size, refunds,
    // and above all HOW a duel actually happens (the two owners pick the venue and
    // both press Ready). A player should never have to read the wiki to know what a
    // button is about to cost them.
    //
    // Built on a self-contained uGUI Canvas with widgets CLONED from vanilla UI
    // (the InventoryGui weight label for text; its "Take All" button for buttons) —
    // no authored assets, matching the vanilla-assets-only constraint and the clone
    // approach in CompanionInventoryGui. The cursor is unlocked while open and our
    // hotkeys are suppressed (IsOpen); Escape closes it.
    //
    // Registration is escrow-based: LockTotem serializes the totem(s), removes them
    // from the player inventory, and sends them to the server (LeaderboardSync).
    // If the server rejects the entry it returns the totem(s) AND any Coin stake.
    public class TournamentRegistration : MonoBehaviour
    {
        private static TournamentRegistration _instance;
        public static bool IsOpen { get; private set; }

        private GameObject _root;      // canvas root
        private TMP_Text _status;
        private readonly List<GameObject> _buttons = new List<GameObject>();
        private TMP_Text _textSource;
        private Button _buttonSource;
        private string _lastSig;       // last rendered state signature (avoid per-frame rebuilds)

        // Client-side pick for the elimination type of the NEXT free tournament an
        // admin starts; only meaningful pre-start (once active, s.eliminationType is
        // authoritative). Wagered tournaments are always single elimination.
        private static readonly string[] EliminationTypes = { "single", "double", "round_robin" };
        private static string DisplayType(string t) => t switch { "double" => "Double Elim", "round_robin" => "Round Robin", _ => "Single Elim" };
        private int _pendingTypeIndex;

        // Panel geometry. The buttons are NOT at fixed positions: the status block
        // above them varies from a few lines to a full bracket listing, and a fixed
        // button row was being buried under it. Rebuild measures the rendered text
        // and starts the columns below whatever it actually took.
        private const float PanelWidth = 860f;
        private const float PanelHeight = 940f;
        private const float StatusTopY = PanelHeight / 2f - 24f;      // top edge of the text block
        private const float PanelBottomY = -PanelHeight / 2f;
        private const float StatusGap = 22f;                           // breathing room under the text
        // Never push the columns so far down that the left column's own buttons
        // fall off the panel, even if the text somehow overruns.
        private const float MinColumnHeight = 240f;

        private void Awake() => _instance = this;

        public static void Toggle(Player player)
        {
            if (_instance == null) return;
            if (IsOpen) { _instance.Close(); return; }
            _instance.Open(player);
        }

        private void Open(Player player)
        {
            if (InventoryGui.instance == null)
            {
                if (MessageHud.instance != null)
                    MessageHud.instance.ShowMessage(MessageHud.MessageType.TopLeft, "The tournament board isn't ready yet.");
                return;
            }
            if (!EnsureBuilt()) return;

            _root.SetActive(true);
            IsOpen = true;
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
            // Re-confirm admin status with the server each time (it's authoritative;
            // ZNet.LocalPlayerIsAdminOrHost is unreliable on a dedicated-server client),
            // and pull fresh copies of both boards so nothing shown here is stale.
            LeaderboardSync.RequestAdminStatus();
            LeaderboardSync.RequestTournament();
            LeaderboardSync.RequestInvites();
            Rebuild(player);
        }

        private void Close()
        {
            IsOpen = false;
            if (_root != null) _root.SetActive(false);
        }

        private void Update()
        {
            if (!IsOpen) return;
            // The cursor is kept free + the camera/player input blocked by the
            // TournamentPanel*Patch pair while IsOpen (vanilla menu behaviour).
            // Close on Escape; refresh from the synced snapshot so the status/buttons
            // track the tournament as it changes.
            if (Input.GetKeyDown(KeyCode.Escape)) { Close(); return; }

            // Rebuild only when the state actually changes — rebuilding every frame
            // would destroy the buttons before a click can register.
            var player = Player.m_localPlayer;
            if (player == null) return;
            var sig = StateSignature(player);
            if (sig != _lastSig) Rebuild(player);
        }

        // ---- construction -----------------------------------------------------

        private bool EnsureBuilt()
        {
            // The panel's widgets are CLONES of InventoryGui's, but InventoryGui is
            // destroyed and rebuilt on every world load while our root survives
            // (DontDestroyOnLoad). After a relog the clone sources are destroyed
            // objects, so Instantiate would NRE — drop the stale panel and rebuild.
            if (_root != null && (_textSource == null || _buttonSource == null))
            {
                Destroy(_root);
                _root = null;
                _buttons.Clear();
                _status = null;
                _lastSig = null;
            }
            if (_root != null) return true;
            try
            {
                if (InventoryGui.instance == null) return false;
                _textSource = InventoryGui.instance.m_containerWeight;
                _buttonSource = InventoryGui.instance.m_takeAllButton;
                if (_textSource == null || _buttonSource == null) return false;

                _root = new GameObject("LSII_TournamentPanel");
                var canvas = _root.AddComponent<Canvas>();
                canvas.renderMode = RenderMode.ScreenSpaceOverlay;
                canvas.sortingOrder = 5000;
                var scaler = _root.AddComponent<CanvasScaler>();
                scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
                scaler.referenceResolution = new Vector2(1920, 1080);
                _root.AddComponent<GraphicRaycaster>();
                DontDestroyOnLoad(_root);

                // Dark backing panel, centered.
                var panel = NewImage(_root.transform, new Color(0.05f, 0.05f, 0.06f, 0.92f));
                var prt = panel.GetComponent<RectTransform>();
                prt.anchorMin = prt.anchorMax = new Vector2(0.5f, 0.5f);
                prt.pivot = new Vector2(0.5f, 0.5f);
                prt.sizeDelta = new Vector2(PanelWidth, PanelHeight);

                // The status block is TOP-ANCHORED (pivot at its top edge) rather
                // than centred, so its first line always starts at the same place
                // however long it gets. That is what lets Rebuild place the buttons
                // underneath it by measuring the rendered height — a centred block
                // grows in both directions and gives you nothing to measure from.
                _status = CloneText(panel.transform, new Vector2(0f, StatusTopY), new Vector2(PanelWidth - 40f, PanelHeight));
                var srt = _status.GetComponent<RectTransform>();
                srt.pivot = new Vector2(0.5f, 1f);
                srt.anchoredPosition = new Vector2(0f, StatusTopY);
                _status.alignment = TextAlignmentOptions.TopLeft;
                _status.fontSize = 16f;
                _status.richText = true;

                return true;
            }
            catch (System.Exception e)
            {
                Plugin.Log.LogWarning($"[tourney-ui] Could not build the tournament panel: {e.Message}");
                if (_root != null) Destroy(_root);
                _root = null;
                _status = null;
                _buttons.Clear();
                return false;
            }
        }

        private static GameObject NewImage(Transform parent, Color color)
        {
            var go = new GameObject("Panel", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var img = go.AddComponent<Image>();
            img.color = color;
            return go;
        }

        private TMP_Text CloneText(Transform parent, Vector2 anchoredPos, Vector2 size)
        {
            var clone = Instantiate(_textSource.gameObject, parent);
            clone.name = "Text";
            var rt = clone.GetComponent<RectTransform>();
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = anchoredPos;
            rt.sizeDelta = size;
            var t = clone.GetComponent<TMP_Text>();
            t.enableAutoSizing = false;
            clone.SetActive(true);
            return t;
        }

        private GameObject AddButton(string label, Vector2 anchoredPos, System.Action onClick, Vector2? size = null)
        {
            if (_buttonSource == null || _root == null) return null;
            var clone = Instantiate(_buttonSource.gameObject, _root.transform.GetChild(0)); // into the panel
            clone.name = "Btn_" + label;
            var rt = clone.GetComponent<RectTransform>();
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = anchoredPos;
            rt.sizeDelta = size ?? new Vector2(250, 40);

            var txt = clone.GetComponentInChildren<TMP_Text>();
            if (txt != null) { txt.text = label; txt.enableAutoSizing = false; txt.fontSize = 15f; }

            var btn = clone.GetComponent<Button>();
            if (btn != null)
            {
                btn.onClick.RemoveAllListeners();
                btn.onClick.AddListener(() => onClick());
                btn.interactable = true;
            }
            clone.SetActive(true);
            _buttons.Add(clone);
            return clone;
        }

        // ---- state / layout ---------------------------------------------------

        // A compact fingerprint of everything the panel renders, so Update only
        // rebuilds when it changes (not every frame — rebuilding destroys the
        // buttons, and a rebuild mid-click swallows the click).
        private string StateSignature(Player player)
        {
            long me = player != null ? player.GetPlayerID() : 0L;
            var sb = new System.Text.StringBuilder();
            sb.Append(_slotIndex).Append('|').Append(LeaderboardSync.LocalIsAdmin);
            foreach (var t in TournamentService.All)
            {
                sb.Append('#').Append(t.key).Append(t.mode).Append(t.phase)
                  .Append(t.entrants.Count).Append(t.currentRound).Append(t.championLabel)
                  .Append(t.entrants.Any(e => e.ownerId == me));
                // Ready flags change what the player's own button says, so they have
                // to be part of the fingerprint or the panel would go stale mid-round.
                foreach (var m in t.matches)
                    if (m.round == t.currentRound)
                        sb.Append(m.aReady ? '1' : '0').Append(m.bReady ? '1' : '0').Append(m.activated ? 'A' : '-');
            }
            foreach (var i in DuelInviteService.All)
                sb.Append('@').Append(i.id).Append(i.phase).Append(i.hostReady ? '1' : '0').Append(i.oppReady ? '1' : '0');
            return sb.ToString();
        }

        // Which tournament slot the panel is looking at. The player cycles through
        // them with one button rather than the panel trying to show three brackets
        // at once — the read-only board (View Bracket) is where you see them all.
        private static readonly string[] Slots = { TournamentService.CoinSlot, TournamentService.ValcoinSlot, TournamentService.FreeSlot };
        private int _slotIndex;

        private string CurrentSlot => Slots[Mathf.Clamp(_slotIndex, 0, Slots.Length - 1)];

        private static string SlotTitle(string key)
        {
            if (key == TournamentService.CoinSlot) return "Coin Tournament";
            if (key == TournamentService.ValcoinSlot) return "Valcoin Tournament";
            return "Server Tournament (free)";
        }

        private static WagerCurrency SlotCurrency(string key)
        {
            if (key == TournamentService.CoinSlot) return WagerCurrency.Coins;
            if (key == TournamentService.ValcoinSlot) return WagerCurrency.Valcoin;
            return WagerCurrency.None;
        }

        private void Rebuild(Player player)
        {
            if (_root == null || _status == null) return;
            _lastSig = StateSignature(player);
            foreach (var b in _buttons) if (b != null) Destroy(b);
            _buttons.Clear();

            _status.text = StatusText(player);

            // Measure what the text ACTUALLY rendered to and start the buttons below
            // it. ForceMeshUpdate first — preferredHeight is stale until the mesh is
            // rebuilt, which otherwise happens at the end of the frame, i.e. after
            // we have already placed everything.
            _status.ForceMeshUpdate();
            float columnTop = Mathf.Clamp(
                StatusTopY - _status.preferredHeight - StatusGap,
                PanelBottomY + MinColumnHeight,
                StatusTopY - 40f);

            // Server-verified admin status (reliable on a dedicated-server client);
            // the host is always admin.
            bool admin = LeaderboardSync.LocalIsAdmin
                || (ZNet.instance != null && ZNet.instance.LocalPlayerIsAdminOrHost());

            var slot = CurrentSlot;
            var currency = SlotCurrency(slot);
            var s = TournamentService.Get(slot);
            bool active = s != null && s.active;
            bool registration = active && s.phase == "registration";
            bool running = active && s.phase == "running";
            long me = player != null ? player.GetPlayerID() : 0L;
            bool entered = active && s.entrants.Any(e => e.ownerId == me);

            // ---- left column: tournament actions ----
            const float lx = -206f;
            var big = new Vector2(340, 38);
            float y = columnTop;

            AddButton($"< {SlotTitle(slot)} >", new Vector2(lx, y),
                () => { _slotIndex = (_slotIndex + 1) % Slots.Length; Rebuild(player); }, big); y -= 44f;

            if (!active && currency != WagerCurrency.None)
            {
                int fee = Wager.TournamentFee(currency);
                AddButton($"Start ({fee} {Wager.Display(currency)})", new Vector2(lx, y),
                    () => StartWagered(player, currency), big); y -= 44f;
            }
            if (registration)
            {
                if (entered)
                    AddButton("Withdraw (refunds stake)", new Vector2(lx, y),
                        () => LeaderboardSync.SendTournamentWithdraw(slot), big);
                else
                    AddButton("Lock Totem -> Enter", new Vector2(lx, y), () => LockTotem(player, slot), big);
                y -= 44f;
            }
            if (running && entered && HasPendingMatch(s, me))
            {
                AddButton("Ready to Fight", new Vector2(lx, y),
                    () => LeaderboardSync.SendTournamentReady(slot), big); y -= 44f;
            }

            AddButton("View Bracket", new Vector2(lx, y),
                () => { Close(); TournamentBoard.Open(player); }, big); y -= 44f;
            AddButton("Close", new Vector2(lx, y), Close, big);

            // ---- right column: duel invites, then admin controls ----
            //
            // This column's length is data-driven (however many open invites, however
            // many entrants to release or forfeit), so unlike the left column it
            // cannot be bounded at design time. Every optional row asks Fits first
            // and simply stops when the panel runs out — better a short list than
            // buttons hanging off the bottom edge.
            const float rx = 206f;
            var wide = new Vector2(350, 36);
            var slim = new Vector2(350, 30);
            float ry = columnTop;
            bool Fits(float yy) => yy - 20f > PanelBottomY + 12f;

            var mine = DuelInviteService.MineIn(me);
            if (mine == null)
            {
                foreach (var c in new[] { WagerCurrency.Coins, WagerCurrency.Valcoin })
                {
                    var cc = c;
                    AddButton($"Post Duel Invite ({Wager.DuelStake(cc)} {Wager.Display(cc)})",
                        new Vector2(rx, ry), () => PostInvite(player, cc), wide); ry -= 40f;
                }
            }
            else
            {
                if (mine.phase == "matched")
                { AddButton("Ready to Fight (duel)", new Vector2(rx, ry), () => InviteAction("ready", mine.id), wide); ry -= 40f; }
                if (mine.phase != "running")
                { AddButton("Withdraw Invite", new Vector2(rx, ry), () => InviteAction("withdraw", mine.id), wide); ry -= 40f; }
            }

            // Open invites posted by other players, newest first. Capped so a busy
            // board can't push the admin controls off the panel.
            foreach (var inv in DuelInviteService.All
                         .Where(i => i.phase == "open" && i.hostId != me)
                         .OrderByDescending(i => i.createdTicks)
                         .Take(mine == null ? 4 : 2))
            {
                if (!Fits(ry)) break;
                var id = inv.id;
                var cur = Wager.Parse(inv.currency);
                AddButton($"Accept {Trim(inv.hostName)} - {inv.stake} {Wager.Display(cur)}",
                    new Vector2(rx, ry), () => AcceptInvite(player, id), slim); ry -= 34f;
            }

            if (!admin) return;

            ry -= 10f;
            if (!active)
            {
                // The free tournament is the only one an admin "starts" as an admin;
                // the staked ones are opened by paying, like anyone else.
                if (currency == WagerCurrency.None)
                {
                    string pendingType = EliminationTypes[_pendingTypeIndex];
                    AddButton($"Type: {DisplayType(pendingType)}", new Vector2(rx, ry),
                        () => { _pendingTypeIndex = (_pendingTypeIndex + 1) % EliminationTypes.Length; Rebuild(player); }, slim); ry -= 34f;
                    AddButton("Admin: Start 1v1", new Vector2(rx, ry), () => Admin($"start|1v1|0|{pendingType}"), slim); ry -= 34f;
                    AddButton("Admin: Start Party", new Vector2(rx, ry), () => Admin($"start|party|0|{pendingType}"), slim); ry -= 34f;
                }
            }
            else if (registration)
            {
                // A wagered bracket begins by itself when it fills; the manual Begin
                // is only meaningful for the free tournament.
                if (currency == WagerCurrency.None)
                { AddButton("Admin: Begin Bracket", new Vector2(rx, ry), () => Admin("begin|" + slot), slim); ry -= 34f; }
                AddButton("Admin: Cancel (refund all)", new Vector2(rx, ry), () => Admin("cancel|" + slot), slim); ry -= 34f;
                foreach (var e in s.entrants.Take(4))
                {
                    if (!Fits(ry)) break;
                    var name = e.label;
                    AddButton($"Release: {Trim(name)}", new Vector2(rx, ry), () => Admin($"release|{name}|{slot}"), slim); ry -= 32f;
                }
            }
            else if (running)
            {
                if (currency == WagerCurrency.None)
                { AddButton("Admin: Activate Round", new Vector2(rx, ry), () => Admin("activate|" + slot), slim); ry -= 34f; }
                AddButton("Admin: Cancel", new Vector2(rx, ry), () => Admin("cancel|" + slot), slim); ry -= 34f;
                foreach (var e in s.entrants.Where(e => !e.eliminated).Take(3))
                {
                    if (!Fits(ry)) break;
                    var name = e.ownerName;
                    var label = e.label;
                    AddButton($"Forfeit: {Trim(label)}", new Vector2(rx, ry), () => Admin($"forfeit|{name}|{slot}"), slim); ry -= 32f;
                }
            }
            else // complete
            {
                AddButton("Admin: Clear (return totems)", new Vector2(rx, ry), () => Admin("cancel|" + slot), slim);
            }
        }

        // True when this player still has an undecided, unstarted match this round —
        // i.e. "Ready to Fight" would mean something.
        private static bool HasPendingMatch(TournamentState s, long ownerId)
        {
            var ent = s.entrants.FirstOrDefault(e => e.ownerId == ownerId);
            if (ent == null) return false;
            return s.matches.Any(m => m.round == s.currentRound && string.IsNullOrEmpty(m.winnerId)
                                      && !m.activated && !string.IsNullOrEmpty(m.bId)
                                      && (m.aId == ent.entrantId || m.bId == ent.entrantId));
        }

        private static void Admin(string cmd) => LeaderboardSync.SendAdminCommand(cmd);

        private static string Trim(string s)
        {
            if (string.IsNullOrEmpty(s)) return "?";
            return s.Length <= 14 ? s : s.Substring(0, 13) + "...";
        }

        // ---- status text ------------------------------------------------------

        private string StatusText(Player player)
        {
            var slot = CurrentSlot;
            var currency = SlotCurrency(slot);
            var s = TournamentService.Get(slot);
            long me = player != null ? player.GetPlayerID() : 0L;

            var sb = new System.Text.StringBuilder();
            sb.Append("<size=125%><color=#FFD24A>Dvergr Tournaments & Duels</color></size>\n");
            sb.Append($"<color=#AAAAAA>Viewing:</color> <color=#FFFFFF>{SlotTitle(slot)}</color>\n\n");

            if (s == null || !s.active)
            {
                if (currency == WagerCurrency.None)
                {
                    sb.Append("<color=#CCCCCC>No free tournament is running.</color> <color=#AAAAAA>An admin can start one from the controls at right.</color>\n\n");
                }
                else
                {
                    int fee = Wager.TournamentFee(currency);
                    int prize = Wager.TournamentPrize(currency);
                    int size = Mathf.Max(2, Plugin.WageredBracketSize.Value);
                    sb.Append($"<color=#CCCCCC>No {Wager.Display(currency)} tournament is running — <color=#FFFFFF>any player can open one.</color></color>\n");
                    sb.Append($"<color=#AAAAAA>Cost to open: <color=#B8F5B0>{fee} {Wager.Display(currency)}</color> — this also pays your own entry, you are never charged twice. ");
                    sb.Append($"Fixed <color=#FFFFFF>{size}-player</color> single-elimination bracket; every other entrant pays <color=#B8F5B0>{fee}</color>. ");
                    sb.Append($"The champion wins <color=#B8F5B0>{prize} {Wager.Display(currency)}</color>. ");
                    sb.Append("One Coin tournament and one Valcoin tournament may run at a time.</color>\n\n");
                    var hint = WagerService.ClientHint(currency);
                    if (!string.IsNullOrEmpty(hint))
                        sb.Append($"<color=#E06666>{hint}</color>\n\n");
                }
                sb.Append(HowItWorks());
                sb.Append('\n').Append(InviteSummary(me));
                return sb.ToString();
            }

            string mode = s.mode == "party" ? "Party" : "1v1";
            string type = DisplayType(s.eliminationType);
            sb.Append($"Mode: <color=#FFFFFF>{mode}</color>   Type: <color=#FFFFFF>{type}</color>   Phase: <color=#FFFFFF>{s.phase}</color>\n");
            sb.Append($"Entrants: <color=#FFFFFF>{s.entrants.Count}{(s.size > 0 ? "/" + s.size : "")}</color>");
            if (s.entryFee > 0)
                sb.Append($"   Entry: <color=#B8F5B0>{s.entryFee} {Wager.Display(currency)}</color>   Purse: <color=#B8F5B0>{s.prize}</color>");
            sb.Append('\n');
            if (s.entryFee > 0 && !string.IsNullOrEmpty(s.hostName))
                sb.Append($"<color=#AAAAAA>Opened by {s.hostName}</color>\n");
            sb.Append('\n');

            var mineEntry = s.entrants.FirstOrDefault(e => e.ownerId == me);
            if (mineEntry != null) sb.Append($"<color=#B8F5B0>You are entered as '{mineEntry.label}'.</color>\n");
            else if (s.phase == "registration")
                sb.Append("<color=#AAAAAA>Lock a companion's Communion Totem to enter. (Seal one at an Incinerator, or in the field with a Dead Raiser.)</color>\n");

            if (s.phase == "registration")
            {
                foreach (var e in s.entrants.Take(8))
                {
                    string lvl = e.level > 0 ? $" — <color=#FFD24A>Lv{e.level}</color>" : "";
                    sb.Append($"  - {e.label} <color=#AAAAAA>({e.ownerName})</color>{lvl}\n");
                }
                if (s.entryFee > 0)
                    sb.Append($"<color=#AAAAAA>The bracket begins automatically at {s.size}/{s.size}. If it never fills it is cancelled and every stake and totem is returned.</color>\n");
            }
            else if (s.phase == "running")
            {
                sb.Append($"<color=#FFFFFF>Round {s.currentRound}</color>\n");
                foreach (var m in s.matches.Where(m => m.round == s.currentRound))
                {
                    if (string.IsNullOrEmpty(m.bId)) { sb.Append($"  - {m.aLabel} — bye\n"); continue; }
                    string state = !string.IsNullOrEmpty(m.winnerId)
                        ? $"<color=#B8F5B0>{(m.winnerId == m.aId ? m.aLabel : m.bLabel)} won</color>"
                        : m.activated ? "<color=#E06666>fighting</color>"
                        : $"<color=#AAAAAA>ready: {(m.aReady ? "yes" : "no")} / {(m.bReady ? "yes" : "no")}</color>";
                    sb.Append($"  - {m.aLabel} vs {m.bLabel} — {state}\n");
                }
                sb.Append("<color=#AAAAAA>You and your opponent choose the venue: meet anywhere, then BOTH press Ready to Fight. Your companions are summoned there at full health and fight automatically.</color>\n");
            }
            else if (s.phase == "complete")
            {
                sb.Append($"<color=#FFD24A>Champion: {s.championLabel}</color>\n");
            }

            sb.Append('\n').Append(InviteSummary(me));
            return sb.ToString();
        }

        // The block of rules every player needs once, spelled out on the panel
        // itself rather than left to the wiki — this is the only place a player is
        // guaranteed to read before spending anything.
        private static string HowItWorks()
        {
            var sb = new System.Text.StringBuilder();
            sb.Append("<color=#FFD24A>How a tournament runs</color>\n<color=#CCCCCC>");
            sb.Append("1. Anyone opens one by paying the stake.\n");
            sb.Append("2. Entrants lock a companion's Communion Totem in; it is held safely until the event ends.\n");
            sb.Append("3. The bracket starts by itself once it is full. Pairings are seeded by ladder rating.\n");
            sb.Append("4. <color=#FFFFFF>You pick where each duel happens.</color> Meet your opponent anywhere you both like, then BOTH press Ready to Fight. Your two companions are summoned beside you at full health, locked onto each other, and fight until one is subdued — no other companion can join in.\n");
            sb.Append("5. The winner advances; both companions are resealed into their totems with any XP they gained.\n");
            sb.Append("6. Lose, and your totem comes back to you when the event ends.</color>\n");
            return sb.ToString();
        }

        private static string InviteSummary(long me)
        {
            var sb = new System.Text.StringBuilder();
            sb.Append("<color=#FFD24A>Duel invites</color> ");
            sb.Append("<color=#AAAAAA>— stake on a single duel instead. Post one, anyone may accept by matching the stake, and the winner takes both. ");
            sb.Append("You may only be in one invite at a time. The two of you choose where to fight, exactly as above.</color>\n");

            var mine = DuelInviteService.MineIn(me);
            if (mine != null)
            {
                var cur = Wager.Display(Wager.Parse(mine.currency));
                string state = mine.phase == "open" ? "waiting for a challenger"
                    : mine.phase == "matched" ? $"{mine.oppName} accepted — ready: {(mine.hostReady ? "yes" : "no")} / {(mine.oppReady ? "yes" : "no")}"
                    : mine.phase == "running" ? "the duel is under way"
                    : "settled";
                sb.Append($"<color=#B8F5B0>Your invite: {mine.stake} {cur}, winner takes {mine.stake * 2} — {state}.</color>\n");
            }
            int open = DuelInviteService.All.Count(i => i.phase == "open" && i.hostId != me);
            if (open > 0) sb.Append($"<color=#8FE3FF>{open} open invite(s) you can accept — see the buttons at right.</color>\n");
            return sb.ToString();
        }

        // ---- actions ----------------------------------------------------------

        // Vanilla Coins are inventory items, so a Coin stake is taken HERE, on the
        // owning client, before the request goes to the server; the server records
        // the amount and refunds it if it refuses. A Valcoin stake is a server-side
        // ledger debit and nothing is taken here. See Economy/WagerService.
        private static bool TakeStake(Player player, WagerCurrency currency, int amount, out int paid)
        {
            paid = 0;
            if (currency != WagerCurrency.Coins || amount <= 0) return true;
            if (!Wager.TryTakeCoins(player, amount))
            {
                Msg($"You need {amount} Coins for that ({Wager.CoinsHeld(player)} held).");
                return false;
            }
            paid = amount;
            return true;
        }

        private void StartWagered(Player player, WagerCurrency currency)
        {
            if (player == null) return;
            if (!Plugin.WagersEnabled.Value) { Msg("Wagered events are disabled on this server."); return; }
            int fee = Wager.TournamentFee(currency);
            if (!TakeStake(player, currency, fee, out var paid)) return;
            LeaderboardSync.SendStartWagered(Wager.Key(currency), paid);
            Msg($"Opening a {Wager.Display(currency)} tournament...");
        }

        // The player's first companion totem, with everything the wagered systems
        // need read off it. Returns false (with a message) if they have none usable.
        private static bool PickTotem(Player player, out ItemDrop.ItemData totem,
            out string id, out string label, out int caste, out int level)
        {
            totem = null; id = null; label = null; caste = 0; level = 0;
            var inv = player != null ? player.GetInventory() : null;
            if (inv == null) return false;
            totem = inv.GetAllItems().FirstOrDefault(TotemConversionService.IsCompanionTotem);
            if (totem == null)
            {
                Msg("You have no companion totem. Seal a companion first — at an Incinerator, or in the field with a Dead Raiser.");
                return false;
            }
            id = TotemConversionService.CompanionIdOf(totem);
            if (string.IsNullOrEmpty(id)) { Msg("That totem has no ladder identity and can't be staked."); return false; }
            label = TotemConversionService.LabelOf(totem);
            caste = (int)TotemConversionService.CasteOf(totem);
            level = TotemConversionService.LevelOf(totem);
            return true;
        }

        private void LockTotem(Player player, string slot)
        {
            var s = TournamentService.Get(slot);
            if (s == null || !s.active || s.phase != "registration") { Msg("Registration isn't open."); return; }
            if (player == null) return;

            var inv = player.GetInventory();
            long ownerId = player.GetPlayerID();
            string ownerName = player.GetPlayerName();
            var currency = Wager.Parse(s.currency);

            // The host's opening fee already covers their own entry, so they are not
            // charged again here (the server applies the same rule).
            bool hostFree = ownerId == s.hostId && s.hostCredit > 0;
            int fee = hostFree ? 0 : s.entryFee;

            if (s.mode == "party")
            {
                var totems = inv.GetAllItems().Where(TotemConversionService.IsCompanionTotem).ToList();
                if (totems.Count == 0)
                { Msg("You have no companion totem to enter. Seal a companion first."); return; }
                int cap = Mathf.Max(1, Plugin.MaxPartySize.Value);
                var chosen = totems.Take(cap).ToList();
                if (!TakeStake(player, currency, fee, out var paidParty)) return;
                var payloads = chosen.Select(TotemConversionService.SerializePayload).ToList();
                var partyName = LeaderboardStore.FindParty(ownerId)?.partyName;
                string plabel = string.IsNullOrEmpty(partyName) ? ownerName : partyName;
                int pseed = LeaderboardStore.FindParty(ownerId)?.rating ?? Rating.StartRating;
                foreach (var t in chosen) inv.RemoveItem(t);
                LeaderboardSync.SendTournamentJoinEscrow(slot, ownerId.ToString(), ownerId, ownerName, plabel, -1, pseed,
                    payloads, 0, paidParty);
                Msg($"Locked {chosen.Count} totem(s) — entering your party...");
                return;
            }

            if (!PickTotem(player, out var totem, out var id, out var label, out var caste, out var level)) return;
            if (!TakeStake(player, currency, fee, out var paid)) return;
            int seed = LeaderboardStore.Find(id)?.rating ?? Rating.StartRating;
            var payload = TotemConversionService.SerializePayload(totem);
            inv.RemoveItem(totem);
            LeaderboardSync.SendTournamentJoinEscrow(slot, id, ownerId, ownerName, label, caste, seed,
                new List<string> { payload }, level, paid);
            Msg($"Locked '{label}' into a slot — entering...");
        }

        private void PostInvite(Player player, WagerCurrency currency)
        {
            if (player == null) return;
            if (!Plugin.WagersEnabled.Value) { Msg("Wagered events are disabled on this server."); return; }
            if (DuelInviteService.MineIn(player.GetPlayerID()) != null)
            { Msg("You already have a duel invite — withdraw it first."); return; }
            if (!PickTotem(player, out var totem, out var id, out var label, out var caste, out var level)) return;
            int stake = Wager.DuelStake(currency);
            if (!TakeStake(player, currency, stake, out var paid)) return;

            var payload = TotemConversionService.SerializePayload(totem);
            player.GetInventory().RemoveItem(totem);
            LeaderboardSync.SendInviteAction("post", null, Wager.Key(currency), id, label, level, caste, payload, paid);
            Msg($"Posting a duel invite with '{label}'...");
        }

        private void AcceptInvite(Player player, string inviteId)
        {
            var invite = DuelInviteService.All.FirstOrDefault(i => i.id == inviteId);
            if (invite == null || invite.phase != "open") { Msg("That invite is no longer open."); return; }
            if (!PickTotem(player, out var totem, out var id, out var label, out var caste, out var level)) return;

            var currency = Wager.Parse(invite.currency);
            if (!TakeStake(player, currency, invite.stake, out var paid)) return;

            var payload = TotemConversionService.SerializePayload(totem);
            player.GetInventory().RemoveItem(totem);
            LeaderboardSync.SendInviteAction("accept", inviteId, invite.currency, id, label, level, caste, payload, paid);
            Msg($"Accepting {invite.hostName}'s challenge with '{label}'...");
        }

        private static void InviteAction(string action, string inviteId)
            => LeaderboardSync.SendInviteAction(action, inviteId, null, null, null, 0, 0, null, 0);

        // Panel feedback goes TOP-LEFT, never Center. A centred MessageHud line is
        // drawn behind our canvas (sortingOrder 5000), so every confirmation and
        // refusal a player triggered from the panel was landing invisibly underneath
        // it. The top-left queue sits outside the panel and is the game's own idiom
        // for "here is what just happened to you" anyway.
        private static void Msg(string m)
        {
            if (MessageHud.instance != null)
                MessageHud.instance.ShowMessage(MessageHud.MessageType.TopLeft, m);
        }
    }
}
