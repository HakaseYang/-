using System.Collections.Generic;
using UnityEngine;

namespace QiuQiu
{
    // Wander / Chase / Flee state machine.
    // The scan is the expensive part: it runs on an interval, uses a pre-allocated
    // buffer with the non-allocating OverlapCircle overload, and each AI starts on a
    // random phase so they never all scan on the same frame.
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Rigidbody2D))]
    [RequireComponent(typeof(SizeController))]
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
        [SerializeField] private int scanBufferSize = 32;
        [SerializeField] private LayerMask scanLayerMask = ~0;

        [Header("Decide")]
        [SerializeField] private float fleeRadius = 6f;
        [SerializeField] private float arriveDistance = 0.6f;
        [SerializeField] private ChasePreference chasePreference = ChasePreference.Nearest;

        [Header("Wander")]
        [SerializeField] private float wanderChangeInterval = 2.5f;
        [SerializeField] private float borderAvoidMargin = 2.5f;
        [SerializeField] private float borderAvoidWeight = 2.2f;

        // what one scan found, kept as a struct so the pass has no side effects
        private struct ScanResult
        {
            public SizeController nearestThreat;
            public float threatDistanceSqr;
            public SizeController nearestPrey;
            public float preyDistanceSqr;
            public Edible nearestFood;
            public float foodDistanceSqr;
        }

        private Rigidbody2D rb;
        private SizeController self;
        private ContactFilter2D filter;
        private Collider2D[] scanBuffer;

        private AIState state = AIState.Wander;
        private Transform chaseTarget;
        private Edible chaseFood;
        private readonly List<SizeController> threats = new List<SizeController>(8);

        private Vector2 wanderDirection = Vector2.right;
        private float nextWanderChangeTime;
        private float nextScanTime;

        public AIState CurrentState { get { return state; } }

        // runtime spawns get a little personality so ten AIs do not move alike
        public void ApplyRandomProfile(float speedScale, float scanScale)
        {
            moveSpeed = Mathf.Max(0.1f, moveSpeed * Mathf.Max(0.1f, speedScale));
            scanRadius = Mathf.Clamp(scanRadius * Mathf.Max(0.1f, scanScale), 3f, 20f);
        }

        private void Awake()
        {
            rb = GetComponent<Rigidbody2D>();
            self = GetComponent<SizeController>();

            rb.gravityScale = 0f;
            rb.freezeRotation = true;
            rb.interpolation = RigidbodyInterpolation2D.Interpolate;

            scanBufferSize = Mathf.Clamp(scanBufferSize, 4, 128);
            scanBuffer = new Collider2D[scanBufferSize];

            filter = new ContactFilter2D
            {
                useTriggers = true,     // food and the eat sensor are triggers
                useLayerMask = scanLayerMask.value != ~0,
                layerMask = scanLayerMask
            };

            PickNewWanderDirection();
            nextScanTime = Time.time + Random.Range(0f, Mathf.Max(0.01f, scanInterval));
        }

        private void FixedUpdate()
        {
            if (self == null || self.IsDead) return;

            if (Time.time >= nextScanTime)
            {
                nextScanTime = Time.time + Mathf.Max(0.02f, scanInterval);
                Scan();
            }

            Vector2 desired = DecideDirection();
            desired = ApplyBorderAvoidance(desired);
            ApplyMovement(desired);

            if (ArenaManager.Instance != null)
            {
                ArenaManager.Instance.Constrain(rb, self.Radius);
            }
        }

        // ------------------------------ scan ------------------------------

        private void Scan()
        {
            ScanResult result = LookAround();
            DecideState(result);
            HandleArrivedTarget();

            if (state == AIState.Wander && Time.time >= nextWanderChangeTime)
            {
                PickNewWanderDirection();
            }
        }

        // one pass over the buffer: fills the threat list and the three nearest
        private ScanResult LookAround()
        {
            var result = new ScanResult
            {
                threatDistanceSqr = float.MaxValue,
                preyDistanceSqr = float.MaxValue,
                foodDistanceSqr = float.MaxValue
            };

            threats.Clear();

            int count = Physics2D.OverlapCircle(rb.position, scanRadius, filter, scanBuffer);
            for (int i = 0; i < count; i++)
            {
                Collider2D hit = scanBuffer[i];
                if (hit == null) continue;

                SizeController other = hit.GetComponentInParent<SizeController>();
                if (other != null && other != self && !other.IsDead)
                {
                    float sqrDistance = ((Vector2)other.transform.position - rb.position).sqrMagnitude;

                    if (other.CanEat(self))
                    {
                        if (!threats.Contains(other)) threats.Add(other);
                        if (sqrDistance < result.threatDistanceSqr)
                        {
                            result.threatDistanceSqr = sqrDistance;
                            result.nearestThreat = other;
                        }
                    }
                    else if (self.CanEat(other) && sqrDistance < result.preyDistanceSqr)
                    {
                        result.preyDistanceSqr = sqrDistance;
                        result.nearestPrey = other;
                    }

                    continue;
                }

                Edible food = hit.GetComponentInParent<Edible>();
                if (food != null)
                {
                    float sqrDistance = ((Vector2)food.transform.position - rb.position).sqrMagnitude;
                    if (sqrDistance < result.foodDistanceSqr)
                    {
                        result.foodDistanceSqr = sqrDistance;
                        result.nearestFood = food;
                    }
                }
            }

            return result;
        }

        private void DecideState(ScanResult result)
        {
            bool hasThreat = result.nearestThreat != null;
            bool hasPrey = result.nearestPrey != null;
            bool hasFood = result.nearestFood != null;

            if (hasThreat && result.threatDistanceSqr <= fleeRadius * fleeRadius)
            {
                EnterFlee();
                return;
            }

            // a threat that is closer than anything worth eating still wins
            if (hasThreat && (hasPrey || hasFood) &&
                result.threatDistanceSqr < Mathf.Min(result.preyDistanceSqr, result.foodDistanceSqr))
            {
                EnterFlee();
                return;
            }

            chaseTarget = null;
            chaseFood = null;

            if (chasePreference == ChasePreference.FoodFirst && hasFood)
            {
                SetFoodTarget(result.nearestFood);
            }
            else if (chasePreference == ChasePreference.PreyFirst && hasPrey)
            {
                SetPreyTarget(result.nearestPrey);
            }
            else if (hasPrey && result.preyDistanceSqr <= result.foodDistanceSqr)
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
            chaseFood = null;
        }

        private void SetFoodTarget(Edible food)
        {
            chaseFood = food;
            chaseTarget = food.transform;
        }

        private void SetPreyTarget(SizeController prey)
        {
            chaseFood = null;
            chaseTarget = prey.transform;
        }

        // stop buzzing around a target we already reached
        private void HandleArrivedTarget()
        {
            if (state != AIState.Chase || chaseTarget == null) return;

            Vector2 delta = (Vector2)chaseTarget.position - rb.position;
            if (delta.sqrMagnitude > arriveDistance * arriveDistance) return;

            chaseTarget = null;
            chaseFood = null;
            if (threats.Count == 0) state = AIState.Wander;
            PickNewWanderDirection();
        }

        // ------------------------------ decide / move ------------------------------

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
                        if (toTarget.sqrMagnitude > 0.0001f) return toTarget.normalized;
                    }
                    return wanderDirection;

                default:
                    if (Time.time >= nextWanderChangeTime) PickNewWanderDirection();
                    return wanderDirection;
            }
        }

        // away from every threat, nearest threat weighed heaviest
        private Vector2 FleeDirection()
        {
            Vector2 away = Vector2.zero;
            Vector2 position = rb.position;

            for (int i = 0; i < threats.Count; i++)
            {
                SizeController threat = threats[i];
                if (threat == null || threat.IsDead) continue;

                Vector2 offset = position - (Vector2)threat.transform.position;
                float sqrDistance = Mathf.Max(offset.sqrMagnitude, 0.01f);
                away += offset.normalized * (1f / sqrDistance);
            }

            if (away.sqrMagnitude < 0.0001f) return Random.insideUnitCircle.normalized;
            return away.normalized;
        }

        private void ApplyMovement(Vector2 desiredDirection)
        {
            if (desiredDirection.sqrMagnitude < 0.0001f) return;

            float speed = moveSpeed * (self != null ? self.MoveSpeedFactor : 1f);
            if (state == AIState.Chase) speed *= Mathf.Max(0.1f, chaseSpeedFactor);
            else if (state == AIState.Flee) speed *= Mathf.Max(0.1f, fleeSpeedFactor);

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

        // soft border: start leaning back inside before the hard clamp kicks in
        private Vector2 ApplyBorderAvoidance(Vector2 direction)
        {
            ArenaManager arena = ArenaManager.Instance;
            if (arena == null) return direction;
            if (arena.IsInside(rb.position, borderAvoidMargin)) return direction;

            Vector2 toCenter = (arena.Center - rb.position).normalized;
            Vector2 blended = direction + toCenter * borderAvoidWeight;

            if (blended.sqrMagnitude < 0.0001f) return toCenter;
            return blended.normalized;
        }

        private void PickNewWanderDirection()
        {
            Vector2 candidate = Random.insideUnitCircle;
            if (candidate.sqrMagnitude < 0.0001f) candidate = Vector2.right;

            wanderDirection = candidate.normalized;
            nextWanderChangeTime = Time.time + wanderChangeInterval * Random.Range(0.7f, 1.3f);
        }

        // ------------------------------ editor ------------------------------

        private void OnDrawGizmosSelected()
        {
            Gizmos.color = Color.yellow;
            Gizmos.DrawWireSphere(transform.position, scanRadius);

            Gizmos.color = new Color(1f, 0.4f, 0.4f, 0.9f);
            Gizmos.DrawWireSphere(transform.position, fleeRadius);

            if (chaseTarget != null)
            {
                Gizmos.color = Color.green;
                Gizmos.DrawLine(transform.position, chaseTarget.position);
            }

#if UNITY_EDITOR
            if (Application.isPlaying)
            {
                string target = chaseTarget != null ? chaseTarget.name : (chaseFood != null ? chaseFood.name : "-");
                UnityEditor.Handles.color = Color.white;
                UnityEditor.Handles.Label(transform.position + Vector3.up * 1.5f, state + " -> " + target);
            }
#endif
        }
    }
}
