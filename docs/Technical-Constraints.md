# Technical Constraints

## Vanilla Assets Only (Hard Rule)

This mod must use **only** vanilla Valheim/Unity assets and programmatic Unity primitives. No custom textures, models, prefabs, animations, sounds, or AssetBundles — mirrors the same constraint already enforced in the sibling ServerGuide project (`CRIT-14-vanilla-assets-only.md`).

| Category | Allowed | Not Allowed |
|---|---|---|
| Creatures | Existing vanilla Dvergr prefabs with new behavior components attached | New models, reskins, custom prefabs |
| UI elements | Programmatic `GameObject` + vanilla Unity components (`Canvas`, `Image`, `CanvasGroup`, `RectTransform`) | Custom UI prefabs, custom sprites/textures |
| Text display | Delegate to ServerGuide's vanilla `TextViewer`/`MessageHud`/`Chat`/Raven systems | Building a separate custom text UI in this mod |
| Visual FX (level-up, Communion success, etc.) | Reused vanilla particle systems / ghost mode / color overlays | Particle systems from custom bundles, custom shaders |
| Arena structure | Composed from existing vanilla building pieces | New structure assets |

### Rules

1. No `AssetBundle.LoadFromFile/Memory` anywhere in the codebase.
2. No custom PNG/JPG/WAV/OGG files in the plugin folder — deploy folder should contain only the DLL.
3. No `Instantiate(prefab)` on non-vanilla prefabs — only instantiate from `ZNetScene`/`PrefabManager`, or build from primitive components.
4. Any new visual effect must be reviewed against this constraint before implementation.

## Prefab Reuse for the Four Dvergr Castes

Recruiting an ally means attaching a `DvergrCompanion` behavior (faction override, leveling state, chore/duel hooks) to the **existing** vanilla Dvergr Rogue / Fire Mage / Ice Mage / Support Mage prefab — never swapping in a different model. Exact internal prefab/creature names need to be confirmed by inspecting `ZNetScene`/`PrefabManager` at runtime or Valheim's `assembly_valheim` prefab list before Phase 2 implementation begins; do not hardcode assumed names without verifying.

## Server Authority

Companion state (faction, level, XP, chore assignment, duel state) must be ZDO-driven so behavior is consistent across all connected clients, not just whichever client is nearest. This mirrors the pattern ServerGuide uses for its own state sync (`CRIT-06-server-authority.md`): the server is the source of truth, clients reflect pushed/synced state rather than computing it independently.

## Dependencies

- **BepInEx + Harmony** — required, standard for Valheim mods.
- **Jötunn** — not required for this mod's mechanics (no custom prefabs/assets to register). Add only if a specific implementation need arises; don't default to it just because ServerGuide uses it.
