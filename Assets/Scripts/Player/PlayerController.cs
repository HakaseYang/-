using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

namespace QiuQiu
{
    /// <summary>
    /// 玩家控制器（常驻管理器，不再挂载在球体上）。
    ///
    /// 为什么要把控制器从球上挪到独立物体（PlayerRoot）？
    ///   旧实现把 PlayerController 挂在“主球”这个 GameObject 上，主球被 AI 吃掉时
    ///   GameObject 连同组件一起被销毁 —— 虽然做了“把控制权移交给最大子球”的交接，
    ///   但交接过程有两个致命副作用：
    ///     1) 新控制器 AddComponent 时立刻跑 Awake/OnEnable，把共享的 InputActionAsset 的
    ///        Action 打开了；同帧稍后旧组件被销毁，它的 OnDisable 又把**同一份** Action 关掉，
    ///        于是新控制器读到的永远是 (0,0) —— 玩家彻底失去控制，子球原地不动。
    ///     2) 每次死亡都要重建组件、复制参数、重绑子球，链路又长又脆。
    ///   现在控制器挂在常驻的 PlayerRoot 上，**任何一颗球被吃掉都不会影响控制器本身**，
    ///   控制目标每帧动态挑选，从根上消除这一类问题。
    ///
    /// 设计要点：
    /// 1. 用 PlayerRoster 记录当前所有存活的玩家球；球被吃时自动移出列表，
    ///    只要列表非空，键盘 / 摇杆就依然能操控剩下的球。
    /// 2. 移动输入施加给列表中**每一颗**需要由控制器驱动的球（带 PlayerSubBall 的子球
    ///    由 PlayerSubBall 自己读取同一份输入，见 InputVelocity）。
    /// 3. 分裂 / 吐球永远从“当前质量最大的存活球”上扣质量（主球优先原则）。
    /// 4. 摄像机 / HUD / GameManager 都读 PlayerController.Active + Balls，天然跟随所有存活球。
    /// </summary>
    [DisallowMultipleComponent]
    public class PlayerController : MonoBehaviour
    {
        /// <summary>当前生效的玩家控制器（常驻，不会随着某颗球死亡而消失）。</summary>
        public static PlayerController Active { get; private set; }

        [Header("Input Asset")]
        [Tooltip("Controls.inputactions，需与 Player 上的 PlayerInput 使用同一份资产")]
        [SerializeField] private InputActionAsset controls;
        [SerializeField] private string actionMapName = "Player";
        [SerializeField] private string moveActionName = "Move";
        [SerializeField] private string splitActionName = "Split";
        [SerializeField] private string spitActionName = "Spit";

        [Header("Movement")]
        [Tooltip("最大移动速度（单位/秒）")]
        [SerializeField] private float moveSpeed = 5f;
        [Tooltip("硬性速度上限，防止外力/叠加导致超速")]
        [SerializeField] private float maxSpeedHardLimit = 12f;
        [Tooltip("加速度，0 表示瞬时启停；数值越小启动/刹车越平滑")]
        [SerializeField] private float acceleration = 60f;
        [Tooltip("摇杆死区，小于该值的输入视为 0，避免手柄漂移")]
        [Range(0f, 0.9f)]
        [SerializeField] private float inputDeadZone = 0.2f;

        [Header("分裂 Split")]
        [Tooltip("质量必须【大于】该值才能分裂（用户需求：> 30）。")]
        [SerializeField] private float splitMinMass = 30f;
        [Tooltip("分裂成功后两半各占的比例（0.5 = 均分）。")]
        [Range(0.2f, 0.8f)]
        [SerializeField] private float splitMassRatio = 0.5f;
        [Tooltip("玩家所有球的数量上限（含主球）。")]
        [SerializeField] private int maxBallCount = 8;
        [Tooltip("分裂冷却（秒）。")]
        [SerializeField] private float splitCooldown = 0.5f;
        [Tooltip("分裂出去的新球初速度（瞬间冲量，单位/秒）。")]
        [SerializeField] private float splitLaunchSpeed = 14f;
        [Tooltip("新球生成时与母球的额外间隙，防止生成就重叠。")]
        [SerializeField] private float splitSpawnGap = 0.05f;

        [Header("吐球 Spit")]
        [Tooltip("每次吐球消耗的质量（用户需求：扣除约 10 点）。")]
        [SerializeField] private float spitMassCost = 10f;
        [Tooltip("吐出去的那个球（食物）携带的质量，通常小于消耗量，差额就是“损耗”。")]
        [SerializeField] private float spitFoodMass = 5f;
        [Tooltip("吐球后自己至少要保留的质量，低于它就吐不出来。")]
        [SerializeField] private float minMassAfterSpit = 5f;
        [Tooltip("吐球冷却（秒）。")]
        [SerializeField] private float spitCooldown = 0.25f;
        [Tooltip("吐出去的球的初速度。")]
        [SerializeField] private float spitLaunchSpeed = 16f;
        [Tooltip("吐出去的球生成位置离球心的额外距离。")]
        [SerializeField] private float spitSpawnGap = 0.15f;
        [Tooltip("吐出去的球多久之后才能被别人（包括自己）吃掉，防止吐完立刻吸回来。")]
        [SerializeField] private float spitArmDelay = 0.12f;

        [Header("合体 Merge")]
        [Tooltip("分裂后多久才允许重新合体（秒）。用户需求：15 秒。")]
        [SerializeField] private float mergeCooldown = 15f;
        [Tooltip("两个球“贴在一起”的判定余量（世界单位）：中心距 <= 半径和 + 该值 就算碰撞，可以合体。")]
        [SerializeField] private float mergeContactTolerance = 0.15f;
        [Tooltip("合体时的吸附过渡时长（秒）：小球会被“吸”进大球中心，避免瞬间消失。")]
        [SerializeField] private float mergePullDuration = 0.18f;
        [Tooltip("合体吸附的缓动曲线强度：1 = 线性，2 = 先慢后快，越大越快贴上去。")]
        [Range(0.5f, 4f)]
        [SerializeField] private float mergePullEase = 2f;

        [Header("接管")]
        [Tooltip("启动时自动把场景里所有 team == 0 的球接管进玩家球列表（列表为空时也会自动重试）。")]
        [SerializeField] private bool autoAdoptPlayerBalls = true;

        private readonly InputReader playerInput = new InputReader();

        // the list of balls and the main ball live in PlayerRoster
        private readonly PlayerRoster roster = new PlayerRoster();

        // split / spit rules (cooldowns, mass math, spawn point) live in PlayerActions
        private readonly PlayerActions actions = new PlayerActions();

        private Vector2 moveInput;

        // 分裂 / 吐球的朝向：最后一次非零输入方向（没有输入时沿用上一次，初始朝右）
        private Vector2 facing = Vector2.right;

        // 每个球“允许合体”的时间点（Time.time）。主球是 0（永远可合体），子球分裂时 = Time.time + mergeCooldown
        private readonly Dictionary<SizeController, float> mergeReadyAt = new Dictionary<SizeController, float>();

        // 正在执行“吸附过渡”的合体任务
        private readonly List<MergeJob> mergeJobs = new List<MergeJob>();

        // 正处于合体过程中、马上要被销毁的小球（用它来区分“被敌人吃掉”和“自己合体”）
        private readonly HashSet<SizeController> mergingBalls = new HashSet<SizeController>();

        private sealed class MergeJob
        {
            public SizeController small;
            public SizeController keeper;
            public Vector2 startPosition;
            public Vector3 startScale;
            public float elapsed;
        }

        /// <summary>
        /// 一份“可拷贝”的调参快照：保留给 GameManager 在重开一局时还原手调数值。
        /// （控制器现在常驻，正常的死亡复活已经不需要它了。）
        /// </summary>
        [System.Serializable]
        public class PlayerTuning
        {
            public InputActionAsset controls;

            public float moveSpeed;
            public float maxSpeedHardLimit;
            public float acceleration;
            public float inputDeadZone;

            public float splitMinMass;
            public float splitMassRatio;
            public int maxBallCount;
            public float splitCooldown;
            public float splitLaunchSpeed;
            public float splitSpawnGap;

            public float spitMassCost;
            public float spitFoodMass;
            public float minMassAfterSpit;
            public float spitCooldown;
            public float spitLaunchSpeed;
            public float spitSpawnGap;
            public float spitArmDelay;

            public float mergeCooldown;
            public float mergeContactTolerance;
            public float mergePullDuration;
            public float mergePullEase;
        }

        /// <summary>把当前 Inspector 上的调参抓成快照。</summary>
        public PlayerTuning CaptureTuning()
        {
            return new PlayerTuning
            {
                controls = controls,

                moveSpeed = moveSpeed,
                maxSpeedHardLimit = maxSpeedHardLimit,
                acceleration = acceleration,
                inputDeadZone = inputDeadZone,

                splitMinMass = splitMinMass,
                splitMassRatio = splitMassRatio,
                maxBallCount = maxBallCount,
                splitCooldown = splitCooldown,
                splitLaunchSpeed = splitLaunchSpeed,
                splitSpawnGap = splitSpawnGap,

                spitMassCost = spitMassCost,
                spitFoodMass = spitFoodMass,
                minMassAfterSpit = minMassAfterSpit,
                spitCooldown = spitCooldown,
                spitLaunchSpeed = spitLaunchSpeed,
                spitSpawnGap = spitSpawnGap,
                spitArmDelay = spitArmDelay,

                mergeCooldown = mergeCooldown,
                mergeContactTolerance = mergeContactTolerance,
                mergePullDuration = mergePullDuration,
                mergePullEase = mergePullEase,
            };
        }

        /// <summary>把快照写回本控制器（Awake 阶段调用，早于 ResolveActions）。</summary>
        public void ApplyTuning(PlayerTuning tuning)
        {
            if (tuning == null) return;

            if (tuning.controls != null) controls = tuning.controls;

            moveSpeed = tuning.moveSpeed;
            maxSpeedHardLimit = tuning.maxSpeedHardLimit;
            acceleration = tuning.acceleration;
            inputDeadZone = tuning.inputDeadZone;

            splitMinMass = tuning.splitMinMass;
            splitMassRatio = tuning.splitMassRatio;
            maxBallCount = tuning.maxBallCount;
            splitCooldown = tuning.splitCooldown;
            splitLaunchSpeed = tuning.splitLaunchSpeed;
            splitSpawnGap = tuning.splitSpawnGap;

            spitMassCost = tuning.spitMassCost;
            spitFoodMass = tuning.spitFoodMass;
            minMassAfterSpit = tuning.minMassAfterSpit;
            spitCooldown = tuning.spitCooldown;
            spitLaunchSpeed = tuning.spitLaunchSpeed;
            spitSpawnGap = tuning.spitSpawnGap;
            spitArmDelay = tuning.spitArmDelay;

            mergeCooldown = tuning.mergeCooldown;
            mergeContactTolerance = tuning.mergeContactTolerance;
            mergePullDuration = tuning.mergePullDuration;
            mergePullEase = tuning.mergePullEase;

            SyncActions();
            ResolveActions();
        }

        // ------------------------------ 生命周期 ------------------------------

        // 复活时用来塑造新球的调参快照（GameManager 持有）
        private static PlayerTuning s_PendingTuning;

        private void Awake()
        {
            Active = this;

            if (s_PendingTuning != null)
            {
                ApplyTuning(s_PendingTuning);
                s_PendingTuning = null;
            }

            playerInput.SplitPressed += OnSplitPressed;
            playerInput.SpitPressed += OnSpitPressed;

            SyncActions();
            ResolveActions();
        }

        // the split / spit numbers live in the inspector fields above, so they are copied over here
        private void SyncActions()
        {
            actions.Configure(roster, SpawnBall, CaptureTuning(), name);
        }

        private void OnDestroy()
        {
            if (Active == this) Active = null;

            for (int i = 0; i < roster.Count; i++)
            {
                SizeController ball = roster.Balls[i];
                if (ball != null) ball.Eaten -= OnBallEaten;
            }
            roster.Clear();
        }

        private void Start()
        {
            if (autoAdoptPlayerBalls) AdoptOrphanPlayerBalls();
        }

        private void ResolveActions()
        {
            playerInput.Configure(controls, actionMapName, moveActionName, splitActionName, spitActionName);
            playerInput.Resolve();

            // the component may get the asset after OnEnable already ran
            if (isActiveAndEnabled)
            {
                playerInput.Enable();
            }
        }

        private void OnEnable()
        {
            playerInput.Enable();
        }

        private void OnDisable()
        {
            // shared InputActionAsset: only the controller that is still Active may switch it off
            if (Active != null && Active != this) return;

            playerInput.Disable();
            moveInput = Vector2.zero;
        }

        private void OnSplitPressed()
        {
            TrySplit();
        }

        private void OnSpitPressed()
        {
            TrySpit();
        }

        private void Update()
        {
            moveInput = playerInput.ReadMove();
        }

        private void FixedUpdate()
        {
            // 1. 先清掉已经销毁/被吃掉的球（事件漏掉时的兜底）
            roster.PruneDead(UnhookBall);

            // 2. 列表空了就尝试重新接管场景里 team == 0 的球
            if (roster.Count == 0 && autoAdoptPlayerBalls) AdoptOrphanPlayerBalls();

            // 3. 主球 = 当前质量最大的存活球（每次重算，不缓存死引用）
            roster.RefreshMain();

            // 4. 输入处理
            Vector2 input = Vector2.ClampMagnitude(moveInput, 1f);
            if (input.magnitude < inputDeadZone) input = Vector2.zero;
            if (input.sqrMagnitude > 0.0001f) facing = input.normalized;

            // 5. 把所有非子球管理的球都按输入驱动（含主球；主球死了就驱动剩下的）
            MovePlayerBalls(input);

            // 6. 合体：先推进已有的吸附动画，再检测新的可合体配对
            UpdateMergeJobs();
            DetectMerges();
        }

        /// <summary>
        /// 按输入驱动玩家的球。
        /// 带 PlayerSubBall 的子球由 PlayerSubBall 自己驱动（它会读取同一份 InputVelocity，
        /// 所以“输入施加给所有存活球”依然成立）；其余球（主球 / 没有跟随组件的球）在这里直接驱动。
        /// </summary>
        private void MovePlayerBalls(Vector2 input)
        {
            for (int i = 0; i < roster.Count; i++)
            {
                SizeController ball = roster.Balls[i];
                if (ball == null || ball.IsDead) continue;

                var sub = ball.GetComponent<PlayerSubBall>();
                if (sub != null && sub.enabled)
                {
                    // 子球：输入由 PlayerSubBall 读取（它会把 InputVelocity 叠加到队形校正上）
                    continue;
                }

                MoveBall(ball, input);
            }
        }

        /// <summary>把一颗球按目标方向推动（三重限速 + 软边界）。</summary>
        private void MoveBall(SizeController ball, Vector2 input)
        {
            var rb = ball.GetComponent<Rigidbody2D>();
            if (rb == null) return;

            // 没有输入时直接硬清零：这是“球自己慢慢漂”的根治办法。
            // 之前用 MoveTowards 逼近 0，虽然理论上会收敛，但只要有任何系统（子球弹簧、
            // 物理接触、边界推回）每帧写入一个极小的非 0 速度，就会形成永久漂移；
            // 这里直接清零，不给残余速度留活路。
            if (input.sqrMagnitude <= 0.0001f)
            {
                rb.linearVelocity = Vector2.zero;

                if (ArenaManager.Instance != null)
                    ArenaManager.Instance.Constrain(rb, ball.Radius);

                return;
            }

            Vector2 targetVelocity = input * (moveSpeed * ball.MoveSpeedFactor);
            Vector2 current = rb.linearVelocity;

            Vector2 next = acceleration <= 0f
                ? targetVelocity
                : Vector2.MoveTowards(current, targetVelocity, acceleration * Time.fixedDeltaTime);

            if (next.sqrMagnitude > maxSpeedHardLimit * maxSpeedHardLimit)
                next = Vector2.ClampMagnitude(next, maxSpeedHardLimit);

            rb.linearVelocity = next;

            if (ArenaManager.Instance != null)
                ArenaManager.Instance.Constrain(rb, ball.Radius);
        }

        // ------------------------------ 球列表维护 ------------------------------

        /// <summary>把一颗球纳入玩家控制（重复调用安全）。</summary>
        public void RegisterBall(SizeController ball)
        {
            if (ball == null || ball.IsDead) return;
            if (roster.Contains(ball)) return;

            roster.Add(ball);
            ball.Eaten += OnBallEaten;

            if (!mergeReadyAt.ContainsKey(ball)) mergeReadyAt[ball] = 0f;
        }

        /// <summary>把一颗球移出玩家控制（被吃掉 / 主动丢弃）。</summary>
        public void UnregisterBall(SizeController ball)
        {
            if (ball == null) return;

            ball.Eaten -= OnBallEaten;
            roster.Remove(ball);
            mergeReadyAt.Remove(ball);
            mergingBalls.Remove(ball);
            mergeJobs.RemoveAll(job => job.small == ball);
        }

        // one place unhooks a ball the roster dropped: events plus cooldown bookkeeping
        private void UnhookBall(SizeController ball)
        {
            ball.Eaten -= OnBallEaten;
            mergeReadyAt.Remove(ball);
            mergingBalls.Remove(ball);
            mergeJobs.RemoveAll(job => job.small == ball);
        }

        /// <summary>
        /// 启动时 / 球列表空时：把场景里属于玩家（team == 0）的球接管进来。
        /// 这样即使场景是手工搭的、或者某帧事件漏了，也不会出现“有球但控制不了”。
        /// </summary>
        private void AdoptOrphanPlayerBalls()
        {
            var registry = SizeController.Registry;
            for (int i = 0; i < registry.Count; i++)
            {
                SizeController ball = registry[i];
                if (ball == null || ball.IsDead || ball.Team != Team.Player) continue;
                if (roster.Contains(ball)) continue;

                RegisterBall(ball);

                // 非主球要保证有跟随组件（否则它会被当成“主球”直接吃输入，队形会乱）
                if (roster.Count > 1 && ball.GetComponent<PlayerSubBall>() == null)
                {
                    var sub = ball.gameObject.AddComponent<PlayerSubBall>();
                    sub.Initialize(this, actions.SlotAngleFor(roster.Count - 1));
                }
            }
        }

        private void OnBallEaten(SizeController victim, SizeController killer)
        {
            victim.Eaten -= OnBallEaten;
            mergeReadyAt.Remove(victim);
            mergingBalls.Remove(victim);
            mergeJobs.RemoveAll(job => job.small == victim);

            bool wasMain = roster.Main == victim;
            roster.Remove(victim);

            string killerName = killer != null ? killer.name : "未知";

            if (wasMain)
            {
                Debug.Log($"[玩家] 主球被 {killerName} 吃掉，剩余球数 {roster.Count}");

                // 主球只是列表里的一员：重新挑一个最大的继续控制，
                // 控制器本身在 PlayerRoot 上，不会跟着球一起消失。
                if (roster.Main != null)
                {
                    Debug.Log($"[玩家] 控制权自动切换到 {roster.Main.name}（质量 {roster.Main.Mass:F0}），剩余球数 {roster.Count}");
                }
            }
            else
            {
                Debug.Log($"[玩家] 子球被吃掉，剩余球数 {roster.Count}");
            }

            if (roster.Count == 0) NotifyDefeat(killer);
        }

        /// <summary>把最后的死讯交给 GameManager（没有 GameManager 时只打日志）。</summary>
        private void NotifyDefeat(SizeController killer)
        {
            Debug.Log($"[玩家] 所有球都被吃掉了，游戏结束（死于 {(killer != null ? killer.name : "未知")}）");

            if (GameManager.Instance != null) GameManager.Instance.NotifyPlayerDefeated(killer);
        }

        // ------------------------------ 分裂 / 吐球 ------------------------------

        public bool TrySplit()
        {
            SizeController source;
            SizeController newBall;
            float newMass;

            if (!actions.TrySplit(facing, out source, out newBall, out newMass)) return false;

            PlayerSubBall sub = newBall.gameObject.AddComponent<PlayerSubBall>();
            sub.Initialize(this, actions.SlotAngleFor(roster.Count - 1));

            // a fresh split ball has to wait out the merge cooldown before it can join again
            mergeReadyAt[newBall] = Time.time + mergeCooldown;

            Debug.Log($"[split] {source.Mass + newMass:F0} -> {source.Mass:F0} + {newMass:F0}, balls {roster.Count}/{maxBallCount}");
            if (AudioManager.Instance != null) AudioManager.Instance.PlaySplit();
            return true;
        }

        public bool TrySpit()
        {
            SizeController source;

            if (!actions.TrySpit(facing, out source)) return false;

            Debug.Log($"[spit] {source.Mass:F0} left, food mass {spitFoodMass:F0}");
            if (AudioManager.Instance != null) AudioManager.Instance.PlaySpit();
            return true;
        }
        // ------------------------------ 球的生成 ------------------------------

        /// <summary>
        /// 运行时生成一颗玩家球并登记到列表（复活 / 分裂共用）。
        /// 控制器本身不需要迁移，所以这里只造球、不改控制器。
        /// </summary>
        public SizeController SpawnBall(string ballName, Vector3 position, float mass, Sprite sprite, Color color)
        {
            if (sprite == null)
            {
                Debug.LogError("[PlayerController] SpawnBall 缺少精灵，无法生成玩家球。");
                return null;
            }

            GameObject go = BallFactory.CreateBall(ballName, position, mass, sprite, color, Team.Player);
            go.tag = "Player";

            SizeController ball = go.GetComponent<SizeController>();
            RegisterBall(ball);
            return ball;
        }

        /// <summary>
        /// 兼容旧用法的入口（GameManager 早期版本用过）：生成一颗玩家球并返回它。
        /// </summary>
        public static SizeController SpawnAt(Vector3 position, float mass, Sprite sprite, Color color,
            PlayerTuning tuning, int sortingOrder = 10)
        {
            PlayerController controller = EnsureExists();
            if (controller == null) return null;

            if (tuning != null) controller.ApplyTuning(tuning);

            return controller.SpawnBall("Player", position, mass, sprite, color);
        }

        /// <summary>拿到（必要时创建）常驻的玩家控制器。</summary>
        public static PlayerController EnsureExists()
        {
            if (Active != null) return Active;

            var found = FindFirstObjectByType<PlayerController>();
            if (found != null)
            {
                Active = found;
                return found;
            }

            var go = new GameObject("PlayerRoot");
            var created = go.AddComponent<PlayerController>();
            Active = created;
            return created;
        }

        // ------------------------------ 合体 ------------------------------

        /// <summary>这个球现在能不能参与合体（冷却结束、没在合体中、还活着）。</summary>
        public bool CanMerge(SizeController ball)
        {
            if (ball == null || ball.IsDead) return false;
            if (mergingBalls.Contains(ball)) return false;
            if (!mergeReadyAt.TryGetValue(ball, out float readyAt)) return false;
            return Time.time >= readyAt;
        }

        /// <summary>这个球是否正在执行合体吸附。</summary>
        public bool IsMerging(SizeController ball) => ball != null && mergingBalls.Contains(ball);

        /// <summary>距离可合体还剩多少秒（给 UI 倒计时用）。</summary>
        public float MergeCooldownRemaining(SizeController ball)
        {
            if (ball == null) return 0f;
            return mergeReadyAt.TryGetValue(ball, out float readyAt) ? Mathf.Max(0f, readyAt - Time.time) : 0f;
        }

        /// <summary>两两检测玩家球是否重叠；重叠且都过了冷却就开始合体。</summary>
        private void DetectMerges()
        {
            if (roster.Count < 2) return;
            if (mergeJobs.Count > 0) return;    // 一次只处理一对，避免连锁抖动

            for (int i = 0; i < roster.Count; i++)
            {
                var a = roster.Balls[i];
                if (a == null || a.IsDead || !CanMerge(a)) continue;

                for (int j = i + 1; j < roster.Count; j++)
                {
                    var b = roster.Balls[j];
                    if (b == null || b.IsDead || !CanMerge(b)) continue;

                    // 两个球都有实体碰撞体，物理上最多“贴住”，不会真的重叠。
                    // 所以判定用「接触」：中心距 <= 半径和 + 余量。冷却时间才是真正的门槛。
                    float contactDistance = a.Radius + b.Radius + mergeContactTolerance;
                    if (Vector2.Distance(a.transform.position, b.transform.position) > contactDistance) continue;

                    StartMerge(a, b);
                    return;
                }
            }
        }

        private void StartMerge(SizeController a, SizeController b)
        {
            // 保留方：主球永远保留（玩家控制的对象不能被自己的子球吃掉），否则留质量大的
            SizeController keeper;
            SizeController small;

            if (a == roster.Main) { keeper = a; small = b; }
            else if (b == roster.Main) { keeper = b; small = a; }
            else if (a.Mass >= b.Mass) { keeper = a; small = b; }
            else { keeper = b; small = a; }

            // 小球交出控制权：停掉弹簧跟随，进入吸附过渡
            var sub = small.GetComponent<PlayerSubBall>();
            if (sub != null) sub.enabled = false;

            // 吸附期间关掉它的碰撞体：否则它会一路把主球顶飞，也避免途中再触发吞噬/碰撞事件
            var smallColliders = small.GetComponentsInChildren<Collider2D>();
            for (int i = 0; i < smallColliders.Length; i++) smallColliders[i].enabled = false;

            mergingBalls.Add(small);
            mergeJobs.Add(new MergeJob
            {
                small = small,
                keeper = keeper,
                startPosition = small.transform.position,
                startScale = small.transform.localScale,
                elapsed = 0f
            });

            Debug.Log($"[合体] 开始吸附：{small.name}({small.Mass:F0}) → {keeper.name}({keeper.Mass:F0})");
        }

        private void UpdateMergeJobs()
        {
            if (mergeJobs.Count == 0) return;

            for (int i = mergeJobs.Count - 1; i >= 0; i--)
            {
                var job = mergeJobs[i];

                // 任何一方意外消失（例如被敌人吃了），放弃这次合体
                if (job.small == null || job.keeper == null || job.small.IsDead || job.keeper.IsDead)
                {
                    if (job.small != null) mergingBalls.Remove(job.small);
                    mergeJobs.RemoveAt(i);
                    continue;
                }

                job.elapsed += Time.fixedDeltaTime;
                float t = Mathf.Clamp01(job.elapsed / Mathf.Max(0.01f, mergePullDuration));
                float eased = Mathf.Pow(t, mergePullEase);   // 先慢后快，像被“吸”进去

                Vector2 position = Vector2.Lerp(job.startPosition, job.keeper.transform.position, eased);
                var rbSmall = job.small.GetComponent<Rigidbody2D>();
                if (rbSmall != null) rbSmall.MovePosition(position);

                // 视觉上“被吸进去”：小球同时收缩，避免到点突然消失
                job.small.transform.localScale = Vector3.Lerp(job.startScale, Vector3.zero, eased);

                if (t < 1f) continue;

                // 过渡结束：先退订任务，再结算质量 / 销毁，避免在事件回调里改列表
                mergeJobs.RemoveAt(i);

                float gain = job.small.Mass;
                float keeperMassBefore = job.keeper.Mass;

                job.keeper.AddMass(gain);
                job.small.Die(job.keeper);   // Die 会触发 Eaten -> OnBallEaten 做清理

                Debug.Log($"[合体] 完成：{keeperMassBefore:F0} + {gain:F0} → {job.keeper.Mass:F0}，" +
                          $"球数 {roster.Count}");
            }
        }

        // -------------------- 移动端 / UI 调用入口 --------------------

        /// <summary>虚拟摇杆调用：传入 [-1,1] 范围内的方向。</summary>
        public void SetVirtualMoveInput(Vector2 value)
        {
            playerInput.SetVirtualMoveInput(value);
        }

        /// <summary>分裂按钮调用（手机端 UI 或技能系统）。</summary>
        public void TriggerSplit() => TrySplit();

        /// <summary>吐球按钮调用（手机端 UI 或技能系统）。</summary>
        public void TriggerSpit() => TrySpit();

        // ------------------------------ 只读数据 ------------------------------

        public float MoveSpeed => moveSpeed;

        /// <summary>当前输入换算出的基础速度（子球会把它叠加到队形校正上）。</summary>
        public Vector2 InputVelocity
        {
            get
            {
                Vector2 input = Vector2.ClampMagnitude(moveInput, 1f);
                if (input.magnitude < inputDeadZone) return Vector2.zero;
                return input * moveSpeed;
            }
        }

        /// <summary>主球速度（兼容旧代码；没有主球时返回零）。</summary>
        public Vector2 CurrentVelocity
        {
            get
            {
                var rb = roster.Main != null ? roster.Main.GetComponent<Rigidbody2D>() : null;
                return rb != null ? rb.linearVelocity : Vector2.zero;
            }
        }

        /// <summary>当前主球 = 质量最大的存活球。</summary>
        public SizeController MainBall { get { return roster.Main; } }

        public int BallCount { get { return roster.Count; } }

        /// <summary>玩家当前所有存活的球（摄像机会用它算取景中心）。</summary>
        public IReadOnlyList<SizeController> Balls { get { return roster.Balls; } }

        /// <summary>玩家所有球的质量总和（做 UI 分数、大小排行榜时用）。</summary>
        public float TotalMass { get { return roster.TotalMass; } }
    }
}
