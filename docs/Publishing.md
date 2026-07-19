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
| `manifest.json` | name `Lost_Scrolls_II`, version `0.4.0` |
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
  manifest.json        name Lost_Scrolls_II_Quest, version 0.4.0
  icon.png             256×256 RGBA PNG (author-supplied)
  README.md
  CHANGELOG.md
  config/
    ValheimServerGuide/
      LostScrollsII/                (own subfolder so our guidance stays separate — 0.4.0)
        guidance.lost-scrolls.yaml  (the biome-descent story)
        guidance.companions.yaml    (the Companion Handbook)
        guidance.rankings.yaml      (ladder pages, rank milestones, new-#1 + Discord) [0.3.0, updated 0.4.0]
        guidance.tournaments.yaml   (tournament announcements + champion prize)       [0.3.0, updated 0.4.0]
        guidance.duels.yaml         (every duel win -> chat + Discord)                [0.4.0]
```

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
- `TaegukGaming-Lost_Scrolls_II-0.4.0` — the base gameplay mod
- `TaegukGaming-ValheimServerGuide-0.9.0` — the story/handbook engine (+ templating & Discord)

**Why `config/ValheimServerGuide/` works out of the box:** ServerGuide loads and
auto-merges every `*.yaml` under `BepInEx/config/ValheimServerGuide/` — recursively,
at any depth, since 0.8.0 (`GuidanceConfigLoader` + `Paths.ConfigPath/PluginName`).
Thunderstore/r2modman routes a package's top-level `config/` folder to
`BepInEx/config/`, so the bundled YAMLs land exactly where ServerGuide reads them —
no manual file copying. They are byte-identical to the in-game-verified copies
(Testing.md §10b–d), sourced from `E:\Valheim Modding\Valheim ServerGuide\examples/`.

This is the complete, single-player-ready experience.

## Building the upload zips

Thunderstore requires `manifest.json`, `icon.png`, and `README.md` at the **root** of the
zip (not nested in a folder). Zip the *contents* of the package folder, not the folder
itself. From PowerShell:

```powershell
# NOTE: the working folder is still "Dvergr Expanded" on disk (the rename to
# "Lost Scrolls II" is pending) — use the path that actually exists.
$ls = "E:\Valheim Modding\Dvergr Expanded\Thunderstore files"
$sg = "E:\Valheim Modding\Valheim ServerGuide\Thunderstore files"

# Base mod
Compress-Archive -Path "$ls\Lost Scrolls II\*"        -DestinationPath "$ls\Lost_Scrolls_II_0.4.0.zip"       -Force
# Quest pack (preserves the config/ subtree)
Compress-Archive -Path "$ls\Lost-Scrolls-II-Quest\*"  -DestinationPath "$ls\Lost_Scrolls_II_Quest_0.4.0.zip" -Force
# ServerGuide (only when its DLL changed — see the caution below)
Compress-Archive -Path "$sg\ValheimServerGuide\*"     -DestinationPath "$sg\ValheimServerGuide_0.9.0.zip"    -Force
```

Produced zips (gitignored):
- `Thunderstore files/Lost_Scrolls_II_0.4.0.zip`
- `Thunderstore files/Lost_Scrolls_II_Quest_0.4.0.zip`
- `../Valheim ServerGuide/Thunderstore files/ValheimServerGuide_0.9.0.zip` *(not built for the
  0.4.0 cut — see below)*

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
   pack's `TaegukGaming-ValheimServerGuide-0.9.0` dependency.
2. **Lost Scrolls II 0.4.0** (base) — needed for `TaegukGaming-Lost_Scrolls_II-0.4.0`.
3. **Lost Scrolls II Quest 0.4.0** (complete pack) — last.

## Version bumping a release

When cutting a new version, keep these in lockstep:
- `src/LostScrollsII.csproj` `<Version>` and `src/Plugin.cs` `PluginVersion`
- both package `manifest.json` `version_number`s
- the Quest pack's `TaegukGaming-Lost_Scrolls_II-<version>` dependency string
- both package `CHANGELOG.md`s
- re-copy `src/bin/Release/LostScrollsII.dll` into the base package, then rebuild both zips

If the release also changes **ServerGuide** (new triggers, templating, Discord), cut a
ServerGuide release alongside it and keep *those* in lockstep too — its
`src/ValheimServerGuide.csproj` `<Version>`, `src/Plugin.cs` `PluginVersion`, its
package `manifest.json` + `CHANGELOG.md`, its staged DLL — and bump the Quest pack's
`TaegukGaming-ValheimServerGuide-<version>` dependency. (Watch for drift: at 0.7.1 the
code and the package manifest had disagreed, with the manifest already at 0.8.0.)

Also refresh the Quest pack's `config/ValheimServerGuide/*.yaml` from the source of
truth in `E:\Valheim Modding\Valheim ServerGuide\examples\` so the bundled guidance
matches what was tested.
