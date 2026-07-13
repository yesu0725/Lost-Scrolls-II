using System.Collections.Generic;
using UnityEngine;

namespace LostScrollsII.Companions
{
    // Shows a live minimap pin at each of the LOCAL player's own recruited
    // companions (docs/Ally-Commands.md). Map pins are entirely client-side, so
    // pinning only companions the local player owns means another player never sees
    // your companions on their map (and you never see theirs).
    //
    // One transient pin per companion (save = false, so nothing is written to the
    // map save file); its position is refreshed as the companion moves and it's
    // removed when the companion despawns / is no longer ours. Runs off a component
    // on the plugin GameObject (persists across scene loads); rebuilds cleanly when
    // the Minimap is recreated (entering/leaving a world).
    public class CompanionMapPins : MonoBehaviour
    {
        private const float UpdateInterval = 0.25f;

        private readonly Dictionary<DvergrCompanion, Minimap.PinData> _pins =
            new Dictionary<DvergrCompanion, Minimap.PinData>();
        private readonly HashSet<DvergrCompanion> _seen = new HashSet<DvergrCompanion>();
        private readonly List<DvergrCompanion> _stale = new List<DvergrCompanion>();

        private Minimap _boundMap;
        private float _timer;

        private void Update()
        {
            var map = Minimap.instance;
            var local = Player.m_localPlayer;

            // No map / not in a world → the pins died with the old Minimap; drop our
            // stale references so we rebuild fresh when a map exists again.
            if (map == null || local == null || !Plugin.ShowMapPins.Value)
            {
                if (_pins.Count > 0) { if (map == null) _pins.Clear(); else RemoveAll(map); }
                _boundMap = map;
                return;
            }

            // Minimap was recreated (new world / respawn) — our old PinData handles
            // belong to the destroyed map, so forget them without touching it.
            if (_boundMap != map) { _pins.Clear(); _boundMap = map; }

            _timer += Time.deltaTime;
            if (_timer < UpdateInterval) return;
            _timer = 0f;

            var color = PinColor();
            float scale = Mathf.Clamp(Plugin.CompanionPinScale.Value, 0.2f, 2f);
            _seen.Clear();

            foreach (var c in DvergrCompanion.All)
            {
                // Strictly the local player's OWN companions (an explicit owner that
                // matches us) — never unowned/legacy allies, never other players'.
                if (c == null || c.OwnerId == 0L || !c.IsOwner(local)) continue;

                _seen.Add(c);
                if (_pins.TryGetValue(c, out var pin) && pin != null)
                {
                    pin.m_pos = c.transform.position;
                    if (pin.m_name != c.DisplayName) pin.m_name = c.DisplayName; // reflect renames
                }
                else
                {
                    // Uses the vanilla PLAYER icon so allies read like little players;
                    // the tint + smaller scale (applied below) set them apart from the
                    // local player's own marker.
                    pin = map.AddPin(c.transform.position, Minimap.PinType.Player, c.DisplayName, false, false, 0L, default);
                    _pins[c] = pin;
                }

                // Re-apply the tint + scale each refresh (the icon UI element is
                // created lazily by Minimap.UpdatePins, so it may be null the first
                // tick after AddPin). Cheap and keeps the look stable across zooms.
                StylePin(pin, color, scale);
            }

            // Drop pins for companions that despawned or are no longer ours.
            _stale.Clear();
            foreach (var kv in _pins) if (!_seen.Contains(kv.Key)) _stale.Add(kv.Key);
            foreach (var c in _stale)
            {
                if (_pins.TryGetValue(c, out var pin) && pin != null) map.RemovePin(pin);
                _pins.Remove(c);
            }
        }

        private void RemoveAll(Minimap map)
        {
            foreach (var kv in _pins) if (kv.Value != null) map.RemovePin(kv.Value);
            _pins.Clear();
        }

        // Tint + shrink a pin's rendered icon. m_iconElement is the vanilla pin's
        // UI Image (publicized); it's instantiated by Minimap.UpdatePins one frame
        // after AddPin, so this no-ops until then.
        private static void StylePin(Minimap.PinData pin, Color color, float scale)
        {
            var icon = pin != null ? pin.m_iconElement : null;
            if (icon == null) return;
            icon.color = color;
            icon.rectTransform.localScale = new Vector3(scale, scale, 1f);
        }

        private static Color PinColor()
        {
            var hex = Plugin.CompanionPinColor.Value ?? string.Empty;
            if (!hex.StartsWith("#")) hex = "#" + hex;
            return ColorUtility.TryParseHtmlString(hex, out var c) ? c : new Color(1f, 0.72f, 0.30f);
        }

        // Drops a persistent (saved) death marker on the LOCAL player's map at the
        // spot a companion fell, labelled with its name. Client-side like the live
        // pins, so only the owner sees it. Called from the death patch.
        public static void AddDeathMarker(string companionName, Vector3 pos)
        {
            if (Plugin.ShowDeathMarker == null || !Plugin.ShowDeathMarker.Value) return;
            var map = Minimap.instance;
            if (map == null) return;
            var label = string.IsNullOrEmpty(companionName) ? "Fallen companion" : companionName;
            // save = true so it persists across sessions like a tombstone marker,
            // until the player removes it by clicking the pin.
            map.AddPin(pos, Minimap.PinType.Death, label, true, false, 0L, default);
            Plugin.Log.LogInfo($"[map] death marker placed for '{label}'.");
        }
    }
}
