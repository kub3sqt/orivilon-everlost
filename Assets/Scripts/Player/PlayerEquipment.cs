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