using System.Collections;
using System.Linq;
using System.Text;
using UnityEngine;
using LostScrollsII.Ranking;

namespace LostScrollsII.Companions
{
    // Player-facing tournament board (docs/Tournaments.md). Covers the whole
    // lifecycle a player sees: current phase, who's registered, the live bracket
    // and pairings, and the champion.
    //
    // The read-only status view is rendered with the vanilla TextViewer "Rune"
    // panel (same as RankingBoard). The INTERACTIVE registration surface — locking
    // a companion's Communion Totem into a slot to enter, and the admin controls —
    // is a separate InventoryGui-based slot panel (TournamentRegistration), opened
    // from here; see Phase 4.
    //
    // TournamentState is already synced to every client (LeaderboardSync pushes the
    // snapshot), so the status view only reads TournamentService.Snapshot.
    public static class TournamentBoard
    {
        public static void Open(Player player)
        {
            if (TextViewer.instance == null)
            {
                if (MessageHud.instance != null)
                    MessageHud.instance.ShowMessage(MessageHud.MessageType.Center, "The tournament board isn't available right now.");
                return;
            }

            // This is a one-shot render of TournamentService.Snapshot, which only
            // updates when a server broadcast happens to have already landed. Unlike
            // the F7 registration panel (which polls every frame and self-heals),
            // a client that missed/hadn't yet received the latest push would show a
            // stale bracket indefinitely. Ask the server for a fresh copy first and
            // give the round trip a brief moment before rendering.
            LeaderboardSync.RequestTournament();
            if (Plugin.Instance != null) Plugin.Instance.StartCoroutine(ShowAfterSync(player));
            else Show(player);
        }

        private static IEnumerator ShowAfterSync(Player player)
        {
            yield return new WaitForSeconds(0.25f);
            Show(player);
        }

        private static void Show(Player player)
        {
            if (TextViewer.instance == null) return;
            TextViewer.instance.ShowText(TextViewer.Style.Rune, "Dvergr Tournament", Build(player), autoHide: false);
        }

        private static string Build(Player player)
        {
            var s = TournamentService.Snapshot;
            var sb = new StringBuilder();
            sb.Append("<align=left>");

            if (s == null || !s.active)
            {
                sb.Append("<size=140%><color=#FFD24A>No tournament is running.</color></size>\n\n");
                sb.Append("<color=#AAAAAA>An admin starts one with <color=#FFFFFF>de_tournament start &lt;1v1|party&gt; [size]</color>");
                sb.Append(" (or the Start control on this board). When registration opens, lock a companion's Communion Totem into a slot to enter.</color>");
                sb.Append("</align>");
                return sb.ToString();
            }

            string mode = s.mode == "party" ? "Party" : "1v1";
            string type = TypeLabel(s.eliminationType);
            sb.Append($"<size=150%><color=#FFD24A>⚔ {mode} Tournament</color></size>  <color=#AAAAAA>({s.phase} — {type})</color>\n\n");

            if (s.phase == "registration")
            {
                int cap = s.size > 0 ? s.size : 0;
                sb.Append($"<color=#FFFFFF>Registration is open</color> — {s.entrants.Count}");
                sb.Append(cap > 0 ? $" / {cap} entrants:\n" : " entrants:\n");
                AppendEntrants(sb, s);
                sb.Append("\n<color=#AAAAAA>Enter by locking a companion's Communion Totem into a slot (open the registration panel from this board). ");
                sb.Append("An admin begins the bracket when everyone's in.</color>");
            }
            else if (s.phase == "running")
            {
                sb.Append($"<color=#FFFFFF>Round {s.currentRound}</color> — the bracket is live:\n");
                AppendBracket(sb, s);
                sb.Append("\n");
                AppendStandings(sb, s);
                sb.Append("\n<color=#AAAAAA>Fight your pairing to advance. de_tournament bracket shows the full draw.</color>");
            }
            else if (s.phase == "complete")
            {
                string champ = string.IsNullOrEmpty(s.championLabel) ? "?" : s.championLabel;
                sb.Append($"<size=140%><color=#FFD24A>🏆 Champion: {champ}</color></size>\n\n");
                AppendStandings(sb, s);
                sb.Append("\n<color=#AAAAAA>See the Hall of Champions with de_champions.</color>");
            }

            sb.Append("</align>");
            return sb.ToString();
        }

        private static void AppendEntrants(StringBuilder sb, TournamentState s)
        {
            if (s.entrants.Count == 0) { sb.Append("<color=#AAAAAA>  (none yet)</color>\n"); return; }
            foreach (var e in s.entrants)
            {
                string label = string.IsNullOrEmpty(e.label) ? "?" : e.label;
                string owner = string.IsNullOrEmpty(e.ownerName) ? "?" : e.ownerName;
                string lvl = e.level > 0 ? $" <color=#FFD24A>Lv{e.level}</color>" : "";
                sb.Append($"  • {label}{lvl} <color=#AAAAAA>({owner})</color>  <color=#8FE3FF>{e.seedRating}</color>\n");
            }
        }

        private static void AppendBracket(StringBuilder sb, TournamentState s)
        {
            int maxRound = 0;
            foreach (var m in s.matches) if (m.round > maxRound) maxRound = m.round;
            bool showBracketTag = s.eliminationType == "double";
            for (int r = 1; r <= maxRound; r++)
            {
                sb.Append($"<color=#FFD24A>-- Round {r} --</color>\n");
                foreach (var m in s.matches)
                {
                    if (m.round != r) continue;
                    string tag = showBracketTag ? $"<color=#AAAAAA>[{m.bracket}]</color> " : "";
                    string a = LabelWithLevel(m.aLabel, m.aLevel);
                    string b = string.IsNullOrEmpty(m.bId) ? "(bye)" : LabelWithLevel(m.bLabel, m.bLevel);
                    string res = string.IsNullOrEmpty(m.winnerId)
                        ? "<color=#AAAAAA>pending</color>"
                        : $"<color=#B8F5B0>winner: {(m.winnerId == m.aId ? m.aLabel : m.bLabel)}</color>";
                    sb.Append($"  {tag}{a} <color=#AAAAAA>vs</color> {b} — {res}\n");
                }
            }
        }

        // Current score standing: each entrant's live W-L record, so players can see
        // where they stand mid-tournament without doing the bracket math themselves.
        private static void AppendStandings(StringBuilder sb, TournamentState s)
        {
            if (s.entrants.Count == 0) return;
            sb.Append("<color=#FFD24A>-- Standings --</color>\n");

            System.Collections.Generic.IEnumerable<TournamentEntrant> ordered = s.eliminationType == "round_robin"
                ? s.entrants.OrderByDescending(e => e.wins).ThenBy(e => e.losses).ThenByDescending(e => e.seedRating)
                : s.entrants.OrderBy(e => e.eliminated ? 1 : 0).ThenByDescending(e => e.wins).ThenBy(e => e.losses);

            foreach (var e in ordered)
            {
                string label = string.IsNullOrEmpty(e.label) ? "?" : e.label;
                string record = $"{e.wins}-{e.losses}";
                string status = e.eliminated ? " <color=#E06666>(eliminated)</color>"
                    : (e.entrantId == s.championId ? " <color=#FFD24A>(champion)</color>" : "");
                sb.Append($"  {label} <color=#8FE3FF>{record}</color>{status}\n");
            }
        }

        private static string LabelWithLevel(string label, int level)
            => level > 0 ? $"{label} <color=#FFD24A>Lv{level}</color>" : label;

        private static string TypeLabel(string eliminationType) => eliminationType switch
        {
            "double" => "double elim",
            "round_robin" => "round robin",
            _ => "single elim",
        };
    }
}
