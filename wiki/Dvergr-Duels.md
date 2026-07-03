# Dvergr Duels

Duels are **non-lethal companion-vs-companion sparring** between two players. It's a safe, repeatable way to train your ally and earn bonus XP — framed as a friendly training exercise, not a high-stakes wager.

## Multiplayer only, by design

A companion in duel mode only fights **another player's** duel-mode companion. That means:

- You **cannot** duel your own two allies against each other.
- Both owners must opt in (each toggles duel mode on their own companion).
- There's no way to farm duel XP solo.

You need a second player with their own companion to duel.

## How to duel

1. Hover **your own** companion and press the **duel key** (`J` by default) to toggle duel mode on. Press again to stand it down.
2. Have your duel partner do the same with their companion.
3. When two duel-mode companions with different owners are in range, they seek each other out and fight.

While hovering your companion, a `[J] Duel` hint appears whenever another player's companion is nearby.

## The rules

- **Nothing else is a target.** A duel-mode companion ignores — and is ignored by — players, monsters, and same-owner allies. It fights only its rival duelist.
- **Players can't hurt it.** A duel-mode companion is immune to player damage even with PvP on. The duel is strictly between companions.
- **No permadeath.** The loser is subdued to low HP, never killed. Heal it back up afterward with a mead (anyone can feed it — see [Companion Commands](Companion-Commands)).
- **The winner gains bonus XP** — the main reason to duel.
- **Auto stand-down.** A companion leaves duel mode on its own when no rival remains, after a wait if no challenger ever shows, or if its owner logs out or wanders too far away.
- **Win is announced** in chat as a shout so everyone sees it.

## Owner tags

Because duels are cross-player, every companion's floating name shows its **owner's name** — so it's always clear whose ally is whose in a fight.

## Multiplayer note

Duel notifications (speech bubbles and center messages) show for the owning client and on a listen host. On a dedicated server where creature ownership sits on the server, some of those messages may not appear locally — the duel itself still resolves correctly.
