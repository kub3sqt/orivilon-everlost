using System.Collections.Generic;
using UnityEngine;
using System.Linq;
using UnityEngine.SceneManagement;
using Orivilon.SaveSystem;
using Orivilon.Core;

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
        public int maxGrassPerChunk = 500;

        /// <summary>Minimální výška noise mapy pro jakýkoliv spawn (simuluje hladinu moře).</summary>
        public float seaLevel = 0f;

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
                DontDestroyOnLoad(gameObject);
            }
            else
            {
                Destroy(gameObject);
            }
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
                int hash = 23;
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
            float vertexSpacing,
            int chunkSize)
        {
            int chunkX = Mathf.FloorToInt(chunkOrigin.x / (chunkSize * vertexSpacing));
            int chunkZ = Mathf.FloorToInt(chunkOrigin.z / (chunkSize * vertexSpacing));
            Vector2Int coord = new Vector2Int(chunkX, chunkZ);

            SpawnObjects(parent, chunkOrigin, heightMap, biomeMap, vertexSpacing, chunkSize, coord);
        }

        /// <summary>
        /// Hlavní metoda spawnu objektů pro jeden chunk.
        /// Pro každý bod mřížky chunku (krok 2) vyhodnotí všechny spawnovatelné objekty:
        /// výšku, biom, Perlin hustotu, pravděpodobnost, raycast na povrch, sklon,
        /// jitter pozice, overlap check a nakonec instantiuje objekt.
        /// Stav spawnu se uloží do spawnedChunks slovníku.
        /// Pokud chunk byl jen "načten ze save" (isLoadedFromSave), přegeneruje se fyzicky.
        /// </summary>
        public void SpawnObjects(
            Transform parent,
            Vector3 chunkOrigin,
            float[,] heightMap,
            BiomeType[,] biomeMap,
            float vertexSpacing,
            int chunkSize,
            Vector2Int chunkCoord)
        {
            if (spawnedChunks.TryGetValue(chunkCoord, out ChunkSpawnState existingState))
            {
                if (!existingState.isLoadedFromSave)
                {
                    return;
                }
            }

            if (spawnables == null || spawnables.Count == 0) return;
            if (biomeMap == null) return;

            System.Random chunkRandom = GetChunkRandom(chunkCoord);

            float noiseOffsetX = (float)chunkRandom.NextDouble() * 10000f;
            float noiseOffsetY = (float)chunkRandom.NextDouble() * 10000f;

            int grassPlaced = 0;
            int otherObjectsPlaced = 0;
            int spawnablesCount = spawnables.Count;

            RaycastHit hit;
            Vector3 rayStart = Vector3.zero;

            ChunkSpawnState chunkState = new ChunkSpawnState
            {
                chunkCoord = chunkCoord,
                spawnedObjects = new List<SpawnedObjectData>(),
                isLoadedFromSave = false
            };

            for (int x = 0; x < chunkSize && otherObjectsPlaced < maxObjectsPerChunk; x += 2)
            {
                for (int z = 0; z < chunkSize && otherObjectsPlaced < maxObjectsPerChunk; z += 2)
                {
                    if (otherObjectsPlaced >= maxObjectsPerChunk) break;

                    float height = heightMap[x, z];
                    if (height < seaLevel) continue;

                    BiomeType biome = biomeMap[x, z];
                    System.Random cellRandom = GetCellRandom(chunkCoord, x, z);

                    for (int si = 0; si < spawnablesCount; si++)
                    {
                        var s = spawnables[si];
                        if (s == null || s.prefab == null) continue;

                        if (s.category == SpawnCategory.Grass && grassPlaced >= maxGrassPerChunk) continue;
                        else if (s.category != SpawnCategory.Grass && otherObjectsPlaced >= maxObjectsPerChunk) continue;

                        if (height < s.minHeight || height > s.maxHeight) continue;
                        if (!s.allowedBiomes.Contains(biome)) continue;

                        if (s.usePerlinDensity)
                        {
                            float nx = (chunkOrigin.x + x + noiseOffsetX) * s.densityScale;
                            float nz = (chunkOrigin.z + z + noiseOffsetY) * s.densityScale;
                            if (Mathf.PerlinNoise(nx, nz) > s.densityThreshold) continue;
                        }

                        if (s.spawnChance < 0.999f)
                        {
                            if (cellRandom.NextDouble() > s.spawnChance) continue;
                        }

                        rayStart.x = chunkOrigin.x + x * vertexSpacing;
                        rayStart.y = raycastHeight;
                        rayStart.z = chunkOrigin.z + z * vertexSpacing;

                        if (!Physics.Raycast(rayStart, Vector3.down, out hit, raycastMax)) continue;

                        float slope = Vector3.Angle(hit.normal, Vector3.up);
                        if (slope < s.minSlope || slope > s.maxSlope) continue;

                        float jitterPower = vertexSpacing * 0.45f;
                        float jitterX = ((float)cellRandom.NextDouble() - 0.5f) * jitterPower;
                        float jitterZ = ((float)cellRandom.NextDouble() - 0.5f) * jitterPower;

                        Vector3 spawnPos = hit.point;
                        spawnPos.x += jitterX;
                        spawnPos.z += jitterZ;

                        int found = Physics.OverlapSphereNonAlloc(spawnPos, overlapRadius, overlapBuffer, spawnCollisionMask, QueryTriggerInteraction.Ignore);
                        if (found > 0) continue;

                        float scaleVal = Mathf.Lerp(s.scaleRange.x, s.scaleRange.y, (float)cellRandom.NextDouble()) * globalScaleMultiplier;
                        float rotationY = (float)cellRandom.NextDouble() * 360f;

                        GameObject go = Instantiate(s.prefab, spawnPos, Quaternion.identity, parent);
                        Transform t = go.transform;
                        t.localScale = new Vector3(scaleVal, scaleVal * 1.3f, scaleVal);

                        if (s.rotateToTerrain) t.rotation = Quaternion.FromToRotation(Vector3.up, hit.normal);
                        else t.rotation = Quaternion.identity;

                        t.Rotate(Vector3.up, rotationY, Space.Self);

                        RemoveLODForSmallObjects(go, s.category);

                        chunkState.spawnedObjects.Add(new SpawnedObjectData
                        {
                            prefabName = s.prefab.name,
                            position = spawnPos,
                            scale = t.localScale,
                            rotation = t.rotation,
                            category = s.category,
                            spawnableIndex = si
                        });

                        if (s.category == SpawnCategory.Grass) grassPlaced++;
                        else otherObjectsPlaced++;

                        break;
                    }
                }
            }

            chunkState.totalObjects = otherObjectsPlaced + grassPlaced;
            chunkState.grassCount = grassPlaced;

            spawnedChunks[chunkCoord] = chunkState;
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
            for (int i = transform.childCount - 1; i >= 0; i--)
            {
                Transform child = transform.GetChild(i);
                if (child != transform) Destroy(child.gameObject);
            }
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
            float adjustedChance = spawnable.spawnChance * chanceMultiplier;

            return randomValue < adjustedChance;
        }
    }
}