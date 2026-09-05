# Companion Chores

Put your allies to work. A recruited Dvergr can be assigned to tend a vanilla workstation and handle the tedious parts of the game for you — smelting, refining, cooking, brewing, farming, herding, and hauling. It's all driven by existing stations and chests; no new objects.

## How to assign

1. Place a **chest** near the workstation and stock it with what the job needs (ore + fuel for a smelter, seeds for a field, food for animals, etc.).
2. Have a companion of the **right caste** nearby (within ~10 m of you).
3. Hover the station and press the **chore key** (`H` by default). Chore-able stations show a `[H] Set companion to work` hint when you own an eligible companion. A working ally's tooltip reads **`Stance: On chore`** — it keeps working until you recall it.

The nearest free companion of the matching caste walks over and starts working.

**One kitchen job per mage.** Cooking stations, the stone oven and fermenters are three separate jobs. A mage works the kind you posted it at and won't wander to the others — a cook that roams is never where the food is when it finishes. Put a second and third mage on the rest.

**One ally covers a whole workshop.** The spot you assigned it at becomes its *post*, and it works **every job of its kind within ~20 m of it**, walking from one to the next. One Fire Mage keeps a row of smelters, kilns and furnaces going. One Support Mage runs the entire kitchen — cookers and fermenters together. You don't need one ally per station, and a second ally is refused for anything already inside someone's patch. It says out loud whatever's blocking it if it gets stuck ("I need more Coal!", "The harvest chest is full!", etc.).

## Recalling

- Press `H` on the **station** your ally is working to send it off the job, **or**
- Press `H` while hovering **your companion** directly — handy when you're across the base. Its tooltip shows `[H] Recall from chore` while it's assigned.

**Doors are no longer a wall.** Allies open a closed door that's in their way and shut it behind them, so a worker can cross your base to reach its station. Locked doors and other players' warded doors are left alone.

A working companion is passive (like Standby) — it won't wander off to fight — but it still retaliates if attacked. Recalling returns it to Follow at your side. If the job ends **on its own** — you tear the station down, the field is picked clean, the animal dies — the ally drops to **Standby** and holds its post instead of walking home.

## Where the work ends up

Your ally **puts what it makes into a chest**. Bars, cooked food, tapped mead, harvested crops, eggs — none of it is left in a pile on the floor for you to come and collect.

- It uses the **nearest chest**, but prefers one that **already holds the same item** — so your copper keeps landing in the copper chest.
- If that chest is full it moves to the **next nearest** with room.
- If nothing in range will take it, it tells you — *"Every chest here is full!"* or *"I have no chest to store this!"* — and stops working until you make room.
- It **never** keeps the goods in its own pack.
- **Chests, barrels and carts all count.** Ships, the Obliterator, gravestones, other players' personal chests and chests inside *someone else's* ward do not. Your own warded base is fine.
- **Several allies can share one chest.** Two workers next to the same chest both use it.
- It only stows **what the station makes**. Ore you dropped beside a smelter, or a mead you fumbled walking past, is left where it is. A **Rogue** is the exception — clearing the ground is half its job, so it takes anything loose in its patch (except the herd's feed).

The chest search range is **10 m** by default (`Chores/ChoreChestRadius`), and covers both where it stores products and where it draws materials from. How wide a patch one ally tends is **20 m** (`Chores/ChoreWorkRadius`).

## Caste → chore mapping

Each caste handles a different domain:

| Caste | Domain | What it does |
|---|---|---|
| **Fire Mage** | Smelting | Feeds ore **and fuel** into every Smelter, Blast Furnace and Charcoal Kiln in range |
| **Ice Mage** | Refining | Tends every Eitr Refinery and Spinning Wheel in range |
| **Support Mage** | Provisioning | Runs the cookfires, *or* the ovens, *or* the fermenters — whichever you posted it at. Pulls food before it burns, adds raw food, **keeps the fire fed**; taps finished mead |
| **Support Mage** | Farming | Give it a **Cultivator** and it works the ground where it stands: one crop per field, seed from its pack or a chest, harvest stored |
| **Rogue** | Husbandry **+** Hauling | Feeds hungry tamed animals, culls the surplus for meat and hides, **and** clears loose items off the ground into your chests |

If you press `H` on a station and no companion of the required caste is nearby, it tells you so.

## Notes

- **Farming works differently from every other chore.** A field has nothing to point at, so you give the ally the tool instead: put a **Cultivator** in the mage's own pack (`Y`), then hover the **mage** and press `H`. The ground it is standing on becomes its field. Take the Cultivator back and it stops.
  - It keeps **one crop per field** while the seed lasts — whatever is already growing there. When that crop's seed runs out it fills the rest of the bed with whatever else it can reach, rather than stopping.
  - It looks in its **own pack first, then every chest in range** — not just the nearest one.
  - If a seed can't grow where you've posted it, it says so and names the biome: *"Barley won't grow here — it needs the Plains."*
  - It plants **several at once, in a tidy square**, and lines its rows up with what's already in the bed. A new farmer sows a 2x2 block; the block gets bigger as it **levels** — and it **harvests by the same armful**.
  - It only plants where **you** could have planted by hand, so it works around rocks, wild growth and build pieces instead of into them.
  - Seed comes from its **own pack** first, then a chest by the soil — hand it a stack so it stops walking back between rows.
  - It won't plant a crop in a biome it can't grow in.
- **Husbandry**: one Support Mage tends a whole pen, cycling through hungry animals — you don't need one mage per animal.
- **The Rogue does two jobs at once.** Post it at a tamed animal *or* at a chest — either way it feeds and culls the herd in range **and** clears loose items off the ground into your chests. A Rogue by a chest with no animals is just a hauler; a Rogue in a pen with junk on the floor is both.
- **Culling**: it leaves **three grown animals of each kind**, spares the young and the pregnant, kills with a blade at arm's reach (never at range), and stores the meat and hides for you. Since Valheim stops a pen breeding at four of a kind, that turnover is what keeps it producing. Set `Chores/HusbandryCullLimit` to `0` if you'd rather it only fed them.
- **One thing it won't pick up**: food the herd eats, left on the ground. The feeding job puts it there on purpose. (Drops from a cull are collected even so.)
- **Several allies can share one job.** Pressing `H` on a workplace always puts another free companion on it, so a big workshop or pen can have two or three workers; the tooltip names who is already there. To take one off, press `H` on **that ally**.
- **Persists across relogs and zone reloads.** A companion returns to its station after you relog. It keeps working while you're away, too — with the one engine limit that Valheim doesn't simulate zones with no player anywhere near them, so a far-off chore pauses until someone reloads that area.
- Chores are **owner-only** to assign, and gated to the right caste.
