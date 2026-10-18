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
    /// Zobrazuje CraftingData.gridSlots[index]; obnovuje se na InventoryData.OnInventoryChanged
    /// (přihlášení v OnEnable/OnDisable). Vstup myši předává do InventoryDragDistributor –
    /// stejná gesta jako v inventáři (klik, Shift+klik, dvojklik, split, Mouse Tweaks, kolečko).
    /// </summary>
    public class CraftingSlot : MonoBehaviour,
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

        private InventoryData subscribed;

        private SlotRef Ref => SlotRef.Grid(index);

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

        /// <summary>Při aktivaci se přihlásí na změny a obnoví zobrazení slotu.</summary>
        private void OnEnable()
        {
            Subscribe();
            InventorySlotRegistry.Register(Ref, transform as RectTransform);
            Refresh();
        }

        private void Start()
        {
            Subscribe();
            Refresh();
        }

        private void OnDisable()
        {
            if (subscribed != null) subscribed.OnInventoryChanged -= OnChanged;
            subscribed = null;
            InventorySlotRegistry.Unregister(Ref, transform as RectTransform);
        }

        private void Subscribe()
        {
            var inv = InventoryData.Instance;
            if (inv == null || inv == subscribed) return;
            if (subscribed != null) subscribed.OnInventoryChanged -= OnChanged;
            inv.OnInventoryChanged += OnChanged;
            subscribed = inv;
        }

        private void OnChanged() => RefreshVisual();

        /// <summary>
        /// Aktualizuje ikonu a počet kusů podle dat v CraftingData.gridSlots[index].
        /// Také obnoví aktivní slot hotbaru (pro případ, že se craftuje item v ruce).
        /// </summary>
        public void Refresh()
        {
            RefreshVisual();
            HotbarController.Instance?.RefreshActiveSlot();
        }

        private void RefreshVisual()
        {
            if (CraftingData.Instance == null || iconImage == null || stackAmountText == null)
                return;
            if (index < 0 || index >= CraftingData.Instance.gridSlots.Length) return;

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
        }

        private static int Button(PointerEventData e) =>
            e.button == PointerEventData.InputButton.Left ? 0 : e.button == PointerEventData.InputButton.Right ? 1 : -1;

        public void OnPointerDown(PointerEventData eventData) => InventoryDragDistributor.PointerDown(Ref, Button(eventData));

        public void OnPointerUp(PointerEventData eventData) => InventoryDragDistributor.PointerUp(Button(eventData));

        /// <summary>Najetí: pokračování gesta a popis itemu.</summary>
        public void OnPointerEnter(PointerEventData eventData)
        {
            InventoryDragDistributor.PointerEnter(Ref);
            if (!InventoryActions.Cursor.IsEmpty) return;

            var slot = CraftingData.Instance != null ? CraftingData.Instance.gridSlots[index] : null;
            if (slot != null && !slot.IsEmpty)
                ItemDescriptionUI.Instance?.ShowFromSlot(slot.item);
        }

        /// <summary>Při odjetí myší skryje popis itemu, pokud kurzor nic nedrží.</summary>
        public void OnPointerExit(PointerEventData eventData)
        {
            InventoryDragDistributor.PointerExit(Ref);
            if (!InventoryActions.Cursor.IsEmpty) return;
            ItemDescriptionUI.Instance?.ClearFromSlot();
        }
    }
}
