# 第四阶段：节奏、病毒数量、孢子归属、爆裂分布

本轮只改机制，没有推翻 `PlayerCellMotion` 架构，上一阶段的镜头拉远、手机长按吐球、AI BotId 全部保留。

## 1. 改动清单

| 文件 | 改了什么 | 行数 |
|---|---|---|
| `Assets/Scripts/Core/GamePacing.cs` | **新增**。9 个速度/时长系数集中在这里，每个系数只在交给 Rigidbody 的那一处乘一次 | 27 |
| `Assets/Scripts/Core/SizeController.cs` | 新增 `OwnerId`：Player 恒为 0；AI 用 `BotId`，没有 BotId 的用 `-1-InstanceID` | 296 |
| `Assets/Scripts/Food/Edible.cs` | 新增 `hasOwner/ownerTeam/ownerId`、`SetOwner()`、`IsSameOwner()`；`TryConsume()` 统一由 `OnTriggerEnter2D` + 新增的 `OnTriggerStay2D` 调用；**同主人直接 return，不置 consumed、不回收** | 165 |
| `Assets/Scripts/Food/SporePool.cs` | 提供 `CreateSporeShell` 需要的池，`Give()` 只 SetActive(false) | 56 |
| `Assets/Scripts/Core/BallFactory.cs` | `CreateSpitFood(..., SizeController owner)`，池化复用时覆盖旧 Owner；新增 `SporeRadius(mass)` | 144 |
| `Assets/Scripts/Player/PlayerActions.cs` | 孢子生成距离 = `母球半径 + 孢子半径 + gap`；吐球速度乘 `GamePacing.SpitLaunch`；分裂冲量乘 `GamePacing.SplitLaunch` | 182 |
| `Assets/Scripts/Player/PlayerController.cs` | 玩家/AI/跟随/吐球/合并的默认值全部放慢；`GroupVelocity` 乘 `GamePacing.PlayerMove` | 686 |
| `Assets/Scripts/Player/PlayerCellMotion.cs` | 新增 `BeginVirusBurst()`（复用 `SplitCoasting` 状态）；合并缓动改为两端斜率为 0 的 `MergeEase()` | 450 |
| `Assets/Scripts/Player/PlayerMerge.cs` | 合并时长乘 `GamePacing.MergeDuration` | 247 |
| `Assets/Scripts/World/Virus.cs` | 爆裂改为**双环布局** + 间距 relax + 边界夹取；`feedsToSplit` 10；爆裂速度 6.5、coast 0.65 s；AI 的 cell 上限按 `BotId` 统计 | 337 |
| `Assets/Scripts/World/VirusSpawner.cs` | 12 个病毒、上限 16、每 1.2 s 最多补 2 个、间距 9/8、`starPoints` 12；`SpawnOne()` 也守 `maxVirus` | 234 |
| `Assets/Scripts/AI/AIController.cs` | 移动全面放慢；新增 `BeginBurst()/TickBurst()`，爆裂期间不寻路 | 467 |
| `Assets/Scripts/Core/ScoreBoard.cs` | 新增 `CellsOfBot(team, botId)`，供病毒爆裂查"这个 bot 已有几个 cell" | 92 |
| `Assets/Scripts/Camera/CameraFollow.cs` | 4 个平滑时间放慢（分裂拉远 0.20、拉远 0.28、拉近 0.45、位置 0.22），乘 `GamePacing.CameraSmooth` | 323 |

## 2. 场景里实际写入的数值（`Assets/Scenes/LevelScene.unity`，已保存）

> 只改 C# 默认值不够 —— 场景里的序列化值会覆盖默认值，所以下面这些都写进了场景。

**PlayerRoot / PlayerController**

| 字段 | 旧 | 新 |
|---|---|---|
| moveSpeed | 5 | **4.2** |
| maxSpeedHardLimit | 12 | **10.5** |
| acceleration | 60 | **38** |
| deceleration | 6 | **5** |
| spitCooldown | 0.12 | **0.2** |
| spitLaunchSpeed | 16 | **12** |
| mergePullDuration | 0.45 | **0.75** |
| subHomeStrength | 2.5 | **1.8** |
| subHomeDamping | 4 | **3** |
| subMaxDriftSpeed | 3 | **4** |
| subFollowAcceleration | 30 | **22** |

**VirusSpawner**

| 字段 | 旧 | 新 |
|---|---|---|
| virusCount | 30 | **12** |
| maxVirus | 50 | **16** |
| checkInterval | 0.5 | **1.2** |
| maxSpawnsPerTick | 6 | **2** |
| starPoints | 16 | **12** |
| minDistanceFromBalls | 6 | **8** |
| minDistanceBetweenViruses | 6 | **9** |

**Main Camera / CameraFollow**

| 字段 | 旧 | 新 |
|---|---|---|
| splitZoomInTime | 0.12 | **0.20** |
| zoomOutTime | 0.18 | **0.28** |
| zoomInTime | 0.32 | **0.45** |
| positionSmoothTime | 0.16 | **0.22** |

**10 个 AI（AI_00…AI_09）/ AIController**

| 字段 | 旧 | 新 |
|---|---|---|
| moveSpeed | 3.2 | **2.7** |
| chaseSpeedFactor | 1.15 | **1.10** |
| fleeSpeedFactor | 1.3 | **1.20** |
| acceleration | 26 | **20** |
| maxTurnDegreesPerSecond | 540 | **360** |

## 3. 关键实现

### 3.1 孢子归属（本次核心 Bug）

- 每个孢子记录 `ownerTeam + ownerId`。Player 的所有 cell 的 `OwnerId` 都是 `0`，所以 16 个分身共享同一个主人。
- `Edible.TryConsume()` 的判断顺序：`consumed` → `null` → `armDelay` → 喂病毒 → 取 `SizeController` → **`IsSameOwner()` 直接 return** → 才置 `consumed = true`。
  同主人碰到孢子时既不吞、也不回收对象，孢子留在原地；别的玩家/AI/病毒照常处理。
- 因为加的是 `OnTriggerStay2D`，所以孢子在 `armDelay` 里就压在身上、保护结束后不会漏判。
- 生成位置 = `母球半径 + 孢子半径 + splitSpawnGap`，孢子出生时完全在球外。
- 对象池复用时 `SetOwner()` 覆盖旧主人，`ResetForReuse()` 清速度/质量/consumed/armDelay/spore 并重开 collider。

### 3.2 病毒爆裂的双环布局

```
内环 7 个：角度 360/7 等分，半径 2.8 × 碎片半径
外环 8 个：角度再错开 22.5°，半径 5.0 × 碎片半径
最后 3 轮 relax：任意两球距离 < 2r + gap 就各推一半，并重新夹回地图内
```

- 半径全部由 `pieceRadius` 推导，不写死世界距离；不足 16 个时内环先排满再排外环。
- 爆裂后每个碎片调 `BeginVirusBurst()`：状态进 `SplitCoasting`、速度 = 方向 × 6.5、`e^-3.5t` 衰减 0.65 s 后交给 `Following`。
- `SplitCoasting` 属于 `IsBusy`，所以 `SyncMotion`、`SetFollowing`、`PushApart` 都不会动它 —— 不会被跟随系统提前吸回中心。
- AI 的碎片走 `AIController.BeginBurst()`，爆裂期间 `FixedUpdate` 直接 return，不寻路。

### 3.3 合并缓动

`MergeEase(t, 2)` = smoothstep，`MergeEase(t, 3)` = smootherstep，两端斜率为 0；不再用 `Pow(t,2)`（慢起步 + 末段猛冲）。合并时长 0.75 s。
`mergeCooldown` / `mergeCooldownPerMass` 没有动 —— 它们决定"什么时候允许合并"，不是动画速度。

## 4. 完整代码

### 4.1  Core\GamePacing.cs

```csharp
namespace QiuQiu
{
    // One place for the global pacing knobs, so "make the whole game slower" is a single edit
    // instead of hunting through six scripts.
    //
    // RULE: every multiplier is applied exactly ONCE, at the point where that speed is handed
    // to the Rigidbody. If two places multiplied the same number the game would quietly run at
    // 0.6 x 0.7 and nobody could tune it back.
    //
    //   1.0    = keep the value written in the Inspector
    //   0.8    = that part of the game runs 20% slower
    //
    // The per-script values are still the fine tuning; these are the "whole game" dials.
    public static class GamePacing
    {
        // where the multiplier is applied:
        public const float PlayerMove = 1f;        // PlayerController.GroupVelocity
        public const float AiMove = 1f;            // AIController.ApplyMovement (speed)
        public const float AiAcceleration = 1f;    // AIController.ApplyMovement (acceleration)
        public const float CellFollow = 1f;        // PlayerCellMotion.ApplyMotionTuning
        public const float SplitLaunch = 1f;       // PlayerActions.TrySplit (bullet impulse)
        public const float VirusBurstLaunch = 1f;  // Virus.LaunchBurstPiece
        public const float SpitLaunch = 1f;        // PlayerActions.TrySpit
        public const float MergeDuration = 1f;     // PlayerMerge.Begin (animation length)
        public const float CameraSmooth = 1f;      // CameraFollow (smooth times)
    }
}
```

### 4.2  Core\SizeController.cs

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

        // Identity of the group this cell belongs to. Every cell of one player, or of one
        // bot, shares the same value, so 16 split pieces are still "the same owner".
        // Used to stop a ball from eating the mass it just spat out.
        public int OwnerId
        {
            get
            {
                if (team == Team.Player)
                {
                    return 0;   // there is only one human player
                }
                return BotId >= 0 ? BotId : -1 - GetInstanceID();
            }
        }

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

### 4.3  Food\Edible.cs

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

        // who spat this pellet out: the pellet must survive until somebody else picks it up
        private bool hasOwner;
        private Team ownerTeam;
        private int ownerId;

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
            hasOwner = false;   // and it belongs to nobody until it is spat out again
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

        // Remember who spat it out. A pooled pellet is handed out again and again, so this
        // has to be overwritten every time it leaves the pool.
        public void SetOwner(Team team, int groupId)
        {
            ownerTeam = team;
            ownerId = groupId;
            hasOwner = true;
        }

        // Same group = never edible. Not "same team": every bot is Team.Enemy, so the team
        // alone would let a bot eat every other bot's pellet as well as its own.
        private bool IsSameOwner(SizeController eater)
        {
            return hasOwner && eater != null && eater.Team == ownerTeam && eater.OwnerId == ownerId;
        }

        private void OnTriggerEnter2D(Collider2D other)
        {
            TryConsume(other);
        }

        // A pellet can already be inside a body while its arm delay is still running. After
        // the delay runs out nothing would fire again if only Enter were wired up.
        private void OnTriggerStay2D(Collider2D other)
        {
            TryConsume(other);
        }

        private void TryConsume(Collider2D other)
        {
            if (consumed)
            {
                return;
            }
            if (other == null)
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

            // Your own spore is not food for you. Checked before consumed is set, so the
            // pellet is neither eaten nor recycled.
            if (IsSameOwner(eater))
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

### 4.4  Food\SporePool.cs

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

### 4.5  Core\BallFactory.cs

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

        public static Edible CreateSpitFood(string name, Vector3 position, Vector2 velocity, float mass, Sprite sprite, Color color, float armDelay, SizeController owner)
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

            // The pool hands out old pellets, so the previous owner has to be overwritten or a
            // recycled pellet would still refuse to be eaten by its new owner.
            if (owner != null)
            {
                edible.SetOwner(owner.Team, owner.OwnerId);
            }

            go.SetActive(true);
            return edible;
        }

        // world radius of a pellet with this mass, so the caller can spawn it outside the body
        public static float SporeRadius(float mass)
        {
            return FoodDiameter(mass) * 0.5f;
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

### 4.6  Player\PlayerActions.cs

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
                    float impulse = groupSpeed * tuning.splitImpulseFactor * GamePacing.SplitLaunch;

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

                // Spawn outside the body: body radius + the pellet's own radius + the gap.
                // Spawning at body radius only buries half of the pellet inside the ball, and
                // no arm delay can hide that.
                float sporeRadius = BallFactory.SporeRadius(tuning.spitFoodMass);
                float spawnDistance = ball.Radius + sporeRadius + tuning.spitSpawnGap;
                Vector3 position = ball.transform.position + (Vector3)(dir * spawnDistance);

                BallFactory.CreateSpitFood("SpitFood", position,
                    dir * (tuning.spitLaunchSpeed * GamePacing.SpitLaunch),
                    tuning.spitFoodMass, sprite, Lighten(color), tuning.spitArmDelay, ball);

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

### 4.7  Player\PlayerController.cs

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
        [SerializeField] private float moveSpeed = 4.2f;
        [SerializeField] private float maxSpeedHardLimit = 10.5f;
        [SerializeField] private float acceleration = 38f;
        [Tooltip("How fast a ball bleeds off speed when nobody pushes it. This is also what makes the split launch fade instead of stopping dead.")]
        [SerializeField] private float deceleration = 5f;
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
        [SerializeField] private float subHomeStrength = 1.8f;
        [Tooltip("How quickly it answers to that nudge. Higher = snappier, no overshoot")]
        [SerializeField] private float subHomeDamping = 3f;
        [Tooltip("How fast a sub cell may drift back beside the big cell. Keep it low: a sub " +
                 "cell is allowed a short catch up, not a permanent speed advantage.")]
        [SerializeField] private float subMaxDriftSpeed = 4f;
        [Tooltip("Fallback follow acceleration, only used when there is no big ball to sit beside")]
        [SerializeField] private float subFollowAcceleration = 22f;

        [Header("Spit")]
        [SerializeField] private float spitMassCost = 12f;
        [SerializeField] private float spitFoodMass = 9f;
        [SerializeField] private float minMassAfterSpit = 20f;
        [SerializeField] private float spitCooldown = 0.2f;
        [Tooltip("A failed spit (no cell big enough) waits this long before trying again, so it is not re-checked every frame")]
        [SerializeField] private float spitRetryInterval = 0.25f;
        [SerializeField] private float spitLaunchSpeed = 12f;
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
        [SerializeField] private float mergePullDuration = 0.75f;
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
                return MoveInput * (moveSpeed * factor * GamePacing.PlayerMove);
            }
        }

        public SizeController MainBall { get { return roster.Main; } }

        public int BallCount { get { return roster.Count; } }

        public IReadOnlyList<SizeController> Balls { get { return roster.Balls; } }

        public float TotalMass { get { return roster.TotalMass; } }
    }
}
```

### 4.8  Player\PlayerCellMotion.cs

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
            followAcceleration = followAccel * GamePacing.CellFollow;
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

        // A virus burst has no slide-out: the virus already placed the cell on its ring. It only
        // gets an outward push, and normal following is locked out until that push has faded,
        // otherwise the home spring would swallow the whole explosion.
        public void BeginVirusBurst(Vector2 direction, float speed, float coastSeconds)
        {
            CacheParts();

            splitDirection = direction.sqrMagnitude > 0.0001f ? direction.normalized : Vector2.right;
            splitImpulse = Mathf.Max(0f, speed);
            coastDuration = Mathf.Max(0.05f, coastSeconds);
            coastElapsed = 0f;

            state = CellMotionState.SplitCoasting;

            if (rb != null)
            {
                rb.bodyType = RigidbodyType2D.Dynamic;
                rb.linearVelocity = splitDirection * splitImpulse;
            }
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
            float eased = MergeEase(t, mergeEase);

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

        // Merge easing. Pow(t, 2) starts almost flat and then rushes the last frames, which is
        // what made the merge look like a snap. These curves both start and end with zero slope:
        // strength 1 = constant speed, 2 = smoothstep (default), 3 = smootherstep.
        private static float MergeEase(float t, float strength)
        {
            t = Mathf.Clamp01(t);
            float smooth = t * t * (3f - 2f * t);
            float smoother = t * t * t * (t * (t * 6f - 15f) + 10f);

            float s = Mathf.Max(0.01f, strength);
            if (s <= 2f)
            {
                return Mathf.Lerp(t, smooth, Mathf.Clamp01(s - 1f));
            }
            return Mathf.Lerp(smooth, smoother, Mathf.Clamp01(s - 2f));
        }

        // Called by PlayerMerge while two cells are not allowed to merge yet: the solid-body
        // separation from Agar. The merge layer decides how far, this component performs it,
        // so the Rigidbody still only has one writer.
        public bool Nudge(Vector2 delta)        {
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

### 4.9  Player\PlayerMerge.cs

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
            motion.BeginMerge(keeper, tuning.mergePullDuration * GamePacing.MergeDuration, tuning.mergePullEase, () => Commit(small, keeper));
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

### 4.10  World\Virus.cs

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
        [Tooltip("Slow push after the burst: low on purpose, the spacing comes from the layout")]
        [SerializeField] private float burstLaunchSpeed = 6.5f;
        [Tooltip("How long a burst piece keeps its outward push before normal following takes over")]
        [SerializeField] private float burstCoastDuration = 0.65f;
        [SerializeField] private float burstSpawnGap = 0.05f;
        [SerializeField] private float burstCooldown = 0.4f;

        [Header("Burst Layout")]
        [Tooltip("Cells on the inner ring; the rest go to the outer ring")]
        [SerializeField] private int burstInnerCount = 7;
        [Tooltip("Ring radius = pieceRadius * this")]
        [SerializeField] private float burstInnerRadiusFactor = 2.8f;
        [SerializeField] private float burstOuterRadiusFactor = 5.0f;
        [Tooltip("Outer ring is rotated so its cells sit between the inner ones")]
        [SerializeField] private float burstOuterAngleOffset = 22.5f;

        [Header("Feeding")]
        [Tooltip("How many spores a virus must swallow before it fires a new virus")]
        [SerializeField] private int feedsToSplit = 10;
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

            // The limit counts the cells of the OWNER. An AI with 10 cells must not get another
            // full 16 just because every one of its cells carries its own AIController.
            int owned = isPlayer && player != null
                ? player.BallCount
                : ScoreBoard.CellsOfBot(ball.Team, ball.BotId);
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

            SpriteRenderer shell = ball.GetComponent<SpriteRenderer>();
            Sprite sprite = shell != null ? shell.sprite : null;
            Color color = shell != null ? shell.color : Color.white;

            // equal pieces, so one radius describes all of them
            ball.SetMass(pieceMass);
            float pieceRadius = ball.Radius;
            Vector2 centre = ball.transform.position;

            List<Vector2> spots = BuildBurstLayout(centre, pieceRadius, pieces - 1);
            List<SizeController> created = new List<SizeController>();

            for (int i = 0; i < spots.Count; i++)
            {
                Vector2 dir = spots[i] - centre;
                dir = dir.sqrMagnitude > 0.0001f ? dir.normalized : Vector2.right;

                SizeController piece = isPlayer && player != null
                    ? player.SpawnBall("Player_Burst", spots[i], pieceMass, sprite, color)
                    : SpawnAiPiece(spots[i], pieceMass, sprite, color, ball.gameObject.name, ball.BotId);

                if (piece == null)
                {
                    continue;
                }

                piece.SetInvulnerable(0.25f);
                LaunchBurstPiece(piece, dir);
                created.Add(piece);
            }

            if (isPlayer && player != null)
            {
                player.MarkBurst(ball, created);
            }

            Debug.Log($"[virus] burst {total:F0} into {pieces} cells, {spots.Count} launched");

            // the virus is consumed by the burst
            Destroy(gameObject);
        }

        // The burst owns the piece for burstCoastDuration: normal following must not pull it
        // straight back, and an AI must not path towards food in the middle of the explosion.
        private void LaunchBurstPiece(SizeController piece, Vector2 direction)
        {
            if (piece == null)
            {
                return;
            }

            float speed = burstLaunchSpeed * GamePacing.VirusBurstLaunch;

            PlayerCellMotion motion = piece.GetComponent<PlayerCellMotion>();
            if (motion != null)
            {
                motion.BeginVirusBurst(direction, speed, burstCoastDuration);
                return;
            }

            AIController ai = piece.GetComponent<AIController>();
            if (ai != null)
            {
                ai.BeginBurst(direction * speed, burstCoastDuration);
                return;
            }

            Rigidbody2D pieceBody = piece.GetComponent<Rigidbody2D>();
            if (pieceBody != null)
            {
                pieceBody.linearVelocity = direction * speed;
            }
        }

        // Two rings instead of one. On a single ring 15 equal circles always overlap, whatever
        // the gap is: the arc between neighbours is shorter than a diameter.
        private List<Vector2> BuildBurstLayout(Vector2 centre, float pieceRadius, int count)
        {
            List<Vector2> spots = new List<Vector2>(Mathf.Max(0, count));
            if (count <= 0)
            {
                return spots;
            }

            int inner = Mathf.Min(count, Mathf.Max(1, burstInnerCount));
            int outer = count - inner;
            float minGap = pieceRadius * 2f + burstSpawnGap;

            for (int i = 0; i < inner; i++)
            {
                float angle = 360f / Mathf.Max(1, inner) * i;
                float r = pieceRadius * burstInnerRadiusFactor;
                Vector2 offset = Quaternion.Euler(0f, 0f, angle) * (Vector2.right * r);
                spots.Add(ClampInside(centre + offset, pieceRadius));
            }

            for (int i = 0; i < outer; i++)
            {
                float angle = burstOuterAngleOffset + 360f / Mathf.Max(1, outer) * i;
                float r = pieceRadius * burstOuterRadiusFactor;
                Vector2 offset = Quaternion.Euler(0f, 0f, angle) * (Vector2.right * r);
                spots.Add(ClampInside(centre + offset, pieceRadius));
            }

            // relax: clamping at the arena edge can squeeze two cells together again
            for (int pass = 0; pass < 3; pass++)
            {
                for (int a = 0; a < spots.Count; a++)
                {
                    for (int b = a + 1; b < spots.Count; b++)
                    {
                        Vector2 delta = spots[b] - spots[a];
                        float distance = delta.magnitude;
                        if (distance >= minGap || distance <= 0.0001f)
                        {
                            continue;
                        }

                        Vector2 push = delta / distance * ((minGap - distance) * 0.5f);
                        spots[a] = ClampInside(spots[a] - push, pieceRadius);
                        spots[b] = ClampInside(spots[b] + push, pieceRadius);
                    }
                }
            }

            return spots;
        }

        private static Vector2 ClampInside(Vector2 point, float radius)
        {
            ArenaManager arena = ArenaManager.Instance;
            if (arena == null)
            {
                return point;
            }

            Vector2 centre = arena.Center;
            Vector2 half = arena.HalfSize;
            return new Vector2(
                Mathf.Clamp(point.x, centre.x - half.x + radius, centre.x + half.x - radius),
                Mathf.Clamp(point.y, centre.y - half.y + radius, centre.y + half.y - radius));
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

### 4.11  World\VirusSpawner.cs

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
        [SerializeField] private int virusCount = 12;
        [Tooltip("Hard cap, including the ones fired by feeding a virus")]
        [SerializeField] private int maxVirus = 16;
        [SerializeField] private bool fillOnStart = true;
        [SerializeField] private float checkInterval = 1.2f;
        [SerializeField] private int maxSpawnsPerTick = 2;

        [Header("Virus")]
        [SerializeField] private float virusMass = 120f;
        [SerializeField] private float radiusScale = 0.237f;
        [SerializeField] private Color virusColor = new Color(0.42f, 0.85f, 0.55f, 1f);
        [Tooltip("Fallback only: Virus.ApplyVisual overrides this at runtime with the size based order")]
        [SerializeField] private int sortingOrder = 15;

        [Header("Star Sprite")]
        [SerializeField] private int textureSize = 128;
        [SerializeField] private int starPoints = 12;
        [SerializeField, Range(0.3f, 0.95f)] private float starInnerRatio = 0.74f;

        [Header("Placement")]
        [SerializeField] private float borderMargin = 2f;
        [SerializeField] private float minDistanceFromBalls = 8f;
        [SerializeField] private float minDistanceBetweenViruses = 9f;
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

            int wanted = Mathf.Clamp(virusCount, 0, Mathf.Max(0, maxVirus));
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

            // The fed-virus path already checked the cap; the normal refill has to check it as
            // well, otherwise a changed virusCount can push the map past maxVirus.
            if (Virus.ActiveCount >= maxVirus)
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

### 4.12  AI\AIController.cs

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
        [SerializeField] private float moveSpeed = 2.7f;
        [SerializeField] private float chaseSpeedFactor = 1.10f;
        [SerializeField] private float fleeSpeedFactor = 1.20f;
        [SerializeField] private float acceleration = 20f;
        [SerializeField] private float maxTurnDegreesPerSecond = 360f;

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

        // virus burst: the cell is thrown outwards and ignores its own pathing for a moment
        private float burstEndTime;
        private float burstStartTime;
        private Vector2 burstVelocity;

        // Called by Virus when this cell was spawned by a burst.
        public void BeginBurst(Vector2 velocity, float duration)
        {
            burstVelocity = velocity;
            burstStartTime = Time.time;
            burstEndTime = Time.time + Mathf.Max(0.05f, duration);

            if (rb != null)
            {
                rb.linearVelocity = velocity;
            }
        }

        private void TickBurst()
        {
            float span = Mathf.Max(0.05f, burstEndTime - burstStartTime);
            float t = Mathf.Clamp01((Time.time - burstStartTime) / span);
            if (rb != null)
            {
                rb.linearVelocity = burstVelocity * Mathf.Exp(-3.5f * t);
            }

            if (ArenaManager.Instance != null && rb != null)
            {
                ArenaManager.Instance.Constrain(rb, self.Radius);
            }
        }

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

            // A virus just threw this cell outwards: it keeps flying until the push fades, and
            // only then does normal pathing resume.
            if (Time.time < burstEndTime)
            {
                TickBurst();
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

            float speed = moveSpeed * (self != null ? self.MoveSpeedFactor : 1f) * GamePacing.AiMove;
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
                currentVelocity, targetVelocity,
                Mathf.Max(1f, acceleration) * GamePacing.AiAcceleration * Time.fixedDeltaTime);
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

### 4.13  Core\ScoreBoard.cs

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

        // how many living cells one participant owns. A virus burst has to respect this number:
        // the cell limit belongs to the owner, not to each individual cell.
        public static int CellsOfBot(Team team, int botId)
        {
            int count = 0;
            var all = SizeController.Registry;

            for (int i = 0; i < all.Count; i++)
            {
                var ball = all[i];
                if (ball == null || ball.IsDead || ball.Team != team)
                {
                    continue;
                }

                int id = ball.BotId >= 0 ? ball.BotId : -1 - ball.GetInstanceID();
                if (id == botId)
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

### 4.14  Camera\CameraFollow.cs

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
        [SerializeField] private float splitZoomInTime = 0.20f;
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
        [SerializeField] private float zoomOutTime = 0.28f;
        [Tooltip("Zoom in, kept slower than zoom out on purpose")]
        [SerializeField] private float zoomInTime = 0.45f;
        [Tooltip("How quickly the camera centre follows the group centre")]
        [SerializeField] private float positionSmoothTime = 0.22f;

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

            float smooth = Mathf.Max(0.0001f, positionSmoothTime * GamePacing.CameraSmooth);

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

            smooth = Mathf.Max(0.0001f, smooth * GamePacing.CameraSmooth);
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


## 5. 实测结果

测试环境：Unity 6000.3.5f2，`LevelScene`，Play 模式，脚本通过 Unity Pipeline `eval` 直接读运行时状态。

### Test 1 地图病毒数量

| 项 | 结果 |
|---|---|
| 开局病毒数 | **12**（旧版本 30） |
| 全场击杀 AI 后清场 | 病毒仍为 12，说明补刷守住了 `virusCount` |
| 上限 | `SpawnOne()` 命中 `maxVirus=16` 直接返回，不再生成 |

### Test 2 吐孢子归属

| 步骤 | 结果 |
|---|---|
| 玩家 1 个 cell 吐一次 | 质量 200 → **188**（扣 12），孢子质量 9 |
| 孢子记录的 owner | `hasOwner=True, ownerId=0`，玩家 cell 的 `OwnerId=0` → 同一主人 |
| 把**玩家自己的 cell** 放到孢子正上方，等 2 s | 孢子仍然存在（`sporesAlive=1`），**没有被自己吃掉** |
| 换一个 `OwnerId=59513` 的敌方球放到孢子位置 | 孢子消失（`sporesLeft=0`），敌方质量 400 → **417.93** |

### Test 3 病毒爆裂

| 项 | 结果 |
|---|---|
| 爆裂后 cell 数 | **16** |
| 布局（反射直接调 `BuildBurstLayout`，pieceRadius=1.185，15 片） | `count=15, inner=7, outer=8`，内环半径 = **2.80×**、外环 = **5.00×** |
| 最小球心间距 | **2.62**，要求 `2r = 2.37` → 不重叠；生成瞬间实测 `minPairGap = +0.25` |
| 爆裂后状态 | `coasting=15, following=0`，`coastDuration=0.65` → **没有被 Following 提前覆盖** |
| 弹射完自动交接 | 时间尺度恢复后全部进入 `Following`，最终正常合并回 1 个，质量守恒（426.3 → 426.3） |
| 截图 | `Screenshots/p4_burst.png`（16 个 cell 分两层散开） |

### Test 4 节奏

| 项 | 旧 | 新 |
|---|---|---|
| 玩家最大移动速度 | 5（组速 5×factor） | 4.2 |
| 玩家加速度 / 减速度 | 60 / 6 | 38 / 5 |
| AI 基础速度 | 3.2 | 2.7 |
| AI 转向 | 540°/s | 360°/s |
| 合并时长 | 0.45 s | 0.75 s，smoothstep 曲线 |
| 分裂拉远时间 | 0.12 s | 0.20 s |

### Test 5 稳定性

| 项 | 结果 |
|---|---|
| 质量守恒 | 爆裂 400 → 424.6（含吃到的食物）→ 合并回 426.3，没有凭空产生 |
| 孢子池 Owner 残留 | 池化复用时 `SetOwner()` 覆盖，旧主人 id 不会残留 |
| 病毒无上限增长 | `SpawnOne` + `SpawnFrom` 双路径都守 `maxVirus` |
| 编译 | `compilationFailed: false`，`consoleErrors: 0` |

## 6. 遗留问题

1. `showDebug` 仍默认开着（左上角调试角标），验收完可以关。
2. 爆裂的"手感"只能靠数值判断，建议真人玩一局确认 6.5 速度 + 0.65 s 的扩散是否符合预期；嫌散得不够可以把 `burstLaunchSpeed` 提到 7~7.5。
3. 手机多点触控只在编辑器里用指针事件模拟过，真机没测。
4. 病毒被吃掉后的补刷是 1.2 s 一次、每次最多 2 个，如果地图清得太快会短暂低于 12 个，属于设计内的缓慢补充。
