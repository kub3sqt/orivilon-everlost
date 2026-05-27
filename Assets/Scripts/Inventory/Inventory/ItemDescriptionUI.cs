using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Orivilon.Inventory.Inventory
{
    /// <summary>
    /// Singleton zobrazující ikonu, název a popis itemu při interakci s inventářem.
    /// Rozlišuje dva zdroje zobrazení:
    /// 1. Hover nad slotem (ShowFromSlot/ClearFromSlot) – přechodné, lze přepsat.
    /// 2. Kurzor drží item (LockToCursor/UnlockFromCursor) – zamknuté, hover ho nepřepíše.
    /// </summary>
    public class ItemDescriptionUI : MonoBehaviour
    {
        /// <summary>Globální instance singletonu.</summary>
        public static ItemDescriptionUI Instance;

        /// <summary>Ikona itemu zobrazená v panelu popisu.</summary>
        [Header("UI")]
        public Image iconImage;

        /// <summary>Název itemu.</summary>
        public TMP_Text nameText;

        /// <summary>Textový popis itemu.</summary>
        public TMP_Text descriptionText;

        /// <summary>
        /// True, pokud je popis zamknutý na item držený kurzorem.
        /// V tomto stavu hover nad slotem popis nepřepíše.
        /// </summary>
        private bool lockedByCursor;

        /// <summary>
        /// Singleton inicializace a vymazání panelu do výchozího stavu.
        /// </summary>
        private void Awake()
        {
            Instance = this;
            Clear();
        }

        /// <summary>
        /// Zobrazí popis itemu při najetí myší nad slot.
        /// Ignorováno pokud je popis zamknutý na kurzorový item.
        /// </summary>
        /// <param name="item">Item, jehož popis se zobrazí.</param>
        public void ShowFromSlot(InventoryItemData item)
        {
            if (lockedByCursor || item == null) return;
            Apply(item);
        }

        /// <summary>
        /// Vymaže popis při odjetí myší ze slotu.
        /// Ignorováno pokud je popis zamknutý na kurzorový item.
        /// </summary>
        public void ClearFromSlot()
        {
            if (lockedByCursor) return;
            Clear();
        }

        /// <summary>
        /// Zamkne popis na item držený kurzorem.
        /// Dokud není odemčeno (UnlockFromCursor), hover nad slotem popis nepřepíše.
        /// </summary>
        /// <param name="item">Item, jehož popis se zamkne.</param>
        public void LockToCursor(InventoryItemData item)
        {
            if (item == null) return;
            lockedByCursor = true;
            Apply(item);
        }

        /// <summary>
        /// Odemkne popis od kurzorového itemu.
        /// Po odemčení může hover nad slotem popis opět přepsat.
        /// </summary>
        public void UnlockFromCursor()
        {
            lockedByCursor = false;
        }

        /// <summary>
        /// Aplikuje data itemu na všechny UI prvky panelu (ikona, název, popis).
        /// </summary>
        /// <param name="item">Item, jehož data se zobrazí.</param>
        private void Apply(InventoryItemData item)
        {
            iconImage.sprite = item.icon;
            iconImage.enabled = true;
            nameText.text = item.itemName;
            descriptionText.text = item.description;
        }

        /// <summary>
        /// Obnoví panel do výchozího prázdného stavu s výzvou k najetí na item.
        /// </summary>
        private void Clear()
        {
            iconImage.enabled = false;
            nameText.text = "Item Description";
            descriptionText.text = "Hover over item to see its description";
        }
    }
}