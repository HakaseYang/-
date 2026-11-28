using System.Collections.Generic;
using UnityEngine;

namespace QiuQiu
{
    [DisallowMultipleComponent]
    public class VirusSpawner : MonoBehaviour
    {
        public static VirusSpawner Instance { get; private set; }

        [Header("Count")]
        [Tooltip("Viruses the map settles at once it has filled up")]
        [SerializeField] private int virusCount = 6;
        [Tooltip("Hard cap, including the ones fired by feeding a virus")]
        [SerializeField] private int maxVirus = 7;
        [Tooltip("Seconds between one virus disappearing and the next one appearing")]
        [SerializeField] private float respawnMinDelay = 2f;
        [SerializeField] private float respawnMaxDelay = 4f;

        [Header("Virus")]
        [SerializeField] private float virusMass = 120f;
        [SerializeField] private float radiusScale = 0.237f;
        [SerializeField] private Color virusColor = new Color(0.42f, 0.85f, 0.55f, 1f);
        [Tooltip("Fallback only: Virus.ApplyVisual overrides this at runtime with the size based order")]
        [SerializeField] private int sortingOrder = 15;

        [Header("Star Sprite")]
        [SerializeField] private int textureSize = 128;
        [SerializeField] private int starPoints = 12;
        [SerializeField, Range(0.3f, 0.95f)] private float starInnerRatio = 0.74f;

        [Header("Placement")]
        [SerializeField] private float borderMargin = 2f;
        [SerializeField] private float minDistanceFromBalls = 8f;
        [SerializeField] private float minDistanceBetweenViruses = 9f;
        [SerializeField] private int placementAttempts = 12;
        [SerializeField] private Transform virusParent;

        private Sprite starSprite;
        private float nextRespawnTime;

        public int Count { get { return Virus.ActiveCount; } }
        public int VirusCount { get { return virusCount; } }
        public int MaxVirus { get { return maxVirus; } }

        private void Awake()
        {
            Instance = this;
            starSprite = BuildStarSprite();
        }

        private void OnDestroy()
        {
            if (Instance == this)
            {
                Instance = null;
            }
        }

        private void Start()
        {
            nextRespawnTime = Time.time + RandomDelay();
        }

        private void Update()
        {
            int wanted = Mathf.Clamp(virusCount, 0, Mathf.Max(0, maxVirus));

            if (Virus.ActiveCount >= Mathf.Min(wanted, maxVirus))
            {
                nextRespawnTime = Mathf.Max(nextRespawnTime, Time.time + RandomDelay());
                return;
            }

            if (Time.time < nextRespawnTime)
            {
                return;
            }

            if (SpawnOne())
            {
                nextRespawnTime = Time.time + RandomDelay();
            }
            else
            {
                nextRespawnTime = Time.time + 0.5f;
            }
        }

        private float RandomDelay()
        {
            float low = Mathf.Max(0.1f, respawnMinDelay);
            float high = Mathf.Max(low, respawnMaxDelay);
            return Random.Range(low, high);
        }

        public bool SpawnOne()
        {
            ArenaManager arena = ArenaManager.Instance;
            if (arena == null)
            {
                return false;
            }

            if (Virus.ActiveCount >= maxVirus)
            {
                return false;
            }

            for (int attempt = 0; attempt < Mathf.Max(1, placementAttempts); attempt++)
            {
                Vector2 point = arena.GetRandomPointInside(borderMargin);
                if (!IsFarFromBalls(point) || !IsFarFromViruses(point))
                {
                    continue;
                }

                Create(point, Vector2.zero);
                return true;
            }

            return false;
        }

        public Virus SpawnFrom(Virus source, Vector2 direction, float speed)
        {
            if (source == null)
            {
                return null;
            }

            if (Virus.ActiveCount >= maxVirus)
            {
                return null;
            }

            Vector2 dir = direction.sqrMagnitude > 0.0001f ? direction.normalized : Vector2.up;
            Vector3 position = source.transform.position + (Vector3)(dir * (source.Radius * 2f + 0.1f));

            ArenaManager arena = ArenaManager.Instance;
            if (arena != null)
            {
                Vector2 centre = arena.Center;
                Vector2 half = arena.HalfSize;
                float margin = source.Radius;
                position = new Vector3(
                    Mathf.Clamp(position.x, centre.x - half.x + margin, centre.x + half.x - margin),
                    Mathf.Clamp(position.y, centre.y - half.y + margin, centre.y + half.y - margin),
                    position.z);
            }

            Virus spawned = Create(position, dir * speed);
            return spawned;
        }

        private Virus Create(Vector2 position, Vector2 velocity)
        {
            GameObject go = new GameObject("Virus");
            go.transform.position = position;

            if (virusParent != null)
            {
                go.transform.SetParent(virusParent, true);
            }

            SpriteRenderer sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = starSprite;
            sr.color = virusColor;
            sr.sortingOrder = sortingOrder;

            Rigidbody2D rb = go.AddComponent<Rigidbody2D>();
            rb.bodyType = RigidbodyType2D.Dynamic;
            rb.gravityScale = 0f;
            rb.freezeRotation = true;
            rb.interpolation = RigidbodyInterpolation2D.Interpolate;
            rb.linearDamping = 0.9f;

            CircleCollider2D col = go.AddComponent<CircleCollider2D>();
            col.radius = 0.5f;
            col.isTrigger = true;

            Virus virus = go.AddComponent<Virus>();
            virus.Configure(virusMass, radiusScale);

            if (velocity.sqrMagnitude > 0.0001f)
            {
                rb.linearVelocity = velocity;
            }

            return virus;
        }

        private bool IsFarFromBalls(Vector2 point)
        {
            IReadOnlyList<SizeController> balls = SizeController.Registry;
            for (int i = 0; i < balls.Count; i++)
            {
                SizeController ball = balls[i];
                if (ball == null || ball.IsDead)
                {
                    continue;
                }

                float gap = minDistanceFromBalls + ball.Radius;
                if (((Vector2)ball.transform.position - point).sqrMagnitude < gap * gap)
                {
                    return false;
                }
            }
            return true;
        }

        private bool IsFarFromViruses(Vector2 point)
        {
            IReadOnlyList<Virus> viruses = Virus.All;
            for (int i = 0; i < viruses.Count; i++)
            {
                Virus other = viruses[i];
                if (other == null)
                {
                    continue;
                }

                float gap = minDistanceBetweenViruses + other.Radius;
                if (((Vector2)other.transform.position - point).sqrMagnitude < gap * gap)
                {
                    return false;
                }
            }
            return true;
        }

        private Sprite BuildStarSprite()
        {
            int size = Mathf.Clamp(textureSize, 32, 512);
            Texture2D texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
            texture.filterMode = FilterMode.Bilinear;

            float centre = (size - 1) * 0.5f;
            float outer = centre - 1f;
            float inner = outer * starInnerRatio;
            int points = Mathf.Max(3, starPoints);
            float wedge = Mathf.PI * 2f / points;

            Color[] pixels = new Color[size * size];
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float dx = x - centre;
                    float dy = y - centre;
                    float distance = Mathf.Sqrt(dx * dx + dy * dy);
                    float angle = Mathf.Atan2(dy, dx);
                    if (angle < 0f)
                    {
                        angle += Mathf.PI * 2f;
                    }

                    float phase = Mathf.Repeat(angle, wedge) / wedge;
                    float edge = Mathf.Lerp(inner, outer, Mathf.Abs(phase * 2f - 1f));
                    float alpha = Mathf.Clamp01((edge - distance) / 1.5f);
                    pixels[y * size + x] = new Color(1f, 1f, 1f, alpha);
                }
            }

            texture.SetPixels(pixels);
            texture.Apply(false, false);

            return Sprite.Create(texture, new Rect(0f, 0f, size, size), new Vector2(0.5f, 0.5f), size);
        }
    }
}
