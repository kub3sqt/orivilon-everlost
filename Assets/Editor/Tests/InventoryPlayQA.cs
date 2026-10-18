using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using Orivilon.Core;
using Orivilon.Inventory.Crafting;
using Orivilon.Inventory.Inventory;
using UnityEditor;
using UnityEngine;
using UnityEngine.EventSystems;
using A = Orivilon.Inventory.Inventory.InventoryActions;
using D = Orivilon.Inventory.Inventory.InventoryDragDistributor;

namespace Orivilon.EditorTools
{
    /// <summary>
    /// Inventář 16.10.2026: Play Mode scénář nad skutečnou scénou Game (jen izolovaný QA svět – qalaunch).
    /// Události jdou přes EventSystem/ExecuteEvents na skutečné GameObjecty slotů, tedy stejnou cestou jako
    /// myš (PointerDown/Enter/Up), ale fyzické tlačítko a klávesy se nesimulují: držení tlačítka nahrazuje
    /// D.testButtonsHeld, číselné klávesy volají přímo InventoryActions.SwapWithHotbar.
    /// Výstup: _reports/remote/inventory_playqa.txt.
    /// </summary>
    internal static class InventoryPlayQA
    {
        private static readonly StringBuilder sb = new StringBuilder();
        private static int fail;
        private static string Root => Path.GetDirectoryName(Application.dataPath);
        private static string Report => Path.Combine(Root, "_reports", "remote", "inventory_playqa.txt");
        private static string Expected => Path.Combine(Root, "_reports", "remote", "inventory_playqa_expected.txt");

        private static void Log(string s) => sb.AppendLine(s);
        private static void Ok(bool c, string what) { if (!c) fail++; Log((c ? "ok   " : "FAIL ") + what); }

        private static void Flush(string title)
        {
            string text = title + ": " + (fail == 0 ? "OK" : fail + " FAIL") + "\n" + sb;
            File.WriteAllText(Report, text);
            File.AppendAllText(Path.Combine(Root, "_reports", "remote", "inventory_playqa_history.txt"), DateTime.Now.ToString("HH:mm:ss ") + text + "\n");
            if (fail == 0) Debug.Log("[InventoryPlayQA] " + title + " OK"); else Debug.LogError("[InventoryPlayQA] " + text);
        }

        private static void Toggle()
        {
            typeof(GameManager).GetMethod("ToggleInventory", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(GameManager.instance, null);
        }

        private static readonly Dictionary<SlotRef, GameObject> ui = new Dictionary<SlotRef, GameObject>();

        private static void CollectUi()
        {
            ui.Clear();
            foreach (var s in UnityEngine.Object.FindObjectsByType<InventorySlot>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
            {
                var r = SlotRef.Player(s.slotIndex);
                // Hotbar má v HUDu i v inventáři stejné indexy – bereme ten, který přijímá raycast.
                if (!ui.ContainsKey(r) || Raycastable(s.gameObject)) ui[r] = s.gameObject;
            }
            foreach (var s in UnityEngine.Object.FindObjectsByType<CraftingSlot>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
                ui[SlotRef.Grid(s.index)] = s.gameObject;
            var o = UnityEngine.Object.FindFirstObjectByType<CraftingOutputSlot>(FindObjectsInactive.Exclude);
            if (o != null) ui[SlotRef.Output] = o.gameObject;
        }

        private static bool Raycastable(GameObject go)
        {
            foreach (var g in go.GetComponentsInParent<CanvasGroup>())
            {
                if (!g.blocksRaycasts) return false;
                if (g.ignoreParentGroups) break;
            }
            return true;
        }

        private static PointerEventData Ev(int button)
        {
            return new PointerEventData(EventSystem.current)
            {
                button = button == 0 ? PointerEventData.InputButton.Left : PointerEventData.InputButton.Right
            };
        }

        private static GameObject Go(SlotRef r)
        {
            if (!ui.TryGetValue(r, out var g) || g == null) throw new Exception("UI slot " + r + " není aktivní");
            return g;
        }

        private static void Down(SlotRef r, int b, bool shift = false) { D.testShift = shift; ExecuteEvents.Execute(Go(r), Ev(b), ExecuteEvents.pointerEnterHandler); ExecuteEvents.Execute(Go(r), Ev(b), ExecuteEvents.pointerDownHandler); }
        private static void Enter(SlotRef r, bool shift = false) { D.testShift = shift; ExecuteEvents.Execute(Go(r), Ev(0), ExecuteEvents.pointerEnterHandler); }
        private static void Exit(SlotRef r) { ExecuteEvents.Execute(Go(r), Ev(0), ExecuteEvents.pointerExitHandler); }
        private static void Up(SlotRef pressed, int b) { ExecuteEvents.Execute(Go(pressed), Ev(b), ExecuteEvents.pointerUpHandler); D.testShift = false; }
        private static float t = 5000f;
        private static void Tick(float dt = 1f) { t += dt; D.testTime = t; }
        private static void Click(SlotRef r, int b, bool shift = false) { Down(r, b, shift); Up(r, b); Exit(r); Tick(); }

        private static InventoryItemData ItemById(string id)
        {
            foreach (var it in Resources.LoadAll<InventoryItemData>("")) if (it.id == id) return it;
            return null;
        }

        private static string Totals()
        {
            var d = new SortedDictionary<string, int>();
            void Add(InventorySlotData s) { if (s != null && !s.IsEmpty) { d.TryGetValue(s.item.id, out int n); d[s.item.id] = n + s.amount; } }
            foreach (var s in InventoryData.Instance.Slots) Add(s);
            if (CraftingData.Instance != null) foreach (var s in CraftingData.Instance.gridSlots) Add(s);
            Add(A.Cursor);
            var b = new StringBuilder();
            foreach (var kv in d) b.Append(kv.Key).Append('=').Append(kv.Value).Append(' ');
            return b.ToString().Trim();
        }

        /// <summary>Žádné ghost ikony: každá aktivní ikona odpovídá datům a kurzor ukazuje jen držený stack.</summary>
        private static void GhostCheck(string when)
        {
            int bad = 0; string first = "";
            foreach (var s in UnityEngine.Object.FindObjectsByType<InventorySlot>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
            {
                var d = InventoryData.Instance.Slots[s.slotIndex];
                bool ok = s.iconImage.enabled == !d.IsEmpty && (d.IsEmpty || s.iconImage.sprite == d.item.icon)
                          && s.stackAmountText.text == (!d.IsEmpty && d.amount > 1 ? d.amount.ToString() : "");
                if (!ok) { bad++; if (first == "") first = s.name + "[" + s.slotIndex + "]"; }
            }
            foreach (var s in UnityEngine.Object.FindObjectsByType<CraftingSlot>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
            {
                var d = CraftingData.Instance.gridSlots[s.index];
                if (s.iconImage.enabled != !d.IsEmpty) { bad++; if (first == "") first = "grid[" + s.index + "]"; }
            }
            var c = InventoryCursor.Instance;
            if (c != null && c.cursorImage.enabled != !A.Cursor.IsEmpty) { bad++; first += " cursor"; }
            Ok(bad == 0, "bez ghost ikon (" + when + ")" + (bad > 0 ? ": " + bad + " nesedí, první " + first : ""));
        }

        [MenuItem("Tools/Everlost/Inventory QA/Play scenario")]
        public static void Run()
        {
            sb.Length = 0; fail = 0;
            try { Scenario(); }
            catch (Exception e) { fail++; Log("FAIL výjimka: " + e); }
            finally
            {
                D.testButtonsHeld = false; D.testShift = null; D.testTime = null;
                try { Flush("Play scenario"); } catch (Exception) { }
            }
        }

        private static void Scenario()
        {
            if (!Application.isPlaying) throw new Exception("není Play Mode");
            var gm = GameManager.instance;
            var inv = InventoryData.Instance;
            if (gm == null || inv == null) throw new Exception("Game scéna není načtená");
            string folder = GameManager.selectedWorld != null ? GameManager.selectedWorld.folderPath ?? "" : "";
            if (!folder.Replace('\\', '/').Contains("/_reports/ui-refresh/qa-saves/")) throw new Exception("odmítnuto: běží svět mimo izolované QA saves (" + folder + ")");
            Log("svět " + folder);

            if (gm.IsInventoryOpen) Toggle();
            int closed0 = inv.ListenerCount;
            Toggle();
            int open0 = inv.ListenerCount;
            var uic = InventoryUIController.Instance;
            Log("InventoryUIController " + (uic != null ? "aktivní" : "chybí (refresh HUD: grid i inventář jsou vidět zároveň)") + ", GridVisible " + A.GridVisible + ", MainVisible " + A.MainVisible);
            CollectUi();
            var sbL = new StringBuilder();
            foreach (var kv in ui) if (kv.Key.IsPlayer && (kv.Key.index < 10 || kv.Key.index > 60)) sbL.Append(kv.Key.index).Append('→').Append(kv.Value.transform.parent.name).Append(' ');
            Log("rozložení: " + sbL + "| grid " + CountArea(SlotArea.Grid) + ", výstup " + ui.ContainsKey(SlotRef.Output) + ", main " + CountArea(SlotArea.Main) + ", hotbar " + CountArea(SlotArea.Hotbar));
            for (int i = 0; i < 4; i++)
            {
                uic?.OpenCrafting(); uic?.OpenInventory();
                Toggle(); Ok(inv.ListenerCount == closed0, "posluchači po zavření #" + i + " = " + inv.ListenerCount + " (výchozí " + closed0 + ")");
                Toggle(); Ok(inv.ListenerCount == open0, "posluchači po otevření #" + i + " = " + inv.ListenerCount + " (výchozí " + open0 + ")");
            }
            CollectUi();
            D.testButtonsHeld = true;

            var stone = ItemById("everlost:stone"); var log = ItemById("everlost:log"); var stick = ItemById("everlost:stick");
            if (stone == null || log == null || stick == null) throw new Exception("chybí itemy v Resources");
            inv.AddItem(stone, 40); inv.AddItem(log, 6); inv.AddItem(stick, 10);
            string before = Totals();
            Log("počty " + before);
            GhostCheck("po přidání");

            int sStone = Find(stone), e1 = Empty(), e2, e3;
            Click(SlotRef.Player(sStone), 0); Ok(A.Cursor.item == stone, "LMB vzal stack");
            Click(SlotRef.Player(e1), 0); Ok(A.Cursor.IsEmpty && inv.Slots[e1].item == stone, "LMB položil do prázdného");
            Click(SlotRef.Player(e1), 1); Ok(A.Cursor.amount == (inv.Slots[e1].amount + A.Cursor.amount + 1) / 2, "RMB vzal polovinu nahoru (" + A.Cursor.amount + ")");
            e2 = Empty(); e3 = Empty(e2);
            int held = A.Cursor.amount;
            Down(SlotRef.Player(e2), 1); Enter(SlotRef.Player(e3)); Enter(SlotRef.Player(e2)); Up(SlotRef.Player(e2), 1); Tick();
            Ok(inv.Slots[e2].amount == 2 && inv.Slots[e3].amount == 1 && A.Cursor.amount == held - 3, "RMB tweak: re-entry přidal kus");
            GhostCheck("po RMB");
            int e4 = Empty(), e5 = Empty(e4);
            held = A.Cursor.amount;
            Down(SlotRef.Player(e4), 0); Enter(SlotRef.Player(e5)); Enter(SlotRef.Player(e4)); Up(SlotRef.Player(e4), 0); Tick();
            Ok(inv.Slots[e4].amount == held / 2 && inv.Slots[e5].amount == held / 2, "LMB split na 2 sloty (" + held + ")");
            Ok(Totals() == before, "počty po splitu");
            Click(SlotRef.Player(e4), 0); Tick(-0.95f); Down(SlotRef.Player(e4), 0); Up(SlotRef.Player(e4), 0); Exit(SlotRef.Player(e4)); Tick();
            Ok(A.Cursor.item == stone && A.Cursor.amount == Math.Min(64, 40), "dvojklik sesbíral kameny (" + A.Cursor.amount + ")");
            Click(SlotRef.Player(e4), 0);
            int hot = EmptyHotbar();
            Ok(A.SwapWithHotbar(SlotRef.Player(e4), hot), "číselná klávesa (přímé volání)"); A.Commit();
            Click(SlotRef.Player(hot), 0, true);
            Ok(inv.Slots[hot].IsEmpty, "Shift+klik hotbar → inventář");
            int sLog = Find(log);
            Enter(SlotRef.Player(sLog)); D.Wheel(SlotRef.Player(sLog), -1f); Exit(SlotRef.Player(sLog));
            Ok(Totals() == before, "kolečko dolů zachovalo počty (" + Totals() + ")");
            GhostCheck("po kolečku");

            // crafting s receptem ze scény
            uic?.OpenCrafting(); CollectUi();
            // Kolečko výše mohlo (podle MouseTweaks správně) poslat kládu do gridu – recept chce čistý grid.
            CraftingData.Instance.ReturnAllToInventory(); A.Commit();
            var ctrl = CraftingController.Instance;
            ShapedRecipe rec = null;
            foreach (var r in ctrl.shapedRecipes) if (r != null && r.result != null && r.pattern != null) { rec = r; break; }
            if (rec == null) Log("skip crafting: ve scéně není tvarovaný recept");
            else
            {
                foreach (var p in rec.pattern) if (p != null) inv.AddItem(p, 2);
                CollectUi();
                Log("recept " + rec.name + " " + rec.width + "x" + rec.height + " → " + rec.result.id + " ×" + rec.resultAmount + "; počty " + Totals());
                for (int y = 0; y < rec.height; y++)
                    for (int x = 0; x < rec.width; x++)
                    {
                        var need = rec.pattern[y * rec.width + x];
                        if (need == null) continue;
                        int src = Find(need);
                        Click(SlotRef.Player(src), 1);          // vezmi polovinu
                        Down(SlotRef.Grid(y * 3 + x), 1); Up(SlotRef.Grid(y * 3 + x), 1); Tick();   // polož 1 do gridu
                        A.ReturnCursorToInventory(); A.Commit();
                    }
                Ok(CraftingData.Instance.resultItem == rec.result, "recept " + rec.name + " rozpoznán");
                string beforeCraft = Totals();
                Click(SlotRef.Output, 0);
                Ok(A.Cursor.item == rec.result && A.Cursor.amount == rec.resultAmount, "výstup na kurzor");
                int gridLeft = 0; foreach (var g in CraftingData.Instance.gridSlots) if (!g.IsEmpty) gridLeft += g.amount;
                var gsb = new StringBuilder(); foreach (var g in CraftingData.Instance.gridSlots) gsb.Append(g.IsEmpty ? "-" : g.item.id + "×" + g.amount).Append(' ');
                Ok(gridLeft == 0, "suroviny spotřebovány (grid: " + gsb + ")");
                A.ReturnCursorToInventory(); A.Commit();
                Log("crafting " + beforeCraft + " → " + Totals());
                GhostCheck("po craftu");
                before = Totals();
            }

            // zavření během držení a splitu, se surovinou v gridu
            int sSt = Find(stone);
            G0(stick);
            Click(SlotRef.Player(sSt), 0);
            int hotE = EmptyHotbar(), hotE2 = EmptyHotbar(hotE);
            Down(SlotRef.Player(hotE), 0); Enter(SlotRef.Player(hotE2));
            Toggle();   // zavřít uprostřed gesta (stejná cesta jako Tab/ESC)
            Ok(!gm.IsInventoryOpen && !gm.isMenuOpen, "inventář zavřen, menu flag shozen");
            Ok(A.Cursor.IsEmpty, "kurzor vrácen do inventáře");
            bool gridEmpty = true; foreach (var g in CraftingData.Instance.gridSlots) if (!g.IsEmpty) gridEmpty = false;
            Ok(gridEmpty, "grid vrácen do inventáře");
            Ok(Totals() == before, "počty po zavření (" + Totals() + ")");
            var pc = typeof(GameManager).GetField("playerController", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(gm) as Behaviour;
            Ok(pc == null || pc.enabled, "ovládání hráče vráceno");
            Up(SlotRef.Player(hotE), 0);   // pozdní PointerUp po zavření
            Ok(Totals() == before, "pozdní PointerUp nic nezměnil");
            GhostCheck("po zavření");

            // vnější změna dat (stavění/Arcanum/jídlo volají jen NotifyInventoryChanged)
            int sStick = Find(stick);
            inv.Slots[sStick].amount -= 1; if (inv.Slots[sStick].amount <= 0) inv.Slots[sStick].Clear();
            inv.NotifyInventoryChanged();
            GhostCheck("po vnější spotřebě");
            inv.AddItem(stick, 1);

            // F5 s otevřeným inventářem: kurzor a grid musí být v savu
            Toggle(); uic?.OpenCrafting(); CollectUi();
            G0(stick);
            Click(SlotRef.Player(Find(stone)), 0);
            string expect = Totals();
            Orivilon.SaveSystem.SaveSystem.SaveEverything();
            File.WriteAllText(Expected, expect);
            Log("uloženo F5 s kurzorem " + A.Cursor.amount + "× " + A.Cursor.item.id + ": " + expect);
            Toggle();
            Ok(Totals() == expect, "zavření po F5 zachovalo počty");
            Ok(!gm.IsInventoryOpen, "zpět ve hře");
        }

        private static void G0(InventoryItemData it)
        {
            int s = Find(it);
            Click(SlotRef.Player(s), 1);
            Down(SlotRef.Grid(4), 1); Up(SlotRef.Grid(4), 1); Tick();
            A.ReturnCursorToInventory(); A.Commit();
        }

        private static int CountArea(SlotArea a) { int n = 0; foreach (var k in ui.Keys) if (k.area == a) n++; return n; }

        private static int Find(InventoryItemData it)
        {
            var s = InventoryData.Instance.Slots;
            for (int i = s.Length - 1; i >= 0; i--) if (!s[i].IsEmpty && s[i].item == it && ui.ContainsKey(SlotRef.Player(i))) return i;
            return -1;
        }

        private static int Empty(int except = -1)
        {
            var s = InventoryData.Instance.Slots;
            for (int i = InventoryData.HotbarSize; i < s.Length; i++) if (i != except && s[i].IsEmpty && ui.ContainsKey(SlotRef.Player(i))) return i;
            throw new Exception("žádný prázdný slot");
        }

        private static int EmptyHotbar(int except = -1)
        {
            var s = InventoryData.Instance.Slots;
            for (int i = 0; i < InventoryData.HotbarSize; i++) if (i != except && s[i].IsEmpty) return i;
            throw new Exception("žádný prázdný hotbar slot");
        }

        /// <summary>Stav hry pár snímků po zavření (GameManager zamyká kurzor se zpožděním dvou snímků).</summary>
        [MenuItem("Tools/Everlost/Inventory QA/Game state")]
        public static void GameState()
        {
            sb.Length = 0; fail = 0;
            var gm = GameManager.instance;
            bool open = gm != null && gm.IsInventoryOpen;
            Log("inventář " + (open ? "otevřen" : "zavřen"));
            if (!open)
            {
                Ok(gm != null && !gm.isMenuOpen && !gm.IsPaused, "hra běží");
                Ok(Cursor.lockState == CursorLockMode.Locked, "kurzor zamčen (" + Cursor.lockState + ", focus " + Application.isFocused + ")");
                Ok(A.Cursor.IsEmpty && (InventoryCursor.Instance == null || !InventoryCursor.Instance.cursorImage.enabled), "na kurzoru nic, ikona skrytá");
            }
            Ok(Mathf.Approximately(Time.timeScale, 1f), "timeScale " + Time.timeScale);
            Log("odstup posledních dvou stisků " + D.LastPressGap.ToString("0.000") + " s, kurzor " + (A.Cursor.IsEmpty ? "prázdný" : A.Cursor.amount + "× " + A.Cursor.item.id));
            Flush("Game state");
        }

        [MenuItem("Tools/Everlost/Inventory QA/Verify loaded")]
        public static void Verify()
        {
            sb.Length = 0; fail = 0;
            try
            {
                if (!Application.isPlaying || InventoryData.Instance == null) throw new Exception("Game scéna není načtená");
                string exp = File.Exists(Expected) ? File.ReadAllText(Expected).Trim() : "";
                string got = Totals();
                Ok(exp == got, "načtený inventář = uložený (" + got + ")" + (exp == got ? "" : " čekáno " + exp));
                GhostCheck("po načtení");
                Ok(A.Cursor.IsEmpty, "kurzor po načtení prázdný");
            }
            catch (Exception e) { fail++; Log("FAIL " + e.Message); }
            Flush("Verify loaded");
        }

        /// <summary>
        /// Starší save: kopie dřívějšího QA světa (napsaného původním kódem) s inventářem doplněným ve
        /// stávajícím formátu JSON. Nastaví ho jako poslední QA svět pro „qareopen“. Originál se nemění.
        /// </summary>
        [MenuItem("Tools/Everlost/Inventory QA/Prepare old save copy")]
        public static void PrepareOld()
        {
            sb.Length = 0; fail = 0;
            try
            {
                string qa = Path.Combine(Root, "_reports", "ui-refresh", "qa-saves", "worlds");
                string src = null; DateTime best = DateTime.MinValue;
                foreach (var d in Directory.GetDirectories(qa))
                {
                    var pj = Path.Combine(d, "player", "player.json");
                    var tm = File.Exists(pj) ? File.GetLastWriteTime(pj) : DateTime.MinValue;
                    // jen světy uložené PŘED úpravou inventáře (16.10.2026 22:00) – zapsal je původní kód
                    if (tm < new DateTime(2026, 10, 16, 22, 0, 0) && tm > best && !Path.GetFileName(d).StartsWith("InvOld")) { best = tm; src = d; }
                }
                if (src == null) throw new Exception("žádný starší QA svět");
                string dst = Path.Combine(qa, "InvOld-" + DateTime.Now.ToString("yyyyMMdd-HHmmss"));
                Copy(src, dst);
                string pjDst = Path.Combine(dst, "player", "player.json");
                string json = File.ReadAllText(pjDst);
                var pd = JsonUtility.FromJson<Orivilon.SaveSystem.PlayerData>(json);
                pd.inventory.slots[0] = new Orivilon.SaveSystem.InventorySlotSaveData { itemID = "everlost:stone", amount = 33 };
                pd.inventory.slots[3] = new Orivilon.SaveSystem.InventorySlotSaveData { itemID = "everlost:log", amount = 8 };
                pd.inventory.slots[20] = new Orivilon.SaveSystem.InventorySlotSaveData { itemID = "everlost:stone_axe", amount = 1 };
                pd.inventory.slots[40] = new Orivilon.SaveSystem.InventorySlotSaveData { itemID = "everlost:stick", amount = 12 };
                File.WriteAllText(pjDst, JsonUtility.ToJson(pd, true));
                var cfg = File.ReadAllText(Path.Combine(dst, "world_data.json"));
                var m = System.Text.RegularExpressions.Regex.Match(cfg, "\"seedText\"\\s*:\\s*\"([^\"]*)\"");
                if (!m.Success) throw new Exception("world_data.json bez seedText");
                EditorPrefs.SetString("Everlost.UiQa.LastQaWorld", "InvOld|" + m.Groups[1].Value + "|" + dst);
                File.WriteAllText(Expected, "everlost:log=8 everlost:stick=12 everlost:stone=33 everlost:stone_axe=1");
                Log("kopie " + Path.GetFileName(src) + " (" + best + ") → " + dst);
            }
            catch (Exception e) { fail++; Log("FAIL " + e); }
            Flush("Prepare old save copy");
        }

        private static void Copy(string src, string dst)
        {
            Directory.CreateDirectory(dst);
            foreach (var f in Directory.GetFiles(src, "*", SearchOption.AllDirectories))
            {
                string rel = f.Substring(src.Length + 1);
                string to = Path.Combine(dst, rel);
                Directory.CreateDirectory(Path.GetDirectoryName(to));
                File.Copy(f, to);
            }
        }
    }
}
