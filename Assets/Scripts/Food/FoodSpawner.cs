using UnityEngine;

namespace QiuQiu
{
    public class FoodSpawner : MonoBehaviour
    {
        [Header("Area")]
        [SerializeField] private float borderMargin = 1f;
        [Header("Count")]
        [SerializeField] private int maxFoodCount = 500;
        [SerializeField] private int mobileFoodCap = 400;
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
            if (Application.isMobilePlatform && mobileFoodCap > 0 && maxFoodCount > mobileFoodCap)
            {
                maxFoodCount = mobileFoodCap;
            }

            player = PlayerController.Active != null ? PlayerController.Active : FindFirstObjectByType<PlayerController>();

            if (foodSprite == null && player != null)
            {
                SpriteRenderer sr = player.GetComponent<SpriteRenderer>();
                if (sr != null)
                {
                    foodSprite = sr.sprite;
                }
            }

            nextCheckTime = Time.time + checkInterval;


            if (foodParent != null && !foodParent.gameObject.activeInHierarchy)
            {
                Debug.LogWarning("[FoodSpawner] foodParent is inactive, food will be invisible");
            }

            FoodPool.UseRoot(foodParent);
            FoodPool.Prewarm(maxFoodCount);

            if (!fillOnStart)
            {
                return;
            }
            for (int i = 0; i < maxFoodCount; i++)
            {
                if (Edible.ActiveCount >= maxFoodCount)
                {
                    break;
                }

                if (!SpawnOne())
                {
                    break;
                }
            }
        }

        private void Update()
        {
            if (Time.time < nextCheckTime)
            {
                return;
            }

            nextCheckTime = Time.time + checkInterval;

            int missing = maxFoodCount - Edible.ActiveCount;
            if (missing <= 0)
            {
                return;
            }

            int budget = Mathf.Min(missing, maxSpawnsPerTick);
            for (int i = 0; i < budget; i++)
            {
                if (!SpawnOne()) break;
            }
        }

        private bool SpawnOne()
        {
            Sprite shape = FoodShapes.Pick();

            if (shape == null && foodSprite == null)
            {
                return false;
            }

            for (int attempt = 0; attempt < placementAttempts; attempt++)
            {
                if (!TryPickPoint(out Vector3 point))
                {
                    return false;
                }

                if (IsTooCloseToPlayer(point))
                {
                    continue;
                }

                float mass = Random.Range(minFoodMass, maxFoodMass);
                Color color = palette != null && palette.Length > 0
                    ? palette[Random.Range(0, palette.Length)]
                    : Color.green;

                BallFactory.CreateFood(point, mass, shape != null ? shape : foodSprite, color, foodParent);
                return true;
            }

            return false;
        }

        private bool TryPickPoint(out Vector3 point)
        {
            point = Vector3.zero;


            if (ArenaManager.Instance == null)
            {
                return false;
            }

            Vector2 inside = ArenaManager.Instance.GetRandomPointInside(borderMargin);
            point = new Vector3(inside.x, inside.y, 0f);
            return true;
        }

        private bool IsTooCloseToPlayer(Vector3 point)
        {
            if (player == null)
            {
                player = PlayerController.Active;
            }
            if (player == null || minDistanceFromPlayer <= 0f)
            {
                return false;
            }

            var balls = player.Balls;
            for (int i = 0; i < balls.Count; i++)
            {
                SizeController ball = balls[i];
                if (ball == null || ball.IsDead)
                {
                    continue;
                }

                if (Vector2.Distance(ball.transform.position, point) < minDistanceFromPlayer + ball.Radius)
                {
                    return true;
                }
            }

            return false;
        }
    }
}
