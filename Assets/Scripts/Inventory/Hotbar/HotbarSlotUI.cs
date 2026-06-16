using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Orivilon.Inventory.Hotbar
{
    /// <summary>
    /// UI element jednoho slotu v hotbaru.
    /// Kliknutí na slot ho nastaví jako aktivní přes HotbarController.
    /// Vizuální zvýraznění aktivního slotu se řídí zobrazením/skrytím outline image.
    /// </summary>
    public class HotbarSlotUI : MonoBehaviour, IPointerClickHandler
    {
        /// <summary>
        /// Image rámečku (outline) zobrazovaná pouze pro aktivní slot.
        /// Skrytím/zobrazením tohoto objektu se indikuje aktivní slot.
        /// </summary>
        [SerializeField] private Image outlineImage;

        /// <summary>
        /// Při kliknutí na slot zjistí jeho pořadí mezi sourozenci
        /// a nastaví ho jako aktivní přes HotbarController.
        /// </summary>
        public void OnPointerClick(PointerEventData eventData)
        {
            int index = transform.GetSiblingIndex();
            HotbarController.Instance.SetActiveSlot(index);
        }

        /// <summary>
        /// Zobrazí nebo skryje outline rámeček aktivního slotu.
        /// Volá se z HotbarController při každé změně aktivního slotu.
        /// </summary>
        /// <param name="active">True = slot je aktivní (outline viditelný), False = slot je neaktivní.</param>
        public void SetActive(bool active)
        {
            if (outlineImage != null)
                outlineImage.gameObject.SetActive(active);
        }
    }
}