using Orivilon.Inventory.Inventory;
using Orivilon.SaveSystem;
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

            // Vyhozený předmět (DroppedItem): ochrana po vyhození a sebrání po částech – co se do inventáře
            // nevejde, zůstane ve světě se sníženým počtem. Nemá DeterministicObjectId ani registr zničených.
            var dropped = GetComponent<DroppedItem>();
            if (dropped != null)
            {
                if (!dropped.CanPickUp || itemData == null || itemCount <= 0) return;
                // Multiplayer klient: kusy přidělí host (žádost s volným místem), lokálně se nic nemění.
                if (dropped.IsReplica) { Orivilon.Multiplayer.NetworkWorldSync.Instance?.RequestPickup(dropped); return; }
                int taken = InventoryData.Instance.AddItemUpTo(itemData, itemCount);
                if (taken <= 0) { Debug.Log("Inventář je plný – item nelze sebrat"); return; }
                itemCount -= taken;
                Orivilon.Multiplayer.NetworkWorldSync.Instance?.HostDropCountChanged(dropped);   // host: ostatním nový počet
                if (itemCount <= 0) dropped.Consume();
                return;
            }

            bool added;
            var mpId = GetComponent<DeterministicObjectId>();
            if (Orivilon.Multiplayer.MpLedger.ClientActive)
            {
                // Multiplayer klient (kolo MP 3): jen nárok. Objekt zůstává a nic se nepřipíše, dokud host
                // neověří skutečné osazení – při kladném verdiktu ho zničí NetworkWorldSync (OnLootResult).
                if (mpId == null || !mpId.HasKey || itemData == null) return;
                if (InventoryData.SpaceFor(InventoryData.Instance.Slots, 0, InventoryData.Instance.Slots.Length - 1, itemData) < itemCount)
                { Debug.Log("Inventář je plný – item nelze sebrat"); return; }
                Orivilon.Multiplayer.MpLedger.ClaimLoot(Orivilon.Multiplayer.MpLedger.LootKind.Pickup, mpId.ChunkCoord, mpId.KeyA, mpId.KeyB, mpId.KeyCode, transform.position);
                return;
            }
            else added = InventoryData.Instance.AddItem(itemData, itemCount);

            if (added)
            {
                Orivilon.SaveSystem.SaveSystem.MarkObjectDestroyed(gameObject);

                // Multiplayer: oznam ostatním hráčům sebrání (no-op v single-playeru).
                var detId = GetComponent<DeterministicObjectId>();
                if (detId != null)
                    Orivilon.Multiplayer.NetworkWorldSync.Instance?.BroadcastObjectDestroyed(detId.Hash);

                Destroy(gameObject);
            }
            else
            {
                Debug.Log("Inventář je plný – item nelze sebrat");
            }
        }
    }
}
