# 重构计划 · 球球大作战（D:\MET4337_EA）

> 状态：**只读分析完成，未改动任何代码**。等你确认清单与 6 个决策点后再开工。
> Unity 6000.3.5f2 ｜ 自制脚本 25 个 / 6269 行 ｜ 参考风格来源：C:\Users\miche\Desktop\EA\ReferenceScripts（24 个文件 / 4936 行）

---

## 0. 我读了什么

| 范围 | 内容 |
|---|---|
| 参考代码 | 24 个 .cs 全部（GBK 编码，已逐行读；另做了量化统计） |
| 自制代码 | Assets\Scripts 下 25 个 .cs 全部读完 |
| 场景数据 | MainMenu.unity、LevelScene.unity 的组件绑定、Build Settings、GUID |
| 运行验证 | 进过一次 Play Mode（12 秒），读取时运行时状态与 Editor.log |
| 第三方 | Assets\SimpleInput（39 个脚本）——核查引用数为 0 |
| 版本控制 | .git 仓库存在（HEAD `af22b0b`），git 命令不在 PATH，但可用 GitHubDesktop 自带的 git.exe |

---

## 1. 参考代码的风格（可执行版）

从 24 个文件里量化出来的硬规则，重构后按这些验收：

1. **零 XML 文档**。241 个方法里 `/// <summary>` 一条都没有；类声明下一行直接是字段或 enum。
2. **只写"为什么"的 `//` 注释**，密度 ~5.6%；允许口语化中英混写、允许画 ASCII 图。
3. **分区用行注释**，如 `//PlayerMove()`、`//Submachine`、`// === 阶段切换 ===`；`#region` 出现 0 次。
4. **配置字段是 `public`**（真实 `[SerializeField]` 全项目只有 2 处）；`[Header]` 只给成组的配置块，`[Tooltip]` 只有 2 处。
5. **方法体一律 `{ }`**；全项目 `=>` 只有 8 处（3 个方法表达式体 + 4 个必须传委托的 lambda）。
6. **方法 10~15 行**（241 个方法均值 14.8 / 中位 11 / 66% ≤15），硬上限 45 行（只有 SpawnEnemy 121 行那种特例）。
7. **单例**：`public static X instance;` + Awake 里 4 行 null 判断 + DontDestroyOnLoad；OnDestroy 置空。
8. **跨系统直调**：`ScoreManager.instance.AddScore(...)`、`AudioManager.PlayAudio("...")`；无事件总线、无 DI、无抽象基类（`abstract/virtual/override` 各 0 次）。
9. **空引用防护是一行早退**：`if (x == null) return;`（15 处），不加大括号。
10. **类规模** 平均 206 行；玩法核心 500~713 行；管理/UI 100~210 行；没有 Util/Helper/Factory 类。

**明确不照抄的**：整方法复制（Boss1↔Boss2 55% 逐字相同）、省大括号导致的 bug、空 `Start(){}` 桩、魔法字符串、双大括号 `{ { } }`、拼写错误。

---

## 2. 体检结论

### 2.1 规模分布

| 类别 | 文件数 | 行数 |
|---|---|---|
| 玩法核心 | 5（PlayerController 923 / GameManager 504 / AIController 446 / CameraFollow 384 / ArenaManager 305） | 2562 |
| 编辑器工具 | 7 | 1904 |
| UI | 5 | 677 |
| 其余 Core/Food/Player | 8 | 1126 |
| **合计** | **25** | **6269** |

风格量化：`/// <summary>` 176 个、`///` 行 600 行、`=>` 44 处、`[SerializeField]` 152 处、`[Tooltip]` 127 处、`[Header]` 50 处、`Team == 0/1` 魔法比较 9 处。

### 2.2 已确认的问题（按严重度）

| # | 位置 | 问题 |
|---|---|---|
| P1 | Assets\Scripts\Core\Eater.cs:26,50-53 | `logEating` 默认 `true`，**每次吞噬都打一条带完整调用栈的日志**；实测 12 秒内日志已被 `[吞噬] AI_09(150) 吃掉 Player(60)` 刷屏 |
| P2 | Assets\Scripts\Food\FoodSpawner.cs:106,118,132 | `CurrentFoodCount` 与 `Edible.ActiveCount` **两套计数互相覆盖**（`++` 之后又被 `=` 覆盖），食物上限会漂移 |
| P3 | Assets\Scripts\Core\GameManager.cs:148,182 | `playerTuning` 捕获后**从未被读取**；连带 PlayerController 里 22 字段的 `PlayerTuning` 快照类、`CaptureTuning`、`ApplyTuning`、`s_PendingTuning`、`SpawnAt(tuning)` 全部是死链（`SpawnAt` 0 调用） |
| P4 | Assets\Scripts\Core\SizeController.cs:48,91-144 | `MassChanged` 事件 **invoke 4 次、订阅 0 次**（注释写"给 UI/AI 用（后续接入）"——从未接入） |
| P5 | Assets\Scripts\Core\GameManager.cs:137 | `GameOver` 事件 **订阅 0 次**（GameOverPanel 是被 `Show()` 直接调的） |
| P6 | Assets\Scripts\AI\AIController.cs:34-39 | 枚举成员用中文：`enum ChasePreference { 食物优先, 小球优先, 就近 }` |
| P7 | Assets\Scripts\AI\AIController.cs:185-320 | `Scan()` **136 行一个方法**（扫描+分类+状态切换+到达判定+换向），其中 chasePreference 三分支复制粘贴 |
| P8 | Assets\Scripts\Camera\CameraFollow.cs | 同一份 `player.Balls` 遍历在 5 个方法里各写一遍，每帧跑 4 次 |
| P9 | Assets\Scripts\Player\PlayerSubBall.cs | `ArenaManager.Instance.Constrain(...)` 同一段写 4 遍；另有只为兼容旧签名存在的 `Initialize(controller, leader, angle)` 重载（转手就调新签名） |
| P10 | Assets\Scripts\Core\DifficultyManager.cs | 整个类只做"读难度 + 打一行日志 + 两个默认关闭的开关"；`Instance` 与 `CurrentDifficulty` **0 引用** |
| P11 | Assets\Scripts\Editor\*.cs | 7 个脚本 1904 行只为一次性搭场景；`CreateText`×3、`GetBuiltinFont`×4、ColorBlock 8 字段×4、收尾三行×4 全是复制品 |
| P12 | Assets\Scripts\Editor\GameUIBuilder.cs | 菜单 **7 / 8 第一行就是 `PrototypeSetup.BuildArenaAIScene()`**，而现在 `PrototypeSetup.ScenePath` = `Assets/Scenes/LevelScene.unity` → **点一下就把正式关卡覆盖重建**（步骤 18 会整体删掉这组脚本） |
| P13 | Assets\Scripts\UI\HUDController.cs:113-131 | `ComputeRank` 与 `GameManager.ComputeRank` 完整重复，且同样用 `ball.Team == 0` |
| P14 | Assets\Scripts\Core\SizeController.cs:62 | `public static readonly List<SizeController> Registry`，全工程可直接 Add/Remove，无任何保护 |
| P15 | 5 个 UI 脚本 | 刷新节流三件套（`nextRefreshTime/lastValue/SetInt`）、Awake 找 GameManager、按名字找 Text 的写法逐行重复 |
| P16 | 全局 | `Team` 用 int 0/1，`Team == 0` 出现 9 次，语义靠注释 |

### 2.3 已确认的死代码（定义处引用数 = 1）

- `MainMenuController.PlayEasy` / `PlayNormal` / `PlayHard`（Assets\Scripts\UI\MainMenuController.cs:52-54）
- `PlayerController.SpawnAt(...)`（Assets\Scripts\Player\PlayerController.cs:699）
- `PlayerSubBall.SetSlotAngle(...)`、`PlayerSubBall.Initialize(controller, leader, angle)` 兼容重载
- `CameraFollow.TryGetPlayerBounds(out Bounds)`（Assets\Scripts\Camera\CameraFollow.cs:349）
- `DifficultyManager.Instance`、`DifficultyManager.CurrentDifficulty`
- `SizeController.MassChanged`、`GameManager.GameOver`（见 P4/P5）
- 整个 `Assets\SimpleInput\`（39 个 .cs + Example 场景 + Sprites）：**场景引用 0、预制体引用 0、代码引用 0**

### 2.4 场景数据问题（空引用）

用脚本遍历两个场景所有组件的 ObjectReference 字段：

| 场景 | 组件 | 空字段 | 影响 |
|---|---|---|---|
| MainMenu.unity | `MainMenuManager` (MainMenuController) | `difficultyLabel`、`bestMassLabel` | 主菜单不显示当前难度与最高分 |
| LevelScene.unity | `ResultMenu` (GameOverPanel) | `finalRankText` | 结算面板"最终排名"空白 |
| LevelScene.unity | `RespawnUI` (RespawnCountdownUI) | `hintText`、`gameManager`（有运行时兜底） | 复活提示缺文案 |
| LevelScene.unity | `RoundTimer` (RoundTimerUI) | `gameManager`（有运行时兜底） | 无实际影响 |
| LevelScene.unity | `Arena` / `FoodSpawner` | `centerOverride` / `foodParent`（都是可选字段） | 无影响 |

成因：`LevelScene.unity` 是未跟踪的新场景，UI 对象被手动改过名（`GameOverUI` → `ResultMenu`）；主菜单那两个标签在 git HEAD 里本来是有绑定的，是工作区这份场景丢的。

---

## 3. 目标形态

**删（7 + 1 + 39）**

| 删除对象 | 理由 |
|---|---|
| Assets\SimpleInput\**（39 脚本 + Example + Sprites） | 0 引用 |
| Assets\Scripts\Core\DifficultyManager.cs | 只打日志（P10），并入 GameSettings；需先从 LevelScene 移除组件 |
| **整个 Assets\Scripts\Editor\（7 个脚本 1904 行）** | 编译进 Assembly-CSharp-Editor，不进包、0 场景引用、无 InitializeOnLoad 钩子；**用户已决定全删**（2026-11-16，选 A） |

**拆（1 → 5）**：`PlayerController` 923 行拆成 `PlayerController`（对外接口 + 移动）、`PlayerInput`、`PlayerBalls`、`PlayerActions`、`PlayerMerge`，每个 ≤160 行。

**新增（11 个小文件，每个 40~160 行）**：`Team.cs`、`RoundTimer.cs`、`ScoreBoard.cs`、`PlayerInput.cs`、`PlayerBalls.cs`、`PlayerActions.cs`、`PlayerMerge.cs`、`RankUtil.cs`、`SceneFlow.cs`、`SaveData.cs`、`ArenaBorder.cs`。

**净结果**：25 个文件 → 28 个文件（18 个运行时代码 − 1 个 DifficultyManager + 11 个新拆分），单个文件从"平均 250 行 / 最大 923 行"变成"平均 110 行 / 最大约 300 行"，总行数预计 6269 → **约 3200 行**（其中删掉编辑器工具 1904 行）。

**注意**：删掉编辑器脚本后，两个场景的层级、物体数值、UI 绑定就没有"再生能力"了。已确认落盘的资源不受影响（`Assets\Sprites\Ball.png` 被 LevelScene 引用 17 次、`Assets\Input\Controls.inputactions` 都在）。以后要做正式 UI（你新加的 `Assets\UI\` 那套按钮/面板贴图），我按新需求另写一个独立的构建脚本即可，不需要留着这 1904 行。

---

## 4. 分阶段清单（19 步，一次一步）

> 每步只碰一个"脚本主题"，改完必须：编译 0 error → 进 LevelScene 跑 20 秒 → 你 review → 提交一次 commit。

### 阶段 A · 地基（步骤 1-3）

**步骤 1｜Assets\Scripts\Food\Edible.cs（61 行）+ Assets\Scripts\Core\Eater.cs（56 行）**
- 删两个文件的 `///` 注释；`Eater.logEating` 删掉（修 P1 日志刷屏），只保留 `// 质量变化交给 SizeController，销毁交给 Edible`。
- `Edible` 的静态计数改成 `public static int Count`（与 `SizeController` 的命名对齐），删掉 `consumed`/`armedAt` 的 summary 但保留字段。
- 验收：Play 20 秒，Editor.log 不再出现 `[吞噬]` 刷屏；食物总数正常。

**步骤 2｜Assets\Scripts\Core\GameSettings.cs（134 行）**
- 删 11 个 summary / 22 行 `///`。
- `Difficulty` 属性里的 PlayerPrefs 读写拆成 `LoadDifficulty()` / `SaveDifficulty()` 两个私有方法，属性只做转发；删 `Clamp(int)`（用 `Mathf.Clamp`）。
- 把 `DifficultyManager` 里唯一有价值的"一行难度日志"挪到 `SetDifficulty`。
- 验收：主菜单选 Easy/Hard → 关卡内 `PlayerEatMultiplier / AiEatMultiplier` 分别为 1.5/0.5 与 0.5/1.5。

**步骤 3｜新增 Assets\Scripts\Core\Team.cs + 改 Assets\Scripts\Core\SizeController.cs（218 行），并删除 DifficultyManager**
- 新 `public enum Team { Player = 0, AI = 1 }`（底层仍是 0/1，场景 YAML 不需要改）。
- `SizeController.team` 改成 `Team` 类型；9 处 `Team == 0` 全部改成 `team == Team.Player`；删 `System.Collections.Generic.List` 全限定名。
- 7 处 `=>` 改回 `{ return ...; }`；删 16 个 summary；`Registry` 保留但加 `// 只允许 SizeController 自己增删` 的注释与 `HideInInspector` 式封装（改为 `public static IReadOnlyList<SizeController> Registry`，内部用一个 private List）。
- 删 `MassChanged` 事件（P4）及其 4 处 invoke。
- 用 Unity 脚本从 LevelScene 移除 `DifficultyManager` 组件，再删 `Assets\Scripts\Core\DifficultyManager.cs`。
- 验收：AI 仍能吃东西、玩家能长大；Inspector 里 team 显示为 Player/AI；场景无 Missing Script。

### 阶段 B · 核心系统（步骤 4-9）

**步骤 4｜Assets\Scripts\Core\BallFactory.cs（131 行）**
- `CreateFood` 与 `CreateSpitFood` 共用一个私有 `BuildFood(...)`，去掉 20 行重复。
- 删 7 个 summary；`DefaultLinearDamping` 之类的常量化保留。
- 验收：场上食物数量、吐出的食物大小与现在一致。

**步骤 5｜Assets\Scripts\World\ArenaManager.cs（305 行）+ 新增 ArenaBorder.cs**
- 把 LineRenderer 建材质、`UpdateBorderPositions`、`OnDrawGizmos` 共约 100 行拆到 `ArenaBorder.cs`（挂在同一个 Arena 对象下，由 ArenaManager 初始化）。
- `GetRandomPointInside(System.Random rng, float margin)` 的 `rng` 参数 4 个调用方全传 null → 删参数，改 `GetRandomPointInside(float margin = 0f)`。
- 单例改 `public static ArenaManager instance;`；删 7 个 summary 与 4 处 `=>`。
- 验收：边界线仍可见、球仍被软推回场内、食物不生成到场外。

**步骤 6｜Assets\Scripts\Food\FoodSpawner.cs（228 行）**
- 删 `CurrentFoodCount` 字段与两处赋值，统一用 `Edible.Count`（修 P2）。
- 删 Awake 里为偷贴图做的 `FindFirstObjectByType<PlayerController>()`，改为 Start 里一次；`ClearAllFood()` 改为遍历自己维护的列表。
- 验收：把 `maxFoodCount` 调到 50，场上食物稳定在 50 附近不漂移。

**步骤 7｜Assets\Scripts\Camera\CameraFollow.cs（384 行）**
- 5 处 `player.Balls` 遍历合并成每帧算一次 `ComputeBounds(out center, out radius)`，其余方法读缓存。
- 删只有一个值的 `centerMode` 枚举、删死方法 `TryGetPlayerBounds`、删 15 个 summary。
- 保留调参：padding / radiusScale / min-maxOrtho / 缩放速度 / 最大平移。
- 验收：分裂成 4 球时镜头仍能框住全场，镜头不抖、不跳。

**步骤 8｜Assets\Scripts\AI\AIController.cs（446 行）**
- `Scan()` 136 行拆成 `ScanNearby()`（OverlapCircle）、`Classify()`（食物/小球/就近三分支合一）、`UpdateState()`、`PickWanderTarget()`，每个 ≤20 行。
- 中文枚举改 `enum ChaseTarget { Food, Ball, Nearest }`（底层 0/1/2，场景值不变）。
- `(a == null && b == null) == false` 改 `if (a != null || b != null)`；`ApplyRandomProfile` 改 `AddSpeedPercent` 这类语义明确的名字。
- 验收：10 个 AI 行为与现在一致（会追小球、会逃、会游走）。

**步骤 9｜Assets\Scripts\Core\GameManager.cs（504 行）+ 新增 RoundTimer.cs / ScoreBoard.cs**
- 拆出 `RoundTimer`（倒计时、警告、到点事件）与 `ScoreBoard`（Peak/FinalMass、FinalRank、TotalParticipants、排名算法）。
- `Update` 里 5 段注释分节逻辑各归其主；`AliveAiCount` 的全表遍历改成 Registry 内一次性统计。
- 删 `GameOver` 事件（P5）、删 `UnityEngine.Random` 全限定（`using Random = UnityEngine.Random;`）。
- 目标：GameManager 由 504 行降到 ≤220 行。
- 验收：倒计时归零 → 结算面板弹出 → Restart 能重开、BackToMenu 能回主菜单。

### 阶段 C · Player 拆分（步骤 10-14）

**步骤 10｜PlayerController 第一刀 → 新增 Assets\Scripts\Player\PlayerInput.cs**
- 把 InputActionAsset / actionMap / 4 个 FindAction / `ResolveActions()` / 虚拟摇杆 `SetVirtualMoveInput` / `OnSplitPerformed` / `OnSpitPerformed` 整体搬过去，对外暴露 `Move`、`SplitPressed`、`SpitPressed`。
- 验收：键盘 WASD、手柄、移动端摇杆三种输入都能推动球。

**步骤 11｜第二刀 → 新增 Assets\Scripts\Player\PlayerBalls.cs**
- 搬 `activePlayerBalls` / `mainBall` / `RegisterBall` / `UnregisterBall` / `OnBallEaten` / `FindLargestBall` / `AdoptOrphanPlayerBalls` / `SlotAngleFor` / `NotifyDefeat`。
- 验收：分裂后主球交接、被吃后球列表减少、死亡触发复活倒计时。

**步骤 12｜第三刀 → 新增 Assets\Scripts\Player\PlayerActions.cs**
- 搬 `TrySplit` / `TrySpit` 及其长方法体（每个 50 行 → 拆成 3 个 ≤20 行的小方法：检查冷却、执行、生成）。
- 验收：分裂方向、冷却、吐球质量与现在一致。

**步骤 13｜第四刀 → 新增 Assets\Scripts\Player\PlayerMerge.cs + 删 PlayerTuning 死链**
- 搬 `mergeReadyAt` / `mergeJobs` / `MergeJob` / `DetectMerges` / `StartMerge` / `UpdateMergeJobs` / `CanMerge`。
- 删除 `PlayerTuning`（22 字段）、`CaptureTuning`、`ApplyTuning`、`s_PendingTuning`、`SpawnAt`、`playerTuning`（P3）。
- 验收：分身后 15 秒能自动吸回，合体动画不跳。

**步骤 14｜Assets\Scripts\Player\PlayerSubBall.cs（207 行）**
- 删兼容重载 `Initialize(controller, leader, angle)` 与 `SetSlotAngle`（死代码）；4 处 `Constrain` 合并成 `ClampToArena()`。
- `FixedUpdate` 110 行拆成 `FollowLeader()` / `ReturnHome()` / `ApplyDrift()`。
- 验收：子球跟随不抖、合体瞬间回到主球。

### 阶段 D · UI（步骤 15-17）

**步骤 15｜Assets\Scripts\UI\HUDController.cs（133 行）+ 新增 RankUtil.cs**
- `ComputeRank` 与 GameManager 的重复实现合并成 `RankUtil.RankOf(float mass)`（P13）。
- 删 4 个 summary；刷新节流改成 `SetIntIfChanged(Text, int)` 静态小方法。

**步骤 16｜Assets\Scripts\UI\RoundTimerUI.cs（103 行）+ RespawnCountdownUI.cs（95 行）**
- 两个脚本共用的"Awake 找 GameManager / 按名字找 Text / 0.x 秒节流"抽成 `HudRefresh`（静态，不引入继承）。
- 删重复的 `FindFirstObjectByType<GameManager>()` 兜底（GameManager 由场景直接绑）。

**步骤 17｜Assets\Scripts\UI\GameOverPanel.cs（185 行）+ MainMenuController.cs（161 行）+ 新增 SceneFlow.cs / SaveData.cs**
- `SceneFlow`：`LoadLevel()`、`LoadMenu()`、`Restart()` 三个方法，统一 `timeScale = 1`、`AudioListener.pause = false`、按 buildIndex 加载；删掉 `CanStreamedLevelBeLoaded` 闸门（它在 Edit Mode 恒为 false，正是上次改名翻车的根因）。
- `SaveData`：难度 + 最高质量两个 PlayerPrefs key 的读写，从 MainMenuController 里搬走。
- 删 `PlayEasy/PlayNormal/PlayHard` 死方法；`mainMenuSceneName` 字符串字段改成常量取 GameSettings。

### 阶段 E · 编辑器与清理（步骤 18-19）

**步骤 18｜删除整个 Assets\Scripts\Editor\（7 个文件 / 1904 行）**
- 先做一次 Play 冒烟，确认当前两个场景是你要的状态（删完就没有再生能力了）。
- 用 Unity 脚本把 2.4 的 4 处空引用补上（`difficultyLabel`、`bestMassLabel`、`finalRankText`、`hintText`），保存两个场景 —— 这是删脚本前必须先做的，否则以后只能手点。
- 删除 7 个 `.cs` 与对应 `.meta`，再删空的 `Assets\Scripts\Editor` 目录：GameUIBuilder / RoundUIBuilder / MainMenuBuilder / MobileUIBuilder / PrototypeSetup / GameViewLandscape / GameViewPortrait。
- 验收：编译 0 error；`Tools` 菜单下"球球大作战"整组消失；两个场景无 Missing Script；进 LevelScene Play 20 秒玩法正常。
- 回滚：这一删是独立 commit，`git revert` / `git checkout` 随时能还原。

**步骤 19｜清理 + 全量回归**
- 删 `Assets\SimpleInput\` 整个目录（含 .meta）。
- 删 Temp 下调试脚本、检查空目录。
- 回归清单：进主菜单 → 选三档难度各进一次 → 关卡内吞噬/分裂/吐球/合体/复活/倒计时归零 → 结算 → Restart → 回主菜单。

---

## 5. 每步的作业流程

1. 我先读一遍该文件（哪怕是重读），再动手。
2. 只改这一步范围内的文件；发现范围外的问题先记到清单，不顺手改。
3. 保存后等 Unity 编译，检查 Editor.log 里 `error CS` 计数为 0。
4. 进 Play Mode 跑 20 秒，读运行时状态 + 日志，确认没有空引用与行为变化。
5. 把改动点（文件 + 行号 + 为什么）发给你 review。
6. 你说 OK → 用 `git.exe` 提交一次（提交信息：`refactor(step N): ...`）→ 进入下一步。
7. 你说不行 / 有问题 → 我改完重来，不前进。

> git 已确认可用：`C:\Users\miche\AppData\Local\GitHubDesktop\app-3.6.10\resources\app\git\cmd\git.exe`，仓库在 `D:\MET4337_EA\.git`。开工前先做一次 checkpoint 提交（当前工作区有 19 项未提交改动，含场景改名）。

---

## 6. 需要你拍板的 6 件事

| # | 问题 | 我的建议 |
|---|---|---|
| Q1 | 要不要像参考代码一样**去掉 `namespace QiuQiu`**？ | **保留**。删掉只是让类型进全局，不改善可读性，还会牵动 25 个文件与 Editor 引用 |
| Q2 | 152 个 `[SerializeField] private` 要不要**全改成 `public`**（照参考）？ | **改**。这是"过度设计"观感的主要来源；改完 Inspector 用法不变 |
| Q3 | 127 个 `[Tooltip]` 要删吗？`[Header]` 呢？ | **Tooltip 全删，Header 保留中文短标签**（参考里 Header 67 处、Tooltip 2 处） |
| Q4 | PlayerController 拆成 5 个文件，接受吗？ | **接受**。923 行拆完每个 ≤160 行，文件名也直观 |
| Q5 | ~~7 个编辑器脚本：合并保留还是整体删除？~~ | **已定：全部删除**（2026-11-16，选 A）→ 见步骤 18 |
| Q6 | 2.4 的 4 处空引用，我来修还是你自己在 Inspector 里点？ | **我来修**（Unity 脚本一次搞定，不改代码语义） |

另外提醒两件事：

- **一次一个脚本**这个节奏很好，但步骤 10~13 是同一个文件拆四次、步骤 9/17 会新增 2~4 个小文件；如果你偏好"一个物理文件一步"，我可以把步骤 9 和 17 再拆细，代价是步数变多。
- 我不会在重构里改任何**玩法数值**（倍率、速度、质量、冷却），除非你单独要求；每步只动结构与写法。

---

## 7. 我不打算做的事

- 不动 `Assets\Scenes\*.unity` 的层级与命名（除了 Q6 的 4 个引用绑定）。
- 不引入 ScriptableObject、事件总线、依赖注入、抽象基类——参考代码里这些全部为 0，而且正是你说的"过度设计"。
- 不改 `Assets\Settings\`、`ProjectSettings\`、URP 配置。
- 不把 UI 做成 prefab（现在全是代码生成，改了会破坏现有场景）。
- 不碰 SimpleInput 以外的第三方资源。
