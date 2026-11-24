using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace QiuQiu
{
    /// <summary>
    /// Small spring used by every juicy widget. No coroutines, no allocations: one float pair
    /// per animated value, integrated each frame. Overshoot comes from the damping, that is
    /// what makes it feel like jelly instead of a linear lerp.
    /// </summary>
    internal static class JuiceSpring
    {
        public static void Step(ref float value, ref float velocity, float target, float stiffness, float damping, float dt)
        {
            velocity += (target - value) * stiffness * dt;
            velocity *= Mathf.Exp(-damping * dt);
            value += velocity * dt;
        }
    }

    /// <summary>
    /// Hover / press jelly for a button. Long buttons tilt less (a huge rotation on a wide
    /// button looks broken), small buttons get the full tilt - same idea as scaling the
    /// animation strength by the rect size.
    /// </summary>
    [DisallowMultipleComponent]
    public class JuicyButton : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IPointerDownHandler, IPointerUpHandler
    {
        [Header("Scale")]
        [SerializeField] private float hoverScale = 1.08f;
        [SerializeField] private float pressScale = 0.93f;
        [SerializeField] private float stiffness = 320f;
        [SerializeField] private float damping = 17f;

        [Header("Tilt")]
        [Tooltip("Max degrees on hover. It is scaled down automatically for wide buttons")]
        [SerializeField] private float hoverTilt = 4f;
        [Tooltip("Width in pixels where the tilt starts to fade out")]
        [SerializeField] private float tiltFadeWidth = 260f;

        private RectTransform rect;
        private Vector3 baseScale = Vector3.one;
        private float baseAngle;

        private float scale = 1f;
        private float scaleVelocity;
        private float tilt;
        private float tiltVelocity;

        private bool hovered;
        private bool pressed;

        private void Awake()
        {
            rect = transform as RectTransform;
            baseScale = transform.localScale;
            baseAngle = transform.localEulerAngles.z;
        }

        private void OnDisable()
        {
            hovered = false;
            pressed = false;
            scale = 1f;
            scaleVelocity = 0f;
            tilt = 0f;
            tiltVelocity = 0f;
            transform.localScale = baseScale;
            transform.localRotation = Quaternion.Euler(0f, 0f, baseAngle);
        }

        public void OnPointerEnter(PointerEventData eventData)
        {
            hovered = true;
        }

        public void OnPointerExit(PointerEventData eventData)
        {
            hovered = false;
            pressed = false;
        }

        public void OnPointerDown(PointerEventData eventData)
        {
            pressed = true;
        }

        public void OnPointerUp(PointerEventData eventData)
        {
            pressed = false;
        }

        private void Update()
        {
            float dt = Mathf.Min(Time.unscaledDeltaTime, 0.05f);

            float targetScale = pressed ? pressScale : (hovered ? hoverScale : 1f);
            float targetTilt = hovered ? TiltForSize() : 0f;

            JuiceSpring.Step(ref scale, ref scaleVelocity, targetScale, stiffness, damping, dt);
            JuiceSpring.Step(ref tilt, ref tiltVelocity, targetTilt, stiffness * 0.8f, damping, dt);

            transform.localScale = baseScale * scale;
            transform.localRotation = Quaternion.Euler(0f, 0f, baseAngle + tilt);
        }

        private float TiltForSize()
        {
            float width = rect != null ? rect.rect.width : tiltFadeWidth;
            float weight = Mathf.Clamp01(tiltFadeWidth / Mathf.Max(1f, width));
            return hoverTilt * weight;
        }
    }

    /// <summary>
    /// Pops when the label changes (score, mass, timer). The punch is a scale spring plus a
    /// short colour flash, so a number "lands" instead of silently swapping text.
    /// </summary>
    [DisallowMultipleComponent]
    public class JuicyNumber : MonoBehaviour
    {
        [SerializeField] private float punchScale = 1.18f;
        [SerializeField] private float stiffness = 260f;
        [SerializeField] private float damping = 12f;
        [SerializeField] private Color flash = new Color(1f, 0.86f, 0.35f, 1f);

        private UnityEngine.UI.Text unityLabel;
        private Vector3 baseScale = Vector3.one;
        private Color baseColor = Color.white;
        private string lastText;

        private float scale = 1f;
        private float scaleVelocity;
        private float punch;

        private void Awake()
        {
            unityLabel = GetComponent<UnityEngine.UI.Text>();
            if (unityLabel != null)
            {
                baseColor = unityLabel.color;
                lastText = unityLabel.text;
            }
            baseScale = transform.localScale;
        }

        private void Update()
        {
            float dt = Mathf.Min(Time.unscaledDeltaTime, 0.05f);

            if (unityLabel != null && unityLabel.text != lastText)
            {
                lastText = unityLabel.text;
                punch = 1f;
            }

            punch = Mathf.MoveTowards(punch, 0f, dt * 4f);

            float target = 1f + (punchScale - 1f) * punch;
            JuiceSpring.Step(ref scale, ref scaleVelocity, target, stiffness, damping, dt);
            transform.localScale = baseScale * scale;

            if (unityLabel != null)
            {
                unityLabel.color = Color.Lerp(baseColor, flash, punch * 0.85f);
            }
        }
    }

    /// <summary>
    /// Staggers the children when the panel is enabled: each one springs in a little later
    /// than the previous, so a menu opens as a wave instead of all at once.
    /// </summary>
    [DisallowMultipleComponent]
    public class JuicyGroup : MonoBehaviour
    {
        [SerializeField] private float stagger = 0.06f;
        [SerializeField] private float slideIn = 60f;
        [SerializeField] private float stiffness = 240f;
        [SerializeField] private float damping = 13f;

        private RectTransform[] kids;
        private CanvasGroup[] groups;
        private Vector2[] homes;
        private float[] values;
        private float[] velocities;
        private float[] delays;
        private bool started;
        private float elapsed;

        private void OnEnable()
        {
            started = false;
            elapsed = 0f;
        }

        private void Update()
        {
            if (!started)
            {
                Collect();
            }

            float dt = Mathf.Min(Time.unscaledDeltaTime, 0.05f);
            elapsed += dt;

            for (int i = 0; i < kids.Length; i++)
            {
                if (kids[i] == null || elapsed < delays[i])
                {
                    continue;
                }
                JuiceSpring.Step(ref values[i], ref velocities[i], 1f, stiffness, damping, dt);
                // Slide + fade instead of scaling: a button on the same object already owns its
                // own scale (JuicyButton), and two systems writing localScale fight each other.
                kids[i].anchoredPosition = homes[i] + new Vector2(-slideIn * (1f - values[i]), 0f);
                if (groups[i] != null)
                {
                    groups[i].alpha = Mathf.Clamp01(values[i]);
                }
            }
        }

        private void Collect()
        {
            started = true;
            int count = transform.childCount;
            kids = new RectTransform[count];
            values = new float[count];
            velocities = new float[count];
            delays = new float[count];
            homes = new Vector2[count];
            groups = new CanvasGroup[count];

            for (int i = 0; i < count; i++)
            {
                kids[i] = transform.GetChild(i) as RectTransform;
                values[i] = 0f;
                velocities[i] = 0f;
                delays[i] = stagger * i;
                if (kids[i] == null)
                {
                    continue;
                }

                homes[i] = kids[i].anchoredPosition;
                groups[i] = kids[i].GetComponent<CanvasGroup>();
                if (groups[i] == null)
                {
                    groups[i] = kids[i].gameObject.AddComponent<CanvasGroup>();
                }
                groups[i].alpha = 0f;
                kids[i].anchoredPosition = homes[i] + new Vector2(-slideIn, 0f);
            }
        }
    }

    /// <summary>
    /// Attaches the jelly to a whole canvas at runtime: every button gets JuicyButton, and the
    /// few value labels (mass, rank, timer) get JuicyNumber. One component per canvas instead of
    /// hand-wiring every widget - move the canvas, the juice comes along.
    /// </summary>
    [DisallowMultipleComponent]
    public class UiJuiceBootstrap : MonoBehaviour
    {
        [SerializeField] private bool juiceButtons = true;

        private void Awake()
        {
            if (juiceButtons)
            {
                JuiceScene();
            }
        }

        /// <summary>
        /// Adds the jelly to every button and to the few value labels of the loaded scene.
        /// Called from the scene controllers so nothing has to be wired by hand in the editor.
        /// Pass false for buttons on a touch-only build: there is no pointer to hover with, and
        /// the pop on press is not worth the extra components on a menu.
        /// </summary>
        public static void JuiceScene(bool juiceButtonsInThisScene = true)
        {
            if (juiceButtonsInThisScene)
            {
                foreach (Button button in UnityEngine.Object.FindObjectsByType<Button>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                {
                    if (button.GetComponent<JuicyButton>() == null)
                    {
                        button.gameObject.AddComponent<JuicyButton>();
                    }
                }
            }

            string[] wanted = { "Mass", "Rank", "Timer" };
            foreach (UnityEngine.UI.Text label in UnityEngine.Object.FindObjectsByType<UnityEngine.UI.Text>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (label.GetComponent<JuicyNumber>() != null)
                {
                    continue;
                }
                foreach (string key in wanted)
                {
                    if (label.name.IndexOf(key, System.StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        label.gameObject.AddComponent<JuicyNumber>();
                        break;
                    }
                }
            }
        }
    }
}
