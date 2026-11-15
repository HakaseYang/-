using UnityEngine;
using UnityEngine.UI;

namespace QiuQiu
{
    /// <summary>
    /// 局内 HUD：左上角显示当前质量与实时名次。
    ///
    /// 刷新策略：
    ///   每 0.2 秒（可调）刷新一次，而不是每帧。原因：
    ///     - 文字内容变一次就会触发 uGUI 的 mesh 重建（Canvas 重新合批），每帧改是纯浪费；
    ///     - 质量/名次这种数值，5Hz 已经足够跟手，玩家感知不到差别。
    ///   计时用 unscaledTime：结算时 timeScale = 0，HUD 仍需要能正常刷最后一下。
    ///
    /// 名次算法与结算面板保持一致（GameManager.ComputeRank）：
    ///   名次 = 1 + 场上“质量比我大的存活对手数”；总数 = 存活对手数 + 1（玩家）。
    ///   统计走 SizeController.Registry 静态表，不做全场景 FindObjectsByType。
    /// </summary>
    [DisallowMultipleComponent]
    public class HUDController : MonoBehaviour
    {
        [Header("引用")]
        [Tooltip("玩家控制器。留空自动找。")]
        [SerializeField] private PlayerController player;

        [Tooltip("显示“Mass: 123”。留空则自动找子物体里名字含 Mass 的 Text。")]
        [SerializeField] private Text massText;

        [Tooltip("显示“Rank: 3 / 12”。留空则自动找子物体里名字含 Rank 的 Text。")]
        [SerializeField] private Text rankText;

        [Header("刷新")]
        [Tooltip("刷新间隔（秒）。0.2 = 每秒 5 次，够跟手又不浪费。")]
        [SerializeField] private float refreshInterval = 0.2f;

        [Header("格式")]
        [SerializeField] private string massFormat = "Mass: {0:F0}";
        [SerializeField] private string rankFormat = "Rank: {0} / {1}";

        private float nextRefreshTime;
        private int lastRank = -1;
        private int lastTotal = -1;
        private int lastMassInt = -1;

        /// <summary>当前名次（1 = 第一）。外部（例如结算）可读。</summary>
        public int CurrentRank { get; private set; } = 1;
        public int TotalParticipants { get; private set; } = 1;

        private void Awake()
        {
            if (player == null) player = FindFirstObjectByType<PlayerController>();

            if (massText == null || rankText == null)
            {
                // 允许把两个 Text 放在同一个 HUD 物体下的子物体里，按名字自动认领
                foreach (var text in GetComponentsInChildren<Text>(true))
                {
                    if (massText == null && text.name.IndexOf("Mass", System.StringComparison.OrdinalIgnoreCase) >= 0)
                        massText = text;
                    else if (rankText == null && text.name.IndexOf("Rank", System.StringComparison.OrdinalIgnoreCase) >= 0)
                        rankText = text;
                }
            }
        }

        private void Start()
        {
            Refresh(true);
            nextRefreshTime = Time.unscaledTime + Mathf.Max(0.05f, refreshInterval);
        }

        private void Update()
        {
            if (Time.unscaledTime < nextRefreshTime) return;
            nextRefreshTime = Time.unscaledTime + Mathf.Max(0.05f, refreshInterval);
            Refresh(false);
        }

        /// <summary>刷新一次显示。force = true 时忽略“内容没变就不改”的优化。</summary>
        private void Refresh(bool force)
        {
            // 已结算：停更，保留死亡前最后的读数（否则会变成 Mass: 0 / Rank: 11，看着像 bug）
            if (!force && GameManager.Instance != null && !GameManager.Instance.IsRunning) return;

            float mass = 0f;

            // 主球阵亡交接后旧控制器会被销毁，兜底取当前的 Active
            if (player == null || !player.isActiveAndEnabled) player = PlayerController.Active;

            if (player != null)
            {
                mass = player.TotalMass;
                ComputeRank(mass);
            }

            int massInt = Mathf.RoundToInt(mass);

            if (force || massInt != lastMassInt)
            {
                lastMassInt = massInt;
                if (massText != null) massText.text = string.Format(massFormat, mass);
            }

            if (force || CurrentRank != lastRank || TotalParticipants != lastTotal)
            {
                lastRank = CurrentRank;
                lastTotal = TotalParticipants;
                if (rankText != null) rankText.text = string.Format(rankFormat, CurrentRank, TotalParticipants);
            }
        }

        /// <summary>名次 = 1 + 质量比我大的存活对手数；总数 = 存活对手数 + 1。</summary>
        private void ComputeRank(float myMass)
        {
            int opponents = 0;
            int better = 0;

            var all = SizeController.Registry;
            for (int i = 0; i < all.Count; i++)
            {
                SizeController ball = all[i];
                if (ball == null || ball.IsDead) continue;
                if (ball.Team == 0) continue;   // 玩家自己的球不参与“对手”统计

                opponents++;
                if (ball.Mass > myMass) better++;
            }

            CurrentRank = better + 1;
            TotalParticipants = opponents + 1;
        }
    }
}
