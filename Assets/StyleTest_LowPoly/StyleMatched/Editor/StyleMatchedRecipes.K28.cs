using System;
using System.Collections.Generic;
using UnityEngine;
using Rng = System.Random;

namespace Orivilon.EditorTools.StyleTest
{
    /// <summary>
    /// Kolo 28 – šestý balík (Biomy6): čedičové pobřeží, obsidiánová pláň, alabastrové plato. Stejný jazyk jako
    /// Biomy5 (flat shading, swatche Color Pallet, kvádrové collidery pro pochozí tvary); jen dvě nové kopie materiálu
    /// kamenů (SMMats.Basalt matný tmavý, SMMats.Obsidian lesklý tmavý). Rozměry v lokálních jednotkách – výšku ve světě
    /// určuje registr (BiomeRegistryBuilder.MapK28).
    /// </summary>
    internal static partial class StyleMatchedRecipes
    {
        /// <summary>Šestiboký čedičový sloup se světlejším temenem; vrací kvádr collideru (strana 1,4·R – nevyčnívá z hranolu).</summary>
        private static void HexCol(SMMesh s, SMParts p, Rng r, float x, float z, float h, float rad, int n, float yBot = -0.3f)
        {
            s.Tube(0, new[] { V(x, yBot, z), V(x, h, z) }, new[] { rad, rad }, 6, r, (i, k) => (k + n) % 3 == 0 ? Sw3.LavaB : Sw3.LavaA, 0f,
                capStart: false, capEnd: true, capUv: (n % 4 == 0 ? Sw3.AshLight : Sw3.Ash));
            p.Boxes.Add((V(x, (h + yBot) * 0.5f, z), V(rad * 1.4f, h - yBot, rad * 1.4f), Quaternion.identity));
        }

        /// <summary>Ostrý skelný střep (3–4 hrany, špička) – obsidián.</summary>
        private static void Shard(SMMesh s, Rng r, Vector3 b, Vector3 dir, float len, float rad, int sides = 4)
        {
            dir.Normalize();
            s.Tube(0, new[] { b - dir * 0.1f, b + dir * len * 0.55f, b + dir * len }, new[] { rad, rad * 0.75f, 0.001f }, sides, r,
                (i, k) => k % 3 == 0 ? Sw3.AgateViolet : (k % 2 == 0 ? Sw3.LavaA : Sw3.LavaB), 0.12f, capStart: true, capEnd: false, capUv: Sw3.LavaA);
        }

        private static void AddK28(List<SMDef> L)
        {
            void Add6(string name, int seed, string refRel, string col, Func<Rng, SMMats, SMParts> b)
                => L.Add(new SMDef { Name = "SM_" + name, Category = "Biomy6", Seed = seed, Ref = refRel, Collider = col, Build = b });
            const float R6 = 0.42f, DX = 0.82f, DZ = 0.71f;   // šestiúhelníková mřížka jako SM_Utes_Cedic

            // ---- čedičové pobřeží (matný tmavý čedič, světlejší temena – čitelné šestiúhelníky shora)
            Add6("Cedic_Kolonada", 2801, "Stones/Stone 13", "boxes", (r, m) =>
            {
                // mohutná kolonáda: hřbet sloupů s terasovitým profilem (prstence klesají o ~0,35 – stupně pro skok)
                var s = new SMMesh(1); var p = new SMParts(); int n = 0;
                for (int q = -3; q <= 3; q++)
                for (int w = -3; w <= 3; w++)
                {
                    if (Mathf.Abs(q + w) > 3) continue;
                    float x = DX * (q + w * 0.5f), z = DZ * w;
                    int ring = Mathf.Max(Mathf.Abs(q), Mathf.Max(Mathf.Abs(w), Mathf.Abs(q + w)));
                    float h = 4.6f - 0.85f * ring - 0.35f * (((q * 7 + w * 3) % 3 + 3) % 3) / 2f + Rf(r, -0.12f, 0.12f) + (x > 0f ? 0.25f * x : 0f);
                    HexCol(s, p, r, x, z, Mathf.Max(0.35f, h), R6, n++);
                }
                p.Add("Rock", s, Vector3.zero, -1, m.Basalt); return p;
            });
            Add6("Cedic_Schody", 2802, "Stones/Stone 13", "boxes", (r, m) =>
            {
                // „obří schodiště“: tři řady sloupů stoupají po 0,30 (ve světě ~1,1 j. < výška schodu hráče) – bezpečná pasáž
                var s = new SMMesh(1); var p = new SMParts(); int n = 0;
                for (int i = 0; i < 11; i++)
                for (int w = -1; w <= 1; w++)
                {
                    float x = DX * (i + ((w & 1) != 0 ? 0.5f : 0f)) - 4.2f, z = DZ * w;
                    float h = 0.25f + 0.30f * i + (w == 0 ? 0f : -0.12f) + Rf(r, -0.04f, 0.04f);
                    HexCol(s, p, r, x, z, h, R6, n++);
                }
                // boční nižší sloupy (podesty)
                for (int i = 2; i < 10; i += 3)
                    foreach (int w in new[] { -2, 2 })
                        HexCol(s, p, r, DX * i - 4.2f, DZ * w, 0.25f + 0.30f * i - 0.6f, R6, n++);
                p.Add("Rock", s, Vector3.zero, -1, m.Basalt); return p;
            });
            Add6("Cedic_Dlazba", 2803, "Stones/Stone 9", "boxes", (r, m) =>
            {
                // nízká dlažba šestiúhelníků (pochozí plošina), temena 0,15–0,6
                var s = new SMMesh(1); var p = new SMParts(); int n = 0;
                for (int q = -3; q <= 3; q++)
                for (int w = -3; w <= 3; w++)
                {
                    if (Mathf.Abs(q + w) > 3) continue;
                    if (Mathf.Abs(q) + Mathf.Abs(w) >= 5 && r.NextDouble() < 0.5) continue;   // roztřepený okraj
                    float x = DX * (q + w * 0.5f), z = DZ * w;
                    HexCol(s, p, r, x, z, 0.15f + 0.45f * (float)r.NextDouble(), R6, n++, -0.25f);
                }
                p.Add("Rock", s, Vector3.zero, -1, m.Basalt); return p;
            });
            Add6("Cedic_Sloupy_More", 2804, "Stones/Stone 13", "boxes", (r, m) =>
            {
                // shluk sloupů vystupujících z mělčiny: pata hluboko (dno 0–7 j. ve světě), temena v různých výškách
                var s = new SMMesh(1); var p = new SMParts(); int n = 0;
                for (int q = -1; q <= 1; q++)
                for (int w = -1; w <= 1; w++)
                {
                    if (Mathf.Abs(q + w) > 1) continue;
                    float x = DX * (q + w * 0.5f), z = DZ * w;
                    float h = (q == 0 && w == 0 ? 4.6f : 3.0f + 1.2f * (float)r.NextDouble());
                    HexCol(s, p, r, x, z, h, R6, n++, -0.6f);
                }
                p.Add("Rock", s, Vector3.zero, -1, m.Basalt); return p;
            });
            Add6("Hnizdo_Ptaci", 2805, null, "none", (r, m) =>
            {
                // statické hnízdo mořských ptáků: věnec větviček (paleta) a tři vejce (kameny) – žádná AI
                var s = new SMMesh(2);
                for (int k = 0; k < 9; k++)
                {
                    float a0 = k * 40f + Rf(r, -8, 8), a1 = a0 + Rf(r, 55f, 80f);
                    Vector3 p0 = Dir(a0) * Rf(r, 0.36f, 0.44f) + Vector3.up * Rf(r, 0.04f, 0.12f), p1 = Dir(a1) * Rf(r, 0.36f, 0.44f) + Vector3.up * Rf(r, 0.04f, 0.14f);
                    s.Tube(1, new[] { p0, (p0 + p1) * 0.5f * 1.08f + Vector3.up * 0.05f, p1 }, new[] { 0.045f, 0.05f, 0.035f }, 4, r, (i, kk) => k % 3 == 0 ? Sw.Bark : Sw.Stem);
                }
                s.Rock(1, r, V(0, 0.04f, 0), V(0.34f, 0.06f, 0.34f), 0, 0.1f, Quaternion.identity, -0.02f, null, n => Sw.Stem);
                for (int k = 0; k < 3; k++)
                    s.Rock(0, r, Dir(k * 120f + 20f) * 0.12f + Vector3.up * 0.12f, V(0.075f, 0.1f, 0.075f), 1, 0.04f, Quaternion.Euler(Rf(r, -25, 25), 0, Rf(r, -25, 25)), -999f, null, n => k == 1 ? Sw4.Cream : Sw4.Snow);
                var p = new SMParts(); p.Add("Rock", s, Vector3.zero, -1, m.Stones, m.Palette); return p;
            });

            // ---- obsidiánová pláň (lesklý tmavý materiál, ostré hrany, fialový odlesk)
            Add6("Obsidian_Strep", 2811, "Stones/Stone 13", "mesh", (r, m) =>
            {
                var s = new SMMesh(1);
                s.Rock(0, r, V(0, 0.2f, 0), V(1.1f, 0.45f, 1.0f), 0, 0.3f, Quaternion.identity, -0.05f, null, n => n.y > 0.7f ? Sw3.LavaA : Sw3.AgateViolet);
                Shard(s, r, V(0, 0.2f, 0), V(0.08f, 1f, 0.05f), 3.4f, 0.45f, 4);
                for (int i = 0; i < 7; i++)
                {
                    Vector3 d = Dir(i * 51f + Rf(r, -12, 12));
                    Shard(s, r, d * Rf(r, 0.3f, 0.8f) + Vector3.up * 0.15f, Vector3.up * 1.4f + d * Rf(r, 0.5f, 1.1f), Rf(r, 1.0f, 2.3f), Rf(r, 0.16f, 0.3f), i % 2 == 0 ? 3 : 4);
                }
                var p = new SMParts(); p.Add("Rock", s, Vector3.zero, -1, m.Obsidian); return p;
            });
            Add6("Obsidian_Hreben", 2812, "Stones/Stone 13", "mesh", (r, m) =>
            {
                // hřeben skelných čepelí: řada naklopených desek podél osy x (nízký, dlouhý, ostrý)
                var s = new SMMesh(1);
                s.Rock(0, r, V(0, 0.15f, 0), V(3.2f, 0.35f, 0.8f), 0, 0.25f, Quaternion.identity, -0.05f, null, n => n.y > 0.7f ? Sw3.LavaB : Sw3.LavaA);
                for (int i = 0; i < 9; i++)
                {
                    float x = -2.8f + i * 0.7f + Rf(r, -0.12f, 0.12f), hh = 1.0f + 1.6f * Mathf.Sin((i + 0.5f) / 9f * Mathf.PI) + Rf(r, -0.2f, 0.2f);
                    Shard(s, r, V(x, 0.15f, Rf(r, -0.2f, 0.2f)), V(Rf(r, -0.25f, 0.25f), 1f, (i % 2 == 0 ? 0.3f : -0.3f)), hh, Rf(r, 0.22f, 0.32f), 3);
                }
                var p = new SMParts(); p.Add("Rock", s, Vector3.zero, -1, m.Obsidian); return p;
            });
            Add6("Obsidian_Balvan", 2813, "Stones/Stone 9", "mesh", (r, m) =>
            {
                // lasturovitě odštípnutý balvan: velké ostré fasety (bez dělení), lesk
                var s = new SMMesh(1);
                s.Rock(0, r, V(0, 0.65f, 0), V(1.3f, 0.9f, 1.05f), 0, 0.28f, Quaternion.Euler(0, 25, 0), -0.08f, null, n => n.y > 0.6f ? Sw3.LavaA : (n.x > 0f ? Sw3.AgateViolet : Sw3.LavaB));
                s.Rock(0, r, V(1.05f, 0.3f, -0.45f), V(0.5f, 0.4f, 0.45f), 0, 0.3f, Quaternion.Euler(0, 70, 0), -0.05f, null, n => Sw3.LavaB);
                var p = new SMParts(); p.Add("Rock", s, Vector3.zero, -1, m.Obsidian); return p;
            });
            Add6("Obsidian_Ulomky", 2814, "Stones/Stone 4", "mesh", (r, m) =>
            {
                var s = new SMMesh(1);
                for (int i = 0; i < 6; i++)
                {
                    Vector3 d = Dir(i * 60f + Rf(r, -20, 20));
                    Shard(s, r, d * (i == 0 ? 0f : Rf(r, 0.2f, 0.45f)), Vector3.up + d * Rf(r, 0.6f, 1.6f), i == 0 ? 0.55f : Rf(r, 0.2f, 0.4f), Rf(r, 0.06f, 0.11f), 3);
                }
                var p = new SMParts(); p.Add("Rock", s, Vector3.zero, -1, m.Obsidian); return p;
            });

            // ---- alabastrové plato (hladké, větrem obroušené tvary: dělené ikosféry s malým šumem, které se hodně překrývají –
            //      čte se jako jeden erodovaný blok, ne hromádka koulí; teplá slonovina a šedé boky, bez čisté bílé)
            Func<Vector3, Vector2> ala = n => n.y > 0.7f ? Sw4.Grey210 : (n.x + 0.3f * n.z > 0.15f ? Sw3.Ruin1 : (n.y < -0.25f ? Sw3.Lime3 : Sw4.Grey189));
            Add6("Alabastr_Oblouk", 2821, "Stones/Stone 13", "boxes", (r, m) =>
            {
                // přírodní oblouk: dva masivní pilíře (vnitřní líc x ±1,3) a souvislý prohnutý překlad (spodek ≥ 4,2) – otvor 2,6 × 4,2
                var s = new SMMesh(1);
                foreach (float x in new[] { -2.4f, 2.4f })
                    for (int i = 0; i < 6; i++)
                    {
                        float t = i / 5f, w = Mathf.Lerp(1.25f, 1.0f, t);
                        s.Rock(0, r, V(x + (x > 0 ? 0.1f : -0.1f) * Mathf.Sin(t * 3f), 0.5f + i * 0.78f, Rf(r, -0.05f, 0.05f)), V(w, 0.62f, w * 0.92f), 2, 0.06f,
                            Quaternion.Euler(0, i * 37f, 0), i == 0 ? -0.2f : -999f, null, ala);
                    }
                for (int i = 0; i < 9; i++)
                {
                    float t = (i - 4) / 4f, x = 2.3f * t, y = 5.0f + 0.45f * (1f - t * t);
                    s.Rock(0, r, V(x, y, 0), V(0.85f, 0.72f + 0.2f * Mathf.Abs(t), 1.0f), 2, 0.05f, Quaternion.Euler(0, i * 19f, 8f * t), -999f, null, ala);
                }
                var p = new SMParts(); p.Add("Rock", s, Vector3.zero, -1, m.Stones);
                p.Boxes.Add((V(-2.45f, 2.3f, 0), V(2.3f, 5.0f, 2.0f), Quaternion.identity));
                p.Boxes.Add((V(2.45f, 2.3f, 0), V(2.3f, 5.0f, 2.0f), Quaternion.identity));
                p.Boxes.Add((V(0, 5.15f, 0), V(6.9f, 1.8f, 1.8f), Quaternion.identity));
                return p;
            });
            Add6("Alabastr_Vez", 2822, "Stones/Stone 13", "mesh", (r, m) =>
            {
                // obroušená věž: souvislý zužující se sloup (překrývající se bloky), zaoblený širší vršek
                var s = new SMMesh(1);
                Vector3 lean = V(Rf(r, -0.15f, 0.15f), 0, Rf(r, -0.15f, 0.15f));
                for (int i = 0; i < 8; i++)
                {
                    float t = i / 7f, w = Mathf.Lerp(1.35f, 0.72f, Mathf.Sqrt(t));
                    s.Rock(0, r, lean * t * 4f + V(0, 0.45f + i * 0.68f, 0), V(w, 0.6f, w * 0.9f), 2, 0.05f, Quaternion.Euler(0, i * 41f, 0), i == 0 ? -0.2f : -999f, null, ala);
                }
                s.Rock(0, r, lean * 4.3f + V(0, 5.6f, 0), V(1.05f, 0.5f, 0.95f), 2, 0.05f, Quaternion.identity, -999f, null, ala);
                var p = new SMParts(); p.Add("Rock", s, Vector3.zero, -1, m.Stones); return p;
            });
            Add6("Alabastr_Plotna", 2823, "Stones/Stone 13", "mesh", (r, m) =>
            {
                // stolová deska: oblý podstavec a široká obroušená deska (pochozí)
                var s = new SMMesh(1);
                s.Rock(0, r, V(0, 0.55f, 0), V(1.9f, 0.75f, 1.6f), 2, 0.05f, Quaternion.identity, -0.2f, null, ala);
                s.Rock(0, r, V(0.1f, 1.45f, 0), V(2.6f, 0.38f, 2.2f), 2, 0.04f, Quaternion.Euler(0, 20, 0), -999f, null, ala);
                var p = new SMParts(); p.Add("Rock", s, Vector3.zero, -1, m.Stones); return p;
            });
            Add6("Alabastr_Balvan", 2824, "Stones/Stone 9", "mesh", (r, m) =>
            {
                var s = new SMMesh(1);
                s.Rock(0, r, V(0, 0.7f, 0), V(1.35f, 0.9f, 1.15f), 2, 0.06f, Quaternion.Euler(0, 15, 0), -0.08f, null, ala);
                s.Rock(0, r, V(1.15f, 0.32f, 0.5f), V(0.55f, 0.4f, 0.5f), 2, 0.06f, Quaternion.Euler(0, 50, 0), -0.05f, null, ala);
                var p = new SMParts(); p.Add("Rock", s, Vector3.zero, -1, m.Stones); return p;
            });
            Add6("Alabastr_Kamen", 2825, "Stones/Stone 4", "mesh", (r, m) =>
            {
                var s = new SMMesh(1);
                for (int i = 0; i < 3; i++)
                    s.Rock(0, r, Dir(i * 120f) * (i == 0 ? 0f : 0.32f) + Vector3.up * 0.12f, V(0.24f, 0.15f, 0.2f) * (i == 0 ? 1f : 0.7f), 1, 0.05f, Quaternion.Euler(0, i * 50f, 0), -0.02f, null, ala);
                var p = new SMParts(); p.Add("Rock", s, Vector3.zero, -1, m.Stones); return p;
            });
        }
    }
}
