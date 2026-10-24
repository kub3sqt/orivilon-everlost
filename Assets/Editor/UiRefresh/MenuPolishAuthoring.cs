using System.Collections.Generic;
using System.Text;
using Orivilon.UI.Menu;
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
    /// Menu polish 23.10.2026 (bridge "menupolish &lt;step&gt;"). Uložené scény/prefaby přes Editor API; kroky jsou
    /// idempotentní (podruhé nic nezmění), každý vypíše, co udělal.
    ///   font     – SpaceMono Bold/Regular SDF: smaže kerningové páry (GlyphPairAdjustmentRecords), které zdrojový
    ///              font vůbec nemá (Space Mono má v GPOS jen mark/mkmk) – kvůli nim se slévalo „sp“, „ss“ atd.
    ///   side     – MainMenu: výběžek vpravo – křížek nahoře, ikony režimu pod ním, CanvasGroup na ikonách, stav
    ///              rámu WorldsCompact (jen křížek) v MainMenuFrame.prefab, propojení Create formuláře.
    ///   settings – SettingsFrame.prefab: stav Closed (nadpis sbalený do loga, panel sbalený); SettingsScreen.prefab:
    ///              CanvasGroup na obsahu a reference pro morph.
    ///   dialog   – MainMenu: dialog hesla dostane stejné pozadí jako ostatní panely (FramePanel + stencil reset).
    ///   verify   – vypíše stav všeho výše.
    /// </summary>
    public static class MenuPolishAuthoring
    {
        private const string FontBold = "Assets/Resources/Fonts/SpaceMono-Bold SDF.asset";
        private const string FontRegular = "Assets/Resources/Fonts/SpaceMono-Regular SDF.asset";
        private const string MainFramePath = PrefabDir + "/Frames/MainMenuFrame.prefab";
        private const string SettingsFramePath = PrefabDir + "/Settings/SettingsFrame.prefab";
        private const string SettingsScreenPath = PrefabDir + "/Settings/SettingsScreen.prefab";

        public static string Run(string step)
        {
            try
            {
                switch (step)
                {
                    case "font": return Font();
                    case "side": return Side();
                    case "settings": return Settings();
                    case "sidefit": return SideFit();
                    case "dialog": return Dialog();
                    case "verify": return Verify();
                    case "framedump": return FrameDump(SettingsFramePath);
                    case "settingsfit": return SettingsFit();
                    case "joinorder": return JoinOrder();
                    case "framedumpmm": return FrameDump(PrefabDir + "/Frames/MainMenuFrame.prefab");
                    default:
                        if (step.StartsWith("qa ")) return Qa(step.Substring(3).Trim().Split(' '));
                        break;
                }
                return "ERR menupolish: unknown step " + step;
            }
            catch (System.Exception e) { return "ERR menupolish " + step + ": " + e.GetType().Name + " " + e.Message; }
        }

        private static Scene NeedScene(string name)
        {
            var s = SceneManager.GetActiveScene();
            if (s.name != name) throw new System.Exception("open the " + name + " scene first (current: " + s.name + ")");
            return s;
        }

        // ── 1. font ──────────────────────────────────────────────────────────────────────────────────

        private static string Font()
        {
            var sb = new StringBuilder("menupolish font:");
            foreach (var path in new[] { FontBold, FontRegular })
            {
                var fa = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(path);
                if (fa == null) { sb.Append(" missing ").Append(path); continue; }
                var recs = fa.fontFeatureTable.glyphPairAdjustmentRecords;
                int before = recs != null ? recs.Count : 0;
                if (before > 0)
                {
                    recs.Clear();
                    fa.ReadFontAssetDefinition();
                    EditorUtility.SetDirty(fa);
                    AssetDatabase.SaveAssetIfDirty(fa);
                }
                sb.Append(' ').Append(fa.name).Append(" pairs ").Append(before).Append("→").Append(fa.fontFeatureTable.glyphPairAdjustmentRecords.Count);
            }
            return sb.ToString();
        }

        // ── 2. side tab + create form ────────────────────────────────────────────────────────────────

        private static string Side()
        {
            var scene = NeedScene("MainMenu");
            var sb = new StringBuilder("menupolish side:");
            var ui = Object.FindFirstObjectByType<MainMenuUI>(FindObjectsInactive.Include);
            var mmc = Object.FindFirstObjectByType<MainMenuController>(FindObjectsInactive.Include);
            if (ui == null || mmc == null) return "ERR menupolish side: MainMenuUI / MainMenuController missing";
            var side = ui.transform.Find("SideGuide");
            var back = side.Find("BackButton") as RectTransform;
            var sp = side.Find("Singleplayer") as RectTransform;
            var mp = side.Find("Multiplayer") as RectTransform;
            if (back == null || sp == null || mp == null) return "ERR menupolish side: side buttons missing";

            // křížek nahoře (20 px od horního okraje výběžku -66), ikony režimu pod ním s mezerou 30 px
            back.anchoredPosition = new Vector2(back.anchoredPosition.x, -144f);
            sp.anchoredPosition = new Vector2(sp.anchoredPosition.x, -244f);
            mp.anchoredPosition = new Vector2(mp.anchoredPosition.x, -344f);
            var groups = new List<CanvasGroup>();
            foreach (var b in new[] { sp, mp })
            {
                var g = b.GetComponent<CanvasGroup>();
                if (g == null) g = Undo.AddComponent<CanvasGroup>(b.gameObject);
                g.alpha = 1f; g.blocksRaycasts = true; g.interactable = true;   // uložený stav = vidět v Editoru
                EditorUtility.SetDirty(b);
                groups.Add(g);
            }
            var so = new SerializedObject(mmc);
            var arr = so.FindProperty("sideModeGroups");
            arr.arraySize = groups.Count;
            for (int i = 0; i < groups.Count; i++) arr.GetArrayElementAtIndex(i).objectReferenceValue = groups[i];
            so.ApplyModifiedProperties();
            sb.Append(" buttons X/SP/MP y -144/-244/-344, groups ").Append(groups.Count);

            // Create New World: režim + chyba
            var creator = Object.FindFirstObjectByType<CreateWorldMenu>(FindObjectsInactive.Include);
            var createMenu = ui.createMenu != null ? ui.createMenu.transform : null;
            var createBtn = createMenu != null ? createMenu.Find("CreateButton") as RectTransform : null;
            if (creator != null && createBtn != null)
            {
                var label = createBtn.Find("Label") as RectTransform;
                var sub = createBtn.Find("ModeSubtitle") as RectTransform;
                var playSub = mmc.playButton != null ? mmc.playButton.transform.Find("ModeSubtitle") as RectTransform : null;
                var playText = mmc.playLabel != null ? mmc.playLabel.rectTransform : null;
                if (sub == null && playSub != null)
                {
                    sub = (RectTransform)Object.Instantiate(playSub.gameObject, createBtn, false).transform;
                    sub.name = "ModeSubtitle";
                    Undo.RegisterCreatedObjectUndo(sub.gameObject, "mode subtitle");
                }
                if (label != null && playText != null)
                {
                    // stejné rozložení jako PLAY: hlavní popisek výš, pod ním režim
                    label.anchorMin = playText.anchorMin; label.anchorMax = playText.anchorMax; label.pivot = playText.pivot;
                    label.anchoredPosition = playText.anchoredPosition; label.sizeDelta = playText.sizeDelta;
                    EditorUtility.SetDirty(label);
                }
                var err = createMenu.Find("CreateError") as RectTransform;
                if (err == null)
                {
                    var t = NewText("CreateError", createMenu, "", false, 18f, new Color32(0xff, 0x8a, 0x80, 0xff), TextAlignmentOptions.BottomLeft, false);
                    t.textWrappingMode = TextWrappingModes.Normal;
                    err = t.rectTransform;
                    err.anchorMin = err.anchorMax = new Vector2(0f, 1f); err.pivot = new Vector2(0f, 0f);
                    err.sizeDelta = new Vector2(900f, 56f);
                    err.anchoredPosition = new Vector2(createBtn.anchoredPosition.x, createBtn.anchoredPosition.y + createBtn.sizeDelta.y + 10f);
                    err.gameObject.SetActive(false);
                    Undo.RegisterCreatedObjectUndo(err.gameObject, "create error");
                }
                var cso = new SerializedObject(creator);
                cso.FindProperty("menu").objectReferenceValue = ui;
                cso.FindProperty("createButton").objectReferenceValue = createBtn.GetComponent<Button>();
                cso.FindProperty("modeSubtitle").objectReferenceValue = sub != null ? sub.GetComponent<TMP_Text>() : null;
                cso.FindProperty("errorText").objectReferenceValue = err.GetComponent<TMP_Text>();
                cso.ApplyModifiedProperties();
                var uso = new SerializedObject(ui);
                uso.FindProperty("creator").objectReferenceValue = creator;
                uso.ApplyModifiedProperties();
                sb.Append(", create form wired (subtitle ").Append(sub != null).Append(", error ").Append(err != null).Append(')');
            }
            else sb.Append(", create form NOT found");

            EditorSceneManager.MarkSceneDirty(scene);
            if (!EditorSceneManager.SaveScene(scene)) return "ERR menupolish side: saving MainMenu failed";

            // stav rámu WorldsCompact
            var root = PrefabUtility.LoadPrefabContents(MainFramePath);
            try
            {
                var frame = root.GetComponent<UiFrame>();
                var worlds = frame.Find(MainMenuController.WorldsState);
                var compact = frame.Find(MainMenuController.WorldsCompactState);
                if (compact == null)
                {
                    compact = new UiFrame.State { name = MainMenuController.WorldsCompactState };
                    frame.states.Add(compact);
                }
                compact.pieces.Clear();
                int tab = 0;
                foreach (var p in worlds.pieces)
                {
                    var c = new UiFrame.PieceState
                    {
                        piece = p.piece, anchorMin = p.anchorMin, anchorMax = p.anchorMax,
                        offsetMin = p.offsetMin, offsetMax = p.offsetMax, fit = p.fit, fitTo = p.fitTo, fitPad = p.fitPad
                    };
                    if (p.piece != null && p.piece.name == "Panel_panelTab")
                    {
                        // jen křížek: 20 px nad ním, 20 px pod ním (křížek 58 px, horní okraj výběžku -66)
                        c.offsetMin = new Vector2(c.offsetMin.x, -164f);
                        tab++;
                    }
                    compact.pieces.Add(c);
                }
                PrefabUtility.SaveAsPrefabAsset(root, MainFramePath);
                sb.Append(", frame state WorldsCompact (tab pieces ").Append(tab).Append(')');
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
            return sb.ToString();
        }

        /// <summary>Scene instance of MainMenuFrame: fit targets (scene objects, e.g. the heading text) of the new
        /// WorldsCompact state copied from Worlds – the prefab cannot reference them.</summary>
        private static string SideFit()
        {
            var scene = NeedScene("MainMenu");
            var mmc = Object.FindFirstObjectByType<MainMenuController>(FindObjectsInactive.Include);
            var frame = mmc != null ? mmc.frame : null;
            if (frame == null) return "ERR menupolish sidefit: frame missing";
            var w = frame.Find(MainMenuController.WorldsState); var c = frame.Find(MainMenuController.WorldsCompactState);
            if (w == null || c == null) return "ERR menupolish sidefit: states missing";
            int n = 0;
            foreach (var cp in c.pieces)
                foreach (var wp in w.pieces)
                    if (wp.piece == cp.piece && (cp.fitTo != wp.fitTo || cp.fit != wp.fit || cp.fitPad != wp.fitPad))
                    { cp.fitTo = wp.fitTo; cp.fit = wp.fit; cp.fitPad = wp.fitPad; n++; }
            EditorUtility.SetDirty(frame);
            PrefabUtility.RecordPrefabInstancePropertyModifications(frame);
            EditorSceneManager.MarkSceneDirty(scene);
            if (!EditorSceneManager.SaveScene(scene)) return "ERR menupolish sidefit: save failed";
            return "menupolish sidefit: copied fit for " + n + " pieces";
        }

        // ── 3. settings morph ────────────────────────────────────────────────────────────────────────

        /// <summary>Výpis stavů UiFrame prefabu (kus, kotvy, offsety, fit) a joinů – pro ladění morphů.</summary>
        private static string FrameDump(string path)
        {
            var sb = new StringBuilder("framedump " + path + ":");
            var root = PrefabUtility.LoadPrefabContents(path);
            try
            {
                var frame = root.GetComponent<UiFrame>();
                foreach (var st in frame.states)
                {
                    sb.Append("\n[").Append(st.name).Append("]");
                    foreach (var p in st.pieces)
                        sb.Append("\n  ").Append(p.piece != null ? p.piece.name : "null").Append(" a").Append(p.anchorMin).Append(p.anchorMax)
                          .Append(" o").Append(p.offsetMin).Append(p.offsetMax).Append(p.fit != UiFrame.FitSide.None ? " fit " + p.fit + "→" + (p.fitTo != null ? p.fitTo.name : "-") : "");
                }
                sb.Append("\n[hierarchy]");
                foreach (var t in root.GetComponentsInChildren<Transform>(true))
                {
                    var comps = new StringBuilder();
                    foreach (var c in t.GetComponents<MonoBehaviour>()) if (c != null) comps.Append(c.GetType().Name).Append(',');
                    sb.Append("\n  ").Append(t.parent == root.transform ? "" : "  ").Append(t.name).Append(" {").Append(comps).Append('}');
                }
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
            System.IO.File.WriteAllText(System.IO.Path.Combine(System.IO.Directory.GetParent(Application.dataPath).FullName, "_reports", "remote", "framedump.txt"), sb.ToString());
            return "framedump → _reports/remote/framedump.txt";
        }

        /// <summary>
        /// Stav Closed vznikl v SettingsFrame.prefab jako kopie Default, ale fit záložek BETA / Orivilon (a případně dalších
        /// kusů) se nastavuje až v SettingsScreen.prefab a ve scénových instancích – v Closed chyběl, takže morph
        /// posouval spodní záložky mezi fitnutou a prefabovou šířkou. Zkopíruje fit/fitTo/fitPad a geometrii z Default do
        /// Closed pro každý kus, který se zavřením nemění (vše kromě nadpisu, můstku a panelu), na všech úrovních.
        /// </summary>
        private static string SettingsFit()
        {
            if (Application.isPlaying) return "ERR menupolish settingsfit: stop Play Mode first";
            var sb = new StringBuilder("menupolish settingsfit:");
            var sroot = PrefabUtility.LoadPrefabContents(SettingsScreenPath);
            try
            {
                foreach (var f in sroot.GetComponentsInChildren<UiFrame>(true)) sb.Append(" prefab ").Append(SyncClosed(f));
                PrefabUtility.SaveAsPrefabAsset(sroot, SettingsScreenPath);
            }
            finally { PrefabUtility.UnloadPrefabContents(sroot); }

            string back = SceneManager.GetActiveScene().path;
            foreach (var path in new[] { "Assets/Scenes/MainMenu.unity", "Assets/Scenes/Game.unity" })
            {
                var scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
                int n = 0;
                foreach (var f in Object.FindObjectsByType<UiFrame>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                {
                    if (f.Find("Closed") == null) continue;
                    sb.Append(' ').Append(scene.name).Append(' ').Append(SyncClosed(f));
                    if (PrefabUtility.IsPartOfPrefabInstance(f)) PrefabUtility.RecordPrefabInstancePropertyModifications(f);
                    EditorUtility.SetDirty(f); n++;
                }
                if (n > 0) { EditorSceneManager.MarkSceneDirty(scene); EditorSceneManager.SaveScene(scene); }
            }
            if (!string.IsNullOrEmpty(back)) EditorSceneManager.OpenScene(back, OpenSceneMode.Single);
            return sb.ToString();
        }

        private static string SyncClosed(UiFrame f)
        {
            var def = f.Find("Default"); var closed = f.Find("Closed");
            if (def == null || closed == null) return f.name + "(no Closed)";
            int copied = 0;
            foreach (var c in closed.pieces)
            {
                if (c.piece == null) continue;
                string n = c.piece.name;
                if (n == "Panel_heading" || n == "Panel_headingBridge" || n == "Panel_panel") continue;
                var d = def.pieces.Find(p => p.piece == c.piece);
                if (d == null) continue;
                c.anchorMin = d.anchorMin; c.anchorMax = d.anchorMax; c.offsetMin = d.offsetMin; c.offsetMax = d.offsetMax;
                c.fit = d.fit; c.fitTo = d.fitTo; c.fitPad = d.fitPad;
                if (d.fit != UiFrame.FitSide.None) copied++;
            }
            return f.name + "(fitted " + copied + ")";
        }

        /// <summary>
        /// Rámy kreslí Edge kusů a joinů se stencilem „kdo dřív, ten kreslí". Join (vnitřní zaoblený roh) musí být v
        /// pořadí PŘED kusy, které spojuje – jinak rovný okraj kusu (např. záložky BETA / Orivilon v SettingsFrame,
        /// kam byly přidány před kontejner Joins) zablokuje oblouk joinu a v rohu vznikne schod / zdvojený okraj.
        /// Přesune kontejner Joins před první kus (hned za StencilReset) ve všech UiFrame prefabech.
        /// </summary>
        private static string JoinOrder()
        {
            if (Application.isPlaying) return "ERR menupolish joinorder: stop Play Mode first";
            var sb = new StringBuilder("menupolish joinorder:");
            foreach (var name in new[] { "Settings/SettingsFrame", "Frames/MainMenuFrame", "Frames/PauseFrame", "Frames/HudFrame", "Frames/LoadingFrame" })
            {
                string path = PrefabDir + "/" + name + ".prefab";
                var root = PrefabUtility.LoadPrefabContents(path);
                try
                {
                    var joins = root.transform.Find("Joins");
                    if (joins == null) { sb.Append(' ').Append(name).Append("(no Joins)"); continue; }
                    int first = -1;
                    for (int i = 0; i < root.transform.childCount; i++)
                    {
                        var c = root.transform.GetChild(i);
                        if (c == joins) continue;
                        if (c.GetComponentInChildren<FramePiece>(true) != null) { first = i; break; }
                    }
                    int before = joins.GetSiblingIndex();
                    if (first >= 0 && before > first)
                    {
                        joins.SetSiblingIndex(first);
                        PrefabUtility.SaveAsPrefabAsset(root, path);
                        sb.Append(' ').Append(name).Append(" Joins ").Append(before).Append("→").Append(joins.GetSiblingIndex());
                    }
                    else sb.Append(' ').Append(name).Append(" ok(").Append(before).Append(')');
                }
                finally { PrefabUtility.UnloadPrefabContents(root); }
            }
            return sb.ToString();
        }

        private static string Settings()
        {
            var sb = new StringBuilder("menupolish settings:");
            var root = PrefabUtility.LoadPrefabContents(SettingsFramePath);
            try
            {
                var frame = root.GetComponent<UiFrame>();
                var def = frame.Find("Default");
                var closed = frame.Find("Closed");
                if (closed == null) { closed = new UiFrame.State { name = "Closed" }; frame.states.Add(closed); }
                closed.pieces.Clear();
                foreach (var p in def.pieces)
                {
                    var c = new UiFrame.PieceState
                    {
                        piece = p.piece, anchorMin = p.anchorMin, anchorMax = p.anchorMax,
                        offsetMin = p.offsetMin, offsetMax = p.offsetMax, fit = p.fit, fitTo = p.fitTo, fitPad = p.fitPad
                    };
                    string n = p.piece != null ? p.piece.name : "";
                    // stejná geometrie jako záložka loga v hlavním menu / pauze (MainMenuFrame stav Menu)
                    if (n == "Panel_heading") { c.offsetMin = new Vector2(-60f, -104f); c.offsetMax = new Vector2(104f, 60f); }
                    else if (n == "Panel_headingBridge") { c.offsetMax = new Vector2(c.offsetMin.x, c.offsetMax.y); }
                    else if (n == "Panel_panel") { c.anchorMax = new Vector2(c.anchorMin.x, c.anchorMax.y); c.offsetMax = new Vector2(c.offsetMin.x, c.offsetMax.y); }
                    closed.pieces.Add(c);
                }
                PrefabUtility.SaveAsPrefabAsset(root, SettingsFramePath);
                sb.Append(" frame Closed (").Append(closed.pieces.Count).Append(" pieces)");
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }

            var sroot = PrefabUtility.LoadPrefabContents(SettingsScreenPath);
            try
            {
                var screen = sroot.GetComponent<SettingsScreen>();
                var groups = new List<CanvasGroup>();
                foreach (var n in new[] { "Heading", "Body" })
                {
                    var t = sroot.transform.Find(n);
                    if (t == null) continue;
                    var g = t.GetComponent<CanvasGroup>();
                    if (g == null) g = t.gameObject.AddComponent<CanvasGroup>();
                    g.alpha = 1f;
                    groups.Add(g);
                }
                screen.frame = sroot.transform.Find("SettingsFrame").GetComponent<UiFrame>();
                screen.contentGroups = groups.ToArray();
                PrefabUtility.SaveAsPrefabAsset(sroot, SettingsScreenPath);
                sb.Append(", screen content groups ").Append(groups.Count);
            }
            finally { PrefabUtility.UnloadPrefabContents(sroot); }
            return sb.ToString();
        }

        // ── 4. password dialog background ────────────────────────────────────────────────────────────

        private static string Dialog()
        {
            var scene = NeedScene("MainMenu");
            var dlg = Object.FindFirstObjectByType<MultiplayerDialog>(FindObjectsInactive.Include);
            var box = dlg != null ? FindDeep(dlg.transform, "Box") as RectTransform : null;
            if (box == null) return "ERR menupolish dialog: MultiplayerDialog/Root/Box not found";
            var style = FrameAuthoring.EnsureStyle();
            var panelPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(FrameAuthoring.PanelPrefabPath);
            if (box.Find("Panel_dialog") == null)
            {
                var reset = NewImage("StencilReset", box, null, Color.white, false);
                reset.material = style.resetMaterial;
                Stretch(reset.rectTransform, -(style.EdgeExtent + 2f));
                reset.transform.SetSiblingIndex(0);
                var panel = (GameObject)PrefabUtility.InstantiatePrefab(panelPrefab, box);
                panel.name = "Panel_dialog";
                var prt = (RectTransform)panel.transform;
                prt.anchorMin = Vector2.zero; prt.anchorMax = Vector2.one; prt.pivot = new Vector2(0.5f, 0.5f);
                prt.offsetMin = Vector2.zero; prt.offsetMax = Vector2.zero;
                panel.transform.SetSiblingIndex(1);
                foreach (var g in panel.GetComponentsInChildren<Graphic>(true)) g.raycastTarget = false;
                Undo.RegisterCreatedObjectUndo(reset.gameObject, "dialog reset");
                Undo.RegisterCreatedObjectUndo(panel, "dialog panel");
            }
            // původní Image boxu zůstane jako průhledný cíl raycastu (kliky mimo pole nepropadnou do menu)
            var img = box.GetComponent<Image>();
            if (img != null) { img.color = new Color(1f, 1f, 1f, 0f); EditorUtility.SetDirty(img); }
            EditorSceneManager.MarkSceneDirty(scene);
            if (!EditorSceneManager.SaveScene(scene)) return "ERR menupolish dialog: saving MainMenu failed";
            return "menupolish dialog: Box → FramePanel background (" + box.rect.size + ")";
        }

        // ── QA (Play Mode) ───────────────────────────────────────────────────────────────────────────

        private static string QaRoot => System.IO.Path.Combine(System.IO.Directory.GetParent(Application.dataPath).FullName, "_reports", "ui-refresh", "qa-saves", "worlds");

        /// <summary>Ve hře: "esc" projde stejnou cestou jako klávesa Escape (Settings vlastní Esc → Back, jinak
        /// GameManager.HandleEscapeKey, ale jen když ho SettingsScreen.BlocksInput nepouští dál); "game" vypíše stav.</summary>
        private static string GameQa(string op)
        {
            var gm = Orivilon.Core.GameManager.instance;
            if (gm == null) return "ERR menupolish qa " + op + ": no GameManager";
            string did = "";
            if (op == "esc")
            {
                var scr = Object.FindFirstObjectByType<Orivilon.UI.Refresh.SettingsScreen>(FindObjectsInactive.Exclude);
                if (scr != null && scr.IsShown) { scr.Back(); did = "settings.Back"; }
                else if (Orivilon.UI.Refresh.SettingsScreen.BlocksInput) did = "blocked (settings closing)";
                else { gm.HandleEscapeKey(); did = "HandleEscapeKey"; }
            }
            return "game " + op + (did != "" ? " → " + did : "") + " | paused " + gm.IsPaused + " pauseMenu " + (gm.pauseMenu != null && gm.pauseMenu.activeInHierarchy)
                + " cursor " + Cursor.lockState + "/" + (Cursor.visible ? "visible" : "hidden") + " blocks " + Orivilon.UI.Refresh.SettingsScreen.BlocksInput
                + " ts " + Time.timeScale.ToString("0.##") + " | " + QaState();
        }

        private static string Qa(string[] a)
        {
            if (!Application.isPlaying) return "ERR menupolish qa: not playing";
            var ui = Object.FindFirstObjectByType<MainMenuUI>(FindObjectsInactive.Include);
            var creator = Object.FindFirstObjectByType<CreateWorldMenu>(FindObjectsInactive.Include);
            var dlg = MultiplayerDialog.Instance;
            if (a[0] == "esc" || a[0] == "game") return GameQa(a[0]);
            if (a[0] == "tip" || a[0] == "tipexit")
            {
                // tooltip QA: pošle slotu stejné PointerEnter/Exit jako EventSystem při najetí myší (poloha tooltipu
                // dál vychází ze skutečné Input.mousePosition, takže mimo Game view ukazuje clamp k okraji Canvasu)
                int idx = a.Length > 1 ? int.Parse(a[1]) : 0;
                foreach (var sl in Object.FindObjectsByType<Orivilon.Inventory.Inventory.InventorySlot>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
                {
                    if (sl.slotIndex != idx) continue;
                    var ev = new UnityEngine.EventSystems.PointerEventData(UnityEngine.EventSystems.EventSystem.current);
                    if (a[0] == "tip") UnityEngine.EventSystems.ExecuteEvents.Execute(sl.gameObject, ev, UnityEngine.EventSystems.ExecuteEvents.pointerEnterHandler);
                    else UnityEngine.EventSystems.ExecuteEvents.Execute(sl.gameObject, ev, UnityEngine.EventSystems.ExecuteEvents.pointerExitHandler);
                    return a[0] + " " + idx + " (" + sl.name + " @ " + ((RectTransform)sl.transform).position + ") mouse " + Input.mousePosition;
                }
                return "ERR " + a[0] + ": slot " + idx + " not active";
            }
            switch (a[0])
            {
                case "state": return "menupolish qa state: " + QaState();
                case "mode":
                    if (a[1] == "multi") ui.SelectMultiplayerMode(); else ui.SelectSingleplayerMode();
                    return "mode " + a[1] + " → " + QaState();
                case "mpmenu": ui.OpenMultiplayerMenu(); return "mpmenu → " + QaState();
                case "worlds": ui.OpenSelectWorldMenu(); return "worlds → " + QaState();
                case "createmenu": ui.OpenCreateWorldMenu(); return "createmenu → " + QaState();
                case "back":
                    {
                        var b = ui.transform.Find("SideGuide/BackButton")?.GetComponent<Button>();
                        if (b == null) return "ERR no back";
                        b.onClick.Invoke(); return "back";
                    }
                case "create":
                case "create2":
                    {
                        CreateWorldMenu.QaWorldsRoot = QaRoot;
                        System.IO.Directory.CreateDirectory(QaRoot);
                        creator.nameField.text = a.Length > 1 ? a[1] : "";
                        creator.seedField.text = a.Length > 2 ? a[2] : "";
                        int before = Orivilon.SaveSystem.WorldSaveManager.instance.worlds.Count;
                        creator.CreateWorld();
                        if (a[0] == "create2") creator.CreateWorld();   // dvojklik ve stejném snímku
                        return a[0] + " '" + creator.nameField.text + "' worlds " + before + "→" + Orivilon.SaveSystem.WorldSaveManager.instance.worlds.Count + " → " + QaState();
                    }
                case "dlg":
                    {
                        if (dlg == null || !dlg.IsOpen) return "ERR dialog not open";
                        if (a[1] == "cancel") { dlg.Cancel(); return "dlg cancel → " + QaState(); }
                        var input = dlg.GetComponentInChildren<TMP_InputField>(true);
                        if (input != null) input.text = a.Length > 2 ? a[2] : "";
                        dlg.Confirm();
                        return "dlg confirm → " + QaState();
                    }
                case "pwshow":
                    {
                        // dialog s rozepsaným heslem (render maskování + písma)
                        dlg.Show("Join world", "This world is protected. Enter the password from the host.", "JOIN", a.Length > 1 ? "Wrong password. Try again." : null, true, _ => { }, null);
                        var input = dlg.GetComponentInChildren<TMP_InputField>(true);
                        if (input != null) { input.text = "Žluťoučký kůň"; input.ForceLabelUpdate(); }
                        return "pwshow";
                    }
                case "sample":
                    {
                        sampleUntil = EditorApplication.timeSinceStartup + (a.Length > 1 ? double.Parse(a[1], System.Globalization.CultureInfo.InvariantCulture) : 1.0);
                        sampleStart = EditorApplication.timeSinceStartup;
                        sampleFile = System.IO.Path.Combine(System.IO.Directory.GetParent(Application.dataPath).FullName, "_reports", "remote", "mp23_trace.txt");
                        System.IO.File.AppendAllText(sampleFile, "--- sample " + System.DateTime.Now.ToString("HH:mm:ss") + " " + (a.Length > 2 ? a[2] : "") + "\n");
                        EditorApplication.update -= SampleTick; EditorApplication.update += SampleTick;
                        return "sample on";
                    }
                case "lobby":
                    {
                        var steam = Orivilon.Multiplayer.SteamLobbyManager.Instance;
                        var f = typeof(Orivilon.Multiplayer.SteamLobbyManager).GetField("currentLobby", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
                        object lob = steam != null && f != null ? f.GetValue(steam) : null;
                        if (lob == null) return "lobby: none";
                        var t = lob.GetType();
                        var getData = t.GetMethod("GetData", new[] { typeof(string) });
                        string D(string k) => getData != null ? (string)getData.Invoke(lob, new object[] { k }) : "?";
                        object id = t.GetProperty("Id")?.GetValue(lob);
                        object members = t.GetProperty("MemberCount")?.GetValue(lob);
                        object max = t.GetProperty("MaxMembers")?.GetValue(lob);
                        var nm = Unity.Netcode.NetworkManager.Singleton;
                        return "lobby id " + id + " everlost '" + D("everlost") + "' v '" + D("v") + "' world '" + D("worldName") + "' host '" + D("host") + "' locked '" + D("locked")
                            + "' members " + members + "/" + max + " | nm listening " + (nm != null && nm.IsListening) + " host " + (nm != null && nm.IsHost) + " protocol " + Orivilon.Multiplayer.SteamLobbyManager.ProtocolVersion;
                    }
                case "pwempty":
                    {
                        var input = dlg.GetComponentInChildren<TMP_InputField>(true);
                        if (input != null) input.text = "";
                        return "pwempty";
                    }
            }
            return "ERR menupolish qa " + string.Join(" ", a);
        }

        private static double sampleUntil, sampleStart; private static string sampleFile; private static int lastFrame = -1;

        /// <summary>Každý snímek: výška výběžku, alfa ikon, šířka nadpisu Settings a alfa obsahu (plynulost animací).</summary>
        private static void SampleTick()
        {
            if (!Application.isPlaying || EditorApplication.timeSinceStartup > sampleUntil) { EditorApplication.update -= SampleTick; return; }
            if (Time.frameCount == lastFrame) return;
            lastFrame = Time.frameCount;
            var sb = new StringBuilder();
            sb.Append((EditorApplication.timeSinceStartup - sampleStart).ToString("0.000"));
            var mmc = Object.FindFirstObjectByType<MainMenuController>(FindObjectsInactive.Include);
            if (mmc != null && mmc.frame != null)
            {
                foreach (var t in mmc.frame.GetComponentsInChildren<RectTransform>(true)) if (t.name == "Panel_panelTab") sb.Append(" tab ").Append(t.rect.height.ToString("0.0"));
                if (mmc.sideModeGroups.Length > 0 && mmc.sideModeGroups[0] != null) sb.Append(" icons ").Append(mmc.sideModeGroups[0].alpha.ToString("0.00"));
            }
            foreach (var s in Object.FindObjectsByType<SettingsScreen>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
            {
                if (s.frame == null) continue;
                foreach (var t in s.frame.GetComponentsInChildren<RectTransform>(true))
                    if (t.name == "Panel_heading") sb.Append(" sHead ").Append(t.rect.width.ToString("0.0"));
                    else if (t.name == "Panel_panel") sb.Append(" sPanel ").Append(t.rect.width.ToString("0.0"));
                    else if (t.name == "Panel_beta" || t.name == "Panel_brand") sb.Append(t.name == "Panel_beta" ? " beta " : " brand ").Append(t.rect.width.ToString("0.0")).Append('@').Append(t.position.x.ToString("0.0"));
                sb.Append(" sA ").Append(s.group.alpha.ToString("0.00")).Append(" content ").Append(s.contentGroups.Length > 0 ? s.contentGroups[0].alpha.ToString("0.00") : "-").Append(" open ").Append(s.IsShown);
            }
            sb.Append(" ts ").Append(Time.timeScale.ToString("0.##"));
            System.IO.File.AppendAllText(sampleFile, sb.ToString() + "\n");
        }

        private static string QaState()
        {
            var sb = new StringBuilder();
            var ui = Object.FindFirstObjectByType<MainMenuUI>(FindObjectsInactive.Include);
            var mmc = Object.FindFirstObjectByType<MainMenuController>(FindObjectsInactive.Include);
            if (ui != null)
            {
                var sel = typeof(MainMenuUI).GetField("selectedWorldData", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)?.GetValue(ui) as Orivilon.SaveSystem.WorldData;
                sb.Append("menu ").Append(ui.gameObject.activeInHierarchy).Append(" friends ").Append(ui.FriendsMode).Append(" sel '").Append(sel != null ? sel.worldName + "/" + sel.playMode : "-").Append('\'');
            }
            if (mmc != null && mmc.frame != null)
            {
                sb.Append(" frame ").Append(mmc.frame.Current).Append(mmc.frame.Animating ? "*" : "");
                foreach (var t in mmc.frame.GetComponentsInChildren<RectTransform>(true))
                    if (t.name == "Panel_panelTab") sb.Append(" tab ").Append(t.rect.height.ToString("0"));
                foreach (var g in mmc.sideModeGroups) if (g != null) sb.Append(' ').Append(g.name).Append(" a ").Append(g.alpha.ToString("0.00")).Append(g.blocksRaycasts ? " hit" : " nohit").Append(g.gameObject.activeInHierarchy ? "" : " off");
            }
            var c = Object.FindFirstObjectByType<CreateWorldMenu>(FindObjectsInactive.Include);
            if (c != null) sb.Append(" | create multi ").Append(c.Multiplayer).Append(" busy ").Append(c.Busy).Append(" sub '").Append(c.modeSubtitle != null ? c.modeSubtitle.text : "-").Append("' err '").Append(c.errorText != null && c.errorText.gameObject.activeInHierarchy ? c.errorText.text : "").Append('\'');
            var d = MultiplayerDialog.Instance;
            sb.Append(" | dialog ").Append(d != null && d.IsOpen);
            foreach (var s in Object.FindObjectsByType<SettingsScreen>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                sb.Append(" | settings open ").Append(s.IsShown).Append(" a ").Append(s.group != null ? s.group.alpha.ToString("0.00") : "-")
                  .Append(" frame ").Append(s.frame != null ? s.frame.Current + (s.frame.Animating ? "*" : "") : "-")
                  .Append(" content ").Append(s.contentGroups.Length > 0 && s.contentGroups[0] != null ? s.contentGroups[0].alpha.ToString("0.00") : "-")
                  .Append(" blocks ").Append(SettingsScreen.BlocksInput);
            sb.Append(" | worlds ").Append(Orivilon.SaveSystem.WorldSaveManager.instance != null ? Orivilon.SaveSystem.WorldSaveManager.instance.worlds.Count : -1);
            sb.Append(" | load ").Append(Orivilon.Core.SceneLoader.IsLoading).Append(" stage '").Append(Orivilon.Core.SceneLoader.NetworkStage).Append('\'');
            sb.Append(" | mp ").Append(Orivilon.Core.GameManager.IsMultiplayer).Append(" host ").Append(Orivilon.Core.GameManager.IsHost);
            var nm = Unity.Netcode.NetworkManager.Singleton;
            sb.Append(" nm ").Append(nm != null && nm.IsListening).Append('/').Append(nm != null && nm.IsHost);
            var steam = Orivilon.Multiplayer.SteamLobbyManager.Instance;
            if (steam != null) sb.Append(" lobby ").Append(steam.CurrentLobbyIdString ?? "-");
            sb.Append(" | pending '").Append(Orivilon.Core.SceneLoader.PendingMenuError).Append('\'');
            return sb.ToString();
        }

        // ── verify ───────────────────────────────────────────────────────────────────────────────────

        private static string Verify()
        {
            var sb = new StringBuilder("menupolish verify:");
            foreach (var path in new[] { FontBold, FontRegular })
            {
                var fa = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(path);
                if (fa != null) sb.Append(' ').Append(fa.name).Append(" pairs ").Append(fa.fontFeatureTable.glyphPairAdjustmentRecords.Count);
            }
            var mf = AssetDatabase.LoadAssetAtPath<GameObject>(MainFramePath);
            sb.Append(" | MainMenuFrame compact ").Append(mf != null && mf.GetComponent<UiFrame>().Find(MainMenuController.WorldsCompactState) != null);
            var sf = AssetDatabase.LoadAssetAtPath<GameObject>(SettingsFramePath);
            sb.Append(" | SettingsFrame Closed ").Append(sf != null && sf.GetComponent<UiFrame>().Find("Closed") != null);
            var ss = AssetDatabase.LoadAssetAtPath<GameObject>(SettingsScreenPath);
            var scr = ss != null ? ss.GetComponent<SettingsScreen>() : null;
            sb.Append(" | SettingsScreen frame ").Append(scr != null && scr.frame != null).Append(" content ").Append(scr != null ? scr.contentGroups.Length : -1);
            var scene = SceneManager.GetActiveScene();
            sb.Append(" | scene ").Append(scene.name);
            foreach (var s in Object.FindObjectsByType<SettingsScreen>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                sb.Append(" [SettingsScreen in scene: frame ").Append(s.frame != null).Append(" content ").Append(s.contentGroups.Length).Append(']');
            if (scene.name == "MainMenu")
            {
                var mmc = Object.FindFirstObjectByType<MainMenuController>(FindObjectsInactive.Include);
                sb.Append(" side groups ").Append(mmc != null ? mmc.sideModeGroups.Length : -1);
                var c = Object.FindFirstObjectByType<CreateWorldMenu>(FindObjectsInactive.Include);
                sb.Append(" create menu ").Append(c != null && c.menu != null).Append(" subtitle ").Append(c != null && c.modeSubtitle != null).Append(" error ").Append(c != null && c.errorText != null);
                var dlg = Object.FindFirstObjectByType<MultiplayerDialog>(FindObjectsInactive.Include);
                sb.Append(" dialog panel ").Append(dlg != null && FindDeep(dlg.transform, "Panel_dialog") != null);
            }
            return sb.ToString();
        }
    }
}
