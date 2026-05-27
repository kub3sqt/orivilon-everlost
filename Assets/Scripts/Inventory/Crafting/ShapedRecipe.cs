using Orivilon.Inventory.Inventory;
using UnityEngine;

namespace Orivilon.Inventory.Crafting
{
    /// <summary>
    /// ScriptableObject definující tvarovanou craftovací recepturu.
    /// Vzor je zadán jako jednorozměrné pole (row-major) o velikosti width × height.
    /// Null hodnota v poli znamená prázdnou buňku vzoru.
    /// Vytváří se v editoru přes menu "Crafting/Shaped Recipe".
    /// </summary>
    [CreateAssetMenu(menuName = "Crafting/Shaped Recipe")]
    public class ShapedRecipe : ScriptableObject
    {
        /// <summary>
        /// Vzor receptury jako jednorozměrné pole (row-major, zleva doprava, shora dolů).
        /// Délka musí být width × height. Null = prázdná buňka.
        /// </summary>
        [Header("Pattern (row-major, min size)")]
        public InventoryItemData[] pattern;

        /// <summary>Šířka vzoru v počtu slotů (max 3 pro grid 3×3).</summary>
        public int width;

        /// <summary>Výška vzoru v počtu slotů (max 3 pro grid 3×3).</summary>
        public int height;

        /// <summary>Item, který crafting vytvoří po splnění vzoru.</summary>
        [Header("Result")]
        public InventoryItemData result;

        /// <summary>Počet kusů výsledného itemu vytvořených jedním craftem.</summary>
        public int resultAmount = 1;

        /// <summary>
        /// Pokud true, vzor se testuje i v horizontálně zrcadlené variantě.
        /// Vhodné pro symetrické předměty jako sekera (true) nebo nesymetrické jako krumpáč (false).
        /// </summary>
        [Header("Rules")]
        public bool allowMirror = false;
    }
}