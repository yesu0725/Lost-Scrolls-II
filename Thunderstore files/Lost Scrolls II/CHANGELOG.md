# Changelog

## 0.10.0

**Your companions stay with you now** — by your side in a fight, mended at your fire, and along for the ride through more kinds of portal.

- **They stop running off.** A **Follow** companion now fights only within about **20 metres** of you — roughly a workbench's build radius. It won't set off across a field after something far away, and if a chase drags it past that ring it breaks off and comes back. Protecting you is the job. (Tunable: `Companions/FollowEngageRange`.)
- **Working allies work.** A companion on a **chore** no longer downs tools to chase whatever wandered past. It stays at its station and only fights something that actually hits it, then goes back to work.
- **Standby means standby.** An ally set to Standby holds its ground properly — no idle wandering off the spot, no picking fights — and defends itself if attacked. Previously it did neither.
- **One shout, not a chorus.** A companion that was being hit while it couldn't fight back — lagging behind, on a chore, on Standby, or overloaded — let out its alert cry on **every single blow**. Now it calls out once, as it should.
- **Rest at camp to heal them.** Sit by a campfire, or stand under a roof with one lit, and every Follow companion near you heals to full over about two minutes, with a **Resting** icon above its health bar. The healing stops the moment you get up and move on. (Tunable: `Companions/RestedHealSeconds`, `RestedHealRadius`.)
- **Your sealed companions keep their names.** A Communion Totem in your pack or a chest reverted to a plain **Fuling Totem** after a relog. Worse, it also got its ability to stack back — so two sealed companions sitting in one slot could merge, and **one of them would be gone**. Fixed on every path an item can load from. If you have been carrying sealed companions, they are safe now.
- **Companions come with you through InterServerPortal.** Follow-stance allies now travel through that mod's **network portals** exactly as they do through a vanilla one. And on an **inter-server** crossing — where the world itself changes and a companion simply cannot be carried across — they are sealed into their Communion Totems as you leave and summoned back beside you on arrival. If your pack is full the ally stays behind in the old world rather than risking its totem.

> Purely optional: InterServerPortal is not required. Nothing here changes if you don't run it.

## 0.9.1

A one-line correction, but to the line that tells you how to start bounty hunting.

- **The Wanted Board named the wrong key.** It told you to *hold* **[E]** on Haldor to take the warden’s commission. That was right when bounty hunting shipped, but ValheimServerGuide changed the key to **Shift + E** in its 0.14.0, so the board had been teaching a way in that no longer worked — and the board is the only place the game explains how to begin. It now says **Shift + E**. A plain `E` still opens Haldor’s store, as always.

> **Running the Quest pack for bounty hunting?** It now requires **ValheimServerGuide 0.15.0**. Rewards attached to a dialogue choice were silently discarded by every earlier build, and that is exactly how Haldor hands over your first commission — on 0.14.0 the conversation plays, you accept, and no bounty is ever marked on your map.

## 0.9.0

**Put a companion on the line.** Staked tournaments and duels that any player can start, plus a faster way to pack a companion for the road.

- **Anyone can run a tournament.** Open one for **100 Coins** or **10 Valcoins** from the tournament panel — no admin needed. That also pays your own entry, so you're never charged twice. A fixed **4-player** bracket that starts by itself the moment it fills; the champion takes **999 Coins** or **100 Valcoins**. One Coin tournament and one Valcoin tournament can run at a time, alongside any free tournament an admin has started.
- **You choose where each duel happens.** Nobody is teleported anywhere. Meet your opponent wherever you both like — an arena you built, a field, a mountaintop — and you both press **Ready to Fight**. Your two companions are summoned right there at full health, locked onto each other, and fight until one is subdued. No other companion can join in.
- **Duel invites.** Rather than a whole bracket, post a single staked challenge for **100 Coins** or **10 Valcoins**. Anyone can accept by matching your stake, and the winner takes both. You can only be in one invite at a time, but everyone else can have their own open at once.
- **Nothing is lost if it falls through.** A tournament that never fills is cancelled and every stake and companion totem is returned. Withdraw during registration and you get your totem and your stake back in one go. Back out of a duel invite and both sides are made whole.
- **Seal a companion in the field.** With a **Dead Raiser** equipped, a **Wisp** in your pack and **Blood Magic 20+**, hold **Block** on your own Follow-stance companion to seal it into a Communion Totem anywhere — no walk back to an Incinerator. It takes 5 seconds at Blood Magic 20, down to 2 seconds at 100. The Incinerator ritual is unchanged, and both produce exactly the same totem.
- **Buttons for every panel.** The Bounty Board button at the top of your inventory screen is now a row of three — **Rankings**, **Tournaments**, **Bounty Board** — so you don't have to remember F6/F7/F8. The keys still work.
- Messages triggered from a panel now appear in the **top-left** instead of behind the panel, and the tournament panel's buttons no longer sit under its own text.
- The Wanted Board told you to **hold [E]** on Haldor to take the warden's commission. That key changed to **Shift + E** in ValheimServerGuide 0.14.0, so the board was teaching a way in that no longer worked. It now says Shift + E.

> **Valcoin stakes** need the **Valheim Donations** mod on the server (and a small config entry for the champion's purse). Coin stakes work with this mod alone; where Valcoins aren't available the option is refused with a clear reason.

## 0.8.0

**Bounty hunting — hardened Dvergr are posted on a Wanted Board, and hunting them pays.** *(Server feature: see the note below.)*

- **A new kind of Dvergr.** Bounty targets are **hostile on sight** — unlike every other Dvergr in the mod, they don't wait to be provoked. They come with an escort, and both scale with the posting's difficulty (five tiers, from *Wanted* to *Accursed*).
- **Kill them, or free them.** The Communion Rite closes a bounty exactly as the sword does, and pays the same. Freeing a bounty target keeps it as your companion **and** pays the reward.
- **The Wanted Board.** Press **F8**, or use the new **Bounty Board** button at the top of your inventory screen, to read the postings. Take one and it's marked on your map; travel there and it will find you before you find it. The board rotates on its own, so there's always fresh work.
- **A quest to get in.** Haldor keeps the list. **Hold [E]** on him (a short press still opens his store) to hear why these Dvergr are hunted and take your first commission — a guaranteed easy posting, close by, marked on your map.
- **Rewards worth the trip.** Each tier pays a bundle of things that take a forge, a smelter or a cooking fire to make — plus coins, scaling with difficulty.
- **A hunters' board.** Bounties answered are scored by difficulty on a persistent leaderboard, shown on the **F6** ranking board alongside the duel and party ladders. The best hunters earn better reward bundles.
- **A reason to duel.** The hardest **Accursed** postings can only be answered by hunters ranked in the top 10 of the duel or party ladder — they're visible to everyone, locked until you've earned them.
- **New settings** under **Bounty**: difficulty curve, escort size, board size and rotation, spawn placement, and the elite rank gate. All server-side.

> **This is a server feature.** Bounty hunting only runs on a server (or a local host) that has **BiomeLords**, **ValheimServerGuide** and **Valheim Donations** installed together. Everywhere else — including single player — the F8 panel and the inventory button still open and explain what bounty hunting is and where it runs. Nothing else in the mod is affected.

## 0.7.0

**The chest/storage window can now be moved wherever you want it.**

- **Drag it anywhere.** Grab an empty part of the storage window — the background, not an item slot — with the left mouse button and drop it where you like. It lights up faintly when the cursor is over grabbable space. Item slots, **Take All** and the companion name field all keep working exactly as before; they're never grabbed by accident.
- **Your position is remembered**, across every chest, every companion pack, and across relogs.
- **A better default.** The window now opens **two inventory rows lower** than vanilla, so extra inventory rows added by other mods stay visible instead of hiding behind it. This replaces the old ComfyQuickSlots-specific fix, which tried to detect other mods and guess — nothing is guessed now.
- **It can't get lost.** The window always keeps part of itself on screen, and `de_container_reset` (or setting the config back to `auto`) puts it straight back to the default spot.
- **New settings** under **Interface**: `ContainerPanelOffset` (the `x,y` position, or `auto`) and `MoveContainerPanel` (turn the whole thing off). The old `Companions / AdjustContainerPanel` setting is gone.
- **BiomeLords users:** BiomeLords already does this for the same window, so when it's installed Lost Scrolls II stands aside completely and leaves the position to it.

## 0.6.0

**The Communion Rite is now a channeled struggle — you free a Dvergr by holding it through the rite while the corruption fights back.**

- **Hold to commune (no longer instant).** Subdue a Dvergr to low health, then **hold your Block button** with the crosshair on it for a few seconds to complete the rite. The tooltip reads `Hold [Block] — Communion`.
- **You can still fight while you do it.** The rite runs on Block on purpose — your shield stays up, so you can **keep blocking and dodge-roll** through the vulnerable channel. A quick roll won't break it.
- **It can fail.** Lower your guard, stray too far (~4 m), or take an unblocked hit and the corruption reclaims the Dvergr — it turns hostile again and must be re-subdued. A *blocked* hit does no damage, so shielding up protects the rite.
- **An accelerating pulse of light** marks the ritual on both you and the Dvergr, quickening as it nears completion — no cluttered progress bar.
- **`G` is now Feed only.** Recruiting moved to the Block button; `G` still feeds an already-recruited companion. New settings under **Recruitment**: `CommunionChannelSeconds` (5), `CommunionMaxDistance` (4), `CommunionBreakOnDamage` (on).

## 0.5.0

**Tournament formats, level-gated events, live standings — plus important fixes to how competitive data is saved.**

- **Three bracket formats** — the host can now run a tournament as **single elimination**, **double elimination**, or **round robin**. Pick the format on the `F7` panel with the new **Type** button (cycles through the three) before starting, or as the last argument of the start command.
- **Level-gated entry** — an optional setting (`Tournaments / RequiredEntrantLevel`, default off) restricts a tournament to companions of an exact level, so you can run events like a "Level 3 only" bracket. Entries that don't match are turned away.
- **Companion name + level on the bracket** — the tournament board and the `F7` panel now show each entrant's companion **level** next to its name, in the entrant list and in every pairing.
- **Live standings** — while a tournament runs, the bracket view shows a **standings table** with each entrant's win–loss record (sorted for the active format — by wins for round robin, alive-first for elimination), and marks who's eliminated.
- **Entrant cap** — a configurable maximum (`Tournaments / MaxEntrants`, default **4**); starting a tournament without a size, or with a larger one, now uses this cap.
- **Full heal on summon** — a companion is **restored to full health** the instant it's summoned for a match, so every round is a clean fight regardless of prior damage.

**Fixes**

- **Competitive data was being silently lost** — on the current game runtime, the engine's JSON serializer dropped list data (tournament entrants, ladder records) when saving and syncing. The result: a tournament could show **0 entrants** to everyone even after players joined, and it wouldn't survive a server restart; ladder records were similarly at risk. Replaced with a dedicated serializer — entries now persist and sync correctly. **If you run tournaments or ladders on a dedicated server, this fix is the reason to update.**
- **Tournament panel error after relogging** — opening the `F7` panel after a world reload could throw and come up half-built; it now rebuilds itself cleanly.
- **Clicking the panel no longer swings your weapon or moves you** — while the `F7` panel is open, attack, movement, block, jump and camera are now fully blocked (a previous gate missed the attack/movement path on some setups, including when another input-hooking mod was present).
- **View Bracket now refreshes** — opening the bracket asks the server for the latest state first, so it can't show a stale, empty board.

## 0.4.0

**Competitive UI + escrow tournaments** — the ranking and tournament systems now have real in-game screens, and tournament entry works by locking a companion's totem into a slot.

- **Ranking board (`F6`)** — a read-only screen showing both the **duel ladder** and the **party ladder** (rank, rating, owner, W/L, team size). `de_ladder` / `de_party_ladder` still work.
- **Tournament panel (`F7`)** — status, entrants, and one-click controls: **Lock Totem → Enter**, **Withdraw**, **View Bracket**. Opens like a proper menu: mouse cursor free, camera and player actions blocked while it's up.
- **Enter by totem** — you register a companion by locking its **Communion Totem** into a slot. The totem is held (escrowed) for the tournament; when the admin activates a round your companion is **summoned automatically** to fight its assigned opponent, then **resealed back into the totem** (keeping any XP it gained). Totems are always returned — on withdraw, admin release, cancel, or when the tournament ends.
- **Assigned pairings** — a summoned companion only fights its own bracket opponent, so several matches can run at once without crossing over.
- **Admin controls (admins only)** — every setup command as a button: Start 1v1 / Start Party / Begin / **Activate Round** / Cancel, plus per-entrant **Forfeit** and **Release**. Admin actions are verified by the server, so they now work from a remote admin client (not just the host). New console subcommands: `de_tournament withdraw | activate | release <name>`.
- **Party names** — name your team with `de_party_name <name>`; it shows on the party ladder, the ranking board, and in announcements.
- **Discord announcements** (via the optional ServerGuide mod, with a webhook configured): every **duel win** (1v1, party, and tournament matches), every **new #1** on either ladder, and the **tournament champion**.

> **Rankings & tournaments require [Valheim ServerGuide](https://thunderstore.io/c/valheim/p/TaegukGaming/ValheimServerGuide/) 0.9.0+**, which delivers every message and reward these systems produce — rank-up and "new #1" announcements, tournament join/pairing/champion messages, the prize bundles, and the Discord posts. Without it the ladders still record and `F6` / `de_ladder` still show the standings, but nothing announces or rewards them. The guidance files come bundled in the **Lost Scrolls II — Quest** pack. (Discord additionally needs `DiscordWebhookUrl` set on the server.)

## 0.3.0

**Competitive suite** — duel rankings, party duels, party rankings, and tournaments — plus map improvements and compatibility fixes.

- **Duel ladder (1v1)** — every companion-vs-companion duel win is recorded on a persistent, server-authoritative ladder. Each companion has an Elo rating (shown as a rank on its name tag), with wins/losses, streaks, and seasons. `de_ladder` shows the standings; `de_season_reset` (host) starts a new season.
- **Party duels** — gather your nearby Follow-stance companions into a team (`K`) and fight another player's team. Non-lethal (members are benched, not killed); win by attrition, with team-size-scaled XP.
- **Party ladder** — a separate persistent ladder for team duels, keyed by owner with the companions that fought for it (`de_party_ladder`).
- **Tournaments** — a server-run bracket for both 1v1 and party formats (`de_tournament start|join|begin|bracket|forfeit|cancel`). Re-seeded single elimination, ordinary player-run matches, a champion prize, and a Hall of Champions (`de_champions`).
- **Map pins** — companion pins now use the player icon, tinted and smaller, so allies are easy to tell apart from your own marker (configurable color/size).
- **Death markers** — when one of your companions dies (with items or not), a named skull marker is dropped on your map where it fell.
- **Fixes** — the duel "double win" (a win could be announced/counted twice) is fixed; and the companion pack no longer conflicts with **BiomeLords'** chest-UI positioning (it now defers to BiomeLords, with a manual toggle).

Story, rankings and tournament guidance, and the Companion Handbook are delivered through the optional companion mod **Valheim ServerGuide** (bundled in the "Quest" pack).

## 0.2.0

**Companion inventory** — each recruited Dvergr now carries its own pack.

- **Own 4×2 inventory**, opened with `Y` while hovering your companion — a chest-style panel (your inventory + crafting + weight readout) that also carries a rename field and a live HP readout.
- **Picks up loot** it already carries (empty pack = nothing); **combat takes priority** over gathering.
- **Eats food** one at a time for a temporary max-HP boost (with a fed icon), **drinks a health mead** when hurt (below 35% HP, until 90%), and **drinks poison/fire/frost resist meads** for the matching resistance (shown as an icon above it).
- **150 weight cap** — over it the companion stops picking up and won't attack (but still moves), with an encumbered icon.
- **Drops its whole pack on death**, and its pack **rides along when sealed into a Communion Totem** (restored on summon).
- **Wood portals** won't send you if a following companion is carrying a non-teleportable item (with a message naming the ally + item).
- **ComfyQuickSlots compatible** — the pack panel no longer hides the extra inventory row.

## 0.1.0

Initial release.

- **Ally recruitment** via the Communion Rite — subdue a Dvergr and press the Communion key (`G`) to free and recruit it. Rogue, Fire Mage, Ice Mage, and Support Mage castes, auto-detected. Recruit state persists across relogs and server restarts.
- **Ally leveling** — companions earn biome-/toughness-scaled kill XP from 1 to 10 on a rising curve, gaining health and damage plus per-caste bonuses. Gold `★N` level badge and a hover readout.
- **Commands** — feed/heal with a health mead (`G`, any player), cycle Follow/Guard/Standby stance (`E`), rename (`Y`), and private per-player minimap pins.
- **Chores** — assign allies by caste to vanilla stations (`H`): Fire Mage smelting, Ice Mage refining, Support Mage cooking/brewing/farming/animal care, Rogue hauling. Persists across relogs and keeps working while you're away.
- **Dvergr duels** — non-lethal, multiplayer-only companion-vs-companion sparring (`J`) for bonus XP, with PvP immunity and auto stand-down.
- **Communion Totems** — seal Follow-stance companions into carriable totems at an Incinerator using Wisps, and summon them back with level/XP/name intact.
- **Travel** — Follow-stance allies board ships and teleport through portals with their owner.
- **Ownership & threat** — the recruiting player owns the companion; commands are owner-only (feeding is shared). Guard treats non-owners as threats; attacking a companion draws retaliation; a butcher-knife strike turns it feral.

Vanilla assets only — no custom models, textures, sounds, or asset bundles.

Story and an in-game Companion Handbook are delivered through the optional companion mod **Valheim ServerGuide**.
