using Unity.Collections;
using Unity.Jobs;

namespace Orivilon.World.Terrain
{
    /// <summary>
    /// Unity Job System job pro paralelní výpočet viditelnosti terrain chunků.
    /// Každý index odpovídá jednomu potenciálnímu chunku v čtvercové mřížce (2*renderDistance+1)^2.
    /// Job vypočítá souřadnice chunku, euklidovskou vzdálenost od hráče a LOD úroveň.
    /// Chunky mimo renderDistance jsou označeny hodnotou -9999 jako neviditelné.
    /// Navržen pro budoucí optimalizaci – aktuálně se využívá UpdateVisibleChunksOptimized() na hlavním vlákně.
    /// </summary>
    public struct ChunkVisibilityJob : IJobParallelFor
    {
        /// <summary>X souřadnice vieweru ve světových jednotkách.</summary>
        [ReadOnly] public float viewerPosX;

        /// <summary>Z souřadnice vieweru ve světových jednotkách (jako Y v 2D mřížce).</summary>
        [ReadOnly] public float viewerPosY;

        /// <summary>Velikost jednoho chunku v jednotkách.</summary>
        [ReadOnly] public int chunkSize;

        /// <summary>Vzdálenost zobrazení v počtu chunků.</summary>
        [ReadOnly] public int renderDistance;

        /// <summary>Výstupní pole X souřadnic viditelných chunků.</summary>
        public NativeArray<int> visibleChunksX;

        /// <summary>Výstupní pole Y souřadnic viditelných chunků.</summary>
        public NativeArray<int> visibleChunksY;

        /// <summary>Výstupní pole LOD úrovní pro každý chunk.</summary>
        public NativeArray<int> lodLevels;

        /// <summary>
        /// Zpracuje jeden chunk (index v lineárním poli 2D mřížky).
        /// Vypočítá offset od středu, euklidovskou vzdálenost a LOD.
        /// Chunky mimo renderDistance dostanou souřadnici -9999 jako příznak neviditelnosti.
        /// </summary>
        public void Execute(int index)
        {
            int xOffset = (index % (renderDistance * 2 + 1)) - renderDistance;
            int yOffset = (index / (renderDistance * 2 + 1)) - renderDistance;

            float distance = System.MathF.Sqrt(xOffset * xOffset + yOffset * yOffset);
            if (distance > renderDistance)
            {
                visibleChunksX[index] = -9999;
                visibleChunksY[index] = -9999;
                return;
            }

            int currentChunkX = (int)System.MathF.Floor(viewerPosX / chunkSize);
            int currentChunkY = (int)System.MathF.Floor(viewerPosY / chunkSize);

            visibleChunksX[index] = currentChunkX + xOffset;
            visibleChunksY[index] = currentChunkY + yOffset;
            lodLevels[index] = (int)System.MathF.Max(1, distance / 3);
        }
    }
}