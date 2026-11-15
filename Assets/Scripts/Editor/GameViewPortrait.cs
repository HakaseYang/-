using System;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace QiuQiu.EditorTools
{
    /// <summary>
    /// 把编辑器 Game 视图切到手机竖屏分辨率（1080×1920），这样在编辑器里看到的构图
    /// 就和真机一致；再切回 Free Aspect 即可恢复。
    /// 实现方式：Unity 的 GameViewSizes 是 internal API，只能用反射访问。
    /// </summary>
    public static class GameViewPortrait
    {
        [MenuItem("Tools/球球大作战/设为竖屏 1080x1920（Game 视图）", false, 50)]
        public static void SetPortrait()
        {
            SetGameViewSize(1080, 1920, "Portrait 1080x1920");
        }

        [MenuItem("Tools/球球大作战/恢复 Free Aspect（Game 视图）", false, 51)]
        public static void RestoreFree()
        {
            var gameViewType = Type.GetType("UnityEditor.GameView,UnityEditor");
            if (gameViewType == null) { Debug.LogWarning("[GameView] 找不到 GameView 类型"); return; }
            var gameView = EditorWindow.GetWindow(gameViewType);
            var sizesType = Type.GetType("UnityEditor.GameViewSizes,UnityEditor");
            var singleton = typeof(ScriptableSingleton<>).MakeGenericType(sizesType);
            var instance = singleton.GetProperty("instance", BindingFlags.Public | BindingFlags.Static).GetValue(null);
            var currentGroup = sizesType.GetProperty("currentGroup", BindingFlags.Public | BindingFlags.Instance).GetValue(instance);
            var groupType = currentGroup.GetType();
            // Free Aspect 是 group 里的固定索引 0
            gameViewType.GetProperty("selectedSizeIndex", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                ?.SetValue(gameView, 0);
            gameView.Repaint();
            Debug.Log("[GameView] 已切回 Free Aspect");
        }

        private static void SetGameViewSize(int width, int height, string label)
        {
            try
            {
                var gameViewType = Type.GetType("UnityEditor.GameView,UnityEditor");
                if (gameViewType == null) { Debug.LogWarning("[GameView] 找不到 GameView 类型"); return; }
                var gameView = EditorWindow.GetWindow(gameViewType);

                var sizesType = Type.GetType("UnityEditor.GameViewSizes,UnityEditor");
                var singleton = typeof(ScriptableSingleton<>).MakeGenericType(sizesType);
                var instance = singleton.GetProperty("instance", BindingFlags.Public | BindingFlags.Static).GetValue(null);
                var currentGroup = sizesType.GetProperty("currentGroup", BindingFlags.Public | BindingFlags.Instance).GetValue(instance);

                var sizeType = Type.GetType("UnityEditor.GameViewSize,UnityEditor");
                var sizeTypeEnum = Type.GetType("UnityEditor.GameViewSizeType,UnityEditor");
                var ctor = sizeType.GetConstructor(new[] { sizeTypeEnum, typeof(int), typeof(int), typeof(string) });
                object fixedResolution = Enum.Parse(sizeTypeEnum, "FixedResolution");
                var newSize = ctor.Invoke(new object[] { fixedResolution, width, height, label });

                var addSize = currentGroup.GetType().GetMethod("AddCustomSize", new[] { sizeType });
                addSize.Invoke(currentGroup, new[] { newSize });

                // 找到刚加入的索引并选中
                var getCount = currentGroup.GetType().GetMethod("GetTotalCount");
                int count = (int)getCount.Invoke(currentGroup, null);
                gameViewType.GetProperty("selectedSizeIndex", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                    ?.SetValue(gameView, count - 1);
                gameView.Repaint();
                Debug.Log($"[GameView] 已切到 {label}（索引 {count - 1}）");
            }
            catch (Exception e)
            {
                Debug.LogWarning("[GameView] 设置竖屏失败：" + e);
            }
        }
    }
}
