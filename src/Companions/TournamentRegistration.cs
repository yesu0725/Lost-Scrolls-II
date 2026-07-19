using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using LostScrollsII.Ranking;

namespace LostScrollsII.Companions
{
    // Interactive tournament panel (docs/Tournaments.md). This is the "application"
    // surface: a player enters by locking a companion's Communion Totem into a slot
    // (the totem leaves the inventory and is escrowed), and admins get the run
    // controls (start / begin / activate the round / release a totem / cancel).
    //
    // Built on a self-contained uGUI Canvas with widgets CLONED from vanilla UI
    // (the InventoryGui weight label for text; its "Take All" button for buttons) —
    // no authored assets, matching the vanilla-assets-only constraint and the clone
    // approach in CompanionInventoryGui. The cursor is unlocked while open and our
    // hotkeys are suppressed (IsOpen); Escape closes it.
    //
    // Registration is escrow-based: LockTotem serializes the totem(s), removes them
    // from the player inventory, and sends them to the server (LeaderboardSync).
    // If the server rejects the entry it returns the totem(s) (RpcTourReturn).
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
                    MessageHud.instance.ShowMessage(MessageHud.MessageType.Center, "The tournament board isn't ready yet.");
                return;
            }
            if (!EnsureBuilt()) return;

            _root.SetActive(true);
            IsOpen = true;
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
            // Re-confirm admin status with the server each time (it's authoritative;
            // ZNet.LocalPlayerIsAdminOrHost is unreliable on a dedicated-server client).
            LeaderboardSync.RequestAdminStatus();
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

            // Rebuild only when the tournament state actually changes — rebuilding
            // every frame would destroy the buttons before a click can register.
            var player = Player.m_localPlayer;
            if (player == null) return;
            var sig = StateSignature(player);
            if (sig != _lastSig) Rebuild(player);
        }

        // ---- construction -----------------------------------------------------

        private bool EnsureBuilt()
        {
            if (_root != null) return true;
            try
            {
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
                prt.sizeDelta = new Vector2(640, 720);

                _status = CloneText(panel.transform, new Vector2(0f, 210f), new Vector2(600, 260));
                _status.alignment = TextAlignmentOptions.TopLeft;
                _status.fontSize = 20f;
                _status.richText = true;

                return true;
            }
            catch (System.Exception e)
            {
                Plugin.Log.LogWarning($"[tourney-ui] Could not build the tournament panel: {e.Message}");
                _root = null;
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
            var clone = Instantiate(_buttonSource.gameObject, _root.transform.GetChild(0)); // into the panel
            clone.name = "Btn_" + label;
            var rt = clone.GetComponent<RectTransform>();
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = anchoredPos;
            rt.sizeDelta = size ?? new Vector2(250, 40);

            var txt = clone.GetComponentInChildren<TMP_Text>();
            if (txt != null) { txt.text = label; txt.enableAutoSizing = false; txt.fontSize = 16f; }

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
        // rebuilds when it changes (not every frame).
        private static string StateSignature(Player player)
        {
            var s = TournamentService.Snapshot;
            if (s == null || !s.active) return "none|" + LeaderboardSync.LocalIsAdmin;
            long me = player != null ? player.GetPlayerID() : 0L;
            bool entered = s.entrants.Any(e => e.ownerId == me);
            // Include admin so the panel rebuilds the moment the server's admin-check
            // reply arrives (it lands a frame or two after the panel opens).
            return $"{s.mode}|{s.phase}|{s.entrants.Count}|{s.currentRound}|{s.championLabel}|{entered}|{LeaderboardSync.LocalIsAdmin}";
        }

        private void Rebuild(Player player)
        {
            if (_root == null) return;
            _lastSig = StateSignature(player);
            foreach (var b in _buttons) Destroy(b);
            _buttons.Clear();

            _status.text = StatusText(player);

            // Server-verified admin status (reliable on a dedicated-server client);
            // the host is always admin.
            bool admin = LeaderboardSync.LocalIsAdmin
                || (ZNet.instance != null && ZNet.instance.LocalPlayerIsAdminOrHost());
            var s = TournamentService.Snapshot;
            bool registration = s != null && s.active && s.phase == "registration";
            bool running = s != null && s.active && s.phase == "running";

            // ---- left column: player actions ----
            const float lx = -155f;
            var big = new Vector2(260, 40);
            float y = -30f;
            if (registration)
            {
                AddButton("Lock Totem → Enter", new Vector2(lx, y), () => LockTotem(player), big); y -= 46f;
                AddButton("Withdraw", new Vector2(lx, y), () => LeaderboardSync.SendTournamentWithdraw(), big); y -= 46f;
            }
            AddButton("View Bracket", new Vector2(lx, y), () => { Close(); TournamentBoard.Open(player); }, big); y -= 46f;
            AddButton("Close", new Vector2(lx, y), Close, big);

            if (!admin) return;

            // ---- right column: admin controls (visible to admins/host only) ----
            // A button for every tournament setup command; the set shown depends on
            // the current phase. The server re-verifies admin on the RPC.
            const float rx = 150f;
            var wide = new Vector2(280, 38);
            var slim = new Vector2(280, 32);
            float ry = -30f;

            if (s == null || !s.active)
            {
                AddButton("Start 1v1", new Vector2(rx, ry), () => Admin("start|1v1|0"), wide); ry -= 42f;
                AddButton("Start Party", new Vector2(rx, ry), () => Admin("start|party|0"), wide); ry -= 42f;
            }
            else if (registration)
            {
                AddButton("Begin Bracket", new Vector2(rx, ry), () => Admin("begin"), wide); ry -= 42f;
                AddButton("Cancel Tournament", new Vector2(rx, ry), () => Admin("cancel"), wide); ry -= 42f;
                // Per-entrant release (return the escrowed totem, drop the entry).
                foreach (var e in s.entrants.Take(8))
                {
                    var name = e.label;
                    AddButton($"Release: {Trim(name)}", new Vector2(rx, ry), () => Admin("release|" + name), slim); ry -= 36f;
                }
            }
            else if (running)
            {
                AddButton("Activate Round", new Vector2(rx, ry), () => Admin("activate"), wide); ry -= 42f;
                AddButton("Cancel Tournament", new Vector2(rx, ry), () => Admin("cancel"), wide); ry -= 42f;
                // Per-undecided-entrant forfeit + release this round.
                foreach (var e in s.entrants.Where(e => !e.eliminated).Take(8))
                {
                    var name = e.ownerName;
                    var label = e.label;
                    AddButton($"Forfeit: {Trim(label)}", new Vector2(rx, ry), () => Admin("forfeit|" + name), slim); ry -= 34f;
                    AddButton($"Release: {Trim(label)}", new Vector2(rx, ry), () => Admin("release|" + label), slim); ry -= 34f;
                }
            }
            else // complete
            {
                AddButton("Clear (return totems)", new Vector2(rx, ry), () => Admin("cancel"), wide); ry -= 42f;
            }
        }

        private static void Admin(string cmd) => LeaderboardSync.SendAdminCommand(cmd);

        private static string Trim(string s)
        {
            if (string.IsNullOrEmpty(s)) return "?";
            return s.Length <= 16 ? s : s.Substring(0, 15) + "…";
        }

        private static string StatusText(Player player)
        {
            var s = TournamentService.Snapshot;
            var sb = new System.Text.StringBuilder();
            sb.Append("<size=130%><color=#FFD24A>Dvergr Tournament</color></size>\n\n");

            if (s == null || !s.active)
            {
                sb.Append("<color=#CCCCCC>No tournament is running.</color>\n");
                sb.Append("<color=#AAAAAA>An admin can start one from the controls at right.</color>");
                return sb.ToString();
            }

            string mode = s.mode == "party" ? "Party" : "1v1";
            sb.Append($"Mode: <color=#FFFFFF>{mode}</color>   Phase: <color=#FFFFFF>{s.phase}</color>\n");
            sb.Append($"Entrants: <color=#FFFFFF>{s.entrants.Count}{(s.size > 0 ? "/" + s.size : "")}</color>\n\n");

            long me = player != null ? player.GetPlayerID() : 0L;
            var mine = s.entrants.FirstOrDefault(e => e.ownerId == me);
            if (mine != null)
                sb.Append($"<color=#B8F5B0>You are entered as '{mine.label}'.</color>\n\n");
            else if (s.phase == "registration")
                sb.Append("<color=#AAAAAA>Lock a companion's Communion Totem to enter. (Seal one at the Incinerator first.)</color>\n\n");

            if (s.phase == "registration")
            {
                foreach (var e in s.entrants.Take(10))
                    sb.Append($"  • {e.label} <color=#AAAAAA>({e.ownerName})</color>\n");
            }
            else if (s.phase == "running")
            {
                sb.Append($"<color=#FFFFFF>Round {s.currentRound}</color> — use View Bracket for pairings.");
            }
            else if (s.phase == "complete")
            {
                sb.Append($"<color=#FFD24A>🏆 Champion: {s.championLabel}</color>");
            }
            return sb.ToString();
        }

        // ---- registration action ---------------------------------------------

        private void LockTotem(Player player)
        {
            var s = TournamentService.Snapshot;
            if (s == null || !s.active || s.phase != "registration")
            { Msg("Registration isn't open."); return; }
            if (player == null) return;

            var inv = player.GetInventory();
            var totems = inv.GetAllItems().Where(TotemConversionService.IsCompanionTotem).ToList();
            if (totems.Count == 0)
            { Msg("You have no companion totem to enter. Seal a companion at the Incinerator first."); return; }

            long ownerId = player.GetPlayerID();
            string ownerName = player.GetPlayerName();

            if (s.mode == "party")
            {
                int cap = Mathf.Max(1, Plugin.MaxPartySize.Value);
                var chosen = totems.Take(cap).ToList();
                var payloads = chosen.Select(TotemConversionService.SerializePayload).ToList();
                var partyName = LeaderboardStore.FindParty(ownerId)?.partyName;
                string label = string.IsNullOrEmpty(partyName) ? ownerName : partyName;
                int seed = LeaderboardStore.FindParty(ownerId)?.rating ?? Rating.StartRating;
                foreach (var t in chosen) inv.RemoveItem(t);
                LeaderboardSync.SendTournamentJoinEscrow(ownerId.ToString(), ownerId, ownerName, label, -1, seed, payloads);
                Msg($"Locked {chosen.Count} totem(s) — entering your party…");
            }
            else
            {
                var t = totems[0];
                string id = TotemConversionService.CompanionIdOf(t);
                if (string.IsNullOrEmpty(id)) { Msg("That totem has no ladder identity and can't be entered."); return; }
                int caste = (int)TotemConversionService.CasteOf(t);
                string label = TotemConversionService.LabelOf(t);
                int seed = LeaderboardStore.Find(id)?.rating ?? Rating.StartRating;
                var payload = TotemConversionService.SerializePayload(t);
                inv.RemoveItem(t);
                LeaderboardSync.SendTournamentJoinEscrow(id, ownerId, ownerName, label, caste, seed,
                    new List<string> { payload });
                Msg($"Locked '{label}' into a slot — entering…");
            }
        }

        private static void Msg(string m)
        {
            if (MessageHud.instance != null)
                MessageHud.instance.ShowMessage(MessageHud.MessageType.Center, m);
        }
    }
}
