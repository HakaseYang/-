using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.UI;

namespace QiuQiu
{
    // 右上角排行榜：照着 agar.io 那个网页版做的（半透明深色板 + 标题 + 名次）。
    // 整块面板在运行时自己搭出来，场景里只要挂这个组件，不用连任何引用。
    public class LeaderboardPanel : MonoBehaviour
    {
        [Header("Layout")]
        [SerializeField] private int rowCount = 8;
        [SerializeField] private Vector2 panelSize = new Vector2(300f, 268f);
        [SerializeField] private Vector2 margin = new Vector2(-16f, -230f);  // 相对右上角（避开右上角暂停键）
        [SerializeField] private float refreshInterval = 0.25f;

        [Header("Colors")]
        [SerializeField] private Color panelColor = new Color(0.04f, 0.05f, 0.08f, 0.74f);
        [SerializeField] private Color titleColor = new Color(1f, 1f, 1f, 0.95f);
        [SerializeField] private Color rowColor = new Color(0.86f, 0.89f, 0.95f, 0.95f);
        [SerializeField] private Color playerColor = new Color(1f, 0.42f, 0.42f, 1f);

        private Text title;
        private RectTransform panelRoot;
        private readonly List<Text> rows = new List<Text>();
        private readonly List<SizeController> sorted = new List<SizeController>();
        private readonly StringBuilder builder = new StringBuilder();
        private float nextRefreshTime;
        private int lastSignature = int.MinValue;

        private void Start()
        {
            Build();
            Refresh(true);
        }

        private void Update()
        {
            if (Time.unscaledTime < nextRefreshTime)
            {
                return;
            }
            nextRefreshTime = Time.unscaledTime + Mathf.Max(0.05f, refreshInterval);
            Refresh(false);
        }

        private void Build()
        {
            if (!(transform is RectTransform))
            {
                // 需要一个 UI 父节点来定位，挂到 Canvas 下面就行
                enabled = false;
                return;
            }

            // 面板自己单开一层，不动宿主物体的 RectTransform（宿主可能是 HUD 或 Canvas）
            GameObject panelObject = new GameObject("Leaderboard", typeof(RectTransform), typeof(Image));
            panelObject.transform.SetParent(transform, false);

            RectTransform root = panelObject.GetComponent<RectTransform>();
            root.anchorMin = new Vector2(1f, 1f);
            root.anchorMax = new Vector2(1f, 1f);
            root.pivot = new Vector2(1f, 1f);
            root.anchoredPosition = margin;
            root.sizeDelta = panelSize;

            Image background = panelObject.GetComponent<Image>();
            background.color = panelColor;
            background.raycastTarget = false;

            panelRoot = root;
            Font font = LoadFont();

            title = NewText("Title", font, 26, FontStyle.Bold, TextAnchor.UpperLeft, titleColor);
            SetTopRow(title.rectTransform, 8f, 32f);
            title.text = "Leaderboard";

            int count = Mathf.Max(1, rowCount);
            for (int i = 0; i < count; i++)
            {
                Text row = NewText("Row" + i, font, 20, FontStyle.Normal, TextAnchor.UpperLeft, rowColor);
                SetTopRow(row.rectTransform, 48f + i * 25f, 24f);
                rows.Add(row);
            }
        }

        // 从面板顶部往下 offset 像素放一行高 height 的文字
        private static void SetTopRow(RectTransform rect, float offset, float height)
        {
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(1f, 1f);
            rect.pivot = new Vector2(0f, 1f);
            rect.offsetMin = new Vector2(12f, -offset - height);
            rect.offsetMax = new Vector2(-12f, -offset);
        }

        private Font LoadFont()
        {
            Text source = GetComponentInParent<Text>();
            if (source == null)
            {
                source = FindFirstObjectByType<Text>();
            }
            if (source != null && source.font != null)
            {
                return source.font;
            }
            return Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        }

        private Text NewText(string objectName, Font font, int size, FontStyle style, TextAnchor anchor, Color color)
        {
            GameObject go = new GameObject(objectName, typeof(RectTransform), typeof(Text));
            go.transform.SetParent(panelRoot != null ? (Transform)panelRoot : transform, false);

            Text text = go.GetComponent<Text>();
            text.font = font;
            text.fontSize = size;
            text.fontStyle = style;
            text.alignment = anchor;
            text.color = color;
            text.raycastTarget = false;
            text.horizontalOverflow = HorizontalWrapMode.Overflow;
            text.text = string.Empty;
            return text;
        }

        private void Refresh(bool force)
        {
            PlayerController player = PlayerController.Active;
            float playerMass = player != null ? player.TotalMass : 0f;

            sorted.Clear();
            IReadOnlyList<SizeController> all = SizeController.Registry;
            for (int i = 0; i < all.Count; i++)
            {
                SizeController ball = all[i];
                if (ball == null || ball.IsDead || ball.Team == Team.Player)
                {
                    continue;
                }
                sorted.Add(ball);
            }
            sorted.Sort(CompareMass);

            int signature = Mathf.RoundToInt(playerMass);
            for (int i = 0; i < sorted.Count && i < rows.Count; i++)
            {
                signature = signature * 31 + Mathf.RoundToInt(sorted[i].Mass);
            }
            if (!force && signature == lastSignature)
            {
                return;
            }
            lastSignature = signature;

            // 把玩家当成一条，和 AI 一起排
            int playerRank = 1;
            for (int i = 0; i < sorted.Count; i++)
            {
                if (sorted[i].Mass > playerMass)
                {
                    playerRank++;
                }
            }

            int shown = 0;
            int rowIndex = 0;
            for (int place = 1; place <= rows.Count && rowIndex < rows.Count; place++)
            {
                if (place == playerRank && playerMass > 0f)
                {
                    WriteRow(rows[rowIndex++], place, "You", true);
                    shown++;
                    continue;
                }

                if (shown >= sorted.Count)
                {
                    break;
                }

                SizeController ball = sorted[shown++];
                WriteRow(rows[rowIndex++], place, ShortName(ball), false);
            }

            for (int i = rowIndex; i < rows.Count; i++)
            {
                rows[i].text = string.Empty;
            }
        }

        private void WriteRow(Text row, int place, string name, bool isPlayer)
        {
            builder.Length = 0;
            builder.Append(place);
            builder.Append(".  ");
            builder.Append(name);
            row.text = builder.ToString();
            row.color = isPlayer ? playerColor : rowColor;
        }

        private static int CompareMass(SizeController a, SizeController b)
        {
            return b.Mass.CompareTo(a.Mass);
        }

        private static string ShortName(SizeController ball)
        {
            string name = ball.gameObject.name;
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
