using UnityEngine;

namespace QiuQiu
{

    [DisallowMultipleComponent]
    [RequireComponent(typeof(CircleCollider2D))]
    public class Edible : MonoBehaviour
    {
        [SerializeField] private float mass = 1f;

        private bool consumed;
        private float armedAt;
        private bool poolable;
        private bool spore;
        private float payback;

        private bool hasOwner;
        private Team ownerTeam;
        private int ownerId;
        private float ownerFreeAt;

        private static int activeCount;


        public static int ActiveCount
        {
            get { return activeCount; }
        }

        public float Mass
        {
            get { return mass; }
        }

        public bool IsSpore
        {
            get { return spore; }
        }

        public float Radius
        {
            get { return transform.localScale.x * 0.5f; }
        }

        private void OnEnable()
        {
            activeCount++;
        }

        private void Update()
        {
            if (!spore)
            {
                return;
            }

            ArenaManager arena = ArenaManager.Instance;
            if (arena == null)
            {
                return;
            }

            Vector2 half = arena.HalfSize;
            Vector2 center = arena.Center;
            float radius = Radius;

            Vector2 p = transform.position;
            Vector2 clamped = new Vector2(
                Mathf.Clamp(p.x, center.x - half.x + radius, center.x + half.x - radius),
                Mathf.Clamp(p.y, center.y - half.y + radius, center.y + half.y - radius));

            if ((clamped - p).sqrMagnitude <= 0.000001f)
            {
                return;
            }

            transform.position = clamped;

            Rigidbody2D body = GetComponent<Rigidbody2D>();
            if (body != null)
            {
                body.linearVelocity = Vector2.zero;
            }
        }

        private void OnDisable()
        {
            activeCount--;
        }

        public void Initialize(float massValue, float armDelay = 0f)
        {
            mass = Mathf.Max(0.01f, massValue);
            armedAt = Time.time + Mathf.Max(0f, armDelay);
        }

        public void ResetForReuse(float massValue, float armDelay)
        {
            consumed = false;
            spore = false;
            hasOwner = false;
            Initialize(massValue, armDelay);

            Rigidbody2D body = GetComponent<Rigidbody2D>();
            if (body != null)
            {
                body.linearVelocity = Vector2.zero;
                body.angularVelocity = 0f;
            }

            Collider2D col = GetComponent<Collider2D>();
            if (col != null)
            {
                col.enabled = true;
            }
        }

        public void MarkPoolable()
        {
            poolable = true;
        }

        public void MarkSpore()
        {
            spore = true;
            payback = 0f;
        }

        public void SetPayback(float value)
        {
            payback = Mathf.Max(0f, value);
        }

        public void SetOwner(Team team, int groupId, float graceSeconds)
        {
            ownerTeam = team;
            ownerId = groupId;
            ownerFreeAt = Time.time + Mathf.Max(0f, graceSeconds);
            hasOwner = true;
        }

        private bool IsSameOwnerBlocked(SizeController eater)
        {
            return IsOwnedBy(eater) && Time.time < ownerFreeAt;
        }

        private bool IsOwnedBy(SizeController eater)
        {
            return hasOwner
                && eater != null
                && eater.Team == ownerTeam
                && eater.OwnerId == ownerId;
        }

        private void OnTriggerEnter2D(Collider2D other)
        {
            TryConsume(other);
        }

        private void OnTriggerStay2D(Collider2D other)
        {
            TryConsume(other);
        }

        private void TryConsume(Collider2D other)
        {
            if (consumed)
            {
                return;
            }
            if (other == null)
            {
                return;
            }
            if (Time.time < armedAt)
            {
                return;
            }

            if (spore)
            {
                Virus virus = other.GetComponentInParent<Virus>();
                if (virus != null)
                {
                    consumed = true;
                    virus.Absorb(this);
                    Release();
                    return;
                }
            }

            SizeController eater = other.GetComponentInParent<SizeController>();
            if (eater == null || eater.IsDead)
            {
                return;
            }

            if (IsSameOwnerBlocked(eater))
            {
                return;
            }

            consumed = true;

            if (spore)
            {
                float gain = IsOwnedBy(eater) && payback > 0f ? payback : mass;
                eater.AddMass(gain);
            }
            else
            {
                eater.AddMassFromEat(mass);
            }

            if (eater.Team == Team.Player && AudioManager.Instance != null)
            {
                AudioManager.Instance.PlayEatFood();
            }

            Release();
        }

        private void Release()
        {
            if (spore)
            {
                SporePool.Give(this);
                return;
            }

            if (poolable)
            {
                FoodPool.Give(this);
            }
            else
            {
                Destroy(gameObject);
            }
        }
    }
}
