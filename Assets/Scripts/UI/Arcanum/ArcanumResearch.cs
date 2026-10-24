using System.Collections.Generic;
using Orivilon.Inventory.Inventory;
using UnityEngine;

namespace Orivilon.UI.Arcanum
{
    public enum ArcanumResult { Ok, AlreadyUnlocked, Locked, MissingMaterials, NoInventory }

    /// <summary>
    /// Pravidla výzkumu nad skutečným inventářem hráče. Cena uzlu je seznam předmětů
    /// (<c>$</c> z data.js), odečítají se stejně jako v BuildingTool. Hradla receptů řeší story 002.
    /// </summary>
    public static class ArcanumResearch
    {
        private static Dictionary<string, InventoryItemData> items;

        public static InventoryItemData Item(string id)
        {
            if (items == null)
            {
                items = new Dictionary<string, InventoryItemData>();
                foreach (var it in Resources.LoadAll<InventoryItemData>("Items"))
                    if (it != null && !string.IsNullOrEmpty(it.id)) items[it.id] = it;
            }
            return id != null && items.TryGetValue(id, out var item) ? item : null;
        }

        /// <summary>První položka ceny uzlu (dočasně pro UI s jednou surovinou; skutečná cena je <see cref="ArcanumNode.cost"/>).</summary>
        public static string MaterialId(ArcanumNode n) =>
            n.cost != null && n.cost.Length > 0 ? n.cost[0].itemId : null;

        public static string MaterialName(ArcanumNode n)
        {
            var it = Item(MaterialId(n));
            return it != null ? it.itemName : "—";
        }

        /// <summary>Zobrazovaný název předmětu; předměty, které ještě neexistují (story 004), ukážou ID.</summary>
        public static string ItemName(string id)
        {
            var it = Item(id);
            return it != null ? it.itemName : id;
        }

        /// <summary>První položka ceny, které má hráč málo (null = cenu pokryje). Pro hlášku „Not enough …“.</summary>
        public static ArcanumTreeAsset.ItemAmount? FirstMissing(ArcanumNode n)
        {
            foreach (var c in n.cost) if (CountOwned(c.itemId) < c.amount) return c;
            return null;
        }

        /// <summary>Hláška pro MissingMaterials: jmenuje položku, která opravdu chybí (cena má více předmětů).</summary>
        public static string MissingMessage(ArcanumNode n)
        {
            var miss = FirstMissing(n);
            if (!miss.HasValue) return "Not enough materials";
            var c = miss.Value;
            return "Not enough " + ItemName(c.itemId) + ": " + CountOwned(c.itemId) + " / " + c.amount;
        }

        public static int CountOwned(string itemId)
        {
            var inv = InventoryData.Instance;
            if (inv == null || string.IsNullOrEmpty(itemId)) return 0;
            int count = 0;
            foreach (var slot in inv.Slots)
                if (slot != null && !slot.IsEmpty && InventoryItemData.Satisfies(slot.item, itemId)) count += slot.amount;   // i druhy dřeva za Log
            return count;
        }

        /// <summary>True, když hráč vlastní všechny předměty ceny uzlu (uzel bez ceny = vždy true).</summary>
        public static bool CanAfford(ArcanumNode n)
        {
            if (n.cost.Length == 0) return true;
            if (InventoryData.Instance == null) return false;
            foreach (var c in n.cost) if (CountOwned(c.itemId) < c.amount) return false;
            return true;
        }

        public static ArcanumResult Check(ArcanumNode n)
        {
            if (ArcanumProgress.IsUnlocked(n.id)) return ArcanumResult.AlreadyUnlocked;
            if (!ArcanumProgress.IsAvailable(n)) return ArcanumResult.Locked;
            if (n.cost.Length > 0)
            {
                if (InventoryData.Instance == null) return ArcanumResult.NoInventory;
                if (!CanAfford(n)) return ArcanumResult.MissingMaterials;
            }
            return ArcanumResult.Ok;
        }

        /// <summary>
        /// Atomický výzkum: nejdřív kompletní validace, pak odečtení všech předmětů ceny a odemčení
        /// v jednom synchronním kroku. Opakované volání (dvojklik) selže na AlreadyUnlocked.
        /// Uzel bez ceny (např. typ D) se odemyká bez spotřeby.
        /// </summary>
        public static ArcanumResult TryUnlock(ArcanumNode n)
        {
            var result = Check(n);
            if (result != ArcanumResult.Ok) return result;

            if (n.cost.Length > 0)
            {
                foreach (var c in n.cost) Consume(c.itemId, c.amount);
                InventoryData.Instance.NotifyInventoryChanged();
            }

            ArcanumProgress.MarkUnlocked(n.id);
            return ArcanumResult.Ok;
        }

        private static void Consume(string id, int amount)
        {
            int left = amount;
            var slots = InventoryData.Instance.Slots;
            // Nejdřív hlavní inventář od konce, hotbar (0–7) až nakonec – hráč nepřijde o věci v ruce zbytečně.
            // Satisfies zajistí, že za "Log (any wood)" se spotřebují i druhy dřeva.
            for (int i = slots.Length - 1; i >= 0 && left > 0; i--)
            {
                var slot = slots[i];
                if (slot == null || slot.IsEmpty || !InventoryItemData.Satisfies(slot.item, id)) continue;
                int take = Mathf.Min(slot.amount, left);
                string takenId = slot.item.id;
                slot.amount -= take;
                left -= take;
                if (slot.amount <= 0) slot.Clear();
                Orivilon.Multiplayer.MpLedger.ReportConsume(takenId, take);   // MP klient: spotřeba i u hosta, po skutečných itemech
            }
        }

        /// <summary>Nejlevnější dostupný výzkum v oboru (nebo null, když není žádný dostupný).</summary>
        public static ArcanumNode NextInCategory(int c)
        {
            ArcanumNode best = null;
            int bestCost = 0;
            foreach (var n in ArcanumData.Nodes)
            {
                if (n.category != c || !ArcanumProgress.IsAvailable(n)) continue;
                int cost = TotalCost(n);
                if (best == null || cost < bestCost) { best = n; bestCost = cost; }   // při shodě vyhrává dřívější uzel
            }
            return best;
        }

        public static int AffordableInCategory(int c)
        {
            int count = 0;
            foreach (var n in ArcanumData.Nodes)
                if (n.category == c && ArcanumProgress.IsAvailable(n) && CanAfford(n)) count++;
            return count;
        }

        private static int TotalCost(ArcanumNode n)
        {
            int sum = 0;
            foreach (var c in n.cost) sum += c.amount;
            return sum;
        }

#if UNITY_EDITOR
        /// <summary>Jen editor/QA: přidá testovací suroviny (výchozí sada, nebo jen zadaná ID). Ve buildu neexistuje.</summary>
        public static void EditorGiveTestMaterials(int amount, params string[] onlyIds)
        {
            if (InventoryData.Instance == null) return;
            var ids = onlyIds != null && onlyIds.Length > 0 ? onlyIds
                : new[] { "everlost:stone", "everlost:stick", "everlost:log", "everlost:grass", "everlost:berries", "everlost:coal" };
            foreach (var id in ids)
            {
                var it = Item(id);
                if (it != null) InventoryData.Instance.AddItem(it, amount);
            }
            InventoryData.Instance.NotifyInventoryChanged();
        }
#endif
    }
}
