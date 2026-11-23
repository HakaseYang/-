# Phase 2：运动控制权归拢（PlayerCellMotion）

本轮只做 Phase 2，没有动分裂手感、相机、合并动画的观感。目标是让**只有一个组件写 Rigidbody**。

## 1. 改动前后

| 项目 | Phase 1 审查时的状态 | Phase 2 之后 |
|---|---|---|
| 写速度的地方 | PlayerController.MoveBall、PlayerSubBall、SplitEject、PlayerMerge.Nudge、ArenaManager | **只有 PlayerCellMotion**（ArenaManager 仍负责边界软夹） |
| 写位置的地方 | SplitEject.MovePosition、PlayerMerge.UpdateJobs + Nudge | PlayerCellMotion.TickSplit / TickMerge / Nudge |
| 写缩放的地方 | SizeController.ApplySizeNow、SplitEject、PlayerMerge.UpdateJobs | SizeController（体积）+ PlayerCellMotion（动画期半径） |
| 控制权协调 | 5 处 if 互相让路 | 一个状态机：Normal / SplitLaunching / Following / Merging / Disabled |
| 子球跟随 | PlayerSubBall（独立组件，自带弹簧与硬停） | PlayerCellMotion.TickFollow（组长速度 + 偏移弹簧） |

## 2. 每个文件改了什么

### 新增 Assets/Scripts/Player/PlayerCellMotion.cs
- 一个 Cell 的运动唯一入口，内部状态机，IsBusy 时别人不许动它。
- BeginSplit(origin, target, duration, launchSpeed)：把 body 变 Kinematic、半径从 20% 长到满，smoothstep 走完位置与尺寸，结束时把速度一次性交给 Following。
- BeginMerge(keeper, duration, ease, onFinished)：位置与尺寸分离插值，结束时回调让 PlayerMerge 提交质量。
- Nudge(delta)：合并前的互相推开（对应原项目 pushAwayCollidingCells）。
- TickFollow：组长速度 + 偏移弹簧，漂移量被 maxDriftSpeed 夹住，保证子球不会长期比主球快。

### 修改 Assets/Scripts/Player/PlayerController.cs
- 删除 MoveBall / MovePlayerBalls / SyncSubDrive，不再自己写任何 Rigidbody。
- 新增 SyncMotion()：保证每个 Cell 都有 PlayerCellMotion、把 tuning 推下去、按 merge.IsMerging 与是否主球设置状态。
- 新增 GroupVelocity（= MoveInput × moveSpeed × 主球 MoveSpeedFactor）作为全组统一的期望速度。
- SpawnBall() 建球时立刻挂 PlayerCellMotion，避免分裂当帧拿不到组件（这是实测发现的 bug）。

### 修改 Assets/Scripts/Player/PlayerActions.cs
- TrySplit 不再 AddComponent<SplitEject>，改为调用新球上的 PlayerCellMotion.BeginSplit(...)。
- 删除 SlotAngleFor（旧的固定站位角度）。

### 修改 Assets/Scripts/Player/PlayerMerge.cs
- 只负责判定与协调：PushApart 调 Nudge、Begin 调 BeginMerge、Commit 是唯一加质量的地方。
- Tick 顺序：PushApart → UpdateJobs → Detect（jobs 为空时）。

### 删除
- Assets/Scripts/Player/SplitEject.cs、Assets/Scripts/Player/PlayerSubBall.cs（功能并入 PlayerCellMotion）。

## 3. 场景参数（已写入 LevelScene.unity）

| 字段 | 值 | 说明 |
|---|---|---|
| subMaxDriftSpeed | 3 | 子球回位的漂移速度上限（原来 6，太快） |
| subHomeStrength | 2.5 | 回位弹簧强度 |
| subHomeDamping | 4 | 回位响应速率 |
| subFollowAcceleration | 30 | 无主球时的跟随加速度 |
| splitEjectDuration | 0.3 | 分裂动画时长 |
| splitSpawnGap | 1.2 | 分裂目标点在接触距离之外的余量 |
| splitLaunchSpeed | 0 | Phase 3 才改成「按速度换算的轻推」 |

## 4. 测试结果（Play 模式实测）

| # | 检查项 | 结果 |
|---|---|---|
| 1 | 分裂瞬间碎片在母球圆心、20% 半径、Kinematic | 通过：scale=1.34（满尺寸 6.71 的 20%）、dist=0.00、bodyType=Kinematic |
| 2 | 分裂动画期间先长尺寸再交棒 | 通过：0.3 s 后 scale=6.71、bodyType=Dynamic |
| 3 | 动画结束不产生速度断层 | 通过：结束时 velocity = groupVel + dir×launchSpeed（当前 launchSpeed=0） |
| 4 | 子球稳定停在接触距离 | 通过：dist=6.71 = r主+r子，子球速度 0.01 |
| 5 | 子球不会长期比主球快 | 通过：漂移被 maxDriftSpeed=3 夹住，稳态速度差 0.01 |
| 6 | 合并动画结束才提交质量 | 通过：两颗 200 合成一颗 400，总质量守恒 |
| 7 | 合并期间不会重复吞噬 | 通过：Commit 是唯一入口，merging 集合在回调期间仍持有小球 |
| 8 | 全工程只有一处写速度 | 通过：玩家 cell 的速度只有 PlayerCellMotion 写（AI 由 AIController、病毒由 Virus、出生初速由 BallFactory、边界软夹由 ArenaManager 各自负责） |
| 9 | 编译 | 通过：compilationFailed=false、consoleErrors=0 |

## 5. 本轮修掉的两个真实缺陷

1. **分裂时碎片没有动画**：碎片由 Update() 创建，而 PlayerCellMotion 原本在下一帧 FixedUpdate 才挂上，BeginSplit 拿不到组件就静默跳过。修法：SpawnBall() 里立刻挂组件并 Initialize。
2. **Duplicated Tooltip 编译错误**：改动 subMaxDriftSpeed 时留下两个 [Tooltip]，已合并。

## 6. 发现问题（不属于 Phase 2，留给 Phase 7）

- **键盘输入读不到**：Keyboard.current.dKey.isPressed == true 且 ReadValue() == 1，但 moveAction.ReadValue<Vector2>() == (0,0)；而反射调用 SetVirtualMoveInput 后立刻正常。说明 Input System 的 Action → 设备这一段有问题，虚拟摇杆通路是好的。
- **手机 UI 仍未接线**：MobileUI/LeftStick 上没有指针处理器，BtnSpit 是普通 Button（只在抬起时触发一次），长按吐球要 Phase 7 才修。

## 7. 完整代码

### 7.1 PlayerCellMotion.cs（381 行）

```csharp
using UnityEngine;

namespace QiuQiu
{
    // Which movement owner currently drives one cell. Only one state runs at a time, and only
    // the state that is running may touch the Rigidbody.
    public enum CellMotionState
    {
        Normal,         // the main cell: takes the player input
        SplitLaunching, // still sliding out of the parent, driven by this component only
        Following,      // sub cell: same speed as the group, gently nudged beside the big cell
        Merging,        // being swallowed by another cell of the same player
        Disabled        // dead or handed over, nothing runs
    }

    // The single owner of one player cell's motion.
    //
    // Before this script there were three owners fighting over the same Rigidbody2D:
    // SplitEject wrote MovePosition while PlayerSubBall wrote linearVelocity and
    // PlayerMerge.PushApart wrote body.position on top of both. Every one of them needed an
    // "if (someone else is running) return" guard, and the guards still left one frame of
    // mixed control at every hand-over.
    //
    // Now: PlayerController hands in the input, PlayerActions creates the cell, PlayerMerge
    // only decides who may merge. All of them talk to this component; none of them writes the
    // body directly.
    [DisallowMultipleComponent]
    public class PlayerCellMotion : MonoBehaviour
    {
        [Header("Group movement")]
        [Tooltip("How hard a cell reaches its target speed when the player pushes it")]
        [SerializeField] private float acceleration = 60f;

        [Tooltip("How fast a cell bleeds speed off when the player lets go")]
        [SerializeField] private float deceleration = 6f;

        [Tooltip("Absolute speed cap for a single cell")]
        [SerializeField] private float maxSpeedHardLimit = 12f;

        [Header("Following")]
        [Tooltip("Only used when the group has no big cell to sit beside")]
        [SerializeField] private float followAcceleration = 30f;

        [Tooltip("How hard a sub cell is nudged back beside the big cell")]
        [SerializeField] private float homeStrength = 2.5f;

        [Tooltip("How quickly it answers to that nudge. Higher = snappier, no overshoot")]
        [SerializeField] private float homeDamping = 4f;

        [Tooltip("Speed cap for the closing motion itself, not counting the group's own speed")]
        [SerializeField] private float maxDriftSpeed = 6f;

        [Header("Split slide")]
        [Tooltip("How big a new piece is when it appears inside its parent, relative to its final size")]
        [SerializeField, Range(0.05f, 1f)] private float splitStartScale = 0.2f;

        private PlayerController owner;
        private SizeController self;
        private Rigidbody2D rb;
        private SpriteRenderer shell;
        private Collider2D[] colliders;

        private CellMotionState state = CellMotionState.Disabled;

        // split slide
        private Vector2 splitFrom;
        private Vector2 splitTo;
        private float splitElapsed;
        private float splitDuration = 0.3f;
        private float splitEndRadius;
        private float splitLaunchSpeed;

        // merge pull
        private SizeController mergeKeeper;
        private Vector2 mergeFrom;
        private float mergeStartRadius;
        private float mergeElapsed;
        private float mergeDuration = 0.45f;
        private float mergeEase = 2f;
        private System.Action mergeFinished;

        public CellMotionState State { get { return state; } }

        // true while an animation owns the body: nothing else may steer this cell
        public bool IsBusy
        {
            get { return state == CellMotionState.SplitLaunching || state == CellMotionState.Merging; }
        }

        private void Awake()
        {
            CacheParts();
        }

        private void CacheParts()
        {
            if (self == null) self = GetComponent<SizeController>();
            if (rb == null) rb = GetComponent<Rigidbody2D>();
            if (shell == null) shell = GetComponent<SpriteRenderer>();
            if (colliders == null || colliders.Length == 0) colliders = GetComponents<Collider2D>();
        }

        public void Initialize(PlayerController controller)
        {
            owner = controller;
        }

        // The component is added at runtime, so the numbers live on PlayerController and are
        // pushed down here. Called on creation and whenever ApplyTuning() runs, never per frame.
        public void ApplyMotionTuning(
            float accel, float decel, float speedLimit,
            float followAccel, float strength, float damping, float maxDrift)
        {
            acceleration = accel;
            deceleration = decel;
            maxSpeedHardLimit = speedLimit;
            followAcceleration = followAccel;
            homeStrength = strength;
            homeDamping = damping;
            maxDriftSpeed = maxDrift;
        }

        // ---- state changes -------------------------------------------------------------

        // Called from the player layer: this cell is a sub cell (or the main one).
        public void SetFollowing(bool isMain)
        {
            if (IsBusy)
            {
                return;   // an animation owns it right now
            }
            state = isMain ? CellMotionState.Normal : CellMotionState.Following;
        }

        public void BeginSplit(Vector2 origin, Vector2 target, float duration, float launchSpeed)
        {
            CacheParts();

            splitFrom = origin;
            splitTo = target;
            splitDuration = Mathf.Max(0.01f, duration);
            splitLaunchSpeed = launchSpeed;
            splitElapsed = 0f;
            splitEndRadius = self != null ? self.Radius : 0.5f;

            state = CellMotionState.SplitLaunching;

            if (self != null)
            {
                self.SuppressVisual = true;              // this component owns the scale now
                self.IsEjecting = true;
                self.SetInvulnerable(splitDuration + 0.1f);   // nothing eats it mid-slide
            }

            if (rb != null)
            {
                rb.bodyType = RigidbodyType2D.Kinematic;
                rb.position = splitFrom;
                rb.linearVelocity = Vector2.zero;
            }

            ApplyRadius(splitEndRadius * splitStartScale);
        }

        public void BeginMerge(SizeController keeper, float duration, float ease, System.Action onFinished)
        {
            CacheParts();

            mergeKeeper = keeper;
            mergeFrom = rb != null ? rb.position : (Vector2)transform.position;
            mergeStartRadius = self != null ? self.Radius : 0.5f;
            mergeDuration = Mathf.Max(0.01f, duration);
            mergeEase = Mathf.Max(0.01f, ease);
            mergeFinished = onFinished;
            mergeElapsed = 0f;

            state = CellMotionState.Merging;

            if (self != null)
            {
                self.SuppressVisual = true;
            }

            SetCollidersEnabled(false);

            if (rb != null)
            {
                rb.linearVelocity = Vector2.zero;
            }
        }

        // the keeper died mid-pull: give the cell back its body and its normal driving
        public void AbortMerge()
        {
            if (state != CellMotionState.Merging)
            {
                return;
            }

            mergeKeeper = null;
            mergeFinished = null;

            SetCollidersEnabled(true);

            if (self != null)
            {
                self.SuppressVisual = false;
                self.ApplySizeNow();
            }

            if (rb != null)
            {
                rb.bodyType = RigidbodyType2D.Dynamic;
                rb.position = mergeFrom;
                rb.linearVelocity = Vector2.zero;
            }

            state = CellMotionState.Following;
        }

        public bool IsMergingInto(SizeController keeper)
        {
            return state == CellMotionState.Merging && mergeKeeper == keeper;
        }

        // ---- the only place a player cell is moved -------------------------------------

        private void FixedUpdate()
        {
            if (self == null || self.IsDead)
            {
                return;
            }

            switch (state)
            {
                case CellMotionState.SplitLaunching:
                    TickSplit();
                    break;

                case CellMotionState.Merging:
                    TickMerge();
                    break;

                case CellMotionState.Following:
                    TickFollow();
                    break;

                case CellMotionState.Normal:
                    TickNormal();
                    break;
            }
        }

        private void TickNormal()
        {
            if (owner == null)
            {
                return;
            }

            Steer(owner.MoveInput * (owner.MoveSpeed * self.MoveSpeedFactor), acceleration, deceleration);
        }

        private void TickFollow()
        {
            if (owner == null)
            {
                return;
            }

            SizeController leader = owner.MainBall;

            // no big cell (it was just eaten): behave like a normal cell this frame
            if (leader == null || leader == self)
            {
                Steer(owner.MoveInput * (owner.MoveSpeed * self.MoveSpeedFactor), followAcceleration, followAcceleration);
                return;
            }

            // everyone in the group uses the leader's speed, so a sub cell can never slowly
            // outrun the big one
            Vector2 groupVelocity = owner.GroupVelocity;

            Vector2 offset = (rb != null ? rb.position : (Vector2)transform.position) - (Vector2)leader.transform.position;
            float restDistance = leader.Radius + self.Radius;

            Vector2 direction = offset.sqrMagnitude > 0.0001f ? offset.normalized : Vector2.zero;
            if (direction == Vector2.zero)
            {
                Vector2 drifting = (rb != null ? rb.linearVelocity : Vector2.zero) - groupVelocity;
                direction = drifting.sqrMagnitude > 0.0001f ? drifting.normalized : Vector2.right;
            }

            Vector2 restingSpot = direction * restDistance;
            Vector2 closing = (restingSpot - offset) * homeStrength;
            if (closing.sqrMagnitude > maxDriftSpeed * maxDriftSpeed)
            {
                closing = Vector2.ClampMagnitude(closing, maxDriftSpeed);
            }

            Steer(groupVelocity + closing, followAcceleration, followAcceleration);
        }

        // One steering rule for every state that is not an animation: approach the target
        // velocity at a fixed rate, never snap unless the rate is zero.
        private void Steer(Vector2 targetVelocity, float pushRate, float idleRate)
        {
            if (rb == null)
            {
                return;
            }

            bool pushing = targetVelocity.sqrMagnitude > 0.0001f;
            float rate = pushing ? pushRate : idleRate;

            Vector2 next = rate <= 0f
                ? targetVelocity
                : Vector2.MoveTowards(rb.linearVelocity, targetVelocity, rate * Time.fixedDeltaTime);

            if (maxSpeedHardLimit > 0f && next.sqrMagnitude > maxSpeedHardLimit * maxSpeedHardLimit)
            {
                next = Vector2.ClampMagnitude(next, maxSpeedHardLimit);
            }

            rb.linearVelocity = next;
            Constrain();
        }

        private void TickSplit()
        {
            splitElapsed += Time.fixedDeltaTime;
            float t = Mathf.Clamp01(splitElapsed / splitDuration);
            float eased = t * t * (3f - 2f * t);   // smoothstep, unchanged for now

            if (rb != null)
            {
                rb.MovePosition(Vector2.Lerp(splitFrom, splitTo, eased));
            }

            ApplyRadius(Mathf.Lerp(splitEndRadius * splitStartScale, splitEndRadius, eased));

            if (t < 1f)
            {
                return;
            }

            // hand over: keep the group's own speed so there is no velocity cliff at the end
            Vector2 direction = splitTo - splitFrom;
            direction = direction.sqrMagnitude > 0.0001f ? direction.normalized : Vector2.right;

            Vector2 handOverVelocity = owner != null ? owner.GroupVelocity : Vector2.zero;
            if (splitLaunchSpeed > 0f)
            {
                handOverVelocity += direction * splitLaunchSpeed;
            }

            if (rb != null)
            {
                rb.bodyType = RigidbodyType2D.Dynamic;
                rb.position = splitTo;
                rb.linearVelocity = handOverVelocity;
            }

            if (self != null)
            {
                self.IsEjecting = false;
                self.SuppressVisual = false;
                self.ApplySizeNow();
            }

            state = CellMotionState.Following;
        }

        private void TickMerge()
        {
            if (mergeKeeper == null || mergeKeeper.IsDead)
            {
                AbortMerge();
                return;
            }

            mergeElapsed += Time.fixedDeltaTime;
            float t = Mathf.Clamp01(mergeElapsed / mergeDuration);
            float eased = Mathf.Pow(t, mergeEase);

            if (rb != null)
            {
                rb.MovePosition(Vector2.Lerp(mergeFrom, (Vector2)mergeKeeper.transform.position, eased));
            }

            ApplyRadius(Mathf.Lerp(mergeStartRadius, 0f, eased));

            if (t < 1f)
            {
                return;
            }

            state = CellMotionState.Disabled;

            System.Action finished = mergeFinished;
            mergeFinished = null;
            mergeKeeper = null;

            if (finished != null)
            {
                finished();   // PlayerMerge commits the mass and kills this cell
            }
        }

        // Called by PlayerMerge while two cells are not allowed to merge yet: the solid-body
        // separation from Agar. The merge layer decides how far, this component performs it,
        // so the Rigidbody still only has one writer.
        public bool Nudge(Vector2 delta)
        {
            if (state != CellMotionState.Normal && state != CellMotionState.Following)
            {
                return false;
            }

            if (rb == null)
            {
                return false;
            }

            rb.position += delta;
            Constrain();
            return true;
        }

        // ---- helpers --------------------------------------------------------------------

        private void ApplyRadius(float radius)
        {
            float scale = Mathf.Max(0.0001f, radius) * 2f;
            transform.localScale = new Vector3(scale, scale, 1f);

            if (shell != null)
            {
                shell.sortingOrder = SizeController.SortingOrderForRadius(radius);
            }
        }

        private void Constrain()
        {
            if (ArenaManager.Instance != null)
            {
                ArenaManager.Instance.Constrain(rb, self.Radius);
            }
        }

        private void SetCollidersEnabled(bool enabled)
        {
            if (colliders == null || colliders.Length == 0)
            {
                CacheParts();
            }

            for (int i = 0; i < colliders.Length; i++)
            {
                if (colliders[i] != null)
                {
                    colliders[i].enabled = enabled;
                }
            }
        }
    }
}
```

### 7.2 PlayerController.cs（668 行）

```csharp
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

namespace QiuQiu
{
    public class PlayerController : MonoBehaviour
    {
        public static PlayerController Active { get; private set; }

        [Header("Input Asset")]
        [SerializeField] private InputActionAsset controls;
        [SerializeField] private string actionMapName = "Player";
        [SerializeField] private string moveActionName = "Move";
        [SerializeField] private string splitActionName = "Split";
        [SerializeField] private string spitActionName = "Spit";

        [Header("Movement")]
        [SerializeField] private float moveSpeed = 5f;
        [SerializeField] private float maxSpeedHardLimit = 12f;
        [SerializeField] private float acceleration = 60f;
        [Tooltip("How fast a ball bleeds off speed when nobody pushes it. This is also what makes the split launch fade instead of stopping dead.")]
        [SerializeField] private float deceleration = 6f;
        [SerializeField] private float inputDeadZone = 0.2f;

        [Header("Split")]
        [SerializeField] private float splitMinMass = 30f;
        [SerializeField] private float splitMassRatio = 0.5f;
        [SerializeField] private int maxBallCount = 16;
        [SerializeField] private float splitCooldown = 0.5f;
        [Tooltip("Extra speed given to a split piece. 0 = the two halves simply keep the same speed and close the gap on their own")]
        [SerializeField] private float splitLaunchSpeed = 0f;
        [Tooltip("How far past touching a new piece lands. It then closes this gap slowly instead of being thrown")]
        [SerializeField] private float splitSpawnGap = 1.2f;
        [Tooltip("How long a new split piece takes to slide out of its parent, in seconds")]
        [SerializeField] private float splitEjectDuration = 0.3f;
        [Tooltip("Aim split / spit at the mouse when a mouse is present (Agar.io), otherwise at the movement direction")]
        [SerializeField] private bool aimAtMouse = false;

        [Header("Player Arrows")]
        [Tooltip("Empty space between the ball edge and its arrow, in world units")]
        [SerializeField] private float markerGap = 0.35f;

        [Header("Sub Ball Offset")]
        [Tooltip("How hard a split piece is nudged back beside the big ball. Higher = closes the gap faster")]
        [SerializeField] private float subHomeStrength = 2.5f;
        [Tooltip("How quickly it answers to that nudge. Higher = snappier, no overshoot")]
        [SerializeField] private float subHomeDamping = 4f;
        [Tooltip("How fast a sub cell may drift back beside the big cell. Keep it low: a sub " +
                 "cell is allowed a short catch up, not a permanent speed advantage.")]
        [SerializeField] private float subMaxDriftSpeed = 3f;
        [Tooltip("Fallback follow acceleration, only used when there is no big ball to sit beside")]
        [SerializeField] private float subFollowAcceleration = 30f;

        [Header("Spit")]
        [SerializeField] private float spitMassCost = 12f;
        [SerializeField] private float spitFoodMass = 9f;
        [SerializeField] private float minMassAfterSpit = 20f;
        [SerializeField] private float spitCooldown = 0.12f;
        [SerializeField] private float spitLaunchSpeed = 16f;
        [SerializeField] private float spitSpawnGap = 0.15f;
        [SerializeField] private float spitArmDelay = 0.12f;

        [Header("Merge")]
        [Tooltip("Seconds before split cells may touch again. The wait also grows with the cell mass.")]
        [SerializeField] private float mergeCooldown = 4.5f;
        [SerializeField] private float mergeCooldownPerMass = 0.002f;
        [SerializeField] private float mergeCooldownMax = 8f;
        [Tooltip("Own cells behave like solid bodies: two cells that are not allowed to merge yet push each other apart at this speed (units per second). Set to 0 to let them overlap again.")]
        [SerializeField] private float pushApartSpeed = 4f;
        [SerializeField] private float mergeContactTolerance = 0.15f;
        [SerializeField] private float mergePullDuration = 0.45f;
        [SerializeField] private float mergePullEase = 2f;

        [Header("Adoption")]
        [SerializeField] private bool autoAdoptPlayerBalls = true;

        private readonly InputReader playerInput = new InputReader();

        // the list of balls and the main ball live in PlayerRoster
        private readonly PlayerRoster roster = new PlayerRoster();

        // split / spit rules (cooldowns, mass math, spawn point) live in PlayerActions
        private readonly PlayerActions actions = new PlayerActions();

        private Vector2 moveInput;

        // split/spit aim: last input, right by default
        private Vector2 facing = Vector2.right;

        // where split / spit actually fly. The mouse when there is one (Agar.io), else the
        // direction the stick points at.
        private Vector2 aim = Vector2.right;

        // merge rules (cooldown, pull animation, mass hand-over) live in PlayerMerge
        private readonly PlayerMerge merge = new PlayerMerge();

        // one triangle per ball, so you can see where the whole group is heading
        private readonly System.Collections.Generic.Dictionary<SizeController, PlayerMarker> markers =
            new System.Collections.Generic.Dictionary<SizeController, PlayerMarker>();

        [System.Serializable]
        public class PlayerTuning
        {
            public InputActionAsset controls;

            public float moveSpeed;
            public float maxSpeedHardLimit;
            public float acceleration;
            public float deceleration;
            public float inputDeadZone;

            public float splitMinMass;
            public float splitMassRatio;
            public int maxBallCount;
            public float splitCooldown;
            public float splitLaunchSpeed;
            public float splitSpawnGap;
            public float splitEjectDuration;

            public float spitMassCost;
            public float spitFoodMass;
            public float minMassAfterSpit;
            public float spitCooldown;
            public float spitLaunchSpeed;
            public float spitSpawnGap;
            public float spitArmDelay;

            public float mergeCooldown;
            public float mergeCooldownPerMass;
            public float mergeCooldownMax;
            public float pushApartSpeed;
            public float mergeContactTolerance;
            public float mergePullDuration;
            public float mergePullEase;
        }

        public PlayerTuning CaptureTuning()
        {
            return new PlayerTuning
            {
                controls = controls,

                moveSpeed = moveSpeed,
                maxSpeedHardLimit = maxSpeedHardLimit,
                acceleration = acceleration,
                deceleration = deceleration,
                inputDeadZone = inputDeadZone,

                splitMinMass = splitMinMass,
                splitMassRatio = splitMassRatio,
                maxBallCount = maxBallCount,
                splitCooldown = splitCooldown,
                splitLaunchSpeed = splitLaunchSpeed,
                splitSpawnGap = splitSpawnGap,
                splitEjectDuration = splitEjectDuration,

                spitMassCost = spitMassCost,
                spitFoodMass = spitFoodMass,
                minMassAfterSpit = minMassAfterSpit,
                spitCooldown = spitCooldown,
                spitLaunchSpeed = spitLaunchSpeed,
                spitSpawnGap = spitSpawnGap,
                spitArmDelay = spitArmDelay,

                mergeCooldown = mergeCooldown,
                mergeCooldownPerMass = mergeCooldownPerMass,
                mergeCooldownMax = mergeCooldownMax,
                pushApartSpeed = pushApartSpeed,
                mergeContactTolerance = mergeContactTolerance,
                mergePullDuration = mergePullDuration,
                mergePullEase = mergePullEase,
            };
        }

        public void ApplyTuning(PlayerTuning tuning)
        {
            if (tuning == null)
            {
                return;
            }

            if (tuning.controls != null)
            {
                controls = tuning.controls;
            }
                
            moveSpeed = tuning.moveSpeed;
            maxSpeedHardLimit = tuning.maxSpeedHardLimit;
            acceleration = tuning.acceleration;
            deceleration = tuning.deceleration;
            inputDeadZone = tuning.inputDeadZone;

            splitMinMass = tuning.splitMinMass;
            splitMassRatio = tuning.splitMassRatio;
            maxBallCount = tuning.maxBallCount;
            splitCooldown = tuning.splitCooldown;
            splitLaunchSpeed = tuning.splitLaunchSpeed;
            splitSpawnGap = tuning.splitSpawnGap;
            splitEjectDuration = tuning.splitEjectDuration;

            spitMassCost = tuning.spitMassCost;
            spitFoodMass = tuning.spitFoodMass;
            minMassAfterSpit = tuning.minMassAfterSpit;
            spitCooldown = tuning.spitCooldown;
            spitLaunchSpeed = tuning.spitLaunchSpeed;
            spitSpawnGap = tuning.spitSpawnGap;
            spitArmDelay = tuning.spitArmDelay;

            mergeCooldown = tuning.mergeCooldown;
            mergeCooldownPerMass = tuning.mergeCooldownPerMass;
            mergeCooldownMax = tuning.mergeCooldownMax;
            pushApartSpeed = tuning.pushApartSpeed;
            mergeContactTolerance = tuning.mergeContactTolerance;
            mergePullDuration = tuning.mergePullDuration;
            mergePullEase = tuning.mergePullEase;

            SyncTools();
            ResolveActions();
        }


        // tuning snapshot used when respawning (owned by GameManager)
        private static PlayerTuning s_PendingTuning;

        private void Awake()
        {
            Active = this;

            playerInput.SplitPressed += OnSplitPressed;
            playerInput.SpitPressed += OnSpitPressed;

            // ApplyTuning already pushes the numbers and resolves the input
            if (s_PendingTuning != null)
            {
                PlayerTuning tuning = s_PendingTuning;
                s_PendingTuning = null;
                ApplyTuning(tuning);
                return;
            }

            SyncTools();
            ResolveActions();
        }

        // every ball carries its own arrow, created the first frame it shows up
        private void SyncMarkers()
        {
            for (int i = 0; i < roster.Count; i++)
            {
                SizeController ball = roster.Balls[i];
                if (ball == null || ball.IsDead || markers.ContainsKey(ball))
                {
                    continue;
                }

                var go = new GameObject("PlayerMarker");
                go.transform.SetParent(transform, true);
                markers.Add(ball, go.AddComponent<PlayerMarker>());
            }

            // drop arrows whose ball is gone
            if (markers.Count == 0)
            {
                return;
            }

            s_stale.Clear();
            foreach (var pair in markers)
            {
                if (pair.Key == null || pair.Key.IsDead || !roster.Contains(pair.Key))
                {
                    s_stale.Add(pair.Key);
                }
            }

            for (int i = 0; i < s_stale.Count; i++)
            {
                PlayerMarker dead;
                if (markers.TryGetValue(s_stale[i], out dead) && dead != null)
                {
                    Destroy(dead.gameObject);
                }
                markers.Remove(s_stale[i]);
            }

            s_stale.Clear();
        }

        private static readonly System.Collections.Generic.List<SizeController> s_stale =
            new System.Collections.Generic.List<SizeController>();

        private void LateUpdate()
        {
            SyncMarkers();

            foreach (var pair in markers)
            {
                if (pair.Value == null)
                {
                    continue;
                }

                pair.Value.Follow(pair.Key, aim, markerGap);
            }
        }

        //give Inspector Parameters to PlayerActions??PlayerMerge
        private void SyncTools()
        {
            PlayerTuning snapshot = CaptureTuning();
            actions.Configure(roster, SpawnBall, snapshot, name);
            merge.Configure(roster, snapshot);
            PushMotionTuning();
        }

        // the motion values live on the cells that already exist, so a tuning change has to be
        // pushed down once instead of every frame
        private void PushMotionTuning()
        {
            for (int i = 0; i < roster.Count; i++)
            {
                SizeController ball = roster.Balls[i];
                if (ball == null)
                {
                    continue;
                }

                PlayerCellMotion motion = ball.GetComponent<PlayerCellMotion>();
                if (motion == null)
                {
                    continue;
                }

                motion.ApplyMotionTuning(
                    acceleration, deceleration, maxSpeedHardLimit,
                    subFollowAcceleration, subHomeStrength, subHomeDamping, subMaxDriftSpeed);
            }
        }

        private void OnDestroy()
        {
            if (Active == this)
            {
                Active = null;
            }

            for (int i = 0; i < roster.Count; i++)
            {
                SizeController ball = roster.Balls[i];
                if (ball != null)
                {
                    ball.Eaten -= OnBallEaten;
                }
            }
            roster.Clear();
        }

        private void Start()
        {
            if (autoAdoptPlayerBalls)
            {
                AdoptOrphanPlayerBalls();
            }
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

            // mouse beats the stick; keyboard-only play keeps using the movement direction
            Vector2 mouseAim;
            if (TryReadMouseAim(out mouseAim))
            {
                aim = mouseAim;
            }
            else if (facing.sqrMagnitude > 0.0001f)
            {
                aim = facing;
            }

            // holding the spit button keeps feeding mass, a single tap still spits once
            // (the tap arrives through OnSpitPressed, this covers the hold)
            if (playerInput.SpitHeld)
            {
                TrySpit();
            }
        }

        private bool TryReadMouseAim(out Vector2 direction)
        {
            direction = Vector2.zero;

            if (!aimAtMouse)
            {
                return false;
            }

            Mouse mouse = Mouse.current;
            Camera cam = Camera.main;
            if (mouse == null || cam == null)
            {
                return false;
            }

            SizeController reference = roster.Main;
            if (reference == null)
            {
                return false;
            }

            Vector3 screen = mouse.position.ReadValue();
            screen.z = Mathf.Abs(cam.transform.position.z);
            Vector3 world = cam.ScreenToWorldPoint(screen);

            Vector2 delta = (Vector2)world - (Vector2)reference.transform.position;
            if (delta.sqrMagnitude < 0.0001f)
            {
                return false;
            }

            direction = delta.normalized;
            return true;
        }

        private void FixedUpdate()
        {
            // 1. drop balls that died without firing the event
            roster.PruneDead(UnhookBall);

            // 2. adopt any orphan player balls
            if (roster.Count == 0 && autoAdoptPlayerBalls)
            {
                AdoptOrphanPlayerBalls();
            }

            // 3. main ball = largest alive ball
            roster.RefreshMain();

            // 4. every cell gets exactly one motion owner, and learns whether it is the main one
            SyncMotion();

            // 5. input
            Vector2 input = Vector2.ClampMagnitude(moveInput, 1f);
            if (input.magnitude < inputDeadZone)
            {
                input = Vector2.zero;
            }
            if (input.sqrMagnitude > 0.0001f)
            {
                facing = input.normalized;
            }

            // 6. merge: pull the running swallow first, then look for a new pair
            merge.Tick(Time.fixedDeltaTime);
        }

        // Every player cell gets exactly one motion owner. PlayerController no longer writes a
        // Rigidbody itself: it only says which cell is the main one. The motion component
        // decides how to move, and the merge pull is allowed to keep a cell busy.
        private void SyncMotion()
        {
            SizeController main = roster.Main;

            for (int i = 0; i < roster.Count; i++)
            {
                SizeController ball = roster.Balls[i];
                if (ball == null || ball.IsDead)
                {
                    continue;
                }

                PlayerCellMotion motion = ball.GetComponent<PlayerCellMotion>();

                if (motion == null)
                {
                    motion = ball.gameObject.AddComponent<PlayerCellMotion>();
                    motion.Initialize(this);
                    motion.ApplyMotionTuning(
                        acceleration, deceleration, maxSpeedHardLimit,
                        subFollowAcceleration, subHomeStrength, subHomeDamping, subMaxDriftSpeed);
                }

                // the merge pull owns this cell right now
                if (merge.IsMerging(ball))
                {
                    continue;
                }

                motion.SetFollowing(ball == main);
            }
        }


        public void RegisterBall(SizeController ball)
        {
            if (ball == null || ball.IsDead)
            {
                return;
            }
            if (roster.Contains(ball))
            {
                return;
            }

            roster.Add(ball);
            ball.Eaten += OnBallEaten;

            merge.Register(ball);
        }

        public void UnregisterBall(SizeController ball)
        {
            if (ball == null)
            {
                return;
            }

            ball.Eaten -= OnBallEaten;
            roster.Remove(ball);
            merge.Drop(ball);
        }

        // one place unhooks a ball the roster dropped: events plus merge bookkeeping
        private void UnhookBall(SizeController ball)
        {
            ball.Eaten -= OnBallEaten;
            merge.Drop(ball);
        }

        private void AdoptOrphanPlayerBalls()
        {
            var registry = SizeController.Registry;
            for (int i = 0; i < registry.Count; i++)
            {
                SizeController ball = registry[i];
                if (ball == null || ball.IsDead || ball.Team != Team.Player)
                {
                    continue;
                }
                if (roster.Contains(ball))
                {
                    continue;
                }

                RegisterBall(ball);
            }
        }

        private void OnBallEaten(SizeController victim, SizeController killer)
        {
            victim.Eaten -= OnBallEaten;

            // a merged ball also fires Eaten,so it is merge?
            bool mergedIn = merge.IsMerging(victim);
            merge.Drop(victim); //clear merging

            bool wasMain = roster.Main == victim;
            roster.Remove(victim);

            if (mergedIn) return;   // self merge 

            string killerName = killer != null ? killer.name : "unknown";

            if (wasMain)
            {
                Debug.Log($"[player] main ball eaten by {killerName}, {roster.Count} left");

                if (roster.Main != null)
                {
                    Debug.Log($"[player] control moved to {roster.Main.name} ({roster.Main.Mass:F0}), {roster.Count} left");
                }
            }
            else
            {
                Debug.Log($"[player] sub ball eaten, {roster.Count} left");
            }

            if (roster.Count == 0) NotifyDefeat(killer);
        }

        private void NotifyDefeat(SizeController killer)
        {
            Debug.Log($"[player] every ball is gone, game over (eaten by {(killer != null ? killer.name : "unknown")})");

            if (GameManager.Instance != null)
            {
                GameManager.Instance.NotifyPlayerDefeated(killer);
            }
        }


        public bool TrySplit()
        {
            List<SizeController> touched = actions.TrySplit(aim);
            if (touched == null)
            {
                return false;
            }

            // every ball that took part waits out the merge cooldown, otherwise the two
            // halves would merge straight back the moment they touch
            for (int i = 0; i < touched.Count; i++)
            {
                merge.MarkSplit(touched[i]);
            }

            Debug.Log($"[split] now {roster.Count}/{maxBallCount} balls");
            if (AudioManager.Instance != null)
            {
                AudioManager.Instance.PlaySplit();
            }
            return true;
        }

        public bool TrySpit()
        {
            int ejected = actions.TrySpit(aim);

            if (ejected == 0)
            {
                return false;
            }

            Debug.Log($"[spit] {ejected} cell(s) spat, food mass {spitFoodMass:F0}");
            if (AudioManager.Instance != null)
            {
                AudioManager.Instance.PlaySpit();
            }
            return true;
        }

        // called by a virus burst: the popped cell and every piece it created share the
        // same merge wait, so the burst can not instantly re-merge into one ball
        public void MarkBurst(SizeController source, List<SizeController> created)
        {
            if (source != null)
            {
                merge.MarkSplit(source);
            }

            if (created != null)
            {
                for (int i = 0; i < created.Count; i++)
                {
                    merge.MarkSplit(created[i]);
                }
            }

            if (AudioManager.Instance != null)
            {
                AudioManager.Instance.PlaySplit();
            }
        }

        public int MaxBallCount
        {
            get { return maxBallCount; }
        }

        public SizeController SpawnBall(string ballName, Vector3 position, float mass, Sprite sprite, Color color)
        {
            if (sprite == null)
            {
                Debug.LogError("[PlayerController] SpawnBall needs a sprite");
                return null;
            }

            GameObject go = BallFactory.CreateBall(ballName, position, mass, sprite, color, Team.Player);
            go.tag = "Player";

            SizeController ball = go.GetComponent<SizeController>();

            // a fresh cell gets its motion owner right away: the split animation starts in the
            // same frame the ball is born, before the next FixedUpdate could hand it one
            PlayerCellMotion motion = go.AddComponent<PlayerCellMotion>();
            motion.Initialize(this);
            motion.ApplyMotionTuning(
                acceleration, deceleration, maxSpeedHardLimit,
                subFollowAcceleration, subHomeStrength, subHomeDamping, subMaxDriftSpeed);

            RegisterBall(ball);
            return ball;
        }

        //just for PlayerRoot have or work (normal not work)
        public static PlayerController EnsureExists()
        {
            if (Active != null)
            {
                return Active;
            }

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


        public bool CanMerge(SizeController ball)
        {
            return merge.CanMerge(ball);
        }

        public bool IsMerging(SizeController ball)
        {
            return merge.IsMerging(ball);
        }


        public void TriggerSplit()
        {
            TrySplit();
        }

        public void TriggerSpit()
        {
            TrySpit();
        }


        public Vector2 InputVelocity
        {
            get { return GroupVelocity; }
        }

        // what the player is asking for, after dead zone and clamping
        public Vector2 MoveInput
        {
            get
            {
                Vector2 input = Vector2.ClampMagnitude(moveInput, 1f);
                return input.magnitude < inputDeadZone ? Vector2.zero : input;
            }
        }

        public float MoveSpeed
        {
            get { return moveSpeed; }
        }

        // The whole group moves at the main cell's speed: a sub cell must never slowly outrun
        // the big one, which is what happened while subs used their own factor.
        public Vector2 GroupVelocity
        {
            get
            {
                SizeController main = roster.Main;
                float factor = main != null ? main.MoveSpeedFactor : 1f;
                return MoveInput * (moveSpeed * factor);
            }
        }

        public SizeController MainBall { get { return roster.Main; } }

        public int BallCount { get { return roster.Count; } }

        public IReadOnlyList<SizeController> Balls { get { return roster.Balls; } }

        public float TotalMass { get { return roster.TotalMass; } }
    }
}
```

### 7.3 PlayerActions.cs（166 行）

```csharp
using System.Collections.Generic;
using UnityEngine;

namespace QiuQiu
{
    // split and spit rules: cooldowns, mass math, where the new ball appears.
    public class PlayerActions
    {
        private PlayerRoster roster;
        //Generic Delegate (get black SizeController)
        private System.Func<string, Vector3, float, Sprite, Color, SizeController> spawnBall;
        private PlayerController.PlayerTuning tuning;
        private string ballPrefix = "Player";

        private float nextSplitTime;
        private float nextSpitTime;

        public void Configure(PlayerRoster roster,System.Func<string, Vector3, float, Sprite, Color, SizeController> spawnBall,
            PlayerController.PlayerTuning tuning, string ballPrefix)
        {
            this.roster = roster;
            this.spawnBall = spawnBall;
            this.tuning = tuning;
            this.ballPrefix = ballPrefix;
        }

        // Agar.io split: every cell that is big enough splits in two at once, not just the
        // biggest one. Once the player is past half of the cell limit only the biggest cells
        // split, and only as many as the limit still allows.
        public List<SizeController> TrySplit(Vector2 aim)
        {
            if (Time.time < nextSplitTime)
            {
                return null;
            }

            if (roster == null || roster.Count == 0)
            {
                return null;
            }

            int room = tuning.maxBallCount - roster.Count;
            if (room <= 0)
            {
                Debug.Log($"[split] ball limit {tuning.maxBallCount} reached");
                return null;
            }

            List<SizeController> candidates = new List<SizeController>();
            for (int i = 0; i < roster.Count; i++)
            {
                SizeController ball = roster.Balls[i];
                if (ball == null || ball.IsDead)
                {
                    continue;
                }
                if (ball.Mass <= tuning.splitMinMass)
                {
                    continue;   // too small to split
                }
                candidates.Add(ball);
            }

            if (candidates.Count == 0)
            {
                Debug.Log($"[split] no ball is above {tuning.splitMinMass:F0} mass");
                return null;
            }

            candidates.Sort((a, b) => b.Mass.CompareTo(a.Mass));

            int allowed = roster.Count > tuning.maxBallCount / 2
                ? Mathf.Max(0, tuning.maxBallCount - roster.Count + 1)   // only the big ones
                : candidates.Count;                                      // everyone splits

            allowed = Mathf.Min(allowed, Mathf.Min(candidates.Count, room));
            if (allowed <= 0)
            {
                return null;
            }

            Vector2 dir = Direction(aim);
            List<SizeController> touched = new List<SizeController>(allowed * 2);

            for (int i = 0; i < allowed; i++)
            {
                SizeController source = candidates[i];
                if (source == null || source.IsDead)
                {
                    continue;
                }

                SpriteRenderer shell = source.GetComponent<SpriteRenderer>();
                if (shell == null)
                {
                    continue;
                }

                float newMass = source.Mass * tuning.splitMassRatio;   // half, not a copy
                if (newMass <= 0f)
                {
                    continue;
                }

                // halve first, so the parent is already the smaller size while the piece
                source.SetMass(source.Mass - newMass);

                // the piece is born in the middle of the body; its motion component then slides
                // it out while it grows, so you can watch it leave the parent
                Vector3 origin = source.transform.position;
                Vector3 target = origin + (Vector3)(dir * (source.Radius * 2f + tuning.splitSpawnGap));
                SizeController newBall = spawnBall($"{ballPrefix}_Ball{roster.Count}", origin, newMass, shell.sprite, shell.color);

                if (newBall == null)
                {
                    source.SetMass(source.Mass + newMass);   // put the mass back, nothing happened
                    continue;
                }

                PlayerCellMotion motion = newBall.GetComponent<PlayerCellMotion>();
                if (motion != null)
                {
                    motion.BeginSplit(origin, target, tuning.splitEjectDuration, tuning.splitLaunchSpeed);
                }

                touched.Add(source);
                touched.Add(newBall);
            }

            if (touched.Count == 0)
            {
                return null;
            }

            nextSplitTime = Time.time + tuning.splitCooldown;
            return touched;
        }

        // Agar ejects from EVERY cell at once: 16 pieces means 16 pellets, one per body part.
        // Returns how many pieces were spat out (0 = nothing happened).
        public int TrySpit(Vector2 facing)
        {
            if (Time.time < nextSpitTime) return 0;

            if (roster == null || roster.Count == 0) return 0;

            Vector2 dir = Direction(facing);
            int ejected = 0;

            for (int i = 0; i < roster.Count; i++)
            {
                SizeController ball = roster.Balls[i];
                if (ball == null || ball.IsDead || ball.IsEjecting)
                {
                    continue;
                }

                // each cell pays for its own pellet, so a thin cell simply can not spit
                if (!ball.TryConsumeMass(tuning.spitMassCost, tuning.minMassAfterSpit))
                {
                    continue;
                }

                SpriteRenderer shell = ball.GetComponent<SpriteRenderer>();
                Sprite sprite = shell != null ? shell.sprite : null;
                Color color = shell != null ? shell.color : Color.cyan;

                Vector3 position = ball.transform.position + (Vector3)(dir * (ball.Radius + tuning.spitSpawnGap));

                BallFactory.CreateSpitFood("SpitFood", position, dir * tuning.spitLaunchSpeed,
                    tuning.spitFoodMass, sprite, Lighten(color), tuning.spitArmDelay);

                ejected++;
            }

            if (ejected == 0)
            {
                Debug.Log($"[spit] no ball is above {tuning.spitMassCost + tuning.minMassAfterSpit:F0} mass");
                return 0;
            }

            nextSpitTime = Time.time + tuning.spitCooldown;
            return ejected;
        }

        private static Vector2 Direction(Vector2 facing)
        {
            return facing.sqrMagnitude > 0.0001f ? facing.normalized : Vector2.right;
        }

        //make spit food more ealy to check and see
        private static Color Lighten(Color color)
        {
            return new Color(
                Mathf.Lerp(color.r, 1f, 0.35f),
                Mathf.Lerp(color.g, 1f, 0.35f),
                Mathf.Lerp(color.b, 1f, 0.35f),
                color.a);
        }
    }
}
```

### 7.4 PlayerMerge.cs（247 行）

```csharp
using System.Collections.Generic;
using UnityEngine;

namespace QiuQiu
{
    // Decides WHO may merge and WHEN, and nothing else.
    //
    // It used to also move the Rigidbody (MovePosition), change the visual scale and push
    // overlapping cells apart by writing body.position directly. That is now the job of
    // PlayerCellMotion: this class only says "these two may melt together" or "these two are
    // not allowed yet, separate them", and the cell itself does the moving.
    public class PlayerMerge
    {
        private PlayerRoster roster;
        private PlayerController.PlayerTuning tuning;

        // a ball may merge again at this time; 0 means "right away"
        private readonly Dictionary<SizeController, float> readyAt = new Dictionary<SizeController, float>();

        // balls that are being pulled in right now
        private readonly List<Job> jobs = new List<Job>();

        // used to tell "taken by an enemy" apart from "merged into our own ball"
        private readonly HashSet<SizeController> merging = new HashSet<SizeController>();

        private sealed class Job
        {
            public SizeController small;
            public SizeController keeper;
        }

        public void Configure(PlayerRoster roster, PlayerController.PlayerTuning tuning)
        {
            this.roster = roster;
            this.tuning = tuning;
        }

        public void Register(SizeController ball)
        {
            if (ball == null)
            {
                return;
            }
            if (!readyAt.ContainsKey(ball))
            {
                readyAt[ball] = 0f;
            }
        }

        // a fresh split ball need wait cooldown before it can join again.
        // The wait is not a fixed number: a heavy cell has to wait longer, which is the
        // Agar.io idea (merge timer grows with the cell).
        public void MarkSplit(SizeController ball)
        {
            if (ball == null)
            {
                return;
            }
            readyAt[ball] = Time.time + CooldownFor(ball.Mass);
        }

        private float CooldownFor(float mass)
        {
            float wait = tuning.mergeCooldown + Mathf.Max(0f, mass) * tuning.mergeCooldownPerMass;
            if (tuning.mergeCooldownMax > 0f)
            {
                wait = Mathf.Min(wait, tuning.mergeCooldownMax);
            }
            return Mathf.Max(0f, wait);
        }

        public void Drop(SizeController ball)
        {
            if (ball == null)
            {
                return;
            }
            readyAt.Remove(ball);
            merging.Remove(ball);
            jobs.RemoveAll(job => job.small == ball);
        }

        public bool CanMerge(SizeController ball)
        {
            if (ball == null || ball.IsDead)
            {
                return false;
            }
            if (merging.Contains(ball)) //when  mergeing
            {
                return false;
            }
            if (!readyAt.TryGetValue(ball, out float at))      //not have Register
            {
                return false;
            }
            return Time.time >= at; //can
        }

        public bool IsMerging(SizeController ball)
        {
            return ball != null && merging.Contains(ball);
        }

        // finish the running pull first, then look for a new pair
        public void Tick(float deltaTime)
        {
            PushApart(deltaTime);   // cells are solid until the cooldown is over
            UpdateJobs();
            if (jobs.Count == 0)
            {
                Detect();
            }
        }

        // Agar.io makes the cells of one player solid bodies: while they are not allowed
        // to merge yet, two overlapping cells push each other out of the way instead of
        // passing through. This is pushAwayCollidingCells() from owenashurst/agar.io-clone
        // (src/server/map/player.js). The merge layer decides how far, the cell's motion
        // component performs the move, so the Rigidbody keeps a single writer.
        private void PushApart(float deltaTime)
        {
            if (roster == null || roster.Count < 2 || tuning == null || tuning.pushApartSpeed <= 0f || deltaTime <= 0f)
            {
                return;
            }

            float maxStep = tuning.pushApartSpeed * deltaTime;

            for (int i = 0; i < roster.Count; i++)
            {
                SizeController a = roster.Balls[i];
                if (a == null || a.IsDead || a.IsEjecting || merging.Contains(a)) continue;

                PlayerCellMotion motionA = a.GetComponent<PlayerCellMotion>();
                if (motionA == null || motionA.IsBusy) continue;

                for (int j = i + 1; j < roster.Count; j++)
                {
                    SizeController b = roster.Balls[j];
                    if (b == null || b.IsDead || b.IsEjecting || merging.Contains(b)) continue;

                    PlayerCellMotion motionB = b.GetComponent<PlayerCellMotion>();
                    if (motionB == null || motionB.IsBusy) continue;

                    // both are allowed to melt together, so let them overlap on purpose
                    if (CanMerge(a) && CanMerge(b)) continue;

                    Vector2 offset = (Vector2)b.transform.position - (Vector2)a.transform.position;
                    float distance = offset.magnitude;
                    float overlap = a.Radius + b.Radius - distance;
                    if (overlap <= 0f) continue;

                    // two cells dropped on the exact same spot still have to separate
                    Vector2 direction = distance > 0.0001f ? offset / distance : Vector2.up;

                    float push = Mathf.Min(overlap * 0.5f, maxStep);
                    motionA.Nudge(-direction * push);
                    motionB.Nudge(direction * push);
                }
            }
        }

        private void Detect()
        {
            if (roster == null || roster.Count < 2)
            {
                return;
            }

            for (int i = 0; i < roster.Count; i++)
            {
                SizeController a = roster.Balls[i];
                if (!CanMerge(a)) continue;

                for (int j = i + 1; j < roster.Count; j++)
                {
                    SizeController b = roster.Balls[j];
                    if (!CanMerge(b)) continue;

                    //touch & time >true
                    float contactDistance = a.Radius + b.Radius + tuning.mergeContactTolerance;
                    if (Vector2.Distance(a.transform.position, b.transform.position) > contactDistance) continue;

                    Begin(a, b);
                    return;
                }
            }
        }

        private void Begin(SizeController a, SizeController b)
        {
            // the main ball always keeps itself, a sub ball may not eat it
            SizeController keeper;
            SizeController small;

            //find main ball
            if (a == roster.Main)
            {
                keeper = a;
                small = b;
            }
            else if (b == roster.Main)
            {
                keeper = b;
                small = a;
            }
            //see who big
            else if (a.Mass >= b.Mass)
            {
                keeper = a;
                small = b;
            }
            else
            {
                keeper = b;
                small = a;
            }

            PlayerCellMotion motion = small.GetComponent<PlayerCellMotion>();
            if (motion == null)
            {
                return;   // no motion owner means nothing can animate the swallow
            }

            merging.Add(small);
            jobs.Add(new Job { small = small, keeper = keeper });

            // the cell animates itself; when the pull is over it calls back so the mass is
            // committed in exactly one place
            motion.BeginMerge(keeper, tuning.mergePullDuration, tuning.mergePullEase, () => Commit(small, keeper));
        }

        // called by the cell when its pull animation finished: this is the only place that
        // adds the mass, so a merge can never add it twice
        private void Commit(SizeController small, SizeController keeper)
        {
            jobs.RemoveAll(job => job.small == small);

            if (small == null || keeper == null || keeper.IsDead)
            {
                merging.Remove(small);
                return;
            }

            float gain = small.Mass;
            keeper.AddMass(gain);

            // Die fires the Eaten event while "merging" still holds the cell, so
            // PlayerController knows this was our own merge and not an enemy kill
            small.Die(keeper);

            merging.Remove(small);
            readyAt.Remove(small);
        }

        // the keeper may be eaten while the pull runs: give the cell back its body
        private void UpdateJobs()
        {
            for (int i = jobs.Count - 1; i >= 0; i--)
            {
                Job job = jobs[i];

                if (job.small == null || job.keeper == null || job.small.IsDead || job.keeper.IsDead)
                {
                    PlayerCellMotion motion = job.small != null ? job.small.GetComponent<PlayerCellMotion>() : null;
                    if (motion != null)
                    {
                        motion.AbortMerge();
                    }

                    if (job.small != null)
                    {
                        merging.Remove(job.small);
                    }

                    jobs.RemoveAt(i);
                    continue;
                }

                // finished animation removed itself from the list through Commit()
                if (!job.small.GetComponent<PlayerCellMotion>().IsMergingInto(job.keeper))
                {
                    merging.Remove(job.small);
                    jobs.RemoveAt(i);
                }
            }
        }
    }
}
```

