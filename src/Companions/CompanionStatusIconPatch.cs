using HarmonyLib;
using LostScrollsII.Companions;
using UnityEngine;
using UnityEngine.UI;

namespace LostScrollsII.Patches
{
    // Renders status icons above a companion's floating health bar (reqs 10 + 13):
    //   * Fed    — a food HP buff is active; shows that food's own item icon.
    //   * Weight — the pack is over the cap; shows the vanilla "Encumbered" status
    //              effect icon.
    // Both are real in-game sprites (vanilla-assets-only). We attach a small
    // manager component to each companion's EnemyHud element, so the icons live and
    // die with the hud the game already pools — no manual cleanup, no leaks.
    [HarmonyPatch(typeof(EnemyHud), "UpdateHuds")]
    public static class CompanionStatusIconPatch
    {
        public static void Postfix(EnemyHud __instance)
        {
            if (__instance == null || __instance.m_huds == null) return;

            foreach (var kv in __instance.m_huds)
            {
                var character = kv.Key;
                if (character == null) continue;
                var companion = character.GetComponent<DvergrCompanion>();
                if (companion == null) continue;

                var hud = kv.Value;
                if (hud == null || hud.m_gui == null) continue;

                var widget = hud.m_gui.GetComponent<CompanionHudStatusIcons>();
                if (widget == null) widget = hud.m_gui.AddComponent<CompanionHudStatusIcons>();
                widget.Refresh(companion, hud);
            }
        }
    }

    // Builds and updates a horizontal row of small icon Images centered just above
    // the hud's name/health: the fed icon, the encumbered icon, and one icon per
    // active damage resistance the companion drank a mead for. Images are pooled and
    // reused; unused ones are hidden.
    public class CompanionHudStatusIcons : MonoBehaviour
    {
        private readonly System.Collections.Generic.List<Image> _pool = new System.Collections.Generic.List<Image>();
        private readonly System.Collections.Generic.List<Sprite> _sprites = new System.Collections.Generic.List<Sprite>();
        private static Sprite _encumberedSprite;
        private static bool _encumberedResolved;

        private const float IconSize = 22f;
        private const float Gap = 2f;

        public void Refresh(DvergrCompanion companion, EnemyHud.HudData hud)
        {
            Transform anchor = hud.m_name != null ? hud.m_name.transform : transform;

            _sprites.Clear();
            // req 10: fed icon (the food's own sprite).
            if (companion.IsFed && companion.FedIcon != null) _sprites.Add(companion.FedIcon);
            // req 13: encumbered icon (vanilla Encumbered status effect sprite).
            if (companion.IsEncumbered && EncumberedSprite() != null) _sprites.Add(EncumberedSprite());
            // req 12: one icon per active resistance the ally is under.
            var character = hud.m_character;
            foreach (var se in CompanionConsumables.ActiveResistEffects(character))
                if (se.m_icon != null) _sprites.Add(se.m_icon);

            // Lay the active sprites out centered above the name, hide the rest.
            float totalW = _sprites.Count * IconSize + Mathf.Max(0, _sprites.Count - 1) * Gap;
            float startX = -totalW * 0.5f + IconSize * 0.5f;

            for (int i = 0; i < _sprites.Count; i++)
            {
                var img = GetIcon(i, anchor);
                img.sprite = _sprites[i];
                img.rectTransform.anchoredPosition = new Vector2(startX + i * (IconSize + Gap), 6f);
                img.gameObject.SetActive(true);
            }
            for (int i = _sprites.Count; i < _pool.Count; i++)
                _pool[i].gameObject.SetActive(false);
        }

        private Image GetIcon(int index, Transform anchor)
        {
            if (index < _pool.Count) return _pool[index];

            var go = new GameObject($"LS_StatusIcon{index}", typeof(RectTransform), typeof(Image));
            var rt = go.GetComponent<RectTransform>();
            rt.SetParent(anchor, false);
            rt.anchorMin = new Vector2(0.5f, 1f);
            rt.anchorMax = new Vector2(0.5f, 1f);
            rt.pivot = new Vector2(0.5f, 0f);
            rt.sizeDelta = new Vector2(IconSize, IconSize);
            var img = go.GetComponent<Image>();
            img.raycastTarget = false;
            img.preserveAspect = true;
            _pool.Add(img);
            return img;
        }

        private static Sprite EncumberedSprite()
        {
            if (_encumberedResolved) return _encumberedSprite;
            _encumberedResolved = true;
            if (ObjectDB.instance != null)
            {
                var se = ObjectDB.instance.GetStatusEffect("Encumbered".GetStableHashCode());
                if (se != null) _encumberedSprite = se.m_icon;
            }
            return _encumberedSprite;
        }
    }
}
