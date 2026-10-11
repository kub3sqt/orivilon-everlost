using UnityEngine;

namespace Orivilon.UI.Refresh
{
    /// <summary>
    /// Keeps a content container glued to a frame piece, so the content moves on exactly the same
    /// transition as its black background (same frame, same easing, same unscaled time).
    ///
    /// <para><b>Translate</b>: the container keeps a fixed offset from a corner of the piece – it slides
    /// rigidly with it (crafting grid, output slot and arrow).<br/>
    /// <b>Rect</b>: the container's rectangle is the piece's rectangle minus insets; put a RectMask2D on it
    /// and anchor the children to the edge the panel grows from – the content slides with that edge and is
    /// clipped to the background (inventory grid, stat labels).</para>
    ///
    /// <para>The offset/insets are measured in the open layout; edit them here or move the piece.
    /// Runs after UiFrame (execution order) and also in the editor, so what you see is what plays.</para>
    /// </summary>
    [ExecuteAlways, DefaultExecutionOrder(1000), DisallowMultipleComponent, RequireComponent(typeof(RectTransform))]
    public class FrameFollower : MonoBehaviour
    {
        public enum Mode { Translate, Rect }

        public RectTransform piece;
        public Mode mode = Mode.Translate;
        [Tooltip("Translate: corner of the piece the content is attached to (0,1 = top-left).")]
        public Vector2 corner = new Vector2(0f, 1f);
        [Tooltip("Translate: offset of this object's same corner from the piece's corner, in the piece's local units (corner-to-corner, so a stretched container keeps its content in place at any aspect ratio).")]
        public Vector2 offset;
        [Tooltip("Rect: insets from the piece's edges (left, bottom, right, top) in the piece's local units.")]
        public Vector4 insets;
        [SerializeField, HideInInspector] private int cornerVersion;   // 0 = legacy pivot offset, 1 = corner-to-corner
        public bool CornerToCorner => cornerVersion >= 1;

        private RectTransform rt;
        private DrivenRectTransformTracker tracker;
        private static readonly Vector3[] c = new Vector3[4];

        private void OnEnable()
        {
            rt = (RectTransform)transform;
            tracker.Clear();
            if (mode == Mode.Rect)
                tracker.Add(this, rt, DrivenTransformProperties.AnchoredPosition | DrivenTransformProperties.SizeDelta | DrivenTransformProperties.Anchors);
            else
                tracker.Add(this, rt, DrivenTransformProperties.AnchoredPosition);
            Follow();
        }

        private void OnDisable() { tracker.Clear(); }

        private void LateUpdate() { Follow(); }

        public void Follow()
        {
            if (piece == null || rt == null) return;
            var parent = rt.parent;
            if (parent == null) return;
            Rect pr = piece.rect;
            if (mode == Mode.Translate)
            {
                Vector3 cornerWorld = piece.TransformPoint(new Vector3(pr.xMin + pr.width * corner.x, pr.yMin + pr.height * corner.y, 0f));
                Vector3 target = cornerWorld + piece.TransformVector(new Vector3(offset.x, offset.y, 0f));
                Vector3 delta = target - (CornerToCorner ? OwnCorner() : rt.position);
                if (delta.sqrMagnitude > 1e-8f) rt.position += delta;
                return;
            }
            Vector3 wMin = piece.TransformPoint(new Vector3(pr.xMin + insets.x, pr.yMin + insets.y, 0f));
            Vector3 wMax = piece.TransformPoint(new Vector3(pr.xMax - insets.z, pr.yMax - insets.w, 0f));
            Vector2 a = parent.InverseTransformPoint(wMin), b = parent.InverseTransformPoint(wMax);
            Vector2 mn = Vector2.Min(a, b), mx = Vector2.Max(a, b);
            Vector3 s = rt.localScale;
            Vector2 size = new Vector2(Mathf.Max(0f, (mx.x - mn.x) / Mathf.Max(1e-5f, Mathf.Abs(s.x))), Mathf.Max(0f, (mx.y - mn.y) / Mathf.Max(1e-5f, Mathf.Abs(s.y))));
            Vector2 pivotLocal = mn + Vector2.Scale(rt.pivot, mx - mn);
            Vector2 parentCenter = parent is RectTransform prt ? prt.rect.center : Vector2.zero;
            var half = new Vector2(0.5f, 0.5f);
            if (rt.anchorMin != half || rt.anchorMax != half) { rt.anchorMin = half; rt.anchorMax = half; }
            if (rt.sizeDelta != size) rt.sizeDelta = size;
            Vector2 ap = pivotLocal - parentCenter;
            if (rt.anchoredPosition != ap) rt.anchoredPosition = ap;
        }

        private Vector3 OwnCorner()
        {
            Rect r = rt.rect;
            return rt.TransformPoint(new Vector3(r.xMin + r.width * corner.x, r.yMin + r.height * corner.y, 0f));
        }

        /// <summary>Editor helper: converts a legacy pivot offset to a corner-to-corner offset (same placement).</summary>
        public bool UpgradeToCorner()
        {
            if (CornerToCorner || piece == null) return false;
            if (rt == null) rt = (RectTransform)transform;
            Vector3 d = rt.TransformVector(Vector2.Scale(corner - rt.pivot, rt.rect.size));
            Vector3 local = piece.InverseTransformVector(d);
            offset += new Vector2(local.x, local.y);
            cornerVersion = 1;
            return true;
        }

        /// <summary>Editor helper: measure offset from the current placement (Translate mode).</summary>
        public void Capture()
        {
            if (piece == null) return;
            if (rt == null) rt = (RectTransform)transform;
            Rect pr = piece.rect;
            Vector3 cornerWorld = piece.TransformPoint(new Vector3(pr.xMin + pr.width * corner.x, pr.yMin + pr.height * corner.y, 0f));
            Vector3 local = piece.InverseTransformVector(OwnCorner() - cornerWorld);
            offset = new Vector2(local.x, local.y);
            cornerVersion = 1;
        }
    }
}
