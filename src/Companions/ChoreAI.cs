using System.Collections.Generic;
using UnityEngine;

namespace LostScrollsII.Companions
{
    // Drives a recruited companion's workstation/field chores, per
    // docs/Ally-Chores.md. One component, three modes:
    //   - Smelter:     keep any vanilla Smelter-component station fed (ore+fuel).
    //   - Farm:        plant seeds from a chest AND harvest ripe crops to it, any
    //                  crop type, within a radius.
    //   - FeedAnimals: drop accepted food by hungry tamed animals in a radius.
    // Caste eligibility is gated at assignment time in Plugin via ChoreRules.
    //
    // Detection/distance are fully 3D, so stations/fields/pens stacked above or
    // below are seen and pathed to (the MonsterAI follow target handles the
    // climb). In-memory only: assignment does NOT persist across reload/relog.
    public class ChoreAI : MonoBehaviour
    {
        public enum ChoreKind { None, Smelter, Farm, FeedAnimals, Fermenter, Cooking, Haul }

        // ZDO persistence: the chore survives a relog / zone reload, the same way
        // recruit state does (see CommunionService.RestoreCompanion). We store the
        // chore kind and the target's world POSITION (a Vector3 round-trips through
        // save/load cleanly). We deliberately do NOT store the target's ZDOID:
        // ZDOIDs go through the connection/remap system and don't reliably survive
        // a reload, which is what made an earlier ZDOID-based attempt fail to
        // restore. Stations don't move, so position is a stable key — on load we
        // re-resolve the station by proximity to the saved point.
        public const string ZdoKeyChoreKind = "DE_ChoreKind";
        public const string ZdoKeyChorePos = "DE_ChorePos";

        // How close a re-resolved object must be to the saved position to count as
        // "the same target". Stations are placed precisely; crops/animals roam, so
        // those resolve within the wider work radius.
        private const float RestoreResolveRadius = 3f;

        // Claim registry: which target object (station/field/animal/chest) each
        // active chore worker has claimed. Lets the hover tooltip report who's
        // already on a station, and stops a second companion from claiming the
        // same chore. In-memory only, like the assignment itself.
        private static readonly Dictionary<GameObject, ChoreAI> s_claims = new Dictionary<GameObject, ChoreAI>();

        // The worker currently tending the given target, or null if unclaimed.
        // Prunes the entry if the claimant was destroyed or has since unassigned.
        public static ChoreAI ClaimantOf(GameObject anchor)
        {
            if (anchor == null) return null;
            if (s_claims.TryGetValue(anchor, out var c))
            {
                if (c != null && c.IsAssigned) return c;
                s_claims.Remove(anchor);
            }
            return null;
        }

        // An active FEED chore worker whose pen (work radius) already covers `pos`,
        // or null. Feeding is radius-based (one mage tends a whole pen), so the claim
        // is by RANGE, not by the single hovered animal — this is what lets the hover
        // tooltip say "already working here" on every animal in a tended pen and stops
        // a second mage being assigned to it.
        public static ChoreAI FeederCovering(Vector3 pos)
        {
            foreach (var comp in DvergrCompanion.All)
            {
                if (comp == null) continue;
                var chore = comp.GetComponent<ChoreAI>();
                if (chore != null && chore.IsAssigned && chore._kind == ChoreKind.FeedAnimals
                    && Vector3.Distance(chore.CurrentCenter(), pos) <= WorkRadius)
                    return chore;
            }
            return null;
        }

        // Name to show in the "already working here" tooltip / message — the
        // companion's display name (custom name if set), so renames are reflected.
        public string WorkerName => _companion != null ? _companion.DisplayName
            : (_humanoid != null ? _humanoid.GetHoverName() : "An ally");

        // Bumped from the originals: safe to look farther for the station/chests,
        // and a roomier arrival range tolerates standing a step off an elevated
        // target rather than dead-center on it.
        private const float ArrivalRange = 4.5f;
        private const float ContainerSearchRadius = 8f;
        private const float WorkRadius = 10f; // crop/pen + haul-sweep radius
        private const float FeedInterval = 5f;

        // Blocker notifications: at most one per minute, and only while the owner
        // is within this range (no point talking to an empty field).
        private const float NotifyInterval = 60f;
        private const float NotifyRange = 20f;

        private Humanoid _humanoid;
        private MonsterAI _ai;
        private DvergrCompanion _companion;
        private ZNetView _znv;
        private float _lastSayTime = -999f;

        // Pending chore restore (read from the ZDO in Awake; resolved in Update
        // once the target object's zone has loaded into the scene).
        private bool _pendingRestore;
        private ChoreKind _restoreKind;
        private Vector3 _restorePos;
        private float _restoreTimer;
        private Container _openChest;
        private float _closeChestTime;

        private ChoreKind _kind = ChoreKind.None;
        private Smelter _station;          // Smelter mode
        private Fermenter _fermenter;      // Fermenter mode
        private CookingStation _cooker;    // Cooking mode
        private Container _haulChest;      // Haul mode (destination chest)
        private GameObject _anchorObject;  // Farm/FeedAnimals: hovered crop/animal
        private GameObject _claimedAnchor; // registry key this worker holds
        private float _feedTimer;

        public bool IsAssigned => _kind != ChoreKind.None;

        private void Awake()
        {
            _humanoid = GetComponent<Humanoid>();
            _ai = GetComponent<MonsterAI>();
            _companion = GetComponent<DvergrCompanion>();
            _znv = GetComponent<ZNetView>();

            // Queue a restore if a chore was persisted on this companion's ZDO.
            // The target object may not be loaded yet, so we resolve it lazily in
            // Update (TryRestore) rather than here.
            if (_znv != null && _znv.IsValid())
            {
                int persisted = _znv.GetZDO().GetInt(ZdoKeyChoreKind, 0);
                if (persisted != (int)ChoreKind.None)
                {
                    _restoreKind = (ChoreKind)persisted;
                    _restorePos = _znv.GetZDO().GetVec3(ZdoKeyChorePos, transform.position);
                    _pendingRestore = true;
                }
            }
        }

        // Does this companion's ZDO carry a persisted (unfinished) chore? Used by
        // CommunionService.RestoreCompanion to decide whether to re-add ChoreAI on
        // spawn so the chore resumes after a relog / zone reload.
        public static bool HasPersistedChore(ZNetView znv)
        {
            return znv != null && znv.IsValid()
                && znv.GetZDO().GetInt(ZdoKeyChoreKind, 0) != (int)ChoreKind.None;
        }

        public void AssignToSmelter(Smelter station)
        {
            _kind = ChoreKind.Smelter;
            _station = station;
            BeginChore(station != null ? station.gameObject : null);
        }

        public void AssignToFarm(GameObject cropAnchor)
        {
            _kind = ChoreKind.Farm;
            BeginChore(cropAnchor);
        }

        public void AssignToFeedAnimals(GameObject animalAnchor)
        {
            _kind = ChoreKind.FeedAnimals;
            BeginChore(animalAnchor);
        }

        public void AssignToFermenter(Fermenter fermenter)
        {
            _kind = ChoreKind.Fermenter;
            _fermenter = fermenter;
            BeginChore(fermenter != null ? fermenter.gameObject : null);
        }

        public void AssignToCooking(CookingStation cooker)
        {
            _kind = ChoreKind.Cooking;
            _cooker = cooker;
            BeginChore(cooker != null ? cooker.gameObject : null);
        }

        public void AssignToHaul(Container chest)
        {
            _kind = ChoreKind.Haul;
            _haulChest = chest;
            BeginChore(chest != null ? chest.gameObject : null);
        }

        private void BeginChore(GameObject anchor)
        {
            _anchorObject = anchor;
            _feedTimer = 0f;
            Claim(anchor);
            PersistChore(anchor);
            // Reuses the proven Phase 2 follow mechanism to walk to the target.
            if (_ai != null && anchor != null) _ai.SetFollowTarget(anchor);
            // A working companion is passive (no proactive threat sensing) — it
            // only fights if a player attacks it. See DvergrCompanion.
            if (_companion != null) { _companion.ChoreActive = true; _companion.SetPassive(true); }
        }

        // Write the chore + its target's world position so it survives relog/zone
        // reload (position round-trips cleanly; see the ZdoKeyChorePos note).
        private void PersistChore(GameObject anchor)
        {
            if (_znv == null || !_znv.IsValid()) return;

            var zdo = _znv.GetZDO();
            zdo.Set(ZdoKeyChoreKind, (int)_kind);
            zdo.Set(ZdoKeyChorePos, anchor != null ? anchor.transform.position : transform.position);
        }

        private void ClearPersistedChore()
        {
            if (_znv == null || !_znv.IsValid()) return;
            _znv.GetZDO().Set(ZdoKeyChoreKind, (int)ChoreKind.None);
        }

        // Resolve a persisted chore once its target's zone has loaded, then re-issue
        // the assignment. Retries until the target object is found near its saved
        // position (the zone may still be streaming in on relog); gives up and
        // clears the stale record if it never shows up (e.g. it was removed).
        private void TryRestore()
        {
            if (ZNetScene.instance == null) return;

            var go = FindRestoreTarget(_restoreKind, _restorePos);
            if (go != null)
            {
                _pendingRestore = false;
                ResumeChore(_restoreKind, go);
                return;
            }

            _restoreTimer += Time.deltaTime;
            if (_restoreTimer > 60f) { _pendingRestore = false; ClearPersistedChore(); }
        }

        // Find the chore target near its saved position. Stations resolve within a
        // tight radius; crops/animals (which roam/regrow) within the work radius.
        private GameObject FindRestoreTarget(ChoreKind kind, Vector3 pos)
        {
            float radius = (kind == ChoreKind.Farm || kind == ChoreKind.FeedAnimals)
                ? WorkRadius : RestoreResolveRadius;

            foreach (var hit in Physics.OverlapSphere(pos, radius))
            {
                switch (kind)
                {
                    case ChoreKind.Smelter: { var s = hit.GetComponentInParent<Smelter>(); if (s != null) return s.gameObject; break; }
                    case ChoreKind.Fermenter: { var f = hit.GetComponentInParent<Fermenter>(); if (f != null) return f.gameObject; break; }
                    case ChoreKind.Cooking: { var c = hit.GetComponentInParent<CookingStation>(); if (c != null) return c.gameObject; break; }
                    case ChoreKind.Haul: { var ct = hit.GetComponentInParent<Container>(); if (ct != null) return ct.gameObject; break; }
                    case ChoreKind.Farm:
                    {
                        // Prefer the Cultivator item-stand (the stable field marker),
                        // then a crop, then a sapling — whichever anchored this field.
                        var stand = hit.GetComponentInParent<ItemStand>();
                        if (stand != null && stand.HaveAttachment() && stand.GetAttachedItem() == "Cultivator") return stand.gameObject;
                        var p = hit.GetComponentInParent<Pickable>(); if (p != null) return p.gameObject;
                        var pl = hit.GetComponentInParent<Plant>(); if (pl != null) return pl.gameObject;
                        break;
                    }
                    case ChoreKind.FeedAnimals:
                    {
                        var ch = hit.GetComponentInParent<Character>();
                        if (ch != null && ch.IsTamed() && ch.GetComponent<DvergrCompanion>() == null) return ch.gameObject;
                        break;
                    }
                }
            }
            return null;
        }

        // Map the persisted kind back onto the resolved target object and re-assign.
        private void ResumeChore(ChoreKind kind, GameObject go)
        {
            switch (kind)
            {
                case ChoreKind.Smelter: { var s = go.GetComponentInParent<Smelter>(); if (s != null) AssignToSmelter(s); break; }
                case ChoreKind.Fermenter: { var f = go.GetComponentInParent<Fermenter>(); if (f != null) AssignToFermenter(f); break; }
                case ChoreKind.Cooking: { var c = go.GetComponentInParent<CookingStation>(); if (c != null) AssignToCooking(c); break; }
                case ChoreKind.Haul: { var ct = go.GetComponentInParent<Container>(); if (ct != null) AssignToHaul(ct); break; }
                case ChoreKind.Farm: AssignToFarm(go); break;
                case ChoreKind.FeedAnimals: AssignToFeedAnimals(go); break;
            }

            // Target component vanished even though the GameObject resolved — clear.
            if (!IsAssigned) ClearPersistedChore();
        }

        // Take exclusive claim of a chore target, releasing any prior one.
        private void Claim(GameObject anchor)
        {
            ReleaseClaim();
            if (anchor == null) return;
            s_claims[anchor] = this;
            _claimedAnchor = anchor;
        }

        private void ReleaseClaim()
        {
            if (_claimedAnchor == null) return;
            if (s_claims.TryGetValue(_claimedAnchor, out var c) && c == this) s_claims.Remove(_claimedAnchor);
            _claimedAnchor = null;
        }

        // Companion despawned/destroyed mid-chore — drop the claim so the station
        // frees up immediately instead of waiting for the next ClaimantOf prune.
        private void OnDisable() => ReleaseClaim();

        public void Unassign()
        {
            _kind = ChoreKind.None;
            _station = null;
            _fermenter = null;
            _cooker = null;
            _haulChest = null;
            _anchorObject = null;
            _pendingRestore = false;
            ReleaseClaim();
            ClearPersistedChore();
            ClearSpeech();
            CloseChest();

            if (_companion != null)
            {
                _companion.ChoreActive = false;
                // Return to the owner's side (restores alert range + follow).
                _companion.SetStance(CompanionStance.Follow, _companion.OwnerPlayer()?.gameObject);
            }
            else if (_ai != null)
            {
                _ai.SetFollowTarget(null);
            }
        }

        // Turn the companion to face what it's working on — vanilla SetLookDir,
        // flattened to the horizontal so it doesn't tilt.
        private void FaceToward(Vector3 pos)
        {
            var dir = pos - transform.position;
            dir.y = 0f;
            if (dir.sqrMagnitude > 0.01f && _humanoid != null) _humanoid.SetLookDir(dir.normalized, 0f);
        }

        // Pops a chest's lid open (vanilla SetInUse animation + effects) while the
        // companion is drawing from / depositing into it, then auto-closes shortly
        // after so the player isn't locked out for long.
        private void OpenChest(Container c)
        {
            if (c == null) return;
            if (_openChest != c)
            {
                CloseChest();
                c.SetInUse(true);
                _openChest = c;
            }
            _closeChestTime = Time.time + 1.5f;
        }

        private void CloseChest()
        {
            if (_openChest != null)
            {
                _openChest.SetInUse(false);
                _openChest = null;
            }
        }

        // Where the chore is centered right now. Stations are fixed; Farm/
        // FeedAnimals follow the anchor object (the animal may roam).
        private Vector3 CurrentCenter()
        {
            if (_kind == ChoreKind.Smelter && _station != null) return _station.transform.position;
            if (_kind == ChoreKind.Fermenter && _fermenter != null) return _fermenter.transform.position;
            if (_kind == ChoreKind.Cooking && _cooker != null) return _cooker.transform.position;
            if (_kind == ChoreKind.Haul && _haulChest != null) return _haulChest.transform.position;
            if (_anchorObject != null) return _anchorObject.transform.position;
            return transform.position;
        }

        private void Update()
        {
            // Auto-close a chest the companion popped open, independent of chore ticks.
            if (_openChest != null && Time.time > _closeChestTime) CloseChest();

            // Resume a chore persisted from a previous session, once its target
            // object has loaded. Runs until resolved (or it gives up).
            if (_pendingRestore) { TryRestore(); return; }

            if (_kind == ChoreKind.None || _humanoid == null) return;

            // Only the client/host that OWNS this companion's ZDO drives the chore,
            // so the work happens exactly once. This is also what lets a chore keep
            // running when the assigning player logs out: ZDO ownership migrates to
            // whoever still has the zone loaded (the server / another player), and
            // that owner picks the chore right up. In single-player the local host
            // always owns it, so this is a no-op there.
            if (_znv != null && _znv.IsValid() && !_znv.IsOwner()) return;

            // Target destroyed (station removed, crop harvested by the player,
            // animal died) — end the chore cleanly. The anchor object is the
            // followed transform for every mode, so a null anchor means gone.
            if (_anchorObject == null) { Unassign(); return; }

            _feedTimer += Time.deltaTime;
            if (_feedTimer < FeedInterval) return;
            _feedTimer = 0f;

            if (Vector3.Distance(transform.position, CurrentCenter()) > ArrivalRange)
            {
                Say(_kind == ChoreKind.Farm ? "I can't reach the field!"
                    : _kind == ChoreKind.FeedAnimals ? "I can't reach the pen!"
                    : _kind == ChoreKind.Haul ? "I can't reach the chest!"
                    : "I can't reach my station!");
                return;
            }

            switch (_kind)
            {
                case ChoreKind.Smelter: ServiceStation(); break;
                case ChoreKind.Farm: ServiceFarm(); break;
                case ChoreKind.FeedAnimals: ServiceAnimals(); break;
                case ChoreKind.Fermenter: ServiceFermenter(); break;
                case ChoreKind.Cooking: ServiceCooking(); break;
                case ChoreKind.Haul: ServiceHaul(); break;
            }
        }

        // ---- Smelter ---------------------------------------------------------

        private void ServiceStation()
        {
            bool needsOre = _station.GetQueueSize() < _station.m_maxOre;
            bool needsFuel = _station.m_fuelItem != null && _station.GetFuel() < _station.m_maxFuel;

            if (!needsOre && !needsFuel) { ClearSpeech(); return; }

            var fuelPrefabName = _station.m_fuelItem != null ? _station.m_fuelItem.name : null;
            bool fuelMissing = needsFuel;
            bool oreMissing = needsOre;

            var companionInventory = _humanoid.GetInventory();
            var hits = Physics.OverlapSphere(_station.transform.position, ContainerSearchRadius);
            bool anyContainer = false;

            foreach (var hit in hits)
            {
                var container = hit.GetComponentInParent<Container>();
                var sourceInventory = container != null ? container.GetInventory() : null;
                if (sourceInventory == null) continue;
                anyContainer = true;

                foreach (var item in new List<ItemDrop.ItemData>(sourceInventory.GetAllItems()))
                {
                    var prefabName = item.m_dropPrefab != null ? item.m_dropPrefab.name : null;
                    if (prefabName == null) continue;

                    if (needsFuel && prefabName == fuelPrefabName)
                    {
                        if (sourceInventory.RemoveItem(item, 1))
                        {
                            companionInventory.AddItem(item.Clone());
                            OpenChest(container);
                            FaceToward(_station.transform.position);
                            _station.SetFuel(_station.GetFuel() + 1f);
                            PlayEffects(_station.m_fuelAddedEffects, _station.transform);
                            needsFuel = false;
                            fuelMissing = false;
                        }
                        continue;
                    }

                    if (needsOre && _station.GetItemConversion(prefabName) != null)
                    {
                        if (sourceInventory.RemoveItem(item, 1))
                        {
                            companionInventory.AddItem(item.Clone());
                            OpenChest(container);
                            FaceToward(_station.transform.position);
                            _station.QueueOre(prefabName);
                            PlayEffects(_station.m_oreAddedEffects, _station.transform);
                            needsOre = false;
                            oreMissing = false;
                        }
                        continue;
                    }
                }

                if (!needsOre && !needsFuel) break;
            }

            if (!anyContainer) { Say("I have no chest to draw from!"); return; }

            ReportMissing(oreMissing, fuelMissing);
        }

        // Localized, station-specific input names so the worker names the actual
        // material it needs (Wood / Coal / Copper / Tin / ...) rather than "ore"
        // for everything. Inputs come from the station's own conversion list.
        private string InputNames()
        {
            var set = new HashSet<string>();
            if (_station != null && _station.m_conversion != null)
            {
                foreach (var conv in _station.m_conversion)
                {
                    if (conv != null && conv.m_from != null && conv.m_from.m_itemData != null && conv.m_from.m_itemData.m_shared != null)
                        set.Add(Localization.instance.Localize(conv.m_from.m_itemData.m_shared.m_name));
                }
            }
            return set.Count > 0 ? string.Join(" or ", set) : "materials";
        }

        private string FuelName()
        {
            if (_station != null && _station.m_fuelItem != null && _station.m_fuelItem.m_itemData != null && _station.m_fuelItem.m_itemData.m_shared != null)
                return Localization.instance.Localize(_station.m_fuelItem.m_itemData.m_shared.m_name);
            return "fuel";
        }

        // ---- Farm (plant + harvest, any crop type) --------------------------

        private void ServiceFarm()
        {
            var center = CurrentCenter();
            var chest = FirstContainerInventory(center);

            // 1) Harvest: one ripe crop of ANY type per tick -> deposit to the chest.
            foreach (var hit in Physics.OverlapSphere(center, WorkRadius))
            {
                var p = hit.GetComponentInParent<Pickable>();
                if (p == null || !p.CanBePicked()) continue;

                if (chest == null) { Say("I have no chest for the harvest."); return; }
                var prefab = p.m_itemPrefab;
                int amount = Mathf.Max(1, p.m_amount);
                if (prefab == null) continue;
                if (!chest.CanAddItem(prefab, amount)) { Say("The harvest chest is full!"); return; }

                chest.AddItem(prefab, amount);
                p.SetPicked(true);
                FaceToward(p.transform.position);
                PlayEffects(p.m_pickEffector, null, p.transform.position);
                ClearSpeech();
                return; // one action per tick
            }

            // 2) Nothing ripe -> plant a seed from the chest onto free cultivated
            //    ground. Works for any seed the chest holds (see PlantingCatalog).
            if (chest == null) { Say("I have no chest for the seeds."); return; }

            ItemDrop.ItemData seed = null;
            GameObject sapling = null;
            bool sawWrongBiomeSeed = false;
            foreach (var item in chest.GetAllItems())
            {
                var name = item.m_dropPrefab != null ? item.m_dropPrefab.name : null;
                if (!PlantingCatalog.TryGetSapling(name, out var s)) continue;
                // Only plant crops whose plant allows the biome we're in — mirrors
                // vanilla, which forbids placing a plant where it can't grow (and it
                // would just wither if forced). See CanGrowInBiome.
                if (!CanGrowInBiome(s.GetComponent<Plant>(), center)) { sawWrongBiomeSeed = true; continue; }
                seed = item; sapling = s; break;
            }
            if (sapling == null)
            {
                Say(sawWrongBiomeSeed ? "These seeds won't grow in this land." : "No crops are ready, and no seeds to plant.");
                return;
            }

            if (!TryFindPlantSpot(center, sapling, out var spot)) { Say("There's no room left to plant."); return; }

            if (chest.RemoveItem(seed, 1))
            {
                Object.Instantiate(sapling, spot, Quaternion.Euler(0f, Random.Range(0f, 360f), 0f));
                var piece = sapling.GetComponent<Piece>();
                if (piece != null) PlayEffects(piece.m_placeEffect, null, spot);
                FaceToward(spot);
                Plugin.Log.LogInfo($"[farm] planted '{sapling.name}' at {spot} (biome {CurrentBiome(spot)}).");
                ClearSpeech();
            }
        }

        // Find a free, cultivated spot to plant within the work radius: sample a few
        // random points, snap Y to the terrain, require cultivated ground, and keep
        // clear of existing plants/unharvested crops by the sapling's grow radius.
        private bool TryFindPlantSpot(Vector3 center, GameObject sapling, out Vector3 spot)
        {
            spot = center;
            var plant = sapling.GetComponent<Plant>();
            float grow = plant != null ? Mathf.Max(plant.m_growRadius, 0.5f) : 1f;
            var zs = ZoneSystem.instance;

            for (int i = 0; i < 24; i++)
            {
                Vector2 r = Random.insideUnitCircle * (WorkRadius * 0.85f);
                var p = new Vector3(center.x + r.x, center.y, center.z + r.y);
                if (zs != null) p.y = zs.GetGroundHeight(p);

                var hm = Heightmap.FindHeightmap(p);
                if (hm == null || !hm.IsCultivated(p)) continue;
                if (!CanGrowInBiome(plant, p)) continue; // biome may vary across the radius
                if (!IsSpotClear(p, grow)) continue;

                spot = p;
                return true;
            }
            return false;
        }

        // A spot is plantable if no growing plant and no still-unharvested crop sits
        // within `radius`. (A picked/empty crop object may linger invisibly on the
        // tile — that's fine to plant over.)
        private static bool IsSpotClear(Vector3 p, float radius)
        {
            foreach (var hit in Physics.OverlapSphere(p, radius))
            {
                if (hit.GetComponentInParent<Plant>() != null) return false;
                var pick = hit.GetComponentInParent<Pickable>();
                if (pick != null && !pick.GetPicked()) return false;
            }
            return true;
        }

        // A crop may only be planted where its plant is allowed to grow. Plant.m_biome
        // is a Heightmap.Biome flags mask; AND it with the biome at the target position
        // (WorldGenerator is vanilla's authoritative source for placement). Fail-open
        // only if the world generator isn't up yet.
        private static bool CanGrowInBiome(Plant plant, Vector3 pos)
        {
            if (plant == null) return true;
            if (WorldGenerator.instance == null) return true;
            var biome = WorldGenerator.instance.GetBiome(pos);
            return ((int)(plant.m_biome & biome)) != 0;
        }

        private static Heightmap.Biome CurrentBiome(Vector3 pos)
        {
            return WorldGenerator.instance != null ? WorldGenerator.instance.GetBiome(pos) : Heightmap.Biome.None;
        }

        // ---- Feed tamed animals ---------------------------------------------

        private void ServiceAnimals()
        {
            var center = CurrentCenter();

            // Collect hungry tamed animals in radius.
            var hungry = new List<Character>();
            foreach (var hit in Physics.OverlapSphere(center, WorkRadius))
            {
                var c = hit.GetComponentInParent<Character>();
                if (c == null || !c.IsTamed()) continue;
                if (c.GetComponent<DvergrCompanion>() != null) continue; // not our own ally
                var tame = c.GetComponent<Tameable>();
                if (tame != null && tame.IsHungry() && !hungry.Contains(c)) hungry.Add(c);
            }

            if (hungry.Count == 0) { Say("The animals aren't hungry."); return; }

            var chest = FirstContainerInventory(center);
            if (chest == null) { Say("I have no food chest nearby."); return; }

            foreach (var animal in hungry)
            {
                var mai = animal.GetComponent<MonsterAI>();
                if (mai == null || mai.m_consumeItems == null) continue;
                if (ItemAlreadyDroppedNear(animal.transform.position)) continue; // wait for it to eat

                var food = FindAcceptedFood(chest, mai.m_consumeItems);
                if (food == null) { Say("I have no food to give."); return; }

                if (chest.RemoveItem(food, 1))
                {
                    var data = food.Clone();
                    data.m_stack = 1;
                    // Drop it at the animal's feet; vanilla MonsterAI auto-eats it.
                    ItemDrop.DropItem(data, 1, animal.transform.position + Vector3.up * 0.3f, Quaternion.identity);
                    ClearSpeech();
                    return; // one feed per tick
                }
            }

            ClearSpeech();
        }

        private static ItemDrop.ItemData FindAcceptedFood(Inventory chest, List<ItemDrop> consumeItems)
        {
            foreach (var item in chest.GetAllItems())
            {
                var name = item.m_dropPrefab != null ? item.m_dropPrefab.name : null;
                if (name == null) continue;
                foreach (var ci in consumeItems)
                {
                    if (ci != null && ci.name == name) return item;
                }
            }
            return null;
        }

        // Avoids piling up food: if any item is already on the ground by the
        // animal, wait for it to be eaten before dropping more.
        private static bool ItemAlreadyDroppedNear(Vector3 pos)
        {
            foreach (var hit in Physics.OverlapSphere(pos, 2f))
            {
                if (hit.GetComponentInParent<ItemDrop>() != null) return true;
            }
            return false;
        }

        // ---- Provisioning: Fermenter (Support Mage) -------------------------

        private void ServiceFermenter()
        {
            switch (_fermenter.GetStatus())
            {
                case Fermenter.Status.Ready:
                    // Tap — vanilla drops the finished meads by the fermenter.
                    _fermenter.Interact(_humanoid, false, false);
                    ClearSpeech();
                    return;
                case Fermenter.Status.Fermenting:
                    ClearSpeech(); // brewing in progress, nothing to do
                    return;
                case Fermenter.Status.Exposed:
                    Say("The brew is exposed to the sky!");
                    return;
            }

            // Empty: load a fermentable base from a chest.
            var chest = FirstContainerInventory(CurrentCenter());
            if (chest == null) { Say("I have no chest to brew from!"); return; }

            foreach (var item in new List<ItemDrop.ItemData>(chest.GetAllItems()))
            {
                var name = item.m_dropPrefab != null ? item.m_dropPrefab.name : null;
                if (name == null || !_fermenter.IsItemAllowed(name)) continue;

                // Mirror the smelter pattern: stage the item in the companion's
                // own inventory, then hand it to the station's add method.
                if (chest.RemoveItem(item, 1))
                {
                    var moved = item.Clone();
                    moved.m_stack = 1;
                    _humanoid.GetInventory().AddItem(moved);
                    if (_fermenter.AddItem(_humanoid, moved)) { ClearSpeech(); return; }
                }
            }

            Say("I have nothing to brew!");
        }

        // ---- Provisioning: Cooking Station (Support Mage) -------------------

        private void ServiceCooking()
        {
            // 1) Pull any done item off first, so cooked food doesn't burn.
            //    Use OnInteract, NOT Interact: for stations with an "add food"
            //    switch (e.g. the Stone Oven) Interact() early-outs to the switch
            //    and never collects, so the food just sits and burns. OnInteract()
            //    is the actual worker (it's what Interact calls on switch-less
            //    stations, and what the oven's food switch ultimately invokes) and
            //    fires the RPC_RemoveDoneItem that spawns the finished food.
            if (_cooker.HaveDoneItem())
            {
                _cooker.OnInteract(_humanoid);
                ClearSpeech();
                return;
            }

            // 2) Fuel (only stations that use it, e.g. the iron cooking station).
            if (_cooker.m_fuelItem != null && _cooker.GetFuel() < _cooker.m_maxFuel)
            {
                var fuelChest = FirstContainerInventory(CurrentCenter());
                var fuelName = _cooker.m_fuelItem.name;
                if (fuelChest != null)
                {
                    foreach (var item in new List<ItemDrop.ItemData>(fuelChest.GetAllItems()))
                    {
                        if ((item.m_dropPrefab != null ? item.m_dropPrefab.name : null) != fuelName) continue;
                        if (fuelChest.RemoveItem(item, 1))
                        {
                            _humanoid.GetInventory().AddItem(item.Clone());
                            _cooker.SetFuel(_cooker.GetFuel() + 1f);
                            ClearSpeech();
                            return;
                        }
                    }
                }
            }

            // 3) Add raw food to a free slot. Only stations that actually require a
            //    fire (campfire-style) are fire-gated — calling IsFireLit() on one
            //    that doesn't (e.g. the Stone Oven, m_requireFire=false) NREs,
            //    because its fire-check points are left unconfigured. So we mirror
            //    vanilla's own guard and skip the check when no fire is required.
            if (_cooker.m_requireFire && !_cooker.IsFireLit()) { Say("The cooking fire is out!"); return; }
            if (_cooker.GetFreeSlot() < 0) { ClearSpeech(); return; } // all slots cooking

            var chest = FirstContainerInventory(CurrentCenter());
            if (chest == null) { Say("I have no chest to cook from!"); return; }

            foreach (var item in new List<ItemDrop.ItemData>(chest.GetAllItems()))
            {
                var name = item.m_dropPrefab != null ? item.m_dropPrefab.name : null;
                if (name == null || _cooker.GetItemConversion(name) == null) continue;

                if (chest.RemoveItem(item, 1))
                {
                    var moved = item.Clone();
                    moved.m_stack = 1;
                    _humanoid.GetInventory().AddItem(moved);
                    if (_cooker.CookItem(_humanoid, moved)) { ClearSpeech(); return; }
                }
            }

            Say("I have nothing to cook!");
        }

        // ---- Hauling: sweep loose dropped items within radius into the chest
        //      (Rogue). The companion stays put at its post by the chest — it does
        //      NOT walk out to each item (reverted at the user's request); it just
        //      pulls everything in range straight in. Still pops the chest lid open
        //      on each deposit, like every other chore interaction.

        private void ServiceHaul()
        {
            var chestInv = _haulChest != null ? _haulChest.GetInventory() : null;
            if (chestInv == null) return;

            foreach (var hit in Physics.OverlapSphere(CurrentCenter(), WorkRadius))
            {
                var drop = hit.GetComponentInParent<ItemDrop>();
                if (drop == null || !drop.CanPickup(false)) continue;

                var nview = drop.GetComponent<ZNetView>();
                if (nview == null || !nview.IsValid()) continue;

                var data = drop.m_itemData;
                if (data == null) continue;

                if (!chestInv.CanAddItem(data, data.m_stack)) { Say("The haul chest is full!"); return; }

                chestInv.AddItem(data.Clone());
                OpenChest(_haulChest);
                FaceToward(_haulChest.transform.position);
                // Take ownership before removing the networked world object.
                nview.ClaimOwnership();
                ZNetScene.instance.Destroy(drop.gameObject);
                ClearSpeech();
                return; // one item per tick
            }

            // Nothing on the ground to haul is idle, not a blocker — stay quiet.
            ClearSpeech();
        }

        // ---- Shared helpers --------------------------------------------------

        private Inventory FirstContainerInventory(Vector3 center)
        {
            foreach (var hit in Physics.OverlapSphere(center, ContainerSearchRadius))
            {
                var container = hit.GetComponentInParent<Container>();
                var inv = container != null ? container.GetInventory() : null;
                if (inv != null) return inv;
            }
            return null;
        }

        // Plays a station/pickable's own vanilla effect list — same VFX/SFX you'd
        // see doing it by hand. Vanilla-assets-only (reuses existing effects).
        private void PlayEffects(EffectList effects, Transform parent, Vector3? at = null)
        {
            if (effects == null) return;
            var pos = at ?? (parent != null ? parent.position : transform.position);
            effects.Create(pos, Quaternion.identity, parent, 1f, -1);
        }

        private void ReportMissing(bool oreMissing, bool fuelMissing)
        {
            string text =
                oreMissing && fuelMissing ? $"I need more {InputNames()} and {FuelName()}!" :
                fuelMissing ? $"I need more {FuelName()}!" :
                oreMissing ? $"I need more {InputNames()}!" :
                null;

            if (text == null) { ClearSpeech(); return; }
            Say(text);
        }

        // NPC speech bubble (same system the trader/Hildir use), but throttled to
        // at most once per minute and only while the owner is nearby — no point
        // narrating a blocker to an empty field.
        private void Say(string text)
        {
            if (Chat.instance == null) return;
            if (_companion == null || !_companion.IsOwnerNear(NotifyRange)) return;
            if (Time.time - _lastSayTime < NotifyInterval) return;

            _lastSayTime = Time.time;
            Chat.instance.SetNpcText(gameObject, Vector3.up * 2.2f, 20f, 8f, string.Empty, text, false);
        }

        private void ClearSpeech()
        {
            if (Chat.instance != null) Chat.instance.ClearNpcText(gameObject);
        }
    }
}
