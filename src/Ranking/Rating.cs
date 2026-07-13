using System;

namespace LostScrollsII.Ranking
{
    // Elo rating math for the duel ladders (docs/Ranking.md). Kept standalone and
    // pure so both the 1v1 ladder (Phase B) and the party ladder (Phase D) drive
    // it the same way. Applied server-side only.
    public static class Rating
    {
        // Every new record starts here.
        public const int StartRating = 1000;

        // Ratings never drop below this, so a losing streak can't run negative or
        // produce absurd expected-score gaps.
        public const int Floor = 100;

        // Expected score for A against B under the Elo model (0..1).
        public static double Expected(int a, int b)
            => 1.0 / (1.0 + Math.Pow(10.0, (b - a) / 400.0));

        // Returns the winner's and loser's new ratings for a decided bout.
        // kFactor controls volatility (higher = bigger swings). Standard is 32.
        public static void Apply(int winner, int loser, int kFactor, out int newWinner, out int newLoser)
        {
            double ew = Expected(winner, loser); // winner's expected score
            double el = 1.0 - ew;                // loser's expected score
            newWinner = winner + (int)Math.Round(kFactor * (1.0 - ew));
            newLoser = loser + (int)Math.Round(kFactor * (0.0 - el));
            if (newLoser < Floor) newLoser = Floor;
            if (newWinner < Floor) newWinner = Floor;
        }
    }
}
