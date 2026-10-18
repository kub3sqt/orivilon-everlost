using System.Collections.Generic;
using Orivilon.Inventory.Crafting;
using UnityEngine;

namespace Orivilon.Inventory.Inventory
{
    /// <summary>Oblast, do které slot patří. Hotbar a Main jsou části pole InventoryData.Slots.</summary>
    public enum SlotArea { None, Hotbar, Main, Grid, Output }

    /// <summary>
    /// Odkaz na jeden slot v kterékoli oblasti (hráčův inventář, craftovací grid, výstup craftingu).
    /// Hodnotový typ – drží jen oblast a index, nikdy referenci na data, takže se nedá „zaliasovat“.
    /// </summary>
    public readonly struct SlotRef : System.IEquatable<SlotRef>
    {
        public readonly SlotArea area;
        public readonly int index;

        public SlotRef(SlotArea area, int index) { this.area = area; this.index = index; }

        public static SlotRef Player(int i) => new SlotRef(i < InventoryData.HotbarSize ? SlotArea.Hotbar : SlotArea.Main, i);
        public static SlotRef Grid(int i) => new SlotRef(SlotArea.Grid, i);
        public static SlotRef Output => new SlotRef(SlotArea.Output, 0);

        public bool IsValid => area != SlotArea.None;
        public bool IsPlayer => area == SlotArea.Hotbar || area == SlotArea.Main;
        public bool IsGrid => area == SlotArea.Grid;
        public bool IsOutput => area == SlotArea.Output;

        /// <summary>Data slotu (null pro výstup craftingu nebo neplatný odkaz).</summary>
        public InventorySlotData Data
        {
            get
            {
                if (IsPlayer)
                {
                    var inv = InventoryData.Instance;
                    return inv != null && index >= 0 && index < inv.Slots.Length ? inv.Slots[index] : null;
                }
                if (IsGrid)
                {
                    var cd = CraftingData.Instance;
                    return cd != null && index >= 0 && index < cd.gridSlots.Length ? cd.gridSlots[index] : null;
                }
                return null;
            }
        }

        public bool Equals(SlotRef o) => area == o.area && index == o.index;
        public override bool Equals(object o) => o is SlotRef r && Equals(r);
        public override int GetHashCode() => ((int)area * 397) ^ index;
        public static bool operator ==(SlotRef a, SlotRef b) => a.Equals(b);
        public static bool operator !=(SlotRef a, SlotRef b) => !a.Equals(b);
        public override string ToString() => area + "[" + index + "]";
    }

    /// <summary>
    /// Autoritativní operace nad inventářem, gridem a kurzorem (vzor Minecraft Java + Mouse Tweaks).
    /// Každá operace jen PŘESOUVÁ kusy mezi existujícími sloty a kurzorem – celkový počet i identita
    /// itemu se zachovávají. Počet mění jen crafting (spotřeba surovin → výsledek) a vnější
    /// pickup/stavění/jídlo. Data kurzoru (<see cref="Cursor"/>) jsou vlastní objekt a nikdy se
    /// nesdílí s objektem slotu – přesuny kopírují item+počet.
    /// UI se obnoví jediným voláním <see cref="Commit"/> po dokončení akce.
    /// </summary>
    public static class InventoryActions
    {
        /// <summary>Stack držený kurzorem. Prázdný = kurzor nic nedrží.</summary>
        public static readonly InventorySlotData Cursor = new InventorySlotData();

        /// <summary>QA/testy bez UI: vynucená viditelnost hlavního inventáře a gridu (null = podle UI).</summary>
        public static bool? ForceMainVisible, ForceGridVisible;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            Cursor.Clear();
            ForceMainVisible = ForceGridVisible = null;
        }

        /// <summary>Je vidět panel hlavního inventáře (sloty HotbarSize+)?</summary>
        public static bool MainVisible
        {
            get
            {
                if (ForceMainVisible.HasValue) return ForceMainVisible.Value;
                var c = InventoryUIController.Instance;
                if (c != null) return c.IsInventoryPanelOpen;
                // Refresh HUD bez záložkového controlleru: podle aktivních UI slotů.
                return InventorySlotRegistry.Find(SlotRef.Player(InventoryData.HotbarSize)) != null || InventorySlotRegistry.Count == 0;
            }
        }

        /// <summary>Je vidět craftovací grid?</summary>
        public static bool GridVisible
        {
            get
            {
                if (ForceGridVisible.HasValue) return ForceGridVisible.Value;
                var c = InventoryUIController.Instance;
                if (c != null) return c.IsCraftingOpen;
                return InventorySlotRegistry.Find(SlotRef.Grid(0)) != null;
            }
        }

        private static InventorySlotData[] PlayerSlots => InventoryData.Instance != null ? InventoryData.Instance.Slots : null;
        private static InventorySlotData[] GridSlots => CraftingData.Instance != null ? CraftingData.Instance.gridSlots : null;

        /// <summary>Stejná věc bez ohledu na stackovatelnost (pro Shift+dvojklik u nástrojů).</summary>
        public static bool SameItem(InventoryItemData a, InventoryItemData b)
            => a != null && b != null && (a == b || (!string.IsNullOrEmpty(a.id) && a.id == b.id));

        private static void Normalize(InventorySlotData s) { if (s != null && s.amount <= 0) s.Clear(); }

        // ───────────────────────── základní kliky ─────────────────────────

        /// <summary>LMB: vzít celý stack / položit / sloučit / prohodit. Na výstupu = vzít výsledek craftu.</summary>
        public static bool LeftClick(SlotRef r)
        {
            if (r.IsOutput) return CraftToCursor();
            var s = r.Data;
            if (s == null) return false;

            if (Cursor.IsEmpty)
            {
                if (s.IsEmpty) return false;
                Cursor.Set(s.item, s.amount);
                s.Clear();
                return true;
            }
            if (s.IsEmpty)
            {
                int put = Mathf.Min(Cursor.amount, InventorySlotData.StackLimit(Cursor.item));
                s.Set(Cursor.item, put);
                Cursor.amount -= put;
                Normalize(Cursor);
                return true;
            }
            if (InventorySlotData.Compatible(s.item, Cursor.item))
            {
                int move = Mathf.Min(s.FreeSpace, Cursor.amount);
                if (move <= 0) return false;
                s.amount += move;
                Cursor.amount -= move;
                Normalize(Cursor);
                return true;
            }
            return Swap(s);
        }

        /// <summary>RMB: prázdný kurzor vezme polovinu (zaokrouhleno nahoru), jinak položí jeden kus / prohodí.</summary>
        public static bool RightClick(SlotRef r)
        {
            if (r.IsOutput) return CraftToCursor();
            var s = r.Data;
            if (s == null) return false;

            if (Cursor.IsEmpty)
            {
                if (s.IsEmpty) return false;
                int take = (s.amount + 1) / 2;
                Cursor.Set(s.item, take);
                s.amount -= take;
                Normalize(s);
                return true;
            }
            if (s.IsEmpty || InventorySlotData.Compatible(s.item, Cursor.item))
                return PlaceOne(r);
            return Swap(s);
        }

        /// <summary>Položí jeden kus z kurzoru do prázdného nebo kompatibilního neplného slotu (nikdy neprohazuje).</summary>
        public static bool PlaceOne(SlotRef r)
        {
            if (r.IsOutput || Cursor.IsEmpty) return false;
            var s = r.Data;
            if (s == null) return false;
            if (s.IsEmpty) s.Set(Cursor.item, 1);
            else if (InventorySlotData.Compatible(s.item, Cursor.item) && s.FreeSpace > 0) s.amount++;
            else return false;
            Cursor.amount--;
            Normalize(Cursor);
            return true;
        }

        private static bool Swap(InventorySlotData s)
        {
            if (Cursor.amount > InventorySlotData.StackLimit(Cursor.item)) return false;
            var item = s.item; int amount = s.amount;
            s.Set(Cursor.item, Cursor.amount);
            Cursor.Set(item, amount);
            return true;
        }

        // ───────────────────────── crafting ─────────────────────────

        /// <summary>Výsledek craftu na kurzor (prázdný nebo kompatibilní kurzor, celý výsledek se musí vejít).</summary>
        public static bool CraftToCursor()
        {
            var ctrl = CraftingController.Instance;
            var data = CraftingData.Instance;
            if (ctrl == null || data == null) return false;
            ctrl.Recalculate();
            if (data.resultItem == null) return false;
            var result = data.resultItem;
            int amount = ctrl.ResultAmount;
            if (!Cursor.IsEmpty &&
                !(InventorySlotData.Compatible(Cursor.item, result) && Cursor.amount + amount <= InventorySlotData.StackLimit(result)))
                return false;
            if (!ctrl.TryCraftOnce(out var item, out int made)) return false;
            if (Cursor.IsEmpty) Cursor.Set(item, made); else Cursor.amount += made;
            return true;
        }

        /// <summary>Jeden craft přímo do inventáře hráče; provede se, jen když se vejde celý výsledek.</summary>
        public static bool CraftToPlayer()
        {
            var ctrl = CraftingController.Instance;
            var data = CraftingData.Instance;
            var slots = PlayerSlots;
            if (ctrl == null || data == null || slots == null) return false;
            ctrl.Recalculate();
            if (data.resultItem == null) return false;
            if (InventoryData.SpaceFor(slots, 0, slots.Length - 1, data.resultItem) < ctrl.ResultAmount) return false;
            if (!ctrl.TryCraftOnce(out var item, out int made)) return false;
            InventoryData.InsertSmart(slots, item, made);
            return true;
        }

        /// <summary>Shift+klik na výstup: craftuje, dokud recept sedí a výsledek se vejde.</summary>
        public static int CraftAll()
        {
            var data = CraftingData.Instance;
            CraftingController.Instance?.Recalculate();
            if (data == null || data.resultItem == null) return 0;
            var first = data.resultItem;
            int n = 0;
            while (n < 4096 && data.resultItem != null && SameItem(data.resultItem, first) && CraftToPlayer()) n++;
            return n;
        }

        /// <summary>Číselná klávesa nad výstupem: jeden craft do daného slotu hotbaru (prázdného či kompatibilního).</summary>
        public static bool CraftIntoSlot(InventorySlotData target)
        {
            var ctrl = CraftingController.Instance;
            var data = CraftingData.Instance;
            if (ctrl == null || data == null || target == null) return false;
            ctrl.Recalculate();
            if (data.resultItem == null) return false;
            var result = data.resultItem;
            if (!target.IsEmpty &&
                !(InventorySlotData.Compatible(target.item, result) && target.amount + ctrl.ResultAmount <= InventorySlotData.StackLimit(result)))
                return false;
            if (!ctrl.TryCraftOnce(out var item, out int made)) return false;
            if (target.IsEmpty) target.Set(item, made); else target.amount += made;
            return true;
        }

        // ───────────────────────── rychlé přesuny ─────────────────────────

        /// <summary>
        /// Shift+klik jako v Minecraftu: Hotbar ↔ Main, grid → inventář (priorita AddItemSmart),
        /// výstup → Craft All. Do gridu jde Shift+klik jen tehdy, když hlavní inventář není vidět
        /// (stará záložka Crafting ukazuje jen hotbar + grid – zachované dřívější chování).
        /// Přesune, co se vejde, zbytek zůstane.
        /// Vrací item, který se pohnul (pro Shift+dvojklik), nebo null.
        /// </summary>
        public static InventoryItemData QuickMove(SlotRef r)
        {
            if (r.IsOutput)
            {
                CraftingController.Instance?.Recalculate();
                var res = CraftingData.Instance != null ? CraftingData.Instance.resultItem : null;
                return CraftAll() > 0 ? res : null;
            }
            var s = r.Data;
            if (s == null || s.IsEmpty) return null;
            var item = s.item;
            int moved = 0;
            var p = PlayerSlots;
            if (r.IsGrid)
            {
                if (p != null) moved = InventoryData.InsertSmart(p, item, s.amount);
            }
            else if (r.area == SlotArea.Hotbar && MainVisible)
            {
                if (p != null) moved = InventoryData.InsertPartial(p, InventoryData.HotbarSize, p.Length - 1, item, s.amount);
            }
            else if (r.area == SlotArea.Hotbar)
            {
                if (GridVisible && GridSlots != null) moved = InventoryData.InsertPartial(GridSlots, 0, GridSlots.Length - 1, item, s.amount);
            }
            else if (p != null)
            {
                moved = InventoryData.InsertPartial(p, 0, InventoryData.HotbarSize - 1, item, s.amount);
            }
            if (moved <= 0) return null;
            s.amount -= moved;
            Normalize(s);
            return item;
        }

        /// <summary>Shift+dvojklik: rychlý přesun všech stejných itemů ze stejné skupiny (hráč nebo grid).</summary>
        public static int QuickMoveAllSame(SlotRef r, InventoryItemData item)
        {
            if (item == null || r.IsOutput) return 0;
            int n = 0;
            foreach (var o in GroupOf(r))
            {
                var s = o.Data;
                if (s != null && !s.IsEmpty && SameItem(s.item, item) && QuickMove(o) != null) n++;
            }
            return n;
        }

        /// <summary>Dvojklik: sesbírá kompatibilní kusy z viditelných slotů na kurzor, do limitu stacku.
        /// Jako vanilla nejdřív bere z neplných stacků, plné až ve druhém průchodu.</summary>
        public static bool CollectToCursor()
        {
            if (Cursor.IsEmpty || !Cursor.item.stackable) return false;
            int limit = InventorySlotData.StackLimit(Cursor.item);
            bool any = false;
            var refs = VisibleStorage();
            for (int pass = 0; pass < 2 && Cursor.amount < limit; pass++)
            {
                foreach (var r in refs)
                {
                    if (Cursor.amount >= limit) break;
                    var s = r.Data;
                    if (s == null || s.IsEmpty || !InventorySlotData.Compatible(s.item, Cursor.item)) continue;
                    if (pass == 0 && s.amount >= InventorySlotData.StackLimit(s.item)) continue;
                    int take = Mathf.Min(s.amount, limit - Cursor.amount);
                    s.amount -= take;
                    Cursor.amount += take;
                    Normalize(s);
                    any = true;
                }
            }
            return any;
        }

        /// <summary>Číselná klávesa: prohodí slot pod kurzorem se slotem hotbaru (jen s prázdným kurzorem).</summary>
        public static bool SwapWithHotbar(SlotRef r, int hotbarIndex)
        {
            var p = PlayerSlots;
            if (!Cursor.IsEmpty || p == null || hotbarIndex < 0 || hotbarIndex >= InventoryData.HotbarSize) return false;
            var hot = p[hotbarIndex];
            if (r.IsOutput) return CraftIntoSlot(hot);
            var s = r.Data;
            if (s == null || (r.IsPlayer && r.index == hotbarIndex)) return false;
            if (s.IsEmpty && hot.IsEmpty) return false;
            var item = s.item; int amount = s.amount;
            s.Set(hot.item, hot.amount);
            hot.Set(item, amount);
            return true;
        }

        // ───────────────────────── Mouse Tweaks: kolečko ─────────────────────────

        /// <summary>Kolečko dolů: odešle až n kusů ze slotu po jednom do „druhé“ oblasti
        /// (kompatibilní neplné stacky, pak prázdné sloty – pořadí jako MouseTweaks findPushSlots).</summary>
        public static int WheelPush(SlotRef r, int n)
        {
            if (r.IsOutput) return WheelOutput(true, n);
            var s = r.Data;
            if (s == null || s.IsEmpty || n <= 0) return 0;
            if (!Cursor.IsEmpty && InventorySlotData.Compatible(Cursor.item, s.item)) return 0;
            var other = OtherGroup(r);
            n = Mathf.Min(n, s.amount);
            int moved = 0;
            var item = s.item;
            int limit = InventorySlotData.StackLimit(item);
            for (int pass = 0; pass < 2 && moved < n; pass++)
            {
                foreach (var o in other)
                {
                    if (moved >= n) break;
                    var t = o.Data;
                    if (t == null) continue;
                    int room;
                    if (pass == 0) { if (t.IsEmpty || !InventorySlotData.Compatible(t.item, item)) continue; room = limit - t.amount; }
                    else { if (!t.IsEmpty) continue; room = limit; }
                    int m = Mathf.Min(room, n - moved);
                    if (m <= 0) continue;
                    if (t.IsEmpty) t.Set(item, m); else t.amount += m;
                    moved += m;
                }
            }
            s.amount -= moved;
            Normalize(s);
            return moved;
        }

        /// <summary>Kolečko nahoru: přitáhne až n kompatibilních kusů z „druhé“ oblasti do slotu (do limitu stacku).
        /// Pořadí hledání podle <paramref name="lastToFirst"/> (MouseTweaks WheelSearchOrder).</summary>
        public static int WheelPull(SlotRef r, int n, bool lastToFirst)
        {
            if (r.IsOutput) return WheelOutput(false, n);
            var s = r.Data;
            if (s == null || s.IsEmpty || n <= 0) return 0;
            if (!Cursor.IsEmpty && InventorySlotData.Compatible(Cursor.item, s.item)) return 0;
            int max = InventorySlotData.StackLimit(s.item) - s.amount;
            n = Mathf.Min(n, max);
            var other = OtherGroup(r);
            // Výstup craftingu je při otevřeném gridu součást „druhé“ oblasti; ve vanilla je to první slot menu.
            bool outputCandidate = r.IsPlayer && GridVisible && CraftingController.Instance != null;
            if (outputCandidate) CraftingController.Instance.Recalculate();
            int pulled = 0;
            while (n > 0)
            {
                int count = other.Count + (outputCandidate ? 1 : 0);
                SlotRef found = default;
                for (int k = 0; k < count; k++)
                {
                    int i = lastToFirst ? count - 1 - k : k;
                    SlotRef o = outputCandidate ? (i == 0 ? SlotRef.Output : other[i - 1]) : other[i];
                    if (o.IsOutput)
                    {
                        var res = CraftingData.Instance != null ? CraftingData.Instance.resultItem : null;
                        if (res != null && InventorySlotData.Compatible(res, s.item)) { found = o; break; }
                        continue;
                    }
                    var t = o.Data;
                    if (t != null && !t.IsEmpty && InventorySlotData.Compatible(t.item, s.item)) { found = o; break; }
                }
                if (!found.IsValid) break;
                if (found.IsOutput)
                {
                    int amount = CraftingController.Instance.ResultAmount;
                    if (max < amount) break;
                    if (!CraftIntoSlot(s)) break;
                    max -= amount; pulled += amount;
                    n = Mathf.Min(n - 1, max);
                    continue;
                }
                var src = found.Data;
                int take = Mathf.Min(src.amount, n);
                src.amount -= take;
                s.amount += take;
                Normalize(src);
                n -= take; max -= take; pulled += take;
            }
            return pulled;
        }

        /// <summary>Kolečko nad výstupem: s prázdným kurzorem dolů = craft do inventáře (jen celý výsledek);
        /// s kompatibilním kurzorem kterýmkoli směrem = craft na kurzor.</summary>
        private static int WheelOutput(bool push, int n)
        {
            var data = CraftingData.Instance;
            CraftingController.Instance?.Recalculate();
            if (data == null || data.resultItem == null) return 0;
            if (!Cursor.IsEmpty && !InventorySlotData.Compatible(Cursor.item, data.resultItem)) return 0;
            int crafts = 0;
            if (Cursor.IsEmpty)
            {
                if (!push) return 0;
                while (n-- > 0 && CraftToPlayer()) crafts++;
            }
            else
            {
                while (n-- > 0 && CraftToCursor()) crafts++;
            }
            return crafts;
        }

        // ───────────────────────── skupiny slotů ─────────────────────────

        /// <summary>Viditelné úložné sloty v pořadí vanilla menu: grid, hlavní inventář, hotbar.</summary>
        public static List<SlotRef> VisibleStorage()
        {
            var list = new List<SlotRef>(72);
            if (GridVisible && GridSlots != null) for (int i = 0; i < GridSlots.Length; i++) list.Add(SlotRef.Grid(i));
            AddPlayer(list, MainVisible, true);
            return list;
        }

        /// <summary>Skupina slotu („kontejner“ ve smyslu Minecraftu): hráč (hotbar+viditelný inventář) nebo grid.</summary>
        public static List<SlotRef> GroupOf(SlotRef r)
        {
            var list = new List<SlotRef>(64);
            if (r.IsGrid) { if (GridSlots != null) for (int i = 0; i < GridSlots.Length; i++) list.Add(SlotRef.Grid(i)); }
            else if (r.IsPlayer) AddPlayer(list, MainVisible, true);
            return list;
        }

        /// <summary>
        /// „Druhá“ oblast pro kolečko. MouseTweaks rozlišuje inventář hráče a cizí kontejner; tady je
        /// jediný cizí kontejner craftovací grid. Bez otevřeného gridu se použije stejné párování
        /// jako Shift+klik (hotbar ↔ hlavní inventář).
        /// </summary>
        public static List<SlotRef> OtherGroup(SlotRef r)
        {
            var list = new List<SlotRef>(64);
            if (r.IsGrid) AddPlayer(list, MainVisible, true);
            else if (r.IsPlayer)
            {
                if (GridVisible && GridSlots != null) for (int i = 0; i < GridSlots.Length; i++) list.Add(SlotRef.Grid(i));
                else if (r.area == SlotArea.Hotbar) { if (MainVisible) AddPlayer(list, true, false); }
                else AddPlayer(list, false, true);
            }
            return list;
        }

        /// <summary>Pořadí jako vanilla InventoryMenu: hlavní inventář, potom hotbar.</summary>
        private static void AddPlayer(List<SlotRef> list, bool main, bool hotbar)
        {
            var p = PlayerSlots;
            if (p == null) return;
            if (main) for (int i = InventoryData.HotbarSize; i < p.Length; i++) list.Add(SlotRef.Player(i));
            if (hotbar) for (int i = 0; i < InventoryData.HotbarSize && i < p.Length; i++) list.Add(SlotRef.Player(i));
        }

        // ───────────────────────── vyhazování ─────────────────────────

        /// <summary>
        /// Q / Ctrl+Q nad slotem (v inventáři jen s prázdným kurzorem, jako Minecraft THROW) nebo z aktivního
        /// slotu hotbaru při hraní. Kusy se odeberou až po úspěšném spawnu ve světě. Nad výstupem craftingu
        /// se vyrobí jeden výsledek na kurzor a ten se vyhodí – když vyhození selže, zůstane na kurzoru.
        /// </summary>
        public static bool DropFromSlot(SlotRef r, bool all)
        {
            if (!Cursor.IsEmpty) return false;
            if (r.IsOutput) { if (!CraftToCursor()) return false; DropCursor(true); return true; }
            var s = r.Data;
            if (s == null || s.IsEmpty) return false;
            int n = all ? s.amount : 1;
            if (!Orivilon.World.Objects.ItemDropService.TrySpawnInFrontOfPlayer(s.item, n, Orivilon.World.Objects.ItemDropService.PlayerDropPickupDelay))
                return false;
            s.amount -= n;
            Normalize(s);
            return true;
        }

        /// <summary>Klik mimo panely inventáře s itemem na kurzoru: LMB celý stack, RMB jeden kus.</summary>
        public static bool DropCursor(bool all)
        {
            if (Cursor.IsEmpty) return false;
            int n = all ? Cursor.amount : 1;
            if (!Orivilon.World.Objects.ItemDropService.TrySpawnInFrontOfPlayer(Cursor.item, n, Orivilon.World.Objects.ItemDropService.PlayerDropPickupDelay))
                return false;
            Cursor.amount -= n;
            Normalize(Cursor);
            return true;
        }

        // ───────────────────────── vrácení a commit ─────────────────────────

        /// <summary>Zavření inventáře: stack z kurzoru zpět do inventáře (co se nevejde, zůstane na kurzoru –
        /// hra nemá systém vyhazování, takže se nic neztratí).</summary>
        public static bool ReturnCursorToInventory()
        {
            var p = PlayerSlots;
            if (Cursor.IsEmpty || p == null) return false;
            int moved = InventoryData.InsertSmart(p, Cursor.item, Cursor.amount);
            Cursor.amount -= moved;
            Normalize(Cursor);
            return moved > 0;
        }

        /// <summary>Po každé akci: přepočet receptu, jedna událost změny (obnoví všechny sloty a hotbar) a kurzor.</summary>
        public static void Commit()
        {
            CraftingController.Instance?.Recalculate();
            InventoryData.Instance?.NotifyInventoryChanged();
            InventoryCursor.Instance?.SyncVisual();
        }

        /// <summary>Celkový počet kusů itemu v inventáři, gridu a na kurzoru (invariant zachování).</summary>
        public static int CountEverywhere(InventoryItemData item)
        {
            int n = 0;
            void Add(InventorySlotData s) { if (s != null && !s.IsEmpty && SameItem(s.item, item)) n += s.amount; }
            var p = PlayerSlots; if (p != null) foreach (var s in p) Add(s);
            var g = GridSlots; if (g != null) foreach (var s in g) Add(s);
            Add(Cursor);
            return n;
        }
    }
}
