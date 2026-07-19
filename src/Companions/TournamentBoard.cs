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
            sb.Append($"<size=150%><color=#FFD24A>⚔ {mode} Tournament</color></size>  <color=#AAAAAA>({s.phase})</color>\n\n");

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
                sb.Append("\n<color=#AAAAAA>Fight your pairing to advance. de_tournament bracket shows the full draw.</color>");
            }
            else if (s.phase == "complete")
            {
                string champ = string.IsNullOrEmpty(s.championLabel) ? "?" : s.championLabel;
                sb.Append($"<size=140%><color=#FFD24A>🏆 Champion: {champ}</color></size>\n\n");
                sb.Append("<color=#AAAAAA>See the Hall of Champions with de_champions.</color>");
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
                sb.Append($"  • {label} <color=#AAAAAA>({owner})</color>  <color=#8FE3FF>{e.seedRating}</color>\n");
            }
        }

        private static void AppendBracket(StringBuilder sb, TournamentState s)
        {
            int maxRound = 0;
            foreach (var m in s.matches) if (m.round > maxRound) maxRound = m.round;
            for (int r = 1; r <= maxRound; r++)
            {
                sb.Append($"<color=#FFD24A>-- Round {r} --</color>\n");
                foreach (var m in s.matches)
                {
                    if (m.round != r) continue;
                    string b = string.IsNullOrEmpty(m.bId) ? "(bye)" : m.bLabel;
                    string res = string.IsNullOrEmpty(m.winnerId)
                        ? "<color=#AAAAAA>pending</color>"
                        : $"<color=#B8F5B0>winner: {(m.winnerId == m.aId ? m.aLabel : m.bLabel)}</color>";
                    sb.Append($"  {m.aLabel} <color=#AAAAAA>vs</color> {b} — {res}\n");
                }
            }
        }
    }
}
