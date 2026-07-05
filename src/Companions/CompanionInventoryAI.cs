using UnityEngine;

namespace LostScrollsII.Companions
{
    // Drives the behavior half of the companion inventory system
    // (docs/Ally-Inventory.md): encumbrance (Phase 2), item pickup (Phase 3), and
    // food/mead consumption (Phase 4). One throttled tick per second, run ONLY on
    // the client that owns the companion's ZDO (like ChoreAI), so the shared
    // inventory + health are mutated exactly once and replicate to everyone else.
    //
    // Added automatically by CompanionInventory, so it rides along wherever an
    // inventory is attached (recruit / restore / admin spawn).
    [RequireComponent(typeof(DvergrCompanion))]
    [RequireComponent(typeof(CompanionInventory))]
    public class CompanionInventoryAI : MonoBehaviour
    {
        private const float TickInterval = 1f;

        // req 3/4: pull matching loose drops within this radius straight in (a
        // radius sweep, matching the hauling chore's established behavior — the
        // companion holds its post rather than walking to each item).
        private const float PickupRange = 8f;

        // req 11: drink a health mead below this fraction, stop once above the
        // upper fraction.
        private const float MeadHealBelowFraction = 0.35f;
        private const float MeadHealUntilFraction = 0.90f;

        private DvergrCompanion _companion;
        private CompanionInventory _inv;
        private Character _character;
        private MonsterAI _ai;
        private ZNetView _znv;
        private float _tick;

        // ---- Food buff bookkeeping (delta-based so it's robust to our own
        //      repeated SetMaxHealth calls; see ExpireFood). Transient — a relog
        //      clears it, which is fine (food is a short live buff). ------------
        private float _foodPeak;      // full bonus == item m_food
        private float _foodApplied;   // bonus currently added to max health
        private float _foodDuration;
        private float _foodElapsed;

        // Health-mead latch (req 11): true between dropping below 35% and rising
        // back above 90%, so the ally keeps sipping across that whole window.
        private bool _healing;

        private void Awake()
        {
            _companion = GetComponent<DvergrCompanion>();
            _inv = GetComponent<CompanionInventory>();
            _character = GetComponent<Character>();
            _ai = GetComponent<MonsterAI>();
            _znv = GetComponent<ZNetView>();
        }

        private void Update()
        {
            if (_character == null || _character.IsDead()) return;

            // Non-owner clients don't simulate, but can still derive the encumbered
            // flag from the replicated container weight so the icon shows for all.
            bool owner = _znv == null || !_znv.IsValid() || _znv.IsOwner();
            if (!owner)
            {
                if (_companion != null && _inv != null) _companion.IsEncumbered = _inv.IsOverCapacity;
                return;
            }

            // Food buff decays smoothly every frame (independent of the 1s tick).
            TickFoodBuff(Time.deltaTime);

            // req 13: enforce encumbrance EVERY frame (not just on the 1 Hz tick) so
            // an overloaded ally truly stops attacking instead of re-acquiring a
            // target between ticks. In duel mode we leave the duel AI alone and only
            // flag the icon.
            bool encumbered = _inv.IsOverCapacity;
            if (_companion.DuelMode) _companion.IsEncumbered = encumbered;
            else _companion.ApplyEncumbrance(encumbered);

            _tick += Time.deltaTime;
            if (_tick < TickInterval) return;
            _tick = 0f;

            // Consumption runs even in combat and even when encumbered — a wounded
            // ally should still be able to gulp a heal mead, and resistances matter
            // most mid-fight.
            TickConsumption();

            // req 13: no gathering while overloaded.
            if (encumbered) return;

            // req 6: combat takes priority over gathering. While alerted / holding a
            // target the ally fights instead of collecting.
            if (_ai != null && (_ai.IsAlerted() || _ai.HaveTarget())) return;

            // Don't let free-roam pickup fight the ally's other jobs: a chore worker
            // manages its own item flow, and a duelist is occupied.
            if (_companion.ChoreActive || _companion.DuelMode) return;

            // req 5: an empty pack collects nothing.
            if (_inv.IsEmpty) return;

            TickPickup();
        }

        // ---- Phase 3: pickup --------------------------------------------------

        private void TickPickup()
        {
            var inventory = _inv.Inventory;
            if (inventory == null) return;

            // Maskless overlap + ItemDrop filter, mirroring the hauling chore
            // (ChoreAI.ServiceHaul) — the proven way this codebase finds loose drops.
            foreach (var hit in Physics.OverlapSphere(transform.position, PickupRange))
            {
                var drop = hit != null ? hit.GetComponentInParent<ItemDrop>() : null;
                if (drop == null || !drop.CanPickup(false)) continue;

                var nview = drop.GetComponent<ZNetView>();
                if (nview == null || !nview.IsValid()) continue;

                var data = drop.m_itemData;
                if (data?.m_shared == null) continue;

                // req 4/5: only collect item TYPES the ally already carries.
                if (!_inv.Holds(data)) continue;

                // Stop if it wouldn't fit (whole stack) — and stop the whole sweep,
                // since a full pack won't fit the next item either.
                if (!inventory.CanAddItem(data, data.m_stack)) return;

                inventory.AddItem(data.Clone());
                nview.ClaimOwnership();
                ZNetScene.instance.Destroy(drop.gameObject);
                return; // one pickup per tick
            }
        }

        // ---- Phase 4: consumption --------------------------------------------

        private void TickConsumption()
        {
            var inventory = _inv.Inventory;
            if (inventory == null) return;

            // 1) Resistance meads (req 12): drink one whose resistance isn't already
            //    active, and keep doing so as long as any remain in the pack.
            var resist = _inv.FindItem(it =>
                CompanionConsumables.Classify(it) == ConsumableKind.ResistMead &&
                !HasEffect(CompanionConsumables.ConsumeEffect(it)));
            if (resist != null)
            {
                DrinkStatusMead(resist);
                return; // one consume action per tick
            }

            // 2) Health mead (req 11): start sipping once HP drops below the low
            //    threshold and KEEP sipping every tick until it climbs above the
            //    high threshold (hysteresis — a single sip rarely clears 35% in one
            //    go, so without the latch it would stop after one gulp).
            float hp = _character.GetHealthPercentage();
            if (hp < MeadHealBelowFraction) _healing = true;
            else if (hp > MeadHealUntilFraction) _healing = false;

            if (_healing)
            {
                var heal = _inv.FindItem(it => CompanionConsumables.Classify(it) == ConsumableKind.HealthMead);
                if (heal != null)
                {
                    float amount = CompanionConsumables.HealAmount(heal);
                    if (_inv.ConsumeOne(heal))
                    {
                        _character.Heal(amount, true);
                        PlayConsumeEffects(heal);
                    }
                    return;
                }
                // No mead left — nothing to sip; drop the latch so we don't spin.
                _healing = false;
            }

            // 3) Food (reqs 7-10): only one at a time — don't eat while a food buff
            //    is still running.
            if (_foodApplied <= 0f && _foodElapsed >= _foodDuration)
            {
                var food = _inv.FindItem(it => CompanionConsumables.Classify(it) == ConsumableKind.Food);
                if (food != null) EatFood(food);
            }
        }

        // Drink a resistance mead: apply its consume status effect (same SE the
        // player would get) for the same duration, and play its VFX.
        private void DrinkStatusMead(ItemDrop.ItemData mead)
        {
            var se = CompanionConsumables.ConsumeEffect(mead);
            if (se == null) return;
            if (!_inv.ConsumeOne(mead)) return;

            _character.GetSEMan().AddStatusEffect(se, true, 0, 0f);
            PlayConsumeEffects(mead);
        }

        private bool HasEffect(StatusEffect se)
        {
            if (se == null || _character == null) return false;
            return _character.GetSEMan().HaveStatusEffect(se.NameHash());
        }

        private void PlayConsumeEffects(ItemDrop.ItemData item)
        {
            var se = CompanionConsumables.ConsumeEffect(item);
            var fx = se != null ? se.m_startEffects : null;
            if (fx != null)
                fx.Create(transform.position, transform.rotation, transform, 1f, -1);
        }

        // ---- Food buff (reqs 7-10) -------------------------------------------

        // Eats one food: raises max health by the food's m_food value for its burn
        // time, decaying over the back half like player food. Sets the fed flag for
        // the status icon (req 10).
        private void EatFood(ItemDrop.ItemData food)
        {
            if (!_inv.ConsumeOne(food)) return;

            _foodPeak = food.m_shared.m_food;
            _foodDuration = Mathf.Max(1f, food.m_shared.m_foodBurnTime);
            _foodElapsed = 0f;

            ApplyFoodBonus(_foodPeak);
            _companion.IsFed = true;
            _companion.FedIcon = (food.m_shared.m_icons != null && food.m_shared.m_icons.Length > 0)
                ? food.m_shared.m_icons[0] : null;
            PlayConsumeEffects(food); // usually null for plain food, harmless
        }

        private void TickFoodBuff(float dt)
        {
            if (_foodApplied <= 0f && _foodElapsed >= _foodDuration) return;

            _foodElapsed += dt;
            if (_foodElapsed >= _foodDuration) { ExpireFood(); return; }

            // Full for the first half, then linear falloff to 0 — a simple stand-in
            // for vanilla food's "gradually degrades" curve (req 9).
            float t = _foodElapsed / _foodDuration;
            float factor = t <= 0.5f ? 1f : Mathf.Clamp01(1f - (t - 0.5f) / 0.5f);
            ApplyFoodBonus(_foodPeak * factor);
        }

        // Delta-based: only ever adds/removes the CHANGE to max health, so repeated
        // calls never compound and we don't need to snapshot an absolute base that a
        // mid-buff level-up could invalidate.
        private void ApplyFoodBonus(float target)
        {
            if (_character == null) return;
            float delta = target - _foodApplied;
            if (Mathf.Abs(delta) < 0.01f) return;

            float newMax = Mathf.Max(1f, _character.GetMaxHealth() + delta);
            _character.SetMaxHealth(newMax);
            if (delta > 0f)
                _character.Heal(delta, false);            // make the added HP usable
            else if (_character.GetHealth() > newMax)
                _character.SetHealth(newMax);             // clamp when the buff shrinks
            _foodApplied = target;
        }

        private void ExpireFood()
        {
            if (_foodApplied > 0f && _character != null)
            {
                float newMax = Mathf.Max(1f, _character.GetMaxHealth() - _foodApplied);
                _character.SetMaxHealth(newMax);
                if (_character.GetHealth() > newMax) _character.SetHealth(newMax);
            }
            _foodApplied = 0f;
            _foodPeak = 0f;
            _foodElapsed = 0f;
            _foodDuration = 0f;
            if (_companion != null) { _companion.IsFed = false; _companion.FedIcon = null; }
        }
    }
}
