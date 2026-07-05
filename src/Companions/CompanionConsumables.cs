using UnityEngine;

namespace LostScrollsII.Companions
{
    // Classifies the consumables a companion carries so CompanionInventoryAI knows
    // what to do with each (docs/Ally-Inventory.md §Consumption). Identifies items
    // by what they DO (their fields / consume status effect), never by prefab name,
    // so it self-corrects across tiers and modded items — the same philosophy as
    // MeadFeedingService.
    public enum ConsumableKind
    {
        None,
        Food,        // m_food > 0: temporary max-HP buff (reqs 7-10)
        HealthMead,  // consume SE heals: sip when hurt (req 11)
        ResistMead   // consume SE grants a damage resistance: poison/fire/frost (req 12)
    }

    public static class CompanionConsumables
    {
        public static ConsumableKind Classify(ItemDrop.ItemData item)
        {
            if (item?.m_shared == null) return ConsumableKind.None;
            if (item.m_shared.m_itemType != ItemDrop.ItemData.ItemType.Consumable) return ConsumableKind.None;

            // Real food carries an m_food value; that's what player food buffs read.
            if (item.m_shared.m_food > 0f) return ConsumableKind.Food;

            var stats = item.m_shared.m_consumeStatusEffect as SE_Stats;
            if (stats == null) return ConsumableKind.None;

            // A healing mead restores health up-front and/or over time.
            if (stats.m_healthUpFront + stats.m_healthOverTime > 0f) return ConsumableKind.HealthMead;

            // A resistance mead's status effect carries a damage-resistance modifier
            // (fire/frost/poison barley-wine & resist meads). Stamina meads have no
            // resistance mods, so they fall through to None and are ignored.
            if (GrantsResistance(stats)) return ConsumableKind.ResistMead;

            return ConsumableKind.None;
        }

        // Total HP a health mead restores (up-front + over-time), for the sip heal.
        public static float HealAmount(ItemDrop.ItemData item)
        {
            var stats = item?.m_shared?.m_consumeStatusEffect as SE_Stats;
            if (stats == null) return 0f;
            return Mathf.Max(0f, stats.m_healthUpFront + stats.m_healthOverTime);
        }

        // The status effect a mead applies when drunk (resistance or heal-over-time).
        public static StatusEffect ConsumeEffect(ItemDrop.ItemData item)
        {
            return item?.m_shared != null ? item.m_shared.m_consumeStatusEffect : null;
        }

        private static bool GrantsResistance(SE_Stats stats)
        {
            if (stats.m_mods == null) return false;
            foreach (var mod in stats.m_mods)
            {
                switch (mod.m_modifier)
                {
                    case HitData.DamageModifier.Resistant:
                    case HitData.DamageModifier.VeryResistant:
                    case HitData.DamageModifier.Immune:
                        return true;
                }
            }
            return false;
        }

        // The resistance status effects currently active on a character (fire /
        // frost / poison resist from meads it drank). Used to show which resist is
        // active — both above the companion and in its inventory panel — and to
        // confirm the resistance really landed.
        public static System.Collections.Generic.List<StatusEffect> ActiveResistEffects(Character c)
        {
            var list = new System.Collections.Generic.List<StatusEffect>();
            var seman = c != null ? c.GetSEMan() : null;
            if (seman == null) return list;
            foreach (var se in seman.GetStatusEffects())
                if (se is SE_Stats stats && GrantsResistance(stats)) list.Add(se);
            return list;
        }
    }
}
