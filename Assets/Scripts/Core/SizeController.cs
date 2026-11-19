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

        [Header("Mass To Radius")]
        [SerializeField] private float radiusAtReferenceMass = 0.75f;

        [Header("Eating Ball Threshold")]
        [SerializeField, Range(1f, 2f)] private float eatMassRatio = 1.2f;   

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

        private Rigidbody2D rb;
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

        public float Radius
        {
            get
            {   //Mathf for / protect
                float massRatio = Mathf.Max(mass, 0.0001f) / Mathf.Max(referenceMass, 0.0001f);
                return radiusAtReferenceMass * Mathf.Sqrt(massRatio);
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
            other.Die(this);
            AddMassFromEat(gain);
            return true;
        }

        // for eat,Multiply by difficulty
        public void AddMassFromEat(float rawAmount)
        {
            if (IsDead || rawAmount <= 0f)
            {
                return;
            }

            float multiplier = team == Team.Player  //Multiply
                ? GameSettings.PlayerEatMultiplier  //is
                : GameSettings.AiEatMultiplier;     //not

            AddMass(rawAmount * multiplier);
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
            float scale = 2f * Radius;   
            transform.localScale = new Vector3(scale, scale, 1f);

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
