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
        private static readonly float[] AiEatTable = { 0.5f, 1f, 1.2f };

        // for check
        private static readonly string[] LogNames = { "Level 1 (Easy)", "Level 2 (Normal)", "Level 3 (Hard)" };

        public static float PlayerEatMultiplier { get; private set; }
        public static float AiEatMultiplier { get; private set; }

        // AI balance: eating another AI only 50 %,
        // and only moire then 30% of player
        public const float AiVsAiMassShare = 0.5f;
        public const float AiMassCapFactor = 3f;

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

        // for mainmenu
        public static void SetDifficulty(int level)
        {
            Difficulty = ClampLevel(level);
        }

        //for log
        public static string GetDisplayName(DifficultyLevel level)
        {
            return LogNames[(int)ClampLevel((int)level) - 1];
        }

        // Changing difficulty&Multiplier
        private static void SetLevel(DifficultyLevel level)
        {
            difficulty = ClampLevel((int)level);
            int index = (int)difficulty - 1;
            PlayerEatMultiplier = PlayerEatTable[index];
            AiEatMultiplier = AiEatTable[index];
        }

        //sure only 1-3(0-2)
        private static DifficultyLevel ClampLevel(int raw)
        {
            return (DifficultyLevel)Mathf.Clamp(raw, (int)DifficultyLevel.Easy, (int)DifficultyLevel.Hard);
        }
    }
}
