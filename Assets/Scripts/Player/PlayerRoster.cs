using System.Collections.Generic;

namespace QiuQiu
{
    // owns the balls the player controls and remembers which one is the main ball.
    public class PlayerRoster
    {
        private readonly List<SizeController> balls = new List<SizeController>(8);
        private SizeController main;

        public IReadOnlyList<SizeController> Balls { get { return balls; } }

        public int Count { get { return balls.Count; } }

        public SizeController Main { get { return main; } }

        public float TotalMass
        {
            get
            {
                float total = 0f;
                for (int i = 0; i < balls.Count; i++)
                {
                    SizeController ball = balls[i];
                    if (ball != null && !ball.IsDead)
                    {
                        total += ball.Mass;
                    }
                }
                return total;
            }
        }

        public bool Contains(SizeController ball)
        {
            return ball != null && balls.Contains(ball);
        }

        public void Add(SizeController ball)
        {
            if (ball == null || ball.IsDead || balls.Contains(ball))
            {
                return;
            }

                balls.Add(ball);

            if (main == null || ball.Mass > main.Mass)
            {
                main = ball;
            }
        }

        public bool Remove(SizeController ball)
        {
            if (ball == null)
            {
                return false;
            }
            bool removed = balls.Remove(ball);
            if (main == ball)
            {
                main = Largest();
            }

            return removed;
        }

        public void Clear()
        {
            balls.Clear();
            main = null;
        }

        // pick the heaviest living ball again(when masses changed)
        // the current main keeps the role on a tie, so a split never swaps control
        public void RefreshMain()
        {
            SizeController best = main != null && !main.IsDead && balls.Contains(main) ? main : null;
            float bestMass = best != null ? best.Mass : float.MinValue;

            for (int i = 0; i < balls.Count; i++)
            {
                SizeController ball = balls[i];
                if (ball == null || ball.IsDead)
                {
                    continue;
                }
                if (ball.Mass > bestMass)
                {
                    bestMass = ball.Mass;
                    best = ball;
                }
            }

            main = best;
        }
        public SizeController Largest()
        {
            SizeController best = null;
            float bestMass = float.MinValue;

            for (int i = balls.Count - 1; i >= 0; i--)
            {
                SizeController ball = balls[i];
                if (ball == null)
                {
                    balls.RemoveAt(i);
                    continue;
                }
                if (ball.IsDead)
                {
                    continue;
                }
                if (ball.Mass > bestMass)
                {
                    bestMass = ball.Mass;
                    best = ball;
                }
            }

            return best;
        }

        // remove destroyed balls
        public void PruneDead(System.Action<SizeController> onDropped)
        {
            for (int i = balls.Count - 1; i >= 0; i--)
            {
                SizeController ball = balls[i];
                if (ball != null && !ball.IsDead)
                {
                    continue;
                }
                    
                    balls.RemoveAt(i);

                if (ball != null && onDropped != null)
                {
                    onDropped(ball);
                }
            }

            if (main == null || main.IsDead)
            {
                main = Largest();
            }
        }
    }
}
