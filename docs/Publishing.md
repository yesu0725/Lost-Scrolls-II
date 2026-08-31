# Publishing & Release Packaging

How Lost Scrolls II is published: the GitHub repository, the wiki, and the two
Thunderstore packages (base mod + complete "Quest" pack). This is the persistent
reference for rebuilding and uploading releases.

## GitHub

- **Repository (public):** `https://github.com/yesu0725/Lost-Scrolls-II`
- Created 2026-07-03 via the GitHub API using the stored `yesu0725` credential
  (there is no `gh` CLI on the build machine — repos are created with a REST call to
  `POST /user/repos`, and pushes use the Git Credential Manager token).
- Default branch `main`. `bin/`, `obj/`, and `Thunderstore files/*.zip` are gitignored
  (build artifacts / packaged zips are not tracked).
- The working-directory folder was renamed `Dvergr Expanded` → `Lost Scrolls II`
  (Windows locks the folder while a session holds it open, so the actual rename runs
  from the parent via `E:\Valheim Modding\Rename to Lost Scrolls II.ps1` after the
  editor/terminal closes).

## Wiki

Player-facing docs live in two places that mirror each other:

- **In-repo:** the `wiki/` folder (source of truth, modeled on the sibling
  Valheim-ServerGuard wiki).
- **GitHub Wiki:** `https://github.com/yesu0725/Lost-Scrolls-II/wiki` — the same pages,
  pushed to the `Lost-Scrolls-II.wiki.git` backing repo. (That backing repo only exists
  after the first page is created once in the GitHub web UI; after that it accepts
  `git push`.)

Pages: `Home`, `Installation`, `Recruiting-Companions`, `Companion-Commands`,
`Companion-Leveling`, `Companion-Chores`, `Dvergr-Duels`, `Communion-Totems`, and a
**spoiler-free** `The-Story`. **No lore reveal in the wiki** — the gospel allegory, the
"mirror" thesis, the woven Scripture, and the author-only finale/Armor-of-God roadmap
(see [Lore.md](Lore.md)) all stay out of public-facing copy.

## Thunderstore packages

Two packages ship from `Thunderstore files/`. Both use the vanilla-assets-only mod;
the difference is whether the narrative content is bundled.

### 1. Base mod — `Lost Scrolls II`

Folder: `Thunderstore files/Lost Scrolls II/`

| File | Purpose |
|---|---|
| `manifest.json` | name `Lost_Scrolls_II`, version `0.10.0` |
| `icon.png` | 256×256 RGBA PNG (author-supplied) |
| `README.md` | Thunderstore listing copy |
| `CHANGELOG.md` | per-release notes |
| `LostScrollsII.dll` | the built plugin (copied from `src/bin/Release/`) |

**Dependencies:** `denikson-BepInExPack_Valheim-5.4.2333` only. ServerGuide is an
**optional** companion here (narrative delivery only) — the gameplay works fully without
it, so it is deliberately *not* a hard dependency of the base package.

Gameplay-only edition: install this if you want the companion system without the story,
or if your server ships its own ServerGuide guidance.

### 2. Complete pack — `Lost-Scrolls-II-Quest`

Folder: `Thunderstore files/Lost-Scrolls-II-Quest/`

A **content pack** that bundles the story and pulls the gameplay mod + story engine in
as dependencies. It does **not** bundle the DLL — the base mod arrives via dependency.

```
Lost-Scrolls-II-Quest/
  manifest.json        name Lost_Scrolls_II_Quest, version 0.10.0
  icon.png             256×256 RGBA PNG (author-supplied)
  README.md
  CHANGELOG.md
  config/
    ValheimServerGuide/
      LostScrollsII/                (own subfolder so our guidance stays separate — 0.4.0)
        guidance.lost-scrolls.yaml   (the biome-descent story)
        guidance.companions.yaml     (the Companion Handbook)
        guidance.rankings.yaml       (ladder pages, rank milestones, new-#1 + Discord) [0.3.0, upd 0.4.0]
        guidance.tournaments.yaml    (tournament announcements + champion prize)       [0.3.0, upd 0.4.0]
        guidance.duels.yaml          (every duel win -> chat + Discord)                [0.4.0]
        guidance.bogwitch-rite.yaml  (weekly Bog Witch rite: first companions w/o Mistlands) [0.7.0]
        guidance.bounty.yaml         (Haldor's warden commission — opens the board)    [0.8.0]
        guidance.bounty-rewards.yaml (per-tier reward bundles + the Valcoin bridge)    [0.8.0]
        guidance.wagers.yaml         (staked tournaments/duels + the Valcoin purse)    [0.9.0]
```

**Nine files as of 0.9.0.** Keep this list in step with what is actually in the
folder — it was two versions stale once already.

> **Why the `LostScrollsII/` subfolder (new in 0.4.0):** ServerGuide **0.8.0+** loads
> guidance **recursively** from any depth under `BepInEx/config/ValheimServerGuide/`,
> so nesting keeps our files from mixing with a server's own guidance (and makes them
> trivial to remove). This *requires* ServerGuide ≥ 0.8.0 — on an older build the
> nested files are simply never read. The Quest pack depends on 0.9.0, so it's safe.
>
> **Upgrade note:** players coming from 0.3.0 have the old **flat** copies in
> `config/ValheimServerGuide/`. Mod managers don't delete files the new version no
> longer ships, so the stale flat copies can linger and load *alongside* the nested
> ones — duplicate ids. Tell upgraders to delete the old top-level
> `guidance.lost-scrolls/companions/rankings/tournaments.yaml`.

> **ServerGuide version requirement (resolved in 0.4.0):** the bundled guidance uses
> template variables (`{rank}`, `{rating}`, `{winSize}`, `{round}`, `{opponent}`,
> `{mode}`, `{bracketSize}`, `{partyName}`, …) and the `dvergr_rank_first` /
> `dvergr_party_rank_first` triggers. Those, plus **reward-message templating** (so
> `chat_message`/`discord` rewards expand the same tokens), ship in
> **ValheimServerGuide 0.9.0** — hence the bumped dependency. On an older ServerGuide
> the entries still fire but the `{...}` placeholders render literally and the
> Discord reward text is not templated.

**Dependencies (all installed automatically):**

- `denikson-BepInExPack_Valheim-5.4.2333`
- `TaegukGaming-Lost_Scrolls_II-0.10.0` — the base gameplay mod
- `TaegukGaming-ValheimServerGuide-0.15.0` — the story/handbook engine (+ templating, Discord, the `tier:` filter the bounty rewards need, and rewards on node dialogue choices, without which Haldor's commission grants nothing)

**Why `config/ValheimServerGuide/` works out of the box:** ServerGuide loads and
auto-merges every `*.yaml` under `BepInEx/config/ValheimServerGuide/` — recursively,
at any depth, since 0.8.0 (`GuidanceConfigLoader` + `Paths.ConfigPath/PluginName`).
Thunderstore/r2modman routes a package's top-level `config/` folder to
`BepInEx/config/`, so the bundled YAMLs land exactly where ServerGuide reads them —
no manual file copying. They are byte-identical to the in-game-verified copies
(Testing.md §10b–d), sourced from `E:\Valheim Modding\Valheim ServerGuide\examples/`.

This is the complete, single-player-ready experience.


## 0.10.0 — release notes and upload order  *(current)*

Cut **2026-08-31**. A companion-behaviour release: three fixes reported from live play,
one data-loss fix, and two features. **Everything in it was verified in a live session
before the cut** ([Testing.md](Testing.md) §28–§32 all passed) — unusual for this project
and worth keeping to.

**What changed since 0.9.1** (all in the base mod):

- **Companion combat leash.** A Follow ally fights only within `Companions/FollowEngageRange`
  (default **20 m**, a workbench's build radius) of its owner, and breaks off a chase that
  drags it past that. Chore and Standby allies acquire nothing and only answer what has hurt
  them; Standby also stops idle-wandering.
- **The alert bark fired once per hit taken** on any ally that couldn't fight back. Root
  cause was writing `BaseAI.SetAlerted` from a repeating tick — see the rule in
  [Ally-Commands.md](Ally-Commands.md).
- **Sealed totems reverted to Fuling Totems after a relog** — and got their stack cap back
  with the name, which could merge two sealed companions and lose one. This is the one item
  in the release that risked destroying player data.
- **Resting at camp mends Follow companions** (`RestedHealSeconds` / `RestedHealRadius`).
- **InterServerPortal travel** — companions follow through its network portals, and an
  inter-server crossing seals them into totems and summons them on arrival. Soft dependency:
  absent that mod, nothing changes.

**No ServerGuide release, and no Quest-pack content change.** Nothing this batch touches
guidance, triggers or templating; the Quest pack is a dependency bump only (its nine files
are byte-identical to 0.9.1). ServerGuide stays at the already-published **0.15.0**.

**Built zips:**

- `Thunderstore files/Lost_Scrolls_II_0.10.0.zip` (261 KB)
- `Thunderstore files/Lost_Scrolls_II_Quest_0.10.0.zip` (146 KB, 9 guidance files)

**Upload order:** Lost Scrolls II 0.10.0 → Lost Scrolls II Quest 0.10.0. The base package
must exist before the Quest pack, which pins `TaegukGaming-Lost_Scrolls_II-0.10.0` and is
validated at publish time.

> **Version numbering:** `0.10.0`, not `0.9.2`. Thunderstore sorts semver, so 0.10.0 > 0.9.1
> correctly — but read it as *ten*, not *one*, if you ever sort these by hand.

## 0.9.1 — release notes and upload order

Cut **2026-08-25**. A patch release, and a lesson: **0.9.0 was already published**
(2026-08-23 16:05 UTC) when the wrong-key bug was found, and Thunderstore never lets a
version be replaced — so the fix needed a new number even though the change is three
lines. Always check what is actually live before assuming a built zip can still go up.

**What changed since the published 0.9.0:**

- `src/Bounty/BountyBoardPanel.cs` — the Wanted Board said *hold* **[E]** on Haldor.
  ServerGuide moved that key to **Shift + E** in its 0.14.0, so the board was teaching a
  way in that no longer worked, and the board is the only in-game explanation of how to
  begin bounty hunting.
- The Quest pack's `guidance.bounty.yaml` (same correction) and its dependency pins.

**Dependency pins now:**

- Quest → `TaegukGaming-Lost_Scrolls_II-0.9.1`
- Quest → `TaegukGaming-ValheimServerGuide-0.15.0` — **hard floor.** Haldor's commission
  hands out `LS_BountyStart` from a node dialogue choice, and no ServerGuide before
  0.15.0 grants rewards on that kind of choice at all: the conversation plays, the player
  accepts, and no bounty is ever posted, silently. 0.14.0 remains the floor for the
  bounty rewards' `tier:` filter as well.

**Built zips:**

- `Thunderstore files/Lost_Scrolls_II_0.9.1.zip` (256 KB)
- `Thunderstore files/Lost_Scrolls_II_Quest_0.9.1.zip` (146 KB, 9 guidance files)

**Upload order:** Lost Scrolls II 0.9.1 → Lost Scrolls II Quest 0.9.1.
**ServerGuide 0.15.0 is already published** — nothing to do there.

> **The Quest pack was two releases behind on Thunderstore** — stuck at 0.7.0, which
> predates bounty hunting entirely (5 guidance files, ServerGuide pinned at 0.9.0).
> Anyone installing the complete pack was getting no bounty and no wager content at all.
> 0.9.1 is the first Quest release since; check the live listing, not just the local
> folder, when deciding what still needs uploading.

## 0.9.0 — release notes and upload order

Cut **2026-08-23**. Adds the wagered tournaments / staked duel invites, Dead Raiser
field sealing, and the inventory menu bar on top of 0.8.0's bounty hunting.

**ServerGuide 0.15.0 must go up first, and the Quest pack now pins it.** The wager
batch itself needed nothing from ServerGuide — its Discord output goes straight to
`DiscordAnnouncer.AnnounceRaw` (server-wide facts, not per-player rewards) and its one
new trigger, `dvergr_tournament_prize`, rides `MatchesTrigger`'s `default: return true`.
But a bug found on 2026-08-25 changed the picture: **rewards on a node dialogue choice
were silently discarded by every ServerGuide up to 0.14.0**, which is exactly how
Haldor's bounty commission hands out `LS_BountyStart`. On 0.14.0 the conversation plays,
the player accepts, and no first bounty is ever posted — the whole bounty feature is
unreachable through its intended route. Fixed in ServerGuide **0.15.0**, so the Quest
pack's dependency was moved from `-0.14.0` to `-0.15.0`.

**Built zips:**

- `Thunderstore files/Lost_Scrolls_II_0.9.0.zip` (262 KB)
- `Thunderstore files/Lost_Scrolls_II_Quest_0.9.0.zip` (149 KB, 9 guidance files)
- `../Valheim ServerGuide/ValheimServerGuide_0.15.0.zip` (250 KB)

**Upload order:** ServerGuide **0.15.0** → Lost Scrolls II 0.9.0 → Lost Scrolls II
Quest 0.9.0. Thunderstore validates the Quest pack's dependencies at publish time, so
uploading it before ServerGuide 0.15.0 exists will be rejected.

**Also corrected in this cut:** the Wanted Board and `guidance.bounty.yaml` both told
players to **hold [E]** on Haldor. ServerGuide changed that key to **Shift + E** in
0.14.0, so the board was teaching a way in that no longer worked. Both now say Shift + E
— keep them in step with `NpcConversationTrigger` if that key ever moves again.

**Server-side setup for the Valcoin half** — the donations mod's `valcoin_quests.yaml`
needs, all with **`capped: false`**:

- `ls_bounty_t1` … `ls_bounty_t5` (0.8.0)
- `ls_tournament_prize` (0.9.0)

`capped: false` matters: without it the backend clamps a 100-coin purse to its 8-coin
daily allowance. It needs Valheim Donations **5.20.0+** and backend **0.10.0+**. A key
with no matching entry is silently worth nothing (logged as `Unknown quest '<id>'`);
item rewards pay regardless.

## 0.8.0 — release notes and upload order

**ServerGuide 0.14.0 was cut on 2026-08-24**, which unblocks the Quest pack. All three
packages are built and ready.

**Why the Quest pack's ServerGuide dependency is strict (`0.14.0`, not "0.9.0+").** The
bounty reward entries use a `tier:` trigger filter added in 0.14.0. On an **older**
ServerGuide those entries are actively harmful rather than merely inert:
`GuidanceDispatcher.MatchesTrigger` ends in `default: return true`, so an unknown
trigger type still matches and the **`tier:` filter is ignored — every tier's reward
bundle would fire at once**. Hence the hard floor.

**What ServerGuide 0.14.0 contains** (see its own CHANGELOG): two NPC-dialogue fixes
(node choices written as `text:` rendered blank; choice buttons were packed three to a
row and grew enormous — now one full-width row each, styled like vanilla's own dialog
buttons) plus Lost Scrolls II's bounty additions — the `tier:` filter, the two
`dvergr_bounty_*` trigger types, and the `{tier}` / `{tierName}` / `{method}` /
`{bountyBiome}` tokens.

**Upload order:**

1. **ServerGuide 0.14.0** — `Valheim ServerGuide/Thunderstore files/ValheimServerGuide_0.14.0.zip`
2. **Lost Scrolls II 0.8.0** (base) — `Lost_Scrolls_II_0.8.0.zip`
3. **Lost Scrolls II Quest 0.8.0** — `Lost_Scrolls_II_Quest_0.8.0.zip`

Thunderstore validates each package's dependencies at publish time, so a later package
cannot go up before the one it depends on.

**Server-side setup for the Valcoin half:** the donations mod's `valcoin_quests.yaml`
needs ids `ls_bounty_t1` … `ls_bounty_t5`. A key with no matching entry is silently
worth nothing (logged as `Unknown quest '<id>'`); the item rewards pay regardless.

## Building the upload zips

Thunderstore requires `manifest.json`, `icon.png`, and `README.md` at the **root** of the
zip (not nested in a folder). Zip the *contents* of the package folder, not the folder
itself. From PowerShell:

```powershell
# NOTE: the working folder is still "Dvergr Expanded" on disk (the rename to
# "Lost Scrolls II" is pending) — use the path that actually exists.
$ls = "E:\Valheim Modding\Dvergr Expanded\Thunderstore files"
$sg = "E:\Valheim Modding\Valheim ServerGuide\Thunderstore files"

# Build Release FIRST and stage the DLL into the base package — the packaged DLL
# must be the build the version strings describe, not whatever was there before.
dotnet build "E:\Valheim Modding\Dvergr Expanded\src\LostScrollsII.csproj" -c Release
Copy-Item "E:\Valheim Modding\Dvergr Expanded\src\bin\Release\LostScrollsII.dll" `
          "$ls\Lost Scrolls II\LostScrollsII.dll" -Force

# Base mod
Compress-Archive -Path "$ls\Lost Scrolls II\*"        -DestinationPath "$ls\Lost_Scrolls_II_0.9.0.zip"       -Force
# Quest pack (preserves the config/ subtree)
Compress-Archive -Path "$ls\Lost-Scrolls-II-Quest\*"  -DestinationPath "$ls\Lost_Scrolls_II_Quest_0.9.0.zip" -Force
# ServerGuide (only when its DLL changed — see the caution below)
Compress-Archive -Path "$sg\ValheimServerGuide\*"     -DestinationPath "$sg\ValheimServerGuide_0.14.0.zip"   -Force
```

Produced zips (gitignored):
- `Thunderstore files/Lost_Scrolls_II_0.9.0.zip`
- `Thunderstore files/Lost_Scrolls_II_Quest_0.9.0.zip`
- `../Valheim ServerGuide/Thunderstore files/ValheimServerGuide_0.14.0.zip` *(that project
  tracks its zips in git, unlike this one — cut it from there, not here)*

> **Caution — ServerGuide is a separate project with its own release cadence.** At the
> 0.4.0 cut its working tree held **unfinished, unrelated work** (a new `RunePanel`
> display mode) alongside the templating/trigger changes Lost Scrolls II needs, so its
> zip was deliberately **not** built here — packaging it would have shipped someone
> else's half-finished feature. Cut the ServerGuide release from that project when its
> own work is ready, then upload it first. Check `git status` there before zipping.

Verify each zip has `manifest.json`, `icon.png` and `README.md` at the **root** (and,
for the Quest pack, the `config/ValheimServerGuide/` subtree) before uploading.

## Upload order (important)

Thunderstore validates dependencies at publish time, so publish in **dependency
order** — the Quest pack lists both other packages and will fail to validate until
they exist at the listed versions:

1. **ValheimServerGuide 0.9.0** (its own Thunderstore page) — needed for the Quest
   pack's `TaegukGaming-ValheimServerGuide-0.9.0` dependency. **Already published and
   unchanged for 0.7.0** (no ServerGuide code changes in this cut) — only re-upload if
   you've since cut a newer build.
2. **Lost Scrolls II 0.7.0** (base) — needed for `TaegukGaming-Lost_Scrolls_II-0.7.0`.
3. **Lost Scrolls II Quest 0.7.0** (complete pack) — last.

## Version bumping a release

When cutting a new version, keep these in lockstep:
- `src/LostScrollsII.csproj` `<Version>` and `src/Plugin.cs` `PluginVersion`
- both package `manifest.json` `version_number`s
- the Quest pack's `TaegukGaming-Lost_Scrolls_II-<version>` dependency string
- both package `CHANGELOG.md`s
- both package `README.md` version footers *(these were missed at 0.8.0 and still read 0.7.0)*
- re-copy `src/bin/Release/LostScrollsII.dll` into the base package — build **Release**,
  not Debug, and confirm the DLL really carries the new version string before zipping
- refresh the Quest pack's `config/ValheimServerGuide/LostScrollsII/*.yaml` from
  `E:\Valheim Modding\Valheim ServerGuide\examples\LostScrollsII\` (source of truth —
  at the 0.9.0 cut four files there had gained `highlight:` blocks and rune theming the
  packaged copies lacked), then rebuild both zips

If the release also changes **ServerGuide** (new triggers, templating, Discord), cut a
ServerGuide release alongside it and keep *those* in lockstep too — its
`src/ValheimServerGuide.csproj` `<Version>`, `src/Plugin.cs` `PluginVersion`, its
package `manifest.json` + `CHANGELOG.md`, its staged DLL — and bump the Quest pack's
`TaegukGaming-ValheimServerGuide-<version>` dependency. (Watch for drift: at 0.7.1 the
code and the package manifest had disagreed, with the manifest already at 0.8.0.)

Also refresh the Quest pack's `config/ValheimServerGuide/*.yaml` from the source of
truth in `E:\Valheim Modding\Valheim ServerGuide\examples\` so the bundled guidance
matches what was tested.
