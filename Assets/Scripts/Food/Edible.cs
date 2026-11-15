using UnityEngine;

namespace QiuQiu
{
    /// <summary>
    /// 食物 / 小物件：任何带 SizeController 的球体碰到它，就给它加质量，然后食物销毁。
    ///
    /// 组件要求（缺一不可）：
    ///   1. CircleCollider2D，勾上 Is Trigger      -> 只发生触发，不阻挡球体运动；
    ///   2. Rigidbody2D，Body Type = Kinematic     -> 2D 触发事件需要碰撞双方至少一个有刚体；
    ///                                                用 Kinematic 是为了让食物不被物理力推走。
    /// 为什么把逻辑写在食物自己身上？因为食物是“被吃”的一方，数量最多、逻辑最简单，
    /// 这样新增食物只要复制一个预制体即可，不需要改球体代码。
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(CircleCollider2D))]
    public class Edible : MonoBehaviour
    {
        [Tooltip("被吃掉时给吃它的球增加多少质量。")]
        [SerializeField] private float mass = 1f;

        // 防止同一帧内被两个球（或同一个球的两个碰撞体）各加一次质量
        private bool consumed;

        public float Mass => mass;

        /// <summary>场上存活的食物总数（全局计数，O(1)）。
        /// 用它替代 FoodSpawner 里每 0.5 秒一次的 FindObjectsByType——那是全场景遍历，
        /// 食物几百个时每半秒一次分配数组，手机上是白白浪费的 GC 压力。</summary>
        public static int ActiveCount => activeCount;
        private static int activeCount;

        private void OnEnable() { activeCount++; }
        private void OnDisable() { activeCount--; }

        // 刚被“吐”出来时的保护时间：这段时间内谁都吃不到它，
        // 否则吐球的人会立刻把自己的球又吸回去。
        private float armedAt;

        /// <summary>初始化（供生成器 / 编辑器搭建脚本调用）。armDelay 为“多久之后才能被吃”。</summary>
        public void Initialize(float massValue, float armDelay = 0f)
        {
            mass = Mathf.Max(0.01f, massValue);
            armedAt = Time.time + Mathf.Max(0f, armDelay);
        }

        private void OnTriggerEnter2D(Collider2D other)
        {
            if (consumed) return;
            if (Time.time < armedAt) return;

            // 球体的“实体碰撞体”碰到食物触发体时，这里收到的 other 就是球体本身
            SizeController eater = other.GetComponentInParent<SizeController>();
            if (eater == null || eater.IsDead) return;

            consumed = true;
            eater.AddMass(mass);
            Destroy(gameObject);
        }
    }
}
