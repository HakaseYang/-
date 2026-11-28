using UnityEngine;

namespace QiuQiu
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(SizeController))]
    public class Eater : MonoBehaviour
    {
        [SerializeField, Range(0.5f, 1f)] private float swallowCoverage = 0.97f;

        public static float DefaultSwallowCoverage = 0.97f;

        public float SwallowCoverage
        {
            get { return swallowCoverage; }
            set { swallowCoverage = Mathf.Clamp(value, 0.5f, 1f); }
        }

        private SizeController self;

        private void Awake()
        {
            self = GetComponent<SizeController>();
            swallowCoverage = Mathf.Clamp(DefaultSwallowCoverage, 0.5f, 1f);
        }

        private void OnTriggerEnter2D(Collider2D other)
        {
            TrySwallow(other);
        }

        private void OnTriggerStay2D(Collider2D other)
        {
            TrySwallow(other);
        }

        private void TrySwallow(Collider2D other)
        {
            if (other == null)
            {
                return;
            }

            SizeController target = other.GetComponentInParent<SizeController>();
            if (target == null || target == self)
            {
                return;
            }

            if (self.IsDead || target.IsDead)
            {
                return;
            }

            bool ownCell = self.Team == Team.Player && target.Team == Team.Player;
            if (ownCell)
            {
                if (target.IsInvulnerable || target.IsEjecting)
                {
                    return;
                }

                if (!Within(swallowCoverage, target))
                {
                    return;
                }

                self.TryEatOwn(target);
                return;
            }

            if (!IsCovered(target))
            {
                return;
            }

            if (!self.TryEat(target))
            {
                return;
            }

            if (self.Team == Team.Player && AudioManager.Instance != null)
            {
                AudioManager.Instance.PlayEatEnemy();
            }
        }

        private bool IsCovered(SizeController target)
        {
            if (!self.CanEat(target))
            {
                return false;
            }

            return Within(swallowCoverage, target);
        }

        private bool Within(float coverage, SizeController target)
        {
            float allowed = self.Radius + target.Radius - 2f * coverage * target.Radius;
            if (allowed <= 0f)
            {
                return false;
            }

            Vector2 offset = (Vector2)target.transform.position - (Vector2)transform.position;
            return offset.sqrMagnitude <= allowed * allowed;
        }
    }
}
