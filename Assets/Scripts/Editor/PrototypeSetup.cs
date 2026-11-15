using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;
using QiuQiu;

namespace QiuQiu.EditorTools
{
    /// <summary>
    /// 原型一键搭建工具（仅编辑器使用）。
    ///
    /// 因为当前环境里 Unity Pipeline 的 script eval 一直超时，这里改用
    /// "编辑器菜单 + unity command editor invoke_menu" 的方式自动完成场景搭建：
    ///   Tools > 球球大作战 > 1. 搭建测试场景
    ///
    /// 该工具是幂等的：重复执行会重建 Player，不会产生重复对象。
    /// </summary>
    public static class PrototypeSetup
    {
        private const string ScenePath = "Assets/Scenes/SampleScene.unity";
        private const string ControlsPath = "Assets/Input/Controls.inputactions";
        private const string SpriteFolder = "Assets/Sprites";
        private const string BallSpritePath = "Assets/Sprites/Ball.png";
        private const int BallTextureSize = 512;
        private const float FoodDiameter = 0.28f;   // 食物世界直径（球体 1 单位直径的 28%）

        [MenuItem("Tools/球球大作战/1. 搭建测试场景（Player + Camera）", false, 1)]
        public static void BuildTestScene()
        {
            EnsureSpriteAsset();
            Sprite ballSprite = LoadAssetWithImport<Sprite>(BallSpritePath);
            InputActionAsset controls = LoadAssetWithImport<InputActionAsset>(ControlsPath);

            // 打开（或重新打开）示例场景，保证在确定的场景里搭建
            var active = EditorSceneManager.GetActiveScene();
            if (!active.IsValid() || active.path != ScenePath)
                EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);

            SetupCamera();
            GameObject player = CreatePlayer(ballSprite, controls);

            AssetDatabase.SaveAssets();
            EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
            EditorSceneManager.SaveOpenScenes();

            Debug.Log($"[PrototypeSetup] 搭建完成：Player={player.name}，Sprite={(ballSprite != null ? BallSpritePath : "缺失")}，Controls={(controls != null ? ControlsPath : "缺失")}");
        }

        /// <summary>
        /// 第二步：在步骤 1 的场景基础上，加入“体积系统 + 吞噬”的可测试内容。
        /// 摆放是确定性的，方便自动验证：
        ///   - Player 质量 10，半径 0.75；
        ///   - Enemy_Small 质量 5，压在 Player 身上 -> 开场就被 Player 吞掉（10 >= 5*1.2）；
        ///   - Enemy_Big   质量 60，在远处不动      -> 用来验证“小的一方不会被吞 / 也不会去吞大的”；
        ///   - 3 个食物压在 Player 身上，其余食物散布在半径 2.5~6 的范围里。
        /// </summary>
        [MenuItem("Tools/球球大作战/2. 搭建吞噬测试场景（Food + Enemy）", false, 2)]
        public static void BuildEatTestScene()
        {
            EnsureSpriteAsset();
            Sprite ballSprite = LoadAssetWithImport<Sprite>(BallSpritePath);
            InputActionAsset controls = LoadAssetWithImport<InputActionAsset>(ControlsPath);

            var active = EditorSceneManager.GetActiveScene();
            if (!active.IsValid() || active.path != ScenePath)
                EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);

            SetupCamera();
            GameObject player = CreatePlayer(ballSprite, controls);

            // 幂等：清掉上一次搭建出的敌人与食物
            ClearByTag("Enemy");
            ClearByTag("Food");

            // ---------- 敌人 ----------
            CreateBall("Enemy_Small", ballSprite, new Color(1f, 0.45f, 0.45f), new Vector3(0.6f, 0f, 0f), 5f, "Enemy");
            CreateBall("Enemy_Big", ballSprite, new Color(1f, 0.72f, 0.28f), new Vector3(5f, 3f, 0f), 60f, "Enemy");

            // ---------- 食物：3 个压在玩家身上（开场即被吃，用于验证） ----------
            CreateFood("Food_Near_1", ballSprite, new Color(0.55f, 1f, 0.6f), new Vector3(0.20f, 0.20f, 0f), 1f);
            CreateFood("Food_Near_2", ballSprite, new Color(0.55f, 1f, 0.6f), new Vector3(-0.30f, 0.10f, 0f), 1f);
            CreateFood("Food_Near_3", ballSprite, new Color(0.55f, 1f, 0.6f), new Vector3(0.10f, -0.25f, 0f), 1f);

            // ---------- 食物：散布在场景里 ----------
            Color[] palette =
            {
                new Color(0.55f, 1f, 0.6f),
                new Color(1f, 0.85f, 0.4f),
                new Color(0.7f, 0.85f, 1f),
                new Color(1f, 0.6f, 0.85f),
            };
            var rng = new System.Random(20240607);
            for (int i = 0; i < 12; i++)
            {
                float angle = (float)(rng.NextDouble() * Mathf.PI * 2f);
                float dist = 2.5f + (float)rng.NextDouble() * 3.5f;
                var pos = new Vector3(Mathf.Cos(angle) * dist, Mathf.Sin(angle) * dist, 0f);
                CreateFood($"Food_{i:00}", ballSprite, palette[i % palette.Length], pos, 1f + (float)rng.NextDouble() * 1.5f);
            }

            AssetDatabase.SaveAssets();
            EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
            EditorSceneManager.SaveOpenScenes();

            Debug.Log($"[PrototypeSetup] 吞噬测试场景搭建完成：Player(10)、Enemy_Small(5)、Enemy_Big(60)、食物 15 个");
        }

        /// <summary>
        /// 第三步：分裂 / 吐球测试场景。
        /// Player 直接给 60 质量（> 30 才能分裂），旁边放一个弱小的敌人方便分裂后去吞，
        /// 食物散布得密一些，吐球后能明显看到食物飞出去。
        /// </summary>
        [MenuItem("Tools/球球大作战/3. 搭建分裂吐球测试场景（Split + Spit）", false, 3)]
        public static void BuildSplitTestScene()
        {
            EnsureSpriteAsset();
            Sprite ballSprite = LoadAssetWithImport<Sprite>(BallSpritePath);
            InputActionAsset controls = LoadAssetWithImport<InputActionAsset>(ControlsPath);

            var active = EditorSceneManager.GetActiveScene();
            if (!active.IsValid() || active.path != ScenePath)
                EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);

            SetupCamera();
            GameObject player = CreatePlayer(ballSprite, controls, 60f);

            ClearByTag("Enemy");
            ClearByTag("Food");

            // 一个弱小的敌人：分裂后可以用子球去围它
            CreateBall("Enemy_Small", ballSprite, new Color(1f, 0.45f, 0.45f), new Vector3(3.5f, 0f, 0f), 5f, "Enemy");

            ScatterFood(ballSprite, 14, 777, 3f, 7f);

            AssetDatabase.SaveAssets();
            EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
            EditorSceneManager.SaveOpenScenes();

            Debug.Log("[PrototypeSetup] 分裂 / 吐球测试场景搭建完成：Player(60，可连按 J 分裂、K 吐球)、Enemy_Small(5)、食物 14 个");
        }

        /// <summary>
        /// 第四步：完整测试场景 —— 摄像机跟随 + 食物生成器 + 合体。
        /// 注意：为了方便演示，这里的 mergeCooldown 被改成 2 秒（代码默认 15 秒）。
        /// </summary>
        [MenuItem("Tools/球球大作战/4. 搭建完整测试场景（相机跟随 + 食物生成器 + 合体）", false, 4)]
        public static void BuildFullTestScene()
        {
            EnsureSpriteAsset();
            Sprite ballSprite = LoadAssetWithImport<Sprite>(BallSpritePath);
            InputActionAsset controls = LoadAssetWithImport<InputActionAsset>(ControlsPath);

            var active = EditorSceneManager.GetActiveScene();
            if (!active.IsValid() || active.path != ScenePath)
                EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);

            SetupCamera();
            GameObject player = CreatePlayer(ballSprite, controls, 60f);

            ClearByTag("Enemy");
            ClearByTag("Food");
            ClearEdibles();

            var playerController = player.GetComponent<PlayerController>();

            // 演示用：把合体冷却压到 2 秒，便于立刻看到「分裂 → 游回 → 合体」的闭环
            if (playerController != null)
            {
                var pcSo = new SerializedObject(playerController);
                var cooldownProp = pcSo.FindProperty("mergeCooldown");
                if (cooldownProp != null) cooldownProp.floatValue = 2f;
                pcSo.ApplyModifiedPropertiesWithoutUndo();
            }

            // 三个体型不同的敌人
            CreateBall("Enemy_Small", ballSprite, new Color(1f, 0.45f, 0.45f), new Vector3(4.5f, 0f, 0f), 5f, "Enemy");
            CreateBall("Enemy_Mid", ballSprite, new Color(1f, 0.75f, 0.4f), new Vector3(-6f, 3.5f, 0f), 40f, "Enemy");
            CreateBall("Enemy_Big", ballSprite, new Color(0.95f, 0.55f, 0.25f), new Vector3(7f, -5f, 0f), 160f, "Enemy");

            // 摄像机跟随
            Camera cam = Camera.main;
            if (cam != null)
            {
                var follow = cam.GetComponent<CameraFollow>();
                if (follow == null) follow = cam.gameObject.AddComponent<CameraFollow>();

                var camSo = new SerializedObject(follow);
                var playerProp = camSo.FindProperty("player");
                if (playerProp != null) playerProp.objectReferenceValue = playerController;
                camSo.ApplyModifiedPropertiesWithoutUndo();
            }

            // 食物生成器
            GameObject spawnerGo = GameObject.Find("FoodSpawner");
            if (spawnerGo == null) spawnerGo = new GameObject("FoodSpawner");

            var spawner = spawnerGo.GetComponent<FoodSpawner>();
            if (spawner == null) spawner = spawnerGo.AddComponent<FoodSpawner>();

            var spawnerSo = new SerializedObject(spawner);
            SerializedProperty spriteProp = spawnerSo.FindProperty("foodSprite");
            if (spriteProp != null) spriteProp.objectReferenceValue = ballSprite;

            SerializedProperty maxProp = spawnerSo.FindProperty("maxFoodCount");
            if (maxProp != null) maxProp.intValue = 180;      // 演示场景用小一点的量，真机可调到 500

            SerializedProperty areaProp = spawnerSo.FindProperty("areaSize");
            if (areaProp != null) areaProp.vector2Value = new Vector2(44f, 30f);

            spawnerSo.ApplyModifiedPropertiesWithoutUndo();

            AssetDatabase.SaveAssets();
            EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
            EditorSceneManager.SaveOpenScenes();

            Debug.Log("[PrototypeSetup] 完整测试场景搭建完成：Player(60, 合体冷却 2s) + 3 个敌人 + CameraFollow + FoodSpawner(上限 180)");
        }

        /// <summary>
        /// 第五步：完整可玩场景 —— 软边界竞技场 + AI 敌人 + 移动端 UI。
        ///
        /// AI 阵营设计：每个 AI 一个独立阵营（1、2、3…），玩家是 0。
        /// 这样 AI 之间也会互相追、互相吃，地图才不会"一潭死水"；
        /// 如果希望 AI 结盟（只吃玩家、互不吞噬），把下面的 team 都改成 1 即可。
        /// </summary>
        [MenuItem("Tools/球球大作战/6. 搭建第五步场景（边界 + AI 敌人 + 移动端 UI）", false, 6)]
        public static void BuildArenaAIScene()
        {
            EnsureSpriteAsset();
            Sprite ballSprite = LoadAssetWithImport<Sprite>(BallSpritePath);
            InputActionAsset controls = LoadAssetWithImport<InputActionAsset>(ControlsPath);

            var active = EditorSceneManager.GetActiveScene();
            if (!active.IsValid() || active.path != ScenePath)
                EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);

            SetupCamera();
            GameObject player = CreatePlayer(ballSprite, controls, 60f);
            var playerController = player.GetComponent<PlayerController>();

            ClearByTag("Enemy");
            ClearByTag("Food");
            ClearEdibles();

            // ---------- 竞技场（软边界） ----------
            var arenaGo = GameObject.Find("Arena");
            if (arenaGo == null) arenaGo = new GameObject("Arena");
            var arena = arenaGo.GetComponent<ArenaManager>();
            if (arena == null) arena = arenaGo.AddComponent<ArenaManager>();

            var arenaSo = new SerializedObject(arena);
            arenaSo.FindProperty("shape").enumValueIndex = (int)ArenaManager.ArenaShape.Rectangle;
            arenaSo.FindProperty("rectangleSize").vector2Value = new Vector2(50f, 50f);
            arenaSo.FindProperty("pushBackStrength").floatValue = 4f;
            arenaSo.FindProperty("maxPushBackSpeed").floatValue = 8f;
            arenaSo.FindProperty("drawBorder").boolValue = true;
            arenaSo.ApplyModifiedPropertiesWithoutUndo();

            // ---------- 摄像机跟随 ----------
            Camera cam = Camera.main;
            if (cam != null)
            {
                var follow = cam.GetComponent<CameraFollow>();
                if (follow == null) follow = cam.gameObject.AddComponent<CameraFollow>();
                var camSo = new SerializedObject(follow);
                var playerProp = camSo.FindProperty("player");
                if (playerProp != null) playerProp.objectReferenceValue = playerController;
                camSo.ApplyModifiedPropertiesWithoutUndo();
            }

            // ---------- 食物生成器（只在边界内撒） ----------
            var spawnerGo = GameObject.Find("FoodSpawner");
            if (spawnerGo == null) spawnerGo = new GameObject("FoodSpawner");
            var spawner = spawnerGo.GetComponent<FoodSpawner>();
            if (spawner == null) spawner = spawnerGo.AddComponent<FoodSpawner>();

            var spawnerSo = new SerializedObject(spawner);
            spawnerSo.FindProperty("foodSprite").objectReferenceValue = ballSprite;
            spawnerSo.FindProperty("maxFoodCount").intValue = 260;
            spawnerSo.FindProperty("areaSize").vector2Value = new Vector2(50f, 50f);
            spawnerSo.FindProperty("useArenaBounds").boolValue = true;
            spawnerSo.ApplyModifiedPropertiesWithoutUndo();

            // ---------- AI 敌人 ----------
            // 质量从 4 到 260 铺开：小球追食物，大球追小球，玩家两边都能遇到
            float[] masses = { 4f, 7f, 11f, 16f, 24f, 34f, 48f, 66f, 90f, 120f };
            Color[] colors =
            {
                new Color(1f, 0.45f, 0.45f),   // 红
                new Color(1f, 0.72f, 0.28f),   // 橙
                new Color(0.95f, 0.5f, 0.85f), // 粉
                new Color(0.55f, 0.85f, 1f),   // 浅蓝
                new Color(0.7f, 1f, 0.6f),     // 黄绿
            };

            var rng = new System.Random(90210);
            for (int i = 0; i < masses.Length; i++)
            {
                // 环形分布；越大的 AI 出生越远（小球 6~10，大球 20~24），
                // 否则开局就有一只 240 质量的球贴在脸上，玩家一秒被吞、毫无体验。
                float angle = (float)(rng.NextDouble() * Mathf.PI * 2f);
                float t = masses.Length > 1 ? (float)i / (masses.Length - 1) : 0f;
                float dist = Mathf.Lerp(6f, 22f, t) + (float)rng.NextDouble() * 2.5f;
                // 第一只（最小）固定放在玩家附近，竖屏初始视野里就能看到一个可以吃的小敌人，
                // 否则开局镜头里只有食物，玩家不知道该干什么。
                if (i == 0) dist = 3.5f;
                var pos = new Vector3(Mathf.Cos(angle) * dist, Mathf.Sin(angle) * dist, 0f);

                string name = $"AI_{i:00}";
                GameObject ai = CreateBall(name, ballSprite, colors[i % colors.Length], pos, masses[i], "Enemy");

                // 所有 AI 共享同一个阵营（1）：AI 之间不会互相吞并，
                // 地图上始终保有一批大小不一的敌人，玩家的“威胁/猎物”分布稳定。
                // 如果想让 AI 之间也互相吞噬，把 aiTeam 改成 i + 1 即可（但实测会让一只
                // AI 雪球式滚到 700+ 质量，然后开局就把玩家吃掉，体验很差）。
                int aiTeam = 1;
                var size = ai.GetComponent<SizeController>();
                if (size != null)
                {
                    var sizeSo = new SerializedObject(size);
                    sizeSo.FindProperty("team").intValue = aiTeam;
                    sizeSo.ApplyModifiedPropertiesWithoutUndo();
                }

                var aiController = ai.AddComponent<AIController>();
                var aiSo = new SerializedObject(aiController);
                aiSo.FindProperty("team").intValue = aiTeam;
                // 每个 AI 速度略有差异，看起来更"活"
                aiSo.FindProperty("moveSpeed").floatValue = 2.8f + (float)rng.NextDouble() * 1.2f;
                aiSo.FindProperty("scanRadius").floatValue = 7f + (float)rng.NextDouble() * 3f;
                aiSo.ApplyModifiedPropertiesWithoutUndo();
            }

            // ---------- 移动端 UI ----------
            MobileUIBuilder.BuildMobileUI(playerController);

            AssetDatabase.SaveAssets();
            EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
            EditorSceneManager.SaveOpenScenes();

            Debug.Log($"[PrototypeSetup] 第五步场景搭建完成：Arena(50x50 软边界) + Player(60) + {masses.Length} 个 AI(4~240, 各自阵营) + FoodSpawner(260) + 移动端 UI");
        }

        /// <summary>删掉场景里所有食物（包括生成器产出的），保证重跑菜单不会堆积。</summary>
        private static void ClearEdibles()
        {
            var all = Object.FindObjectsByType<Edible>(FindObjectsSortMode.None);
            foreach (var edible in all)
            {
                if (edible != null) Object.DestroyImmediate(edible.gameObject);
            }
        }

        /// <summary>在半径 [minDist, maxDist] 的环带里撒食物（固定种子，保证可复现）。</summary>
        private static void ScatterFood(Sprite ballSprite, int count, int seed, float minDist, float maxDist)
        {
            Color[] palette =
            {
                new Color(0.55f, 1f, 0.6f),
                new Color(1f, 0.85f, 0.4f),
                new Color(0.7f, 0.85f, 1f),
                new Color(1f, 0.6f, 0.85f),
            };

            var rng = new System.Random(seed);
            for (int i = 0; i < count; i++)
            {
                float angle = (float)(rng.NextDouble() * Mathf.PI * 2f);
                float dist = minDist + (float)rng.NextDouble() * (maxDist - minDist);
                var pos = new Vector3(Mathf.Cos(angle) * dist, Mathf.Sin(angle) * dist, 0f);
                CreateFood($"Food_{i:00}", ballSprite, palette[i % palette.Length], pos, 1f + (float)rng.NextDouble() * 1.5f);
            }
        }

        // ---------------------------------------------------------------- 精灵

        /// <summary>
        /// 先尝试直接加载；若资产尚未进入 AssetDatabase（例如刚被外部工具写入），
        /// 强制同步导入一次再加载。避免"assets 明明在磁盘上却读到 null"。
        /// </summary>
        private static T LoadAssetWithImport<T>(string path) where T : Object
        {
            T asset = AssetDatabase.LoadAssetAtPath<T>(path);
            if (asset != null) return asset;

            if (!File.Exists(path)) return null;

            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate | ImportAssetOptions.ForceSynchronousImport);
            return AssetDatabase.LoadAssetAtPath<T>(path);
        }

        private static void EnsureSpriteAsset()
        {
            if (!AssetDatabase.IsValidFolder(SpriteFolder))
                AssetDatabase.CreateFolder("Assets", "Sprites");

            if (File.Exists(BallSpritePath))
            {
                // 已存在则只修正导入设置
                ApplySpriteImportSettings();
                return;
            }

            Texture2D tex = GenerateCircleTexture(BallTextureSize);
            byte[] png = tex.EncodeToPNG();
            Object.DestroyImmediate(tex);

            File.WriteAllBytes(BallSpritePath, png);
            AssetDatabase.ImportAsset(BallSpritePath, ImportAssetOptions.ForceUpdate | ImportAssetOptions.ForceSynchronousImport);
            ApplySpriteImportSettings();
            Debug.Log("[PrototypeSetup] 已生成球体贴图 " + BallSpritePath);
        }

        private static void ApplySpriteImportSettings()
        {
            var importer = AssetImporter.GetAtPath(BallSpritePath) as TextureImporter;
            if (importer == null) return;

            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.spritePixelsPerUnit = BallTextureSize; // 512px/512ppu => 1 个世界单位
            importer.alphaIsTransparency = true;
            importer.mipmapEnabled = false;
            importer.filterMode = FilterMode.Bilinear;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.maxTextureSize = BallTextureSize;
            importer.SaveAndReimport();
        }

        private static Texture2D GenerateCircleTexture(int size)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            tex.name = "Ball";

            float center = (size - 1) * 0.5f;
            float radius = center - 2f; // 留 2px 边距
            var pixels = new Color32[size * size];

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float dx = x - center;
                    float dy = y - center;
                    float dist = Mathf.Sqrt(dx * dx + dy * dy);

                    // 边缘 1.5px 做抗锯齿，内部纯白（颜色交给 SpriteRenderer 着色）
                    float alpha = Mathf.Clamp01((radius - dist) / 1.5f);
                    pixels[y * size + x] = new Color32(255, 255, 255, (byte)(alpha * 255f));
                }
            }

            tex.SetPixels32(pixels);
            tex.Apply();
            return tex;
        }

        // ---------------------------------------------------------------- 相机

        private static void SetupCamera()
        {
            Camera cam = Camera.main;
            if (cam == null)
            {
                var go = new GameObject("Main Camera");
                cam = go.AddComponent<Camera>();
                go.AddComponent<AudioListener>();
                go.tag = "MainCamera";
            }

            cam.orthographic = true;
            cam.orthographicSize = 8f;
            cam.transform.position = new Vector3(0f, 0f, -10f);
            cam.transform.rotation = Quaternion.identity;
            cam.backgroundColor = new Color(0.09f, 0.11f, 0.16f);
            cam.clearFlags = CameraClearFlags.SolidColor;
        }

        // ---------------------------------------------------------------- 玩家

        /// <summary>
        /// 玩家 = 「PlayerRoot（常驻控制器）」+「Player（球体）」两个物体。
        /// 控制器不再挂在球上：球的 GameObject 被吃掉销毁时控制器必须还活着，
        /// 否则玩家剩下的子球会失去控制（旧实现的致命 bug）。
        /// 返回值是 PlayerRoot，方便调用方继续取 PlayerController。
        /// </summary>
        private static GameObject CreatePlayer(Sprite ballSprite, InputActionAsset controls, float mass = 10f)
        {
            // 清掉旧的 PlayerRoot / Player，保证幂等
            foreach (var old in Object.FindObjectsByType<PlayerController>(FindObjectsSortMode.None))
                Object.DestroyImmediate(old.gameObject);

            for (int guard = 0; guard < 8; guard++)
            {
                var leftoverPlayer = GameObject.Find("Player");
                var leftoverRoot = GameObject.Find("PlayerRoot");
                if (leftoverPlayer == null && leftoverRoot == null) break;
                if (leftoverPlayer != null) Object.DestroyImmediate(leftoverPlayer);
                if (leftoverRoot != null) Object.DestroyImmediate(leftoverRoot);
            }

            var go = new GameObject("Player");
            go.tag = "Player";
            go.transform.position = Vector3.zero;
            go.transform.localScale = Vector3.one * 1.5f; // 直径 1.5 世界单位

            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = ballSprite;
            sr.color = new Color(0.35f, 0.75f, 1f);
            sr.sortingOrder = 10;

            var rb = go.AddComponent<Rigidbody2D>();
            rb.bodyType = RigidbodyType2D.Dynamic;
            rb.gravityScale = 0f;
            rb.mass = 1f;
            rb.linearDamping = 0f;
            rb.angularDamping = 0f;
            rb.freezeRotation = true;
            rb.interpolation = RigidbodyInterpolation2D.Interpolate;
            rb.collisionDetectionMode = CollisionDetectionMode2D.Continuous;

            var col = go.AddComponent<CircleCollider2D>();
            col.radius = 0.5f; // 局部半径，随 scale 放大到 0.75

            // 挂上体积系统与吞噬判定（真实大小由 SizeController 按质量换算，上面的 scale 会被覆盖）
            AttachSizeAndEater(go, mass, 0);

            // 控制器放在常驻的 PlayerRoot 上：球被吃掉也不会连带销毁控制逻辑。
            // 球列表由 PlayerController 启动时自动接管（team == 0 的球）。
            var root = new GameObject("PlayerRoot");
            var controller = root.AddComponent<PlayerController>();

            var so = new SerializedObject(controller);
            var controlsProp = so.FindProperty("controls");
            if (controlsProp != null)
            {
                controlsProp.objectReferenceValue = controls;
                so.ApplyModifiedPropertiesWithoutUndo();
            }

            return root;
        }

        // ---------------------------------------------------------------- 体积 / 吞噬

        /// <summary>
        /// 给一个球体补齐“双碰撞体 + SizeController + Eater”三件套。
        /// 幂等：重复调用不会叠加组件（已存在则复用）。
        /// </summary>
        private static void AttachSizeAndEater(GameObject go, float mass, int team)
        {
            // 1) 实体碰撞体：不勾 Is Trigger，负责球与球之间的真实碰撞
            var body = go.GetComponent<CircleCollider2D>();
            if (body == null) body = go.AddComponent<CircleCollider2D>();
            body.radius = 0.5f;
            body.isTrigger = false;

            // 2) 吞噬感应体：勾 Is Trigger，负责产生 OnTriggerEnter2D
            CircleCollider2D sensor = null;
            foreach (var c in go.GetComponents<CircleCollider2D>())
            {
                if (c != body) { sensor = c; break; }
            }
            if (sensor == null) sensor = go.AddComponent<CircleCollider2D>();
            sensor.radius = 0.5f;
            sensor.isTrigger = true;

            // 3) 体积系统（半径参考统一为 mass=10 -> 半径 0.75）
            var size = go.GetComponent<SizeController>();
            if (size == null) size = go.AddComponent<SizeController>();
            var so = new SerializedObject(size);
            so.FindProperty("mass").floatValue = mass;
            so.FindProperty("referenceMass").floatValue = 10f;
            so.FindProperty("radiusAtReferenceMass").floatValue = 0.75f;
            so.FindProperty("team").intValue = team;   // 0 = 玩家，1 = AI：同阵营不互吃
            so.ApplyModifiedPropertiesWithoutUndo();
            size.ApplySizeNow();

            // 4) 吞噬判定
            if (go.GetComponent<Eater>() == null) go.AddComponent<Eater>();
        }

        /// <summary>创建一个球体（玩家不用这个，敌人用）。</summary>
        private static GameObject CreateBall(string name, Sprite sprite, Color color, Vector3 position, float mass, string tag)
        {
            var go = new GameObject(name);
            go.tag = tag;
            go.transform.position = position;

            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = sprite;
            sr.color = color;
            sr.sortingOrder = 9;

            var rb = go.AddComponent<Rigidbody2D>();
            rb.bodyType = RigidbodyType2D.Dynamic;
            rb.gravityScale = 0f;
            rb.freezeRotation = true;
            rb.interpolation = RigidbodyInterpolation2D.Interpolate;
            rb.collisionDetectionMode = CollisionDetectionMode2D.Continuous;

            AttachSizeAndEater(go, mass, 1);   // 敌人阵营 = 1
            return go;
        }

        /// <summary>创建一个食物：Trigger 碰撞体 + Kinematic 刚体 + Edible。</summary>
        private static GameObject CreateFood(string name, Sprite sprite, Color color, Vector3 position, float mass)
        {
            var go = new GameObject(name);
            go.tag = "Food";
            go.transform.position = position;
            go.transform.localScale = Vector3.one * FoodDiameter;

            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = sprite;
            sr.color = color;
            sr.sortingOrder = 2;

            var col = go.AddComponent<CircleCollider2D>();
            col.radius = 0.5f;       // 局部半径，乘以 scale 后即为食物半径
            col.isTrigger = true;

            var rb = go.AddComponent<Rigidbody2D>();
            rb.bodyType = RigidbodyType2D.Kinematic;   // 不被物理力推动
            rb.simulated = true;

            var edible = go.AddComponent<Edible>();
            edible.Initialize(mass);
            return go;
        }

        /// <summary>清掉场景里指定 Tag 的所有根对象（保证菜单可重复执行）。</summary>
        private static void ClearByTag(string tag)
        {
            foreach (var go in Object.FindObjectsByType<GameObject>(FindObjectsSortMode.None))
            {
                if (go.transform.parent != null) continue;
                if (go.CompareTag(tag)) Object.DestroyImmediate(go);
            }
        }
    }
}
