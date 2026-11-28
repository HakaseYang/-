using UnityEngine;

namespace QiuQiu
{
    public static class BallFactory
    {
        private const float FoodDiameterAtMass1 = 0.28f;
        private const float DefaultLinearDamping = 0f;

        private const int BallOrder = 10;
        private const int FoodOrder = 2;
        private const int SpitOrder = 3;

        public static GameObject CreateBall(string name, Vector3 position, float mass, Sprite sprite, Color color, Team team)
        {
            GameObject go = NewVisual(name, position, sprite, color, BallOrder);
            go.transform.localScale = Vector3.one;

            Rigidbody2D rb = go.AddComponent<Rigidbody2D>();
            rb.bodyType = RigidbodyType2D.Dynamic;
            rb.gravityScale = 0f;
            rb.freezeRotation = true;
            rb.interpolation = RigidbodyInterpolation2D.Interpolate;
            rb.linearDamping = DefaultLinearDamping;

            CircleCollider2D sensor = go.AddComponent<CircleCollider2D>();
            sensor.radius = 0.5f;
            sensor.isTrigger = true;

            SizeController size = go.AddComponent<SizeController>();
            size.Initialize(mass, team);

            go.AddComponent<Eater>();
            go.AddComponent<BallName>();
            return go;
        }

        public static GameObject CreateFood(Vector3 position, float mass, Sprite sprite, Color color, Transform parent)
        {

            Edible edible = FoodPool.Take();
            GameObject go = edible.gameObject;

            go.transform.SetParent(parent, true);
            go.transform.position = position;
            go.transform.localScale = Vector3.one * FoodDiameter(mass);

            SpriteRenderer sr = go.GetComponent<SpriteRenderer>();
            sr.sprite = sprite;
            sr.color = color;
            sr.sortingOrder = FoodOrder;

            edible.ResetForReuse(mass, 0f);
            go.SetActive(true);
            return go;
        }

        public static GameObject CreateFoodShell()
        {
            GameObject go = NewVisual("Food", Vector3.zero, null, Color.white, FoodOrder);
            go.SetActive(false);

            CircleCollider2D col = go.AddComponent<CircleCollider2D>();
            col.radius = 0.5f;
            col.isTrigger = true;

            Edible edible = go.AddComponent<Edible>();
            edible.MarkPoolable();
            return go;
        }

        public static Edible CreateSpitFood(string name, Vector3 position, Vector2 velocity, float mass, float payback, Sprite sprite, Color color, float armDelay, SizeController owner, float ownerGrace)
        {
            Edible edible = SporePool.Take();
            GameObject go = edible.gameObject;

            go.name = name;
            go.transform.SetParent(null, true);
            go.transform.position = position;
            go.transform.localScale = Vector3.one * FoodDiameter(mass);

            SpriteRenderer sr = go.GetComponent<SpriteRenderer>();
            sr.sprite = sprite;
            sr.color = color;
            sr.sortingOrder = SpitOrder;

            edible.ResetForReuse(mass, armDelay);

            Rigidbody2D rb = go.GetComponent<Rigidbody2D>();
            if (rb != null)
            {
                rb.bodyType = RigidbodyType2D.Dynamic;
            }

            edible.MarkSpore();

            if (owner != null)
            {
                edible.SetOwner(owner.Team, owner.OwnerId, ownerGrace);
                edible.SetPayback(payback);
            }

            go.SetActive(true);

            if (rb != null)
            {
                rb.linearVelocity = velocity;
            }

            return edible;
        }

        public static float SporeRadius(float mass)
        {
            return FoodDiameter(mass) * 0.5f;
        }

        public static GameObject CreateSporeShell()
        {
            GameObject go = NewVisual("SpitFood", Vector3.zero, null, Color.white, SpitOrder);

            Rigidbody2D rb = go.AddComponent<Rigidbody2D>();
            rb.bodyType = RigidbodyType2D.Dynamic;
            rb.gravityScale = 0f;
            rb.freezeRotation = true;
            rb.interpolation = RigidbodyInterpolation2D.Interpolate;
            rb.linearDamping = 1.8f;

            CircleCollider2D col = go.AddComponent<CircleCollider2D>();
            col.radius = 0.5f;
            col.isTrigger = true;

            Edible edible = go.AddComponent<Edible>();
            edible.MarkPoolable();

            go.SetActive(false);
            return go;
        }

        private static GameObject CreateFoodBase(string name, Vector3 position, float mass, Sprite sprite, Color color, int order, float armDelay)
        {
            GameObject go = NewVisual(name, position, sprite, color, order);
            go.transform.localScale = Vector3.one * FoodDiameter(mass);

            CircleCollider2D col = go.AddComponent<CircleCollider2D>();
            col.radius = 0.5f;
            col.isTrigger = true;

            Edible edible = go.AddComponent<Edible>();
            edible.Initialize(mass, armDelay);
            return go;
        }

        private static GameObject NewVisual(string name, Vector3 position, Sprite sprite, Color color, int order)
        {
            GameObject go = new GameObject(name);
            go.transform.position = position;

            SpriteRenderer sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = sprite;
            sr.color = color;
            sr.sortingOrder = order;
            return go;
        }


        private static float FoodDiameter(float mass)
        {
            return Mathf.Clamp(FoodDiameterAtMass1 * Mathf.Sqrt(Mathf.Max(mass, 0.01f)), 0.18f, 1.2f);
        }
    }
}
