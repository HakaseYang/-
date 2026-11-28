using UnityEngine;

namespace QiuQiu
{
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
