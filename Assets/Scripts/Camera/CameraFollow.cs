using UnityEngine;

namespace QiuQiu
{
    public class CameraFollow : MonoBehaviour
    {
        public enum CenterMode
        {
            BoundsCenter,
            MassWeightedCenter
        }

        [Header("Target")]
        [SerializeField] private PlayerController player;
        [SerializeField] private Vector3 followOffset = new Vector3(0f, 0f, -10f);
        [SerializeField] private CenterMode centerMode = CenterMode.MassWeightedCenter;
        [SerializeField] private float positionLerpSpeed = 8f;
        [SerializeField] private float maxPanDistance = 12f;
        [SerializeField] private bool holdPositionWhenNoBall = true;

        [Header("Framing")]
        [SerializeField] private float padding = 1.5f;
        [SerializeField] private float radiusScale = 1.1f;
        [SerializeField] private float minOrthographicSize = 5f;
        [SerializeField] private float maxOrthographicSize = 0f;

        [Header("Zoom smoothing")]
        [SerializeField] private float zoomOutLerpSpeed = 10f;
        [SerializeField] private float zoomInLerpSpeed = 2.5f;
        [SerializeField] private bool snapWhenBallWouldClip = true;

        private Camera cam;

        
        private struct PlayerFrame
        {
            public bool valid;
            public float minX;
            public float minY;
            public float maxX;
            public float maxY;
            public Vector2 weightedCenter;
        }

        private void Awake()
        {
            cam = GetComponent<Camera>();
            cam.orthographic = true;

            if (player == null)
            {
                player = FindFirstObjectByType<PlayerController>();
            }

            
        }

        private void LateUpdate()
        {

            if (player == null || !player.isActiveAndEnabled)
            {
                player = PlayerController.Active;
            }

            PlayerFrame frame = CaptureFrame();

            if (!frame.valid)
            {
                if (!holdPositionWhenNoBall)
                {
                    transform.position = followOffset;
                }
                    return;
            }

            Vector3 center = ViewCenter(frame);
            float aspect = GetAspect();

            //camera
            UpdatePosition(center);

            //how big to make camera can see all ball
            RequiredExtents(frame, center, out float halfWidth, out float halfHeight);

            float targetSize = SizeFor(halfWidth, halfHeight, aspect);

            
            if (snapWhenBallWouldClip)
            {
                Vector2 actualCenter = transform.position - followOffset;
                RequiredExtents(frame, actualCenter, out float actualHalfWidth, out float actualHalfHeight);
                float actualSize = SizeFor(actualHalfWidth, actualHalfHeight, aspect);
                if (actualSize > targetSize)
                {
                    targetSize = actualSize;
                }
            }

            UpdateZoom(targetSize, halfWidth, halfHeight, aspect);
        }

        private PlayerFrame CaptureFrame()
        {
            var frame = new PlayerFrame();
            if (player == null)
            {
                return frame;
            }

            var balls = player.Balls;
            if (balls == null)
            {
                return frame;
            }

            float scale = Mathf.Max(0.01f, radiusScale);
            Vector2 weightedSum = Vector2.zero;
            float weightSum = 0f;
            bool any = false;

            for (int i = 0; i < balls.Count; i++)
            {
                var ball = balls[i];
                if (ball == null || ball.IsDead)
                {
                    continue;
                }

                Vector2 p = ball.transform.position;
                float r = Mathf.Max(0.01f, ball.Radius * scale);
                float weight = Mathf.Max(0.01f, ball.Mass);
                weightedSum += p * weight;
                weightSum += weight;

                if (!any)
                {
                    frame.minX = p.x - r;
                    frame.maxX = p.x + r;
                    frame.minY = p.y - r;
                    frame.maxY = p.y + r;
                    any = true;
                    continue;
                }

                frame.minX = Mathf.Min(frame.minX, p.x - r);
                frame.maxX = Mathf.Max(frame.maxX, p.x + r);
                frame.minY = Mathf.Min(frame.minY, p.y - r);
                frame.maxY = Mathf.Max(frame.maxY, p.y + r);
            }

            if (!any)   //not ball
            {
                return frame;
            }

            frame.valid = true;
            frame.weightedCenter = weightSum > 0f ? weightedSum / weightSum : BoundsCenterOf(frame);
            return frame;
        }

        private static Vector2 BoundsCenterOf(PlayerFrame frame)
        {
            return new Vector2((frame.minX + frame.maxX) * 0.5f, (frame.minY + frame.maxY) * 0.5f);
        }

        private Vector3 ViewCenter(PlayerFrame frame)
        {
            if (centerMode == CenterMode.BoundsCenter) return BoundsCenterOf(frame);

           
            return frame.weightedCenter;
        }

        private void RequiredExtents(PlayerFrame frame, Vector2 center, out float halfWidth, out float halfHeight)
        {
            
            float pad = Mathf.Max(0f, padding);
            halfWidth = Mathf.Max(center.x - frame.minX, frame.maxX - center.x) + pad;
            halfHeight = Mathf.Max(center.y - frame.minY, frame.maxY - center.y) + pad;
        }

        private float SizeFor(float halfWidth, float halfHeight, float aspect)
        {
           
            float size = Mathf.Max(halfHeight, halfWidth / Mathf.Max(0.0001f, aspect), minOrthographicSize);
            if (maxOrthographicSize > 0f) size = Mathf.Min(size, maxOrthographicSize);
            return size;
        }

        private void UpdatePosition(Vector3 center)
        {
            Vector3 target = center + followOffset;
            float gap = Vector2.Distance(transform.position, target);

            //for respawn
            if (maxPanDistance > 0f && gap > maxPanDistance)
            {
                
                transform.position = target;
                return;
            }

            float t = 1f - Mathf.Exp(-Mathf.Max(0f, positionLerpSpeed) * Time.deltaTime);
            transform.position = Vector3.Lerp(transform.position, target, t);
        }

        private void UpdateZoom(float targetSize, float halfWidth, float halfHeight, float aspect)
        {
            float current = cam.orthographicSize;
            float speed = targetSize > current ? zoomOutLerpSpeed : zoomInLerpSpeed;
            float t = 1f - Mathf.Exp(-Mathf.Max(0.0001f, speed) * Time.unscaledDeltaTime);
            float next = Mathf.Lerp(current, targetSize, t);

            if (snapWhenBallWouldClip && WouldClip(next, halfWidth, halfHeight, aspect))
            {
                next = targetSize;
            }

            cam.orthographicSize = Mathf.Max(minOrthographicSize, next);
        }

        //to check hava or not have ball will be cut out in camera
        private static bool WouldClip(float size, float halfWidth, float halfHeight, float aspect)
        {
            const float epsilon = 0.01f;
            return halfHeight > size + epsilon || halfWidth > size * aspect + epsilon;
        }

        //get aspect ratio to avoid camera cut ball size that can't to see
        private float GetAspect()
        {
            float aspect = cam != null ? cam.aspect : 0f;
            return aspect > 0.0001f ? aspect : 1f;
        }
    }
}
