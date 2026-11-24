using UnityEngine;

namespace QiuQiu
{
    /// <summary>
    /// Writes one line before the splash screen and one line after the first scene loads.
    /// A build that dies on the Unity logo prints neither (the engine never finished starting);
    /// a build that dies while loading a scene prints the first line only. That difference is
    /// the whole diagnosis, and it costs two log lines.
    /// </summary>
    public static class BootProbe
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSplashScreen)]
        private static void BeforeSplash()
        {
            Debug.Log("[boot] BeforeSplashScreen | " + SystemInfo.operatingSystem +
                      " | " + SystemInfo.graphicsDeviceType + " " + SystemInfo.graphicsDeviceVersion +
                      " | " + SystemInfo.processorCount + " cores / " + SystemInfo.systemMemorySize + " MB");
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void AfterScene()
        {
            Debug.Log("[boot] AfterSceneLoad | scene=" + UnityEngine.SceneManagement.SceneManager.GetActiveScene().name);
        }
    }
}
