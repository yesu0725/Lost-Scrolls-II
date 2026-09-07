# Ally Chores

## Concept

Recruited allies can be assigned to handle tedious vanilla workstation tasks instead of following the player at all times. This is an AI behavior state, not a new object or asset.

## Proposed Design

- **Assignment**: player interacts with the ally near a vanilla workstation (smelter, charcoal kiln, kiln) to assign it there. Assignment state persists on the ally's ZDO.
- **Behavior loop**: while assigned, the ally periodically checks a linked container (e.g., a chest placed nearby) for fuel/ore, and feeds the workstation automatically on a timer — implemented as a custom AI state controlling existing interactable objects, not a new object type.
- **Unassign**: re-interacting with the ally (or moving far enough away) returns it to normal follow/combat behavior.
- **Possible later extension**: wood-cutting in a radius, or fetch-hauling between two player-marked points. Not committed for MVP — chores start with the single workstation-feeding behavior in Phase 4 and expand only if it proves fun.

## Mechanical Notes

- Must respect server authority — chore state changes should be ZDO-driven so the behavior is consistent for all connected clients, not just the assigning player.
- No UI assets — any chore-assignment feedback uses vanilla message/tooltip systems per [Technical-Constraints.md](Technical-Constraints.md).

## Implementation (Phase 4)

- Drives **any vanilla `Smelter`-component station** — Smelter, Blast Furnace, Charcoal Kiln, Eitr Refinery, Spinning Wheel — since they share that component. Assign/unassign via hotkey (default `H`, configurable) while hovering one; assigns the nearest recruited companion **of the matching caste** within a configurable radius (default 10m of the player).
- **Caste-gating** (`ChoreRules`): a station may only be tended by its mapped caste — Smelting stations (smelter/blastfurnace/charcoal) → **Fire Mage**; Refining stations (eitr/spinningwheel/windmill) → **Ice Mage** (matched by prefab name). Caste itself is detected from the Dvergr's prefab at recruit (see [Ally-Recruitment.md](Ally-Recruitment.md)). If no companion of the required caste is nearby, the assign attempt says so.
- Movement to the station reuses the same `MonsterAI.SetFollowTarget` mechanism Phase 2 uses for following the player, just pointed at the workstation instead. Detection/distance are **fully 3D**, so stations stacked above or below are seen and pathed to (the companion climbs to them); ranges were bumped (arrival 4.5 m; chest search is now `Chores/ChoreChestRadius`, default 10 m).
- Service loop runs every 5s once the companion is within range of the station: scans for a `Container` and tops up **both ore and fuel**, each **only up to capacity**.
- **Vanilla add VFX.** When the worker adds ore or fuel, it plays the station's own `m_oreAddedEffects` / `m_fuelAddedEffects` — the exact effect you'd see adding it by hand.
- **Respects capacity (bug fix).** The first version called `QueueOre` unconditionally and overfilled past `m_maxOre`. It now gates on `GetQueueSize() < m_maxOre` (ore) and `GetFuel() < m_maxFuel` (fuel), so it never overfills.
- **Fuel feeding now implemented.** For stations that need fuel (`m_fuelItem != null`, e.g. the Smelter's Coal), the worker pulls the matching fuel item and adds it via `SetFuel(GetFuel() + 1)` — which writes the ZDO the same way the working `QueueOre` does (verified by decompiling both). Fuel is fed before ore so the smelter never stalls. Charcoal-kiln-style stations with no `m_fuelItem` simply skip the fuel step.
- **Voiced blockers.** The worker says whatever is stopping it, via vanilla's NPC speech bubble (`Chat.SetNpcText`). **Throttled to once per minute, and only while the owner is within 20 m.** Input shortages name the **actual item** (from the station's own conversion list + `m_fuelItem`, localized) — e.g. *"I need more Wood!"* on a kiln, *"I need more Copper or Tin!"* on a smelter, *"I need more Coal!"* for fuel — not a generic "ore". The bubble clears once unblocked or unassigned.
- **Looks at its work + opens chests.** On each transfer the worker turns to face the station/target (`SetLookDir`) and pops the source chest's lid open (`Container.SetInUse`, vanilla open animation + effects), auto-closing ~1.5 s later so the player isn't locked out.
- **Passive while working.** A chore-assigned companion acquires **no** targets and is never alerted by a passer-by, so it can't wander off to fight monsters. It fights back only against something that has actually **hurt** it — a player or a creature — and returns to the station when that threat expires. Enforced by `DvergrCompanion.AllowsCombatTarget` (see [Ally-Commands.md](Ally-Commands.md); the old `m_alertRange = 0` approach never worked). Unassigning returns it to Follow at the owner's side.
- **Assignment is owner-only and caste-gated.** Only the player's own companion of the matching caste can be assigned.
- **Discoverability tooltip + claim display.** Chore-able stations show an `[<key>] Set companion to work` hint on hover (via `GetHoverText` postfixes — `Switch` gated to Smelter parents, plus `CookingStation`/`Fermenter`/`Container`), shown only when the local player owns a companion. Replaces the previously hidden `H` command. **If a companion is already tending that exact station**, the tooltip instead reads *"&lt;name&gt; is already working here."* (orange), shown to everyone regardless of ownership.
- **One worker per chore (claim registry).** `ChoreAI` keeps a static map of *target object → worker*. Assigning claims the target; unassigning / despawning (`OnDisable`) / the worker dying releases it. The assign path (`Plugin.HandleChoreAssignInput`) refuses a second companion on a claimed station — pressing `H` on a station **your own** ally already works **toggles it off** ("Ally returns to your side"); pressing it on a station another player's ally works just reports who's on it. The "find a companion" search also skips allies already busy on a chore (`freeOnly`), so a free one is chosen instead of yanking a working one. (This replaces the earlier idea of an in-world chore-selection menu — discarded in favor of the lighter hover-tooltip approach.)
- **Recall directly from the companion.** Pressing the chore key (`H`) while hovering **your own companion** unassigns whatever chore it's on ("Ally returns to your side") — you don't have to find/hover the station again (handy after the station moved out of view or you're across the base). On a companion with no chore it just says so; on another player's ally it reports "answers to another." A `[H] Recall from chore` hint is added to the companion's crosshair tooltip while it's assigned.
- **Farming chore (Support Mage) — plant + harvest.** *(Assignment reworked — see "Farming: the tool is the switch" above. It is no longer assigned from a crop or an item stand; the Cultivator in the ally's pack is the switch.)* Each tick the worker does one of:
  - **Harvest** — the first ready crop of *any* type (`Pickable.CanBePicked()`) → deposit `m_itemPrefab × m_amount` into a nearby chest (`Inventory.AddItem`), `SetPicked(true)`, play the crop's own `m_pickEffector` VFX.
  - **Plant** — if nothing is ripe, take any seed the chest holds and plant it on a free cultivated spot. Seed→sapling is resolved generically via `PlantingCatalog` (scans `ZNetScene` for prefabs with both a `Plant` and a `Piece`, keyed by the `Piece` resource requirement), so it plants *whatever* seed type is in the chest without hardcoding crop pairs. The spot search samples the radius, snaps Y to terrain (`ZoneSystem.GetGroundHeight`), requires `Heightmap.IsCultivated`, and keeps clear of existing plants / unharvested crops by the sapling's `m_growRadius`; the sapling is `Instantiate`d and the seed consumed, playing the sapling's vanilla `Piece.m_placeEffect`.
  - **Biome-gated:** it only plants a crop whose `Plant.m_biome` (a `Heightmap.Biome` flags mask) includes the biome at the target (`WorldGenerator.instance.GetBiome`), mirroring vanilla's own placement restriction — so Plains-only crops (barley/flax) won't be planted in the Meadows, etc. A wrong-biome seed voices *"These seeds won't grow in this land."*; each plant logs `[farm] planted '<sapling>' … (biome <Biome>)`.
  - This makes a field **self-sustaining**: harvested produce goes to the chest and seeds from the chest are replanted onto free tilled ground. Voiced blockers: "No crops are ready, and no seeds to plant.", "I have no chest for the harvest/seeds.", "The harvest chest is full!", "There's no room left to plant.", "I can't reach the field."
- **Husbandry chore (Rogue since 0.11; was the Support Mage's).** Hover a **tamed animal** and press `H` to assign a Support Mage to tend animals in radius. A hover **tooltip** (`[H] Set companion to tend the herd`) shows on tamed livestock, added on **both** `Tameable.GetHoverText` and `Character.GetHoverText` so it appears regardless of which Hoverable a creature uses — this is what makes it show on **Chicken/Hen** (they surface hover via `Character`, not `Tameable`), as well as boars/wolves/etc. It skips your own recruited allies. **One assigned mage tends the whole pen** — each tick it feeds *one* hungry tamed animal (`Tameable.IsHungry()`) within the 10 m radius, cycling through them across ticks, so a single mage keeps multiple animals fed (it is *not* one-mage-per-animal). It pulls a food that animal accepts (`MonsterAI.m_consumeItems`) from a nearby chest and drops it at the animal's feet (`ItemDrop.DropItem`) — vanilla auto-eats it, keeping them fed → happy → breeding. Won't pile up food (skips if an item is already on the ground nearby). Voiced blockers: "The animals aren't hungry.", "I have no food chest nearby.", "I have no food to give.", "I can't reach the pen."
  - **Claim is by RANGE.** Because one mage covers a whole pen, the "already working here" claim now applies to **every** tamed creature within an active feeder's work radius (`ChoreAI.WorkerCovering`, which now generalises that range rule to every chore domain), not just the single hovered animal: hovering any animal in a tended pen shows *"&lt;name&gt; is already working here."*, and the assign path refuses a second worker on that pen (pressing `H` on it toggles **your own** herder off instead).
- Every chore reuses the same `ChoreAI` component (domain-based: `Smelter` / `Provisioning` / `Farm` / `Husbandry`) and the same follow/speech/range scaffolding.
## Configuration (`Chores` section)

| Key | Default | What it governs |
|---|---|---|
| `ChoreAssignKey` | `H` | Assign / recall. On a workplace it **adds** a worker; on your own companion it recalls that one. |
| `ChoreAssignRadius` | 10 | How far from the *player* to look for a free companion of the right caste when assigning. |
| `ChoreWorkRadius` | 20 | How wide a **patch** one worker tends — every job of its kind within this of the post. Also the size of a field, a pen, and the product sweep. |
| `ChoreChestRadius` | 10 | How far a worker looks for a chest, both to **store** products and to **draw** inputs. |
| `ChoreStationReach` | 3.4 | How close it must be to work a station. **Hard floor 3.2** — `BaseAI.Follow` stops at 3 m, so anything at or under that is a distance the ally can never close. |
| `HusbandryCullLimit` | 3 | Grown animals of each kind a Rogue leaves in the pen. `0` disables culling. Below vanilla's breeding cap of 4 on purpose — see *The Rogue's corner*. |

Related, outside this section: `Companions/FollowEngageRange` (the combat leash),
`Companions/ShowMapPins`, `Interface/ContainerPanelOffset`.

## One worker, a whole workshop

**A chore is a patch of ground, not a single station.** You assign a worker by
hovering something; the position of that thing becomes its **post**, and from then
on it tends everything of its kind within `Chores/ChoreWorkRadius` (default
**20 m**) of the post, walking from job to job. One Fire Mage keeps a whole row of
smelters, kilns and blast furnaces going. One Support Mage runs the entire
kitchen — every cooker and every fermenter — as a single chore.

- **Taking a chore ends the stance the ally was in.** `BeginChore` clears the
  follow target and any combat target and patrols the post before optionally
  following an anchor. Setting `ChoreActive` alone was not enough — MonsterAI
  kept whatever follow target the stance had given it, so a Follow companion put
  to work carried on trailing its master. It went unnoticed while every chore
  was assigned *at* an object, because `BeginChore` then overwrote the follow
  target with the station; **farming**, posted on open ground with no anchor, is
  where it showed.
- **A provisioning mage keeps to one KIND of station.** Cooking stations, the
  stone oven and fermenters are three jobs sharing one domain, and a mage that
  roams between them is out of position whenever a rack finishes. It works the kind
  you posted it at and nothing else; you staff the rest of the kitchen with more
  allies (a patch is shared).
  - The split comes from vanilla's own **`CookingStation.m_requireFire`**, not a
    list of prefab names: the wood and iron stations cook over a fire, the stone
    oven is self-heating and leaves its fire-check points unconfigured (the same
    flag behind the old Stone Oven `IsFireLit` crash). "Wood and iron together,
    oven separate" therefore falls out of the game's own data.
  - The choice is persisted (`DE_ChoreVariant`) and gates **every** sweep that walks
    the patch — job search, product sweep, burning check, patch-has-work, restore
    — the same set of places the Fire/Ice split had to be applied to, for the same
    reason. Coverage reporting respects it too.
  - A chore saved before the split carries `Any` and **adopts the kind it restores
    onto**, so an existing kitchen ally settles into one job instead of staying
    unspecialised.
- **Food about to burn jumps the queue.** A cooking station holding a done item is
  served before distance is considered at all, a visit clears **every** done slot
  rather than one per tick, and while anything is cooked and waiting the round ticks
  at **1 s** instead of 5. Without all three a cook could walk past finished meat to
  go and load a fermenter, and a full rack burned from the bottom up while it
  collected the top one.
- **A station being torn down is skipped.** `InPatch<T>` requires a valid
  `ZNetView`: a destroyed piece stays reachable through its colliders for a frame or
  two after its ZDO has gone, and asking it anything walks into vanilla's own null
  dereference (`CookingStation.GetFreeSlot` reads the ZDO through `GetSlot`).
- **The round.** Each 5 s tick the worker picks the **nearest station in the patch
  that wants something**, walks to it (the same `SetFollowTarget` mechanism used
  for following the player), and services it once in reach. "In reach" is
  `Chores/ChoreStationReach` (default **3.4 m**), which has a hard floor of 3.2:
  `BaseAI.Follow` stops moving at 3 m from its target, so anything at or under that
  is a distance the ally can never close and the station would be reported
  unreachable instead.
- **A stuck station can't starve the rest.** If a station turns out to be
  unservable — no ore in any chest, a brew exposed to the sky — it is set aside
  for 60 s and the worker moves on to the next one, having said what was wrong.
  Same for a station it can't physically reach, which takes six consecutive ticks
  of getting nowhere before that verdict is passed (one trip across a 20 m patch
  is not a failure).
- **The caste split still applies inside a patch**, and at every point that walks
  it. A Fire Mage posted in a workshop that also holds an Eitr Refinery keeps to
  the heat stations and leaves the refinery for an Ice Mage. Four places have to
  ask, not one: `FindNextJob` (what it services), `ProductNames` (what it sweeps
  off the floor), `PatchHasWork` (whether the post is still worth holding) and
  `FindRestoreTarget` (what it resumes onto). Only the first of those asked at
  first, and the missing product filter is what made the two castes look like
  they were sharing each other's chores — a Fire Mage was stowing the
  refinery's eitr.
  - `RequiredCaste(Smelter)` tests the **refining** tokens first: they are
    substrings of a prefab name and `"smelter"` is the loosest of them, so a
    refining station whose name contained it would be claimed by the Fire Mage
    before the Ice Mage's own token was looked at. A one-line-per-prefab log
    (`[chore] station 'x' -> Caste`) makes a mismatch diagnosable.
  - The gate also runs **while working**, not only at assignment: a chore
    persists on the ZDO, so a record written before a domain changed hands would
    otherwise keep an ally on a chore its caste no longer does. `Smelter` is
    exempt from that domain-level check because its split is genuinely
    per-station, and `RequiredCaste(ChoreKind)` returns **null** for it rather
    than a plausible default.
- **A patch is shared, not owned.** Pressing the chore key on a workplace always
  puts **another** free ally on it, so a big workshop or a big pen can have two or
  three workers on the same ground. `ChoreAI.WorkersCovering` **reports** who is on
  a job (the tooltip names one, or counts several) rather than gating it. Recall is
  done on the companion, not the station: with several workers sharing a patch,
  "press `H` on this smelter to recall" has no unambiguous answer.
  - One hazard that opens: `ZNetScene.Destroy` defers destruction to the end of the
    frame, so two workers ticking in the same frame could both see the same loose
    item, both bank it, and duplicate it. A short-lived static `ClaimDrop` latch
    closes that.
- **The post is a position, not the object you hovered.** This is what lets the
  crop be harvested, the animal be culled and one furnace of six be torn down
  without ending the chore. A chore ends when the player recalls the worker, or
  when **nothing of its kind has existed in the patch for 60 s** — which is not
  the same as having nothing to do: a field with nothing ripe and a forge with
  nothing queued are both working sites the ally keeps standing at.

## Farming: the tool is the switch

A field is ground, not a station, so the farm chore is the one you hand to an
**ally** rather than point at.

- **Put a Cultivator in the companion's own pack** (`Y`). Its crosshair tooltip
  then offers `[H] Set companion to farm`, and pressing `H` posts it on the ground
  it is standing on. Pressing `H` again recalls it, and **taking the Cultivator
  back also ends the chore** — the tool is the licence, not just the switch that
  started the job. Crops and Cultivator-on-an-item-stand no longer carry farm
  hints; hovering one crop of many was always an odd way to say "work this bed".
- **The post has no anchor object.** `AssignToFarmHere` passes null deliberately:
  anchoring on the companion would have set the ally following itself. `BeginChore`
  then takes its current position as the post.
- **A field can be bare**, so the restore path accepts *cultivated ground under the
  saved post* as proof the workplace still exists. Without it, a freshly harvested
  bed would fail to restore and stand the ally down after 60 s.
- **One crop per field, while the seed lasts.** The field's own crop (whatever is
  already growing, `FieldSapling`) is tried first, so a farmer doesn't leave a
  patchwork of carrots, turnips and barley in one bed. But it is a **preference
  paid for out of the seed supply**, not a promise to leave ground bare: when that
  crop's seed runs out the ally plants whatever else it can reach and fills the
  field. Re-seeding to something else deliberately still works the way you would
  expect — plant the first of the new crop yourself.
  - This is also what makes root crops work properly. Vanilla has **two** saplings
    per root crop — `CarrotSeeds` grows a carrot, a `Carrot` grows more seed — so
    a carrot field with only `Carrot` in the chest has a perfectly good thing to
    plant, and the absolute version of the rule refused it.
- **A wrong-biome seed names its biome.** *"Barley won't grow here — it needs the
  Plains."* `Plant.m_biome` is a flags mask, so a crop with more than one home names
  them all, and one that grows anywhere doesn't recite nine biomes.
- **Harvesting is an armful, sized like the planting block.** The same
  `PlantBlockSide()` squared: 4 at level 1, rising on the same curve. A farmer that
  sows twenty-five in a pass and picks one every five seconds is a strange sort of
  expert. If storage fills mid-armful it keeps what it gathered and only complains
  when it could store nothing at all.
- **Planting is a block, on a grid.** Positions are snapped to a **world-aligned
  lattice** (a multiple of the crop's spacing), so successive batches line up with
  each other and with what is already in the ground. World space, not relative to
  the post, so two farmers on neighbouring beds agree with each other. How many go
  in at once is the ally's **level**: `2x2` to begin with, one more per side every
  three levels. The search prefers a complete block nearest the post (the bed grows
  outward from where you set the ally to work) and falls back to filling whatever
  single cells are free, so a nearly-full field is topped up rather than reporting
  "no room".
  - **Clearance is vanilla's own test, replicated.** `Plant.HaveGrowSpace` rejects
    any collider on `Default / static_solid / Default_small / piece / piece_nonsolid`
    within the grow radius unless it is an unhealthy plant — rocks, build pieces,
    wild growth, fallen logs. Our earlier check only looked for a `Plant` or an
    unharvested `Pickable`, which is far more permissive than the rule the game
    applies to the player, and that is why crops ended up sown into rocks. The right
    standard for a chore is "only where you could have planted it by hand", and the
    cheapest way to be sure is to run the game's rule rather than an approximation.
  - Spacing is the crop's own `m_growRadius` doubled **plus a margin**. Not
    cosmetic: a whole block is resolved against the world *before* any of it is
    planted, so its cells are never checked against each other, and exactly 2x the
    radius would leave every neighbour on the boundary.
  - Deciding whether a cell is soil needs **no ground height at all**:
    `Heightmap.IsPointInside` ignores y and `IsCultivated` samples the paint mask by
    world x/z. The cheap tests therefore run first and the raycast is paid only for
    cells that turn out to be soil — it used to be one raycast per cell, thousands
    per tick, nearly all on bare grass.
  - The search runs in **two passes**, and only the expensive half is rationed.
    Terrain paint and biome are cheap lookups, so pass one is unbudgeted and
    collects every cultivated cell in the patch; only the clearance test costs
    a physics query, so only that is budgeted. Charging every cell against one
    budget — including cells that simply aren't soil — meant a farmer posted
    at the edge of a field ran out on bare ground before reaching the far half,
    and then reported *"There's no room left to plant"* with visible soil a few
    metres away.
  - **Elevated ground is fine.** `ZoneSystem.GetGroundHeight` raycasts from
    y = 6000 straight down, so it answers correctly for raised terrain and the
    lattice point's own y never enters into it.
  - **Off-grid fallback.** A bed the *player* sowed by hand does not line up
    with the lattice, so its gaps can be real ground no lattice cell can reach.
    If the grid yields nothing, the ally plants a single half-cell-offset spot:
    tidiness is the preference, not a requirement.
- **It harvests a field, not a forage.** A crop is only picked when it stands on
  **cultivated soil**. `Pickable` is the same component behind stones, branches,
  dandelions, mushrooms and berry bushes, so without that test a farmer working a
  20 m patch in the Meadows stripped the wild ground around its bed. Nothing wild
  grows on tilled ground, so the one test separates the two.
- **Seeds may ride in the pack.** The worker looks in its own bag first and then in
  **every chest in range** — not just the nearest, which in a real base is the one
  the harvest goes into while the seed chest sits behind it. That single-chest
  lookup is what made seeds "not be recognised" and an empty bed report nothing to
  plant. The chest list is resolved once a second, since planting a block asks for
  it once per seed. Seeds are matched on their **shared name**, not their prefab name:
  `Inventory.Load` rebuilds a stored item by instantiating its prefab and keeping
  the clone's `ItemData`, so `m_dropPrefab` can be null or `"(Clone)"`-suffixed —
  which is exactly why pack seeds were invisible while the same seeds in a chest
  were found. This needed the reverse lookup `PlantingCatalog.SeedFor(sapling)`,
  because the chore decides the crop first and then goes looking for its seed,
  rather than picking a crop from whatever seed it happened to find.
- **Storing the harvest searches twice.** A patch is 20 m and the chest search is
  10 m, so a crop at the far edge of a field has no chest within reach of *itself*.
  `StoreProduct` tries the item's position and then the **post**. Searching only
  from the item is what made the farmer report *"I have no chest to store this!"*
  for the whole outer ring of its own field while the chest sat by its post
  ([Testing.md](Testing.md) §8h #7).

## Doors

Vanilla creatures cannot open doors, which is invisible until you put an ally to
work: a chore worker meets the workshop door and stops, and the chore reports the
station unreachable. `CompanionDoorOpener` (owner-driven, attached alongside
`DvergrCompanion` like `ShipRideAI`) opens a closed door standing between the ally
and where it is heading, and **closes it again** once the ally has moved away and
nobody is in the doorway — leaving a base propped open would be a real cost.

It calls **`Door.Open`, not `Door.Interact`.** Interact is the player's path and is
unusable here: it runs `PrivateArea.CheckAccess`, which resolves the ward against
`Player.m_localPlayer` — the wrong player on a client and **null on a dedicated
server**, where the chore usually runs — and it books a player statistic. `Open`
is the half that matters: it invokes the vanilla `UseDoor` RPC and the ZDO owner
toggles the state. The ward is therefore checked here instead, for the companion's
**owner**, through the same `ChoreStorage.WardPermits` the chests use. Locked doors
(`m_keyItem`) are left alone — the ally carries no keys, and vanilla would only
bounce it with a "you need the key" message.

## The Rogue's corner: the herd, and the ground

Husbandry and hauling are **one domain wearing two ids** (`ChoreAI.IsRogueDomain`).
Hover a tamed animal *or* a chest, press `H`, and the Rogue does **both jobs across
the same patch**: it feeds and culls whatever herd is in range, and it clears every
loose item in range into a chest. Which of the two you pointed at only decides what
it says it is starting (*"Ally tends the herd."* vs *"Ally clears this area."*), and
which id is persisted.

- **Hauling is the product sweep.** There is no separate hauling code any more:
  clearing the ground and filing what a chore produces are the same sweep
  (`StoreProducts`), and for the Rogue the product set is *everything loose in the
  patch*. So a Rogue posted by a chest with no animals is a plain hauler, one in a
  pen with nothing on the floor is a plain herder, and one in a pen with drops is
  both. Up to four items a tick, into the nearest chest that already holds that
  item.
- **It will not haul the herd's feed.** The single exclusion is the animals'
  `m_consumeItems`, because the feeding job puts food on the ground on purpose and
  waits for an animal to take it — collecting it back would be a loop run by the
  ally's own hand. A fresh **cull spot** suspends even that (see below). The
  practical consequence worth knowing: carrots dropped beside a chest that has
  boars in range are left alone.
- **No more "The animals aren't hungry."** A fed herd is not a blocker now that the
  same worker is also hauling — that line would be a worker announcing idleness
  while it was busy. Silence means it has nothing left to do.

Feeding and culling, unchanged from the 0.11 pass:

- **Feeding** is unchanged: one hungry animal per tick (`Tameable.IsHungry()`),
  fed a food it accepts (`MonsterAI.m_consumeItems`) pulled from a nearby chest
  and dropped at its feet for vanilla to auto-eat.
- **Culling.** Grown tamed animals are grouped by prefab; any group over
  `Chores/HusbandryCullLimit` (default **3**) has its surplus thinned. **Young are
  never culled** and a **pregnant** animal is spared while any other candidate
  exists. The drops are stored like any other chore product.
- **Why the default is 3.** Vanilla `Procreation` stops a pen breeding once
  `m_maxCreatures` (**4** by default, young included) are within
  `m_totalCheckRange`. A cull limit at or above that cap would therefore never
  fire — the pen simply stops at four and sits there. Below it, the pen keeps
  turning over: three adults plus a calf reaches the cap, the calf grows up, the
  surplus adult is culled, and breeding resumes. `0` disables culling entirely.
- **Melee only, structurally.** The requirement isn't a setting to be respected —
  nothing in the cull path goes through `MonsterAI`, which is the code that picks
  an attack and could pick a ranged one. The worker has to close to `CullRange`
  itself and the blow is landed directly. The visible swing is the ally's own
  equipped weapon through vanilla `Humanoid.StartAttack`, and only when that
  weapon's primary attack is not a projectile — a caste holding a staff lands
  the blow without an animation rather than casting across the pen.
- **`CullRange` is 4 m, not the ~2 m a swing covers**, because `BaseAI.Follow`
  **stops at 3 m**: a shorter range is one the follow logic can never close, and
  the worker would circle its quarry until it declared the animal unreachable.
  Same reason `ArrivalRange` is roomy.
- **One clean blow, not a fight.** The hit is scaled to the animal's max health so
  it kills outright. Livestock is butchered, not duelled — a boar that took six
  5-second ticks to die would spend that whole time running from its butcher.
- **Cull drops beat the feed exclusion.** Husbandry stores anything that turns up
  in the pen *except* the herd's own food (see *What counts as a product*), which
  would otherwise mean a culled wolf's meat is handed straight back to the wolves.
  So a kill records a **cull spot** — 4 m, 60 s — inside which even feed items are
  collected.
- **Culling grants no XP.** `Character.SetTamed` does not change `m_faction`, so
  `KillXpPatch`'s existing "no XP for Players-faction deaths" test never actually
  covered livestock. Without an explicit `IsTamed()` check a breeding pen would
  have become a renewable XP farm run by the ally itself.

## Storing the products of a chore

Every chore now ends in a chest instead of a pile on the ground. A smelter worker
stows its bars, a cook its meals, a brewer its tapped meads, a herder the eggs;
the farm harvest and the haul sweep already worked this way and now share the
same chooser.

- **Where it goes.** Nearest first, with one preference: **a chest that already
  holds the same item wins over an empty one**, so a smelter's copper keeps
  landing in the copper chest even when a nearer chest has a free slot. If the
  best chest is full the ally moves to the next nearest, and so on.
- **Never into its own pack.** A chore worker moves goods between the world and
  your storage; it does not hoard them. (Its pack is eight slots with a weight
  cap that would stop it fighting once full — see
  [Ally-Inventory.md](Ally-Inventory.md). `CompanionInventoryAI` already suspends
  its free-roam pickup while a chore is active, so the two never race.)
- **When nothing will take it** the ally says so — *"Every chest here is full!"*
  if there is storage but no room, *"I have no chest to store this!"* if there is
  no storage at all — and skips the rest of that tick. It deliberately stops
  *making* more of what it cannot put away.
- **Radius**: config `Chores/ChoreChestRadius`, default **10 m**. The same radius
  now governs where a chore **draws its inputs** from (ore, fuel, seed, raw
  food), which used to be a separate hardcoded 8 m — one number, so "the chests
  my ally can see" means one thing.
- **Up to 4 deposits per tick** (`MaxDepositsPerTick`), because a station can
  hand back a burst — a smelter empties its whole queue at once — and one item
  per 5 s tick would fall behind. The chore's own work still runs in the same
  tick.

### What counts as a chest

Not a hardcoded prefab list: **any placed container piece** qualifies — the four
vanilla chests, a cart, a barrel, and any modded storage built the same way. The
exclusions are what matter, because vanilla's `Container` component is also used
by several things that are emphatically not storage:

| Excluded | Why |
|---|---|
| **Incinerator** (Obliterator) | `Incinerator.m_container` is a real `Container` — without this an ally would feed the smelter's output into the one station whose job is destroying items. It is also where the [Communion Totem](Companion-Totems.md) ritual runs. |
| **TombStone / Corpse** | A player's gravestone, and the loot bag a destroyed chest leaves. Both are `Container`s. (Separate MonoBehaviours with no shared base, so both are named.) |
| **Ships** | Karve/longship cargo is a plain `Container` child of the hull — a placed piece by every other test. Judgment call: an ally shouldn't quietly load your longship, and a moored ship can drift out of range mid-chore. |
| **Companion packs** | `CompanionInventory` puts a `Container` on the creature itself. Covers this worker's own pack *and* any ally standing nearby. |
| **Dungeon / loot chests** | `Container`s with no `Piece` — filtered by the "must be a placed piece (or a cart)" rule rather than by name. |
| **Another player's personal chest** | Vanilla's `Container.CheckAccess`, asked for the companion's **owner** rather than whoever is looking. |
| **A chest inside someone else's ward** | Vanilla gates chest interaction on the guard stone (`m_checkGuardStone`); a companion must not be the way around one. Mirrors vanilla's `HaveLocalAccess` — `m_piece.IsCreator() \|\| IsPermitted(id)` — asked for the **owner** instead of the local player, since the static `CheckAccess` resolves against whoever is looking and is unanswerable at all on a dedicated server. **Both halves are required**: a ward's creator is *not* in its permitted list (`Setup` records only the creator's name; the id lives on the `Piece`), so an `IsPermitted`-only test refuses the very player who placed the ward — which is exactly what it did in a live session, blocking every chest in the owner's own base. |

**Companions share chests.** Availability is deliberately **not** gated on
`Container.IsInUse()`. That was tried and had to come out: our own lid animation
calls `Container.SetInUse`, so the instant one worker deposited, that chest went
invisible — to every other worker, and to itself for the rest of the same tick,
which is what made "chest full → use the next one" report no storage instead of
moving on. The gate bought nothing either: `SetInUse` is local, unreplicated, and
only runs on the ZDO owner, so another player's open chest never sets it here.
Vanilla lets several players share a chest and refreshes an open GUI from the
inventory's change event; companions share one the same way.

### What counts as a product

Decided by the **station's own conversion list**, never by "whatever is lying
around" — an ally must not pocket the ore you dropped beside its smelter, or the
mead you fumbled walking past.

| Chore | Product |
|---|---|
| Smelter family | every `m_conversion[].m_to` (bars, coal from a kiln, eitr…) |
| Cooking | every `m_conversion[].m_to`, plus `m_overCookedItem` — burnt food is still output, and still shouldn't be left on the floor |
| Fermenter | every `m_conversion[].m_to` (the tapped meads) |
| Husbandry / Haul (Rogue) | **inverted**: anything loose in the patch — eggs, what a culled animal leaves, and whatever is lying on the floor (this *is* the hauling chore) — **except** the animals' own `m_consumeItems`, since the feeding job puts that food down on purpose and collecting it back would be a loop run by the ally's own hand. A fresh cull spot suspends even that exclusion |
| Farm | the harvest, stored as it is picked (no ground stage) |


**Chest writes claim ownership first.** `Container.OnContainerChanged` only calls
`Save()` on the ZDO owner, and `CheckForChanges` reloads from the ZDO whenever a
newer revision arrives — so an add or remove made from a non-owning client lands
in a local copy and is then quietly overwritten. Vanilla dodges this by claiming
the chest when a player opens it; a companion has to do the same. This matters on
a **dedicated server**, where a chest is usually owned by the server rather than
by whichever client is running the chore.

- **Persists across relog / zone reload.** The chore (kind + the target's world **position**) is written to the companion's ZDO, the same mechanism recruit state uses. On respawn, `CommunionService.RestoreCompanion` re-adds `ChoreAI`, which re-resolves the station **by proximity to the saved position** (`Physics.OverlapSphere`) once its zone has loaded, then re-issues the assignment — so a freed ally returns to its station after a relog instead of idling at your side. If the target never reappears (e.g. it was removed), the stale record is cleared after ~60 s and the ally is handed back through the normal `Unassign` path — to **Standby**, not Follow.

  **A chore that ends by itself leaves the ally on Standby.** That covers both the restore give-up above and the mid-work case where the anchor is destroyed (station removed, crop harvested, animal died). Nobody called the ally off, so it holds the field or station it was posted to rather than setting off across the map after an owner who may be anywhere. Only a recall the **player** issued — `H` on the companion, or re-assigning a station it already claimed — returns it to Follow and your side.

  **The resolve gap matters.** `ChoreAI` needs a few frames (sometimes seconds, while the zone streams in) before it finds the station again, and for that whole window the companion would otherwise read as *not working* — which, in the usual Follow stance, means it walks back to its master and abandons the post. So `DvergrCompanion.Awake` marks a companion carrying a persisted chore as `ChoreActive` **immediately**, from the ZDO record alone, and puts it passive; `ChoreAI` takes that state over when it resumes the chore, or clears it on the ~60 s give-up. Its **stance** is persisted separately (ZDO `DE_Stance`, see [Ally-Commands.md](Ally-Commands.md)), so a worker that was on Guard or Standby comes back on that stance rather than Follow. *(Position — not the target's `ZDOID` — is used deliberately: ZDOIDs go through the connection/remap system and do not reliably survive a save/load, which made an earlier ZDOID-based attempt silently fail to restore. A `Vector3` round-trips cleanly, and stations don't move.)*
- **Keeps working when the owner leaves / logs out.** The service loop never depended on owner proximity (only the *voiced blocker* is range-gated), so a companion keeps tending its station while you're away. Work is gated to the **ZDO owner** (`ZNetView.IsOwner`) so it happens exactly once; when the assigning player logs out, ZDO ownership migrates to whoever still has the zone loaded (the server / another nearby player) and they pick the chore up seamlessly. **Engine limit:** Valheim does not simulate fully *unloaded* zones, so if no player is anywhere near the station it pauses until the zone reloads (then resumes from the persisted state) — true offline/idle-game simulation isn't possible within vanilla's world model.
- Grants no XP currently — the Phase 3 open question of whether chore time should count toward leveling was not resolved, just left at "no" by default.

## Open Questions (resolve after in-game verification)

- Does chore time count toward leveling XP, and at what rate relative to combat XP? (currently: no XP from chores)
- Can a chore-assigned ally still be called to fight if the player is attacked nearby, or is it strictly idle until manually reassigned? (currently: strictly idle/feeding until manually unassigned)
- ~~Should chore assignment persist across reload?~~ **Resolved — yes, implemented** (ZDO-persisted chore kind + target world **position**, re-resolved by proximity on spawn).
- ~~Where do the products of a chore go?~~ **Resolved — the nearest chest**, preferring one that already holds the same item (see *Storing the products of a chore*).
- **Provisioning — Fermenter (Support Mage).** Hover a **Fermenter** and press `H`. Per tick by `Fermenter.GetStatus()`: **Empty** → load a fermentable base from a nearby chest (`IsItemAllowed` → stage in companion inventory → `AddItem`); **Ready** → tap (`Interact`, vanilla drops the finished meads); **Fermenting** → wait; **Exposed** → voices "The brew is exposed to the sky!". Other blockers: "I have no chest to brew from!", "I have nothing to brew!".
- **Provisioning — Cooking Station (Support Mage).** Hover a **Cooking Station** and press `H`. Per tick: first pull any **done** item off (`HaveDoneItem` → **`OnInteract`**) so food doesn't burn; top up **fuel** if the station uses it (`m_fuelItem`, e.g. the iron cooking station); **tend the fire** (below); then add **raw food** to a free slot (`GetFreeSlot` + `CookItem`). The fire check (`IsFireLit`) only runs on stations that need a fire (`m_requireFire`). Blockers: "The fire needs Wood!", "The cooking fire is out!", "I have no chest to cook from!", "I have nothing to cook!".
  - **The cook keeps its own fire in.** Reporting that the fire was out and stopping made the chore half a chore — the ally tended the food and left the player to tend the flame under it. `TendFire` finds the fireplace through the station's own **`m_fireCheckPoints`** (the transforms vanilla itself tests for a burning `EffectArea`, so we look exactly where the game looks), feeds it wood from a chest via `Fireplace.AddFuel`, and relights a fuelled-but-off hearth with the same `RPC_ToggleOn` that `Fireplace.Interact` invokes. Both are RPCs and neither touches the local player — which matters, because this runs on whichever machine owns the companion, often the dedicated server where there is no local player at all.
  - **Why `OnInteract`, not `Interact`** (Stone Oven fix): on stations with an "add food" switch (the **Stone Oven**), `CookingStation.Interact()` early-outs to that switch and never collects, so finished food would sit and **burn**. `OnInteract()` is the actual worker — it's what `Interact` calls on switch-less stations and what the oven's food switch ultimately invokes — and it fires `RPC_RemoveDoneItem` to spawn the finished food. (Note: like vanilla collection, the food spawns on the ground by the oven; the worker's own product sweep chests it on the next tick. `OnInteract` also nudges the *local player's* cooking skill, a harmless side effect.)
- **Hauling (Rogue).** Hover a **chest** and press `H` — the Rogue is posted there and clears loose `ItemDrop`s across the patch into chests, four per 5 s tick, choosing the destination with the same rule every other chore uses (nearest chest that already holds the item, else nearest with room). It **stays at its post — it does not walk out to each item** (the walk-to-item/pickup-VFX choreography was tried and reverted at the user's request). It still pops the chest lid open on each deposit. Idle is silent. It was briefly retired at 0.11 and restored the same week, folded into the Rogue's husbandry domain so one worker does both — which is also why it no longer needs a *designated* chest: `ChoreStorage` picks one.
- ~~Farming **replanting** is the one remaining unbuilt piece (harvest-only shipped)~~ — **now implemented**: the farm chore plants seeds from the chest onto free cultivated ground (any crop type, via `PlantingCatalog`) as well as harvesting.

## Chore → caste mapping (approved)

| Caste | Domain | Stations / task |
|---|---|---|
| Fire Mage | Smelting | every Smelter, Blast Furnace and Charcoal Kiln in the patch |
| Ice Mage | Refining | every Eitr Refinery / Spinning Wheel / Windmill in the patch |
| Support Mage | Provisioning | the patch's **cookfires**, *or* its **ovens**, *or* its **fermenters** — whichever kind you posted it at |
| Support Mage | Farming | started by putting a **Cultivator** in the ally's pack; plants (one crop per field) and harvests → chest |
| Rogue | Husbandry **+** Hauling | feed the herd, cull its surplus, **and** clear every loose item in the patch into a chest |

*Husbandry moved from the Support Mage to the Rogue at 0.11 — it is no longer
only feeding, and culling is butcher's work. Hauling was briefly retired in the
same pass and then restored alongside it as one Rogue domain: neither job fills a
5-second tick on its own, and "tend this corner of my base" is one errand from
the player's side.*
