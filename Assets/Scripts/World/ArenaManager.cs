using UnityEngine;

namespace QiuQiu
{
    public class ArenaManager : MonoBehaviour
    {
        public static ArenaManager Instance { get; private set; }

        [Header("Shape")]
        [SerializeField] private Vector2 rectangleSize = new Vector2(60f, 60f);

        [Header("Border Behaviour")]
        [SerializeField] private bool clampPosition = true;
        [SerializeField] private float pushBackStrength = 4f;
        [SerializeField] private float maxPushBackSpeed = 8f;

        [Header("Border Visual")]
        [SerializeField] private bool drawBorder = true;
        [SerializeField] private Color borderColor = new Color(0.45f, 0.75f, 1f, 0.65f);
        [SerializeField] private float borderWidth = 0.12f;

        private ArenaBorder border;

        // Arena center is the object's own position; nothing else moves it.
        public Vector2 Center { get { return (Vector2)transform.position; } }

        public Vector2 HalfSize { get { return rectangleSize * 0.5f; } }

        private void Awake()
        {
            Instance = this;
            if (drawBorder)
            {
                CreateBorder();
            }
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        // ball fully inside the arena?
        public bool IsInside(Vector2 position, float radius = 0f)
        {
            Vector2 center = Center;
            Vector2 half = HalfSize;

            return position.x >= center.x - half.x + radius &&
                   position.x <= center.x + half.x - radius &&
                   position.y >= center.y - half.y + radius &&
                   position.y <= center.y + half.y - radius;
        }

        private Vector2 ClampPosition(Vector2 position, float radius)
        {
            Vector2 center = Center;
            Vector2 half = HalfSize;

            float minX = center.x - half.x + radius;
            float maxX = center.x + half.x - radius;
            float minY = center.y - half.y + radius;
            float maxY = center.y + half.y - radius;

            // Arena smaller than the ball: pin it to the center.
            if (minX > maxX) 
            {
                minX = maxX = center.x; 
            }
            if (minY > maxY)
            { 
                minY = maxY = center.y; 
            }

            return new Vector2(Mathf.Clamp(position.x, minX, maxX), Mathf.Clamp(position.y, minY, maxY));
        }

        // for Spaen AI
        public Vector2 GetRandomPointInside(float margin = 0f)
        {
            Vector2 center = Center;
            Vector2 half = HalfSize;

            float innerX = Mathf.Max(0f, half.x - margin);
            float innerY = Mathf.Max(0f, half.y - margin);

            float x = (Random.value * 2f - 1f) * innerX;
            float y = (Random.value * 2f - 1f) * innerY;

            return center + new Vector2(x, y);
        }

        // Called from each ball's FixedUpdate. Returns true when it had to correct something.
        public void Constrain(Rigidbody2D body, float radius)
        {
            if (body == null)
            {
                return ;
            }

            Vector2 position = body.position;
            Vector2 clamped = ClampPosition(position, radius);

            //only Compare distance between two points(avoid the square root (sqrt) operation)
            //1e-6 =0.001
            //the ball is less than 1 millimeter off its legal position = not gone out
            //to avoid after do
            if ((clamped - position).sqrMagnitude < 1e-6f)
            {
                return ;
            }
            //get direction in area(go back)
            Vector2 inward = (clamped - position).normalized;
            float overflow = Vector2.Distance(position, clamped);

            if (clampPosition)
            {
                body.position = clamped;
            }

            Vector2 velocity = body.linearVelocity;

            // Remove the outward part(speed) so the ball slides along the edge.
            // Vector2.Dot get Two vectors point to(direction)
            //Vector2.Dot > get +x =ball go out area; -x go in area direction
            float outwardSpeed = Vector2.Dot(velocity, -inward);
            //if go out
            if (outwardSpeed > 0f)
            {
                // to discharge velocity go out area(need add back go in speed )
                velocity += inward * outwardSpeed;
            }

            // Add a small inward push, capped, so it leaves the edge .
            if (pushBackStrength > 0f)
            {
                float push = Mathf.Min(pushBackStrength * overflow, maxPushBackSpeed);
                float currentInward = Vector2.Dot(velocity, inward);

                // if ball  is already moving inward faster than the push.then don;t push it
                if (currentInward < push)
                {
                    velocity += inward * (push - currentInward);
                }
            }

            body.linearVelocity = velocity;
        }

        private void CreateBorder()
        {
            border = gameObject.AddComponent<ArenaBorder>();
            RefreshBorder();
        }

        private void RefreshBorder()
        {
            if (border == null)
            {
                return;
            }
            border.Refresh(borderColor, borderWidth, Center, HalfSize);
        }

        // Editor preview; the runtime line is drawn by ArenaBorder.(just for test)
        private void OnDrawGizmos()
        {
            Vector2 center = Center;
            Vector2 half = HalfSize;

            Gizmos.color = new Color(0.4f, 0.9f, 1f, 0.85f);
            Gizmos.DrawWireCube(center, new Vector3(half.x * 2f, half.y * 2f, 0.1f));
        }
    }
}
