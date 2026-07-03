using UnityEngine;

namespace LostScrollsII.Companions
{
    // Feeding consumes a health mead potion and heals the companion.
    //
    // ROOT-CAUSE FIX (verified against the real assembly metadata, not guessed):
    // healing meads do NOT use m_food. That field is for *food* (the HP/stamina/
    // regen you gain from eating). A mead has m_food == 0; its heal is delivered
    // by m_consumeStatusEffect, which is an SE_Stats whose m_healthUpFront +
    // m_healthOverTime is the actual amount it restores. The previous version
    // filtered on (m_food > 0) and computed the heal from m_food, so it rejected
    // every real mead and would have healed 0 even if one slipped through — that
    // is why "the mead potions are not working to heal the communed Dvergr".
    //
    // We now identify a healing mead by what it does rather than by its prefab
    // name: any Consumable whose consume status effect is an SE_Stats that
    // restores health. This is self-correcting (works for any tier, and for
    // modded healing meads) and needs no fragile name matching.
    //
    // Heal transfer stays proportional to capacity (the documented design in
    // docs/Ally-Commands.md): the potion restores some fraction of the PLAYER's
    // max health, so we heal the companion that same fraction of ITS max health.
    public static class MeadFeedingService
    {
        public static bool TryFeed(Character target, Player player)
        {
            var inventory = player.GetInventory();
            ItemDrop.ItemData potion = null;
            float potionHeal = 0f;

            foreach (var item in inventory.GetAllItems())
            {
                var heal = HealthMeadRestoreAmount(item);
                if (heal > 0f)
                {
                    potion = item;
                    potionHeal = heal;
                    break;
                }
            }

            if (potion == null)
            {
                // Diagnostic: list every Consumable in the inventory with its
                // consume-effect type, so any detection miss can be diagnosed
                // from the BepInEx log rather than guessed at.
                foreach (var item in inventory.GetAllItems())
                {
                    if (item.m_shared != null &&
                        item.m_shared.m_itemType == ItemDrop.ItemData.ItemType.Consumable)
                    {
                        var name = item.m_dropPrefab != null ? item.m_dropPrefab.name : item.m_shared.m_name;
                        var se = item.m_shared.m_consumeStatusEffect;
                        Plugin.Log.LogInfo($"[feed] Consumable not matched as health mead: '{name}' " +
                            $"(m_food={item.m_shared.m_food}, consumeEffect={(se != null ? se.GetType().Name : "null")}).");
                    }
                }
                return false;
            }

            inventory.RemoveItem(potion, 1);

            // Restore exactly the flat HP the potion would give the player — no
            // scaling to the companion's (much larger) max-health pool, which
            // previously over-healed massively.
            //
            // Use Character.Heal (NOT SetHealth): SetHealth writes s_health only on
            // the client that owns the target's ZDO, with no RPC fallback, so it's a
            // silent no-op off-owner (verified against the decompiled assembly).
            // Character.Heal instead routes to the ZDO owner over RPC_Heal when we're
            // not the owner. This fixes BOTH: (1) the "duel loser can't be healed"
            // bug — after a cross-client duel the loser's ZDO is owned by the
            // winner's client, and (2) letting ANY player feed someone else's
            // companion. Crucially it does NOT claim ownership: stealing the ZDO
            // would strand the companion's follow AI on the feeder's client. Heal
            // clamps to max health internally and shows the heal number.
            var healAmount = potionHeal;
            target.Heal(healAmount, true);

            // Play the potion's OWN consume VFX on the companion — the same
            // healing burst you'd see drinking it yourself (m_consumeStatusEffect
            // .m_startEffects). Vanilla-assets-only: we reuse the potion's effect
            // list rather than authoring a new one. Parented to the companion so
            // it tracks it for the effect's lifetime.
            var startEffects = potion.m_shared.m_consumeStatusEffect != null
                ? potion.m_shared.m_consumeStatusEffect.m_startEffects
                : null;
            if (startEffects != null)
            {
                startEffects.Create(target.transform.position, target.transform.rotation, target.transform, 1f, -1);
            }

            Plugin.Log.LogInfo($"[feed] Fed companion: potion restores {potionHeal} HP " +
                $"to the companion (flat, same as it heals the player).");
            return true;
        }

        // Returns the total HP a consumable would restore (upfront + over-time),
        // or 0 if it is not a health-restoring potion. The heal lives on the
        // item's consume status effect (an SE_Stats), NOT on m_food.
        private static float HealthMeadRestoreAmount(ItemDrop.ItemData item)
        {
            if (item?.m_shared == null) return 0f;
            if (item.m_shared.m_itemType != ItemDrop.ItemData.ItemType.Consumable) return 0f;

            var stats = item.m_shared.m_consumeStatusEffect as SE_Stats;
            if (stats == null) return 0f;

            var total = stats.m_healthUpFront + stats.m_healthOverTime;
            return total > 0f ? total : 0f;
        }
    }
}
