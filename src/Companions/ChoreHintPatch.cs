using HarmonyLib;
using LostScrollsII.Companions;
using UnityEngine;

namespace LostScrollsII.Patches
{
    // Surfaces the chore-assign command on the workstations themselves instead of
    // it being a hidden hotkey: a GetHoverText postfix appends a line to each
    // chore-able station's tooltip. If a companion is already tending that exact
    // station it instead reports who's on it (so you can't double-assign); else,
    // when the local player owns a companion, it shows the "[<key>] Set ..." hint.
    // One patch class per station type since they don't share a common chore base.
    internal static class ChoreHint
    {
        public static string Line(Object anchorObj, string verb)
        {
            var anchor = anchorObj as GameObject;
            if (anchor == null && anchorObj is Component comp) anchor = comp.gameObject;

            // Radius-aware, and INFORMATIONAL. A patch is shared now, so naming the
            // workers already on it no longer replaces the assign hint — pressing
            // the key here puts another ally on the same ground.
            string line = string.Empty;

            var workers = anchor != null ? ChoreAI.WorkersCovering(anchor) : null;
            if (workers != null && workers.Count == 1)
                line += $"\n<color=orange>{workers[0].WorkerName} is working here.</color>";
            else if (workers != null && workers.Count > 1)
                line += $"\n<color=orange>{workers.Count} allies are working here.</color>";

            if (DvergrCompanion.PlayerHasCompanion(Player.m_localPlayer))
                line += $"\n<color=yellow>[{Plugin.ChoreAssignKey.Value}] {verb}</color>";

            return line.Length > 0 ? line : null;
        }

        // Husbandry hint for a tamed, non-ally creature. RANGE-aware: if a herder's
        // pen already covers this creature it reports that instead of the assign hint
        // (and the assign path refuses a second worker the same way).
        public static string FeedLine(Character ch)
        {
            if (ch == null || !ch.IsTamed()) return null;
            if (ch.GetComponent<DvergrCompanion>() != null) return null; // our ally, not livestock

            return ChoreHint.Line(ch.gameObject, "Set companion to tend the herd");
        }

        // Append a hint line only if it isn't already present. Some creatures (the
        // Hen especially) route their hover text through BOTH Tameable and Character
        // GetHoverText for a single display, so the two feed patches would otherwise
        // each append the same line — a doubled tooltip. This makes the append
        // idempotent so the hint shows exactly once regardless of the route.
        public static void AppendOnce(ref string result, string line)
        {
            if (string.IsNullOrEmpty(line)) return;
            if (result != null && result.Contains(line)) return;
            result += line;
        }
    }

    // Smelter-family stations (smelter/blast furnace/charcoal kiln/eitr refinery/
    // spinning wheel) have no GetHoverText of their own — their tooltip comes
    // from the child add-ore Switch. Patch Switch but only when it belongs to a
    // Smelter, so doors/levers/etc. are untouched.
    [HarmonyPatch(typeof(Switch), nameof(Switch.GetHoverText))]
    public static class SmelterSwitchChoreHintPatch
    {
        public static void Postfix(Switch __instance, ref string __result)
        {
            if (__instance == null) return;
            var smelter = __instance.GetComponentInParent<Smelter>();
            if (smelter == null) return;
            // Anchor is the Smelter's GameObject — the same object BeginChore claims.
            var line = ChoreHint.Line(smelter.gameObject, "Set companion to work");
            if (line != null) __result += line;
        }
    }

    [HarmonyPatch(typeof(CookingStation), nameof(CookingStation.GetHoverText))]
    public static class CookingChoreHintPatch
    {
        public static void Postfix(CookingStation __instance, ref string __result)
        {
            var line = ChoreHint.Line(__instance, "Set companion to cook");
            if (line != null) __result += line;
        }
    }

    [HarmonyPatch(typeof(Fermenter), nameof(Fermenter.GetHoverText))]
    public static class FermenterChoreHintPatch
    {
        public static void Postfix(Fermenter __instance, ref string __result)
        {
            var line = ChoreHint.Line(__instance, "Set companion to brew");
            if (line != null) __result += line;
        }
    }

    // A chest posts a Rogue to clear the ground around it (the other half of its
    // domain — see ChoreAI.IsRogueDomain). Gated on ChoreStorage.IsStoragePiece so
    // the Obliterator, a gravestone and a ship's hold — all Containers — never
    // offer to have a worker posted at them.
    [HarmonyPatch(typeof(Container), nameof(Container.GetHoverText))]
    public static class ContainerChoreHintPatch
    {
        public static void Postfix(Container __instance, ref string __result)
        {
            if (__instance == null || !ChoreStorage.IsStoragePiece(__instance)) return;
            var line = ChoreHint.Line(__instance, "Set companion to clear this area");
            if (line != null) __result += line;
        }
    }

    // Tamed livestock — the husbandry chore's target. Most tamed animals show their
    // hover via Tameable (a Hoverable), so patch it here.
    [HarmonyPatch(typeof(Tameable), nameof(Tameable.GetHoverText))]
    public static class TameableChoreHintPatch
    {
        public static void Postfix(Tameable __instance, ref string __result)
        {
            if (__instance == null) return;
            var line = ChoreHint.FeedLine(__instance.GetComponent<Character>());
            ChoreHint.AppendOnce(ref __result, line);
        }
    }

    // Some tamed creatures (notably Chicken / Hen) surface their hover text through
    // Character rather than Tameable, so the Tameable patch above never fires for
    // them. Patch Character.GetHoverText too so those still get the husbandry hint.
    // A Hen actually routes through BOTH (Tameable's hover text delegates to the
    // Character's), so AppendOnce keeps the hint from doubling. FeedLine self-gates
    // to tamed, non-ally creatures, so players / recruit targets are unaffected.
    [HarmonyPatch(typeof(Character), nameof(Character.GetHoverText))]
    public static class CharacterFeedChoreHintPatch
    {
        public static void Postfix(Character __instance, ref string __result)
        {
            var line = ChoreHint.FeedLine(__instance);
            ChoreHint.AppendOnce(ref __result, line);
        }
    }

    // NOTE: crops and Cultivator-on-an-ItemStand used to carry farm-chore hints.
    // They don't any more — farming is started from the COMPANION, by giving it a
    // Cultivator and pressing the chore key on the ally itself (a field has no
    // station to point at, and hovering one crop of many was always an odd way to
    // say "work this ground"). The hint now lives on the companion's own tooltip,
    // in CompanionHoverTextPatch.
}
