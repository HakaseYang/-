using System.IO;
using UnityEngine;
using UnityEngine.SceneManagement;

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
        [SerializeField] private string gameplaySceneName = GameSettings.GameplaySceneName;

        public const string BestMassPrefKey = "QiuQiu.BestMass";

        private void Start()
        {
            // 主菜单里没有玩家球，确保不残留上一局的时间缩放
            Time.timeScale = 1f;
            AudioListener.pause = false;
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

        /// <summary>
        /// 加载游戏场景。
        ///
        /// 为什么不再用 Application.CanStreamedLevelBeLoaded(name) 做判断：
        ///   1. 它只认 Build Settings 里的场景名，场景文件一改名，代码/Inspector 里
        ///      存着的旧名字就会静默失效——表现为“点按钮完全没反应”；
        ///   2. 它在 Edit Mode（非 Play Mode）里几乎总是返回 false，调试时非常误导。
        /// 这里改成直接遍历 Build Settings 拿 buildIndex，并且在配置的名字失效时
        /// 自动回退到 GameSettings.GameplaySceneName，改名字不再需要同步 Inspector。
        /// </summary>
        private void LoadGameplayScene()
        {
            string resolved = ResolveGameplaySceneName(out int buildIndex);

            if (resolved == null)
            {
                Debug.LogError($"[主菜单] 场景 \"{gameplaySceneName}\" 不在 Build Settings 里" +
                               $"（备用名 \"{GameSettings.GameplaySceneName}\" 也没有），无法加载关卡。" +
                               "请打开 File > Build Settings 确认场景已勾选。");
                return;
            }

            if (!string.Equals(resolved, gameplaySceneName, System.StringComparison.Ordinal))
            {
                Debug.LogWarning($"[主菜单] 配置的场景名 \"{gameplaySceneName}\" 已失效，" +
                                 $"自动改用 \"{resolved}\"（Build Settings 索引 {buildIndex}）。" +
                                 "建议在 Inspector 里把 Gameplay Scene Name 改过来。");
            }

            SceneManager.LoadScene(buildIndex);
        }

        /// <summary>在 Build Settings 里按场景名（不含扩展名）找索引；找不到返回 -1。</summary>
        public static int GetBuildIndexBySceneName(string sceneName)
        {
            if (string.IsNullOrEmpty(sceneName)) return -1;

            int count = SceneManager.sceneCountInBuildSettings;
            for (int i = 0; i < count; i++)
            {
                string path = SceneUtility.GetScenePathByBuildIndex(i);
                if (string.IsNullOrEmpty(path)) continue;

                if (string.Equals(Path.GetFileNameWithoutExtension(path), sceneName,
                        System.StringComparison.OrdinalIgnoreCase))
                {
                    return i;
                }
            }

            return -1;
        }

        /// <summary>先按 Inspector 配置的名字找，再回退到 GameSettings 的常量名。</summary>
        private string ResolveGameplaySceneName(out int buildIndex)
        {
            buildIndex = GetBuildIndexBySceneName(gameplaySceneName);
            if (buildIndex >= 0) return gameplaySceneName;

            buildIndex = GetBuildIndexBySceneName(GameSettings.GameplaySceneName);
            if (buildIndex >= 0) return GameSettings.GameplaySceneName;

            buildIndex = -1;
            return null;
        }
    }
}
