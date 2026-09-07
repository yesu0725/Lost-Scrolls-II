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

**One package** ships from `Thunderstore files/` as of 0.10.0. The second (the Quest
pack) is discontinued — see below.

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

### 2. Complete pack — `Lost-Scrolls-II-Quest`  *(DISCONTINUED)*

**Retired at 0.10.0 (2026-08-31) at the owner's request. Do not cut a new one.** Its last
release was **0.9.1**, which stays installable on Thunderstore.

All it ever did was bundle the nine guidance YAMLs under
`config/ValheimServerGuide/LostScrollsII/` and pull the base mod + ServerGuide in as
dependencies — it never shipped a DLL. Those files were byte-identical to
`E:\Valheim Modding\Valheim ServerGuide\examples\LostScrollsII\` (verified before
removal), and they now live in this repo at **[`guidance/`](../guidance/)** with a README
carrying the install steps and the ServerGuide version floor. Players copy them into
`BepInEx/config/ValheimServerGuide/LostScrollsII/` by hand.

The package folder is gone from `Thunderstore files/`; recover it from git history
(before commit `35fa0cb`'s successor) if it is ever needed. Its historical zips
`Lost_Scrolls_II_Quest_0.3.0` … `_0.9.1.zip` are left in place as build artifacts of
already-published releases — **the never-published `_0.10.0.zip` was deleted**, since the
only thing that could go wrong here is someone uploading it.

**Considered and rejected:** folding the guidance into the base package's own `config/`
subtree (Thunderstore routes a package's top-level `config/` to `BepInEx/config/`, so it
would work and players would get the story automatically). The owner chose repo-only, so
the base package stays gameplay-only exactly as it always was.

**Consequences to keep in mind:**

- The base mod's README and the wiki now link
  `https://github.com/yesu0725/Lost-Scrolls-II/tree/main/guidance` wherever they used to
  say "use the Quest pack". If that folder is ever moved or renamed, those links break in
  a published package and on the wiki.
- Anyone still running Quest 0.9.1 is fine — its bundled files are the same ones. But a
  player who installs the manual files **without** uninstalling the package first ends up
  with two copies of every id.
- The story content is no longer versioned alongside a package. `guidance/` is the
  distribution point now; keep it in step with the ServerGuide `examples/` source of truth.

## 0.11.0 — release notes and upload order  *(current)*

Cut **2026-09-05**. A chore-system release. **Everything in it was verified in a live
session before the cut** ([Testing.md](Testing.md) §8g–§8p all passed), across roughly a
dozen report-and-fix rounds — that is where most of the value is, and the test sections
are worth reading as a record of what was actually exercised.

**What changed since 0.10.0** (all in the base mod):

- **A chore is a patch of ground, not a station.** One worker tends every job of its kind
  within `Chores/ChoreWorkRadius` (**20 m**), walking between them. Cooking and brewing
  merged into one **Provisioning** domain. Patches are **shared** — press the key again to
  add another ally; recall is done on the companion.
- **Every chore files its output in a chest** (`ChoreStorage`), preferring one that already
  holds the item. What counts as a chest is a structural test, not a prefab list — and the
  exclusions carry the weight (Obliterator, gravestones, ship holds, companion packs,
  dungeon chests, other players' private chests, other players' wards).
- **Husbandry moved to the Rogue and now culls**, and **hauling came back as the other half
  of the same domain** — one worker does both across one patch.
- **Farming was rebuilt.** Started by putting a **Cultivator in the ally's pack**; plants and
  harvests in **level-scaled blocks** on a world-aligned grid; one crop per field while the
  seed lasts, then the next crop; seed from the pack or any chest in range; vanilla's own
  `Plant.HaveGrowSpace` clearance test, so it never sows into rocks or wild growth.
- **Companions open doors** (`Door.Open`, never `Door.Interact` — see
  [Ally-Chores.md](Ally-Chores.md) for why that distinction is load-bearing on a server).
- **Map pins outlive the companion's zone**, and persist per world+player under
  `BepInEx/config/LostScrollsII/`.
- **Stance persists across a relog** (ZDO `DE_Stance`), and starting a chore ends it.
- **Renaming is an explicit mode** with a Rename/Save button that kills every key while
  it is armed — including other mods' hotkeys, which read `UnityEngine.Input` directly.

**No ServerGuide release.** The guidance **content** changed (the four caste chore pages
were rewritten and version-bumped), but it uses only existing triggers and templating, so
ServerGuide stays at the already-published **0.15.0**. Sync `guidance/` from the source of
truth and push — that folder is the distribution point.

> The dedicated server's copy of `guidance.companions.yaml` carries two deliberate local
> edits (`mode: raven` — `rune` on two entries). They were left alone; don't blind-copy the
> repo file over it.

**Built zip:**

- `Thunderstore files/Lost_Scrolls_II_0.11.0.zip` — one package, verified to hold
  `manifest.json`, `icon.png`, `README.md` and a `0.11.0.0` DLL at the **root**

**Upload:** Lost Scrolls II 0.11.0. Nothing else.

**State at the cut (2026-09-05):**

| | |
|---|---|
| `Lost-Scrolls-II` `main` | `afc62b8` — pushed |
| `Valheim-ServerGuide` `master` | `c76ff78` — pushed (the `examples/LostScrollsII/` guidance source of truth) |
| `Lost-Scrolls-II.wiki` `master` | `0aa96a9` — pushed, every page byte-identical to `wiki/` |
| Thunderstore | **not uploaded yet** — the zip is built and waiting |

The wiki sync caught three stale lines in `Companion-Chores` that this cycle's own
changes had left behind (recall documented on the station, husbandry still the Support
Mage's, the seed source stated twice). Fixed in `wiki/` first, then mirrored — that
folder is the source of truth, so never edit the GitHub Wiki directly.

> Five of the fourteen wiki pages showed as differing on a plain `diff` and were
> **identical in content** — the backing repo stores CRLF and `wiki/` stores LF.
> Compare with `diff <(tr -d '' < a) <(tr -d '' < b)` before assuming a page is
> stale, and write CRLF back so the pushed diff stays readable.

## 0.10.0 — release notes and upload order

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

**No ServerGuide release.** Nothing this batch touches guidance, triggers or templating,
so ServerGuide stays at the already-published **0.15.0**.

**The Quest pack is discontinued in this release** (owner's decision, 2026-08-31). Its
0.10.0 zip was built and then deleted unpublished; the guidance it carried moved to
[`guidance/`](../guidance/) in this repo. See the retired section above for the full
consequences.

**Built zip:**

- `Thunderstore files/Lost_Scrolls_II_0.10.0.zip` (261 KB) — one package now

**Upload:** Lost Scrolls II 0.10.0. Nothing else. (The zip was rebuilt after the Quest
removal, because the package README ships inside it and had to be repointed at the new
`guidance/` folder.)

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

> **Confirmed 2026-08-31: both 0.9.1 packages were uploaded.** Quest 0.9.1 is the last
> Quest release there will ever be — the pack was discontinued at 0.10.0.

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

# Base mod — the only package now (the Quest pack is discontinued)
Compress-Archive -Path "$ls\Lost Scrolls II\*"    -DestinationPath "$ls\Lost_Scrolls_II_0.11.0.zip"      -Force
# ServerGuide (only when its DLL changed — see the caution below)
Compress-Archive -Path "$sg\ValheimServerGuide\*" -DestinationPath "$sg\ValheimServerGuide_0.15.0.zip"   -Force
```

Produced zips (gitignored):
- `Thunderstore files/Lost_Scrolls_II_0.11.0.zip`
- `../Valheim ServerGuide/Thunderstore files/ValheimServerGuide_0.15.0.zip` *(that project
  tracks its zips in git, unlike this one — cut it from there, not here)*

> **Caution — ServerGuide is a separate project with its own release cadence.** At the
> 0.4.0 cut its working tree held **unfinished, unrelated work** (a new `RunePanel`
> display mode) alongside the templating/trigger changes Lost Scrolls II needs, so its
> zip was deliberately **not** built here — packaging it would have shipped someone
> else's half-finished feature. Cut the ServerGuide release from that project when its
> own work is ready, then upload it first. Check `git status` there before zipping.

Verify the zip has `manifest.json`, `icon.png`, `README.md` and `LostScrollsII.dll` at the
**root** before uploading.

## Upload order

Only one Lost Scrolls II package remains, so there is nothing to order among them — but
**ServerGuide still goes first whenever it changed**, because it is a genuine runtime
dependency of the ranking, tournament and bounty features. It has not changed since
0.15.0.

1. **ValheimServerGuide** (its own Thunderstore page) — only when you have cut a new one.
2. **Lost Scrolls II** (base) — the only package this repo publishes.

The guidance YAMLs are not uploaded anywhere; they ship in the GitHub repo at
[`guidance/`](../guidance/).

## Version bumping a release

When cutting a new version, keep these in lockstep:
- `src/LostScrollsII.csproj` `<Version>` and `src/Plugin.cs` `PluginVersion`
- the package `manifest.json` `version_number`
- the package `CHANGELOG.md`
- the package `README.md` version footer *(this was missed at 0.8.0 and still read 0.7.0)*
- re-copy `src/bin/Release/LostScrollsII.dll` into the base package — build **Release**,
  not Debug, hash it against `bin/Release`, and confirm the DLL really carries the new
  version string before zipping
- if any **player-facing README/wiki text** changed, rebuild the zip: the package README
  ships *inside* it, so editing it after zipping leaves a stale archive

If the release also changes **ServerGuide** (new triggers, templating, Discord), cut a
ServerGuide release alongside it and keep *those* in lockstep too — its
`src/ValheimServerGuide.csproj` `<Version>`, `src/Plugin.cs` `PluginVersion`, its
package `manifest.json` + `CHANGELOG.md`, its staged DLL. (Watch for drift: at 0.7.1 the
code and the package manifest had disagreed, with the manifest already at 0.8.0.)

If the release changes the **guidance YAMLs**, sync `guidance/` from the source of truth in
`E:\Valheim Modding\Valheim ServerGuide\examples\LostScrollsII\` and push — that folder
is now the distribution point, so a stale copy there is a stale copy for every player.
