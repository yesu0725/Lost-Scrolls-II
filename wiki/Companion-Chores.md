# Companion Chores

Put your allies to work. A recruited Dvergr can be assigned to tend a vanilla workstation and handle the tedious parts of the game for you — smelting, refining, cooking, brewing, farming, animal care, and hauling. It's all driven by existing stations and chests; no new objects.

## How to assign

1. Place a **chest** near the workstation and stock it with what the job needs (ore + fuel for a smelter, seeds for a field, food for animals, etc.).
2. Have a companion of the **right caste** nearby (within ~10 m of you).
3. Hover the station and press the **chore key** (`H` by default). Chore-able stations show a `[H] Set companion to work` hint when you own an eligible companion.

The nearest free companion of the matching caste walks over and starts working. It says out loud whatever's blocking it if it gets stuck ("I need more Coal!", "The harvest chest is full!", etc.).

## Recalling

- Press `H` on the **station** your ally is working to send it off the job, **or**
- Press `H` while hovering **your companion** directly — handy when you're across the base. Its tooltip shows `[H] Recall from chore` while it's assigned.

A working companion is passive (like Standby) — it won't wander off to fight — but it still retaliates if attacked. Recalling returns it to Follow at your side.

## Caste → chore mapping

Each caste handles a different domain:

| Caste | Domain | What it does |
|---|---|---|
| **Fire Mage** | Smelting | Feeds ore **and fuel** into Smelters, Blast Furnaces, and Charcoal Kilns |
| **Ice Mage** | Refining | Tends the Eitr Refinery and Spinning Wheel |
| **Support Mage** | Provisioning | Runs Cooking Stations (pulls food before it burns, adds fuel + raw food) and Fermenters (loads, waits, taps finished mead) |
| **Support Mage** | Farming | Plants seeds from the chest onto tilled ground and harvests ripe crops back into it — any crop type, biome-aware |
| **Support Mage** | Husbandry | Feeds hungry tamed animals from a chest to keep them happy and breeding |
| **Rogue** | Hauling | Sweeps loose dropped items within range straight into a chest |

If you press `H` on a station and no companion of the required caste is nearby, it tells you so.

## Notes

- **Farming** can be pinned to a field by placing a **Cultivator on an item stand** (the stable marker), or by hovering a crop on tilled ground. The mage both plants and harvests, and won't plant a crop in a biome it can't grow in.
- **Husbandry**: one Support Mage tends a whole pen, cycling through hungry animals — you don't need one mage per animal.
- **Hauling**: the Rogue holds its post by the chest and pulls items in range straight in (it doesn't run out to each item).
- **One worker per job.** A station can only be claimed by one companion; hovering a claimed station shows *"&lt;name&gt; is already working here."*
- **Persists across relogs and zone reloads.** A companion returns to its station after you relog. It keeps working while you're away, too — with the one engine limit that Valheim doesn't simulate zones with no player anywhere near them, so a far-off chore pauses until someone reloads that area.
- Chores are **owner-only** to assign, and gated to the right caste.
