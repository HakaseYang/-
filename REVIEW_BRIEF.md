# 代码审查说明：第三阶段（镜头拉远 / 轻度弹射 / 手机长按吐球 / AI 数量）

> 这份文档是给外部审查用的自包含说明。项目：Unity 6.3（`6000.3.5f2`）2D，Agar.io 类吞噬游戏，工程根目录 `E:\zm\FYP\ea\-`。
> 本轮只改机制，不动美术、UI 结构、场景层级。全部代码编译通过（0 error / 0 warning），并在 Play 模式实测过。

---

## 1. 现有架构（本轮必须遵守的约束）

| 脚本 | 职责 | 是否写 Rigidbody |
|---|---|---|
| `InputReader` | 读 Input System / 手机按钮，输出移动向量与"按住"状态 | 否 |
| `PlayerController` | 只读输入、算 aim、管理 roster、吐球/分裂入口 | **否**（第二阶段已把写入全部移走） |
| `PlayerActions` | 只建球、扣质量、算分裂距离 | 否 |
| `PlayerMerge` | 只做合球判定、互相推开、动画结束后提交质量 | 间接（通过 PlayerCellMotion） |
| **`PlayerCellMotion`** | **唯一的运动写入者**，状态机：`Normal / SplitLaunching / SplitCoasting / Following / Merging / Disabled` | **是** |
| `SizeController` | 唯一质量与半径来源，静态 `registry` 记录所有活着的球 | 只写 `localScale` 与 `rb.mass` |
| `CameraFollow` | 只读 cell 列表算镜头 | 否 |

设计意图：任何"某一帧球往哪走"的问题只可能来自 `PlayerCellMotion`（以及 `ArenaManager.Constrain` 的边界夹取）。这是上一轮重构的结论，本轮在此基础上加东西。

---

## 2. 本轮四个目标（用户要求）

| 阶段 | 用户原话要点 | 我做的 |
|---|---|---|
| A 镜头 | "镜头分裂缩放效果仍然不明显"；倍率 1.35–1.50；要能 Debug 显示 | 乘法倍率 **1.40**，加 `OnGUI` 角标 |
| B 弹射 | "分裂出来的球仍然停留在原球附近，没有明显弹射"；距离按半径、冲量按组速、短时衰减、之后同速 | 距离 = `(R父 + R子) × 1.8`；冲量 = `组速 × 3`，0.45 s 指数衰减 |
| C 吐球 | "手机长按吐质量仍然不能连续发射"；多点触控、失败重试、孢子对象池 | `MobileInputButton` + 静态手机状态；失败写 0.25 s 重试；`SporePool` |
| D AI 数量 | AI 要按"参赛者"计，不要一个球分裂成 16 个就算 16 个 | `BotId` 身份，`ScoreBoard.AliveBots` 按 id 聚合 |

---

## 3. 改动清单

| 文件 | 行数 | 阶段 | 改动 |
|---|---|---|---|
| `Assets/Scripts/Camera/CameraFollow.cs` | 323 | A | `splitZoomBonus`（加值）→ `splitZoomMultiplier`（乘 baseSize）；`OnGUI` 调试角标 |
| `Assets/Scripts/Player/PlayerCellMotion.cs` | 419 | B | 新增 `SplitCoasting` 状态 + `TickCoast()`；`BeginSplit` 增加 `impulse` / `coastSeconds` 参数 |
| `Assets/Scripts/Player/PlayerActions.cs` | 176 | B/C | 分裂距离按半径算；冲量按组速算；吐球失败写重试时间、删掉每帧日志 |
| `Assets/Scripts/Player/PlayerController.cs` | 686 | B/C | 删 `splitLaunchSpeed` / `splitSpawnGap`；新增 `splitDistanceMultiplier` / `splitImpulseFactor` / `splitCoastDuration` / `spitRetryInterval`；吐球只走 `SpitHeld` |
| `Assets/Scripts/UI/MobileInputButton.cs` | 104 | C | **新增**：按下即置位，支持多指，处理 PointerExit / 禁用 / 失焦 |
| `Assets/Scripts/Player/InputReader.cs` | 153 | C | 静态 `SetMobileSpitHeld`；`SpitHeld = 键盘 || 手机` |
| `Assets/Scripts/Food/SporePool.cs` | 64 | C | **新增**：孢子对象池（Stack + 复用） |
| `Assets/Scripts/Food/Edible.cs` | 126 | C | 孢子回收进池；复用时清速度、重新启用碰撞体 |
| `Assets/Scripts/Core/BallFactory.cs` | 133 | C | `CreateSpitFood` 改为池化；新增 `CreateSporeShell()` |
| `Assets/Scripts/Core/SizeController.cs` | 282 | D | `public int BotId { get; set; } = -1;` |
| `Assets/Scripts/Core/GameManager.cs` | 401 | D | `SpawnAi()` 发 `BotId = aiSpawnCounter++`；`AliveAiCount` 改按 bot 计 |
| `Assets/Scripts/Core/ScoreBoard.cs` | 71 | D | 新增 `AliveBots` / `BotMasses`；`Rank` 按 bot 总质量 |
| `Assets/Scripts/AI/AIController.cs` | 431 | D | 场景里手工摆放的 AI 在 `Awake` 领唯一 id（100 起始） |
| `Assets/Scripts/World/Virus.cs` | 239 | D | 爆裂碎片继承父球的 `BotId` |
| `Assets/Scripts/UI/HUDController.cs` | 108 | D | 排名兜底值按 bot 统计 |

被删除的代码：`splitLaunchSpeed`、`splitSpawnGap`、`OnSpitPressed()` 及其 `SpitPressed` 订阅（会与 `SpitHeld` 路径双倍吐球）、`PlayerActions.TrySpit` 失败分支的每帧 `Debug.Log`、`CameraFollow.splitZoomBonus`。

---

## 4. 关键实现

### 4.1 分裂距离与冲量 —— `PlayerActions.cs:99-134`

```csharp
float newMass = source.Mass * tuning.splitMassRatio;   // half, not a copy
source.SetMass(source.Mass - newMass);                 // parent shrinks first

Vector3 origin = source.transform.position;
float splitRadius = source.Radius * Mathf.Sqrt(tuning.splitMassRatio);
float distance = (source.Radius + splitRadius) * tuning.splitDistanceMultiplier;
Vector3 target = origin + (Vector3)(dir * distance);
SizeController newBall = spawnBall($"{ballPrefix}_Ball{roster.Count}", origin, newMass, shell.sprite, shell.color);

if (newBall == null) { source.SetMass(source.Mass + newMass); continue; }   // rollback

PlayerCellMotion motion = newBall.GetComponent<PlayerCellMotion>();
if (motion != null)
{
    float groupSpeed = tuning.moveSpeed *
        (roster.Main != null ? roster.Main.MoveSpeedFactor : 1f);
    float impulse = groupSpeed * tuning.splitImpulseFactor;
    motion.BeginSplit(origin, target, tuning.splitEjectDuration, impulse, tuning.splitCoastDuration);
}
```

### 4.2 分裂动画（滑出 + 长大）—— `PlayerCellMotion.cs:148-181 / 352-381`

```csharp
public void BeginSplit(Vector2 origin, Vector2 target, float duration, float impulse, float coastSeconds)
{
    splitFrom = origin;  splitTo = target;
    splitDuration = Mathf.Max(0.01f, duration);
    splitImpulse  = Mathf.Max(0f, impulse);
    coastDuration = Mathf.Max(0.05f, coastSeconds);
    splitElapsed = 0f;  coastElapsed = 0f;
    splitEndRadius = self != null ? self.Radius : 0.5f;

    Vector2 direction = target - origin;
    splitDirection = direction.sqrMagnitude > 0.0001f ? direction.normalized : Vector2.right;
    state = CellMotionState.SplitLaunching;

    self.SuppressVisual = true;   // 尺寸由本组件接管
    self.IsEjecting = true;
    self.SetInvulnerable(splitDuration + coastDuration + 0.1f);

    rb.bodyType = RigidbodyType2D.Kinematic;
    rb.position = splitFrom;
    rb.linearVelocity = Vector2.zero;
    ApplyRadius(splitEndRadius * splitStartScale);   // 从 20% 尺寸开始
}
```

```csharp
private void TickSplit()
{
    splitElapsed += Time.fixedDeltaTime;
    float t = Mathf.Clamp01(splitElapsed / splitDuration);
    float eased = t * t * (3f - 2f * t);            // smoothstep

    rb.MovePosition(Vector2.Lerp(splitFrom, splitTo, eased));
    ApplyRadius(Mathf.Lerp(splitEndRadius * splitStartScale, splitEndRadius, eased));

    if (t < 1f) return;

    rb.bodyType = RigidbodyType2D.Dynamic;
    rb.position = splitTo;
    rb.linearVelocity = owner != null ? owner.GroupVelocity : Vector2.zero;
    state = CellMotionState.SplitCoasting;
    coastElapsed = 0f;
}
```

### 4.3 惯性衰减 —— `PlayerCellMotion.cs:385-419`

```csharp
private void TickCoast()
{
    coastElapsed += Time.fixedDeltaTime;
    float t = Mathf.Clamp01(coastElapsed / Mathf.Max(0.05f, coastDuration));

    Vector2 groupVelocity = owner != null ? owner.GroupVelocity : Vector2.zero;
    float decay = Mathf.Exp(-3.5f * t);                       // 1 -> 0.03
    Vector2 velocity = groupVelocity + splitDirection * (splitImpulse * decay);

    if (maxSpeedHardLimit > 0f && velocity.sqrMagnitude > maxSpeedHardLimit * maxSpeedHardLimit)
        velocity = Vector2.ClampMagnitude(velocity, maxSpeedHardLimit);

    rb.linearVelocity = velocity;
    Constrain();

    if (t < 1f) return;

    self.IsEjecting = false;
    self.SuppressVisual = false;
    self.ApplySizeNow();
    state = CellMotionState.Following;
}
```

### 4.4 跟随阶段（唯一的速度规则）—— `PlayerCellMotion.TickFollow`

```csharp
Vector2 offset = rb.position - (Vector2)leader.transform.position;
float restDistance = leader.Radius + self.Radius;
Vector2 closing = (restingSpot - offset) * homeStrength;      // homeStrength = 2.5
Vector2 desired = groupVelocity + closing;
rb.linearVelocity = Vector2.Lerp(rb.linearVelocity, desired, Damp(followResponse * dt));
```

### 4.5 相机倍率 —— `CameraFollow.cs:244-283`

```csharp
float baseSize = SizeForMass(frame.totalMass);      // 7 * (totalMass/60)^0.25
float multiplier = 1f;
bool splitActive = frame.count > 1 || Time.time - lastSplitTime < Mathf.Max(0f, splitBonusHold);
if (splitActive && splitZoomMultiplier > 1f)
{
    float ramp = 1f;
    if (Time.time - lastSplitTime < splitZoomInTime && splitZoomInTime > 0.0001f)
        ramp = Mathf.Clamp01((Time.time - lastSplitTime) / splitZoomInTime);
    multiplier = Mathf.Lerp(1f, splitZoomMultiplier, ramp);
}

float target = baseSize * multiplier;               // 乘 baseSize，不乘当前值

float halfWidth  = Mathf.Max(center.x - frame.minX, frame.maxX - center.x) + padding;
float halfHeight = Mathf.Max(center.y - frame.minY, frame.maxY - center.y) + padding;
float boundsSize = Mathf.Max(halfHeight, halfWidth / aspect);

target = Mathf.Max(target, boundsSize);
target = Mathf.Max(target, minOrthographicSize);
if (maxOrthographicSize > 0f) target = Mathf.Min(target, maxOrthographicSize);
```

`CaptureGroup()`（`:179-215`）用 `p.x ± ball.Radius * radiusScale` 建 bounds，**包含半径**。

### 4.6 手机长按 —— `MobileInputButton.cs`（新增，节选）

```csharp
private readonly HashSet<int> pressing = new HashSet<int>();

public void OnPointerDown(PointerEventData e)
{
    pressing.Add(e.pointerId);
    if (isSpitButton) InputReader.SetMobileSpitHeld(true);
}

public void OnPointerUp(PointerEventData e)
{
    if (e != null) pressing.Remove(e.pointerId);
    if (pressing.Count == 0) Release();
}

public void OnPointerExit(PointerEventData e)   // 手指滑出按钮也要松开
{
    if (e != null) pressing.Remove(e.pointerId);
    if (pressing.Count == 0) Release();
}

private void Clear() { bool was = pressing.Count > 0; pressing.Clear(); if (was) Release(); }
// OnEnable / OnDisable / OnApplicationPause(true) 都调 Clear()
```

`InputReader.cs:121-139`：

```csharp
private static bool mobileSpitHeld;
public static void SetMobileSpitHeld(bool held) { mobileSpitHeld = held; }

public bool SpitHeld
{
    get
    {
        bool keyHeld = spitAction != null && spitAction.IsPressed();
        return keyHeld || mobileSpitHeld;
    }
}
```

`PlayerController.Update()` 里只保留一条路径：`if (playerInput.SpitHeld) TrySpit();`（不再订阅 `SpitPressed`）。

### 4.7 孢子对象池 —— `SporePool.cs`（新增）

```csharp
public static Edible Take()
{
    while (free.Count > 0) { var pooled = free.Pop(); if (pooled != null) return pooled; }
    GameObject go = BallFactory.CreateSporeShell(); created++;
    return go.GetComponent<Edible>();
}

public static void Give(Edible edible)
{
    if (edible == null) return;
    edible.gameObject.SetActive(false);
    edible.transform.SetParent(Root, false);
    free.Push(edible);
}
```

### 4.8 AI 身份 —— `BotId`

```csharp
// SizeController.cs:94
public int BotId { get; set; } = -1;

// AIController.Awake()：场景里手工摆放的 AI 没有走过生成流程
if (self != null && self.BotId < 0) self.BotId = s_NextSceneBotId++;   // static int = 100

// GameManager.SpawnAi()
int botId = aiSpawnCounter;
aiSpawnCounter++;
size.BotId = botId;

// GameManager.AliveAiCount
get { return ScoreBoard.AliveBots(aiTeam); }

// ScoreBoard.BotMasses
int id = ball.BotId >= 0 ? ball.BotId : -1 - ball.GetInstanceID();
totals[id] = current + ball.Mass;
```

---

## 5. 参数表（代码默认值 / 场景实际写入值）

| 参数 | 代码默认 | 场景值 | 含义 |
|---|---|---|---|
| `splitDistanceMultiplier` | 1.8 | 1.8 | 分裂距离 = (R父+R子) × 该值 |
| `splitImpulseFactor` | 3 | 3 | 冲量 = 组速 × 该值 |
| `splitCoastDuration` | 0.45 | 0.45 | 冲量衰减时长（s） |
| `splitEjectDuration` | 0.18 | 0.18 | 碎片滑出时长（s） |
| `splitMassRatio` | 0.5 | 0.5 | 一分为二 |
| `splitCooldown` | 0.5 | 0.5 | 分裂冷却 |
| `splitMinMass` | 30 | 30 | 可分裂最小质量 |
| `maxBallCount` | 16 | 16 | 上限 |
| `spitMassCost` | 12 | 12 | 每颗孢子扣的质量 |
| `spitFoodMass` | 9 | 9 | 孢子自身质量（成本 > 产出） |
| `spitCooldown` | 0.12 | 0.12 | 每次吐球间隔 |
| `spitRetryInterval` | 0.25 | 0.25 | 质量不足时重试间隔 |
| `minMassAfterSpit` | 20 | 20 | 吐完留底 |
| `moveSpeed` | 3.2 | — | 基础速度 |
| `homeStrength` / `homeDamping` | 2.5 / 4 | 2.5 / 4 | 跟随弹簧 |
| `maxDriftSpeed` | 3 | 3 | 子球相对主球的最大偏离速度 |
| `baseZoomBase` / `baseZoomReferenceMass` / `baseZoomExponent` | 7 / 60 / 0.25 | 7 / 60 / 0.25 | 基础镜头 |
| `splitZoomMultiplier` | 1.40 | **1.4** | 分裂倍率 |
| `splitZoomInTime` / `splitBonusHold` | 0.12 / 0.45 | — | 拉远速度 / 保持 |
| `zoomOutTime` / `zoomInTime` / `positionSmoothTime` | 0.18 / 0.32 / 0.16 | — | 平滑 |
| `showDebug` | false | **true** | 左上角调试角标 |

场景其他改动：`BtnSpit` 的 `Button.onClick` 清空并改挂 `MobileInputButton`；`EventSystem.m_sendNavigationEvents = false`（旧选中态会每帧触发 `TriggerSplit()`）。

---

## 6. 实测证据（Play 模式，编辑器内）

| # | 检查 | 判据 | 结果 |
|---|---|---|---|
| A1 | 倍率生效 | 读相机调试字段 | `base 12.155 × 1.40 = 17.017` |
| A2 | 相机真的到位 | `Camera.main.orthographicSize` | 12.42 → **17.016** |
| B1 | 计划位移 | `splitFrom` / `splitTo` | **10.31** = 1.8 × (3.35 + 3.35×√0.5) |
| B2 | 刚体实际位移 | `rb.position` | (18.53,−16.96) → **(26.22,−9.11)** = 10.31 |
| B3 | 冲量 | `splitImpulse` | **11.79** = 组速 × 3 |
| B4 | 状态机 | `state` | `SplitLaunching(0.18s) → SplitCoasting(0.45s) → Following` |
| C1 | 长按连续 | 置手机状态 2 s | 孢子 **0 → 42**，总质量 553 → 25 |
| C2 | 松开即停 | 置 false | 停止 |
| D1 | 参赛者 | `gm.ParticipantCount` | **11** |
| D2 | AI 按 bot 计 | `ScoreBoard.AliveBots` | **10**（同刻 41 个 cell） |
| D3 | id 唯一 | `BotId` 分布 | min 100 / max 109，10 个互不相同 |
| 编译 | | console | 0 error / 0 warning |

---

## 7. 想请你（审查者）重点验证的 9 点

1. **交接帧速度连续性**：`TickSplit` 结束把 `linearVelocity` 设成 `owner.GroupVelocity`，下一帧 `TickCoast` 又覆盖成 `group + impulse·decay`。中间是否有一帧"掉速"（片被主球追近）？
2. **coast → following 的台阶**：`TickCoast` 里完全没有 home 弹簧，0.45 s 结束时瞬间切到 `TickFollow`（弹簧 + Lerp 阻尼）。冲量较大时（如 11.79）会不会在切换点看到一次速度突变？
3. **`PushApart` 与 `MovePosition` 冲突**：`PlayerMerge.Nudge` 直接 `rb.position += delta`（绕过物理），`PlayerCellMotion` 在 `SplitLaunching` 用 `rb.MovePosition`。两个都改位置，谁覆盖谁？`IsBusy` 跳过是否足够？
4. **质量先扣后建的回滚窗口**：先 `SetMass(Mass - newMass)` 再 `spawnBall`，失败才回滚。同一帧内 `Radius` 已缩小，是否会与 `Eater` / `Virus` 的触发判定产生一帧不一致（例如刚好被吃）？
5. **相机 bounds 是否真的包含半径**：`CaptureGroup` 用 `p.x ± Radius*radiusScale`，请复核（我认为包含）。
6. **分裂判定用 `frame.count > 1`**：两个球合体后 `count == 1`，靠 `splitBonusHold=0.45s` 兜底回落；拉近用 0.32 s。这段"先保持再回落"会不会有一顿？
7. **`MobileInputButton` 的释放路径**：只处理了 PointerUp / PointerExit / OnDisable / 失焦。`OnPointerCancel`、指针被 UI 系统夺走、或者 Button 组件自身 `interactable=false` 时是否会卡在"一直吐"？
8. **`SporePool` 静态状态**：`root` 与 `free` 都是静态。场景切换/回合重开时，池中的 `GameObject` 已销毁但引用还在栈里（`Take()` 用 `pooled != null` 过滤 Unity 假 null）。是否有更稳的清理时机？
9. **`ScoreBoard.BotMasses` 每次调用都 `new Dictionary`**：`HUDController` 每 0.2 s 调一次 `Rank`，`GameManager.TickAiRespawn` 每帧调 `AliveBots`。是否需要缓存或改成无分配统计？

## 8. 已知不确定（不是 bug，但没验证）

- 三个手感参数（1.8 / 3 / 0.45）只有数值正确，没有真人手感验证。
- 手机真机没测，只用编辑器里的指针事件模拟。
- `showDebug` 默认开，验收后需要手动关闭。
