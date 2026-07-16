using Orivilon.Inventory.Inventory;
using System.Collections.Generic;
using UnityEngine;

namespace Orivilon.Building
{
    /// <summary>
    /// ScriptableObject obsahující veškerá data o jednom stavebním dílu.
    /// Vytváří se v editoru přes menu "Building/Piece".
    /// Definuje vzhled, typ, pravidla připojování, podporu, cenu a umístění dílu.
    /// </summary>
    [CreateAssetMenu(menuName = "Building/Piece")]
    public class BuildingPieceData : ScriptableObject
    {
        /// <summary>
        /// Unikátní textový identifikátor dílu.
        /// Používá se při ukládání a načítání postavených konstrukcí.
        /// </summary>
        [Header("Identification")]
        public string id;

        /// <summary>
        /// Prefab skutečného dílu, který se vloží do světa po potvrzení stavby.
        /// </summary>
        [Header("Prefabs")]
        public GameObject prefab;

        /// <summary>
        /// Prefab průhledného náhledu zobrazovaného před umístěním dílu.
        /// Hráč vidí, kam díl přesně půjde, dříve než klikne.
        /// </summary>
        public GameObject previewPrefab;

        /// <summary>
        /// Typ dílu (Foundation, Floor, Wall, Pillar).
        /// Typ ovlivňuje pravidla stability a kompatibilitu se sockety.
        /// </summary>
        [Header("Type")]
        public BuildingPieceType type;

        /// <summary>
        /// Seznam typů socketů, na které lze tento díl umístit.
        /// Díl se přichytí pouze k socketu, jehož typ je v tomto seznamu.
        /// </summary>
        [Header("Sockets")]
        public List<SocketType> allowedSockets;

        /// <summary>
        /// Maximální počet kroků (přes sousední díly) k nejbližšímu Foundation dílu.
        /// Díl bez cesty k Foundation v tomto limitu je považován za nestabilní.
        /// </summary>
        [Header("Support")]
        public int maxSupportDistance = 3;

        /// <summary>
        /// Pole itemů potřebných ke stavbě tohoto dílu.
        /// Index odpovídá hodnotě ve stejném indexu pole <see cref="costAmounts"/>.
        /// </summary>
        [Header("Cost")]
        public InventoryItemData[] costItems;

        /// <summary>
        /// Počty kusů každého požadovaného materiálu.
        /// Index odpovídá hodnotě ve stejném indexu pole <see cref="costItems"/>.
        /// </summary>
        public int[] costAmounts;

        /// <summary>
        /// Rotační offset aplikovaný při přichycení dílu k socketu.
        /// Slouží k opravě orientace vůči směru socketu.
        /// </summary>
        [Header("Placement")]
        public Vector3 rotationOffset;

        /// <summary>
        /// Poziční offset aplikovaný při přichycení dílu k socketu.
        /// Umožňuje jemné doladění výsledné polohy dílu.
        /// </summary>
        public Vector3 positionOffset;

        /// <summary>
        /// Povoluje umístění dílu přímo na terén bez nutnosti socketu.
        /// Typicky zapnuto pouze pro základové desky (Foundation).
        /// </summary>
        public bool allowTerrainPlacement;
    }
}