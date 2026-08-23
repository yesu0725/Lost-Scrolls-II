using System.Collections.Generic;
using UnityEngine;

namespace LostScrollsII.Bounty
{
    // Map pins for posted bounties (docs/Bounty-Hunting.md, Phase B — requirement:
    // pin the bounty location, remove the pin once it's resolved).
    //
    // Client-side, like the companion pins: a pin is a purely local map annotation,
    // so the server never "places" one — it tells a client about a bounty and the
    // client draws it. Pins are transient (save = false), so a bounty pin can never
    // be orphaned into the player's saved map if the mod is removed or a resolution
    // message is missed.
    //
    // Keyed by bounty id rather than by object, so the pin can be removed later from
    // a resolution message alone, without holding a reference to anything.
    public static class BountyMapPin
    {
        private static readonly Dictionary<string, Minimap.PinData> _pins =
            new Dictionary<string, Minimap.PinData>();

        // The Minimap is destroyed and rebuilt on every world load, which invalidates
        // every PinData we hold. Tracking which map our handles belong to lets us drop
        // them without touching the destroyed object (the same guard CompanionMapPins
        // uses for its live pins).
        private static Minimap _boundMap;

        // Bounty pins use the vanilla BOSS icon: it already reads as "a dangerous
        // thing lives here", and it's visually distinct from the player-icon pins
        // companions use and the skull used for companion deaths.
        private const Minimap.PinType BountyPinType = Minimap.PinType.Boss;

        private static bool SyncMap(out Minimap map)
        {
            map = Minimap.instance;
            if (map == null)
            {
                // No map (not in a world) — our handles died with it.
                _pins.Clear();
                _boundMap = null;
                return false;
            }
            if (_boundMap != map)
            {
                _pins.Clear();
                _boundMap = map;
            }
            return true;
        }

        // Add or move the pin for a bounty. Safe to call repeatedly with the same id.
        public static void Show(string bountyId, string label, Vector3 pos)
        {
            if (string.IsNullOrEmpty(bountyId)) return;
            if (!SyncMap(out var map)) return;

            var name = string.IsNullOrEmpty(label) ? "Bounty" : label;

            if (_pins.TryGetValue(bountyId, out var pin) && pin != null)
            {
                pin.m_pos = pos;
                if (pin.m_name != name) pin.m_name = name;
                Style(pin);
                return;
            }

            pin = map.AddPin(pos, BountyPinType, name, false, false, 0L, default);
            _pins[bountyId] = pin;
            Style(pin);
            Plugin.Log.LogInfo($"[bounty] pinned '{name}' at ({pos.x:F0}, {pos.z:F0}).");
        }

        // Remove a bounty's pin — called when it's claimed, resolved, or expires.
        // No-ops for an unknown id, so a duplicate resolution message is harmless.
        public static void Remove(string bountyId)
        {
            if (string.IsNullOrEmpty(bountyId)) return;
            if (!SyncMap(out var map)) return;
            if (!_pins.TryGetValue(bountyId, out var pin)) return;
            if (pin != null) map.RemovePin(pin);
            _pins.Remove(bountyId);
            Plugin.Log.LogInfo($"[bounty] pin removed for bounty '{bountyId}'.");
        }

        // Drop every bounty pin (leaving a world, feature switched off, board cleared).
        public static void ClearAll()
        {
            if (!SyncMap(out var map)) return;
            foreach (var kv in _pins) if (kv.Value != null) map.RemovePin(kv.Value);
            _pins.Clear();
        }

        public static int Count => _pins.Count;

        // Tint the icon so a bounty reads as hostile at a glance. The icon element is
        // instantiated lazily by Minimap.UpdatePins (one frame after AddPin), so this
        // no-ops on the first call and takes effect on the next Show for that pin —
        // the same lazy-icon behaviour the companion pins handle.
        private static void Style(Minimap.PinData pin)
        {
            var icon = pin != null ? pin.m_iconElement : null;
            if (icon == null) return;
            icon.color = new Color(1f, 0.35f, 0.30f);
        }
    }
}
