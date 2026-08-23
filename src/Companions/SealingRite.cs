using UnityEngine;

namespace LostScrollsII.Companions
{
    // Sealing a companion into a Communion Totem IN THE FIELD, with a Dead Raiser
    // (docs/Companion-Totems.md). The Incinerator ritual still exists and is
    // unchanged; this is the portable counterpart for a blood-magic practitioner.
    //
    // Requirements (all four, checked live every frame of the channel):
    //   * a Dead Raiser (`StaffSkeleton`) EQUIPPED,
    //   * at least one Wisp in the pack (consumed 1:1, exactly like the
    //     Incinerator ritual — the wisp is what the soul is bound into),
    //   * Blood Magic skill >= SealMinBloodMagic (default 20),
    //   * the target is YOUR OWN companion, in FOLLOW stance, free (no chore, no
    //     duel, not feral).
    //
    // The channel is the same shape as the Communion Rite (CommunionRite): hold
    // the vanilla Block button with the crosshair on the companion, stay close,
    // do not get hit. It is deliberately built as a sibling of that class rather
    // than a mode inside it — the two rites share only their input idiom, and
    // every fail condition, message and outcome differs. The one thing that IS
    // shared is the accelerating Wishbone ping (played through CommunionRite's
    // helper), so both rites read the same way to a player.
    //
    // Duration scales with Blood Magic: SealChannelMaxSeconds at the minimum skill
    // down to SealChannelMinSeconds at SealFullSpeedBloodMagic. Below the minimum
    // skill the rite cannot be started at all.
    //
    // No new assets: Dead Raiser, Wisp and GoblinTotem are all vanilla, and the
    // totem itself is built by the same TotemConversionService the Incinerator
    // path uses, so a field-sealed companion is identical to an
    // Incinerator-sealed one (level/XP/name/owner/pack/ladder id all carried).
    public class SealingRite : MonoBehaviour
    {
        // Cross-client soft lock on the companion being sealed, mirroring
        // CommunionRite's. Cleared on every end path.
        public const string ZdoKeySealing = "DE_Sealing";

        public static SealingRite Instance { get; private set; }

        public const string StaffPrefab = "StaffSkeleton";

        private const float DamageEpsilon = 0.5f;
        private const float ReleaseGraceSeconds = 0.5f;
        private const float PulseIntervalStart = 1.0f;
        private const float PulseIntervalEnd = 0.28f;
        private const float BeatInterval = 1.25f;

        private DvergrCompanion _target;
        private Player _player;
        private float _elapsed;
        private float _duration;
        private float _beatTimer;
        private float _pulseTimer;
        private float _releaseGrace;
        private float _lastPlayerHealth;
        private int _beatIndex;

        private static string _staffSharedName;
        private static string _wispSharedName;

        private static readonly string[] BeatLines =
        {
            "The staff drinks the light from its eyes...",
            "Bind the soul — do not falter.",
            "The wisp swells with a borrowed spirit.",
        };

        public bool IsActive => _target != null;

        private void Awake() => Instance = this;

        // ---- eligibility ------------------------------------------------------

        // Everything that must be true to start (and to keep) the rite. `reason` is
        // only filled when the player is clearly TRYING to seal (staff equipped),
        // so an ordinary block near your own ally never spams a refusal.
        public static bool CanSeal(Player player, DvergrCompanion companion, out string reason)
        {
            reason = null;
            if (player == null || companion == null) return false;
            if (Plugin.StaffSealEnabled == null || !Plugin.StaffSealEnabled.Value) return false;
            if (!HasStaffEquipped(player)) return false;   // silent: not a sealing attempt at all

            if (!companion.IsOwner(player)) { reason = "This companion answers to another."; return false; }

            var character = companion.GetComponent<Character>();
            if (character == null || character.IsDead()) return false;

            if (companion.Stance != CompanionStance.Follow)
            { reason = "Only a companion in Follow can be sealed — set it to follow you first."; return false; }
            if (companion.ChoreActive)
            { reason = "Recall it from its work before sealing it."; return false; }
            if (companion.InAnyDuelMode)
            { reason = "It is in a duel — stand it down first."; return false; }
            if (companion.IsFeral)
            { reason = "It has turned on you. The staff finds nothing to bind."; return false; }

            int skill = BloodMagicLevel(player);
            int required = Mathf.Max(0, Plugin.SealMinBloodMagic.Value);
            if (skill < required)
            { reason = $"Your Blood Magic is too weak to bind a soul ({skill}/{required})."; return false; }

            if (WispCount(player) <= 0)
            { reason = "You need a Wisp to bind the soul into."; return false; }

            return true;
        }

        // Channel length for this player: SealChannelMaxSeconds at the minimum
        // Blood Magic skill, falling to SealChannelMinSeconds at
        // SealFullSpeedBloodMagic and clamped either side.
        public static float SealSeconds(Player player)
        {
            float slow = Mathf.Max(0.1f, Plugin.SealChannelMaxSeconds.Value);
            float fast = Mathf.Max(0.1f, Plugin.SealChannelMinSeconds.Value);
            if (fast > slow) { var swap = fast; fast = slow; slow = swap; }

            int min = Mathf.Max(0, Plugin.SealMinBloodMagic.Value);
            int full = Mathf.Max(min + 1, Plugin.SealFullSpeedBloodMagic.Value);
            int skill = BloodMagicLevel(player);

            float t01 = Mathf.Clamp01((skill - min) / (float)(full - min));
            return Mathf.Lerp(slow, fast, t01);
        }

        public static int BloodMagicLevel(Player player)
        {
            if (player == null) return 0;
            try
            {
                var skills = player.GetSkills();
                if (skills == null) return 0;
                return Mathf.FloorToInt(skills.GetSkillLevel(Skills.SkillType.BloodMagic));
            }
            catch { return 0; }
        }

        // Equipped-item check by SHARED NAME rather than m_dropPrefab: an item
        // loaded from a ZDO can have a null drop prefab, which would silently make
        // a genuinely-equipped staff invisible to us. Same technique the wisp count
        // uses (TotemConversionService.WispSharedName).
        public static bool HasStaffEquipped(Player player)
        {
            if (player == null) return false;
            var wanted = StaffSharedName();
            if (string.IsNullOrEmpty(wanted)) return false;
            var inv = player.GetInventory();
            if (inv == null) return false;
            foreach (var item in inv.GetEquippedItems())
            {
                if (item?.m_shared == null) continue;
                if (item.m_shared.m_name == wanted) return true;
            }
            return false;
        }

        private static string StaffSharedName()
        {
            if (!string.IsNullOrEmpty(_staffSharedName)) return _staffSharedName;
            var prefab = ObjectDB.instance != null ? ObjectDB.instance.GetItemPrefab(StaffPrefab) : null;
            var shared = prefab != null ? prefab.GetComponent<ItemDrop>()?.m_itemData?.m_shared : null;
            if (shared != null) _staffSharedName = shared.m_name;
            return _staffSharedName;
        }

        private static string WispName()
        {
            if (!string.IsNullOrEmpty(_wispSharedName)) return _wispSharedName;
            _wispSharedName = TotemConversionService.WispSharedName();
            return _wispSharedName;
        }

        public static int WispCount(Player player)
        {
            var name = WispName();
            if (player == null || string.IsNullOrEmpty(name)) return 0;
            var inv = player.GetInventory();
            return inv == null ? 0 : inv.CountItems(name);
        }

        // ---- the rite ---------------------------------------------------------

        public bool Begin(DvergrCompanion companion, Player player)
        {
            if (_target != null) return false;
            if (!CanSeal(player, companion, out var reason))
            {
                if (!string.IsNullOrEmpty(reason)) Msg(reason);
                return false;
            }

            var znv = companion.GetComponent<ZNetView>();
            if (znv == null || !znv.IsValid()) return false;
            if (znv.GetZDO().GetBool(ZdoKeySealing))
            {
                Msg("Another rite already has hold of it.");
                return false;
            }
            znv.ClaimOwnership();
            znv.GetZDO().Set(ZdoKeySealing, true);

            _target = companion;
            _player = player;
            _elapsed = 0f;
            _duration = SealSeconds(player);
            _beatTimer = 0f;
            _beatIndex = 0;
            _pulseTimer = 0f;
            _releaseGrace = 0f;
            _lastPlayerHealth = player.GetHealth();

            CommunionRite.Instance?.PlayPulseAt(player.transform.position);
            CommunionRite.Instance?.PlayPulseAt(companion.transform.position);

            Msg($"You raise the Dead Raiser — binding {_target.DisplayName} into a totem...");
            return true;
        }

        private void Update()
        {
            if (_target == null) return;

            var character = _target != null ? _target.GetComponent<Character>() : null;
            if (_player == null || character == null || character.IsDead())
            {
                Fail("The binding slips away.");
                return;
            }

            // Any requirement lost mid-channel breaks the rite — including
            // unequipping the staff or losing the last Wisp.
            if (!CanSeal(_player, _target, out _))
            {
                Fail("The rite is broken — the binding needs staff, wisp and a following ally.");
                return;
            }

            if (CommunionRite.BlockHeld())
            {
                _releaseGrace = 0f;
            }
            else
            {
                _releaseGrace += Time.deltaTime;
                if (_releaseGrace >= ReleaseGraceSeconds)
                {
                    Fail("You lower the staff — the binding fails.");
                    return;
                }
            }

            float maxDist = Mathf.Max(1f, Plugin.SealMaxDistance.Value);
            if (Vector3.Distance(_player.transform.position, _target.transform.position) > maxDist)
            {
                Fail("You stray too far — the binding fails.");
                return;
            }

            float hp = _player.GetHealth();
            if (Plugin.CommunionBreakOnDamage.Value && hp < _lastPlayerHealth - DamageEpsilon)
            {
                Fail("Struck mid-rite — the binding fails.");
                return;
            }
            _lastPlayerHealth = hp;

            _elapsed += Time.deltaTime;

            float progress = Mathf.Clamp01(_elapsed / Mathf.Max(0.1f, _duration));
            _pulseTimer += Time.deltaTime;
            if (_pulseTimer >= Mathf.Lerp(PulseIntervalStart, PulseIntervalEnd, progress))
            {
                _pulseTimer = 0f;
                CommunionRite.Instance?.PlayPulseAt(_target.transform.position);
                CommunionRite.Instance?.PlayPulseAt(_player.transform.position);
            }

            _beatTimer += Time.deltaTime;
            if (_beatTimer >= BeatInterval)
            {
                _beatTimer = 0f;
                Msg(BeatLines[_beatIndex % BeatLines.Length]);
                _beatIndex++;
            }

            if (_elapsed >= _duration) Succeed();
        }

        private void Succeed()
        {
            var companion = _target;
            var player = _player;
            ClearLock(companion);
            Reset();
            if (companion == null || player == null) return;

            // Build the totem BEFORE taking the wisp, so a missing prefab cannot
            // consume the reagent for nothing.
            var totem = TotemConversionService.CreateTotem(companion);
            if (totem == null)
            {
                Msg("The binding finds no vessel.");
                return;
            }

            var inv = player.GetInventory();
            var wisp = WispName();
            if (inv == null || string.IsNullOrEmpty(wisp) || inv.CountItems(wisp) <= 0)
            {
                Msg("The wisp is gone — the binding fails.");
                return;
            }
            inv.RemoveItem(wisp, 1);

            if (!inv.AddItem(totem))
            {
                // Pack full: drop it at the player's feet rather than losing the
                // companion (the totem IS the companion at this point).
                ItemDrop.DropItem(totem, 1, player.transform.position + Vector3.up, player.transform.rotation);
                Msg("Your pack is full — the totem falls at your feet.");
            }

            TotemConversionService.PlaySealVfx(companion.transform.position + Vector3.up);
            Plugin.Log.LogInfo($"[totem] {player.GetPlayerName()} sealed '{companion.DisplayName}' with a Dead Raiser.");
            Msg($"{companion.DisplayName} is sealed within a totem.");
            companion.DespawnToTotem();
        }

        private void Fail(string message)
        {
            var companion = _target;
            ClearLock(companion);
            Reset();
            Msg(message);
        }

        private static void ClearLock(DvergrCompanion companion)
        {
            if (companion == null) return;
            var znv = companion.GetComponent<ZNetView>();
            if (znv != null && znv.IsValid() && znv.IsOwner()) znv.GetZDO().Set(ZdoKeySealing, false);
        }

        private void Reset()
        {
            _target = null;
            _player = null;
            _elapsed = 0f;
            _beatTimer = 0f;
            _beatIndex = 0;
            _pulseTimer = 0f;
            _releaseGrace = 0f;
        }

        private static void Msg(string m)
        {
            if (!string.IsNullOrEmpty(m) && MessageHud.instance != null)
                MessageHud.instance.ShowMessage(MessageHud.MessageType.Center, m);
        }
    }
}
