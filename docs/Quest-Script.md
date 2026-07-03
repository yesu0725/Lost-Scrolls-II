# Quest Script — Scene by Scene

> **⚠️ SUPERSEDED (2026-07-03) — kept for history.** The shipped lore was reworked from this act structure into a **biome-by-biome descent**. The current authored content is `guidance.lost-scrolls.yaml` (see [Lore.md](Lore.md) → "The Descent and the Mirror" and [ServerGuide-Integration.md](ServerGuide-Integration.md)). This document is the earlier act-structured draft the premise grew from; its beats and themes still inform the descent, but the act framing, per-act triggers, and Act 6 epilogue no longer match what ships.

Expands [Lore.md](Lore.md) into an authorable sequence of story beats, organized by act and vanilla location. This is the source material Phase 5 will translate into actual `guidance.yaml` chains (see [Quest-Chains-Draft.yaml](Quest-Chains-Draft.yaml) for the conceptual chain structure).

Tone note: keep flavor text in the cryptic, runic register vanilla Valheim already uses for its own location/lore text (short, oblique, second-person-adjacent) — not exposition-heavy. Players should piece the story together, not be told it.

Allegory note (for the author, never surfaced in-game): per [Lore.md](Lore.md), this is a **never-named gospel allegory** — corruption = rebellion/sin, Damon = the adversary who reigns over a fallen world, the Sword of Truth = the Word (recovered, not made), Communion = grace (the fallen are freed, they don't free themselves). Nothing is named directly. When writing beats, lean on the *structure*: a world under a shadow, a turning-away that can't self-cure, a truth given from outside, restoration offered rather than forced.

---

## Act 1 — The First Fragment
**Location:** Burial Chambers (Black Forest) and Sunken Crypts (Swamp)

Players find the first scroll fragments here, before any Dvergr or Mistlands content. This act exists purely to seed the premise early, long before a player is ready for Mistlands.

- **Beat 1.1 — Fragment found** (`item_acquired`, a scroll item from a Burial Chambers vault/chest): introduces the idea that *something* was sealed away, in verse, without naming Damon directly yet.
  > "Bound, not broken. The dark does not die — it waits in stone and root."
- **Beat 1.2 — Second fragment** (`item_acquired`, Sunken Crypts): first oblique mention of "the small folk" (Dvergr) and a "rite of waking."
  > "The small folk knew a rite — not of binding, but of waking what sleeps true beneath what sleeps false."
- **Beat 1.3 — Naming the echo** (after both fragments collected, `requires:` gate): names Damon for the only time in the entire script, framed strictly as a past event — and as a *servant* of a shadow older than him, not its source (Lore.md: Damon = the adversary's first servant, the shadow predates any single villain).
  > "Damon's reign broke. But the shadow he served was old before his name — and it did not break. It settled where iron sleeps and embers cool."

This act never repeats Damon's name again after Beat 1.3 — from here on he is "the shadow," "the echo," or unnamed, per Lore.md's "echo, not character" rule. The shadow is deliberately framed as larger and older than Damon, so his defeat never reads as the darkness itself being over.

## Act 2 — The Sword of Truth
**Location:** Mountain or Swamp crypt (exact dungeon TBD during Phase 5 — needs a vanilla dungeon with a suitable "vault" chest mechanic)

- **Beat 2.1 — The sword recovered** (`item_acquired`, Sword of Truth — a repurposed vanilla weapon, not a new asset): the allegorical crux — the Sword = the Word. It is **recovered, not made** by the player, and it was **true before** the fall; the small folk need it but can no longer forge it themselves.
  > "You did not make this, and it did not begin with you. It was true before the small folk, before the shadow, before the first lie."
- **Beat 2.2 — First explanation of Communion** (raven popup, fires on equip): the player's first mechanical tutorial moment, written in-world rather than as a UI tooltip. "Offered, not forced" carries the grace note deliberately.
  > "Hold this where the corrupted kneel, not where they stand. Truth is offered, not forced."

This act ends with the player mechanically able to attempt Communion (Phase 2 implementation) but having not yet met a recruitable Dvergr.

## Act 3 — Into Mistlands
**Location:** Mistlands biome entry, Dvergr camps

- **Beat 3.1 — First sight of the fog** (`location`, Mistlands biome entry, once-per-player): ties the fog directly to the Act 1 "shadow."
  > "The shadow did not vanish. It became weather."
- **Beat 3.2 — First corrupted Dvergr encountered** (`kill` or proximity trigger near a Dvergr camp, fires before recruitment is possible): plants the idea that these aren't just monsters — and that the corruption is a *turning from within*, not a curse laid from without (the allegory's "sin = rebellion" beat).
  > "Not cursed from without. Turned from within. It screams in a tongue too old for screaming."
- **Beat 3.3 — First successful Communion** (`dvergr_recruited`, caste: Rogue — Phase 5 trigger): the payoff beat for Phase 2's MVP mechanic.
  > "The shadow's grip loosens. Something underneath remembers its own name."

Act 3 is the critical path for the Phase 2 MVP — everything above this line can be authored and tested without any Lost Scrolls II code; everything from Beat 3.3 onward requires the recruit system to exist.

> **Note — "the corruption awakens" (mod-side, not ServerGuide).** When an unrecruited Dvergr first turns hostile, Lost Scrolls II itself shows a short line about the corruption within it being roused (*"Something old stirs in it…"*), delivered by the mod, not by a ServerGuide chain. Keep the Beat 3.2 rune text above distinct from it so a player provoking a nearby Dvergr doesn't see two overlapping lines saying the same thing. See [Ally-Recruitment.md](Ally-Recruitment.md) → "The corruption awakens".

## Act 4 — Camps and Strongholds
**Location:** Infested Mines, Mistlands strongholds

> **Narrative order (not a mechanical gate).** The intended arc is Rogue → Fire → Ice → Support, and these act chains are authored in that order. Recruitment itself is **not** gated, so what actually keeps the caste beats in sequence is the `requires:` chain between the act entries (each act requires the previous), not a recruit restriction — a player could free castes out of order, but the *story* beats still unlock in order.

- **Beat 4.1 — Fire Mage recruit** (`dvergr_recruited`, caste: Fire Mage): caste flavor per Lore.md's "forge-keepers."
  > "Its flame was a weapon. Before that, it was a hearth."
- **Beat 4.2 — Ice Mage recruit** (`dvergr_recruited`, caste: Ice Mage): caste flavor per Lore.md's "wardens of preservation."
  > "It kept the cold from killing. The shadow taught it to kill with cold instead."
- **Beat 4.3 — A stronghold's worth of history** (`location`, a Mistlands stronghold, once-per-player): first hint that one caste tended the rite itself, without naming which yet.
  > "Here, once, the rite had keepers. Not warriors. Not smiths. Something else."

## Act 5 — The Rite-Keepers
**Location:** deepest Mistlands content (exact site TBD)

- **Beat 5.1 — Support Mage recruit** (`dvergr_recruited`, caste: Support Mage): the reveal that this caste are the original Communion-rite keepers — explicitly the most lore-dense recruit moment.
  > "It taught the rite to others, once. Now it must be taught its own rite back."
- **Beat 5.2 — How Communion actually works** (raven/rune popup, gated behind Beat 5.1): an in-world explanation of the rite's origin, satisfying the "Support Mage explains the mechanic" hook from Lore.md.
  > "Not a weapon turned inward. A question, asked of what remains. The freed answer for themselves."

## Act 6 — The Shadow Thins (Epilogue / Open Hook)
**Location:** no fixed site — the capstone fires on freeing the last caste, wherever that happens (the Altar of Communion is **not** part of the story; per [Lore.md](Lore.md) it survives only as an optional gameplay reward with no narrative weight).

- **Beat 6.1 — All four castes freed** (gated behind recruiting at least one of each caste): a quiet capstone beat, not a final-boss reveal — deliberately leaves the "what's next" question open per Lore.md. The line carries the allegory's closing note: **not one of the freed rescued itself** (grace, no self-salvation), and the shadow — older than Damon — is thinned, not ended.
  > "Four castes, freed — and not one of them freed itself. The shadow thins, but does not end. Something still stirs where the fog used to be the only thing watching."

This final line is the planted hook for whatever finale threat gets defined later (explicitly deferred in Lore.md) — it should read as foreboding, not as a cliffhanger that promises a specific named villain. Keep it deliberately balanced so a player **cannot tell whether a final confrontation is coming or the story simply rests here**. The actual plan for that finale is held in Lore.md's *author-only roadmap note* and must not leak into any in-game or public-facing text — the ambiguity is intentional and protects a future update.

---

## Authoring Notes for Phase 5

- Beats in Acts 1–2 use only trigger types ServerGuide already supports (`item_acquired`, `location`) — these can be drafted and tested in ServerGuide's `guidance.yaml` immediately, independent of Lost Scrolls II's build progress.
- Beats from Act 3 onward depend on the three new trigger types from [ServerGuide-Integration.md](ServerGuide-Integration.md) (`dvergr_recruited`, etc.) and cannot be wired live until those exist.
- ~~Exact vanilla location/item identifiers are placeholders pending verification.~~ **Resolved** (open item #2, confirmed against Valheim's asset tree at `E:\Valheim Modding\ValheimTemplate`): scroll fragments → `SurtlingCore` (Burial Chambers) and `WitheredBone` (Sunken Crypts); Sword of Truth → `SwordMistwalker`; the "first corrupted Dvergr" beat → `location_entered` on `Mistlands_DvergrTownEntrance*` (was a `kill` trigger — changed to fit the free-don't-kill theme); Act 4.3 stronghold → `location_entered` on `Mistlands_DvergrBossEntrance1` (was an unsupported `location` type). Still unverified in a live session.
- **Act 6 no longer needs a location** (the Altar was dropped from lore) — its capstone fires purely on the "all four castes freed" condition, not on entering any structure.
- Recruitment is **not** order-gated, so the `requires:` chain between the act entries is the **only** thing keeping the caste `dvergr_recruited` beats in narrative order — keep those `requires:` gates intact (an out-of-order recruit still won't fire a later act's beat until its predecessor act completes).
