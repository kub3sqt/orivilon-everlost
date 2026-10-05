using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using Rng = System.Random;

namespace Orivilon.EditorTools.StyleTest
{
    /// <summary>Swatche (UV) v existujících paletových texturách projektu – jen se na ně ukazuje, textury se nemění.</summary>
    internal static class Sw
    {
        // Assets/Models/Textures/Misc/Color_Palette.png (materiál Color_Palette – kmeny stromů/keřů)
        public static readonly Vector2 Bark = new Vector2(0.615f, 0.875f);      // (138,110,81) kůra listnáče
        public static readonly Vector2 Root = new Vector2(0.615f, 0.78f);       // (157,130,100) světlé kořenové náběhy
        public static readonly Vector2 Birch = new Vector2(0.864f, 0.88f);      // (240,238,231) bříza
        public static readonly Vector2 BirchDark = new Vector2(0.609f, 0.22f);  // (61,61,61) skvrny / pata břízy
        public static readonly Vector2 Stem = new Vector2(0.377f, 0.88f);       // (90,68,45) větvičky keřů
        public static readonly Vector2 WoodIn = new Vector2(0.37f, 0.78f);      // (178,136,103) řez dřeva
        public static readonly Vector2 Moss = new Vector2(0.37f, 0.22f);        // (94,121,82) mech
        // Assets/Models/Textures/Misc/Color Pallet.png (materiál Color Pallet – kameny Stone N)
        public static readonly Vector2 Stone = new Vector2(0.962f, 0.803f);     // (148,139,125)
        // kolo 14 – suché regiony (také Color Pallet.png, jednolité swatche)
        public static readonly Vector2 MesaRed = new Vector2(0.625f, 0.812f);    // (186,78,43) terakota
        public static readonly Vector2 MesaOrange = new Vector2(0.406f, 0.500f); // (179,112,65)
        public static readonly Vector2 Sand = new Vector2(0.242f, 0.562f);       // (193,157,115) pískovec
        public static readonly Vector2 SandLight = new Vector2(0.438f, 0.562f);  // (197,178,132)
    }

    /// <summary>Síť s explicitními normálami a UV (karty listí se „sférickými“ normálami, flat paletová tělesa).</summary>
    internal sealed class SMMesh
    {
        public readonly List<Vector3> V = new List<Vector3>();
        public readonly List<Vector3> N = new List<Vector3>();
        public readonly List<Vector2> UV = new List<Vector2>();
        public readonly List<int>[] Subs;

        public SMMesh(int subs = 1) { Subs = new List<int>[subs]; for (int i = 0; i < subs; i++) Subs[i] = new List<int>(); }

        public static float Rf(Rng r, float a, float b) => a + (float)r.NextDouble() * (b - a);

        public void FlatTri(int sub, Vector3 a, Vector3 b, Vector3 c, Vector3 outward, Vector2 uv)
        {
            Vector3 n = Vector3.Cross(b - a, c - a);
            if (n.sqrMagnitude < 1e-14f) return;
            if (Vector3.Dot(n, outward) < 0f) { Vector3 t = b; b = c; c = t; n = -n; }
            n.Normalize();
            int i = V.Count;
            V.Add(a); V.Add(b); V.Add(c);
            N.Add(n); N.Add(n); N.Add(n);
            UV.Add(uv); UV.Add(uv); UV.Add(uv);
            Subs[sub].Add(i); Subs[sub].Add(i + 1); Subs[sub].Add(i + 2);
        }

        /// <summary>Čtvercová listová/travní karta, UV 0..1 přes celý atlas (stejně jako herní karty).</summary>
        /// <remarks>UNP/Vegetation otáčí normálu na rubu (SV_IsFrontFace) – líc karty proto míří do frontDir (ven z koruny / trsu).</remarks>
        public void Card(int sub, Vector3 c, Vector3 halfRight, Vector3 halfUp, Func<Vector3, Vector3> normalAt, Vector3? frontDir = null)
        {
            if (frontDir.HasValue && Vector3.Dot(Vector3.Cross(halfRight, halfUp), frontDir.Value) < 0f) halfRight = -halfRight;
            int i = V.Count;
            Vector3 p0 = c - halfRight - halfUp, p1 = c + halfRight - halfUp, p2 = c + halfRight + halfUp, p3 = c - halfRight + halfUp;
            V.Add(p0); V.Add(p1); V.Add(p2); V.Add(p3);
            N.Add(normalAt(p0)); N.Add(normalAt(p1)); N.Add(normalAt(p2)); N.Add(normalAt(p3));
            UV.Add(new Vector2(0, 0)); UV.Add(new Vector2(1, 0)); UV.Add(new Vector2(1, 1)); UV.Add(new Vector2(0, 1));
            Subs[sub].AddRange(new[] { i, i + 1, i + 2, i, i + 2, i + 3 });
        }

        public void AddRaw(int sub, Vector3[] p, Vector3[] n, Vector2[] uv, int[] tris)
        {
            int b = V.Count;
            V.AddRange(p); N.AddRange(n); UV.AddRange(uv);
            foreach (int t in tris) Subs[sub].Add(b + t);
        }

        /// <summary>Rovnoměrně převzorkuje lomenou čáru na n bodů (víc prstenců pro rýhy kůry).</summary>
        public static Vector3[] Resample(Vector3[] p, int n)
        {
            var o = new Vector3[n];
            for (int i = 0; i < n; i++)
            {
                float f = i / (float)(n - 1) * (p.Length - 1);
                int j = Mathf.Min((int)f, p.Length - 2);
                o[i] = Vector3.Lerp(p[j], p[j + 1], f - j);
            }
            return o;
        }

        public static float[] Resample(float[] p, int n)
        {
            var o = new float[n];
            for (int i = 0; i < n; i++)
            {
                float f = i / (float)(n - 1) * (p.Length - 1);
                int j = Mathf.Min((int)f, p.Length - 2);
                o[i] = Mathf.Lerp(p[j], p[j + 1], f - j);
            }
            return o;
        }

        public Bounds Bounds()
        {
            if (V.Count == 0) return new Bounds();
            var b = new Bounds(V[0], Vector3.zero);
            foreach (var v in V) b.Encapsulate(v);
            return b;
        }

        public void Offset(Vector3 d) { for (int i = 0; i < V.Count; i++) V[i] += d; }

        public int Tris { get { int c = 0; foreach (var s in Subs) c += s.Count / 3; return c; } }

        /// <summary>Hranatá trubka (kmen, větev, kořen) – flat shading, UV podle pick(ring, strana).</summary>
        public void Tube(int sub, Vector3[] pts, float[] radii, int sides, Rng r, Func<int, int, Vector2> uvPick, float radialJitter = 0f,
            bool capStart = true, bool capEnd = true, Vector2? capUv = null, float ridge = 0f, float twist = 0f)
        {
            int n = pts.Length;
            var rings = new Vector3[n, sides];
            Vector3 t0 = (pts[1] - pts[0]).normalized;
            Vector3 u = Mathf.Abs(t0.y) < 0.9f ? Vector3.up : Vector3.right;
            float phase = Rf(r, 0f, Mathf.PI * 2f);
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
                    float a = phase + i * twist + k * Mathf.PI * 2f / sides;
                    rings[i, k] = pts[i] + (Mathf.Cos(a) * u + Mathf.Sin(a) * w) * (radii[i] * (1f + radialJitter * Rf(r, -1f, 1f)) * (k % 2 == 0 ? 1f : 1f - ridge));
                }
            }
            for (int i = 0; i < n - 1; i++)
            {
                Vector3 axis = (pts[i] + pts[i + 1]) * 0.5f;
                for (int k = 0; k < sides; k++)
                {
                    int k1 = (k + 1) % sides;
                    Vector3 a = rings[i, k], b = rings[i, k1], c = rings[i + 1, k1], d = rings[i + 1, k];
                    Vector3 o = (a + b + c + d) * 0.25f - axis;
                    Vector2 uv = uvPick(i, k);
                    FlatTri(sub, a, b, c, o, uv); FlatTri(sub, a, c, d, o, uv);
                }
            }
            Vector2 cu = capUv ?? uvPick(0, 0);
            if (capStart && radii[0] > 0f)
                for (int k = 0; k < sides; k++) FlatTri(sub, pts[0], rings[0, k], rings[0, (k + 1) % sides], -(pts[1] - pts[0]), cu);
            if (capEnd && radii[n - 1] > 0f)
                for (int k = 0; k < sides; k++) FlatTri(sub, pts[n - 1], rings[n - 1, k], rings[n - 1, (k + 1) % sides], pts[n - 1] - pts[n - 2], capUv ?? uvPick(n - 2, 0));
        }

        /// <summary>Flat-shaded zdeformovaná ikosféra (kámen) s UV na swatch palety.</summary>
        public void Rock(int sub, Rng r, Vector3 center, Vector3 radii, int subdiv, float jitter, Quaternion rot, float floorY, Func<Vector3, int> subPick = null, Func<Vector3, Vector2> uvPick = null)
        {
            LowPolyMesh.Ico(subdiv, out var v, out var f);
            var p = new Vector3[v.Length];
            for (int i = 0; i < v.Length; i++)
            {
                Vector3 q = rot * Vector3.Scale(v[i] * (1f + jitter * Rf(r, -1f, 1f)), radii) + center;
                if (q.y < floorY) q.y = floorY;
                p[i] = q;
            }
            for (int i = 0; i < f.Length; i += 3)
            {
                Vector3 a = p[f[i]], b = p[f[i + 1]], c = p[f[i + 2]];
                Vector3 o = (a + b + c) / 3f - center;
                Vector3 n = Vector3.Cross(b - a, c - a); if (Vector3.Dot(n, o) < 0) n = -n; n.Normalize();
                FlatTri(subPick != null ? subPick(n) : sub, a, b, c, o, uvPick != null ? uvPick(n) : Sw.Stone);
            }
        }

        public void Fill(Mesh mesh, string name, out int[] used)
        {
            var u = new List<int>();
            for (int s = 0; s < Subs.Length; s++) if (Subs[s].Count > 0) u.Add(s);
            used = u.ToArray();
            mesh.Clear();
            mesh.name = name;
            mesh.indexFormat = V.Count > 65000 ? IndexFormat.UInt32 : IndexFormat.UInt16;
            mesh.SetVertices(V); mesh.SetNormals(N); mesh.SetUVs(0, UV);
            mesh.subMeshCount = used.Length;
            for (int i = 0; i < used.Length; i++) mesh.SetTriangles(Subs[used[i]], i, true);
            mesh.RecalculateBounds();
        }
    }
}
