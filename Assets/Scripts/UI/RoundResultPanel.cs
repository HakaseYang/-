using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace QiuQiu
{
    public class RoundResultPanel : MonoBehaviour
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

        private GameManager gameManager;

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
                titleText.text = "ROUND OVER";
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
                AudioManager.Instance.PlayRoundEnd();
            }

            // keyboard and gamepad players should reach restart without a mouse
            if (restartButton != null && EventSystem.current != null)
            {
                EventSystem.current.SetSelectedGameObject(restartButton.gameObject);
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
