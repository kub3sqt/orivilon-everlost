using Orivilon.Inventory.Crafting;
using Orivilon.Core;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Singleton řídící přepínání záložek inventářního okna (Inventory, Armory, Crafting).
/// Zajišťuje výchozí otevření na záložce Inventory, přepínání aktivního panelu
/// a vizuální změnu sprite tlačítek (aktivní/neaktivní stav).
///
/// DŮLEŽITÉ: Tento skript záměrně neobsahuje metodu Update() ani nezpracovává vstup.
/// Tab a Escape řeší výhradně GameManager, který volá OpenInventory() a OnClose().
/// Dvojí zpracování vstupu způsobovalo konflikt – inventář se nedal zavřít.
/// </summary>
namespace Orivilon.Inventory.Inventory
{
    public class InventoryUIController : MonoBehaviour
    {
        /// <summary>Kořenový GameObject celého inventářního okna.</summary>
        [Header("Root inventáře")]
        [SerializeField] private GameObject inventoryRoot;

        /// <summary>Panel s mřížkou inventářních slotů.</summary>
        [Header("Panely")]
        [SerializeField] private GameObject inventoryPanel;

        /// <summary>Panel se sloty zbroje (momentálně v přípravě).</summary>
        [SerializeField] private GameObject armoryPanel;

        /// <summary>Panel s craftovacím gridem 3×3 a výstupním slotem.</summary>
        [SerializeField] private GameObject craftingPanel;

        /// <summary>Image komponenta tlačítka záložky Inventory (mění sprite při aktivaci).</summary>
        [Header("Tlačítka")]
        [SerializeField] private Image inventoryButton;

        /// <summary>Image komponenta tlačítka záložky Armory.</summary>
        [SerializeField] private Image armoryButton;

        /// <summary>Image komponenta tlačítka záložky Crafting.</summary>
        [SerializeField] private Image craftingButton;

        /// <summary>Sprite tlačítka Inventory v neaktivním stavu.</summary>
        [Header("Textury tlačítek")]
        [SerializeField] private Sprite inventoryInactive;

        /// <summary>Sprite tlačítka Inventory v aktivním stavu.</summary>
        [SerializeField] private Sprite inventoryActive;

        /// <summary>Sprite tlačítka Armory v neaktivním stavu.</summary>
        [SerializeField] private Sprite armoryInactive;

        /// <summary>Sprite tlačítka Armory v aktivním stavu.</summary>
        [SerializeField] private Sprite armoryActive;

        /// <summary>Sprite tlačítka Crafting v neaktivním stavu.</summary>
        [SerializeField] private Sprite craftingInactive;

        /// <summary>Sprite tlačítka Crafting v aktivním stavu.</summary>
        [SerializeField] private Sprite craftingActive;

        /// <summary>Globální instance singletonu.</summary>
        public static InventoryUIController Instance;

        /// <summary>True, pokud je aktuálně aktivní záložka Crafting.</summary>
        public bool IsCraftingOpen => craftingPanel.activeSelf;

        /// <summary>Singleton inicializace.</summary>
        private void Awake()
        {
            Instance = this;
        }

        /// <summary>
        /// Aktivuje záložku Inventory jako výchozí panel po otevření inventáře.
        /// Pokud byl otevřen crafting, vrátí suroviny do inventáře před přepnutím.
        /// Volá GameManager při každém otevření inventáře.
        /// </summary>
        public void OpenInventory()
        {
            if (craftingPanel.activeSelf)
            {
                CraftingData.Instance.ReturnAllToInventory();
                CraftingController.Instance.RefreshAllSlots();
            }
            SetPanels(inventoryPanel);
            SetButtons(inventoryActive, armoryInactive, craftingInactive);
            ItemDescriptionUI.Instance.ClearFromSlot();
        }

        /// <summary>
        /// Přepne na záložku Armory.
        /// Pokud byl otevřen crafting, vrátí suroviny do inventáře před přepnutím.
        /// Volá se z UI tlačítka záložky Armory.
        /// </summary>
        public void OpenArmory()
        {
            if (craftingPanel.activeSelf)
            {
                CraftingData.Instance.ReturnAllToInventory();
                CraftingController.Instance.RefreshAllSlots();
            }
            SetPanels(armoryPanel);
            SetButtons(inventoryInactive, armoryActive, craftingInactive);
            ItemDescriptionUI.Instance.ClearFromSlot();
        }

        /// <summary>
        /// Přepne na záložku Crafting.
        /// Suroviny se nevracejí – uživatel přešel na crafting záměrně.
        /// Volá se z UI tlačítka záložky Crafting.
        /// </summary>
        public void OpenCrafting()
        {
            SetPanels(craftingPanel);
            SetButtons(inventoryInactive, armoryInactive, craftingActive);
            ItemDescriptionUI.Instance.ClearFromSlot();
        }

        /// <summary>
        /// Připraví inventář na zavření.
        /// Pokud je otevřen crafting, vrátí všechny suroviny z gridu do inventáře.
        /// Volá GameManager těsně před deaktivací inventářního panelu.
        /// </summary>
        public void OnClose()
        {
            if (craftingPanel.activeSelf)
            {
                CraftingData.Instance.ReturnAllToInventory();
                CraftingController.Instance.RefreshAllSlots();
            }
        }

        /// <summary>
        /// Skryje všechny panely a zobrazí pouze požadovaný aktivní panel.
        /// </summary>
        /// <param name="activePanel">Panel, který má být zobrazen.</param>
        private void SetPanels(GameObject activePanel)
        {
            inventoryPanel.SetActive(false);
            armoryPanel.SetActive(false);
            craftingPanel.SetActive(false);

            activePanel.SetActive(true);
        }

        /// <summary>
        /// Nastaví sprite všech tří záložkových tlačítek najednou.
        /// Aktivní záložka dostane "active" sprite, ostatní "inactive" sprite.
        /// </summary>
        /// <param name="inventory">Sprite pro tlačítko Inventory.</param>
        /// <param name="armory">Sprite pro tlačítko Armory.</param>
        /// <param name="crafting">Sprite pro tlačítko Crafting.</param>
        private void SetButtons(Sprite inventory, Sprite armory, Sprite crafting)
        {
            inventoryButton.sprite = inventory;
            armoryButton.sprite = armory;
            craftingButton.sprite = crafting;
        }
    }
}