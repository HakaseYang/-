using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

namespace QiuQiu
{
    public class PlayerController : MonoBehaviour
    {
        public static PlayerController Active { get; private set; }

        [Header("Input Asset")]
        [SerializeField] private InputActionAsset controls;
        [SerializeField] private string actionMapName = "Player";
        [SerializeField] private string moveActionName = "Move";
        [SerializeField] private string splitActionName = "Split";
        [SerializeField] private string spitActionName = "Spit";

        [Header("Movement")]
        [SerializeField] private float moveSpeed = 5f;
        [SerializeField] private float maxSpeedHardLimit = 12f;
        [SerializeField] private float acceleration = 60f;
        [SerializeField] private float inputDeadZone = 0.2f;

        [Header("Split")]
        [SerializeField] private float splitMinMass = 30f;
        [SerializeField] private float splitMassRatio = 0.5f;
        [SerializeField] private int maxBallCount = 8;
        [SerializeField] private float splitCooldown = 0.5f;
        [SerializeField] private float splitLaunchSpeed = 14f;
        [SerializeField] private float splitSpawnGap = 0.05f;

        [Header("Spit")]
        [SerializeField] private float spitMassCost = 10f;
        [SerializeField] private float spitFoodMass = 5f;
        [SerializeField] private float minMassAfterSpit = 5f;
        [SerializeField] private float spitCooldown = 0.25f;
        [SerializeField] private float spitLaunchSpeed = 16f;
        [SerializeField] private float spitSpawnGap = 0.15f;
        [SerializeField] private float spitArmDelay = 0.12f;

        [Header("Merge")]
        [SerializeField] private float mergeCooldown = 15f;
        [SerializeField] private float mergeContactTolerance = 0.15f;
        [SerializeField] private float mergePullDuration = 0.18f;
        [SerializeField] private float mergePullEase = 2f;

        [Header("Adoption")]
        [SerializeField] private bool autoAdoptPlayerBalls = true;

        private readonly InputReader playerInput = new InputReader();

        // the list of balls and the main ball live in PlayerRoster
        private readonly PlayerRoster roster = new PlayerRoster();

        // split / spit rules (cooldowns, mass math, spawn point) live in PlayerActions
        private readonly PlayerActions actions = new PlayerActions();

        private Vector2 moveInput;

        // split/spit aim: last input, right by default
        private Vector2 facing = Vector2.right;

        // merge rules (cooldown, pull animation, mass hand-over) live in PlayerMerge
        private readonly PlayerMerge merge = new PlayerMerge();

        [System.Serializable]
        public class PlayerTuning
        {
            public InputActionAsset controls;

            public float moveSpeed;
            public float maxSpeedHardLimit;
            public float acceleration;
            public float inputDeadZone;

            public float splitMinMass;
            public float splitMassRatio;
            public int maxBallCount;
            public float splitCooldown;
            public float splitLaunchSpeed;
            public float splitSpawnGap;

            public float spitMassCost;
            public float spitFoodMass;
            public float minMassAfterSpit;
            public float spitCooldown;
            public float spitLaunchSpeed;
            public float spitSpawnGap;
            public float spitArmDelay;

            public float mergeCooldown;
            public float mergeContactTolerance;
            public float mergePullDuration;
            public float mergePullEase;
        }

        public PlayerTuning CaptureTuning()
        {
            return new PlayerTuning
            {
                controls = controls,

                moveSpeed = moveSpeed,
                maxSpeedHardLimit = maxSpeedHardLimit,
                acceleration = acceleration,
                inputDeadZone = inputDeadZone,

                splitMinMass = splitMinMass,
                splitMassRatio = splitMassRatio,
                maxBallCount = maxBallCount,
                splitCooldown = splitCooldown,
                splitLaunchSpeed = splitLaunchSpeed,
                splitSpawnGap = splitSpawnGap,

                spitMassCost = spitMassCost,
                spitFoodMass = spitFoodMass,
                minMassAfterSpit = minMassAfterSpit,
                spitCooldown = spitCooldown,
                spitLaunchSpeed = spitLaunchSpeed,
                spitSpawnGap = spitSpawnGap,
                spitArmDelay = spitArmDelay,

                mergeCooldown = mergeCooldown,
                mergeContactTolerance = mergeContactTolerance,
                mergePullDuration = mergePullDuration,
                mergePullEase = mergePullEase,
            };
        }

        public void ApplyTuning(PlayerTuning tuning)
        {
            if (tuning == null)
            {
                return;
            }

            if (tuning.controls != null)
            {
                controls = tuning.controls;
            }
                
            moveSpeed = tuning.moveSpeed;
            maxSpeedHardLimit = tuning.maxSpeedHardLimit;
            acceleration = tuning.acceleration;
            inputDeadZone = tuning.inputDeadZone;

            splitMinMass = tuning.splitMinMass;
            splitMassRatio = tuning.splitMassRatio;
            maxBallCount = tuning.maxBallCount;
            splitCooldown = tuning.splitCooldown;
            splitLaunchSpeed = tuning.splitLaunchSpeed;
            splitSpawnGap = tuning.splitSpawnGap;

            spitMassCost = tuning.spitMassCost;
            spitFoodMass = tuning.spitFoodMass;
            minMassAfterSpit = tuning.minMassAfterSpit;
            spitCooldown = tuning.spitCooldown;
            spitLaunchSpeed = tuning.spitLaunchSpeed;
            spitSpawnGap = tuning.spitSpawnGap;
            spitArmDelay = tuning.spitArmDelay;

            mergeCooldown = tuning.mergeCooldown;
            mergeContactTolerance = tuning.mergeContactTolerance;
            mergePullDuration = tuning.mergePullDuration;
            mergePullEase = tuning.mergePullEase;

            SyncTools();
            ResolveActions();
        }


        // tuning snapshot used when respawning (owned by GameManager)
        private static PlayerTuning s_PendingTuning;

        private void Awake()
        {
            Active = this;

            playerInput.SplitPressed += OnSplitPressed;
            playerInput.SpitPressed += OnSpitPressed;

            // ApplyTuning already pushes the numbers and resolves the input
            if (s_PendingTuning != null)
            {
                PlayerTuning tuning = s_PendingTuning;
                s_PendingTuning = null;
                ApplyTuning(tuning);
                return;
            }

            SyncTools();
            ResolveActions();
        }

        //give Inspector Parameters to PlayerActions??PlayerMerge
        private void SyncTools()
        {
            PlayerTuning snapshot = CaptureTuning();
            actions.Configure(roster, SpawnBall, snapshot, name);
            merge.Configure(roster, snapshot);
        }

        private void OnDestroy()
        {
            if (Active == this)
            {
                Active = null;
            }

            for (int i = 0; i < roster.Count; i++)
            {
                SizeController ball = roster.Balls[i];
                if (ball != null)
                {
                    ball.Eaten -= OnBallEaten;
                }
            }
            roster.Clear();
        }

        private void Start()
        {
            if (autoAdoptPlayerBalls)
            {
                AdoptOrphanPlayerBalls();
            }
        }

        private void ResolveActions()
        {
            playerInput.Configure(controls, actionMapName, moveActionName, splitActionName, spitActionName);
            playerInput.Resolve();

            // the component may get the asset after OnEnable already ran
            if (isActiveAndEnabled)
            {
                playerInput.Enable();
            }
        }

        private void OnEnable()
        {
            playerInput.Enable();
        }

        private void OnDisable()
        {
            // shared InputActionAsset: only the controller that is still Active may switch it off
            if (Active != null && Active != this) return;

            playerInput.Disable();
            moveInput = Vector2.zero;
        }

        private void OnSplitPressed()
        {
            TrySplit();
        }

        private void OnSpitPressed()
        {
            TrySpit();
        }

        private void Update()
        {
            moveInput = playerInput.ReadMove();
        }

        private void FixedUpdate()
        {
            // 1. drop balls that died without firing the event
            roster.PruneDead(UnhookBall);

            // 2. adopt any orphan player balls
            if (roster.Count == 0 && autoAdoptPlayerBalls)
            {
                AdoptOrphanPlayerBalls();
            }

            // 3. main ball = largest alive ball
            roster.RefreshMain();

            // 4. only the main ball takes input, every other ball follows
            SyncSubDrive();

            // 5. input
            Vector2 input = Vector2.ClampMagnitude(moveInput, 1f);
            if (input.magnitude < inputDeadZone)
            {
                input = Vector2.zero;
            }
            if (input.sqrMagnitude > 0.0001f)
            {
                facing = input.normalized;
            }

            // 6. drive every ball that no sub-ball script owns(main ball)
            MovePlayerBalls(input);

            // 7. merge:  running pulls first, then look for new 
            merge.Tick(Time.fixedDeltaTime);
        }

        // the main ball takes input, every other ball follows through its own script
        private void SyncSubDrive()
        {
            SizeController main = roster.Main;
            int slot = 0;

            for (int i = 0; i < roster.Count; i++)
            {
                SizeController ball = roster.Balls[i];
                if (ball == null || ball.IsDead)
                {
                    continue;
                }

                PlayerSubBall sub = ball.GetComponent<PlayerSubBall>();

                if (ball == main)
                {
                    if (sub != null && !merge.IsMerging(ball))
                    {
                        sub.enabled = false;
                    }
                    continue;
                }

                if (merge.IsMerging(ball))
                {
                    continue;   // the merge pull owns this ball right now
                }

                slot++;
                if (sub == null)
                {
                    sub = ball.gameObject.AddComponent<PlayerSubBall>();
                    sub.Initialize(this, actions.SlotAngleFor(slot));
                }
                else if (!sub.enabled)
                {
                    sub.enabled = true;
                }
            }
        }

        private void MovePlayerBalls(Vector2 input)
        {
            for (int i = 0; i < roster.Count; i++)
            {
                SizeController ball = roster.Balls[i];
                if (ball == null || ball.IsDead)
                {
                    continue;
                }

                var sub = ball.GetComponent<PlayerSubBall>();
                if (sub != null && sub.enabled)
                {
                    continue;
                }

                MoveBall(ball, input);
            }
        }

        private void MoveBall(SizeController ball, Vector2 input)
        {
            var rb = ball.GetComponent<Rigidbody2D>();
            if (rb == null)
            {
                return;
            }
                
                // no input: zero the velocity outright
                if (input.sqrMagnitude <= 0.0001f)
            {
                rb.linearVelocity = Vector2.zero;

                if (ArenaManager.Instance != null)
                {
                    ArenaManager.Instance.Constrain(rb, ball.Radius);
                }

                return;
            }

            Vector2 targetVelocity = input * (moveSpeed * ball.MoveSpeedFactor);
            Vector2 current = rb.linearVelocity;

            Vector2 next = acceleration <= 0f
                ? targetVelocity
                : Vector2.MoveTowards(current, targetVelocity, acceleration * Time.fixedDeltaTime);

            if (next.sqrMagnitude > maxSpeedHardLimit * maxSpeedHardLimit)
            {
                next = Vector2.ClampMagnitude(next, maxSpeedHardLimit);
            }

            rb.linearVelocity = next;

            if (ArenaManager.Instance != null)
            {
                ArenaManager.Instance.Constrain(rb, ball.Radius);
            }
                
        }


        public void RegisterBall(SizeController ball)
        {
            if (ball == null || ball.IsDead)
            {
                return;
            }
            if (roster.Contains(ball))
            {
                return;
            }

            roster.Add(ball);
            ball.Eaten += OnBallEaten;

            merge.Register(ball);
        }

        public void UnregisterBall(SizeController ball)
        {
            if (ball == null)
            {
                return;
            }

            ball.Eaten -= OnBallEaten;
            roster.Remove(ball);
            merge.Drop(ball);
        }

        // one place unhooks a ball the roster dropped: events plus merge bookkeeping
        private void UnhookBall(SizeController ball)
        {
            ball.Eaten -= OnBallEaten;
            merge.Drop(ball);
        }

        private void AdoptOrphanPlayerBalls()
        {
            var registry = SizeController.Registry;
            for (int i = 0; i < registry.Count; i++)
            {
                SizeController ball = registry[i];
                if (ball == null || ball.IsDead || ball.Team != Team.Player)
                {
                    continue;
                }
                if (roster.Contains(ball))
                {
                    continue;
                }

                RegisterBall(ball);
            }
        }

        private void OnBallEaten(SizeController victim, SizeController killer)
        {
            victim.Eaten -= OnBallEaten;

            // a merged ball also fires Eaten,so it is merge?
            bool mergedIn = merge.IsMerging(victim);
            merge.Drop(victim); //clear merging

            bool wasMain = roster.Main == victim;
            roster.Remove(victim);

            if (mergedIn) return;   // self merge 

            string killerName = killer != null ? killer.name : "unknown";

            if (wasMain)
            {
                Debug.Log($"[player] main ball eaten by {killerName}, {roster.Count} left");

                if (roster.Main != null)
                {
                    Debug.Log($"[player] control moved to {roster.Main.name} ({roster.Main.Mass:F0}), {roster.Count} left");
                }
            }
            else
            {
                Debug.Log($"[player] sub ball eaten, {roster.Count} left");
            }

            if (roster.Count == 0) NotifyDefeat(killer);
        }

        private void NotifyDefeat(SizeController killer)
        {
            Debug.Log($"[player] every ball is gone, game over (eaten by {(killer != null ? killer.name : "unknown")})");

            if (GameManager.Instance != null)
            {
                GameManager.Instance.NotifyPlayerDefeated(killer);
            }
        }


        public bool TrySplit()
        {
            SizeController source;
            SizeController newBall;
            float newMass;

            if (!actions.TrySplit(facing, out source, out newBall, out newMass))
            {
                return false;
            }

            // SyncSubDrive gives the new ball its follow script on the next physics step

            // a fresh split ball has to wait out the merge cooldown, and so does the
            // mother: otherwise the half without an entry merges back on contact
            merge.MarkSplit(newBall);
            merge.MarkSplit(source);

            Debug.Log($"[split] {source.Mass + newMass:F0} -> {source.Mass:F0} + {newMass:F0}, balls {roster.Count}/{maxBallCount}");
            if (AudioManager.Instance != null)
            {
                AudioManager.Instance.PlaySplit();
            }
            return true;
        }

        public bool TrySpit()
        {
            SizeController source;

            if (!actions.TrySpit(facing, out source))
            {
                return false;
            }
            Debug.Log($"[spit] {source.Mass:F0} left, food mass {spitFoodMass:F0}");
            if (AudioManager.Instance != null)
            {
                AudioManager.Instance.PlaySpit();
            }
                return true;
        }

        public SizeController SpawnBall(string ballName, Vector3 position, float mass, Sprite sprite, Color color)
        {
            if (sprite == null)
            {
                Debug.LogError("[PlayerController] SpawnBall needs a sprite");
                return null;
            }

            GameObject go = BallFactory.CreateBall(ballName, position, mass, sprite, color, Team.Player);
            go.tag = "Player";

            SizeController ball = go.GetComponent<SizeController>();
            RegisterBall(ball);
            return ball;
        }

        //just for PlayerRoot have or work (normal not work)
        public static PlayerController EnsureExists()
        {
            if (Active != null)
            {
                return Active;
            }

            var found = FindFirstObjectByType<PlayerController>();
            if (found != null)
            {
                Active = found;
                return found;
            }

            var go = new GameObject("PlayerRoot");
            var created = go.AddComponent<PlayerController>();
            Active = created;
            return created;
        }


        public bool CanMerge(SizeController ball)
        {
            return merge.CanMerge(ball);
        }

        public bool IsMerging(SizeController ball)
        {
            return merge.IsMerging(ball);
        }


        public void TriggerSplit()
        {
            TrySplit();
        }

        public void TriggerSpit()
        {
            TrySpit();
        }


        public Vector2 InputVelocity
        {
            get
            {
                Vector2 input = Vector2.ClampMagnitude(moveInput, 1f);
                if (input.magnitude < inputDeadZone)
                {
                    return Vector2.zero;
                }
                return input * moveSpeed;
            }
        }

        public SizeController MainBall { get { return roster.Main; } }

        public int BallCount { get { return roster.Count; } }

        public IReadOnlyList<SizeController> Balls { get { return roster.Balls; } }

        public float TotalMass { get { return roster.TotalMass; } }
    }
}
