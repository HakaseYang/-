using UnityEngine;

namespace QiuQiu
{
    /// <summary>
    /// 摄像机跟随 + 动态缩放（竖屏取景版）。
    ///
    /// 核心思想：**只按“玩家所有球的包围盒”算取景，不再按质量估算**。
    ///   1. 把玩家每个存活球的圆心 + (半径 × radiusScale) 作为它的外接圆，取所有球的 min/max，
    ///      得到总包围盒（宽 W、高 H）；
    ///   2. 正交相机的 orthographicSize 是**屏幕高度的一半**，所以：
    ///          高度需求  SizeY = H / 2 + padding
    ///          宽度需求  SizeX = (W / 2 + padding) / aspect
    ///          TargetSize = max(SizeY, SizeX, minOrthographicSize)
    ///      竖屏（aspect ≈ 0.5625）时宽度是短板，SizeX 会自动比 SizeY 大，这就是“球横向跑出画面”的根因；
    ///   3. 默认不设上限（maxOrthographicSize &lt;= 0 表示无限制），球再大也会继续拉远；
    ///   4. 缩放用 Mathf.Lerp 平滑：放大（zoom out）比缩小（zoom in）快，避免球快速长大时被裁掉；
    ///   5. 兜底：如果某一帧仍有球超出画面，本帧直接把尺寸补齐到“刚好装下”，保证永不裁切。
    ///
    /// 为什么不用质量换算：质量到半径是 sqrt 关系，而多球分裂后“总质量不变、包围盒变大”，
    /// 用质量算出来的尺寸会和实际取景需求脱节，球大了反而框不住。包围盒才是唯一可靠的依据。
    /// </summary>
    [RequireComponent(typeof(Camera))]
    public class CameraFollow : MonoBehaviour
    {
        public enum CenterMode
        {
            /// <summary>所有球的包围盒中心：取景最稳，球永远不会偏出画面。</summary>
            BoundsCenter,

            /// <summary>按质量加权的质心：大球居中，手感更舒服（推荐）。</summary>
            MassWeightedCenter
        }

        [Header("跟随目标")]
        [Tooltip("留空会自动找场景里的 PlayerController，并在其阵亡后自动跟随新的 PlayerController.Active。")]
        [SerializeField] private PlayerController player;

        [Tooltip("摄像机相对取景中心的偏移（z = -10 用来把相机放在 2D 平面之后）。")]
        [SerializeField] private Vector3 followOffset = new Vector3(0f, 0f, -10f);

        [Tooltip("取景中心的算法：质心手感好，包围盒中心最稳。")]
        [SerializeField] private CenterMode centerMode = CenterMode.MassWeightedCenter;

        [Tooltip("位置平滑速度：越大跟得越紧。0 = 瞬间贴合。")]
        [SerializeField] private float positionLerpSpeed = 8f;

        [Tooltip("取景中心与目标差超过这个距离时，直接瞬移过去（复活/传送时避免镜头横穿整张地图）。0 = 永不瞬移。")]
        [SerializeField] private float maxPanDistance = 12f;

        [Tooltip("没有任何球时，摄像机停住不动（复活等待时停在死亡位置，不会闪回原点）。")]
        [SerializeField] private bool holdPositionWhenNoBall = true;

        [Header("取景边距（Inspector 常用）")]
        [Tooltip("【边距】包围盒四周各留出的世界单位。1.5 ≈ 球边上再留一个多球身，越大越松。")]
        [SerializeField] private float padding = 1.5f;

        [Tooltip("【半径放大】算包围盒时把球半径乘上它，用来给球边留一点余量。1.0 = 紧贴球边。")]
        [SerializeField] private float radiusScale = 1.1f;

        [Header("缩放范围")]
        [Tooltip("最小视野（正交尺寸 = 屏幕半高）。太小会在球很小时贴脸。")]
        [SerializeField] private float minOrthographicSize = 5f;

        [Tooltip("最大视野。<= 0 表示【不设上限】：球再大也继续拉远。")]
        [SerializeField] private float maxOrthographicSize = 0f;

        [Header("缩放平滑（Inspector 常用）")]
        [Tooltip("【拉远速度】球变大时镜头后退的速度。越大跟得越紧，建议 8~15。")]
        [SerializeField] private float zoomOutLerpSpeed = 10f;

        [Tooltip("【拉近速度】球变小时镜头收回的速度。慢一点更舒服，建议 2~4。")]
        [SerializeField] private float zoomInLerpSpeed = 2.5f;

        [Header("兜底")]
        [Tooltip("开启后：只要有球超出当前画面，本帧就把尺寸补齐到刚好装下，保证永不裁切。")]
        [SerializeField] private bool snapWhenBallWouldClip = true;

        private Camera cam;

        // ---------------- 对外只读数据（UI / 调试用） ----------------

        /// <summary>本帧算出来的目标正交尺寸。</summary>
        public float TargetOrthographicSize { get; private set; }

        /// <summary>玩家所有球（含半径与边距）的世界包围盒。</summary>
        public Bounds PlayerBounds { get; private set; }

        /// <summary>本帧的取景中心。</summary>
        public Vector3 PlayerCenter { get; private set; }

        /// <summary>装下所有球所需的半宽（含边距）。</summary>
        public float RequiredHalfWidth { get; private set; }

        /// <summary>装下所有球所需的半高（含边距）。</summary>
        public float RequiredHalfHeight { get; private set; }

        private void Awake()
        {
            cam = GetComponent<Camera>();
            cam.orthographic = true;

            if (player == null) player = FindFirstObjectByType<PlayerController>();

            cam.orthographicSize = Mathf.Max(minOrthographicSize, cam.orthographicSize);
        }

        private void LateUpdate()
        {
            // 主球阵亡后旧控制器会被销毁、新控制器挂在幸存球上，这里每帧兜底取 Active
            if (player == null || !player.isActiveAndEnabled) player = PlayerController.Active;

            float aspect = GetAspect();

            Vector3 center;
            if (!TryGetViewCenter(out center))
            {
                // 没有球：镜头留在原地（复活等待时停在死亡位置）
                if (!holdPositionWhenNoBall) transform.position = followOffset;
                return;
            }

            PlayerCenter = center;

            // ---------------- 1. 位置 ----------------
            Vector3 targetPosition = center + followOffset;
            float gap = Vector3.Distance(new Vector3(transform.position.x, transform.position.y, 0f),
                                         new Vector3(targetPosition.x, targetPosition.y, 0f));

            if (maxPanDistance > 0f && gap > maxPanDistance)
            {
                // 复活/传送：直接把镜头搬过去，避免镜头横穿地图
                transform.position = targetPosition;
            }
            else
            {
                float posT = 1f - Mathf.Exp(-Mathf.Max(0f, positionLerpSpeed) * Time.deltaTime);
                transform.position = Vector3.Lerp(transform.position, targetPosition, posT);
            }

            // ---------------- 2. 取景需求（按取景中心算） ----------------
            ComputeRequiredExtents(center, out float halfWidth, out float halfHeight);
            RequiredHalfWidth = halfWidth;
            RequiredHalfHeight = halfHeight;

            float targetSize = ComputeTargetSize(halfWidth, halfHeight, aspect);

            // 兜底：位置还在追赶时，用“相机当前实际所在位置”再算一次需求，
            // 取两者最大 —— 这样即使镜头正在平移，也不会有球被切出画面。
            if (snapWhenBallWouldClip)
            {
                Vector3 actualCenter = transform.position - followOffset;
                ComputeRequiredExtents(actualCenter, out float actualHalfWidth, out float actualHalfHeight);
                float actualSize = ComputeTargetSize(actualHalfWidth, actualHalfHeight, aspect);
                if (actualSize > targetSize) targetSize = actualSize;
            }

            TargetOrthographicSize = targetSize;

            // ---------------- 3. 平滑缩放 ----------------
            float current = cam.orthographicSize;
            // 拉远比拉近快：球快速变大时镜头要马上跟上，球变小时慢慢收回
            float speed = targetSize > current ? zoomOutLerpSpeed : zoomInLerpSpeed;
            float zoomT = 1f - Mathf.Exp(-Mathf.Max(0.0001f, speed) * Time.unscaledDeltaTime);
            float next = Mathf.Lerp(current, targetSize, zoomT);

            // 再兜一层：当前帧若还有球会超出画面，直接补齐（宁可这一帧生硬，也不要裁切）
            if (snapWhenBallWouldClip && WillClip(next, center, aspect))
            {
                next = targetSize;
            }

            cam.orthographicSize = Mathf.Max(minOrthographicSize, next);
        }

        /// <summary>当前相机的宽高比（竖屏 9:16 ≈ 0.5625）。</summary>
        private float GetAspect()
        {
            float aspect = cam != null ? cam.aspect : 0f;
            return aspect > 0.0001f ? aspect : 1f;
        }

        /// <summary>取景中心：质心或包围盒中心；没有球时返回 false。</summary>
        private bool TryGetViewCenter(out Vector3 center)
        {
            center = transform.position - followOffset;

            if (player == null) return false;

            var balls = player.Balls;
            if (balls == null || balls.Count == 0) return false;

            if (!TryGetBounds(out Bounds bounds)) return false;
            PlayerBounds = bounds;

            if (centerMode == CenterMode.BoundsCenter)
            {
                center = bounds.center;
                return true;
            }

            // 质量加权质心（大球权重高）
            Vector2 sum = Vector2.zero;
            float weightSum = 0f;

            for (int i = 0; i < balls.Count; i++)
            {
                var ball = balls[i];
                if (ball == null || ball.IsDead) continue;

                float weight = Mathf.Max(0.01f, ball.Mass);
                sum += (Vector2)ball.transform.position * weight;
                weightSum += weight;
            }

            if (weightSum <= 0f)
            {
                center = bounds.center;
                return true;
            }

            Vector2 weighted = sum / weightSum;
            center = new Vector3(weighted.x, weighted.y, 0f);
            return true;
        }

        /// <summary>玩家所有球的世界包围盒（半径 × radiusScale）。</summary>
        private bool TryGetBounds(out Bounds bounds)
        {
            bounds = new Bounds(Vector3.zero, Vector3.zero);

            if (player == null) return false;

            var balls = player.Balls;
            if (balls == null || balls.Count == 0) return false;

            bool any = false;
            float minX = 0f, maxX = 0f, minY = 0f, maxY = 0f;

            for (int i = 0; i < balls.Count; i++)
            {
                var ball = balls[i];
                if (ball == null || ball.IsDead) continue;

                Vector2 p = ball.transform.position;
                float r = Mathf.Max(0.01f, ball.Radius * Mathf.Max(0.01f, radiusScale));

                if (!any)
                {
                    minX = p.x - r; maxX = p.x + r;
                    minY = p.y - r; maxY = p.y + r;
                    any = true;
                }
                else
                {
                    if (p.x - r < minX) minX = p.x - r;
                    if (p.x + r > maxX) maxX = p.x + r;
                    if (p.y - r < minY) minY = p.y - r;
                    if (p.y + r > maxY) maxY = p.y + r;
                }
            }

            if (!any) return false;

            bounds = new Bounds(new Vector3((minX + maxX) * 0.5f, (minY + maxY) * 0.5f, 0f),
                                new Vector3(maxX - minX, maxY - minY, 0f));
            return true;
        }

        /// <summary>
        /// 以给定中心为取景中心，算出装下所有球所需的半宽/半高（含 padding）。
        /// 注意这里是“相对中心”的最大偏移，不是包围盒的一半 —— 质心与包围盒中心不重合时也能框住。
        /// </summary>
        private void ComputeRequiredExtents(Vector3 center, out float halfWidth, out float halfHeight)
        {
            halfWidth = 0f;
            halfHeight = 0f;

            if (player == null) return;

            var balls = player.Balls;
            if (balls == null) return;

            float pad = Mathf.Max(0f, padding);

            for (int i = 0; i < balls.Count; i++)
            {
                var ball = balls[i];
                if (ball == null || ball.IsDead) continue;

                Vector2 p = ball.transform.position;
                float r = Mathf.Max(0.01f, ball.Radius * Mathf.Max(0.01f, radiusScale));

                float dx = Mathf.Abs(p.x - center.x) + r;
                float dy = Mathf.Abs(p.y - center.y) + r;

                if (dx > halfWidth) halfWidth = dx;
                if (dy > halfHeight) halfHeight = dy;
            }

            halfWidth += pad;
            halfHeight += pad;
        }

        /// <summary>
        /// 由半宽/半高换算正交尺寸：
        ///   orthographicSize 是屏幕高度的一半 → 高度需要 halfHeight，宽度需要 halfWidth / aspect。
        /// </summary>
        private float ComputeTargetSize(float halfWidth, float halfHeight, float aspect)
        {
            float sizeY = halfHeight;
            float sizeX = halfWidth / Mathf.Max(0.0001f, aspect);
            float size = Mathf.Max(sizeY, sizeX, minOrthographicSize);

            if (maxOrthographicSize > 0f)
                size = Mathf.Min(size, maxOrthographicSize);   // 只有显式配置上限时才限制

            return size;
        }

        /// <summary>给定尺寸下，是否有球会超出画面（用来做兜底判断）。</summary>
        private bool WillClip(float size, Vector3 center, float aspect)
        {
            if (player == null) return false;

            var balls = player.Balls;
            if (balls == null) return false;

            float halfH = size;
            float halfW = size * aspect;
            const float epsilon = 0.01f;

            for (int i = 0; i < balls.Count; i++)
            {
                var ball = balls[i];
                if (ball == null || ball.IsDead) continue;

                Vector2 p = ball.transform.position;
                float r = Mathf.Max(0.01f, ball.Radius * Mathf.Max(0.01f, radiusScale));

                if (Mathf.Abs(p.x - center.x) + r > halfW + epsilon) return true;
                if (Mathf.Abs(p.y - center.y) + r > halfH + epsilon) return true;
            }

            return false;
        }

        /// <summary>给 UI / 调试用：当前包围盒。</summary>
        public bool TryGetPlayerBounds(out Bounds bounds) => TryGetBounds(out bounds);

        private void OnDrawGizmosSelected()
        {
            if (player == null) return;

            var balls = player.Balls;
            if (balls == null) return;

            Gizmos.color = Color.yellow;
            for (int i = 0; i < balls.Count; i++)
            {
                var ball = balls[i];
                if (ball == null || ball.IsDead) continue;
                Gizmos.DrawWireSphere(ball.transform.position, ball.Radius);
            }

            if (!Application.isPlaying) return;
            if (!TryGetBounds(out Bounds bounds)) return;

            // 青色 = 包围盒
            Gizmos.color = new Color(0.3f, 0.9f, 1f, 0.6f);
            Gizmos.DrawWireCube(bounds.center, bounds.size);

            // 绿色 = 相机实际取景矩形（竖屏时能直观看到宽度是否装得下）
            if (cam != null && cam.orthographic)
            {
                float halfH = cam.orthographicSize;
                float halfW = halfH * GetAspect();
                Vector3 c = new Vector3(transform.position.x, transform.position.y, 0f);
                Gizmos.color = new Color(0.4f, 1f, 0.5f, 0.9f);
                Gizmos.DrawWireCube(c, new Vector3(halfW * 2f, halfH * 2f, 0f));
            }
        }
    }
}
