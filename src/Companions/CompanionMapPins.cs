using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityEngine;

namespace LostScrollsII.Companions
{
    // Shows a minimap pin at each of the LOCAL player's own recruited companions
    // (docs/Ally-Commands.md). Map pins are entirely client-side, so pinning only
    // companions the local player owns means another player never sees your
    // companions on their map (and you never see theirs).
    //
    // A PIN OUTLIVES THE COMPANION'S ZONE. Pins used to be keyed on the live
    // DvergrCompanion instance and dropped the moment it left DvergrCompanion.All
    // — which is to say, the moment the owner walked far enough away for its zone to
    // unload. That is exactly when a map marker earns its keep: an ally left tending
    // a smelter at home is the one you want to find from across the map, and it was
    // the only one guaranteed to have no pin.
    //
    // So the tracker keys on the STABLE companion id and remembers the last place
    // each ally was seen. A pin is only removed when the companion is really gone —
    // it died (the death marker takes over), it was sealed into a totem, or it
    // stopped being ours. Being out of render range is not "gone".
    //
    // The remembered positions are written to a small file under BepInEx's config
    // folder, keyed by world and player, so they also survive a relog. The file is
    // deliberately client-side and disposable: losing it costs nothing but a pin
    // that reappears the next time you see that ally.
    public class CompanionMapPins : MonoBehaviour
    {
        private const float UpdateInterval = 0.25f;
        private const float SaveInterval = 30f;

        private class Tracked
        {
            public string Name;
            public Vector3 Pos;
        }

        // companionId -> last known name/position, and the pin showing it.
        private readonly Dictionary<string, Tracked> _known = new Dictionary<string, Tracked>();
        private readonly Dictionary<string, Minimap.PinData> _pins = new Dictionary<string, Minimap.PinData>();
        private readonly List<string> _stale = new List<string>();

        // Ids that are gone for good, so a stale saved entry can't resurrect them.
        private static readonly HashSet<string> s_forgotten = new HashSet<string>();

        private Minimap _boundMap;
        private float _timer;
        private float _saveTimer;
        private string _loadedFor;
        private bool _dirty;

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

            EnsureLoaded(local);

            // 1) Refresh what we can actually see.
            foreach (var c in DvergrCompanion.All)
            {
                // Strictly the local player's OWN companions (an explicit owner that
                // matches us) — never unowned/legacy allies, never other players'.
                if (c == null || c.OwnerId == 0L || !c.IsOwner(local)) continue;

                var id = KeyFor(c);
                if (string.IsNullOrEmpty(id)) continue;

                // An ally whose stable id was just backfilled has been pinned under
                // its session key up to now. Retire that entry, or the same
                // companion shows twice on the map for the rest of the session.
                if (!IsSessionKey(id))
                {
                    var session = SessionKeyFor(c);
                    if (session != null && _known.Remove(session)) _dirty = true;
                }

                // A dead ally is about to be destroyed and its death marker is
                // already down; don't re-add a live pin in the frames between.
                var character = c.GetComponent<Character>();
                if (character != null && character.IsDead()) continue;

                // Seeing it again UN-forgets it: a companion summoned back out of its
                // totem is on the map once more, and without this the seal would have
                // been permanent as far as the map was concerned.
                s_forgotten.Remove(id);

                if (!_known.TryGetValue(id, out var tracked))
                    _known[id] = tracked = new Tracked();

                if (tracked.Name != c.DisplayName || tracked.Pos != c.transform.position) _dirty = true;
                tracked.Name = c.DisplayName;
                tracked.Pos = c.transform.position;
            }

            // 2) Show every remembered ally, whether or not its zone is loaded.
            var color = PinColor();
            float scale = Mathf.Clamp(Plugin.CompanionPinScale.Value, 0.2f, 2f);

            foreach (var kv in _known)
            {
                var tracked = kv.Value;
                if (_pins.TryGetValue(kv.Key, out var pin) && pin != null)
                {
                    pin.m_pos = tracked.Pos;
                    if (pin.m_name != tracked.Name) pin.m_name = tracked.Name; // reflect renames
                }
                else
                {
                    // Uses the vanilla PLAYER icon so allies read like little players;
                    // the tint + smaller scale (applied below) set them apart from the
                    // local player's own marker.
                    pin = map.AddPin(tracked.Pos, Minimap.PinType.Player, tracked.Name, false, false, 0L, default);
                    _pins[kv.Key] = pin;
                }

                // Re-apply the tint + scale each refresh (the icon UI element is
                // created lazily by Minimap.UpdatePins, so it may be null the first
                // tick after AddPin). Cheap and keeps the look stable across zooms.
                StylePin(pin, color, scale);
            }

            // 3) Drop pins for allies we have deliberately forgotten.
            _stale.Clear();
            foreach (var kv in _pins) if (!_known.ContainsKey(kv.Key)) _stale.Add(kv.Key);
            foreach (var id in _stale)
            {
                if (_pins.TryGetValue(id, out var pin) && pin != null) map.RemovePin(pin);
                _pins.Remove(id);
            }

            _saveTimer += UpdateInterval;
            if (_saveTimer >= SaveInterval) { _saveTimer = 0f; Save(local); }
        }

        // What a pin is keyed on.
        //
        // The stable DE_CompanionId when there is one. There isn't always: that id
        // arrived with the duel ladders, so a companion freed before them carries
        // none until CommunionService backfills it on its next spawn — and keying
        // strictly on it meant those allies got NO PIN AT ALL, which is exactly the
        // "only one of my companions shows on the map" report.
        //
        // The fallback is the ZDOID, which is unique and stable for as long as the
        // world is loaded. That is enough for a pin, and NOT enough to write down:
        // ZDOIDs go through the connection/remap system and don't survive a reload
        // (the same reason chores persist a position rather than a ZDOID), so a
        // saved one would come back as a ghost pin pointing at nothing. Session-only
        // keys are therefore excluded from the save file.
        private static string KeyFor(DvergrCompanion c)
        {
            if (c == null) return null;

            var id = c.CompanionId;
            if (!string.IsNullOrEmpty(id)) return id;

            return SessionKeyFor(c);
        }

        private static string SessionKeyFor(DvergrCompanion c)
        {
            var znv = c != null ? c.GetComponent<ZNetView>() : null;
            return znv != null && znv.IsValid() ? SessionPrefix + znv.GetZDO().m_uid : null;
        }

        private const string SessionPrefix = "z:";

        private static bool IsSessionKey(string key)
            => !string.IsNullOrEmpty(key) && key.StartsWith(SessionPrefix);

        // A companion is really gone: it died, was sealed into a totem, or changed
        // hands. Only these remove a pin — being out of render range does not.
        public static void Forget(DvergrCompanion companion) => Forget(KeyFor(companion));

        public static void Forget(string companionId)
        {
            if (string.IsNullOrEmpty(companionId)) return;
            s_forgotten.Add(companionId);

            var instance = Plugin.MapPins;
            if (instance == null) return;

            instance._known.Remove(companionId);
            instance._dirty = true;
        }

        // ---- persistence -----------------------------------------------------

        private void EnsureLoaded(Player local)
        {
            var key = FileKey(local);
            if (key == null || key == _loadedFor) return;

            _loadedFor = key;
            _known.Clear();
            _pins.Clear();

            var path = FilePath(key);
            if (!File.Exists(path)) return;

            try
            {
                foreach (var line in File.ReadAllLines(path))
                {
                    // id|name|x|y|z — deliberately trivial. InvariantCulture on every
                    // float, or a comma-decimal locale writes coordinates the next
                    // session can't read back (the rule the competitive stores learned
                    // the hard way).
                    var parts = line.Split('|');
                    if (parts.Length < 5) continue;
                    if (s_forgotten.Contains(parts[0])) continue;

                    if (!float.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out var x)) continue;
                    if (!float.TryParse(parts[3], NumberStyles.Float, CultureInfo.InvariantCulture, out var y)) continue;
                    if (!float.TryParse(parts[4], NumberStyles.Float, CultureInfo.InvariantCulture, out var z)) continue;

                    _known[parts[0]] = new Tracked { Name = parts[1], Pos = new Vector3(x, y, z) };
                }

                Plugin.Log.LogInfo($"[map] restored {_known.Count} companion pin(s) for this world.");
            }
            catch (System.Exception e)
            {
                Plugin.Log.LogWarning($"[map] could not read remembered companion pins: {e.Message}");
            }
        }

        private void Save(Player local)
        {
            if (!_dirty) return;
            var key = FileKey(local);
            if (key == null) return;

            try
            {
                var path = FilePath(key);
                Directory.CreateDirectory(Path.GetDirectoryName(path));

                var lines = new List<string>(_known.Count);
                foreach (var kv in _known)
                {
                    if (IsSessionKey(kv.Key)) continue;   // see KeyFor
                    lines.Add(string.Join("|", new[]
                    {
                        kv.Key,
                        kv.Value.Name ?? string.Empty,
                        kv.Value.Pos.x.ToString("R", CultureInfo.InvariantCulture),
                        kv.Value.Pos.y.ToString("R", CultureInfo.InvariantCulture),
                        kv.Value.Pos.z.ToString("R", CultureInfo.InvariantCulture),
                    }));
                }

                File.WriteAllLines(path, lines.ToArray());
                _dirty = false;
            }
            catch (System.Exception e)
            {
                Plugin.Log.LogWarning($"[map] could not save companion pins: {e.Message}");
            }
        }

        // World + player, so one character's allies never show on another's map and
        // two worlds don't share a pin list.
        private static string FileKey(Player local)
        {
            if (local == null || ZNet.instance == null) return null;
            var world = ZNet.instance.GetWorldName();
            if (string.IsNullOrEmpty(world)) return null;

            foreach (var bad in Path.GetInvalidFileNameChars()) world = world.Replace(bad, '_');
            return $"{world}.{local.GetPlayerID()}";
        }

        private static string FilePath(string key)
            => Path.Combine(Path.Combine(BepInEx.Paths.ConfigPath, "LostScrollsII"), $"pins.{key}.txt");

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
