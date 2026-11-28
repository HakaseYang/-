using UnityEngine;
using UnityEngine.UI;

namespace QiuQiu
{
    public class RoundTimerUI : MonoBehaviour
    {
        [Header("Refs")]
        [SerializeField] private Text timerText;

        [SerializeField] private GameManager gameManager;

        [Header("Refresh")]
        [SerializeField] private float refreshInterval = 0.2f;

        [Header("Style")]
        [SerializeField] private Color normalColor = new Color(0.95f, 0.98f, 1f, 0.95f);
        [SerializeField] private Color warnColor = new Color(1f, 0.45f, 0.45f, 1f);
        [SerializeField] private float pulseSpeed = 2.2f;

        private float nextRefreshTime;
        private int lastShownSeconds = -1;

        private void Awake()
        {
            gameManager = HudRefresh.Resolve(gameManager);
            if (timerText == null)
            {
                timerText = FindText("Time");
            }
        }

        private void OnEnable()
        {
            Refresh(true);
        }

        private void Update()
        {
            if (HudRefresh.Due(ref nextRefreshTime, refreshInterval))
            {
                Refresh(false);
            }
        }

        private void Refresh(bool force)
        {
            if (timerText == null)
            {
                return;
            }

            gameManager = HudRefresh.Resolve(gameManager);
            if (gameManager == null)
            {
                return;
            }

            float remaining = Mathf.Max(0f, gameManager.RemainingTime);
            int totalSeconds = Mathf.CeilToInt(remaining);

            if (force || totalSeconds != lastShownSeconds)
            {
                lastShownSeconds = totalSeconds;
                timerText.text = $"{totalSeconds / 60:00}:{totalSeconds % 60:00}";
            }

            bool urgent = remaining <=  gameManager.WarnTime;
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

        private Text FindText(string token)
        {
            foreach (Text text in GetComponentsInChildren<Text>(true))
            {
                if (text.name.IndexOf(token, System.StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return text;
                }
            }

            return null;
        }
    }
}
