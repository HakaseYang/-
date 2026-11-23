using UnityEngine;

namespace QiuQiu
{
    /// <summary>
    /// Agar 风格的镜头：正常时按玩家总质量给一个适中的视野，分裂时主动向外拉远，
    /// 合并回去再慢慢拉近。镜头中心用所有 cell 的整体包围盒中心，不再用质量加权，
    /// 所以分裂后的质量分配不会把构图带偏。
    /// </summary>
    public class CameraFollow : MonoBehaviour
    {
        [Header("Target")]
        [SerializeField] private PlayerController player;
        [SerializeField] private Vector3 followOffset = new Vector3(0f, 0f, -10f);
        [SerializeField] private bool holdPositionWhenNoBall = true;

        [Header("Base zoom (uses the whole group mass)")]
        [Tooltip("Orthographic size for a group whose total mass is baseZoomReferenceMass")]
        [SerializeField] private float baseZoomBase = 7f;
        [Tooltip("Total mass at which the view is exactly baseZoomBase")]
        [SerializeField] private float baseZoomReferenceMass = 60f;
        [Tooltip("How fast the view opens up as the group grows. 0.25 keeps the old radius^0.45 curve")]
        [SerializeField] private float baseZoomExponent = 0.25f;
        [SerializeField] private float minOrthographicSize = 5f;
        [SerializeField] private float maxOrthographicSize = 26f;

        [Header("Group framing")]
        [Tooltip("Extra world units kept around the group")]
        [SerializeField] private float padding = 1.5f;
        [Tooltip("Cell radius multiplier used when fitting the group on screen")]
        [SerializeField] private float radiusScale = 1.05f;

        [Header("Split zoom")]
        [Tooltip("The view while split is baseZoomMultiplier x the normal view. 1.40 = the balls look 1/1.4 as big")]
        [SerializeField] private float splitZoomMultiplier = 1.40f;
        [Tooltip("Seconds the multiplier takes to come in. Keep it short, the split should feel instant")]
        [SerializeField] private float splitZoomInTime = 0.20f;
        [Tooltip("Seconds the multiplier is kept after a split even if the pieces already sit together")]
        [SerializeField] private float splitBonusHold = 0.45f;
        [Tooltip("Predict the split reach for this long, so the camera starts pulling out before the pieces separate")]
        [SerializeField] private float splitPredictTime = 0.30f;

        [Header("Debug")]
        [Tooltip("Draw the live camera numbers in the top left corner")]
        [SerializeField] private bool showDebug;

        [Header("Smoothing")]
        [Tooltip("Zoom out while the group is split")]
        [SerializeField] private float splitZoomOutTime = 0.16f;
        [Tooltip("Zoom out when the group just grows")]
        [SerializeField] private float zoomOutTime = 0.28f;
        [Tooltip("Zoom in, kept slower than zoom out on purpose")]
        [SerializeField] private float zoomInTime = 0.45f;
        [Tooltip("How quickly the camera centre follows the group centre")]
        [SerializeField] private float positionSmoothTime = 0.22f;

        private Camera cam;
        private float zoomVelocity;
        private Vector3 positionVelocity;

        private float lastSplitTime = -999f;
        private float splitPredictUntil = -999f;
        private Vector2 splitPredictCenter;
        private float splitPredictReach;
        private int lastBallCount;
        private bool subscribed;

        // numbers the debug overlay prints, refreshed every LateUpdate
        private float dbgBaseSize;
        private float dbgMultiplier;
        private float dbgTargetSize;
        private int dbgCellCount;
        private Vector4 dbgBounds;

        // Cached frame of every live cell, recomputed each frame.
        private struct GroupFrame
        {
            public bool valid;
            public float minX, maxX, minY, maxY;
            public Vector2 center;
            public float totalMass;
            public int count;
        }

        private void Awake()
        {
            cam = GetComponent<Camera>();
            cam.orthographic = true;
            if (cam.nearClipPlane < 0.01f) cam.nearClipPlane = 0.01f;
            if (player == null) player = FindFirstObjectByType<PlayerController>();
        }

        private void OnEnable()
        {
            TrySubscribe();
        }

        private void OnDisable()
        {
            Unsubscribe();
        }

        // The split event is pushed from PlayerController, so the camera can start
        // pulling out on the same frame the split happens instead of a frame later.
        private void TrySubscribe()
        {
            if (subscribed || player == null) return;
            player.SplitPerformed += OnSplitPerformed;
            subscribed = true;
        }

        private void Unsubscribe()
        {
            if (!subscribed) return;
            if (player != null) player.SplitPerformed -= OnSplitPerformed;
            subscribed = false;
        }

        private void OnSplitPerformed()
        {
            lastSplitTime = Time.time;

            // Predict where the pieces will be a moment from now. The pieces are born at the
            // parent centre, so without this the camera would only react after they separate.
            GroupFrame frame = CaptureGroup();
            if (frame.valid)
            {
                splitPredictCenter = frame.center;
                splitPredictReach = EstimateSplitReach(frame);
                splitPredictUntil = Time.time + Mathf.Max(0.01f, splitPredictTime);
            }
        }

        private void LateUpdate()
        {
            if (player == null || !player.isActiveAndEnabled)
            {
                player = PlayerController.Active;
                TrySubscribe();
            }

            GroupFrame frame = CaptureGroup();

            if (!frame.valid)
            {
                if (!holdPositionWhenNoBall) transform.position = followOffset;
                return;
            }

            // Safety net: if the split event was missed (adapter order, scene reload),
            // a rising cell count still counts as a split.
            if (frame.count > lastBallCount && lastBallCount > 0)
            {
                OnSplitPerformed();
            }
            lastBallCount = frame.count;

            float aspect = GetAspect();
            Vector2 center = frame.center;

            // While the split is predicted, the group frame is inflated around the pre split centre,
            // so the camera pulls out before the pieces actually spread.
            if (Time.time < splitPredictUntil)
            {
                float inflate = Mathf.Max(0f, splitPredictReach);
                frame.minX = Mathf.Min(frame.minX, splitPredictCenter.x - inflate);
                frame.maxX = Mathf.Max(frame.maxX, splitPredictCenter.x + inflate);
                frame.minY = Mathf.Min(frame.minY, splitPredictCenter.y - inflate);
                frame.maxY = Mathf.Max(frame.maxY, splitPredictCenter.y + inflate);
                center = new Vector2((frame.minX + frame.maxX) * 0.5f, (frame.minY + frame.maxY) * 0.5f);
            }

            float targetSize = ComputeTargetSize(frame, center, aspect);

            UpdatePosition(center, targetSize, aspect);
            UpdateZoom(targetSize);
        }

        private GroupFrame CaptureGroup()
        {
            var frame = new GroupFrame();
            if (player == null) return frame;

            var balls = player.Balls;
            if (balls == null) return frame;

            float scale = Mathf.Max(0.01f, radiusScale);
            float sumMass = 0f;
            bool any = false;

            for (int i = 0; i < balls.Count; i++)
            {
                SizeController ball = balls[i];
                if (ball == null || ball.IsDead) continue;

                Vector2 p = ball.transform.position;
                float r = Mathf.Max(0.01f, ball.Radius * scale);

                if (!any)
                {
                    frame.minX = p.x - r;
                    frame.maxX = p.x + r;
                    frame.minY = p.y - r;
                    frame.maxY = p.y + r;
                    any = true;
                }
                else
                {
                    frame.minX = Mathf.Min(frame.minX, p.x - r);
                    frame.maxX = Mathf.Max(frame.maxX, p.x + r);
                    frame.minY = Mathf.Min(frame.minY, p.y - r);
                    frame.maxY = Mathf.Max(frame.maxY, p.y + r);
                }

                sumMass += Mathf.Max(0.01f, ball.Mass);
                frame.count++;
            }

            if (!any) return frame;

            frame.valid = true;
            frame.totalMass = sumMass;
            frame.center = new Vector2((frame.minX + frame.maxX) * 0.5f, (frame.minY + frame.maxY) * 0.5f);
            return frame;
        }

        private float EstimateSplitReach(GroupFrame frame)
        {
            // A split piece lands roughly one parent diameter away, so that is the reach worth predicting.
            float reach = 0f;
            var balls = player.Balls;
            if (balls != null)
            {
                for (int i = 0; i < balls.Count; i++)
                {
                    var ball = balls[i];
                    if (ball == null || ball.IsDead) continue;
                    reach = Mathf.Max(reach, ball.Radius * 2.4f);
                }
            }
            return reach;
        }

        private float ComputeTargetSize(GroupFrame frame, Vector2 center, float aspect)
        {
            // 1) Base view from the whole group mass. Splitting does not change total mass,
            //    so the base view does not jump when the main ball halves.
            float baseSize = SizeForMass(frame.totalMass);
            dbgBaseSize = baseSize;
            dbgCellCount = frame.count;
            dbgBounds = new Vector4(frame.minX, frame.maxX, frame.minY, frame.maxY);

            // 2) Split multiplier. It multiplies the base size, never the current size,
            //    so chaining splits cannot stack the effect forever.
            float multiplier = 1f;
            bool splitActive = frame.count > 1 || Time.time - lastSplitTime < Mathf.Max(0f, splitBonusHold);
            if (splitActive && splitZoomMultiplier > 1f)
            {
                float ramp = 1f;
                if (Time.time - lastSplitTime < splitZoomInTime && splitZoomInTime > 0.0001f)
                {
                    ramp = Mathf.Clamp01((Time.time - lastSplitTime) / splitZoomInTime);
                }
                multiplier = Mathf.Lerp(1f, splitZoomMultiplier, ramp);
            }

            float target = baseSize * multiplier;

            // 3) Everything must stay visible: fit the whole group bounds.
            float halfWidth = Mathf.Max(center.x - frame.minX, frame.maxX - center.x) + Mathf.Max(0f, padding);
            float halfHeight = Mathf.Max(center.y - frame.minY, frame.maxY - center.y) + Mathf.Max(0f, padding);
            float boundsSize = Mathf.Max(halfHeight, halfWidth / Mathf.Max(0.0001f, aspect));

            target = Mathf.Max(target, boundsSize);
            target = Mathf.Max(target, minOrthographicSize);

            // Clamp so a huge group cannot shrink the balls into nothing.
            if (maxOrthographicSize > 0f) target = Mathf.Min(target, maxOrthographicSize);

            dbgMultiplier = multiplier;
            dbgTargetSize = target;
            return target;
        }

        // Temporary read out, so the split zoom can be checked on screen instead of guessed.
        private void OnGUI()
        {
            if (!showDebug) return;

            float current = cam != null ? cam.orthographicSize : 0f;
            string text =
                "Camera Size : " + current.ToString("F2") + "\n" +
                "Target Size : " + dbgTargetSize.ToString("F2") + "\n" +
                "Base Size   : " + dbgBaseSize.ToString("F2") + "\n" +
                "Split Mult. : " + dbgMultiplier.ToString("F2") + "\n" +
                "Cell Count  : " + dbgCellCount + "\n" +
                "Bounds X    : " + dbgBounds.x.ToString("F1") + " .. " + dbgBounds.y.ToString("F1") + "\n" +
                "Bounds Y    : " + dbgBounds.z.ToString("F1") + " .. " + dbgBounds.w.ToString("F1");

            GUI.Label(new Rect(12f, 96f, 320f, 170f), text);
        }

        private float SizeForMass(float totalMass)
        {
            if (baseZoomExponent <= 0f) return Mathf.Max(0f, baseZoomBase);

            float reference = Mathf.Max(0.01f, baseZoomReferenceMass);
            float ratio = Mathf.Max(0.01f, totalMass / reference);
            return Mathf.Max(0.01f, baseZoomBase) * Mathf.Pow(ratio, baseZoomExponent);
        }

        private void UpdatePosition(Vector2 center, float targetSize, float aspect)
        {
            Vector3 target = new Vector3(center.x, center.y, 0f) + followOffset;

            // Keep the view inside the arena instead of teleporting the camera when the group is far.
            Vector2 clamped = ClampToArena(center, targetSize, aspect);
            target = new Vector3(clamped.x, clamped.y, 0f) + followOffset;

            float smooth = Mathf.Max(0.0001f, positionSmoothTime * GamePacing.CameraSmooth);

            // Split is the one moment where the group centre jumps, follow it a bit quicker.
            if (Time.time - lastSplitTime < Mathf.Max(0f, splitBonusHold))
            {
                smooth = Mathf.Min(smooth, Mathf.Max(0.05f, splitZoomInTime));
            }

            transform.position = Vector3.SmoothDamp(transform.position, target, ref positionVelocity, smooth, Mathf.Infinity, Time.unscaledDeltaTime);
        }

        private Vector2 ClampToArena(Vector2 center, float viewSize, float aspect)
        {
            ArenaManager arena = ArenaManager.Instance;
            if (arena == null) return center;

            Vector2 arenaCenter = arena.Center;
            Vector2 half = arena.HalfSize;
            float halfHeight = viewSize;
            float halfWidth = viewSize * Mathf.Max(0.0001f, aspect);

            // Only clamp an axis when the arena is wider than the view on that axis,
            // otherwise the clamp would fight the follow and cause jitter.
            if (half.x * 2f > halfWidth * 2f)
            {
                center.x = Mathf.Clamp(center.x, arenaCenter.x - half.x + halfWidth, arenaCenter.x + half.x - halfWidth);
            }
            if (half.y * 2f > halfHeight * 2f)
            {
                center.y = Mathf.Clamp(center.y, arenaCenter.y - half.y + halfHeight, arenaCenter.y + half.y - halfHeight);
            }
            return center;
        }

        private void UpdateZoom(float targetSize)
        {
            float current = cam.orthographicSize;
            float smooth;

            if (targetSize > current)
            {
                bool justSplit = Time.time - lastSplitTime < SplitWindow();
                smooth = justSplit ? splitZoomOutTime : zoomOutTime;
            }
            else
            {
                smooth = zoomInTime;
            }

            smooth = Mathf.Max(0.0001f, smooth * GamePacing.CameraSmooth);
            float next = Mathf.SmoothDamp(current, targetSize, ref zoomVelocity, smooth, Mathf.Infinity, Time.unscaledDeltaTime);

            cam.orthographicSize = Mathf.Clamp(next, minOrthographicSize, maxOrthographicSize > 0f ? maxOrthographicSize : next);
        }

        private float SplitWindow()
        {
            return Mathf.Max(splitPredictTime, splitBonusHold);
        }

        private float GetAspect()
        {
            float aspect = cam != null ? cam.aspect : 0f;
            return aspect > 0.0001f ? aspect : 1f;
        }
    }
}
