using System.Collections.Generic;

namespace QiuQiu
{
    // owns the balls the player controls and remembers which one is the main ball.
    // no Unity component, no per frame work unless the owner asks for it.
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
                    if (ball != null && !ball.IsDead) total += ball.Mass;
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
            if (ball == null || ball.IsDead || balls.Contains(ball)) return;

            balls.Add(ball);

            if (main == null || ball.Mass > main.Mass) main = ball;
        }

        // true when the ball was actually in the list
        public bool Remove(SizeController ball)
        {
            if (ball == null) return false;

            bool removed = balls.Remove(ball);
            if (main == ball) main = Largest();
            return removed;
        }

        public void Clear()
        {
            balls.Clear();
            main = null;
        }

        // pick the heaviest living ball again; call it when masses changed
        public void RefreshMain()
        {
            main = Largest();
        }

        // drops Unity-null entries, returns the heaviest living ball
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
                if (ball.IsDead) continue;

                if (ball.Mass > bestMass)
                {
                    bestMass = ball.Mass;
                    best = ball;
                }
            }

            return best;
        }

        // remove destroyed balls; onDropped lets the owner unhook events for each one
        public void PruneDead(System.Action<SizeController> onDropped)
        {
            for (int i = balls.Count - 1; i >= 0; i--)
            {
                SizeController ball = balls[i];
                if (ball != null && !ball.IsDead) continue;

                balls.RemoveAt(i);

                if (ball != null && onDropped != null) onDropped(ball);
            }

            if (main == null || main.IsDead) main = Largest();
        }
    }
}
