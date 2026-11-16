using System;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace QiuQiu.EditorTools
{
    /// <summary>
    /// 把项目切成「横屏」：Player Settings 的默认方向 + 编辑器 Game 视图的预览分辨率。
    /// 两个都可以单独用，也可以按「一键横屏」一次做完。
    /// GameViewSizes 是 internal API，只能反射访问（做法同 GameViewPortrait.cs）。
    /// </summary>
    public static class GameViewLandscape
    {
        [MenuItem("Tools/球球大作战/横屏/一键横屏（Player + Game 视图）", false, 30)]
        public static void ApplyAll()
        {
            SetPlayerLandscape();
            SetGameViewLandscape();
        }

        [MenuItem("Tools/球球大作战/横屏/Player Settings 设为横屏", false, 31)]
        public static void SetPlayerLandscape()
        {
            PlayerSettings.defaultInterfaceOrientation = UIOrientation.LandscapeLeft;
            PlayerSettings.allowedAutorotateToPortrait = false;
            PlayerSettings.allowedAutorotateToPortraitUpsideDown = false;
            PlayerSettings.allowedAutorotateToLandscapeLeft = true;
            PlayerSettings.allowedAutorotateToLandscapeRight = true;
            PlayerSettings.defaultScreenWidth = 1920;
            PlayerSettings.defaultScreenHeight = 1080;

            AssetDatabase.SaveAssets();
            Debug.Log($"[Orientation] default={PlayerSettings.defaultInterfaceOrientation} " +
                      $"autoPortrait={PlayerSettings.allowedAutorotateToPortrait} " +
                      $"autoPortraitUp={PlayerSettings.allowedAutorotateToPortraitUpsideDown} " +
                      $"autoLandLeft={PlayerSettings.allowedAutorotateToLandscapeLeft} " +
                      $"autoLandRight={PlayerSettings.allowedAutorotateToLandscapeRight} " +
                      $"default={PlayerSettings.defaultScreenWidth}x{PlayerSettings.defaultScreenHeight}");
        }

        [MenuItem("Tools/球球大作战/横屏/Game 视图设为 1920x1080 横屏", false, 32)]
        public static void SetGameViewLandscape()
        {
            SetGameViewSize(1920, 1080, "Landscape 1920x1080");
        }

        [MenuItem("Tools/球球大作战/横屏/Game 视图恢复 Free Aspect", false, 33)]
        public static void RestoreFreeAspect()
        {
            try
            {
                var gameViewType = Type.GetType("UnityEditor.GameView,UnityEditor");
                var gameView = EditorWindow.GetWindow(gameViewType);
                gameViewType.GetProperty("selectedSizeIndex",
                        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                    ?.SetValue(gameView, 0);
                gameView.Repaint();
                Debug.Log("[GameView] 已切回 Free Aspect");
            }
            catch (Exception e)
            {
                Debug.LogWarning("[GameView] 恢复失败：" + e);
            }
        }

        private static void SetGameViewSize(int width, int height, string label)
        {
            try
            {
                var gameViewType = Type.GetType("UnityEditor.GameView,UnityEditor");
                if (gameViewType == null) { Debug.LogWarning("[GameView] 找不到 GameView 类型"); return; }
                var gameView = EditorWindow.GetWindow(gameViewType);

                var sizesType = Type.GetType("UnityEditor.GameViewSizes,UnityEditor");
                var singletonType = typeof(ScriptableSingleton<>).MakeGenericType(sizesType);
                var instance = singletonType.GetProperty("instance", BindingFlags.Public | BindingFlags.Static).GetValue(null);
                var group = sizesType.GetProperty("currentGroup", BindingFlags.Public | BindingFlags.Instance).GetValue(instance);
                var groupType = group.GetType();

                int count = (int)groupType.GetMethod("GetTotalCount").Invoke(group, null);
                var getSize = groupType.GetMethod("GetGameViewSize");
                int index = -1;
                string found = null;

                for (int i = 0; i < count; i++)
                {
                    var size = getSize.Invoke(group, new object[] { i });
                    var sizeType = size.GetType();
                    int w = (int)sizeType.GetProperty("width").GetValue(size);
                    int h = (int)sizeType.GetProperty("height").GetValue(size);
                    if (w == width && h == height)
                    {
                        index = i;
                        found = sizeType.GetProperty("baseLabel")?.GetValue(size) as string;
                        break;
                    }
                }

                if (index < 0)
                {
                    var sizeType = Type.GetType("UnityEditor.GameViewSize,UnityEditor");
                    var sizeEnum = Type.GetType("UnityEditor.GameViewSizeType,UnityEditor");
                    var ctor = sizeType.GetConstructor(new[] { sizeEnum, typeof(int), typeof(int), typeof(string) });
                    var newSize = ctor.Invoke(new object[]
                        { Enum.Parse(sizeEnum, "FixedResolution"), width, height, label });
                    groupType.GetMethod("AddCustomSize", new[] { sizeType }).Invoke(group, new[] { newSize });
                    count = (int)groupType.GetMethod("GetTotalCount").Invoke(group, null);
                    index = count - 1;
                    found = label;
                }

                gameViewType.GetProperty("selectedSizeIndex",
                        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                    ?.SetValue(gameView, index);
                gameView.Repaint();
                Debug.Log($"[GameView] 已切到 {found}（{width}x{height}，索引 {index}）");
            }
            catch (Exception e)
            {
                Debug.LogWarning("[GameView] 设置横屏失败：" + e);
            }
        }
    }
}
