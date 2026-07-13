using System.Collections.Generic;
using LostScrollsII.Integration;
using UnityEngine;

namespace LostScrollsII.Companions
{
    // Attached to a vanilla Dvergr creature once it has been recruited via the
    // Communion Rite (see docs/Ally-Recruitment.md). Phase 3 adds XP/leveling
    // state on top of Phase 2's recruit + follow/fight behavior.
    // Implements TextReceiver so the vanilla rename text box (signs / tamed
    // animals use the same one) can write a custom name straight onto the ally.
    public class DvergrCompanion : MonoBehaviour, TextReceiver
    {
        public const string ZdoKeyRecruited = "DE_Recruited";
        public const string ZdoKeyCaste = "DE_Caste";
        public const string ZdoKeyLevel = "DE_Level";
        public const string ZdoKeyXp = "DE_Xp";
        public const string ZdoKeyOwner = "DE_Owner";
        public const string ZdoKeyOwnerName = "DE_OwnerName";
        public const string ZdoKeyName = "DE_Name";
        // Stable per-companion identity for the duel ladders (docs/Ranking.md).
        // A GUID assigned at recruit and carried through the Communion Totem's
        // m_customData — unlike a ZDOID, it survives seal/summon and relog, so it
        // can key a durable ranking record.
        public const string ZdoKeyCompanionId = "DE_CompanionId";
        // Duel mode is stored on the ZDO so the state replicates to every client
        // (the cross-client IsEnemy check needs BOTH duelists' flags visible on a
        // single machine to pair them up). Cleared on spawn so a relog ends any
        // duel — see req 5 / docs/Duel-Arena.md.
        public const string ZdoKeyDuel = "DE_Duel";
        // Party-duel mode (docs/Party-Duels.md) — a team-vs-team variant. Same
        // replication + spawn-clear contract as ZdoKeyDuel. A companion is never
        // in both modes at once.
        public const string ZdoKeyPartyDuel = "DE_PartyDuel";

        // ---- Duel mode (docs/Duel-Arena.md) -----------------------------------
        // A duel is no longer a scripted 1v1 driven by a controller — it's a
        // *mode* the owner toggles on their own companion. A companion in duel
        // mode fights only OTHER players' duel-mode companions (never players,
        // creatures, or same-owner allies — enforced by the IsEnemy patch), and
        // auto-stands-down when no rival remains or its owner leaves. Non-lethal:
        // a hit that would drop a duelist to/below this fraction of max HP is
        // capped and ends its participation instead of killing it.
        public const float SubdueFloorHealthFraction = 0.05f;
        private const float DuelWinXpBonus = 50f;
        // How far a duel-mode companion looks for a rival duelist. Also the range
        // used to decide "no more challengers" (req 3).
        private const float DuelDetectRange = 30f;
        // If the owner is farther than this (or gone entirely), the companion
        // leaves duel mode (req 5).
        private const float OwnerVisionRange = 40f;
        // A lone duel-mode companion waits this long for a challenger before
        // standing down on its own, so it doesn't idle in duel mode forever.
        private const float DuelWaitTimeout = 60f;
        private const float DuelScanInterval = 1f;

        // Alert range used in Standby (effectively no proactive target acquisition).
        private const float StandbyAlertRange = 0f;

        // How long a player stays a hostile target after triggering retaliation
        // (they hit us) or being attacked by our owner while we Follow.
        private const float HostileDuration = 30f;

        // Hard cap. A companion starts at level 1 and can reach at most level 10.
        // Past this, XP stops accumulating entirely.
        public const int MaxLevel = 10;

        // XP needed to advance FROM a given level to the next, indexed by
        // (level - 1): level 1->2 costs 100, ... level 9->10 costs 3200. The
        // cost rises each level so leveling slows down toward the cap (see
        // docs/Ally-Leveling.md). Reaching level 10 takes 11,500 XP total.
        private static readonly int[] XpToNextByLevel =
            { 100, 250, 450, 700, 1000, 1400, 1900, 2500, 3200 };

        // How far m_alertRange is multiplied for the Guard stance vs. its
        // original value. Phase 8 (Feature add): see docs/Ally-Chores.md-style
        // open-question pattern — tune once playtested.
        private const float GuardAlertRangeMultiplier = 2.5f;

        // Lightweight per-caste leveling identity (docs/Ally-Leveling.md). Layered
        // ON TOP of vanilla SetLevel (which already scales health + damage for
        // every caste, mages' spells included). Aggressive front castes
        // (Rogue, Fire Mage) gain move speed; protective/backline castes
        // (Ice Mage, Support Mage) gain max health. Per level over base.
        private const float SpeedBonusPerLevel = 0.03f;  // +3%/level -> +27% at Lv10
        private const float HealthBonusPerLevel = 0.04f; // +4%/level -> +36% at Lv10

        public DvergrCaste Caste { get; private set; }
        public int Level { get; private set; } = 1;
        public float Xp { get; private set; }
        public CompanionStance Stance { get; private set; } = CompanionStance.Follow;

        // 0-100. Used for the hover-text indicator — see HoverTextPatch.cs.
        public float XpPercentToNextLevel =>
            IsMaxLevel ? 100f : Mathf.Clamp(Xp / XpRequiredForNextLevel() * 100f, 0f, 100f);

        // True once the companion has reached the level cap and can gain no more.
        public bool IsMaxLevel => Level >= MaxLevel;

        // XP required to advance from the current level to the next.
        private float XpRequiredForNextLevel()
        {
            int idx = Level - 1;
            if (idx < 0 || idx >= XpToNextByLevel.Length) return float.MaxValue; // at/over cap
            return XpToNextByLevel[idx];
        }

        private Character _character;
        private ZNetView _znv;
        private MonsterAI _ai;
        private float _baseAlertRange;
        private bool _baseAlertRangeCaptured;
        private float _baseRunSpeed, _baseSpeed, _baseWalkSpeed;
        private bool _baseSpeedsCaptured;

        // Player-given name (empty = use the default localized creature name).
        private string _customName;

        // Stable ladder identity (see ZdoKeyCompanionId). Lazily generated the
        // first time it's needed if the companion predates the id (legacy allies).
        private string _companionId;

        // Owner (the recruiting player's id) and transient per-player hostility.
        private long _ownerId;
        private string _ownerName;
        private readonly Dictionary<Character, float> _hostileUntil = new Dictionary<Character, float>();
        private float _pruneTimer;

        // Duel-mode runtime state (transient — never persisted; a duel is a
        // short live event that also ends on relog/owner-leave anyway).
        private bool _duelEngaged;   // has actually seen a rival this session
        private float _duelWaitTimer;
        private float _duelScanTimer;

        // Idempotency latch for the subdue. Once a companion has been subdued in
        // a bout it must NOT be re-declared a loser: the damage prefix caps HP at
        // the 5% floor, but vanilla Character/MonsterAI health regen ticks it back
        // above the floor within a frame or two while the winner's AI is still
        // swinging (state hasn't replicated yet), so a second hit would fire
        // AwardDuelWin again — a double announcement and, worse, a double point
        // award once the ranking system lands. Set true on subdue, reset only
        // when the companion re-enters duel mode. In-memory (immediate) so it
        // guards even before the ZDO DuelMode flag propagates across clients.
        private bool _duelResolved;
        public bool IsDuelResolved => _duelResolved;

        // Party-duel runtime state (transient; docs/Party-Duels.md). Mirrors the
        // 1v1 fields but a team fight ends by attrition (win when no enemy team
        // member remains) rather than a single subdue. _partyPeakEnemies records
        // the largest enemy-team size seen during the bout so the winner's XP can
        // be scaled by team sizes at stand-down (the losers are gone by then).
        private bool _partyEngaged;
        private float _partyWaitTimer;
        private float _partyScanTimer;
        private int _partyPeakEnemies;

        // Rosters accumulated over the bout (companionId -> "caste:level") so the
        // winner can report BOTH teams to the party ladder even though the losers
        // are benched (and gone from the live set) by the time it stands down. Plus
        // the opponent owner, captured on first sighting.
        private readonly Dictionary<string, string> _partyAllySnap = new Dictionary<string, string>();
        private readonly Dictionary<string, string> _partyEnemySnap = new Dictionary<string, string>();
        private long _partyOpponentOwner;
        private string _partyOpponentOwnerName;

        // Report once per (winnerOwner, loserOwner) match: every surviving winner
        // runs AwardPartyWin, but they share the same owner -> same authority client,
        // so a static latch here dedups the ladder report + the party_duel_won event.
        private static readonly Dictionary<string, float> s_partyReportLatch = new Dictionary<string, float>();
        private const float PartyReportWindow = 15f;

        // "Feral": hit with a butcher knife, the companion turns on ALL players
        // (owner included) — a deliberate betrayal/release action. In-memory only.
        private bool _feral;

        public long OwnerId => _ownerId;

        // ZDO-backed so it's visible on every client (see ZdoKeyDuel). Falls back
        // to the local mirror only if there's no valid ZNetView.
        private bool _duelModeLocal;
        public bool DuelMode
        {
            get
            {
                if (_znv != null && _znv.IsValid()) return _znv.GetZDO().GetBool(ZdoKeyDuel, false);
                return _duelModeLocal;
            }
            private set
            {
                _duelModeLocal = value;
                if (_znv != null && _znv.IsValid())
                {
                    _znv.ClaimOwnership();               // make our write authoritative so it replicates
                    _znv.GetZDO().Set(ZdoKeyDuel, value);
                }
            }
        }

        // Party-duel flag — ZDO-backed for the same cross-client reasons as DuelMode.
        private bool _partyDuelLocal;
        public bool PartyDuelMode
        {
            get
            {
                if (_znv != null && _znv.IsValid()) return _znv.GetZDO().GetBool(ZdoKeyPartyDuel, false);
                return _partyDuelLocal;
            }
            private set
            {
                _partyDuelLocal = value;
                if (_znv != null && _znv.IsValid())
                {
                    _znv.ClaimOwnership();
                    _znv.GetZDO().Set(ZdoKeyPartyDuel, value);
                }
            }
        }

        // In either competitive mode — used by the targeting/damage patches to
        // treat 1v1 and party duelists uniformly where the rules coincide (PvP
        // immunity, the non-lethal floor).
        public bool InAnyDuelMode => DuelMode || PartyDuelMode;

        // Owner's display name for the floating name tag (req 4). Prefer the
        // persisted name (works even when the owner is offline / out of range);
        // fall back to a live lookup, then null.
        public string OwnerName
        {
            get
            {
                if (!string.IsNullOrEmpty(_ownerName)) return _ownerName;
                if (_ownerId != 0L)
                {
                    var p = Player.GetPlayer(_ownerId);
                    if (p != null) return p.GetPlayerName();
                }
                return null;
            }
        }

        // Live registry of all companions (for owner-attack propagation and the
        // chore-hint gate). Maintained via OnEnable/OnDisable.
        private static readonly HashSet<DvergrCompanion> s_all = new HashSet<DvergrCompanion>();
        public static IEnumerable<DvergrCompanion> All => s_all;
        private void OnEnable() { s_all.Add(this); }
        private void OnDisable() { s_all.Remove(this); }

        // True if the player owns at least one recruited companion right now.
        public static bool PlayerHasCompanion(Player player)
        {
            if (player == null) return false;
            foreach (var c in s_all) if (c != null && c.IsOwner(player)) return true;
            return false;
        }

        private void Awake()
        {
            _character = GetComponent<Character>();
            _znv = GetComponent<ZNetView>();
            _ai = GetComponent<MonsterAI>();

            if (_znv != null && _znv.IsValid() && _znv.GetZDO().GetBool(ZdoKeyRecruited))
            {
                Caste = (DvergrCaste)_znv.GetZDO().GetInt(ZdoKeyCaste, 0);
                Level = _znv.GetZDO().GetInt(ZdoKeyLevel, 1);
                Xp = _znv.GetZDO().GetFloat(ZdoKeyXp, 0f);
                _ownerId = _znv.GetZDO().GetLong(ZdoKeyOwner, 0L);
                _ownerName = _znv.GetZDO().GetString(ZdoKeyOwnerName, null);
                _customName = _znv.GetZDO().GetString(ZdoKeyName, null);
                _companionId = _znv.GetZDO().GetString(ZdoKeyCompanionId, null);

                // Re-apply the persisted level on load — Character.SetLevel isn't
                // itself persisted by vanilla for non-star creatures, so this
                // keeps a reconnect/reload from resetting an ally back to level 1.
                ApplyLevelToCharacter();

                // A relog/reload ends any in-progress duel (req 5): clear the
                // replicated flags on spawn so a companion never comes back dueling.
                if (_znv.IsOwner())
                {
                    _znv.GetZDO().Set(ZdoKeyDuel, false);
                    _znv.GetZDO().Set(ZdoKeyPartyDuel, false);
                }
            }

            if (_ai != null)
            {
                _baseAlertRange = _ai.m_alertRange;
                _baseAlertRangeCaptured = true;
            }
            ApplyMovementTweaks();
        }

        // Pathfinding mitigation (best-effort): Dvergr can't jump by default, which
        // leaves them stuck shuffling at the foot of slopes, build pieces, and
        // raised terrain. A periodic, LOW hop lets them clear small ledges without
        // the floaty high jump. NEEDS IN-GAME VERIFICATION — eases the worst cases,
        // not a full pathfinding rewrite.
        private void ApplyMovementTweaks()
        {
            if (_ai != null && _ai.m_jumpInterval <= 0f) _ai.m_jumpInterval = 8f;
            if (_character != null) _character.m_jumpForce *= 0.5f; // shorter hop
        }

        // While true (chore-assigned) the companion is passive like Standby: no
        // proactive target acquisition — only retaliation. ChoreAI toggles it.
        public bool ChoreActive { get; set; }

        // Inventory status flags, set each tick by CompanionInventoryAI (on the
        // ZDO-owner client) and read by the name-badge / status-icon patches.
        //  * IsEncumbered — over the pack weight cap (req 13). Any client can also
        //    derive this straight from the replicated container weight.
        //  * IsFed — a food HP buff is currently active (req 10).
        public bool IsEncumbered { get; set; }
        public bool IsFed { get; set; }

        // The icon of the food currently buffing this companion (its item icon),
        // shown by the status-icon patch while IsFed. Set by CompanionInventoryAI.
        public Sprite FedIcon { get; set; }

        // Suppresses/restores proactive target acquisition by zeroing the alert
        // range (passive) or restoring the captured base.
        public void SetPassive(bool passive)
        {
            if (_ai == null) return;
            _ai.m_alertRange = passive ? 0f : (_baseAlertRangeCaptured ? _baseAlertRange : _ai.m_alertRange);
        }

        private bool _encumbranceActive;

        // req 13: an over-weight ally won't attack but can still move. Enforced
        // every frame (from CompanionInventoryAI) so it truly stops fighting rather
        // than re-acquiring between 1 Hz ticks: drop any target, go passive (alert
        // range 0 so it can't acquire a new one), and don't retaliate. Restores the
        // correct stance behavior when the load drops back under the cap.
        public void ApplyEncumbrance(bool encumbered)
        {
            IsEncumbered = encumbered;
            if (_ai == null) return;

            if (encumbered)
            {
                if (!_baseAlertRangeCaptured) { _baseAlertRange = _ai.m_alertRange; _baseAlertRangeCaptured = true; }
                _ai.SetTarget(null);
                _ai.SetAlerted(false);
                _ai.m_alertRange = 0f;
                _encumbranceActive = true;
            }
            else if (_encumbranceActive)
            {
                _encumbranceActive = false;
                // A chore worker stays passive; otherwise return to the stance's range.
                if (ChoreActive) SetPassive(true);
                else RestoreAlertRangeForStance();
            }
        }

        // ---- Naming -----------------------------------------------------------

        public bool HasCustomName => !string.IsNullOrEmpty(_customName);

        // The name shown in hover text, the level badge, and chore tooltips. Falls
        // back to the vanilla localized creature name when unnamed.
        public string DisplayName => HasCustomName ? _customName : DefaultName();

        private string DefaultName()
        {
            if (_character != null && !string.IsNullOrEmpty(_character.m_name) && Localization.instance != null)
                return Localization.instance.Localize(_character.m_name);
            return "Dvergr";
        }

        public void SetName(string name)
        {
            _customName = string.IsNullOrWhiteSpace(name) ? null : name.Trim();
            if (_znv != null && _znv.IsValid())
                _znv.GetZDO().Set(ZdoKeyName, _customName ?? string.Empty);
        }

        // TextReceiver — driven by the vanilla rename text box (see
        // Plugin.HandleRenameInput). GetText prefills the box with the current
        // name; SetText commits the new one and the ally acknowledges it.
        public string GetText() => DisplayName;

        public void SetText(string text)
        {
            SetName(text);
            if (Chat.instance != null)
                Chat.instance.SetNpcText(gameObject, Vector3.up * 2.2f, 20f, 4f, string.Empty, $"I'll answer to {DisplayName} now.", false);
        }

        // ---- Ladder identity (docs/Ranking.md) --------------------------------

        // The current id (may be null for a legacy companion that never got one).
        // Reads the ZDO directly so a bystander client sees the replicated id even
        // when its own _companionId cache is empty.
        public string CompanionId
        {
            get
            {
                if (!string.IsNullOrEmpty(_companionId)) return _companionId;
                if (_znv != null && _znv.IsValid())
                {
                    var v = _znv.GetZDO().GetString(ZdoKeyCompanionId, null);
                    if (!string.IsNullOrEmpty(v)) _companionId = v;
                }
                return _companionId;
            }
        }

        // Returns the id, assigning + persisting a fresh GUID if there isn't one.
        public string EnsureCompanionId()
        {
            var existing = CompanionId;
            if (!string.IsNullOrEmpty(existing)) return existing;
            _companionId = System.Guid.NewGuid().ToString("N");
            if (_znv != null && _znv.IsValid()) _znv.GetZDO().Set(ZdoKeyCompanionId, _companionId);
            return _companionId;
        }

        // Adopt a specific id (used by the Communion Totem so a summoned companion
        // keeps its ladder record across seal/summon).
        public void SetCompanionId(string id)
        {
            if (string.IsNullOrEmpty(id)) return;
            _companionId = id;
            if (_znv != null && _znv.IsValid()) _znv.GetZDO().Set(ZdoKeyCompanionId, id);
        }

        public void SetOwner(long ownerId)
        {
            _ownerId = ownerId;
            if (_znv != null && _znv.IsValid()) _znv.GetZDO().Set(ZdoKeyOwner, ownerId);
        }

        // Persisted so the owner name tag (req 4) shows even when the owner is
        // offline or out of range.
        public void SetOwnerName(string ownerName)
        {
            _ownerName = string.IsNullOrWhiteSpace(ownerName) ? null : ownerName;
            if (_znv != null && _znv.IsValid()) _znv.GetZDO().Set(ZdoKeyOwnerName, _ownerName ?? string.Empty);
        }

        public bool IsOwner(Player player)
        {
            if (player == null) return false;
            if (_ownerId == 0L) return true; // legacy/unowned: anyone may command
            return player.GetPlayerID() == _ownerId;
        }

        public Player OwnerPlayer()
        {
            if (_ownerId == 0L) return Player.m_localPlayer;
            return Player.GetPlayer(_ownerId);
        }

        public bool IsOwnerNear(float range)
        {
            var owner = OwnerPlayer();
            return owner != null && Vector3.Distance(owner.transform.position, transform.position) <= range;
        }

        // Whether this companion currently treats a Character as an enemy. Read by
        // the BaseAI.IsEnemy patch so vanilla AI targets/attacks them normally.
        // Handles both players (feral/owner/guard/timed rules) and OTHER characters
        // — notably another player's companion, which settings 1 & 2 mark hostile
        // via the timed dictionary so a struck ally also turns on the aggressor's
        // companions.
        public bool IsHostileTo(Character other)
        {
            if (other == null) return false;
            if (other is Player player)
            {
                if (_feral) return true;                                      // butcher-knife betrayal: everyone, owner included
                if (IsOwner(player)) return false;                            // never the owner
                if (Stance == CompanionStance.Guard && _ownerId != 0L) return true; // guard: all others
            }
            return _hostileUntil.TryGetValue(other, out var until) && Time.time < until; // timed
        }

        public bool IsFeral => _feral;

        // Non-duel change: struck with a butcher knife, the companion turns on
        // all players (docs/Duel-Arena.md "Butcher-knife betrayal"). Distinct
        // from Retaliate — this is not owner-scoped and not timed; the ally is
        // lost until it dies. Any duel is abandoned; alert range is opened so it
        // proactively hunts even from a passive stance.
        public void GoFeral(Character attacker)
        {
            if (_feral) return;
            _feral = true;
            if (DuelMode) ExitDuelMode(DuelExitReason.OwnerStopped);
            if (PartyDuelMode) ExitPartyDuelMode(DuelExitReason.OwnerStopped);

            if (_ai != null)
            {
                if (!_baseAlertRangeCaptured) { _baseAlertRange = _ai.m_alertRange; _baseAlertRangeCaptured = true; }
                _ai.m_alertRange = _baseAlertRange * GuardAlertRangeMultiplier;
                if (attacker != null) _ai.SetTarget(attacker);
                _ai.SetAlerted(true);
            }

            Announce($"{DisplayName} turns on you, eyes wild with the old corruption!");
        }

        // Marks a player hostile for HostileDuration (retaliation / owner-attacked).
        public void MarkHostile(Character player)
        {
            if (player == null) return;
            _hostileUntil[player] = Time.time + HostileDuration;
        }

        // Retaliation: a non-owner player hit us — go hostile and engage now,
        // regardless of stance (even Standby defends itself).
        public void Retaliate(Character attacker)
        {
            MarkHostile(attacker);
            if (_ai != null)
            {
                _ai.SetTarget(attacker);
                _ai.SetAlerted(true);
            }
        }

        // Settings 1 & 2: turn this companion on every companion owned by
        // `ownerId` (the aggressor), so a struck ally also fights the attacker's
        // allies — not just the attacking player. Marked BOTH ways so it's a real
        // brawl: a companion-on-companion hit doesn't run the player-retaliation
        // path (the attacker is a companion, not a Player), so without the mutual
        // mark the aggressor's allies would never fight back. Timed like other
        // hostility.
        public void MarkOwnersCompanionsHostile(long ownerId)
        {
            if (ownerId == 0L) return;
            foreach (var other in s_all)
            {
                if (other == null || other == this || other._ownerId != ownerId) continue;
                if (other._character != null) MarkHostile(other._character);
                if (_character != null) other.MarkHostile(_character);
            }
        }

        // Setting 4: is there another player's companion close enough to challenge
        // to a duel? Drives the "[J] Duel" hover hint. Mirrors the rival rules in
        // FindNearestDuelRival (different, non-zero owner) but doesn't require
        // either side to already be in duel mode.
        public bool HasPotentialDuelRivalNearby()
        {
            if (_ownerId == 0L) return false;
            foreach (var other in s_all)
            {
                if (other == null || other == this) continue;
                if (other._ownerId == 0L || other._ownerId == _ownerId) continue;
                var oc = other._character;
                if (oc == null || oc.IsDead()) continue;
                if (Vector3.Distance(transform.position, other.transform.position) <= DuelDetectRange) return true;
            }
            return false;
        }

        private void Update()
        {
            // Duel mode overrides passive stances entirely (it drives its own
            // targeting below), so skip the target-drop while dueling.
            // In Standby OR while doing a chore the companion does nothing
            // proactively — drop any target that isn't a player it's actively
            // retaliating against, so it won't wander off to fight monsters.
            if (!InAnyDuelMode && (Stance == CompanionStance.Standby || ChoreActive) && _ai != null)
            {
                var tgt = _ai.GetTargetCreature();
                if (tgt != null && !(tgt is Player p && IsHostileTo(p))) _ai.SetTarget(null);
            }

            // Drive a duel only on the instance that owns the ZDO (also runs the
            // MonsterAI), so transitions/targeting aren't duplicated across clients.
            // Bystander clients just read the replicated flags.
            bool authority = _znv == null || !_znv.IsValid() || _znv.IsOwner();
            if (DuelMode && authority) TickDuel();
            else if (PartyDuelMode && authority) TickPartyDuel();

            // Occasionally prune expired hostility so the dictionary doesn't grow.
            _pruneTimer += Time.deltaTime;
            if (_pruneTimer >= 5f)
            {
                _pruneTimer = 0f;
                if (_hostileUntil.Count > 0)
                {
                    var now = Time.time;
                    var expired = new List<Character>();
                    foreach (var kv in _hostileUntil) if (kv.Value <= now) expired.Add(kv.Key);
                    foreach (var c in expired) _hostileUntil.Remove(c);
                }
            }
        }

        public void SetCaste(DvergrCaste caste)
        {
            Caste = caste;
            if (_znv != null && _znv.IsValid())
            {
                _znv.GetZDO().Set(ZdoKeyCaste, (int)caste);
            }
        }

        public void AddXp(float amount)
        {
            if (amount <= 0f) return;
            if (Level >= MaxLevel) return; // capped — no further XP or level-ups

            Xp += amount;

            while (Level < MaxLevel && Xp >= XpRequiredForNextLevel())
            {
                Xp -= XpRequiredForNextLevel();
                Level++;
                ApplyLevelToCharacter();

                Plugin.Log.LogInfo($"Dvergr {Caste} reached level {Level}.");
                ServerGuideBridge.RaiseLevelUp(Caste, Level);
            }

            // At the cap there is no "next level" to fill toward, so drop any
            // leftover progress rather than showing a partial bar that never
            // completes.
            if (Level >= MaxLevel) Xp = 0f;

            Persist();
        }

        // Reuses vanilla's own star-tier scaling system (the same mechanism that
        // powers 1/2/3-star creatures) rather than hand-rolling stat multipliers —
        // see docs/Ally-Leveling.md "Why Not Vanilla 1/2/3-Star" for why this is
        // still the right call even though we're driving it with our own curve:
        // SetLevel is just the scaling mechanism, our XP curve is what drives it.
        private void ApplyLevelToCharacter()
        {
            if (_character == null) return;

            _character.SetLevel(Level);
            ApplyCasteBonus();
        }

        // Per-caste flavor bonus on top of SetLevel. Re-applied fresh on every
        // level-up and once on relog-restore; because SetLevel resets the scaled
        // stats first, multiplying here each call does NOT compound. No bonus at
        // level 1 (steps == 0).
        private void ApplyCasteBonus()
        {
            int steps = Level - 1;

            switch (Caste)
            {
                case DvergrCaste.Rogue:
                case DvergrCaste.FireMage:
                    // Capture the untouched prefab speeds once, before any change.
                    if (!_baseSpeedsCaptured)
                    {
                        _baseRunSpeed = _character.m_runSpeed;
                        _baseSpeed = _character.m_speed;
                        _baseWalkSpeed = _character.m_walkSpeed;
                        _baseSpeedsCaptured = true;
                    }
                    float speedMul = 1f + SpeedBonusPerLevel * steps;
                    _character.m_runSpeed = _baseRunSpeed * speedMul;
                    _character.m_speed = _baseSpeed * speedMul;
                    _character.m_walkSpeed = _baseWalkSpeed * speedMul;
                    break;

                case DvergrCaste.IceMage:
                case DvergrCaste.SupportMage:
                    // SetLevel just set max health to the level-scaled base;
                    // multiply that fresh so it never compounds across calls.
                    float scaledMax = _character.GetMaxHealth();
                    float newMax = scaledMax * (1f + HealthBonusPerLevel * steps);
                    _character.SetMaxHealth(newMax);
                    // Make the extra health immediately usable.
                    _character.SetHealth(Mathf.Min(_character.GetHealth() + (newMax - scaledMax), newMax));
                    break;
            }
        }

        private void Persist()
        {
            if (_znv != null && _znv.IsValid())
            {
                _znv.GetZDO().Set(ZdoKeyLevel, Level);
                _znv.GetZDO().Set(ZdoKeyXp, Xp);
            }
        }

        // Feature add: Follow/Guard command. In-memory only — like chore
        // assignment (see docs/Ally-Chores.md), this does not persist across
        // reload/relog; the companion resets to Follow on reload. Guard clears
        // the follow target, anchors a patrol point at the companion's current
        // position, and widens its alert range so it proactively engages threats
        // near its post instead of only what wanders directly into it.
        // (The earlier "Stay" stance — anchor without the widened alert range —
        // was removed as redundant; Guard is the single hold-position stance.)
        // NEEDS IN-GAME VERIFICATION that the patrol point actually keeps it
        // from wandering off, and that widening m_alertRange doesn't have side
        // effects that weren't anticipated from static analysis alone.
        public void SetStance(CompanionStance stance, GameObject followTarget)
        {
            Stance = stance;
            AnnounceCapability();

            if (_ai == null) return;

            if (!_baseAlertRangeCaptured)
            {
                _baseAlertRange = _ai.m_alertRange;
                _baseAlertRangeCaptured = true;
            }

            switch (stance)
            {
                case CompanionStance.Follow:
                    _ai.m_alertRange = _baseAlertRange;
                    _ai.SetFollowTarget(followTarget);
                    break;

                case CompanionStance.Guard:
                    _ai.m_alertRange = _baseAlertRange * GuardAlertRangeMultiplier;
                    _ai.SetFollowTarget(null);
                    _ai.SetPatrolPoint(transform.position);
                    break;

                case CompanionStance.Standby:
                    // Passive: no proactive acquisition (alert range 0), holds
                    // position. Only retaliation (Retaliate) makes it fight.
                    _ai.m_alertRange = StandbyAlertRange;
                    _ai.SetFollowTarget(null);
                    _ai.SetPatrolPoint(transform.position);
                    _ai.SetTarget(null);
                    break;
            }
        }

        // Stance + caste "what I can do" speech bubble that replaces the vanilla
        // Dvergr chatter (which CommunionService disables on recruit). Shown on
        // recruit and on every stance change so the line always matches the
        // companion's current posture. Vanilla NPC speech system (no new assets).
        public void AnnounceCapability()
        {
            if (Chat.instance == null || _character == null) return;

            string skill;
            switch (Caste)
            {
                case DvergrCaste.FireMage:    skill = "tend smelters, kilns and forges"; break;
                case DvergrCaste.IceMage:     skill = "run the refineries"; break;
                case DvergrCaste.SupportMage: skill = "farm, cook, brew and tend the beasts"; break;
                default:                      skill = "haul loose drops to a chest"; break; // Rogue
            }

            string line;
            switch (Stance)
            {
                case CompanionStance.Guard:
                    line = $"I'll hold this ground. Or set me to {skill}.";
                    break;
                case CompanionStance.Standby:
                    line = $"I'll wait here, quietly. Set me to {skill} when you're ready.";
                    break;
                default: // Follow
                    line = $"I'll follow and fight at your side. Set me to {skill}.";
                    break;
            }

            Chat.instance.SetNpcText(gameObject, Vector3.up * 2.2f, 20f, 6f, string.Empty, line, false);
        }

        // Spoken once when the companion climbs aboard the owner's ship (see
        // ShipRideAI). Reuses the vanilla NPC speech bubble — no new assets.
        public void AnnounceBoarded()
        {
            if (Chat.instance == null || _character == null) return;
            Chat.instance.SetNpcText(gameObject, Vector3.up * 2.2f, 20f, 5f, string.Empty,
                "Aboard — I'll take a seat and hold fast.", false);
        }

        // ---- Duel mode --------------------------------------------------------

        // Owner toggled duel mode ON (req 1). Refused while on a chore. Opens the
        // alert range and alerts the AI so it proactively seeks a rival duelist;
        // actual target selection is left to vanilla MonsterAI + the IsEnemy
        // patch (which only ever lets it see other-owner duel-mode companions).
        public bool EnterDuelMode()
        {
            if (DuelMode) return true;
            if (ChoreActive || _feral || PartyDuelMode) return false;

            DuelMode = true;
            _duelEngaged = false;
            _duelResolved = false; // fresh bout — allow exactly one subdue
            _duelWaitTimer = 0f;
            _duelScanTimer = 0f;

            if (_ai != null)
            {
                if (!_baseAlertRangeCaptured) { _baseAlertRange = _ai.m_alertRange; _baseAlertRangeCaptured = true; }
                _ai.m_alertRange = DuelDetectRange;
                _ai.SetAlerted(true);
            }

            Announce($"{DisplayName} squares up — challenging rival companions to a duel.");
            return true;
        }

        public enum DuelExitReason { Defeated, NoOpponents, OwnerGone, OwnerStopped }

        // Leaves duel mode and restores the alert range appropriate to the
        // current stance. Idempotent. The message differs by reason so the
        // notifications called for in reqs 3 & 5 read correctly.
        public void ExitDuelMode(DuelExitReason reason)
        {
            if (!DuelMode) return;
            DuelMode = false;
            _duelEngaged = false;

            if (_ai != null)
            {
                _ai.SetTarget(null);
                _ai.SetAlerted(false);
                RestoreAlertRangeForStance();
            }

            switch (reason)
            {
                case DuelExitReason.Defeated:
                    Announce($"{DisplayName} is subdued and yields the duel."); break;
                case DuelExitReason.NoOpponents:
                    Announce($"{DisplayName} finds no more challengers and stands down."); break;
                case DuelExitReason.OwnerGone:
                    Announce($"{DisplayName} loses sight of its owner and breaks off the duel."); break;
                case DuelExitReason.OwnerStopped:
                    Announce($"{DisplayName} lowers its guard and leaves the sparring circle."); break;
            }
        }

        // Idempotent subdue resolution — the single entry point the damage prefix
        // uses when a duelist is knocked to the HP floor. Guarantees a bout
        // resolves exactly once even if several near-simultaneous hits (or a regen
        // tick nudging HP back above the floor mid-swing) reach the prefix before
        // the ExitDuelMode flag replicates. Returns true only on the first call;
        // the caller should skip the damage regardless.
        public bool ResolveSubdue(DvergrCompanion winner)
        {
            if (_duelResolved) return false;
            _duelResolved = true;
            winner?.AwardDuelWin(this);
            ExitDuelMode(DuelExitReason.Defeated);
            return true;
        }

        // Winner reward path: flat XP bonus + a ServerGuide event. Called from
        // ResolveSubdue (never directly from the damage patch) so the double-win
        // guard is always in force.
        public void AwardDuelWin(DvergrCompanion loser)
        {
            AddXp(DuelWinXpBonus);
            ServerGuideBridge.RaiseDuelWon(Caste, loser != null ? loser.Caste : Caste);

            // Ranking (docs/Ranking.md): report the result to the server-authoritative
            // ladder. Only rank real, different-owner bouts (the same rule the duel
            // targeting already enforces); a legacy ally with no stable id / owner is
            // skipped. The loser's id is read from its replicated ZDO (assigned at
            // recruit), so we don't need to own its ZDO here.
            if (loser != null && _ownerId != 0L && loser._ownerId != 0L && _ownerId != loser._ownerId)
            {
                var winnerId = EnsureCompanionId();
                var loserId = loser.CompanionId;
                if (!string.IsNullOrEmpty(winnerId) && !string.IsNullOrEmpty(loserId))
                {
                    Ranking.LeaderboardSync.ReportDuel(new Ranking.DuelResult
                    {
                        WinnerId = winnerId, WinnerOwnerId = _ownerId, WinnerOwnerName = OwnerName ?? string.Empty,
                        WinnerName = DisplayName, WinnerCaste = (int)Caste,
                        LoserId = loserId, LoserOwnerId = loser._ownerId, LoserOwnerName = loser.OwnerName ?? string.Empty,
                        LoserName = loser.DisplayName, LoserCaste = (int)loser.Caste,
                    });
                }
            }
            Announce($"{DisplayName} wins the bout! (+{DuelWinXpBonus:0} XP)");

            // Setting 3: broadcast the result to everyone via a chat shout. This
            // runs on the winner-companion's client (the attacker-side damage
            // prefix), so SendText shouts as that client's local player and the
            // line reaches all players' chat windows.
            if (Chat.instance != null)
            {
                string winnerOwner = OwnerName;
                string loserName = loser != null ? loser.DisplayName : "its rival";
                string tag = string.IsNullOrEmpty(winnerOwner) ? DisplayName : $"{DisplayName} ({winnerOwner})";
                Chat.instance.SendText(Talker.Type.Shout, $"{tag} wins the duel against {loserName}!");
            }
        }

        // Per-second duel bookkeeping (called from Update while DuelMode):
        //  - req 5: owner logged out or left vision range -> stand down.
        //  - req 1/2: lock onto the nearest rival duelist if we have no valid target.
        //  - req 3: once a rival has been seen, standing down when none remain.
        //           Before ever seeing one, a wait-timeout prevents idling forever.
        private void TickDuel()
        {
            var owner = OwnerPlayer();
            if (owner == null || Vector3.Distance(owner.transform.position, transform.position) > OwnerVisionRange)
            {
                ExitDuelMode(DuelExitReason.OwnerGone);
                return;
            }

            _duelScanTimer += Time.deltaTime;
            if (_duelScanTimer < DuelScanInterval) return;
            _duelScanTimer = 0f;

            var rival = FindNearestDuelRival();
            if (rival != null)
            {
                _duelEngaged = true;
                _duelWaitTimer = 0f;
                if (_ai != null)
                {
                    var cur = _ai.GetTargetCreature();
                    if (cur == null || cur.IsDead() || cur.GetComponent<DvergrCompanion>() != rival)
                    {
                        _ai.SetTarget(rival.GetComponent<Character>());
                        _ai.SetAlerted(true);
                    }
                }
                return;
            }

            // No rival in range right now.
            if (_duelEngaged)
            {
                ExitDuelMode(DuelExitReason.NoOpponents);
                return;
            }
            _duelWaitTimer += DuelScanInterval;
            if (_duelWaitTimer >= DuelWaitTimeout) ExitDuelMode(DuelExitReason.NoOpponents);
        }

        private DvergrCompanion FindNearestDuelRival()
        {
            DvergrCompanion best = null;
            float bestDist = float.MaxValue;
            foreach (var other in s_all)
            {
                if (other == null || other == this || !other.DuelMode) continue;
                if (other._ownerId == _ownerId) continue;          // never a same-owner ally
                if (_ownerId == 0L || other._ownerId == 0L) continue; // legacy/unowned can't duel
                var oc = other._character;
                if (oc == null || oc.IsDead()) continue;
                float d = Vector3.Distance(transform.position, other.transform.position);
                if (d <= DuelDetectRange && d < bestDist) { bestDist = d; best = other; }
            }
            return best;
        }

        // ---- Party-duel mode (docs/Party-Duels.md) ----------------------------

        // Owner toggled this companion into the team fight. Refused while on a
        // chore, feral, or already in a 1v1 duel. Targeting is left entirely to
        // vanilla MonsterAI + the IsEnemy patch (which lets it see every OTHER
        // player's party-duel companion), so a team member freely engages any
        // enemy — no single-rival lock like 1v1.
        public bool EnterPartyDuelMode()
        {
            if (PartyDuelMode) return true;
            if (ChoreActive || _feral || DuelMode) return false;

            PartyDuelMode = true;
            _duelResolved = false;   // fresh bout — allow exactly one bench
            _partyEngaged = false;
            _partyWaitTimer = 0f;
            _partyScanTimer = 0f;
            _partyPeakEnemies = 0;
            _partyAllySnap.Clear();
            _partyEnemySnap.Clear();
            _partyOpponentOwner = 0L;
            _partyOpponentOwnerName = null;

            if (_ai != null)
            {
                if (!_baseAlertRangeCaptured) { _baseAlertRange = _ai.m_alertRange; _baseAlertRangeCaptured = true; }
                _ai.m_alertRange = DuelDetectRange;
                _ai.SetAlerted(true);
            }

            Announce($"{DisplayName} joins the melee — a party duel!");
            return true;
        }

        // Leaves party-duel mode and restores the stance's alert range. Idempotent.
        // Reason drives the notification; Defeated = benched (subdued), the others
        // = the bout ended for this member.
        public void ExitPartyDuelMode(DuelExitReason reason)
        {
            if (!PartyDuelMode) return;
            PartyDuelMode = false;
            _partyEngaged = false;

            if (_ai != null)
            {
                _ai.SetTarget(null);
                _ai.SetAlerted(false);
                RestoreAlertRangeForStance();
            }

            switch (reason)
            {
                case DuelExitReason.Defeated:
                    Announce($"{DisplayName} is subdued and steps out of the melee."); break;
                case DuelExitReason.NoOpponents:
                    Announce($"{DisplayName}'s side stands victorious!"); break;
                case DuelExitReason.OwnerGone:
                    Announce($"{DisplayName} loses sight of its owner and withdraws."); break;
                case DuelExitReason.OwnerStopped:
                    Announce($"{DisplayName} lowers its guard and leaves the melee."); break;
            }
        }

        // Idempotent bench on subdue — the party counterpart to ResolveSubdue. A
        // benched member leaves party mode (so it's no longer an enemy to anyone
        // and stops fighting), but the MATCH continues; the surviving side wins by
        // attrition when no enemy remains (see TickPartyDuel). No direct XP award
        // here — winners are paid at their victorious stand-down.
        public bool ResolvePartySubdue()
        {
            if (_duelResolved) return false;
            _duelResolved = true;
            ExitPartyDuelMode(DuelExitReason.Defeated);
            return true;
        }

        // Per-second party-duel bookkeeping (authority instance only):
        //  - owner leash: owner gone / >vision range -> this member forfeits.
        //  - lock onto the nearest enemy team member when idle (vanilla AI then
        //    fights it; any enemy is valid, not one fixed rival).
        //  - win by attrition: once engaged, if NO enemy-team party-duel companion
        //    remains anywhere loaded, this surviving member stands down a winner
        //    and is paid team-size-scaled XP. A never-engaged member times out.
        private void TickPartyDuel()
        {
            var owner = OwnerPlayer();
            if (owner == null || Vector3.Distance(owner.transform.position, transform.position) > OwnerVisionRange)
            {
                ExitPartyDuelMode(DuelExitReason.OwnerGone);
                return;
            }

            _partyScanTimer += Time.deltaTime;
            if (_partyScanTimer < DuelScanInterval) return;
            _partyScanTimer = 0f;

            int enemies = ScanParty();
            if (enemies > _partyPeakEnemies) _partyPeakEnemies = enemies;

            if (enemies > 0)
            {
                _partyEngaged = true;
                _partyWaitTimer = 0f;
                if (_ai != null)
                {
                    var cur = _ai.GetTargetCreature();
                    if (cur == null || cur.IsDead() || cur.GetComponent<DvergrCompanion>() == null
                        || !cur.GetComponent<DvergrCompanion>().PartyDuelMode)
                    {
                        var rival = FindNearestPartyRival();
                        if (rival != null) { _ai.SetTarget(rival.GetComponent<Character>()); _ai.SetAlerted(true); }
                    }
                }
                return;
            }

            // No enemy team members left.
            if (_partyEngaged)
            {
                AwardPartyWin();
                ExitPartyDuelMode(DuelExitReason.NoOpponents);
                return;
            }
            _partyWaitTimer += DuelScanInterval;
            if (_partyWaitTimer >= DuelWaitTimeout) ExitPartyDuelMode(DuelExitReason.NoOpponents);
        }

        // Winner payout at a victorious stand-down. XP is scaled by team sizes so a
        // bigger, riskier win pays more per head and winning by outnumbering pays
        // less: base * loserTeamSize / winnerTeamSize (both floored at 1). The
        // loser team is gone by now, so its size is the accumulated enemy roster
        // (fallback: peak enemy count). Every surviving winner is paid its own XP,
        // but the ladder report + ServerGuide event fire ONCE per match (static
        // latch — all winners share this owner/authority).
        private void AwardPartyWin()
        {
            int winnerSize = Mathf.Max(1, _partyAllySnap.Count);
            int loserSize = Mathf.Max(1, _partyEnemySnap.Count > 0 ? _partyEnemySnap.Count : _partyPeakEnemies);
            float xp = DuelWinXpBonus * loserSize / winnerSize;
            AddXp(xp);
            Announce($"{DisplayName} wins the party duel! (+{xp:0} XP)");

            // Once-per-match: report to the party ladder (Phase D) and fire the
            // ServerGuide party-win event, deduped by the owner pair.
            if (_partyOpponentOwner == 0L || _ownerId == 0L) return;
            var key = _ownerId <= _partyOpponentOwner
                ? $"{_ownerId}|{_partyOpponentOwner}" : $"{_partyOpponentOwner}|{_ownerId}";
            if (s_partyReportLatch.TryGetValue(key, out var t) && Time.time - t < PartyReportWindow) return;
            s_partyReportLatch[key] = Time.time;

            var result = new Ranking.PartyDuelResult
            {
                WinnerOwnerId = _ownerId,
                WinnerOwnerName = OwnerName ?? string.Empty,
                LoserOwnerId = _partyOpponentOwner,
                LoserOwnerName = _partyOpponentOwnerName ?? string.Empty,
                MvpCaste = (int)Caste,
                WinnerMembers = SnapToList(_partyAllySnap),
                LoserMembers = SnapToList(_partyEnemySnap),
            };
            Ranking.LeaderboardSync.ReportPartyDuel(result);
            ServerGuideBridge.RaisePartyDuelWon(OwnerName ?? string.Empty, winnerSize,
                _partyOpponentOwnerName ?? string.Empty, (int)Caste);
        }

        private static List<Ranking.PartyMemberSnap> SnapToList(Dictionary<string, string> snap)
        {
            var list = new List<Ranking.PartyMemberSnap>(snap.Count);
            foreach (var kv in snap)
            {
                var parts = kv.Value.Split(':');
                int caste = 0, level = 1;
                if (parts.Length >= 2) { int.TryParse(parts[0], out caste); int.TryParse(parts[1], out level); }
                list.Add(new Ranking.PartyMemberSnap { companionId = kv.Key, caste = caste, level = level });
            }
            return list;
        }

        // Single scan over the loaded companion set: accumulates BOTH team rosters
        // (so the winner can report them after the losers are benched) and returns
        // the count of enemy-team members still active. No range filter on the
        // count — a temporarily-distant enemy still means the match is ongoing;
        // only a fully gone/benched side ends it.
        private int ScanParty()
        {
            // Always keep self in the ally roster.
            var myId = CompanionId;
            if (!string.IsNullOrEmpty(myId)) _partyAllySnap[myId] = $"{(int)Caste}:{Level}";

            int enemies = 0;
            foreach (var other in s_all)
            {
                if (other == null || other == this || !other.PartyDuelMode) continue;
                if (_ownerId == 0L || other._ownerId == 0L) continue;
                var oc = other._character;
                if (oc == null || oc.IsDead()) continue;

                var oid = other.CompanionId;
                if (other._ownerId == _ownerId)
                {
                    if (!string.IsNullOrEmpty(oid)) _partyAllySnap[oid] = $"{(int)other.Caste}:{other.Level}";
                }
                else
                {
                    enemies++;
                    if (!string.IsNullOrEmpty(oid)) _partyEnemySnap[oid] = $"{(int)other.Caste}:{other.Level}";
                    if (_partyOpponentOwner == 0L)
                    {
                        _partyOpponentOwner = other._ownerId;
                        _partyOpponentOwnerName = other.OwnerName;
                    }
                }
            }
            return enemies;
        }

        private DvergrCompanion FindNearestPartyRival()
        {
            DvergrCompanion best = null;
            float bestDist = float.MaxValue;
            foreach (var other in s_all)
            {
                if (other == null || other == this || !other.PartyDuelMode) continue;
                if (other._ownerId == _ownerId) continue;
                if (_ownerId == 0L || other._ownerId == 0L) continue;
                var oc = other._character;
                if (oc == null || oc.IsDead()) continue;
                float d = Vector3.Distance(transform.position, other.transform.position);
                if (d < bestDist) { bestDist = d; best = other; }
            }
            return best;
        }

        // Drives the "[K] Party duel" hover hint — another player's companion is
        // near enough that gathering a team here would find a fight.
        public bool HasPotentialPartyRivalNearby() => HasPotentialDuelRivalNearby();

        private void RestoreAlertRangeForStance()
        {
            if (_ai == null) return;
            float baseRange = _baseAlertRangeCaptured ? _baseAlertRange : _ai.m_alertRange;
            switch (Stance)
            {
                case CompanionStance.Guard:   _ai.m_alertRange = baseRange * GuardAlertRangeMultiplier; break;
                case CompanionStance.Standby: _ai.m_alertRange = StandbyAlertRange; break;
                default:                      _ai.m_alertRange = baseRange; break;
            }
        }

        // Chat/speech notification for duel + feral state changes. Shows a speech
        // bubble above the companion (visible to everyone nearby) and, when the
        // local player is this companion's owner, a center HUD message too.
        private void Announce(string line)
        {
            if (_character == null) return;
            if (Chat.instance != null)
                Chat.instance.SetNpcText(gameObject, Vector3.up * 2.2f, 20f, 5f, string.Empty, line, false);
            if (MessageHud.instance != null && Player.m_localPlayer != null && IsOwner(Player.m_localPlayer))
                MessageHud.instance.ShowMessage(MessageHud.MessageType.Center, line);
        }
    }

    public enum CompanionStance
    {
        Follow,
        Guard,
        Standby
    }
}
