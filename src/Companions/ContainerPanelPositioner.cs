using System.Globalization;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace LostScrollsII.Companions
{
    // Owns the on-screen position of the shared chest/storage panel
    // (InventoryGui.m_container) — the panel every container uses, vanilla chests
    // and the companion pack alike.
    //
    // Two things live here:
    //   * a DEFAULT offset — the panel sits two inventory rows below its vanilla
    //     spot, so a mod-grown player inventory (extra rows extend downward) can't
    //     end up hidden behind it;
    //   * DRAG-TO-MOVE — grab any empty part of the panel with the left mouse
    //     button and drop it wherever you like. The position is written back to
    //     the config (Interface/ContainerPanelOffset), so it survives a relog.
    //
    // This replaces the old "measure the player inventory height and shift by the
    // extra rows" fix, which inferred what other slot mods (ComfyQuickSlots etc.)
    // had done. Nothing is inferred now: the panel goes where the config says, and
    // the player can move it.
    //
    // The one mod we still detect is BiomeLords, which ships the same feature for
    // the same panel. When it's loaded we disable ourselves entirely — no default
    // offset, no drag surface — and leave the panel to it.
    //
    // Vanilla-assets-only: the drag surface is a bare transparent uGUI Image, no
    // authored art.
    public class ContainerPanelPositioner : MonoBehaviour
    {
        // How far below the vanilla position the panel sits by default, in player
        // inventory rows. Two rows clears the extra row(s) slot mods add and then
        // some.
        private const float DefaultRowsBelow = 2f;
        // Fallback row height if the player grid isn't readable (vanilla spacing).
        private const float FallbackRowHeight = 68f;
        // Keep at least this many pixels of the panel on screen, so it can never be
        // dragged somewhere it can't be grabbed back from.
        private const float ScreenMargin = 48f;

        private const string AutoValue = "auto";

        private RectTransform _panel;      // the m_container we're tracking
        private Vector2 _basePos;          // its vanilla anchoredPosition
        private Vector2 _offset;           // current offset from _basePos, canvas units
        private bool _applied;             // we've moved the panel and owe a restore
        private bool _dragging;
        private string _lastConfigValue;   // so external config edits are picked up
        private bool _parseWarned;

        private ContainerDragSurface _surface;

        // BiomeLords repositions this exact panel (its "move chest UI" setting) and
        // now offers the same drag-to-move we do, so the two would fight. Detected
        // once — the plugin set is fixed for the session. (-1 unknown, 0 absent,
        // 1 present.)
        private static int _biomeLords = -1;
        public static bool BiomeLordsLoaded()
        {
            if (_biomeLords < 0)
            {
                _biomeLords = 0;
                foreach (var kv in BepInEx.Bootstrap.Chainloader.PluginInfos)
                {
                    var key = kv.Key ?? string.Empty;
                    var name = kv.Value?.Metadata?.Name ?? string.Empty;
                    if (key.IndexOf("biomelord", System.StringComparison.OrdinalIgnoreCase) >= 0
                        || name.IndexOf("biomelord", System.StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        _biomeLords = 1;
                        break;
                    }
                }
            }
            return _biomeLords == 1;
        }

        // Whether we manage the panel at all. Off when the player disabled it, or
        // when BiomeLords is present. Logged once so a reported conflict can be
        // read straight out of the BepInEx log.
        private static bool _decisionLogged;
        public static bool Enabled
        {
            get
            {
                bool cfgOn = Plugin.MoveContainerPanel == null || Plugin.MoveContainerPanel.Value;
                bool biome = BiomeLordsLoaded();
                bool on = cfgOn && !biome;
                if (!_decisionLogged)
                {
                    _decisionLogged = true;
                    Plugin.Log.LogInfo($"[inventory] container-panel positioning: {(on ? "ON" : "OFF")} " +
                        $"(config={cfgOn}, BiomeLords={biome}). BiomeLords ⇒ we leave the chest UI position to it.");
                }
                return on;
            }
        }

        private void Update()
        {
            if (!Enabled) { Release(); return; }

            var gui = InventoryGui.instance;
            var container = gui != null ? gui.m_container : null;
            // Only while a container is actually open — the panel is deactivated
            // otherwise, and its position means nothing.
            if (container == null || !container.gameObject.activeInHierarchy) return;

            Track(container);
            EnsureSurface();
            SyncFromConfig(gui);
            if (!_dragging) Apply();
        }

        // A new InventoryGui (world load) means a new panel object: capture its
        // untouched position as our baseline before we move anything.
        private void Track(RectTransform container)
        {
            if (_panel == container) return;
            _panel = container;
            _basePos = container.anchoredPosition;
            _applied = false;
            _dragging = false;
            _surface = null;
            _lastConfigValue = null;
        }

        private void SyncFromConfig(InventoryGui gui)
        {
            string raw = Plugin.ContainerPanelOffset != null ? Plugin.ContainerPanelOffset.Value : AutoValue;
            if (raw == _lastConfigValue) return;
            _lastConfigValue = raw;
            _offset = ParseOffset(raw, gui);
        }

        private Vector2 ParseOffset(string raw, InventoryGui gui)
        {
            raw = (raw ?? string.Empty).Trim();
            if (raw.Length == 0 || raw.Equals(AutoValue, System.StringComparison.OrdinalIgnoreCase))
                return DefaultOffset(gui);

            var parts = raw.Split(new[] { ',', ';', ' ' }, System.StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 2
                && float.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out float x)
                && float.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out float y))
                return new Vector2(x, y);

            if (!_parseWarned)
            {
                _parseWarned = true;
                Plugin.Log.LogWarning($"[inventory] ContainerPanelOffset '{raw}' isn't 'auto' or 'x,y' — using the default position.");
            }
            return DefaultOffset(gui);
        }

        // Two inventory rows below the vanilla spot (-y is downward in anchored
        // space). Measured off the live grid so it tracks the game's own spacing.
        private static Vector2 DefaultOffset(InventoryGui gui)
        {
            float row = gui != null && gui.m_playerGrid != null && gui.m_playerGrid.m_elementSpace > 0f
                ? gui.m_playerGrid.m_elementSpace
                : FallbackRowHeight;
            return new Vector2(0f, -DefaultRowsBelow * row);
        }

        private void Apply()
        {
            if (_panel == null) return;
            _panel.anchoredPosition = _basePos + _offset;
            _applied = true;
            ClampToScreen();
        }

        // Push the panel back if it's (nearly) off screen — a drag can't strand it
        // somewhere unreachable, and a stale config from another resolution can't
        // hide it either.
        private void ClampToScreen()
        {
            if (_panel == null) return;
            var corners = new Vector3[4];
            _panel.GetWorldCorners(corners); // screen pixels: Valheim's HUD canvas is Screen Space Overlay
            float minX = corners[0].x, maxX = corners[2].x;
            float minY = corners[0].y, maxY = corners[1].y;

            float dx = 0f, dy = 0f;
            if (maxX < ScreenMargin) dx = ScreenMargin - maxX;
            else if (minX > Screen.width - ScreenMargin) dx = Screen.width - ScreenMargin - minX;
            if (maxY < ScreenMargin) dy = ScreenMargin - maxY;
            else if (minY > Screen.height - ScreenMargin) dy = Screen.height - ScreenMargin - minY;
            if (dx == 0f && dy == 0f) return;

            var push = new Vector2(dx, dy) / CanvasScale();
            _offset += push;
            _panel.anchoredPosition = _basePos + _offset;
        }

        private float CanvasScale()
        {
            var canvas = _panel != null ? _panel.GetComponentInParent<Canvas>() : null;
            float s = canvas != null ? canvas.scaleFactor : 1f;
            return s > 0.0001f ? s : 1f;
        }

        // Put the panel back where the game had it (feature turned off mid-session,
        // or BiomeLords-style hand-off).
        private void Release()
        {
            if (_panel != null && _applied)
            {
                _panel.anchoredPosition = _basePos;
                _applied = false;
            }
            if (_surface != null) Destroy(_surface.gameObject);
            _surface = null;
            _dragging = false;
        }

        // ---- drag surface ------------------------------------------------------

        // A transparent Image stretched over the whole panel, inserted as the FIRST
        // child so every real widget (item slots, Take All, the companion pack's
        // name field) sits in front of it and keeps its own clicks. What's left for
        // us is the panel's empty background — the grab area.
        private void EnsureSurface()
        {
            if (_surface != null) return;
            if (_panel == null) return;

            var go = new GameObject("LSII_ContainerDragSurface", typeof(RectTransform));
            var rt = (RectTransform)go.transform;
            rt.SetParent(_panel, false);
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;

            var img = go.AddComponent<Image>();
            img.color = Color.clear;      // invisible, but still a raycast target
            img.raycastTarget = true;

            rt.SetAsFirstSibling();

            _surface = go.AddComponent<ContainerDragSurface>();
            _surface.Bind(this, img);
        }

        // ---- called by the drag surface ---------------------------------------

        internal void BeginDrag()
        {
            _dragging = true;
        }

        internal void DragBy(Vector2 screenDelta)
        {
            if (_panel == null) return;
            _offset += screenDelta / CanvasScale();
            _panel.anchoredPosition = _basePos + _offset;
            _applied = true;
            ClampToScreen();
        }

        internal void EndDrag()
        {
            _dragging = false;
            if (Plugin.ContainerPanelOffset == null) return;
            string value = string.Format(CultureInfo.InvariantCulture, "{0:0.##},{1:0.##}", _offset.x, _offset.y);
            _lastConfigValue = value;                    // our own write, don't re-parse it
            Plugin.ContainerPanelOffset.Value = value;   // BepInEx persists this for us
        }
    }

    // Pointer plumbing for the grab area. Kept separate so the positioner stays a
    // plain plugin-lifetime component while this one lives and dies with the panel.
    public class ContainerDragSurface : MonoBehaviour,
        IBeginDragHandler, IDragHandler, IEndDragHandler, IPointerEnterHandler, IPointerExitHandler
    {
        // Faint wash while the cursor is over grabbable space — the only hint that
        // the panel can be moved.
        private static readonly Color Hover = new Color(1f, 1f, 1f, 0.05f);

        private ContainerPanelPositioner _owner;
        private Image _image;

        internal void Bind(ContainerPanelPositioner owner, Image image)
        {
            _owner = owner;
            _image = image;
        }

        public void OnBeginDrag(PointerEventData eventData)
        {
            if (eventData.button != PointerEventData.InputButton.Left) return;
            _owner?.BeginDrag();
        }

        public void OnDrag(PointerEventData eventData)
        {
            if (eventData.button != PointerEventData.InputButton.Left) return;
            _owner?.DragBy(eventData.delta);
        }

        public void OnEndDrag(PointerEventData eventData)
        {
            if (eventData.button != PointerEventData.InputButton.Left) return;
            _owner?.EndDrag();
        }

        public void OnPointerEnter(PointerEventData eventData)
        {
            if (_image != null) _image.color = Hover;
        }

        public void OnPointerExit(PointerEventData eventData)
        {
            if (_image != null) _image.color = Color.clear;
        }
    }
}
