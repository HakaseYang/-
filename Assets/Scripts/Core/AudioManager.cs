using UnityEngine;
using UnityEngine.SceneManagement;

public class AudioManager : MonoBehaviour
{
    private static AudioManager instance;

    public static AudioManager Instance
    {
        get
        {
            if (instance == null)
            {
                instance = FindFirstObjectByType<AudioManager>();
            }

            return instance;
        }
        private set { instance = value; }
    }

    private const string BgmVolumeKey = "audio.bgm";
    private const string SfxVolumeKey = "audio.sfx";

    [Header("Audio Sources")]
    public AudioSource bgmSource;
    public AudioSource sfxSource;
    public AudioSource uiSource;

    [Header("SFX Clips")]
    public AudioClip eatFoodClip;
    public AudioClip eatEnemyClip;
    public AudioClip splitClip;
    public AudioClip spitClip;
    public AudioClip uiClickClip;
    public AudioClip gameOverClip;

    [Header("Eat Sound Budget")]
    [Tooltip("Minimum seconds between two eat sounds. Ten AI eating at once must not flood the mixer.")]
    public float eatSoundInterval = 0.07f;

    private float nextEatSoundTime;

    void Awake()
    {
        // use the field here, not the Instance property:
        // the property searches the scene, finds this very object and destroys it.
        if (instance == null)
        {
            instance = this;
            DontDestroyOnLoad(gameObject); // survives scene loads
            SceneManager.sceneLoaded += OnSceneLoaded;
        }
        else if (instance != this)
        {
            Destroy(gameObject);
        }
    }

    void OnDestroy()
    {
        if (instance == this)
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
            instance = null;
        }
    }

    void Start()
    {
        if (bgmSource != null)
        {
            bgmSource.volume = PlayerPrefs.GetFloat(BgmVolumeKey, bgmSource.volume);
            // the round ends with PlayRoundEnd stopping the music, so a fresh start has to arm it again
            bgmSource.Play();
        }
        if (sfxSource != null)
        {
            sfxSource.volume = PlayerPrefs.GetFloat(SfxVolumeKey, sfxSource.volume);
            // ui clicks must still be heard while the pause screen holds the listener paused
            sfxSource.ignoreListenerPause = true;
        }
        if (uiSource != null)
        {
            uiSource.volume = PlayerPrefs.GetFloat(SfxVolumeKey, uiSource.volume);
            uiSource.ignoreListenerPause = true;
        }
    }

    private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (bgmSource == null || bgmSource.clip == null || bgmSource.isPlaying)
        {
            return;
        }
        bgmSource.volume = GetBgmVolume();
        bgmSource.Play();
    }

    // --- volume ---
    public float GetBgmVolume()
    {
        return bgmSource != null ? bgmSource.volume : PlayerPrefs.GetFloat(BgmVolumeKey, 1f);
    }

    public void SetBgmVolume(float value)
    {
        value = Mathf.Clamp01(value);
        if (bgmSource != null)
        {
            bgmSource.volume = value;
        }
        PlayerPrefs.SetFloat(BgmVolumeKey, value);
    }

    public float GetSfxVolume()
    {
        return sfxSource != null ? sfxSource.volume : PlayerPrefs.GetFloat(SfxVolumeKey, 1f);
    }

    public void SetSfxVolume(float value)
    {
        value = Mathf.Clamp01(value);
        if (sfxSource != null)
        {
            sfxSource.volume = value;
        }
        if (uiSource != null)
        {
            uiSource.volume = value;
        }
        PlayerPrefs.SetFloat(SfxVolumeKey, value);
    }

    // --- public hooks ---
    public void PlayBGM()
    {
        if (bgmSource != null && !bgmSource.isPlaying)
        {
            bgmSource.Play();
        }
    }

    public void PlayEatFood()
    {
        if (Time.unscaledTime < nextEatSoundTime)
        {
            return;
        }
        nextEatSoundTime = Time.unscaledTime + eatSoundInterval;
        PlaySFX(eatFoodClip, 0.25f);
    }

    public void PlayEatEnemy()
    {
        if (Time.unscaledTime < nextEatSoundTime)
        {
            return;
        }
        nextEatSoundTime = Time.unscaledTime + eatSoundInterval;
        PlaySFX(eatEnemyClip, 0.6f);
    }

    public void PlaySplit()
    {
        PlaySFX(splitClip, 0.6f);
    }

    public void PlaySpit()
    {
        PlaySFX(spitClip, 0.4f);
    }

    public void PlayUIClick()
    {
        AudioSource source = uiSource != null ? uiSource : sfxSource;
        if (uiClickClip != null && source != null)
        {
            source.PlayOneShot(uiClickClip, 0.5f);
        }
    }

    public void PlayRoundEnd()
    {
        if (bgmSource != null)
        {
            bgmSource.Stop(); // stop the music on death
        }
        PlaySFX(gameOverClip, 1f);
    }

    private void PlaySFX(AudioClip clip, float volume)
    {
        if (clip != null && sfxSource != null)
        {
            // random pitch so repeated pickups do not sound flat
            sfxSource.pitch = Random.Range(0.9f, 1.1f);
            sfxSource.PlayOneShot(clip, volume);
        }
    }
}
