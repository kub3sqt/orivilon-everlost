using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System.Linq;
using UnityEngine.SceneManagement;
using Orivilon.SaveSystem;
using Orivilon.Core;
using Orivilon.World.Objects;
using Orivilon.World.Terrain;
using Orivilon.World.Generation;

namespace Orivilon.World.Spawning
{
    /// <summary>
    /// Výčet všech typů biomů ve světě hry.
    /// Spawn objektů se omezuje na povolené biomy v každém SpawnableObject.
    /// </summary>
    [System.Serializable]
    public enum BiomeType
    {
        None, Badlands, ColdOcean, Coldlands, Desert, Grasslands, Hills, IceLands, RainyFields, Savanna, SnowyLands, Swamps, TemperateOcean
    }

    /// <summary>
    /// Kategorie spawnovatelných objektů pro oddělené limity spawnu.
    /// Grass má vlastní limit maxGrassPerChunk, ostatní sdílí maxObjectsPerChunk.
    /// </summary>
    [System.Serializable]
    public enum SpawnCategory
    {
        Trees, Stones, Grass, SmallObjects
    }

    /// <summary>
    /// Konfigurace jednoho typu spawnovatelného objektu.
    /// Definuje prefab, kategorii, pravděpodobnost, výškový a sklonový rozsah,
    /// měřítko, povolené biomy a volitelnou Perlin noise hustotu.
    /// </summary>
    [System.Serializable]
    public class SpawnableObject
    {
        /// <summary>Název objektu pro přehlednost v Inspektoru.</summary>
        public string name;

        /// <summary>Prefab, jehož instance se spawnují do světa.</summary>
        public GameObject prefab;

        /// <summary>Kategorie objektu pro oddělené počítání limitů.</summary>
        public SpawnCategory category;

        /// <summary>Pravděpodobnost spawnu na každém testovaném bodu (0–100 %).</summary>
        [Range(0f, 100f)] public float spawnChance = 0.2f;

        /// <summary>Minimální výška noise mapy pro spawn (0–1).</summary>
        public float minHeight = 0f;

        /// <summary>Maximální výška noise mapy pro spawn (0–1).</summary>
        public float maxHeight = 1f;

        /// <summary>Minimální sklon terénu ve stupních (0 = rovina).</summary>
        public float minSlope = 0f;

        /// <summary>Maximální sklon terénu ve stupních (90 = svislá stěna).</summary>
        public float maxSlope = 30f;

        /// <summary>Rozsah náhodného měřítka (x = minimum, y = maximum).</summary>
        public Vector2 scaleRange = new Vector2(0.8f, 1.2f);

        /// <summary>Povolené biomy pro tento typ objektu. Objekt se nespawní mimo tyto biomy.</summary>
        public List<BiomeType> allowedBiomes = new List<BiomeType>();

        /// <summary>Pokud true, hustota spawnu se řídí Perlin noise místo čistě náhodné pravděpodobnosti.</summary>
        public bool usePerlinDensity = false;

        /// <summary>Měřítko Perlin noise pro hustotu spawnu.</summary>
        public float densityScale = 0.05f;

        /// <summary>Práh noise hodnoty pro spawn (body pod prahem se přeskočí).</summary>
        public float densityThreshold = 0.5f;

        /// <summary>Pokud true, objekt se otočí podle normály terénu (přizpůsobí sklon). Jinak stojí svisle.</summary>
        public bool rotateToTerrain = true;

        /// <summary>
        /// Maximální vzdálenost (v chuncích od hráče), do které je objekt fyzicky ve scéně.
        /// 0 = bez omezení (objekt existuje ve všech viditelných chuncích – původní chování).
        /// Vhodné pro malé objekty (klacky, kamínky, houby), které v dálce nejsou vidět,
        /// ale jako GameObjecty stojí výkon. Tráva se řídí globálním EndlessTerrain.grassDistance.
        /// </summary>
        [Tooltip("Max. vzdálenost v chuncích, do které je objekt ve scéně. 0 = neomezeno.")]
        public int maxViewDistanceChunks = 0;
    }

    /// <summary>
    /// Singleton (DontDestroyOnLoad) zajišťující deterministický spawn objektů na terén.
    /// Pro každý chunk generuje objekty ze seedu světa – objekty jsou vždy na stejném místě.
    /// Spravuje interní stav spawnutých chunků a umožňuje jejich serializaci pro save systém.
    /// Při přechodu mezi scénami se přenačte seed ze souboru pro konzistenci.
    /// </summary>
    public partial class ObjectSpawner : MonoBehaviour
    {
        /// <summary>Globální instance singletonu.</summary>
        public static ObjectSpawner Instance { get; private set; }

        /// <summary>
        /// Pokud true, ignoruje seed ze GameManager a použije debugSeedValue.
        /// Určeno pro testování konkrétních světů bez nutnosti vytváření save souborů.
        /// </summary>
        [Header("DEBUG TEST")]
        [Tooltip("Pokud je zaškrtnuto, ignoruje se GameManager a použije se seed níže.")]
        public bool useDebugSeed = false;

        /// <summary>Textový seed pro debug mód.</summary>
        public string debugSeedValue = "TEST_SEED";

        /// <summary>Globální násobitel měřítka všech spawnutých objektů (výchozí 10).</summary>
        [Header("Spawn Settings")]
        [Range(0.1f, 100f)] public float globalScaleMultiplier = 10f;

        /// <summary>Seznam všech konfigurovatelných typů spawnovatelných objektů.</summary>
        public List<SpawnableObject> spawnables = new List<SpawnableObject>();

        /// <summary>Maximální počet ne-travních objektů na jeden chunk.</summary>
        [Header("Object Limits")]
        public int maxObjectsPerChunk = 100;

        /// <summary>Maximální počet travních objektů na jeden chunk.</summary>
        public int maxGrassPerChunk = 2500;

        /// <summary>Minimální výška noise mapy pro jakýkoliv spawn (simuluje hladinu moře).</summary>
        public float seaLevel = 0f;

        [Header("Vegetation Distribution")]
        [Tooltip("Měřítko velkých lesních oblastí. Menší číslo = větší souvislé lesy.")]
        public float forestNoiseScale = 0.008f;

        [Tooltip("Práh lesní noise mapy. Vyšší číslo = méně lesa, nižší číslo = více lesa.")]
        [Range(0f, 1f)] public float forestThreshold = 0.42f;

        [Tooltip("Násobitel hustoty trávy v biomech, kde je povolená.")]
        [Range(0f, 2f)] public float grassDensityMultiplier = 1f;

        /// <summary>Layer maska pro kontrolu kolizí při spawnu (zabraňuje překrývání objektů).</summary>
        [Header("Collision Settings")]
        public LayerMask spawnCollisionMask;

        /// <summary>Předalokovaný buffer pro OverlapSphere (zabraňuje GC alokacím v hlavní smyčce).</summary>
        private readonly Collider[] overlapBuffer = new Collider[4];

        /// <summary>Výška nad terénem, ze které se vysílají raycasto dolů pro hledání povrchu.</summary>
        /// <summary>
        /// Svislé okno, ve kterém se vůbec osazuje. Dřív to byl pevný rozsah odvozený
        /// od raycastu (⟨−50, 50⟩ m); voxelový svět sahá výš, takže je to teď nastavitelné.
        /// </summary>
        public float surfaceYMin = -256f;

        /// <inheritdoc cref="surfaceYMin"/>
        public float surfaceYMax = 512f;

        /// <summary>
        /// Dohled trávy v chuncích, když neběží EndlessTerrain. Voxelový sloupec je 32 m,
        /// takže 4 = 128 m, což odpovídá původním 2 chunkům po 64 m.
        /// </summary>
        public int grassDistanceChunks = 4;

        private const float raycastHeight = 50f;

        /// <summary>Maximální délka raycastu pro hledání povrchu.</summary>
        private const float raycastMax = 100f;

        /// <summary>Poloměr overlap sphery pro kontrolu volného místa před spawnем.</summary>
        private const float overlapRadius = 25f;

        /// <summary>Slovník uchovávající stav spawnutých chunků (koordináty → stav).</summary>
        private Dictionary<Vector2Int, ChunkSpawnState> spawnedChunks = new Dictionary<Vector2Int, ChunkSpawnState>();

        /// <summary>
        /// Pokud true, tráva a SmallObjects nevrhají stíny (jejich stíny nejsou okem
        /// rozlišitelné, ale zdvojnásobovaly počet draw callů ve shadow passu).
        /// </summary>
        [Tooltip("Vypnout vrhání stínů pro trávu a malé objekty (velká úspora výkonu).")]
        public bool disableShadowsForSmallDecorations = true;

        /// <summary>Maximální počet instancí vytvořených za jeden snímek při spawnu chunku.</summary>
        [Tooltip("Kolik dekorací se smí instancovat za jeden snímek (rozkládá zátěž).")]
        public int maxInstantiatesPerFrame = 25;

        [Header("Instancing trávy")]
        [Tooltip("Kreslit trávu přes GPU instancing místo jednotlivých GameObjectů. " +
                 "Vypnutím se vrátí staré chování (a s ním i tisíce batchů).")]
        public bool instanceGrass = true;

        /// <summary>
        /// Dávky trávy poslané do <see cref="VegetationRenderer"/>: index spawnable → úchyt.
        /// Drží se jen pro sloupce v dosahu trávy; mimo něj se pole matic zahodí.
        /// </summary>
        [System.NonSerialized] private readonly Dictionary<int, int> grassBatches = new Dictionary<int, int>(4);

        /// <summary>
        /// Mesh + materiál vytažené z prefabu, sdílené všemi sloupci. Klíč je prefab.
        ///
        /// <para>Statické schválně: materiál se musí vyrobit JEDNOU. Kdyby si ho každý
        /// sloupec vytvořil sám, měl by každý vlastní instanci téhož materiálu a GPU by
        /// je nemohlo sloučit – instancing by se tím zrušil sám.</para>
        /// </summary>
        private static readonly Dictionary<GameObject, InstancedSource> instancedSources =
            new Dictionary<GameObject, InstancedSource>();

        /// <summary>Co je potřeba ke kreslení jednoho druhu vegetace bez GameObjectu.</summary>
        private struct InstancedSource
        {
            public Mesh mesh;
            public Material material;
            public int layer;

            /// <summary>
            /// Transform meshe vůči kořeni prefabu. Mesh bývá na potomkovi s vlastním
            /// posunem; bez tohohle by tráva vyrostla posunutá proti tomu, kde ji
            /// GameObjectová cesta kreslila.
            /// </summary>
            public Matrix4x4 offset;

            /// <summary>
            /// Nemá <c>HarvestableObject</c> ani <c>PickupItem</c>, takže se s ním nedá nijak
            /// pracovat – existuje jen proto, aby byl vidět, a případně aby do něj šlo vrazit.
            /// </summary>
            public bool passive;

            /// <summary>Má collider, takže GameObject musí vzniknout i bez rendereru.</summary>
            public bool hasCollider;

            public bool Valid => mesh != null && material != null;
        }

        /// <summary>
        /// Matice pasivních propů čekající na odeslání rendereru, po druzích.
        /// Plní se ve spawn rutině a odesílá se na jejím konci – jedna registrace
        /// na druh a sloupec místo registrace po každém kameni.
        /// </summary>
        [System.NonSerialized] private readonly Dictionary<int, List<Matrix4x4>> propMatrices =
            new Dictionary<int, List<Matrix4x4>>(8);

        /// <summary>Dávky pasivních propů tohoto sloupce: index spawnable → úchyt.</summary>
        [System.NonSerialized] private readonly Dictionary<int, int> propBatches = new Dictionary<int, int>(8);

        [Tooltip("Kreslit PASIVNÍ propy (kameny, klacky, keře bez interakce) instancovaně. " +
                 "Collidery zůstávají, mizí jen renderer – interaktivní objekty se nedotýká.")]
        public bool instancePassiveProps = true;

        /// <summary>
        /// Dekorace s omezeným dohledem (tráva + objekty s maxViewDistanceChunks > 0),
        /// deterministicky vypočtené v SpawnObjects. Fyzicky se instancují
        /// až přes UpdateDetailVisibility podle vzdálenosti chunku od hráče.
        /// </summary>
        [System.NonSerialized] private readonly List<PendingDetailData> pendingDetails = new List<PendingDetailData>();

        /// <summary>Instancované objekty pending dekorací (index odpovídá pendingDetails; null = neinstancováno).</summary>
        [System.NonSerialized] private readonly List<GameObject> spawnedDetails = new List<GameObject>();

        /// <summary>Poslední vzdálenost chunku od hráče předaná z EndlessTerrain (v chuncích).</summary>
        private int lastDetailDistance = int.MaxValue;

        /// <summary>True dokud běží coroutine SpawnObjectsRoutine (kompletní data ještě nejsou).</summary>
        private bool spawnRoutineRunning = false;

        /// <summary>
        /// Kompletní deterministická data jedné odložené dekorace –
        /// transform je vypočtený předem, instancování je pak jen Instantiate.
        /// </summary>
        private struct PendingDetailData
        {
            public int spawnableIndex;
            public Vector3 position;
            public Quaternion rotation;
            public Vector3 scale;
            public long objectHash;
            public Vector2Int chunkCoord;
            /// <summary>Jen v dosahu trávy (sebratelné kamínky) – daleko by byly zbytečné GameObjecty.</summary>
            public bool nearOnly;
        }

        /// <summary>Numerický seed světa pro deterministickou generaci objektů.</summary>
        /// <summary>
        /// Seed světa. STATICKÝ: spawner se instancuje jednou na sloupec terénu, takže
        /// instanční pole by znamenalo čtení save souboru z disku pro každý sloupec.
        /// </summary>
        private static int worldSeed = 0;

        /// <summary>Jestli už seed někdo načetl. Brání opakovanému čtení z disku.</summary>
        private static bool worldSeedReady = false;

        /// <summary>Uložená souřadnice chunku pro zpětnou kompatibilitu se starším rozhraním.</summary>
        private Vector2Int chunkCoord;

        /// <summary>Příznak spawnu pro zpětnou kompatibilitu.</summary>
        private bool objectsSpawned = false;

        /// <summary>Počítadlo trávy pro zpětnou kompatibilitu.</summary>
        private int grassPlaced = 0;

        /// <summary>Počítadlo ostatních objektů pro zpětnou kompatibilitu.</summary>
        private int otherObjectsPlaced = 0;

        /// <summary>
        /// Vnitřní stav spawnutého chunku – souřadnice, počty objektů a seznam spawnnutých dat.
        /// </summary>
        [System.Serializable]
        public class ChunkSpawnState
        {
            /// <summary>Souřadnice chunku.</summary>
            public Vector2Int chunkCoord;

            /// <summary>Celkový počet spawnutých objektů (bez trávy).</summary>
            public int totalObjects;

            /// <summary>Počet spawnutých travních objektů.</summary>
            public int grassCount;

            /// <summary>
            /// True pokud byl stav načten ze save souboru (ne fyzicky vygenerován ve scéně).
            /// Chunky s tímto příznakem se znovu vygenerují při příštím SpawnObjects volání.
            /// </summary>
            public bool isLoadedFromSave = false;

            /// <summary>Detailní seznam dat každého spawnutého objektu.</summary>
            public List<SpawnedObjectData> spawnedObjects = new List<SpawnedObjectData>();
        }

        /// <summary>
        /// Data jednoho spawnutého objektu pro serializaci a obnovu.
        /// </summary>
        [System.Serializable]
        public class SpawnedObjectData
        {
            /// <summary>Název prefabu (pro identifikaci při načítání).</summary>
            public string prefabName;

            /// <summary>Světová pozice objektu.</summary>
            public Vector3 position;

            /// <summary>Měřítko objektu.</summary>
            public Vector3 scale;

            /// <summary>Rotace objektu.</summary>
            public Quaternion rotation;

            /// <summary>Kategorie objektu.</summary>
            public SpawnCategory category;

            /// <summary>Index v poli spawnables (pro rychlé nalezení konfigurace).</summary>
            public int spawnableIndex;
        }

        /// <summary>
        /// Singleton inicializace – pokud instance již existuje, tento objekt se zničí.
        /// </summary>
        private void Awake()
        {
            if (Instance == null)
            {
                Instance = this;
            }
        }

        private void OnDestroy()
        {
            ReleaseGrassBatches();
            ReleasePropBatches();

            if (Instance == this)
                Instance = null;
        }

        /// <summary>
        /// Inicializuje seed světa při startu.
        /// </summary>
        private void Start()
        {
            // Jen jednou za běh – spawner je instancovaný na každý sloupec terénu.
            if (!worldSeedReady) InitializeWorldSeed();
        }

        /// <summary>
        /// Přihlásí se na událost načtení scény pro reset seedu při přechodu do Game scény.
        /// </summary>
        private void OnEnable()
        {
            SceneManager.sceneLoaded += OnSceneLoaded;
        }

        /// <summary>
        /// Odhlásí se z události načtení scény při deaktivaci.
        /// </summary>
        private void OnDisable()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
        }

        /// <summary>
        /// Při načtení Game scény vyčistí stav spawnutých chunků a přenačte seed.
        /// Nutné protože Start() se u singletonu volá pouze jednou za celý běh aplikace.
        /// </summary>
        private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            if (scene.name == "Game")
            {
                Debug.Log("[SPAWNER] Scene loaded - Reloading Seed and Cleaning up...");
                spawnedChunks.Clear();
                InitializeWorldSeed();
            }
        }

        /// <summary>
        /// Starší metoda pro zpětnou kompatibilitu – inicializuje spawn pro konkrétní chunk.
        /// </summary>
        public void Initialize(int seed, Vector2Int coord)
        {
            chunkCoord = coord;
            objectsSpawned = false;

            // Seed musí být načtený DŘÍV, než se poprvé losuje. Start() by se u čerstvě
            // instancovaného spawneru spustil až na konci snímku, tedy po SpawnObjects,
            // a první sloupec by se osadil s nulovým seedem – jinak než všechny ostatní.
            if (!worldSeedReady) InitializeWorldSeed();

            ClearSpawnedObjects();
        }

        /// <summary>
        /// Inicializuje numerický seed světa pro deterministický spawn.
        /// Priorita: 1. Debug seed (pokud zapnut), 2. Seed ze save souboru na disku,
        /// 3. Seed z GameManager.selectedWorld v paměti, 4. Výchozí "DEFAULT_SEED".
        /// Načítání ze souboru má přednost i před GameManager, aby byl seed
        /// správný i po návratu z hlavního menu.
        /// </summary>
        public void InitializeWorldSeed()
        {
            worldSeedReady = true;

            if (useDebugSeed)
            {
                worldSeed = GetStableHashCode(debugSeedValue);
                Debug.LogWarning($"[SPAWNER] DEBUG MODE - Using debug seed: {worldSeed}");
                return;
            }

            WorldSaveData worldData = SaveSystem.SaveSystem.LoadWorldData();

            if (worldData != null)
            {
                worldSeed = worldData.seedNumeric;
                Debug.Log($"[SPAWNER] RELOADED seed from disk: {worldSeed}");
            }
            else if (GameManager.instance != null && GameManager.selectedWorld != null)
            {
                worldSeed = GameManager.selectedWorld.worldSeed;
                if (worldSeed == 0 && !string.IsNullOrEmpty(GameManager.selectedWorld.seed))
                {
                    worldSeed = GetStableHashCode(GameManager.selectedWorld.seed);
                }
                Debug.Log($"[SPAWNER] Using GameManager seed: {worldSeed}");
            }
            else
            {
                worldSeed = GetStableHashCode("DEFAULT_SEED");
            }
        }

        /// <summary>
        /// Vypočítá deterministický hash textového sedu.
        /// Algoritmus: hash = hash * 31 + c pro každý znak (unchecked přetečení).
        /// </summary>
        /// <param name="str">Textový seed k hashování.</param>
        /// <returns>Deterministický int hash.</returns>
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
        /// Vrátí deterministický Random pro celý chunk (odvozený ze seedu světa a souřadnic chunku).
        /// </summary>
        private System.Random GetChunkRandom(Vector2Int chunkCoord)
        {
            int chunkSeed = worldSeed + chunkCoord.x * 1619 + chunkCoord.y * 31337;
            return new System.Random(chunkSeed);
        }

        /// <summary>
        /// Předpočítá indexy spawnovatelných objektů podle biomu.
        /// Nahrazuje lineární allowedBiomes.Contains(biome) v hlavní spawn smyčce –
        /// při velkém počtu spawnables se pro každou buňku prochází jen relevantní podmnožina.
        /// Indexy v seznamech zůstávají vzestupně seřazené, takže pořadí vyhodnocování
        /// (a tedy determinismus spawnu) je stejné jako u původního plného průchodu.
        /// </summary>
        private Dictionary<BiomeType, List<int>> BuildBiomeCandidates()
        {
            var map = new Dictionary<BiomeType, List<int>>();
            for (int si = 0; si < spawnables.Count; si++)
            {
                var s = spawnables[si];
                if (s == null || s.prefab == null || s.allowedBiomes == null) continue;

                for (int bi = 0; bi < s.allowedBiomes.Count; bi++)
                {
                    BiomeType biome = s.allowedBiomes[bi];
                    if (!map.TryGetValue(biome, out List<int> list))
                    {
                        list = new List<int>();
                        map[biome] = list;
                    }
                    // ochrana proti duplicitnímu biomu v jednom záznamu
                    if (list.Count == 0 || list[list.Count - 1] != si)
                        list.Add(si);
                }
            }
            return map;
        }

        /// <summary>
        /// Vrátí deterministický Random pro konkrétní buňku v chunku.
        /// Každá kombinace (chunk, x, z) dá vždy stejný výsledek.
        /// </summary>
        private System.Random GetCellRandom(Vector2Int chunkCoord, int x, int z)
        {
            int cellSeed = worldSeed + chunkCoord.x * 73856093 + chunkCoord.y * 19349663 + x * 83492791 + z * 116129781;
            return new System.Random(cellSeed);
        }

        /// <summary>
        /// Zjistí, zda má být chunk vygenerován (ještě nebyl fyzicky spawnut).
        /// </summary>
        public bool ShouldSpawnChunk(Vector2Int chunkCoord)
        {
            return !spawnedChunks.ContainsKey(chunkCoord);
        }

        /// <summary>
        /// Starší přetížení SpawnObjects bez explicitního chunkCoord pro zpětnou kompatibilitu.
        /// Odvodí chunkCoord z chunkOrigin a deleguje na nové přetížení.
        /// </summary>
        public void SpawnObjects(
            Transform parent,
            Vector3 chunkOrigin,
            float[,] heightMap,
            BiomeType[,] biomeMap,
            float[,] altitudeMap,
            float vertexSpacing,
            int chunkSize)
        {
            int chunkX = Mathf.FloorToInt(chunkOrigin.x / (chunkSize * vertexSpacing));
            int chunkZ = Mathf.FloorToInt(chunkOrigin.z / (chunkSize * vertexSpacing));
            Vector2Int coord = new Vector2Int(chunkX, chunkZ);

            SpawnObjects(parent, chunkOrigin, heightMap, biomeMap, altitudeMap, vertexSpacing, chunkSize, coord);
        }

        /// <summary>
        /// Hlavní metoda spawnu objektů pro jeden chunk.
        /// Pro každý bod mřížky chunku (krok 2) vyhodnotí všechny spawnovatelné objekty:
        /// výšku, biom, Perlin hustotu, pravděpodobnost, povrch a sklon z altitude mapy,
        /// jitter pozice, overlap check a nakonec instantiuje objekt.
        /// Pozice povrchu se čte z altitude mapy (dříve tisíce Physics.Raycast na hlavním
        /// vlákně; na mřížkových bodech dává altitude mapa identickou výšku jako raycast).
        /// Tráva a objekty s maxViewDistanceChunks se neinstancují hned – uloží se jako
        /// pending a jejich přítomnost řídí UpdateDetailVisibility podle vzdálenosti od hráče.
        /// Pořadí čerpání náhodných čísel je zachováno kvůli determinismu světa.
        /// Stav spawnu se uloží do spawnedChunks slovníku.
        /// Pokud chunk byl jen "načten ze save" (isLoadedFromSave), přegeneruje se fyzicky.
        /// </summary>
        public void SpawnObjects(
            Transform parent,
            Vector3 chunkOrigin,
            float[,] heightMap,
            BiomeType[,] biomeMap,
            float[,] altitudeMap,
            float vertexSpacing,
            int chunkSize,
            Vector2Int chunkCoord)
        {
            if (altitudeMap == null)
            {
                Debug.LogError($"[SPAWNER] SpawnObjects: altitudeMap je null pro chunk {chunkCoord} – spawn přeskočen.");
                return;
            }
            if (spawnedChunks.TryGetValue(chunkCoord, out ChunkSpawnState existingState))
            {
                if (!existingState.isLoadedFromSave)
                {
                    return;
                }
            }

            if (spawnables == null || spawnables.Count == 0) return;
            if (biomeMap == null) return;
            if (spawnRoutineRunning) return;

            spawnRoutineRunning = true;
            StartCoroutine(SpawnObjectsRoutine(parent, chunkOrigin, heightMap, biomeMap, altitudeMap, vertexSpacing, chunkSize, chunkCoord));
        }

        /// <summary>
        /// Vlastní tělo spawnu jako coroutine – instancování je rozložené do více snímků
        /// (max maxInstantiatesPerFrame za snímek), aby vjezd do nové oblasti nezpůsoboval
        /// propady FPS. Yield probíhá VÝHRADNĚ mezi buňkami mřížky: každá buňka má vlastní
        /// deterministický Random (GetCellRandom), takže pořadí čerpání náhodných čísel
        /// je identické s jednorázovým průchodem a determinismus světa je zachován.
        /// </summary>
        private IEnumerator SpawnObjectsRoutine(
            Transform parent,
            Vector3 chunkOrigin,
            float[,] heightMap,
            BiomeType[,] biomeMap,
            float[,] altitudeMap,
            float vertexSpacing,
            int chunkSize,
            Vector2Int chunkCoord)
        {
            System.Random chunkRandom = GetChunkRandom(chunkCoord);

            float noiseOffsetX = (float)chunkRandom.NextDouble() * 10000f;
            float noiseOffsetY = (float)chunkRandom.NextDouble() * 10000f;

            int grassPlaced = 0;
            int otherObjectsPlaced = 0;
            Dictionary<BiomeType, List<int>> biomeCandidates = BuildBiomeCandidates();
            List<Vector3> reservedSpawnPositions = new List<Vector3>(maxObjectsPerChunk + maxGrassPerChunk);

            // Regenerace chunku (např. po načtení ze save) – vyčistit staré pending dekorace.
            if (detailApplyRoutine != null)
            {
                StopCoroutine(detailApplyRoutine);
                detailApplyRoutine = null;
            }
            DespawnAllDetails();
            pendingDetails.Clear();
            spawnedDetails.Clear();

            int altitudeSizeX = altitudeMap.GetLength(0);
            int altitudeSizeZ = altitudeMap.GetLength(1);
            int instantiatedThisFrame = 0;

            ChunkSpawnState chunkState = new ChunkSpawnState
            {
                chunkCoord = chunkCoord,
                spawnedObjects = new List<SpawnedObjectData>(),
                isLoadedFromSave = false
            };

            for (int x = 0; x < chunkSize; x += 2)
            {
                for (int z = 0; z < chunkSize; z += 2)
                {
                    if (otherObjectsPlaced >= maxObjectsPerChunk && grassPlaced >= maxGrassPerChunk) break;

                    float height = heightMap[x, z];
                    if (height < seaLevel) continue;

                    BiomeType biome = biomeMap[x, z];
                    if (!biomeCandidates.TryGetValue(biome, out List<int> cellCandidates)) continue;
                    System.Random cellRandom = GetCellRandom(chunkCoord, x, z);

                    for (int ci = 0; ci < cellCandidates.Count; ci++)
                    {
                        int si = cellCandidates[ci];
                        var s = spawnables[si];
                        if (s == null || s.prefab == null) continue;

                        if (s.category == SpawnCategory.Grass && grassPlaced >= maxGrassPerChunk) continue;
                        else if (s.category != SpawnCategory.Grass && otherObjectsPlaced >= maxObjectsPerChunk) continue;

                        if (height < s.minHeight || height > s.maxHeight) continue;

                        float worldX = chunkOrigin.x + x * vertexSpacing;
                        float worldZ = chunkOrigin.z + z * vertexSpacing;
                        float chance = GetSpawnChance(s);

                        if (s.category == SpawnCategory.Trees)
                        {
                            float forestNoise = Mathf.PerlinNoise((worldX + noiseOffsetX) * forestNoiseScale, (worldZ + noiseOffsetY) * forestNoiseScale);
                            if (forestNoise < forestThreshold) continue;

                            chance *= Mathf.InverseLerp(forestThreshold, 1f, forestNoise);
                        }
                        else if (s.category == SpawnCategory.Grass)
                        {
                            chance = Mathf.Clamp01(chance * grassDensityMultiplier);
                        }
                        else if (s.usePerlinDensity)
                        {
                            float nx = (chunkOrigin.x + x + noiseOffsetX) * s.densityScale;
                            float nz = (chunkOrigin.z + z + noiseOffsetY) * s.densityScale;
                            if (Mathf.PerlinNoise(nx, nz) > s.densityThreshold) continue;
                        }

                        if (chance < 0.999f)
                        {
                            if (cellRandom.NextDouble() > chance) continue;
                        }

                        // Výška povrchu přímo z altitude mapy – na mřížkových bodech je identická
                        // s tím, co dřív vracel raycast na terén, ale bez fyziky a bez závislosti
                        // na existenci collideru (vzdálené chunky už collider nemají).
                        if (x >= altitudeSizeX || z >= altitudeSizeZ) continue;
                        float surfaceY = altitudeMap[x, z];

                        // Stejné okno jako původní raycast (start v raycastHeight, délka raycastMax) –
                        // zachovává chování, kdy se na extrémně vysokém/nízkém terénu nespawnovalo.
                        if (surfaceY > surfaceYMax || surfaceY < surfaceYMin) continue;

                        Vector3 surfaceNormal = GetTerrainNormal(altitudeMap, x, z, vertexSpacing, altitudeSizeX, altitudeSizeZ);

                        float slope = Vector3.Angle(surfaceNormal, Vector3.up);
                        if (slope < s.minSlope || slope > s.maxSlope) continue;

                        float jitterPower = vertexSpacing * 0.45f;
                        float jitterX = ((float)cellRandom.NextDouble() - 0.5f) * jitterPower;
                        float jitterZ = ((float)cellRandom.NextDouble() - 0.5f) * jitterPower;

                        Vector3 spawnPos = new Vector3(worldX + jitterX, surfaceY, worldZ + jitterZ);

                        long objectHash = GetDeterministicObjectHash(chunkCoord, x, z, si);
                        if (SaveSystem.SaveSystem.IsObjectDestroyed(objectHash))
                        {
                            if (s.category != SpawnCategory.Grass)
                                reservedSpawnPositions.Add(spawnPos);
                            break;
                        }

                        if (s.category != SpawnCategory.Grass && HasReservedSpawnPosition(spawnPos, reservedSpawnPositions, GetOverlapRadius(s.category))) continue;

                        if (s.category != SpawnCategory.Grass)
                        {
                            int found = Physics.OverlapSphereNonAlloc(spawnPos, GetOverlapRadius(s.category), overlapBuffer, spawnCollisionMask, QueryTriggerInteraction.Ignore);
                            if (found > 0) continue;
                        }

                        float scaleVal = Mathf.Lerp(s.scaleRange.x, s.scaleRange.y, (float)cellRandom.NextDouble()) * globalScaleMultiplier;
                        float rotationY = (float)cellRandom.NextDouble() * 360f;

                        Vector3 finalScale = new Vector3(scaleVal, scaleVal * 1.3f, scaleVal);

                        // Stejná matematika jako původní: rotation = base * Rotate(up, rotationY, Space.Self)
                        Quaternion baseRotation = s.rotateToTerrain
                            ? Quaternion.FromToRotation(Vector3.up, surfaceNormal)
                            : Quaternion.identity;
                        Quaternion finalRotation = baseRotation * Quaternion.AngleAxis(rotationY, Vector3.up);

                        // ── mikro-biom: co se tu smí objevit ──────────────────────
                        //
                        // Až TADY, po všech losech. Kdyby se filtrovalo dřív, spotřebovalo by
                        // se jiné množství náhodných čísel a rozmístění by se v celém sloupci
                        // posunulo – determinismus stojí na tom, že pořadí losů je pevné.
                        if (!MicroAllows(s, spawnPos)) continue;

                        if (s.category == SpawnCategory.Grass || s.maxViewDistanceChunks > 0)
                        {
                            // Dekorace s omezeným dohledem se neinstancují hned – transform je
                            // deterministicky spočtený, fyzicky vzniknou až přes UpdateDetailVisibility
                            // podle vzdálenosti chunku od hráče.
                            pendingDetails.Add(new PendingDetailData
                            {
                                spawnableIndex = si,
                                position = spawnPos,
                                rotation = finalRotation,
                                scale = finalScale,
                                objectHash = objectHash,
                                chunkCoord = chunkCoord
                            });
                        }
                        else
                        {
                            // ── pasivní prop: kreslí ho instancer, GameObject je tu jen kvůli collideru ──
                            //
                            // Kámen ani suchý keř nemá HarvestableObject ani PickupItem, takže z něj
                            // hráč nic nedostane a jediné, co po něm chce, je do něj nevejít. Renderer
                            // proto zhasne a tvar pošleme rendereru; když prefab nemá ani collider,
                            // nevzniká GameObject vůbec. Tohle je tam, kde se batche ve skutečnosti
                            // sypaly – kamenů je ve spawnables 42 druhů proti 19 interaktivním.
                            InstancedSource psrc = instancePassiveProps && s.category != SpawnCategory.Grass
                                ? GetInstancedSource(s.prefab, s.category)
                                : default;

                            if (psrc.Valid && psrc.passive)
                            {
                                if (!propMatrices.TryGetValue(si, out List<Matrix4x4> plist))
                                {
                                    plist = new List<Matrix4x4>(64);
                                    propMatrices[si] = plist;
                                }
                                plist.Add(Matrix4x4.TRS(spawnPos, finalRotation, finalScale) * psrc.offset);

                                if (psrc.hasCollider)
                                {
                                    GameObject shell = Instantiate(s.prefab, spawnPos, Quaternion.identity, parent);
                                    Transform st = shell.transform;
                                    st.localScale = finalScale;
                                    st.rotation = finalRotation;

                                    StripRenderers(shell);

                                    DeterministicObjectId sid = shell.GetComponent<DeterministicObjectId>();
                                    if (sid == null) sid = shell.AddComponent<DeterministicObjectId>();
                                    sid.Initialize(objectHash, chunkCoord);

                                    instantiatedThisFrame++;
                                }
                            }
                            else
                            {
                                GameObject go = Instantiate(s.prefab, spawnPos, Quaternion.identity, parent);
                                Transform t = go.transform;
                                t.localScale = finalScale;
                                t.rotation = finalRotation;

                                ApplySmallDecorationTweaks(go, s.category);
                                ApplyMicroLook(go, s.category, spawnPos);
                                ApplyTrunkLook(go, s.category);

                                DeterministicObjectId id = go.GetComponent<DeterministicObjectId>();
                                if (id == null) id = go.AddComponent<DeterministicObjectId>();
                                id.Initialize(objectHash, chunkCoord);

                                instantiatedThisFrame++;
                            }
                        }

                        chunkState.spawnedObjects.Add(new SpawnedObjectData
                        {
                            prefabName = s.prefab.name,
                            position = spawnPos,
                            scale = finalScale,
                            rotation = finalRotation,
                            category = s.category,
                            spawnableIndex = si
                        });

                        if (s.category == SpawnCategory.Grass) grassPlaced++;
                        else otherObjectsPlaced++;

                        if (s.category != SpawnCategory.Grass)
                            reservedSpawnPositions.Add(spawnPos);
                        break;
                    }

                    // Yield POUZE mezi buňkami – uvnitř buňky by přerušil RNG sekvenci.
                    if (instantiatedThisFrame >= Mathf.Max(1, maxInstantiatesPerFrame))
                    {
                        instantiatedThisFrame = 0;
                        yield return null;
                    }
                }
            }

            chunkState.totalObjects = otherObjectsPlaced + grassPlaced;
            chunkState.grassCount = grassPlaced;

            spawnedChunks[chunkCoord] = chunkState;
            spawnRoutineRunning = false;

            PublishPropBatches();
            ApplyDetailVisibility(parent);
        }

        /// <summary>
        /// Odešle nasbírané matice pasivních propů rendereru – jedna dávka na druh a sloupec.
        ///
        /// <para>Až na konci spawn rutiny schválně: během ní se seznam ještě plní a
        /// registrovat ho po kouskách by znamenalo desítky dávek místo jednotek.</para>
        /// </summary>
        private void PublishPropBatches()
        {
            if (propMatrices.Count == 0) return;

            VegetationRenderer vr = VegetationRenderer.Instance;
            if (vr == null) { propMatrices.Clear(); return; }

            foreach (KeyValuePair<int, List<Matrix4x4>> kv in propMatrices)
            {
                if (kv.Value.Count == 0) continue;
                if (kv.Key < 0 || kv.Key >= spawnables.Count) continue;

                InstancedSource src = GetInstancedSource(spawnables[kv.Key].prefab, spawnables[kv.Key].category);
                if (!src.Valid) continue;

                // Kolo 6: pole z poolu místo ToArray (vrací se v ReleasePropBatches).
                int count = kv.Value.Count;
                Matrix4x4[] matrices = RentMatrices(count);
                kv.Value.CopyTo(matrices);
                kv.Value.Clear();

                Bounds bounds = new Bounds(matrices[0].GetColumn(3), Vector3.zero);
                for (int i = 1; i < count; i++)
                    bounds.Encapsulate(matrices[i].GetColumn(3));
                bounds.Expand(src.mesh.bounds.size.magnitude * 3f);

                // Propy vrhají stín – na rozdíl od trávy jsou dost velké na to, aby to bylo vidět.
                int handle = vr.Register(src.mesh, src.material, matrices, count,
                                         bounds, UnityEngine.Rendering.ShadowCastingMode.On,
                                         src.layer, 0f);

                if (handle != 0) { propBatches[kv.Key] = handle; propArrays[kv.Key] = matrices; }
                else ReturnMatrices(matrices);
            }

            // Seznamy se nechávají ve slovníku prázdné – spawner z poolu je použije znovu.
        }

        /// <summary>
        /// Zhasne renderery na instanci, ale nechá všechno ostatní.
        ///
        /// <para>Vypíná se i LODGroup – ta si renderery zapíná sama podle vzdálenosti,
        /// takže by je po chvíli rozsvítila zpátky a objekt by se kreslil dvakrát.
        /// Collidery, skripty ani hierarchie se nedotýká; proto se objekty MAŽOU
        /// renderery, ne odstraňují – kdyby se odstranil celý potomek, zmizel by
        /// s ním i collider, který na něm může sedět.</para>
        /// </summary>
        private static void StripRenderers(GameObject instance)
        {
            var group = instance.GetComponentInChildren<LODGroup>(true);
            if (group != null) group.enabled = false;

            var renderers = instance.GetComponentsInChildren<Renderer>(true);
            for (int i = 0; i < renderers.Length; i++)
                renderers[i].enabled = false;
        }

        /// <summary>Běžící coroutine aplikace viditelnosti detailů (null = neběží).</summary>
        private Coroutine detailApplyRoutine;

        /// <summary>
        /// Aktualizuje fyzickou přítomnost pending dekorací podle vzdálenosti chunku od hráče.
        /// Tráva používá globální EndlessTerrain.grassDistance, ostatní své maxViewDistanceChunks.
        /// Hystereze +1 chunk brání přeblikávání na hranici. Volá TerrainChunk při každém
        /// update cyklu – při nezměněné vzdálenosti se nic nedělá.
        /// </summary>
        /// <param name="chunkDistance">Chebyshev vzdálenost chunku od hráče (v chuncích).</param>
        /// <param name="parent">Parent transform pro instancované dekorace (chunk objekt).</param>
        public void UpdateDetailVisibility(int chunkDistance, Transform parent)
        {
            if (chunkDistance == lastDetailDistance) return;

            lastDetailDistance = chunkDistance;
            ApplyDetailVisibility(parent);
        }

        /// <summary>
        /// Spustí (nebo restartuje) postupnou aplikaci viditelnosti pending dekorací.
        /// Instancování i ničení je rozložené do snímků přes maxInstantiatesPerFrame.
        /// </summary>
        private void ApplyDetailVisibility(Transform parent)
        {
            if (pendingDetails.Count == 0) return;
            if (!isActiveAndEnabled) return;

            if (detailApplyRoutine != null)
                StopCoroutine(detailApplyRoutine);

            detailApplyRoutine = StartCoroutine(ApplyDetailVisibilityRoutine(parent));
        }

        /// <summary>
        /// Projde pending dekorace a uvede scénu do souladu s aktuální vzdáleností:
        /// chybějící blízké instancuje, přebývající vzdálené zničí. Restart uprostřed
        /// průchodu je bezpečný – rozhodnutí se vždy počítají znovu od začátku seznamu.
        /// </summary>
        private IEnumerator ApplyDetailVisibilityRoutine(Transform parent)
        {
            int grassLimit = (EndlessTerrain.instance != null) ? EndlessTerrain.instance.grassDistance : grassDistanceChunks;
            int budget = Mathf.Max(1, maxInstantiatesPerFrame);
            int workThisFrame = 0;

            // Tráva se vyřizuje NAJEDNOU a mimo tuhle smyčku: je to jedna dávka na druh,
            // ne 2500 rozhodnutí rozložených do snímků. Smyčka níž ji proto přeskočí.
            float columnSpan = Generation.VoxelTerrain.instance != null
                ? Generation.VoxelTerrain.instance.ChunkSpan
                : 32f;
            SyncGrassBatches(lastDetailDistance <= grassLimit, (grassLimit + 1) * columnSpan);

            while (spawnedDetails.Count < pendingDetails.Count)
                spawnedDetails.Add(null);

            for (int i = 0; i < pendingDetails.Count; i++)
            {
                PendingDetailData data = pendingDetails[i];

                if (data.spawnableIndex < 0 || data.spawnableIndex >= spawnables.Count) continue;
                SpawnableObject s = spawnables[data.spawnableIndex];
                if (s == null || s.prefab == null) continue;

                // Tráva, kterou kreslí VegetationRenderer, tady nemá co dělat – jinak by
                // vznikla podruhé jako GameObject a překrývala by se sama se sebou.
                if (s.category == SpawnCategory.Grass && GrassIsInstanced(data.spawnableIndex)) continue;

                int limit = (s.category == SpawnCategory.Grass || data.nearOnly) ? grassLimit : s.maxViewDistanceChunks;
                if (limit <= 0 || limit > 100000) limit = 100000;

                bool shouldExist;
                int distance = lastDetailDistance;
                if (distance <= limit) shouldExist = true;
                else if (distance > limit + 1) shouldExist = false;
                else shouldExist = spawnedDetails[i] != null; // hystereze – stav se nemění

                if (shouldExist && spawnedDetails[i] == null)
                {
                    if (SaveSystem.SaveSystem.IsObjectDestroyed(data.objectHash)) continue;

                    GameObject go = Instantiate(s.prefab, data.position, data.rotation, parent);
                    go.transform.localScale = data.scale;

                    ApplySmallDecorationTweaks(go, s.category);
                    ApplyTrunkLook(go, s.category);

                    DeterministicObjectId id = go.GetComponent<DeterministicObjectId>();
                    if (id == null) id = go.AddComponent<DeterministicObjectId>();
                    id.Initialize(data.objectHash, data.chunkCoord);

                    spawnedDetails[i] = go;
                    workThisFrame++;
                }
                else if (!shouldExist && spawnedDetails[i] != null)
                {
                    Destroy(spawnedDetails[i]);
                    spawnedDetails[i] = null;
                    workThisFrame++;
                }

                if (workThisFrame >= budget)
                {
                    workThisFrame = 0;
                    yield return null;
                }
            }

            detailApplyRoutine = null;
        }

        // ── tráva přes GPU instancing ──────────────────────────────────

        /// <summary>
        /// Uvede dávky trávy do souladu s tím, jestli je sloupec v dosahu.
        ///
        /// <para>Staví se jednou při vstupu do dosahu a zahazuje při odchodu – tedy přesně
        /// v momentech, kdy se dřív instancovaly a ničily tisíce GameObjectů. Rozdíl je,
        /// že tady jde o jednu alokaci pole matic místo 2500 objektů se scénickým grafem.</para>
        ///
        /// <para>Matice se drží jen pro sloupce v dosahu trávy. Kdyby se držely pro všechny
        /// načtené sloupce, bylo by to při dohledu 2 km řádově desítky megabajtů za nic –
        /// vzdálenou trávu stejně nikdo nevidí.</para>
        /// </summary>
        private void SyncGrassBatches(bool shouldDraw, float maxDistance)
        {
            if (!instanceGrass || VegetationRenderer.Instance == null)
            {
                ReleaseGrassBatches();
                return;
            }

            if (!shouldDraw) { ReleaseGrassBatches(); return; }
            if (grassBatches.Count > 0) return;   // už postavené, není co dělat

            BuildGrassBatches(maxDistance);
        }

        /// <summary>Zahodí všechny dávky tohoto sloupce. Volá se i z OnDestroy.</summary>
        private void ReleaseGrassBatches()
        {
            UnlistGrassPickups();
            if (grassBatches.Count == 0) return;

            VegetationRenderer vr = VegetationRenderer.Instance;
            if (vr != null)
            {
                foreach (KeyValuePair<int, int> kv in grassBatches)
                    vr.Unregister(kv.Value);
            }
            grassBatches.Clear();
            foreach (KeyValuePair<int, Matrix4x4[]> kv in grassArrays) ReturnMatrices(kv.Value);
            grassArrays.Clear();
        }

        /// <summary>
        /// Zruší dávky pasivních propů. Na rozdíl od trávy žijí celou dobu existence
        /// sloupce – prop je vidět na celý dohled LOD0, takže se nemá podle čeho zapínat.
        /// </summary>
        private void ReleasePropBatches()
        {
            propMatrices.Clear();
            if (propBatches.Count == 0) return;

            VegetationRenderer vr = VegetationRenderer.Instance;
            if (vr != null)
            {
                foreach (KeyValuePair<int, int> kv in propBatches)
                    vr.Unregister(kv.Value);
            }
            propBatches.Clear();
            foreach (KeyValuePair<int, Matrix4x4[]> kv in propArrays) ReturnMatrices(kv.Value);
            propArrays.Clear();
        }

        /// <summary>
        /// Poskládá matice trávy po druzích a pošle je rendereru.
        ///
        /// <para>Pořadí se bere z <c>pendingDetails</c>, které vzniklo deterministicky ve
        /// spawn rutině – žádné náhodné číslo se tu netočí, takže rozmístění zůstává
        /// bit po bitu stejné jako u GameObjectové cesty.</para>
        /// </summary>
        private void BuildGrassBatches(float maxDistance)
        {
            grassMaxDistance = maxDistance;
            if (pendingDetails.Count == 0) return;

            // Kolo 6: bez Dictionary/List/ToArray na každý sloupec. Nejdřív se spočítá, kolik
            // stébel má který druh, pak se pole matic půjčí z poolu (vrací se v
            // ReleaseGrassBatches) a plní přímo. Pořadí stébel zůstává stejné.
            if (grassCount == null || grassCount.Length < spawnables.Count)
            {
                grassCount = new int[spawnables.Count];
                grassFill = new int[spawnables.Count];
                grassArr = new Matrix4x4[spawnables.Count][];
            }
            System.Array.Clear(grassCount, 0, grassCount.Length);

            for (int pass = 0; pass < 2; pass++)
            {
                for (int i = 0; i < pendingDetails.Count; i++)
                {
                    PendingDetailData data = pendingDetails[i];
                    if (data.spawnableIndex < 0 || data.spawnableIndex >= spawnables.Count) continue;

                    SpawnableObject s = spawnables[data.spawnableIndex];
                    if (s == null || s.prefab == null || s.category != SpawnCategory.Grass) continue;

                    InstancedSource src = GetInstancedSource(s.prefab);
                    if (!src.Valid) continue;   // nedá se instancovat – zůstane na GameObjectové cestě

                    // Zničené objekty se přeskakují stejně jako při instancování.
                    if (SaveSystem.SaveSystem.IsObjectDestroyed(data.objectHash)) continue;

                    int si = data.spawnableIndex;
                    if (pass == 0) { grassCount[si]++; continue; }
                    grassArr[si][grassFill[si]++] = Matrix4x4.TRS(data.position, data.rotation, data.scale) * src.offset;
                }

                if (pass == 0)
                    for (int si = 0; si < spawnables.Count; si++)
                    {
                        grassFill[si] = 0;
                        grassArr[si] = grassCount[si] > 0 ? RentMatrices(grassCount[si]) : null;
                    }
            }

            for (int si = 0; si < spawnables.Count; si++)
            {
                int count = grassFill[si];
                Matrix4x4[] matrices = grassArr[si];
                grassArr[si] = null;
                if (matrices == null) continue;
                if (count == 0) { ReturnMatrices(matrices); continue; }

                InstancedSource src = GetInstancedSource(spawnables[si].prefab);

                // Obálka celého sloupce. Frustum se testuje proti ní, ne proti stéblům –
                // 2500 testů na sloupec a snímek by sežralo víc, než kolik by ušetřilo.
                Bounds bounds = new Bounds(matrices[0].GetColumn(3), Vector3.zero);
                for (int i = 1; i < count; i++)
                    bounds.Encapsulate(matrices[i].GetColumn(3));
                bounds.Expand(src.mesh.bounds.size.magnitude * 2f);

                var shadows = disableShadowsForSmallDecorations
                    ? UnityEngine.Rendering.ShadowCastingMode.Off
                    : UnityEngine.Rendering.ShadowCastingMode.On;

                int handle = VegetationRenderer.Instance.Register(
                    src.mesh, src.material, matrices, count,
                    bounds, shadows, src.layer, maxDistance);

                if (handle != 0) { grassBatches[si] = handle; grassArrays[si] = matrices; AddGrassPickupBounds(bounds); }
                else ReturnMatrices(matrices);
            }
            ListGrassPickups();
        }

        // ── kolo 6: pool polí matic (tráva a pasivní propy) ───────────────
        [System.NonSerialized] private readonly Dictionary<int, Matrix4x4[]> grassArrays = new Dictionary<int, Matrix4x4[]>(4);
        [System.NonSerialized] private readonly Dictionary<int, Matrix4x4[]> propArrays = new Dictionary<int, Matrix4x4[]>(8);
        private static int[] grassCount, grassFill;
        private static Matrix4x4[][] grassArr;
        private static readonly Dictionary<int, Stack<Matrix4x4[]>> matrixPool = new Dictionary<int, Stack<Matrix4x4[]>>(8);

        /// <summary>Pole matic aspoň pro <paramref name="n"/> instancí (mocnina dvou), z poolu nebo nové.</summary>
        private static Matrix4x4[] RentMatrices(int n)
        {
            int cap = Mathf.NextPowerOfTwo(Mathf.Max(n, 32));
            if (matrixPool.TryGetValue(cap, out Stack<Matrix4x4[]> st) && st.Count > 0) return st.Pop();
            return new Matrix4x4[cap];
        }

        /// <summary>Vrátí pole do poolu (VegetationRenderer už ho po Unregister nedrží).</summary>
        private static void ReturnMatrices(Matrix4x4[] a)
        {
            if (a == null) return;
            if (!matrixPool.TryGetValue(a.Length, out Stack<Matrix4x4[]> st))
            {
                st = new Stack<Matrix4x4[]>(8);
                matrixPool[a.Length] = st;
            }
            if (st.Count < 48) st.Push(a);
        }

        /// <summary>
        /// Vytáhne z prefabu mesh a materiál pro instancing, nebo vrátí neplatný zdroj.
        ///
        /// <para><b>Instancovat jde jen jednoduchý prefab</b> – právě jeden renderer s jedním
        /// materiálem. Složitější prefab (víc materiálů, víc částí) by se musel kreslit po
        /// částech a tím by se výhoda ztratila; takový prostě propadne na GameObjectovou
        /// cestu. Raději nechat pár typů po staru než tiše kreslit něco jiného.</para>
        ///
        /// <para>Materiál se KOPÍRUJE a zapíná se mu instancing. Přepsat sdílený asset
        /// v Resources by změnilo soubor na disku; a bez zapnutého instancingu by
        /// <c>DrawMeshInstanced</c> jen tiše nekreslilo nic.</para>
        /// </summary>
        private static InstancedSource GetInstancedSource(GameObject prefab, SpawnCategory category = SpawnCategory.Grass)
        {
            if (prefab == null) return default;
            if (instancedSources.TryGetValue(prefab, out InstancedSource cached)) return cached;

            InstancedSource src = default;
            MeshRenderer chosen = PickRenderer(prefab);

            // Meziúkol: prefab z více dílů v LOD0 (keř = kmen + listí) instancer nakreslí jen
            // jedním meshem – z keře zbyl holý kmen. Takový zůstává na GameObjectech.
            var lodGroup = prefab.GetComponentInChildren<LODGroup>(true);
            if (lodGroup != null)
            {
                LOD[] lods = lodGroup.GetLODs();
                int n = 0;
                if (lods.Length > 0 && lods[0].renderers != null)
                    foreach (Renderer r in lods[0].renderers) if (r != null) n++;
                if (n > 1) chosen = null;
            }

            if (chosen != null && chosen.sharedMaterials.Length == 1)
            {
                var filter = chosen.GetComponent<MeshFilter>();
                Material source = chosen.sharedMaterial;

                if (filter != null && filter.sharedMesh != null && filter.sharedMesh.subMeshCount == 1
                    && source != null)
                {
                    var mat = new Material(source) { enableInstancing = true, name = source.name + " (instanced)" };
                    // Kolo 11: suchý strom (jeden mesh = holý kmen) dostane stejný doplněk oblohy jako kmeny.
                    if (category == SpawnCategory.Trees && IsTrunkMaterial(source)) RegisterTrunkMaterial(mat);

                    src = new InstancedSource
                    {
                        mesh = filter.sharedMesh,
                        material = mat,
                        layer = chosen.gameObject.layer,
                        offset = prefab.transform.worldToLocalMatrix * chosen.transform.localToWorldMatrix,
                        passive = prefab.GetComponentInChildren<HarvestableObject>(true) == null
                                  && prefab.GetComponentInChildren<PickupItem>(true) == null,
                        hasCollider = prefab.GetComponentInChildren<Collider>(true) != null,
                    };
                }
            }

            if (src.Valid)
                Debug.Log($"[ObjectSpawner] '{prefab.name}' se kreslí instancovaně " +
                          $"(mesh {src.mesh.name}, {src.mesh.vertexCount} vrcholů).", prefab);
            else
                Debug.LogWarning($"[ObjectSpawner] Prefab '{prefab.name}' nejde instancovat " +
                                 "(čekal se jeden mesh s jedním materiálem). Zůstává na GameObjectech.", prefab);

            instancedSources[prefab] = src;
            return src;
        }

        /// <summary>
        /// Vybere z prefabu ten renderer, který se má instancovat.
        ///
        /// <para><b>LODGroup je pravidlo, ne výjimka.</b> Travní prefaby z asset packů mají
        /// tři úrovně detailu, tedy tři MeshRenderery – kdo čeká jediný renderer, odmítne
        /// úplně všechnu trávu a ani se nedozví proč. Bere se <b>LOD0</b>, protože přesně
        /// ten kreslila i GameObjectová cesta: <see cref="RemoveLODForSmallObjects"/> u trávy
        /// LODGroup po instancování stejně zahazuje, takže se zobrazení nemění.</para>
        /// </summary>
        private static MeshRenderer PickRenderer(GameObject prefab)
        {
            var group = prefab.GetComponentInChildren<LODGroup>(true);
            if (group != null)
            {
                LOD[] lods = group.GetLODs();
                if (lods.Length > 0 && lods[0].renderers != null)
                {
                    for (int i = 0; i < lods[0].renderers.Length; i++)
                    {
                        if (lods[0].renderers[i] is MeshRenderer mr && mr != null) return mr;
                    }
                }
            }

            var renderers = prefab.GetComponentsInChildren<MeshRenderer>(true);
            return renderers.Length == 1 ? renderers[0] : null;
        }

        /// <summary>
        /// Materiály listí přebarvené do podzimu, klíčované původním materiálem.
        ///
        /// <para><b>Jedna sdílená kopie na celý svět, ne jedna na strom.</b> Na tom stojí
        /// všechno ostatní: SRP Batcher spojuje kresbu podle shaderu a materiálu, takže
        /// tisíc bříz se sdíleným materiálem je pořád jedna dávka. Kdyby se materiál klonoval
        /// per instanci (nebo se sáhlo na <c>renderer.material</c>, což klon udělá samo a
        /// potichu), rozpadlo by se dávkování na tisíc kusů – přesně ten problém, který jsme
        /// právě vyřešili u rákosí.</para>
        ///
        /// <para>Proto se taky nepřidává prefabová varianta jako asset: nový prefab by musel
        /// vzniknout v projektu i s .meta souborem a pak ho někdo musí udržovat vedle originálu.
        /// Výsledek na obrazovce je stejný, jen bez druhého assetu k zapomenutí.</para>
        /// </summary>
        private static readonly Dictionary<Material, Material> autumnLeaves =
            new Dictionary<Material, Material>(4);

        /// <summary>
        /// Barva listí ve zlatém háji: spodek koruny a špičky.
        ///
        /// <para>Jantar, ne oranžová – čistá oranžová vypadá na nízkopolygonovém listu jako
        /// plast. Dvě barvy proto, že shader vegetace míchá <c>_MainColor</c> u báze a
        /// <c>_SecondColor</c> u špiček; jedna barva by korunu zploštila do jedné plochy.</para>
        /// </summary>
        private static readonly Color AutumnLeaf = new Color(0.60f, 0.29f, 0.07f, 1f);

        /// <inheritdoc cref="AutumnLeaf"/>
        private static readonly Color AutumnLeafTip = new Color(0.90f, 0.55f, 0.12f, 1f);

        /// <summary>
        /// Vizuální varianta objektu podle mikro-biomu. Zatím jen podzimní listí v háji.
        /// </summary>
        private void ApplyMicroLook(GameObject go, SpawnCategory category, Vector3 worldPos)
        {
            if (category != SpawnCategory.Trees) return;

            VoxelTerrain vt = VoxelTerrain.instance;
            if (vt == null) return;
            if (vt.MicroAt(worldPos.x, worldPos.z, out float w) != MicroBiome.GoldenGrove || w < 0.35f) return;

            var renderers = go.GetComponentsInChildren<MeshRenderer>(true);
            for (int i = 0; i < renderers.Length; i++)
            {
                Material[] mats = renderers[i].sharedMaterials;
                bool changed = false;

                for (int m = 0; m < mats.Length; m++)
                {
                    if (!IsLeafMaterial(mats[m])) continue;
                    mats[m] = AutumnVariant(mats[m]);
                    changed = true;
                }

                // Přiřadit se smí jen když se opravdu něco změnilo: sharedMaterials vrací
                // kopii pole, ale zpětný zápis renderer označí jako změněný i bez rozdílu.
                if (changed) renderers[i].sharedMaterials = mats;
            }
        }

        /// <summary>
        /// Je to materiál listí? Poznává se podle jména – materiál sám o sobě nenese nic,
        /// z čeho by šlo odvodit, že kryje korunu a ne kmen.
        /// </summary>
        private static bool IsLeafMaterial(Material m)
            => m != null && (m.name.IndexOf("Leaves", StringComparison.OrdinalIgnoreCase) >= 0
                             || m.name.IndexOf("Leaf", StringComparison.OrdinalIgnoreCase) >= 0);

        /// <summary>Podzimní kopie materiálu listí. Vyrobí se jednou a sdílí se.</summary>
        private static Material AutumnVariant(Material src)
        {
            if (autumnLeaves.TryGetValue(src, out Material cached) && cached != null) return cached;

            var v = new Material(src) { name = src.name + " (autumn)" };

            // Barvu nese _MainColor a _SecondColor shaderu UNP_Vegetation, NE _BaseColor.
            // V materiálu sice pole _BaseColor* zůstala po dřívějším shaderu a v souboru
            // vypadají rozumně, ale tenhle shader je nečte – přebarvit je nedělá vůbec nic.
            // Právě na tom první pokus tiše selhal: materiál se vyrobil, jen byl beze změny.
            SetIfHas(v, "_MainColor", AutumnLeaf);
            SetIfHas(v, "_SecondColor", AutumnLeafTip);

            autumnLeaves[src] = v;
            return v;
        }

        private static void SetIfHas(Material m, string prop, Color c)
        {
            if (m.HasProperty(prop)) m.SetColor(prop, c);
        }

        // ── Kolo 11: kmeny ve stínu ───────────────────────────────────────────
        //
        // Kmen kreslí URP/Lit s paletovou texturou („Color_Palette“). Ve stínu koruny mu zbude
        // jen obloha (SH do boku ~0,18 proti slunci 3,0): tmavá kůra je pak černá plocha bez
        // kresby. Měřeno: SSAO na tom podíl nemá (vypnuté = stejné pixely), rozhoduje poměr
        // slunce a ambientu. Globální ambient se zvedat nesmí (terén, atmosféra), proto kmeny
        // dostanou běhovou kopii materiálu se shaderem „Everlost/Trunk“ – to je URP/Lit
        // (stíny, hloubka a SSAO přes UsePass), jen osvětlený pass vrátí do stínu část
        // světla s obalem N·L a trochu oblohy. Stejný mechanismus jako listí v kole 10.
        //
        // Sdílené kopie (jedna na zdrojový materiál) – SRP Batcher ani instancing se
        // nerozpadne. Asset Color_Palette se nemění (sdílí ho i kameny a klády).

        /// <summary>Kolo 11: světlo kmenů zapnuté (konzole <c>/kmen look on|off</c>).</summary>
        public static bool TrunkLook = true;

        /// <summary>Kolik ztraceného přímého světla se kmeni vrátí do stínu (s obalem N·L).</summary>
        public static float TrunkShadowFloor = 0.22f;

        /// <summary>Kolik oblohy (SH) se kmeni přidá navíc; 1 = obloha na kmeni ×2.</summary>
        public static float TrunkAmbient = 0.85f;

        /// <summary>Nejnižší lineární jas albeda kůry (tmavý pás břízy 0,046 se zvedne na tuto hodnotu).</summary>
        public static float TrunkAlbedoFloor = 0.11f;

        /// <summary>Diagnostika: 1 = kmeny jako maska (plná purpurová) pro měření pixelů.</summary>
        public static int TrunkDiagMask;

        /// <summary>
        /// Kolo 26: viditelnost v mlze F (0–1); mezi F a F/3 strom – kmen i listí najednou – plynule zmizí
        /// (shadery Everlost/Trunk a UNP/Vegetation, jen listí). Za koncem mlhy zůstávala z koruny bledá
        /// silueta proti obloze, kmen splynul s terénem a vypadalo to jako levitující listí. 0 = vypnuto.
        /// </summary>
        public static float TreeFogFade = 0.3f;

        private static readonly Dictionary<Material, Material> trunkVariants = new Dictionary<Material, Material>(4);
        private static readonly List<Material> trunkMaterials = new List<Material>(8);
        private static Shader trunkShader;
        private static bool trunkShaderMissing;

        /// <summary>Materiál kmene stromu: URP/Lit (nebo už kmenová kopie), který není listím.</summary>
        public static bool IsTrunkMaterial(Material m)
            => m != null && !IsLeafMaterial(m) && m.shader != null
               && (m.shader.name == "Universal Render Pipeline/Lit" || m.shader.name == "Everlost/Trunk");

        private static Shader TrunkShader
        {
            get
            {
                if (trunkShader == null && !trunkShaderMissing)
                {
                    trunkShader = Shader.Find("Everlost/Trunk");
                    if (trunkShader == null || !trunkShader.isSupported)
                    {
                        trunkShaderMissing = true;
                        trunkShader = null;
                        Debug.LogWarning("[ObjectSpawner] Shader „Everlost/Trunk“ chybí nebo není podporovaný – kmeny zůstávají na URP/Lit.");
                    }
                }
                return trunkShader;
            }
        }

        /// <summary>Sdílená kmenová kopie materiálu; pro jiný než kmenový materiál vrací tentýž.</summary>
        public static Material TrunkVariant(Material src)
        {
            if (!IsTrunkMaterial(src) || trunkMaterials.Contains(src)) return src;
            if (trunkVariants.TryGetValue(src, out Material cached) && cached != null) return cached;
            if (TrunkShader == null) return src;
            var v = new Material(src) { name = src.name + " (kmen)" };
            RegisterTrunkMaterial(v);
            trunkVariants[src] = v;
            return v;
        }

        /// <summary>
        /// Přepne materiál na shader kmene. Volá se i pro instancované a vzdálené kopie.
        /// Alpha test se vypíná: paleta je všude neprůhledná (alfa 255), takže řez nic
        /// neodřezával – jen bral kmeni early-Z.
        /// </summary>
        public static void RegisterTrunkMaterial(Material v)
        {
            if (v == null || trunkMaterials.Contains(v) || TrunkShader == null) return;
            int queue = v.renderQueue;
            v.shader = TrunkShader;
            v.DisableKeyword("_ALPHATEST_ON");
            if (v.HasProperty("_AlphaClip")) v.SetFloat("_AlphaClip", 0f);
            v.renderQueue = queue <= 2450 ? queue : 2000;
            trunkMaterials.Add(v);
        }

        /// <summary>Kmeny stromu na GameObjectu přepne na sdílenou kmenovou kopii.</summary>
        private static void ApplyTrunkLook(GameObject go, SpawnCategory category)
        {
            if (category != SpawnCategory.Trees || go == null) return;
            var renderers = go.GetComponentsInChildren<MeshRenderer>(true);
            for (int i = 0; i < renderers.Length; i++)
            {
                Material[] mats = renderers[i].sharedMaterials;
                bool changed = false;
                for (int m = 0; m < mats.Length; m++)
                {
                    Material t = TrunkVariant(mats[m]);
                    if (t != mats[m]) { mats[m] = t; changed = true; }
                }
                if (changed) renderers[i].sharedMaterials = mats;
            }
        }

        /// <summary>Globální parametry shaderu kmene (volá VoxelTerrain.LateUpdate; levné – tři SetGlobalFloat).</summary>
        public static void UpdateTrunkLook()
        {
            Shader.SetGlobalFloat("_TrunkShadowFloor", TrunkLook ? TrunkShadowFloor : 0f);
            Shader.SetGlobalFloat("_TrunkAmbient", TrunkLook ? TrunkAmbient : 0f);
            Shader.SetGlobalFloat("_TrunkDiag", TrunkDiagMask);
            Shader.SetGlobalFloat("_TrunkAlbedoFloor", TrunkLook ? TrunkAlbedoFloor : 0f);
            Shader.SetGlobalFloat("_TreeFogFade", TreeFogFade);
        }

        /// <summary>Diagnostika pro konzoli.</summary>
        public static string TrunkInfo()
            => string.Format(System.Globalization.CultureInfo.InvariantCulture,
                "kmenových materiálů {0} (shader {1}), světlo kmenů {2}: stín {3:0.00}, obloha +{4:0.00}, albedo ≥ {6:0.000}, maska {5}",
                trunkMaterials.Count, TrunkShader != null ? "Everlost/Trunk" : "CHYBÍ", TrunkLook ? "ZAP" : "VYP",
                TrunkShadowFloor, TrunkAmbient, TrunkDiagMask, TrunkAlbedoFloor);

        /// <summary>Diagnostika: všechny kmenové materiály.</summary>
        public static List<Material> TrunkMaterials => trunkMaterials;

        /// <summary>
        /// Smí tenhle druh vyrůst na tomhle místě? Rozhoduje mikro-biom.
        ///
        /// <para>Biom sloupce už je přepsaný ve <c>VoxelProps</c>, takže v háji je v nabídce
        /// sada s břízami a na čediči suť. To ale nestačí: sada s břízami nese i jedle a
        /// suť nese trávu. Tenhle filtr dělá to poslední – vyřadí, co by charakter místa
        /// rozmělnilo.</para>
        ///
        /// <para><b>Podle jména prefabu.</b> Není to hezké, ale spawnable nenese žádnou
        /// informaci o druhu – jen kategorii (strom, kámen, tráva, drobnost). Přidat do
        /// dat druh by znamenalo přerovnat sto třicet záznamů ve scéně ručně; jméno je
        /// v projektu stabilní a v kódu je aspoň vidět, co se filtruje.</para>
        /// </summary>
        private bool MicroAllows(SpawnableObject s, Vector3 worldPos)
        {
            VoxelTerrain vt = VoxelTerrain.instance;
            if (vt == null) return true;

            MicroBiome m = vt.MicroAt(worldPos.x, worldPos.z, out float w);
            if (m == MicroBiome.None || w < 0.35f) return true;

            if (m == MicroBiome.GoldenGrove)
            {
                // Háj je březový. Jehličnan uprostřed zlatého listí sráží celý dojem,
                // protože jehličí se nepodzimňuje a zůstane v obraze jako tmavá skvrna.
                if (s.category != SpawnCategory.Trees) return true;
                return s.prefab != null && s.prefab.name.IndexOf("Birch", StringComparison.OrdinalIgnoreCase) >= 0;
            }

            // Čedič je holá vychladlá láva. Tráva ani stromy tu nemají co dělat – celý
            // efekt stojí na tom, že je vidět kámen.
            if (s.category == SpawnCategory.Grass || s.category == SpawnCategory.Trees) return false;

            // A ze skal jen svislé útvary. Světlé balvany z pouštní sady na černém poli
            // nevypadaly jako čedič, ale jako pískovec někdo rozsypal po lávě – kontrast
            // je tak silný, že zabil i tvar terénu pod nimi.
            if (s.category == SpawnCategory.Stones)
                return s.prefab != null && s.prefab.name.IndexOf("Cliff", StringComparison.OrdinalIgnoreCase) >= 0;

            return true;
        }

        /// <summary>Kreslí se tráva tohoto sloupce instancovaně? Používá se k přeskočení GameObjectů.</summary>
        private bool GrassIsInstanced(int spawnableIndex) => grassBatches.ContainsKey(spawnableIndex);

        /// <summary>
        /// Okamžitě zničí všechny instancované pending dekorace (bez rozkladu do snímků).
        /// Používá se při čištění/regeneraci chunku.
        /// </summary>
        private void DespawnAllDetails()
        {
            ReleaseGrassBatches();

            // Volá se i na začátku spawn rutiny (regenerace sloupce). Bez tohohle by se
            // propy zaregistrovaly podruhé a kreslily by se přes sebe.
            ReleasePropBatches();

            for (int i = 0; i < spawnedDetails.Count; i++)
            {
                if (spawnedDetails[i] != null)
                    Destroy(spawnedDetails[i]);
            }
            spawnedDetails.Clear();
        }

        /// <summary>
        /// Úpravy malých dekorací po instancování: odstranění LODGroup (tráva/SmallObjects)
        /// a vypnutí vrhání stínů (stíny malých objektů nejsou vidět, ale stály draw cally).
        /// </summary>
        private void ApplySmallDecorationTweaks(GameObject spawnedObject, SpawnCategory category)
        {
            RemoveLODForSmallObjects(spawnedObject, category);

            if (disableShadowsForSmallDecorations &&
                (category == SpawnCategory.Grass || category == SpawnCategory.SmallObjects))
            {
                var renderers = spawnedObject.GetComponentsInChildren<Renderer>(true);
                for (int i = 0; i < renderers.Length; i++)
                    renderers[i].shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            }
        }

        /// <summary>
        /// Vypočítá normálu terénu z altitude mapy centrálními diferencemi.
        /// Odpovídá normále vizuálního meshe (drobné odchylky od dřívější raycast normály
        /// konkrétního trojúhelníku jsou možné – ovlivňují jen rotaci objektů, ne pozice).
        /// </summary>
        private static Vector3 GetTerrainNormal(float[,] altitudeMap, int x, int z, float vertexSpacing, int sizeX, int sizeZ)
        {
            int xPrev = Mathf.Max(x - 1, 0);
            int xNext = Mathf.Min(x + 1, sizeX - 1);
            int zPrev = Mathf.Max(z - 1, 0);
            int zNext = Mathf.Min(z + 1, sizeZ - 1);

            float dX = altitudeMap[xPrev, z] - altitudeMap[xNext, z];
            float dZ = altitudeMap[x, zPrev] - altitudeMap[x, zNext];

            float horizontalStep = 2f * Mathf.Max(vertexSpacing, 0.0001f);

            return new Vector3(dX, horizontalStep, dZ).normalized;
        }

        /// <summary>
        /// Starší metoda pro zpětnou kompatibilitu – zjistí zda byl daný chunk inicializován.
        /// </summary>
        public bool IsInitializedForChunk(Vector2Int checkCoord)
        {
            return objectsSpawned && this.chunkCoord == checkCoord;
        }

        /// <summary>
        /// Zničí všechny child objekty tohoto spawneru a resetuje počítadla.
        /// Starší metoda pro zpětnou kompatibilitu s TerrainChunk.
        /// </summary>
        public void ClearSpawnedObjects()
        {
            StopAllCoroutines();
            spawnRoutineRunning = false;
            detailApplyRoutine = null;

            for (int i = transform.childCount - 1; i >= 0; i--)
            {
                Transform child = transform.GetChild(i);
                if (child != transform) Destroy(child.gameObject);
            }

            DespawnAllDetails();
            pendingDetails.Clear();
            lastDetailDistance = int.MaxValue;

            objectsSpawned = false;
            grassPlaced = 0;
            otherObjectsPlaced = 0;
        }

        /// <summary>
        /// Odstraní LODGroup komponentu z malých objektů (tráva, SmallObjects).
        /// Malé objekty nepotřebují LOD – jejich LOD způsoboval problémy při recyklaci chunků.
        /// </summary>
        private void RemoveLODForSmallObjects(GameObject spawnedObject, SpawnCategory category)
        {
            if (category != SpawnCategory.Grass && category != SpawnCategory.SmallObjects) return;

            var lodGroup = spawnedObject.GetComponent<LODGroup>();
            if (lodGroup == null) return;

            // NEJDŘÍV zhasnout renderery ostatních úrovní, teprve pak zahodit LODGroup.
            //
            // Tohle byla tichá díra ve výkonu: LODGroup je jediné, co vyšší úrovně detailu
            // vypíná. Když se odstranil sám, zůstalo v objektu VŠECH pět mesh rendererů
            // zapnutých a kreslily se najednou, ve všech vzdálenostech. U rákosí z asset
            // packu to znamenalo pět rendererů po dvou materiálech, tedy deset draw callů
            // na jedno stéblo místo jednoho. V mokřadu to samo dělalo přes 6 000 rendererů.
            //
            // Z obrazu se to nepozná – vyšší úrovně detailu leží v témž místě jako LOD0 a
            // jen se překrývají. Pozná se to jen na počtu batchů.
            LOD[] lods = lodGroup.GetLODs();
            if (lods.Length > 1)
            {
                var keep = new HashSet<Renderer>();
                if (lods[0].renderers != null)
                    for (int i = 0; i < lods[0].renderers.Length; i++)
                        if (lods[0].renderers[i] != null) keep.Add(lods[0].renderers[i]);

                Renderer[] all = spawnedObject.GetComponentsInChildren<Renderer>(true);
                for (int i = 0; i < all.Length; i++)
                    if (!keep.Contains(all[i])) all[i].enabled = false;
            }

            DestroyImmediate(lodGroup, true);
        }

        /// <summary>
        /// Odstraní záznam o spawnu daného chunku ze slovníku.
        /// Chunk bude při příštím průchodu znovu vygenerován.
        /// </summary>
        public void ClearChunk(Vector2Int chunkCoord)
        {
            spawnedChunks.Remove(chunkCoord);
        }

        /// <summary>
        /// Vrátí metadata spawnutých chunků pro serializaci do save souboru.
        /// </summary>
        public List<ChunkSpawnData> GetSpawnedChunksData()
        {
            return spawnedChunks.Values.Select(state => new ChunkSpawnData
            {
                chunkX = state.chunkCoord.x,
                chunkY = state.chunkCoord.y,
                objectCount = state.totalObjects,
                grassCount = state.grassCount
            }).ToList();
        }

        /// <summary>
        /// Načte metadata spawnutých chunků ze save souboru do interního slovníku.
        /// Chunky jsou označeny jako isLoadedFromSave = true – budou fyzicky regenerovány
        /// při prvním SpawnObjects volání.
        /// </summary>
        public void LoadSpawnedChunksData(List<ChunkSpawnData> chunksData)
        {
            spawnedChunks.Clear();
            foreach (var data in chunksData)
            {
                spawnedChunks[new Vector2Int(data.chunkX, data.chunkY)] = new ChunkSpawnState
                {
                    chunkCoord = new Vector2Int(data.chunkX, data.chunkY),
                    totalObjects = data.objectCount,
                    grassCount = data.grassCount,
                    isLoadedFromSave = true
                };
            }
        }

        /// <summary>
        /// Vyčistí slovník spawnutých chunků a zničí všechny child objekty.
        /// Volá se při návratu do hlavního menu pro čistý reset stavu spawnu.
        /// </summary>
        public void ResetSpawner()
        {
            spawnedChunks.Clear();
            ClearSpawnedObjects();
        }

        /// <summary>
        /// Vrátí deterministický Perlin noise offset pro daný chunk.
        /// Používá se pro variaci hustoty spawnu bez viditelného opakování vzorů.
        /// </summary>
        private Vector2 GetPerlinOffsetForChunk(Vector2Int chunkCoord)
        {
            int offsetSeed = worldSeed + chunkCoord.x * 1619 + chunkCoord.y * 31337;
            System.Random rand = new System.Random(offsetSeed);
            float offsetX = (float)rand.NextDouble() * 10000f;
            float offsetY = (float)rand.NextDouble() * 10000f;
            return new Vector2(offsetX, offsetY);
        }

        /// <summary>
        /// Deterministicky rozhodne, zda se objekt má spawnout na dané pozici.
        /// Každá kombinace (chunk, x, z, spawnable) dá vždy stejný výsledek ze seedu.
        /// </summary>
        private bool ShouldSpawnAtPosition(Vector2Int chunkCoord, int x, int z, SpawnableObject spawnable, float chanceMultiplier = 1f)
        {
            int posSeed = worldSeed + chunkCoord.x * 73856093 + chunkCoord.y * 19349663 + x * 83492791 + z * 116129781 + spawnable.GetHashCode();
            System.Random posRand = new System.Random(posSeed);

            float randomValue = (float)posRand.NextDouble();
            float adjustedChance = GetSpawnChance(spawnable) * chanceMultiplier;

            return randomValue < adjustedChance;
        }

        private float GetSpawnChance(SpawnableObject spawnable)
        {
            if (spawnable == null) return 0f;

            return spawnable.spawnChance > 1f
                ? Mathf.Clamp01(spawnable.spawnChance / 100f)
                : Mathf.Clamp01(spawnable.spawnChance);
        }

        private float GetOverlapRadius(SpawnCategory category)
        {
            switch (category)
            {
                case SpawnCategory.Trees:
                    return 7f;
                case SpawnCategory.Stones:
                    return 5f;
                case SpawnCategory.SmallObjects:
                    return 2f;
                case SpawnCategory.Grass:
                    return 0.75f;
                default:
                    return overlapRadius;
            }
        }

        private bool HasReservedSpawnPosition(Vector3 spawnPos, List<Vector3> reservedSpawnPositions, float radius)
        {
            float sqrRadius = radius * radius;
            for (int i = 0; i < reservedSpawnPositions.Count; i++)
            {
                if ((reservedSpawnPositions[i] - spawnPos).sqrMagnitude <= sqrRadius)
                    return true;
            }

            return false;
        }

        private long GetDeterministicObjectHash(Vector2Int chunkCoord, int x, int z, int spawnableIndex)
        {
            unchecked
            {
                long hash = 1469598103934665603L;
                hash = (hash ^ worldSeed) * 1099511628211L;
                hash = (hash ^ chunkCoord.x) * 1099511628211L;
                hash = (hash ^ chunkCoord.y) * 1099511628211L;
                hash = (hash ^ x) * 1099511628211L;
                hash = (hash ^ z) * 1099511628211L;
                hash = (hash ^ spawnableIndex) * 1099511628211L;
                return hash;
            }
        }
    }
}
