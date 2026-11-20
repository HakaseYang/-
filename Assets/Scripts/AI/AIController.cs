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

        
        private struct ScanResult
        {
            public SizeController nearestThreat;
            public float threatGap;          // surface distance, negative once the bodies touch
            public SizeController nearestPrey;
            public float preyGap;
            public Edible nearestFood;
            public float foodGap;
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

            scanBufferSize = 32;
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
            ApplyMovement(desired); //go

            //for ball go back to area(if go out)
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
                foodGap = float.MaxValue
            };

            threats.Clear();
            //check circle have? other Collider2D (AI/Player)
            int count = Physics2D.OverlapCircle(rb.position, scanRadius, filter, scanBuffer);
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
                        float gap = Mathf.Sqrt(sqrDistance) - other.Radius;  // who is closest by surface, not by center
                        if (gap < result.threatGap)
                        {
                            result.threatGap = gap;
                            result.nearestThreat = other;
                        }
                    }
                    else if (self.CanEat(other))
                    {
                        float gap = Mathf.Sqrt(sqrDistance) - other.Radius;
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
                    float gap = Mathf.Sqrt(sqrDistance) - food.Radius;
                    if (gap < result.foodGap)
                    {
                        result.foodGap = gap;
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

            if (hasThreat && result.threatGap <= fleeRadius)
            {
                EnterFlee();
                return;
            }

            // all three are surface gaps now, so they compare directly
            if (hasThreat && (hasPrey || hasFood) &&
                result.threatGap <= Mathf.Min(result.preyGap, result.foodGap))
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
