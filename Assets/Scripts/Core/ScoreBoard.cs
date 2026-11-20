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

        // rank 1 = the biggest ball; totalPlayers stays fixed for the whole round,
        // so a dead AI never shrinks the denominator the player is scored against
        public static void Rank(float playerMass, int totalPlayers, out int rank)
        {
            int better = 0;
            var all = SizeController.Registry;

            for (int i = 0; i < all.Count; i++)
            {
                var ball = all[i];
                if (ball == null || ball.IsDead || ball.Team == Team.Player)
                {
                    continue;
                }

                if (ball.Mass > playerMass)
                {
                    better++;
                }
            }

            rank = better + 1;
        }
    }
}
