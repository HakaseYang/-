using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

namespace QiuQiu.EditorTools
{
    /// <summary>
    /// 第七步一键搭建（仅编辑器使用）：在第五步场景 + 第六步 HUD/结算面板的基础上，
    /// 追加「顶部倒计时」与「复活倒计时」两块 UI，并把 GameManager / CameraFollow 调成
    /// 「3 分钟一局 + 无限复活 + 竖屏霸屏」的参数。
    ///
    /// 产出结构（MobileUI 画布下）：
    ///   MobileUI
    ///     ├─ RoundTimer  (Text "03:00"，顶部正中)         ← RoundTimerUI
    ///     ├─ HUD         (Mass / Rank，左上角)            ← HUDController
    ///     ├─ RespawnUI   (全屏半透明 + "复活倒计时 3")     ← RespawnCountdownUI，默认隐藏
    ///     └─ GameOverUI  (全屏结算面板，默认隐藏)          ← GameOverPanel
    ///   GameManager    (倒计时 / 复活 / 补员 / 结算)      ← GameManager
    /// </summary>
    public static class RoundUIBuilder
    {
        private const string CanvasName = "MobileUI";
        private const string TimerName = "RoundTimer";
        private const string RespawnName = "RespawnUI";
        private const string BallSpritePath = "Assets/Sprites/Ball.png";

        [MenuItem("Tools/球球大作战/8. 搭建第七步场景（竖屏霸屏相机 + 复活 + 倒计时）", false, 9)]
        public static void BuildStep7Scene()
        {
            // 1) 重建第五步的完整场景（竞技区 + AI + 食物 + 移动端 UI）
            PrototypeSetup.BuildArenaAIScene();

            // 2) 叠加第六步的 HUD / 结算面板 / GameManager
            GameUIBuilder.BuildUIOnly();

            GameObject canvas = GameObject.Find(CanvasName);
            if (canvas == null)
            {
                EditorUtility.DisplayDialog("第七步", $"场景里找不到 {CanvasName} 画布，先执行菜单 6/7。", "好");
                return;
            }

            Sprite sprite = LoadAssetWithImport<Sprite>(BallSpritePath);

            // 3) 顶部倒计时
            BuildTimer(canvas.transform);

            // 4) 复活倒计时
            BuildRespawnUI(canvas.transform);

            // 5) 参数：相机霸屏 + GameManager 局时长/复活/补员
            ConfigureCamera();
            ConfigureGameManager(sprite);

            AssetDatabase.SaveAssets();
            EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
            EditorSceneManager.SaveOpenScenes();

            Debug.Log("[RoundUIBuilder] 第七步完成：顶部倒计时 + 复活倒计时 UI，相机按包围盒自适应取景（无上限），GameManager 3 分钟/5 秒复活/10 只 AI");
        }

        // ------------------------------ 顶部倒计时 ------------------------------

        private static void BuildTimer(Transform canvasRoot)
        {
            var old = canvasRoot.Find(TimerName);
            if (old != null) Object.DestroyImmediate(old.gameObject);

            var go = new GameObject(TimerName, typeof(RectTransform), typeof(RoundTimerUI));
            var rect = (RectTransform)go.transform;
            rect.SetParent(canvasRoot, false);

            rect.anchorMin = new Vector2(0.5f, 1f);
            rect.anchorMax = new Vector2(0.5f, 1f);
            rect.pivot = new Vector2(0.5f, 1f);
            rect.anchoredPosition = new Vector2(0f, -28f);
            rect.sizeDelta = new Vector2(360f, 90f);

            Text text = CreateText(go.transform, "TimerText", "03:00", 66, TextAnchor.MiddleCenter,
                new Vector2(0f, 0f), new Vector2(1f, 1f), Vector2.zero, Vector2.zero,
                new Color(0.95f, 0.98f, 1f, 0.95f), FontStyle.Bold);

            var ui = go.GetComponent<RoundTimerUI>();
            var so = new SerializedObject(ui);
            so.FindProperty("timerText").objectReferenceValue = text;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        // ------------------------------ 复活倒计时 ------------------------------

        private static void BuildRespawnUI(Transform canvasRoot)
        {
            var old = canvasRoot.Find(RespawnName);
            if (old != null) Object.DestroyImmediate(old.gameObject);

            // 根物体常驻激活（组件挂在它上面），内部 Content 子容器默认关闭 → 由脚本显隐。
            // 注意不能把组件挂在会被 SetActive(false) 的物体上，否则脚本一停就再也显示不出来。
            var rootGo = new GameObject(RespawnName, typeof(RectTransform), typeof(RespawnCountdownUI));
            var rootRect = (RectTransform)rootGo.transform;
            rootRect.SetParent(canvasRoot, false);
            Stretch(rootRect);

            var content = new GameObject("Content", typeof(RectTransform));
            var contentRect = (RectTransform)content.transform;
            contentRect.SetParent(rootRect, false);
            Stretch(contentRect);

            // 半透明遮罩（不接收射线，避免影响 SPLIT/SPIT 按钮）
            var dim = new GameObject("Dim", typeof(RectTransform), typeof(Image));
            var dimRect = (RectTransform)dim.transform;
            dimRect.SetParent(contentRect, false);
            Stretch(dimRect);
            var dimImage = dim.GetComponent<Image>();
            dimImage.color = new Color(0.02f, 0.04f, 0.07f, 0.45f);
            dimImage.raycastTarget = false;

            Text countdown = CreateText(contentRect, "CountdownText", "复活倒计时 5", 78, TextAnchor.MiddleCenter,
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, 60f),
                new Vector2(760f, 120f), new Color(1f, 0.92f, 0.55f), FontStyle.Bold);

            Text hint = CreateText(contentRect, "HintText", "你的球被吃掉了，稍后会在地图安全处重生", 36,
                TextAnchor.MiddleCenter, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                new Vector2(0f, -40f), new Vector2(860f, 80f), new Color(0.85f, 0.9f, 1f, 0.9f));

            var ui = rootGo.GetComponent<RespawnCountdownUI>();
            var so = new SerializedObject(ui);
            so.FindProperty("countdownText").objectReferenceValue = countdown;
            so.FindProperty("hintText").objectReferenceValue = hint;
            so.FindProperty("root").objectReferenceValue = content;   // 只开关 Content
            so.ApplyModifiedPropertiesWithoutUndo();

            // 默认隐藏
            content.SetActive(false);
        }

        // ------------------------------ 参数配置 ------------------------------

        private static void ConfigureCamera()
        {
            var follow = Object.FindFirstObjectByType<CameraFollow>();
            if (follow == null)
            {
                Debug.LogWarning("[RoundUIBuilder] 场景里没有 CameraFollow，跳过相机参数配置");
                return;
            }

            var so = new SerializedObject(follow);

            // 取景：只按包围盒算，不设上限（maxOrthographicSize <= 0 = 无限拉远）
            SetFloat(so, "padding", 1.5f);              // 四边安全边距（世界单位）
            SetFloat(so, "radiusScale", 1.1f);          // 半径余量系数
            SetFloat(so, "minOrthographicSize", 5f);
            SetFloat(so, "maxOrthographicSize", 0f);    // 0 = 不限制

            // 平滑：拉远比拉近快，球快速长大时镜头跟得上
            SetFloat(so, "zoomOutLerpSpeed", 10f);
            SetFloat(so, "zoomInLerpSpeed", 2.5f);
            SetFloat(so, "positionLerpSpeed", 8f);
            SetFloat(so, "maxPanDistance", 12f);        // 超过这个距离直接瞬移，防止镜头横穿地图
            SetBool(so, "snapWhenBallWouldClip", true); // 有球超出画面时本帧补齐

            SetEnum(so, "centerMode", (int)CameraFollow.CenterMode.MassWeightedCenter);

            so.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void ConfigureGameManager(Sprite ballSprite)
        {
            var manager = Object.FindFirstObjectByType<GameManager>();
            if (manager == null)
            {
                Debug.LogWarning("[RoundUIBuilder] 场景里没有 GameManager，跳过参数配置");
                return;
            }

            var so = new SerializedObject(manager);
            SetFloat(so, "roundDuration", 180f);          // 3 分钟
            SetFloat(so, "warnTime", 30f);
            SetFloat(so, "playerRespawnDelay", 5f);       // 复活 CD 5 秒
            SetFloat(so, "playerRespawnMass", 40f);       // 复活基础质量（> 30，复活后立刻能分裂）
            SetFloat(so, "respawnInvulnerability", 2.5f); // 复活保护
            SetFloat(so, "respawnSafeRadius", 10f);
            SetInt(so, "aiMaxCount", 10);
            SetFloat(so, "aiRespawnDelay", 3f);
            SetFloat(so, "aiMinMass", 4f);
            SetFloat(so, "aiMaxMass", 60f);

            SerializedProperty sprite = so.FindProperty("playerSprite");
            if (sprite != null && ballSprite != null) sprite.objectReferenceValue = ballSprite;

            so.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void SetFloat(SerializedObject so, string name, float value)
        {
            SerializedProperty p = so.FindProperty(name);
            if (p != null) p.floatValue = value;
        }

        private static void SetInt(SerializedObject so, string name, int value)
        {
            SerializedProperty p = so.FindProperty(name);
            if (p != null) p.intValue = value;
        }

        private static void SetBool(SerializedObject so, string name, bool value)
        {
            SerializedProperty p = so.FindProperty(name);
            if (p != null) p.boolValue = value;
        }

        /// <summary>设置枚举字段（按枚举的序号）。</summary>
        private static void SetEnum(SerializedObject so, string name, int enumValueIndex)
        {
            SerializedProperty p = so.FindProperty(name);
            if (p != null) p.enumValueIndex = enumValueIndex;
        }

        // ------------------------------ 工具 ------------------------------

        private static void Stretch(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = Vector2.zero;
            rect.sizeDelta = Vector2.zero;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
        }

        private static Text CreateText(Transform parent, string name, string content, int fontSize, TextAnchor anchor,
            Vector2 anchorMin, Vector2 anchorMax, Vector2 anchoredPos, Vector2 size, Color? color = null,
            FontStyle style = FontStyle.Normal)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Text));
            var rect = (RectTransform)go.transform;
            rect.SetParent(parent, false);

            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.pivot = new Vector2(0.5f, 0.5f);

            if (size == Vector2.zero)
            {
                rect.offsetMin = Vector2.zero;
                rect.offsetMax = Vector2.zero;
            }
            else
            {
                rect.anchoredPosition = anchoredPos;
                rect.sizeDelta = size;
            }

            var text = go.GetComponent<Text>();
            text.font = GetBuiltinFont();
            text.fontSize = fontSize;
            text.fontStyle = style;
            text.alignment = anchor;
            text.text = content;
            text.color = color ?? Color.white;
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

        private static T LoadAssetWithImport<T>(string path) where T : Object
        {
            T asset = AssetDatabase.LoadAssetAtPath<T>(path);
            if (asset == null && System.IO.File.Exists(path))
            {
                AssetDatabase.ImportAsset(path,
                    ImportAssetOptions.ForceUpdate | ImportAssetOptions.ForceSynchronousImport);
                asset = AssetDatabase.LoadAssetAtPath<T>(path);
            }

            if (asset == null) Debug.LogWarning($"[RoundUIBuilder] 加载失败：{path}");
            return asset;
        }
    }
}
