using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

namespace Orivilon.EditorTools.StyleTest
{
    /// <summary>
    /// Kolo 13b – StyleMatched: analýza referenčních herních assetů a dávkové srovnávací snímky.
    /// Původní herní assety jen čte (instancuje dočasně do galerie, nic neukládá do nich).
    /// Ovládání: _reports/kolo13b/cmd.txt (analyze, refshots, build, shots, compare).
    /// </summary>
    [InitializeOnLoad]
    internal static class StyleMatchedTools
    {
        internal static readonly string Dir = Path.Combine(Path.GetDirectoryName(Application.dataPath), "_reports", "kolo13b");
        private static string CmdPath => Path.Combine(Dir, "cmd.txt");
        private static string lastText;
        private static double nextPoll;

        internal const string GalleryScene = "Assets/StyleTest_LowPoly/Scenes/AssetGallery.unity";
        internal const string RefRoot = "Assets/Models/Prefabs/Environment/";

        internal static readonly string[] RefPrefabs =
        {
            "Foliage/Trees/Broadleaf/Tree_Broadleaf_01", "Foliage/Trees/Broadleaf/Tree_Broadleaf_02", "Foliage/Trees/Broadleaf/Tree_Broadleaf_03",
            "Foliage/Trees/Broadleaf/Tree_Broadleaf_04", "Foliage/Trees/Broadleaf/Tree_Broadleaf_05",
            "Foliage/Trees/Birch/Tree_Birch_01", "Foliage/Trees/Birch/Tree_Birch_02", "Foliage/Trees/Birch/Tree_Birch_03",
            "Foliage/Trees/Birch/Tree_Birch_04", "Foliage/Trees/Birch/Tree_Birch_05",
            "Foliage/Grass/Grass_01_High", "Foliage/Grass/Grass_01_Medium", "Foliage/Grass/Grass_01_Low", "Foliage/Grass/Grass_02",
            "Foliage/Bushes/Bush_Short", "Foliage/Bushes/Bush_Tall", "Foliage/Plants/Plant_Fern_High", "Foliage/Plants/Plant_Clovers",
            "Stones/Stone 4", "Stones/Stone 9", "Stones/Stone 13", "Rocks/Large/Rock_Large_01", "Rocks/Small/Rock_Small_01",
            "Wood/Stumps/Tree_Broadleaf_Stump", "Wood/Logs/Tree_Birch_Log"
        };

        /// <summary>Kolo 14: existující herní assety nově použité v biomech – pro srovnávací řadu v galerii.</summary>
        internal static readonly string[] ExtraBiomeRefs =
        {
            "Foliage/Cactus 1", "Foliage/Cactus 3", "Foliage/Dead Bush 1", "Foliage/Dead Bush 2", "Foliage/Dead_Tree",
            "Foliage/Trees/Fir/Tree_Fir_Tall_01", "Foliage/Trees/Fir/Tree_Fir_Short", "Foliage/Plants/Plant_Reeds",
            "Foliage/Trees/Broadleaf/Tree_Broadleaf_01_Dead", "Foliage/Trees/Birch/Tree_Birch_02_Dead"
        };

        static StyleMatchedTools()
        {
            try { Directory.CreateDirectory(Dir); if (File.Exists(CmdPath)) lastText = File.ReadAllText(CmdPath); } catch (Exception) { }
            EditorApplication.update += Tick;
            Out("load 13b");
        }

        internal static void Out(string s)
        {
            try { File.AppendAllText(Path.Combine(Dir, "out.log"), DateTime.Now.ToString("HH:mm:ss ") + s + "\n"); } catch (Exception) { }
        }

        private static void Tick()
        {
            double now = EditorApplication.timeSinceStartup;
            if (now < nextPoll) return;
            nextPoll = now + 0.5;
            string text;
            try { if (!File.Exists(CmdPath)) return; text = File.ReadAllText(CmdPath); } catch (Exception) { return; }
            if (text == lastText) return;
            lastText = text;
            foreach (string raw in text.Split('\n'))
            {
                string line = raw.Trim();
                if (line.Length == 0 || line.StartsWith("#")) continue;
                Out("> " + line);
                try
                {
                    if (EditorApplication.isPlaying) { Out("ERR běží Play"); continue; }
                    if (line == "analyze") Analyze();
                    else if (line == "analyze2") Analyze2();
                    else if (line == "refshots") RefShots();
                    else if (line.StartsWith("build")) StyleMatchedBuilder.Build(line.Length > 6 ? line.Substring(6).Trim() : "");
                    else if (line.StartsWith("shots")) StyleMatchedBuilder.Shots(line.Length > 6 ? line.Substring(6).Trim() : null);
                    else if (line == "registry") BiomeRegistryBuilder.Build();
                    else if (line == "discardgame")
                    {
                        // Herní scéna zašpiněná jen dočasnými instancemi měření (vytvořené a hned smazané) – zahodit bez uložení.
                        var act = SceneManager.GetActiveScene();
                        if (act.path == "Assets/Scenes/Game.unity" && act.isDirty)
                        {
                            EditorSceneManager.OpenScene(GalleryScene, OpenSceneMode.Single);
                            Out("Game: neuložené dočasné změny zahozeny, otevřena galerie");
                        }
                    }
                    else if (line == "reopen") { if (!SceneManager.GetActiveScene().isDirty) EditorSceneManager.OpenScene("Assets/Scenes/Game.unity", OpenSceneMode.Single); }
                    else Out("ERR neznámý příkaz");
                }
                catch (Exception e) { Out("ERR " + line + ": " + e); }
            }
        }

        internal static GameObject LoadRef(string rel) => AssetDatabase.LoadAssetAtPath<GameObject>(RefRoot + rel + ".prefab");

        // ------------------------------------------------------------ analýza
        private static void Analyze()
        {
            var sb = new StringBuilder();
            foreach (var rel in RefPrefabs)
            {
                var go = LoadRef(rel);
                if (go == null) { sb.AppendLine("CHYBÍ " + rel); continue; }
                sb.AppendLine("== " + rel);
                foreach (var mr in go.GetComponentsInChildren<MeshRenderer>(true))
                {
                    var mf = mr.GetComponent<MeshFilter>();
                    var mesh = mf ? mf.sharedMesh : null;
                    if (mesh == null) continue;
                    Transform t = mr.transform;
                    sb.AppendFormat(CultureInfo.InvariantCulture, "  renderer '{0}' mesh '{1}' v {2} tris {3} bounds {4} lossyScale {5} rot {6}\n",
                        mr.name, mesh.name, mesh.vertexCount, mesh.triangles.Length / 3, mesh.bounds.size.ToString("F2"), t.lossyScale.ToString("F2"), t.rotation.eulerAngles.ToString("F0"));
                    var verts = mesh.vertices; var uvs = mesh.uv; var cols = mesh.colors;
                    sb.AppendFormat("    uv {0} colors {1} normals {2}\n", uvs.Length > 0, cols.Length > 0, mesh.normals.Length > 0);
                    var mats = mr.sharedMaterials;
                    for (int s = 0; s < mesh.subMeshCount; s++)
                    {
                        var m = s < mats.Length ? mats[s] : null;
                        int[] tri = mesh.GetTriangles(s);
                        sb.AppendFormat("    sub {0}: tris {1} mat '{2}' shader '{3}'\n", s, tri.Length / 3, m ? m.name : "-", m && m.shader ? m.shader.name : "-");
                        if (m != null && m.HasProperty("_MainColor")) sb.AppendFormat("      _MainColor {0} _SecondColor {1} _HeightLevel {2} _FadeRange {3} _AlphaCutoff {4}\n",
                            m.GetColor("_MainColor"), m.HasProperty("_SecondColor") ? m.GetColor("_SecondColor").ToString() : "-",
                            m.HasProperty("_HeightLevel") ? m.GetFloat("_HeightLevel") : -1, m.HasProperty("_FadeRange") ? m.GetFloat("_FadeRange") : -1, m.HasProperty("_AlphaCutoff") ? m.GetFloat("_AlphaCutoff") : -1);
                        // komponenty (karty / shluky) přes sdílené indexy
                        var parent = new int[verts.Length];
                        for (int i = 0; i < parent.Length; i++) parent[i] = i;
                        int Find(int x) { while (parent[x] != x) { parent[x] = parent[parent[x]]; x = parent[x]; } return x; }
                        for (int i = 0; i < tri.Length; i += 3) { int a = Find(tri[i]), b = Find(tri[i + 1]), c = Find(tri[i + 2]); parent[b] = a; parent[Find(c)] = a; }
                        var comps = new Dictionary<int, List<int>>();
                        for (int i = 0; i < tri.Length; i += 3) { int r = Find(tri[i]); if (!comps.TryGetValue(r, out var l)) comps[r] = l = new List<int>(); l.Add(i); }
                        Bounds sb2 = new Bounds(verts[tri[0]], Vector3.zero);
                        foreach (int idx in tri) sb2.Encapsulate(verts[idx]);
                        var sizes = new List<float>(); var trisPer = new List<int>(); var upDots = new List<float>(); var outDots = new List<float>(); var uvSz = new List<float>();
                        foreach (var kv in comps)
                        {
                            Bounds b = new Bounds(verts[tri[kv.Value[0]]], Vector3.zero); Vector3 n = Vector3.zero; Rect ur = Rect.zero; bool first = true;
                            foreach (int i in kv.Value)
                                for (int k = 0; k < 3; k++)
                                {
                                    int vi = tri[i + k]; b.Encapsulate(verts[vi]);
                                    if (uvs.Length > 0) { Vector2 u = uvs[vi]; if (first) { ur = new Rect(u, Vector2.zero); first = false; } else { ur.xMin = Mathf.Min(ur.xMin, u.x); ur.yMin = Mathf.Min(ur.yMin, u.y); ur.xMax = Mathf.Max(ur.xMax, u.x); ur.yMax = Mathf.Max(ur.yMax, u.y); } }
                                }
                            foreach (int i in kv.Value) n += Vector3.Cross(verts[tri[i + 1]] - verts[tri[i]], verts[tri[i + 2]] - verts[tri[i]]);
                            n.Normalize();
                            sizes.Add(b.size.magnitude); trisPer.Add(kv.Value.Count);
                            upDots.Add(Mathf.Abs(n.y));
                            Vector3 radial = (b.center - sb2.center); radial.y *= 0.5f;
                            outDots.Add(radial.sqrMagnitude > 1e-6f ? Mathf.Abs(Vector3.Dot(n, radial.normalized)) : 0f);
                            uvSz.Add(Mathf.Max(ur.width, ur.height));
                        }
                        sizes.Sort(); trisPer.Sort(); upDots.Sort(); outDots.Sort(); uvSz.Sort();
                        float Med<T>(List<T> l, Func<T, float> f) => l.Count == 0 ? 0 : f(l[l.Count / 2]);
                        sb.AppendFormat(CultureInfo.InvariantCulture,
                            "      komponenty {0} | tris/komp med {1} max {2} | velikost med {3:0.00} min {4:0.00} max {5:0.00} m | |n.y| med {6:0.00} | |n·radiála| med {7:0.00} | UV rozsah med {8:0.00} | obálka {9}\n",
                            comps.Count, Med(trisPer, x => x), trisPer.Count > 0 ? trisPer[trisPer.Count - 1] : 0, Med(sizes, x => x), sizes.Count > 0 ? sizes[0] : 0, sizes.Count > 0 ? sizes[sizes.Count - 1] : 0,
                            Med(upDots, x => x), Med(outDots, x => x), Med(uvSz, x => x), sb2.size.ToString("F2"));
                        if (uvs.Length > 0 && m != null && m.name.Contains("Palette"))
                        {
                            var set = new HashSet<string>();
                            foreach (int idx in tri) set.Add(uvs[idx].ToString("F3"));
                            sb.AppendLine("      palette UV: " + string.Join(" ", set));
                        }
                    }
                }
            }
            File.WriteAllText(Path.Combine(Dir, "analysis.txt"), sb.ToString());
            Out("DONE analyze");
        }


        private static void Analyze2()
        {
            var sb = new StringBuilder();
            foreach (var rel in RefPrefabs)
            {
                var go = LoadRef(rel); if (go == null) continue;
                foreach (var mr in go.GetComponentsInChildren<MeshRenderer>(true))
                {
                    if (mr.name.Contains("LOD") && !mr.name.Contains("LOD0")) continue;
                    var mesh = mr.GetComponent<MeshFilter>().sharedMesh; if (mesh == null) continue;
                    var v = mesh.vertices; var nr = mesh.normals; var uv = mesh.uv; var t = mesh.triangles;
                    var mat = mr.sharedMaterial;
                    sb.AppendLine("== " + rel + " / " + mr.name + " / " + (mat ? mat.name : "-"));
                    Bounds b = mesh.bounds;
                    if (mat != null && mat.shader != null && mat.shader.name.StartsWith("UNP"))
                    {
                        double dFace = 0, dRad = 0, dUp = 0; int n = 0; var hist = new int[6]; var hy = new int[6];
                        for (int i = 0; i < t.Length; i += 3)
                        {
                            Vector3 fn = Vector3.Cross(v[t[i + 1]] - v[t[i]], v[t[i + 2]] - v[t[i]]).normalized;
                            Vector3 c = (v[t[i]] + v[t[i + 1]] + v[t[i + 2]]) / 3f;
                            for (int k = 0; k < 3; k++)
                            {
                                Vector3 vn = nr[t[i + k]];
                                dFace += Mathf.Abs(Vector3.Dot(vn, fn));
                                Vector3 rd = v[t[i + k]] - b.center; dRad += Vector3.Dot(vn, rd.normalized); dUp += vn.y; n++;
                            }
                            Vector3 q = c - b.center; q = new Vector3(q.x / Mathf.Max(b.extents.x, 1e-3f), q.y / Mathf.Max(b.extents.y, 1e-3f), q.z / Mathf.Max(b.extents.z, 1e-3f));
                            hist[Mathf.Clamp((int)(q.magnitude * 5f), 0, 5)]++;
                            hy[Mathf.Clamp((int)((c.y - b.min.y) / Mathf.Max(b.size.y, 1e-3f) * 6f), 0, 5)]++;
                        }
                        sb.AppendFormat(CultureInfo.InvariantCulture, "  |vn·fn| {0:0.00} vn·radiála {1:0.00} vn.y {2:0.00} | radiální histogram (0..1.2) {3} | výška (6 pásů) {4} | bounds min {5} max {6}\n",
                            dFace / n, dRad / n, dUp / n, string.Join(",", hist), string.Join(",", hy), b.min.ToString("F2"), b.max.ToString("F2"));
                        // vzorek prvních 3 karet
                        for (int i = 0; i < Mathf.Min(t.Length, 12); i += 6)
                            sb.AppendFormat("  karta: v {0} {1} {2} uv {3} {4} {5} n {6}\n", v[t[i]].ToString("F2"), v[t[i + 1]].ToString("F2"), v[t[i + 2]].ToString("F2"), uv[t[i]].ToString("F2"), uv[t[i + 1]].ToString("F2"), uv[t[i + 2]].ToString("F2"), nr[t[i]].ToString("F2"));
                    }
                    else if (uv.Length > 0)
                    {
                        var cnt = new Dictionary<string, int>();
                        foreach (int idx in t) { string k = uv[idx].ToString("F3"); cnt.TryGetValue(k, out int c0); cnt[k] = c0 + 1; }
                        var l = new List<KeyValuePair<string, int>>(cnt); l.Sort((a, c) => c.Value.CompareTo(a.Value));
                        sb.Append("  top UV: ");
                        for (int i = 0; i < Mathf.Min(8, l.Count); i++) sb.Append(l[i].Key + "x" + l[i].Value / 3 + " ");
                        sb.AppendFormat(" (unikátních {0}) | flat: v {1} / idx {2}\n", l.Count, v.Length, t.Length);
                        var tex = mat ? mat.GetTexture("_BaseMap") : null;
                        sb.AppendLine("  textura: " + (tex ? AssetDatabase.GetAssetPath(tex) : "-") + " baseColor " + (mat && mat.HasProperty("_BaseColor") ? mat.GetColor("_BaseColor").ToString() : "-"));
                    }
                }
            }
            File.WriteAllText(Path.Combine(Dir, "analysis2.txt"), sb.ToString());
            Out("DONE analyze2");
        }

        // ------------------------------------------------------------ snímky
        internal static void Render(Camera cam, string path, int w, int h)
        {
            var rt = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB) { antiAliasing = 4 };
            var rt2 = new RenderTexture(w, h, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            rt.Create(); rt2.Create();
            cam.targetTexture = rt;
            var req = new RenderPipeline.StandardRequest { destination = rt };
            if (RenderPipeline.SupportsRenderRequest(cam, req)) RenderPipeline.SubmitRenderRequest(cam, req); else cam.Render();
            cam.targetTexture = null;
            var prev = RenderTexture.active;
            Graphics.Blit(rt, rt2);
            RenderTexture.active = rt2;
            var tex = new Texture2D(w, h, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, w, h), 0, 0); tex.Apply();
            RenderTexture.active = prev;
            File.WriteAllBytes(path, tex.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(tex);
            rt.Release(); rt2.Release(); UnityEngine.Object.DestroyImmediate(rt); UnityEngine.Object.DestroyImmediate(rt2);
        }

        internal static Camera MakeCam()
        {
            var camGo = new GameObject("StyleMatchedShotCam") { hideFlags = HideFlags.HideAndDontSave };
            var cam = camGo.AddComponent<Camera>();
            var acd = camGo.AddComponent<UniversalAdditionalCameraData>();
            acd.renderPostProcessing = true;
            acd.antialiasing = AntialiasingMode.SubpixelMorphologicalAntiAliasing;
            cam.clearFlags = CameraClearFlags.Skybox; cam.nearClipPlane = 0.03f; cam.farClipPlane = 1500f;
            return cam;
        }

        internal static Bounds RBounds(GameObject g)
        {
            var rs = g.GetComponentsInChildren<Renderer>();
            Bounds b = rs[0].bounds; foreach (var r in rs) b.Encapsulate(r.bounds); return b;
        }

        /// <summary>Jednotný náhled (stejný jako v kole 13) + u stromů pohled proti obloze.</summary>
        internal static void ShootObject(Camera cam, GameObject target, string dir, string baseName, bool sky, float fitRadius = -1f, Bounds? known = null)
        {
            Bounds b = known ?? RBounds(target);
            float rad = fitRadius > 0 ? fitRadius : b.extents.magnitude;
            cam.fieldOfView = 30f;
            float dist = Mathf.Max(rad / Mathf.Sin(15f * Mathf.Deg2Rad) * 0.92f, 1.0f);
            Vector3 d = Quaternion.Euler(16f, 30f, 0f) * Vector3.back;
            cam.transform.position = b.center + d * dist; cam.transform.LookAt(b.center);
            Render(cam, Path.Combine(dir, baseName + ".png"), 1200, 900);
            if (!sky) return;
            cam.fieldOfView = 62f;
            Vector3 flat = new Vector3(d.x, 0f, d.z).normalized;
            float ext = fitRadius > 0 ? fitRadius * 0.62f : b.extents.y;
            cam.transform.position = new Vector3(b.center.x, 0.6f, b.center.z) + flat * (ext * 1.55f);
            cam.transform.LookAt(new Vector3(b.center.x, b.min.y + b.size.y * 0.6f, b.center.z));
            Render(cam, Path.Combine(dir, baseName + "_obloha.png"), 1200, 900);
        }

        internal static bool OpenGallery()
        {
            var active = SceneManager.GetActiveScene();
            if (active.path == GalleryScene) return true;
            if (active.isDirty) { Out("ERR neuložená scéna " + active.name); return false; }
            EditorSceneManager.OpenScene(GalleryScene, OpenSceneMode.Single);
            return true;
        }

        /// <summary>Skryje vše kromě podkladu (Galerie/Podklad) a vrátí, co skrylo.</summary>
        internal static List<GameObject> HideAllButGround()
        {
            var hidden = new List<GameObject>();
            foreach (var root in SceneManager.GetActiveScene().GetRootGameObjects())
            {
                if (root.name != "Galerie" && root.name != "GalerieStyleMatched") continue;
                foreach (Transform c in root.transform)
                    if (c.name != "Podklad" && c.gameObject.activeSelf) { c.gameObject.SetActive(false); hidden.Add(c.gameObject); }
            }
            return hidden;
        }

        private static void RefShots()
        {
            if (!OpenGallery()) return;
            string dir = Path.Combine(Dir, "ref"); Directory.CreateDirectory(dir);
            var hidden = HideAllButGround();
            var cam = MakeCam();
            try
            {
                foreach (var rel in RefPrefabs)
                {
                    var p = LoadRef(rel); if (p == null) continue;
                    var inst = (GameObject)PrefabUtility.InstantiatePrefab(p);
                    inst.hideFlags = HideFlags.DontSave;
                    inst.transform.position = Vector3.zero;
                    bool tree = rel.Contains("/Trees/");
                    ShootObject(cam, inst, dir, "REF_" + Path.GetFileName(rel).Replace(' ', '_'), tree);
                    UnityEngine.Object.DestroyImmediate(inst);
                }
            }
            finally
            {
                foreach (var g in hidden) g.SetActive(true);
                UnityEngine.Object.DestroyImmediate(cam.gameObject);
            }
            if (SceneManager.GetActiveScene().isDirty) EditorSceneManager.SaveScene(SceneManager.GetActiveScene());
            Out("DONE refshots");
        }
    }
}
