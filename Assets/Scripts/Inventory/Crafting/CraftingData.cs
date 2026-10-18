using Orivilon.Inventory.Inventory;
using UnityEngine;

namespace Orivilon.Inventory.Crafting
{
    /// <summary>
    /// Singleton datový model craftovacího gridu (3×3 = 9 slotů) a výsledného itemu.
    /// Odděluje data od UI – CraftingController čte data odsud, CraftingSlot je zobrazuje.
    /// Inicializuje prázdné sloty v Awake, aby ostatní skripty mohly přistupovat bez null chyb.
    /// </summary>
    public class CraftingData : MonoBehaviour
    {
        /// <summary>Globální instance singletonu.</summary>
        public static CraftingData Instance;

        /// <summary>
        /// Pole 9 slotů craftovacího gridu (3×3).
        /// Index 0 = levý horní roh, index 8 = pravý dolní roh (row-major pořadí).
        /// </summary>
        [Header("Crafting Grid (3x3)")]
        public InventorySlotData[] gridSlots = new InventorySlotData[9];

        /// <summary>Item, který vznikne craftingem podle aktuálního obsahu gridu. Null = žádný recept nesedí.</summary>
        [Header("Crafting Result")]
        public InventoryItemData resultItem;

        /// <summary>
        /// Singleton inicializace. Zajistí, že každý slot má platnou instanci (nikdy null).
        /// </summary>
        private void Awake()
        {
            Instance = this;

            for (int i = 0; i < gridSlots.Length; i++)
            {
                if (gridSlots[i] == null)
                    gridSlots[i] = new InventorySlotData();
            }

            CraftingController.Instance?.RefreshAllSlots();
        }

        /// <summary>
        /// Přidá item do gridu (nejdřív ke kompatibilním stackům, pak do prázdných slotů) – VŠE NEBO NIC.
        /// </summary>
        /// <param name="item">Item k přidání.</param>
        /// <param name="amount">Počet kusů.</param>
        /// <returns>True, pokud se vešlo celé množství; při false se grid nezměnil.</returns>
        public bool TryAddToGrid(InventoryItemData item, int amount)
        {
            if (item == null || amount <= 0) return false;
            if (InventoryData.SpaceFor(gridSlots, 0, gridSlots.Length - 1, item) < amount) return false;
            InventoryData.InsertPartial(gridSlots, 0, gridSlots.Length - 1, item, amount);
            return true;
        }

        /// <summary>
        /// Vrátí itemy z craftovacího gridu zpět do inventáře hráče (priorita AddItemSmart).
        /// Co se do plného inventáře nevejde, ZŮSTANE v gridu – dřív se slot vymazal i při neúspěchu.
        /// Volá se při zavření craftingu nebo přepnutí záložky.
        /// </summary>
        public void ReturnAllToInventory()
        {
            var inv = InventoryData.Instance;
            if (inv == null) return;
            for (int i = 0; i < gridSlots.Length; i++)
            {
                var slot = gridSlots[i];
                if (slot.IsEmpty) continue;

                int moved = InventoryData.InsertSmart(inv.Slots, slot.item, slot.amount);
                slot.amount -= moved;
                if (slot.amount <= 0) slot.Clear();
            }

            CraftingController.Instance?.Recalculate();
            inv.NotifyInventoryChanged();
        }
    }
}