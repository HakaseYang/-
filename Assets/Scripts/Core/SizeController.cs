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
            StripSolidColliders();
            ApplySizeNow();
        }

        // Balls never push each other here: eating needs one ball to slide over the other, and
        // a solid collider stops exactly that. Factory made balls only get a trigger, but a ball
        // placed by hand in the scene can still carry a solid one, so it is removed on spawn.
        private void StripSolidColliders()
        {
            Collider2D[] colliders = GetComponents<Collider2D>();
            for (int i = 0; i < colliders.Length; i++)
            {
                if (colliders[i] != null && !colliders[i].isTrigger)
                {
                    Destroy(colliders[i]);
                }
            }
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
