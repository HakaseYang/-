using System.Collections.Generic;

namespace QiuQiu
{
    public static class ScoreBoard
    {
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

        public static int AliveBots(Team team)
        {
            Dictionary<int, float> totals = BotMasses(team);
            return totals.Count;
        }

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
