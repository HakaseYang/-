using System;
using System.Collections.Generic;
using UnityEngine;

namespace QiuQiu
{
    /// <summary>
    /// 一局游戏的总控：
    ///   ① 局内倒计时（默认 3 分钟）—— 时间到才是真正的“局终”；
    ///   ② 玩家无限复活：所有球被吃光 → 进入 5 秒复活 CD（摄像机停在死亡位置、UI 显示倒计时）
    ///      → CD 结束在安全区域重生一颗基础质量的球，恢复控制；
    ///   ③ AI 自动补员：场上 AI 少于上限时，每隔几秒在随机位置生成一只新的；
    ///   ④ 局终结算：冻结世界（timeScale = 0 + 禁用 AI + 速度清零）、按当前场上所有球的总质量
    ///      算名次、弹出结算面板，并触发 GameOver 事件。
    ///
    /// 为什么局终用 Time.timeScale = 0 而不是逐个关组件？
    ///   关组件要给每个 AI / 子球 / 生成器都写一遍暂停逻辑，容易漏；
    ///   timeScale = 0 是引擎级冻结，FixedUpdate 直接不再调用、物理求解器不步进，
    ///   而 UI（EventSystem / Button / Text）走 Update + unscaledTime，不受影响。
    ///   重开一局时必须在 Awake 里把 timeScale 恢复成 1，否则新场景开局就是定格的。
    /// </summary>
    [DisallowMultipleComponent]
    public class GameManager : MonoBehaviour
    {
        public enum RoundState
        {
            Playing,          // 正常进行（玩家可能活着，也可能在等复活）
            Finished          // 倒计时结束，已冻结并在结算
        }

        public static GameManager Instance { get; private set; }

        [Header("引用（留空会自动在场景里找）")]
        [SerializeField] private PlayerController player;
        [SerializeField] private GameOverPanel gameOverPanel;

        [Header("局内倒计时")]
        [Tooltip("一局总时长（秒）。默认 180 = 3 分钟。")]
        [SerializeField] private float roundDuration = 180f;

        [Tooltip("剩余多少秒时开始变红（UI 侧读取）。")]
        [SerializeField] private float warnTime = 30f;

        [Header("玩家复活")]
        [Tooltip("玩家所有球被吃掉后，多少秒后复活。")]
        [SerializeField] private float playerRespawnDelay = 5f;

        [Tooltip("复活球的基础质量。")]
        [SerializeField] private float playerRespawnMass = 40f;

        [Tooltip("复活后的无敌时间（秒）：这段时间内别人吃不到你，避免刚复活就被守尸。")]
        [SerializeField] private float respawnInvulnerability = 2.5f;

        [Tooltip("复活球的精灵（留空则沿用场景里 Player 的精灵）。")]
        [SerializeField] private Sprite playerSprite;

        [Tooltip("复活球的颜色。")]
        [SerializeField] private Color playerColor = new Color(0.35f, 0.75f, 1f);

        [Tooltip("复活点周围这么大范围内若有质量超过 respawnThreatMassRatio 倍的球，换点重试。")]
        [SerializeField] private float respawnSafeRadius = 10f;

        [Tooltip("“危险球”判定：质量 >= 复活质量 × 该倍率的球视为威胁。")]
        [SerializeField] private float respawnThreatMassRatio = 1.5f;

        [Tooltip("复活点最多重试次数。")]
        [SerializeField] private int respawnPlacementAttempts = 12;

        [Header("AI 自动补员")]
        [Tooltip("场上 AI 数量上限。少于它就补。")]
        [SerializeField] private int aiMaxCount = 10;

        [Tooltip("补员间隔（秒）：两次生成之间至少隔这么久，避免一帧刷出一堆。")]
        [SerializeField] private float aiRespawnDelay = 1.5f;

        [Tooltip("新 AI 的质量范围。")]
        [SerializeField] private float aiMinMass = 4f;
        [SerializeField] private float aiMaxMass = 60f;

        [Tooltip("新 AI 出生无敌时间（秒）：避免刚生成就被大雪球连吃。0 = 关闭。")]
        [SerializeField] private float aiSpawnInvulnerability = 2f;

        [Tooltip("新 AI 的阵营编号（1 = AI）。只有玩家自己的球互免，AI 之间照样互吃。")]
        [SerializeField] private int aiTeam = 1;

        [Tooltip("AI 可用的颜色。")]
        [SerializeField] private Color[] aiColors =
        {
            new Color(1f, 0.55f, 0.5f),
            new Color(1f, 0.75f, 0.35f),
            new Color(0.65f, 0.92f, 0.55f),
            new Color(0.72f, 0.62f, 1f),
            new Color(0.55f, 0.9f, 1f),
        };

        [Header("结算")]
        [Tooltip("局终冻结整个世界（Time.timeScale = 0）。")]
        [SerializeField] private bool freezeOnGameOver = true;

        [SerializeField] private bool muteAudioOnGameOver = false;

        // ------------------------------ 运行时状态 ------------------------------

        public RoundState State { get; private set; } = RoundState.Playing;

        /// <summary>本局是否还在进行（倒计时未结束）。</summary>
        public bool IsRunning => State == RoundState.Playing;

        /// <summary>倒计时是否已经结束（结算中）。</summary>
        public bool IsRoundOver => State == RoundState.Finished;

        /// <summary>本局剩余秒数（不小于 0）。</summary>
        public float RemainingTime { get; private set; }

        /// <summary>剩余时间小于这个值时 UI 可以变红。</summary>
        public float WarnTime => warnTime;

        /// <summary>玩家是否处于“死亡观战 / 复活等待”。</summary>
        public bool IsPlayerRespawning { get; private set; }

        /// <summary>复活还剩多少秒（未在等待时为 0）。</summary>
        public float RespawnTimeLeft { get; private set; }

        /// <summary>本局玩家的最高质量（峰值）—— 结算面板显示的成绩。</summary>
        public float PeakMass { get; private set; }

        /// <summary>结算时玩家的总质量（用来算名次）。</summary>
        public float FinalMass { get; private set; }

        /// <summary>最终名次（1 = 第一）。</summary>
        public int FinalRank { get; private set; } = 1;

        /// <summary>结算时的参赛者总数（存活 AI 数 + 1 名玩家）。</summary>
        public int TotalParticipants { get; private set; } = 1;

        /// <summary>谁吃掉了玩家的最后一个球（可能为 null）。</summary>
        public SizeController Killer { get; private set; }

        /// <summary>局终触发一次（参数为 GameManager 自己，方便 UI 直接读数据）。</summary>
        public event Action<GameManager> GameOver;

        // ------------------------------ 内部字段 ------------------------------

        private float respawnDeadline = -1f;
        private float nextAiSpawnTime;
        private bool gameOverFired;
        private Vector3 lastDeathPosition;
        private int aiSpawnCounter;

        /// <summary>玩家调参快照：玩家控制器被销毁后依然能用来塑造复活球。</summary>
        private PlayerController.PlayerTuning playerTuning;

        private void Awake()
        {
            Instance = this;

            // 上一局结算时把 timeScale 设成了 0，重开场景后必须恢复，
            // 否则新的一局开局就是定格的（重开逻辑最容易踩的坑）。
            Time.timeScale = 1f;

            if (player == null) player = FindFirstObjectByType<PlayerController>();
            if (gameOverPanel == null)
                gameOverPanel = FindFirstObjectByType<GameOverPanel>(FindObjectsInactive.Include);
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        private void Start()
        {
            State = RoundState.Playing;
            gameOverFired = false;

            RemainingTime = Mathf.Max(1f, roundDuration);
            nextAiSpawnTime = Time.unscaledTime + aiRespawnDelay;

            if (player == null) player = PlayerController.Active;

            if (player != null)
            {
                PeakMass = player.TotalMass;
                FinalMass = PeakMass;
                playerTuning = player.CaptureTuning();     // 记下 Inspector 上的调参
                if (playerSprite == null && player.MainBall != null)
                {
                    var sr = player.MainBall.GetComponent<SpriteRenderer>();
                    if (sr != null) playerSprite = sr.sprite;
                }
            }
        }

        private void Update()
        {
            if (State == RoundState.Finished) return;

            // ---------------- 1. 倒计时 ----------------
            // 用 unscaledDeltaTime：即使某一帧被人为暂停（例如弹窗），计时也不会冻住
            RemainingTime = Mathf.Max(0f, RemainingTime - Time.unscaledDeltaTime);

            // ---------------- 2. 玩家状态 ----------------
            // 主球阵亡交接后旧控制器会被销毁，兜底取当前的 Active
            if (player == null || !player.isActiveAndEnabled) player = PlayerController.Active;

            if (player != null)
            {
                if (!IsPlayerRespawning)
                {
                    float mass = player.TotalMass;
                    if (mass > 0f) FinalMass = mass;
                    if (mass > PeakMass) PeakMass = mass;
                }
            }
            else
            {
                // 兜底：没有控制器也没有球 → 进入复活等待
                if (!IsPlayerRespawning) BeginPlayerRespawn(null);
            }

            // ---------------- 3. 复活倒计时 ----------------
            if (IsPlayerRespawning)
            {
                RespawnTimeLeft = Mathf.Max(0f, respawnDeadline - Time.unscaledTime);
                if (RespawnTimeLeft <= 0f) RespawnPlayer();
            }

            // ---------------- 4. AI 补员 ----------------
            TickAiRespawn();

            // ---------------- 5. 时间到 → 结算 ----------------
            if (RemainingTime <= 0f) EndRound();
        }

        // ======================== 玩家死亡 / 复活 ========================

        /// <summary>玩家所有球都没了的时候由 PlayerController 调用（开始复活倒计时，不结束游戏）。</summary>
        public void NotifyPlayerDefeated(SizeController killer)
        {
            if (State == RoundState.Finished) return;

            // 连续多次触发（多个球同帧死亡）只处理一次
            if (IsPlayerRespawning) return;

            Killer = killer;
            BeginPlayerRespawn(killer);
        }

        private void BeginPlayerRespawn(SizeController killer)
        {
            IsPlayerRespawning = true;

            // 顺手把死亡前最后有效的质量记下来（复活期间用来算名次/峰值）
            if (player != null)
            {
                float mass = player.TotalMass;
                if (mass > 0f) FinalMass = mass;
            }

            if (player != null && player.MainBall != null)
                lastDeathPosition = player.MainBall.transform.position;

            respawnDeadline = Time.unscaledTime + Mathf.Max(0.1f, playerRespawnDelay);
            RespawnTimeLeft = Mathf.Max(0f, playerRespawnDelay);

            Debug.Log($"[复活] 玩家被 {(killer != null ? killer.name : "未知")} 吃掉，{playerRespawnDelay:F1} 秒后复活");
        }

        /// <summary>在安全位置生成一颗基础质量的玩家球并恢复控制。</summary>
        public void RespawnPlayer()
        {
            if (State == RoundState.Finished) return;

            IsPlayerRespawning = false;
            RespawnTimeLeft = 0f;
            respawnDeadline = -1f;

            Vector3 spawn = FindSafeSpawnPoint(playerRespawnMass);

            if (playerSprite == null)
            {
                Debug.LogError("[复活] 没有 playerSprite，无法生成玩家球。请把 Ball.png 拖到 GameManager.playerSprite。");
                return;
            }

            PlayerController controller = PlayerController.Active;
            if (controller == null) controller = PlayerController.EnsureExists();

            SizeController ball = controller.SpawnBall("Player", spawn, playerRespawnMass, playerSprite, playerColor);

            player = controller;

            if (ball != null)
            {
                // 复活保护：短短几秒内不会被别人吃掉，否则刚出生就被守在大球旁边秒掉
                ball.SetInvulnerable(respawnInvulnerability);

                Debug.Log($"[复活] 玩家在 {spawn} 复活，质量 {playerRespawnMass:F0}，" +
                          $"无敌 {respawnInvulnerability:F1} 秒");
            }
        }

        /// <summary>
        /// 找一个安全的复活点：先取竞技区内的随机点，再看这个半径内有没有“能一口吃掉我”的球，
        /// 有就换点重试；都不行就退回死亡位置（至少不是原地立刻被吃）。
        /// </summary>
        private Vector3 FindSafeSpawnPoint(float mass)
        {
            return FindSafeSpawnPoint(mass, lastDeathPosition);
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
                    // 留出安全边距，别贴边生成
                    candidate = ArenaManager.Instance.GetRandomPointInside(null, 2f);
                }

                bool blocked = false;
                for (int i = 0; i < all.Count; i++)
                {
                    var ball = all[i];
                    if (ball == null || ball.IsDead) continue;
                    if (ball.Mass < threatMass) continue;     // 吃不了我的忽略

                    if (Vector2.Distance(ball.transform.position, candidate) < safeRadius + ball.Radius)
                    {
                        blocked = true;
                        break;
                    }
                }

                if (!blocked) return candidate;
            }

            return fallback;
        }

        // ======================== AI 补员 ========================

        /// <summary>场上存活的 AI 数量。</summary>
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

            int alive = AliveAiCount;
            if (alive >= aiMaxCount) return;

            nextAiSpawnTime = Time.unscaledTime + Mathf.Max(0.1f, aiRespawnDelay);
            SpawnAi();
        }

        /// <summary>随机位置生成一只 AI（质量/颜色/半径随机）。</summary>
        public SizeController SpawnAi()
        {
            if (playerSprite == null) return null;

            float mass = UnityEngine.Random.Range(Mathf.Min(aiMinMass, aiMaxMass), Mathf.Max(aiMinMass, aiMaxMass));
            Color color = aiColors != null && aiColors.Length > 0
                ? aiColors[aiSpawnCounter % aiColors.Length]
                : Color.red;

            Vector3 position = ArenaManager.Instance != null
                ? ArenaManager.Instance.GetRandomPointInside(null, 1.5f)
                : Vector3.zero;

            // 别生成在能一口吃掉它的球脸上（雪球模式下就是那只霸主）
            position = FindSafeSpawnPoint(mass, position);

            string name = $"AI_{aiSpawnCounter:00}";
            aiSpawnCounter++;

            var go = BallFactory.CreateBall(name, position, mass, playerSprite, color, aiTeam, 10);
            go.tag = "Enemy";

            var ai = go.AddComponent<AIController>();
            // 给一点个体差异：速度/扫描半径小范围随机，图里不会所有 AI 一个节奏
            ai.ApplyRandomProfile(UnityEngine.Random.Range(0.9f, 1.25f), UnityEngine.Random.Range(0.85f, 1.15f));

            var size = go.GetComponent<SizeController>();
            size.SetInvulnerable(aiSpawnInvulnerability);
            Debug.Log($"[补员] 生成 {name}（质量 {mass:F0}）在 {position}，场上 AI 数 {AliveAiCount}");
            return size;
        }

        // ======================== 局终结算 ========================

        /// <summary>倒计时结束：冻结世界、按当前质量排名、弹面板。</summary>
        public void EndRound()
        {
            if (State == RoundState.Finished) return;
            if (gameOverFired) return;

            gameOverFired = true;
            State = RoundState.Finished;
            IsPlayerRespawning = false;
            RespawnTimeLeft = 0f;
            RemainingTime = 0f;

            // 局终那一帧的玩家总质量（可能为 0 —— 玩家正好死在复活等待里，
            // 这时不能把成绩清零，要保留死亡前最后记录的 FinalMass）
            if (player == null || !player.isActiveAndEnabled) player = PlayerController.Active;

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

        /// <summary>
        /// 冻结世界：① timeScale = 0（物理不再步进、所有 FixedUpdate 不再调用）；
        /// ② 顺手禁用 AI 控制器、残余速度清零（多一层保险，也方便 Inspector 里看状态）。
        /// </summary>
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

        /// <summary>重新开始：恢复时间缩放后重载当前场景。</summary>
        public void RestartGame()
        {
            Time.timeScale = 1f;
            AudioListener.pause = false;

            var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
            UnityEngine.SceneManagement.SceneManager.LoadScene(scene.buildIndex >= 0 ? scene.buildIndex : 0);
        }

        /// <summary>
        /// 名次 = 1 + “场上质量比我大的存活球数”（玩家总质量 vs 每个 AI 的质量）。
        /// 用 SizeController 静态注册表统计，避免 FindObjectsByType 全场景遍历。
        /// </summary>
        private void ComputeRank()
        {
            int aliveOpponents = 0;
            int betterThanMe = 0;

            var all = SizeController.Registry;
            for (int i = 0; i < all.Count; i++)
            {
                var ball = all[i];
                if (ball == null || ball.IsDead) continue;
                if (ball.Team == 0) continue;          // 只统计对手（AI）

                aliveOpponents++;
                if (ball.Mass > FinalMass) betterThanMe++;
            }

            FinalRank = betterThanMe + 1;
            TotalParticipants = aliveOpponents + 1;    // +1 是玩家
        }
    }
}
