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
