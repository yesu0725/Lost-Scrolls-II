# Ally Commands — Feed, Stance, Rename, Hover, Map Pins & Travel

Feature adds (post-Phase 6): companion-care/control mechanics that didn't fit neatly into Recruitment, Leveling, or Chores — **feed/heal**, **stance**, **rename**, the **crosshair hover tooltip**, the **capability voice lines**, **minimap pins**, and **travelling with the owner** (ships + portals). Most are hover + hotkey interactions, consistent with every other mechanic in this mod (no new UI assets). Default keys: feed `G`, stance `E`, rename `Y` (all configurable). Ship riding has its own doc — see [Ship-Riding.md](Ship-Riding.md).

## Feed / Heal (revised — now mead-based, not generic food)

- **Key**: `CommunionKey` (default `G`) — **not** a separate key. Originally this had its own `FeedKey` (`F`), but since the Communion key has no further use once a Dvergr is already recruited, the same key now does double duty: subdued+unrecruited → Communion, already-recruited → Feed. See [Ally-Recruitment.md](Ally-Recruitment.md).
- **What it consumes**: specifically a **health mead potion**, not any food item. Detected by *behavior*, not by name: any `Consumable` whose `m_consumeStatusEffect` is an `SE_Stats` that restores health (`m_healthUpFront + m_healthOverTime > 0`). See `MeadFeedingService.HealthMeadRestoreAmount`.
- **Heal amount — read live from the potion's own data, not hardcoded**: the potion's real heal total (`m_healthUpFront + m_healthOverTime` off its consume status effect) is read at the moment of feeding, expressed as a fraction of the *player's* max health, then that same fraction is applied to the *companion's* max health. This keeps the heal proportionate across wildly different companion health pools at different levels, while reflecting each potion tier's actual real value rather than a guessed flat number.
- **Feedback VFX**: feeding plays the potion's *own* consume effect (`m_consumeStatusEffect.m_startEffects`) on the companion — the same healing burst you see when you drink the mead yourself — parented to the Dvergr. Vanilla-assets-only: it reuses the potion's existing effect list, no authored VFX.
- **NOT owner-gated — anyone can feed anyone's companion.** Unlike stance/rename/chore/duel (which stay owner-only), the heal is deliberately shared: any player may offer a mead to any recruited Dvergr — a friend can top up your ally, or heal a duel loser. The success line reads *"Your ally drinks deep."* for your own, or *"&lt;name&gt; drinks deep."* for someone else's.
- **Cross-client heal via `Character.Heal`, not `SetHealth`.** `Character.SetHealth` writes health only on the ZDO owner (no RPC fallback — confirmed against the decompiled assembly), so healing someone else's companion — or a duel loser whose ZDO the winner's client claimed during the subdue — silently did nothing. `MeadFeedingService.TryFeed` now calls `Character.Heal(amount, true)`, which **routes to the ZDO owner over `RPC_Heal`** when the feeder isn't the owner. It does **not** claim ownership (stealing the ZDO would strand the companion's follow AI on the feeder's client); `Heal` clamps to max HP and shows the heal number.
- **Implementation**: `MeadFeedingService.TryFeed`, called from `Plugin.HandleCommunionInput`.

### Bug fix: feeding never worked (wrong heal source)

**Root cause (verified against the real assembly metadata via reflection, not guessed):** healing meads do **not** use `m_food`. That field is only for *food* items (the HP/stamina/regen you gain from eating). A mead's `m_food` is `0`; its heal is delivered by `m_consumeStatusEffect`, an `SE_Stats` whose `m_healthUpFront`/`m_healthOverTime` hold the actual restore amount. The first implementation filtered on `m_food > 0` (so it rejected every real mead) and computed the heal as `m_food / maxHealth` (so it would have healed `0` even if one slipped through). That is why the mead potions did nothing.

The fix stops matching prefab names altogether and identifies a healing mead by what it does — a consumable whose consume effect is an `SE_Stats` that restores health — then heals proportionally from that effect's real values. This works for every mead tier and any modded healing mead. The `SE_Stats` field names (`m_healthUpFront`, `m_healthOverTime`) and `SharedData.m_consumeStatusEffect` were all confirmed against `assembly_valheim.dll`'s actual metadata.

If feeding still doesn't detect a potion you're holding, `MeadFeedingService.TryFeed` logs every Consumable in your inventory with its real prefab name, `m_food`, and consume-effect type to the BepInEx log on a failed attempt — check that log rather than guessing.

## Stance: Follow / Guard / Standby

- **Key**: `StanceCycleKey` (default `E`), configurable. *(`E` is vanilla "Use"; a Dvergr has no interaction, so hovering one and pressing `E` only cycles stance.)*
- **Flow**: press while hovering your companion to cycle Follow → Guard → Standby → Follow.
- **Follow**: `MonsterAI.SetFollowTarget(player)`. Attacks monsters, and players only as governed by the threat rules below.
- **Guard**: clears the follow target, anchors a `MonsterAI.SetPatrolPoint()`, and multiplies `m_alertRange` (×2.5) so it proactively engages threats near its post.
- **Standby** (new): passive — `m_alertRange` set to 0 and target cleared each tick, so it does **nothing**, including not attacking monsters. The only exception is retaliation (see below). Holds position.
- *(The earlier "Stay" stance was removed; Standby replaces the need for it.)*
- **Still a cycle, not a menu.** An in-world stance-selection menu was considered but kept as the lightweight `E`-cycle, consistent with discarding the chore menu (the cycle already reaches all three stances in two presses). Revisit only if cycling proves clumsy in play.

## Hover tooltip (crosshair)

- Looking at **your** companion shows a crosshair tooltip with its current **Stance** and the command hints `[E] Cycle stance` and `[Y] Rename`. Implemented as a `Character.GetHoverText` postfix (`CompanionHoverTextPatch`); `GetHoverText` is declared on `Character` and not overridden by `Humanoid`, so a Dvergr resolves to it. Owner-only — another player's companion shows just its floating name/badge.
- Separate from the floating name above the creature (that's `GetHoverName`, which carries the custom name, an **owner name tag** `(OwnerName)`, and the `★N` level badge — `CompanionNameBadgePatch`). The owner tag identifies whose ally is whose, which matters for cross-player duels; owner name is persisted on ZDO `DE_OwnerName` so it shows even when the owner is offline.

## Rename

- **Key**: `RenameKey` (default `Y`), configurable. Hover **your** companion and press `Y` → the **vanilla text-input box** opens (the same one used for signs and tamed-animal renaming), prefilled with the current name.
- **How it works**: `DvergrCompanion` implements `TextReceiver`; `Plugin.HandleRenameInput` calls `TextInput.instance.RequestText(companion, "Rename companion", 24)`. Confirming calls `SetText` → stores the name on the companion's ZDO (`DE_Name`) and the ally acknowledges it ("I'll answer to … now.").
- **Where the name shows**: the floating name + badge above the creature (`GetHoverName`), the crosshair tooltip, and the **chore claim tooltip** on stations/smelters (*"&lt;name&gt; is already working here."* via `ChoreAI.WorkerName` → `DisplayName`).
- **Persists** across relog/zone reload (ZDO-stored, read in `Awake`). Unnamed companions fall back to the localized creature name ("Dvergr").
- **Owner-only**, like every other command. Input is ignored while a text field (rename box / chat / console) has focus, so letters typed into a name don't fire other hotkeys.

## Voiced identity: capability lines replace vanilla Dvergr chatter

- **Vanilla chatter silenced on recruit.** A wild Dvergr's ambient barks ("intruder!", grumbles, greets/goodbyes, aggravated lines) are driven by the `NpcTalk` component. On the freed state (`CommunionService.ApplyFreedState`, shared by recruit / relog-restore / `de_spawn`) that component is **disabled**, so a companion no longer talks like a hostile camp Dvergr.
- **Replaced with stance + caste "what I can do" lines.** `DvergrCompanion.AnnounceCapability()` shows a vanilla NPC speech bubble (`Chat.SetNpcText`) describing what the ally can do **in its current stance**, e.g. Follow → *"I'll follow and fight at your side. Set me to tend smelters, kilns and forges."*, Guard → *"I'll hold this ground. Or set me to …"*, Standby → *"I'll wait here, quietly. Set me to … when you're ready."* The skill phrase is caste-specific (Fire→smelting, Ice→refineries, Support→farm/cook/brew/beasts, Rogue→haul). Spoken **on recruit** and on **every stance change**, so the line always matches the companion's posture. Vanilla-assets-only (reuses the NPC speech system; no authored audio/text assets).

### Ownership & threat behavior

- **Owner**: the recruiting player is recorded as owner (`DvergrCompanion` ZDO `DE_Owner` = `Player.GetPlayerID()`). Commanding it — chore, stance, rename, duel — is **owner-only**; another player gets "This companion answers to another." **Feeding is the exception: any player may feed any companion** (see the Feed / Heal section). (Companions recruited before this change have no owner and accept anyone, for save compatibility.)
- **Selective hostility to players** is injected via a `BaseAI.IsEnemy` postfix (`CompanionIsEnemyPatch`) keyed off `DvergrCompanion.IsHostileTo`, so vanilla targeting then does the rest — no faction hacks:
  - **Guard**: every non-owner player is a threat.
  - **Follow**: only players the **owner attacked** (timed, 30s), wired from a `Character.Damage` prefix (`CompanionDamagePatch`).
  - **Any stance (incl. Standby/chores)**: a player who **attacks the companion** is retaliated against (timed, 30s) — `Retaliate` marks them hostile and sets them as the immediate target.
  - **Butcher-knife betrayal**: if any player (owner included) strikes a companion with a **butcher knife** (`KnifeButcher`), it goes **feral** — `GoFeral` makes `IsHostileTo` return true for *every* player, permanently (until it dies), not timed. A deliberate release action. Detected in `CompanionDamagePatch` by the attacker's equipped weapon name. (A duel-mode companion is immune to player hits, so this only applies outside duel mode — see [Duel-Arena.md](Duel-Arena.md).)
  - The **owner is never** a threat (unless the companion has gone feral).
- **Busy guard**: stance changes are blocked (with a message) while the companion is chore-assigned ([Ally-Chores.md](Ally-Chores.md)) or actively dueling ([Duel-Arena.md](Duel-Arena.md)), since both of those already drive the companion's `MonsterAI` directly.
- **No persistence**: like chore assignment, stance is in-memory only (`DvergrCompanion.Stance`) and resets on reload/relog — the companion will need Follow re-issued. Consistent with the existing chore-assignment limitation, not a new gap.
- **Implementation**: `DvergrCompanion.SetStance()`, called from `Plugin.HandleStanceCycleInput`.

## Minimap pins — find your companions

- **A live map pin at each of your own companions.** `CompanionMapPins` (a
  component on the plugin GameObject) maintains one vanilla minimap pin per
  companion the **local player owns**, refreshed to the companion's world position
  ~4×/second and removed when it despawns.
- **Private by design.** Map pins are entirely **client-side**, so pinning only
  companions where `DvergrCompanion.OwnerId == the local player` means **another
  player never sees your companions on their map**, and you never see theirs.
  Unowned/legacy allies (no recorded owner) are deliberately **not** pinned.
- **Transient.** Pins are added with `save = false`, so nothing is written to the
  map save file; they rebuild cleanly when the `Minimap` is recreated
  (entering/leaving a world). The pin label follows the companion's display name
  (renames included).
- **Config** (`Companions` section): `ShowMapPins` (default on) and `MapPinIcon`
  (which vanilla 0-4 pin sprite to use, default 3).
- Vanilla-assets-only: reuses `Minimap.AddPin` / the stock pin sprites — no custom
  icons.

## Travelling with the owner — ships & portals

Two ways a **Follow**-stance ally comes along when the owner moves between places.
Both are gated on Follow only (a chore / duel / feral / Guard / Standby ally stays
where it is) and use vanilla mechanics — no new assets.

- **Ships.** Follow companions board the owner's ship through a ladder and then walk
  the deck freely; they avoid water otherwise. Full detail in
  [Ship-Riding.md](Ship-Riding.md).
- **Portals.** When the owner steps through a portal, every companion **in Follow
  stance, owned by that player, and loaded nearby** is teleported to the destination
  and keeps following. Implemented by `CompanionPortalPatch`, a postfix on
  `TeleportWorld.Teleport(Player)` (portals route through `Player.TeleportTo`, which
  records the destination on the player as `m_teleportTargetPos`/`m_teleportTargetRot`).
  - Each companion's **ZDO position is committed** to the destination after
    `ClaimOwnership`, so it survives the zone change: the old instance unloads during
    the portal fade and the ZDO re-instantiates the ally at the exit once that zone
    loads. They're spread in a small ring so they don't stack on the player.
  - **Owner-scoped and client-local:** the patch only acts for the local player, and
    only moves companions whose `DE_Owner` matches — so another player portalling
    never drags your allies, and you only bring your own.
  - Logs `[portal] Brought N companion(s) through the portal…` when it fires.
  - The only requirement is Follow — this replaces the removed `LadderClimbAI`
    ladder-climbing experiment as the reliable "get the ally to where I am" path.

## Needs In-Game Verification

- Whether `SetPatrolPoint()` actually keeps a companion anchored, or whether it still wanders within some radius (untested — name suggests "patrol" which may imply roaming between points, not a hard anchor).
- Whether widening `m_alertRange` for Guard has any side effects beyond detection radius (e.g., interaction with vanilla's own threat-assessment logic) that weren't visible from static analysis of the assembly.
- Whether the behavior-based mead detection (`HealthMeadRestoreAmount`: consumable + `SE_Stats` consume effect with health restore) matches the real potions in-game. Much more robust than the old name filter, but still unconfirmed in a live session.
- Whether the flat per-potion heal feels right on higher-level companions (their max-health pool grows with level, so a fixed potion heal is proportionally smaller — by design, but unverified in play).
- Whether the **minimap pins** track smoothly and clear on despawn, and that in multiplayer each player only ever sees their **own** companions' pins ([Testing.md](Testing.md) §14).
- Whether **portal follow** actually lands the ally at the destination after the zone finishes loading (ZDO position commit vs. zone unload timing), only brings Follow-stance own companions, and behaves in multiplayer ([Testing.md](Testing.md) §15).
