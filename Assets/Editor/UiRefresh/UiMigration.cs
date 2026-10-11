using System.Collections.Generic;
using System.Text;
using Orivilon.Core;
using Orivilon.UI.Arcanum;
using Orivilon.UI.Menu;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using static Orivilon.UI.Refresh.EditorTools.UiAuthoringKit;

namespace Orivilon.UI.Refresh.EditorTools
{
    /// <summary>
    /// One-time conversion of the runtime-built UI refresh into saved scenes and prefabs.
    /// Steps (bridge command "uimigrate &lt;step&gt;", or the menu Tools/Everlost/UI Refresh):
    ///   assets   – frame style, sprites, materials, building-block prefabs, Arcanum + F3 + blur prefabs
    ///   menu     – MainMenu scene (must be the open scene)
    ///   loading  – LoadingScreen scene (open)
    ///   game     – Game scene (open): HUD, inventory, pause, blur, Arcanum and F3 instances
    ///   verify   – checks the open scene's references
    /// Each scene step saves the scene. The Game view must be 1920×1080.
    /// </summary>
    public static class UiMigration
    {
        public static string Run(string step)
        {
            try
            {
                switch (step)
                {
                    // The conversion is one-time: once a scene/prefab exists it belongs to the user and is
                    // never rebuilt automatically (a stray re-run must not overwrite manual edits).
                    case "assets":
                        if (AssetDatabase.LoadAssetAtPath<GameObject>(ArcanumAuthoring.PrefabPath) != null) return "ERR uimigrate assets: already converted (use 'assets!' to rebuild and overwrite the generated prefabs)";
                        return Assets();
                    case "assets!": return Assets();
                    case "menu":
                        if (Object.FindFirstObjectByType<MainMenuController>(FindObjectsInactive.Include) != null) return "ERR uimigrate menu: already converted";
                        return Menu();
                    case "loading":
                        if (Object.FindFirstObjectByType<LoadingController>(FindObjectsInactive.Include) != null) return "ERR uimigrate loading: already converted";
                        return Loading();
                    case "game":
                        if (Object.FindFirstObjectByType<HudController>(FindObjectsInactive.Include) != null) return "ERR uimigrate game: already converted";
                        return Game();
                    case "menufix": return MenuFix();
                    case "verify": return Verify();
                    default: return "ERR uimigrate: unknown step " + step;
                }
            }
            catch (System.Exception e)
            {
                Debug.LogException(e);
                return "ERR uimigrate " + step + ": " + e.Message;
            }
        }

        [MenuItem("Tools/Everlost/UI Refresh/Verify open scene")]
        private static void VerifyMenu() => Debug.Log(Verify());

        private static string Assets()
        {
            var style = FrameAuthoring.EnsureStyle();
            FrameAuthoring.Regenerate(style);
            FrameAuthoring.EnsureBlockPrefabs(style);
            ScreenAuthoring.MakeSprites();
            ArcanumAuthoring.AuthorPrefab();
            ArcanumAuthoring.AuthorF3();
            BlurPrefab();
            AssetDatabase.SaveAssets();
            return "uimigrate assets ok";
        }

        public const string BlurPrefabPath = PrefabDir + "/WorldBlurBackdrop.prefab";

        private static void BlurPrefab()
        {
            var root = new GameObject("WorldBlurBackdrop");
            var b = root.AddComponent<WorldBlurBackdrop>();
            var cgo = new GameObject("BlurCanvas", typeof(RectTransform), typeof(Canvas));
            cgo.layer = 5;
            cgo.transform.SetParent(root.transform, false);
            var canvas = cgo.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay; canvas.sortingOrder = -50; canvas.enabled = false;
            RawImage Raw(string n) { var r = NewRect(n, cgo.transform); Stretch(r); var ri = r.gameObject.AddComponent<RawImage>(); ri.raycastTarget = false; return ri; }
            b.canvas = canvas; b.sharp = Raw("Sharp"); b.blur = Raw("Blur");
            b.blur.color = new Color(1f, 1f, 1f, 0f);
            b.dim = NewImage("Dim", cgo.transform, null, new Color32(3, 5, 3, 0), false);
            Stretch(b.dim.rectTransform);
            var bgo = new GameObject("BlurBlackCamera");
            bgo.transform.SetParent(root.transform, false);
            var cam = bgo.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = Color.black; cam.cullingMask = 0; cam.depth = -100; cam.enabled = false;
            b.blackCamera = cam;
            PrefabUtility.SaveAsPrefabAsset(root, BlurPrefabPath);
            Object.DestroyImmediate(root);
        }

        private static Scene Open(string name)
        {
            var s = SceneManager.GetActiveScene();
            if (s.name != name) throw new System.Exception("open the " + name + " scene first (current: " + s.name + ")");
            return s;
        }

        private static string Save(Scene s)
        {
            EditorSceneManager.MarkSceneDirty(s);
            if (!EditorSceneManager.SaveScene(s)) throw new System.Exception("saving " + s.name + " failed");
            return "uimigrate " + s.name + " saved";
        }

        private static string Menu()
        {
            var s = Open("MainMenu");
            var style = FrameAuthoring.EnsureStyle();
            var sp = ScreenAuthoring.MakeSprites();
            var mainBg = Object.FindFirstObjectByType<MainMenuUI>(FindObjectsInactive.Include);
            ScreenAuthoring.AuthorMenu(style, sp);
            // blurred menu background for the loading screen
            var canvas = mainBg.GetComponentInParent<Canvas>(true).rootCanvas.transform;
            ScreenAuthoring.SaveBlurredMenu(canvas.Find("MainBackground")?.GetComponent<RawImage>());
            // world-list row: prefab asset + the scene's template instance
            var ui = mainBg;
            var template = ui.listElementPrefab;
            var src = PrefabUtility.GetCorrespondingObjectFromSource(template);
            if (src != null)
            {
                string path = AssetDatabase.GetAssetPath(src);
                var contents = PrefabUtility.LoadPrefabContents(path);
                ScreenAuthoring.StyleRow(contents, sp);
                PrefabUtility.SaveAsPrefabAsset(contents, path);
                PrefabUtility.UnloadPrefabContents(contents);
            }
            ScreenAuthoring.StyleRow(template, sp);
            return Save(s);
        }

        /// <summary>Repairs the "Selected world" name font (an accidental partial re-run set it to Space Mono).</summary>
        private static string MenuFix()
        {
            var s = Open("MainMenu");
            var mc = Object.FindFirstObjectByType<MainMenuController>(FindObjectsInactive.Include);
            if (mc == null || mc.detailName == null) return "ERR menufix: controller missing";
            SetHeading(mc.detailName, 44f);
            return Save(s);
        }

        private static string Loading()
        {
            var s = Open("LoadingScreen");
            var style = FrameAuthoring.EnsureStyle();
            var sp = ScreenAuthoring.MakeSprites();
            var blurred = AssetDatabase.LoadAssetAtPath<Texture2D>(SpriteDir + "/MenuBackgroundBlurred.png");
            ScreenAuthoring.AuthorLoading(style, sp, blurred);
            return Save(s);
        }

        private static string Game()
        {
            var s = Open("Game");
            var style = FrameAuthoring.EnsureStyle();
            var sp = ScreenAuthoring.MakeSprites();
            ScreenAuthoring.AuthorHud(style, sp);
            ScreenAuthoring.AuthorPause(style);

            foreach (var old in Object.FindObjectsByType<WorldBlurBackdrop>(FindObjectsInactive.Include, FindObjectsSortMode.None)) Object.DestroyImmediate(old.gameObject);
            PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(BlurPrefabPath), s);

            foreach (var old in Object.FindObjectsByType<ArcanumUI>(FindObjectsInactive.Include, FindObjectsSortMode.None)) Object.DestroyImmediate(old.gameObject);
            var arc = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(ArcanumAuthoring.PrefabPath), s);
            var arcCanvas = arc.transform.Find("ArcanumCanvas").gameObject;
            arcCanvas.SetActive(false);   // hidden in the scene (it would cover the HUD); open the prefab to edit it
            PrefabUtility.RecordPrefabInstancePropertyModifications(arcCanvas);

            foreach (var old in Object.FindObjectsByType<F3Overlay>(FindObjectsInactive.Include, FindObjectsSortMode.None)) Object.DestroyImmediate(old.gameObject);
            var f3 = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(ArcanumAuthoring.F3PrefabPath), s);
            var ov = f3.GetComponent<F3Overlay>();
            ov.canvas.enabled = false;
            PrefabUtility.RecordPrefabInstancePropertyModifications(ov.canvas);
            var dbg = Object.FindFirstObjectByType<DebugGUI>(FindObjectsInactive.Include);
            if (dbg != null)
            {
                var so = new SerializedObject(dbg);
                so.FindProperty("overlay").objectReferenceValue = ov;
                so.ApplyModifiedPropertiesWithoutUndo();
            }
            return Save(s);
        }

        // ── verification ─────────────────────────────────────────────────────

        public static string Verify()
        {
            var sb = new StringBuilder();
            var s = SceneManager.GetActiveScene();
            sb.Append("verify ").Append(s.name).Append(": ");
            int missingScripts = 0, nullRefs = 0;
            var problems = new List<string>();
            foreach (var root in s.GetRootGameObjects())
            {
                foreach (var t in root.GetComponentsInChildren<Transform>(true))
                {
                    int m = GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(t.gameObject);
                    if (m > 0) { missingScripts += m; problems.Add("missing script on " + PathOf(t)); }
                }
                foreach (var c in root.GetComponentsInChildren<MonoBehaviour>(true))
                {
                    if (c == null) continue;
                    var ns = c.GetType().Namespace ?? "";
                    if (!ns.StartsWith("Orivilon.UI.Refresh") && !(c is DebugGUI) && !(c is F3Overlay) && !(c is ArcanumUI)) continue;
                    var so = new SerializedObject(c);
                    var it = so.GetIterator();
                    while (it.NextVisible(true))
                        if (it.propertyType == SerializedPropertyType.ObjectReference && it.objectReferenceValue == null && it.objectReferenceInstanceIDValue != 0)
                        { nullRefs++; problems.Add("broken ref " + c.GetType().Name + "." + it.propertyPath + " on " + PathOf(c.transform)); }
                }
            }
            int frames = 0, pieces = 0, joins = 0, fits = 0;
            foreach (var f in Object.FindObjectsByType<UiFrame>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                frames++;
                foreach (var st in f.states)
                foreach (var p in st.pieces)
                {
                    if (p.piece == null) problems.Add("frame " + f.name + " state " + st.name + " has an empty piece");
                    if (p.fit != UiFrame.FitSide.None) { fits++; if (p.fitTo == null) problems.Add("frame " + f.name + "/" + st.name + " fit target missing"); }
                }
            }
            foreach (var p in Object.FindObjectsByType<FramePiece>(FindObjectsInactive.Include, FindObjectsSortMode.None)) { pieces++; if (p.edge == null || p.fill == null || p.style == null) problems.Add("piece " + PathOf(p.transform) + " incomplete"); }
            foreach (var j in Object.FindObjectsByType<FrameJoin>(FindObjectsInactive.Include, FindObjectsSortMode.None)) { joins++; if (j.a == null || j.b == null) problems.Add("join " + j.name + " incomplete"); }
            int canvases = Object.FindObjectsByType<Canvas>(FindObjectsInactive.Include, FindObjectsSortMode.None).Length;
            sb.Append("frames ").Append(frames).Append(", pieces ").Append(pieces).Append(", joins ").Append(joins).Append(", fitted ").Append(fits)
              .Append(", canvases ").Append(canvases).Append(", missingScripts ").Append(missingScripts).Append(", brokenRefs ").Append(nullRefs);
            if (s.name == "Game")
            {
                var hc = Object.FindFirstObjectByType<HudController>(FindObjectsInactive.Include);
                sb.Append(", hud ").Append(hc != null && hc.frame != null ? "ok" : "MISSING");
                var pc = Object.FindFirstObjectByType<PauseController>(FindObjectsInactive.Include);
                sb.Append(", pause ").Append(pc != null && pc.frame != null ? "ok" : "MISSING");
                sb.Append(", arcanum ").Append(Object.FindObjectsByType<ArcanumUI>(FindObjectsInactive.Include, FindObjectsSortMode.None).Length);
                sb.Append(", f3 ").Append(Object.FindObjectsByType<F3Overlay>(FindObjectsInactive.Include, FindObjectsSortMode.None).Length);
                sb.Append(", blur ").Append(Object.FindObjectsByType<WorldBlurBackdrop>(FindObjectsInactive.Include, FindObjectsSortMode.None).Length);
            }
            if (problems.Count > 0) sb.Append(" | ").Append(string.Join("; ", problems.GetRange(0, Mathf.Min(12, problems.Count))));
            return sb.ToString();
        }
    }
}
