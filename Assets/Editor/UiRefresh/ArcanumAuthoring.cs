using System.Collections.Generic;
using Orivilon.EditorTools;
using Orivilon.UI.Arcanum;
using TMPro;
using UnityEditor;
using UnityEditor.Events;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;
using static Orivilon.UI.Refresh.EditorTools.UiAuthoringKit;

namespace Orivilon.UI.Refresh.EditorTools
{
    /// <summary>
    /// Authors the Arcanum panel as prefabs: ArcanumNode, ArcanumCategoryRow and the complete
    /// Resources/UI/ArcanumUI prefab (header, stats, six disciplines, legend, zoom controls,
    /// tooltip / pinned detail, toast and all 181 nodes laid out from the serialized tree asset).
    /// Ported from the former ArcanumUI.Build(); the layout values are identical.
    /// </summary>
    public static class ArcanumAuthoring
    {
        public const string TreeAssetPath = "Assets/Resources/Arcanum/ArcanumTree.asset";
        public const string PrefabPath = "Assets/Resources/UI/ArcanumUI.prefab";
        public const string NodePrefabPath = PrefabDir + "/Arcanum/ArcanumNode.prefab";
        public const string RowPrefabPath = PrefabDir + "/Arcanum/ArcanumCategoryRow.prefab";

        private static readonly Color RowColor = new Color32(0x1a, 0x1d, 0x1a, 0xff);
        private static readonly Color RowBorder = new Color32(0x55, 0x59, 0x55, 0xff);
        private static readonly Color MutedText = new Color32(0xa8, 0xb0, 0xa6, 0xff);
        private static readonly Color DimText = new Color32(0x83, 0x8b, 0x82, 0xff);
        private static readonly Color GoodText = new Color32(0xc9, 0xe9, 0x86, 0xff);
        private static readonly Color AvailableFrame = new Color32(0xe2, 0xe6, 0xdc, 0xff);

        private static Sprite panelSprite, rowSprite, outlineSprite, slotSprite, solidSprite;
        private static TMP_FontAsset titleFont, bodyFont;

        // ── tree data asset ──────────────────────────────────────────────────

        public static ArcanumTreeAsset EnsureTreeAsset()
        {
            var a = AssetDatabase.LoadAssetAtPath<ArcanumTreeAsset>(TreeAssetPath);
            if (a != null && a.nodes != null && a.nodes.Count > 0) return a;
            // Strom v3 se generuje z design/arcanum/data.js, ne z kódu.
            return ArcanumTreeImporter.Build();
        }

        // ── shared art ───────────────────────────────────────────────────────

        private static void Art()
        {
            panelSprite = ArcanumRounded("Arcanum_Panel", 96, 30f, -1f, true);
            rowSprite = ArcanumRounded("Arcanum_Row", 64, 16f, -1f, false);
            outlineSprite = ArcanumRounded("Arcanum_Outline", 64, 16f, 4f, false);
            solidSprite = ArcanumRounded("Arcanum_Solid", 16, 0.01f, -1f, false);
            slotSprite = null;
            foreach (var g in AssetDatabase.FindAssets("inventory_slot_outline"))
            {
                foreach (var o in AssetDatabase.LoadAllAssetsAtPath(AssetDatabase.GUIDToAssetPath(g)))
                    if (o is Sprite s && s.name == "inventory_slot_outline") { slotSprite = s; break; }
                if (slotSprite != null) break;
            }
            if (slotSprite == null) slotSprite = ArcanumRounded("Arcanum_Slot", 64, 14f, -1f, false);
            titleFont = Heading;
            bodyFont = MonoBold;
        }

        private static TextMeshProUGUI Text(string name, Transform parent, string text, TMP_FontAsset font, float size, Color color, TextAlignmentOptions align)
        {
            var rt = NewRect(name, parent);
            var t = rt.gameObject.AddComponent<TextMeshProUGUI>();
            t.font = font; t.fontSharedMaterial = font.material;
            t.text = text; t.fontSize = size; t.color = color; t.alignment = align;
            t.raycastTarget = false; t.richText = true;
            return t;
        }

        private static Image Img(string name, Transform parent, Sprite sprite, Color color)
        {
            var rt = NewRect(name, parent);
            var img = rt.gameObject.AddComponent<Image>();
            img.sprite = sprite; img.color = color;
            return img;
        }

        private static void PlaceTL(RectTransform rt, Vector2 anchor, Vector2 topLeft, Vector2 size)
        {
            rt.anchorMin = rt.anchorMax = anchor; rt.pivot = new Vector2(0, 1); rt.sizeDelta = size; rt.anchoredPosition = topLeft;
        }

        private static void AnchorRow(RectTransform rt, Vector2 leftTop, Vector2 rightTop, float height)
        {
            rt.anchorMin = new Vector2(0, 1); rt.anchorMax = new Vector2(1, 1); rt.pivot = new Vector2(0, 1);
            rt.offsetMin = new Vector2(leftTop.x, leftTop.y - height); rt.offsetMax = new Vector2(rightTop.x, rightTop.y);
        }

        // ── building block prefabs ───────────────────────────────────────────

        private static GameObject NodePrefab()
        {
            var slot = Img("ArcanumNode", null, slotSprite, Color.white);
            slot.gameObject.layer = 5;
            slot.rectTransform.sizeDelta = new Vector2(50f, 50f);
            var frame = Img("Frame", slot.transform, outlineSprite, Color.white);
            frame.type = Image.Type.Sliced; Stretch(frame.rectTransform, -2f); frame.raycastTarget = false;
            var sel = Img("Selected", slot.transform, outlineSprite, Color.white);
            sel.type = Image.Type.Sliced; Stretch(sel.rectTransform, -10f); sel.raycastTarget = false; sel.enabled = false;
            var icon = Img("Icon", slot.transform, null, Color.white);
            Stretch(icon.rectTransform, 10f); icon.preserveAspect = true; icon.raycastTarget = false;
            var lbl = Text("Label", slot.transform, "LABEL", titleFont, 15, Color.white, TextAlignmentOptions.Top);
            lbl.rectTransform.anchorMin = lbl.rectTransform.anchorMax = new Vector2(0.5f, 0f);
            lbl.rectTransform.pivot = new Vector2(0.5f, 1f); lbl.rectTransform.sizeDelta = new Vector2(220, 40); lbl.rectTransform.anchoredPosition = new Vector2(0, -12);
            lbl.gameObject.SetActive(false);
            var selectable = slot.gameObject.AddComponent<Selectable>(); selectable.transition = Selectable.Transition.None;
            var view = slot.gameObject.AddComponent<ArcanumNodeView>();
            view.slot = slot; view.frame = frame; view.selection = sel; view.icon = icon;
            EnsureFolder(PrefabDir + "/Arcanum");
            var asset = PrefabUtility.SaveAsPrefabAsset(slot.gameObject, NodePrefabPath);
            Object.DestroyImmediate(slot.gameObject);
            return asset;
        }

        private static GameObject RowPrefab()
        {
            var bg = Img("ArcanumCategoryRow", null, rowSprite, RowColor);
            bg.gameObject.layer = 5; bg.type = Image.Type.Sliced;
            bg.rectTransform.sizeDelta = new Vector2(348f, 100f);
            bg.gameObject.AddComponent<CanvasGroup>();
            var border = Img("Border", bg.transform, outlineSprite, RowBorder);
            border.type = Image.Type.Sliced; border.pixelsPerUnitMultiplier = 2f; Stretch(border.rectTransform); border.raycastTarget = false;
            var btn = bg.gameObject.AddComponent<Button>();
            btn.transition = Selectable.Transition.ColorTint;
            var colors = btn.colors; colors.highlightedColor = new Color(1.25f, 1.25f, 1.25f); colors.pressedColor = new Color(0.85f, 0.85f, 0.85f); colors.fadeDuration = 0.08f; btn.colors = colors;
            var slot = Img("Slot", bg.transform, slotSprite, Color.white);
            slot.rectTransform.anchorMin = slot.rectTransform.anchorMax = new Vector2(0, 0.5f); slot.rectTransform.pivot = new Vector2(0, 0.5f);
            slot.rectTransform.sizeDelta = new Vector2(52, 52); slot.rectTransform.anchoredPosition = new Vector2(10, 0); slot.raycastTarget = false;
            var catIcon = Img("Icon", slot.transform, null, Color.white); Stretch(catIcon.rectTransform, 10f); catIcon.preserveAspect = true; catIcon.raycastTarget = false;
            var stripe = Img("Stripe", slot.transform, rowSprite, Color.white); stripe.type = Image.Type.Sliced;
            stripe.rectTransform.anchorMin = new Vector2(0.2f, 0); stripe.rectTransform.anchorMax = new Vector2(0.8f, 0);
            stripe.rectTransform.sizeDelta = new Vector2(0, 4); stripe.rectTransform.anchoredPosition = new Vector2(0, 3); stripe.raycastTarget = false;
            var name = Text("Name", bg.transform, "DISCIPLINE", titleFont, 19, Color.white, TextAlignmentOptions.TopLeft);
            name.textWrappingMode = TextWrappingModes.NoWrap; name.enableAutoSizing = true; name.fontSizeMin = 12; name.fontSizeMax = 19;
            AnchorRow(name.rectTransform, new Vector2(74, -10), new Vector2(-96, -10), 26);
            var ready = Text("Ready", bg.transform, "0 ready", bodyFont, 15, DimText, TextAlignmentOptions.TopRight);
            AnchorRow(ready.rectTransform, new Vector2(190, -12), new Vector2(-12, -12), 22);
            var mat = Img("MatIcon", bg.transform, null, Color.white);
            mat.rectTransform.anchorMin = mat.rectTransform.anchorMax = new Vector2(0, 0.5f); mat.rectTransform.pivot = new Vector2(0, 0.5f);
            mat.rectTransform.sizeDelta = new Vector2(20, 20); mat.rectTransform.anchoredPosition = new Vector2(74, -2); mat.preserveAspect = true; mat.raycastTarget = false;
            var supply = Text("Supply", bg.transform, "", bodyFont, 16, MutedText, TextAlignmentOptions.MidlineLeft);
            supply.rectTransform.anchorMin = new Vector2(0, 0.5f); supply.rectTransform.anchorMax = new Vector2(1, 0.5f); supply.rectTransform.pivot = new Vector2(0, 0.5f);
            supply.rectTransform.offsetMin = new Vector2(100, -14); supply.rectTransform.offsetMax = new Vector2(-12, 10);
            var barBg = Img("BarBg", bg.transform, rowSprite, new Color32(0x07, 0x09, 0x07, 0xff));
            barBg.type = Image.Type.Sliced; barBg.pixelsPerUnitMultiplier = 6f;
            barBg.rectTransform.anchorMin = new Vector2(0, 0); barBg.rectTransform.anchorMax = new Vector2(1, 0);
            barBg.rectTransform.offsetMin = new Vector2(74, 28); barBg.rectTransform.offsetMax = new Vector2(-14, 33); barBg.raycastTarget = false;
            var bar = Img("Bar", barBg.transform, rowSprite, Color.white);
            bar.type = Image.Type.Sliced; bar.pixelsPerUnitMultiplier = 6f;
            bar.rectTransform.anchorMin = Vector2.zero; bar.rectTransform.anchorMax = new Vector2(0.5f, 1f); bar.rectTransform.offsetMin = bar.rectTransform.offsetMax = Vector2.zero; bar.raycastTarget = false;
            var next = Text("Next", bg.transform, "", bodyFont, 14, DimText, TextAlignmentOptions.BottomLeft);
            next.rectTransform.anchorMin = new Vector2(0, 0); next.rectTransform.anchorMax = new Vector2(1, 0); next.rectTransform.pivot = new Vector2(0, 0);
            next.rectTransform.offsetMin = new Vector2(74, 5); next.rectTransform.offsetMax = new Vector2(-12, 26);
            next.overflowMode = TextOverflowModes.Ellipsis; next.textWrappingMode = TextWrappingModes.NoWrap;
            var asset = PrefabUtility.SaveAsPrefabAsset(bg.gameObject, RowPrefabPath);
            Object.DestroyImmediate(bg.gameObject);
            return asset;
        }

        // ── main prefab ──────────────────────────────────────────────────────

        private class Ctx
        {
            public ArcanumUI ui;
            public SerializedObject so;
            public RectTransform canvasRect;
            public readonly List<(RectTransform rt, CanvasGroup g, Vector2 offset, float delay)> anims = new List<(RectTransform, CanvasGroup, Vector2, float)>();
            public void Ref(string field, Object value) { var p = so.FindProperty(field); if (p == null) throw new System.Exception("ArcanumUI." + field + " missing"); p.objectReferenceValue = value; }
        }

        public static GameObject AuthorPrefab()
        {
            EnsureTreeAsset();
            Art();
            var nodePrefab = NodePrefab();
            var rowPrefab = RowPrefab();
            var nodes = ArcanumData.Nodes;

            var root = new GameObject("ArcanumUI");
            var ui = root.AddComponent<ArcanumUI>();
            var x = new Ctx { ui = ui, so = new SerializedObject(ui) };

            var cgo = new GameObject("ArcanumCanvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster), typeof(CanvasGroup));
            cgo.layer = 5;
            cgo.transform.SetParent(root.transform, false);
            var canvas = cgo.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay; canvas.sortingOrder = 60;
            var scaler = cgo.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize; scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight; scaler.matchWidthOrHeight = 1f;
            var canvasRect = (RectTransform)cgo.transform;
            x.canvasRect = canvasRect;
            x.Ref("canvas", canvas); x.Ref("canvasRect", canvasRect); x.Ref("rootGroup", cgo.GetComponent<CanvasGroup>());

            // backdrop: blur + dim come from WorldBlurBackdrop, this stays transparent
            var dim = Img("Dim", canvasRect, solidSprite, new Color(0.01f, 0.02f, 0.01f, 0f));
            Stretch(dim.rectTransform); dim.raycastTarget = false;
            x.Ref("dim", dim);

            var vpImg = Img("Viewport", canvasRect, null, new Color(0, 0, 0, 0));
            var viewport = vpImg.rectTransform; Stretch(viewport); viewport.pivot = Vector2.zero;
            viewport.gameObject.AddComponent<ArcanumViewport>().owner = ui;
            x.Ref("viewport", viewport);

            var content = NewRect("Content", viewport);
            content.anchorMin = content.anchorMax = Vector2.zero; content.pivot = new Vector2(0.5f, 0.5f); content.sizeDelta = Vector2.zero;
            // editor preview = the home view at 1920×1080
            content.anchoredPosition = new Vector2((1920f - 380f) * 0.5f, 95f);
            content.localScale = Vector3.one * 0.82f;
            x.Ref("content", content);

            var treeRt = NewRect("Connections", content);
            var tree = treeRt.gameObject.AddComponent<ArcanumTreeGraphic>();
            tree.raycastTarget = false;
            x.Ref("tree", tree);

            for (int c = 0; c < ArcanumData.Categories.Length; c++)
            {
                float a = Mathf.PI - ArcanumData.CategoryCenterAngle(c);
                var label = Text("Sector_" + ArcanumData.Categories[c].name, content, ArcanumData.Categories[c].name.ToUpperInvariant() +
                    "\n<size=40%><color=#9aa697>DISCIPLINE 0" + (c + 1) + "  /  30 RESEARCH</color></size>", titleFont, 46, new Color32(0xd5, 0xdb, 0xd0, 0xff), TextAlignmentOptions.Center);
                label.rectTransform.sizeDelta = new Vector2(520, 120);
                label.rectTransform.anchoredPosition = new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * 1990f;
            }

            var nodesRoot = NewRect("Nodes", content);
            nodesRoot.sizeDelta = Vector2.zero;
            var viewsProp = x.so.FindProperty("views");
            viewsProp.arraySize = nodes.Count;
            var nodeViews = new List<ArcanumNodeView>();
            foreach (var n in nodes)
            {
                var go = (GameObject)PrefabUtility.InstantiatePrefab(nodePrefab, nodesRoot);
                go.name = "Node_" + n.id.ToString("000") + "_" + n.name.Replace(' ', '_');
                var rt = (RectTransform)go.transform;
                float size = n.id == 0 ? 84f : n.major ? 62f : 50f;
                rt.sizeDelta = new Vector2(size, size);
                rt.anchoredPosition = n.pos;
                var v = go.GetComponent<ArcanumNodeView>();
                v.owner = ui; v.nodeId = n.id;
                v.icon.rectTransform.offsetMin = new Vector2(size * 0.2f, size * 0.2f); v.icon.rectTransform.offsetMax = new Vector2(-size * 0.2f, -size * 0.2f);
                var item = ArcanumResearch.Item(n.category >= 0 ? ArcanumData.Categories[n.category].iconItemId : ArcanumData.RootIconItemId);
                v.icon.sprite = item != null ? item.icon : null;
                // preview state: root researched, its children available
                bool done = n.id == 0, avail = n.parent == 0;
                Color cat = n.category >= 0 ? ArcanumData.Categories[n.category].color : AvailableFrame;
                v.frame.enabled = done || avail;
                v.frame.color = done ? cat : AvailableFrame;
                v.slot.color = done ? Color.white : avail ? new Color(0.92f, 0.92f, 0.92f, 1f) : new Color(0.6f, 0.6f, 0.6f, 0.85f);
                v.icon.color = done ? Color.white : avail ? new Color(1f, 1f, 1f, 0.85f) : new Color(0.55f, 0.57f, 0.55f, 0.45f);
                if (n.id == 0 || (n.major && n.tier == 6))
                {
                    var lbl = go.transform.Find("Label").GetComponent<TMP_Text>();
                    lbl.gameObject.SetActive(true);
                    lbl.text = n.id == 0 ? n.name.ToUpperInvariant() : n.name.ToUpperInvariant();
                }
                viewsProp.GetArrayElementAtIndex(n.id).objectReferenceValue = v;
                nodeViews.Add(v);
            }
            tree.nodes = nodeViews;

            var pulse = NewRect("UnlockPulse", content);
            var pulseImage = pulse.gameObject.AddComponent<Image>();
            pulseImage.sprite = outlineSprite; pulseImage.type = Image.Type.Sliced; pulseImage.raycastTarget = false;
            pulse.gameObject.SetActive(false);
            x.Ref("pulse", pulse); x.Ref("pulseImage", pulseImage);

            BuildHeader(x); BuildStats(x); BuildCategories(x, rowPrefab); BuildFooters(x); BuildTooltip(x); BuildToast(x);

            var animsProp = x.so.FindProperty("anims");
            animsProp.arraySize = x.anims.Count;
            for (int i = 0; i < x.anims.Count; i++)
            {
                var e = animsProp.GetArrayElementAtIndex(i);
                e.FindPropertyRelative("rt").objectReferenceValue = x.anims[i].rt;
                e.FindPropertyRelative("group").objectReferenceValue = x.anims[i].g;
                e.FindPropertyRelative("offset").vector2Value = x.anims[i].offset;
                e.FindPropertyRelative("delay").floatValue = x.anims[i].delay;
            }
            x.so.ApplyModifiedPropertiesWithoutUndo();

            EnsureFolder("Assets/Resources/UI");
            var asset = PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            Object.DestroyImmediate(root);
            return asset;
        }

        private static RectTransform EdgePanel(Ctx x, string name, Vector2 anchor, Vector2 size, Vector2 bleed)
        {
            var img = Img(name, x.canvasRect, panelSprite, Color.white);
            img.type = Image.Type.Sliced;
            var rt = img.rectTransform;
            rt.anchorMin = rt.anchorMax = anchor; rt.pivot = anchor;
            rt.sizeDelta = size + new Vector2(Mathf.Abs(bleed.x), Mathf.Abs(bleed.y));
            rt.anchoredPosition = bleed;
            img.raycastTarget = true;
            return rt;
        }

        private static void BuildHeader(Ctx x)
        {
            var header = EdgePanel(x, "Header", new Vector2(0, 1), new Vector2(330, 112), new Vector2(-30, 30));
            PlaceTL(Text("Eyebrow", header, "CRAFTS & DISCOVERIES", bodyFont, 15, MutedText, TextAlignmentOptions.TopLeft).rectTransform, new Vector2(0, 1), new Vector2(58, -52), new Vector2(280, 22));
            PlaceTL(Text("Title", header, "ARCANUM", titleFont, 44, Color.white, TextAlignmentOptions.TopLeft).rectTransform, new Vector2(0, 1), new Vector2(56, -74), new Vector2(290, 56));
            x.Ref("header", header);
            x.anims.Add((header, null, new Vector2(0, 120), 0f));
        }

        private static void BuildStats(Ctx x)
        {
            var stats = EdgePanel(x, "Stats", new Vector2(1, 1), new Vector2(380, 112), new Vector2(30, 30));
            var researched = Text("Researched", stats, "1 / 181", titleFont, 34, Color.white, TextAlignmentOptions.TopLeft);
            PlaceTL(researched.rectTransform, new Vector2(0, 1), new Vector2(34, -50), new Vector2(190, 44));
            PlaceTL(Text("ResearchedLabel", stats, "RESEARCHED", bodyFont, 15, MutedText, TextAlignmentOptions.TopLeft).rectTransform, new Vector2(0, 1), new Vector2(34, -96), new Vector2(190, 20));
            var ready = Text("Ready", stats, "6", titleFont, 34, GoodText, TextAlignmentOptions.TopLeft);
            PlaceTL(ready.rectTransform, new Vector2(0, 1), new Vector2(230, -50), new Vector2(150, 44));
            PlaceTL(Text("ReadyLabel", stats, "READY NOW", bodyFont, 15, MutedText, TextAlignmentOptions.TopLeft).rectTransform, new Vector2(0, 1), new Vector2(230, -96), new Vector2(150, 20));
            x.Ref("stats", stats); x.Ref("researchedValue", researched); x.Ref("readyValue", ready);
            x.anims.Add((stats, null, new Vector2(0, 120), 0.03f));
        }

        private static void BuildCategories(Ctx x, GameObject rowPrefab)
        {
            var img = Img("Disciplines", x.canvasRect, panelSprite, Color.white);
            img.type = Image.Type.Sliced;
            var panel = img.rectTransform;
            panel.anchorMin = new Vector2(1, 0); panel.anchorMax = new Vector2(1, 1); panel.pivot = new Vector2(1, 0.5f);
            panel.offsetMin = new Vector2(-380, 104); panel.offsetMax = new Vector2(34, -132);
            PlaceTL(Text("Eyebrow", panel, "RESEARCH SUPPLIES", bodyFont, 15, MutedText, TextAlignmentOptions.TopLeft).rectTransform, new Vector2(0, 1), new Vector2(26, -22), new Vector2(320, 20));
            PlaceTL(Text("Title", panel, "DISCIPLINES", titleFont, 28, Color.white, TextAlignmentOptions.TopLeft).rectTransform, new Vector2(0, 1), new Vector2(24, -44), new Vector2(330, 36));
            PlaceTL(Text("Help", panel, "Owned / required for the next research.", bodyFont, 15, DimText, TextAlignmentOptions.TopLeft).rectTransform, new Vector2(0, 1), new Vector2(26, -84), new Vector2(318, 36));

            var list = NewRect("Rows", panel);
            list.anchorMin = new Vector2(0, 0); list.anchorMax = new Vector2(1, 1);
            list.offsetMin = new Vector2(16, 76); list.offsetMax = new Vector2(-50, -124);
            var vlg = list.gameObject.AddComponent<VerticalLayoutGroup>();
            vlg.spacing = 8; vlg.childControlHeight = true; vlg.childControlWidth = true; vlg.childForceExpandHeight = true; vlg.childForceExpandWidth = true;

            var rowsProp = x.so.FindProperty("rows");
            rowsProp.arraySize = ArcanumData.Categories.Length;
            for (int c = 0; c < ArcanumData.Categories.Length; c++)
            {
                var cat = ArcanumData.Categories[c];
                var go = (GameObject)PrefabUtility.InstantiatePrefab(rowPrefab, list);
                go.name = "Row_" + cat.name;
                var t = go.transform;
                var bg = go.GetComponent<Image>();
                var nameT = t.Find("Name").GetComponent<TMP_Text>(); nameT.text = cat.name.ToUpperInvariant();
                t.Find("Slot/Icon").GetComponent<Image>().sprite = ArcanumResearch.Item(cat.iconItemId)?.icon;
                t.Find("Slot/Stripe").GetComponent<Image>().color = cat.color;
                var bar = t.Find("BarBg/Bar").GetComponent<Image>(); bar.color = Color.Lerp(cat.color, Color.gray, 0.45f);
                bar.rectTransform.anchorMax = new Vector2(0f, 1f);
                var item = ArcanumResearch.Item(cat.iconItemId);
                var mat = t.Find("MatIcon").GetComponent<Image>(); mat.sprite = item != null ? item.icon : null; mat.enabled = item != null && item.icon != null;
                var supply = t.Find("Supply").GetComponent<TMP_Text>();
                supply.text = (item != null ? item.itemName : "Material") + "  <color=#e6a56b>0 / 5</color>";
                var next = t.Find("Next").GetComponent<TMP_Text>(); next.text = "Next: —";
                var btn = go.GetComponent<Button>();
                UnityEventTools.AddIntPersistentListener(btn.onClick, new UnityAction<int>(x.ui.FocusCategory), c);
                var e = rowsProp.GetArrayElementAtIndex(c);
                e.FindPropertyRelative("rt").objectReferenceValue = go.transform;
                e.FindPropertyRelative("bg").objectReferenceValue = bg;
                e.FindPropertyRelative("name").objectReferenceValue = nameT;
                e.FindPropertyRelative("ready").objectReferenceValue = t.Find("Ready").GetComponent<TMP_Text>();
                e.FindPropertyRelative("supply").objectReferenceValue = supply;
                e.FindPropertyRelative("next").objectReferenceValue = next;
                e.FindPropertyRelative("matIcon").objectReferenceValue = mat;
                e.FindPropertyRelative("bar").objectReferenceValue = bar;
                e.FindPropertyRelative("barBg").objectReferenceValue = t.Find("BarBg").GetComponent<Image>();
                e.FindPropertyRelative("border").objectReferenceValue = t.Find("Border").GetComponent<Image>();
                var g = go.GetComponent<CanvasGroup>();
                e.FindPropertyRelative("group").objectReferenceValue = g;
                x.anims.Add((null, g, Vector2.zero, 0.05f + c * 0.03f));
            }

            var note = Text("Note", panel, "Consumes materials from your inventory.\nPlaceholder: no crafting recipes are added yet.", bodyFont, 12, DimText, TextAlignmentOptions.BottomLeft);
            note.rectTransform.anchorMin = new Vector2(0, 0); note.rectTransform.anchorMax = new Vector2(1, 0); note.rectTransform.pivot = new Vector2(0, 0);
            note.rectTransform.offsetMin = new Vector2(24, 14); note.rectTransform.offsetMax = new Vector2(-50, 70);
            x.Ref("categoryPanel", panel);
            x.anims.Add((panel, null, new Vector2(420, 0), 0.02f));
        }

        private static Button MakeButton(Transform parent, string label, Vector2 topLeft, Vector2 size)
        {
            var img = Img("Btn_" + (label == "-" ? "ZoomOut" : label == "+" ? "ZoomIn" : label), parent, slotSprite, Color.white);
            img.type = slotSprite.border != Vector4.zero ? Image.Type.Sliced : Image.Type.Simple;
            PlaceTL(img.rectTransform, Vector2.zero, topLeft, size);
            var b = img.gameObject.AddComponent<Button>();
            var colors = b.colors; colors.highlightedColor = new Color(1.3f, 1.3f, 1.3f); colors.fadeDuration = 0.08f; b.colors = colors;
            var t = Text("Label", img.rectTransform, label, titleFont, label.Length > 1 ? 13 : 20, Color.white, TextAlignmentOptions.Center);
            Stretch(t.rectTransform);
            return b;
        }

        private static void BuildFooters(Ctx x)
        {
            var left = EdgePanel(x, "Legend", new Vector2(0, 0), new Vector2(680, 74), new Vector2(-30, -30));
            PlaceTL(Text("Legend", left, "<color=#c9e986>RESEARCHED</color>     <color=#e2e6dc>AVAILABLE</color>     <color=#6b706c>LOCKED</color>", bodyFont, 15, MutedText, TextAlignmentOptions.TopLeft).rectTransform, Vector2.zero, new Vector2(54, 96), new Vector2(620, 22));
            PlaceTL(Text("Help", left, "DRAG - pan    WHEEL - zoom    CLICK - pin details    C / ESC - close", bodyFont, 14, DimText, TextAlignmentOptions.TopLeft).rectTransform, Vector2.zero, new Vector2(54, 66), new Vector2(620, 22));
            x.Ref("footerLeft", left);
            x.anims.Add((left, null, new Vector2(0, -110), 0.04f));

            var right = EdgePanel(x, "ZoomControls", new Vector2(1, 0), new Vector2(250, 74), new Vector2(30, -30));
            var zo = MakeButton(right, "-", new Vector2(22, 88), new Vector2(44, 40));
            UnityEventTools.AddPersistentListener(zo.onClick, x.ui.ZoomOut);
            var zoom = Text("Zoom", right, "82%", bodyFont, 17, MutedText, TextAlignmentOptions.Center);
            PlaceTL(zoom.rectTransform, Vector2.zero, new Vector2(70, 80), new Vector2(60, 24));
            var zi = MakeButton(right, "+", new Vector2(132, 88), new Vector2(44, 40));
            UnityEventTools.AddPersistentListener(zi.onClick, x.ui.ZoomIn);
            var home = MakeButton(right, "HOME", new Vector2(182, 88), new Vector2(62, 40));
            UnityEventTools.AddPersistentListener(home.onClick, x.ui.ResetView);
            x.Ref("footerRight", right); x.Ref("zoomLabel", zoom);
            x.anims.Add((right, null, new Vector2(0, -110), 0.06f));
        }

        private static void BuildTooltip(Ctx x)
        {
            var img = Img("ResearchDetail", x.canvasRect, panelSprite, Color.white);
            img.type = Image.Type.Sliced;
            var tooltip = img.rectTransform;
            tooltip.anchorMin = tooltip.anchorMax = Vector2.zero; tooltip.pivot = new Vector2(0, 1); tooltip.sizeDelta = new Vector2(420, 0);
            tooltip.anchoredPosition = new Vector2(1000f, 700f);   // preview position (runtime places it next to the node)
            var group = img.gameObject.AddComponent<CanvasGroup>();
            var vlg = img.gameObject.AddComponent<VerticalLayoutGroup>();
            vlg.padding = new RectOffset(26, 26, 22, 22); vlg.spacing = 8;
            vlg.childControlHeight = true; vlg.childControlWidth = true; vlg.childForceExpandHeight = false; vlg.childForceExpandWidth = true;
            img.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            var root = ArcanumData.Nodes[0];
            var branch = Text("Branch", tooltip, "THE ROOT OF ALL DISCIPLINES", bodyFont, 14, MutedText, TextAlignmentOptions.TopLeft);
            var headRow = NewRect("Head", tooltip);
            var hl = headRow.gameObject.AddComponent<HorizontalLayoutGroup>();
            hl.spacing = 14; hl.childControlWidth = true; hl.childControlHeight = true; hl.childForceExpandWidth = false; hl.childAlignment = TextAnchor.MiddleLeft;
            var iconSlot = Img("IconSlot", headRow, slotSprite, Color.white);
            var le = iconSlot.gameObject.AddComponent<LayoutElement>(); le.preferredWidth = 60; le.preferredHeight = 60; le.flexibleWidth = 0;
            var iconFrame = Img("Frame", iconSlot.rectTransform, outlineSprite, AvailableFrame); iconFrame.type = Image.Type.Sliced; Stretch(iconFrame.rectTransform, -2f);
            var icon = Img("Icon", iconSlot.rectTransform, ArcanumResearch.Item(ArcanumData.RootIconItemId)?.icon, Color.white); Stretch(icon.rectTransform, 12f); icon.preserveAspect = true;
            var title = Text("Title", headRow, root.name, titleFont, 22, Color.white, TextAlignmentOptions.MidlineLeft);
            title.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1;
            var desc = Text("Description", tooltip, ArcanumData.Description(root), bodyFont, 15, MutedText, TextAlignmentOptions.TopLeft);
            var recipe = Text("Recipe", tooltip, "Unlocks recipe: <color=#ffffff>" + root.recipe + "</color>  <color=#838b82>(placeholder)</color>", bodyFont, 15, MutedText, TextAlignmentOptions.TopLeft);
            var matRow = NewRect("Materials", tooltip);
            var ml = matRow.gameObject.AddComponent<HorizontalLayoutGroup>();
            ml.spacing = 12; ml.childControlWidth = true; ml.childControlHeight = true; ml.childForceExpandWidth = false; ml.childAlignment = TextAnchor.MiddleLeft;
            var matSlot = Img("MatSlot", matRow, slotSprite, Color.white);
            var mle = matSlot.gameObject.AddComponent<LayoutElement>(); mle.preferredWidth = 50; mle.preferredHeight = 50;
            var matIcon = Img("Icon", matSlot.rectTransform, null, Color.white); Stretch(matIcon.rectTransform, 10f); matIcon.preserveAspect = true; matIcon.enabled = false;
            var matText = Text("MatText", matRow, "No materials required", bodyFont, 15, Color.white, TextAlignmentOptions.MidlineLeft);
            matText.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1;
            var status = Text("Status", tooltip, "<color=#c9e986>Researched.</color> The recipe will be available for crafting.", bodyFont, 15, MutedText, TextAlignmentOptions.TopLeft);
            var rb = Img("ResearchButton", tooltip, rowSprite, new Color32(0x2c, 0x31, 0x2b, 0xff));
            rb.type = Image.Type.Sliced;
            rb.gameObject.AddComponent<LayoutElement>().preferredHeight = 48;
            var researchButton = rb.gameObject.AddComponent<Button>();
            var rc = researchButton.colors; rc.highlightedColor = new Color(1.18f, 1.18f, 1.18f); rc.disabledColor = new Color(0.8f, 0.8f, 0.8f); rc.fadeDuration = 0.08f; researchButton.colors = rc;
            UnityEventTools.AddPersistentListener(researchButton.onClick, x.ui.OnResearchClicked);
            var rl = Text("Label", rb.rectTransform, "RESEARCHED", titleFont, 16, MutedText, TextAlignmentOptions.Center);
            Stretch(rl.rectTransform);
            var hint = Text("Hint", tooltip, "Esc, X or click outside to close", bodyFont, 13, DimText, TextAlignmentOptions.TopLeft);
            var close = Img("Close", tooltip, solidSprite, new Color(0, 0, 0, 0));
            close.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;
            close.rectTransform.anchorMin = close.rectTransform.anchorMax = new Vector2(1, 1); close.rectTransform.pivot = new Vector2(1, 1);
            close.rectTransform.sizeDelta = new Vector2(40, 40); close.rectTransform.anchoredPosition = new Vector2(-10, -10);
            var closeButton = close.gameObject.AddComponent<Button>();
            UnityEventTools.AddPersistentListener(closeButton.onClick, x.ui.CloseTooltip);
            var xt = Text("X", close.rectTransform, "X", titleFont, 18, MutedText, TextAlignmentOptions.Center); Stretch(xt.rectTransform);

            x.Ref("tooltip", tooltip); x.Ref("tooltipGroup", group);
            x.Ref("tipBranch", branch); x.Ref("tipTitle", title); x.Ref("tipDesc", desc); x.Ref("tipRecipe", recipe);
            x.Ref("tipMaterial", matText); x.Ref("tipStatus", status); x.Ref("tipHint", hint);
            x.Ref("tipIcon", icon); x.Ref("tipIconFrame", iconFrame); x.Ref("tipMatIcon", matIcon);
            x.Ref("researchButton", researchButton); x.Ref("closeButton", closeButton); x.Ref("researchLabel", rl); x.Ref("researchImage", rb);
            // stays active in the prefab so the detail view can be edited; ArcanumUI hides it on start
        }

        private static void BuildToast(Ctx x)
        {
            var img = Img("Toast", x.canvasRect, rowSprite, new Color32(0x0b, 0x10, 0x09, 0xf5));
            img.type = Image.Type.Sliced;
            var toast = img.rectTransform;
            toast.anchorMin = toast.anchorMax = new Vector2(0.45f, 1f); toast.pivot = new Vector2(0.5f, 1f);
            toast.sizeDelta = new Vector2(660, 54); toast.anchoredPosition = new Vector2(0, -26);
            img.raycastTarget = false;
            var g = img.gameObject.AddComponent<CanvasGroup>(); g.blocksRaycasts = false;
            var t = Text("Text", toast, "New recipe discovered: Research Table  (placeholder)", bodyFont, 16, Color.white, TextAlignmentOptions.Center);
            Stretch(t.rectTransform, 10f);
            toast.gameObject.SetActive(false);
            x.Ref("toast", toast); x.Ref("toastGroup", g);
        }

        // ── F3 diagnostics overlay ───────────────────────────────────────────

        public const string F3PrefabPath = "Assets/Resources/UI/F3Diagnostics.prefab";

        public static GameObject AuthorF3()
        {
            if (panelSprite == null) Art();
            var f3Sprite = ArcanumRounded("F3_Panel", 96, 28f, -1f, true);
            var root = new GameObject("F3Diagnostics", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
            root.layer = 5;
            var overlay = root.AddComponent<Orivilon.Core.F3Overlay>();
            var canvas = root.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay; canvas.sortingOrder = 70;
            var scaler = root.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize; scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight; scaler.matchWidthOrHeight = 1f;
            var panelImg = Img("Panel", root.transform, f3Sprite, Color.white);
            panelImg.type = Image.Type.Sliced; panelImg.raycastTarget = false;
            var panel = panelImg.rectTransform;
            panel.anchorMin = panel.anchorMax = new Vector2(0f, 1f); panel.pivot = new Vector2(0f, 1f);
            panel.anchoredPosition = new Vector2(-6f, 6f); panel.sizeDelta = new Vector2(600f, 452f);
            var group = panelImg.gameObject.AddComponent<CanvasGroup>(); group.blocksRaycasts = false; group.interactable = false;
            var header = Text("Header", panel, "DIAGNOSTICS <size=60%><color=#838b82>  BETA</color></size>", titleFont, 22f, Color.white, TextAlignmentOptions.TopLeft);
            header.textWrappingMode = TextWrappingModes.NoWrap;
            header.rectTransform.anchorMin = new Vector2(0f, 1f); header.rectTransform.anchorMax = new Vector2(1f, 1f); header.rectTransform.pivot = new Vector2(0f, 1f);
            header.rectTransform.offsetMin = new Vector2(32f, -62f); header.rectTransform.offsetMax = new Vector2(-24f, -24f);
            var key = Text("Key", panel, "F3", bodyFont, 15f, new Color32(0x83, 0x8b, 0x82, 0xff), TextAlignmentOptions.TopRight);
            key.rectTransform.anchorMin = key.rectTransform.anchorMax = new Vector2(1f, 1f); key.rectTransform.pivot = new Vector2(1f, 1f);
            key.rectTransform.anchoredPosition = new Vector2(-26f, -30f); key.rectTransform.sizeDelta = new Vector2(60f, 24f);
            var body = Text("Body", panel, PreviewBody(), MonoRegular, 17f, Color.white, TextAlignmentOptions.TopLeft);
            body.textWrappingMode = TextWrappingModes.NoWrap; body.overflowMode = TextOverflowModes.Overflow; body.lineSpacing = 8f;
            body.rectTransform.anchorMin = new Vector2(0f, 1f); body.rectTransform.anchorMax = new Vector2(1f, 1f); body.rectTransform.pivot = new Vector2(0f, 1f);
            body.rectTransform.offsetMin = new Vector2(32f, -1000f); body.rectTransform.offsetMax = new Vector2(-24f, -66f);
            overlay.canvas = canvas; overlay.panel = panel; overlay.group = group; overlay.header = header; overlay.body = body;
            EnsureFolder("Assets/Resources/UI");
            var asset = PrefabUtility.SaveAsPrefabAsset(root, F3PrefabPath);
            Object.DestroyImmediate(root);
            return asset;
        }

        private static string PreviewBody() =>
            "<color=#838b82><size=70%>PERFORMANCE</size></color>\n" +
            "FPS        144        6.9 ms   max 9.8 ms\n" +
            "<color=#838b82><size=70%>POSITION</size></color>\n" +
            "XYZ        -792.1     87.5     704.6\n" +
            "Chunk      -50        44       Facing NE\n" +
            "<color=#838b82><size=70%>WORLD</size></color>\n" +
            "Biome      Steppe & Savanna\n" +
            "Climate    Temperate  temp 0.54  hum 0.38\n" +
            "Surface    163.1 m    Time 10:30\n" +
            "<color=#838b82><size=70%>MEMORY</size></color>\n" +
            "Unity      1876 MB    Managed 311 MB";
    }
}
