using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace Orivilon.UI.Arcanum
{
    /// <summary>
    /// Connections of the Arcanum tree (radial trunk → concentric arc → node), guide half-circles
    /// and ticks, drawn as one mesh. Node positions are read from the node objects in the prefab,
    /// so moving a node in Prefab Mode re-routes its connections immediately (editor preview).
    /// Colours and widths are editable here; the mesh is rebuilt only when progress or node
    /// positions change.
    /// </summary>
    [ExecuteAlways, RequireComponent(typeof(CanvasRenderer))]
    public class ArcanumTreeGraphic : MaskableGraphic
    {
        [Header("Nodes (index = node id)")]
        public List<ArcanumNodeView> nodes = new List<ArcanumNodeView>();

        [Header("Look")]
        public Color lockedColor = new Color32(0x4a, 0x4f, 0x4b, 0xff);
        public Color availableColor = new Color32(0xb4, 0xbb, 0xb1, 0xff);
        public Color guideColor = new Color(0.62f, 0.68f, 0.64f, 0.12f);
        public Color junctionColor = new Color32(0x6e, 0x75, 0x70, 0xb0);
        public float lockedWidth = 2f, availableWidth = 3f, researchedWidth = 5f, highlightWidth = 9f;
        public float guideStart = 450f, guideEnd = 1800f, guideStep = 185f, tickRadius = 1770f;

        [System.NonSerialized] public int highlightNode = -1;

        private readonly List<Vector2> path = new List<Vector2>(64);
        private readonly List<Vector2> junctions = new List<Vector2>();
        private readonly List<int> junctionParents = new List<int>();
        private List<Vector2>[] cachedPaths;
        private Vector2[] positions;
        private int checkFrame;

        protected override void Awake()
        {
            base.Awake();
            raycastTarget = false;
        }

        /// <summary>Node position in this graphic's local space.</summary>
        public Vector2 Pos(int id)
        {
            if (id < 0 || id >= nodes.Count || nodes[id] == null) return Vector2.zero;
            return transform.InverseTransformPoint(nodes[id].transform.position);
        }

        private void Update()
        {
            // Cheap check (every few frames) whether a node moved – e.g. while editing the prefab.
            if (++checkFrame % 10 != 0 || positions == null || positions.Length != nodes.Count) { if (positions == null || positions.Length != nodes.Count) Invalidate(); return; }
            for (int i = 0; i < nodes.Count; i++)
                if ((Pos(i) - positions[i]).sqrMagnitude > 0.01f) { Invalidate(); return; }
        }

        public void Invalidate()
        {
            cachedPaths = null;
            SetVerticesDirty();
        }

        private void EnsurePaths()
        {
            var data = ArcanumData.Nodes;
            int count = Mathf.Min(data.Count, nodes.Count);
            if (cachedPaths != null && cachedPaths.Length == count) return;
            positions = new Vector2[nodes.Count];
            for (int i = 0; i < nodes.Count; i++) positions[i] = Pos(i);
            cachedPaths = new List<Vector2>[count];
            junctions.Clear();
            junctionParents.Clear();
            for (int i = 1; i < count; i++)
            {
                path.Clear();
                BuildConnection(data[i], path, data);
                cachedPaths[i] = new List<Vector2>(path);
            }
        }

        private static Vector2 Polar(float r, float a) => new Vector2(Mathf.Cos(a) * r, Mathf.Sin(a) * r);
        private float Angle(int id) { var p = positions[id]; return Mathf.Atan2(p.y, p.x); }
        private float Radius(int id) => positions[id].magnitude;

        /// <summary>Same geometry as the prototype: trunk to the junction, along a concentric arc, then to the node.</summary>
        private void BuildConnection(ArcanumNode n, List<Vector2> pts, IReadOnlyList<ArcanumNode> data)
        {
            if (n.parent < 0 || n.parent >= positions.Length) return;
            float a = Angle(n.id);
            if (n.parent == 0)
            {
                pts.Add(positions[0]);
                pts.Add(positions[0] + Polar(210f, a));
                pts.Add(positions[n.id]);
                return;
            }

            var p = data[n.parent];
            float pa = Angle(p.id), pr = Radius(p.id), nr = Radius(n.id);
            float minPositive = float.MaxValue;
            foreach (int cid in p.children)
            {
                if (cid >= positions.Length) continue;
                float d = Radius(cid) - pr;
                if (d > 55f && d < minPositive) minPositive = d;
            }
            float jr = Mathf.Abs(nr - pr) < 65f ? pr
                : pr + (minPositive < float.MaxValue ? minPositive * 0.48f : (nr - pr) * 0.48f);

            if (p.children.Count > 1 && Mathf.Abs(a - pa) > 0.025f)
            {
                junctions.Add(Polar(jr, pa));
                junctionParents.Add(p.id);
            }

            pts.Add(positions[p.id]);
            pts.Add(Polar(jr, pa));
            int steps = Mathf.Max(2, Mathf.CeilToInt(Mathf.Abs(a - pa) * jr / 14f));
            for (int s = 1; s <= steps; s++)
                pts.Add(Polar(jr, Mathf.Lerp(pa, a, s / (float)steps)));
            pts.Add(positions[n.id]);
        }

        private static bool Unlocked(int id) => Application.isPlaying ? ArcanumProgress.IsUnlocked(id) : id == 0;

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            if (nodes == null || nodes.Count == 0) return;
            EnsurePaths();
            var data = ArcanumData.Nodes;

            for (float r = guideStart; r < guideEnd; r += guideStep)
                Arc(vh, r, 0f, Mathf.PI, 1.6f, guideColor);
            for (int i = 0; i <= 72; i++)
            {
                float a = i * Mathf.PI / 72f;
                Line(vh, Polar(tickRadius, a), Polar(i % 6 == 0 ? tickRadius + 35f : tickRadius + 12f, a), 1.6f, guideColor * 2f);
            }

            // locked first, researched on top
            for (int pass = 0; pass < 3; pass++)
            {
                for (int i = 1; i < cachedPaths.Length; i++)
                {
                    var n = data[i];
                    bool done = Unlocked(n.id);
                    bool avail = !done && Unlocked(n.parent);
                    int state = done ? 2 : avail ? 1 : 0;
                    if (state != pass || cachedPaths[i] == null) continue;
                    Color c; float w;
                    if (done)
                    {
                        c = Color.Lerp(ArcanumData.Categories[n.category].color, Color.white, 0.45f);
                        w = n.id == highlightNode ? highlightWidth : researchedWidth;
                    }
                    else if (avail) { c = availableColor; w = availableWidth; }
                    else { c = lockedColor; w = lockedWidth; }
                    Polyline(vh, cachedPaths[i], w, c);
                }
            }

            for (int j = 0; j < junctions.Count; j++)
            {
                bool lit = Unlocked(junctionParents[j]);
                Dot(vh, junctions[j], lit ? 6f : 4.5f, lit ? Color.white : junctionColor);
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

        /// <summary>Segment as a quad, extended by half the width so joints close without extra geometry.</summary>
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
