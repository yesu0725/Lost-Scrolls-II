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

            var icon = PinIcon();
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
                    if (pin.m_type != icon) pin.m_type = icon;                    // reflect config change
                }
                else
                {
                    _pins[c] = map.AddPin(c.transform.position, icon, c.DisplayName, false, false, 0L, default);
                }
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

        // Map the configured 0-4 index to one of the vanilla map-pin sprites.
        private static Minimap.PinType PinIcon()
        {
            switch (Mathf.Clamp(Plugin.MapPinIcon.Value, 0, 4))
            {
                case 0: return Minimap.PinType.Icon0;
                case 1: return Minimap.PinType.Icon1;
                case 2: return Minimap.PinType.Icon2;
                case 4: return Minimap.PinType.Icon4;
                default: return Minimap.PinType.Icon3;
            }
        }
    }
}
