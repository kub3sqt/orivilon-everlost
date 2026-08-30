using UnityEngine;
using UnityEngine.Serialization;
using UnityEngine.UI;

namespace Orivilon.UI.Menu
{
    /// <summary>
    /// Vizuál položky v seznamu světů: normální a selected sprite pozadí.
    /// Mezi stavy přepíná MainMenuUI při označování světů.
    /// Volá se z MainMenuUI.OnWorldSelected() / ClearWorldSelection().
    /// </summary>
    public class WorldListElement : MonoBehaviour
    {
        /// <summary>Image komponenta pozadí položky, jejíž sprite se mění.</summary>
        [Header("Background Image")]
        [SerializeField] private Image backgroundImage;

        /// <summary>Sprite neoznačené položky. Prázdné pole se doplní ze spritu Image při Awake.</summary>
        [Header("Background Sprites")]
        [FormerlySerializedAs("middleSprite")]
        [SerializeField] private Sprite normalSprite;

        /// <summary>Sprite označené položky.</summary>
        [SerializeField] private Sprite selectedSprite;

        /// <summary>
        /// Fallback: pokud normální sprite není přiřazen, převezme se aktuální sprite Image,
        /// aby SetSelected(false) vždy mělo k čemu se vrátit.
        /// </summary>
        private void Awake()
        {
            if (normalSprite == null && backgroundImage != null)
                normalSprite = backgroundImage.sprite;
        }

        /// <summary>
        /// Přepne pozadí mezi normálním a selected spritem.
        /// Pokud cílový sprite není přiřazen, sprite se nemění.
        /// </summary>
        /// <param name="selected">True = položka je označená.</param>
        public void SetSelected(bool selected)
        {
            if (backgroundImage == null) return;

            Sprite target = selected ? selectedSprite : normalSprite;

            if (target != null)
                backgroundImage.sprite = target;
        }
    }
}
