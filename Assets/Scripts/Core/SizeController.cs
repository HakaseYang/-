using System;
using System.Collections.Generic;
using UnityEngine;

namespace QiuQiu
{
    // 质量是唯一真值，大小 / 物理质量 / UI 全从它推。
    // 半径 = 参考半径 * sqrt(质量比)：吃掉一倍质量只长到 1.41 倍。
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Rigidbody2D))]
    public class SizeController : MonoBehaviour
    {
        [Header("质量（= 分数）")]
        [SerializeField] private float mass = 10f;
        [SerializeField] private float referenceMass = 10f;
        [SerializeField] private float minMass = 1f;
        [SerializeField] private float maxMass = 100000f;

        [Header("尺寸映射")]
        [SerializeField] private float radiusAtReferenceMass = 0.75f;

        [Header("吞噬规则")]
        [SerializeField, Range(1f, 2f)] private float eatMassRatio = 1.2f;   // 1.2 = 要到对方 120% 才吃得掉

        [Header("阵营")]
        [SerializeField] private Team team = Team.Player;

        [Header("移动手感")]
        [SerializeField] private float speedReferenceMass = 60f;             // 该质量下倍率 = 1.0（玩家开局质量）
        [SerializeField, Range(0.1f, 1f)] private float minSpeedFactor = 0.4f;

        public event Action<SizeController> MassChanged;
        public event Action<SizeController, SizeController> Eaten;

        // 场上所有存活球（玩家 + AI），替代每 0.2 秒一次 FindObjectsByType
        private static readonly List<SizeController> registry = new List<SizeController>(64);

        public static IReadOnlyList<SizeController> Registry { get { return registry; } }

        private Rigidbody2D rb;
        private float invulnerableUntil;   // 复活保护到期时间点（0 = 没保护）

        private void OnEnable()
        {
            if (!registry.Contains(this)) registry.Add(this);
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

        private void OnValidate()
        {
            referenceMass = Mathf.Max(referenceMass, 0.0001f);
            mass = Mathf.Clamp(mass, minMass, maxMass);
            if (Application.isPlaying) return;

            if (rb == null) rb = GetComponent<Rigidbody2D>();
            ApplySizeNow();
        }

        public float Mass { get { return mass; } }
        public float Score { get { return Mathf.FloorToInt(mass); } }
        public float EatMassRatio { get { return eatMassRatio; } }
        public Team Team { get { return team; } }
        public bool IsDead { get; private set; }
        public bool IsInvulnerable { get { return Time.time < invulnerableUntil; } }
        public float InvulnerableTimeLeft { get { return Mathf.Max(0f, invulnerableUntil - Time.time); } }

        public float Radius
        {
            get
            {
                float massRatio = Mathf.Max(mass, 0.0001f) / Mathf.Max(referenceMass, 0.0001f);
                return radiusAtReferenceMass * Mathf.Sqrt(massRatio);
            }
        }

        // 质量越大越慢：倍率 = (参考质量 / 质量) ^ 0.2，夹在 [minSpeedFactor, 1]
        public float MoveSpeedFactor
        {
            get
            {
                float ratio = Mathf.Max(0.0001f, speedReferenceMass) / Mathf.Max(0.0001f, mass);
                return Mathf.Clamp(Mathf.Pow(ratio, 0.2f), minSpeedFactor, 1f);
            }
        }

        // 运行时出生走这里（分裂出的球 / AI / 复活），不走 SerializedObject
        public void Initialize(float startMass, Team teamId)
        {
            team = teamId;
            mass = Mathf.Clamp(startMass, minMass, maxMass);

            if (rb == null) rb = GetComponent<Rigidbody2D>();

            ApplySizeNow();
            MassChanged?.Invoke(this);
        }

        // 扣质量（吐球），扣完不能低于 lowerBound
        public bool TryConsumeMass(float amount, float lowerBound = 1f)
        {
            if (IsDead || amount <= 0f) return false;
            if (mass - amount < lowerBound) return false;

            mass -= amount;
            ApplySizeNow();
            MassChanged?.Invoke(this);
            return true;
        }

        // 吃食物和吃球都走这里，质量只增不减
        public void AddMass(float amount)
        {
            if (IsDead || amount <= 0f) return;

            float before = mass;
            mass = Mathf.Clamp(mass + amount, minMass, maxMass);
            if (Mathf.Approximately(before, mass)) return;

            ApplySizeNow();
            MassChanged?.Invoke(this);
        }

        public void SetMass(float value)
        {
            if (IsDead) return;

            mass = Mathf.Clamp(value, minMass, maxMass);
            ApplySizeNow();
            MassChanged?.Invoke(this);
        }

        // 质量要到对方的 eatMassRatio 倍，且对方不在复活保护里
        public bool CanEat(SizeController other)
        {
            if (other == null || ReferenceEquals(other, this)) return false;
            if (IsDead || other.IsDead) return false;
            if (other.IsInvulnerable) return false;

            return mass >= other.mass * eatMassRatio;
        }

        public bool TryEat(SizeController other)
        {
            if (!CanEat(other)) return false;

            float gain = other.mass;   // 先记下，Die 之后对方数据就不可信了
            other.Die(this);
            AddMassFromEat(gain);
            return true;
        }

        // 吃的统一入口：按难度乘倍率，难度只在这一处生效
        public void AddMassFromEat(float rawAmount)
        {
            if (IsDead || rawAmount <= 0f) return;

            float multiplier = team == Team.Player
                ? GameSettings.PlayerEatMultiplier
                : GameSettings.AiEatMultiplier;

            AddMass(rawAmount * multiplier);
        }

        // 只影响「被吃」，自己照样能吃别人，否则复活后什么都做不了
        public void SetInvulnerable(float seconds)
        {
            invulnerableUntil = seconds > 0f ? Time.time + seconds : 0f;
        }

        public void Die(SizeController killer)
        {
            if (IsDead) return;

            IsDead = true;
            Eaten?.Invoke(this, killer);
            Destroy(gameObject);
        }

        public void ApplySizeNow()
        {
            float scale = 2f * Radius;   // 精灵在 scale = 1 时直径正好 1 个世界单位
            transform.localScale = new Vector3(scale, scale, 1f);

            if (rb == null) rb = GetComponent<Rigidbody2D>();
            if (rb != null) rb.mass = Mathf.Max(mass, 0.01f);
        }
    }
}
