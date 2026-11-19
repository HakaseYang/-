using UnityEngine;

public class AudioManager : MonoBehaviour
{
    private static AudioManager instance;

    /// <summary>
    /// 全局单例。get 里兜底查找场景里的实例：
    /// 1) 关掉 Domain Reload 时静态字段会跨 Play 保留，旧引用可能已被销毁；
    /// 2) 直接从 LevelScene 起播（没经过主菜单）时，Awake 顺序也不保证别人先取到。
    /// 这样调用方 AudioManager.Instance.PlayXxx() 就不会因为拿不到实例而抛 NRE。
    /// </summary>
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

    [Header("Audio Sources")]
    public AudioSource bgmSource;
    public AudioSource sfxSource;

    [Header("SFX Clips")]
    public AudioClip eatFoodClip;
    public AudioClip eatEnemyClip;
    public AudioClip splitClip;
    public AudioClip spitClip;
    public AudioClip uiClickClip;
    public AudioClip gameOverClip;

    void Awake()
    {
        // 注意这里用字段 instance 而不是属性 Instance：
        // 属性会去场景里 Find 一个实例，那样会把自己也算进来，导致误判“已经有了”而自毁。
        if (instance == null)
        {
            instance = this;
            DontDestroyOnLoad(gameObject); // 切场景不销毁音乐
        }
        else if (instance != this)
        {
            Destroy(gameObject);
        }
    }

    void Start()
    {
        // 播放背景音乐
        if (bgmSource != null) 
        {
            bgmSource.Play();
        }
    }

    // --- 提供给组员调用的接口 ---
    public void PlayBGM()
    {
        if (bgmSource != null && !bgmSource.isPlaying)
        {
            bgmSource.Play();
        }
    }

    public void PlayEatFood() 
    {
        PlaySFX(eatFoodClip, 0.5f);
    }

    public void PlayEatEnemy() 
    {
        PlaySFX(eatEnemyClip, 0.8f);
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
        PlaySFX(uiClickClip, 0.5f);
    }

    public void PlayGameOver() 
    {
        if (bgmSource != null) 
        {
            bgmSource.Stop(); // 死了停音乐
        }
        PlaySFX(gameOverClip, 1f);
    }

    private void PlaySFX(AudioClip clip, float volume)
    {
        if (clip != null && sfxSource != null)
        {
            // 随机音调，让连续吃食物的声音不那么死板
            sfxSource.pitch = Random.Range(0.9f, 1.1f); 
            sfxSource.PlayOneShot(clip, volume);
        }
    }
}