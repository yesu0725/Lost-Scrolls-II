# Companion Commands

Everything you do with a companion is a **hover + hotkey** action — look at the ally (or a workstation) and press a key. No menus. All keys are configurable; defaults are shown below and all are ignored while a text box, chat, or console is focused.

| Key | Action | Owner only? |
|---|---|---|
| `G` | Feed / heal (drink a health mead) | No — anyone can heal |
| `E` | Cycle stance: Follow → Guard → Standby | Yes |
| `Y` | Rename the companion | Yes |
| `H` | Assign to a chore (on a station) / recall (on the ally) | Yes |
| `J` | Toggle duel mode | Yes |

Hover your own companion and a crosshair tooltip shows its current **stance** plus the relevant hints (`[E] Cycle stance`, `[Y] Rename`, and `[H] Recall from chore` when it's working).

## Feed / Heal

Hold a **health mead** and press `G` while hovering any recruited companion to heal it. The heal scales to the mead's real potency and the companion's health pool, and plays the mead's own healing burst. Feeding is **not** owner-restricted — you can top up a friend's ally, or heal the loser of a [duel](Dvergr-Duels).

## Stance: Follow / Guard / Standby

Press `E` while hovering your companion to cycle its stance:

- **Follow** — comes with you and fights at your side. Attacks monsters; attacks players only under the threat rules below.
- **Guard** — holds its ground and proactively engages threats near its post. Treats every non-owner player as a threat.
- **Standby** — fully passive. Holds position and does nothing, *except* retaliate if attacked.

The companion speaks a short line describing what it can do in its new stance each time you cycle.

> Note: stance is per-session — after a relog you'll want to re-issue Follow.

### Threat behavior (who a companion will fight)

- The **owner is never** a target (unless the companion has gone feral from a butcher knife — see [Recruiting Companions](Recruiting-Companions)).
- **Guard** treats every non-owner player as a threat.
- **Follow** only turns on players the **owner** has attacked (briefly).
- **Any stance** — a player who attacks the companion is retaliated against (briefly). Attacking a companion also turns it on the attacker's own companions.

## Rename

Press `Y` while hovering your companion to open the vanilla text box (the same one used for signs). The name persists across relogs and shows up on the floating name, the crosshair tooltip, and the "already working here" chore tooltip. Unnamed companions just read "Dvergr".

## Voiced companions

Freed companions stop making hostile-camp Dvergr chatter. Instead they speak short "here's what I can do" lines — on recruit and on each stance change — hinting at the chores their caste can take on.

## Minimap pins

Your own companions show as **live pins on your minimap** so you can find them. Pins are private and client-side: other players never see your companions on their map, and you never see theirs. Toggle with the `ShowMapPins` config option (on by default); pick which vanilla pin sprite to use with `MapPinIcon`.

## Travelling with you

A **Follow**-stance companion comes along when you move between places:

- **Ships** — it boards your ship (climbing a ladder if there is one) and then walks the deck freely, fighting and moving normally while the ship carries it. Companions otherwise avoid deep water. See more under ship riding in the [Home](Home) topics.
- **Portals** — step through a portal and every nearby Follow-stance companion you own is teleported to the exit with you and keeps following.

Only Follow-stance allies travel; a companion that's on a chore, guarding, on standby, dueling, or feral stays put.
