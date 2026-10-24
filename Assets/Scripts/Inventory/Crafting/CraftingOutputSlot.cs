using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using TMPro;
using Orivilon.Inventory.Inventory;

namespace Orivilon.Inventory.Crafting
{
    /// <summary>
    /// UI slot zobrazující výsledek craftingu.
    /// Stisk LMB/RMB přesune výsledek na kurzor a spotřebuje ingredience (přes InventoryDragDistributor).
    /// Shift+LMB (Craft All) craftuje, dokud recept sedí a výsledek se vejde do inventáře.
    /// Najetí myší zobrazí popis výsledného itemu.
    /// </summary>
    public class CraftingOutputSlot : MonoBehaviour,
        IPointerDownHandler,
        IPointerUpHandler,
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
            // OnEnable může přijít DŘÍV, než se stihnou postavit singletony craftingu –
            // typicky když se scéna spouští napřímo, ne přes menu. Bez téhle stráže to
            // padalo na NullReferenceException hned při startu a kazilo to konzoli.
            // Slot v takové chvíli prostě nemá co zobrazit; naplní se při dalším Refreshi.
            if (CraftingData.Instance == null || CraftingController.Instance == null) return;
            if (iconImage == null) return;

            var item = CraftingData.Instance.resultItem;
            var amount = CraftingController.Instance.ResultAmount;

            if (item != null)
            {
                iconImage.sprite = item.icon;
                iconImage.enabled = true;
                if (stackAmountText != null) stackAmountText.text = amount > 1 ? amount.ToString() : "";
            }
            else
            {
                iconImage.enabled = false;
                if (stackAmountText != null) stackAmountText.text = "";
            }
        }

        private static int Button(PointerEventData e) =>
            e.button == PointerEventData.InputButton.Left ? 0 : e.button == PointerEventData.InputButton.Right ? 1 : -1;

        /// <summary>
        /// Stisk na výstupu: LMB/RMB vezme výsledek na kurzor (prázdný nebo kompatibilní, celý se musí vejít),
        /// Shift+LMB = Craft All do inventáře. Výstup není skladovací slot – nic se do něj nepokládá.
        /// Logiku i spotřebu surovin řeší InventoryActions + CraftingController.TryCraftOnce.
        /// </summary>
        public void OnPointerDown(PointerEventData eventData)
        {
            InventoryDragDistributor.PointerDown(SlotRef.Output, Button(eventData));
            ItemDescriptionUI.Instance?.ClearFromSlot();
        }

        public void OnPointerUp(PointerEventData eventData) => InventoryDragDistributor.PointerUp(Button(eventData));

        /// <summary>
        /// Při najetí myší zobrazí popis výsledného itemu, pokud kurzor nic nedrží.
        /// </summary>
        public void OnPointerEnter(PointerEventData eventData)
        {
            InventoryDragDistributor.PointerEnter(SlotRef.Output);
            ItemTooltip.Ensure(this, stackAmountText);
            if (!InventoryActions.Cursor.IsEmpty) return;

            var item = CraftingData.Instance.resultItem;
            if (item != null)
                ItemDescriptionUI.Instance?.ShowFromSlot(item);
        }

        /// <summary>
        /// Při odjetí myší skryje popis itemu, pokud kurzor nic nedrží.
        /// </summary>
        public void OnPointerExit(PointerEventData eventData)
        {
            InventoryDragDistributor.PointerExit(SlotRef.Output);
            if (!InventoryActions.Cursor.IsEmpty) return;
            ItemDescriptionUI.Instance?.ClearFromSlot();
        }
    }
}