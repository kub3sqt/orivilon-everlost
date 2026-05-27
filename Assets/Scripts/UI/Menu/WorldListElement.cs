using UnityEngine;
using UnityEngine.UI;

namespace Orivilon.UI.Menu
{
    /// <summary>
    /// Nastavuje vizuální styl položky v seznamu světů podle její pozice v seznamu.
    /// Používá tři různé sprite pro pozadí: první položka, prostřední položky a poslední položka.
    /// Pokud existuje jen jeden svět, použije se sprite prostřední položky.
    /// Volá se z MainMenuUI.CreateWorldListItem() ihned po instantiaci prefabu.
    /// </summary>
    public class WorldListElement : MonoBehaviour
    {
        /// <summary>Image komponenta pozadí položky, jejíž sprite se mění.</summary>
        [Header("Background Image")]
        [SerializeField] private Image backgroundImage;

        /// <summary>Sprite používaný pro první položku v seznamu.</summary>
        [Header("Background Sprites")]
        [SerializeField] private Sprite firstSprite;

        /// <summary>Sprite používaný pro prostřední položky v seznamu.</summary>
        [SerializeField] private Sprite middleSprite;

        /// <summary>Sprite používaný pro poslední položku v seznamu.</summary>
        [SerializeField] private Sprite lastSprite;

        /// <summary>
        /// Nastaví sprite pozadí podle pozice v seznamu.
        /// Jediný svět dostane middleSprite. První prvek firstSprite, poslední lastSprite,
        /// všechny ostatní middleSprite.
        /// </summary>
        /// <param name="index">Index této položky v seznamu (0 = první).</param>
        /// <param name="totalCount">Celkový počet položek v seznamu.</param>
        public void SetVisual(int index, int totalCount)
        {
            if (backgroundImage == null) return;

            if (totalCount == 1)
            {
                backgroundImage.sprite = middleSprite;
                return;
            }

            if (index == 0)
            {
                backgroundImage.sprite = firstSprite;
                return;
            }

            if (index == totalCount - 1)
            {
                backgroundImage.sprite = lastSprite;
                return;
            }

            backgroundImage.sprite = middleSprite;
        }
    }
}