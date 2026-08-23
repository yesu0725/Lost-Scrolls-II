using System.Collections.Generic;
using System.Linq;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace LostScrollsII.Bounty
{
    // The Wanted Board UI (docs/Bounty-Hunting.md, Phase F, requirement 7).
    //
    // Same construction approach as the tournament panel: a self-contained uGUI
    // Canvas whose widgets are CLONED from vanilla InventoryGui parts (its weight
    // label for text, its "Take All" button for buttons), so no authored assets are
    // introduced and the vanilla-assets-only constraint holds. Input capture while
    // open is handled by the shared ModalPanels gate.
    //
    // The panel is ALWAYS reachable — a player on a server without bounty hunting
    // gets an explanation instead of a dead key or a missing button. That's the
    // "introduce it to players who aren't on the server" half of the brief; it costs
    // nothing because the data behind it is what's gated, not the UI.
    public class BountyBoardPanel : MonoBehaviour
    {
        private static BountyBoardPanel _instance;
        public static bool IsOpen { get; private set; }

        private GameObject _root;
        private TMP_Text _status;
        private readonly List<GameObject> _buttons = new List<GameObject>();
        private TMP_Text _textSource;
        private Button _buttonSource;
        private string _lastSig;

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
                Msg("The bounty board isn't ready yet.");
                return;
            }
            if (!EnsureBuilt()) return;

            _root.SetActive(true);
            IsOpen = true;
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
            // Pull a fresh board rather than trusting whatever was last pushed, so
            // "Accept" can't act on a posting someone already took.
            BountySync.RequestBoard();
            _lastSig = null;
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
            if (Input.GetKeyDown(KeyCode.Escape)) { Close(); return; }

            var player = Player.m_localPlayer;
            if (player == null) return;
            // Rebuild only on change — rebuilding every frame would destroy the
            // buttons before a click could register (learned on the tournament panel).
            var sig = StateSignature(player);
            if (sig != _lastSig) Rebuild(player);
        }

        // ---- construction -----------------------------------------------------

        private bool EnsureBuilt()
        {
            // InventoryGui is rebuilt on every world load while our root survives
            // (DontDestroyOnLoad), so after a relog the clone sources are destroyed
            // objects and Instantiate would NRE. Drop the stale panel and rebuild.
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

                _root = new GameObject("LSII_BountyPanel");
                var canvas = _root.AddComponent<Canvas>();
                canvas.renderMode = RenderMode.ScreenSpaceOverlay;
                canvas.sortingOrder = 5000;
                var scaler = _root.AddComponent<CanvasScaler>();
                scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
                scaler.referenceResolution = new Vector2(1920, 1080);
                _root.AddComponent<GraphicRaycaster>();
                DontDestroyOnLoad(_root);

                var panel = NewImage(_root.transform, new Color(0.05f, 0.05f, 0.06f, 0.92f));
                var prt = panel.GetComponent<RectTransform>();
                prt.anchorMin = prt.anchorMax = new Vector2(0.5f, 0.5f);
                prt.pivot = new Vector2(0.5f, 0.5f);
                prt.sizeDelta = new Vector2(700, 760);

                _status = CloneText(panel.transform, new Vector2(0f, 200f), new Vector2(660, 330));
                _status.alignment = TextAlignmentOptions.TopLeft;
                _status.fontSize = 19f;
                _status.richText = true;

                return true;
            }
            catch (System.Exception e)
            {
                Plugin.Log.LogWarning($"[bounty-ui] Could not build the bounty panel: {e.Message}");
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
            var clone = Instantiate(_buttonSource.gameObject, _root.transform.GetChild(0));
            clone.name = "Btn_" + label;
            var rt = clone.GetComponent<RectTransform>();
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = anchoredPos;
            rt.sizeDelta = size ?? new Vector2(300, 40);

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

        private static string StateSignature(Player player)
        {
            if (!BountyFeatureGate.AvailableToLocalPlayer) return "off";
            var sb = new StringBuilder("on");
            sb.Append(BountyQuestGate.IsUnlocked(player) ? "|open" : "|locked");
            var mine = BountyBoardStore.AcceptedBy(player.GetPlayerID());
            sb.Append('|').Append(mine != null ? mine.id + (mine.spawned ? "+s" : "") : "none");
            foreach (var p in BountyBoardStore.OpenPostings()) sb.Append('|').Append(p.id);
            return sb.ToString();
        }

        private void Rebuild(Player player)
        {
            _lastSig = StateSignature(player);
            foreach (var b in _buttons) if (b != null) Destroy(b);
            _buttons.Clear();
            if (_status == null) return;

            // Not available here — show the teaser instead of an empty board.
            if (!BountyFeatureGate.AvailableToLocalPlayer)
            {
                _status.text = TeaserText();
                AddButton("Close", new Vector2(0f, -300f), Close, new Vector2(300, 44));
                return;
            }

            _status.text = BoardText(player);

            float y = -30f;
            var mine = BountyBoardStore.AcceptedBy(player.GetPlayerID());
            if (mine != null)
            {
                // The warden's commission can't be abandoned — it's the way in, and
                // dropping it would leave the player with no route back to the board.
                if (!mine.tutorial)
                {
                    AddButton("Abandon Bounty", new Vector2(0f, y), () => BountySync.SendAbandon(),
                        new Vector2(320, 42));
                    y -= 48f;
                }
            }
            else if (!BountyQuestGate.IsUnlocked(player))
            {
                // Locked: the board lists nothing until the commission is answered.
                // No button here — the way forward is a conversation, not a click.
            }
            else
            {
                bool elite = BountyService.IsEliteEligible(player.GetPlayerID());
                foreach (var p in BountyBoardStore.OpenPostings())
                {
                    var id = p.id; // capture per posting, not the loop variable
                    float dist = Distance(player, p);

                    // An elite posting the player can't take is still SHOWN, as a
                    // disabled row. Hiding it would hide the reason to go and rank up;
                    // seeing it locked is the whole point of the gate.
                    if (BountyBoardStore.IsElite(p) && !elite)
                    {
                        var locked = AddButton($"Locked: {p.Label} — {dist:F0}m",
                            new Vector2(0f, y), () => { }, new Vector2(560, 40));
                        var lockedBtn = locked != null ? locked.GetComponent<Button>() : null;
                        if (lockedBtn != null) lockedBtn.interactable = false;
                    }
                    else
                    {
                        AddButton($"Accept: {p.Label} — {dist:F0}m",
                            new Vector2(0f, y), () => BountySync.SendAccept(id), new Vector2(560, 40));
                    }

                    y -= 46f;
                    if (y < -250f) break; // keep the panel on one screen
                }
            }

            AddButton("Close", new Vector2(0f, -320f), Close, new Vector2(300, 44));
        }

        private static float Distance(Player player, BountyPosting p)
        {
            var a = new Vector3(player.transform.position.x, 0f, player.transform.position.z);
            return Vector3.Distance(a, new Vector3(p.x, 0f, p.z));
        }

        private static string BoardText(Player player)
        {
            var sb = new StringBuilder();
            sb.Append("<size=150%><color=#FF6B4A>The Wanted Board</color></size>\n\n");

            var mine = BountyBoardStore.AcceptedBy(player.GetPlayerID());
            if (mine != null)
            {
                float dist = Distance(player, mine);
                sb.Append(mine.tutorial
                    ? "<color=#FFD24A>The warden's commission:</color> "
                    : "<color=#FFD24A>Your posting:</color> ");
                sb.Append($"{mine.Label}\n");
                sb.Append($"  <color=#AAAAAA>{dist:F0}m away — marked on your map.</color>\n");
                sb.Append(mine.spawned
                    ? "  <color=#FF6B4A>It knows you are coming.</color>\n\n"
                    : "  <color=#AAAAAA>Travel there to find it.</color>\n\n");
            }
            else if (!BountyQuestGate.IsUnlocked(player))
            {
                // Locked. Point at the conversation rather than showing an empty list,
                // so the board itself teaches the way in.
                sb.Append("<color=#AAAAAA>The board is not yours to read yet.</color>\n\n");
                sb.Append("Seek out <color=#FFD24A>Haldor</color>, the trader in the black woods, " +
                          "and <color=#FFD24A>hold [E]</color> to speak with him rather than open his " +
                          "store. He keeps the postings, and he will not hand them to a stranger who " +
                          "has never answered one.\n\n");
            }
            else
            {
                var open = BountyBoardStore.OpenPostings();
                sb.Append(open.Count > 0
                    ? "<color=#AAAAAA>Take a posting below. It will be marked on your map.</color>\n"
                    : "<color=#AAAAAA>No postings right now. Check back shortly.</color>\n");

                // Name the elite gate explicitly when one is hanging there and the
                // player can't take it — the point of showing a locked posting is to
                // say what would unlock it.
                var eliteOpen = open.FirstOrDefault(BountyBoardStore.IsElite);
                if (eliteOpen != null && !BountyService.IsEliteEligible(player.GetPlayerID()))
                {
                    int topN = Plugin.BountyEliteRankTopN?.Value ?? 10;
                    sb.Append($"<color=#FF6B4A>An {BountyTiers.TierName(eliteOpen.tier)} posting hangs on the board.</color> " +
                              $"<color=#AAAAAA>Only hunters in the top {topN} of the duel or party ladder may answer it.</color>\n");
                }
                sb.Append('\n');
            }

            // Standing — the same numbers as the F6 board, in the place a hunter is
            // already looking.
            long id = player.GetPlayerID();
            int rank = BountyLeaderboardStore.RankOf(id);
            var rec = BountyLeaderboardStore.Find(id);
            sb.Append("<size=120%><color=#FFD24A>Your standing</color></size>\n");
            if (rec == null || rank == 0)
            {
                sb.Append("<color=#AAAAAA>No bounties answered yet.</color>\n");
            }
            else
            {
                int bonusAt = Plugin.BountyLeaderboardBonusRank?.Value ?? 3;
                sb.Append($"  #{rank} · {rec.points} points · {rec.kills} felled / {rec.communes} freed\n");
                if (bonusAt > 0)
                    sb.Append(rank <= bonusAt
                        ? "  <color=#B8F5B0>Your standing earns better rewards.</color>\n"
                        : $"  <color=#AAAAAA>Reach #{bonusAt} for better rewards.</color>\n");
            }

            var top = BountyLeaderboardStore.Ranked();
            if (top.Count > 0)
            {
                sb.Append("\n<size=120%><color=#FFD24A>Best hunters</color></size>\n");
                for (int i = 0; i < top.Count && i < 3; i++)
                    sb.Append($"  #{i + 1} {top[i].ownerName} — {top[i].points}\n");
            }

            return sb.ToString();
        }

        // Shown wherever bounty hunting isn't running — including single-player and
        // any server without the three required mods.
        private static string TeaserText()
        {
            var sb = new StringBuilder();
            sb.Append("<size=150%><color=#FF6B4A>The Wanted Board</color></size>\n\n");
            sb.Append("<color=#AAAAAA>There is no board here.</color>\n\n");
            sb.Append("Some Dvergr hardened past the point where the corruption merely held them — " +
                      "they turned it into a weapon, and they hunt the roads still. Where someone keeps " +
                      "a Wanted Board, those Dvergr can be hunted for pay: goods that take a forge or a " +
                      "cooking fire to make, and a name among the hunters who answer the hardest postings.\n\n");
            sb.Append("<color=#FFD24A>Bounty hunting runs on servers</color> with the Lost Scrolls II " +
                      "Quest pack alongside <color=#FFD24A>BiomeLords</color>, " +
                      "<color=#FFD24A>ValheimServerGuide</color> and " +
                      "<color=#FFD24A>Valheim Donations</color>.\n\n");
            sb.Append("<color=#AAAAAA>Everything else in Lost Scrolls II — companions, chores, duels, " +
                      "tournaments — works here as normal.</color>");
            return sb.ToString();
        }

        private static void Msg(string m)
        {
            if (MessageHud.instance != null)
                // Top-left for the same reason the tournament panel uses it: a
                // centred line renders behind this canvas and is never seen.
                MessageHud.instance.ShowMessage(MessageHud.MessageType.TopLeft, m);
        }
    }
}
