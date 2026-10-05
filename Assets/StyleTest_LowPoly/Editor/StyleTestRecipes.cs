using System;
using System.Collections.Generic;
using UnityEngine;
using Rng = System.Random;

namespace Orivilon.EditorTools.StyleTest
{
    internal sealed class AssetDef
    {
        public string Name;
        public string Category;   // Stromy, Skaly, Rostliny, Detaily
        public int Seed;
        public Action<LowPolyMesh, Rng> Build;
        public string Collider;   // none | mesh | capsule:r:h
    }

    /// <summary>
    /// Deterministické recepty schvalovací sady (kolo 13). Stejný seed = stejný tvar.
    /// Pivot je vždy uprostřed paty objektu na y = 0; spodek je mírně zapuštěný pod 0.
    /// </summary>
    internal static class StyleTestRecipes
    {
        private static float Rf(Rng r, float a, float b) => LowPolyMesh.Rf(r, a, b);
        private static Vector3 V(float x, float y, float z) => new Vector3(x, y, z);
        private static Vector3 Dir(float yawDeg) => new Vector3(Mathf.Sin(yawDeg * Mathf.Deg2Rad), 0f, Mathf.Cos(yawDeg * Mathf.Deg2Rad));
        private static Vector3 Lean(float yawDeg, float leanDeg)
        {
            float l = leanDeg * Mathf.Deg2Rad;
            return Dir(yawDeg) * Mathf.Sin(l) + Vector3.up * Mathf.Cos(l);
        }

        private static Vector3[] Line(Vector3 a, Vector3 b, int segs, Rng r, float wobble)
        {
            var p = new Vector3[segs + 1];
            for (int i = 0; i <= segs; i++)
            {
                p[i] = Vector3.Lerp(a, b, i / (float)segs);
                if (i > 0 && i < segs) p[i] += V(Rf(r, -wobble, wobble), 0f, Rf(r, -wobble, wobble));
            }
            return p;
        }

        private static float[] Taper(int n, float r0, float r1)
        {
            var a = new float[n];
            for (int i = 0; i < n; i++) a[i] = Mathf.Lerp(r0, r1, i / (float)(n - 1));
            return a;
        }

        private static void Crown(LowPolyMesh m, Rng r, Vector3 c, Vector3 rad, int sub)
            => m.Blob(sub, r, c, rad, 1, 0.14f, Rf(r, 0f, 360f));

        private static void Blade(LowPolyMesh m, Rng r, int sub, Vector3 basePos, float yaw, float lean, float len, float width, float bend)
        {
            Vector3 d1 = Lean(yaw, lean), d2 = Lean(yaw, lean + bend);
            Vector3 p0 = basePos + Vector3.down * 0.04f;
            Vector3 p1 = basePos + d1 * (len * 0.5f);
            Vector3 p2 = p1 + d2 * (len * 0.5f);
            m.Tube(sub, new[] { p0, p1, p2 }, new[] { width, width * 0.65f, 0f }, 3, r, capEnd: false);
        }

        private static void Frond(LowPolyMesh m, Rng r, int sub, Vector3 basePos, float yaw, float len, float width, float rise, float droop)
        {
            Vector3 o = Dir(yaw);
            var p = new Vector3[5];
            float[] w = { width * 0.15f, width * 0.75f, width, width * 0.7f, 0f };
            for (int i = 0; i < 5; i++)
            {
                float t = i / 4f;
                p[i] = basePos + o * (len * t) + Vector3.up * (rise * t * (2f - t * (1f + droop)));
            }
            p[0] += Vector3.down * 0.03f;
            m.Tube(sub, p, w, 4, r, upHint: Vector3.up, flat: 0.18f, capEnd: false, randomPhase: false);
        }

        private static void Flower(LowPolyMesh m, Rng r, Vector3 basePos, float h, int petalSub, int centerSub, int petals, float petalLen)
        {
            Vector3 lean = Lean(Rf(r, 0f, 360f), Rf(r, 0f, 12f));
            Vector3 top = basePos + lean * h;
            Vector3 mid = basePos + Vector3.up * (h * 0.5f) + (top - basePos - Vector3.up * h) * 0.3f;
            m.Tube(Pal.GrassA, new[] { basePos + Vector3.down * 0.03f, mid, top }, new[] { 0.01f, 0.008f, 0.007f }, 3, r);
            m.Blob(centerSub, r, top + Vector3.up * 0.008f, V(0.026f, 0.02f, 0.026f), 0, 0.1f, Rf(r, 0f, 360f));
            float a0 = Rf(r, 0f, 360f);
            for (int i = 0; i < petals; i++)
            {
                float a = a0 + i * 360f / petals;
                m.Blob(petalSub, r, top + Dir(a) * (petalLen * 0.55f), V(0.022f, 0.008f, petalLen * 0.55f), 0, 0.08f,
                    Quaternion.Euler(-14f, a, 0f));
            }
        }

        private static Func<Vector3, int> UpPick(float threshold, int up, int other) => n => n.y > threshold ? up : other;

        public static List<AssetDef> All()
        {
            var L = new List<AssetDef>();
            void Add(string name, string cat, int seed, string col, Action<LowPolyMesh, Rng> build)
                => L.Add(new AssetDef { Name = "LP_" + name, Category = cat, Seed = seed, Collider = col, Build = build });

            // ================= STROMY =================
            Add("Strom_Listnaty_Mlady", "Stromy", 101, "capsule:0.13:3.1", (m, r) =>
            {
                m.Tube(Pal.Bark, Line(V(0, -0.3f, 0), V(0.1f, 3.1f, 0.05f), 4, r, 0.06f), Taper(5, 0.13f, 0.06f), 6, r, 0.05f);
                Crown(m, r, V(0.1f, 3.4f, 0.05f), V(1.1f, 1.25f, 1.1f), Pal.LeafB);
                Crown(m, r, V(0.5f, 2.85f, 0.35f), V(0.7f, 0.6f, 0.7f), Pal.LeafA);
                Crown(m, r, V(-0.4f, 3.0f, -0.3f), V(0.6f, 0.55f, 0.6f), Pal.LeafA);
            });

            Add("Strom_Listnaty_Stihly", "Stromy", 102, "capsule:0.26:8.2", (m, r) =>
            {
                m.Tube(Pal.Bark, Line(V(0, -0.3f, 0), V(0.15f, 8.2f, 0.1f), 6, r, 0.08f), Taper(7, 0.26f, 0.09f), 6, r, 0.05f);
                Crown(m, r, V(0.05f, 4.3f, 0f), V(1.0f, 1.2f, 1.0f), Pal.LeafDark);
                Crown(m, r, V(0.1f, 5.6f, 0.05f), V(1.25f, 1.5f, 1.25f), Pal.LeafA);
                Crown(m, r, V(0.15f, 7.1f, 0.1f), V(1.05f, 1.4f, 1.05f), Pal.LeafDark);
                Crown(m, r, V(0.2f, 8.4f, 0.1f), V(0.7f, 0.9f, 0.7f), Pal.LeafA);
            });

            Add("Strom_Listnaty_Rozlozity", "Stromy", 103, "capsule:0.48:4.2", (m, r) =>
            {
                m.Tube(Pal.Bark, Line(V(0, -0.3f, 0), V(0f, 4.2f, 0f), 3, r, 0.05f), Taper(4, 0.48f, 0.26f), 7, r, 0.06f);
                for (int k = 0; k < 3; k++)
                {
                    float yaw = 20f + k * 120f + Rf(r, -15f, 15f);
                    Vector3 s = V(0f, 2.8f + 0.25f * k, 0f), d = Dir(yaw);
                    Vector3 mid = s + d * 1.4f + Vector3.up * 0.9f, end = s + d * 2.7f + Vector3.up * 1.8f;
                    m.Tube(Pal.Bark, new[] { s, mid, end }, new[] { 0.24f, 0.17f, 0.10f }, 5, r);
                    Crown(m, r, end + Vector3.up * 0.4f, V(1.7f, 1.2f, 1.7f), k % 2 == 0 ? Pal.LeafA : Pal.LeafB);
                }
                Crown(m, r, V(0f, 5.1f, 0f), V(2.0f, 1.4f, 2.0f), Pal.LeafA);
                Crown(m, r, V(0.3f, 6.1f, -0.2f), V(1.4f, 1.0f, 1.4f), Pal.LeafB);
            });

            Add("Strom_Listnaty_Krivy", "Stromy", 104, "capsule:0.32:2.4", (m, r) =>
            {
                var trunk = new[] { V(0, -0.3f, 0), V(0.2f, 1.2f, 0f), V(0.9f, 2.4f, 0.1f), V(1.0f, 3.4f, 0.3f), V(0.4f, 4.5f, 0.5f), V(0.6f, 5.4f, 0.4f) };
                m.Tube(Pal.BarkDark, trunk, Taper(6, 0.32f, 0.12f), 6, r, 0.06f);
                m.Tube(Pal.BarkDark, new[] { V(0.9f, 2.4f, 0.1f), V(-0.4f, 3.4f, -0.1f), V(-1.3f, 4.2f, -0.3f) }, new[] { 0.15f, 0.10f, 0.06f }, 5, r);
                Crown(m, r, V(-1.4f, 4.4f, -0.3f), V(1.0f, 0.8f, 1.0f), Pal.LeafA);
                Crown(m, r, V(0.7f, 5.8f, 0.4f), V(1.8f, 1.2f, 1.5f), Pal.LeafDark);
                Crown(m, r, V(1.8f, 5.4f, 0.0f), V(1.2f, 0.9f, 1.1f), Pal.LeafA);
                Crown(m, r, V(-0.2f, 6.2f, 0.8f), V(1.1f, 0.9f, 1.0f), Pal.LeafA);
            });

            Add("Strom_Briza", "Stromy", 105, "capsule:0.19:8.0", (m, r) =>
            {
                Func<Vector3, int, int, int> birch = (n, ring, k) => r.NextDouble() < (ring == 0 ? 0.5 : 0.13) ? Pal.BirchMark : Pal.BirchBark;
                m.Tube(Pal.BirchBark, Line(V(0, -0.3f, 0), V(0.25f, 8.0f, 0.1f), 16, r, 0.03f), Taper(17, 0.19f, 0.06f), 6, r, 0.04f, pick: birch);
                Crown(m, r, V(0.15f, 4.9f, 0.4f), V(0.8f, 0.7f, 0.8f), Pal.LeafBirch);
                Crown(m, r, V(0.05f, 5.8f, -0.4f), V(0.9f, 0.8f, 0.9f), Pal.LeafB);
                Crown(m, r, V(0.45f, 6.6f, 0.3f), V(1.0f, 0.85f, 1.0f), Pal.LeafBirch);
                Crown(m, r, V(0.1f, 7.5f, -0.1f), V(0.95f, 0.85f, 0.95f), Pal.LeafBirch);
                Crown(m, r, V(0.3f, 8.4f, 0.1f), V(0.7f, 0.7f, 0.7f), Pal.LeafB);
            });

            Add("Strom_Listnaty_Kulaty", "Stromy", 106, "capsule:0.32:4.2", (m, r) =>
            {
                m.Tube(Pal.BarkDark, Line(V(0, -0.3f, 0), V(0f, 4.2f, 0f), 3, r, 0.05f), Taper(4, 0.32f, 0.18f), 6, r, 0.05f);
                Vector3 c = V(0f, 5.4f, 0f);
                Crown(m, r, c, V(2.3f, 2.0f, 2.3f), Pal.LeafA);
                for (int k = 0; k < 5; k++)
                {
                    float yaw = k * 72f + Rf(r, -15f, 15f), el = Rf(r, 10f, 45f);
                    Vector3 d = Dir(yaw) * Mathf.Cos(el * Mathf.Deg2Rad) + Vector3.up * Mathf.Sin(el * Mathf.Deg2Rad);
                    float s = Rf(r, 1.0f, 1.2f);
                    Crown(m, r, c + Vector3.Scale(d, V(1.9f, 1.6f, 1.9f)), V(s, s * 0.9f, s), Pal.LeafB);
                }
            });

            Add("Strom_Briza_Dvojita", "Stromy", 107, "capsule:0.2:6.0", (m, r) =>
            {
                Func<Vector3, int, int, int> birch = (n, ring, k) => r.NextDouble() < (ring == 0 ? 0.5 : 0.13) ? Pal.BirchMark : Pal.BirchBark;
                Vector3 a0 = V(0.06f, -0.3f, 0f), a1 = V(0.9f, 7.2f, 0.25f), b0 = V(-0.06f, -0.3f, 0f), b1 = V(-1.1f, 6.3f, -0.35f);
                m.Tube(Pal.BirchBark, Line(a0, a1, 14, r, 0.03f), Taper(15, 0.17f, 0.06f), 6, r, 0.04f, pick: birch);
                m.Tube(Pal.BirchBark, Line(b0, b1, 12, r, 0.03f), Taper(13, 0.15f, 0.05f), 6, r, 0.04f, pick: birch);
                float[] ta = { 0.62f, 0.76f, 0.9f, 1.02f }, tb = { 0.6f, 0.78f, 0.95f };
                for (int i = 0; i < ta.Length; i++)
                    Crown(m, r, Vector3.LerpUnclamped(a0, a1, ta[i]) + V(Rf(r, 0f, 0.5f), 0f, Rf(r, -0.4f, 0.4f)), V(0.85f, 0.75f, 0.85f), i % 2 == 0 ? Pal.LeafBirch : Pal.LeafB);
                for (int i = 0; i < tb.Length; i++)
                    Crown(m, r, Vector3.LerpUnclamped(b0, b1, tb[i]) + V(Rf(r, -0.5f, 0f), 0f, Rf(r, -0.4f, 0.4f)), V(0.8f, 0.7f, 0.8f), i % 2 == 0 ? Pal.LeafB : Pal.LeafBirch);
            });

            // ================= SKÁLY A KAMENY =================
            Add("Oblazky_Skupina", "Skaly", 201, "none", (m, r) =>
            {
                for (int i = 0; i < 7; i++)
                {
                    float ang = i * 51f + Rf(r, -15f, 15f), dist = i == 0 ? 0f : Rf(r, 0.22f, 0.6f);
                    float rx = Rf(r, 0.09f, 0.2f), ry = rx * Rf(r, 0.5f, 0.75f), rz = rx * Rf(r, 0.7f, 1f);
                    m.Blob(i % 3 == 0 ? Pal.RockMid : Pal.RockLight, r, Dir(ang) * dist + Vector3.up * (ry * 0.55f), V(rx, ry, rz), 0, 0.15f, Rf(r, 0f, 360f), -0.02f);
                }
            });
            Add("Kamen_Maly_A", "Skaly", 202, "mesh", (m, r) =>
                m.Blob(Pal.RockMid, r, V(0f, 0.2f, 0f), V(0.45f, 0.3f, 0.38f), 0, 0.2f, 25f, -0.04f));
            Add("Kamen_Maly_B", "Skaly", 203, "mesh", (m, r) =>
                m.Blob(Pal.RockLight, r, V(0f, 0.27f, 0f), V(0.35f, 0.4f, 0.3f), 1, 0.15f, Quaternion.Euler(10f, 40f, -8f), -0.04f));
            Add("Kamen_Plochy", "Skaly", 204, "mesh", (m, r) =>
                m.Blob(Pal.RockLight, r, V(0f, 0.1f, 0f), V(0.9f, 0.2f, 0.7f), 0, 0.15f, 10f, -0.04f));
            Add("Balvan_Kulaty", "Skaly", 205, "mesh", (m, r) =>
                m.Blob(Pal.RockMid, r, V(0f, 0.7f, 0f), V(1.1f, 0.95f, 1.0f), 1, 0.1f, 0f, -0.06f));
            Add("Balvan_Velky", "Skaly", 206, "mesh", (m, r) =>
                m.Blob(Pal.RockMid, r, V(0f, 1.15f, 0f), V(2.0f, 1.6f, 1.7f), 1, 0.14f, 30f, -0.08f, n => n.y < -0.3f ? Pal.RockDark : Pal.RockMid));
            Add("Balvan_Mechovy", "Skaly", 207, "mesh", (m, r) =>
                m.Blob(Pal.RockMid, r, V(0f, 0.8f, 0f), V(1.4f, 1.1f, 1.2f), 1, 0.12f, 70f, -0.06f, UpPick(0.55f, Pal.Moss, Pal.RockMid)));
            Add("Skalni_Blok_Ostry", "Skaly", 208, "mesh", (m, r) =>
            {
                m.Blob(Pal.RockDark, r, V(0f, 1.8f, 0f), V(1.3f, 2.1f, 1.0f), 0, 0.25f, Quaternion.Euler(6f, 15f, -8f), -0.08f);
                m.Blob(Pal.RockMid, r, V(1.0f, 0.5f, 0.5f), V(0.75f, 0.65f, 0.6f), 0, 0.2f, Quaternion.Euler(0f, 60f, 15f), -0.08f);
            });
            Add("Skalni_Vychoz", "Skaly", 209, "mesh", (m, r) =>
            {
                m.Blob(Pal.RockMid, r, V(0f, 1.3f, 0f), V(1.0f, 1.8f, 0.9f), 0, 0.22f, Quaternion.Euler(12f, 20f, -10f), -0.08f);
                m.Blob(Pal.RockDark, r, V(1.3f, 0.9f, 0.4f), V(0.8f, 1.2f, 0.7f), 0, 0.22f, Quaternion.Euler(-8f, 70f, 22f), -0.08f);
                m.Blob(Pal.RockMid, r, V(-1.1f, 0.7f, -0.2f), V(0.9f, 0.9f, 0.8f), 0, 0.22f, Quaternion.Euler(5f, 140f, -25f), -0.08f);
            });
            Add("Skalni_Jehla", "Skaly", 210, "mesh", (m, r) =>
            {
                m.Blob(Pal.RockMid, r, V(0f, 2.7f, 0f), V(0.85f, 3.0f, 0.75f), 0, 0.18f, Quaternion.Euler(4f, 30f, -3f), -0.08f);
                m.Blob(Pal.RockDark, r, V(0f, 0.45f, 0f), V(1.4f, 0.8f, 1.2f), 1, 0.15f, 0f, -0.08f);
            });

            // ================= TRÁVY, KEŘE, ROSTLINY =================
            Add("Trava_Trs_Nizky", "Rostliny", 301, "none", (m, r) =>
            {
                for (int i = 0; i < 12; i++)
                {
                    float yaw = i * 30f + Rf(r, -12f, 12f);
                    Blade(m, r, i % 3 == 0 ? Pal.GrassB : Pal.GrassA, Dir(yaw) * Rf(r, 0f, 0.12f), yaw, Rf(r, 15f, 35f), Rf(r, 0.25f, 0.4f), 0.035f, Rf(r, 10f, 25f));
                }
            });
            Add("Trava_Trs_Vysoky", "Rostliny", 302, "none", (m, r) =>
            {
                for (int i = 0; i < 16; i++)
                {
                    float yaw = i * 22.5f + Rf(r, -10f, 10f);
                    Blade(m, r, i % 3 == 0 ? Pal.GrassB : Pal.GrassA, Dir(yaw) * Rf(r, 0f, 0.14f), yaw, Rf(r, 5f, 25f), Rf(r, 0.6f, 0.95f), 0.04f, Rf(r, 8f, 22f));
                }
            });
            Add("Trava_Trs_Vejir", "Rostliny", 303, "none", (m, r) =>
            {
                for (int i = 0; i < 14; i++)
                {
                    float yaw = i * 25.7f + Rf(r, -10f, 10f);
                    Blade(m, r, i % 2 == 0 ? Pal.GrassB : Pal.GrassA, Dir(yaw) * Rf(r, 0f, 0.1f), yaw, Rf(r, 35f, 60f), Rf(r, 0.4f, 0.7f), 0.04f, Rf(r, 15f, 30f));
                }
            });
            Add("Trava_Trs_Suchy", "Rostliny", 304, "none", (m, r) =>
            {
                for (int i = 0; i < 14; i++)
                {
                    float yaw = i * 25.7f + Rf(r, -10f, 10f);
                    Blade(m, r, i % 4 == 0 ? Pal.DryWood : Pal.GrassDry, Dir(yaw) * Rf(r, 0f, 0.12f), yaw, Rf(r, 15f, 40f), Rf(r, 0.4f, 0.75f), 0.035f, Rf(r, 30f, 60f));
                }
            });
            Add("Ker_Kulaty", "Rostliny", 305, "none", (m, r) =>
            {
                m.Blob(Pal.LeafA, r, V(0f, 0.55f, 0f), V(0.75f, 0.6f, 0.75f), 1, 0.13f, Rf(r, 0f, 360f), -0.05f);
                m.Blob(Pal.LeafB, r, V(0.35f, 0.75f, 0.2f), V(0.5f, 0.45f, 0.5f), 1, 0.13f, Rf(r, 0f, 360f), -0.05f);
                m.Blob(Pal.LeafB, r, V(-0.3f, 0.7f, -0.25f), V(0.45f, 0.4f, 0.45f), 1, 0.13f, Rf(r, 0f, 360f), -0.05f);
            });
            Add("Ker_Nizky_Siroky", "Rostliny", 306, "none", (m, r) =>
            {
                for (int i = 0; i < 5; i++)
                {
                    float x = -0.8f + i * 0.4f;
                    m.Blob(i % 2 == 0 ? Pal.LeafA : Pal.LeafDark, r, V(x, 0.28f, Rf(r, -0.3f, 0.3f)),
                        V(Rf(r, 0.45f, 0.6f), Rf(r, 0.3f, 0.38f), Rf(r, 0.45f, 0.55f)), 1, 0.13f, Rf(r, 0f, 360f), -0.04f);
                }
            });
            Add("Ker_Vysoky", "Rostliny", 307, "none", (m, r) =>
            {
                for (int i = 0; i < 3; i++)
                {
                    float yaw = i * 120f + Rf(r, -20f, 20f);
                    m.Tube(Pal.Bark, new[] { Dir(yaw) * 0.08f + Vector3.down * 0.05f, Dir(yaw) * 0.25f + Vector3.up * 1.0f }, new[] { 0.05f, 0.03f }, 5, r);
                }
                m.Blob(Pal.LeafDark, r, V(0f, 0.95f, 0f), V(0.75f, 0.6f, 0.75f), 1, 0.13f, Rf(r, 0f, 360f));
                m.Blob(Pal.LeafA, r, V(0.15f, 1.6f, 0.1f), V(0.6f, 0.55f, 0.6f), 1, 0.13f, Rf(r, 0f, 360f));
                m.Blob(Pal.LeafA, r, V(-0.25f, 1.25f, -0.2f), V(0.5f, 0.45f, 0.5f), 1, 0.13f, Rf(r, 0f, 360f));
            });
            Add("Kapradi", "Rostliny", 308, "none", (m, r) =>
            {
                for (int i = 0; i < 9; i++)
                    Frond(m, r, i % 2 == 0 ? Pal.LeafA : Pal.LeafDark, Vector3.zero, i * 40f + Rf(r, -10f, 10f), Rf(r, 0.8f, 1.1f), 0.11f, Rf(r, 0.45f, 0.6f), 0.7f);
                m.Blob(Pal.LeafDark, r, V(0f, 0.04f, 0f), V(0.12f, 0.08f, 0.12f), 0, 0.1f, 0f, -0.02f);
            });
            Add("Rostlina_Siroke_Listy", "Rostliny", 309, "none", (m, r) =>
            {
                for (int i = 0; i < 6; i++)
                    Frond(m, r, i % 2 == 0 ? Pal.LeafB : Pal.LeafA, Vector3.zero, i * 60f + Rf(r, -12f, 12f), Rf(r, 0.55f, 0.7f), 0.17f, Rf(r, 0.4f, 0.5f), 0.5f);
                m.Blob(Pal.LeafA, r, V(0f, 0.05f, 0f), V(0.1f, 0.08f, 0.1f), 0, 0.1f, 0f, -0.02f);
            });

            // ================= DETAILY =================
            Add("Parez", "Detaily", 401, "mesh", (m, r) =>
                m.Tube(Pal.Bark, new[] { V(0, -0.2f, 0), V(0, 0.3f, 0), V(0.02f, 0.55f, 0.01f) }, new[] { 0.44f, 0.40f, 0.36f }, 8, r, 0.08f, capEndSub: Pal.WoodInner));
            Add("Parez_Koreny", "Detaily", 402, "mesh", (m, r) =>
            {
                m.Tube(Pal.BarkDark, new[] { V(0, -0.2f, 0), V(0, 0.4f, 0), V(0f, 0.75f, 0f) }, new[] { 0.36f, 0.32f, 0.30f }, 7, r, 0.1f, capEndSub: Pal.WoodInner);
                for (int k = 0; k < 5; k++)
                {
                    Vector3 d = Dir(k * 72f + Rf(r, -15f, 15f));
                    m.Tube(Pal.BarkDark, new[] { d * 0.2f + Vector3.up * 0.3f, d * 0.55f + Vector3.up * 0.12f, d * 0.95f + Vector3.down * 0.06f }, new[] { 0.13f, 0.08f, 0.02f }, 4, r, capEnd: false);
                }
            });
            Add("Kmen_Padly", "Detaily", 403, "mesh", (m, r) =>
            {
                m.Tube(Pal.Bark, new[] { V(-2.6f, 0.3f, 0f), V(-0.9f, 0.32f, 0.1f), V(0.9f, 0.31f, -0.05f), V(2.6f, 0.28f, 0.05f) },
                    new[] { 0.36f, 0.35f, 0.33f, 0.31f }, 8, r, 0.05f, capStartSub: Pal.WoodInner, capEndSub: Pal.WoodInner);
                m.Tube(Pal.Bark, new[] { V(0.4f, 0.45f, 0.15f), V(0.7f, 0.95f, 0.55f) }, new[] { 0.1f, 0.06f }, 5, r, capEndSub: Pal.WoodInner);
            });
            Add("Kmen_Padly_Mech", "Detaily", 404, "mesh", (m, r) =>
                m.Tube(Pal.BarkDark, new[] { V(-1.7f, 0.38f, 0f), V(0f, 0.4f, 0.08f), V(1.7f, 0.36f, 0f) }, new[] { 0.44f, 0.42f, 0.40f }, 8, r, 0.05f,
                    capStartSub: Pal.WoodInner, capEndSub: Pal.WoodInner, pick: (n, i, k) => n.y > 0.45f ? Pal.Moss : Pal.BarkDark));
            Add("Vetev_Spadla", "Detaily", 405, "none", (m, r) =>
            {
                m.Tube(Pal.DryWood, new[] { V(-1.4f, 0.06f, 0f), V(-0.5f, 0.07f, 0.15f), V(0.4f, 0.06f, -0.05f), V(1.3f, 0.05f, 0.2f) }, new[] { 0.075f, 0.065f, 0.05f, 0.03f }, 5, r);
                m.Tube(Pal.DryWood, new[] { V(-0.5f, 0.07f, 0.15f), V(-0.2f, 0.05f, 0.6f) }, new[] { 0.035f, 0.014f }, 4, r);
                m.Tube(Pal.DryWood, new[] { V(0.4f, 0.06f, -0.05f), V(0.9f, 0.04f, -0.45f) }, new[] { 0.03f, 0.012f }, 4, r);
            });
            Add("Ker_Suchy", "Detaily", 406, "none", (m, r) =>
            {
                for (int i = 0; i < 7; i++)
                {
                    float yaw = i * 51f + Rf(r, -15f, 15f), lean = Rf(r, 15f, 40f);
                    Vector3 p0 = Dir(yaw) * Rf(r, 0f, 0.06f) + Vector3.down * 0.05f;
                    Vector3 p1 = p0 + Lean(yaw, lean) * 0.4f, p2 = p1 + Lean(yaw, lean + 12f) * 0.35f, p3 = p2 + Lean(yaw, lean + 20f) * 0.3f;
                    m.Tube(Pal.DryWood, new[] { p0, p1, p2, p3 }, new[] { 0.035f, 0.025f, 0.016f, 0.006f }, 4, r);
                    Vector3 tb = i % 2 == 0 ? p1 : p2;
                    float ty = yaw + Rf(r, -60f, 60f);
                    m.Tube(Pal.DryWood, new[] { tb, tb + Lean(ty, lean + 20f) * 0.25f }, new[] { 0.014f, 0.004f }, 4, r);
                }
            });
            Add("Kvetiny_Zlute", "Detaily", 407, "none", (m, r) =>
            {
                for (int i = 0; i < 7; i++)
                    Flower(m, r, Dir(i * 51f + Rf(r, 0f, 30f)) * Rf(r, 0f, 0.25f), Rf(r, 0.3f, 0.5f), Pal.FlowerYellow, Pal.FlowerCenter, 5, 0.065f);
                for (int i = 0; i < 5; i++)
                    Blade(m, r, Pal.GrassA, Vector3.zero, i * 72f + Rf(r, -15f, 15f), Rf(r, 40f, 60f), Rf(r, 0.15f, 0.25f), 0.03f, 20f);
            });
            Add("Kvetiny_Bile", "Detaily", 408, "none", (m, r) =>
            {
                for (int i = 0; i < 9; i++)
                    Flower(m, r, Dir(i * 40f + Rf(r, 0f, 25f)) * Rf(r, 0f, 0.28f), Rf(r, 0.2f, 0.35f), Pal.FlowerWhite, Pal.FlowerYellow, 8, 0.06f);
                for (int i = 0; i < 5; i++)
                    Blade(m, r, Pal.GrassA, Vector3.zero, i * 72f + Rf(r, -15f, 15f), Rf(r, 40f, 60f), Rf(r, 0.15f, 0.22f), 0.03f, 20f);
            });
            Add("Kvetiny_Fialove", "Detaily", 409, "none", (m, r) =>
            {
                for (int i = 0; i < 8; i++)
                {
                    Vector3 b = Dir(i * 45f + Rf(r, 0f, 30f)) * Rf(r, 0f, 0.22f);
                    float h = Rf(r, 0.35f, 0.55f);
                    Vector3 top = b + Lean(Rf(r, 0f, 360f), Rf(r, 0f, 10f)) * h;
                    m.Tube(Pal.GrassA, new[] { b + Vector3.down * 0.03f, top }, new[] { 0.012f, 0.008f }, 3, r);
                    for (int k = 0; k < 4; k++)
                    {
                        float s = 1f - k * 0.18f;
                        m.Blob(Pal.FlowerPurple, r, Vector3.Lerp(b, top, 0.72f + k * 0.09f), V(0.028f * s, 0.04f * s, 0.028f * s), 0, 0.1f, Rf(r, 0f, 360f));
                    }
                }
                for (int i = 0; i < 6; i++)
                    Blade(m, r, Pal.GrassA, Vector3.zero, i * 60f + Rf(r, -15f, 15f), Rf(r, 35f, 55f), Rf(r, 0.18f, 0.28f), 0.03f, 20f);
            });
            Add("Houby_Skupina", "Detaily", 410, "none", (m, r) =>
            {
                for (int i = 0; i < 4; i++)
                {
                    Vector3 b = i == 0 ? Vector3.zero : Dir(i * 120f + Rf(r, -20f, 20f)) * Rf(r, 0.18f, 0.3f);
                    float h = i == 0 ? 0.26f : Rf(r, 0.1f, 0.2f), rs = i == 0 ? 0.08f : Rf(r, 0.045f, 0.065f), rc = rs * 2.6f;
                    m.Tube(Pal.MushStem, new[] { b + Vector3.down * 0.02f, b + Vector3.up * h }, new[] { rs, rs * 0.85f }, 6, r);
                    m.Blob(i == 3 ? Pal.DryWood : Pal.MushCap, r, b + Vector3.up * h, V(rc, rc * 0.55f, rc), 1, 0.08f, Rf(r, 0f, 360f));
                }
            });

            return L;
        }

    }
}
