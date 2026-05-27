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
        }

        /// <summary>
        /// Pokusí se přidat item do prvního volného slotu craftovacího gridu.
        /// Používá se při Shift+kliknutí v inventáři, když je crafting otevřen.
        /// </summary>
        /// <param name="item">Item k přidání.</param>
        /// <param name="amount">Počet kusů.</param>
        /// <returns>True, pokud byl item úspěšně přidán do volného slotu.</returns>
        public bool TryAddToGrid(InventoryItemData item, int amount)
        {
            for (int i = 0; i < gridSlots.Length; i++)
            {
                var slot = gridSlots[i];
                if (!slot.IsEmpty) continue;

                slot.item = item;
                slot.amount = amount;
                return true;
            }

            return false;
        }

        /// <summary>
        /// Vrátí všechny itemy z craftovacího gridu zpět do inventáře hráče.
        /// Prioritně plní inventář (sloty 9+), pak hotbar (sloty 0–8).
        /// Volá se při zavření craftingu nebo přepnutí záložky.
        /// </summary>
        public void ReturnAllToInventory()
        {
            for (int i = 0; i < gridSlots.Length; i++)
            {
                var slot = gridSlots[i];
                if (slot.IsEmpty) continue;

                InventoryData.Instance.AddItemSmart(slot.item, slot.amount);
                slot.Clear();
            }

            resultItem = null;
        }
    }
}