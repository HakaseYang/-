using UnityEngine;

namespace QiuQiu
{

    public enum DifficultyLevel
    {
        Easy = 1,
        Normal = 2,
        Hard = 3,
    }

    public static class GameSettings
    {
        public const string GameplaySceneName = "LevelScene";
        public const string MainMenuSceneName = "MainMenu";

        // Difficulty → Devour Multiplier
        private static readonly float[] PlayerEatTable = { 1.5f, 1f, 0.5f };
        private static readonly float[] AiEatTable = { 0.5f, 1f, 1.5f };

        // 给日志/报错看的长名字，下标 = 难度 - 1
        private static readonly string[] LogNames = { "Level 1 (Easy)", "Level 2 (Normal)", "Level 3 (Hard)" };

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

        // 主菜单按钮传的是 1 / 2 / 3
        public static void SetDifficulty(int level)
        {
            Difficulty = ClampLevel(level);
        }

        public static string GetDisplayName(DifficultyLevel level)
        {
            return LogNames[(int)ClampLevel((int)level) - 1];
        }

        // 改难度 = 顺手把倍率一起换成同一档
        private static void SetLevel(DifficultyLevel level)
        {
            difficulty = ClampLevel((int)level);
            int index = (int)difficulty - 1;
            PlayerEatMultiplier = PlayerEatTable[index];
            AiEatMultiplier = AiEatTable[index];
        }

        
        private static DifficultyLevel ClampLevel(int raw)
        {
            return (DifficultyLevel)Mathf.Clamp(raw, (int)DifficultyLevel.Easy, (int)DifficultyLevel.Hard);
        }
    }
}
