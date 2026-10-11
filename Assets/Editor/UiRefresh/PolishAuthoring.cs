using System.Collections.Generic;
using Orivilon.UI.Arcanum;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using static Orivilon.UI.Refresh.EditorTools.UiAuthoringKit;
using static Orivilon.UI.Refresh.EditorTools.FrameAuthoring;

namespace Orivilon.UI.Refresh.EditorTools
{
    /// <summary>
    /// Focused finishing edits on the already-saved UI (bridge command "polish &lt;step&gt;").
    /// Each step edits the existing scene/prefab in place (no rebuild, user edits are kept) and refuses
    /// to run twice.
    ///   menu    – MainMenu: bridge piece joining the heading tab and the world panel
    ///   arcanum – ArcanumUI prefab: screen rim + frame pieces under the edge panels + inward joins
    ///   hud     – Game: inventory content follows its backgrounds, slide-in closed states, cursor font
    ///   hover   – open scene + UI prefabs: #565656 hover (HoverTint) on every button
    /// </summary>
    public static class PolishAuthoring
    {
        public const string HoverStylePath = Root + "/HoverStyle.asset";

        public static string Run(string step)
        {
            try
            {
                switch (step)
                {
                    case "menu": return Menu();
                    case "arcanum": return Arcanum();
                    case "hud": return Hud();
                    case "hover": return Hover();
                    case "hoverfix": return HoverFix();
                    case "backfix": return BackFix();
                    case "refollow": return Refollow();
                    case "slots": return Slots();
                    case "slotoutline": return SlotOutline();
                    case "cleanview": return CleanViewStep();
                    case "loadtext": return LoadText();
                    default: return "ERR polish: unknown step " + step;
                }
            }
            catch (System.Exception e)
            {
                Debug.LogException(e);
                return "ERR polish " + step + ": " + e.Message;
            }
        }

        private static Scene Need(string name)
        {
            var s = SceneManager.GetActiveScene();
            if (s.name != name) throw new System.Exception("open the " + name + " scene first (current: " + s.name + ")");
            return s;
        }

        private static string Save(Scene s, string what)
        {
            EditorSceneManager.MarkSceneDirty(s);
            if (!EditorSceneManager.SaveScene(s)) throw new System.Exception("saving " + s.name + " failed");
            return "polish " + what + " ok (" + s.name + " saved)";
        }

        private static Transform FindChild(Transform root, string name) => FindDeep(root, name);

        // ── 1. main menu: heading ↔ panel bridge ─────────────────────────────

        private static string Menu()
        {
            var s = Need("MainMenu");
            var mc = Object.FindFirstObjectByType<MainMenuController>(FindObjectsInactive.Include);
            if (mc == null || mc.frame == null) throw new System.Exception("MainMenuController/frame missing");
            var frame = mc.frame;
            if (frame.transform.Find("Panel_headingBridge") != null) return "ERR polish menu: already applied";
            string path = PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(frame.gameObject);
            if (string.IsNullOrEmpty(path)) throw new System.Exception("menu frame is not a prefab instance");

            var root = PrefabUtility.LoadPrefabContents(path);
            try
            {
                var f = root.GetComponent<UiFrame>();
                var head = root.transform.Find("Panel_heading") as RectTransform;
                if (head == null) throw new System.Exception("Panel_heading missing");
                var go = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(PanelPrefabPath), root.transform);
                go.name = "Panel_headingBridge";
                go.transform.SetAsLastSibling();   // drawn after the panel: its rim never wins over the real contour
                var rt = (RectTransform)go.transform;
                float R = f.style != null ? f.style.radius : 18f;
                foreach (var st in f.states)
                {
                    var hs = st.pieces.Find(p => p.piece == head);
                    var ps = new UiFrame.PieceState
                    {
                        piece = rt,
                        anchorMin = new Vector2(0f, 1f), anchorMax = new Vector2(0f, 1f),
                        offsetMin = new Vector2(110f, -104f - 2f * R), offsetMax = new Vector2(110f, -104f + 2f * R),
                    };
                    if (hs != null && hs.fit == UiFrame.FitSide.Right)
                    {
                        // fills the seam between the heading tab and the panel below it, up to the tab's fitted edge
                        ps.fit = UiFrame.FitSide.Right; ps.fitPad = hs.fitPad;
                        ps.offsetMax.x = hs.offsetMax.x;
                    }
                    st.pieces.Add(ps);
                }
                var cur = f.Find(f.initialState) ?? f.states[0];
                var mine = cur.pieces.Find(p => p.piece == rt);
                rt.anchorMin = mine.anchorMin; rt.anchorMax = mine.anchorMax; rt.offsetMin = mine.offsetMin; rt.offsetMax = mine.offsetMax;
                PrefabUtility.SaveAsPrefabAsset(root, path);
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }

            // scene instance: fit target = the heading text (same as the heading tab)
            var headInst = frame.transform.Find("Panel_heading") as RectTransform;
            var bridgeInst = frame.transform.Find("Panel_headingBridge") as RectTransform;
            foreach (var st in frame.states)
            {
                var hs = st.pieces.Find(p => p.piece == headInst);
                var bs = st.pieces.Find(p => p.piece == bridgeInst);
                if (hs != null && bs != null && bs.fit != UiFrame.FitSide.None) bs.fitTo = hs.fitTo;
            }
            PrefabUtility.RecordPrefabInstancePropertyModifications(frame);
            var ui = mc.ui;
            string saved = ui.gameObject.activeSelf ? (ui.createMenu.activeSelf ? "Create" : "Worlds") : "Menu";
            frame.ApplyInstant(frame.Find(saved));
            return Save(s, "menu");
        }

        // ── 2. Arcanum: continuous frame ─────────────────────────────────────

        private static string Arcanum()
        {
            string path = ArcanumAuthoring.PrefabPath;
            var root = PrefabUtility.LoadPrefabContents(path);
            try
            {
                var canvas = root.transform.Find("ArcanumCanvas") as RectTransform;
                if (canvas == null) throw new System.Exception("ArcanumCanvas missing");
                if (canvas.Find("ArcanumFrame") != null) return "ERR polish arcanum: already applied";
                var style = AssetDatabase.LoadAssetAtPath<FrameStyle>(StylePath);
                var panelPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(PanelPrefabPath);
                var joinPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(JoinPrefabPath);

                var frt = NewRect("ArcanumFrame", canvas);
                Stretch(frt);
                var viewport = canvas.Find("Viewport");
                frt.SetSiblingIndex(viewport != null ? viewport.GetSiblingIndex() + 1 : 1);
                var reset = NewImage("StencilReset", frt, null, Color.white, false);
                reset.material = style.resetMaterial; Stretch(reset.rectTransform);
                var joinsRoot = NewRect("Joins", frt); Stretch(joinsRoot);

                var spec = new Spec { name = "ArcanumFrame", initialState = "Default" };
                spec.states.Add("Default");
                var map = new Dictionary<string, RectTransform>();
                const float BAR = 12f, OFF = 60f;
                void Ring(string n, C x0, C y0, C x1, C y1)
                {
                    var go = (GameObject)PrefabUtility.InstantiatePrefab(panelPrefab, frt);
                    go.name = "Panel_" + n;
                    var b = new Box(x0, y0, x1, y1);
                    SetBox((RectTransform)go.transform, b);
                    spec.All(spec.P(n), b);
                    map[n] = (RectTransform)go.transform;
                }
                Ring("frameTop", L(-OFF), R(-BAR), R(OFF), R(OFF));
                Ring("frameBottom", L(-OFF), L(-OFF), R(OFF), L(BAR));
                Ring("frameLeft", L(-OFF), L(-OFF), L(BAR), R(OFF));
                Ring("frameRight", R(-BAR), L(-OFF), R(OFF), R(OFF));

                foreach (var n in new[] { "Header", "Stats", "Disciplines", "Legend", "ZoomControls" })
                {
                    var panel = canvas.Find(n) as RectTransform;
                    if (panel == null) throw new System.Exception("Arcanum panel " + n + " missing");
                    var img = panel.GetComponent<Image>();
                    if (img != null) { var c = img.color; c.a = 0f; img.color = c; }   // keeps blocking clicks into the map
                    var go = (GameObject)PrefabUtility.InstantiatePrefab(panelPrefab, panel);
                    go.name = "FramePanel";
                    go.transform.SetAsFirstSibling();
                    var prt = (RectTransform)go.transform;
                    prt.anchorMin = Vector2.zero; prt.anchorMax = Vector2.one; prt.offsetMin = prt.offsetMax = Vector2.zero; prt.pivot = new Vector2(0.5f, 0.5f);
                    // the panel's rectangle as a responsive box (Arcanum canvas = 1080 high, width varies)
                    var b = new Box(K(panel.anchorMin.x, panel.offsetMin.x), K(panel.anchorMin.y, panel.offsetMin.y), K(panel.anchorMax.x, panel.offsetMax.x), K(panel.anchorMax.y, panel.offsetMax.y));
                    spec.All(spec.P(n), b);
                    map[n] = prt;
                }
                int joins = 0;
                foreach (var k in DetectJoins(spec, new Vector2(1920f, 1080f), new Vector2(1440f, 1080f), new Vector2(2580f, 1080f)))
                {
                    var go = (GameObject)PrefabUtility.InstantiatePrefab(joinPrefab, joinsRoot);
                    go.name = "Join_" + k;
                    var j = go.GetComponent<FrameJoin>();
                    j.a = map[k.a]; j.aRightEdge = k.aRight; j.b = map[k.b]; j.bTopEdge = k.bTop;
                    joins++;
                }
                PrefabUtility.SaveAsPrefabAsset(root, path);
                return "polish arcanum ok (joins " + joins + ")";
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }

        // ── 3. HUD: content slides with its backgrounds ──────────────────────

        public const float SlideDistance = 540f;

        private static string Hud()
        {
            var s = Need("Game");
            var hc = Object.FindFirstObjectByType<HudController>(FindObjectsInactive.Include);
            if (hc == null || hc.frame == null) throw new System.Exception("HudController/frame missing");
            var invRoot = hc.inventoryRoot.transform;
            var crafting = FindDeep(invRoot, "Crafting") as RectTransform;
            if (crafting.GetComponent<FrameFollower>() != null) return "ERR polish hud: already applied";

            // closed state: crafting, output and stem slide in from the left instead of growing from a point
            string path = PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(hc.frame.gameObject);
            var root = PrefabUtility.LoadPrefabContents(path);
            try
            {
                var f = root.GetComponent<UiFrame>();
                var open = f.Find("Open"); var closed = f.Find("Closed");
                foreach (var n in new[] { "Panel_stem", "Panel_crafting", "Panel_output" })
                {
                    var piece = root.transform.Find(n) as RectTransform;
                    var o = open.pieces.Find(p => p.piece == piece); var c = closed.pieces.Find(p => p.piece == piece);
                    c.anchorMin = o.anchorMin; c.anchorMax = o.anchorMax;
                    c.offsetMin = o.offsetMin - new Vector2(SlideDistance, 0f); c.offsetMax = o.offsetMax - new Vector2(SlideDistance, 0f);
                }
                PrefabUtility.SaveAsPrefabAsset(root, path);
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }

            var frame = hc.frame;
            frame.ApplyInstant(frame.Find("Open"));
            foreach (var g in hc.inventoryGroups) if (g != null) g.alpha = 1f;
            Canvas.ForceUpdateCanvases();
            var pCraft = frame.transform.Find("Panel_crafting") as RectTransform;
            var pInv = frame.transform.Find("Panel_inventory") as RectTransform;
            var pStatus = frame.transform.Find("Panel_status") as RectTransform;

            // crafting grid, output slot and arrow: rigid slide with the crafting panel
            var fc = crafting.gameObject.AddComponent<FrameFollower>();
            fc.piece = pCraft; fc.mode = FrameFollower.Mode.Translate; fc.corner = new Vector2(0f, 1f); fc.Capture();

            // inventory column: content rides the panel's top-left corner, clipped to the panel
            var invGrid = FindDeep(invRoot, "InventorySlots") as RectTransform;
            var invPanel = invGrid.parent as RectTransform;
            FollowRect(invPanel, pInv, Vector4.zero, new[] { invGrid }, new Vector2(0f, 1f));

            // stat labels: slide out from behind the status icons with the panel's right edge, clipped
            var desc = FindDeep(invRoot, "StatsDescription") as RectTransform;
            var labels = new List<RectTransform>();
            for (int i = 0; i < desc.childCount; i++) labels.Add((RectTransform)desc.GetChild(i));
            FollowRect(desc, pStatus, new Vector4(160f, 0f, 0f, 0f), labels.ToArray(), new Vector2(1f, 0.5f));

            hc.inventoryColumnGroup = invPanel.GetComponent<CanvasGroup>();
            hc.inventoryColumnPiece = pInv;
            EditorUtility.SetDirty(hc);

            // held-stack count on the cursor: Space Mono like every other count
            var cursor = Object.FindFirstObjectByType<Orivilon.Inventory.Inventory.InventoryCursor>(FindObjectsInactive.Include);
            if (cursor != null && cursor.stackAmountText != null) SetMono(cursor.stackAmountText, true);
            return Save(s, "hud");
        }

        /// <summary>Makes <paramref name="container"/> follow the piece's rectangle (with a RectMask2D) and
        /// re-anchors the given children to <paramref name="anchor"/> without moving them.</summary>
        private static void FollowRect(RectTransform container, RectTransform piece, Vector4 insets, RectTransform[] children, Vector2 anchor)
        {
            var world = new Vector3[children.Length];
            for (int i = 0; i < children.Length; i++) world[i] = children[i].position;
            var f = container.gameObject.AddComponent<FrameFollower>();
            f.piece = piece; f.mode = FrameFollower.Mode.Rect; f.insets = insets;
            f.Follow();
            Ensure<RectMask2D>(container.gameObject);
            for (int i = 0; i < children.Length; i++)
            {
                var c = children[i];
                Vector2 size = c.rect.size;
                c.anchorMin = c.anchorMax = anchor;
                c.sizeDelta = size;
                c.position = world[i];
                EditorUtility.SetDirty(c);
            }
        }

        // ── 4. hover ─────────────────────────────────────────────────────────

        public static HoverStyle EnsureHoverStyle()
        {
            var st = AssetDatabase.LoadAssetAtPath<HoverStyle>(HoverStylePath);
            if (st != null) return st;
            st = ScriptableObject.CreateInstance<HoverStyle>();
            st.fillSmall = Rounded("HoverFill_Small", 14f, Color.white, Color.white, 0f, Color.clear);
            st.fillLarge = Rounded("HoverFill_Large", 16f, Color.white, Color.white, 0f, Color.clear);
            AssetDatabase.CreateAsset(st, HoverStylePath);
            AssetDatabase.SaveAssets();
            return st;
        }

        private static string Hover()
        {
            var style = EnsureHoverStyle();
            var counts = new Dictionary<string, int> { ["Text"] = 0, ["Icon"] = 0, ["Fill"] = 0, ["skip"] = 0 };
            // prefabs used for dynamic content first (nested instances inherit)
            foreach (var p in new[] { ArcanumAuthoring.RowPrefabPath, ArcanumAuthoring.PrefabPath, "Assets/Resources/Prefabs/WorldListElement.prefab" })
                EditPrefab(p, style, counts);
            var scene = SceneManager.GetActiveScene();
            foreach (var r in scene.GetRootGameObjects())
                foreach (var b in r.GetComponentsInChildren<Button>(true)) Add(b, style, counts);
            // friends entries (prefab asset or scene template)
            foreach (var fl in Object.FindObjectsByType<Orivilon.UI.Menu.FriendsListUI>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                var so = new SerializedObject(fl);
                var e = so.FindProperty("entryPrefab").objectReferenceValue as GameObject;
                if (e == null) continue;
                if (EditorUtility.IsPersistent(e)) EditPrefab(AssetDatabase.GetAssetPath(e), style, counts);
                else foreach (var b in e.GetComponentsInChildren<Button>(true)) Add(b, style, counts);
            }
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            return "polish hover ok (" + scene.name + ": text " + counts["Text"] + ", icon " + counts["Icon"] + ", fill " + counts["Fill"] + ", already " + counts["skip"] + ")";
        }

        private static void EditPrefab(string path, HoverStyle style, Dictionary<string, int> counts)
        {
            if (AssetDatabase.LoadAssetAtPath<GameObject>(path) == null) return;
            var root = PrefabUtility.LoadPrefabContents(path);
            try
            {
                bool changed = false;
                foreach (var b in root.GetComponentsInChildren<Button>(true)) changed |= Add(b, style, counts);
                if (changed) PrefabUtility.SaveAsPrefabAsset(root, path);
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }

        /// <summary>Text buttons whose target graphic is their own text were classified as Fill – make them Text.</summary>
        private static string HoverFix()
        {
            int n = 0;
            foreach (var p in new[] { ArcanumAuthoring.RowPrefabPath, ArcanumAuthoring.PrefabPath, "Assets/Resources/Prefabs/WorldListElement.prefab" })
            {
                if (AssetDatabase.LoadAssetAtPath<GameObject>(p) == null) continue;
                var root = PrefabUtility.LoadPrefabContents(p);
                try
                {
                    int k = 0;
                    foreach (var h in root.GetComponentsInChildren<HoverTint>(true)) if (FixText(h)) k++;
                    if (k > 0) PrefabUtility.SaveAsPrefabAsset(root, p);
                    n += k;
                }
                finally { PrefabUtility.UnloadPrefabContents(root); }
            }
            var scene = SceneManager.GetActiveScene();
            foreach (var r in scene.GetRootGameObjects())
                foreach (var h in r.GetComponentsInChildren<HoverTint>(true)) if (FixText(h)) n++;
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            return "polish hoverfix ok (" + scene.name + ": text buttons fixed " + n + ")";
        }

        private static bool FixText(HoverTint h)
        {
            if (h.mode != HoverTint.Mode.Fill || h.targets == null || h.targets.Length == 0) return false;
            foreach (var g in h.targets) if (!(g is TMP_Text)) return false;
            var texts = new List<Graphic>();
            foreach (var t in h.GetComponentsInChildren<TMP_Text>(true)) if (!string.IsNullOrWhiteSpace(t.text) || t.gameObject.activeSelf) texts.Add(t);
            if (texts.Count == 0) return false;
            Undo.RecordObject(h, "hoverfix");
            h.mode = HoverTint.Mode.Text; h.targets = texts.ToArray(); h.labels = new TMP_Text[0]; h.fillSprite = null;
            EditorUtility.SetDirty(h);
            if (PrefabUtility.IsPartOfPrefabInstance(h)) PrefabUtility.RecordPrefabInstancePropertyModifications(h);
            return true;
        }

        /// <summary>Game: convert Translate followers to corner-to-corner offsets (placement unchanged at the authored size).</summary>
        private static string Refollow()
        {
            var s = Need("Game");
            var sb = new System.Text.StringBuilder();
            foreach (var r in s.GetRootGameObjects())
                foreach (var f in r.GetComponentsInChildren<FrameFollower>(true))
                {
                    if (f.mode != FrameFollower.Mode.Translate) continue;
                    Undo.RecordObject(f, "refollow");
                    var before = f.offset;
                    if (!f.UpgradeToCorner()) { sb.Append(f.name).Append(" already; "); continue; }
                    EditorUtility.SetDirty(f);
                    sb.Append(f.name).Append(' ').Append(before).Append("->").Append(f.offset).Append("; ");
                }
            return Save(s, "refollow") + " " + sb;
        }

        /// <summary>Game: inventory, hotbar and crafting slots get the same #565656 hover (Fill on the slot background).</summary>
        private static string Slots()
        {
            var s = Need("Game");
            var style = EnsureHoverStyle();
            int n = 0, skip = 0;
            var types = new System.Type[] { typeof(Orivilon.Inventory.Inventory.InventorySlot), typeof(Orivilon.Inventory.Crafting.CraftingSlot), typeof(Orivilon.Inventory.Crafting.CraftingOutputSlot) };
            foreach (var r in s.GetRootGameObjects())
                foreach (var t in types)
                    foreach (var c in r.GetComponentsInChildren(t, true))
                    {
                        if (c.GetComponent<HoverTint>() != null) { skip++; continue; }
                        var img = c.GetComponent<Image>();
                        if (img == null) continue;
                        var h = Undo.AddComponent<HoverTint>(c.gameObject);
                        h.style = style; h.mode = HoverTint.Mode.Fill; h.targets = new Graphic[] { img };
                        h.labels = c.GetComponentsInChildren<TMP_Text>(true); h.fillSprite = style.fillSmall;
                        EditorUtility.SetDirty(h); n++;
                    }
            return Save(s, "slots") + " slots " + n + " already " + skip;
        }

        // ── round 3 ──────────────────────────────────────────────────────────

        /// <summary>Thin white ring with the slot's corner radius (alpha = coverage, colour white).</summary>
        private static Sprite SlotRing(float radius, float width)
        {
            string path = SpriteDir + "/SlotHover_Outline.png";
            var existing = AssetDatabase.LoadAssetAtPath<Sprite>(path);
            if (existing != null) return existing;
            int size = Mathf.CeilToInt(radius * 2f + 8f);
            var tex = NewTex(size, size);
            var px = new Color[size * size];
            float half = size * 0.5f;
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                Vector2 p = new Vector2(x + 0.5f - half, y + 0.5f - half);
                Vector2 q = new Vector2(Mathf.Abs(p.x) - (half - 2f - radius), Mathf.Abs(p.y) - (half - 2f - radius));
                float d = new Vector2(Mathf.Max(q.x, 0f), Mathf.Max(q.y, 0f)).magnitude + Mathf.Min(Mathf.Max(q.x, q.y), 0f) - radius;
                float a = Mathf.Clamp01(0.5f - d) * Mathf.Clamp01(0.5f + d + width);
                px[y * size + x] = new Color(1f, 1f, 1f, a);
            }
            tex.SetPixels(px); tex.Apply(false, false);
            float bd = Mathf.Ceil(radius + 3f);
            return SaveSprite("SlotHover_Outline", tex, new Vector4(bd, bd, bd, bd));
        }

        /// <summary>Game: item slots switch from the grey fill hover to a thin light outline (child "HoverOutline").</summary>
        private static string SlotOutline()
        {
            var s = Need("Game");
            var style = EnsureHoverStyle();
            if (style.slotOutline == null)
            {
                style.slotOutline = SlotRing(16f, 2f);
                EditorUtility.SetDirty(style); AssetDatabase.SaveAssets();
            }
            int n = 0, skip = 0;
            var types = new System.Type[] { typeof(Orivilon.Inventory.Inventory.InventorySlot), typeof(Orivilon.Inventory.Crafting.CraftingSlot), typeof(Orivilon.Inventory.Crafting.CraftingOutputSlot) };
            foreach (var r in s.GetRootGameObjects())
                foreach (var t in types)
                    foreach (var c in r.GetComponentsInChildren(t, true))
                    {
                        var h = c.GetComponent<HoverTint>();
                        if (h == null) continue;
                        if (h.mode == HoverTint.Mode.Outline && h.outline != null) { skip++; continue; }
                        var tr = c.transform.Find("HoverOutline");
                        GameObject go;
                        if (tr == null)
                        {
                            go = new GameObject("HoverOutline", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(LayoutElement));
                            Undo.RegisterCreatedObjectUndo(go, "slot outline");
                            go.transform.SetParent(c.transform, false);
                        }
                        else go = tr.gameObject;
                        go.transform.SetAsLastSibling();
                        go.layer = c.gameObject.layer;
                        var rt = (RectTransform)go.transform;
                        rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one; rt.pivot = new Vector2(0.5f, 0.5f);
                        rt.offsetMin = new Vector2(2f, 2f); rt.offsetMax = new Vector2(-2f, -2f);
                        go.GetComponent<LayoutElement>().ignoreLayout = true;
                        var img = go.GetComponent<Image>();
                        img.sprite = style.slotOutline; img.type = Image.Type.Sliced; img.pixelsPerUnitMultiplier = 1f;
                        img.color = style.slotOutlineColor; img.raycastTarget = false; img.enabled = false;
                        Undo.RecordObject(h, "slot outline");
                        h.mode = HoverTint.Mode.Outline; h.targets = new Graphic[0]; h.labels = new TMP_Text[0]; h.fillSprite = null; h.outline = img;
                        EditorUtility.SetDirty(h); EditorUtility.SetDirty(img);
                        n++;
                    }
            return Save(s, "slotoutline") + " slots " + n + " already " + skip;
        }

        /// <summary>Game: F1 clean view controller on its own always-active object.</summary>
        private static string CleanViewStep()
        {
            var s = Need("Game");
            foreach (var r in s.GetRootGameObjects())
                if (r.GetComponentInChildren<CleanView>(true) != null) return "ERR polish cleanview: already applied";
            var go = new GameObject("CleanView (F1)");
            Undo.RegisterCreatedObjectUndo(go, "clean view");
            SceneManager.MoveGameObjectToScene(go, s);
            go.AddComponent<CleanView>();
            return Save(s, "cleanview");
        }

        /// <summary>LoadingScreen: flavor line under the world name + fade group for the loading controller.</summary>
        private static string LoadText()
        {
            var s = Need("LoadingScreen");
            LoadingController lc = null;
            foreach (var r in s.GetRootGameObjects()) { lc = r.GetComponentInChildren<LoadingController>(true); if (lc != null) break; }
            if (lc == null) throw new System.Exception("LoadingController not found");
            if (lc.flavorText != null) return "ERR polish loadtext: already applied";
            if (lc.worldName == null) throw new System.Exception("world name text missing");
            var src = lc.worldName;
            var copy = Object.Instantiate(src.gameObject, src.transform.parent);
            Undo.RegisterCreatedObjectUndo(copy, "flavor");
            copy.name = "FlavorText";
            copy.transform.SetSiblingIndex(src.transform.GetSiblingIndex() + 1);
            foreach (var mb in copy.GetComponents<MonoBehaviour>())
                if (!(mb is TMP_Text)) Object.DestroyImmediate(mb);
            var t = copy.GetComponent<TMP_Text>();
            var srt = (RectTransform)src.transform; var rt = (RectTransform)copy.transform;
            rt.anchoredPosition = srt.anchoredPosition - new Vector2(0f, Mathf.Max(srt.rect.height, src.fontSize * 1.3f) + 6f);
            SetMono(t, false, Mathf.Round(src.fontSize * 0.8f));
            var c = src.color; t.color = new Color(c.r, c.g, c.b, c.a * 0.6f);
            t.fontStyle |= FontStyles.Italic;
            t.text = "Polishing triangles…";
            t.raycastTarget = false;
            var panel = lc.manager != null && lc.manager.loadingPanel != null ? lc.manager.loadingPanel : lc.gameObject;
            var g = panel.GetComponent<CanvasGroup>();
            if (g == null) g = Undo.AddComponent<CanvasGroup>(panel);
            Undo.RecordObject(lc, "loadtext");
            lc.flavorText = t; lc.fadeGroup = g;
            EditorUtility.SetDirty(lc); EditorUtility.SetDirty(t);
            return Save(s, "loadtext") + " flavor under '" + src.name + "' panel " + panel.name;
        }

        /// <summary>MainMenu: "Back to worlds" also shows the "Create New World" tab again (it hides itself on click).</summary>
        private static string BackFix()
        {
            var s = Need("MainMenu");
            Button back = null; GameObject create = null;
            foreach (var r in s.GetRootGameObjects())
            {
                foreach (var b in r.GetComponentsInChildren<Button>(true))
                {
                    if (b.name == "BackToWorlds") back = b;
                    if (b.name == "NewWorlButton") create = b.gameObject;
                }
            }
            if (back == null || create == null) throw new System.Exception("BackToWorlds / NewWorlButton not found");
            for (int i = 0; i < back.onClick.GetPersistentEventCount(); i++)
                if (back.onClick.GetPersistentTarget(i) == create && back.onClick.GetPersistentMethodName(i) == "SetActive")
                    return "polish backfix: already done";
            Undo.RecordObject(back, "backfix");
            UnityEditor.Events.UnityEventTools.AddBoolPersistentListener(back.onClick, create.SetActive, true);
            EditorUtility.SetDirty(back);
            if (PrefabUtility.IsPartOfPrefabInstance(back)) PrefabUtility.RecordPrefabInstancePropertyModifications(back);
            return Save(s, "backfix");
        }

        /// <summary>Classifies a button and adds a configured HoverTint. Returns true when it added one.</summary>
        public static bool Add(Button b, HoverStyle style, Dictionary<string, int> counts)
        {
            if (b.GetComponent<HoverTint>() != null) { counts["skip"]++; return false; }
            var tg = b.targetGraphic;
            var texts = new List<TMP_Text>();
            foreach (var t in b.GetComponentsInChildren<TMP_Text>(true)) if (!string.IsNullOrWhiteSpace(t.text) || t.gameObject.activeSelf) texts.Add(t);
            var h = b.gameObject.AddComponent<HoverTint>();
            h.style = style; h.selectable = b;
            bool invisibleBg = tg is TMP_Text || tg == null || tg.color.a < 0.05f || (tg is Image ti && ti.sprite == null && ti.color.a < 0.05f);
            if (invisibleBg && texts.Count > 0)
            {
                h.mode = HoverTint.Mode.Text; h.targets = texts.ToArray();
            }
            else if (texts.Count == 0 || tg == null)
            {
                h.mode = HoverTint.Mode.Icon;
                h.targets = tg != null ? new[] { tg } : b.GetComponentsInChildren<Image>(true);
            }
            else
            {
                h.mode = HoverTint.Mode.Fill; h.targets = new[] { tg }; h.labels = texts.ToArray();
                if (tg is Image img && img.sprite != null && img.type == Image.Type.Sliced)
                    h.fillSprite = img.sprite.border.x >= 16f ? style.fillLarge : style.fillSmall;
            }
            // the button's own tint must not multiply the hover colour
            if (b.transition == Selectable.Transition.ColorTint)
            {
                var cb = b.colors; cb.highlightedColor = cb.normalColor; b.colors = cb;
            }
            counts[h.mode.ToString()]++;
            EditorUtility.SetDirty(b); EditorUtility.SetDirty(h);
            return true;
        }
    }
}
