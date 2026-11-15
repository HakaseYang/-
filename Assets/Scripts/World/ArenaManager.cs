using UnityEngine;

namespace QiuQiu
{
    /// <summary>
    /// 世界边界（软边界）。
    ///
    /// 为什么不用“死墙”（BoxCollider2D 围墙）？
    ///   1. 球是 Dynamic Rigidbody2D，被墙挡住时物理求解器要在接触点上反复迭代，
    ///      玩家一直顶着墙走会出现抖动、卡住、甚至被弹飞；
    ///   2. 高倍速 + 大体型下，墙体厚度不够还会出现“穿墙”（虽然可以用 Continuous 缓解，
    ///      但代价是性能）；
    ///   3. 球越大越容易被墙“卡”在角落里，手感很差。
    ///
    /// 这里的做法（位置纠正 + 速度投影，属于“软边界”）：
    ///   1. 每个物理帧检查球心是否越界，越界就把位置直接纠正回边界内侧（等价于 Mathf.Clamp）；
    ///   2. 把速度里“朝外”的那个分量减掉（相当于贴着墙滑行，而不是撞墙停下）；
    ///   3. 再补一点点朝内的推力，让球离开边界时更顺滑，不会一直贴着边缘。
    ///   全程不产生物理碰撞，所以既不会有求解器抖动，也不会有性能开销。
    ///
    /// 使用方式：场景里放一个挂本组件的物体即可（单例，自己注册）。
    /// </summary>
    [DisallowMultipleComponent]
    public class ArenaManager : MonoBehaviour
    {
        public enum ArenaShape
        {
            Rectangle,
            Circle
        }

        public static ArenaManager Instance { get; private set; }

        [Header("形状")]
        [Tooltip("矩形 = 长宽可分别设置；圆形 = 只用半径。")]
        [SerializeField] private ArenaShape shape = ArenaShape.Rectangle;

        [Tooltip("矩形竞技区的完整尺寸（宽, 高），单位：世界单位。")]
        [SerializeField] private Vector2 rectangleSize = new Vector2(50f, 50f);

        [Tooltip("圆形竞技区的半径。")]
        [SerializeField] private float circleRadius = 30f;

        [Tooltip("竞技区中心点（留空则用本物体的位置）。")]
        [SerializeField] private Transform centerOverride;

        [Header("边界行为")]
        [Tooltip("越界时是否直接把坐标夹回边界内侧（关掉则只靠推回速度，球能短暂冲出边界）。")]
        [SerializeField] private bool clampPosition = true;

        [Tooltip("越界越深时额外的朝内推力（单位/秒），让球快速离开边缘。设 0 表示只做速度投影。")]
        [SerializeField] private float pushBackStrength = 4f;

        [Tooltip("推回速度的上限，避免越界太深时产生一次猛烈弹射。")]
        [SerializeField] private float maxPushBackSpeed = 8f;

        [Header("边缘可视化")]
        [Tooltip("运行时用 LineRenderer 画出边界线（方便调试，正式版可关）。")]
        [SerializeField] private bool drawBorder = true;

        [SerializeField] private Color borderColor = new Color(0.45f, 0.75f, 1f, 0.65f);

        [Tooltip("边界线宽度（世界单位）。")]
        [SerializeField] private float borderWidth = 0.12f;

        private LineRenderer borderLine;

        public ArenaShape Shape => shape;
        public Vector2 Center => centerOverride != null ? (Vector2)centerOverride.position : (Vector2)transform.position;

        /// <summary>矩形的半尺寸（宽/2, 高/2）。圆形下为 (radius, radius)。</summary>
        public Vector2 HalfSize => shape == ArenaShape.Rectangle
            ? rectangleSize * 0.5f
            : new Vector2(circleRadius, circleRadius);

        private void Awake()
        {
            Instance = this;

            if (drawBorder) CreateBorderLine();
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        // ------------------------------ 查询 ------------------------------

        /// <summary>点（考虑半径为 radius 的球）是否完全落在竞技区内。</summary>
        public bool IsInside(Vector2 position, float radius = 0f)
        {
            Vector2 center = Center;

            if (shape == ArenaShape.Circle)
            {
                return Vector2.Distance(position, center) <= circleRadius - radius;
            }

            Vector2 half = HalfSize;
            return position.x >= center.x - half.x + radius &&
                   position.x <= center.x + half.x - radius &&
                   position.y >= center.y - half.y + radius &&
                   position.y <= center.y + half.y - radius;
        }

        /// <summary>把一个点（考虑球半径）夹到竞技区内。</summary>
        public Vector2 ClampPosition(Vector2 position, float radius = 0f)
        {
            Vector2 center = Center;

            if (shape == ArenaShape.Circle)
            {
                float allowed = Mathf.Max(0f, circleRadius - radius);
                Vector2 offset = position - center;
                if (offset.sqrMagnitude <= allowed * allowed) return position;
                return center + offset.normalized * allowed;
            }

            Vector2 half = HalfSize;
            float minX = center.x - half.x + radius;
            float maxX = center.x + half.x - radius;
            float minY = center.y - half.y + radius;
            float maxY = center.y + half.y - radius;

            // 边界比球还小的极端情况：直接压到中心
            if (minX > maxX) { minX = maxX = center.x; }
            if (minY > maxY) { minY = maxY = center.y; }

            return new Vector2(Mathf.Clamp(position.x, minX, maxX), Mathf.Clamp(position.y, minY, maxY));
        }

        /// <summary>
        /// 在竞技区内部随机取一个点。
        /// margin 是“离边界至少多远”，一般传球的半径，避免一生成就压线。
        /// </summary>
        public Vector2 GetRandomPointInside(System.Random rng, float margin = 0f)
        {
            if (rng == null) rng = new System.Random();

            Vector2 center = Center;

            if (shape == ArenaShape.Circle)
            {
                float allowed = Mathf.Max(0f, circleRadius - margin);

                // 圆内均匀分布：半径要开平方，否则点会往圆心聚集
                float angle = (float)(rng.NextDouble() * Mathf.PI * 2.0);
                float distance = allowed * Mathf.Sqrt((float)rng.NextDouble());

                return center + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * distance;
            }

            Vector2 half = HalfSize;
            float innerX = Mathf.Max(0f, half.x - margin);
            float innerY = Mathf.Max(0f, half.y - margin);

            float x = ((float)rng.NextDouble() * 2f - 1f) * innerX;
            float y = ((float)rng.NextDouble() * 2f - 1f) * innerY;

            return center + new Vector2(x, y);
        }

        /// <summary>边界内可用面积占比（圆形下用包围盒近似，给 FPS/Mass 估算用）。</summary>
        public Vector2 Size => shape == ArenaShape.Rectangle
            ? rectangleSize
            : new Vector2(circleRadius * 2f, circleRadius * 2f);

        // ------------------------------ 物理约束 ------------------------------

        /// <summary>
        /// 把一个刚体约束在竞技区内。每个物理帧在 FixedUpdate 里调用。
        /// 返回 true 表示这一帧做了越界纠正。
        ///
        /// 关键点：只改 position 和 velocity，不加任何 Collider，因此不会触发物理碰撞、
        /// 不会被物理求解器反复修正，性能和手感都好。
        /// </summary>
        public bool Constrain(Rigidbody2D body, float radius)
        {
            if (body == null) return false;

            Vector2 position = body.position;
            Vector2 clamped = ClampPosition(position, radius);

            if ((clamped - position).sqrMagnitude < 0.000001f)
            {
                // 已经在界内，什么都不用做
                return false;
            }

            Vector2 inward = (clamped - position).normalized;   // 朝内的方向
            float overflow = Vector2.Distance(position, clamped);

            if (clampPosition) body.position = clamped;

            Vector2 velocity = body.linearVelocity;

            // 1) 减掉朝外的速度分量：球沿着边界滑行，而不是被“撞停”
            float outwardSpeed = Vector2.Dot(velocity, -inward);
            if (outwardSpeed > 0f) velocity += inward * outwardSpeed;

            // 2) 补一点朝内的推力（越界越深越强，但有上限），让它尽快离开边缘
            if (pushBackStrength > 0f)
            {
                float push = Mathf.Min(pushBackStrength * overflow, maxPushBackSpeed);
                Vector2 inwardVelocity = inward * push;

                // 不覆盖玩家已有的同向速度，只做“至少要有这么快”的补偿
                float currentInward = Vector2.Dot(velocity, inward);
                if (currentInward < push) velocity += inward * (push - currentInward);
            }

            body.linearVelocity = velocity;
            return true;
        }

        // ------------------------------ 可视化 ------------------------------

        private void CreateBorderLine()
        {
            borderLine = gameObject.AddComponent<LineRenderer>();
            borderLine.useWorldSpace = true;
            borderLine.loop = true;
            borderLine.startWidth = borderWidth;
            borderLine.endWidth = borderWidth;
            borderLine.positionCount = shape == ArenaShape.Circle ? 64 : 4;

            var shader = Shader.Find("Sprites/Default");
            if (shader != null)
            {
                borderLine.material = new Material(shader);
                borderLine.material.color = borderColor;
            }

            borderLine.startColor = borderColor;
            borderLine.endColor = borderColor;
            borderLine.sortingOrder = 200;   // 画在球和食物之上

            UpdateBorderPositions();
        }

        private void UpdateBorderPositions()
        {
            if (borderLine == null) return;

            Vector2 center = Center;

            if (shape == ArenaShape.Circle)
            {
                if (borderLine.positionCount != 64) borderLine.positionCount = 64;

                for (int i = 0; i < 64; i++)
                {
                    float angle = (float)i / 64f * Mathf.PI * 2f;
                    borderLine.SetPosition(i, new Vector3(
                        center.x + Mathf.Cos(angle) * circleRadius,
                        center.y + Mathf.Sin(angle) * circleRadius,
                        0f));
                }
            }
            else
            {
                if (borderLine.positionCount != 4) borderLine.positionCount = 4;

                Vector2 half = HalfSize;
                borderLine.SetPosition(0, new Vector3(center.x - half.x, center.y - half.y, 0f));
                borderLine.SetPosition(1, new Vector3(center.x - half.x, center.y + half.y, 0f));
                borderLine.SetPosition(2, new Vector3(center.x + half.x, center.y + half.y, 0f));
                borderLine.SetPosition(3, new Vector3(center.x + half.x, center.y - half.y, 0f));
            }
        }

        private void OnDrawGizmos()
        {
            Vector2 center = Center;

            Gizmos.color = new Color(0.4f, 0.9f, 1f, 0.85f);

            if (shape == ArenaShape.Circle)
            {
                Gizmos.DrawWireSphere(center, circleRadius);

                // 画十字辅助线，方便看清圆心
                Gizmos.DrawLine(center + Vector2.left * circleRadius, center + Vector2.right * circleRadius);
                Gizmos.DrawLine(center + Vector2.down * circleRadius, center + Vector2.up * circleRadius);
            }
            else
            {
                Vector2 half = HalfSize;
                Gizmos.DrawWireCube(center, new Vector3(half.x * 2f, half.y * 2f, 0.1f));
            }
        }

        private void OnValidate()
        {
            rectangleSize.x = Mathf.Max(1f, rectangleSize.x);
            rectangleSize.y = Mathf.Max(1f, rectangleSize.y);
            circleRadius = Mathf.Max(1f, circleRadius);
            pushBackStrength = Mathf.Max(0f, pushBackStrength);
            maxPushBackSpeed = Mathf.Max(0f, maxPushBackSpeed);

            if (Application.isPlaying) UpdateBorderPositions();
        }
    }
}
