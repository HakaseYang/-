using UnityEngine;

namespace QiuQiu
{
    // Keeps the arena stocked: every checkInterval seconds it tops the food up to maxFoodCount.
    // Topping up (instead of spawning on a timer) keeps the object count flat on mobile,
    // and maxSpawnsPerTick caps how much work a single frame can do.
    public class FoodSpawner : MonoBehaviour
    {
        [Header("Area")]
        [SerializeField] private Vector2 areaCenter = Vector2.zero;
        [SerializeField] private Vector2 areaSize = new Vector2(44f, 30f);
        [SerializeField] private bool useArenaBounds = true;
        [SerializeField] private float arenaEdgePadding = 0.4f;

        [Header("Count")]
        [SerializeField] private int maxFoodCount = 500;
        [SerializeField] private bool fillOnStart = true;
        [SerializeField] private float checkInterval = 0.5f;
        [SerializeField] private int maxSpawnsPerTick = 40;

        [Header("Food")]
        [SerializeField] private Sprite foodSprite;
        [SerializeField] private float minFoodMass = 1f;
        [SerializeField] private float maxFoodMass = 2.5f;
        [SerializeField]
        private Color[] palette =
        {
            new Color(0.55f, 0.95f, 0.55f),
            new Color(1.00f, 0.80f, 0.35f),
            new Color(1.00f, 0.55f, 0.70f),
            new Color(0.60f, 0.85f, 1.00f),
            new Color(0.85f, 0.70f, 1.00f),
        };

        [Header("Avoid")]
        [SerializeField] private float minDistanceFromPlayer = 3f;
        [SerializeField] private int placementAttempts = 8;
        [SerializeField] private Transform foodParent;

        private float nextCheckTime;
        private PlayerController player;

        private void Start()
        {
            player = PlayerController.Active != null ? PlayerController.Active : FindFirstObjectByType<PlayerController>();

            if (foodSprite == null && player != null)
            {
                // no sprite assigned: borrow the ball sprite so food is at least visible
                SpriteRenderer sr = player.GetComponent<SpriteRenderer>();
                if (sr != null) foodSprite = sr.sprite;
            }

            nextCheckTime = Time.time + checkInterval;

            // shells up front: after this, a bite recycles an object instead of allocating one
            FoodPool.Prewarm(maxFoodCount);

            if (!fillOnStart) return;

            // one shot fill, guarded so a missing sprite cannot spin here forever
            int guard = 0;
            while (Edible.ActiveCount < maxFoodCount && guard++ < maxFoodCount)
            {
                if (!SpawnOne()) break;
            }
        }

        private void Update()
        {
            if (Time.time < nextCheckTime) return;
            nextCheckTime = Time.time + Mathf.Max(0.05f, checkInterval);

            // Edible.ActiveCount is O(1); the old FindObjectsByType allocated an array every check.
            // Spit food is Edible too, so it counts as food here - that is intended.
            int missing = maxFoodCount - Edible.ActiveCount;
            if (missing <= 0) return;

            int budget = Mathf.Min(missing, Mathf.Max(1, maxSpawnsPerTick));
            for (int i = 0; i < budget; i++)
            {
                if (!SpawnOne()) break;
            }
        }

        private bool SpawnOne()
        {
            if (foodSprite == null) return false;

            for (int attempt = 0; attempt < Mathf.Max(1, placementAttempts); attempt++)
            {
                Vector3 point = PickPoint();
                if (IsTooCloseToPlayer(point)) continue;

                float mass = Random.Range(minFoodMass, maxFoodMass);
                Color color = palette != null && palette.Length > 0
                    ? palette[Random.Range(0, palette.Length)]
                    : Color.green;

                GameObject go = BallFactory.CreateFood(point, mass, foodSprite, color);
                if (foodParent != null) go.transform.SetParent(foodParent, true);
                return true;
            }

            return false;
        }

        // ArenaManager.GetRandomPointInside already keeps the point inside the arena,
        // so a second inside-check here would only re-test the same thing.
        private Vector3 PickPoint()
        {
            if (useArenaBounds && ArenaManager.Instance != null)
            {
                Vector2 point = ArenaManager.Instance.GetRandomPointInside(arenaEdgePadding + 0.6f);
                return new Vector3(point.x, point.y, 0f);
            }

            Vector2 local = areaCenter + new Vector2(
                Random.Range(-areaSize.x * 0.5f, areaSize.x * 0.5f),
                Random.Range(-areaSize.y * 0.5f, areaSize.y * 0.5f));

            return transform.position + new Vector3(local.x, local.y, 0f);
        }

        private bool IsTooCloseToPlayer(Vector3 point)
        {
            // the controller survives a death and re-adopts the balls, but the field can go stale
            if (player == null) player = PlayerController.Active;
            if (player == null || minDistanceFromPlayer <= 0f) return false;

            var balls = player.Balls;
            for (int i = 0; i < balls.Count; i++)
            {
                SizeController ball = balls[i];
                if (ball == null || ball.IsDead) continue;

                if (Vector2.Distance(ball.transform.position, point) < minDistanceFromPlayer + ball.Radius) return true;
            }

            return false;
        }

        private void OnDrawGizmosSelected()
        {
            Gizmos.color = new Color(0.3f, 1f, 0.5f, 0.8f);
            Vector3 center = transform.position + new Vector3(areaCenter.x, areaCenter.y, 0f);
            Gizmos.DrawWireCube(center, new Vector3(areaSize.x, areaSize.y, 0.1f));
        }
    }
}
