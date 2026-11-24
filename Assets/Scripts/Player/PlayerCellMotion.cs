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
        private float mergeEase = 2f;
        private System.Action mergeFinished;
        private int mergeStartOrder;

        // How much the player's stick still steers a cell that is being swallowed. 0 = the cell
        // marches straight into the body, 1 = the stick moves it as strongly as the crawl does.
        private const float mergeSteer = 0.85f;

        private float mergeCrawlSpeed = 0.7f;
        private float mergeEatDepth = 0.35f;

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

        public void BeginMerge(SizeController keeper, float crawlSpeed, float eatDepth, float ease, System.Action onFinished)
        {
            CacheParts();

            mergeKeeper = keeper;
            mergeCrawlSpeed = Mathf.Max(0.05f, crawlSpeed);
            mergeEatDepth = Mathf.Clamp(eatDepth, 0.05f, 1f);
            mergeEase = Mathf.Max(0.01f, ease);
            mergeFinished = onFinished;

            state = CellMotionState.Merging;

            // Colliders stay ON. A cell that is busy being swallowed is still a normal target for
            // enemies: if a bigger ball covers it first, this cell dies and the merge is dropped.
            // Its own keeper cannot eat it (Eater ignores player vs player).

            if (rb != null)
            {
                rb.bodyType = RigidbodyType2D.Dynamic;
                rb.linearVelocity = Vector2.zero;
            }

            // Draw the melting cell on top of the keeper: behind it, the shrink would be hidden
            // and the merge would look like a snap again.
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

            Vector2 position = rb != null ? rb.position : (Vector2)transform.position;
            Vector2 offset = position - (Vector2)leader.transform.position;
            float distance = offset.magnitude;
            float restDistance = leader.Radius + self.Radius;
            Vector2 direction = distance > 0.0001f ? offset / distance : Vector2.right;

            // Heavy feels heavy: a small cell answers its own input faster than a big one, which is
            // also what lets it catch up and slide around the leader instead of trailing behind.
            float weight = Mathf.Clamp(Mathf.Pow(60f / Mathf.Max(1f, self.Mass), 0.2f), 0.4f, 1.6f);
            float response = followAcceleration * weight;

            // A sub cell is NOT glued to the big one. It only gets pushed when the bodies overlap,
            // and only gets pulled when the leash is stretched to its max.
            Vector2 closing = Vector2.zero;

            if (distance < restDistance)
            {
                float overlap = restDistance - distance;
                Vector2 push = direction * Mathf.Min(overlap * homeStrength, maxDriftSpeed * weight);

                // part of the push is sideways: that makes a slower big cell squeeze the small one
                // out around its side rather than wedging it at the back
                Vector2 tangent = new Vector2(-direction.y, direction.x);
                float side = Vector2.Dot(rb != null ? rb.linearVelocity : Vector2.zero, tangent) >= 0f ? 1f : -1f;

                closing = push * (1f - slideAround) + tangent * (side * push.magnitude * slideAround);
            }
            else if (distance > restDistance * leashSlack)
            {
                float lag = distance - restDistance * leashSlack;
                closing = -direction * Mathf.Min(lag * homeStrength * 0.5f, maxDriftSpeed * 0.6f);
            }

            // The trip home does not wait for the cooldown. A cell that flew out on a split starts
            // drifting back the moment the launch impulse is spent, so it never parks at the far
            // end and then snaps back. Only the swallow itself waits for the cooldown.
            if (distance > restDistance * 1.02f)
            {
                closing += -direction * mergeCrawlSpeed;
            }

            // Once the cooldown is over a cell aims at the nearest cell it may melt into. That is
            // not always the main ball: two sub cells are allowed to join each other, so the target
            // is whichever partner is closest and ready.
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

            // A small cell moves at its own (faster) speed; the bigger one uses the group speed.
            Vector2 ownVelocity = owner.MoveInput * (owner.MoveSpeed * self.MoveSpeedFactor);
            Vector2 groupVelocity = owner.GroupVelocity;
            Vector2 baseVelocity = ownVelocity.sqrMagnitude >= groupVelocity.sqrMagnitude ? ownVelocity : groupVelocity;

            Steer(baseVelocity + closing, response, response);
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
            if (!pushing)
            {
                // No input: a heavy ball keeps gliding. There is no friction here on purpose -
                // the player stops it by pushing the other way.
                Constrain();
                return;
            }

            float rate = pushRate;

            // Reversing a heavy ball is not instant: when the target points back against the way we
            // are already moving, answer with reverseRate instead of the full push rate.
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

            // Fly out, slow down, turn around and start coming back - one continuous motion, no
            // stop in between. The outward part fades exactly as fast as the inward part grows.
            float fade = Mathf.Exp(-3.5f * t);          // 1 -> 0.03
            float outward = splitImpulse * fade;
            float inward = mergeCrawlSpeed * (1f - fade);
            Vector2 velocity = groupVelocity + splitDirection * (outward - inward);

            // A split launch is meant to be faster than normal movement, so the ordinary speed
            // cap must not eat the impulse. Coasting speed is allowed up to the impulse itself.
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

            // Shoved away by the player: give up, keep every gram of mass.
            if (distance > contact * 1.15f)
            {
                AbortMerge();
                return;
            }

            // Crawl in slowly, still full size: it must read as the cell sliding into the body,
            // not as it shrinking away on the spot. The player can still steer it - the stick
            // direction is added on top, so the cell drifts off course while being swallowed.
            if (rb != null)
            {
                Vector2 steer = owner != null ? owner.MoveInput * (owner.MoveSpeed * self.MoveSpeedFactor) : Vector2.zero;
                rb.linearVelocity = direction * mergeCrawlSpeed + steer * mergeSteer;
            }

            // Draw it above the keeper, otherwise it vanishes behind the big body while sliding in.
            if (shell != null)
            {
                SpriteRenderer keeperShell = mergeKeeper.GetComponent<SpriteRenderer>();
                int keeperOrder = keeperShell != null ? keeperShell.sortingOrder : shell.sortingOrder;
                shell.sortingOrder = keeperOrder + 2;
            }

            // Covered = eaten. Full cover means the small body fits inside the big one, i.e. its
            // centre is at most (keeperRadius - ownRadius) away. When the two are nearly the same
            // size that distance is close to zero, so a fraction of the keeper radius is used as a
            // floor - otherwise equal halves could never finish merging.
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
                finished();   // PlayerMerge drops this cell from the roster
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
