using UnityEngine;

namespace LostScrollsII.Companions
{
    // The Communion Rite as a CHANNELED STRUGGLE, not an instant keypress
    // (docs/Ally-Recruitment.md). Pressing the Communion key on a subdued Dvergr
    // no longer recruits it outright — it BEGINS a rite the corruption fights
    // back against. The player must hold the key, stay close, and survive for
    // the channel duration; letting go, walking off, or taking a hit lets the
    // shadow reclaim the Dvergr (it re-aggravates and must be re-subdued).
    //
    // Vanilla-assets-only and deliberately MINIMAL: no progress bar and no smoke.
    // The only ambient effect during the channel is the vanilla WISHBONE ping
    // ripple, pulsed on both the Dvergr and the communing player. We play the ping
    // EFFECT directly (SE_Finder.m_pingEffectNear) rather than applying the wishbone
    // status effect, because that status effect only pulses when near buried
    // treasure — with none around it shows nothing. The pulse ACCELERATES as the
    // rite nears completion (interval lerps from slow → fast), so the quickening
    // ping reads as progress without needing a bar. Other feedback: a few
    // center-message beats, plus one small spawn poof on success.
    //
    // Lives as a component on the plugin GameObject (persists across scene
    // loads), driven by its own Update so it can watch the held key and the
    // fail conditions every frame independently of Plugin.Update's input gates.
    public class CommunionRite : MonoBehaviour
    {
        // Cross-client soft lock: set true on the target's ZDO while a rite is in
        // progress so a second player can't channel the same Dvergr (and so other
        // players' hover text can show "Communion in progress"). Always cleared on
        // every end path; harmless if ever left stale (it only blocks re-recruit).
        public const string ZdoKeyCommuning = "DE_Communing";

        public static CommunionRite Instance { get; private set; }

        // Interval between "the corruption writhes" lash-out beats.
        private const float LashInterval = 1.25f;
        // How much of a health drop counts as "took damage" (ignore rounding noise).
        private const float DamageEpsilon = 0.5f;
        // The rite is channeled by HOLDING the vanilla Block button (so the player
        // keeps their shield up and can still block/dodge through the struggle).
        // Blocking and dodging both ride on this one button, and a dodge roll can
        // briefly drop it — so a short release-grace window keeps the rite alive
        // through a roll instead of shattering the instant Block reads false.
        private const float ReleaseGraceSeconds = 0.5f;

        // Wishbone-ping pulse cadence: slow at the start of the rite, fast at the
        // end. The accelerating ping is the progress cue (in place of a bar).
        private const float PulseIntervalStart = 1.0f;
        private const float PulseIntervalEnd = 0.28f;

        private Character _target;
        private Player _player;
        private float _elapsed;
        private float _lashTimer;
        private float _lastPlayerHealth;
        private float _releaseGrace;
        private float _pulseTimer;
        private int _lashIndex;

        // Cached vanilla Wishbone equip status effect + its ping ripple effect,
        // resolved lazily from the Wishbone item in ObjectDB.
        private StatusEffect _wishboneSE;
        private EffectList _pingEffect;

        private static readonly string[] LashLines =
        {
            "The corruption writhes — hold fast.",
            "It fights the light within.",
            "The shadow claws to keep its hold.",
            "Damon's grip will not loosen easily.",
        };

        // True while THIS client is mid-channel (guards re-entry in Plugin).
        public bool IsActive => _target != null;

        private void Awake()
        {
            Instance = this;
        }

        // Begins the rite on a freshly-hovered subdued Dvergr. Returns false (with a
        // reason message) if it can't start; the caller has already confirmed the
        // target is a subdued Dvergr.
        public bool Begin(Character target, Player player)
        {
            if (target == null || player == null) return false;
            if (_target != null) return false; // already channeling something

            var znv = target.GetComponent<ZNetView>();
            if (znv == null || !znv.IsValid()) return false;

            // Respect another client's in-progress rite on this Dvergr.
            if (znv.GetZDO().GetBool(ZdoKeyCommuning))
            {
                MessageHud.instance.ShowMessage(MessageHud.MessageType.Center,
                    "Another is already in communion with it.");
                return false;
            }

            // Own the ZDO so our lock flag + any re-aggravation on failure replicate.
            znv.ClaimOwnership();
            znv.GetZDO().Set(ZdoKeyCommuning, true);

            _target = target;
            _player = player;
            _elapsed = 0f;
            _lashTimer = 0f;
            _lashIndex = 0;
            _releaseGrace = 0f;
            _pulseTimer = 0f;
            _lastPlayerHealth = player.GetHealth();

            // Minimal ambient effect: the Wishbone ping ripple on both participants,
            // fired once now and then pulsed (accelerating) from Update. No cleanup
            // needed — these are transient one-shot effects, not a persisted SE.
            PlayPulse(player);
            PlayPulse(target);

            MessageHud.instance.ShowMessage(MessageHud.MessageType.Center,
                "You reach for the soul beneath the corruption…");
            return true;
        }

        private void Update()
        {
            if (_target == null) return;

            // Target gone (died, unloaded, or turned into something else) → break.
            if (_target == null || _target.IsDead() || _player == null)
            {
                Fail("The rite is broken.");
                return;
            }

            // Stopped holding Block → the player let the rite slip. A brief release
            // is forgiven (ReleaseGraceSeconds) so a dodge roll — which shares the
            // Block button — doesn't shatter the rite. We only READ the button, so
            // blocking and dodging keep working normally underneath the channel.
            if (BlockHeld())
            {
                _releaseGrace = 0f;
            }
            else
            {
                _releaseGrace += Time.deltaTime;
                if (_releaseGrace >= ReleaseGraceSeconds)
                {
                    Fail("You lower your guard — the shadow reclaims it!");
                    return;
                }
            }

            // Walked too far from the Dvergr → the connection snaps.
            float maxDist = Mathf.Max(1f, Plugin.CommunionMaxDistance.Value);
            if (Vector3.Distance(_player.transform.position, _target.transform.position) > maxDist)
            {
                Fail("You stray too far — the rite shatters!");
                return;
            }

            // Took a hit while channeling → concentration breaks.
            float hp = _player.GetHealth();
            if (Plugin.CommunionBreakOnDamage.Value && hp < _lastPlayerHealth - DamageEpsilon)
            {
                Fail("Struck mid-rite — the shadow reclaims it!");
                return;
            }
            _lastPlayerHealth = hp;

            // The corruption is no longer subdued? (regen or a heal pushed it back up.)
            // We don't re-gate on the threshold every frame — Begin already confirmed
            // it — but if it somehow fully recovers, drop the rite.
            if (_target.GetHealthPercentage() > 0.5f)
            {
                Fail("It tears free of your grasp!");
                return;
            }

            _elapsed += Time.deltaTime;

            // Wishbone ping ripple, pulsed on both participants — the interval
            // shortens as the rite progresses, so the quickening ping IS the
            // progress cue (in place of a bar).
            float progress = Mathf.Clamp01(_elapsed / Mathf.Max(0.1f, Plugin.CommunionChannelSeconds.Value));
            float pulseInterval = Mathf.Lerp(PulseIntervalStart, PulseIntervalEnd, progress);
            _pulseTimer += Time.deltaTime;
            if (_pulseTimer >= pulseInterval)
            {
                _pulseTimer = 0f;
                PlayPulse(_target);
                PlayPulse(_player);
            }

            // Lash-out beats — the corruption fights back at intervals. Text only
            // (no VFX) to keep the rite visually minimal.
            _lashTimer += Time.deltaTime;
            if (_lashTimer >= LashInterval)
            {
                _lashTimer = 0f;
                MessageHud.instance.ShowMessage(MessageHud.MessageType.Center,
                    LashLines[_lashIndex % LashLines.Length]);
                _lashIndex++;
            }

            if (_elapsed >= Mathf.Max(0.1f, Plugin.CommunionChannelSeconds.Value))
            {
                Succeed();
            }
        }

        private void Succeed()
        {
            var target = _target;
            var player = _player;
            ClearLock(target);
            Reset();

            var caste = CommunionService.DetectCaste(target);
            Plugin.Log.LogInfo($"[recruit] rite complete — '{target.name}' detected as caste {caste}.");
            if (CommunionService.TryRecruit(target, player, caste))
            {
                TotemConversionService.PlaySummonVfx(target.transform.position + Vector3.up);
                MessageHud.instance.ShowMessage(MessageHud.MessageType.Center,
                    $"The shadow's grip loosens — a {caste.Display()} joins you.");
            }
            else
            {
                MessageHud.instance.ShowMessage(MessageHud.MessageType.Center,
                    "The rite falters at the last moment.");
            }
        }

        private void Fail(string message)
        {
            var target = _target;
            var player = _player;
            ClearLock(target);
            Reset();
            if (target == null) return;

            // The shadow reclaims it: the Dvergr breaks free and turns hostile again,
            // so the player must re-subdue (or survive) before trying once more.
            var ai = target.GetComponent<MonsterAI>();
            if (ai != null)
            {
                ai.SetAggravated(true, BaseAI.AggravatedReason.Damage);
                ai.SetAlerted(true);
                if (player != null) ai.SetTarget(player.GetComponent<Character>());
            }

            MessageHud.instance.ShowMessage(MessageHud.MessageType.Center, message);
        }

        private static void ClearLock(Character target)
        {
            if (target == null) return;
            var znv = target.GetComponent<ZNetView>();
            if (znv != null && znv.IsValid() && znv.IsOwner())
            {
                znv.GetZDO().Set(ZdoKeyCommuning, false);
            }
        }

        private void Reset()
        {
            _target = null;
            _player = null;
            _elapsed = 0f;
            _lashTimer = 0f;
            _lashIndex = 0;
            _releaseGrace = 0f;
        }

        // True while the vanilla Block button is held. Using the named ZInput
        // button (not a hard KeyCode) follows whatever the player has bound Block
        // to — mouse, key, or gamepad — and is exactly the input blocking/dodging
        // already use, so holding it raises the shield as normal.
        public static bool BlockHeld()
        {
            return ZInput.instance != null && ZInput.GetButton("Block");
        }

        // The vanilla Wishbone equip status effect, read off the Wishbone item in
        // ObjectDB and cached. It's an SE_Finder, whose ping ripple we borrow.
        private StatusEffect WishboneSE()
        {
            if (_wishboneSE != null) return _wishboneSE;
            if (ObjectDB.instance == null) return null;
            var prefab = ObjectDB.instance.GetItemPrefab("Wishbone");
            var drop = prefab != null ? prefab.GetComponent<ItemDrop>() : null;
            if (drop != null && drop.m_itemData != null && drop.m_itemData.m_shared != null)
                _wishboneSE = drop.m_itemData.m_shared.m_equipStatusEffect;
            return _wishboneSE;
        }

        // The Wishbone's "near" ping ripple (the strong sonar pulse). Played
        // directly so it shows without any buried treasure around.
        private EffectList PingEffect()
        {
            if (_pingEffect != null) return _pingEffect;
            var finder = WishboneSE() as SE_Finder;
            if (finder != null) _pingEffect = finder.m_pingEffectNear;
            return _pingEffect;
        }

        // One-shot the ping ripple at a character's position (transient VFX/SFX,
        // no persistent state to clean up).
        private void PlayPulse(Character c)
        {
            if (c == null) return;
            PlayPulseAt(c.transform.position);
        }

        // Same ripple at an arbitrary point. Public so the Dead Raiser sealing rite
        // (SealingRite) can borrow the identical accelerating ping rather than
        // resolving the Wishbone effect a second time — the two rites deliberately
        // read the same way to the player.
        public void PlayPulseAt(Vector3 pos)
        {
            var fx = PingEffect();
            if (fx != null) fx.Create(pos, Quaternion.identity);
        }
    }
}
