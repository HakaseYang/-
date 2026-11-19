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

            if (self.Team == Team.Player && target.Team == Team.Player)
            {
                return;
            }

            self.TryEat(target);
        }
    }
}
