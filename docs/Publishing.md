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
| `manifest.json` | name `Lost_Scrolls_II`, version `0.2.0` |
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
  manifest.json        name Lost_Scrolls_II_Quest, version 0.2.0
  icon.png             256×256 RGBA PNG (author-supplied)
  README.md
  CHANGELOG.md
  config/
    ValheimServerGuide/
      guidance.lost-scrolls.yaml    (the biome-descent story)
      guidance.companions.yaml      (the Companion Handbook)
```

**Dependencies (all installed automatically):**

- `denikson-BepInExPack_Valheim-5.4.2333`
- `TaegukGaming-Lost_Scrolls_II-0.2.0` — the base gameplay mod
- `TaegukGaming-ValheimServerGuide-0.7.1` — the story/handbook engine

**Why `config/ValheimServerGuide/` works out of the box:** ServerGuide loads and
auto-merges every `*.yaml` in `BepInEx/config/ValheimServerGuide/`
(`GuidanceConfigLoader` + `Paths.ConfigPath/PluginName`). Thunderstore/r2modman routes a
package's top-level `config/` folder to `BepInEx/config/`, so the two bundled YAMLs land
exactly where ServerGuide reads them — no manual file copying. The two files are
byte-identical to the in-game-verified copies (Testing.md §10b–d), sourced from
`E:\Valheim Modding\Valheim ServerGuide\examples/`.

This is the complete, single-player-ready experience.

## Building the upload zips

Thunderstore requires `manifest.json`, `icon.png`, and `README.md` at the **root** of the
zip (not nested in a folder). Zip the *contents* of the package folder, not the folder
itself. From PowerShell:

```powershell
# Base mod
$src = "E:\Valheim Modding\Lost Scrolls II\Thunderstore files\Lost Scrolls II"
Compress-Archive -Path "$src\*" -DestinationPath "$src\..\Lost_Scrolls_II_0.2.0.zip" -Force

# Quest pack (preserves the config/ subtree)
$src = "E:\Valheim Modding\Lost Scrolls II\Thunderstore files\Lost-Scrolls-II-Quest"
Compress-Archive -Path "$src\*" -DestinationPath "$src\..\Lost_Scrolls_II_Quest_0.2.0.zip" -Force
```

Produced zips (gitignored):
- `Thunderstore files/Lost_Scrolls_II_0.2.0.zip`
- `Thunderstore files/Lost_Scrolls_II_Quest_0.2.0.zip`

## Upload order (important)

Thunderstore validates dependencies at publish time, so **publish the base
`Lost Scrolls II` package first**. The Quest pack lists `TaegukGaming-Lost_Scrolls_II-0.2.0`
as a dependency, which must already exist on Thunderstore for the Quest upload to
validate.

## Version bumping a release

When cutting a new version, keep these in lockstep:
- `src/LostScrollsII.csproj` `<Version>` and `src/Plugin.cs` `PluginVersion`
- both package `manifest.json` `version_number`s
- the Quest pack's `TaegukGaming-Lost_Scrolls_II-<version>` dependency string
- both package `CHANGELOG.md`s
- re-copy `src/bin/Release/LostScrollsII.dll` into the base package, then rebuild both zips
