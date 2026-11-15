using System.Collections.Generic;
using UnityEngine;

namespace QiuQiu
{
    /// <summary>
    /// AI 敌人：Wander（游走） / Chase（追逐） / Flee（逃跑） 三状态机。
    ///
    /// 性能设计（这是本脚本最重要的部分，手机上必须省）：
    ///   1. 不做每帧扫描：只在 scanInterval（默认 0.2s）到点时扫一次，且每只 AI 的第一次扫描
    ///      时间随机错开，避免“同一帧所有 AI 一起扫”造成感知卡顿。
    ///   2. 扫描用 Physics2D.OverlapCircle 的非分配重载（传 ContactFilter2D + 预分配数组），
    ///      不产生 GC；数组大小固定，满了就忽略多出来的碰撞体（AI 不需要知道全部）。
    ///   3. 扫描半径有限（默认 8），并且只接受带 SizeController / Edible 的目标。
    ///   4. 决策结果（当前状态 + 当前目标）缓存起来，FixedUpdate 只做“朝缓存目标移动”这一件廉价事。
    ///   5. 用 ContactFilter2D.useTriggers = true，否则球的“吞噬感应触发体”和食物（Trigger）都扫不到。
    ///
    /// 组件要求：Rigidbody2D + SizeController + Eater（Eater 负责真正执行吞噬）。
    /// 注意：AI 不要挂 Edible —— Edible 是“谁碰到都能吃掉它”的食物组件，
    ///       挂到 AI 身上会让小球也能一口吞掉大 AI，破坏“大吞小”规则。
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Rigidbody2D))]
    [RequireComponent(typeof(SizeController))]
    public class AIController : MonoBehaviour
    {
        public enum AIState
        {
            Wander,
            Chase,
            Flee
        }

        public enum ChasePreference
        {
            食物优先,
            小球优先,
            就近
        }

        [Header("阵营")]
        [Tooltip("同阵营的球不会互相吞噬（Eater 会跳过同 Team）。玩家是 0，AI 建议 >= 1。")]
        [SerializeField] private int team = 1;

        [Header("移动")]
        [Tooltip("基础移动速度（世界单位/秒）。Wander 状态用这个速度。")]
        [SerializeField] private float moveSpeed = 3.2f;

        [Tooltip("Chase 状态的速度倍率。")]
        [SerializeField] private float chaseSpeedFactor = 1.15f;

        [Tooltip("Flee 状态的速度倍率。")]
        [SerializeField] private float fleeSpeedFactor = 1.3f;

        [Tooltip("速度收敛加速度：越大转向越猛，越小越像大船漂移。")]
        [SerializeField] private float acceleration = 26f;

        [Tooltip("转向时的角速度上限（度/秒），让 AI 不会瞬间掉头。")]
        [SerializeField] private float maxTurnDegreesPerSecond = 540f;

        [Header("扫描")]
        [Tooltip("感知半径。越大越聪明，但 OverlapCircle 开销也越大。")]
        [SerializeField] private float scanRadius = 8f;

        [Tooltip("扫描间隔（秒）。0.2 = 每秒 5 次，对 AI 来说足够灵敏。")]
        [SerializeField] private float scanInterval = 0.2f;

        [Tooltip("扫描时最多接收多少个碰撞体（数组固定大小，避免 GC）。")]
        [SerializeField] private int scanBufferSize = 32;

        [Tooltip("哪些层参与扫描。默认 Everything。")]
        [SerializeField] private LayerMask scanLayerMask = ~0;

        [Header("决策")]
        [Tooltip("威胁进入这个距离就进入 Flee（哪怕同时看到食物）。")]
        [SerializeField] private float fleeRadius = 6f;

        [Tooltip("距离目标多近就算“到达”，到达后重新挑目标（防止贴脸抖动）。")]
        [SerializeField] private float arriveDistance = 0.6f;

        [Tooltip("追什么：食物 / 小球 / 就近。")]
        [SerializeField] private ChasePreference chasePreference = ChasePreference.就近;

        [Header("游走 Wander")]
        [Tooltip("游走时每隔多久换一次随机方向。")]
        [SerializeField] private float wanderChangeInterval = 2.5f;

        [Tooltip("离边界多近就开始主动往回走。")]
        [SerializeField] private float borderAvoidMargin = 2.5f;

        [Tooltip("离边界太近时，朝内转向的权重（越大越早转身）。")]
        [SerializeField] private float borderAvoidWeight = 2.2f;

        private Rigidbody2D rb;
        private SizeController self;
        private ContactFilter2D filter;
        private Collider2D[] scanBuffer;

        private AIState state = AIState.Wander;

        // 当前目标（Chase 用）
        private Transform chaseTarget;
        private Edible chaseFood;

        // 当前威胁（Flee 用，可能有多个，逃跑方向是加权和）
        private readonly List<SizeController> threats = new List<SizeController>(8);

        private Vector2 wanderDirection = Vector2.right;
        private float nextWanderChangeTime;
        private float nextScanTime;

        public AIState CurrentState => state;
        public int Team => team;
        public string CurrentTargetName =>
            chaseTarget != null ? chaseTarget.name : (chaseFood != null ? chaseFood.name : "-");

        /// <summary>
        /// 给运行时生成的 AI 一点个体差异（补员时调用）：
        /// speedScale 缩放移动速度，scanScale 缩放扫描半径。
        /// 用乘法而不是直接赋值，这样 Inspector 里的基准值依然有效。
        /// </summary>
        public void ApplyRandomProfile(float speedScale, float scanScale)
        {
            moveSpeed = Mathf.Max(0.1f, moveSpeed * Mathf.Max(0.1f, speedScale));
            scanRadius = Mathf.Clamp(scanRadius * Mathf.Max(0.1f, scanScale), 3f, 20f);
        }

        private void Awake()
        {
            rb = GetComponent<Rigidbody2D>();
            self = GetComponent<SizeController>();

            rb.gravityScale = 0f;
            rb.freezeRotation = true;
            rb.interpolation = RigidbodyInterpolation2D.Interpolate;
            rb.collisionDetectionMode = CollisionDetectionMode2D.Continuous;

            scanBufferSize = Mathf.Clamp(scanBufferSize, 4, 128);
            scanBuffer = new Collider2D[scanBufferSize];

            filter = new ContactFilter2D
            {
                useTriggers = true,      // 必须：食物和吞噬感应体都是 Trigger
                useLayerMask = scanLayerMask.value != ~0,
                layerMask = scanLayerMask
            };

            PickNewWanderDirection();

            // 首扫时间随机错开，避免所有 AI 挤在同一帧扫描
            nextScanTime = Time.time + Random.Range(0f, Mathf.Max(0.01f, scanInterval));
        }

        private void FixedUpdate()
        {
            if (self == null || self.IsDead) return;

            if (Time.time >= nextScanTime)
            {
                nextScanTime = Time.time + Mathf.Max(0.02f, scanInterval);
                Scan();
            }

            Vector2 desiredDirection = DecideDirection();

            // 软边界：离边缘太近时把方向往场内拉
            desiredDirection = ApplyBorderAvoidance(desiredDirection);

            ApplyMovement(desiredDirection);

            // 世界边界（软）：越界会被夹回并抹掉朝外速度
            if (ArenaManager.Instance != null) ArenaManager.Instance.Constrain(rb, self.Radius);
        }

        // ------------------------------ 感知 ------------------------------

        /// <summary>扫描周围一圈，刷新“威胁列表 / 追逐目标 / 状态”。</summary>
        private void Scan()
        {
            threats.Clear();
            chaseTarget = null;
            chaseFood = null;

            int count = Physics2D.OverlapCircle(rb.position, scanRadius, filter, scanBuffer);

            SizeController nearestThreat = null;
            float nearestThreatDistanceSqr = float.MaxValue;

            SizeController nearestPrey = null;
            float nearestPreyDistanceSqr = float.MaxValue;

            Edible nearestFood = null;
            float nearestFoodDistanceSqr = float.MaxValue;

            for (int i = 0; i < count; i++)
            {
                Collider2D hit = scanBuffer[i];
                if (hit == null) continue;

                // 1) 是不是一个“球”
                SizeController other = hit.GetComponentInParent<SizeController>();
                if (other != null && other != self && !other.IsDead)
                {
                    float sqrDistance = ((Vector2)other.transform.position - rb.position).sqrMagnitude;

                    if (other.CanEat(self))
                    {
                        // 它能吃我 → 威胁
                        if (!threats.Contains(other)) threats.Add(other);
                        if (sqrDistance < nearestThreatDistanceSqr)
                        {
                            nearestThreatDistanceSqr = sqrDistance;
                            nearestThreat = other;
                        }
                    }
                    else if (self.CanEat(other) && other.Team != team)
                    {
                        // 我能吃它，且不是同阵营 → 猎物
                        if (sqrDistance < nearestPreyDistanceSqr)
                        {
                            nearestPreyDistanceSqr = sqrDistance;
                            nearestPrey = other;
                        }
                    }

                    continue;
                }

                // 2) 是不是一个食物
                Edible food = hit.GetComponentInParent<Edible>();
                if (food != null)
                {
                    float sqrDistance = ((Vector2)food.transform.position - rb.position).sqrMagnitude;
                    if (sqrDistance < nearestFoodDistanceSqr)
                    {
                        nearestFoodDistanceSqr = sqrDistance;
                        nearestFood = food;
                    }
                }
            }

            // 3) 状态优先级：有威胁 → Flee；没威胁有猎物/食物 → Chase；否则 Wander
            bool threatInFleeRange = nearestThreat != null &&
                                     nearestThreatDistanceSqr <= fleeRadius * fleeRadius;

            if (threatInFleeRange)
            {
                state = AIState.Flee;
                chaseTarget = null;
                chaseFood = null;
            }
            else
            {
                // 威胁不在逃跑半径内，但如果它比猎物更近，还是先躲（保持一点谨慎）
                bool preferFlee = nearestThreat != null &&
                                  (nearestPrey == null && nearestFood == null) == false &&
                                  nearestThreatDistanceSqr < Mathf.Min(nearestPreyDistanceSqr, nearestFoodDistanceSqr);

                if (preferFlee)
                {
                    state = AIState.Flee;
                }
                else
                {
                    switch (chasePreference)
                    {
                        case ChasePreference.食物优先:
                            chaseFood = nearestFood;
                            chaseTarget = chaseFood != null ? chaseFood.transform : (nearestPrey != null ? nearestPrey.transform : null);
                            break;

                        case ChasePreference.小球优先:
                            chaseTarget = nearestPrey != null ? nearestPrey.transform : null;
                            chaseFood = chaseTarget == null ? nearestFood : null;
                            if (chaseTarget == null && chaseFood != null) chaseTarget = chaseFood.transform;
                            break;

                        default: // 就近
                            if (nearestPreyDistanceSqr <= nearestFoodDistanceSqr)
                            {
                                chaseTarget = nearestPrey != null ? nearestPrey.transform : null;
                                chaseFood = chaseTarget == null ? nearestFood : null;
                            }
                            else
                            {
                                chaseFood = nearestFood;
                                chaseTarget = chaseFood != null ? chaseFood.transform : null;
                            }
                            break;
                    }

                    state = chaseTarget != null ? AIState.Chase : AIState.Wander;
                }
            }

            // 到达目标后就换方向，避免贴脸原地抖
            if (state == AIState.Chase && chaseTarget != null)
            {
                float sqrDistance = ((Vector2)chaseTarget.position - rb.position).sqrMagnitude;
                if (sqrDistance <= arriveDistance * arriveDistance)
                {
                    chaseTarget = null;
                    chaseFood = null;
                    if (threats.Count == 0) state = AIState.Wander;
                    PickNewWanderDirection();
                }
            }

            if (state == AIState.Wander && Time.time >= nextWanderChangeTime)
            {
                PickNewWanderDirection();
            }
        }

        // ------------------------------ 决策 / 移动 ------------------------------

        private Vector2 DecideDirection()
        {
            switch (state)
            {
                case AIState.Flee:
                    return FleeDirection();

                case AIState.Chase:
                    if (chaseTarget != null)
                    {
                        Vector2 toTarget = (Vector2)chaseTarget.position - rb.position;
                        if (toTarget.sqrMagnitude > 0.0001f) return toTarget.normalized;
                    }
                    return wanderDirection;

                default:
                    if (Time.time >= nextWanderChangeTime) PickNewWanderDirection();
                    return wanderDirection;
            }
        }

        /// <summary>逃跑方向：所有威胁的反方向的加权和（近的权重大）。</summary>
        private Vector2 FleeDirection()
        {
            Vector2 away = Vector2.zero;
            Vector2 position = rb.position;

            for (int i = 0; i < threats.Count; i++)
            {
                SizeController threat = threats[i];
                if (threat == null || threat.IsDead) continue;

                Vector2 offset = position - (Vector2)threat.transform.position;
                float sqrDistance = Mathf.Max(offset.sqrMagnitude, 0.01f);
                away += offset.normalized * (1f / sqrDistance);
            }

            if (away.sqrMagnitude < 0.0001f)
            {
                // 威胁正好重合（几乎不会发生），随便挑个方向
                return Random.insideUnitCircle.normalized;
            }

            return away.normalized;
        }

        private void ApplyMovement(Vector2 desiredDirection)
        {
            if (desiredDirection.sqrMagnitude < 0.0001f) return;

            float speed = moveSpeed;
            if (state == AIState.Chase) speed *= Mathf.Max(0.1f, chaseSpeedFactor);
            else if (state == AIState.Flee) speed *= Mathf.Max(0.1f, fleeSpeedFactor);

            Vector2 targetVelocity = desiredDirection.normalized * speed;

            // 限制转向速度：从当前速度方向平滑转到目标方向
            Vector2 currentVelocity = rb.linearVelocity;
            if (currentVelocity.sqrMagnitude > 0.0001f)
            {
                float currentAngle = Mathf.Atan2(currentVelocity.y, currentVelocity.x) * Mathf.Rad2Deg;
                float targetAngle = Mathf.Atan2(targetVelocity.y, targetVelocity.x) * Mathf.Rad2Deg;
                float maxDelta = maxTurnDegreesPerSecond * Time.fixedDeltaTime;
                float angle = Mathf.MoveTowardsAngle(currentAngle, targetAngle, maxDelta);

                targetVelocity = new Vector2(Mathf.Cos(angle * Mathf.Deg2Rad), Mathf.Sin(angle * Mathf.Deg2Rad)) * speed;
            }

            rb.linearVelocity = Vector2.MoveTowards(
                currentVelocity, targetVelocity, Mathf.Max(1f, acceleration) * Time.fixedDeltaTime);
        }

        private Vector2 ApplyBorderAvoidance(Vector2 direction)
        {
            ArenaManager arena = ArenaManager.Instance;
            if (arena == null) return direction;

            // 已经接近边界：叠加上“朝中心”的方向
            if (arena.IsInside(rb.position, borderAvoidMargin)) return direction;

            Vector2 toCenter = (arena.Center - rb.position).normalized;
            Vector2 blended = direction + toCenter * borderAvoidWeight;

            if (blended.sqrMagnitude < 0.0001f) return toCenter;
            return blended.normalized;
        }

        private void PickNewWanderDirection()
        {
            Vector2 candidate = Random.insideUnitCircle;
            if (candidate.sqrMagnitude < 0.0001f) candidate = Vector2.right;

            wanderDirection = candidate.normalized;
            nextWanderChangeTime = Time.time + wanderChangeInterval * Random.Range(0.7f, 1.3f);
        }

        // ------------------------------ 编辑器辅助 ------------------------------

        private void OnDrawGizmosSelected()
        {
            Gizmos.color = Color.yellow;
            Gizmos.DrawWireSphere(transform.position, scanRadius);

            Gizmos.color = new Color(1f, 0.4f, 0.4f, 0.9f);
            Gizmos.DrawWireSphere(transform.position, fleeRadius);

            if (chaseTarget != null)
            {
                Gizmos.color = Color.green;
                Gizmos.DrawLine(transform.position, chaseTarget.position);
            }

            if (Application.isPlaying)
            {
                Vector3 labelPos = transform.position + Vector3.up * 1.5f;
#if UNITY_EDITOR
                UnityEditor.Handles.color = Color.white;
                UnityEditor.Handles.Label(labelPos, $"{state} → {CurrentTargetName}");
#endif
            }
        }
    }
}
