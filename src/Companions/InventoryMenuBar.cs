using HarmonyLib;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace LostScrollsII.Companions
{
    // A row of buttons across the top of the player inventory screen, one per
    // full-screen panel this mod adds: Rankings, Tournaments, Bounty Board.
    //
    // WHY THIS EXISTS. Every panel used to be reachable only by function key
    // (F6/F7/F8). Those keys are still bound and still work, but a hotkey nobody
    // told you about is not discoverable — a player who never reads the config or
    // the wiki has no way to find out the ranking board exists. The inventory
    // screen is where a player already goes looking for things, so that is where
    // the entry points belong. This replaces the single Bounty Board button that
    // used to live here (BountyInventoryButtonPatch), which had exactly the same
    // rationale for exactly the same reason.
    //
    // BUILT FROM VANILLA. Each button is a CLONE of the inventory's own "Take All"
    // button, so it inherits vanilla styling with no authored assets — the same
    // technique the tournament panel and the companion inventory use.
    //
    // REBUILT PER WORLD LOAD. InventoryGui is destroyed and rebuilt on every world
    // load, taking our clones with it. That is why the row is re-validated on every
    // Show rather than created once and cached.
    //
    // ALWAYS SHOWN, even where a feature isn't running: the panel a button opens
    // explains itself (the bounty board, for instance, shows a teaser describing
    // where bounty hunting runs when the server doesn't qualify). A button that
    // vanished on unsupported servers would leave those players with no way to
    // learn the feature exists at all.
    [HarmonyPatch(typeof(InventoryGui), nameof(InventoryGui.Show))]
    public static class InventoryMenuBar
    {
        // One entry per panel. Adding a fourth is a line here and nothing else —
        // the layout is derived from the count.
        private struct MenuEntry
        {
            public string Name;      // GameObject name; also the "already built?" probe
            public string Label;
            public System.Action Open;
        }

        private static MenuEntry[] Entries => new[]
        {
            new MenuEntry
            {
                Name = "LSII_RankingsButton",
                Label = "Rankings",
                Open = () => RankingBoard.Open(),
            },
            new MenuEntry
            {
                Name = "LSII_TournamentButton",
                Label = "Tournaments",
                Open = () => TournamentRegistration.Toggle(Player.m_localPlayer),
            },
            new MenuEntry
            {
                Name = "LSII_BountyBoardButton",
                Label = "Bounty Board",
                Open = () => Bounty.BountyBoardPanel.Toggle(Player.m_localPlayer),
            },
        };

        // Top-centre of the inventory screen, clear of the hotbar row beneath it.
        private const float TopEdgeInset = -18f;
        private const float ButtonWidth = 132f;
        private const float ButtonHeight = 32f;
        private const float ButtonGap = 6f;

        public static void Postfix(InventoryGui __instance)
        {
            try { Ensure(__instance); }
            catch (System.Exception e)
            {
                Plugin.Log.LogWarning($"[ui] inventory menu bar failed: {e.Message}");
            }
        }

        private static void Ensure(InventoryGui gui)
        {
            if (gui == null || gui.m_player == null) return;

            var source = gui.m_takeAllButton;
            if (source == null) return;

            // Parented to the screen root (the container the inventory panels sit
            // in) rather than to any one panel, so "centre" means the middle of the
            // screen and the row doesn't shift when a panel is resized or another
            // mod adds one.
            var parent = (gui.m_player.parent as RectTransform) ?? gui.m_player as RectTransform;
            if (parent == null) return;

            var entries = Entries;

            // Centre the whole row: total width spans every button plus the gaps
            // between them, and the first button starts half that to the left.
            float totalWidth = entries.Length * ButtonWidth + (entries.Length - 1) * ButtonGap;
            float firstCentreX = -totalWidth / 2f + ButtonWidth / 2f;
            var offset = ParseOffset(MenuBarOffset());

            bool builtAny = false;
            for (int i = 0; i < entries.Length; i++)
            {
                var entry = entries[i];

                // Already present on this (re)built InventoryGui. Checked per button
                // rather than for the row as a whole, so a partially-built row (a
                // clone that failed last time) heals instead of staying broken.
                if (parent.Find(entry.Name) != null) continue;

                float x = firstCentreX + i * (ButtonWidth + ButtonGap);
                Build(source, parent, entry, new Vector2(x, TopEdgeInset) + offset);
                builtAny = true;
            }

            if (builtAny)
                Plugin.Log.LogInfo($"[ui] inventory menu bar: {entries.Length} button(s) under " +
                    $"'{parent.name}', row width {totalWidth}, offset {offset}.");
        }

        private static void Build(Button source, RectTransform parent, MenuEntry entry, Vector2 pos)
        {
            var clone = Object.Instantiate(source.gameObject, parent);
            clone.name = entry.Name;

            // Opt out of layout. This container drives a layout group, which would
            // otherwise override anchoredPosition every frame — the row's position
            // has to be ours deliberately, not a side effect of that group happening
            // to centre things.
            var layoutElement = clone.GetComponent<LayoutElement>() ?? clone.AddComponent<LayoutElement>();
            layoutElement.ignoreLayout = true;

            var rt = clone.GetComponent<RectTransform>();
            if (rt != null)
            {
                rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 1f);
                rt.pivot = new Vector2(0.5f, 1f);
                rt.sizeDelta = new Vector2(ButtonWidth, ButtonHeight);
                rt.localScale = Vector3.one;
                rt.anchoredPosition = pos;
            }

            var txt = clone.GetComponentInChildren<TMP_Text>();
            if (txt != null)
            {
                txt.text = entry.Label;
                txt.enableAutoSizing = false;
                txt.fontSize = 14f;
            }

            var btn = clone.GetComponent<Button>();
            if (btn != null)
            {
                var open = entry.Open;
                btn.onClick.RemoveAllListeners();
                btn.onClick.AddListener(() =>
                {
                    // Close the inventory first: these are all full-screen panels,
                    // and leaving the inventory open behind one makes cursor
                    // ownership ambiguous.
                    if (InventoryGui.instance != null) InventoryGui.instance.Hide();
                    open();
                });
                btn.interactable = true;
            }

            clone.SetActive(true);
        }

        // Where the row sits, as an "x,y" pixel nudge.
        //
        // `Interface/MenuBarOffset` is the current key. The row grew out of the
        // single Bounty Board button, so `Bounty/InventoryButtonOffset` is still
        // honoured when it has been changed from its default — an operator who had
        // already dialled that in shouldn't silently lose it just because the button
        // gained neighbours.
        private static string MenuBarOffset()
        {
            var legacy = Plugin.BountyButtonOffset?.Value;
            if (!string.IsNullOrEmpty(legacy) && legacy.Replace(" ", "") != "0,0")
                return legacy;
            return Plugin.MenuBarOffset?.Value;
        }

        private static Vector2 ParseOffset(string s)
        {
            if (string.IsNullOrEmpty(s)) return Vector2.zero;
            var parts = s.Split(',');
            if (parts.Length != 2) return Vector2.zero;
            if (!float.TryParse(parts[0].Trim(), System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out float x)) return Vector2.zero;
            if (!float.TryParse(parts[1].Trim(), System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out float y)) return Vector2.zero;
            return new Vector2(x, y);
        }
    }
}
