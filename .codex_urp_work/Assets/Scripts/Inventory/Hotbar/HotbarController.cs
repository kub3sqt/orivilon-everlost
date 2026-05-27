using Orivilon.Core;
using Orivilon.Inventory.Inventory;
using Orivilon.Player;
using UnityEngine;

namespace Orivilon.Inventory.Hotbar
{
    /// <summary>
    /// Singleton řídící aktivní slot hotbaru a item držený v ruce hráče.
    /// Přepínání slotů probíhá kolečkem myši (cyklicky) nebo kliknutím na slot v UI.
    /// Po každé změně informuje PlayerEquipment (herní logika nástroje)
    /// i HandItemRenderer (vizuální model v ruce).
    /// Během pauzy nebo otevřeného menu scroll ignoruje.
    /// </summary>
    public class HotbarController : MonoBehaviour
    {
        /// <summary>Globální instance singletonu.</summary>
        public static HotbarController Instance { get; private set; }

        /// <summary>Pole UI elementů jednotlivých slotů hotbaru (přiřazovat v Inspektoru).</summary>
        [Header("Hotbar UI")]
        [SerializeField] private HotbarSlotUI[] slots;

        /// <summary>Index aktuálně aktivního slotu (0–8).</summary>
        private int currentIndex = 0;

        /// <summary>Pevná velikost hotbaru – prvních 9 slotů inventáře.</summary>
        private const int HOTBAR_SIZE = 9;

        /// <summary>
        /// Singleton inicializace.
        /// </summary>
        private void Awake()
        {
            Instance = this;
        }

        /// <summary>
        /// Přihlásí se na událost změny inventáře a nastaví výchozí aktivní slot (index 0).
        /// </summary>
        private void Start()
        {
            InventoryData.Instance.OnInventoryChanged += RefreshActiveSlot;
            SetActiveSlot(0);
        }

        /// <summary>
        /// Každý snímek čte kolečko myši a přepíná aktivní slot.
        /// Blokováno během pauzy nebo otevřeného menu.
        /// </summary>
        private void Update()
        {
            if (GameManager.instance != null && (GameManager.instance.IsPaused || GameManager.instance.isMenuOpen))
                return;

            float scroll = Input.GetAxis("Mouse ScrollWheel");

            if (scroll > 0f)
                SetActiveSlot(currentIndex - 1);
            else if (scroll < 0f)
                SetActiveSlot(currentIndex + 1);
        }

        /// <summary>
        /// Nastaví aktivní slot na daný index. Index se cyklicky zalamuje (0–8).
        /// Aktualizuje vizuální zvýraznění všech slotů a informuje o změně drženého itemu.
        /// </summary>
        /// <param name="index">Požadovaný index slotu (automaticky zalamován).</param>
        public void SetActiveSlot(int index)
        {
            if (index < 0)
                index = HOTBAR_SIZE - 1;
            if (index >= HOTBAR_SIZE)
                index = 0;

            currentIndex = index;

            for (int i = 0; i < slots.Length; i++)
                slots[i].SetActive(i == currentIndex);

            UpdateHeldItem();
        }

        /// <summary>
        /// Obnoví informace o drženém itemu bez změny indexu.
        /// Volá se po každé změně inventáře (pickup, crafting, použití jídla).
        /// </summary>
        public void RefreshActiveSlot()
        {
            UpdateHeldItem();
        }

        /// <summary>
        /// Předá aktuálně držený item oběma systémům:
        /// PlayerEquipment (herní logika – typ nástroje) a HandItemRenderer (3D model v ruce).
        /// Pokud je aktivní slot prázdný, oba systémy dostanou null.
        /// </summary>
        private void UpdateHeldItem()
        {
            if (InventoryData.Instance == null)
                return;

            var slotData = InventoryData.Instance.Slots[currentIndex];
            var item = (!slotData.IsEmpty) ? slotData.item : null;

            PlayerEquipment.Instance?.EquipTool(item);

            if (HandItemRenderer.Instance != null)
                HandItemRenderer.Instance.SetItem(item);
        }

        /// <summary>
        /// Vrátí datovou definici itemu v aktuálně aktivním slotu, nebo null pokud je slot prázdný.
        /// </summary>
        /// <returns>Item v aktivním slotu hotbaru.</returns>
        public InventoryItemData GetHeldItem()
        {
            var slotData = InventoryData.Instance.Slots[currentIndex];
            return (!slotData.IsEmpty) ? slotData.item : null;
        }

        /// <summary>
        /// Vrátí celá data slotu (item + počet) pro aktuálně aktivní slot hotbaru.
        /// Používá se například při konzumaci jídla nebo použití itemu.
        /// </summary>
        /// <returns>Data aktivního slotu hotbaru.</returns>
        public InventorySlotData GetHeldSlot()
        {
            return InventoryData.Instance.Slots[currentIndex];
        }
    }
}