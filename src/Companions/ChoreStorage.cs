using System.Collections.Generic;
using UnityEngine;

namespace LostScrollsII.Companions
{
    // Where a chore worker puts what it makes, and where it draws what it needs.
    // See docs/Ally-Chores.md ("Storing the products of a chore").
    //
    // Every chore now ends in a chest rather than a pile on the ground: a smelter
    // worker stows its bars, a cook its meals, a brewer its meads, a herder the
    // eggs. The companion's OWN pack is never a destination — a chore worker moves
    // goods between the world and your storage, it doesn't hoard them (and the pack
    // is eight slots with a weight cap that would stop it fighting once full).
    //
    // "A chest" is deliberately not a hardcoded prefab list. Anything that is a
    // PLACED container piece qualifies — the four vanilla chests, a cart, a barrel,
    // and any modded storage built the same way — which is both simpler and more
    // permissive than naming prefabs. What matters is the exclusions, because the
    // vanilla Container component is used by several things that are emphatically
    // not storage:
    //
    //   * Incinerator (the Obliterator) — its m_container is a real Container, so
    //     without this an ally would happily feed the smelter's output into the one
    //     station whose entire job is destroying items. It is also where our own
    //     Communion Totem ritual runs (docs/Companion-Totems.md).
    //   * TombStone and Corpse — a player's gravestone, and the loot bag a
    //     destroyed chest leaves behind. Both are Containers; neither is somewhere
    //     to file your iron. (They are separate MonoBehaviours with no shared base,
    //     so both are named here.)
    //   * Ships — karve/longship cargo is a plain Container child of the hull, a
    //     placed piece by every other test, so it needs its own exclusion. Judgment
    //     call: an ally shouldn't quietly load your longship, and a moored ship can
    //     drift out of range mid-chore.
    //   * Companion packs — CompanionInventory puts a Container on the creature
    //     itself (docs/Ally-Inventory.md). Covers this worker's own pack and any
    //     ally standing nearby.
    //   * Dungeon/loot chests — Containers with no Piece, filtered out by the
    //     "must be a placed piece (or a cart)" rule rather than by name.
    public static class ChoreStorage
    {
        private const float DefaultSearchRadius = 10f;

        // How far a worker looks for a chest, both to store products and to draw
        // inputs. One radius for both, so "the chests my ally can see" means one
        // thing (docs/Ally-Chores.md).
        public static float SearchRadius =>
            Plugin.ChoreChestRadius != null ? Plugin.ChoreChestRadius.Value : DefaultSearchRadius;

        // Is this container somewhere a chore worker may read from / write to on
        // behalf of `ownerId`? See the class comment for why each exclusion exists.
        public static bool IsUsableStorage(Container c, long ownerId)
        {
            if (!IsStoragePiece(c)) return false;

            // Another player's personal chest. Vanilla's own access rule, asked on
            // behalf of the companion's owner rather than whoever is looking.
            if (!c.CheckAccess(ownerId)) return false;

            // Guard stones. Vanilla gates chest interaction on the ward
            // (Container.m_checkGuardStone), and a companion must not become the way
            // around one — an ally standing in someone else's warded base would
            // otherwise quietly file goods into their chests. The owner's OWN ward
            // must never get in the way, though; see WardPermits.
            if (c.m_checkGuardStone && !WardPermits(c.transform.position, ownerId)) return false;

            // NOT gated on Container.IsInUse(). That was tried and had to come out:
            // our own lid animation (ChoreAI.OpenChest) calls Container.SetInUse,
            // so the instant a worker deposited into a chest that chest became
            // invisible — to the next worker, and to itself for the rest of the
            // same tick, which is what made "chest full -> use the next one" report
            // no storage instead of moving on. It also bought nothing it claimed
            // to: SetInUse is local state, never replicated, so another player's
            // open chest on another client never sets it here. Vanilla lets several
            // players share a chest and refreshes the open GUI from Inventory's
            // change event; companions share one the same way.

            return true;
        }

        // Is this container STORAGE at all, structurally? The half of the test that
        // has nothing to do with who is asking, so the hover hint and
        // ChoreAI.KindFor can use it without an owner in hand. See the class
        // comment for why each exclusion is here.
        public static bool IsStoragePiece(Container c)
        {
            if (c == null || c.GetInventory() == null) return false;

            var nview = c.m_nview;
            if (nview == null || !nview.IsValid()) return false;

            // Storage is a placed piece — or a cart, which carries its own Vagon
            // reference on the container.
            if (c.m_piece == null && c.m_wagon == null) return false;

            if (c.GetComponentInParent<Incinerator>() != null) return false;
            if (c.GetComponentInParent<TombStone>() != null) return false;
            if (c.GetComponentInParent<Corpse>() != null) return false;
            if (c.GetComponentInParent<Ship>() != null) return false;
            if (c.GetComponentInParent<DvergrCompanion>() != null) return false;

            return true;
        }

        // Does every ward covering this spot permit the companion's OWNER?
        //
        // This mirrors vanilla's own PrivateArea.HaveLocalAccess, which is
        //     m_piece.IsCreator() || IsPermitted(localPlayerId)
        // asked for an arbitrary player instead of the local one — the static
        // PrivateArea.CheckAccess resolves against whoever is looking, which is the
        // wrong question here and unanswerable at all on a dedicated server, where
        // the chore runs with no local player.
        //
        // BOTH halves are needed, and the first one is the one that bit: a ward's
        // CREATOR IS NOT IN ITS PERMITTED LIST. PrivateArea.Setup only records the
        // creator's NAME on the ward; the creator's player id lives on the Piece,
        // and vanilla checks it separately. So an IsPermitted-only test says no to
        // the very player who placed the ward — which made a companion refuse
        // every chest in its owner's own warded base (reported in a live session).
        //
        // m_allAreas holds the loaded wards, which is exactly the set that can
        // cover a loaded chest.
        public static bool WardPermits(Vector3 pos, long ownerId)
        {
            var areas = PrivateArea.m_allAreas;
            if (areas == null || ownerId == 0L) return true;

            foreach (var area in areas)
            {
                if (area == null || !area.IsEnabled()) continue;
                if (!area.IsInside(pos, 0f)) continue;

                if (area.m_piece != null && area.m_piece.GetCreator() == ownerId) continue;
                if (area.IsPermitted(ownerId)) continue;

                return false;
            }
            return true;
        }

        // Every usable container within range, NEAREST FIRST. Distance is 3D like
        // the rest of the chore system, so a chest a floor up still counts.
        public static List<Container> Nearby(Vector3 center, long ownerId, float radius = -1f)
        {
            if (radius <= 0f) radius = SearchRadius;

            var found = new List<Container>();
            foreach (var hit in Physics.OverlapSphere(center, radius))
            {
                var c = hit.GetComponentInParent<Container>();
                if (c == null || found.Contains(c)) continue;
                if (!IsUsableStorage(c, ownerId)) continue;
                found.Add(c);
            }

            found.Sort((a, b) =>
                (a.transform.position - center).sqrMagnitude
                    .CompareTo((b.transform.position - center).sqrMagnitude));
            return found;
        }

        // The nearest usable container, or null. Used for chore INPUTS (ore, fuel,
        // seed, raw food) — the same filtered, ordered view the outputs use.
        public static Container NearestSource(Vector3 center, long ownerId)
        {
            var list = Nearby(center, ownerId);
            return list.Count > 0 ? list[0] : null;
        }

        // Take ownership before writing. Container.OnContainerChanged only calls
        // Save() on the ZDO owner, and Container.CheckForChanges reloads from the
        // ZDO whenever a newer revision arrives — so an add or remove made from a
        // non-owning client lands in a local copy and is then quietly overwritten.
        // Vanilla dodges this by claiming the chest when a player opens it; a
        // companion has to claim it the same way. This matters on a dedicated
        // server, where a chest is usually owned by the server rather than by
        // whichever client happens to be running the chore.
        public static void ClaimForWrite(Container c)
        {
            if (c == null) return;
            var nview = c.m_nview;
            if (nview != null && nview.IsValid() && !nview.IsOwner()) nview.ClaimOwnership();
        }

        // Store `stack` of `item` in the best chest in range; returns the chest
        // used, or null if nothing would take it.
        //
        // Two passes, both nearest-first: a chest that ALREADY HOLDS this item wins
        // over an empty one, so a smelter's copper keeps landing in the copper
        // chest even when a nearer chest has a free slot. Then any chest with room.
        public static Container Store(ItemDrop.ItemData item, int stack, Vector3 center, long ownerId)
        {
            if (item == null || item.m_shared == null) return null;

            var candidates = Nearby(center, ownerId);

            foreach (var c in candidates)
                if (Holds(c, item.m_shared.m_name) && Deposit(c, item, stack)) return c;

            foreach (var c in candidates)
                if (Deposit(c, item, stack)) return c;

            return null;
        }

        // Prefab overload, for a product taken straight off a Pickable (the farm
        // harvest) that never exists as a live ItemDrop in the world.
        public static Container Store(GameObject itemPrefab, int amount, Vector3 center, long ownerId)
        {
            if (itemPrefab == null) return null;

            var drop = itemPrefab.GetComponent<ItemDrop>();
            var shared = drop != null && drop.m_itemData != null ? drop.m_itemData.m_shared : null;

            var candidates = Nearby(center, ownerId);

            foreach (var c in candidates)
            {
                if (shared == null || !Holds(c, shared.m_name)) continue;
                if (DepositPrefab(c, itemPrefab, amount)) return c;
            }

            foreach (var c in candidates)
                if (DepositPrefab(c, itemPrefab, amount)) return c;

            return null;
        }

        // Are there any usable chests at all in range? Separates "nowhere to put
        // this" from "everything is full" in the blocker the worker speaks.
        public static bool AnyStorageNearby(Vector3 center, long ownerId)
            => Nearby(center, ownerId).Count > 0;

        // Does this chest already hold this kind of item? Keyed on the shared name,
        // which is what vanilla stacks on — and unlike m_dropPrefab it is always
        // populated, on chest items and world drops alike.
        private static bool Holds(Container c, string sharedName)
        {
            var inv = c != null ? c.GetInventory() : null;
            if (inv == null || string.IsNullOrEmpty(sharedName)) return false;

            foreach (var held in inv.GetAllItems())
                if (held != null && held.m_shared != null && held.m_shared.m_name == sharedName) return true;
            return false;
        }

        private static bool Deposit(Container c, ItemDrop.ItemData item, int stack)
        {
            var inv = c.GetInventory();
            if (inv == null || !inv.CanAddItem(item, stack)) return false;

            ClaimForWrite(c);
            var copy = item.Clone();
            copy.m_stack = stack;
            return inv.AddItem(copy);
        }

        private static bool DepositPrefab(Container c, GameObject itemPrefab, int amount)
        {
            var inv = c.GetInventory();
            if (inv == null || !inv.CanAddItem(itemPrefab, amount)) return false;

            ClaimForWrite(c);
            return inv.AddItem(itemPrefab, amount);
        }
    }
}
