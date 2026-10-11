using UnityEngine;
using UnityEngine.UI;

namespace Orivilon.UI.Refresh
{
    /// <summary>
    /// One rectangular panel of a continuous frame. Move/resize this RectTransform in the
    /// Scene view; its "Edge" child (rim + glow) and "Fill" child follow automatically.
    /// A piece narrower or lower than 1 px is hidden (used for collapsed states).
    /// </summary>
    [ExecuteAlways, DisallowMultipleComponent, RequireComponent(typeof(RectTransform))]
    public class FramePiece : MonoBehaviour
    {
        public FrameStyle style;
        public Image edge;
        public Image fill;

        private RectTransform rt;
        private bool hidden;

        private void OnEnable() { Refresh(); }
        private void OnRectTransformDimensionsChange() { if (isActiveAndEnabled) Refresh(); }

#if UNITY_EDITOR
        private void OnValidate() { if (isActiveAndEnabled) Refresh(); }
#endif

        public void Refresh()
        {
            if (rt == null) rt = (RectTransform)transform;
            Vector2 s = rt.rect.size;
            hidden = s.x < 1f || s.y < 1f;
            Cull(edge, hidden);
            Cull(fill, hidden);
            if (edge != null && style != null)
            {
                float e = style.EdgeExtent;
                var r = edge.rectTransform;
                var min = new Vector2(-e, -e);
                if (r.offsetMin != min || r.offsetMax != -min || r.anchorMin != Vector2.zero || r.anchorMax != Vector2.one)
                {
                    r.anchorMin = Vector2.zero; r.anchorMax = Vector2.one;
                    r.offsetMin = min; r.offsetMax = -min;
                }
            }
        }

        public bool Hidden => hidden;

        private static void Cull(Graphic g, bool c)
        {
            if (g != null && g.canvasRenderer.cull != c) g.canvasRenderer.cull = c;
        }
    }
}
