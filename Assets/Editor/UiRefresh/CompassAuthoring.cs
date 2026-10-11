using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using Orivilon.Player;
using Orivilon.UI.HUD;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using static Orivilon.UI.Refresh.EditorTools.UiAuthoringKit;

namespace Orivilon.UI.Refresh.EditorTools
{
    /// <summary>
    /// HUD compass (bridge "compassui &lt;step&gt;").
    ///   build  – Game scene open: saves Assets/UI/Refresh/Prefabs/HUD/Compass.prefab (FramePanel edge-attached tab,
    ///            masked strip with ticks + direction labels, centre marker, marker template) and places it on the HUD
    ///            canvas right after HudFrame. Refuses if the scene already has a compass.
    ///   state  – Play Mode: canvas, panel rect, visible labels with x, duplicates, raycast targets, overlaps.
    ///   yaw &lt;deg&gt; – Play Mode: turns the player camera (FirstPersonController.SetLook).
    ///   sweep &lt;from&gt; &lt;to&gt; &lt;deg/s&gt; – Play Mode: turns smoothly and logs the N label every frame to
    ///            _reports/remote/compass_sweep.txt (wrap seam check).
    /// </summary>
    public static class CompassAuthoring
    {
        public const string PrefabPath = PrefabDir + "/HUD/Compass.prefab";
        private const float W = 700f, H = 60f, Inset = 14f, LabelY = 11f, TickBaseY = -18f;
        private static readonly string[] Names = { "N", "NE", "E", "SE", "S", "SW", "W", "NW" };

        public static string Run(string step)
        {
            try
            {
                var a = step.Split(' ');
                switch (a[0])
                {
                    case "build": return Build();
                    case "restyle": return Restyle();
                    case "state": return "compassui state: " + State();
                    case "arcanum":
                        {
                            var gm = Orivilon.Core.GameManager.instance;
                            var mi = typeof(Orivilon.Core.GameManager).GetMethod("ToggleArcanum", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
                            if (gm == null || mi == null) return "ERR compassui arcanum";
                            mi.Invoke(gm, null);
                            return "compassui arcanum toggled";
                        }
                    case "yaw": return Yaw(float.Parse(a[1], CultureInfo.InvariantCulture));
                    case "sweep": return Sweep(float.Parse(a[1], CultureInfo.InvariantCulture), float.Parse(a[2], CultureInfo.InvariantCulture), float.Parse(a[3], CultureInfo.InvariantCulture));
                }
                return "ERR compassui: unknown step " + step;
            }
            catch (System.Exception e) { return "ERR compassui " + step + ": " + e.Message; }
        }

        // ------------------------------------------------------------------ build

        private static string Build()
        {
            var scene = SceneManager.GetActiveScene();
            if (scene.name != "Game") return "ERR compassui build: open the Game scene first (current: " + scene.name + ")";
            if (Object.FindFirstObjectByType<CompassUI>(FindObjectsInactive.Include) != null) return "ERR compassui build: the scene already has a compass";
            Transform canvas = FindHudCanvas();
            if (canvas == null) return "ERR compassui build: HUD canvas (with HudFrame) not found";
            var hudFrame = canvas.Find("HudFrame");

            var style = FrameAuthoring.EnsureStyle();
            var panelPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(FrameAuthoring.PanelPrefabPath);
            if (style == null || panelPrefab == null) return "ERR compassui build: FrameStyle / FramePanel missing";
            var dot = AssetDatabase.LoadAssetAtPath<Sprite>(SpriteDir + "/CompassDot.png") ?? Dot();

            var root = new GameObject("Compass", typeof(RectTransform));
            root.layer = 5;
            var rt = (RectTransform)root.transform;
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 1f);
            rt.pivot = new Vector2(0.5f, 1f);
            rt.sizeDelta = new Vector2(W, H);
            rt.anchoredPosition = Vector2.zero;

            // stencil reset first (as in every UiFrame), then the panel: edge-attached tab, top edge off screen
            var reset = NewImage("StencilReset", rt, null, Color.white, false);
            reset.material = style.resetMaterial;
            Stretch(reset.rectTransform);
            reset.rectTransform.offsetMin = new Vector2(-32f, -32f); reset.rectTransform.offsetMax = new Vector2(32f, 92f);

            var panel = (GameObject)PrefabUtility.InstantiatePrefab(panelPrefab, rt);
            panel.name = "Panel_compass";
            var prt = (RectTransform)panel.transform;
            prt.anchorMin = Vector2.zero; prt.anchorMax = Vector2.one; prt.pivot = new Vector2(0.5f, 0.5f);
            prt.offsetMin = Vector2.zero; prt.offsetMax = new Vector2(0f, 60f);

            var stripRt = NewRect("Strip", rt);
            stripRt.anchorMin = Vector2.zero; stripRt.anchorMax = Vector2.one;
            stripRt.offsetMin = new Vector2(Inset, 0f); stripRt.offsetMax = new Vector2(-Inset, 0f);
            var mask = stripRt.gameObject.AddComponent<RectMask2D>();
            mask.softness = new Vector2Int(72, 0);

            var scale = NewRect("Scale", stripRt);
            Center(scale, Vector2.zero);
            var items = new List<CompassUI.ScaleItem>();
            for (int az = 0; az < 360; az += 5)
            {
                bool cardinal = az % 90 == 0, major = az % 45 == 0, medium = az % 15 == 0;
                float h = cardinal ? 13f : major ? 11f : medium ? 8f : 5f;
                float w = major || medium ? 2f : 1.5f;
                float alpha = cardinal ? 0.95f : major ? 0.8f : medium ? 0.5f : 0.3f;
                var t = NewImage("Tick_" + az.ToString("000"), scale, null, new Color(Ink.r, Ink.g, Ink.b, alpha), false);
                Center(t.rectTransform, new Vector2(0.5f, 0f));
                t.rectTransform.sizeDelta = new Vector2(w, h);
                t.rectTransform.anchoredPosition = new Vector2(0f, TickBaseY);
                items.Add(new CompassUI.ScaleItem { rect = t.rectTransform, azimuth = az });
                if (!major) continue;
                string n = Names[az / 45];
                var l = NewText("Label_" + n, scale, n, false, cardinal ? 20f : 14f, new Color(Ink.r, Ink.g, Ink.b, cardinal ? 1f : 0.7f), TextAlignmentOptions.Center, true);
                Center(l.rectTransform, new Vector2(0.5f, 0.5f));
                l.rectTransform.sizeDelta = new Vector2(64f, 28f);
                l.rectTransform.anchoredPosition = new Vector2(0f, LabelY);
                l.overflowMode = TextOverflowModes.Overflow;
                items.Add(new CompassUI.ScaleItem { rect = l.rectTransform, azimuth = az });
            }

            // markers sit outside the mask (their labels hang just below the panel, as before)
            var markers = NewRect("Markers", rt);
            Center(markers, new Vector2(0.5f, 0.5f));
            markers.anchorMin = markers.anchorMax = new Vector2(0.5f, 0.5f);
            var tpl = NewRect("MarkerTemplate", markers);
            Center(tpl, new Vector2(0.5f, 0.5f));
            var cg = tpl.gameObject.AddComponent<CanvasGroup>();
            cg.alpha = 0f; cg.blocksRaycasts = false; cg.interactable = false;
            var stem = NewImage("Stem", tpl, null, new Color(1f, 1f, 1f, 0.45f), false);
            Center(stem.rectTransform, new Vector2(0.5f, 1f));
            stem.rectTransform.sizeDelta = new Vector2(1f, 10f);
            stem.rectTransform.anchoredPosition = new Vector2(0f, TickBaseY + 13f);
            var icon = NewImage("Icon", tpl, dot, Color.white, false);
            Center(icon.rectTransform, new Vector2(0.5f, 0.5f));
            icon.rectTransform.sizeDelta = new Vector2(16f, 16f);
            icon.rectTransform.anchoredPosition = new Vector2(0f, -H * 0.5f - 12f);
            var sh = icon.gameObject.AddComponent<Shadow>();
            sh.effectColor = new Color(0f, 0f, 0f, 0.65f); sh.effectDistance = new Vector2(1f, -1f);
            var ml = NewText("Label", tpl, "", false, 13f, new Color(Ink.r, Ink.g, Ink.b, 0.9f), TextAlignmentOptions.Top, false);
            Center(ml.rectTransform, new Vector2(0.5f, 1f));
            ml.rectTransform.sizeDelta = new Vector2(160f, 18f);
            ml.rectTransform.anchoredPosition = new Vector2(0f, -H * 0.5f - 22f);
            ml.overflowMode = TextOverflowModes.Overflow;
            tpl.gameObject.SetActive(false);

            // subtle fixed bearing marker over the tick row
            var center = NewImage("CenterMarker", rt, null, new Color(Ink.r, Ink.g, Ink.b, 0.95f), false);
            Center(center.rectTransform, new Vector2(0.5f, 0f));
            center.rectTransform.sizeDelta = new Vector2(2f, 22f);
            center.rectTransform.anchoredPosition = new Vector2(0f, TickBaseY - 4f);

            var ui = root.AddComponent<CompassUI>();
            var so = new SerializedObject(ui);
            so.FindProperty("strip").objectReferenceValue = stripRt;
            so.FindProperty("markersRoot").objectReferenceValue = markers;
            so.FindProperty("markerTemplate").objectReferenceValue = tpl;
            var arr = so.FindProperty("items");
            arr.arraySize = items.Count;
            for (int i = 0; i < items.Count; i++)
            {
                var e = arr.GetArrayElementAtIndex(i);
                e.FindPropertyRelative("rect").objectReferenceValue = items[i].rect;
                e.FindPropertyRelative("azimuth").floatValue = items[i].azimuth;
            }
            so.ApplyModifiedPropertiesWithoutUndo();
            foreach (var g in root.GetComponentsInChildren<Graphic>(true)) g.raycastTarget = false;

            EnsureFolder(Path.GetDirectoryName(PrefabPath).Replace('\\', '/'));
            var asset = PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            Object.DestroyImmediate(root);

            var inst = (GameObject)PrefabUtility.InstantiatePrefab(asset, canvas);
            if (hudFrame != null) inst.transform.SetSiblingIndex(hudFrame.GetSiblingIndex() + 1);
            EditorSceneManager.MarkSceneDirty(scene);
            if (!EditorSceneManager.SaveScene(scene)) return "ERR compassui build: saving Game failed";
            return "compassui build: " + PrefabPath + " → " + PathOf(inst.transform) + " (sibling " + inst.transform.GetSiblingIndex() + ", items " + items.Count + ", HudController on canvas " + (canvas.GetComponent<HudController>() != null) + ")";
        }

        /// <summary>Larger direction labels (readable at 1280x720 where the HUD scales to 0.67) and a slightly
        /// lower tick row to keep the padding balanced. Edits the saved prefab; the scene instance follows.</summary>
        private static string Restyle()
        {
            var root = PrefabUtility.LoadPrefabContents(PrefabPath);
            int n = 0;
            try
            {
                foreach (var t in root.GetComponentsInChildren<TextMeshProUGUI>(true))
                {
                    if (!t.name.StartsWith("Label_")) continue;
                    bool cardinal = t.text.Length == 1;
                    t.fontSize = cardinal ? 26f : 18f;
                    t.color = new Color(Ink.r, Ink.g, Ink.b, cardinal ? 1f : 0.78f);
                    t.rectTransform.sizeDelta = new Vector2(72f, 32f);
                    var p = t.rectTransform.anchoredPosition; p.y = 9f; t.rectTransform.anchoredPosition = p;
                    n++;
                }
                foreach (var img in root.GetComponentsInChildren<Image>(true))
                {
                    if (!img.name.StartsWith("Tick_")) continue;
                    var p = img.rectTransform.anchoredPosition; p.y = -20f; img.rectTransform.anchoredPosition = p;
                    var sd = img.rectTransform.sizeDelta; if (sd.y > 12.5f) sd.y = 12f; img.rectTransform.sizeDelta = sd;
                }
                // authored at heading 0 so the saved asset reads correctly outside Play Mode
                var cu = root.GetComponent<CompassUI>();
                var so = new SerializedObject(cu);
                var arr = so.FindProperty("items");
                float ppd = (W - 2f * Inset) / so.FindProperty("visibleDegrees").floatValue;
                for (int i = 0; i < arr.arraySize; i++)
                {
                    var e = arr.GetArrayElementAtIndex(i);
                    var r = e.FindPropertyRelative("rect").objectReferenceValue as RectTransform;
                    if (r == null) continue;
                    var ap = r.anchoredPosition; ap.x = Mathf.DeltaAngle(0f, e.FindPropertyRelative("azimuth").floatValue) * ppd; r.anchoredPosition = ap;
                }
                var c = root.transform.Find("CenterMarker") as RectTransform;
                if (c != null) { c.anchoredPosition = new Vector2(0f, -24f); c.sizeDelta = new Vector2(2f, 20f); }
                PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
            return "compassui restyle: " + n + " labels";
        }

        private static Transform FindHudCanvas()
        {
            foreach (var c in Object.FindObjectsByType<Canvas>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                if (c.isRootCanvas && c.transform.Find("HudFrame") != null) return c.transform;
            return null;
        }

        private static void Center(RectTransform r, Vector2 pivot)
        {
            r.anchorMin = r.anchorMax = new Vector2(0.5f, 0.5f);
            r.pivot = pivot;
            r.sizeDelta = Vector2.zero;
            r.anchoredPosition = Vector2.zero;
        }

        private static Sprite Dot()
        {
            const int S = 64;
            var tex = NewTex(S, S);
            float rad = S * 0.5f - 1f; var c = new Vector2(S * 0.5f, S * 0.5f);
            for (int y = 0; y < S; y++)
                for (int x = 0; x < S; x++)
                    tex.SetPixel(x, y, new Color(1f, 1f, 1f, Mathf.Clamp01(rad - Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), c))));
            tex.Apply();
            return SaveSprite("CompassDot", tex, Vector4.zero);
        }

        // ------------------------------------------------------------------ QA

        public static string State()
        {
            var ui = Object.FindFirstObjectByType<CompassUI>(FindObjectsInactive.Include);
            if (ui == null) return "no compass";
            var sb = new StringBuilder();
            var canvas = ui.GetComponentInParent<Canvas>(true);
            sb.Append("active ").Append(ui.gameObject.activeInHierarchy).Append(" canvas '").Append(canvas != null ? canvas.rootCanvas.name : "-").Append('\'');
            sb.Append(" hudCanvas ").Append(canvas != null && canvas.rootCanvas.transform.Find("HudFrame") != null);
            sb.Append(" count ").Append(Object.FindObjectsByType<CompassUI>(FindObjectsInactive.Include, FindObjectsSortMode.None).Length);
            var cam = Camera.main;
            var fpc = Object.FindFirstObjectByType<FirstPersonController>();
            if (fpc != null) sb.AppendFormat(CultureInfo.InvariantCulture, " yaw {0:0.0}", fpc.GetComponentInChildren<Camera>().transform.eulerAngles.y);
            var rt = (RectTransform)ui.transform;
            Rect r = ScreenRect(rt);
            sb.AppendFormat(CultureInfo.InvariantCulture, " panel {0:0},{1:0} {2:0}x{3:0} (screen {4}x{5})", r.x, r.y, r.width, r.height, Screen.width, Screen.height);
            var strip = rt.Find("Strip") as RectTransform;
            Rect sr = ScreenRect(strip);
            var seen = new Dictionary<string, int>();
            sb.Append(" visible");
            foreach (var t in rt.GetComponentsInChildren<TextMeshProUGUI>(false))
            {
                if (!t.name.StartsWith("Label_")) continue;
                Rect lr = ScreenRect(t.rectTransform);
                float cx = lr.center.x;
                if (cx < sr.xMin || cx > sr.xMax) continue;
                seen.TryGetValue(t.text, out int k); seen[t.text] = k + 1;
                sb.AppendFormat(CultureInfo.InvariantCulture, " {0}@{1:0}", t.text, cx - sr.center.x);
            }
            int dup = 0; foreach (var kv in seen) if (kv.Value > 1) dup++;
            sb.Append(" dup ").Append(dup);
            int rc = 0; foreach (var g in rt.GetComponentsInChildren<Graphic>(true)) if (g.raycastTarget) rc++;
            sb.Append(" raycast ").Append(rc);
            // overlaps with other visible HUD graphics on the same canvas (outside the compass)
            if (canvas != null && ui.gameObject.activeInHierarchy)
            {
                var hits = new List<string>();
                foreach (var g in canvas.rootCanvas.GetComponentsInChildren<Graphic>(false))
                {
                    if (g.transform.IsChildOf(rt) || !g.enabled || g.canvasRenderer.cull || g.color.a < 0.05f) continue;
                    var cgp = g.GetComponentInParent<CanvasGroup>();
                    if (cgp != null && cgp.alpha < 0.05f) continue;
                    Rect gr = ScreenRect(g.rectTransform);
                    if (gr.width >= Screen.width * 0.9f && gr.height >= Screen.height * 0.9f) continue;   // full-screen layers
                    if (gr.Overlaps(r)) hits.Add(g.name);
                }
                sb.Append(" overlaps [").Append(string.Join(",", hits)).Append(']');
            }
            return sb.ToString();
        }

        private static Rect ScreenRect(RectTransform t)
        {
            var c = new Vector3[4]; t.GetWorldCorners(c);
            var canvas = t.GetComponentInParent<Canvas>();
            Camera cam = canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay ? canvas.worldCamera : null;
            Vector2 a = RectTransformUtility.WorldToScreenPoint(cam, c[0]), b = RectTransformUtility.WorldToScreenPoint(cam, c[2]);
            return Rect.MinMaxRect(Mathf.Min(a.x, b.x), Mathf.Min(a.y, b.y), Mathf.Max(a.x, b.x), Mathf.Max(a.y, b.y));
        }

        private static string Yaw(float deg)
        {
            var fpc = Object.FindFirstObjectByType<FirstPersonController>();
            if (fpc == null) return "ERR compassui yaw: no player";
            fpc.SetLook(deg, fpc.CurrentLook.y);
            return "compassui yaw " + deg;
        }

        private static float sweepFrom, sweepTo, sweepSpeed, sweepYaw;
        private static string sweepFile;

        private static string Sweep(float from, float to, float speed)
        {
            var fpc = Object.FindFirstObjectByType<FirstPersonController>();
            if (fpc == null) return "ERR compassui sweep: no player";
            sweepFrom = from; sweepTo = to; sweepSpeed = speed; sweepYaw = from;
            sweepFile = Path.Combine(Directory.GetParent(Application.dataPath).FullName, "_reports", "remote", "compass_sweep.txt");
            File.WriteAllText(sweepFile, "yaw(set) camYaw N.x E.x W.x NE.x NW.x (strip-local, NaN = outside strip)\n");
            fpc.SetLook(from, fpc.CurrentLook.y);
            EditorApplication.update -= SweepTick;
            EditorApplication.update += SweepTick;
            return "compassui sweep " + from + "→" + to + " at " + speed + "°/s → " + sweepFile;
        }

        private static void SweepTick()
        {
            var fpc = Application.isPlaying ? Object.FindFirstObjectByType<FirstPersonController>() : null;
            var ui = Object.FindFirstObjectByType<CompassUI>();
            if (fpc == null || ui == null) { EditorApplication.update -= SweepTick; return; }
            var rt = (RectTransform)ui.transform;
            var strip = (RectTransform)rt.Find("Strip");
            Rect sr = ScreenRect(strip);
            var sb = new StringBuilder();
            float camYaw = fpc.GetComponentInChildren<Camera>().transform.eulerAngles.y;
            sb.AppendFormat(CultureInfo.InvariantCulture, "{0:0.00} {1:0.00}", Mathf.Repeat(sweepYaw, 360f), camYaw);
            foreach (var n in new[] { "N", "E", "W", "NE", "NW" })
            {
                var l = rt.Find("Strip/Scale/Label_" + n) as RectTransform;
                float x = ScreenRect(l).center.x;
                sb.Append(' ').Append(x < sr.xMin || x > sr.xMax ? "NaN" : (x - sr.center.x).ToString("0.0", CultureInfo.InvariantCulture));
            }
            File.AppendAllText(sweepFile, sb.ToString() + "\n");
            float dir = Mathf.Sign(sweepTo - sweepFrom);
            sweepYaw += dir * sweepSpeed * Time.unscaledDeltaTime;
            if ((sweepTo - sweepYaw) * dir <= 0f) { EditorApplication.update -= SweepTick; File.AppendAllText(sweepFile, "done\n"); return; }
            fpc.SetLook(Mathf.Repeat(sweepYaw, 360f), fpc.CurrentLook.y);
        }
    }
}
