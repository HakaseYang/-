using System;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace QiuQiu
{
    // 一局的总控：倒计时、玩家复活、AI 补员、局终结算。
    // 局终用 Time.timeScale = 0 冻结：物理和 FixedUpdate 全停，UI 走 unscaledTime 不受影响，
    // 比给每个 AI / 子球写暂停逻辑可靠。重开一局必须在 Awake 里把 timeScale 恢复成 1。
    [DisallowMultipleComponent]
    public class GameManager : MonoBehaviour
    {
        public enum RoundState
        {
            Playing,
            Finished
        }

        public static GameManager Instance { get; private set; }

        [Header("引用（留空自动在场景里找）")]
        [SerializeField] private PlayerController player;
        [SerializeField] private GameOverPanel gameOverPanel;

        [Header("局内倒计时")]
        [SerializeField] private float roundDuration = 180f;
        [SerializeField] private float warnTime = 30f;

        [Header("玩家复活")]
        [SerializeField] private float playerRespawnDelay = 5f;
        [SerializeField] private float playerRespawnMass = 40f;
        [SerializeField] private float respawnInvulnerability = 2.5f;
        [SerializeField] private Sprite playerSprite;
        [SerializeField] private Color playerColor = new Color(0.35f, 0.75f, 1f);
        [SerializeField] private float respawnSafeRadius = 10f;
        [SerializeField] private float respawnThreatMassRatio = 1.5f;
        [SerializeField] private int respawnPlacementAttempts = 12;

        [Header("AI 自动补员")]
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

        [Header("结算")]
        [SerializeField] private bool freezeOnGameOver = true;
        [SerializeField] private bool muteAudioOnGameOver;

        public RoundState State { get; private set; }

        // 玩家是否在等复活（球全被吃掉，倒计时中）
        public bool IsPlayerRespawning { get; private set; }

        public float RemainingTime { get; private set; }
        public float RespawnTimeLeft { get; private set; }

        // 结算面板要的三项成绩
        public float PeakMass { get; private set; }
        public float FinalMass { get; private set; }
        public int FinalRank { get; private set; } = 1;
        public int TotalParticipants { get; private set; } = 1;

        public bool IsRunning { get { return State == RoundState.Playing; } }
        public float WarnTime { get { return warnTime; } }

        public event Action<GameManager> GameOver;

        private float respawnDeadline = -1f;
        private float nextAiSpawnTime;
        private Vector3 lastDeathPosition;
        private int aiSpawnCounter;

        private void Awake()
        {
            Instance = this;

            // 上一局结算时 timeScale 停在 0，这里不恢复新一局开局就是定格的
            Time.timeScale = 1f;

            if (player == null) player = FindFirstObjectByType<PlayerController>();
            if (gameOverPanel == null)
            {
                gameOverPanel = FindFirstObjectByType<GameOverPanel>(FindObjectsInactive.Include);
            }
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        private void Start()
        {
            State = RoundState.Playing;
            RemainingTime = Mathf.Max(1f, roundDuration);
            nextAiSpawnTime = Time.unscaledTime + aiRespawnDelay;

            if (player == null) player = PlayerController.Active;
            if (player == null) return;

            PeakMass = player.TotalMass;
            FinalMass = PeakMass;

            if (playerSprite == null && player.MainBall != null)
            {
                var sr = player.MainBall.GetComponent<SpriteRenderer>();
                if (sr != null) playerSprite = sr.sprite;
            }
        }

        private void Update()
        {
            if (State == RoundState.Finished) return;

            TickCountdown();
            TickPlayerState();
            TickRespawn();
            TickAiRespawn();

            if (RemainingTime <= 0f) EndRound();
        }

        // 用 unscaledDeltaTime：中途被暂停（弹窗、timeScale 改动）时计时照走
        private void TickCountdown()
        {
            RemainingTime = Mathf.Max(0f, RemainingTime - Time.unscaledDeltaTime);
        }

        private void TickPlayerState()
        {
            // 主球阵亡后旧控制器会被销毁，这里兜底拿当前的
            if (player == null || !player.isActiveAndEnabled) player = PlayerController.Active;

            if (player == null)
            {
                if (!IsPlayerRespawning) BeginPlayerRespawn(null);
                return;
            }

            if (IsPlayerRespawning) return;

            float mass = player.TotalMass;
            if (mass <= 0f) return;

            FinalMass = mass;
            if (mass > PeakMass) PeakMass = mass;
        }

        private void TickRespawn()
        {
            if (!IsPlayerRespawning) return;

            RespawnTimeLeft = Mathf.Max(0f, respawnDeadline - Time.unscaledTime);
            if (RespawnTimeLeft <= 0f) RespawnPlayer();
        }

        // 玩家所有球都没了时由 PlayerController 调用：开倒计时，不结束游戏
        public void NotifyPlayerDefeated(SizeController killer)
        {
            if (State == RoundState.Finished) return;
            if (IsPlayerRespawning) return;   // 多个球同帧死亡只处理一次

            BeginPlayerRespawn(killer);
        }

        private void BeginPlayerRespawn(SizeController killer)
        {
            IsPlayerRespawning = true;

            if (player != null)
            {
                float mass = player.TotalMass;
                if (mass > 0f) FinalMass = mass;
                if (player.MainBall != null) lastDeathPosition = player.MainBall.transform.position;
            }

            respawnDeadline = Time.unscaledTime + Mathf.Max(0.1f, playerRespawnDelay);
            RespawnTimeLeft = playerRespawnDelay;

            Debug.Log($"[复活] 玩家被 {(killer != null ? killer.name : "未知")} 吃掉，{playerRespawnDelay:F1} 秒后复活");
        }

        public void RespawnPlayer()
        {
            if (State == RoundState.Finished) return;

            IsPlayerRespawning = false;
            RespawnTimeLeft = 0f;
            respawnDeadline = -1f;

            if (playerSprite == null)
            {
                Debug.LogError("[复活] 没有 playerSprite，无法生成玩家球。请把 Ball.png 拖到 GameManager.playerSprite。");
                return;
            }

            PlayerController controller = PlayerController.Active;
            if (controller == null) controller = PlayerController.EnsureExists();

            Vector3 spawn = FindSafeSpawnPoint(playerRespawnMass, lastDeathPosition);
            SizeController ball = controller.SpawnBall("Player", spawn, playerRespawnMass, playerSprite, playerColor);

            player = controller;
            if (ball == null) return;

            ball.SetInvulnerable(respawnInvulnerability);
            Debug.Log($"[复活] 玩家在 {spawn} 复活，质量 {playerRespawnMass:F0}，无敌 {respawnInvulnerability:F1} 秒");
        }

        // 竞技区内随机取点，周围有能一口吃掉我的球就换点；全被堵住就退回死亡位置
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
                    candidate = ArenaManager.Instance.GetRandomPointInside(null, 2f);
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
                if (ball == null || ball.IsDead) continue;
                if (ball.Mass < threatMass) continue;   // 吃不了我的不算威胁

                if (Vector2.Distance(ball.transform.position, point) < safeRadius + ball.Radius) return true;
            }

            return false;
        }

        public int AliveAiCount
        {
            get
            {
                int count = 0;
                var all = SizeController.Registry;

                for (int i = 0; i < all.Count; i++)
                {
                    var ball = all[i];
                    if (ball == null || ball.IsDead) continue;
                    if (ball.Team == aiTeam) count++;
                }

                return count;
            }
        }

        private void TickAiRespawn()
        {
            if (aiMaxCount <= 0) return;
            if (Time.unscaledTime < nextAiSpawnTime) return;
            if (AliveAiCount >= aiMaxCount) return;

            nextAiSpawnTime = Time.unscaledTime + Mathf.Max(0.1f, aiRespawnDelay);
            SpawnAi();
        }

        private SizeController SpawnAi()
        {
            if (playerSprite == null) return null;

            float mass = UnityEngine.Random.Range(Mathf.Min(aiMinMass, aiMaxMass), Mathf.Max(aiMinMass, aiMaxMass));
            Color color = aiColors != null && aiColors.Length > 0
                ? aiColors[aiSpawnCounter % aiColors.Length]
                : Color.red;

            Vector3 position = ArenaManager.Instance != null
                ? ArenaManager.Instance.GetRandomPointInside(null, 1.5f)
                : Vector3.zero;

            // 别刷在能一口吃掉它的球脸上（雪球模式下就是那只霸主）
            position = FindSafeSpawnPoint(mass, position);

            string aiName = $"AI_{aiSpawnCounter:00}";
            aiSpawnCounter++;

            var go = BallFactory.CreateBall(aiName, position, mass, playerSprite, color, aiTeam, 10);
            go.tag = "Enemy";

            var ai = go.AddComponent<AIController>();
            // 个体差异：速度和扫描半径各自浮动，避免所有 AI 一个节奏
            ai.ApplyRandomProfile(UnityEngine.Random.Range(0.9f, 1.25f), UnityEngine.Random.Range(0.85f, 1.15f));

            var size = go.GetComponent<SizeController>();
            size.SetInvulnerable(aiSpawnInvulnerability);

            return size;
        }

        public void EndRound()
        {
            if (State == RoundState.Finished) return;

            State = RoundState.Finished;
            IsPlayerRespawning = false;
            RespawnTimeLeft = 0f;
            RemainingTime = 0f;

            if (player == null || !player.isActiveAndEnabled) player = PlayerController.Active;

            // 玩家正好死在复活等待里时 TotalMass 是 0，不能把成绩清零，保留死亡前记下的值
            if (player != null)
            {
                float mass = player.TotalMass;
                if (mass > 0f) FinalMass = mass;
            }

            if (FinalMass <= 0f) FinalMass = PeakMass;
            if (FinalMass > PeakMass) PeakMass = FinalMass;

            ComputeRank();

            if (freezeOnGameOver) FreezeWorld();
            if (muteAudioOnGameOver) AudioListener.pause = true;

            Debug.Log($"[结算] 时间到｜最高质量 {PeakMass:F0}｜最终质量 {FinalMass:F0}｜" +
                      $"最终排名 第 {FinalRank} 名 / 共 {TotalParticipants}");

            GameOver?.Invoke(this);
            if (gameOverPanel != null) gameOverPanel.Show(this);
        }

        // timeScale = 0 已经够冻结了；顺手停 AI、清速度，方便在 Inspector 里看终局状态
        private void FreezeWorld()
        {
            Time.timeScale = 0f;
            var all = SizeController.Registry;

            for (int i = 0; i < all.Count; i++)
            {
                var ball = all[i];
                if (ball == null || ball.IsDead) continue;

                var ai = ball.GetComponent<AIController>();
                if (ai != null && ai.enabled) ai.enabled = false;

                var rb = ball.GetComponent<Rigidbody2D>();
                if (rb != null) rb.linearVelocity = Vector2.zero;
            }
        }

        // 名次 = 1 + 场上质量比我大的对手数，用注册表统计，不做全场景遍历
        private void ComputeRank()
        {
            int aliveOpponents = 0;
            int betterThanMe = 0;
            var all = SizeController.Registry;

            for (int i = 0; i < all.Count; i++)
            {
                var ball = all[i];
                if (ball == null || ball.IsDead) continue;
                if (ball.Team == Team.Player) continue;

                aliveOpponents++;
                if (ball.Mass > FinalMass) betterThanMe++;
            }

            FinalRank = betterThanMe + 1;
            TotalParticipants = aliveOpponents + 1;   // +1 是玩家
        }
    }
}
