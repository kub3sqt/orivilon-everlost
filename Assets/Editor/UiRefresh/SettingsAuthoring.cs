using System.Collections.Generic;
using Orivilon.UI.Menu;
using TMPro;
using UnityEditor;
using UnityEditor.Events;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using static Orivilon.UI.Refresh.EditorTools.UiAuthoringKit;
using static Orivilon.UI.Refresh.EditorTools.FrameAuthoring;

namespace Orivilon.UI.Refresh.EditorTools
{
    /// <summary>
    /// Settings screen + per-world play-mode selectors (bridge "settingsui &lt;step&gt;"). Edits saved
    /// scenes/prefabs through editor APIs; each step refuses to run twice.
    ///   menu – MainMenu: builds SettingsFrame + SettingsScreen prefabs, places the screen over the main-menu
    ///          canvas, rewires the "Settings" link, adds the play-mode selected markers + mode line.
    ///   game – Game: places the SettingsScreen prefab next to the pause menu and rewires the pause "Settings" tab.
    /// </summary>
    public static class SettingsAuthoring
    {
        public const string FramePath = PrefabDir + "/Settings/SettingsFrame.prefab";
        public const string ScreenPath = PrefabDir + "/Settings/SettingsScreen.prefab";
        private const float OFF = 60f, BAR = 12f;
        private static readonly Vector2[] Probe = { new Vector2(1920f, 1080f), new Vector2(1728f, 1080f), new Vector2(2580f, 1080f), new Vector2(1888f, 1180f) };
        private static readonly Color Dark = new Color32(0x1d, 0x1f, 0x1d, 0xff);

        public static string Run(string step)
        {
            try
            {
                switch (step)
                {
                    case "menu": return Menu();
                    case "game": return Game();
                    case "tabs": return Tabs();
                    case "visual": return Visual();
                    case "bridge": return Bridge();
                    case "subtitle": return SubtitleFix();
                    default: return "ERR settingsui: unknown step " + step;
                }
            }
            catch (System.Exception e) { Debug.LogException(e); return "ERR settingsui " + step + ": " + e.Message; }
        }

        private static Box B(C x0, C y0, C x1, C y1) => new Box(x0, y0, x1, y1);

        private static Spec FrameSpec(float headW)
        {
            var s = new Spec { name = "SettingsFrame", initialState = "Default" };
            s.states.Add("Default");
            s.guides.Add(new Guide { name = "PanelGuide", left = 110f, right = 110f, maxWidth = 1500f, box = B(L(0), L(80f), L(0), R(-104f)) });
            s.All(s.P("frameTop"), B(L(-OFF), R(-BAR), R(OFF), R(OFF)));
            s.All(s.P("frameBottom"), B(L(-OFF), L(-OFF), R(OFF), L(BAR)));
            s.All(s.P("frameLeft"), B(L(-OFF), L(-OFF), L(BAR), R(OFF)));
            s.All(s.P("frameRight"), B(R(-BAR), L(-OFF), R(OFF), R(OFF)));
            s.All(s.P("heading"), B(L(-OFF), R(-104f), L(56f + headW), R(OFF)));
            s.All(s.P("headingBridge"), B(L(110f), R(-122f), L(56f + headW), R(-86f)));
            s.All(s.P("panel", "PanelGuide"), B(L(0f), L(0f), R(0f), R(0f)));
            return s;
        }

        private static Sprite S(string name) => AssetDatabase.LoadAssetAtPath<Sprite>(SpriteDir + "/" + name + ".png");

        // ── small builders ───────────────────────────────────────────────────

        private static RectTransform Rect(string name, Transform parent, Vector2 aMin, Vector2 aMax, Vector2 oMin, Vector2 oMax)
        {
            var rt = NewRect(name, parent);
            rt.anchorMin = aMin; rt.anchorMax = aMax; rt.offsetMin = oMin; rt.offsetMax = oMax; rt.pivot = new Vector2(0.5f, 0.5f);
            return rt;
        }

        private static TextMeshProUGUI Txt(string name, Transform parent, string text, float size, Color color, TextAlignmentOptions align, bool bold = true)
        {
            var t = NewText(name, parent, text, false, size, color, align, bold);
            SetMono(t, bold, size);
            t.color = color; t.alignment = align; t.raycastTarget = false;
            t.textWrappingMode = TextWrappingModes.NoWrap; t.overflowMode = TextOverflowModes.Ellipsis;
            return t;
        }

        private static Button Btn(string name, Transform parent, string text, bool filled, float size = 24f)
        {
            var img = NewImage(name, parent, filled ? S("ButtonLight") : null, filled ? Color.white : new Color(1f, 1f, 1f, 0f), filled, true);
            var b = img.gameObject.AddComponent<Button>();
            b.targetGraphic = img; b.transition = Selectable.Transition.ColorTint;
            var cb = b.colors; cb.disabledColor = new Color(1f, 1f, 1f, 0.35f); b.colors = cb;
            var t = Txt("Label", img.transform, text, size, filled ? Dark : Ink, TextAlignmentOptions.Center);
            Stretch(t.rectTransform);
            return b;
        }

        private static void Listen(Button b, UnityAction call)
        {
            UnityEventTools.AddPersistentListener(b.onClick, call);
            EditorUtility.SetDirty(b);
        }

        private static void Section(Transform content, string title)
        {
            var t = NewText("Section_" + title, content, title, true, 30f, Ink, TextAlignmentOptions.BottomLeft);
            SetHeading(t, 30f); t.alignment = TextAlignmentOptions.BottomLeft; t.raycastTarget = false;
            t.margin = new Vector4(8f, 0f, 0f, 6f);
            var le = t.gameObject.AddComponent<LayoutElement>(); le.preferredHeight = 62f; le.minHeight = 62f;
        }

        private static SettingRow Row(Transform content, string key, string label, bool slider)
        {
            var bg = NewImage("Row_" + key, content, S("Row"), Color.white, true);
            var le = bg.gameObject.AddComponent<LayoutElement>(); le.preferredHeight = 62f; le.minHeight = 62f;
            var row = bg.gameObject.AddComponent<SettingRow>();
            row.key = key;
            row.group = bg.gameObject.AddComponent<CanvasGroup>();
            row.label = Txt("Label", bg.transform, label, 22f, Ink, TextAlignmentOptions.MidlineLeft);
            var lr = row.label.rectTransform; lr.anchorMin = Vector2.zero; lr.anchorMax = Vector2.one; lr.offsetMin = new Vector2(24f, 0f); lr.offsetMax = new Vector2(-580f, 0f);
            var ctl = Rect("Control", bg.transform, new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(-560f, 0f), new Vector2(-16f, 0f));
            if (!slider)
            {
                row.prev = Btn("Prev", ctl, "<", false, 28f);
                var pr = (RectTransform)row.prev.transform; pr.anchorMin = new Vector2(0f, 0.5f); pr.anchorMax = new Vector2(0f, 0.5f); pr.pivot = new Vector2(0f, 0.5f); pr.sizeDelta = new Vector2(56f, 48f); pr.anchoredPosition = Vector2.zero;
                row.next = Btn("Next", ctl, ">", false, 28f);
                var nr = (RectTransform)row.next.transform; nr.anchorMin = new Vector2(1f, 0.5f); nr.anchorMax = new Vector2(1f, 0.5f); nr.pivot = new Vector2(1f, 0.5f); nr.sizeDelta = new Vector2(56f, 48f); nr.anchoredPosition = Vector2.zero;
                row.value = Txt("Value", ctl, "-", 22f, Ink, TextAlignmentOptions.Center);
                var vr = row.value.rectTransform; vr.anchorMin = Vector2.zero; vr.anchorMax = Vector2.one; vr.offsetMin = new Vector2(64f, 0f); vr.offsetMax = new Vector2(-64f, 0f);
            }
            else
            {
                var sgo = Rect("Slider", ctl, new Vector2(0f, 0.5f), new Vector2(1f, 0.5f), new Vector2(8f, -12f), new Vector2(-140f, 12f));
                var track = sgo.gameObject.AddComponent<Image>(); track.sprite = S("LoadingTrack"); track.type = Image.Type.Sliced; track.raycastTarget = true;
                var fillArea = Rect("Fill Area", sgo, Vector2.zero, Vector2.one, new Vector2(5f, 5f), new Vector2(-5f, -5f));
                var fill = NewImage("Fill", fillArea, S("LoadingFill"), Color.white, true);
                fill.rectTransform.anchorMin = Vector2.zero; fill.rectTransform.anchorMax = new Vector2(0f, 1f); fill.rectTransform.offsetMin = Vector2.zero; fill.rectTransform.offsetMax = new Vector2(10f, 0f);
                var handleArea = Rect("Handle Slide Area", sgo, Vector2.zero, Vector2.one, new Vector2(12f, 0f), new Vector2(-12f, 0f));
                var handle = NewImage("Handle", handleArea, S("ButtonLight"), Color.white, true, true);
                handle.rectTransform.anchorMin = new Vector2(0f, 0.5f); handle.rectTransform.anchorMax = new Vector2(0f, 0.5f); handle.rectTransform.sizeDelta = new Vector2(30f, 36f);
                var sl = sgo.gameObject.AddComponent<Slider>();
                sl.fillRect = fill.rectTransform; sl.handleRect = handle.rectTransform; sl.targetGraphic = handle;
                sl.direction = Slider.Direction.LeftToRight; sl.wholeNumbers = true; sl.minValue = 0; sl.maxValue = 100;
                sl.transition = Selectable.Transition.ColorTint;
                var nav = sl.navigation; nav.mode = Navigation.Mode.None; sl.navigation = nav;
                row.slider = sl;
                row.value = Txt("Value", ctl, "-", 22f, Ink, TextAlignmentOptions.MidlineRight);
                var vr = row.value.rectTransform; vr.anchorMin = new Vector2(1f, 0f); vr.anchorMax = Vector2.one; vr.offsetMin = new Vector2(-124f, 0f); vr.offsetMax = Vector2.zero;
            }
            EditorUtility.SetDirty(row);
            return row;
        }

        // ── MainMenu ─────────────────────────────────────────────────────────

        private static Scene Need(string name)
        {
            var s = SceneManager.GetActiveScene();
            if (s.name != name) throw new System.Exception("open the " + name + " scene first (current: " + s.name + ")");
            return s;
        }

        private static string Menu()
        {
            var scene = Need("MainMenu");
            var mmc = Object.FindFirstObjectByType<MainMenuController>(FindObjectsInactive.Include);
            if (mmc == null) throw new System.Exception("MainMenuController not found");
            var canvas = mmc.GetComponentInParent<Canvas>(true).rootCanvas.transform;
            if (canvas.Find("SettingsScreen") != null) return "ERR settingsui menu: already applied";
            var style = EnsureStyle();
            var hover = PolishAuthoring.EnsureHoverStyle();

            // heading width from the real font
            var probe = NewText("probe", canvas, "Settings", true, 40f, Ink, TextAlignmentOptions.Left);
            SetHeading(probe, 40f);
            float headW = Mathf.Ceil(probe.GetPreferredValues("Settings", 10000f, 1000f).x);
            Object.DestroyImmediate(probe.gameObject);

            var spec = FrameSpec(headW);
            var framePrefab = BuildPrefab(spec, style, FramePath, Probe[0], Probe[1], Probe[2], Probe[3]);

            // screen root (over the whole canvas)
            var root = NewRect("SettingsScreen", canvas);
            root.SetAsLastSibling();
            Stretch(root);
            var group = root.gameObject.AddComponent<CanvasGroup>();
            group.alpha = 0f; group.blocksRaycasts = false; group.interactable = false;
            var screen = root.gameObject.AddComponent<SettingsScreen>();
            screen.group = group;
            var frame = Instantiate(framePrefab, root, 0, spec, null);
            var panel = frame.transform.Find("PanelGuide/Panel_panel") as RectTransform;
            if (panel == null) throw new System.Exception("panel piece not found");

            var heading = NewText("Heading", root, "Settings", true, 40f, Color.white, TextAlignmentOptions.BottomLeft);
            SetHeading(heading, 40f); heading.raycastTarget = false; heading.textWrappingMode = TextWrappingModes.NoWrap;
            Place(heading.rectTransform, L(28f), R(-76f), L(28f + headW + 8f), R(-28f), new Vector2(0f, 0f));

            // body follows the panel piece
            var body = NewRect("Body", root);
            var follow = body.gameObject.AddComponent<FrameFollower>();
            follow.piece = panel; follow.mode = FrameFollower.Mode.Rect; follow.insets = new Vector4(36f, 26f, 36f, 30f);

            var scrollRT = Rect("Scroll", body, Vector2.zero, Vector2.one, new Vector2(0f, 84f), Vector2.zero);
            var scroll = scrollRT.gameObject.AddComponent<ScrollRect>();
            scroll.horizontal = false; scroll.movementType = ScrollRect.MovementType.Clamped; scroll.scrollSensitivity = 40f;
            var viewport = Rect("Viewport", scrollRT, Vector2.zero, Vector2.one, Vector2.zero, new Vector2(-22f, 0f));
            viewport.gameObject.AddComponent<RectMask2D>();
            var vimg = viewport.gameObject.AddComponent<Image>(); vimg.color = new Color(0f, 0f, 0f, 0f); vimg.raycastTarget = true;
            var content = Rect("Content", viewport, new Vector2(0f, 1f), new Vector2(1f, 1f), Vector2.zero, Vector2.zero);
            content.pivot = new Vector2(0.5f, 1f);
            var vl = content.gameObject.AddComponent<VerticalLayoutGroup>();
            vl.spacing = 8f; vl.padding = new RectOffset(0, 8, 0, 16);
            vl.childControlWidth = true; vl.childControlHeight = true; vl.childForceExpandWidth = true; vl.childForceExpandHeight = false;
            var csf = content.gameObject.AddComponent<ContentSizeFitter>(); csf.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            scroll.viewport = viewport; scroll.content = content;
            var sbRT = Rect("Scrollbar", scrollRT, new Vector2(1f, 0f), Vector2.one, new Vector2(-8f, 0f), Vector2.zero);
            var sbBg = sbRT.gameObject.AddComponent<Image>(); sbBg.color = new Color(1f, 1f, 1f, 0.08f); sbBg.sprite = S("ScrollbarRound"); sbBg.type = Image.Type.Sliced;
            var sbArea = Rect("Sliding Area", sbRT, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            var sbHandle = NewImage("Handle", sbArea, S("ScrollbarRound"), new Color(0.91f, 0.93f, 0.9f, 0.6f), true, true);
            Stretch(sbHandle.rectTransform);
            var sb = sbRT.gameObject.AddComponent<Scrollbar>();
            sb.handleRect = sbHandle.rectTransform; sb.targetGraphic = sbHandle; sb.direction = Scrollbar.Direction.BottomToTop;
            var sbNav = sb.navigation; sbNav.mode = Navigation.Mode.None; sb.navigation = sbNav;
            scroll.verticalScrollbar = sb; scroll.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.AutoHide;
            screen.scroll = scroll;

            Section(content, "Display");
            Row(content, "displayMode", "Display mode", false);
            Row(content, "resolution", "Resolution", false);
            Row(content, "vsync", "VSync", false);
            Row(content, "frameLimit", "Frame limit", false);
            Section(content, "Graphics");
            Row(content, "quality", "Quality preset", false);
            Row(content, "shadows", "Shadows", false);
            Row(content, "detail", "Terrain detail distance", true);
            Section(content, "Audio");
            Row(content, "volume", "Master volume", true);
            Row(content, "muteUnfocused", "Mute when in background", false);
            Section(content, "Controls");
            Row(content, "sensitivity", "Mouse sensitivity", true);
            Row(content, "invertY", "Invert mouse Y", false);
            Row(content, "fov", "Field of view", true);

            // footer
            var footer = Rect("Footer", body, Vector2.zero, new Vector2(1f, 0f), Vector2.zero, new Vector2(0f, 64f));
            var reset = Btn("ResetDefaults", footer, "Reset to Defaults", false);
            var rr = (RectTransform)reset.transform; rr.anchorMin = new Vector2(0f, 0f); rr.anchorMax = new Vector2(0f, 1f); rr.pivot = new Vector2(0f, 0.5f); rr.offsetMin = new Vector2(0f, 6f); rr.offsetMax = new Vector2(300f, -6f);
            var apply = Btn("Apply", footer, "APPLY", true, 24f);
            var ar = (RectTransform)apply.transform; ar.anchorMin = new Vector2(1f, 0f); ar.anchorMax = new Vector2(1f, 1f); ar.pivot = new Vector2(1f, 0.5f); ar.offsetMin = new Vector2(-200f, 6f); ar.offsetMax = new Vector2(0f, -6f);
            var back = Btn("Back", footer, "Back", false);
            var br = (RectTransform)back.transform; br.anchorMin = new Vector2(1f, 0f); br.anchorMax = new Vector2(1f, 1f); br.pivot = new Vector2(1f, 0.5f); br.offsetMin = new Vector2(-340f, 6f); br.offsetMax = new Vector2(-220f, -6f);
            var status = Txt("Status", footer, "", 18f, Muted, TextAlignmentOptions.MidlineRight, false);
            var sr = status.rectTransform; sr.anchorMin = Vector2.zero; sr.anchorMax = Vector2.one; sr.offsetMin = new Vector2(320f, 0f); sr.offsetMax = new Vector2(-360f, 0f);
            screen.resetButton = reset; screen.applyButton = apply; screen.backButton = back; screen.statusText = status;

            // display keep / revert
            var confirm = NewImage("ConfirmDisplay", body, null, new Color(0f, 0f, 0f, 0.86f), false, true);
            Stretch(confirm.rectTransform);
            var box = NewImage("Box", confirm.transform, S("Row"), Color.white, true);
            box.rectTransform.anchorMin = box.rectTransform.anchorMax = new Vector2(0.5f, 0.5f); box.rectTransform.sizeDelta = new Vector2(760f, 270f);
            var ctext = Txt("Text", box.transform, "Keep these display settings?\nReverting in 15 s", 26f, Ink, TextAlignmentOptions.Center);
            ctext.textWrappingMode = TextWrappingModes.Normal;
            var ct = ctext.rectTransform; ct.anchorMin = new Vector2(0f, 0.4f); ct.anchorMax = Vector2.one; ct.offsetMin = new Vector2(30f, 0f); ct.offsetMax = new Vector2(-30f, -24f);
            var keep = Btn("Keep", box.transform, "KEEP", true);
            var kr = (RectTransform)keep.transform; kr.anchorMin = kr.anchorMax = new Vector2(0.5f, 0f); kr.pivot = new Vector2(0f, 0f); kr.sizeDelta = new Vector2(200f, 54f); kr.anchoredPosition = new Vector2(20f, 32f);
            var revert = Btn("Revert", box.transform, "Revert", false);
            var vr2 = (RectTransform)revert.transform; vr2.anchorMin = vr2.anchorMax = new Vector2(0.5f, 0f); vr2.pivot = new Vector2(1f, 0f); vr2.sizeDelta = new Vector2(200f, 54f); vr2.anchoredPosition = new Vector2(-20f, 32f);
            confirm.gameObject.SetActive(false);
            screen.confirmPanel = confirm.gameObject; screen.confirmText = ctext; screen.keepButton = keep; screen.revertButton = revert;

            // hover (#565656) on every new button + slider handles
            var counts = new Dictionary<string, int> { ["Text"] = 0, ["Icon"] = 0, ["Fill"] = 0, ["skip"] = 0 };
            foreach (var b in root.GetComponentsInChildren<Button>(true)) PolishAuthoring.Add(b, hover, counts);
            foreach (var sl in root.GetComponentsInChildren<Slider>(true))
            {
                var h = sl.gameObject.AddComponent<HoverTint>();
                h.style = hover; h.mode = HoverTint.Mode.Icon; h.selectable = sl; h.targets = new Graphic[] { sl.targetGraphic };
            }
            EditorUtility.SetDirty(screen);

            // prefab (screen + frame), then scene-specific links on the instance
            EnsureFolder(PrefabDir + "/Settings");
            var prefab = PrefabUtility.SaveAsPrefabAssetAndConnect(root.gameObject, ScreenPath, InteractionMode.AutomatedAction);
            if (prefab == null) throw new System.Exception("saving the SettingsScreen prefab failed");

            var frameRoot = canvas.Find("MainMenuFrame");
            var hide = new List<CanvasGroup>();
            if (frameRoot != null) hide.Add(Ensure<CanvasGroup>(frameRoot.gameObject));
            if (mmc.menuGroup != null) hide.Add(mmc.menuGroup);
            screen.hideWhileOpen = hide.ToArray();
            PrefabUtility.RecordPrefabInstancePropertyModifications(screen);

            // "Settings" link → new screen
            var link = FindDeep(canvas, "MainMenu")?.Find("Settings")?.GetComponent<Button>();
            if (link == null) throw new System.Exception("MainMenu/Settings button not found");
            Rewire(link, screen);

            string mode = PlayModeVisuals(canvas, hover);
            EditorSceneManager.MarkSceneDirty(scene);
            if (!EditorSceneManager.SaveScene(scene)) throw new System.Exception("saving MainMenu failed");
            return "settingsui menu ok (head " + headW + ", buttons hover " + (counts["Text"] + counts["Fill"] + counts["Icon"]) + "; " + mode + ")";
        }

        /// <summary>Removes the old animator toggle (SettingsButton) from the button and opens the new screen instead.</summary>
        private static void Rewire(Button b, SettingsScreen screen)
        {
            for (int i = b.onClick.GetPersistentEventCount() - 1; i >= 0; i--)
                if (b.onClick.GetPersistentTarget(i) is Orivilon.UI.Settings.SettingsButton || b.onClick.GetPersistentTarget(i) is SettingsScreen)
                    UnityEventTools.RemovePersistentListener(b.onClick, i);
            Listen(b, screen.Open);
            if (PrefabUtility.IsPartOfPrefabInstance(b)) PrefabUtility.RecordPrefabInstancePropertyModifications(b);
        }

        /// <summary>Selected markers on the Singleplayer / Multiplayer selectors + the mode line above PLAY.</summary>
        private static string PlayModeVisuals(Transform canvas, HoverStyle hover)
        {
            var ui = Object.FindFirstObjectByType<MainMenuUI>(FindObjectsInactive.Include);
            if (ui == null) return "no MainMenuUI";
            var so = new SerializedObject(ui);
            var sp = so.FindProperty("singleplayerButton").objectReferenceValue as Button;
            var mp = so.FindProperty("multiplayerButton").objectReferenceValue as Button;
            GameObject Marker(Button b)
            {
                if (b == null) return null;
                var existing = b.transform.Find("ModeSelected");
                if (existing != null) return existing.gameObject;
                var img = NewImage("ModeSelected", b.transform, hover.slotOutline, Ink, true);
                img.raycastTarget = false;
                var rt = img.rectTransform; rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one; rt.offsetMin = new Vector2(-9f, -9f); rt.offsetMax = new Vector2(9f, 9f);
                img.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;
                img.gameObject.SetActive(false);
                return img.gameObject;
            }
            var spMark = Marker(sp); var mpMark = Marker(mp);
            TMP_Text modeText = null;
            var play = FindDeep(canvas, "PlayButton") as RectTransform;
            if (play != null)
            {
                var t = Txt("PlayModeText", play.parent, "", 18f, Muted, TextAlignmentOptions.Center, false);
                t.textWrappingMode = TextWrappingModes.Normal; t.overflowMode = TextOverflowModes.Overflow;
                var rt = t.rectTransform;
                rt.anchorMin = new Vector2(play.anchorMin.x, play.anchorMax.y); rt.anchorMax = new Vector2(play.anchorMax.x, play.anchorMax.y);
                rt.pivot = new Vector2(0.5f, 0f);
                rt.offsetMin = new Vector2(play.offsetMin.x - 80f, play.offsetMax.y + 10f);
                rt.offsetMax = new Vector2(play.offsetMax.x + 80f, play.offsetMax.y + 62f);
                t.transform.SetSiblingIndex(play.GetSiblingIndex());
                modeText = t;
            }
            so.FindProperty("singleplayerSelected").objectReferenceValue = spMark;
            so.FindProperty("multiplayerSelected").objectReferenceValue = mpMark;
            so.FindProperty("playModeText").objectReferenceValue = modeText;
            so.ApplyModifiedPropertiesWithoutUndo();
            return "mode markers " + (spMark != null) + "/" + (mpMark != null) + ", mode line " + (modeText != null);
        }

        /// <summary>
        /// Adds the BETA / brand corner tabs (same geometry as the main-menu and pause frames) to the saved
        /// SettingsFrame prefab, with their inward joins, and puts the version + brand texts on them in the
        /// SettingsScreen prefab (copied from the MainMenu canvas, GameVersionText keeps the live version).
        /// </summary>
        private static string Tabs()
        {
            Need("MainMenu");
            var framePrefab = PrefabUtility.LoadPrefabContents(FramePath);
            int joins = 0;
            try
            {
                if (framePrefab.transform.Find("Panel_beta") != null) return "ERR settingsui tabs: already applied";
                var frame = framePrefab.GetComponent<UiFrame>();
                var probe = framePrefab.transform.Find("Panel_heading");
                var head = (RectTransform)probe;
                float headW = head.offsetMax.x - 56f;
                var spec = FrameSpec(headW);
                spec.All(spec.P("beta"), B(L(-OFF), L(-OFF), L(336f), L(50f)));
                spec.All(spec.P("brand"), B(R(-220f), L(-OFF), R(OFF), L(50f)));
                var panelPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(PanelPrefabPath);
                var joinPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(JoinPrefabPath);
                var made = new Dictionary<string, RectTransform>();
                foreach (var p in spec.pieces)
                {
                    var t = framePrefab.transform.Find("Panel_" + p.name) ?? framePrefab.transform.Find("PanelGuide/Panel_" + p.name);
                    if (t != null) { made[p.name] = (RectTransform)t; continue; }
                    var go = (GameObject)PrefabUtility.InstantiatePrefab(panelPrefab, framePrefab.transform);
                    go.name = "Panel_" + p.name;
                    var rt = (RectTransform)go.transform; rt.pivot = new Vector2(0.5f, 0.5f);
                    var b = p.boxes["Default"]; SetBox(rt, b);
                    go.transform.SetSiblingIndex(framePrefab.transform.Find("Joins").GetSiblingIndex());
                    made[p.name] = rt;
                    frame.states[0].pieces.Add(new UiFrame.PieceState { piece = rt, anchorMin = b.aMin, anchorMax = b.aMax, offsetMin = b.oMin, offsetMax = b.oMax });
                }
                var joinsRoot = framePrefab.transform.Find("Joins");
                foreach (var k in DetectJoins(spec, Probe))
                {
                    if (k.a != "beta" && k.b != "beta" && k.a != "brand" && k.b != "brand") continue;
                    var go = (GameObject)PrefabUtility.InstantiatePrefab(joinPrefab, joinsRoot);
                    go.name = "Join_" + k;
                    var j = go.GetComponent<FrameJoin>();
                    j.a = made[k.a]; j.aRightEdge = k.aRight; j.b = made[k.b]; j.bTopEdge = k.bTop;
                    joins++;
                }
                PrefabUtility.SaveAsPrefabAsset(framePrefab, FramePath);
            }
            finally { PrefabUtility.UnloadPrefabContents(framePrefab); }

            var mmc = Object.FindFirstObjectByType<MainMenuController>(FindObjectsInactive.Include);
            var canvas = mmc.GetComponentInParent<Canvas>(true).rootCanvas.transform;
            var srcVersion = canvas.Find("BuildVersion"); var srcBrand = canvas.Find("Orivilon");
            var screenPrefab = PrefabUtility.LoadPrefabContents(ScreenPath);
            try
            {
                int after = screenPrefab.transform.Find("SettingsFrame").GetSiblingIndex() + 1;
                foreach (var src in new[] { srcVersion, srcBrand })
                {
                    if (src == null) continue;
                    var c = Object.Instantiate(src.gameObject, screenPrefab.transform);
                    c.name = src.name;
                    c.transform.SetSiblingIndex(after++);
                }
                PrefabUtility.SaveAsPrefabAsset(screenPrefab, ScreenPath);
            }
            finally { PrefabUtility.UnloadPrefabContents(screenPrefab); }
            return "settingsui tabs ok (joins " + joins + ", texts " + (srcVersion != null) + "/" + (srcBrand != null) + ")";
        }

        // ── visual pass: Settings join, glyph-fitted corner tabs, PLAY subtitle ─

        private static RectTransform MakeFit(Transform parent, string name, Vector2 corner, float h, TMP_Text[] labels, RectTransform[] hits)
        {
            var t = parent.Find(name) as RectTransform;
            if (t == null) t = NewRect(name, parent);
            var f = Ensure<GlyphTabFit>(t.gameObject);
            f.corner = corner; f.tabHeight = h; f.labels = labels; f.hits = hits ?? new RectTransform[0];
            f.pad = -1f; f.gap = -1f;
            f.Fit();
            EditorUtility.SetDirty(f);
            return t;
        }

        private static int SetFit(UiFrame frame, string piece, UiFrame.FitSide side, RectTransform fitTo, string onlyState = null)
        {
            if (frame == null || fitTo == null) return 0;
            int n = 0;
            foreach (var st in frame.states)
            {
                if (onlyState != null && st.name != onlyState) continue;
                foreach (var ps in st.pieces)
                    if (ps.piece != null && ps.piece.name == "Panel_" + piece) { ps.fit = side; ps.fitTo = fitTo; ps.fitPad = 0f; n++; }
            }
            EditorUtility.SetDirty(frame);
            if (PrefabUtility.IsPartOfPrefabInstance(frame)) PrefabUtility.RecordPrefabInstancePropertyModifications(frame);
            return n;
        }

        private static TMP_Text Label(Transform t) => t != null ? t.GetComponentInChildren<TMP_Text>(true) : null;

        // The heading bridge (between the Settings heading and the panel) was exactly 2R tall, so its rounded
        // corners and the panel's / heading's rounded corners met tangentially and left 1-px lens holes at
        // the left panel edge and the heading's right edge (visible as stray rim pixels). 4R tall and
        // centred on the same seam, its rounded corners lie fully inside the panel / heading fills.
        private static int GrowBridge(UiFrame frame)
        {
            int n = 0;
            foreach (var st in frame.states)
                foreach (var ps in st.pieces)
                    if (ps.piece != null && ps.piece.name == "Panel_headingBridge" && ps.offsetMax.y - ps.offsetMin.y < 50f)
                    { float h = ps.offsetMax.y - ps.offsetMin.y; ps.offsetMin.y -= h * 0.5f; ps.offsetMax.y += h * 0.5f; n++; }
            var b = frame.transform.Find("PanelGuide/Panel_headingBridge") as RectTransform;
            if (b == null) foreach (var t in frame.GetComponentsInChildren<RectTransform>(true)) if (t.name == "Panel_headingBridge") { b = t; break; }
            if (b != null && b.rect.height < 50f) { float h = b.offsetMax.y - b.offsetMin.y; b.offsetMin -= new Vector2(0f, h * 0.5f); b.offsetMax += new Vector2(0f, h * 0.5f); EditorUtility.SetDirty(b); }
            EditorUtility.SetDirty(frame);
            if (PrefabUtility.IsPartOfPrefabInstance(frame)) PrefabUtility.RecordPrefabInstancePropertyModifications(frame);
            return n;
        }

        // The PLAY subtitle's rect (26 px) was shorter than Space Mono's line height at 18 pt, so TMP's
        // ellipsis overflow dropped the whole line. Give it room and plain overflow (single line, no wrap).
        private static string SubtitleFix()
        {
            var scene = Need("MainMenu");
            var mmc = Object.FindFirstObjectByType<MainMenuController>(FindObjectsInactive.Include);
            var sub = mmc != null && mmc.playButton != null ? mmc.playButton.transform.Find("ModeSubtitle")?.GetComponent<TMP_Text>() : null;
            if (sub == null) return "ERR settingsui subtitle: ModeSubtitle not found";
            sub.overflowMode = TextOverflowModes.Overflow;
            sub.textWrappingMode = TextWrappingModes.NoWrap;
            var r = sub.rectTransform; r.offsetMin = new Vector2(16f, 12f); r.offsetMax = new Vector2(-16f, 44f);
            EditorUtility.SetDirty(sub); EditorUtility.SetDirty(r);
            EditorSceneManager.MarkSceneDirty(scene);
            if (!EditorSceneManager.SaveScene(scene)) throw new System.Exception("saving MainMenu failed");
            return "settingsui subtitle: overflow, rect " + r.rect.size;
        }

        private static string Bridge()
        {
            var report = new System.Text.StringBuilder("settingsui bridge:");
            var fp = PrefabUtility.LoadPrefabContents(FramePath);
            try { report.Append(" frame ").Append(GrowBridge(fp.GetComponentInChildren<UiFrame>(true))); PrefabUtility.SaveAsPrefabAsset(fp, FramePath); }
            finally { PrefabUtility.UnloadPrefabContents(fp); }
            var sp = PrefabUtility.LoadPrefabContents(ScreenPath);
            try { report.Append(" screen ").Append(GrowBridge(sp.transform.Find("SettingsFrame").GetComponent<UiFrame>())); PrefabUtility.SaveAsPrefabAsset(sp, ScreenPath); }
            finally { PrefabUtility.UnloadPrefabContents(sp); }
            return report.ToString();
        }

        private static string Visual()
        {
            var report = new System.Text.StringBuilder("settingsui visual:");
            var scene = Need("MainMenu");

            // 1. Settings frame: the heading/panel corner join belongs to the panel's long left edge (as in
            //    the world screens), not to the short bridge piece – that produced the notched corner.
            var fp = PrefabUtility.LoadPrefabContents(FramePath);
            try
            {
                var j = fp.transform.Find("Joins/Join_headingBridgeL_headingB");
                if (j != null)
                {
                    var fj = j.GetComponent<FrameJoin>();
                    fj.a = fp.transform.Find("PanelGuide/Panel_panel") as RectTransform;
                    j.name = "Join_panelL_headingB";
                    PrefabUtility.SaveAsPrefabAsset(fp, FramePath);
                    report.Append(" join->panel");
                }
                else report.Append(" join already");
            }
            finally { PrefabUtility.UnloadPrefabContents(fp); }

            // 2. Settings screen prefab: BETA / brand tabs fitted to their glyphs
            var sp = PrefabUtility.LoadPrefabContents(ScreenPath);
            try
            {
                var root = sp.transform;
                var frame = root.Find("SettingsFrame").GetComponent<UiFrame>();
                var ver = root.Find("BuildVersion")?.GetComponent<TMP_Text>(); var brand = root.Find("Orivilon")?.GetComponent<TMP_Text>();
                if (ver != null) SetFit(frame, "beta", UiFrame.FitSide.Right, MakeFit(root, "TabFit_beta", Vector2.zero, 50f, new[] { ver }, null));
                if (brand != null) SetFit(frame, "brand", UiFrame.FitSide.Left, MakeFit(root, "TabFit_brand", new Vector2(1f, 0f), 50f, new[] { brand }, null));
                PrefabUtility.SaveAsPrefabAsset(sp, ScreenPath);
                report.Append(" settings-tabs");
            }
            finally { PrefabUtility.UnloadPrefabContents(sp); }

            // 3. MainMenu: BETA / brand / top links + PLAY subtitle
            var mmc = Object.FindFirstObjectByType<MainMenuController>(FindObjectsInactive.Include);
            var canvas = mmc.GetComponentInParent<Canvas>(true).rootCanvas.transform;
            var mframe = canvas.Find("MainMenuFrame").GetComponent<UiFrame>();
            var mver = canvas.Find("BuildVersion")?.GetComponent<TMP_Text>(); var mbrand = canvas.Find("Orivilon")?.GetComponent<TMP_Text>();
            if (mver != null) report.Append(" beta " + SetFit(mframe, "beta", UiFrame.FitSide.Right, MakeFit(canvas, "TabFit_beta", Vector2.zero, 50f, new[] { mver }, null)));
            if (mbrand != null) report.Append(" brand " + SetFit(mframe, "brand", UiFrame.FitSide.Left, MakeFit(canvas, "TabFit_brand", new Vector2(1f, 0f), 50f, new[] { mbrand }, null)));
            var menu = FindDeep(canvas, "MainMenu");
            var links = new[] { "Settings", "Discord", "Website" };
            var hits = new RectTransform[3]; var labs = new TMP_Text[3];
            for (int i = 0; i < 3; i++) { hits[i] = menu.Find(links[i]) as RectTransform; labs[i] = Label(hits[i]); }
            foreach (var l in labs) if (l != null) { l.alignment = TextAlignmentOptions.Center; EditorUtility.SetDirty(l); }
            report.Append(" links " + SetFit(mframe, "links", UiFrame.FitSide.Left, MakeFit(canvas, "TabFit_links", Vector2.one, 56f, labs, hits), "Menu"));

            var play = mmc.playButton; var playText = mmc.playLabel;
            if (play != null && playText != null && play.transform.Find("ModeSubtitle") == null)
            {
                var sub = Txt("ModeSubtitle", play.transform, "", 18f, new Color(0.11f, 0.12f, 0.11f, 0.8f), TextAlignmentOptions.Center, false);
                var srt = sub.rectTransform; srt.anchorMin = Vector2.zero; srt.anchorMax = new Vector2(1f, 0f); srt.pivot = new Vector2(0.5f, 0f);
                srt.offsetMin = new Vector2(16f, 14f); srt.offsetMax = new Vector2(-16f, 40f);
                var prt = playText.rectTransform; prt.anchorMin = Vector2.zero; prt.anchorMax = Vector2.one; prt.offsetMin = new Vector2(0f, 34f); prt.offsetMax = new Vector2(0f, -4f);
                EditorUtility.SetDirty(playText);
                var ht = play.GetComponent<HoverTint>();
                if (ht != null)
                {
                    var l = new List<TMP_Text>(ht.labels ?? new TMP_Text[0]); if (!l.Contains(sub)) l.Add(sub); ht.labels = l.ToArray(); EditorUtility.SetDirty(ht);
                }
                var ui = Object.FindFirstObjectByType<MainMenuUI>(FindObjectsInactive.Include);
                var so = new SerializedObject(ui);
                so.FindProperty("playModeSubtitle").objectReferenceValue = sub;
                so.ApplyModifiedPropertiesWithoutUndo();
                report.Append(" subtitle");
            }
            EditorSceneManager.MarkSceneDirty(scene);
            if (!EditorSceneManager.SaveScene(scene)) throw new System.Exception("saving MainMenu failed");

            // 4. Game: HUD + pause BETA, pause brand, pause Settings tab
            var game = EditorSceneManager.OpenScene("Assets/Scenes/Game.unity", OpenSceneMode.Single);
            var pm = Object.FindFirstObjectByType<PauseMenuUI>(FindObjectsInactive.Include);
            var gcanvas = pm.GetComponentInParent<Canvas>(true).rootCanvas.transform;
            UiFrame pauseFrame = pm.transform.Find("PauseFrame")?.GetComponent<UiFrame>(), hudFrame = gcanvas.Find("HudFrame")?.GetComponent<UiFrame>();
            var gver = gcanvas.Find("BuildVersion")?.GetComponent<TMP_Text>(); var gbrand = pm.transform.Find("Orivilon")?.GetComponent<TMP_Text>();
            if (gver != null)
            {
                var fit = MakeFit(gcanvas, "TabFit_beta", Vector2.zero, 50f, new[] { gver }, null);
                report.Append(" game beta " + (SetFit(pauseFrame, "beta", UiFrame.FitSide.Right, fit) + SetFit(hudFrame, "beta", UiFrame.FitSide.Right, fit)));
            }
            if (gbrand != null) report.Append(" game brand " + SetFit(pauseFrame, "brand", UiFrame.FitSide.Left, MakeFit(pm.transform, "TabFit_brand", new Vector2(1f, 0f), 50f, new[] { gbrand }, null)));
            RectTransform tab = null;
            for (int i = 0; i < pm.transform.childCount; i++) { var c = pm.transform.GetChild(i); if (c.name == "Settings" && c.gameObject.activeSelf) { tab = (RectTransform)c; break; } }
            if (tab != null)
            {
                var ftt = tab.GetComponent<FitToText>(); if (ftt != null) { ftt.enabled = false; EditorUtility.SetDirty(ftt); }
                var tl = Label(tab); if (tl != null) { tl.alignment = TextAlignmentOptions.Center; EditorUtility.SetDirty(tl); }
                report.Append(" pause tab " + SetFit(pauseFrame, "settingsTab", UiFrame.FitSide.Left, MakeFit(pm.transform, "TabFit_settings", Vector2.one, 56f, new[] { tl }, new[] { tab })));
            }
            EditorSceneManager.MarkSceneDirty(game);
            if (!EditorSceneManager.SaveScene(game)) throw new System.Exception("saving Game failed");
            return report.ToString();
        }

        // ── Game ─────────────────────────────────────────────────────────────

        private static string Game()
        {
            var scene = Need("Game");
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(ScreenPath);
            if (prefab == null) throw new System.Exception("run 'settingsui menu' first (prefab missing)");
            var pm = Object.FindFirstObjectByType<PauseMenuUI>(FindObjectsInactive.Include);
            if (pm == null) throw new System.Exception("PauseMenuUI not found");
            var canvas = pm.GetComponentInParent<Canvas>(true).rootCanvas.transform;
            if (canvas.Find("SettingsScreen") != null) return "ERR settingsui game: already applied";
            var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, canvas);
            go.name = "SettingsScreen";
            go.transform.SetAsLastSibling();
            var rt = (RectTransform)go.transform; rt.localScale = Vector3.one; Stretch(rt);
            Place(rt, L(0f), L(0f), R(0f), R(0f));
            var screen = go.GetComponent<SettingsScreen>();
            screen.hideWhileOpen = new[] { Ensure<CanvasGroup>(pm.gameObject) };
            PrefabUtility.RecordPrefabInstancePropertyModifications(screen);
            Button tab = null;
            for (int i = 0; i < pm.transform.childCount; i++)
            {
                var c = pm.transform.GetChild(i);
                if (c.name == "Settings" && c.gameObject.activeSelf) { tab = c.GetComponent<Button>(); break; }
            }
            if (tab == null) throw new System.Exception("pause Settings tab not found");
            Rewire(tab, screen);
            EditorSceneManager.MarkSceneDirty(scene);
            if (!EditorSceneManager.SaveScene(scene)) throw new System.Exception("saving Game failed");
            return "settingsui game ok (under " + canvas.name + ", pause tab rewired)";
        }
    }
}
