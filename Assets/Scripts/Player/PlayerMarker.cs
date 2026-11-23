using UnityEngine;

namespace QiuQiu
{
    // The arrow that floats just outside every ball the player owns.
    // There is no art for it in the project, so the triangle is built from code once and
    // reused. It is NOT parented to the ball: the ball scales with mass, and a child would
    // get squashed along with it.
    public class PlayerMarker : MonoBehaviour
    {
        [Header("Shape")]
        [SerializeField] private float sizeFactor = 0.45f;   // arrow size relative to the ball radius
        [SerializeField] private float edgeFactor = 1.12f;    // distance from the centre, >1 = outside the ball
        [SerializeField] private float gap = 0.35f;          // world units of empty space between ball and arrow
        [SerializeField] private Color color = new Color(1f, 1f, 1f, 0.95f);

        private SpriteRenderer sprite;
        private Vector2 lastDirection = Vector2.right;

        private void Awake()
        {
            EnsureSprite();
        }

        // called every frame by PlayerController with one of the player's balls
        public void Follow(SizeController ball, Vector2 direction, float extraGap)
        {
            EnsureSprite();

            if (ball == null || ball.IsDead)
            {
                sprite.enabled = false;
                return;
            }

            sprite.enabled = true;

            if (direction.sqrMagnitude > 0.0001f)
            {
                lastDirection = direction.normalized;
            }
            else
            {
                direction = lastDirection;
            }

            float radius = ball.Radius;
            float size = Mathf.Max(0.3f, radius * sizeFactor);

            // the arrow head sits outside the ball and keeps a real gap, so the ball edge is
            // never covered by its own arrow however big the ball gets
            float distance = radius * edgeFactor + gap + extraGap;
            Vector3 offset = (Vector3)(lastDirection * distance);

            transform.position = ball.transform.position + offset;
            transform.rotation = Quaternion.Euler(0f, 0f, Mathf.Atan2(lastDirection.y, lastDirection.x) * Mathf.Rad2Deg);

            transform.localScale = new Vector3(size, size, 1f);

            // always above the ball it belongs to, and above anything it is covering
            sprite.sortingOrder = SizeController.SortingOrderForRadius(radius) + 200;
        }

        private void EnsureSprite()
        {
            if (sprite != null)
            {
                return;
            }

            sprite = GetComponent<SpriteRenderer>();
            if (sprite == null)
            {
                sprite = gameObject.AddComponent<SpriteRenderer>();
            }

            if (sprite.sprite == null)
            {
                sprite.sprite = BuildTriangle(64);
            }

            sprite.color = color;
        }

        // white triangle pointing along +X, 2x supersampled so the edge is not jagged
        private static Sprite BuildTriangle(int resolution)
        {
            var texture = new Texture2D(resolution, resolution, TextureFormat.RGBA32, false);
            texture.filterMode = FilterMode.Bilinear;

            Vector2 a = new Vector2(resolution * 0.90f, resolution * 0.5f);   // tip, right
            Vector2 b = new Vector2(resolution * 0.22f, resolution * 0.10f);  // bottom left
            Vector2 c = new Vector2(resolution * 0.22f, resolution * 0.90f);  // top left

            var pixels = new Color32[resolution * resolution];

            for (int y = 0; y < resolution; y++)
            {
                for (int x = 0; x < resolution; x++)
                {
                    int hits = 0;
                    for (int sy = 0; sy < 2; sy++)
                    {
                        for (int sx = 0; sx < 2; sx++)
                        {
                            Vector2 p = new Vector2(x + (sx + 0.5f) * 0.5f, y + (sy + 0.5f) * 0.5f);
                            if (Inside(p, a, b, c))
                            {
                                hits++;
                            }
                        }
                    }

                    byte alpha = (byte)(hits * 255 / 4);
                    pixels[y * resolution + x] = new Color32(255, 255, 255, alpha);
                }
            }

            texture.SetPixels32(pixels);
            texture.Apply();

            return Sprite.Create(texture, new Rect(0f, 0f, resolution, resolution), new Vector2(0.5f, 0.5f), resolution);
        }

        private static bool Inside(Vector2 p, Vector2 a, Vector2 b, Vector2 c)
        {
            float d1 = Cross(p - a, b - a);
            float d2 = Cross(p - b, c - b);
            float d3 = Cross(p - c, a - c);

            bool negative = d1 < 0f || d2 < 0f || d3 < 0f;
            bool positive = d1 > 0f || d2 > 0f || d3 > 0f;

            return !(negative && positive);
        }

        private static float Cross(Vector2 u, Vector2 v)
        {
            return u.x * v.y - u.y * v.x;
        }
    }
}
