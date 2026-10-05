using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using Rng = System.Random;

namespace Orivilon.EditorTools.StyleTest
{
    /// <summary>
    /// Paleta materiálů low-poly sady (kolo 13). Index = submesh v <see cref="LowPolyMesh"/>.
    /// </summary>
    internal static class Pal
    {
        public const int Bark = 0, BarkDark = 1, BirchBark = 2, BirchMark = 3,
            LeafA = 4, LeafB = 5, LeafDark = 6, LeafBirch = 7,
            GrassA = 8, GrassB = 9, GrassDry = 10,
            RockLight = 11, RockMid = 12, RockDark = 13, Moss = 14,
            WoodInner = 15, DryWood = 16,
            FlowerYellow = 17, FlowerWhite = 18, FlowerPurple = 19, FlowerCenter = 20,
            MushCap = 21, MushStem = 22, Count = 23;

        public static readonly string[] Names =
        {
            "Kura", "Kura_Tmava", "Briza_Kura", "Briza_Skvrny",
            "Listi_A", "Listi_B", "Listi_Tmave", "Listi_Briza",
            "Trava_A", "Trava_B", "Trava_Sucha",
            "Kamen_Svetly", "Kamen_Stredni", "Kamen_Tmavy", "Mech",
            "Drevo_Vnitrek", "Drevo_Suche",
            "Kvet_Zluty", "Kvet_Bily", "Kvet_Fialovy", "Kvet_Stred",
            "Houba_Klobouk", "Houba_Trenik"
        };

        public static readonly Color[] Colors =
        {
            new Color(0.40f, 0.28f, 0.19f), new Color(0.29f, 0.21f, 0.15f), new Color(0.88f, 0.86f, 0.80f), new Color(0.20f, 0.19f, 0.18f),
            new Color(0.30f, 0.56f, 0.18f), new Color(0.42f, 0.66f, 0.21f), new Color(0.21f, 0.44f, 0.17f), new Color(0.58f, 0.74f, 0.26f),
            new Color(0.40f, 0.65f, 0.20f), new Color(0.56f, 0.75f, 0.25f), new Color(0.78f, 0.69f, 0.38f),
            new Color(0.66f, 0.66f, 0.64f), new Color(0.52f, 0.52f, 0.53f), new Color(0.39f, 0.39f, 0.42f), new Color(0.42f, 0.57f, 0.21f),
            new Color(0.82f, 0.67f, 0.45f), new Color(0.56f, 0.46f, 0.34f),
            new Color(0.98f, 0.82f, 0.20f), new Color(0.95f, 0.95f, 0.91f), new Color(0.62f, 0.43f, 0.86f), new Color(0.96f, 0.64f, 0.15f),
            new Color(0.78f, 0.26f, 0.18f), new Color(0.92f, 0.88f, 0.78f)
        };
    }

    /// <summary>
    /// Jednoduchý stavitel flat-shaded low-poly sítí: každý trojúhelník má vlastní vrcholy (ostré fazety),
    /// všechny díly jsou uzavřená tělesa (žádné oboustranné plochy → žádné černé rubové strany).
    /// Orientace trojúhelníků se opravuje podle zadaného směru „ven“.
    /// </summary>
    internal sealed class LowPolyMesh
    {
        public readonly List<Vector3> Verts = new List<Vector3>();
        public readonly List<int>[] Subs;
        public readonly List<Bounds> Parts = new List<Bounds>();
        private int partStart = -1;

        public LowPolyMesh(int submeshes)
        {
            Subs = new List<int>[submeshes];
            for (int i = 0; i < submeshes; i++) Subs[i] = new List<int>();
        }

        public static float Rf(Rng r, float a, float b) => a + (float)r.NextDouble() * (b - a);

        public void Tri(int sub, Vector3 a, Vector3 b, Vector3 c, Vector3 outward)
        {
            Vector3 n = Vector3.Cross(b - a, c - a);
            if (n.sqrMagnitude < 1e-14f) return;
            if (Vector3.Dot(n, outward) < 0f) { Vector3 t = b; b = c; c = t; }
            int i = Verts.Count;
            Verts.Add(a); Verts.Add(b); Verts.Add(c);
            Subs[sub].Add(i); Subs[sub].Add(i + 1); Subs[sub].Add(i + 2);
        }

        private void BeginPart() { partStart = Verts.Count; }

        private void EndPart()
        {
            if (partStart < 0 || partStart >= Verts.Count) { partStart = -1; return; }
            var b = new Bounds(Verts[partStart], Vector3.zero);
            for (int i = partStart + 1; i < Verts.Count; i++) b.Encapsulate(Verts[i]);
            Parts.Add(b);
            partStart = -1;
        }

        // ---------- ikosféra ----------
        private static readonly Dictionary<int, KeyValuePair<Vector3[], int[]>> icoCache = new Dictionary<int, KeyValuePair<Vector3[], int[]>>();

        internal static void Ico(int subdiv, out Vector3[] verts, out int[] faces)
        {
            if (icoCache.TryGetValue(subdiv, out var kv)) { verts = kv.Key; faces = kv.Value; return; }
            float t = (1f + Mathf.Sqrt(5f)) * 0.5f;
            var v = new List<Vector3>
            {
                new Vector3(-1, t, 0), new Vector3(1, t, 0), new Vector3(-1, -t, 0), new Vector3(1, -t, 0),
                new Vector3(0, -1, t), new Vector3(0, 1, t), new Vector3(0, -1, -t), new Vector3(0, 1, -t),
                new Vector3(t, 0, -1), new Vector3(t, 0, 1), new Vector3(-t, 0, -1), new Vector3(-t, 0, 1)
            };
            for (int i = 0; i < v.Count; i++) v[i] = v[i].normalized;
            var f = new List<int> { 0,11,5, 0,5,1, 0,1,7, 0,7,10, 0,10,11, 1,5,9, 5,11,4, 11,10,2, 10,7,6, 7,1,8,
                                    3,9,4, 3,4,2, 3,2,6, 3,6,8, 3,8,9, 4,9,5, 2,4,11, 6,2,10, 8,6,7, 9,8,1 };
            for (int s = 0; s < subdiv; s++)
            {
                var mid = new Dictionary<long, int>();
                var nf = new List<int>();
                int Mid(int a, int b)
                {
                    long key = a < b ? ((long)a << 32) | (uint)b : ((long)b << 32) | (uint)a;
                    if (mid.TryGetValue(key, out int idx)) return idx;
                    v.Add(((v[a] + v[b]) * 0.5f).normalized);
                    mid[key] = v.Count - 1;
                    return v.Count - 1;
                }
                for (int i = 0; i < f.Count; i += 3)
                {
                    int a = f[i], b = f[i + 1], c = f[i + 2];
                    int ab = Mid(a, b), bc = Mid(b, c), ca = Mid(c, a);
                    nf.AddRange(new[] { a, ab, ca, b, bc, ab, c, ca, bc, ab, bc, ca });
                }
                f = nf;
            }
            verts = v.ToArray();
            faces = f.ToArray();
            icoCache[subdiv] = new KeyValuePair<Vector3[], int[]>(verts, faces);
        }

        /// <summary>Zdeformovaná ikosféra (koruna, keř, kámen). floorY zarovná spodek do roviny (zapuštění pod zem).</summary>
        public void Blob(int sub, Rng r, Vector3 center, Vector3 radii, int subdiv, float jitter, Quaternion rot,
            float floorY = float.NegativeInfinity, Func<Vector3, int> pick = null)
        {
            Ico(subdiv, out var v, out var f);
            var p = new Vector3[v.Length];
            for (int i = 0; i < v.Length; i++)
            {
                float k = 1f + jitter * Rf(r, -1f, 1f);
                Vector3 q = rot * Vector3.Scale(v[i] * k, radii) + center;
                if (q.y < floorY) q.y = floorY;
                p[i] = q;
            }
            BeginPart();
            for (int i = 0; i < f.Length; i += 3)
            {
                Vector3 a = p[f[i]], b = p[f[i + 1]], c = p[f[i + 2]];
                Vector3 outward = (a + b + c) / 3f - center;
                int s = sub;
                if (pick != null)
                {
                    Vector3 n = Vector3.Cross(b - a, c - a);
                    if (Vector3.Dot(n, outward) < 0f) n = -n;
                    s = pick(n.normalized);
                }
                Tri(s, a, b, c, outward);
            }
            EndPart();
        }

        public void Blob(int sub, Rng r, Vector3 center, Vector3 radii, int subdiv, float jitter, float yawDeg,
            float floorY = float.NegativeInfinity, Func<Vector3, int> pick = null)
            => Blob(sub, r, center, radii, subdiv, jitter, Quaternion.Euler(0f, yawDeg, 0f), floorY, pick);

        /// <summary>
        /// Hranatá trubka podél lomené čáry (kmen, větev, stéblo, list). flat &lt; 1 zploští průřez ve směru upHint (list).
        /// Poloměr 0 na konci = špička bez víčka.
        /// </summary>
        public void Tube(int sub, Vector3[] pts, float[] radii, int sides, Rng r, float radialJitter = 0f,
            Vector3? upHint = null, float flat = 1f, bool capStart = true, bool capEnd = true,
            int capStartSub = -1, int capEndSub = -1, Func<Vector3, int, int, int> pick = null, bool randomPhase = true)
        {
            int n = pts.Length;
            var rings = new Vector3[n, sides];
            Vector3 t0 = (pts[1] - pts[0]).normalized;
            Vector3 u = upHint ?? (Mathf.Abs(t0.y) < 0.9f ? Vector3.up : Vector3.right);
            float phase = randomPhase ? Rf(r, 0f, Mathf.PI * 2f) : 0f;
            for (int i = 0; i < n; i++)
            {
                Vector3 t = i == 0 ? pts[1] - pts[0] : i == n - 1 ? pts[n - 1] - pts[n - 2] : pts[i + 1] - pts[i - 1];
                t.Normalize();
                Vector3 uu = Vector3.ProjectOnPlane(u, t);
                if (uu.sqrMagnitude < 1e-6f) uu = Vector3.ProjectOnPlane(Vector3.right, t);
                u = uu.normalized;
                Vector3 w = Vector3.Cross(t, u);
                for (int k = 0; k < sides; k++)
                {
                    float a = phase + k * Mathf.PI * 2f / sides;
                    float rr = radii[i] * (1f + radialJitter * Rf(r, -1f, 1f));
                    rings[i, k] = pts[i] + (Mathf.Cos(a) * flat * u + Mathf.Sin(a) * w) * rr;
                }
            }
            BeginPart();
            for (int i = 0; i < n - 1; i++)
            {
                Vector3 axis = (pts[i] + pts[i + 1]) * 0.5f;
                for (int k = 0; k < sides; k++)
                {
                    int k1 = (k + 1) % sides;
                    Vector3 a = rings[i, k], b = rings[i, k1], c = rings[i + 1, k1], d = rings[i + 1, k];
                    Vector3 outward = (a + b + c + d) * 0.25f - axis;
                    int s = pick != null ? pick(outward.normalized, i, k) : sub;
                    Tri(s, a, b, c, outward);
                    Tri(s, a, c, d, outward);
                }
            }
            if (capStart && radii[0] > 0f)
            {
                Vector3 dir = -(pts[1] - pts[0]).normalized;
                for (int k = 0; k < sides; k++)
                    Tri(capStartSub >= 0 ? capStartSub : sub, pts[0], rings[0, k], rings[0, (k + 1) % sides], dir);
            }
            if (capEnd && radii[n - 1] > 0f)
            {
                Vector3 dir = (pts[n - 1] - pts[n - 2]).normalized;
                for (int k = 0; k < sides; k++)
                    Tri(capEndSub >= 0 ? capEndSub : sub, pts[n - 1], rings[n - 1, k], rings[n - 1, (k + 1) % sides], dir);
            }
            EndPart();
        }

        /// <summary>Počet dílů, které se přes překryv obálek nenapojí na nic, co stojí na zemi (hrubá kontrola „plovoucí geometrie“).</summary>
        public int FloatingParts(out float minY, out float maxY)
        {
            minY = float.MaxValue; maxY = float.MinValue;
            foreach (var v in Verts) { if (v.y < minY) minY = v.y; if (v.y > maxY) maxY = v.y; }
            int n = Parts.Count;
            var ok = new bool[n];
            for (int i = 0; i < n; i++) ok[i] = Parts[i].min.y <= 0.02f;
            bool changed = true;
            while (changed)
            {
                changed = false;
                for (int i = 0; i < n; i++)
                {
                    if (ok[i]) continue;
                    var bi = Parts[i]; bi.Expand(0.01f);
                    for (int j = 0; j < n; j++)
                        if (ok[j] && bi.Intersects(Parts[j])) { ok[i] = true; changed = true; break; }
                }
            }
            int bad = 0;
            for (int i = 0; i < n; i++) if (!ok[i]) bad++;
            return bad;
        }

        public int TriangleCount
        {
            get { int c = 0; foreach (var s in Subs) c += s.Count / 3; return c; }
        }

        /// <summary>Naplní (i existující) Mesh; prázdné submeshe vynechá. used = indexy palety pro materiály.</summary>
        public void Fill(Mesh mesh, string name, out int[] used)
        {
            var u = new List<int>();
            for (int s = 0; s < Subs.Length; s++) if (Subs[s].Count > 0) u.Add(s);
            used = u.ToArray();
            mesh.Clear();
            mesh.name = name;
            mesh.indexFormat = Verts.Count > 65000 ? IndexFormat.UInt32 : IndexFormat.UInt16;
            mesh.SetVertices(Verts);
            mesh.subMeshCount = used.Length;
            for (int i = 0; i < used.Length; i++) mesh.SetTriangles(Subs[used[i]], i, true);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
        }
    }
}
