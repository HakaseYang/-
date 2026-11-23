using System.Collections.Generic;

namespace QiuQiu
{
    // who is still alive and where the player places on the board.
    //
    // Counting is per LOGICAL participant, not per cell: one AI that a virus burst into
    // sixteen cells is still one opponent. SizeController.BotId is the identity used for that.
    public static class ScoreBoard
    {
        // how many living cells one participant owns. A virus burst has to respect this number:
        // the cell limit belongs to the owner, not to each individual cell.
        public static int CellsOfBot(Team team, int botId)
        {
            int count = 0;
            var all = SizeController.Registry;

            for (int i = 0; i < all.Count; i++)
            {
                var ball = all[i];
                if (ball == null || ball.IsDead || ball.Team != team)
                {
                    continue;
                }

                int id = ball.BotId >= 0 ? ball.BotId : -1 - ball.GetInstanceID();
                if (id == botId)
                {
                    count++;
                }
            }

            return count;
        }

        // number of living BOTS of a team: each BotId counts once, however many cells it has
        public static int AliveBots(Team team)
        {
            Dictionary<int, float> totals = BotMasses(team);
            return totals.Count;
        }

        // total mass per logical participant. Cells with the same BotId are added together,
        // a cell without an id (negative) is treated as its own participant.
        public static Dictionary<int, float> BotMasses(Team team)
        {
            var totals = new Dictionary<int, float>();
            var all = SizeController.Registry;

            for (int i = 0; i < all.Count; i++)
            {
                var ball = all[i];
                if (ball == null || ball.IsDead || ball.Team != team)
                {
                    continue;
                }

                int id = ball.BotId >= 0 ? ball.BotId : -1 - ball.GetInstanceID();

                float current;
                totals.TryGetValue(id, out current);
                totals[id] = current + ball.Mass;
            }

            return totals;
        }

        // rank 1 = the biggest participant; totalPlayers stays fixed for the whole round,
        // so a dead AI never shrinks the denominator the player is scored against
        public static void Rank(float playerMass, int totalPlayers, out int rank, Team opponents = Team.Enemy)
        {
            Dictionary<int, float> totals = BotMasses(opponents);

            int better = 0;
            foreach (var pair in totals)
            {
                if (pair.Value > playerMass)
                {
                    better++;
                }
            }

            rank = better + 1;
        }
    }
}
