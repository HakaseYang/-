using UnityEngine;

namespace QiuQiu
{
    // Runtime outline of the arena. Added by ArenaManager.Awake, never placed by hand.
    [DisallowMultipleComponent]
    public class ArenaBorder : MonoBehaviour
    {
        private const int CircleSegments = 64;
        private const int BorderSortingOrder = 200;   // above balls and food

        private LineRenderer line;
        private bool isCircle;
        private Vector2 center;
        private Vector2 halfSize;
        private float circleRadius;

        public void Refresh(Color color, float width, bool circle, Vector2 arenaCenter, Vector2 arenaHalfSize, float arenaCircleRadius)
        {
            isCircle = circle;
            center = arenaCenter;
            halfSize = arenaHalfSize;
            circleRadius = arenaCircleRadius;

            if (line == null) CreateLine(color, width);
            ApplyStyle(color, width);
            RebuildPositions();
        }

        private void CreateLine(Color color, float width)
        {
            line = gameObject.AddComponent<LineRenderer>();
            line.useWorldSpace = true;
            line.loop = true;
            line.positionCount = CircleSegments;
            line.sortingOrder = BorderSortingOrder;

            Shader shader = Shader.Find("Sprites/Default");
            if (shader != null) line.material = new Material(shader);
        }

        private void ApplyStyle(Color color, float width)
        {
            line.startWidth = width;
            line.endWidth = width;
            line.startColor = color;
            line.endColor = color;

            if (line.material != null) line.material.color = color;
        }

        private void RebuildPositions()
        {
            if (isCircle)
            {
                if (line.positionCount != CircleSegments) line.positionCount = CircleSegments;

                for (int i = 0; i < CircleSegments; i++)
                {
                    float angle = (float)i / CircleSegments * Mathf.PI * 2f;
                    line.SetPosition(i, new Vector3(
                        center.x + Mathf.Cos(angle) * circleRadius,
                        center.y + Mathf.Sin(angle) * circleRadius,
                        0f));
                }

                return;
            }

            if (line.positionCount != 4) line.positionCount = 4;

            line.SetPosition(0, new Vector3(center.x - halfSize.x, center.y - halfSize.y, 0f));
            line.SetPosition(1, new Vector3(center.x - halfSize.x, center.y + halfSize.y, 0f));
            line.SetPosition(2, new Vector3(center.x + halfSize.x, center.y + halfSize.y, 0f));
            line.SetPosition(3, new Vector3(center.x + halfSize.x, center.y - halfSize.y, 0f));
        }
    }
}
