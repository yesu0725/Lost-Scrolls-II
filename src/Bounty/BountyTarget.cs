using LostScrollsII.Companions;
using UnityEngine;

namespace LostScrollsII.Bounty
{
    // Marks a spawned bounty creature — the target itself or one of its escort —
    // and keeps it hostile and correctly scaled (docs/Bounty-Hunting.md, Phase C).
    //
    // Two things this must survive: a relog, and ownership changing hands as players
    // come and go. Both are handled by keeping the truth on the ZDO and re-deriving
    // everything else from it on Awake:
    //  - the extra health multiplier is NOT persisted by vanilla (only the star level
    //    is), so it's re-applied every load, preserving damage already taken;
    //  - aggression is AI state that resets, so it's re-asserted on a slow tick.
    //
    // Requirement 1 (a bounty target may be communed instead of killed) needs no code
    // here: these are ordinary Dvergr prefabs, so the existing Communion Rite already
    // works once one is subdued. This component simply stands down the moment the
    // creature becomes a companion, so it can't keep forcing an ally hostile.
    public class BountyTarget : MonoBehaviour
    {
        public const string ZdoKeyBountyId = "DE_BountyId";
        public const string ZdoKeyBountyTier = "DE_BountyTier";
        public const string ZdoKeyBountyMinion = "DE_BountyMinion";
        // Set once the first time a target is scaled, so later loads preserve the
        // damage it has taken instead of topping it back up to full.
        public const string ZdoKeyBountyInit = "DE_BountyInit";

        // How often aggression is re-asserted. Slow on purpose — this is a safety net
        // against vanilla calming the creature down, not a per-frame override.
        private const float EnforceInterval = 3f;

        private Character _character;
        private MonsterAI _ai;
        private ZNetView _znv;
        private float _timer;
        private bool _standDown;

        public string BountyId { get; private set; }
        public int Tier { get; private set; } = 1;
        public bool IsMinion { get; private set; }

        private void Awake()
        {
            _character = GetComponent<Character>();
            _ai = GetComponent<MonsterAI>();
            _znv = GetComponent<ZNetView>();

            if (_znv == null || !_znv.IsValid()) { enabled = false; return; }

            var zdo = _znv.GetZDO();
            BountyId = zdo.GetString(ZdoKeyBountyId, null);
            Tier = zdo.GetInt(ZdoKeyBountyTier, 1);
            IsMinion = zdo.GetBool(ZdoKeyBountyMinion, false);

            if (string.IsNullOrEmpty(BountyId)) { enabled = false; return; }

            ApplyScaling();
            ApplyAggression();
        }

        private void Update()
        {
            if (_standDown) return;

            // Everything below is a periodic safety net, so gate on the timer FIRST —
            // a GetComponent per frame per bounty creature (a target plus up to six
            // escorts) is pure waste.
            _timer += Time.deltaTime;
            if (_timer < EnforceInterval) return;
            _timer = 0f;

            // Freed by the Communion Rite — it's an ally now, so stop touching it.
            if (_character == null || _character.IsDead() || GetComponent<DvergrCompanion>() != null)
            {
                StandDown();
                return;
            }

            ApplyAggression();
        }

        // Health scaling on top of the vanilla star level. SetLevel has already been
        // applied (at spawn, and re-applied by vanilla from the persisted level on
        // load), so multiplying the CURRENT max fresh each time can't compound —
        // the same trick DvergrCompanion.ApplyCasteBonus uses.
        private void ApplyScaling()
        {
            if (_character == null || _znv == null || !_znv.IsOwner()) return;

            var zdo = _znv.GetZDO();
            float mult = IsMinion
                ? BountyTiers.HealthMultiplier(BountyTiers.MinionTier(Tier))
                : BountyTiers.HealthMultiplier(Tier);

            float wounded = _character.GetHealth();
            bool firstTime = !zdo.GetBool(ZdoKeyBountyInit, false);

            _character.SetMaxHealth(_character.GetMaxHealth() * mult);

            if (firstTime)
            {
                // Fresh spawn: fill the newly enlarged pool.
                _character.SetHealth(_character.GetMaxHealth());
                zdo.Set(ZdoKeyBountyInit, true);
            }
            else
            {
                // Reload: keep the damage the hunters already did. Without this a
                // bounty would heal to full every time its zone reloaded.
                _character.SetHealth(Mathf.Min(wounded, _character.GetMaxHealth()));
            }
        }

        // These Dvergr hunt rather than wait to be provoked — the opposite of every
        // other Dvergr in the mod, which is neutral until aggravated.
        private void ApplyAggression()
        {
            if (_ai == null) return;

            _ai.m_aggravatable = true;
            if (!_ai.IsAggravated())
                _ai.SetAggravated(true, BaseAI.AggravatedReason.Damage);
            _ai.SetHuntPlayer(true);

            // Notice hunters from further off, so a bounty reads as actively hunting
            // rather than something to be stumbled over.
            float mult = Mathf.Max(1f, Plugin.BountyAlertRangeMultiplier?.Value ?? 2f);
            if (_baseAlertRange <= 0f) _baseAlertRange = _ai.m_alertRange;
            _ai.m_alertRange = _baseAlertRange * mult;

            // Keep it near its posting so the map pin stays meaningful — vanilla's own
            // roam limit, tightened, rather than a hand-rolled leash.
            float roam = Mathf.Max(1f, Plugin.BountyRoamRadius?.Value ?? 12f);
            if (_ai.m_randomMoveRange > roam) _ai.m_randomMoveRange = roam;
        }

        private float _baseAlertRange;

        private void StandDown()
        {
            _standDown = true;
            enabled = false;
        }

        // ---- Helpers used by the spawner and (later) the resolution path --------

        public static bool IsBounty(Character c)
            => c != null && c.GetComponent<BountyTarget>() != null;

        // Reads the bounty id straight off a character's ZDO — works even if the
        // component hasn't woken yet, and on a client that doesn't own the creature.
        public static string BountyIdOf(Character c)
        {
            var znv = c != null ? c.GetComponent<ZNetView>() : null;
            if (znv == null || !znv.IsValid()) return null;
            return znv.GetZDO().GetString(ZdoKeyBountyId, null);
        }

        // Stamps the ZDO before the component reads it, so Awake picks everything up
        // (the same ordering CommunionService.SpawnRecruited relies on).
        public static void Stamp(GameObject go, string bountyId, int tier, bool minion)
        {
            var znv = go != null ? go.GetComponent<ZNetView>() : null;
            if (znv == null || !znv.IsValid()) return;
            var zdo = znv.GetZDO();
            zdo.Set(ZdoKeyBountyId, bountyId ?? string.Empty);
            zdo.Set(ZdoKeyBountyTier, tier);
            zdo.Set(ZdoKeyBountyMinion, minion);
        }
    }
}
