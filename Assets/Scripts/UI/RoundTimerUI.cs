using UnityEngine;
using UnityEngine.UI;

namespace QiuQiu
{
    /// <summary>
    /// 屏幕顶部正中央的局内倒计时（如 03:00）。
    ///
    /// 刷新策略：
    ///   每 0.2 秒刷一次（可调）。倒计时只显示到秒，每帧改 Text 只会白触发 uGUI 的 mesh 重建。
    ///   计时用 unscaledTime：结算瞬间 timeScale = 0，但这里只要读到 GameManager.RemainingTime 就行。
    ///
    /// 颜色：
    ///   剩余时间 &lt;= GameManager.WarnTime（默认 30 秒）时变红并轻微脉动，提醒玩家最后一分钟。
    /// </summary>
    [DisallowMultipleComponent]
    public class RoundTimerUI : MonoBehaviour
    {
        [Header("引用")]
        [Tooltip("显示 mm:ss 的 Text。留空则自动找子物体里名字含 Timer / Time 的 Text。")]
        [SerializeField] private Text timerText;

        [Tooltip("GameManager。留空自动找。")]
        [SerializeField] private GameManager gameManager;

        [Header("刷新")]
        [SerializeField] private float refreshInterval = 0.2f;

        [Header("样式")]
        [SerializeField] private Color normalColor = new Color(0.95f, 0.98f, 1f, 0.95f);
        [SerializeField] private Color warnColor = new Color(1f, 0.45f, 0.45f, 1f);
        [Tooltip("紧急时的脉动频率（每秒几次）。0 = 不脉动。")]
        [SerializeField] private float pulseSpeed = 2.2f;

        private float nextRefreshTime;
        private int lastShownSeconds = -1;

        /// <summary>当前显示用的文本（调试/测试用）。</summary>
        public string CurrentText => timerText != null ? timerText.text : string.Empty;

        private void Awake()
        {
            if (gameManager == null) gameManager = FindFirstObjectByType<GameManager>();

            if (timerText == null)
            {
                foreach (var text in GetComponentsInChildren<Text>(true))
                {
                    string n = text.name;
                    if (n.IndexOf("Timer", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                        n.IndexOf("Time", System.StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        timerText = text;
                        break;
                    }
                }
            }
        }

        private void OnEnable()
        {
            Refresh(true);
        }

        private void Update()
        {
            if (Time.unscaledTime < nextRefreshTime) return;
            nextRefreshTime = Time.unscaledTime + Mathf.Max(0.05f, refreshInterval);
            Refresh(false);
        }

        private void Refresh(bool force)
        {
            if (timerText == null) return;
            if (gameManager == null) gameManager = FindFirstObjectByType<GameManager>();
            if (gameManager == null) return;

            float remaining = Mathf.Max(0f, gameManager.RemainingTime);
            int totalSeconds = Mathf.CeilToInt(remaining);

            if (force || totalSeconds != lastShownSeconds)
            {
                lastShownSeconds = totalSeconds;

                int minutes = totalSeconds / 60;
                int seconds = totalSeconds % 60;
                timerText.text = $"{minutes:00}:{seconds:00}";
            }

            // 颜色：最后 WarnTime 秒变红 + 脉动
            bool urgent = remaining <= Mathf.Max(0f, gameManager.WarnTime);
            if (urgent && pulseSpeed > 0.01f)
            {
                float t = (Mathf.Sin(Time.unscaledTime * pulseSpeed * Mathf.PI * 2f) + 1f) * 0.5f;
                timerText.color = Color.Lerp(normalColor, warnColor, Mathf.Lerp(0.55f, 1f, t));
            }
            else
            {
                timerText.color = urgent ? warnColor : normalColor;
            }
        }
    }
}
