using UnityEngine;

namespace QiuQiu
{
    /// <summary>
    /// 食物生成器：在矩形区域里维持“场上食物数量 = maxFoodCount”。
    ///
    /// 为什么不是“定时无限生成”？
    ///   手机端最大瓶颈是物体数量。这里改成“补货制”：每隔 checkInterval 秒数一次场上还剩多少食物，
    ///   只补足差额，并且每次最多补 maxSpawnsPerTick 个。
    ///   —— 总量被封顶，不会随游戏时长线性变卡；单帧新增数量也被封顶，不会突然卡一帧。
    ///
    /// 食物本身仍然走 BallFactory.CreateFood（Trigger + Kinematic + Edible），和场景里手动摆的食物完全一样。
    /// </summary>
    public class FoodSpawner : MonoBehaviour
    {
        [Header("生成区域（以本物体为原点）")]
        [Tooltip("矩形区域的中心偏移。")]
        [SerializeField] private Vector2 areaCenter = Vector2.zero;

        [Tooltip("矩形区域尺寸（宽, 高）。")]
        [SerializeField] private Vector2 areaSize = new Vector2(44f, 30f);

        [Tooltip("优先跟随 ArenaManager 的形状生成（圆形竞技场也能正确铺食物）。关掉则只用上面的矩形。")]
        [SerializeField] private bool useArenaBounds = true;

        [Tooltip("生成点离竞技场边缘的最小距离。")]
        [SerializeField] private float arenaEdgePadding = 0.4f;

        [Header("数量控制")]
        [Tooltip("场上食物的最大数量：达到这个数就不再生成。")]
        [SerializeField] private int maxFoodCount = 500;

        [Tooltip("是否在 Start 时先把食物一次性补满（关掉则靠运行时慢慢补）。")]
        [SerializeField] private bool fillOnStart = true;

        [Tooltip("补货检测间隔（秒）。")]
        [SerializeField] private float checkInterval = 0.5f;

        [Tooltip("单次补货最多生成多少个，避免一帧创建几百个物体卡顿。")]
        [SerializeField] private int maxSpawnsPerTick = 40;

        [Header("食物外观 / 质量")]
        [Tooltip("食物贴图，留空则尝试用玩家的球贴图。")]
        [SerializeField] private Sprite foodSprite;

        [Tooltip("单个食物质量下限。")]
        [SerializeField] private float minFoodMass = 1f;

        [Tooltip("单个食物质量上限。")]
        [SerializeField] private float maxFoodMass = 2.5f;

        [Tooltip("随机颜色池，食物点会更花。")]
        [SerializeField]
        private Color[] palette =
        {
            new Color(0.55f, 0.95f, 0.55f),
            new Color(1.00f, 0.80f, 0.35f),
            new Color(1.00f, 0.55f, 0.70f),
            new Color(0.60f, 0.85f, 1.00f),
            new Color(0.85f, 0.70f, 1.00f),
        };

        [Header("避让")]
        [Tooltip("距离玩家任意球小于这个值时重新取点，避免食物直接生成在嘴里。")]
        [SerializeField] private float minDistanceFromPlayer = 3f;

        [Tooltip("每次取点的最大重试次数。")]
        [SerializeField] private int placementAttempts = 8;

        [Tooltip("生成的食物在层级里的父节点（留空则挂在场景根）。")]
        [SerializeField] private Transform foodParent;

        private float nextCheckTime;
        private PlayerController player;
        private int spawnCounter;

        public int CurrentFoodCount { get; private set; }

        private void Awake()
        {
            if (foodSprite == null)
            {
                // 没指定就借用玩家球的贴图，保证至少能显示出来
                var anyPlayer = FindFirstObjectByType<PlayerController>();
                if (anyPlayer != null)
                {
                    var sr = anyPlayer.GetComponent<SpriteRenderer>();
                    if (sr != null) foodSprite = sr.sprite;
                }
            }
        }

        private void Start()
        {
            player = FindFirstObjectByType<PlayerController>();
            nextCheckTime = Time.time + checkInterval;

            if (fillOnStart)
            {
                // 开局直接补满（一次性，但受 maxFoodCount 限制）
                int guard = 0;
                while (CurrentFoodCount < maxFoodCount && guard++ < maxFoodCount)
                {
                    if (!SpawnOne()) break;
                    CurrentFoodCount++;
                }
            }
        }

        private void Update()
        {
            if (Time.time < nextCheckTime) return;
            nextCheckTime = Time.time + Mathf.Max(0.05f, checkInterval);

            // 数一遍场上食物：用 Edible 的静态计数器（O(1)），
            // 不再全场景 FindObjectsByType（那是每次分配一个新数组，几百个食物时 GC 压力明显）
            CurrentFoodCount = Edible.ActiveCount;

            int missing = maxFoodCount - CurrentFoodCount;
            if (missing <= 0) return;

            int budget = Mathf.Min(missing, Mathf.Max(1, maxSpawnsPerTick));
            int spawned = 0;

            for (int i = 0; i < budget; i++)
            {
                if (!SpawnOne()) break;
                spawned++;
            }

            CurrentFoodCount += spawned;
        }

        /// <summary>在区域内取一个随机点并生成一颗食物。</summary>
        private bool SpawnOne()
        {
            if (foodSprite == null) return false;

            for (int attempt = 0; attempt < Mathf.Max(1, placementAttempts); attempt++)
            {
                Vector3 worldPosition = PickSpawnPosition();

                if (IsTooCloseToPlayer(worldPosition)) continue;
                if (!IsInsideArena(worldPosition)) continue;

                float mass = Random.Range(minFoodMass, maxFoodMass);
                Color color = palette != null && palette.Length > 0
                    ? palette[Random.Range(0, palette.Length)]
                    : Color.green;

                string name = $"Food_{spawnCounter++:000}";
                GameObject go = BallFactory.CreateFood(name, worldPosition, mass, foodSprite, color);
                if (foodParent != null) go.transform.SetParent(foodParent, true);

                return true;
            }

            return false;
        }

        /// <summary>取一个候选生成点：有 ArenaManager 且开启 useArenaBounds 时按竞技场形状取，否则用本地矩形。</summary>
        private Vector3 PickSpawnPosition()
        {
            ArenaManager arena = useArenaBounds ? ArenaManager.Instance : null;

            if (arena != null)
            {
                Vector2 point = arena.GetRandomPointInside(arenaEdgePadding + 0.6f);
                return new Vector3(point.x, point.y, 0f);
            }

            Vector2 randomPoint = areaCenter + new Vector2(
                Random.Range(-areaSize.x * 0.5f, areaSize.x * 0.5f),
                Random.Range(-areaSize.y * 0.5f, areaSize.y * 0.5f));

            return transform.position + new Vector3(randomPoint.x, randomPoint.y, 0f);
        }

        /// <summary>兜底检查：候选点必须在竞技场内部（防止矩形区域超出圆形边界）。</summary>
        private bool IsInsideArena(Vector3 position)
        {
            ArenaManager arena = useArenaBounds ? ArenaManager.Instance : null;
            if (arena == null) return true;

            return arena.IsInside(position, arenaEdgePadding);
        }

        private bool IsTooCloseToPlayer(Vector3 position)
        {
            // 主球阵亡交接后旧控制器会被销毁，这里兜底取当前的 Active
            if (player == null) player = PlayerController.Active;
            if (player == null || minDistanceFromPlayer <= 0f) return false;

            var ballList = player.Balls;
            if (ballList == null) return false;

            for (int i = 0; i < ballList.Count; i++)
            {
                var ball = ballList[i];
                if (ball == null || ball.IsDead) continue;

                float safeDistance = minDistanceFromPlayer + ball.Radius;
                if (Vector2.Distance(ball.transform.position, position) < safeDistance) return true;
            }

            return false;
        }

        /// <summary>清空场上所有食物（调试 / 重开一局用）。</summary>
        public void ClearAllFood()
        {
            var all = Object.FindObjectsByType<Edible>(FindObjectsSortMode.None);
            foreach (var edible in all)
            {
                if (edible != null) Destroy(edible.gameObject);
            }
            CurrentFoodCount = 0;
        }

        private void OnDrawGizmosSelected()
        {
            Gizmos.color = new Color(0.3f, 1f, 0.5f, 0.8f);
            Vector3 center = transform.position + new Vector3(areaCenter.x, areaCenter.y, 0f);
            Gizmos.DrawWireCube(center, new Vector3(areaSize.x, areaSize.y, 0.1f));
        }
    }
}
