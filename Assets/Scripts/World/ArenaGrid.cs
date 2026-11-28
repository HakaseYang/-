using System.Collections.Generic;
using UnityEngine;

namespace QiuQiu
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(ArenaManager))]
    public class ArenaGrid : MonoBehaviour
    {
        [Header("Grid")]
        [Tooltip("World units between two lines")]
        [SerializeField] private float cellSize = 4f;

        [Tooltip("Line thickness in world units")]
        [SerializeField] private float lineWidth = 0.055f;

        [Tooltip("Line colour, alpha is what keeps it subtle on the dark background")]
        [SerializeField] private Color lineColor = new Color(1f, 1f, 1f, 0.075f);

        [Tooltip("Every N lines gets a brighter one; 0 disables the accent")]
        [SerializeField] private int accentEvery = 5;

        [Tooltip("Accent line colour")]
        [SerializeField] private Color accentColor = new Color(1f, 1f, 1f, 0.13f);

        [Tooltip("Lower than every sprite so the grid stays behind the balls")]
        [SerializeField] private int sortingOrder = -100;

        [Header("Background")]
        [Tooltip("Also repaint the camera background, for the light 'paper' look")]
        [SerializeField] private bool paintBackground = false;

        [SerializeField] private Color backgroundColor = new Color(0.55f, 0.57f, 0.60f, 1f);

        [SerializeField] private Color lineColorOnPaper = new Color(0.42f, 0.44f, 0.47f, 1f);

        private Mesh mesh;
        private MeshRenderer meshRenderer;
        private Material material;

        private void Awake()
        {
            Build();
        }

        private void Start()
        {
            if (!paintBackground)
            {
                return;
            }

            Camera cam = Camera.main;
            if (cam == null)
            {
                return;
            }

            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = backgroundColor;
            RebuildWithColor(lineColorOnPaper, lineColorOnPaper);
        }

        [ContextMenu("Rebuild Grid")]
        public void Build()
        {
            ArenaManager arena = GetComponent<ArenaManager>();
            if (arena == null)
            {
                return;
            }

            Vector2 half = arena.HalfSize;
            Vector2 center = arena.Center;

            EnsureRenderer();

            float minX = center.x - half.x - cellSize;
            float maxX = center.x + half.x + cellSize;
            float minY = center.y - half.y - cellSize;
            float maxY = center.y + half.y + cellSize;

            List<Vector3> vertices = new List<Vector3>(512);
            List<Color> colors = new List<Color>(512);
            List<int> triangles = new List<int>(768);

            int index = 0;
            float halfLine = lineWidth * 0.5f;

            for (float x = minX; x <= maxX + 0.001f; x += cellSize)
            {
                bool accent = accentEvery > 0 && Mathf.Abs(Mathf.Round(x / cellSize)) % accentEvery < 0.5f;
                AddQuad(vertices, colors, triangles, ref index,
                    new Vector2(x - halfLine, minY), new Vector2(x + halfLine, maxY),
                    accent ? accentColor : lineColor);
            }

            for (float y = minY; y <= maxY + 0.001f; y += cellSize)
            {
                bool accent = accentEvery > 0 && Mathf.Abs(Mathf.Round(y / cellSize)) % accentEvery < 0.5f;
                AddQuad(vertices, colors, triangles, ref index,
                    new Vector2(minX, y - halfLine), new Vector2(maxX, y + halfLine),
                    accent ? accentColor : lineColor);
            }

            if (mesh == null)
            {
                mesh = new Mesh();
                mesh.name = "Arena Grid";
                mesh.MarkDynamic();
            }

            mesh.Clear();
            mesh.SetVertices(vertices);
            mesh.SetColors(colors);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateBounds();

            meshRenderer.sortingOrder = sortingOrder;
        }

        private void RebuildWithColor(Color normal, Color accent)
        {
            Color keepNormal = lineColor;
            Color keepAccent = accentColor;
            lineColor = normal;
            accentColor = accent;
            Build();
            lineColor = keepNormal;
            accentColor = keepAccent;
        }

        private void AddQuad(List<Vector3> vertices, List<Color> colors, List<int> triangles,
            ref int index, Vector2 a, Vector2 b, Color color)
        {
            vertices.Add(new Vector3(a.x, a.y, 0f));
            vertices.Add(new Vector3(a.x, b.y, 0f));
            vertices.Add(new Vector3(b.x, b.y, 0f));
            vertices.Add(new Vector3(b.x, a.y, 0f));

            colors.Add(color);
            colors.Add(color);
            colors.Add(color);
            colors.Add(color);

            triangles.Add(index);
            triangles.Add(index + 1);
            triangles.Add(index + 2);
            triangles.Add(index);
            triangles.Add(index + 2);
            triangles.Add(index + 3);

            index += 4;
        }

        private void EnsureRenderer()
        {
            if (meshRenderer != null)
            {
                return;
            }

            GameObject child = new GameObject("Grid Lines");
            child.transform.SetParent(transform, false);
            child.transform.localPosition = Vector3.zero;

            MeshFilter filter = child.AddComponent<MeshFilter>();
            meshRenderer = child.AddComponent<MeshRenderer>();

            Shader shader = Shader.Find("Sprites/Default");
            if (shader == null)
            {
                Debug.LogWarning("[ArenaGrid] Sprites/Default not available, grid skipped.");
                Destroy(child);
                return;
            }

            material = new Material(shader);
            material.name = "Arena Grid Material";

            meshRenderer.sharedMaterial = material;
            meshRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            meshRenderer.receiveShadows = false;

            filter.sharedMesh = null;
            mesh = new Mesh();
            mesh.name = "Arena Grid";
            filter.sharedMesh = mesh;
        }

        private void OnDestroy()
        {
            if (mesh != null)
            {
                Destroy(mesh);
            }

            if (material != null)
            {
                Destroy(material);
            }
        }
    }
}
