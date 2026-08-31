# Communion Totems

Not ready to bring an ally along? **Seal it into a carriable totem** and summon it back later — with its name, level, and XP fully preserved. Handy for "banking" companions you don't want trailing you everywhere.

Everything reuses vanilla assets: the totem is the stock Fuling Totem item (renamed "Communion Totem"), the reagent is the stock **Wisp**, and the ritual station is the vanilla **Incinerator** (Obliterator).

There are **two ways to seal**, and both produce exactly the same totem:

| | **At an Incinerator** | **With a Dead Raiser** |
|---|---|---|
| Where | at an Obliterator | anywhere |
| Cost | 1 Wisp per companion | 1 Wisp, and a Dead Raiser equipped |
| Needs | nothing else | Blood Magic 20+ |
| How many | several at once | one at a time |
| How long | the usual lever animation | 5 seconds, down to 2 as your Blood Magic grows |

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

## Sealing in the field (Dead Raiser)

If you'd rather not walk an ally back to an Obliterator, a blood-magic practitioner can seal one on the spot.

You need:

- a **Dead Raiser** (the skeleton staff) **equipped**,
- a **Wisp** in your pack,
- **Blood Magic 20** or higher,
- and the companion must be **yours**, in **Follow** stance, and free — not on a chore, not duelling, not turned feral.

Then just **hold your Block button** while looking at it. Look at your own ally with the staff equipped and the crosshair tooltip tells you whether you can seal it, and exactly how long it will take.

The rite takes **5 seconds at Blood Magic 20**, getting faster as your skill grows until it reaches **2 seconds at Blood Magic 100**. A rippling pulse plays on both of you and quickens as it nears completion — that's your progress bar.

It can fail. Let go of Block (a dodge roll is forgiven), walk too far away, take a hit, unequip the staff, or lose your last Wisp, and the binding breaks. Nothing is consumed and your ally is unharmed — just start again.

On success the Wisp is spent and the totem goes into your pack. If your pack is full it drops at your feet rather than being lost.

## Summoning a companion

Put the totem on your hotbar and **press its slot number** (or right-click → Use in your inventory). The companion spawns **where you're looking**, owned by you, at its sealed level and XP with its name intact. The totem is consumed.

## Crossing between worlds

If your server runs **InterServerPortal**, stepping through an **inter-server** portal seals every Follow-stance companion into a totem automatically as you leave, and summons them back beside you on the other side. You don't have to prepare anything — but a full pack means an ally is left behind in the old world, so leave a slot or two free before you cross.

## Good to know

- A sealed companion's progress rides on the **item**, so it survives saving, dropping, and trading — you can hand a companion to another player as a totem.
- Communion Totems never stack (so two companions can't merge into one), which is why they don't combine like ordinary Fuling Totems.
- Multiplayer sealing works on a listen host and for the activating client; a fully cross-client incinerator setup is the least-tested path.
