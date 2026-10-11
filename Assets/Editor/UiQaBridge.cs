using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace Orivilon.EditorTools
{
    /// <summary>
    /// UI QA příkazy pro RemoteBridge (jen editor, do buildu nejde):
    /// <c>uidump jméno [kořen]</c> – hierarchie všech canvasů s obrazovkovými obdélníky;
    /// <c>loadwatch on|off</c> – během loading screenu hlídá každý snímek, zda nějaká herní
    /// grafika (mimo loading scénu) nemá nenulovou zděděnou alfu; výsledek do out.log.
    /// </summary>
    internal static class UiQaBridge
    {
        private static readonly string Dir =
            Path.Combine(Path.GetDirectoryName(Application.dataPath), "_reports", "remote");

        private static bool watching;
        private static int watchFrames, leakFrames, lastFrame = -1;
        private static readonly HashSet<string> leakNames = new HashSet<string>();

        public static bool TryRun(string line, Action<string> outFn)
        {
            if (line.StartsWith("uidump "))
            {
                var p = line.Substring(7).Trim().Split(' ');
                string file = Path.Combine(Dir, p[0] + ".txt");
                File.WriteAllText(file, Dump(p.Length > 1 ? p[1] : null));
                outFn("uidump " + file);
                return true;
            }
            if (line.StartsWith("loadwatch "))
            {
                string a = line.Substring(10).Trim();
                if (a == "on")
                {
                    watching = true; watchFrames = leakFrames = 0; leakNames.Clear();
                    EditorApplication.update -= Watch; EditorApplication.update += Watch;
                    outFn("loadwatch on");
                }
                else
                {
                    watching = false; EditorApplication.update -= Watch;
                    outFn(string.Format("loadwatch report: loading frames {0}, frames with visible game UI {1}; {2}",
                        watchFrames, leakFrames, string.Join(", ", leakNames)));
                }
                return true;
            }
            if (line.StartsWith("f3 "))
            {
                var d = UnityEngine.Object.FindFirstObjectByType<Orivilon.Core.DebugGUI>();
                if (d == null) { outFn("ERR f3: DebugGUI chybí"); return true; }
                d.SetToggled(line.Substring(3).Trim() == "on");
                outFn("f3 " + d.IsToggled);
                return true;
            }
            if (line.StartsWith("f3dump "))
            {
                // F3 text vs. autoritativní RegionAt v téže poloze hráče (kořen vieweru).
                var t = Orivilon.World.Generation.VoxelTerrain.instance;
                var pos = Orivilon.Core.DebugGUI.LastSamplePos;
                string shown = System.Text.RegularExpressions.Regex.Replace(Orivilon.Core.DebugGUI.LastText, "<[^>]+>", "").Replace('\u2007', ' ');
                string auth = "?";
                Vector3 now = Vector3.zero;
                if (t != null)
                {
                    Transform pr = t.viewer != null ? (t.viewer.root != null ? t.viewer.root : t.viewer) : null;
                    if (pr != null) now = pr.position;
                    var w = t.RegionAt(now.x, now.z, true, out float surf, out _);
                    int dm = w.Dominant();
                    auth = string.Format(CultureInfo.InvariantCulture, "{0} ({1}) {2:0}% surf {3:0.0}", Orivilon.Core.GameConsole.RegionCanonicalId(dm),
                        Orivilon.Core.GameConsole.RegionDisplayName(dm), w.Get(dm) * 100f, surf);
                }
                string name = line.Substring(7).Trim();
                string res = string.Format(CultureInfo.InvariantCulture,
                    "seed {0} player ({1:0.0}, {2:0.0}, {3:0.0}) | F3 sample ({4:0.0}, {5:0.0}) region {6} = {7} | authoritative now: {8} | query {9:0.00} ms",
                    t != null ? t.WorldSeed : 0, now.x, now.y, now.z, pos.x, pos.z, Orivilon.Core.DebugGUI.LastRegion,
                    Orivilon.Core.DebugGUI.LastRegion >= 0 ? Orivilon.Core.GameConsole.RegionCanonicalId(Orivilon.Core.DebugGUI.LastRegion) : "-", auth, Orivilon.Core.DebugGUI.LastBiomeQueryMs);
                File.WriteAllText(Path.Combine(Dir, name + ".txt"), res + "\n----\n" + shown);
                File.AppendAllText(Path.Combine(Dir, "f3_biome_log.txt"), name + " | " + res + "\n");
                outFn("f3dump " + name + ": " + res);
                return true;
            }
            if (line.StartsWith("qaworld "))
            {
                // Disabled: it created index entries with an empty save path (saves landed in the project folder).
                outFn("ERR qaworld is disabled – use qalaunch (isolated save folder)");
                return true;
            }
            if (line == "mmselect" || line == "mmclose")
            {
                var ui = UnityEngine.Object.FindFirstObjectByType<Orivilon.UI.Menu.MainMenuUI>(FindObjectsInactive.Include);
                if (ui == null) { outFn("ERR mmselect"); return true; }
                if (line == "mmselect") ui.OpenSelectWorldMenu(); else ui.CloseMenus();
                outFn(line);
                return true;
            }
            if (line == "lstate")
            {
                var lm = Orivilon.Core.LoadingScreenManager.instance;
                var gm = Orivilon.Core.GameManager.instance;
                var pl = GameObject.FindGameObjectWithTag("Player");
                outFn(string.Format(CultureInfo.InvariantCulture, "lstate: loadingPanel {0}, IsLoading {1}, gm.IsLoadingComplete {2}, player {3}",
                    lm != null && lm.loadingPanel != null && lm.loadingPanel.activeInHierarchy, Orivilon.Core.SceneLoader.IsLoading,
                    gm != null && gm.IsLoadingComplete, pl != null ? pl.transform.position.ToString("F1") : "-"));
                return true;
            }
            if (line == "unveil")
            {
                // Pozitivní kontrola pro loadwatch: zruší závoj herního UI (simuluje stav před opravou).
                var root = UnityEngine.Object.FindFirstObjectByType<Orivilon.Inventory.InventoryRoot>(FindObjectsInactive.Include);
                var cv = root != null ? root.GetComponentInParent<Canvas>(true) : null;
                var g = cv != null ? cv.rootCanvas.GetComponent<CanvasGroup>() : null;
                if (g != null) g.alpha = 1f;
                outFn("unveil " + (g != null));
                return true;
            }
            if (line.StartsWith("tmpinfo "))
            {
                string n = line.Substring(8).Trim(); var sb = new StringBuilder("tmpinfo " + n + ":");
                foreach (var t in UnityEngine.Object.FindObjectsByType<TMPro.TMP_Text>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                {
                    if (t.name != n) continue;
                    var r = t.rectTransform; var c = new Vector3[4]; r.GetWorldCorners(c);
                    t.ForceMeshUpdate();
                    sb.AppendFormat(CultureInfo.InvariantCulture, " ['{0}' act {1} col {2} a {3:0.##} size {4} font {5} mat {6} rect {7:0},{8:0}-{9:0},{10:0} chars {11} vis {12} canvasA {13:0.##} renderer cull {14} alpha {15:0.##}]",
                        t.text, t.isActiveAndEnabled, t.color, t.alpha, t.fontSize, t.font != null ? t.font.name : "-", t.fontSharedMaterial != null ? t.fontSharedMaterial.name : "-",
                        c[0].x, c[0].y, c[2].x, c[2].y, t.textInfo.characterCount, t.textInfo.characterInfo != null && t.textInfo.characterCount > 0 && t.textInfo.characterInfo[0].isVisible,
                        t.canvasRenderer.GetInheritedAlpha(), t.canvasRenderer.cull, t.canvasRenderer.GetAlpha());
                }
                outFn(sb.ToString());
                return true;
            }
            if (line == "joins")
            {
                var sb = new StringBuilder("joins:");
                foreach (var j in UnityEngine.Object.FindObjectsByType<Orivilon.UI.Refresh.FrameJoin>(FindObjectsSortMode.None))
                {
                    if (!j.Visible) continue;
                    var r = (RectTransform)j.transform;
                    var c = new Vector3[4]; r.GetWorldCorners(c);
                    var cam = j.GetComponentInParent<Canvas>().rootCanvas;
                    Vector2 v = RectTransformUtility.WorldToScreenPoint(null, j.transform.position);
                    sb.AppendFormat(CultureInfo.InvariantCulture, " [{0}/{1} k {2:0.###},{3:0.###} screen {4:0.#},{5:0.#} canvasScale {6:0.###}]", j.transform.root.name, j.name, r.localScale.x, r.localScale.y, v.x, v.y, cam.scaleFactor);
                }
                outFn(sb.ToString());
                return true;
            }
            if (line.StartsWith("qaselect "))
            {
                string n = line.Substring(9).Trim();
                foreach (var e in UnityEngine.Object.FindObjectsByType<Orivilon.UI.Menu.WorldListElement>(FindObjectsSortMode.None))
                {
                    var t = e.GetComponentInChildren<TMPro.TMP_Text>();
                    var b = e.GetComponentInChildren<Button>();
                    if (t != null && b != null && t.text == n) { b.onClick.Invoke(); outFn("qaselect " + n + " → " + Orivilon.UI.Refresh.EditorTools.SettingsQA.NetState()); return true; }
                }
                outFn("ERR qaselect " + n);
                return true;
            }
            if (line.StartsWith("click "))
            {
                string n = line.Substring(6).Trim();
                foreach (var b in UnityEngine.Object.FindObjectsByType<Button>(FindObjectsSortMode.None))
                    if (b.name == n && b.isActiveAndEnabled) { b.onClick.Invoke(); outFn("click " + n); return true; }
                outFn("ERR click " + n);
                return true;
            }
            if (line.StartsWith("hudalpha "))
            {
                // Čistý snímek světa pro náhledy (jen alfa kořenového CanvasGroup, nic se neukládá).
                var root = UnityEngine.Object.FindFirstObjectByType<Orivilon.Inventory.InventoryRoot>(FindObjectsInactive.Include);
                var cv = root != null ? root.GetComponentInParent<Canvas>(true) : null;
                var g = cv != null ? cv.rootCanvas.GetComponent<CanvasGroup>() : null;
                if (g != null) g.alpha = float.Parse(line.Substring(9).Trim(), CultureInfo.InvariantCulture);
                outFn("hudalpha " + (g != null));
                return true;
            }
            if (line == "mkfonts")
            {
                // UI refresh rollout: TMP font assety pro Space Mono (dynamické, v Resources/Fonts).
                var made = new System.Collections.Generic.List<string>();
                foreach (var nm in new[] { "SpaceMono-Regular", "SpaceMono-Bold" })
                {
                    string dst = "Assets/Resources/Fonts/" + nm + " SDF.asset";
                    if (AssetDatabase.LoadAssetAtPath<TMPro.TMP_FontAsset>(dst) != null) { made.Add(nm + " exists"); continue; }
                    var font = AssetDatabase.LoadAssetAtPath<Font>("Assets/Fonts/SpaceMono/" + nm + ".ttf");
                    if (font == null) { made.Add(nm + " TTF missing"); continue; }
                    if (!AssetDatabase.IsValidFolder("Assets/Resources/Fonts")) AssetDatabase.CreateFolder("Assets/Resources", "Fonts");
                    var fa = TMPro.TMP_FontAsset.CreateFontAsset(font, 90, 9, UnityEngine.TextCore.LowLevel.GlyphRenderMode.SDFAA, 1024, 1024,
                        TMPro.AtlasPopulationMode.Dynamic, true);
                    fa.name = nm + " SDF";
                    AssetDatabase.CreateAsset(fa, dst);
                    fa.atlasTextures[0].name = nm + " Atlas";
                    AssetDatabase.AddObjectToAsset(fa.atlasTextures[0], fa);
                    fa.material.name = nm + " Material";
                    AssetDatabase.AddObjectToAsset(fa.material, fa);
                    EditorUtility.SetDirty(fa);
                    made.Add(nm + " created");
                }
                AssetDatabase.SaveAssets();
                outFn("mkfonts: " + string.Join(", ", made));
                return true;
            }
            if (line.StartsWith("uimigrate ")) { outFn(Orivilon.UI.Refresh.EditorTools.UiMigration.Run(line.Substring(10).Trim())); return true; }
            if (line.StartsWith("editshot ")) { outFn(Orivilon.UI.Refresh.EditorTools.EditPreview.Shot(line.Substring(9).Trim())); return true; }
            if (line.StartsWith("previewprefab ")) { outFn(Orivilon.UI.Refresh.EditorTools.EditPreview.PreviewPrefab(line.Substring(14).Trim())); return true; }
            if (line.StartsWith("polish ")) { outFn(Orivilon.UI.Refresh.EditorTools.PolishAuthoring.Run(line.Substring(7).Trim())); return true; }
            if (line == "hoverenter") { outFn(Orivilon.UI.Refresh.EditorTools.PolishQA.Enter()); return true; }
            if (line == "hovermeasure") { outFn(Orivilon.UI.Refresh.EditorTools.PolishQA.Measure()); return true; }
            if (line == "hoverexit") { outFn(Orivilon.UI.Refresh.EditorTools.PolishQA.Exit()); return true; }
            if (line == "hoverrestored") { outFn(Orivilon.UI.Refresh.EditorTools.PolishQA.CheckRestored()); return true; }
            if (line.StartsWith("hoverinfo ")) { outFn(Orivilon.UI.Refresh.EditorTools.PolishQA.Info(line.Substring(10).Trim())); return true; }
            if (line.StartsWith("invshots ")) { var a = line.Substring(9).Trim().Split(' '); outFn(Orivilon.UI.Refresh.EditorTools.PolishQA.ToggleShots(Dir, a[0], a[1].Split(','))); return true; }
            if (line.StartsWith("timescale ")) { UnityEngine.Time.timeScale = float.Parse(line.Substring(10).Trim(), System.Globalization.CultureInfo.InvariantCulture); outFn("timescale " + UnityEngine.Time.timeScale); return true; }
            if (line == "loadprobe") { outFn(Orivilon.UI.Refresh.EditorTools.PolishQA.LoadProbe(Dir)); return true; }
            if (line.StartsWith("clean ")) { outFn(Orivilon.UI.Refresh.EditorTools.PolishQA.Clean(line.Substring(6).Trim())); return true; }
            if (line == "qareopen") { outFn(Orivilon.UI.Refresh.EditorTools.QaSaveGuard.Reopen()); return true; }
            if (line.StartsWith("settingsui ")) { outFn(Orivilon.UI.Refresh.EditorTools.SettingsAuthoring.Run(line.Substring(11).Trim())); return true; }
            if (line.StartsWith("settings ")) { outFn(Orivilon.UI.Refresh.EditorTools.SettingsQA.Settings(line.Substring(9).Trim())); return true; }
            if (line.StartsWith("settingsbak ")) { outFn(Orivilon.UI.Refresh.EditorTools.SettingsQA.Backup(line.Substring(12).Trim(), false)); return true; }
            if (line.StartsWith("settingsrestore ")) { outFn(Orivilon.UI.Refresh.EditorTools.SettingsQA.Backup(line.Substring(16).Trim(), true)); return true; }
            if (line == "settingsfile") { outFn(Orivilon.UI.Refresh.EditorTools.SettingsQA.FileText()); return true; }
            if (line.StartsWith("menufix ")) { outFn(Orivilon.UI.Refresh.EditorTools.SteamMacFix.Run(line.Substring(8).Trim())); return true; }
            if (line.StartsWith("compassui ")) { outFn(Orivilon.UI.Refresh.EditorTools.CompassAuthoring.Run(line.Substring(10).Trim())); return true; }
            if (line.StartsWith("mpqa ")) { outFn(Orivilon.UI.Refresh.EditorTools.MpLoadingQA.Run(line.Substring(5).Trim())); return true; }
            if (line.StartsWith("qaworlds ")) { outFn(Orivilon.UI.Refresh.EditorTools.SettingsQA.QaWorlds(line.Substring(9).Trim())); return true; }
            if (line.StartsWith("occupyport ")) { outFn(Orivilon.UI.Refresh.EditorTools.SettingsQA.OccupyPort(line.EndsWith("on"))); return true; }
            if (line.StartsWith("mode ")) { outFn(Orivilon.UI.Refresh.EditorTools.SettingsQA.Mode(line.Substring(5).Trim())); return true; }
            if (line.StartsWith("mpfail ")) { outFn(Orivilon.UI.Refresh.EditorTools.SettingsQA.MpFail(line.EndsWith("on"))); return true; }
            if (line.StartsWith("indexclean ")) { outFn(Orivilon.UI.Refresh.EditorTools.SettingsQA.IndexClean(line.Substring(11).Trim())); return true; }
            if (line == "tabs") { outFn(Orivilon.UI.Refresh.EditorTools.SettingsQA.Tabs()); return true; }
            if (line == "netstate") { outFn(Orivilon.UI.Refresh.EditorTools.SettingsQA.NetState()); return true; }
            if (line == "hoverstate") { outFn(Orivilon.UI.Refresh.EditorTools.PolishQA.State()); return true; }
            if (line.StartsWith("dragtest ")) { outFn(Orivilon.UI.Refresh.EditorTools.PolishQA.Drag(line.Substring(9).Trim())); return true; }
            if (line.StartsWith("cursorat ")) { var a = line.Substring(9).Trim().Split(' '); outFn(Orivilon.UI.Refresh.EditorTools.PolishQA.CursorAt(float.Parse(a[0], CultureInfo.InvariantCulture), float.Parse(a[1], CultureInfo.InvariantCulture))); return true; }
            if (line == "cursorstate") { outFn(Orivilon.UI.Refresh.EditorTools.EditPreview.CursorState()); return true; }
            if (line == "previewclose") { outFn(Orivilon.UI.Refresh.EditorTools.EditPreview.Close()); return true; }
            if (line == "previewrevert") { outFn(Orivilon.UI.Refresh.EditorTools.EditPreview.Revert()); return true; }
            if (line.StartsWith("previewstate ")) { var a = line.Substring(13).Trim().Split(' '); outFn(a.Length == 2 ? Orivilon.UI.Refresh.EditorTools.EditPreview.State(a[0], a[1]) : "ERR previewstate Frame State"); return true; }
            if (line.StartsWith("savesnap ")) { outFn(Orivilon.UI.Refresh.EditorTools.QaSaveGuard.Snap(line.Substring(9).Trim())); return true; }
            if (line.StartsWith("savecheck ")) { outFn(Orivilon.UI.Refresh.EditorTools.QaSaveGuard.Check(line.Substring(10).Trim(), false)); return true; }
            if (line.StartsWith("saverestore ")) { outFn(Orivilon.UI.Refresh.EditorTools.QaSaveGuard.Check(line.Substring(12).Trim(), true)); return true; }
            if (line.StartsWith("qalaunch "))
            {
                var p = line.Substring(9).Trim(); int sp = p.LastIndexOf(' ');
                outFn(sp > 0 ? Orivilon.UI.Refresh.EditorTools.QaSaveGuard.Launch(p.Substring(0, sp), p.Substring(sp + 1)) : "ERR qalaunch name seed");
                return true;
            }
            if (line == "invtoggle")
            {
                var gm = Orivilon.Core.GameManager.instance;
                var mi = typeof(Orivilon.Core.GameManager).GetMethod("ToggleInventory", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
                if (gm == null || mi == null) { outFn("ERR invtoggle"); return true; }
                mi.Invoke(gm, null);
                outFn("invtoggle");
                return true;
            }
            if (line.StartsWith("biomemap "))
            {
                var sb = new StringBuilder();
                for (int i = 0; i < Orivilon.World.Generation.BiomeMath.Count; i++)
                {
                    int ci = Orivilon.World.Generation.BiomeCatalog.OfRegion(i);
                    sb.AppendFormat("{0,2} enum {1,-18} id {2,-22} name {3,-30} catalog {4}\n", i, ((Orivilon.World.Generation.BiomeRegion)i).ToString(),
                        Orivilon.Core.GameConsole.RegionCanonicalId(i), Orivilon.Core.GameConsole.RegionDisplayName(i),
                        ci >= 0 ? Orivilon.World.Generation.BiomeCatalog.All[ci].id : "-");
                }
                File.WriteAllText(Path.Combine(Dir, line.Substring(9).Trim() + ".txt"), sb.ToString());
                outFn("biomemap written");
                return true;
            }
            return false;
        }

        private static void Watch()
        {
            if (!watching || !EditorApplication.isPlaying) return;
            if (Time.frameCount == lastFrame) return;
            lastFrame = Time.frameCount;
            var lm = Orivilon.Core.LoadingScreenManager.instance;
            if (lm == null || lm.loadingPanel == null || !lm.loadingPanel.activeInHierarchy) return;
            watchFrames++;
            bool leak = false;
            foreach (var g in UnityEngine.Object.FindObjectsByType<Graphic>(FindObjectsSortMode.None))
            {
                if (!g.isActiveAndEnabled || g.gameObject.scene.name == "LoadingScreen") continue;
                if (g.canvas == null || !g.canvas.isActiveAndEnabled) continue;
                if (g.color.a <= 0.01f || g.canvasRenderer.GetInheritedAlpha() <= 0.01f) continue;
                if (g is Text t0 && string.IsNullOrEmpty(t0.text)) continue;
                if (g is TMPro.TMP_Text t1 && string.IsNullOrEmpty(t1.text)) continue;
                leak = true;
                if (leakNames.Count < 12) leakNames.Add(PathOf(g.transform) + "@" + g.canvas.rootCanvas.sortingOrder);
            }
            if (leak) leakFrames++;
        }

        private static string PathOf(Transform t)
        {
            string s = t.name;
            while (t.parent != null) { t = t.parent; s = t.name + "/" + s; }
            return s;
        }

        private static string Dump(string root)
        {
            var sb = new StringBuilder();
            sb.AppendFormat(CultureInfo.InvariantCulture, "screen {0}x{1}\n", Screen.width, Screen.height);
            foreach (var c in UnityEngine.Object.FindObjectsByType<Canvas>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (!c.isRootCanvas && c.transform.parent != null && c.transform.parent.GetComponentInParent<Canvas>(true) != null) continue;
                if (root != null && c.name != root) continue;
                var sc = c.GetComponent<CanvasScaler>();
                sb.AppendFormat(CultureInfo.InvariantCulture, "== {0} scene {1} mode {2} order {3} scale {4:0.###} {5}\n",
                    c.name, c.gameObject.scene.name, c.renderMode, c.sortingOrder, c.scaleFactor,
                    sc != null ? sc.uiScaleMode + " ref " + sc.referenceResolution + " match " + sc.matchWidthOrHeight : "");
                Walk(c.transform, 0, sb);
            }
            return sb.ToString();
        }

        private static readonly Vector3[] corners = new Vector3[4];

        private static void Walk(Transform t, int d, StringBuilder sb)
        {
            var rt = t as RectTransform;
            sb.Append(' ', d * 2);
            if (!t.gameObject.activeSelf) sb.Append("[off] ");
            sb.Append(t.name);
            if (rt != null)
            {
                rt.GetWorldCorners(corners);
                sb.AppendFormat(CultureInfo.InvariantCulture, " scr({0:0},{1:0} {2:0}x{3:0}) a({4:0.##},{5:0.##}-{6:0.##},{7:0.##}) pv({8:0.##},{9:0.##}) pos({10:0.#},{11:0.#}) sz({12:0.#},{13:0.#})",
                    corners[0].x, corners[0].y, corners[2].x - corners[0].x, corners[2].y - corners[0].y,
                    rt.anchorMin.x, rt.anchorMin.y, rt.anchorMax.x, rt.anchorMax.y, rt.pivot.x, rt.pivot.y,
                    rt.anchoredPosition.x, rt.anchoredPosition.y, rt.sizeDelta.x, rt.sizeDelta.y);
                if (rt.localScale != Vector3.one) sb.AppendFormat(CultureInfo.InvariantCulture, " scale({0:0.##},{1:0.##})", rt.localScale.x, rt.localScale.y);
            }
            foreach (var comp in t.GetComponents<Component>())
            {
                if (comp == null || comp is Transform || comp is CanvasRenderer) continue;
                sb.Append(" |").Append(comp.GetType().Name);
                if (comp is Graphic gr && gr.canvasRenderer != null && gr.canvasRenderer.cull) sb.Append("[CULLED]");
                if (comp is Image im)
                    sb.AppendFormat(CultureInfo.InvariantCulture, "[{0} {1} #{2} ppu{3:0.#}{4}]", im.sprite != null ? im.sprite.name : "-", im.type,
                        ColorUtility.ToHtmlStringRGBA(im.color), im.pixelsPerUnitMultiplier, im.enabled ? "" : " disabled");
                else if (comp is TMPro.TMP_Text tx)
                {
                    sb.AppendFormat(CultureInfo.InvariantCulture, "[\"{0}\" {1} {2:0.#}pt #{3}]", Short(tx.text), tx.font != null ? tx.font.name : "-", tx.fontSize, ColorUtility.ToHtmlStringRGBA(tx.color));
                    sb.AppendFormat(CultureInfo.InvariantCulture, "[m {0} chars {1} en {2} ov {3} a {4:0.##}]", tx.margin, tx.textInfo != null ? tx.textInfo.characterCount : -1, tx.enabled, tx.overflowMode, tx.canvasRenderer != null ? tx.canvasRenderer.GetInheritedAlpha() : -1f);
                }
                else if (comp is Canvas cv && t.parent != null)
                    sb.AppendFormat("[override {0} order {1}]", cv.overrideSorting, cv.sortingOrder);
                else if (comp is CanvasGroup cg)
                    sb.AppendFormat(CultureInfo.InvariantCulture, "[a {0:0.##}]", cg.alpha);
                else if (comp is Animator an)
                    sb.AppendFormat("[{0}]", an.runtimeAnimatorController != null ? an.runtimeAnimatorController.name : "-");
            }
            sb.Append('\n');
            bool slotRun = false;
            for (int i = 0; i < t.childCount; i++)
            {
                var ch = t.GetChild(i);
                bool isSlot = ch.name.StartsWith("Slot") || ch.name.StartsWith("InventorySlot") || ch.name.StartsWith("HotbarSlot (");
                if (isSlot && slotRun && i < t.childCount - 1) continue;   // ze sérií slotů jen první a poslední
                Walk(ch, d + 1, sb);
                slotRun = isSlot;
            }
        }

        private static string Short(string s)
        {
            if (s == null) return "";
            s = s.Replace("\n", "\\n");
            return s.Length > 40 ? s.Substring(0, 40) + "…" : s;
        }
    }
}
