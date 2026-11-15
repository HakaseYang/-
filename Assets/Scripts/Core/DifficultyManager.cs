using UnityEngine;

namespace QiuQiu
{
    /// <summary>
    /// 难度管理器：游戏场景（SampleScene）里的一个常驻对象，Start 时读取主菜单写入的难度，
    /// 把“吞噬质量倍率”落到全局，并按需覆盖 GameManager 的初始数值。
    ///
    /// 它只做三件事：
    ///   1. Start：读 GameSettings.Difficulty → GameSettings.RefreshMultipliers()；
    ///   2. 打一条清晰日志（方便自测时确认本局难度）；
    ///   3. 可选：把 GameManager 的复活速度 / AI 上限等按难度微调（默认关闭，避免数值失控）。
    ///
    /// 真正的乘算发生在 SizeController.AddMassFromEat()，所以不需要改任何 AI / 食物脚本。
    /// </summary>
    [DisallowMultipleComponent]
    public class DifficultyManager : MonoBehaviour
    {
        public static DifficultyManager Instance { get; private set; }

        [Header("调试")]
        [Tooltip("在 Inspector 里强制指定难度（勾上后忽略主菜单选择，便于编辑器里单独调某一档）。")]
        [SerializeField] private bool forceDifficulty = false;
        [SerializeField] private DifficultyLevel forcedDifficulty = DifficultyLevel.Normal;

        [Header("AI 微调（默认关闭）")]
        [Tooltip("简单难度下把 AI 的移动速度压低一点，让新手有喘息空间。")]
        [SerializeField] private bool scaleAiSpeedByDifficulty = false;

        private void Awake()
        {
            Instance = this;
        }

        private void Start()
        {
            if (forceDifficulty) GameSettings.Difficulty = forcedDifficulty;

            // 读取 + 应用倍率（主菜单已经写过一次，这里再同步一次，保证场景单独启动也正确）
            GameSettings.RefreshMultipliers();

            DifficultyLevel level = GameSettings.Difficulty;

            Debug.Log($"[难度] 本局难度 = {GameSettings.GetDisplayName(level)}（{(int)level}）｜" +
                      $"玩家吞噬 ×{GameSettings.PlayerEatMultiplier:F2}｜AI 吞噬 ×{GameSettings.AiEatMultiplier:F2}");

            if (scaleAiSpeedByDifficulty) ApplyAiSpeedScale(level);
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        private void ApplyAiSpeedScale(DifficultyLevel level)
        {
            float scale = level == DifficultyLevel.Easy ? 0.85f
                        : level == DifficultyLevel.Hard ? 1.15f
                        : 1f;

            var all = SizeController.Registry;
            for (int i = 0; i < all.Count; i++)
            {
                SizeController ball = all[i];
                if (ball == null || ball.Team == 0) continue;

                var ai = ball.GetComponent<AIController>();
                if (ai != null) ai.MoveSpeed *= scale;
            }
        }

        /// <summary>当前难度（给 HUD / 结算面板显示用）。</summary>
        public DifficultyLevel CurrentDifficulty => GameSettings.Difficulty;
    }
}
