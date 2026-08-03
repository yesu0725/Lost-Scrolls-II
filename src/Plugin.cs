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
    public class Plugin : BaseUnityPlugin
    {
        public const string PluginGuid = "com.lostscrollsii";
        public const string PluginName = "Lost Scrolls II";
        public const string PluginVersion = "0.7.0";

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

            _harmony = new Harmony(PluginGuid);
            _harmony.PatchAll();

            // Client-side companion map pins (see CompanionMapPins). Lives on the
            // plugin GameObject so it persists across scene loads.
            gameObject.AddComponent<CompanionMapPins>();

            // Drives the channeled Communion Rite (hold-to-recruit with fail
            // conditions). See CommunionRite / docs/Ally-Recruitment.md.
            gameObject.AddComponent<Companions.CommunionRite>();

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
            // While the tournament panel is open its own buttons/Escape drive it;
            // don't let world hotkeys fire underneath it (F7 still toggles it closed).
            if (Companions.TournamentRegistration.IsOpen && !Input.GetKeyDown(TournamentUiKey.Value)) return;

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
            if (Companions.CommunionRite.BlockHeld())
            {
                TryBeginCommune(player);
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
