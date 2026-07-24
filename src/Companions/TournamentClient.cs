using System.Collections.Generic;
using UnityEngine;
using LostScrollsII.Ranking;

namespace LostScrollsII.Companions
{
    // A tag put on a companion summoned for a tournament match, so the client can
    // find it again to reseal + despawn when the match resolves.
    public class TournamentCombatant : MonoBehaviour
    {
        public string EntrantId;
        public bool Party;
        public int Round;
    }

    // Client-side driver for the escrow tournament (docs/Tournaments.md — escrow &
    // auto-summon). Lives on the plugin GameObject (added in Plugin.Awake).
    //
    //  - SummonForMatch: on the owner's client, rebuild the escrowed companion(s)
    //    from their totem payload(s), place them by the owner, enter duel mode
    //    against the assigned opponent, and tag them.
    //  - Update (1 Hz): when a summoned entrant's match has resolved (read from the
    //    synced TournamentState) — or the tournament ended / the entrant was
    //    released — reseal the companion(s) back into escrow with their updated
    //    level/XP and despawn them. Each client only ever touches its OWN
    //    combatants (1v1: one; party: the whole team, sharing an entrant id), so no
    //    cross-client coordination is needed.
    public class TournamentClient : MonoBehaviour
    {
        private static TournamentClient _instance;
        private float _tick;

        private void Awake() => _instance = this;

        public static void SummonForMatch(Player owner, string entrantId, string mode,
            string opponentEntrantId, string opponentLabel, int round, List<string> payloads)
        {
            if (owner == null || payloads == null || payloads.Count == 0) return;

            bool party = mode == "party";
            long opponentOwner = 0L;
            if (party) long.TryParse(opponentEntrantId, out opponentOwner);

            int i = 0;
            int summoned = 0;
            foreach (var payload in payloads)
            {
                var totem = TotemConversionService.BuildTotemFromPayload(payload);
                if (totem == null) continue;

                // Spread members out beside the owner.
                Vector3 pos = owner.transform.position
                    + owner.transform.forward * 1.5f
                    + owner.transform.right * (i % 2 == 0 ? 1.4f : -1.4f) * (1 + i / 2);

                var go = TotemConversionService.SummonAt(owner, totem, pos);
                var comp = go != null ? go.GetComponent<DvergrCompanion>() : null;
                if (comp == null) continue;

                // Enter every match at full health regardless of the HP it was
                // sealed at (Heal, never SetHealth — see MeadFeedingService).
                var character = go.GetComponent<Character>();
                if (character != null) character.Heal(character.GetMaxHealth(), true);

                if (party)
                {
                    comp.DuelOpponentOwner = opponentOwner;
                    comp.EnterPartyDuelMode();
                }
                else
                {
                    comp.DuelOpponentId = opponentEntrantId;
                    comp.EnterDuelMode();
                }

                var tag = go.GetComponent<TournamentCombatant>() ?? go.AddComponent<TournamentCombatant>();
                tag.EntrantId = entrantId;
                tag.Party = party;
                tag.Round = round;
                i++; summoned++;
            }

            if (summoned > 0 && MessageHud.instance != null)
                MessageHud.instance.ShowMessage(MessageHud.MessageType.Center,
                    $"Your companion enters the arena — opponent: {opponentLabel}.");
        }

        private void Update()
        {
            _tick += Time.deltaTime;
            if (_tick < 1f) return;
            _tick = 0f;
            ResolveFinishedMatches();
        }

        // Group this client's combatants by entrant, and finish any whose match is
        // decided (or whose tournament/entry is gone).
        private static readonly Dictionary<string, List<DvergrCompanion>> _byEntrant = new Dictionary<string, List<DvergrCompanion>>();

        private void ResolveFinishedMatches()
        {
            _byEntrant.Clear();
            foreach (var c in DvergrCompanion.All)
            {
                if (c == null) continue;
                var tag = c.GetComponent<TournamentCombatant>();
                if (tag == null || string.IsNullOrEmpty(tag.EntrantId)) continue;
                if (!_byEntrant.TryGetValue(tag.EntrantId, out var list))
                {
                    list = new List<DvergrCompanion>();
                    _byEntrant[tag.EntrantId] = list;
                }
                list.Add(c);
            }
            if (_byEntrant.Count == 0) return;

            foreach (var kv in _byEntrant)
            {
                var entrantId = kv.Key;
                var members = kv.Value;
                int round = members[0].GetComponent<TournamentCombatant>()?.Round ?? 0;
                if (!MatchDecided(entrantId, round)) continue;

                // Reseal all members of this entrant with their current (leveled-up)
                // state, then despawn them back into escrow.
                var payloads = new List<string>();
                foreach (var c in members)
                {
                    var totem = TotemConversionService.CreateTotem(c);
                    if (totem != null) payloads.Add(TotemConversionService.SerializePayload(totem));
                }
                if (payloads.Count > 0) LeaderboardSync.SendReseal(entrantId, payloads);
                foreach (var c in members) c.DespawnToTotem();
            }
        }

        // A summoned entrant is "done" when the tournament ended, its entry is gone,
        // or its match in the summoned round now has a winner.
        private static bool MatchDecided(string entrantId, int round)
        {
            var s = TournamentService.Snapshot;
            if (s == null || !s.active || s.phase != "running") return true;
            if (s.Find(entrantId) == null) return true;
            foreach (var m in s.matches)
            {
                if (m.round != round) continue;
                if (m.aId == entrantId || m.bId == entrantId)
                    return !string.IsNullOrEmpty(m.winnerId);
            }
            return true; // no such match anymore
        }
    }
}
