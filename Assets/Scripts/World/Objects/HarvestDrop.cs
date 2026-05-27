using Orivilon.Inventory.Inventory;
using UnityEngine;

namespace Orivilon.World.Objects
{
    /// <summary>
    /// Serializovatelná třída definující jeden možný drop z těžitelného objektu.
    /// Každý HarvestableObject může mít více dropů s různými itemy, množstvími a pravděpodobnostmi.
    /// Při těžení se pro každý drop provede hod na pravděpodobnost a náhodný výběr množství.
    /// </summary>
    [System.Serializable]
    public class HarvestDrop
    {
        /// <summary>Item, který může z těžení vypadnout.</summary>
        [Tooltip("Item, který může padnout")]
        public InventoryItemData item;

        /// <summary>Minimální počet kusů dropu (včetně).</summary>
        [Tooltip("Minimální počet kusů")]
        public int minAmount = 1;

        /// <summary>Maximální počet kusů dropu (včetně).</summary>
        [Tooltip("Maximální počet kusů")]
        public int maxAmount = 1;

        /// <summary>
        /// Pravděpodobnost dropu jako číslo 0–1.
        /// Hodnota 1 = drop vždy, 0.5 = drop v 50 % případů, 0 = nikdy.
        /// </summary>
        [Range(0f, 1f)]
        [Tooltip("Pravděpodobnost dropu (1 = vždy)")]
        public float chance = 1f;

        /// <summary>
        /// Vygeneruje náhodný počet kusů v rozsahu minAmount–maxAmount (oba včetně).
        /// Pokud je maxAmount menší než minAmount, opraví ho na minAmount.
        /// </summary>
        /// <returns>Náhodný počet kusů dropu.</returns>
        public int GetAmount()
        {
            if (maxAmount < minAmount)
                maxAmount = minAmount;

            return Random.Range(minAmount, maxAmount + 1);
        }

        /// <summary>
        /// Rozhodne náhodně, zda drop vypadne na základě hodnoty chance.
        /// Porovnává Random.value (0–1) s nastavenou pravděpodobností.
        /// </summary>
        /// <returns>True, pokud drop vypadne.</returns>
        public bool RollChance()
        {
            return Random.value <= chance;
        }
    }
}