using UnityEngine;

namespace QiuQiu
{

    public class ArenaBorder : MonoBehaviour
    {
        private const int BorderSortingOrder = 200;   // above balls and food

        private LineRenderer line;

        public void Refresh(Color color, float width, Vector2 center, Vector2 halfSize)
        {
            if (line == null)
            {
                CreateLine();
            }

            ApplyStyle(color, width);
            RebuildPositions(center, halfSize);
        }

        private void CreateLine()
        {
            line = gameObject.AddComponent<LineRenderer>();
            line.useWorldSpace = true;
            line.loop = true;   //make 4 point connect auto
            line.positionCount = 4;
            line.sortingOrder = BorderSortingOrder;

            Shader shader = Shader.Find("Sprites/Default");
            if (shader != null)
            {
                line.material = new Material(shader);
            }
        }

        private void ApplyStyle(Color color, float width)
        {
            line.startWidth = width;
            line.endWidth = width;
            line.startColor = color;
            line.endColor = color;

            if (line.material != null)
            {
                line.material.color = Color.white;
            }
        }

        private void OnDestroy()
        {
            if (line != null && line.material != null)
            {
                Destroy(line.material);
            }
        }

        // Clockwise from bottom-left; loop closes the rectangle.
        private void RebuildPositions(Vector2 center, Vector2 halfSize)
        {
            line.SetPosition(0, new Vector3(center.x - halfSize.x, center.y - halfSize.y, 0f));
            line.SetPosition(1, new Vector3(center.x - halfSize.x, center.y + halfSize.y, 0f));
            line.SetPosition(2, new Vector3(center.x + halfSize.x, center.y + halfSize.y, 0f));
            line.SetPosition(3, new Vector3(center.x + halfSize.x, center.y - halfSize.y, 0f));
        }
    }
}
