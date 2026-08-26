using System.Collections.Generic;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using LostScrollsII.Companions;
using HarmonyLib;
using UnityEngine;

namespace LostScrollsII
{
    [BepInPlugin(PluginGuid, PluginName, PluginVersion)]
    // Narrative delivery only (see docs/ServerGuide-Integration.md) — soft dependency
    // so this mod still loads and functions fully without ServerGuide installed.
    [BepInDependency("com.valheimserverguide", BepInDependency.DependencyFlags.SoftDependency)]
    // Bounty hunting (docs/Bounty-Hunting.md) only runs where BOTH of these are
    // present alongside ServerGuide. Declared soft so they're loaded before us —
    // BountyFeatureGate probes Chainloader for them — while leaving this mod fully
    // functional without either.
    [BepInDependency("com.taeguk.BiomeLords", BepInDependency.DependencyFlags.SoftDependency)]
    [BepInDependency("com.taeguk.valheimdonations", BepInDependency.DependencyFlags.SoftDependency)]
    public class Plugin : BaseUnityPlugin
    {
        public const string PluginGuid = "com.lostscrollsii";
        public const string PluginName = "Lost Scrolls II";
        public const string PluginVersion = "0.9.1";

        public static Plugin Instance { get; private set; }
        public static ManualLogSource Log { get; private set; }

        // Feed key: on a recruited companion this feeds it a health mead from the
        // player's inventory (see docs/Ally-Commands.md). Recruiting itself no
        // longer lives on this key — the Communion Rite is now channeled by holding
        // the vanilla Block button (see CommunionRite / docs/Ally-Recruitment.md).
        // The config id stays "CommunionKey" for back-compat with existing configs.
        public static ConfigEntry<KeyCode> CommunionKey { get; private set; }

        // The Communion Rite is a CHANNELED struggle, not an instant keypress
        // (see CommunionRite / docs/Ally-Recruitment.md). Hold the key for
        // CommunionChannelSeconds while staying within CommunionMaxDistance; if
        // CommunionBreakOnDamage is on, taking a hit also breaks it.
        public static ConfigEntry<float> CommunionChannelSeconds { get; private set; }
        public static ConfigEntry<float> CommunionMaxDistance { get; private set; }
        public static ConfigEntry<bool> CommunionBreakOnDamage { get; private set; }

        // Field sealing with a Dead Raiser (see SealingRite / docs/Companion-Totems.md).
        // Hold Block on your OWN Follow-stance companion with a StaffSkeleton equipped
        // and a Wisp in the pack; the channel shortens as Blood Magic rises.
        public static ConfigEntry<bool> StaffSealEnabled { get; private set; }
        public static ConfigEntry<int> SealMinBloodMagic { get; private set; }
        public static ConfigEntry<int> SealFullSpeedBloodMagic { get; private set; }
        public static ConfigEntry<float> SealChannelMaxSeconds { get; private set; }
        public static ConfigEntry<float> SealChannelMinSeconds { get; private set; }
        public static ConfigEntry<float> SealMaxDistance { get; private set; }

        // Phase 4: pressed while hovering a Smelter to assign/unassign the nearest
        // recruited companion as its chore worker. See docs/Ally-Chores.md.
        public static ConfigEntry<KeyCode> ChoreAssignKey { get; private set; }
        public static ConfigEntry<float> ChoreAssignRadius { get; private set; }

        // Phase 6: press while hovering YOUR OWN companion to toggle its duel
        // mode. A duel-mode companion fights other players' duel-mode companions.
        // See docs/Duel-Arena.md.
        public static ConfigEntry<KeyCode> DuelSelectKey { get; private set; }

        // Phase C: press while hovering YOUR OWN companion to toggle a PARTY duel —
        // gathers your nearby Follow-stance companions into a team that fights
        // another player's party-duel team. See docs/Party-Duels.md.
        public static ConfigEntry<KeyCode> PartyDuelKey { get; private set; }
        public static ConfigEntry<int> MaxPartySize { get; private set; }

        // Feature add: toggle a hovered companion's stance (Follow <-> Guard).
        public static ConfigEntry<KeyCode> StanceCycleKey { get; private set; }

        // Feature add: open a hovered companion's inventory (chest-like panel that
        // also carries a rename field — req 3). See CompanionInventory /
        // CompanionInventoryGui and docs/Ally-Inventory.md.
        public static ConfigEntry<KeyCode> InventoryKey { get; private set; }

        // Feature add: show a live minimap pin at each of the local player's own
        // companions. Client-side, so other players never see your companions.
        public static ConfigEntry<bool> ShowMapPins { get; private set; }
        // Companion map pins use the vanilla PLAYER icon, tinted + scaled so they
        // read as "your allies" but stay distinct from your own player marker.
        public static ConfigEntry<string> CompanionPinColor { get; private set; }
        public static ConfigEntry<float> CompanionPinScale { get; private set; }
        // A persistent death marker (skull) is dropped on the owner's map when a
        // companion dies, labelled with its name.
        public static ConfigEntry<bool> ShowDeathMarker { get; private set; }

        // Position of the shared chest/storage panel (vanilla chests and the
        // companion pack both use it) — see ContainerPanelPositioner. The panel can
        // be dragged anywhere on screen; ContainerPanelOffset remembers where. Both
        // are ignored when BiomeLords is loaded (it owns the same panel).
        public static ConfigEntry<bool> MoveContainerPanel { get; private set; }
        public static ConfigEntry<string> ContainerPanelOffset { get; private set; }

        // Where the row of menu buttons sits on the inventory screen (see
        // InventoryMenuBar). Supersedes Bounty/InventoryButtonOffset, which is
        // still honoured when it has been changed from its default.
        public static ConfigEntry<string> MenuBarOffset { get; private set; }

        // Ranking (docs/Ranking.md). K-factor controls Elo volatility; the per-pair
        // cooldown blocks two players padding each other's rating with repeat wins.
        public static ConfigEntry<int> RankingKFactor { get; private set; }
        public static ConfigEntry<float> RankingPairCooldown { get; private set; }
        public static ConfigEntry<bool> ShowRankOnNameTag { get; private set; }

        // Opens the read-only ranking board (duel + party ladders) — see RankingBoard.
        public static ConfigEntry<KeyCode> RankingUiKey { get; private set; }

        // Opens the tournament board (status/registration/bracket) — see TournamentBoard.
        public static ConfigEntry<KeyCode> TournamentUiKey { get; private set; }

        // 0 = no restriction. When >0, a 1v1 entrant's companion level must match
        // exactly (docs/Tournaments.md — level-gated events).
        public static ConfigEntry<int> RequiredEntrantLevel { get; private set; }

        // Hard cap on tournament entrants. A `size` of 0 passed to `de_tournament
        // start` now means "use this cap" rather than "uncapped" (an uncapped bracket
        // no longer makes sense once escrow + auto-summon is in play).
        public static ConfigEntry<int> MaxEntrants { get; private set; }

        // Wagered events (docs/Wagers.md): player-started tournaments and duel
        // invites staked in vanilla Coins or in Valcoins. Valcoin stakes need the
        // Valheim Donations wallet API; Coin stakes work with this mod alone.
        public static ConfigEntry<bool> WagersEnabled { get; private set; }
        public static ConfigEntry<int> TournamentValcoinFee { get; private set; }
        public static ConfigEntry<int> TournamentValcoinPrize { get; private set; }
        public static ConfigEntry<int> TournamentCoinFee { get; private set; }
        public static ConfigEntry<int> TournamentCoinPrize { get; private set; }
        public static ConfigEntry<int> DuelValcoinStake { get; private set; }
        public static ConfigEntry<int> DuelCoinStake { get; private set; }
        public static ConfigEntry<int> WageredBracketSize { get; private set; }
        public static ConfigEntry<float> WageredRegistrationMinutes { get; private set; }

        // Bounty hunting (docs/Bounty-Hunting.md). A server-only feature that also
        // needs BiomeLords + ServerGuide + Valheim Donations all loaded; this is the
        // admin's manual off-switch on top of that gate, never a way to force it on.
        public static ConfigEntry<bool> BountyEnabled { get; private set; }

        // Where bounty targets may be placed (docs/Bounty-Hunting.md, Phase B). A
        // bounty must never sit underwater, on a shoreline or on an islet, so a
        // candidate is validated against rings of terrain out to BountyLandCheckRadius
        // rather than at a single point.
        public static ConfigEntry<float> BountySearchMinRadius { get; private set; }
        public static ConfigEntry<float> BountySearchMaxRadius { get; private set; }
        public static ConfigEntry<float> BountyLandCheckRadius { get; private set; }
        public static ConfigEntry<float> BountyWaterMargin { get; private set; }
        public static ConfigEntry<float> BountyMaxHeightVariance { get; private set; }
        public static ConfigEntry<int> BountySampleAttempts { get; private set; }

        // Bounty difficulty (docs/Bounty-Hunting.md, Phase C). Our own tier curve —
        // BiomeLords has no callable scaling API and no minion scaling at all, so the
        // numbers live here where a server admin can tune them.
        public static ConfigEntry<int> BountyTierBlackForest { get; private set; }
        public static ConfigEntry<int> BountyTierSwamp { get; private set; }
        public static ConfigEntry<int> BountyTierMountain { get; private set; }
        public static ConfigEntry<int> BountyTierPlains { get; private set; }
        public static ConfigEntry<float> BountyHealthBase { get; private set; }
        public static ConfigEntry<float> BountyHealthGrowth { get; private set; }
        public static ConfigEntry<int> BountyMaxStarLevel { get; private set; }
        public static ConfigEntry<int> BountyMinionsBase { get; private set; }
        public static ConfigEntry<int> BountyMinionsPerTier { get; private set; }
        public static ConfigEntry<int> BountyMaxMinions { get; private set; }
        public static ConfigEntry<float> BountyMinionRingRadius { get; private set; }
        public static ConfigEntry<float> BountyAlertRangeMultiplier { get; private set; }
        public static ConfigEntry<float> BountyRoamRadius { get; private set; }

        // Bounty rewards (docs/Bounty-Hunting.md, Phase D). The ITEMS are authored in
        // ServerGuide guidance, not here — these only control the effective reward
        // tier and the Valcoin payout CHANCE. Valcoin is reward-only: nothing in this
        // feature ever spends it, per Valheim Donations' "no selling power" guardrail.
        public static ConfigEntry<int> BountyMaxTierBonus { get; private set; }
        public static ConfigEntry<float> BountyValcoinBaseChance { get; private set; }
        public static ConfigEntry<float> BountyValcoinChancePerTier { get; private set; }
        public static ConfigEntry<float> BountyValcoinRankBonus { get; private set; }
        public static ConfigEntry<int> BountyValcoinRankDepth { get; private set; }
        public static ConfigEntry<float> BountyValcoinMaxChance { get; private set; }

        // Bounty leaderboard (docs/Bounty-Hunting.md, Phase E).
        public static ConfigEntry<int> BountyPointsPerTier { get; private set; }
        public static ConfigEntry<int> BountyLeaderboardBonusRank { get; private set; }

        // The Wanted Board (docs/Bounty-Hunting.md, Phase F).
        public static ConfigEntry<KeyCode> BountyUiKey { get; private set; }
        public static ConfigEntry<int> BountyMaxBoardEntries { get; private set; }
        public static ConfigEntry<int> BountyMaxActivePerPlayer { get; private set; }
        public static ConfigEntry<float> BountyArrivalRadius { get; private set; }
        public static ConfigEntry<string> BountyButtonOffset { get; private set; }

        // Board rotation + the rank-gated elite tier (docs/Bounty-Hunting.md, Phase H).
        public static ConfigEntry<float> BountyRefreshHours { get; private set; }
        public static ConfigEntry<float> BountyEliteChance { get; private set; }
        public static ConfigEntry<int> BountyEliteRankTopN { get; private set; }

        private Harmony _harmony;

        private void Awake()
        {
            Instance = this;
            Log = Logger;

            CommunionKey = Config.Bind(
                "Recruitment",
                "CommunionKey",
                KeyCode.G,
                "Key pressed while hovering a recruited companion to feed it a health mead. (The Communion Rite that recruits a subdued Dvergr is now channeled by holding the Block button, not this key.)");

            CommunionChannelSeconds = Config.Bind(
                "Recruitment",
                "CommunionChannelSeconds",
                5f,
                "How long you must hold the Communion key to complete the rite. The corruption fights back the whole time — release the key, stray too far, or take a hit and the rite breaks.");

            CommunionMaxDistance = Config.Bind(
                "Recruitment",
                "CommunionMaxDistance",
                4f,
                "How far you may drift from the Dvergr mid-rite before the connection snaps and the rite fails.");

            CommunionBreakOnDamage = Config.Bind(
                "Recruitment",
                "CommunionBreakOnDamage",
                true,
                "If true, taking damage while channeling the Communion Rite breaks it (the shadow reclaims the Dvergr). Turn off for a more forgiving rite.");

            StaffSealEnabled = Config.Bind(
                "Recruitment",
                "StaffSealEnabled",
                true,
                "Allow sealing a companion into a Communion Totem in the field with an equipped Dead Raiser (StaffSkeleton) + a Wisp, by holding Block on your own Follow-stance ally. The Incinerator ritual is unaffected either way.");

            SealMinBloodMagic = Config.Bind(
                "Recruitment",
                "SealMinBloodMagic",
                20,
                "Minimum Blood Magic skill needed to seal a companion with a Dead Raiser. Below this the rite cannot be started.");

            SealFullSpeedBloodMagic = Config.Bind(
                "Recruitment",
                "SealFullSpeedBloodMagic",
                100,
                "The Blood Magic skill at which the sealing channel reaches its fastest (SealChannelMinSeconds). Between SealMinBloodMagic and this value the channel time scales smoothly.");

            SealChannelMaxSeconds = Config.Bind(
                "Recruitment",
                "SealChannelMaxSeconds",
                5f,
                "How long the Dead Raiser sealing channel takes at the MINIMUM Blood Magic skill (the slowest it ever is).");

            SealChannelMinSeconds = Config.Bind(
                "Recruitment",
                "SealChannelMinSeconds",
                2f,
                "How long the Dead Raiser sealing channel takes at SealFullSpeedBloodMagic (the fastest it ever is).");

            SealMaxDistance = Config.Bind(
                "Recruitment",
                "SealMaxDistance",
                4f,
                "How far you may drift from the companion mid-sealing before the binding fails.");

            ChoreAssignKey = Config.Bind(
                "Chores",
                "ChoreAssignKey",
                KeyCode.H,
                "Key pressed while hovering a workstation (Smelter-family), a ripe crop, or a tamed animal to assign/unassign the nearest matching-caste companion to that chore.");

            ChoreAssignRadius = Config.Bind(
                "Chores",
                "ChoreAssignRadius",
                10f,
                "Max distance from the player to look for a recruited companion to assign to a chore.");

            DuelSelectKey = Config.Bind(
                "Duels",
                "DuelSelectKey",
                KeyCode.J,
                "Press while hovering your own recruited companion to toggle its duel mode. A duel-mode companion fights other players' duel-mode companions and ignores everyone else.");

            PartyDuelKey = Config.Bind(
                "Duels",
                "PartyDuelKey",
                KeyCode.K,
                "Press while hovering your own recruited companion to toggle a party duel: your nearby Follow-stance companions form a team that fights another player's party-duel team.");

            MaxPartySize = Config.Bind(
                "Duels",
                "MaxPartySize",
                4,
                "Maximum number of your companions that join a party duel when you toggle it on.");

            StanceCycleKey = Config.Bind(
                "Companions",
                "StanceCycleKey",
                KeyCode.E,
                "Press while hovering your companion to cycle its stance: Follow -> Guard -> Standby.");

            InventoryKey = Config.Bind(
                "Companions",
                "InventoryKey",
                KeyCode.Y,
                "Press while hovering your companion to open its inventory (a chest-like panel that also lets you rename it).");

            ShowMapPins = Config.Bind(
                "Companions",
                "ShowMapPins",
                true,
                "Show a live minimap pin at each of your own recruited companions. Pins are client-side — other players never see your companions, and you never see theirs.");

            CompanionPinColor = Config.Bind(
                "Companions",
                "CompanionPinColor",
                "FFB84D",
                "Hex tint (RRGGBB) for companion map pins, which use the player icon in a smaller size. Distinguishes your allies from your own marker.");

            CompanionPinScale = Config.Bind(
                "Companions",
                "CompanionPinScale",
                0.7f,
                "Size multiplier for companion map pins (1 = full player-icon size). Smaller keeps them from crowding your own marker.");

            ShowDeathMarker = Config.Bind(
                "Companions",
                "ShowDeathMarker",
                true,
                "Drop a persistent death marker (skull) on your map, labelled with the companion's name, when one of your companions dies.");

            MoveContainerPanel = Config.Bind(
                "Interface",
                "MoveContainerPanel",
                true,
                "Let the chest/storage UI be repositioned: it opens at ContainerPanelOffset, and you can drag it anywhere on screen by grabbing an empty part of the panel. Set to false to leave the panel exactly where the game puts it. Automatically ignored when BiomeLords is installed (it moves the same panel itself).");

            ContainerPanelOffset = Config.Bind(
                "Interface",
                "ContainerPanelOffset",
                "auto",
                "Where the chest/storage UI sits, as an \"x,y\" pixel offset from the game's own position (+x right, +y up). \"auto\" places it two inventory rows below, so extra rows added by other mods aren't hidden behind it. Dragging the panel writes the new value here; set it back to \"auto\" to restore the default position.");

            MenuBarOffset = Config.Bind(
                "Interface",
                "MenuBarOffset",
                "0,0",
                "Nudge the row of menu buttons at the top of the inventory screen by \"x,y\" pixels (+x right, +y up). The row holds Rankings, Tournaments and Bounty Board; the matching function keys keep working either way.");

            RankingKFactor = Config.Bind(
                "Ranking",
                "EloKFactor",
                32,
                "Elo K-factor for the duel ladder — higher means bigger rating swings per match.");

            RankingPairCooldown = Config.Bind(
                "Ranking",
                "PairCooldownSeconds",
                300f,
                "Seconds before the same two companions can move each other's rating again (W/L still count). Anti friend-farming.");

            ShowRankOnNameTag = Config.Bind(
                "Ranking",
                "ShowRankOnNameTag",
                true,
                "Append the ladder rank (e.g. #3) to a companion's floating name when it has one.");

            RankingUiKey = Config.Bind(
                "Ranking",
                "RankingUiKey",
                KeyCode.F6,
                "Opens the ranking board — a read-only view of the duel and party ladders. Close it with Escape, like reading a runestone. (de_ladder / de_party_ladder still work in the console.)");

            TournamentUiKey = Config.Bind(
                "Tournaments",
                "TournamentUiKey",
                KeyCode.F7,
                "Opens the tournament board — status, registration (lock a companion's Communion Totem into a slot to enter), the bracket, and admin controls. (de_tournament still works in the console.)");

            RequiredEntrantLevel = Config.Bind(
                "Tournaments",
                "RequiredEntrantLevel",
                0,
                "0 = no restriction. When >0, a 1v1 entrant's companion level must match this exactly to be allowed to register (party entries are not level-gated).");

            MaxEntrants = Config.Bind(
                "Tournaments",
                "MaxEntrants",
                4,
                "Hard cap on tournament entrants. `de_tournament start` clamps any size above this, and a size of 0 (or omitted) now means \"use this cap\" instead of unlimited.");

            WagersEnabled = Config.Bind(
                "Wagers",
                "WagersEnabled",
                true,
                "Master switch for wagered events: player-started tournaments and duel invites staked in Coins or Valcoins. Turn off to leave only the free admin-run tournaments.");

            WageredBracketSize = Config.Bind(
                "Wagers",
                "BracketSize",
                4,
                "How many entrants a wagered tournament needs. The bracket starts automatically once it is full, and a tournament that never fills is cancelled with every stake refunded.");

            WageredRegistrationMinutes = Config.Bind(
                "Wagers",
                "RegistrationMinutes",
                30f,
                "How long a wagered tournament stays open for registration before it is auto-cancelled and every stake refunded. 0 = never expire (the host or an admin must cancel it).");

            TournamentCoinFee = Config.Bind(
                "Wagers",
                "TournamentCoinFee",
                100,
                "Coins it costs to START a Coin tournament, and the same amount each entrant pays to register.");

            TournamentCoinPrize = Config.Bind(
                "Wagers",
                "TournamentCoinPrize",
                999,
                "Coins paid to the champion of a Coin tournament.");

            TournamentValcoinFee = Config.Bind(
                "Wagers",
                "TournamentValcoinFee",
                10,
                "Valcoins it costs to START a Valcoin tournament, and the same amount each entrant pays to register. Requires the Valheim Donations wallet API on the server.");

            TournamentValcoinPrize = Config.Bind(
                "Wagers",
                "TournamentValcoinPrize",
                100,
                "Valcoins the champion of a Valcoin tournament receives. DISPLAY ONLY on this side: the actual payout is minted by the donations mod from its own valcoin_quests.yaml entry `ls_tournament_prize`, which is the only place allowed to price a Valcoin reward. Keep the two numbers in step.");

            DuelCoinStake = Config.Bind(
                "Wagers",
                "DuelCoinStake",
                100,
                "Coins staked by each side of a Coin duel invite. The winner takes both stakes.");

            DuelValcoinStake = Config.Bind(
                "Wagers",
                "DuelValcoinStake",
                10,
                "Valcoins staked by each side of a Valcoin duel invite. The winner takes both stakes — no Valcoins are created, they only change hands.");

            BountyEnabled = Config.Bind(
                "Bounty",
                "Enabled",
                true,
                "Master switch for bounty hunting. Even when true the feature only runs on a server/host that ALSO has BiomeLords, ValheimServerGuide and Valheim Donations installed — set it to false to switch bounties off on a server that would otherwise qualify. Check the log line \"[bounty] feature gate: ON/OFF\" to see which condition failed.");

            BountySearchMinRadius = Config.Bind(
                "Bounty",
                "SearchMinRadius",
                500f,
                "Closest a bounty may be posted to the world centre, in metres. Keeps targets out of the starting area.");

            BountySearchMaxRadius = Config.Bind(
                "Bounty",
                "SearchMaxRadius",
                6000f,
                "Furthest a bounty may be posted from the world centre, in metres. Keep well inside the world edge (~10000) so candidates don't fall into the edge ocean.");

            BountyLandCheckRadius = Config.Bind(
                "Bounty",
                "LandCheckRadius",
                80f,
                "How far around a candidate spot the ground must be dry land, in metres. Checked as three rings — clearing the outermost means the landmass is at least twice this across, which is what rules out islets and narrow spits. Lower it only if bounties become hard to place.");

            BountyWaterMargin = Config.Bind(
                "Bounty",
                "WaterMargin",
                3f,
                "How far above sea level the ground must sit, in metres, to count as dry. Raise it to push bounties further inland from shorelines.");

            BountyMaxHeightVariance = Config.Bind(
                "Bounty",
                "MaxHeightVariance",
                10f,
                "Largest height swing allowed in the immediate area around a bounty, in metres. Rejects cliff faces and spires where a fight can't happen. Raise it for more mountain bounties.");

            BountySampleAttempts = Config.Bind(
                "Bounty",
                "SampleAttempts",
                200,
                "How many random spots to test before giving up on placing one bounty. Higher is slower but more reliable on worlds with little qualifying land.");

            BountyTierBlackForest = Config.Bind("Bounty", "TierBlackForest", 1,
                "Base difficulty tier (1-5) for bounties posted in the Black Forest.");
            BountyTierSwamp = Config.Bind("Bounty", "TierSwamp", 2,
                "Base difficulty tier (1-5) for bounties posted in the Swamp.");
            BountyTierMountain = Config.Bind("Bounty", "TierMountain", 3,
                "Base difficulty tier (1-5) for bounties posted in the Mountains.");
            BountyTierPlains = Config.Bind("Bounty", "TierPlains", 4,
                "Base difficulty tier (1-5) for bounties posted in the Plains.");

            BountyHealthBase = Config.Bind(
                "Bounty",
                "HealthMultiplierBase",
                3f,
                "Health multiplier for a tier 1 bounty target, over the creature's normal health. Applied on top of its star level.");

            BountyHealthGrowth = Config.Bind(
                "Bounty",
                "HealthMultiplierGrowth",
                1.6f,
                "How much the health multiplier compounds per tier. With the defaults: tier 1 = 3x, tier 2 = 4.8x, tier 3 = 7.7x, tier 4 = 12.3x, tier 5 = 19.7x.");

            BountyMaxStarLevel = Config.Bind(
                "Bounty",
                "MaxStarLevel",
                3,
                "Cap on the vanilla star level given to a bounty target. Stars carry DAMAGE scaling, so raising this makes targets hit far harder, not just survive longer — most of a tier's difficulty is meant to come from health and escort size instead.");

            BountyMinionsBase = Config.Bind("Bounty", "MinionsBase", 1,
                "How many escort minions a tier 1 bounty target has.");
            BountyMinionsPerTier = Config.Bind("Bounty", "MinionsPerTier", 1,
                "Extra escort minions added per tier above 1.");
            BountyMaxMinions = Config.Bind("Bounty", "MaxMinions", 6,
                "Hard cap on escort minions, whatever the tier. Bounds the AI load of one fight.");

            BountyMinionRingRadius = Config.Bind(
                "Bounty",
                "MinionRingRadius",
                6f,
                "How far from the bounty target its escort spawns, in metres.");

            BountyAlertRangeMultiplier = Config.Bind(
                "Bounty",
                "AlertRangeMultiplier",
                2f,
                "How much further than normal a bounty creature notices hunters. Bounty Dvergr hunt on sight rather than waiting to be provoked, so they notice you first.");

            BountyRoamRadius = Config.Bind(
                "Bounty",
                "RoamRadius",
                12f,
                "How far a bounty creature wanders from where it was posted, in metres. Keeps the map pin meaningful; it still chases hunters normally.");

            BountyMaxTierBonus = Config.Bind(
                "Bounty",
                "MaxTierBonus",
                1,
                "How many tiers a top-standing bounty hunter's rewards may be bumped by. The bump selects a higher-tier reward entry, so better hunters get better bundles without a second reward table.");

            BountyValcoinBaseChance = Config.Bind(
                "Bounty",
                "ValcoinBaseChance",
                0.10f,
                "Chance (0-1) that a tier 1 bounty pays Valcoin, before any rank bonus. Valcoin is a REWARD ONLY — nothing in bounty hunting can be bought with it.");

            BountyValcoinChancePerTier = Config.Bind(
                "Bounty",
                "ValcoinChancePerTier",
                0.05f,
                "Extra Valcoin chance (0-1) added per tier above 1.");

            BountyValcoinRankBonus = Config.Bind(
                "Bounty",
                "ValcoinRankBonus",
                0.25f,
                "Extra Valcoin chance (0-1) for the #1 ranked duel/party player, tapering to zero at ValcoinRankDepth. This is what ties the coin payout to the duel and tournament ladders.");

            BountyValcoinRankDepth = Config.Bind(
                "Bounty",
                "ValcoinRankDepth",
                10,
                "How deep the ladder bonus reaches. At rank 1 the full ValcoinRankBonus applies; at this rank and below, none of it does.");

            BountyValcoinMaxChance = Config.Bind(
                "Bounty",
                "ValcoinMaxChance",
                0.75f,
                "Hard cap (0-1) on the Valcoin payout chance, so a payout is never guaranteed however high a player ranks.");

            BountyPointsPerTier = Config.Bind(
                "Bounty",
                "PointsPerTier",
                10,
                "Bounty-ladder points awarded per tier of the bounty answered (tier 3 pays 3x this). Tier-weighting is what stops the board being won by grinding easy postings.");

            BountyLeaderboardBonusRank = Config.Bind(
                "Bounty",
                "LeaderboardBonusRank",
                3,
                "Hunters ranked this high or better on the bounty ladder get +1 reward tier (capped by MaxTierBonus). 0 disables the bonus entirely.");

            BountyUiKey = Config.Bind(
                "Bounty",
                "BountyUiKey",
                KeyCode.F8,
                "Opens the Wanted Board — open postings, your active bounty, and the hunter standings. There is also a Bounty Board button in your inventory. Close it with Escape.");

            BountyMaxBoardEntries = Config.Bind(
                "Bounty",
                "MaxBoardEntries",
                3,
                "How many OPEN postings the board keeps available. Postings players have accepted don't count toward this, so the board still offers work while others are out hunting.");

            BountyMaxActivePerPlayer = Config.Bind(
                "Bounty",
                "MaxActiveBountyPerPlayer",
                1,
                "How many bounties one player may hold at a time.");

            BountyArrivalRadius = Config.Bind(
                "Bounty",
                "ArrivalRadius",
                80f,
                "How close a hunter must get before the bounty's creatures are spawned, in metres. They aren't spawned until someone travels there — Valheim doesn't simulate unloaded zones, so a camp placed in advance would sit frozen.");

            BountyButtonOffset = Config.Bind(
                "Bounty",
                "InventoryButtonOffset",
                "0,0",
                "LEGACY — superseded by Interface/MenuBarOffset. The Bounty Board button is now one of a row of menu buttons, so this nudges the WHOLE row. Left in place because it still works: if you had already dialled a value in here it is honoured and MenuBarOffset is ignored. Leave it at \"0,0\" to use MenuBarOffset instead.");

            BountyRefreshHours = Config.Bind(
                "Bounty",
                "RefreshHours",
                24f,
                "How long an UNCLAIMED posting stays on the board before it's retired and replaced, in real hours. Accepted bounties never expire — a hunter part-way to their mark keeps it. Set to 0 to disable rotation entirely.");

            BountyEliteChance = Config.Bind(
                "Bounty",
                "EliteChance",
                0.25f,
                "Chance (0-1) that a newly posted bounty is an ELITE (top-tier) one. Only ever one open at a time, and only hunters ranked high enough may take it — see EliteRankTopN. The top tier is never reached from a biome alone, so this is the only way it appears.");

            BountyEliteRankTopN = Config.Bind(
                "Bounty",
                "EliteRankTopN",
                10,
                "How high a player must rank on the duel OR party ladder to answer elite postings. This is the second, non-Valcoin reason to compete: rank buys ACCESS here, where the coin roll only changes a chance. 0 disables the gate (anyone may take them).");

            _harmony = new Harmony(PluginGuid);
            _harmony.PatchAll();

            // Client-side companion map pins (see CompanionMapPins). Lives on the
            // plugin GameObject so it persists across scene loads.
            gameObject.AddComponent<CompanionMapPins>();

            // Drives the channeled Communion Rite (hold-to-recruit with fail
            // conditions). See CommunionRite / docs/Ally-Recruitment.md.
            gameObject.AddComponent<Companions.CommunionRite>();

            // Drives the Dead Raiser sealing rite (hold Block on your own Follow
            // companion with a staff + wisp). See SealingRite / docs/Companion-Totems.md.
            gameObject.AddComponent<Companions.SealingRite>();

            // Manages the companion-inventory panel + injected rename field.
            gameObject.AddComponent<CompanionInventoryGui>();

            // Places the shared chest/storage panel (config offset) and makes it
            // draggable. Self-disables when BiomeLords is loaded.
            gameObject.AddComponent<Companions.ContainerPanelPositioner>();

            // Client-side tournament driver: summons escrowed companions for a match
            // and reseals/despawns them when it resolves (docs/Tournaments.md).
            gameObject.AddComponent<TournamentClient>();

            // Interactive tournament panel (registration slots + admin controls).
            gameObject.AddComponent<TournamentRegistration>();

            // Wanted Board: keeps postings stocked (server), pins the local player's
            // accepted bounty and spawns it on arrival (docs/Bounty-Hunting.md).
            gameObject.AddComponent<Bounty.BountyBoardRunner>();
            gameObject.AddComponent<Bounty.BountyBoardPanel>();

            Log.LogInfo($"{PluginName} v{PluginVersion} loaded.");
        }

        private void OnDestroy()
        {
            _harmony?.UnpatchSelf();
        }

        private void Update()
        {
            var player = Player.m_localPlayer;
            if (player == null) return;

            // Don't let our hotkeys fire while the player is typing — in the rename
            // box, chat, console, etc. Otherwise letters in a name (e/y/g/h/j…)
            // would trigger stance/rename/feed actions mid-edit.
            if (TextInput.IsVisible() || (Chat.instance != null && Chat.instance.HasFocus())) return;
            if (Console.IsVisible()) return;
            // Suppress our keys while the player is typing in the companion inventory
            // panel's name field (its GuiInputField isn't TextInput, so the checks
            // above don't cover it).
            if (Companions.CompanionInventoryGui.IsTyping) return;
            // While one of our full-screen panels is open its own buttons/Escape drive
            // it; don't let world hotkeys fire underneath. Each panel's own key still
            // toggles it closed.
            if (Companions.TournamentRegistration.IsOpen && !Input.GetKeyDown(TournamentUiKey.Value)) return;
            if (Bounty.BountyBoardPanel.IsOpen && !Input.GetKeyDown(BountyUiKey.Value)) return;

            if (Input.GetKeyDown(BountyUiKey.Value))
            {
                Bounty.BountyBoardPanel.Toggle(player);
            }

            if (Input.GetKeyDown(CommunionKey.Value))
            {
                HandleFeedInput(player);
            }

            // The Communion Rite is now channeled by HOLDING the vanilla Block
            // button (feature change) — begin it whenever Block is HELD and the
            // crosshair is on a subdued Dvergr. We key off "held" (not the press
            // down-edge) on purpose: you often block continuously through the fight,
            // so the Dvergr can drop to the subdue threshold while Block is already
            // down and there'd be no fresh press to catch. TryBeginCommune no-ops if
            // a rite is already active. We only READ the button, so the shield still
            // raises and blocking/dodging keep working through the rite.
            // The same held-Block idiom drives two rites, told apart purely by what
            // the crosshair is on: an unrecruited subdued Dvergr -> Communion (free
            // it), your OWN Follow-stance companion -> Sealing (bind it into a
            // Communion Totem, Dead Raiser + Wisp required). Each helper early-outs
            // on the other's target, so they can never both fire.
            if (Companions.CommunionRite.BlockHeld())
            {
                TryBeginCommune(player);
                TryBeginSeal(player);
            }

            if (Input.GetKeyDown(ChoreAssignKey.Value))
            {
                HandleChoreAssignInput(player);
            }

            if (Input.GetKeyDown(DuelSelectKey.Value))
            {
                HandleDuelInput(player);
            }

            if (Input.GetKeyDown(PartyDuelKey.Value))
            {
                HandlePartyDuelInput(player);
            }

            if (Input.GetKeyDown(StanceCycleKey.Value))
            {
                HandleStanceCycleInput(player);
            }

            if (Input.GetKeyDown(InventoryKey.Value))
            {
                HandleInventoryInput(player);
            }

            if (Input.GetKeyDown(RankingUiKey.Value))
            {
                Companions.RankingBoard.Open();
            }

            if (Input.GetKeyDown(TournamentUiKey.Value))
            {
                Companions.TournamentRegistration.Toggle(player);
            }
        }

        // Opens the hovered companion's inventory (chest-like panel, req 15). The
        // panel also carries a rename field, so this single key covers both the
        // "open inventory" and "rename" jobs (req 3). Owner-gated like the other
        // command keys.
        private void HandleInventoryInput(Player player)
        {
            var hoverObject = player.GetHoverObject();
            if (hoverObject == null) return;

            var target = hoverObject.GetComponentInParent<Character>();
            var companion = target?.GetComponent<DvergrCompanion>();
            if (companion == null) return;

            if (!companion.IsOwner(player))
            {
                MessageHud.instance.ShowMessage(MessageHud.MessageType.Center, "This companion answers to another.");
                return;
            }

            var inventory = companion.GetComponent<CompanionInventory>();
            if (inventory == null) inventory = companion.gameObject.AddComponent<CompanionInventory>();

            CompanionInventoryGui.Open(companion, inventory);
        }

        // The CommunionKey (G) now only FEEDS a hovered companion — recruiting moved
        // onto the Block button (see TryBeginCommune). Once a Dvergr is freed the key
        // naturally becomes Feed; see docs/Ally-Commands.md.
        private void HandleFeedInput(Player player)
        {
            var hoverObject = player.GetHoverObject();
            if (hoverObject == null) return;

            var target = hoverObject.GetComponentInParent<Character>();
            if (target == null) return;

            var hovered = target.GetComponent<DvergrCompanion>();
            if (hovered == null) return;

            // Feeding is NOT owner-gated — any player may offer a mead to any
            // companion (a friend can top up your ally, or heal a duel loser).
            // Other commands (stance/rename/chore/duel) stay owner-only; only
            // the heal is shared. TryFeed claims the companion's ZDO before
            // SetHealth, so the heal lands even on someone else's ally.
            bool mine = hovered.IsOwner(player);
            if (MeadFeedingService.TryFeed(target, player))
            {
                MessageHud.instance.ShowMessage(MessageHud.MessageType.Center,
                    mine ? "Your ally drinks deep." : $"{hovered.DisplayName} drinks deep.");
            }
            else
            {
                MessageHud.instance.ShowMessage(MessageHud.MessageType.Center, "You have no health mead to offer.");
            }
        }

        // Fires on every Block press. Only acts when the crosshair is on a subdued,
        // unrecruited Dvergr — otherwise it does nothing and Block behaves as vanilla
        // (no message spam while blocking in combat). Begins the channeled rite; the
        // CommunionRite component then watches the held Block button + fail conditions
        // each frame and calls TryRecruit once the channel completes (see
        // CommunionRite / docs/Ally-Recruitment.md).
        private void TryBeginCommune(Player player)
        {
            if (Companions.CommunionRite.Instance == null || Companions.CommunionRite.Instance.IsActive) return;

            var hoverObject = player.GetHoverObject();
            if (hoverObject == null) return;

            var target = hoverObject.GetComponentInParent<Character>();
            if (target == null) return;

            // Blocking near/at an already-freed companion is just normal combat.
            if (target.GetComponent<DvergrCompanion>() != null) return;
            if (!CommunionService.IsSubduedDvergr(target)) return;

            Companions.CommunionRite.Instance.Begin(target, player);
        }

        // The sealing counterpart of TryBeginCommune: hold Block on YOUR OWN
        // Follow-stance companion with a Dead Raiser equipped and a Wisp in the
        // pack to bind it into a Communion Totem in the field (SealingRite).
        // Refuses silently unless the staff is actually equipped, so blocking beside
        // an ally in a fight never nags.
        private void TryBeginSeal(Player player)
        {
            var rite = Companions.SealingRite.Instance;
            if (rite == null || rite.IsActive) return;
            if (Companions.CommunionRite.Instance != null && Companions.CommunionRite.Instance.IsActive) return;

            var hoverObject = player.GetHoverObject();
            if (hoverObject == null) return;

            var target = hoverObject.GetComponentInParent<Character>();
            var companion = target != null ? target.GetComponent<DvergrCompanion>() : null;
            if (companion == null) return;

            rite.Begin(companion, player);
        }

        private void HandleChoreAssignInput(Player player)
        {
            var hoverObject = player.GetHoverObject();
            if (hoverObject == null) return;

            // Press H directly on YOUR OWN companion to pull it off its current
            // chore (the counterpart to pressing H on the station, which only works
            // if you can still find/hover the station it's tending).
            var hoveredCharacter = hoverObject.GetComponentInParent<Character>();
            var hoveredCompanion = hoveredCharacter != null ? hoveredCharacter.GetComponent<DvergrCompanion>() : null;
            if (hoveredCompanion != null)
            {
                if (!hoveredCompanion.IsOwner(player))
                {
                    MessageHud.instance.ShowMessage(MessageHud.MessageType.Center, "This companion answers to another.");
                    return;
                }

                var hoveredChore = hoveredCompanion.GetComponent<ChoreAI>();
                if (hoveredChore != null && hoveredChore.IsAssigned)
                {
                    hoveredChore.Unassign();
                    MessageHud.instance.ShowMessage(MessageHud.MessageType.Center, "Ally returns to your side.");
                }
                else
                {
                    MessageHud.instance.ShowMessage(MessageHud.MessageType.Center, "This ally has no chore to leave.");
                }
                return;
            }

            // Figure out what kind of chore the hovered thing is, and which caste
            // is allowed to do it.
            var station = hoverObject.GetComponentInParent<Smelter>();
            var fermenter = station == null ? hoverObject.GetComponentInParent<Fermenter>() : null;
            var cooker = (station == null && fermenter == null) ? hoverObject.GetComponentInParent<CookingStation>() : null;
            var crop = (station == null && fermenter == null && cooker == null) ? hoverObject.GetComponentInParent<Pickable>() : null;
            ItemStand farmStand = null;
            Character animal = null;
            Container haulChest = null;
            if (station == null && fermenter == null && cooker == null && crop == null)
            {
                // A Cultivator on an item stand marks a field to farm (plant + harvest
                // in radius around the stand). GetAttachedItem() is the item's prefab name.
                var stand = hoverObject.GetComponentInParent<ItemStand>();
                if (stand != null && stand.HaveAttachment() && stand.GetAttachedItem() == "Cultivator") farmStand = stand;

                if (farmStand == null)
                {
                    var ch = hoverObject.GetComponentInParent<Character>();
                    if (ch != null && ch.IsTamed() && ch.GetComponent<DvergrCompanion>() == null) animal = ch;
                    if (animal == null) haulChest = hoverObject.GetComponentInParent<Container>();
                }
            }
            if (station == null && fermenter == null && cooker == null && crop == null && farmStand == null && animal == null && haulChest == null) return;

            // The exact object a chore is claimed against (matches ChoreAI.BeginChore).
            GameObject anchorGo =
                station != null ? station.gameObject :
                fermenter != null ? fermenter.gameObject :
                cooker != null ? cooker.gameObject :
                crop != null ? crop.gameObject :
                farmStand != null ? farmStand.gameObject :
                animal != null ? animal.gameObject :
                haulChest != null ? haulChest.gameObject : null;

            // Already being tended? Pressing H on a station your OWN ally works
            // releases it (toggle-off); otherwise report who's on it and refuse —
            // two companions never share a chore.
            var existingClaim = ChoreAI.ClaimantOf(anchorGo);

            // Feeding is claimed by RANGE (one mage tends a whole pen), so also treat
            // any animal already covered by a feeder's radius as claimed — this is
            // what blocks a second mage on a pen that's already being fed.
            if (existingClaim == null && animal != null)
                existingClaim = ChoreAI.FeederCovering(animal.transform.position);
            if (existingClaim != null)
            {
                var holder = existingClaim.GetComponent<DvergrCompanion>();
                if (holder != null && holder.IsOwner(player))
                {
                    existingClaim.Unassign();
                    MessageHud.instance.ShowMessage(MessageHud.MessageType.Center, "Ally returns to your side.");
                }
                else
                {
                    MessageHud.instance.ShowMessage(MessageHud.MessageType.Center, $"{existingClaim.WorkerName} is already working here.");
                }
                return;
            }

            // Smelter-family stations map to Fire/Ice; Provisioning (fermenter/
            // cooking), Farm and animal-tending are the Support Mage's; Hauling
            // (a destination chest) is the Rogue's (docs/Ally-Chores.md).
            DvergrCaste? requiredCaste =
                station != null ? ChoreRules.RequiredCaste(station) :
                haulChest != null ? DvergrCaste.Rogue :
                DvergrCaste.SupportMage;

            // Only the player's OWN, currently-free companions of the right caste
            // are eligible (an ally already on a chore isn't yanked off it).
            var companion = FindNearestRecruitedCompanion(player.transform.position, ChoreAssignRadius.Value, requiredCaste, player, freeOnly: true);
            if (companion == null)
            {
                var msg = requiredCaste.HasValue
                    ? $"Only your own free {ChoreRules.DisplayName(requiredCaste.Value)} can do this — none nearby."
                    : "No free ally of yours nearby to assign.";
                MessageHud.instance.ShowMessage(MessageHud.MessageType.Center, msg);
                return;
            }

            var chore = companion.GetComponent<ChoreAI>();
            if (chore == null)
            {
                chore = companion.gameObject.AddComponent<ChoreAI>();
            }

            if (station != null)
            {
                chore.AssignToSmelter(station);
                MessageHud.instance.ShowMessage(MessageHud.MessageType.Center, "Ally tends the station.");
            }
            else if (fermenter != null)
            {
                chore.AssignToFermenter(fermenter);
                MessageHud.instance.ShowMessage(MessageHud.MessageType.Center, "Ally tends the brew.");
            }
            else if (cooker != null)
            {
                chore.AssignToCooking(cooker);
                MessageHud.instance.ShowMessage(MessageHud.MessageType.Center, "Ally tends the cookfire.");
            }
            else if (crop != null || farmStand != null)
            {
                chore.AssignToFarm(crop != null ? crop.gameObject : farmStand.gameObject);
                MessageHud.instance.ShowMessage(MessageHud.MessageType.Center, "Ally tends the field.");
            }
            else if (animal != null)
            {
                chore.AssignToFeedAnimals(animal.gameObject);
                MessageHud.instance.ShowMessage(MessageHud.MessageType.Center, "Ally tends the animals.");
            }
            else
            {
                chore.AssignToHaul(haulChest);
                MessageHud.instance.ShowMessage(MessageHud.MessageType.Center, "Ally hauls to this chest.");
            }
        }

        // Toggles duel mode on the hovered companion. Only the owner may do this
        // (req 1). Entering makes it fight other players' duel-mode companions;
        // pressing again stands it down. See docs/Duel-Arena.md.
        private void HandleDuelInput(Player player)
        {
            var hoverObject = player.GetHoverObject();
            if (hoverObject == null) return;

            var target = hoverObject.GetComponentInParent<Character>();
            var duelComp = target != null ? target.GetComponent<DvergrCompanion>() : null;
            if (duelComp == null)
            {
                MessageHud.instance.ShowMessage(MessageHud.MessageType.Center, "That is not an ally you can pit in a duel.");
                return;
            }

            // req 1: only the owner can put their own companion into duel mode.
            if (!duelComp.IsOwner(player))
            {
                MessageHud.instance.ShowMessage(MessageHud.MessageType.Center, "Only its owner can send this companion to duel.");
                return;
            }

            if (duelComp.DuelMode)
            {
                duelComp.ExitDuelMode(DvergrCompanion.DuelExitReason.OwnerStopped);
                return;
            }

            if (!duelComp.EnterDuelMode())
            {
                MessageHud.instance.ShowMessage(MessageHud.MessageType.Center, "This companion cannot duel right now — unassign its chore first.");
            }
        }

        // Toggles a PARTY duel on the hovered companion's owner (must be the local
        // player). Entering gathers the player's nearby Follow-stance companions
        // into a team; pressing again stands the whole team down. See
        // docs/Party-Duels.md.
        private void HandlePartyDuelInput(Player player)
        {
            var hoverObject = player.GetHoverObject();
            if (hoverObject == null) return;

            var target = hoverObject.GetComponentInParent<Character>();
            var hovered = target != null ? target.GetComponent<DvergrCompanion>() : null;
            if (hovered == null)
            {
                MessageHud.instance.ShowMessage(MessageHud.MessageType.Center, "That is not an ally you can send to a party duel.");
                return;
            }
            if (!hovered.IsOwner(player))
            {
                MessageHud.instance.ShowMessage(MessageHud.MessageType.Center, "Only its owner can send this companion to duel.");
                return;
            }

            long ownerId = player.GetPlayerID();

            // Already in a party duel: stand the whole team down.
            bool anyInParty = false;
            foreach (var c in DvergrCompanion.All)
                if (c != null && c.OwnerId == ownerId && c.PartyDuelMode) { anyInParty = true; break; }
            if (anyInParty)
            {
                int stood = 0;
                foreach (var c in DvergrCompanion.All)
                    if (c != null && c.OwnerId == ownerId && c.PartyDuelMode)
                    { c.ExitPartyDuelMode(DvergrCompanion.DuelExitReason.OwnerStopped); stood++; }
                MessageHud.instance.ShowMessage(MessageHud.MessageType.Center, $"Your party ({stood}) stands down.");
                return;
            }

            // Gather the player's nearby, free, Follow-stance companions into a team.
            const float gatherRadius = 25f;
            var nearby = new List<Character>();
            Character.GetCharactersInRange(player.transform.position, gatherRadius, nearby);

            var team = new List<DvergrCompanion>();
            foreach (var ch in nearby)
            {
                var c = ch.GetComponent<DvergrCompanion>();
                if (c == null || c.OwnerId != ownerId) continue;
                if (c.Stance != CompanionStance.Follow) continue;
                if (ch.GetComponent<ChoreAI>()?.IsAssigned == true) continue;
                if (c.DuelMode || c.IsFeral) continue;
                team.Add(c);
            }
            if (team.Count == 0)
            {
                MessageHud.instance.ShowMessage(MessageHud.MessageType.Center,
                    "No free Follow-stance allies of yours nearby to form a party.");
                return;
            }

            // Nearest-first, capped at MaxPartySize.
            team.Sort((a, b) => Vector3.Distance(player.transform.position, a.transform.position)
                .CompareTo(Vector3.Distance(player.transform.position, b.transform.position)));
            int cap = Mathf.Max(1, MaxPartySize.Value);
            int joined = 0;
            foreach (var c in team)
            {
                if (joined >= cap) break;
                if (c.EnterPartyDuelMode()) joined++;
            }
            MessageHud.instance.ShowMessage(MessageHud.MessageType.Center,
                $"Your party ({joined}) squares up — waiting for a rival party.");
        }

        private void HandleStanceCycleInput(Player player)
        {
            var hoverObject = player.GetHoverObject();
            if (hoverObject == null) return;

            var target = hoverObject.GetComponentInParent<Character>();
            var companion = target?.GetComponent<DvergrCompanion>();
            if (companion == null) return;

            if (!companion.IsOwner(player))
            {
                MessageHud.instance.ShowMessage(MessageHud.MessageType.Center, "This companion answers to another.");
                return;
            }

            if (target.GetComponent<ChoreAI>()?.IsAssigned == true || companion.InAnyDuelMode)
            {
                MessageHud.instance.ShowMessage(MessageHud.MessageType.Center, "Ally is busy — unassign it first.");
                return;
            }

            // Follow -> Guard -> Standby -> Follow.
            var next = companion.Stance == CompanionStance.Follow ? CompanionStance.Guard
                : companion.Stance == CompanionStance.Guard ? CompanionStance.Standby
                : CompanionStance.Follow;

            companion.SetStance(next, player.gameObject);

            var label = next == CompanionStance.Follow ? "Follow"
                : next == CompanionStance.Guard ? "Guard" : "Standby";
            MessageHud.instance.ShowMessage(MessageHud.MessageType.Center, $"Ally stance: {label}.");
        }

        private static DvergrCompanion FindNearestRecruitedCompanion(Vector3 origin, float radius, DvergrCaste? requiredCaste = null, Player owner = null, bool freeOnly = false)
        {
            var nearby = new List<Character>();
            Character.GetCharactersInRange(origin, radius, nearby);

            DvergrCompanion closest = null;
            var closestDist = float.MaxValue;

            foreach (var character in nearby)
            {
                var companion = character.GetComponent<DvergrCompanion>();
                if (companion == null) continue;
                if (requiredCaste.HasValue && companion.Caste != requiredCaste.Value) continue;
                if (owner != null && !companion.IsOwner(owner)) continue;
                if (freeOnly && character.GetComponent<ChoreAI>()?.IsAssigned == true) continue;

                var dist = Vector3.Distance(origin, character.transform.position);
                if (dist < closestDist)
                {
                    closestDist = dist;
                    closest = companion;
                }
            }

            return closest;
        }
    }
}
