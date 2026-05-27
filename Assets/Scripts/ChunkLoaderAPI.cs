using Orivilon.World.Terrain;
using UnityEngine;

namespace Orivilon
{
    /// <summary>
    /// Statická třída sledující průběh načítání herních chunkůh (herních bloků terénu).
    /// Poskytuje rozhraní pro spuštění generování, sledování progresu a zjištění dokončení.
    /// Používá ji SceneLoader i GameManager pro synchronizaci načítací obrazovky s generováním světa.
    /// </summary>
    public static class ChunkLoaderAPI
    {
        /// <summary>
        /// Celkový počet chunkůh, které mají být vygenerovány.
        /// </summary>
        private static int totalToLoad = 0;

        /// <summary>
        /// Počet dosud načtených chunkůh.
        /// </summary>
        private static int loaded = 0;

        /// <summary>
        /// Příznak, zda právě probíhá načítání.
        /// </summary>
        private static bool loading = false;

        /// <summary>
        /// Spustí generování všech chunkůh světa.
        /// Zjistí jejich celkový počet z EndlessTerrain.instance nebo záložním výpočtem,
        /// a deleguje samotné generování na EndlessTerrain s callbacky pro průběh a dokončení.
        /// </summary>
        public static void StartLoadingChunks()
        {
            loading = true;
            loaded = 0;

            if (EndlessTerrain.instance != null)
            {
                totalToLoad = EndlessTerrain.instance.GetTargetChunkCount();
            }
            else
            {
                Debug.LogWarning("[ChunkLoaderAPI] EndlessTerrain.instance is null, computing fallback total");
                ComputeFallbackTotal();
            }

            Debug.Log($"[ChunkLoaderAPI] Starting chunk loading with totalToLoad={totalToLoad}");

            EndlessTerrain.instance.GenerateWorldChunks(OnChunkLoaded, OnAllChunksGenerated);
        }

        /// <summary>
        /// Záložní výpočet celkového počtu chunkůh pro případ, že EndlessTerrain ještě neexistuje.
        /// Počítá chunky v kruhu s pevně daným poloměrem 11 (renderDistance 10 + 1).
        /// </summary>
        private static void ComputeFallbackTotal()
        {
            int effectiveRender = 10 + 1;
            totalToLoad = 0;

            for (int y = -effectiveRender; y <= effectiveRender; y++)
            {
                for (int x = -effectiveRender; x <= effectiveRender; x++)
                {
                    if (x * x + y * y <= effectiveRender * effectiveRender)
                        totalToLoad++;
                }
            }
        }

        /// <summary>
        /// Callback volaný po vygenerování každého jednotlivého chunku.
        /// Zvyšuje počítadlo načtených chunkůh a loguje průběh každých 10 chunkůh nebo na konci.
        /// </summary>
        private static void OnChunkLoaded()
        {
            loaded++;
            if (loaded % 10 == 0 || loaded == totalToLoad)
            {
                Debug.Log($"[ChunkLoaderAPI] OnChunkLoaded {loaded}/{totalToLoad} ({GetLoadProgress() * 100:F1}%)");
            }
        }

        /// <summary>
        /// Callback volaný po dokončení generování všech chunkůh.
        /// Opraví případný nesoulad mezi počítadlem a celkovým počtem (loaded vs. total)
        /// a nastaví příznak loading na false.
        /// </summary>
        private static void OnAllChunksGenerated()
        {
            Debug.Log($"[ChunkLoaderAPI] OnAllChunksGenerated: loaded={loaded}, total={totalToLoad}");

            if (loaded < totalToLoad)
            {
                Debug.LogWarning($"[ChunkLoaderAPI] loaded({loaded}) < total({totalToLoad}) - correcting");
                loaded = totalToLoad;
            }
            else if (loaded > totalToLoad)
            {
                Debug.LogWarning($"[ChunkLoaderAPI] loaded({loaded}) > total({totalToLoad}) - correcting");
                totalToLoad = loaded;
            }

            loading = false;
        }

        /// <summary>
        /// Vrátí aktuální progres načítání jako číslo od 0.0 do 1.0.
        /// Pokud je načítání dokončeno (loaded >= total), vrátí přesně 1.0.
        /// </summary>
        /// <returns>Progres načítání v rozsahu 0–1.</returns>
        public static float GetLoadProgress()
        {
            if (totalToLoad <= 0) return 0f;

            float progress = Mathf.Clamp01((float)loaded / totalToLoad);

            if (progress > 0.99f && loaded >= totalToLoad)
                return 1f;

            return progress;
        }

        /// <summary>
        /// Zjistí, zda bylo načítání chunkůh plně dokončeno.
        /// Vrátí true pouze pokud jsou načteny všechny chunky a loading flag je false.
        /// </summary>
        /// <returns>True, pokud je načítání kompletně hotové.</returns>
        public static bool IsFinishedLoading()
        {
            bool finished = loaded >= totalToLoad && totalToLoad > 0 && !loading;

            if (finished)
            {
                Debug.Log($"[ChunkLoaderAPI] IsFinishedLoading = true (loaded={loaded}, total={totalToLoad})");
            }

            return finished;
        }

        /// <summary>
        /// Resetuje vnitřní stav API do výchozích hodnot.
        /// Volá se před každým novým spuštěním načítání (např. při přechodu do hry).
        /// </summary>
        public static void Reset()
        {
            totalToLoad = 0;
            loaded = 0;
            loading = false;
        }
    }
}