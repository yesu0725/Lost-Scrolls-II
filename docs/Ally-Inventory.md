# Ally Inventory

Recruited Dvergr each carry their **own inventory** — a place to stash loot, feed
them food and meads, and manage their carrying weight. Built entirely on vanilla
`Container` / `Inventory` / `StatusEffect` types (no authored assets), per the
[vanilla-assets-only constraint](Technical-Constraints.md).

Status: **verified in-game (2026-07-05).** Core storage/UI, pickup, food, weight,
death-drop, totem carry-over, the wood-portal cargo block, and ComfyQuickSlots panel
compatibility all passed a live session — see [Testing.md §16](Testing.md).

## At a glance

| Req | Behavior |
|---|---|
| 1-2 | Each companion has its own **4 columns × 2 rows** (8-slot) inventory. |
| 3 | The owner opens it with **`Y`** while hovering the companion; the panel carries both the inventory slots **and** a name/rename field (which suppresses all binds while focused). |
| 4-6 | The companion **picks up loose items it already carries**; an empty pack picks up nothing; **combat takes priority** over gathering. |
| 7-10 | **Food:** eats one at a time, gaining a temporary max-HP buff for the food's duration that gradually decays, with a **fed status icon** on its name (a live `HP cur/max` readout in the panel confirms the bump). |
| 11 | **Health mead:** starts drinking below **35%** HP and keeps sipping until above **90%**. |
| 12 | **Resist meads** (poison / fire / frost): drinks for the resistance effect, shows the resistance icon above it, and keeps drinking while any remain. |
| 13-14 | **150 weight cap.** Over it the companion stops picking up and **won't attack** (still moves), and shows an **encumbered icon**; the panel shows the pack's total weight. |
| 15 | Opening the pack is **exactly like opening a chest** — player inventory + crafting + the container grid + weight readout. |
| + | **Drops its whole pack on death**; a **Communion Totem** carries the sealed pack through seal→summon; a **wood portal** refuses a player whose following companion holds a non-teleportable item. |

## How it's built

Three components ride on every recruited companion (attached wherever
`DvergrCompanion` is — recruit, relog-restore, admin spawn):

- **`CompanionInventory`** — owns the storage. It puts a stock **`Container`** on the
  companion, sized 4×2, sharing the creature's own `ZNetView`. That gives us **ZDO
  persistence + multi-client sync for free** (Container saves under the creature's
  `items` ZDO var and re-syncs via its own `CheckForChanges`), and lets the pack be
  shown with `InventoryGui.Show(container)` — the same call the game uses for a
  chest, so the player inventory, crafting panel and the container **weight readout**
  all come along (reqs 14 + 15).

- **`CompanionInventoryAI`** — the behavior half. One throttled tick per second, run
  **only on the companion's ZDO-owner client** (like `ChoreAI`), so the shared
  inventory and health mutate exactly once and replicate outward. Handles pickup,
  consumption and encumbrance.

- **`CompanionInventoryGui`** (on the plugin object) — opens the panel and injects the
  rename field.

Because the `Container` shares the creature GameObject, it's a competing
`Hoverable`/`Interactable`. **`CompanionContainerAccessPatch`** suppresses the vanilla
`[E] Open` hover and `Interact` for companion containers, so the **only** way in is
the owner-gated `Y` handler — no ungated chest access, no clash with the stance key.

### The Y key does double duty (req 3)

`Y` opens the inventory panel; the panel also carries the rename field, so the one
key covers "open inventory" and "rename". The field is a **clone of the vanilla
rename box's input field** (`TextInput.m_inputField`, a `GuiInputField : TMP_InputField`),
reparented into the container panel and prefilled with the current name — cloning
gives a fully wired, correctly themed field instead of a hand-built one. If cloning
ever fails the inventory still opens; only the in-panel rename degrades. Next to the
name is a live **`HP cur / max`** readout (gold while a food buff is padding max HP).

**Typing suppresses all binds.** While the name field is focused, `Plugin.Update`
skips our hotkeys (`CompanionInventoryGui.IsTyping`) **and** a
`ZInput.GetButtonDown` prefix swallows every vanilla button action
(`CompanionTypingButtonDownPatch`) — otherwise the game still reacted to raw binds it
knows about (e.g. `InventoryGui.Update` closes the container on the `"Use"` bind = `E`),
because the injected `GuiInputField` isn't part of the game's chat/console input gate.

## Pickup (reqs 4-6)

A **radius sweep** (~8 m), matching the hauling chore's established behavior — the
companion holds its post and pulls matching loose `ItemDrop`s straight in rather than
walking to each one (the walk-to-item approach was tried and reverted for hauling at
the user's request; we stay consistent).

- Only item **types the companion already carries** are collected (matched on the
  shared item name), and only if the whole stack fits.
- An **empty pack collects nothing** (req 5).
- While **alerted or holding a target** the companion fights and does not gather
  (req 6). Pickup is also suspended while on a **chore** or in a **duel** (those
  manage their own item flow / are occupied).

## Consumption (reqs 7-12)

Consumables are classified by **what they do**, never by prefab name
(`CompanionConsumables`), so it self-corrects across tiers and modded items — the same
philosophy as [mead feeding](Ally-Commands.md).

- **Food** (`m_food > 0`): eats **one at a time**. Raises max HP by the food's
  `m_food` value for its `m_foodBurnTime`, held full for the first half then decaying
  linearly (a stand-in for vanilla food's falloff). The buff is delta-based so
  repeated adjustments never compound. Shows the food's own icon as a **fed** status.
- **Health mead** (consume effect restores health): sipped only when HP `< 35%`, and
  stops once HP `> 90%`.
- **Resist mead** (consume effect carries a fire/frost/poison **resistance** modifier):
  drunk on sight (applying the same `StatusEffect` the player would get, for the same
  duration) and re-drunk whenever the resistance lapses and one remains. Stamina meads
  (no health, no resistance) are ignored. Active resistances render as **icons above
  the companion in-world** (`CompanionConsumables.ActiveResistEffects`); the resistance
  genuinely applies because `Character.RPC_Damage` folds `SEMan.ModifyDamageMods` into
  the damage for **every** character, not just players.

Health-mead sipping is **latched**: it starts below 35% and keeps going until above
90% (a single sip rarely clears 35% in one gulp).

## Weight & encumbrance (reqs 13-14)

The pack caps at **150 total weight**. Over the cap the companion:

- **stops picking up**,
- **won't attack** — its AI target is dropped each tick (it does not proactively fight
  or retaliate while overloaded),
- **can still move / follow**, and
- shows the vanilla **Encumbered** status-effect icon above its health bar.

Encumbrance is enforced **every frame** (not just on the 1 Hz tick) via
`DvergrCompanion.ApplyEncumbrance` — it drops the target and goes passive (alert range
0) so the ally genuinely stops swinging instead of re-acquiring between ticks, then
restores the correct stance behavior once the load drops back under the cap. Any client
can derive encumbrance straight from the replicated container weight, so the icon shows
for everyone; the **fed** icon is owner-client local.

## Death, totems, and portals

- **Drops its pack on death** — `CompanionDeathDropPatch` (`Character.OnDeath`,
  owner-gated) spills every item to the ground so nothing is lost. Sealing into a totem
  destroys the creature directly (not via death), so it does **not** double-drop.
- **Totems carry the pack** — when a companion is sealed into a Communion Totem, its
  whole inventory is serialized into the totem's `m_customData` (`DE_TotemInv`) and
  restored into the summoned companion (`TotemConversionService`), so items survive
  seal → summon. See [Companion-Totems.md](Companion-Totems.md).
- **Wood-portal cargo block** — if a **Follow** companion that would teleport with you
  (owned, in range) carries a **non-teleportable** item, the `portal_wood` refuses to
  send you even with a clean personal inventory, naming the ally + item
  (`CompanionPortalBlockPatch`, throttled). Scoped to `portal_wood`; other portals are
  unaffected. See [Ally-Commands.md](Ally-Commands.md) (portal follow).

## ComfyQuickSlots compatibility

ComfyQuickSlots (and other slot mods) grow the player inventory downward, and that
extra row would sit **behind** our pack panel. While the pack is open we push the
shared container panel (`InventoryGui.m_container`) down by the extra rows' height
(`(GetHeight() − 4) × elementSpace` + a small clearance) so the row stays visible —
the same `m_container` shift BiomeLords uses — and restore it on close, leaving vanilla
chests untouched. Re-applied every frame because CQS re-runs its own layout a frame
after `Show`.

## Known caveats / open items

- **Level-up mid-food-buff** — the delta-based max-HP buff is robust to our own writes,
  but a level-up's absolute `SetMaxHealth` landing *during* an active food buff could
  briefly misalign the max until the buff ends. Rare (level-ups come from kills/duels);
  documented rather than fixed.
- **ZDO `items` key** — the companion Container persists under the creature's own
  `items` ZDO var; vanilla creatures don't persist an equipment inventory there, so no
  collision has been observed.
- **MP scope** — pickup/consumption/food run on the companion's **ZDO-owner** client;
  the encumbered icon is derived from the replicated weight (shows for everyone) but the
  **fed** icon is owner-client local.
