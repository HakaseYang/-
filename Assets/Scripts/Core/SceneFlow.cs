using UnityEngine;
using UnityEngine.SceneManagement;

namespace QiuQiu
{
    public static class SceneFlow
    {
        public static void LoadGameplay()
        {
            Load(GameSettings.GameplaySceneName);
        }

        public static void LoadMainMenu()
        {
            Load(GameSettings.MainMenuSceneName);
        }

        public static void Reload()
        {
            Load(SceneManager.GetActiveScene().name);
        }

        private static void Load(string sceneName)
        {
            Time.timeScale = 1f;
            AudioListener.pause = false;
            SceneManager.LoadScene(sceneName);
        }
    }
}
