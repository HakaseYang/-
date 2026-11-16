using UnityEngine;

namespace QiuQiu
{
    // SizeController.TryEat decide who eat who
    [DisallowMultipleComponent]
    [RequireComponent(typeof(SizeController))]
    public class Eater : MonoBehaviour
    {
        private SizeController self;

        private void Awake()
        {
            self = GetComponent<SizeController>();
        }

        private void OnTriggerEnter2D(Collider2D other)
        {
            
            SizeController target = other.GetComponentInParent<SizeController>();
            if (target == null || target == self)
            {
                return;
            }

            // 玩家自己的球互不吃（AI 之间、AI 和玩家之间都能吃）
            if (self.Team == Team.Player && target.Team == Team.Player)
            {
                return;
            }

            self.TryEat(target);
        }
    }
}
