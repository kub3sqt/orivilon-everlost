using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using Orivilon.Core;
using Orivilon.Inventory.Crafting;
using Orivilon.Inventory.Hotbar;
using Orivilon.Inventory.Inventory;
using Orivilon.World.Objects;
using UnityEditor;
using UnityEngine;
using A = Orivilon.Inventory.Inventory.InventoryActions;

namespace Orivilon.EditorTools
{
    /// <summary>
    /// Drop systém 16.10.2026: Play Mode kroky v izolovaném QA světě (qalaunch). Každý krok je zvlášť,
    /// aby mezi nimi mohly běžet snímky fyziky, čekání na ochranu před sebráním a teleport (bridge „wait“).
    /// Výstup: _reports/remote/drop_playqa.txt (+ historie), očekávané stavy v drop_playqa_state.txt.
    /// </summary>
    internal static class DropPlayQA
    {
        private static readonly StringBuilder sb = new StringBuilder();
        private static int fail;
        private static string Root => Path.GetDirectoryName(Application.dataPath);
        private static string Dir => Path.Combine(Root, "_reports", "remote");
        private static string StateFile => Path.Combine(Dir, "drop_playqa_state.txt");

        private static void Log(string s) => sb.AppendLine(s);
        private static void Ok(bool c, string what) { if (!c) fail++; Log((c ? "ok   " : "FAIL ") + what); }

        private static void Run(string title, Action body)
        {
            sb.Length = 0; fail = 0;
            try
            {
                if (!Application.isPlaying || InventoryData.Instance == null || GameManager.instance == null) throw new Exception("Game scéna neběží");
                string folder = GameManager.selectedWorld != null ? GameManager.selectedWorld.folderPath ?? "" : "";
                if (!folder.Replace('\\', '/').Contains("/_reports/ui-refresh/qa-saves/")) throw new Exception("odmítnuto: svět mimo izolované QA saves (" + folder + ")");
                body();
            }
            catch (Exception e) { fail++; Log("FAIL výjimka: " + e); }
            finally { ItemDropService.ForceFail = false; }
            string text = title + ": " + (fail == 0 ? "OK" : fail + " FAIL") + "\n" + sb;
            File.WriteAllText(Path.Combine(Dir, "drop_playqa.txt"), text);
            File.AppendAllText(Path.Combine(Dir, "drop_playqa_history.txt"), DateTime.Now.ToString("HH:mm:ss ") + text + "\n");
            if (fail == 0) Debug.Log("[DropPlayQA] " + title + " OK"); else Debug.LogError("[DropPlayQA] " + text);
        }

        private static InventoryItemData Item(string id)
        {
            foreach (var it in Resources.LoadAll<InventoryItemData>("")) if (it.id == id) return it;
            throw new Exception("chybí item " + id);
        }

        /// <summary>Počty po itemech: inventář + kurzor + grid + svět.</summary>
        private static string Totals()
        {
            var d = new SortedDictionary<string, int>();
            void Add(string id, int n) { if (n <= 0) return; d.TryGetValue(id, out int c); d[id] = c + n; }
            foreach (var s in InventoryData.Instance.Slots) if (!s.IsEmpty) Add(s.item.id, s.amount);
            if (CraftingData.Instance != null) foreach (var s in CraftingData.Instance.gridSlots) if (!s.IsEmpty) Add(s.item.id, s.amount);
            if (!A.Cursor.IsEmpty) Add(A.Cursor.item.id, A.Cursor.amount);
            foreach (var x in ItemDropService.Active) if (x != null && x.Pickup != null && x.Pickup.itemCount > 0) Add(x.Pickup.itemData.id, x.Pickup.itemCount);
            var b = new StringBuilder();
            foreach (var kv in d) b.Append(kv.Key).Append('=').Append(kv.Value).Append(' ');
            return b.ToString().Trim();
        }

        private static string WorldList()
        {
            var b = new StringBuilder();
            var list = new List<string>();
            foreach (var x in ItemDropService.Active)
                if (x != null && x.Pickup != null && x.Pickup.itemCount > 0)
                    list.Add(x.Pickup.itemData.id + "×" + x.Pickup.itemCount + "@" + V(x.RestPosition));
            list.Sort();
            foreach (var s in list) b.Append(s).Append(';');
            return b.ToString();
        }

        private static string V(Vector3 v) => v.x.ToString("0.0") + "," + v.y.ToString("0.0") + "," + v.z.ToString("0.0");

        private static int Inv(InventoryItemData it) { int n = 0; foreach (var s in InventoryData.Instance.Slots) if (!s.IsEmpty && s.item == it) n += s.amount; return n; }

        private static int SlotOf(InventoryItemData it)
        {
            var s = InventoryData.Instance.Slots;
            for (int i = 0; i < InventoryData.HotbarSize; i++) if (!s[i].IsEmpty && s[i].item == it) return i;
            for (int i = 0; i < InventoryData.HotbarSize; i++) if (s[i].IsEmpty) { int j = Array.FindIndex(s, x => !x.IsEmpty && x.item == it); if (j >= 0) { s[i].Set(s[j].item, s[j].amount); s[j].Clear(); A.Commit(); return i; } }
            throw new Exception("item " + it.id + " není v inventáři nebo není volný hotbar");
        }

        private static Toggle_ T = new Toggle_();
        private class Toggle_ { public void Inv() => typeof(GameManager).GetMethod("ToggleInventory", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(GameManager.instance, null); }

        /// <summary>Registr přidává v OnEnable na konec – poslední je nejnovější.</summary>
        private static DroppedItem Newest() => ItemDropService.Active.Count > 0 ? ItemDropService.Active[ItemDropService.Active.Count - 1] : null;

        private static void CheckPlacement(DroppedItem d, string what)
        {
            var player = GameObject.FindWithTag("Player");
            var pb = player.GetComponent<CapsuleCollider>().bounds;
            Vector3 to = d.transform.position - pb.center; to.y = 0f;
            var cam = Camera.main.transform.forward; cam.y = 0f;
            Ok(to.magnitude > Mathf.Max(pb.extents.x, pb.extents.z) && to.magnitude < 12f, what + ": vzdálenost od hráče " + to.magnitude.ToString("0.0") + " m");
            Ok(Vector3.Dot(to.normalized, cam.normalized) > 0.3f, what + ": před hráčem (dot " + Vector3.Dot(to.normalized, cam.normalized).ToString("0.00") + ")");
            Ok(!pb.Intersects(d.GetComponent<Collider>().bounds), what + ": nepřekrývá hráče");
        }

        // ───────── krok 1: vyhazování ─────────
        [MenuItem("Tools/Everlost/Drop QA/1 Drop")]
        public static void Step1() => Run("1 Drop", () =>
        {
            var gm = GameManager.instance; var inv = InventoryData.Instance; var hot = HotbarController.Instance;
            if (gm.IsInventoryOpen) T.Inv();
            var stone = Item("everlost:stone"); var axe = Item("everlost:stone_axe"); var log = Item("everlost:log"); var stick = Item("everlost:stick");
            inv.AddItem(stone, 12); inv.AddItem(axe, 1); inv.AddItem(log, 8); inv.AddItem(stick, 5);
            string before = Totals(); Log("počty " + before + " | svět " + ItemDropService.Active.Count);
            Ok(ItemDropService.GameplayInputAllowed(), "vstup ve hře povolen");

            int si = SlotOf(stone); hot.SetActiveSlot(si);
            int s0 = Inv(stone);
            Ok(hot.DropActive(false), "Q z hotbaru");
            var d1 = Newest();
            Ok(Inv(stone) == s0 - 1 && d1 != null && d1.Pickup.itemCount == 1, "Q: −1 v inventáři, 1 ve světě");
            CheckPlacement(d1, "Q");
            d1.Pickup.PickUp();
            Ok(d1 != null && d1.Pickup.itemCount == 1 && Inv(stone) == s0 - 1, "okamžité sebrání zablokováno ochranou");
            Ok(hot.DropActive(true), "Ctrl+Q z hotbaru");
            Ok(Inv(stone) == 0 && Newest().Pickup.itemCount == s0 - 1, "Ctrl+Q: celý stack (" + (s0 - 1) + ")");
            Ok(!hot.DropActive(false), "prázdný slot: nic");

            hot.SetActiveSlot(SlotOf(axe));
            Ok(hot.DropActive(true) && Inv(axe) == 0 && Newest().Pickup.itemCount == 1, "nástroj maxStack 1");

            hot.SetActiveSlot(SlotOf(log));
            for (int i = 0; i < 20; i++) hot.DropActive(false);
            Ok(Inv(log) == 0, "rychlé opakování Q vyprázdnilo 8 klád");
            Ok(Totals() == before, "počty po vyhazování (" + Totals() + ")");

            int sti = SlotOf(stick); hot.SetActiveSlot(sti);
            ItemDropService.ForceFail = true;
            Ok(!hot.DropActive(true) && inv.Slots[sti].amount == 5, "selhání spawnu: zdroj ponechán");
            ItemDropService.ForceFail = false;

            // inventář: kurzor a klik mimo panely
            T.Inv();
            Ok(!ItemDropService.GameplayInputAllowed(), "s otevřeným inventářem herní Q blokováno");
            // Bod mimo panely se hledá z GEOMETRIE skutečných raycastovatelných grafik (převod rohů do
            // obrazovky), ne pevným středem – ten při 1920×1080 leží na panelu, při menším okně ne.
            Vector2 free;
            bool found = D_FindFreePoint(out free, out string why);
            Ok(found, "nalezen bod mimo všechny panely z geometrie (" + why + ")");
            if (found) Ok(D_IsOutside(free), "hit-test souhlasí s geometrií v bodě " + free + " (okno " + Screen.width + "×" + Screen.height + ")" + D_Hits(free));
            Log("střed " + (Screen.width / 2) + "," + (Screen.height / 2) + " mimo panely: " + D_IsOutside(new Vector2(Screen.width * 0.5f, Screen.height * 0.5f)) + D_Hits(new Vector2(Screen.width * 0.5f, Screen.height * 0.5f)));
            Log("roh 4,4 mimo panely: " + D_IsOutside(new Vector2(4, 4)) + " (HUD prvky v rohu se počítají jako panel)");
            var slotUi = InventorySlotRegistry.Find(SlotRef.Player(sti));
            Vector2 sp = RectTransformUtility.WorldToScreenPoint(null, slotUi.position);
            Ok(!D_IsOutside(sp), "nad slotem = uvnitř panelu");
            A.LeftClick(SlotRef.Player(sti)); A.Commit();
            Ok(A.DropCursor(false) && A.Cursor.amount == 4, "RMB mimo: jeden kus z kurzoru");
            Ok(A.DropCursor(true) && A.Cursor.IsEmpty, "LMB mimo: zbytek stacku");
            T.Inv();
            Ok(Totals() == before, "počty po kurzoru (" + Totals() + ")");
            File.WriteAllText(StateFile, Totals());
            Log("svět: " + ItemDropService.Active.Count + " předmětů");
        });

        private static bool D_IsOutside(Vector2 p) => InventoryDragDistributor.IsOutsidePanels(p);

        /// <summary>Diagnostika: collidery do 0,5 m od předmětu (jméno, trigger, vrstva, průnik bez posunu a s posunem dolů).</summary>
        private static string D_Around(DroppedItem d)
        {
            var col = d.GetComponent<Collider>();
            var sb = new StringBuilder(col.GetType().Name + " ext " + col.bounds.extents + " rot " + d.transform.eulerAngles);
            int gm = Physics.DefaultRaycastLayers & ~(1 << LayerMask.NameToLayer("Player")) & ~(1 << LayerMask.NameToLayer("Pickup"));
            if (Physics.Raycast(col.bounds.center, Vector3.down, out RaycastHit gh, 50f, gm, QueryTriggerInteraction.Ignore))
                sb.Append(" | mezera spodek→zem ").Append((gh.distance - col.bounds.extents.y).ToString("0.000")).Append(" m (").Append(gh.collider.name).Append(')');
            float age = Time.time - d.SettledAt;
            sb.Append(" | dosedl před ").Append(age.ToString("0.0")).Append(" s: ");
            foreach (var o in Physics.OverlapBox(col.bounds.center, col.bounds.extents + Vector3.one * 0.5f, Quaternion.identity, ~0, QueryTriggerInteraction.Collide))
            {
                if (o == col) continue;
                bool p0 = Physics.ComputePenetration(col, col.transform.position, col.transform.rotation, o, o.transform.position, o.transform.rotation, out Vector3 d0, out float s0);
                bool p1 = Physics.ComputePenetration(col, col.transform.position + Vector3.down * 0.06f, col.transform.rotation, o, o.transform.position, o.transform.rotation, out Vector3 d1, out float s1);
                sb.Append(o.name).Append(o.isTrigger ? "[T]" : "").Append(" L").Append(o.gameObject.layer)
                  .Append(p0 ? " pen0 " + s0.ToString("0.00") + " n" + d0.y.ToString("0.00") : "").Append(p1 ? " pen6 " + s1.ToString("0.00") + " n" + d1.y.ToString("0.00") : " -").Append("; ");
            }
            return sb.ToString();
        }

        /// <summary>0 = bez opory, 1 = leží na terénu/objektu světa, 2 = leží na jiném dropu (hromádka).</summary>
        private static int D_Support(DroppedItem d)
        {
            var col = d.GetComponent<Collider>();
            if (col == null) return 0;
            var b = col.bounds;
            int mask = Physics.DefaultRaycastLayers & ~(1 << LayerMask.NameToLayer("Player"));
            Vector3 down = Vector3.down * 0.06f;
            int res = 0;
            foreach (var o in Physics.OverlapBox(b.center + down, b.extents + Vector3.one * 0.02f, Quaternion.identity, mask, QueryTriggerInteraction.Ignore))
            {
                if (o == col) continue;
                if (!Physics.ComputePenetration(col, col.transform.position + down, col.transform.rotation, o, o.transform.position, o.transform.rotation, out Vector3 dir, out float dist)) continue;
                // Opora zespodu: vytlačení nahoru, nebo (strmý svah – nejkratší vytlačení je vodorovné) dotek už v
                // klidové poloze s normálou mířící nahoru. Boční dotek svislé stěny se nepočítá.
                bool below = dir.y > 0.05f;
                if (!below && Physics.ComputePenetration(col, col.transform.position + Vector3.down * 0.005f, col.transform.rotation, o, o.transform.position, o.transform.rotation, out Vector3 n0, out float _))
                    below = n0.y > 0.3f;
                if (!below) continue;
                if (o.GetComponent<DroppedItem>() != null) { if (res == 0) res = 2; }
                else return 1;
            }
            return res;
        }

        /// <summary>Obrazovkové obdélníky všech aktivních raycast grafik, které nepokrývají celou obrazovku.</summary>
        private static List<Rect> D_PanelRects()
        {
            var list = new List<Rect>();
            var c = new Vector3[4];
            foreach (var g in UnityEngine.Object.FindObjectsByType<UnityEngine.UI.Graphic>(FindObjectsSortMode.None))
            {
                if (!g.isActiveAndEnabled || !g.raycastTarget || g.canvas == null) continue;
                var cg = g.GetComponentInParent<CanvasGroup>();
                if (cg != null && !cg.blocksRaycasts) continue;
                Camera cam = g.canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : g.canvas.worldCamera;
                g.rectTransform.GetWorldCorners(c);
                Vector2 mn = new Vector2(float.MaxValue, float.MaxValue), mx = new Vector2(float.MinValue, float.MinValue);
                for (int i = 0; i < 4; i++) { var sp = RectTransformUtility.WorldToScreenPoint(cam, c[i]); mn = Vector2.Min(mn, sp); mx = Vector2.Max(mx, sp); }
                if (mx.x - mn.x >= Screen.width * 0.85f && mx.y - mn.y >= Screen.height * 0.85f) continue;   // stejné pravidlo jako hra
                list.Add(Rect.MinMaxRect(mn.x, mn.y, mx.x, mx.y));
            }
            return list;
        }

        /// <summary>Najde bod (mřížka 32×18, okraj 5 %) s maximální vzdáleností od všech panelů.</summary>
        private static bool D_FindFreePoint(out Vector2 best, out string why)
        {
            var rects = D_PanelRects();
            best = Vector2.zero; float bestD = -1f;
            for (int iy = 1; iy < 18; iy++)
                for (int ix = 1; ix < 32; ix++)
                {
                    var p = new Vector2(Screen.width * (0.05f + 0.9f * ix / 32f), Screen.height * (0.05f + 0.9f * iy / 18f));
                    float d = float.MaxValue;
                    foreach (var r in rects)
                    {
                        if (r.Contains(p)) { d = -1f; break; }
                        float dx = Mathf.Max(r.xMin - p.x, 0f, p.x - r.xMax), dy = Mathf.Max(r.yMin - p.y, 0f, p.y - r.yMax);
                        d = Mathf.Min(d, Mathf.Sqrt(dx * dx + dy * dy));
                    }
                    if (d > bestD) { bestD = d; best = p; }
                }
            why = rects.Count + " panelů, odstup " + bestD.ToString("0") + " px";
            return bestD >= 8f;
        }

        private static string D_Hits(Vector2 p)
        {
            var es = UnityEngine.EventSystems.EventSystem.current;
            if (es == null) return " [bez EventSystem]";
            var res = new List<UnityEngine.EventSystems.RaycastResult>();
            es.RaycastAll(new UnityEngine.EventSystems.PointerEventData(es) { position = p }, res);
            if (res.Count == 0) return "";
            var sb = new System.Text.StringBuilder(" [zásahy:");
            foreach (var r in res) sb.Append(' ').Append(r.gameObject.name);
            return sb.Append(']').ToString();
        }

        // ───────── krok 0: čekání na hratelný stav (místo pevné pauzy po načtení) ─────────
        [MenuItem("Tools/Everlost/Drop QA/0 Await ready")]
        public static void AwaitReady()
        {
            string file = Path.Combine(Dir, "drop_playqa_ready.txt");
            if (File.Exists(file)) File.Delete(file);
            double t0 = EditorApplication.timeSinceStartup;
            EditorApplication.CallbackFunction tick = null;
            tick = () =>
            {
                var t = Orivilon.World.Generation.VoxelTerrain.instance;
                bool ready = Application.isPlaying && ItemDropService.GameplayInputAllowed() && t != null && t.IsSettled && InventoryData.Instance != null;
                double el = EditorApplication.timeSinceStartup - t0;
                if (ready || !Application.isPlaying || el > 120.0)
                {
                    EditorApplication.update -= tick;
                    File.WriteAllText(file, (ready ? "OK" : "TIMEOUT") + " po " + el.ToString("0.0") + " s");
                }
            };
            EditorApplication.update += tick;
        }

        // ───────── krok 1b: čekání na skutečné ustálení (místo pevné pauzy) ─────────
        /// <summary>Kolo MP 3: krok 2 se smí spustit, až všechny dropy dosednou (nejvýš 30 s). Výsledek do
        /// drop_playqa_settle.txt – most čeká na soubor, ne na odhadnutý čas.</summary>
        [MenuItem("Tools/Everlost/Drop QA/1b Await settle")]
        public static void AwaitSettle()
        {
            string file = Path.Combine(Dir, "drop_playqa_settle.txt");
            if (File.Exists(file)) File.Delete(file);
            double t0 = EditorApplication.timeSinceStartup;
            EditorApplication.CallbackFunction tick = null;
            tick = () =>
            {
                int n = 0, s = 0;
                foreach (var d in ItemDropService.Active) { if (d == null) continue; n++; if (d.Settled) s++; }
                double el = EditorApplication.timeSinceStartup - t0;
                if (!Application.isPlaying || (n > 0 && s == n) || el > 30.0)
                {
                    EditorApplication.update -= tick;
                    File.WriteAllText(file, (s == n && n > 0 ? "OK" : "TIMEOUT") + " dosedlo " + s + "/" + n + " po " + el.ToString("0.0") + " s");
                }
            };
            EditorApplication.update += tick;
        }

        // ───────── krok 2: fyzika, viditelnost, pickup, těžba ─────────
        [MenuItem("Tools/Everlost/Drop QA/2 Settle and pickup")]
        public static void Step2() => Run("2 Settle and pickup", () =>
        {
            var inv = InventoryData.Instance;
            string before = File.ReadAllText(StateFile).Trim();
            Ok(Totals() == before, "počty po dosednutí");
            int settled = 0, grounded = 0, piled = 0, visible = 0, n = 0;
            foreach (var d in ItemDropService.Active)
            {
                if (d == null) continue; n++;
                if (d.Settled) settled++;
                // Kolo MP 3: opora podle skutečného tvaru collideru (posun o 6 cm dolů a průnik), ne paprsek ze středu –
                // poleno opřené o hranu svahu má těžiště nad prázdnem, a přesto leží.
                switch (D_Support(d))
                {
                    case 1: grounded++; break;
                    case 2: grounded++; piled++; break;
                    default: Log("bez opory: " + d.Pickup.itemData.id + " @" + d.transform.position + " dosedl " + d.SettleReason + " po " + d.SettledAfter.ToString("0.0") + " s | okolí: " + D_Around(d)); break;
                }
                foreach (var r in d.GetComponentsInChildren<Renderer>()) if (r.isVisible) { visible++; break; }
            }
            Ok(n > 0 && settled == n, "dosedlo " + settled + "/" + n + " (kinematické)");
            Ok(grounded == n, "leží na zemi " + grounded + "/" + n + " (z toho na hromádce " + piled + ")");
            Ok(visible > 0, "viditelné kamerou " + visible + "/" + n);

            var stone = Item("everlost:stone"); var log = Item("everlost:log");
            DroppedItem big = null, one = null;
            foreach (var d in ItemDropService.Active) if (d != null && d.Pickup.itemData == stone) { if (d.Pickup.itemCount > 1) big = d; else one = d; }
            int s0 = Inv(stone);
            one.Pickup.PickUp();
            Ok(Inv(stone) == s0 + 1, "sebrání po ochraně (+1 kámen)");
            // částečně plný inventář: místo jen na 3 kameny
            var slots = inv.Slots; int filled = 0, stonesIn = Inv(stone);
            for (int i = 0; i < slots.Length; i++) if (slots[i].IsEmpty) { slots[i].Set(log, 8); filled++; }
            int room = InventoryData.SpaceFor(slots, 0, slots.Length - 1, stone);
            Log("vyplněno " + filled + " slotů kládami, místo na " + room + " kamenů");
            if (room > 3)
            {
                int need = room - 3;
                for (int i = 0; i < slots.Length && need > 0; i++) if (!slots[i].IsEmpty && slots[i].item == stone) { int add = Math.Min(need, 64 - slots[i].amount); slots[i].amount += add; need -= add; }
            }
            A.Commit();
            string mid = Totals();
            int bigBefore = big.Pickup.itemCount, invBefore = Inv(stone);
            big.Pickup.PickUp();
            Ok(Inv(stone) == invBefore + 3 && big != null && big.Pickup.itemCount == bigBefore - 3, "částečný pickup: 3 do inventáře, " + (bigBefore - 3) + " zůstalo ve světě");
            big.Pickup.PickUp();
            Ok(big.Pickup.itemCount == bigBefore - 3, "plný inventář: zbytek zůstal");
            Ok(Totals() == mid, "počty po pickupu");

            // přebytek těžby s plným inventářem
            var player = GameObject.FindWithTag("Player").transform;
            HarvestableObject h = null; float best = float.MaxValue;
            foreach (var x in UnityEngine.Object.FindObjectsByType<HarvestableObject>(FindObjectsSortMode.None))
            {
                float dd = (x.transform.position - player.position).sqrMagnitude;
                if (dd < best && x.drops != null && x.drops.Length > 0) { best = dd; h = x; }
            }
            if (h == null) Log("skip těžba: žádný HarvestableObject ve scéně");
            else
            {
                int worldBefore = ItemDropService.Active.Count; string invB = InvOnly();
                typeof(HarvestableObject).GetField("currentHealth", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(h, 1);
                typeof(HarvestableObject).GetMethod("Harvest", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(h, null);
                int added = ItemDropService.Active.Count - worldBefore;
                Log("těžba " + h.name + " ve vzdálenosti " + Mathf.Sqrt(best).ToString("0") + " m → nových dropů " + added);
                Ok(InvOnly() == invB, "plný inventář se těžbou nezměnil");
                Ok(added > 0 || !HasGuaranteedDrop(h), "přebytek těžby vypadl do světa");
            }
            File.WriteAllText(StateFile, Totals());
        });

        /// <summary>Po dosednutí přebytku z těžby: stav pro streaming/save a uložení (F5 cesta).</summary>
        [MenuItem("Tools/Everlost/Drop QA/2b Save")]
        public static void Step2b() => Run("2b Save", () =>
        {
            Ok(Totals() == File.ReadAllText(StateFile).Trim(), "počty po dosednutí přebytku");
            int n = 0, settled = 0; foreach (var d in ItemDropService.Active) if (d != null) { n++; if (d.Settled) settled++; }
            Ok(settled == n, "dosedlo " + settled + "/" + n);
            File.WriteAllText(StateFile, Totals() + "\n" + WorldList());
            SaveSystemSave();
            var saved = JsonUtility.FromJson<DroppedItemsSaveData>(File.ReadAllText(Path.Combine(GameManager.selectedWorld.folderPath, "world", "dropped_items.json")));
            Ok(saved.items.Count == n, "dropped_items.json má " + saved.items.Count + " záznamů");
            Log("uloženo; svět " + n + ": " + WorldList());
        });

        private static bool HasGuaranteedDrop(HarvestableObject h)
        {
            foreach (var d in h.drops) if (d != null && d.item != null && d.chance >= 1f && d.minAmount > 0) return true;
            return false;
        }

        private static string InvOnly()
        {
            var b = new StringBuilder();
            foreach (var s in InventoryData.Instance.Slots) b.Append(s.IsEmpty ? "-" : s.item.id + s.amount).Append(',');
            return b.ToString();
        }

        private static void SaveSystemSave() => Orivilon.SaveSystem.SaveSystem.SaveEverything();

        /// <summary>Jen čte: každý předmět leží na zemi NEBO na jiném vyhozeném předmětu (hromádka).</summary>
        [MenuItem("Tools/Everlost/Drop QA/Ground check")]
        public static void GroundCheck() => Run("Ground check", () =>
        {
            int mask = Physics.DefaultRaycastLayers & ~(1 << LayerMask.NameToLayer("Player"));
            int n = 0, ok = 0;
            foreach (var d in ItemDropService.Active)
            {
                if (d == null) continue; n++;
                var col = d.GetComponent<Collider>(); var b = col.bounds;
                float best = float.MaxValue; string what = "nic";
                foreach (var h in Physics.RaycastAll(b.center, Vector3.down, b.extents.y + 1.5f, mask, QueryTriggerInteraction.Ignore))
                    if (h.collider != col && h.distance < best) { best = h.distance; what = h.collider.GetComponent<DroppedItem>() != null ? "jiný drop" : "terén/" + h.collider.name; }
                if (best < float.MaxValue) ok++;
                float deep = float.MaxValue; string deepWhat = "nic do 200 m";
                foreach (var h in Physics.RaycastAll(b.center, Vector3.down, 200f, mask, QueryTriggerInteraction.Ignore))
                    if (h.collider != col && h.distance < deep) { deep = h.distance; deepWhat = h.collider.name + "@" + h.distance.ToString("0.0"); }
                Log(d.Pickup.itemData.id + "×" + d.Pickup.itemCount + " pod sebou: " + what + (best < float.MaxValue ? " (" + best.ToString("0.00") + " m)" : "")
                    + " | dosedl " + d.SettleReason + " po " + d.SettledAfter.ToString("0.0") + " s, níž: " + deepWhat + " | výška středu nad spodkem " + b.extents.y.ToString("0.00"));
            }
            Ok(ok == n, "na zemi nebo na hromádce " + ok + "/" + n);
            // Paprsek z kamery jako PlayerInteraction (E): najde nejbližší drop v dosahu 15 m?
            var cam = Camera.main; DroppedItem near = null; float nd = float.MaxValue;
            foreach (var d in ItemDropService.Active) if (d != null) { float dd = (d.transform.position - cam.transform.position).sqrMagnitude; if (dd < nd) { nd = dd; near = d; } }
            if (near != null)
            {
                var dir = near.GetComponent<Collider>().bounds.center - cam.transform.position;
                bool hit = Physics.Raycast(cam.transform.position, dir.normalized, out var h, 15f) && h.collider.GetComponent<PickupItem>() != null;
                Ok(hit, "paprsek E z kamery zasáhne PickupItem (vzdálenost " + dir.magnitude.ToString("0.0") + " m, zásah " + (h.collider != null ? h.collider.name : "nic") + ")");
            }
        });

        // ───────── krok 3–5: streaming ─────────
        [MenuItem("Tools/Everlost/Drop QA/3 Teleport away")]
        public static void Step3() => Run("3 Teleport away", () =>
        {
            var p = GameObject.FindWithTag("Player").transform.position;
            File.WriteAllText(Path.Combine(Dir, "drop_playqa_pos.txt"), p.x.ToString(System.Globalization.CultureInfo.InvariantCulture) + " " + p.z.ToString(System.Globalization.CultureInfo.InvariantCulture));
            Orivilon.World.Generation.VoxelTerrain.instance.Teleport(new Vector3(p.x + 3000f, 0f, p.z + 3000f));
            Log("teleport z " + V(p) + " o 3 km");
        });

        [MenuItem("Tools/Everlost/Drop QA/4 Check away and return")]
        public static void Step4() => Run("4 Check away and return", () =>
        {
            var lines = File.ReadAllText(StateFile).Split('\n');
            var p = GameObject.FindWithTag("Player").transform.position;
            Log("hráč na " + V(p));
            Ok(Totals() == lines[0].Trim(), "počty daleko od dropů");
            Ok(WorldList() == lines[1].Trim(), "dropy daleko: stejné pozice a počty");
            var a = File.ReadAllText(Path.Combine(Dir, "drop_playqa_pos.txt")).Split(' ');
            float x = float.Parse(a[0], System.Globalization.CultureInfo.InvariantCulture), z = float.Parse(a[1], System.Globalization.CultureInfo.InvariantCulture);
            Orivilon.World.Generation.VoxelTerrain.instance.Teleport(new Vector3(x, 0f, z));
        });

        [MenuItem("Tools/Everlost/Drop QA/5 Verify")]
        public static void Step5() => Run("5 Verify", () =>
        {
            var lines = File.ReadAllText(StateFile).Split('\n');
            Ok(Totals() == lines[0].Trim(), "počty (" + Totals() + ")");
            Ok(WorldList() == lines[1].Trim(), "dropy: stejné pozice a počty, žádné duplikáty");
            int n = 0; foreach (var d in ItemDropService.Active) if (d != null) n++;
            int objs = UnityEngine.Object.FindObjectsByType<DroppedItem>(FindObjectsSortMode.None).Length;
            Ok(n == objs, "registr " + n + " = objektů ve scéně " + objs);
            Log("svět: " + WorldList());
        });
    }
}
