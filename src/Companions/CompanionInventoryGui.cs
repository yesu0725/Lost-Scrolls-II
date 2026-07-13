using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace LostScrollsII.Companions
{
    // Opens a companion's storage the same way the game opens a chest
    // (InventoryGui.Show), and augments the container panel with:
    //   * a rename field, so the single Y key both renames the ally and shows its
    //     inventory (req 3),
    //   * a live HP readout next to the name (so a food buff's max-HP bump is
    //     visible).
    // (Active resistances are shown only above the companion in-world, not in this
    // panel — removed here per feedback.)
    //
    // Lives on the plugin GameObject (added in Plugin.Awake) so it survives scene
    // loads. It watches the open container each frame: while the companion panel is
    // showing it keeps the extra widgets parked in the panel and refreshed; when
    // the panel closes (or switches to a real chest) it hides them.
    //
    // The name field is a CLONE of the vanilla rename box's input field
    // (TextInput.m_inputField); the HP text is a clone of the container weight label
    // — cloning gives fully wired, correctly themed widgets and keeps us inside the
    // vanilla-assets-only constraint. If a clone ever fails the inventory still
    // opens; only that widget degrades (log + continue).
    public class CompanionInventoryGui : MonoBehaviour
    {
        private static CompanionInventoryGui _instance;

        private DvergrCompanion _openCompanion;
        private CompanionInventory _openInventory;
        private Character _openCharacter;

        private TMP_InputField _nameField;
        private TMP_Text _hpText;
        private bool _cloneFailed;

        // Vanilla player-inventory height (8×4). Mods like ComfyQuickSlots grow this
        // (CQS forces 5 rows), and the extra row extends DOWN into where our pack
        // panel sits — leaving that row hidden behind our UI. We push the container
        // panel down by the extra rows' height so it clears them (the same fix
        // BiomeLords uses). Restored when our panel closes, so vanilla chests are
        // untouched.
        private const int VanillaInventoryHeight = 4;
        // A little extra gap so the pack's header bar doesn't graze the bottom row.
        private const float ContainerClearancePx = 22f;
        private static float _baseContainerY = float.NaN;

        // BiomeLords ALSO repositions the shared InventoryGui.m_container panel (and
        // exposes a config to move the chest UI). Our shift is the same technique —
        // borrowed from BiomeLords — so when BiomeLords is loaded the two fight and
        // its "move chest UI" setting stops working. Since our companion pack uses
        // that same container panel, BiomeLords' own repositioning already covers it,
        // so we defer entirely to BiomeLords and skip our shift. Cached: the plugin
        // set is fixed for the session. (-1 unknown, 0 absent, 1 present.)
        private static int _biomeLordsLoaded = -1;
        private static bool BiomeLordsLoaded()
        {
            if (_biomeLordsLoaded < 0)
            {
                _biomeLordsLoaded = 0;
                foreach (var kv in BepInEx.Bootstrap.Chainloader.PluginInfos)
                {
                    var key = kv.Key ?? string.Empty;
                    var name = kv.Value?.Metadata?.Name ?? string.Empty;
                    if (key.IndexOf("biomelord", System.StringComparison.OrdinalIgnoreCase) >= 0
                        || name.IndexOf("biomelord", System.StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        _biomeLordsLoaded = 1;
                        break;
                    }
                }
            }
            return _biomeLordsLoaded == 1;
        }

        // Whether we should reposition the shared container panel at all. Off if the
        // user disabled it, or if BiomeLords is present (it owns that panel and our
        // shift would fight its "move chest UI" behaviour). Logged once so the
        // BepInEx log states exactly what was decided if a conflict is reported.
        private static bool _shiftDecisionLogged;
        private static bool ShouldAdjustContainer()
        {
            bool cfgOn = Plugin.AdjustContainerPanel == null || Plugin.AdjustContainerPanel.Value;
            bool biome = BiomeLordsLoaded();
            bool adjust = cfgOn && !biome;
            if (!_shiftDecisionLogged)
            {
                _shiftDecisionLogged = true;
                Plugin.Log.LogInfo($"[inventory] container-panel shift: {(adjust ? "ON" : "OFF")} " +
                    $"(config={cfgOn}, BiomeLords={biome}). BiomeLords/config off ⇒ we leave the chest UI position alone.");
            }
            return adjust;
        }

        // True while the player is typing in the companion name field, so
        // Plugin.Update can suppress every hotkey/bind-key (the typed letters must
        // not fire stance/feed/chore/etc.).
        public static bool IsTyping =>
            _instance != null && _instance._nameField != null && _instance._nameField.isFocused;

        public static void Open(DvergrCompanion companion, CompanionInventory inventory)
        {
            if (_instance == null || companion == null || inventory == null) return;
            if (InventoryGui.instance == null) return;

            inventory.EnsureContainer();
            inventory.RefreshTitle();
            if (inventory.Container == null) return;

            InventoryGui.instance.Show(inventory.Container, 0);

            _instance._openCompanion = companion;
            _instance._openInventory = inventory;
            _instance._openCharacter = companion.GetComponent<Character>();
            _instance.BuildWidgets();
            _instance.ShowWidgets(true);
            _instance.Refresh();
        }

        private void Awake() => _instance = this;

        private void Update()
        {
            if (_openCompanion == null) return;

            var gui = InventoryGui.instance;
            bool stillOpen =
                gui != null && InventoryGui.IsVisible() &&
                gui.m_currentContainer != null &&
                _openInventory != null &&
                gui.m_currentContainer == _openInventory.Container &&
                _openCharacter != null && !_openCharacter.IsDead();

            if (!stillOpen)
            {
                ShowWidgets(false);
                RestoreContainerShift();
                _openCompanion = null;
                _openInventory = null;
                _openCharacter = null;
                return;
            }

            Refresh();
        }

        private void Refresh()
        {
            // Keep the pack panel clear of extra player-inventory rows (CQS etc.)
            // every frame — CQS re-runs its own layout a frame after Show, so a
            // one-shot placement could be clobbered.
            ApplyContainerShift();

            if (_nameField != null && !_nameField.isFocused)
                _nameField.SetTextWithoutNotify(_openCompanion.DisplayName);

            if (_hpText != null && _openCharacter != null)
            {
                float cur = Mathf.Ceil(_openCharacter.GetHealth());
                float max = Mathf.Ceil(_openCharacter.GetMaxHealth());
                // Gold when a food buff is padding max HP, so the bump is obvious.
                string color = _openCompanion.IsFed ? "#FFD24A" : "#FFFFFF";
                _hpText.text = $"<color={color}>HP {cur:0} / {max:0}</color>";
            }
        }

        // ---- ComfyQuickSlots (and other slot mods) compatibility -------------

        // Shift the shared container panel down by however many rows the player
        // inventory has beyond vanilla, so a mod-added bottom row (e.g. CQS's
        // armor/quickslot row) isn't hidden behind our pack UI. delta == 0 (vanilla
        // 4 rows) leaves it exactly at its base position.
        private void ApplyContainerShift()
        {
            if (!ShouldAdjustContainer()) return;

            var gui = InventoryGui.instance;
            var player = Player.m_localPlayer;
            if (gui == null || player == null || gui.m_container == null || gui.m_playerGrid == null) return;

            var container = gui.m_container;
            if (float.IsNaN(_baseContainerY)) _baseContainerY = container.anchoredPosition.y;

            int extraRows = Mathf.Max(0, player.GetInventory().GetHeight() - VanillaInventoryHeight);
            float delta = extraRows * gui.m_playerGrid.m_elementSpace;
            float gap = delta > 0f ? ContainerClearancePx : 0f;

            var pos = container.anchoredPosition;
            pos.y = _baseContainerY - delta - gap; // -y is downward in anchored space
            container.anchoredPosition = pos;
        }

        private void RestoreContainerShift()
        {
            // Never touched the panel (BiomeLords/config off), so nothing to restore.
            if (!ShouldAdjustContainer()) return;
            if (float.IsNaN(_baseContainerY)) return;
            var container = InventoryGui.instance != null ? InventoryGui.instance.m_container : null;
            if (container == null) return;
            var pos = container.anchoredPosition;
            pos.y = _baseContainerY;
            container.anchoredPosition = pos;
        }

        // ---- widget construction ---------------------------------------------

        private void BuildWidgets()
        {
            if (_cloneFailed) return;
            var panel = InventoryGui.instance != null ? InventoryGui.instance.m_container : null;
            if (panel == null) return;

            try
            {
                EnsureNameField(panel);
                EnsureHpText(panel);
            }
            catch (System.Exception e)
            {
                _cloneFailed = true;
                Plugin.Log.LogWarning($"[inventory] Could not build companion panel widgets: {e.Message}");
            }
        }

        private void EnsureNameField(RectTransform panel)
        {
            if (_nameField != null) return;

            var source = TextInput.instance != null ? TextInput.instance.m_inputField : null;
            if (source == null) return;

            var clone = Object.Instantiate(source.gameObject, panel);
            clone.name = "CompanionNameField";

            var rt = clone.GetComponent<RectTransform>();
            if (rt != null) Park(rt, new Vector2(18f, -44f), new Vector2(210f, 28f));

            _nameField = clone.GetComponent<TMP_InputField>();
            if (_nameField == null) { Object.Destroy(clone); return; }

            _nameField.onEndEdit.RemoveAllListeners();
            _nameField.onEndEdit.AddListener(OnNameSubmitted);
            _nameField.characterLimit = 24;
        }

        private void EnsureHpText(RectTransform panel)
        {
            if (_hpText != null) return;
            var weight = InventoryGui.instance != null ? InventoryGui.instance.m_containerWeight : null;
            if (weight == null) return;

            var clone = Object.Instantiate(weight.gameObject, panel);
            clone.name = "CompanionHpText";
            var rt = clone.GetComponent<RectTransform>();
            if (rt != null) Park(rt, new Vector2(236f, -44f), new Vector2(210f, 28f));
            _hpText = clone.GetComponent<TMP_Text>();
            if (_hpText != null)
            {
                _hpText.alignment = TextAlignmentOptions.Left;
                _hpText.enableAutoSizing = false;
                _hpText.fontSize = 18f;
                _hpText.richText = true;
            }
        }

        // Anchor to the top-left of the container panel at a fixed offset/size.
        private static void Park(RectTransform rt, Vector2 anchoredPos, Vector2 size)
        {
            rt.anchorMin = new Vector2(0f, 1f);
            rt.anchorMax = new Vector2(0f, 1f);
            rt.pivot = new Vector2(0f, 1f);
            rt.anchoredPosition = anchoredPos;
            rt.sizeDelta = size;
        }

        private void ShowWidgets(bool show)
        {
            if (_nameField != null) _nameField.gameObject.SetActive(show);
            if (_hpText != null) _hpText.gameObject.SetActive(show);
        }

        private void OnNameSubmitted(string text)
        {
            if (_openCompanion == null) return;
            if (string.IsNullOrWhiteSpace(text)) return;
            if (text == _openCompanion.DisplayName) return;

            _openCompanion.SetName(text);
            if (_openInventory != null) _openInventory.RefreshTitle();
        }
    }
}
