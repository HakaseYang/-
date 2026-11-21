using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace QiuQiu
{
    public class PauseMenu : MonoBehaviour
    {
        [SerializeField] private GameObject panel;
        [SerializeField] private GameObject body;
        [SerializeField] private GameObject settings;
        [SerializeField] private Slider bgmSlider;
        [SerializeField] private Slider sfxSlider;
        [SerializeField] private Button optionButton;
        [SerializeField] private Button restartButton;
        [SerializeField] private Button backButton;
        [SerializeField] private GameObject closeButton;


        private void Awake()
        {
            if (panel == null && transform.childCount > 0)
            {
                panel = transform.GetChild(0).gameObject;
            }

            if (panel != null)
            {
                panel.SetActive(false);
            }

            if (body != null)
            {
                body.SetActive(true);
            }

            if (settings != null)
            {
                settings.SetActive(false);
            }

        }

        private void Update()
        {
            //for test
            if (Keyboard.current == null || !Keyboard.current.escapeKey.wasPressedThisFrame)
            {
                return;
            }

            Toggle();
        }

        public void Toggle()
        {
            if (panel == null)
            {
                return;
            }

            if (!panel.activeSelf)
            {
                Click();
                Show();
            }
            else if (settings != null && settings.activeSelf)
            {
                CloseSettings();
            }
            else
            {
                Resume();
            }
        }

        public void Show()
        {
            if (GameManager.Instance != null && GameManager.Instance.State != GameManager.RoundState.Playing)
            {
                return;
            }

            if (panel != null)
            {
                panel.SetActive(true);
            }

            if (body != null)
            {
                body.SetActive(true);
            }

            if (settings != null)
            {
                settings.SetActive(false);
            }

            if (closeButton != null)
            {
                closeButton.SetActive(true);
            }

            LoadVolumes();

            Time.timeScale = 0f;
            AudioListener.pause = true;
        }

        public void Resume()
        {
            Click();
            Leave();

            if (panel != null)
            {
                panel.SetActive(false);
            }
        }

        public void OpenSettings()
        {
            Click();

            if (body != null)
            {
                body.SetActive(false);
            }

            if (settings != null)
            {
                settings.SetActive(true);
            }

            if (closeButton != null)
            {
                closeButton.SetActive(false);
            }

            LoadVolumes();
        }

        public void CloseSettings()
        {
            Click();

            if (settings != null)
            {
                settings.SetActive(false);
            }

            if (body != null)
            {
                body.SetActive(true);
            }

            if (closeButton != null)
            {
                closeButton.SetActive(true);
            }
        }

        public void SetBgmVolume(float value)
        {
            if (AudioManager.Instance != null)
            {
                AudioManager.Instance.SetBgmVolume(value);
            }
        }

        public void SetSfxVolume(float value)
        {
            if (AudioManager.Instance != null)
            {
                AudioManager.Instance.SetSfxVolume(value);
            }
        }

        public void Restart()
        {
            Click();
            SceneFlow.LoadGameplay();
        }

        public void BackToMenu()
        {
            Click();
            SceneFlow.LoadMainMenu();
        }

        private void Click()
        {
            if (AudioManager.Instance != null)
            {
                AudioManager.Instance.PlayUIClick();
            }
        }

        private void Leave()
        {
            Time.timeScale = 1f;
            AudioListener.pause = false;
        }

        private void LoadVolumes()
        {
            if (AudioManager.Instance == null)
            {
                return;
            }

            if (bgmSlider != null)
            {
                bgmSlider.SetValueWithoutNotify(AudioManager.Instance.GetBgmVolume());
            }

            if (sfxSlider != null)
            {
                sfxSlider.SetValueWithoutNotify(AudioManager.Instance.GetSfxVolume());
            }
        }
    }
}
