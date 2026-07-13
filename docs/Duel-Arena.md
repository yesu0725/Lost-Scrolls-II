# Duel / Arena System

## Concept

A duel is a **mode** an owner toggles on their own companion. A companion in duel mode fights **other players'** duel-mode companions — and nothing else. It's framed as a **training exercise**, not a high-stakes wager — confirmed design decision: non-lethal sparring only. Because both sides must opt in (each owner toggles their own ally), it's a consented PvP-adjacent activity that a single player can't self-farm.

## Rules

- **Owner-initiated, own companion only.** Only a companion's owner can put it into duel mode (press the duel key while hovering it). Pressing again stands it down. (req 1)
- **Different owners only / no self-farm.** A duel-mode companion only ever treats **another player's** duel-mode companion as an enemy — never a same-owner ally, and never an unowned/legacy companion (owner id 0). A player therefore cannot pit their own two allies against each other to farm the win-XP bonus.
- **Nothing else is a target.** While in duel mode a companion does **not** attack players or other creatures, and is not attacked by them — only its rival duelists. (req 2)
- **Auto-stand-down.** When no rival duel-mode companion remains in range, a companion that had been dueling automatically leaves duel mode with a notification. A lone duel-mode companion that never finds a challenger stands down after a wait timeout. (req 3)
- **Owner leash.** If the owner logs out or leaves the companion's vision range while it's dueling, the companion leaves duel mode with a notification. (req 5)
- **PvP immunity.** Players cannot damage a duel-mode companion, even with PvP enabled — the duel is between companions only. (req 6)
- **No permadeath.** The losing ally is subdued (bottoms out at low HP), never killed. The owner can heal it back up with a mead afterward (the feed path claims ZDO ownership first, so the heal isn't lost to the ownership the winner's client took during the subdue).
- **Winner gains bonus XP** beyond a normal fight — the main incentive to duel.
- **Winner announced in chat.** A duel win is broadcast as a chat **shout** to every player (setting 3), on top of the local bubble/center message.
- **Duel hint on hover.** Hovering your own companion shows a `[J] Duel` hint whenever another player's companion is within duel range (setting 4).
- **Owner name tag.** Every companion's floating name shows its owner's name, so it's clear whose ally is whose during a cross-player duel. (req 4)

## Why Non-Lethal

Permadeath risk on a leveled, time-invested companion is a strong source of player frustration and was explicitly rejected in favor of keeping duels as a safe, repeatable leveling tool. This may be revisited later as an optional toggle, but is not in scope for the initial build (Phase 6).

## Implementation (Phase 6, reworked — duel *mode*)

The original scripted 1v1 (`DuelController` disabling `MonsterAI` and hand-driving straight-line movement) has been **removed**. Duels now ride on vanilla AI:

- **Duel mode is a companion state** (`DvergrCompanion.EnterDuelMode` / `ExitDuelMode`), toggled by the owner via `Plugin.HandleDuelInput`. It's **ZDO-backed** (`DE_Duel`) so the flag replicates to every client — the cross-client enemy pairing needs both duelists' flags visible on one machine. It's cleared on spawn, so a relog ends any duel.
- **Targeting is via the `BaseAI.IsEnemy` patch.** When either party is a duel-mode companion, the patch returns its result *authoritatively*: `true` only if **both** are duel-mode companions with **different, non-zero owners**; `false` otherwise. That single rule makes duelists seek each other and makes them invisible to (and ignore) players, creatures, and same-owner allies for the duration (reqs 1 & 2). Movement/attack is then just vanilla `MonsterAI` — no hand-rolled pathing.
- **Per-tick driver** (`TickDuel`, run only on the ZDO-owning instance): checks the owner leash (req 5), locks onto the nearest rival duelist when it has no valid target, and stands the companion down when — having engaged — no rival remains (req 3), or after a wait timeout if it never found one.
- **Non-lethal guarantee**: folded into the `Character.Damage` prefix (`CompanionDamagePatch`). A companion-vs-companion blow that would drop a duelist to/below 5% max HP is capped (health set to the floor), the loser leaves duel mode, and the striking companion is credited the win. *(The old `Character.ApplyDamage` prefix — whose parameter shape the compiler couldn't verify — is gone; `Character.Damage(HitData)` is confirmed by Cecil and is the single chokepoint used for all of this.)*
- **Single-subdue guarantee (double-win bug fix)**: capping HP at the 5% floor left the loser sitting right on the threshold, and vanilla `Character`/`MonsterAI` health regen ticks it back **above** the floor within a frame or two — while the winner's AI is still swinging and before the `ExitDuelMode` flag has replicated across clients. The next hit re-satisfied the "at/below floor" test and fired the win **again**: a double announcement, and (critically for the ranking system) a double point award. Fixed with an **in-memory idempotency latch** on the loser (`DvergrCompanion._duelResolved`): the subdue now runs through `ResolveSubdue(winner)`, which resolves the bout **exactly once** (credits the win + leaves duel mode) and is reset only when the companion next *enters* duel mode. The damage prefix also swallows any further hits on an already-resolved duelist (`IsDuelResolved`). Being in-memory, the latch guards even before the ZDO flag propagates. Party duels reuse the same latch to bench a member exactly once.
- **PvP immunity (req 6)**: the same `Character.Damage` prefix cancels any hit on a duel-mode companion whose attacker is a `Player`, regardless of the PvP flag.
- **Winner reward**: flat 50 XP via `DvergrCompanion.AddXp` (Phase 3 path) plus a `dvergr_duel_won` event into ServerGuide.
- **Notifications**: enter/stand-down/defeat/win each raise a speech bubble above the companion (`Chat.SetNpcText`) and a center HUD message to the owner.

### Butcher-knife betrayal (non-duel)

Separate from duels: if **any** player (owner included) strikes a companion with a **butcher knife** (`KnifeButcher`), the companion goes **feral** — hostile to *all* players via `IsHostileTo` returning true for everyone. This is a deliberate release/betrayal action, not timed retaliation. A duel-mode companion is immune to player hits (req 6), so the butcher rule only applies outside duel mode.

## MP caveat

Notifications and state transitions are driven on the ZDO-owning instance (the same "gated to the ZDO owner" pattern chores use). On a dedicated server with server-owned creature ZDOs there is no local player/chat, so bubbles/center messages may not show there; this works cleanly on a listen host and for the owning client. ZDO writes claim ownership first (`ZNetView.ClaimOwnership`) to replicate the flag.

## Open Questions (resolve after in-game verification)

- Any-caste vs. same-caste-only rivalries: currently any two other-owner companions can duel, no caste restriction.
- Cooldown between duels: none currently (the different-owner requirement already blocks solo farming).
- Whether a physical arena/structure is worth adding for feel.
- `OwnerVisionRange` (40m), `DuelDetectRange` (30m), and the 60s wait timeout are first-pass values — tune once playtested.
