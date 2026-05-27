using Orivilon.Core;
using Orivilon.Data;
using Orivilon.World.Biomes;
using Orivilon.World.Spawning;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Profiling;

namespace Orivilon.World.Terrain
{
    /// <summary>
    /// Singleton koordinující asynchronní generování dat terénu.
    /// Přijímá požadavky na MapData (výšková mapa) a MeshData (mesh pro každý LOD)
    /// a zpracovává je ve vláknech přes Task.Run. Výsledky fronty se zpracovávají
    /// na hlavním vlákně přes coroutiny (max N za snímek pro stabilní FPS).
    /// Flat shading se předpočítává ve vedlejším vlákně v BakeFlatShadingOnThread().
    /// </summary>
    public class MapGenerator : MonoBehaviour
    {
        /// <summary>Globální instance singletonu.</summary>
        public static MapGenerator instance;

        /// <summary>Reference na BiomeValuesGenerator pro generování teplotních a vlhkostních map.</summary>
        public BiomeValuesGenerator biomeValuesGenerator;

        /// <summary>Reference na ObjectSpawner pro spawn dekorací na chuncích.</summary>
        public ObjectSpawner objectSpawner;

        #region Settings
        /// <summary>Materiál aplikovaný na všechny terrain chunky.</summary>
        public Material terrainMaterial;

        /// <summary>Sbírka biomů pro výběr barvy a výšky terénu na základě klimatu.</summary>
        public BiomeCollection biomeCollection;

        /// <summary>Výšková hodnota hladiny moře v noise mapě (body pod touto hodnotou jsou podmořské).</summary>
        public float seaLevel = 0.4f;

        /// <summary>Posun noise mapy pro variaci mezi různými světy (generováno ze seedu).</summary>
        [Header("Noise Settings")]
        public Vector2 offset;

        /// <summary>Měřítko Perlin noise (větší = plynulejší terén, menší = detailnější).</summary>
        [Range(20, 10000)] public float noiseScale;

        /// <summary>Počet oktáv Perlin noise (více = detailnější ale výpočetně náročnější).</summary>
        [Range(1, 10)] public int octaves;

        /// <summary>Persistence kontroluje, jak rychle klesá amplituda každé oktávy (0–1).</summary>
        [Range(0, 1)] public float persistance;

        /// <summary>Lacunarity kontroluje, jak rychle roste frekvence každé oktávy (1–10).</summary>
        [Range(1, 10)] public float lacunarity;

        /// <summary>Seed světa pro deterministickou generaci noise mapy.</summary>
        public int seed;
        #endregion

        /// <summary>Velikost chunku synchronizovaná s EndlessTerrain.chunkSize.</summary>
        [HideInInspector] public int chunkSize;

        /// <summary>Počet LOD úrovní odvozený z EndlessTerrain.LODCount.</summary>
        private int lodCount;

        /// <summary>True pokud se povedlo převzít velikost chunku a LOD z EndlessTerrain.</summary>
        private bool terrainSettingsInitialized = false;

        /// <summary>Maximální počet hotových dat zpracovaných za jeden snímek (pro stabilní FPS).</summary>
        private const int MAX_CHUNKS_PROCESSED_PER_FRAME = 1;

        /// <summary>
        /// Převede světovou pozici na souřadnice chunku.
        /// </summary>
        public Vector2Int GetCoord(Vector3 position)
        {
            EnsureTerrainSettings();
            return new Vector2Int(Mathf.FloorToInt(position.x / chunkSize), Mathf.FloorToInt(position.z / chunkSize));
        }

        /// <summary>
        /// Vygeneruje výškovou mapu pro daný chunk pomocí Perlin noise.
        /// Výsledek je normalizován globálně pro konzistentní výšky přes všechny chunky.
        /// </summary>
        public float[,] GetHeightMap(Vector2Int coord)
        {
            EnsureTerrainSettings();
            return Noise.GenerateNoiseMap(chunkSize + 1, chunkSize + 1, seed, noiseScale, octaves, persistance, lacunarity, new Vector2(coord.x * chunkSize, coord.y * chunkSize) + offset, Noise.NormalizeMode.Global);
        }

        /// <summary>Fronta hotových MapData z vláken čekající na zpracování hlavním vláknem.</summary>
        Queue<ThreadInfo<MapData>> mapDataThreadInfoQueue = new Queue<ThreadInfo<MapData>>();

        /// <summary>Fronta hotových MeshData[] z vláken čekající na zpracování hlavním vláknem.</summary>
        public Queue<ThreadInfo<MeshData[]>> meshDataThreadInfoQueue = new Queue<ThreadInfo<MeshData[]>>();

        /// <summary>
        /// Inicializace singletonu. Nastaví seed ze selectedWorld nebo vygeneruje náhodný.
        /// Vypočte náhodný offset z PRNG seedu. Synchronizuje chunkSize a lodCount s EndlessTerrain.
        /// </summary>
        private void Awake()
        {
            instance = this;

            if (GameManager.selectedWorld != null)
            {
                seed = GameManager.selectedWorld.worldSeed;

                if (seed == 0 && !string.IsNullOrEmpty(GameManager.selectedWorld.seed))
                    seed = GetStableHashCode(GameManager.selectedWorld.seed);
            }

            if (seed == 0) seed = UnityEngine.Random.Range(int.MinValue, int.MaxValue);

            System.Random prng = new System.Random(seed);
            offset = new Vector2(prng.Next(100000, 10000000), prng.Next(100000, 10000000));

            EnsureTerrainSettings();
        }

        /// <summary>
        /// Stejný deterministický hash textového seedu jako používá WorldData.
        /// </summary>
        private int GetStableHashCode(string str)
        {
            if (string.IsNullOrEmpty(str)) return 0;

            unchecked
            {
                int hash = 0;
                foreach (char c in str)
                    hash = hash * 31 + c;
                return hash;
            }
        }

        /// <summary>
        /// Spustí coroutiny pro zpracování front MapData a MeshData.
        /// </summary>
        private void Start()
        {
            EnsureTerrainSettings();
            StartCoroutine(MapDataRoutine());
            StartCoroutine(MeshDataRoutine());
        }

        /// <summary>
        /// Synchronizuje MapGenerator s EndlessTerrain i v případě, že Awake proběhne ve špatném pořadí.
        /// </summary>
        private void EnsureTerrainSettings()
        {
            if (terrainSettingsInitialized && chunkSize > 0 && lodCount > 0)
                return;

            EndlessTerrain terrain = EndlessTerrain.instance;
            if (terrain == null)
                terrain = FindAnyObjectByType<EndlessTerrain>();

            if (terrain == null)
            {
                Debug.LogWarning("[MapGenerator] EndlessTerrain not available yet; terrain settings remain uninitialized.");
                return;
            }

            chunkSize = terrain.chunkSize;
            lodCount = Mathf.Max(1, terrain.LODCount);

            if (biomeCollection != null)
                biomeCollection.chunkSize = chunkSize;

            terrainSettingsInitialized = chunkSize > 0 && lodCount > 0;
            Debug.Log($"[MapGenerator] Terrain settings initialized: chunkSize={chunkSize}, lodCount={lodCount}");
        }

        /// <summary>
        /// Asynchronně požádá o vygenerování MapData pro daný chunk.
        /// Generování probíhá ve vedlejším vlákně, výsledek se zařadí do fronty.
        /// </summary>
        public void RequestMapData(Vector2Int coord, Action<MapData> callback)
        {
            EnsureTerrainSettings();
            Task.Run(() => MapDataThread(coord, callback));
        }

        /// <summary>
        /// Vygeneruje MapData (výšková mapa + souřadnice) ve vedlejším vlákně
        /// a zařadí výsledek do mapDataThreadInfoQueue.
        /// </summary>
        private void MapDataThread(Vector2Int coord, Action<MapData> callback)
        {
            MapData mapData = TerrainGenerator.CreateMapData(GetHeightMap(coord), coord);
            lock (mapDataThreadInfoQueue)
            {
                mapDataThreadInfoQueue.Enqueue(new ThreadInfo<MapData>(callback, mapData));
            }
        }

        /// <summary>
        /// Asynchronně požádá o vygenerování MeshData[] pro všechny LOD úrovně.
        /// Generování a flat shading probíhají ve vedlejším vlákně.
        /// </summary>
        public void RequestMeshData(MapData mapData, Action<MeshData[]> callback)
        {
            EnsureTerrainSettings();
            Task.Run(() => MeshDataThread(mapData, callback));
        }

        /// <summary>
        /// Vygeneruje MeshData[] pro všechny LOD úrovně ve vedlejším vlákně.
        /// Předpočítá flat shading (BakeFlatShadingOnThread) pro každý LOD.
        /// Loguje chyby pro prázdné nebo neplatné MeshData.
        /// Výsledek zařadí do meshDataThreadInfoQueue.
        /// </summary>
        private void MeshDataThread(MapData mapData, Action<MeshData[]> callback)
        {
            MeshData[] meshDataArray = TerrainGenerator.CreateMeshDataWithLOD(mapData, biomeCollection, lodCount);

            if (meshDataArray == null || meshDataArray.Length == 0)
            {
                Debug.LogError($"[MapGenerator] CreateMeshDataWithLOD vrátil null/empty pro chunk {mapData.chunkCoords}!");
                meshDataArray = new MeshData[0];
            }

            for (int i = 0; i < meshDataArray.Length; i++)
            {
                meshDataArray[i].BakeFlatShadingOnThread();
            }

            for (int i = 0; i < meshDataArray.Length; i++)
            {
                var md = meshDataArray[i];
                if (md.vertices == null || md.vertices.Length == 0 || md.triangles == null || md.triangles.Length == 0)
                {
                    Debug.LogError($"[MapGenerator] EMPTY MeshData for chunk {mapData.chunkCoords} LOD={md.lod} (index={i}). vertices={(md.vertices == null ? 0 : md.vertices.Length)} triangles={(md.triangles == null ? 0 : md.triangles.Length)}");
                }
            }

            lock (meshDataThreadInfoQueue)
            {
                meshDataThreadInfoQueue.Enqueue(new ThreadInfo<MeshData[]>(callback, meshDataArray));
            }
        }

        /// <summary>
        /// Coroutina zpracovávající frontu MapData na hlavním vlákně.
        /// Zpracuje max MAX_CHUNKS_PROCESSED_PER_FRAME * 2 položek za snímek.
        /// </summary>
        IEnumerator MapDataRoutine()
        {
            while (true)
            {
                int processed = 0;
                while (mapDataThreadInfoQueue.Count > 0 && processed < MAX_CHUNKS_PROCESSED_PER_FRAME * 2)
                {
                    ThreadInfo<MapData> threadInfo;
                    lock (mapDataThreadInfoQueue) threadInfo = mapDataThreadInfoQueue.Dequeue();
                    threadInfo.callback(threadInfo.parameter);
                    processed++;
                }
                yield return null;
            }
        }

        /// <summary>
        /// Coroutina zpracovávající frontu MeshData[] na hlavním vlákně.
        /// Zpracuje max MAX_CHUNKS_PROCESSED_PER_FRAME položek za snímek pro stabilní FPS.
        /// </summary>
        IEnumerator MeshDataRoutine()
        {
            while (true)
            {
                int processed = 0;
                while (meshDataThreadInfoQueue.Count > 0 && processed < MAX_CHUNKS_PROCESSED_PER_FRAME)
                {
                    ThreadInfo<MeshData[]> threadInfo;
                    lock (meshDataThreadInfoQueue) threadInfo = meshDataThreadInfoQueue.Dequeue();
                    threadInfo.callback(threadInfo.parameter);
                    processed++;
                }
                yield return null;
            }
        }
    }

    /// <summary>
    /// Generická struktura přenášející výsledek z vedlejšího vlákna na hlavní vlákno.
    /// Obsahuje callback k zavolání a data k předání.
    /// </summary>
    public struct ThreadInfo<T>
    {
        /// <summary>Callback zavolaný na hlavním vlákně s výsledkem.</summary>
        public readonly Action<T> callback;

        /// <summary>Data předaná callbacku.</summary>
        public readonly T parameter;

        public ThreadInfo(Action<T> callback, T parameter)
        {
            this.callback = callback;
            this.parameter = parameter;
        }
    }

    /// <summary>
    /// Struktura obsahující veškerá data pro vytvoření Unity Mesh objektu.
    /// Surová data (vertices, triangles, uvs, colors) se předpočítají ve vlákně
    /// přes BakeFlatShadingOnThread() do baked polí. CreateMesh() pak pouze
    /// přiřadí předpočítaná data bez nákladného flat shadingu na hlavním vlákně.
    /// </summary>
    public struct MeshData
    {
        /// <summary>Pole vrcholů meshe (před flat shadingem – sdílené).</summary>
        public Vector3[] vertices;

        /// <summary>Pole indexů trojúhelníků.</summary>
        public int[] triangles;

        /// <summary>Pole UV souřadnic.</summary>
        public Vector2[] uvs;

        /// <summary>Pole barev vrcholů (z biom color mapy).</summary>
        public Color[] colors;

        /// <summary>LOD úroveň tohoto meshe (0 = nejvyšší detail).</summary>
        public int lod;

        /// <summary>Předpočítané vrcholy pro flat shading (každý trojúhelník má vlastní vrcholy).</summary>
        public Vector3[] bakedVertices;

        /// <summary>Předpočítané UV pro flat shading.</summary>
        public Vector2[] bakedUVs;

        /// <summary>Předpočítané barvy pro flat shading.</summary>
        public Color[] bakedColors;

        /// <summary>Předpočítané indexy trojúhelníků pro flat shading.</summary>
        public int[] bakedTriangles;

        /// <summary>Předpočítané normály pro flat shading (jedna normála na trojúhelník).</summary>
        public Vector3[] bakedNormals;

        /// <summary>Příznak, zda byl flat shading předpočítán ve vlákně.</summary>
        private bool isBaked;

        /// <summary>
        /// Vytvoří MeshData se surovými daty. Baked pole jsou null dokud se nezavolá BakeFlatShadingOnThread().
        /// </summary>
        public MeshData(Vector3[] vertices, int[] triangles, Vector2[] uvs, Color[] colors, int lod)
        {
            this.vertices = vertices;
            this.triangles = triangles;
            this.uvs = uvs;
            this.colors = colors;
            this.lod = lod;

            this.bakedVertices = null;
            this.bakedUVs = null;
            this.bakedColors = null;
            this.bakedTriangles = null;
            this.bakedNormals = null;
            this.isBaked = false;
        }

        /// <summary>
        /// Předpočítá flat shading ve vedlejším vlákně.
        /// Každý trojúhelník dostane vlastní kopie vrcholů (duplikuje vrcholy sdílené mezi trojúhelníky).
        /// Normála každého trojúhelníku se vypočítá jako cross product dvou hran a aplikuje na všechny 3 vrcholy.
        /// Safe-guardy zabraňují indexování mimo rozsah polí.
        /// </summary>
        public void BakeFlatShadingOnThread()
        {
            if (vertices == null || triangles == null) return;

            bakedVertices = new Vector3[triangles.Length];
            bakedUVs = new Vector2[triangles.Length];
            bakedColors = new Color[triangles.Length];
            bakedTriangles = new int[triangles.Length];
            bakedNormals = new Vector3[triangles.Length];

            for (int i = 0; i < triangles.Length; i++)
            {
                int triIndex = triangles[i];

                bakedVertices[i] = (triIndex >= 0 && triIndex < vertices.Length) ? vertices[triIndex] : Vector3.zero;
                bakedUVs[i] = (uvs != null && triIndex >= 0 && triIndex < uvs.Length) ? uvs[triIndex] : Vector2.zero;
                if (colors != null && triIndex >= 0 && triIndex < colors.Length)
                    bakedColors[i] = colors[triIndex];
                else
                    bakedColors[i] = Color.white;

                bakedTriangles[i] = i;
            }

            if (bakedTriangles.Length >= 3)
            {
                for (int i = 0; i < bakedTriangles.Length; i += 3)
                {
                    if (i + 2 >= bakedTriangles.Length) break;
                    Vector3 a = bakedVertices[bakedTriangles[i]];
                    Vector3 b = bakedVertices[bakedTriangles[i + 1]];
                    Vector3 c = bakedVertices[bakedTriangles[i + 2]];
                    Vector3 normal = Vector3.Cross(b - a, c - a).normalized;
                    bakedNormals[bakedTriangles[i]] = normal;
                    bakedNormals[bakedTriangles[i + 1]] = normal;
                    bakedNormals[bakedTriangles[i + 2]] = normal;
                }
            }

            isBaked = true;
        }

        /// <summary>
        /// Vytvoří Unity Mesh z předpočítaných (baked) nebo surových dat.
        /// Pokud byl flat shading předpočítán (isBaked = true), pouze přiřadí pole – velmi rychlé.
        /// Jinak použije surová data s RecalculateNormals() – pomalejší záložní varianta.
        /// Vždy zavolá RecalculateBounds() pro správné frustum culling.
        /// </summary>
        /// <param name="v">Parametr zachován pro zpětnou kompatibilitu (nevyužit).</param>
        /// <returns>Nový Unity Mesh objekt.</returns>
        public Mesh CreateMesh(bool v)
        {
            Mesh mesh = new Mesh();

            if (isBaked)
            {
                if (bakedVertices == null || bakedTriangles == null || bakedVertices.Length == 0 || bakedTriangles.Length == 0)
                {
                    Debug.LogError($"[MeshData] CreateMesh: baked data invalid (lod={lod}). Returning empty mesh.");
                    return mesh;
                }

                mesh.vertices = bakedVertices;
                mesh.triangles = bakedTriangles;
                mesh.uv = bakedUVs ?? new Vector2[bakedVertices.Length];
                mesh.colors = bakedColors ?? new Color[bakedVertices.Length];
                mesh.normals = bakedNormals;
            }
            else
            {
                if (vertices == null || triangles == null || vertices.Length == 0 || triangles.Length == 0)
                {
                    Debug.LogError($"[MeshData] CreateMesh: raw data invalid (lod={lod}). Returning empty mesh.");
                    return mesh;
                }

                mesh.vertices = vertices;
                mesh.triangles = triangles;
                mesh.uv = uvs ?? new Vector2[vertices.Length];
                mesh.colors = colors ?? new Color[vertices.Length];
                mesh.RecalculateNormals();
            }

            mesh.RecalculateBounds();
            return mesh;
        }
    }

    /// <summary>
    /// Struktura sdružující výškovou mapu a souřadnice chunku pro předání mezi systémy.
    /// </summary>
    public struct MapData
    {
        /// <summary>2D pole výškových hodnot z Perlin noise (rozměr (chunkSize+1) × (chunkSize+1)).</summary>
        public float[,] heightMap;

        /// <summary>Souřadnice chunku v mřížce světa.</summary>
        public Vector2Int chunkCoords;

        public MapData(float[,] heightMap, Vector2Int chunkCoords)
        {
            this.heightMap = heightMap;
            this.chunkCoords = chunkCoords;
        }
    }
}
