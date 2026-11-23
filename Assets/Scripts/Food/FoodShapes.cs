using UnityEngine;

namespace QiuQiu
{
    // A map covered in identical circles gets tiring to look at, so food picks one of a few
    // silhouettes instead. They are drawn once at start-up, all white with an alpha mask, so the
    // SpriteRenderer colour still tints them like the old round sprite did.
    public static class FoodShapes
    {
        private const int TextureSize = 64;
        private const int SubSamples = 3;
        private const float StarInnerRatio = 0.45f;

        private static Sprite[] cache;

        public static Sprite[] All
        {
            get
            {
                if (cache == null)
                {
                    Build();
                }

                return cache;
            }
        }

        public static Sprite Pick()
        {
            Sprite[] all = All;
            return all[Random.Range(0, all.Length)];
        }

        private static void Build()
        {
            cache = new Sprite[]
            {
                Make("FoodCircle", 0),
                Make("FoodTriangle", 3),
                Make("FoodSquare", 4),
                Make("FoodDiamond", 4, 45f),
                Make("FoodPentagon", 5),
                Make("FoodStar", 5, -90f, true),
            };
        }

        private static Sprite Make(string name, int sides, float rotationDegrees = 0f, bool star = false)
        {
            Texture2D texture = new Texture2D(TextureSize, TextureSize, TextureFormat.RGBA32, false)
            {
                name = name + "Texture",
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
            };

            Vector2[] polygon = BuildPolygon(sides, rotationDegrees, star);
            Color32[] pixels = new Color32[TextureSize * TextureSize];
            float radius = TextureSize * 0.5f - 1f;
            Vector2 centre = new Vector2(TextureSize * 0.5f, TextureSize * 0.5f);
            float step = 1f / SubSamples;

            for (int y = 0; y < TextureSize; y++)
            {
                for (int x = 0; x < TextureSize; x++)
                {
                    int hits = 0;
                    for (int sy = 0; sy < SubSamples; sy++)
                    {
                        for (int sx = 0; sx < SubSamples; sx++)
                        {
                            Vector2 sample = new Vector2(x + (sx + 0.5f) * step, y + (sy + 0.5f) * step);
                            if (Covers((sample - centre) / radius, polygon))
                            {
                                hits++;
                            }
                        }
                    }

                    float alpha = hits / (float)(SubSamples * SubSamples);
                    pixels[y * TextureSize + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(alpha * 255f));
                }
            }

            texture.SetPixels32(pixels);
            texture.Apply(false, false);

            return Sprite.Create(texture, new Rect(0f, 0f, TextureSize, TextureSize), new Vector2(0.5f, 0.5f), TextureSize);
        }

        // sides = 0 means "circle"; a star is a 2*points polygon with alternating radii.
        private static Vector2[] BuildPolygon(int sides, float rotationDegrees, bool star)
        {
            if (sides <= 0)
            {
                return null;
            }

            int points = star ? sides * 2 : sides;
            Vector2[] polygon = new Vector2[points];

            for (int i = 0; i < points; i++)
            {
                float angle = (rotationDegrees - 90f + 360f * i / points) * Mathf.Deg2Rad;
                float radius = star && i % 2 == 1 ? StarInnerRatio : 1f;
                polygon[i] = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * radius;
            }

            return polygon;
        }

        private static bool Covers(Vector2 point, Vector2[] polygon)
        {
            if (polygon == null)
            {
                return point.sqrMagnitude <= 1f;
            }

            bool inside = false;
            for (int i = 0, j = polygon.Length - 1; i < polygon.Length; j = i++)
            {
                Vector2 a = polygon[i];
                Vector2 b = polygon[j];
                if (a.y > point.y != b.y > point.y &&
                    point.x < (b.x - a.x) * (point.y - a.y) / (b.y - a.y) + a.x)
                {
                    inside = !inside;
                }
            }

            return inside;
        }
    }
}
