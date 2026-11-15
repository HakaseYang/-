using UnityEngine;

namespace QiuQiu
{
    /// <summary>
    /// 玩家分裂出来的“子球”：不自己存主球引用，而是每帧向 PlayerController 要
    /// “当前主球”和“当前输入速度”。
    ///
    /// 为什么改成动态读取？
    ///   以前 Initialize(controller, leader, angle) 把主球引用**固定**存下来，
    ///   主球一旦被 AI 吃掉，leaderBall 变成 null，子球就直接进“原地减速停住”分支
    ///   —— 玩家还在，但球不动了。现在每帧从 controller.MainBall 现取（主球 = 当前质量最大的球），
    ///   主球死了自动换成下一颗，子球永远不会失去目标。
    ///
    /// 跟随方式：速度弹簧（velocity spring）+ 输入前馈
    ///   1. 目标点 = 主球位置 + 方向 * (主球半径 + 自己半径 + 间隙)，即围绕主球的一个“编队槽位”；
    ///   2. 期望速度 = (目标点 - 自己位置) * followStrength
    ///                 + controller.InputVelocity（玩家输入，保证摇杆/WASD 对每颗球都生效）
    ///                 + 0.5 * 主球速度（额外的追赶前馈，避免主球快跑时子球掉队）；
    ///   3. 用 Vector2.MoveTowards 让实际速度平滑逼近期望速度 —— 不会瞬移、不会抖动，
    ///      而且全程只改 rb.linearVelocity，物体依然是物理体，会被别的球挤开、不会穿模。
    ///
    /// 为什么不用“把子球设为 Transform 的子节点”？
    ///   子节点会被主球的缩放影响（主球变大时子球跟着变形），而且父子位置是硬绑定的，
    ///   子球就无法被物理推挤、也无法独立吃食物。用弹簧跟随既保留物理性，又能自动归位。
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Rigidbody2D))]
    [RequireComponent(typeof(SizeController))]
    public class PlayerSubBall : MonoBehaviour
    {
        [Header("跟随参数")]
        [Tooltip("弹簧刚度：越大越贴着槽位，越小越松散。")]
        [SerializeField] private float followStrength = 8f;

        [Tooltip("跟随阶段的最大速度，防止主球瞬移时子球弹飞。")]
        [SerializeField] private float maxFollowSpeed = 18f;

        [Tooltip("速度收敛加速度：越大跟随越硬，越小越软。")]
        [SerializeField] private float followAcceleration = 80f;

        [Tooltip("槽位与主球之间的额外间隙，避免两个球一直判定为接触。")]
        [SerializeField] private float slotGap = 0.06f;

        [Tooltip("合体冷却结束后，子球是否主动“游回”主球中心（这样才会真正重叠并触发合体）。")]
        [SerializeField] private bool returnHomeWhenMergeReady = true;

        [Tooltip("游回合体时的跟随刚度倍率：比正常编队更硬一点，收得更干脆。")]
        [SerializeField] private float returnHomeStrengthMultiplier = 1.2f;

        [Tooltip("游回合体时的速度上限：数值小一点，避免子球高速撞飞主球。")]
        [SerializeField] private float returnHomeMaxSpeed = 4f;

        [Tooltip("离主球过远（被撞飞/被边界卡住）时的最大拉力倍率，防止子球掉队太远。")]
        [SerializeField] private float farAwayBoost = 2.5f;

        private PlayerController leaderController;
        private SizeController self;
        private Rigidbody2D rb;
        private float slotAngle;   // 槽位相对主球的固定角度（弧度）

        /// <summary>由 PlayerController 在分裂 / 接管时调用（主球是动态的，不需要传进来）。</summary>
        public void Initialize(PlayerController controller, float slotAngleDegrees)
        {
            leaderController = controller;
            slotAngle = slotAngleDegrees * Mathf.Deg2Rad;
        }

        /// <summary>兼容旧签名的重载：leader 参数已废弃，实际每帧从 controller.MainBall 取。</summary>
        public void Initialize(PlayerController controller, SizeController leader, float slotAngleDegrees)
        {
            Initialize(controller, slotAngleDegrees);
        }

        private void Awake()
        {
            rb = GetComponent<Rigidbody2D>();
            self = GetComponent<SizeController>();
        }

        private void FixedUpdate()
        {
            if (self == null || self.IsDead) return;

            // 控制器没了 = 玩家整个不存在了，此时才允许停下
            if (leaderController == null)
            {
                leaderController = PlayerController.Active;
                if (leaderController == null)
                {
                    StopGradually();
                    return;
                }
            }

            // 每帧现取主球（质量最大的存活球），主球被吃也不会导致这里失效
            SizeController leader = leaderController.MainBall;

            Vector2 inputVelocity = leaderController.InputVelocity;

            if (leader == null || leader == self)
            {
                // 自己就是主球（或场上只剩自己）：直接响应玩家输入，
                // 不能走“绕自己转圈”的编队逻辑，否则会自己推自己。
                Vector2 direct = Vector2.ClampMagnitude(inputVelocity, maxFollowSpeed);
                rb.linearVelocity = Vector2.MoveTowards(
                    rb.linearVelocity, direct, followAcceleration * Time.fixedDeltaTime);

                if (ArenaManager.Instance != null)
                    ArenaManager.Instance.Constrain(rb, self.Radius);
                return;
            }

            // 槽位随主球/子球体型变化自动外扩
            float distance = leader.Radius + self.Radius + slotGap;
            Vector2 direction = new Vector2(Mathf.Cos(slotAngle), Mathf.Sin(slotAngle));
            Vector2 target = (Vector2)leader.transform.position + direction * distance;

            float strength = followStrength;
            float speedLimit = maxFollowSpeed;

            // 离得太远时加大拉力，避免子球被顶飞后长时间掉队
            float currentDistance = Vector2.Distance(rb.position, leader.transform.position);
            if (currentDistance > distance * 2f)
                strength *= Mathf.Lerp(1f, Mathf.Max(1f, farAwayBoost), Mathf.InverseLerp(distance * 2f, distance * 6f, currentDistance));

            // 合体冷却结束后，槽位收缩到主球中心：子球会自己游回去，靠得够近时由 PlayerController 判定合体
            bool canMergeNow = returnHomeWhenMergeReady && leaderController.CanMerge(self);
            if (canMergeNow)
            {
                target = leader.transform.position;
                strength = followStrength * Mathf.Max(1f, returnHomeStrengthMultiplier);
                speedLimit = Mathf.Min(maxFollowSpeed, Mathf.Max(0.1f, returnHomeMaxSpeed));
            }

            // 期望速度 = 玩家输入（主项，保证操控手感）
            //          + 队形校正（限幅，不能盖过输入，否则子球会被编队弹簧拽着乱飘）
            //          + 主球速度前馈（避免掉队）
            Vector2 inputVelocityClamped = Vector2.ClampMagnitude(inputVelocity, speedLimit);
            Vector2 spring = (target - rb.position) * strength;
            float correctionBudget = Mathf.Max(0f, speedLimit - inputVelocityClamped.magnitude);
            Vector2 correction = Vector2.ClampMagnitude(spring, correctionBudget);

            Vector2 desired = inputVelocityClamped + correction;
            var leaderRb = leader.GetComponent<Rigidbody2D>();
            if (leaderRb != null) desired += leaderRb.linearVelocity * 0.5f;  // 追赶前馈，避免掉队
            desired = Vector2.ClampMagnitude(desired, speedLimit);

            rb.linearVelocity = Vector2.MoveTowards(
                rb.linearVelocity, desired, followAcceleration * Time.fixedDeltaTime);

            // 世界边界（软）：子球同样受约束，避免被主球带着一起越界
            if (ArenaManager.Instance != null)
                ArenaManager.Instance.Constrain(rb, self.Radius);
        }

        private void StopGradually()
        {
            rb.linearVelocity = Vector2.MoveTowards(
                rb.linearVelocity, Vector2.zero, followAcceleration * Time.fixedDeltaTime);
        }

        /// <summary>主球给子球重新分配槽位（例如后续做“重新合体”时用）。</summary>
        public void SetSlotAngle(float slotAngleDegrees)
        {
            slotAngle = slotAngleDegrees * Mathf.Deg2Rad;
        }
    }
}
