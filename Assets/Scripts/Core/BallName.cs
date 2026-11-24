using UnityEngine;

namespace QiuQiu
{
    // 球里面那行字（网页版 agar.io 就是把名字写在球心），只做显示，不参与任何玩法判定。
    public class BallName : MonoBehaviour
    {
        [SerializeField] private string displayName = "";
        [SerializeField] private int fontSize = 48;
        [SerializeField] private float characterSize = 0.1f;
        [SerializeField] private Color color = new Color(1f, 1f, 1f, 0.92f);

        private TextMesh label;
        private MeshRenderer labelRenderer;
        private SizeController size;

        private void Awake()
        {
            size = GetComponent<SizeController>();
        }

        private void Start()
        {
            if (string.IsNullOrEmpty(displayName))
            {
                displayName = ResolveName();
            }
            Build();
        }

        private void LateUpdate()
        {
            if (label == null || size == null)
            {
                return;
            }

            // 球本身会随质量放大，这里反向补偿，让名字的世界尺寸保持不变
            float scale = Mathf.Max(0.01f, transform.localScale.x);
            label.transform.localScale = Vector3.one / scale;
            labelRenderer.sortingOrder = SizeController.SortingOrderForRadius(size.Radius) + 40;
        }

        private void Build()
        {
            GameObject go = new GameObject("Name");
            go.transform.SetParent(transform, false);
            go.transform.localPosition = Vector3.zero;

            label = go.AddComponent<TextMesh>();
            label.text = displayName;
            label.fontSize = fontSize;
            label.characterSize = characterSize;
            label.anchor = TextAnchor.MiddleCenter;
            label.alignment = TextAlignment.Center;
            label.color = color;
            label.richText = false;
            if (label.font == null)
            {
                label.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            }

            if (label.font == null)
            {
                // no built-in font in this build: drop the label instead of throwing
                Destroy(go);
                return;
            }

            labelRenderer = go.GetComponent<MeshRenderer>();
            if (labelRenderer != null)
            {
                labelRenderer.sharedMaterial = label.font.material;
                labelRenderer.sortingOrder = SizeController.SortingOrderForRadius(size != null ? size.Radius : 1f) + 40;
            }
        }

        private string ResolveName()
        {
            if (size != null && size.Team == Team.Player)
            {
                return "You";
            }

            string name = gameObject.name;
            int cut = name.IndexOf("_Ball", System.StringComparison.Ordinal);
            if (cut > 0)
            {
                name = name.Substring(0, cut);
            }
            cut = name.IndexOf("_Piece", System.StringComparison.Ordinal);
            if (cut > 0)
            {
                name = name.Substring(0, cut);
            }
            cut = name.IndexOf("_Burst", System.StringComparison.Ordinal);
            if (cut > 0)
            {
                name = name.Substring(0, cut);
            }
            return name;
        }
    }
}
