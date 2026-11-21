using UnityEngine;
using UnityEngine.SceneManagement;

namespace QiuQiu
{
    // the only two ways out of a scene, both of them unpause first
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
            // a paused or finished round leaves timeScale at 0, the next scene would start frozen
            Time.timeScale = 1f;
            AudioListener.pause = false;
            SceneManager.LoadScene(sceneName);
        }
    }
}
