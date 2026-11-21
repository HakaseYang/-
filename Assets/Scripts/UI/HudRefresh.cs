using UnityEngine;

namespace QiuQiu
{
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
