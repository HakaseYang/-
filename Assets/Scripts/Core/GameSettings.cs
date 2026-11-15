using UnityEngine;

namespace QiuQiu
{
    /// <summary>三档难度。</summary>
    public enum DifficultyLevel
    {
        Easy = 1,
        Normal = 2,
        Hard = 3,
    }

    /// <summary>
    /// 全局游戏设置（难度）。
    ///
    /// 为什么用静态类 + PlayerPrefs：
    ///   - 难度只是“一个数字”，没必要为每档难度各做一个场景；
    ///   - 主菜单写、游戏场景读，用静态字段零成本传递；
    ///   - 同时落盘到 PlayerPrefs，重开游戏 / 从编辑器重进也记得上次选择。
    ///
    /// 倍率怎么用：见 SizeController.AddMassFromEat()。
    ///   玩家（team==0）吃食物/敌人时乘 PlayerEatMultiplier，
    ///   AI（team!=0）吃食物/敌人时乘 AiEatMultiplier。
    /// </summary>
    public static class GameSettings
    {
        public const string DifficultyPrefKey = "QiuQiu.Difficulty";

        /// <summary>主菜单场景名（Build Settings 索引 0）。</summary>
        public const string MainMenuSceneName = "MainMenu";

        /// <summary>游戏场景名。</summary>
        public const string GameplaySceneName = "SampleScene";

        // 三档难度的吞噬倍率表（想调平衡改这里就够了）
        private const float EasyPlayerMultiplier = 1.5f;
        private const float EasyAiMultiplier = 0.5f;
        private const float NormalPlayerMultiplier = 1f;
        private const float NormalAiMultiplier = 1f;
        private const float HardPlayerMultiplier = 0.5f;
        private const float HardAiMultiplier = 1.5f;

        /// <summary>玩家吞噬质量倍率（SizeController 读它）。</summary>
        public static float PlayerEatMultiplier { get; private set; } = NormalPlayerMultiplier;

        /// <summary>AI 吞噬质量倍率（SizeController 读它）。</summary>
        public static float AiEatMultiplier { get; private set; } = NormalAiMultiplier;

        private static DifficultyLevel? cachedDifficulty;

        /// <summary>当前难度；读取时懒加载 PlayerPrefs，写入时立即落盘。</summary>
        public static DifficultyLevel Difficulty
        {
            get
            {
                if (!cachedDifficulty.HasValue)
                {
                    int raw = PlayerPrefs.GetInt(DifficultyPrefKey, (int)DifficultyLevel.Normal);
                    cachedDifficulty = Clamp(raw);
                    ApplyMultipliers(cachedDifficulty.Value);
                }

                return cachedDifficulty.Value;
            }
            set
            {
                cachedDifficulty = value;
                ApplyMultipliers(value);
                PlayerPrefs.SetInt(DifficultyPrefKey, (int)value);
                PlayerPrefs.Save();
            }
        }

        /// <summary>按枚举/数字设置难度（越界自动夹到 1~3）。</summary>
        public static void SetDifficulty(int level)
        {
            Difficulty = Clamp(level);
        }

        /// <summary>显示名，例如 “Level 2 (Normal)”。</summary>
        public static string GetDisplayName(DifficultyLevel level)
        {
            switch (level)
            {
                case DifficultyLevel.Easy: return "Level 1 (Easy)";
                case DifficultyLevel.Hard: return "Level 3 (Hard)";
                default: return "Level 2 (Normal)";
            }
        }

        /// <summary>中文难度名，用于 HUD / 结算面板。</summary>
        public static string GetShortName(DifficultyLevel level)
        {
            switch (level)
            {
                case DifficultyLevel.Easy: return "简单";
                case DifficultyLevel.Hard: return "困难";
                default: return "普通";
            }
        }

        /// <summary>强制按当前难度重算倍率（DifficultyManager 在 Start 里也会调一次）。</summary>
        public static void RefreshMultipliers()
        {
            ApplyMultipliers(Difficulty);
        }

        private static void ApplyMultipliers(DifficultyLevel level)
        {
            switch (level)
            {
                case DifficultyLevel.Easy:
                    PlayerEatMultiplier = EasyPlayerMultiplier;
                    AiEatMultiplier = EasyAiMultiplier;
                    break;
                case DifficultyLevel.Hard:
                    PlayerEatMultiplier = HardPlayerMultiplier;
                    AiEatMultiplier = HardAiMultiplier;
                    break;
                default:
                    PlayerEatMultiplier = NormalPlayerMultiplier;
                    AiEatMultiplier = NormalAiMultiplier;
                    break;
            }
        }

        private static DifficultyLevel Clamp(int raw)
        {
            if (raw <= (int)DifficultyLevel.Easy) return DifficultyLevel.Easy;
            if (raw >= (int)DifficultyLevel.Hard) return DifficultyLevel.Hard;
            return (DifficultyLevel)raw;
        }
    }
}
