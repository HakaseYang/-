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

        // 同一套下标：给日志看的长名字 / 给玩家看的中文名
        private static readonly string[] LogNames = { "Level 1 (Easy)", "Level 2 (Normal)", "Level 3 (Hard)" };
        private static readonly string[] PlayerNames = { "简单", "普通", "困难" };

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

        public static string GetShortName(DifficultyLevel level)
        {
            return PlayerNames[(int)ClampLevel((int)level) - 1];
        }

        // 唯一改状态的地方：存难度 + 同步倍率，两件事分不开
        private static void SetLevel(DifficultyLevel level)
        {
            difficulty = ClampLevel((int)level);
            int index = (int)difficulty - 1;
            PlayerEatMultiplier = PlayerEatTable[index];
            AiEatMultiplier = AiEatTable[index];
        }

        // 外部能传 0、99、(DifficultyLevel)7，这里是唯一的守门人
        private static DifficultyLevel ClampLevel(int raw)
        {
            return (DifficultyLevel)Mathf.Clamp(raw, (int)DifficultyLevel.Easy, (int)DifficultyLevel.Hard);
        }
    }
}
