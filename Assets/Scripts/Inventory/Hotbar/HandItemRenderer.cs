using Orivilon.Inventory.Inventory;
using UnityEngine;

namespace Orivilon.Inventory.Hotbar
{
    /// <summary>
    /// Singleton zobrazující 3D model itemu v ruce hráče.
    /// Při změně aktivního slotu hotbaru obdrží nový item přes SetItem()
    /// a okamžitě přepne zobrazený model. Fyzikální komponenty (Collider, Rigidbody)
    /// se z modelu automaticky odstraní, aby neinterferovaly s fyzikou hráče.
    /// </summary>
    public class HandItemRenderer : MonoBehaviour
    {
        /// <summary>Globální instance singletonu.</summary>
        public static HandItemRenderer Instance;

        /// <summary>
        /// Kotevní bod (Transform) v hierarchii hráče, ke kterému se 3D model připíná.
        /// Typicky umístěn na pravé ruce nebo zbraňovém kloubu.
        /// </summary>
        [Header("Držák itemu")]
        [SerializeField] private Transform handAnchor;

        /// <summary>Aktuálně zobrazená instance 3D modelu itemu.</summary>
        private GameObject currentInstance;

        /// <summary>Data aktuálně drženého itemu (nebo null pokud hráč nic nedrží).</summary>
        private InventoryItemData currentItem;

        /// <summary>Veřejný přístup k datům aktuálně drženého itemu.</summary>
        public InventoryItemData CurrentItem => currentItem;

        /// <summary>
        /// Singleton inicializace – pokud instance již existuje, tento objekt se zničí.
        /// </summary>
        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }

            Instance = this;
        }

        /// <summary>
        /// Nastaví item, jehož 3D model se má zobrazit v ruce.
        /// Zničí starý model, vytvoří nový z handPrefab, aplikuje lokální offsety
        /// pozice, rotace a měřítka z dat itemu a odstraní fyzikální komponenty.
        /// Pokud je item null nebo nemá handPrefab, ruka se pouze vyprázdní.
        /// </summary>
        /// <param name="item">Datová definice itemu k zobrazení, nebo null pro prázdnou ruku.</param>
        public void SetItem(InventoryItemData item)
        {
            currentItem = item;

            Clear();

            if (item == null || item.handPrefab == null)
                return;

            currentInstance = Instantiate(
                item.handPrefab,
                handAnchor
            );

            currentInstance.transform.localPosition = item.handPositionOffset;
            currentInstance.transform.localRotation = Quaternion.Euler(item.handRotationOffset);
            currentInstance.transform.localScale = item.handScale;

            RemovePhysicsComponents(currentInstance);
        }

        /// <summary>
        /// Odstraní všechny Collider a Rigidbody komponenty z drženého modelu a jeho potomků.
        /// Nutné, aby model v ruce neblokoval raycasto ani neovlivňoval fyziku hráče.
        /// </summary>
        /// <param name="root">Kořenový GameObject, ze kterého se komponenty odstraní.</param>
        private void RemovePhysicsComponents(GameObject root)
        {
            var colliders = root.GetComponentsInChildren<Collider>(true);
            foreach (var col in colliders)
            {
                Destroy(col);
            }

            var rigidbodies = root.GetComponentsInChildren<Rigidbody>(true);
            foreach (var rb in rigidbodies)
            {
                Destroy(rb);
            }
        }

        /// <summary>
        /// Zničí aktuálně zobrazený 3D model itemu.
        /// Volá se před každým přepnutím na nový model.
        /// </summary>
        private void Clear()
        {
            if (currentInstance != null)
                Destroy(currentInstance);
        }
    }
}