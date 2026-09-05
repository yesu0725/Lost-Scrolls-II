using System.Collections.Generic;
using UnityEngine;

namespace LostScrollsII.Companions
{
    // Lets a companion work a door instead of standing at it.
    //
    // Vanilla creatures cannot open doors at all, which is invisible until you put
    // an ally to work: a chore worker walks its round, meets the workshop door and
    // stops, and the chore quietly reports the station as unreachable. The same
    // thing strands a Follow ally on the wrong side of your porch.
    //
    // WHY NOT Door.Interact. That is the method a player calls, and it is unusable
    // here: it runs `PrivateArea.CheckAccess`, which resolves the ward against
    // `Player.m_localPlayer` — the wrong player on a client, and NULL on a dedicated
    // server, where the chore is usually running. It also books a player statistic.
    // `Door.Open` is the half that matters: it invokes the vanilla `UseDoor` RPC,
    // the ZDO owner toggles the state, and every client animates it. So the ward is
    // checked HERE instead, for the companion's OWNER, the same way chest access is
    // (ChoreStorage.WardPermits).
    //
    // It also closes what it opened. Leaving a base standing open because an ally
    // walked through would be a real cost — doors are how you keep greydwarves out
    // — so each door this component opened is shut again once the companion has
    // moved away from it, and only if nobody is standing in the doorway.
    //
    // Attached alongside DvergrCompanion (like ShipRideAI) and driven only on the
    // ZDO owner, which is the client that runs the ally's MonsterAI.
    public class CompanionDoorOpener : MonoBehaviour
    {
        // How close the companion has to be for a door to count as "in my way".
        // Generous, because BaseAI.Follow stops 3 m short of its target and the ally
        // has to reach for the handle from wherever pathing parks it.
        private const float ReachRange = 3.5f;

        // Don't touch a door that isn't roughly between us and where we're headed.
        // Dot of (door - me) against (destination - me); 0 = a right angle.
        private const float ForwardDot = 0.15f;

        private const float TickInterval = 0.35f;

        // A door we opened is closed again once we are this far from it, after this
        // long, and only when nothing is standing in the doorway.
        private const float CloseDistance = 4f;
        private const float CloseDelay = 4f;
        private const float DoorwayClearance = 1.6f;

        private DvergrCompanion _companion;
        private MonsterAI _ai;
        private Humanoid _humanoid;
        private ZNetView _znv;
        private float _timer;

        // Doors this companion opened, and when. Only these are ever closed again.
        private readonly Dictionary<Door, float> _opened = new Dictionary<Door, float>();

        private void Awake()
        {
            _companion = GetComponent<DvergrCompanion>();
            _ai = GetComponent<MonsterAI>();
            _humanoid = GetComponent<Humanoid>();
            _znv = GetComponent<ZNetView>();
        }

        private void Update()
        {
            if (_ai == null || _humanoid == null || _humanoid.IsDead()) return;
            if (_znv != null && _znv.IsValid() && !_znv.IsOwner()) return;

            _timer += Time.deltaTime;
            if (_timer < TickInterval) return;
            _timer = 0f;

            CloseFinishedDoors();

            var destination = Destination();
            if (!destination.HasValue) return;

            OpenDoorsOnTheWay(destination.Value);
        }

        // Where is this companion trying to get to? A door is only opened when the
        // ally actually has somewhere to be — otherwise an idle Standby guard would
        // sit flicking the nearest door open and shut.
        private Vector3? Destination()
        {
            var follow = _ai.GetFollowTarget();
            if (follow != null) return follow.transform.position;

            var target = _ai.GetTargetCreature();
            if (target != null) return target.transform.position;

            return null;
        }

        private void OpenDoorsOnTheWay(Vector3 destination)
        {
            var here = transform.position;
            var toDestination = destination - here;
            toDestination.y = 0f;
            if (toDestination.sqrMagnitude < 0.01f) return;
            toDestination.Normalize();

            foreach (var hit in Physics.OverlapSphere(here, ReachRange))
            {
                var door = hit.GetComponentInParent<Door>();
                if (door == null || _opened.ContainsKey(door)) continue;
                if (!CanWork(door)) continue;
                if (!IsClosed(door)) continue;

                var toDoor = door.transform.position - here;
                toDoor.y = 0f;
                if (toDoor.sqrMagnitude < 0.01f) continue;
                if (Vector3.Dot(toDoor.normalized, toDestination) < ForwardDot) continue;

                // Same argument vanilla passes: the direction from the door to
                // whoever is using it, so the leaf swings away rather than into us.
                door.Open((here - door.transform.position).normalized);
                _opened[door] = Time.time;
                return; // one door per tick
            }
        }

        private void CloseFinishedDoors()
        {
            if (_opened.Count == 0) return;

            List<Door> done = null;
            foreach (var pair in _opened)
            {
                var door = pair.Key;
                if (door == null) { (done ?? (done = new List<Door>())).Add(door); continue; }

                if (Time.time - pair.Value < CloseDelay) continue;
                if (Vector3.Distance(transform.position, door.transform.position) < CloseDistance) continue;

                // Never swing it shut on somebody standing in it.
                if (!DoorwayClear(door)) continue;

                if (!IsClosed(door) && CanWork(door))
                    door.Open((transform.position - door.transform.position).normalized);

                (done ?? (done = new List<Door>())).Add(door);
            }

            if (done != null)
                foreach (var door in done) _opened.Remove(door);
        }

        private static bool DoorwayClear(Door door)
        {
            foreach (var hit in Physics.OverlapSphere(door.transform.position, DoorwayClearance))
            {
                var ch = hit.GetComponentInParent<Character>();
                if (ch != null && !ch.IsDead()) return false;
            }
            return true;
        }

        // A door the companion may work at all: it must be loaded, unlocked, and
        // inside a ward its OWNER is allowed in.
        private bool CanWork(Door door)
        {
            var nview = door.m_nview;
            if (nview == null || !nview.IsValid()) return false;

            // A key door is the player's own business — the ally carries no keys,
            // and vanilla would only bounce it with a "you need the key" message.
            if (door.m_keyItem != null) return false;

            if (door.m_checkGuardStone && _companion != null
                && !ChoreStorage.WardPermits(door.transform.position, _companion.OwnerId))
                return false;

            return true;
        }

        private static bool IsClosed(Door door)
        {
            var nview = door.m_nview;
            return nview != null && nview.IsValid() && nview.GetZDO().GetInt(ZDOVars.s_state) == 0;
        }
    }
}
