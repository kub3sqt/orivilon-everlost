using Orivilon.World.Spawning;
using Orivilon.World.Terrain;
using System.Collections.Generic;
using UnityEngine;

namespace Orivilon.Utility
{
    /// <summary>
    /// Singleton spravující pool TerrainChunkObject instancí pro systém nekonečného terénu.
    /// Místo opakovaného Instantiate/Destroy recykluje chunky – deaktivuje je, resetuje
    /// a vrátí do fronty pro znovu použití. Tím se výrazně snižuje alokace paměti a GC.
    /// Pool se automaticky rozšiřuje pokud jsou všechny chunky obsazeny.
    /// </summary>
    public class PoolManager : MonoBehaviour
    {
        /// <summary>Globální instance singletonu.</summary>
        public static PoolManager Instance;

        /// <summary>
        /// Konfigurace poolu terrain chunků (název, prefab, počáteční velikost, expandAmount).
        /// </summary>
        [System.Serializable]
        public class PoolSettings
        {
            /// <summary>Název poolu (pro přehlednost v Inspektoru).</summary>
            public string poolName = "TerrainChunks";

            /// <summary>Prefab TerrainChunkObject. Pokud null, vytvoří se základní chunk programově.</summary>
            public GameObject chunkPrefab;

            /// <summary>Počáteční počet předvytvořených chunků.</summary>
            public int initialPoolSize = 20;

            /// <summary>Počet chunků přidaných při rozšíření prázdného poolu.</summary>
            public int expandAmount = 5;
        }

        /// <summary>Konfigurace terrain chunk poolu (přiřadit v Inspektoru).</summary>
        [Header("Pool Settings")]
        public PoolSettings terrainChunkPool;

        /// <summary>Fronta dostupných (neaktivních) chunků připravených k vydání.</summary>
        private Queue<TerrainChunkObject> availableChunks = new Queue<TerrainChunkObject>();

        /// <summary>Seznam všech chunků v poolu (aktivních i neaktivních) pro správu paměti.</summary>
        private List<TerrainChunkObject> allChunks = new List<TerrainChunkObject>();

        /// <summary>Transform kontejneru sdružujícího všechny poolované chunky v hierarchii.</summary>
        private Transform poolContainer;

        /// <summary>Příznak chybného stavu prefabu – pokud true, vytváří se záložní základní chunk.</summary>
        private bool isPrefabDestroyed = false;

        /// <summary>
        /// Singleton inicializace a naplnění poolu počátečními chunky.
        /// </summary>
        private void Awake()
        {
            if (Instance != null)
            {
                Destroy(this);
                return;
            }

            Instance = this;
            InitializePool();
        }

        /// <summary>
        /// Vrátí všechny aktivní chunky do poolu a vyčistí jejich spawnuté objekty.
        /// Volá se při spuštění nové hry pro čistý start.
        /// </summary>
        public void ResetPoolForNewGame()
        {
            foreach (var chunk in allChunks)
            {
                if (chunk != null && chunk.gameObject != null)
                {
                    ReturnChunkObject(chunk);
                }
            }

            foreach (var chunk in allChunks)
            {
                if (chunk != null && chunk.gameObject != null)
                {
                    var spawner = chunk.GetComponent<ObjectSpawner>();
                    if (spawner != null)
                    {
                        spawner.ClearSpawnedObjects();
                    }
                }
            }
        }

        /// <summary>
        /// Vytvoří kontejner a naplní pool počátečními chunky.
        /// Pokud vytváření chunků selže, inicializace se přeruší.
        /// </summary>
        private void InitializePool()
        {
            poolContainer = new GameObject("Pooled Chunks").transform;
            poolContainer.SetParent(transform);
            poolContainer.localPosition = Vector3.zero;

            for (int i = 0; i < terrainChunkPool.initialPoolSize; i++)
            {
                if (!CreateNewChunkObject())
                {
                    break;
                }
            }
        }

        /// <summary>
        /// Vytvoří základní chunk prefab programově (záložní řešení pokud chunkPrefab chybí).
        /// Přidá MeshFilter, MeshRenderer, MeshCollider a TerrainChunkObject.
        /// </summary>
        /// <returns>Nový základní chunk GameObject.</returns>
        private GameObject CreateBasicChunkPrefab()
        {
            GameObject prefab = new GameObject("Basic Chunk Prefab");
            var meshFilter = prefab.AddComponent<MeshFilter>();
            var meshRenderer = prefab.AddComponent<MeshRenderer>();
            var meshCollider = prefab.AddComponent<MeshCollider>();
            var chunkObject = prefab.AddComponent<TerrainChunkObject>();

            chunkObject.meshFilter = meshFilter;
            chunkObject.meshRenderer = meshRenderer;
            chunkObject.meshCollider = meshCollider;

            return prefab;
        }

        /// <summary>
        /// Vytvoří jeden nový chunk objekt a přidá ho do poolu.
        /// Použije chunkPrefab pokud je platný, jinak vytvoří záložní základní chunk.
        /// Zajistí, že TerrainChunkObject má přiřazeny všechny potřebné komponenty.
        /// Při výjimce označí prefab jako zničený a vrátí false.
        /// </summary>
        /// <returns>True při úspěchu, False při chybě.</returns>
        private bool CreateNewChunkObject()
        {
            try
            {
                GameObject chunkGO;

                if (terrainChunkPool.chunkPrefab != null && !isPrefabDestroyed)
                {
                    chunkGO = Instantiate(terrainChunkPool.chunkPrefab, poolContainer);
                }
                else
                {
                    chunkGO = CreateBasicChunkPrefab();
                    chunkGO = Instantiate(chunkGO, poolContainer);
                }

                TerrainChunkObject chunkObject = chunkGO.GetComponent<TerrainChunkObject>();
                if (chunkObject == null)
                {
                    chunkObject = chunkGO.AddComponent<TerrainChunkObject>();
                }

                if (chunkObject.meshFilter == null)
                    chunkObject.meshFilter = chunkGO.GetComponent<MeshFilter>();
                if (chunkObject.meshRenderer == null)
                    chunkObject.meshRenderer = chunkGO.GetComponent<MeshRenderer>();
                if (chunkObject.meshCollider == null)
                    chunkObject.meshCollider = chunkGO.GetComponent<MeshCollider>();

                chunkGO.name = "Pooled Chunk";
                chunkGO.SetActive(false);
                availableChunks.Enqueue(chunkObject);
                allChunks.Add(chunkObject);

                return true;
            }
            catch (System.Exception e)
            {
                Debug.LogError($"[PoolManager] Chyba při vytváření chunk objektu: {e.Message}");
                isPrefabDestroyed = true;
                return false;
            }
        }

        /// <summary>
        /// Vydá jeden chunk objekt z poolu, aktivuje ho a vrátí ho volajícímu.
        /// Pokud je pool prázdný, automaticky ho rozšíří přes ExpandPool().
        /// Při neplatném chunku ho odstraní ze seznamu a zkusí znovu.
        /// Vrátí null pokud rozšíření selže.
        /// </summary>
        /// <returns>Aktivovaný TerrainChunkObject nebo null.</returns>
        public TerrainChunkObject GetChunkObject()
        {
            if (availableChunks.Count == 0)
            {
                ExpandPool();
            }

            if (availableChunks.Count > 0)
            {
                TerrainChunkObject chunk = availableChunks.Dequeue();
                if (chunk != null && chunk.gameObject != null)
                {
                    chunk.gameObject.SetActive(true);
                    return chunk;
                }
                else
                {
                    if (chunk != null) allChunks.Remove(chunk);
                }
            }

            if (CreateNewChunkObject())
            {
                return GetChunkObject();
            }
            else
            {
                return null;
            }
        }

        /// <summary>
        /// Vrátí chunk zpět do poolu.
        /// Resetuje Transform, deaktivuje objekt, vymaže mesh/materiál/collider
        /// a zničí všechny podřízené objekty (dekorace, spawnery).
        /// Při výjimce (poškozený chunk) ho odstraní z allChunks.
        /// </summary>
        /// <param name="chunkObject">Chunk k vrácení do poolu.</param>
        public void ReturnChunkObject(TerrainChunkObject chunkObject)
        {
            if (chunkObject == null)
            {
                return;
            }

            if (chunkObject.gameObject == null)
            {
                allChunks.Remove(chunkObject);
                return;
            }

            try
            {
                chunkObject.transform.SetParent(poolContainer);
                chunkObject.transform.position = Vector3.zero;
                chunkObject.transform.rotation = Quaternion.identity;
                chunkObject.transform.localScale = Vector3.one;

                chunkObject.gameObject.SetActive(false);

                if (chunkObject.meshFilter != null)
                    chunkObject.meshFilter.mesh = null;

                if (chunkObject.meshRenderer != null)
                    chunkObject.meshRenderer.material = null;

                if (chunkObject.meshCollider != null)
                {
                    chunkObject.meshCollider.sharedMesh = null;
                    chunkObject.meshCollider.enabled = false;
                }

                foreach (Transform child in chunkObject.transform)
                {
                    if (child != chunkObject.transform && child.gameObject != null)
                    {
                        Destroy(child.gameObject);
                    }
                }

                availableChunks.Enqueue(chunkObject);
            }
            catch (System.Exception)
            {
                allChunks.Remove(chunkObject);
            }
        }

        /// <summary>
        /// Rozšíří pool o expandAmount nových chunků.
        /// Pokud vytváření chunků selže, přeruší se.
        /// </summary>
        private void ExpandPool()
        {
            int successfullyCreated = 0;
            for (int i = 0; i < terrainChunkPool.expandAmount; i++)
            {
                if (CreateNewChunkObject())
                {
                    successfullyCreated++;
                }
            }
        }

        /// <summary>
        /// Zničí všechny chunky v poolu, vyčistí frontu a seznam a znovu inicializuje pool.
        /// Volá se při návratu do hlavního menu nebo restartu hry.
        /// </summary>
        public void ResetPool()
        {
            foreach (var chunk in allChunks)
            {
                if (chunk != null && chunk.gameObject != null)
                    Destroy(chunk.gameObject);
            }
            availableChunks.Clear();
            allChunks.Clear();
            isPrefabDestroyed = false;

            InitializePool();
        }

        /// <summary>
        /// Vypíše statistiku poolu do konzole (počet platných chunků).
        /// Použitelné pro ladění výkonu pool systému.
        /// </summary>
        public void PrintPoolStats()
        {
            int validChunks = 0;
            foreach (var chunk in allChunks)
            {
                if (chunk != null && chunk.gameObject != null) validChunks++;
            }
        }

        /// <summary>
        /// Při zničení PoolManageru uklidí všechny zbývající chunky a vymaže statickou referenci.
        /// </summary>
        private void OnDestroy()
        {
            foreach (var chunk in allChunks)
            {
                if (chunk != null && chunk.gameObject != null)
                    Destroy(chunk.gameObject);
            }

            if (Instance == this)
            {
                Instance = null;
            }
        }
    }
}
