// BiomeData.cs
using Orivilon.World.Spawning;
using System;
using System.Collections.Generic;
using UnityEngine;

namespace Orivilon.Data
{
    /// <summary>
    /// Definice jednoho typu terénu v rámci biomu.
    /// Každý biom může mít více typů terénu oddělených výškovými prahy (altitude).
    /// </summary>
    [System.Serializable]
    public class TerrainType
    {
        /// <summary>Název terénu pro přehlednost v editoru (např. "Water", "Grass", "Rock").</summary>
        public string name;

        /// <summary>
        /// Výškový práh – body s hodnotou noise pod tímto číslem patří do tohoto terénu.
        /// Typy jsou seřazeny vzestupně podle altitude.
        /// </summary>
        public float altitude;

        /// <summary>Pokud true, výška tohoto terénu se náhodně variuje v rozsahu variance.</summary>
        [Tooltip("Does this terrain type vary in height?")]
        public bool varyHeight = false;

        /// <summary>Maximální násobitel variace výšky (pokud je varyHeight zapnuto).</summary>
        [Tooltip("Variance in height for this terrain type")]
        public float variance = 1;

        /// <summary>Násobitel aplikovaný na výškovou mapu pro tvarování terénu tohoto typu.</summary>
        public float heightMultiplier;

        /// <summary>Barva přiřazená tomuto typu terénu v barevné mapě meshe.</summary>
        public Color color;
    }

    /// <summary>
    /// ScriptableObject definující vlastnosti jednoho biomu.
    /// Obsahuje klimatické parametry (teplota, vlhkost), seznam typů terénu
    /// a faktory prolínání barev a výšek na hranicích terénu.
    /// Vytváří se v editoru přes menu "Bioms/BiomeData".
    /// </summary>
    [CreateAssetMenu(fileName = "Biome Data", menuName = "Bioms/BiomeData")]
    public class BiomeData : ScriptableObject
    {
        /// <summary>Zobrazovaný název biomu (např. "Grassland", "Desert").</summary>
        [Header("Biome Settings")]
        public string biomeName;

        /// <summary>Typ biomu jako výčtová hodnota pro použití v spawn systému.</summary>
        public BiomeType biomeType;

        /// <summary>Cílová vlhkost biomu (0 = sucho, 1 = mokro). Porovnává se s generovanou vlhkostí bodu.</summary>
        public float humidity;

        /// <summary>Cílová teplota biomu (0 = zima, 1 = teplo). Porovnává se s generovanou teplotou bodu.</summary>
        public float temperature;

        /// <summary>Seznam typů terénu seřazených podle výškového prahu.</summary>
        [Header("Terrain Settings")]
        public List<TerrainType> terrainTypes = new();

        /// <summary>Jak silně se prolínají barvy sousedních typů terénu na jejich hranici (0 = žádné prolínání).</summary>
        public float colorBlendFactor = 0.1f;

        /// <summary>Jak silně se prolínají výšky sousedních typů terénu na jejich hranici (0 = žádné prolínání).</summary>
        public float heightBlendFactor = 0.1f;

        /// <summary>Seřazená kopie terrainTypes použitá při výpočtech (třídí se automaticky).</summary>
        private List<TerrainType> sortedTerrainTypes;

        /// <summary>Příznak, zda byl seznam terénu již seřazen (resetuje se při OnValidate).</summary>
        bool isSorted = false;

        /// <summary>Interní generátor náhodných čísel pro variaci výšky.</summary>
        private System.Random rng = new System.Random();

        /// <summary>Vrátí náhodné číslo v daném rozsahu z interního generátoru.</summary>
        private float Random(float min, float max) => (float)rng.NextDouble() * (max - min) + min;

        /// <summary>
        /// Vypočítá faktor prolínání mezi dvěma sousedními typy terénu pro danou výšku.
        /// Vrací hodnotu 0–1 reprezentující, jak daleko je výška od prahu closestTerrain k prahu blendTerrain.
        /// </summary>
        private float GetBlendFactor(TerrainType closestTerrain, TerrainType blendTerrain, float height)
        {
            return Mathf.InverseLerp(closestTerrain.altitude, blendTerrain.altitude, height);
        }

        /// <summary>
        /// Seřadí typy terénu vzestupně podle výškového prahu.
        /// Nutné pro správnou funkci GetClosestTerrainIndex. Voláno automaticky při OnValidate.
        /// </summary>
        public void SortTerrains()
        {
            sortedTerrainTypes = new List<TerrainType>(terrainTypes);
            sortedTerrainTypes.Sort((a, b) => a.altitude.CompareTo(b.altitude));
        }

        /// <summary>
        /// Vrátí barvu terénu pro danou výšku s volitelným prolínáním do sousedního typu.
        /// Pokud má výška sousední typ, výsledná barva se interpoluje mezi oběma.
        /// </summary>
        /// <param name="height">Výšková hodnota z noise mapy (0–1).</param>
        /// <returns>Výsledná barva pro daný bod.</returns>
        public Color GetColor(float height)
        {
            int closestTerrainIndex = GetClosestTerrainIndex(height);
            TerrainType closestTerrain = sortedTerrainTypes[closestTerrainIndex];

            TerrainType blendTerrain = GetBlendTerrain(closestTerrainIndex);

            if (blendTerrain == closestTerrain)
            {
                return closestTerrain.color;
            }

            float factor = GetBlendFactor(closestTerrain, blendTerrain, height) * colorBlendFactor;

            return Color.Lerp(closestTerrain.color, blendTerrain.color, factor);
        }

        /// <summary>
        /// Vrátí výškový násobitel pro daný bod s volitelnou variací a prolínáním.
        /// Pokud má typ terénu zapnutou variaci, násobitel se náhodně mění v rozsahu variance.
        /// </summary>
        /// <param name="x">X souřadnice bodu v rámci chunku.</param>
        /// <param name="y">Y souřadnice bodu v rámci chunku (odpovídá Z ve světových souřadnicích).</param>
        /// <param name="data">Výšková mapa předaná referencí.</param>
        /// <returns>Výsledný násobitel výšky pro daný bod.</returns>
        public float GetHeightMultiplier(int x, int y, ref float[,] data)
        {
            float height = data[x, y];
            int closestTerrainIndex = GetClosestTerrainIndex(height);
            TerrainType closestTerrain = sortedTerrainTypes[closestTerrainIndex];

            TerrainType blendTerrain = GetBlendTerrain(closestTerrainIndex);

            float heightMultiplier = closestTerrain.heightMultiplier * (closestTerrain.varyHeight ? Random(1, closestTerrain.variance) : 1);

            if (blendTerrain != closestTerrain)
            {
                float factor = GetBlendFactor(closestTerrain, blendTerrain, height) * heightBlendFactor;

                float blendHeight = blendTerrain.heightMultiplier * (blendTerrain.varyHeight ? Random(1, blendTerrain.variance) : 1);

                heightMultiplier = Mathf.Lerp(heightMultiplier, blendHeight, factor);
            }

            return heightMultiplier;
        }

        /// <summary>
        /// Označí seznam terénu jako neseřazený při editaci v inspektoru.
        /// Zajišťuje, že se seznam znovu seřadí při příštím přístupu.
        /// </summary>
        private void OnValidate()
        {
            isSorted = false;
        }

        /// <summary>
        /// Najde index nejvhodnějšího typu terénu pro danou výšku.
        /// Pokud seznam není seřazen, seřadí ho. Vrátí index prvního terénu,
        /// jehož altitude je větší nebo roven výšce bodu.
        /// </summary>
        /// <param name="height">Výšková hodnota z noise mapy (0–1).</param>
        /// <returns>Index v seřazeném seznamu sortedTerrainTypes.</returns>
        public int GetClosestTerrainIndex(float height)
        {
            if (!isSorted)
            {
                SortTerrains();
                isSorted = true;
            }

            int index = 0;
            bool found = false;

            if (height <= sortedTerrainTypes[0].altitude)
            {
                return 0;
            }
            else if (height >= sortedTerrainTypes[sortedTerrainTypes.Count - 1].altitude)
            {
                return sortedTerrainTypes.Count - 1;
            }

            for (int i = 1; i < sortedTerrainTypes.Count; i++)
            {
                if (height <= sortedTerrainTypes[i].altitude)
                {
                    index = i - 1;
                    found = true;
                    break;
                }
            }
            if (!found)
            {
                index = sortedTerrainTypes.Count - 1;
            }

            return index;
        }

        /// <summary>
        /// Vrátí sousední (vyšší) typ terénu pro prolínání.
        /// Pokud je index poslední prvek seznamu, vrátí ten samý typ (žádné prolínání).
        /// </summary>
        /// <param name="index">Index aktuálního typu terénu.</param>
        /// <returns>Sousední typ terénu nebo stejný typ při hranici seznamu.</returns>
        public TerrainType GetBlendTerrain(int index)
        {
            return index == sortedTerrainTypes.Count - 1 ? sortedTerrainTypes[index] : sortedTerrainTypes[index + 1];
        }
    }
}