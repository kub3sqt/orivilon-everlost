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
            NotifyInventoryChanged();
        }

        /// <summary>Počet slotů hotbaru (indexy 0 .. HotbarSize-1); zbytek pole je hlavní inventář.</summary>
        public const int HotbarSize = 8;

        /// <summary>
        /// Přidá item do inventáře s podporou stackování – VŠE NEBO NIC.
        /// Když se nevejde celé množství, inventář se nezmění a vrátí se false
        /// (dřív se část zapsala a volající – např. PickupItem – nechal předmět ve světě celý → duplikace).
        /// Průchod 1: stackuje do existujících slotů se stejným itemem. Průchod 2: první prázdné sloty.
        /// </summary>
        /// <param name="item">Item k přidání.</param>
        /// <param name="amount">Počet kusů (výchozí 1).</param>
        /// <returns>True, pokud byly všechny kusy přidány.</returns>
        public bool AddItem(InventoryItemData item, int amount = 1)
        {
            if (item == null || amount <= 0)
                return false;

            if (SpaceFor(slots, 0, slots.Length - 1, item) < amount)
            {
                Debug.Log("Inventář je plný");
                return false;
            }

            InsertPartial(slots, 0, slots.Length - 1, item, amount);
            NotifyInventoryChanged();
            return true;
        }

        /// <summary>
        /// Přidá co nejvíc kusů (priorita AddItemSmart) a vrátí počet přidaných. Pro zdroje, které
        /// zbytek stejně neuchovají (těžba bez systému vyhazování) – chování jako dřív, bez falešného false.
        /// </summary>
        public int AddItemUpTo(InventoryItemData item, int amount)
        {
            if (item == null || amount <= 0) return 0;
            int added = InsertSmart(slots, item, amount);
            if (added > 0) NotifyInventoryChanged();
            return added;
        }

        /// <summary>
        /// Přidá item s prioritou umístění (stack v inventáři → stack v hotbaru → prázdné v inventáři
        /// → prázdné v hotbaru) – VŠE NEBO NIC, aby se hotbar nezaplnil dřív než inventář.
        /// </summary>
        /// <returns>True, pokud byly všechny kusy přidány; při false se nic nezměnilo.</returns>
        public bool AddItemSmart(InventoryItemData item, int amount)
        {
            if (item == null || amount <= 0)
                return false;
            if (SpaceFor(slots, 0, slots.Length - 1, item) < amount)
                return false;

            InsertSmart(slots, item, amount);
            NotifyInventoryChanged();
            return true;
        }

        /// <summary>
        /// Přidá item do konkrétního rozsahu slotů (stack + prázdné) – VŠE NEBO NIC.
        /// </summary>
        /// <returns>True, pokud byly všechny kusy přidány do daného rozsahu; při false se nic nezměnilo.</returns>
        public bool AddItemRange(int from, int to, InventoryItemData item, int amount)
        {
            if (item == null || amount <= 0)
                return false;
            if (SpaceFor(slots, from, to, item) < amount)
                return false;

            InsertPartial(slots, from, to, item, amount);
            NotifyInventoryChanged();
            return true;
        }

        /// <summary>
        /// Kolik kusů daného itemu se vejde do rozsahu slotů (stack + prázdné), bez změny dat.
        /// </summary>
        public static int SpaceFor(InventorySlotData[] arr, int from, int to, InventoryItemData item)
        {
            if (item == null || arr == null) return 0;
            int limit = InventorySlotData.StackLimit(item);
            int space = 0;
            from = Mathf.Max(0, from);
            to = Mathf.Min(arr.Length - 1, to);
            for (int i = from; i <= to; i++)
            {
                var s = arr[i];
                if (s == null) continue;
                if (s.IsEmpty) space += limit;
                else if (InventorySlotData.Compatible(s.item, item)) space += Mathf.Max(0, limit - s.amount);
            }
            return space;
        }

        /// <summary>
        /// Vloží co nejvíc kusů do rozsahu: nejdřív do kompatibilních stacků, pak do prázdných slotů.
        /// Vrací POČET VLOŽENÝCH kusů; zbytek zůstává volajícímu (nic se neztrácí ani nevzniká).
        /// Nevyvolává událost – volající ji vyvolá po dokončení celé operace.
        /// </summary>
        public static int InsertPartial(InventorySlotData[] arr, int from, int to, InventoryItemData item, int amount)
        {
            if (item == null || amount <= 0 || arr == null) return 0;
            int left = amount;
            left -= StackInto(arr, from, to, item, left);
            left -= FillEmpty(arr, from, to, item, left);
            return amount - left;
        }

        /// <summary>Priorita AddItemSmart nad libovolným polem slotů. Vrací počet vložených kusů.</summary>
        public static int InsertSmart(InventorySlotData[] arr, InventoryItemData item, int amount)
        {
            if (item == null || amount <= 0 || arr == null) return 0;
            int last = arr.Length - 1;
            int left = amount;
            left -= StackInto(arr, HotbarSize, last, item, left);
            left -= StackInto(arr, 0, HotbarSize - 1, item, left);
            left -= FillEmpty(arr, HotbarSize, last, item, left);
            left -= FillEmpty(arr, 0, HotbarSize - 1, item, left);
            return amount - left;
        }

        /// <summary>Doplní kompatibilní neplné stacky v rozsahu. Vrací počet vložených kusů.</summary>
        private static int StackInto(InventorySlotData[] arr, int from, int to, InventoryItemData item, int amount)
        {
            if (amount <= 0 || !item.stackable) return 0;
            int left = amount;
            from = Mathf.Max(0, from);
            to = Mathf.Min(arr.Length - 1, to);
            for (int i = from; i <= to && left > 0; i++)
            {
                var slot = arr[i];
                if (slot == null || !slot.CanStackWith(item)) continue;
                int move = Mathf.Min(slot.FreeSpace, left);
                slot.amount += move;
                left -= move;
            }
            return amount - left;
        }

        /// <summary>Uloží kusy do prázdných slotů v rozsahu (max limit stacku na slot). Vrací počet vložených.</summary>
        private static int FillEmpty(InventorySlotData[] arr, int from, int to, InventoryItemData item, int amount)
        {
            if (amount <= 0) return 0;
            int limit = InventorySlotData.StackLimit(item);
            int left = amount;
            from = Mathf.Max(0, from);
            to = Mathf.Min(arr.Length - 1, to);
            for (int i = from; i <= to && left > 0; i++)
            {
                var slot = arr[i];
                if (slot == null || !slot.IsEmpty) continue;
                int move = Mathf.Min(limit, left);
                slot.Set(item, move);
                left -= move;
            }
            return amount - left;
        }

        /// <summary>
        /// Vyvolá událost OnInventoryChanged. Na ni jsou přihlášené všechny zobrazené sloty
        /// (InventorySlot, CraftingSlot), hotbar i Arcanum – jediná cesta k obnovení UI po změně dat.
        /// Volají ji i systémy, které mění data slotů přímo (BuildingTool, PlayerItemUse, Arcanum, save).
        /// </summary>
        public void NotifyInventoryChanged()
        {
            OnInventoryChanged?.Invoke();
        }

        /// <summary>Počet aktuálních posluchačů OnInventoryChanged (diagnostika úniku subscriptions).</summary>
        public int ListenerCount => OnInventoryChanged == null ? 0 : OnInventoryChanged.GetInvocationList().Length;
    }
}