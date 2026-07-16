using UnityEngine;
using UnityEngine.UI;
using System.Collections;
using System.Collections.Generic;

namespace Orivilon.Interaction
{
    /// <summary>
    /// Nastaví přesný pixel-perfect hitbox pro UI tlačítko s průhlednou texturou.
    /// Při startu nastaví alphaHitTestMinimumThreshold na 0.5, takže
    /// kliknutí na průhlednou část sprite se nepovažuje za kliknutí na tlačítko.
    /// Vyžaduje, aby textura sprite měla povolenou možnost "Read/Write" v importu.
    /// </summary>
    public class ButtonHitbox : MonoBehaviour
    {
        /// <summary>Image komponenta, na které se nastaví threshold pro průhlednost.</summary>
        public Image img;

        void Start()
        {
            img = GetComponent<Image>();
            img.alphaHitTestMinimumThreshold = 0.5f;
        }
    }
}