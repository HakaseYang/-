using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace QiuQiu
{
    /// <summary>
    /// 全屏结算面板：默认隐藏，GameOver 时显示。
    ///
    /// 组成（由编辑器菜单 7 一键搭建，也可手动摆）：
    ///   GameOverPanel (Canvas 下的全屏 Image，半透明黑底)
    ///     └─ Card (圆角/纯色卡片)
    ///          ├─ Title      "GAME OVER"
    ///          ├─ PeakMass   "本局最高质量：1234"
    ///          ├─ FinalRank  "最终排名：第 2 名"
    ///          └─ BtnRestart "重新开始"
    ///
    /// 为什么面板在 timeScale = 0 时还能点？
    ///   uGUI 的 EventSystem / Button 走的是 Update + unscaled 时间，
    ///   而 Input System UI Input Module 也是按真实时间派发指针事件，
    ///   所以 Time.timeScale = 0 冻住世界的同时，按钮依然可以正常点击（含触摸）。
    ///
    /// 重开一局：
    ///   SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex)
    ///   注意两点：
    ///     1. 场景必须在 Build Settings 里（否则 buildIndex = -1，加载会失败）——本工程 LevelScene 已在列表里；
    ///     2. 加载前把 Time.timeScale 恢复成 1，否则新场景一进去就是定格的（GameManager.Awake 里也做了一次兜底）。
    /// </summary>
    [DisallowMultipleComponent]
    public class GameOverPanel : MonoBehaviour
    {
        [Header("面板")]
        [Tooltip("面板根物体（显示/隐藏它）。留空则用本物体。")]
        [SerializeField] private GameObject panelRoot;

        [Header("文字")]
        [SerializeField] private Text titleText;
        [SerializeField] private Text peakMassText;
        [SerializeField] private Text finalRankText;

        [Header("按钮")]
        [SerializeField] private Button restartButton;

        [Tooltip("返回主菜单按钮（可留空；留空则不显示返回按钮功能）。")]
        [SerializeField] private Button backToMenuButton;

        [Tooltip("返回主菜单时加载的场景名。")]
        [SerializeField] private string mainMenuSceneName = "MainMenu";

        [Header("可选：重新开始前倒数（秒）。0 = 立刻重开")]
        [SerializeField] private float restartDelay = 0f;

        [Header("显示时自动选中按钮（手柄/键盘可直接确认）")]
        [SerializeField] private bool selectButtonOnShow = true;

        private GameManager gameManager;
        private float pendingRestartAt = -1f;

        public bool IsVisible => panelRoot != null ? panelRoot.activeSelf : gameObject.activeSelf;

        private void Awake()
        {
            if (panelRoot == null) panelRoot = gameObject;
            if (gameManager == null) gameManager = FindFirstObjectByType<GameManager>();

            if (restartButton != null && restartButton.onClick.GetPersistentEventCount() == 0)
            {
                // 代码兜底挂监听：如果 Inspector 里已经配了持久化 OnClick（编辑器菜单 7 会配），就不重复挂，
                // 避免一次点击触发两次重载（第二次 LoadScene 会把刚加载好的场景再刷一遍，闪一下）。
                restartButton.onClick.RemoveListener(Restart);
                restartButton.onClick.AddListener(Restart);
            }

            if (backToMenuButton != null && backToMenuButton.onClick.GetPersistentEventCount() == 0)
            {
                backToMenuButton.onClick.RemoveListener(BackToMenu);
                backToMenuButton.onClick.AddListener(BackToMenu);
            }

            Hide();   // 默认隐藏
        }

        private void Update()
        {
            if (pendingRestartAt < 0f) return;

            // 用 unscaledTime：结算时 timeScale = 0，普通 Time.time 不会走
            if (Time.unscaledTime >= pendingRestartAt)
            {
                pendingRestartAt = -1f;
                DoRestart();
            }
        }

        /// <summary>显示结算面板（GameManager 在 GameOver 时调用）。</summary>
        public void Show(GameManager manager)
        {
            gameManager = manager != null ? manager : gameManager;

            if (panelRoot == null) panelRoot = gameObject;

            float peak = gameManager != null ? gameManager.PeakMass : 0f;
            int rank = gameManager != null ? gameManager.FinalRank : 1;
            int total = gameManager != null ? gameManager.TotalParticipants : 1;

            if (titleText != null) titleText.text = "GAME OVER";
            if (peakMassText != null) peakMassText.text = $"本局最高质量：{peak:F0}";
            if (finalRankText != null) finalRankText.text = $"最终排名：第 {rank} 名 / 共 {total} 人";

            // 记录历史最好成绩（主菜单显示用）
            MainMenuController.ReportBestMass(peak);

            panelRoot.SetActive(true);

            if (selectButtonOnShow && restartButton != null &&
                UnityEngine.EventSystems.EventSystem.current != null)
            {
                UnityEngine.EventSystems.EventSystem.current.SetSelectedGameObject(restartButton.gameObject);
            }
        }

        public void Hide()
        {
            if (panelRoot == null) panelRoot = gameObject;
            panelRoot.SetActive(false);
        }

        /// <summary>重新开始（按钮 OnClick 与外部都调这个）。</summary>
        public void Restart()
        {
            if (restartDelay > 0f)
            {
                pendingRestartAt = Time.unscaledTime + restartDelay;
                if (restartButton != null) restartButton.interactable = false;
                return;
            }

            DoRestart();
        }

        private void DoRestart()
        {
            // 关键：先把时间缩放恢复正常，再重载场景
            Time.timeScale = 1f;
            AudioListener.pause = false;

            Scene scene = SceneManager.GetActiveScene();
            int index = scene.buildIndex;

            if (index < 0)
            {
                // 场景没进 Build Settings 时 buildIndex 为 -1，直接按名字重载兜底
                Debug.LogWarning($"[GameOverPanel] 场景 {scene.name} 不在 Build Settings 中，按名字重载。");
                SceneManager.LoadScene(scene.name);
                return;
            }

            SceneManager.LoadScene(index);
        }

        /// <summary>返回主菜单（按钮 OnClick 与外部都调这个）。</summary>
        public void BackToMenu()
        {
            // 结算时 timeScale = 0，离开前必须复位，否则主菜单是定格的
            Time.timeScale = 1f;
            AudioListener.pause = false;

            if (string.IsNullOrEmpty(mainMenuSceneName))
            {
                Debug.LogError("[GameOverPanel] mainMenuSceneName 为空，无法返回主菜单");
                return;
            }

            // 用 buildIndex 加载：名字被改过也不会静默失败（CanStreamedLevelBeLoaded 只认名字）
            int menuIndex = MainMenuController.GetBuildIndexBySceneName(mainMenuSceneName);
            if (menuIndex < 0)
            {
                Debug.LogError($"[GameOverPanel] 场景 {mainMenuSceneName} 不在 Build Settings 里，请先添加");
                return;
            }

            SceneManager.LoadScene(menuIndex);
        }
    }
}
