using UnityEngine;

namespace LostScrollsII.Companions
{
    // Resting at camp patches up your companions: while the owner is genuinely
    // resting — sitting by a campfire, or under a roof with a fire lit — every
    // Follow-stance ally standing with them regenerates health.
    //
    // The signal is the vanilla **`Resting`** status effect, not `Rested`. Those
    // are two different things and the distinction matters here: `Resting` is the
    // LIVE state (Player.UpdateEnvStatusEffects adds it while near a fire and
    // either sitting or sheltered, and removes it the moment you stand up and walk
    // off), while `Rested` is the lingering buff that state accrues and which
    // survives ~5+ minutes of travelling. Keying off `Rested` would have kept
    // healing allies halfway across the map; keying off `Resting` means the healing
    // starts and stops exactly with the camp, which is what was asked for.
    //
    // Runs on the OWNER's client. That is deliberate: the rest status is computed
    // locally and is not reliably replicated to other machines, so the owner's
    // client is the only place the condition can be read honestly. Healing is safe
    // from there because Character.Heal routes to the companion's ZDO owner over
    // RPC_Heal (and clamps to max HP) — the same reason mead feeding was moved onto
    // it. Vanilla assets only; no new prefabs, no new status effect.
    public class CompanionRestedHeal : MonoBehaviour
    {
        // Matches vanilla's own creature-regeneration cadence (BaseAI ticks health
        // regen every 2 s). Also keeps the Heal RPC traffic down in multiplayer.
        private const float TickSeconds = 2f;

        private float _timer;

        private void Update()
        {
            _timer += Time.deltaTime;
            if (_timer < TickSeconds) return;
            float elapsed = _timer;
            _timer = 0f;

            var player = Player.m_localPlayer;
            if (player == null) return;

            float healSeconds = Plugin.RestedHealSeconds != null ? Plugin.RestedHealSeconds.Value : 0f;
            float radius = Plugin.RestedHealRadius != null ? Plugin.RestedHealRadius.Value : 10f;

            var seman = player.GetSEMan();
            bool camped = healSeconds > 0f && seman != null
                          && seman.HaveStatusEffect(SEMan.s_statusEffectResting);

            long ownerId = player.GetPlayerID();

            foreach (var comp in DvergrCompanion.All)
            {
                if (comp == null) continue;
                // Only ever touch this player's own allies — every client runs this
                // for its own, so nobody sets a flag on somebody else's companion.
                if (comp.OwnerId == 0L || comp.OwnerId != ownerId) continue;

                var ch = comp.GetComponent<Character>();
                if (ch == null || ch.IsDead()) { comp.IsResting = false; continue; }

                bool resting = camped
                    && comp.Stance == CompanionStance.Follow
                    && !comp.ChoreActive && !comp.InAnyDuelMode && !comp.IsFeral
                    && Vector3.Distance(ch.transform.position, player.transform.position) <= radius;

                comp.IsResting = resting;
                if (!resting) continue;

                float max = ch.GetMaxHealth();
                if (ch.GetHealth() >= max) continue;   // don't spend an RPC on a full ally

                // Heal a fixed fraction of the ally's own pool, so a high-level
                // companion with a bigger pool still takes the same time to mend.
                ch.Heal(max / healSeconds * elapsed, false);
            }
        }
    }
}
