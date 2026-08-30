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
    /// asynchronně požádá MapGenerator o data a po jejich obdržení líně vytváří meshe a dekorace.
    /// LOD se přepíná metodou UpdateLOD. Collider (indexovaný LOD0, svařené vrcholy) mají
    /// jen chunky blízko hráče – řídí EndlessTerrain přes SetColliderActive.
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

        /// <summary>MeshCollider komponenta pro fyziku (indexovaný LOD0 mesh, jen blízko hráče).</summary>
        public MeshCollider meshCollider;

        /// <summary>
        /// Pole meshů pro každou LOD úroveň. Vytvářejí se LÍNĚ – mesh vznikne
        /// až když chunk danou LOD skutečně zobrazuje (šetří hlavní vlákno i VRAM).
        /// </summary>
        public Mesh[] lodMeshes;

        /// <summary>Surová MeshData pro každou LOD (zdroj pro líné vytváření meshů).</summary>
        private MeshData[] lodMeshData;

        /// <summary>Mesh collideru ze sdílených (svařených) vrcholů – viz MeshData.CreateColliderMesh.</summary>
        private Mesh colliderMesh;

        /// <summary>Aktuálně zobrazovaná LOD úroveň.</summary>
        public int currentLOD;

        /// <summary>True pokud byl mesh úspěšně přijat a aplikován.</summary>
        private bool initialized = false;

        /// <summary>True pokud je chunk aktuálně viditelný (MeshRenderer enabled).</summary>
        private bool visible = false;

        /// <summary>True pokud má chunk mít aktivní collider (nastavuje EndlessTerrain podle vzdálenosti).</summary>
        private bool colliderActive = false;

        /// <summary>Aktuální vzdálenost chunku od hráče v chuncích (nastavuje EndlessTerrain).</summary>
        private int detailDistance = int.MaxValue;

        /// <summary>True po úspěšném spawnu dekorací (stromy/kameny; tráva se řídí zvlášť).</summary>
        private bool decorationsSpawned = false;

        /// <summary>True po Dispose – pozdní callbacky z vláken se ignorují.</summary>
        private bool disposed = false;

        /// <summary>Cache surové noise mapy chunku (pro výškové limity spawnu).</summary>
        private float[,] cachedHeightMap;

        /// <summary>Cache světových výšek LOD0 vrcholů (pro spawn dekorací bez raycastů).</summary>
        private float[,] cachedAltitudeMap;

        /// <summary>Cache biome mapy (spočtená ve vedlejším vlákně).</summary>
        private BiomeType[,] cachedBiomeMap;

        /// <summary>ObjectSpawner instance tohoto chunku (child objekt).</summary>
        private ObjectSpawner chunkSpawner;

        /// <summary>Nejnižší bod terénu chunku (pro ChunkWater). float.MaxValue dokud není inicializován.</summary>
        public float MinTerrainHeight { get; private set; } = float.MaxValue;

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

            // sharedMaterial: dřívější .material vytvářelo kopii materiálu pro každý chunk
            // (stovky instancí, rozbitý SRP batching). Všechny chunky sdílí jeden materiál.
            if (material != null && meshRenderer != null)
                meshRenderer.sharedMaterial = material;

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
            if (disposed) return;

            if (MapGenerator.instance != null)
                MapGenerator.instance.RequestMeshData(mapData, OnMeshDataReceived);
            else
            {
                if (onInitializedCallback != null)
                    chunkObject?.StartCoroutine(InvokeCallbackNextFrame());
            }
        }

        /// <summary>
        /// Callback volaný po obdržení ChunkGenResult z vlákna.
        /// Uloží MeshData, altitude a biome mapy. Unity Mesh objekty se NEvytvářejí
        /// předem pro všechny LOD – vznikají líně až při zobrazení dané úrovně.
        /// Collider se vytvoří jen pokud je chunk blízko hráče (colliderActive).
        /// Při prázdných nebo neplatných datech zavolá callback i tak (aby loader pokračoval).
        /// </summary>
        void OnMeshDataReceived(ChunkGenResult result)
        {
            if (disposed)
            {
                onInitializedCallback?.Invoke();
                return;
            }

            MeshData[] meshData = result.lodMeshData;

            if (meshData == null || meshData.Length == 0)
            {
                initialized = false;
                onInitializedCallback?.Invoke();
                return;
            }

            bool hasValidMesh = false;
            for (int i = 0; i < meshData.Length; i++)
            {
                if (meshData[i].vertices != null && meshData[i].vertices.Length > 0)
                {
                    hasValidMesh = true;
                    break;
                }
            }

            if (!hasValidMesh)
            {
                initialized = false;
                onInitializedCallback?.Invoke();
                return;
            }

            lodMeshData = meshData;
            lodMeshes = new Mesh[meshData.Length];
            cachedHeightMap = result.heightMap;
            cachedAltitudeMap = result.altitudeMap;
            cachedBiomeMap = result.biomeMap;

            if (meshData[0].vertices != null && meshData[0].vertices.Length > 0)
                MinTerrainHeight = meshData[0].minHeight;

            initialized = true;

            ApplyColliderState();

            if (meshFilter != null)
                SetVisible(visible, currentLOD);

            if (visible)
                EnsureDecorations();

            onInitializedCallback?.Invoke();
        }

        /// <summary>
        /// Vrátí (a při prvním použití vytvoří) Unity Mesh pro danou LOD úroveň.
        /// Vizuální meshe se po nahrání na GPU označí jako non-readable (úspora RAM).
        /// </summary>
        private Mesh GetOrCreateLODMesh(int lod)
        {
            if (lodMeshes == null || lodMeshData == null) return null;
            if (lod < 0 || lod >= lodMeshes.Length) return null;

            if (lodMeshes[lod] == null)
            {
                if (lodMeshData[lod].vertices == null || lodMeshData[lod].vertices.Length == 0)
                    return null;

                lodMeshes[lod] = lodMeshData[lod].CreateMesh(true);
            }

            return lodMeshes[lod];
        }

        /// <summary>
        /// Zapne/vypne collider podle vzdálenosti od hráče. Volá EndlessTerrain.
        /// Vzdálené chunky collider nepotřebují – nic tam nesimuluje fyziku,
        /// a PhysX cooking stovek chunků byl hlavní žrout CPU.
        /// </summary>
        public void SetColliderActive(bool active)
        {
            colliderActive = active;

            if (!initialized) return;

            ApplyColliderState();
        }

        /// <summary>
        /// Aplikuje požadovaný stav collideru. Mesh collideru se vytváří jen jednou
        /// (cooking proběhne při prvním přiřazení sharedMesh); enable/disable je pak levné.
        /// </summary>
        private void ApplyColliderState()
        {
            if (meshCollider == null) return;

            if (colliderActive)
            {
                if (colliderMesh == null && lodMeshData != null && lodMeshData.Length > 0)
                    colliderMesh = lodMeshData[0].CreateColliderMesh();

                if (colliderMesh != null)
                {
                    // Guard: opakované přiřazení stejného meshe by vynutilo zbytečný re-cooking.
                    if (meshCollider.sharedMesh != colliderMesh)
                        meshCollider.sharedMesh = colliderMesh;

                    meshCollider.enabled = true;
                }
            }
            else
            {
                meshCollider.enabled = false;
            }
        }

        /// <summary>
        /// Předá spawneru aktuální vzdálenost chunku od hráče. Volá EndlessTerrain.
        /// Spawner podle ní řídí fyzickou přítomnost trávy (globální grassDistance)
        /// a dekorací s nastaveným maxViewDistanceChunks.
        /// </summary>
        public void SetDetailDistance(int chunkDistance)
        {
            detailDistance = chunkDistance;

            if (!initialized || !decorationsSpawned) return;

            if (chunkSpawner != null && chunkObject != null)
                chunkSpawner.UpdateDetailVisibility(chunkDistance, chunkObject.transform);
        }

        /// <summary>
        /// Spustí spawn dekorací (stromy, kameny, tráva) na tomto chunku – jen jednou.
        /// Používá výškovou, altitude a biome mapu nacachovanou z vedlejšího vlákna –
        /// dřívější verze je znovu počítala na hlavním vlákně (tisíce Perlin vzorků na chunk).
        /// Tráva se neinstancuje hned: spawner si ji uloží jako "pending" a fyzicky
        /// ji vytvoří až podle SetDetailDistance (vzdálenost od hráče).
        /// </summary>
        private void EnsureDecorations()
        {
            if (decorationsSpawned || disposed) return;
            if (chunkObject == null) return;
            if (cachedHeightMap == null || cachedBiomeMap == null || cachedAltitudeMap == null) return;
            if (EndlessTerrain.instance == null || MapGenerator.instance == null) return;
            if (MapGenerator.instance.biomeCollection == null) return;

            // GetComponentInChildren: spawner je child objekt chunku
            // (dřívější GetComponent na root ho nikdy nenašel a instancoval nový).
            ObjectSpawner spawner = chunkObject.GetComponentInChildren<ObjectSpawner>();

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

            if (spawner == null) return;
            chunkSpawner = spawner;

            spawner.Initialize(MapGenerator.instance.seed, coord);

            Vector3 chunkOrigin = chunkObject.transform.position;

            spawner.SpawnObjects(
                chunkObject.transform,
                chunkOrigin,
                cachedHeightMap,
                cachedBiomeMap,
                cachedAltitudeMap,
                EndlessTerrain.instance.vertexSpacing,
                EndlessTerrain.instance.chunkSize,
                coord
            );

            decorationsSpawned = true;

            spawner.UpdateDetailVisibility(detailDistance, chunkObject.transform);
        }

        /// <summary>
        /// Nastaví viditelnost chunku (MeshRenderer enabled/disabled) a přepne LOD.
        /// Pokud chunk ještě není inicializován, pouze uloží požadovaný stav pro pozdější aplikaci.
        /// Dekorace se spawnou při prvním zviditelnění (dříve se u chunku
        /// inicializovaného jako skrytý nespawnuly nikdy).
        /// </summary>
        public void SetVisible(bool visible, int lod = 0)
        {
            this.visible = visible;
            currentLOD = lod;

            if (!initialized) return;

            if (meshRenderer != null)
                meshRenderer.enabled = visible;

            if (visible)
            {
                UpdateLOD(lod);
                if (!decorationsSpawned)
                    EnsureDecorations();
            }
        }

        /// <summary>
        /// Přepne vizuální mesh na danou LOD úroveň (mesh se případně líně vytvoří).
        /// Collider se zde NEřeší – řídí ho SetColliderActive podle vzdálenosti;
        /// dřívější přiřazování sharedMesh při každém přepnutí LOD vynucovalo
        /// opakovaný PhysX cooking plného meshe.
        /// </summary>
        public void UpdateLOD(int lod)
        {
            if (lodMeshes == null || meshFilter == null) return;

            currentLOD = Mathf.Clamp(lod, 0, lodMeshes.Length - 1);

            Mesh lodMesh = GetOrCreateLODMesh(currentLOD);
            if (lodMesh != null && meshFilter.sharedMesh != lodMesh)
                meshFilter.sharedMesh = lodMesh;
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

            if (colliderMesh != null)
            {
                GameObject.Destroy(colliderMesh);
                colliderMesh = null;
            }

            lodMeshes = null;
            lodMeshData = null;
            cachedHeightMap = null;
            cachedAltitudeMap = null;
            cachedBiomeMap = null;
            chunkSpawner = null;
            decorationsSpawned = false;
            MinTerrainHeight = float.MaxValue;
            disposed = true;
            initialized = false;
            onInitializedCallback = null;
        }
    }
}