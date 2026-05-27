using Orivilon.Inventory.Inventory;
using UnityEngine;

namespace Orivilon.World.Objects
{
    /// <summary>
    /// MonoBehaviour reprezentující sebratelný předmět ve světě.
    /// Hráč ho sebere klávesou E pokud je v dosahu interakce (PlayerInteraction).
    /// Po úspěšném přidání do inventáře se objekt zničí.
    /// Pokud je inventář plný, objekt zůstane ve světě.
    /// </summary>
    public class PickupItem : MonoBehaviour
    {
        /// <summary>
        /// Datová definice itemu, který se přidá do inventáře při sebrání.
        /// </summary>
        [Header("Item data")]
        [Tooltip("ScriptableObject item, který se přidá do inventáře")]
        public InventoryItemData itemData;

        /// <summary>Počet kusů itemu přidaných do inventáře při sebrání.</summary>
        public int itemCount = 1;

        /// <summary>
        /// Přidá item do inventáře hráče a zničí tento GameObject.
        /// Pokud InventoryData neexistuje, zaloguje chybu a nic neprovede.
        /// Pokud je inventář plný, item zůstane ve světě a nic se nestane.
        /// </summary>
        public void PickUp()
        {
            if (InventoryData.Instance == null)
            {
                Debug.LogError("InventoryData.Instance je NULL");
                return;
            }

            bool added = InventoryData.Instance.AddItem(itemData, itemCount);

            if (added)
            {
                Destroy(gameObject);
            }
            else
            {
                Debug.Log("Inventář je plný – item nelze sebrat");
            }
        }
    }
}