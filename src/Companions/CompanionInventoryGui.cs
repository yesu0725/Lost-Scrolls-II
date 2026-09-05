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
        private Button _renameButton;
        private TMP_Text _renameLabel;
        private bool _cloneFailed;

        // Renaming is an explicit MODE, entered and left by the button beside the
        // field, rather than something that starts when the field happens to take
        // focus. That distinction is the whole fix for "pressing E while renaming
        // closed the inventory": the suppression below has to cover the entire edit
        // session, and a TMP_InputField loses focus for a frame on all sorts of
        // things (a click landing elsewhere, the panel refreshing). Keying the
        // suppression off isFocused meant a single dropped frame let E through to
        // InventoryGui.Update, which closes the open container on the Use bind.
        private bool _editingName;

        // True while the player is renaming, so Plugin.Update and the ZInput patch
        // suppress every hotkey/bind-key (the typed letters must not fire
        // stance/feed/chore/etc., and E must not close the panel).
        public static bool IsTyping => _instance != null && _instance._editingName;

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
                _openCompanion = null;
                _openInventory = null;
                _openCharacter = null;
                return;
            }

            Refresh();
        }

        private void Refresh()
        {
            // Panel placement isn't our business any more — ContainerPanelPositioner
            // owns where the shared chest/storage panel sits (config + drag-to-move).
            // Never overwrite what the player is in the middle of typing.
            if (_nameField != null && !_editingName)
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

        // ---- widget construction ---------------------------------------------

        private void BuildWidgets()
        {
            if (_cloneFailed) return;
            var panel = InventoryGui.instance != null ? InventoryGui.instance.m_container : null;
            if (panel == null) return;

            try
            {
                EnsureNameField(panel);
                EnsureRenameButton(panel);
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
            // Enter still commits, and routes through the same exit path as the
            // button so the two can never disagree about what mode we are in.
            _nameField.onSubmit.RemoveAllListeners();
            _nameField.onSubmit.AddListener(_ => EndRename(commit: true));
            _nameField.characterLimit = 24;

            SetEditing(false);
        }

        // The button beside the name field: "Rename" arms the field, "Save" commits.
        // Cloned from the inventory's own Take All button, like every other button
        // this mod adds, so it is vanilla-styled with no authored assets.
        private void EnsureRenameButton(RectTransform panel)
        {
            if (_renameButton != null) return;

            var source = InventoryGui.instance != null ? InventoryGui.instance.m_takeAllButton : null;
            if (source == null) return;

            var clone = Object.Instantiate(source.gameObject, panel);
            clone.name = "CompanionRenameButton";

            // The container panel drives a layout group; opt out of it or the button
            // is re-positioned every frame (the same reason the inventory menu bar
            // sets this).
            var layoutElement = clone.GetComponent<LayoutElement>() ?? clone.AddComponent<LayoutElement>();
            layoutElement.ignoreLayout = true;

            var rt = clone.GetComponent<RectTransform>();
            if (rt != null) Park(rt, new Vector2(232f, -44f), new Vector2(84f, 28f));

            _renameLabel = clone.GetComponentInChildren<TMP_Text>();
            if (_renameLabel != null)
            {
                _renameLabel.enableAutoSizing = false;
                _renameLabel.fontSize = 14f;
            }

            _renameButton = clone.GetComponent<Button>();
            if (_renameButton == null) { Object.Destroy(clone); return; }

            _renameButton.onClick.RemoveAllListeners();
            _renameButton.onClick.AddListener(ToggleRename);
            _renameButton.interactable = true;
            clone.SetActive(true);

            SetEditing(false);
        }

        private void ToggleRename()
        {
            if (_editingName) EndRename(commit: true);
            else BeginRename();
        }

        private void BeginRename()
        {
            if (_nameField == null || _openCompanion == null) return;

            SetEditing(true);
            _nameField.SetTextWithoutNotify(_openCompanion.DisplayName);
            _nameField.ActivateInputField();
            _nameField.caretPosition = _nameField.text.Length;
        }

        private void EndRename(bool commit)
        {
            if (_nameField == null) return;

            if (commit) CommitName(_nameField.text);
            _nameField.DeactivateInputField();
            SetEditing(false);

            if (_openCompanion != null) _nameField.SetTextWithoutNotify(_openCompanion.DisplayName);
        }

        // The field is read-only outside an edit session, so a stray click on it
        // can't start typing without arming the button first — which is what keeps
        // IsTyping (and therefore the key suppression) honest.
        private void SetEditing(bool editing)
        {
            _editingName = editing;

            if (_nameField != null)
            {
                _nameField.readOnly = !editing;
                _nameField.interactable = editing;
            }

            if (_renameLabel != null) _renameLabel.text = editing ? "Save" : "Rename";
        }

        private void EnsureHpText(RectTransform panel)
        {
            if (_hpText != null) return;
            var weight = InventoryGui.instance != null ? InventoryGui.instance.m_containerWeight : null;
            if (weight == null) return;

            var clone = Object.Instantiate(weight.gameObject, panel);
            clone.name = "CompanionHpText";
            var rt = clone.GetComponent<RectTransform>();
            // Clear of the name field (18..228) and the Rename button (232..316).
            if (rt != null) Park(rt, new Vector2(324f, -44f), new Vector2(180f, 28f));
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
            // Closing the panel mid-rename must not leave the suppression latched on
            // — that would swallow the player's binds with no field on screen.
            if (!show && _editingName) EndRename(commit: false);

            if (_nameField != null) _nameField.gameObject.SetActive(show);
            if (_renameButton != null) _renameButton.gameObject.SetActive(show);
            if (_hpText != null) _hpText.gameObject.SetActive(show);
        }

        private void CommitName(string text)
        {
            if (_openCompanion == null) return;
            if (string.IsNullOrWhiteSpace(text)) return;
            if (text == _openCompanion.DisplayName) return;

            _openCompanion.SetName(text);
            if (_openInventory != null) _openInventory.RefreshTitle();
        }
    }
}
