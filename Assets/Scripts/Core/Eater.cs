using UnityEngine;

namespace QiuQiu
{
    /// <summary>
    /// 吞噬判定组件：挂在球体根节点上，负责“球吃球”。
    ///
    /// 碰撞体布局（同一个 GameObject 上两个 CircleCollider2D）：
    ///   - 实体碰撞体：半径 0.5，不勾 Is Trigger  -> 球与球之间会真实碰撞、互相挤开；
    ///   - 吞噬感应体：半径 0.5，勾上 Is Trigger  -> 只用来产生 OnTriggerEnter2D 事件。
    /// 为什么不用实体碰撞体直接做 OnTriggerEnter2D？因为它一旦勾上 Trigger 就失去实体碰撞。
    /// 为什么不给感应体单独建子物体？因为两个 Collider 挂在同一物体上时，缩放会一起跟随球体变大，最省事。
    ///
    /// 判定规则：
    ///   - 对方也带 SizeController（是另一个球）：交给 SizeController.CanEat / TryEat 判断；
    ///   - 对方是食物（带 Edible）：由食物自己处理（食物并不知道会被谁吃，见 Edible.cs）。
    ///
    /// 注意：两个球互相接触时，双方的 Eater 都会收到 OnTriggerEnter2D，
    /// 但只有质量达标的一方 TryEat 成功，另一方 CanEat 为 false 直接返回，因此不会“互相吃掉”。
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(SizeController))]
    public class Eater : MonoBehaviour
    {
        [Tooltip("吞噬成功时是否打印日志（调试用，正式发布可关掉）。")]
        [SerializeField] private bool logEating = true;

        private SizeController self;

        private void Awake()
        {
            self = GetComponent<SizeController>();
        }

        private void OnTriggerEnter2D(Collider2D other)
        {
            // 从碰撞体所在物体往上找 SizeController：
            // 这样就算以后把碰撞体放到子物体上（例如贴图分层），判定依然有效。
            SizeController target = other.GetComponentInParent<SizeController>();
            if (target == null || target == self) return;

            // 同阵营不互吃：玩家分裂出的子球不能被主球吃掉，敌人之间也不会互相吞并
            if (target.Team == self.Team) return;

            float myMassBefore = self.Mass;
            float targetMass = target.Mass;

            if (!self.TryEat(target)) return;   // 质量不够 20% 优势，不做任何事

            if (logEating)
            {
                Debug.Log($"[吞噬] {name}({myMassBefore:F0}) 吃掉了 {target.name}({targetMass:F0}) → 现在 {self.Mass:F0}");
            }
        }
    }
}
