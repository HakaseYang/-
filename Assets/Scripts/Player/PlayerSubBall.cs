using UnityEngine;

namespace QiuQiu
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Rigidbody2D))]
    [RequireComponent(typeof(SizeController))]
    public class PlayerSubBall : MonoBehaviour
    {
        [Header("Follow")]
        [SerializeField] private float followStrength = 8f;

        [SerializeField] private float maxFollowSpeed = 18f;

        [SerializeField] private float followAcceleration = 80f;

        [SerializeField] private float slotGap = 0.06f;

        [SerializeField] private bool returnHomeWhenMergeReady = true;

        [SerializeField] private float returnHomeStrengthMultiplier = 1.2f;

        [SerializeField] private float returnHomeMaxSpeed = 4f;

        [SerializeField] private float farAwayBoost = 2.5f;

        [SerializeField] private float driftDeadzone = 0.25f;

        private PlayerController leaderController;
        private SizeController self;
        private Rigidbody2D rb;
        private float slotAngle;

        public void Initialize(PlayerController controller, float slotAngleDegrees)
        {
            leaderController = controller;
            slotAngle = slotAngleDegrees * Mathf.Deg2Rad;
        }

        private void Awake()
        {
            rb = GetComponent<Rigidbody2D>();
            self = GetComponent<SizeController>();
        }

        private void FixedUpdate()
        {
            if (self == null || self.IsDead) return;

            // no controller means no player at all, only then stop
            if (leaderController == null)
            {
                leaderController = PlayerController.Active;
                if (leaderController == null)
                {
                    StopGradually();
                    return;
                }
            }

            // the leader can be eaten at any time, so read it every frame
            SizeController leader = leaderController.MainBall;
            Vector2 inputVelocity = leaderController.InputVelocity;

            if (leader == null || leader == self)
            {
                DriveAsLeader(inputVelocity);
                return;
            }

            DriveInFormation(leader, inputVelocity);
        }

        // we are the main ball (or the last one): take the input directly
        private void DriveAsLeader(Vector2 inputVelocity)
        {
            if (inputVelocity.sqrMagnitude <= 0.0001f)
            {
                rb.linearVelocity = Vector2.zero;
            }
            else
            {
                Vector2 direct = Vector2.ClampMagnitude(inputVelocity, maxFollowSpeed);
                rb.linearVelocity = Vector2.MoveTowards(
                    rb.linearVelocity, direct, followAcceleration * Time.fixedDeltaTime);
            }

            Constrain();
        }

        private void DriveInFormation(SizeController leader, Vector2 inputVelocity)
        {
            Vector2 leaderPosition = leader.transform.position;
            float distance = leader.Radius + self.Radius + slotGap;
            Vector2 direction = new Vector2(Mathf.Cos(slotAngle), Mathf.Sin(slotAngle));
            Vector2 target = leaderPosition + direction * distance;

            float strength = followStrength;
            float speedLimit = maxFollowSpeed;

            // pull harder when we fall behind
            float currentDistance = Vector2.Distance(rb.position, leaderPosition);
            if (currentDistance > distance * 2f)
            {
                float t = Mathf.InverseLerp(distance * 2f, distance * 6f, currentDistance);
                strength *= Mathf.Lerp(1f, Mathf.Max(1f, farAwayBoost), t);
            }

            // once the cooldown ends the slot collapses onto the leader and the merge starts
            bool canMergeNow = returnHomeWhenMergeReady && leaderController.CanMerge(self);
            if (canMergeNow)
            {
                target = leaderPosition;
                strength = followStrength * Mathf.Max(1f, returnHomeStrengthMultiplier);
                speedLimit = Mathf.Min(maxFollowSpeed, Mathf.Max(0.1f, returnHomeMaxSpeed));
            }

            Vector2 inputTerm = Vector2.ClampMagnitude(inputVelocity, speedLimit);
            Vector2 spring = (target - rb.position) * strength;
            float correctionBudget = Mathf.Max(0f, speedLimit - inputTerm.magnitude);
            Vector2 correction = Vector2.ClampMagnitude(spring, correctionBudget);

            Vector2 desired = inputTerm + correction;

            bool hasInput = inputVelocity.sqrMagnitude > 0.01f;
            bool slotSettled = !canMergeNow && (target - rb.position).sqrMagnitude < driftDeadzone * driftDeadzone;

            if (hasInput || canMergeNow)
            {
                var leaderRb = leader.GetComponent<Rigidbody2D>();
                if (leaderRb != null) desired += leaderRb.linearVelocity * 0.5f;
            }

            desired = Vector2.ClampMagnitude(desired, speedLimit);

            // on the slot and idle: stop dead, the spring residue never decays on its own
            if (slotSettled && !hasInput)
            {
                rb.linearVelocity = Vector2.zero;
                Constrain();
                return;
            }

            if (desired.sqrMagnitude < driftDeadzone * driftDeadzone)
            {
                rb.linearVelocity = Vector2.zero;
            }
            else
            {
                rb.linearVelocity = Vector2.MoveTowards(
                    rb.linearVelocity, desired, followAcceleration * Time.fixedDeltaTime);
            }

            Constrain();
        }

        private void StopGradually()
        {
            rb.linearVelocity = Vector2.MoveTowards(
                rb.linearVelocity, Vector2.zero, followAcceleration * Time.fixedDeltaTime);
        }

        // the project has no wall colliders, the arena edge is a soft clamp
        private void Constrain()
        {
            if (ArenaManager.Instance != null)
            {
                ArenaManager.Instance.Constrain(rb, self.Radius);
            }
        }
    }
}
