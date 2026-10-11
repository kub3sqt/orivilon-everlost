using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Orivilon.UI.Refresh.EditorTools
{
    /// <summary>
    /// QA helpers for checking the saved UI outside Play Mode (editor only):
    ///   editshot name        – saves what the Game view shows right now (edit mode)
    ///   previewprefab path   – instantiates a prefab into a temporary additive scene (nothing saved)
    ///   previewclose         – removes that temporary scene
    ///   previewstate Frame State – applies a UiFrame state in the open scene without saving
    /// </summary>
    public static class EditPreview
    {
        private static Scene temp;

        private static EditorWindow GameView()
        {
            var t = typeof(EditorWindow).Assembly.GetType("UnityEditor.GameView");
            return EditorWindow.GetWindow(t, false, null, false);
        }

        public static string Shot(string name)
        {
            var gv = GameView();
            RenderTexture rt = null;
            var seen = new System.Text.StringBuilder();
            for (var t = gv.GetType(); t != null && rt == null; t = t.BaseType)
                foreach (var f in t.GetFields(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.DeclaredOnly))
                {
                    if (!typeof(Texture).IsAssignableFrom(f.FieldType)) continue;
                    seen.Append(t.Name).Append('.').Append(f.Name).Append(' ');
                    var v = f.GetValue(gv) as RenderTexture;
                    if (v != null && v.width >= 640) { rt = v; break; }
                }
            gv.Repaint();
            if (rt == null) return RenderCanvases(name, 1920, 1080, new Color(0.55f, 0.78f, 1f));
            var prev = RenderTexture.active;
            RenderTexture.active = rt;
            var tex = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
            tex.Apply();
            RenderTexture.active = prev;
            if (SystemInfo.graphicsUVStartsAtTop)
            {
                // game view texture is stored top-down on Metal/D3D
                var px = tex.GetPixels32(); var fl = new Color32[px.Length]; int w = tex.width, h = tex.height;
                for (int y = 0; y < h; y++) System.Array.Copy(px, y * w, fl, (h - 1 - y) * w, w);
                tex.SetPixels32(fl); tex.Apply();
            }
            string path = Path.Combine(Path.GetDirectoryName(Application.dataPath), "_reports", "remote", name + ".png");
            File.WriteAllBytes(path, tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
            return "editshot " + path + " " + rt.width + "x" + rt.height;
        }

        /// <summary>
        /// Renders every overlay canvas of the loaded scenes (as saved, outside Play Mode) over a
        /// flat bright background into a PNG. The canvases are switched to a temporary camera for
        /// the capture and switched back afterwards.
        /// </summary>
        public static string RenderCanvases(string name, int w, int h, Color background)
        {
            var go = new GameObject("__EditShotCamera") { hideFlags = HideFlags.HideAndDontSave };
            var cam = go.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = background;
            cam.orthographic = true; cam.cullingMask = -1; cam.nearClipPlane = 0.1f; cam.farClipPlane = 1000f;
            go.transform.position = new Vector3(0f, -50000f, 0f);   // away from any world geometry
            var rt = RenderTexture.GetTemporary(w, h, 24, RenderTextureFormat.ARGB32);
            cam.targetTexture = rt;
            var changed = new System.Collections.Generic.List<(Canvas c, RenderMode m, Camera cam, float d)>();
            foreach (var c in Object.FindObjectsByType<Canvas>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
            {
                if (!c.isRootCanvas || c.renderMode != RenderMode.ScreenSpaceOverlay || !c.enabled) continue;
                changed.Add((c, c.renderMode, c.worldCamera, c.planeDistance));
                c.renderMode = RenderMode.ScreenSpaceCamera; c.worldCamera = cam; c.planeDistance = 100f;
            }
            Canvas.ForceUpdateCanvases();
            cam.Render();
            var prev = RenderTexture.active; RenderTexture.active = rt;
            var tex = new Texture2D(w, h, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, w, h), 0, 0); tex.Apply();
            RenderTexture.active = prev;
            foreach (var x in changed) { x.c.renderMode = x.m; x.c.worldCamera = x.cam; x.c.planeDistance = x.d; }
            cam.targetTexture = null; RenderTexture.ReleaseTemporary(rt); Object.DestroyImmediate(go);
            string path = Path.Combine(Path.GetDirectoryName(Application.dataPath), "_reports", "remote", name + ".png");
            File.WriteAllBytes(path, tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
            return "editshot(render) " + path + " canvases " + changed.Count;
        }

        public static string PreviewPrefab(string path)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (prefab == null) return "ERR previewprefab: " + path;
            if (!temp.IsValid()) temp = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
            var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, temp);
            foreach (var c in go.GetComponentsInChildren<Canvas>(true)) { c.gameObject.SetActive(true); c.enabled = true; }
            return "previewprefab " + path;
        }

        public static string Close()
        {
            if (temp.IsValid()) EditorSceneManager.CloseScene(temp, true);
            temp = default;
            return "previewclose";
        }

        /// <summary>Reloads the open scene from disk, dropping preview-only changes.</summary>
        public static string Revert()
        {
            Close();
            var s = SceneManager.GetActiveScene();
            EditorSceneManager.OpenScene(s.path, OpenSceneMode.Single);
            return "previewrevert " + s.name;
        }

        /// <summary>Play Mode: cursor lock/visibility and menu flags (for input-ownership checks).</summary>
        public static string CursorState()
        {
            var gm = Orivilon.Core.GameManager.instance;
            var hud = HudController.Instance;
            int frames = Object.FindObjectsByType<UiFrame>(FindObjectsInactive.Include, FindObjectsSortMode.None).Length;
            int blurs = Object.FindObjectsByType<WorldBlurBackdrop>(FindObjectsInactive.Include, FindObjectsSortMode.None).Length;
            return "cursor " + Cursor.lockState + "/" + (Cursor.visible ? "visible" : "hidden") +
                   (gm != null ? " menuOpen " + gm.isMenuOpen + " paused " + gm.IsPaused + " arcanum " + gm.IsArcanumOpen : " no GameManager") +
                   (hud != null ? " hudOpen " + hud.IsOpen + " frameAlpha " + (hud.frameGroup != null ? hud.frameGroup.alpha.ToString("0.00") : "-") : "") +
                   " frames " + frames + " blurBackdrops " + blurs + " blurActive " + WorldBlurBackdrop.IsActive;
        }

        public static string State(string frameName, string state)
        {
            foreach (var f in Object.FindObjectsByType<UiFrame>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                if (f.name == frameName) { f.ApplyInstant(f.Find(state)); return "previewstate " + frameName + " " + state; }
            return "ERR previewstate: no frame " + frameName;
        }
    }
}
