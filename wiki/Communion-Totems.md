# Communion Totems

Not ready to bring an ally along? **Seal it into a carriable totem** and summon it back later — with its name, level, and XP fully preserved. Handy for "banking" companions you don't want trailing you everywhere.

Everything reuses vanilla assets: the totem is the stock Fuling Totem item (renamed "Communion Totem"), the reagent is the stock **Wisp**, and the ritual station is the vanilla **Incinerator** (Obliterator).

## Sealing a companion

1. Set the companions you want to seal to **Follow** and gather them at an **Incinerator** (within ~15 m).
2. Put **Wisps** into the Incinerator — **one Wisp per companion** you want to seal.
3. Pull the lever. During the normal lighting animation the ritual resolves.
4. The resulting **Communion Totems** appear in the Incinerator's slots, one per sealed companion. Take them into your inventory.

Notes:

- The number sealed is the **smaller** of Wisps and following companions. Extra Wisps and extra companions are left untouched.
- Only **Follow-stance, free** companions are sealed — not ones on a chore or in a duel.
- With no Wisps or no following companions present, the Incinerator works exactly like vanilla.

Each totem is renamed **"Communion Totem"** and its tooltip carries the sealed companion's name, caste, level, and owner. (Real Fuling Totems are untouched and still stack and behave normally.)

## Summoning a companion

Put the totem on your hotbar and **press its slot number** (or right-click → Use in your inventory). The companion spawns **where you're looking**, owned by you, at its sealed level and XP with its name intact. The totem is consumed.

## Good to know

- A sealed companion's progress rides on the **item**, so it survives saving, dropping, and trading — you can hand a companion to another player as a totem.
- Communion Totems never stack (so two companions can't merge into one), which is why they don't combine like ordinary Fuling Totems.
- Multiplayer sealing works on a listen host and for the activating client; a fully cross-client incinerator setup is the least-tested path.
