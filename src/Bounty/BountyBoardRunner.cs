using UnityEngine;

namespace LostScrollsII.Bounty
{
    // Drives the Wanted Board at runtime (docs/Bounty-Hunting.md, Phase F).
    //
    // Two jobs, split by who can do them:
    //  * SERVER — keep the board stocked with open postings.
    //  * HUNTER'S CLIENT — pin the posting it accepted, and spawn the creatures when
    //    it actually gets there. The spawn has to happen client-side because Valheim
    //    doesn't simulate unloaded zones: a camp instantiated from the server while
    //    nobody is nearby would sit frozen (or worse, unload immediately). Spawning
    //    on arrival also means a posting costs nothing until someone goes for it.
    //
    // Lives on the plugin GameObject, so it survives scene loads like the other
    // long-lived components.
    public class BountyBoardRunner : MonoBehaviour
    {
        private const float TickInterval = 2f;

        private float _timer;
        private string _pinnedPostingId;

        private void Update()
        {
            _timer += Time.deltaTime;
            if (_timer < TickInterval) return;
            _timer = 0f;

            if (BountyFeatureGate.IsServerAuthority)
            {
                // EnsurePostings both retires stale postings and refills the board;
                // push to clients whenever either happened.
                if (BountyFeatureGate.IsEnabled && BountyBoardStore.EnsurePostings())
                    BountySync.BroadcastBoard();
            }

            UpdateLocalHunter();
        }

        // The local player's own accepted posting: keep a pin on it, and spawn the
        // bounty once they're close enough for the zone to be live.
        private void UpdateLocalHunter()
        {
            var lp = Player.m_localPlayer;
            if (lp == null || !BountyFeatureGate.AvailableToLocalPlayer)
            {
                ClearPin();
                return;
            }

            // The Haldor dialogue hands off by granting a player key (Phase G) — pick
            // it up here and ask the server for the commission posting. Consumed so a
            // relog can't request a second one.
            if (BountyQuestGate.HasPendingStart(lp) && BountyQuestGate.ConsumeStartRequest(lp))
                BountySync.SendCommissionRequest(lp.transform.position);

            var posting = BountyBoardStore.AcceptedBy(lp.GetPlayerID());
            if (posting == null)
            {
                ClearPin();
                return;
            }

            // Pin (requirement 6). Only the hunter's own posting is pinned — pinning
            // every open posting would bury the map, and the board panel already
            // lists them with distances.
            if (_pinnedPostingId != posting.id)
            {
                ClearPin();
                _pinnedPostingId = posting.id;
            }
            BountyMapPin.Show(posting.id, posting.Label, posting.Position);

            if (posting.spawned) return;

            float radius = Mathf.Max(16f, Plugin.BountyArrivalRadius?.Value ?? 80f);
            var flat = new Vector3(lp.transform.position.x, 0f, lp.transform.position.z);
            var target = new Vector3(posting.x, 0f, posting.z);
            if (Vector3.Distance(flat, target) > radius) return;

            // Close enough that the zone is loaded — put the camp on the ground.
            var pos = posting.Position;
            if (ZoneSystem.instance != null && ZoneSystem.instance.GetSolidHeight(pos, out float h))
                pos.y = h;

            var spawned = BountySpawner.Spawn(posting.id, posting.tier, pos, posting.Biome);
            if (spawned == null) return;

            // Tell the server so it records the spawn and a second arrival (or a
            // relog on the way in) can't stack a duplicate camp on the same posting.
            BountySync.SendSpawned(posting.id);

            if (MessageHud.instance != null)
                MessageHud.instance.ShowMessage(MessageHud.MessageType.Center,
                    $"You have found the {BountyTiers.TierName(posting.tier)} one. It knows you are here.");
        }

        private void ClearPin()
        {
            if (string.IsNullOrEmpty(_pinnedPostingId)) return;
            BountyMapPin.Remove(_pinnedPostingId);
            _pinnedPostingId = null;
        }
    }
}
