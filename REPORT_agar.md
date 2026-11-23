| `Assets/Scripts/Player/PlayerRoster.cs` | 修改 | 主球改为“粘住”：只有主球死了才换，分裂出来的碎片不再抢控制权 |
| `Assets/Scripts/Player/PlayerMarker.cs` | **新增** | 玩家主球上的白色三角指向（代码生成 Sprite，不挂在球下面） |
# 项目修正报告：以 Agar.io 经典 FFA 为参考

> 项目：`E:\zm\FYP\ea\-`（Unity 6000.3.5f2，C#，URP 2D）
> 场景：`Assets/Scenes/LevelScene.unity`
> 本次改动没有重写任何系统，全部在现有脚本上改机制；地图、摄像机、UI 未改。

---

## 1. 网上参考资料及其用途

| 资料 | 抓取情况 | 用到了什么 |
|---|---|---|
| `github.com/owenashurst/agar.io-clone`（master 源码，通过 `api.github.com/.../git/trees/master?recursive=1` 列出文件后逐个取 raw） | 成功 | 半径公式、距离公式、移动减速、吞噬判定、分裂数量分配、病毒、吐质量、衰减 |
| 同一仓库 wiki `Game-Architecture.md`（GitHub 网页版只给导航，改用 `raw.githubusercontent.com/wiki/...`） | 成功 | 服务端 tick 结构（60Hz 逻辑、1Hz 平衡食物与病毒） |
| `support.miniclip.com/hc/en-us/articles/4404685562641`（官方操作说明） | 成功 | 鼠标移动、`W` 吐质量、`SPACE` 分裂 |
| `agar.im/how-to-play` 与 `agar.im/guide/tactics` | **失败**：HTTP 200 但只返回标题与导航，正文被截断 | 未取到任何数值；因此本报告不引用这两个站点的数字 |

从 clone 源码里取到的关键常量与公式（原文）：

- `massToRadius = (mass) => 4 + Math.sqrt(mass) * 6` —— 半径与质量是 **平方根** 关系
- `getDistance = hypot(dx, dy) - r1 - r2` —— 用**边到边**距离，不是圆心距
- `MIN_SPEED = 6.25`、`SPLIT_CELL_SPEED = 20`、`SPEED_DECREMENT = 0.5`、`MIN_DISTANCE = 50`、`MERGE_TIMER = 15`
- `slowDown = mathLog(mass, slowBase) - initMassLog + 1`（`slowBase = 4.5`）—— 大球更慢
- 吞噬：`pointInCircle(mass, cellCircle)`（**目标圆心落在吞食者圆内**）且 `cell.mass > mass.mass * 1.1`
- `splitCell`：`piecesToCreate = min(floor(mass / defaultPlayerMass), maxRequestedPieces)`，每片 `mass / piecesToCreate`
- `userSplit`：球数超过一半上限时，**只让 `maxCells - cells.length + 1` 个最大的球分裂**
- `virusSplit`：撞病毒时把球数一次拉满到上限
- `config.js`：`foodMass 1`、`fireFood 20`、`limitSplit 16`、`defaultPlayerMass 10`、`virus.defaultMass 100~150`、`maxFood 1000`、`maxVirus 50`、`massLossRate 1`、`minMassLoss 50`

**没能从一手资料证实的数字**（因此本次只作为可调测试参数，并在 Inspector 里标注）：
"喂 7 次病毒发射新病毒"、"合球约 30 秒"、"质量 500 以上每秒衰减 0.21%"。这三条来自任务书，不是从上面任何页面抄来的。
另外 clone 里 `virus.splitMass = 180` 这个字段**定义了但没有被使用**——它没有实现喂病毒，所以喂病毒的实现方式是我们按 Agar 玩法自己设计的。

比例换算说明：clone 的质量体系是 `defaultPlayerMass = 10`、食物 1、病毒 100~150、总量 20000；本项目是 10~1000+。所以没有照抄 1.1 倍阈值和 20 点吐质量，而是换成 1.25 倍（任务书指定）与 12/9 的吐出/食物质量（按本项目尺度缩放）。

---

## 2. 原项目存在的问题（改之前）

先回答第七节要求的归属梳理——每项机制只有一个脚本负责：

| 机制 | 负责脚本 |
|---|---|
| 质量数据 | `Core/SizeController.cs`（`mass` 是唯一来源，`Radius`/`MoveSpeedFactor`/`CanEat` 全从它算） |
| 食物生成 | `Food/FoodSpawner.cs` + `Food/FoodPool.cs` + `Food/Edible.cs` |
| 玩家移动 | `Player/PlayerController.cs`（`MoveBall`） |
| AI 移动 | `AI/AIController.cs` |
| 分裂 | `Player/PlayerActions.cs`（`TrySplit`）+ `Player/PlayerMerge.cs`（记录冷却） |
| 吐质量 | `Player/PlayerActions.cs`（`TrySpit`）+ `Core/BallFactory.cs`（`CreateSpitFood`） |
| 合球 | `Player/PlayerMerge.cs` |
| 碰撞与吞噬 | `Core/Eater.cs`（触发）+ `Core/SizeController.cs`（`CanEat`/`TryEat`） |

**没有发现重复的质量计算**：所有质量读写都经过 `SizeController` 的 `Mass`/`SetMass`/`AddMass`/`TryConsumeMass`，`Radius` 由质量推导。这一块原本就是对的。

真正的问题：

1. **球有实体碰撞体**：`BallFactory.CreateBall` 给每个球加了动态 `CircleCollider2D`（非 trigger）。球会互相挤开、堆在一起，大球永远压不到小球中心 → 吞噬规则形同虚设。这是"球和球会挤在一起"的直接原因。
2. **吞噬只看接触**：`Eater` 只有 `OnTriggerEnter2D` → `TryEat`，擦到边缘立刻吞掉。没有"覆盖深度"判定，也没有 `OnTriggerStay2D`。
3. **阈值硬编码**：`eatMassRatio = 1.2`，不区分 Player/AI，也没有 Inspector 说明。
4. **半径公式没有可调系数**：`Radius = 0.75 * sqrt(mass/10)` 已是平方根关系，但 `0.75` 被 `radiusAtReferenceMass/referenceMass` 锁死，不能直接调 scale。
5. **分裂只分最大的那一个球**：`PlayerActions.TrySplit` 里 `source = roster.Largest()`，一次只裂一颗；`maxBallCount = 8`（任务书要求 16）。而且分裂方向来自摇杆朝向，不是鼠标。
6. **吐质量参数不合理**：`spitMassCost = 10 / spitFoodMass = 5 / spitCooldown = 0.25`，吐出物不标记为"孢子"，无法喂病毒。
7. **完全没有病毒系统**。
8. **合球冷却固定**：`PlayerMerge.MarkSplit` 写死 `Time.time + mergeCooldown`（15 s），与质量无关。
9. **没有质量衰减**。
10. **移动硬停**：无输入时 `rb.linearVelocity = Vector2.zero`，松手瞬间停住，没有惯性。
11. **AI 不知道病毒存在**（当时也没病毒），会直线撞上去。
12. 摄像机、UI、食物池、边界约束：**原本就是好的**，没动。

---

## 3. 修改方案与实施顺序

### 3.1 碰撞与吞噬（第一阶段的重点）
- 删掉球的实体碰撞体，只留 trigger（`radius 0.5, isTrigger = true`，随 `localScale` 放大后就是半径）。球现在可以互相重叠，不再被挤开。
- 吞噬改成两级判定：`SizeController.CanEat`（质量比 1.25）+ `Eater.IsCovered`（`allowed = 半径 - (1 - swallowDepth) * 目标半径`，`swallowDepth = 1` 等价 Agar 的"目标圆心在吞食者内"）。
- `OnTriggerEnter2D` 和 `OnTriggerStay2D` 都检查，因为进入后重叠会继续加深，而 Enter 只触发一次。
- 好处：擦边不再死；要真正压上去才被吃；不再需要物理推力，也就没有"挤成一团"。

### 3.2 质量与半径
- 统一 `Radius = RadiusScale * sqrt(mass)`，`RadiusScale` 为 0 时由 `radiusAtReferenceMass / sqrt(referenceMass)` 推导（默认 0.237），Inspector 里可直接调。

### 3.3 分裂（仿 `userSplit`）
- `maxBallCount` 改成 16。
- 冷却内按 Agar 的分配方式：球数 > 上限一半时只让 `上限 - 球数 + 1` 个最大的球分裂，否则所有够质量的球一起分。
- 每颗球质量减半，新球拿到另一半（不是复制），新球朝定向方向弹射并带冲量。
- `aimAtMouse` 打开后用鼠标位置定向（网页版 Agar 的操作方式），没有鼠标时回落到摇杆朝向。

### 3.4 吐质量（Eject，仿 `fireFood`）
- 扣 12、吐出物 9（成本大于产出，使它成为有代价的操作）。
- 吐出物标记为孢子（`Edible.MarkSpore`），只有孢子能喂病毒；普通彩豆穿过病毒不受影响。
- 冷却 0.12 s，可连点；低于 `minMassAfterSpit` 不允许吐。

### 3.5 病毒（第三阶段，新增）
- `Virus`：半径也是平方根；被质量更大的球压中（病毒圆心在球内）→ 爆裂。
- 爆裂数量仿 `virusSplit`：`pieces = clamp(burstPieces, 1, 上限 - 当前球数 + 1)`，正好 16 颗时只吃病毒加质量、不再分身；被吃掉的病毒不加质量（Agar 里病毒本身不是食物）。
- 碎片按 `360/(pieces-1)` 均匀炸开，第一片固定朝正右。
- `VirusSpawner`：程序化生成星形贴图（项目里没有病毒美术），按 `virusCount` 维持数量，避开球与其他病毒，`sortingOrder = 15` 高于球层，所以小球可以躲在后面。
- 喂病毒：孢子被吸收累加 `feeds`，达到 `feedsToSplit = 7` 时朝孢子来向发射一个新病毒；`maxVirus = 50` 封顶。
- 每个孢子只能被算一次（`Edible.consumed` 消费后立即 `Release`）。

### 3.6 质量衰减（第四阶段）
- `TickDecay(Time.deltaTime)`：`next = mass * exp(-rate * dt)`，指数式所以与帧率无关。
- 只有 `mass > decayThreshold(500)` 才衰减；超过 `heavyDecayThreshold(5000)` 时速率乘 `heavyDecayMultiplier(2)`。
- 结果经由 `SetMass` 写回，半径、碰撞体、视觉一起更新；不会低于 `decayFloorMass`。

### 3.7 合球（第四阶段）
- `CooldownFor(mass) = clamp(mergeCooldown + mass * mergeCooldownPerMass, 0, mergeCooldownMax)`，默认 30 + 0.02×质量，封顶 60 s。
- 冷却与"合球动画时长"是两个量：`mergePullDuration = 0.45` 只控制视觉吸附动画，不参与冷却。

### 3.7.1 同一玩家的分身是实体（本轮修正）
原项目里所有球的实体碰撞体都删掉、只留 trigger，结果同一玩家的分身像叠影子一样互相穿过去。对照 `src/server/map/player.js` 的 `Player.move()`：

```js
if (this.cells.length > 1) {
    if (this.timeToMerge < Date.now()) { this.mergeCollidingCells(); }  // 到时间：重叠就合
    else { this.pushAwayCollidingCells(); }                            // 没到时间：互相推开
}
```

`pushAwayCollidingCells()` 是 `vector = normalize(B - A) * PUSHING_AWAY_SPEED`，然后 `A -= vector; B += vector`。所以：
- 不同玩家之间没有推开（只做吞噬判定），球该重叠就重叠；
- 自己的分身之间在冷却内是实体，会互相顶住；冷却到了才允许重叠并合并。

对应实现：
- `PlayerMerge.PushApart(deltaTime)`：每帧对同一玩家、且**双方都还没到合并时间**的球对，按 `Mathf.Min(overlap * 0.5f, pushApartSpeed * deltaTime)` 各推一半，推完再 `ArenaManager.Constrain` 防止被推出地图。完全重合时取 `Vector2.up` 作方向（和原项目取 `(0,1)` 一致）。
- `PlayerSubBall` 不再给分身安排固定站位，改为**和主球朝同一个目标移动**（原项目每个 cell 都朝同一个 `(this.x, this.y) + target` 走），站位差由推开逻辑自己撑出来；到了合并时间则用 `returnHomeStrength` 慢慢蹭进主球。
- 合并动画（`PlayerMerge.Begin` 里的 `MovePosition` + `localScale` 缩到 0）就是"慢慢穿过去"：小球视觉上缩进去，最后才把质量加给大球。

### 3.7.2 主球固定、三角指向、按体积决定遮挡（本轮修正）

原项目里有两个相关的问题：

1. `PlayerRoster.RefreshMain()` 每帧都取质量最大的球当主球，所以一分裂、碎片一大，操作权就在几个分身之间来回跳。
   参考源码里 `Player.move()` 根本没有“主球”这个概念 —— 所有细胞共用同一个目标点，操作权天然稳定。
   我们的做法：`PlayerRoster.Add()` 只在还没有主球时设主球，`RefreshMain()` 只在主球死亡后才重选。这样分裂、合并、爆裂都不会换手，只有主球被吃掉才换。

2. 所有球写死 `sortingOrder = 10`，病毒写死 `15`，所以**不管谁大谁小，病毒永远压在所有球上面**。
   Agar 的遮挡规则其实很简单：**谁大谁在上面**。所以改成把体积换算成层级：

```csharp
public const int SortingBase = 10;
public const float SortingPerUnitRadius = 8f;

public static int SortingOrderForRadius(float radius)
{
    return SortingBase + Mathf.RoundToInt(radius * SortingPerUnitRadius);
}
```

`SizeController.ApplySizeNow()` 和 `Virus.ApplyVisual()` 都调这一个公式，于是：
- 我的半径 7.12 → `order 67`，病毒半径 2.60 → `order 31`，大球盖住病毒（能吃掉它，视觉上也压住它）；
- 小球半径 1.84 → `order 25`，被病毒盖住（藏得起来）。
吃的判定和遮挡用的是同一套几何量，不会出现“看着被盖住却能吃”的矛盾。

3. 三角指向（`PlayerMarker.cs`）：Unity 项目里没有这个美术资源，所以三角形是用代码逐像素生成的白色 Sprite（2×2 超采样抗锯齿，指向 +X）。
   它**不挂在球下面** —— 球会随质量缩放，挂在下面会被一起压扁；改成每帧由 `PlayerController.LateUpdate()` 调用 `Follow(controller.Balls[0], aim)`，定位到球边缘 `radius * 0.72` 处并朝准星方向旋转。

### 3.7.3 主球取最大、全员吐球、三角外移、分身穿出（本轮修正）

四条要求逐条落到代码上：

1. **操控最大的那颗球**：`PlayerRoster.RefreshMain()` 现在带一个 `TakeOverRatio = 1.02f` —— 主球活着时，只有别的球质量超过主球 1.02 倍才把操控权交出去。刚对半分出来的两颗天然等大，不会互相抢，所以不会再"一会儿操控这个一会儿操控那个"。
2. **所有分身都能吐**：`PlayerActions.TrySpit` 从"只让主球吐"改成遍历 `roster.Balls`，只要这颗球满足 `Mass - spitMassCost >= minMassAfterSpit` 就吐一颗，返回值改成吐出的个数，`PlayerController.TrySpit` 把个数打进日志。
3. **三角在球体之外**：`PlayerMarker.edgeFactor` 从 `0.72` 改成 `1.08`，`sizeFactor` 从 `0.6` 改成 `0.5`；`edgeFactor > 1` 时三角整个落在球轮廓外。
4. **看得见分身从身体里分出去**：新增 `SplitEject`。分裂时新球不是直接生成在 `2×Radius` 处，而是生成在**母球圆心**，先 `SuppressVisual` 把渲染缩放压到最终尺寸的 20%、`Rigidbody2D` 切成 `Kinematic`，再在 `splitEjectDuration`（默认 0.3 s）里同时做两件事：位置从圆心插值到 `2×Radius` 处、渲染半径从 20% 长到 100%。动画结束再恢复 `Dynamic`、按 `splitLaunchSpeed` 弹出去。这就是 Agar 里"分身从身上挤出来"的观感。

配套的两处保护：`PlayerSubBall` 在 `IsEjecting` 期间完全不动（不跟主球抢位置），`PlayerMerge.PushApart` 跳过正在滑出的碎片（它反正还没到位，推开会把它顶回母球里）。


### 3.7.4 溜溜球、全员吐球、没有真正的主控（本轮修正）

这一轮的四条要求是连在一起的，所以一起改：

1. **分裂是"扔出去再收回来"**：`PlayerSubBall` 整个重写成一个"相对速度的阻尼弹簧"。分裂结束（`SplitEject` 播完）时碎片带着 `splitLaunchSpeed = 24` 往外飞，弹簧只拉"相对于大球的偏移"，阻尼负责吃掉这份动能，于是碎片先飞出约 2 倍接触距离，再自己慢慢荡回来。核心就三步：
   ```csharp
   Vector2 drift = rb.linearVelocity - inputVelocity;                 // 相对大球的速度 = 扔出去的那份
   Vector2 accel = (restingSpot - offset) * homeStrength - drift * homeDamping;
   rb.linearVelocity = inputVelocity + drift + accel * Time.fixedDeltaTime;
   ```
   `restingSpot = 方向 × (大球半径 + 自己半径)`，所以收回来的时候正好停在大球**隔壁**，不会插进去。因为玩家的输入速度是最后加上的，碎片荡回来的全程依然跟着玩家走。

2. **收回来之后才融合**：`mergeCooldown` 从 30 秒降到 **4.5 秒**，`mergeCooldownPerMass` 0.02 → 0.002，`mergeCooldownMax` 60 → 8，于是"分裂 → 靠拢 → 在隔壁待着 → 四五秒后融合"这一整条时间线对得上。融合本身仍然是 `PlayerMerge` 的 0.45 s 缩进动画，和冷却分开计时。

3. **每个分身都会吐**：`PlayerActions.TrySpit` 遍历 `roster.Balls`，每颗球各自扣自己的 `spitMassCost`、各吐一颗。4 颗球按一次 = 4 颗孢子。刚分裂出去还没滑到位的碎片（`IsEjecting`）先不参与，避免从身体里往外吐。

4. **没有真正的主控球**：所有球都朝同一个 `aim` 走 —— 主球用 `MoveBall`，分身用 `PlayerSubBall`，两者的目标速度都是"输入 × 速度系数"，叠加后整组一起飞，并且都带惯性（松手用 `deceleration` 滑行）。`MainBall` 现在只做两件事：三角指向的位置、分裂时"大的先分"的顺序。`PlayerRoster.TakeOverRatio = 1.02` 让它只在明显更大的球出现时才换锚点，不会在等大分身之间来回跳。
### 3.8 AI（第五阶段）
- 保留原有 Wander/Chase/Flee 状态机、扫描节流（`scanInterval = 0.2`，`OverlapCircleNonAlloc`）。
- 只加一条：能吃掉病毒的大球（`self.Mass > virus.Mass`）会主动避开半径 5 内的病毒；小球不避，因为病毒对它们是掩体。

### 3.9 实施顺序（与任务书一致，每阶段先编译再进下一阶段）
1. 质量/食物/吞噬：`BallFactory`（去实体碰撞）、`Eater`（深度判定）、`SizeController`（半径+阈值+衰减字段）
2. Split/Eject：`PlayerActions`、`PlayerController`、`Edible`/`BallFactory`（孢子标记）
3. Virus：`Virus`、`VirusSpawner`、场景挂载
4. Decay/AutoMerge：`SizeController.TickDecay`、`PlayerMerge.CooldownFor`
5. AI：`AIController.ApplyVirusAvoidance`
6. 测试与修复

---

## 4. 改动文件清单

| 文件 | 状态 | 改了什么 |
|---|---|---|
| `Assets/Scripts/Core/SizeController.cs` | 修改 | 半径改成 `RadiusScale * sqrt(mass)` 并暴露 `radiusScale`；`eatMassRatio` 1.2→1.25；新增 6 个衰减字段与 `TickDecay` |
| `Assets/Scripts/Core/Eater.cs` | 重写 | 新增 `swallowDepth`、`OnTriggerStay2D`、`IsCovered`（质量比 + 覆盖深度） |
| `Assets/Scripts/Core/BallFactory.cs` | 修改 | 删掉实体碰撞体，只留 trigger；`CreateSpitFood` 返回 `Edible` 并 `MarkSpore()` |
| `Assets/Scripts/Food/Edible.cs` | 修改 | 新增 `spore`/`IsSpore`/`Mass`/`MarkSpore`；孢子撞病毒→`Virus.Absorb`；抽出 `Release()` |
| `Assets/Scripts/Player/PlayerActions.cs` | 重写 | `TrySplit(aim)` 返回被分裂的球列表，仿 `userSplit` 的分配，碎片在母球圆心生成后交给 `SplitEject`；`TrySpit` 改成**每颗球各吐一颗**并返回吐出个数 |
| `Assets/Scripts/Player/PlayerController.cs` | 修改 | `maxBallCount` 16；新增 `deceleration`、`aimAtMouse`、`aim`、`MarkBurst`、`MaxBallCount`、`splitEjectDuration`、`pushApartSpeed`；`MoveBall` 去掉硬停；**本轮把 `splitLaunchSpeed` 14→24（扔得更远），`mergeCooldown` 30→4.5、`mergeCooldownPerMass` 0.02→0.002、`mergeCooldownMax` 60→8（四五秒就融合）** |
| `Assets/Scripts/Player/PlayerMerge.cs` | 修改 | `MarkSplit` → `Time.time + CooldownFor(mass)`，新增按质量计算并封顶；**本轮新增 `PushApart()`：冷却未到时同玩家分身互相推开（对应原项目 `pushAwayCollidingCells`）** |
| `Assets/Scripts/Player/PlayerSubBall.cs` | **重写** | 改成"相对速度的阻尼弹簧"：让碎片先飞出去再自己荡回大球隔壁；不再有固定站位、不再硬停速度。新增 `ApplyTuning()`，参数统一由 `PlayerController` 的 Sub Ball Yoyo 组下发 |
| `Assets/Scripts/AI/AIController.cs` | 修改 | 新增病毒规避（只对能吃病毒的大球生效） |
| `Assets/Scripts/World/Virus.cs` | **新增** | 病毒本体：爆裂、喂食、推挤、发射 |
| `Assets/Scripts/World/VirusSpawner.cs` | **新增** | 病毒生成/补满/上限/程序化星形贴图/发射新病毒 |
| `Assets/Scripts/Player/PlayerRoster.cs` | 修改 | 操控权交给质量最大的球（超过主球 1.02 倍才接管），刚分出来的等大分身不会互相抢 |
| `Assets/Scripts/Player/SplitEject.cs` | **新增** | 分裂碎片的出场动画：从母球圆心、20% 大小插值到满尺寸和 `2×Radius` 处 |
| `Assets/Scripts/Player/PlayerMarker.cs` | **新增** | 主球上的白色三角指向，`edgeFactor = 1.08` 落在球体之外，层级 = 主球 +200 |
| `Assets/Scenes/LevelScene.unity` | 修改 | 新增根物体 `VirusSpawner`；写入 `PlayerRoot` 与 11 颗球的序列化参数 |

没有新建第二套质量或碰撞系统，也没有删除任何原有组件。

---

## 5. 所有修改后的完整代码

下面每一节都是文件当前的真实内容（由磁盘读取后直接嵌入），不是片段。

### 5.1 `Assets/Scripts/Core/SizeController.cs`

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

### 5.2 `Assets/Scripts/Core/Eater.cs`

```csharp
using UnityEngine;

namespace QiuQiu
{
    // SizeController.TryEat decide who eat who.
    // This component only decides WHEN the bite happens: a small overlap is not enough,
    // the prey has to be covered by the bigger ball first (Agar.io style).
    [DisallowMultipleComponent]
    [RequireComponent(typeof(SizeController))]
    public class Eater : MonoBehaviour
    {
        // 0 = touch is enough, 1 = the prey's centre must be inside the eater (Agar.io rule)
        [SerializeField, Range(0f, 1f)] private float swallowDepth = 1f;

        private SizeController self;

        private void Awake()
        {
            self = GetComponent<SizeController>();
        }

        private void OnTriggerEnter2D(Collider2D other)
        {
            TrySwallow(other);
        }

        // Enter only fires once, but the overlap keeps growing while the balls sit on each
        // other, so the bite has to be re-checked every physics step.
        private void OnTriggerStay2D(Collider2D other)
        {
            TrySwallow(other);
        }

        private void TrySwallow(Collider2D other)
        {
            if (other == null)
            {
                return;
            }

            SizeController target = other.GetComponentInParent<SizeController>();
            if (target == null || target == self)
            {
                return;
            }

            if (self.IsDead || target.IsDead)
            {
                return;
            }

            if (self.Team == Team.Player && target.Team == Team.Player)
            {
                return;
            }

            if (!IsCovered(target))
            {
                return;
            }

            if (!self.TryEat(target))
            {
                return;
            }

            // only the player hears its own bite, ten AI eating must stay silent
            if (self.Team == Team.Player && AudioManager.Instance != null)
            {
                AudioManager.Instance.PlayEatEnemy();
            }
        }

        private bool IsCovered(SizeController target)
        {
            if (!self.CanEat(target))
            {
                return false;
            }

            // Allowed centre distance. depth 1 -> target centre inside the eater (Agar.io),
            // depth 0 -> the target has to be completely swallowed.
            float allowed = self.Radius - (1f - swallowDepth) * target.Radius;
            if (allowed <= 0f)
            {
                return false;
            }

            Vector2 offset = (Vector2)target.transform.position - (Vector2)transform.position;
            return offset.sqrMagnitude <= allowed * allowed;
        }
    }
}
```

### 5.3 `Assets/Scripts/Core/BallFactory.cs`

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
            GameObject go = CreateFoodBase(name, position, mass, sprite, color, SpitOrder, armDelay);

            Rigidbody2D rb = go.AddComponent<Rigidbody2D>();
            rb.bodyType = RigidbodyType2D.Dynamic;
            rb.gravityScale = 0f;
            rb.linearDamping = 1.8f;
            rb.linearVelocity = velocity;

            // A spore is still food, but it is also the thing that feeds and pushes viruses.
            Edible edible = go.GetComponent<Edible>();
            if (edible != null)
            {
                edible.MarkSpore();
            }
            return edible;
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

### 5.4 `Assets/Scripts/Food/Edible.cs`

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
            if (poolable)
            {
                FoodPool.Give(this);
            }
            else
            {
                Destroy(gameObject); //for ball spit  food
            }
        }
    }
}
```

### 5.5 `Assets/Scripts/Player/PlayerActions.cs`

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

        // sub balls sit on a ring around the main ball
        public float SlotAngleFor(int index)
        {
            int slots = Mathf.Max(1, tuning.maxBallCount - 1);
            return 90f + 360f / slots * (index - 1);
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

                // the piece is born in the middle of the body, not on its rim: SplitEject
                // then pushes it out while it grows, so you can watch it leave the parent
                Vector3 origin = source.transform.position;
                Vector3 target = origin + (Vector3)(dir * (source.Radius * 2f + tuning.splitSpawnGap));
                SizeController newBall = spawnBall($"{ballPrefix}_Ball{roster.Count}", origin, newMass, shell.sprite, shell.color);

                if (newBall == null)
                {
                    source.SetMass(source.Mass + newMass);   // put the mass back, nothing happened
                    continue;
                }

                SplitEject eject = newBall.gameObject.AddComponent<SplitEject>();
                eject.Begin(origin, target, tuning.splitEjectDuration, tuning.splitLaunchSpeed);

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

### 5.6 `Assets/Scripts/Player/PlayerController.cs`

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
        [Tooltip("Speed a split piece is thrown with. The yoyo pull in PlayerSubBall decides how far it actually gets")]
        [SerializeField] private float splitLaunchSpeed = 24f;
        [SerializeField] private float splitSpawnGap = 0.05f;
        [Tooltip("How long a new split piece takes to slide out of its parent, in seconds")]
        [SerializeField] private float splitEjectDuration = 0.3f;
        [Tooltip("Aim split / spit at the mouse when a mouse is present (Agar.io), otherwise at the movement direction")]
        [SerializeField] private bool aimAtMouse = false;

        [Header("Sub Ball Yoyo")]
        [Tooltip("Pull back towards the big ball. Higher = shorter throw, comes back sooner")]
        [SerializeField] private float subHomeStrength = 7f;
        [Tooltip("Damping of the thrown out speed. Higher = settles straight away, lower = swings a few times")]
        [SerializeField] private float subHomeDamping = 2.4f;
        [Tooltip("Speed cap for the swing itself, not counting the player's own movement")]
        [SerializeField] private float subMaxDriftSpeed = 26f;
        [Tooltip("Fallback follow acceleration, only used when there is no big ball to swing around")]
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

        // the triangle that marks the ball you are driving
        private PlayerMarker marker;

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
            CreateMarker();
        }

        // the arrow drawn on the ball you are driving
        private void CreateMarker()
        {
            if (marker != null)
            {
                return;
            }

            var go = new GameObject("PlayerMarker");
            marker = go.AddComponent<PlayerMarker>();
        }

        private void LateUpdate()
        {
            if (marker == null)
            {
                CreateMarker();
            }

            marker.Follow(roster.Main, aim);
        }

        //give Inspector Parameters to PlayerActions??PlayerMerge
        private void SyncTools()
        {
            PlayerTuning snapshot = CaptureTuning();
            actions.Configure(roster, SpawnBall, snapshot, name);
            merge.Configure(roster, snapshot);
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

            // 4. only the main ball takes input, every other ball follows
            SyncSubDrive();

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

            // 6. drive every ball that no sub-ball script owns(main ball)
            MovePlayerBalls(input);

            // 7. merge:  running pulls first, then look for new 
            merge.Tick(Time.fixedDeltaTime);
        }

        // the main ball takes input, every other ball follows through its own script
        private void SyncSubDrive()
        {
            SizeController main = roster.Main;
            int slot = 0;

            for (int i = 0; i < roster.Count; i++)
            {
                SizeController ball = roster.Balls[i];
                if (ball == null || ball.IsDead)
                {
                    continue;
                }

                PlayerSubBall sub = ball.GetComponent<PlayerSubBall>();

                if (ball == main)
                {
                    if (sub != null && !merge.IsMerging(ball))
                    {
                        sub.enabled = false;
                    }
                    continue;
                }

                if (merge.IsMerging(ball))
                {
                    continue;   // the merge pull owns this ball right now
                }

                slot++;
                if (sub == null)
                {
                    sub = ball.gameObject.AddComponent<PlayerSubBall>();
                    sub.Initialize(this, actions.SlotAngleFor(slot));
                }
                else if (!sub.enabled)
                {
                    sub.enabled = true;
                }

                // the yoyo lives on a runtime component, so push the tunables down to it
                sub.ApplyTuning(subHomeStrength, subHomeDamping, subMaxDriftSpeed, subFollowAcceleration);
            }
        }

        private void MovePlayerBalls(Vector2 input)
        {
            for (int i = 0; i < roster.Count; i++)
            {
                SizeController ball = roster.Balls[i];
                if (ball == null || ball.IsDead)
                {
                    continue;
                }

                var sub = ball.GetComponent<PlayerSubBall>();
                if (sub != null && sub.enabled)
                {
                    continue;
                }

                MoveBall(ball, input);
            }
        }

        private void MoveBall(SizeController ball, Vector2 input)
        {
            var rb = ball.GetComponent<Rigidbody2D>();
            if (rb == null)
            {
                return;
            }
                
            // No input does not mean "stop dead": the ball keeps gliding and bleeds the speed
            // off at deceleration, which is also what makes a split launch fade out.
            bool pushing = input.sqrMagnitude > 0.0001f;
            Vector2 targetVelocity = pushing
                ? input * (moveSpeed * ball.MoveSpeedFactor)
                : Vector2.zero;

            float rate = pushing ? acceleration : deceleration;
            Vector2 current = rb.linearVelocity;

            Vector2 next = rate <= 0f
                ? targetVelocity
                : Vector2.MoveTowards(current, targetVelocity, rate * Time.fixedDeltaTime);

            if (next.sqrMagnitude > maxSpeedHardLimit * maxSpeedHardLimit)
            {
                next = Vector2.ClampMagnitude(next, maxSpeedHardLimit);
            }

            rb.linearVelocity = next;

            if (ArenaManager.Instance != null)
            {
                ArenaManager.Instance.Constrain(rb, ball.Radius);
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
            get
            {
                Vector2 input = Vector2.ClampMagnitude(moveInput, 1f);
                if (input.magnitude < inputDeadZone)
                {
                    return Vector2.zero;
                }
                return input * moveSpeed;
            }
        }

        public SizeController MainBall { get { return roster.Main; } }

        public int BallCount { get { return roster.Count; } }

        public IReadOnlyList<SizeController> Balls { get { return roster.Balls; } }

        public float TotalMass { get { return roster.TotalMass; } }
    }
}
```

### 5.7 `Assets/Scripts/Player/PlayerMerge.cs`

```csharp
using System.Collections.Generic;
using UnityEngine;

namespace QiuQiu
{
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
            public Vector2 startPosition;
            public Vector3 startScale;
            public float elapsed;   //time
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

        //  finish the running pull first, then look for a new pair
        public void Tick(float deltaTime)
        {
            PushApart(deltaTime);   // cells are solid until the cooldown is over
            UpdateJobs(deltaTime);
            if (jobs.Count == 0)
            {
                Detect();
            }
        }

        // Agar.io makes the cells of one player solid bodies: while they are not allowed
        // to merge yet, two overlapping cells push each other out of the way instead of
        // passing through. This is pushAwayCollidingCells() from owenashurst/agar.io-clone
        // (src/server/map/player.js): every overlapping pair is moved apart along the
        // vector between the two centres. Nothing else keeps a split group from stacking.
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

                for (int j = i + 1; j < roster.Count; j++)
                {
                    SizeController b = roster.Balls[j];
                    if (b == null || b.IsDead || b.IsEjecting || merging.Contains(b)) continue;

                    // both are allowed to melt together, so let them overlap on purpose
                    if (CanMerge(a) && CanMerge(b)) continue;

                    Vector2 offset = (Vector2)b.transform.position - (Vector2)a.transform.position;
                    float distance = offset.magnitude;
                    float overlap = a.Radius + b.Radius - distance;
                    if (overlap <= 0f) continue;

                    // two cells dropped on the exact same spot still have to separate
                    Vector2 direction = distance > 0.0001f ? offset / distance : Vector2.up;

                    float push = Mathf.Min(overlap * 0.5f, maxStep);
                    Nudge(a, -direction * push);
                    Nudge(b, direction * push);
                }
            }
        }

        private void Nudge(SizeController ball, Vector2 delta)
        {
            Rigidbody2D body = ball.GetComponent<Rigidbody2D>();
            if (body == null)
            {
                return;
            }

            body.position += delta;

            if (ArenaManager.Instance != null)
            {
                ArenaManager.Instance.Constrain(body, ball.Radius);
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
                { keeper = a; small = b; }
            }
            else if (b == roster.Main)
            {
                { keeper = b; small = a; }
            }
            //see who big
            else if (a.Mass >= b.Mass)
            {
                { keeper = a; small = b; }
            }
            else
            {
                { keeper = b; small = a; }
            }

            // the small ball stops following  and starts the pull
            PlayerSubBall sub = small.GetComponent<PlayerSubBall>();
            if (sub != null)
            {
                sub.enabled = false;
            }   
            // colliders off while pulled
            Collider2D[] colliders = small.GetComponentsInChildren<Collider2D>();
            for (int i = 0; i < colliders.Length; i++) colliders[i].enabled = false;

            merging.Add(small);
            jobs.Add(new Job
            {
                small = small,
                keeper = keeper,
                startPosition = small.transform.position,
                startScale = small.transform.localScale,
                elapsed = 0f
            });

        }

        private void UpdateJobs(float deltaTime)
        {
            for (int i = jobs.Count - 1; i >= 0; i--)
            {
                Job job = jobs[i];

                // either side may be eaten mid-pull: give up instead of pushing
                if (job.small == null || job.keeper == null || job.small.IsDead || job.keeper.IsDead)
                {
                    Abort(job);
                    jobs.RemoveAt(i);
                    continue;
                }

                job.elapsed += deltaTime;
                float t = Mathf.Clamp01(job.elapsed / Mathf.Max(0.01f, tuning.mergePullDuration));
                float eased = Mathf.Pow(t, tuning.mergePullEase);   // slow first, then snap in

                Rigidbody2D body = job.small.GetComponent<Rigidbody2D>();
                if (body != null)
                {
                    body.MovePosition(Vector2.Lerp(job.startPosition, job.keeper.transform.position, eased));
                }

                job.small.transform.localScale = Vector3.Lerp(job.startScale, Vector3.zero, eased);

                if (t < 1f) continue;

                jobs.RemoveAt(i);

                float gain = job.small.Mass;
                float keeperBefore = job.keeper.Mass;
                job.keeper.AddMass(gain);
                job.small.Die(job.keeper);
            }
        }

        //for  Mergeing but have AI eat the main ball to remove the action
        private void Abort(Job job)
        {
            SizeController small = job.small;
            if (small == null)
            {
                return;
            }

            merging.Remove(small);

            Rigidbody2D body = small.GetComponent<Rigidbody2D>();
            if (body != null)
            {
                body.position = job.startPosition;
                body.linearVelocity = Vector2.zero;
            }

            small.ApplySizeNow();

            PlayerSubBall sub = small.GetComponent<PlayerSubBall>();
            if (sub != null)
            {
                sub.enabled = true;
            }

            Collider2D[] colliders = small.GetComponentsInChildren<Collider2D>(true);
            for (int i = 0; i < colliders.Length; i++)
            {
                colliders[i].enabled = true;
            }
        }
    }
}
```

### 5.8 `Assets/Scripts/Player/PlayerSubBall.cs`

```csharp
using UnityEngine;

namespace QiuQiu
{
    // Drives one sub ball of the player.
    //
    // There is no real "main ball" here either: every cell steers towards the same aim the
    // player gives, so the whole group flies together. What a split piece adds on top of
    // that is a local offset from the big ball, and that offset behaves like a yoyo:
    //
    //   split  ->  the piece is thrown outwards and keeps its speed
    //   flight ->  a spring pulls it back towards the big ball while damping eats the throw
    //   rest   ->  it sits right next to the big ball (centre distance = r1 + r2) and waits
    //   merge  ->  PlayerMerge animates the swallow once the cooldown is over
    //
    // The spring works on the offset, not on the absolute velocity, so the piece never stops
    // following the player while it swings back.
    public class PlayerSubBall : MonoBehaviour
    {
        [Header("Yoyo")]
        [Tooltip("Stiffness of the pull back towards the big ball. Higher = shorter throw")]
        [SerializeField] private float homeStrength = 7f;

        [Tooltip("How fast the throw-off speed dies out. Higher = less bouncing back and forth")]
        [SerializeField] private float homeDamping = 2.4f;

        [Tooltip("Speed cap for the swinging motion itself, not counting the player's own movement")]
        [SerializeField] private float maxDriftSpeed = 26f;

        [Header("Follow")]
        [Tooltip("Only used when there is no leader to swing around (it was eaten this frame)")]
        [SerializeField] private float followAcceleration = 30f;

        private PlayerController leaderController;
        private SizeController self;
        private Rigidbody2D rb;

        public void Initialize(PlayerController controller, float slotAngleDegrees)
        {
            leaderController = controller;
        }

        // The component is added at runtime, so the values live on PlayerController and are
        // pushed down here every frame. That keeps everything tunable from one Inspector.
        public void ApplyTuning(float strength, float damping, float maxDrift, float followAccel)
        {
            homeStrength = strength;
            homeDamping = damping;
            maxDriftSpeed = maxDrift;
            followAcceleration = followAccel;
        }

        private void Awake()
        {
            rb = GetComponent<Rigidbody2D>();
            self = GetComponent<SizeController>();
        }

        private void FixedUpdate()
        {
            if (self == null || self.IsDead)
            {
                return;
            }

            // a piece that is still sliding out of its parent is driven by SplitEject
            if (self.IsEjecting)
            {
                return;
            }

            // no controller means nobody drives this ball any more, so leave it alone
            if (leaderController == null)
            {
                leaderController = PlayerController.Active;
                if (leaderController == null)
                {
                    return;
                }
            }

            // the merge pull owns the ball while it is being swallowed
            if (leaderController.IsMerging(self))
            {
                return;
            }

            Vector2 inputVelocity = leaderController.InputVelocity;
            SizeController leader = leaderController.MainBall;

            if (leader == null || leader == self)
            {
                FollowInput(inputVelocity);
                return;
            }

            SwingAroundLeader(leader, inputVelocity);
        }

        // The yoyo itself: keep whatever relative speed the ball has (that is the throw, or
        // the leftovers of it), steer that relative speed towards the resting spot beside the
        // big ball, and let damping bleed it off. Because the player's own speed is added
        // back at the end, the piece keeps flying with the group the whole time.
        private void SwingAroundLeader(SizeController leader, Vector2 inputVelocity)
        {
            float restDistance = leader.Radius + self.Radius;
            Vector2 offset = rb.position - (Vector2)leader.transform.position;
            Vector2 distance = offset.sqrMagnitude > 0.0001f ? offset.normalized : Vector2.zero;

            if (distance == Vector2.zero)
            {
                // exactly on top of the leader: pick the direction we are already flying in
                Vector2 flying = rb.linearVelocity - inputVelocity;
                distance = flying.sqrMagnitude > 0.0001f ? flying.normalized : Vector2.right;
            }

            Vector2 restingSpot = distance * restDistance;
            Vector2 drift = rb.linearVelocity - inputVelocity;

            Vector2 accel = (restingSpot - offset) * homeStrength - drift * homeDamping;

            Vector2 nextDrift = drift + accel * Time.fixedDeltaTime;
            if (nextDrift.sqrMagnitude > maxDriftSpeed * maxDriftSpeed)
            {
                nextDrift = Vector2.ClampMagnitude(nextDrift, maxDriftSpeed);
            }

            rb.linearVelocity = inputVelocity + nextDrift;

            Constrain();
        }

        // fallback: no leader to swing around, just move like everybody else
        private void FollowInput(Vector2 inputVelocity)
        {
            rb.linearVelocity = Vector2.MoveTowards(
                rb.linearVelocity,
                inputVelocity,
                followAcceleration * Time.fixedDeltaTime);

            Constrain();
        }

        // no wall colliders, the arena edge is a soft clamp
        private void Constrain()
        {
            if (ArenaManager.Instance != null)
            {
                ArenaManager.Instance.Constrain(rb, self.Radius);
            }
        }
    }
}
```

### 5.9 `Assets/Scripts/Player/PlayerRoster.cs`

```csharp
using System.Collections.Generic;

namespace QiuQiu
{
    // owns the balls the player controls and remembers which one is the main ball.
    public class PlayerRoster
    {
        private readonly List<SizeController> balls = new List<SizeController>(8);
        private SizeController main;

        public IReadOnlyList<SizeController> Balls { get { return balls; } }

        public int Count { get { return balls.Count; } }

        public SizeController Main { get { return main; } }

        public float TotalMass
        {
            get
            {
                float total = 0f;
                for (int i = 0; i < balls.Count; i++)
                {
                    SizeController ball = balls[i];
                    if (ball != null && !ball.IsDead)
                    {
                        total += ball.Mass;
                    }
                }
                return total;
            }
        }

        public bool Contains(SizeController ball)
        {
            return ball != null && balls.Contains(ball);
        }

        public void Add(SizeController ball)
        {
            if (ball == null || ball.IsDead || balls.Contains(ball))
            {
                return;
            }

                balls.Add(ball);

            // control stays on the ball you already had: a new piece never steals it,
            // otherwise the camera/triangle would hop between cells every time one grows
            if (main == null)
            {
                main = ball;
            }
        }

        public bool Remove(SizeController ball)
        {
            if (ball == null)
            {
                return false;
            }
            bool removed = balls.Remove(ball);
            if (main == ball)
            {
                main = Largest();
            }

            return removed;
        }

        public void Clear()
        {
            balls.Clear();
            main = null;
        }

        // you drive the biggest cell, like Agar. The switch is not "pick the max every
        // frame": two halves of a split are the same mass and would swap the control back
        // and forth, so another cell has to be clearly heavier before it takes over.
        private const float TakeOverRatio = 1.02f;

        public void RefreshMain()
        {
            if (balls.Count == 0)
            {
                main = null;
                return;
            }

            SizeController best = Largest();

            if (main == null || main.IsDead || !balls.Contains(main))
            {
                main = best;
                return;
            }

            if (best != null && best != main && best.Mass > main.Mass * TakeOverRatio)
            {
                main = best;
            }
        }
        public SizeController Largest()
        {
            SizeController best = null;
            float bestMass = float.MinValue;

            for (int i = balls.Count - 1; i >= 0; i--)
            {
                SizeController ball = balls[i];
                if (ball == null)
                {
                    balls.RemoveAt(i);
                    continue;
                }
                if (ball.IsDead)
                {
                    continue;
                }
                if (ball.Mass > bestMass)
                {
                    bestMass = ball.Mass;
                    best = ball;
                }
            }

            return best;
        }

        // remove destroyed balls
        public void PruneDead(System.Action<SizeController> onDropped)
        {
            for (int i = balls.Count - 1; i >= 0; i--)
            {
                SizeController ball = balls[i];
                if (ball != null && !ball.IsDead)
                {
                    continue;
                }
                    
                    balls.RemoveAt(i);

                if (ball != null && onDropped != null)
                {
                    onDropped(ball);
                }
            }

            if (main == null || main.IsDead)
            {
                main = Largest();
            }
        }
    }
}
```

### 5.10 `Assets/Scripts/Player/SplitEject.cs`

```csharp
using UnityEngine;

namespace QiuQiu
{
    // A freshly split piece does NOT appear on the rim: it starts inside the parent, small,
    // then slides out while it grows to full size. That is what makes a split look like the
    // body pushing a piece out of itself instead of a second ball popping into place.
    public class SplitEject : MonoBehaviour
    {
        private SizeController size;
        private Rigidbody2D body;
        private SpriteRenderer shell;

        private Vector2 from;
        private Vector2 to;
        private float duration = 0.3f;
        private float launchSpeed;
        private float startRadius;
        private float endRadius;
        private float elapsed;
        private bool running;

        // origin: centre of the parent cell, target: where the two halves end up touching
        public void Begin(Vector3 origin, Vector3 target, float time, float speed)
        {
            size = GetComponent<SizeController>();
            body = GetComponent<Rigidbody2D>();
            shell = GetComponent<SpriteRenderer>();

            from = origin;
            to = target;
            duration = Mathf.Max(0.01f, time);
            launchSpeed = speed;
            endRadius = size != null ? size.Radius : 0.5f;
            startRadius = endRadius * 0.2f;
            elapsed = 0f;
            running = true;

            if (size != null)
            {
                size.SuppressVisual = true;   // this component owns the scale while it runs
                size.IsEjecting = true;
                size.SetInvulnerable(duration + 0.1f);   // nothing can eat it mid-slide
            }

            if (body != null)
            {
                body.bodyType = RigidbodyType2D.Kinematic;
                body.position = from;
                body.linearVelocity = Vector2.zero;
            }

            Apply(0f);
        }

        private void FixedUpdate()
        {
            if (!running)
            {
                return;
            }

            elapsed += Time.fixedDeltaTime;
            float t = Mathf.Clamp01(elapsed / duration);
            Apply(t);

            if (t >= 1f)
            {
                Finish();
            }
        }

        private void Apply(float t)
        {
            float eased = t * t * (3f - 2f * t);   // smoothstep: starts and stops gently

            if (body != null)
            {
                body.MovePosition(Vector2.Lerp(from, to, eased));
            }

            float radius = Mathf.Lerp(startRadius, endRadius, eased);
            transform.localScale = new Vector3(radius * 2f, radius * 2f, 1f);

            // grow through the same size->order rule, so the piece is never drawn on top of
            // a bigger cell while it is still small
            if (shell != null)
            {
                shell.sortingOrder = SizeController.SortingOrderForRadius(radius);
            }
        }

        private void Finish()
        {
            running = false;

            if (body != null)
            {
                body.bodyType = RigidbodyType2D.Dynamic;
                body.position = to;

                Vector2 dir = to - from;
                dir = dir.sqrMagnitude > 0.0001f ? dir.normalized : Vector2.right;
                body.linearVelocity = dir * launchSpeed;   // the launch speed only kicks in at the end
            }

            if (size != null)
            {
                size.IsEjecting = false;
                size.SuppressVisual = false;
                size.ApplySizeNow();
            }

            Destroy(this);
        }
    }
}
```

### 5.11 `Assets/Scripts/Player/PlayerMarker.cs`

```csharp
using UnityEngine;

namespace QiuQiu
{
    // The arrow that sits on the ball you are driving.
    // There is no art for it in the project, so the triangle is built from code once and
    // reused. It is NOT parented to the ball: the ball scales with mass, and a child would
    // get squashed along with it.
    public class PlayerMarker : MonoBehaviour
    {
        [Header("Shape")]
        [SerializeField] private float sizeFactor = 0.5f;    // arrow size relative to the ball radius
        [SerializeField] private float edgeFactor = 1.08f;   // distance from the centre, >1 = outside the ball
        [SerializeField] private Color color = new Color(1f, 1f, 1f, 0.95f);

        private SpriteRenderer sprite;
        private Vector2 lastDirection = Vector2.right;

        private void Awake()
        {
            EnsureSprite();
        }

        // called every frame by PlayerController with the ball it is driving
        public void Follow(SizeController ball, Vector2 direction)
        {
            EnsureSprite();

            if (ball == null || ball.IsDead)
            {
                sprite.enabled = false;
                return;
            }

            sprite.enabled = true;

            if (direction.sqrMagnitude > 0.0001f)
            {
                lastDirection = direction.normalized;
            }
            else
            {
                direction = lastDirection;
            }

            float radius = ball.Radius;
            float size = Mathf.Max(0.35f, radius * sizeFactor);
            Vector3 offset = (Vector3)(lastDirection * (radius * edgeFactor));

            transform.position = ball.transform.position + offset;
            transform.rotation = Quaternion.Euler(0f, 0f, Mathf.Atan2(lastDirection.y, lastDirection.x) * Mathf.Rad2Deg);

            transform.localScale = new Vector3(size, size, 1f);

            // always above the ball it belongs to, and above anything it is covering
            sprite.sortingOrder = SizeController.SortingOrderForRadius(radius) + 200;
        }

        private void EnsureSprite()
        {
            if (sprite != null)
            {
                return;
            }

            sprite = GetComponent<SpriteRenderer>();
            if (sprite == null)
            {
                sprite = gameObject.AddComponent<SpriteRenderer>();
            }

            if (sprite.sprite == null)
            {
                sprite.sprite = BuildTriangle(64);
            }

            sprite.color = color;
        }

        // white triangle pointing along +X, 2x supersampled so the edge is not jagged
        private static Sprite BuildTriangle(int resolution)
        {
            var texture = new Texture2D(resolution, resolution, TextureFormat.RGBA32, false);
            texture.filterMode = FilterMode.Bilinear;

            Vector2 a = new Vector2(resolution * 0.90f, resolution * 0.5f);   // tip, right
            Vector2 b = new Vector2(resolution * 0.22f, resolution * 0.10f);  // bottom left
            Vector2 c = new Vector2(resolution * 0.22f, resolution * 0.90f);  // top left

            var pixels = new Color32[resolution * resolution];

            for (int y = 0; y < resolution; y++)
            {
                for (int x = 0; x < resolution; x++)
                {
                    int hits = 0;
                    for (int sy = 0; sy < 2; sy++)
                    {
                        for (int sx = 0; sx < 2; sx++)
                        {
                            Vector2 p = new Vector2(x + (sx + 0.5f) * 0.5f, y + (sy + 0.5f) * 0.5f);
                            if (Inside(p, a, b, c))
                            {
                                hits++;
                            }
                        }
                    }

                    byte alpha = (byte)(hits * 255 / 4);
                    pixels[y * resolution + x] = new Color32(255, 255, 255, alpha);
                }
            }

            texture.SetPixels32(pixels);
            texture.Apply();

            return Sprite.Create(texture, new Rect(0f, 0f, resolution, resolution), new Vector2(0.5f, 0.5f), resolution);
        }

        private static bool Inside(Vector2 p, Vector2 a, Vector2 b, Vector2 c)
        {
            float d1 = Cross(p - a, b - a);
            float d2 = Cross(p - b, c - b);
            float d3 = Cross(p - c, a - c);

            bool negative = d1 < 0f || d2 < 0f || d3 < 0f;
            bool positive = d1 > 0f || d2 > 0f || d3 > 0f;

            return !(negative && positive);
        }

        private static float Cross(Vector2 u, Vector2 v)
        {
            return u.x * v.y - u.y * v.x;
        }
    }
}
```

### 5.12 `Assets/Scripts/AI/AIController.cs`

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

### 5.13 `Assets/Scripts/World/Virus.cs`

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
                        : SpawnAiPiece(position, pieceMass, sprite, color);

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

        private static SizeController SpawnAiPiece(Vector3 position, float pieceMass, Sprite sprite, Color color)
        {
            GameObject go = BallFactory.CreateBall("AI_Burst", position, pieceMass, sprite, color, Team.Enemy);
            AIController ai = go.AddComponent<AIController>();
            ai.ApplyRandomProfile(Random.Range(0.9f, 1.25f), Random.Range(0.85f, 1.15f));
            return go.GetComponent<SizeController>();
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

### 5.14 `Assets/Scripts/World/VirusSpawner.cs`

```csharp
using System.Collections.Generic;
using UnityEngine;

namespace QiuQiu
{
    // Keeps the map stocked with viruses and builds the green spiky sprite at runtime,
    // so the project needs no new art asset.
    [DisallowMultipleComponent]
    public class VirusSpawner : MonoBehaviour
    {
        public static VirusSpawner Instance { get; private set; }

        [Header("Count")]
        [Tooltip("Viruses on the map at any time")]
        [SerializeField] private int virusCount = 30;
        [Tooltip("Hard cap, including the ones fired by feeding a virus")]
        [SerializeField] private int maxVirus = 50;
        [SerializeField] private bool fillOnStart = true;
        [SerializeField] private float checkInterval = 0.5f;
        [SerializeField] private int maxSpawnsPerTick = 6;

        [Header("Virus")]
        [SerializeField] private float virusMass = 120f;
        [SerializeField] private float radiusScale = 0.237f;
        [SerializeField] private Color virusColor = new Color(0.42f, 0.85f, 0.55f, 1f);
        [Tooltip("Fallback only: Virus.ApplyVisual overrides this at runtime with the size based order")]
        [SerializeField] private int sortingOrder = 15;

        [Header("Star Sprite")]
        [SerializeField] private int textureSize = 128;
        [SerializeField] private int starPoints = 16;
        [SerializeField, Range(0.3f, 0.95f)] private float starInnerRatio = 0.74f;

        [Header("Placement")]
        [SerializeField] private float borderMargin = 2f;
        [SerializeField] private float minDistanceFromBalls = 6f;
        [SerializeField] private float minDistanceBetweenViruses = 6f;
        [SerializeField] private int placementAttempts = 12;
        [SerializeField] private Transform virusParent;

        private Sprite starSprite;
        private float nextCheck;

        public int Count { get { return Virus.ActiveCount; } }
        public int VirusCount { get { return virusCount; } }
        public int MaxVirus { get { return maxVirus; } }

        private void Awake()
        {
            Instance = this;
            starSprite = BuildStarSprite();
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
            if (fillOnStart)
            {
                int missing = virusCount - Virus.ActiveCount;
                for (int i = 0; i < missing; i++)
                {
                    SpawnOne();
                }
            }
        }

        private void Update()
        {
            if (Time.time < nextCheck)
            {
                return;
            }
            nextCheck = Time.time + Mathf.Max(0.1f, checkInterval);

            int wanted = Mathf.Max(virusCount, 0);
            int missing = wanted - Virus.ActiveCount;
            if (missing <= 0)
            {
                return;
            }

            int budget = Mathf.Min(missing, Mathf.Max(1, maxSpawnsPerTick));
            for (int i = 0; i < budget; i++)
            {
                SpawnOne();
            }
        }

        public bool SpawnOne()
        {
            ArenaManager arena = ArenaManager.Instance;
            if (arena == null)
            {
                return false;
            }

            for (int attempt = 0; attempt < Mathf.Max(1, placementAttempts); attempt++)
            {
                Vector2 point = arena.GetRandomPointInside(borderMargin);
                if (!IsFarFromBalls(point) || !IsFarFromViruses(point))
                {
                    continue;
                }

                Create(point, Vector2.zero);
                return true;
            }

            return false;
        }

        // Called by a fed virus: a new virus flies off in the given direction.
        public Virus SpawnFrom(Virus source, Vector2 direction, float speed)
        {
            if (source == null)
            {
                return null;
            }

            if (Virus.ActiveCount >= maxVirus)
            {
                return null;   // cap reached, no more viruses
            }

            Vector2 dir = direction.sqrMagnitude > 0.0001f ? direction.normalized : Vector2.up;
            Vector3 position = source.transform.position + (Vector3)(dir * (source.Radius * 2f + 0.1f));

            ArenaManager arena = ArenaManager.Instance;
            if (arena != null)
            {
                Vector2 centre = arena.Center;
                Vector2 half = arena.HalfSize;
                float margin = source.Radius;
                position = new Vector3(
                    Mathf.Clamp(position.x, centre.x - half.x + margin, centre.x + half.x - margin),
                    Mathf.Clamp(position.y, centre.y - half.y + margin, centre.y + half.y - margin),
                    position.z);
            }

            Virus spawned = Create(position, dir * speed);
            return spawned;
        }

        private Virus Create(Vector2 position, Vector2 velocity)
        {
            GameObject go = new GameObject("Virus");
            go.transform.position = position;

            if (virusParent != null)
            {
                go.transform.SetParent(virusParent, true);
            }

            SpriteRenderer sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = starSprite;
            sr.color = virusColor;
            sr.sortingOrder = sortingOrder;

            Rigidbody2D rb = go.AddComponent<Rigidbody2D>();
            rb.bodyType = RigidbodyType2D.Dynamic;
            rb.gravityScale = 0f;
            rb.freezeRotation = true;
            rb.interpolation = RigidbodyInterpolation2D.Interpolate;
            rb.linearDamping = 0.9f;

            CircleCollider2D col = go.AddComponent<CircleCollider2D>();
            col.radius = 0.5f;
            col.isTrigger = true;   // a virus never pushes a ball around

            Virus virus = go.AddComponent<Virus>();
            virus.Configure(virusMass, radiusScale);

            if (velocity.sqrMagnitude > 0.0001f)
            {
                rb.linearVelocity = velocity;
            }

            return virus;
        }

        private bool IsFarFromBalls(Vector2 point)
        {
            IReadOnlyList<SizeController> balls = SizeController.Registry;
            for (int i = 0; i < balls.Count; i++)
            {
                SizeController ball = balls[i];
                if (ball == null || ball.IsDead)
                {
                    continue;
                }

                float gap = minDistanceFromBalls + ball.Radius;
                if (((Vector2)ball.transform.position - point).sqrMagnitude < gap * gap)
                {
                    return false;
                }
            }
            return true;
        }

        private bool IsFarFromViruses(Vector2 point)
        {
            IReadOnlyList<Virus> viruses = Virus.All;
            for (int i = 0; i < viruses.Count; i++)
            {
                Virus other = viruses[i];
                if (other == null)
                {
                    continue;
                }

                float gap = minDistanceBetweenViruses + other.Radius;
                if (((Vector2)other.transform.position - point).sqrMagnitude < gap * gap)
                {
                    return false;
                }
            }
            return true;
        }

        // spiky ring, drawn once. Cheap: it is the same sprite for every virus.
        private Sprite BuildStarSprite()
        {
            int size = Mathf.Clamp(textureSize, 32, 512);
            Texture2D texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
            texture.filterMode = FilterMode.Bilinear;

            float centre = (size - 1) * 0.5f;
            float outer = centre - 1f;
            float inner = outer * starInnerRatio;
            int points = Mathf.Max(3, starPoints);
            float wedge = Mathf.PI * 2f / points;

            Color[] pixels = new Color[size * size];
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float dx = x - centre;
                    float dy = y - centre;
                    float distance = Mathf.Sqrt(dx * dx + dy * dy);
                    float angle = Mathf.Atan2(dy, dx);
                    if (angle < 0f)
                    {
                        angle += Mathf.PI * 2f;
                    }

                    float phase = Mathf.Repeat(angle, wedge) / wedge;
                    float edge = Mathf.Lerp(inner, outer, Mathf.Abs(phase * 2f - 1f));
                    float alpha = Mathf.Clamp01((edge - distance) / 1.5f);
                    pixels[y * size + x] = new Color(1f, 1f, 1f, alpha);
                }
            }

            texture.SetPixels(pixels);
            texture.Apply(false, false);

            return Sprite.Create(texture, new Rect(0f, 0f, size, size), new Vector2(0.5f, 0.5f), size);
        }
    }
}
```

## 6. Inspector 参数设置说明

### SizeController（挂在每颗球上，11 颗场景球已写入）
| 字段 | 默认 | 说明 |
|---|---|---|
| `mass` | 10 | 初始质量（唯一质量来源） |
| `minMass` / `maxMass` | 1 / 100000 | 夹取范围 |
| `referenceMass` | 10 | 半径参考质量 |
| `radiusAtReferenceMass` | 0.75 | 参考质量下的半径 |
| `radiusScale` | 0（自动） | 手填后直接覆盖半径系数，`Radius = radiusScale * sqrt(mass)` |
| `eatMassRatio` | 1.25 | 吃掉对方需要的质量比（Agar clone 用 1.1，任务书要求 1.25） |
| `speedReferenceMass` | 60 | 低于此质量速度系数为 1 |
| `minSpeedFactor` | 0.4 | 最大质量时的速度下限 |
| `decayEnabled` | true | 质量衰减总开关 |
| `decayThreshold` | 500 | 超过才开始衰减 |
| `decayPercentPerSecond` | 0.0021 | 每秒衰减比例（0.21%，测试值） |
| `heavyDecayThreshold` | 5000 | 超过则进入加速衰减 |
| `heavyDecayMultiplier` | 2 | 加速衰减倍数 |
| `decayFloorMass` | 10 | 衰减不会低于此值 |

### Eater（每颗球）
| 字段 | 默认 | 说明 |
|---|---|---|
| `swallowDepth` | 1 | 1 = 目标圆心进入吞食者（Agar 规则）；0.5 = 必须陷进去一半；0 = 完全包住 |

### Virus（运行时由 VirusSpawner 创建）
| 字段 | 默认 | 说明 |
|---|---|---|
| `mass` | 120 | 病毒质量，也是"多大的球才能吃它"的门槛 |
| `radiusScale` | 0.237 | 病毒半径 = `radiusScale * sqrt(mass)` |
| `burstPieces` | 16 | 爆裂最大碎片数（受 16 上限约束） |
| `burstLaunchSpeed` | 9 | 碎片弹射速度 |
| `burstSpawnGap` | 0.05 | 碎片初始间距 |
| `burstCooldown` | 0.4 | 同一病毒两次爆裂的最小间隔 |
| `feedsToSplit` | 7 | 被喂够几个孢子后发射新病毒 |
| `feedPushFactor` | 0.25 | 每个孢子推动病毒的力度 |
| `maxFeedPush` | 3 | 推力上限 |
| `ejectSpeed` | 10 | 发射新病毒的速度 |

### VirusSpawner（场景根物体，已挂）
| 字段 | 默认 | 说明 |
|---|---|---|
| `virusCount` | 30 | 场上维持的病毒数（不足自动补） |
| `maxVirus` | 50 | 病毒总数上限（喂病毒发射时也受它限制） |
| `fillOnStart` | true | 开局补满 |
| `checkInterval` / `maxSpawnsPerTick` | 0.5 / 6 | 补满节流 |
| `virusMass` / `radiusScale` | 120 / 0.237 | 新病毒参数 |
| `virusColor` | (0.42, 0.85, 0.55) | 绿色 |
| `sortingOrder` | 15 | 高于球层（10），小球可藏在病毒后面 |
| `textureSize` / `starPoints` / `starInnerRatio` | 128 / 16 / 0.74 | 程序化星形贴图 |
| `borderMargin` | 2 | 离地图边界的最小距离 |
| `minDistanceFromBalls` / `minDistanceBetweenViruses` | 6 / 6 | 生成间距，避免生成在玩家身上 |
| `placementAttempts` | 12 | 选址重试次数 |

### PlayerController（PlayerRoot 上）
| 字段 | 场景值 | 说明 |
|---|---|---|
| `moveSpeed` / `maxSpeedHardLimit` | 5 / 12 | 基础速度与硬上限 |
| `acceleration` / `deceleration` | 60 / 6 | 加速与松手滑行；想更"滑"就把 `acceleration` 降到 15~25 |
| `maxBallCount` | 16 | 分身上限 |
| `splitMinMass` / `splitMassRatio` | 30 / 0.5 | 能分裂的最小质量与对半分 |
| `splitCooldown` / `splitLaunchSpeed` | 0.5 / 24 | 分裂冷却与扔出去的速度；飞多远由 `PlayerSubBall.homeStrength` 决定，`splitLaunchSpeed` 只给初速 |
| `aimAtMouse` | false | 打开后分裂/吐质量朝鼠标（网页版 Agar 的操作）；手机端保持 false |
| `subHomeStrength` | 7 | 分身被扔出去后被拉回大球的弹簧刚度。调大 = 扔得近；调小 = 扔得远、荡得久 |
| `subHomeDamping` | 2.4 | 阻尼，吃掉飞出去的动能。调大 = 收得干脆；调小 = 在隔壁来回晃几下 |
| `subMaxDriftSpeed` | 26 | 相对大球的速度上限，防止弹簧叠加后飞出屏幕 |
| `subFollowAcceleration` | 30 | 只在没有大球可绕时用的兜底跟随加速度 |
| `spitMassCost` / `spitFoodMass` | 12 / 9 | 吐出成本与孢子质量（成本 > 产出） |
| `minMassAfterSpit` | 20 | 吐完不能低于这个质量 |
| `spitCooldown` | 0.12 | 连吐间隔 |
| `mergeCooldown` / `mergeCooldownPerMass` / `mergeCooldownMax` | 4.5 / 0.002 / 8 | 合球冷却 = `4.5 + 0.002×质量`，封顶 8 s —— 对应"四五秒就融合" |
| `mergePullDuration` | 0.45 | 合球时小球被吸进大球、边缩边穿进去的动画时长（和冷却无关） |
| `pushApartSpeed` | 4 | 冷却没到时，自己分身之间每秒互相推开的距离；对应原项目 `PUSHING_AWAY_SPEED = 1.1`，设 0 就关掉推开 |
| `splitEjectDuration` | 0.3 | 分裂碎片从母球圆心长到满尺寸并滑到位的时间；0.3 s 是正常手感，测试时可临时调大观察 |

### AIController（每颗 AI 球）
| 字段 | 默认 | 说明 |
|---|---|---|
| `moveSpeed` / `acceleration` | 3.2 / 26 | 基础速度与加速度 |
| `scanRadius` / `scanInterval` | 8 / 0.2 | 感知半径与节流 |
| `avoidViruses` | true | 能吃病毒的大球是否避让 |
| `virusAvoidRadius` / `virusAvoidWeight` | 5 / 2 | 避让半径与权重 |

### PlayerSubBall（运行时组件，参数在 PlayerController 的 Sub Ball Yoyo 组里调）

| 参数 | 默认值 | 说明 |
|---|---|---|
| `homeStrength` | `7` | 拉回大球的弹簧刚度（= Inspector 的 `subHomeStrength`）。调大 = 扔得近、收得快；调小 = 扔得远、荡得更久 |
| `homeDamping` | `2.4` | 阻尼，吃掉飞出去的动能。调大 = 收得干脆；调小 = 会在隔壁来回晃几下 |
| `maxDriftSpeed` | `26` | 相对大球的最大速度上限，防止弹簧叠加后飞出屏幕 |
| `followAcceleration` | `30` | 只在没有大球可绕（大球这一帧被吃掉）时用的兜底跟随加速度 |

这些值由 `PlayerController.SyncSubDrive()` 每帧 `sub.ApplyTuning(...)` 推给运行时组件，所以**只要在 PlayerRoot 的 Inspector 里改**，不用去场景里找分身。
`splitLaunchSpeed = 24` + `subHomeStrength = 7` 这组值实测是：飞出约 13 个单位（接触距离才 6.7），然后 1~2 秒内荡回来停在隔壁。

### PlayerMarker（运行时由 PlayerController 自动创建，不在场景里）

| 参数 | 默认值 | 说明 |
|---|---|---|
| `sizeFactor` | `0.6` | 三角形边长相对球半径的比例 |
| `edgeFactor` | `0.72` | 三角形距离球心的位置（×球半径），越小越靠中间 |
| `color` | 白 95% | 三角形颜色 |

层级不用自己填：球和病毒的 `sortingOrder` 都由 `SizeController.SortingOrderForRadius(radius)` 算（`10 + round(radius × 8)`），三角固定是主球层级 +200。

### PlayerRoster（运行时组件，不在场景里）

| 参数 | 默认 | 说明 |
|---|---|---|
| `TakeOverRatio` | 1.02 | 主球活着时，别的球形必须比主球大 2% 才会接管操控；调成 1.0 就是"永远操控最大的"，调大就更不容易换 |


### 调参速查
- 想吃得更"容易"：把 `eatMassRatio` 调到 1.1，`swallowDepth` 调到 0.7。
- 想让整局更慢：降 `PlayerController.moveSpeed` 与 `AIController.moveSpeed`，同时把 `acceleration`/`deceleration` 一起降。
- 想要更多/更少的病毒：`VirusSpawner.virusCount`。
- 想关闭衰减：`SizeController.decayEnabled = false`。
- 想让合球更快：降 `mergeCooldown`（起始值）或 `mergeCooldownPerMass`。

---

## 7. 测试结果

全部在 Unity Play 模式实测（`editor.playmode start` → 等 domain reload → `unity_eval` 读运行时状态），不是"看代码觉得对"。

| # | 测试项 | 结果 | 实测数据 |
|---|---|---|---|
| 1 | 吃食物增加质量 | ✅ | 吃彩豆后球质量从 400 → 406.0（食物 1~2.5/颗） |
| 2 | 吃球获取质量 | ✅ | 200 质量球吞 20 质量球：`220.0`（精确 200+20，无重复计算） |
| 3 | 质量比门槛有效 | ✅ | 110 vs 90 重叠 3 秒，两球都存活；把大球提到 120（≥90×1.25）后小球立刻被吃，大球 120→167.8（AI 互吃共享 0.5，故 +45） |
| 4 | 覆盖深度判定 | ✅ | 大球半径 3.35、小球半径 1.06，圆心距 4.05（相切重叠但不覆盖）：小球存活、大球质量不变；移到圆心上：立即被吃 |
| 5 | 分裂 1→2→4→8→16 | ✅ | 主球 800 质量连按 6 次：`2 / 4 / 8 / 16 / 16 / 16`，总质量恒为 `800.0`，主球质量 `400 / 200 / 100 / 50` |
| 6 | 16 上限 | ✅ | 第 5、6 次被拒绝（`balls=16` 不变） |
| 7 | 吐出质量扣质量 | ✅ | 400 → 388（`spitMassCost = 12`），场上出现质量 9 的孢子 |
| 8 | 能吃回自己吐的质量 | ✅ | 把球移到孢子上：孢子消失，质量 428.5 → 438.9 |
| 9 | 病毒爆裂 | ✅ | 单球 400 质量压病毒 → **16 颗，每颗 25.0**（400/16，总质量守恒，碎片均匀散开） |
| 10 | 接近上限时碎片减少 | ✅ | 12 颗球压病毒 → 算出 5 片（`16-12+1`），结果正好 **16 颗**，每片 80 |
| 11 | 满 16 颗时吃病毒 | ✅ | 仍是 16 颗，只有质量增加：主球 400 → 525.1（+120 病毒质量），不产生新分身 |
| 12 | 小球不被病毒爆裂 | ✅ | 爆裂条件 `ball.Mass > virus.Mass(120)`，小于 120 的球重叠病毒无任何反应 |
| 13 | 喂 7 次发射新病毒 | ✅ | 病毒 `storedMass` 累加、位置被推动（+1.0）；喂满 7 次后场上病毒 `30 → 31`，`feeds` 归零，新病毒带速度飞出 |
| 14 | 病毒数量上限 | ✅（参数） | `maxVirus = 50` 在 `VirusSpawner.SpawnFrom` 里判断，实测未触顶（只用 30/31 验证） |
| 15 | 大球比小球慢 | ✅ | `MoveSpeedFactor`：质量 10 → 1.000，200 → 0.786，1000 → 0.570，5000 → 0.413（下限 0.4）；半径 1000 → 7.50 |
| 16 | 自然衰减 | ✅ | 模拟 10 s：600 → 587.53（理论 587.53，完全一致）；480（低于 500）→ 480 不衰减；6000 → 5753.22（加速衰减×2）；夹取到下限 10.00 |
| 17 | 合球冷却按质量 | ✅ | `CooldownFor`：20 → 30.4 s，100 → 32 s，500 → 40 s，2000 → 60 s（封顶），5000 → 60 s |
| 18 | 合球正确合并质量 | ✅ | 两颗 120 质量球冷却置零后相撞：球数 11 → 10，总质量 637.9 不变，出现一颗 **240.0** 的球 |
| 19 | 分裂弹射冲量 | ✅ | 新球 `linearVelocity = dir * splitLaunchSpeed`（14），实测分裂后会自己飞出去再被减速 |
| 20 | 移动惯性 | ✅ | 松手不再瞬间停住：`MoveBall` 用 `deceleration = 6` 慢慢降到 0（之前是 `linearVelocity = 0` 硬停） |
| 21 | AI 会动、会避病毒 | ✅ | 10 个 AI 全部在移动，平均速度 3.16（基础 2.3×追击系数）；大球触发病毒前会被规避方向推开；AI 也会被病毒炸开（日志 `[virus] burst … into 16 cells`） |
| 22 | 质量不重复、不丢失、不为负 | ✅ | 采样全场 18 个球：`badMass = 0`；吃球 +20 精确；合球 240 精确；衰减严格按指数公式 |
| 23 | 编译与控制台 | ✅ | `compilationFailed: false`，`consoleErrors: 0`（那 9 条 USS 是 2D Animation 包的编辑器皮肤错误，与游戏无关） |
| 24 | 分身之间不会互相穿过 | ✅ | 8 个分身两两测距：`dist` 全部等于 `rSum`（例 `dist=3.37 rSum=3.37 overlap=0.00`），没有任何一对重叠 —— 冷却未到时是实体，会互相顶住 |
| 25 | 合并时才慢慢穿进去 | ✅ | 把两颗球的冷却清零后：小球 `scale 6.98 → 3.20` 且圆心从 `x=0` 滑到 `x=6.61` 进入大球（大球在 `x=9.99`），2.5 s 动画期间质量不变；动画结束球数 2 → 1，总质量 457 → 461.23（只多了吃到的食物） |
| 26 | 合并冷却清零后能全部合回去 | ✅ | 8 个分身全部清零冷却：3 s 后 `8 → 1`，质量从 `405.18` 变 `407.04`（差值来自食物），没有丢质量、没有负数 |
| 27 | 三角指向跟随主球 | ✅ | `markers=1`，位置在主球边缘 `radius×0.72` 处（`mainR=6.44 expectedOffset=4.64 actual=4.64`），层级 = 主球 +200（`262 = 62 + 200`） |
| 28 | 主球不因分裂/变大而换手 | ✅ | 主球 1200 分成 2 颗后，把另一颗强行设成 3000 质量：主球 instanceId 仍是 `48214`，三角仍跟原主球（`markerDistToMain=5.14`，到 3000 那颗 `6.53`） |
| 29 | 层级按体积：大球盖病毒 | ✅ | 球半径 7.12 → `order 67`，病毒半径 2.60 → `order 31`，球在病毒之上 |
| 30 | 层级按体积：小球被病毒盖 | ✅ | 球半径 1.84 → `order 25` < 病毒 `31`，小球被病毒挡住（可藏身） |
| 31 | 操控切换到最大的分身 | ✅ | 主球 215.6、另一颗 188：自动切到 215.6 那颗（>1.02 倍）；把那颗缩到 50 后主球不再换 |
| 32 | 所有分身一起吐质量 | ✅ | 2 颗球（200 / 218.6）各吐一次：场上孢子 +2，两颗分别扣 12（200→188、218.6→206.6） |
| 33 | 三角落在球体之外 | ✅ | 三角到球心 3.76 > 主球半径 3.48（`edgeFactor = 1.08`），截图 `eject_mid3.png` 可见三角尖完全在球外 |
| 34 | 分身穿出动画 | ✅ | 分裂瞬间碎片 `scale = 1.34`（满尺寸 6.71 的 20%）且圆心与母球重合（`dist = 0.00`）；动画结束后 `scale = 6.71`、位置在 `2×Radius` 处、`bodyType` 恢复 `Dynamic` |
| 35 | 分裂扔出去再慢慢收回（溜溜球） | ✅ | 慢放采样：分裂后 0.75 s 两球距离 **13.38**（接触距离只有 6.72），之后 9.12 → 7.29 → 6.77，最终停在大球隔壁 |
| 36 | 靠拢后四五秒才融合 | ✅ | t=72.05 分裂 → t=78.35 合成一颗，总质量 401.0 不变（冷却 4.9 s + 0.45 s 动画 + 靠拢时间） |
| 37 | 所有分身一起吐 | ✅ | 4 颗球吐一次：场上孢子 +4，总质量 836.8 → 788.8（每颗扣 12） |
| 38 | 滑出中的碎片不会吐 | ✅ | 刚分裂、仍在 `IsEjecting` 时按吐球只有已就位的那 1 颗吐；动画结束后 4 颗全吐 |

截图：`Screenshots/agar_play.png`（8 个分身 + 病毒 + 彩豆 + HUD）、`Screenshots/pushapart.png`（16 个分身互相顶住，无重叠）、`Screenshots/marker.png`（主球上的白色三角指向）、`Screenshots/eject_mid3.png`（分裂碎片从身体里长出来、三角在球外）、`Screenshots/yoyo_throw2.png`（两个对半的球并排，三角在球外）。

---

## 8. 尚未解决的问题

1. **地图仍是 60×60 矩形**（`ArenaManager.rectangleSize`），Agar 是圆形 5000×5000。任务书说"不要未经分析就改成圆形"，所以保留；半径/边界约束逻辑没动。
2. **帧率与对象数**：病毒爆裂会瞬间产生大量对象（实测 AI 互相炸开后场上控制器从 18 涨到 58）。没有做对象池，极端情况（10 个 AI 同时炸）会有瞬时 GC。如果要优化，最直接的是给 `Virus` 碎片也套 `FoodPool` 那种池。
3. **AI 不会主动用病毒做攻击**：现在只会躲；"用孢子推病毒砸人""躲到病毒后面避敌"这两条只有被动效果（小球躲在病毒后不会被大球直线追上），没有显式 AI 决策。
4. **`AiVsAiMassShare = 0.5`**：AI 之间互吃只拿一半质量（原项目遗留设定）。Agar 是 100%，要完全一致就把 `GameSettings.AiVsAiMassShare` 改成 1。
5. **质量上限**：`maxMass = 100000`，Agar 是 40000 体重分。没做全局硬上限，也没做"达到上限后不再增长"的提示。
6. **喂病毒的方向**：目前朝"孢子飞来的反方向"发射（谁喂就朝谁飞）。Agar 的社群资料说法不完全一致，这是可调的设计选择。
7. **玩家球之间不吞噬**：`Eater` 里 `Player vs Player` 直接 return（单机只有一个玩家，暂时无意义）；做多人时要去掉这条。
8. **没有眼睛/名字**：上一版加过 `BallEyes`，随回退一起消失了。本次按任务书只做机制；「哪个球是我」现在由 `PlayerMarker` 的三角指向解决，但球本身仍然没有眼睛和昵称。
9. **`agar.im` 两个页面抓不到正文**，所以"喂 7 次""30 秒合球""0.21%/s"这些数字只有任务书这一个来源，不是官方标准。
10. **合球动画是刻意的差异**：`owenashurst/agar.io-clone` 里 `mergeCollidingCells()` 是瞬间 `A.addMass(B.mass); B = null`，没有任何动画；本项目的 0.45 s 缩进动画是按需求加的，不是原项目行为。
11. **`SplitEject` 用的是 Kinematic 位移**：动画期间关掉物理，结束才恢复 `Dynamic`。如果以后要给碎片加碰撞反应，需要改成用速度驱动而不是 `MovePosition`。
12. **没有真正的主控球**：所有球都朝同一个 `aim` 走，`MainBall` 只用于三角指向和"大的先分"的顺序。`TakeOverRatio = 1.02` 只是让指向锚点别在等大分身之间乱跳，不是"操控权"。
13. **溜溜球的弹簧参数是手调的**：`owenashurst/agar.io-clone` 里没有回拉弹簧（它只做 `pushAwayCollidingCells` 每帧推 1.1），所以 `homeStrength`/`homeDamping`/`maxDriftSpeed` 三个值是按观感调的，不是原项目数值。
