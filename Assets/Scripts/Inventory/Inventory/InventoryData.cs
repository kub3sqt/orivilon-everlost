using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;

namespace Orivilon.Inventory.Inventory
{
    /// <summary>
    /// Singleton – centrální datový model inventáře hráče.
    /// Uchovává pole slotů (výchozí velikost 63: hotbar 0–7 + inventář 8–62).
    /// Poskytuje metody pro přidávání itemů se stackováním a prioritou umístění.
    /// Vyvolává událost OnInventoryChanged po každé změně dat.
    /// </summary>
    public class InventoryData : MonoBehaviour
    {
        /// <summary>Globální instance singletonu.</summary>
        public static InventoryData Instance;

        /// <summary>Celkový počet slotů inventáře (hotbar + hlavní inventář).</summary>
        [Header("Inventory Settings")]
        [SerializeField] private int inventorySize = 63;

        /// <summary>Pole dat všech inventářních slotů.</summary>
        [Header("Inventory Slots")]
        [SerializeField] private InventorySlotData[] slots;

        /// <summary>Veřejný přístup k poli slotů (read-only reference).</summary>
        public InventorySlotData[] Slots => slots;

        /// <summary>
        /// Událost vyvolaná po každé změně obsahu inventáře.
        /// Přihlašují se na ni HotbarController a další systémy potřebující aktualizaci.
        /// </summary>
        public event System.Action OnInventoryChanged;

        /// <summary>
        /// Singleton inicializace. Vytvoří nebo upraví pole slotů na správnou velikost
        /// a zajistí, že každý slot má platnou instanci InventorySlotData.
        /// </summary>
        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }

            Instance = this;

            if (slots == null || slots.Length == 0)
            {
                slots = new InventorySlotData[inventorySize];
            }
            else if (slots.Length != inventorySize)
            {
                System.Array.Resize(ref slots, inventorySize);
            }

            for (int i = 0; i < slots.Length; i++)
            {
                if (slots[i] == null)
                    slots[i] = new InventorySlotData();
            }
        }

        /// <summary>
        /// Při startu obnoví celé UI inventáře a vyvolá událost změny.
        /// </summary>
        private void Start()
        {
            InventoryUI.RefreshAll();
            OnInventoryChanged?.Invoke();
        }

        /// <summary>
        /// Přidá item do inventáře s podporou stackování.
        /// Průchod 1: stackuje do existujících slotů se stejným itemem.
        /// Průchod 2: ukládá do prvních prázdných slotů.
        /// Pokud se nevešlo vše, obnoví UI a vrátí false.
        /// </summary>
        /// <param name="item">Item k přidání.</param>
        /// <param name="amount">Počet kusů (výchozí 1).</param>
        /// <returns>True, pokud byly všechny kusy úspěšně přidány.</returns>
        public bool AddItem(InventoryItemData item, int amount = 1)
        {
            if (item == null || amount <= 0)
                return false;

            if (item.stackable)
            {
                for (int i = 0; i < slots.Length; i++)
                {
                    var slot = slots[i];

                    if (slot.CanStackWith(item))
                    {
                        int move = Mathf.Min(slot.FreeSpace, amount);
                        slot.amount += move;
                        amount -= move;

                        if (amount <= 0)
                        {
                            InventoryUI.RefreshAll();
                            OnInventoryChanged?.Invoke();
                            return true;
                        }
                    }
                }
            }

            for (int i = 0; i < slots.Length; i++)
            {
                var slot = slots[i];

                if (slot.IsEmpty)
                {
                    slot.item = item;
                    slot.amount = Mathf.Min(item.maxStack, amount);
                    amount -= slot.amount;

                    if (amount <= 0)
                    {
                        InventoryUI.RefreshAll();
                        OnInventoryChanged?.Invoke();
                        return true;
                    }
                }
            }

            Debug.Log("Inventář je plný");
            InventoryUI.RefreshAll();
            OnInventoryChanged?.Invoke();
            return false;
        }

        /// <summary>
        /// Přidá item do inventáře s chytrou prioritou umístění:
        /// 1. Stackuje do inventáře (9+).
        /// 2. Stackuje do hotbaru (0–8).
        /// 3. Ukládá do prázdných slotů inventáře.
        /// 4. Ukládá do prázdných slotů hotbaru (poslední možnost).
        /// Tato priorita zajišťuje, že hotbar se nezaplní dříve než inventář.
        /// </summary>
        /// <param name="item">Item k přidání.</param>
        /// <param name="amount">Počet kusů k přidání.</param>
        /// <returns>True, pokud byly všechny kusy úspěšně přidány.</returns>
        public bool AddItemSmart(InventoryItemData item, int amount)
        {
            if (item == null || amount <= 0)
                return false;

            amount = TryStackRange(9, slots.Length - 1, item, amount);
            if (amount <= 0) return true;

            amount = TryStackRange(0, 8, item, amount);
            if (amount <= 0) return true;

            amount = TryEmptyRange(9, slots.Length - 1, item, amount);
            if (amount <= 0) return true;

            amount = TryEmptyRange(0, 8, item, amount);

            InventoryUI.RefreshAll();
            OnInventoryChanged?.Invoke();
            return amount <= 0;
        }

        /// <summary>
        /// Přidá item do konkrétního rozsahu slotů (stack + prázdné).
        /// Používá se pro přesun mezi hotbarem a inventářem (Shift+klik).
        /// </summary>
        /// <param name="from">Počáteční index rozsahu (včetně).</param>
        /// <param name="to">Koncový index rozsahu (včetně).</param>
        /// <param name="item">Item k přidání.</param>
        /// <param name="amount">Počet kusů.</param>
        /// <returns>True, pokud byly všechny kusy přidány do daného rozsahu.</returns>
        public bool AddItemRange(int from, int to, InventoryItemData item, int amount)
        {
            int remaining = amount;

            remaining = TryStackRange(from, to, item, remaining);
            if (remaining <= 0) return true;

            remaining = TryEmptyRange(from, to, item, remaining);
            return remaining <= 0;
        }

        /// <summary>
        /// Pokusí se stackovat item do existujících slotů v daném rozsahu.
        /// Přeskočí nestackovatelné itemy. Vrátí zbývající počet kusů.
        /// </summary>
        private int TryStackRange(int from, int to, InventoryItemData item, int amount)
        {
            if (!item.stackable) return amount;

            for (int i = from; i <= to; i++)
            {
                var slot = slots[i];
                if (!slot.CanStackWith(item)) continue;

                int move = Mathf.Min(slot.FreeSpace, amount);
                slot.amount += move;
                amount -= move;

                if (amount <= 0)
                    return 0;
            }

            return amount;
        }

        /// <summary>
        /// Ukládá item do prázdných slotů v daném rozsahu.
        /// Respektuje maxStack – jeden slot pojme nejvýše maxStack kusů.
        /// Vrátí zbývající počet kusů.
        /// </summary>
        private int TryEmptyRange(int from, int to, InventoryItemData item, int amount)
        {
            for (int i = from; i <= to; i++)
            {
                var slot = slots[i];
                if (!slot.IsEmpty) continue;

                slot.item = item;
                slot.amount = Mathf.Min(item.maxStack, amount);
                amount -= slot.amount;

                if (amount <= 0)
                    return 0;
            }

            return amount;
        }

        /// <summary>
        /// Vyvolá událost OnInventoryChanged a tím spustí refresh UI.
        /// Volá se externě ze systémů, které mění data slotů přímo (BuildingTool, PlayerItemUse).
        /// </summary>
        public void NotifyInventoryChanged()
        {
            OnInventoryChanged?.Invoke();
        }
    }
}