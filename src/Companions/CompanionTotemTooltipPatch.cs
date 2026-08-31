using HarmonyLib;
using LostScrollsII.Companions;

namespace LostScrollsII.Patches
{
    // Appends the sealed companion's name + stats to a companion totem's tooltip.
    // The purpose-based item NAME and DESCRIPTION themselves come from a
    // per-instance SharedData copy (TotemConversionService.ApplyTotemShared), so
    // this patch only adds the per-companion stat block below the description.
    // Targets the static 5-arg GetTooltip overload that GetTooltip(int) delegates to.
    [HarmonyPatch(typeof(ItemDrop.ItemData), nameof(ItemDrop.ItemData.GetTooltip),
        new[] { typeof(ItemDrop.ItemData), typeof(int), typeof(bool), typeof(float), typeof(int) })]
    public static class CompanionTotemTooltipPatch
    {
        public static void Postfix(ItemDrop.ItemData item, ref string __result)
        {
            if (!TotemConversionService.IsCompanionTotem(item)) return;
            __result += TotemConversionService.BuildTooltipBlock(item);
        }
    }

    // A companion totem's custom name/description live on a per-instance SharedData
    // clone. When the item is saved and reloaded it is rebuilt from the GoblinTotem
    // prefab, resetting m_shared back to the vanilla "Fuling Totem" data — so the
    // override has to be re-applied every time an item loads. There are two
    // LoadFromZDO overloads (both static on ItemDrop, taking an ItemData param —
    // NOT on the nested ItemData type): the ZDO-based one (dropped items in the
    // world) and the index-based one (inventory / container slots); both re-apply.
    [HarmonyPatch(typeof(ItemDrop), "LoadFromZDO",
        new[] { typeof(ItemDrop.ItemData), typeof(ZDO) })]
    public static class CompanionTotemLoadZdoPatch
    {
        public static void Postfix(ItemDrop.ItemData itemData)
        {
            TotemConversionService.ReapplyTotemShared(itemData);
        }
    }

    [HarmonyPatch(typeof(ItemDrop), "LoadFromZDO",
        new[] { typeof(int), typeof(ItemDrop.ItemData), typeof(ZDO) })]
    public static class CompanionTotemLoadIndexedPatch
    {
        public static void Postfix(ItemDrop.ItemData itemData)
        {
            TotemConversionService.ReapplyTotemShared(itemData);
        }
    }

    // The player's own inventory (and every Container) is NOT loaded through
    // ItemDrop.LoadFromZDO — Inventory.Load rebuilds each item by instantiating
    // its prefab, so a saved companion totem comes back with the stock
    // "Fuling Totem" SharedData: vanilla name AND m_maxStackSize 20. That is the
    // root cause of "my sealed Dvergr turns back into a Fuling Totem after a
    // relog", and the stack cap coming back is the dangerous half — two sealed
    // companions sharing a slot would merge (Valheim stacks by shared NAME and
    // ignores m_customData) and one companion would be lost.
    //
    // The re-apply therefore has to happen BEFORE the stacking decision, not
    // after the load: Inventory.Load -> AddItem(name, ..., customData, ...) sets
    // m_customData and then calls this private AddItem overload, which is where
    // the "same shared name -> merge into that slot" check lives. Patching its
    // prefix is the first point at which the item is both identifiable as a
    // companion totem and still un-stacked.
    [HarmonyPatch(typeof(Inventory), "AddItem",
        new[] { typeof(ItemDrop.ItemData), typeof(int), typeof(int), typeof(int) })]
    public static class CompanionTotemInventoryAddPatch
    {
        public static void Prefix(ItemDrop.ItemData item)
        {
            TotemConversionService.ReapplyTotemShared(item);
        }
    }

    // Catch-all after a whole inventory has loaded: anything that reached the
    // list by another route (or was added before its custom data was written)
    // still gets its name/description/stack cap back. Idempotent and cheap —
    // ReapplyTotemShared early-outs on every non-totem item.
    [HarmonyPatch(typeof(Inventory), nameof(Inventory.Load))]
    public static class CompanionTotemInventoryLoadPatch
    {
        public static void Postfix(Inventory __instance)
        {
            var items = __instance?.m_inventory;
            if (items == null) return;
            for (int i = 0; i < items.Count; i++) TotemConversionService.ReapplyTotemShared(items[i]);
        }
    }
}
