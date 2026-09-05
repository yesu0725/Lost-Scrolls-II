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
- **Passed:** §16b pickup; §16c food (eats one at a time, fed icon, HP readout confirms the max-HP bump); the **ComfyQuickSlots** panel-gap fix ("inventory gap test passed" — that mechanic was later replaced by the movable panel, §16e2); §16f drop-pack-on-death; §16h totem carries the pack; §16i wood-portal block on prohibited companion cargo; resist status removed from the pack panel (shown above the companion only).
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

## 7. Stance — Follow / Guard / Standby  ✅ PASSED *(step 6 ⏳ unverified)*

1. Hover your companion, press `E` to cycle **Follow → Guard → Standby → Follow** (watch the stance message).
2. **Guard:** holds its spot, engages threats in a wider radius.
3. **Standby:** does **nothing** — won't even attack a monster that walks up (until/unless a *player* attacks it, see §7c). Holds position.
4. Stance changes blocked (with a message) while chore-assigned or dueling.
5. **Persistence (2026-09-03)  ✅ PASSED:** set a companion to **Standby** (or Guard), log out and back in — it must still be on that stance, holding the same post, and must **not** have reverted to Follow or walked back to you. Re-check after a zone reload (walk far enough away that its zone unloads, then return). It should **not** re-speak its capability line on the reload. A companion summoned back from a **Communion Totem** correctly starts on Follow (sealing requires Follow).
6. **Persistence while on a chore (2026-09-03):** leave an ally working a station, log out and back in **within range of that station** — it must stay at its post and resume working. Watch the first few seconds specifically: it must **not** set off toward you first and then turn back (that gap was the bug). Then repeat while standing far enough away that the station's zone unloads, and walk back — same result. Finally, destroy the station while the ally is away, relog, and confirm that after ~60 s it gives up cleanly and drops to **Standby** — holding position, hover shows `Stance: Standby` — rather than standing there passive with no stance or walking back to you. Same expectation when a chore ends **while you watch**: destroy the station (or let a farm worker finish the crop that anchored its field) and it should hold its post on Standby. Only `H` on the companion, or re-assigning its station, brings it back to Follow.
7. **Note:** `E` is vanilla "Use" — since a Dvergr has no interaction, hovering it and pressing `E` only cycles stance. Rebind in the config if it conflicts with anything you have.

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

## 8c. Tamed-animal feeding chore  ✅ PASSED *(moved to the Rogue at 0.11 — see §8h)*

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

## 8e. Hauling chore (Rogue)  ✅ PASSED *(reworked at 0.11 into the Rogue's shared domain — see §8h)*

1. Recruit a **Rogue** (`Dverger` — `[recruit]` log shows `caste Rogue`). Drop loose items on the ground within ~10 m of a **chest**.
2. Hover the **chest**, press `H` → "Ally hauls to this chest."
3. Confirm it sweeps the loose items into that chest one per tick (the world items disappear, the chest fills, the lid opens on each deposit). It **stays at the chest — it does not walk out to each item**. When the ground is clear it stays quiet.
4. Blocker: assigned chest full → it now **spills into the next nearest chest** rather than stopping; only when nothing in range will take the item does it say so (see §8g). Wrong caste (non-Rogue) hovering the chest → "Only a Rogue can do this — none nearby."

## 8g. Chores store what they produce (2026-09-03)  ✅ PASSED

Config: `Chores/ChoreChestRadius` (default 10 m) governs both storing products and drawing inputs.

1. ✅ **Smelter/kiln:** put a chest within 10 m, assign a **Fire Mage**, let the smelter run. The output bars/coal must be **picked off the ground and stowed** rather than piling up. A charcoal kiln's coal likewise.
2. ✅ **Same-item preference:** put **two** chests in range, one already holding a few of that bar and one empty, with the empty one **nearer**. The output must go to the chest that already holds the bar.
3. ✅ **Full → next chest:** fill the preferred chest. The ally must move to the next nearest chest with room, without complaining. *(Failed first pass — the `IsInUse` gate hid a chest from the ally the moment it used it. Gate removed; retest.)*
4. ✅ **All full / none at all:** fill or remove every chest in range → it says *"Every chest here is full!"* or *"I have no chest to store this!"* and **stops feeding the station** until you make room.
5. ✅ **Cooking:** a Support Mage on a cooking station must stow the finished food (and burnt food) instead of leaving it on the rack area. **Fermenter:** the tapped meads must be collected.
6. ✅ **Pen:** a feeding worker must stow **eggs** but must **never pick back up the food it just dropped** for the animals — watch for a loop of drop/collect/drop.
7. ✅ **It must not steal.** Drop some **ore** next to the tended smelter, and a **mead** next to it: neither is that station's output, so both must be left alone. (Loose items near a **haul** worker are still fair game — that's its job.)
8. ✅ **Never into its own pack:** open the worker's pack (`Y`) at any point — the products must not be in it.
9. ✅ **Not into the wrong container:** stand an **Obliterator**, a **cart**, a **ship** and (if you can arrange one) a **gravestone** in range. The cart **must** be used; the Obliterator, ship and gravestone must **not**. Another ally standing next to the station must not have goods stuffed into its pack.
10. ✅ **Ward:** a chest inside **another player's** guard stone must be refused (the ally reports no chest); your **own** warded chests must still work normally — both drawing inputs and storing products. *(Failed first pass: a ward's creator is not in its own permitted list, so the owner's own base blocked everything. Fixed; retest both halves.)*
11. ✅ **Dedicated server:** repeat 1–3 on the dedicated server with the chest owned by the server (nobody has opened it since the zone loaded). Deposits must **stick** — re-open the chest after a few ticks and after a relog. This is what the ownership claim is for; before it, writes from a non-owning client were silently reverted.
12. ✅ **Two workers, one chest:** put two chores (say a smelter and a kiln) next to the **same** chest and confirm both allies use it — neither should complain that it has nowhere to store. *(This is the case the removed `IsInUse` gate broke.)*
13. ✅ **Stance readout:** hover a working ally — the tooltip must say `Stance: On chore`. Recall it (`H`) and it goes back to reading `Follow`.

## 8h. One worker, a whole workshop + the Rogue's herds (2026-09-04)  ✅ PASSED *(7 reworked into §8i; 15/16/22 not run)*

Config: `Chores/ChoreWorkRadius` (default 20 m), `Chores/HusbandryCullLimit` (default 3).

**Multi-station**
1. ✅ Build **three or more smelters/kilns** spread out to about 15 m apart. Assign one **Fire Mage** by hovering any one of them. It must work **all of them**, walking from one to the next — not just the one you hovered.
2. ✅ Hover a *different* station in that group: the tooltip must read *"&lt;name&gt; is already working here."*, and pressing `H` on it must recall **your own** worker (toggle-off) rather than assigning a second.
3. ✅ Put a station **just outside** 20 m of the post — it must be ignored, and offer a normal assign hint.
4. ✅ **Mixed patch:** stand an **Eitr Refinery** among the smelters. The Fire Mage must leave it alone; an Ice Mage assigned to it must work it (and only it).
5. ✅ **Stuck station:** empty the chests of one station's ore but leave another's stocked. The worker must voice the shortage once and keep working the *other* stations — not stall on the empty one. After ~60 s it retries.
6. ✅ **Provisioning is one chore:** hover a cooking station and assign a **Support Mage**. It must also run **fermenters** in the same patch — loading, tapping, and pulling cooked food before it burns.
7. ❌ **Farm** with a 20 m field — FAILED ("I have no chest to store this"); the chest search was item-only while the patch is twice its radius. Fixed, and the whole farm chore reworked — retest under §8i.
8. ✅ **The patch outlives its parts.** Harvest the exact crop you assigned at, or tear down the exact furnace you assigned at, leaving others nearby: the chore must **continue**. Only when the last station/crop/animal in radius is gone does it stand down to **Standby**, after ~60 s.
9. ✅ **Reachability:** wall a station off. After ~30 s the worker says *"I can't reach my station!"*, sets it aside, and moves to another — it must not say this simply for walking across the patch.
10. ✅ **Relog** mid-round: the worker returns to the same post and resumes the round (§8f still holds).

**Husbandry (Rogue)**
11. Hovering a tamed animal must offer `[H] Set companion to tend the herd` and require a **Rogue** — a Support Mage must now be refused.
12. Breed a pen past three grown animals of one kind. The Rogue must **walk up to** one and kill it **in melee** — confirm it closes to arm's reach and never attacks from across the pen.
13. The cull **drops must be stored** in a chest, including for a species that eats what it drops (a **wolf** is the case that matters — wolf meat is wolf food, and outside the cull window that exclusion is what stops the feeding chore eating its own tail).
14. **Young are never culled**; a **pregnant** animal is spared while another candidate exists.
15. Culling must grant the Rogue **no XP** (watch the `★N` badge / `[xp]` log).
16. Set `HusbandryCullLimit = 0` → no culling at all, feeding continues.
17. Feeding still works: hungry animals are fed one per tick from a chest, and the ally must **not** pick the food back up.

**Hauling, as the Rogue's other half**
18. Hover a **chest** — it must offer `[H] Set companion to clear this area` and require a **Rogue**. Hovering an **Obliterator**, a **ship's hold** or a **gravestone** must offer nothing.
19. A Rogue posted at a chest **with no animals nearby** must behave as a plain hauler: loose items across the patch go into chests, four per tick, and it stays quiet otherwise.
20. A Rogue posted at an **animal** must ALSO haul — drop loose items in the pen and confirm they are stored without re-assigning anything. Conversely a Rogue posted at a **chest** with a pen in range must also feed and cull. This is the point of the change: one worker, both jobs.
21. **It must not haul the herd's feed.** Drop carrots (or whatever the herd eats) in a pen the Rogue covers: they must be left on the ground. Then cull an animal — its drops must be stored even when they are also feed (a **wolf** is the case that matters).
22. A fed, quiet herd must produce **no** "The animals aren't hungry." line while the Rogue is hauling.
23. **Legacy:** a companion that was on a haul chore before this batch resumes hauling after a relog; one on a cooking/fermenter chore comes back on **Provisioning**.

## 8i. Farming by Cultivator, doors, shared patches, Rename button (2026-09-04)  ✅ PASSED *(7 and 18 failed → fixed, retest under §8l)*

**Farming (reworked)**
✅ 1. Put a **Cultivator** in a **Support Mage's** pack (`Y`). Hovering it must now offer `[H] Set companion to farm`. A Rogue/Fire/Ice mage carrying one must be refused; a Support Mage without one must show no farm hint.
✅ 2. Press `H` on the ally standing on tilled ground → *"Ally works this field."* It farms the ground **where it stood**. Press `H` again → recalled.
✅ 3. **Take the Cultivator out of its pack** while it works → it says so and drops to Standby.
✅ 4. Hovering a **crop** or a **Cultivator on an item stand** must no longer offer any farm hint.
✅ 5. **The §8h #7 failure:** a field ~20 m across with the chest by the post. Crops at the **far edge** must be harvested and stored — no *"I have no chest to store this"*.
6. **One crop per field.** Plant a few carrots, then put carrot AND turnip seed in the chest. It must only ever plant **carrots** there. Clear the bed completely, leave only turnip seed → it may now start a turnip field, and must then stay on turnips.
7. **Seeds in the pack.** Put seed in the ally's own bag and none in the chest → it plants from the bag. With both, either source is fine.
8. **Relog on a BARE field** (everything harvested, nothing growing): the chore must resume, not stand the ally down after 60 s. This is the case the cultivated-ground restore check exists for.

**Doors**
9. Put a closed **wood door** between a worker and its station. It must open the door, walk through, and — once it has moved off and nobody is in the doorway — **close it again**.
10. A **Follow** companion must do the same following you through your base.
11. A **locked** door (one that needs a key) must be left alone.
12. A door inside **another player's** ward must not be opened; your own must.
13. It must not stand flicking a door open and shut when it has nowhere to go (park a Standby ally next to one).
14. **Dedicated server:** repeat 9 — this is the path where `Door.Interact` would have thrown on a null local player.

**Shared patches**
15. Press `H` on the same smelter twice with two free Fire Mages nearby → **both** are assigned and both work the row. The tooltip must read *"2 allies are working here."* and still offer the assign hint.
16. Recall one of them by pressing `H` **on that ally**; the other keeps working.
17. Two **Rogues** on one patch full of loose items: everything is stored and **nothing is duplicated** (count a stack before and after — this is what the drop latch guards).

**Rename**
18. Open a companion's pack (`Y`). The name field must be **read-only** with a **Rename** button beside it.
19. Click **Rename** → the field arms, the button reads **Save**. Type a name using letters that are also binds (`e`, `y`, `h`, `j`, `k`) → the panel must stay open and nothing must fire.
20. Click **Save** → the name commits, the floating name and the panel title update, the button reads **Rename** again, and binds work normally.
21. Pressing **Enter** must commit the same way and reset the button.
22. Closing the panel mid-rename must not leave your keys dead.

## 8j. Dead keys, the cook's fire, tidy planting, standing at the station (2026-09-04)  ✅ PASSED *(1 and 2 failed → fixed, retest under §8l)*

**Renaming (retest of §8i 18–22)**
1. Open a companion's pack, click **Rename**, and type a name containing every letter that is also a bind (`e`, `y`, `h`, `j`, `k`, `f`). **Nothing** may fire and the panel must stay open. This is the one that failed before: the gate was a `ZInput` prefix that another mod out-ordered, and it now runs through the shared `ModalPanels` postfix gate instead.
2. Test it **on the dedicated server with the other server mods loaded**, not only in single player — the failure was caused by another mod's patch order and would not reproduce alone.
3. Click **Save** → name commits, binds work again immediately.

**Cooking**
4. Hover a **cooking station** or a **fermenter** → it must ask for a **Support Mage**, not a Rogue.
5. A campfire cooking station whose fire has **burned out**: the mage must feed the fire **wood from a chest** and carry on cooking, rather than saying "The cooking fire is out!" and stopping.
6. With no wood anywhere in range it says *"The fire needs Wood!"*.
7. A hearth that can be **turned off**, left off but fuelled → the mage relights it.
8. A **Stone Oven** (no fire required) must be unaffected.

**Planting**
9. A **level 1** Support Mage must plant **4 at a time in a 2x2 block**, not one seed at a time.
10. The block must be **square and aligned** — successive batches line up into rows with each other and with crops already in the bed. No more scattered planting.
11. Level the mage past 4, then 7 → the block grows to 3x3, then 4x4.
12. With fewer seeds than the block needs, it plants what it has and stops cleanly.
13. A nearly-full field (odd gaps, no room for a whole block) must still get its gaps filled rather than reporting "There's no room left to plant."
14. Plants must not end up inside each other's grow radius — check none of the new bed is stunted/blocked.

**Standing at the station**
15. Workers must walk **up to** the furnace/cooker before working it, not service it from across the room.
16. Set `Chores/ChoreStationReach` to `1` → it must clamp to 3.2 and still work, NOT stall reporting the station unreachable (vanilla's follow stops at 3 m).

## 8k. Caste separation, stance handover, and seeing the whole field (2026-09-04)  ✅ PASSED

**Caste separation** (build a workshop holding BOTH a smelter and an eitr refinery)
1. Assign a **Fire Mage** at the smelter. It must feed the smelter and **never touch the refinery** — including not collecting the refinery's **eitr** off the ground. Watch the output specifically; that sweep is what made the castes look like they were sharing.
2. Assign an **Ice Mage** at the refinery. Same in reverse: it must not collect the smelter's bars.
3. Check the log for one `[chore] station '<prefab>' -> <Caste>` line per station type, and confirm each is what you expect. Include the **charcoal kiln**, **blast furnace**, **spinning wheel** and **windmill**.
4. A Fire Mage posted in a patch containing **only** refineries must stand down to Standby after ~60 s rather than standing there forever.
5. Hovering a cooking station or fermenter must ask for a **Support Mage** (retest of §8j #4).
6. **Legacy records:** a companion that was on a chore its caste no longer does (e.g. a Support Mage saved mid-feeding chore) says *"This is not my craft."* and drops to Standby on its first tick after the update.

**Stance handover**
7. Set a companion to **Follow**, then start any chore. It must **stop following you immediately** and go to its work. Repeat for **farming** specifically — that is the one that failed, because a farm post has no anchor object to follow instead.
8. Repeat from **Guard** and **Standby**: starting a chore must override them too.
9. Recall the ally → back to Follow at your side (or Standby if the chore ended by itself).

**Seeing the whole field**
10. Stand a farmer at the **edge** of a large (~20 m) field with bare soil at the far end. It must find and plant that ground, not report *"There's no room left to plant."*
11. **Raised/elevated** cultivated ground (a terraced bed) must be planted normally, and the seedlings must sit on the soil rather than under it.
12. A bed the **player** planted by hand (not on the mod's grid) with gaps in it: the ally must still fill the gaps one at a time rather than refusing.
13. On an empty field it must still lay out a **tidy aligned block** (retest of §8j #10).

## 8l. Input gate, pack seeds, burning food, foraging, destroyed stations (2026-09-05)  ✅ PASSED *(9 failed → fixed; 10-12 reworked, see §8m)*

**Input while renaming** *(retest of §8i 18 and §8j 1–2 — both failed before)*
1. **On the dedicated server, with the full mod set loaded**, open a companion's pack, click **Rename**, and type a name containing `e`. The panel must **not** close. Single player will not reproduce the original failure: it was another mod's `ZInput` prefix out-ordering ours.
2. Type letters bound to **other mods'** hotkeys — none of them may fire. (This is the half that needed muting `UnityEngine.Input` directly; `ZInput` gating never sees those.)
3. Click **Save** → the name commits and every key works again immediately, ours and other mods'.
4. Check nothing is **stuck**: after Save, walk, attack, open the inventory, use a mod hotkey.
5. Close the panel **mid-rename** (click away / take a portal / die) → keys must come back.

**Pack seeds** *(retest of §8i 7 — failed before)*
6. Put seed in the ally's **pack only**, with none in any chest → it must plant from the pack.
7. Seed in the **chest only** → still works.
8. Both → the pack is used first.
9. Do 6 again **after a relog**, so the pack contents are rebuilt from the ZDO. That is the case the old prefab-name match failed on.

**Cooking**
10. Load a cooking station and walk the ally away to a fermenter at the far end of the patch. When the food is done it must **come straight back for it** and nothing may burn.
11. A **full rack** of done food must be cleared in one visit, not one item per tick.
12. Nothing burns over a long unattended run with several stations.

**Farming stays in the field**
13. Post a farmer in a bed surrounded by **wild** ground with stones, branches, dandelions, mushrooms or berry bushes in range. It must harvest **only** the cultivated bed and leave all of it alone.
14. Crops on cultivated ground are still harvested normally, including at the far edge of a 20 m patch.

**Destroyed stations**
15. **Destroy a cooking station while an ally is working it.** No `NullReferenceException` in the log, and the ally moves on to another station (or stands down after 60 s if that was the last one).
16. Same for a smelter and a fermenter.

## 8m. One kitchen job per mage, and pack seeds after a relog (2026-09-05)  ✅ PASSED

**Provisioning stays put** (build a kitchen with a **wood cooking station**, an **iron cooking station**, a **stone oven** and a **fermenter**, all within 20 m)
1. Assign a mage at a **cooking station** → message reads *"Ally tends the cookfires."* It must work **both** cooking stations and must **never** touch the oven or the fermenter — including not collecting their output off the ground.
2. Assign a second mage at the **stone oven** → *"Ally tends the oven."* Ovens only.
3. Assign a third at the **fermenter** → *"Ally tends the brew."* Fermenters only.
4. All three working at once: nothing burns, nothing is left, and no two allies fight over a station.
5. Hovering the fermenter must **not** report the oven mage as working there (and vice versa) — coverage is per kind now.
6. Remove every station of one mage's kind, leaving the others → that mage stands down to Standby after ~60 s; the others carry on.
7. **Relog** with all three assigned → each returns to its own kind of station.
8. **Legacy:** a provisioning ally assigned *before this update* keeps working after a relog, and settles onto whichever kind it restored onto rather than continuing to wander.

**Pack seeds after a relog** *(retest of §8l #9 — failed before)*
9. **Bare field**, seed in the ally's **pack only**, nothing in any chest. Assign the farm chore, then **relog**. It must plant from the pack — this is the case that failed: the empty-bed path resolved seed names through `ObjectDB` at call time, which is not dependable where the chore runs.
10. Same, but with a crop already growing in the bed → still works (this half always did).
11. Check the log for `[farm] planting catalog built: N seed→sapling entries (M by shared name)` and that **M is not 0**.

## 8n. Every chest, every seed, and the right biome (2026-09-05)  ✅ PASSED

**Seeds found wherever they are**
1. Two chests in range of the field: the **nearest** holding only the harvest, a **further** one holding the seed. The farmer must find the seed. (It used to read only the nearest chest — this is the root of most of §8n.)
2. Seed split across **three** chests → all of it is used.
3. Seed in the **pack** and none in any chest → still works, bare field and planted field alike.

**Everything plantable**
4. Check the log for the one-time catalog census: `[farm] planting catalog built: N ...` followed by a `[farm]   <Seed> -> <sapling>` line per entry. Confirm the crops you use are listed — **`CarrotSeeds`, `Carrot`, `TurnipSeeds`, `Turnip`, `OnionSeeds`, `Onion`, `Barley`, `Flax`** should all appear (vanilla has two saplings per root crop: the seed grows the crop, the crop grows more seed).
5. A field of carrots with **only `Carrot`** (no `CarrotSeeds`) in the chest: it must plant the carrots to make seed, not say *"I've no more seed for this field."*
6. Barley and Flax (planted as the crop itself, no seed item) must work in the Plains.

**Crop switching**
7. A carrot field, carrot seed **exhausted**, turnip seed in the chest → it fills the rest of the bed with turnips rather than stopping.
8. While carrot seed lasts it must **keep planting carrots** — the one-crop preference still holds first.

**Biome message**
9. Put **Barley** in the chest with a farmer in the **Meadows** → the bubble must name the crop and the biome: *"Barley won't grow here — it needs the Plains."*
10. A crop that grows anywhere must not produce a nine-biome recital.

**The empty-field case, again**
11. **Bare cultivated field, nothing growing**, seed in the chest only → plants. Then repeat with seed in the pack only. Then repeat both **after a relog**.
12. If it still refuses, the `[farm]` log now says why within 30 s — seed types in reach, chests searched, cultivated cells found, clearance budget left. Please include that line.
13. Watch for frame hitching on a large field: the per-cell ground raycast is gone, so the scan should be noticeably cheaper than the previous build.

## 8o. Harvest by the armful, no planting in rocks, pins that stay (2026-09-05)  ✅ PASSED *(10 failed → fixed, see §8p)*

**Harvest scales with level**
1. A **level 1** Support Mage must harvest **4** ripe crops per tick, not one.
2. Level it past 4 and past 7 → 9 then 16, matching what it plants.
3. Fill the chests mid-harvest → it keeps what it already gathered that tick and only complains when it could store nothing at all.
4. It must still harvest **only** cultivated ground (§8l #13 still holds).

**No planting into things**
5. Put **rocks / surface stone** in the middle of a cultivated bed. The farmer must plant around them, never into them.
6. Same with **wild pickables** (mushrooms, thistle, berry bushes) standing on or beside the bed.
7. Same with **build pieces** — a fence post, a workbench, a wall corner inside the field.
8. Cross-check by hand: anywhere the ally plants, **you** must also be able to place that crop yourself. That is now literally the same test (`Plant.HaveGrowSpace`'s mask).
9. A normal open bed must still fill with a tidy block — the stricter rule must not make it refuse good ground.

**Map pins**
10. Leave a companion at your base and **sail to the far side of the map**. Its pin must stay on the map the whole way, at the spot you left it.
11. Return → the pin snaps back to live tracking as it loads.
12. **Relog** far from that ally → the pin is still there (restored from `BepInEx/config/LostScrollsII/pins.<world>.<playerId>.txt`; the log says `[map] restored N companion pin(s)`).
13. **Death:** a companion that dies far away loses its live pin and gains the skull death marker — not both.
14. **Totem:** sealing an ally removes its pin; **summoning it back restores** one.
15. A second character on the same world must not see the first character's pins.
16. Another player's companions must still never appear on your map.

## 8p. Every companion gets a pin (2026-09-05)  ✅ PASSED

1. With **three or more** companions out at once — including at least one recruited **before** this week's builds — **every** one of them must have a map pin. (Only the newest showed before: older allies had no `DE_CompanionId`, and the tracker keyed on it.)
2. Check the log on first load for `[recruit] backfilled a stable id for a legacy companion` — one line per old ally, once ever.
3. Each pin must carry the **right name**, and follow a rename.
4. No **doubled** pins: an ally whose id is backfilled mid-session must not end up with two markers at the same spot.
5. Walk far away → all pins stay (§8o #10 again, now with several allies).
6. **Relog** far away → all pins are still there. Allies that got their id backfilled are now saved; any still keyed by session only reappear when next seen, which is acceptable and should not leave a **ghost** pin at an empty spot.
7. Death and totem-sealing still remove the right pin, and only that one.

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
4. Companion **despawns** (sealed into a totem, or unloaded) → its **live** pin
   disappears.
5. **Player-icon look (2026-07-13, ✅ verified):** live pins use the **player icon**,
   **tinted** (`CompanionPinColor`, default amber) and **smaller** (`CompanionPinScale`,
   default 0.7) — visually distinct from your own player marker. Adjust the config →
   color/size change.
6. **Config:** set `ShowMapPins = false` → all companion pins vanish; set it back →
   they reappear.
7. **Save file:** relog → live pins are recreated (they're transient, `save = false`);
   confirm the map save file didn't accumulate stale companion pins.

**Pass:** exactly your own companions are pinned as tinted, smaller player icons,
pins follow them, labels match names, pins clear on despawn, toggle works.

### 14b. Death markers (2026-07-13)  ✅ PASSED

1. A companion of yours **dies** (killed by a monster) — **with items in its pack
   or empty**. A persistent **skull** pin labelled with the companion's **name** is
   placed on your map where it fell.
2. The marker **persists** across relog (`save = true`) until you click it away.
3. **Owner-only:** only the companion's owner sees the marker; other players don't.
4. **Config:** `ShowDeathMarker = false` → no marker is placed.

**Pass:** every companion death drops a named skull marker on the owner's map,
regardless of pack contents; it persists and is clickable-to-remove.

**MP (needs two clients):** each player should see **only their own** companions'
pins/markers — confirm player B's do **not** appear on player A's map, and vice versa.

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

### 16e2. Movable chest/storage panel (2026-08-03)  ⬜ UNVERIFIED
*(Replaces the old ComfyQuickSlots row-shift test, which passed 2026-07-05 — that mechanic is gone.)*
1. Open any **vanilla chest** on a fresh config. The panel sits **two inventory rows lower** than vanilla — with a slot mod like **ComfyQuickSlots** installed, its extra player-inventory row is fully visible, not hidden behind the panel.
2. Hover an **empty part** of the panel (its background, not a slot) → a faint white wash appears. **Hold left mouse and drag** → the panel follows the cursor. Release.
3. Item handling is untouched: **click/drag items** between the player inventory and the container, and press **Take All** — none of these move the panel.
4. Reopen the chest (and a **companion pack**, `Y`) → both open at the dragged position. `Interface/ContainerPanelOffset` now reads the `x,y` you dropped it at.
5. **Relog** → the position is still there.
6. Drag toward a screen edge → the panel **stops** with a sliver still on screen (≥48 px) and can be dragged back.
7. Run **`de_container_reset`** (or set the config to `auto`) → the panel returns to the default two-rows-below spot without a restart.

**Pass:** default placement clears mod-added inventory rows; the panel drags from empty space only, never from item slots/buttons; the position persists across reopen and relog; it can't be lost off screen; reset works.

### 16e3. BiomeLords compatibility  ✅ PASSED (2026-07-13) / ⬜ RE-VERIFY (2026-08-03)
1. With **BiomeLords** installed (which ships this same move-the-chest-UI feature), open a companion pack and a regular chest.
2. BiomeLords' own chest-UI positioning works normally — LSII **stands down entirely**: no default offset, **no drag surface** (hovering the panel background shows no wash and dragging does nothing). The BepInEx log shows `[inventory] container-panel positioning: OFF (…, BiomeLords=True)`.
3. Manual override: `Interface/MoveContainerPanel = false` disables LSII's positioning regardless of what's installed (and restores the panel to the game's own spot mid-session).

**Pass:** with BiomeLords present the chest UI position is owned entirely by BiomeLords; LSII adds nothing. Without BiomeLords, §16e2 applies.

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

## 17. Duel ladder & ranking (requires **two players**)  ⬜ UNVERIFIED

Phase A/B of the competitive suite (docs/Ranking.md). The duel **double-win fix**
(single subdue) is a prerequisite — verify it here too.

**Setup:** two players (ideally a listen host + one client), each with at least one
recruited companion. On a fresh world the ladder file starts empty.

1. **Single subdue (bug fix, §9 revisit):** run a duel to its end. The win is
   announced **exactly once** — one "wins the bout!" bubble, one chat shout, one
   center message. No double announcement even though the loser sits at ~5% HP and
   regens. (Was the double-win bug.)
2. **Record created:** after the first decided duel, run **`de_ladder`** in the
   console. Both companions appear — winner with `1W/0L`, loser `0W/1L` — each row
   showing `#rank  rating  name (owner)  W/L`. The winner's rating is above 1000,
   the loser's below.
3. **Owner + companion in the record:** the row's name is the companion's display
   name and the owner name in parentheses matches the recruiting player — even if
   that player is offline/out of range (names are snapshotted).
4. **Name-tag rank:** with `ShowRankOnNameTag` on (default), a ranked companion's
   floating name shows a blue `#rank` after the `★level` badge. Toggle the config
   off → the rank disappears (owner tag + star remain).
5. **Persistence:** relog / restart the world → `de_ladder` still shows the same
   standings (JSON file under `<save>/LostScrollsII/ladder.<world>.json`).
6. **Totem carry-through (identity):** seal a ranked companion into a Communion
   Totem and summon it back (§12). Win another duel with it → its **existing**
   record advances (same row, `2W`), not a new duplicate row. (Confirms
   `DE_CompanionId` survives seal/summon.)
7. **Anti-farm cooldown:** immediately re-duel the **same two** companions and let
   the same one win again within `PairCooldownSeconds` (default 300). The W/L still
   increments, but the **ratings do not move** the second time. After the cooldown,
   ratings move again.
8. **Client snapshot:** the **non-host** player runs `de_ladder` and sees the same
   standings (pushed from the server on join/spawn and after each match).
9. **`dvergr_rank_changed` reward:** when a companion climbs into the top 3, the
   winning player sees the ServerGuide "Among the Champions" message
   (`ls_ladder_top3`) + the chat broadcast, and the buff is granted **to that
   player**. (Confirms the rank event routes to the winner's client, not the server.)
10. **Season reset (host):** run **`de_season_reset`** on the host → `de_ladder`
    is empty, and a `ladder.<world>.season1.json` archive appears next to the live
    file.

**Watch:** ratings should be server-authoritative — a client can't move the ladder
except by reporting a real duel win it actually landed. On a **dedicated** server
(no local player) the rank-changed reward path still routes to the winner client;
confirm it fires there too.

---

## 18. Party duels (requires **two players**)  ⬜ UNVERIFIED

Phase C of the competitive suite (docs/Party-Duels.md). Mechanics only — party
bouts do **not** feed a ladder yet (that's Phase D).

**Setup:** two players, each with **2+** recruited **Follow**-stance companions
nearby (so both sides field a real team). Default party key is **`K`**.

1. **Form a team:** Player A hovers **their own** companion and presses `K` →
   "Your party (N) squares up…" and every eligible nearby Follow ally enters party
   mode ("joins the melee"). Confirm the count respects `MaxPartySize` (default 4).
2. **Both sides in:** Player B does the same → the two teams seek each other and
   fight; a companion targets **any** enemy-team member, not one fixed rival.
3. **Non-lethal bench (bug-fix reuse):** a member knocked to ~5% HP is **benched**
   once ("subdued and steps out of the melee") — it stops fighting and can't be
   re-hit into the match (no double bench even as its HP regens). The **rest of the
   match continues**.
4. **Win by attrition:** when one side has **no un-benched members left**, each
   surviving winner stands down ("side stands victorious!" / "wins the party
   duel!") and gains **team-size-scaled XP** — verify a lone winner over a bigger
   team gets more, and members of a team that won by outnumbering get less each.
5. **Owner-only + toggle-off:** pressing `K` on **another player's** companion is
   refused. Pressing `K` again on your own (while your party is up) stands the
   **whole team** down ("Your party (N) stands down").
6. **Owner leash / forfeit:** if Player A logs out or walks >40 m away mid-melee,
   A's team withdraws ("loses sight of its owner") and B's side wins by attrition.
7. **Immunity:** while in a party duel, members ignore and are immune to players
   (even with PvP on), creatures, and same-owner allies — only enemy-team members.
8. **Mode exclusivity:** a companion already on a chore / in a 1v1 duel / feral is
   not pulled into the party; `K` and `J` and stance-cycle are mutually blocked
   while busy.
9. **Relog ends it:** logging out mid-melee and back in leaves companions **not**
   in party mode (the `DE_PartyDuel` flag clears on spawn).

**Watch:** the cross-client bench → win-detection chain (a benched member on one
client must drop out of the other client's enemy count). Two players / a listen
host + client is the real test.

---

## 19. Party ladder & ranking (requires **two players**, ideally 2v2+)  ⬜ UNVERIFIED

Phase D of the competitive suite (docs/Party-Duels.md). Builds on §18 (party
mechanics) + §17 (the shared ladder store/sync).

1. **Record created:** run a party duel (§18) to a decision. Afterward run
   **`de_party_ladder`** → both owners appear (winner `1W/0L` above 1000, loser
   below), each row showing `#rank  rating  owner  W/L  (team N)`.
2. **Owner + companions record:** the winning row's team size matches the number of
   companions that fought for it, and the owner name is the winning player. (Under
   the hood the record snapshots each member's companion id/caste/level.)
3. **One report per match:** with **multiple** winners (a 2v1 or 2v2 you win with 2+
   survivors), the ladder moves **once** — winner `1W`, not `2W`; a single "Party
   Victory" ServerGuide message, not one per surviving companion.
4. **Team Elo:** beating a **higher-rated** party moves your rating more than
   beating a lower-rated one; the loser drops.
5. **`dvergr_party_duel_won` reward:** the winning player sees the "Party Victory"
   notice ("Your party of N bested <opponent>'s team!") on each win.
6. **`dvergr_party_rank_changed` reward:** climbing into the top 3 shows the "A
   Renowned Company" message + chat broadcast + buff, granted **to the winning
   player**.
7. **Persistence:** relog/restart → `de_party_ladder` unchanged (same per-world JSON
   as the 1v1 ladder; a `parties` list).
8. **Season reset:** `de_season_reset` (host) clears the party ladder too.
9. **Client view:** the non-host player's `de_party_ladder` matches the host's.

**Watch:** the once-per-match latch (step 3) is the key correctness point — if you
ever see a party win double-count, the owner-pair dedup window needs tuning. Also
confirm the loser roster is captured (the winner accumulates it during the fight,
before the losers are benched).

---

## 20. Tournaments (requires **3–4+ players**)  ⬜ UNVERIFIED

Phase E (docs/Tournaments.md). Server-authoritative bracket; matches are ordinary
player-run duels the server watches. Test on a listen host + clients.

**1v1 tournament:**
1. **Start (host):** `de_tournament start 1v1` → "open for registration". Each
   player runs **`de_tournament join`** while hovering the companion they'll field
   → "Registered '<name>' (N)" and the ServerGuide "Entered the Tournament" raven.
2. **Bracket:** `de_tournament bracket` lists entrants with seed ratings (from the
   duel ladder). `de_tournament begin` (host) seeds by rating, builds round 1, and
   each paired player gets the "Round 1: you face <opponent>" center message. An
   odd entrant count gives the **top seed a bye** (shown in the bracket).
3. **Play a match:** the two paired players duel (`J`) **with their registered
   companion**. When the duel resolves, the bracket advances — `de_tournament
   bracket` shows the winner and the next round's pairings, with new match messages.
4. **Champion:** the final winner gets the "Tournament Champion" intro beat + the
   prize bundle (chat shout, buff, coins) and is written to the **Hall of
   Champions** (`de_champions`). Only the champion is rewarded.
5. **Forfeit / no-show:** `de_tournament forfeit <playerName>` (host) advances that
   player's opponent. `de_tournament cancel` (host) clears everything.

**Party tournament:** repeat with `de_tournament start party`; each player runs
`de_tournament join` (registers their **owner**, seeded from the party ladder),
and plays each match as a party duel (`K`). The `dvergr_party_duel_won` report
resolves the bracket match.

**Persistence:** with a tournament mid-round, restart the world → it **resumes** at
the same phase/round (`tournament.<world>.json`).

**Watch:**
- A match only advances if the two paired entrants actually duel with the
  **registered** entrant (1v1: the registered companion; party: any of the owner's
  companions). A duel between non-entrants must **not** touch the bracket.
- Match/champion messages must reach the **correct** player only (broadcast is
  name-filtered client-side).
- **Deviations (by design, not bugs):** no auto-teleport to an arena, no enforced
  arena ward, and admin subcommands only work on the host/server console.

---

## 21. Ranking & tournament UI + Discord + party naming + escrow (competitive UI batch)  ⬜ UNVERIFIED

New player-facing UI, Discord broadcasting, party naming, and escrow-based
tournament entry (docs/Ranking.md, docs/Party-Duels.md, docs/Tournaments.md).
Built but **unverified**; needs 2 players (4 for a full bracket) and a configured
Discord webhook for the announcement checks.

### 21a. Ranking board UI (`F6`)
1. Press **`F6`** → a darkened rune-panel "Dvergr Rankings" listing the **Duel
   Ladder** (rank, rating, companion, owner·caste, W/L) and the **Party Ladder**
   (rank, rating, party name/owner, W/L, team size). Escape closes it.
2. It must **match** `de_ladder` / `de_party_ladder`, and update after a duel
   (reopen to refresh). Empty ladders show a hint line, not an error.

### 21b. Party naming
1. `de_party_name The Ironhands` → "Your party is now named 'The Ironhands'."
2. The name shows on **`de_party_ladder`**, the F6 board's party section, and in
   party-duel / party-rank announcements (`{partyName}`). Confirm it **persists**
   across a relog (it lives in the server-side `PartyRecord`).

### 21c0. Guidance loads from the `LostScrollsII/` subfolder
1. Confirm the guidance YAMLs sit in `BepInEx/config/ValheimServerGuide/**LostScrollsII/**`
   (both the dedicated server and the client profile) and that **no stale flat copies**
   remain at the top level — two copies = duplicate ids.
2. On load, the ServerGuide log should list the entries from the subfolder (recursive
   loading, ServerGuide 0.8.0+). If nothing fires at all, this is the first thing to check.

### 21c. Discord announcements (needs a webhook set in ServerGuide config)
With `DiscordWebhookUrl` set on the server, each of these posts **once**:
1. Any **1v1 duel win** → "⚔️ <companion> (<player>'s <caste>) won a duel against
   <opponent>!" (tournament matches count too — same trigger).
2. Any **party duel win** → "🛡️ <partyName> (led by <player>, N strong) won…".
3. A companion **reaching #1** on the duel ladder → "👑 … claimed #1 …" (fires only
   on a genuine climb to first — `dvergr_rank_first`). Party #1 likewise.
4. A **tournament champion** → "🏆 <player> is the <mode> champion…".
   With **no** webhook set: the chat lines still fire and the log notes the skip.
5. Reward-message templating: confirm `{companionName}`/`{rank}`/`{winSize}`/`{mode}`
   now expand in chat_message/discord rewards (previously only `{player_name}` did).

### 21d. Tournament panel + totem-slot registration (`F7`)  — escrow & auto-summon
1. Seal a companion at the Incinerator (§12) so you hold a **Communion Totem**.
2. Admin: press **`F7`** → the tournament panel; click **[Admin] Start 1v1**.
3. Each player opens `F7`, clicks **Lock Totem → Enter** → the totem **leaves the
   inventory** and the entry appears ("You are entered as '<name>'"). Console
   fallback: `de_tournament join` (seals the **hovered** live companion).
4. **Withdraw** (button or `de_tournament withdraw`) returns the totem to the bag.
   **Admin release** (`de_tournament release <name>`) returns another player's totem.
5. Admin **[Admin] Begin** → bracket built. Admin **[Admin] Activate Round** →
   each pairing's companions are **auto-summoned** beside their owners in duel mode.
6. Confirm each summoned companion fights **only its assigned opponent** (run two
   matches at once → no cross-targeting). Winner is subdued-resolved as usual.
7. On match resolve the companions **despawn back into their totems** (reseal); the
   winner advances (next **Activate Round** re-summons the leveled-up companion).
   Losers are eliminated but their totem is **returned at tournament end / cancel**.
8. **Party tournament:** Start Party; each player locks up to `MaxPartySize` totems
   (or `de_tournament join` seals nearby Follow allies); Activate Round summons the
   whole team; it fights as a party; team reseals on resolve.

**Watch:**
- Totems must never be **lost**: a rejected join, withdraw, release, cancel, and
  tournament completion all return the totem(s) (bag, or dropped at your feet if full).
- Admin buttons appear only for **admins/host** and the server **re-verifies** admin
  on the RPC (a non-admin crafting `LSII_AdminCmd` is denied + logged).
- While the `F7` panel is open, input is taken over like a vanilla menu: the mouse
  **cursor is free** to click the buttons, and the **camera + all player controls**
  (move / look / attack / use / jump / hotbar) are **blocked** underneath it
  (`Player.TakeInput` + `GameCamera.UpdateMouseCapture` patches). Confirm the camera
  doesn't drift while clicking, and that Escape / `F7` closes it and **restores**
  normal play + the captured cursor.

## 22. Bog Witch Dvergr rites (Quest pack `guidance.bogwitch-rite.yaml`)  ⬜ UNVERIFIED

Two weekly quest chains that spawn a wild, recruitable Dvergr in the Swamp without
needing the Mistlands. Requires **`ProfMags-TraderOverhaul`** installed (for the Bog
Witch trader) alongside the Quest pack. Pure ServerGuide guidance — no Lost Scrolls II
code involved, so nothing here can be broken by a base-mod bug, only by the guidance
YAML itself or the interaction with the always-on Communion Rite.

### 22a. The Rogue rite
1. Hold E on the **Bog Witch** → pick "Tell me of the stirring." → she reveals the rite
   ("An Echo in the Mire"). Confirm the topic then **disappears** from her conversation
   options (it's `once: true`).
2. Kill **2 `Draugr_Elite`** anywhere in the Swamp (party members' kills should also
   count — `share_progress: true`). Confirm the rune panel fires ("The Shell Breaks")
   and a **wild, untamed `Dverger`** spawns next to the player.
3. Confirm the spawned Dverger is **neutral until struck** (vanilla behavior) and, once
   subdued and communed, registers as **Rogue caste** (no staff detected).
4. Kill 2 more Draugr Elite immediately after — confirm the rite does **not** re-fire
   (weekly cooldown) until `SeenTracker.CooldownReady` allows it again.

### 22b. The mage rite (gated + night-only)
1. Before completing 22a: confirm the Bog Witch does **not** offer "the deeper echo" —
   `ls_bogwitch_mage_intro` requires `ls_bogwitch_echo_rite` to have fired at least once.
2. After 22a's rite has fired once, talk to her again → the new topic ("Tell me of the
   deeper echo.") should now appear.
3. `Wraith` only spawns **at night** — confirm the kill trigger only counts night kills
   (a daytime Wraith simply shouldn't exist to kill; nothing extra to gate in the YAML).
   Kill 2 Wraith → confirm a wild, untamed **`DvergerMage`** spawns, and that its caste
   (Fire/Ice/Support, read from whatever staff it spawned with) varies across repeated
   completions rather than always being the same caste.
4. Confirm this rite also respects its own independent weekly cooldown, separate from
   22a's.

**Watch:**
- **Multi-quest picker** — if a server also runs Hearthbound's own Bog Witch daily-rites
  conversation (`lw_giver_bogwitch`), confirm both conversation entries coexist via
  ServerGuide's multi-quest picker rather than conflicting.
- **`requires` gating survives a relog** — confirm `ls_bogwitch_mage_intro`'s gate stays
  satisfied (and the topic stays available) after a disconnect/reconnect, not just
  within the same session.
- **Spawn safety** — the Dverger/DvergerMage spawns in a ring near the player
  (ServerGuide's `spawn_creature` reward); confirm it never spawns inside terrain, water,
  or a player build, especially near the Sunken Crypts' tight geometry.

## 23. Bounty hunting — Phase A: feature gate & sync  ✅ PASSED

Phase A ships no gameplay — it only proves the triple-dependency gate and the
server→client feature flag behave (see [Bounty-Hunting.md](Bounty-Hunting.md)).
Everything here is checked with the new `de_bounty_status` console command and the
boot log line `[bounty] feature gate: …`.

**On the server/host:**
- [ ] With **all three** of BiomeLords, ServerGuide and Valheim Donations installed,
      the boot log reads `[bounty] feature gate: ON (…, isServer=True, config=True)`
      and `de_bounty_status` reports **ACTIVE** with all four sub-conditions true.
- [ ] Remove **each** dependency in turn (three runs): the gate logs `OFF` and
      `de_bounty_status` names the missing one as `False`. Confirm BiomeLords is
      detected despite its mixed-case GUID (`com.taeguk.BiomeLords`).
- [ ] Set `Bounty/Enabled = false` with all three present: gate `OFF`, `Config: False`.
- [ ] **Singleplayer / local world** with all three installed — the gate should still
      be ON only because a local world *is* the host; on a pure client it must be OFF
      (see below). This is the check that the server-only rule isn't accidentally
      inverted.

**On a connecting client:**
- [ ] Joining a **qualifying** server, `de_bounty_status` reports *"ACTIVE on this
      server"* and the log shows `[bounty] server reports bounty hunting ACTIVE`.
- [ ] Joining a **non-qualifying** server (any dependency missing, or `Enabled=false`),
      it reports *"not available on this server"*.
- [ ] **Death/respawn and reconnect** both re-request the flag (it must not go stale
      or flip to available on a server that never sent it).
- [ ] **Server-hop**: connect to a qualifying server, disconnect, then join a
      non-qualifying one — the client must report *not available*, proving the flag is
      cleared on `ZNet.OnDestroy` rather than carried across worlds.

**Watch:** the client's default is `false` (teaser, once Phase F lands), so a dropped
or missed push shows as "unavailable" rather than a live board the player can't use.

## 23b. Bounty hunting — Phase B: location sampling & map pins  ✅ PASSED

Phase B still spawns no creature. It proves the sampler only ever picks reachable
land in the four bounty biomes, and that pins appear and clear correctly. Driven by
`de_bounty_sample [count]` (rolls candidates through the real sampler and pins each)
and `de_bounty_sample_clear`.

**Sampling correctness — the core requirement:**
- [ ] `de_bounty_sample 20` reports 20/20 found, and every line names one of
      **BlackForest / Swamp / Mountain / Plains** — never Meadows, Mistlands,
      Ashlands, DeepNorth or Ocean.
- [ ] Every reported height is comfortably above sea level (30) — with the default
      `WaterMargin` of 3, nothing below ~33.
- [ ] Run it several times (100+ candidates total). The rejection tally in the output
      should show `island/coast` and `water` rejections actually happening — if those
      are always 0, the land check isn't doing anything and the test is worthless.
- [ ] **Walk or fly (`devcommands` → `fly`) to at least one pin per biome.** This is
      the check that matters: the target spot must be dry, walkable land, not a
      shoreline, not a rock in the sea, not a cliff face. Confirm you can stand there.
- [ ] **Islet check specifically** — pick the pin nearest a coastline and confirm the
      landmass extends at least ~80 m in every direction (the `LandCheckRadius`
      guarantee). A pin on a small island is a **fail**, and the first thing to
      re-tune.

**Pin lifecycle:**
- [ ] Pins appear on the minimap and the full map at the reported coordinates, using
      the boss icon tinted red — visually distinct from companion pins (player icon,
      orange) and companion death markers (skull).
- [ ] `de_bounty_sample_clear` removes **all** of them, leaving no orphans.
- [ ] Sample some pins, then **relog without clearing**. The pins must be **gone** on
      return (they're `save = false`) — a bounty pin must never persist into the saved
      map. Confirm no stray pins accumulate in the map file across several cycles.
- [ ] Sample pins, then **exit to the main menu and load a different world**. No
      leftover pins, and no errors in the log (this exercises the Minimap-rebuild
      guard — stale `PinData` handles from the destroyed map must be dropped, not
      poked).

**Tuning notes:** if placement fails or takes many attempts, the tally names the
cause — raise `SampleAttempts`, or lower `LandCheckRadius` (islet protection) /
raise `MaxHeightVariance` (mountain bounties on steep ground) as appropriate. These
are first-pass values and expected to move after this session.

## 23c. Bounty hunting — Phase C: scaled, auto-hostile targets  ✅ PASSED

> **2026-08-20:** everything below passed except the star display — tiers 1–2 showed
> no stars at all, because vanilla renders level 1 as zero stars and `EnemyHud` has
> only two star rects. Fixed by spreading the star level across the tier range
> (1/2/2/3/3 → 0/1/1/2/2 stars) and adding a **tier name badge**. Re-check just the
> "tier is visible" block below.

**Tier visibility (re-check after the 2026-08-20 fix):**
- [ ] `de_bounty_spawn 1` … `de_bounty_spawn 5` — each target's floating name shows
      a red **`[T<n> <Name> Bounty]`** badge (`[T1 Wanted Bounty]` … `[T5 Accursed
      Bounty]`), so the tier is unambiguous even where vanilla shows no stars.
- [ ] Escort minions show a paler **`[Escort]`** badge instead.
- [ ] Vanilla stars are still visible on higher tiers (0/1/1/2/2 stars for tiers
      1–5) and now actually differ between low and high tiers.
- [ ] Badges render as **text, not empty boxes** (the serif font can't draw exotic
      glyphs — everything here is ASCII).
- [ ] A bounty target **communed into an ally** loses the bounty badge and shows the
      normal gold `★N` companion badge instead — never both.

Phase C spawns the real thing: a scaled Dvergr that hunts on sight, with an escort.
Driven by `de_bounty_spawn [tier 1-5] [far]`. There is still no board, no reward and
no quest gate — killing or communing a target just ends that fight.

**Scaling reads correctly:**
- [ ] `de_bounty_spawn 1` through `de_bounty_spawn 5` each report a target whose HP
      roughly matches the curve (≈3 / 4.8 / 7.7 / 12.3 / 19.7× a normal Dverger's
      ~100–200 HP) and whose star level rises to at most 3.
- [ ] Escort size grows with tier — 1 minion at tier 1 up to 5 at tier 5 (capped by
      `MaxMinions`). Confirm escorts are visibly **weaker** than the target (they're
      one tier down) and mostly melee Dvergr rather than all mages.
- [ ] Higher tiers mix in **mage** targets (Fire/Ice/Support) more often than tier 1,
      which should be plain Dverger.

**Auto-aggression — the defining behaviour:**
- [ ] Spawn one and walk toward it. It must **attack on sight, unprovoked** — every
      other Dvergr in the mod stays neutral until you hit it. This is the single most
      important check in this section.
- [ ] It notices you from noticeably further than a normal Dvergr
      (`AlertRangeMultiplier`).
- [ ] The **"corruption awakens" message must NOT appear** when bounty creatures
      spawn or aggro. If it does, the suppression guard failed.
- [ ] Walk away and leave it alone: it should stay near where it spawned
      (`RoamRadius`), not wander off across the map away from its pin.

**Communing a bounty target (requirement 1):**
- [ ] Fight one down to ≤20% HP and complete the Communion Rite (hold Block). It must
      recruit **exactly like any other Dvergr** — same channel, same failure rules.
- [ ] Once freed it must behave as a normal companion: **no lingering hostility**, it
      follows you, and it does **not** revert to hostile after a few seconds (the
      3 s aggression tick must have stood down).
- [ ] **Relog with that freed bounty companion.** It must come back as your ally, not
      re-armed as a bounty (the restore patch must skip creatures carrying
      `DvergrCompanion`).

**Persistence and health handling:**
- [ ] Spawn a tier 4 target, damage it to roughly half, then force a zone reload
      (walk far away and back, or relog). It must return **still wounded at the same
      scaled max HP** — not healed to full, and not reset to a normal Dverger's HP.
      This is the `DE_BountyInit` behaviour and the most likely thing to be subtly
      wrong.
- [ ] After that reload it must still be hostile and still hunt you.
- [ ] Kill a target and confirm nothing errors in the log as it dies and its
      component stands down.

**Multiplayer (worth a two-client pass):**
- [ ] Spawn a bounty, have the **other player** approach so ownership moves to their
      client. Scaling must not double-apply (HP shouldn't jump) and the creature must
      stay hostile to both players.

**Known gap (by design, not a bug):** `de_bounty_spawn ... far` samples a real remote
location but that zone almost certainly isn't loaded, so nothing spawns there. Posting
a location and spawning creatures when a hunter arrives is Phase H's job — Valheim
doesn't simulate unloaded zones, so the split is deliberate.

## 23d. Bounty hunting — Phase D: resolution & rewards  ✅ PASSED

Closing a bounty now pays out. Items come from `guidance.bounty-rewards.yaml` (no
loot table exists in the mod), and Valcoin is a rank-scaled **chance**, reward-only.

> **Requires a ServerGuide rebuild** (new `dvergr_bounty_resolved` /
> `dvergr_bounty_valcoin` triggers, the `tier:` filter, and the `{tier}`/`{tierName}`/
> `{method}`/`{bountyBiome}` vars) — deployed 2026-08-21. The **coin half additionally
> needs `ls_bounty_t1`…`ls_bounty_t5` defined in the donations mod's
> `valcoin_quests.yaml`**; without them the key is set and silently worth nothing
> (server logs `Unknown quest '<id>'`).

**Item rewards (the always-pays half):**
- [ ] `de_bounty_spawn 1`, kill the target → the tier 1 bundle arrives (Coal, Bronze,
      Sausages, 50 Coins) and a top-left message names the tier and biome.
- [ ] Repeat for tiers 2–5; each grants a **visibly better** bundle, and tiers 3–5 use
      the rune/intro display rather than a corner message.
- [ ] **Check the server log for skipped items.** A wrong prefab id is skipped with a
      warning rather than erroring, so a bundle can quietly under-deliver — this is
      the most likely thing to be wrong on first run, since the item ids were authored
      from vanilla knowledge and not yet confirmed in-game.
- [ ] Tier 5 additionally posts a chat line and a **Discord** message (needs
      `DiscordWebhookUrl` set server-side).

**Killing vs. communing (requirement 1):**
- [ ] Kill a target → reward text reads `(killed)`.
- [ ] **Commune** a target instead → the **same tier bundle** is granted and the text
      reads `(communed)`. Both must pay; the Rite is not a way to skip the reward.
- [ ] After communing, you keep the companion **and** got the bounty reward.

**Only the target pays:**
- [ ] Kill the **escort minions** only, leaving the target alive → **no reward**, no
      message, pin stays.
- [ ] Kill the target → reward fires once and the **map pin disappears**.

**Paid exactly once:**
- [ ] Kill a target and watch for a **double** reward (the idempotency latch). One
      bundle only.
- [ ] Subdue a target to ≤20%, commune it, then kill that companion → **no second
      payout**.

**Valcoin (rank-scaled chance, reward-only):**
- [ ] `de_bounty_chance` lists a per-tier percentage and your best duel/party
      standing. Unranked → base chance only; higher tiers → higher chance.
- [ ] Earn a duel/party rank inside the top 10, re-run it → **chance visibly rises**.
      This is the requirement-4 link between the ladders and the coin payout.
- [ ] Resolve bounties until the log shows `[bounty] valcoin roll … => PAID`, then
      confirm the coins actually land in the donations ledger (F4). If the roll says
      PAID but no coins arrive, check the donations log for `Unknown quest`.
- [ ] Confirm **no bounty text ever promises coins** — the payout depends on the
      donations backend's own daily caps, so the in-world text deliberately doesn't
      mention them.
- [ ] Confirm there is **no way to spend Valcoin** on bounties anywhere (re-rolls,
      better odds, tier access). Reward-only is a hard guardrail from the donations
      mod's own docs.

**Watch:** the reward-tier bonus for top bounty hunters (requirement 5) is wired but
returns 0 until Phase E builds the bounty ladder, so effective tier == base tier for
now. That's expected, not a bug.

## 23e. Bounty hunting — Phase E: the bounty leaderboard  ✅ PASSED

> **2026-08-22:** everything passed. `de_bounty_season_reset` initially refused a
> **remote admin** (it gated on *being* the server rather than on being an admin);
> both season resets were moved onto the admin-authenticated RPC and re-verified.
> `de_season_reset` (duel ladder) had the same flaw and was fixed with it.

Answered bounties now accumulate a persistent, server-authoritative standing that
feeds back into reward quality (requirement 5).

**Scoring:**
- [ ] Answer a tier 1 bounty → `de_bounty_ladder` shows you with **10 points**
      (`PointsPerTier` default 10 × tier).
- [ ] Answer a tier 4 bounty → **40 points** added, not 10. Tier-weighting is what
      stops the board being won by grinding easy postings.
- [ ] **Kills and communes tally separately** — the row reads `N felled / M freed`
      and both increase the same point total.
- [ ] `best:` shows the hardest tier you've ever answered, and never goes down.

**Persistence (the serializer risk):**
- [ ] Answer a few bounties, then **restart the server**. The standings must come
      back intact. An empty board after restart means the hunter *list* was dropped —
      the exact `JsonUtility` failure this store was written to avoid (it uses
      `CompetitiveJson`, so this should hold, but it's the one thing worth proving).
- [ ] Check `<save>/LostScrollsII/bounty.<world>.json` exists and is readable, with a
      populated `hunters` array.

**Client sync (needs a second player or a client connection):**
- [ ] A **connecting client** sees the same standings (pushed on join) via
      `de_bounty_ladder` and the `F6` board.
- [ ] When another player answers a bounty, your board updates without a relog.
- [ ] `F6` shows a **Bounty Hunters** section below the duel and party ladders, and
      that section is **absent** on a server where bounty hunting isn't running.

**Reward bonus (requirement 5 — the point of the ladder):**
- [ ] While **unranked or below `LeaderboardBonusRank`** (default top 3), a tier 2
      bounty pays the **tier 2** bundle.
- [ ] Climb into the top 3, then answer another tier 2 bounty → it pays the **tier 3**
      bundle (+1 effective tier), and the log line shows `tier 2 -> effective 3`.
- [ ] The bonus is **capped by `MaxTierBonus`** (default 1) — never more than one tier.
- [ ] A tier 5 bounty with the bonus still pays tier 5 (clamped at the top).
- [ ] **Points are scored at the BASE tier, not the bonused one** — a top-3 hunter
      answering a tier 2 posting gains 20 points, not 30. Otherwise standing would
      compound into itself.

**Season reset (re-check after the 2026-08-22 fix):**
- [ ] `de_bounty_season_reset` run by an **admin connected as a client** now works —
      it reports "Bounty season reset requested." and the server replies with how many
      hunter records were archived.
- [ ] `bounty.<world>.season<N>.json` is written alongside the live file, and the live
      board is empty afterwards.
- [ ] Clients see the cleared board without a relog.
- [ ] `de_season_reset` (duel ladder) likewise works for a **remote admin** — it had
      the same host-console-only flaw and was fixed in the same change.
- [ ] A **non-admin** player running either command is refused by the server (the
      server re-verifies admin rights itself; the console's own admin flag isn't
      trusted).

## 23f. Bounty hunting — Phase F: the Wanted Board + UI  ✅ PASSED

> **2026-08-23:** all passed. The inventory button took several passes to place;
> it settled at the **top centre of the inventory screen**, positioned deliberately
> with `LayoutElement.ignoreLayout` so the container's layout group can't move it.

The board is now real: postings are generated, taken, travelled to, and the camp
spawns on arrival. Opened with **F8** or the **Bounty Board button in your
inventory**.

**The board generates and syncs:**
- [ ] On a qualifying server, `de_bounty_board` lists **3 open postings**
      (`MaxBoardEntries`), each naming a tier and one of the four biomes.
- [ ] `F8` opens the panel and lists the same postings with **live distances** that
      update as you move.
- [ ] A **second player** sees the same board. When one player accepts a posting, it
      disappears from the other's list without a relog.
- [ ] **Restart the server** — postings persist (`board.<world>.json`).

**Accepting:**
- [ ] Accept a posting → it's marked on your map, the panel shows it as your posting,
      and the Accept buttons are replaced by **Abandon Bounty**.
- [ ] With `MaxActiveBountyPerPlayer = 1`, you cannot accept a second.
- [ ] Two players **racing for the same posting**: exactly one gets it, the other is
      told who took it. (The server decides — a client can only ask.)
- [ ] **Abandon** → the pin clears and the posting returns to the open list for
      anyone, rather than vanishing.

**Travelling and arrival spawn:**
- [ ] Travel to your posting. Nothing exists there until you're within ~80 m
      (`ArrivalRadius`), then the camp spawns and you get "You have found the … one."
- [ ] The spawned target matches the posting's **tier and biome** and carries the
      right `[T<n> … Bounty]` badge.
- [ ] **Relog while approaching**, then return — you must get **one** camp, not two
      (the server records the spawn).
- [ ] Answer it (kill or commune) → reward pays as in §23d, the **pin clears**, the
      posting leaves the board, and a **new posting** appears to replace it.

**Inventory menu bar + panel behaviour:** (see also §27 for the full bar)
- [ ] The **Bounty Board button** sits in the menu row at the **top centre of the
      inventory screen** and stays there across opens, relogs and window resizes
      (the row opts out of the container's layout group, so nothing shuffles it).
- [ ] It opens the panel (closing the inventory as it does).
- [ ] It's still there after a **relog** and after **dying/respawning** (InventoryGui
      is rebuilt each world load).
- [ ] While the panel is open: the camera is frozen, WASD does nothing, and clicking a
      button does **not** swing your weapon. Escape and F8 both close it. (Same gates
      as the F7 panel — they're now shared, so a regression here would hit both.)
- [ ] Open **F7 and F8** in sequence and confirm neither leaves input stuck after
      closing.

**The teaser (players not on a bounty server):**
- [ ] In **single-player**, or on a server missing any of the three mods, `F8` and the
      inventory button still work and show the explanation panel — not an empty board
      and not a dead key.
- [ ] The teaser names the three required mods and makes **no mention of Valcoin**
      (payouts are the donations backend's business, and its own rules forbid
      promising them).
- [ ] `de_bounty_board` on such a server says bounty hunting isn't active.

**Watch:** postings currently top up as soon as one is answered. The **timed
rotation cadence and the rank-gated elite tier are Phase H** — a tier 5 "Accursed"
posting should NOT appear yet.

## 23g. Bounty hunting — Phase G: the warden's commission  ✅ PASSED

The board is now gated behind a quest told through Haldor's dialogue. A fresh
character should not be able to reach a posting without it.

**The gate holds:**
- [ ] On a **character that has never done the quest**, `F8` shows "The board is not
      yours to read yet" and names Haldor + Shift + E. **No postings are listed and
      no Accept buttons appear**, even though the server has open postings
      (`de_bounty_board` on the server still shows them).
- [ ] The locked panel still shows your standing and the hunter list — only the
      postings are withheld.

**The conversation:**
- [ ] Find Haldor and press **Shift + E**. The warden's conversation opens instead
      of his store. A **short press must still open the store** normally.
- [ ] The dialogue branches: both "what do you mean" and "why keep the list" lead
      onward, and "Another time" / "Not my trouble" exit without granting anything.
- [ ] Re-opening the conversation after exiting **resumes where you left off**
      (`resume_on_return`).
- [ ] Choosing **"Mark it on my map"** grants the commission. Within a couple of
      seconds a **tier 1 posting appears, already assigned to you**, pinned on your
      map, and `F8` shows it as "The warden's commission".

**The commission posting:**
- [ ] It is **tier 1** regardless of which biome it landed in, and it is **near you**
      (roughly 150–1200 m), not across the map.
- [ ] It has **no Abandon button** — the commission can't be dropped.
- [ ] Travel there: the camp spawns on arrival as normal, and answering it (kill
      **or** commune) pays the tier 1 reward.
- [ ] On resolving it you get "The warden will hear of this. The Wanted Board is
      open to you." and the **`Hunting the Hardened`** rune page fires.
- [ ] `F8` now lists the real postings with Accept buttons.

**Persistence and edge cases:**
- [ ] **Relog after unlocking** — the board stays open (the key persists with the
      character).
- [ ] **Relog after accepting the commission but before finishing it** — you still
      have exactly **one** commission, not a second one (the request key is consumed).
- [ ] A **second character** on the same account starts locked again.
- [ ] `de_bounty_quest_reset` re-locks the board; `de_bounty_commission` grants the
      commission without walking to Haldor (it sets the same key the dialogue does).

**Watch:** ServerGuide records the conversation as `once: true` per player, so after
resetting the gate Haldor won't re-offer it — use `de_bounty_commission` to replay,
or reset ServerGuide's own state if you want to re-test the dialogue itself.

## 23h. Bounty hunting — Phase H: rotation + the elite tier  ✅ PASSED

The board now turns over on its own, and the top tier is gated behind competitive
rank. `de_bounty_board` reports each posting's age and tags elite/commission rows.

**Rotation:**
- [ ] Set `Bounty/RefreshHours` to something short (e.g. **0.05** ≈ 3 minutes),
      restart, and watch `de_bounty_board`: unclaimed postings are retired and
      replaced, with a `[bounty] board rotated: retired N, posted M` log line.
- [ ] **Accept a posting, then wait past the window.** It must **NOT** be retired —
      only unclaimed postings rotate. This is the important one: losing a bounty
      you're walking to would be maddening.
- [ ] The **warden's commission** likewise never expires.
- [ ] Clients see the rotated board without a relog.
- [ ] Set `RefreshHours = 0` → nothing is ever retired.
- [ ] **Restart the server** with aged postings on the board: they're retired
      correctly on load (rotation uses real UTC time stored with the posting, so a
      restart doesn't reset the clock).

**The elite tier:**
- [ ] With `EliteChance` raised (e.g. **1.0**) an **Accursed (tier 5)** posting
      appears — it never appears from a biome alone, so this is the only route to it.
- [ ] Only **one** elite posting is ever open at a time, however many postings turn
      over.
- [ ] As an **unranked** player it shows as a **greyed-out "Locked:" row**, not
      hidden, and the panel explains that the top N of the duel or party ladder may
      answer it.
- [ ] Clicking the locked row does nothing.
- [ ] **Try to accept it anyway** — e.g. from a client with the panel state stale.
      The server must refuse with the "Only the realm's finest…" message. The panel
      greys it out for looks; the server is the rule.
- [ ] Earn a **top-10 duel or party rank**, reopen `F8`: the same posting is now
      **acceptable**, and taking it works.
- [ ] Answer an elite bounty → the **tier 5 "Accursed" reward bundle** pays
      (authored back in Phase D and only reachable now), including its chat line and
      Discord broadcast.
- [ ] Set `EliteRankTopN = 0` → the gate is off and anyone may take elite postings.

**Interaction with the reward-tier bonus:**
- [ ] A **top-3 bounty hunter** answering an elite (tier 5) posting still pays tier 5
      — the +1 bonus is clamped at the top tier, not overflowed.

## 24. Dead Raiser sealing — seal a companion in the field  ⬜ UNVERIFIED

Requires a **Dead Raiser** (`StaffSkeleton`), a **Wisp**, and **Blood Magic 20+**.
`devcommands` → `raiseskill BloodMagic 25` is the quickest way in.

**The happy path:**
- [ ] Equip the Dead Raiser and hold a Wisp. Hover your own **Follow-stance**
      companion — the crosshair tooltip shows
      `Hold [Block] — Seal into a totem (N.Ns)`, with N matching your skill.
- [ ] Hold Block. The Wishbone ripple pulses on you and on the companion, and
      **accelerates** as the rite nears completion (the same cue the Communion Rite
      uses — they should feel identical).
- [ ] It completes at the advertised time. One **Wisp is consumed**, a **Communion
      Totem** appears in your pack, and the companion vanishes with the seal VFX.
- [ ] Summon the totem: the companion returns with **the same name, level, XP,
      owner and pack contents**. A field-sealed totem must be indistinguishable
      from an Incinerator-sealed one.

**Skill scaling:**
- [ ] At Blood Magic **20** the channel takes `SealChannelMaxSeconds` (5 s).
- [ ] At Blood Magic **100** it takes `SealChannelMinSeconds` (2 s).
- [ ] Somewhere in between it scales smoothly, and the hover tooltip's figure
      matches the real duration.
- [ ] At Blood Magic **19** the rite cannot start; the tooltip says why.

**Every refusal, with the staff equipped (so the reason is shown):**
- [ ] **Guard** or **Standby** stance → refused, "set it to follow you first".
- [ ] On a **chore** → refused, "recall it from its work".
- [ ] In a **duel** (`J`) or **party duel** (`K`) → refused.
- [ ] **Feral** (butcher-knife betrayal) → refused.
- [ ] **Someone else's** companion → refused.
- [ ] **No Wisp** → refused, "you need a Wisp".

**Silence when it isn't a sealing attempt:**
- [ ] With **no staff equipped**, hold Block while looking at your companion —
      nothing happens, no message. (Blocking beside an ally in a fight must never
      nag.) The tooltip shows no seal hint either.

**Every break condition:**
- [ ] Release Block for **longer than the grace** → fails. A quick **dodge roll**
      mid-rite does NOT fail it (the roll shares the Block button).
- [ ] Walk past `SealMaxDistance` → fails.
- [ ] **Take a hit** → fails (with `CommunionBreakOnDamage` on).
- [ ] Unequip the staff mid-channel → fails.
- [ ] Drop/consume your last Wisp mid-channel → fails.
- [ ] After any failure the companion is **unharmed and still yours**, and the
      rite can be started again immediately.

**Edge cases:**
- [ ] **Full inventory** at the moment of success → the totem **drops at your
      feet** with a message. It must never be destroyed — the totem *is* the
      companion at that point.
- [ ] Two players holding Block on the **same** companion → the second is told
      another rite has hold of it. (Only one should ever be able to; the
      requirement to own the companion makes this rare, so it is a soft check.)
- [ ] `StaffSealEnabled = false` → the feature is entirely absent, including the
      hover hint, and the Incinerator ritual still works.

## 25. Wagered tournaments (requires **4 players**, ideally 2 clients + alts)  ⬜ UNVERIFIED

See [Wagers.md](Wagers.md). Coin wagers need nothing extra; **Valcoin wagers need
the donations plugin rebuilt with `ValcoinWallet.cs`** and an
`ls_tournament_prize` entry in `valcoin_quests.yaml`.

**Panel layout and feedback (check these first — they gate everything else):**
- [ ] Open `F7` with **no tournament running**: the long "how a tournament runs"
      block is fully readable and the buttons sit **below** it, not on top of it.
- [ ] Cycle through all three slots and through registration / running / complete:
      the button rows **move down as the text grows** and never overlap it, and
      never fall off the bottom of the panel.
- [ ] With several entrants and (as admin) several Release/Forfeit rows, the right
      column **stops early** rather than running past the panel edge.
- [ ] Click any button that produces a message (a refusal, a confirmation, a
      refund): it appears in the **top-left**, outside the panel, and is readable
      **while the panel is still open**. Nothing should be hidden behind it.

**Opening (Coins first — it has no external dependency):**
- [ ] With <100 Coins, press Start on the Coin tournament → refused, **no Coins
      taken**.
- [ ] With 100 Coins → opened; **exactly 100 Coins gone**; Discord announces it
      with the entry fee, purse and bracket size.
- [ ] A second player pressing Start on the **same** currency → refused, "only one
      at a time".
- [ ] The **other** currency can still be opened at the same time — both appear on
      the F7 panel (cycle the slot button) and on View Bracket.

**The host's fee pays their entry:**
- [ ] The host locks their totem → registered, and **no second charge**. Their
      Coin count is unchanged by entering.
- [ ] If the host **never** enters and the tournament is cancelled, their opening
      fee is refunded.

**Entering:**
- [ ] Three more players lock totems, each charged the fee, each announced to
      Discord with a running count.
- [ ] A player with no companion totem → told to seal one first, **nothing
      charged**.
- [ ] A player trying to enter twice → refused.
- [ ] A fifth player → "the tournament is full", **nothing charged**.
- [ ] At 4/4 the bracket **begins by itself** — no admin action — and Discord
      posts the round-1 draw plus the ready-up instruction.

**Ready-up and the venue (the core new behaviour):**
- [ ] Neither companion is summoned when the round begins. Nothing appears
      anywhere.
- [ ] One player presses **Ready to Fight** → told it is waiting for the opponent;
      still nothing summoned.
- [ ] The pair walk to **somewhere of their own choosing**, and the second presses
      Ready → **both** companions are summoned **there**, beside their own owners,
      at **full health**, and Discord announces the duel starting.
- [ ] They fight **only each other**. With the second pairing active elsewhere,
      confirm no cross-targeting (the `MatchesDuelAssignment` gate).
- [ ] On a result: Discord posts the winner, then the "still to fight" list; both
      companions are **resealed and despawned**.
- [ ] Pressing Ready again after activation does nothing (no double summon).

**Completion:**
- [ ] Final round resolves → champion announced, purse paid (**999 Coins**
      arriving on the champion's client), and a **full summary** posted with
      standings and every result.
- [ ] Every totem is returned — and the **champion's totem carries the XP its
      companion gained in the final**, not its pre-final state. (This is what the
      6 s completion grace exists for; check the level/XP on the returned totem.)
- [ ] The slot is freed: a new Coin tournament can be opened straight away.

**Refunds:**
- [ ] Withdraw during registration → totem **and** stake both returned in one go.
- [ ] Admin **Cancel** during registration → every entrant refunded, every totem
      returned, Discord announces it.
- [ ] Set `RegistrationMinutes` to `1`, open a tournament, leave it unfilled → it
      auto-cancels, refunds everyone, and announces why.

**Valcoin (needs the donations wallet):**
- [ ] Open a Valcoin tournament → **10 Valcoins debited** (check `/coins`), and the
      donations log shows an `eco_ls_tourney_entry` spend.
- [ ] With too few Valcoins → refused with the ledger's own message; nothing taken.
- [ ] Champion → `VC.Q.ls_tournament_prize` fires and the configured amount is
      credited. **Without** the `valcoin_quests.yaml` entry, confirm the donations
      log warns about an unknown quest (the documented failure mode).
- [ ] On a server **without** the donations wallet: the Valcoin button is present,
      pressing it is refused with a clear reason, and **Coin wagers still work**.
- [ ] From a client that does **not** have the donations mod installed but whose
      *server* does: the panel must NOT claim Valcoin is unavailable (the client
      cannot judge that — see `WagerService.ClientHint`).

## 26. Duel invites (requires **two players**)  ⬜ UNVERIFIED

- [ ] Post an invite (Coins) → stake taken, totem escrowed, Discord announces it
      with the stake and the doubled purse. It appears on both players' F7 panels
      and on View Bracket.
- [ ] Posting a **second** invite → refused, nothing taken.
- [ ] Accepting your **own** invite → refused.
- [ ] The other player accepts → their stake taken, their totem escrowed, Discord
      announces the pairing.
- [ ] While in an invite, either player trying to post another → refused ("no dual
      invites").
- [ ] Multiple **different** players can each have an invite open at the same time.
- [ ] Both press Ready **where they choose to meet** → both companions summoned
      there at full health, Discord announces the duel starting.
- [ ] On the result: winner gets **both stakes** (200 Coins), Discord announces it,
      both totems return, and the winner's carries the XP it just gained.
- [ ] **Withdraw as the poster** before anyone accepts → stake and totem back.
- [ ] **Withdraw as the poster** after acceptance → *both* players made whole and
      the invite is gone.
- [ ] **Withdraw as the challenger** → only they are refunded and the invite goes
      back to **open** for someone else.
- [ ] Withdraw once the duel is **running** → refused ("fight it out").
- [ ] An open invite left past `RegistrationMinutes` → expires, poster refunded,
      Discord announces it.
- [ ] The bout also lands on the **normal duel ladder** (it is an ordinary duel
      underneath) — check `F6`.

## 27. Inventory menu bar (buttons for every panel)  ⬜ UNVERIFIED

Every full-screen panel used to be hotkey-only. The row of buttons across the top
of the inventory screen is the discoverable way in; the function keys still work.

- [ ] Open the inventory: a centred row of **three** buttons sits along the top —
      **Rankings**, **Tournaments**, **Bounty Board** — evenly spaced and clear of
      the hotbar beneath.
- [ ] Each one opens its panel, closing the inventory as it does.
- [ ] The function keys still work independently: **F6** rankings, **F7**
      tournaments, **F8** bounty board.
- [ ] The row survives a **relog** and a **death/respawn** (InventoryGui is rebuilt
      each world load, taking the clones with it — they must be re-created).
- [ ] The row survives a **window resize** and stays centred.
- [ ] Nothing shuffles the row when another mod adds inventory rows or panels
      (it opts out of the container's layout group).
- [ ] `Interface/MenuBarOffset` (`"x,y"` pixels, +x right / +y up) moves the whole
      row; the log line `[ui] inventory menu bar: 3 button(s) under '<parent>' …`
      reports where it landed.
- [ ] **Back-compat:** set the old `Bounty/InventoryButtonOffset` to something other
      than `"0,0"` — it wins over `MenuBarOffset` and moves the whole row (so an
      operator's existing tweak isn't silently lost). Set it back to `"0,0"` and
      `MenuBarOffset` takes over again.
- [ ] Open a panel from a button, close it, and confirm player input is restored
      (the panels' input capture is unchanged by how they were opened).

## 28. Sealed totems survive a relog  ✅ PASSED

The bug: a Communion Totem came back from a relog named **Fuling Totem**, stacking
like one. The player inventory (and every container) is loaded by
`Inventory.Load`, which rebuilds each item from its prefab — a path the old
`ItemDrop.LoadFromZDO` patches never saw.

- [x] Seal a companion, **log out and back in**: the item is still called
      **Communion Totem**, keeps its description, its `⚔ <name>` stat block and
      the crafter line.
- [x] Seal **two different** companions, put both totems in the inventory, relog:
      they are still **two separate items in two slots**, never merged into a
      stack of 2. Summon each and confirm you get the right companion back — with
      its own name, level, XP and pack.
- [x] Same two checks with the totems inside a **chest** (containers load through
      the same path), and with one **dropped on the ground** through a
      server/world reload.
- [x] A **real Fuling Totem** is unaffected: still named Fuling Totem, still
      stacks with other Fuling Totems.
- [x] Carry a totem through a **seal → relog → summon → reseal** cycle and confirm
      nothing is lost.

## 29. Companion combat leash, chore and standby passivity  ✅ PASSED

Three related behaviour fixes. Note the old implementation set `m_alertRange = 0`
to make an ally "passive", which never did anything: vanilla acquires targets
through `m_viewRange`/`m_hearRange`, and the one place `m_alertRange` leashes a
target is gated on `IsTamed()`, which a freed Dvergr is not. The gate now sits on
`BaseAI.CanSenseTarget` plus a per-frame target drop.

**Follow leash (`Companions/FollowEngageRange`, default 20 m — a workbench radius):**

- [x] Stand with a Follow companion near a distant Greydwarf (> 20 m from you):
      the ally **ignores it** and stays at your side.
- [x] Let something come within 20 m of you: the ally engages it normally.
- [x] While it is fighting, **walk away**. The moment the ally is more than 20 m
      from you it **breaks off** and comes back — it does not finish the fight.
- [x] It still fights normally in **Guard** stance (which has no master to stand
      beside) and while in a **duel** or **party duel**.
- [x] PvP is unaffected: a player who attacks you or the ally is still answered,
      even past the leash.
- [x] Change `FollowEngageRange` in the config and confirm the new radius applies.
- [x] After a **relog**, a Follow companion walks back to you on its own (the
      follow target is re-asserted; it is not persisted by vanilla).

**Chore worker (req 3):**

- [x] Assign a chore, then walk a Greydwarf past the station: the ally **does not
      react** — no alert, no chase, it keeps working.
- [x] Let that creature **hit** the ally: it fights back, then returns to the
      station once the threat is gone.
- [x] Friendly fire from **your own other companion** does not set the two on
      each other.

**Standby (req 4):**

- [x] Set an ally to Standby and watch it for a minute: it **stands still** — no
      idle wandering around the spot.
- [x] Creatures walking past are ignored; it never becomes alerted on its own.
- [x] Hit it with a creature: it defends itself, and stops moving again afterwards.

## 30. Companions through InterServerPortal (requires that mod)  ✅ PASSED

Neither of InterServerPortal's modes runs the vanilla teleport, so each needed its
own hook. Confirm the log line `[portal] InterServerPortal detected …` at startup.

**Network mode (same world):**

- [x] Walk a Follow companion into a **network** portal, pick a destination: the
      ally arrives with you, spread around the exit.
- [x] Guard / Standby / chore / dueling allies **stay behind**.
- [x] Back out of the destination menu: nothing is teleported.
- [x] A follower carrying a **non-teleportable** item blocks the crossing with the
      same message a wood portal gives.

**Inter-server mode (different world):**

- [x] Cross with Follow companions: you are told they are **sealed into totems**,
      and the totems are in your pack on the other side.
- [x] They are **summoned back beside you** automatically a few seconds after the
      destination world loads, with name, level, XP and pack intact.
- [x] With a **full pack**: the crossing still happens, you are warned, and the
      unsealed companions are still in the origin world when you return.
- [x] Crossing to a server **without this mod**: the totems simply stay in the
      pack (and summon on your return).
- [x] A crossing that fails and drops you back at the origin still summons them.
- [x] **Regression:** cross with **two or more** followers and watch the log for
      `Collection was modified` — sealing destroys each companion, which mutates the
      static registry the follower list is read from.
- [x] **Regression:** the world switch itself must always happen. If anything in the
      sealing fails it is logged as `[portal] Sealing companions for the crossing
      failed` and the crossing continues without those allies — it must never leave
      you standing in the origin world.

## 31. Resting at camp mends Follow companions  ✅ PASSED

- [x] Damage a Follow companion, then **sit by a campfire** with it beside you: its
      health climbs steadily and a **Resting** icon appears above its health bar.
- [x] It reaches full in about `RestedHealSeconds` (default **120 s**), and the
      healing **stops the moment you stand up and walk away** — confirm it does NOT
      keep healing on the lingering *Rested* buff (that is the whole point of using
      the `Resting` effect instead).
- [x] Same result **under a roof with a fire lit** while standing, not just sitting.
- [x] Walk the ally more than `RestedHealRadius` (default 10 m) from you while you
      stay seated: it stops mending and the icon clears.
- [x] **Guard**, **Standby**, **chore-assigned**, **dueling** and **feral** allies
      are NOT healed while you rest.
- [x] Another player's companion sitting at your fire is not healed by you, and you
      do not see a Resting icon on it.
- [x] **Multiplayer:** the ally is healed even when its ZDO is owned by a different
      client (the heal goes through `RPC_Heal`) — check with a friend standing
      closer to it than you.
- [x] `RestedHealSeconds = 0` disables the feature entirely.
- [x] A companion already at full health generates no healing traffic (nothing in
      the log, no floating heal numbers).

## 32. The alert bark plays once, not once per hit  ✅ PASSED

`BaseAI.SetAlerted` spawns `m_alertedEffects` (the Dvergr's alert shout) on every
false→true transition, and vanilla re-asserts `true` on every hit taken. Anything
that clears the flag on a repeating tick therefore makes an ally bark per hit.

- [x] Let a creature beat on a companion that **can't fight back** — one on
      **Standby**, one **working a chore**, and one lagging **past the follow
      leash**. Each should shout **once**, not on every blow.
- [x] Overload a companion's pack past the weight cap and let something hit it:
      again one shout, not a stream (this path ran every frame and is the
      pre-existing half of the bug).
- [x] Normal combat is unchanged: a Follow ally engaging near you shouts once as it
      alerts, then fights quietly.
- [x] After breaking off, the ally keeps the alert pose for a few seconds and then
      settles — vanilla's 30 s no-contact timeout, not us.
- [x] Cycle stance mid-fight and confirm it still calms down properly (the latch
      resets on a deliberate stance change).
- [x] With several companions out, a fight is not a wall of overlapping shouts.

## Highest-risk items to watch

- **Duel mode cross-client engagement** (#9) — the reworked `DE_Duel` ZDO flag must replicate so two different players' duelists actually pair up; confirm they seek each other (needs two players). Non-lethal now rides on the confirmed `Character.Damage` prefix, so that specific error risk is gone.
- **Mead detection** (#4) — behavior-based now (robust), but first-run confirmation still wanted.
- **Mid-fight commune** (#2) — reported broken three times; confirm the aggravated-flag fix finally holds.
- **Reference HP values** (#5) — documented, not extracted; correct against the live `[xp]` log if needed.
- ~~**Lore beats** (#10b)~~ — **verified in-game 2026-07-03**: the `distance`-triggered biome descent, the StartTemple opening (new + returning players), the recruit-order guide (§10c), and the Companion Handbook (§10d) all passed.
- **Ship riding** (#13) — the boarding lift and "free to walk the deck" behavior are static-analysis designs; confirm the ally boards through the ladder, stays on the moving hull, and avoids water otherwise.
- **Portal follow** (#15) — the risky bit is the ZDO position commit vs. zone-unload timing: confirm the ally is really at the destination after loading, not left at the origin. Two clients for the owner-scoping check.
- **Bog Witch rite gating** (#22) — the `ls_bogwitch_mage_intro`/`_rite` chain depends on `requires: [ls_bogwitch_echo_rite]` reading `SeenTracker.HasFired` correctly across a relog; also confirm the `spawn_creature` reward never places a Dverger inside terrain near the Sunken Crypts.
- **Minimap pins** (#14) — confirm pins track/clear and, in multiplayer, each player sees only their own companions' pins.
- **Tournament escrow round-trip** (#21d) — the riskiest new path: a totem must never be lost. Verify the summon→duel→reseal→despawn cycle and that every exit (reject/withdraw/release/cancel/complete) returns the totem. The `F7` panel now takes input over like a vanilla menu (free cursor + camera/keyboard blocked via `Player.TakeInput` / `GameCamera.UpdateMouseCapture` patches) — confirm buttons are clickable, the camera is frozen, and play is restored on close.
- **Assigned-opponent targeting** (#21d) — with two matches active at once, each summoned companion must engage ONLY its bracket opponent (the `MatchesDuelAssignment` gate in `CompanionIsEnemyPatch`).
- **Wager refunds are the money path** (#25, #26) — every rejection, withdrawal, cancellation and expiry must return BOTH the totem and the stake. The dangerous cases are the asynchronous ones: a Valcoin charge that settles after the tournament filled, was cancelled, or the player joined something else. Each of those re-checks and refunds; confirm with a slow/failing backend if you can.
- **Completion grace** (#25) — escrowed totems are returned 6 s after a bracket completes so the winner's reseal lands first. If a client is lagging worse than that, the champion gets a pre-final totem. Check the returned level/XP.
- ~~**Sealed totems across a relog** (#28)~~ — **verified 2026-08-31.** Keep the two-totem case in any future regression pass: the dangerous half is the stack cap, not the name, and one totem will never show it.
- ~~**The combat leash drops targets every frame** (#29)~~ — **verified 2026-08-31** at the 20 m default; no boundary oscillation observed.
- ~~**Inter-server crossing is a seal, not a teleport** (#30)~~ — **verified 2026-08-31.** Two rules survive it: a totem must never be dropped on the ground in the world you are leaving, and our hooks run as Harmony **prefixes** on the host mod's commit path, so a throw there cancels the whole world switch — every hook is wrapped and must stay wrapped.
- ~~**Rest healing is read on one client only** (#31)~~ — **verified 2026-08-31.** Still true by design: an ally at someone else's fire with its owner far away is not healed.
- **Never write `SetAlerted` on a repeating tick** (#32) — fix verified 2026-08-31, but the rule is permanent: it is edge-triggered and spawns `m_alertedEffects` on each false→true flip, while vanilla re-asserts `true` on every hit. Any future "make the ally stand down" code must go through `DvergrCompanion.StandDown()`, never touch the flag directly.
- **Valcoin availability is a server fact** (#25) — a client without the donations mod must not be told Valcoin wagers are unavailable on a server that has it.
- **Dead Raiser sealing must never eat a companion** (#24) — the totem is the companion. Confirm the full-inventory drop path and that no failure mode consumes the wisp without producing a totem.
- **Discord de-dup** (#21c) — each duel/party/#1/champion event should post exactly one webhook message; watch for doubles on cross-client resolution.
