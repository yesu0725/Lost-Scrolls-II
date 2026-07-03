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

            var claimant = anchor != null ? ChoreAI.ClaimantOf(anchor) : null;
            if (claimant != null)
                return $"\n<color=orange>{claimant.WorkerName} is already working here.</color>";

            if (!DvergrCompanion.PlayerHasCompanion(Player.m_localPlayer)) return null;
            return $"\n<color=yellow>[{Plugin.ChoreAssignKey.Value}] {verb}</color>";
        }

        // Feed-chore hint for a tamed, non-ally creature. RANGE-aware: if a feeding
        // companion's pen already covers this creature it reports that instead of the
        // assign hint (and the assign path refuses a second mage the same way).
        public static string FeedLine(Character ch)
        {
            if (ch == null || !ch.IsTamed()) return null;
            if (ch.GetComponent<DvergrCompanion>() != null) return null; // our ally, not livestock

            var feeder = ChoreAI.FeederCovering(ch.transform.position);
            if (feeder != null)
                return $"\n<color=orange>{feeder.WorkerName} is already working here.</color>";

            if (!DvergrCompanion.PlayerHasCompanion(Player.m_localPlayer)) return null;
            return $"\n<color=yellow>[{Plugin.ChoreAssignKey.Value}] Set companion to feed</color>";
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

    [HarmonyPatch(typeof(Container), nameof(Container.GetHoverText))]
    public static class ContainerChoreHintPatch
    {
        public static void Postfix(Container __instance, ref string __result)
        {
            var line = ChoreHint.Line(__instance, "Set companion to haul here");
            if (line != null) __result += line;
        }
    }

    // Tamed livestock — the feed chore's target. Most tamed animals show their hover
    // via Tameable (a Hoverable), so patch it here.
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
    // them. Patch Character.GetHoverText too so those still get the feed hint.
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

    // Crops — the farm chore's target. Only show on Pickables sitting on cultivated
    // ground (a field), so wild berries / branches / surface stone stay untouched.
    [HarmonyPatch(typeof(Pickable), nameof(Pickable.GetHoverText))]
    public static class PickableChoreHintPatch
    {
        public static void Postfix(Pickable __instance, ref string __result)
        {
            if (__instance == null) return;
            var pos = __instance.transform.position;
            var hm = Heightmap.FindHeightmap(pos);
            if (hm == null || !hm.IsCultivated(pos)) return;
            var line = ChoreHint.Line(__instance.gameObject, "Set companion to farm here");
            if (line != null) __result += line;
        }
    }

    // A Cultivator placed on an ItemStand marks a field: hovering that stand offers
    // the farm chore, centered on the stand. GetAttachedItem() returns the attached
    // item's prefab name (verified), so "Cultivator" identifies the tool.
    [HarmonyPatch(typeof(ItemStand), nameof(ItemStand.GetHoverText))]
    public static class ItemStandFarmHintPatch
    {
        public static void Postfix(ItemStand __instance, ref string __result)
        {
            if (__instance == null) return;
            if (!__instance.HaveAttachment() || __instance.GetAttachedItem() != "Cultivator") return;
            var line = ChoreHint.Line(__instance.gameObject, "Set companion to farm this field");
            if (line != null) __result += line;
        }
    }
}
