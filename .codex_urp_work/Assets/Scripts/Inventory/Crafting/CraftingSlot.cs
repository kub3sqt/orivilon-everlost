using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using TMPro;
using Orivilon.Inventory.Inventory;
using Orivilon.Inventory.Hotbar;

namespace Orivilon.Inventory.Crafting
{
    /// <summary>
    /// UI element jednoho slotu v craftovacím gridu (3×3).
    /// Zpracovává kliknutí, najetí myší a drag-distribute operace.
    /// Levé kliknutí: vzít/položit celý stack.
    /// Shift+LMB: přesunout stack do inventáře.
    /// Double-click: sebrat všechny kusy stejného itemu.
    /// Pravý drag: vložit 1 kus do každého najetého slotu.
    /// Levý drag: rovnoměrně rozdělit stack přes najeté sloty.
    /// </summary>
    public class CraftingSlot : MonoBehaviour,
        IPointerClickHandler,
        IPointerEnterHandler,
        IPointerExitHandler,
        IPointerDownHandler,
        IPointerUpHandler
    {
        /// <summary>Index tohoto slotu v poli CraftingData.gridSlots (0–8, row-major).</summary>
        public int index;

        /// <summary>Ikona itemu v tomto slotu.</summary>
        public Image iconImage;

        /// <summary>Text zobrazující počet kusů (skrytý pokud = 1).</summary>
        public TMP_Text stackAmountText;

        /// <summary>Čas posledního kliknutí pro detekci double-clicku.</summary>
        private float lastClickTime;

        /// <summary>Maximální interval (v sekundách) mezi dvěma kliknutími pro double-click.</summary>
        private const float DOUBLE_CLICK_TIME = 0.25f;

        /// <summary>
        /// Automaticky dohledá iconImage a stackAmountText pokud nejsou přiřazeny v Inspektoru.
        /// </summary>
        private void Awake()
        {
            if (!iconImage)
                foreach (var img in GetComponentsInChildren<Image>(true))
                    if (!img.raycastTarget)
                        iconImage = img;

            if (!stackAmountText)
                stackAmountText = GetComponentInChildren<TMP_Text>(true);
        }

        /// <summary>Při aktivaci objektu obnoví zobrazení slotu.</summary>
        private void OnEnable() => Refresh();

        /// <summary>
        /// Aktualizuje ikonu a počet kusů podle dat v CraftingData.gridSlots[index].
        /// Také obnoví aktivní slot hotbaru (pro případ, že se craftuje item v ruce).
        /// </summary>
        public void Refresh()
        {
            var slot = CraftingData.Instance.gridSlots[index];

            if (slot != null && !slot.IsEmpty)
            {
                iconImage.sprite = slot.item.icon;
                iconImage.enabled = true;

                stackAmountText.text = slot.amount > 1
                    ? slot.amount.ToString()
                    : "";
            }
            else
            {
                iconImage.enabled = false;
                stackAmountText.text = "";
            }

            HotbarController.Instance?.RefreshActiveSlot();
        }

        /// <summary>
        /// Zpracovává kliknutí na slot.
        /// Pokud drag pohltil kliknutí, přeskočí zpracování.
        /// Shift+LMB: přesun celého stacku do inventáře.
        /// LMB: vzít celý stack na kurzor / položit kurzor do slotu / stackovat.
        /// Double-click: sebrat všechny kusy stejného itemu z inventáře i craftingu.
        /// Blok pravého tlačítka je záměrně zakomentován (deaktivovaná funkce).
        /// </summary>
        public void OnPointerClick(PointerEventData eventData)
        {
            if (InventoryDragDistributor.DragConsumedClick)
                return;

            if (eventData.button == PointerEventData.InputButton.Right &&
                CraftingDragDistributor.IsRightDrag)
            {
                return;
            }

            var cursor = InventoryCursor.Instance;
            var slot = CraftingData.Instance.gridSlots[index];

            if (cursor == null || slot == null)
                return;

            bool shift = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);

            if (shift && eventData.button == PointerEventData.InputButton.Left)
            {
                if (slot.IsEmpty) return;

                bool moved = InventoryData.Instance.AddItemSmart(slot.item, slot.amount);
                if (!moved) return;

                slot.Clear();
                InventoryUI.RefreshAll();
                Refresh();
                CraftingController.Instance.Recalculate();
                return;
            }

            if (eventData.button != PointerEventData.InputButton.Left)
                return;

            if (cursor.HeldSlot == null)
            {
                if (slot.IsEmpty) return;

                cursor.SetHeldSlot(new InventorySlotData
                {
                    item = slot.item,
                    amount = slot.amount
                });

                slot.Clear();
                Refresh();
                CraftingController.Instance.Recalculate();
                ItemDescriptionUI.Instance.LockToCursor(cursor.HeldSlot.item);
            }
            else
            {
                if (slot.IsEmpty)
                {
                    slot.item = cursor.HeldSlot.item;
                    slot.amount = cursor.HeldSlot.amount;

                    cursor.ClearHeldSlot();
                    ItemDescriptionUI.Instance.UnlockFromCursor();
                }
                else if (
                    slot.item == cursor.HeldSlot.item &&
                    slot.item.stackable
                )
                {
                    int free = slot.item.maxStack - slot.amount;
                    if (free <= 0) return;

                    int move = Mathf.Min(free, cursor.HeldSlot.amount);
                    slot.amount += move;
                    cursor.HeldSlot.amount -= move;

                    if (cursor.HeldSlot.amount <= 0)
                    {
                        cursor.ClearHeldSlot();
                        ItemDescriptionUI.Instance.UnlockFromCursor();
                    }
                }
                else
                {
                    return;
                }
                Refresh();
                CraftingController.Instance.Recalculate();
            }

            if (eventData.button == PointerEventData.InputButton.Left)
            {
                if (Time.time - lastClickTime <= DOUBLE_CLICK_TIME)
                {
                    if (!slot.IsEmpty)
                    {
                        InventoryCursor.Instance.CollectAllOfItem(slot.item);
                        InventoryUI.RefreshAll();
                        CraftingController.Instance.Recalculate();
                    }
                    return;
                }

                lastClickTime = Time.time;
            }
        }

        /// <summary>
        /// Při najetí myší zahájí drag (pokud kurzor drží item a je stisknuto tlačítko),
        /// nebo zobrazí popis itemu v slotu.
        /// </summary>
        public void OnPointerEnter(PointerEventData eventData)
        {
            if (InventoryCursor.Instance.HeldSlot != null)
            {
                if (Input.GetMouseButton(0) || Input.GetMouseButton(1))
                {
                    CraftingDragDistributor.HoverSlot(this);
                }
                return;
            }

            var slot = CraftingData.Instance.gridSlots[index];
            if (!slot.IsEmpty)
                ItemDescriptionUI.Instance.ShowFromSlot(slot.item);
        }

        /// <summary>
        /// Při odjetí myší skryje popis itemu, pokud kurzor nic nedrží.
        /// </summary>
        public void OnPointerExit(PointerEventData eventData)
        {
            if (InventoryCursor.Instance.HeldSlot != null) return;
            ItemDescriptionUI.Instance.ClearFromSlot();
        }

        /// <summary>
        /// Při stisknutí tlačítka myší zahájí drag distribuci (pokud kurzor drží item).
        /// Levé tlačítko = rovnoměrné dělení, pravé tlačítko = 1 kus/slot.
        /// </summary>
        public void OnPointerDown(PointerEventData eventData)
        {
            if (InventoryCursor.Instance.HeldSlot == null)
                return;

            if (eventData.button == PointerEventData.InputButton.Left)
                CraftingDragDistributor.BeginDrag(this, false);

            if (eventData.button == PointerEventData.InputButton.Right)
                CraftingDragDistributor.BeginDrag(this, true);
        }

        /// <summary>
        /// Při uvolnění tlačítka myší ukončí drag distribuci.
        /// </summary>
        public void OnPointerUp(PointerEventData eventData)
        {
            if (eventData.button == PointerEventData.InputButton.Left || eventData.button == PointerEventData.InputButton.Right)
            {
                CraftingDragDistributor.EndDrag();
            }
        }
    }
}