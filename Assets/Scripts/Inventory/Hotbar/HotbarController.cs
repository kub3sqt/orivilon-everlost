using Orivilon.Core;
using Orivilon.Inventory.Inventory;
using Orivilon.Player;
using UnityEngine;

namespace Orivilon.Inventory.Hotbar
{
    /// <summary>
    /// Singleton řídící aktivní slot hotbaru a item držený v ruce hráče.
    /// Přepínání slotů probíhá kolečkem myši (cyklicky), číselnými klávesami 1–8 nebo kliknutím na slot v UI.
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

        /// <summary>
        /// 23.10.2026: zvýraznění aktivního slotu je pro hraní – při otevřeném inventáři je skryté (index zůstává),
        /// po zavření se obnoví a vybaví se AKTUÁLNÍ obsah slotu.
        /// </summary>
        private bool glowHidden;

        /// <summary>Je otevřený inventář (equip index se v něm nemění, glow je skrytý)?</summary>
        public static bool InventoryOpen => GameManager.instance != null && GameManager.instance.IsInventoryOpen;

        /// <summary>Pevná velikost hotbaru.</summary>
        private const int HOTBAR_SIZE = InventoryData.HotbarSize;

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

        private void OnDestroy()
        {
            if (InventoryData.Instance != null)
                InventoryData.Instance.OnInventoryChanged -= RefreshActiveSlot;
        }

        /// <summary>
        /// Každý snímek čte kolečko myši a přepíná aktivní slot.
        /// Blokováno během pauzy nebo otevřeného menu.
        /// </summary>
        private void Update()
        {
            bool hide = InventoryOpen;
            if (hide != glowHidden)
            {
                glowHidden = hide;
                ApplyGlow();
                if (!hide) UpdateHeldItem();   // obsah původního slotu se mohl v inventáři změnit
            }

            if (GameManager.instance != null && (GameManager.instance.IsPaused || GameManager.instance.isMenuOpen))
                return;

            // Kolečko myši nesmí přepínat sloty, když hráč píše do konzole.
            if (GameConsole.IsOpen)
                return;

            // Q = jeden kus, Ctrl+Q = celý stack z aktivního slotu (Cmd+Q na Macu ukončuje aplikaci, proto jen Ctrl).
            // Game.unity má dvě zapnuté komponenty HotbarController – vyhazuje jen singleton, jinak by
            // jedno stisknutí Q vyhodilo dva kusy.
            if (Instance == this && Input.GetKeyDown(KeyCode.Q) && Orivilon.World.Objects.ItemDropService.GameplayInputAllowed())
                DropActive(Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl));

            // Číselné klávesy 1–8 = přímý výběr slotu (dřív chyběly úplně – jen kolečko a klik v UI). Jen singleton
            // a jen při volném herním vstupu (ne v pauze, menu, inventáři ani při psaní); v otevřeném inventáři
            // čísla patří InventoryDragDistributor (výměna slotu pod kurzorem).
            if (Instance == this && Orivilon.World.Objects.ItemDropService.GameplayInputAllowed())
                for (int k = 0; k < HOTBAR_SIZE; k++)
                    if (Input.GetKeyDown(KeyCode.Alpha1 + k) || Input.GetKeyDown(KeyCode.Keypad1 + k)) { SetActiveSlot(k); break; }

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
            ApplyGlow();
            UpdateHeldItem();
        }

        /// <summary>Aktuálně vybraný (vybavený) slot hotbaru.</summary>
        public int CurrentIndex => currentIndex;

        private void ApplyGlow()
        {
            for (int i = 0; i < slots.Length; i++)
                slots[i].SetActive(!glowHidden && i == currentIndex);
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

        /// <summary>Vyhodí jeden kus (all = celý stack) z aktivního slotu hotbaru před hráče.</summary>
        public bool DropActive(bool all)
        {
            bool ok = InventoryActions.DropFromSlot(SlotRef.Player(currentIndex), all);
            if (ok) InventoryActions.Commit();
            return ok;
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