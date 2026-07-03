using System.Globalization;
using System.Reflection;
using UnityEngine;

namespace LostScrollsII.Companions
{
    // "Companion totems" — sealing a recruited Dvergr into a carriable GoblinTotem
    // item and later summoning it back (docs/Companion-Totems.md).
    //
    // Feasibility (verified against the decompiled assembly, not guessed):
    //  - ItemDrop.ItemData.m_customData is a persisted Dictionary<string,string>
    //    (saved/loaded to ZDO + inventory strings), so per-companion state rides
    //    on the individual item instance and survives save/reload/drop/trade.
    //  - GoblinTotem and Wisp are stock vanilla prefabs (no new assets), resolved
    //    at runtime from ObjectDB with a null-log guard like the rest of the mod.
    //  - Summoning reuses CommunionService.SpawnRecruited, which already writes
    //    every recruit ZDO flag the DvergrCompanion component reads on Awake.
    //
    // Two totems must never stack (Valheim stacks by shared name and ignores
    // m_customData, which would merge two companions and lose one). GoblinTotem's
    // shared max stack is forced to 1 on ObjectDB load — see GoblinTotemStackPatch.
    public static class TotemConversionService
    {
        public const string TotemPrefab = "GoblinTotem";
        public const string WispPrefab = "Wisp";

        // The purpose-based item name + description shown for a companion totem
        // (replacing the stock "Fuling Totem" / "Channels the ancient power of
        // Yagluth"). These live on a PER-INSTANCE SharedData copy (see
        // ApplyTotemShared) so real Fuling Totems keep their vanilla name.
        public const string ItemName = "Communion Totem";
        public const string ItemDescription =
            "A vessel of the Communion Rite. Within slumbers a freed Dvergr, bound to the totem until you call it forth again.";

        // Vanilla effect prefabs reused for the seal/summon VFX (no new assets).
        // Tried in order — the first that resolves in ZNetScene is used, so a wrong
        // name degrades gracefully instead of throwing. Seal = a soul dissipating;
        // summon = a spawn burst.
        private static readonly string[] SealFx = { "vfx_ghost_death", "fx_spawn_smoke", "vfx_spawn" };
        private static readonly string[] SummonFx = { "vfx_spawn", "fx_spawn_smoke", "vfx_ghost_death" };

        private static readonly MethodInfo MemberwiseCloneMethod =
            typeof(object).GetMethod("MemberwiseClone", BindingFlags.Instance | BindingFlags.NonPublic);

        // m_customData keys. Prefixed like every other ZDO/data key in the mod.
        private const string KeyMarker = "DE_Totem";
        private const string KeyCaste = "DE_TotemCaste";
        private const string KeyLevel = "DE_TotemLevel";
        private const string KeyXp = "DE_TotemXp";
        private const string KeyName = "DE_TotemName";       // custom name, empty = use default
        private const string KeyOwner = "DE_TotemOwner";
        private const string KeyOwnerName = "DE_TotemOwnerName";

        private static string _wispSharedName;

        // The localized-token shared name Wisp items carry, cached from ObjectDB.
        // Used to count/consume Wisps in the incinerator container by name.
        public static string WispSharedName()
        {
            if (!string.IsNullOrEmpty(_wispSharedName)) return _wispSharedName;
            var prefab = ObjectDB.instance != null ? ObjectDB.instance.GetItemPrefab(WispPrefab) : null;
            var shared = prefab != null ? prefab.GetComponent<ItemDrop>()?.m_itemData?.m_shared : null;
            if (shared != null) _wispSharedName = shared.m_name;
            return _wispSharedName;
        }

        public static bool IsCompanionTotem(ItemDrop.ItemData item)
        {
            return item?.m_customData != null && item.m_customData.ContainsKey(KeyMarker);
        }

        // Builds a fresh GoblinTotem item stamped with this companion's state.
        // Returns null if the vanilla prefab is missing (logged).
        public static ItemDrop.ItemData CreateTotem(DvergrCompanion companion)
        {
            if (companion == null) return null;
            var prefab = ObjectDB.instance != null ? ObjectDB.instance.GetItemPrefab(TotemPrefab) : null;
            var drop = prefab != null ? prefab.GetComponent<ItemDrop>() : null;
            if (drop == null)
            {
                Plugin.Log.LogWarning($"[totem] '{TotemPrefab}' prefab not found in ObjectDB — cannot seal companion.");
                return null;
            }

            var item = drop.m_itemData.Clone();
            item.m_dropPrefab = prefab;
            item.m_stack = 1;
            item.m_quality = 1;
            item.m_worldLevel = (byte)Game.m_worldLevel;

            // Crafter fields give a native "$item_crafter: <name>" line for free,
            // so the totem reads as belonging to that companion even before our
            // tooltip block. The full stat block is appended in the tooltip patch.
            item.m_crafterName = companion.DisplayName;
            item.m_crafterID = companion.OwnerId;

            var d = item.m_customData;
            d[KeyMarker] = "1";
            d[KeyCaste] = ((int)companion.Caste).ToString(CultureInfo.InvariantCulture);
            d[KeyLevel] = companion.Level.ToString(CultureInfo.InvariantCulture);
            d[KeyXp] = companion.Xp.ToString("R", CultureInfo.InvariantCulture);
            d[KeyName] = companion.HasCustomName ? companion.DisplayName : string.Empty;
            d[KeyOwner] = companion.OwnerId.ToString(CultureInfo.InvariantCulture);
            d[KeyOwnerName] = companion.OwnerName ?? string.Empty;

            ApplyTotemShared(item);
            return item;
        }

        // Gives the totem its OWN SharedData (a shallow clone of GoblinTotem's) with
        // the purpose-based name/description and non-stacking. Real Fuling Totems
        // are untouched (they keep the shared prefab data). Only strings + a stack
        // cap are changed, so sharing the clone's lists/icon by reference is safe.
        // maxStackSize=1 is what actually prevents two companions merging (Valheim
        // stacks by shared name and ignores m_customData).
        public static void ApplyTotemShared(ItemDrop.ItemData item)
        {
            if (item?.m_shared == null || MemberwiseCloneMethod == null) return;
            var shared = MemberwiseCloneMethod.Invoke(item.m_shared, null) as ItemDrop.ItemData.SharedData;
            if (shared == null) return;
            shared.m_name = ItemName;
            shared.m_description = ItemDescription;
            shared.m_maxStackSize = 1;
            item.m_shared = shared;
        }

        // Re-applies the custom SharedData after an item loads from disk (the loaded
        // instance is rebuilt from the GoblinTotem prefab, so its shared resets to
        // "Fuling Totem"). Called from the LoadFromZDO patches. Idempotent.
        public static void ReapplyTotemShared(ItemDrop.ItemData item)
        {
            if (!IsCompanionTotem(item)) return;
            if (item.m_shared != null && item.m_shared.m_name == ItemName) return; // already ours
            ApplyTotemShared(item);
        }

        // Summons the sealed companion at the point the player is looking, owned by
        // the summoning player. Returns false if no valid spawn point / prefab.
        public static bool TrySummon(Player player, ItemDrop.ItemData totem)
        {
            if (player == null || !IsCompanionTotem(totem)) return false;
            if (!TryGetAimPoint(player, out var pos)) return false;

            var caste = (DvergrCaste)GetInt(totem, KeyCaste, 0);
            int level = Mathf.Clamp(GetInt(totem, KeyLevel, 1), 1, DvergrCompanion.MaxLevel);
            float xp = GetFloat(totem, KeyXp, 0f);
            string name = GetStr(totem, KeyName);

            var go = CommunionService.SpawnRecruited(caste, level, player, pos + Vector3.up * 0.5f, xp);
            if (go == null) return false;

            if (!string.IsNullOrEmpty(name))
                go.GetComponent<DvergrCompanion>()?.SetName(name);

            PlaySummonVfx(go.transform.position);
            Plugin.Log.LogInfo($"[totem] Summoned {caste} (lv {level}) from totem for {player.GetPlayerName()}.");
            return true;
        }

        // VFX (vanilla effect prefabs) for the two ends of the system.
        public static void PlaySealVfx(Vector3 pos) => PlayEffect(SealFx, pos);
        public static void PlaySummonVfx(Vector3 pos) => PlayEffect(SummonFx, pos);

        private static void PlayEffect(string[] candidates, Vector3 pos)
        {
            if (ZNetScene.instance == null) return;
            foreach (var candidate in candidates)
            {
                var prefab = ZNetScene.instance.GetPrefab(candidate);
                if (prefab == null) continue;
                var list = new EffectList
                {
                    m_effectPrefabs = new[] { new EffectList.EffectData { m_prefab = prefab, m_enabled = true } }
                };
                list.Create(pos, Quaternion.identity);
                return;
            }
            Plugin.Log.LogWarning($"[totem] No VFX prefab resolved (tried: {string.Join(", ", candidates)}).");
        }

        // Raycast from the camera to find where the player is looking; falls back
        // to a point a few metres in front of the player if nothing is hit.
        private static bool TryGetAimPoint(Player player, out Vector3 point)
        {
            point = Vector3.zero;
            var cam = GameCamera.instance;
            if (cam != null)
            {
                int mask = LayerMask.GetMask("Default", "static_solid", "Default_small", "piece", "terrain", "vehicle");
                if (Physics.Raycast(cam.transform.position, cam.transform.forward, out var hit, 50f, mask))
                {
                    point = hit.point;
                    return true;
                }
            }
            if (player == null) return false;
            point = player.transform.position + player.transform.forward * 4f;
            return true;
        }

        // Tooltip stat block appended to a companion totem (see tooltip patch).
        public static string BuildTooltipBlock(ItemDrop.ItemData item)
        {
            var caste = (DvergrCaste)GetInt(item, KeyCaste, 0);
            int level = GetInt(item, KeyLevel, 1);
            string name = GetStr(item, KeyName);
            string ownerName = GetStr(item, KeyOwnerName);
            if (string.IsNullOrEmpty(name)) name = caste.Display();

            var sb = new System.Text.StringBuilder();
            sb.Append("\n\n<color=#FFD24A>⚔ ").Append(name).Append("</color>");
            sb.Append("\n").Append(caste.Display()).Append(" — <color=orange>Level ").Append(level).Append("</color>");
            if (level >= DvergrCompanion.MaxLevel) sb.Append(" (max)");
            if (!string.IsNullOrEmpty(ownerName))
                sb.Append("\nBound to <color=orange>").Append(ownerName).Append("</color>");
            sb.Append("\n\n<color=#AAAAAA>Use to summon this companion where you are looking.</color>");
            return sb.ToString();
        }

        private static int GetInt(ItemDrop.ItemData item, string key, int fallback)
        {
            if (item?.m_customData != null && item.m_customData.TryGetValue(key, out var s)
                && int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var v))
                return v;
            return fallback;
        }

        private static float GetFloat(ItemDrop.ItemData item, string key, float fallback)
        {
            if (item?.m_customData != null && item.m_customData.TryGetValue(key, out var s)
                && float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var v))
                return v;
            return fallback;
        }

        private static string GetStr(ItemDrop.ItemData item, string key)
        {
            if (item?.m_customData != null && item.m_customData.TryGetValue(key, out var s)) return s;
            return string.Empty;
        }
    }
}
