using UnityEditor;
using UnityEditor.Events;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace QiuQiu.EditorTools
{
    /// <summary>
    /// 主菜单场景 + 难度系统的一键搭建。
    ///
    /// 菜单：Tools/球球大作战/9. 创建主菜单场景 + 难度系统
    ///
    /// 产物：
    ///   Assets/Scenes/MainMenu.unity —— Build Settings 索引 0
    ///     └─ Main Camera（纯色背景）
    ///     └─ Canvas（1080×1920，Scale With Screen Size）
    ///          ├─ Title           "球球大作战"
    ///          ├─ DifficultyLabel "当前难度：普通"
    ///          ├─ BestMassLabel   "历史最高质量：—"
    ///          ├─ BtnEasy         "Level 1 (Easy)"   → MainMenuController.Play(1)
    ///          ├─ BtnNormal       "Level 2 (Normal)" → MainMenuController.Play(2)
    ///          ├─ BtnHard         "Level 3 (Hard)"   → MainMenuController.Play(3)
    ///          └─ BtnQuit         "Quit Game"        → MainMenuController.QuitGame()
    ///     └─ EventSystem（Input System UI Input Module）
    ///     └─ MainMenuManager（MainMenuController）
    ///   Build Settings：MainMenu = 0，SampleScene = 1
    /// </summary>
    public static class MainMenuBuilder
    {
        private const string MenuScenePath = "Assets/Scenes/MainMenu.unity";
        private const string GameplayScenePath = "Assets/Scenes/SampleScene.unity";

        private static readonly Color BackdropColor = new Color(0.055f, 0.075f, 0.11f, 1f);

        [MenuItem("Tools/球球大作战/9. 创建主菜单场景 + 难度系统", false, 10)]
        public static void BuildMainMenuScene()
        {
            EditorSceneManager.SaveOpenScenes();

            // 新建空场景（不要默认相机/灯光，自己搭，保证画面风格一致）
            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            BuildCamera();
            Canvas canvas = BuildCanvas();
            BuildTitle(canvas.transform);
            MainMenuController controller = BuildManager(canvas.transform);
            BuildButtons(canvas.transform, controller);
            EnsureEventSystem();

            // 保存场景
            EditorSceneManager.SaveScene(scene, MenuScenePath);
            AssetDatabase.Refresh();

            ConfigureBuildSettings();

            Debug.Log($"[MainMenuBuilder] 主菜单场景已生成：{MenuScenePath}（Build Settings 索引 0），" +
                      $"游戏场景 {GameplayScenePath}（索引 1）");
        }

        [MenuItem("Tools/球球大作战/9b. 只修 Build Settings 顺序", false, 11)]
        public static void FixBuildSettings()
        {
            ConfigureBuildSettings();
            Debug.Log("[MainMenuBuilder] Build Settings 已重排：MainMenu = 0，SampleScene = 1");
        }

        private static void ConfigureBuildSettings()
        {
            var scenes = new[]
            {
                new EditorBuildSettingsScene(MenuScenePath, true),
                new EditorBuildSettingsScene(GameplayScenePath, true),
            };

            EditorBuildSettings.scenes = scenes;

            // EditorBuildSettings 的修改只驻留在内存里，必须显式落盘，
            // 否则 ProjectSettings/EditorBuildSettings.asset 还是旧的顺序（打包会漏掉主菜单）。
            AssetDatabase.SaveAssets();
            EditorApplication.ExecuteMenuItem("File/Save Project");
        }

        // ------------------------------ 场景内容 ------------------------------

        private static void BuildCamera()
        {
            var go = new GameObject("Main Camera", typeof(Camera), typeof(AudioListener));
            go.tag = "MainCamera";
            go.transform.position = new Vector3(0f, 0f, -10f);

            var cam = go.GetComponent<Camera>();
            cam.orthographic = true;
            cam.orthographicSize = 8f;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = BackdropColor;
        }

        private static Canvas BuildCanvas()
        {
            var go = new GameObject("Canvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            var canvas = go.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 100;

            var scaler = go.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1080f, 1920f);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;

            go.GetComponent<GraphicRaycaster>();

            return canvas;
        }

        private static void BuildTitle(Transform canvas)
        {
            Text title = CreateText(canvas, "Title", "球球大作战", 108, TextAnchor.MiddleCenter,
                new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -240f), new Vector2(900f, 150f),
                new Color(0.55f, 0.82f, 1f, 1f));
            title.fontStyle = FontStyle.Bold;

            Text subtitle = CreateText(canvas, "Subtitle", "分裂 · 吞噬 · 称霸", 42, TextAnchor.MiddleCenter,
                new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -340f), new Vector2(900f, 70f),
                new Color(0.72f, 0.82f, 0.95f, 0.9f));
            subtitle.fontStyle = FontStyle.Normal;
        }

        private static MainMenuController BuildManager(Transform canvas)
        {
            var go = new GameObject("MainMenuManager", typeof(MainMenuController));
            var controller = go.GetComponent<MainMenuController>();

            Text difficultyLabel = CreateText(canvas, "DifficultyLabel", "当前难度：普通", 40, TextAnchor.MiddleCenter,
                new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 300f), new Vector2(900f, 60f),
                new Color(0.78f, 0.88f, 1f, 0.95f));

            Text bestMassLabel = CreateText(canvas, "BestMassLabel", "历史最高质量：—", 36, TextAnchor.MiddleCenter,
                new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 240f), new Vector2(900f, 60f),
                new Color(0.66f, 0.76f, 0.9f, 0.85f));

            var so = new SerializedObject(controller);
            so.FindProperty("gameplaySceneName").stringValue = GameSettings.GameplaySceneName;
            so.FindProperty("difficultyLabel").objectReferenceValue = difficultyLabel;
            so.FindProperty("bestMassLabel").objectReferenceValue = bestMassLabel;
            so.ApplyModifiedPropertiesWithoutUndo();

            return controller;
        }

        private static void BuildButtons(Transform canvas, MainMenuController controller)
        {
            CreateMenuButton(canvas, "BtnEasy", "Level 1 (Easy)", new Vector2(0f, 330f),
                new Color(0.42f, 0.86f, 0.62f, 0.96f), controller, 1);

            CreateMenuButton(canvas, "BtnNormal", "Level 2 (Normal)", new Vector2(0f, 110f),
                new Color(0.44f, 0.72f, 1f, 0.96f), controller, 2);

            CreateMenuButton(canvas, "BtnHard", "Level 3 (Hard)", new Vector2(0f, -110f),
                new Color(1f, 0.55f, 0.5f, 0.96f), controller, 3);

            CreateMenuButton(canvas, "BtnQuit", "Quit Game", new Vector2(0f, -400f),
                new Color(0.36f, 0.4f, 0.48f, 0.96f), controller, 0);
        }

        /// <summary>level = 1/2/3 走 Play(level)；level = 0 走 QuitGame()。</summary>
        private static void CreateMenuButton(Transform canvas, string name, string label, Vector2 anchoredPosition,
            Color color, MainMenuController controller, int level)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button));
            var rect = (RectTransform)go.transform;
            rect.SetParent(canvas, false);
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = level == 0 ? new Vector2(620f, 120f) : new Vector2(760f, 150f);
            rect.anchoredPosition = anchoredPosition;

            var image = go.GetComponent<Image>();
            image.color = color;

            var button = go.GetComponent<Button>();
            button.targetGraphic = image;
            button.transition = Selectable.Transition.ColorTint;
            button.colors = new ColorBlock
            {
                normalColor = Color.white,
                highlightedColor = new Color(0.92f, 0.92f, 0.92f, 1f),
                pressedColor = new Color(0.72f, 0.72f, 0.72f, 1f),
                selectedColor = Color.white,
                disabledColor = new Color(0.5f, 0.5f, 0.5f, 0.5f),
                colorMultiplier = 1f,
                fadeDuration = 0.08f,
            };

            Text text = CreateText(go.transform, "Label", label, level == 0 ? 46 : 52, TextAnchor.MiddleCenter,
                Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero,
                new Color(0.05f, 0.08f, 0.1f, 0.95f));
            text.fontStyle = FontStyle.Bold;

            if (level == 0)
            {
                UnityEventTools.AddPersistentListener(button.onClick, controller.QuitGame);
            }
            else
            {
                UnityEventTools.AddIntPersistentListener(button.onClick, controller.Play, level);
            }
        }

        private static void EnsureEventSystem()
        {
            EventSystem existing = Object.FindFirstObjectByType<EventSystem>();
            if (existing != null) Object.DestroyImmediate(existing.gameObject);

            var go = new GameObject("EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));
            var module = go.GetComponent<InputSystemUIInputModule>();
            module.deselectOnBackgroundClick = false;
        }

        private static Text CreateText(Transform parent, string name, string content, int fontSize, TextAnchor anchor,
            Vector2 anchorMin, Vector2 anchorMax, Vector2 anchoredPos, Vector2 size, Color color)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Text));
            var rect = (RectTransform)go.transform;
            rect.SetParent(parent, false);
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = anchoredPos;
            rect.sizeDelta = size;

            var text = go.GetComponent<Text>();
            text.text = content;
            text.font = GetBuiltinFont();
            text.fontSize = fontSize;
            text.alignment = anchor;
            text.color = color;
            text.raycastTarget = false;
            text.horizontalOverflow = HorizontalWrapMode.Overflow;
            text.verticalOverflow = VerticalWrapMode.Overflow;

            return text;
        }

        private static Font GetBuiltinFont()
        {
            Font font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            if (font == null) font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            return font;
        }
    }
}
