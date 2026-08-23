using System.Collections;
using System.Linq;
using System.Text;
using UnityEngine;
using LostScrollsII.Economy;
using LostScrollsII.Ranking;

namespace LostScrollsII.Companions
{
    // Player-facing tournament board (docs/Tournaments.md, docs/Wagers.md). Covers
    // the whole lifecycle a player sees: current phase, who's registered, the live
    // bracket and pairings, the champion — and the staked duel-invite board.
    //
    // The read-only status view is rendered with the vanilla TextViewer "Rune"
    // panel (same as RankingBoard). The INTERACTIVE surface — locking a companion's
    // Communion Totem into a slot, opening a staked tournament, posting/accepting a
    // duel invite, and the admin controls — is the separate InventoryGui-based
    // panel (TournamentRegistration), opened from here.
    //
    // Every tournament AND the invite board are synced to every client
    // (LeaderboardSync pushes both snapshots), so this view only reads them. Several
    // tournaments can be live at once — the free admin one plus one per wager
    // currency — and each is rendered in turn.
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

            // This is a one-shot render of the synced snapshots, which only update
            // when a server broadcast happens to have already landed. Unlike the F7
            // registration panel (which polls every frame and self-heals), a client
            // that missed/hadn't yet received the latest push would show a stale
            // bracket indefinitely. Ask the server for fresh copies first and give
            // the round trip a brief moment before rendering.
            LeaderboardSync.RequestTournament();
            LeaderboardSync.RequestInvites();
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
            var sb = new StringBuilder();
            sb.Append("<align=left>");

            // Several tournaments can run at once now (the free admin one plus one
            // per wager currency), so the board renders every active bracket in turn
            // rather than assuming a single state.
            var all = TournamentService.All.ToList();
            if (all.Count == 0)
            {
                sb.Append("<size=140%><color=#FFD24A>No tournament is running.</color></size>\n\n");
                sb.Append("<color=#AAAAAA>Any player can open one from the Tournament panel (");
                sb.Append($"[{Plugin.TournamentUiKey.Value}]");
                sb.Append(") by paying the entry stake — one Coin tournament and one Valcoin tournament may run at a time. ");
                sb.Append("Admins can also start a free one. When registration opens, lock a companion's Communion Totem into a slot to enter.</color>\n");
            }
            else
            {
                bool first = true;
                foreach (var s in all)
                {
                    if (!first) sb.Append("\n<color=#555555>--------------------</color>\n\n");
                    first = false;
                    AppendTournament(sb, s);
                }
            }

            AppendInvites(sb, player);
            sb.Append("</align>");
            return sb.ToString();
        }

        private static void AppendTournament(StringBuilder sb, TournamentState s)
        {
            string mode = s.mode == "party" ? "Party" : "1v1";
            string type = TypeLabel(s.eliminationType);
            var currency = Wager.Parse(s.currency);
            string stake = s.entryFee > 0
                ? $"  <color=#B8F5B0>{s.entryFee} {Wager.Display(currency)} entry, {s.prize} to the champion</color>"
                : "";
            sb.Append($"<size=150%><color=#FFD24A>{Title(s)}</color></size>  <color=#AAAAAA>({s.phase} — {mode}, {type})</color>{stake}\n");
            if (s.entryFee > 0 && !string.IsNullOrEmpty(s.hostName))
                sb.Append($"<color=#AAAAAA>Opened by {s.hostName}</color>\n");
            sb.Append("\n");

            if (s.phase == "registration")
            {
                int cap = s.size > 0 ? s.size : 0;
                sb.Append($"<color=#FFFFFF>Registration is open</color> — {s.entrants.Count}");
                sb.Append(cap > 0 ? $" / {cap} entrants:\n" : " entrants:\n");
                AppendEntrants(sb, s);
                sb.Append("\n<color=#AAAAAA>Enter by locking a companion's Communion Totem into a slot (open the registration panel from this board). ");
                sb.Append(s.entryFee > 0
                    ? "The bracket begins automatically the moment it fills; if it never fills, every stake and totem is returned.</color>\n"
                    : "An admin begins the bracket when everyone's in.</color>\n");
            }
            else if (s.phase == "running")
            {
                sb.Append($"<color=#FFFFFF>Round {s.currentRound}</color> — the bracket is live:\n");
                AppendBracket(sb, s);
                sb.Append("\n");
                AppendStandings(sb, s);
                sb.Append(s.entryFee > 0
                    ? "\n<color=#AAAAAA>You and your opponent pick where to fight: meet up anywhere, then both press Ready to Fight on the Tournament panel. Your companions are summoned there, at full health.</color>\n"
                    : "\n<color=#AAAAAA>Fight your pairing to advance. An admin activates each round.</color>\n");
            }
            else if (s.phase == "complete")
            {
                string champ = string.IsNullOrEmpty(s.championLabel) ? "?" : s.championLabel;
                sb.Append($"<size=140%><color=#FFD24A>Champion: {champ}</color></size>\n\n");
                AppendStandings(sb, s);
                sb.Append("\n<color=#AAAAAA>See the Hall of Champions with de_champions.</color>\n");
            }
        }

        // The staked duel-invite board shares this panel: it answers the same "who
        // can I fight, and for what" question, and giving it a second full-screen
        // surface for a handful of lines would be worse than a section here.
        private static void AppendInvites(StringBuilder sb, Player player)
        {
            var invites = DuelInviteService.All.ToList();
            if (invites.Count == 0) return;

            sb.Append("\n<color=#555555>--------------------</color>\n\n");
            sb.Append("<size=150%><color=#FFD24A>Duel Invites</color></size>\n\n");
            long me = player != null ? player.GetPlayerID() : 0L;
            foreach (var i in invites)
            {
                var currency = Wager.Display(Wager.Parse(i.currency));
                string mine = i.Involves(me) ? " <color=#B8F5B0>(yours)</color>" : "";
                string state;
                switch (i.phase)
                {
                    case "matched": state = $"<color=#FFD24A>{i.oppLabel} ({i.oppName}) accepted — waiting for both to be ready</color>"; break;
                    case "running": state = "<color=#E06666>fighting now</color>"; break;
                    case "complete": state = "<color=#AAAAAA>settled</color>"; break;
                    default: state = "<color=#8FE3FF>open — anyone may accept</color>"; break;
                }
                sb.Append($"  {LabelWithLevel(i.hostLabel, i.hostLevel)} <color=#AAAAAA>({i.hostName})</color>{mine}\n");
                sb.Append($"     stake <color=#B8F5B0>{i.stake} {currency}</color>, winner takes <color=#B8F5B0>{i.stake * 2}</color> — {state}\n");
            }
            sb.Append("\n<color=#AAAAAA>Post or accept an invite from the Tournament panel. You may only be in one invite at a time, and the two of you choose where to fight.</color>\n");
        }

        private static string Title(TournamentState s)
        {
            var currency = Wager.Parse(s.currency);
            return currency == WagerCurrency.None
                ? "Server Tournament"
                : $"{Wager.Display(currency)} Tournament";
        }

        private static void AppendEntrants(StringBuilder sb, TournamentState s)
        {
            if (s.entrants.Count == 0) { sb.Append("<color=#AAAAAA>  (none yet)</color>\n"); return; }
            foreach (var e in s.entrants)
            {
                string label = string.IsNullOrEmpty(e.label) ? "?" : e.label;
                string owner = string.IsNullOrEmpty(e.ownerName) ? "?" : e.ownerName;
                string lvl = e.level > 0 ? $" <color=#FFD24A>Lv{e.level}</color>" : "";
                sb.Append($"  {label}{lvl} <color=#AAAAAA>({owner})</color>  <color=#8FE3FF>{e.seedRating}</color>\n");
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
                    string res;
                    if (!string.IsNullOrEmpty(m.winnerId))
                        res = $"<color=#B8F5B0>winner: {(m.winnerId == m.aId ? m.aLabel : m.bLabel)}</color>";
                    else if (m.activated)
                        res = "<color=#E06666>fighting</color>";
                    else
                        res = $"<color=#AAAAAA>ready: {(m.aReady ? "yes" : "no")} / {(m.bReady ? "yes" : "no")}</color>";
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

        private static string TypeLabel(string eliminationType)
        {
            switch (eliminationType)
            {
                case "double": return "double elim";
                case "round_robin": return "round robin";
                default: return "single elim";
            }
        }
    }
}
