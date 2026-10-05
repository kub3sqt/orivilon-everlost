using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using Orivilon.StyleTest;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using Rng = System.Random;

namespace Orivilon.EditorTools.StyleTest
{
    /// <summary>
    /// Kolo 13b – StyleMatched sada. Vše v Assets/StyleTest_LowPoly/StyleMatched. Interní materiály se jen
    /// KOPÍRUJÍ (AssetDatabase.CopyAsset) do vlastní složky; zdrojové materiály, textury a prefaby se nemění.
    /// </summary>
    internal static class StyleMatchedBuilder
    {
        private const string Root = "Assets/StyleTest_LowPoly/StyleMatched";
        private const string MeshDir = Root + "/Meshes", MatDir = Root + "/Materials", PrefabDir = Root + "/Prefabs";
        private const string SrcMat = "Assets/Models/Materials/";
        internal static readonly Vector3 Origin = new Vector3(200f, 0f, 0f);

        private static readonly string[] Cats = { "Stromy", "Rostliny", "Skaly", "Detaily", "Biomy", "Biomy2", "Biomy3", "Biomy4", "Biomy5", "Biomy6" };
        private static readonly string[] CatTitles = { "STROMY", "KERE, TRAVA, KAPRADI", "KAMENY", "DREVO A DETAILY", "BIOMY (KOLO 14)", "BIOMY 2 (KOLO 16)", "BIOMY 3 (KOLO 19)", "BIOMY 4 (KOLO 20)", "BIOMY 5 (KOLO 21)", "BIOMY 6 (KOLO 28)" };

        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            string parent = Path.GetDirectoryName(path).Replace('\\', '/');
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
        }

        private static Material CopyMat(string src, string dstName, Action<Material> tweak = null)
        {
            string dst = MatDir + "/" + dstName + ".mat";
            if (AssetDatabase.LoadAssetAtPath<Material>(dst) == null)
            {
                if (!AssetDatabase.CopyAsset(SrcMat + src + ".mat", dst)) { StyleMatchedTools.Out("ERR kopie materiálu " + src); return null; }
            }
            var m = AssetDatabase.LoadAssetAtPath<Material>(dst);
            if (tweak != null) { tweak(m); EditorUtility.SetDirty(m); }
            return m;
        }

        private static SMMats Mats()
        {
            EnsureFolder(MatDir);
            return new SMMats
            {
                Broad = CopyMat("Environment/Foliage/Leaves/Broadleaf_Leaves", "SM_Broadleaf_Leaves"),
                Birch = CopyMat("Environment/Foliage/Leaves/Birch_Leaves", "SM_Birch_Leaves"),
                Bush = CopyMat("Environment/Foliage/Leaves/Bush_Leaves", "SM_Bush_Leaves"),
                // varianta kopie pro vysoký keř: přechod barvy posunutý výš, aby dolní 2/3 měly stejný tón jako Bush_Tall
                Savana = CopyMat("Environment/Foliage/Leaves/Broadleaf_Leaves", "SM_Broadleaf_Leaves_Savana", m =>
                {
                    m.SetColor("_MainColor", new Color(0.17f, 0.33f, 0.04f));
                    m.SetColor("_SecondColor", new Color(0.55f, 0.62f, 0.22f));
                }),
                BushTundra = CopyMat("Environment/Foliage/Leaves/Bush_Leaves", "SM_Bush_Leaves_Tundra", m =>
                {
                    m.SetColor("_MainColor", new Color(0.33f, 0.38f, 0.10f));
                    m.SetColor("_SecondColor", new Color(0.62f, 0.40f, 0.16f));
                    m.SetFloat("_HeightLevel", 0.3f);
                }),
                BushTall = CopyMat("Environment/Foliage/Leaves/Bush_Leaves", "SM_Bush_Leaves_Vysoky", m => m.SetFloat("_HeightLevel", 1.5f)),
                GrassHigh = CopyMat("Environment/Foliage/Grass/Grass_01_High", "SM_Grass_01_High"),
                GrassMed = CopyMat("Environment/Foliage/Grass/Grass_01_Medium", "SM_Grass_01_Medium"),
                GrassLow = CopyMat("Environment/Foliage/Grass/Grass_01_Low", "SM_Grass_01_Low"),
                Grass02 = CopyMat("Environment/Foliage/Grass/Grass_02", "SM_Grass_02"),
                // jediná varianta: sušší tón trávy (kopie Grass_01_Medium, změněné jen barvy kopie)
                GrassDry = CopyMat("Environment/Foliage/Grass/Grass_01_Medium", "SM_Grass_Sucha", m =>
                {
                    m.SetColor("_MainColor", new Color(0.55f, 0.58f, 0.22f));
                    m.SetColor("_SecondColor", new Color(0.93f, 0.85f, 0.45f));
                }),
                Fern = CopyMat("Environment/Foliage/Leaves/Plant_Fern_Leaf", "SM_Plant_Fern_Leaf"),
                // kolo 16: barevné varianty kopií (zdrojové materiály se nemění)
                Sakura = CopyMat("Environment/Foliage/Leaves/Broadleaf_Leaves", "SM_Broadleaf_Leaves_Sakura", m =>
                {
                    m.SetColor("_MainColor", new Color(0.78f, 0.36f, 0.52f));
                    m.SetColor("_SecondColor", new Color(1.0f, 0.78f, 0.86f));
                }),
                Jungle = CopyMat("Environment/Foliage/Leaves/Broadleaf_Leaves", "SM_Broadleaf_Leaves_Dzungle", m =>
                {
                    m.SetColor("_MainColor", new Color(0.03f, 0.19f, 0.03f));
                    m.SetColor("_SecondColor", new Color(0.24f, 0.52f, 0.10f));
                }),
                Bamboo = CopyMat("Environment/Foliage/Leaves/Broadleaf_Leaves", "SM_Broadleaf_Leaves_Bambus", m =>
                {
                    m.SetColor("_MainColor", new Color(0.16f, 0.38f, 0.05f));
                    m.SetColor("_SecondColor", new Color(0.58f, 0.74f, 0.22f));
                }),
                Sequoia = CopyMat("Environment/Foliage/Leaves/Broadleaf_Leaves", "SM_Broadleaf_Leaves_Sekvoje", m =>
                {
                    m.SetColor("_MainColor", new Color(0.03f, 0.15f, 0.07f));
                    m.SetColor("_SecondColor", new Color(0.18f, 0.36f, 0.20f));
                }),
                Heather = CopyMat("Environment/Foliage/Leaves/Bush_Leaves", "SM_Bush_Leaves_Vres", m =>
                {
                    m.SetColor("_MainColor", new Color(0.26f, 0.12f, 0.24f));
                    m.SetColor("_SecondColor", new Color(0.66f, 0.38f, 0.66f));
                    m.SetFloat("_HeightLevel", 0.25f);
                }),
                FernTrop = CopyMat("Environment/Foliage/Leaves/Plant_Fern_Leaf", "SM_Plant_Fern_Leaf_Tropicky", m =>
                {
                    m.SetColor("_MainColor", new Color(0.05f, 0.24f, 0.04f));
                    m.SetColor("_SecondColor", new Color(0.30f, 0.56f, 0.12f));
                }),
                FirDark = CopyMat("Environment/Foliage/Leaves/Fir_Branch", "SM_Fir_Branch_Tmavy", m =>
                {
                    m.SetColor("_MainColor", new Color(0.01f, 0.13f, 0.03f));
                    m.SetColor("_SecondColor", new Color(0.12f, 0.27f, 0.12f));
                }),
                // kolo 19: listy palem – kopie listu kapradí, teplejší zelená
                Palm = CopyMat("Environment/Foliage/Leaves/Plant_Fern_Leaf", "SM_Plant_Fern_Leaf_Palma", m =>
                {
                    m.SetColor("_MainColor", new Color(0.10f, 0.30f, 0.05f));
                    m.SetColor("_SecondColor", new Color(0.50f, 0.64f, 0.18f));
                }),
                // kolo 20: led a tyrkysová krusta – kopie materiálu kamenů (stejná paletová textura) s modravým tónem
                Ice = CopyMat("Color Pallet", "SM_Color_Pallet_Led", m =>
                {
                    m.SetColor("_BaseColor", new Color(0.84f, 0.95f, 1.0f));
                    if (m.HasProperty("_Color")) m.SetColor("_Color", new Color(0.84f, 0.95f, 1.0f));
                }),
                // kolo 21: svítící krystaly a houby – kopie materiálu kamenů, emise = tatáž paletová textura × 0,85 (svítí barvou swatche)
                Glow = CopyMat("Color Pallet", "SM_Color_Pallet_Svit", m =>
                {
                    m.EnableKeyword("_EMISSION");
                    m.SetTexture("_EmissionMap", m.GetTexture("_BaseMap"));
                    m.SetColor("_EmissionColor", new Color(0.85f, 0.85f, 0.95f));
                    if (m.HasProperty("_EmissionEnabled")) m.SetFloat("_EmissionEnabled", 1f);
                    m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
                }),
                // kolo 28: čedič – matně ztmavená kopie kamenů; obsidián – tmavá lesklá kopie (stejná paletová textura)
                Basalt = CopyMat("Color Pallet", "SM_Color_Pallet_Cedic", m =>
                {
                    // doověření K28: 0,62 dávalo ve hře čistě černou siluetu bez čitelných fazet sloupů → 0,85
                    m.SetColor("_BaseColor", new Color(0.85f, 0.85f, 0.88f));
                    if (m.HasProperty("_Color")) m.SetColor("_Color", new Color(0.85f, 0.85f, 0.88f));
                    m.SetFloat("_Smoothness", 0.3f);
                }),
                Obsidian = CopyMat("Color Pallet", "SM_Color_Pallet_Obsidian", m =>
                {
                    m.SetColor("_BaseColor", new Color(0.40f, 0.39f, 0.44f));
                    if (m.HasProperty("_Color")) m.SetColor("_Color", new Color(0.40f, 0.39f, 0.44f));
                    m.SetFloat("_Smoothness", 0.9f);
                    if (m.HasProperty("_Glossiness")) m.SetFloat("_Glossiness", 0.9f);
                }),
                Palette = CopyMat("Misc/Color_Palette", "SM_Color_Palette"),
                Stones = CopyMat("Color Pallet", "SM_Color_Pallet_Kameny"),
            };
        }

        private static Mesh SaveMesh(string name, SMMesh sm, out int[] used)
        {
            string path = MeshDir + "/" + name + ".asset";
            var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            bool isNew = mesh == null;
            if (isNew) mesh = new Mesh();
            sm.Fill(mesh, name, out used);
            if (isNew) AssetDatabase.CreateAsset(mesh, path); else EditorUtility.SetDirty(mesh);
            return mesh;
        }

        internal sealed class Built { public SMDef Def; public GameObject Prefab; public Bounds Bounds; public int Tris, Cards; public string Info; }

        private static List<Built> BuildAssets(bool pilotOnly, string onlyCat = null)
        {
            EnsureFolder(MeshDir);
            foreach (var c in Cats) EnsureFolder(PrefabDir + "/" + c);
            var mats = Mats();
            var refLg = StyleMatchedTools.LoadRef("Foliage/Trees/Broadleaf/Tree_Broadleaf_01")?.GetComponent<LODGroup>();
            var refLods = refLg != null ? refLg.GetLODs() : null;
            var res = new List<Built>();
            foreach (var def in StyleMatchedRecipes.All())
            {
                if (pilotOnly && !def.Pilot) continue;
                string ppath = PrefabDir + "/" + def.Category + "/" + def.Name + ".prefab";
                if (!string.IsNullOrEmpty(onlyCat) && def.Category != onlyCat)
                {
                    // kolo 16: ostatní kategorie se nepřestavují – jen se načtou do galerie
                    var old = AssetDatabase.LoadAssetAtPath<GameObject>(ppath);
                    if (old == null) continue;
                    var oi = (GameObject)PrefabUtility.InstantiatePrefab(old); Bounds ob = StyleMatchedTools.RBounds(oi); UnityEngine.Object.DestroyImmediate(oi);
                    res.Add(new Built { Def = def, Prefab = old, Bounds = ob, Info = "beze změny" });
                    continue;
                }
                if (def.Variant != null)
                {
                    // kolo 16: varianta herního prefabu – instance, náhrada materiálů podle jména, uložit jako nový prefab
                    var src = StyleMatchedTools.LoadRef(def.Ref);
                    if (src == null) { StyleMatchedTools.Out("ERR varianta: chybí " + def.Ref); continue; }
                    var vi = (GameObject)PrefabUtility.InstantiatePrefab(src);
                    PrefabUtility.UnpackPrefabInstance(vi, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
                    vi.name = def.Name; vi.transform.position = Vector3.zero;
                    var map = def.Variant(mats); int swapped = 0, vtris = 0;
                    foreach (var rr in vi.GetComponentsInChildren<Renderer>(true))
                    {
                        var ms = rr.sharedMaterials;
                        for (int i = 0; i < ms.Length; i++)
                            if (ms[i] != null && map.TryGetValue(ms[i].name, out Material rep) && rep != null) { ms[i] = rep; swapped++; }
                        rr.sharedMaterials = ms;
                        var mf = rr.GetComponent<MeshFilter>(); if (mf != null && mf.sharedMesh != null) vtris += mf.sharedMesh.triangles.Length / 3;
                    }
                    var vp = PrefabUtility.SaveAsPrefabAsset(vi, ppath);
                    UnityEngine.Object.DestroyImmediate(vi);
                    var vin = (GameObject)PrefabUtility.InstantiatePrefab(vp); Bounds vb = StyleMatchedTools.RBounds(vin); UnityEngine.Object.DestroyImmediate(vin);
                    res.Add(new Built { Def = def, Prefab = vp, Bounds = vb, Tris = vtris, Info = "varianta " + def.Ref + ", vyměněno " + swapped + " materiálů" });
                    continue;
                }
                var parts = def.Build(new Rng(def.Seed), mats);
                var root = new GameObject(def.Name);
                var lodRenderers = new List<Renderer>[3] { new List<Renderer>(), new List<Renderer>(), new List<Renderer>() };
                int tris = 0, cards = 0; var info = new StringBuilder();
                foreach (var it in parts.Items)
                {
                    var mesh = SaveMesh(def.Name + "_" + it.name, it.mesh, out int[] used);
                    var go = new GameObject(def.Name + "_" + it.name);
                    go.transform.SetParent(root.transform, false);
                    go.transform.localPosition = it.pos;
                    go.AddComponent<MeshFilter>().sharedMesh = mesh;
                    var mr = go.AddComponent<MeshRenderer>();
                    var rm = new Material[used.Length];
                    for (int i = 0; i < used.Length; i++) rm[i] = it.mats[Mathf.Min(used[i], it.mats.Length - 1)];
                    mr.sharedMaterials = rm;
                    mr.shadowCastingMode = ShadowCastingMode.On;
                    if (it.lod < 0) { for (int l = 0; l < 3; l++) lodRenderers[l].Add(mr); }
                    else lodRenderers[it.lod].Add(mr);
                    if (it.lod <= 0) tris += it.mesh.Tris;
                    if (it.lod == 0 || it.name == "Leaves" || it.name == "Grass") cards += it.mesh.Tris / 2;
                    info.AppendFormat("{0}:{1}tris ", it.name, it.mesh.Tris);
                }
                if (parts.HasLods && refLods != null && refLods.Length >= 4)
                {
                    var lg = root.AddComponent<LODGroup>();
                    lg.SetLODs(new[]
                    {
                        new LOD(refLods[0].screenRelativeTransitionHeight, lodRenderers[0].ToArray()),
                        new LOD(refLods[1].screenRelativeTransitionHeight, lodRenderers[1].ToArray()),
                        new LOD(refLods[3].screenRelativeTransitionHeight, lodRenderers[2].ToArray()),
                    });
                    lg.RecalculateBounds();
                }
                if (def.Collider == "capsule" && def.Ref != null)
                {
                    // Kolo 14: stromy jdou do světa – kácení jako u referenčního stromu (kopie komponenty i hodnot).
                    var refP = StyleMatchedTools.LoadRef(def.Ref);
                    var hv = refP != null ? refP.GetComponentInChildren<Orivilon.World.Objects.HarvestableObject>(true) : null;
                    if (hv != null && UnityEditorInternal.ComponentUtility.CopyComponent(hv))
                        UnityEditorInternal.ComponentUtility.PasteComponentAsNew(root);
                }
                if (def.Collider == "capsule")
                {
                    var cc = root.AddComponent<CapsuleCollider>();
                    cc.radius = parts.TrunkR; cc.height = parts.TrunkH; cc.center = new Vector3(0f, parts.TrunkH * 0.5f, 0f);
                }
                else if (def.Collider == "boxes")
                {
                    // Kolo 19: ruiny a portály – složený collider z kvádrů, průchody zůstanou volné (žádný konvexní obal).
                    for (int bi = 0; bi < parts.Boxes.Count; bi++)
                    {
                        var bx = parts.Boxes[bi];
                        var cg = new GameObject("Col_" + bi);
                        cg.transform.SetParent(root.transform, false);
                        cg.transform.localPosition = bx.c; cg.transform.localRotation = bx.rot;
                        cg.AddComponent<BoxCollider>().size = bx.size;
                    }
                }
                else if (def.Collider == "mesh")
                {
                    var mf = root.GetComponentInChildren<MeshFilter>();
                    var mc = mf.gameObject.AddComponent<MeshCollider>(); mc.sharedMesh = mf.sharedMesh; mc.convex = true;
                }
                var prefab = PrefabUtility.SaveAsPrefabAsset(root, PrefabDir + "/" + def.Category + "/" + def.Name + ".prefab");
                UnityEngine.Object.DestroyImmediate(root);
                var inst = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
                Bounds b = StyleMatchedTools.RBounds(inst);
                UnityEngine.Object.DestroyImmediate(inst);
                res.Add(new Built { Def = def, Prefab = prefab, Bounds = b, Tris = tris, Cards = cards, Info = info.ToString() });
            }
            AssetDatabase.SaveAssets();
            return res;
        }

        // ------------------------------------------------------------------ galerie
        private static Transform Cube(string n, Transform p, Vector3 lp, Vector3 s, Quaternion lr, Material m)
        {
            var g = GameObject.CreatePrimitive(PrimitiveType.Cube); g.name = n;
            UnityEngine.Object.DestroyImmediate(g.GetComponent<Collider>());
            g.transform.SetParent(p, false); g.transform.localPosition = lp; g.transform.localRotation = lr; g.transform.localScale = s;
            g.GetComponent<MeshRenderer>().sharedMaterial = m; return g.transform;
        }

        private static void Sign(Transform parent, string name, string text, Vector3 pos, float width, float scale, Material post, Material board, Color textCol)
        {
            var root = new GameObject("Cedulka_" + name).transform;
            root.SetParent(parent, false); root.position = pos; root.localScale = Vector3.one * scale;
            Quaternion tilt = Quaternion.Euler(14f, 0f, 0f);
            Cube("Sloupek", root, new Vector3(0f, 0.38f, 0.03f), new Vector3(0.07f, 0.86f, 0.07f), Quaternion.identity, post);
            Cube("Deska", root, new Vector3(0f, 0.82f, 0f), new Vector3(width, 0.36f, 0.04f), tilt, board);
            var tg = new GameObject("Text"); tg.transform.SetParent(root, false);
            tg.transform.localPosition = new Vector3(0f, 0.82f, 0f) + tilt * new Vector3(0f, 0f, -0.026f);
            tg.transform.localRotation = tilt;
            var tmp = tg.AddComponent<TextMeshPro>();
            tmp.text = text; tmp.rectTransform.sizeDelta = new Vector2(width - 0.1f, 0.32f);
            tmp.enableAutoSizing = true; tmp.fontSizeMin = 0.2f; tmp.fontSizeMax = 2.6f;
            tmp.alignment = TextAlignmentOptions.Center; tmp.color = textCol;
        }

        private static Mesh Ground()
        {
            var sm = new SMMesh(1);
            const float step = 5f; const int nx = 36, nz = 34; float x0 = -90f, z0 = -80f;
            var h = new float[nx + 1, nz + 1];
            for (int i = 0; i <= nx; i++)
                for (int j = 0; j <= nz; j++)
                {
                    float x = x0 + i * step, z = z0 + j * step;
                    float dx = Mathf.Max(0f, Mathf.Abs(x) - 55f), dz = Mathf.Max(0f, Mathf.Max(z - 40f, -50f - z));
                    float t = Mathf.Clamp01(Mathf.Max(dx, dz) / 25f);
                    h[i, j] = t * (0.5f + 3f * Mathf.PerlinNoise((x + 200f) * 0.035f + 3.3f, z * 0.035f + 9.1f));
                }
            for (int i = 0; i < nx; i++)
                for (int j = 0; j < nz; j++)
                {
                    Vector3 a = new Vector3(x0 + i * step, h[i, j], z0 + j * step), b = new Vector3(x0 + (i + 1) * step, h[i + 1, j], z0 + j * step);
                    Vector3 c = new Vector3(x0 + (i + 1) * step, h[i + 1, j + 1], z0 + (j + 1) * step), d = new Vector3(x0 + i * step, h[i, j + 1], z0 + (j + 1) * step);
                    sm.FlatTri(0, a, b, c, Vector3.up, Vector2.zero); sm.FlatTri(0, a, c, d, Vector3.up, Vector2.zero);
                }
            return SaveMesh("SM_Galerie_Podklad", sm, out _);
        }

        // řady: (z, popis, kategorie nebo null = reference dané kategorie)
        private static readonly (float z, string cat, bool reference)[] Rows =
        {
            (26f, "Stromy", true), (10f, "Stromy", false), (-4f, "Rostliny", true), (-12f, "Rostliny", false),
            (-20f, "Skaly", true), (-27f, "Skaly", false), (44f, "Detaily", true), (-36f, "Detaily", false), (62f, "Biomy", true), (-48f, "Biomy", false), (78f, "Biomy2", true), (-60f, "Biomy2", false), (88f, "Biomy3", true), (-72f, "Biomy3", false), (98f, "Biomy4", true), (-86f, "Biomy4", false), (108f, "Biomy5", true), (-98f, "Biomy5", false), (118f, "Biomy6", true), (-110f, "Biomy6", false)
        };

        private static void BuildGallery(List<Built> built)
        {
            if (!StyleMatchedTools.OpenGallery()) return;
            var scene = SceneManager.GetActiveScene();
            foreach (var g in scene.GetRootGameObjects()) if (g.name == "GalerieStyleMatched") UnityEngine.Object.DestroyImmediate(g);
            var root = new GameObject("GalerieStyleMatched").transform;
            root.position = Origin;
            var groundMat = AssetDatabase.LoadAssetAtPath<Material>("Assets/StyleTest_LowPoly/Materials/LP_Galerie_Podklad.mat");
            var post = AssetDatabase.LoadAssetAtPath<Material>("Assets/StyleTest_LowPoly/Materials/LP_Galerie_Sloupek.mat");
            var board = AssetDatabase.LoadAssetAtPath<Material>("Assets/StyleTest_LowPoly/Materials/LP_Galerie_Deska.mat");
            var ground = new GameObject("Podklad"); ground.transform.SetParent(root, false);
            var gm = Ground();
            ground.AddComponent<MeshFilter>().sharedMesh = gm; ground.AddComponent<MeshRenderer>().sharedMaterial = groundMat; ground.AddComponent<MeshCollider>().sharedMesh = gm;
            var assets = new GameObject("Assety").transform; assets.SetParent(root, false);
            var refs = new GameObject("Reference").transform; refs.SetParent(root, false);
            var signs = new GameObject("Cedulky").transform; signs.SetParent(root, false);

            foreach (var row in Rows)
            {
                var items = new List<(string label, GameObject prefab, Bounds b)>();
                if (row.reference)
                {
                    var seen = new HashSet<string>();
                    if (row.cat == "Biomy")
                        foreach (var extra in StyleMatchedTools.ExtraBiomeRefs)
                        {
                            var p = StyleMatchedTools.LoadRef(extra); if (p == null || !seen.Add(extra)) continue;
                            var t = (GameObject)PrefabUtility.InstantiatePrefab(p); Bounds bb = StyleMatchedTools.RBounds(t); UnityEngine.Object.DestroyImmediate(t);
                            items.Add(("HRA: " + Path.GetFileName(extra), p, bb));
                        }
                    foreach (var b in built)
                        if (b.Def.Category == row.cat && b.Def.Ref != null && seen.Add(b.Def.Ref))
                        {
                            var p = StyleMatchedTools.LoadRef(b.Def.Ref); if (p == null) continue;
                            var t = (GameObject)PrefabUtility.InstantiatePrefab(p); Bounds bb = StyleMatchedTools.RBounds(t); UnityEngine.Object.DestroyImmediate(t);
                            items.Add(("HRA: " + Path.GetFileName(b.Def.Ref), p, bb));
                        }
                }
                else foreach (var b in built) if (b.Def.Category == row.cat) items.Add((b.Def.Name.Substring(3), b.Prefab, b.Bounds));
                if (items.Count == 0) continue;
                float gap = row.cat == "Stromy" ? 2.5f : 1.4f;
                float total = -gap; foreach (var it in items) total += it.b.size.x + gap;
                float x = -total * 0.5f, left = x;
                foreach (var it in items)
                {
                    float cx = x + it.b.size.x * 0.5f;
                    var inst = (GameObject)PrefabUtility.InstantiatePrefab(it.prefab, row.reference ? refs : assets);
                    inst.transform.position = Origin + new Vector3(cx - it.b.center.x, 0f, row.z - it.b.center.z);
                    Sign(signs, inst.name, it.label, Origin + new Vector3(cx, 0f, row.z - it.b.extents.z - 0.7f), 1.6f, row.cat == "Stromy" ? 1.2f : 0.8f, post, board,
                        row.reference ? new Color(0.35f, 0.1f, 0.05f) : new Color(0.08f, 0.22f, 0.06f));
                    x += it.b.size.x + gap;
                }
                int ci = Array.IndexOf(Cats, row.cat);
                Sign(signs, "Rada_" + row.z, (row.reference ? "REFERENCE ZE HRY – " : "STYLEMATCHED – ") + CatTitles[ci], Origin + new Vector3(left - 3.5f, 0f, row.z - 0.5f), 2.8f, 1.3f, post, board, Color.black);
            }

            // pohledy volné kamery: původních 6 + 4 nové
            var fly = UnityEngine.Object.FindFirstObjectByType<GalleryFlyCamera>();
            if (fly != null)
            {
                var so = new SerializedObject(fly);
                var pp = so.FindProperty("viewPositions"); var tp = so.FindProperty("viewTargets");
                Vector3[] np = { Origin + new Vector3(0, 20, -78), Origin + new Vector3(0, 6, -18), Origin + new Vector3(0, 4, -30), Origin + new Vector3(0, 1.4f, 15) };
                Vector3[] nt = { Origin + new Vector3(0, 2, -6), Origin + new Vector3(0, 5, 18), Origin + new Vector3(0, 0.5f, -16), Origin + new Vector3(0, 7, 24) };
                pp.arraySize = 6 + np.Length; tp.arraySize = 6 + np.Length;
                for (int i = 0; i < np.Length; i++) { pp.GetArrayElementAtIndex(6 + i).vector3Value = np[i]; tp.GetArrayElementAtIndex(6 + i).vector3Value = nt[i]; }
                so.ApplyModifiedPropertiesWithoutUndo();
            }
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
        }

        public static void Build(string arg)
        {
            var t0 = DateTime.Now;
            bool pilot = arg == "pilot";
            string onlyCat = !pilot && !string.IsNullOrEmpty(arg) ? arg : null;
            // Měření prefabů instancuje dočasné kopie do aktivní scény – jen v galerii, nikdy v herní scéně.
            if (!StyleMatchedTools.OpenGallery()) return;
            var built = BuildAssets(pilot, onlyCat);
            var sb = new StringBuilder();
            foreach (var b in built)
                sb.AppendLine(string.Format(CultureInfo.InvariantCulture, "{0}|{1}|ref {2}|{3:0.00}x{4:0.00}x{5:0.00}|minY {6:0.00}|LOD0 tris {7}|karet {8}|{9}",
                    b.Def.Name, b.Def.Category, b.Def.Ref, b.Bounds.size.x, b.Bounds.size.y, b.Bounds.size.z, b.Bounds.min.y, b.Tris, b.Cards, b.Info));
            File.WriteAllText(Path.Combine(StyleMatchedTools.Dir, onlyCat != null ? "build_checks_" + onlyCat + ".txt" : "build_checks.txt"), sb.ToString());
            BuildGallery(built);
            StyleMatchedTools.Out(string.Format(CultureInfo.InvariantCulture, "DONE build {0}: {1} assetů, {2:0.0} s", pilot ? "pilot" : "vše", built.Count, (DateTime.Now - t0).TotalSeconds));
        }

        // ------------------------------------------------------------------ snímky
        public static void Shots(string onlyCat = null)
        {
            var t0 = DateTime.Now;
            if (!StyleMatchedTools.OpenGallery()) return;
            string dir = Path.Combine(StyleMatchedTools.Dir, "shots", "raw"); Directory.CreateDirectory(dir);
            var smRoot = GameObject.Find("GalerieStyleMatched/Assety");
            if (smRoot == null) { StyleMatchedTools.Out("ERR shots: chybí GalerieStyleMatched"); return; }
            var names = new HashSet<string>(); foreach (Transform t in smRoot.transform) names.Add(t.name);
            var hidden = StyleMatchedTools.HideAllButGround();
            var cam = StyleMatchedTools.MakeCam();
            var man = new StringBuilder();
            Vector3 spot = Origin + new Vector3(0f, 0f, -5f);
            try
            {
                int idx = 0;
                foreach (var def in StyleMatchedRecipes.All())
                {
                    if (!names.Contains(def.Name)) continue;
                    if (onlyCat != null && def.Category != onlyCat) continue;
                    idx++;
                    var p = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabDir + "/" + def.Category + "/" + def.Name + ".prefab");
                    var inst = (GameObject)PrefabUtility.InstantiatePrefab(p); inst.hideFlags = HideFlags.DontSave; inst.transform.position = spot;
                    bool tree = def.Category == "Stromy" || ((def.Category == "Biomy2" || def.Category == "Biomy3" || def.Category == "Biomy4") && (def.Collider == "capsule" || def.Collider == "variant"));
                    string tag = string.Format("{0:00}_{1}", idx, def.Name);
                    StyleMatchedTools.ShootObject(cam, inst, dir, "S" + tag, tree);
                    Bounds bn = StyleMatchedTools.RBounds(inst);
                    man.AppendLine(string.Format(CultureInfo.InvariantCulture, "S|{0:00}|{1}|{2}|{3:0.0}x{4:0.0}x{5:0.0}|{6}", idx, def.Name, def.Category, bn.size.x, bn.size.y, bn.size.z, def.Ref));
                    if (def.Ref != null)
                    {
                        var rp = StyleMatchedTools.LoadRef(def.Ref);
                        var rinst = (GameObject)PrefabUtility.InstantiatePrefab(rp); rinst.hideFlags = HideFlags.DontSave; rinst.transform.position = spot;
                        Bounds br = StyleMatchedTools.RBounds(rinst);
                        float ratio = Mathf.Max(bn.extents.magnitude, br.extents.magnitude) / Mathf.Max(0.01f, Mathf.Min(bn.extents.magnitude, br.extents.magnitude));
                        bool sameScale = ratio < 2.5f;
                        float fit = sameScale ? Mathf.Max(bn.extents.magnitude, br.extents.magnitude) : -1f;
                        if (!sameScale) man.AppendLine("X|" + idx + "|" + ratio.ToString("0.0", CultureInfo.InvariantCulture));
                        rinst.SetActive(false);   // nikdy oba najednou
                        StyleMatchedTools.ShootObject(cam, inst, dir, "P" + tag + "_nove", tree, fit, bn);
                        inst.SetActive(false); rinst.SetActive(true);
                        StyleMatchedTools.ShootObject(cam, rinst, dir, "P" + tag + "_hra", tree, fit, br);
                        UnityEngine.Object.DestroyImmediate(rinst);
                    }
                    UnityEngine.Object.DestroyImmediate(inst);
                }
            }
            finally
            {
                foreach (var g in hidden) g.SetActive(true);
                UnityEngine.Object.DestroyImmediate(cam.gameObject);
            }
            // celkové pohledy na sekci StyleMatched (vše viditelné)
            var cam2 = StyleMatchedTools.MakeCam();
            cam2.fieldOfView = 50f;
            Vector3[] vp = { new Vector3(0, 18, -70), new Vector3(0, 6, -16), new Vector3(0, 7f, -48), new Vector3(-40, 9, -2), new Vector3(0, 1.4f, 15) };
            Vector3[] vt = { new Vector3(0, 2, -6), new Vector3(0, 5, 18), new Vector3(0, 0.5f, -16), new Vector3(0, 4, 14), new Vector3(0, 7, 24) };
            string[] vn = { "celek", "stromy_hra_vs_nove", "nizke_a_kameny", "stromy_bok", "stromy_proti_obloze" };
            for (int v = 0; v < vp.Length; v++)
            {
                cam2.transform.position = Origin + vp[v]; cam2.transform.LookAt(Origin + vt[v]);
                StyleMatchedTools.Render(cam2, Path.Combine(dir, "G" + (v + 1) + "_" + vn[v] + ".png"), 1600, 900);
                man.AppendLine("G|" + (v + 1) + "|" + vn[v]);
            }
            UnityEngine.Object.DestroyImmediate(cam2.gameObject);
            File.WriteAllText(Path.Combine(StyleMatchedTools.Dir, "shots", "manifest.txt"), man.ToString());
            if (SceneManager.GetActiveScene().isDirty) EditorSceneManager.SaveScene(SceneManager.GetActiveScene());
            StyleMatchedTools.Out(string.Format(CultureInfo.InvariantCulture, "DONE shots {0:0.0} s", (DateTime.Now - t0).TotalSeconds));
        }
    }
}
