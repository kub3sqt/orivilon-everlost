using Orivilon.Data;
using System;
using UnityEngine;

namespace Orivilon.World.Terrain
{
    /// <summary>
    /// Statická třída s pomocnými metodami pro převod výškové mapy na data meshe.
    /// Generuje vrcholy, UV souřadnice, barvy a indexy trojúhelníků pro danou LOD úroveň.
    /// Podporuje generování pole MeshData pro všechny LOD úrovně najednou.
    /// </summary>
    public static class TerrainGenerator
    {
        /// <summary>
        /// Vytvoří MeshData pro daný chunk a LOD úroveň.
        /// Krok vrcholů se zdvojnásobuje pro každou LOD úroveň (step = 2^lod).
        /// Výšky a barvy se berou z BiomeCollection.GetAltitudeAndColorMap()
        /// která prolíná hodnoty sousedních biomů pro plynulé přechody.
        /// Trojúhelníky se generují jako dvojice pravých trojúhelníků pro každý čtverec mřížky.
        /// </summary>
        /// <param name="mapData">Výšková mapa a souřadnice chunku.</param>
        /// <param name="biomes">Sbírka biomů pro výpočet výšek a barev.</param>
        /// <param name="lod">LOD úroveň (0 = plný detail, vyšší = méně vrcholů).</param>
        /// <returns>MeshData se všemi daty potřebnými pro vytvoření Unity Mesh.</returns>
        public static MeshData CreateMeshData(MapData mapData, BiomeCollection biomes, int lod = 0)
        {
            float[,] data = mapData.heightMap;
            var altitudeAndColorMap = biomes.GetAltitudeAndColorMap(mapData.chunkCoords, ref data);
            return CreateMeshData(mapData, lod, altitudeAndColorMap.Item1, altitudeAndColorMap.Item2);
        }

        /// <summary>
        /// Vytvoří MeshData z předpočítané altitude/color mapy.
        /// Mapy se počítají jednou pro celý chunk a sdílí se mezi všemi LOD úrovněmi
        /// (výpočet map je nejdražší část generování – dříve se počítal pro každou LOD znovu).
        /// </summary>
        public static MeshData CreateMeshData(MapData mapData, int lod, float[,] altitudeMap, Color[,] colorMap)
        {
            float[,] data = mapData.heightMap;
            int dataSize = data.GetLength(0);

            int step = 1 << lod;
            int verticesPerLine = (dataSize - 1) / step + 1;

            int sqrVertices = verticesPerLine * verticesPerLine;
            Vector3[] vertices = new Vector3[sqrVertices];
            Vector2[] uvs = new Vector2[sqrVertices];
            Color32[] colors = new Color32[sqrVertices];
            int[] triangles = new int[(sqrVertices - 2 * verticesPerLine + 1) * 6];

            float minHeight = float.MaxValue;
            float maxHeight = float.MinValue;

            int vPerLineMinus1 = verticesPerLine - 1;
            for (int y = 0; y < verticesPerLine; y++)
            {
                for (int x = 0; x < verticesPerLine; x++)
                {
                    int heightIndex = y * verticesPerLine + x;
                    int xStep = x * step;
                    int yStep = y * step;
                    float altitude = altitudeMap[xStep, yStep];
                    vertices[heightIndex] = new Vector3(xStep, altitude, yStep);
                    uvs[heightIndex] = new Vector2((float)x / vPerLineMinus1, (float)y / vPerLineMinus1);
                    colors[heightIndex] = colorMap[xStep, yStep];

                    if (altitude < minHeight) minHeight = altitude;
                    if (altitude > maxHeight) maxHeight = altitude;
                }
            }

            int index = 0;
            for (int y = 0; y < verticesPerLine - 1; y++)
            {
                for (int x = 0; x < verticesPerLine - 1; x++)
                {
                    int baseIndex = y * verticesPerLine + x;
                    triangles[index] = baseIndex;
                    triangles[index + 1] = baseIndex + verticesPerLine;
                    triangles[index + 2] = baseIndex + 1;
                    triangles[index + 3] = baseIndex + 1;
                    triangles[index + 4] = baseIndex + verticesPerLine;
                    triangles[index + 5] = baseIndex + verticesPerLine + 1;
                    index += 6;
                }
            }

            return new MeshData
            {
                vertices = vertices,
                triangles = triangles,
                uvs = uvs,
                colors = colors,
                lod = lod,
                minHeight = minHeight,
                maxHeight = maxHeight
            };
        }

        /// <summary>
        /// Vytvoří pole MeshData pro všechny LOD úrovně (0 až lodCount-1).
        /// Volá CreateMeshData pro každou úroveň. Zachytí výjimky a vrátí null při chybě.
        /// </summary>
        /// <param name="mapData">Výšková mapa a souřadnice chunku.</param>
        /// <param name="biomes">Sbírka biomů pro výpočet výšek a barev.</param>
        /// <param name="lodCount">Počet LOD úrovní k vygenerování.</param>
        /// <returns>Pole MeshData pro každou LOD úroveň, nebo null při chybě.</returns>
        public static MeshData[] CreateMeshDataWithLOD(MapData mapData, BiomeCollection biomes, int lodCount)
        {
            return CreateMeshDataWithLOD(mapData, biomes, lodCount, out _);
        }

        /// <summary>
        /// Varianta vracející i altitude mapu (světové výšky LOD0 vrcholů).
        /// Altitude mapa se dále používá pro spawn dekorací bez raycastů.
        /// Altitude/color mapa se počítá pouze JEDNOU a sdílí mezi všemi LOD úrovněmi.
        /// </summary>
        public static MeshData[] CreateMeshDataWithLOD(MapData mapData, BiomeCollection biomes, int lodCount, out float[,] altitudeMap)
        {
            altitudeMap = null;
            try
            {
                float[,] data = mapData.heightMap;
                var maps = biomes.GetAltitudeAndColorMap(mapData.chunkCoords, ref data);
                altitudeMap = maps.Item1;

                MeshData[] meshData = new MeshData[lodCount];
                for (int i = 0; i < lodCount; i++)
                {
                    meshData[i] = CreateMeshData(mapData, i, maps.Item1, maps.Item2);
                }
                return meshData;
            }
            catch (System.Exception e)
            {
                Debug.LogError("Error creating mesh data with LOD: " + e);
                return null;
            }
        }

        /// <summary>
        /// Vytvoří MapData strukturu z výškové mapy a souřadnic chunku.
        /// Jednoduchý wrapper pro vytvoření MapData bez přímé inicializace struktury.
        /// </summary>
        /// <param name="data">2D pole výškových hodnot.</param>
        /// <param name="coords">Souřadnice chunku v mřížce světa.</param>
        /// <returns>MapData struktura připravená pro předání do MeshData generátoru.</returns>
        public static MapData CreateMapData(float[,] data, Vector2Int coords)
        {
            return new MapData
            {
                heightMap = data,
                chunkCoords = coords
            };
        }
    }
}