using Orivilon.Inventory.Crafting;
using System;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Orivilon.Inventory.Inventory
{
    /// <summary>
    /// UI element jednoho slotu inventáře nebo hotbaru.
    /// Zpracovává veškeré mouse eventy: kliknutí, najetí, drag.
    /// Levé kliknutí: vzít/položit stack z/na kurzor.
    /// Shift+LMB: quick-move (hotbar↔inventář nebo do craftovacího gridu).
    /// Double-click: sebrat všechny kusy stejného itemu.
    /// Levý/pravý drag: distribuce stacku přes více slotů.
    /// Blok pravého kliknutí (split stack) je záměrně zakomentován.
    /// </summary>
    public class InventorySlot : MonoBehaviour,
        IPointerClickHandler,
        IPointerEnterHandler,
        IPointerExitHandler,
        IPointerDownHandler,
        IPointerUpHandler
    {
        /// <summary>Index tohoto slotu v poli InventoryData.Slots (0–35).</summary>
        [Header("Slot Index")]
        public int slotIndex;

        /// <summary>Image komponenta zobrazující ikonu itemu v slotu.</summary>
        [Header("UI")]
        public Image iconImage;

        /// <summary>Text zobrazující počet kusů (skrytý pokud = 1).</summary>
        public TMP_Text stackAmountText;

        /// <summary>Průhledná ikona pro budoucí ghost-drag funkcionalitu (momentálně nepoužívána).</summary>
        [SerializeField] private Image ghostIcon;

        /// <summary>Text ghost-drag počtu (momentálně nepoužíván).</summary>
        [SerializeField] private TMP_Text ghostAmount;

        /// <summary>Čas posledního kliknutí pro detekci double-clicku.</summary>
        private float lastClickTime;

        /// <summary>Maximální interval (v sekundách) mezi dvěma kliknutími pro double-click.</summary>
        private const float DOUBLE_CLICK_TIME = 0.25f;

        /// <summary>Událost vyvolaná po změně obsahu slotu (přihlašuje se na ni GameManager).</summary>
        public event System.Action OnInventoryChanged;

        /// <summary>
        /// Automaticky dohledá iconImage a stackAmountText pokud nejsou přiřazeny v Inspektoru.
        /// </summary>
        private void Awake()
        {
            if (iconImage == null)
            {
                foreach (var img in GetComponentsInChildren<Image>(true))
                {
                    if (!img.raycastTarget)
                    {
                        iconImage = img;
                        break;
                    }
                }
            }

            if (stackAmountText == null)
            {
                stackAmountText = GetComponentInChildren<TMP_Text>(true);
            }
        }

        /// <summary>Při aktivaci objektu obnoví zobrazení slotu.</summary>
        private void OnEnable()
        {
            Refresh();
        }

        /// <summary>
        /// Aktualizuje ikonu a počet kusů podle dat v InventoryData.Slots[slotIndex].
        /// Pokud je index mimo rozsah nebo InventoryData neexistuje, slot se skryje.
        /// </summary>
        public void Refresh()
        {
            var inventory = InventoryData.Instance;
            if (inventory == null || slotIndex < 0 || slotIndex >= inventory.Slots.Length)
            {
                iconImage.enabled = false;
                stackAmountText.text = "";
                return;
            }

            var slot = inventory.Slots[slotIndex];

            if (!slot.IsEmpty)
            {
                iconImage.sprite = slot.item.icon;
                iconImage.enabled = true;

                stackAmountText.text = slot.amount > 1 ? slot.amount.ToString() : "";
            }
            else
            {
                iconImage.enabled = false;
                stackAmountText.text = "";
            }
        }

        /// <summary>
        /// Zpracovává kliknutí na slot inventáře.
        /// Shift+LMB: přesune stack do craftovacího gridu (pokud je otevřen) nebo opačného rozsahu (hotbar↔inventář).
        /// LMB: vzít stack na kurzor / položit kurzor do slotu.
        /// Double-click LMB: sebrat všechny kusy stejného itemu.
        /// Blok pravého kliknutí je záměrně zakomentován (split stack).
        /// </summary>
        public void OnPointerClick(PointerEventData eventData)
        {
            if (InventoryDragDistributor.DragConsumedClick)
                return;

            if (eventData.button == PointerEventData.InputButton.Right &&
                InventoryDragDistributor.IsRightDrag)
            {
                return;
            }

            var cursor = InventoryCursor.Instance;
            var inventory = InventoryData.Instance;

            if (cursor == null || inventory == null)
                return;

            var slot = inventory.Slots[slotIndex];

            bool shift = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);

            if (shift && eventData.button == PointerEventData.InputButton.Left)
            {
                if (slot.IsEmpty) return;

                int amount = slot.amount;
                var item = slot.item;

                if (InventoryUIController.Instance != null &&
                    InventoryUIController.Instance.IsCraftingOpen)
                {
                    bool moved = CraftingData.Instance.TryAddToGrid(item, amount);
                    if (!moved) return;

                    slot.Clear();
                    InventoryUI.RefreshAll();
                    OnInventoryChanged?.Invoke();
                    CraftingController.Instance.RefreshAllSlots();
                    CraftingController.Instance.Recalculate();
                    return;
                }

                bool success;

                if (slotIndex <= 8)
                    success = inventory.AddItemRange(9, inventory.Slots.Length - 1, item, amount);
                else
                    success = inventory.AddItemRange(0, 8, item, amount);

                if (!success) return;

                slot.Clear();
                InventoryUI.RefreshAll();
                OnInventoryChanged?.Invoke();
                return;
            }

            if (eventData.button != PointerEventData.InputButton.Left)
                return;

            if (cursor.HeldSlot == null)
            {
                if (slot.IsEmpty)
                    return;

                cursor.TakeFromSlot(slotIndex);
                ItemDescriptionUI.Instance?.LockToCursor(slot.item);
            }
            else
            {
                cursor.PlaceIntoSlot(slotIndex);
                ItemDescriptionUI.Instance?.UnlockFromCursor();
            }

            if (eventData.button == PointerEventData.InputButton.Left)
            {
                if (Time.time - lastClickTime <= DOUBLE_CLICK_TIME)
                {
                    if (!slot.IsEmpty)
                    {
                        InventoryCursor.Instance.CollectAllOfItem(slot.item);
                        InventoryUI.RefreshAll();
                        OnInventoryChanged?.Invoke();
                        CraftingController.Instance?.RefreshAllSlots();
                        CraftingController.Instance?.Recalculate();
                    }
                    return;
                }

                lastClickTime = Time.time;
            }

            Refresh();
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
                    InventoryDragDistributor.HoverSlot(this);
                }
                return;
            }

            var slot = InventoryData.Instance.Slots[slotIndex];
            if (!slot.IsEmpty)
                ItemDescriptionUI.Instance?.ShowFromSlot(slot.item);
        }

        /// <summary>
        /// Při odjetí myší skryje popis itemu, pokud kurzor nic nedrží.
        /// </summary>
        public void OnPointerExit(PointerEventData eventData)
        {
            if (InventoryCursor.Instance.HeldSlot != null)
                return;

            ItemDescriptionUI.Instance?.ClearFromSlot();
        }

        /// <summary>
        /// Při stisknutí tlačítka myší zahájí drag distribuci (pokud kurzor drží item).
        /// Levé = rovnoměrné dělení, pravé = 1 kus/slot.
        /// </summary>
        public void OnPointerDown(PointerEventData eventData)
        {
            if (InventoryCursor.Instance.HeldSlot == null)
                return;

            if (eventData.button == PointerEventData.InputButton.Left)
                InventoryDragDistributor.BeginDrag(this, false);

            if (eventData.button == PointerEventData.InputButton.Right)
                InventoryDragDistributor.BeginDrag(this, true);
        }

        /// <summary>
        /// Při uvolnění tlačítka myší ukončí drag distribuci.
        /// </summary>
        public void OnPointerUp(PointerEventData eventData)
        {
            if (eventData.button == PointerEventData.InputButton.Left || eventData.button == PointerEventData.InputButton.Right)
            {
                InventoryDragDistributor.EndDrag();
            }
        }
    }
}