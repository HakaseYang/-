using System;
using UnityEngine;
using UnityEngine.UI;

namespace QiuQiu
{
    [DisallowMultipleComponent]
    public class HUDController : MonoBehaviour
    {
        [SerializeField] private PlayerController player;
        [SerializeField] private Text massText;
        [SerializeField] private Text rankText;

        [SerializeField] private float refreshInterval = 0.2f;

        [SerializeField] private string massFormat = "Mass: {0:F0}";
        [SerializeField] private string rankFormat = "Rank: {0} / {1}";

        private float interval;
        private float nextRefreshTime;
        private int lastRank = -1;
        private int lastTotal = -1;
        private int lastMassInt = -1;

        public int CurrentRank { get; private set; } = 1;
        public int TotalParticipants { get; private set; } = 1;

        private void Awake()
        {
            interval = Mathf.Max(0.05f, refreshInterval);

            if (player == null)
            {
                player = FindFirstObjectByType<PlayerController>();
            }
            if (massText != null && rankText != null)
            {
                return;
            }

            // the two labels live under this object; adopt them by name when unset
            foreach (Text text in GetComponentsInChildren<Text>(true))
            {
                if (massText == null && IsMassLabel(text.name))
                {
                    massText = text;
                }
                else if (rankText == null && text.name.IndexOf("Rank", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    rankText = text;
                }
            }
        }

        private static bool IsMassLabel(string objectName)
        {
            return objectName.IndexOf("Mass", StringComparison.OrdinalIgnoreCase) >= 0
                || objectName.IndexOf("Weight", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private void Start()
        {
            Refresh(true);
            nextRefreshTime = Time.unscaledTime + interval;
        }

        private void Update()
        {
            if (Time.unscaledTime < nextRefreshTime)
            {
                return;
            }
            nextRefreshTime = Time.unscaledTime + interval;
            Refresh(false);
        }

        private void Refresh(bool force)
        {
            GameManager gm = GameManager.Instance;

            // round over: keep the last numbers instead of showing zeros
            if (!force && gm != null && !gm.IsRunning)
            {
                return;
            }

            // after a hand-over the old controller is gone, fall back to Active
            if (player == null || !player.isActiveAndEnabled)
            {
                player = PlayerController.Active;
            }

            float mass = player != null ? player.TotalMass : 0f;
            UpdateRank(gm, mass);
            UpdateLabels(force, mass);
        }

        private void UpdateRank(GameManager gm, float mass)
        {
            TotalParticipants = gm != null ? gm.ParticipantCount : 1 + ScoreBoard.AliveCount(Team.Enemy);
            ScoreBoard.Rank(mass, TotalParticipants, out int rank);
            CurrentRank = rank;
        }

        private void UpdateLabels(bool force, float mass)
        {
            int massInt = Mathf.RoundToInt(mass);
            if (force || massInt != lastMassInt)
            {
                lastMassInt = massInt;
                if (massText != null)
                {
                    massText.text = string.Format(massFormat, mass);
                }
            }

            if (force || CurrentRank != lastRank || TotalParticipants != lastTotal)
            {
                lastRank = CurrentRank;
                lastTotal = TotalParticipants;
                if (rankText != null)
                {
                    rankText.text = string.Format(rankFormat, CurrentRank, TotalParticipants);
                }
            }
        }
    }
}
