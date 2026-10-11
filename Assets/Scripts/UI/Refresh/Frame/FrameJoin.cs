using UnityEngine;
using UnityEngine.UI;

namespace Orivilon.UI.Refresh
{
    /// <summary>
    /// Rounded inward (concave) corner where a vertical edge of panel <see cref="a"/> meets a
    /// horizontal edge of panel <see cref="b"/>. The fill sprite is transparent inside the curve,
    /// so the world stays visible exactly up to the rounded contour.
    ///
    /// <para>The join keeps itself glued to the two panels (also while editing and during frame
    /// transitions). Its own RectTransform is driven, not saved. When the two edges no longer form
    /// an inward corner (e.g. a panel is collapsed) the join shrinks to nothing.</para>
    /// </summary>
    [ExecuteAlways, DisallowMultipleComponent, RequireComponent(typeof(RectTransform))]
    public class FrameJoin : MonoBehaviour
    {
        public FrameStyle style;
        [Tooltip("Panel that provides the vertical edge.")]
        public RectTransform a;
        [Tooltip("True = right edge of A (curve opens to the right), false = left edge.")]
        public bool aRightEdge = true;
        [Tooltip("Panel that provides the horizontal edge.")]
        public RectTransform b;
        [Tooltip("True = top edge of B (curve opens upwards), false = bottom edge.")]
        public bool bTopEdge = true;
        public Image edge;
        public Image fill;

        private RectTransform rt;
        private DrivenRectTransformTracker tracker;
        private Rect lastA, lastB;
        private Vector2 lastParent;
        private bool dirty = true;

        public bool Visible { get; private set; }

        private static readonly Vector3[] corners = new Vector3[4];

        private void OnEnable()
        {
            rt = (RectTransform)transform;
            tracker.Clear();
            tracker.Add(this, rt, DrivenTransformProperties.All);
            dirty = true;
            UpdateJoin();
        }

        private void OnDisable() { tracker.Clear(); }

#if UNITY_EDITOR
        private void OnValidate() { dirty = true; }
#endif

        private void LateUpdate() { UpdateJoin(); }

        public void ForceUpdate() { dirty = true; UpdateJoin(); }

        private static Rect RectIn(RectTransform t, Transform space)
        {
            t.GetWorldCorners(corners);
            Vector2 mn = new Vector2(float.MaxValue, float.MaxValue), mx = new Vector2(float.MinValue, float.MinValue);
            for (int i = 0; i < 4; i++)
            {
                Vector2 p = space.InverseTransformPoint(corners[i]);
                mn = Vector2.Min(mn, p); mx = Vector2.Max(mx, p);
            }
            return Rect.MinMaxRect(mn.x, mn.y, mx.x, mx.y);
        }

        private static bool Inside(Rect r, float x, float y) => x > r.xMin && x < r.xMax && y > r.yMin && y < r.yMax;

        private void UpdateJoin()
        {
            if (rt == null) rt = (RectTransform)transform;
            var parent = rt.parent as RectTransform;
            if (a == null || b == null || style == null || parent == null) { Hide(); return; }

            Rect A = RectIn(a, parent), B = RectIn(b, parent);
            Vector2 ps = parent.rect.size;
            if (!dirty && A == lastA && B == lastB && ps == lastParent) return;
            dirty = false; lastA = A; lastB = B; lastParent = ps;

            float dx = aRightEdge ? 1f : -1f, dy = bTopEdge ? 1f : -1f;
            float vx = aRightEdge ? A.xMax : A.xMin, vy = bTopEdge ? B.yMax : B.yMin;
            const float e = 0.5f;
            bool aHidden = A.width < 1f || A.height < 1f, bHidden = B.width < 1f || B.height < 1f;
            bool valid = !aHidden && !bHidden
                && Inside(A, vx - dx * e, vy + dy * e)                       // A fills the quadrant beside the curve
                && Inside(B, vx + dx * e, vy - dy * e)                       // B fills the quadrant below/above it
                && !Inside(A, vx + dx * e, vy + dy * e) && !Inside(B, vx + dx * e, vy + dy * e)  // the curve's quadrant is open
                && (Inside(A, vx - dx * e, vy - dy * e) || Inside(B, vx - dx * e, vy - dy * e));
            float lenA = dy > 0f ? A.yMax - vy : vy - A.yMin;
            float lenB = dx > 0f ? B.xMax - vx : vx - B.xMin;
            float R = style.radius;
            float r = valid ? Mathf.Min(R, lenA * 0.5f, lenB * 0.5f) : 0f;
            if (r < 0.5f) { Hide(); return; }

            float k = r / R;
            rt.anchorMin = rt.anchorMax = parent.pivot;     // anchor point = parent's local origin
            rt.pivot = Vector2.zero;
            rt.sizeDelta = new Vector2(R, R);
            rt.anchoredPosition = new Vector2(vx, vy);
            rt.localRotation = Quaternion.identity;
            rt.localScale = new Vector3(dx * k, dy * k, 1f);
            Visible = true;
            float m = FrameStyle.JoinMargin;
            Fit(edge, m); Fit(fill, m);
        }

        private static void Fit(Image img, float m)
        {
            if (img == null) return;
            var r = img.rectTransform;
            var min = new Vector2(-m, -m);
            if (r.anchorMin != Vector2.zero || r.anchorMax != Vector2.one || r.offsetMin != min || r.offsetMax != Vector2.zero)
            {
                r.anchorMin = Vector2.zero; r.anchorMax = Vector2.one;
                r.offsetMin = min; r.offsetMax = Vector2.zero;
            }
        }

        private void Hide()
        {
            Visible = false;
            if (rt == null) return;
            if (rt.localScale != Vector3.zero) rt.localScale = Vector3.zero;
        }
    }
}
