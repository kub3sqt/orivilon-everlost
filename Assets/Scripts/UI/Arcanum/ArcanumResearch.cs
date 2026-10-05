using System.Collections.Generic;
using Orivilon.Inventory.Inventory;
using UnityEngine;

namespace Orivilon.UI.Arcanum
{
    public enum ArcanumResult { Ok, AlreadyUnlocked, Locked, MissingMaterials, NoInventory }

    /// <summary>
    /// Pravidla výzkumu nad skutečným inventářem hráče. Suroviny jsou existující itemy
    /// (Stone, Berries, Log, Coal, Grass, Stick) a odečítají se stejně jako v BuildingTool.
    /// Výzkum zatím nepřidává crafting recepty – je to placeholder progrese.
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

        public static string MaterialId(ArcanumNode n) =>
            n.category < 0 ? null : ArcanumData.Categories[n.category].materialItemId;

        public static string MaterialName(ArcanumNode n)
        {
            var it = Item(MaterialId(n));
            return it != null ? it.itemName : "—";
        }

        public static int CountOwned(string itemId)
        {
            var inv = InventoryData.Instance;
            if (inv == null || string.IsNullOrEmpty(itemId)) return 0;
            int count = 0;
            foreach (var slot in inv.Slots)
                if (slot != null && !slot.IsEmpty && slot.item.id == itemId) count += slot.amount;
            return count;
        }

        public static ArcanumResult Check(ArcanumNode n)
        {
            if (ArcanumProgress.IsUnlocked(n.id)) return ArcanumResult.AlreadyUnlocked;
            if (!ArcanumProgress.IsAvailable(n)) return ArcanumResult.Locked;
            if (n.materialCost > 0)
            {
                if (InventoryData.Instance == null) return ArcanumResult.NoInventory;
                if (CountOwned(MaterialId(n)) < n.materialCost) return ArcanumResult.MissingMaterials;
            }
            return ArcanumResult.Ok;
        }

        /// <summary>
        /// Atomický výzkum: nejdřív kompletní validace, pak odečtení surovin a odemčení
        /// v jednom synchronním kroku. Opakované volání (dvojklik) selže na AlreadyUnlocked.
        /// </summary>
        public static ArcanumResult TryUnlock(ArcanumNode n)
        {
            var result = Check(n);
            if (result != ArcanumResult.Ok) return result;

            if (n.materialCost > 0)
            {
                string id = MaterialId(n);
                int left = n.materialCost;
                var slots = InventoryData.Instance.Slots;
                // Nejdřív hlavní inventář od konce, hotbar (0–7) až nakonec – hráč nepřijde o věci v ruce zbytečně.
                for (int i = slots.Length - 1; i >= 0 && left > 0; i--)
                {
                    var slot = slots[i];
                    if (slot == null || slot.IsEmpty || slot.item.id != id) continue;
                    int take = Mathf.Min(slot.amount, left);
                    slot.amount -= take;
                    left -= take;
                    if (slot.amount <= 0) slot.Clear();
                }
                InventoryData.Instance.NotifyInventoryChanged();
            }

            ArcanumProgress.MarkUnlocked(n.id);
            return ArcanumResult.Ok;
        }

        /// <summary>Nejlevnější dostupný výzkum v oboru (nebo null, když je obor hotový).</summary>
        public static ArcanumNode NextInCategory(int c)
        {
            ArcanumNode best = null;
            var nodes = ArcanumData.Nodes;
            int start = 1 + c * ArcanumData.NodesPerCategory;
            for (int i = start; i < start + ArcanumData.NodesPerCategory; i++)
            {
                var n = nodes[i];
                if (!ArcanumProgress.IsAvailable(n)) continue;
                if (best == null || n.materialCost < best.materialCost || (n.materialCost == best.materialCost && n.id < best.id))
                    best = n;
            }
            return best;
        }

        public static int AffordableInCategory(int c, int owned)
        {
            int count = 0;
            var nodes = ArcanumData.Nodes;
            int start = 1 + c * ArcanumData.NodesPerCategory;
            for (int i = start; i < start + ArcanumData.NodesPerCategory; i++)
                if (ArcanumProgress.IsAvailable(nodes[i]) && owned >= nodes[i].materialCost) count++;
            return count;
        }

#if UNITY_EDITOR
        /// <summary>Jen editor/QA: přidá testovací suroviny všech oborů. Ve buildu neexistuje.</summary>
        public static void EditorGiveTestMaterials(int amount)
        {
            if (InventoryData.Instance == null) return;
            foreach (var cat in ArcanumData.Categories)
            {
                var it = Item(cat.materialItemId);
                if (it != null) InventoryData.Instance.AddItem(it, amount);
            }
            InventoryData.Instance.NotifyInventoryChanged();
        }
#endif
    }
}
