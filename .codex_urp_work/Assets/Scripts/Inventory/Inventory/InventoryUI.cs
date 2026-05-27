using Orivilon.Inventory.Hotbar;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;

namespace Orivilon.Inventory.Inventory
{
    /// <summary>
    /// Statická pomocná třída pro hromadné obnovení UI inventáře.
    /// Metody jsou označeny [Obsolete] kvůli použití FindObjectsOfType,
    /// které je výkonnostně náročné – vhodné pro budoucí refaktoring na cached reference.
    /// </summary>
    public static class InventoryUI
    {
        /// <summary>
        /// Obnoví zobrazení všech InventorySlot komponent ve scéně (včetně neaktivních).
        /// Také aktualizuje aktivní slot hotbaru pro případ změny drženého itemu.
        /// Volá se po každé změně inventáře, craftingu nebo pickup operaci.
        /// </summary>
        [System.Obsolete]
        public static void RefreshAll()
        {
            var slots = Object.FindObjectsOfType<InventorySlot>(true);

            foreach (var slot in slots)
            {
                slot.Refresh();
            }

            HotbarController.Instance?.RefreshActiveSlot();
        }

        /// <summary>
        /// Najde a vrátí InventorySlot s daným indexem slotIndex.
        /// Prohledává všechny aktivní InventorySlot objekty ve scéně.
        /// Vrátí null pokud slot s daným indexem neexistuje.
        /// </summary>
        /// <param name="index">Index hledaného slotu v InventoryData.Slots.</param>
        /// <returns>InventorySlot UI komponenta nebo null.</returns>
        [System.Obsolete]
        public static InventorySlot GetSlotByIndex(int index)
        {
            foreach (var slot in Object.FindObjectsOfType<InventorySlot>())
            {
                if (slot.slotIndex == index)
                    return slot;
            }
            return null;
        }
    }
}