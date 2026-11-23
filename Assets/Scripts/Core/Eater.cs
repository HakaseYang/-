using UnityEngine;

namespace QiuQiu
{
    // SizeController.TryEat decide who eat who.
    // This component only decides WHEN the bite happens: a small overlap is not enough,
    // the prey has to be covered by the bigger ball first (Agar.io style).
    [DisallowMultipleComponent]
    [RequireComponent(typeof(SizeController))]
    public class Eater : MonoBehaviour
    {
        // How much of the prey has to be inside the eater before it is swallowed:
        //   0.5 = the prey sits on the eater's edge, 0.97 = it is almost completely covered.
        // Kept near 1 so a big ball really has to drive over the small one, not just touch it.
        [SerializeField, Range(0.5f, 1f)] private float swallowCoverage = 0.97f;

        // GameManager owns the number the player actually tunes; every Eater (including the ones
        // spawned later by splits and virus bursts) reads it here so they all agree.
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

        // Enter only fires once, but the overlap keeps growing while the balls sit on each
        // other, so the bite has to be re-checked every physics step.
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

            if (self.Team == Team.Player && target.Team == Team.Player)
            {
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

            // only the player hears its own bite, ten AI eating must stay silent
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

            // How far the prey's covered part spans: c = (R - d + r) / 2r, so solving for the
            // centre distance gives d = R + r - 2cr. coverage 0.5 -> d = R (half covered),
            // coverage 1 -> d = R - r (fully swallowed).
            float allowed = self.Radius + target.Radius - 2f * swallowCoverage * target.Radius;
            if (allowed <= 0f)
            {
                return false;
            }

            Vector2 offset = (Vector2)target.transform.position - (Vector2)transform.position;
            return offset.sqrMagnitude <= allowed * allowed;
        }
    }
}
