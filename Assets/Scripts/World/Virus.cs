using System.Collections.Generic;
using UnityEngine;

namespace QiuQiu
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Rigidbody2D), typeof(CircleCollider2D))]
    public class Virus : MonoBehaviour
    {
        [Header("Size / Mass")]
        [SerializeField] private float mass = 120f;
        [Tooltip("Radius = radiusScale * sqrt(mass)")]
        [SerializeField] private float radiusScale = 0.237f;

        [Header("Burst")]
        [Tooltip("Upper bound of new cells one burst may create")]
        [SerializeField] private int burstPieces = 16;
        [Tooltip("Slow push after the burst: low on purpose, the spacing comes from the layout")]
        [SerializeField] private float burstLaunchSpeed = 6.5f;
        [Tooltip("How long a burst piece keeps its outward push before normal following takes over")]
        [SerializeField] private float burstCoastDuration = 0.65f;
        [SerializeField] private float burstSpawnGap = 0.05f;
        [SerializeField] private float burstCooldown = 0.4f;

        [Header("Burst Layout")]
        [Tooltip("Cells on the inner ring; the rest go to the outer ring")]
        [SerializeField] private int burstInnerCount = 7;
        [Tooltip("Ring radius = pieceRadius * this")]
        [SerializeField] private float burstInnerRadiusFactor = 2.8f;
        [SerializeField] private float burstOuterRadiusFactor = 5.0f;
        [Tooltip("Outer ring is rotated so its cells sit between the inner ones")]
        [SerializeField] private float burstOuterAngleOffset = 22.5f;

        [Header("Feeding")]
        [Tooltip("How many spores a virus must swallow before it fires a new virus")]
        [SerializeField] private int feedsToSplit = 10;
        [SerializeField] private float feedPushFactor = 0.25f;
        [SerializeField] private float maxFeedPush = 3f;
        [SerializeField] private float ejectSpeed = 10f;

        private static readonly List<Virus> registry = new List<Virus>(32);
        public static IReadOnlyList<Virus> All { get { return registry; } }
        public static int ActiveCount { get { return registry.Count; } }

        private Rigidbody2D body;
        private SpriteRenderer spriteRenderer;
        private float storedMass;
        private int feeds;
        private float nextBurstTime;

        public float Mass { get { return mass; } }
        public float Radius { get { return radiusScale * Mathf.Sqrt(Mathf.Max(mass, 0.01f)); } }

        private void Awake()
        {
            body = GetComponent<Rigidbody2D>();
            body.gravityScale = 0f;
            body.freezeRotation = true;
            body.interpolation = RigidbodyInterpolation2D.Interpolate;
            body.linearDamping = 0.9f;
        }

        private void OnEnable()
        {
            if (!registry.Contains(this))
            {
                registry.Add(this);
            }
        }

        private void OnDisable()
        {
            registry.Remove(this);
        }

        public void Configure(float virusMass, float scale)
        {
            mass = Mathf.Max(1f, virusMass);
            radiusScale = Mathf.Max(0.01f, scale);
            ApplyVisual();
        }

        public void ApplyVisual()
        {
            float size = Radius * 2f;
            transform.localScale = new Vector3(size, size, 1f);

            if (spriteRenderer == null)
            {
                spriteRenderer = GetComponent<SpriteRenderer>();
            }
            if (spriteRenderer != null)
            {
                spriteRenderer.sortingOrder = SizeController.SortingOrderForRadius(Radius);
            }
        }

        private void FixedUpdate()
        {
            if (body != null && body.linearVelocity.sqrMagnitude < 0.0025f)
            {
                body.linearVelocity = Vector2.zero;
            }

            if (ArenaManager.Instance != null && body != null)
            {
                ArenaManager.Instance.Constrain(body, Radius);
            }
        }

        private void OnTriggerEnter2D(Collider2D other)
        {
            TryBurst(other);
        }

        private void OnTriggerStay2D(Collider2D other)
        {
            TryBurst(other);
        }

        private void TryBurst(Collider2D other)
        {
            if (Time.time < nextBurstTime)
            {
                return;
            }

            SizeController ball = other.GetComponentInParent<SizeController>();
            if (ball == null || ball.IsDead || ball.IsInvulnerable)
            {
                return;
            }

            if (ball.Mass <= mass)
            {
                return;
            }

            Vector2 offset = (Vector2)transform.position - (Vector2)ball.transform.position;
            if (offset.sqrMagnitude > ball.Radius * ball.Radius)
            {
                return;
            }

            nextBurstTime = Time.time + burstCooldown;
            Burst(ball);
        }

        private void Burst(SizeController ball)
        {
            bool isPlayer = ball.Team == Team.Player;
            PlayerController player = isPlayer ? PlayerController.Active : null;

            int owned = isPlayer && player != null
                ? player.BallCount
                : ScoreBoard.CellsOfBot(ball.Team, ball.BotId);
            int maxBalls = isPlayer && player != null ? player.MaxBallCount : 16;

            int room = Mathf.Max(1, maxBalls - (owned - 1));
            int pieces = Mathf.Clamp(burstPieces, 1, room);

            float total = ball.Mass;
            float pieceMass = total / pieces;
            if (pieceMass <= 1f)
            {
                pieces = 1;
            }

            if (pieces <= 1)
            {
                ball.AddMass(mass);
                Debug.Log($"[virus] ate the virus at the cell limit, +{mass:F0} mass");
                Destroy(gameObject);
                return;
            }

            SpriteRenderer shell = ball.GetComponent<SpriteRenderer>();
            Sprite sprite = shell != null ? shell.sprite : null;
            Color color = shell != null ? shell.color : Color.white;

            ball.SetMass(pieceMass);
            float pieceRadius = ball.Radius;
            Vector2 centre = ball.transform.position;

            List<Vector2> spots = BuildBurstLayout(centre, pieceRadius, pieces - 1);
            List<SizeController> created = new List<SizeController>();

            for (int i = 0; i < spots.Count; i++)
            {
                Vector2 dir = spots[i] - centre;
                dir = dir.sqrMagnitude > 0.0001f ? dir.normalized : Vector2.right;

                SizeController piece = isPlayer && player != null
                    ? player.SpawnBall("Player_Burst", spots[i], pieceMass, sprite, color)
                    : SpawnAiPiece(spots[i], pieceMass, sprite, color, ball.gameObject.name, ball.BotId);

                if (piece == null)
                {
                    continue;
                }

                piece.SetInvulnerable(0.25f);
                LaunchBurstPiece(piece, dir);
                created.Add(piece);
            }

            if (isPlayer && player != null)
            {
                player.MarkBurst(ball, created);
            }

            Debug.Log($"[virus] burst {total:F0} into {pieces} cells, {spots.Count} launched");

            Destroy(gameObject);
        }

        private void LaunchBurstPiece(SizeController piece, Vector2 direction)
        {
            if (piece == null)
            {
                return;
            }

            float speed = burstLaunchSpeed * GamePacing.VirusBurstLaunch;

            PlayerCellMotion motion = piece.GetComponent<PlayerCellMotion>();
            if (motion != null)
            {
                motion.BeginVirusBurst(direction, speed, burstCoastDuration);
                return;
            }

            AIController ai = piece.GetComponent<AIController>();
            if (ai != null)
            {
                ai.BeginBurst(direction * speed, burstCoastDuration);
                return;
            }

            Rigidbody2D pieceBody = piece.GetComponent<Rigidbody2D>();
            if (pieceBody != null)
            {
                pieceBody.linearVelocity = direction * speed;
            }
        }

        private List<Vector2> BuildBurstLayout(Vector2 centre, float pieceRadius, int count)
        {
            List<Vector2> spots = new List<Vector2>(Mathf.Max(0, count));
            if (count <= 0)
            {
                return spots;
            }

            int inner = Mathf.Min(count, Mathf.Max(1, burstInnerCount));
            int outer = count - inner;
            float minGap = pieceRadius * 2f + burstSpawnGap;

            for (int i = 0; i < inner; i++)
            {
                float angle = 360f / Mathf.Max(1, inner) * i;
                float r = pieceRadius * burstInnerRadiusFactor;
                Vector2 offset = Quaternion.Euler(0f, 0f, angle) * (Vector2.right * r);
                spots.Add(ClampInside(centre + offset, pieceRadius));
            }

            for (int i = 0; i < outer; i++)
            {
                float angle = burstOuterAngleOffset + 360f / Mathf.Max(1, outer) * i;
                float r = pieceRadius * burstOuterRadiusFactor;
                Vector2 offset = Quaternion.Euler(0f, 0f, angle) * (Vector2.right * r);
                spots.Add(ClampInside(centre + offset, pieceRadius));
            }

            for (int pass = 0; pass < 3; pass++)
            {
                for (int a = 0; a < spots.Count; a++)
                {
                    for (int b = a + 1; b < spots.Count; b++)
                    {
                        Vector2 delta = spots[b] - spots[a];
                        float distance = delta.magnitude;
                        if (distance >= minGap || distance <= 0.0001f)
                        {
                            continue;
                        }

                        Vector2 push = delta / distance * ((minGap - distance) * 0.5f);
                        spots[a] = ClampInside(spots[a] - push, pieceRadius);
                        spots[b] = ClampInside(spots[b] + push, pieceRadius);
                    }
                }
            }

            return spots;
        }

        private static Vector2 ClampInside(Vector2 point, float radius)
        {
            ArenaManager arena = ArenaManager.Instance;
            if (arena == null)
            {
                return point;
            }

            Vector2 centre = arena.Center;
            Vector2 half = arena.HalfSize;
            return new Vector2(
                Mathf.Clamp(point.x, centre.x - half.x + radius, centre.x + half.x - radius),
                Mathf.Clamp(point.y, centre.y - half.y + radius, centre.y + half.y - radius));
        }

        private static SizeController SpawnAiPiece(Vector3 position, float pieceMass, Sprite sprite, Color color, string ownerName, int botId)
        {
            string pieceName = ownerName.EndsWith("_Burst") ? ownerName : ownerName + "_Burst";
            GameObject go = BallFactory.CreateBall(pieceName, position, pieceMass, sprite, color, Team.Enemy);
            AIController ai = go.AddComponent<AIController>();
            ai.ApplyRandomProfile(Random.Range(0.9f, 1.25f), Random.Range(0.85f, 1.15f));

            SizeController size = go.GetComponent<SizeController>();
            size.BotId = botId;
            return size;
        }

        public void Absorb(Edible spore)
        {
            if (spore == null)
            {
                return;
            }

            Vector2 direction = body != null ? body.linearVelocity.normalized : Vector2.right;
            if (direction.sqrMagnitude < 0.01f)
            {
                direction = Vector2.up;
            }

            storedMass += spore.Mass;
            feeds += 1;

            if (body != null)
            {
                body.linearVelocity = Vector2.ClampMagnitude(
                    body.linearVelocity + direction * (spore.Mass * feedPushFactor), maxFeedPush);
            }

            Debug.Log($"[virus] fed {feeds}/{feedsToSplit}, stored {storedMass:F0}");

            if (feeds >= feedsToSplit)
            {
                FireNewVirus(direction);
            }
        }

        private void FireNewVirus(Vector2 direction)
        {
            feeds = 0;
            storedMass = 0f;

            if (VirusSpawner.Instance == null)
            {
                return;
            }

            Virus spawned = VirusSpawner.Instance.SpawnFrom(this, direction, ejectSpeed);
            if (spawned == null)
            {
                return;
            }

            Debug.Log("[virus] fired a new virus");
        }
    }
}
