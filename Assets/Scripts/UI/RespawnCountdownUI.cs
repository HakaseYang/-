using UnityEngine;
using UnityEngine.UI;

namespace QiuQiu
{
    /// <summary>
    /// 玩家“死亡观战 / 复活等待”时显示的全屏提示：
    ///   半透明遮罩（可选） + 一行"复活倒计时 3" + 一行小字提示。
    ///
    /// 显示逻辑完全由 GameManager 的状态驱动（本脚本只读，不主动改状态）：
    ///   GameManager.IsPlayerRespawning == true  → 显示并每 0.1 秒更新剩余秒数
    ///   false                                  → 隐藏
    ///
    /// 为什么用 unscaledTime：复活等待期间如果以后加了暂停，计时也不该被 timeScale 影响。
    /// </summary>
    [DisallowMultipleComponent]
    public class RespawnCountdownUI : MonoBehaviour
    {
        [Header("引用")]
        [Tooltip("显示“复活倒计时 3”的 Text。留空自动找子物体里第一个 Text。")]
        [SerializeField] private Text countdownText;

        [Tooltip("副标题（可选），例如“再坚持一下，马上就回来”。")]
        [SerializeField] private Text hintText;

        [Tooltip("整块 UI 的根物体（显示/隐藏它）。留空则用本物体。")]
        [SerializeField] private GameObject root;

        [Tooltip("GameManager。留空自动找。")]
        [SerializeField] private GameManager gameManager;

        [Header("样式")]
        [SerializeField] private Color countdownColor = new Color(1f, 0.92f, 0.55f, 1f);
        [SerializeField] private string countdownFormat = "复活倒计时 {0}";
        [SerializeField] private string hint = "你的球被吃掉了，稍后会在地图安全处重生";

        private float nextRefreshTime;
        private int lastShown = -1;
        private bool shown;

        public bool IsShowing => shown;

        private void Awake()
        {
            if (gameManager == null) gameManager = FindFirstObjectByType<GameManager>();

            // 显隐要操作的必须是“子容器”，不能是自己：
            // SetActive(false) 会连脚本一起停掉，之后就没法再显示出来了。
            if (root == null)
                root = transform.childCount > 0 ? transform.GetChild(0).gameObject : null;

            if (countdownText == null) countdownText = GetComponentInChildren<Text>(true);
            if (hintText != null && !string.IsNullOrEmpty(hint)) hintText.text = hint;

            SetShown(false);
        }

        private void Update()
        {
            if (gameManager == null) gameManager = FindFirstObjectByType<GameManager>();
            if (gameManager == null) return;

            bool shouldShow = gameManager.IsPlayerRespawning;
            if (shouldShow != shown) SetShown(shouldShow);

            if (!shouldShow) return;

            if (Time.unscaledTime < nextRefreshTime) return;
            nextRefreshTime = Time.unscaledTime + 0.1f;

            int seconds = Mathf.Max(1, Mathf.CeilToInt(gameManager.RespawnTimeLeft));
            if (seconds != lastShown)
            {
                lastShown = seconds;
                if (countdownText != null) countdownText.text = string.Format(countdownFormat, seconds);
            }
        }

        private void SetShown(bool value)
        {
            shown = value;
            lastShown = -1;

            // 只开关子容器（root），脚本自己所在的物体始终保持激活，
            // 否则 SetActive(false) 之后 Update 不再运行，就永远显示不出来了。
            if (root != null && root != gameObject) root.SetActive(value);

            if (value && countdownText != null)
            {
                countdownText.color = countdownColor;
                countdownText.text = string.Format(countdownFormat, Mathf.Max(1, Mathf.CeilToInt(gameManager != null ? gameManager.RespawnTimeLeft : 5f)));
            }
        }
    }
}
