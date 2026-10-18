using Orivilon.Inventory.Inventory;

namespace Orivilon.Inventory.Crafting
{
    /// <summary>
    /// Zachováno kvůli kompatibilitě: craftovací grid sdílí jediný stavový automat gest
    /// <see cref="InventoryDragDistributor"/>. Dřív měl grid vlastní kopii logiky se samostatným
    /// stavem, takže CraftingSlot kontroloval příznak z jiného distributoru a klik po dragu se zpracoval dvakrát.
    /// </summary>
    public static class CraftingDragDistributor
    {
        public static bool IsDragging => InventoryDragDistributor.IsDragging;
        public static bool IsRightDrag => InventoryDragDistributor.IsRightDrag;
        public static bool DragConsumedClick => InventoryDragDistributor.DragConsumedClick;

        public static void BeginDrag(CraftingSlot startSlot, bool isRightClick)
        {
            if (startSlot != null) InventoryDragDistributor.PointerDown(SlotRef.Grid(startSlot.index), isRightClick ? 1 : 0);
        }

        public static void HoverSlot(CraftingSlot slot)
        {
            if (slot != null) InventoryDragDistributor.PointerEnter(SlotRef.Grid(slot.index));
        }

        public static void EndDrag() => InventoryDragDistributor.EndDrag();
    }
}
