using System.Text;
using UnityEngine;
using LostScrollsII.Ranking;

namespace LostScrollsII.Companions
{
    // Player-facing ranking board (docs/Ranking.md). A read-only view of the duel
    // and party ladders, opened with a hotkey.
    //
    // Rendered with the vanilla TextViewer "Rune" panel — the same darkened,
    // rich-text parchment ServerGuide uses for its rune/intro guidance
    // (GuidanceDisplay.ShowViewer). That keeps us inside the vanilla-assets-only
    // constraint with zero custom UI construction, and the player dismisses it the
    // usual way (Escape / interact), exactly like reading a runestone.
    //
    // The ladders are already synced to every client (LeaderboardSync pushes the
    // whole table), so this only reads LeaderboardStore's snapshot — no RPC needed.
    public static class RankingBoard
    {
        // How many rows to show per ladder (keeps the panel on one screen).
        private const int MaxRows = 12;

        public static void Open()
        {
            if (TextViewer.instance == null)
            {
                if (MessageHud.instance != null)
                    MessageHud.instance.ShowMessage(MessageHud.MessageType.Center, "Rankings aren't available right now.");
                return;
            }
            TextViewer.instance.ShowText(TextViewer.Style.Rune, "Dvergr Rankings", Build(), autoHide: false);
        }

        private static string Build()
        {
            var sb = new StringBuilder();
            sb.Append("<align=left>");

            // ---- 1v1 duel ladder ----
            sb.Append("<size=150%><color=#FFD24A>⚔ Duel Ladder</color></size>\n");
            var duels = LeaderboardStore.Ranked();
            if (duels.Count == 0)
            {
                sb.Append("<color=#AAAAAA>No duels recorded yet. Pit your companion against another player's with [J].</color>\n");
            }
            else
            {
                for (int i = 0; i < duels.Count && i < MaxRows; i++)
                {
                    var r = duels[i];
                    string name = string.IsNullOrEmpty(r.companionName) ? "?" : r.companionName;
                    string owner = string.IsNullOrEmpty(r.ownerName) ? "?" : r.ownerName;
                    string caste = ((DvergrCaste)r.caste).Display();
                    sb.Append($"<color=#FFFFFF>#{i + 1,-2}</color> ")
                      .Append($"<color=#8FE3FF>{r.rating,4}</color>  ")
                      .Append($"{name} <color=#AAAAAA>({owner} · {caste})</color>  ")
                      .Append($"<color=#B8F5B0>{r.wins}W</color>/<color=#F5B0B0>{r.losses}L</color>\n");
                }
            }

            // ---- party ladder ----
            sb.Append("\n<size=150%><color=#FFD24A>\U0001F6E1 Party Ladder</color></size>\n");
            var parties = LeaderboardStore.RankedParties();
            if (parties.Count == 0)
            {
                sb.Append("<color=#AAAAAA>No party duels recorded yet. Gather a team with [K]; name it with de_party_name.</color>\n");
            }
            else
            {
                for (int i = 0; i < parties.Count && i < MaxRows; i++)
                {
                    var r = parties[i];
                    string owner = string.IsNullOrEmpty(r.ownerName) ? "?" : r.ownerName;
                    string label = string.IsNullOrEmpty(r.partyName) ? owner : $"{r.partyName} <color=#AAAAAA>({owner})</color>";
                    int team = r.memberSnapshot != null ? r.memberSnapshot.Count : 0;
                    sb.Append($"<color=#FFFFFF>#{i + 1,-2}</color> ")
                      .Append($"<color=#8FE3FF>{r.rating,4}</color>  ")
                      .Append($"{label}  ")
                      .Append($"<color=#B8F5B0>{r.wins}W</color>/<color=#F5B0B0>{r.losses}L</color>  ")
                      .Append($"<color=#AAAAAA>team {team}</color>\n");
                }
            }

            sb.Append("</align>");
            return sb.ToString();
        }
    }
}
