using System.Collections.Generic;
using System.Reflection;
using Orivilon.Core;
using Orivilon.Inventory;
using Orivilon.UI.Menu;
using TMPro;
using UnityEditor;
using UnityEditor.Events;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;
using static Orivilon.UI.Refresh.EditorTools.UiAuthoringKit;
using static Orivilon.UI.Refresh.EditorTools.FrameAuthoring;

namespace Orivilon.UI.Refresh.EditorTools
{
    /// <summary>
    /// Authors the revision-3 look into the MainMenu, LoadingScreen and Game scenes (HUD, inventory,
    /// pause) as saved objects. Ported 1:1 from the former runtime builders (MainMenuRefresh,
    /// LoadingRefresh, HudRefresh, PauseRefresh), but every rectangle is now converted into real
    /// anchors/offsets so Unity's layout handles all screen sizes without runtime placement.
    /// Must run with the Game view at 1920×1080 (checked).
    /// </summary>
    public static class ScreenAuthoring
    {
        private const float OFF = 60f, BAR = 12f;
        private const BindingFlags NP = BindingFlags.Instance | BindingFlags.NonPublic;

        // ── common ───────────────────────────────────────────────────────────

        private static RectTransform canvasRT;

        private static void UseCanvas(Component c)
        {
            var cv = c.GetComponentInParent<Canvas>(true);
            canvasRT = (RectTransform)cv.rootCanvas.transform;
        }

        public static void CheckCanvas(Canvas cv)
        {
            var s = ((RectTransform)cv.transform).rect.size;
            if (Mathf.Abs(s.x - 1920f) > 0.5f || Mathf.Abs(s.y - 1080f) > 0.5f)
                throw new System.Exception("Canvas '" + cv.name + "' is " + s + " – set the Game view to 1920x1080 and canvas scale 1 before authoring.");
        }

        private static readonly Vector3[] corners = new Vector3[4];

        /// <summary>Real canvas-space rect (origin bottom-left) of an element.</summary>
        private static Rect Real(RectTransform rt)
        {
            rt.GetWorldCorners(corners);
            Vector2 a = canvasRT.InverseTransformPoint(corners[0]), b = canvasRT.InverseTransformPoint(corners[2]);
            Vector2 o = canvasRT.rect.min;
            a -= o; b -= o;
            return Rect.MinMaxRect(Mathf.Min(a.x, b.x), Mathf.Min(a.y, b.y), Mathf.Max(a.x, b.x), Mathf.Max(a.y, b.y));
        }

        /// <summary>Place on a canvas rect given at 1920×1080 with per-axis anchoring k (0 left/bottom, 0.5 centre, 1 right/top).</summary>
        private static void PlaceK(RectTransform rt, Rect r, float kx, float ky, Vector2? pivot = null)
        {
            Place(rt, K(kx, r.xMin - kx * 1920f), K(ky, r.yMin - ky * 1080f), K(kx, r.xMax - kx * 1920f), K(ky, r.yMax - ky * 1080f), pivot);
        }

        private static void ScaleModel(Canvas cv, float minRefWidth)
        {
            var sm = Ensure<UiScaleModel>(cv.gameObject);
            sm.referenceHeight = 1080f; sm.minRefWidth = minRefWidth;
            var sc = cv.GetComponent<CanvasScaler>();
            if (sc != null) { sc.uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize; sc.scaleFactor = 1f; EditorUtility.SetDirty(sc); }
            EditorUtility.SetDirty(sm);
        }

        private static void Disable(Behaviour b) { if (b != null) { b.enabled = false; EditorUtility.SetDirty(b); } }

        private static void SetField(Object target, string field, Object value)
        {
            var so = new SerializedObject(target);
            var p = so.FindProperty(field);
            if (p == null) throw new System.Exception(target.GetType().Name + "." + field + " not found");
            p.objectReferenceValue = value;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        private static T GetField<T>(Object target, string field) where T : Object
        {
            var so = new SerializedObject(target);
            var p = so.FindProperty(field);
            return p != null ? p.objectReferenceValue as T : null;
        }

        private static void TextStretch(TMP_Text t)
        {
            var r = t.rectTransform; r.anchorMin = Vector2.zero; r.anchorMax = Vector2.one; r.offsetMin = r.offsetMax = Vector2.zero;
            r.localScale = Vector3.one; t.margin = Vector4.zero;
        }

        private static void Listen(Button b, UnityAction call)
        {
            for (int i = b.onClick.GetPersistentEventCount() - 1; i >= 0; i--) UnityEventTools.RemovePersistentListener(b.onClick, i);
            UnityEventTools.AddPersistentListener(b.onClick, call);
            EditorUtility.SetDirty(b);
        }

        // ── sprites shared by the screens ────────────────────────────────────

        public class Sprites
        {
            public Sprite row, rowSel, field, light, scroll, chevrons, cross, track, trackFill;
        }

        public static Sprites MakeSprites()
        {
            return new Sprites
            {
                row = Rounded("Row", 14f, new Color32(0x2b, 0x2d, 0x2b, 0xff), new Color32(0x1d, 0x1f, 0x1d, 0xff), 2f, new Color32(0x4d, 0x50, 0x4d, 0xff)),
                rowSel = Rounded("RowSelected", 14f, new Color32(0x3f, 0x42, 0x3f, 0xff), new Color32(0x2c, 0x2e, 0x2c, 0xff), 3f, new Color32(0xe9, 0xec, 0xe6, 0xff)),
                field = Rounded("Field", 14f, new Color32(0x1b, 0x1d, 0x1b, 0xff), new Color32(0x1b, 0x1d, 0x1b, 0xff), 2f, new Color32(0x4d, 0x50, 0x4d, 0xff)),
                light = Rounded("ButtonLight", 16f, new Color32(0xe9, 0xec, 0xe6, 0xff), new Color32(0xe9, 0xec, 0xe6, 0xff), 0f, Color.clear),
                scroll = Rounded("ScrollbarRound", 3f, Color.white, Color.white, 0f, Color.clear),
                chevrons = Lines("Chevrons", 36, 28, 4f, new Vector4(5, 4, 15, 14), new Vector4(15, 14, 5, 24), new Vector4(19, 4, 29, 14), new Vector4(29, 14, 19, 24)),
                cross = Lines("Cross", 28, 28, 5f, new Vector4(6, 6, 22, 22), new Vector4(22, 6, 6, 22)),
                track = Rounded("LoadingTrack", 14f, new Color32(0x23, 0x25, 0x23, 0xff), new Color32(0x1a, 0x1c, 0x1a, 0xff), 2f, new Color32(0x5f, 0x62, 0x5f, 0xff)),
                trackFill = Rounded("LoadingFill", 10f, new Color32(0xf2, 0xf4, 0xef, 0xff), new Color32(0xbf, 0xc4, 0xbc, 0xff), 0f, Color.clear),
            };
        }

        // ── frame layouts (reference px, see REVISION-3.md) ──────────────────

        private static Box B(C x0, C y0, C x1, C y1) => new Box(x0, y0, x1, y1);

        private static void Ring(Spec s)
        {
            s.All(s.P("frameTop"), B(L(-OFF), R(-BAR), R(OFF), R(OFF)));
            s.All(s.P("frameBottom"), B(L(-OFF), L(-OFF), R(OFF), L(BAR)));
            s.All(s.P("frameLeft"), B(L(-OFF), L(-OFF), L(BAR), R(OFF)));
            s.All(s.P("frameRight"), B(R(-BAR), L(-OFF), R(OFF), R(OFF)));
        }

        public static Spec HudSpec()
        {
            var s = new Spec { name = "HudFrame", initialState = "Closed" };
            s.states.Add("Closed"); s.states.Add("Open");
            float topC = 1080f - 572f, topO = 1080f - 575f, statusBot = 1080f - 938f;
            s.All(s.P("strip"), B(L(-OFF), L(-OFF), R(OFF), L(BAR)));
            var rim = s.P("statusRim");
            rim.boxes["Closed"] = B(L(-OFF), L(-OFF), L(13f), L(topC));
            rim.boxes["Open"] = B(L(-OFF), L(-OFF), L(13f), L(topO));
            s.All(s.P("beta"), B(L(-OFF), L(-OFF), L(336f), L(1080f - 1030f)));
            var left = s.P("status");
            left.boxes["Closed"] = B(L(-OFF), L(statusBot), L(100f), L(topC));
            left.boxes["Open"] = B(L(-OFF), L(statusBot), L(568f), L(topO));
            s.All(s.P("hotbar"), B(M(-360f), L(-OFF), M(360f), L(10f + 84f + 10f)));
            var stem = s.P("stem"); var craft = s.P("crafting"); var outp = s.P("output"); var right = s.P("inventory");
            stem.boxes["Closed"] = B(L(-OFF), L(topC), L(0f), L(topC));
            craft.boxes["Closed"] = B(L(-OFF), L(topC), L(0f), L(topC));
            outp.boxes["Closed"] = B(L(-OFF), L(topC), L(0f), L(topC));
            right.boxes["Closed"] = B(R(-252f), L(-OFF), R(OFF), L(252f));
            // pieces overlap their neighbours by at least the corner radius, so no inner corner shows
            stem.boxes["Open"] = B(L(-OFF), L(topO - 20f), L(14f), R(-390f));
            craft.boxes["Open"] = B(L(-OFF), R(-418f), L(279f), R(-140f));
            outp.boxes["Open"] = B(L(255f), R(-331f), L(456f), R(-227f));
            right.boxes["Open"] = B(R(-462f), L(-OFF), R(OFF), L(1080f - 91f));
            return s;
        }

        public static Spec PauseSpec(float settingsW)
        {
            var s = new Spec { name = "PauseFrame", initialState = "Default" };
            s.states.Add("Default");
            Ring(s);
            s.All(s.P("logo"), B(L(-OFF), R(-104f), L(104f), R(OFF)));
            s.All(s.P("settingsTab"), B(R(-(64f + settingsW)), R(-56f), R(OFF), R(OFF)).Fit(UiFrame.FitSide.Left, "settings", 64f, settingsW));
            s.All(s.P("side"), B(L(-OFF), L(1080f - 908f), L(112f), L(1080f - 652f)));
            s.All(s.P("beta"), B(L(-OFF), L(-OFF), L(336f), L(50f)));
            s.All(s.P("brand"), B(R(-220f), L(-OFF), R(OFF), L(50f)));
            return s;
        }

        public static Spec LoadingSpec()
        {
            var s = new Spec { name = "LoadingFrame", initialState = "Default" };
            s.states.Add("Default");
            Ring(s);
            s.All(s.P("progressTab"), B(M(-600f), L(-OFF), M(600f), L(176f)));
            return s;
        }

        public static Spec MenuSpec(float headW, float newWorldW, float backW)
        {
            var s = new Spec { name = "MainMenuFrame", initialState = "Menu" };
            s.states.Add("Menu"); s.states.Add("Worlds"); s.states.Add("Create");
            s.guides.Add(new Guide { name = "PanelGuide", left = 110f, right = 220f, maxWidth = 1700f, box = B(L(0), L(104f), L(0), R(-104f)) });
            Ring(s);
            s.All(s.P("beta"), B(L(-OFF), L(-OFF), L(336f), L(50f)));
            s.All(s.P("brand"), B(R(-220f), L(-OFF), R(OFF), L(50f)));
            var logo = s.P("logo"); var side = s.P("side"); var head = s.P("heading"); var tr = s.P("links");
            var panel = s.P("panel", "PanelGuide"); var tab = s.P("panelTab", "PanelGuide");
            logo.boxes["Menu"] = B(L(-OFF), R(-104f), L(104f), R(OFF));
            side.boxes["Menu"] = B(L(-OFF), L(1080f - 908f), L(112f), L(1080f - 652f));
            head.boxes["Menu"] = B(L(-OFF), R(-104f), L(104f), R(OFF));
            tr.boxes["Menu"] = B(R(-568f), R(-56f), R(OFF), R(OFF));
            panel.boxes["Menu"] = B(L(0f), L(0f), L(0f), R(0f));
            tab.boxes["Menu"] = B(L(0f), R(-366f), L(0f), R(-66f));
            foreach (var st in new[] { "Worlds", "Create" })
            {
                logo.boxes[st] = B(L(-OFF), R(-104f), L(0f), R(OFF));
                side.boxes[st] = B(L(-OFF), L(1080f - 908f), L(0f), L(1080f - 652f));
                head.boxes[st] = B(L(-OFF), R(-104f), L(0f), R(OFF)).Fit(UiFrame.FitSide.Right, "heading", 56f, headW);
                panel.boxes[st] = B(L(0f), L(0f), R(0f), R(0f));
                tab.boxes[st] = B(R(-24f), R(-366f), R(96f), R(-66f));
                float w = st == "Worlds" ? newWorldW : backW;
                tr.boxes[st] = B(R(-(64f + w)), R(-56f), R(OFF), R(OFF)).Fit(UiFrame.FitSide.Left, st == "Worlds" ? "newWorld" : "backToWorlds", 64f, w);
            }
            return s;
        }

        private static readonly Vector2[] ProbeSizes = { new Vector2(1920f, 1080f), new Vector2(1728f, 1080f), new Vector2(2580f, 1080f), new Vector2(1888f, 1180f) };

        // ── HUD + inventory (Game scene) ─────────────────────────────────────

        public static void AuthorHud(FrameStyle style, Sprites sp)
        {
            var root = Object.FindFirstObjectByType<InventoryRoot>(FindObjectsInactive.Include);
            if (root == null) throw new System.Exception("InventoryRoot not found");
            var invRoot = root.transform;
            var cv = root.GetComponentInParent<Canvas>(true).rootCanvas;
            UseCanvas(root);
            ScaleModel(cv, 1888f);
            CheckCanvas(cv);

            var bg = invRoot.Find("Backgrounds");
            if (bg != null)
            {
                foreach (var img in bg.GetComponentsInChildren<Image>(true)) Disable(img);
                foreach (var an in bg.GetComponentsInChildren<Animator>(true)) Disable(an);
            }
            var hotGrid = FindDeep(invRoot, "HotbarSlots")?.GetComponent<GridLayoutGroup>();
            var craftGrid = FindDeep(invRoot, "CraftingSlots")?.GetComponent<GridLayoutGroup>();
            var invGrid = FindDeep(invRoot, "InventorySlots")?.GetComponent<GridLayoutGroup>();
            var outSlot = FindDeep(invRoot, "CraftingOutputSlot") as RectTransform;
            var statusBar = FindDeep(invRoot, "StatsusBar") as RectTransform;
            var bars = FindDeep(invRoot, "StatsBars");
            var healthBar = bars != null ? FindDeep(bars, "HealthBar") as RectTransform : null;
            var crafting = FindDeep(invRoot, "Crafting");
            var invPanel = invGrid != null ? invGrid.transform.parent : null;
            var desc = FindDeep(invRoot, "StatsDescription");
            if (hotGrid == null || craftGrid == null || invGrid == null || outSlot == null || statusBar == null || healthBar == null || crafting == null || desc == null)
                throw new System.Exception("HUD objects missing");

            foreach (var t in new[] { crafting, invPanel, desc, invGrid.transform })
                foreach (var an in t.GetComponents<Animator>()) Disable(an);

            // StatsDescription was a plain Transform – give it a RectTransform so its labels can anchor
            if (!(desc is RectTransform))
            {
                var go = desc.gameObject;
                go.AddComponent<RectTransform>();
                desc = go.transform;
            }
            var descRT = (RectTransform)desc;
            // it keeps its place inside the status bar (anchored bottom-left); the labels are placed relative to it
            descRT.sizeDelta = Vector2.zero;

            var groups = new List<CanvasGroup>();
            foreach (var t in new[] { crafting, invPanel, desc })
            {
                var g = Ensure<CanvasGroup>(t.gameObject);
                g.alpha = 1f; g.blocksRaycasts = true; g.interactable = true;   // saved visible for editing; HudController hides on start
                groups.Add(g);
            }

            // fonts
            foreach (var t in invRoot.GetComponentsInChildren<TMP_Text>(true)) SetMono(t, true);
            var bv = Object.FindFirstObjectByType<GameVersionText>(FindObjectsInactive.Include);
            var buildVersion = bv != null ? (RectTransform)bv.transform : null;
            if (buildVersion != null) { var t = buildVersion.GetComponent<TMP_Text>(); SetMono(t, true, 34f); t.alignment = TextAlignmentOptions.MidlineLeft; }
            var compass = Object.FindFirstObjectByType<Orivilon.UI.HUD.CompassUI>(FindObjectsInactive.Include);
            if (compass != null)
            {
                foreach (var t in compass.GetComponentsInChildren<TMP_Text>(true)) SetMono(t, true);
                var so = new SerializedObject(compass); var p = so.FindProperty("labelFont");
                if (p != null) { p.objectReferenceValue = MonoBold; so.ApplyModifiedPropertiesWithoutUndo(); }
            }
            var cross = Object.FindFirstObjectByType<Orivilon.UI.HUD.CrosshairUI>(FindObjectsInactive.Include);
            if (cross != null) foreach (var t in cross.GetComponentsInChildren<TMP_Text>(true)) SetMono(t, true);

            // crafting arrow
            var arrowT = crafting.Find("CraftingArrow");
            var arrow = arrowT != null ? arrowT.GetComponent<Image>() : NewImage("CraftingArrow", crafting, sp.chevrons, Ink, false);
            arrow.sprite = sp.chevrons; arrow.color = Ink; arrow.preserveAspect = true;

            const float Slot = 84f;
            Canvas.ForceUpdateCanvases();
            // hotbar: pitch 88, centred, 10 px above the bottom
            Pitch(hotGrid, 88f);
            {
                float gw = 8f * Slot + 7f * 4f;
                MoveFirstTo(hotGrid, new Vector2(960f - gw * 0.5f + Slot * 0.5f, 10f + Slot * 0.5f), 0.5f, 0f);
            }
            Pitch(craftGrid, 87f);
            MoveFirstTo(craftGrid, new Vector2(9f + Slot * 0.5f, 1080f - 150f - Slot * 0.5f), 0f, 1f);
            {
                Rect r = Real(outSlot);
                Vector2 c = new Vector2(360f + Slot * 0.5f, 1080f - 237f - Slot * 0.5f);
                PlaceK(outSlot, new Rect(c - r.size * 0.5f, r.size), 0f, 1f);
            }
            {
                float ax = (9f + 2f * 87f + Slot + 360f) * 0.5f;
                PlaceK(arrow.rectTransform, new Rect(ax - 18f, 1080f - 279f - 14f, 36f, 28f), 0f, 1f, Vector2.zero);
            }
            Pitch(invGrid, 87f);
            {
                float gw = 5f * Slot + 4f * 3f, gh = 11f * Slot + 10f * 3f;
                float x0 = 1920f - 462f + (462f - gw) * 0.5f;
                float top = 1080f - 91f, bottom = BAR;
                float yTop = bottom + (top - bottom - gh) * 0.5f + gh;
                MoveFirstTo(invGrid, new Vector2(x0 + Slot * 0.5f, yTop - Slot * 0.5f), 1f, 0f);
            }
            {
                Rect hb = Real(healthBar), sb = Real(statusBar);
                Vector2 d = new Vector2(3f + 39.5f, 1080f - 584f - 39.5f) - hb.center;
                PlaceK(statusBar, new Rect(sb.position + d, sb.size), 0f, 0f);
            }
            float[] fillY = { 584f, 672f, 760f, 847f };
            string[] names = { "Health", "Hunger", "Thirst", "Stamina" };
            for (int i = 0; i < 4; i++)
            {
                float cy = 1080f - fillY[i] - 39.5f;
                var l = desc.Find(names[i])?.GetComponent<TMP_Text>();
                var v = l != null ? l.transform.Find(names[i] + "Value")?.GetComponent<TMP_Text>() : null;
                if (l != null)
                {
                    SetMono(l, true, 40f); l.alignment = TextAlignmentOptions.MidlineLeft; l.textWrappingMode = TextWrappingModes.NoWrap;
                    l.text = l.text.Trim(); l.color = Color.white;
                    l.rectTransform.localScale = Vector3.one;
                    PlaceK(l.rectTransform, new Rect(114f, cy - 28f, 280f, 56f), 0f, 0f, Vector2.zero);
                }
                if (v != null)
                {
                    SetMono(v, true, 40f); v.alignment = TextAlignmentOptions.MidlineRight; v.textWrappingMode = TextWrappingModes.NoWrap;
                    v.rectTransform.localScale = Vector3.one;
                    PlaceK(v.rectTransform, new Rect(408f, cy - 28f, 120f, 56f), 0f, 0f, Vector2.zero);
                }
            }
            var mm = Object.FindFirstObjectByType<Orivilon.UI.HUD.MinimapUI>(FindObjectsInactive.Include);
            if (mm != null) PlaceK((RectTransform)mm.transform, new Rect(1920f - 236f, 16f, 220f, 220f), 1f, 0f);
            if (buildVersion != null) PlaceK(buildVersion, new Rect(18f, 0f, 318f, 50f), 0f, 0f, Vector2.zero);

            // frame (sibling before the inventory root so slots draw on top)
            var spec = HudSpec();
            var prefab = BuildPrefab(spec, style, PrefabDir + "/Frames/HudFrame.prefab", ProbeSizes[0], ProbeSizes[1], ProbeSizes[2], ProbeSizes[3]);
            var old = invRoot.parent.Find("HudFrame");
            if (old != null) Object.DestroyImmediate(old.gameObject);
            var frame = Instantiate(prefab, invRoot.parent, invRoot.GetSiblingIndex(), spec, null);
            var fg = Ensure<CanvasGroup>(frame.gameObject);
            fg.blocksRaycasts = false; fg.interactable = false;
            frame.ApplyInstant(frame.Find("Open"));
            PrefabUtility.RecordPrefabInstancePropertyModifications(frame.transform);

            var hc = Ensure<HudController>(cv.gameObject);
            hc.frame = frame; hc.frameGroup = fg; hc.inventoryRoot = root.gameObject;
            hc.inventoryRootGroup = root.GetComponent<CanvasGroup>();
            hc.inventoryGroups = groups.ToArray();
            EditorUtility.SetDirty(hc);
        }

        private static void Pitch(GridLayoutGroup g, float pitch)
        {
            var first = FirstActiveChild(g.transform);
            LayoutRebuilder.ForceRebuildLayoutImmediate((RectTransform)g.transform);
            float k = Real(first).width / Mathf.Max(1e-3f, first.rect.width);
            g.spacing = new Vector2(pitch / k - g.cellSize.x, pitch / k - g.cellSize.y);
            EditorUtility.SetDirty(g);
            LayoutRebuilder.ForceRebuildLayoutImmediate((RectTransform)g.transform);
            Canvas.ForceUpdateCanvases();
        }

        /// <summary>Moves a grid so its first slot's centre lands on target, anchored by kx/ky.</summary>
        private static void MoveFirstTo(GridLayoutGroup g, Vector2 target, float kx, float ky)
        {
            var rt = (RectTransform)g.transform;
            var first = FirstActiveChild(g.transform);
            Rect gr = Real(rt), fr = Real(first);
            Vector2 d = target - fr.center;
            PlaceK(rt, new Rect(gr.position + d, gr.size), kx, ky);
            Canvas.ForceUpdateCanvases();
            LayoutRebuilder.ForceRebuildLayoutImmediate(rt);
        }

        // ── Pause ────────────────────────────────────────────────────────────

        public static void AuthorPause(FrameStyle style)
        {
            var pm = Object.FindFirstObjectByType<PauseMenuUI>(FindObjectsInactive.Include);
            if (pm == null) throw new System.Exception("PauseMenuUI not found");
            UseCanvas(pm);
            var t = pm.transform;
            foreach (var n in new[] { "Border", "BorderNoGlow" }) { var c = t.Find(n); if (c != null) Disable(c.GetComponent<Image>()); }
            RectTransform settings = null;
            for (int i = 0; i < t.childCount; i++) { var c = t.GetChild(i); if (c.name == "Settings" && c.gameObject.activeSelf) { settings = (RectTransform)c; break; } }
            if (settings == null) throw new System.Exception("Pause Settings button not found");
            var st = settings.GetComponentInChildren<TMP_Text>(true);
            foreach (var x in t.GetComponentsInChildren<TMP_Text>(true))
                if (x.GetComponentInParent<Orivilon.UI.Settings.SettingsMenuUI>(true) == null) SetMono(x, true);
            SetMono(st, true, 25f); st.alignment = TextAlignmentOptions.Center; st.textWrappingMode = TextWrappingModes.NoWrap;
            TextStretch(st);
            float tw = Mathf.Ceil(st.GetPreferredValues(st.text, 10000f, 1000f).x);
            settings.localScale = Vector3.one;
            Place(settings, R(-32f - tw), R(-50f), R(-32f), R(-14f), new Vector2(1f, 0f));
            var fit = Ensure<FitToText>(settings.gameObject); fit.text = st; fit.padding = 0f; fit.Apply();
            var o = t.Find("Orivilon")?.GetComponent<TMP_Text>();
            if (o != null) { SetMono(o, true, 30f); o.alignment = TextAlignmentOptions.MidlineRight; Place(o.rectTransform, R(-212f), L(0f), R(-16f), L(50f), Vector2.zero); }

            var spec = PauseSpec(tw);
            var prefab = BuildPrefab(spec, style, PrefabDir + "/Frames/PauseFrame.prefab", ProbeSizes[0], ProbeSizes[1], ProbeSizes[2], ProbeSizes[3]);
            var old = t.Find("PauseFrame"); if (old != null) Object.DestroyImmediate(old.gameObject);
            var frame = Instantiate(prefab, t, 0, spec, new Dictionary<string, RectTransform> { ["settings"] = settings });
            var pc = Ensure<PauseController>(pm.gameObject);
            pc.frame = frame;
            var sbg = t.Find("SettingsBackground");
            pc.settingsBackground = sbg != null ? sbg.GetComponent<Image>() : null;
            EditorUtility.SetDirty(pc);
        }

        // ── Loading screen ───────────────────────────────────────────────────

        public static void AuthorLoading(FrameStyle style, Sprites sp, Texture blurredMenu)
        {
            var lm = Object.FindFirstObjectByType<LoadingScreenManager>(FindObjectsInactive.Include);
            if (lm == null || lm.loadingPanel == null) throw new System.Exception("LoadingScreenManager not found");
            UseCanvas(lm);
            var cv = canvasRT.GetComponent<Canvas>();
            ScaleModel(cv, 0f);
            CheckCanvas(cv);
            var panel = (RectTransform)lm.loadingPanel.transform;
            var pimg = panel.GetComponent<Image>(); if (pimg != null) { pimg.color = Color.black; EditorUtility.SetDirty(pimg); }

            var bgT = panel.Find("Background") as RectTransform;
            var bg = bgT != null ? bgT.GetComponent<RawImage>() : null;
            if (bg == null) { bgT = NewRect("Background", panel); bg = bgT.gameObject.AddComponent<RawImage>(); }
            bgT.SetSiblingIndex(0); Stretch(bgT);
            bg.raycastTarget = false; bg.texture = blurredMenu;
            bg.color = blurredMenu != null ? new Color(0.25f, 0.25f, 0.25f, 1f) : Color.black;
            var ar = Ensure<AspectRatioFitter>(bgT.gameObject); ar.aspectMode = AspectRatioFitter.AspectMode.EnvelopeParent; ar.aspectRatio = 16f / 9f;

            var title = panel.Find("Title")?.GetComponent<TMP_Text>() ?? NewText("Title", panel, "EVERLOST", true, 96f, new Color(1f, 1f, 1f, 0.92f), TextAlignmentOptions.Center);
            SetHeading(title, 96f);
            Place(title.rectTransform, M(-450f), R(-440f), M(450f), R(-330f), Vector2.zero);
            var world = panel.Find("WorldName")?.GetComponent<TMP_Text>() ?? NewText("WorldName", panel, "World name", false, 26f, Muted, TextAlignmentOptions.Center);
            world.overflowMode = TextOverflowModes.Ellipsis;
            Place(world.rectTransform, M(-300f), R(-490f), M(300f), R(-446f), Vector2.zero);

            foreach (var t in panel.GetComponentsInChildren<TMP_Text>(true)) if (t != title) SetMono(t, true);
            if (lm.statusText != null)
            {
                var s = lm.statusText;
                s.alignment = TextAlignmentOptions.MidlineLeft; s.enableAutoSizing = false; s.fontSize = 28f; s.color = Color.white; s.textWrappingMode = TextWrappingModes.NoWrap;
                s.rectTransform.localScale = Vector3.one;
                Place(s.rectTransform, M(-530f), L(106f), M(330f), L(146f), Vector2.zero);
            }
            if (lm.progressText != null)
            {
                var s = lm.progressText;
                s.alignment = TextAlignmentOptions.MidlineRight; s.enableAutoSizing = false; s.fontSize = 44f; s.color = Color.white; s.textWrappingMode = TextWrappingModes.NoWrap;
                s.rectTransform.localScale = Vector3.one;
                Place(s.rectTransform, M(330f), L(100f), M(530f), L(154f), Vector2.zero);
            }
            var lb = lm.loadingBarFill != null ? lm.loadingBarFill.transform.parent : null;
            if (lb != null) foreach (var g in lb.GetComponents<Graphic>()) Disable(g);
            if (lm.loadingBarFill != null) Disable(lm.loadingBarFill);

            var track = panel.Find("ProgressTrack")?.GetComponent<Image>() ?? NewImage("ProgressTrack", panel, sp.track, Color.white, true);
            track.sprite = sp.track; track.type = Image.Type.Sliced;
            Place(track.rectTransform, M(-530f), L(42f), M(530f), L(82f), Vector2.zero);
            var fill = track.transform.Find("Fill")?.GetComponent<Image>() ?? NewImage("Fill", track.transform, sp.trackFill, Color.white, true);
            fill.sprite = sp.trackFill; fill.type = Image.Type.Sliced;
            var fr = fill.rectTransform;
            fr.anchorMin = Vector2.zero; fr.anchorMax = new Vector2(0f, 1f); fr.pivot = new Vector2(0f, 0.5f);
            fr.sizeDelta = new Vector2((1060f - 10f) * 0.6f, -10f); fr.anchoredPosition = new Vector2(5f, 0f);   // 60 % preview

            var spec = LoadingSpec();
            var prefab = BuildPrefab(spec, style, PrefabDir + "/Frames/LoadingFrame.prefab", ProbeSizes[0], ProbeSizes[1], ProbeSizes[2]);
            var old = panel.Find("LoadingFrame"); if (old != null) Object.DestroyImmediate(old.gameObject);
            Instantiate(prefab, panel, 1, spec, null);

            var lc = Ensure<LoadingController>(lm.gameObject);
            lc.manager = lm; lc.worldName = world; lc.track = track.rectTransform; lc.fill = fr;
            EditorUtility.SetDirty(lc);
        }

        // ── Main menu ────────────────────────────────────────────────────────

        public static Texture2D SaveBlurredMenu(RawImage src)
        {
            if (src == null || src.texture == null) return null;
            var tex = src.texture;
            int w = Mathf.Max(16, tex.width / 16), h = Mathf.Max(9, tex.height / 16);
            var mid = RenderTexture.GetTemporary(tex.width / 4, tex.height / 4, 0);
            var small = RenderTexture.GetTemporary(w, h, 0);
            mid.filterMode = small.filterMode = FilterMode.Bilinear;
            Graphics.Blit(tex, mid); Graphics.Blit(mid, small);
            var prev = RenderTexture.active; RenderTexture.active = small;
            var outTex = new Texture2D(w, h, TextureFormat.RGBA32, false);
            outTex.ReadPixels(new Rect(0, 0, w, h), 0, 0); outTex.Apply();
            RenderTexture.active = prev;
            RenderTexture.ReleaseTemporary(mid); RenderTexture.ReleaseTemporary(small);
            EnsureFolder(SpriteDir);
            string path = SpriteDir + "/MenuBackgroundBlurred.png";
            System.IO.File.WriteAllBytes(path, outTex.EncodeToPNG());
            Object.DestroyImmediate(outTex);
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
            var imp = (TextureImporter)AssetImporter.GetAtPath(path);
            imp.textureType = TextureImporterType.Default; imp.mipmapEnabled = false; imp.wrapMode = TextureWrapMode.Clamp;
            imp.filterMode = FilterMode.Bilinear; imp.textureCompression = TextureImporterCompression.Uncompressed; imp.npotScale = TextureImporterNPOTScale.None;
            imp.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        public static void AuthorMenu(FrameStyle style, Sprites sp)
        {
            var ui = Object.FindFirstObjectByType<MainMenuUI>(FindObjectsInactive.Include);
            if (ui == null) throw new System.Exception("MainMenuUI not found");
            UseCanvas(ui);
            var cv = canvasRT.GetComponent<Canvas>();
            ScaleModel(cv, 0f);
            CheckCanvas(cv);
            Transform c = canvasRT;
            foreach (var n in new[] { "Border", "BorderNoGlow", "BorderHearder", "PlayMenuBackground" })
            {
                var t = c.Find(n);
                if (t != null) foreach (var g in t.GetComponents<Graphic>()) Disable(g);
            }
            var bvt = c.Find("BuildVersion")?.GetComponent<TMP_Text>();
            var ot = c.Find("Orivilon")?.GetComponent<TMP_Text>();
            var mainBg = c.Find("MainBackground");

            var mm = c.Find("MainMenu");
            var menuGroup = mm != null ? Ensure<CanvasGroup>(mm.gameObject) : null;

            var header = GetField<TMP_Text>(ui, "menuHeaderText");
            Disable(GetField<Animator>(ui, "headerAnimator"));
            Disable(GetField<TMP_Text>(ui, "worldStatsText"));

            var sv = ui.worldScrollView.transform;
            var viewport = (RectTransform)sv.Find("Viewport");
            var content = (RectTransform)ui.listContent;
            var scrollbar = (RectTransform)sv.Find("Scrollbar Vertical");
            var newWorldBtn = (RectTransform)sv.Find("NewWorlButton");
            var newWorldText = newWorldBtn.GetComponentInChildren<TMP_Text>(true);
            var back = (RectTransform)ui.transform.Find("BackButton");
            var spb = GetField<Button>(ui, "singleplayerButton"); var mpb = GetField<Button>(ui, "multiplayerButton");
            var createMenu = (RectTransform)ui.createMenu.transform;

            // heading: game font, fitted to its text
            SetHeading(header, 40f); header.alignment = TextAlignmentOptions.MidlineLeft; header.textWrappingMode = TextWrappingModes.NoWrap;
            header.rectTransform.localScale = Vector3.one;
            Place(header.rectTransform, L(28f), R(-76f), L(328f), R(-30f), Vector2.zero);
            var hfit = Ensure<FitToText>(header.gameObject); hfit.text = header; hfit.padding = 0f; hfit.Apply();

            foreach (var t in canvasRT.GetComponentsInChildren<TMP_Text>(true))
            {
                if (t == header || t.GetComponentInParent<Orivilon.UI.Settings.SettingsMenuUI>(true) != null || t.GetComponentInParent<FriendsListUI>(true) != null) continue;
                if (t.name == "Everlost") continue;
                SetMono(t, true);
                if (t.name == "SettingsText" || t.name == "DiscordText" || t.name == "WebsiteText") t.fontSize = 25f;
            }
            if (bvt != null) { SetMono(bvt, true, 34f); bvt.alignment = TextAlignmentOptions.MidlineLeft; Place(bvt.rectTransform, L(18f), L(0f), L(336f), L(50f), Vector2.zero); }
            if (ot != null) { SetMono(ot, true, 30f); ot.alignment = TextAlignmentOptions.MidlineRight; Place(ot.rectTransform, R(-212f), L(0f), R(-16f), L(50f), Vector2.zero); }

            // back: white cross
            var bimg = back.GetComponent<Image>();
            bimg.sprite = sp.cross; bimg.color = Ink; bimg.preserveAspect = true; bimg.type = Image.Type.Simple;
            back.localScale = Vector3.one;

            // world list
            viewport.localScale = Vector3.one;
            var vm = viewport.GetComponent<RectMask2D>();
            if (vm != null) { vm.padding = Vector4.zero; vm.softness = Vector2Int.zero; EditorUtility.SetDirty(vm); }
            var vlg = content.GetComponent<VerticalLayoutGroup>();
            if (vlg != null)
            {
                vlg.spacing = 16f; vlg.padding = new RectOffset(0, 0, 0, 0); vlg.childAlignment = TextAnchor.UpperLeft;
                vlg.childControlWidth = true; vlg.childControlHeight = true; vlg.childForceExpandWidth = true; vlg.childForceExpandHeight = false;
                EditorUtility.SetDirty(vlg);
            }
            foreach (var n in new[] { "TopSpace", "BottomSpace" }) { var t = content.Find(n); if (t != null) t.gameObject.SetActive(false); }
            var sbImg = scrollbar.GetComponent<Image>();
            sbImg.sprite = sp.scroll; sbImg.type = Image.Type.Sliced; sbImg.color = new Color32(0x2a, 0x2c, 0x2a, 0xff);
            var area = scrollbar.Find("Sliding Area") as RectTransform;
            if (area != null)
            {
                area.offsetMin = area.offsetMax = Vector2.zero;
                var hd = area.Find("Handle") as RectTransform;
                if (hd != null) { hd.sizeDelta = Vector2.zero; var hi = hd.GetComponent<Image>(); hi.sprite = sp.scroll; hi.type = Image.Type.Sliced; hi.color = Muted; }
            }

            // "Create New World" link, fitted
            newWorldText.text = newWorldText.text.Trim(); SetMono(newWorldText, true, 25f);
            newWorldText.alignment = TextAlignmentOptions.Center; TextStretch(newWorldText);
            float nw = Mathf.Ceil(newWorldText.GetPreferredValues(newWorldText.text, 10000f, 1000f).x);
            newWorldBtn.localScale = Vector3.one;
            Place(newWorldBtn, R(-32f - nw), R(-50f), R(-32f), R(-14f), new Vector2(1f, 0f));
            var nfit = Ensure<FitToText>(newWorldBtn.gameObject); nfit.text = newWorldText; nfit.Apply();

            // panel guides (max 1700 px wide on ultrawide)
            var worldsPanel = sv.Find("WorldsPanel") as RectTransform ?? NewRect("WorldsPanel", sv);
            worldsPanel.anchorMin = new Vector2(0f, 0f); worldsPanel.anchorMax = new Vector2(0f, 1f);
            worldsPanel.offsetMin = new Vector2(0f, 104f); worldsPanel.offsetMax = new Vector2(0f, -104f);
            var wf = Ensure<MaxWidthFitter>(worldsPanel.gameObject); wf.left = 110f; wf.right = 220f; wf.maxWidth = 1700f; wf.Apply();
            // the ScrollRect object itself spans the canvas
            var svRT = (RectTransform)sv; Stretch(svRT); svRT.localScale = Vector3.one;
            viewport.SetParent(worldsPanel, true); scrollbar.SetParent(worldsPanel, true);
            var sideGuide = ui.transform.Find("SideGuide") as RectTransform ?? NewRect("SideGuide", ui.transform);
            sideGuide.anchorMin = new Vector2(0f, 0f); sideGuide.anchorMax = new Vector2(0f, 1f);
            sideGuide.offsetMin = new Vector2(0f, 104f); sideGuide.offsetMax = new Vector2(0f, -104f);
            var sf = Ensure<MaxWidthFitter>(sideGuide.gameObject); sf.left = 110f; sf.right = 220f; sf.maxWidth = 1700f; sf.Apply();
            foreach (var b in new Transform[] { spb.transform, mpb.transform, back }) b.SetParent(sideGuide, true);
            Canvas.ForceUpdateCanvases();

            // world list column: 46 % of the panel; details in the rest
            const float k = 0.46f;
            C XL(float a) => K(k, 110f - k * 330f + a);   // x relative to 110 + 0.46 * panel width (the list column's edge)
            Place(viewport, L(150f), L(144f), XL(26f), R(-144f), Vector2.zero);
            Place(scrollbar, XL(34f), L(144f), XL(40f), R(-144f), Vector2.zero);
            var detail = worldsPanel.Find("Detail") as RectTransform ?? NewRect("Detail", worldsPanel);
            Stretch(detail);
            var sepImg = detail.Find("Separator")?.GetComponent<Image>() ?? NewImage("Separator", detail, null, new Color32(0x3a, 0x3c, 0x3a, 0xff), false);
            Place(sepImg.rectTransform, XL(64f), L(144f), XL(66f), R(-144f), Vector2.zero);
            var dLabel = detail.Find("Label")?.GetComponent<TMP_Text>() ?? NewText("Label", detail, "SELECTED WORLD", false, 16f, Muted, TextAlignmentOptions.TopLeft);
            dLabel.characterSpacing = 8f;
            Place(dLabel.rectTransform, XL(90f), R(-172f), R(-260f), R(-152f), Vector2.zero);
            var dName = detail.Find("Name")?.GetComponent<TMP_Text>() ?? NewText("Name", detail, "Select a world", true, 44f, Color.white, TextAlignmentOptions.TopLeft);
            dName.textWrappingMode = TextWrappingModes.Normal; dName.overflowMode = TextOverflowModes.Ellipsis; dName.maxVisibleLines = 2;
            Place(dName.rectTransform, XL(90f), R(-284f), R(-260f), R(-184f), Vector2.zero);
            string[] keys = { "Seed", "Last played", "Play time" };
            var dKeys = new TMP_Text[3]; var dVals = new TMP_Text[3];
            for (int i = 0; i < 3; i++)
            {
                float ty = -(334f + i * 64f);
                dKeys[i] = detail.Find("Key" + i)?.GetComponent<TMP_Text>() ?? NewText("Key" + i, detail, keys[i], false, 20f, Muted, TextAlignmentOptions.MidlineLeft);
                dVals[i] = detail.Find("Value" + i)?.GetComponent<TMP_Text>() ?? NewText("Value" + i, detail, "", false, 26f, Color.white, TextAlignmentOptions.MidlineLeft);
                dVals[i].overflowMode = TextOverflowModes.Ellipsis;
                Place(dKeys[i].rectTransform, XL(90f), R(ty - 30f), XL(340f), R(ty), Vector2.zero);
                Place(dVals[i].rectTransform, XL(350f), R(ty - 36f), R(-260f), R(ty + 6f), Vector2.zero);
            }
            var playImg = detail.Find("PlayButton")?.GetComponent<Image>() ?? NewImage("PlayButton", detail, sp.light, Color.white, true, true);
            playImg.sprite = sp.light; playImg.type = Image.Type.Sliced; playImg.raycastTarget = true;
            var play = Ensure<Button>(playImg.gameObject); play.targetGraphic = playImg;
            Listen(play, ui.PlaySelectedWorldSingleplayer);
            var playText = playImg.transform.Find("Text")?.GetComponent<TMP_Text>() ?? NewText("Text", playImg.transform, "PLAY", false, 34f, Color.black, TextAlignmentOptions.Center);
            TextStretch(playText);
            Place(playImg.rectTransform, XL(90f), L(198f), XL(610f), L(294f), Vector2.zero);

            // side buttons follow the panel's right edge
            Place((RectTransform)spb.transform, R(-206f), R(-260f), R(-136f), R(-190f), Vector2.zero);
            Place((RectTransform)mpb.transform, R(-206f), R(-360f), R(-136f), R(-290f), Vector2.zero);
            Place(back, R(-200f), R(-450f), R(-142f), R(-392f), Vector2.zero);

            // create world
            createMenu.localScale = Vector3.one;
            Stretch(createMenu);
            var cmi = createMenu.GetComponent<Image>(); if (cmi != null) cmi.raycastTarget = false;
            var nameLabel = createMenu.Find("Name") as RectTransform; var nameInput = createMenu.Find("NameInput") as RectTransform;
            var seedLabel = createMenu.Find("Seed") as RectTransform; var seedInput = createMenu.Find("SeedInput") as RectTransform;
            var createButton = createMenu.Find("CreateButton") as RectTransform;
            foreach (var f in new[] { nameInput, seedInput })
            {
                var img = f.GetComponent<Image>();
                img.sprite = sp.field; img.type = Image.Type.Sliced; img.color = Color.white;
                var inp = f.GetComponent<TMP_InputField>();
                if (inp.textComponent != null) { SetMono(inp.textComponent, true, 26f); inp.textComponent.color = Color.white; }
                if (inp.placeholder is TMP_Text ph) { SetMono(ph, false, 26f); ph.fontStyle = FontStyles.Normal; ph.color = new Color32(0x6f, 0x76, 0x6e, 0xff); }
                if (inp.textViewport != null) { inp.textViewport.anchorMin = Vector2.zero; inp.textViewport.anchorMax = Vector2.one; inp.textViewport.offsetMin = new Vector2(22f, 0f); inp.textViewport.offsetMax = new Vector2(-22f, 0f); }
                foreach (var t in new[] { inp.textComponent as TMP_Text, inp.placeholder as TMP_Text })
                    if (t != null) { var r = t.rectTransform; r.anchorMin = Vector2.zero; r.anchorMax = Vector2.one; r.offsetMin = r.offsetMax = Vector2.zero; t.alignment = TextAlignmentOptions.MidlineLeft; t.enableAutoSizing = false; t.fontSize = 26f; }
                f.localScale = Vector3.one;
                EditorUtility.SetDirty(inp);
            }
            foreach (var l in new[] { nameLabel, seedLabel })
            {
                var t = l.GetComponent<TMP_Text>(); SetMono(t, true, 34f); t.alignment = TextAlignmentOptions.BottomLeft;
                Disable(l.GetComponent<Animator>());
                l.localScale = Vector3.one;
            }
            var cimg = createButton.GetComponent<Image>();
            cimg.sprite = sp.light; cimg.type = Image.Type.Sliced; cimg.color = Color.white;
            createButton.localScale = Vector3.one;
            var cl = createButton.Find("Label")?.GetComponent<TMP_Text>() ?? NewText("Label", createButton, "CREATE", false, 34f, Color.black, TextAlignmentOptions.Center);
            TextStretch(cl);
            var backLinkRT = createMenu.Find("BackToWorlds") as RectTransform;
            if (backLinkRT == null)
            {
                backLinkRT = NewRect("BackToWorlds", createMenu);
                var bi = backLinkRT.gameObject.AddComponent<Image>(); bi.color = new Color(0f, 0f, 0f, 0f);
                var bb = backLinkRT.gameObject.AddComponent<Button>(); bb.targetGraphic = bi;
            }
            Listen(backLinkRT.GetComponent<Button>(), ui.OpenSelectWorldMenu);
            var backText = backLinkRT.Find("Text")?.GetComponent<TMP_Text>() ?? NewText("Text", backLinkRT, "Back to worlds", false, 25f, Color.white, TextAlignmentOptions.Center);
            TextStretch(backText);
            float bw = Mathf.Ceil(backText.GetPreferredValues(backText.text, 10000f, 1000f).x);
            Place(backLinkRT, R(-32f - bw), R(-50f), R(-32f), R(-14f), new Vector2(1f, 0f));
            var bfit = Ensure<FitToText>(backLinkRT.gameObject); bfit.text = backText; bfit.Apply();
            const float fx = 190f;
            Place(nameLabel, L(fx), R(-218f), L(fx + 600f), R(-174f), Vector2.zero);
            Place(nameInput, L(fx), R(-308f), L(fx + 900f), R(-224f), Vector2.zero);
            Place(seedLabel, L(fx), R(-398f), L(fx + 600f), R(-354f), Vector2.zero);
            Place(seedInput, L(fx), R(-488f), L(fx + 900f), R(-404f), Vector2.zero);
            Place(createButton, L(fx), R(-660f), L(fx + 380f), R(-564f), Vector2.zero);

            // frame
            float hw = Mathf.Ceil(header.GetPreferredValues(header.text, 10000f, 1000f).x);
            var spec = MenuSpec(hw, nw, bw);
            var prefab = BuildPrefab(spec, style, PrefabDir + "/Frames/MainMenuFrame.prefab", ProbeSizes[0], ProbeSizes[1], ProbeSizes[2]);
            var old = c.Find("MainMenuFrame"); if (old != null) Object.DestroyImmediate(old.gameObject);
            var frame = Instantiate(prefab, c, mainBg != null ? mainBg.GetSiblingIndex() + 1 : 0, spec,
                new Dictionary<string, RectTransform> { ["heading"] = header.rectTransform, ["newWorld"] = newWorldBtn, ["backToWorlds"] = backLinkRT });
            string saved = ui.gameObject.activeSelf ? (createMenu.gameObject.activeSelf ? "Create" : "Worlds") : "Menu";
            frame.ApplyInstant(frame.Find(saved));

            var mc = Ensure<MainMenuController>(canvasRT.gameObject);
            mc.ui = ui; mc.frame = frame; mc.menuGroup = menuGroup;
            mc.listContent = content; mc.scroll = sv.GetComponent<ScrollRect>();
            mc.detailName = dName; mc.detailKeys = dKeys; mc.detailValues = dVals; mc.playButton = play; mc.playLabel = playText;
            EditorUtility.SetDirty(mc);
        }

        /// <summary>Styles the world-list row (prefab asset and the scene's template instance).</summary>
        public static void StyleRow(GameObject row, Sprites sp)
        {
            var el = row.GetComponent<WorldListElement>();
            var rt = (RectTransform)row.transform;
            rt.localScale = Vector3.one;
            var le = Ensure<LayoutElement>(row); le.preferredHeight = 76f; le.minHeight = 76f; le.flexibleWidth = 1f;
            var img = GetField<Image>(el, "backgroundImage") ?? row.GetComponent<Image>();
            SetField(el, "normalSprite", sp.row); SetField(el, "selectedSprite", sp.rowSel);
            if (img != null) { img.sprite = sp.row; img.type = Image.Type.Sliced; img.color = Color.white; EditorUtility.SetDirty(img); }
            var name = row.GetComponentInChildren<TMP_Text>(true);
            SetMono(name, false, 28f); name.color = Color.white; name.alignment = TextAlignmentOptions.MidlineLeft;
            name.textWrappingMode = TextWrappingModes.NoWrap; name.overflowMode = TextOverflowModes.Ellipsis; name.enableAutoSizing = false;
            var r = name.rectTransform; r.localScale = Vector3.one; r.anchorMin = Vector2.zero; r.anchorMax = Vector2.one; r.pivot = new Vector2(0.5f, 0.5f);
            r.offsetMin = new Vector2(22f, 0f); r.offsetMax = new Vector2(-170f, 0f);
            var metaT = row.transform.Find("LastPlayed");
            var meta = metaT != null ? metaT.GetComponent<TMP_Text>() : NewText("LastPlayed", row.transform, "Today 12:00", false, 15f, Muted, TextAlignmentOptions.MidlineRight, false);
            var mr = meta.rectTransform; mr.anchorMin = new Vector2(1f, 0f); mr.anchorMax = new Vector2(1f, 1f); mr.pivot = new Vector2(1f, 0.5f);
            mr.sizeDelta = new Vector2(150f, 0f); mr.anchoredPosition = new Vector2(-22f, 0f);
            meta.transform.SetAsLastSibling();
            var wm = Ensure<WorldRowMeta>(row); wm.nameText = name; wm.metaText = meta;
            EditorUtility.SetDirty(row);
        }
    }
}
