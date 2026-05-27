using Orivilon.Inventory.Inventory;
using System.Collections.Generic;
using UnityEngine;

namespace Orivilon.Inventory.Crafting
{
    /// <summary>
    /// Statická třída řídící drag-distribute operace přes craftovací sloty.
    /// Levý drag rovnoměrně rozdělí stack kurzoru mezi označené sloty.
    /// Pravý drag umístí 1 kus do každého najetého slotu (každý slot jen jednou).
    /// Drag se aktivuje až od druhého najetého slotu – první kliknutí se nezapočítává jako drag.
    /// </summary>
    public static class CraftingDragDistributor
    {
        /// <summary>Množina slotů, přes které hráč přejel myší během dragu.</summary>
        private static HashSet<CraftingSlot> hoveredSlots = new();

        /// <summary>Počet kusů na kurzoru při zahájení dragu.</summary>
        private static int startAmount;

        /// <summary>Příznak, zda je drag aktuálně aktivní (alespoň 2 sloty).</summary>
        private static bool dragActive;

        /// <summary>Příznak, zda jde o pravý drag (1 kus/slot) nebo levý (rovnoměrné dělení).</summary>
        private static bool rightDrag;

        /// <summary>Množina slotů, do kterých byl při pravém dragu již vložen 1 kus.</summary>
        private static HashSet<CraftingSlot> appliedRightSlots = new();

        /// <summary>True, pokud je drag aktivní.</summary>
        public static bool IsDragging => dragActive;

        /// <summary>True, pokud je aktivní pravý drag.</summary>
        public static bool IsRightDrag => dragActive && rightDrag;

        /// <summary>
        /// True, pokud drag pohltil kliknutí a nesmí se zpracovat jako normální click.
        /// CraftingSlot.OnPointerClick() toto kontroluje před dalším zpracováním.
        /// </summary>
        public static bool DragConsumedClick { get; private set; }

        /// <summary>
        /// Zahájí drag operaci od daného slotu.
        /// Uloží počáteční množství na kurzoru a přidá první slot do množiny.
        /// Drag samotný se aktivuje až při najetí na druhý slot.
        /// </summary>
        /// <param name="startSlot">Slot, ze kterého drag začíná.</param>
        /// <param name="isRightClick">True = pravý drag (1 kus/slot), False = levý drag (rovnoměrné dělení).</param>
        public static void BeginDrag(CraftingSlot startSlot, bool isRightClick)
        {
            DragConsumedClick = false;
            hoveredSlots.Clear();
            dragActive = false;
            rightDrag = isRightClick;

            var cursor = InventoryCursor.Instance;
            if (cursor == null || cursor.HeldSlot == null)
                return;

            startAmount = cursor.HeldSlot.amount;

            if (startSlot != null)
            {
                hoveredSlots.Add(startSlot);

                if (rightDrag)
                    ApplyRightDrag(startSlot);
            }
        }

        /// <summary>
        /// Zaregistruje najetí myší na slot během dragu.
        /// Od druhého slotu se drag aktivuje a začne distribuce.
        /// Každý slot je zaregistrován nejvýše jednou.
        /// </summary>
        /// <param name="slot">Slot, na který hráč najel myší.</param>
        public static void HoverSlot(CraftingSlot slot)
        {
            DragConsumedClick = true;
            if (slot == null)
                return;

            if (!hoveredSlots.Add(slot))
                return;

            if (hoveredSlots.Count >= 2)
                dragActive = true;

            if (!dragActive)
                return;

            if (rightDrag)
                ApplyRightDrag(slot);
            else
                Redistribute();
        }

        /// <summary>
        /// Ukončí drag operaci. Vyčistí interní stav, obnoví kurzor
        /// a spustí přepočet craftovacího receptu.
        /// </summary>
        public static void EndDrag()
        {
            DragConsumedClick = false;
            hoveredSlots.Clear();
            appliedRightSlots.Clear();
            dragActive = false;
            rightDrag = false;

            var cursor = InventoryCursor.Instance;
            if (cursor == null)
                return;

            if (cursor.HeldSlot == null || cursor.HeldSlot.amount <= 0)
                cursor.ClearHeldSlot();
            else
                cursor.ForceRefresh();

            CraftingController.Instance.Recalculate();
        }

        /// <summary>
        /// Rovnoměrně rozdělí stack kurzoru mezi všechny označené sloty (levý drag).
        /// Počítá perSlot = startAmount / počet slotů. Zbytek zůstane na kurzoru.
        /// Přeskočí sloty, které obsahují jiný item než kurzor.
        /// </summary>
        private static void Redistribute()
        {
            var cursor = InventoryCursor.Instance;
            if (cursor == null || cursor.HeldSlot == null)
                return;

            int count = hoveredSlots.Count;
            if (count == 0)
                return;

            int perSlot = startAmount / count;
            if (perSlot == 0)
                return;

            int used = 0;

            foreach (var slotUI in hoveredSlots)
            {
                var slot = CraftingData.Instance.gridSlots[slotUI.index];

                if (!slot.IsEmpty && slot.item != cursor.HeldSlot.item)
                    continue;

                slot.item = cursor.HeldSlot.item;
                slot.amount = perSlot;
                used += perSlot;
            }

            cursor.HeldSlot.amount = startAmount - used;

            if (cursor.HeldSlot.amount <= 0)
                cursor.ClearHeldSlot();

            CraftingController.Instance.RefreshAllSlots();
        }

        /// <summary>
        /// Vloží 1 kus z kurzoru do daného slotu (pravý drag).
        /// Každý slot je ošetřen nejvýše jednou (pomocí appliedRightSlots).
        /// Přeskočí sloty s jiným itemem nebo plné sloty.
        /// </summary>
        /// <param name="slotUI">Craftovací slot, do kterého se vkládá 1 kus.</param>
        private static void ApplyRightDrag(CraftingSlot slotUI)
        {
            if (appliedRightSlots.Contains(slotUI))
                return;

            var cursor = InventoryCursor.Instance;
            if (cursor == null || cursor.HeldSlot == null)
                return;

            if (cursor.HeldSlot.amount <= 0)
                return;

            var slot = CraftingData.Instance.gridSlots[slotUI.index];

            if (!slot.IsEmpty && slot.item != cursor.HeldSlot.item)
                return;

            if (slot.IsEmpty)
            {
                slot.item = cursor.HeldSlot.item;
                slot.amount = 1;
            }
            else
            {
                slot.amount += 1;
            }

            cursor.HeldSlot.amount -= 1;
            appliedRightSlots.Add(slotUI);

            if (cursor.HeldSlot.amount <= 0)
                cursor.ClearHeldSlot();

            CraftingController.Instance.RefreshAllSlots();
        }
    }
}