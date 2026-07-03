# Ship Riding

Recruited companions in the default **Follow** stance get aboard the owner's ship:
they board through a ladder and then behave normally on deck — free to walk around,
follow the owner, and fight — while the ship's own moving-platform physics carries
them along.

Vanilla assets only — reuses the stock `Ship` and `Ladder` components plus the
ship's moving-platform physics. No new prefabs, models, sounds, or navmesh over the
moving deck.

Implemented by [`src/Companions/ShipRideAI.cs`](../src/Companions/ShipRideAI.cs),
one component attached to every companion alongside `DvergrCompanion` (in the
recruit, admin-spawn, and on-load restore paths).

## Behavior

The component does exactly one thing: **board the ship.** Everything else on deck
is plain vanilla AI.

1. **Board through the ladder.** While the owner is aboard a ship, the companion
   trails to the hull using the normal land Follow (pathing stops at the water's
   edge — there is no navmesh over a moving deck). When it reaches the base of a
   boarding **`Ladder`** on that hull, it climbs aboard: it snaps up to the ladder's
   deck target (`Ladder.m_targetPos`). On a laderless hull (e.g. a raft) it boards
   once it's alongside, stepping onto the deck next to the owner. It speaks a short
   *"Aboard…"* line the first time it climbs on.
2. **Free to walk around.** Once it is standing on the owner's ship the component
   leaves it completely alone — no seat, no position-locking, no idle suppression.
   Vanilla `MonsterAI` keeps following the owner, so the companion walks the deck
   freely and rides along with the hull via the ship's platform physics.
3. **Stay aboard.** If the companion ends up back in the water alongside the ship
   (e.g. it walked off an edge) while the owner is still aboard, it is lifted back
   on. It is never pinned to a spot.

There is no special disembark handling: when the owner steps off (or the ship
despawns) the companion simply stops boarding and its normal land Follow paths it
ashore.

## Water avoidance

Companions **avoid water by default** (`BaseAI.m_avoidWater = true`, set as a
baseline in `CommunionService.ApplyFreedState`) so they don't wander into the sea
on land. `ShipRideAI` clears that flag **only while the owner is aboard a ship**, so
the companion is free to swim out to the hull and board / stay with it; the moment
the owner is off the ship (or the companion isn't boarding-eligible), water
avoidance is restored.

## Scope / gating

- **Follow stance only.** A companion that is on a chore, dueling, feral, or told
  to hold a land post (**Guard** / **Standby**) does not board — and keeps avoiding
  water.
- **Owner-ZDO gated**, like chores: only the machine that owns the companion's ZDO
  performs the lift; other clients read the synced transform.
- **Transient — not persisted.** Nothing about being aboard is written to the ZDO;
  a relog / zone reload just re-boards on its own once the owner is aboard again.

## Known limitations

- **Deck movement is vanilla.** The moving deck isn't in the navmesh, so on-deck
  following/pathing is whatever vanilla `MonsterAI` manages on a moving platform —
  the companion may drift or bunch up; the water re-board keeps it from being lost.
- **Catching a moving ship.** Boarding only fires once the companion is within
  boarding range of a ladder/hull, which in practice means the ship is stopped or
  slow — the same as a player boarding. If you sail off before it climbs, it's left
  behind until you stop (or summon it — see [Companion-Totems.md](Companion-Totems.md)).

See [Testing.md](Testing.md) §13 for the in-game checklist. **Unverified in a live
session** — the boarding lift and the "free to walk around" deck behavior want
confirmation in-game (single-player first, then a two-client pass for the
owner-ZDO gating).
