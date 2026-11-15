using UnityEditor;
using UnityEditor.Events;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.OnScreen;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

namespace QiuQiu.EditorTools
{
    /// <summary>
    /// 移动端 UI 一键搭建（仅编辑器使用）。
    ///
    /// 产出结构（Canvas 竖屏 1080x1920，Scale With Screen Size）：
    ///   MobileUI (Canvas + CanvasScaler + GraphicRaycaster)
    ///     ├─ LeftStick           摇杆底盘（Image，纯视觉）
    ///     │    └─ Handle         OnScreenStick（controlPath = &lt;Gamepad&gt;/leftStick）
    ///     ├─ BtnSplit            分裂按钮 -> PlayerController.TriggerSplit()
    ///     └─ BtnSpit             吐球按钮 -> PlayerController.TriggerSpit()
    ///   EventSystem               InputSystemUIInputModule（不是旧的 StandaloneInputModule）
    ///
    /// 关键点：OnScreenStick 会在运行时自动创建一台虚拟 Gamepad 设备（usage = OnScreen），
    /// 而 Controls.inputactions 里 Move 本来就绑了 &lt;Gamepad&gt;/leftStick，
    /// 所以摇杆推一下就直接驱动了玩家移动，不需要额外接线。
    /// </summary>
    public static class MobileUIBuilder
    {
        private const string CanvasName = "MobileUI";
        private const string BallSpritePath = "Assets/Sprites/Ball.png";

        [MenuItem("Tools/球球大作战/5. 搭建移动端 UI（摇杆 + 分裂/吐球按钮）", false, 5)]
        public static void BuildMobileUIFromMenu()
        {
            var player = Object.FindFirstObjectByType<PlayerController>();
            if (player == null)
            {
                EditorUtility.DisplayDialog("移动端 UI", "当前场景里找不到挂 PlayerController 的 Player，请先执行菜单 4。", "好");
                return;
            }

            BuildMobileUI(player);
            AssetDatabase.SaveAssets();
            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(
                UnityEditor.SceneManagement.EditorSceneManager.GetActiveScene());
            UnityEditor.SceneManagement.EditorSceneManager.SaveOpenScenes();
            Debug.Log("[MobileUIBuilder] 移动端 UI 搭建完成：Canvas(1080x1920) + OnScreenStick(<Gamepad>/leftStick) + BtnSplit/BtnSpit");
        }

        /// <summary>搭出整套移动端 UI，并把两个按钮的 OnClick 指向 PlayerController。</summary>
        public static GameObject BuildMobileUI(PlayerController player)
        {
            Sprite circle = AssetDatabase.LoadAssetAtPath<Sprite>(BallSpritePath);
            if (circle == null)
                circle = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/Knob.psd");

            // ---------- Canvas ----------
            var old = GameObject.Find(CanvasName);
            if (old != null) Object.DestroyImmediate(old);

            var canvasGo = new GameObject(CanvasName, typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            var canvas = canvasGo.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 100;

            var scaler = canvasGo.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1080f, 1920f);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;

            // ---------- EventSystem（Input System UI Input Module） ----------
            EnsureEventSystem();

            // ---------- 左下：摇杆 ----------
            var stickArea = CreateUI(CanvasName + "/LeftStick", canvasGo.transform);
            var areaRect = (RectTransform)stickArea.transform;
            areaRect.anchorMin = new Vector2(0f, 0f);
            areaRect.anchorMax = new Vector2(0f, 0f);
            areaRect.pivot = new Vector2(0.5f, 0.5f);
            areaRect.anchoredPosition = new Vector2(250f, 330f);
            areaRect.sizeDelta = new Vector2(420f, 420f);
            var areaImage = stickArea.AddComponent<Image>();
            areaImage.sprite = circle;
            areaImage.color = new Color(1f, 1f, 1f, 0.12f);
            areaImage.raycastTarget = false;   // 视觉底盘不吃点击，点击交给 Handle 的动态原点区域

            var handle = CreateUI(CanvasName + "/LeftStick/Handle", stickArea.transform);
            var handleRect = (RectTransform)handle.transform;
            handleRect.anchorMin = new Vector2(0.5f, 0.5f);
            handleRect.anchorMax = new Vector2(0.5f, 0.5f);
            handleRect.pivot = new Vector2(0.5f, 0.5f);
            handleRect.anchoredPosition = Vector2.zero;
            handleRect.sizeDelta = new Vector2(170f, 170f);
            var handleImage = handle.AddComponent<Image>();
            handleImage.sprite = circle;
            handleImage.color = new Color(0.85f, 0.93f, 1f, 0.75f);

            var stick = handle.AddComponent<OnScreenStick>();
            var stickSo = new SerializedObject(stick);
            stickSo.FindProperty("m_ControlPath").stringValue = "<Gamepad>/leftStick";
            stickSo.FindProperty("m_MovementRange").floatValue = 130f;
            // 2 = ExactPositionWithDynamicOrigin：手指按在底盘任意位置，摇杆中心跟过去（手游常见手感）
            stickSo.FindProperty("m_Behaviour").enumValueIndex = 2;
            stickSo.FindProperty("m_DynamicOriginRange").floatValue = 200f;
            stickSo.ApplyModifiedPropertiesWithoutUndo();

            // ---------- 右下：两个按钮 ----------
            // SPLIT 靠角（主操作，最大最好按），SPIT 放在它左上一点：
            // 摇杆底盘右侧边缘到 x=460，按钮从 x=735 起，两者不重叠。
            CreateRoundButton(canvasGo.transform, "BtnSplit", "SPLIT",
                new Vector2(-230f, 260f), 250f, new Color(1f, 0.45f, 0.45f, 0.92f),
                circle, player, true);

            CreateRoundButton(canvasGo.transform, "BtnSpit", "SPIT",
                new Vector2(-250f, 570f), 190f, new Color(0.55f, 0.9f, 1f, 0.92f),
                circle, player, false);

            return canvasGo;
        }

        private static void CreateRoundButton(Transform parent, string name, string label, Vector2 anchoredPos,
            float size, Color color, Sprite circle, PlayerController player, bool isSplit)
        {
            var go = CreateUI("MobileUI/" + name, parent);
            var rect = (RectTransform)go.transform;
            rect.anchorMin = new Vector2(1f, 0f);
            rect.anchorMax = new Vector2(1f, 0f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = anchoredPos;
            rect.sizeDelta = new Vector2(size, size);

            var image = go.AddComponent<Image>();
            image.sprite = circle;
            image.color = color;

            var button = go.AddComponent<Button>();
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

            // 文字（用内置字体；只放英文，避免中文字体缺失）
            var textGo = CreateUI("MobileUI/" + name + "/Label", go.transform);
            var textRect = (RectTransform)textGo.transform;
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.offsetMin = Vector2.zero;
            textRect.offsetMax = Vector2.zero;
            var text = textGo.AddComponent<Text>();
            text.text = label;
            text.font = GetBuiltinFont();
            text.fontSize = Mathf.RoundToInt(size * 0.24f);
            text.alignment = TextAnchor.MiddleCenter;
            text.color = new Color(0.08f, 0.1f, 0.14f, 0.95f);
            text.raycastTarget = false;

            // OnClick -> PlayerController.TriggerSplit / TriggerSpit（持久化监听，Inspector 里看得见）
            if (isSplit) UnityEventTools.AddPersistentListener(button.onClick, player.TriggerSplit);
            else UnityEventTools.AddPersistentListener(button.onClick, player.TriggerSpit);
        }

        private static void EnsureEventSystem()
        {
            var existing = Object.FindFirstObjectByType<EventSystem>();
            if (existing != null) Object.DestroyImmediate(existing.gameObject);

            var go = new GameObject("EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));
            var module = go.GetComponent<InputSystemUIInputModule>();
            module.deselectOnBackgroundClick = false;
            module.pointerBehavior = UIPointerBehavior.SingleMouseOrPenButMultiTouchAndTrack;
        }

        private static GameObject CreateUI(string name, Transform parent)
        {
            int slash = name.LastIndexOf('/');
            var go = new GameObject(slash >= 0 ? name.Substring(slash + 1) : name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            return go;
        }

        private static Font GetBuiltinFont()
        {
            // Unity 6 内置字体更名为 LegacyRuntime.ttf（Arial.ttf 已移除），两个都试一下做兼容
            Font font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            if (font == null) font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            return font;
        }
    }
}
