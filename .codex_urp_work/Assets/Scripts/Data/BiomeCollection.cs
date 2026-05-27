using Orivilon.World.Biomes;
using Orivilon.World.Spawning;
using Orivilon.World.Terrain;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Orivilon.Data
{
    /// <summary>
    /// ScriptableObject sbírky všech biomů ve hře.
    /// Poskytuje metody pro výběr biomu podle teploty a vlhkosti,
    /// výpočet váhovaných výšek a barev terénu a generování biom mapy pro chunk.
    /// Vytváří se v editoru přes menu "Bioms/Biome Collection".
    /// </summary>
    [CreateAssetMenu(fileName = "Biome Collection", menuName = "Bioms/Biome Collection")]
    public class BiomeCollection : ScriptableObject
    {
        /// <summary>Velikost jednoho chunku v jednotkách (musí souhlasit s EndlessTerrain.chunkSize).</summary>
        public int chunkSize;

        /// <summary>Pole všech dostupných biomů. Pořadí ovlivňuje fallback při prázdném výsledku.</summary>
        public BiomeData[] biomes;

        /// <summary>
        /// Síla prolínání biomů. Vyšší hodnota = ostřejší přechody, nižší = plynulejší.
        /// Používá se jako exponent při výpočtu váhy každého biomu.
        /// </summary>
        public int biomeBlendingFactor = 10;

        /// <summary>
        /// Nastaví faktor prolínání biomů. Volá se ze slider UI v debug panelu.
        /// </summary>
        /// <param name="value">Nová hodnota faktoru prolínání.</param>
        public void SetBiomeBlendingFactor(int value) => biomeBlendingFactor = value;

        /// <summary>
        /// Vybere nejvhodnější biom pro danou světovou souřadnici.
        /// Zjistí teplotu a vlhkost přes BiomeValuesGenerator a porovná je se všemi biomy.
        /// Vrátí biom s nejmenší odchylkou (Manhattan distance v prostoru teplota/vlhkost).
        /// </summary>
        /// <param name="x">Lokální X souřadnice v rámci chunku.</param>
        /// <param name="z">Lokální Z souřadnice v rámci chunku.</param>
        /// <param name="chunkCoord">Souřadnice chunku pro převod na světové souřadnice.</param>
        /// <returns>Nejvhodnější biom, nebo první biom v poli jako záložní hodnota.</returns>
        public BiomeData ChooseBiome(int x, int z, Vector2Int chunkCoord)
        {
            float temperature = BiomeValuesGenerator.instance.GetTemperature(x + chunkCoord.x * chunkSize, z + chunkCoord.y * chunkSize);
            float humidity = BiomeValuesGenerator.instance.GetHumidity(x + chunkCoord.x * chunkSize, z + chunkCoord.y * chunkSize);

            BiomeData bestBiome = null;
            float bestMatch = float.MaxValue;

            for (int i = 0; i < biomes.Length; i++)
            {
                float match = GetBiomeMatchValue(biomes[i], temperature, humidity);
                if (match < bestMatch)
                {
                    bestMatch = match;
                    bestBiome = biomes[i];
                }
            }
            return bestBiome ?? biomes[0];
        }

        /// <summary>
        /// Vypočítá váhované výšky a barvy pro každý bod chunku.
        /// Pro každý bod zjistí teplotu a vlhkost, pak váhovaně prolíná příspěvky
        /// všech biomů podle jejich vzdálenosti v teplotně-vlhkostním prostoru.
        /// Biomy s malým příspěvkem (váha pod 0.001) se přeskočí pro výkon.
        /// Optimalizováno bez LINQ a bez Dictionary alokací.
        /// </summary>
        /// <param name="chunkCoord">Souřadnice chunku.</param>
        /// <param name="data">Výšková mapa chunku předaná referencí (pro výpočet altitude multiplikátorů).</param>
        /// <returns>Dvojice (altitudeMap, colorMap) pro celý chunk.</returns>
        public Tuple<float[,], Color[,]> GetAltitudeAndColorMap(Vector2Int chunkCoord, ref float[,] data)
        {
            int size = chunkSize + 1;
            float[,] altitudeMap = new float[size, size];
            Color[,] colorMap = new Color[size, size];
            var biomeGen = BiomeValuesGenerator.instance;
            int biomeCount = biomes.Length;

            int globalXStart = chunkCoord.x * chunkSize;
            int globalYStart = chunkCoord.y * chunkSize;

            for (int z = 0; z < size; z++)
            {
                for (int x = 0; x < size; x++)
                {
                    float temp = biomeGen.GetTemperature(x + globalXStart, z + globalYStart);
                    float hum = biomeGen.GetHumidity(x + globalXStart, z + globalYStart);

                    float totalWeight = 0f;
                    float totalAltitude = 0f;
                    float r = 0, g = 0, b = 0;

                    float dataHeight = data[x, z];

                    for (int i = 0; i < biomeCount; i++)
                    {
                        BiomeData bData = biomes[i];

                        float tDiff = bData.temperature - temp;
                        float hDiff = bData.humidity - hum;
                        float match = (tDiff < 0 ? -tDiff : tDiff) + (hDiff < 0 ? -hDiff : hDiff);

                        float weight = 1f / (match + 0.1f);
                        if (biomeBlendingFactor > 1) weight = Mathf.Pow(weight, biomeBlendingFactor);

                        if (weight < 0.001f) continue;

                        totalWeight += weight;
                        totalAltitude += bData.GetHeightMultiplier(x, z, ref data) * weight;

                        Color c = bData.GetColor(dataHeight);
                        r += c.r * weight;
                        g += c.g * weight;
                        b += c.b * weight;
                    }

                    if (totalWeight > 0)
                    {
                        float invW = 1f / totalWeight;
                        altitudeMap[x, z] = totalAltitude * invW;
                        colorMap[x, z] = new Color(r * invW, g * invW, b * invW, 1f);
                    }
                }
            }
            return new Tuple<float[,], Color[,]>(altitudeMap, colorMap);
        }

        /// <summary>
        /// Vypočítá míru shody biomu s danými klimatickými hodnotami.
        /// Nižší hodnota = lepší shoda. Používá Manhattan distance.
        /// </summary>
        /// <param name="biome">Testovaný biom.</param>
        /// <param name="temperature">Aktuální teplota bodu (0–1).</param>
        /// <param name="humidity">Aktuální vlhkost bodu (0–1).</param>
        /// <returns>Míra neshody – čím nižší, tím lépe odpovídá biom.</returns>
        private float GetBiomeMatchValue(BiomeData biome, float temperature, float humidity)
        {
            return Mathf.Abs(biome.temperature - temperature) + Mathf.Abs(biome.humidity - humidity);
        }

        /// <summary>
        /// Vygeneruje mapu typů biomů (BiomeType[,]) pro celý chunk.
        /// Body pod úrovní moře jsou vždy označeny jako TemperateOcean.
        /// Pro ostatní body se vybere biom metodou ChooseBiome.
        /// </summary>
        /// <param name="heightMap">Výšková mapa chunku.</param>
        /// <param name="chunkOrigin">Světová pozice levého dolního rohu chunku.</param>
        /// <param name="seed">Seed světa (momentálně nevyužit, připraven pro budoucí rozšíření).</param>
        /// <returns>Dvourozměrné pole typů biomů pro každý bod chunku.</returns>
        public BiomeType[,] GenerateBiomeMap(float[,] heightMap, Vector3 chunkOrigin, int seed)
        {
            int sizeX = heightMap.GetLength(0);
            int sizeZ = heightMap.GetLength(1);
            BiomeType[,] biomeMap = new BiomeType[sizeX, sizeZ];

            Vector2Int chunkCoord;
            if (MapGenerator.instance != null) chunkCoord = MapGenerator.instance.GetCoord(chunkOrigin);
            else chunkCoord = new Vector2Int(Mathf.FloorToInt(chunkOrigin.x / chunkSize), Mathf.FloorToInt(chunkOrigin.z / chunkSize));

            float seaLvl = (MapGenerator.instance != null) ? MapGenerator.instance.seaLevel : 0f;

            for (int x = 0; x < sizeX; x++)
            {
                for (int z = 0; z < sizeZ; z++)
                {
                    if (heightMap[x, z] < seaLvl)
                    {
                        biomeMap[x, z] = BiomeType.TemperateOcean;
                        continue;
                    }
                    BiomeData b = ChooseBiome(x, z, chunkCoord);
                    biomeMap[x, z] = (b != null) ? b.biomeType : BiomeType.None;
                }
            }
            return biomeMap;
        }

        /// <summary>
        /// Při změně hodnot v editoru seřadí terény každého biomu podle nadmořské výšky.
        /// Zajišťuje správnou funkci GetClosestTerrainIndex bez nutnosti ručního třídění.
        /// </summary>
        private void OnValidate()
        {
            if (biomes == null) return;
            foreach (var b in biomes) if (b != null) b.SortTerrains();
        }
    }
}