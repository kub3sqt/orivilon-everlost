using UnityEngine;

namespace Orivilon.World.Terrain
{
    /// <summary>
    /// Statická třída pro generování multi-oktávové Perlin noise mapy.
    /// Výsledná mapa se normalizuje buď lokálně (min-max v rámci chunku)
    /// nebo globálně (teoretické maximum přes všechny chunky) pro konzistentní výšky.
    /// Optimalizována předpočítáním inverzního měřítka a použitím pole offsetů místo alokace v cyklu.
    /// </summary>
    public static class Noise
    {
        /// <summary>
        /// Mód normalizace výstupní noise mapy.
        /// Local = normalizuje podle minima a maxima v rámci jednoho volání (různé výšky mezi chunky).
        /// Global = normalizuje podle teoretického maxima (stejné výšky napříč celým světem).
        /// </summary>
        public enum NormalizeMode { Local, Global };

        /// <summary>
        /// Vygeneruje 2D Perlin noise mapu s více oktávami.
        /// Každá oktáva přidává detail s nižší amplitudou a vyšší frekvencí.
        /// Seed zajišťuje deterministické offsety pro každou oktávu.
        /// Výsledek je normalizován do rozsahu 0–1 (nebo 0–∞ v Global módu s Clamp).
        /// </summary>
        /// <param name="mapWidth">Šířka výstupní mapy v bodech.</param>
        /// <param name="mapHeight">Výška výstupní mapy v bodech.</param>
        /// <param name="seed">Seed pro deterministické offsety oktáv.</param>
        /// <param name="scale">Měřítko noise (větší = plynulejší).</param>
        /// <param name="octaves">Počet oktáv (více = detailnější).</param>
        /// <param name="persistance">Pokles amplitudy každé oktávy (0–1).</param>
        /// <param name="lacunarity">Nárůst frekvence každé oktávy (>1).</param>
        /// <param name="offset">Posun noise mapy pro variaci mezi světy.</param>
        /// <param name="normalizeMode">Mód normalizace (Local nebo Global).</param>
        /// <returns>2D pole hodnot noise normalizovaných do rozsahu 0–1.</returns>
        public static float[,] GenerateNoiseMap(int mapWidth, int mapHeight, int seed, float scale, int octaves, float persistance, float lacunarity, Vector2 offset, NormalizeMode normalizeMode)
        {
            float[,] noiseMap = new float[mapWidth, mapHeight];
            System.Random prng = new System.Random(seed);

            Vector2[] octaveOffsets = new Vector2[octaves];

            float maxPossibleHeight = 0;
            float amplitude = 1;
            float frequency = 1;

            for (int i = 0; i < octaves; i++)
            {
                float offsetX = prng.Next(1000, 100000) + offset.x;
                float offsetY = prng.Next(1000, 100000) + offset.y;
                octaveOffsets[i] = new Vector2(offsetX, offsetY);

                maxPossibleHeight += amplitude;
                amplitude *= persistance;
            }

            if (scale <= 0) scale = 0.0001f;

            float maxLocalNoiseHeight = float.MinValue;
            float minLocalNoiseHeight = float.MaxValue;

            float halfWidth = mapWidth / 2f;
            float halfHeight = mapHeight / 2f;

            float invScale = 1f / scale;

            for (int y = 0; y < mapHeight; y++)
            {
                for (int x = 0; x < mapWidth; x++)
                {
                    amplitude = 1;
                    frequency = 1;
                    float noiseHeight = 0;

                    for (int i = 0; i < octaves; i++)
                    {
                        float sampleX = (x - halfWidth + octaveOffsets[i].x) * invScale * frequency;
                        float sampleY = (y - halfHeight + octaveOffsets[i].y) * invScale * frequency;

                        float perlinValue = Mathf.PerlinNoise(sampleX, sampleY) * 2 - 1;
                        noiseHeight += perlinValue * amplitude;

                        amplitude *= persistance;
                        frequency *= lacunarity;
                    }

                    if (noiseHeight > maxLocalNoiseHeight) maxLocalNoiseHeight = noiseHeight;
                    if (noiseHeight < minLocalNoiseHeight) minLocalNoiseHeight = noiseHeight;

                    noiseMap[x, y] = noiseHeight;
                }
            }

            if (normalizeMode == NormalizeMode.Local)
            {
                float delta = maxLocalNoiseHeight - minLocalNoiseHeight;
                float invDelta = (delta == 0) ? 1 : 1f / delta;

                for (int y = 0; y < mapHeight; y++)
                {
                    for (int x = 0; x < mapWidth; x++)
                    {
                        noiseMap[x, y] = Mathf.Clamp01((noiseMap[x, y] - minLocalNoiseHeight) * invDelta);
                    }
                }
            }
            else
            {
                float normalizeFactor = 1f / (maxPossibleHeight / 0.9f);
                for (int y = 0; y < mapHeight; y++)
                {
                    for (int x = 0; x < mapWidth; x++)
                    {
                        float normalizedHeight = (noiseMap[x, y] + 1) * normalizeFactor;
                        noiseMap[x, y] = Mathf.Clamp(normalizedHeight, 0, int.MaxValue);
                    }
                }
            }

            return noiseMap;
        }
    }
}