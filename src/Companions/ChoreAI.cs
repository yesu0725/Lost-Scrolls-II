using System.Collections.Generic;
using UnityEngine;

namespace LostScrollsII.Companions
{
    // Drives a recruited companion's workstation/field chores, per
    // docs/Ally-Chores.md. Four domains:
    //   - Smelter:      every Smelter-component station in radius (ore + fuel).
    //   - Provisioning: every cooking station AND fermenter in radius.
    //   - Farm:         plant seeds from a chest AND harvest ripe crops to it, any
    //                   crop type, within a radius.
    //   - Husbandry /
    //     Haul:         one Rogue domain — feed the herd, cull its surplus, AND
    //                   clear every loose item in the patch into a chest.
    // Caste eligibility is gated at assignment time in Plugin via ChoreRules.
    //
    // A CHORE IS A PATCH OF GROUND, NOT ONE STATION. You assign a worker by
    // hovering something, and the position of that thing becomes its POST; from
    // then on it tends everything of its kind within WorkRadius of the post,
    // walking between jobs. One Fire Mage keeps a whole row of smelters, kilns and
    // blast furnaces going; one Support Mage runs the entire kitchen. This is also
    // why the post is a POSITION rather than the object you hovered: the crop gets
    // harvested, the animal gets culled, one furnace of six gets torn down — none
    // of which should end the chore. It ends when the player recalls the worker, or
    // when nothing of its kind has existed in radius for IdleGiveUpSeconds.
    //
    // Detection/distance are fully 3D, so stations/fields/pens stacked above or
    // below are seen and pathed to (the MonsterAI follow target handles the climb).
    public class ChoreAI : MonoBehaviour
    {
        // Chore DOMAINS. Values are PINNED because they are persisted on the
        // companion's ZDO. The pre-0.11 layout had Fermenter = 4 and Cooking = 5 as
        // separate chores; those are folded into Provisioning, and LegacyKind()
        // maps an old record onto the new set. Never renumber these.
        //
        // Husbandry and Haul are ONE DOMAIN wearing two ids (see IsRogueDomain).
        // A Rogue assigned at either does both jobs across the same patch — it
        // culls and feeds the herd AND clears the ground into chests — because
        // both are the same errand from the player's side and neither fills a
        // 5-second tick on its own. The two ids survive only so that the thing you
        // pointed at decides what the ally says it is doing, and so a chore saved
        // before they merged still restores.
        public enum ChoreKind
        {
            None = 0,
            Smelter = 1,
            Farm = 2,
            Husbandry = 3,      // was FeedAnimals — now feeding AND culling (Rogue)
            Haul = 6,           // assigned at a chest; same Rogue domain as Husbandry
            Provisioning = 7,   // cooking stations and fermenters together
        }

        // Which KIND of provisioning station a worker was posted at.
        //
        // Provisioning is one domain but three different jobs, and mixing them is a
        // real cost: a cook that wanders from the cookfires to the stone oven and
        // back is out of position when a rack finishes, and food burns while it is
        // across the room loading a fermenter. So a mage keeps to the kind of
        // station you assigned it at, and you put a second one on the others.
        //
        // The split is taken from vanilla's own `CookingStation.m_requireFire`
        // rather than a list of prefab names: the wood and iron cooking stations
        // cook over a fire, the stone oven is its own heat source and leaves its
        // fire-check points unconfigured (the same flag the Stone Oven NRE fix
        // turned on years' worth of station handling).
        public enum ChoreVariant
        {
            Any = 0,
            Cookfire = 1,    // cooking stations that need a fire under them
            Oven = 2,        // the stone oven and anything else self-heating
            Fermenter = 3,
        }

        public static ChoreVariant VariantFor(GameObject go)
        {
            if (go == null) return ChoreVariant.Any;

            if (go.GetComponentInParent<Fermenter>() != null) return ChoreVariant.Fermenter;

            var cooker = go.GetComponentInParent<CookingStation>();
            if (cooker != null) return cooker.m_requireFire ? ChoreVariant.Cookfire : ChoreVariant.Oven;

            return ChoreVariant.Any;
        }

        // Does this worker's posting cover that station? `Any` is the legacy answer
        // (a chore saved before the split) and deliberately matches everything, so
        // an existing provisioning ally keeps working rather than standing down.
        private bool CoversVariant(GameObject go)
            => _variant == ChoreVariant.Any || VariantFor(go) == _variant;

        // The Rogue's domain: assigned at an animal or at a chest, same work either
        // way. Everything that asks "is this the same chore" has to go through here
        // rather than comparing ChoreKind directly, or a Rogue posted at a chest
        // would not be seen to cover the pen it is standing in.
        public static bool IsRogueDomain(ChoreKind k)
            => k == ChoreKind.Husbandry || k == ChoreKind.Haul;

        private static bool SameDomain(ChoreKind a, ChoreKind b)
            => a == b || (IsRogueDomain(a) && IsRogueDomain(b));

        // ZDO persistence: the chore survives a relog / zone reload, the same way
        // recruit state does (see CommunionService.RestoreCompanion). We store the
        // chore kind and the POST's world position (a Vector3 round-trips through
        // save/load cleanly). We deliberately do NOT store a ZDOID: ZDOIDs go
        // through the connection/remap system and don't reliably survive a reload,
        // which is what made an earlier ZDOID-based attempt fail to restore.
        public const string ZdoKeyChoreKind = "DE_ChoreKind";
        public const string ZdoKeyChorePos = "DE_ChorePos";
        public const string ZdoKeyChoreVariant = "DE_ChoreVariant";

        // How close a re-resolved object must be to the saved post to prove the
        // workplace is still there. Stations are placed precisely; crops/animals
        // roam, so those resolve within the wider work radius.
        private const float RestoreResolveRadius = 3f;

        // Loose items one worker has already taken this tick, so two allies sharing
        // a patch can't both bank the same drop. ZNetScene.Destroy defers the actual
        // destruction to the end of the frame, so without this the second worker's
        // OverlapSphere still returns a drop the first has already put in a chest
        // — and the item is duplicated. Short-lived; entries expire on their own.
        private static readonly Dictionary<ItemDrop, float> s_takenDrops = new Dictionary<ItemDrop, float>();
        private const float TakenDropMemory = 2f;

        private static bool ClaimDrop(ItemDrop drop)
        {
            if (drop == null) return false;

            // Prune opportunistically; this dictionary only ever holds a few frames'
            // worth of entries.
            if (s_takenDrops.Count > 32)
            {
                var stale = new List<ItemDrop>();
                foreach (var kv in s_takenDrops)
                    if (kv.Key == null || Time.time - kv.Value > TakenDropMemory) stale.Add(kv.Key);
                foreach (var k in stale) s_takenDrops.Remove(k);
            }

            if (s_takenDrops.TryGetValue(drop, out var when) && Time.time - when <= TakenDropMemory)
                return false;

            s_takenDrops[drop] = Time.time;
            return true;
        }

        // Claim registry: which target object each active chore worker was assigned
        // at. Coverage is really decided by radius now (see WorkerCovering), but the
        // exact-anchor entry is what lets pressing the key on the very thing you
        // assigned toggle the chore off. In-memory only, like the assignment itself.
        private static readonly Dictionary<GameObject, ChoreAI> s_claims = new Dictionary<GameObject, ChoreAI>();

        // The worker assigned at exactly this object, or null if unclaimed.
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

        // Which chore domain would this object be worked by? Used to decide whether
        // an existing worker's patch already covers it.
        public static ChoreKind KindFor(GameObject go)
        {
            if (go == null) return ChoreKind.None;
            if (go.GetComponentInParent<Smelter>() != null) return ChoreKind.Smelter;
            if (go.GetComponentInParent<CookingStation>() != null) return ChoreKind.Provisioning;
            if (go.GetComponentInParent<Fermenter>() != null) return ChoreKind.Provisioning;

            var ch = go.GetComponentInParent<Character>();
            if (ch != null && ch.IsTamed() && ch.GetComponent<DvergrCompanion>() == null) return ChoreKind.Husbandry;

            if (go.GetComponentInParent<Pickable>() != null) return ChoreKind.Farm;
            if (go.GetComponentInParent<Plant>() != null) return ChoreKind.Farm;

            var stand = go.GetComponentInParent<ItemStand>();
            if (stand != null && stand.HaveAttachment() && stand.GetAttachedItem() == "Cultivator") return ChoreKind.Farm;

            // A chest posts a Rogue to clear the ground around it. Structural test
            // only: the Obliterator, a gravestone and a ship's hold are Containers
            // too, and none of them is somewhere to send a worker.
            var container = go.GetComponentInParent<Container>();
            if (container != null && ChoreStorage.IsStoragePiece(container)) return ChoreKind.Haul;

            return ChoreKind.None;
        }

        // Every active worker of the matching domain whose patch covers this object.
        //
        // A patch is no longer exclusive: several companions may share one chore and
        // work it together, so this REPORTS who is on a job rather than gating it.
        // (It used to refuse a second worker; that was the reason a big workshop
        // could only ever have one ally in it.) Ordered nearest-post first so the
        // tooltip names whoever is most obviously "the one working here".
        public static List<ChoreAI> WorkersCovering(GameObject target)
        {
            var found = new List<ChoreAI>();
            if (target == null) return found;

            var kind = KindFor(target);
            if (kind == ChoreKind.None) return found;

            var pos = target.transform.position;
            foreach (var comp in DvergrCompanion.All)
            {
                if (comp == null) continue;
                var chore = comp.GetComponent<ChoreAI>();
                if (chore == null || !chore.IsAssigned || !SameDomain(chore._kind, kind)) continue;

                // Provisioning is three jobs sharing one domain, so a mage on the
                // ovens is not "working here" as far as a fermenter is concerned.
                if (kind == ChoreKind.Provisioning && !chore.CoversVariant(target)) continue;

                if (Vector3.Distance(chore._post, pos) <= WorkRadius) found.Add(chore);
            }

            found.Sort((a, b) =>
                (a._post - pos).sqrMagnitude.CompareTo((b._post - pos).sqrMagnitude));
            return found;
        }

        public static ChoreAI WorkerCovering(GameObject target)
        {
            var all = WorkersCovering(target);
            return all.Count > 0 ? all[0] : null;
        }

        // Name to show in the "already working here" tooltip / message — the
        // companion's display name (custom name if set), so renames are reflected.
        public string WorkerName => _companion != null ? _companion.DisplayName
            : (_humanoid != null ? _humanoid.GetHoverName() : "An ally");

        // How close a worker must be to a station before it may work it.
        //
        // There is a HARD FLOOR under this: BaseAI.Follow stops moving at 3 m from
        // its target, so any value at or under 3 is one the follow logic can never
        // close, and the ally would loop until it declared the station unreachable.
        // The default sits just above that floor so the companion is visibly AT the
        // station rather than reaching for it from across the room; the config is
        // clamped so it can be tuned but not broken.
        private const float DefaultStationReach = 3.4f;
        private const float MinStationReach = 3.2f;

        public static float ArrivalRange => Mathf.Max(
            MinStationReach,
            Plugin.ChoreStationReach != null ? Plugin.ChoreStationReach.Value : DefaultStationReach);

        private const float FeedInterval = 5f;

        // Tick used while a cooking station is holding finished food.
        private const float UrgentInterval = 1f;

        // How wide a patch one worker tends, when the config hasn't been bound yet.
        private const float DefaultWorkRadius = 20f;

        public static float WorkRadius =>
            Plugin.ChoreWorkRadius != null ? Plugin.ChoreWorkRadius.Value : DefaultWorkRadius;

        // A station that couldn't be served (no ore in any chest, brew exposed to
        // the sky) is set aside for this long so one stuck furnace can't starve the
        // other five. It is re-tried after the wait, and the blocker is spoken.
        private const float BlockedRetrySeconds = 60f;

        // Consecutive service ticks spent walking to the same job before the worker
        // decides it simply cannot get there, says so, and sets that job aside.
        private const int UnreachableTicks = 6;

        // How long the patch must be EMPTY of anything this chore could ever work
        // — every station torn down, every animal gone — before the chore ends
        // itself. Not "nothing to do right now": an idle field with nothing ripe is
        // a working field.
        private const float IdleGiveUpSeconds = 60f;

        // How many finished products a worker files away in one tick. More than one
        // because a station can hand back a burst (a smelter empties its whole queue
        // at once), few enough that a busy forge doesn't turn into an inventory
        // stampede. The chore's own work still runs in the same tick.
        private const int MaxDepositsPerTick = 4;

        // Culling (Husbandry). The worker must be this close to strike — it is a
        // melee cull by construction, never a spell at range. Drops from a cull are
        // collected within CullDropRadius of the kill for CullDropSeconds, which is
        // what lets meat be stored even for a species that eats meat (see
        // StoreProducts).
        //
        // 4 m, not the 2 m a swing actually covers, because BaseAI.Follow STOPS AT
        // 3 m: a shorter cull range is one the follow logic will never close, and
        // the worker would circle its quarry until it declared the animal
        // unreachable. Same reason ArrivalRange is roomy.
        private const float CullRange = 4f;
        private const float CullDropRadius = 4f;
        private const float CullDropSeconds = 60f;
        private const int DefaultCullLimit = 3;

        public static int CullLimit =>
            Plugin.HusbandryCullLimit != null ? Plugin.HusbandryCullLimit.Value : DefaultCullLimit;

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
        private ChoreVariant _restoreVariant;
        private Vector3 _restorePos;
        private float _restoreTimer;
        private Container _openChest;
        private float _closeChestTime;

        private ChoreKind _kind = ChoreKind.None;
        private ChoreVariant _variant = ChoreVariant.Any;

        // The patch this worker tends: a world position, plus the object it was
        // assigned at (kept only for the exact-anchor claim / toggle-off).
        private Vector3 _post;
        private GameObject _anchorObject;
        private GameObject _claimedAnchor;

        // The single job being served THIS tick — transient, re-picked every tick.
        private Smelter _station;
        private Fermenter _fermenter;
        private CookingStation _cooker;

        private float _feedTimer;

        // Jobs set aside for a while, and the walk-progress counter behind the
        // "I can't reach it" verdict.
        private readonly Dictionary<GameObject, float> _blockedUntil = new Dictionary<GameObject, float>();
        private GameObject _walkTarget;
        private int _walkTicks;
        private int _postTicks;
        private float _idleSince = -1f;

        // Where a cull just happened, so its drops can be stored even when they are
        // also the herd's feed.
        private readonly List<KeyValuePair<Vector3, float>> _cullDrops = new List<KeyValuePair<Vector3, float>>();

        public bool IsAssigned => _kind != ChoreKind.None;

        // The prefab name of the tool that makes a companion a farmer. The farm
        // chore is the one chore with no station to point at — a field is just
        // ground — so instead of hovering a crop, you hand the ally a Cultivator and
        // tell it to get on with it where it stands. That also means the tool is
        // visible: you can see which of your allies is a farmer by looking in its
        // bag, and taking the Cultivator back is a second way to retire it.
        public const string CultivatorItem = "Cultivator";

        public static bool CarriesCultivator(DvergrCompanion companion)
        {
            if (companion == null) return false;
            var pack = companion.GetComponent<CompanionInventory>();
            return pack != null
                && pack.FindItem(i => i.m_dropPrefab != null && i.m_dropPrefab.name == CultivatorItem) != null;
        }

        // The player this worker acts for. Chest access (personal chests) is judged
        // against the owner, not against whoever happens to be running the chore.
        private long OwnerId => _companion != null ? _companion.OwnerId : 0L;

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
                var persisted = LegacyKind(_znv.GetZDO().GetInt(ZdoKeyChoreKind, 0));
                if (persisted != ChoreKind.None)
                {
                    _restoreKind = persisted;
                    _restoreVariant = (ChoreVariant)_znv.GetZDO().GetInt(ZdoKeyChoreVariant, (int)ChoreVariant.Any);
                    _restorePos = _znv.GetZDO().GetVec3(ZdoKeyChorePos, transform.position);
                    _pendingRestore = true;
                }
            }
        }

        // Translate a persisted chore id written before the domains were merged:
        // Fermenter (4) and Cooking (5) are one Provisioning chore now. Haul (6)
        // still means what it always did, so an old haul chore resumes as itself.
        private static ChoreKind LegacyKind(int persisted)
        {
            switch (persisted)
            {
                case 4:
                case 5: return ChoreKind.Provisioning;
                default: return (ChoreKind)persisted;
            }
        }

        // Does this companion's ZDO carry a persisted (unfinished) chore? Used by
        // CommunionService.RestoreCompanion to decide whether to re-add ChoreAI on
        // spawn so the chore resumes after a relog / zone reload, and by
        // DvergrCompanion.Awake to mark the ally as working from frame one.
        public static bool HasPersistedChore(ZNetView znv)
        {
            return znv != null && znv.IsValid()
                && LegacyKind(znv.GetZDO().GetInt(ZdoKeyChoreKind, 0)) != ChoreKind.None;
        }

        // ---- Assignment ------------------------------------------------------

        public void AssignToStations(Smelter station)
        {
            _kind = ChoreKind.Smelter;
            BeginChore(station != null ? station.gameObject : null);
        }

        public void AssignToProvisioning(GameObject stationObject)
        {
            _kind = ChoreKind.Provisioning;
            BeginChore(stationObject);
        }

        // What the ally will say it is starting, and what the player has to know to
        // staff the rest of the kitchen.
        public string ProvisioningLabel()
        {
            switch (_variant)
            {
                case ChoreVariant.Fermenter: return "Ally tends the brew.";
                case ChoreVariant.Oven:      return "Ally tends the oven.";
                case ChoreVariant.Cookfire:  return "Ally tends the cookfires.";
                default:                     return "Ally tends the kitchen.";
            }
        }

        // Farming posts the worker where it is STANDING: a field is ground, not a
        // station, so there is no anchor object to follow. Passing null is
        // deliberate — BeginChore then takes the companion's own position as the
        // post and skips SetFollowTarget, which would otherwise have the ally
        // following itself.
        public void AssignToFarmHere()
        {
            _kind = ChoreKind.Farm;
            BeginChore(null);
        }

        public void AssignToHusbandry(GameObject animalAnchor)
        {
            _kind = ChoreKind.Husbandry;
            BeginChore(animalAnchor);
        }

        public void AssignToHaul(GameObject chestAnchor)
        {
            _kind = ChoreKind.Haul;
            BeginChore(chestAnchor);
        }

        private void BeginChore(GameObject anchor)
        {
            _anchorObject = anchor;
            _post = anchor != null ? anchor.transform.position : transform.position;
            _variant = _kind == ChoreKind.Provisioning ? VariantFor(anchor) : ChoreVariant.Any;
            _feedTimer = 0f;
            _idleSince = -1f;
            _walkTarget = null;
            _walkTicks = 0;
            _postTicks = 0;
            _blockedUntil.Clear();
            _cullDrops.Clear();

            Claim(anchor);
            PersistChore();

            // TAKING A CHORE ENDS THE STANCE THE ALLY WAS IN. Setting ChoreActive
            // alone is not enough: MonsterAI keeps whatever follow target the stance
            // gave it, so a Follow companion put to work carried on trailing its
            // master. That went unnoticed while every chore was assigned AT an
            // object, because BeginChore then overwrote the follow target with the
            // station — farming, which is posted on open ground with no anchor, is
            // where it finally showed. Clearing first (and patrolling the post)
            // makes it true for every chore rather than as a side effect of one.
            if (_ai != null)
            {
                _ai.SetFollowTarget(null);
                _ai.SetTarget(null);
                _ai.SetPatrolPoint(_post);

                // Reuses the proven Phase 2 follow mechanism to walk to the post.
                if (anchor != null) _ai.SetFollowTarget(anchor);
            }

            // A working companion is passive (no proactive threat sensing) — it
            // only fights if something attacks it. See DvergrCompanion.
            if (_companion != null) { _companion.ChoreActive = true; _companion.SetPassive(true); }
        }

        // Write the chore + its post so it survives relog/zone reload.
        private void PersistChore()
        {
            if (_znv == null || !_znv.IsValid()) return;

            var zdo = _znv.GetZDO();
            zdo.Set(ZdoKeyChoreKind, (int)_kind);
            zdo.Set(ZdoKeyChoreVariant, (int)_variant);
            zdo.Set(ZdoKeyChorePos, _post);
        }

        private void ClearPersistedChore()
        {
            if (_znv == null || !_znv.IsValid()) return;
            _znv.GetZDO().Set(ZdoKeyChoreKind, (int)ChoreKind.None);
        }

        // Resolve a persisted chore once its workplace has loaded, then re-issue
        // the assignment. Retries until something of the right kind is found near
        // the saved post (the zone may still be streaming in on relog); gives up
        // and clears the stale record if nothing ever shows up.
        private void TryRestore()
        {
            if (ZNetScene.instance == null) return;

            // A FIELD can be bare. Since farming became a chore you give an ally
            // rather than a crop you point at, a farm post may have nothing growing
            // on it at all — the whole bed may be freshly harvested. Cultivated
            // ground under the saved post is proof enough that the workplace is
            // still there, and Heightmap.FindHeightmap returning non-null is itself
            // the "has the zone streamed in yet" gate this method is waiting on.
            if (_restoreKind == ChoreKind.Farm)
            {
                var hm = Heightmap.FindHeightmap(_restorePos);
                if (hm != null && hm.IsCultivated(_restorePos))
                {
                    _pendingRestore = false;
                    ResumeChore(ChoreKind.Farm, null);
                    return;
                }
            }

            var go = FindRestoreTarget(_restoreKind, _restorePos);
            if (go != null)
            {
                _pendingRestore = false;
                ResumeChore(_restoreKind, go);
                return;
            }

            _restoreTimer += Time.deltaTime;
            if (_restoreTimer > 60f)
            {
                // Gave up — the workplace never loaded (removed, or its zone never
                // streamed in). Go through the full Unassign rather than just wiping
                // the record: DvergrCompanion.Awake marks a companion carrying a
                // persisted chore as working before this resolves, so dropping the
                // record alone would leave it passive and postless forever.
                //
                // Falls back to STANDBY, not Follow. Nobody ordered this ally off
                // its chore — the work simply stopped existing, most likely while
                // its owner was nowhere near — so it holds the ground it was left
                // on instead of setting off across the map to find them. A recall
                // the player actually issued still returns it to their side.
                _pendingRestore = false;
                if (_znv == null || !_znv.IsValid() || _znv.IsOwner()) Unassign(CompanionStance.Standby);
                else if (_companion != null) _companion.ChoreActive = false;
            }
        }

        // Find something of the chore's kind near the saved post, to prove the
        // workplace still exists. Stations resolve within a tight radius; crops and
        // animals (which roam/regrow) within the work radius.
        private GameObject FindRestoreTarget(ChoreKind kind, Vector3 pos)
        {
            float radius = (kind == ChoreKind.Farm || IsRogueDomain(kind))
                ? WorkRadius : RestoreResolveRadius;

            foreach (var hit in Physics.OverlapSphere(pos, radius))
            {
                switch (kind)
                {
                    // Caste-filtered too: restoring onto a station this ally may not
                    // work would post it somewhere it can only stand and watch.
                    case ChoreKind.Smelter: { var s = hit.GetComponentInParent<Smelter>(); if (MayWork(s)) return s.gameObject; break; }
                    case ChoreKind.Provisioning:
                    {
                        // Only the kind of station this worker was posted at counts
                        // as proof its workplace is still there.
                        var c = hit.GetComponentInParent<CookingStation>();
                        if (c != null && VariantMatchesRestore(c.gameObject)) return c.gameObject;
                        var f = hit.GetComponentInParent<Fermenter>();
                        if (f != null && VariantMatchesRestore(f.gameObject)) return f.gameObject;
                        break;
                    }
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
                    // Either Rogue id restores on either kind of workplace: a herd
                    // to tend, or a chest to clear the ground into.
                    case ChoreKind.Husbandry:
                    case ChoreKind.Haul:
                    {
                        var ch = hit.GetComponentInParent<Character>();
                        if (ch != null && ch.IsTamed() && ch.GetComponent<DvergrCompanion>() == null) return ch.gameObject;
                        var ct = hit.GetComponentInParent<Container>();
                        if (ct != null && ChoreStorage.IsStoragePiece(ct)) return ct.gameObject;
                        break;
                    }
                }
            }
            return null;
        }

        private bool VariantMatchesRestore(GameObject go)
            => _restoreVariant == ChoreVariant.Any || VariantFor(go) == _restoreVariant;

        // Re-post the worker where it was working. The resolved object only proves
        // the workplace is still there; the SAVED position is the post, so a chore
        // doesn't drift each time it is restored from a different animal or crop.
        private void ResumeChore(ChoreKind kind, GameObject go)
        {
            _kind = kind;
            BeginChore(go);   // derives the variant from the station it resolved onto

            // A saved variant wins, EXCEPT the legacy "Any" written before
            // provisioning was split into cookfires / oven / fermenters. Those adopt
            // the kind of station they just restored onto rather than staying
            // unspecialised forever, so an existing kitchen ally settles into one job
            // instead of continuing to wander between all three.
            if (_restoreVariant != ChoreVariant.Any) _variant = _restoreVariant;

            _post = _restorePos;
            PersistChore();
        }

        // Take exclusive claim of the assignment anchor, releasing any prior one.
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
        // frees up immediately instead of waiting for the next ClaimantOf prune, and
        // shut any chest lid it was holding open (Container.SetInUse is not
        // self-clearing, so a worker that vanishes mid-deposit would leave the chest
        // standing open with its lid up).
        private void OnDisable()
        {
            ReleaseClaim();
            CloseChest();
        }

        // Ends the chore and hands the companion back to a stance. Defaults to
        // Follow — the player recalled it, so it comes to them. Both places the
        // chore ends on its OWN (the restore give-up in TryRestore, and the
        // patch-went-empty check in Update) pass Standby instead: nobody called
        // that ally off, so it holds its post rather than setting off to find its
        // owner, who may be anywhere.
        public void Unassign(CompanionStance fallback = CompanionStance.Follow)
        {
            _kind = ChoreKind.None;
            _station = null;
            _fermenter = null;
            _cooker = null;
            _anchorObject = null;
            _pendingRestore = false;
            _walkTarget = null;
            _blockedUntil.Clear();
            _cullDrops.Clear();

            ReleaseClaim();
            ClearPersistedChore();
            ClearSpeech();
            CloseChest();

            if (_companion != null)
            {
                _companion.ChoreActive = false;
                // Restores the alert range + follow/patrol behavior for the stance.
                _companion.SetStance(
                    fallback,
                    fallback == CompanionStance.Follow ? _companion.OwnerPlayer()?.gameObject : null);
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

        // ---- Tick ------------------------------------------------------------

        private void Update()
        {
            // Auto-close a chest the companion popped open, independent of chore ticks.
            if (_openChest != null && Time.time > _closeChestTime) CloseChest();

            // Resume a chore persisted from a previous session, once its workplace
            // has loaded. Runs until resolved (or it gives up).
            if (_pendingRestore) { TryRestore(); return; }

            if (_kind == ChoreKind.None || _humanoid == null) return;

            // Only the client/host that OWNS this companion's ZDO drives the chore,
            // so the work happens exactly once. This is also what lets a chore keep
            // running when the assigning player logs out: ZDO ownership migrates to
            // whoever still has the zone loaded (the server / another player), and
            // that owner picks the chore right up. In single-player the local host
            // always owns it, so this is a no-op there.
            if (_znv != null && _znv.IsValid() && !_znv.IsOwner()) return;

            // The round runs on a lazy tick, EXCEPT while something is cooked and
            // waiting: food burns on its own timer, so the cook checks back quickly
            // until the rack is clear.
            //
            // Written in this order deliberately. The obvious form
            //     if (_feedTimer < (AnythingBurning() ? Urgent : Feed)) return;
            // asks the question EVERY FRAME, and the question is an overlap query
            // across a 20 m patch. Bailing out under the urgent interval first means
            // it is asked at most once a second, and only by a provisioning worker.
            _feedTimer += Time.deltaTime;
            if (_feedTimer < UrgentInterval) return;
            if (_feedTimer < FeedInterval && !AnythingBurning()) return;
            _feedTimer = 0f;

            // The caste gate is not only an assignment-time check. A chore persists
            // on the ZDO, so a record written before a domain changed hands (feeding
            // was the Support Mage's until husbandry moved to the Rogue) would
            // otherwise keep an ally working a chore its caste no longer does. The
            // Smelter domain is exempt here because its split is per-station rather
            // than per-domain — MayWork enforces it, and a caste-filtered
            // PatchHasWork stands the ally down if nothing in the patch is its work.
            var domainCaste = ChoreRules.RequiredCaste(_kind);
            if (domainCaste.HasValue && _companion != null && _companion.Caste != domainCaste.Value)
            {
                Say("This is not my craft.");
                Unassign(CompanionStance.Standby);
                return;
            }

            // Is there still anything of this kind here at all? Individual stations,
            // crops and animals come and go without ending the chore; an empty patch
            // for a full minute does.
            if (!PatchHasWork())
            {
                if (_idleSince < 0f) _idleSince = Time.time;
                if (Time.time - _idleSince > IdleGiveUpSeconds) { Unassign(CompanionStance.Standby); return; }
            }
            else
            {
                _idleSince = -1f;
            }

            // File what the chore has already produced before making more of it.
            // A blocked deposit (nowhere with room) ends the tick — the worker has
            // said why, and piling up more output helps nobody.
            if (!StoreProducts()) return;

            switch (_kind)
            {
                case ChoreKind.Smelter:
                case ChoreKind.Provisioning:
                    ServiceStations();
                    break;

                case ChoreKind.Farm:
                    if (!AtPost("I can't reach the field!")) return;
                    ServiceFarm();
                    break;

                case ChoreKind.Husbandry:
                case ChoreKind.Haul:
                    ServiceRogue();
                    break;
            }
        }

        // Radius chores (the field, the pen) are worked FROM the post rather than by
        // walking to each target, so they still need to check the worker is actually
        // standing at its workplace.
        //
        // The tolerance is deliberately loose. An idle worker holds its post as a
        // patrol point and vanilla wanders it around that point, and a herder has
        // just walked as far as the pen's edge to cull something — neither is
        // "stuck", so a tight radius would report a blocker on a chore that is
        // working perfectly. And like a station it can't reach, the verdict only
        // lands after several consecutive ticks of getting nowhere.
        private bool AtPost(string complaint)
        {
            float tolerance = Mathf.Max(8f, WorkRadius * 0.5f);
            if (Vector3.Distance(transform.position, _post) <= tolerance)
            {
                _postTicks = 0;
                return true;
            }

            if (++_postTicks >= UnreachableTicks)
            {
                Say(complaint);
                _postTicks = 0;
            }
            return false;
        }

        // Send the worker back to stand at its post with nothing in particular to
        // do. There is no GameObject to follow (the post is a position), so this is
        // the patrol point — the same mechanism the Guard stance uses.
        private void ReturnToPost()
        {
            if (_ai == null) return;
            _ai.SetFollowTarget(null);
            _ai.SetPatrolPoint(_post);
        }

        // Is there anything of this chore's kind in the patch at all? Deliberately
        // NOT "is there anything to do" — a field with nothing ripe and a forge with
        // nothing queued are both working sites the ally should keep standing at.
        private bool PatchHasWork()
        {
            switch (_kind)
            {
                case ChoreKind.Smelter:
                    foreach (var s in InPatch<Smelter>()) if (MayWork(s)) return true;
                    return false;

                case ChoreKind.Provisioning:
                    foreach (var c in InPatch<CookingStation>()) if (CoversVariant(c.gameObject)) return true;
                    foreach (var f in InPatch<Fermenter>()) if (CoversVariant(f.gameObject)) return true;
                    return false;

                case ChoreKind.Husbandry:
                case ChoreKind.Haul:
                    foreach (var hit in Physics.OverlapSphere(_post, WorkRadius))
                    {
                        var ch = hit.GetComponentInParent<Character>();
                        if (ch != null && ch.IsTamed() && ch.GetComponent<DvergrCompanion>() == null) return true;
                        var ct = hit.GetComponentInParent<Container>();
                        if (ct != null && ChoreStorage.IsStoragePiece(ct)) return true;
                    }
                    return false;

                case ChoreKind.Farm:
                {
                    foreach (var hit in Physics.OverlapSphere(_post, WorkRadius))
                    {
                        if (hit.GetComponentInParent<Pickable>() != null) return true;
                        if (hit.GetComponentInParent<Plant>() != null) return true;
                    }
                    // A field that has been picked clean is still a field: cultivated
                    // ground under the post means there is somewhere to plant.
                    var hm = Heightmap.FindHeightmap(_post);
                    return hm != null && hm.IsCultivated(_post);
                }
            }
            return false;
        }

        // Every LIVE component of the given type inside the patch, de-duplicated.
        //
        // "Live" is the load-bearing word. A station being torn down leaves its
        // component reachable through the colliders for a frame or two while its
        // ZDO is already gone, and asking such a station anything walks into a
        // NullReferenceException inside vanilla — CookingStation.GetFreeSlot reads
        // the ZDO through GetSlot, which is exactly the crash reported after a
        // cooking station was destroyed under a working ally. Filtering here fixes
        // it for every caller at once rather than per question.
        private List<T> InPatch<T>() where T : Component
        {
            var found = new List<T>();
            foreach (var hit in Physics.OverlapSphere(_post, WorkRadius))
            {
                var c = hit.GetComponentInParent<T>();
                if (c == null || found.Contains(c)) continue;
                if (!IsLive(c)) continue;
                found.Add(c);
            }
            return found;
        }

        // A world object is safe to interrogate only while its ZNetView still holds
        // a ZDO. Everything the chore system touches is a networked piece, so this
        // one test covers stations, containers and creatures alike.
        private static bool IsLive(Component c)
        {
            if (c == null) return false;
            var nview = c.GetComponentInParent<ZNetView>();
            return nview != null && nview.IsValid();
        }

        // ---- Stations: walk the round ----------------------------------------

        // One job per tick: pick the nearest station in the patch that wants
        // something, walk to it, and service it once it is in reach. A station that
        // turns out to be unservable (no ore in any chest, a brew exposed to the
        // sky) is set aside for BlockedRetrySeconds so it can't starve the rest of
        // the row — the worker says why, then moves on to the next furnace.
        private void ServiceStations()
        {
            var job = FindNextJob();
            if (job == null)
            {
                ReturnToPost();
                ClearSpeech();
                return;
            }

            if (_ai != null && _ai.GetFollowTarget() != job.gameObject) _ai.SetFollowTarget(job.gameObject);

            if (Vector3.Distance(transform.position, job.transform.position) > ArrivalRange)
            {
                // Still walking. Only after several ticks of getting nowhere is it
                // fair to call the station unreachable — otherwise every trip across
                // the patch would be reported as a failure.
                if (_walkTarget == job.gameObject) _walkTicks++;
                else { _walkTarget = job.gameObject; _walkTicks = 1; }

                if (_walkTicks >= UnreachableTicks)
                {
                    Say("I can't reach my station!");
                    SetAside(job.gameObject);
                    _walkTarget = null;
                    _walkTicks = 0;
                }
                return;
            }

            _walkTarget = null;
            _walkTicks = 0;
            FaceToward(job.transform.position);

            bool served;
            var smelter = job as Smelter;
            if (smelter != null) { _station = smelter; served = ServiceSmelter(smelter); }
            else
            {
                var cooker = job as CookingStation;
                if (cooker != null) { _cooker = cooker; served = ServiceCooking(cooker); }
                else { _fermenter = job as Fermenter; served = ServiceFermenter(_fermenter); }
            }

            if (!served) SetAside(job.gameObject);
        }

        // Nearest station in the patch that wants something and isn't set aside —
        // except that FOOD ABOUT TO BURN jumps the queue.
        //
        // A cooking station holds a done item for a few seconds and then ruins it,
        // and the round is a 5 s tick across a patch up to 20 m wide, so a cook that
        // simply took the nearest job would walk past finished meat to go and load a
        // fermenter. Anything with a done item on it is served first, nearest of
        // those, before distance is considered at all.
        private Component FindNextJob()
        {
            Component best = null;
            float bestDist = float.MaxValue;
            Component urgent = null;
            float urgentDist = float.MaxValue;

            void Consider(Component c, bool wants)
            {
                if (c == null || !wants || IsSetAside(c.gameObject)) return;
                float d = Vector3.Distance(transform.position, c.transform.position);

                var cooker = c as CookingStation;
                if (cooker != null && cooker.HaveDoneItem())
                {
                    if (d < urgentDist) { urgentDist = d; urgent = c; }
                    return;
                }

                if (d < bestDist) { bestDist = d; best = c; }
            }

            if (_kind == ChoreKind.Smelter)
            {
                // The caste split still applies WITHIN a patch: a Fire Mage posted
                // in a workshop that also holds an eitr refinery keeps to the
                // smelters, kilns and furnaces and leaves the refinery to an Ice
                // Mage (ChoreRules).
                foreach (var s in InPatch<Smelter>())
                    Consider(s, MayWork(s) && WantsWork(s));
            }
            else
            {
                foreach (var c in InPatch<CookingStation>())
                    Consider(c, CoversVariant(c.gameObject) && WantsWork(c));
                foreach (var f in InPatch<Fermenter>())
                    Consider(f, CoversVariant(f.gameObject) && WantsWork(f));
            }

            return urgent ?? best;
        }

        // Is anything in the patch about to burn? Drives the short tick below.
        private bool AnythingBurning()
        {
            if (_kind != ChoreKind.Provisioning) return false;
            foreach (var c in InPatch<CookingStation>())
                if (CoversVariant(c.gameObject) && c.HaveDoneItem()) return true;
            return false;
        }

        // May THIS worker touch this station? The Fire/Ice split is not just an
        // assignment-time gate: a patch can hold both kinds, and every place that
        // walks the stations in a patch has to ask, or a Fire Mage ends up storing
        // the eitr refinery's output and an Ice Mage the smelter's bars — which
        // reads in-game exactly like the two castes sharing each other's chores.
        private bool MayWork(Smelter s)
        {
            if (s == null) return false;
            ChoreRules.LogMapping(s);

            var required = ChoreRules.RequiredCaste(s);
            return !required.HasValue || _companion == null || required.Value == _companion.Caste;
        }

        private static bool WantsWork(Smelter s)
        {
            if (!IsLive(s)) return false;
            if (s.GetQueueSize() < s.m_maxOre) return true;
            return s.m_fuelItem != null && s.GetFuel() < s.m_maxFuel;
        }

        private static bool WantsWork(CookingStation c)
        {
            if (!IsLive(c)) return false;
            if (c.HaveDoneItem()) return true;
            if (c.m_fuelItem != null && c.GetFuel() < c.m_maxFuel) return true;
            return c.GetFreeSlot() >= 0;
        }

        private static bool WantsWork(Fermenter f)
        {
            if (!IsLive(f)) return false;
            var status = f.GetStatus();
            return status == Fermenter.Status.Ready
                || status == Fermenter.Status.Empty
                || status == Fermenter.Status.Exposed;
        }

        private void SetAside(GameObject job)
        {
            if (job != null) _blockedUntil[job] = Time.time + BlockedRetrySeconds;
        }

        private bool IsSetAside(GameObject job)
        {
            if (job == null) return true;
            if (!_blockedUntil.TryGetValue(job, out var until)) return false;
            if (Time.time >= until) { _blockedUntil.Remove(job); return false; }
            return true;
        }

        // ---- Smelter family ---------------------------------------------------

        private bool ServiceSmelter(Smelter station)
        {
            bool needsOre = station.GetQueueSize() < station.m_maxOre;
            bool needsFuel = station.m_fuelItem != null && station.GetFuel() < station.m_maxFuel;

            if (!needsOre && !needsFuel) { ClearSpeech(); return true; }

            var fuelPrefabName = station.m_fuelItem != null ? station.m_fuelItem.name : null;
            bool fuelMissing = needsFuel;
            bool oreMissing = needsOre;

            var containers = ChoreStorage.Nearby(station.transform.position, OwnerId);
            bool anyContainer = containers.Count > 0;

            foreach (var container in containers)
            {
                var sourceInventory = container.GetInventory();
                if (sourceInventory == null) continue;

                foreach (var item in new List<ItemDrop.ItemData>(sourceInventory.GetAllItems()))
                {
                    var prefabName = item.m_dropPrefab != null ? item.m_dropPrefab.name : null;
                    if (prefabName == null) continue;

                    if (needsFuel && prefabName == fuelPrefabName)
                    {
                        ChoreStorage.ClaimForWrite(container);
                        if (sourceInventory.RemoveItem(item, 1))
                        {
                            OpenChest(container);
                            FaceToward(station.transform.position);
                            station.SetFuel(station.GetFuel() + 1f);
                            PlayEffects(station.m_fuelAddedEffects, station.transform);
                            needsFuel = false;
                            fuelMissing = false;
                        }
                        continue;
                    }

                    if (needsOre && station.GetItemConversion(prefabName) != null)
                    {
                        ChoreStorage.ClaimForWrite(container);
                        if (sourceInventory.RemoveItem(item, 1))
                        {
                            OpenChest(container);
                            FaceToward(station.transform.position);
                            station.QueueOre(prefabName);
                            PlayEffects(station.m_oreAddedEffects, station.transform);
                            needsOre = false;
                            oreMissing = false;
                        }
                        continue;
                    }
                }

                if (!needsOre && !needsFuel) break;
            }

            if (!anyContainer) { Say("I have no chest to draw from!"); return false; }

            ReportMissing(oreMissing, fuelMissing);
            return !(oreMissing || fuelMissing);
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
            // The Cultivator in the ally's pack is what made it a farmer, so taking
            // it back is a way to retire it — the tool is the licence, not just the
            // switch that started the job. Standby, like every chore that ends
            // without the player calling the ally off: it holds the field.
            if (_companion != null && !CarriesCultivator(_companion))
            {
                Say("I've no cultivator to work this ground.");
                Unassign(CompanionStance.Standby);
                return;
            }

            var center = _post;

            // 1) Harvest: one ripe crop of ANY type per tick -> stored by the same
            //    chooser every other chore uses, searched from where the crop stood
            //    and then from the post (see StoreProduct — a 20 m field is wider
            //    than the 10 m chest search, so the far ring needs that fallback).
            // Harvest the same number of crops the ally would PLANT: a level-10
            // farmer that sows twenty-five at a pass and then picks one every five
            // seconds is a strange sort of expert. Same block size, same reason.
            int armful = PlantBlockSide() * PlantBlockSide();
            int picked = 0;
            Vector3 lastPick = center;

            foreach (var hit in Physics.OverlapSphere(center, WorkRadius))
            {
                if (picked >= armful) break;

                var p = hit.GetComponentInParent<Pickable>();
                if (p == null || !p.CanBePicked()) continue;

                // A FIELD, not a foraging trip. Pickable is the same component behind
                // stones, branches, dandelions, mushrooms and berry bushes, so a
                // farmer working a 20 m patch in the Meadows would strip the wild
                // ground around it as well as the bed. Cultivated soil under the crop
                // is what separates the two — nothing wild grows on tilled ground.
                if (!IsOnCultivatedGround(p.transform.position)) continue;

                var prefab = p.m_itemPrefab;
                int amount = Mathf.Max(1, p.m_amount);
                if (prefab == null) continue;

                var bin = StoreProduct(prefab, amount, p.transform.position);
                if (bin == null)
                {
                    // Nowhere to put it. Say so, but keep whatever was already
                    // gathered this tick rather than throwing the armful away.
                    if (picked == 0) { SayNoStorage(p.transform.position); return; }
                    break;
                }

                p.SetPicked(true);
                OpenChest(bin);
                PlayEffects(p.m_pickEffector, null, p.transform.position);
                lastPick = p.transform.position;
                picked++;
            }

            if (picked > 0)
            {
                FaceToward(lastPick);
                ClearSpeech();
                return; // harvesting was this tick's action
            }

            // 2) Nothing ripe -> plant a BLOCK of seed, on a tidy grid. See
            //    PlantBlockSide / TryFindPlantBlock.
            if (!TryFindPlantSpotForField(center, out var sapling, out var spots, out var why))
            {
                if (why != null) { Say(why); LogFarm(why); }
                return;
            }

            var piece = sapling.GetComponent<Piece>();
            int planted = 0;

            foreach (var spot in spots)
            {
                if (!ConsumeSeed(sapling)) break;   // ran out mid-row

                Object.Instantiate(sapling, spot, Quaternion.Euler(0f, Random.Range(0f, 360f), 0f));
                if (piece != null) PlayEffects(piece.m_placeEffect, null, spot);
                planted++;
            }

            if (planted == 0) return;

            FaceToward(spots[0]);
            Plugin.Log.LogInfo($"[farm] planted {planted}x '{sapling.name}' around {spots[0]} (biome {CurrentBiome(spots[0])}).");
            ClearSpeech();
        }

        // ---- Planting layout -------------------------------------------------
        //
        // A farmer plants a BLOCK, not one seed at a time, and it plants on a grid.
        //
        // The grid is a world-aligned lattice (positions snapped to a multiple of
        // the crop's spacing), which is what makes successive batches line up with
        // each other and with what is already in the ground. Before this, spots were
        // sampled at random inside the radius and the field came out looking sown by
        // hand in the dark.
        //
        // How many at once is the ally's LEVEL: 2x2 to begin with, and one more per
        // side every few levels, so a seasoned farmer plants a visibly bigger bed
        // per pass. Square rather than a flat count, because the block has to tile.
        private const int BasePlantSide = 2;
        private const int LevelsPerPlantSide = 3;

        private int PlantBlockSide()
        {
            int level = _companion != null ? _companion.Level : 1;
            return BasePlantSide + Mathf.Max(0, (level - 1) / LevelsPerPlantSide);
        }

        // Row spacing for a crop: its own grow radius, doubled so two neighbours
        // don't sit inside each other's clearance (which is exactly what IsSpotClear
        // rejects), plus a small margin.
        //
        // The margin is not cosmetic. A whole block is resolved against the world
        // BEFORE any of it is planted, so the cells are never checked against each
        // other — exactly 2x the radius apart would put every neighbour on the
        // boundary and leave it to float comparison whether the bed is legal.
        private const float PlantSpacingMargin = 0.1f;

        private static float PlantSpacing(GameObject sapling)
        {
            var plant = sapling != null ? sapling.GetComponent<Plant>() : null;
            float grow = plant != null ? Mathf.Max(plant.m_growRadius, 0.25f) : 0.5f;
            return Mathf.Max(grow * 2f + PlantSpacingMargin, 0.6f);
        }

        // Snap to the world lattice. Deliberately in WORLD space, not relative to
        // the post: two farmers working neighbouring beds, or one field replanted
        // after the post drifted, still line up.
        private static Vector3 LatticePoint(int gx, int gz, float spacing)
            => new Vector3(gx * spacing, 0f, gz * spacing);

        // Find a tidy block of plantable cells, nearest the post first. A complete
        // side x side block wins; if the field has no room for a whole one, whatever
        // single cells are free are used instead, so a nearly-full bed still gets
        // topped up rather than reporting "no room".
        private bool TryFindPlantBlock(Vector3 center, GameObject sapling, out List<Vector3> spots)
        {
            spots = new List<Vector3>();

            float spacing = PlantSpacing(sapling);
            int side = PlantBlockSide();
            var plant = sapling.GetComponent<Plant>();
            float grow = plant != null ? Mathf.Max(plant.m_growRadius, 0.5f) : 1f;

            int reach = Mathf.CeilToInt(WorkRadius / spacing);
            int originX = Mathf.RoundToInt(center.x / spacing);
            int originZ = Mathf.RoundToInt(center.z / spacing);

            // PASS 1 (cheap, and unbudgeted): which lattice cells sit on cultivated
            // ground this crop can grow in?
            //
            // This is deliberately free. The first version charged every cell against
            // one budget, including the ones that just aren't soil, so on a field the
            // worker was standing at the edge of, the budget ran out on bare ground
            // before it ever reached the far half — and the chore then reported
            // "There's no room left to plant" while there was plainly room a few
            // metres away. Terrain paint and biome are cheap lookups; only the
            // clearance test costs a physics query, so only that is rationed.
            //
            // Ground height comes from ZoneSystem.GetGroundHeight, which raycasts
            // from y = 6000 straight down and so answers correctly for RAISED terrain
            // as well as flat (the lattice point's own y is irrelevant to it).
            var soil = new List<KeyValuePair<Vector2Int, Vector3>>();
            var zs = ZoneSystem.instance;

            for (int dx = -reach; dx <= reach; dx++)
                for (int dz = -reach; dz <= reach; dz++)
                {
                    var cell = new Vector2Int(originX + dx, originZ + dz);
                    var p = LatticePoint(cell.x, cell.y, spacing);
                    if ((new Vector2(p.x - center.x, p.z - center.z)).sqrMagnitude > WorkRadius * WorkRadius) continue;

                    // Terrain paint and biome are decided by x/z alone — Heightmap's
                    // own IsPointInside ignores y entirely, and IsCultivated samples
                    // the paint mask by world x/z. So the cheap tests run FIRST and
                    // the ground height (a raycast from y = 6000) is only paid for on
                    // cells that turn out to be soil. Doing it the other way round
                    // meant a raycast for every cell in a 20 m patch, thousands of
                    // them, most on bare grass.
                    var hm = Heightmap.FindHeightmap(p);
                    if (hm == null || !hm.IsCultivated(p)) continue;
                    if (!CanGrowInBiome(plant, p)) continue;

                    if (zs != null) p.y = zs.GetGroundHeight(p);

                    soil.Add(new KeyValuePair<Vector2Int, Vector3>(cell, p));
                }

            if (soil.Count == 0)
            {
                LogFarm($"no cultivated ground for '{sapling.name}' in the {WorkRadius:0.#} m patch around {_post} " +
                        $"(grid spacing {spacing:0.##} m).");
                return false;
            }

            // Nearest the post first, so a bed grows outward from where the ally was
            // set to work rather than starting in a far corner.
            soil.Sort((a, b) =>
                (new Vector2(a.Value.x - center.x, a.Value.z - center.z)).sqrMagnitude
                .CompareTo(new Vector2(b.Value.x - center.x, b.Value.z - center.z).sqrMagnitude));

            var soilByCell = new Dictionary<Vector2Int, Vector3>(soil.Count);
            foreach (var entry in soil) soilByCell[entry.Key] = entry.Value;

            // PASS 2: clearance. Memoised per tick (blocks overlap heavily) and
            // budgeted, since this is the part that costs a physics query.
            var clear = new Dictionary<Vector2Int, bool>();
            int budget = PlantClearanceBudget;

            System.Func<Vector2Int, bool> IsFree = cell =>
            {
                if (clear.TryGetValue(cell, out var cached)) return cached;
                if (!soilByCell.TryGetValue(cell, out var p)) { clear[cell] = false; return false; }

                bool ok = budget-- > 0 && IsSpotClear(p, grow);
                clear[cell] = ok;
                return ok;
            };

            // 1) A complete block.
            foreach (var entry in soil)
            {
                if (budget <= 0) break;

                var block = new List<Vector3>(side * side);
                bool whole = true;

                for (int ox = 0; ox < side && whole; ox++)
                    for (int oz = 0; oz < side; oz++)
                    {
                        var cell = new Vector2Int(entry.Key.x + ox, entry.Key.y + oz);
                        if (!IsFree(cell)) { whole = false; break; }
                        block.Add(soilByCell[cell]);
                    }

                if (whole && block.Count == side * side) { spots = block; return true; }
            }

            // 2) No whole block: fill whatever single cells are free, nearest first.
            foreach (var entry in soil)
            {
                if (IsFree(entry.Key)) spots.Add(entry.Value);
                if (spots.Count >= side * side) break;
            }
            if (spots.Count > 0) return true;

            LogFarm($"{soil.Count} cultivated cell(s) found for '{sapling.name}' but none clear " +
                    $"(clearance budget left {budget}); trying off-grid.");

            // 3) Last resort, OFF the lattice. A bed the player sowed by hand does
            //    not line up with our grid, so its gaps can be real ground that no
            //    lattice cell can reach — visible room the ally would otherwise
            //    refuse to use. Tidiness is the preference, not a requirement.
            foreach (var entry in soil)
            {
                for (int i = 0; i < 4; i++)
                {
                    var offset = new Vector3(
                        (i == 0 ? 1 : i == 1 ? -1 : 0) * spacing * 0.5f, 0f,
                        (i == 2 ? 1 : i == 3 ? -1 : 0) * spacing * 0.5f);

                    var p = entry.Value + offset;
                    if (zs != null) p.y = zs.GetGroundHeight(p);

                    var hm = Heightmap.FindHeightmap(p);
                    if (hm == null || !hm.IsCultivated(p)) continue;
                    if (!IsSpotClear(p, grow)) continue;

                    spots.Add(p);
                    return true;   // one is enough to keep the field moving
                }
            }

            return false;
        }

        // Clearance tests per tick. Only the physics half of the search is rationed
        // (see pass 1), and generously — the number that matters is "enough to
        // cross a full field", not "enough to fill one block".
        private const int PlantClearanceBudget = 600;

        // The chests in range, resolved at most once a second.
        //
        // Planting a block calls ConsumeSeed once per seed — up to 25 times for a
        // level-10 farmer — and each of those used to run its own overlap query for
        // the chest list. Chests do not move; the list does not need re-deriving
        // twenty-five times in one tick.
        private List<Container> _chestCache;
        private float _chestCacheTime = -999f;

        private List<Container> NearbyChests()
        {
            if (_chestCache != null && Time.time - _chestCacheTime < 1f) return _chestCache;
            _chestCache = ChoreStorage.Nearby(_post, OwnerId);
            _chestCacheTime = Time.time;
            return _chestCache;
        }

        // Why a farm tick did nothing, at most once every 30 s per worker.
        //
        // "It won't plant on an empty field" has now been chased through three
        // different causes (a budget that ran out on bare ground, a seed lookup that
        // only read the nearest chest, a shared-name lookup that went through
        // ObjectDB), and each round cost a full test pass to narrow down. The
        // numbers that would have settled it immediately — how many seed types are
        // in reach, how many cultivated cells were found, how much of the clearance
        // budget was spent — are cheap to print and impossible to infer from the
        // speech bubble.
        private float _lastFarmLog = -999f;

        private void LogFarm(string message)
        {
            if (Time.time - _lastFarmLog < 30f) return;
            _lastFarmLog = Time.time;
            Plugin.Log.LogInfo($"[farm] {WorkerName}: {message}");
        }

        // ONE CROP PER FIELD. A farmer must not leave a patchwork of carrots,
        // turnips and barley in the same bed: you plant a field of something. So the
        // crop is decided by what is ALREADY growing in the patch, and only when
        // nothing is growing at all does the ally get to choose (the first seed it
        // can legally plant). The field therefore keeps its identity across
        // harvests, and a player re-seeds it to something else simply by planting
        // the first of the new crop themselves.
        private GameObject FieldSapling()
        {
            foreach (var hit in Physics.OverlapSphere(_post, WorkRadius))
            {
                var plant = hit.GetComponentInParent<Plant>();
                if (plant == null) continue;

                var prefab = ZNetScene.instance != null
                    ? ZNetScene.instance.GetPrefab(Utils.GetPrefabName(plant.gameObject))
                    : null;
                if (prefab != null) return prefab;
            }
            return null;
        }

        // Seeds the worker can actually plant, its own pack first. Carrying seed is
        // a convenience the player sets up deliberately (drop a stack in the ally's
        // bag and it stops walking back to the chest between rows); the chest by the
        // soil remains the normal source.
        private bool HasSeedFor(GameObject sapling, out bool inPack)
        {
            inPack = false;
            if (sapling == null) return false;

            string prefabName = PlantingCatalog.SeedFor(sapling);
            string sharedName = PlantingCatalog.SeedSharedName(sapling);
            if (string.IsNullOrEmpty(prefabName) && string.IsNullOrEmpty(sharedName)) return false;

            var pack = GetComponent<CompanionInventory>();
            if (pack != null && pack.FindItem(i => IsSeed(i, prefabName, sharedName)) != null)
            {
                inPack = true;
                return true;
            }

            // EVERY chest in range, not just the nearest one. Looking only at the
            // nearest is how "not all my seeds are recognised" happens: the closest
            // chest is usually the one the harvest goes into, and the seed chest is
            // the one behind it.
            foreach (var container in NearbyChests())
            {
                var inv = container.GetInventory();
                if (inv == null) continue;
                foreach (var item in inv.GetAllItems())
                    if (IsSeed(item, prefabName, sharedName)) return true;
            }

            return false;
        }

        // Does this inventory item hold that seed?
        //
        // Matched on the SHARED name first. The prefab name looked like the obvious
        // key and it is not a safe one: Inventory.Load rebuilds each stored item by
        // instantiating its prefab and keeping the clone's ItemData, so
        // m_dropPrefab can be null or point at a "(Clone)"-suffixed object. That is
        // why seeds sitting in a companion's pack were invisible to the farmer while
        // the same seeds in a chest were found. The prefab name is still accepted
        // (clone suffix tolerated) so nothing that used to match stops matching.
        private static bool IsSeed(ItemDrop.ItemData item, string prefabName, string sharedName)
        {
            if (item == null) return false;

            if (!string.IsNullOrEmpty(sharedName) && item.m_shared != null
                && item.m_shared.m_name == sharedName) return true;

            var n = item.m_dropPrefab != null ? item.m_dropPrefab.name : null;
            if (string.IsNullOrEmpty(n) || string.IsNullOrEmpty(prefabName)) return false;
            if (n == prefabName) return true;

            const string clone = "(Clone)";
            return n.EndsWith(clone) && n.Substring(0, n.Length - clone.Length) == prefabName;
        }

        private bool ConsumeSeed(GameObject sapling)
        {
            string prefabName = PlantingCatalog.SeedFor(sapling);
            string sharedName = PlantingCatalog.SeedSharedName(sapling);
            if (string.IsNullOrEmpty(prefabName) && string.IsNullOrEmpty(sharedName)) return false;

            // The ally's own bag first — carrying seed is a convenience the player
            // set up deliberately so it stops walking back to the chest between rows.
            var pack = GetComponent<CompanionInventory>();
            if (pack != null)
            {
                var held = pack.FindItem(i => IsSeed(i, prefabName, sharedName));
                if (held != null && pack.ConsumeOne(held)) return true;
            }

            foreach (var container in NearbyChests())
            {
                var inv = container.GetInventory();
                if (inv == null) continue;

                foreach (var item in new List<ItemDrop.ItemData>(inv.GetAllItems()))
                {
                    if (!IsSeed(item, prefabName, sharedName)) continue;
                    ChoreStorage.ClaimForWrite(container);
                    if (inv.RemoveItem(item, 1)) { OpenChest(container); return true; }
                }
            }
            return false;
        }

        // Pick the crop and the spot together, because "which crop" constrains
        // "where" (biome) and vice versa. `why` carries the blocker to voice, or
        // null when there is simply nothing to do.
        private bool TryFindPlantSpotForField(Vector3 center, out GameObject sapling, out List<Vector3> spots, out string why)
        {
            spots = new List<Vector3>();
            sapling = null;
            why = null;

            // Everything we could plant right now, from the pack and every chest in
            // range.
            var reachable = PlantableSeedsInReach();
            if (reachable.Count == 0)
            {
                LogFarm($"nothing plantable in reach (pack + {NearbyChests().Count} chest(s) within " +
                        $"{ChoreStorage.SearchRadius:0.#} m of the post).");
                why = "No crops are ready, and no seeds to plant.";
                return false;
            }

            // ORDER OF PREFERENCE. The field's own crop first, so a bed keeps to one
            // kind while its seed lasts; then anything else we hold, so a field whose
            // crop has RUN OUT gets filled with the next thing rather than standing
            // half empty. That is the whole rule: one crop per field is a preference
            // paid for out of the seed supply, not a promise to leave ground bare.
            var ordered = new List<GameObject>();
            var field = FieldSapling();
            if (field != null && reachable.Contains(field)) ordered.Add(field);
            foreach (var s in reachable) if (!ordered.Contains(s)) ordered.Add(s);

            GameObject wrongBiome = null;

            foreach (var candidate in ordered)
            {
                if (!CanGrowInBiome(candidate.GetComponent<Plant>(), center))
                {
                    if (wrongBiome == null) wrongBiome = candidate;
                    continue;
                }

                if (TryFindPlantBlock(center, candidate, out spots))
                {
                    sapling = candidate;
                    return true;
                }

                // Room is a property of the GROUND, not of the crop, so if this one
                // doesn't fit the next won't either. Stop rather than re-scanning the
                // whole field once per seed type we happen to be carrying.
                LogFarm($"{reachable.Count} seed type(s) in reach, tried '{candidate.name}', found no room " +
                        $"in the {WorkRadius:0.#} m patch around {_post}.");
                why = "There's no room left to plant.";
                return false;
            }

            // Nothing we hold will grow here. Name the crop and where it belongs —
            // "these seeds won't grow in this land" leaves the player to guess which
            // seed and which land.
            why = wrongBiome != null
                ? BiomeComplaint(wrongBiome, center)
                : "No crops are ready, and no seeds to plant.";
            return false;
        }

        // "Barley won't grow here — it needs the Plains."
        private static string BiomeComplaint(GameObject sapling, Vector3 where)
        {
            var plant = sapling != null ? sapling.GetComponent<Plant>() : null;
            string crop = CropName(sapling);
            string wants = plant != null ? BiomeNames(plant.m_biome) : null;

            return string.IsNullOrEmpty(wants)
                ? $"{crop} won't grow in this land."
                : $"{crop} won't grow here — it needs the {wants}.";
        }

        private static string CropName(GameObject sapling)
        {
            var piece = sapling != null ? sapling.GetComponent<Piece>() : null;
            if (piece != null && !string.IsNullOrEmpty(piece.m_name) && Localization.instance != null)
                return Localization.instance.Localize(piece.m_name);
            return sapling != null ? sapling.name : "That";
        }

        // Plant.m_biome is a flags mask, so a crop can name more than one home.
        private static string BiomeNames(Heightmap.Biome mask)
        {
            // A crop that grows everywhere is not worth naming nine biomes for.
            if ((mask & Heightmap.Biome.All) == Heightmap.Biome.All) return null;

            var names = new List<string>();
            foreach (Heightmap.Biome biome in System.Enum.GetValues(typeof(Heightmap.Biome)))
            {
                // None and All are the enum's bookends, not places.
                if (biome == Heightmap.Biome.None || biome == Heightmap.Biome.All) continue;
                if ((mask & biome) == 0) continue;
                names.Add(PrettyBiome(biome));
            }
            return names.Count > 0 ? string.Join(" or ", names.ToArray()) : null;
        }

        private static string PrettyBiome(Heightmap.Biome biome)
        {
            switch (biome)
            {
                case Heightmap.Biome.BlackForest: return "Black Forest";
                case Heightmap.Biome.AshLands:    return "Ashlands";
                case Heightmap.Biome.DeepNorth:   return "Deep North";
                default:                          return biome.ToString();
            }
        }

        // Every sapling the worker could plant right now, pack first then the chest.
        private List<GameObject> PlantableSeedsInReach()
        {
            var found = new List<GameObject>();

            void Scan(Inventory inv)
            {
                if (inv == null) return;
                foreach (var item in inv.GetAllItems())
                {
                    var name = item.m_dropPrefab != null ? item.m_dropPrefab.name : null;
                    if (!string.IsNullOrEmpty(name))
                    {
                        const string clone = "(Clone)";
                        if (name.EndsWith(clone)) name = name.Substring(0, name.Length - clone.Length);
                        if (PlantingCatalog.TryGetSapling(name, out var byPrefab))
                        {
                            if (!found.Contains(byPrefab)) found.Add(byPrefab);
                            continue;
                        }
                    }

                    // Fall back to the shared name, for items rebuilt from a ZDO with
                    // no usable m_dropPrefab (see IsSeed).
                    if (item.m_shared == null) continue;
                    var bySharedName = PlantingCatalog.SaplingForSharedName(item.m_shared.m_name);
                    if (bySharedName != null && !found.Contains(bySharedName)) found.Add(bySharedName);
                }
            }

            var pack = GetComponent<CompanionInventory>();
            if (pack != null) Scan(pack.Inventory);

            foreach (var container in NearbyChests())
                Scan(container.GetInventory());

            return found;
        }

        private static bool IsOnCultivatedGround(Vector3 pos)
        {
            var hm = Heightmap.FindHeightmap(pos);
            return hm != null && hm.IsCultivated(pos);
        }

        // Is there room to plant here? This is VANILLA'S OWN TEST, replicated.
        //
        // Plant.HaveGrowSpace rejects a spot if ANY collider on
        // Default / static_solid / Default_small / piece / piece_nonsolid sits within
        // the crop's grow radius — unless it is a Plant that isn't healthy. Rocks,
        // build pieces, wild pickables, fallen logs: all of it blocks.
        //
        // The earlier version only looked for a Plant or an unharvested Pickable,
        // which is far more permissive than the rule the game applies to the player,
        // and that is why the farmer sowed crops into rocks and wild growth. Copying
        // vanilla's mask means the ally can only plant where you could have planted
        // by hand, which is the right standard for a chore.
        //
        // (The vines clause of HaveGrowSpace is not replicated: m_growRadiusVines is
        // zero on every crop, and a vine sapling is not something a field chore
        // plants.)
        private static int s_growSpaceMask;

        private static bool IsSpotClear(Vector3 p, float radius)
        {
            if (s_growSpaceMask == 0)
                s_growSpaceMask = LayerMask.GetMask("Default", "static_solid", "Default_small", "piece", "piece_nonsolid");

            foreach (var hit in Physics.OverlapSphere(p, radius, s_growSpaceMask))
            {
                var plant = hit.GetComponent<Plant>();
                if (plant == null || plant.GetStatus() == Plant.Status.Healthy) return false;
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

        // ---- Husbandry: feed the herd, and cull the surplus ------------------

        // The Rogue's round. Hauling is not a separate step here: clearing the
        // ground is done by the product sweep (StoreProducts), which has already run
        // this tick and takes anything loose in the patch. What is left for this
        // method is the herd — and it deliberately FALLS THROUGH when there is no
        // herd work, so a Rogue posted by a chest with no animals nearby is a plain
        // hauler, and one posted in a pen full of drops is both.
        private void ServiceRogue()
        {
            // Culling comes first, because it is the only part that needs the worker
            // to walk somewhere: it is a MELEE cull by construction. We never hand
            // the kill to MonsterAI's attack picker (the thing that could choose a
            // ranged spell) — the ally has to close to arm's reach itself, and the
            // blow is landed directly. See Cull.
            var quarry = FindCullTarget();
            if (quarry != null)
            {
                if (_ai != null && _ai.GetFollowTarget() != quarry.gameObject) _ai.SetFollowTarget(quarry.gameObject);

                if (Vector3.Distance(transform.position, quarry.transform.position) > CullRange)
                {
                    if (_walkTarget == quarry.gameObject) _walkTicks++;
                    else { _walkTarget = quarry.gameObject; _walkTicks = 1; }

                    if (_walkTicks >= UnreachableTicks)
                    {
                        Say("I can't get to that one!");
                        SetAside(quarry.gameObject);
                        _walkTarget = null;
                        _walkTicks = 0;
                    }
                    return;
                }

                _walkTarget = null;
                _walkTicks = 0;
                Cull(quarry);
                return; // one action per tick
            }

            ReturnToPost();
            if (!AtPost("I can't get back to my post!")) return;

            var center = _post;

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

            // No herd, or a herd that has eaten, is not a blocker — the hauling half
            // of this chore is already done for the tick and there is nothing to
            // report. (The old feeding-only chore said "The animals aren't hungry."
            // here; with hauling folded in, that line would be a worker announcing
            // idleness while it was busy.)
            if (hungry.Count == 0) { ClearSpeech(); return; }

            var foodChest = ChoreStorage.NearestSource(center, OwnerId);
            var chest = foodChest != null ? foodChest.GetInventory() : null;
            if (chest == null) { Say("I have no food chest nearby."); return; }

            foreach (var animal in hungry)
            {
                var mai = animal.GetComponent<MonsterAI>();
                if (mai == null || mai.m_consumeItems == null) continue;
                if (ItemAlreadyDroppedNear(animal.transform.position)) continue; // wait for it to eat

                var food = FindAcceptedFood(chest, mai.m_consumeItems);
                if (food == null) { Say("I have no food to give."); return; }

                ChoreStorage.ClaimForWrite(foodChest);
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

        // Which animal, if any, should be culled? Grown tamed animals are grouped by
        // prefab, and any group over CullLimit has its surplus thinned.
        //
        // Vanilla Procreation stops breeding once m_maxCreatures (4 by default,
        // counting young) are within m_totalCheckRange, so a pen tops out on its own
        // — a cull limit BELOW that cap is what keeps the herd turning over: three
        // grown animals plus a calf reaches the cap, the calf grows up, the surplus
        // adult is culled, and breeding resumes. Young are never culled (they are
        // the next generation, and they already count toward the vanilla cap), and a
        // pregnant animal is spared while there is any other candidate.
        private Character FindCullTarget()
        {
            int limit = CullLimit;
            if (limit <= 0) return null;

            var grouped = new Dictionary<string, List<Character>>();
            foreach (var hit in Physics.OverlapSphere(_post, WorkRadius))
            {
                var c = hit.GetComponentInParent<Character>();
                if (c == null || !c.IsTamed() || c.IsDead()) continue;
                if (c.GetComponent<DvergrCompanion>() != null) continue;
                if (c.GetComponent<Growup>() != null) continue;   // still young

                var key = Utils.GetPrefabName(c.gameObject);
                if (!grouped.TryGetValue(key, out var list)) grouped[key] = list = new List<Character>();
                if (!list.Contains(c)) list.Add(c);
            }

            Character fallback = null;
            float fallbackDist = float.MaxValue;
            Character best = null;
            float bestDist = float.MaxValue;

            foreach (var group in grouped)
            {
                if (group.Value.Count <= limit) continue;

                foreach (var animal in group.Value)
                {
                    if (IsSetAside(animal.gameObject)) continue;

                    float d = Vector3.Distance(transform.position, animal.transform.position);
                    var proc = animal.GetComponent<Procreation>();
                    bool pregnant = proc != null && proc.IsPregnant();

                    if (pregnant)
                    {
                        if (d < fallbackDist) { fallbackDist = d; fallback = animal; }
                    }
                    else if (d < bestDist) { bestDist = d; best = animal; }
                }
            }

            return best ?? fallback;
        }

        // The cull itself. One clean blow at arm's reach, not a fight: livestock is
        // butchered, not duelled, and a boar that took six 5-second ticks to die
        // would spend that whole time running from its butcher.
        //
        // "Melee only" is structural rather than a setting. The swing is the ally's
        // OWN equipped weapon through the vanilla attack, and only when that
        // weapon's primary attack isn't a projectile — a caste holding a staff
        // simply lands the blow without an animation rather than casting across the
        // pen. Nothing here routes through MonsterAI, which is the code that would
        // otherwise pick an attack, and could pick a ranged one.
        private void Cull(Character animal)
        {
            FaceToward(animal.transform.position);

            var weapon = _humanoid != null ? _humanoid.GetCurrentWeapon() : null;
            var attack = weapon != null && weapon.m_shared != null ? weapon.m_shared.m_attack : null;
            bool melee = attack != null
                && attack.m_attackType != Attack.AttackType.Projectile
                && attack.m_attackType != Attack.AttackType.TriggerProjectile;
            if (melee) _humanoid.StartAttack(animal, false);

            var hit = new HitData();
            hit.m_point = animal.GetCenterPoint();
            hit.m_dir = (animal.transform.position - transform.position).normalized;
            hit.m_hitType = HitData.HitType.PlayerHit;
            hit.SetAttacker(_humanoid);
            hit.m_damage.m_slash = animal.GetMaxHealth() * 2f;
            animal.Damage(hit);

            // Remember where it fell. The drops are collected on a later tick by
            // StoreProducts, and this is what lets meat be stored for a species that
            // eats meat — outside a fresh cull spot the herd's own feed is left
            // alone so the feeding chore doesn't collect what it just put down.
            _cullDrops.Add(new KeyValuePair<Vector3, float>(animal.transform.position, Time.time + CullDropSeconds));
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

        // ---- Provisioning: Fermenter ----------------------------------------

        private bool ServiceFermenter(Fermenter fermenter)
        {
            if (fermenter == null) return false;

            switch (fermenter.GetStatus())
            {
                case Fermenter.Status.Ready:
                    // Tap — vanilla drops the finished meads by the fermenter.
                    fermenter.Interact(_humanoid, false, false);
                    ClearSpeech();
                    return true;
                case Fermenter.Status.Fermenting:
                    ClearSpeech(); // brewing in progress, nothing to do
                    return true;
                case Fermenter.Status.Exposed:
                    Say("The brew is exposed to the sky!");
                    return false;
            }

            // Empty: load a fermentable base from a chest.
            var brewChest = ChoreStorage.NearestSource(fermenter.transform.position, OwnerId);
            var chest = brewChest != null ? brewChest.GetInventory() : null;
            if (chest == null) { Say("I have no chest to brew from!"); return false; }

            foreach (var item in new List<ItemDrop.ItemData>(chest.GetAllItems()))
            {
                var name = item.m_dropPrefab != null ? item.m_dropPrefab.name : null;
                if (name == null || !fermenter.IsItemAllowed(name)) continue;

                // Mirror the smelter pattern: stage the item in the companion's
                // own inventory, then hand it to the station's add method. The
                // staging is REQUIRED here (unlike the smelter's QueueOre) because
                // Fermenter.AddItem consumes the item from the user's inventory.
                ChoreStorage.ClaimForWrite(brewChest);
                if (chest.RemoveItem(item, 1))
                {
                    var moved = item.Clone();
                    moved.m_stack = 1;
                    _humanoid.GetInventory().AddItem(moved);
                    if (fermenter.AddItem(_humanoid, moved)) { ClearSpeech(); return true; }
                }
            }

            Say("I have nothing to brew!");
            return false;
        }

        // ---- Provisioning: Cooking Station ----------------------------------

        private bool ServiceCooking(CookingStation cooker)
        {
            if (cooker == null) return false;

            // 1) Pull any done item off first, so cooked food doesn't burn.
            //    Use OnInteract, NOT Interact: for stations with an "add food"
            //    switch (e.g. the Stone Oven) Interact() early-outs to the switch
            //    and never collects, so the food just sits and burns. OnInteract()
            //    is the actual worker (it's what Interact calls on switch-less
            //    stations, and what the oven's food switch ultimately invokes) and
            //    fires the RPC_RemoveDoneItem that spawns the finished food.
            // Clear EVERY done slot in one visit, not one per 5 s tick: a full rack
            // would otherwise burn from the bottom up while the ally collected the
            // top one and walked away. OnInteract takes one item per call, so it is
            // called until the rack is clear (bounded by the slot count so a station
            // that never reports clear can't spin).
            if (cooker.HaveDoneItem())
            {
                int guard = cooker.m_slots != null ? cooker.m_slots.Length + 1 : 8;
                while (cooker.HaveDoneItem() && guard-- > 0)
                    cooker.OnInteract(_humanoid);

                ClearSpeech();
                return true;
            }

            // 2) Fuel (only stations that use it, e.g. the iron cooking station).
            if (cooker.m_fuelItem != null && cooker.GetFuel() < cooker.m_maxFuel)
            {
                var fuelContainer = ChoreStorage.NearestSource(cooker.transform.position, OwnerId);
                var fuelChest = fuelContainer != null ? fuelContainer.GetInventory() : null;
                var fuelName = cooker.m_fuelItem.name;
                if (fuelChest != null)
                {
                    foreach (var item in new List<ItemDrop.ItemData>(fuelChest.GetAllItems()))
                    {
                        if ((item.m_dropPrefab != null ? item.m_dropPrefab.name : null) != fuelName) continue;
                        ChoreStorage.ClaimForWrite(fuelContainer);
                        if (fuelChest.RemoveItem(item, 1))
                        {
                            cooker.SetFuel(cooker.GetFuel() + 1f);
                            ClearSpeech();
                            return true;
                        }
                    }
                }
            }

            // 3) Add raw food to a free slot. Only stations that actually require a
            //    fire (campfire-style) are fire-gated — calling IsFireLit() on one
            //    that doesn't (e.g. the Stone Oven, m_requireFire=false) NREs,
            //    because its fire-check points are left unconfigured. So we mirror
            //    vanilla's own guard and skip the check when no fire is required.
            if (cooker.m_requireFire && !cooker.IsFireLit() && !TendFire(cooker))
            {
                Say("The cooking fire is out!");
                return false;
            }
            if (cooker.GetFreeSlot() < 0) { ClearSpeech(); return true; } // all slots cooking

            var cookChest = ChoreStorage.NearestSource(cooker.transform.position, OwnerId);
            var chest = cookChest != null ? cookChest.GetInventory() : null;
            if (chest == null) { Say("I have no chest to cook from!"); return false; }

            foreach (var item in new List<ItemDrop.ItemData>(chest.GetAllItems()))
            {
                var name = item.m_dropPrefab != null ? item.m_dropPrefab.name : null;
                if (name == null || cooker.GetItemConversion(name) == null) continue;

                // Staging is REQUIRED here: CookingStation.CookItem consumes the
                // item from the user's inventory.
                ChoreStorage.ClaimForWrite(cookChest);
                if (chest.RemoveItem(item, 1))
                {
                    var moved = item.Clone();
                    moved.m_stack = 1;
                    _humanoid.GetInventory().AddItem(moved);
                    if (cooker.CookItem(_humanoid, moved)) { ClearSpeech(); return true; }
                }
            }

            Say("I have nothing to cook!");
            return false;
        }

        // A campfire-style cooking station only cooks over a lit fire, and a fire
        // burns down. Reporting "the cooking fire is out!" and stopping there made
        // the chore only half a chore: the ally would tend the food and leave the
        // player to tend the flame under it. So the cook keeps its own fire in.
        //
        // The fire is found through the station's OWN fire-check points — the
        // transforms vanilla tests for a burning EffectArea (CookingStation.
        // IsFireLit) — so we look exactly where the game looks rather than guessing
        // a radius around the grill.
        //
        // Refuelling goes through Fireplace.AddFuel, which invokes an RPC and is
        // therefore safe from any client; relighting invokes the same RPC_ToggleOn
        // that Fireplace.Interact does. Neither touches the local player, which
        // matters because this runs on whichever machine owns the companion — often
        // the dedicated server, where there is no local player at all.
        //
        // Returns true when the fire is now burning (or will be within the moment).
        private bool TendFire(CookingStation cooker)
        {
            var fire = FindFireplace(cooker);
            if (fire == null) return false;

            var nview = fire.m_nview;
            if (nview == null || !nview.IsValid()) return false;

            float fuel = nview.GetZDO().GetFloat(ZDOVars.s_fuel);

            // Out of wood: fetch one from a chest by the station.
            if (!fire.m_infiniteFuel && fire.m_fuelItem != null && Mathf.CeilToInt(fuel) < fire.m_maxFuel)
            {
                var fuelName = fire.m_fuelItem.name;
                foreach (var container in ChoreStorage.Nearby(cooker.transform.position, OwnerId))
                {
                    var inv = container.GetInventory();
                    if (inv == null) continue;

                    foreach (var item in new List<ItemDrop.ItemData>(inv.GetAllItems()))
                    {
                        if ((item.m_dropPrefab != null ? item.m_dropPrefab.name : null) != fuelName) continue;

                        ChoreStorage.ClaimForWrite(container);
                        if (!inv.RemoveItem(item, 1)) continue;

                        OpenChest(container);
                        FaceToward(fire.transform.position);
                        fire.AddFuel(1f);
                        PlayEffects(fire.m_fuelAddedEffects, fire.transform);
                        ClearSpeech();
                        return true;
                    }
                }

                if (fuel <= 0f)
                {
                    Say($"The fire needs {FuelDisplayName(fire)}!");
                    return false;
                }
            }

            // Fuelled but off (a hearth the player turned off) — light it again.
            if (!fire.IsBurning() && fuel > 0f)
            {
                FaceToward(fire.transform.position);
                nview.InvokeRPC("RPC_ToggleOn");
                ClearSpeech();
                return true;
            }

            return fire.IsBurning();
        }

        // The fireplace under a cooking station, found via the station's own
        // fire-check transforms (falling back to the station itself when a prefab
        // leaves them unset).
        private static Fireplace FindFireplace(CookingStation cooker)
        {
            const float FireSearchRadius = 2.5f;

            var points = cooker.m_fireCheckPoints;
            if (points != null && points.Length > 0)
            {
                foreach (var point in points)
                {
                    if (point == null) continue;
                    var found = NearestFireplace(point.position, FireSearchRadius);
                    if (found != null) return found;
                }
                return null;
            }

            return NearestFireplace(cooker.transform.position, FireSearchRadius);
        }

        private static Fireplace NearestFireplace(Vector3 pos, float radius)
        {
            Fireplace best = null;
            float bestDist = float.MaxValue;

            foreach (var hit in Physics.OverlapSphere(pos, radius))
            {
                var fire = hit.GetComponentInParent<Fireplace>();
                if (fire == null) continue;

                float d = Vector3.Distance(pos, fire.transform.position);
                if (d < bestDist) { bestDist = d; best = fire; }
            }
            return best;
        }

        private static string FuelDisplayName(Fireplace fire)
        {
            if (fire != null && fire.m_fuelItem != null && fire.m_fuelItem.m_itemData != null
                && fire.m_fuelItem.m_itemData.m_shared != null && Localization.instance != null)
                return Localization.instance.Localize(fire.m_fuelItem.m_itemData.m_shared.m_name);
            return "wood";
        }

        // ---- Storing what the chore produces ---------------------------------
        //
        // Stations spit their output onto the ground: a smelter drops bars by its
        // outlet, a tapped fermenter drops meads, a cooking station pops the
        // finished food off the rack. Left alone that piles up until a player walks
        // over and collects it by hand — exactly the tedium chores exist to remove.
        // So before working the round again, a worker files its output in a chest
        // (docs/Ally-Chores.md), searching for that chest from where the item
        // actually lies rather than from the post.
        //
        // WHAT COUNTS AS A PRODUCT is decided by the stations' own conversion lists,
        // never by "whatever is lying around": an ally must not pocket the ore you
        // dropped beside its smelter, or the mead you fumbled walking past. The pen
        // is the one exception — husbandry produce is simply what turns up in the
        // pen (eggs, and what a culled animal leaves) — so there the rule is
        // inverted: take anything EXCEPT the food the ally itself just dropped for
        // the animals, which it must never pick straight back up. A fresh CULL SPOT
        // suspends even that exclusion, which is how wolf meat reaches the chest
        // instead of being handed back to the wolves.
        //
        // Returns false when products are waiting and nothing in range will take
        // them; the caller ends the tick, having said so.
        private bool StoreProducts()
        {
            var wanted = ProductNames();
            if (wanted == null) return true;   // Farm stores as it harvests

            var feed = _kind == ChoreKind.Husbandry ? AnimalFoodNames(_post) : null;
            PruneCullDrops();

            int stored = 0;
            foreach (var hit in Physics.OverlapSphere(_post, WorkRadius))
            {
                if (stored >= MaxDepositsPerTick) break;

                var drop = hit.GetComponentInParent<ItemDrop>();
                if (drop == null || !drop.CanPickup(false)) continue;

                var data = drop.m_itemData;
                if (data == null || data.m_shared == null) continue;

                var where = drop.transform.position;

                // An empty wanted-set means "anything in the pen" (see above).
                if (wanted.Count > 0 && !wanted.Contains(data.m_shared.m_name)) continue;
                if (feed != null && feed.Contains(data.m_shared.m_name) && !IsFromCull(where)) continue;

                var nview = drop.GetComponent<ZNetView>();
                if (nview == null || !nview.IsValid()) continue;
                if (!ClaimDrop(drop)) continue;   // another worker already banked it

                var bin = StoreProduct(data, data.m_stack, where);
                if (bin == null) { SayNoStorage(where); return false; }

                OpenChest(bin);
                FaceToward(bin.transform.position);
                // Take ownership before removing the networked world object.
                nview.ClaimOwnership();
                ZNetScene.instance.Destroy(drop.gameObject);
                stored++;
            }

            if (stored > 0) ClearSpeech();
            return true;
        }

        // Store one product, looking for a chest around the ITEM first and then
        // around the POST.
        //
        // The fallback is load-bearing now that a patch (20 m) is twice the chest
        // radius (10 m): a crop at the far edge of a field has no chest within reach
        // of itself, and searching only from the item made the farmer report "I have
        // no chest to store this!" for the whole outer ring of its own field while
        // the chest sat next to its post. Item-first keeps the original intent
        // — put it away near where you found it — without that failure.
        private Container StoreProduct(ItemDrop.ItemData item, int stack, Vector3 where)
        {
            return ChoreStorage.Store(item, stack, where, OwnerId)
                ?? ChoreStorage.Store(item, stack, _post, OwnerId);
        }

        private Container StoreProduct(GameObject itemPrefab, int amount, Vector3 where)
        {
            return ChoreStorage.Store(itemPrefab, amount, where, OwnerId)
                ?? ChoreStorage.Store(itemPrefab, amount, _post, OwnerId);
        }

        // Is there anywhere at all, around either centre, that could take something?
        private bool AnyStorage(Vector3 where)
            => ChoreStorage.AnyStorageNearby(where, OwnerId) || ChoreStorage.AnyStorageNearby(_post, OwnerId);

        private void PruneCullDrops()
        {
            for (int i = _cullDrops.Count - 1; i >= 0; i--)
                if (Time.time > _cullDrops[i].Value) _cullDrops.RemoveAt(i);
        }

        private bool IsFromCull(Vector3 pos)
        {
            foreach (var spot in _cullDrops)
                if (Vector3.Distance(spot.Key, pos) <= CullDropRadius) return true;
            return false;
        }

        // The shared names the stations in this patch turn their inputs INTO. Null
        // means the chore has no ground-borne product to collect; an empty set means
        // "anything found in the work radius" (the pen).
        private HashSet<string> ProductNames()
        {
            var set = new HashSet<string>();
            switch (_kind)
            {
                case ChoreKind.Smelter:
                    // Caste-filtered: an Ice Mage must not sweep up the smelter's
                    // bars, nor a Fire Mage the refinery's eitr.
                    foreach (var s in InPatch<Smelter>())
                        if (MayWork(s) && s.m_conversion != null)
                            foreach (var conv in s.m_conversion) AddOutput(set, conv != null ? conv.m_to : null);
                    return set;

                case ChoreKind.Provisioning:
                    // Variant-filtered, for the same reason the Fire/Ice split is:
                    // a brewer must not be sweeping up the oven's bread.
                    foreach (var c in InPatch<CookingStation>())
                    {
                        if (!CoversVariant(c.gameObject)) continue;
                        if (c.m_conversion != null)
                            foreach (var conv in c.m_conversion) AddOutput(set, conv != null ? conv.m_to : null);
                        // Burnt food is still the station's output, and still
                        // shouldn't be left lying on the floor.
                        AddOutput(set, c.m_overCookedItem);
                    }
                    foreach (var f in InPatch<Fermenter>())
                    {
                        if (!CoversVariant(f.gameObject)) continue;
                        if (f.m_conversion != null)
                            foreach (var conv in f.m_conversion) AddOutput(set, conv != null ? conv.m_to : null);
                    }
                    return set;

                case ChoreKind.Husbandry:
                case ChoreKind.Haul:
                    // Empty set = anything loose in the patch. This IS the hauling
                    // chore: clearing the ground into chests and filing what the pen
                    // produces are the same sweep, and the only rule on it is that
                    // the herd's own feed is left where the feeding job put it (see
                    // StoreProducts).
                    return set;

                default:
                    return null;  // Farm
            }
        }

        private static void AddOutput(HashSet<string> set, ItemDrop output)
        {
            if (output != null && output.m_itemData != null && output.m_itemData.m_shared != null)
                set.Add(output.m_itemData.m_shared.m_name);
        }

        // Everything the tamed animals in this pen will eat. The feeding chore drops
        // food at their feet and waits for them to take it (ItemAlreadyDroppedNear),
        // so collecting these back up would be an infinite loop run by the ally's own
        // hand — out of the chest, onto the ground, back into the chest.
        private static HashSet<string> AnimalFoodNames(Vector3 center)
        {
            var set = new HashSet<string>();
            foreach (var hit in Physics.OverlapSphere(center, WorkRadius))
            {
                var c = hit.GetComponentInParent<Character>();
                if (c == null || !c.IsTamed()) continue;

                var mai = c.GetComponent<MonsterAI>();
                if (mai == null || mai.m_consumeItems == null) continue;

                foreach (var ci in mai.m_consumeItems)
                    if (ci != null && ci.m_itemData != null && ci.m_itemData.m_shared != null)
                        set.Add(ci.m_itemData.m_shared.m_name);
            }
            return set;
        }

        // One blocker line covering every "it won't fit" case, worded so the player
        // knows which problem they have: no storage built here at all, or storage
        // that has run out of room.
        private void SayNoStorage(Vector3 center)
        {
            Say(AnyStorage(center)
                ? "Every chest here is full!"
                : "I have no chest to store this!");
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
