using UnityEngine;
using UnityEngine.UI;

namespace QiuQiu
{
    [DisallowMultipleComponent]
    public class RespawnCountdownUI : MonoBehaviour
    {
        [Header("Refs")]
        [SerializeField] private Text countdownText;

        [SerializeField] private Text hintText;

        [SerializeField] private GameObject root;

        [SerializeField] private GameManager gameManager;

        [Header("Style")]
        [SerializeField] private Color countdownColor = new Color(1f, 0.92f, 0.55f, 1f);
        [SerializeField] private string countdownFormat = "RESPAWN IN {0}";
        [SerializeField] private string hint = "You were eaten, respawning at a safe spot";

        private float nextRefreshTime;
        private int lastShown = -1;
        private bool shown;

        private void Awake()
        {
            gameManager = HudRefresh.Resolve(gameManager);

            // toggle a child container, never this object: SetActive(false) stops
            // this script and it could never show again
            if (root == null)
            {
                root = transform.childCount > 0 ? transform.GetChild(0).gameObject : null;
            }
            if (countdownText == null)
            {
                countdownText = GetComponentInChildren<Text>(true);
            }

            if (countdownText != null)
            {
                countdownText.color = countdownColor;
            }
            if (hintText != null && !string.IsNullOrEmpty(hint))
            {
                hintText.text = hint;
            }

            SetShown(false);
        }

        private void Update()
        {
            gameManager = HudRefresh.Resolve(gameManager);
            if (gameManager == null)
            {
                return;
            }

            bool shouldShow = gameManager.IsPlayerRespawning;
            if (shouldShow != shown)
            {
                SetShown(shouldShow);
            }
            if (shouldShow && HudRefresh.Due(ref nextRefreshTime, 0.1f))
            {
                Write(Mathf.Max(1, Mathf.CeilToInt(gameManager.RespawnTimeLeft)));
            }
        }

        private void SetShown(bool value)
        {
            shown = value;
            lastShown = -1;

            if (root != null && root != gameObject)
            {
                root.SetActive(value);
            }
            if (value && gameManager != null)
            {
                Write(Mathf.Max(1, Mathf.CeilToInt(gameManager.RespawnTimeLeft)));
            }
        }

        private void Write(int seconds)
        {
            if (countdownText == null || seconds == lastShown)
            {
                return;
            }

            lastShown = seconds;
            countdownText.text = string.Format(countdownFormat, seconds);
        }
    }
}
