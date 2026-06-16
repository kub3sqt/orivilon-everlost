using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using TMPro;
using Orivilon.Inventory.Inventory;

namespace Orivilon.Inventory.Crafting
{
    /// <summary>
    /// UI slot zobrazující výsledek craftingu.
    /// Levé kliknutí přesune výsledek na kurzor a spotřebuje ingredience.
    /// Shift+levé kliknutí (Craft All) provede maximální možný počet craftů najednou
    /// a přesune vše do inventáře.
    /// Najetí myší zobrazí popis výsledného itemu.
    /// </summary>
    public class CraftingOutputSlot : MonoBehaviour,
        IPointerClickHandler,
        IPointerEnterHandler,
        IPointerExitHandler
    {
        /// <summary>Ikona výsledného itemu.</summary>
        public Image iconImage;

        /// <summary>Text zobrazující počet kusů výsledku (skrytý pokud = 1).</summary>
        public TMP_Text stackAmountText;

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

        /// <summary>Při aktivaci objektu obnoví zobrazení výsledku.</summary>
        private void OnEnable() => Refresh();

        /// <summary>
        /// Aktualizuje ikonu a počet kusů podle aktuálního výsledku craftingu.
        /// Pokud není žádný výsledek, ikona se skryje.
        /// </summary>
        public void Refresh()
        {
            var item = CraftingData.Instance.resultItem;
            var amount = CraftingController.Instance.ResultAmount;

            if (item != null)
            {
                iconImage.sprite = item.icon;
                iconImage.enabled = true;
                stackAmountText.text = amount > 1 ? amount.ToString() : "";
            }
            else
            {
                iconImage.enabled = false;
                stackAmountText.text = "";
            }
        }

        /// <summary>
        /// Zpracovává kliknutí na výstupní slot.
        /// Shift+LMB: Craft All – provede tolik craftů, kolik je surovin, přidá vše do inventáře.
        /// LMB bez Shift: přesune výsledek na kurzor (nebo stackuje na existující kurzor).
        /// </summary>
        public void OnPointerClick(PointerEventData eventData)
        {
            var cursor = InventoryCursor.Instance;
            if (cursor == null) return;

            var item = CraftingData.Instance.resultItem;
            if (item == null) return;

            bool shift = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);

            if (shift && eventData.button == PointerEventData.InputButton.Left)
            {
                int craftCount = CraftingController.Instance.GetMaxCraftCount();
                if (craftCount <= 0) return;

                int totalAmount = craftCount * CraftingController.Instance.ResultAmount;

                bool added = InventoryData.Instance.AddItemSmart(
                    CraftingData.Instance.resultItem,
                    totalAmount
                );

                if (!added) return;

                for (int i = 0; i < craftCount; i++)
                    CraftingController.Instance.ConsumeIngredients();

                CraftingController.Instance.Recalculate();
                InventoryUI.RefreshAll();
                ItemDescriptionUI.Instance?.ClearFromSlot();
                return;
            }

            if (eventData.button != PointerEventData.InputButton.Left)
                return;

            if (cursor.HeldSlot == null)
            {
                cursor.SetHeldSlot(new InventorySlotData
                {
                    item = item,
                    amount = CraftingController.Instance.ResultAmount
                });

                ItemDescriptionUI.Instance?.LockToCursor(item);
                CraftingController.Instance.ConsumeIngredients();
                CraftingController.Instance.Recalculate();
            }
            else if (cursor.TryAddToHeldSlot(item, CraftingController.Instance.ResultAmount))
            {
                CraftingController.Instance.ConsumeIngredients();
                CraftingController.Instance.Recalculate();
            }
        }

        /// <summary>
        /// Při najetí myší zobrazí popis výsledného itemu, pokud kurzor nic nedrží.
        /// </summary>
        public void OnPointerEnter(PointerEventData eventData)
        {
            if (InventoryCursor.Instance.HeldSlot != null) return;

            var item = CraftingData.Instance.resultItem;
            if (item != null)
                ItemDescriptionUI.Instance?.ShowFromSlot(item);
        }

        /// <summary>
        /// Při odjetí myší skryje popis itemu, pokud kurzor nic nedrží.
        /// </summary>
        public void OnPointerExit(PointerEventData eventData)
        {
            if (InventoryCursor.Instance.HeldSlot != null) return;
            ItemDescriptionUI.Instance?.ClearFromSlot();
        }
    }
}