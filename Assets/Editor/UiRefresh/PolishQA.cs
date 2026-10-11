using System.Collections.Generic;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Orivilon.UI.Refresh.EditorTools
{
    /// <summary>
    /// Play Mode QA for the polish pass (editor only, bridge commands). These are SIMULATED inputs:
    /// pointer events are sent through EventSystem, inventory picks call the same InventoryCursor
    /// methods the slot handlers call.
    ///   hoverenter / hovermeasure / hoverexit – hover every visible interactable HoverTint button and
    ///       read the colour actually rendered (Graphic colour × CanvasRenderer tint, i.e. incl. the
    ///       button's ColorBlock), then check that exit restores it
    ///   hoverstate               – reports buttons currently showing hover (for real-mouse tests)
    ///   dragtest pick|split|drop – picks up / splits a multi-item stack onto the cursor, reports its count text and font
    /// </summary>
    public static class PolishQA
    {
        private static readonly List<HoverTint> tested = new List<HoverTint>();
        private static readonly Dictionary<HoverTint, Color> before = new Dictionary<HoverTint, Color>();
        private static int dragSlot = -1;

        private static Color Effective(Graphic g) => g.color * g.canvasRenderer.GetColor();
        private static string Hex(Color c) => ColorUtility.ToHtmlStringRGB(c);

        private static PointerEventData Ped() => new PointerEventData(EventSystem.current);

        private static Graphic Probe(HoverTint h)
        {
            foreach (var g in h.targets) if (g != null && g.isActiveAndEnabled) return g;
            return null;
        }

        public static string Enter()
        {
            tested.Clear(); before.Clear();
            foreach (var h in Object.FindObjectsByType<HoverTint>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
            {
                if (!h.isActiveAndEnabled || (h.selectable != null && !h.selectable.IsInteractable())) continue;
                var g = Probe(h); if (g == null) continue;
                before[h] = Effective(g);
                tested.Add(h);
                ExecuteEvents.Execute(h.gameObject, Ped(), ExecuteEvents.pointerEnterHandler);
            }
            return "hoverenter " + tested.Count;
        }

        public static string Measure()
        {
            int ok = 0; var bad = new List<string>(); var sample = new StringBuilder();
            var modes = new Dictionary<HoverTint.Mode, int>();
            foreach (var h in tested)
            {
                if (h == null) continue;
                var g = Probe(h); if (g == null) continue;
                Color e = Effective(g);
                bool match = Mathf.Abs(e.r - h.style.hoverColor.r) < 1.5f / 255f && Mathf.Abs(e.g - h.style.hoverColor.g) < 1.5f / 255f && Mathf.Abs(e.b - h.style.hoverColor.b) < 1.5f / 255f;
                if (match) { ok++; modes[h.mode] = (modes.TryGetValue(h.mode, out var n) ? n : 0) + 1; }
                else bad.Add(h.name + "(" + h.mode + " #" + Hex(e) + ")");
                if (sample.Length < 600) sample.Append(h.name).Append(':').Append(h.mode).Append(" #").Append(Hex(e)).Append("; ");
            }
            var ms = new StringBuilder();
            foreach (var kv in modes) ms.Append(kv.Key).Append(' ').Append(kv.Value).Append(' ');
            return "hovermeasure ok " + ok + "/" + tested.Count + " [" + ms + "]" + (bad.Count > 0 ? " MISMATCH " + string.Join(", ", bad) : "") + " | " + sample;
        }

        public static string Exit()
        {
            foreach (var h in tested)
            {
                if (h == null) continue;
                ExecuteEvents.Execute(h.gameObject, Ped(), ExecuteEvents.pointerExitHandler);
            }
            return "hoverexit sent " + tested.Count;
        }

        public static string CheckRestored()
        {
            int ok = 0; var bad = new List<string>();
            foreach (var h in tested)
            {
                if (h == null) continue;
                var g = Probe(h); if (g == null) continue;
                Color e = Effective(g), b = before[h];
                if (Mathf.Abs(e.r - b.r) < 1.5f / 255f && Mathf.Abs(e.g - b.g) < 1.5f / 255f && Mathf.Abs(e.b - b.b) < 1.5f / 255f && Mathf.Abs(e.a - b.a) < 0.02f) ok++;
                else bad.Add(h.name + " #" + Hex(b) + "→#" + Hex(e));
            }
            return "hoverrestored " + ok + "/" + tested.Count + (bad.Count > 0 ? " DIFF " + string.Join(", ", bad) : "");
        }

        public static string State()
        {
            var sb = new StringBuilder();
            int n = 0;
            foreach (var h in Object.FindObjectsByType<HoverTint>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
                if (h.IsApplied)
                {
                    n++; var g = h.mode == HoverTint.Mode.Outline ? h.outline : Probe(h);
                    sb.Append(h.name).Append(':').Append(h.mode).Append(" #").Append(g != null ? Hex(Effective(g)) : "-");
                    if (h.mode == HoverTint.Mode.Outline && h.outline != null) sb.Append(" outlineOn ").Append(h.outline.enabled);
                    sb.Append("; ");
                }
            return "hoverstate " + n + " hovered " + sb;
        }

        public static string Drag(string what)
        {
            var cursor = Orivilon.Inventory.Inventory.InventoryCursor.Instance;
            var inv = Orivilon.Inventory.Inventory.InventoryData.Instance;
            if (cursor == null || inv == null) return "ERR dragtest: inventory not ready";
            var slots = inv.Slots;
            if (what == "pick" || what == "split")
            {
                dragSlot = -1;
                for (int i = 0; i < slots.Length; i++) if (slots[i] != null && !slots[i].IsEmpty && slots[i].amount > 2) { dragSlot = i; break; }
                if (dragSlot < 0) return "ERR dragtest: no stack > 2";
                int had = slots[dragSlot].amount;
                if (what == "pick") cursor.TakeFromSlot(dragSlot); else cursor.RightClickOnSlot(dragSlot);
                var t = cursor.stackAmountText;
                return "dragtest " + what + " slot " + dragSlot + " had " + had + " held " + (cursor.HeldSlot != null ? cursor.HeldSlot.amount : 0) +
                       " text '" + (t != null ? t.text : "-") + "' font " + (t != null && t.font != null ? t.font.name : "-") + " size " + (t != null ? t.fontSize : 0) +
                       " cursorActive " + (cursor.cursorImage != null && cursor.cursorImage.isActiveAndEnabled);
            }
            if (what == "drop")
            {
                if (dragSlot < 0 || cursor.HeldSlot == null) return "dragtest drop: nothing held";
                cursor.PlaceIntoSlot(dragSlot);
                return "dragtest drop slot " + dragSlot + " now " + slots[dragSlot].amount + " held " + (cursor.HeldSlot != null ? cursor.HeldSlot.amount : 0);
            }
            return "ERR dragtest pick|split|drop";
        }

        /// <summary>
        /// Toggles the inventory (same GameManager method the key calls) and captures the Game view at the
        /// given real-time delays (seconds) – the bridge itself is too slow to hit mid-transition frames.
        /// Each capture logs the elapsed time, the inventory piece height and the content position so the
        /// content can be checked to move together with its background.
        /// </summary>
        public static string ToggleShots(string dir, string name, string[] tokens)
        {
            var gm = Orivilon.Core.GameManager.instance;
            var mi = typeof(Orivilon.Core.GameManager).GetMethod("ToggleInventory", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            if (gm == null || mi == null) return "ERR invshots";
            // "0.1" = capture at 0.1 s, "t0.08" = toggle again at 0.08 s (rapid reversal)
            var delays = new float[tokens.Length]; var isToggle = new bool[tokens.Length];
            for (int i = 0; i < tokens.Length; i++)
            {
                isToggle[i] = tokens[i].StartsWith("t");
                delays[i] = float.Parse(isToggle[i] ? tokens[i].Substring(1) : tokens[i], System.Globalization.CultureInfo.InvariantCulture);
            }
            double t0 = UnityEditor.EditorApplication.timeSinceStartup;
            int shot = 0;
            int next = 0;
            var log = new StringBuilder();
            UnityEditor.EditorApplication.CallbackFunction cb = null;
            cb = () =>
            {
                double el = UnityEditor.EditorApplication.timeSinceStartup - t0;
                while (next < delays.Length && el >= delays[next])
                {
                    if (isToggle[next]) { mi.Invoke(gm, null); log.Append("TOGGLE t=").Append(el.ToString("0.000")).Append(' ').Append(Probe()).Append("; "); }
                    else
                    {
                        string path = System.IO.Path.Combine(dir, name + "_" + shot + ".png");
                        ScreenCapture.CaptureScreenshot(path);
                        log.Append(name).Append('_').Append(shot).Append(" t=").Append(el.ToString("0.000")).Append(' ').Append(Probe()).Append("; ");
                        shot++; next++;
                        break;   // one capture per frame
                    }
                    next++;
                }
                if (next >= delays.Length || el > 5)
                {
                    UnityEditor.EditorApplication.update -= cb;
                    Debug.Log("[PolishQA] invshots " + log);
                    System.IO.File.AppendAllText(System.IO.Path.Combine(dir, "out.log"), System.DateTime.Now.ToString("HH:mm:ss") + " invshots " + log + "\n");
                }
            };
            mi.Invoke(gm, null);
            UnityEditor.EditorApplication.update += cb;
            return "invshots " + name + " toggled, " + delays.Length + " captures scheduled";
        }

        private static string Probe()
        {
            var h = HudController.Instance;
            if (h == null || h.inventoryColumnPiece == null) return "-";
            var sb = new StringBuilder();
            sb.Append("invH ").Append(h.inventoryColumnPiece.rect.height.ToString("0")).Append(" colA ").Append(h.inventoryColumnGroup != null ? h.inventoryColumnGroup.alpha.ToString("0.00") : "-");
            foreach (var f in Object.FindObjectsByType<FrameFollower>(FindObjectsSortMode.None))
            {
                if (f.piece == null) continue;
                var pc = new Vector3[4]; var fc = new Vector3[4];
                f.piece.GetWorldCorners(pc); ((RectTransform)f.transform).GetWorldCorners(fc);
                sb.Append(' ').Append(f.name).Append(" bgTL(").Append(pc[1].x.ToString("0")).Append(',').Append(pc[1].y.ToString("0")).Append(") contentTL(").Append(fc[1].x.ToString("0")).Append(',').Append(fc[1].y.ToString("0")).Append(')');
            }
            return sb.ToString();
        }

        // ── round 3: loading probe, clean view probe ─────────────────────

        private static UnityEditor.EditorApplication.CallbackFunction loadCb;

        /// <summary>
        /// Samples the loading screen every 0.2 s (real time) into out.log: shown/real progress, stage, status
        /// and flavor text, the worst frame gap since the previous sample (main-thread stalls) and when the
        /// game reports ready. Stops by itself 2 s after loading has finished.
        /// </summary>
        public static string LoadProbe(string dir)
        {
            if (loadCb != null) UnityEditor.EditorApplication.update -= loadCb;
            double t0 = UnityEditor.EditorApplication.timeSinceStartup, last = t0, lastSample = t0, doneAt = -1;
            double worstGap = 0;
            var log = new StringBuilder();
            bool sawLoading = false;
            loadCb = () =>
            {
                double now = UnityEditor.EditorApplication.timeSinceStartup;
                worstGap = System.Math.Max(worstGap, now - last); last = now;
                if (now - lastSample < 0.2) return;
                lastSample = now;
                var lc = LoadingController.Instance;
                bool loading = Orivilon.Core.SceneLoader.IsLoading;
                sawLoading |= loading || lc != null;
                var gm = Orivilon.Core.GameManager.instance;
                string line = "t=" + (now - t0).ToString("0.0") + (lc != null
                    ? " shown " + (lc.Shown * 100f).ToString("0.0") + "% real " + (lc.Real * 100f).ToString("0.0") + "% " + lc.Stage + " new=" + lc.IsNewWorld +
                      " status='" + (lc.manager != null && lc.manager.statusText != null ? lc.manager.statusText.text : "-") + "' pct='" + (lc.manager != null && lc.manager.progressText != null ? lc.manager.progressText.text : "-") +
                      "' flavor='" + (lc.flavorText != null ? lc.flavorText.text : "-") + "' alpha " + (lc.fadeGroup != null ? lc.fadeGroup.alpha.ToString("0.00") : "-")
                    : " (no loading screen)") +
                    " chunks " + (Orivilon.ChunkLoaderAPI.GetLoadProgress() * 100f).ToString("0") + "% isLoading " + loading +
                    " ready " + (gm != null && gm.IsLoadingComplete) + " worstGap " + (worstGap * 1000).ToString("0") + "ms";
                worstGap = 0;
                System.IO.File.AppendAllText(System.IO.Path.Combine(dir, "loadprobe.log"), line + "\n");
                if (sawLoading && !loading && lc == null && doneAt < 0) doneAt = now;
                if ((doneAt > 0 && now - doneAt > 2) || now - t0 > 180)
                {
                    UnityEditor.EditorApplication.update -= loadCb; loadCb = null;
                    System.IO.File.AppendAllText(System.IO.Path.Combine(dir, "loadprobe.log"), "end\n");
                }
            };
            System.IO.File.AppendAllText(System.IO.Path.Combine(dir, "loadprobe.log"), "== loadprobe " + System.DateTime.Now.ToString("HH:mm:ss") + "\n");
            UnityEditor.EditorApplication.update += loadCb;
            return "loadprobe started";
        }

        /// <summary>clean on|off|state – method-call clean view toggle and a check for anything still visible or clickable.</summary>
        public static string Clean(string what)
        {
            var cv = CleanView.Instance;
            if (cv == null) return "ERR clean: no CleanView";
            if (what == "on") cv.Enter();
            else if (what == "off") cv.Exit();
            var sb = new StringBuilder("clean " + what + ": active " + cv.IsActive + " hidden " + cv.HiddenCanvasCount);
            int vis = 0, rc = 0; var names = new List<string>();
            foreach (var c in Object.FindObjectsByType<Canvas>(FindObjectsSortMode.None))
            {
                if (!c.isRootCanvas || !c.enabled || !c.gameObject.activeInHierarchy) continue;
                if (c.renderMode == RenderMode.ScreenSpaceOverlay && c.targetDisplay != 0) continue;
                bool any = false;
                foreach (var g in c.GetComponentsInChildren<Graphic>()) if (g.isActiveAndEnabled && g.canvasRenderer.GetAlpha() * g.color.a > 0.01f && !g.canvasRenderer.cull) { any = true; break; }
                var grp = c.GetComponent<CanvasGroup>();
                if (grp != null && grp.alpha < 0.01f) any = false;
                if (any) { vis++; names.Add(c.name); }
            }
            foreach (var r in Object.FindObjectsByType<GraphicRaycaster>(FindObjectsSortMode.None)) if (r.isActiveAndEnabled) rc++;
            int hits = 0;
            if (EventSystem.current != null)
            {
                var res = new List<RaycastResult>();
                for (int i = 1; i <= 3; i++)
                    for (int j = 1; j <= 3; j++)
                    {
                        var pe = new PointerEventData(EventSystem.current) { position = new Vector2(Screen.width * i / 4f, Screen.height * j / 4f) };
                        res.Clear(); EventSystem.current.RaycastAll(pe, res); hits += res.Count;
                    }
            }
            sb.Append(" | visible canvases ").Append(vis).Append(names.Count > 0 ? " (" + string.Join(",", names) + ")" : "")
              .Append(" raycasters on ").Append(rc).Append(" ui hits(9 pts) ").Append(hits)
              .Append(" cursor ").Append(Cursor.lockState).Append('/').Append(Cursor.visible ? "visible" : "hidden")
              .Append(" console ").Append(Orivilon.Core.GameConsole.IsOpen);
            var gm = Orivilon.Core.GameManager.instance;
            if (gm != null) sb.Append(" paused ").Append(gm.IsPaused).Append(" arcanum ").Append(gm.IsArcanumOpen).Append(" menuOpen ").Append(gm.isMenuOpen);
            var cur = Orivilon.Inventory.Inventory.InventoryCursor.Instance;
            if (cur != null) sb.Append(" held ").Append(cur.HeldSlot != null ? cur.HeldSlot.amount : 0);
            return sb.ToString();
        }

        /// <summary>Diagnostics for one named object: components, Selectable colours, HoverTint targets.</summary>
        public static string Info(string name)
        {
            var sb = new StringBuilder("hoverinfo " + name + ": ");
            foreach (var t in Object.FindObjectsByType<RectTransform>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (t.name != name) continue;
                sb.Append("[active ").Append(t.gameObject.activeInHierarchy).Append(" comps ");
                foreach (var c in t.GetComponents<Component>()) sb.Append(c != null ? c.GetType().Name : "null").Append(',');
                var s = t.GetComponent<Selectable>();
                if (s != null)
                    sb.Append(" sel ").Append(s.transition).Append(" interactable ").Append(s.IsInteractable()).Append(" tg ").Append(s.targetGraphic != null ? s.targetGraphic.name : "-")
                      .Append(" n#").Append(Hex(s.colors.normalColor)).Append(" h#").Append(Hex(s.colors.highlightedColor)).Append(" x").Append(s.colors.colorMultiplier);
                var h = t.GetComponent<HoverTint>();
                if (h != null)
                {
                    sb.Append(" hover ").Append(h.mode).Append(" applied ").Append(h.IsApplied).Append(" targets ");
                    foreach (var g in h.targets) if (g != null) sb.Append(g.name).Append(" #").Append(Hex(g.color)).Append("*#").Append(Hex(g.canvasRenderer.GetColor())).Append(' ');
                }
                sb.Append("] ");
                if (sb.Length > 1500) break;
            }
            return sb.ToString();
        }

        /// <summary>Positions the held-stack cursor (normally at the mouse) for a screenshot.</summary>
        public static string CursorAt(float x, float y)
        {
            var cursor = Orivilon.Inventory.Inventory.InventoryCursor.Instance;
            if (cursor == null || cursor.cursorImage == null) return "ERR cursorat";
            cursor.cursorImage.transform.position = new Vector3(x, y, 0f);
            return "cursorat " + x + "," + y;
        }
    }
}
