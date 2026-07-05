using System.Collections.Generic;
using UnityEngine;

namespace LostScrollsII.Companions
{
    // Per-companion storage (docs/Ally-Inventory.md).
    //
    // A recruited Dvergr carries its OWN 4x2 (8-slot) inventory. We reuse the
    // stock vanilla Container + Inventory rather than hand-rolling storage, which
    // buys us three things for free and keeps us inside the vanilla-assets-only
    // constraint (Container/Inventory are engine types, not authored assets):
    //   * ZDO persistence + multi-client sync (Container.Save/Load/CheckForChanges
    //     on the creature's own ZNetView, under the "items" ZDO var),
    //   * a chest-identical UI when opened via InventoryGui.Show(container) — the
    //     player inventory, crafting panel and the container total-weight readout
    //     all come along (reqs 14 + 15),
    //   * item stacking / add / remove semantics used by the pickup + consumption
    //     phases.
    //
    // The Container lives on the SAME GameObject as the creature so it can share
    // the creature's ZNetView. That makes it a competing Hoverable/Interactable,
    // so CompanionContainerAccessPatch suppresses the vanilla "[E] Open" hover and
    // Interact for companion containers — all opening goes through the owner-gated
    // Y handler instead (see Plugin.HandleInventoryInput).
    [RequireComponent(typeof(DvergrCompanion))]
    public class CompanionInventory : MonoBehaviour
    {
        public const int Columns = 4;
        public const int Rows = 2;

        // req 13: a companion can carry at most this much total weight before it
        // becomes encumbered (stops picking up, stops attacking, can still move).
        public const float MaxWeight = 150f;

        private Container _container;
        private DvergrCompanion _companion;

        public Container Container => _container;
        public Inventory Inventory => _container != null ? _container.GetInventory() : null;

        private void Awake()
        {
            _companion = GetComponent<DvergrCompanion>();
            EnsureContainer();

            // The behavior driver (encumbrance / pickup / consumption) rides along
            // with storage, so it's attached wherever an inventory is.
            if (GetComponent<CompanionInventoryAI>() == null)
                gameObject.AddComponent<CompanionInventoryAI>();
        }

        // Idempotent: creates + configures the backing Container the first time,
        // and is safe to call again (used from CommunionService on recruit/restore
        // in case component ordering means the Container isn't ready yet).
        public void EnsureContainer()
        {
            if (_container != null && _container.GetInventory() != null) return;

            _container = GetComponent<Container>() ?? gameObject.AddComponent<Container>();

            // A runtime-added Container ran its Awake with the default 0x0 size and
            // an empty name. Reconfigure to our grid and rebuild the backing
            // inventory at the right size, then Load() to pull any persisted items
            // back out of the shared creature ZDO.
            _container.m_name = InventoryTitle();
            _container.m_width = Columns;
            _container.m_height = Rows;
            _container.m_checkGuardStone = false; // owner gate is ours, not the ward system
            _container.m_autoDestroyEmpty = false;

            var nview = GetComponent<ZNetView>();
            if (nview != null && nview.IsValid())
            {
                var inv = new Inventory(_container.m_name, null, Columns, Rows);
                inv.m_onChanged = _container.OnContainerChanged;
                _container.m_inventory = inv;
                _container.Load();
            }
        }

        // Cosmetic label shown at the top of the container panel — the companion's
        // display name so the owner knows whose bag this is. Not a $token, so
        // Localize() leaves it as-is.
        public string InventoryTitle()
        {
            var name = _companion != null ? _companion.DisplayName : "Dvergr";
            return $"{name}'s pack";
        }

        // Keeps the panel title in sync after a rename while the UI is open.
        public void RefreshTitle()
        {
            if (_container != null) _container.m_name = InventoryTitle();
        }

        // ---- Queries used by later phases -------------------------------------

        public float TotalWeight() => Inventory != null ? Inventory.GetTotalWeight() : 0f;

        // req 13: at/over the cap the ally stops working (no pickup, no attacks).
        public bool IsOverCapacity => TotalWeight() > MaxWeight;

        // req 5: an empty pack means "pick up nothing".
        public bool IsEmpty => Inventory == null || Inventory.NrOfItems() == 0;

        // req 4: the ally only collects item types it already carries. Match on the
        // shared name (the item identity), so any stack/quality of that item counts.
        public bool Holds(ItemDrop.ItemData item)
        {
            if (item?.m_shared == null || Inventory == null) return false;
            foreach (var owned in Inventory.GetAllItems())
                if (owned?.m_shared != null && owned.m_shared.m_name == item.m_shared.m_name)
                    return true;
            return false;
        }

        // First inventory item whose consume behavior matches a predicate — used by
        // the food/mead consumption phase.
        public ItemDrop.ItemData FindItem(System.Func<ItemDrop.ItemData, bool> match)
        {
            if (Inventory == null) return null;
            foreach (var item in Inventory.GetAllItems())
                if (item != null && match(item)) return item;
            return null;
        }

        public List<ItemDrop.ItemData> Items()
        {
            return Inventory != null ? Inventory.GetAllItems() : new List<ItemDrop.ItemData>();
        }

        // Consume one unit of an item the ally holds (food/mead). Returns false if
        // it's no longer present.
        public bool ConsumeOne(ItemDrop.ItemData item)
        {
            if (item == null || Inventory == null) return false;
            return Inventory.RemoveItem(item, 1);
        }
    }
}
