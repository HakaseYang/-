using UnityEngine;
using UnityEngine.UI;

namespace QiuQiu
{
    [DisallowMultipleComponent]
    public class GameOverPanel : MonoBehaviour
    {
        [Header("Panel")]
        [SerializeField] private GameObject panelRoot;

        [Header("Text")]
        [SerializeField] private Text titleText;
        [SerializeField] private Text peakMassText;
        [SerializeField] private Text finalRankText;

        [Header("Buttons")]
        [SerializeField] private Button restartButton;
        [SerializeField] private Button backToMenuButton;

        [Header("Restart delay in seconds, 0 = instant")]
        [SerializeField] private float restartDelay = 0f;

        [Header("Auto select a button when shown")]
        [SerializeField] private bool selectButtonOnShow = true;

        private GameManager gameManager;
        private float pendingRestartAt = -1f;

        private void Awake()
        {
            if (panelRoot == null)
            {
                panelRoot = gameObject;
            }

            if (gameManager == null)
            {
                gameManager = FindFirstObjectByType<GameManager>();
            }

            // wire the click in code only when no persistent listener is set,
            // otherwise one click reloads twice and the screen blinks
            if (restartButton != null && restartButton.onClick.GetPersistentEventCount() == 0)
            {
                restartButton.onClick.AddListener(Restart);
            }

            if (backToMenuButton != null && backToMenuButton.onClick.GetPersistentEventCount() == 0)
            {
                backToMenuButton.onClick.AddListener(BackToMenu);
            }

            Hide();
        }

        private void Update()
        {
            if (pendingRestartAt < 0f)
            {
                return;
            }

            // unscaled time, timeScale is 0 while the result screen is up
            if (Time.unscaledTime >= pendingRestartAt)
            {
                pendingRestartAt = -1f;
                RestartNow();
            }
        }

        public void Show(GameManager manager)
        {
            // the root may be inactive, so light it up before the children
            if (!gameObject.activeSelf)
            {
                gameObject.SetActive(true);
            }

            if (manager != null)
            {
                gameManager = manager;
            }

            if (panelRoot == null)
            {
                panelRoot = gameObject;
            }

            float peak = gameManager != null ? gameManager.PeakMass : 0f;
            int rank = gameManager != null ? gameManager.FinalRank : 1;
            int total = gameManager != null ? gameManager.TotalParticipants : 1;

            if (titleText != null)
            {
                titleText.text = "GAME OVER";
            }

            if (peakMassText != null)
            {
                peakMassText.text = $"BEST MASS  {peak:F0}";
            }

            if (finalRankText != null)
            {
                finalRankText.text = $"RANK  {rank} / {total}";
            }

            SaveData.ReportBestMass(peak);

            panelRoot.SetActive(true);

            if (AudioManager.Instance != null)
            {
                AudioManager.Instance.PlayGameOver();
            }

            if (selectButtonOnShow && restartButton != null && UnityEngine.EventSystems.EventSystem.current != null)
            {
                UnityEngine.EventSystems.EventSystem.current.SetSelectedGameObject(restartButton.gameObject);
            }
        }

        public void Hide()
        {
            if (panelRoot == null)
            {
                panelRoot = gameObject;
            }

            panelRoot.SetActive(false);
        }

        public void Restart()
        {
            if (AudioManager.Instance != null)
            {
                AudioManager.Instance.PlayUIClick();
                AudioManager.Instance.PlayBGM();
            }

            if (restartDelay > 0f)
            {
                pendingRestartAt = Time.unscaledTime + restartDelay;
                if (restartButton != null)
                {
                    restartButton.interactable = false;
                }

                return;
            }

            RestartNow();
        }

        private void RestartNow()
        {
            SceneFlow.Reload();
        }

        public void BackToMenu()
        {
            if (AudioManager.Instance != null)
            {
                AudioManager.Instance.PlayUIClick();
                AudioManager.Instance.PlayBGM();
            }

            SceneFlow.LoadMainMenu();
        }
    }
}
