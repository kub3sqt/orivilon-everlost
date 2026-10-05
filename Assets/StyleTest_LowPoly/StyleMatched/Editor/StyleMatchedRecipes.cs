using System;
using System.Collections.Generic;
using UnityEngine;
using Rng = System.Random;

namespace Orivilon.EditorTools.StyleTest
{
    /// <summary>Materiály StyleMatched – kopie interních materiálů (zdroje se nemění).</summary>
    internal sealed class SMMats
    {
        public Material Broad, Birch, Bush, BushTall, Savana, BushTundra, GrassHigh, GrassMed, GrassLow, Grass02, GrassDry, Fern, Palette, Stones;
        // kolo 16
        public Material Sakura, Jungle, Bamboo, Sequoia, Heather, FernTrop, FirDark;
        // kolo 19
        public Material Palm;
        // kolo 20: kopie materiálu kamenů s modravým tónem (led, tyrkysová krusta) – stejná paletová textura
        public Material Ice;
        // kolo 21: kopie materiálu kamenů s emisí z téže paletové textury (svítí barvou swatche) – krystaly, svítící houby
        public Material Glow;
        // kolo 28: kopie materiálu kamenů (stejná paletová textura) – matně ztmavený čedič a leskle černý obsidián
        public Material Basalt, Obsidian;
    }

    internal sealed class SMDef
    {
        public string Name, Category, Ref, Collider;
        public int Seed;
        public bool Pilot;
        public Func<Rng, SMMats, SMParts> Build;
        /// <summary>Kolo 16: varianta existujícího herního prefabu – (název zdrojového materiálu → náhrada).</summary>
        public Func<SMMats, Dictionary<string, Material>> Variant;
    }

    /// <summary>Výsledek receptu: díly (mesh + materiály + lokální pozice), volitelně LOD úrovně listí.</summary>
    internal sealed class SMParts
    {
        public readonly List<(string name, SMMesh mesh, Material[] mats, Vector3 pos, int lod)> Items = new List<(string, SMMesh, Material[], Vector3, int)>();
        public bool HasLods;
        public float TrunkR, TrunkH;
        /// <summary>Kolo 19: složený collider z kvádrů (střed, rozměr, natočení) – ruiny a portály bez děr a pastí.</summary>
        public readonly List<(Vector3 c, Vector3 size, Quaternion rot)> Boxes = new List<(Vector3, Vector3, Quaternion)>();
        public void Add(string n, SMMesh m, Vector3 pos, int lod, params Material[] mats) => Items.Add((n, m, mats, pos, lod));
    }

    internal static partial class StyleMatchedRecipes
    {
        private static float Rf(Rng r, float a, float b) => SMMesh.Rf(r, a, b);
        private static Vector3 V(float x, float y, float z) => new Vector3(x, y, z);
        private static Vector3 Dir(float yaw) => new Vector3(Mathf.Sin(yaw * Mathf.Deg2Rad), 0f, Mathf.Cos(yaw * Mathf.Deg2Rad));
        private static Vector3 RandUnit(Rng r)
        {
            for (int i = 0; i < 64; i++)
            {
                var v = V(Rf(r, -1, 1), Rf(r, -1, 1), Rf(r, -1, 1));
                float m = v.sqrMagnitude;
                if (m > 0.01f && m <= 1f) return v / Mathf.Sqrt(m);
            }
            return Vector3.up;
        }
        private static Quaternion RandRot(Rng r) => Quaternion.LookRotation(RandUnit(r), RandUnit(r));

        private static Vector3 OnLine(Vector3[] p, float t)
        {
            float f = Mathf.Clamp01(t) * (p.Length - 1);
            int i = Mathf.Min((int)f, p.Length - 2);
            return Vector3.Lerp(p[i], p[i + 1], f - i);
        }

        // ================================================================ STROMY
        internal sealed class TreeSpec
        {
            public float H = 7f, R = 0.3f, Lean = 0.3f, Wobble = 0.08f, BStart = 0.45f, BLen = 2f, ElevMin = 25f, ElevMax = 50f;
            public float ClR = 1.4f, TopCl = 1.6f, Density = 88f, Card = 0.7f, SquashY = 0.85f, BiasYaw = -1f, MidCl = 0.9f;
            public int Branches = 6, Stems = 1;
            public bool Birch;
            public Material Leaves;   // kolo 14: přebití listového materiálu (savana)
            public float RootScale = 1f;   // kolo 16: kořenové náběhy (džungle, sekvoje)
            public int Roots = -1, Lianas = 0;
        }

        private static SMParts Tree(Rng r, SMMats m, TreeSpec s)
        {
            var trunk = new SMMesh(1);
            var cl = new List<Vector4>();
            for (int st = 0; st < s.Stems; st++)
            {
                float leanYaw = s.BiasYaw >= 0 ? s.BiasYaw + st * 160f : Rf(r, 0, 360);
                Vector3 baseP = s.Stems > 1 ? Dir(leanYaw) * 0.1f : Vector3.zero;
                float h = s.H * (st == 0 ? 1f : 0.85f);
                int segs = s.Birch ? 14 : 11;
                var pts = new Vector3[segs + 1];
                var rad = new float[segs + 1];
                for (int i = 0; i <= segs; i++)
                {
                    float t = i / (float)segs;
                    pts[i] = baseP + Vector3.up * (-0.3f + (h + 0.3f) * t) + Dir(leanYaw) * (s.Lean * t * t);
                    if (i > 0 && i < segs) pts[i] += V(Rf(r, -s.Wobble, s.Wobble), 0, Rf(r, -s.Wobble, s.Wobble));
                    rad[i] = Mathf.Lerp(s.R, s.R * 0.28f, t);
                }
                Func<int, int, Vector2> uvT = s.Birch
                    ? (i, k) => i <= 1 ? Sw.BirchDark : (r.NextDouble() < 0.17 ? Sw.BirchDark : Sw.Birch)
                    : (Func<int, int, Vector2>)((i, k) => i == 0 ? Sw.Root : Sw.Bark);
                trunk.Tube(0, pts, rad, s.Birch ? 6 : 7, r, uvT, 0.05f);
                // kořenové náběhy (světlé u listnáčů, tmavé u břízy)
                int roots = s.Roots > 0 ? s.Roots : (s.Birch ? 3 : 5);
                float rs = s.RootScale;
                for (int k = 0; k < roots; k++)
                {
                    Vector3 d = Dir(k * 360f / roots + Rf(r, -15, 15));
                    trunk.Tube(0, new[] { baseP + Vector3.up * (0.45f * rs) + d * s.R * 0.3f, baseP + d * s.R * (1.3f * rs) + Vector3.up * (0.18f * rs), baseP + d * s.R * (2.3f * rs) + Vector3.down * 0.08f },
                        new[] { s.R * 0.5f, s.R * 0.32f, s.R * 0.08f }, 4, r, (i, kk) => s.Birch ? Sw.BirchDark : Sw.Root);
                }
                Vector2 bUv = s.Birch ? Sw.Birch : Sw.Bark;
                int nb = st == 0 ? s.Branches : Mathf.Max(2, s.Branches - 2);
                for (int b = 0; b < nb; b++)
                {
                    float t = s.BStart + (0.93f - s.BStart) * (b + Rf(r, 0f, 0.7f)) / nb;
                    Vector3 p0 = OnLine(pts, t);
                    float yaw = s.BiasYaw >= 0 ? s.BiasYaw + st * 160f + Rf(r, -75, 75) : b * 137.5f + st * 60f + Rf(r, -20, 20);
                    float el = Rf(r, s.ElevMin, s.ElevMax) * Mathf.Deg2Rad;
                    float len = s.BLen * Mathf.Lerp(1.15f, 0.6f, (t - s.BStart) / Mathf.Max(0.01f, 1f - s.BStart)) * Rf(r, 0.8f, 1.15f);
                    Vector3 d = Dir(yaw) * Mathf.Cos(el) + Vector3.up * Mathf.Sin(el);
                    Vector3 mid = p0 + d * (len * 0.5f) + Vector3.up * (len * 0.08f), end = p0 + d * len + Vector3.up * (len * 0.22f);
                    float r0 = Mathf.Lerp(s.R, s.R * 0.28f, t) * 0.6f;
                    trunk.Tube(0, new[] { p0, mid, end }, new[] { r0, r0 * 0.6f, r0 * 0.25f }, 5, r, (i, k) => bUv);
                    cl.Add(new Vector4(end.x, end.y, end.z, s.ClR * Rf(r, 0.8f, 1.15f)));
                    if (len > 1.2f)
                    {
                        float fy = yaw + (r.NextDouble() < 0.5 ? -1 : 1) * Rf(r, 40, 70);
                        Vector3 fend = mid + Dir(fy) * (len * 0.45f) + Vector3.up * (len * 0.35f);
                        trunk.Tube(0, new[] { mid, (mid + fend) * 0.5f + Vector3.up * 0.1f, fend }, new[] { r0 * 0.45f, r0 * 0.3f, r0 * 0.12f }, 4, r, (i, k) => bUv);
                        cl.Add(new Vector4(fend.x, fend.y, fend.z, s.ClR * Rf(r, 0.65f, 0.9f)));
                    }
                }
                Vector3 top = pts[segs];
                cl.Add(new Vector4(top.x, top.y + s.TopCl * 0.25f, top.z, s.TopCl));
                if (s.MidCl > 0f) { Vector3 mc = OnLine(pts, 0.8f); cl.Add(new Vector4(mc.x, mc.y, mc.z, s.ClR * s.MidCl)); }
            }
            // kolo 16: liány – tenké visící provazce pod shluky koruny (paleta, tmavý stonek)
            for (int k = 0; k < s.Lianas && cl.Count > 1; k++)
            {
                Vector4 c = cl[r.Next(cl.Count - 1)];
                Vector3 a0 = (Vector3)c + V(Rf(r, -0.4f, 0.4f), -c.w * 0.35f, Rf(r, -0.4f, 0.4f));
                float L = Mathf.Min(a0.y - 0.6f, Rf(r, 2.0f, 4.2f));
                if (L < 0.8f) continue;
                Vector3 sway = V(Rf(r, -0.35f, 0.35f), 0f, Rf(r, -0.35f, 0.35f));
                trunk.Tube(0, new[] { a0, a0 + Vector3.down * (L * 0.5f) + sway, a0 + Vector3.down * L + sway * 0.4f }, new[] { 0.035f, 0.03f, 0.02f }, 4, r, (i, kk) => Sw.Stem);
            }
            var parts = new SMParts { HasLods = true, TrunkR = s.R, TrunkH = s.H * 0.6f };
            parts.Add("Trunk", trunk, Vector3.zero, -1, m.Palette);
            AddCrown(parts, r, cl, s.Density, s.Card, s.SquashY, s.Leaves != null ? s.Leaves : (s.Birch ? m.Birch : m.Broad));
            return parts;
        }

        /// <summary>Koruna jako herní: shluky náhodně natočených karet (celý atlas na kartu), hustší u povrchu shluku,
        /// normály mezi „nahoru“ a radiálou od středu koruny. LOD1/LOD2 = řidší a větší karty jako u originálů.</summary>
        private static void AddCrown(SMParts parts, Rng r, List<Vector4> cl, float density, float card, float squash, Material mat)
        {
            Vector3 cc = Vector3.zero; float wsum = 0f;
            foreach (var c in cl) { float w = c.w * c.w; cc += (Vector3)c * w; wsum += w; }
            cc /= Mathf.Max(wsum, 1e-3f);
            var cards = new List<(Vector3 p, Quaternion q, float s, Vector3 f)>();
            foreach (var c in cl)
            {
                int n = Mathf.RoundToInt(density * c.w * c.w);
                float sx = Rf(r, 0.85f, 1.15f), sy = Rf(r, 0.8f, 1.05f), sz = Rf(r, 0.85f, 1.15f);
                for (int j = 0; j < n; j++)
                {
                    Vector3 d = RandUnit(r);
                    float dist = c.w * (0.55f + 0.45f * Mathf.Sqrt((float)r.NextDouble()));
                    Vector3 p = (Vector3)c + Vector3.Scale(d * dist, V(sx, squash * sy, sz));
                    cards.Add((p, RandRot(r), card * Rf(r, 0.88f, 1.12f), p - (Vector3)c));
                }
            }
            Bounds b = new Bounds(cards[0].p, Vector3.zero);
            foreach (var c in cards) b.Encapsulate(c.p);
            Vector3 origin = b.center;
            Func<Vector3, Vector3> nrm = p => ((p - (cc - origin)).normalized * 0.55f + Vector3.up * 0.8f).normalized;
            int[] steps = { 1, 2, 4 }; float[] scale = { 1f, 1.42f, 2f };
            for (int l = 0; l < 3; l++)
            {
                var mesh = new SMMesh(1);
                for (int i = 0; i < cards.Count; i += steps[l])
                {
                    var c = cards[i];
                    float hs = c.s * scale[l] * 0.5f;
                    mesh.Card(0, c.p - origin, c.q * Vector3.right * hs, c.q * Vector3.up * hs, nrm);  // líc náhodně jako u originálu
                }
                parts.Add("Leaves_LOD" + l, mesh, origin, l, mat);
            }
        }

        // ================================================================ KEŘE
        private static SMParts Bush(Rng r, SMMats m, int stems, float w, float h, float clR, float density, Material leafMat = null)
        {
            var wood = new SMMesh(1);
            var cl = new List<Vector4>();
            for (int i = 0; i < stems; i++)
            {
                Vector3 d = Dir(i * 360f / stems + Rf(r, -20, 20));
                float ww = w * Rf(0.55f, 1f, r), hh = h * Rf(0.7f, 1f, r);
                Vector3 p0 = d * 0.04f + Vector3.down * 0.05f, p1 = d * (ww * 0.3f) + Vector3.up * (hh * 0.45f), p2 = d * (ww * 0.55f) + Vector3.up * (hh * 0.8f);
                wood.Tube(0, new[] { p0, p1, p2 }, new[] { 0.045f, 0.03f, 0.012f }, 4, r, (a, b) => Sw.Stem);
                cl.Add(new Vector4(p2.x, p2.y, p2.z, clR * Rf(r, 0.85f, 1.15f)));
            }
            cl.Add(new Vector4(0, h * 0.75f, 0, clR * 1.15f));
            var leaves = new SMMesh(1);
            Vector3 cc = V(0, h * 0.55f, 0);
            Func<Vector3, Vector3> nrm = p => ((p - cc).normalized * 0.6f + Vector3.up * 0.75f).normalized;
            foreach (var c in cl)
            {
                int n = Mathf.RoundToInt(density * c.w * c.w);
                for (int j = 0; j < n; j++)
                {
                    Vector3 p = (Vector3)c + Vector3.Scale(RandUnit(r) * (c.w * (0.35f + 0.65f * Mathf.Sqrt((float)r.NextDouble()))), V(1f, 0.8f, 1f));
                    if (p.y < 0.25f) p.y = 0.25f + Rf(r, 0f, 0.12f);
                    var q = RandRot(r); float hs = 0.6f * Rf(r, 0.88f, 1.12f) * 0.5f;
                    leaves.Card(0, p, q * Vector3.right * hs, q * Vector3.up * hs, nrm);
                }
            }
            var parts = new SMParts();
            parts.Add("Trunk", wood, Vector3.zero, -1, m.Palette);
            parts.Add("Leaves", leaves, Vector3.zero, -1, leafMat ?? m.Bush);
            return parts;
        }

        private static float Rf(float a, float b, Rng r) => SMMesh.Rf(r, a, b);

        // ================================================================ TRÁVA a KAPRADÍ
        private static SMParts Grass(Rng r, Material mat, int cards, float radius, float h, float wScale, float tiltMax)
        {
            var g = new SMMesh(1);
            Func<Vector3, Vector3> up = p => Vector3.up;   // jako originál: normály trávy kolmo nahoru
            for (int j = 0; j < cards; j++)
            {
                float ang = Rf(r, 0, 360), rad = radius * Mathf.Sqrt((float)r.NextDouble());
                Vector3 d = Dir(ang), c = d * rad;
                Vector3 tangent = Quaternion.Euler(0, Rf(r, -25, 25), 0) * Vector3.Cross(Vector3.up, d);
                float hh = h * Rf(r, 0.8f, 1.1f), w = Rf(r, 0.55f, 0.75f) * wScale;
                Vector3 upv = (Vector3.up * hh + d * (hh * Rf(r, 0f, tiltMax))) * 0.5f;
                g.Card(0, c + upv + Vector3.down * 0.03f, tangent * (w * 0.5f), upv, up, d);
            }
            var parts = new SMParts();
            parts.Add("Grass", g, Vector3.zero, -1, mat);
            return parts;
        }

        private static SMParts Fern(Rng r, Material mat, int fronds, float len, float width, float rise, float droop)
        {
            var f = new SMMesh(1);
            const int segs = 5;
            for (int i = 0; i < fronds; i++)
            {
                float yaw = i * 360f / fronds + Rf(r, -12, 12);
                Vector3 d = Dir(yaw), side = Vector3.Cross(Vector3.up, d);
                float L = len * Rf(r, 0.8f, 1.1f);
                var p = new Vector3[(segs + 1) * 3]; var n = new Vector3[p.Length]; var uv = new Vector2[p.Length];
                for (int k = 0; k <= segs; k++)
                {
                    float t = k / (float)segs;
                    Vector3 c = d * (L * t) + Vector3.up * (rise * t * (2f - t * (1f + droop))) + Vector3.down * 0.01f;
                    float w = width * (1f - 0.25f * t);
                    p[k * 3] = c; p[k * 3 + 1] = c + side * w + Vector3.down * (w * 0.2f); p[k * 3 + 2] = c - side * w + Vector3.down * (w * 0.2f);
                    uv[k * 3] = new Vector2(0.5f, t); uv[k * 3 + 1] = new Vector2(0f, t); uv[k * 3 + 2] = new Vector2(1f, t);
                    n[k * 3] = n[k * 3 + 1] = n[k * 3 + 2] = Vector3.up;
                }
                var tris = new List<int>();
                for (int k = 0; k < segs; k++)
                {
                    int a = k * 3, b = (k + 1) * 3;
                    tris.AddRange(new[] { a, a + 1, b + 1, a, b + 1, b, a, b, b + 2, a, b + 2, a + 2 });
                }
                for (int q = 0; q < tris.Count; q += 3)
                    if (Vector3.Cross(p[tris[q + 1]] - p[tris[q]], p[tris[q + 2]] - p[tris[q]]).y < 0f) { int t = tris[q + 1]; tris[q + 1] = tris[q + 2]; tris[q + 2] = t; }
                f.AddRaw(0, p, n, uv, tris.ToArray());
            }
            var parts = new SMParts();
            parts.Add("Fern", f, Vector3.zero, -1, mat);
            return parts;
        }

        // ================================================================ KAMENY
        private static SMParts Rocks(Rng r, SMMats m, Action<SMMesh, Rng> build)
        {
            var mesh = new SMMesh(2);
            build(mesh, r);
            var parts = new SMParts();
            parts.Add("Rock", mesh, Vector3.zero, -1, m.Stones, m.Palette);
            return parts;
        }

        // ================================================================ KOLO 16
        /// <summary>Shluk bambusu: štíhlé zelené stonky (paleta, mech) s listovými shluky v horní třetině.</summary>
        private static SMParts Bamboo(Rng r, SMMats m, int stems, float spread, float hMin, float hMax)
        {
            var wood = new SMMesh(1);
            var cl = new List<Vector4>();
            float maxH = 0f;
            for (int i = 0; i < stems; i++)
            {
                Vector3 d = Dir(i * 137.5f + Rf(r, -20, 20));
                Vector3 b = d * (spread * Mathf.Sqrt((float)r.NextDouble()));
                float h = Rf(r, hMin, hMax); maxH = Mathf.Max(maxH, h);
                Vector3 lean = d * Rf(r, 0.2f, 0.7f) + V(Rf(r, -0.15f, 0.15f), 0, Rf(r, -0.15f, 0.15f));
                const int segs = 9;
                var pts = new Vector3[segs + 1]; var rad = new float[segs + 1];
                float r0 = Rf(r, 0.055f, 0.085f);
                for (int k = 0; k <= segs; k++)
                {
                    float t = k / (float)segs;
                    pts[k] = b + Vector3.up * (-0.2f + (h + 0.2f) * t) + lean * (t * t);
                    rad[k] = Mathf.Lerp(r0, r0 * 0.55f, t);
                }
                wood.Tube(0, pts, rad, 5, r, (a, kk) => a % 3 == 0 ? Sw.Stem : Sw.Moss);
                for (int c = 0; c < 3; c++)
                {
                    float t = 0.62f + 0.16f * c + Rf(r, -0.04f, 0.04f);
                    Vector3 p = OnLine(pts, t) + Dir(Rf(r, 0, 360)) * Rf(r, 0.15f, 0.45f);
                    cl.Add(new Vector4(p.x, p.y, p.z, Rf(r, 0.45f, 0.62f)));
                }
            }
            var parts = new SMParts { HasLods = true, TrunkR = spread * 0.8f + 0.1f, TrunkH = maxH * 0.6f };
            parts.Add("Trunk", wood, Vector3.zero, -1, m.Palette);
            AddCrown(parts, r, cl, 60f, 0.5f, 0.55f, m.Bamboo);
            return parts;
        }

        /// <summary>Stromová kapradina: zakřivený kmínek (kůra) a růžice velkých listů nahoře.</summary>
        private static SMParts TreeFern(Rng r, SMMats m, float h, int fronds, float len)
        {
            var wood = new SMMesh(1);
            Vector3 lean = Dir(Rf(r, 0, 360)) * Rf(r, 0.15f, 0.4f);
            var pts = new[] { Vector3.down * 0.1f, Vector3.up * (h * 0.35f) + lean * 0.2f, Vector3.up * (h * 0.7f) + lean * 0.6f, Vector3.up * h + lean };
            wood.Tube(0, SMMesh.Resample(pts, 7), SMMesh.Resample(new[] { 0.16f, 0.12f, 0.11f, 0.13f }, 7), 6, r, (i, k) => i == 0 ? Sw.Root : Sw.Bark, 0.03f);
            var fern = Fern(r, m.FernTrop, fronds, len, 0.4f, 0.35f, 1.35f);
            var parts = new SMParts { TrunkR = 0.16f, TrunkH = h };
            parts.Add("Trunk", wood, Vector3.zero, -1, m.Palette);
            var it = fern.Items[0];
            parts.Add("Fronds", it.mesh, pts[3] + Vector3.down * 0.05f, -1, m.FernTrop);
            return parts;
        }

        /// <summary>Přesličky: trs tenkých článkovaných stonků (paleta).</summary>
        private static SMParts Horsetail(Rng r, SMMats m, int stems, float radius, float h)
        {
            var g = new SMMesh(1);
            for (int i = 0; i < stems; i++)
            {
                Vector3 d = Dir(Rf(r, 0, 360)); Vector3 b = d * (radius * Mathf.Sqrt((float)r.NextDouble()));
                float hh = h * Rf(r, 0.6f, 1.1f);
                Vector3 tip = b + Vector3.up * hh + d * Rf(r, 0.05f, 0.25f);
                g.Tube(0, new[] { b + Vector3.down * 0.05f, (b + tip) * 0.5f, tip }, new[] { 0.022f, 0.018f, 0.008f }, 4, r, (a, k) => a == 1 ? Sw.Stem : Sw.Moss);
                // přeslen – krátké šikmé větvičky v polovině
                for (int w = 0; w < 4; w++)
                {
                    Vector3 mid = Vector3.Lerp(b, tip, 0.55f); Vector3 dd = Dir(w * 90f + Rf(r, -20, 20));
                    g.Tube(0, new[] { mid, mid + dd * 0.12f + Vector3.up * 0.08f }, new[] { 0.008f, 0.004f }, 3, r, (a, k) => Sw.Moss);
                }
            }
            var parts = new SMParts();
            parts.Add("Stems", g, Vector3.zero, -1, m.Palette);
            return parts;
        }

        // ================================================================ KOLO 19
        /// <summary>Kolo 19: swatche v Color Pallet.png (sub 0, materiál kamenů) a Color_Palette.png (sub 1) – jen ukazatele, textury se nemění.</summary>
        internal static class Sw3
        {
            // Color Pallet.png
            public static readonly Vector2 LavaA = new Vector2(0.6943f, 0.8018f);     // (63,63,64)
            public static readonly Vector2 LavaB = new Vector2(0.7275f, 0.8682f);     // (60,64,66)
            public static readonly Vector2 LavaC = new Vector2(0.7588f, 0.8037f);     // (58,65,58)
            public static readonly Vector2 LavaBrown = new Vector2(0.2119f, 0.9053f); // (71,60,52)
            public static readonly Vector2 Ash = new Vector2(0.2471f, 0.7959f);       // (98,97,95)
            public static readonly Vector2 AshLight = new Vector2(0.7920f, 0.9033f);  // (143,143,143)
            public static readonly Vector2 Rust = new Vector2(0.6318f, 0.8076f);      // (186,78,43)
            public static readonly Vector2 RustDark = new Vector2(0.1064f, 0.8330f);  // (127,71,59)
            public static readonly Vector2 RustOrange = new Vector2(0.4443f, 0.4951f);// (165,97,50)
            public static readonly Vector2 Lime1 = new Vector2(0.9248f, 0.9014f);     // (220,220,220)
            public static readonly Vector2 Lime2 = new Vector2(0.8916f, 0.9033f);     // (210,210,210)
            public static readonly Vector2 Lime3 = new Vector2(0.2100f, 0.7959f);     // (183,177,167)
            public static readonly Vector2 LimeCream = new Vector2(0.0361f, 0.7607f); // (224,201,184)
            public static readonly Vector2 Moss = new Vector2(0.4990f, 0.8408f);      // (102,129,77)
            public static readonly Vector2 MossDark = new Vector2(0.3994f, 0.9072f);  // (85,109,78)
            public static readonly Vector2 PetWood1 = new Vector2(0.2100f, 0.8682f);  // (150,117,90)
            public static readonly Vector2 PetWood2 = new Vector2(0.3174f, 0.9053f);  // (124,95,67)
            public static readonly Vector2 PetWood3 = new Vector2(0.1768f, 0.8330f);  // (178,134,88)
            public static readonly Vector2 PetGrey = new Vector2(0.2100f, 0.7607f);   // (138,129,118)
            public static readonly Vector2 AgateWine = new Vector2(0.6162f, 0.5635f); // (138,39,61)
            public static readonly Vector2 AgateLav = new Vector2(0.5986f, 0.7744f);  // (154,159,213)
            public static readonly Vector2 AgateBlue = new Vector2(0.6318f, 0.7744f); // (102,109,186)
            public static readonly Vector2 AgateViolet = new Vector2(0.3682f, 0.7432f); // (61,66,123)
            public static readonly Vector2 AgateCream = new Vector2(0.1768f, 0.9053f); // (237,207,150)
            public static readonly Vector2 AgateCoral = new Vector2(0.4658f, 0.7744f); // (223,98,98)
            public static readonly Vector2 Ruin1 = new Vector2(0.3174f, 0.8682f);     // (210,186,148)
            public static readonly Vector2 Ruin2 = new Vector2(0.1416f, 0.7607f);     // (174,156,139)
            public static readonly Vector2 Ruin3 = new Vector2(0.2471f, 0.8682f);     // (186,145,115)
            public static readonly Vector2 Ruin4 = new Vector2(0.3174f, 0.7607f);     // (143,125,109)
            // Color_Palette.png
            public static readonly Vector2 Char = new Vector2(0.6196f, 0.2202f);      // (61,61,61)
            public static readonly Vector2 CharBrown = new Vector2(0.3735f, 0.8794f); // (90,68,45)
            public static readonly Vector2 Ember = new Vector2(0.1333f, 0.2202f);     // (152,84,28)
            public static readonly Vector2 PalmBark = new Vector2(0.1323f, 0.8794f);  // (156,132,89)
            public static readonly Vector2 PalmLight = new Vector2(0.1313f, 0.7720f); // (182,151,105)
        }

        /// <summary>Kvádr s mírně zdeformovanými rohy (opotřebený kámen), flat shading, UV podle normály.</summary>
        private static void Block(SMMesh m, int sub, Rng r, Vector3 c, Vector3 size, Quaternion rot, float jitter, Func<Vector3, Vector2> uv)
        {
            var h = size * 0.5f;
            var p = new Vector3[8];
            for (int i = 0; i < 8; i++)
            {
                var q = new Vector3((i & 1) == 0 ? -h.x : h.x, (i & 2) == 0 ? -h.y : h.y, (i & 4) == 0 ? -h.z : h.z);
                q += new Vector3(Rf(r, -jitter, jitter), (i & 2) == 0 ? 0f : Rf(r, -jitter, jitter), Rf(r, -jitter, jitter));
                p[i] = c + rot * q;
            }
            int[][] f = { new[] { 0, 2, 3, 1 }, new[] { 4, 5, 7, 6 }, new[] { 0, 1, 5, 4 }, new[] { 2, 6, 7, 3 }, new[] { 0, 4, 6, 2 }, new[] { 1, 3, 7, 5 } };
            foreach (var q in f)
            {
                Vector3 fc = (p[q[0]] + p[q[1]] + p[q[2]] + p[q[3]]) * 0.25f, o = fc - c;
                Vector3 n = o.normalized;
                m.FlatTri(sub, p[q[0]], p[q[1]], p[q[2]], o, uv(n));
                m.FlatTri(sub, p[q[0]], p[q[2]], p[q[3]], o, uv(n));
            }
        }

        /// <summary>Ohořelý pahýl: holý kmen se zlomeným vrškem, pahýly větví a tmavé kořeny (Color_Palette).</summary>
        private static SMParts Snag(Rng r, SMMats m, float h, float rad, int stubs, float lean)
        {
            var wood = new SMMesh(1);
            const int segs = 9;
            var pts = new Vector3[segs + 1]; var rr = new float[segs + 1];
            Vector3 ld = Dir(Rf(r, 0, 360));
            for (int i = 0; i <= segs; i++)
            {
                float t = i / (float)segs;
                pts[i] = Vector3.up * (-0.3f + (h + 0.3f) * t) + ld * (lean * t * t);
                if (i > 0 && i < segs) pts[i] += V(Rf(r, -0.06f, 0.06f), 0, Rf(r, -0.06f, 0.06f));
                rr[i] = Mathf.Lerp(rad, rad * 0.45f, t);
            }
            // zlomený vršek – poslední prstenec šikmo a užší
            pts[segs] += V(Rf(r, -0.2f, 0.2f), -Rf(r, 0.1f, 0.4f), Rf(r, -0.2f, 0.2f));
            wood.Tube(0, pts, rr, 7, r, (i, k) => (i + k) % 5 == 0 ? Sw3.CharBrown : Sw3.Char, 0.08f, capUv: Sw3.Ember, ridge: 0.18f, twist: 0.1f);
            for (int b = 0; b < stubs; b++)
            {
                float t = Rf(r, 0.35f, 0.85f);
                Vector3 p0 = OnLine(pts, t);
                Vector3 d = Dir(b * 137.5f + Rf(r, -20, 20)) * 0.8f + Vector3.up * Rf(r, 0.25f, 0.6f);
                float L = Rf(r, 0.6f, 1.6f) * (1.2f - t * 0.6f);
                float r0 = Mathf.Lerp(rad, rad * 0.45f, t) * 0.45f;
                wood.Tube(0, new[] { p0, p0 + d.normalized * L }, new[] { r0, r0 * 0.55f }, 5, r, (i, k) => Sw3.Char, capUv: Sw3.CharBrown);
            }
            for (int k = 0; k < 4; k++)
            {
                Vector3 d = Dir(k * 90f + Rf(r, -20, 20));
                wood.Tube(0, new[] { Vector3.up * 0.35f + d * rad * 0.3f, d * rad * 1.4f + Vector3.up * 0.12f, d * rad * 2.3f + Vector3.down * 0.08f },
                    new[] { rad * 0.45f, rad * 0.3f, rad * 0.08f }, 4, r, (i, kk) => Sw3.Char);
            }
            var parts = new SMParts { HasLods = true, TrunkR = rad, TrunkH = h * 0.8f };
            parts.Add("Trunk", wood, Vector3.zero, -1, m.Palette);
            return parts;
        }

        /// <summary>Palma: zahnutý článkovaný kmen a koruna listů (karty kapradí) ve třech LOD.</summary>
        private static SMParts Palm(Rng r, SMMats m, float h, float rad, float bend, int fronds, float len)
        {
            var wood = new SMMesh(1);
            const int segs = 12;
            var pts = new Vector3[segs + 1]; var rr = new float[segs + 1];
            Vector3 ld = Dir(Rf(r, 0, 360));
            for (int i = 0; i <= segs; i++)
            {
                float t = i / (float)segs;
                pts[i] = Vector3.up * (-0.3f + (h + 0.3f) * t) + ld * (bend * t * t);
                rr[i] = Mathf.Lerp(rad * 1.25f, rad * 0.7f, Mathf.Sqrt(t));
            }
            wood.Tube(0, pts, rr, 7, r, (i, k) => i % 2 == 0 ? Sw3.PalmBark : Sw3.PalmLight, 0.04f, capUv: Sw3.PalmBark, ridge: 0.1f);
            var parts = new SMParts { HasLods = true, TrunkR = rad, TrunkH = h * 0.85f };
            parts.Add("Trunk", wood, Vector3.zero, -1, m.Palette);
            Vector3 top = pts[segs] + Vector3.down * 0.1f;
            int[] fr = { fronds, Mathf.Max(5, fronds * 2 / 3), Mathf.Max(4, fronds / 2) };
            float[] wd = { 0.42f, 0.5f, 0.6f };
            for (int l = 0; l < 3; l++)
            {
                var f = Fern(new Rng(r.Next()), m.Palm, fr[l], len, wd[l], 0.55f, 1.6f);
                parts.Add("Leaves_LOD" + l, f.Items[0].mesh, top, l, m.Palm);
            }
            return parts;
        }

        /// <summary>Hranolový krystal (6 stran, špička) – minerály.</summary>
        private static void Crystal(SMMesh s, Rng r, Vector3 b, Vector3 dir, float len, float rad, Vector2 uvA, Vector2 uvB, int sub = 0)
        {
            dir.Normalize();
            s.Tube(sub, new[] { b - dir * 0.05f, b + dir * len * 0.78f, b + dir * len }, new[] { rad, rad * 0.92f, 0.001f }, 6, r, (i, k) => k % 2 == 0 ? uvA : uvB);
        }

        // ================================================================ KOLO 20
        /// <summary>
        /// Kolo 20: další swatche v Color Pallet.png (jen ukazatele, textury se nemění). Led a tyrkys nemají v paletě
        /// vlastní odstín – kreslí se stejnými swatchemi přes kopii materiálu kamenů s modravým tónem (SMMats.Ice).
        /// </summary>
        internal static class Sw4
        {
            public static readonly Vector2 White = new Vector2(0.3086f, 0.7305f);     // (255,255,255)
            public static readonly Vector2 Snow = new Vector2(0.9531f, 0.9062f);      // (233,233,233)
            public static readonly Vector2 Grey210 = new Vector2(0.8916f, 0.9033f);   // (210,210,210)
            public static readonly Vector2 Grey189 = new Vector2(0.8555f, 0.9102f);   // (189,189,189)
            public static readonly Vector2 Grey167 = new Vector2(0.8203f, 0.9102f);   // (167,167,167)
            public static readonly Vector2 Grey154 = new Vector2(0.6133f, 0.5352f);   // (154,154,154)
            public static readonly Vector2 Lav = new Vector2(0.5986f, 0.7744f);       // (154,159,213) → s tónem ledu světle modrá
            public static readonly Vector2 Blue = new Vector2(0.6318f, 0.7744f);      // (102,109,186) → hluboká modrá ledu
            public static readonly Vector2 Teal = new Vector2(0.3945f, 0.8477f);      // (131,185,159) → s tónem ledu tyrkysová
            public static readonly Vector2 Sulfur = new Vector2(0.7539f, 0.8398f);    // (228,193,30)
            public static readonly Vector2 SulfurLight = new Vector2(0.4258f, 0.7461f); // (240,207,55)
            public static readonly Vector2 Orange = new Vector2(0.4961f, 0.7461f);    // (226,104,63)
            public static readonly Vector2 OrangeDeep = new Vector2(0.2070f, 0.4961f); // (182,105,30)
            public static readonly Vector2 Cream = new Vector2(0.1758f, 0.4648f);     // (243,207,178)
            public static readonly Vector2 Taupe = new Vector2(0.7852f, 0.8398f);     // (113,102,97)
            public static readonly Vector2 TaupeLight = new Vector2(0.8242f, 0.8398f); // (136,123,118)
        }

        /// <summary>
        /// Kolo 20: nepravidelný hranol (kra, solná deska, terasa) – horní plocha mírně klenutá, flat shading.
        /// topUv(i) = swatch horní fasety i, sideUv = boky, botUv = spodek. Vrací body obrysu (pro collider).
        /// </summary>
        private static Vector3[] Slab(SMMesh m, int sub, Rng r, Vector3 c, float rx, float rz, int sides, float yTop, float yBot,
                                      float jitter, float dome, Func<int, Vector2> topUv, Vector2 sideUv, Vector2 botUv, float yaw = 0f)
        {
            var ring = new Vector3[sides];
            Quaternion q = Quaternion.Euler(0f, yaw, 0f);
            for (int k = 0; k < sides; k++)
            {
                float a = (k + Rf(r, -0.25f, 0.25f)) * Mathf.PI * 2f / sides;
                float f = 1f + Rf(r, -jitter, jitter);
                ring[k] = c + q * V(Mathf.Cos(a) * rx * f, 0f, Mathf.Sin(a) * rz * f);
            }
            Vector3 top = c + Vector3.up * (yTop + dome), bot = c + Vector3.up * yBot;
            for (int k = 0; k < sides; k++)
            {
                int k1 = (k + 1) % sides;
                Vector3 a0 = ring[k] + Vector3.up * (yTop + Rf(r, -0.03f, 0.03f)), a1 = ring[k1] + Vector3.up * yTop;
                Vector3 b0 = ring[k] + Vector3.up * yBot, b1 = ring[k1] + Vector3.up * yBot;
                Vector3 o = (ring[k] + ring[k1]) * 0.5f - c; o.y = 0f;
                m.FlatTri(sub, top, a0, a1, Vector3.up, topUv(k));
                m.FlatTri(sub, a0, b0, b1, o, sideUv); m.FlatTri(sub, a0, b1, a1, o, sideUv);
                m.FlatTri(sub, bot, b1, b0, Vector3.down, botUv);
            }
            return ring;
        }

        /// <summary>Kolo 20: mangrovník – kmen nesený obloukovými chůdovými kořeny, široká plochá tmavá koruna (3 LOD).</summary>
        private static SMParts Mangrove(Rng r, SMMats m, float h, float rad, int roots, float rootR, float crownW, int branches)
        {
            var wood = new SMMesh(1);
            float trunkBase = rootR * 0.55f;   // kmen začíná nad zemí – drží ho kořeny
            var pts = SMMesh.Resample(new[] { Vector3.up * trunkBase, Vector3.up * (h * 0.45f) + V(Rf(r, -0.2f, 0.2f), 0, Rf(r, -0.2f, 0.2f)), Vector3.up * h }, 8);
            wood.Tube(0, pts, SMMesh.Resample(new[] { rad, rad * 0.8f, rad * 0.45f }, 8), 7, r, (i, k) => i == 0 ? Sw.Root : Sw.Bark, 0.06f, ridge: 0.12f);
            // chůdové kořeny: oblouk z kmene dolů do kruhu kolem paty (konce mírně pod zemí)
            for (int k = 0; k < roots; k++)
            {
                Vector3 d = Dir(k * 360f / roots + Rf(r, -18, 18));
                float y0 = trunkBase + Rf(r, 0.1f, rootR * 0.6f);
                float reach = rootR * Rf(0.8f, 1.15f, r);
                var rp = new[] { Vector3.up * y0 + d * rad * 0.6f, Vector3.up * (y0 + 0.25f) + d * (reach * 0.45f), Vector3.up * (y0 * 0.45f) + d * (reach * 0.85f), Vector3.down * 0.12f + d * reach };
                wood.Tube(0, SMMesh.Resample(rp, 6), SMMesh.Resample(new[] { rad * 0.38f, rad * 0.3f, rad * 0.26f, rad * 0.22f }, 6), 5, r, (i, kk) => i % 2 == 0 ? Sw.Root : Sw.Bark, 0.04f);
            }
            var cl = new List<Vector4>();
            for (int b = 0; b < branches; b++)
            {
                float t = Rf(r, 0.55f, 0.92f);
                Vector3 p0 = OnLine(pts, t);
                Vector3 dd = Dir(b * 137.5f + Rf(r, -20, 20));
                Vector3 end = p0 + dd * (crownW * Rf(r, 0.6f, 1.0f)) + Vector3.up * Rf(r, 0.3f, 0.9f);
                wood.Tube(0, new[] { p0, (p0 + end) * 0.5f + Vector3.up * 0.25f, end }, new[] { rad * 0.45f, rad * 0.3f, rad * 0.12f }, 5, r, (i, kk) => Sw.Bark);
                cl.Add(new Vector4(end.x, end.y + 0.2f, end.z, crownW * Rf(r, 0.42f, 0.55f)));
            }
            Vector3 top = pts[pts.Length - 1];
            cl.Add(new Vector4(top.x, top.y + 0.3f, top.z, crownW * 0.55f));
            var parts = new SMParts { HasLods = true, TrunkR = rad, TrunkH = h * 0.8f };
            parts.Add("Trunk", wood, Vector3.zero, -1, m.Palette);
            AddCrown(parts, r, cl, 70f, 0.62f, 0.62f, m.Jungle);
            return parts;
        }

        /// <summary>
        /// Kolo 21: houba – mírně prohnutý třeň a klobouk (lem pod kloboukem = lupeny, vrch klobouku z rovin flat shading),
        /// volitelně bílé skvrny. Vše do submeshe <paramref name="sub"/>.
        /// </summary>
        private static void Shroom(SMMesh s, int sub, Rng r, Vector3 b, float h, float capR, float stemR, Vector2 capA, Vector2 capB, Vector2 gill, Vector2 stem, bool spots, float lean = 0.12f, float capH = 0.55f, int sides = 11, int stemSegs = 6)
        {
            Vector3 d = Dir(Rf(r, 0, 360));
            Vector3 top = b + Vector3.up * h + d * (h * lean);
            var pts = SMMesh.Resample(new[] { b + Vector3.down * 0.15f * h, b + Vector3.up * (h * 0.5f) + d * (h * lean * 0.3f), top }, stemSegs);
            s.Tube(sub, pts, SMMesh.Resample(new[] { stemR * 1.3f, stemR, stemR * 0.85f }, stemSegs), sides > 8 ? 7 : 5, r, (i, k) => k % 3 == 0 ? Sw4.Cream : stem, 0.05f, capStart: false, capEnd: false);
            var cap = new[] { top + Vector3.down * (0.05f * capR), top + Vector3.up * (0.12f * capR), top + Vector3.up * (capH * 0.55f * capR), top + Vector3.up * (capH * capR) };
            s.Tube(sub, cap, new[] { stemR * 0.9f, capR, capR * 0.72f, 0.001f }, sides, r, (i, k) => i == 0 ? gill : ((i + k) % 4 == 0 ? capB : capA), 0.04f, capStart: true, capEnd: false, capUv: gill);
            if (!spots) return;
            for (int k = 0; k < 9; k++)
            {
                float a = k * 40f + Rf(r, -12, 12);
                float rr = k < 6 ? capR * 0.82f : capR * 0.45f, yy = k < 6 ? capH * 0.42f * capR : capH * 0.78f * capR;
                Vector3 c = top + Dir(a) * rr + Vector3.up * yy;
                s.Rock(sub, r, c, V(capR * 0.11f, capR * 0.05f, capR * 0.11f), 0, 0.15f, Quaternion.FromToRotation(Vector3.up, (Dir(a) * 0.6f + Vector3.up).normalized), -999f, null, n => Sw4.White);
            }
        }

        public static List<SMDef> All()
        {
            var L = new List<SMDef>();
            void Add(string name, string cat, int seed, string refRel, string col, bool pilot, Func<Rng, SMMats, SMParts> b)
                => L.Add(new SMDef { Name = "SM_" + name, Category = cat, Seed = seed, Ref = refRel, Collider = col, Pilot = pilot, Build = b });

            // ---- stromy
            Add("Strom_Listnaty_Kulaty", "Stromy", 1106, "Foliage/Trees/Broadleaf/Tree_Broadleaf_02", "capsule", true, (r, m) => Tree(r, m, new TreeSpec
                { H = 5.6f, R = 0.17f, Lean = 0.2f, Branches = 7, BStart = 0.4f, BLen = 2.1f, ElevMin = 28, ElevMax = 58, ClR = 1.1f, TopCl = 1.45f, MidCl = 0f }));
            Add("Strom_Listnaty_Stihly", "Stromy", 1102, "Foliage/Trees/Broadleaf/Tree_Broadleaf_01", "capsule", false, (r, m) => Tree(r, m, new TreeSpec
                { H = 8.6f, R = 0.16f, Lean = 0.25f, Branches = 8, BStart = 0.35f, BLen = 1.3f, ElevMin = 45, ElevMax = 70, ClR = 1.05f, TopCl = 1.3f, MidCl = 1.1f }));
            Add("Strom_Listnaty_Rozlozity", "Stromy", 1103, "Foliage/Trees/Broadleaf/Tree_Broadleaf_03", "capsule", false, (r, m) => Tree(r, m, new TreeSpec
                { H = 5.4f, R = 0.26f, Lean = 0.15f, Branches = 7, BStart = 0.5f, BLen = 3.2f, ElevMin = 20, ElevMax = 40, ClR = 1.6f, TopCl = 1.9f, MidCl = 0f }));
            Add("Strom_Listnaty_Mlady", "Stromy", 1101, "Foliage/Trees/Broadleaf/Tree_Broadleaf_04", "capsule", false, (r, m) => Tree(r, m, new TreeSpec
                { H = 3.6f, R = 0.13f, Lean = 0.1f, Branches = 4, BStart = 0.5f, BLen = 1.1f, ElevMin = 35, ElevMax = 60, ClR = 0.95f, TopCl = 1.15f, MidCl = 0f, Card = 0.66f }));
            Add("Strom_Listnaty_Krivy", "Stromy", 1104, "Foliage/Trees/Broadleaf/Tree_Broadleaf_05", "capsule", false, (r, m) => Tree(r, m, new TreeSpec
                { H = 5.2f, R = 0.2f, Lean = 1.9f, Wobble = 0.25f, Branches = 6, BStart = 0.45f, BLen = 2.3f, ElevMin = 15, ElevMax = 45, ClR = 1.3f, TopCl = 1.5f, BiasYaw = 80f, MidCl = 0f }));
            Add("Strom_Briza", "Stromy", 1105, "Foliage/Trees/Birch/Tree_Birch_02", "capsule", false, (r, m) => Tree(r, m, new TreeSpec
                { H = 8.8f, R = 0.2f, Lean = 0.35f, Wobble = 0.05f, Branches = 9, BStart = 0.38f, BLen = 1.5f, ElevMin = 20, ElevMax = 50, ClR = 0.85f, TopCl = 0.95f, SquashY = 0.7f, Birch = true, MidCl = 0f, Density = 48f }));
            Add("Strom_Briza_Dvojita", "Stromy", 1107, "Foliage/Trees/Birch/Tree_Birch_05", "capsule", false, (r, m) => Tree(r, m, new TreeSpec
                { H = 8.2f, R = 0.17f, Lean = 1.1f, Wobble = 0.05f, Branches = 6, BStart = 0.42f, BLen = 1.3f, ElevMin = 20, ElevMax = 50, ClR = 0.8f, TopCl = 0.9f, SquashY = 0.7f, Birch = true, Stems = 2, BiasYaw = 40f, MidCl = 0f, Density = 48f }));

            // ---- keře
            Add("Ker_Kulaty", "Rostliny", 1305, "Foliage/Bushes/Bush_Short", "none", false, (r, m) => Bush(r, m, 7, 0.9f, 1.15f, 0.48f, 115f));
            Add("Ker_Nizky_Siroky", "Rostliny", 1306, "Foliage/Bushes/Bush_Short", "none", false, (r, m) => Bush(r, m, 9, 2.0f, 0.7f, 0.45f, 110f));
            Add("Ker_Vysoky", "Rostliny", 1307, "Foliage/Bushes/Bush_Tall", "none", false, (r, m) => Bush(r, m, 6, 0.75f, 1.9f, 0.5f, 115f, m.BushTall));

            // ---- tráva / kapradí
            Add("Trava_Trs", "Rostliny", 1301, "Foliage/Grass/Grass_01_Medium", "none", true, (r, m) => Grass(r, m.GrassMed, 30, 0.45f, 0.75f, 0.9f, 0.15f));
            Add("Trava_Vysoka", "Rostliny", 1302, "Foliage/Grass/Grass_01_High", "none", false, (r, m) => Grass(r, m.GrassHigh, 42, 0.6f, 1.25f, 1f, 0.12f));
            Add("Trava_Louka_Siroka", "Rostliny", 1303, "Foliage/Grass/Grass_01_Low", "none", false, (r, m) => Grass(r, m.GrassLow, 95, 1.4f, 0.5f, 1f, 0.2f));
            Add("Trava_Sucha", "Rostliny", 1304, "Foliage/Grass/Grass_02", "none", false, (r, m) => Grass(r, m.GrassDry, 36, 0.55f, 0.7f, 0.9f, 0.25f));
            Add("Kapradi", "Rostliny", 1308, "Foliage/Plants/Plant_Fern_High", "none", false, (r, m) => Fern(r, m.Fern, 12, 1.15f, 0.36f, 0.5f, 0.75f));
            Add("Kapradi_Male", "Rostliny", 1309, "Foliage/Plants/Plant_Fern_High", "none", false, (r, m) => Fern(r, m.Fern, 7, 0.7f, 0.28f, 0.35f, 0.6f));

            // ---- kameny (paleta Color Pallet, jemné fazety jako Stone N)
            Add("Balvan_Kulaty", "Skaly", 1205, "Stones/Stone 9", "mesh", true, (r, m) => Rocks(r, m, (s, q) =>
                s.Rock(0, q, V(0, 0.75f, 0), V(1.15f, 0.95f, 1.05f), 2, 0.07f, Quaternion.Euler(0, 20, 0), -0.06f)));
            Add("Kamen_Maly_A", "Skaly", 1202, "Stones/Stone 4", "mesh", false, (r, m) => Rocks(r, m, (s, q) =>
                s.Rock(0, q, V(0, 0.2f, 0), V(0.42f, 0.3f, 0.36f), 1, 0.12f, Quaternion.Euler(0, 25, 0), -0.04f)));
            Add("Kamen_Maly_B", "Skaly", 1203, "Stones/Stone 4", "mesh", false, (r, m) => Rocks(r, m, (s, q) =>
                s.Rock(0, q, V(0, 0.26f, 0), V(0.32f, 0.38f, 0.3f), 1, 0.15f, Quaternion.Euler(10, 40, -8), -0.04f)));
            Add("Kamen_Plochy", "Skaly", 1204, "Stones/Stone 4", "mesh", false, (r, m) => Rocks(r, m, (s, q) =>
                s.Rock(0, q, V(0, 0.1f, 0), V(0.9f, 0.2f, 0.7f), 1, 0.1f, Quaternion.Euler(0, 10, 0), -0.04f)));
            Add("Oblazky_Skupina", "Skaly", 1201, "Stones/Stone 4", "none", false, (r, m) => Rocks(r, m, (s, q) =>
            {
                for (int i = 0; i < 7; i++)
                {
                    float rx = Rf(q, 0.09f, 0.2f), ry = rx * Rf(q, 0.5f, 0.75f);
                    s.Rock(0, q, Dir(i * 51f) * (i == 0 ? 0f : Rf(q, 0.22f, 0.6f)) + Vector3.up * (ry * 0.55f), V(rx, ry, rx * Rf(q, 0.7f, 1f)), 1, 0.1f, Quaternion.Euler(0, Rf(q, 0, 360), 0), -0.02f);
                }
            }));
            Add("Balvan_Velky", "Skaly", 1206, "Stones/Stone 9", "mesh", false, (r, m) => Rocks(r, m, (s, q) =>
                s.Rock(0, q, V(0, 1.2f, 0), V(2.0f, 1.6f, 1.75f), 2, 0.05f, Quaternion.Euler(0, 30, 0), -0.08f)));
            Add("Balvan_Mechovy", "Skaly", 1207, "Stones/Stone 9", "mesh", false, (r, m) => Rocks(r, m, (s, q) =>
                s.Rock(0, q, V(0, 0.85f, 0), V(1.4f, 1.1f, 1.2f), 2, 0.08f, Quaternion.Euler(0, 70, 0), -0.06f, n => n.y > 0.72f ? 1 : 0, n => n.y > 0.72f ? Sw.Moss : Sw.Stone)));
            Add("Skalni_Blok_Ostry", "Skaly", 1208, "Stones/Stone 13", "mesh", false, (r, m) => Rocks(r, m, (s, q) =>
            {
                s.Rock(0, q, V(0, 1.7f, 0), V(1.2f, 2.0f, 0.95f), 1, 0.16f, Quaternion.Euler(6, 15, -8), -0.08f);
                s.Rock(0, q, V(1.0f, 0.5f, 0.5f), V(0.7f, 0.62f, 0.58f), 1, 0.14f, Quaternion.Euler(0, 60, 15), -0.08f);
            }));
            Add("Skalni_Vychoz", "Skaly", 1209, "Stones/Stone 13", "mesh", false, (r, m) => Rocks(r, m, (s, q) =>
            {
                s.Rock(0, q, V(0, 1.3f, 0), V(1.0f, 1.8f, 0.9f), 1, 0.14f, Quaternion.Euler(12, 20, -10), -0.08f);
                s.Rock(0, q, V(1.3f, 0.9f, 0.4f), V(0.8f, 1.2f, 0.7f), 1, 0.14f, Quaternion.Euler(-8, 70, 22), -0.08f);
                s.Rock(0, q, V(-1.1f, 0.7f, -0.2f), V(0.9f, 0.9f, 0.8f), 1, 0.14f, Quaternion.Euler(5, 140, -25), -0.08f);
            }));
            Add("Skalni_Jehla", "Skaly", 1210, "Stones/Stone 13", "mesh", false, (r, m) => Rocks(r, m, (s, q) =>
            {
                s.Rock(0, q, V(0, 2.6f, 0), V(0.8f, 2.9f, 0.72f), 1, 0.12f, Quaternion.Euler(4, 30, -3), -0.08f);
                s.Rock(0, q, V(0, 0.45f, 0), V(1.4f, 0.8f, 1.2f), 2, 0.08f, Quaternion.identity, -0.08f);
            }));

            // ---- dřevo (paleta Color_Palette jako originální pařezy/kmeny)
            Add("Parez", "Detaily", 1401, "Wood/Stumps/Tree_Broadleaf_Stump", "mesh", false, (r, m) =>
            {
                var s = new SMMesh(1);
                s.Tube(0, SMMesh.Resample(new[] { V(0, -0.2f, 0), V(0, 0.35f, 0), V(0.02f, 0.6f, 0.01f) }, 5), SMMesh.Resample(new[] { 0.46f, 0.4f, 0.36f }, 5), 14, r, (i, k) => i <= 1 ? Sw.Root : Sw.Bark, 0.05f, capUv: Sw.WoodIn, ridge: 0.25f, twist: 0.08f);
                for (int k = 0; k < 5; k++)
                {
                    Vector3 d = Dir(k * 72f + Rf(r, -15, 15));
                    s.Tube(0, new[] { d * 0.25f + Vector3.up * 0.32f, d * 0.48f + Vector3.up * 0.1f, d * 0.68f + Vector3.down * 0.06f }, new[] { 0.16f, 0.11f, 0.05f }, 4, r, (i, kk) => Sw.Root);
                }
                var p = new SMParts(); p.Add("Stump", s, Vector3.zero, -1, m.Palette); return p;
            });
            Add("Kmen_Padly", "Detaily", 1403, "Wood/Logs/Tree_Broadleaf_Log", "mesh", false, (r, m) =>
            {
                var s = new SMMesh(1);
                s.Tube(0, SMMesh.Resample(new[] { V(-2.4f, 0.3f, 0), V(-0.8f, 0.32f, 0.1f), V(0.8f, 0.31f, -0.05f), V(2.4f, 0.28f, 0.05f) }, 10), SMMesh.Resample(new[] { 0.36f, 0.35f, 0.33f, 0.31f }, 10), 12, r, (i, k) => Sw.Bark, 0.06f, capUv: Sw.WoodIn, ridge: 0.3f, twist: 0.13f);
                s.Tube(0, new[] { V(0.4f, 0.45f, 0.15f), V(0.7f, 0.95f, 0.55f) }, new[] { 0.1f, 0.06f }, 5, r, (i, k) => Sw.Bark, capUv: Sw.WoodIn);
                var p = new SMParts(); p.Add("Log", s, Vector3.zero, -1, m.Palette); return p;
            });
            Add("Kmen_Briza", "Detaily", 1404, "Wood/Logs/Tree_Birch_Log", "mesh", false, (r, m) =>
            {
                var s = new SMMesh(1);
                s.Tube(0, SMMesh.Resample(new[] { V(-1.6f, 0.23f, 0), V(-0.5f, 0.24f, 0.06f), V(0.6f, 0.23f, -0.04f), V(1.6f, 0.22f, 0.02f) }, 10), SMMesh.Resample(new[] { 0.28f, 0.27f, 0.26f, 0.25f }, 10), 12, r,
                    (i, k) => r.NextDouble() < 0.25 ? Sw.BirchDark : Sw.Birch, 0.05f, capUv: Sw.WoodIn, ridge: 0.22f, twist: 0.1f);
                var p = new SMParts(); p.Add("Log", s, Vector3.zero, -1, m.Palette); return p;
            });
            Add("Vetev_Spadla", "Detaily", 1405, "Wood/Logs/Tree_Broadleaf_Log", "none", false, (r, m) =>
            {
                var s = new SMMesh(1);
                s.Tube(0, new[] { V(-1.4f, 0.06f, 0), V(-0.5f, 0.07f, 0.15f), V(0.4f, 0.06f, -0.05f), V(1.3f, 0.05f, 0.2f) }, new[] { 0.075f, 0.065f, 0.05f, 0.03f }, 5, r, (i, k) => Sw.Bark);
                s.Tube(0, new[] { V(-0.5f, 0.07f, 0.15f), V(-0.2f, 0.05f, 0.6f) }, new[] { 0.035f, 0.014f }, 4, r, (i, k) => Sw.Bark);
                s.Tube(0, new[] { V(0.4f, 0.06f, -0.05f), V(0.9f, 0.04f, -0.45f) }, new[] { 0.03f, 0.012f }, 4, r, (i, k) => Sw.Bark);
                var p = new SMParts(); p.Add("Branch", s, Vector3.zero, -1, m.Palette); return p;
            });
            Add("Ker_Suchy", "Detaily", 1406, "Foliage/Bushes/Bush_Short", "none", false, (r, m) =>
            {
                var s = new SMMesh(1);
                for (int i = 0; i < 8; i++)
                {
                    float yaw = i * 45f + Rf(r, -15, 15), lean = Rf(r, 15, 40);
                    Vector3 Ld(float y, float l) => Dir(y) * Mathf.Sin(l * Mathf.Deg2Rad) + Vector3.up * Mathf.Cos(l * Mathf.Deg2Rad);
                    Vector3 p0 = Dir(yaw) * 0.04f + Vector3.down * 0.05f, p1 = p0 + Ld(yaw, lean) * 0.45f, p2 = p1 + Ld(yaw, lean + 12) * 0.4f, p3 = p2 + Ld(yaw, lean + 20) * 0.3f;
                    s.Tube(0, new[] { p0, p1, p2, p3 }, new[] { 0.04f, 0.028f, 0.018f, 0.007f }, 4, r, (a, b) => Sw.Stem);
                    Vector3 tb = i % 2 == 0 ? p1 : p2; float ty = yaw + Rf(r, -60, 60);
                    s.Tube(0, new[] { tb, tb + Ld(ty, lean + 20) * 0.28f }, new[] { 0.015f, 0.005f }, 4, r, (a, b) => Sw.Stem);
                }
                var p = new SMParts(); p.Add("Sticks", s, Vector3.zero, -1, m.Palette); return p;
            });
            // ================= KOLO 14: doplňky pro biomy =================
            Add("Strom_Akacie", "Biomy", 1501, "Foliage/Trees/Broadleaf/Tree_Broadleaf_03", "capsule", false, (r, m) => Tree(r, m, new TreeSpec
                { H = 5.0f, R = 0.2f, Lean = 0.5f, Wobble = 0.12f, Branches = 6, BStart = 0.55f, BLen = 2.6f, ElevMin = 22, ElevMax = 42,
                  ClR = 1.15f, TopCl = 1.3f, SquashY = 0.42f, MidCl = 0f, Density = 72f, Leaves = m.Savana }));
            Add("Strom_Akacie_Siroka", "Biomy", 1502, "Foliage/Trees/Broadleaf/Tree_Broadleaf_03", "capsule", false, (r, m) => Tree(r, m, new TreeSpec
                { H = 4.4f, R = 0.24f, Lean = 0.3f, Wobble = 0.1f, Branches = 7, BStart = 0.5f, BLen = 3.4f, ElevMin = 15, ElevMax = 32,
                  ClR = 1.25f, TopCl = 1.2f, SquashY = 0.38f, MidCl = 0f, Density = 70f, Leaves = m.Savana }));
            Add("Ker_Tundra", "Biomy", 1503, "Foliage/Bushes/Bush_Short", "none", false, (r, m) => Bush(r, m, 6, 0.9f, 0.55f, 0.38f, 120f, m.BushTundra));
            Add("Ker_Tundra_Nizky", "Biomy", 1504, "Foliage/Bushes/Bush_Short", "none", false, (r, m) => Bush(r, m, 5, 1.3f, 0.35f, 0.33f, 120f, m.BushTundra));
            Func<Vector3, Vector2> mesaTop = n => n.y > 0.72f ? Sw.MesaOrange : Sw.MesaRed;
            Func<Vector3, Vector2> sandTop = n => n.y > 0.72f ? Sw.SandLight : Sw.Sand;
            Add("Balvan_Mesa", "Biomy", 1505, "Stones/Stone 9", "mesh", false, (r, m) => Rocks(r, m, (s, q) =>
                s.Rock(0, q, V(0, 1.0f, 0), V(1.6f, 1.3f, 1.4f), 2, 0.07f, Quaternion.Euler(0, 15, 0), -0.08f, null, mesaTop)));
            Add("Skala_Mesa_Blok", "Biomy", 1506, "Stones/Stone 13", "mesh", false, (r, m) => Rocks(r, m, (s, q) =>
            {
                s.Rock(0, q, V(0, 2.0f, 0), V(1.3f, 2.4f, 1.1f), 1, 0.14f, Quaternion.Euler(4, 25, -5), -0.08f, null, mesaTop);
                s.Rock(0, q, V(1.1f, 0.6f, 0.5f), V(0.75f, 0.7f, 0.6f), 1, 0.14f, Quaternion.Euler(0, 60, 12), -0.08f, null, mesaTop);
            }));
            Add("Skala_Mesa_Vychoz", "Biomy", 1507, "Stones/Stone 13", "mesh", false, (r, m) => Rocks(r, m, (s, q) =>
            {
                s.Rock(0, q, V(0, 1.2f, 0), V(1.1f, 1.6f, 0.9f), 1, 0.13f, Quaternion.Euler(10, 20, -8), -0.08f, null, mesaTop);
                s.Rock(0, q, V(1.3f, 0.8f, 0.4f), V(0.8f, 1.1f, 0.7f), 1, 0.13f, Quaternion.Euler(-6, 70, 18), -0.08f, null, n => Sw.MesaOrange);
                s.Rock(0, q, V(-1.1f, 0.6f, -0.2f), V(0.9f, 0.8f, 0.8f), 1, 0.13f, Quaternion.Euler(5, 140, -20), -0.08f, null, mesaTop);
            }));
            Add("Kamen_Mesa", "Biomy", 1508, "Stones/Stone 4", "mesh", false, (r, m) => Rocks(r, m, (s, q) =>
                s.Rock(0, q, V(0, 0.22f, 0), V(0.42f, 0.32f, 0.36f), 1, 0.12f, Quaternion.Euler(0, 25, 0), -0.04f, null, mesaTop)));
            Add("Kamen_Mesa_Plochy", "Biomy", 1509, "Stones/Stone 4", "mesh", false, (r, m) => Rocks(r, m, (s, q) =>
                s.Rock(0, q, V(0, 0.1f, 0), V(0.85f, 0.2f, 0.7f), 1, 0.1f, Quaternion.Euler(0, 40, 0), -0.04f, null, n => Sw.MesaOrange)));
            Add("Balvan_Pouste", "Biomy", 1510, "Stones/Stone 9", "mesh", false, (r, m) => Rocks(r, m, (s, q) =>
                s.Rock(0, q, V(0, 0.9f, 0), V(1.5f, 1.1f, 1.3f), 2, 0.06f, Quaternion.Euler(0, 35, 0), -0.08f, null, sandTop)));
            Add("Skala_Pouste", "Biomy", 1511, "Stones/Stone 13", "mesh", false, (r, m) => Rocks(r, m, (s, q) =>
            {
                s.Rock(0, q, V(0, 1.4f, 0), V(1.4f, 1.7f, 1.1f), 1, 0.12f, Quaternion.Euler(6, 10, -6), -0.08f, null, sandTop);
                s.Rock(0, q, V(-1.0f, 0.5f, 0.6f), V(0.8f, 0.6f, 0.7f), 1, 0.12f, Quaternion.Euler(0, 80, 10), -0.08f, null, sandTop);
            }));
            Add("Kamen_Pouste", "Biomy", 1512, "Stones/Stone 4", "mesh", false, (r, m) => Rocks(r, m, (s, q) =>
                s.Rock(0, q, V(0, 0.2f, 0), V(0.42f, 0.3f, 0.37f), 1, 0.12f, Quaternion.Euler(0, 55, 0), -0.04f, null, sandTop)));
            Add("Oblazky_Pouste", "Biomy", 1513, "Stones/Stone 4", "none", false, (r, m) => Rocks(r, m, (s, q) =>
            {
                for (int i = 0; i < 6; i++)
                {
                    float rx = Rf(q, 0.1f, 0.22f), ry = rx * Rf(q, 0.5f, 0.75f);
                    s.Rock(0, q, Dir(i * 60f) * (i == 0 ? 0f : Rf(q, 0.25f, 0.6f)) + Vector3.up * (ry * 0.55f), V(rx, ry, rx * Rf(q, 0.7f, 1f)), 1, 0.1f, Quaternion.Euler(0, Rf(q, 0, 360), 0), -0.02f, null, sandTop);
                }
            }));
            // ================= KOLO 16: druhý balík biomů =================
            void Var(string name, string refRel, Func<SMMats, Dictionary<string, Material>> map)
                => L.Add(new SMDef { Name = "SM_" + name, Category = "Biomy2", Seed = 0, Ref = refRel, Collider = "variant", Variant = map });
            // černý les: herní jedle s tmavší kopií materiálu jehličí (tvar beze změny)
            Var("Smrk_Tmavy", "Foliage/Trees/Fir/Tree_Fir_Tall_01", m => new Dictionary<string, Material> { { "Fir_Branch", m.FirDark } });
            Var("Smrk_Tmavy_2", "Foliage/Trees/Fir/Tree_Fir_Tall_02", m => new Dictionary<string, Material> { { "Fir_Branch", m.FirDark } });
            Var("Smrk_Tmavy_Nizky", "Foliage/Trees/Fir/Tree_Fir_Short", m => new Dictionary<string, Material> { { "Fir_Branch", m.FirDark } });
            Add("Sekvoje", "Biomy2", 1601, "Foliage/Trees/Fir/Tree_Fir_Tall_01", "capsule", false, (r, m) => Tree(r, m, new TreeSpec
                { H = 15f, R = 0.62f, Lean = 0.12f, Wobble = 0.05f, Branches = 16, BStart = 0.5f, BLen = 1.7f, ElevMin = -5, ElevMax = 18,
                  ClR = 0.95f, TopCl = 1.15f, SquashY = 0.7f, MidCl = 0f, Density = 72f, Card = 0.62f, Leaves = m.Sequoia, RootScale = 1.7f, Roots = 7 }));
            Add("Sekvoje_Mlada", "Biomy2", 1602, "Foliage/Trees/Fir/Tree_Fir_Tall_01", "capsule", false, (r, m) => Tree(r, m, new TreeSpec
                { H = 10f, R = 0.4f, Lean = 0.15f, Wobble = 0.05f, Branches = 12, BStart = 0.45f, BLen = 1.4f, ElevMin = -5, ElevMax = 20,
                  ClR = 0.85f, TopCl = 1.0f, SquashY = 0.7f, MidCl = 0f, Density = 72f, Card = 0.6f, Leaves = m.Sequoia, RootScale = 1.5f, Roots = 6 }));
            Add("Sakura", "Biomy2", 1603, "Foliage/Trees/Broadleaf/Tree_Broadleaf_03", "capsule", false, (r, m) => Tree(r, m, new TreeSpec
                { H = 4.8f, R = 0.21f, Lean = 0.45f, Wobble = 0.16f, Branches = 8, BStart = 0.4f, BLen = 2.5f, ElevMin = 18, ElevMax = 45,
                  ClR = 1.25f, TopCl = 1.35f, SquashY = 0.68f, MidCl = 0f, Density = 82f, Leaves = m.Sakura }));
            Add("Sakura_Mlada", "Biomy2", 1604, "Foliage/Trees/Broadleaf/Tree_Broadleaf_04", "capsule", false, (r, m) => Tree(r, m, new TreeSpec
                { H = 3.6f, R = 0.15f, Lean = 0.3f, Wobble = 0.12f, Branches = 6, BStart = 0.45f, BLen = 1.6f, ElevMin = 22, ElevMax = 48,
                  ClR = 1.0f, TopCl = 1.1f, SquashY = 0.7f, MidCl = 0f, Density = 82f, Card = 0.64f, Leaves = m.Sakura }));
            Add("Bambus_Shluk", "Biomy2", 1605, "Foliage/Trees/Birch/Tree_Birch_02", "capsule", false, (r, m) => Bamboo(r, m, 13, 0.9f, 7.5f, 10.5f));
            Add("Bambus_Nizky", "Biomy2", 1606, "Foliage/Trees/Birch/Tree_Birch_02", "capsule", false, (r, m) => Bamboo(r, m, 8, 0.6f, 4.5f, 6.5f));
            Add("Strom_Dzungle", "Biomy2", 1607, "Foliage/Trees/Broadleaf/Tree_Broadleaf_03", "capsule", false, (r, m) => Tree(r, m, new TreeSpec
                { H = 7.5f, R = 0.32f, Lean = 0.2f, Wobble = 0.1f, Branches = 8, BStart = 0.55f, BLen = 3.0f, ElevMin = 15, ElevMax = 38,
                  ClR = 1.5f, TopCl = 1.8f, SquashY = 0.62f, MidCl = 0f, Density = 70f, Card = 0.74f, Leaves = m.Jungle, RootScale = 1.8f, Roots = 6, Lianas = 6 }));
            Add("Strom_Dzungle_Velky", "Biomy2", 1608, "Foliage/Trees/Broadleaf/Tree_Broadleaf_01", "capsule", false, (r, m) => Tree(r, m, new TreeSpec
                { H = 10f, R = 0.42f, Lean = 0.15f, Wobble = 0.08f, Branches = 9, BStart = 0.6f, BLen = 3.4f, ElevMin = 12, ElevMax = 32,
                  ClR = 1.6f, TopCl = 2.0f, SquashY = 0.6f, MidCl = 0f, Density = 64f, Card = 0.78f, Leaves = m.Jungle, RootScale = 2.1f, Roots = 7, Lianas = 8 }));
            Add("Kapradi_Stromova", "Biomy2", 1609, "Foliage/Plants/Plant_Fern_High", "none", false, (r, m) => TreeFern(r, m, 2.6f, 14, 1.7f));
            Add("Kapradi_Stromova_Mala", "Biomy2", 1610, "Foliage/Plants/Plant_Fern_High", "none", false, (r, m) => TreeFern(r, m, 1.5f, 11, 1.3f));
            Add("Rostlina_Velkolista", "Biomy2", 1611, "Foliage/Plants/Plant_Fern_High", "none", false, (r, m) => Fern(r, m.FernTrop, 8, 1.15f, 0.55f, 0.5f, 1.0f));
            Add("Preslicky", "Biomy2", 1612, "Foliage/Grass/Grass_01_High", "none", false, (r, m) => Horsetail(r, m, 22, 0.45f, 1.0f));
            Add("Vres", "Biomy2", 1613, "Foliage/Bushes/Bush_Short", "none", false, (r, m) => Bush(r, m, 8, 1.0f, 0.45f, 0.32f, 135f, m.Heather));
            Add("Vres_Nizky", "Biomy2", 1614, "Foliage/Bushes/Bush_Short", "none", false, (r, m) => Bush(r, m, 6, 1.4f, 0.3f, 0.28f, 135f, m.Heather));
            Add("Monolit", "Biomy2", 1615, "Stones/Stone 13", "mesh", false, (r, m) => Rocks(r, m, (s, q) =>
                s.Rock(0, q, V(0, 2.9f, 0), V(0.72f, 3.2f, 0.58f), 1, 0.1f, Quaternion.Euler(3, 20, -4), -0.1f)));
            Add("Monolit_Skupina", "Biomy2", 1616, "Stones/Stone 13", "mesh", false, (r, m) => Rocks(r, m, (s, q) =>
            {
                s.Rock(0, q, V(0, 2.4f, 0), V(0.6f, 2.7f, 0.5f), 1, 0.1f, Quaternion.Euler(4, 10, -3), -0.1f);
                s.Rock(0, q, V(1.6f, 1.6f, 0.4f), V(0.5f, 1.9f, 0.45f), 1, 0.1f, Quaternion.Euler(-6, 50, 8), -0.1f);
                s.Rock(0, q, V(-1.3f, 1.2f, 0.9f), V(0.45f, 1.4f, 0.42f), 1, 0.1f, Quaternion.Euler(5, 120, -10), -0.1f);
            }));
            Func<Vector3, Vector2> light = n => n.y > 0.6f ? Sw.SandLight : Sw.Stone;
            Add("Kamen_Svetly", "Biomy2", 1617, "Stones/Stone 4", "mesh", false, (r, m) => Rocks(r, m, (s, q) =>
                s.Rock(0, q, V(0, 0.24f, 0), V(0.46f, 0.32f, 0.4f), 1, 0.12f, Quaternion.Euler(0, 35, 0), -0.04f, null, light)));
            Add("Balvan_Svetly", "Biomy2", 1618, "Stones/Stone 9", "mesh", false, (r, m) => Rocks(r, m, (s, q) =>
                s.Rock(0, q, V(0, 0.8f, 0), V(1.3f, 1.0f, 1.15f), 2, 0.07f, Quaternion.Euler(0, 25, 0), -0.08f, null, light)));

            // ================= KOLO 19: třetí balík – teplé a geologické krajiny (Biomy3) =================
            // Kameny a stavby: jen Color Pallet (sub 0) = jeden materiál → instancují se. Dřevo: Color_Palette.
            void Add3(string name, int seed, string refRel, string col, Func<Rng, SMMats, SMParts> b) => Add(name, "Biomy3", seed, refRel, col, false, b);
            Func<Vector3, Vector2> lava = n => n.y > 0.8f ? Sw3.LavaBrown : (n.x + n.z > 0f ? Sw3.LavaA : Sw3.LavaB);
            Func<Vector3, Vector2> lime = n => n.y > 0.7f ? Sw3.Moss : (Mathf.Abs(n.x) > Mathf.Abs(n.z) ? Sw3.Lime1 : Sw3.Lime2);
            // ---- vulkán
            Add3("Lava_Jehla", 1901, "Stones/Stone 13", "mesh", (r, m) =>
            {
                var s = new SMMesh(2);
                // čedičový sloup: šestiboký, hranatý, mírně zúžený; rezavý oxidovaný vršek
                var pts = SMMesh.Resample(new[] { V(0, -0.3f, 0), V(0.08f, 3.0f, 0.05f), V(0.18f, 6.2f, 0.1f) }, 7);
                s.Tube(0, pts, SMMesh.Resample(new[] { 0.95f, 0.78f, 0.5f }, 7), 6, r, (i, k) => k % 2 == 0 ? Sw3.LavaA : (i % 3 == 0 ? Sw3.LavaC : Sw3.LavaB), 0.06f, capUv: Sw3.Rust, ridge: 0.14f);
                s.Rock(0, r, V(0.9f, 0.3f, 0.5f), V(0.7f, 0.5f, 0.6f), 1, 0.18f, Quaternion.Euler(0, 30, 10), -0.1f, null, lava);
                s.Rock(0, r, V(-0.8f, 0.25f, -0.6f), V(0.6f, 0.42f, 0.55f), 1, 0.18f, Quaternion.Euler(0, 80, -8), -0.1f, null, lava);
                var p = new SMParts(); p.Add("Rock", s, Vector3.zero, -1, m.Stones, m.Palette); return p;
            });
            Add3("Lava_Jehla_Skupina", 1902, "Stones/Stone 13", "mesh", (r, m) =>
            {
                var s = new SMMesh(2);
                // varhany: 5 sloupů různé výšky těsně u sebe
                float[] hh = { 5.4f, 4.1f, 3.3f, 4.6f, 2.4f };
                for (int i = 0; i < hh.Length; i++)
                {
                    Vector3 b = i == 0 ? Vector3.zero : Dir(i * 72f + Rf(r, -10, 10)) * 1.05f;
                    var pts = SMMesh.Resample(new[] { b + V(0, -0.3f, 0), b + V(0.05f, hh[i] * 0.5f, 0.03f), b + V(0.1f, hh[i], 0.05f) }, 5);
                    s.Tube(0, pts, SMMesh.Resample(new[] { 0.62f, 0.56f, 0.48f }, 5), 6, r, (a, k) => k % 2 == 0 ? Sw3.LavaB : Sw3.LavaA, 0.05f, capUv: i % 2 == 0 ? Sw3.Rust : Sw3.LavaBrown, ridge: 0.1f);
                }
                s.Rock(0, r, V(1.8f, 0.3f, -0.6f), V(0.7f, 0.5f, 0.6f), 1, 0.2f, Quaternion.Euler(0, 15, 6), -0.1f, null, lava);
                var p = new SMParts(); p.Add("Rock", s, Vector3.zero, -1, m.Stones, m.Palette); return p;
            });
            Add3("Lava_Balvan", 1903, "Stones/Stone 9", "mesh", (r, m) => Rocks(r, m, (s, q) =>
            {
                s.Rock(0, q, V(0, 0.75f, 0), V(1.5f, 1.05f, 1.25f), 1, 0.2f, Quaternion.Euler(0, 20, 0), -0.08f, null, lava);
                s.Rock(0, q, V(1.1f, 0.35f, 0.5f), V(0.55f, 0.45f, 0.5f), 1, 0.2f, Quaternion.Euler(0, 70, 0), -0.08f, null, n => n.y > 0.6f ? Sw3.Rust : Sw3.LavaB);
            }));
            Add3("Lava_Kamen", 1904, "Stones/Stone 4", "mesh", (r, m) => Rocks(r, m, (s, q) =>
                s.Rock(0, q, V(0, 0.2f, 0), V(0.44f, 0.3f, 0.36f), 1, 0.22f, Quaternion.Euler(0, 40, 0), -0.04f, null, lava)));
            Add3("Lava_Sut", 1905, "Stones/Stone 4", "none", (r, m) => Rocks(r, m, (s, q) =>
            {
                for (int i = 0; i < 9; i++)
                {
                    float rx = Rf(q, 0.1f, 0.24f), ry = rx * Rf(q, 0.35f, 0.6f);
                    Vector2 c = i % 3 == 0 ? Sw3.Ash : i % 3 == 1 ? Sw3.LavaB : Sw3.RustDark;
                    s.Rock(0, q, Dir(i * 40f) * (i == 0 ? 0f : Rf(q, 0.25f, 0.75f)) + Vector3.up * (ry * 0.5f), V(rx, ry, rx * Rf(q, 0.6f, 1f)), 1, 0.2f, Quaternion.Euler(0, Rf(q, 0, 360), 0), -0.02f, null, n => c);
                }
            }));
            Add3("Kamen_Rezavy", 1906, "Stones/Stone 4", "mesh", (r, m) => Rocks(r, m, (s, q) =>
                s.Rock(0, q, V(0, 0.22f, 0), V(0.42f, 0.32f, 0.38f), 1, 0.15f, Quaternion.Euler(0, 15, 0), -0.04f, null, n => n.y > 0.75f ? Sw3.RustOrange : Sw3.RustDark)));
            // ---- spálený les (Color_Palette: uhel, tmavé dřevo, žhavý řez)
            Add3("Kmen_Ohoreny", 1911, "Foliage/Trees/Broadleaf/Tree_Broadleaf_01", "capsule", (r, m) => Snag(r, m, 7.2f, 0.28f, 5, 0.35f));
            Add3("Kmen_Ohoreny_Nizky", 1912, "Foliage/Trees/Broadleaf/Tree_Broadleaf_04", "capsule", (r, m) => Snag(r, m, 3.4f, 0.32f, 2, 0.15f));
            Add3("Parez_Ohoreny", 1913, "Wood/Stumps/Tree_Broadleaf_Stump", "mesh", (r, m) =>
            {
                var s = new SMMesh(1);
                s.Tube(0, SMMesh.Resample(new[] { V(0, -0.2f, 0), V(0, 0.3f, 0), V(0.03f, 0.55f, 0.02f) }, 5), SMMesh.Resample(new[] { 0.44f, 0.39f, 0.33f }, 5), 12, r,
                    (i, k) => (i + k) % 4 == 0 ? Sw3.CharBrown : Sw3.Char, 0.07f, capUv: Sw3.Ember, ridge: 0.25f, twist: 0.08f);
                for (int k = 0; k < 4; k++)
                {
                    Vector3 d = Dir(k * 90f + Rf(r, -15, 15));
                    s.Tube(0, new[] { d * 0.25f + Vector3.up * 0.28f, d * 0.48f + Vector3.up * 0.08f, d * 0.66f + Vector3.down * 0.06f }, new[] { 0.15f, 0.1f, 0.04f }, 4, r, (i, kk) => Sw3.Char);
                }
                var p = new SMParts(); p.Add("Stump", s, Vector3.zero, -1, m.Palette); return p;
            });
            Add3("Kmen_Ohoreny_Padly", 1914, "Wood/Logs/Tree_Broadleaf_Log", "mesh", (r, m) =>
            {
                var s = new SMMesh(1);
                s.Tube(0, SMMesh.Resample(new[] { V(-2.3f, 0.3f, 0), V(-0.7f, 0.31f, 0.1f), V(0.8f, 0.3f, -0.05f), V(2.3f, 0.27f, 0.05f) }, 10), SMMesh.Resample(new[] { 0.34f, 0.33f, 0.31f, 0.26f }, 10), 10, r,
                    (i, k) => (i * 3 + k) % 7 == 0 ? Sw3.CharBrown : Sw3.Char, 0.08f, capUv: Sw3.Ember, ridge: 0.3f, twist: 0.12f);
                var p = new SMParts(); p.Add("Log", s, Vector3.zero, -1, m.Palette); return p;
            });
            // ---- kras (světlý vápenec ve vrstvách, mechové temeno)
            Add3("Vapenec_Vez", 1921, "Stones/Stone 13", "mesh", (r, m) =>
            {
                var s = new SMMesh(2);
                var pts = SMMesh.Resample(new[] { V(0, -0.4f, 0), V(0.15f, 3.5f, 0.1f), V(-0.1f, 7.5f, 0.2f), V(0.2f, 10.5f, 0.05f) }, 9);
                // štíhlá věž (poloměr ~1/6 výšky – vejde se do sloupce osazení), vrstvy po prstencích, mechové temeno
                s.Tube(0, pts, SMMesh.Resample(new[] { 1.75f, 1.45f, 1.3f, 0.95f }, 9), 8, r, (i, k) => i % 3 == 2 ? Sw3.Lime3 : (k % 2 == 0 ? Sw3.Lime1 : Sw3.Lime2), 0.14f, capUv: Sw3.Moss, ridge: 0.22f, twist: 0.25f);
                s.Rock(0, r, V(0.2f, 10.6f, 0.05f), V(1.1f, 0.6f, 1.0f), 1, 0.15f, Quaternion.Euler(0, 15, 0), 9.9f, null, lime);
                s.Rock(0, r, V(1.3f, 0.5f, 0.5f), V(0.75f, 0.75f, 0.7f), 1, 0.15f, Quaternion.Euler(0, 40, 0), -0.1f, null, lime);
                var p = new SMParts(); p.Add("Rock", s, Vector3.zero, -1, m.Stones, m.Palette); return p;
            });
            Add3("Vapenec_Jehla", 1922, "Stones/Stone 13", "mesh", (r, m) =>
            {
                var s = new SMMesh(2);
                var pts = SMMesh.Resample(new[] { V(0, -0.3f, 0), V(0.2f, 3f, 0.1f), V(0.35f, 6.4f, 0.15f) }, 8);
                s.Tube(0, pts, SMMesh.Resample(new[] { 1.0f, 0.7f, 0.18f }, 8), 7, r, (i, k) => i % 3 == 1 ? Sw3.Lime3 : Sw3.Lime1, 0.15f, capUv: Sw3.Lime2, ridge: 0.2f, twist: 0.3f);
                var p = new SMParts(); p.Add("Rock", s, Vector3.zero, -1, m.Stones, m.Palette); return p;
            });
            Add3("Vapenec_Portal", 1923, "Stones/Stone 13", "boxes", (r, m) =>
            {
                var s = new SMMesh(2);
                // dva pilíře 3,2 m od sebe (průchod 2,4 m × 3,6 m) a klenák; mechové temeno
                foreach (float x in new[] { -1.6f, 1.6f })
                {
                    var pts = SMMesh.Resample(new[] { V(x, -0.3f, 0), V(x * 1.02f, 2.0f, 0.05f), V(x * 0.98f, 4.0f, 0) }, 5);
                    s.Tube(0, pts, SMMesh.Resample(new[] { 0.85f, 0.7f, 0.75f }, 5), 7, r, (i, k) => i % 2 == 0 ? Sw3.Lime1 : Sw3.Lime3, 0.1f, capStart: true, capEnd: false, ridge: 0.15f);
                }
                s.Rock(0, r, V(0, 4.5f, 0), V(2.7f, 0.85f, 0.95f), 1, 0.12f, Quaternion.identity, 3.65f, null, lime);
                var p = new SMParts(); p.Add("Rock", s, Vector3.zero, -1, m.Stones, m.Palette);
                p.Boxes.Add((V(-1.6f, 1.9f, 0), V(1.5f, 4.4f, 1.4f), Quaternion.identity));
                p.Boxes.Add((V(1.6f, 1.9f, 0), V(1.5f, 4.4f, 1.4f), Quaternion.identity));
                p.Boxes.Add((V(0, 4.55f, 0), V(5.2f, 1.5f, 1.5f), Quaternion.identity));
                return p;
            });
            Add3("Vapenec_Balvan", 1924, "Stones/Stone 9", "mesh", (r, m) => Rocks(r, m, (s, q) =>
                s.Rock(0, q, V(0, 0.8f, 0), V(1.3f, 1.05f, 1.1f), 2, 0.09f, Quaternion.Euler(0, 30, 0), -0.08f, null, lime)));
            Add3("Vapenec_Kamen", 1925, "Stones/Stone 4", "mesh", (r, m) => Rocks(r, m, (s, q) =>
                s.Rock(0, q, V(0, 0.22f, 0), V(0.45f, 0.3f, 0.38f), 1, 0.13f, Quaternion.Euler(0, 20, 0), -0.04f, null, n => n.y > 0.6f ? Sw3.Lime1 : Sw3.Lime3)));
            // ---- zkamenělý les (kamenné dřevo, achát, ametyst)
            Func<int, int, Vector2> petUv = (i, k) => (i + k) % 3 == 0 ? Sw3.PetWood3 : (k % 2 == 0 ? Sw3.PetWood1 : Sw3.PetWood2);
            Add3("Kmen_Zkamenely", 1931, "Wood/Logs/Tree_Broadleaf_Log", "mesh", (r, m) =>
            {
                var s = new SMMesh(2);
                // kmen rozlámaný na tři špalky s mezerami, řezy achátové
                float[] xs = { -3.4f, -1.2f, -1.0f, 1.1f, 1.3f, 3.3f };
                Vector2[] cut = { Sw3.AgateCoral, Sw3.AgateLav, Sw3.AgateCream };
                for (int j = 0; j < 3; j++)
                {
                    float y0 = 0.42f - j * 0.03f;
                    var pts = SMMesh.Resample(new[] { V(xs[j * 2], y0, Rf(r, -0.1f, 0.1f)), V(xs[j * 2 + 1], y0 - 0.02f, Rf(r, -0.1f, 0.1f)) }, 4);
                    s.Tube(0, pts, SMMesh.Resample(new[] { 0.48f - j * 0.03f, 0.45f - j * 0.03f }, 4), 8, r, petUv, 0.06f, capUv: cut[j], ridge: 0.18f, twist: 0.1f);
                }
                var p = new SMParts(); p.Add("Rock", s, Vector3.zero, -1, m.Stones, m.Palette); return p;
            });
            Add3("Kmen_Zkamenely_Kratky", 1932, "Wood/Logs/Tree_Broadleaf_Log", "mesh", (r, m) =>
            {
                var s = new SMMesh(2);
                s.Tube(0, SMMesh.Resample(new[] { V(-1.7f, 0.4f, 0), V(-0.05f, 0.38f, 0.08f) }, 4), new[] { 0.44f, 0.43f, 0.42f, 0.41f }, 8, r, petUv, 0.06f, capUv: Sw3.AgateLav, ridge: 0.18f);
                s.Tube(0, SMMesh.Resample(new[] { V(0.15f, 0.37f, 0.1f), V(1.6f, 0.35f, 0.2f) }, 4), new[] { 0.41f, 0.4f, 0.39f, 0.37f }, 8, r, petUv, 0.06f, capUv: Sw3.AgateCoral, ridge: 0.18f);
                var p = new SMParts(); p.Add("Rock", s, Vector3.zero, -1, m.Stones, m.Palette); return p;
            });
            Add3("Parez_Zkamenely", 1933, "Wood/Stumps/Tree_Broadleaf_Stump", "mesh", (r, m) =>
            {
                var s = new SMMesh(2);
                s.Tube(0, SMMesh.Resample(new[] { V(0, -0.2f, 0), V(0, 0.4f, 0), V(0.04f, 0.75f, 0.02f) }, 5), SMMesh.Resample(new[] { 0.6f, 0.52f, 0.47f }, 5), 9, r, petUv, 0.07f, capUv: Sw3.AgateCream, ridge: 0.22f);
                var p = new SMParts(); p.Add("Rock", s, Vector3.zero, -1, m.Stones, m.Palette); return p;
            });
            Add3("Achat_Rez", 1934, "Stones/Stone 4", "mesh", (r, m) =>
            {
                var s = new SMMesh(2);
                // plochý výbrus: soustředné prstence (fialová kůra → levandule → krém → modré jádro)
                const int seg = 12;
                float[] rad = { 0.5f, 0.4f, 0.28f, 0.15f };
                Vector2[] col = { Sw3.AgateViolet, Sw3.AgateLav, Sw3.AgateCream, Sw3.AgateBlue };
                float top = 0.16f;
                var ring = new Vector3[rad.Length, seg];
                for (int k = 0; k < seg; k++)
                {
                    float a = k * Mathf.PI * 2f / seg; float w = 1f + Rf(r, -0.06f, 0.06f);
                    for (int j = 0; j < rad.Length; j++) ring[j, k] = V(Mathf.Cos(a) * rad[j] * w * 1.15f, top, Mathf.Sin(a) * rad[j] * w * 0.9f);
                }
                for (int k = 0; k < seg; k++)
                {
                    int k1 = (k + 1) % seg;
                    for (int j = 0; j + 1 < rad.Length; j++)
                    {
                        s.FlatTri(0, ring[j, k], ring[j, k1], ring[j + 1, k1], Vector3.up, col[j]);
                        s.FlatTri(0, ring[j, k], ring[j + 1, k1], ring[j + 1, k], Vector3.up, col[j]);
                    }
                    s.FlatTri(0, ring[rad.Length - 1, k], ring[rad.Length - 1, k1], V(0, top, 0), Vector3.up, col[rad.Length - 1]);
                    Vector3 b0 = ring[0, k] - V(0, top + 0.02f, 0), b1 = ring[0, k1] - V(0, top + 0.02f, 0);
                    Vector3 o = (ring[0, k] + ring[0, k1]) * 0.5f; o.y = 0f;
                    s.FlatTri(0, ring[0, k], b0, b1, o, Sw3.PetGrey); s.FlatTri(0, ring[0, k], b1, ring[0, k1], o, Sw3.PetGrey);
                    s.FlatTri(0, b0, b1, V(0, -0.02f, 0), Vector3.down, Sw3.PetGrey);
                }
                var p = new SMParts(); p.Add("Rock", s, Vector3.zero, -1, m.Stones, m.Palette); return p;
            });
            Add3("Mineraly", 1935, "Stones/Stone 4", "mesh", (r, m) =>
            {
                var s = new SMMesh(2);
                s.Rock(0, r, V(0, 0.15f, 0), V(0.55f, 0.3f, 0.45f), 1, 0.15f, Quaternion.identity, -0.04f, null, n => Sw3.PetGrey);
                for (int i = 0; i < 8; i++)
                {
                    Vector3 d = Dir(i * 45f + Rf(r, -15, 15)) * Rf(r, 0.1f, 0.6f) + Vector3.up;
                    Vector3 b = Dir(i * 45f) * Rf(r, 0.05f, 0.3f) + Vector3.up * 0.25f;
                    bool lav = i % 3 == 0;
                    Crystal(s, r, b, d, Rf(r, 0.35f, 0.85f), Rf(r, 0.06f, 0.12f), lav ? Sw3.AgateLav : Sw3.AgateViolet, lav ? Sw3.AgateBlue : Sw3.AgateLav);
                }
                var p = new SMParts(); p.Add("Rock", s, Vector3.zero, -1, m.Stones, m.Palette); return p;
            });
            // ---- ruiny (pískovcové kvádry, mech na vodorovných plochách, kořeny)
            Func<Vector3, Vector2> mason = n => n.y > 0.75f ? Sw3.Moss : (n.x > 0.3f ? Sw3.Ruin2 : n.z > 0.3f ? Sw3.Ruin3 : Sw3.Ruin1);
            Func<Rng, SMMats, float, int, float, SMParts> wall = (r, m, len, rows, thick) =>
            {
                var s = new SMMesh(2); var p = new SMParts();
                int cols = Mathf.Max(2, Mathf.RoundToInt(len / 1.0f));
                float bw = len / cols, bh = 0.5f;
                var colH = new int[cols];
                for (int c = 0; c < cols; c++) colH[c] = Mathf.Clamp(rows - Mathf.Abs(c - cols / 2) / 2 - r.Next(0, 3), 1, rows);   // rozpadlý, uprostřed nejvyšší
                for (int c = 0; c < cols; c++)
                {
                    for (int row = 0; row < colH[c]; row++)
                    {
                        float off = row % 2 == 0 ? 0f : bw * 0.5f;
                        float x = -len * 0.5f + (c + 0.5f) * bw + (c == cols - 1 && row % 2 == 1 ? -off : off * 0.0f);
                        Block(s, 0, r, V(x, row * bh + bh * 0.5f - 0.15f, Rf(r, -0.03f, 0.03f)), V(bw - 0.04f, bh - 0.03f, thick), Quaternion.Euler(0, Rf(r, -2, 2), 0), 0.04f, mason);
                    }
                    float hc = colH[c] * bh - 0.15f;
                    p.Boxes.Add((V(-len * 0.5f + (c + 0.5f) * bw, hc * 0.5f - 0.15f, 0), V(bw, hc + 0.3f, thick + 0.05f), Quaternion.identity));
                }
                // kořeny přes zeď (kamenné dřevo Color Pallet – jeden materiál)
                for (int k = 0; k < 2; k++)
                {
                    float x = Rf(r, -len * 0.35f, len * 0.35f); int c = Mathf.Clamp((int)((x + len * 0.5f) / bw), 0, cols - 1);
                    float hc = colH[c] * bh - 0.15f, side = k == 0 ? 1f : -1f;
                    s.Tube(0, new[] { V(x, hc + 0.05f, 0), V(x + 0.2f, hc * 0.6f, side * (thick * 0.5f + 0.06f)), V(x + 0.35f, 0.05f, side * (thick * 0.5f + 0.35f)), V(x + 0.5f, -0.15f, side * (thick * 0.5f + 0.7f)) },
                        new[] { 0.06f, 0.07f, 0.06f, 0.03f }, 4, r, (i, kk) => Sw3.PetWood2);
                }
                p.Add("Rock", s, Vector3.zero, -1, m.Stones, m.Palette); return p;
            };
            Add3("Ruina_Zed", 1941, "Stones/Stone 13", "boxes", (r, m) => wall(r, m, 5f, 6, 0.75f));
            Add3("Ruina_Zed_Nizka", 1942, "Stones/Stone 13", "boxes", (r, m) => wall(r, m, 3.2f, 3, 0.65f));
            Add3("Ruina_Sloup", 1943, "Stones/Stone 13", "mesh", (r, m) =>
            {
                var s = new SMMesh(2);
                Block(s, 0, r, V(0, 0.15f, 0), V(1.3f, 0.6f, 1.3f), Quaternion.identity, 0.04f, mason);
                s.Tube(0, SMMesh.Resample(new[] { V(0, 0.4f, 0), V(0, 4.6f, 0) }, 6), SMMesh.Resample(new[] { 0.46f, 0.4f }, 6), 10, r, (i, k) => k % 2 == 0 ? Sw3.Ruin1 : Sw3.Ruin2, 0.03f, ridge: 0.12f);
                Block(s, 0, r, V(0, 4.85f, 0), V(1.2f, 0.5f, 1.2f), Quaternion.Euler(0, 8, 2), 0.05f, mason);
                var p = new SMParts(); p.Add("Rock", s, Vector3.zero, -1, m.Stones, m.Palette); return p;
            });
            Add3("Ruina_Sloup_Zlomeny", 1944, "Stones/Stone 13", "mesh", (r, m) =>
            {
                var s = new SMMesh(2);
                Block(s, 0, r, V(0, 0.15f, 0), V(1.3f, 0.6f, 1.3f), Quaternion.identity, 0.04f, mason);
                var pts = SMMesh.Resample(new[] { V(0, 0.4f, 0), V(0.02f, 2.1f, 0.01f) }, 4);
                pts[3] += V(0.1f, -0.25f, 0.05f);   // šikmý lom
                s.Tube(0, pts, new[] { 0.46f, 0.45f, 0.44f, 0.43f }, 10, r, (i, k) => k % 2 == 0 ? Sw3.Ruin1 : Sw3.Ruin2, 0.03f, capUv: Sw3.Moss, ridge: 0.12f);
                var p = new SMParts(); p.Add("Rock", s, Vector3.zero, -1, m.Stones, m.Palette); return p;
            });
            Add3("Ruina_Oblouk", 1945, "Stones/Stone 13", "boxes", (r, m) =>
            {
                var s = new SMMesh(2); var p = new SMParts();
                // pilíře 0,8 m, světlost 2,8 m, výška průchodu 3,2 m, klenba z 7 klenáků
                foreach (float x in new[] { -1.8f, 1.8f })
                {
                    for (int row = 0; row < 6; row++)
                        Block(s, 0, r, V(x, row * 0.55f + 0.12f, 0), V(0.8f, 0.52f, 0.8f), Quaternion.Euler(0, Rf(r, -3, 3), 0), 0.04f, mason);
                    p.Boxes.Add((V(x, 1.5f, 0), V(0.85f, 3.3f, 0.85f), Quaternion.identity));
                }
                const float R = 1.8f, yc = 3.15f;
                for (int k = 0; k < 7; k++)
                {
                    float a = Mathf.PI * (k + 0.5f) / 7f;
                    Vector3 c = V(-Mathf.Cos(a) * R, yc + Mathf.Sin(a) * R, 0);
                    Quaternion q = Quaternion.Euler(0, 0, a * Mathf.Rad2Deg - 90f);
                    Block(s, 0, r, c, V(0.78f, 0.62f, 0.8f), q, 0.03f, mason);
                    p.Boxes.Add((c, V(0.8f, 0.64f, 0.82f), q));
                }
                p.Add("Rock", s, Vector3.zero, -1, m.Stones, m.Palette); return p;
            });
            Add3("Ruina_Kvadr", 1946, "Stones/Stone 4", "mesh", (r, m) =>
            {
                var s = new SMMesh(2);
                Block(s, 0, r, V(0, 0.22f, 0), V(0.9f, 0.5f, 0.6f), Quaternion.Euler(0, 0, 4), 0.05f, mason);
                var p = new SMParts(); p.Add("Rock", s, Vector3.zero, -1, m.Stones, m.Palette); return p;
            });
            // ---- oáza
            Add3("Palma", 1951, "Foliage/Trees/Broadleaf/Tree_Broadleaf_01", "capsule", (r, m) => Palm(r, m, 7.6f, 0.22f, 1.7f, 16, 2.8f));
            Add3("Palma_Mlada", 1952, "Foliage/Trees/Broadleaf/Tree_Broadleaf_04", "capsule", (r, m) => Palm(r, m, 4.2f, 0.18f, 0.9f, 13, 2.5f));
            // nízká palma (vrstva keřů): smí blíž k vodě než strom, nesmí do koryta – pravidla keřů
            Add3("Palma_Nizka", 1954, "Foliage/Bushes/Bush_Tall", "capsule", (r, m) => Palm(r, m, 2.6f, 0.16f, 0.45f, 12, 1.9f));
            Add3("Palma_Ker", 1953, "Foliage/Bushes/Bush_Tall", "none", (r, m) =>
            {
                var f = Fern(r, m.Palm, 11, 1.3f, 0.34f, 1.5f, 0.9f);
                var p = new SMParts(); p.Add("Fronds", f.Items[0].mesh, Vector3.zero, -1, m.Palm); return p;
            });
            // ================= KOLO 20: čtvrtý balík – chladné, pobřežní a geotermální krajiny (Biomy4) =================
            // Kameny/led/sůl/minerály: jeden materiál (kameny, nebo kopie s tónem ledu) → instancují se. Dřevo a listí jako stromy.
            void Add4(string name, int seed, string refRel, string col, Func<Rng, SMMats, SMParts> b) => Add(name, "Biomy4", seed, refRel, col, false, b);
            Func<Vector3, Vector2> snowRock = n => n.y > 0.55f ? Sw4.Snow : (n.y > 0.2f ? Sw4.Grey167 : (n.x + n.z > 0f ? Sw3.PetGrey : Sw4.Taupe));
            Func<Vector3, Vector2> iceUv = n => n.y > 0.55f ? Sw4.White : (n.y > -0.35f ? (Mathf.Abs(n.x) > Mathf.Abs(n.z) ? Sw4.Lav : Sw4.Grey210) : Sw4.Blue);
            // ---- zasněžené štíty
            Add4("Snih_Balvan", 2001, "Stones/Stone 9", "mesh", (r, m) => Rocks(r, m, (s, q) =>
            {
                s.Rock(0, q, V(0, 0.8f, 0), V(1.45f, 1.05f, 1.2f), 1, 0.22f, Quaternion.Euler(0, 25, 0), -0.08f, null, snowRock);
                s.Rock(0, q, V(1.15f, 0.35f, -0.4f), V(0.6f, 0.45f, 0.55f), 1, 0.2f, Quaternion.Euler(0, 70, 0), -0.08f, null, snowRock);
            }));
            Add4("Snih_Hreben", 2002, "Stones/Stone 13", "mesh", (r, m) =>
            {
                var s = new SMMesh(2);
                // zubatý hřeben: 5 skalních zubů v řadě, ostré vrcholy, sníh na vodorovných plochách
                float[] hh = { 3.4f, 5.6f, 4.4f, 6.4f, 3.0f };
                for (int i = 0; i < hh.Length; i++)
                {
                    float x = -3.2f + i * 1.6f + Rf(r, -0.2f, 0.2f);
                    s.Rock(0, r, V(x, hh[i] * 0.45f, Rf(r, -0.3f, 0.3f)), V(0.95f, hh[i] * 0.55f, 0.85f), 0, 0.28f,
                        Quaternion.Euler(Rf(r, -8, 8), Rf(r, 0, 360), Rf(r, -10, 10)), -0.1f, null, snowRock);
                }
                s.Rock(0, r, V(0, 0.45f, 0), V(4.2f, 0.7f, 1.2f), 1, 0.15f, Quaternion.identity, -0.1f, null, snowRock);
                var p = new SMParts(); p.Add("Rock", s, Vector3.zero, -1, m.Stones, m.Palette); return p;
            });
            Add4("Sut_Snih", 2003, "Stones/Stone 4", "none", (r, m) => Rocks(r, m, (s, q) =>
            {
                for (int i = 0; i < 10; i++)
                {
                    float rx = Rf(q, 0.12f, 0.28f), ry = rx * Rf(q, 0.4f, 0.7f);
                    Vector2 c = i % 4 == 0 ? Sw4.Snow : i % 4 == 1 ? Sw4.Grey154 : i % 4 == 2 ? Sw3.PetGrey : Sw4.Taupe;
                    s.Rock(0, q, Dir(i * 36f) * (i == 0 ? 0f : Rf(q, 0.25f, 0.85f)) + Vector3.up * (ry * 0.5f), V(rx, ry, rx * Rf(q, 0.6f, 1f)), 1, 0.25f, Quaternion.Euler(0, Rf(q, 0, 360), 0), -0.02f, null, n => n.y > 0.7f ? Sw4.Snow : c);
                }
            }));
            // ---- ledovec (led: materiál Ice = kameny s modravým tónem)
            Add4("Led_Kus", 2011, "Stones/Stone 4", "mesh", (r, m) =>
            {
                var s = new SMMesh(1);
                s.Rock(0, r, V(0, 0.25f, 0), V(0.5f, 0.38f, 0.42f), 0, 0.3f, Quaternion.Euler(0, 30, 8), -0.05f, null, iceUv);
                var p = new SMParts(); p.Add("Rock", s, Vector3.zero, -1, m.Ice); return p;
            });
            Add4("Led_Stena", 2012, "Stones/Stone 13", "boxes", (r, m) =>
            {
                var s = new SMMesh(1); var p = new SMParts();
                // ledová stěna: řada svislých ledových desek (séraků) různé výšky, sněhová čepice
                float x = -3.6f;
                for (int i = 0; i < 6; i++)
                {
                    float w = Rf(r, 1.0f, 1.5f), hgt = Rf(r, 3.2f, 5.4f), d = Rf(r, 1.2f, 1.7f);
                    Vector3 c = V(x + w * 0.5f, hgt * 0.5f - 0.2f, Rf(r, -0.25f, 0.25f));
                    Quaternion q = Quaternion.Euler(Rf(r, -4, 4), Rf(r, -8, 8), Rf(r, -5, 5));
                    Block(s, 0, r, c, V(w, hgt, d), q, 0.18f, iceUv);
                    p.Boxes.Add((c, V(w * 0.95f, hgt, d * 0.95f), q));
                    x += w + Rf(r, -0.05f, 0.1f);
                }
                p.Add("Rock", s, Vector3.zero, -1, m.Ice); return p;
            });
            Add4("Led_Serak", 2013, "Stones/Stone 13", "mesh", (r, m) =>
            {
                var s = new SMMesh(1);
                // shluk naklopených ledových věží
                for (int i = 0; i < 4; i++)
                {
                    float hgt = Rf(r, 2.4f, 4.6f);
                    Vector3 b = i == 0 ? Vector3.zero : Dir(i * 120f + Rf(r, -20, 20)) * 1.1f;
                    s.Rock(0, r, b + Vector3.up * (hgt * 0.45f), V(0.8f, hgt * 0.55f, 0.7f), 0, 0.22f, Quaternion.Euler(Rf(r, -12, 12), Rf(r, 0, 360), Rf(r, -12, 12)), -0.1f, null, iceUv);
                }
                var p = new SMParts(); p.Add("Rock", s, Vector3.zero, -1, m.Ice); return p;
            });
            Add4("Bludny_Balvan", 2014, "Stones/Stone 9", "mesh", (r, m) => Rocks(r, m, (s, q) =>
            {
                // bludný balvan na moréně: velký zaoblený kámen a pár menších, poprašek sněhu
                s.Rock(0, q, V(0, 1.0f, 0), V(1.6f, 1.25f, 1.4f), 2, 0.08f, Quaternion.Euler(0, 15, 0), -0.1f, null, n => n.y > 0.8f ? Sw4.Snow : (n.x > 0f ? Sw3.PetGrey : Sw4.Grey154));
                for (int i = 0; i < 3; i++)
                    s.Rock(0, q, Dir(i * 120f + 30f) * 1.7f + Vector3.up * 0.2f, V(0.35f, 0.28f, 0.3f), 1, 0.2f, Quaternion.Euler(0, i * 50f, 0), -0.03f, null, n => n.y > 0.7f ? Sw4.Snow : Sw4.Taupe);
            }));
            // ---- zamrzlý oceán: kry (spodek −0,45 m pod hladinou, vršek +0,35 m) – kreslí se jen nad existujícím mořem
            Func<int, Vector2> floeTop = k => k % 5 == 3 ? Sw4.Grey210 : Sw4.White;
            Add4("Kra_Velka", 2021, "Stones/Stone 9", "mesh", (r, m) =>
            {
                var s = new SMMesh(1);
                // dvě desky s úzkou puklinou mezi sebou (tmavá voda prosvítá)
                Slab(s, 0, r, V(-1.6f, 0, 0.2f), 2.9f, 3.6f, 9, 0.35f, -0.45f, 0.18f, 0.06f, floeTop, Sw4.Lav, Sw4.Blue, 10f);
                Slab(s, 0, r, V(2.4f, 0, -0.3f), 1.8f, 2.9f, 8, 0.32f, -0.42f, 0.2f, 0.05f, floeTop, Sw4.Lav, Sw4.Blue, -15f);
                var p = new SMParts(); p.Add("Rock", s, Vector3.zero, -1, m.Ice); return p;
            });
            Add4("Kra_Stredni", 2022, "Stones/Stone 9", "mesh", (r, m) =>
            {
                var s = new SMMesh(1);
                Slab(s, 0, r, Vector3.zero, 3.4f, 2.8f, 9, 0.35f, -0.45f, 0.2f, 0.05f, floeTop, Sw4.Lav, Sw4.Blue);
                // tlakový val: naházené ledové kry na hraně
                for (int i = 0; i < 4; i++)
                    Block(s, 0, r, V(-1.2f + i * 0.8f, 0.55f, 1.2f + Rf(r, -0.2f, 0.2f)), V(0.8f, 0.5f, 0.4f), Quaternion.Euler(Rf(r, -30, 30), Rf(r, -30, 30), Rf(r, -25, 25)), 0.08f, iceUv);
                var p = new SMParts(); p.Add("Rock", s, Vector3.zero, -1, m.Ice); return p;
            });
            Add4("Kra_Zavej", 2023, "Stones/Stone 9", "mesh", (r, m) =>
            {
                var s = new SMMesh(1);
                Slab(s, 0, r, Vector3.zero, 3.6f, 3.0f, 10, 0.35f, -0.45f, 0.16f, 0.05f, floeTop, Sw4.Lav, Sw4.Blue);
                // sněhová závěj na kře (protáhlá ve směru větru)
                s.Rock(0, r, V(0.4f, 0.35f, 0.2f), V(2.0f, 0.5f, 1.1f), 1, 0.12f, Quaternion.Euler(0, 20, 0), 0.34f, null, n => Sw4.White);
                var p = new SMParts(); p.Add("Rock", s, Vector3.zero, -1, m.Ice); return p;
            });
            Add4("Kra_Mala", 2024, "Stones/Stone 4", "mesh", (r, m) =>
            {
                var s = new SMMesh(1);
                Slab(s, 0, r, Vector3.zero, 1.7f, 1.4f, 7, 0.3f, -0.4f, 0.2f, 0.04f, floeTop, Sw4.Lav, Sw4.Blue);
                var p = new SMParts(); p.Add("Rock", s, Vector3.zero, -1, m.Ice); return p;
            });
            Add4("Zavej", 2025, "Stones/Stone 9", "mesh", (r, m) => Rocks(r, m, (s, q) =>
            {
                // závěj na břehu: dlouhá nízká sněhová duna s ostrou návětrnou hranou
                s.Rock(0, q, V(0, 0.15f, 0), V(2.4f, 0.75f, 1.2f), 1, 0.1f, Quaternion.Euler(0, 0, 6), -0.05f, null, n => n.y > 0.3f ? Sw4.White : Sw4.Snow);
                s.Rock(0, q, V(-1.6f, 0.08f, 0.4f), V(1.0f, 0.4f, 0.7f), 1, 0.1f, Quaternion.Euler(0, 30, 0), -0.05f, null, n => Sw4.Snow);
            }));
            // ---- mangrovy (Color_Palette kůra/kořeny, listí tropické džungle)
            Add4("Mangrovnik", 2031, "Foliage/Trees/Broadleaf/Tree_Broadleaf_04", "capsule", (r, m) => Mangrove(r, m, 5.6f, 0.2f, 8, 1.7f, 2.2f, 6));
            Add4("Mangrovnik_Mlady", 2032, "Foliage/Trees/Broadleaf/Tree_Broadleaf_04", "capsule", (r, m) => Mangrove(r, m, 3.4f, 0.14f, 6, 1.1f, 1.5f, 4));
            Add4("Mangrove_Koreny", 2033, "Foliage/Bushes/Bush_Tall", "none", (r, m) =>
            {
                // shluk chůdových kořenů s výhonky (mladé semenáčky) – keřová vrstva
                var parts = Bush(r, m, 5, 1.0f, 1.3f, 0.45f, 26f, m.Jungle);
                var wood = parts.Items[0].mesh;
                for (int k = 0; k < 7; k++)
                {
                    Vector3 d = Dir(k * 51f + Rf(r, -15, 15));
                    float reach = Rf(r, 0.7f, 1.1f);
                    wood.Tube(0, SMMesh.Resample(new[] { Vector3.up * 0.9f + d * 0.08f, Vector3.up * 1.0f + d * (reach * 0.45f), Vector3.up * 0.4f + d * (reach * 0.85f), Vector3.down * 0.1f + d * reach }, 5),
                        SMMesh.Resample(new[] { 0.06f, 0.05f, 0.045f, 0.035f }, 5), 4, r, (i, kk) => i % 2 == 0 ? Sw.Root : Sw.Bark);
                }
                return parts;
            });
            // ---- solné pláně (bílá sůl, šedé hrany, jeden materiál)
            Add4("Sul_Krusta", 2041, "Stones/Stone 4", "none", (r, m) =>
            {
                var s = new SMMesh(2);
                // popraskané polygonální desky s vyzdviženými okraji (nízké – po nich se chodí)
                for (int i = 0; i < 6; i++)
                {
                    Vector3 c = i == 0 ? Vector3.zero : Dir(i * 72f + Rf(r, -12, 12)) * Rf(r, 1.15f, 1.35f);
                    Slab(s, 0, r, c, Rf(r, 0.5f, 0.62f), Rf(r, 0.48f, 0.6f), 6, Rf(r, 0.06f, 0.11f), -0.05f, 0.12f, Rf(r, -0.03f, 0.02f),
                        k => k % 3 == 0 ? Sw4.Grey210 : Sw4.White, Sw4.Grey189, Sw4.Grey167);
                }
                var p = new SMParts(); p.Add("Rock", s, Vector3.zero, -1, m.Stones, m.Palette); return p;
            });
            Add4("Sul_Kopa", 2042, "Stones/Stone 9", "mesh", (r, m) => Rocks(r, m, (s, q) =>
            {
                // vyhrabaná solná kupa: kužel s vrstvami
                s.Rock(0, q, V(0, 0.7f, 0), V(1.3f, 1.0f, 1.2f), 1, 0.12f, Quaternion.identity, -0.05f, null, n => n.y > 0.75f ? Sw4.White : (n.y > 0.3f ? Sw4.Snow : Sw4.Grey189));
                s.Rock(0, q, V(0.9f, 0.25f, 0.6f), V(0.55f, 0.35f, 0.5f), 1, 0.15f, Quaternion.Euler(0, 40, 0), -0.05f, null, n => n.y > 0.5f ? Sw4.White : Sw4.Grey189);
            }));
            Add4("Sul_Krystaly", 2043, "Stones/Stone 4", "mesh", (r, m) =>
            {
                var s = new SMMesh(2);
                // shluk kubických krystalů soli (halit), naklopených
                for (int i = 0; i < 9; i++)
                {
                    float e = Rf(r, 0.22f, 0.5f);
                    Vector3 c = (i == 0 ? Vector3.zero : Dir(i * 40f + Rf(r, -15, 15)) * Rf(r, 0.3f, 0.75f)) + Vector3.up * (e * 0.35f);
                    Block(s, 0, r, c, V(e, e, e), Quaternion.Euler(Rf(r, -25, 25), Rf(r, 0, 90), Rf(r, -25, 25)), 0.02f, n => n.y > 0.5f ? Sw4.White : (n.x > 0f ? Sw4.Snow : Sw4.Grey210));
                }
                var p = new SMParts(); p.Add("Rock", s, Vector3.zero, -1, m.Stones, m.Palette); return p;
            });
            // ---- geotermální pole (travertin, sintr, síra; tyrkysová krusta = kameny s tónem ledu ve 2. submeshi)
            Func<Rng, SMMats, int, SMParts> terrace = (r, m, tiers) =>
            {
                var s = new SMMesh(2); var p = new SMParts();
                // stupňovité travertinové terasy: každý stupeň je mělká mísa s krémovým okrajem a tyrkysovou krustou na dně
                for (int t = 0; t < tiers; t++)
                {
                    float rx = 2.6f - t * 0.75f, rz = 2.1f - t * 0.6f, y = t * 0.45f;
                    Vector3 c = V(-t * 0.55f, 0f, -t * 0.35f);
                    var ring = Slab(s, 0, r, c, rx, rz, 11, y + 0.42f, -0.25f, 0.1f, 0f,
                        k => k % 2 == 0 ? Sw4.Cream : Sw3.LimeCream, Sw4.OrangeDeep, Sw4.Taupe, Rf(r, 0, 40));
                    // dno mísy (tyrkysová minerální krusta, ne voda – neprůhledné, nad okrajem nic nestojí)
                    Slab(s, 1, r, c, rx * 0.78f, rz * 0.78f, 9, y + 0.46f, y + 0.3f, 0.08f, -0.03f, k => Sw4.Teal, Sw4.Teal, Sw4.Teal, Rf(r, 0, 40));
                    // oranžové stékající krusty na čele stupně
                    for (int k = 0; k < 3; k++)
                    {
                        Vector3 e = ring[(k * 3 + 1) % ring.Length];
                        s.Rock(0, r, e + Vector3.up * (y + 0.1f), V(0.35f, 0.3f, 0.35f), 0, 0.2f, Quaternion.identity, -0.25f, null, n => n.y > 0.5f ? Sw4.Orange : Sw4.OrangeDeep);
                    }
                    p.Boxes.Add((c + Vector3.up * ((y + 0.42f - 0.25f) * 0.5f), V(rx * 1.4f, y + 0.67f, rz * 1.4f), Quaternion.identity));
                }
                p.Add("Rock", s, Vector3.zero, -1, m.Stones, m.Ice); return p;
            };
            Add4("Travertin_Terasa", 2051, "Stones/Stone 13", "boxes", (r, m) => terrace(r, m, 3));
            Add4("Travertin_Terasa_Mala", 2052, "Stones/Stone 9", "boxes", (r, m) => terrace(r, m, 2));
            Add4("Vyduch_Sirny", 2053, "Stones/Stone 9", "mesh", (r, m) =>
            {
                var s = new SMMesh(2);
                // fumarola: nízký kopeček s tmavým ústím, kolem sírové krystaly
                s.Tube(0, SMMesh.Resample(new[] { V(0, -0.2f, 0), V(0, 0.45f, 0), V(0.05f, 0.8f, 0) }, 5), SMMesh.Resample(new[] { 1.1f, 0.75f, 0.32f }, 5), 9, r,
                    (i, k) => i >= 3 ? Sw4.Sulfur : ((i + k) % 3 == 0 ? Sw4.SulfurLight : Sw4.Grey189), 0.12f, capUv: Sw3.LavaA, ridge: 0.1f);
                for (int i = 0; i < 7; i++)
                {
                    Vector3 b = Dir(i * 51f + Rf(r, -12, 12)) * Rf(r, 0.5f, 1.1f) + Vector3.up * 0.15f;
                    Crystal(s, r, b, Vector3.up + Dir(i * 51f) * 0.4f, Rf(r, 0.2f, 0.45f), Rf(r, 0.05f, 0.09f), Sw4.Sulfur, Sw4.SulfurLight);
                }
                var p = new SMParts(); p.Add("Rock", s, Vector3.zero, -1, m.Stones, m.Ice); return p;
            });
            Add4("Gejzir_Kuzel", 2054, "Stones/Stone 13", "mesh", (r, m) =>
            {
                var s = new SMMesh(2);
                // sintrový kužel gejzíru s oranžovými pruhy a tyrkysovým ústím
                s.Tube(0, SMMesh.Resample(new[] { V(0, -0.3f, 0), V(0.05f, 1.1f, 0), V(0.08f, 2.1f, 0.03f) }, 7), SMMesh.Resample(new[] { 1.5f, 0.95f, 0.45f }, 7), 10, r,
                    (i, k) => k % 4 == 0 ? Sw4.Orange : (i % 2 == 0 ? Sw4.Cream : Sw3.LimeCream), 0.1f, capEnd: false, ridge: 0.15f);
                Slab(s, 1, r, V(0.08f, 0, 0.03f), 0.42f, 0.42f, 8, 2.12f, 1.9f, 0.05f, -0.05f, k => Sw4.Teal, Sw4.Teal, Sw4.Teal);
                var p = new SMParts(); p.Add("Rock", s, Vector3.zero, -1, m.Stones, m.Ice); return p;
            });
            Add4("Krusta_Sirna", 2055, "Stones/Stone 4", "none", (r, m) =>
            {
                var s = new SMMesh(2);
                // plochá barevná krusta: síra uprostřed, oranžová a rez na okraji (soustředné nepravidelné prstence)
                Slab(s, 0, r, Vector3.zero, 1.5f, 1.2f, 10, 0.05f, -0.06f, 0.2f, 0f, k => Sw3.RustDark, Sw3.RustDark, Sw4.Taupe);
                Slab(s, 0, r, V(0.1f, 0, 0), 1.1f, 0.9f, 9, 0.08f, 0.0f, 0.2f, 0f, k => Sw4.Orange, Sw4.OrangeDeep, Sw4.Taupe);
                Slab(s, 0, r, V(0.15f, 0, 0.05f), 0.65f, 0.55f, 8, 0.11f, 0.02f, 0.2f, 0.01f, k => k % 2 == 0 ? Sw4.Sulfur : Sw4.SulfurLight, Sw4.Sulfur, Sw4.Taupe);
                var p = new SMParts(); p.Add("Rock", s, Vector3.zero, -1, m.Stones, m.Palette); return p;
            });
            // ================= KOLO 21: pátý balík – krystaly, houby, útesy (Biomy5) =================
            // Svítící kusy mají jediný materiál Glow (kopie kamenů s emisí z téže palety) → instancují se jako kameny.
            void Add5(string name, int seed, string refRel, string col, Func<Rng, SMMats, SMParts> b) => Add(name, "Biomy5", seed, refRel, col, false, b);
            Vector2[] crys = { Sw3.AgateLav, Sw3.AgateBlue, Sw4.Lav, Sw3.AgateViolet };   // světlé odstíny převládají – svítí
            Func<Vector3, Vector2> darkRock = n => n.y > 0.6f ? Sw3.Ash : (n.x + n.z > 0f ? Sw3.AgateViolet : Sw3.LavaB);
            Func<SMMesh, Material, SMParts> one = (sm, mat) => { var pp = new SMParts(); pp.Add("Rock", sm, Vector3.zero, -1, mat); return pp; };
            // ---- krystalová oblast
            Add5("Krystal_Shluk", 2101, "Stones/Stone 9", "mesh", (r, m) =>
            {
                var s = new SMMesh(1);
                s.Rock(0, r, V(0, 0.25f, 0), V(1.1f, 0.5f, 1.0f), 1, 0.2f, Quaternion.identity, -0.05f, null, darkRock);
                for (int i = 0; i < 9; i++)
                {
                    Vector3 d = Dir(i * 40f + Rf(r, -15, 15));
                    Vector3 b = (i == 0 ? Vector3.zero : d * Rf(r, 0.25f, 0.75f)) + Vector3.up * 0.35f;
                    Crystal(s, r, b, Vector3.up * 1.6f + d * Rf(r, 0.2f, 0.8f), i == 0 ? 2.2f : Rf(r, 0.7f, 1.6f), i == 0 ? 0.32f : Rf(r, 0.14f, 0.26f), crys[i % 4], crys[(i + 1) % 4]);
                }
                return one(s, m.Glow);
            });
            Add5("Krystal_Velky", 2102, "Stones/Stone 13", "mesh", (r, m) =>
            {
                var s = new SMMesh(1);
                // obří krystaly: jeden svislý hranol a tři menší naklopené z jedné skalní paty
                s.Rock(0, r, V(0, 0.3f, 0), V(1.6f, 0.6f, 1.4f), 1, 0.2f, Quaternion.identity, -0.1f, null, darkRock);
                Crystal(s, r, V(0, 0.2f, 0), V(0.1f, 1f, 0.05f), 5.2f, 0.75f, Sw3.AgateLav, Sw3.AgateBlue);
                Crystal(s, r, V(0.7f, 0.2f, 0.3f), V(0.45f, 1f, 0.2f), 3.4f, 0.5f, Sw3.AgateBlue, Sw4.Lav);
                Crystal(s, r, V(-0.6f, 0.2f, -0.4f), V(-0.4f, 1f, -0.3f), 2.8f, 0.45f, Sw3.AgateLav, Sw3.AgateViolet);
                Crystal(s, r, V(-0.2f, 0.2f, 0.7f), V(-0.1f, 1f, 0.5f), 1.8f, 0.3f, Sw4.Lav, Sw3.AgateBlue);
                return one(s, m.Glow);
            });
            Add5("Krystal_Maly", 2103, "Stones/Stone 4", "mesh", (r, m) =>
            {
                var s = new SMMesh(1);
                s.Rock(0, r, V(0, 0.08f, 0), V(0.4f, 0.16f, 0.35f), 1, 0.2f, Quaternion.identity, -0.02f, null, darkRock);
                for (int i = 0; i < 5; i++)
                {
                    Vector3 d = Dir(i * 72f + Rf(r, -20, 20));
                    Crystal(s, r, d * (i == 0 ? 0f : 0.18f) + Vector3.up * 0.1f, Vector3.up + d * 0.5f, i == 0 ? 0.6f : Rf(r, 0.25f, 0.45f), i == 0 ? 0.11f : 0.07f, crys[i % 4], crys[(i + 2) % 4]);
                }
                return one(s, m.Glow);
            });
            Add5("Mineral_Stena", 2104, "Stones/Stone 13", "mesh", (r, m) =>
            {
                var s = new SMMesh(1);
                // vrstvená minerální stěna: ustupující desky šedofialové horniny, do čela vrostlé modré žíly (bez emise)
                for (int i = 0; i < 6; i++)
                {
                    int ii = i;
                    Block(s, 0, r, V(Rf(r, -0.2f, 0.2f), i * 0.75f + 0.35f, -i * 0.12f), V(4.6f - i * 0.45f, 0.8f, 1.6f - i * 0.12f),
                        Quaternion.Euler(Rf(r, -3, 3), Rf(r, -6, 6), Rf(r, -4, 4)), 0.15f, n => n.y > 0.6f ? Sw3.PetGrey : (ii % 2 == 0 ? Sw3.AgateViolet : Sw4.TaupeLight));
                }
                for (int k = 0; k < 6; k++)
                    Crystal(s, r, V(Rf(r, -1.8f, 1.8f), Rf(r, 0.4f, 3.4f), 0.7f - Rf(r, 0f, 0.4f)), V(Rf(r, -0.3f, 0.3f), Rf(r, 0.2f, 0.6f), 1f), Rf(r, 0.3f, 0.6f), Rf(r, 0.08f, 0.14f), Sw3.AgateBlue, Sw4.Lav);
                return one(s, m.Stones);
            });
            Add5("Krystal_Jeskyne", 2105, "Stones/Stone 13", "boxes", (r, m) =>
            {
                var s = new SMMesh(2);
                // jeskynní brána (první fáze): dva skalní pilíře a klenba, průchod ~1,8 × 3,7 m; krystaly na stěnách míří vzhůru
                foreach (float x in new[] { -1.65f, 1.65f })
                    s.Rock(0, r, V(x, 1.9f, 0), V(0.85f, 2.3f, 1.6f), 1, 0.1f, Quaternion.identity, -0.2f, null, darkRock);
                s.Rock(0, r, V(0, 4.45f, 0), V(2.7f, 0.9f, 1.7f), 1, 0.1f, Quaternion.identity, 3.7f, null, darkRock);
                for (int k = 0; k < 10; k++)
                {
                    float side = k % 2 == 0 ? -1f : 1f;
                    Crystal(s, r, V(side * 0.95f, Rf(r, 0.4f, 3.0f), Rf(r, -1.2f, 1.2f)), V(-side * 0.45f, 1f, Rf(r, -0.2f, 0.2f)), Rf(r, 0.3f, 0.45f), Rf(r, 0.07f, 0.12f), crys[k % 4], crys[(k + 1) % 4], 1);
                }
                for (int k = 0; k < 5; k++)
                    Crystal(s, r, V(Rf(r, -2.2f, 2.2f), 5.1f, Rf(r, -0.9f, 0.9f)), V(Rf(r, -0.3f, 0.3f), 1f, Rf(r, -0.3f, 0.3f)), Rf(r, 0.5f, 1.1f), Rf(r, 0.12f, 0.2f), crys[k % 4], crys[(k + 3) % 4], 1);
                var p = new SMParts(); p.Add("Rock", s, Vector3.zero, -1, m.Stones, m.Glow);
                p.Boxes.Add((V(-1.65f, 1.9f, 0), V(1.5f, 4.4f, 2.6f), Quaternion.identity));
                p.Boxes.Add((V(1.65f, 1.9f, 0), V(1.5f, 4.4f, 2.6f), Quaternion.identity));
                p.Boxes.Add((V(0, 4.55f, 0), V(5.2f, 1.5f, 2.8f), Quaternion.identity));
                return p;
            });
            // ---- houbový les (třeně a klobouky z palety kamenů – jeden materiál; svítící drobné houby materiál Glow)
            Add5("Houba_Obri", 2111, null, "capsule", (r, m) =>
            {
                var s = new SMMesh(1);
                Shroom(s, 0, r, Vector3.zero, 4.2f, 2.3f, 0.38f, Sw3.AgateCoral, Sw3.Rust, Sw4.Cream, Sw3.LimeCream, true, 0.08f, 0.55f);
                var p = one(s, m.Stones); p.TrunkR = 0.42f; p.TrunkH = 4.0f; return p;
            });
            Add5("Houba_Obri_Fialova", 2112, null, "capsule", (r, m) =>
            {
                var s = new SMMesh(1);
                // štíhlá vysoká fialová houba s kuželovým kloboukem a menší sestrou u paty
                Shroom(s, 0, r, Vector3.zero, 5.6f, 1.6f, 0.3f, Sw3.AgateViolet, Sw3.AgateLav, Sw3.AgateCream, Sw4.Cream, false, 0.14f, 1.0f);
                Shroom(s, 0, r, V(0.9f, 0, 0.5f), 2.2f, 0.85f, 0.16f, Sw3.AgateLav, Sw3.AgateViolet, Sw3.AgateCream, Sw4.Cream, false, 0.2f, 0.9f);
                var p = one(s, m.Stones); p.TrunkR = 0.34f; p.TrunkH = 5.2f; return p;
            });
            Add5("Houba_Shluk", 2113, null, "none", (r, m) =>
            {
                var s = new SMMesh(1);
                Vector2[] caps = { Sw4.Orange, Sw3.CharBrown, Sw3.AgateCoral, Sw3.AgateWine };
                for (int i = 0; i < 5; i++)
                {
                    Vector3 b = i == 0 ? Vector3.zero : Dir(i * 72f + Rf(r, -20, 20)) * Rf(r, 0.5f, 0.9f);
                    float h = i == 0 ? 1.5f : Rf(r, 0.6f, 1.1f);
                    Shroom(s, 0, r, b, h, h * Rf(r, 0.45f, 0.6f), h * 0.1f, caps[i % 4], caps[(i + 1) % 4], Sw4.Cream, Sw3.LimeCream, false, 0.15f, 0.6f, 8, 3);
                }
                return one(s, m.Stones);
            });
            Add5("Houba_Svitici", 2114, null, "none", (r, m) =>
            {
                var s = new SMMesh(1);
                Vector2[] glow = { Sw4.Teal, Sw4.Lav, Sw4.SulfurLight, Sw4.Teal };
                for (int i = 0; i < 4; i++)
                {
                    Vector3 b = i == 0 ? Vector3.zero : Dir(i * 90f + Rf(r, -20, 20)) * Rf(r, 0.12f, 0.3f);
                    float h = i == 0 ? 0.34f : Rf(r, 0.14f, 0.26f);
                    Shroom(s, 0, r, b, h, h * 0.5f, h * 0.09f, glow[i % 4], glow[(i + 1) % 4], glow[i % 4], Sw4.Cream, false, 0.2f, 0.7f, 6, 3);
                }
                return one(s, m.Glow);
            });
            // ---- útesové pobřeží (křída: bílé a šedé swatche, pazourek tmavý, travnatý vršek mech; čedič lávové swatche)
            Add5("Utes_Kridovy", 2121, "Stones/Stone 13", "mesh", (r, m) =>
            {
                var s = new SMMesh(1);
                // křídový útes: čtyři ustupující lavice, svislé čelo, travnatý vršek, tmavé pásky pazourku
                for (int i = 0; i < 4; i++)
                {
                    int ii = i;
                    Block(s, 0, r, V(Rf(r, -0.3f, 0.3f), 0.9f + i * 1.7f, Rf(r, -0.2f, 0.2f) - i * 0.15f), V(5.2f - i * 0.5f, 1.8f, 3.2f - i * 0.25f),
                        Quaternion.Euler(0, Rf(r, -5, 5), 0), 0.2f, n => n.y > 0.75f ? (ii == 3 ? Sw3.Moss : Sw4.Snow) : (ii % 2 == 0 ? Sw4.White : Sw4.Grey210));
                }
                for (int k = 0; k < 3; k++)
                    Block(s, 0, r, V(0, 1.75f + k * 1.7f, 1.62f - k * 0.15f), V(4.6f - k * 0.5f, 0.12f, 0.1f), Quaternion.identity, 0.02f, n => Sw3.LavaA);
                return one(s, m.Stones);
            });
            Add5("Utes_Cedic", 2122, "Stones/Stone 13", "mesh", (r, m) =>
            {
                var s = new SMMesh(1);
                // čedičové varhany: šestiboké sloupy různé výšky v šestiúhelníkové mřížce, temena světlejší
                int n6 = 0;
                for (int q = -2; q <= 2; q++)
                for (int w = -2; w <= 2; w++)
                {
                    if (Mathf.Abs(q + w) > 2) continue;
                    float x = 0.82f * (q + w * 0.5f), z = 0.71f * w;
                    float h = Mathf.Max(0.6f, 4.2f - 0.9f * Mathf.Sqrt(x * x + z * z) + Rf(r, -0.6f, 0.6f));
                    s.Tube(0, new[] { V(x, -0.3f, z), V(x, h, z) }, new[] { 0.42f, 0.42f }, 6, r, (i, k) => (k + n6) % 3 == 0 ? Sw3.LavaB : Sw3.LavaA, 0f, capStart: false, capEnd: true, capUv: Sw3.Ash);
                    n6++;
                }
                return one(s, m.Stones);
            });
            Add5("Utes_Pilir", 2123, "Stones/Stone 13", "mesh", (r, m) =>
            {
                var s = new SMMesh(1);
                // mořský pilíř: zužující se křídová věž, trs trávy na temeni
                for (int i = 0; i < 5; i++)
                {
                    int ii = i;
                    s.Rock(0, r, V(Rf(r, -0.1f, 0.1f), 0.7f + i * 1.35f, Rf(r, -0.1f, 0.1f)), V(1.25f - i * 0.12f, 0.85f, 1.1f - i * 0.1f), 1, 0.1f, Quaternion.Euler(0, i * 37f, 0), -0.2f, null,
                        n => n.y > 0.8f && ii == 4 ? Sw3.Moss : (n.y > 0.5f ? Sw4.Snow : (ii % 2 == 0 ? Sw4.White : Sw4.Grey210)));
                }
                return one(s, m.Stones);
            });
            Add5("Utes_Brana", 2124, "Stones/Stone 13", "boxes", (r, m) =>
            {
                var s = new SMMesh(1);
                // průchozí útesová brána: dva křídové pilíře a mohutný překlad s trávou (stejný otvor jako vápencový portál)
                Func<Vector3, Vector2> ch = n => n.y > 0.75f ? Sw3.Moss : (n.x > 0f ? Sw4.White : Sw4.Grey210);
                foreach (float x in new[] { -1.65f, 1.65f })
                    s.Rock(0, r, V(x, 1.9f, 0), V(0.85f, 2.3f, 1.4f), 1, 0.1f, Quaternion.identity, -0.2f, null, ch);
                s.Rock(0, r, V(0, 4.5f, 0), V(2.8f, 0.95f, 1.5f), 1, 0.1f, Quaternion.identity, 3.7f, null, ch);
                var p = one(s, m.Stones);
                p.Boxes.Add((V(-1.65f, 1.9f, 0), V(1.5f, 4.4f, 2.4f), Quaternion.identity));
                p.Boxes.Add((V(1.65f, 1.9f, 0), V(1.5f, 4.4f, 2.4f), Quaternion.identity));
                p.Boxes.Add((V(0, 4.6f, 0), V(5.4f, 1.6f, 2.6f), Quaternion.identity));
                return p;
            });
            Add5("Utes_Balvan", 2125, "Stones/Stone 9", "mesh", (r, m) => Rocks(r, m, (s, q) =>
            {
                s.Rock(0, q, V(0, 0.7f, 0), V(1.3f, 0.95f, 1.1f), 1, 0.12f, Quaternion.Euler(0, 20, 0), -0.08f, null, n => n.y > 0.7f ? Sw4.Snow : (n.x > 0f ? Sw4.White : Sw4.Grey189));
                s.Rock(0, q, V(1.0f, 0.3f, -0.5f), V(0.5f, 0.38f, 0.45f), 1, 0.15f, Quaternion.Euler(0, 60, 0), -0.05f, null, n => n.y > 0.6f ? Sw4.White : Sw4.Grey210);
            }));
            AddK28(L);
            return L;
        }
    }
}
