using UnityEngine;

namespace QiuQiu
{

    [DisallowMultipleComponent]
    [RequireComponent(typeof(CircleCollider2D))]
    public class Edible : MonoBehaviour
    {
        [SerializeField] private float mass = 1f;

        private bool consumed;    //  one frame > only one
        private float armedAt;    //  avoid  own spits the ball then get back at once
        private bool poolable;    //  set for  FoodPool to recycle 
        private bool spore;       //  true for mass a ball spat out (can feed a virus)
        private float payback;    //  mass the spitter gets back when it eats its own pellet

        // who spat this pellet out, and when it becomes edible for that same group again
        private bool hasOwner;
        private Team ownerTeam;
        private int ownerId;
        private float ownerFreeAt;

        private static int activeCount; //allCount(now)


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

        // the sprite is one unit wide, so localScale.x is the world diameter
        public float Radius
        {
            get { return transform.localScale.x * 0.5f; }
        }

        private void OnEnable()
        {
            activeCount++;
        }

        // A pellet that reaches the wall stops there instead of flying out of the arena.
        // Only spores move, so the check is skipped for the static food.
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
                body.linearVelocity = Vector2.zero;   // it rests against the wall
            }
        }

        private void OnDisable() 
        {
            activeCount--; 
        }

        // armDelay：wait time > armedAt
        public void Initialize(float massValue, float armDelay = 0f)
        {
            mass = Mathf.Max(0.01f, massValue);
            armedAt = Time.time + Mathf.Max(0f, armDelay);
        }

        // clear the state for set active again
        public void ResetForReuse(float massValue, float armDelay)
        {
            consumed = false;
            spore = false;      // a recycled pellet is plain food again
            hasOwner = false;   // and it belongs to nobody until it is spat out again
            Initialize(massValue, armDelay);

            // a pooled spore keeps its Rigidbody2D, so the leftover speed has to go
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

        // mass a ball spat out: it can still be eaten by anyone, but it can also feed a virus
        public void MarkSpore()
        {
            spore = true;
            payback = 0f;
        }

        // How much the spitter itself gets back if it swallows this pellet again. It is always
        // less than what it paid, otherwise spitting and eating would print free weight. Everyone
        // else still eats the full mass, so a pellet is never a way to hide mass from the world.
        public void SetPayback(float value)
        {
            payback = Mathf.Max(0f, value);
        }

        // Remember who spat it out. A pooled pellet is handed out again and again, so this
        // has to be overwritten every time it leaves the pool.
        // graceSeconds keeps the pellet out of the owner's mouth while it is still leaving the
        // body; after that the owner can eat its own mass back, the way Agar lets you.
        public void SetOwner(Team team, int groupId, float graceSeconds)
        {
            ownerTeam = team;
            ownerId = groupId;
            ownerFreeAt = Time.time + Mathf.Max(0f, graceSeconds);
            hasOwner = true;
        }

        // Same group, and still inside the grace window: not edible yet. After the window the
        // pellet is ordinary food again, otherwise spit mass could never be re-absorbed.
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

        // A pellet can already be inside a body while its arm delay is still running. After
        // the delay runs out nothing would fire again if only Enter were wired up.
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

            // Only mass spat out by a ball feeds a virus. A normal pellet flies straight through.
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

            // your own pellet is untouchable only while it is still leaving your body
            if (IsSameOwnerBlocked(eater))
            {
                return;
            }

            consumed = true;

            // Spat mass pays back exactly what the pellet says. Everything else goes through the
            // difficulty table, which would otherwise turn a 6 mass pellet into a 9 mass meal on
            // the easy preset and let players farm their own spit.
            if (spore)
            {
                // The spitter only gets its payback back, outsiders get the whole pellet. That
                // keeps the pellet looking big while making spit-and-swallow a net loss.
                float gain = IsOwnedBy(eater) && payback > 0f ? payback : mass;
                eater.AddMass(gain);
            }
            else
            {
                eater.AddMassFromEat(mass);
            }

            // only the player's own eating is audible
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
                SporePool.Give(this);   // spat mass has its own pool
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
