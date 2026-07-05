# In-Game Test Plan

Persistent checklist for verifying Lost Scrolls II in a live Valheim session. Work top to bottom; later tests assume a recruited companion exists.

## Results log

**2026-06-30 — first live test pass.** Confirmed working in-game: §1 recruit + §2 mid-fight commune (the 3×-reported aggravated bug — **fixed**); §3 relog persistence (also exercised by §8f); all Companion care & control (§4 feed, §7 stance, §7e capability lines, §7f hover/rename, §7b pathfinding); all Leveling (§5, §6, §6b); §8 Smelter-family chore; §8f chore persistence; §7d chore claim tooltip; §11 admin `de_spawn`. Sections below are marked ✅ where passed; unmarked sections still need testing.

**2026-06-30 — feature change.** The **caste recruit-order gate** (which passed in the pass above) and the **pre-corrupted-camps** feature were **both dropped** at the user's request and replaced by §1b "the corruption awakens" (a message shown when an unrecruited Dvergr is provoked). Also fixed: §2c (ally no longer attacks its owner when the owner hits a wild Dvergr).

**2026-07-02 — chore suite pass.** All remaining chore mechanics confirmed working in-game: §8b Farming (plant + harvest, any type, biome-gated planting, Cultivator-on-item-stand trigger), §8c Tamed-animal feeding (incl. Chicken/Hen tooltip and claim-by-range), §8d Provisioning (Fermenter / Cooking Station / Stone Oven), §8e Hauling. **The full caste-gated chore system (Fire/Ice/Support/Rogue) is now verified.**

**2026-07-02 — §1b passed.** "The corruption awakens" message (shown when an unrecruited Dvergr is provoked) confirmed working in-game.

**2026-07-02 — §2c & §2d passed.** Freed allies no longer turn on the owner when the owner attacks a nearby wild Dvergr (`m_aggravatable` fix), and no longer attack the player's build pieces (`m_attackPlayerObjects` fix).

**2026-07-03 — §12–§15 passed.** Companion totems (seal & summon), ship riding, minimap pins, and portal-follow all confirmed working in-game. Their sections below are marked ✅.

**2026-07-03 — story/guidance verified.** §10b (the biome-descent lore beats — `distance`-triggered, incl. the StartTemple opening and the returning-player `ls_call_to_start` nudge), §10c (the recruit-order guide), and §10d (the Companion Handbook — command card, per-caste chore tips, ship/portal/level/totem tips) **all passed** in-game. The `distance`-over-`location_entered` choice and the `Charred*` / `incinerator` names held up.

**2026-07-03 — PvP/duel batch.** Fixed the "losing companion can't be healed by mead after a duel" bug (ownership: the feed's `SetHealth` is a no-op off-owner, and the cross-client subdue had left the loser's ZDO owned by the winner's client — feeding now claims ownership first). Added: (1) a player-struck companion now also turns on the *attacker's* companions, not just the attacking player; (2) when one player attacks another (both PvP on), the attacked player's companions turn on the aggressor **and** its companions; (3) a duel win is now broadcast as a chat shout; (4) a `[J]` duel hint shows on your companion when another player's companion is in range. **These four are unverified in a live session — need a two-player pass (see §7c/§9).**

**2026-07-05 — companion inventory pass.** The new per-companion inventory system (§16) was built and iterated over three feedback rounds, all confirmed in-game:
- **Passed:** §16b pickup; §16c food (eats one at a time, fed icon, HP readout confirms the max-HP bump); §16e2 the **ComfyQuickSlots** panel-gap fix ("inventory gap test passed"); §16f drop-pack-on-death; §16h totem carries the pack; §16i wood-portal block on prohibited companion cargo; resist status removed from the pack panel (shown above the companion only).
- **Fixed during the pass:** name field moved out from under the panel title; encumbrance now truly stops attacks (enforced every frame); health mead now keeps sipping across the full 35%→90% window; resist meads now show an icon (hud) and genuinely resist; **and the `Y`-rename field now suppresses all game binds while focused** (a `ZInput.GetButtonDown` prefix — the earlier gate only blocked our own keys, so pressing `E` still closed the panel).

## Setup

1. Build deploys to all three targets automatically (Steam `BepInEx/plugins/LostScrollsII`, the r2modman profile, and the dedicated server). Launch whichever you're testing.
2. Confirm the plugin loaded: BepInEx console/log shows the `com.lostscrollsii` plugin and no Harmony patch errors on startup.
3. Keep the **BepInEx log open** — several tests are confirmed by `[feed]` / `[xp]` log lines, not just on-screen behavior.

### Default keys (configurable in the BepInEx config)

| Key | Action |
|---|---|
| `G` | **Communion** on a subdued Dvergr / **Feed** on a recruited companion (same key, context-sensitive) |
| `H` | Assign nearest companion to a workstation chore (within 10 m) |
| `J` | Select companion for a duel |
| `E` | Cycle stance: Follow → Guard → Standby (owner-only) |
| `Y` | Open a companion's **inventory** (chest-like panel that also renames it) (owner-only) |
| *(item use)* | Use a **companion totem** from the hotbar/inventory to summon it back; seal companions via the **Incinerator** + Wisps (no key) |

---

## 1. Recruitment — the Communion Rite  ✅ PASSED

1. Find a Dvergr (Mistlands). Attack it down to **≤ 20% HP without killing it**.
2. Hover it — crosshair tooltip should show a `[G] Communion` hint.
3. Press `G`. Expect the message **"The shadow's grip loosens."**
4. The Dvergr should stop being hostile and begin following you.

**Pass:** message shown, faction flipped, it follows and no longer attacks.

## 1b. The corruption awakens (message on aggravation)  ✅ PASSED

Replaces the dropped recruit-order and pre-corrupted-camp features.

1. Approach a neutral, **unrecruited** Dvergr and provoke it (attack it, or trigger its camp).
2. The instant it turns hostile, expect a center-screen line such as **"Something old stirs in it — the corruption was never truly gone. Roused, it turns on you."** (one of a small rotating set).
3. Provoke **several** Dvergr at once (a camp) → you should see **one** line, not one per Dvergr (≈6 s throttle).
4. Provoke another Dvergr a while later → a (possibly different) line shows again.
5. A Dvergr aggravated **far away** from you (out to ~40 m) should **not** trigger the message.
6. Freed allies must **never** trigger it (they're non-aggravatable now anyway).

**Pass:** message fires on a fresh aggravation of an unfreed Dvergr near you, reads well, rotates, and doesn't spam when several wake together.

## 2. Mid-fight commune (the aggravated-flag fix)  ✅ PASSED

This is the bug that was reported and fixed three times — test it deliberately.

1. Aggravate a Dvergr so it is **actively attacking you**.
2. Knock it to ≤ 20% HP while it's still mid-fight.
3. Press `G` to commune **during the fight**.

**Pass:** it stops attacking **immediately** on recruit.
**If it still swings once** before calming, note whether it's a single already-committed attack vs. continued hunting — the former is a minor animation-timing issue, the latter means the fix didn't hold (capture the log).

## 2c. Ally doesn't turn on the owner when owner attacks a wild Dvergr  ✅ PASSED

Regression test for the `m_aggravatable` fix.

1. Stand near a **recruited** companion with an **unrecruited** Dvergr also in range.
2. Attack the **wild** Dvergr.

**Pass:** the companion **never attacks you (the owner)**. It may help fight the wild Dvergr (fine), but no hit lands on the owner.
**Fail signal:** the companion swings at the owner — the freed ally is still being re-aggravated by the wild Dvergr's `AggravateAllInArea` (i.e. `m_aggravatable` wasn't cleared on this spawn; check it also holds **after a relog**, since the flag is re-applied on restore).

## 2d. Ally doesn't attack the player's build pieces  ✅ PASSED

Regression test for the `m_attackPlayerObjects` fix.

1. Recruit a Dvergr and lead it into/around your **base** (walls, workbench, other build pieces).
2. Let it idle in Follow, and also try Guard/Standby near structures.

**Pass:** the companion **never attacks build pieces** — no hits on walls/benches, no structure damage.
**Fail signal:** it swings at a wall/workbench — `m_attackPlayerObjects` wasn't cleared. Confirm it also holds **after a relog** (re-applied on restore).

## 3. Persistence across relog  ✅ PASSED

1. Recruit a Dvergr.
2. **Log out and back in** (or restart the server / walk far enough to unload and reload the chunk).
3. Hover the companion.

**Pass:** hover shows `Companion · Lv X`, **not** `[G] Communion`; it's still your ally and doesn't attack.
**Fail signal:** it reverts to a recruitable subdued Dvergr — the restore patch (`MonsterAI.Start` → `RestoreCompanion`) isn't firing.

## 4. Feeding with health mead (+ heal amount + VFX)  ✅ PASSED

1. Brew/obtain a **healing mead** (minor/medium/major) and hold it in inventory.
2. Damage your companion (let an enemy hit it, or duel it down a bit).
3. Hover the companion — tooltip should show `[G] Feed`. Press `G`.

**Pass:**
- Message **"Your ally drinks deep."**
- One mead consumed from inventory.
- Companion HP rises by **the same flat amount the mead would heal you** (not a full bar).
- The mead's **own healing VFX** (green burst) plays on the Dvergr.

**If "You have no health mead to offer."** appears while holding a mead: check the BepInEx log for `[feed] Consumable not matched...` lines — they print each consumable's name and consume-effect type so the detection can be corrected.

**Note (2026-07-03):** the heal now goes through `Character.Heal` (was `SetHealth`), which routes to the ZDO owner over RPC. Re-confirm the single-player owner-feed still heals + shows VFX; the cross-client cases are §4b.

## 4b. Feeding is shared — any player can feed any companion  ✅ PASSED

Feeding is deliberately **not** owner-gated (unlike stance/rename/chore/duel).

1. Player B holds a health mead and hovers **Player A's** companion, presses `G`.
2. **Pass:** the companion's HP rises (heal applied on A's client via `RPC_Heal`), B's mead is consumed, and B sees *"&lt;name&gt; drinks deep."* (not "This companion answers to another").
3. **Duel-loser heal (the bug fix):** right after A's companion **loses a duel** (bottoms out at ~5% HP), have **A** feed it — HP must rise. Previously nothing happened because the loser's ZDO was owned by the winner's (B's) client; `Character.Heal` now routes the heal to whoever owns it.
4. **No follow disruption:** after B feeds A's companion, A's companion should **keep following A normally** — feeding must **not** transfer ownership (it uses `Heal`'s RPC, never `ClaimOwnership`). Confirm the ally doesn't stall or start trailing B.

## 5. Leveling — biome-/HP-scaled XP  ✅ PASSED

1. With a companion following, kill creatures in **different biomes**.
2. Watch the log for `[xp] Kill in <Biome>: '<creature>' maxHP=… / ref … -> N XP (cap …)`.

**Pass / what to verify:**
- Meadows kills give tiny XP (~1–5), Ashlands kills give large XP (~up to 100).
- Tougher creatures in a biome give more than weak ones; **star creatures give more** (higher live `maxHP`).
- **Reference HP sanity check:** the log prints each creature's real `maxHP`. Compare the toughest common creature you actually fight per biome against the reference values below — if a reference is off, tell me and I'll adjust the 7 numbers (everything else scales from live HP and is exact).

  | Biome | Cap | Reference HP |
  |---|---|---|
  | Meadows | 5 | 25 |
  | Black Forest | 12 | 150 |
  | Swamp | 22 | 150 |
  | Mountain | 35 | 100 |
  | Plains | 50 | 80 |
  | Mistlands | 75 | 200 |
  | Ashlands | 100 | 200 |

3. **Player-kill XP** (PvP / friendly-fire test, if feasible): a player death near a companion grants a flat 50 XP.

## 6. Level cap, curve, and the star badge  ✅ PASSED

1. Keep farming XP on one companion. It should level **slowly** (100 XP for level 2, rising to 3,200 for level 10; 11,500 total).
2. Each level-up logs `Dvergr <caste> reached level N`.

**Pass:**
- Floating name above the companion shows a gold **`★N` badge** that climbs with level (1–10).
- **No vanilla star sprites** appear on the companion (they're suppressed).
- Crosshair hover shows the **caste name** + `Lv N (Y% to next)` (e.g. `Fire Mage · Lv 3 (…)`), switching to `Lv 10 (max)` at the cap.
- XP stops accumulating at level 10.

## 6b. Per-caste leveling identity  ✅ PASSED

1. Level a **Rogue or Fire Mage** → it should get **visibly faster** as it climbs (+3%/level, +27% at Lv 10).
2. Level an **Ice Mage or Support Mage** → it should get **tankier** (+4% max health/level, +36% at Lv 10).
3. Relog a leveled companion → the bonus should persist (rebuilt on restore) and **not** double up / compound.
4. All castes should still hit harder at higher levels (vanilla `SetLevel` damage scaling, independent of the above).

## 7. Stance — Follow / Guard / Standby  ✅ PASSED

1. Hover your companion, press `E` to cycle **Follow → Guard → Standby → Follow** (watch the stance message).
2. **Guard:** holds its spot, engages threats in a wider radius.
3. **Standby:** does **nothing** — won't even attack a monster that walks up (until/unless a *player* attacks it, see §7c). Holds position.
4. Stance changes blocked (with a message) while chore-assigned or dueling.
5. **Note:** `E` is vanilla "Use" — since a Dvergr has no interaction, hovering it and pressing `E` only cycles stance. Rebind in the config if it conflicts with anything you have.

**Known caveat:** stance is in-memory only — resets to Follow after relog.

## 7f. Companion hover tooltip + rename  ✅ PASSED

1. **Hover tooltip:** put the crosshair on **your** companion → the tooltip shows its current **Stance**, plus `[E] Cycle stance` and `[Y] Rename`. (Another player's companion shows only its floating name/badge, no command hints.)
2. **Rename:** hover your companion, press `Y` → the vanilla text box opens prefilled with its current name. Type a name, confirm → it acknowledges ("I'll answer to … now."), and the floating name above it updates (keeping the gold `★N` badge).
3. **Persists:** the name survives a relog (stored on the companion's ZDO).
4. **Reflects on chores:** assign the renamed ally to a station, then hover that station with another companion → the claim line reads *"&lt;your chosen name&gt; is already working here."*
5. While the rename box (or chat/console) is open, typing letters must **not** trigger stance/feed/chore actions.

## 7e. Capability lines replace vanilla chatter  ✅ PASSED

1. **On recruit:** the freed Dvergr should immediately speak a stance/caste line (e.g. a Fire Mage in Follow → *"I'll follow and fight at your side. Set me to tend smelters, kilns and forges."*) — **not** the old hostile-Dvergr barks.
2. **On stance change:** press `E` → each stance shows a matching line (Guard → "I'll hold this ground. …", Standby → "I'll wait here, quietly. …").
3. Over time the companion should **no longer** emit the wild Dvergr ambient chatter (NpcTalk disabled). Confirm after a relog too (restore path also disables it).

## 7b. Pathfinding on slopes / builds (best-effort fix)  ✅ PASSED

1. Lead a companion up a **slope**, across **build pieces**, and over **modified terrain**.
2. It should hop small ledges with a **low** hop (jump force halved) and shuffle/shake noticeably **less** than before.

**Honest expectation:** this eases the worst sticking, it is **not** a full pathfinding rewrite — report cases where it still gets stuck and where (slope angle / piece type).

## 11. Admin spawn command  ✅ PASSED

1. Open the console (F5) and run e.g. `de_spawn fire 5`, `de_spawn rogue`, `de_spawn support 10`.
2. A recruited companion of that caste + level should appear in front of you, already yours (owner = you), correct `★N` badge, and follow you.
3. Works without `devcommands` enabled. Bad args print a usage line.

## 7c. Ownership & threat (multiplayer / PvP)  ✅ PASSED

*(Single-player can only check the owner-only command gating; the player-threat parts need a second player + PvP.)*
1. **Owner-only commands:** a second player tries `G`/`H`/`F`/`J` on your companion → "This companion answers to another." Your own commands still work.
2. **Guard vs other players:** set Guard → it treats a **non-owner** player as a threat and attacks; it must **never** turn on you (the owner).
3. **Follow vs owner's target:** in Follow, attack another player → your follow companions join on **that** player only.
4. **Retaliation:** while the companion is on Standby / doing chores / Follow, have another player hit it → it retaliates against that attacker regardless of stance.
5. **Retaliation extends to the attacker's companions (setting 1):** when another player hits your companion, your companion should turn on **both** that player **and** that player's own companions in range — not just the player. Confirm the attacked ally engages an aggressor's companion standing nearby.
6. **Attacked player's companions defend (setting 2, needs PvP on):** with PvP enabled on both players, have player B attack player A. Player A's companions should turn on **B (the aggressor)** and **B's companions**. Confirm A's allies engage the aggressor's side; with PvP **off**, no such reaction (the hit doesn't land). The existing behavior — the *attacker's* Follow companions joining in on the player their owner attacked — should still hold.

## 7d. Chore tooltip + "already working" claim  ✅ PASSED

1. With a companion, hover a **Smelter / Cooking Station / Fermenter / chest** → tooltip shows `[H] Set companion to work` (or cook/brew/haul).
2. With **no** companion owned, the hint is absent. Doors/levers/other switches never show it.
3. **Already-working display:** assign a companion to a station, then hover **that same station** → the tooltip now reads (orange) *"&lt;name&gt; is already working here."* instead of the `[H]` hint.
4. **No double-assign:** with that station claimed, press `H` again on it — if it's **your** ally, it unassigns ("Ally returns to your side"); a second/other companion is never added. With a free same-caste ally nearby, `H` on a **different** unclaimed station assigns that ally (the busy one isn't yanked off its job).
5. After the worker is unassigned / despawns / dies, the station frees up and the `[H]` hint returns.

## 8. Chores — workstation automation (Smelter-family)  ✅ PASSED

1. Recruit a **Fire Mage** Dvergr (`DvergerMageFire` — check the `[recruit]` log line shows `caste FireMage`). Place a **Smelter** with a nearby **chest** containing ore (e.g. Copper) **and Coal**.
2. Press `H` to assign the nearest matching-caste companion (within 10 m). It walks/climbs to the smelter.
3. Confirm it ferries **ore** in **and adds coal as fuel**, plays the **vanilla add VFX/SFX** each time, and **stops at each cap** (no overfilling — the reported bug).
4. **Caste-gating:** hover the smelter with only a **Rogue** (or wrong-caste) companion nearby and press `H` → it should refuse with *"Only a Fire Mage can tend this station — none nearby."* Repeat the whole flow with an **Ice Mage** on an **Eitr Refinery** or **Spinning Wheel**.
5. **Vertical reach:** put the station on a platform above/below the companion → it should still detect and path up/down to it.
6. **Voiced blockers** — bubble appears for a blocker, but **only once per minute and only while you (the owner) are within ~20 m**:
   - Empty the chest of a needed input → *"I need more ore!"* / *"I need more coal!"*; remove the chest → *"I have no chest to draw from!"*; block its path → *"I can't reach my station!"*.
   - Walk >20 m away → no new bubbles. Come back → it can speak again (after the 1-min throttle).
   - Refill/unblock or unassign → bubble clears.

**Known gaps (by design / not yet built):** farm **replanting** is not yet implemented (harvest-only). *(Chore assignment now **does** survive a relog — see §8f.)*

## 8f. Chore persistence (relog / out of range)  ✅ PASSED

1. Assign a chore (e.g. a Fire Mage on a Smelter) and confirm it's working.
2. **Walk away** until the station is still in your loaded area → it should keep ferrying ore/fuel even with you out of "notify" range (you just won't hear its blocker bubbles).
3. **Relog** (log out to main menu, log back in). The companion should respawn and, once the station has loaded, **walk back to it and resume** the chore automatically — not idle at your side.
4. **Move so far the zone fully unloads**, then return: it pauses while unloaded (engine limit — Valheim doesn't simulate unloaded zones) and **resumes** from the persisted state on reload.
5. Unassign (toggle `H` on the station) → after a relog it should **stay** unassigned (the persisted record is cleared).
6. If the station was **removed** while you were away, the companion gives up the stale chore after ~60 s and returns to normal.
7. **Re-resolves the right station:** after a relog the ally should resume on the **same** station it was assigned to (matched by saved position), not a different same-type one nearby.

**Multiplayer note:** work is gated to the companion's **ZDO owner**, so on a server it runs once; when the assigning player logs out, another nearby player/the server takes over the chore.

## 8b. Farming chore (Support Mage) — plant + harvest, any type  ✅ PASSED

1. Recruit a **Support Mage** Dvergr. Till a patch of ground (Cultivator), place a **chest** within ~8 m with some **seeds** in it (e.g. Carrot/Turnip/Onion seeds — and try mixed types).
2. **Cultivator-stand trigger (primary):** place an **Item Stand** by the field and put a **Cultivator** on it. Hover the stand → crosshair shows `[H] Set companion to farm this field`. Press `H` → "Ally tends the field." The ally should plant/harvest in the radius around the **stand**. (Also confirm it **survives relog** — the chore re-resolves to the cultivator stand.)
2b. **Crop trigger (alt):** hover a crop / tilled-ground `Pickable` → `[H] Set companion to farm here` (wild berries/branches/surface stone should **not** show it); `H` assigns the same chore centered on that crop.
3. **Log:** on first farm tick, `[farm] planting catalog built: N seed→sapling entries.` should appear (N > 0). If N = 0, the `Plant`+`Piece` scan found nothing — capture the log.
4. **Harvest:** grow some crops to ripe → confirm it harvests any ripe crop (any type) into the chest with the **pick VFX**; unripe plants are left alone.
5. **Plant:** with nothing ripe and seeds in the chest → confirm it plants saplings on **free cultivated ground** (place VFX plays, one seed consumed per plant), and does **not** plant on non-tilled ground, on top of growing plants, or on unharvested crops. Mixed seed types should all get planted. Each plant logs `[farm] planted '<sapling>' at <pos> (biome <Biome>)`.
5a. **Biome gate (the key check for this pass):** put a **Plains-only** seed (Barley or Flax) in the chest while farming in the **Meadows/Black Forest**. Confirm it is **not** planted there and the ally voices *"These seeds won't grow in this land."* Then run the same in the **Plains** → it should plant normally (and the `[farm] planted` log shows `biome Plains`). Cross-check the `biome` in the log matches where you're standing. *(The per-crop biome allow-lists come from the game's assets, so this log line is how we confirm the gate is using the right data.)*
6. **Self-sustain:** over time the field should keep going — harvest fills the chest, seeds from the chest go back into the ground.
7. Blockers: no chest → "I have no chest for the harvest/seeds."; full chest → "The harvest chest is full!"; no room → "There's no room left to plant."; nothing to do → "No crops are ready, and no seeds to plant."; out of reach → "I can't reach the field."
8. **Caste-gating:** a Rogue/other caste hovering a crop + `H` → refusal (no Support Mage nearby).

## 8c. Tamed-animal feeding chore (Support Mage)  ✅ PASSED

1. With a **Support Mage**, build a pen of **tamed** animals (boar/wolf/etc.) and a **chest** with food they eat (e.g. carrots) within ~8 m.
2. **Tooltip:** hover a **tamed animal** → the crosshair shows `[H] Set companion to feed` (your own recruited allies should **not** show it). Press `H` → "Ally tends the animals."
2b. **Chicken/Hen tooltip:** hover a tamed **Chicken** and a tamed **Hen** → both must show the `[H] Set companion to feed` hint (they route hover through `Character`, not `Tameable` — this is the specific fix to check).
2c. **Claim by range:** with a mage already feeding a pen, hover a **different** animal in that same pen → it should read *"&lt;name&gt; is already working here."*, and pressing `H` there must **not** add a second mage (pressing `H` toggles **your own** feeder off instead).
3. **One mage, whole pen:** let **several** animals get **hungry** → confirm the single assigned mage feeds them all over successive ~5 s ticks (one drop per tick, cycling), from the chest, and they eat it (and over time breed). It shouldn't pile up food. (This is expected — one mage tends the pen, not one mage per animal.)
4. Blockers: not hungry → "The animals aren't hungry."; no chest → "I have no food chest nearby."; chest lacks accepted food → "I have no food to give."; out of reach → "I can't reach the pen."

## 8d. Provisioning chore (Support Mage)  ✅ PASSED

**Fermenter:**
1. With a **Support Mage**, place a **Fermenter** and a **chest** with a fermentable base (e.g. mead base) within ~8 m. Hover the fermenter, press `H` → "Ally tends the brew."
2. Confirm it loads a base when empty, waits while fermenting, and **taps** when ready (meads drop by the fermenter).
3. Blockers: uncovered fermenter → "The brew is exposed to the sky!"; no/empty chest → "I have no chest to brew from!" / "I have nothing to brew!".

**Cooking Station:**
1. Hover a lit **Cooking Station** with a chest of raw food (e.g. raw meat) within ~8 m, press `H` → "Ally tends the cookfire."
2. Confirm it places raw food on free slots, and **removes cooked food before it burns**. On stations that use fuel, confirm it refuels.
3. Blockers: unlit → "The cooking fire is out!"; no chest → "I have no chest to cook from!"; nothing cookable → "I have nothing to cook!".
4. **Stone Oven (regression):** assigning a cooking chore to a **Stone Oven** must **not** spam `CookingStation.IsFireLit` NullReferenceExceptions. The oven is its own heat source (`m_requireFire = false`), so the fire check is skipped and it's treated as always lit — it should cook bread/pies normally.
5. **Stone Oven collection:** confirm the companion **collects the finished food before it burns** (it spawns by the oven). Earlier it would cook but never collect (food burned) because `Interact()` no-ops on the oven's add-food switch; we now call `OnInteract()` directly. (A "+N" bonus-food popup / a tick of your own cooking skill on collection is a known harmless side effect.)

## 8e. Hauling chore (Rogue)  ✅ PASSED

1. Recruit a **Rogue** (`Dverger` — `[recruit]` log shows `caste Rogue`). Drop loose items on the ground within ~10 m of a **chest**.
2. Hover the **chest**, press `H` → "Ally hauls to this chest."
3. Confirm it sweeps the loose items into that chest one per tick (the world items disappear, the chest fills, the lid opens on each deposit). It **stays at the chest — it does not walk out to each item**. When the ground is clear it stays quiet.
4. Blocker: chest full → "The haul chest is full!". Wrong caste (non-Rogue) hovering the chest → "Only a Rogue can do this — none nearby."

## 9. Duels — duel mode (requires **two players**)  ✅ PASSED

1. Two different players each have a recruited companion nearby.
2. Player A hovers **their own** ally and presses `J` → it enters duel mode ("squares up…" bubble + center message). (req 1)
3. Player B does the same on **their** ally → the two companions seek each other and fight.

**Pass — non-lethal (req):** they fight but **neither dies** — the loser bottoms out at ~5% HP, leaves duel mode ("subdued and yields"), and the winner gets +50 XP ("wins the bout!"). Non-lethal now rides on the confirmed `Character.Damage(HitData)` prefix (the old unverifiable `ApplyDamage` patch is gone).
**Only-rival targeting (req 2):** a duelist ignores players and creatures and isn't attacked by them while dueling; only its rival duelist. Try standing next to it / spawning a mob — it shouldn't engage.
**Owner-only (req 1):** pressing `J` on **another player's** companion is refused ("Only its owner can send this companion to duel").
**Auto-stand-down (req 3):** when the rival leaves duel mode or moves out of range, the other companion stands down on its own ("no more challengers"). A lone duel-mode companion stands down after ~60s.
**Owner leash (req 5):** if Player A logs out or walks >40m away mid-duel, A's companion leaves duel mode ("loses sight of its owner").
**PvP immunity (req 6):** with PvP on, a player attacking a duel-mode companion deals **no damage**.
**Owner name tag (req 4):** every companion's floating name shows `(OwnerName)` before the `★N` badge.
**Duel hint (setting 4):** hover **your own** companion while **another player's** companion is within ~30 m → the crosshair tooltip shows `[J] Duel a rival companion nearby`. With no rival companion in range the hint is absent. While already in duel mode it reads `[J] Stand down from duel` instead.
**Win announced in chat (setting 3):** when a duel is decided, a **chat shout** should broadcast to all players, e.g. *"&lt;winner&gt; (&lt;owner&gt;) wins the duel against &lt;loser&gt;!"* (in addition to the speech bubble + owner center message).
**Heal the loser after a duel (bug fix):** after a companion loses (bottoms out at ~5% HP and stands down), its **owner** should be able to **feed it a health mead** (`G`) and see its HP rise. Previously the heal silently did nothing because the loser's ZDO had been claimed by the winner's client and `SetHealth` only writes on the owner; feeding now uses `Character.Heal`, which routes to the ZDO owner over RPC. Confirm the HP bar actually moves (see also §4b).
**Butcher-knife betrayal (non-duel):** hit a (non-dueling) companion with a **butcher knife** → it turns feral and attacks players, including its owner.
**By design:** there is no arena structure — duels happen wherever you trigger them.

## 10. ServerGuide story integration

If **Valheim ServerGuide** is installed alongside this mod:

1. Recruiting, leveling up, and winning a duel should fire `dvergr_recruited` / `dvergr_level_up` / `dvergr_duel_won` events into ServerGuide.
2. Expect the configured raven/message popups (per ServerGuide's `guidance.yaml`) to appear on those events.

**If ServerGuide isn't installed:** the mod should run fine and simply not raise those events (the bridge is soft-guarded).

## 10b. The Lost Scrolls II lore — biome descent (`ls_beat_*`)  ✅ PASSED

The lore lives in `guidance.lost-scrolls.yaml` (already deployed to the test profile's
`config/ValheimServerGuide/`). It was **reworked from the old 6-Act chains into a
biome-by-biome descent**: reflective beats fire at distinct in-game locations as the
player travels Meadows → Ashlands, holding a mirror up to the player (the toiling
Dvergr are humanity — slaves of their own path). Scripture is woven in **verbatim,
never cited**.

**Key mechanic — `distance`, not `location_entered`.** The beats use trigger type
`distance` (proximity to a named ZoneLocation). This is deliberate: `location_entered`
burns a one-shot `loc_<name>` dedup key for **every** location a player nears,
regardless of guidance, persisted on the character — so **players already on the
server would never see the beats**. `distance` only burns `dist_<name>` when a matching
guidance is in range, so a fresh config fires for veterans and new characters alike.
It logs `[distance] entered range of '<name>'` at **Info** level.

**Starting the lore + returning players:**
1. **New character:** spawns at `StartTemple` → within ~5 s the opening beat *"No New
   Thing"* fires (intro).
2. **Player already on the server (off elsewhere):** ~1 min after login, a raven
   nudge *"Return to the Stones"* (`ls_call_to_start`) points them back to StartTemple,
   repeating on a 30-min cooldown until they read the opening beat (`stop_when`). Walk
   to StartTemple → the opening beat fires (works because `distance` isn't burned).

**Verify the beats fire, each pointing to the next (order is guided by the text, not
enforced — they're independent entries):**

| Biome | Location(s) | Expect |
|---|---|---|
| Meadows | `StartTemple`, `Eikthyrnir` | *"…no new thing under the sun…"* → *"…served the creature more than the Creator… sweat of thy face…"* |
| Black Forest | `Crypt*`, `GDKing` | *"…the darkness comprehended it not…"* → *"Men loved darkness rather than light…"* (Damon named) |
| Swamp | `SunkenCrypt4`, `Bonemass` | *"…creation groaneth and travaileth…"* → *"…the wages of sin is death."* |
| Mountain | `MountainCave*`, `Dragonqueen` | *"…a way which seemeth right unto a man…"* → *"All we like sheep have gone astray…"* |
| Plains | `GoblinCamp2`, `GoblinKing` | *"…worshipped and served the creature… became fools."* → *"…the end thereof are the ways of death."* |
| Mistlands | `Mistlands_DvergrTownEntrance*`, `Mistlands_Excavation*`, `Mistlands_DvergrBossEntrance1` | the mirror *"…the servant of sin. So are you."* → chore mirror *"…set it to your chores, and look at it again."* → *"…delivered from the bondage of corruption…"* |
| Ashlands | `CharredRuins*`, `CharredFortress` | *"…having no hope, and without God in the world…"* → verdict + held-back light *"…upon them hath the light shined. The shadow thins, but does not end…"* |

**Per-caste voices** — recruiting any caste any time fires its raven voice line
(now carrying *"the truth shall make you free"* / *"free indeed"*). On a recruit you
may see a world beat AND a raven voice — intended.

**Pass:** the opening fires at StartTemple (both for new and returning players); each
biome's beats fire near their locations with the right verbatim scripture and channel
(intro/rune); each beat's closing line points toward the next landmark; the Mistlands
mirror + chore-mirror land; the Ashlands finale reads as hopeless-but-not-sealed.
**Fail signals:** a beat never fires → check the `[distance] entered range of '<name>'`
Info log to confirm the real ZoneLocation prefab name matches the YAML (esp. the
**Ashlands `Charred*`** names, which are wildcarded pending confirmation); the
StartTemple opening doesn't fire for a **returning** player (confirm `distance`, not
`location_entered`, and that `dist_StartTemple` wasn't already burned by an earlier
test).

## 10c. The recruit-order guide (`ls_guide_recruit_order`)  ✅ PASSED

A plain, **tracked** walkthrough (separate from the mythic act beats) that steers the
player to free the four castes in order: **Rogue → Fire Mage → Ice Mage → Support
Mage**. Lives in `guidance.lost-scrolls.yaml`, already deployed to the test profile's
`config/ValheimServerGuide/` folder.

1. **Starts on the Mistlands.** Enter the Mistlands → a raven card *"The Order of
   Communion"* explains the subdue-then-`G` mechanic and the order.
2. **Pin it & read the objective.** Open the Codex (`F3`), find *"The Order of
   Communion"*, click **Show on Tracker**. The `F10` HUD panel shows the title + `n/5`
   progress; **hover the row** → the tooltip names the **next caste to free** and how
   to recognize it (e.g. *"① Free a ROGUE first — the melee Dvergr that carries NO
   staff…"*).
3. **Advances in order.** Recruit a **Rogue** → progress ticks to the next step and the
   tooltip now asks for a **Fire Mage**; then **Ice Mage**; then **Support Mage**.
4. **Out-of-order is allowed but doesn't advance the guide.** Recruit, say, a Fire Mage
   *before* a Rogue → the mod still frees it (recruitment isn't order-gated), but the
   guide **stays** on "free a Rogue" until you actually do. This is the intended
   teaching behavior.
5. **Completion.** After the Support Mage, the chain completes (drops off the tracker)
   and the Codex shows the `summary` recap.

**Pass:** the guide activates in the Mistlands, the tracker tooltip always names the
correct next caste, it advances only on the asked-for caste, and completes after the
Support Mage.
**Fail signals:** the chain never appears on the tracker after entering the Mistlands
(biome step didn't fire, or the quest wasn't pinned); a step advances on the wrong
caste (check the `caste:` filter); the tooltip is blank (missing `description`).

## 10d. The Companion Handbook (`guidance.companions.yaml`)  ✅ PASSED

Gameplay help (separate `category: Companions` in the F3 Codex) teaching how to use
companions for **chores** and **adventures**. Deployed to the test profile. Keys shown
are the mod **defaults** (E / Y / G / J / H). Each tip is `once` per character but
re-readable in the Codex.

1. **First recruit → command reference.** Free any Dvergr → a raven card *"Commanding
   Your Companion"* lists the stance/rename/feed/duel keys and the `[H]` chore/recall
   key. (On this first recruit you'll also see the caste chore tip below and the lore
   voice — expected; all `once`.)
2. **Caste chore tips.** Freeing each caste fires its chore card once:
   - **Fire Mage** → smelters / blast furnaces / kilns / forges (chest of ore+coal ~8 m).
   - **Ice Mage** → Eitr Refinery / Spinning Wheel.
   - **Support Mage** → farm / cook / brew / feed tamed animals.
   - **Rogue** → haul loose drops into a chest.
   Confirm each names the right stations and the `[H]` assign / recall flow.
3. **Adventure tips (contextual).** After you've recruited (they `require`
   `ls_companion_commands`): **sail a ship** → the ship-riding tip fires once;
   **use a portal** → the portal-follow tip fires once. Both stress *Follow* stance.
4. **Level-up tip.** First companion level-up → the leveling card fires once.
5. **Totem tip.** Place an **Incinerator/Obliterator** → the Communion-Totem sealing
   tip fires once. (If it doesn't, confirm the piece name in the `[build] subject=...`
   Info log matches `incinerator`.)
6. **Codex reference.** All of the above stay readable under **Companions** in F3.

**Pass:** the command card fires on first recruit; each caste tip fires with the right
stations on freeing that caste; ship/portal/level/totem tips fire in their contexts;
everything is grouped under **Companions** in the Codex.
**Fail signals:** a caste tip fires for the wrong caste (`caste:` filter); the totem
tip never fires (piece name mismatch — check `[build]` log); ship/portal tips fire
before any recruit (the `requires` gate failed).

---

## 12. Companion totems — seal & summon  ✅ PASSED

See [Companion-Totems.md](Companion-Totems.md).

**Seal:**
1. Recruit 2 companions, level at least one past 1. Set both to **Follow** and
   stand them by an **Incinerator** (Obliterator).
2. Put **1 Wisp** in the incinerator, pull the lever. During the 5–7 s lighting
   animation exactly **one** companion (the nearest) should dissolve with a **soul
   VFX**; **one** totem appears in the incinerator slots; **one** Wisp is consumed;
   the other companion stays. Message: *"A companion is sealed within a totem."*
3. Take the totem. Its name should read **"Communion Totem"** (not "Fuling Totem")
   with the purpose description; hover shows the companion's **name**, **caste**,
   **level** (and "Bound to <owner>"). Confirm a real Fuling Totem, if you have one,
   is **unaffected** (still "Fuling Totem", still stackable). Confirm two companion
   totems do **not** stack.
4. Put **2 Wisps** in with 2 followers → **2** named totems, both companions gone.
5. Put **1 Wisp** in with 2 followers → only **1** sealed, 1 stays (req 5).
6. No wisps, or no followers → the incinerator behaves like **vanilla** (nothing
   sealed).

**Summon:**
7. Put a totem on the hotbar, aim at open ground, press its slot number. The
   companion spawns **where you're looking** with a **spawn VFX**, at its **sealed
   level** (check the `★N` badge) and **name**; the totem is consumed. Message:
   *"The totem cracks…"*.
8. Relog with a totem in a chest → the **"Communion Totem"** name/description, the
   stat tooltip, and summon all still work (customData + shared re-applied on load).

**Pass:** 1:1 seal ratio honored; level/name/XP round-trip; surplus companions
untouched; totems don't stack; summon lands at the aim point.
**Fail signals:** all goblin totems in the world renamed (shared-name write leaked
into `m_shared`); totems merged and a companion lost (stack size not forced to 1);
summon spawns at the player's feet regardless of aim (raycast/layer mask).

**MP (needs two clients):** seal at an incinerator owned by the other player;
confirm the totem appears and the companion dissolves on both. Summon a totem your
teammate sealed.

---

## 13. Ship riding — get aboard, walk freely  ✅ PASSED

See [Ship-Riding.md](Ship-Riding.md).

1. Recruit a companion, keep it in **Follow**. Build/board a ship (Karve or
   Longship) with a boarding **ladder**. Let the companion trail to the hull.
2. **Board:** with the ship stopped, the companion should climb aboard at the
   ladder's deck target (snaps up onto the deck). Speech: *"Aboard…"* — once.
3. **Free to walk:** on deck it should behave normally — walk around and follow you,
   **not** lock to a seat or freeze in place. No idle-suppression, no snapping.
4. **Ride:** sail. It should stay on the boat (platform physics carries it) and
   keep following you around the deck.
5. **Stay aboard:** if it walks off an edge into the water alongside the boat while
   you're still aboard, it should be lifted back on (never pinned to a spot).
6. **Fight:** aggro a serpent / drop a hostile near the boat. The companion should
   fight it via normal AI, then go back to following you on deck.
7. **Avoids water on land:** off any ship, lead the companion along a shoreline /
   past a pond. It should **not** wander into deep water (avoids it). Then board a
   ship a short swim offshore — now it **should** enter the water to swim out and
   board (avoidance lifted only while you're aboard).
8. **Disembark:** land and step off. The companion should path ashore via normal
   land Follow, and go back to avoiding water.
9. **Relog aboard:** relog while sailing → once you're aboard again, the companion
   re-boards on its own.

**Pass:** climbs aboard through the ladder, then moves freely on deck and follows
you; stays on the boat while sailing.
**Fail signals:** companion swims behind the boat and never climbs (boarding range /
ladder detection); repeatedly teleports/​jitters (board cooldown / stand-on-ship
check); can't be lost in the water beside the boat but also never behaves normally
on deck.

**MP (needs two clients):** confirm only the ZDO-owner performs the lift and the
companion looks correct aboard on the **other** client (synced transform).

---

## 14. Companion minimap pins  ✅ PASSED

See the "Minimap pins" section of [Ally-Commands.md](Ally-Commands.md).

1. Recruit a companion. Open the map (`M`). A **pin** should sit on the companion
   and **move with it** as it follows you. Hover shows its **name**.
2. **Rename** it (`Y`) → the pin label should update to the new name.
3. Recruit several → **one pin each**. Send one far away (e.g. a chore across the
   base) → its pin tracks it there.
4. Companion **despawns** (dies, or you seal it into a totem) → its pin **disappears**.
5. **Config:** set `ShowMapPins = false` → all companion pins vanish; set it back →
   they reappear. Change `MapPinIcon` (0-4) → the pin sprite changes.
6. **Save file:** relog → pins are recreated live (they're transient); confirm the
   map save file didn't accumulate stale companion pins.

**Pass:** exactly your own companions are pinned, pins follow them, labels match
names, pins clear on despawn, toggle works.
**Fail signals:** a pin left behind after a companion moved/died (position not
refreshed / not removed); pins written to the save (should be `save = false`);
duplicate pins after relog (Minimap-rebind logic).

**MP (needs two clients):** each player should see **only their own** companions'
pins — confirm player B's companions do **not** appear on player A's map, and vice
versa.

---

## 15. Companions follow through portals  ✅ PASSED

A **Follow**-stance companion owned by you should teleport with you through a portal.

1. Recruit a companion, keep it in **Follow**. Build a connected portal pair. Step
   through. The companion should arrive at the destination portal with you (spread a
   bit around the exit, not stacked on you) and resume following.
2. **Several companions** → all your Follow allies come through, spread around the
   exit. Log line: `[portal] Brought N companion(s) through the portal…`.
3. **Only Follow:** an ally on a **chore** or in **Guard / Standby / duel** stays
   behind (it isn't following). Recruit-owned check: only **your** companions come,
   not another player's.
4. **Round trip:** go back through — they follow back.
5. **Persistence:** after arriving, confirm the companion is really at the
   destination after the zone finishes loading (ZDO position committed), not left at
   the origin.

**Pass:** your Follow companions arrive with you and keep following; busy/other-owner
allies don't.
**Fail signals:** companion left at the origin (ZDO position not committed / zone
unloaded before flush); companion stacks exactly on the player; a chore/Guard ally
gets yanked along; another player's ally teleported.

**MP (needs two clients):** only the teleporting player's own companions come; the
other client should see them arrive correctly (position synced), and player B's
companions shouldn't move when player A portals.

---

## 16. Companion inventory system

Full design in [Ally-Inventory.md](Ally-Inventory.md). All items below are **unverified in a live session.** Recruit a companion first. Keys use the `Y` inventory key.

### 16a. Open the inventory + rename (reqs 1-3, 14, 15)
1. Hover **your own** companion. The crosshair tooltip should read `[Y] Inventory / rename`.
2. Press **`Y`**. A **chest-style panel** opens: the companion's **4×2 (8-slot)** grid on top, **your own inventory + crafting panel** below — exactly like opening a chest.
3. The panel shows a **total-weight readout** for the companion's grid.
4. A **name field** sits in the top-left of the container panel (below the title, no longer overlapping it), prefilled with the companion's name. Type a new name and press Enter → the floating name updates; reopen to confirm it stuck.
4b. Next to the name is a live **HP readout** (`HP cur / max`) that updates while the panel is open. (Active resistances are shown **above the companion in the world**, not in this panel.)
7. While the **name field is focused**, none of the mod hotkeys fire — typing "e", "g", "h", "j", "y", etc. edits the name and does **not** cycle stance / feed / assign chores / open the panel again.
5. Hover **another player's** companion and press `Y` → refused ("answers to another").
6. Put items in the grid, close, **relog / reload the zone** → the items are still there (Container ZDO persistence).

**Pass:** chest-like panel with the 8-slot grid + player inventory + crafting + weight; rename field works; contents persist.
**Fail signals:** no panel; grid wrong size; pressing the interact key on the companion opens the bag ungated (the hover/interact suppression failed); name field missing (check the log for "Could not build companion name field").

### 16b. Pickup (reqs 4-6)  ✅ PASSED
1. Put, say, **1 Wood** in the companion's pack. Drop a stack of Wood on the ground within ~8 m.
2. In **Follow** with no enemies near, the companion pulls the matching Wood straight into its pack (radius sweep — it does not walk to each item).
3. Drop an item type it does **not** carry → it's ignored.
4. **Empty the pack entirely** → it picks up nothing (req 5).
5. Aggro a monster near it (or start a fight) → it **fights and does not gather** while alerted (req 6); gathering resumes once combat ends.

**Pass:** only already-held item types are collected; empty pack = no pickup; combat suspends pickup.

### 16c. Food (reqs 7-10)  ✅ PASSED
1. Put a cooked **food** (e.g. grilled meat) in the pack. Within ~1 s the companion eats **one**, and a **fed icon** (the food's own icon) appears above its health bar.
2. **Confirm the HP bump:** open the pack (`Y`) and watch the **`HP cur / max`** readout by the name — **max** should jump by roughly the food's HP value while fed (shown in gold), then decay back over the food's burn time.
3. It will **not** eat a second food while the first is active (req 8).
4. The bonus **decays** over the burn time and the fed icon clears at the end (req 9/10).

**Pass:** exactly one food at a time; the HP readout's max rises then decays; fed icon shows then clears.

### 16d. Meads (reqs 11-12)
1. **Health mead:** put a healing mead in the pack and damage the companion below **35%** HP → it starts drinking and **keeps drinking until above 90%** HP, then stops (req 11 — the latch fix).
2. **Resist mead** (fire/frost/poison barley-wine or resist mead): put one in the pack → it drinks it. **Confirm the effect landed:** the matching **resistance icon** appears above its health bar **and** in the pack panel under the name (with the resistance name). It **keeps drinking** more as long as any remain (req 12).
3. **Confirm it really resists:** with, e.g., fire resistance active, expose the companion to fire (a Surtling / fire staff) → it should take noticeably reduced fire damage vs. an un-medded ally.
4. A **stamina** mead is ignored (no health, no resistance icon).

**Pass:** health mead runs the full 35%→90% window; resist meads consumed on sight, the resistance is shown (hud + panel) and demonstrably reduces that damage type; stamina meads ignored.

### 16e. Weight cap / encumbrance (reqs 13-14)
1. Load the pack past **150 weight** (the panel weight readout crosses 150).
2. The companion shows an **encumbered icon** above its health bar, **stops picking up**, and **will not attack** — it drops its target and goes passive **every frame**, so it no longer swings at enemies — but can still **move / follow**.
3. Drop below 150 → it returns to normal (attacks, gathers again).

**Pass:** over-150 = encumbered icon + genuinely no attacking + no pickup but still mobile; clearing weight restores normal behavior.

**MP note:** pickup/consumption/food run on the companion's **ZDO-owner** client only; the encumbered icon is derived from the replicated container weight so it shows for everyone, but the **fed** icon is owner-client local.

### 16e2. ComfyQuickSlots compatibility  ✅ PASSED
1. With **ComfyQuickSlots** installed, open a companion's pack (`Y`).
2. The player inventory's extra bottom row (armor/quickslots) is **fully visible** — not hidden behind the pack panel; the pack panel sits just below it.
3. Close and open a **vanilla chest** → its layout is unchanged (the shift only applies to the companion pack).

**Pass:** the CQS extra row is never covered by the pack UI; vanilla chests unaffected. (Tunable: `ContainerClearancePx` / `VanillaInventoryHeight` in `CompanionInventoryGui`.)

### 16f. Drop pack on death  ✅ PASSED
1. Put items in a companion's pack, then let it **die** (e.g. in combat).
2. All pack items **spill onto the ground** at the death spot (a small scatter), recoverable like any drop.

**Pass:** every item in the pack drops on death; nothing is silently lost. (Sealing into a totem does **not** drop — those items ride the totem instead, see §16h.)

### 16g. Name-field key suppression  ✅ PASSED
With the rename field focused, **all** binds are suppressed — not just the mod's own
hotkeys but every vanilla button action (a `ZInput.GetButtonDown` prefix), so typing
`E` no longer closes the panel and typed letters don't fire hotbar/use/etc.

### 16h. Totem carries the pack  ✅ PASSED
1. Put a few items in a companion's pack.
2. **Seal** it into a Communion Totem (Incinerator + Wisps, §12).
3. **Summon** it back from the totem (use the totem item).
4. The summoned companion's pack still holds **exactly those items**.

**Pass:** sealed companion's inventory round-trips through the totem intact (watch for the `[totem] Restored N pack item(s)` log line).

### 16i. Portal block on prohibited companion cargo (wood portal only)  ✅ PASSED
1. Give a **Follow**-stance companion a **non-teleportable** item (e.g. Copper/Tin/an ore) in its pack. Keep your **own** inventory clean of prohibited items.
2. Walk into a connected **`portal_wood`** with the companion nearby → **you do not teleport**, and a center message names the ally and the item ("<name> is carrying <item> — you can't take it through the portal.").
3. Remove the item from the companion's pack (or send it away / change its stance) → you can now teleport normally, and it comes with you (§15).

**Pass:** a following ally's prohibited cargo blocks the wood portal even when your own inventory is clean; the notification names the ally + item; clearing it unblocks. Non-wood/modded portals are unaffected.

---

## Highest-risk items to watch

- **Duel mode cross-client engagement** (#9) — the reworked `DE_Duel` ZDO flag must replicate so two different players' duelists actually pair up; confirm they seek each other (needs two players). Non-lethal now rides on the confirmed `Character.Damage` prefix, so that specific error risk is gone.
- **Mead detection** (#4) — behavior-based now (robust), but first-run confirmation still wanted.
- **Mid-fight commune** (#2) — reported broken three times; confirm the aggravated-flag fix finally holds.
- **Reference HP values** (#5) — documented, not extracted; correct against the live `[xp]` log if needed.
- ~~**Lore beats** (#10b)~~ — **verified in-game 2026-07-03**: the `distance`-triggered biome descent, the StartTemple opening (new + returning players), the recruit-order guide (§10c), and the Companion Handbook (§10d) all passed.
- **Ship riding** (#13) — the boarding lift and "free to walk the deck" behavior are static-analysis designs; confirm the ally boards through the ladder, stays on the moving hull, and avoids water otherwise.
- **Portal follow** (#15) — the risky bit is the ZDO position commit vs. zone-unload timing: confirm the ally is really at the destination after loading, not left at the origin. Two clients for the owner-scoping check.
- **Minimap pins** (#14) — confirm pins track/clear and, in multiplayer, each player sees only their own companions' pins.
