namespace QiuQiu
{
    // who is still alive and where the player places on the board
    public static class ScoreBoard
    {
        public static int AliveCount(Team team)
        {
            int count = 0;
            var all = SizeController.Registry;

            for (int i = 0; i < all.Count; i++)
            {
                var ball = all[i];
                if (ball == null || ball.IsDead)
                {
                    continue;
                }
                if (ball.Team == team)
                {
                    count++;
                }
            }

            return count;
        }

        // rank 1 = the biggest ball; participants counts every opponent still alive
        public static void Rank(float playerMass, out int rank, out int participants)
        {
            int alive = 0;
            int better = 0;
            var all = SizeController.Registry;

            for (int i = 0; i < all.Count; i++)
            {
                var ball = all[i];
                if (ball == null || ball.IsDead || ball.Team == Team.Player)
                {
                    continue;
                }

                alive++;

                if (ball.Mass > playerMass)
                {
                    better++;
                }
            }

            rank = better + 1;
            participants = alive + 1;
        }
    }
}
