using UnityEngine;
using System.Collections.Generic;
using Orivilon.Inventory.Inventory;

namespace Orivilon.Inventory.Crafting
{
    /// <summary>
    /// ScriptableObject definující beztvarou craftovací recepturu.
    /// Na pořadí ingrediencí v gridu nezáleží – důležitý je pouze jejich výčet a počet.
    /// Vytváří se v editoru přes menu "Crafting/Shapeless Recipe".
    /// </summary>
    [CreateAssetMenu(menuName = "Crafting/Shapeless Recipe")]
    public class ShapelessRecipe : ScriptableObject
    {
        /// <summary>
        /// Seznam potřebných ingrediencí. Duplicity jsou povoleny (např. 2× Wood = dvakrát Wood v listu).
        /// </summary>
        public List<InventoryItemData> ingredients;

        /// <summary>Item, který crafting vytvoří po splnění ingrediencí.</summary>
        public InventoryItemData result;

        /// <summary>Počet kusů výsledného itemu vytvořených jedním craftem.</summary>
        public int resultAmount = 1;
    }
}