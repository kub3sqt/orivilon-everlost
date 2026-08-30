using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System.Linq;
using UnityEngine.SceneManagement;
using Orivilon.SaveSystem;
using Orivilon.Core;
using Orivilon.World.Objects;
using Orivilon.World.Terrain;

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
    public class ObjectSpawner : MonoBehaviour
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
        }

        /// <summary>Numerický seed světa pro deterministickou generaci objektů.</summary>
        private int worldSeed = 0;

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
            if (Instance == this)
                Instance = null;
        }

        /// <summary>
        /// Inicializuje seed světa při startu.
        /// </summary>
        private void Start()
        {
            InitializeWorldSeed();
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
                        if (surfaceY > raycastHeight || surfaceY < raycastHeight - raycastMax) continue;

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
                            GameObject go = Instantiate(s.prefab, spawnPos, Quaternion.identity, parent);
                            Transform t = go.transform;
                            t.localScale = finalScale;
                            t.rotation = finalRotation;

                            ApplySmallDecorationTweaks(go, s.category);

                            DeterministicObjectId id = go.GetComponent<DeterministicObjectId>();
                            if (id == null) id = go.AddComponent<DeterministicObjectId>();
                            id.Initialize(objectHash, chunkCoord);

                            instantiatedThisFrame++;
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

            ApplyDetailVisibility(parent);
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
            int grassLimit = (EndlessTerrain.instance != null) ? EndlessTerrain.instance.grassDistance : 2;
            int budget = Mathf.Max(1, maxInstantiatesPerFrame);
            int workThisFrame = 0;

            while (spawnedDetails.Count < pendingDetails.Count)
                spawnedDetails.Add(null);

            for (int i = 0; i < pendingDetails.Count; i++)
            {
                PendingDetailData data = pendingDetails[i];

                if (data.spawnableIndex < 0 || data.spawnableIndex >= spawnables.Count) continue;
                SpawnableObject s = spawnables[data.spawnableIndex];
                if (s == null || s.prefab == null) continue;

                int limit = (s.category == SpawnCategory.Grass) ? grassLimit : s.maxViewDistanceChunks;
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

        /// <summary>
        /// Okamžitě zničí všechny instancované pending dekorace (bez rozkladu do snímků).
        /// Používá se při čištění/regeneraci chunku.
        /// </summary>
        private void DespawnAllDetails()
        {
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
            if (category == SpawnCategory.Grass || category == SpawnCategory.SmallObjects)
            {
                var lodGroup = spawnedObject.GetComponent<LODGroup>();
                if (lodGroup != null) DestroyImmediate(lodGroup, true);
            }
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
