# Companion Inventory

Every recruited Dvergr carries its **own pack** — a place to stash loot, feed it, and
let it look after itself. Open it with **`Y`** while hovering your companion.

## Opening the pack

Press `Y` on your own companion and a **chest-style panel** opens — exactly like opening
a chest:

- the companion's own **4 columns × 2 rows** (8 slots) of storage,
- your **own inventory + crafting**, so you can move things in and out,
- a **total weight** readout for the pack,
- a **name field** to rename the companion, and
- a live **HP** readout next to the name.

Only the companion's **owner** can open its pack. While you're typing in the name field
your normal hotkeys are suppressed, so the letters only edit the name.

The pack is saved on the companion, so its contents survive relogs and world reloads.

## What the companion does with its pack

Your ally manages its own pack automatically:

### Picks up loot
It gathers nearby loose items **of types it already carries** — so it only collects
things you've told it to by seeding one in its pack first. An **empty pack picks up
nothing**. It always **fights first**: while there are enemies around it won't wander off
to collect items.

### Eats food
Drop **food** in the pack and the companion eats **one at a time**, gaining a temporary
**bonus to its maximum health** for that food's normal duration (which then fades, just
like your own food). While a food buff is active a **food icon** shows above its health
bar, and the panel's HP readout shows the raised maximum.

### Drinks meads
- A **health mead** is sipped when the companion drops **below 35%** health, and it keeps
  drinking until it's back **above 90%**.
- **Poison, fire, and frost resistance meads** are drunk for their resistance — the
  matching **resistance icon** appears above the companion, and it genuinely takes less of
  that damage type. It keeps a resistance topped up as long as it has meads for it.
- Plain stamina meads are ignored.

### Gets encumbered
A pack holds up to **150 weight**. Go over that and the companion becomes **encumbered**:
it stops picking things up and **won't attack** (an encumbered icon shows above it) — but
it can still **move and follow you**. Drop back under 150 and it returns to normal.

## Losing and moving the pack

- **On death**, a companion **drops its whole pack** on the ground where it fell, so you
  can recover the contents.
- **Communion Totems** carry the pack too: seal a companion into a [totem](Communion-Totems)
  and its inventory is stored with it, then restored when you summon it back.
- **Portals**: because a following companion travels with you, a **wood portal** won't let
  you through while one of your Follow-stance allies is carrying a **non-teleportable**
  item (ore and other portal-restricted materials). You'll get a message naming the ally
  and the item — clear it from the pack (or leave that ally behind) and you can portal.

## Compatibility

Works alongside **ComfyQuickSlots**: the pack panel is positioned so it never hides the
extra inventory row that mod adds.

---

See also: **[Companion Commands](Companion-Commands)** for the full hotkey list.
