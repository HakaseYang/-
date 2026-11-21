using UnityEngine;
using UnityEngine.UI;


public class VolumeSliders : MonoBehaviour
{
    [SerializeField] private Slider bgmSlider;
    [SerializeField] private Slider sfxSlider;

    private void Awake()
    {
        if (bgmSlider != null)
        {
            bgmSlider.onValueChanged.AddListener(OnBgmChanged);
        }
        if (sfxSlider != null)
        {
            sfxSlider.onValueChanged.AddListener(OnSfxChanged);
        }
    }

    private void OnEnable()
    {
        AudioManager audio = AudioManager.Instance;
        if (audio == null)
        {
            return;
        }
        if (bgmSlider != null)
        {
            bgmSlider.SetValueWithoutNotify(audio.GetBgmVolume());
        }
        if (sfxSlider != null)
        {
            sfxSlider.SetValueWithoutNotify(audio.GetSfxVolume());
        }
    }

    private static void OnBgmChanged(float value)
    {
        if (AudioManager.Instance != null)
        {
            AudioManager.Instance.SetBgmVolume(value);
        }
    }

    private static void OnSfxChanged(float value)
    {
        if (AudioManager.Instance != null)
        {
            AudioManager.Instance.SetSfxVolume(value);
        }
    }
}
