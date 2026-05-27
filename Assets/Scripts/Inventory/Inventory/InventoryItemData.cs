using Orivilon.Player;
using UnityEngine;

namespace Orivilon.Inventory.Inventory
{
    /// <summary>
    /// ScriptableObject definující datovou podobu jednoho herního itemu.
    /// Neobsahuje žádnou logiku ani UI – pouze konfigurační hodnoty.
    /// Vytváří se v editoru přes menu "Inventory/Inventory Item".
    /// </summary>
    [CreateAssetMenu(menuName = "Inventory/Inventory Item")]
    public class InventoryItemData : ScriptableObject
    {
        /// <summary>Unikátní textové ID itemu používané při ukládání a načítání inventáře.</summary>
        public string id;

        /// <summary>Ikona itemu zobrazovaná v inventáři, hotbaru a na kurzoru.</summary>
        public Sprite icon;

        /// <summary>Zobrazovaný název itemu (v popisu, interakčním textu apod.).</summary>
        public string itemName;

        /// <summary>Textový popis itemu zobrazovaný v ItemDescriptionUI při najetí myší.</summary>
        [TextArea(2, 6)]
        public string description;

        /// <summary>Pokud true, kusy itemu lze skládat do jednoho slotu až do maxStack.</summary>
        [Header("Stacking")]
        public bool stackable = true;

        /// <summary>Maximální počet kusů itemu v jednom inventářním slotu.</summary>
        public int maxStack = 64;

        /// <summary>
        /// Prefab 3D modelu zobrazovaného v ruce hráče.
        /// Zpracovává HandItemRenderer – fyzikální komponenty se z něj automaticky odstraní.
        /// </summary>
        [Header("3D Model v ruce")]
        [Tooltip("Prefab 3D modelu, který se zobrazí v ruce hráče")]
        public GameObject handPrefab;

        /// <summary>Lokální pozice modelu itemu relativně k handAnchor bodu ruky.</summary>
        [Header("Hand Transform Offset")]
        [Tooltip("Lokální pozice itemu v ruce")]
        public Vector3 handPositionOffset;

        /// <summary>Lokální rotace modelu itemu v ruce (Eulerovy úhly).</summary>
        [Tooltip("Lokální rotace itemu v ruce (Euler)")]
        public Vector3 handRotationOffset;

        /// <summary>Lokální měřítko modelu itemu v ruce.</summary>
        [Tooltip("Lokální měřítko itemu v ruce")]
        public Vector3 handScale = Vector3.one;

        /// <summary>
        /// Typ nástroje itemu. Určuje, které těžitelné objekty (HarvestableObject) lze tímto itemem těžit.
        /// Hodnota None = item není nástroj.
        /// </summary>
        [Header("Tool")]
        public ToolType toolType = ToolType.None;

        /// <summary>Příznak, zda lze item sníst/vypít (použití pravým tlačítkem).</summary>
        [Header("Food")]
        public bool isFood;

        /// <summary>Množství hladu doplněného po konzumaci (0 = žádné).</summary>
        public float foodRestore;

        /// <summary>Množství žízně doplněné po konzumaci (0 = žádné).</summary>
        public float waterRestore;
    }
}