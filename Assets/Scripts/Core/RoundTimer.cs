using UnityEngine;

namespace QiuQiu
{
    // countdown in unscaled time, so a frozen game still runs the clock out
    public class RoundTimer
    {
        private float remaining;

        public float Remaining { get { return remaining; } }
        public bool IsFinished { get { return remaining <= 0f; } }

        public void Start(float duration)
        {
            remaining = Mathf.Max(0f, duration);
        }

        public void Tick(float unscaledDelta)
        {
            if (remaining > 0f)
            {
                remaining = Mathf.Max(0f, remaining - unscaledDelta);
            }
        }
    }
}
