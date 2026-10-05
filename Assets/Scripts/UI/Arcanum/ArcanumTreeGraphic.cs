using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace Orivilon.UI.Arcanum
{
    /// <summary>
    /// Kreslí spojnice stromu (radiální kmen → soustředný oblouk → uzel), pomocné
    /// půlkruhy a rysky jedním meshem. Mesh se přestaví jen při změně stavu
    /// (odemčení, otevření), ne každý snímek.
    /// </summary>
    [RequireComponent(typeof(CanvasRenderer))]
    public class ArcanumTreeGraphic : MaskableGraphic
    {
        private static readonly Color LockedColor = new Color32(0x4a, 0x4f, 0x4b, 0xff);
        private static readonly Color AvailableColor = new Color32(0xb4, 0xbb, 0xb1, 0xff);
        private static readonly Color GuideColor = new Color(0.62f, 0.68f, 0.64f, 0.12f);
        private static readonly Color JunctionColor = new Color32(0x6e, 0x75, 0x70, 0xb0);

        private readonly List<Vector2> path = new List<Vector2>(64);
        private readonly List<Vector2> junctions = new List<Vector2>();
        private readonly List<int> junctionParents = new List<int>();
        private List<Vector2>[] cachedPaths;

        public int highlightNode = -1;

        protected override void Awake()
        {
            base.Awake();
            raycastTarget = false;
        }

        private void EnsurePaths()
        {
            if (cachedPaths != null) return;
            var nodes = ArcanumData.Nodes;
            cachedPaths = new List<Vector2>[nodes.Count];
            junctions.Clear();
            junctionParents.Clear();
            for (int i = 1; i < nodes.Count; i++)
            {
                path.Clear();
                BuildConnection(nodes[i], path);
                cachedPaths[i] = new List<Vector2>(path);
            }
        }

        private static Vector2 Polar(float r, float a) => new Vector2(Mathf.Cos(a) * r, Mathf.Sin(a) * r);

        /// <summary>Stejná geometrie jako prototyp: kmen k rozcestí, oblouk po soustředné kružnici, pak k uzlu.</summary>
        private void BuildConnection(ArcanumNode n, List<Vector2> pts)
        {
            var nodes = ArcanumData.Nodes;
            var p = nodes[n.parent];
            float a = n.angle;
            if (p.id == 0)
            {
                pts.Add(Vector2.zero);
                pts.Add(Polar(210f, a));
                pts.Add(n.pos);
                return;
            }

            float pa = p.angle, pr = p.radius;
            float minPositive = float.MaxValue;
            foreach (int cid in p.children)
            {
                float d = nodes[cid].radius - pr;
                if (d > 55f && d < minPositive) minPositive = d;
            }
            float jr = Mathf.Abs(n.radius - pr) < 65f ? pr
                : pr + (minPositive < float.MaxValue ? minPositive * 0.48f : (n.radius - pr) * 0.48f);

            if (p.children.Count > 1 && Mathf.Abs(a - pa) > 0.025f)
            {
                junctions.Add(Polar(jr, pa));
                junctionParents.Add(p.id);
            }

            pts.Add(p.pos);
            pts.Add(Polar(jr, pa));
            int steps = Mathf.Max(2, Mathf.CeilToInt(Mathf.Abs(a - pa) * jr / 14f));
            for (int s = 1; s <= steps; s++)
                pts.Add(Polar(jr, Mathf.Lerp(pa, a, s / (float)steps)));
            pts.Add(n.pos);
        }

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            EnsurePaths();
            var nodes = ArcanumData.Nodes;

            // Pomocné soustředné půlkruhy a rysky po obvodu.
            for (float r = 450f; r < 1800f; r += 185f)
                Arc(vh, r, 0f, Mathf.PI, 1.6f, GuideColor);
            for (int i = 0; i <= 72; i++)
            {
                float a = i * Mathf.PI / 72f;
                Line(vh, Polar(1770f, a), Polar(i % 6 == 0 ? 1805f : 1782f, a), 1.6f, GuideColor * 2f);
            }

            // Zamčené nejdřív, odemčené navrch.
            for (int pass = 0; pass < 3; pass++)
            {
                for (int i = 1; i < nodes.Count; i++)
                {
                    var n = nodes[i];
                    bool done = ArcanumProgress.IsUnlocked(n.id);
                    bool avail = !done && ArcanumProgress.IsUnlocked(n.parent);
                    int state = done ? 2 : avail ? 1 : 0;
                    if (state != pass) continue;
                    Color c; float w;
                    if (done)
                    {
                        c = Color.Lerp(ArcanumData.Categories[n.category].color, Color.white, 0.45f);
                        w = n.id == highlightNode ? 9f : 5f;
                    }
                    else if (avail) { c = AvailableColor; w = 3f; }
                    else { c = LockedColor; w = 2f; }
                    Polyline(vh, cachedPaths[i], w, c);
                }
            }

            for (int j = 0; j < junctions.Count; j++)
            {
                bool lit = ArcanumProgress.IsUnlocked(junctionParents[j]);
                Dot(vh, junctions[j], lit ? 6f : 4.5f, lit ? Color.white : JunctionColor);
            }
        }

        private static void Arc(VertexHelper vh, float r, float a0, float a1, float w, Color c)
        {
            int steps = Mathf.CeilToInt((a1 - a0) * r / 20f);
            Vector2 prev = Polar(r, a0);
            for (int s = 1; s <= steps; s++)
            {
                Vector2 cur = Polar(r, Mathf.Lerp(a0, a1, s / (float)steps));
                Line(vh, prev, cur, w, c);
                prev = cur;
            }
        }

        private static void Polyline(VertexHelper vh, List<Vector2> pts, float w, Color c)
        {
            for (int i = 1; i < pts.Count; i++)
                Line(vh, pts[i - 1], pts[i], w, c);
        }

        /// <summary>Úsečka jako quad, prodloužená o půl šířky – zakryje spoje bez extra geometrie.</summary>
        private static void Line(VertexHelper vh, Vector2 a, Vector2 b, float w, Color c)
        {
            Vector2 d = b - a;
            float len = d.magnitude;
            if (len < 0.01f) return;
            d /= len;
            Vector2 n = new Vector2(-d.y, d.x) * (w * 0.5f);
            Vector2 e = d * (w * 0.5f);
            int i = vh.currentVertCount;
            vh.AddVert(a - e - n, c, Vector4.zero);
            vh.AddVert(a - e + n, c, Vector4.zero);
            vh.AddVert(b + e + n, c, Vector4.zero);
            vh.AddVert(b + e - n, c, Vector4.zero);
            vh.AddTriangle(i, i + 1, i + 2);
            vh.AddTriangle(i, i + 2, i + 3);
        }

        private static void Dot(VertexHelper vh, Vector2 p, float r, Color c)
        {
            int center = vh.currentVertCount;
            vh.AddVert(p, c, Vector4.zero);
            const int seg = 10;
            for (int s = 0; s <= seg; s++)
            {
                float a = s * Mathf.PI * 2f / seg;
                vh.AddVert(p + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * r, c, Vector4.zero);
                if (s > 0) vh.AddTriangle(center, center + s, center + s + 1);
            }
        }
    }
}
