using UnityEngine;

namespace QiuQiu
{
    /// <summary>
    /// 运行时球体/食物工厂：分裂、吐球、AI 刷怪都可以用它造对象，避免到处复制粘贴组件装配代码。
    ///
    /// 造出来的球体结构（与编辑器搭建脚本 PrototypeSetup 保持一致）：
    ///   GameObject
    ///     ├─ SpriteRenderer      视觉
    ///     ├─ Rigidbody2D         Dynamic、无重力、冻结旋转
    ///     ├─ CircleCollider2D    实体碰撞（非 Trigger）
    ///     ├─ CircleCollider2D    吞噬感应（Trigger）
    ///     ├─ SizeController      质量 / 大小 / 阵营
    ///     └─ Eater               球吃球判定
    /// </summary>
    public static class BallFactory
    {
        /// <summary>食物（含吐出来的球）在质量 = 1 时的世界直径。</summary>
        public const float FoodDiameterAtMass1 = 0.28f;

        /// <summary>创建一个可以被吞噬、也能吞噬别人的球体。</summary>
        public static GameObject CreateBall(string name, Vector3 position, float mass, Sprite sprite, Color color, int team, int sortingOrder = 10)
        {
            var go = new GameObject(name);
            go.transform.position = position;
            go.transform.localScale = Vector3.one;   // 真实大小由 SizeController 按质量换算

            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = sprite;
            sr.color = color;
            sr.sortingOrder = sortingOrder;

            var rb = go.AddComponent<Rigidbody2D>();
            rb.bodyType = RigidbodyType2D.Dynamic;
            rb.gravityScale = 0f;
            rb.linearDamping = 0f;
            rb.angularDamping = 0f;
            rb.freezeRotation = true;
            rb.interpolation = RigidbodyInterpolation2D.Interpolate;
            rb.collisionDetectionMode = CollisionDetectionMode2D.Continuous;

            // 实体碰撞体 + 吞噬感应体（详见 Eater.cs 的注释）
            var body = go.AddComponent<CircleCollider2D>();
            body.radius = 0.5f;
            body.isTrigger = false;

            var sensor = go.AddComponent<CircleCollider2D>();
            sensor.radius = 0.5f;
            sensor.isTrigger = true;

            var size = go.AddComponent<SizeController>();
            size.Initialize(mass, team);

            go.AddComponent<Eater>();
            return go;
        }

        /// <summary>
        /// 创建一颗静态食物（生成器用）：Trigger 碰撞体 + Kinematic 刚体 + Edible。
        /// 用 Kinematic 是为了食物不会被物理推动，也不会因为球撞上来而滑走。
        /// </summary>
        public static GameObject CreateFood(string name, Vector3 position, float mass, Sprite sprite, Color color, int sortingOrder = 2)
        {
            var go = new GameObject(name);
            go.transform.position = position;
            go.transform.localScale = Vector3.one * FoodDiameter(mass);

            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = sprite;
            sr.color = color;
            sr.sortingOrder = sortingOrder;

            var col = go.AddComponent<CircleCollider2D>();
            col.radius = 0.5f;          // 局部半径，乘上 scale 后才是世界半径
            col.isTrigger = true;

            var rb = go.AddComponent<Rigidbody2D>();
            rb.bodyType = RigidbodyType2D.Kinematic;
            rb.simulated = true;

            var edible = go.AddComponent<Edible>();
            edible.Initialize(mass);

            return go;
        }

        /// <summary>
        /// 创建一个“吐出来的球”：本质是食物（Edible），但带初速度，会飞出去再逐渐减速。
        /// spawnVelocity 直接写进 linearVelocity（Unity 6 已用 linearVelocity 取代 velocity）。
        /// </summary>
        public static GameObject CreateSpitFood(string name, Vector3 position, Vector2 spawnVelocity, float mass, Sprite sprite, Color color, float armDelay, int sortingOrder = 3)
        {
            var go = new GameObject(name);
            go.transform.position = position;
            go.transform.localScale = Vector3.one * FoodDiameter(mass);

            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = sprite;
            sr.color = color;
            sr.sortingOrder = sortingOrder;

            var col = go.AddComponent<CircleCollider2D>();
            col.radius = 0.5f;
            col.isTrigger = true;

            var rb = go.AddComponent<Rigidbody2D>();
            rb.bodyType = RigidbodyType2D.Dynamic;   // Dynamic 才会受 linearDamping 影响自然减速
            rb.gravityScale = 0f;
            rb.linearDamping = 1.8f;
            rb.collisionDetectionMode = CollisionDetectionMode2D.Continuous;
            rb.linearVelocity = spawnVelocity;

            var edible = go.AddComponent<Edible>();
            edible.Initialize(mass, armDelay);

            return go;
        }

        /// <summary>食物直径：质量越大视觉越大，但按 sqrt 增长，避免吐出来的球太夸张。</summary>
        public static float FoodDiameter(float mass)
        {
            return Mathf.Clamp(FoodDiameterAtMass1 * Mathf.Sqrt(Mathf.Max(mass, 0.01f)), 0.18f, 1.2f);
        }
    }
}
