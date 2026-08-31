# Story & guidance files

The narrative and reward content for Lost Scrolls II. These nine YAML files are read
by the sibling mod **[Valheim ServerGuide](https://thunderstore.io/c/valheim/p/TaegukGaming/ValheimServerGuide/)**,
which is what actually shows the story beats and hands out the rewards — Lost Scrolls II
itself has no quest-text UI.

They used to ship as a separate Thunderstore package (*Lost Scrolls II — Quest*). **That
package is discontinued as of 0.10.0** — these files are now installed by hand from here.
Nothing else changed: they are the same files, byte for byte.

## Install

1. Install **Lost Scrolls II** and **ValheimServerGuide 0.15.0 or newer**.
2. Copy every `guidance.*.yaml` from this folder into:

   ```
   BepInEx/config/ValheimServerGuide/LostScrollsII/
   ```

   Create the `LostScrollsII` folder if it isn't there. ServerGuide loads every `*.yaml`
   under `config/ValheimServerGuide/` recursively, so the subfolder just keeps our files
   from mixing with a server's own guidance — and makes them trivial to remove.
3. On a **dedicated server**, install them on the *server*. The story beats, rewards and
   Discord posts are all driven server-side.

**Upgrading from the old Quest pack?** Delete the package through your mod manager first.
Mod managers don't remove files a package no longer ships, so leftover copies would load
alongside these and every id would be defined twice.

## Version floor

**ValheimServerGuide 0.15.0 is a hard minimum.** Two separate silent failures below it:

- **Below 0.15.0** — Haldor's bounty commission grants its player key from a *node*
  dialogue choice, and no earlier build grants rewards on that kind of choice at all. The
  conversation plays, you accept, and no bounty is ever posted.
- **Below 0.14.0** — the bounty rewards use a `tier:` trigger filter added there. Older
  builds fall through to "matches anything", so **every** tier's reward bundle fires at
  once.

## What each file does

| File | Content |
|---|---|
| `guidance.lost-scrolls.yaml` | The main story — reflective beats fired at real locations, Meadows down to Ashlands |
| `guidance.companions.yaml` | The in-game **Companion Handbook**: command keys, per-caste chores, travelling together |
| `guidance.rankings.yaml` | Duel + party ladder pages, rank milestones, new-#1 announcements |
| `guidance.tournaments.yaml` | Tournament join/pairing/champion messages and the prize bundle |
| `guidance.duels.yaml` | Every duel win → chat + Discord |
| `guidance.wagers.yaml` | Staked tournaments and duel invites, and the Valcoin purse |
| `guidance.bounty.yaml` | Haldor's warden commission — **this is what opens the Wanted Board** |
| `guidance.bounty-rewards.yaml` | Per-tier bounty reward bundles + the Valcoin bridge |
| `guidance.bogwitch-rite.yaml` | The Bog Witch's weekly rite: your first companions without the Mistlands (needs the `ProfMags-TraderOverhaul` Bog Witch; inert without her) |

Rankings, tournaments and bounties still **record** without any of this — the ladders
track, `F6`/`F7`/`F8` still open — but they announce and reward nothing. Bounty hunting
additionally needs `guidance.bounty.yaml` to be reachable at all, since Haldor's
conversation is the only way in.

## Editing them

Plain YAML; edit the reward tables, prizes and text freely for your own server. If you
want to know what the trigger types and template variables mean, ServerGuide's own wiki
documents the format, and
[docs/ServerGuide-Integration.md](../docs/ServerGuide-Integration.md) lists every trigger
this mod fires.
