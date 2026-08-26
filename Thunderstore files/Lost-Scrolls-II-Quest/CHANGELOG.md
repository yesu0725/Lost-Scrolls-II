# Changelog

## 0.9.1

- Requires **Lost Scrolls II 0.9.1** and **ValheimServerGuide 0.15.0**.
- **Haldor’s commission is opened with `Shift + E`.** The guidance said “hold [E]”, which was correct until ValheimServerGuide changed that key in 0.14.0. A plain `E` still opens his store.
- No new content — the nine guidance files are otherwise unchanged from 0.9.0.

> **This is the first Quest pack release since 0.7.0.** Upgrading from 0.7.0 brings two releases at once: the bounty-hunting guidance and rewards (0.8.0) and the wagered tournaments and duel invites (0.9.0). Nothing needs deleting — every file lives in the same `config/ValheimServerGuide/LostScrollsII/` subfolder.

## 0.9.0

- Requires **Lost Scrolls II 0.9.0** and **ValheimServerGuide 0.15.0**.
- **The bounty quest actually works now.** Haldor's commission grants its reward on a dialogue
  choice, and rewards on that kind of choice were silently discarded by every ServerGuide up to
  0.14.0 — so the conversation played, you accepted the commission, and no first bounty was ever
  marked on your map. Fixed in ServerGuide 0.15.0, which is why the dependency moved. Nothing in
  this pack needed changing.
- Haldor's commission is opened with **Shift + E** (a plain `E` still opens his store). The pack
  said "hold [E]", which was right until ServerGuide 0.14.0 changed the key.
- **New guidance file — `guidance.wagers.yaml`** (9 files in total now). Teaches the staked tournaments and duel invites added in 0.9.0: what they cost, that the bracket starts by itself when it fills, that you and your opponent pick where each duel happens, and that an unfilled tournament refunds everything.
- It also carries the **Valcoin champion's purse**. As with the bounty rewards, this mod never states a Valcoin amount — it sets a key the **Valheim Donations** mod reads and prices from the server's own config.

> **Server setup for the Valcoin purse.** Add `ls_tournament_prize` to the donations mod's `valcoin_quests.yaml` with **`capped: false`** — without that flag the backend trims the 100-coin purse to its 8-coin daily allowance. Needs Valheim Donations **5.20.0+** and its backend **0.10.0+**. Coin tournaments need no setup at all.

> **Upgrading?** Nothing to delete this time — the new file lands in the same `config/ValheimServerGuide/LostScrollsII/` subfolder as the rest.

## 0.8.0

**Adds the bounty-hunting content: the warden's commission and the reward tables.**

- **`guidance.bounty.yaml`** — *The Warden's Commission*. Haldor's dialogue (hold **[E]** on him) explaining that most Dvergr are merely held and can be freed, but some turned the corruption into a weapon and hunt the roads by choice. Accepting grants your first bounty; finishing it opens the Wanted Board. Includes a follow-up page teaching how bounty hunting works.
- **`guidance.bounty-rewards.yaml`** — the **reward bundles**, one per difficulty tier, plus the optional Valcoin payouts. This is where all bounty loot is defined: edit this file to retune it, no code changes needed.
- Now **eight** guidance files, all under `config/ValheimServerGuide/LostScrollsII/`.

> **Requires ValheimServerGuide 0.14.0+.** The bounty entries use a new `tier:` trigger filter. On an older ServerGuide the tier filter is ignored, which would fire **every** tier's reward bundle at once — so this pack's dependency is deliberately strict.

> **Valcoin payouts are optional and need server setup.** The coin half only pays if the server runs **Valheim Donations** with quest ids `ls_bounty_t1` … `ls_bounty_t5` defined in its `valcoin_quests.yaml`. Without them the item rewards still pay normally.

## 0.7.0

**New: the Bog Witch's Dvergr rites** — a way to find and free your first Dvergr companions **without traveling to the Mistlands**, courtesy of a new `guidance.bogwitch-rite.yaml` file. Requires the **Bog Witch** trader (from the `ProfMags-TraderOverhaul` mod, e.g. the Hearthbound modpack) — on a server without her, this content simply never appears, with no effect on anything else.

- **"An Echo in the Mire"** — talk to the Bog Witch about a stirring in her mire. She points you to the Sunken Crypts, where an echo of a Dvergr lies bound in the bones of the rotted dead.
- **"The Rite of Waking"** — kill 2 Draugr Elite in the Swamp and a wild, unrecruited **Rogue-caste** Dverger wakes nearby, free to subdue and commune with normally. Renews **weekly**.
- **"The Deeper Echo"** — unlocked after you've completed the rite above at least once. The Bog Witch reveals a second, stranger echo, bound in something that only walks her mire by **night**.
- **"The Rite of the Unseen Hand"** — kill 2 Wraith (a **night-only** Swamp spawn) and a wild **DvergerMage** wakes nearby. Its caste (Fire, Ice, or Support) is random — the same odds as meeting one naturally in the Mistlands. Renews **weekly**.
- Both rites grant a small Coin reward alongside the spawned companion. No code changes in either mod — built entirely from ServerGuide's existing `npc_conversation` / `kill` / `spawn_creature` building blocks.
- Updated to depend on **Lost Scrolls II 0.7.0**, which makes the **chest/storage window movable** — drag it anywhere by an empty part of the panel, and it opens two inventory rows lower by default so extra rows from other inventory mods stay visible. See the base mod's changelog for detail.
- Story, Companion Handbook, ranking and tournament guidance are otherwise **unchanged**; still depends on **ValheimServerGuide 0.9.0+**.

## 0.6.0

- Updated to depend on **Lost Scrolls II 0.6.0** — the Communion Rite is now a **channeled struggle**: subdue a Dvergr, then **hold Block** to channel the rite (you can keep blocking/dodging), and it can fail if you lower your guard, stray too far, or take an unblocked hit. Recruiting moved off `G` (now Feed-only) onto Block. See the base mod's changelog for detail.
- Story, Companion Handbook, ranking and tournament guidance are **unchanged**; still depends on **ValheimServerGuide 0.9.0+**.

## 0.5.0

- Updated to depend on **Lost Scrolls II 0.5.0** — tournament formats (single/double elimination, round robin), optional level-gated events, companion level shown on the bracket, live win–loss standings, an entrant cap, full-heal-on-summon, and important fixes to how ranking/tournament data is saved and synced. See the base mod's changelog for detail.
- **Updated `guidance.rankings.yaml` and `guidance.tournaments.yaml`** — rewritten around the in-game **UI**: the ranking pages now point players to the **`F6`** ranking board (instead of console commands), and the tournament pages describe the current **`F7`** panel flow — sealing a companion into a Communion Totem, **Lock Totem → Enter**, automatic summon at full health, and **View Bracket** for pairings, levels and standings.
- **New guidance page** — *"Entering a Tournament"* teaches the whole `F7` entry flow the first time a player frees a companion.
- Story and Companion Handbook content unchanged; still depends on **ValheimServerGuide 0.9.0+**.

## 0.4.0

- Updated to depend on **Lost Scrolls II 0.4.0** (in-game ranking board `F6`, tournament panel `F7`, totem-based tournament entry with auto-summon, party names, admin controls) and on **ValheimServerGuide 0.9.0**, which adds the message templating and Discord support these guidance files rely on.
- **Moved into their own subfolder** — the bundled guidance now installs to `BepInEx/config/ValheimServerGuide/**LostScrollsII/**` instead of the top level, so it stays separate from any guidance your server already runs and is easy to remove. ServerGuide loads its config folder recursively (0.8.0+), so nothing else changes.
- **New guidance file**:
  - `guidance.duels.yaml` — broadcasts **every duel win** (1v1 and party, tournament matches included) to server chat and Discord.
- **Updated** `guidance.rankings.yaml` — added "new #1" entries for both ladders (a companion or party reaching the top of the board), with Discord announcements.
- **Updated** `guidance.tournaments.yaml` — the champion announcement now posts a fully templated message to Discord.
- Story and Companion Handbook content unchanged.

> ⚠️ **Upgrading from 0.3.0:** delete the old **top-level** `guidance.lost-scrolls.yaml`, `guidance.companions.yaml`, `guidance.rankings.yaml` and `guidance.tournaments.yaml` from `BepInEx/config/ValheimServerGuide/`. Mod managers don't delete files a new version stopped shipping, so leaving them loads the guidance twice (duplicate ids).

> **Rankings & tournaments require ValheimServerGuide 0.9.0+** — these guidance files are what announces and rewards them (rank-ups, "new #1", tournament pairings, champion prizes, Discord). Without ServerGuide the ladders still record and `F6` still shows the standings, but nothing announces or rewards them.

> **Discord is optional.** These announcements only post if the **server** sets `DiscordWebhookUrl` in the ServerGuide config. Without it the in-game chat/messages still fire and the webhook step is skipped.

## 0.3.0

- Updated to depend on **Lost Scrolls II 0.3.0**, which adds the **competitive suite** — 1v1 duel rankings, party duels, party rankings, and tournaments — plus player-icon map pins and companion death markers. See the base mod's changelog for detail.
- **New guidance files** bundled into `BepInEx/config/ValheimServerGuide/`:
  - `guidance.rankings.yaml` — duel-ladder + party-ladder reference pages and rank milestones (`category: Rankings`).
  - `guidance.tournaments.yaml` — tournament join/pairing announcements and the champion prize bundle (`category: Tournaments`).
- Story and Companion Handbook content unchanged.

> **Note:** the dynamic values in the new rankings/tournament messages (rank, rating, opponent, round, etc.) require a **ServerGuide build that includes the Lost Scrolls II competitive template variables**. With an older ServerGuide the guidance still fires, but those `{...}` placeholders show literally. Update ServerGuide alongside this pack for full templating.

## 0.2.0

- Updated to depend on **Lost Scrolls II 0.2.0**, which adds the **companion inventory system** (per-companion 4×2 pack, item pickup, food/mead consumption, weight/encumbrance, death-drop, totem carry-over, wood-portal cargo block, ComfyQuickSlots compatibility). See the base mod's changelog for detail.
- **Companion Handbook** (`guidance.companions.yaml`) updated for the inventory system: a new **"Your Companion's Pack"** guidance (opening the 8-slot pack with `[Y]`, auto-pickup, self-feeding, health/resist meads, the 150 weight cap, death-drop and totem carry-over), and corrected the `[Y]` key description in "Commanding Your Companion" (it now opens the pack — which holds the rename field — rather than only renaming).
- Story content unchanged.

## 0.1.0

Initial release of the complete "Quest" pack.

- A content pack that pulls in the **Lost Scrolls II** gameplay mod as a dependency and adds its full in-game narrative, ready for single player — no manual guidance-file setup.
- Requires **Valheim ServerGuide** (`TaegukGaming-ValheimServerGuide-0.7.1`), which delivers the story and Companion Handbook.
- Ships two guidance files into `BepInEx/config/ValheimServerGuide/`:
  - `guidance.lost-scrolls.yaml` — the biome-by-biome story descent (Meadows → Ashlands).
  - `guidance.companions.yaml` — the Companion Handbook (command keys, per-caste chores, adventuring tips).
- Same gameplay as the base Lost Scrolls II package: Communion recruitment, leveling (1→10), caste chores, duels, Communion Totems, and ship/portal travel. Vanilla assets only.

See the base **Lost Scrolls II** package changelog for the underlying mod's feature history.
