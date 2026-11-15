using UnityEditor;
using UnityEditor.Events;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

namespace QiuQiu.EditorTools
{
    /// <summary>
    /// 第六步 UI 一键搭建（仅编辑器使用）：局内 HUD + 全屏结算面板 + GameManager。
    ///
    /// 产出结构（挂在已有的 MobileUI Canvas 下，竖屏 1080×1920）：
    ///   MobileUI
    ///     ├─ HUD (Text/Mass 左上角 + Text/Rank 其下)   ← HUDController
    ///     └─ GameOverUI (GameOverPanel 组件，常驻激活)
    ///          └─ Panel (全屏半透明底 + 卡片 + 标题 + 两行成绩 + 重新开始按钮，默认隐藏)
    ///   GameManager (独立物体，持有 player / gameOverPanel 引用)
    ///
    /// 关键设计：GameOverPanel 组件挂在**常驻激活**的 GameOverUI 上，
    /// 只有它的子物体 Panel 是隐藏的。这样 Awake 能在开局就跑（绑定按钮监听、隐藏面板），
    /// 不会出现“面板被 Show() 激活 → Awake 里又把自己隐藏掉”的经典坑。
    /// </summary>
    public static class GameUIBuilder
    {
        private const string CanvasName = "MobileUI";
        private const string HudName = "HUD";
        private const string GameOverName = "GameOverUI";
        private const string BallSpritePath = "Assets/Sprites/Ball.png";

        [MenuItem("Tools/球球大作战/7. 搭建第六步场景（HUD + 结算面板 + GameManager）", false, 7)]
        public static void BuildStep6Scene()
        {
            // 先重建第五步的完整场景（边界 + AI + 移动端 UI），再往上叠 HUD / 结算 / GameManager
            PrototypeSetup.BuildArenaAIScene();

            var player = Object.FindFirstObjectByType<PlayerController>();
            if (player == null)
            {
                EditorUtility.DisplayDialog("第六步 UI", "场景里找不到 PlayerController，先执行菜单 6。", "好");
                return;
            }

            GameObject canvas = GameObject.Find(CanvasName);
            if (canvas == null)
            {
                EditorUtility.DisplayDialog("第六步 UI", $"场景里找不到 {CanvasName} 画布，先执行菜单 6。", "好");
                return;
            }

            BuildHUD(canvas.transform, player);
            GameOverPanel panel = BuildGameOverPanel(canvas.transform);
            BuildGameManager(player, panel);

            AssetDatabase.SaveAssets();
            EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
            EditorSceneManager.SaveOpenScenes();

            Debug.Log("[GameUIBuilder] 第六步搭建完成：HUD(Mass/Rank) + GameOverPanel(重开) + GameManager");
        }

        [MenuItem("Tools/球球大作战/7b. 只补 UI（HUD + 结算面板 + GameManager）", false, 8)]
        public static void BuildUIOnly()
        {
            var player = Object.FindFirstObjectByType<PlayerController>();
            var canvas = GameObject.Find(CanvasName);
            if (player == null || canvas == null)
            {
                EditorUtility.DisplayDialog("第六步 UI", "需要场景里已有 Player 和 MobileUI 画布。", "好");
                return;
            }

            BuildHUD(canvas.transform, player);
            GameOverPanel panel = BuildGameOverPanel(canvas.transform);
            BuildGameManager(player, panel);

            AssetDatabase.SaveAssets();
            EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
            EditorSceneManager.SaveOpenScenes();
            Debug.Log("[GameUIBuilder] 只补 UI：HUD + GameOverPanel + GameManager 已就绪");
        }

        // ------------------------------ HUD ------------------------------

        private static void BuildHUD(Transform canvasRoot, PlayerController player)
        {
            var old = canvasRoot.Find(HudName);
            if (old != null) Object.DestroyImmediate(old.gameObject);

            var hud = new GameObject(HudName, typeof(RectTransform), typeof(HUDController));
            var hudRect = (RectTransform)hud.transform;
            hud.transform.SetParent(canvasRoot, false);
            hudRect.anchorMin = new Vector2(0f, 1f);
            hudRect.anchorMax = new Vector2(0f, 1f);
            hudRect.pivot = new Vector2(0f, 1f);
            hudRect.anchoredPosition = new Vector2(40f, -40f);
            hudRect.sizeDelta = new Vector2(620f, 150f);

            Text mass = CreateText(hud.transform, "Mass", "Mass: 0", 54, TextAnchor.UpperLeft,
                new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 0f), new Vector2(620f, 70f),
                pivot: new Vector2(0f, 1f));
            Text rank = CreateText(hud.transform, "Rank", "Rank: 1 / 1", 40, TextAnchor.UpperLeft,
                new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, -76f), new Vector2(620f, 60f),
                pivot: new Vector2(0f, 1f));

            mass.color = new Color(0.95f, 0.98f, 1f, 0.98f);
            rank.color = new Color(0.72f, 0.85f, 1f, 0.95f);

            var hudController = hud.GetComponent<HUDController>();
            var so = new SerializedObject(hudController);
            so.FindProperty("player").objectReferenceValue = player;
            so.FindProperty("massText").objectReferenceValue = mass;
            so.FindProperty("rankText").objectReferenceValue = rank;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        // ------------------------------ 结算面板 ------------------------------

        private static GameOverPanel BuildGameOverPanel(Transform canvasRoot)
        {
            var old = canvasRoot.Find(GameOverName);
            if (old != null) Object.DestroyImmediate(old.gameObject);

            // 常驻激活的根（组件挂这里），真正的视觉 Panel 是它的子物体、默认隐藏
            var root = new GameObject(GameOverName, typeof(RectTransform), typeof(GameOverPanel));
            var rootRect = (RectTransform)root.transform;
            root.transform.SetParent(canvasRoot, false);
            rootRect.anchorMin = Vector2.zero;
            rootRect.anchorMax = Vector2.one;
            rootRect.offsetMin = Vector2.zero;
            rootRect.offsetMax = Vector2.zero;

            // 全屏半透明底：挡住下面的摇杆/按钮点击，也压暗战场
            var panel = new GameObject("Panel", typeof(RectTransform), typeof(Image));
            var panelRect = (RectTransform)panel.transform;
            panel.transform.SetParent(root.transform, false);
            panelRect.anchorMin = Vector2.zero;
            panelRect.anchorMax = Vector2.one;
            panelRect.offsetMin = Vector2.zero;
            panelRect.offsetMax = Vector2.zero;
            var panelImage = panel.GetComponent<Image>();
            panelImage.color = new Color(0.02f, 0.04f, 0.07f, 0.82f);
            panelImage.raycastTarget = true;   // 吃掉点击，防止面板后面的摇杆还能被拖

            // 卡片：用纯色矩形（Image 不指定 sprite），比拿圆形 sprite 拉伸成 Sliced 干净得多
            var card = new GameObject("Card", typeof(RectTransform), typeof(Image));
            var cardRect = (RectTransform)card.transform;
            card.transform.SetParent(panel.transform, false);
            cardRect.anchorMin = new Vector2(0.5f, 0.5f);
            cardRect.anchorMax = new Vector2(0.5f, 0.5f);
            cardRect.pivot = new Vector2(0.5f, 0.5f);
            cardRect.sizeDelta = new Vector2(860f, 760f);
            cardRect.anchoredPosition = Vector2.zero;
            var cardImage = card.GetComponent<Image>();
            cardImage.color = new Color(0.10f, 0.14f, 0.21f, 0.97f);

            // 标题
            CreateText(card.transform, "Title", "GAME OVER", 88, TextAnchor.MiddleCenter,
                new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -110f), new Vector2(760f, 110f),
                new Color(1f, 0.62f, 0.62f, 1f));

            // 最高质量
            Text peak = CreateText(card.transform, "PeakMass", "本局最高质量：0", 52, TextAnchor.MiddleCenter,
                new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -260f), new Vector2(760f, 80f),
                new Color(0.95f, 0.98f, 1f, 1f));

            // 最终排名
            Text rank = CreateText(card.transform, "FinalRank", "最终排名：第 1 名 / 共 1 人", 46, TextAnchor.MiddleCenter,
                new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -350f), new Vector2(760f, 70f),
                new Color(0.78f, 0.88f, 1f, 1f));

            // 重新开始按钮
            var btnGo = new GameObject("BtnRestart", typeof(RectTransform), typeof(Image), typeof(Button));
            var btnRect = (RectTransform)btnGo.transform;
            btnGo.transform.SetParent(card.transform, false);
            btnRect.anchorMin = new Vector2(0.5f, 0f);
            btnRect.anchorMax = new Vector2(0.5f, 0f);
            btnRect.pivot = new Vector2(0.5f, 0f);
            btnRect.sizeDelta = new Vector2(460f, 150f);
            btnRect.anchoredPosition = new Vector2(0f, 90f);

            var btnImage = btnGo.GetComponent<Image>();
            btnImage.color = new Color(0.42f, 0.86f, 0.62f, 0.98f);

            var button = btnGo.GetComponent<Button>();
            button.targetGraphic = btnImage;
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

            CreateText(btnGo.transform, "Label", "重新开始", 56, TextAnchor.MiddleCenter,
                Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero,
                new Color(0.05f, 0.09f, 0.07f, 0.95f));

            // 返回主菜单按钮（放在“重新开始”上方，纯色矩形，风格一致）
            var backGo = new GameObject("BtnBackToMenu", typeof(RectTransform), typeof(Image), typeof(Button));
            var backRect = (RectTransform)backGo.transform;
            backGo.transform.SetParent(card.transform, false);
            backRect.anchorMin = new Vector2(0.5f, 0f);
            backRect.anchorMax = new Vector2(0.5f, 0f);
            backRect.pivot = new Vector2(0.5f, 0f);
            backRect.sizeDelta = new Vector2(460f, 120f);
            backRect.anchoredPosition = new Vector2(0f, 268f);

            var backImage = backGo.GetComponent<Image>();
            backImage.color = new Color(0.34f, 0.52f, 0.78f, 0.98f);

            var backButton = backGo.GetComponent<Button>();
            backButton.targetGraphic = backImage;
            backButton.transition = Selectable.Transition.ColorTint;
            backButton.colors = new ColorBlock
            {
                normalColor = Color.white,
                highlightedColor = new Color(0.92f, 0.92f, 0.92f, 1f),
                pressedColor = new Color(0.72f, 0.72f, 0.72f, 1f),
                selectedColor = Color.white,
                disabledColor = new Color(0.5f, 0.5f, 0.5f, 0.5f),
                colorMultiplier = 1f,
                fadeDuration = 0.08f,
            };

            CreateText(backGo.transform, "Label", "返回主菜单", 46, TextAnchor.MiddleCenter,
                Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero,
                new Color(0.95f, 0.97f, 1f, 0.98f));

            // 组件引用：panelRoot = 视觉 Panel（默认隐藏）
            var gameOver = root.GetComponent<GameOverPanel>();
            var so = new SerializedObject(gameOver);
            so.FindProperty("panelRoot").objectReferenceValue = panel;
            so.FindProperty("titleText").objectReferenceValue = card.transform.Find("Title").GetComponent<Text>();
            so.FindProperty("peakMassText").objectReferenceValue = peak;
            so.FindProperty("finalRankText").objectReferenceValue = rank;
            so.FindProperty("restartButton").objectReferenceValue = button;
            so.FindProperty("backToMenuButton").objectReferenceValue = backButton;
            so.FindProperty("mainMenuSceneName").stringValue = "MainMenu";
            so.ApplyModifiedPropertiesWithoutUndo();

            // OnClick -> GameOverPanel.Restart()（持久化监听，Inspector 里看得见；Awake 见已有监听就不再重复挂）
            UnityEventTools.AddPersistentListener(button.onClick, gameOver.Restart);
            UnityEventTools.AddPersistentListener(backButton.onClick, gameOver.BackToMenu);

            // 面板本体默认隐藏（注意是子物体 Panel，不是挂组件的 root）
            panel.SetActive(false);

            return gameOver;
        }

        // ------------------------------ GameManager ------------------------------

        private static void BuildGameManager(PlayerController player, GameOverPanel panel)
        {
            var existing = Object.FindFirstObjectByType<GameManager>(FindObjectsInactive.Include);
            GameObject go;

            if (existing != null)
            {
                go = existing.gameObject;
            }
            else
            {
                go = new GameObject("GameManager", typeof(GameManager));
            }

            var manager = go.GetComponent<GameManager>();
            var so = new SerializedObject(manager);
            so.FindProperty("player").objectReferenceValue = player;
            so.FindProperty("gameOverPanel").objectReferenceValue = panel;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        // ------------------------------ 工具 ------------------------------

        private static Text CreateText(Transform parent, string name, string content, int fontSize, TextAnchor anchor,
            Vector2 anchorMin, Vector2 anchorMax, Vector2 anchoredPos, Vector2 size, Color? color = null,
            Vector2? pivot = null)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Text));
            var rect = (RectTransform)go.transform;
            go.transform.SetParent(parent, false);

            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.pivot = pivot ?? new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = anchoredPos;
            rect.sizeDelta = size;

            var text = go.GetComponent<Text>();
            text.text = content;
            text.font = GetBuiltinFont();
            text.fontSize = fontSize;
            text.alignment = anchor;
            text.color = color ?? Color.white;
            text.raycastTarget = false;
            text.horizontalOverflow = HorizontalWrapMode.Overflow;
            text.verticalOverflow = VerticalWrapMode.Overflow;

            // 用内置 UI 材质，避免默认材质在 URP 下显示成粉色
            text.material = null;

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
