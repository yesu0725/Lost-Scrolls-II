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
        public const string PluginVersion = "0.1.0";

        public static Plugin Instance { get; private set; }
        public static ManualLogSource Log { get; private set; }

        // Dual-purpose key: on a subdued, unrecruited Dvergr this performs the
        // Communion Rite (Phase 2; Sword-of-Truth item gate still deferred —
        // see docs/Ally-Recruitment.md). On an already-recruited companion it
        // instead feeds it a health mead from the player's inventory (feature
        // change — see docs/Ally-Commands.md). Once a Dvergr is communed it no
        // longer needs recruiting, so the same key naturally becomes Feed.
        public static ConfigEntry<KeyCode> CommunionKey { get; private set; }

        // Phase 4: pressed while hovering a Smelter to assign/unassign the nearest
        // recruited companion as its chore worker. See docs/Ally-Chores.md.
        public static ConfigEntry<KeyCode> ChoreAssignKey { get; private set; }
        public static ConfigEntry<float> ChoreAssignRadius { get; private set; }

        // Phase 6: press while hovering YOUR OWN companion to toggle its duel
        // mode. A duel-mode companion fights other players' duel-mode companions.
        // See docs/Duel-Arena.md.
        public static ConfigEntry<KeyCode> DuelSelectKey { get; private set; }

        // Feature add: toggle a hovered companion's stance (Follow <-> Guard).
        public static ConfigEntry<KeyCode> StanceCycleKey { get; private set; }

        // Feature add: rename a hovered companion (opens the vanilla text input).
        public static ConfigEntry<KeyCode> RenameKey { get; private set; }

        // Feature add: show a live minimap pin at each of the local player's own
        // companions. Client-side, so other players never see your companions.
        public static ConfigEntry<bool> ShowMapPins { get; private set; }
        public static ConfigEntry<int> MapPinIcon { get; private set; }

        private Harmony _harmony;

        private void Awake()
        {
            Instance = this;
            Log = Logger;

            CommunionKey = Config.Bind(
                "Recruitment",
                "CommunionKey",
                KeyCode.G,
                "Key held while hovering a subdued Dvergr to perform the Communion Rite and recruit it.");

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

            StanceCycleKey = Config.Bind(
                "Companions",
                "StanceCycleKey",
                KeyCode.E,
                "Press while hovering your companion to cycle its stance: Follow -> Guard -> Standby.");

            RenameKey = Config.Bind(
                "Companions",
                "RenameKey",
                KeyCode.Y,
                "Press while hovering your companion to rename it.");

            ShowMapPins = Config.Bind(
                "Companions",
                "ShowMapPins",
                true,
                "Show a live minimap pin at each of your own recruited companions. Pins are client-side — other players never see your companions, and you never see theirs.");

            MapPinIcon = Config.Bind(
                "Companions",
                "MapPinIcon",
                3,
                "Which vanilla map-pin icon (0-4) to use for companion pins.");

            _harmony = new Harmony(PluginGuid);
            _harmony.PatchAll();

            // Client-side companion map pins (see CompanionMapPins). Lives on the
            // plugin GameObject so it persists across scene loads.
            gameObject.AddComponent<CompanionMapPins>();

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

            if (Input.GetKeyDown(CommunionKey.Value))
            {
                HandleCommunionInput(player);
            }

            if (Input.GetKeyDown(ChoreAssignKey.Value))
            {
                HandleChoreAssignInput(player);
            }

            if (Input.GetKeyDown(DuelSelectKey.Value))
            {
                HandleDuelInput(player);
            }

            if (Input.GetKeyDown(StanceCycleKey.Value))
            {
                HandleStanceCycleInput(player);
            }

            if (Input.GetKeyDown(RenameKey.Value))
            {
                HandleRenameInput(player);
            }
        }

        private void HandleRenameInput(Player player)
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

            // Vanilla rename UI — same text box used for signs / tamed creatures.
            // The companion is the TextReceiver, so confirming writes its new name.
            if (TextInput.instance != null)
            {
                TextInput.instance.RequestText(companion, "Rename companion", 24);
            }
        }

        private void HandleCommunionInput(Player player)
        {
            var hoverObject = player.GetHoverObject();
            if (hoverObject == null) return;

            var target = hoverObject.GetComponentInParent<Character>();
            if (target == null) return;

            // Already a companion: G is now Feed instead of Communion (feature
            // change — see docs/Ally-Commands.md). A Dvergr that's already been
            // freed has no further use for the recruit action on this key.
            var hovered = target.GetComponent<DvergrCompanion>();
            if (hovered != null)
            {
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
                return;
            }

            if (!CommunionService.IsSubduedDvergr(target))
            {
                MessageHud.instance.ShowMessage(MessageHud.MessageType.Center, "It is not yet ready for Communion.");
                return;
            }

            // Caste is detected from the Dvergr's prefab (all four castes are
            // real prefabs) so a recruited Fire/Ice/Support mage is tagged
            // correctly — this is what makes caste-gated chores meaningful.
            var caste = CommunionService.DetectCaste(target);
            Log.LogInfo($"[recruit] '{target.name}' detected as caste {caste}.");
            if (CommunionService.TryRecruit(target, player, caste))
            {
                MessageHud.instance.ShowMessage(MessageHud.MessageType.Center, $"The shadow's grip loosens — a {caste.Display()} joins you.");
            }
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

            if (target.GetComponent<ChoreAI>()?.IsAssigned == true || companion.DuelMode)
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
