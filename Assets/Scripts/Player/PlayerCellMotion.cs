using UnityEngine;

namespace QiuQiu
{
    public enum CellMotionState
    {
        Normal,
        SplitLaunching,
        SplitCoasting,
        Following,
        Merging,
        Disabled
    }

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

        [Tooltip("Cells may float this many body-widths apart before the leash pulls them back. 1 = always touching")]
        [SerializeField] private float leashSlack = 1.6f;

        [Tooltip("Share of the separation turned sideways, so a small cell slides around the big one instead of sticking behind it")]
        [SerializeField, Range(0f, 1f)] private float slideAround = 0.55f;

        [Tooltip("How fast a cell answers when the target speed points against the way it is already going. Lower = heavier")]
        [SerializeField] private float reverseRate = 8f;

        [Header("Split slide")]
        [Tooltip("How big a new piece is when it appears inside its parent, relative to its final size")]
        [SerializeField, Range(0.05f, 1f)] private float splitStartScale = 0.2f;

        private PlayerController owner;
        private SizeController self;
        private Rigidbody2D rb;
        private SpriteRenderer shell;
        private Collider2D[] colliders;

        private CellMotionState state = CellMotionState.Disabled;

        private Vector2 splitFrom;
        private Vector2 splitTo;
        private Vector2 splitDirection = Vector2.right;
        private float splitElapsed;
        private float splitDuration = 0.3f;
        private float splitEndRadius;
        private float splitImpulse;

        private float coastElapsed;
        private float coastDuration = 0.45f;

        private SizeController mergeKeeper;
        private float mergeEase = 2f;
        private System.Action mergeFinished;
        private int mergeStartOrder;

        private const float mergeSteer = 0.85f;

        private float mergeCrawlSpeed = 0.7f;
        private float mergeEatDepth = 0.35f;

        public CellMotionState State { get { return state; } }

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

        public void ApplyMotionTuning(
            float accel, float decel, float speedLimit,
            float followAccel, float strength, float damping, float maxDrift,
            float reverseSpeed, float slide, float leash,
            float crawlSpeed, float eatDepth)
        {
            acceleration = accel;
            deceleration = decel;
            maxSpeedHardLimit = speedLimit;
            followAcceleration = followAccel * GamePacing.CellFollow;
            homeStrength = strength;
            homeDamping = damping;
            maxDriftSpeed = maxDrift;
            reverseRate = reverseSpeed;
            slideAround = Mathf.Clamp01(slide);
            leashSlack = Mathf.Max(1f, leash);
            mergeCrawlSpeed = Mathf.Max(0.05f, crawlSpeed);
            mergeEatDepth = Mathf.Clamp(eatDepth, 0.05f, 1f);
        }


        public void SetFollowing(bool isMain)
        {
            if (IsBusy)
            {
                return;
            }
            state = isMain ? CellMotionState.Normal : CellMotionState.Following;
        }

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
                self.SuppressVisual = true;
                self.IsEjecting = true;
                self.SetInvulnerable(splitDuration + coastDuration + 0.1f);
            }

            if (rb != null)
            {
                rb.bodyType = RigidbodyType2D.Kinematic;
                rb.position = splitFrom;
                rb.linearVelocity = Vector2.zero;
            }

            ApplyRadius(splitEndRadius * splitStartScale);
        }

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

        public void BeginMerge(SizeController keeper, float crawlSpeed, float eatDepth, float ease, System.Action onFinished)
        {
            CacheParts();

            mergeKeeper = keeper;
            mergeCrawlSpeed = Mathf.Max(0.05f, crawlSpeed);
            mergeEatDepth = Mathf.Clamp(eatDepth, 0.05f, 1f);
            mergeEase = Mathf.Max(0.01f, ease);
            mergeFinished = onFinished;

            state = CellMotionState.Merging;


            if (rb != null)
            {
                rb.bodyType = RigidbodyType2D.Dynamic;
                rb.linearVelocity = Vector2.zero;
            }

            if (shell != null)
            {
                mergeStartOrder = shell.sortingOrder;
                SpriteRenderer keeperShell = keeper != null ? keeper.GetComponent<SpriteRenderer>() : null;
                if (keeperShell != null)
                {
                    shell.sortingOrder = keeperShell.sortingOrder + 1;
                }
            }
        }

        public void AbortMerge()
        {
            if (state != CellMotionState.Merging)
            {
                return;
            }

            mergeKeeper = null;
            mergeFinished = null;

            SetCollidersEnabled(true);

            if (shell != null)
            {
                shell.sortingOrder = mergeStartOrder;
            }

            if (self != null)
            {
                self.ApplySizeNow();
            }

            if (rb != null)
            {
                rb.bodyType = RigidbodyType2D.Dynamic;
                rb.linearVelocity = Vector2.zero;
            }

            state = CellMotionState.Following;
        }

        public bool IsMergingInto(SizeController keeper)
        {
            return state == CellMotionState.Merging && mergeKeeper == keeper;
        }


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

            if (leader == null || leader == self)
            {
                Steer(owner.MoveInput * (owner.MoveSpeed * self.MoveSpeedFactor), followAcceleration, followAcceleration);
                return;
            }

            Vector2 position = rb != null ? rb.position : (Vector2)transform.position;
            Vector2 offset = position - (Vector2)leader.transform.position;
            float distance = offset.magnitude;
            float restDistance = leader.Radius + self.Radius;
            Vector2 direction = distance > 0.0001f ? offset / distance : Vector2.right;

            float weight = Mathf.Clamp(Mathf.Pow(60f / Mathf.Max(1f, self.Mass), 0.2f), 0.4f, 1.6f);
            float response = followAcceleration * weight;

            Vector2 closing = Vector2.zero;

            if (distance < restDistance)
            {
                float overlap = restDistance - distance;
                Vector2 push = direction * Mathf.Min(overlap * homeStrength, maxDriftSpeed * weight);

                Vector2 tangent = new Vector2(-direction.y, direction.x);
                float side = Vector2.Dot(rb != null ? rb.linearVelocity : Vector2.zero, tangent) >= 0f ? 1f : -1f;

                closing = push * (1f - slideAround) + tangent * (side * push.magnitude * slideAround);
            }
            else if (distance > restDistance * leashSlack)
            {
                float lag = distance - restDistance * leashSlack;
                closing = -direction * Mathf.Min(lag * homeStrength * 0.5f, maxDriftSpeed * 0.6f);
            }

            if (distance > restDistance * 1.02f)
            {
                closing += -direction * mergeCrawlSpeed;
            }

            if (owner.CanMerge(self))
            {
                SizeController partner;
                if (owner.TryFindMergePartner(self, out partner) && partner != null)
                {
                    Vector2 toPartner = (Vector2)partner.transform.position - position;
                    float partnerDistance = toPartner.magnitude;
                    if (partnerDistance > 0.0001f)
                    {
                        closing += toPartner / partnerDistance * mergeCrawlSpeed;
                    }
                }
            }

            Vector2 ownVelocity = owner.MoveInput * (owner.MoveSpeed * self.MoveSpeedFactor);
            Vector2 groupVelocity = owner.GroupVelocity;
            Vector2 baseVelocity = ownVelocity.sqrMagnitude >= groupVelocity.sqrMagnitude ? ownVelocity : groupVelocity;

            Steer(baseVelocity + closing, response, response);
        }

        private void Steer(Vector2 targetVelocity, float pushRate, float idleRate)
        {
            if (rb == null)
            {
                return;
            }

            bool pushing = targetVelocity.sqrMagnitude > 0.0001f;
            if (!pushing)
            {
                Constrain();
                return;
            }

            float rate = pushRate;

            Vector2 current = rb.linearVelocity;
            if (pushing && current.sqrMagnitude > 0.0001f && reverseRate > 0f)
            {
                float align = Vector2.Dot(current.normalized, targetVelocity.normalized);
                if (align < 0.35f)
                {
                    float heaviness = Mathf.InverseLerp(0.35f, -1f, align);
                    rate = Mathf.Lerp(rate, reverseRate, heaviness);
                }
            }

            Vector2 next = rate <= 0f
                ? targetVelocity
                : Vector2.MoveTowards(current, targetVelocity, rate * Time.fixedDeltaTime);

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
            float eased = t * t * (3f - 2f * t);

            if (rb != null)
            {
                rb.MovePosition(Vector2.Lerp(splitFrom, splitTo, eased));
            }

            ApplyRadius(Mathf.Lerp(splitEndRadius * splitStartScale, splitEndRadius, eased));

            if (t < 1f)
            {
                return;
            }

            if (rb != null)
            {
                rb.bodyType = RigidbodyType2D.Dynamic;
                rb.position = splitTo;
                rb.linearVelocity = owner != null ? owner.GroupVelocity : Vector2.zero;
            }

            state = CellMotionState.SplitCoasting;
            coastElapsed = 0f;
        }

        private void TickCoast()
        {
            coastElapsed += Time.fixedDeltaTime;
            float t = Mathf.Clamp01(coastElapsed / Mathf.Max(0.05f, coastDuration));

            Vector2 groupVelocity = owner != null ? owner.GroupVelocity : Vector2.zero;

            float fade = Mathf.Exp(-3.5f * t);
            float outward = splitImpulse * fade;
            float inward = mergeCrawlSpeed * (1f - fade);
            Vector2 velocity = groupVelocity + splitDirection * (outward - inward);

            float limit = Mathf.Max(maxSpeedHardLimit, splitImpulse);
            if (limit > 0f && velocity.sqrMagnitude > limit * limit)
            {
                velocity = Vector2.ClampMagnitude(velocity, limit);
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

            Vector2 keeperPos = mergeKeeper.transform.position;
            Vector2 position = rb != null ? rb.position : (Vector2)transform.position;
            Vector2 offset = keeperPos - position;
            float distance = offset.magnitude;
            float contact = mergeKeeper.Radius + self.Radius;
            Vector2 direction = distance > 0.0001f ? offset / distance : Vector2.zero;

            if (distance > contact * 1.15f)
            {
                AbortMerge();
                return;
            }

            if (rb != null)
            {
                Vector2 steer = owner != null ? owner.MoveInput * (owner.MoveSpeed * self.MoveSpeedFactor) : Vector2.zero;
                rb.linearVelocity = direction * mergeCrawlSpeed + steer * mergeSteer;
            }

            if (shell != null)
            {
                SpriteRenderer keeperShell = mergeKeeper.GetComponent<SpriteRenderer>();
                int keeperOrder = keeperShell != null ? keeperShell.sortingOrder : shell.sortingOrder;
                shell.sortingOrder = keeperOrder + 2;
            }

            float covered = Mathf.Max(mergeKeeper.Radius - self.Radius, mergeKeeper.Radius * mergeEatDepth);
            if (distance > covered)
            {
                return;
            }

            state = CellMotionState.Disabled;

            System.Action finished = mergeFinished;
            mergeFinished = null;
            mergeKeeper = null;

            if (finished != null)
            {
                finished();
            }
        }

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
