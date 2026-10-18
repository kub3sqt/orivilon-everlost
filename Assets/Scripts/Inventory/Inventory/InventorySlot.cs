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
    /// Zobrazuje data InventoryData.Slots[slotIndex] a obnovuje se na událost InventoryData.OnInventoryChanged
    /// (přihlášení v OnEnable, odhlášení v OnDisable – opakované otevírání UI nepřidává posluchače).
    /// Vstup myši jen předává do <see cref="InventoryDragDistributor"/>, který řídí všechna gesta
    /// (klik, Shift+klik, dvojklik, split, Mouse Tweaks tažení, kolečko, číselné klávesy).
    /// </summary>
    public class InventorySlot : MonoBehaviour,
        IPointerEnterHandler,
        IPointerExitHandler,
        IPointerDownHandler,
        IPointerUpHandler
    {
        /// <summary>Index tohoto slotu v poli InventoryData.Slots (hotbar 0–7, inventář 8+).</summary>
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

        /// <summary>Událost vyvolaná po změně obsahu slotu (zachováno kvůli kompatibilitě).</summary>
#pragma warning disable 67
        public event System.Action OnInventoryChanged;
#pragma warning restore 67

        /// <summary>Inventář, na jehož událost je slot právě přihlášen (pro korektní odhlášení).</summary>
        private InventoryData subscribed;

        private SlotRef Ref => SlotRef.Player(slotIndex);

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

        /// <summary>Při aktivaci se přihlásí na změny inventáře a obnoví zobrazení.</summary>
        private void OnEnable()
        {
            Subscribe();
            InventorySlotRegistry.Register(Ref, transform as RectTransform);
            Refresh();
        }

        /// <summary>InventoryData mohl při OnEnable ještě chybět (pořadí Awake) – dohnat přihlášení.</summary>
        private void Start()
        {
            Subscribe();
            Refresh();
        }

        private void OnDisable()
        {
            if (subscribed != null) subscribed.OnInventoryChanged -= Refresh;
            subscribed = null;
            InventorySlotRegistry.Unregister(Ref, transform as RectTransform);
        }

        private void Subscribe()
        {
            var inv = InventoryData.Instance;
            if (inv == null || inv == subscribed) return;
            if (subscribed != null) subscribed.OnInventoryChanged -= Refresh;
            inv.OnInventoryChanged += Refresh;
            subscribed = inv;
        }

        /// <summary>Aktivní UI slot s daným indexem (přes registr, bez FindObjectsOfType).</summary>
        public static InventorySlot FindActive(int index)
        {
            var t = InventorySlotRegistry.Find(SlotRef.Player(index));
            return t != null ? t.GetComponent<InventorySlot>() : null;
        }

        /// <summary>
        /// Aktualizuje ikonu a počet kusů podle dat v InventoryData.Slots[slotIndex].
        /// Pokud je index mimo rozsah nebo InventoryData neexistuje, slot se skryje.
        /// </summary>
        public void Refresh()
        {
            if (iconImage == null || stackAmountText == null) return;

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

        private static int Button(PointerEventData e) =>
            e.button == PointerEventData.InputButton.Left ? 0 : e.button == PointerEventData.InputButton.Right ? 1 : -1;

        /// <summary>Stisk: klik/Shift+klik/dvojklik nebo začátek gesta (rozhoduje InventoryDragDistributor).</summary>
        public void OnPointerDown(PointerEventData eventData) => InventoryDragDistributor.PointerDown(Ref, Button(eventData));

        /// <summary>Uvolnění: konec gesta (split se potvrdí, jediný slot = obyčejný klik).</summary>
        public void OnPointerUp(PointerEventData eventData) => InventoryDragDistributor.PointerUp(Button(eventData));

        /// <summary>Najetí: pokračování gesta tažením a popis itemu.</summary>
        public void OnPointerEnter(PointerEventData eventData)
        {
            InventoryDragDistributor.PointerEnter(Ref);

            if (!InventoryActions.Cursor.IsEmpty)
                return;

            var inv = InventoryData.Instance;
            if (inv == null || slotIndex < 0 || slotIndex >= inv.Slots.Length) return;
            var slot = inv.Slots[slotIndex];
            if (!slot.IsEmpty)
                ItemDescriptionUI.Instance?.ShowFromSlot(slot.item);
        }

        /// <summary>Odjetí: skryje popis itemu, pokud kurzor nic nedrží.</summary>
        public void OnPointerExit(PointerEventData eventData)
        {
            InventoryDragDistributor.PointerExit(Ref);

            if (!InventoryActions.Cursor.IsEmpty)
                return;

            ItemDescriptionUI.Instance?.ClearFromSlot();
        }
    }
}
