using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UI;
using static Orivilon.UI.Refresh.EditorTools.UiAuthoringKit;

namespace Orivilon.UI.Refresh.EditorTools
{
    /// <summary>
    /// Generates the frame art (from <see cref="FrameStyle"/>), the FramePanel / FrameJoin building
    /// block prefabs, and complete frame prefabs from a layout spec (pieces + named states). Inward
    /// corners are detected from the states and saved as FrameJoin objects.
    /// </summary>
    public static class FrameAuthoring
    {
        public const int FillBit = 128, EdgeBit = 64;
        public const string PanelPrefabPath = PrefabDir + "/Frame/FramePanel.prefab";
        public const string JoinPrefabPath = PrefabDir + "/Frame/FrameJoin.prefab";

        // ── style + sprites ──────────────────────────────────────────────────

        public static FrameStyle EnsureStyle()
        {
            EnsureFolder(Root);
            var s = AssetDatabase.LoadAssetAtPath<FrameStyle>(StylePath);
            if (s == null) { s = ScriptableObject.CreateInstance<FrameStyle>(); AssetDatabase.CreateAsset(s, StylePath); }
            if (s.panelFill == null || s.panelEdge == null || s.joinFill == null || s.joinEdge == null || s.fillMaterial == null) Regenerate(s);
            return s;
        }

        /// <summary>(Re)creates the four frame sprites and the three stencil materials.</summary>
        public static void Regenerate(FrameStyle s)
        {
            float R = s.radius, rimH = s.rimWidth * 0.5f, glowH = s.glowWidth * 0.5f, E = s.EdgeExtent, m = FrameStyle.JoinMargin;

            // Fill: contour = texture border, fill stops half a rim inside it
            {
                int T = 64;
                var tex = NewTex(T, T);
                Sample(tex, (x, y) => { float d = RoundRectDist(x, y, T, T, 0f, R); return d < -rimH ? s.fillColor : Color.clear; });
                float b = Mathf.Ceil(R) + 2f;
                s.panelFill = SaveSprite("Frame_PanelFill", tex, new Vector4(b, b, b, b));
            }
            // Edge: contour inset by E from the texture border
            {
                int T = Mathf.Max(64, Mathf.CeilToInt(2f * (R + E) + 8f));
                var tex = NewTex(T, T);
                Sample(tex, (x, y) => Band(s, RoundRectDist(x, y, T, T, E, R)));
                float b = Mathf.Ceil(R + E) + 2f;
                s.panelEdge = SaveSprite("Frame_PanelEdge", tex, new Vector4(b, b, b, b));
            }
            // Join (concave corner): square [-m, R] with the curve's centre at (R, R)
            {
                int T = 64;
                float scale = T / (R + m);
                var tf = NewTex(T, T);
                Sample(tf, (x, y) =>
                {
                    float px = -m + x / scale, py = -m + y / scale;
                    float rho = new Vector2(px - R, py - R).magnitude;
                    return rho > R + rimH ? s.fillColor : Color.clear;
                });
                s.joinFill = SaveSprite("Frame_JoinFill", tf, Vector4.zero);
                var te = NewTex(T, T);
                Sample(te, (x, y) =>
                {
                    float px = -m + x / scale, py = -m + y / scale;
                    float rho = new Vector2(px - R, py - R).magnitude;
                    return Band(s, R - rho);
                });
                s.joinEdge = SaveSprite("Frame_JoinEdge", te, Vector4.zero);
            }
            s.fillMaterial = StencilMaterial("UIFrameFill", CompareFunction.Always, FillBit, StencilOp.Replace, 255, FillBit, true, true);
            s.edgeMaterial = StencilMaterial("UIFrameEdge", CompareFunction.Greater, EdgeBit, StencilOp.Replace, FillBit | EdgeBit, EdgeBit, true, true);
            s.resetMaterial = StencilMaterial("UIFrameStencilReset", CompareFunction.Always, 0, StencilOp.Replace, 255, FillBit | EdgeBit, false, false);
            EditorUtility.SetDirty(s);
            AssetDatabase.SaveAssets();
        }

        /// <summary>Colour of the rim/glow band at signed distance d outside the contour.</summary>
        private static Color Band(FrameStyle s, float d)
        {
            float rimH = s.rimWidth * 0.5f, glowH = s.glowWidth * 0.5f;
            if (d < -rimH - 1f) return Color.clear;
            if (d < 0f)
            {
                // inner half of the rim, as it looked over the black fill
                Color c = s.rimColor * s.rimColor.a + s.fillColor * (1f - s.rimColor.a); c.a = 1f; return c;
            }
            if (d < rimH)
            {
                float a = s.rimColor.a + s.glowColor.a * (1f - s.rimColor.a);
                Color c = (s.rimColor * s.rimColor.a + s.glowColor * s.glowColor.a * (1f - s.rimColor.a)) / Mathf.Max(1e-4f, a);
                c.a = a; return c;
            }
            if (d < glowH) return s.glowColor;
            return Color.clear;
        }

        private static float RoundRectDist(float x, float y, int w, int h, float inset, float r)
        {
            float hw = w * 0.5f - inset, hh = h * 0.5f - inset;
            Vector2 p = new Vector2(x - w * 0.5f, y - h * 0.5f);
            Vector2 q = new Vector2(Mathf.Abs(p.x) - (hw - r), Mathf.Abs(p.y) - (hh - r));
            return new Vector2(Mathf.Max(q.x, 0f), Mathf.Max(q.y, 0f)).magnitude + Mathf.Min(Mathf.Max(q.x, q.y), 0f) - r;
        }

        /// <summary>4×4 supersampled, premultiplied average.</summary>
        private static void Sample(Texture2D tex, System.Func<float, float, Color> f)
        {
            int W = tex.width, H = tex.height, n = 4;
            var px = new Color[W * H];
            for (int y = 0; y < H; y++)
            for (int x = 0; x < W; x++)
            {
                Color acc = Color.clear;
                for (int j = 0; j < n; j++)
                for (int i = 0; i < n; i++)
                {
                    Color c = f(x + (i + 0.5f) / n, y + (j + 0.5f) / n);
                    acc += new Color(c.r * c.a, c.g * c.a, c.b * c.a, c.a);
                }
                acc /= n * n;
                px[y * W + x] = acc.a > 1e-5f ? new Color(acc.r / acc.a, acc.g / acc.a, acc.b / acc.a, acc.a) : Color.clear;
            }
            tex.SetPixels(px); tex.Apply(false, false);
        }

        // ── building block prefabs ───────────────────────────────────────────

        public static void EnsureBlockPrefabs(FrameStyle s)
        {
            EnsureFolder(PrefabDir + "/Frame");
            {
                var root = new GameObject("FramePanel", typeof(RectTransform));
                root.layer = 5;
                var edge = NewImage("Edge", root.transform, s.panelEdge, Color.white, true);
                edge.material = s.edgeMaterial;
                var fill = NewImage("Fill", root.transform, s.panelFill, Color.white, true);
                fill.material = s.fillMaterial;
                Stretch(fill.rectTransform);
                var p = root.AddComponent<FramePiece>();
                p.style = s; p.edge = edge; p.fill = fill;
                p.Refresh();
                ((RectTransform)root.transform).sizeDelta = new Vector2(200f, 120f);
                PrefabUtility.SaveAsPrefabAsset(root, PanelPrefabPath);
                Object.DestroyImmediate(root);
            }
            {
                var root = new GameObject("FrameJoin", typeof(RectTransform));
                root.layer = 5;
                var edge = NewImage("Edge", root.transform, s.joinEdge, Color.white, false);
                edge.material = s.edgeMaterial;
                var fill = NewImage("Fill", root.transform, s.joinFill, Color.white, false);
                fill.material = s.fillMaterial;
                foreach (var img in new[] { edge, fill })
                {
                    var r = img.rectTransform; r.anchorMin = Vector2.zero; r.anchorMax = Vector2.one;
                    r.offsetMin = new Vector2(-FrameStyle.JoinMargin, -FrameStyle.JoinMargin); r.offsetMax = Vector2.zero;
                }
                var j = root.AddComponent<FrameJoin>();
                j.style = s; j.edge = edge; j.fill = fill;
                PrefabUtility.SaveAsPrefabAsset(root, JoinPrefabPath);
                Object.DestroyImmediate(root);
            }
        }

        // ── frame spec ───────────────────────────────────────────────────────

        public class Box
        {
            public Vector2 aMin, aMax, oMin, oMax;
            public UiFrame.FitSide fit;
            public string fitKey;   // resolved against the scene (fitTo)
            public float fitPad;
            public Box(C x0, C y0, C x1, C y1) { aMin = new Vector2(x0.k, y0.k); aMax = new Vector2(x1.k, y1.k); oMin = new Vector2(x0.c, y0.c); oMax = new Vector2(x1.c, y1.c); }
            public Box Fit(UiFrame.FitSide side, string key, float pad, float previewWidth)
            {
                fit = side; fitKey = key; fitPad = pad;
                if (side == UiFrame.FitSide.Right) oMax.x = pad + previewWidth; else oMin.x = -(pad + previewWidth);
                return this;
            }
        }

        public class Piece
        {
            public string name, guide;
            public readonly Dictionary<string, Box> boxes = new Dictionary<string, Box>();
        }

        public class Guide
        {
            public string name;
            public float left, right, maxWidth;
            public Box box;   // vertical part (horizontal handled by MaxWidthFitter)
        }

        public class Spec
        {
            public string name;
            public List<string> states = new List<string>();
            public string initialState;
            public readonly List<Piece> pieces = new List<Piece>();
            public readonly List<Guide> guides = new List<Guide>();

            public Piece P(string name, string guide = null) { var p = new Piece { name = name, guide = guide }; pieces.Add(p); return p; }
            public void All(Piece p, Box b) { foreach (var s in states) p.boxes[s] = b; }
        }

        // ── evaluation & inward corner detection ─────────────────────────────

        private static Rect Eval(Box b, Rect parent)
        {
            Vector2 mn = parent.min + Vector2.Scale(b.aMin, parent.size) + b.oMin;
            Vector2 mx = parent.min + Vector2.Scale(b.aMax, parent.size) + b.oMax;
            return Rect.MinMaxRect(mn.x, mn.y, mx.x, mx.y);
        }

        private static Dictionary<string, Rect> Layout(Spec spec, string state, Vector2 size)
        {
            var root = new Rect(0f, 0f, size.x, size.y);
            var guides = new Dictionary<string, Rect>();
            foreach (var g in spec.guides)
            {
                float w = Mathf.Max(0f, Mathf.Min(size.x - g.left - g.right, g.maxWidth));
                Rect v = Eval(g.box, root);
                guides[g.name] = Rect.MinMaxRect(g.left, v.yMin, g.left + w, v.yMax);
            }
            var r = new Dictionary<string, Rect>();
            foreach (var p in spec.pieces)
            {
                if (!p.boxes.TryGetValue(state, out var b)) continue;
                Rect parent = p.guide != null ? guides[p.guide] : root;
                Rect e = Eval(b, parent);
                if (e.width >= 1f && e.height >= 1f) r[p.name] = e;
            }
            return r;
        }

        public struct JoinKey { public string a, b; public bool aRight, bTop; public override string ToString() => a + (aRight ? "R" : "L") + "_" + b + (bTop ? "T" : "B"); }

        private static bool Inside(Rect r, float x, float y) => x > r.xMin && x < r.xMax && y > r.yMin && y < r.yMax;

        public static List<JoinKey> DetectJoins(Spec spec, params Vector2[] sizes)
        {
            var keys = new Dictionary<string, JoinKey>();
            foreach (var state in spec.states)
            foreach (var size in sizes)
            {
                var L = Layout(spec, state, size);
                var xs = new HashSet<float>(); var ys = new HashSet<float>();
                foreach (var r in L.Values) { xs.Add(r.xMin); xs.Add(r.xMax); ys.Add(r.yMin); ys.Add(r.yMax); }
                bool In(float x, float y) { foreach (var r in L.Values) if (Inside(r, x, y)) return true; return false; }
                foreach (float x in xs)
                foreach (float y in ys)
                {
                    const float e = 0.5f;
                    bool pp = In(x + e, y + e), mp = In(x - e, y + e), pm = In(x + e, y - e), mm = In(x - e, y - e);
                    int n = (pp ? 1 : 0) + (mp ? 1 : 0) + (pm ? 1 : 0) + (mm ? 1 : 0);
                    if (n != 3) continue;
                    float dx = !pp || !pm ? 1f : -1f, dy = !pp || !mp ? 1f : -1f;
                    if (!pp) { dx = 1f; dy = 1f; } else if (!mp) { dx = -1f; dy = 1f; } else if (!pm) { dx = 1f; dy = -1f; } else { dx = -1f; dy = -1f; }
                    string A = null, B = null;
                    foreach (var kv in L)
                    {
                        Rect r = kv.Value;
                        float ex = dx > 0f ? r.xMax : r.xMin, ey = dy > 0f ? r.yMax : r.yMin;
                        if (A == null && Mathf.Abs(ex - x) < 0.01f && Inside(r, x - dx * e, y + dy * e)) A = kv.Key;
                        if (B == null && Mathf.Abs(ey - y) < 0.01f && Inside(r, x + dx * e, y - dy * e)) B = kv.Key;
                    }
                    if (A == null || B == null) { Debug.LogWarning("[FrameAuthoring] " + spec.name + "/" + state + ": inward corner at " + x + "," + y + " has no clear owner pieces."); continue; }
                    bool backOk = Inside(L[A], x - dx * e, y - dy * e) || Inside(L[B], x - dx * e, y - dy * e);
                    if (!backOk) Debug.LogWarning("[FrameAuthoring] " + spec.name + "/" + state + ": corner " + A + "/" + B + " needs a third piece behind it.");
                    var k = new JoinKey { a = A, b = B, aRight = dx > 0f, bTop = dy > 0f };
                    keys[k.ToString()] = k;
                }
            }
            return new List<JoinKey>(keys.Values);
        }

        // ── prefab building ──────────────────────────────────────────────────

        /// <summary>Builds the frame as a prefab asset (nested FramePanel / FrameJoin instances).
        /// Fit references are left empty – they point at scene objects and are set on the instance.</summary>
        public static GameObject BuildPrefab(Spec spec, FrameStyle style, string path, Vector2 joinProbeSizeA, params Vector2[] moreSizes)
        {
            var panelPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(PanelPrefabPath);
            var joinPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(JoinPrefabPath);
            var root = new GameObject(spec.name, typeof(RectTransform));
            root.layer = 5;
            var rrt = (RectTransform)root.transform;
            rrt.sizeDelta = new Vector2(1920f, 1080f);
            var frame = root.AddComponent<UiFrame>();
            frame.style = style;
            frame.initialState = spec.initialState;

            var reset = NewImage("StencilReset", rrt, null, Color.white, false);
            reset.material = style.resetMaterial;
            Stretch(reset.rectTransform);

            var joinsRoot = NewRect("Joins", rrt); Stretch(joinsRoot);
            var guides = new Dictionary<string, RectTransform>();
            foreach (var g in spec.guides)
            {
                var gr = NewRect(g.name, rrt);
                gr.anchorMin = new Vector2(0f, g.box.aMin.y); gr.anchorMax = new Vector2(0f, g.box.aMax.y);
                gr.offsetMin = new Vector2(0f, g.box.oMin.y); gr.offsetMax = new Vector2(0f, g.box.oMax.y);
                var f = gr.gameObject.AddComponent<MaxWidthFitter>();
                f.left = g.left; f.right = g.right; f.maxWidth = g.maxWidth;
                guides[g.name] = gr;
            }
            var made = new Dictionary<string, RectTransform>();
            string first = spec.initialState ?? spec.states[0];
            foreach (var p in spec.pieces)
            {
                var parent = p.guide != null ? guides[p.guide] : rrt;
                var go = (GameObject)PrefabUtility.InstantiatePrefab(panelPrefab, parent);
                go.name = "Panel_" + p.name;
                var rt = (RectTransform)go.transform;
                rt.pivot = new Vector2(0.5f, 0.5f);
                if (p.boxes.TryGetValue(first, out var b)) SetBox(rt, b);
                made[p.name] = rt;
            }
            foreach (var s in spec.states)
            {
                var st = new UiFrame.State { name = s };
                foreach (var p in spec.pieces)
                {
                    if (!p.boxes.TryGetValue(s, out var b)) continue;
                    st.pieces.Add(new UiFrame.PieceState { piece = made[p.name], anchorMin = b.aMin, anchorMax = b.aMax, offsetMin = b.oMin, offsetMax = b.oMax, fit = b.fit, fitPad = b.fitPad });
                }
                frame.states.Add(st);
            }
            var sizes = new List<Vector2> { joinProbeSizeA };
            sizes.AddRange(moreSizes);
            foreach (var k in DetectJoins(spec, sizes.ToArray()))
            {
                var go = (GameObject)PrefabUtility.InstantiatePrefab(joinPrefab, joinsRoot);
                go.name = "Join_" + k;
                var j = go.GetComponent<FrameJoin>();
                j.a = made[k.a]; j.aRightEdge = k.aRight; j.b = made[k.b]; j.bTopEdge = k.bTop;
            }
            EnsureFolder(System.IO.Path.GetDirectoryName(path).Replace('\\', '/'));
            var asset = PrefabUtility.SaveAsPrefabAsset(root, path);
            Object.DestroyImmediate(root);
            return asset;
        }

        public static void SetBox(RectTransform rt, Box b)
        {
            rt.anchorMin = b.aMin; rt.anchorMax = b.aMax; rt.offsetMin = b.oMin; rt.offsetMax = b.oMax;
        }

        /// <summary>Instantiates a frame prefab in the open scene, stretched over its parent, and
        /// resolves the per-state fit references to scene objects.</summary>
        public static UiFrame Instantiate(GameObject prefab, Transform parent, int siblingIndex, Spec spec, Dictionary<string, RectTransform> fitTargets)
        {
            var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent);
            go.transform.SetSiblingIndex(siblingIndex);
            var rt = (RectTransform)go.transform;
            rt.localScale = Vector3.one;
            Stretch(rt);
            Place(rt, L(0f), L(0f), R(0f), R(0f));   // exactly the canvas, even under a scaled parent
            var frame = go.GetComponent<UiFrame>();
            if (fitTargets != null)
            {
                foreach (var st in frame.states)
                foreach (var ps in st.pieces)
                {
                    string pname = ps.piece != null ? ps.piece.name.Replace("Panel_", "") : null;
                    var piece = spec.pieces.Find(x => x.name == pname);
                    if (piece == null || !piece.boxes.TryGetValue(st.name, out var b) || b.fitKey == null) continue;
                    if (fitTargets.TryGetValue(b.fitKey, out var target)) ps.fitTo = target;
                    else Debug.LogWarning("[FrameAuthoring] fit target '" + b.fitKey + "' missing");
                }
                PrefabUtility.RecordPrefabInstancePropertyModifications(frame);
            }
            EditorUtility.SetDirty(frame);
            return frame;
        }
    }
}
