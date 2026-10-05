using Orivilon.Inventory.Hotbar;
using Orivilon.Inventory.Inventory;
using UnityEngine;

namespace Orivilon.Player
{
    /// <summary>
    /// Singleton uchovávající referenci na aktuálně vybavený nástroj hráče.
    /// Ostatní systémy (HarvestableObject, PlayerInteraction) čtou EquippedTool
    /// pro zjištění, zda hráč drží správný nástroj pro danou akci.
    /// </summary>
    public class PlayerEquipment : MonoBehaviour
    {
        /// <summary>Globální instance singletonu.</summary>
        public static PlayerEquipment Instance;

        /// <summary>Datová definice aktuálně vybaveného nástroje, nebo null pokud hráč nic nedrží.</summary>
        public InventoryItemData EquippedTool { get; private set; }

        /// <summary>
        /// Drží hráč krumpáč? Jediné místo, kde se to rozhoduje.
        ///
        /// <para>Ptá se na to jak nástroj na kopání (smí vůbec těžit), tak ovladač kamery
        /// (nesmí zoomovat pravým tlačítkem, protože to při krumpáči staví). Kdyby si tu
        /// podmínku každý psal sám, rozešly by se při první změně – například až přibude
        /// druhý typ krumpáče.</para>
        ///
        /// <para>Vrací false i když singleton ještě neexistuje, takže se na ni dá ptát
        /// z <c>Update</c> kterékoli komponenty bez ohledu na pořadí inicializace.</para>
        /// </summary>
        public static bool HoldingPickaxe => HeldTool != null && HeldTool.toolType == ToolType.Pickaxe;

        /// <summary>
        /// Nástroj v ruce. Primárně z tohohle singletonu, se zálohou v hotbaru.
        ///
        /// <para><b>Ta záloha není opatrnictví.</b> <see cref="EquippedTool"/> se plní z
        /// <c>HotbarController</c> při změně slotu – jenže hotbar volá <c>SetActiveSlot(0)</c>
        /// ve svém <c>Start</c>, a hráč se do scény spawnuje až z vlastní rutiny. Když se
        /// trefí do tohohle pořadí, proběhne první nastavení slotu dřív, než tenhle singleton
        /// vůbec existuje, a <c>EquippedTool</c> zůstane prázdný, dokud hráč nezmění slot.
        /// Projevilo by se to jako „krumpáč v ruce mám, ale kopat to nejde".</para>
        /// </summary>
        public static InventoryItemData HeldTool
        {
            get
            {
                InventoryItemData item = Instance != null ? Instance.EquippedTool : null;
                if (item != null) return item;

                return HotbarController.Instance != null ? HotbarController.Instance.GetHeldItem() : null;
            }
        }

        /// <summary>Singleton inicializace.</summary>
        private void Awake()
        {
            Instance = this;
        }

        /// <summary>
        /// Nastaví aktuálně vybavený nástroj.
        /// Volá se z HotbarController při každé změně aktivního slotu.
        /// </summary>
        /// <param name="item">Nový vybavený nástroj, nebo null pro prázdnou ruku.</param>
        public void EquipTool(InventoryItemData item)
        {
            EquippedTool = item;
        }
    }
}