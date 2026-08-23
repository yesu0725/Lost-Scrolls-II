# ServerGuide Integration

## Why a Separate Mod Delivers the Story

`E:\Valheim Modding\Valheim ServerGuide` already has a working, server-authoritative system for delivering narrative content: trigger-driven `guidance.yaml` chains with raven/message/chat/rune/intro display modes, multi-step progression, and prerequisite gating. Lost Scrolls II should fire events into that system rather than building a parallel quest-text UI.

This keeps Lost Scrolls II focused on mechanics (recruit, level, chores, duel) and ServerGuide focused on narration — matching how the two mods are already scoped.

## New Trigger Types (implemented, Phase 5)

| Trigger id | Fired when | Subject format | YAML filter field |
|---|---|---|---|
| `dvergr_recruited` | A Communion Rite succeeds | caste name (e.g. `"Rogue"`) | `caste:` (optional — empty matches any) |
| `dvergr_level_up` | An ally levels up | `"Caste:Level"` (e.g. `"Rogue:3"`) | `caste:` and/or `level:` (both optional; `level: 0` = any level) |
| `dvergr_duel_won` | A duel resolves | winner's caste name | `caste:` filters on the **winner** |

Implemented in ServerGuide:
- `src/Config/GuidanceConfig.cs` — added `TriggerSpec.Caste`.
- `src/Triggers/GuidanceDispatcher.cs` — added `MatchesTrigger` cases for all three, plus a `MatchDvergrLevelUp` helper mirroring the existing `MatchSkillLevel` pattern.

`dvergr_duel_won` additionally carries the loser's caste in `TriggerEvent.Extra["loserCaste"]` — not exposed as a YAML filter field, just available for templating if needed later. Since 0.4.0 it also carries `companionName` (the winning companion) and `opponent` (the loser), so a duel-won entry can name both duelists.

## Competitive Trigger Types (Phases A–E + the 0.4.0 UI batch)

The ranking, party-duel and tournament systems raise these. All are fired **on the
relevant player's client** so any ServerGuide reward lands on the right player.

| Trigger id | Fired when | Subject | Templating vars |
|---|---|---|---|
| `dvergr_rank_changed` | A companion climbs into the ladder's top 3 | winner's caste | `{rank}` `{rating}` `{companionName}` `{ownerName}` |
| `dvergr_rank_first` | A companion reaches **#1** (a genuine climb only) | winner's caste | same as above (`{rank}` is always 1) |
| `dvergr_party_duel_won` | A party duel resolves | winning owner name | `{winSize}` `{opponentOwner}` `{mvpCaste}` `{partyName}` `{ownerName}` |
| `dvergr_party_rank_changed` | A party climbs into the party ladder's top 3 | winning owner name | `{rank}` `{rating}` `{partyName}` `{ownerName}` |
| `dvergr_party_rank_first` | A party reaches **#1** | winning owner name | same as above |
| `dvergr_tournament_joined` | A player registers for a tournament | caste name, or `"party"` | — |
| `dvergr_tournament_match` | A round's pairing is announced | caste name, or `"party"` | `{round}` `{opponent}` |
| `dvergr_tournament_won` | The champion is decided | caste name, or `"party"` | `{mode}` `{bracketSize}` |

## Bounty Triggers (Phase D)

Raised on the **hunter's own client** so ServerGuide's rewards land on the right
player. Both were added to the ServerGuide tree on 2026-08-21 (built from **0.13.1**,
version deliberately **not** bumped — that project cuts its own releases), alongside a
new `TriggerSpec.Tier` filter field and four templating vars. A server running an
older ServerGuide simply never matches these entries.

| Trigger id | Fired when | Subject | Templating vars |
|---|---|---|---|
| `dvergr_bounty_resolved` | A bounty target is **killed or communed** | effective tier (`"1"`–`"5"`) | `{tier}` `{tierName}` `{bountyBiome}` `{method}` |
| `dvergr_bounty_valcoin` | The Valcoin payout roll **already succeeded** | effective tier | `{tier}` `{tierName}` |

`tier:` is an optional numeric filter (0/absent = any), which is how one guidance
entry per tier carries its own reward bundle. Two deliberate choices:

- **`{bountyBiome}` is its own token, not `{biome}`.** The existing `{biome}` reports
  the *local player's current* biome, which is only incidentally the bounty's.
- **The chance lives in Lost Scrolls II, not YAML.** ServerGuide has neither a numeric
  rank filter nor a random-chance reward, so the mod rolls it (off the player's best
  duel/party standing) and only fires `dvergr_bounty_valcoin` on success — the entry
  behind it just grants. Its **only** reward is `set_player_key: VC.Q.ls_bounty_t<N>`,
  the sanctioned Valheim Donations bridge; no coin amount ever leaves this mod, so
  nothing here can inflate a payout. See [Bounty-Hunting.md](Bounty-Hunting.md).

`caste:` is an optional filter on the caste-subject triggers; the party ones are
type-only. **Why `*_rank_first` exists rather than a `rank: 1` filter:** ServerGuide's
dispatcher has no numeric filter, so a dedicated trigger is the cleaner way to attach a
"new champion" announcement (see `LeaderboardSync.OnRankEvent` — it fires the `_first`
variant only when `rank == 1`, and the underlying rank event only fires on a real climb,
so it can't spam).

**Reward templating (ServerGuide 0.9.0):** `chat_message` / `discord` **reward**
messages now expand this same variable set, not just `{player_name}` — that fix is what
lets the Discord announcements name the companion, rank, party, etc.
(`RewardDispatcher.Grant` takes an optional token expander supplied by the dispatcher.)

## Wager Triggers, and the Direct Discord Path

The wagered-tournament / duel-invite batch ([Wagers.md](Wagers.md)) added **one**
trigger and **no** ServerGuide changes at all.

| Trigger id | Fired when | Subject | Templating vars |
|---|---|---|---|
| `dvergr_tournament_prize` | A **Valcoin** tournament champion is due their purse | the currency key (`"valcoin"`) | `{mode}` = currency, `{bracketSize}` = the configured purse |

It exists for the same reason `dvergr_bounty_valcoin` does: this mod is not
allowed to price a Valcoin reward. The entry behind it does nothing but
`set_player_key: VC.Q.ls_tournament_prize`, which Valheim Donations reads and
prices from its own `valcoin_quests.yaml`. No amount leaves this mod.

It needs no dispatcher change — `MatchesTrigger`'s `default: return true` handles
an unknown type as "no subject filter", which is exactly right here.

**Everything else in that batch bypasses triggers entirely.** A tournament opening,
a bracket draw, a duel starting, the "who's next" list, an end-of-event summary —
these are **server-wide facts**, not something that happened to one player.
Routing them through a per-player `type: discord` reward would have meant inventing
a trigger per beat and firing it on some arbitrary client, plus a template variable
for every field.

Instead the server posts them directly:

```
ServerGuideBridge.AnnounceDiscord(text)
  -> ValheimServerGuide.Discord.DiscordAnnouncer.AnnounceRaw(text)
```

`AnnounceRaw` is public, server-side, and reads `DiscordWebhookUrl` from the
server's own config — it is what a `discord` reward ultimately calls anyway, after
an RPC hop. `AnnounceDiscord` gates on `IsLoaded` **and** `ZNet.IsServer()` so only
the authoritative instance posts (otherwise every connected client would fire the
same webhook), and is a logged no-op when the webhook is unset.

The rule this establishes: **per-player outcomes go through triggers + guidance;
server-wide announcements go straight to `AnnounceRaw`.**

## Integration Mechanism (implemented, Phase 5)

In-process static calls, not an RPC — both mods load in the same game process, so `LostScrollsII.Integration.ServerGuideBridge` calls `ValheimServerGuide.Triggers.GuidanceDispatcher.Raise()` directly:

- `ServerGuideBridge.IsLoaded` checks `BepInEx.Bootstrap.Chainloader.PluginInfos` for ServerGuide's GUID (`com.valheimserverguide`) before ever touching its types, so Lost Scrolls II works fully without ServerGuide installed (declared as a `SoftDependency` in `Plugin.cs`).
- Each raise method (`RaiseRecruited`, `RaiseLevelUp`, `RaiseDuelWon`) wraps its actual cross-assembly call in a try/catch and only invokes it after `IsLoaded` is true — relying on .NET's lazy per-method JIT so referencing ServerGuide's types never throws on a machine where the assembly is absent.
- `LostScrollsII.csproj` references `ValheimServerGuide.dll` from the shared BepInEx plugins folder (`Private=false` — not bundled into Lost Scrolls II's own output). **Building Lost Scrolls II requires ServerGuide to have been built/deployed at least once on the same machine.**
- Wired call sites: `CommunionService.TryRecruit` → `RaiseRecruited`; `DvergrCompanion.AddXp` (on each level-up) → `RaiseLevelUp`; `DvergrCompanion.AwardDuelWin` (duel subdue in the `Character.Damage` prefix) → `RaiseDuelWon`. All three events now have live callers.

## Authoring Lore Content (current — biome descent)

The authored content lives in **two files** in `E:\Valheim Modding\Valheim ServerGuide\examples\`, both **deployed** to the test profile's `BepInEx/config/ValheimServerGuide/` (ServerGuide auto-merges every `*.yaml` in that folder — no manual merge into `guidance.yaml` needed):

1. **`guidance.lost-scrolls.yaml`** — the **lore** (`category: LostScrolls`), reworked from the old 6-Act chains into a **biome-by-biome descent** (Meadows → Ashlands). Reflective beats fire at distinct world locations as the player progresses; the through-line is a mirror held up to the player (the toiling Dvergr are us), with Scripture woven in verbatim but never cited. Also holds the four per-caste recruit "voices" (raven) and the `ls_guide_recruit_order` tracked recruit-order walkthrough.
2. **`guidance.companions.yaml`** — the **Companion Handbook** (`category: Companions`): gameplay help teaching the command keys, per-caste chores, and adventure use (ships/portals/totems/leveling). See [Testing.md](Testing.md) §10d.
3. **`guidance.bogwitch-rite.yaml`** — the **Bog Witch's Dvergr rites** (`category: Companions`, added Quest pack 0.7.0): two weekly `npc_conversation` → `kill` → `spawn_creature` quest chains that let a player find and free a Dvergr in the Swamp without visiting the Mistlands. Unlike the trigger types above, this file raises **no custom Lost Scrolls II event at all** — it's built entirely from ServerGuide's own stock trigger/reward types, so there is no `ServerGuideBridge` call site for it and no coupling to break if either mod's trigger surface changes. Its only dependency is the **Bog Witch** NPC prefab existing (provided by `ProfMags-TraderOverhaul`, not Lost Scrolls II or ServerGuide) — without it the entries just never match, silently. See [Testing.md](Testing.md) §22 and [Ally-Recruitment.md](Ally-Recruitment.md) → "Finding a Dvergr before the Mistlands".

**Trigger choice — `distance`, not `location_entered`.** The lore beats use ServerGuide's `distance` (proximity-to-named-location) trigger, deliberately: `location_entered` burns a one-shot per-location dedup key for *every* location a player nears regardless of guidance, persisted on the character — so players already on a server would never see the beats. `distance` only burns its key when a matching guidance is in range, so a fresh config fires for veterans and new characters alike. Both log the matched location name at Info level (`[distance] entered range of '<name>'`) for confirmation. Because location dedup is one-shot, the beats are **independent entries** (a chain step waiting on an already-passed location would stall); reading order is carried by directional text in each beat.

**Starting the lore.** Everything begins at `StartTemple` (spawn stones). New characters trigger the opening on spawn; established players are drawn back by `ls_call_to_start` (a `timed` raven nudge that repeats on cooldown until they reach the stones, `stop_when` the opening beat). `intro` display mode is used only at the two ends of the arc (opening + Ashlands finale); everything in between is `rune`/`raven`.

The historical, act-structured drafts ([Quest-Script.md](Quest-Script.md), [Quest-Chains-Draft.yaml](Quest-Chains-Draft.yaml)) are superseded by the above and kept only for history.

## Reference

See ServerGuide's own docs for the underlying system: `.claude/criteria/hearthbound/phase-02-guide-chains.md` (chain YAML shape, state format) and `.claude/criteria/CRIT-06-server-authority.md` (RPC/server-authority pattern).
