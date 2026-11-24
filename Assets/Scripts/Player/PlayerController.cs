using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace QiuQiu
{
    public class PlayerController : MonoBehaviour
    {
        public static PlayerController Active { get; private set; }

        [Header("Movement")]
        [SerializeField] private float moveSpeed = 4.2f;
        [SerializeField] private float maxSpeedHardLimit = 10.5f;
        [SerializeField] private float acceleration = 38f;
        [Tooltip("How fast a ball bleeds off speed when nobody pushes it. Low = it coasts like a heavy object.")]
        [SerializeField] private float deceleration = 1.2f;
        [Tooltip("How fast a ball answers when the stick is pushed the opposite way. Lower = heavier.")]
        [SerializeField] private float reverseRate = 8f;
        [SerializeField] private float inputDeadZone = 0.2f;

        [Header("Split")]
        [SerializeField] private float splitMinMass = 30f;
        [SerializeField] private float splitMassRatio = 0.5f;
        [SerializeField] private int maxBallCount = 16;
        [SerializeField] private float splitCooldown = 0.5f;
        [Tooltip("Split distance = (parent radius + piece radius) x this. The piece is pushed this far, then the launch impulse carries it further out before it drifts back")]
        [SerializeField, Range(1.05f, 3f)] private float splitDistanceMultiplier = 1.55f;
        [Tooltip("Launch impulse = group move speed x this. The piece flies out, slows down, then the follow spring pulls it back in")]
        [SerializeField] private float splitImpulseFactor = 4.5f;
        [Tooltip("How long the launch impulse takes to fade to nothing, in seconds")]
        [SerializeField] private float splitCoastDuration = 0.75f;
        [Tooltip("How long a new split piece takes to slide out of its parent, in seconds")]
        [SerializeField] private float splitEjectDuration = 0.18f;
        [Tooltip("Aim split / spit at the mouse when a mouse is present (Agar.io), otherwise at the movement direction")]
        [SerializeField] private bool aimAtMouse = false;

        [Header("Player Arrows")]
        [Tooltip("Empty space between the ball edge and its arrow, in world units")]
        [SerializeField] private float markerGap = 0.35f;

        [Header("Spit Stance")]
        [Tooltip("How fast pushing the stick in changes the spit stance. 2.5 means about 0.4 s from perpendicular to centre")]
        [SerializeField] private float centerAimSpeed = 2.5f;

        [Header("Sub Ball Offset")]
        [Tooltip("How hard a split piece is nudged back beside the big ball. Higher = closes the gap faster")]
        [SerializeField] private float subHomeStrength = 1.8f;
        [Tooltip("How quickly it answers to that nudge. Higher = snappier, no overshoot")]
        [SerializeField] private float subHomeDamping = 3f;
        [Tooltip("How fast a sub cell may drift back beside the big cell. Keep it low: a sub " +
                 "cell is allowed a short catch up, not a permanent speed advantage.")]
        [SerializeField] private float subMaxDriftSpeed = 4f;
        [Tooltip("Fallback follow acceleration, only used when there is no big ball to sit beside")]
        [SerializeField] private float subFollowAcceleration = 22f;

        [Tooltip("Share of a body overlap turned sideways, so a small cell slides around the big one")]
        [SerializeField, Range(0f, 1f)] private float slideAround = 0.55f;
        [Tooltip("How many body-widths a cell may drift away before the leash pulls it back. 1 = always touching")]
        [SerializeField] private float leashSlack = 1.6f;

        [Header("Spit")]
        [Tooltip("Mass taken from the cell per pellet. Kept low so a freshly split sub cell can still spit")]
        [SerializeField] private float spitMassCost = 8f;
        [SerializeField] private float spitFoodMass = 6f;
        [Tooltip("A pellet carries at most this fraction of the mass that was paid for it, so spitting can never print free weight")]
        [SerializeField, Range(0.1f, 1f)] private float spitReturnRatio = 0.8f;
        [Tooltip("A cell needs more than spitMassCost + this to spit, so tiny cells cannot drain themselves")]
        [SerializeField] private float minMassAfterSpit = 10f;
        [Tooltip("Seconds between two pellets while the spit button is held. Too short and the pellets pile up into one blob")]
        [SerializeField] private float spitCooldown = 0.26f;
        [Tooltip("A failed spit (no cell big enough) waits this long before trying again, so it is not re-checked every frame")]
        [SerializeField] private float spitRetryInterval = 0.25f;
        [SerializeField] private float spitLaunchSpeed = 12f;
        [SerializeField] private float spitSpawnGap = 0.15f;
        [SerializeField] private float spitArmDelay = 0.12f;
        [Tooltip("Your own pellet ignores you this long after leaving the body. After that you can eat your own mass back, like Agar")]
        [SerializeField] private float spitOwnerGrace = 0.6f;

        [Header("Merge")]
        [Tooltip("Seconds before split cells may touch again. The wait also grows with the cell mass.")]
        [SerializeField] private float mergeCooldown = 4.5f;
        [SerializeField] private float mergeCooldownPerMass = 0.002f;
        [SerializeField] private float mergeCooldownMax = 8f;
        [Tooltip("Own cells behave like solid bodies: two cells that are not allowed to merge yet push each other apart at this speed (units per second). Set to 0 to let them overlap again.")]
        [SerializeField] private float pushApartSpeed = 40f;
        [SerializeField] private float mergeContactTolerance = 0.15f;
        [Tooltip("How fast a merging cell crawls into the keeper once its cooldown is over. Slow on purpose.")]
        [SerializeField] private float mergeCrawlSpeed = 0.7f;
        [Tooltip("Smallest fraction of the keeper radius that counts as fully covered (a floor for equal-sized cells)")]
        [SerializeField, Range(0.05f, 1f)] private float mergeEatDepth = 0.35f;
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

        // where split / spit actually fly. The mouse when there is one (Agar.io), else the
        // direction the stick points at.
        private Vector2 aim = Vector2.right;

        // How far the stick is pushed in, 0 = at the rim (perpendicular spit), 1 = at the
        // centre (centre spit). Smoothed so dragging the stick in changes the stance gradually
        // instead of flipping between two directions.
        private float centerAimAmount;

        // merge rules (cooldown, pull animation, mass hand-over) live in PlayerMerge
        private readonly PlayerMerge merge = new PlayerMerge();

        // one triangle per ball, so you can see where the whole group is heading
        private readonly System.Collections.Generic.Dictionary<SizeController, PlayerMarker> markers =
            new System.Collections.Generic.Dictionary<SizeController, PlayerMarker>();

        [System.Serializable]
        public class PlayerTuning
        {
            public float moveSpeed;
            public float maxSpeedHardLimit;
            public float acceleration;
            public float deceleration;
        public float reverseRate;
            public float inputDeadZone;

            public float splitMinMass;
            public float splitMassRatio;
            public int maxBallCount;
            public float splitCooldown;
            public float splitDistanceMultiplier;
            public float splitImpulseFactor;
            public float splitCoastDuration;
            public float splitEjectDuration;

            public float spitMassCost;
            public float spitFoodMass;
            public float spitReturnRatio;
            public float minMassAfterSpit;
            public float spitCooldown;
            public float spitRetryInterval;
            public float spitLaunchSpeed;
            public float spitSpawnGap;
            public float spitArmDelay;
            public float spitOwnerGrace;

            public float mergeCooldown;
            public float mergeCooldownPerMass;
            public float mergeCooldownMax;
            public float pushApartSpeed;
            public float mergeContactTolerance;
            public float mergeCrawlSpeed;
        public float mergeEatDepth;
            public float mergePullEase;
            public float slideAround;
            public float leashSlack;
        }

        public PlayerTuning CaptureTuning()
        {
            return new PlayerTuning
            {
                moveSpeed = moveSpeed,
                maxSpeedHardLimit = maxSpeedHardLimit,
                acceleration = acceleration,
                deceleration = deceleration,
                reverseRate = reverseRate,
                inputDeadZone = inputDeadZone,

                splitMinMass = splitMinMass,
                splitMassRatio = splitMassRatio,
                maxBallCount = maxBallCount,
                splitCooldown = splitCooldown,
                splitDistanceMultiplier = splitDistanceMultiplier,
                splitImpulseFactor = splitImpulseFactor,
                splitCoastDuration = splitCoastDuration,
                splitEjectDuration = splitEjectDuration,

                spitMassCost = spitMassCost,
                spitFoodMass = spitFoodMass,
                spitReturnRatio = spitReturnRatio,
                minMassAfterSpit = minMassAfterSpit,
                spitCooldown = spitCooldown,
                spitRetryInterval = spitRetryInterval,
                spitLaunchSpeed = spitLaunchSpeed,
                spitSpawnGap = spitSpawnGap,
                spitArmDelay = spitArmDelay,
                spitOwnerGrace = spitOwnerGrace,

                mergeCooldown = mergeCooldown,
                mergeCooldownPerMass = mergeCooldownPerMass,
                mergeCooldownMax = mergeCooldownMax,
                pushApartSpeed = pushApartSpeed,
                mergeContactTolerance = mergeContactTolerance,
                mergeCrawlSpeed = mergeCrawlSpeed,
                mergeEatDepth = mergeEatDepth,
                mergePullEase = mergePullEase,
                slideAround = slideAround,
                leashSlack = leashSlack,
            };
        }

        public void ApplyTuning(PlayerTuning tuning)
        {
            if (tuning == null)
            {
                return;
            }

            moveSpeed = tuning.moveSpeed;
            maxSpeedHardLimit = tuning.maxSpeedHardLimit;
            acceleration = tuning.acceleration;
            deceleration = tuning.deceleration;
            reverseRate = tuning.reverseRate;
            inputDeadZone = tuning.inputDeadZone;

            splitMinMass = tuning.splitMinMass;
            splitMassRatio = tuning.splitMassRatio;
            maxBallCount = tuning.maxBallCount;
            splitCooldown = tuning.splitCooldown;
            splitDistanceMultiplier = tuning.splitDistanceMultiplier;
            splitImpulseFactor = tuning.splitImpulseFactor;
            splitCoastDuration = tuning.splitCoastDuration;
            splitEjectDuration = tuning.splitEjectDuration;

            spitMassCost = tuning.spitMassCost;
            spitFoodMass = tuning.spitFoodMass;
            spitReturnRatio = tuning.spitReturnRatio;
            minMassAfterSpit = tuning.minMassAfterSpit;
            spitCooldown = tuning.spitCooldown;
            spitRetryInterval = tuning.spitRetryInterval;
            spitLaunchSpeed = tuning.spitLaunchSpeed;
            spitSpawnGap = tuning.spitSpawnGap;
            spitArmDelay = tuning.spitArmDelay;
            spitOwnerGrace = tuning.spitOwnerGrace;

            mergeCooldown = tuning.mergeCooldown;
            mergeCooldownPerMass = tuning.mergeCooldownPerMass;
            mergeCooldownMax = tuning.mergeCooldownMax;
            pushApartSpeed = tuning.pushApartSpeed;
            mergeContactTolerance = tuning.mergeContactTolerance;
            mergeCrawlSpeed = tuning.mergeCrawlSpeed;
            mergeEatDepth = tuning.mergeEatDepth;
            mergePullEase = tuning.mergePullEase;
            slideAround = tuning.slideAround;
            leashSlack = tuning.leashSlack;

            SyncTools();
        }


        // tuning snapshot used when respawning (owned by GameManager)
        private static PlayerTuning s_PendingTuning;

        private void Awake()
        {
            Active = this;

            // ApplyTuning already pushes the numbers down to the cells
            if (s_PendingTuning != null)
            {
                PlayerTuning tuning = s_PendingTuning;
                s_PendingTuning = null;
                ApplyTuning(tuning);
                return;
            }

            SyncTools();
        }

        // every ball carries its own arrow, created the first frame it shows up
        private void SyncMarkers()
        {
            for (int i = 0; i < roster.Count; i++)
            {
                SizeController ball = roster.Balls[i];
                if (ball == null || ball.IsDead || markers.ContainsKey(ball))
                {
                    continue;
                }

                var go = new GameObject("PlayerMarker");
                go.transform.SetParent(transform, true);
                markers.Add(ball, go.AddComponent<PlayerMarker>());
            }

            // drop arrows whose ball is gone
            if (markers.Count == 0)
            {
                return;
            }

            s_stale.Clear();
            foreach (var pair in markers)
            {
                if (pair.Key == null || pair.Key.IsDead || !roster.Contains(pair.Key))
                {
                    s_stale.Add(pair.Key);
                }
            }

            for (int i = 0; i < s_stale.Count; i++)
            {
                PlayerMarker dead;
                if (markers.TryGetValue(s_stale[i], out dead) && dead != null)
                {
                    Destroy(dead.gameObject);
                }
                markers.Remove(s_stale[i]);
            }

            s_stale.Clear();
        }

        private static readonly System.Collections.Generic.List<SizeController> s_stale =
            new System.Collections.Generic.List<SizeController>();

        private void LateUpdate()
        {
            SyncMarkers();

            // each arrow shows where THAT cell will actually spit / split, so pushing the stick
            // in visibly turns the arrows from sideways into crossed
            Vector2 centroid = GroupCentroid();
            int count = roster.Count;

            foreach (var pair in markers)
            {
                if (pair.Value == null || pair.Key == null)
                {
                    continue;
                }

                Vector2 cellDir = PlayerActions.CellDirection(
                    aim, centroid, pair.Key.transform.position, count, centerAimAmount);

                pair.Value.Follow(pair.Key, cellDir, markerGap);
            }
        }

        private Vector2 GroupCentroid()
        {
            Vector2 sum = Vector2.zero;
            int n = 0;

            for (int i = 0; i < roster.Count; i++)
            {
                SizeController ball = roster.Balls[i];
                if (ball == null || ball.IsDead)
                {
                    continue;
                }

                sum += (Vector2)ball.transform.position;
                n++;
            }

            return n > 0 ? sum / n : (Vector2)transform.position;
        }

        //give Inspector Parameters to PlayerActions??PlayerMerge
        private void SyncTools()
        {
            PlayerTuning snapshot = CaptureTuning();
            actions.Configure(roster, SpawnBall, snapshot, name);
            merge.Configure(roster, snapshot);
            PushMotionTuning();
        }

        // the motion values live on the cells that already exist, so a tuning change has to be
        // pushed down once instead of every frame
        private void PushMotionTuning()
        {
            for (int i = 0; i < roster.Count; i++)
            {
                SizeController ball = roster.Balls[i];
                if (ball == null)
                {
                    continue;
                }

                PlayerCellMotion motion = ball.GetComponent<PlayerCellMotion>();
                if (motion == null)
                {
                    continue;
                }

                motion.ApplyMotionTuning(
                    acceleration, deceleration, maxSpeedHardLimit,
                    subFollowAcceleration, subHomeStrength, subHomeDamping, subMaxDriftSpeed,
                    reverseRate, slideAround, leashSlack, mergeCrawlSpeed, mergeEatDepth);
            }
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

            // Space is the split key, and a selected UI button would also fire on Space.
            // Turning navigation off keeps the on screen buttons finger/click only.
            Selectable[] selectables = FindObjectsByType<Selectable>(FindObjectsSortMode.None);
            for (int i = 0; i < selectables.Length; i++)
            {
                Navigation navigation = selectables[i].navigation;
                navigation.mode = Navigation.Mode.None;
                selectables[i].navigation = navigation;
            }

            if (EventSystem.current != null)
            {
                EventSystem.current.sendNavigationEvents = false;
                EventSystem.current.SetSelectedGameObject(null);
            }
        }

        private void OnEnable()
        {
            playerInput.Enable();
        }

        private void OnDisable()
        {
            playerInput.Disable();
            moveInput = Vector2.zero;
        }

        private void Update()
        {
            moveInput = playerInput.ReadMove();

            // Pushing the stick from the rim toward the centre is what turns the spit from
            // perpendicular into crossed and finally into a centre spit.
            float wantedCenter = Mathf.Clamp01(1f - moveInput.magnitude);
            centerAimAmount = Mathf.MoveTowards(centerAimAmount, wantedCenter, Time.deltaTime * centerAimSpeed);

            // mouse beats the stick; keyboard-only play keeps using the movement direction
            Vector2 mouseAim;
            if (TryReadMouseAim(out mouseAim))
            {
                aim = mouseAim;
            }
            else if (facing.sqrMagnitude > 0.0001f)
            {
                aim = facing;
            }

            // one edge per press for split, and repeat while the spit button stays down,
            // so tap and hold both work from the same place
            if (playerInput.SplitPressedThisFrame())
            {
                TrySplit();
            }

            if (playerInput.SpitHeld)
            {
                TrySpit();
            }
        }

        private bool TryReadMouseAim(out Vector2 direction)
        {
            direction = Vector2.zero;

            if (!aimAtMouse)
            {
                return false;
            }

            Mouse mouse = Mouse.current;
            Camera cam = Camera.main;
            if (mouse == null || cam == null)
            {
                return false;
            }

            SizeController reference = roster.Main;
            if (reference == null)
            {
                return false;
            }

            Vector3 screen = mouse.position.ReadValue();
            screen.z = Mathf.Abs(cam.transform.position.z);
            Vector3 world = cam.ScreenToWorldPoint(screen);

            Vector2 delta = (Vector2)world - (Vector2)reference.transform.position;
            if (delta.sqrMagnitude < 0.0001f)
            {
                return false;
            }

            direction = delta.normalized;
            return true;
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

            // 4. every cell gets exactly one motion owner, and learns whether it is the main one
            SyncMotion();

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

            // 6. merge: pull the running swallow first, then look for a new pair
            merge.Tick(Time.fixedDeltaTime);
        }

        // Every player cell gets exactly one motion owner. PlayerController no longer writes a
        // Rigidbody itself: it only says which cell is the main one. The motion component
        // decides how to move, and the merge pull is allowed to keep a cell busy.
        private void SyncMotion()
        {
            SizeController main = roster.Main;

            for (int i = 0; i < roster.Count; i++)
            {
                SizeController ball = roster.Balls[i];
                if (ball == null || ball.IsDead)
                {
                    continue;
                }

                PlayerCellMotion motion = ball.GetComponent<PlayerCellMotion>();

                if (motion == null)
                {
                    motion = ball.gameObject.AddComponent<PlayerCellMotion>();
                    motion.Initialize(this);
                    motion.ApplyMotionTuning(
                        acceleration, deceleration, maxSpeedHardLimit,
                        subFollowAcceleration, subHomeStrength, subHomeDamping, subMaxDriftSpeed,
                        reverseRate, slideAround, leashSlack, mergeCrawlSpeed, mergeEatDepth);
                }

                // the merge pull owns this cell right now
                if (merge.IsMerging(ball))
                {
                    continue;
                }

                motion.SetFollowing(ball == main);
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

            // Player cells carry no drag: the input alone decides the speed, so letting go of the
            // stick keeps the ball gliding until it is pushed the other way. Balls placed by hand
            // in the scene still carry the Rigidbody2D drag they were saved with, so zero it here.
            Rigidbody2D body = ball.GetComponent<Rigidbody2D>();
            if (body != null)
            {
                body.linearDamping = 0f;
            }

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
            List<SizeController> touched = actions.TrySplit(aim, centerAimAmount);
            if (touched == null)
            {
                return false;
            }

            // every ball that took part waits out the merge cooldown, otherwise the two
            // halves would merge straight back the moment they touch
            for (int i = 0; i < touched.Count; i++)
            {
                merge.MarkSplit(touched[i]);
            }

            if (AudioManager.Instance != null)
            {
                AudioManager.Instance.PlaySplit();
            }

            // The camera starts pulling out on this frame, not after the pieces separate.
            if (SplitPerformed != null)
            {
                SplitPerformed();
            }
            return true;
        }

        public bool TrySpit()
        {
            int ejected = actions.TrySpit(aim, centerAimAmount);

            if (ejected == 0)
            {
                return false;
            }

            if (AudioManager.Instance != null)
            {
                AudioManager.Instance.PlaySpit();
            }
            return true;
        }

        // called by a virus burst: the popped cell and every piece it created share the
        // same merge wait, so the burst can not instantly re-merge into one ball
        public void MarkBurst(SizeController source, List<SizeController> created)
        {
            if (source != null)
            {
                merge.MarkSplit(source);
            }

            if (created != null)
            {
                for (int i = 0; i < created.Count; i++)
                {
                    merge.MarkSplit(created[i]);
                }
            }

            if (AudioManager.Instance != null)
            {
                AudioManager.Instance.PlaySplit();
            }
        }

        public int MaxBallCount
        {
            get { return maxBallCount; }
        }

        /// <summary>
        /// Fired the moment a split is accepted. The camera listens to this so it can start
        /// zooming out on the same frame instead of waiting for the pieces to separate.
        /// </summary>
        public event System.Action SplitPerformed;

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

            // a fresh cell gets its motion owner right away: the split animation starts in the
            // same frame the ball is born, before the next FixedUpdate could hand it one
            PlayerCellMotion motion = go.AddComponent<PlayerCellMotion>();
            motion.Initialize(this);
            motion.ApplyMotionTuning(
                acceleration, deceleration, maxSpeedHardLimit,
                subFollowAcceleration, subHomeStrength, subHomeDamping, subMaxDriftSpeed,
                reverseRate, slideAround, leashSlack, mergeCrawlSpeed, mergeEatDepth);

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

        // Nearest cell this one is allowed to melt into. Two sub cells join each other, not only
        // into the main ball, so the merge target is picked by distance among every ready cell.
        public bool TryFindMergePartner(SizeController ball, out SizeController partner)
        {
            partner = null;

            if (ball == null || roster == null)
            {
                return false;
            }

            float bestDistance = float.MaxValue;

            for (int i = 0; i < roster.Balls.Count; i++)
            {
                SizeController other = roster.Balls[i];

                if (other == null || other == ball || other.IsDead)
                {
                    continue;
                }

                if (!merge.CanMerge(other))
                {
                    continue;
                }

                float distance = Vector2.Distance(ball.transform.position, other.transform.position);
                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    partner = other;
                }
            }

            return partner != null;
        }


        public void TriggerSplit()
        {
            TrySplit();
        }

        public void TriggerSpit()
        {
            TrySpit();
        }

        // what the player is asking for, after dead zone and clamping
        public Vector2 MoveInput
        {
            get
            {
                Vector2 input = Vector2.ClampMagnitude(moveInput, 1f);
                return input.magnitude < inputDeadZone ? Vector2.zero : input;
            }
        }

        public float MoveSpeed
        {
            get { return moveSpeed; }
        }

        // The whole group moves at the main cell's speed: a sub cell must never slowly outrun
        // the big one, which is what happened while subs used their own factor.
        public Vector2 GroupVelocity
        {
            get
            {
                SizeController main = roster.Main;
                float factor = main != null ? main.MoveSpeedFactor : 1f;
                return MoveInput * (moveSpeed * factor * GamePacing.PlayerMove);
            }
        }

        public SizeController MainBall { get { return roster.Main; } }

        public int BallCount { get { return roster.Count; } }

        public IReadOnlyList<SizeController> Balls { get { return roster.Balls; } }

        public float TotalMass { get { return roster.TotalMass; } }
    }
}
