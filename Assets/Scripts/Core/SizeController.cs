using System;
using UnityEngine;

namespace QiuQiu
{
    /// <summary>
    /// 体积系统：统一管理一个球体的“质量（= 分数）”和“物理/视觉大小”。
    ///
    /// 设计要点
    /// 1. 质量是唯一真实数值，大小由质量推导：
    ///        radius = radiusAtReferenceMass * sqrt(mass / referenceMass)
    ///    用开平方而不是线性，是因为球体“面积”与半径平方成正比：
    ///    吃掉相当于自身 1 倍的质量，视觉上只变成 1.41 倍，膨胀感更自然。
    /// 2. 质量变化时同步更新三处：
    ///    - transform.localScale：视觉大小，同时 CircleCollider2D 会跟随 scale 自动变大；
    ///    - Rigidbody2D.mass：物理惯性，避免出现“看起来很大但一撞就飞”；
    ///    - MassChanged 事件：给 UI / AI / 相机缩放用（后续步骤接入）。
    /// 3. 吞噬判定基于“质量比”（eatMassRatio，默认 1.2 表示必须大 20% 以上），
    ///    而不是半径比，避免尺寸换算误差导致判定抖动。
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Rigidbody2D))]
    public class SizeController : MonoBehaviour
    {
        [Header("质量（= 分数）")]
        [Tooltip("当前质量，整个游戏里所有“谁吃谁”的判定都用它。")]
        [SerializeField] private float mass = 10f;

        [Tooltip("半径参考质量：该质量下半径等于 radiusAtReferenceMass。所有球体共用同一套换算。")]
        [SerializeField] private float referenceMass = 10f;

        [SerializeField] private float minMass = 1f;
        [SerializeField] private float maxMass = 100000f;

        [Header("尺寸映射")]
        [Tooltip("referenceMass 质量对应的世界半径（世界单位）。半径 = 该值 * sqrt(mass / referenceMass)。")]
        [SerializeField] private float radiusAtReferenceMass = 0.75f;

        [Header("吞噬规则")]
        [Tooltip("1.2 表示质量必须达到对方的 120% 才能吞掉对方（即“大 20%”）。")]
        [SerializeField, Range(1f, 2f)] private float eatMassRatio = 1.2f;

        [Header("阵营")]
        [Tooltip("0 = 玩家，1 = AI。阵营只用于统计（吞噬倍率 / 名次 / 补员计数），不决定谁能吃谁——" +
                 "那个判定在 Eater 里，只保护玩家自己的球。")]
        [SerializeField] private int team = 0;

        [Header("移动手感")]
        [Tooltip("速度参考质量：这个质量下移动倍率是 1.0，比它大就越慢。默认 60 = 玩家开局质量。")]
        [SerializeField] private float speedReferenceMass = 60f;

        [Tooltip("大球的最低速度倍率：质量再大也不会低于它，避免变成一块石头。")]
        [SerializeField, Range(0.1f, 1f)] private float minSpeedFactor = 0.4f;

        /// <summary>质量变化后触发（参数为自己）。</summary>
        public event Action<SizeController> MassChanged;

        // 复活保护的到期时间（Time.time）。0 表示没有保护。
        // 用时间点而不是倒计时秒数，这样不用每帧递减，读到的时候算一次就行。
        private float invulnerableUntil;

        /// <summary>自己被吃掉时触发（参数：被吃者、吃它的对象）。</summary>
        public event Action<SizeController, SizeController> Eaten;

        /// <summary>
        /// 场上所有存活的球（玩家 + AI）的静态注册表。
        /// 结算排名、HUD 名次、AI 计数都从这里读，取代每次 FindObjectsByType 全场景遍历
        /// （后者在球多时每 0.2 秒分配一次数组，手机上是纯浪费）。
        /// </summary>
        public static readonly System.Collections.Generic.List<SizeController> Registry =
            new System.Collections.Generic.List<SizeController>(64);

        private void OnEnable()
        {
            if (!Registry.Contains(this)) Registry.Add(this);
        }

        private void OnDisable()
        {
            Registry.Remove(this);
        }

        public float Mass => mass;
        public float Radius => radiusAtReferenceMass * Mathf.Sqrt(Mathf.Max(mass, 0.0001f) / Mathf.Max(referenceMass, 0.0001f));
        public float EatMassRatio => eatMassRatio;
        public int Score => Mathf.FloorToInt(mass);

        /// <summary>
        /// 质量越大越慢：倍率 = (speedReferenceMass / mass) ^ 0.2，夹在 [minSpeedFactor, 1]。
        /// 开场 60 质量 = 1.0，300 ≈ 0.70，1157 ≈ 0.48。AI 和玩家共用同一条曲线，
        /// 所以大雪球追不上小球是规则，不是运气。
        /// </summary>
        public float MoveSpeedFactor
        {
            get
            {
                float ratio = Mathf.Max(0.0001f, speedReferenceMass) / Mathf.Max(0.0001f, mass);
                return Mathf.Clamp(Mathf.Pow(ratio, 0.2f), minSpeedFactor, 1f);
            }
        }
        public int Team => team;
        public bool IsDead { get; private set; }

        private Rigidbody2D rb;

        /// <summary>运行时初始化（分裂生成的新球、AI 生成都走这里，不用 SerializedObject）。</summary>
        public void Initialize(float startMass, int teamId)
        {
            team = teamId;
            mass = Mathf.Clamp(startMass, minMass, maxMass);
            if (rb == null) rb = GetComponent<Rigidbody2D>();
            ApplySizeNow();
            MassChanged?.Invoke(this);
        }

        /// <summary>扣除质量（吐球消耗）：扣完不得低于 lowerBound，成功返回 true。</summary>
        public bool TryConsumeMass(float amount, float lowerBound = 1f)
        {
            if (IsDead || amount <= 0f) return false;
            if (mass - amount < lowerBound) return false;

            mass -= amount;
            ApplySizeNow();
            MassChanged?.Invoke(this);
            return true;
        }

        private void Awake()
        {
            rb = GetComponent<Rigidbody2D>();
            ApplySizeNow();
        }

        private void OnValidate()
        {
            referenceMass = Mathf.Max(referenceMass, 0.0001f);
            mass = Mathf.Clamp(mass, minMass, maxMass);

            if (!Application.isPlaying)
            {
                if (rb == null) rb = GetComponent<Rigidbody2D>();
                ApplySizeNow();
            }
        }

        /// <summary>增加质量（吃食物 / 吃球都走这里）。质量只会增加，想要变小请自行扩展。</summary>
        public void AddMass(float amount)
        {
            if (IsDead || amount <= 0f) return;

            float before = mass;
            mass = Mathf.Clamp(mass + amount, minMass, maxMass);
            if (Mathf.Approximately(before, mass)) return;

            ApplySizeNow();
            MassChanged?.Invoke(this);
        }

        /// <summary>直接设置质量（初始化 / 调试用）。</summary>
        public void SetMass(float value)
        {
            if (IsDead) return;

            mass = Mathf.Clamp(value, minMass, maxMass);
            ApplySizeNow();
            MassChanged?.Invoke(this);
        }

        /// <summary>能否吃掉对方：双方都活着、对方不是无敌状态，且自己的质量达到对方的 eatMassRatio 倍。</summary>
        public bool CanEat(SizeController other)
        {
            if (other == null || ReferenceEquals(other, this)) return false;
            if (IsDead || other.IsDead) return false;
            if (other.IsInvulnerable) return false;   // 复活保护：刚出生的一小段时间不能被吃

            return mass >= other.mass * eatMassRatio;
        }

        /// <summary>尝试吞噬对方：成功则对方销毁、自己加上对方的质量。</summary>
        public bool TryEat(SizeController other)
        {
            if (!CanEat(other)) return false;

            float gain = other.mass;   // 先记录，Die 之后对方数据就不可信了
            other.Die(this);
            AddMassFromEat(gain);
            return true;
        }

        /// <summary>
        /// 吃掉东西时增加质量（统一入口）：按难度乘上吞噬倍率。
        ///   玩家（team == 0）用 GameSettings.PlayerEatMultiplier；
        ///   AI（team != 0）用 GameSettings.AiEatMultiplier。
        /// 食物（Edible）和吞噬敌人（TryEat）都走这里，所以难度只在一处生效。
        /// </summary>
        public void AddMassFromEat(float rawAmount)
        {
            if (IsDead || rawAmount <= 0f) return;

            float multiplier = Team == 0 ? GameSettings.PlayerEatMultiplier : GameSettings.AiEatMultiplier;
            AddMass(rawAmount * multiplier);
        }

        /// <summary>
        /// 复活保护：让这颗球在一段时间内不会被别人吃掉（CanEat 会直接返回 false）。
        /// 只影响“被吃”，自己照样可以吃别人 —— 否则复活后什么都没法做。
        /// </summary>
        public void SetInvulnerable(float seconds)
        {
            invulnerableUntil = seconds > 0f ? Time.time + seconds : 0f;
        }

        /// <summary>当前是否处于复活保护中。</summary>
        public bool IsInvulnerable => Time.time < invulnerableUntil;

        /// <summary>复活保护剩余时间（秒）。</summary>
        public float InvulnerableTimeLeft => Mathf.Max(0f, invulnerableUntil - Time.time);

        /// <summary>自己被吃掉：标记死亡 → 广播事件 → 销毁。</summary>
        public void Die(SizeController killer)
        {
            if (IsDead) return;

            IsDead = true;
            Eaten?.Invoke(this, killer);
            Destroy(gameObject);
        }

        /// <summary>按当前质量重算缩放与物理质量。编辑器里由搭建脚本调用，运行时由 AddMass 调用。</summary>
        public void ApplySizeNow()
        {
            float scale = 2f * Radius;   // 球体精灵在 scale=1 时直径正好 1 个世界单位

            transform.localScale = new Vector3(scale, scale, 1f);

            if (rb == null) rb = GetComponent<Rigidbody2D>();
            if (rb != null) rb.mass = Mathf.Max(mass, 0.01f);
        }
    }
}
