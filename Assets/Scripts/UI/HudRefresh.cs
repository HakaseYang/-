using UnityEngine;

namespace QiuQiu
{
    // the small HUD widgets all need the same two things: a lazy GameManager
    // reference and a throttled refresh clock
    internal static class HudRefresh
    {
        private const float MinInterval = 0.02f;

        public static GameManager Resolve(GameManager current)
        {
            if (current != null)
            {
                return current;
            }

            GameManager live = GameManager.Instance;
            return live != null ? live : Object.FindFirstObjectByType<GameManager>();
        }

        // true when the widget may redraw now; advances the clock when it says yes
        public static bool Due(ref float nextTime, float interval)
        {
            if (Time.unscaledTime < nextTime)
            {
                return false;
            }

            nextTime = Time.unscaledTime + Mathf.Max(MinInterval, interval);
            return true;
        }
    }
}
