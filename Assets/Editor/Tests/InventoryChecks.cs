using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using Orivilon.Inventory.Crafting;
using Orivilon.Inventory.Inventory;
using UnityEditor;
using UnityEngine;
using D = Orivilon.Inventory.Inventory.InventoryDragDistributor;
using A = Orivilon.Inventory.Inventory.InventoryActions;

namespace Orivilon.EditorTools
{
    /// <summary>
    /// Inventář 16.10.2026: regresní kontroly bez scény (Tools/Everlost/Inventory checks).
    /// Staví dočasné InventoryData + CraftingData + CraftingController se syntetickými itemy a
    /// prověřuje, že žádná operace ani gesto nezmění celkový počet itemů (mimo crafting se správnou
    /// spotřebou) a že se nevytvoří alias objektu kurzoru se slotem. První kontrola je reprodukce
    /// duplikace Shift+klikem z 16.10. (původní kód: 118 → 128 kamenů).
    /// Výsledek: _reports/remote/inventory_checks.txt (a Console).
    /// </summary>
    internal static class InventoryChecks
    {
        private static InventoryData inv;
        private static CraftingData craft;
        private static CraftingController ctrl;
        private static InventoryItemData stone, log, stick, plank, tool, small;
        private static readonly List<UnityEngine.Object> temp = new List<UnityEngine.Object>();
        private static readonly StringBuilder sb = new StringBuilder();
        private static int pass, fail;
        private static float t = 1000f;

        [MenuItem("Tools/Everlost/Inventory checks")]
        public static void Run()
        {
            sb.Length = 0; pass = fail = 0;
            var prevInv = InventoryData.Instance; var prevCraft = CraftingData.Instance; var prevCtrl = CraftingController.Instance;
            try
            {
                Setup();
                Check("Shift+klik při částečném místě nepřidá kusy (regrese duplikace)", ShiftClickPartial);
                Check("AddItem/AddItemRange/AddItemSmart jsou všechno-nebo-nic", AtomicAdds);
                Check("LMB vzít/položit/sloučit/prohodit + limit stacku", LeftClicks);
                Check("RMB polovina nahoru, jeden kus, plný a nekompatibilní slot", RightClicks);
                Check("Split tažením: prázdné+částečné, nekompatibilní přeskočen, limit, opakovaný vstup", SplitDrag);
                Check("Split: zavření/ESC během tažení vrátí náhled", SplitCancel);
                Check("Split přes jediný slot = obyčejný klik; uvolnění mimo UI", SplitSingle);
                Check("RMB tweak: opětovný vstup přidá kus, stání nic", RmbTweak);
                Check("LMB tweak s itemem: sbírá do limitu, Shift přesouvá", LmbTweakWithItem);
                Check("Shift+LMB tažení bez itemu rychle přesouvá", LmbTweakWithoutItem);
                Check("Dvojklik sesbírá (neplné dřív), nespustí split ani druhý klik", DoubleClick);
                Check("Shift+dvojklik přesune všechny stejné", ShiftDoubleClick);
                Check("Číselné klávesy prohodí s hotbarem", NumberKeys);
                Check("Kolečko: push/pull, kapacita, pořadí hledání, směr", Wheel);
                Check("Crafting: shapeless spotřeba (regrese), výstup není sklad, Craft All do kapacity", Crafting);
                Check("Shift+klik na výstup s itemem na kurzoru: Craft All, kurzor beze změny, kapacita, bez surovin", CraftShiftWithCursor);
                Check("Vrácení gridu/kurzoru do plného inventáře nic neztratí", ReturnFull);
                Check("Snímek savu obsahuje kurzor+grid, živá data beze změny", SaveSnapshot);
                Check("Fuzz 4000 náhodných gest zachová počty a limity", Fuzz);
                Check("Drop: Q kus / Ctrl+Q stack, prázdný zdroj, maxStack 1, selhání spawnu, rychlé opakování", DropSlot);
                Check("Drop kurzoru LMB/RMB, výstup craftingu, konec tažení venku nic nevyhodí", DropCursor);
                Check("Pickup vyhozeného: ochrana, plný a částečně plný inventář", DropPickup);
            }
            catch (Exception e) { fail++; sb.AppendLine("FAIL setup: " + e); }
            finally
            {
                Teardown();
                InventoryData.Instance = prevInv; CraftingData.Instance = prevCraft; CraftingController.Instance = prevCtrl;
            }
            string head = (fail == 0 ? "OK " : "FAIL ") + pass + " prošlo, " + fail + " selhalo";
            string text = head + "\n" + sb;
            try { File.WriteAllText(Path.Combine(Path.GetDirectoryName(Application.dataPath), "_reports", "remote", "inventory_checks.txt"), text); } catch (Exception) { }
            if (fail == 0) Debug.Log("[InventoryChecks] " + head); else Debug.LogError("[InventoryChecks] " + text);
        }

        // ───────────── prostředí ─────────────

        private static InventoryItemData Item(string id, int max, bool stackable = true)
        {
            var it = ScriptableObject.CreateInstance<InventoryItemData>();
            it.id = id; it.itemName = id; it.maxStack = max; it.stackable = stackable;
            temp.Add(it); return it;
        }

        private static void Setup()
        {
            stone = Item("qa_stone", 64); log = Item("qa_log", 64); stick = Item("qa_stick", 64);
            plank = Item("qa_plank", 64); tool = Item("qa_tool", 1, false); small = Item("qa_small", 16);

            var go = new GameObject("InvCheck") { hideFlags = HideFlags.HideAndDontSave };
            temp.Add(go);
            inv = go.AddComponent<InventoryData>();
            var slots = new InventorySlotData[63];
            for (int i = 0; i < slots.Length; i++) slots[i] = new InventorySlotData();
            typeof(InventoryData).GetField("slots", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(inv, slots);
            InventoryData.Instance = inv;

            craft = go.AddComponent<CraftingData>();
            for (int i = 0; i < craft.gridSlots.Length; i++) craft.gridSlots[i] = new InventorySlotData();
            CraftingData.Instance = craft;

            ctrl = go.AddComponent<CraftingController>();
            ctrl.shapedRecipes = new List<ShapedRecipe>();
            ctrl.shapelessRecipes = new List<ShapelessRecipe>();
            CraftingController.Instance = ctrl;
        }

        private static void Teardown()
        {
            A.Cursor.Clear(); A.ForceMainVisible = A.ForceGridVisible = null;
            Orivilon.World.Objects.ItemDropService.SpawnOverride = null;
            D.CancelGesture(); D.testButtonsHeld = false; D.testShift = null; D.testTime = null;
            foreach (var o in temp) if (o != null) UnityEngine.Object.DestroyImmediate(o);
            temp.Clear();
        }

        private static void Reset(bool grid = false)
        {
            foreach (var s in inv.Slots) s.Clear();
            foreach (var s in craft.gridSlots) s.Clear();
            A.Cursor.Clear();
            D.CancelGesture();
            ctrl.shapedRecipes.Clear(); ctrl.shapelessRecipes.Clear();
            A.ForceMainVisible = true; A.ForceGridVisible = grid;
            D.testButtonsHeld = true; D.testShift = false;
            t += 10f; D.testTime = t;
            ctrl.Recalculate();
        }

        private static void Tick(float dt = 1f) { t += dt; D.testTime = t; }

        private static InventorySlotData P(int i) => inv.Slots[i];
        private static InventorySlotData G(int i) => craft.gridSlots[i];
        private static SlotRef RP(int i) => SlotRef.Player(i);
        private static SlotRef RG(int i) => SlotRef.Grid(i);

        private static void Down(SlotRef r, int b, bool shift = false) { D.testShift = shift; D.PointerDown(r, b, shift); }
        private static void Enter(SlotRef r, bool shift = false) { D.testShift = shift; D.PointerEnter(r); }
        private static void Up(int b) { D.PointerUp(b); D.testShift = false; }
        private static void Click(SlotRef r, int b, bool shift = false) { Down(r, b, shift); Up(b); Tick(); }

        private static int Total(InventoryItemData it) => A.CountEverywhere(it);

        private static void Check(string name, Action test)
        {
            try { test(); pass++; sb.AppendLine("ok   " + name); }
            catch (Exception e) { fail++; sb.AppendLine("FAIL " + name + ": " + e.Message); }
        }

        private static void Eq(int expected, int actual, string what)
        { if (expected != actual) throw new Exception(what + ": čekáno " + expected + ", je " + actual); }

        private static void True(bool c, string what) { if (!c) throw new Exception(what); }

        private static void Invariants()
        {
            foreach (var arr in new[] { inv.Slots, craft.gridSlots })
                foreach (var s in arr)
                {
                    True(!ReferenceEquals(s, A.Cursor), "kurzor sdílí objekt se slotem");
                    if (s.IsEmpty) { True(s.amount >= 0, "záporný počet"); continue; }
                    True(s.amount <= InventorySlotData.StackLimit(s.item), "překročen limit stacku " + s.item.id + " " + s.amount);
                }
            if (!A.Cursor.IsEmpty) True(A.Cursor.amount <= InventorySlotData.StackLimit(A.Cursor.item), "kurzor nad limit");
        }

        // ───────────── testy ─────────────

        private static void ShiftClickPartial()
        {
            Reset();
            for (int i = 8; i < 63; i++) P(i).Set(log, 64);
            P(20).Set(stone, 54);
            P(0).Set(stone, 64);
            int before = Total(stone);
            Click(RP(0), 0, true);
            Eq(before, Total(stone), "kameny po Shift+kliku");
            Eq(54, P(0).amount, "zbytek ve zdroji");
            Eq(64, P(20).amount, "doplněný stack");
            Invariants();
        }

        private static void AtomicAdds()
        {
            Reset();
            for (int i = 0; i < 63; i++) P(i).Set(log, 64);
            P(5).Set(stone, 60);
            True(!inv.AddItem(stone, 10), "AddItem měl selhat");
            Eq(60, P(5).amount, "AddItem po neúspěchu");
            True(!inv.AddItemSmart(stone, 5), "AddItemSmart měl selhat");
            True(!inv.AddItemRange(0, 7, stone, 5), "AddItemRange měl selhat");
            Eq(60, Total(stone), "počet po neúspěších");
            True(inv.AddItem(stone, 4), "AddItem 4 se vejde");
            Eq(64, Total(stone), "po úspěchu");
            Eq(0, inv.AddItemUpTo(stone, 3), "AddItemUpTo do plného");
        }

        private static void LeftClicks()
        {
            Reset();
            P(8).Set(small, 10); P(9).Set(small, 12); P(10).Set(stone, 3);
            Click(RP(8), 0);                         // vzít 10
            Eq(10, A.Cursor.amount, "vzato"); True(P(8).IsEmpty, "zdroj prázdný");
            Click(RP(9), 0);                         // sloučit do limitu 16: 12+4
            Eq(16, P(9).amount, "sloučeno"); Eq(6, A.Cursor.amount, "zbytek na kurzoru");
            Click(RP(10), 0);                        // prohodit s kamenem
            True(A.Cursor.item == stone && A.Cursor.amount == 3 && P(10).item == small && P(10).amount == 6, "prohození");
            Click(RP(11), 0);                        // položit do prázdného
            True(A.Cursor.IsEmpty && P(11).amount == 3, "položení");
            A.Cursor.Set(small, 16); P(12).Set(small, 16);
            Click(RP(12), 0);                        // plný kompatibilní: nic
            Eq(16, A.Cursor.amount, "plný slot nemění kurzor");
            Eq(16 + 6 + 16 + 16, Total(small), "small celkem");
            Invariants();
        }

        private static void RightClicks()
        {
            Reset();
            P(8).Set(stone, 5); P(9).Set(log, 2); P(10).Set(tool, 1);
            Click(RP(8), 1);
            Eq(3, A.Cursor.amount, "polovina nahoru"); Eq(2, P(8).amount, "zbylo");
            Click(RP(11), 1); Eq(1, P(11).amount, "jeden kus do prázdného"); Eq(2, A.Cursor.amount, "kurzor");
            Click(RP(8), 1); Eq(3, P(8).amount, "jeden kus do kompatibilního");
            Click(RP(9), 1);                         // nekompatibilní → prohození
            True(A.Cursor.item == log && A.Cursor.amount == 2 && P(9).item == stone && P(9).amount == 1, "RMB prohození");
            A.Cursor.Clear(); P(12).Set(stone, 1);
            Click(RP(12), 1); Eq(1, A.Cursor.amount, "polovina z 1 = 1");
            A.Cursor.Clear();
            Click(RP(10), 1); True(A.Cursor.item == tool && P(10).IsEmpty, "nestackovatelný vezme celý");
            Invariants();
        }

        private static void SplitDrag()
        {
            Reset();
            A.Cursor.Set(stone, 10);
            P(9).Set(stone, 60); P(10).Set(log, 5);
            Down(RP(8), 0); Enter(RP(10)); Enter(RP(9));   // 8 prázdný, 10 nekompatibilní, 9 částečný
            Eq(5, P(8).amount, "prázdný slot dostal polovinu");
            Eq(64, P(9).amount, "částečný jen do limitu");
            Eq(1, A.Cursor.amount, "zbytek na kurzoru");
            Enter(RP(8)); Enter(RP(9));                   // opakovaný vstup nic nemění
            Eq(5, P(8).amount, "opakovaný vstup"); Eq(70, Total(stone), "kameny během dragu");
            Up(0); Tick();
            Eq(70, Total(stone), "kameny po dragu"); Eq(5, P(10).amount, "nekompatibilní netknutý");
            Invariants();
        }

        private static void SplitCancel()
        {
            Reset();
            A.Cursor.Set(stone, 9); P(9).Set(stone, 2);
            Down(RP(8), 0); Enter(RP(9)); Enter(RP(11));
            D.CancelGesture();
            Eq(9, A.Cursor.amount, "kurzor po zrušení"); Eq(2, P(9).amount, "slot po zrušení"); True(P(8).IsEmpty && P(11).IsEmpty, "náhled vrácen");
            Up(0);   // pozdní PointerUp po zrušení nesmí nic udělat
            Eq(9, A.Cursor.amount, "pozdní Up"); Eq(11, Total(stone), "kameny");
        }

        private static void SplitSingle()
        {
            Reset();
            A.Cursor.Set(stone, 7);
            Down(RP(8), 0); Up(0); Tick();
            True(A.Cursor.IsEmpty && P(8).amount == 7, "jediný slot = položení");
            Click(RP(8), 0);   // vzít zpět
            Down(RP(9), 0); Enter(RP(10));
            D.testButtonsHeld = false;   // uvolnění mimo UI: OnPointerUp nepřišel, pojistka v Tick
            D.Tick(); D.testButtonsHeld = true;
            Eq(7, Total(stone), "po uvolnění mimo UI"); True(A.Cursor.amount == 1 && P(9).amount == 3 && P(10).amount == 3, "split potvrzen");
        }

        private static void RmbTweak()
        {
            Reset();
            A.Cursor.Set(stone, 5);
            Down(RP(8), 1);
            Enter(RP(9)); Enter(RP(8));
            Eq(2, P(8).amount, "opětovný vstup přidá"); Eq(1, P(9).amount, "druhý slot");
            // stání nad slotem: žádné další události → žádné další kusy
            Eq(2, A.Cursor.amount, "kurzor");
            Enter(RP(9)); Enter(RP(10)); Enter(RP(11));   // došly kusy na 10, 11 nic
            Up(1); Tick();
            Eq(5, Total(stone), "kameny"); True(A.Cursor.IsEmpty, "kurzor prázdný"); True(P(11).IsEmpty, "11 prázdný");
            P(12).Set(log, 1); A.Cursor.Set(stone, 2);
            Down(RP(13), 1); Enter(RP(12)); Up(1); Tick();   // nekompatibilní slot přeskočit (bez prohození)
            True(P(12).item == log && A.Cursor.amount == 1, "RMB tah neprohazuje");
            Invariants();
        }

        private static void LmbTweakWithItem()
        {
            Reset();
            P(8).Set(stone, 10); P(9).Set(stone, 20); P(10).Set(stone, 50); P(11).Set(log, 5); P(0).Set(stone, 4); P(12).Set(stone, 3);
            Down(RP(8), 0);
            Enter(RP(9)); Enter(RP(10)); Enter(RP(11)); Enter(RP(0));
            Eq(34, A.Cursor.amount, "sebráno 10+20+4, 50 se nevejde"); Eq(50, P(10).amount, "plný přeskočen");
            Enter(RP(12), true);  // se Shiftem rychlý přesun (do hotbaru)
            True(P(12).IsEmpty, "Shift tažení přesunulo");
            Up(0); Tick();
            Eq(87, Total(stone), "kameny"); Invariants();
        }

        private static void LmbTweakWithoutItem()
        {
            Reset();
            P(0).Set(stone, 5); P(1).Set(log, 6); P(2).Set(stone, 7);
            Down(RP(0), 0, true); Enter(RP(1), true); Enter(RP(2), true); Up(0); Tick();
            True(P(0).IsEmpty && P(1).IsEmpty && P(2).IsEmpty, "hotbar vyprázdněn do inventáře");
            Eq(12, Total(stone), "kameny"); Eq(6, Total(log), "klády"); True(A.Cursor.IsEmpty, "kurzor prázdný");
            Down(RP(8), 0); Enter(RP(9)); Up(0); Tick();   // bez Shiftu a bez itemu: jen vzetí stacku
            Invariants();
        }

        private static void DoubleClick()
        {
            Reset();
            P(8).Set(stone, 10); P(9).Set(stone, 64); P(10).Set(stone, 30); P(0).Set(stone, 30);
            Down(RP(8), 0); Up(0); Tick(0.1f);         // první klik: vzít 10
            Down(RP(8), 0);                            // druhý stisk do 250 ms: sesbírat
            Eq(64, A.Cursor.amount, "sesbíráno do limitu");
            Eq(64, P(9).amount, "plný stack netknut (neplné mají přednost)");
            True(P(8).IsEmpty, "dvojklik nepoložil zpět");
            Enter(RP(11)); Up(0); Tick();              // tažení po dvojkliku nespustí split
            True(P(11).IsEmpty, "žádný split po dvojkliku");
            Eq(134, Total(stone), "kameny"); Invariants();
            // rychlé klikání: trojklik nesmí být druhý dvojklik
            Click(RP(11), 0); Tick(-0.9f); Down(RP(11), 0); Up(0); Tick(0.1f); Down(RP(11), 0); Up(0); Tick();
            Eq(134, Total(stone), "kameny po trojkliku"); Invariants();
        }

        private static void ShiftDoubleClick()
        {
            Reset();
            P(8).Set(stone, 5); P(9).Set(stone, 6); P(10).Set(log, 7); P(11).Set(stone, 8);
            Down(RP(8), 0, true); Up(0); Tick(0.1f);
            Down(RP(8), 0, true); Up(0); Tick();
            True(P(9).IsEmpty && P(11).IsEmpty && P(10).item == log, "stejné přesunuty, cizí zůstal");
            Eq(19, Total(stone), "kameny"); Invariants();
        }

        private static void NumberKeys()
        {
            Reset();
            P(20).Set(stone, 5); P(2).Set(log, 3);
            True(A.SwapWithHotbar(RP(20), 2), "swap");
            True(P(2).item == stone && P(20).item == log, "prohozeno");
            A.Cursor.Set(stick, 1);
            True(!A.SwapWithHotbar(RP(20), 2), "s itemem na kurzoru se neprohazuje");
            A.Cursor.Clear();
            G(0).Set(stick, 4); A.ForceGridVisible = true;
            True(A.SwapWithHotbar(RG(0), 5), "grid ↔ hotbar"); True(P(5).item == stick && G(0).IsEmpty, "grid prohozen");
        }

        private static void Wheel()
        {
            Reset();
            P(0).Set(stone, 5); P(30).Set(stone, 62); P(9).Set(log, 1);
            Eq(2, A.WheelPush(RP(0), 2), "push 2");
            Eq(64, P(30).amount, "nejdřív do kompatibilního stacku");
            Eq(3, A.WheelPush(RP(0), 10), "push zbytek");
            True(P(0).IsEmpty && P(8).item == stone && P(8).amount == 3, "pak do prvního prázdného");
            P(1).Set(stone, 60);
            Eq(4, A.WheelPull(RP(1), 9, true), "pull do limitu");
            Eq(64, P(1).amount, "hotbar plný"); Eq(60, P(30).amount, "last-to-first bral z posledního slotu (30 před 8)"); Eq(3, P(8).amount, "slot 8 netknut");
            P(1).Set(stone, 1); P(8).Set(stone, 3);
            A.WheelPull(RP(1), 1, false); Eq(2, P(8).amount, "first-to-last bral z prvního");
            A.Cursor.Set(stone, 1);
            Eq(0, A.WheelPush(RP(1), 1), "s kompatibilním itemem na kurzoru nic");
            A.Cursor.Clear();
            // grid otevřen: druhá oblast je grid
            A.ForceGridVisible = true; G(4).Set(log, 2);
            Eq(1, A.WheelPush(RP(9), 1), "do gridu"); Eq(3, G(4).amount, "kompatibilní grid slot");
            P(15).Set(log, 2);
            Eq(2, A.WheelPull(RG(4), 5, true), "grid přitáhne z inventáře"); Eq(5, G(4).amount, "grid");
            int tot = Total(stone) + Total(log);
            // směr: D.Wheel s nastavením (výchozí: dolů = push)
            D.Wheel(RP(1), -1f);
            Eq(tot, Total(stone) + Total(log), "kolečko zachová počty");
            Invariants();
        }

        private static void Crafting()
        {
            Reset(true);
            var sl = ScriptableObject.CreateInstance<ShapelessRecipe>(); temp.Add(sl);
            sl.ingredients = new List<InventoryItemData> { stick, stick }; sl.result = plank; sl.resultAmount = 4;
            ctrl.shapelessRecipes.Add(sl);
            G(0).Set(stick, 2);
            Click(SlotRef.Output, 0);
            True(G(0).IsEmpty, "shapeless spotřeboval OBĚ hůlky (dřív zůstala 1)");
            Eq(4, A.Cursor.amount, "výsledek na kurzoru");
            // výstup není skladovací slot
            Click(SlotRef.Output, 0); Eq(4, A.Cursor.amount, "bez receptu nic");
            True(!A.PlaceOne(SlotRef.Output), "do výstupu nejde položit");
            A.Cursor.Clear();

            var sh = ScriptableObject.CreateInstance<ShapedRecipe>(); temp.Add(sh);
            sh.width = 1; sh.height = 2; sh.pattern = new[] { log, log }; sh.result = stick; sh.resultAmount = 4;
            ctrl.shapedRecipes.Add(sh);
            G(0).Set(log, 3); G(3).Set(log, 3);
            for (int i = 0; i < 63; i++) P(i).Set(stone, 64);
            P(40).Set(stick, 56);                    // místo jen na 2 crafty (8 hůlek)
            ctrl.Recalculate();
            int n = A.CraftAll();
            Eq(2, n, "Craft All zastaví na kapacitě");
            Eq(1, G(0).amount, "zbyla klády na třetí craft"); Eq(64, P(40).amount, "hůlky");
            A.Cursor.Set(log, 1);
            Click(SlotRef.Output, 0); Eq(1, A.Cursor.amount, "nekompatibilní kurzor nebere výsledek");
            A.Cursor.Set(stick, 62);
            Click(SlotRef.Output, 0); Eq(62, A.Cursor.amount, "nevejde se celý výsledek → nic (dřív ztráta)");
            Eq(1, G(0).amount, "suroviny nespotřebovány");
            A.Cursor.Set(stick, 60);
            Click(SlotRef.Output, 0); Eq(64, A.Cursor.amount, "vejde se → craft"); True(G(0).IsEmpty && G(3).IsEmpty, "spotřeba 1/buňku");
            Invariants();
        }

        private static int InInventory(InventoryItemData it)
        {
            int n = 0;
            foreach (var x in inv.Slots) if (!x.IsEmpty && x.item == it) n += x.amount;
            return n;
        }

        /// <summary>23.10.2026: Shift+LMB na výstup s obsazeným kurzorem = stávající Craft All do inventáře.</summary>
        private static void CraftShiftWithCursor()
        {
            Reset(true);
            var sh = ScriptableObject.CreateInstance<ShapedRecipe>(); temp.Add(sh);
            sh.width = 1; sh.height = 2; sh.pattern = new[] { log, log }; sh.result = stick; sh.resultAmount = 4;
            ctrl.shapedRecipes.Add(sh);

            // jiný item na kurzoru
            G(0).Set(log, 3); G(3).Set(log, 3);
            A.Cursor.Set(stone, 10);
            Click(SlotRef.Output, 0, true);
            True(A.Cursor.item == stone && A.Cursor.amount == 10, "kurzor (jiný item) beze změny");
            Eq(12, InInventory(stick), "3 crafty do inventáře");
            True(G(0).IsEmpty && G(3).IsEmpty, "suroviny spotřebovány přesně na 3 crafty");

            // stejný item jako výsledek na kurzoru
            G(0).Set(log, 2); G(3).Set(log, 2);
            A.Cursor.Set(stick, 5);
            Click(SlotRef.Output, 0, true);
            True(A.Cursor.item == stick && A.Cursor.amount == 5, "kurzor (stejný item) beze změny");
            Eq(20, InInventory(stick), "2 další crafty do inventáře, ne na kurzor");

            // částečná kapacita: místo jen na 1 craft; na kurzoru surovina receptu
            for (int i = 0; i < 63; i++) P(i).Set(stone, 64);
            P(40).Set(stick, 60);
            G(0).Set(log, 3); G(3).Set(log, 3);
            A.Cursor.Set(log, 7);
            Click(SlotRef.Output, 0, true);
            Eq(64, P(40).amount, "jen 1 craft (kapacita)");
            Eq(2, G(0).amount, "zbylo na 2 crafty");
            True(A.Cursor.item == log && A.Cursor.amount == 7, "kurzor se surovinou beze změny");

            // plný inventář: nic
            Click(SlotRef.Output, 0, true);
            Eq(2, G(0).amount, "plný inventář: žádný craft");

            // bez surovin (recept nesedí): nic, kurzor se do výstupu nepoloží
            foreach (var x in inv.Slots) x.Clear();
            G(0).Set(log, 1); G(3).Clear();
            A.Cursor.Set(stone, 3);
            Click(SlotRef.Output, 0, true);
            True(A.Cursor.item == stone && A.Cursor.amount == 3, "bez receptu kurzor beze změny");
            Eq(1, G(0).amount, "surovina nespotřebována"); Eq(0, InInventory(stick), "nic nevzniklo");
            Invariants();
            A.Cursor.Clear();
        }

        private static void ReturnFull()
        {
            Reset(true);
            for (int i = 0; i < 63; i++) P(i).Set(log, 64);
            P(3).Set(stone, 60);
            G(0).Set(stone, 10); A.Cursor.Set(stone, 7);
            craft.ReturnAllToInventory();
            A.ReturnCursorToInventory();
            Eq(77, Total(stone), "nic se neztratilo");
            Eq(64, P(3).amount, "doplněno"); Eq(6, G(0).amount, "zbytek v gridu"); Eq(7, A.Cursor.amount, "kurzor zůstal");
        }

        private static void SaveSnapshot()
        {
            Reset(true);
            P(0).Set(stone, 3); G(2).Set(stone, 5); A.Cursor.Set(log, 9);
            var m = typeof(Orivilon.SaveSystem.SaveSystem).GetMethod("SaveInventory", BindingFlags.NonPublic | BindingFlags.Static);
            var data = (Orivilon.SaveSystem.InventorySaveData)m.Invoke(null, null);
            int s = 0, l = 0;
            foreach (var e in data.slots) { if (e.itemID == "qa_stone") s += e.amount; if (e.itemID == "qa_log") l += e.amount; }
            Eq(8, s, "kameny v savu"); Eq(9, l, "klády z kurzoru v savu"); Eq(63, data.slots.Count, "počet slotů savu");
            Eq(5, G(2).amount, "živý grid beze změny"); Eq(9, A.Cursor.amount, "živý kurzor beze změny"); Eq(3, P(0).amount, "živý slot");
        }

        // ───────────── drop (spawn nahrazen záznamem „světa“) ─────────────

        private static readonly Dictionary<InventoryItemData, int> world = new Dictionary<InventoryItemData, int>();
        private static int spawns;
        private static bool spawnOk = true;

        private static void HookSpawn()
        {
            world.Clear(); spawns = 0; spawnOk = true;
            Orivilon.World.Objects.ItemDropService.SpawnOverride = (it, n, delay) =>
            {
                if (!spawnOk) return false;
                spawns++;
                world.TryGetValue(it, out int w); world[it] = w + n;
                return true;
            };
        }

        private static int W(InventoryItemData it) => world.TryGetValue(it, out int n) ? n : 0;

        private static void DropSlot()
        {
            Reset(); HookSpawn();
            P(0).Set(stone, 10); P(1).Set(tool, 1);
            True(A.DropFromSlot(RP(0), false), "Q"); Eq(9, P(0).amount, "Q vzal 1"); Eq(1, W(stone), "ve světě 1");
            True(A.DropFromSlot(RP(0), true), "Ctrl+Q"); True(P(0).IsEmpty, "Ctrl+Q vzal stack"); Eq(10, W(stone), "ve světě 10");
            True(!A.DropFromSlot(RP(0), false), "prázdný zdroj nic"); Eq(2, spawns, "žádný spawn navíc");
            True(A.DropFromSlot(RP(1), false), "nástroj"); True(P(1).IsEmpty && W(tool) == 1, "maxStack 1");
            P(2).Set(stone, 5); spawnOk = false;
            True(!A.DropFromSlot(RP(2), true), "selhání spawnu"); Eq(5, P(2).amount, "zdroj ponechán");
            spawnOk = true;
            P(3).Set(log, 40);
            for (int i = 0; i < 60; i++) A.DropFromSlot(RP(3), false);   // rychlé opakování
            Eq(40, Total(log) + W(log), "klády inventář+svět"); Eq(40, W(log), "vše ve světě"); True(P(3).IsEmpty, "slot prázdný");
            A.Cursor.Set(stone, 3); P(4).Set(stone, 2);
            True(!A.DropFromSlot(RP(4), false), "s itemem na kurzoru Q nad slotem nic (Minecraft)");
            Eq(20, Total(stone) + W(stone), "kameny inventář+kurzor+svět");
            Invariants();
        }

        private static void DropCursor()
        {
            Reset(true); HookSpawn();
            A.Cursor.Set(stone, 7);
            True(A.DropCursor(false), "RMB venku"); Eq(6, A.Cursor.amount, "kurzor -1"); Eq(1, W(stone), "svět 1");
            True(A.DropCursor(true), "LMB venku"); True(A.Cursor.IsEmpty && W(stone) == 7, "celý stack");
            True(!A.DropCursor(true), "prázdný kurzor nic");
            spawnOk = false; A.Cursor.Set(stone, 4);
            True(!A.DropCursor(true), "selhání"); Eq(4, A.Cursor.amount, "kurzor ponechán");
            spawnOk = true; A.Cursor.Clear();
            // výstup craftingu: craft na kurzor + vyhození
            var sl = ScriptableObject.CreateInstance<ShapelessRecipe>(); temp.Add(sl);
            sl.ingredients = new List<InventoryItemData> { stick }; sl.result = plank; sl.resultAmount = 4;
            ctrl.shapelessRecipes.Add(sl); G(0).Set(stick, 1);
            True(A.DropFromSlot(SlotRef.Output, false), "Q nad výstupem"); Eq(4, W(plank), "výsledek ve světě"); True(G(0).IsEmpty && A.Cursor.IsEmpty, "spotřeba, kurzor prázdný");
            // konec splitu mimo UI: uvolnění nesmí vyhodit
            A.Cursor.Set(stone, 6); int before = spawns;
            Down(RP(8), 0); Enter(RP(9)); Up(0); Tick();
            Eq(before, spawns, "konec tažení venku nic nevyhodil"); Eq(13, Total(stone) + W(stone), "kameny");
            Invariants();
        }

        private static void DropPickup()
        {
            Reset();
            var go = new GameObject("DropTest") { hideFlags = HideFlags.HideAndDontSave };
            temp.Add(go);
            var pick = go.AddComponent<Orivilon.World.Objects.PickupItem>();
            pick.itemData = stone; pick.itemCount = 10;
            var dropped = go.AddComponent<Orivilon.World.Objects.DroppedItem>();
            dropped.Init(pick, null, null, 1000f, true);
            pick.PickUp();
            Eq(10, pick.itemCount, "ochrana: nic nesebráno"); Eq(0, Total(stone), "inventář");
            dropped.pickupAllowedAt = -1f;
            for (int i = 0; i < 63; i++) P(i).Set(log, 64);
            P(5).Set(stone, 61);                         // místo na 3
            pick.PickUp();
            Eq(7, pick.itemCount, "zbytek ve světě"); Eq(64, P(5).amount, "doplněno"); True(go != null, "předmět zůstal");
            pick.PickUp();
            Eq(7, pick.itemCount, "plný inventář: nic"); Eq(71, Total(stone) + pick.itemCount, "kameny inventář+svět");
            P(5).Clear(); P(6).Clear();
            pick.PickUp();
            True(go == null, "celý sebraný → zničen"); Eq(7, Total(stone), "kameny po sebrání (slot 5 test vyprázdnil)");
        }

        private static void Fuzz()
        {
            Reset();
            var rnd = new System.Random(16102026);
            var items = new[] { stone, log, small, tool };
            for (int i = 0; i < 40; i++) { var it = items[rnd.Next(items.Length)]; P(rnd.Next(63)).Set(it, 1 + rnd.Next(InventorySlotData.StackLimit(it))); }
            for (int i = 0; i < 5; i++) { var it = items[rnd.Next(items.Length)]; G(rnd.Next(9)).Set(it, 1 + rnd.Next(InventorySlotData.StackLimit(it))); }
            var totals = new int[items.Length];
            for (int k = 0; k < items.Length; k++) totals[k] = Total(items[k]);
            int held = -1;
            for (int step = 0; step < 4000; step++)
            {
                A.ForceGridVisible = rnd.Next(4) == 0; A.ForceMainVisible = rnd.Next(5) != 0;
                SlotRef r = rnd.Next(6) == 0 ? RG(rnd.Next(9)) : rnd.Next(30) == 0 ? SlotRef.Output : RP(rnd.Next(63));
                bool shift = rnd.Next(4) == 0;
                switch (rnd.Next(9))
                {
                    case 0: case 1: if (held < 0) { held = rnd.Next(2); Down(r, held, shift); } break;
                    case 2: case 3: Enter(r, shift); break;
                    case 4: if (held >= 0) { Up(held); held = -1; } break;
                    // Kolečko a klávesy jdou jen mimo gesto (stejně jako InventoryDragDistributor.Tick).
                    case 5: if (held < 0) A.WheelPush(r, 1 + rnd.Next(3)); else D.Wheel(r, -1f); break;
                    case 6: if (held < 0) A.WheelPull(r, 1 + rnd.Next(3), rnd.Next(2) == 0); break;
                    case 7: if (held < 0) A.SwapWithHotbar(r, rnd.Next(8)); break;
                    case 8: if (rnd.Next(3) == 0) { D.CancelGesture(); held = -1; } else Tick(rnd.Next(2) == 0 ? 0.1f : 1f); break;
                }
                for (int k = 0; k < items.Length; k++)
                    if (Total(items[k]) != totals[k]) throw new Exception("krok " + step + ": " + items[k].id + " " + totals[k] + " → " + Total(items[k]));
                Invariants();
            }
            if (held >= 0) Up(held);
        }
    }
}
