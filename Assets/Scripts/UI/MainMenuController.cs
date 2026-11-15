using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace QiuQiu
{
    /// <summary>
    /// 主菜单控制器：四个按钮 → 写入难度 + 加载游戏场景 / 退出。
    ///
    /// 按钮在 Inspector 里绑 onClick（持久化监听），也可以在代码里 AddListener；
    /// 这里同时暴露带参数的 Play(int) 便于在 Inspector 里直接填 1/2/3。
    /// </summary>
    [DisallowMultipleComponent]
    public class MainMenuController : MonoBehaviour
    {
        [Header("场景")]
        [Tooltip("点击难度后加载的游戏场景名（必须在 Build Settings 里）。")]
        [SerializeField] private string gameplaySceneName = "SampleScene";

        [Header("可选 UI")]
        [Tooltip("显示当前难度的文本（可留空）。")]
        [SerializeField] private Text difficultyLabel;

        [Tooltip("显示历史最高质量的文本（可留空）。")]
        [SerializeField] private Text bestMassLabel;

        public const string BestMassPrefKey = "QiuQiu.BestMass";

        private void Start()
        {
            // 主菜单里没有玩家球，确保不残留上一局的时间缩放
            Time.timeScale = 1f;
            AudioListener.pause = false;

            RefreshLabels();
        }

        /// <summary>按钮 1~3 的统一入口：level = 1/2/3。</summary>
        public void Play(int level)
        {
            GameSettings.SetDifficulty(level);

            DifficultyLevel current = GameSettings.Difficulty;
            Debug.Log($"[主菜单] 选择 {GameSettings.GetDisplayName(current)}｜" +
                      $"玩家吞噬 ×{GameSettings.PlayerEatMultiplier:F2}｜AI 吞噬 ×{GameSettings.AiEatMultiplier:F2}｜" +
                      $"加载场景 {gameplaySceneName}");

            LoadGameplayScene();
        }

        public void PlayEasy() => Play((int)DifficultyLevel.Easy);
        public void PlayNormal() => Play((int)DifficultyLevel.Normal);
        public void PlayHard() => Play((int)DifficultyLevel.Hard);

        /// <summary>退出游戏：编辑器里只是停止播放，真机才真正退出。</summary>
        public void QuitGame()
        {
            Debug.Log("[主菜单] 退出游戏");

#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }

        /// <summary>记录本局最高质量（GameManager 结算时调用），主菜单显示用。</summary>
        public static void ReportBestMass(float mass)
        {
            if (mass <= 0f) return;

            float best = PlayerPrefs.GetFloat(BestMassPrefKey, 0f);
            if (mass > best)
            {
                PlayerPrefs.SetFloat(BestMassPrefKey, mass);
                PlayerPrefs.Save();
            }
        }

        private void LoadGameplayScene()
        {
            if (string.IsNullOrEmpty(gameplaySceneName))
            {
                Debug.LogError("[主菜单] gameplaySceneName 为空，无法加载游戏场景");
                return;
            }

            if (Application.CanStreamedLevelBeLoaded(gameplaySceneName))
            {
                SceneManager.LoadScene(gameplaySceneName);
            }
            else
            {
                Debug.LogError($"[主菜单] 场景 {gameplaySceneName} 不在 Build Settings 里，请先添加");
            }
        }

        private void RefreshLabels()
        {
            if (difficultyLabel != null)
            {
                difficultyLabel.text = $"当前难度：{GameSettings.GetShortName(GameSettings.Difficulty)}";
            }

            if (bestMassLabel != null)
            {
                float best = PlayerPrefs.GetFloat(BestMassPrefKey, 0f);
                bestMassLabel.text = best > 0f ? $"历史最高质量：{best:F0}" : "历史最高质量：—";
            }
        }
    }
}
