using UnityEngine;

namespace QiuQiu
{
    public class GameManager : MonoBehaviour
    {
        public enum RoundState
        {
            Playing,
            Finished
        }

        public static GameManager Instance { get; private set; }

        [Header("Object")]
        [SerializeField] private PlayerController player;
        [SerializeField] private RoundResultPanel resultPanel;

        [Header("Timer")]
        [SerializeField] private float roundDuration = 180f;
        [SerializeField] private float warnTime = 30f;

        [Header("playerRespawn")]
        [SerializeField] private float playerRespawnDelay = 5f;
        [SerializeField] private float playerRespawnMass = 40f;
        [SerializeField] private float respawnInvulnerability = 2.5f;
        [SerializeField] private Sprite playerSprite;
        [SerializeField] private Color playerColor = new Color(0.35f, 0.75f, 1f);
        [SerializeField] private float respawnSafeRadius = 10f;
        [SerializeField] private float respawnThreatMassRatio = 1.5f;
        [SerializeField] private int respawnPlacementAttempts = 12;

        [Header("AI Respawn")]
        [SerializeField] private int aiMaxCount = 10;
        [SerializeField] private float aiRespawnDelay = 1.5f;
        [SerializeField] private float aiMinMass = 4f;
        [SerializeField] private float aiMaxMass = 60f;
        [SerializeField] private float aiSpawnInvulnerability = 2f;
        [SerializeField] private Team aiTeam = Team.Enemy;

        [SerializeField] private Color[] aiColors =
        {
            new Color(1f, 0.55f, 0.5f),
            new Color(1f, 0.75f, 0.35f),
            new Color(0.65f, 0.92f, 0.55f),
            new Color(0.72f, 0.62f, 1f),
            new Color(0.55f, 0.9f, 1f),
        };

        [Header("Result")]
        [SerializeField] private bool freezeOnGameOver = true;
        [SerializeField] private bool muteAudioOnGameOver;

        [Header("Tuning: eating")]
        [Tooltip("How much of the smaller ball must be inside the bigger one before it is eaten. 0.97 = almost fully covered.")]
        [SerializeField, Range(0.5f, 1f)] private float swallowCoverage = 0.97f;

        [Header("Tuning: split")]
        [Tooltip("How far the new cell is placed, as a multiple of (parent radius + new radius).")]
        [SerializeField, Range(1f, 3f)] private float splitDistanceMultiplier = 1.55f;
        [Tooltip("Launch speed of a split, as a multiple of the cell's normal move speed.")]
        [SerializeField, Range(0f, 10f)] private float splitImpulseFactor = 4.5f;
        [Tooltip("How long the launched cell keeps coasting before the follow spring takes over.")]
        [SerializeField] private float splitCoastDuration = 0.75f;
        [Tooltip("How long the new cell takes to slide out of the parent.")]
        [SerializeField] private float splitEjectDuration = 0.18f;

        [Header("Tuning: merge")]
        [Tooltip("How fast a merging cell crawls into the keeper. Slow on purpose.")]
        [SerializeField] private float mergeCrawlSpeed = 1.6f;
        [Tooltip("Smallest fraction of the keeper radius that counts as fully covered (a floor for equal-sized cells)")]
        [SerializeField, Range(0.05f, 1f)] private float mergeEatDepth = 0.35f;
        [Tooltip("How much of a body overlap is turned sideways so a small cell slides around the big one")]
        [SerializeField, Range(0f, 1f)] private float cellSlideAround = 0.55f;
        [Tooltip("How many body-widths a cell may drift away before the leash pulls it back. 1 = always touching")]
        [SerializeField] private float cellLeashSlack = 1.6f;
        [Tooltip("Separation speed for own cells that may not merge yet. Must beat the group speed or they wedge together")]
        [SerializeField] private float cellPushApartSpeed = 40f;
        [SerializeField] private float mergeCooldown = 4.5f;
        [Tooltip("Extra merge cooldown per point of mass.")]
        [SerializeField] private float mergeCooldownPerMass = 0.002f;
        [SerializeField] private float mergeCooldownMax = 8f;

        [Header("Tuning: spit")]
        [SerializeField] private float spitCooldown = 0.26f;
        [Tooltip("Mass taken from the cell per spore.")]
        [SerializeField] private float spitMassCost = 8f;
        [Tooltip("Mass of the spore that actually lands on the ground.")]
        [SerializeField] private float spitFoodMass = 6f;
        [Tooltip("A pellet carries at most this fraction of the mass paid for it, so spitting can never print free weight")]
        [SerializeField, Range(0.1f, 1f)] private float spitReturnRatio = 0.8f;
        [SerializeField] private float spitLaunchSpeed = 12f;
        [SerializeField] private float minMassAfterSpit = 10f;
        [Tooltip("Seconds during which the shooter cannot swallow its own spore.")]
        [SerializeField] private float spitOwnerGrace = 0.6f;

        [Header("Tuning: movement")]
        [SerializeField] private float playerMoveSpeed = 4.2f;
        [SerializeField] private float playerMaxSpeed = 10.5f;
        [SerializeField] private float playerAcceleration = 38f;
        [SerializeField] private float playerDeceleration = 5f;

        [Tooltip("How fast a ball answers when the stick is pushed the opposite way. Lower = heavier.")]
        [SerializeField] private float playerReverseRate = 8f;

        public RoundState State { get; private set; }

        public bool IsPlayerRespawning { get; private set; }

        public float RespawnTimeLeft { get; private set; }

        public float PeakMass { get; private set; }
        public float FinalMass { get; private set; }
        public int FinalRank { get; private set; } = 1;
        public int TotalParticipants { get; private set; } = 1;

        public int ParticipantCount { get; private set; } = 1;

        public bool IsRunning { get { return State == RoundState.Playing; } }
        public float WarnTime { get { return warnTime; } }

        private readonly RoundTimer timer = new RoundTimer();

        public float RemainingTime { get { return timer.Remaining; } }

        private float respawnDeadline = -1f;
        private float nextAiSpawnTime;
        private Vector3 lastDeathPosition;
        private int aiSpawnCounter;
        private bool loggedMissingSprite;

        private void Awake()
        {
            Instance = this;

            Time.timeScale = 1f;

            UiJuiceBootstrap.JuiceScene();

            if (player == null)
            {
                player = FindFirstObjectByType<PlayerController>();
            }

            if (resultPanel == null)
            {
                resultPanel = FindFirstObjectByType<RoundResultPanel>(FindObjectsInactive.Include);
            }
        }

        private void OnDestroy()
        {
            if (Instance == this)
            {
                Instance = null;
            }
        }

        private void Start()
        {
            State = RoundState.Playing;
            ParticipantCount = 1 + Mathf.Max(aiMaxCount, AliveAiCount);
            TotalParticipants = ParticipantCount;
            timer.Start(roundDuration);
            nextAiSpawnTime = Time.time + aiRespawnDelay;

            if (player == null)
            {
                player = PlayerController.Active;
            }
            if (player == null)
            {
                return;
            }

            PeakMass = player.TotalMass;
            FinalMass = PeakMass;

            ResolveBallSprite();
            ApplyTuning();
        }

        [ContextMenu("Apply Tuning Now")]
        public void ApplyTuning()
        {
            Eater.DefaultSwallowCoverage = swallowCoverage;
            foreach (Eater eater in FindObjectsByType<Eater>(FindObjectsSortMode.None))
            {
                eater.SwallowCoverage = swallowCoverage;
            }

            if (player == null)
            {
                player = PlayerController.Active;
            }

            if (player == null)
            {
                return;
            }

            PlayerController.PlayerTuning tuning = player.CaptureTuning();
            tuning.moveSpeed = playerMoveSpeed;
            tuning.maxSpeedHardLimit = playerMaxSpeed;
            tuning.acceleration = playerAcceleration;
            tuning.deceleration = playerDeceleration;

            tuning.splitDistanceMultiplier = splitDistanceMultiplier;
            tuning.splitImpulseFactor = splitImpulseFactor;
            tuning.splitCoastDuration = splitCoastDuration;
            tuning.splitEjectDuration = splitEjectDuration;

            tuning.mergeCrawlSpeed = mergeCrawlSpeed;
            tuning.mergeEatDepth = mergeEatDepth;
            tuning.slideAround = cellSlideAround;
            tuning.leashSlack = cellLeashSlack;
            tuning.pushApartSpeed = cellPushApartSpeed;
            tuning.reverseRate = playerReverseRate;
            tuning.mergeCooldown = mergeCooldown;
            tuning.mergeCooldownPerMass = mergeCooldownPerMass;
            tuning.mergeCooldownMax = mergeCooldownMax;

            tuning.spitCooldown = spitCooldown;
            tuning.spitMassCost = spitMassCost;
            tuning.spitFoodMass = spitFoodMass;
        tuning.spitReturnRatio = spitReturnRatio;
            tuning.spitLaunchSpeed = spitLaunchSpeed;
            tuning.minMassAfterSpit = minMassAfterSpit;
            tuning.spitOwnerGrace = spitOwnerGrace;

            player.ApplyTuning(tuning);
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            if (Application.isPlaying)
            {
                ApplyTuning();
            }
        }
#endif

        private void Update()
        {
            if (State == RoundState.Finished)
            {
                return;
            }

            timer.Tick(Time.deltaTime);
            TickPlayerState();
            TickRespawn();
            TickAiRespawn();
            UpdateAiMassCap();

            if (timer.IsFinished)
            {
                EndRound();
            }
        }

        private void UpdateAiMassCap()
        {
            if (player == null || !player.isActiveAndEnabled)
            {
                SizeController.AiMassCap = float.MaxValue;
                return;
            }

            SizeController.AiMassCap = Mathf.Max(0f, player.TotalMass) * GameSettings.AiMassCapFactor;
        }

        private void TickPlayerState()
        {
            if (player == null || !player.isActiveAndEnabled)
            {
                player = PlayerController.Active;
            }

            if (player == null)
            {
                if (!IsPlayerRespawning)
                {
                    BeginPlayerRespawn(null);
                }
                return;
            }

            if (IsPlayerRespawning)
            {
                return;
            }

            float mass = player.TotalMass;

            if (mass <= 0f)
            {
                return;
            }

            FinalMass = mass;

            if (mass > PeakMass)
            {
                PeakMass = mass;
            }
        }

        private void TickRespawn()
        {
            if (!IsPlayerRespawning)
            {
                return;
            }

            RespawnTimeLeft = Mathf.Max(0f, respawnDeadline - Time.time);
            if (RespawnTimeLeft <= 0f)
            {
                RespawnPlayer();
            }
        }

        public void NotifyPlayerDefeated(SizeController killer)
        {
            if (State == RoundState.Finished)
            {
                return;
            }

            if (IsPlayerRespawning) return;

            BeginPlayerRespawn(killer);
        }

        private void BeginPlayerRespawn(SizeController killer)
        {
            IsPlayerRespawning = true;

            if (player != null)
            {
                float mass = player.TotalMass;
                if (mass > 0f) FinalMass = mass;
                if (player.MainBall != null)
                {
                    lastDeathPosition = player.MainBall.transform.position;
                }
            }

            respawnDeadline = Time.time + playerRespawnDelay;
            RespawnTimeLeft = playerRespawnDelay;
        }

        public void RespawnPlayer()
        {
            if (State == RoundState.Finished)
            {
                return;
            }

            Sprite sprite = ResolveBallSprite();

            if (sprite == null)
            {
                if (!loggedMissingSprite)
                {
                    loggedMissingSprite = true;
                    Debug.LogError("[GameManager] no ball sprite in the scene, player respawn will retry every second");
                }

                IsPlayerRespawning = true;
                RespawnTimeLeft = 1f;
                respawnDeadline = Time.time + 1f;
                return;
            }

            IsPlayerRespawning = false;
            RespawnTimeLeft = 0f;
            respawnDeadline = -1f;

            PlayerController controller = PlayerController.Active;

            if (controller == null)
            {
                controller = PlayerController.EnsureExists();
            }

            Vector3 spawn = FindSafeSpawnPoint(playerRespawnMass, lastDeathPosition);

            SizeController ball = controller.SpawnBall("Player", spawn, playerRespawnMass, sprite, playerColor);

            player = controller;

            if (ball == null)
            {
                return;
            }

            ball.SetInvulnerable(respawnInvulnerability);
        }

        private Sprite ResolveBallSprite()
        {
            if (playerSprite != null)
            {
                return playerSprite;
            }

            if (player != null && player.MainBall != null)
            {
                var sr = player.MainBall.GetComponent<SpriteRenderer>();
                if (sr != null && sr.sprite != null)
                {
                    return playerSprite = sr.sprite;
                }
            }

            var all = SizeController.Registry;
            for (int i = 0; i < all.Count; i++)
            {
                if (all[i] == null)
                {
                    continue;
                }

                var sr = all[i].GetComponent<SpriteRenderer>();
                if (sr != null && sr.sprite != null)
                {
                    return playerSprite = sr.sprite;
                }
            }

            return null;
        }

        private Vector3 FindSafeSpawnPoint(float mass, Vector3 fallback)
        {
            float threatMass = mass * Mathf.Max(1f, respawnThreatMassRatio);
            float safeRadius = Mathf.Max(0.5f, respawnSafeRadius);
            var all = SizeController.Registry;

            for (int attempt = 0; attempt < Mathf.Max(1, respawnPlacementAttempts); attempt++)
            {
                Vector3 candidate = fallback;
                if (ArenaManager.Instance != null)
                {
                    candidate = ArenaManager.Instance.GetRandomPointInside(2f);
                }

                if (!IsBlocked(candidate, threatMass, safeRadius, all)) return candidate;
            }

            return fallback;
        }

        private static bool IsBlocked(Vector3 point, float threatMass, float safeRadius,
            System.Collections.Generic.IReadOnlyList<SizeController> all)
        {
            for (int i = 0; i < all.Count; i++)
            {
                var ball = all[i];
                if (ball == null || ball.IsDead)
                {
                    continue;
                }
                if (ball.Mass < threatMass)
                {
                    continue;
                }

                if (Vector2.Distance(ball.transform.position, point) < safeRadius + ball.Radius)
                {
                    return true;
                }
            }

            return false;
        }

        public int AliveAiCount
        {
            get { return ScoreBoard.AliveBots(aiTeam); }
        }

        private void TickAiRespawn()
        {

            if (Time.time < nextAiSpawnTime)
            {
                return;

            }
            if (AliveAiCount >= aiMaxCount)
            {
                return;
            }

            nextAiSpawnTime = Time.time + aiRespawnDelay;

            SpawnAi();
        }

        private SizeController SpawnAi()
        {
            Sprite sprite = ResolveBallSprite();

            if (sprite == null)
            {
                return null;
            }

            float mass = UnityEngine.Random.Range(aiMinMass, aiMaxMass);
            Color color = aiColors != null && aiColors.Length > 0
                ? aiColors[aiSpawnCounter % aiColors.Length]
                : Color.red;

            Vector3 position = ArenaManager.Instance != null
                ? ArenaManager.Instance.GetRandomPointInside(1.5f)
                : Vector3.zero;

            position = FindSafeSpawnPoint(mass, position);

            string aiName = $"AI_{aiSpawnCounter:00}";
            int botId = aiSpawnCounter;
            aiSpawnCounter++;

            var go = BallFactory.CreateBall(aiName, position, mass, sprite, color, aiTeam);

            var ai = go.AddComponent<AIController>();

            ai.ApplyRandomProfile(UnityEngine.Random.Range(0.9f, 1.25f), UnityEngine.Random.Range(0.85f, 1.15f));

            var size = go.GetComponent<SizeController>();
            size.BotId = botId;
            size.SetInvulnerable(aiSpawnInvulnerability);

            return size;
        }

        public void EndRound()
        {
            if (State == RoundState.Finished)
            {
                return;
            }

            State = RoundState.Finished;
            IsPlayerRespawning = false;
            RespawnTimeLeft = 0f;
            timer.Start(0f);

            if (player == null || !player.isActiveAndEnabled)
            {
                player = PlayerController.Active;
            }

            if (player != null)
            {
                float mass = player.TotalMass;
                if (mass > 0f) FinalMass = mass;
            }

            if (FinalMass <= 0f)
            {
                FinalMass = PeakMass;
            }
            if (FinalMass > PeakMass)
            {
                PeakMass = FinalMass;
            }

            ComputeRank();

            if (freezeOnGameOver)
            {
                FreezeWorld();
            }

            if (muteAudioOnGameOver)
            {
                AudioListener.pause = true;
            }

            Debug.Log($"[round] time up | peak {PeakMass:F0} | final {FinalMass:F0} | rank {FinalRank}/{TotalParticipants}");

            if (resultPanel != null)
            {
                resultPanel.Show(this);
            }
        }

        private void FreezeWorld()
        {
            Time.timeScale = 0f;
        }

        private void ComputeRank()
        {
            int rank;
            ScoreBoard.Rank(FinalMass, ParticipantCount, out rank);

            FinalRank = rank;
            TotalParticipants = ParticipantCount;
        }
    }
}
