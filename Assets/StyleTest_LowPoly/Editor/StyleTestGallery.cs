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
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using Rng = System.Random;

namespace Orivilon.EditorTools.StyleTest
{
    /// <summary>
    /// Kolo 13 – schvalovací low-poly sada. Generuje meshe/materiály/prefaby do izolované složky
    /// Assets/StyleTest_LowPoly a testovací scénu AssetGallery. Nic z toho není napojené na spawn/biomy.
    /// Ovládání: menu Orivilon/Style Test nebo soubor _reports/kolo13/cmd.txt (build, shots, open, reopen, playshots).
    /// </summary>
    [InitializeOnLoad]
    internal static class StyleTestGallery
    {
        private const string Root = "Assets/StyleTest_LowPoly";
        private const string MeshDir = Root + "/Meshes";
        private const string MatDir = Root + "/Materials";
        private const string PrefabDir = Root + "/Prefabs";
        private const string SceneDir = Root + "/Scenes";
        private const string ScenePath = SceneDir + "/AssetGallery.unity";
        private const string GameScenePath = "Assets/Scenes/Game.unity";
        private const string SkyPath = "Assets/Settings/PV_Sky_Day.mat";

        private static readonly string ProjectDir = Path.GetDirectoryName(Application.dataPath);
        private static readonly string Dir = Path.Combine(ProjectDir, "_reports", "kolo13");
        private static string CmdPath => Path.Combine(Dir, "cmd.txt");
        private static string lastText;
        private static double nextPoll, stepAt;
        private static readonly Queue<KeyValuePair<float, Action>> steps = new Queue<KeyValuePair<float, Action>>();

        private static readonly string[] Categories = { "Stromy", "Skaly", "Rostliny", "Detaily" };
        private static readonly string[] CategoryTitles = { "STROMY", "SKALY A KAMENY", "TRAVY, KERE, ROSTLINY", "DETAILY" };
        private static readonly float[] RowZ = { 16f, 0f, -12f, -22f };
        private static readonly float[] RowGap = { 3.5f, 2.5f, 1.6f, 1.6f };

        // Pohledy galerie (stejné pro editor snímky i Play kameru)
        private static readonly string[] ViewNames = { "celek", "stromy", "skaly", "rostliny_detaily", "bok", "stromy_proti_obloze" };
        private static readonly Vector3[] ViewPos = { new Vector3(0f, 19f, -50f), new Vector3(0f, 6f, -26f), new Vector3(0f, 7f, -24f), new Vector3(0f, 5f, -38f), new Vector3(-44f, 11f, -36f), new Vector3(0f, 1.4f, 5f) };
        private static readonly Vector3[] ViewTarget = { new Vector3(0f, 1f, -6f), new Vector3(0f, 4f, 16f), new Vector3(0f, 1f, 0f), new Vector3(0f, 0.3f, -17f), new Vector3(0f, 2f, -3f), new Vector3(0f, 7f, 16f) };

        static StyleTestGallery()
        {
            try
            {
                Directory.CreateDirectory(Dir);
                if (File.Exists(CmdPath)) lastText = File.ReadAllText(CmdPath);
            }
            catch (Exception) { }
            EditorApplication.update += Tick;
            Application.logMessageReceived += OnLog;
            Out("load (skripty kola 13 zkompilované)");
        }

        // ------------------------------------------------------------------ dálkové ovládání
        private static void Out(string s)
        {
            try { File.AppendAllText(Path.Combine(Dir, "out.log"), DateTime.Now.ToString("HH:mm:ss ") + s + "\n"); }
            catch (Exception) { }
        }

        private static void OnLog(string msg, string stack, LogType type)
        {
            if (type == LogType.Log) return;
            try
            {
                string m = msg.Length > 600 ? msg.Substring(0, 600) : msg;
                File.AppendAllText(Path.Combine(Dir, "console.log"), DateTime.Now.ToString("HH:mm:ss ") + "[" + type + "] " + m.Replace("\n", " | ") + "\n");
            }
            catch (Exception) { }
        }

        private static void Tick()
        {
            double now = EditorApplication.timeSinceStartup;
            if (steps.Count > 0)
            {
                if (now < stepAt) return;
                var s = steps.Dequeue();
                try { s.Value(); } catch (Exception e) { Out("ERR step: " + e); }
                if (steps.Count > 0) stepAt = now + steps.Peek().Key;
                return;
            }
            if (now < nextPoll) return;
            nextPoll = now + 0.5;
            string text;
            try { if (!File.Exists(CmdPath)) return; text = File.ReadAllText(CmdPath); }
            catch (Exception) { return; }
            if (text == lastText) return;
            lastText = text;
            foreach (string raw in text.Split('\n'))
            {
                string line = raw.Trim();
                if (line.Length == 0 || line.StartsWith("#")) continue;
                try { Run(line); }
                catch (Exception e) { Out("ERR " + line + ": " + e); }
            }
        }

        private static void Run(string line)
        {
            Out("> " + line);
            if (line == "build") { if (EditorApplication.isPlaying) { Out("ERR build: běží Play"); return; } BuildAll(); }
            else if (line == "shots") { if (EditorApplication.isPlaying) { Out("ERR shots: běží Play"); return; } TakeShots(); }
            else if (line == "open") { OpenGallery(); }
            else if (line == "reopen") { ReopenGame(); }
            else if (line == "playshots") { QueuePlayShots(); }
            else Out("ERR neznámý příkaz");
        }

        [MenuItem("Orivilon/Style Test/Vygenerovat sadu a galerii")]
        private static void MenuBuild() => BuildAll();

        [MenuItem("Orivilon/Style Test/Snímky galerie")]
        private static void MenuShots() => TakeShots();

        // ------------------------------------------------------------------ stavba
        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            string parent = Path.GetDirectoryName(path).Replace('\\', '/');
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
        }

        private static Material MakeMat(string name, Color c)
        {
            string path = MatDir + "/LP_" + name + ".mat";
            var sh = Shader.Find("Universal Render Pipeline/Lit");
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            bool isNew = m == null;
            if (isNew) m = new Material(sh); else m.shader = sh;
            m.SetColor("_BaseColor", c);
            m.SetFloat("_Smoothness", 0.05f);
            m.SetFloat("_Metallic", 0f);
            m.SetFloat("_SpecularHighlights", 0f);
            m.EnableKeyword("_SPECULARHIGHLIGHTS_OFF");
            m.SetFloat("_EnvironmentReflections", 0f);
            m.EnableKeyword("_ENVIRONMENTREFLECTIONS_OFF");
            m.enableInstancing = true;
            if (isNew) AssetDatabase.CreateAsset(m, path); else EditorUtility.SetDirty(m);
            return m;
        }

        private static Mesh SaveMesh(string name, Action<Mesh> fill)
        {
            string path = MeshDir + "/" + name + ".asset";
            var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            bool isNew = mesh == null;
            if (isNew) mesh = new Mesh();
            fill(mesh);
            if (isNew) AssetDatabase.CreateAsset(mesh, path); else EditorUtility.SetDirty(mesh);
            return mesh;
        }

        private sealed class Built
        {
            public AssetDef Def;
            public GameObject Prefab;
            public Mesh Mesh;
            public int Tris, Floating, Materials;
            public float MinY, MaxY;
        }

        private static void BuildAll()
        {
            if (SceneManager.GetActiveScene().isDirty) { Out("ERR build: aktivní scéna má neuložené změny – nic nedělám"); return; }
            var t0 = DateTime.Now;
            EnsureFolder(MeshDir); EnsureFolder(MatDir); EnsureFolder(SceneDir);
            foreach (var c in Categories) EnsureFolder(PrefabDir + "/" + c);

            var mats = new Material[Pal.Count];
            for (int i = 0; i < Pal.Count; i++) mats[i] = MakeMat(Pal.Names[i], Pal.Colors[i]);

            var built = new List<Built>();
            var sb = new StringBuilder();
            sb.AppendLine("# kolo 13 – kontrola sítí (min/max y v m, trojúhelníky, plovoucí díly, materiály)");
            foreach (var def in StyleTestRecipes.All())
            {
                var lp = new LowPolyMesh(Pal.Count);
                def.Build(lp, new Rng(def.Seed));
                int floating = lp.FloatingParts(out float minY, out float maxY);
                int[] used = null;
                var mesh = SaveMesh(def.Name, m => lp.Fill(m, def.Name, out used));

                var go = new GameObject(def.Name);
                go.AddComponent<MeshFilter>().sharedMesh = mesh;
                var mr = go.AddComponent<MeshRenderer>();
                var rm = new Material[used.Length];
                for (int i = 0; i < used.Length; i++) rm[i] = mats[used[i]];
                mr.sharedMaterials = rm;
                mr.shadowCastingMode = ShadowCastingMode.On;
                mr.receiveShadows = true;
                AddCollider(go, def.Collider, mesh);
                string prefabPath = PrefabDir + "/" + def.Category + "/" + def.Name + ".prefab";
                var prefab = PrefabUtility.SaveAsPrefabAsset(go, prefabPath);
                UnityEngine.Object.DestroyImmediate(go);

                var b = new Built { Def = def, Prefab = prefab, Mesh = mesh, Tris = lp.TriangleCount, Floating = floating, MinY = minY, MaxY = maxY, Materials = used.Length };
                built.Add(b);
                Vector3 sz = mesh.bounds.size;
                sb.AppendLine(string.Format(CultureInfo.InvariantCulture,
                    "{0}|{1}|{2:0.00}x{3:0.00}x{4:0.00}|minY {5:0.000}|maxY {6:0.00}|tris {7}|parts {8}|floating {9}|mats {10}|nullmat {11}",
                    def.Name, def.Category, sz.x, sz.y, sz.z, minY, maxY, b.Tris, lp.Parts.Count, floating, used.Length, Array.FindAll(rm, x => x == null).Length));
            }
            AssetDatabase.SaveAssets();
            File.WriteAllText(Path.Combine(Dir, "build_checks.txt"), sb.ToString());
            BuildScene(built);
            AssetDatabase.SaveAssets();
            Out(string.Format(CultureInfo.InvariantCulture, "DONE build: {0} assetů, {1:0.0} s", built.Count, (DateTime.Now - t0).TotalSeconds));
        }

        private static void AddCollider(GameObject go, string kind, Mesh mesh)
        {
            if (string.IsNullOrEmpty(kind) || kind == "none") return;
            if (kind == "mesh")
            {
                var mc = go.AddComponent<MeshCollider>();
                mc.sharedMesh = mesh;
                mc.convex = true;
                return;
            }
            if (kind.StartsWith("capsule:"))
            {
                var p = kind.Split(':');
                float r = float.Parse(p[1], CultureInfo.InvariantCulture), h = float.Parse(p[2], CultureInfo.InvariantCulture);
                var cc = go.AddComponent<CapsuleCollider>();
                cc.radius = r;
                cc.height = h;
                cc.center = new Vector3(0f, h * 0.5f, 0f);
            }
        }

        // ------------------------------------------------------------------ scéna
        private static Mesh BuildGroundMesh()
        {
            return SaveMesh("LP_Galerie_Podklad", mesh =>
            {
                const float step = 5f;
                const int nx = 48, nz = 44;
                const float x0 = -120f, z0 = -125f;
                var h = new float[nx + 1, nz + 1];
                for (int i = 0; i <= nx; i++)
                    for (int j = 0; j <= nz; j++)
                    {
                        float x = x0 + i * step, z = z0 + j * step;
                        float dx = Mathf.Max(0f, Mathf.Abs(x) - 70f), dz = Mathf.Max(0f, Mathf.Max(z - 35f, -60f - z));
                        float t = Mathf.Clamp01(Mathf.Max(dx, dz) / 30f);
                        h[i, j] = t * (0.5f + 3.5f * Mathf.PerlinNoise(x * 0.035f + 11.3f, z * 0.035f + 7.1f));
                    }
                var lp = new LowPolyMesh(1);
                for (int i = 0; i < nx; i++)
                    for (int j = 0; j < nz; j++)
                    {
                        Vector3 a = new Vector3(x0 + i * step, h[i, j], z0 + j * step);
                        Vector3 b = new Vector3(x0 + (i + 1) * step, h[i + 1, j], z0 + j * step);
                        Vector3 c = new Vector3(x0 + (i + 1) * step, h[i + 1, j + 1], z0 + (j + 1) * step);
                        Vector3 d = new Vector3(x0 + i * step, h[i, j + 1], z0 + (j + 1) * step);
                        if (((i + j) & 1) == 0) { lp.Tri(0, a, b, c, Vector3.up); lp.Tri(0, a, c, d, Vector3.up); }
                        else { lp.Tri(0, a, b, d, Vector3.up); lp.Tri(0, b, c, d, Vector3.up); }
                    }
                lp.Fill(mesh, "LP_Galerie_Podklad", out _);
            });
        }

        private static GameObject Cube(string name, Transform parent, Vector3 lpos, Vector3 scale, Quaternion lrot, Material m)
        {
            var g = GameObject.CreatePrimitive(PrimitiveType.Cube);
            g.name = name;
            UnityEngine.Object.DestroyImmediate(g.GetComponent<Collider>());
            g.transform.SetParent(parent, false);
            g.transform.localPosition = lpos;
            g.transform.localRotation = lrot;
            g.transform.localScale = scale;
            g.GetComponent<MeshRenderer>().sharedMaterial = m;
            return g;
        }

        private static void MakeSign(Transform parent, string name, string text, Vector3 pos, float width, float scale, Material post, Material board)
        {
            var root = new GameObject("Cedulka_" + name);
            root.transform.SetParent(parent, false);
            root.transform.position = pos;
            root.transform.localScale = Vector3.one * scale;
            Quaternion tilt = Quaternion.Euler(14f, 0f, 0f);
            Cube("Sloupek", root.transform, new Vector3(0f, 0.38f, 0.03f), new Vector3(0.07f, 0.86f, 0.07f), Quaternion.identity, post);
            Cube("Deska", root.transform, new Vector3(0f, 0.82f, 0f), new Vector3(width, 0.36f, 0.04f), tilt, board);
            var tg = new GameObject("Text");
            tg.transform.SetParent(root.transform, false);
            tg.transform.localPosition = new Vector3(0f, 0.82f, 0f) + tilt * new Vector3(0f, 0f, -0.026f);
            tg.transform.localRotation = tilt;
            var tmp = tg.AddComponent<TextMeshPro>();
            tmp.text = text;
            tmp.rectTransform.sizeDelta = new Vector2(width - 0.1f, 0.32f);
            tmp.enableAutoSizing = true;
            tmp.fontSizeMin = 0.2f;
            tmp.fontSizeMax = 2.6f;
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.color = new Color(0.16f, 0.11f, 0.07f);
        }

        private static void BuildScene(List<Built> built)
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            var sky = AssetDatabase.LoadAssetAtPath<Material>(SkyPath);
            if (sky != null) RenderSettings.skybox = sky;
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.56f, 0.61f, 0.70f);
            RenderSettings.ambientEquatorColor = new Color(0.44f, 0.46f, 0.48f);
            RenderSettings.ambientGroundColor = new Color(0.30f, 0.29f, 0.27f);
            RenderSettings.ambientIntensity = 1f;
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Exponential;
            RenderSettings.fogDensity = 0.0025f;
            RenderSettings.fogColor = new Color(0.74f, 0.81f, 0.89f);

            var sunGo = new GameObject("Sun");
            var sun = sunGo.AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.intensity = 1.5f;
            sun.color = new Color(1f, 0.96f, 0.9f);
            sun.shadows = LightShadows.Soft;
            sunGo.transform.rotation = Quaternion.Euler(45f, 60f, 0f);
            RenderSettings.sun = sun;

            var galerie = new GameObject("Galerie").transform;
            var groundMat = MakeMat("Galerie_Podklad", new Color(0.60f, 0.63f, 0.55f));
            var postMat = MakeMat("Galerie_Sloupek", new Color(0.42f, 0.31f, 0.22f));
            var boardMat = MakeMat("Galerie_Deska", new Color(0.93f, 0.89f, 0.78f));
            var refMat = MakeMat("Galerie_Meritko", new Color(0.55f, 0.57f, 0.62f));

            var ground = new GameObject("Podklad");
            ground.transform.SetParent(galerie, false);
            var gm = BuildGroundMesh();
            ground.AddComponent<MeshFilter>().sharedMesh = gm;
            ground.AddComponent<MeshRenderer>().sharedMaterial = groundMat;
            ground.AddComponent<MeshCollider>().sharedMesh = gm;

            var assetsRoot = new GameObject("Assety").transform; assetsRoot.SetParent(galerie, false);
            var signs = new GameObject("Cedulky").transform; signs.SetParent(galerie, false);
            var refs = new GameObject("Meritko").transform; refs.SetParent(galerie, false);

            for (int c = 0; c < Categories.Length; c++)
            {
                var row = built.FindAll(b => b.Def.Category == Categories[c]);
                float total = 0f;
                foreach (var b in row) total += b.Mesh.bounds.size.x;
                total += RowGap[c] * (row.Count - 1);
                float x = -total * 0.5f;
                float left = x;
                foreach (var b in row)
                {
                    Bounds bb = b.Mesh.bounds;
                    float cx = x + bb.size.x * 0.5f;
                    var inst = (GameObject)PrefabUtility.InstantiatePrefab(b.Prefab, assetsRoot);
                    inst.transform.position = new Vector3(cx - bb.center.x, 0f, RowZ[c] - bb.center.z);
                    float sc = c == 0 ? 1.3f : c == 1 ? 1.0f : 0.8f;
                    string label = b.Def.Name.Substring(3) + "\n" + string.Format(CultureInfo.InvariantCulture, "v {0:0.0} m", Mathf.Max(0f, b.MaxY));
                    MakeSign(signs, b.Def.Name, label, new Vector3(cx, 0f, RowZ[c] - bb.extents.z - 0.9f), 1.5f, sc, postMat, boardMat);
                    x += bb.size.x + RowGap[c];
                }
                MakeSign(signs, "Kategorie_" + Categories[c], CategoryTitles[c] + " (" + row.Count + ")", new Vector3(left - 3.2f, 0f, RowZ[c] - 0.5f), 2.4f, 1.6f, postMat, boardMat);
                if (c < 2)
                {
                    var cap = GameObject.CreatePrimitive(PrimitiveType.Capsule);
                    cap.name = "Meritko_Postava_1_8m_" + Categories[c];
                    UnityEngine.Object.DestroyImmediate(cap.GetComponent<Collider>());
                    cap.transform.SetParent(refs, false);
                    cap.transform.position = new Vector3(left - 1.4f, 0.9f, RowZ[c] + 0.6f);
                    cap.transform.localScale = new Vector3(0.5f, 0.9f, 0.5f);
                    cap.GetComponent<MeshRenderer>().sharedMaterial = refMat;
                }
            }

            var camGo = new GameObject("Main Camera");
            camGo.tag = "MainCamera";
            var cam = camGo.AddComponent<Camera>();
            cam.fieldOfView = 50f;
            cam.nearClipPlane = 0.05f;
            cam.farClipPlane = 1500f;
            var acd = camGo.AddComponent<UniversalAdditionalCameraData>();
            acd.renderPostProcessing = true;
            acd.antialiasing = AntialiasingMode.SubpixelMorphologicalAntiAliasing;
            camGo.AddComponent<AudioListener>();
            var fly = camGo.AddComponent<GalleryFlyCamera>();
            fly.SetViews(ViewPos, ViewTarget);
            fly.SetPose(ViewPos[0], ViewTarget[0]);

            EditorSceneManager.SaveScene(scene, ScenePath);
            Out("scéna uložena: " + ScenePath);
        }

        // ------------------------------------------------------------------ snímky
        private static void OpenGallery()
        {
            if (EditorApplication.isPlaying) { Out("ERR open: běží Play"); return; }
            var active = SceneManager.GetActiveScene();
            if (active.path == ScenePath) { Out("galerie už je otevřená"); return; }
            if (active.isDirty) { Out("ERR open: neuložená scéna " + active.name); return; }
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            Out("open " + ScenePath);
        }

        private static void ReopenGame()
        {
            if (EditorApplication.isPlaying) { Out("ERR reopen: běží Play"); return; }
            var active = SceneManager.GetActiveScene();
            if (active.isDirty) { Out("ERR reopen: neuložená scéna " + active.name); return; }
            EditorSceneManager.OpenScene(GameScenePath, OpenSceneMode.Single);
            Out("reopen " + GameScenePath);
        }

        private static void Render(Camera cam, string path, int w, int h)
        {
            var rt = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB) { antiAliasing = 4 };
            var rt2 = new RenderTexture(w, h, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            rt.Create(); rt2.Create();
            cam.targetTexture = rt;
            var req = new RenderPipeline.StandardRequest { destination = rt };
            if (RenderPipeline.SupportsRenderRequest(cam, req)) RenderPipeline.SubmitRenderRequest(cam, req);
            else cam.Render();
            cam.targetTexture = null;
            var prev = RenderTexture.active;
            Graphics.Blit(rt, rt2);
            RenderTexture.active = rt2;
            var tex = new Texture2D(w, h, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, w, h), 0, 0);
            tex.Apply();
            RenderTexture.active = prev;
            File.WriteAllBytes(path, tex.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(tex);
            rt.Release(); rt2.Release();
            UnityEngine.Object.DestroyImmediate(rt);
            UnityEngine.Object.DestroyImmediate(rt2);
        }

        private static Bounds RendererBounds(GameObject g)
        {
            var rs = g.GetComponentsInChildren<Renderer>();
            Bounds b = rs[0].bounds;
            foreach (var r in rs) b.Encapsulate(r.bounds);
            return b;
        }

        private static void TakeShots()
        {
            var t0 = DateTime.Now;
            OpenGallery();
            if (SceneManager.GetActiveScene().path != ScenePath) { Out("ERR shots: galerie není otevřená"); return; }
            var assetsRoot = GameObject.Find("Galerie/Assety");
            var signs = GameObject.Find("Galerie/Cedulky");
            var refs = GameObject.Find("Galerie/Meritko");
            if (assetsRoot == null) { Out("ERR shots: Galerie/Assety chybí"); return; }

            string outDir = Path.Combine(Dir, "shots", "raw");
            Directory.CreateDirectory(outDir);

            var camGo = new GameObject("StyleTestShotCam") { hideFlags = HideFlags.HideAndDontSave };
            var cam = camGo.AddComponent<Camera>();
            var acd = camGo.AddComponent<UniversalAdditionalCameraData>();
            acd.renderPostProcessing = true;
            acd.antialiasing = AntialiasingMode.SubpixelMorphologicalAntiAliasing;
            cam.clearFlags = CameraClearFlags.Skybox;
            cam.nearClipPlane = 0.03f;
            cam.farClipPlane = 1500f;

            var manifest = new StringBuilder();
            var defs = StyleTestRecipes.All();
            var children = new List<GameObject>();
            foreach (Transform t in assetsRoot.transform) children.Add(t.gameObject);

            try
            {
                if (signs) signs.SetActive(false);
                if (refs) refs.SetActive(false);
                int idx = 0;
                foreach (var def in defs)
                {
                    idx++;
                    var target = children.Find(g => g.name == def.Name);
                    if (target == null) { Out("ERR shots: v galerii chybí " + def.Name); continue; }
                    foreach (var g in children) g.SetActive(g == target);
                    Bounds b = RendererBounds(target);
                    float rad = b.extents.magnitude;
                    cam.fieldOfView = 30f;
                    float dist = Mathf.Max(rad / Mathf.Sin(15f * Mathf.Deg2Rad) * 0.92f, 1.0f);
                    Vector3 dir = Quaternion.Euler(16f, 30f, 0f) * Vector3.back;
                    cam.transform.position = b.center + dir * dist;
                    cam.transform.LookAt(b.center);
                    string file = string.Format("{0:00}_{1}.png", idx, def.Name);
                    Render(cam, Path.Combine(outDir, file), 1200, 900);
                    manifest.AppendLine(string.Format(CultureInfo.InvariantCulture, "{0:00}|{1}|{2}|{3:0.00}x{4:0.00}x{5:0.00}|{6}", idx, def.Name, def.Category, b.size.x, b.size.y, b.size.z, file));

                    if (def.Category == "Stromy")
                    {
                        cam.fieldOfView = 62f;
                        Vector3 flat = new Vector3(dir.x, 0f, dir.z).normalized;
                        cam.transform.position = new Vector3(b.center.x, 0.6f, b.center.z) + flat * (b.extents.y * 1.55f);
                        cam.transform.LookAt(new Vector3(b.center.x, b.min.y + b.size.y * 0.6f, b.center.z));
                        string file2 = string.Format("{0:00}b_{1}_proti_obloze.png", idx, def.Name);
                        Render(cam, Path.Combine(outDir, file2), 1200, 900);
                        manifest.AppendLine(string.Format("{0:00}b|{1}|{2}|proti obloze|{3}", idx, def.Name, def.Category, file2));
                    }
                }
            }
            finally
            {
                foreach (var g in children) g.SetActive(true);
                if (signs) signs.SetActive(true);
                if (refs) refs.SetActive(true);
            }

            cam.fieldOfView = 50f;
            for (int v = 0; v < ViewPos.Length; v++)
            {
                cam.transform.position = ViewPos[v];
                cam.transform.LookAt(ViewTarget[v]);
                string file = string.Format("G{0}_{1}.png", v + 1, ViewNames[v]);
                Render(cam, Path.Combine(outDir, file), 1600, 900);
                manifest.AppendLine(string.Format("G{0}|{1}|galerie|{2}", v + 1, ViewNames[v], file));
            }
            UnityEngine.Object.DestroyImmediate(camGo);
            File.WriteAllText(Path.Combine(Dir, "shots", "manifest.txt"), manifest.ToString());
            var scene = SceneManager.GetActiveScene();
            if (scene.isDirty) EditorSceneManager.SaveScene(scene);
            Out(string.Format(CultureInfo.InvariantCulture, "DONE shots {0:0.0} s", (DateTime.Now - t0).TotalSeconds));
        }

        private static void QueuePlayShots()
        {
            if (!EditorApplication.isPlaying) { Out("ERR playshots: neběží Play"); return; }
            string outDir = Path.Combine(Dir, "shots", "raw");
            Directory.CreateDirectory(outDir);
            steps.Clear();
            for (int v = 0; v < ViewPos.Length; v++)
            {
                int vi = v;
                steps.Enqueue(new KeyValuePair<float, Action>(0.2f, () =>
                {
                    var fly = UnityEngine.Object.FindFirstObjectByType<GalleryFlyCamera>();
                    if (fly == null) { Out("ERR playshots: GalleryFlyCamera nenalezena (scéna " + SceneManager.GetActiveScene().name + ")"); return; }
                    fly.SetView(vi);
                }));
                steps.Enqueue(new KeyValuePair<float, Action>(1.5f, () =>
                {
                    string p = Path.Combine(outDir, string.Format("P{0}_play_{1}.png", vi + 1, ViewNames[vi]));
                    ScreenCapture.CaptureScreenshot(p);
                    Out("play shot " + p);
                }));
            }
            steps.Enqueue(new KeyValuePair<float, Action>(1.5f, () => Out("DONE playshots")));
            stepAt = EditorApplication.timeSinceStartup + 0.2;
        }
    }
}
