using UnityEngine;

namespace QiuQiu
{
    [DisallowMultipleComponent]
    public class MainMenuController : MonoBehaviour
    {
        public void Play(int level)
        {
            if (AudioManager.Instance != null)
            {
                AudioManager.Instance.PlayUIClick();
            }

            GameSettings.SetDifficulty(level);
            Debug.Log($"[menu] {GameSettings.GetDisplayName(GameSettings.Difficulty)} | " +
                      $"eat x{GameSettings.PlayerEatMultiplier:F2} / ai x{GameSettings.AiEatMultiplier:F2} | " +
                      $"scene {GameSettings.GameplaySceneName}");

            SceneFlow.LoadGameplay();
        }

        public void PlayEasy()
        {
            Play((int)DifficultyLevel.Easy);
        }

        public void PlayNormal()
        {
            Play((int)DifficultyLevel.Normal);
        }

        public void PlayHard()
        {
            Play((int)DifficultyLevel.Hard);
        }

        public void QuitGame()
        {
            if (AudioManager.Instance != null)
            {
                AudioManager.Instance.PlayUIClick();
            }

            Debug.Log("[menu] quit");

#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }
    }
}
