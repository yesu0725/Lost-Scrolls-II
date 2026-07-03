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
}
