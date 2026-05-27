using Orivilon.World.Spawning;
using Orivilon.World.Terrain;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Orivilon.World.Biomes
{
    /// <summary>
    /// Singleton generující hodnoty teploty a vlhkosti pro biom systém.
    /// Každá souřadnice světa má deterministickou teplotu a vlhkost vypočtenou
    /// z Perlin noise s offsety odvozenými ze seedu světa.
    /// Tyto hodnoty BiomeCollection používá pro výběr a prolínání biomů.
    /// </summary>
    public class BiomeValuesGenerator : MonoBehaviour
    {
        /// <summary>Globální instance singletonu.</summary>
        public static BiomeValuesGenerator instance;

        /// <summary>Náhodný offset pro noise vlhkosti (generovaný ze seedu).</summary>
        public Vector2 humidityOffset;

        /// <summary>Měřítko noise mapy vlhkosti – větší hodnota = plynulejší přechody.</summary>
        public float humidityScale;

        /// <summary>Náhodný offset pro noise teploty (generovaný ze seedu).</summary>
        public Vector2 temperatureOffset;

        /// <summary>Měřítko noise mapy teploty – větší hodnota = plynulejší přechody.</summary>
        public float temperatureScale;

        /// <summary>Velikost jednoho chunku (synchronizovaná s MapGenerator).</summary>
        public int chunkSize;

        /// <summary>Seed světa převzatý z MapGenerator.instance.seed.</summary>
        public int seed;

        /// <summary>
        /// Inicializuje singletona a generuje náhodné offsety pro teplotu a vlhkost
        /// z deterministického generátoru seedu světa.
        /// </summary>
        private void Start()
        {
            instance = this;
            seed = MapGenerator.instance.seed;
            System.Random prng = new System.Random(seed);

            humidityOffset = new Vector2(prng.Next(10, 1000), prng.Next(10, 1000));
            temperatureOffset = new Vector2(prng.Next(10, 1000), prng.Next(10, 1000));
        }

        /// <summary>
        /// Vrátí hodnotu vlhkosti pro daný 2D bod (přetížení s Vector2).
        /// </summary>
        public float GetHumidity(Vector2 coord) => GetHumidity(coord.x, coord.y);

        /// <summary>
        /// Vrátí hodnotu vlhkosti pro danou světovou souřadnici (0–1).
        /// Vypočítá se jako Perlin noise s humidityOffset a humidityScale.
        /// </summary>
        /// <param name="x">X světová souřadnice.</param>
        /// <param name="y">Z světová souřadnice (předávána jako y parametr).</param>
        /// <returns>Hodnota vlhkosti v rozsahu 0–1.</returns>
        public float GetHumidity(float x, float y)
        {
            float xCoord = x / humidityScale + humidityOffset.x;
            float zCoord = y / humidityScale + humidityOffset.y;

            return Mathf.PerlinNoise(xCoord, zCoord);
        }

        /// <summary>
        /// Vrátí hodnotu teploty pro daný 2D bod (přetížení s Vector2).
        /// </summary>
        public float GetTemperature(Vector2 coord) => GetTemperature(coord.x, coord.y);

        /// <summary>
        /// Vrátí hodnotu teploty pro danou světovou souřadnici (0–1).
        /// Vypočítá se jako Perlin noise s temperatureOffset a temperatureScale.
        /// </summary>
        /// <param name="x">X světová souřadnice.</param>
        /// <param name="y">Z světová souřadnice (předávána jako y parametr).</param>
        /// <returns>Hodnota teploty v rozsahu 0–1.</returns>
        public float GetTemperature(float x, float y)
        {
            float xCoord = x / temperatureScale + temperatureOffset.x;
            float zCoord = y / temperatureScale + temperatureOffset.y;

            return Mathf.PerlinNoise(xCoord, zCoord);
        }

        /// <summary>
        /// Vygeneruje mapu typů biomů pro chunk na dané souřadnici.
        /// Kombinuje teplotní a vlhkostní mapy k určení biomu každého bodu.
        /// Na konci loguje distribuci biomů pro ladění.
        /// </summary>
        /// <param name="coord">Souřadnice chunku v mřížce světa.</param>
        /// <returns>Dvourozměrné pole BiomeType pro každý bod chunku.</returns>
        public BiomeType[,] GenerateBiomeMap(Vector2Int coord)
        {
            int size = MapGenerator.instance.chunkSize + 1;
            BiomeType[,] biomeMap = new BiomeType[size, size];

            float[,] temperature = BiomeValuesGenerator.instance.GenerateTemperatureMap(coord);
            float[,] humidity = BiomeValuesGenerator.instance.GenerateHumidityMap(coord);

            for (int x = 0; x < size; x++)
            {
                for (int z = 0; z < size; z++)
                {
                    float t = temperature[x, z];
                    float h = humidity[x, z];
                }
            }

            int[] biomeCounts = new int[6];
            for (int x = 0; x < size; x++)
            {
                for (int z = 0; z < size; z++)
                {
                    biomeCounts[(int)biomeMap[x, z]]++;
                }
            }

            Debug.Log($"[BiomeMap] chunk {coord}: Grassland={biomeCounts[0]}, Forest={biomeCounts[1]}, Desert={biomeCounts[2]}, Mountain={biomeCounts[3]}, Snow={biomeCounts[4]}, Water={biomeCounts[5]}");

            return biomeMap;
        }

        /// <summary>
        /// Vygeneruje teplotní mapu pro chunk jako 2D pole hodnot 0–1.
        /// Používá Perlin noise s měřítkem odvozeným od noiseScale MapGeneratoru.
        /// Každý chunk má deterministické hodnoty díky seed-based PRNG.
        /// </summary>
        /// <param name="coord">Souřadnice chunku.</param>
        /// <returns>2D pole teplotních hodnot (0–1) pro každý bod chunku.</returns>
        public float[,] GenerateTemperatureMap(Vector2Int coord)
        {
            int size = MapGenerator.instance.chunkSize + 1;
            float[,] map = new float[size, size];
            System.Random prng = new System.Random(MapGenerator.instance.seed + coord.x * 73856093 + coord.y * 19349663);

            float baseScale = MapGenerator.instance.noiseScale * 0.5f;
            for (int x = 0; x < size; x++)
            {
                for (int y = 0; y < size; y++)
                {
                    float nx = (coord.x * size + x + MapGenerator.instance.offset.x) * baseScale;
                    float ny = (coord.y * size + y + MapGenerator.instance.offset.y) * baseScale;
                    float temp = Mathf.PerlinNoise(nx, ny);
                    map[x, y] = Mathf.Clamp01(temp);
                }
            }
            return map;
        }

        /// <summary>
        /// Vygeneruje mapu vlhkosti pro chunk jako 2D pole hodnot 0–1.
        /// Symetrická metoda k GenerateTemperatureMap s jiným seed offsetem.
        /// </summary>
        /// <param name="coord">Souřadnice chunku.</param>
        /// <returns>2D pole hodnot vlhkosti (0–1) pro každý bod chunku.</returns>
        public float[,] GenerateHumidityMap(Vector2Int coord)
        {
            int size = MapGenerator.instance.chunkSize + 1;
            float[,] map = new float[size, size];
            System.Random prng = new System.Random(MapGenerator.instance.seed + coord.x * 83492791 + coord.y * 29765743);

            float baseScale = MapGenerator.instance.noiseScale * 0.5f;
            for (int x = 0; x < size; x++)
            {
                for (int y = 0; y < size; y++)
                {
                    float nx = (coord.x * size + x + MapGenerator.instance.offset.x) * baseScale;
                    float ny = (coord.y * size + y + MapGenerator.instance.offset.y) * baseScale;
                    float hum = Mathf.PerlinNoise(nx, ny);
                    map[x, y] = Mathf.Clamp01(hum);
                }
            }
            return map;
        }
    }
}