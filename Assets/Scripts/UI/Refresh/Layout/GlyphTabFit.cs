using TMPro;
using UnityEngine;

namespace Orivilon.UI.Refresh
{
    /// <summary>
    /// Fits a corner tab (BETA / brand / top links / pause Settings) to the RENDERED glyphs of its labels.
    /// This RectTransform becomes the tab's visible size (frame pieces fit their width to it through
    /// UiFrame's fit), and the labels (and optional hit buttons) are placed so the glyph bounds sit at equal
    /// padding from the screen edge, the tab edges and each other, on one shared baseline.
    ///
    /// <para>Padding: <see cref="pad"/> &lt; 0 = automatic, (tabHeight − glyph height) / 2, so top/bottom and
    /// outer left/right gaps are equal. <see cref="gap"/> &lt; 0 = 2 × pad between neighbouring labels.
    /// Re-measures when a text, font or size changes (e.g. the version string set at runtime).</para>
    /// </summary>
    [ExecuteAlways, DefaultExecutionOrder(-120), RequireComponent(typeof(RectTransform))]
    public class GlyphTabFit : MonoBehaviour
    {
        [Tooltip("Screen corner the tab is attached to: (0,0) bottom-left, (1,0) bottom-right, (1,1) top-right.")]
        public Vector2 corner = Vector2.zero;
        [Tooltip("Labels from the outer screen edge inwards… listed left to right.")]
        public TMP_Text[] labels = new TMP_Text[0];
        [Tooltip("Optional clickable rects (same order as labels): sized to each label's cell over the full tab height.")]
        public RectTransform[] hits = new RectTransform[0];
        public float tabHeight = 50f;
        [Tooltip("< 0 = automatic (equal vertical/outer padding).")]
        public float pad = -1f;
        [Tooltip("< 0 = 2 × padding.")]
        public float gap = -1f;

        /// <summary>Read-outs for QA: padding used and the measured glyph box per label (tab-local, bottom-left origin).</summary>
        public float UsedPad { get; private set; }
        public Rect[] GlyphBoxes { get; private set; } = new Rect[0];

        private RectTransform rt;
        private string[] lastText = new string[0];
        private Bounds[] bounds = new Bounds[0];
        private float[] lastSize = new float[0];
        private bool[] measured = new bool[0];

        private void OnEnable() { rt = (RectTransform)transform; lastText = new string[0]; Fit(); }
        private void Update() => Fit();
        private void LateUpdate() => Fit();

        private bool Measure()
        {
            int n = labels.Length;
            bool changed = lastText.Length != n;
            if (changed) { lastText = new string[n]; bounds = new Bounds[n]; lastSize = new float[n]; measured = new bool[n]; }
            for (int i = 0; i < n; i++)
            {
                var t = labels[i];
                if (t == null) continue;
                if (!changed && lastText[i] == t.text && Mathf.Approximately(lastSize[i], t.fontSize)) continue;
                t.ForceMeshUpdate(true);
                var b = InkBounds(t);
                // A label that has never been active (e.g. a hidden screen in edit mode) yields no mesh: use the
                // font's preferred size as a provisional width and keep re-measuring until real glyphs exist.
                bool ok = b.size.x > 0.01f || string.IsNullOrEmpty(t.text);
                if (!ok) { var pv = t.GetPreferredValues(t.text); b = new Bounds(Vector3.zero, new Vector3(pv.x, t.fontSize * 0.72f, 0f)); }
                bounds[i] = b; measured[i] = ok;
                lastText[i] = ok ? t.text : null; lastSize[i] = t.fontSize;
                changed = true;
            }
            return changed;
        }

        /// <summary>Tight bounds of the visible glyph ink (label-local). TMP's textBounds spans the line's
        /// ascender/descender and advances, which would leave unequal visual padding.</summary>
        private static Bounds InkBounds(TMP_Text t)
        {
            var ti = t.textInfo;
            bool any = false; Vector2 mn = Vector2.zero, mx = Vector2.zero;
            for (int c = 0; ti != null && c < ti.characterCount; c++)
            {
                var ci = ti.characterInfo[c];
                if (!ci.isVisible || ci.textElement == null || ci.textElement.glyph == null) continue;
                var m = ci.textElement.glyph.metrics; float k = ci.scale;
                float x0 = ci.origin + m.horizontalBearingX * k, x1 = x0 + m.width * k;
                float y1 = ci.baseLine + m.horizontalBearingY * k, y0 = y1 - m.height * k;
                if (!any) { mn = new Vector2(x0, y0); mx = new Vector2(x1, y1); any = true; }
                else { mn = Vector2.Min(mn, new Vector2(x0, y0)); mx = Vector2.Max(mx, new Vector2(x1, y1)); }
            }
            if (!any) return new Bounds();
            return new Bounds((Vector3)((mn + mx) * 0.5f), (Vector3)(mx - mn));
        }

        public void Fit()
        {
            if (rt == null) rt = (RectTransform)transform;
            if (labels == null || labels.Length == 0) return;
            Measure();
            int n = labels.Length;
            float h = 0f, sum = 0f;
            for (int i = 0; i < n; i++) { if (labels[i] == null) continue; h = Mathf.Max(h, bounds[i].size.y); sum += bounds[i].size.x; }
            float p = pad >= 0f ? pad : Mathf.Max(4f, (tabHeight - h) * 0.5f);
            float s = gap >= 0f ? gap : 2f * p;
            UsedPad = p;
            float width = 2f * p + sum + s * (n - 1);

            // the tab rect itself, at the screen corner
            var pivot = corner;
            if (rt.anchorMin != corner || rt.anchorMax != corner) { rt.anchorMin = corner; rt.anchorMax = corner; }
            if (rt.pivot != pivot) rt.pivot = pivot;
            var size = new Vector2(width, tabHeight);
            if ((rt.sizeDelta - size).sqrMagnitude > 1e-4f) rt.sizeDelta = size;
            if (rt.anchoredPosition.sqrMagnitude > 1e-6f) rt.anchoredPosition = Vector2.zero;

            // shared baseline: every label gets the same vertical shift (union of glyph boxes centred)
            float yMin = float.MaxValue, yMax = float.MinValue;
            for (int i = 0; i < n; i++) { if (labels[i] == null) continue; yMin = Mathf.Min(yMin, bounds[i].min.y); yMax = Mathf.Max(yMax, bounds[i].max.y); }

            if (GlyphBoxes.Length != n) GlyphBoxes = new Rect[n];
            Vector3[] wc = new Vector3[4];
            rt.GetWorldCorners(wc);   // tab rect in world space (0 = bottom-left)
            Vector3 origin = wc[0];
            Vector3 ux = (wc[3] - wc[0]) / Mathf.Max(1e-4f, width), uy = (wc[1] - wc[0]) / Mathf.Max(1e-4f, tabHeight);
            float x = p;
            for (int i = 0; i < n; i++)
            {
                var t = labels[i];
                if (t == null) continue;
                float w = bounds[i].size.x;
                // clickable cell: half the gap on each side (outer cells reach the tab ends)
                if (hits != null && i < hits.Length && hits[i] != null)
                {
                    float c0 = i == 0 ? 0f : x - s * 0.5f, c1 = i == n - 1 ? width : x + w + s * 0.5f;
                    PlaceWorld(hits[i], origin + ux * c0, origin + ux * c1 + uy * tabHeight);
                }
                if (!measured[i]) { GlyphBoxes[i] = new Rect(x, 0f, w, 0f); x += w + s; continue; }
                // glyph box target (tab-local): x .. x+w, vertically the shared union centred in the tab
                float gy0 = (tabHeight - (yMax - yMin)) * 0.5f + (bounds[i].min.y - yMin);
                Vector3 wantCenter = origin + ux * (x + w * 0.5f) + uy * (gy0 + bounds[i].size.y * 0.5f);
                Vector3 haveCenter = t.transform.TransformPoint(bounds[i].center);
                Vector3 d = wantCenter - haveCenter;
                if (d.sqrMagnitude > 1e-6f * Mathf.Max(1f, ux.sqrMagnitude)) t.transform.position += d;
                GlyphBoxes[i] = new Rect(x, gy0, w, bounds[i].size.y);
                x += w + s;
            }
        }

        private static void PlaceWorld(RectTransform r, Vector3 min, Vector3 max)
        {
            var parent = r.parent as RectTransform;
            if (parent == null) return;
            Vector2 a = parent.InverseTransformPoint(min), b = parent.InverseTransformPoint(max);
            Vector2 mn = Vector2.Min(a, b), mx = Vector2.Max(a, b);
            var half = new Vector2(0.5f, 0.5f);
            if (r.anchorMin != half || r.anchorMax != half) { r.anchorMin = half; r.anchorMax = half; }
            Vector3 s = r.localScale;
            Vector2 size = new Vector2((mx.x - mn.x) / Mathf.Max(1e-4f, Mathf.Abs(s.x)), (mx.y - mn.y) / Mathf.Max(1e-4f, Mathf.Abs(s.y)));
            if ((r.sizeDelta - size).sqrMagnitude > 1e-4f) r.sizeDelta = size;
            Vector2 pivotLocal = mn + Vector2.Scale(r.pivot, mx - mn);
            Vector2 ap = pivotLocal - parent.rect.center;
            if ((r.anchoredPosition - ap).sqrMagnitude > 1e-4f) r.anchoredPosition = ap;
        }
    }
}
