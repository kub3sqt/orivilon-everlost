using Orivilon.Inventory.Crafting;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Orivilon.Inventory.Inventory
{
    /// <summary>
    /// Singleton spravující item držený kurzorem během drag-and-drop operací v inventáři.
    /// Ikona drženého itemu sleduje pohyb myši. Třída poskytuje metody pro vzětí,
    /// položení, rozdělení stacku, sbírání stejných itemů a refresh vizuálu kurzoru.
    /// </summary>
    public class InventoryCursor : MonoBehaviour
    {
        /// <summary>Globální instance singletonu.</summary>
        public static InventoryCursor Instance;

        /// <summary>Image komponenta zobrazující ikonu drženého itemu na kurzoru.</summary>
        [Header("Cursor UI")]
        public Image cursorImage;

        /// <summary>Text zobrazující počet kusů drženého stacku (skrytý pokud ≤ 1).</summary>
        [Header("Stack Amount")]
        public TMP_Text stackAmountText;

        /// <summary>Poslední zobrazený počet kusů – slouží k detekci změny bez vynuceného překreslení.</summary>
        private int lastAmount = -1;

        /// <summary>Data slotu aktuálně drženého kurzorem (null = kurzor nic nedrží).</summary>
        public InventorySlotData HeldSlot { get; private set; }

        /// <summary>
        /// Singleton inicializace. Skryje ikonu kurzoru a resetuje text počtu.
        /// </summary>
        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }

            Instance = this;
            cursorImage.enabled = false;
            RefreshCursorAmount();
        }

        /// <summary>
        /// Každý snímek přesouvá ikonu kurzoru na pozici myši.
        /// Pokud se změnil počet kusů, obnoví textový popis.
        /// </summary>
        private void Update()
        {
            if (HeldSlot != null)
            {
                cursorImage.transform.position = Input.mousePosition;

                if (HeldSlot.amount != lastAmount)
                {
                    RefreshCursorAmount();
                    lastAmount = HeldSlot.amount;
                }
            }
        }

        /// <summary>
        /// Vezme celý stack z daného slotu inventáře a přesune ho na kurzor.
        /// Zdrojový slot se vymaže. Obnoví celé inventářní UI.
        /// </summary>
        /// <param name="slotIndex">Index slotu v InventoryData.Slots.</param>
        public void TakeFromSlot(int slotIndex)
        {
            var slots = InventoryData.Instance.Slots;
            var slot = slots[slotIndex];

            if (slot == null || slot.IsEmpty)
                return;

            HeldSlot = new InventorySlotData
            {
                item = slot.item,
                amount = slot.amount
            };

            slot.Clear();

            cursorImage.sprite = HeldSlot.item.icon;
            cursorImage.enabled = true;
            RefreshCursorAmount();

            InventoryUI.RefreshAll();
        }

        /// <summary>
        /// Položí item z kurzoru do daného slotu inventáře.
        /// Prázdný slot: přesune celý stack. Stejný item: stackuje (do maxStack).
        /// Jiný item nebo plný slot: neprovede nic.
        /// </summary>
        /// <param name="slotIndex">Index cílového slotu v InventoryData.Slots.</param>
        public void PlaceIntoSlot(int slotIndex)
        {
            if (HeldSlot == null)
                return;

            var slots = InventoryData.Instance.Slots;
            var slot = slots[slotIndex];

            if (slot.IsEmpty)
            {
                slot.item = HeldSlot.item;
                slot.amount = HeldSlot.amount;
                HeldSlot = null;
            }
            else if (slot.CanStackWith(HeldSlot.item))
            {
                int move = Mathf.Min(slot.FreeSpace, HeldSlot.amount);
                slot.amount += move;
                HeldSlot.amount -= move;

                if (HeldSlot.amount <= 0)
                    HeldSlot = null;
            }
            else
            {
                return;
            }

            cursorImage.enabled = HeldSlot != null;
            RefreshCursorAmount();

            InventoryUI.RefreshAll();
        }

        /// <summary>
        /// Zpracuje pravé kliknutí na slot inventáře.
        /// Kurzor prázdný: vezme polovinu stacku ze slotu.
        /// Kurzor drží item: položí 1 kus do slotu (prázdný nebo stejný item).
        /// </summary>
        /// <param name="slotIndex">Index slotu v InventoryData.Slots.</param>
        public void RightClickOnSlot(int slotIndex)
        {
            var slots = InventoryData.Instance.Slots;
            var slot = slots[slotIndex];

            if (HeldSlot == null)
            {
                if (slot.IsEmpty || !slot.item.stackable || slot.amount <= 1)
                    return;

                int half = slot.amount / 2;

                HeldSlot = new InventorySlotData
                {
                    item = slot.item,
                    amount = half
                };

                slot.amount -= half;

                cursorImage.sprite = HeldSlot.item.icon;
                cursorImage.enabled = true;

                InventoryUI.RefreshAll();
                RefreshCursorAmount();
                return;
            }

            if (HeldSlot != null)
            {
                if (slot.IsEmpty)
                {
                    slot.item = HeldSlot.item;
                    slot.amount = 1;
                    HeldSlot.amount--;
                }
                else if (slot.CanStackWith(HeldSlot.item))
                {
                    slot.amount += 1;
                    HeldSlot.amount--;
                }
                else
                {
                    return;
                }

                if (HeldSlot.amount <= 0)
                {
                    ClearHeldSlot();
                }
                else
                {
                    RefreshCursorUI();
                }

                InventoryUI.RefreshAll();
            }
        }

        /// <summary>
        /// Pokusí se přidat daný počet kusů k itemu drženému na kurzoru.
        /// Selže pokud kurzor drží jiný item nebo je stack plný.
        /// </summary>
        /// <param name="item">Item, který se má přidat ke stacku na kurzoru.</param>
        /// <param name="amount">Počet kusů k přidání.</param>
        /// <returns>True, pokud byly alespoň některé kusy úspěšně přidány.</returns>
        public bool TryAddToHeldSlot(InventoryItemData item, int amount)
        {
            if (HeldSlot == null)
                return false;

            if (HeldSlot.item != item)
                return false;

            int max = item.maxStack;
            int canAdd = max - HeldSlot.amount;

            if (canAdd <= 0)
                return false;

            int add = Mathf.Min(canAdd, amount);
            HeldSlot.amount += add;

            RefreshCursorUI();
            return true;
        }

        /// <summary>
        /// Sesbírá všechny kusy daného itemu z inventáře i craftovacího gridu
        /// a přidá je ke stacku na kurzoru (do maxStack). Přebytek vrátí do inventáře.
        /// Pokud kurzor drží jiný item, metoda nic neprovede.
        /// </summary>
        /// <param name="item">Item, jehož všechny kusy se mají sebrat.</param>
        public void CollectAllOfItem(InventoryItemData item)
        {
            if (item == null)
                return;

            int total = 0;

            var invSlots = InventoryData.Instance.Slots;
            for (int i = 0; i < invSlots.Length; i++)
            {
                var s = invSlots[i];
                if (!s.IsEmpty && s.item == item)
                {
                    total += s.amount;
                    s.Clear();
                }
            }

            var craftSlots = CraftingData.Instance.gridSlots;
            for (int i = 0; i < craftSlots.Length; i++)
            {
                var s = craftSlots[i];
                if (!s.IsEmpty && s.item == item)
                {
                    total += s.amount;
                    s.Clear();
                }
            }

            if (total <= 0)
                return;

            int max = item.maxStack;

            if (HeldSlot == null)
            {
                int take = Mathf.Min(total, max);
                HeldSlot = new InventorySlotData
                {
                    item = item,
                    amount = take
                };
                total -= take;
            }
            else if (HeldSlot.item == item)
            {
                int free = max - HeldSlot.amount;
                int take = Mathf.Min(free, total);
                HeldSlot.amount += take;
                total -= take;
            }

            if (total > 0)
            {
                InventoryData.Instance.AddItemSmart(item, total);
            }

            RefreshCursorUI();
        }

        /// <summary>
        /// Nastaví nový slot jako držený kurzorem a zobrazí jeho ikonu.
        /// </summary>
        /// <param name="slot">Data slotu k držení (item + amount).</param>
        public void SetHeldSlot(InventorySlotData slot)
        {
            HeldSlot = slot;
            cursorImage.sprite = slot.item.icon;
            cursorImage.enabled = true;
            RefreshCursorAmount();
        }

        /// <summary>
        /// Vymaže držený slot a skryje ikonu kurzoru.
        /// </summary>
        public void ClearHeldSlot()
        {
            HeldSlot = null;
            cursorImage.enabled = false;
            RefreshCursorAmount();
            stackAmountText.text = "";
            lastAmount = -1;
        }

        /// <summary>
        /// Obnoví ikonu kurzoru podle aktuálně drženého slotu.
        /// </summary>
        private void RefreshCursorIcon()
        {
            if (HeldSlot == null)
            {
                cursorImage.enabled = false;
                return;
            }

            cursorImage.sprite = HeldSlot.item.icon;
            cursorImage.enabled = true;
        }

        /// <summary>
        /// Obnoví textový počet kusů na kurzoru.
        /// Text se skryje pokud kurzor nic nedrží nebo drží pouze 1 kus.
        /// </summary>
        private void RefreshCursorAmount()
        {
            if (HeldSlot == null || HeldSlot.amount <= 1)
            {
                stackAmountText.text = "";
                return;
            }

            stackAmountText.text = HeldSlot.amount.ToString();
        }

        /// <summary>
        /// Obnoví ikonu i text počtu kurzoru najednou.
        /// </summary>
        public void RefreshCursorUI()
        {
            RefreshCursorIcon();
            RefreshCursorAmount();
        }

        /// <summary>
        /// Vynucené obnovení celého vizuálu kurzoru (ikona i text).
        /// Používá se po ukončení drag operace k synchronizaci stavu.
        /// </summary>
        public void ForceRefresh()
        {
            if (HeldSlot == null)
            {
                cursorImage.enabled = false;
                stackAmountText.text = "";
                lastAmount = -1;
                return;
            }

            cursorImage.sprite = HeldSlot.item.icon;
            cursorImage.enabled = true;
            stackAmountText.text = HeldSlot.amount > 1
                ? HeldSlot.amount.ToString()
                : "";

            lastAmount = HeldSlot.amount;
        }
    }
}