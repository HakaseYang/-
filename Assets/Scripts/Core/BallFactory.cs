using UnityEngine;

namespace QiuQiu
{
    // ball   = Dynamic body + solid collider + trigger sensor + SizeController + Eater
    // food   = Kinematic body + trigger only + Edible
    public static class BallFactory
    {
        private const float FoodDiameterAtMass1 = 0.28f;
        // No drag: a ball coasts until something stops it, the player has to push back.
        private const float DefaultLinearDamping = 0f;

        private const int BallOrder = 10;
        private const int FoodOrder = 2;
        private const int SpitOrder = 3;

        public static GameObject CreateBall(string name, Vector3 position, float mass, Sprite sprite, Color color, Team team)
        {
            GameObject go = NewVisual(name, position, sprite, color, BallOrder);
            go.transform.localScale = Vector3.one;   // real size is set by SizeController from mass

            Rigidbody2D rb = go.AddComponent<Rigidbody2D>();
            rb.bodyType = RigidbodyType2D.Dynamic;
            rb.gravityScale = 0f;
            rb.freezeRotation = true;
            rb.interpolation = RigidbodyInterpolation2D.Interpolate;
            // damping eats leftover speed from splits and knockback, so balls don't drift forever
            rb.linearDamping = DefaultLinearDamping;

            // Only a trigger sensor: Agar.io cells overlap and slide through each other,
            // they are not billiard balls. Eating is decided by Eater (how deep the prey is
            // covered), not by the physics engine pushing the two bodies apart.
            CircleCollider2D sensor = go.AddComponent<CircleCollider2D>();
            sensor.radius = 0.5f;
            sensor.isTrigger = true;

            SizeController size = go.AddComponent<SizeController>();
            size.Initialize(mass, team);

            go.AddComponent<Eater>();
            go.AddComponent<BallName>();   // the name printed inside the cell, like agar.io
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
            // spores are pooled: holding the spit button must not allocate a new object per pellet
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

            // reset first: it clears the leftover speed of the last pellet this object was, so
            // setting the velocity before it would be wiped out and the pellet would sit still
            edible.ResetForReuse(mass, armDelay);

            Rigidbody2D rb = go.GetComponent<Rigidbody2D>();
            if (rb != null)
            {
                rb.bodyType = RigidbodyType2D.Dynamic;
            }

            // A spore is still food, but it is also the thing that feeds and pushes viruses.
            edible.MarkSpore();

            // The pool hands out old pellets, so the previous owner has to be overwritten or a
            // recycled pellet would still refuse to be eaten by its new owner.
            if (owner != null)
            {
                edible.SetOwner(owner.Team, owner.OwnerId, ownerGrace);
                edible.SetPayback(payback);
            }

            // Activate first, then give the speed. A Rigidbody2D that is switched on wakes up with
            // no velocity, so setting it before this line would be silently thrown away and the
            // pellet would stand still where it was spat.
            go.SetActive(true);

            if (rb != null)
            {
                rb.linearVelocity = velocity;
            }

            return edible;
        }

        // world radius of a pellet with this mass, so the caller can spawn it outside the body
        public static float SporeRadius(float mass)
        {
            return FoodDiameter(mass) * 0.5f;
        }

        // shell used by SporePool: it keeps its Rigidbody2D because a spore always flies
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
        {   //food size 0.18f -1.2f
            return Mathf.Clamp(FoodDiameterAtMass1 * Mathf.Sqrt(Mathf.Max(mass, 0.01f)), 0.18f, 1.2f);
        }
    }
}
