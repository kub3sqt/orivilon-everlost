using System;
using System.Collections.Generic;
using UnityEngine;
using Rng = System.Random;

namespace Orivilon.EditorTools.StyleTest
{
    /// <summary>
    /// Kolo 29 – sedmý balík (Biomy7): meteorický kráter, zkamenělý korálový útes, bahenní sopky. Stejný jazyk jako Biomy6
    /// (flat shading, swatche Color Pallet, kvádrové collidery u průchozích tvarů). Žádný nový materiál – kameny, čedič (matný
    /// tmavý kov meteoritu) a obsidián (lesklé černé sklo tektitů). Rozměry v lokálních jednotkách – výšku ve světě určuje registr
    /// (BiomeRegistryBuilder.MapK29). Průchozí tvary (brána, kostra) mají otvor u počátku: x ±1,3, výška 0–4,3.
    /// </summary>
    internal static partial class StyleMatchedRecipes
    {
        private static void AddK29(List<SMDef> L)
        {
            void Add7(string name, int seed, string refRel, string col, Func<Rng, SMMats, SMParts> b)
                => L.Add(new SMDef { Name = "SM_" + name, Category = "Biomy7", Seed = seed, Ref = refRel, Collider = col, Build = b });

            // ---- meteorický kráter: tmavé železo s rezavými skvrnami, vyvržené balvany, černé sklo, rudné žíly
            Func<Vector3, Vector2> iron = n => n.y > 0.6f ? Sw3.LavaB : (n.x + n.z > 0.45f ? Sw3.RustDark : (n.y < -0.2f ? Sw3.Char : Sw3.LavaA));
            Add7("Meteorit_Jadro", 2901, "Stones/Stone 9", "mesh", (r, m) =>
            {
                // nepravidelné kovové jádro do poloviny zaryté v kráteru, regmaglypty (důlky) jako tmavé výstupky, rezavé skvrny
                var s = new SMMesh(1);
                s.Rock(0, r, V(0, 0.55f, 0), V(1.25f, 0.85f, 1.05f), 1, 0.22f, Quaternion.Euler(8, 20, 0), -0.15f, null, iron);
                s.Rock(0, r, V(0.78f, 0.35f, -0.5f), V(0.55f, 0.45f, 0.5f), 0, 0.25f, Quaternion.Euler(0, 40, 12), -0.1f, null, iron);
                s.Rock(0, r, V(-0.72f, 0.3f, 0.45f), V(0.5f, 0.38f, 0.45f), 0, 0.25f, Quaternion.Euler(0, -30, -10), -0.1f, null, iron);
                for (int i = 0; i < 5; i++)
                {
                    Vector3 d = Dir(i * 72f + Rf(r, -15, 15));
                    int ii = i;
                    s.Rock(0, r, d * 0.95f + Vector3.up * Rf(r, 0.5f, 1.0f), V(0.22f, 0.16f, 0.2f), 0, 0.2f, Quaternion.identity, -999f, null, n => ii % 2 == 0 ? Sw3.Rust : Sw3.RustOrange);
                }
                var p = new SMParts(); p.Add("Rock", s, Vector3.zero, -1, m.Basalt); return p;
            });
            Add7("Kraterovy_Balvan", 2902, "Stones/Stone 13", "mesh", (r, m) =>
            {
                // roztříštěný vyvržený balvan, ostré úlomky opřené šikmo ven (směr od dopadu), rezavé a popelavé plochy
                var s = new SMMesh(1);
                Func<Vector3, Vector2> br = n => n.y > 0.65f ? Sw3.Ash : (n.x > 0.3f ? Sw3.RustDark : Sw3.LavaB);
                s.Rock(0, r, V(0, 0.6f, 0), V(1.2f, 0.8f, 0.95f), 0, 0.3f, Quaternion.Euler(12, 30, 8), -0.1f, null, br);
                s.Rock(0, r, V(0.9f, 0.3f, 0.5f), V(0.6f, 0.42f, 0.5f), 0, 0.3f, Quaternion.Euler(0, 60, 20), -0.08f, null, br);
                s.Rock(0, r, V(-0.85f, 0.25f, -0.4f), V(0.45f, 0.3f, 0.5f), 0, 0.3f, Quaternion.Euler(-15, 10, 0), -0.08f, null, br);
                for (int i = 0; i < 3; i++)
                {
                    Vector3 d = Dir(150f + i * 35f);
                    Shard(s, r, d * 0.9f, Vector3.up * 0.6f + d, Rf(r, 0.6f, 1.0f), Rf(r, 0.15f, 0.25f), 4);
                }
                var p = new SMParts(); p.Add("Rock", s, Vector3.zero, -1, m.Stones); return p;
            });
            Add7("Tektit_Sklo", 2903, "Stones/Stone 9", "mesh", (r, m) =>
            {
                // nízká zatuhlá kaluž impaktního skla: plochý lesklý štít, jazyky taveniny do stran, kapky tektitů navrchu
                var s = new SMMesh(1);
                Func<Vector3, Vector2> gl = n => n.y > 0.7f ? Sw3.LavaA : (n.x > 0.2f ? Sw3.AgateViolet : Sw3.LavaB);
                s.Rock(0, r, V(0, 0.25f, 0), V(1.4f, 0.4f, 1.1f), 1, 0.18f, Quaternion.Euler(0, 15, 0), -0.05f, null, gl);
                for (int i = 0; i < 4; i++)
                {
                    Vector3 d = Dir(i * 90f + Rf(r, -20, 20));
                    s.Rock(0, r, d * 1.3f + Vector3.up * 0.12f, V(0.35f, 0.22f, 0.75f), 0, 0.15f, Quaternion.LookRotation(d), -0.04f, null, gl);
                }
                for (int i = 0; i < 6; i++)
                {
                    Vector3 d = Dir(i * 60f + Rf(r, -25, 25));
                    s.Rock(0, r, d * Rf(r, 0.3f, 0.9f) + Vector3.up * Rf(r, 0.48f, 0.6f), V(0.12f, 0.16f, 0.12f), 0, 0.1f, Quaternion.identity, -999f, null, n => Sw3.AgateViolet);
                }
                var p = new SMParts(); p.Add("Rock", s, Vector3.zero, -1, m.Obsidian); return p;
            });
            Add7("Ruda_Shluk", 2904, "Stones/Stone 13", "mesh", (r, m) =>
            {
                // šedá hornina s vyčnívajícími rudnými hranoly: tyrkysová měď, rezavé železo, sírová žluť
                var s = new SMMesh(1);
                s.Rock(0, r, V(0, 0.45f, 0), V(1.0f, 0.65f, 0.85f), 0, 0.3f, Quaternion.Euler(0, 25, 0), -0.08f, null, n => n.y > 0.6f ? Sw3.Ash : Sw3.LavaB);
                for (int i = 0; i < 8; i++)
                {
                    Vector3 d = Dir(i * 45f + Rf(r, -10, 10));
                    Vector3 b = d * Rf(r, 0.25f, 0.65f) + Vector3.up * Rf(r, 0.35f, 0.8f);
                    Vector2 col = i % 3 == 0 ? Sw4.Teal : (i % 3 == 1 ? Sw3.RustOrange : Sw4.Sulfur);
                    s.Tube(0, new[] { b - d * 0.15f, b + (Vector3.up * 1.2f + d * 0.8f).normalized * Rf(r, 0.5f, 0.9f) }, new[] { Rf(r, 0.1f, 0.16f), 0.02f }, 5, r,
                        (ii, k) => col, 0f, capStart: true, capEnd: false, capUv: col);
                }
                var p = new SMParts(); p.Add("Rock", s, Vector3.zero, -1, m.Stones); return p;
            });
            Add7("Tektit_Strepy", 2905, "Stones/Stone 4", "mesh", (r, m) =>
            {
                // drobné tektity: kapky, knoflíky a střípky černozeleného skla
                var s = new SMMesh(1);
                for (int i = 0; i < 6; i++)
                {
                    Vector3 d = Dir(i * 60f + Rf(r, -20, 20));
                    if (i % 2 == 0) s.Rock(0, r, d * Rf(r, 0.1f, 0.45f) + Vector3.up * 0.08f, V(0.11f, 0.09f, 0.16f), 0, 0.12f, Quaternion.Euler(0, Rf(r, 0, 180), 0), -0.01f, null, n => n.y > 0.5f ? Sw3.LavaA : Sw3.AgateViolet);
                    else Shard(s, r, d * Rf(r, 0.15f, 0.45f), Vector3.up + d * Rf(r, 0.8f, 1.6f), Rf(r, 0.18f, 0.32f), Rf(r, 0.05f, 0.09f), 3);
                }
                var p = new SMParts(); p.Add("Rock", s, Vector3.zero, -1, m.Obsidian); return p;
            });

            // ---- zkamenělý korálový útes: vybělený kalcit (krémová, slonovina, šedobéžová), vzácně vybledlý lososový odstín
            // pilot: ve hře vycházely větve šedohnědé (stín, flat shading) → světlejší swatche (Lime1/Snow) a lososové špičky
            Func<int, int, Vector2> calc = (i, k) => (k + i) % 4 == 0 ? Sw3.LimeCream : (k % 2 == 0 ? Sw3.Lime1 : Sw4.Cream);
            Func<Vector3, Vector2> lime = n => n.y > 0.55f ? Sw3.LimeCream : (n.y < -0.25f ? Sw3.Ruin4 : (n.x + n.z > 0.5f ? Sw3.Lime3 : Sw3.Ruin1));
            Add7("Koral_Vetevnaty", 2911, "Stones/Stone 13", "mesh", (r, m) =>
            {
                // větevnatý korál (parohovitý): porézní vápencový podstavec, z něj tři kmeny, každý se 2× větví, oblé konce
                var s = new SMMesh(1);
                s.Rock(0, r, V(0, 0.35f, 0), V(1.15f, 0.5f, 1.0f), 1, 0.15f, Quaternion.identity, -0.1f, null, lime);
                void Branch(Vector3 b, Vector3 dir, float len, float rad, int depth)
                {
                    Vector3 e = b + dir.normalized * len;
                    Vector3 mid = (b + e) * 0.5f + new Vector3(Rf(r, -0.1f, 0.1f), 0f, Rf(r, -0.1f, 0.1f));
                    s.Tube(0, new[] { b, mid, e }, new[] { rad, rad * 0.86f, rad * 0.72f }, 6, r, calc, 0.08f, capStart: false, capEnd: true, capUv: Sw4.Cream);
                    if (depth == 0) { s.Rock(0, r, e, V(rad * 1.1f, rad * 1.1f, rad * 1.1f), 0, 0.1f, Quaternion.identity, -999f, null, n => Sw3.AgateCoral); return; }
                    int nb = depth > 1 ? 3 : 2;
                    float y0 = Rf(r, 0f, 360f);
                    for (int i = 0; i < nb; i++)
                        Branch(e, Vector3.up * 1.3f + Dir(y0 + i * 360f / nb + Rf(r, -25f, 25f)) * Rf(r, 0.6f, 1.0f), len * Rf(r, 0.62f, 0.78f), rad * 0.7f, depth - 1);
                }
                for (int i = 0; i < 3; i++)
                    Branch(Dir(i * 120f) * 0.35f + Vector3.up * 0.5f, Vector3.up * 1.6f + Dir(i * 120f + Rf(r, -20, 20)) * 0.7f, 1.5f, 0.32f, 2);
                var p = new SMParts(); p.Add("Rock", s, Vector3.zero, -1, m.Stones); return p;
            });
            Add7("Koral_Stolovy", 2912, "Stones/Stone 13", "mesh", (r, m) =>
            {
                // stolový korál: krátký kmen a široká deska s polypovými hrbolky (pochozí temeno)
                var s = new SMMesh(1);
                s.Rock(0, r, V(0, 0.15f, 0), V(0.8f, 0.3f, 0.75f), 0, 0.15f, Quaternion.identity, -0.1f, null, lime);
                s.Tube(0, new[] { V(0, -0.1f, 0), V(0, 0.9f, 0), V(0.05f, 1.55f, 0) }, new[] { 0.45f, 0.34f, 0.55f }, 7, r, calc, 0.1f, capStart: false, capEnd: false);
                s.Rock(0, r, V(0, 1.7f, 0), V(1.9f, 0.28f, 1.7f), 1, 0.08f, Quaternion.Euler(0, 10, 0), -999f, null, n => n.y > 0.5f ? Sw4.Cream : (n.y < -0.3f ? Sw3.Lime3 : Sw3.LimeCream));
                for (int i = 0; i < 10; i++)
                {
                    Vector3 d = Dir(i * 36f + Rf(r, -10, 10)) * Rf(r, 0.3f, 1.45f);
                    s.Rock(0, r, d + Vector3.up * 1.92f, V(0.18f, 0.12f, 0.18f), 0, 0.1f, Quaternion.identity, -999f, null, n => i % 4 == 0 ? Sw3.AgateCream : Sw3.LimeCream);
                }
                var p = new SMParts(); p.Add("Rock", s, Vector3.zero, -1, m.Stones); return p;
            });
            Add7("Koral_Mozkovy", 2913, "Stones/Stone 9", "mesh", (r, m) =>
            {
                // mozkový korál: hladká kopule s meandrujícími hřbety
                var s = new SMMesh(1);
                s.Rock(0, r, V(0, 0.2f, 0), V(1.2f, 0.95f, 1.1f), 2, 0.04f, Quaternion.identity, -0.05f, null, n => n.y > 0.5f ? Sw4.Cream : Sw3.LimeCream);
                for (int j = 0; j < 9; j++)
                {
                    float lat = 14f + j * 8f, a0 = Rf(r, 0f, 360f), span = Rf(r, 100f, 220f);
                    var pts = new Vector3[7]; var rad = new float[7];
                    for (int k = 0; k < 7; k++)
                    {
                        float a = (a0 + span * k / 6f) * Mathf.Deg2Rad, la = (lat + 8f * Mathf.Sin(k * 1.7f + j)) * Mathf.Deg2Rad;
                        pts[k] = new Vector3(Mathf.Cos(la) * Mathf.Cos(a) * 1.2f, 0.2f + Mathf.Sin(la) * 0.95f, Mathf.Cos(la) * Mathf.Sin(a) * 1.1f) * 0.99f;
                        rad[k] = 0.095f;
                    }
                    int jj = j;
                    s.Tube(0, pts, rad, 4, r, (i, k) => jj % 2 == 0 ? Sw3.Lime3 : Sw3.Ruin1, 0f);
                }
                var p = new SMParts(); p.Add("Rock", s, Vector3.zero, -1, m.Stones); return p;
            });
            Add7("Koral_Brana", 2914, "Stones/Stone 13", "boxes", (r, m) =>
            {
                // porézní vápencová brána (bezpečný průchod): dva pilíře (vnitřní líc x ±1,35) a překlad (spodek ≥ 4,3), navrchu korály
                var s = new SMMesh(1);
                foreach (float x in new[] { -2.25f, 2.25f })
                    for (int i = 0; i < 6; i++)
                    {
                        float t = i / 5f, w = Mathf.Lerp(0.95f, 0.8f, t);
                        s.Rock(0, r, V(x + Rf(r, -0.06f, 0.06f), 0.45f + i * 0.72f, Rf(r, -0.06f, 0.06f)), V(w, 0.55f, w * 0.95f), 1, 0.12f, Quaternion.Euler(0, i * 47f, 0), i == 0 ? -0.2f : -999f, null, lime);
                        // „póry“: tmavší drobné prohlubně/výstupky na boku pilíře
                        if (i % 2 == 1) s.Rock(0, r, V(x + (x > 0 ? 0.55f : -0.55f), 0.45f + i * 0.72f, Rf(r, -0.4f, 0.4f)), V(0.16f, 0.16f, 0.16f), 0, 0.1f, Quaternion.identity, -999f, null, n => Sw3.Ruin4);
                    }
                for (int i = 0; i < 9; i++)
                {
                    float t = (i - 4) / 4f, x = 2.3f * t, y = 5.05f + 0.35f * (1f - t * t);
                    s.Rock(0, r, V(x, y, 0), V(0.8f, 0.6f + 0.15f * Mathf.Abs(t), 0.95f), 1, 0.1f, Quaternion.Euler(0, i * 23f, 6f * t), -999f, null, lime);
                }
                // korálové výrůstky na překladu (malé větvičky a kopulky)
                for (int i = 0; i < 5; i++)
                {
                    Vector3 b = V(-1.8f + i * 0.9f, 5.6f, Rf(r, -0.3f, 0.3f));
                    if (i % 2 == 0) s.Tube(0, new[] { b, b + V(Rf(r, -0.2f, 0.2f), 0.7f, Rf(r, -0.2f, 0.2f)) }, new[] { 0.14f, 0.1f }, 5, r, calc, 0.05f, capStart: false, capEnd: true, capUv: Sw4.Cream);
                    else s.Rock(0, r, b + V(0, 0.15f, 0), V(0.35f, 0.28f, 0.32f), 1, 0.05f, Quaternion.identity, -999f, null, n => Sw4.Cream);
                }
                var p = new SMParts(); p.Add("Rock", s, Vector3.zero, -1, m.Stones);
                p.Boxes.Add((V(-2.3f, 2.3f, 0), V(1.9f, 4.9f, 1.85f), Quaternion.identity));
                p.Boxes.Add((V(2.3f, 2.3f, 0), V(1.9f, 4.9f, 1.85f), Quaternion.identity));
                p.Boxes.Add((V(0, 5.25f, 0), V(6.4f, 1.5f, 1.8f), Quaternion.identity));
                return p;
            });
            Add7("Koral_Kostra", 2915, "Stones/Stone 13", "boxes", (r, m) =>
            {
                // zkamenělá kostra (žebroví nad průchodem podél osy x): páteř navrchu, 5 párů žeber jako oblouky, otvor u počátku
                var s = new SMMesh(1);
                Func<int, int, Vector2> bone = (i, k) => k % 3 == 0 ? Sw3.Ruin2 : Sw3.LimeCream;
                var p = new SMParts();
                for (int j = 0; j < 5; j++)
                {
                    float x = -2.4f + j * 1.2f, h = 4.9f - 0.25f * Mathf.Abs(j - 2), half = 1.75f;
                    var pts = new Vector3[9]; var rad = new float[9];
                    for (int k = 0; k < 9; k++)
                    {
                        float th = Mathf.PI * k / 8f;
                        pts[k] = V(x + 0.08f * Mathf.Sin(th * 2f), h * Mathf.Sin(th), -half * Mathf.Cos(th));
                        rad[k] = Mathf.Lerp(0.2f, 0.12f, Mathf.Sin(th));
                    }
                    pts[0].y = -0.25f; pts[8].y = -0.25f;
                    s.Tube(0, pts, rad, 5, r, bone, 0.05f, capStart: false, capEnd: false);
                    for (int k = 0; k < 8; k++)
                    {
                        Vector3 a = pts[k], b = pts[k + 1], c = (a + b) * 0.5f, d = b - a;
                        p.Boxes.Add((c, V(0.32f, d.magnitude + 0.1f, 0.32f), Quaternion.FromToRotation(Vector3.up, d.normalized)));
                    }
                }
                // páteř s obratli
                s.Tube(0, new[] { V(-3.1f, 4.6f, 0), V(0, 4.95f, 0), V(3.1f, 4.6f, 0) }, new[] { 0.2f, 0.24f, 0.18f }, 6, r, bone, 0.05f);
                for (int i = 0; i < 8; i++) s.Rock(0, r, V(-2.8f + i * 0.8f, 5.0f + 0.2f * Mathf.Sin(i / 7f * Mathf.PI), 0), V(0.26f, 0.22f, 0.32f), 0, 0.1f, Quaternion.identity, -999f, null, n => n.y > 0.4f ? Sw3.LimeCream : Sw3.Ruin2);
                p.Boxes.Add((V(0, 4.85f, 0), V(6.4f, 0.55f, 0.6f), Quaternion.identity));
                // napůl zapadlý pánevní/lebeční blok na konci (mimo průchod)
                s.Rock(0, r, V(3.6f, 0.45f, 0.2f), V(0.9f, 0.6f, 0.8f), 1, 0.12f, Quaternion.Euler(0, 20, 10), -0.1f, null, lime);
                p.Boxes.Add((V(3.6f, 0.45f, 0.2f), V(1.6f, 1.1f, 1.4f), Quaternion.Euler(0, 20, 10)));
                p.Add("Rock", s, Vector3.zero, -1, m.Stones); return p;
            });
            Add7("Koral_Ulomky", 2916, "Stones/Stone 4", "mesh", (r, m) =>
            {
                // úlomky větviček korálu a lastury mezi nimi
                var s = new SMMesh(1);
                for (int i = 0; i < 4; i++)
                {
                    Vector3 d = Dir(i * 90f + Rf(r, -25, 25)), b = d * Rf(r, 0.1f, 0.35f) + Vector3.up * 0.05f;
                    Vector3 e = b + Dir(Rf(r, 0, 360)) * Rf(r, 0.25f, 0.4f) + Vector3.up * 0.04f;
                    s.Tube(0, new[] { b, e }, new[] { 0.04f, 0.03f }, 5, r, calc, 0.05f);
                    s.Tube(0, new[] { e, e + Dir(Rf(r, 0, 360)) * 0.18f + Vector3.up * 0.08f }, new[] { 0.03f, 0.02f }, 5, r, calc, 0.05f);   // vidlička větvičky
                }
                for (int i = 0; i < 3; i++)
                    s.Rock(0, r, Dir(i * 120f + 40f) * 0.3f + Vector3.up * 0.04f, V(0.12f, 0.05f, 0.1f), 0, 0.08f, Quaternion.Euler(0, i * 60f, 0), -0.01f, null, n => i == 1 ? Sw3.AgateCream : Sw4.Cream);
                var p = new SMParts(); p.Add("Rock", s, Vector3.zero, -1, m.Stones); return p;
            });

            // ---- bahenní sopky a solfatary: šedé bahno, praskající krusty, sírově žluté krystaly
            Func<int, int, Vector2> mud = (i, k) => (k + 2 * i) % 5 == 0 ? Sw4.Taupe : (k % 2 == 0 ? Sw4.Grey154 : Sw4.TaupeLight);
            SMParts Cone(Rng r, SMMats m, bool twin)
            {
                // kužel s kráterem: obrys kuželu (rotační trubka), tmavé čerstvé bahno v kráteru, stékající jazyky a pukliny
                var s = new SMMesh(1);
                s.Tube(0, new[] { V(0, -0.25f, 0), V(0, 0.15f, 0), V(0, 0.55f, 0), V(0, 0.92f, 0) }, new[] { 1.05f, 0.92f, 0.58f, 0.34f }, 9, r, mud, 0.07f, capStart: false, capEnd: false);
                s.Tube(0, new[] { V(0, 0.92f, 0), V(0, 1.0f, 0) }, new[] { 0.34f, 0.37f }, 9, r, (i, k) => Sw4.Grey167, 0.04f, capStart: false, capEnd: false);
                s.Rock(0, r, V(0, 0.93f, 0), V(0.3f, 0.05f, 0.3f), 0, 0.05f, Quaternion.identity, -999f, null, n => Sw3.Ash);
                for (int i = 0; i < 4; i++)
                {
                    Vector3 d = Dir(i * 90f + Rf(r, -30, 30));
                    s.Rock(0, r, d * 0.7f + Vector3.up * 0.3f, V(0.15f, 0.045f, 0.36f), 0, 0.08f, Quaternion.LookRotation(d + Vector3.down * 0.85f), -999f, null, n => Sw3.Ash);
                }
                if (twin)
                {
                    // druhý menší kužel (gryfon) a sírová krusta u paty
                    s.Tube(0, new[] { V(1.35f, -0.2f, 0.45f), V(1.35f, 0.12f, 0.45f), V(1.35f, 0.42f, 0.45f), V(1.35f, 0.5f, 0.45f) }, new[] { 0.5f, 0.42f, 0.2f, 0.17f }, 7, r, mud, 0.06f, capStart: false, capEnd: true, capUv: Sw3.Ash);
                    s.Rock(0, r, V(-0.95f, 0.02f, -0.45f), V(0.45f, 0.06f, 0.35f), 0, 0.1f, Quaternion.Euler(0, 30, 0), -0.02f, null, n => Sw4.Sulfur);
                }
                var p = new SMParts(); p.Add("Rock", s, Vector3.zero, -1, m.Stones); return p;
            }
            Add7("Bahenni_Kuzel", 2921, "Stones/Stone 9", "mesh", (r, m) => Cone(r, m, false));
            Add7("Bahenni_Kuzel_Velky", 2922, "Stones/Stone 13", "mesh", (r, m) => Cone(r, m, true));
            Add7("Sirne_Krystaly", 2923, "Stones/Stone 4", "mesh", (r, m) =>
            {
                // sírové krystaly: shluk žlutých hranolů na šedé krustě, okrové lemy
                var s = new SMMesh(1);
                s.Rock(0, r, V(0, 0.08f, 0), V(0.75f, 0.14f, 0.6f), 0, 0.15f, Quaternion.identity, -0.03f, null, n => n.y > 0.5f ? (n.x > 0.15f ? Sw4.OrangeDeep : Sw4.Grey167) : Sw4.Grey154);
                for (int i = 0; i < 9; i++)
                {
                    Vector3 d = Dir(i * 40f + Rf(r, -12, 12)), b = d * Rf(r, 0.05f, 0.45f) + Vector3.up * 0.12f;
                    Vector2 col = i % 3 == 0 ? Sw4.SulfurLight : Sw4.Sulfur;
                    s.Tube(0, new[] { b, b + (Vector3.up * 1.4f + d * Rf(r, 0.3f, 0.9f)).normalized * Rf(r, 0.25f, 0.7f) }, new[] { Rf(r, 0.06f, 0.11f), 0.015f }, 4, r,
                        (ii, k) => col, 0f, capStart: true, capEnd: false, capUv: col);
                }
                var p = new SMParts(); p.Add("Rock", s, Vector3.zero, -1, m.Stones); return p;
            });
            Add7("Bahenni_Krusta", 2924, "Stones/Stone 4", "mesh", (r, m) =>
            {
                // praskající krusta: mozaika plochých desek s mezerami, okraje desek mírně zvednuté
                var s = new SMMesh(1);
                for (int q = -1; q <= 1; q++)
                for (int w = -1; w <= 1; w++)
                {
                    if (Mathf.Abs(q + w) > 1) continue;
                    Vector3 c = V(0.5f * (q + w * 0.5f), 0.04f, 0.43f * w) + V(Rf(r, -0.04f, 0.04f), 0f, Rf(r, -0.04f, 0.04f));
                    s.Rock(0, r, c, V(0.22f, 0.04f, 0.2f), 0, 0.12f, Quaternion.Euler(Rf(r, -6, 6), Rf(r, 0, 360), Rf(r, -6, 6)), -0.01f, null, n => n.y > 0.6f ? ((q + w) % 2 == 0 ? Sw4.Grey167 : Sw4.TaupeLight) : Sw4.Taupe);
                }
                var p = new SMParts(); p.Add("Rock", s, Vector3.zero, -1, m.Stones); return p;
            });
        }
    }
}
