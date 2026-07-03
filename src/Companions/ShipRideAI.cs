using UnityEngine;

namespace LostScrollsII.Companions
{
    // Lets a Follow-stance companion get aboard the owner's ship (docs/Ship-Riding.md).
    //
    // Its ONLY job is boarding: while the owner is aboard a ship and the companion
    // has trailed (via the normal Follow) up to a boarding ladder on that hull, it
    // climbs aboard — snapping onto the ladder's deck target. Once on deck it is
    // left completely alone: vanilla MonsterAI keeps following the owner, so the
    // companion is free to walk around the deck, and the ship's own moving-platform
    // physics carries it along. No seats, no position-locking, no idle suppression.
    // If it ends up back in the water next to the ship (e.g. it walked off an edge)
    // while the owner is still aboard, it is lifted back on — but it is never pinned
    // to a spot.
    //
    // Vanilla assets only: reuses Ship / Ladder + the ship's platform physics — no
    // new prefabs, no navmesh over the moving deck.
    //
    // Owner-ZDO gated (like ChoreAI) so exactly one machine performs the lift; other
    // clients read the synced transform. Transient by design (nothing persisted).
    //
    // NEEDS IN-GAME VERIFICATION — see docs/Testing.md §13.
    public class ShipRideAI : MonoBehaviour
    {
        // How close (horizontal) the companion must get to a ladder's base before
        // it climbs aboard through it.
        private const float LadderBoardRange = 3.5f;
        // Fallback when a hull has no ladder: board once this close to the ship.
        private const float HullBoardRange = 5f;
        // Cooldown after a lift so the platform physics can settle before we might
        // decide it's "off the ship" again and re-board.
        private const float BoardCooldown = 1.5f;
        // Scan cadence for the (cheap) boarding check.
        private const float ScanInterval = 0.4f;

        private Character _character;
        private MonsterAI _ai;
        private ZNetView _znv;
        private DvergrCompanion _companion;
        private Rigidbody _body;

        private float _scanTimer;
        private float _lastBoardTime = -999f;
        private bool _wasAboard;

        private void Awake()
        {
            _character = GetComponent<Character>();
            _ai = GetComponent<MonsterAI>();
            _znv = GetComponent<ZNetView>();
            _companion = GetComponent<DvergrCompanion>();
            _body = _character != null ? _character.m_body : null;
        }

        // Only the machine that owns this companion's ZDO drives it (single-player
        // host, or whoever simulates the zone). Others read the synced position.
        private bool IsDriver => _znv == null || !_znv.IsValid() || _znv.IsOwner();

        // Boarding is only for a plain Follow companion — not one that's on a chore,
        // dueling, feral, or told to hold a land post (Guard / Standby).
        private bool Eligible =>
            _companion != null
            && _companion.Stance == CompanionStance.Follow
            && !_companion.ChoreActive
            && !_companion.DuelMode
            && !_companion.IsFeral;

        private void Update()
        {
            if (!IsDriver || _character == null || _ai == null) return;

            _scanTimer += Time.deltaTime;
            if (_scanTimer < ScanInterval) return;
            _scanTimer = 0f;

            // Not boarding-eligible (chore / duel / feral / holding a land post) →
            // avoid water like every other land moment.
            if (!Eligible) { _wasAboard = false; SetAvoidWater(true); return; }

            var owner = _companion.OwnerPlayer();
            var ship = owner != null ? FindShipCarrying(owner) : null;

            // Only let the companion into the water while the owner is aboard a ship
            // (so it can swim out to board / stay with it). Otherwise it avoids water.
            SetAvoidWater(ship == null);

            // Owner not on a ship (or gone) → nothing to do; land Follow handles it.
            if (ship == null) { _wasAboard = false; return; }

            // Already standing on the owner's ship → leave it be. It's free to walk
            // the deck and follow the owner via vanilla AI; physics carries it.
            if (_character.GetStandingOnShip() == ship) { _wasAboard = true; return; }

            // Just lifted aboard — give physics a moment before reconsidering.
            if (Time.time - _lastBoardTime < BoardCooldown) return;

            TryBoard(ship);
        }

        // Board when the companion (trailing the owner via normal Follow) has
        // reached a boarding ladder on the owner's ship — or, on a laderless hull,
        // simply gotten alongside it. Prefers climbing through the nearest ladder.
        private void TryBoard(Ship ship)
        {
            var pos = transform.position;

            Ladder bestLadder = null;
            float bestLadderDist = float.MaxValue;
            foreach (var ladder in ship.GetComponentsInChildren<Ladder>())
            {
                if (ladder == null) continue;
                float d = HorizontalDistance(pos, ladder.transform.position);
                if (d < bestLadderDist) { bestLadderDist = d; bestLadder = ladder; }
            }

            if (bestLadder != null && bestLadderDist <= LadderBoardRange)
            {
                var top = bestLadder.m_targetPos != null ? bestLadder.m_targetPos.position
                    : bestLadder.transform.position + Vector3.up * 1.5f;
                Board(top);
                return;
            }

            // Laderless hull (e.g. raft): board once alongside, next to the owner.
            if (bestLadder == null && HorizontalDistance(pos, ship.transform.position) <= HullBoardRange)
            {
                var owner = _companion.OwnerPlayer();
                var spot = owner != null ? owner.transform.position : ship.transform.position;
                Board(spot + ship.transform.right * 0.8f);
            }
        }

        // Lift onto the deck, then hand straight back to vanilla AI (the Follow
        // target is still the owner, so it just carries on walking the deck).
        private void Board(Vector3 deckPoint)
        {
            if (_body != null)
            {
                _body.position = deckPoint;
                _body.rotation = transform.rotation;
            }
            transform.position = deckPoint;

            _lastBoardTime = Time.time;
            if (!_wasAboard) _companion.AnnounceBoarded();
            _wasAboard = true;
        }

        private void SetAvoidWater(bool avoid)
        {
            if (_ai != null && _ai.m_avoidWater != avoid) _ai.m_avoidWater = avoid;
        }

        // Find the ship the player is currently aboard (authoritative: the ship's
        // own onboard-player list), scanning the live ship instances.
        private static Ship FindShipCarrying(Player owner)
        {
            if (owner == null || Ship.s_currentShips == null) return null;
            foreach (var ship in Ship.s_currentShips)
                if (ship != null && ship.IsPlayerInBoat(owner)) return ship;
            return null;
        }

        private static float HorizontalDistance(Vector3 a, Vector3 b)
        {
            a.y = 0f; b.y = 0f;
            return Vector3.Distance(a, b);
        }
    }
}
