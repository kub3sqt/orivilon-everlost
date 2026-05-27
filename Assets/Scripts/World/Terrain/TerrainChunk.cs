using Orivilon.Utility;
using Orivilon.World.Spawning;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Profiling;

namespace Orivilon.World.Terrain
{
    /// <summary>
    /// Třída (ne MonoBehaviour) reprezentující jeden terénní chunk v systému nekonečného světa.
    /// Při vytvoření si vyžádá TerrainChunkObject z PoolManageru (nebo vytvoří nový GameObject),
    /// asynchronně požádá MapGenerator o data a po jejich obdržení aplikuje mesh, collider a dekorace.
    /// LOD se přepíná metodou UpdateLOD – collider vždy používá LOD0 pro přesnou fyziku.
    /// Při uvolnění (Dispose) se chunk vrátí do poolu pro recyklaci.
    /// </summary>
    public class TerrainChunk
    {
        /// <summary>Souřadnice chunku v mřížce světa.</summary>
        public Vector2Int coord;

        /// <summary>MonoBehaviour wrapper chunku v Unity scéně.</summary>
        public TerrainChunkObject chunkObject;

        /// <summary>MeshFilter komponenta pro vizuální mesh.</summary>
        public MeshFilter meshFilter;

        /// <summary>MeshRenderer komponenta pro renderování meshe.</summary>
        public MeshRenderer meshRenderer;

        /// <summary>MeshCollider komponenta pro fyziku (vždy LOD0).</summary>
        public MeshCollider meshCollider;

        /// <summary>Pole předgenerovaných meshů pro každou LOD úroveň.</summary>
        public Mesh[] lodMeshes;

        /// <summary>Aktuálně zobrazovaná LOD úroveň.</summary>
        public int currentLOD;

        /// <summary>True pokud byl mesh úspěšně přijat a aplikován.</summary>
        private bool initialized = false;

        /// <summary>True pokud je chunk aktuálně viditelný (MeshRenderer enabled).</summary>
        private bool visible = false;

        /// <summary>Cache prefabu SpawnableObjects z Resources pro spawner komponentu.</summary>
        private static GameObject cachedSpawnerPrefabResource;

        /// <summary>Callback zavolaný po dokončení inicializace (pro ChunkLoaderAPI progres).</summary>
        private System.Action onInitializedCallback;

        /// <summary>True pokud byl chunk úspěšně inicializován s platným meshem.</summary>
        public bool IsInitialized => initialized;

        /// <summary>
        /// Vytvoří terénní chunk – získá nebo vytvoří TerrainChunkObject, nastaví pozici a materiál,
        /// a asynchronně požádá MapGenerator o data výškové mapy.
        /// Pokud MapGenerator neexistuje, zavolá callback v příštím snímku (aby loader nezůstal viset).
        /// </summary>
        /// <param name="coord">Souřadnice chunku v mřížce světa.</param>
        /// <param name="position">Světová pozice chunku.</param>
        /// <param name="parent">Parent Transform v hierarchii scény.</param>
        /// <param name="material">Materiál aplikovaný na MeshRenderer.</param>
        /// <param name="lod">Počáteční LOD úroveň.</param>
        /// <param name="onInitialized">Volitelný callback po dokončení inicializace.</param>
        public TerrainChunk(Vector2Int coord, Vector3 position, Transform parent, Material material, int lod, System.Action onInitialized = null)
        {
            this.coord = coord;
            this.onInitializedCallback = onInitialized;

            if (PoolManager.Instance != null)
            {
                chunkObject = PoolManager.Instance.GetChunkObject();
            }

            if (chunkObject == null)
            {
                GameObject chunkGO = new GameObject($"Terrain Chunk {coord}");
                chunkGO.transform.position = position;
                chunkGO.transform.parent = parent;

                meshFilter = chunkGO.AddComponent<MeshFilter>();
                meshRenderer = chunkGO.AddComponent<MeshRenderer>();
                meshCollider = chunkGO.AddComponent<MeshCollider>();

                chunkObject = chunkGO.AddComponent<TerrainChunkObject>();
                chunkObject.meshFilter = meshFilter;
                chunkObject.meshRenderer = meshRenderer;
                chunkObject.meshCollider = meshCollider;
            }
            else
            {
                chunkObject.name = $"Terrain Chunk {coord}";
                chunkObject.transform.position = position;
                chunkObject.transform.parent = parent;

                meshFilter = chunkObject.meshFilter;
                meshRenderer = chunkObject.meshRenderer;
                meshCollider = chunkObject.meshCollider;
            }

            currentLOD = lod;
            visible = true;

            if (material != null && meshRenderer != null)
                meshRenderer.material = material;

            if (MapGenerator.instance != null)
            {
                MapGenerator.instance.RequestMapData(coord, OnMapDataReceived);
            }
            else
            {
                chunkObject?.StartCoroutine(InvokeCallbackNextFrame());
            }
        }

        /// <summary>
        /// Čeká jeden snímek a zavolá onInitializedCallback.
        /// Záložní mechanismus pro případ nedostupného MapGeneratoru.
        /// </summary>
        private IEnumerator InvokeCallbackNextFrame()
        {
            yield return null;
            onInitializedCallback?.Invoke();
        }

        /// <summary>
        /// Callback volaný po obdržení MapData z vlákna.
        /// Ihned požádá o MeshData, nebo zavolá callback při chybě.
        /// </summary>
        void OnMapDataReceived(MapData mapData)
        {
            if (MapGenerator.instance != null)
                MapGenerator.instance.RequestMeshData(mapData, OnMeshDataReceived);
            else
            {
                if (onInitializedCallback != null)
                    chunkObject?.StartCoroutine(InvokeCallbackNextFrame());
            }
        }

        /// <summary>
        /// Callback volaný po obdržení MeshData[] z vlákna.
        /// Vytvoří Mesh objekty pro každý LOD, aplikuje LOD0 na collider,
        /// nastaví viditelný mesh a spustí spawn dekorací.
        /// Při prázdných nebo neplatných datech zavolá callback i tak (aby loader pokračoval).
        /// </summary>
        void OnMeshDataReceived(MeshData[] meshData)
        {
            if (meshData == null || meshData.Length == 0)
            {
                initialized = false;
                onInitializedCallback?.Invoke();
                return;
            }

            lodMeshes = new Mesh[meshData.Length];
            bool hasValidMesh = false;

            for (int i = 0; i < meshData.Length; i++)
            {
                if (meshData[i].vertices != null && meshData[i].vertices.Length > 0)
                {
                    lodMeshes[i] = meshData[i].CreateMesh(true);
                    hasValidMesh = true;
                }
                else
                {
                    lodMeshes[i] = new Mesh();
                }
            }

            if (!hasValidMesh)
            {
                initialized = false;
                onInitializedCallback?.Invoke();
                return;
            }

            initialized = true;

            if (lodMeshes != null && lodMeshes.Length > 0 && lodMeshes[0] != null && meshCollider != null)
            {
                meshCollider.sharedMesh = lodMeshes[0];
                meshCollider.enabled = true;
            }

            if (meshFilter != null)
                SetVisible(visible, currentLOD);

            if (visible)
                SpawnDecorations();

            onInitializedCallback?.Invoke();
        }

        /// <summary>
        /// Spustí spawn dekorací (stromy, kameny, tráva) na tomto chunku.
        /// Nejprve zkusí najít ObjectSpawner na chunkObject, pak načte ze Resources.
        /// Generuje výškovou a biom mapu a předá je ObjectSpawneru.
        /// </summary>
        private void SpawnDecorations()
        {
            if (chunkObject == null) return;

            ObjectSpawner spawner = chunkObject.GetComponent<ObjectSpawner>();

            if (spawner == null)
            {
                if (cachedSpawnerPrefabResource == null)
                {
                    cachedSpawnerPrefabResource = Resources.Load<GameObject>("Prefabs/SpawnableObjects");
                }

                if (cachedSpawnerPrefabResource != null)
                {
                    GameObject spawned = GameObject.Instantiate(cachedSpawnerPrefabResource, chunkObject.transform);
                    spawner = spawned.GetComponent<ObjectSpawner>();
                }
                else return;
            }

            if (spawner.IsInitializedForChunk(coord)) return;
            if (EndlessTerrain.instance == null || MapGenerator.instance == null) return;
            if (MapGenerator.instance.biomeCollection == null) return;

            spawner.Initialize(MapGenerator.instance.seed, coord);

            float[,] heightMap = MapGenerator.instance.GetHeightMap(coord);
            Vector3 chunkOrigin = chunkObject.transform.position;

            BiomeType[,] biomeMap = MapGenerator.instance.biomeCollection.GenerateBiomeMap(
                heightMap,
                chunkOrigin,
                MapGenerator.instance.seed
            );

            spawner.SpawnObjects(
                chunkObject.transform,
                chunkOrigin,
                heightMap,
                biomeMap,
                EndlessTerrain.instance.vertexSpacing,
                EndlessTerrain.instance.chunkSize
            );
        }

        /// <summary>
        /// Nastaví viditelnost chunku (MeshRenderer enabled/disabled) a přepne LOD.
        /// Pokud chunk ještě není inicializován, pouze uloží požadovaný stav pro pozdější aplikaci.
        /// </summary>
        public void SetVisible(bool visible, int lod = 0)
        {
            this.visible = visible;
            currentLOD = lod;

            if (!initialized) return;

            if (meshRenderer != null)
                meshRenderer.enabled = visible;

            if (visible)
                UpdateLOD(lod);
        }

        /// <summary>
        /// Přepne vizuální mesh na danou LOD úroveň.
        /// Collider vždy zůstává na LOD0 pro přesnou fyziku bez ohledu na vizuální LOD.
        /// </summary>
        public void UpdateLOD(int lod)
        {
            if (lodMeshes == null || meshFilter == null) return;

            currentLOD = Mathf.Clamp(lod, 0, lodMeshes.Length - 1);

            if (lodMeshes[currentLOD] != null)
                meshFilter.mesh = lodMeshes[currentLOD];

            if (meshCollider != null && lodMeshes != null && lodMeshes.Length > 0 && lodMeshes[0] != null)
            {
                meshCollider.sharedMesh = lodMeshes[0];
                meshCollider.enabled = true;
            }
        }

        /// <summary>
        /// Uvolní chunk – deaktivuje renderer a collider, vyčistí mesh,
        /// zničí spawnuté child objekty a vrátí TerrainChunkObject do PoolManageru (nebo ho zničí).
        /// Zničí také všechny Mesh objekty pro uvolnění paměti GPU.
        /// </summary>
        public void Dispose()
        {
            if (chunkObject != null)
            {
                if (meshRenderer != null) meshRenderer.enabled = false;
                if (meshCollider != null) meshCollider.enabled = false;
                if (meshFilter != null) meshFilter.mesh = null;

                foreach (Transform child in chunkObject.transform)
                {
                    var spawner = child.GetComponent<ObjectSpawner>();
                    if (spawner != null) spawner.ClearSpawnedObjects();
                    else GameObject.Destroy(child.gameObject);
                }

                if (PoolManager.Instance != null)
                    PoolManager.Instance.ReturnChunkObject(chunkObject);
                else
                    GameObject.Destroy(chunkObject.gameObject);
            }

            if (lodMeshes != null)
            {
                for (int i = 0; i < lodMeshes.Length; i++)
                {
                    if (lodMeshes[i] != null)
                        GameObject.Destroy(lodMeshes[i]);
                }
            }

            lodMeshes = null;
            initialized = false;
            onInitializedCallback = null;
        }
    }
}