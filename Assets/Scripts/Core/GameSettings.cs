using UnityEngine;

namespace QiuQiu
{
    // 枚举值会被主菜单 Play(int) 拿去当关卡编号，别改
    public enum DifficultyLevel
    {
        Easy = 1,
        Normal = 2,
        Hard = 3,
    }

    // 全局难度设置：主菜单三个按钮选，关卡读。
    // 每局都必须重新选，所以不记盘，难度只活在内存里（重开游戏回到 Normal）。
    public static class GameSettings
    {
        public const string GameplaySceneName = "LevelScene";
        public const string MainMenuSceneName = "MainMenu";

        // 难度 → 吞噬倍率，下标是 (int)难度 - 1
        private static readonly float[] PlayerEatTable = { 1.5f, 1f, 0.5f };
        private static readonly float[] AiEatTable = { 0.5f, 1f, 1.5f };

        public static float PlayerEatMultiplier { get; private set; }
        public static float AiEatMultiplier { get; private set; }

        private static DifficultyLevel difficulty;

        static GameSettings()
        {
            SetLevel(DifficultyLevel.Normal);
        }

        public static DifficultyLevel Difficulty
        {
            get { return difficulty; }
            set { SetLevel(value); }
        }

        public static void SetDifficulty(int level)
        {
            Difficulty = ClampLevel(level);
        }

        public static void RefreshMultipliers()
        {
            ApplyMultipliers(difficulty);
        }

        public static string GetDisplayName(DifficultyLevel level)
        {
            switch (level)
            {
                case DifficultyLevel.Easy: return "Level 1 (Easy)";
                case DifficultyLevel.Hard: return "Level 3 (Hard)";
                default: return "Level 2 (Normal)";
            }
        }

        public static string GetShortName(DifficultyLevel level)
        {
            switch (level)
            {
                case DifficultyLevel.Easy: return "简单";
                case DifficultyLevel.Hard: return "困难";
                default: return "普通";
            }
        }

        private static void SetLevel(DifficultyLevel level)
        {
            difficulty = ClampLevel((int)level);
            ApplyMultipliers(difficulty);
        }

        private static void ApplyMultipliers(DifficultyLevel level)
        {
            int index = Mathf.Clamp((int)level - 1, 0, PlayerEatTable.Length - 1);
            PlayerEatMultiplier = PlayerEatTable[index];
            AiEatMultiplier = AiEatTable[index];
        }

        private static DifficultyLevel ClampLevel(int raw)
        {
            return (DifficultyLevel)Mathf.Clamp(raw, (int)DifficultyLevel.Easy, (int)DifficultyLevel.Hard);
        }
    }
}
