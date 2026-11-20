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
        [SerializeField] private GameOverPanel gameOverPanel;

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

        public RoundState State { get; private set; }

        // Play Respawn
        public bool IsPlayerRespawning { get; private set; }

        public float RespawnTimeLeft { get; private set; }

        // Result
        public float PeakMass { get; private set; }
        public float FinalMass { get; private set; }
        public int FinalRank { get; private set; } = 1;
        public int TotalParticipants { get; private set; } = 1;

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

            if (player == null)
            {
                player = FindFirstObjectByType<PlayerController>();
            }

            if (gameOverPanel == null)
            {   //Exclude/Include    > check only active/Both(+ not active) GameObject
                gameOverPanel = FindFirstObjectByType<GameOverPanel>(FindObjectsInactive.Include);
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
        }

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

        // make AI snowballing Speed be down
        private void UpdateAiMassCap()
        {
            if (player == null || !player.isActiveAndEnabled)
            {
                SizeController.AiMassCap = float.MaxValue;
                return;
            }

            SizeController.AiMassCap = Mathf.Max(0f, player.TotalMass) * GameSettings.AiMassCapFactor;
        }

        // scaled clock: a pause sets timeScale = 0, so the round clock stops with the world
        private void TickPlayerState()
        {
            // for main ball die
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
                {   //for main ball fit other it self
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
                // nothing to draw a ball with: retry instead of leaving the player dead forever
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
                controller = PlayerController.EnsureExists(); //sure play live
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

        // the sprite can live on the serialized field, on a live ball, or nowhere at all
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

            //12 time
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
            get { return ScoreBoard.AliveCount(aiTeam); }
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

            string aiName = $"AI_{aiSpawnCounter:00}"; // for check
            aiSpawnCounter++;

            var go = BallFactory.CreateBall(aiName, position, mass, sprite, color, aiTeam);

            var ai = go.AddComponent<AIController>();
            
            ai.ApplyRandomProfile(UnityEngine.Random.Range(0.9f, 1.25f), UnityEngine.Random.Range(0.85f, 1.15f));

            var size = go.GetComponent<SizeController>();
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

            // // for wait respown  TotalMass is die before
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

            Debug.Log($"[结算] 时间到｜最高质量 {PeakMass:F0}｜最终质量 {FinalMass:F0}｜" +
                      $"最终排名 第 {FinalRank} 名 / 共 {TotalParticipants}");  //for check

            if (gameOverPanel != null)
            {
                gameOverPanel.Show(this);
            }
        }

        private void FreezeWorld()
        {
            Time.timeScale = 0f;
        }

        private void ComputeRank()
        {
            int rank;
            int participants;
            ScoreBoard.Rank(FinalMass, out rank, out participants);

            FinalRank = rank;
            TotalParticipants = participants;
        }
    }
}
