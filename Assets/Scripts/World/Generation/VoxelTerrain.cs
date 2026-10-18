using System;
using System.Collections;
using System.Collections.Generic;
using Orivilon.World.Biomes;
using Orivilon.World.Generation.Hydro;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;

namespace Orivilon.World.Generation
{
    /// <summary>
    /// Streamer voxelového terénu – nástupce EndlessTerrain.
    ///
    /// Načítá a uvolňuje chunky kolem vieweru. Generování běží v jobech napříč snímky:
    /// nic se nikdy nedokončuje ve stejném snímku, ve kterém se to naplánovalo, a počet
    /// rozpracovaných chunků i počet aplikovaných meshů za snímek je omezený.
    ///
    /// Fáze 3a: jen LOD0. LOD prstence a šití švů přijdou ve fázi 3b, takže dohled je
    /// zatím omezený na to, co se dá uživit v plném rozlišení.
    /// </summary>
    [AddComponentMenu("Orivilon/Voxel Terrain")]
    public partial class VoxelTerrain : MonoBehaviour
    {
        public static VoxelTerrain instance;

        [Header("Generátor")]
        public WorldGenSettings settings;

        [Tooltip("0 = převzít ze světa v GameManageru, jinak náhodně.")]
        public int seed;

        [Range(0.5f, 4f)] public float voxelSize = 1f;

        [Tooltip("Hloubka sukně v buňkách chunku. Sukně zakrývá škvíry na hranici dvou LOD. 0 = vypnuto.")]
        [Range(0f, 6f)] public float skirtCells = 3f;
        [Range(0f, 0.24f)] public float edgeSnap = 0.06f;
        public TerrainPalette palette = TerrainPalette.Default;

        [Header("Dohled")]
        [Tooltip("Transform, kolem kterého se streamuje. Prázdné = hlavní kamera.")]
        public Transform viewer;

        [Tooltip("Poloměr nejjemnějšího prstence v chuncích. 5 je 160 m plného rozlišení.")]
        [Range(1, 128)] public int renderDistance = 5;

        [Tooltip("Kolik úrovní detailu. 1 = jen plné rozlišení. Každá další má dvojnásobný voxel a dvojnásobný chunk.")]
        [Range(1, 5)] public int lodCount = 3;

        [Tooltip("Poloměr hrubších prstenců ve VLASTNÍCH chuncích. Při 4 a třech úrovních vyjde dohled kolem 930 m.")]
        [Range(2, 10)] public int lodRing = 4;

        [Tooltip("Do jaké vzdálenosti mají chunky collider.")]
        [Range(0, 8)] public int colliderDistance = 2;

        [Tooltip("Jak dlouho smí sloupec mimo dohled zůstat viditelný, než se uvolní i bez " +
                 "hotové náhrady. Jen pojistka – běžně se prohodí dřív, jakmile je náhrada hotová.")]
        [Range(0.5f, 10f)] public float unloadGrace = 4f;

        [Header("Rozpočet")]
        [Tooltip("Kolik chunků smí být rozpracovaných naráz. Zároveň počet pracovních slotů.")]
        [Range(1, 8)] public int maxChunksInFlight = 4;

        [Tooltip("Kolik hotových meshů se smí předat Unity za snímek. Tohle je hlavní páka proti záseku.")]
        [Range(1, 8)] public int maxAppliesPerFrame = 2;

        [Tooltip("Kolik sloupců se smí naplánovat za snímek.")]
        [Range(1, 8)] public int maxColumnsPerFrame = 2;

        [Tooltip("Kolik sloupců smí mít naráz alokované pole. Strop paměti; hotové sloupce se samy uvolňují.")]
        [Range(8, 192)] public int maxLiveColumns = 48;

        [Header("Zobrazení")]
        public Material terrainMaterial;

        [Tooltip("Materiál hladiny. Prázdné = voda se nevykreslí. V projektu je Assets/Models/Shaders/LowPolyWater.mat.")]
        public Material waterMaterial;

        [Tooltip("Od jaké síly koryta se kreslí říční hladina. Smí být nízké: břehovou čáru řeže izolinie hladina–terén, takže co by vyšlo nad terén, se stejně ořízne.")]
        [Range(0.05f, 0.95f)] public float waterRiverMin = 0.2f;

        [Tooltip("O kolik metrů se hladina zanoří pod břeh, než se uřízne. Drží okraj vody schovaný pod terénem – proti blikání o hloubkový buffer. 0 = řez přesně na břehové čáře.")]
        [Range(0f, 2f)] public float waterMinDepth = 0.45f;
        public bool castShadows = true;

        [Header("Hydrologie")]
        [Tooltip("Jak daleko před hranou regionu se začne stavět soused, v metrech. " +
                 "Vlastnosti řek se ladí ve World Gen Settings, tohle je jen streaming.")]
        public float hydroPrefetch = 1500f;

        [Tooltip("Kolik hotových hydrologických regionů se drží. Devět = hráč uprostřed " +
                 "a všech osm sousedů, zhruba 10 MB.")]
        [Range(4, 25)] public int hydroMaxRegions = 9;

        [Header("Osazení")]
        [Tooltip("Sázet stromy, trávu a kamení. Bere se prefab Resources/Prefabs/SpawnableObjects, stejný jako dřív.")]
        public bool spawnProps = true;

        /// <summary>
        /// DOČASNÉ – kolo kvality vody a terénu (26.9.2026). Stromy, kameny a tráva jsou
        /// vypnuté v kódu, aby šla geometrie, voda a pobřeží posuzovat bez zakrytí.
        /// Schválně to není změna <see cref="spawnProps"/> ve scéně: vrátí se to jedním
        /// řádkem a scéna ani prefab se kvůli tomu nemění. Až bude kolo uzavřené, dát false.
        /// </summary>
        public static bool PropsSuspended = false;

        /// <summary>
        /// Kolo 5: osazovat ekologickým osazovačem (<see cref="EcologyPlacer"/>). False vrátí
        /// původní mřížkový spawner (jen pro srovnání; ten neumí vodu ani svahy hlídat).
        /// </summary>
        public static bool EcoProps = true;

        /// <summary>Počty osazených sloupců podle vzdálenosti od hráče v okamžiku osazení (měření pop-inu).</summary>
        public static int PropSpawnCount, PropSpawnNear100, PropSpawnNear150;

        /// <summary>Sázet se opravdu bude: Inspector to chce a nic to dočasně nepozastavilo.</summary>
        private bool PropsActive => spawnProps && !PropsSuspended;

        [Tooltip("Kolik sloupců se smí začít osazovat za snímek. Každý si pak instancuje po svém, rozložené do snímků.")]
        [Range(1, 4)] public int maxPropColumnsPerFrame = 1;

        // ── vnitřní stav ───────────────────────────────────────────────

        private sealed class ColumnEntry
        {
            public int2 coord;
            public int lod;
            public ColumnField field;
            public NativeArray<float> probe;
            public JobHandle handle;
            public bool ready;
            public int pending;                   // kolik chunků z něj právě staví
            public List<int3> surfaceChunks;      // platné až po ready

            /// <summary>
            /// Pole už je uvolněné, protože všechny jeho chunky stojí. Sloupec zůstává jen
            /// jako značka, že se nemá plánovat znovu. Bez tohohle by se s LOD prstenci
            /// držely stovky sloupcových polí naráz a paměť by šla do desítek megabajtů.
            /// </summary>
            public bool retired;

            /// <summary>
            /// Chunky tohohle sloupce jsou ve scéně a vidět. Do té doby leží ve
            /// <see cref="staged"/> neaktivní.
            /// </summary>
            public bool published;

            /// <summary>
            /// Probíhá přestavba po úpravě terénu. Staré chunky zůstávají viditelné,
            /// nové se hromadí ve <see cref="staged"/> a prohodí se naráz.
            /// </summary>
            public bool rebuilding;

            /// <summary>
            /// Hotové, ale ještě nezveřejněné chunky. Existence tohohle mezikroku je
            /// důvod, proč po kopnutí ani při změně LOD nevznikne díra: nic se neschová
            /// dřív, než je náhrada kompletní a nahraná na GPU.
            /// </summary>
            public readonly Dictionary<int4, ChunkEntry> staged = new Dictionary<int4, ChunkEntry>();

            /// <summary>Klíče, které v téhle přestavbě vyšly prázdné. Patří ke <see cref="staged"/>.</summary>
            public readonly HashSet<int4> stagedEmpty = new HashSet<int4>();

            /// <summary>
            /// Kdy nejpozději se sloupec uvolní, i kdyby náhrada nikdy nedorazila.
            /// Nula = neběží. Pojistka proti tomu, aby něco viselo ve scéně navždy.
            /// </summary>
            public float unloadAt;

            // Voda je 2D plocha, takže patří sloupci, ne chunku – jeden mesh na 32×32 m.
            public GameObject waterGo;
            public MeshFilter waterFilter;
            public MeshRenderer waterRenderer;
            public Mesh waterMesh;

            /// <summary>
            /// Hladina v každém mřížkovém bodě sloupce, nebo <c>WaterSurface.NoWater</c>.
            ///
            /// <para>Drží se jen u sloupců, kde voda opravdu je, a přežije uvolnění
            /// sloupcového pole – právě proto existuje. Bez něj se po dostavění chunků
            /// nedá zjistit, kde je hladina, protože data, ze kterých vznikla, jsou pryč.</para>
            /// </summary>
            public float[] waterLevels;

            /// <summary>Kolo 22: vstup hladiny LOD0 před šitím švu – přestavba při posunu prstence.</summary>
            public WaterSurface.SeamSnapshot waterSeam;

            /// <summary>Kolo 22: maska hran s LOD0 sousedem, se kterou se hladina naposledy postavila.</summary>
            public int waterFine;

            /// <summary>Kolo 24: hrany s LOD0 sousedem, se kterými je postavené aktuální pole / rozestavěná
            /// verze terénu (na nich se nešije), a maska právě zveřejněné verze.</summary>
            public int seamFine, seamFinePub;

            /// <summary>Kolo 24: běží přestavba terénu kvůli změně souseda (posun prstence).</summary>
            public bool seamRebuild;

            /// <summary>Kolo 24: od kdy (realtime) čeká hotová verze na souseda; −1 = nečeká.</summary>
            public float seamBlockedSince = -1f;
            public bool seamForced;
            public bool seamWaterDeferred;

            /// <summary>Kolo 22 diagnostika pop-inu: kdy vznikl, kdy (a jak daleko od hráče) se zveřejnil.</summary>
            public float createdAt, publishedAt, publishDist = -1f;

            /// <summary>Mesh hladiny něco obsahuje. Zapne se až se zveřejněním sloupce.</summary>
            public bool hasWater;

            /// <summary>Kolo 15 diagnostika: kdy (Time.time) se naposledy postavila hladina.</summary>
            public float waterBuiltAt;

            // Osazení (stromy, tráva, kameny). Taky patří sloupci, ne 3D chunku.
            public GameObject propsGo;
            public Orivilon.World.Spawning.ObjectSpawner spawner;
            public bool propsRequested;
            public EcologyPlacer.BuildSession propsBuild;

            /// <summary>Kolo 6: vzdálené stromy tohoto (LOD1/LOD2) sloupce už jsou v TreeProxies.</summary>
            public bool proxyRequested;

            /// <summary>Výkonové kolo 14.10.2026: LOD0 sloupec už má skutečné stromy, zástupci zahozeni.</summary>
            public bool proxyDropped;

            /// <summary>Kolo 3: zástupci postaveni z hrubého pole (přesná data ještě nebyla) – přepíšou se.</summary>
            public bool proxyApprox;

            /// <summary>Kolo 3: pole obnovené po retire (RehydrateField) se ještě počítá.</summary>
            public bool fieldPending;

            /// <summary>Do kdy (unscaledTime) se chunky sloupce plynule vyměňují za předchozí LOD.</summary>
            public float morphUntil;

            /// <summary>Rozpracovaná asynchronní příprava morphu (VoxelTerrain.Geomorph.cs).</summary>
            public MorphPrep morphPrep;

            /// <summary>Morph se pro tuto verzi sloupce nedělá (bez zdrojů / nad stropem).</summary>
            public bool morphSkip;

            /// <summary>
            /// Odcházející sloupec, přes který už je zčásti zveřejněná jiná úroveň: jeho chunky
            /// jsou o kus zapuštěné, aby nový terén vyhrál hloubkový test (bez z-fightingu),
            /// a přitom tam, kde náhrada ještě chybí, nezůstala díra.
            /// </summary>
            public bool sunk;

            /// <summary>Kolo 5: od kdy sloupec není žádaný (unscaledTime), −1 = žádaný. Pojistka atomické výměny.</summary>
            public float leavingSince = -1f;

            /// <summary>Kolo 5: atomickou výměnu tohoto odcházejícího sloupce už pustila pojistka (počítá se jednou).</summary>
            public bool atomicForced;

            /// <summary>Kolo 5: plochu celou kryje zveřejněná náhrada, ale běží mu job – vzhled je schovaný.</summary>
            public bool replacedHidden;

            /// <summary>Kolo 5: odcházejícímu sloupci jsou z vykreslení vyřazené trojúhelníky nad plochou, kterou už kreslí náhrada.</summary>
            public bool masked;
            public int maskVersion = -1;
            public int[] waterMaskOrig;
        }

        private sealed class ChunkEntry
        {
            public int3 coord;
            public int lod;
            public GameObject go;
            public MeshFilter filter;
            public MeshRenderer meshRenderer;
            public MeshCollider meshCollider;
            public Mesh renderMesh;
            public Mesh colliderMesh;
            public bool colliderAssigned;

            /// <summary>Kolo 3: zvýší se při každém vrácení do poolu – identita pro nouzový collider.</summary>
            public int generation;

            /// <summary>Potomek s MeshFilter/MeshRenderer (posouvá ho jen SinkColumn).</summary>
            public Transform renderTf;

            /// <summary>
            /// Výkonové kolo 14.10.2026: PhysX data collideru se pečou v jobu hned po stavbě
            /// (<see cref="Physics.BakeMesh(int, bool, MeshColliderCookingOptions)"/>), ne až při
            /// přiřazení na hlavním vlákně. Mesh se do dokončení nesmí měnit.
            /// </summary>
            public JobHandle bake;
            public bool baking;

            /// <summary>Probíhající plynulá výměna LOD (VoxelTerrain.Geomorph.cs), jinak null.</summary>
            public Morph morph;

            /// <summary>Kolo 5: původní indexy render meshe, dokud je zamaskovaný (OverlapMask), jinak null.</summary>
            public int[] maskOrig;

            /// <summary>Čeká ve frontě na pečení (ScheduleBakes, v rámci rozpočtu jobů).</summary>
            public bool bakeWanted;

            public void CompleteBake()
            {
                bakeWanted = false;   // zrušení z fronty (recyklace, přímé přiřazení)
                if (!baking) return;
                bake.Complete();
                baking = false;
            }
        }

        /// <summary>Upeče PhysX data meshe mimo hlavní vlákno (Unity to v jobu výslovně dovoluje).</summary>
        private struct ColliderBakeJob : IJob
        {
            public int meshId;
            public MeshColliderCookingOptions options;
            public void Execute() => Physics.BakeMesh(meshId, false, options);
        }

        private struct Pending
        {
            public int3 coord;
            public int lod;
            public int slot;
            public ChunkMeshData data;
            public JobHandle handle;
            public ColumnEntry column;
            public int par;   // kolik vláken smí chunk zabrat (rozpočet jobů)
        }

        /// <summary>
        /// Jeden builder na úroveň detailu. Builder si drží velikost voxelu, okraj sloupce
        /// i pool pracovních polí, takže LOD je čistě „další instance" – joby o něm nevědí.
        /// </summary>
        private VoxelChunkBuilder[] builders;

        /// <summary>Zkratka na nejjemnější úroveň. Dotazy na povrch a osazení jdou přes ni.</summary>
        private VoxelChunkBuilder builder;

        // Klíče nesou LOD: sloupec je (x, z, lod), chunk (x, y, z, lod).
        private readonly Dictionary<int3, ColumnEntry> columns = new Dictionary<int3, ColumnEntry>();
        private readonly Dictionary<int4, ChunkEntry> chunks = new Dictionary<int4, ChunkEntry>();
        private readonly HashSet<int4> building = new HashSet<int4>();
        private readonly HashSet<int4> resolvedEmpty = new HashSet<int4>();

        // Výkonové kolo 14.10.2026: index chunků (a prázdných chunků) po sloupcích. Zveřejnění
        // i uvolnění sloupce dřív procházelo VŠECHNY chunky a celý resolvedEmpty – při dávce
        // desítek sloupců v jednom snímku to dělalo špičky 20–50 ms.
        private readonly Dictionary<int3, List<int4>> colChunks = new Dictionary<int3, List<int4>>();
        private readonly Dictionary<int3, List<int4>> colEmpty = new Dictionary<int3, List<int4>>();
        private readonly Stack<List<int4>> keyListPool = new Stack<List<int4>>();

        private static int3 ColKey(int4 k) => new int3(k.x, k.z, k.w);

        private void IndexAdd(Dictionary<int3, List<int4>> index, int4 k)
        {
            int3 c = ColKey(k);
            if (!index.TryGetValue(c, out List<int4> l))
            {
                l = keyListPool.Count > 0 ? keyListPool.Pop() : new List<int4>(8);
                index[c] = l;
            }
            l.Add(k);
        }

        private void IndexRemove(Dictionary<int3, List<int4>> index, int4 k)
        {
            int3 c = ColKey(k);
            if (!index.TryGetValue(c, out List<int4> l)) return;
            l.Remove(k);
            if (l.Count == 0) { index.Remove(c); keyListPool.Push(l); }
        }

        /// <summary>Klíče zveřejněných chunků sloupce do <see cref="scratchChunkKeys"/>.</summary>
        private void CollectColumnChunks(int3 column)
        {
            scratchChunkKeys.Clear();
            if (colChunks.TryGetValue(column, out List<int4> l)) scratchChunkKeys.AddRange(l);
        }

        /// <summary>Zapomene prázdné chunky sloupce (náhrada za resolvedEmpty.RemoveWhere).</summary>
        private void ForgetColumnEmpty(int3 column)
        {
            if (!colEmpty.TryGetValue(column, out List<int4> l)) return;
            for (int i = 0; i < l.Count; i++) resolvedEmpty.Remove(l[i]);
            l.Clear();
            colEmpty.Remove(column);
            keyListPool.Push(l);
        }
        private readonly List<Pending> inFlight = new List<Pending>();
        private readonly Stack<ChunkEntry> pool = new Stack<ChunkEntry>();
        private readonly Stack<GameObject> waterPool = new Stack<GameObject>();
        private readonly HashSet<int3> desired = new HashSet<int3>();
        private readonly List<int3> desiredSorted = new List<int3>();
        private readonly List<int4> scratchChunkKeys = new List<int4>();
        private readonly List<int3> scratchColumnKeys = new List<int3>();
        private readonly List<int3> scratchPublish = new List<int3>();

        private int2 lastViewerColumn = new int2(int.MinValue, int.MinValue);
        private bool desiredDirty = true;
        private bool warnedMaterial;

        /// <summary>
        /// Kořen chunků, držený natvrdo v počátku světa.
        ///
        /// Vrcholy meshů jsou ve SVĚTOVÝCH souřadnicích, takže kdyby chunky visely rovnou
        /// pod touhle komponentou, posunul by je její transform. Vlastní kořen v počátku
        /// tuhle závislost odstraní a je jedno, kde komponenta ve scéně stojí.
        /// </summary>
        private Transform chunkRoot;

        /// <summary>Strom chunků z doby před reloadem. Drží hráče, dokud nestojí nový terén.</summary>
        private GameObject legacyRoot;

        private Action onChunkLoaded;
        private Action onInitialComplete;
        private int resolvedColumns;

        /// <summary>True, jakmile je dogenerované celé okolí vieweru.</summary>
        public bool IsInitialLoadComplete { get; private set; }

        public float ChunkSpan => VoxelWorld.ChunkDim * voxelSize;

        /// <summary>
        /// Jak daleko od hráče vůbec existuje terén, v metrech: šířka chunku nejhrubší
        /// úrovně krát poloměr prstence.
        ///
        /// <para>Je veřejná schválně. Mlha, dosah stínů i editorový příkaz na prezentaci
        /// z ní musí vycházet, jinak se rozejdou – přesně to se stalo, když se dohled
        /// zvedl na 2 km a mlha zůstala zavřená ve 450 m. Hráč pak vidí mlhu, ne svět,
        /// a z chování se to nepozná: terén se opravdu generuje, jen ho není vidět.</para>
        /// </summary>
        public float ViewDistance
            => ChunkSpan * (1 << (Mathf.Clamp(lodCount, 1, 5) - 1)) * Mathf.Max(2, lodRing);

        // ── životní cyklus ─────────────────────────────────────────────

        private void Awake()
        {
            instance = this;
            Orivilon.World.Spawning.TreeProxies.Clear();   // statický stav z minulého Play Mode

#if UNITY_EDITOR
            // Nativní kolekce reload domény nepřežijí a Unity je nahlásí jako únik.
            // Uklidí se tedy dřív, než k reloadu dojde.
            UnityEditor.AssemblyReloadEvents.beforeAssemblyReload += ReleaseNative;
#endif
        }

#if UNITY_EDITOR
        private void ReleaseNative()
        {
            UnityEditor.AssemblyReloadEvents.beforeAssemblyReload -= ReleaseNative;
            OnDestroy();
        }
#endif

        private void Start()
        {
            Debug.Log(string.Format(System.Globalization.CultureInfo.InvariantCulture,
                "[PhysicsConfig] collider streaming v2; fixed step {0:0.00000} s, max catch-up {1:0.000} s",
                Time.fixedDeltaTime, Time.maximumDeltaTime));
            EnsureInitialized();
        }

        /// <summary>
        /// Postaví buildery, kořen chunků a úložiště úprav. Volá se ze <c>Start</c> a znovu
        /// z <c>LateUpdate</c>, kdyby se stav ztratil.
        ///
        /// <para><b>Proč znovu.</b> Když Unity v Play mode překompiluje skripty, udělá reload
        /// domény: pole, která nejsou serializovaná – tedy buildery, slovníky chunků i sloupců –
        /// se vynulují, ale <c>Start</c> se už nezavolá. Streamer pak tiše zemře uprostřed hry,
        /// scéna zůstane plná osiřelých chunků a jediné, co je vidět, je zamrzlý terén.
        /// Tohle se v editoru stane při každé úpravě kódu za běhu, takže se to nedá ignorovat.</para>
        /// </summary>
        private void EnsureInitialized()
        {
            if (builders != null) return;

            // Po reloadu domény se Awake už nezavolá, takže singleton i úklidový háček
            // je potřeba obnovit tady – jinak by na streamer nikdo nedosáhl.
            instance = this;
#if UNITY_EDITOR
            UnityEditor.AssemblyReloadEvents.beforeAssemblyReload -= ReleaseNative;
            UnityEditor.AssemblyReloadEvents.beforeAssemblyReload += ReleaseNative;
#endif

            if (settings == null)
            {
                Debug.LogError("[VoxelTerrain] Chybí WorldGenSettings, streamer se vypíná.", this);
                enabled = false;
                return;
            }

            if (viewer == null && Camera.main != null) viewer = Camera.main.transform;

            // Po reloadu tu visí starý strom chunků, na který už nikdo nemá odkaz.
            //
            // NERUŠÍ se hned. Hráč na něm stojí, a než se stihne vygenerovat nový terén,
            // uběhnou vteřiny – bez podlahy by za tu dobu propadl mimo svět a už se
            // nevrátil. Zůstane tedy stát i s collidery a zahodí se, až nový terén vznikne.
            // Dvojitá geometrie po tu chvíli není vidět, protože je to tentýž tvar.
            Transform orphan = transform.Find("Chunks");
            if (orphan != null)
            {
                if (legacyRoot != null) Destroy(legacyRoot);
                orphan.name = "Chunks (staré)";
                legacyRoot = orphan.gameObject;
            }

            var rootGo = new GameObject("Chunks");
            rootGo.transform.SetParent(transform, false);
            rootGo.transform.position = Vector3.zero;
            rootGo.transform.rotation = Quaternion.identity;
            rootGo.transform.localScale = Vector3.one;
            chunkRoot = rootGo.transform;

            palette.EnsureClimate();
            palette.art = WorldGenSettings.ArtPassEnabled ? 1f : 0f;
            ApplyFoliageLook(FoliageLook);

            int seedValue = ResolveSeed();
            palette.biomes = WorldGenSettings.BiomesEnabled ? 1f : 0f;
            palette.biomeSeed = seedValue;
            int levels = Mathf.Clamp(lodCount, 1, 5);
            builders = new VoxelChunkBuilder[levels];
            // Švy mezi LOD: hladina i terén počítají uzly od hrany LOD0 sloupce.
            WaterSurface.SeamLine = VoxelWorld.ChunkDim * voxelSize;

            for (int l = 0; l < levels; l++)
            {
                // Hrubší úrovně dostanou míň pracovních slotů. Slot je 575 kB nativní paměti
                // a vzdálené prstence se stejně staví pomaleji než ten kolem hráče.
                int slots = l == 0 ? maxChunksInFlight : Mathf.Max(2, maxChunksInFlight / 2);

                builders[l] = new VoxelChunkBuilder(settings, seedValue, voxelSize * (1 << l), slots, l)
                {
                    edgeSnap = edgeSnap,
                    palette = palette,
                    skirtCells = skirtCells,
                };
            }

            builder = builders[0];

            // Hydrologie musí stát dřív, než se naplánuje první sloupec. Sloupec bez regionu
            // by se vygeneroval bez řek a musel by se pak celý přestavět – a než by se to
            // stalo, stálo by v terénu koryto bez vody.
            ReleaseSearchHydro();
            hydro?.Dispose();
            hydro = new HydroMap(builder.Params, builder.Splines)
            {
                prefetchDistance = hydroPrefetch,
                maxRegions = hydroMaxRegions,
            };

            // Odvozený stav se musí vynulovat RUČNĚ.
            //
            // Reload domény si zálohuje i privátní pole, pokud jsou serializovatelná –
            // a `int2` serializovatelný je. `lastViewerColumn` tedy reload přežije, kdežto
            // `desired` (HashSet) ne. Stráž v UpdateDesired pak porovná dnešní sloupec
            // vieweru s tím zapamatovaným, uvidí shodu a usoudí, že není co dělat.
            // Výsledkem je streamer, který běží, nic nehlásí a nikdy nic nevygeneruje.
            lastViewerColumn = new int2(int.MinValue, int.MinValue);
            desiredDirty = true;
            IsInitialLoadComplete = false;

            // Pojistka pro případ, že se hráč přesto ocitl mimo svět – třeba když se
            // reload trefil do okamžiku, kdy zrovna padal.
            if (viewer != null && viewer.position.y < VoxelWorld.WorldMinY - 50f)
            {
                // Přesouvá se KOŘEN, ne kamera. Kamera je dítě hráče a ovladač si ji každý
                // snímek srovná zpátky k hlavě, takže posunout ji samotnou nic neudělá.
                Transform body = viewer.root != null ? viewer.root : viewer;

                Vector3 p = body.position;
                p.y = 0f;

                if (TryFindLandSpawn(p, out Vector3 land)) p = land + Vector3.up * 3f;
                else p.y = SurfaceHeightFrom(builders[0], p.x, p.z) + 3f;

                body.position = p;
                Debug.Log($"[VoxelTerrain] Viewer byl pod světem, vrácen na souš {p}.", this);
            }

            // Úpravy terénu jsou vlastnost světa, ne úrovně detailu – drží je streamer
            // a dostane je jen nejjemnější builder.
            edits = new VoxelEdits(voxelSize);
            builder.edits = edits;

            // Uložené úpravy se načtou dřív, než se vygeneruje první sloupec – terén tak
            // vznikne rovnou s nimi a nemusí se nic přestavovat.
            Orivilon.SaveSystem.SaveSystem.LoadTerrainEdits();
        }

        /// <summary>
        /// Seed světa. Jednou vyřešený se pamatuje.
        ///
        /// <para>Poslední větev sahá po náhodném čísle, což je správně jen jednou za svět.
        /// <c>GameManager.selectedWorld</c> je ale statické pole a reload domény statiku
        /// vynuluje – při každé úpravě kódu za běhu by se tedy vylosoval JINÝ svět a terén
        /// by se přegeneroval někam jinam. Privátní pole reload přežije (Unity si zálohuje
        /// i neveřejná serializovatelná pole), a do scény se přitom nezapíše.</para>
        /// </summary>
        private int resolvedSeed;
        private bool seedResolved;

        /// <summary>Seed, se kterým se svět opravdu staví. Jen pro diagnostiku – běh sám
        /// si ho drží uvnitř.</summary>
        public int ResolvedSeed => ResolveSeed();

        private int ResolveSeed()
        {
            if (seedResolved) return resolvedSeed;

            resolvedSeed = ResolveSeedCore();
            seedResolved = true;
            return resolvedSeed;
        }

        private int ResolveSeedCore()
        {
            if (seed != 0) return seed;

            var world = Core.GameManager.selectedWorld;
            if (world != null)
            {
                if (world.worldSeed != 0) return world.worldSeed;
                if (!string.IsNullOrEmpty(world.seed)) return StableHash(world.seed);
            }
#if UNITY_EDITOR
            // Scéna Game spuštěná napřímo v editoru (bez světa z menu) dostává pevný seed,
            // aby šlo porovnat záběr před a po opravě na tomtéž místě. Hra z menu i build
            // jdou větví výš a tohle se jich netýká. Přepíná se z konzole: /seed set N
            // (platí od dalšího spuštění Play Mode, drží se v EditorPrefs).
            return UnityEditor.EditorPrefs.GetInt(EditorSeedPrefKey, EditorDirectStartSeed);
#else
            return UnityEngine.Random.Range(int.MinValue, int.MaxValue);
#endif
        }

        /// <summary>Seed pro přímý start scény Game v editoru. Viz <see cref="ResolveSeedCore"/>.</summary>
        public const int EditorDirectStartSeed = 1337;

        /// <summary>Klíč EditorPrefs se seedem pro přímý start v editoru.</summary>
        public const string EditorSeedPrefKey = "Everlost.DirectStartSeed";

        private static int StableHash(string s)
        {
            unchecked
            {
                int h = 0;
                foreach (char c in s) h = h * 31 + c;
                return h;
            }
        }

        private void LateUpdate()
        {
            EnsureInitialized();
            if (builder == null) return;

            // Kolo 11: doplněk oblohy na kmenech sleduje aktuální ambient (denní doba, biom).
            Orivilon.World.Spawning.ObjectSpawner.UpdateTrunkLook();

            // Viewer se hledá i tady, ne jen ve Start(). Hráč se ve scéně objevuje až
            // ze spawn rutiny, takže Camera.main při startu streameru ještě nemusí existovat –
            // a jednorázové hledání by pak nechalo streamer navždy vypnutý.
            if (viewer == null)
            {
                if (Camera.main == null) return;
                viewer = Camera.main.transform;
                desiredDirty = true;
            }

            // Hydrologie se posouvá DŘÍV, než se cokoli plánuje: sloupec se bez hotového
            // regionu nesmí naplánovat, takže by se jinak celý snímek promarnil čekáním
            // na region, o který si v tomhle snímku ještě nikdo neřekl.
            // Kolo 17: regiony dostavěné pro hledání se po 2 s bez dotazu uvolní samy.
            if (searchHydro.Count > 0 && Time.frameCount - searchHydroFrame > 120) ReleaseSearchHydro();

            if (!SpikeProbe)
            {
                // Značky profileru: bez puštěného profileru nic nestojí a v profilu rozdělí
                // čas i GC Alloc streameru po krocích (jinak je vidět jen „LateUpdate").
                BeginJobBudget();
                try
                {
                    using (MkHydro.Auto()) HydroUpdateBudgeted();
                    using (MkCollect.Auto()) CollectFinished();
                    using (MkDesired.Auto()) UpdateDesired();
                    using (MkPromote.Auto()) PromoteReadyColumns();
                    using (MkPublish.Auto()) PublishColumns();
                    // Kopání a přestavby švu PŘED novou prací: berou ze stejného rozpočtu
                    // a mají přednost (kopání hráč vidí hned, šev blokuje zveřejnění sousedů).
                    RebuildEdited();
                    UpdateTerrainSeams();
                    using (MkSchedule.Auto()) ScheduleWork();
                }
                finally { EndJobBudget(); }
                using (MkUnload.Auto()) UnloadFar();
                UpdateMorphs();
                UpdateSinks();
                UpdateWaterSeams();
                using (MkRetire.Auto()) RetireResolvedColumns();
                using (MkColliders.Auto()) UpdateColliders();
                UpdatePropDetail();
                DropLegacyRoot();
                CheckInitialComplete();
                return;
            }

            // Měřená varianta téhož pořadí (/teren spike on). Nic jiného nemění.
            var sw = System.Diagnostics.Stopwatch.StartNew();
            int gc0 = System.GC.CollectionCount(0);
            int regions0 = hydro != null ? hydro.ReadyCount : 0;
            BeginJobBudget();
            try
            {
                HydroUpdateBudgeted(); spikeT[0] = Lap(sw);
                CollectFinished(); spikeT[1] = Lap(sw);
                UpdateDesired(); spikeT[2] = Lap(sw);
                PromoteReadyColumns(); spikeT[3] = Lap(sw);
                PublishColumns(); spikeT[4] = Lap(sw);
                RebuildEdited(); UpdateTerrainSeams(); spikeT[6] = Lap(sw);
                ScheduleWork(); spikeT[5] = Lap(sw);
            }
            finally { EndJobBudget(); }
            UnloadFar(); UpdateMorphs(); UpdateSinks(); UpdateWaterSeams(); spikeT[7] = Lap(sw);
            RetireResolvedColumns(); spikeT[8] = Lap(sw);
            UpdateColliders(); spikeT[9] = Lap(sw);
            UpdatePropDetail(); spikeT[10] = Lap(sw);
            DropLegacyRoot();
            CheckInitialComplete(); spikeT[11] = Lap(sw);
            float total = 0f; for (int k = 0; k < 12; k++) total += spikeT[k];
            int regions1 = hydro != null ? hydro.ReadyCount : 0;

            // Předchozí snímek byl dlouhý? Pak vypiš rozpad jeho Update i tohohle.
            float dtMs = Time.unscaledDeltaTime * 1000f;
            int gcDuring = gc0 - spikePrevGc;
            if (gcDuring > 0 && spikePrevGc > 0)
            {
                spikeGcFrames++;
                spikeGcMsSum += dtMs;
                if (dtMs > spikeGcMsMax) spikeGcMsMax = dtMs;
                SpikeLog?.Invoke(string.Format(System.Globalization.CultureInfo.InvariantCulture,
                    "[GC] sběr gen0 během snímku {0:0.0} ms | inkrementální GC {1} | mono heap {2:0.0} MB, použito {3:0.0} MB | GC snímků {4}, průměr {5:0.0} ms, max {6:0.0} ms | ostatní snímky max {7:0.0} ms",
                    dtMs, UnityEngine.Scripting.GarbageCollector.isIncremental,
                    UnityEngine.Profiling.Profiler.GetMonoHeapSizeLong() / 1048576.0,
                    UnityEngine.Profiling.Profiler.GetMonoUsedSizeLong() / 1048576.0,
                    spikeGcFrames, spikeGcMsSum / spikeGcFrames, spikeGcMsMax, spikeOtherMax));
            }
            else if (spikePrevGc > 0 && dtMs > spikeOtherMax) spikeOtherMax = dtMs;
            // Kolik spravované paměti hra alokuje (bajty za předchozí snímek), zvlášť
            // při stavbě (inFlight > 0) a v klidu. Za 10 s jeden řádek do logu.
            if (!spikeAllocRec.Valid)
                spikeAllocRec = Unity.Profiling.ProfilerRecorder.StartNew(Unity.Profiling.ProfilerCategory.Memory, "GC Allocated In Frame");
            long alloc = spikeAllocRec.Valid ? spikeAllocRec.LastValue : 0;
            int bucket = inFlight.Count > 0 ? 1 : 0;
            spikeAllocSum[bucket] += alloc; spikeAllocFrames[bucket]++;
            if (alloc > spikeAllocMax[bucket]) spikeAllocMax[bucket] = alloc;

            // Snímky a čas GC v okně. Čas GC se bere ze značek profileru „GC.*“ (kromě
            // GC.Alloc), takže zachytí i kousky inkrementálního sběru, ne jen celý sběr.
            if (spikeGcRecs == null) StartGcRecorders();
            float gcMs = 0f;
            foreach (var rec in spikeGcRecs) if (rec.Valid) gcMs += rec.LastValue * 1e-6f;
            spikeWinFrames++;
            if (dtMs > spikeWinMax) spikeWinMax = dtMs;
            if (dtMs > 33.3f) spikeWin33++;
            if (dtMs > 50f) spikeWin50++;
            if (dtMs > 100f) spikeWin100++;
            spikeWinGcSum += gcMs;
            if (gcMs > spikeWinGcMax) spikeWinGcMax = gcMs;
            if (gcMs > 0.05f) spikeWinGcFrames++;

            if (Time.realtimeSinceStartup - spikeWindowStart > 10f)
            {
                int gcNow = System.GC.CollectionCount(0);
                SpikeLog?.Invoke(string.Format(System.Globalization.CultureInfo.InvariantCulture,
                    "[Okno] 10 s: {0} snímků, max {1:0.0} ms, >33 ms {2}×, >50 ms {3}×, >100 ms {4}× | GC: sběrů {5}, čas {6:0.0} ms v {7} snímcích, nejdelší {8:0.0} ms, inkrementální {9} | alloc stavba {10:0.0} KB/snímek ({11} sn.), klid {12:0.0} KB/snímek ({13} sn.) | mono použito {14:0.0} MB / heap {15:0.0} MB",
                    spikeWinFrames, spikeWinMax, spikeWin33, spikeWin50, spikeWin100,
                    gcNow - spikeWinGc0, spikeWinGcSum, spikeWinGcFrames, spikeWinGcMax,
                    UnityEngine.Scripting.GarbageCollector.isIncremental,
                    spikeAllocFrames[1] > 0 ? spikeAllocSum[1] / 1024.0 / spikeAllocFrames[1] : 0, spikeAllocFrames[1],
                    spikeAllocFrames[0] > 0 ? spikeAllocSum[0] / 1024.0 / spikeAllocFrames[0] : 0, spikeAllocFrames[0],
                    UnityEngine.Profiling.Profiler.GetMonoUsedSizeLong() / 1048576.0,
                    UnityEngine.Profiling.Profiler.GetMonoHeapSizeLong() / 1048576.0));
                spikeWindowStart = Time.realtimeSinceStartup;
                spikeAllocSum[0] = spikeAllocSum[1] = 0; spikeAllocFrames[0] = spikeAllocFrames[1] = 0;
                spikeAllocMax[0] = spikeAllocMax[1] = 0;
                spikeWinFrames = spikeWin33 = spikeWin50 = spikeWin100 = spikeWinGcFrames = 0;
                spikeWinMax = spikeWinGcSum = spikeWinGcMax = 0f;
                spikeWinGc0 = gcNow;
            }

            // Kolo 17: rozpad CELÉHO předchozího snímku podle značek hlavní smyčky (bez puštěného
            // profileru; jen při /teren spike on). Terénní Update sám o sobě dlouhé snímky nevysvětlil.
            if (spikeFrameRecs == null) StartFrameRecorders();
            if (dtMs > SpikeFrameMs || total > 30f)
            {
                SpikeLog?.Invoke(string.Format(System.Globalization.CultureInfo.InvariantCulture,
                    "[Spawn] previous frame object/detail work {0:0.0} ms (shared target 2 ms; individual operations cannot be interrupted)",
                    Orivilon.World.Spawning.ObjectSpawner.SpawnWorkMsInFrame(Time.frameCount - 1)));
                var fb = new System.Text.StringBuilder(256);
                for (int k = 0; k < spikeFrameRecs.Count; k++)
                {
                    // Záznamník drží poslední 3 snímky; v editoru bývá poslední hodnota o snímek pozadu,
                    // proto se vypisují všechny (nejstarší / … / nejnovější).
                    var rec = spikeFrameRecs[k];
                    if (!rec.Valid || rec.Count == 0) continue;
                    float mx = 0f;
                    for (int q = 0; q < rec.Count; q++) mx = Mathf.Max(mx, rec.GetSample(q).Value * 1e-6f);
                    if (mx < 0.5f) continue;
                    fb.Append(SpikeFrameNames[k]).Append(' ');
                    for (int q = 0; q < rec.Count; q++)
                        fb.Append(q > 0 ? "/" : "").Append((rec.GetSample(q).Value * 1e-6f).ToString("0.0", System.Globalization.CultureInfo.InvariantCulture));
                    fb.Append(", ");
                }
                SpikeLog?.Invoke(string.Format(System.Globalization.CultureInfo.InvariantCulture,
                    "[Spike] snímek {0:0.0} ms (předchozí Update terénu {1:0.0} ms, z toho osazení {21:0.0} ms / {22} sl.) | tento Update {2:0.0} ms: hydro {3:0.0}, collect {4:0.0}, desired {5:0.0}, promote {6:0.0}, publish {7:0.0}, schedule {8:0.0}, edited {9:0.0}, unload {10:0.0}, retire {11:0.0}, colliders {12:0.0}, props {13:0.0}, init {14:0.0} | GC0 +{15} (předchozí +{16}) | regiony {17}->{18}, inFlight {19}, sloupců {20} | předchozí snímek: {23}instancí stromů {24}",
                    dtMs, spikePrevTotal, total, spikeT[0], spikeT[1], spikeT[2], spikeT[3], spikeT[4], spikeT[5], spikeT[6],
                    spikeT[7], spikeT[8], spikeT[9], spikeT[10], spikeT[11], System.GC.CollectionCount(0) - gc0, gc0 - spikePrevGc,
                    regions0, regions1, inFlight.Count, columns.Count, spikePrevPropMs, spikePrevPropCols, fb,
                    Orivilon.World.Spawning.ObjectSpawner.HeavyInFrame(Time.frameCount - 1)));
            }
            spikePrevPropMs = spikePropMs; spikePrevPropCols = spikePropCols;
            spikePropMs = 0f; spikePropCols = 0;
            spikePrevTotal = total;
            spikePrevGc = System.GC.CollectionCount(0);
        }

        private static readonly Unity.Profiling.ProfilerMarker MkHydro = new Unity.Profiling.ProfilerMarker("VoxelTerrain.Hydro");
        private static readonly Unity.Profiling.ProfilerMarker MkCollect = new Unity.Profiling.ProfilerMarker("VoxelTerrain.CollectFinished");
        private static readonly Unity.Profiling.ProfilerMarker MkDesired = new Unity.Profiling.ProfilerMarker("VoxelTerrain.UpdateDesired");
        private static readonly Unity.Profiling.ProfilerMarker MkPromote = new Unity.Profiling.ProfilerMarker("VoxelTerrain.PromoteReadyColumns");
        private static readonly Unity.Profiling.ProfilerMarker MkPublish = new Unity.Profiling.ProfilerMarker("VoxelTerrain.PublishColumns");
        private static readonly Unity.Profiling.ProfilerMarker MkSchedule = new Unity.Profiling.ProfilerMarker("VoxelTerrain.ScheduleWork");
        private static readonly Unity.Profiling.ProfilerMarker MkUnload = new Unity.Profiling.ProfilerMarker("VoxelTerrain.UnloadFar");
        private static readonly Unity.Profiling.ProfilerMarker MkRetire = new Unity.Profiling.ProfilerMarker("VoxelTerrain.RetireResolvedColumns");
        private static readonly Unity.Profiling.ProfilerMarker MkColliders = new Unity.Profiling.ProfilerMarker("VoxelTerrain.UpdateColliders");

        /// <summary>Jen pro měření záseků (/teren spike on): rozpad času Update po krocích.</summary>
        public static bool SpikeProbe;
        public static System.Action<string> SpikeLog;
        private readonly float[] spikeT = new float[12];
        private float spikePrevTotal;
        private int spikeGcFrames;
        private static Unity.Profiling.ProfilerRecorder spikeAllocRec;
        private readonly long[] spikeAllocSum = new long[2], spikeAllocMax = new long[2];
        private readonly int[] spikeAllocFrames = new int[2];
        private float spikeWindowStart;
        private static System.Collections.Generic.List<Unity.Profiling.ProfilerRecorder> spikeGcRecs;
        private int spikeWinFrames, spikeWin33, spikeWin50, spikeWin100, spikeWinGcFrames, spikeWinGc0;
        private float spikeWinMax, spikeWinGcSum, spikeWinGcMax;

        private static void StartGcRecorders()
        {
            spikeGcRecs = new System.Collections.Generic.List<Unity.Profiling.ProfilerRecorder>();
            var handles = new System.Collections.Generic.List<Unity.Profiling.LowLevel.Unsafe.ProfilerRecorderHandle>();
            Unity.Profiling.LowLevel.Unsafe.ProfilerRecorderHandle.GetAvailable(handles);
            var names = new System.Text.StringBuilder();
            foreach (var h in handles)
            {
                var d = Unity.Profiling.LowLevel.Unsafe.ProfilerRecorderHandle.GetDescription(h);
                if (d.Name == null || !d.Name.StartsWith("GC.") || d.Name == "GC.Alloc") continue;
                if (d.UnitType != Unity.Profiling.ProfilerMarkerDataUnit.TimeNanoseconds) continue;
                spikeGcRecs.Add(new Unity.Profiling.ProfilerRecorder(h, 1, Unity.Profiling.ProfilerRecorderOptions.Default));
                names.Append(d.Name).Append(' ');
            }
            foreach (var r in spikeGcRecs) r.Start();
            SpikeLog?.Invoke("[Okno] sledované značky GC: " + names);
        }
        private float spikeGcMsSum, spikeGcMsMax, spikeOtherMax;

        /// <summary>Kolo 17: práh dlouhého snímku pro [Spike] (ms). Dřív 60 ms – špičky kolem 68 ms padaly těsně nad něj.</summary>
        public static float SpikeFrameMs = 45f;

        // Kolo 17: čas osazení (EcologyPlacer + instancování na hlavním vlákně) v tomto a předchozím snímku.
        private float spikePropMs, spikePrevPropMs;
        private int spikePropCols, spikePrevPropCols;

        private static readonly string[] SpikeFrameMarkers =
        {
            "PlayerLoop", "Update.ScriptRunBehaviourUpdate", "Update.ScriptRunDelayedDynamicFrameRate",
            "PreLateUpdate.ScriptRunBehaviourLateUpdate", "FixedUpdate.PhysicsFixedUpdate", "Physics.Processing",
            "Physics.BakeCollisionMeshes", "Instantiate", "Loading.ReadObject", "Shader.CreateGPUProgram",
            "PostLateUpdate.FinishFrameRendering", "Gfx.WaitForPresentOnGfxThread", "Gfx.WaitForGfxCommandsFromMainThread",
            "PostLateUpdate.UpdateAllRenderers", "PreLateUpdate.DirectorUpdateAnimationBegin", "EditorLoop",
            "Camera.Render", "RenderPipelineManager.DoRenderLoop_Internal()", "CullScriptable", "Shadows.RenderShadowMap",
            "PostLateUpdate.PlayerUpdateCanvases", "Canvas.SendWillRenderCanvases", "PostLateUpdate.PlayerEmitCanvasGeometry",
            "Gfx.PresentFrame", "Gfx.WaitForRenderThread",
        };
        private static readonly string[] SpikeFrameNames =
        {
            "PlayerLoop", "Update", "coroutiny", "LateUpdate", "fyzika", "fyzika.proc", "bake colliderů", "Instantiate",
            "načítání", "shader", "render", "čekání na GPU", "gfx čeká na main", "renderery", "animace", "EditorLoop",
            "Camera.Render", "URP smyčka", "culling", "stínové mapy", "UI canvasy", "UI WillRender", "UI geometrie",
            "present", "čekání na render vlákno",
        };
        private static System.Collections.Generic.List<Unity.Profiling.ProfilerRecorder> spikeFrameRecs;

        private static void StartFrameRecorders()
        {
            spikeFrameRecs = new System.Collections.Generic.List<Unity.Profiling.ProfilerRecorder>();
            for (int i = 0; i < SpikeFrameMarkers.Length; i++)
                spikeFrameRecs.Add(Unity.Profiling.ProfilerRecorder.StartNew(Unity.Profiling.ProfilerCategory.Internal, SpikeFrameMarkers[i], 3));
            var ok = new System.Text.StringBuilder();
            for (int i = 0; i < spikeFrameRecs.Count; i++) ok.Append(SpikeFrameNames[i]).Append(spikeFrameRecs[i].Valid ? "" : "(-)").Append(' ');
            SpikeLog?.Invoke("[Spike] značky snímku: " + ok);
        }
        private int spikePrevGc;

        private static float Lap(System.Diagnostics.Stopwatch sw)
        {
            float ms = (float)sw.Elapsed.TotalMilliseconds;
            sw.Restart();
            return ms;
        }

        /// <summary>
        /// Nemá žádný sloupec rozpracovaný job? Jen tehdy se smí uvolnit hydrologický region –
        /// sloupcové joby z jeho polí čtou a Unity by Dispose nad nimi odmítlo.
        /// </summary>
        private bool NoColumnJobsRunning()
        {
            foreach (var kv in columns)
                if (!kv.Value.ready) return false;
            return true;
        }

        private void OnDestroy()
        {
            if (artMaterial != null) Destroy(artMaterial);
            if (artWaterMaterial != null) Destroy(artWaterMaterial);
#if UNITY_EDITOR
            UnityEditor.AssemblyReloadEvents.beforeAssemblyReload -= ReleaseNative;
#endif
            Orivilon.World.Spawning.TreeProxies.Clear();

            // Nedokončené joby se musí dojet, jinak Unity nahlásí neuvolněné nativní kolekce.
            for (int i = 0; i < inFlight.Count; i++)
            {
                inFlight[i].handle.Complete();
                inFlight[i].data.Dispose();
            }
            inFlight.Clear();

            foreach (var e in columns.Values)
            {
                // Rozestavěné chunky nikdo jiný nedrží – bez tohohle by po reloadu domény
                // zůstaly viset Meshe, které Unity nahlásí jako únik.
                foreach (var kv in e.staged) DestroyChunkEntry(kv.Value);
                e.staged.Clear();

                if (e.waterMesh != null) DestroyImmediate(e.waterMesh);
                if (e.propsGo != null) DestroyImmediate(e.propsGo);
                DisposeColumn(e);
            }
            columns.Clear();

            // Hydrologie se uvolňuje AŽ TADY, ale ještě PŘED buildery: její joby čtou spline
            // LUT, které patří builderu, takže opačné pořadí by sáhlo do uvolněné paměti.
            ReleaseSearchHydro();
            hydro?.Dispose();
            hydro = null;

            foreach (var e in chunks.Values) DestroyChunkEntry(e);
            chunks.Clear();
            colChunks.Clear();
            colEmpty.Clear();

            if (legacyRoot != null) { DestroyImmediate(legacyRoot); legacyRoot = null; }
            while (pool.Count > 0) DestroyChunkEntry(pool.Pop());
            DisposeAllMorphs();
            DisposeExactTrees();

            if (builders != null)
                for (int i = 0; i < builders.Length; i++) builders[i]?.Dispose();
            lootBuilder?.Dispose(); lootBuilder = null;   // kolo MP 3: ověřovací builder hosta
            builders = null;
            builder = null;

            edits?.Dispose();
            edits = null;

            if (instance == this) instance = null;
        }

        // ── sloupce ────────────────────────────────────────────────────

        private int2 ViewerColumn()
        {
            Vector3 p = viewer.position;
            return new int2(Mathf.FloorToInt(p.x / ChunkSpan), Mathf.FloorToInt(p.z / ChunkSpan));
        }

        /// <summary>Šířka chunku dané úrovně ve světových jednotkách.</summary>
        private float SpanOf(int lod) => ChunkSpan * (1 << lod);

        /// <summary>Uzavřený obdélník sloupců jedné úrovně, v jejích vlastních souřadnicích.</summary>
        private struct Ring { public int2 lo, hi; }

        private Ring[] rings;

        /// <summary>Kopání a přisypávání. Veřejné kvůli ukládání a multiplayeru.</summary>
        public VoxelEdits Edits => edits;

        private VoxelEdits edits;
        private readonly List<int3> dirtyColumns = new List<int3>();

        /// <summary>
        /// Spočítá vnořené obdélníky prstenců.
        ///
        /// <para>Klíčová podmínka je, že hranice jemnějšího obdélníku musí ležet na mřížce
        /// toho hrubšího. Jinak by hrubý sloupec byl krytý jen zčásti: buď se zahodí a
        /// zůstane díra, nebo se nakreslí a překryje jemný terén. Proto se každý obdélník
        /// roztáhne ven na sudý začátek a lichý konec – tím obsáhne celý počet rodičovských
        /// buněk a rodič z něj může vyříznout přesnou díru.</para>
        /// </summary>
        private void UpdateDesired()
        {
            int2 vc = ViewerColumn();
            if (!desiredDirty && vc.x == lastViewerColumn.x && vc.y == lastViewerColumn.y) return;

            lastViewerColumn = vc;
            desiredDirty = false;
            coverageVersion++;   // kolo 5: masky překryvu závisí i na tom, co je žádané

            int levels = builders.Length;
            if (rings == null || rings.Length != levels) rings = new Ring[levels];

            Vector3 p = viewer.position;

            for (int l = 0; l < levels; l++)
            {
                float span = SpanOf(l);
                int cx = Mathf.FloorToInt(p.x / span);
                int cz = Mathf.FloorToInt(p.z / span);
                int r = l == 0 ? Mathf.Max(1, renderDistance) : Mathf.Max(2, lodRing);

                rings[l] = MakeRing(cx, cz, r);

                // Výkonové kolo 14.10.2026: hrubší prstenec musí jemnější obsáhnout celý
                // a s okrajem aspoň jednoho sloupce. Při velkém detailním dosahu (8+) byl
                // LOD0 prstenec větší než LOD1 – LOD1 pak vyšel prázdný, LOD2 vyřízl díru
                // jen podle LOD1 a jeho hrubá geometrie ležela PŘES jemný terén (překryv,
                // z-fighting, „mizení" při přechodu). Výchozí nastavení se tím nemění.
                if (l > 0)
                {
                    int2 holeLo = rings[l - 1].lo / 2, holeHi = (rings[l - 1].hi - 1) / 2;
                    while (rings[l].lo.x >= holeLo.x || rings[l].lo.y >= holeLo.y
                           || rings[l].hi.x <= holeHi.x || rings[l].hi.y <= holeHi.y)
                        rings[l] = MakeRing(cx, cz, ++r);
                }
            }

            desired.Clear();
            desiredSorted.Clear();

            for (int l = 0; l < levels; l++)
            {
                Ring ring = rings[l];

                // Díra po jemnějším prstenci, přepočtená do souřadnic tohoto LOD.
                bool hasHole = l > 0;
                int2 holeLo = default, holeHi = default;
                if (hasHole)
                {
                    holeLo = rings[l - 1].lo / 2;
                    holeHi = (rings[l - 1].hi - 1) / 2;
                }

                for (int z = ring.lo.y; z <= ring.hi.y; z++)
                for (int x = ring.lo.x; x <= ring.hi.x; x++)
                {
                    if (hasHole && x >= holeLo.x && x <= holeHi.x && z >= holeLo.y && z <= holeHi.y) continue;

                    var key = new int3(x, z, l);
                    desired.Add(key);
                    desiredSorted.Add(key);
                }
            }

            desiredSorted.Sort((a, b) => ColumnDist(a).CompareTo(ColumnDist(b)));
            IsInitialLoadComplete = false;
            terrainSeamScan = true;
        }

        private static Ring MakeRing(int cx, int cz, int r) => new Ring
        {
            lo = new int2(SnapDown(cx - r), SnapDown(cz - r)),
            hi = new int2(SnapUp(cx + r), SnapUp(cz + r)),
        };

        /// <summary>Zaokrouhlení dolů na sudé číslo (i pro záporná).</summary>
        private static int SnapDown(int v) => (v >> 1) << 1;

        /// <summary>Zaokrouhlení nahoru na liché číslo.</summary>
        private static int SnapUp(int v) => ((v >> 1) << 1) + 1;

        /// <summary>Vzdálenost středu sloupce od vieweru v metrech. Pořadí načítání.</summary>
        private float ColumnDist(int3 key)
        {
            float span = SpanOf(key.z);
            Vector3 p = viewer.position;
            float cx = (key.x + 0.5f) * span - p.x;
            float cz = (key.y + 0.5f) * span - p.z;
            return cx * cx + cz * cz;
        }

        /// <summary>
        /// Kolo 3: plné osazení LOD0 sloupců jen do tohoto čtvercového poloměru (ve sloupcích, stejná
        /// metrika jako LOD0 prstenec v MakeRing, +1 na zaokrouhlení prstence na sudé). Strop je AKTIVNÍ
        /// jen při detailu vyšším než PropsMaxColumns – při detailu ≤ 6 je chování přesně jako dřív
        /// (osazený celý prstenec, sloupce se retirují). 0 = vypnuto.
        /// </summary>
        public static int PropsMaxColumns = 0;   // kolo 3 po review: čisté A/B bez přínosu pro snímky → výchozí VYP, A/B přes /perf propscap 6
        private bool PropsCapActive => PropsMaxColumns > 0 && renderDistance > PropsMaxColumns;

        /// <summary>Leží LOD0 sloupec za stropem (+ <paramref name="extra"/> sloupců hystereze)?</summary>
        private bool BeyondPropsCap(int2 coord, int extra)
        {
            if (!PropsCapActive || lastViewerColumn.x == int.MinValue) return false;
            int d = Mathf.Max(Mathf.Abs(coord.x - lastViewerColumn.x), Mathf.Abs(coord.y - lastViewerColumn.y));
            return d > PropsMaxColumns + 1 + extra;
        }

        public static int StatPropsReleasedFar, StatPropsRehydrated;

        /// <summary>
        /// Kolo 3 (po review): osazení LOD0 sloupce, který se vzdálil za strop (+2 sloupce hystereze),
        /// se uvolní a sloupec dostane přesné zástupce stromů ze svého LOD0 pole. Při aktivním stropu
        /// se LOD0 sloupce neretirují (pole drží, paměť omezená velikostí prstence). Sloupec retirovaný
        /// DŘÍV (detail ≤ 6, pak přepnuto na 10) dostane pole znovu (jen 2D pole, chunky se nestaví)
        /// a uvolní se, až je hotové. Při návratu ho SpawnProps osadí znovu ze stejného pole – stejné
        /// rozmístění, stejné hashe, vykácené stromy filtruje registr zničených jako vždy.
        /// Nejvýš 2 uvolnění a 2 obnovy pole za snímek.
        /// </summary>
        private void ReleaseFarProps()
        {
            if (!PropsCapActive || viewer == null) return;
            int n = 0, rehydrated = 0;
            foreach (var kv in columns)
            {
                if (n >= 2) break;
                ColumnEntry e = kv.Value;
                if (e.lod != 0 || !e.propsRequested || e.propsBuild != null || !e.published || e.rebuilding) continue;
                if (e.spawner != null && e.spawner.SpawnInProgress) continue;
                if (!BeyondPropsCap(e.coord, 2)) continue;
                if (e.retired)
                {
                    if (rehydrated >= 2) continue;
                    RehydrateField(e);
                    rehydrated++;
                    continue;
                }
                if (!FieldReady(e)) continue;   // pole se teprve dopočítává
                if (EcoProps) VoxelProps.Release(e.spawner);
                if (e.propsGo != null) Destroy(e.propsGo);
                e.propsGo = null;
                e.spawner = null;
                e.propsRequested = false;
                e.proxyRequested = false;
                e.proxyDropped = false;
                if (WantsTreeProxies(e)) BuildProxiesFor(e);   // tentýž snímek – strom nezmizí
                StatPropsReleasedFar++;
                n++;
            }
        }

        /// <summary>Znovu naplánuje jen sloupcové pole retirovaného LOD0 sloupce (terén zůstává).</summary>
        private void RehydrateField(ColumnEntry e)
        {
            VoxelChunkBuilder b = builders[0];
            e.probe = b.AllocProbe(Allocator.Persistent);
            e.handle = b.ScheduleColumn(e.coord, Allocator.Persistent, e.probe, HydroRef(b.ColumnCenter(e.coord)), e.seamFine, out e.field);
            e.retired = false;
            e.fieldPending = true;
            StatPropsRehydrated++;
        }

        /// <summary>Je pole sloupce použitelné? Obnovené pole se dokončí, až doběhne job.</summary>
        private static bool FieldReady(ColumnEntry e)
        {
            if (!e.field.IsCreated) return false;
            if (!e.fieldPending) return true;
            if (!e.handle.IsCompleted) return false;
            e.handle.Complete();
            e.fieldPending = false;
            return true;
        }

        /// <summary>Kolo 3: strop práce PromoteReadyColumns za snímek (klasifikace chunků + hladina), ms.</summary>
        public static float PromoteFrameBudgetMs = 3f;

        private void PromoteReadyColumns()
        {
            long prt0 = System.Diagnostics.Stopwatch.GetTimestamp();
            int promoted = 0;
            foreach (var e in columns.Values)
            {
                if (e.ready || !e.handle.IsCompleted) continue;
                // Kolo 3: v CrossOver stálo povýšení dávky sloupců až 13 ms v jednom snímku.
                if (promoted > 0 && streamedOnce && !IsTeleporting && PromoteFrameBudgetMs > 0f
                    && (System.Diagnostics.Stopwatch.GetTimestamp() - prt0) * 1000.0 / System.Diagnostics.Stopwatch.Frequency > PromoteFrameBudgetMs
                    && ColumnDist(new int3(e.coord.x, e.coord.y, e.lod)) >= UrgentDistSqr)
                    continue;
                promoted++;

                e.handle.Complete();
                e.ready = true;
                StatColumnsReady++;

                VoxelChunkBuilder b = builders[e.lod];
                b.ColumnChunkRange(e.coord, e.field, out int minY, out int maxY);
                e.surfaceChunks = new List<int3>(6);

                for (int y = minY; y <= maxY; y++)
                {
                    if (b.Classify(e.coord, e.field, y, e.probe) != ChunkOccupancy.Surface) continue;
                    e.surfaceChunks.Add(new int3(e.coord.x, y, e.coord.y));
                }

                // Kolo 24: hladina přestavby švu se postaví až při prohození terénu (PublishColumn),
                // jinak by nová hladina pár snímků ležela na starém terénu.
                if (e.seamRebuild && e.published) e.seamWaterDeferred = true;
                else BuildWater(e);
            }

            using (MkProps.Auto()) SpawnProps();
            ReleaseFarProps();
            using (MkProxies.Auto()) SpawnTreeProxies();
        }

        private static readonly Unity.Profiling.ProfilerMarker MkPropAcquire = new Unity.Profiling.ProfilerMarker("Props.AcquireSpawner");
        private static readonly Unity.Profiling.ProfilerMarker MkPropBegin = new Unity.Profiling.ProfilerMarker("Props.BeginBuild");
        private static readonly Unity.Profiling.ProfilerMarker MkPropStep = new Unity.Profiling.ProfilerMarker("Props.PlacementStep");
        private static readonly Unity.Profiling.ProfilerMarker MkPropSpawn = new Unity.Profiling.ProfilerMarker("Props.SpawnPlacements");
        private static readonly Unity.Profiling.ProfilerMarker MkPropRaster = new Unity.Profiling.ProfilerMarker("Props.Raster");
        private static readonly Unity.Profiling.ProfilerMarker MkProps = new Unity.Profiling.ProfilerMarker("VoxelTerrain.SpawnProps");
        private static readonly Unity.Profiling.ProfilerMarker MkProxies = new Unity.Profiling.ProfilerMarker("VoxelTerrain.SpawnTreeProxies");

        /// <summary>Kolo 6: má sloupec dostat vzdálené stromy (a do té doby si nechat pole)?</summary>
        private bool WantsTreeProxies(ColumnEntry e)
            => e.lod <= Orivilon.World.Spawning.TreeProxies.MaxLod && !e.proxyRequested
               && (e.lod >= 1 || !PropsDone(e))
               && PropsActive && EcoProps && Orivilon.World.Spawning.TreeProxies.Enabled && VoxelProps.Available;

        /// <summary>
        /// Kolo 6: vzdálené stromy pro zveřejněné LOD1/LOD2 sloupce – jen vrstva stromů
        /// osazovače z pole sloupce, kreslí se instancovaně (<see cref="Orivilon.World.Spawning.TreeProxies"/>).
        /// Dva sloupce za snímek; LOD2 sloupec (128 m) stojí pod milisekundu.
        /// </summary>
        /// <summary>
        /// Dopočítal LOD0 sloupec rozmístění a začal instancovat skutečné stromy? Do té doby ho
        /// kreslí instancovaní zástupci – jinak by stromy v nově zjemnělé ploše chyběly, dokud
        /// osazovač nedojede (při rychlém pohybu klidně několik sekund). Zástupci se zahazují
        /// už na začátku instancování, ne na konci: jinak by se pár snímků kreslil strom
        /// dvakrát přes sebe (z-fighting korun).
        /// </summary>
        private static bool PropsDone(ColumnEntry e)
            => e.propsRequested && e.propsBuild == null;

        /// <summary>Osazení LOD0 sloupce úplně doběhlo (objekty stojí, dávky jsou u rendereru).</summary>
        private static bool PropsFinished(ColumnEntry e)
            => PropsDone(e) && (e.spawner == null || !e.spawner.SpawnInProgress);

        /// <summary>Diagnostika předání zástupců: převzato po kusech / zahozeno na konci bez protějšku.</summary>
        public static int StatProxyHandedOver, StatProxyDroppedUnmatched;

        /// <summary>
        /// Předá stromy LOD0 sloupce ze zástupců na skutečné objekty postupně, podle toho, co je
        /// opravdu vidět (ObjectSpawner.ReadyTrees). Odebírá se z vlastního klíče i z rodiče a
        /// prarodiče, které mohou ještě dobíhat (ReleaseDelay). Na konci se zahodí zbytek – ti
        /// zástupci nemají skutečný protějšek (vykácený, odmítnutý přesnou kontrolou).
        /// </summary>
        private void HandOverProxies(ColumnEntry e)
        {
            var sp = e.spawner;
            if (sp != null && sp.ReadyTrees.Count > 0)
            {
                long k0 = Orivilon.World.Spawning.TreeProxies.Key(e.coord.x, e.coord.y, 0);
                long k1 = Orivilon.World.Spawning.TreeProxies.Key(e.coord.x >> 1, e.coord.y >> 1, 1);
                long k2 = Orivilon.World.Spawning.TreeProxies.Key(e.coord.x >> 2, e.coord.y >> 2, 2);
                StatProxyHandedOver += Orivilon.World.Spawning.TreeProxies.RemoveMatching(k0, sp.ReadyTrees);
                Orivilon.World.Spawning.TreeProxies.RemoveMatching(k1, sp.ReadyTrees);
                Orivilon.World.Spawning.TreeProxies.RemoveMatching(k2, sp.ReadyTrees);
                sp.ReadyTrees.Clear();
            }
            if (!PropsFinished(e)) return;
            if (e.publishedAt > NearT0 && e.publishDist >= 0f)
            {
                // Kolo 4: latence zveřejnění → hotové osazení podle vzdálenosti při zveřejnění.
                float lag = Time.realtimeSinceStartup - e.publishedAt;
                int b = e.publishDist < 80f ? 0 : e.publishDist < 160f ? 1 : 2;
                PubPropsCount[b]++; PubPropsSum[b] += lag; if (lag > PubPropsMax[b]) PubPropsMax[b] = lag;
                if (lag > 2f) PubPropsSlow[b]++;
            }
            e.proxyDropped = true;
            if (!e.proxyRequested) return;
            long key = Orivilon.World.Spawning.TreeProxies.Key(e.coord.x, e.coord.y, 0);
            StatProxyDroppedUnmatched += Orivilon.World.Spawning.TreeProxies.CountOf(key);
            Orivilon.World.Spawning.TreeProxies.Remove(key, 0f);
        }

        private void SpawnTreeProxies()
        {
            // Zástupci LOD0 sloupců se předávají po kusech, jak se objevují skutečné stromy.
            foreach (var kv in columns)
            {
                ColumnEntry e = kv.Value;
                if (e.lod != 0 || e.proxyDropped || !PropsDone(e)) continue;
                HandOverProxies(e);   // i bez vlastních zástupců – dobíhající rodič/prarodič
            }

            int started = 0;
            foreach (var kv in columns)
            {
                if (started >= 4) break;
                ColumnEntry e = kv.Value;
                if (!WantsTreeProxies(e)) continue;
                if (!e.ready || !e.published || e.rebuilding || !FieldReady(e)) continue;
                if (Morphing(e)) continue;
                BuildProxiesFor(e);
                started++;
            }
            CollectExactFields();
            UpgradeApproxProxies();
            ReleaseCoveredOrphans();
            Orivilon.World.Spawning.TreeProxies.Tick();
        }

        private readonly List<Vector4> proxyScratch = new List<Vector4>(128);

        /// <summary>
        /// Postaví zástupce sloupce a TYTÉŽ stromy odebere předkům, kteří ještě dobíhají
        /// (osiřelí po uvolnění) – jeden strom se tak nikdy nekreslí dvakrát.
        /// </summary>
        private void BuildProxiesFor(ColumnEntry e)
        {
            e.proxyRequested = true;
            VoxelChunkBuilder b = builders[e.lod];
            var sp = VoxelProps.SpawnerAsset;
            if (sp == null) return;
            // Kolo 3: přednostně přesná data (pole v rozlišení LOD0, viz VoxelTerrain.ExactProxies.cs).
            List<Orivilon.World.Spawning.EcoPlacement> list;
            e.proxyApprox = false;
            if (e.lod >= 1 && ProxyAudit) AuditStoreApprox(e, sp, b);
            if (e.lod >= 1 && ExactTreeProxies)
            {
                if (!TryExactTrees(e.coord, e.lod, out list))
                {
                    RequestExactTrees(e.coord, e.lod);
                    list = EcologyPlacer.BuildTreeProxies(sp, e.field, b.ColumnPad, b.voxelSize, b.Params.seaLevel,
                                                          palette, b.Params.Micro, ResolvedSeed, builder.ChunkSpan);
                    e.proxyApprox = true;
                    StatExactApprox++;
                }
            }
            else
                list = EcologyPlacer.BuildTreeProxies(sp, e.field, b.ColumnPad, b.voxelSize, b.Params.seaLevel,
                                                      palette, b.Params.Micro, ResolvedSeed, builder.ChunkSpan,
                                                      e.lod == 0 && ExactTreeProxies ? 5f : 0f);
            proxyScratch.Clear();
            for (int i = 0; i < list.Count; i++)
                proxyScratch.Add(new Vector4(list[i].position.x, list[i].position.z, list[i].spawnable, 0f));
            for (int m = e.lod + 1; m <= Orivilon.World.Spawning.TreeProxies.MaxLod; m++)
            {
                int sh = m - e.lod;
                Orivilon.World.Spawning.TreeProxies.RemoveMatching(
                    Orivilon.World.Spawning.TreeProxies.Key(e.coord.x >> sh, e.coord.y >> sh, m), proxyScratch);
            }
            Orivilon.World.Spawning.TreeProxies.Add(
                Orivilon.World.Spawning.TreeProxies.Key(e.coord.x, e.coord.y, e.lod), sp, list);
        }

        // Oprava po review 14.10.2026: zástupci uvolněného hrubšího sloupce se nezahazují po
        // pevné 1,5 s, ale až když jeho plochu převezmou jemnější sloupce (vlastní zástupci
        // nebo doběhlé osazení). Do té doby z nich děti po kusech odebírají to, co už kreslí
        // samy. Pojistka OrphanProxyTimeout proti věčnému visení.
        private struct OrphanProxy { public int3 column; public float since; }
        private readonly Dictionary<long, OrphanProxy> orphanProxies = new Dictionary<long, OrphanProxy>();
        private readonly List<long> orphanDone = new List<long>();
        private const float OrphanProxyTimeout = 20f;

        private void ReleaseCoveredOrphans()
        {
            if (orphanProxies.Count == 0) return;
            orphanDone.Clear();
            float now = Time.unscaledTime;
            foreach (var kv in orphanProxies)
            {
                OrphanProxy o = kv.Value;
                if (now - o.since > OrphanProxyTimeout || Orivilon.World.Spawning.TreeProxies.CountOf(kv.Key) == 0
                    || OrphanCovered(o.column)) orphanDone.Add(kv.Key);
            }
            for (int i = 0; i < orphanDone.Count; i++)
            {
                orphanProxies.Remove(orphanDone[i]);
                Orivilon.World.Spawning.TreeProxies.Remove(orphanDone[i], 0f);
            }
        }

        /// <summary>Kryjí plochu osiřelého sloupce jeho žádané děti (zástupci nebo hotové osazení)?</summary>
        private bool OrphanCovered(int3 key)
        {
            bool anyDesired = false;
            for (int m = key.z - 1; m >= 0; m--)
            {
                int k = 1 << (key.z - m);
                for (int dz = 0; dz < k; dz++)
                for (int dx = 0; dx < k; dx++)
                {
                    var c = new int3(key.x * k + dx, key.y * k + dz, m);
                    if (!desired.Contains(c)) continue;
                    anyDesired = true;
                    if (!columns.TryGetValue(c, out ColumnEntry e) || !e.published) return false;
                    bool covered = m == 0 ? (e.proxyRequested || PropsFinished(e)) : e.proxyRequested;
                    if (!covered) return false;
                }
            }
            // Plochu převzal rodič (hrubší sloupec) – ten si zástupce staví při zveřejnění.
            if (!anyDesired) return true;
            return true;
        }

        private ColumnEntry activePropsColumn;
        private const double PropPlacementBudgetMs = 2.0;

        private void AdvancePropPlacements() => AdvancePropPlacements(PropPlacementBudgetMs);

        private void AdvancePropPlacements(double budgetMs)
        {
            ColumnEntry e = activePropsColumn;
            if (e == null) return;
            if (e.propsBuild == null) { activePropsColumn = null; return; }
            long started = System.Diagnostics.Stopwatch.GetTimestamp();
            bool complete;
            using (MkPropStep.Auto()) complete = e.propsBuild.Step(budgetMs);
            if (SpikeProbe)
                spikePropMs += (float)((System.Diagnostics.Stopwatch.GetTimestamp() - started)
                    * 1000.0 / System.Diagnostics.Stopwatch.Frequency);
            if (!complete) return;
            var build = e.propsBuild;
            if (ProxyAudit && e.lod == 0) AuditCompare(e.coord, build.Placements);
            using (MkPropSpawn.Auto())
                e.spawner.SpawnPlacements(e.propsGo.transform, build.Placements,
                                         new Vector2Int(e.coord.x, e.coord.y));
            if (SpikeProbe)
                SpikeLog?.Invoke(string.Format(System.Globalization.CultureInfo.InvariantCulture,
                    "[Props] column {0},{1}: placements total {2:0.0} ms, max slice {3:0.0} ms, candidates {4}",
                    e.coord.x, e.coord.y, build.ActiveMs, build.MaxSliceMs, build.Placements.Count));
            build.Dispose();
            e.propsBuild = null;
            activePropsColumn = null;
        }

        private static void CancelPropPlacements(ColumnEntry e)
        {
            if (e.propsBuild == null) return;
            e.propsBuild.Dispose();
            e.propsBuild = null;
            e.propsRequested = false;
            VoxelProps.Release(e.spawner);
            e.spawner = null;
            if (e.propsGo != null) Destroy(e.propsGo);
            e.propsGo = null;
        }

        /// <summary>
        /// Runs one resumable placement calculation at a time. Native column data stays
        /// alive until completion; object instantiation has its own shared frame budget.
        /// </summary>
        /// <summary>
        /// Kolo 4: osazení bez mezer mezi sloupci. Dřív: snímek = buď jeden 2ms krok aktivního sloupce,
        /// nebo jen start dalšího (bez kroku), a po dokončení sloupce se zbytek snímku zahodil.
        /// Hráč při chůzi (macOS release, detail 10) čekal od zveřejnění LOD0 sloupce na objekty
        /// průměrně 5,8 s (max 16 s). Nově se ve snímku pokračuje dalším sloupcem, dokud nedojde
        /// rozpočet; rozpočet je 2 ms, při frontě ≥ 3 sloupců 4 ms. Pořadí (nejbližší první) a
        /// determinismus rozmístění se nemění – mění se jen, kolik se toho stihne za snímek.
        /// A/B: <c>/perf propsfast on|off</c>.
        /// </summary>
        public static bool PropsFastPath = true;
        private const double PropBacklogBudgetMs = 4.0;
        private int propsBacklog;

        private void SpawnProps()
        {
            if (!PropsFastPath) { SpawnPropsOnce(PropPlacementBudgetMs); return; }
            long t0 = System.Diagnostics.Stopwatch.GetTimestamp();
            double budget = propsBacklog >= 3 ? PropBacklogBudgetMs : PropPlacementBudgetMs;
            for (int guard = 0; guard < 8; guard++)
            {
                double used = (System.Diagnostics.Stopwatch.GetTimestamp() - t0) * 1000.0 / System.Diagnostics.Stopwatch.Frequency;
                if (used >= budget - 0.25) break;
                bool hadActive = activePropsColumn != null;
                SpawnPropsOnce(budget - used);
                if (!hadActive && activePropsColumn == null) break;   // nic dalšího k osazení
                if (hadActive && activePropsColumn != null) break;    // krok neskončil sloupcem → rozpočet vyčerpán
            }
        }

        private void SpawnPropsOnce(double stepBudgetMs)
        {
            if (!PropsActive || !VoxelProps.Available) return;
            if (activePropsColumn != null)
            {
                AdvancePropPlacements(stepBudgetMs);
                // One shared placement slice per frame, even when a column finishes.
                return;
            }

            int started = 0;
            while (started < maxPropColumnsPerFrame)
            {
                // Výkonové kolo 14.10.2026: NEJBLIŽŠÍ čekající sloupec, ne první ve slovníku.
                // Dřív se při průjezdu osazovaly vzdálené sloupce dřív než ty u hráče a stromy
                // kolem něj chyběly („neviditelné"), dokud fronta nedojela.
                ColumnEntry e = null;
                float bestDist = float.MaxValue;
                int backlog = 0;
                foreach (var kv in columns)
                {
                    ColumnEntry c = kv.Value;
                    // Osazuje se jen nejjemnější prstenec. Ve vzdálených by stromy stály na
                    // terénu, který je o metry jinde, a stejně jsou pod dohledem dekorací.
                    if (c.lod != 0 || !c.ready || c.retired || c.propsRequested) continue;
                    // Ekologické osazení sedá na skutečný mesh, takže čeká na zveřejněný sloupec
                    // (všechny jeho LOD0 chunky hotové). Bonus: propy nikdy nepředběhnou terén.
                    if (EcoProps && (!c.published || c.rebuilding || !c.field.IsCreated)) continue;
                    if (Morphing(c)) continue;   // terén se ještě sesouvá do konečné polohy
                    float d = ColumnDist(kv.Key);
                    // Kolo 3: plné osazení jen do PropsMaxColumns (aktivní jen při detailu > 6). Dál
                    // kreslí LOD0 sloupec přesné instancované zástupce stromů (stejný zdroj jako LOD0),
                    // takže vyšší dosah zjemní terén, ale nesází tisíce GameObjectů navíc.
                    if (BeyondPropsCap(c.coord, 0)) continue;
                    if (!FieldReady(c)) continue;
                    backlog++;
                    if (d < bestDist) { bestDist = d; e = c; }
                }
                propsBacklog = backlog;
                if (e == null) break;

                e.propsRequested = true;
                started++;

                e.propsGo = new GameObject($"Props {e.coord.x} {e.coord.y}");
                e.propsGo.transform.SetParent(chunkRoot, false);

                long propT0 = SpikeProbe ? System.Diagnostics.Stopwatch.GetTimestamp() : 0;
                if (EcoProps)
                {
                    using (MkPropRaster.Auto()) BuildPropRaster(e);
                    EcologyPlacer.SetCaveParams(builder.Params);   // kolo 12c
                    using (MkPropAcquire.Auto()) e.spawner = VoxelProps.AcquireEcoSpawner(e.propsGo.transform, e.coord);
                    if (e.spawner != null)
                    {
                        using (MkPropBegin.Auto())
                        e.propsBuild = EcologyPlacer.BeginBuild(e.spawner, e.field, builder.ColumnPad,
                            voxelSize, builder.Params.seaLevel, e.coord, palette, builder.Params.Micro,
                            ResolvedSeed, e.waterLevels);
                        activePropsColumn = e;
                    }
                }
                else
                e.spawner = VoxelProps.Spawn(e.propsGo.transform, e.field, builder.ColumnPad,
                                             voxelSize, builder.Params.seaLevel, e.coord,
                                             palette, builder.Params.Micro);
                if (SpikeProbe)
                {
                    spikePropMs += (float)((System.Diagnostics.Stopwatch.GetTimestamp() - propT0) * 1000.0 / System.Diagnostics.Stopwatch.Frequency);
                    spikePropCols++;
                }
                if (e.spawner == null)
                {
                    Destroy(e.propsGo);
                    e.propsGo = null;
                }
                else if (viewer != null)
                {
                    // Kde se osazení objevuje vůči hráči – měřítko „pop-inu" (střed sloupce).
                    float span = builder.ChunkSpan;
                    float dx = (e.coord.x + 0.5f) * span - viewer.position.x;
                    float dz = (e.coord.y + 0.5f) * span - viewer.position.z;
                    float d = Mathf.Sqrt(dx * dx + dz * dz);
                    PropSpawnCount++;
                    if (d < 100f) PropSpawnNear100++;
                    else if (d < 150f) PropSpawnNear150++;
                    if (d < 150f) PopInRecord(e, d);
                }
                if (activePropsColumn != null) break;
            }
        }

        // ── kolo 22: diagnostika pop-inu osazení (jen měření, chování nemění) ──

        /// <summary>Začátek měřeného úseku (realtime) – nastavuje /props jizda.</summary>
        public static float PopInT0;
        public static readonly System.Text.StringBuilder PopInLog = new System.Text.StringBuilder();
        /// <summary>Třídy: A = osazeno v 1. s měření (dluh z doby před jízdou), B = terén sám zveřejněn &lt;150 m
        /// (osazení čekalo na terén), C = terén zveřejněn ≥150 m, ale osazení se opozdilo (rozpočet).</summary>
        public static int PopInA, PopInB, PopInC;
        public static float PopInMaxLagC;

        /// <summary>
        /// Kolo 22 diagnostika: stav žádaných LOD0 sloupců do 150 m od hráče – chybí (z toho kvůli
        /// hydrologii), rozestavěné, zveřejněné; a živé sloupce proti limitu. Nic nemění.
        /// </summary>
        public string PopInProbe(out int missing)
        {
            missing = 0;
            int hyd = 0, unpub = 0, pub = 0, live = 0;
            foreach (var kv in columns) if (!kv.Value.retired) live++;
            float span = SpanOf(0);
            foreach (int3 k in desired)
            {
                if (k.z != 0) continue;
                float dx = (k.x + 0.5f) * span - viewer.position.x, dz = (k.y + 0.5f) * span - viewer.position.z;
                if (dx * dx + dz * dz > 150f * 150f) continue;
                if (!columns.TryGetValue(k, out ColumnEntry e))
                {
                    missing++;
                    if (hydro != null && !hydro.IsReady(builders[0].ColumnCenter(new int2(k.x, k.y)))) hyd++;
                }
                else if (!e.published) unpub++;
                else pub++;
            }
            return string.Format(System.Globalization.CultureInfo.InvariantCulture,
                "chybí {0} (hydro {1}), rozestavěno {2}, hotovo {3}, živých {4}/{5}", missing, hyd, unpub, pub, live, maxLiveColumns);
        }

        public static readonly System.Text.StringBuilder TeleportStalls = new System.Text.StringBuilder();

        public static void PopInReset() { TeleportStalls.Length = 0; PopInT0 = Time.realtimeSinceStartup; PopInLog.Length = 0; PopInA = PopInB = PopInC = 0; PopInMaxLagC = 0f; }

        private void PopInRecord(ColumnEntry e, float d)
        {
            float now = Time.realtimeSinceStartup;
            float lag = e.publishedAt > 0f ? now - e.publishedAt : -1f;
            char cls;
            if (e.publishedAt > 0f && e.publishedAt < PopInT0) { cls = 'A'; PopInA++; }
            else if (e.publishDist >= 0f && e.publishDist < 150f) { cls = 'B'; PopInB++; }
            else { cls = 'C'; PopInC++; if (lag > PopInMaxLagC) PopInMaxLagC = lag; }
            if (PopInLog.Length < 2400)
                PopInLog.AppendFormat(System.Globalization.CultureInfo.InvariantCulture,
                    " {0}({1},{2}) t{3:0.00} d{4:0} pub{5:0}@{6:0.00} vzn{7:0.00};",
                    cls, e.coord.x, e.coord.y, now - PopInT0, d, e.publishDist, e.publishedAt - PopInT0, e.createdAt - PopInT0);
        }

        private readonly List<Mesh> rasterMeshes = new List<Mesh>(8);
        private readonly List<Matrix4x4> rasterMatrices = new List<Matrix4x4>(8);

        /// <summary>Naplní <see cref="SurfaceRaster"/> kolizními meshi LOD0 chunků sloupce.</summary>
        private void BuildPropRaster(ColumnEntry e)
        {
            rasterMeshes.Clear();
            rasterMatrices.Clear();
            if (e.surfaceChunks != null)
            {
                for (int i = 0; i < e.surfaceChunks.Count; i++)
                {
                    int3 c = e.surfaceChunks[i];
                    if (!chunks.TryGetValue(new int4(c.x, c.y, c.z, 0), out ChunkEntry ce)) continue;
                    if (ce.colliderMesh == null || ce.colliderMesh.vertexCount == 0) continue;
                    ce.CompleteBake();
                    rasterMeshes.Add(ce.colliderMesh);
                    rasterMatrices.Add(ce.go.transform.localToWorldMatrix);
                }
            }
            float2 corner = builder.ColumnCorner(e.coord);
            SurfaceRaster.Build(rasterMeshes, rasterMatrices, corner.x, corner.y, builder.ChunkSpan, 1f);
            // Kolo 24: osazení na přirozený povrch i tam, kde je teď šev k hrubšímu sousedovi.
            if (e.field.surfRaw.IsCreated)
                SurfaceRaster.SetSeamDelta(e.field.surfRaw, e.field.surfY, e.field.origin.x, e.field.origin.y,
                                           e.field.step, e.field.side);
        }

        /// <summary>Spawnery všech osazených sloupců (pro audit a statistiku propů).</summary>
        /// <summary>
        /// Kolo 22 diagnostika determinismu: otisk osazení LOD0 sloupců v okruhu – počet a hash
        /// (druh, vrstva, buňka, poloha na cm, měřítko na ‰) zvlášť pro objekty a pro trávu/drobnosti.
        /// Nezávisí na colliderech ani na detailním dosahu. Porovnává se mezi dvěma Play.
        /// </summary>
        public string PropFingerprint(Vector3 center, float radius)
        {
            var keys = new List<int3>();
            foreach (var kv in columns)
            {
                ColumnEntry e = kv.Value;
                if (e.lod != 0 || e.spawner == null || e.spawner.EcoPlaced == null) continue;
                float span = builder.ChunkSpan;
                float dx = (e.coord.x + 0.5f) * span - center.x, dz = (e.coord.y + 0.5f) * span - center.z;
                if (dx * dx + dz * dz > radius * radius) continue;
                keys.Add(kv.Key);
            }
            keys.Sort((a, b) => a.y != b.y ? a.y.CompareTo(b.y) : a.x.CompareTo(b.x));
            var sb = new System.Text.StringBuilder();
            var det = new List<Orivilon.World.Spawning.EcoPlacement>(4096);
            ulong all = 1469598103934665603UL; int nE = 0, nD = 0;
            var lines = new System.Text.StringBuilder();
            foreach (int3 k in keys)
            {
                ColumnEntry e = columns[k];
                ulong he = Fp(e.spawner.EcoPlaced);
                det.Clear(); e.spawner.CollectDetails(det);
                ulong hd = Fp(det);
                nE += e.spawner.EcoPlaced.Count; nD += det.Count;
                all = (all ^ he) * 1099511628211UL; all = (all ^ hd) * 1099511628211UL;
                lines.AppendFormat(System.Globalization.CultureInfo.InvariantCulture, "  ({0},{1}) obj {2} {3:x8} det {4} {5:x8}\n",
                    k.x, k.y, e.spawner.EcoPlaced.Count, (uint)he, det.Count, (uint)hd);
            }
            sb.AppendFormat(System.Globalization.CultureInfo.InvariantCulture,
                "[props otisk] r={0:0} kolem ({1:0},{2:0}): sloupců {3}, objektů {4}, trávy/drobností {5}, otisk {6:x16}\n",
                radius, center.x, center.z, keys.Count, nE, nD, all);
            sb.Append(lines);
            return sb.ToString();
        }

        private static ulong Fp(List<Orivilon.World.Spawning.EcoPlacement> list)
        {
            // Nezávislé na pořadí: součet hashů jednotlivých položek.
            ulong sum = 0;
            for (int i = 0; i < list.Count; i++)
            {
                var p = list[i];
                ulong h = 1469598103934665603UL;
                void Mix(int v) { h = (h ^ (uint)v) * 1099511628211UL; }
                Mix(p.spawnable); Mix(p.layer); Mix(p.cellX); Mix(p.cellZ);
                Mix(Mathf.RoundToInt(p.position.x * 100f)); Mix(Mathf.RoundToInt(p.position.y * 100f)); Mix(Mathf.RoundToInt(p.position.z * 100f));
                Mix(Mathf.RoundToInt(p.scale.y * 1000f));
                sum += h;
            }
            return sum;
        }

        public void CollectPropSpawners(List<Orivilon.World.Spawning.ObjectSpawner> list)
        {
            foreach (var e in columns.Values)
                if (e.spawner != null && e.propsGo != null) list.Add(e.spawner);
        }

        /// <summary>
        /// Zapne / vypne osazení za běhu (pro měření výkonu). Vypnutí smaže propy všech
        /// sloupců; po zapnutí se osadí jen sloupce, které ještě mají pole (nově načtené).
        /// </summary>
        public int SetPropsSuspended(bool suspended)
        {
            PropsSuspended = suspended;
            int n = 0;
            if (!suspended) return 0;
            Orivilon.World.Spawning.TreeProxies.Clear();
            foreach (var e in columns.Values) e.proxyRequested = true;
            foreach (var e in columns.Values)
            {
                CancelPropPlacements(e);
                if (EcoProps) VoxelProps.Release(e.spawner);
                if (e.propsGo != null) { Destroy(e.propsGo); n++; }
                e.propsGo = null;
                e.spawner = null;
            }
            return n;
        }

        /// <summary>
        /// Přepne dohled dekorací podle vzdálenosti sloupce od hráče. Spawner si pak sám
        /// dorovná, co má fyzicky existovat.
        /// </summary>
        private void UpdatePropDetail()
        {
            if (!PropsActive) return;

            int2 vc = lastViewerColumn;
            foreach (var e in columns.Values)
            {
                if (e.spawner == null || e.propsGo == null) continue;
                int d = Mathf.Max(Mathf.Abs(e.coord.x - vc.x), Mathf.Abs(e.coord.y - vc.y));
                e.spawner.UpdateDetailVisibility(d, e.propsGo.transform);
            }
        }

        /// <summary>
        /// Postaví hladinu sloupce. Je to jen čtení z hotového ColumnField bez šumu,
        /// takže to zvládne hlavní vlákno; job by se nevyplatil.
        /// </summary>
        private void BuildWater(ColumnEntry e)
        {
            if (waterMaterial == null) return;

            if (e.waterGo == null)
            {
                GameObject go = waterPool.Count > 0 ? waterPool.Pop() : CreateWaterObject();
                e.waterGo = go;
                e.waterFilter = go.GetComponent<MeshFilter>();
                e.waterRenderer = go.GetComponent<MeshRenderer>();
                e.waterMesh = e.waterFilter.sharedMesh != null
                    ? e.waterFilter.sharedMesh
                    : new Mesh { name = "water" };
            }

            VoxelChunkBuilder b = builders[e.lod];

            // Mřížku hladin si drží jen LOD0: jen tam může hráč fyzicky být, a u hrubších
            // prstenců by to byla paměť za data, na která se nikdo nezeptá.
            if (e.lod == 0 && e.waterLevels == null)
                e.waterLevels = new float[VoxelWorld.SampleDim * VoxelWorld.SampleDim];

            // Kolo 22: LOD0 si nechá vstup hladiny a šije jen hrany k hrubšímu sousedovi.
            int fine = 0;
            if (e.lod == 0)
            {
                if (e.waterSeam == null) e.waterSeam = WaterSurface.RentSnapshot();
                fine = WaterFineEdges(e.coord);
            }

            // Mřížka hladin (kamera pod vodou, mokro pro osazení) se staví VŽDY bez ohledu na
            // sousedy – stejně jako v kole 21 – aby osazení a jeho hashe nezávisely na tom, v jakém
            // pořadí se sousední sloupce zveřejnily. Jen viditelný mesh pak dostane šev podle souseda.
            bool hasWater = WaterSurface.Build(e.field, b.ColumnPad, b.voxelSize,
                                               b.Params.seaLevel, waterRiverMin,
                                               waterMinDepth, e.waterMesh, e.waterLevels,
                                               0, e.waterSeam);
            if (!hasWater) e.waterLevels = null;
            e.waterFine = 0;
            if (e.waterSeam != null && !e.waterSeam.Any)
            {
                WaterSurface.ReturnSnapshot(e.waterSeam);
                e.waterSeam = null;
            }
            else if (e.waterSeam != null && WaterSurface.SeamByNeighbor && fine != 0)
            {
                hasWater = WaterSurface.Rebuild(e.waterSeam, fine, e.waterMesh, null);
                e.waterFine = fine;
            }

            e.waterBuiltAt = Time.time;
            e.waterMaskOrig = null; e.maskVersion = -1;   // kolo 5: mesh přepsán celý
            e.waterGo.name = $"Water {e.coord.x} {e.coord.y} L{e.lod}";

            // Hladina se ukáže až se svým terénem (PublishColumn). Dřív se zapínala hned
            // po dopočtu pole, tedy dřív než chunky sloupce – a ještě za viditelného rodiče
            // z hrubšího LOD. Dvě hladiny přes sebe (dvojitá průhlednost, zubaté hrubé
            // okraje) a voda ležící na starém terénu, který k ní nepatří.
            e.hasWater = hasWater;
            e.waterGo.SetActive(hasWater && e.published && !e.replacedHidden);
            e.waterFilter.sharedMesh = e.waterMesh;
            e.waterRenderer.sharedMaterial = RenderWaterMaterial;
        }

        // ── kolo 22: šev hladiny podle skutečného LOD souseda ─────────────

        private bool waterSeamDirty;

        /// <summary>Kolo 22 diagnostika: přestavby hladiny kvůli změně souseda (počet, ms celkem, nejvíc za snímek).</summary>
        public int WaterSeamRebuilds;
        public double WaterSeamMs, WaterSeamFrameMaxMs;
        public int WaterSeamFrameMaxCount, WaterSeamSnapshots;

        /// <summary>
        /// Hrany LOD0 sloupce, za kterými je zveřejněný LOD0 soused, a jen na přímkách, kde se
        /// hladina jinak šije (LineLevel &gt; 0). Na přímkách po 32 m se nešije nikdy.
        /// </summary>
        private int WaterFineEdges(int2 c)
        {
            float span = Lod0Span;
            int m = 0;
            if (LodSeamJob.LineLevel(c.y * span, WaterSurface.SeamLine) > 0 && ColumnPublished(new int3(c.x, c.y - 1, 0))) m |= WaterSurface.EdgeZ0;
            if (LodSeamJob.LineLevel((c.y + 1) * span, WaterSurface.SeamLine) > 0 && ColumnPublished(new int3(c.x, c.y + 1, 0))) m |= WaterSurface.EdgeZ1;
            if (LodSeamJob.LineLevel(c.x * span, WaterSurface.SeamLine) > 0 && ColumnPublished(new int3(c.x - 1, c.y, 0))) m |= WaterSurface.EdgeX0;
            if (LodSeamJob.LineLevel((c.x + 1) * span, WaterSurface.SeamLine) > 0 && ColumnPublished(new int3(c.x + 1, c.y, 0))) m |= WaterSurface.EdgeX1;
            return m;
        }

        private readonly List<ColumnEntry> scratchSeam = new List<ColumnEntry>();

        /// <summary>
        /// Kolo 22: po zveřejnění/uvolnění LOD0 sloupců přestaví hladinu těch LOD0 sloupců,
        /// kterým se změnil soused (LOD0 ↔ hrubší). Jen 2D hladina ze snímku – terén, pole,
        /// hydrologie ani osazení se nepřestavují. Běží jen ve snímku, kdy se něco změnilo.
        /// </summary>
        private void UpdateWaterSeams()
        {
            if (!waterSeamDirty) return;
            waterSeamDirty = false;

            scratchSeam.Clear();
            int snaps = 0;
            foreach (var kv in columns)
            {
                ColumnEntry e = kv.Value;
                if (e.lod != 0 || e.waterSeam == null || e.waterMesh == null) continue;
                snaps++;
                int fine = WaterSurface.SeamByNeighbor ? WaterFineEdges(e.coord) : 0;
                if (fine != e.waterFine) scratchSeam.Add(e);
            }
            WaterSeamSnapshots = snaps;
            if (scratchSeam.Count == 0) return;

            var sw = System.Diagnostics.Stopwatch.StartNew();
            for (int i = 0; i < scratchSeam.Count; i++)
            {
                ColumnEntry e = scratchSeam[i];
                int fine = WaterSurface.SeamByNeighbor ? WaterFineEdges(e.coord) : 0;
                // e.waterLevels se nemění – drží se nezávislé na sousedech (determinismus osazení).
                bool has = WaterSurface.Rebuild(e.waterSeam, fine, e.waterMesh, null);
                e.waterFine = fine;
                e.hasWater = has;
                e.waterBuiltAt = Time.time;
                e.waterMaskOrig = null; e.maskVersion = -1;   // kolo 5: mesh přepsán celý, maska se případně obnoví
                if (e.waterGo != null) e.waterGo.SetActive(has && e.published && !e.replacedHidden);
            }
            double ms = sw.Elapsed.TotalMilliseconds;
            WaterSeamRebuilds += scratchSeam.Count;
            WaterSeamMs += ms;
            if (ms > WaterSeamFrameMaxMs) WaterSeamFrameMaxMs = ms;
            if (scratchSeam.Count > WaterSeamFrameMaxCount) WaterSeamFrameMaxCount = scratchSeam.Count;
        }

        /// <summary>Kolo 22: A/B přepínač – přestaví hladinu všech LOD0 sloupců se snímkem.</summary>
        public void SetWaterSeamByNeighbor(bool on)
        {
            WaterSurface.SeamByNeighbor = on;
            foreach (var kv in columns) if (kv.Value.waterSeam != null) kv.Value.waterFine = -1;
            waterSeamDirty = true;
            UpdateWaterSeams();
        }

        public string WaterSeamStatus()
            => string.Format(System.Globalization.CultureInfo.InvariantCulture,
                "[Voda sevlod] šev podle LOD souseda {0} | snímků LOD0 {1} (~{2:0.0} MB) | přestaveb {3}, celkem {4:0.0} ms, nejvíc za snímek {5} sloupců / {6:0.00} ms",
                WaterSurface.SeamByNeighbor ? "ZAP" : "VYP", WaterSeamSnapshots, WaterSeamSnapshots * 27.2 / 1024.0,
                WaterSeamRebuilds, WaterSeamMs, WaterSeamFrameMaxCount, WaterSeamFrameMaxMs);

        public void ResetWaterSeamStats() { WaterSeamRebuilds = 0; WaterSeamMs = 0; WaterSeamFrameMaxMs = 0; WaterSeamFrameMaxCount = 0; }

        // ── kolo 24: šev terénu podle skutečného LOD souseda ──────────────

        /// <summary>
        /// Kolo 24: LOD0 terén šije šev (pás 1,5–5 m k lomené čáře) jen k hrubšímu sousedovi.
        /// Na hraně LOD0|LOD0 zůstává přirozený povrch – stejně jako hladina od kola 22. VYP =
        /// chování do kola 23 (pás na každé přímce po 64 m). A/B: <c>/voda sevteren on|off</c>.
        /// </summary>
        public static bool TerrainSeamByNeighbor = true;

        /// <summary>Kolik přestaveb švu smí běžet naráz a kolik se jich smí začít za snímek.</summary>
        public static int SeamRebuildConcurrent = 8, SeamRebuildPerFrame = 2;

        /// <summary>Pojistka: po kolika sekundách čekání na souseda se hotová verze zveřejní i tak.</summary>
        public static float SeamPublishTimeout = 3f;

        private bool terrainSeamScan;
        private readonly List<int3> seamQueue = new List<int3>();
        private System.Comparison<int3> seamCompare;
        private readonly HashSet<int3> seamCand = new HashSet<int3>();

        /// <summary>Kolo 24 diagnostika.</summary>
        public int TerrainSeamQueued, TerrainSeamRestarts, TerrainSeamSwaps, TerrainSeamForced, TerrainSeamBlockedFrames;
        public float TerrainSeamWaitMax, TerrainSeamQueueMax;
        public double TerrainSeamScheduleMs, TerrainSeamFrameMaxMs;
        public double TerrainSeamWaterMs, TerrainSeamWaterFrameMaxMs;
        public int TerrainSeamPeakFields;
        public long TerrainSeamPeakBytes;
        private int seamWaterFrame;
        private double seamWaterFrameMs;

        /// <summary>
        /// Hrany LOD0 sloupce, za kterými je podle ŽÁDANÉHO prstence taky LOD0 – jen na přímkách,
        /// kde se jinak šije (LineLevel &gt; 0). Žádaný stav, ne zveřejněný: pole se staví dopředu,
        /// a že se ukáže až se sousedem, hlídá <see cref="FilterSeamPublish"/>.
        /// </summary>
        private int TerrainFineEdges(int2 c)
        {
            if (!TerrainSeamByNeighbor) return 0;
            float span = Lod0Span;
            int m = 0;
            if (LodSeamJob.LineLevel(c.y * span, WaterSurface.SeamLine) > 0 && desired.Contains(new int3(c.x, c.y - 1, 0))) m |= WaterSurface.EdgeZ0;
            if (LodSeamJob.LineLevel((c.y + 1) * span, WaterSurface.SeamLine) > 0 && desired.Contains(new int3(c.x, c.y + 1, 0))) m |= WaterSurface.EdgeZ1;
            if (LodSeamJob.LineLevel(c.x * span, WaterSurface.SeamLine) > 0 && desired.Contains(new int3(c.x - 1, c.y, 0))) m |= WaterSurface.EdgeX0;
            if (LodSeamJob.LineLevel((c.x + 1) * span, WaterSurface.SeamLine) > 0 && desired.Contains(new int3(c.x + 1, c.y, 0))) m |= WaterSurface.EdgeX1;
            return m;
        }

        /// <summary>
        /// Po změně žádaného prstence najde LOD0 sloupce, kterým se změnil soused, a omezeně je
        /// přestaví (jen dotčené sloupce, nejvýš <see cref="SeamRebuildPerFrame"/> za snímek).
        /// Staré chunky zůstávají vidět až do prohození – stejně jako po kopnutí.
        /// </summary>
        private void UpdateTerrainSeams()
        {
            if (terrainSeamScan)
            {
                terrainSeamScan = false;
                foreach (var kv in columns)
                {
                    ColumnEntry e = kv.Value;
                    if (e.lod != 0 || !desired.Contains(kv.Key)) continue;
                    if (TerrainFineEdges(e.coord) != e.seamFine && !seamQueue.Contains(kv.Key))
                    {
                        seamQueue.Add(kv.Key);
                        TerrainSeamQueued++;
                    }
                }
                if (seamQueue.Count > TerrainSeamQueueMax) TerrainSeamQueueMax = seamQueue.Count;
            }
            int held = 0; long heldBytes = 0;
            foreach (var kv in columns)
                if (kv.Value.seamRebuild) { held++; heldBytes += kv.Value.field.ApproxBytes; }
            if (held > TerrainSeamPeakFields) TerrainSeamPeakFields = held;
            if (heldBytes > TerrainSeamPeakBytes) TerrainSeamPeakBytes = heldBytes;
            if (seamQueue.Count == 0) return;

            int running = 0;
            foreach (var kv in columns)
                if (kv.Value.seamRebuild && kv.Value.rebuilding && !IsColumnComplete(kv.Value)) running++;

            var sw = System.Diagnostics.Stopwatch.StartNew();
            int started = 0;
            // Od nejbližšího – blízký šev je vidět víc než ten v mlze.
            if (seamCompare == null) seamCompare = (a, b) => ColumnDist(a).CompareTo(ColumnDist(b));
            seamQueue.Sort(seamCompare);
            // Výkonové kolo 14.10.2026: přestavby švu berou ze společného rozpočtu jobů a smí
            // obsadit nejvýš třetinu slotů (aspoň jeden) – nové pole a chunky tak nikdy úplně nestojí, a šev
            // přitom nečeká donekonečna (zveřejnění souseda má navíc pojistku SeamPublishTimeout).
            int seamCap = budgetLimited ? Mathf.Max(1, budgetSlots / 3) : SeamRebuildConcurrent;
            for (int i = 0; i < seamQueue.Count; i++)
            {
                if (started >= SeamRebuildPerFrame || running >= SeamRebuildConcurrent || running >= seamCap) break;
                if (!UnitsFree(1)) break;
                int3 key = seamQueue[i];
                if (!columns.TryGetValue(key, out ColumnEntry e) || !desired.Contains(key)
                    || TerrainFineEdges(e.coord) == e.seamFine)
                {
                    seamQueue.RemoveAt(i--);
                    continue;
                }
                if (!e.ready || e.pending > 0 || !e.handle.IsCompleted) continue;   // dostaví se, pak znovu

                TryTakeUnits(1);
                RestartColumn(e);
                CommitLane(e.handle);
                e.seamRebuild = true;
                e.seamBlockedSince = -1f;
                seamQueue.RemoveAt(i--);
                started++;
                running++;
                TerrainSeamRestarts++;
            }
            if (started > 0)
            {
                double ms = sw.Elapsed.TotalMilliseconds;
                TerrainSeamScheduleMs += ms;
                if (ms > TerrainSeamFrameMaxMs) TerrainSeamFrameMaxMs = ms;
            }
        }

        private static int Opp(int bit)
            => bit == WaterSurface.EdgeX0 ? WaterSurface.EdgeX1 : bit == WaterSurface.EdgeX1 ? WaterSurface.EdgeX0
             : bit == WaterSurface.EdgeZ0 ? WaterSurface.EdgeZ1 : WaterSurface.EdgeZ0;

        /// <summary>Bude LOD0 sloupec na konci snímku vidět, a s jakou maskou švu?</summary>
        private bool SeamVisibleEnd(int3 k, out int fine)
        {
            fine = 0;
            if (!columns.TryGetValue(k, out ColumnEntry e)) return false;
            if (seamCand.Contains(k)) { fine = e.seamFine; return true; }
            if (!e.published) return false;
            // Zveřejňuje-li se v tomhle snímku rodič, UnloadFar sloupec ještě tentýž snímek schová.
            if (seamCand.Contains(new int3(k.x >> 1, k.y >> 1, 1))) return false;
            fine = e.seamFinePub;
            return true;
        }

        private static readonly int2[] SeamDirs = { new int2(-1, 0), new int2(1, 0), new int2(0, -1), new int2(0, 1) };
        private static readonly int[] SeamBits = { WaterSurface.EdgeX0, WaterSurface.EdgeX1, WaterSurface.EdgeZ0, WaterSurface.EdgeZ1 };

        /// <summary>Sedí šev zveřejňovaného sloupce s tím, co bude na konci snímku vidět vedle?</summary>
        private bool SeamPublishOk(int3 k)
        {
            ColumnEntry e = columns[k];
            float span = Lod0Span;
            if (k.z == 0)
            {
                for (int d = 0; d < 4; d++)
                {
                    int bit = SeamBits[d];
                    float line = d == 0 ? k.x * span : d == 1 ? (k.x + 1) * span : d == 2 ? k.y * span : (k.y + 1) * span;
                    if (LodSeamJob.LineLevel(line, WaterSurface.SeamLine) <= 0) continue;
                    var n = new int3(k.x + SeamDirs[d].x, k.y + SeamDirs[d].y, 0);
                    bool want = (e.seamFine & bit) != 0;
                    if (SeamVisibleEnd(n, out int nf))
                    {
                        if (((nf & Opp(bit)) != 0) != want) return false;
                    }
                    else if (want)
                    {
                        var p = new int3(n.x >> 1, n.y >> 1, 1);
                        if (ColumnPublished(p) || seamCand.Contains(p)) return false;   // vedle by byl hrubý terén
                    }
                }
                return true;
            }
            // Kolo 24: hrubší sloupec (LOD1) na LOD0 souseda nečeká. Čekáním by vedle LOD0 sloupce,
            // který odchází, zůstal vidět až LOD2 (audit K24F s90210: přechodně 67 m na útesu). Čeká
            // jen LOD0 strana: přestavba se švem k hrubšímu se prohodí až se zveřejněním LOD1; když
            // LOD1 dojede dřív, je do prohození LOD0 (≤ ~1 s) na hraně nesoulad ≤ ~1 m pod sukní.
            return true;
        }

        /// <summary>Čtveřice nahrazující zveřejněného rodiče jde jen celá (po filtru).</summary>
        private bool SeamQuadOk(int3 k)
        {
            var parent = new int3(k.x >> 1, k.y >> 1, k.z + 1);
            if (!ColumnPublished(parent)) return true;
            for (int dz = 0; dz < 2; dz++)
            for (int dx = 0; dx < 2; dx++)
            {
                var s = new int3(parent.x * 2 + dx, parent.y * 2 + dz, k.z);
                if (!desired.Contains(s) || seamCand.Contains(s)) continue;
                if (columns.TryGetValue(s, out ColumnEntry se) && se.published && !se.rebuilding) continue;
                return false;
            }
            return true;
        }

        /// <summary>
        /// Kolo 24: z hotových sloupců nechá zveřejnit jen ty, jejichž šev terénu bude sedět se
        /// sousedem, který bude na konci snímku vidět. Přestavba švu a nový soused (čtveřice
        /// dětí nebo hrubý rodič) se tak prohodí v jednom snímku – bez schodu i bez mezery.
        /// Nic, co vidět není (načítání, teleport), nečeká. Pojistka: po SeamPublishTimeout se
        /// zveřejní i tak (počítá se do TerrainSeamForced).
        /// </summary>
        private void FilterSeamPublish(List<int3> list)
        {
            if (!TerrainSeamByNeighbor || list.Count == 0) return;
            seamCand.Clear();
            for (int i = 0; i < list.Count; i++) seamCand.Add(list[i]);
            float now = Time.realtimeSinceStartup;

            bool changed = true;
            for (int guard = 0; changed && guard < 32; guard++)
            {
                changed = false;
                for (int i = list.Count - 1; i >= 0; i--)
                {
                    int3 k = list[i];
                    if (!SeamQuadOk(k)) { seamCand.Remove(k); list.RemoveAt(i); changed = true; continue; }
                    if (SeamPublishOk(k)) continue;
                    ColumnEntry e = columns[k];
                    if (e.seamBlockedSince < 0f) e.seamBlockedSince = now;
                    if (now - e.seamBlockedSince > SeamPublishTimeout) { e.seamForced = true; continue; }
                    seamCand.Remove(k); list.RemoveAt(i); changed = true;
                }
            }
            for (int i = 0; i < list.Count; i++) columns[list[i]].seamForced &= !SeamPublishOk(list[i]);
            foreach (var kv in columns)
                if (kv.Value.seamBlockedSince >= 0f && !seamCand.Contains(kv.Key)) TerrainSeamBlockedFrames++;
        }

        private void SeamPublished(ColumnEntry e)
        {
            if (e.seamBlockedSince >= 0f)
            {
                float w = Time.realtimeSinceStartup - e.seamBlockedSince;
                if (w > TerrainSeamWaitMax) TerrainSeamWaitMax = w;
            }
            if (e.seamForced) TerrainSeamForced++;
            if (e.seamRebuild) TerrainSeamSwaps++;
            e.seamFinePub = e.seamFine;
            e.seamRebuild = false;
            e.seamForced = false;
            e.seamBlockedSince = -1f;
        }

        /// <summary>Kolo 24: A/B přepínač – přestaví (omezeně, stejnou cestou) všechny dotčené LOD0 sloupce.</summary>
        public void SetTerrainSeamByNeighbor(bool on)
        {
            TerrainSeamByNeighbor = on;
            terrainSeamScan = true;
        }

        public int TerrainSeamPending()
        {
            int n = seamQueue.Count;
            foreach (var kv in columns) if (kv.Value.seamRebuild) n++;
            return n;
        }

        public string TerrainSeamStatus()
            => string.Format(System.Globalization.CultureInfo.InvariantCulture,
                "[Teren sevlod] šev terénu podle LOD souseda {0} | ve frontě/rozestavěno {1} | zařazeno {2}, přestaveno {3}, prohozeno {4}, vynuceno {5} | čekání na souseda max {6:0.00} s ({7} sloupcosnímků) | plánování celkem {8:0.0} ms, nejvíc za snímek {9:0.00} ms | fronta max {10:0} | hladina při prohození celkem {11:0.0} ms, nejvíc za snímek {12:0.00} ms | drženo polí max {13} (~{14:0.00} MB)",
                TerrainSeamByNeighbor ? "ZAP" : "VYP", TerrainSeamPending(), TerrainSeamQueued, TerrainSeamRestarts, TerrainSeamSwaps,
                TerrainSeamForced, TerrainSeamWaitMax, TerrainSeamBlockedFrames, TerrainSeamScheduleMs, TerrainSeamFrameMaxMs, TerrainSeamQueueMax,
                TerrainSeamWaterMs, TerrainSeamWaterFrameMaxMs, TerrainSeamPeakFields, TerrainSeamPeakBytes / 1048576.0);

        public void ResetTerrainSeamStats()
        {
            TerrainSeamQueued = TerrainSeamRestarts = TerrainSeamSwaps = TerrainSeamForced = TerrainSeamBlockedFrames = 0;
            TerrainSeamWaitMax = TerrainSeamQueueMax = 0f; TerrainSeamScheduleMs = TerrainSeamFrameMaxMs = 0;
            TerrainSeamWaterMs = TerrainSeamWaterFrameMaxMs = 0; TerrainSeamPeakFields = 0; TerrainSeamPeakBytes = 0;
        }

        private GameObject CreateWaterObject()
        {
            var go = new GameObject("Water");
            go.transform.SetParent(chunkRoot, false);   // hladina je taky ve světových souřadnicích
            go.AddComponent<MeshFilter>();
            var mr = go.AddComponent<MeshRenderer>();
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            return go;
        }

        // ── plánování ──────────────────────────────────────────────────

        /// <summary>
        /// Výkonové kolo 14.10.2026: svět už jednou stál celý (konec loading screenu). Do té doby
        /// se terén staví na plný výkon všech vláken, potom s rezervou pro fyziku a render.
        /// </summary>
        private bool streamedOnce;

        /// <summary>A/B přepínač rozpočtu jobů (/perf budget on|off). Výchozí zapnuto.</summary>
        public static bool JobBudgetEnabled = true;

        /// <summary>Čítače propustnosti streameru pro /perf (sloupcová pole hotová, chunky postavené, sloupce zveřejněné).</summary>
        public static int StatColumnsReady, StatChunksBuilt, StatColumnsPublished;

        /// <summary>
        /// Rozpočet jobů (výkonové kolo 14.10.2026): kolik terénních jobů smí být naráz
        /// rozpracovaných (<paramref name="slots"/>; sloupcová pole + chunky dohromady) a kolik
        /// vláken smí zabrat jeden (<see cref="JobBudget.MaxParallel"/>). Jedno vlákno (od 9 dvě)
        /// tak zůstává vždy volné pro PhysX, culling a render. Při načítání a teleportu (hráč
        /// čeká, gameplay neběží) bez omezení – vrací false.
        /// </summary>
        private bool ComputeJobBudget(out int slots)
        {
            slots = int.MaxValue;
            if (!JobBudgetEnabled || !streamedOnce || IsTeleporting) return false;
            int workers = Mathf.Max(1, Unity.Jobs.LowLevel.Unsafe.JobsUtility.JobWorkerCount);
            slots = Mathf.Max(1, workers - (workers > 8 ? 2 : 1));
            return true;
        }

        /// <summary>Kolik sloupců ještě počítá pole (job opravdu běží nebo čeká ve frontě).</summary>
        private int ColumnsComputing()
        {
            int n = 0;
            foreach (var kv in columns) if (!kv.Value.ready && !kv.Value.handle.IsCompleted) n++;
            return n;
        }

        /// <summary>Kolik chunků má job ještě rozpracovaný (hotové, jen nepředané, vlákno nedrží).</summary>
        private int ChunksRunning()
        {
            int n = 0;
            for (int i = 0; i < inFlight.Count; i++) if (!inFlight[i].handle.IsCompleted) n += inFlight[i].par;
            return n;
        }

        // ── společný rozpočet jobů pro snímek (výkonové kolo 14.10.2026, oprava po review) ──
        // Všechny herní cesty, které plánují terénní joby – nová pole, chunky, přestavby
        // švu, kopání, pečení colliderů i hydrologie – berou jednotky z jednoho počítadla.
        // Jednotka = jedno pracovní vlákno, které job (nebo řetězec jobů) smí naráz zabrat;
        // paralelní joby se dělí na nejvýš tolik dávek (JobBudget.MaxParallel).
        //
        // DRÁHY (lanes): rozpočet má tolik drah, kolik smí terén naráz zabrat vláken. Každá
        // nová práce (řetězec jobů) se zařadí za poslední práci své dráhy – závislostí, ne
        // čekáním hlavního vlákna. Joby jedné dráhy tak nikdy neběží souběžně a každý job má
        // MaxParallel = 1 (jedna dávka), takže spustitelných terénních dávek je v každém
        // okamžiku nejvýš tolik, kolik je drah (+ 1 pro stavbu hydrologického regionu, která
        // zabírá vlastní dráhu). Dráha má místo, když za běžící prací čeká nejvýš jedna další –
        // vlákno tak nezahálí mezi snímky a fronta se nepřeplní.
        private bool budgetLimited;
        private int budgetSlots, budgetUsed, budgetChunkPar, laneCount, pendingLane = -1;
        private JobHandle[] laneTail, lanePrev;

        private void BeginJobBudget()
        {
            budgetLimited = ComputeJobBudget(out budgetSlots);
            budgetChunkPar = budgetLimited ? 1 : 0;
            JobBudget.MaxParallel = budgetLimited ? 1 : 0;
            JobBudget.Dependency = default;
            pendingLane = -1;
            if (!budgetLimited) { budgetUsed = 0; return; }
            int max = Unity.Jobs.LowLevel.Unsafe.JobsUtility.JobWorkerMaximumCount + 1;
            if (laneTail == null || laneTail.Length < max) { laneTail = new JobHandle[max]; lanePrev = new JobHandle[max]; }
            bool hydroBusy = hydro != null && hydro.BuildingCount > 0;
            laneCount = Mathf.Clamp(budgetSlots - (hydroBusy ? 1 : 0), 1, laneTail.Length);
            budgetUsed = BusyLanes() + (hydroBusy ? 1 : 0);
            if (hydro != null) hydro.AllowBegin = hydroBusy || budgetSlots > 1;   // region si vezme vlastní dráhu
        }

        private void EndJobBudget()
        {
            JobBudget.MaxParallel = 0;
            JobBudget.Dependency = default;
            pendingLane = -1;
            BudgetUnitsInUse = budgetLimited ? BusyLanes() + (hydro != null && hydro.BuildingCount > 0 ? 1 : 0) : 0;
        }

        private int BusyLanes()
        {
            int n = 0;
            for (int i = 0; i < laneCount; i++) if (!laneTail[i].IsCompleted) n++;
            return n;
        }

        private int LanesWithRoom()
        {
            int n = 0;
            for (int i = 0; i < laneCount; i++) if (lanePrev[i].IsCompleted) n++;
            return n;
        }

        /// <summary>Po naplánování práce: zařadí ji na konec vybrané dráhy.</summary>
        private void CommitLane(JobHandle h)
        {
            JobBudget.Dependency = default;
            if (pendingLane < 0) return;
            lanePrev[pendingLane] = laneTail[pendingLane];
            laneTail[pendingLane] = h;
            pendingLane = -1;
        }

        /// <summary>Diagnostika /perf: obsazené jednotky po naplánování v tomto snímku a velikost rozpočtu.</summary>
        public int BudgetUnitsInUse { get; private set; }
        public int BudgetSlots => budgetLimited ? budgetSlots : 0;

        private void HydroUpdateBudgeted()
        {
            if (hydro == null) return;
            int before = hydro.BuildingCount;
            hydro.Update(new float2(viewer.position.x, viewer.position.z), NoColumnJobsRunning());
            if (hydro.BuildingCount > before && budgetLimited && before == 0) laneCount = Mathf.Max(1, laneCount - 1);
        }

        private void HydroRequestBudgeted(float2 center)
        {
            if (budgetLimited) hydro.AllowBegin = hydro.BuildingCount > 0 || laneCount > 1;
            int before = hydro.BuildingCount;
            hydro.Request(center);
            if (hydro.BuildingCount > before && budgetLimited && before == 0) laneCount = Mathf.Max(1, laneCount - 1);
        }

        /// <summary>Jednotky obsazené joby, které ještě běží (hotové, jen nesebrané, vlákno nedrží).</summary>
        private int UnitsInUse()
        {
            int n = ColumnsComputing() + ChunksRunning() + BakesRunning();
            if (hydro != null && hydro.BuildingCount > 0) n += 1;
            return n;
        }

        private readonly List<ChunkEntry> bakeQueue = new List<ChunkEntry>(32);
        private readonly List<ChunkEntry> bakeRunning = new List<ChunkEntry>(32);

        private int BakesRunning()
        {
            int n = 0;
            for (int i = bakeRunning.Count - 1; i >= 0; i--)
            {
                ChunkEntry c = bakeRunning[i];
                if (!c.baking || c.bake.IsCompleted) { bakeRunning.RemoveAt(i); continue; }
                n++;
            }
            return n;
        }

        /// <summary>Pečení colliderů (jedno vlákno na kus) – nejvyšší priorita v rozpočtu, jsou krátká.</summary>
        private void ScheduleBakes()
        {
            int i = 0;
            for (; i < bakeQueue.Count; i++)
            {
                ChunkEntry c = bakeQueue[i];
                if (!c.bakeWanted || c.baking) continue;          // mezitím zrušeno / už se peče
                if (!TryTakeUnits(1)) break;
                c.bakeWanted = false;
                c.bake = new ColliderBakeJob
                {
                    meshId = c.colliderMesh.GetInstanceID(),
                    options = c.meshCollider.cookingOptions,
                }.Schedule(JobBudget.Dependency);
                CommitLane(c.bake);
                c.baking = true;
                bakeRunning.Add(c);
            }
            bakeQueue.RemoveRange(0, i);
        }

        /// <summary>
        /// Vybere dráhu s místem (přednostně nečinnou) pro jednu práci; plánovač pak musí
        /// použít JobBudget.Dependency a po naplánování zavolat CommitLane. Bez omezení vždy true.
        /// </summary>
        private bool TryTakeUnits(int units)
        {
            if (!budgetLimited) return true;
            int pick = -1;
            for (int i = 0; i < laneCount; i++)
            {
                if (laneTail[i].IsCompleted) { pick = i; break; }
                if (pick < 0 && lanePrev[i].IsCompleted) pick = i;
            }
            if (pick < 0) return false;
            pendingLane = pick;
            JobBudget.Dependency = laneTail[pick];
            return true;
        }

        /// <summary>Do této vzdálenosti (středu LOD0 sloupce od hráče, m²) je stavba naléhavá.</summary>
        private float UrgentDistSqr => 1.6f * ChunkSpan * 1.6f * ChunkSpan;

        private bool UnitsFree(int units) => !budgetLimited || LanesWithRoom() >= units;

        private void ScheduleWork()
        {
            ScheduleBakes();
            ScheduleWorkCore(budgetLimited, budgetSlots);
            ScheduleExactFields();   // kolo 3: nejnižší priorita – jen volné dráhy
        }

        private void ScheduleWorkCore(bool limited, int slots)
        {
            // Plánování sloupců se drží zpátky, dokud se ty rozdělané nedostaví. Bez toho
            // by při startu vzniklo naráz několik set sloupcových polí – práce se stejně
            // udělá, jen by se mezitím drželo desítky megabajtů nativní paměti navíc.
            int live = 0;
            // Kolo 24: přestavby švu se do limitu nepočítají – nové sloupce na ně čekají při
            // zveřejnění, takže by se jinak mohly vzájemně zablokovat.
            // Výkonové kolo 14.10.2026: hotový sloupec čekající jen na sourozence se nepočítá –
            // jinak by čekající skupiny zaplnily limit a streamer by stál (nic nového by se
            // neplánovalo, takže by se na sourozence čekalo donekonečna).
            // Stejně tak zveřejněný LOD0 sloupec, který drží pole už jen pro osazovač: terén má
            // hotový, a když osazovač při rychlém pohybu nestíhá, nesmí to zastavit stavbu terénu.
            // Paměť drží zvlášť strop polí (4× limit, ~150 kB na pole).
            int fields = 0;
            foreach (var kv in columns)
            {
                ColumnEntry ce = kv.Value;
                if (ce.retired) continue;
                // Kolo 3: při aktivním stropu plného osazení drží zveřejněné LOD0 sloupce pole
                // (uvolnění na zástupce a nové osazení při návratu). Je jich nejvýš tolik, kolik má LOD0 prstenec
                // (paměť omezená dosahem), a do stropu polí se nepočítají – jinak by při detailu
                // 10 zaplnily strop a streamer by stál (změřeno: 348 nedostavěných sloupců).
                if (ce.lod == 0 && ce.published && !ce.rebuilding && PropsCapActive) continue;
                fields++;
                if (ce.seamRebuild || IsColumnComplete(ce)) continue;
                if (ce.published && !ce.rebuilding && ce.pending == 0) continue;
                live++;
            }
            if (fields >= maxLiveColumns * 4) live = maxLiveColumns;

            int reserveColumn = 0;
            // Chunků je nejvýš maxChunksInFlight (počet scratch slotů), takže při hodně vláknech
            // smí jeden chunk zabrat víc vláken; sloupcové pole vždy jedno.
            int chunkPar = budgetChunkPar;
            if (limited)
            {
                // Chunky smí vzít všechny volné sloty; jeden se drží pro sloupcová pole, jen
                // když nějaký žádaný sloupec ještě nezačal (jinak by slot ležel ladem).
                bool columnDemand = false;
                if (live < maxLiveColumns)
                    for (int i = 0; i < desiredSorted.Count; i++)
                        if (!columns.ContainsKey(desiredSorted[i])) { columnDemand = true; break; }
                reserveColumn = columnDemand && ColumnsComputing() == 0 && slots > 1 ? 1 : 0;
            }

            // Chunky první: dokončují viditelný terén. Sloupcová pole pak doplní zbylé sloty.
            // Počítají se jen joby, které ještě běží – hotový chunk čekající na předání vlákno nedrží.
            if (limited) JobBudget.MaxParallel = 1;
            while (inFlight.Count < maxChunksInFlight)
            {
                if (!TryPickNearestChunk(out int3 coord, out ColumnEntry column)) break;
                bool urgent = limited && column.lod == 0
                              && ColumnDist(new int3(column.coord.x, column.coord.y, 0)) < UrgentDistSqr;
                if (!urgent && !UnitsFree(1 + reserveColumn)) break;

                VoxelChunkBuilder b = builders[column.lod];
                int slot = b.RentScratch();
                if (slot < 0) break;

                if (urgent) { JobBudget.MaxParallel = 0; JobBudget.Dependency = default; }
                else TryTakeUnits(1);
                JobHandle h = b.ScheduleChunk(coord, column.field, slot,
                                              Allocator.Persistent, out ChunkMeshData data,
                                              JobHandle.CombineDependencies(column.handle, JobBudget.Dependency),
                                              !b.IsUnderground(column.field, coord.y));
                if (urgent) JobBudget.MaxParallel = 1; else CommitLane(h);
                column.pending++;
                building.Add(new int4(coord, column.lod));
                inFlight.Add(new Pending { coord = coord, lod = column.lod, slot = slot,
                                           data = data, handle = h, column = column, par = 1 });
            }

            int newColumns = 0;
            if (limited) JobBudget.MaxParallel = 1;
            for (int i = 0; i < desiredSorted.Count && newColumns < maxColumnsPerFrame
                                                    && live < maxLiveColumns
                                                    && UnitsFree(1); i++)
            {
                int3 key = desiredSorted[i];
                if (columns.ContainsKey(key)) continue;

                VoxelChunkBuilder b = builders[key.z];
                var c = new int2(key.x, key.y);
                float2 center = b.ColumnCenter(c);

                // Sloupec se NEPLÁNUJE, dokud nestojí jeho hydrologický region – a rovnou
                // si o něj řekne. Fronta je seřazená podle vzdálenosti, takže se regiony
                // staví od hráče ven a nikdy se nečeká na něco, co nikdo nevyžádal.
                if (hydro != null && !hydro.IsReady(center)) { HydroRequestBudgeted(center); continue; }

                var e = new ColumnEntry { coord = c, lod = key.z, probe = b.AllocProbe(Allocator.Persistent), createdAt = Time.realtimeSinceStartup };
                RequestExactTrees(c, key.z);   // kolo 3: přesní zástupci stromů ať jsou do zveřejnění hotoví
                e.seamFine = key.z == 0 ? TerrainFineEdges(c) : 0;
                // Sloupec pod hráčem a hned vedle je naléhavý (bez něj není podlaha): jde mimo
                // dráhy, s plnou paralelizací – jako při načítání. Jsou to nejvýš jednotky sloupců.
                bool urgent = limited && key.z == 0 && ColumnDist(key) < UrgentDistSqr;
                if (urgent) { JobBudget.MaxParallel = 0; JobBudget.Dependency = default; }
                else TryTakeUnits(1);
                e.handle = b.ScheduleColumn(c, Allocator.Persistent, e.probe, HydroRef(center), e.seamFine, out e.field);
                if (urgent) JobBudget.MaxParallel = 1; else CommitLane(e.handle);
                columns[key] = e;
                newColumns++;
                live++;
            }

        }

        /// <summary>
        /// Lineární výběr nejbližšího nenačteného chunku. Sloupců je řádově sto a chunků
        /// na sloupec pár, takže se to vejde levněji než třídění fronty každý snímek.
        /// </summary>
        private bool TryPickNearestChunk(out int3 best, out ColumnEntry bestColumn)
        {
            best = default;
            bestColumn = null;
            float bestDist = float.MaxValue;

            foreach (var kv in columns)
            {
                ColumnEntry e = kv.Value;
                if (!e.ready || e.retired || e.surfaceChunks == null || !desired.Contains(kv.Key)) continue;

                float d = ColumnDist(kv.Key);
                // Výkonové kolo 14.10.2026: přestavba švu je kosmetika nad už viditelným terénem –
                // nový terén (jinak by se čekalo na hrubou náhradu) má přednost (vzdálenost ×4 ≈ 2× dál).
                if (e.seamRebuild && e.published) d *= 4f;
                if (d >= bestDist) continue;

                // Sloupec, který se teprve staví nebo přestavuje, se ptá na STAGED sadu.
                // Ptát se na `chunks` by u přestavby vidělo pořád ty staré chunky a nikdy
                // by nic nenaplánovalo – díra po kopnutí by zůstala navždy.
                bool staging = !e.published || e.rebuilding;

                for (int i = 0; i < e.surfaceChunks.Count; i++)
                {
                    int3 c = e.surfaceChunks[i];
                    var key = new int4(c, e.lod);

                    bool have = staging
                        ? e.staged.ContainsKey(key) || e.stagedEmpty.Contains(key)
                        : chunks.ContainsKey(key) || resolvedEmpty.Contains(key);

                    if (have || building.Contains(key)) continue;

                    best = c;
                    bestColumn = e;
                    bestDist = d;
                    break;
                }
            }

            return bestColumn != null;
        }

        private void CollectFinished()
        {
            int applied = 0;

            for (int i = inFlight.Count - 1; i >= 0; i--)
            {
                if (applied >= maxAppliesPerFrame) break;

                Pending p = inFlight[i];
                if (!p.handle.IsCompleted) continue;

                p.handle.Complete();

                var key = new int4(p.coord, p.lod);
                if (p.data.IsEmpty) p.column.stagedEmpty.Add(key);
                else ApplyChunk(key, p.data, p.column);

                p.data.Dispose();
                builders[p.lod].ReturnScratch(p.slot);
                p.column.pending--;
                building.Remove(key);
                inFlight.RemoveAt(i);

                onChunkLoaded?.Invoke();
                applied++;
                StatChunksBuilt++;
            }
        }

        // ── GameObjecty ────────────────────────────────────────────────

        /// <summary>
        /// Postaví GameObject hotového chunku, ale NECHÁ HO NEAKTIVNÍ.
        ///
        /// <para>Zveřejní se až s celým sloupcem v <see cref="PublishColumn"/>. Chunk sám
        /// o sobě není smysluplná jednotka viditelnosti: kdyby se ukazoval hned, střídaly by
        /// se ve výhledu kusy starého a nového LOD a přesně to vypadá jako problikávání.</para>
        /// </summary>
        private void ApplyChunk(int4 key, in ChunkMeshData data, ColumnEntry column)
        {
            ChunkEntry e = PopFreeChunkEntry();   // kolo 3: bez synchronního čekání na čtení morphu

            var coord = new int3(key.x, key.y, key.z);
            e.coord = coord;
            e.lod = key.w;
            e.go.name = $"Chunk {coord.x} {coord.y} {coord.z} L{key.w}";
            e.go.SetActive(false);
            e.renderTf.localPosition = Vector3.zero;   // mohl být zapuštěný (SinkColumn)
            e.renderTf.gameObject.SetActive(true);     // kolo 5: mohl být schovaný (HideReplaced)
            e.maskOrig = null;                         // kolo 5: mesh se přepíše celý, maska zaniká

            e.CompleteBake();
            EnsureMeshNotRead(e.renderMesh);   // znovupoužitý chunk mohla číst příprava morphu
            // Collider mají jen chunky LOD0 – hrubším se kolizní mesh vůbec nestaví (dřív se
            // plnil zbytečně u každého chunku; paměť i čas na hlavním vlákně).
            bool wantsCollider = key.w == 0;
            data.Apply(e.renderMesh, wantsCollider ? e.colliderMesh : null);
            if (!wantsCollider) e.colliderMesh.Clear();
            else if (e.colliderMesh.vertexCount > 0)
            {
                // Pečení se jen zařadí; naplánuje ho ScheduleBakes v rámci rozpočtu jobů.
                if (BakeInJobs) { e.bakeWanted = true; bakeQueue.Add(e); }
            }

            e.filter.sharedMesh = e.renderMesh;

            if (terrainMaterial == null && !warnedMaterial)
            {
                warnedMaterial = true;
                Debug.LogWarning("[VoxelTerrain] Není přiřazen materiál terénu – chunky budou růžové. " +
                                 "Použij Assets/Terrain.mat (čte vertex colors).", this);
            }
            e.meshRenderer.sharedMaterial = RenderMaterial;
            e.meshRenderer.shadowCastingMode = castShadows
                ? UnityEngine.Rendering.ShadowCastingMode.On
                : UnityEngine.Rendering.ShadowCastingMode.Off;

            // Collider se přiřazuje až podle vzdálenosti – přiřazení spustí cook v PhysX,
            // což je ta drahá část, ne samotný mesh.
            e.meshCollider.enabled = false;
            e.meshCollider.sharedMesh = null;
            e.colliderAssigned = false;

            column.staged[key] = e;
        }

        /// <summary>
        /// Zveřejní sloupce, které jsou celé hotové.
        ///
        /// <para>Sbírá se do pomocného seznamu, protože samotné zveřejnění může uvolnit
        /// jiné sloupce a měnit slovník, přes který by se zrovna iterovalo.</para>
        /// </summary>
        private void PublishColumns()
        {
            long pt0 = System.Diagnostics.Stopwatch.GetTimestamp();
            scratchPublish.Clear();
            lastBlockedMorph = 0;

            foreach (var kv in columns)
                if (IsColumnComplete(kv.Value)) scratchPublish.Add(kv.Key);

            // Sloupec, který přebírá plochu po hrubším rodiči, se nesmí zveřejnit sám.
            // Dokud jsou hotoví jen někteří ze čtyř sourozenců, ležela by nová jemná
            // geometrie přes tu starou hrubou a na skoro shodných plochách z toho je
            // z-fighting – tedy zase blikání, jen jinak vypadající. Celá čtveřice se
            // proto zveřejní naráz a rodič se schová ještě v tomtéž snímku v UnloadFar.
            int complete = scratchPublish.Count;
            for (int i = scratchPublish.Count - 1; i >= 0; i--)
                if (!SiblingsComplete(scratchPublish[i])) scratchPublish.RemoveAt(i);
            lastBlockedSiblings = complete - scratchPublish.Count;

            // Oprava po review: morph se připravuje asynchronně (EnsureMorphPrepared). Skupina
            // (vše pod týmž nejhrubším sloupcem – sourozenci se tak nikdy nerozdělí) se zveřejní,
            // až mají přípravu hotovou VŠICHNI její členové; do té doby zůstává starý terén.
            // Přestavby po kopání/švu se nemorfují a nečekají.
            if (scratchPublish.Count > 1)
            {
                if (publishGroupCompare == null) publishGroupCompare = ComparePublishGroup;
                scratchPublish.Sort(publishGroupCompare);
            }

            // Kolo 3 (měření Windows buildu v CrossOver): zveřejnění 18–42 sloupců naráz stálo
            // 12–33 ms v jednom snímku (+ uvolnění rodičů v UnloadFar). Za snímek se proto vezme jen
            // tolik CELÝCH skupin (nejbližší první), kolik se podle průběžně měřené ceny sloupce vejde
            // do PublishFrameBudgetMs. Skupina u hráče (urgentní vzdálenost) jde vždy. Omezí se
            // PŘED filtrem švů, aby šev počítal jen se sloupci, které se v tomto snímku opravdu ukážou.
            // Pořadí: nejdřív šev (na celém seznamu), pak jen jednotky s hotovou přípravou morphu,
            // pak rozpočet a nakonec ještě jednou šev – jinak by blokované jednotky zabíraly rozpočet
            // a streamer by vyhladověl (změřeno: 67 místo 379 zveřejněných sloupců za minutu).

            // Kolo 24: šev terénu závisí na sousedovi – co se ukáže, musí sedět s tím, co je vidět vedle.
            long pt1 = System.Diagnostics.Stopwatch.GetTimestamp();
            int beforeSeam = scratchPublish.Count;
            FilterSeamPublish(scratchPublish);
            if (TrimPublishToBudget(scratchPublish)) FilterSeamPublish(scratchPublish);
            lastBlockedSeam = beforeSeam - scratchPublish.Count;
            // Kolo 5: plocha odcházejícího sloupce se převezme celá naráz (viz VoxelTerrain.LodSwap.cs).
            // Až PO rozpočtu a švu – ty umí vyřadit jen část náhrady; šev se po vyřazení přepočítá.
            lastBlockedAtomic = 0;
            for (int it = 0; it < 4; it++)
            {
                int beforeAtomic = scratchPublish.Count;
                if (!FilterAtomicSwap(scratchPublish)) break;
                lastBlockedAtomic += beforeAtomic - scratchPublish.Count;
                FilterSeamPublish(scratchPublish);
            }
            long pt2 = System.Diagnostics.Stopwatch.GetTimestamp();
            double morph0 = StatMorphStartMs;
            int published = 0;
            for (int i = 0; i < scratchPublish.Count;)
            {
                int3 g = PublishGroup(scratchPublish[i]);
                int j = i;
                bool ready = true;
                while (j < scratchPublish.Count && PublishGroup(scratchPublish[j]).Equals(g))
                {
                    ColumnEntry ce = columns[scratchPublish[j]];
                    if (!(ce.published && !ce.rebuilding) && !EnsureMorphPrepared(ce)) ready = false;
                    j++;
                }
                if (!ready) { lastBlockedMorph += j - i; i = j; continue; }
                for (; i < j; i++)
                {
                    ColumnEntry e = columns[scratchPublish[i]];
                    if (e.published && !e.rebuilding) continue;   // zveřejněn už v tomhle průchodu
                    PublishColumn(e);
                    published++;
                }
            }
            if (published > 0)
            {
                double pms = (System.Diagnostics.Stopwatch.GetTimestamp() - pt2) * 1000.0 / System.Diagnostics.Stopwatch.Frequency / published;
                publishMsPerColumn = publishMsPerColumn <= 0.0 ? pms : publishMsPerColumn * 0.8 + pms * 0.2;
            }
            if (SpikeProbe)
            {
                double f = 1000.0 / System.Diagnostics.Stopwatch.Frequency;
                long pt3 = System.Diagnostics.Stopwatch.GetTimestamp();
                if ((pt3 - pt0) * f > 8.0)
                    SpikeLog?.Invoke(string.Format(System.Globalization.CultureInfo.InvariantCulture,
                        "[Publish] {0:0.0} ms: hotové+sourozenci {1:0.0}, šev filtr {2:0.0}, zveřejnění {3} sloupců {4:0.0} (z toho morph {5:0.0})",
                        (pt3 - pt0) * f, (pt1 - pt0) * f, (pt2 - pt1) * f, published, (pt3 - pt2) * f, StatMorphStartMs - morph0));
            }
        }

        /// <summary>Kolo 3: kolik ms smí za snímek stát zveřejňování sloupců (mimo skupiny u hráče).</summary>
        public static float PublishFrameBudgetMs = 4f;

        /// <summary>Průběžný odhad ceny zveřejnění jednoho sloupce (EMA), ms.</summary>
        private double publishMsPerColumn;
        public static int StatPublishDeferred;
        public static double StatPublishProxyMs;

        // Atomická jednotka zveřejnění = všechno žádané pod nejbližším ZVEŘEJNĚNÝM předkem (≤ 2 úrovně,
        // stejně jako SiblingsComplete); sloupec bez zveřejněného předka je jednotkou sám.
        private int3 PublishUnit(int3 k)
        {
            int levels = builders != null ? builders.Length : 1;
            for (int m = k.z + 1; m < levels && m <= k.z + 2; m++)
            {
                int sh = m - k.z;
                var p = new int3(k.x >> sh, k.y >> sh, m);
                if (ColumnPublished(p)) return p;
            }
            return k;
        }

        private readonly Dictionary<int3, int> unitCount = new Dictionary<int3, int>();
        private readonly Dictionary<int3, float> unitDist = new Dictionary<int3, float>();
        private readonly List<int3> unitOrder = new List<int3>(64);
        private readonly HashSet<int3> unitTake = new HashSet<int3>();
        private System.Comparison<int3> unitCompare;

        /// <returns>true, když se seznam zkrátil.</returns>
        private bool TrimPublishToBudget(List<int3> list)
        {
            if (PublishFrameBudgetMs <= 0f || list.Count <= 1 || !streamedOnce || IsTeleporting) return false;
            double per = publishMsPerColumn > 0.0 ? System.Math.Min(publishMsPerColumn, 2.0) : 0.5;
            int allowance = Mathf.Max(2, (int)(PublishFrameBudgetMs / per));
            // Dlouhá fronta hotových (typicky čekající na šev se sousedem): aspoň osmina za snímek,
            // aby se fronta nehromadila (změřeno: −22 % zveřejněných sloupců při pevném stropu).
            allowance = Mathf.Max(allowance, (list.Count + 7) / 8);
            if (list.Count <= allowance) return false;
            unitCount.Clear(); unitDist.Clear(); unitOrder.Clear(); unitTake.Clear(); unitBlocked.Clear();
            for (int i = 0; i < list.Count; i++)
            {
                int3 u = PublishUnit(list[i]);
                float d = ColumnDist(list[i]);
                // Jednotka, jejíž člen ještě čeká na přípravu morphu, se v tomto snímku stejně neukáže
                // (EnsureMorphPrepared přípravu případně rovnou spustí) – rozpočet nezabírá.
                ColumnEntry ce = columns[list[i]];
                if (!(ce.published && !ce.rebuilding) && !EnsureMorphPrepared(ce)) unitBlocked.Add(u);
                if (unitCount.TryGetValue(u, out int c)) { unitCount[u] = c + 1; if (d < unitDist[u]) unitDist[u] = d; }
                else { unitCount[u] = 1; unitDist[u] = d; unitOrder.Add(u); }
            }
            if (unitCompare == null) unitCompare = (x, y) => unitDist[x].CompareTo(unitDist[y]);
            unitOrder.Sort(unitCompare);
            int taken = 0;
            float urgent = UrgentDistSqr;
            for (int i = 0; i < unitOrder.Count; i++)
            {
                int3 u = unitOrder[i];
                if (unitBlocked.Contains(u)) continue;
                int c = unitCount[u];
                if (taken == 0 || taken + c <= allowance || unitDist[u] < urgent) { unitTake.Add(u); taken += c; }
            }
            int before = list.Count;
            for (int i = list.Count - 1; i >= 0; i--)
            {
                int3 u = PublishUnit(list[i]);
                if (unitTake.Contains(u)) continue;
                if (!unitBlocked.Contains(u)) StatPublishDeferred++;
                list.RemoveAt(i);
            }
            return list.Count != before;
        }

        private readonly HashSet<int3> unitBlocked = new HashSet<int3>();

        /// <summary>Je sloupec dostavěný a čeká jen na zveřejnění?</summary>
        private static bool IsColumnComplete(ColumnEntry e)
        {
            if (!e.ready || e.surfaceChunks == null) return false;
            if (e.published && !e.rebuilding) return false;
            if (e.pending > 0) return false;

            return e.staged.Count + e.stagedEmpty.Count >= e.surfaceChunks.Count;
        }

        /// <summary>
        /// Jsou hotoví i ostatní sloupci, kteří spolu s tímhle nahrazují jednoho hrubšího?
        ///
        /// <para>Když rodič není zveřejněný, není za koho čekat – to je běžný stav při
        /// prvním načtení světa i po kopnutí, takže se tím nic nezdrží.</para>
        ///
        /// <para>Zaseknout se to nemůže: dokud čtveřice nedojede, zůstává viditelný rodič.
        /// Nejhorší následek je, že hrubší LOD vydrží o pár snímků déle – ne díra.</para>
        /// </summary>
        private bool SiblingsComplete(int3 key)
        {
            // Výkonové kolo 14.10.2026: nejbližší ZVEŘEJNĚNÝ předek (rodič, prarodič…). Dřív se
            // hledal jen rodič; když plochu kryl prarodič (rychlý pohyb, změna dosahu), jemné
            // sloupce se zveřejňovaly po jednom přes hrubou geometrii.
            int levels = builders != null ? builders.Length : 1;
            int a = -1;
            int3 anc = default;
            // Nejvýš prarodič: skupina pod vzdálenějším předkem by měla až stovky sloupců
            // a čekání na ni by streamer zdrželo víc, než kolik stojí krátký překryv.
            for (int m = key.z + 1; m < levels && m <= key.z + 2; m++)
            {
                int sh = m - key.z;
                var p = new int3(key.x >> sh, key.y >> sh, m);
                if (ColumnPublished(p)) { a = m; anc = p; break; }
            }
            if (a < 0) return true;

            // Všechno žádané pod tímto předkem (na všech jemnějších úrovních) se musí
            // zveřejnit naráz – předek se schová v tomtéž snímku v UnloadFar.
            for (int m = a - 1; m >= 0; m--)
            {
                int k = 1 << (a - m);
                for (int dz = 0; dz < k; dz++)
                for (int dx = 0; dx < k; dx++)
                {
                    var c = new int3(anc.x * k + dx, anc.y * k + dz, m);
                    if (!desired.Contains(c)) continue;
                    if (!columns.TryGetValue(c, out ColumnEntry s)) return false;
                    if (s.published && !s.rebuilding) continue;
                    if (!IsColumnComplete(s)) return false;
                }
            }

            return true;
        }

        /// <summary>
        /// Prohodí staré chunky sloupce za nové – v jediném snímku.
        ///
        /// <para>Pořadí je podstatné: nejdřív se schová staré, pak ukáže nové. Obráceně
        /// by byl jeden snímek dvojitá geometrie a na skoro shodných plochách z toho je
        /// z-fighting, tedy zase blikání, jen jiné.</para>
        /// </summary>
        private void PublishColumn(ColumnEntry e)
        {
            if (e.seamWaterDeferred)
            {
                e.seamWaterDeferred = false;
                long w0 = System.Diagnostics.Stopwatch.GetTimestamp();
                BuildWater(e);
                double wms = (System.Diagnostics.Stopwatch.GetTimestamp() - w0) * 1000.0 / System.Diagnostics.Stopwatch.Frequency;
                TerrainSeamWaterMs += wms;
                if (Time.frameCount != seamWaterFrame) { seamWaterFrame = Time.frameCount; seamWaterFrameMs = 0; }
                seamWaterFrameMs += wms;
                if (seamWaterFrameMs > TerrainSeamWaterFrameMaxMs) TerrainSeamWaterFrameMaxMs = seamWaterFrameMs;
            }
            // Výkonové kolo 14.10.2026: plynulá výměna – nové chunky začnou na starém povrchu.
            BeginColumnMorph(e);

            var ck = new int3(e.coord.x, e.coord.y, e.lod);
            CollectColumnChunks(ck);
            for (int i = 0; i < scratchChunkKeys.Count; i++) RecycleChunk(scratchChunkKeys[i]);

            // Prázdné chunky staré verze se musí zapomenout taky – po odkopání prázdný
            // chunk být nemusí a naopak.
            ForgetColumnEmpty(ck);

            foreach (var kv in e.staged)
            {
                kv.Value.go.SetActive(true);
                if (!chunks.ContainsKey(kv.Key)) IndexAdd(colChunks, kv.Key);
                chunks[kv.Key] = kv.Value;
            }
            foreach (int4 k in e.stagedEmpty) if (resolvedEmpty.Add(k)) IndexAdd(colEmpty, k);

            e.staged.Clear();
            e.stagedEmpty.Clear();
            e.published = true;
            coverageVersion++;
            e.masked = false; e.maskVersion = -1;   // nové chunky mají celé meshe
            e.replacedHidden = false;   // kolo 5: nové chunky jsou vidět; UnloadFar případně schová znovu
            StatColumnsPublished++;
            e.rebuilding = false;
            if (e.lod == 0) waterSeamDirty = true;
            SeamPublished(e);
            if (e.publishDist < 0f && viewer != null)
            {
                float sp = SpanOf(e.lod);
                float pdx = (e.coord.x + 0.5f) * sp - viewer.position.x, pdz = (e.coord.y + 0.5f) * sp - viewer.position.z;
                e.publishDist = Mathf.Sqrt(pdx * pdx + pdz * pdz);
                e.publishedAt = Time.realtimeSinceStartup;
            }

            // Hladina patří ke sloupci stejně jako jeho chunky – ukáže se v témž snímku.
            if (e.waterGo != null) e.waterGo.SetActive(e.hasWater);

            // Oprava po review: hrubší sloupec, který přebírá plochu po jemnějších (s jejich
            // skutečnými stromy, které se v tomtéž snímku uvolní), postaví zástupce HNED –
            // jinak by stromy do jejich postavení chyběly.
            // Totéž pro LOD0 (do doběhnutí osazení). Na morph se nečeká: strom na konečné výšce
            // terénu, který se ještě ≤ 0,6 s dosouvá, je na hraně detailního prstence (200+ m)
            // nepostřehnutelný, kdežto chybějící strom vidět je.
            if (WantsTreeProxies(e) && e.field.IsCreated && !e.rebuilding)
            {
                long px0 = System.Diagnostics.Stopwatch.GetTimestamp();
                BuildProxiesFor(e);
                StatPublishProxyMs += (System.Diagnostics.Stopwatch.GetTimestamp() - px0) * 1000.0 / System.Diagnostics.Stopwatch.Frequency;
            }
            if (e.lod >= 1) orphanProxies.Remove(Orivilon.World.Spawning.TreeProxies.Key(e.coord.x, e.coord.y, e.lod));
        }

        /// <summary>Vrátí chunk do poolu a vyřadí ho ze scény.</summary>
        private void RecycleChunk(int4 key)
        {
            if (!chunks.TryGetValue(key, out ChunkEntry e)) return;
            chunks.Remove(key);
            IndexRemove(colChunks, key);
            RecycleEntry(e);
        }

        private void RecycleEntry(ChunkEntry e)
        {
            e.CompleteBake();
            EndMorph(e, false);
            e.go.SetActive(false);
            e.meshCollider.enabled = false;
            e.meshCollider.sharedMesh = null;
            e.colliderAssigned = false;
            e.generation++;
            pool.Push(e);
        }

        /// <summary>
        /// Je za sloupec, který vypadl z dohledu, hotová náhrada?
        ///
        /// <para>Prstence se mění jen o jednu úroveň, takže tutéž plochu může převzít buď
        /// čtveřice jemnějších dětí, nebo jeden hrubší rodič. Nic jiného se s ní překrývat
        /// nemůže – sloupce téže úrovně se nepřekrývají nikdy.</para>
        ///
        /// <para>Když se s ní nepřekrývá žádný žádaný sloupec, sloupec prostě opustil dohled
        /// a uvolní se hned; není za co čekat.</para>
        /// </summary>
        private bool ReplacementReady(int3 leaving)
        {
            // Výkonové kolo 14.10.2026: přes VŠECHNY úrovně, ne jen dítě/rodič. Při rychlém
            // pohybu, teleportu nebo změně dosahu přebírá plochu i vnuk nebo prarodič – dřív
            // se v tu chvíli sloupec uvolnil hned a do dostavění náhrady zela díra.
            int levels = builders != null ? builders.Length : 1;
            int l = leaving.z;

            for (int m = l - 1; m >= 0; m--)
            {
                int k = 1 << (l - m);
                for (int dz = 0; dz < k; dz++)
                for (int dx = 0; dx < k; dx++)
                {
                    var child = new int3(leaving.x * k + dx, leaving.y * k + dz, m);
                    if (desired.Contains(child) && !ColumnPublished(child)) return false;
                }
            }

            // Posun vpravo dělá u záporných čísel dělení dolů, což je přesně to, co
            // souřadnice rodiče potřebuje – dělení se zaokrouhlením k nule by u záporné
            // poloviny světa ukazovalo na sousední sloupec.
            for (int m = l + 1; m < levels; m++)
            {
                int sh = m - l;
                var parent = new int3(leaving.x >> sh, leaving.y >> sh, m);
                if (desired.Contains(parent) && !ColumnPublished(parent)) return false;
            }

            return true;
        }

        /// <summary>Kryje část plochy sloupce už zveřejněný sloupec jiné úrovně?</summary>
        private bool OverlappedByPublished(int3 key)
        {
            int levels = builders != null ? builders.Length : 1;
            int l = key.z;
            for (int m = l - 1; m >= 0; m--)
            {
                int k = 1 << (l - m);
                for (int dz = 0; dz < k; dz++)
                for (int dx = 0; dx < k; dx++)
                    if (ColumnPublished(new int3(key.x * k + dx, key.y * k + dz, m))) return true;
            }
            for (int m = l + 1; m < levels; m++)
            {
                int sh = m - l;
                if (ColumnPublished(new int3(key.x >> sh, key.y >> sh, m))) return true;
            }
            return false;
        }

        /// <summary>Zapustí / vrátí chunky odcházejícího sloupce (viz <see cref="ColumnEntry.sunk"/>).</summary>
        private void SinkColumn(ColumnEntry e, bool sink)
        {
            e.sunk = sink;
            sinking.Add(new int3(e.coord.x, e.coord.y, e.lod));   // posun se rozloží do ~0,35 s (bez skoku)
        }

        private readonly HashSet<int3> sinking = new HashSet<int3>();
        private readonly List<int3> sinkDone = new List<int3>();

        /// <summary>Hloubka zapuštění vzhledu odcházejícího sloupce (jen render potomek, ne collider).</summary>
        private float SinkDepth(int lod) => 0.5f * voxelSize * (1 << lod) + 0.25f;

        /// <summary>Plynule posouvá vzhled zapouštěných / vracených sloupců.</summary>
        private void UpdateSinks()
        {
            if (sinking.Count == 0) return;
            sinkDone.Clear();
            foreach (int3 k in sinking)
            {
                if (!columns.TryGetValue(k, out ColumnEntry e) || !colChunks.TryGetValue(k, out List<int4> l)) { sinkDone.Add(k); continue; }
                float depth = SinkDepth(k.z);
                float target = e.sunk ? -depth : 0f;
                float step = depth / 0.35f * Time.unscaledDeltaTime;
                bool done = true;
                for (int i = 0; i < l.Count; i++)
                {
                    if (!chunks.TryGetValue(l[i], out ChunkEntry c)) continue;
                    Vector3 p = c.renderTf.localPosition;
                    p.y = Mathf.MoveTowards(p.y, target, step);
                    c.renderTf.localPosition = p;
                    if (p.y != target) done = false;
                }
                if (done) sinkDone.Add(k);
            }
            for (int i = 0; i < sinkDone.Count; i++) sinking.Remove(sinkDone[i]);
        }

        /// <summary>Nejdéle, kolik smí sloupec mimo dohled čekat na náhradu, s.</summary>
        private const float MaxReplacementWait = 120f;

        private bool ColumnPublished(int3 key)
            => columns.TryGetValue(key, out ColumnEntry e) && e.published;

        private ChunkEntry CreateChunkObject()
        {
            var go = new GameObject("Chunk");
            go.transform.SetParent(chunkRoot, false);

            // Oprava po review 14.10.2026: vzhled je na vlastním potomkovi. Zapuštění
            // odcházejícího sloupce (SinkColumn) posouvá jen jeho vykreslení – collider
            // zůstává na kořeni chunku a fyzika se nikdy nehne.
            var render = new GameObject("Render");
            render.layer = go.layer;
            render.transform.SetParent(go.transform, false);

            return new ChunkEntry
            {
                go = go,
                renderTf = render.transform,
                filter = render.AddComponent<MeshFilter>(),
                meshRenderer = render.AddComponent<MeshRenderer>(),
                meshCollider = go.AddComponent<MeshCollider>(),
                renderMesh = new Mesh { name = "chunk_render" },
                colliderMesh = new Mesh { name = "chunk_collider" },
            };
        }

        /// <summary>
        /// Uvolní sloupce mimo dohled – ale až ve chvíli, kdy za ně stojí náhrada.
        ///
        /// <para>Tohle je druhá polovina řešení blikání. Chunk se od téhle chvíle neuvolňuje
        /// podle vlastního klíče, ale VŽDY se svým sloupcem; jinak by se stará hrubá geometrie
        /// schovala dřív, než dojedou její jemnější náhrady, a při chůzi by v terénu na každém
        /// kroku probleskla díra.</para>
        /// </summary>
        private void UnloadFar()
        {
            if (chunks.Count == 0 && columns.Count == 0) return;

            scratchColumnKeys.Clear();

            foreach (var kv in columns)
            {
                ColumnEntry e = kv.Value;

                if (desired.Contains(kv.Key))
                {
                    e.unloadAt = 0f; e.leavingSince = -1f; e.atomicForced = false;
                    if (e.sunk) SinkColumn(e, false);
                    if (e.replacedHidden) HideReplaced(kv.Key, e, false);   // rychlý návrat
                    if (e.masked) ClearOverlapMask(kv.Key, e);
                    continue;
                }
                if (e.leavingSince < 0f) e.leavingSince = Time.unscaledTime;
                // Kolo 5: odcházející sloupec se už nezapouští – zůstává na své výšce, dokud ho
                // náhrada nepřevezme CELÝ (FilterAtomicSwap) a v témž snímku se níž neuvolní.
                // Původní zapouštění zůstává jen jako A/B (/perf sink on).
                if (LegacySinkOutgoing) { if (e.published && !e.sunk && OverlappedByPublished(kv.Key)) SinkColumn(e, true); }
                else
                {
                    if (e.sunk) SinkColumn(e, false);
                    // Překryv vznikne jen výjimečně (pojistka atomické výměny, sloupec u hráče):
                    // tam, kde už kreslí náhrada, se trojúhelníky starého sloupce nevykreslí.
                    // Sloupec, za který je hotová celá náhrada, se v tomto průchodu uvolní (nebo schová) – maska by byla zbytečná práce.
                    if (e.published && !e.replacedHidden && e.maskVersion != coverageVersion && !ReplacementReady(kv.Key))
                        UpdateOverlapMask(kv.Key, e);
                }
                if (e.pending > 0)
                {
                    // Celý nahrazený, ale nesmí se uvolnit, dokud mu běží job: schová se jen vzhled.
                    if (!LegacySinkOutgoing && e.published && !e.replacedHidden && OverlappedByPublished(kv.Key) && ReplacementReady(kv.Key))
                        HideReplaced(kv.Key, e, true);
                    continue;
                }

                if (e.published)
                {
                    // Pojistka proti věčnému visení; s rozpočtem jobů trvá dostavění náhrady
                    // déle než dřív, a 4 s by při rychlém letu otevíraly díry. Paměť je
                    // omezená plochou dohledu: drží se jen sloupce, které žádaný sloupec překrývá.
                    if (e.unloadAt <= 0f) e.unloadAt = Time.unscaledTime + Mathf.Max(unloadGrace, MaxReplacementWait);
                    if (!ReplacementReady(kv.Key) && Time.unscaledTime < e.unloadAt) continue;
                }

                scratchColumnKeys.Add(kv.Key);
            }

            for (int i = 0; i < scratchColumnKeys.Count; i++)
                UnloadColumn(scratchColumnKeys[i], columns[scratchColumnKeys[i]]);

            // Pojistka: chunk, jehož sloupec už neexistuje, by jinak zůstal viset ve scéně.
            // Nemělo by nastat, ale díra v terénu se hledá hůř než tenhle průchod stojí.
            if (chunks.Count > 0)
            {
                scratchChunkKeys.Clear();
                foreach (var kv in chunks)
                    if (!columns.ContainsKey(new int3(kv.Key.x, kv.Key.z, kv.Key.w)))
                        scratchChunkKeys.Add(kv.Key);

                for (int i = 0; i < scratchChunkKeys.Count; i++) RecycleChunk(scratchChunkKeys[i]);
            }
        }

        /// <summary>Uvolní sloupec i všechno, co k němu patří – chunky, vodu a osazení.</summary>
        private void UnloadColumn(int3 key, ColumnEntry e)
        {
            if (e.published) coverageVersion++;   // kolo 5: masky překryvu se přepočítají
            var ck = new int3(e.coord.x, e.coord.y, e.lod);
            CollectColumnChunks(ck);
            for (int i = 0; i < scratchChunkKeys.Count; i++) RecycleChunk(scratchChunkKeys[i]);

            foreach (var kv in e.staged) RecycleEntry(kv.Value);
            e.staged.Clear();
            e.stagedEmpty.Clear();

            ForgetColumnEmpty(ck);

            if (e.waterGo != null)
            {
                e.waterGo.SetActive(false);
                waterPool.Push(e.waterGo);
                e.waterGo = null;
            }
            if (e.waterSeam != null) { WaterSurface.ReturnSnapshot(e.waterSeam); e.waterSeam = null; }
            if (e.lod == 0) waterSeamDirty = true;

            // Osazení se nepooluje – objekty jsou různé prefaby, uklízí se celý podstrom.
            // Jen spawner (kolo 6) se před tím odpojí a vrátí do poolu.
            CancelPropPlacements(e);
            if (EcoProps) VoxelProps.Release(e.spawner);
            if (e.propsGo != null) Destroy(e.propsGo);
            e.propsGo = null;
            e.spawner = null;

            // Vzdálené stromy zůstávají, dokud plochu nepřevezmou jemnější sloupce (OrphanCovered),
            // a mezitím z nich děti odebírají, co už kreslí samy. Když plochu přebírá hrubší
            // rodič, ten zástupce postavil už při svém zveřejnění, takže se zahodí hned.
            if (e.proxyRequested && !e.proxyDropped)
            {
                long pk = Orivilon.World.Spawning.TreeProxies.Key(e.coord.x, e.coord.y, e.lod);
                if (e.lod > 0) orphanProxies[pk] = new OrphanProxy { column = key, since = Time.unscaledTime };
                else Orivilon.World.Spawning.TreeProxies.Remove(pk, 0f);
            }

            DisposeColumn(e);
            columns.Remove(key);
        }

        /// <summary>
        /// Uvolní sloupcové pole, jakmile všechny jeho chunky stojí.
        ///
        /// <para>Pole zabírá kolem 150 kB a s LOD prstenci jich je naráz několik set –
        /// bez tohohle by streamer držel desítky megabajtů nativní paměti, kterou už
        /// nikdo nepotřebuje. Voda i osazení se z pole čtou jednorázově při promotion,
        /// chunky se z něj přestavují jen dokud sloupec zůstává v dohledu, a jakmile je
        /// dostaví, nikdo se ho už nezeptá. Záznam sloupce zůstává jako značka, aby se
        /// neplánoval znovu.</para>
        /// </summary>
        private void RetireResolvedColumns()
        {
            foreach (var kv in columns)
            {
                ColumnEntry e = kv.Value;

                // Zveřejněný sloupec má z definice všechny chunky hotové, takže se
                // jednotlivě dohledávat nemusí. Rozestavěná přestavba pole ještě potřebuje.
                if (!e.published || e.rebuilding || e.retired || e.pending > 0) continue;
                if (e.propsBuild != null) continue;
                if (e.lod == 0 && PropsActive && VoxelProps.Available && !e.propsRequested) continue;
                if (e.lod == 0 && PropsCapActive) continue;   // kolo 3: pole pro uvolnění/návrat osazení
                if (WantsTreeProxies(e)) continue;

                e.retired = true;
                if (e.fieldPending) { e.handle.Complete(); e.fieldPending = false; }
                if (e.field.IsCreated) e.field.Dispose();
                if (e.probe.IsCreated) e.probe.Dispose();
            }
        }

        private void DisposeColumn(ColumnEntry e)
        {
            CancelMorphPrep(e);
            CancelPropPlacements(e);
            if (!e.retired) e.handle.Complete();
            if (e.field.IsCreated) e.field.Dispose();
            if (e.probe.IsCreated) e.probe.Dispose();
        }

        private static void DestroyChunkEntry(ChunkEntry e)
        {
            e.CompleteBake();
            if (e.morph != null) { DisposeMorph(e.morph); e.morph = null; }
            if (e.renderMesh != null) DestroyImmediate(e.renderMesh);
            if (e.colliderMesh != null) DestroyImmediate(e.colliderMesh);
        }

        private struct ColliderCandidate
        {
            public ChunkEntry entry;
            public float distanceSqr;
        }
        private readonly List<ColliderCandidate> colliderCandidates = new List<ColliderCandidate>(64);
        private static readonly System.Comparison<ColliderCandidate> CompareColliderDistance =
            (a, b) => a.distanceSqr.CompareTo(b.distanceSqr);

        private void UpdateColliders()
        {
            int2 vc = lastViewerColumn;
            int viewerChunkY = Mathf.FloorToInt(viewer.position.y / ChunkSpan);
            int radius = Mathf.Max(0, colliderDistance);
            int enableSqr = radius * radius;
            int disableSqr = (radius + 1) * (radius + 1);
            float span = ChunkSpan;
            Vector3 position = viewer.position;
            colliderCandidates.Clear();

            foreach (var kv in chunks)
            {
                int4 c = kv.Key;
                ChunkEntry e = kv.Value;
                int dx = c.x - vc.x, dz = c.z - vc.y, dy = c.y - viewerChunkY;
                int distanceSqr = dx * dx + dz * dz + dy * dy;
                // Hysteresis avoids removing and reinserting the same static shapes
                // whenever the viewer crosses a chunk boundary.
                if (e.colliderAssigned)
                {
                    if (c.w != 0 || distanceSqr > disableSqr)
                    {
                        e.meshCollider.enabled = false;
                        e.colliderAssigned = false;
                    }
                    continue;
                }
                if (c.w != 0 || distanceSqr > enableSqr || e.colliderMesh.vertexCount == 0) continue;
                // Upečení ještě běží: počkat, ať se cook nespustí znovu na hlavním vlákně.
                // Výjimka: chunk pod hráčem (níže) – ten se dokončí hned, propadnout se nesmí.
                Vector3 minimum = new Vector3(c.x, c.y, c.z) * span;
                Vector3 closest = Vector3.Max(minimum, Vector3.Min(position, minimum + Vector3.one * span));
                colliderCandidates.Add(new ColliderCandidate { entry = e, distanceSqr = (closest - position).sqrMagnitude });
            }
            colliderCandidates.Sort(CompareColliderDistance);
            long started = System.Diagnostics.Stopwatch.GetTimestamp();
            int assigned = 0;
            for (int i = 0; i < colliderCandidates.Count; i++)
            {
                ColliderCandidate candidate = colliderCandidates[i];
                // Ground and any chunk within 4 metres are never delayed by the budget.
                if (candidate.distanceSqr > 16f && (assigned >= 2 ||
                    (System.Diagnostics.Stopwatch.GetTimestamp() - started) * 1000.0
                        / System.Diagnostics.Stopwatch.Frequency >= 1.0)) break;
                ChunkEntry e = candidate.entry;
                if (e.baking || e.bakeWanted)
                {
                    // Ještě se peče / čeká ve frontě: počkat, ať se cook nespustí na hlavním
                    // vlákně. Chunk do 4 m od hráče se dokončí hned (propadnout se nesmí) –
                    // když čekal ve frontě, upeče ho Unity při přiřazení, jako dřív.
                    if (candidate.distanceSqr > 16f && (e.bakeWanted || !e.bake.IsCompleted)) continue;
                    e.CompleteBake();
                }
                AssignCollider(e.meshCollider, e.colliderMesh);
                e.colliderAssigned = true;
                assigned++;
            }
            UpdateFallbackCollider(vc);
        }

        // Oprava po review 14.10.2026 (test chůze při přepínání dosahu 10↔3): když pod hráčem
        // ještě není zveřejněný LOD0 sloupec (jeho stavba po zmenšení a zvětšení dosahu běží),
        // stojí tam jen hrubý terén bez collideru a hráč propadl světem. Do zveřejnění LOD0 proto
        // dostane collider hrubý sloupec přímo pod hráčem (z render meshe – ve světových
        // souřadnicích, collider není zapuštěný). Jakmile LOD0 stojí, nouzový se vypne.
        // Kolo 3 (review Codexu): položky se drží s generací chunku (poolované ChunkEntry se recyklují
        // a znovu používají) a s příznakem, zda se mesh v okamžiku cooku ještě morfoval.
        private struct FallbackRef { public ChunkEntry entry; public int generation; public bool morphing; }
        private readonly List<FallbackRef> fallbackColliders = new List<FallbackRef>(8);
        private int3 fallbackColumn = new int3(int.MinValue, int.MinValue, -1);
        private int fallbackKeyCount;
        public static int StatFallbackColliderFrames, StatFallbackRebuilt;

        /// <summary>
        /// Oprava po review 14.10.2026 (nalezeno testem chůze): přiřazení sharedMesh do VYPNUTÉHO
        /// MeshCollideru, který měl mesh null, Unity občas tiše zahodí – collider se pak zapne
        /// bez meshe a hráč propadne (regrese vypínání colliderů z předchozího kola). Nejdřív se
        /// proto zapne a teprve pak přiřadí; když getter i tak vrátí null, přiřadí se znovu.
        /// </summary>
        private static void AssignCollider(MeshCollider mc, Mesh mesh)
        {
            mc.enabled = true;
            if (mc.sharedMesh != mesh) mc.sharedMesh = mesh;
            if (mc.sharedMesh == null && mesh != null)
            {
                StatColliderReassign++;
                mc.sharedMesh = null;
                mc.sharedMesh = mesh;
            }
        }

        public static int StatColliderReassign;

        /// <summary>A/B (/perf bake on|off): pečení kolizních meshů v jobech.</summary>
        public static bool BakeInJobs = true;

        private void UpdateFallbackCollider(int2 vc)
        {
            int3 want = new int3(int.MinValue, int.MinValue, -1);
            if (!ColumnPublished(new int3(vc.x, vc.y, 0)) && builders != null)
            {
                Vector3 p = viewer.position;
                for (int l = 1; l < builders.Length; l++)
                {
                    float span = SpanOf(l);
                    var k = new int3(Mathf.FloorToInt(p.x / span), Mathf.FloorToInt(p.z / span), l);
                    if (ColumnPublished(k)) { want = k; break; }
                }
            }
            if (want.Equals(fallbackColumn))
            {
                if (want.z < 0) return;
                if (FallbackStillValid(want)) { StatFallbackColliderFrames++; return; }
                // Stejný klíč, ale sloupec se mezitím přestavěl/recykloval nebo dojel morph: obnovit.
                StatFallbackRebuilt++;
            }
            ReleaseFallback();
            fallbackColumn = want;
            fallbackKeyCount = 0;
            if (want.z < 0 || !colChunks.TryGetValue(want, out List<int4> keys)) return;
            fallbackKeyCount = keys.Count;
            for (int i = 0; i < keys.Count; i++)
            {
                if (!chunks.TryGetValue(keys[i], out ChunkEntry c) || c.renderMesh == null || c.renderMesh.vertexCount == 0) continue;
                EnsureMeshNotRead(c.renderMesh);
                AssignCollider(c.meshCollider, c.renderMesh);   // cook na hlavním vlákně – vzácná nouze
                fallbackColliders.Add(new FallbackRef { entry = c, generation = c.generation, morphing = c.morph != null });
            }
            StatFallbackColliderFrames++;
        }

        /// <summary>
        /// Platí nouzové collidery pořád pro aktuální chunky sloupce? Neplatí, když se některý chunk
        /// recykloval (jiná generace), přestal mít náš mesh, sloupec má jinou sadu chunků, nebo
        /// collider vznikl uprostřed morphu, který mezitím doběhl (mesh je teď na konečné výšce).
        /// </summary>
        private bool FallbackStillValid(int3 col)
        {
            if (!colChunks.TryGetValue(col, out List<int4> keys) || keys.Count != fallbackKeyCount) return false;
            for (int i = 0; i < fallbackColliders.Count; i++)
            {
                FallbackRef f = fallbackColliders[i];
                ChunkEntry c = f.entry;
                if (c.go == null || c.generation != f.generation) return false;
                if (!c.colliderAssigned && (!c.meshCollider.enabled || c.meshCollider.sharedMesh != c.renderMesh)) return false;
                if (f.morphing && c.morph == null) return false;
            }
            return true;
        }

        /// <summary>Vypne jen VLASTNÍ nouzové collidery – recyklovaný a znovu použitý chunk (jiná generace) nechá být.</summary>
        private void ReleaseFallback()
        {
            for (int i = 0; i < fallbackColliders.Count; i++)
            {
                FallbackRef f = fallbackColliders[i];
                ChunkEntry c = f.entry;
                if (c.go == null || c.generation != f.generation || c.colliderAssigned) continue;
                c.meshCollider.enabled = false;
                c.meshCollider.sharedMesh = null;
            }
            fallbackColliders.Clear();
        }

        /// <summary>
        /// Kolik chunků stojí a kolik z nich má přiřazený collider.
        ///
        /// <para>Tohle rozliší tři úplně různé poruchy, které se navenek chovají stejně
        /// (paprsek nic netrefí): nula chunků = streamer nic nepostavil, chunky bez LOD0 =
        /// stojí jen hrubé prstence a ty collider z principu nedostávají, LOD0 bez colliderů
        /// = rozbité přiřazování.</para>
        /// </summary>
        public void ColliderStats(out int total, out int lod0, out int withCollider)
        {
            total = 0; lod0 = 0; withCollider = 0;

            foreach (var kv in chunks)
            {
                total++;
                if (kv.Key.w != 0) continue;

                lod0++;
                if (kv.Value.colliderAssigned) withCollider++;
            }
        }

        // ── dotazy na terén ────────────────────────────────────────────

        /// <summary>
        /// Výška povrchu v daném bodě, analyticky – bez colliderů a bez čekání na chunky.
        ///
        /// <para>Tohle je jediná spolehlivá cesta, jak zjistit, kde je zem, ještě než se
        /// terén vůbec vygeneruje. Raycast to neumí: collider se přiřazuje až chunkům
        /// blízko vieweru, takže dotaz z výšky 500 m nemá do čeho trefit.</para>
        ///
        /// <para>Nezná 3D tvarosloví – převisy, jeskyně ani skalní brány. Vrací výšku
        /// hlavního povrchu, což je přesně to, co spawn a osazování potřebují.</para>
        /// </summary>
        public float SurfaceHeight(float x, float z)
            => builder == null ? 0f : SurfaceHeightFrom(builder, x, z);

        /// <summary>
        /// Bez hotového regionu vrací terén BEZ řek – schválně.
        ///
        /// <para>Tahle cesta se volá i ve spirálách o statisících vzorků (hledání souše při
        /// spawnu, hledání biomu z konzole). Dostavovat kvůli každému vzorku region by
        /// znamenalo vteřinové záseky. Rozdíl je nanejvýš hloubka koryta, což pro spawn
        /// ani pro osazení nic neznamená.</para>
        /// </summary>
        private float SurfaceHeightFrom(VoxelChunkBuilder b, float x, float z)
        {
            var p = new float2(x, z);
            return WorldGen.SampleSurfaceY(p, b.Params, b.Splines, HydroRef(p));
        }

        /// <summary>Pohled na hydrologický region daného bodu. Neplatný, když ještě nestojí.</summary>
        private HydroRegionRef HydroRef(float2 world)
        {
            if (hydro == null) return default;
            HydroRegionRef r = hydro.GetRef(world);
            r.legacy = LegacySampler ? 1 : 0;
            return r;
        }

        // ── kolo 17: hydrologie pro hledání daleko od hráče ──────────────
        //
        // Proč: streamer drží jen regiony kolem hráče (3×3 po 8 km, sousedé až 1,5 km před
        // hranou). RiverClearance mimo ně vracel „žádná řeka", takže /biome tp kapradiny
        // (rokle = svahy nad řekou) viděl rokli jen tam, kde zrovna stála hydrologie, a výsledek
        // závisel na výchozí pozici a stavu streamingu. Hledání si proto chybějící regiony
        // postaví samo (synchronně, odpojeně od streameru) a po hledání je uvolní.

        private readonly Dictionary<int2, HydroRegion> searchHydro = new Dictionary<int2, HydroRegion>();
        private int searchHydroFrame;

        /// <summary>Jen pro A/B měření (/biome hydro off): hledání vidí jen regiony streameru jako před kolem 17.</summary>
        public static bool SearchHydroEnabled = true;

        /// <summary>Kolik regionů si hledání dostavělo od posledního uvolnění (pro diagnostiku).</summary>
        public int SearchHydroBuilt { get; private set; }

        /// <summary>Čas stavby těch regionů v ms (pro diagnostiku).</summary>
        public float SearchHydroMs { get; private set; }

        /// <summary>Strop odpojených regionů (~1,1 MB uloženého pole na region). 64 = čtverec 8 × 8 regionů ≈ okruh 30 km (vzdálené /biome tp).</summary>
        private const int SearchHydroMax = 64;

        /// <summary>Hydrologický region bodu: od streameru, a když tam není, odpojený (dostaví se).</summary>
        public HydroRegionRef SearchHydroRef(float2 world)
        {
            HydroRegionRef r = HydroRef(world);
            if (r.IsValid || hydro == null || !SearchHydroEnabled) return r;
            searchHydroFrame = Time.frameCount;
            int2 c = HydroWorld.RegionOf(world);
            if (!searchHydro.TryGetValue(c, out HydroRegion reg))
            {
                if (searchHydro.Count >= SearchHydroMax) ReleaseSearchHydro();
                var sw = System.Diagnostics.Stopwatch.StartNew();
                reg = hydro.BuildDetached(c);
                SearchHydroMs += (float)sw.Elapsed.TotalMilliseconds;
                SearchHydroBuilt++;
                searchHydro[c] = reg;
            }
            r = HydroMap.RefOf(reg);
            r.legacy = LegacySampler ? 1 : 0;
            return r;
        }

        /// <summary>Uvolní regiony dostavěné pro hledání. Konzole volá po každém hledání/teleportu.</summary>
        public void ReleaseSearchHydro()
        {
            foreach (var kv in searchHydro) kv.Value.Dispose();
            searchHydro.Clear();
        }

        /// <summary>Vynuluje počítadla <see cref="SearchHydroBuilt"/> a <see cref="SearchHydroMs"/>.</summary>
        public void ResetSearchHydroStats() { SearchHydroBuilt = 0; SearchHydroMs = 0f; }

        /// <summary>
        /// Jen pro měření: přepne výběr koryta na chování před 26.9.2026, aby šlo změřit,
        /// kolik stojí nový sampler. Mění tvar terénu, takže to není herní volba.
        /// </summary>
        public static bool LegacySampler;

        /// <summary>
        /// Je streamer v klidu – všechno žádané stojí, nic se nestaví? Pro měření doby
        /// dostavění světa po skoku (<c>/teren cekej</c>).
        /// </summary>
        public bool IsSettled
        {
            get
            {
                if (inFlight.Count > 0) return false;
                if (seamQueue.Count > 0) return false;   // kolo 24: přestavby švu taky
                foreach (int3 k in desired)
                    if (!columns.TryGetValue(k, out ColumnEntry e) || !e.published || e.seamRebuild) return false;
                return true;
            }
        }

        /// <summary>
        /// Kontrola děr a překryvů (výkonové kolo 14.10.2026): mřížka bodů kolem hráče, pro každý
        /// se spočítá, kolik úrovní LOD má v tom místě zveřejněný sloupec. 0 = díra (nic se tam
        /// nekreslí), 2+ = překryv dvou úrovní (z-fighting). Body mimo žádaný dohled se nepočítají.
        /// </summary>
        public void CoverageProbe(float radius, float step, out int samples, out int holes, out int overlaps)
            => CoverageProbe(radius, step, out samples, out holes, out overlaps, null);

        public void CoverageProbe(float radius, float step, out int samples, out int holes, out int overlaps,
                                  System.Text.StringBuilder firstHole)
        {
            samples = holes = overlaps = 0;
            if (viewer == null || builders == null) return;
            int levels = builders.Length;
            Vector3 p = viewer.position;
            int n = Mathf.CeilToInt(radius / step);
            for (int iz = -n; iz <= n; iz++)
            for (int ix = -n; ix <= n; ix++)
            {
                float x = p.x + ix * step, z = p.z + iz * step;
                int cover = 0, sunkCover = 0;
                bool wanted = false;
                for (int l = 0; l < levels; l++)
                {
                    float span = SpanOf(l);
                    var key = new int3(Mathf.FloorToInt(x / span), Mathf.FloorToInt(z / span), l);
                    if (desired.Contains(key)) wanted = true;
                    if (columns.TryGetValue(key, out ColumnEntry e) && e.published && !e.replacedHidden && !MaskedAt(key, e, x, z)) { if (e.sunk) sunkCover++; else cover++; }
                }
                if (!wanted) continue;
                samples++;
                if (cover == 0 && sunkCover > 0) cover = 1;   // kryje jen zapuštěný odcházející sloupec – díra to není
                bool dumpHole = cover == 0 && firstHole != null && !firstHoleDumped;
                bool dumpOverlap = cover > 1 && firstHole != null && !firstOverlapDumped;
                if (dumpHole || dumpOverlap)
                {
                    if (dumpHole) firstHoleDumped = true; else firstOverlapDumped = true;
                    firstHole.Append(" || ");
                    firstHole.AppendFormat(System.Globalization.CultureInfo.InvariantCulture, (cover == 0 ? "díra" : "překryv") + " ({0:0},{1:0}) {2:0} m od hráče:", x, z,
                        Mathf.Sqrt((x - p.x) * (x - p.x) + (z - p.z) * (z - p.z)));
                    for (int l = 0; l < levels; l++)
                    {
                        float span = SpanOf(l);
                        var key = new int3(Mathf.FloorToInt(x / span), Mathf.FloorToInt(z / span), l);
                        firstHole.Append(" L").Append(l).Append(desired.Contains(key) ? "*" : "");
                        if (columns.TryGetValue(key, out ColumnEntry ce))
                            firstHole.AppendFormat(System.Globalization.CultureInfo.InvariantCulture,
                                "[r{0} p{1} ret{2} reb{3} pend{4} st{5}/{6} seam{7} age{8:0.0}s unl{9:0.0} repl{10}]",
                                ce.ready ? 1 : 0, ce.published ? 1 : 0, ce.retired ? 1 : 0, ce.rebuilding ? 1 : 0, ce.pending,
                                ce.staged.Count + ce.stagedEmpty.Count, ce.surfaceChunks != null ? ce.surfaceChunks.Count : -1,
                                ce.seamRebuild ? 1 : 0, Time.realtimeSinceStartup - ce.createdAt,
                                ce.unloadAt > 0f ? ce.unloadAt - Time.unscaledTime : -1f, ReplacementReady(key) ? 1 : 0);
                        else firstHole.Append("[-]");
                    }
                }
                if (cover == 0) holes++;
                else if (cover > 1) overlaps++;
            }
        }

        private int lastBlockedSiblings, lastBlockedSeam, lastBlockedMorph, lastBlockedAtomic;
        private System.Comparison<int3> publishGroupCompare;

        /// <summary>Skupina zveřejnění = nejhrubší sloupec, pod kterým sloupec leží.</summary>
        private int3 PublishGroup(int3 k)
        {
            int top = (builders != null ? builders.Length : 1) - 1;
            int sh = top - k.z;
            return new int3(k.x >> sh, k.y >> sh, top);
        }

        private int ComparePublishGroup(int3 a, int3 b)
        {
            int3 ga = PublishGroup(a), gb = PublishGroup(b);
            if (!ga.Equals(gb))
            {
                int d = ColumnDist(ga).CompareTo(ColumnDist(gb));   // bližší skupina dřív
                if (d != 0) return d;
            }
            if (ga.x != gb.x) return ga.x.CompareTo(gb.x);
            if (ga.y != gb.y) return ga.y.CompareTo(gb.y);
            return ColumnDist(a).CompareTo(ColumnDist(b));
        }

        /// <summary>Diagnostika /perf: první překryv v běhu už je vypsaný.</summary>
        [System.NonSerialized] public bool firstOverlapDumped, firstHoleDumped;   // jen diagnostika – nepatří do scény

        /// <summary>
        /// Diagnostika (oprava po review): kolik chunků s ZAPNUTÝM colliderem má kořen mimo
        /// počátek (fyzika posunutá) a kolik má zapuštěný jen vzhled.
        /// </summary>
        public void ColliderSinkProbe(out int movedColliders, out int sunkVisuals)
        {
            movedColliders = sunkVisuals = 0;
            foreach (var kv in chunks)
            {
                ChunkEntry c = kv.Value;
                if (c.go == null) continue;
                if (c.meshCollider.enabled && c.go.transform.localPosition != Vector3.zero) movedColliders++;
                if (c.renderTf.localPosition.y != 0f) sunkVisuals++;
            }
        }

        /// <summary>
        /// Diagnostika předání stromů: zveřejněné LOD0 sloupce s rozpracovaným osazením, které
        /// nekryjí žádní zástupci (vlastní, rodiče ani prarodiče) – tam by stromy chyběly.
        /// </summary>
        public static System.Text.StringBuilder TreeGapDump;

        public int TreeGapProbe(out int inHandover)
        {
            inHandover = 0;
            int gaps = 0;
            foreach (var kv in columns)
            {
                ColumnEntry e = kv.Value;
                if (e.lod != 0 || !e.published || !PropsActive || PropsFinished(e)) continue;
                inHandover++;
                // Záznam zástupců (i prázdný = v té ploše žádné stromy nejsou) znamená krytí.
                bool own = e.proxyRequested && !e.proxyDropped
                           && Orivilon.World.Spawning.TreeProxies.Has(Orivilon.World.Spawning.TreeProxies.Key(e.coord.x, e.coord.y, 0));
                bool parent = Orivilon.World.Spawning.TreeProxies.Has(Orivilon.World.Spawning.TreeProxies.Key(e.coord.x >> 1, e.coord.y >> 1, 1))
                              || Orivilon.World.Spawning.TreeProxies.Has(Orivilon.World.Spawning.TreeProxies.Key(e.coord.x >> 2, e.coord.y >> 2, 2));
                if (!own && !parent)
                {
                    gaps++;   // nedoinstancované stromy by tu chyběly
                    if (TreeGapDump != null && TreeGapDump.Length == 0)
                        TreeGapDump.AppendFormat(System.Globalization.CultureInfo.InvariantCulture,
                            "LOD0 ({0},{1}) {2:0} m: proxyReq {3} dropped {4} propsReq {5} build {6} spawner {7} morph {8} field {9} retired {10} wants {11} | rodič L1 {12}, L2 {13}",
                            e.coord.x, e.coord.y, Mathf.Sqrt(ColumnDist(kv.Key)), e.proxyRequested, e.proxyDropped, e.propsRequested,
                            e.propsBuild != null, e.spawner != null, Morphing(e), e.field.IsCreated, e.retired, WantsTreeProxies(e),
                            columns.TryGetValue(new int3(e.coord.x >> 1, e.coord.y >> 1, 1), out ColumnEntry p1) ? (p1.published ? "zveř." : "nezveř.") + (p1.proxyRequested ? "+zást." : "") : "-",
                            columns.TryGetValue(new int3(e.coord.x >> 2, e.coord.y >> 2, 2), out ColumnEntry p2) ? (p2.published ? "zveř." : "nezveř.") + (p2.proxyRequested ? "+zást." : "") : "-");
                }
            }
            return gaps;
        }

        /// <summary>Diagnostika propadu: stav LOD0 sloupce a jeho chunků/colliderů pod bodem.</summary>
        public string DescribeGroundAt(Vector3 p)
        {
            var sb = new System.Text.StringBuilder(256);
            int cx = Mathf.FloorToInt(p.x / ChunkSpan), cz = Mathf.FloorToInt(p.z / ChunkSpan);
            for (int l = 0; l < (builders != null ? builders.Length : 1); l++)
            {
                float span = SpanOf(l);
                var key = new int3(Mathf.FloorToInt(p.x / span), Mathf.FloorToInt(p.z / span), l);
                sb.Append(" L").Append(l).Append(desired.Contains(key) ? "*" : "");
                if (columns.TryGetValue(key, out ColumnEntry e))
                    sb.AppendFormat(System.Globalization.CultureInfo.InvariantCulture, "[r{0} p{1} reb{2} seam{3} pend{4} sunk{5} unl{6:0.0}]",
                        e.ready ? 1 : 0, e.published ? 1 : 0, e.rebuilding ? 1 : 0, e.seamRebuild ? 1 : 0, e.pending, e.sunk ? 1 : 0,
                        e.unloadAt > 0 ? e.unloadAt - Time.unscaledTime : -1f);
                else sb.Append("[-]");
            }
            if (colChunks.TryGetValue(new int3(cx, cz, 0), out List<int4> l0))
                foreach (int4 k in l0)
                    if (chunks.TryGetValue(k, out ChunkEntry c))
                        sb.AppendFormat(System.Globalization.CultureInfo.InvariantCulture, " ch y{0}: aktivní {1} coll {2}/{3} mesh {4} bake {5}/{6}",
                            k.y, c.go.activeSelf, c.colliderAssigned, c.meshCollider.enabled, c.meshCollider.sharedMesh != null, c.bakeWanted, c.baking);
            return sb.ToString();
        }

        /// <summary>Diagnostika chybějících stromů: stav sloupců, osazení a zástupců v bodě.</summary>
        public string DescribePropsAt(Vector3 p)
        {
            var sb = new System.Text.StringBuilder(256);
            for (int l = 0; l < (builders != null ? builders.Length : 1); l++)
            {
                float span = SpanOf(l);
                var key = new int3(Mathf.FloorToInt(p.x / span), Mathf.FloorToInt(p.z / span), l);
                long pk = Orivilon.World.Spawning.TreeProxies.Key(key.x, key.y, l);
                sb.Append(" L").Append(l).Append(desired.Contains(key) ? "*" : "");
                if (columns.TryGetValue(key, out ColumnEntry e))
                    sb.AppendFormat(System.Globalization.CultureInfo.InvariantCulture,
                        "[p{0} ret{1} sunk{2} propsReq{3} build{4} sp{5} fin{6} proxReq{7} drop{8} prox{9}]",
                        e.published ? 1 : 0, e.retired ? 1 : 0, e.sunk ? 1 : 0, e.propsRequested ? 1 : 0, e.propsBuild != null ? 1 : 0,
                        e.spawner != null ? (e.spawner.SpawnInProgress ? "busy" : "ok") : "-", PropsFinished(e) ? 1 : 0,
                        e.proxyRequested ? 1 : 0, e.proxyDropped ? 1 : 0, Orivilon.World.Spawning.TreeProxies.CountOf(pk));
                else sb.Append("[-] prox").Append(Orivilon.World.Spawning.TreeProxies.CountOf(pk));
            }
            var hits = new Collider[256];
            int n = Physics.OverlapSphereNonAlloc(p, 20f, hits, ~0, QueryTriggerInteraction.Ignore);
            var roots = new HashSet<Transform>();
            for (int i = 0; i < n; i++)
            {
                LODGroup g = hits[i].GetComponentInParent<LODGroup>();
                if (g != null) roots.Add(g.transform);
            }
            sb.Append(" | LODGroup objektů v 20 m: ").Append(roots.Count);
            return sb.ToString();
        }

        /// <summary>Stav fronty streameru pro /perf: co drží nedostavěné sloupce.</summary>
        public string StreamingStats()
        {
            int live = 0, computing = 0, waiting = 0, noProps = 0, unpublished = 0;
            foreach (var kv in columns)
            {
                ColumnEntry e = kv.Value;
                if (!e.retired && !e.seamRebuild) live++;
                if (!e.ready) computing++;
                else if (IsColumnComplete(e)) waiting++;
                else if (!e.published) unpublished++;
                if (e.lod == 0 && e.published && !e.retired && !e.propsRequested && !BeyondPropsCap(e.coord, 0)) noProps++;
            }
            return string.Format(System.Globalization.CultureInfo.InvariantCulture,
                "sloupců {0}, živých {1} (limit {2}), počítá pole {3}, staví chunky {4}, hotové čekají na zveřejnění {5} (sourozenci {8}, šev {9}, přestavby švu {10}, rozpočet morphu {11}, atomická výměna {12}), LOD0 čeká na osazení {6}, chunků rozpracováno {7}",
                columns.Count, live, maxLiveColumns, computing, unpublished, waiting, noProps, inFlight.Count,
                lastBlockedSiblings, lastBlockedSeam, TerrainSeamPending(), lastBlockedMorph, lastBlockedAtomic);
        }

        /// <summary>Kolik žádaných sloupců ještě není zveřejněných (měření /perf – drží streamer krok?).</summary>
        public int StreamingBacklog()
        {
            int n = 0;
            foreach (int3 k in desired)
                if (!columns.TryGetValue(k, out ColumnEntry e) || !e.published) n++;
            return n;
        }

        /// <summary>
        /// Změří čistou cenu sloupcového pole (tam běží HydroSampler) a stavby hladiny na
        /// <paramref name="count"/> LOD0 sloupcích kolem bodu – nový sampler proti starému,
        /// na týchž sloupcích a v témže běhu. Synchronně na hlavním vlákně (joby se pustí
        /// a hned dokončí), takže čísla jsou srovnatelná mezi sebou, ne absolutní.
        /// </summary>
        public string BenchColumns(Vector3 around, int count)
        {
            if (builders == null) return "streamer neběží";
            VoxelChunkBuilder b = builders[0];
            int2 c0 = (int2)math.floor(new float2(around.x, around.z) / b.ChunkSpan);
            var coords = new List<int2>();
            for (int r = 0; coords.Count < count; r++)
                for (int dz = -r; dz <= r && coords.Count < count; dz++)
                for (int dx = -r; dx <= r && coords.Count < count; dx++)
                    if (math.max(math.abs(dx), math.abs(dz)) == r) coords.Add(c0 + new int2(dx, dz));

            // 0 = zahřátí (Burst, cache), 1 = bez hydrologie (spodní mez), 2 = starý sampler 5×5,
            // 3 = nový sampler 9×9, 4 = nový bez dorovnání LOD švu. Každý průchod 3×, bere se medián.
            string[] names = { "", "bez hydrologie", "původní sampler 5×5", "AKTUÁLNÍ (9×9 + šev)", "aktuální bez LOD švu" };
            var mesh = new Mesh();
            var sb = new System.Text.StringBuilder();
            bool keep = LegacySampler;
            try
            {
                for (int pass = 0; pass < 5; pass++)
                {
                    LegacySampler = pass == 2;
                    VoxelChunkBuilder.BenchNoSeam = pass == 4;
                    var fieldRuns = new List<double>();
                    var waterRuns = new List<double>();
                    var batchRuns = new List<double>();
                    for (int rep = 0; rep < (pass == 0 ? 1 : 3); rep++)
                    {
                        double tField = 0, tWater = 0;
                        var sw = new System.Diagnostics.Stopwatch();
                        foreach (int2 c in coords)
                        {
                            HydroRegionRef h = HydroRef(b.ColumnCenter(c));
                            if (pass == 1) h.valid = 0; // pole zůstanou přiřazená, sampler vrátí „nic"
                            NativeArray<float> probe = b.AllocProbe(Allocator.TempJob);
                            sw.Restart();
                            ColumnField f = b.BuildColumn(c, Allocator.TempJob, probe, h);
                            tField += sw.Elapsed.TotalMilliseconds;
                            sw.Restart();
                            WaterSurface.Build(f, b.ColumnPad, b.voxelSize, b.Params.seaLevel,
                                               waterRiverMin, waterMinDepth, mesh);
                            tWater += sw.Elapsed.TotalMilliseconds;
                            f.Dispose();
                            probe.Dispose();
                        }
                        fieldRuns.Add(tField / coords.Count);
                        waterRuns.Add(tWater / coords.Count);

                        // Propustnost: všechny sloupce naráz, jako když streamer dohání skok.
                        var fields = new List<ColumnField>();
                        var probes = new List<NativeArray<float>>();
                        var handles = new NativeArray<JobHandle>(coords.Count, Allocator.Temp);
                        sw.Restart();
                        for (int k = 0; k < coords.Count; k++)
                        {
                            HydroRegionRef h = HydroRef(b.ColumnCenter(coords[k]));
                            if (pass == 1) h.valid = 0;
                            NativeArray<float> probe = b.AllocProbe(Allocator.TempJob);
                            handles[k] = b.ScheduleColumn(coords[k], Allocator.TempJob, probe, h, out ColumnField f);
                            fields.Add(f); probes.Add(probe);
                        }
                        JobHandle.CompleteAll(handles);
                        batchRuns.Add(sw.Elapsed.TotalMilliseconds);
                        handles.Dispose();
                        foreach (ColumnField f in fields) f.Dispose();
                        foreach (NativeArray<float> pr in probes) pr.Dispose();
                    }
                    if (pass == 0) continue;
                    fieldRuns.Sort(); waterRuns.Sort(); batchRuns.Sort();
                    sb.AppendFormat(System.Globalization.CultureInfo.InvariantCulture,
                        "{0}: pole {1:0.000} ms/sloupec (min {2:0.000}, max {3:0.000}), dávka {5:0.0} ms, hladina {4:0.000} ms/sloupec | ",
                        names[pass], fieldRuns[1], fieldRuns[0], fieldRuns[2], waterRuns[1], batchRuns[1]);
                }
                sb.AppendFormat("{0} sloupců LOD0, medián ze 3 běhů", coords.Count);
            }
            finally
            {
                LegacySampler = keep;
                VoxelChunkBuilder.BenchNoSeam = false;
                Destroy(mesh);
            }
            return sb.ToString();
        }

        /// <summary>
        /// Najde nejbližší kus souše, na kterém se dá stát.
        ///
        /// <para>Počátek světa je bod jako každý jiný – že zrovna tam vyšlo moře, je náhoda
        /// seedu, ne chyba. Spawn se proto nesmí ptát „jak vysoko je terén v nule", ale
        /// „kde je nejbližší souš". Analytický dotaz na povrch je levný, takže se dá projít
        /// spirála o stovkách vzorků dřív, než se vygeneruje jediný chunk.</para>
        ///
        /// <para>Kromě výšky nad hladinou se kontroluje i rovinatost: bod uprostřed útesu
        /// je sice nad mořem, ale hráč po něm sjede.</para>
        /// </summary>
        /// <param name="minAboveSea">O kolik metrů musí být terén nad hladinou.</param>
        public bool TryFindLandSpawn(Vector3 near, out Vector3 point,
                                     float minAboveSea = 4f, float maxRadius = 4000f)
        {
            point = near;
            if (builder == null) return false;

            float sea = builder.Params.seaLevel;
            const float step = 40f;

            for (float r = 0f; r <= maxRadius; r += step)
            {
                // Úhlový krok se s poloměrem zmenšuje, aby hustota vzorků zůstala stejná.
                float da = r < step ? 7f : step / r;

                for (float a = 0f; a < 6.2831853f; a += da)
                {
                    float x = near.x + r * Mathf.Cos(a);
                    float z = near.z + r * Mathf.Sin(a);

                    float y = SurfaceHeight(x, z);
                    if (y < sea + minAboveSea) continue;

                    // Rovinatost: čtyři sousedi na čtyřech metrech nesmí být o moc jinde.
                    float e = SurfaceHeight(x + 4f, z), w = SurfaceHeight(x - 4f, z);
                    float n = SurfaceHeight(x, z + 4f), s2 = SurfaceHeight(x, z - 4f);

                    float lo = Mathf.Min(Mathf.Min(e, w), Mathf.Min(n, s2));
                    float hi = Mathf.Max(Mathf.Max(e, w), Mathf.Max(n, s2));
                    if (hi - lo > 5f || lo < sea + 1f) continue;

                    point = new Vector3(x, y, z);
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Klima v daném bodě – tytéž hodnoty, ze kterých se barví terén a vybírá biom.
        /// Levné: dva fbm, žádný sloupec.
        /// </summary>
        public void ClimateAt(float x, float z, out float temperature, out float humidity)
        {
            temperature = humidity = 0.5f;
            if (builder == null) return;

            GenParams gp = builder.Params;
            var p = new float2(x, z);

            WorldGenMath.Climate(p, gp, out temperature, out humidity);
        }

        /// <summary>Hladina moře podle nastavení generátoru.</summary>
        public float SeaLevel => builder != null ? builder.Params.seaLevel : 0f;

        /// <summary>Seed světa, jak ho streamer skutečně používá (ne pole v Inspectoru).</summary>
        public int WorldSeed => ResolveSeed();

        /// <summary>Barevná škála, ze které se odvozují i názvy biomů.</summary>
        public TerrainPalette Palette => palette;

        /// <summary>
        /// Biom v daném bodě. Levné – dvě fbm na klima a analytický dotaz na povrch,
        /// žádný sloupec a žádné čekání na chunky, takže se s tím dá prohledávat svět.
        /// </summary>
        public VoxelBiome BiomeAt(float x, float z, out float surfaceY)
        {
            surfaceY = SurfaceHeight(x, z);
            ClimateAt(x, z, out float t, out float h);
            // Kolo 20: sněžné štíty jen tam, kde paleta sníh opravdu kreslí (zvednutí čáry podle biomového klimatu).
            float lift = 0f;
            if (builder != null && palette.biomes > 0.5f && palette.snowLiftOff < 0.5f)
            {
                GenParams gp = builder.Params;
                lift = BiomeMath.SnowLift(BiomeMath.Climate(new float2(x, z), surfaceY, t, h, gp.seaLevel, gp.offMicro).t);
            }
            return VoxelBiomes.Classify(surfaceY, t, h, SeaLevel, palette, lift);
        }

        /// <summary>Kolo 20 (A/B, <c>/biome snih</c>): zvednutí sněžné čáry podle biomového klimatu. Platí pro nově stavěné chunky.</summary>
        public void SetSnowLift(bool on)
        {
            palette.snowLiftOff = on ? 0f : 1f;
            if (builders != null)
                for (int i = 0; i < builders.Length; i++)
                    if (builders[i] != null) builders[i].palette = palette;
        }

        public bool SnowLiftOn => palette.snowLiftOff < 0.5f;

        /// <summary>
        /// Mikro-biom v bodě (zlatý háj, čedičové pole) a jeho síla 0–1.
        ///
        /// <para>Stejně levné jako <see cref="BiomeAt"/> – devět hashů, žádný sloupec –
        /// takže se s tím dá prohledávat svět po spirále stejně jako u biomů.</para>
        /// </summary>
        public MicroBiome MicroAt(float x, float z, out float weight)
        {
            weight = 0f;
            if (builder == null) return MicroBiome.None;

            // Nejdřív levná část: devět hashů. Výška terénu se sahá AŽ POTOM a jen u háje,
            // protože SurfaceHeight je celé analytické vyhodnocení povrchu včetně řek –
            // tisíckrát dražší než hash. Spirálové hledání dělá desítky tisíc vzorků, takže
            // kdyby se výška počítala vždycky, trvalo by minuty místo vteřin. Obsazená je
            // ale jen každá třicátá buňka, takže se drahá cesta projde zlomkem vzorků.
            MicroBiomeMath.Sample(new float2(x, z), builder.Params.Micro, out int k, out float w);

            if (k == (int)MicroBiome.GoldenGrove && w > 0.001f)
            {
                w *= MicroBiomeMath.LandFade(SurfaceHeight(x, z), SeaLevel);
                if (w < 0.001f) k = 0;
            }

            weight = w;
            return (MicroBiome)k;
        }

        /// <summary>
        /// Hladina vody nad daným bodem, pokud tam nějaká je.
        ///
        /// <para><b>Čte se z hotového sloupce, ne analyticky.</b> Který živel v buňce je —
        /// moře, jezero, nebo koryto — rozhodl sloupcový job a stavitel hladiny podle
        /// TÝCHŽ dat postavil mesh. Druhý, analytický výpočet by se s ním dřív nebo později
        /// rozešel o desítky centimetrů a hráč by se topil ve vzduchu kousek nad hladinou.</para>
        ///
        /// <para>Mimo načtené sloupce vrací false. Pod vodou se hráč nemůže ocitnout tam,
        /// kde ještě není terén – neměl by kudy doplavat.</para>
        /// </summary>
        /// <summary>
        /// Vzdálenost od břehu nejbližšího koryta (osa − polovina šířky) z hydrologie – pro
        /// osazení, které potřebuje vidět dál, než sahá okraj sloupcového pole. Deterministické
        /// (stejný zdroj jako tvar koryta). <paramref name="lakeDepth"/> &gt; 0 = bod leží v jezerní pánvi.
        /// Bez hotového regionu vrací velké číslo.
        /// </summary>
        public float RiverClearance(float x, float z, out float lakeDepth)
            => RiverClearanceFrom(HydroRef(new float2(x, z)), x, z, out lakeDepth);

        /// <summary>
        /// Kolo 17: <see cref="RiverClearance"/> pro hledání a teleport – hydrologie se v případě
        /// potřeby dostaví (<see cref="SearchHydroRef"/>), takže výsledek nezávisí na tom, kde hráč
        /// stojí a co streamer zrovna drží. Jen pro konzoli, ne pro osazování (zásek desítky ms na region).
        /// </summary>
        public float RiverClearanceSearch(float x, float z, out float lakeDepth)
            => RiverClearanceFrom(SearchHydroRef(new float2(x, z)), x, z, out lakeDepth);

        private float RiverClearanceFrom(HydroRegionRef h, float x, float z, out float lakeDepth)
        {
            lakeDepth = 0f;
            if (builder == null) return 1e4f;
            var p = new float2(x, z);
            if (!h.IsValid) return 1e4f;
            GenParams gp = builder.Params;
            HydroFlow f = HydroSampler.Nearest(p, h, gp.riverMinAccum, gp.riverWidthMin, gp.riverWidthMax, gp.riverAccumFull);
            lakeDepth = f.lakeDepth;
            if (!f.Found) return 1e4f;
            return f.distance - math.max(f.width * 0.5f, 1f);
        }

        public bool WaterLevelAt(float x, float z, out float level)
        {
            level = 0f;
            if (builder == null) return false;

            VoxelChunkBuilder b = builders[0];
            var p = new float2(x, z);
            int2 c = (int2)math.floor(p / b.ChunkSpan);

            if (!columns.TryGetValue(new int3(c.x, c.y, 0), out ColumnEntry e)) return false;
            if (e.waterLevels == null || !e.published) return false;

            // Mřížka je v souřadnicích hladiny, tedy rohu sloupce BEZ okraje pole.
            int dim = VoxelWorld.SampleDim;
            float2 corner = b.ColumnCorner(c);
            float2 local = (p - corner) / b.voxelSize;

            int ix = math.clamp((int)math.round(local.x), 0, dim - 1);
            int iz = math.clamp((int)math.round(local.y), 0, dim - 1);

            float lv = e.waterLevels[iz * dim + ix];
            if (lv <= WaterSurface.NoWater) return false;

            level = lv;
            return true;
        }

        // ── teleport ───────────────────────────────────────────────────

        /// <summary>Běží právě teleport? Konzole podle toho nepustí druhý.</summary>
        public bool IsTeleporting { get; private set; }

        /// <summary>
        /// Přesune hráče a PODRŽÍ ho tam, dokud pod ním nevznikne collider.
        ///
        /// <para>Tohle držení je celý smysl metody. Collider dostávají jen chunky
        /// v okruhu <see cref="colliderDistance"/> od vieweru a ty se po skoku teprve
        /// začnou stavět – hráč puštěný hned by propadl několik set metrů, než pod ním
        /// vznikne podlaha, a skončil by pod světem. Prosté nastavení pozice proto
        /// nestačí, i když v editoru na krátkou vzdálenost vypadá, že funguje.</para>
        /// </summary>
        /// <param name="target">Cíl. Y se přepíše výškou povrchu, pokud <paramref name="snapToSurface"/>.</param>
        public void Teleport(Vector3 target, bool snapToSurface = true)
        {
            if (viewer == null || IsTeleporting) return;

            Transform body = viewer.root != null ? viewer.root : viewer;
            StartCoroutine(TeleportRoutine(body, target, snapToSurface));
        }

        private IEnumerator TeleportRoutine(Transform body, Vector3 target, bool snapToSurface)
        {
            IsTeleporting = true;

            if (snapToSurface) target.y = SurfaceHeight(target.x, target.z) + 4f;

            Rigidbody rb = body.GetComponentInChildren<Rigidbody>();
            bool hadGravity = rb != null && rb.useGravity;

            if (rb != null)
            {
                rb.linearVelocity = Vector3.zero;
                rb.angularVelocity = Vector3.zero;
                rb.useGravity = false;
                rb.position = target;
            }
            body.position = target;

            // Prstence se musí přepočítat hned. Stráž v UpdateDesired porovnává sloupec
            // vieweru s posledním známým, a ten se po skoku o kilometry sice liší, ale
            // dokud se nezavolá LateUpdate, streamer o skoku neví – tohle mu to řekne.
            desiredDirty = true;

            float deadline = Time.unscaledTime + 15f;
            float tpStart = Time.unscaledTime;

            while (Time.unscaledTime < deadline)
            {
                if (rb != null)
                {
                    rb.linearVelocity = Vector3.zero;
                    rb.position = target;
                }
                body.position = target;

                if (Physics.Raycast(target + Vector3.up * 3f, Vector3.down,
                                    out RaycastHit hit, 60f, ~0, QueryTriggerInteraction.Ignore))
                {
                    target.y = hit.point.y + 1.2f;
                    if (rb != null) rb.position = target;
                    body.position = target;
                    break;
                }

                yield return new WaitForFixedUpdate();
            }

            // Kolo 22 diagnostika: dlouhé čekání na collider pod cílem (jen záznam).
            float waited = Time.unscaledTime - tpStart;
            if (waited > 0.3f && TeleportStalls.Length < 800)
            {
                var tc = new int3((int)math.floor(target.x / SpanOf(0)), (int)math.floor(target.z / SpanOf(0)), 0);
                columns.TryGetValue(tc, out ColumnEntry te);
                TeleportStalls.AppendFormat(System.Globalization.CultureInfo.InvariantCulture,
                    " čekal {0:0.00} s u ({1:0},{2:0},{3:0}) sloupec {4}: {5};", waited, target.x, target.y, target.z, tc.x + "," + tc.y,
                    te == null ? "neexistuje" : te.published ? "zveřejněn" : "rozestavěn");
            }

            if (rb != null)
            {
                rb.linearVelocity = Vector3.zero;
                rb.useGravity = hadGravity;
            }

            IsTeleporting = false;
        }

        /// <summary>
        /// Posune výškové prahy palety nahoru, aby odpovídaly světu s masivními pohořími.
        ///
        /// <para>Prahy palety jsou serializované ve scéně, takže je změna výchozích hodnot
        /// v kódu NEPŘEPÍŠE – Unity použije to, co má uložené. Tohle je proto jediná cesta,
        /// jak je hromadně přenastavit, aniž by se editovala scéna ručně.</para>
        ///
        /// <para>Projeví se na chuncích postavených od téhle chvíle; starým zůstane barva
        /// zapečená ve vertex colors, dokud se nepřestaví.</para>
        /// </summary>
        [ContextMenu("Paleta: vysokohorská")]
        public void ApplyAlpinePalette()
        {
            palette.grassTop = 120f;
            palette.rockTop = 240f;
            palette.snowTop = 300f;
            palette.snowTempShift = 110f;
            palette.iceRise = 150f;
            palette.EnsureClimate();

            if (builders != null)
                for (int i = 0; i < builders.Length; i++)
                    if (builders[i] != null) builders[i].palette = palette;

            Debug.Log("[VoxelTerrain] Paleta přenastavena na vysokohorskou " +
                      "(tráva do 120 m, skála do 240 m, sníh od 300 m, led od 450 m). " +
                      "Projeví se na nově stavěných chuncích.", this);
        }

        /// <summary>
        /// Přepíše paletu ve scéně celou výchozí paletou z kódu.
        ///
        /// <para><b>Proč to musí jít přes tlačítko.</b> Paleta je serializovaná ve scéně,
        /// takže změna výchozích hodnot v kódu se do běžícího světa nikdy nedostane – Unity
        /// použije to, co má uložené. Tohle je jediná cesta, jak je hromadně srovnat.</para>
        ///
        /// <para>Kopíruje se <b>celá</b> paleta, tedy i barvy. Ruční doladění odstínů
        /// v Inspectoru se tím ztratí; prahy a barvy jsou ale provázané (sněžná čára řídí
        /// i klasifikaci biomů), takže míchat staré barvy s novými prahy je horší volba.</para>
        ///
        /// <para>Projeví se na chuncích postavených od téhle chvíle; starým zůstane barva
        /// zapečená ve vertex colors, dokud se nepřestaví.</para>
        /// </summary>
        [ContextMenu("Paleta: obnovit výchozí z kódu")]
        public void ApplyRichPalette()
        {
            palette = TerrainPalette.Default;
            palette.EnsureClimate();
            palette.art = WorldGenSettings.ArtPassEnabled ? 1f : 0f;
            palette.biomes = WorldGenSettings.BiomesEnabled ? 1f : 0f;
            palette.biomeSeed = ResolveSeed();

            if (builders != null)
                for (int i = 0; i < builders.Length; i++)
                    if (builders[i] != null) builders[i].palette = palette;

            Debug.Log($"[VoxelTerrain] Paleta obnovena z kódu: tráva do {palette.grassTop} m, " +
                      $"skála do {palette.rockTop} m, sníh od {palette.snowTop} m, " +
                      $"mikro-variace {palette.microStrength}, břehový písek {palette.shoreSandRise} m. " +
                      "Projeví se na nově stavěných chuncích.", this);
        }

        // ── hydrologie ─────────────────────────────────────────────────

        private HydroMap hydro;

        /// <summary>
        /// Regionální hydrologie. Zatím se z ní jen měří – do generování se zapojí
        /// v další fázi, aby šla stará údolní metoda a nová síť porovnat vedle sebe.
        /// </summary>
        /// <remarks>
        /// Schválně se nejmenuje <c>Hydro</c>: tak se jmenuje i jmenný prostor
        /// <c>Orivilon.World.Generation.Hydro</c>, který je odsud vidět, a překlep v přístupu
        /// by pak hlásil nesrozumitelnou chybu o jmenném prostoru místo o vlastnosti.
        /// </remarks>
        public HydroMap HydroLayer
        {
            get
            {
                if (hydro == null && builder != null)
                    hydro = new HydroMap(builder.Params, builder.Splines);
                return hydro;
            }
        }

        /// <summary>
        /// Postaví hydrologický region pod hráčem a vypíše, co z něj vyšlo.
        ///
        /// <para>Prahy říční sítě se bez tohohle ladí naslepo. Jediné číslo, které o síti
        /// něco vypovídá, je „kolik procent souše je koryto" – u skutečné krajiny to vychází
        /// kolem 1–3 %. Deset procent znamená bažinu, desetina procenta poušť.</para>
        /// </summary>
        [ContextMenu("Změřit hydrologii")]
        public void MeasureHydrology()
        {
            if (builder == null) { Debug.Log("[VoxelTerrain] Streamer ještě neběží."); return; }

            float2 p = viewer != null
                ? new float2(viewer.position.x, viewer.position.z)
                : new float2(0f);

            GenParams gp = builder.Params;
            Debug.Log(HydroLayer.Measure(HydroWorld.RegionOf(p), gp.riverMinAccum,
                                         gp.riverWidthMin, gp.riverWidthMax, gp.riverAccumFull), this);
        }

        // ── měření ─────────────────────────────────────────────────────

        /// <summary>
        /// Změří, co streamer právě drží. Volá se z kontextového menu komponenty za běhu.
        ///
        /// <para>Odhadovat rozpočet z počtu chunků je nespolehlivé – trojúhelníků na chunk
        /// je od 0 do několika tisíc podle toho, jak členitý je zrovna terén, a nativní paměť
        /// drží hlavně sloupcová pole, kterých je vidět nula. Tohle čte skutečné meshe
        /// a skutečné délky polí.</para>
        /// </summary>
        [ContextMenu("Změřit terén")]
        public void MeasureTerrain()
        {
            if (builders == null) { Debug.Log("[VoxelTerrain] Streamer ještě neběží."); return; }

            int levels = builders.Length;
            var colLive = new int[levels];
            var colRetired = new int[levels];
            var chunkCount = new int[levels];
            var triCount = new long[levels];
            long vertCount = 0, columnBytes = 0;

            foreach (var kv in columns)
            {
                ColumnEntry e = kv.Value;
                if (e.lod < 0 || e.lod >= levels) continue;

                if (e.retired) colRetired[e.lod]++;
                else { colLive[e.lod]++; columnBytes += e.field.ApproxBytes; }
            }

            foreach (var kv in chunks)
            {
                int l = kv.Key.w;
                if (l < 0 || l >= levels) continue;

                chunkCount[l]++;
                Mesh m = kv.Value.renderMesh;
                if (m == null) continue;

                triCount[l] += m.GetIndexCount(0) / 3;
                vertCount += m.vertexCount;
            }

            int waterMeshes = 0, propColumns = 0;
            foreach (var kv in columns)
            {
                if (kv.Value.waterGo != null && kv.Value.waterGo.activeSelf) waterMeshes++;
                if (kv.Value.propsGo != null) propColumns++;
            }

            // Pracovní sloty: pole hustoty (33³ float) a mapa hran (33³ × 3 int).
            int n = VoxelWorld.SampleDim * VoxelWorld.SampleDim * VoxelWorld.SampleDim;
            long scratchBytes = 0;
            for (int l = 0; l < levels; l++) scratchBytes += builders[l].ScratchSlots * (long)n * (4 + 12);

            long editBytes = edits != null ? edits.EditedChunks * (long)n : 0;

            var sb = new System.Text.StringBuilder();
            sb.AppendLine("[VoxelTerrain] Stav streameru");
            sb.AppendLine($"  viewer: {(viewer != null ? viewer.name : "CHYBÍ")} "
                        + $"{(viewer != null ? viewer.position.ToString("0.0") : "")}, "
                        + $"sloupec vieweru {lastViewerColumn.x}/{lastViewerColumn.y}, "
                        + $"žádané sloupce {desired.Count}, fronta {desiredSorted.Count}, "
                        + $"chunkRoot {(chunkRoot != null ? "ok" : "CHYBÍ")}, "
                        + $"enabled {enabled}, isActiveAndEnabled {isActiveAndEnabled}");

            long triTotal = 0, chunkTotal = 0;
            for (int l = 0; l < levels; l++)
            {
                triTotal += triCount[l];
                chunkTotal += chunkCount[l];
                float span = SpanOf(l);
                sb.AppendLine($"  LOD{l} ({span:0} m/chunk): {chunkCount[l],4} chunků, "
                            + $"{triCount[l],7} trojúhelníků, sloupce {colLive[l]} živých / {colRetired[l]} uvolněných");
            }

            sb.AppendLine($"  celkem {chunkTotal} chunků, {triTotal} trojúhelníků, {vertCount} vrcholů");
            sb.AppendLine($"  voda {waterMeshes} hladin, osazení {propColumns} sloupců, rozpracováno {inFlight.Count} chunků");
            sb.AppendLine($"  nativní paměť: sloupce {columnBytes / 1048576f:0.0} MB, "
                        + $"pracovní sloty {scratchBytes / 1048576f:0.0} MB, "
                        + $"úpravy {editBytes / 1048576f:0.0} MB "
                        + $"({(edits != null ? edits.EditedChunks : 0)} chunků)");
            sb.AppendLine($"  dohled {ViewDistance:0} m");
            sb.Append(hydro != null
                ? $"  hydrologie: {hydro.ReadyCount} regionů hotových, {hydro.BuildingCount} se staví"
                : "  hydrologie: neběží");

            Debug.Log(sb.ToString());
        }

        // ── audit vody ─────────────────────────────────────────────────

        /// <summary>
        /// Podklady pro <see cref="WaterAudit"/>: hladiny LOD0 sloupců, terénní collidery
        /// a sloupce, ve kterých collidery opravdu jsou. Audit sám nic z interního stavu
        /// streameru nemění, jen čte.
        /// </summary>
        public void CollectAuditData(List<Mesh> waterMeshes, HashSet<Collider> terrainColliders,
                                     HashSet<int2> colliderColumns)
        {
            foreach (var kv in columns)
            {
                ColumnEntry e = kv.Value;
                if (e.lod != 0 || !e.published) continue;
                if (e.waterGo != null && e.waterGo.activeSelf && e.waterMesh != null)
                    waterMeshes.Add(e.waterMesh);
            }

            // Collidery se běžně přiřazují podle 3D vzdálenosti, takže chunk o patro níž
            // (mořské dno, koryto pod hráčem) ho mít nemusí a paprsek by hlásil díru, která
            // není. Audit si je proto u všech zveřejněných LOD0 chunků v dosahu vynutí;
            // UpdateColliders ty vzdálené v dalším snímku zase sundá.
            float span = Lod0Span;
            float reach = auditRadius + span;
            foreach (var kv in chunks)
            {
                ChunkEntry c = kv.Value;
                if (c.lod != 0 || c.meshCollider == null) continue;
                if (c.go == null || !c.go.activeInHierarchy) continue;

                float cx = (c.coord.x + 0.5f) * span - auditCenter.x;
                float cz = (c.coord.z + 0.5f) * span - auditCenter.z;
                if (cx * cx + cz * cz > reach * reach && !c.colliderAssigned) continue;

                if (!c.colliderAssigned)
                {
                    c.CompleteBake();
                    c.meshCollider.sharedMesh = c.colliderMesh.vertexCount > 0 ? c.colliderMesh : null;
                    c.meshCollider.enabled = c.meshCollider.sharedMesh != null;   // od kola 14.10. jsou collidery mimo dosah vypnuté
                    c.colliderAssigned = true;
                }
                terrainColliders.Add(c.meshCollider);
                colliderColumns.Add(new int2(c.coord.x, c.coord.z));
            }
            Physics.SyncTransforms();
        }

        /// <summary>Kde a jak daleko má <see cref="CollectAuditData"/> vynutit collidery.</summary>
        public void SetAuditArea(Vector3 center, float radius)
        {
            auditCenter = center;
            auditRadius = radius;
        }

        private Vector3 auditCenter;
        private float auditRadius;

        /// <summary>
        /// Které vodní meshe (a z jakého LOD a stavu sloupce) leží nad daným bodem.
        /// Dvě hladiny nad jedním místem = dvojitá průhledná vrstva a schody na okrajích.
        /// </summary>
        public string WaterLayersAt(Vector3 p)
        {
            var sb = new System.Text.StringBuilder();
            int n = 0;
            foreach (var kv in columns)
            {
                ColumnEntry e = kv.Value;
                float span = SpanOf(e.lod);
                if (p.x < e.coord.x * span || p.x >= (e.coord.x + 1) * span
                    || p.z < e.coord.y * span || p.z >= (e.coord.y + 1) * span) continue;
                bool act = e.waterGo != null && e.waterGo.activeSelf;
                n++;
                int tris = e.waterMesh != null ? (int)(e.waterMesh.GetIndexCount(0) / 3) : -1;
                sb.Append($" [L{e.lod} {e.coord.x},{e.coord.y} pub={(e.published ? 1 : 0)} des={(desired.Contains(kv.Key) ? 1 : 0)} " +
                          $"has={(e.hasWater ? 1 : 0)} act={(act ? 1 : 0)} tris={tris}]");
            }
            return n + " columns here:" + sb;
        }

        /// <summary>
        /// Všechno, co generátor v bodě ví o vodě – bodovou cestou, stejnou matematikou jako
        /// sloupcový job. Diagnostika pro konzoli (<c>/voda bod</c>).
        /// </summary>
        /// <summary>
        /// Kolo 12 diagnostika: vrstvy výšky v bodě – makro (spliny), povrch bez terasování,
        /// hotový povrch a síla terasování. Hledá, ve které vrstvě vznikají stupně.
        /// </summary>
        public bool ProbeLayers(float x, float z, out float macro, out float noTerrace, out float surf, out float cliffW)
        {
            macro = noTerrace = surf = cliffW = 0f;
            if (builder == null) return false;
            GenParams gp = builder.Params;
            var p = new float2(x, z);
            HydroRegionRef h = HydroRef(p);
            WorldGenMath.EvalMacro(p, gp, builder.Splines, out float my, out float sh, out float c, out float e, out float v);
            float slope = WorldGenMath.MacroSlopeAt(p, math.max(1f, gp.lakeCell * 0.01f), gp, builder.Splines);
            float basin = 0f, waterY = ColumnField.NoLake;
            int2 cc = (int2)math.floor(p / gp.lakeCell);
            for (int oz = -1; oz <= 1; oz++)
            for (int ox = -1; ox <= 1; ox++)
            {
                LakeBody lb = WorldGenMath.EvalLakeCell(cc + new int2(ox, oz), gp, builder.Splines);
                float b = WorldGenMath.LakeBasin(p, lb, gp);
                if (b > basin) { basin = b; waterY = lb.waterY; }
            }
            HydroFlow f = HydroSampler.Nearest(p, h, gp.riverMinAccum, gp.riverWidthMin, gp.riverWidthMax, gp.riverAccumFull);
            WorldGenMath.Climate(p, gp, out float temp, out float hum);
            surf = WorldGenMath.EvalSurface(p, gp, my, sh, c, e, v, slope, temp, hum, 1f, f, waterY, basin,
                                            out _, out _, out cliffW, out _, CraterMath.ForPoint(p, gp, builder.Splines));
            GenParams g2 = gp; g2.cliffStrength = 0f;
            noTerrace = WorldGenMath.EvalSurface(p, g2, my, sh, c, e, v, slope, temp, hum, 1f, f, waterY, basin,
                                                 out _, out _, out _, out _, CraterMath.ForPoint(p, gp, builder.Splines));
            macro = my;
            return true;
        }

        public string ProbeWater(float x, float z, float sampleStep = 1f)
        {
            if (builder == null) return "streamer neběží";
            GenParams gp = builder.Params;
            var p = new float2(x, z);
            HydroRegionRef h = HydroRef(p);

            WorldGenMath.EvalMacro(p, gp, builder.Splines, out float my, out float sh,
                                   out float c, out float e, out float v);
            float slope = WorldGenMath.MacroSlopeAt(p, math.max(1f, gp.lakeCell * 0.01f), gp, builder.Splines);
            float basin = 0f, waterY = ColumnField.NoLake;
            int2 cc = (int2)math.floor(p / gp.lakeCell);
            for (int oz = -1; oz <= 1; oz++)
            for (int ox = -1; ox <= 1; ox++)
            {
                LakeBody lb = WorldGenMath.EvalLakeCell(cc + new int2(ox, oz), gp, builder.Splines);
                float b = WorldGenMath.LakeBasin(p, lb, gp);
                if (b > basin) { basin = b; waterY = lb.waterY; }
            }
            HydroFlow f = HydroSampler.Nearest(p, h, gp.riverMinAccum, gp.riverWidthMin,
                                               gp.riverWidthMax, gp.riverAccumFull);
            WorldGenMath.Climate(p, gp, out float temp, out float hum);
            float y = WorldGenMath.EvalSurface(p, gp, my, sh, c, e, v, slope, temp, hum, sampleStep,
                                               f, waterY, basin, out float rc, out float rY,
                                               out float cl, out float hl, CraterMath.ForPoint(p, gp, builder.Splines));
            return string.Format(System.Globalization.CultureInfo.InvariantCulture,
                "surf {0:0.00} macro {1:0.00} | river: found {2} dist {3:0.0} w {4:0.0} waterY {5:0.00} core {6:0.00} riverY {7:0.00} | " +
                "hydro lake: depth {8:0.00} level {9:0.00} bed {10:0.00} -> lakeY {11:0.00} | worley basin {12:0.00} wY {13:0.00} | cliff {14:0.00} region {15}",
                y, my, f.Found ? 1 : 0, f.distance, f.width, f.waterY, rc, rY,
                f.lakeDepth, f.lakeLevel, f.lakeBed, hl, basin, waterY, cl, h.IsValid ? "ok" : "none");
        }

        /// <summary>
        /// Kolo 7: jen pro A/B měření – zapne/vypne práh u hydrologického schodu ve všech
        /// builderech. Platí pro sloupce postavené potom (odskok a návrat přestaví okolí).
        /// </summary>
        /// <summary>
        /// Kolo 11: jen pro A/B – plynulý směr toku u soutoků. Platí pro sloupce postavené potom
        /// (odskok a návrat přestaví okolí); diagnostika <see cref="FlowAt"/> platí hned.
        /// </summary>
        public void SetFlowBlend(bool on)
        {
            WorldGenSettings.FlowBlendEnabled = on;
            if (builders != null) foreach (var b in builders) b?.SetFlowBlend(on);
            builder?.SetFlowBlend(on);
        }

        public bool FlowBlendOn => builder != null && builder.Params.flowBlend > 0.5f;

        /// <summary>
        /// Kolo 12: jen pro A/B – měkké stupně slabého terasování. Mění povrch, takže platí pro
        /// sloupce postavené potom (odskok a návrat přestaví okolí); ProbeLayers/ProbeSurface hned.
        /// </summary>
        public void SetSoftTerrace(bool on)
        {
            WorldGenSettings.SoftTerraceEnabled = on;
            if (builders != null) foreach (var b in builders) b?.SetSoftTerrace(on);
            builder?.SetSoftTerrace(on);
        }

        public bool SoftTerraceOn => builder != null && builder.Params.softTerrace > 0.5f;

        /// <summary>Kolo 12b diagnostika: násobiče povrchových deformací (viz VoxelChunkBuilder.SetDetailDiag).</summary>
        public void SetDetailDiag(float ridge, float grain, float overhang, float squish, float cave = 1f, float terrace = 1f)
        {
            if (builders != null) foreach (var b in builders) b?.SetDetailDiag(ridge, grain, overhang, squish, cave, terrace);
            if (builder != null && (builders == null || System.Array.IndexOf(builders, builder) < 0))
                builder.SetDetailDiag(ridge, grain, overhang, squish, cave, terrace);
        }

        /// <summary>Kolo 12b: klidný povrchový 3D detail zap/vyp (+ volitelné ladění) pro nově stavěné chunky.</summary>
        public void SetCalmDetail(bool on, float yStretch = -1f, int ovOct = -1, int sqOct = -1, int mask = -1)
        {
            WorldGenSettings.CalmDetailEnabled = on;
            if (builders != null) foreach (var b in builders) b?.SetCalmDetail(on, yStretch, ovOct, sqOct, mask);
            if (builder != null && (builders == null || System.Array.IndexOf(builders, builder) < 0))
                builder.SetCalmDetail(on, yStretch, ovOct, sqOct, mask);
        }

        public bool CalmDetailOn => builder != null && builder.Params.calmDetail > 0.5f;

        /// <summary>
        /// Kolo 11 diagnostika: soutoky v okruhu – buňky makro mapy, do kterých tečou aspoň dva
        /// toky nad prahem řeky. Vrací střed buňky (x, z) a úhel mezi přítoky (y).
        /// </summary>
        /// <summary>Diagnostika FindConfluences: x = buněk řeky v okruhu, y = z nich s přítokem nad prahem.</summary>
        public static Vector2Int ConfluenceStats;

        public void FindConfluences(Vector3 center, float radius, List<Vector3> into)
        {
            ConfluenceStats = Vector2Int.zero;
            if (builder == null) return;
            var p = new float2(center.x, center.z);
            HydroRegionRef r = HydroRef(p);
            if (!r.IsValid) return;
            float cell = HydroWorld.CellSize;
            float minAcc = builder.Params.riverMinAccum;
            int2 c = (int2)math.floor((p - r.origin) / cell);
            int rc = (int)math.ceil(radius / cell);
            for (int y = c.y - rc; y <= c.y + rc; y++)
            for (int x = c.x - rc; x <= c.x + rc; x++)
            {
                if (x < 1 || y < 1 || x >= r.side - 1 || y >= r.side - 1) continue;
                int n = 0; float2 d0 = default, d1 = default;
                for (int k = 0; k < 8; k++)
                {
                    int2 o = HydroWorld.DirOffset(k);
                    int ni = (y - o.y) * r.side + (x - o.x);      // soused, který by tekl do (x, y)
                    if (r.dir[ni] != k || r.accum[ni] < minAcc) continue;
                    if (n == 0) d0 = math.normalize((float2)o); else d1 = math.normalize((float2)o);
                    n++;
                }
                if (r.accum[y * r.side + x] >= minAcc) { ConfluenceStats.x++; if (n >= 1) ConfluenceStats.y++; }
                if (n < 2) continue;
                float2 w = r.origin + (new float2(x, y) + 0.5f) * cell;
                if (math.distance(w, p) > radius) continue;
                into.Add(new Vector3(w.x, math.degrees(math.acos(math.clamp(math.dot(d0, d1), -1f, 1f))), w.y));
            }
        }

        /// <summary>Kolo 11 diagnostika: kandidátní úsečky toku kolem bodu (viz HydroSampler.Describe).</summary>
        public string FlowDescribe(float x, float z, float reach)
        {
            if (builder == null) return "bez builderu";
            var p = new float2(x, z);
            HydroRegionRef h = HydroRef(p);
            return HydroSampler.Describe(p, h, builder.Params.riverMinAccum, reach);
        }

        public void SetHydroSill(bool on)
        {
            WorldGenSettings.HydroSillEnabled = on;
            WaterSurface.StepAwareCut = on;
            if (builders != null) foreach (var b in builders) b?.SetHydroSill(on);
            builder?.SetHydroSill(on);
        }

        /// <summary>
        /// Kolo 8: materiál, kterým se kreslí chunky. S uměleckou paletou je to běhová kopie
        /// <see cref="terrainMaterial"/> (asset se nemění) s kovovostí 0,3 → 0,04: kovová
        /// složka při nulové hladkosti tlumila difúzní barvu o 30 % a přimíchávala odraz
        /// oblohy, takže každý biom vybledl do stejné béžové.
        /// </summary>
        private Material RenderMaterial
        {
            get
            {
                if (terrainMaterial == null || !WorldGenSettings.ArtPassEnabled) return terrainMaterial;
                if (artMaterial == null)
                {
                    artMaterial = new Material(terrainMaterial) { name = terrainMaterial.name + " (art)" };
                    if (artMaterial.HasProperty("_Metallic")) artMaterial.SetFloat("_Metallic", ArtMetallic);
                }
                return artMaterial;
            }
        }
        private Material artMaterial;

        /// <summary>
        /// Kolo 8: běhová kopie <see cref="waterMaterial"/> s pěnou do světle akvamarínové
        /// místo čistě bílé a o něco užším pásem (1,2 → 0,9 m). Pěna u břehu zůstává čitelná,
        /// ale mělčiny na prazích a pod vodopády už nesvítí jako bílé plošky.
        /// </summary>
        private Material RenderWaterMaterial
        {
            get
            {
                if (waterMaterial == null || !WorldGenSettings.ArtPassEnabled) return waterMaterial;
                if (artWaterMaterial == null)
                {
                    artWaterMaterial = new Material(waterMaterial) { name = waterMaterial.name + " (art)" };
                    if (artWaterMaterial.HasProperty("_FoamColor")) artWaterMaterial.SetColor("_FoamColor", ArtFoamColor);
                    if (artWaterMaterial.HasProperty("_FoamDistance")) artWaterMaterial.SetFloat("_FoamDistance", ArtFoamDistance);
                    ApplyWaterSpec();
                }
                return artWaterMaterial;
            }
        }
        private Material artWaterMaterial;
        public static Color ArtFoamColor = new Color(0.78f, 0.90f, 0.96f, 1f);
        public static float ArtFoamDistance = 0.9f;

        /// <summary>
        /// Kolo 9: odlesk slunce na hladině. Původně SpecPower 200 / síla 0,6: na flat-shaded
        /// fasetách se tím celý trojúhelník buď rozsvítí do bíla, nebo nic – bílé „kostičky“.
        /// Širší a slabší lalok rozdělí odlesk do přechodu přes víc faset.
        /// </summary>
        public static float Art9SpecPower = 120f, Art9SpecStrength = 0.20f;

        /// <summary>
        /// Kolo 10: světlo vegetace (UNP/Vegetation). Listy a tráva ve stínu dřív dostaly jen
        /// ambient (SH ~0,1–0,3 proti slunci 3,0) a vyšly černé; rub a bok karet na siluetě koruny
        /// k tomu měl normálu od slunce. Globální parametry shaderu, platí i pro instancovanou
        /// trávu a vzdálené proxy stromů. Vypnuto = nuly = původní shader.
        /// </summary>
        public static bool FoliageLook = true;
        public static float FolNormalUpLeaves = 0.3f, FolNormalUpGrass = 0.25f,
                            FolShadowFloor = 0.3f, FolAmbient = 0.25f,
                            FolShadowFloorGrass = 0.12f, FolAmbientGrass = 0.08f;

        public static void ApplyFoliageLook(bool on)
        {
            FoliageLook = on;
            Shader.SetGlobalFloat("_FolNormalUpLeaves", on ? FolNormalUpLeaves : 0f);
            Shader.SetGlobalFloat("_FolNormalUpGrass", on ? FolNormalUpGrass : 0f);
            Shader.SetGlobalFloat("_FolShadowFloor", on ? FolShadowFloor : 0f);
            Shader.SetGlobalFloat("_FolAmbient", on ? FolAmbient : 0f);
            Shader.SetGlobalFloat("_FolShadowFloorGrass", on ? FolShadowFloorGrass : 0f);
            Shader.SetGlobalFloat("_FolAmbientGrass", on ? FolAmbientGrass : 0f);
            Shader.SetGlobalFloat("_FolDiag", 0f);
        }

        /// <summary>Nastaví odlesk na běhové kopii vodního materiálu podle Art9Enabled.</summary>
        public void ApplyWaterSpec()
        {
            if (artWaterMaterial == null || waterMaterial == null) return;
            bool on = WorldGenSettings.Art9Enabled;
            if (artWaterMaterial.HasProperty("_SpecPower"))
                artWaterMaterial.SetFloat("_SpecPower", on ? Art9SpecPower : waterMaterial.GetFloat("_SpecPower"));
            if (artWaterMaterial.HasProperty("_SpecStrength"))
                artWaterMaterial.SetFloat("_SpecStrength", on ? Art9SpecStrength : waterMaterial.GetFloat("_SpecStrength"));
        }
        public static float ArtMetallic = 0.04f;

        /// <summary>Kolo 8: přepne uměleckou paletu biomů. Projeví se na nově stavěných chuncích.</summary>
        public void SetArtPass(bool on)
        {
            WorldGenSettings.ArtPassEnabled = on;
            palette.art = on ? 1f : 0f;
            if (builders != null)
                for (int i = 0; i < builders.Length; i++)
                    if (builders[i] != null) builders[i].palette = palette;
        }

        public bool ArtPassOn => palette.art > 0.5f;

        /// <summary>Kolo 14: zapne/vypne klimatické regiony v barvě terénu (nově stavěné chunky) i v osazení.</summary>
        public void SetBiomes(bool on)
        {
            WorldGenSettings.BiomesEnabled = on;
            palette.biomes = on ? 1f : 0f;
            palette.biomeSeed = ResolveSeed();
            if (builders != null)
                for (int i = 0; i < builders.Length; i++)
                    if (builders[i] != null) builders[i].palette = palette;
        }

        /// <summary>
        /// Kolo 14: váhy regionů v bodě – analyticky (bez sloupce, bez hydrologie), pro hledání,
        /// mapu a konzoli. Osazení a barva počítají totéž ze sloupcového pole.
        /// </summary>
        public RegionWeights RegionAt(float x, float z, out float surf, out float cliff) => RegionAt(x, z, true, out surf, out cliff);

        /// <param name="water">false = bez dotazu do hydrologie (rychlé, pro mapu a hledání).</param>
        public RegionWeights RegionAt(float x, float z, bool water, out float surf, out float cliff)
            => RegionAt(x, z, water, out surf, out cliff, out _);

        /// <param name="clim">Kolo 14b: makro klima téhož bodu (/biome kde, /biome klima).</param>
        public RegionWeights RegionAt(float x, float z, bool water, out float surf, out float cliff, out ClimateSample clim)
        {
            surf = 0f; cliff = 0f; clim = default;
            if (builder == null) return default;
            GenParams gp = builder.Params;
            var p = new float2(x, z);
            WorldGenMath.EvalMacro(p, gp, builder.Splines, out float my, out float sh, out float c, out float e, out float v);
            float slope = WorldGenMath.MacroSlopeAt(p, math.max(1f, gp.lakeCell * 0.01f), gp, builder.Splines);
            WorldGenMath.Climate(p, gp, out float temp, out float hum);
            HydroFlow none = HydroFlow.None;
            CraterCell crater29 = CraterMath.ForPoint(p, gp, builder.Splines);   // kolo 29
            surf = WorldGenMath.EvalSurface(p, gp, my, sh, c, e, v, slope, temp, hum, 4f, none, ColumnField.NoLake, 0f,
                                            out _, out _, out cliff, out _, crater29);
            float rc = 1e4f, lakeDepth = 0f;
            // Kolo 17: hledání regionů musí vidět hydrologii všude, ne jen v 3×3 regionech kolem hráče.
            if (water) rc = RiverClearanceSearch(x, z, out lakeDepth);
            bool wet = rc < 30f || lakeDepth > 0.5f;
            float hW = wet ? math.max(0f, rc) * 0.25f + 0.5f : 1e4f;
            clim = BiomeMath.Climate(p, surf, temp, hum, gp.seaLevel, gp.offMicro);
            return BiomeMath.Weights(p, surf, temp, hum, cliff, hW, wet, gp.seaLevel, gp.offMicro, crater29);
        }

        /// <summary>Kolo 14b: váhy s danou výškou nad blízkou hladinou (z publikovaných sloupců, /biome tp mokrad).</summary>
        public RegionWeights RegionWithShore(float x, float z, float hW, out float surf)
        {
            surf = 0f;
            if (builder == null) return default;
            GenParams gp = builder.Params;
            var p = new float2(x, z);
            WorldGenMath.EvalMacro(p, gp, builder.Splines, out float my, out float sh, out float c, out float e, out float v);
            float slope = WorldGenMath.MacroSlopeAt(p, math.max(1f, gp.lakeCell * 0.01f), gp, builder.Splines);
            WorldGenMath.Climate(p, gp, out float temp, out float hum);
            HydroFlow none = HydroFlow.None;
            CraterCell crater29 = CraterMath.ForPoint(p, gp, builder.Splines);   // kolo 29
            surf = WorldGenMath.EvalSurface(p, gp, my, sh, c, e, v, slope, temp, hum, 4f, none, ColumnField.NoLake, 0f,
                                            out _, out _, out float cliff, out _, crater29);
            return BiomeMath.Weights(p, surf, temp, hum, cliff, hW, true, gp.seaLevel, gp.offMicro, crater29);
        }

        /// <summary>Kolo 29: meteorický kráter zasahující do obdélníku (osazení sloupce, konzole). valid = 0 → žádný.</summary>
        public CraterCell CraterFor(float2 mn, float2 mx) => builder != null ? CraterMath.ForBox(mn, mx, builder.Params, builder.Splines) : default;

        /// <summary>Kolo 14b: offset šumů regionů (gp.offMicro).</summary>
        public float3 MicroOffset => builder != null ? builder.Params.offMicro : default;

        /// <summary>Kolo 14b: makro klima v bodě (stejný vzorek, z něhož počítají váhy regionů).</summary>
        public ClimateSample ClimateSampleAt(float x, float z, out float rawTemp, out float rawHum)
        {
            rawTemp = rawHum = 0f;
            if (builder == null) return default;
            GenParams gp = builder.Params;
            var p = new float2(x, z);
            RegionAt(x, z, false, out _, out _, out ClimateSample c);
            WorldGenMath.Climate(p, gp, out rawTemp, out rawHum);
            return c;
        }

        /// <summary>Kolo 12 diagnostika: režim barvení terénu (viz TerrainPalette.diag). Platí pro nově stavěné chunky.</summary>
        public void SetPaletteDiag(float mode)
        {
            palette.diag = mode;
            if (builders != null)
                for (int i = 0; i < builders.Length; i++)
                    if (builders[i] != null) builders[i].palette = palette;
        }

        public float PaletteDiag => palette.diag;

        public bool HydroSillOn => builder != null && builder.Params.hydroSill > 0.5f;

        /// <summary>Kolo 10 diagnostika: hydrologický směr a spád toku v bodě (jako do sloupcového pole).</summary>
        public bool FlowAt(float x, float z, out Vector2 dir, out float slope)
        {
            dir = Vector2.zero; slope = 0f;
            if (builder == null) return false;
            GenParams gp = builder.Params;
            var p = new float2(x, z);
            HydroRegionRef h = HydroRef(p);
            if (!h.IsValid) return false;
            HydroFlow f = HydroSampler.Nearest(p, h, gp.riverMinAccum, gp.riverWidthMin, gp.riverWidthMax, gp.riverAccumFull, gp.flowBlend);
            if (!f.Found) return false;
            dir = new Vector2(f.dir.x, f.dir.y); slope = gp.flowBlend > 0.5f ? f.flowSlope : f.waterSlope;
            return true;
        }

        /// <summary>Kolo 6 diagnostika: povrch a řeka v bodě bodovou cestou (jako ProbeWater, jen čísla).</summary>
        public bool ProbeSurface(float x, float z, float sampleStep, out float surf, out float riverCoreV, out float riverYV, out float lakeYV)
        {
            surf = riverCoreV = riverYV = 0f; lakeYV = ColumnField.NoLake;
            if (builder == null) return false;
            GenParams gp = builder.Params;
            var p = new float2(x, z);
            HydroRegionRef h = HydroRef(p);
            WorldGenMath.EvalMacro(p, gp, builder.Splines, out float my, out float sh, out float c, out float e, out float v);
            float slope = WorldGenMath.MacroSlopeAt(p, math.max(1f, gp.lakeCell * 0.01f), gp, builder.Splines);
            float basin = 0f, waterY = ColumnField.NoLake;
            int2 cc = (int2)math.floor(p / gp.lakeCell);
            for (int oz = -1; oz <= 1; oz++)
            for (int ox = -1; ox <= 1; ox++)
            {
                LakeBody lb = WorldGenMath.EvalLakeCell(cc + new int2(ox, oz), gp, builder.Splines);
                float b = WorldGenMath.LakeBasin(p, lb, gp);
                if (b > basin) { basin = b; waterY = lb.waterY; }
            }
            HydroFlow f = HydroSampler.Nearest(p, h, gp.riverMinAccum, gp.riverWidthMin, gp.riverWidthMax, gp.riverAccumFull);
            WorldGenMath.Climate(p, gp, out float temp, out float hum);
            surf = WorldGenMath.EvalSurface(p, gp, my, sh, c, e, v, slope, temp, hum, sampleStep,
                                            f, waterY, basin, out riverCoreV, out riverYV, out float cl, out float hl, CraterMath.ForPoint(p, gp, builder.Splines));
            lakeYV = hl;
            return true;
        }

        /// <summary>Viditelné chunky nad bodem (LOD, souřadnice) – k hledání překryvů a švů.</summary>
        public string ChunksAt(float x, float z)
        {
            var sb = new System.Text.StringBuilder();
            int n = 0;
            foreach (var kv in chunks)
            {
                ChunkEntry c = kv.Value;
                if (c.go == null || !c.go.activeInHierarchy || c.renderMesh == null) continue;
                Bounds b = c.renderMesh.bounds;
                if (x < b.min.x || x > b.max.x || z < b.min.z || z > b.max.z) continue;
                n++;
                sb.Append($" [L{c.lod} {c.coord.x},{c.coord.y},{c.coord.z} y{b.min.y:0}..{b.max.y:0}]");
            }
            return n + " chunks:" + sb;
        }

        /// <summary>Jeden zveřejněný sloupec pro audit LOD hranic.</summary>
        public struct LodColumnInfo
        {
            public int lod;
            public int2 coord;
            public float span;
            public Mesh water;
            public List<Mesh> terrain;
        }

        /// <summary>Zveřejněné sloupce v okruhu – jejich hladiny a meshe chunků (pro WaterAudit.RunLod).</summary>
        public void CollectLodData(Vector3 center, float radius, List<LodColumnInfo> list)
        {
            var byKey = new Dictionary<int3, LodColumnInfo>();
            foreach (var kv in columns)
            {
                ColumnEntry e = kv.Value;
                if (!e.published) continue;
                float span = SpanOf(e.lod);
                float cx = (e.coord.x + 0.5f) * span - center.x, cz = (e.coord.y + 0.5f) * span - center.z;
                if (math.sqrt(cx * cx + cz * cz) > radius + span) continue;
                byKey[kv.Key] = new LodColumnInfo
                {
                    lod = e.lod, coord = e.coord, span = span,
                    water = e.waterGo != null && e.waterGo.activeSelf ? e.waterMesh : null,
                    terrain = new List<Mesh>(),
                };
            }
            foreach (var kv in chunks)
            {
                ChunkEntry c = kv.Value;
                if (c.go == null || !c.go.activeInHierarchy || c.renderMesh == null) continue;
                if (byKey.TryGetValue(new int3(c.coord.x, c.coord.z, c.lod), out LodColumnInfo info))
                    info.terrain.Add(c.renderMesh);
            }
            list.AddRange(byKey.Values);
        }

        /// <summary>
        /// Kolo 15 diagnostika: pro bod porovná zveřejněnou hladinu každého LOD sloupce s hladinou,
        /// kterou by sloupec dostal, kdyby se postavil teď (stejný builder, aktuální hydrologie).
        /// Rozdíl = sloupec stojí na starších vstupech.
        /// </summary>
        public string WaterStaleProbe(float x, float z)
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendFormat(System.Globalization.CultureInfo.InvariantCulture, "[Voda stale] ({0:0.0},{1:0.0}) t={2:0.0}s:", x, z, Time.time);
            if (builders == null) return sb.Append(" bez builderu").ToString();
            for (int lod = 0; lod < builders.Length; lod++)
            {
                VoxelChunkBuilder b = builders[lod];
                float span = SpanOf(lod);
                var c = new int2((int)math.floor(x / span), (int)math.floor(z / span));
                if (!columns.TryGetValue(new int3(c.x, c.y, lod), out ColumnEntry e)) continue;
                float cur = SampleMesh(e.waterGo != null && e.waterGo.activeSelf ? e.waterMesh : null, x, z);
                NativeArray<float> probe = b.AllocProbe(Allocator.TempJob);
                ColumnField f = b.BuildColumn(c, Allocator.TempJob, probe, HydroRef(b.ColumnCenter(c)));
                var mesh = new Mesh();
                WaterSurface.Build(f, b.ColumnPad, b.voxelSize, b.Params.seaLevel, waterRiverMin, waterMinDepth, mesh);
                float fresh = SampleMesh(mesh, x, z);
                Destroy(mesh); f.Dispose(); probe.Dispose();
                sb.AppendFormat(System.Globalization.CultureInfo.InvariantCulture,
                    " | L{0} col({1},{2}) pub={3} postaveno {4:0.0}s: teď {5:0.00} / znovu {6:0.00}{7}",
                    lod, c.x, c.y, e.published, e.waterBuiltAt, cur, fresh,
                    float.IsNaN(cur) || float.IsNaN(fresh) ? "" : (math.abs(cur - fresh) > 0.05f ? " ZASTARALÉ" : " ok"));
            }
            return sb.ToString();
        }

        /// <summary>Odsazení vzorku od švu pro <see cref="WaterSeamProfile"/> (m).</summary>
        public static float WaterSeamProbeOffset = 0.02f;

        /// <summary>Kolo 15 diagnostika: hladina obou LOD čerstvě postavená podél svislé hrany x, z0..z1 po 1 m.</summary>
        public string WaterSeamProfile(float x, float z0, float z1, int lodA, int lodB)
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendFormat(System.Globalization.CultureInfo.InvariantCulture, "[Voda profil] x={0:0.0} z {1:0}..{2:0} L{3}|L{4}:", x, z0, z1, lodA, lodB);
            var meshes = new Mesh[2]; int[] lods = { lodA, lodB }; float[] xs = { x + WaterSeamProbeOffset, x - WaterSeamProbeOffset };
            for (int k = 0; k < 2; k++)
            {
                VoxelChunkBuilder b = builders[lods[k]];
                float span = SpanOf(lods[k]);
                var c = new int2((int)math.floor(xs[k] / span), (int)math.floor((z0 + z1) * 0.5f / span));
                NativeArray<float> probe = b.AllocProbe(Allocator.TempJob);
                ColumnField f = b.BuildColumn(c, Allocator.TempJob, probe, HydroRef(b.ColumnCenter(c)));
                meshes[k] = new Mesh();
                WaterSurface.Build(f, b.ColumnPad, b.voxelSize, b.Params.seaLevel, waterRiverMin, waterMinDepth, meshes[k]);
                f.Dispose(); probe.Dispose();
                sb.AppendFormat(" col{0}=({1},{2})", k, c.x, c.y);
            }
            for (float z = z0; z <= z1 + 1e-3f; z += 1f)
            {
                float a = SampleMesh(meshes[0], xs[0], z), bb = SampleMesh(meshes[1], xs[1], z);
                sb.AppendFormat(System.Globalization.CultureInfo.InvariantCulture, " | {0:0}:{1:0.00}/{2:0.00} g{3:0.0}", z, a, bb, SurfaceHeight(x, z));
            }
            Destroy(meshes[0]); Destroy(meshes[1]);
            return sb.ToString();
        }

        private static float SampleMesh(Mesh m, float x, float z)
        {
            if (m == null) return float.NaN;
            var v = new List<Vector3>(); var tri = new List<int>();
            m.GetVertices(v); m.GetTriangles(tri, 0);
            float best = float.NaN;
            for (int k = 0; k < tri.Count; k += 3)
            {
                Vector3 a = v[tri[k]], bb = v[tri[k + 1]], cc = v[tri[k + 2]];
                float det = (bb.z - cc.z) * (a.x - cc.x) + (cc.x - bb.x) * (a.z - cc.z);
                if (math.abs(det) < 1e-9f) continue;
                float l1 = ((bb.z - cc.z) * (x - cc.x) + (cc.x - bb.x) * (z - cc.z)) / det;
                float l2 = ((cc.z - a.z) * (x - cc.x) + (a.x - cc.x) * (z - cc.z)) / det;
                float l3 = 1f - l1 - l2;
                if (l1 < -1e-4f || l2 < -1e-4f || l3 < -1e-4f) continue;
                float y = l1 * a.y + l2 * bb.y + l3 * cc.y;
                if (float.IsNaN(best) || y > best) best = y;
            }
            return best;
        }

        /// <summary>Znovu postaví pole a hladinu jednoho sloupce a vypíše statistiku – diagnostika.</summary>
        public string ProbeColumnWater(int lod, int cx, int cz)
        {
            if (builders == null || lod < 0 || lod >= builders.Length) return "bad lod";
            VoxelChunkBuilder b = builders[lod];
            var c = new int2(cx, cz);
            NativeArray<float> probe = b.AllocProbe(Allocator.TempJob);
            ColumnField f = b.BuildColumn(c, Allocator.TempJob, probe, HydroRef(b.ColumnCenter(c)));
            var mesh = new Mesh();
            bool has = WaterSurface.Build(f, b.ColumnPad, b.voxelSize, b.Params.seaLevel,
                                          waterRiverMin, waterMinDepth, mesh);
            WaterSurface.Stats st = WaterSurface.LastStats;
            int pad = b.ColumnPad;
            float g0 = f.surfY[pad * f.side + pad], g1 = f.surfY[(pad + 32) * f.side + pad + 32];
            var col = new System.Text.StringBuilder();
            for (int zi = 0; zi <= 32; zi += 4)
                col.Append($"{f.surfY[(pad + zi) * f.side + pad + 32]:0} ");
            string r = $"L{lod} {cx},{cz}: has={has} tris={mesh.GetIndexCount(0) / 3} cells={st.waterCells} sea={st.sea} lake={st.lake} river={st.river} " +
                       $"pad={pad} side={f.side} origin={f.origin} step={f.step} ground00={g0:0.0} ground11={g1:0.0} " +
                       $"bounds={mesh.bounds.min:0}..{mesh.bounds.max:0} eastEdge z0..32/4: {col}";
            Destroy(mesh);
            f.Dispose();
            probe.Dispose();
            return r;
        }

        /// <summary>
        /// Diagnostika švu: v bodě (x, z) postaví sloupce dvou LOD a vypíše výšku pole
        /// s šitím i bez něj, hladinu řeky a bránu 3D vrstvy. Když se na přímce švu
        /// liší už výškové pole, je vada v generování; když ne, je v 3D vrstvě / MC.
        /// </summary>
        public string SeamProbe(float x, float z, int lodA, int lodB)
        {
            if (builders == null) return "streamer neběží";
            var sb = new System.Text.StringBuilder();
            sb.AppendFormat(System.Globalization.CultureInfo.InvariantCulture, "({0:0.0}, {1:0.0})", x, z);
            foreach (int lod in new[] { lodA, lodB })
            {
                if (lod < 0 || lod >= builders.Length) continue;
                VoxelChunkBuilder b = builders[lod];
                var c = (int2)math.floor(new float2(x, z) / b.ChunkSpan);
                for (int pass = 0; pass < 2; pass++)
                {
                    VoxelChunkBuilder.BenchNoSeam = pass == 1;
                    NativeArray<float> probe = b.AllocProbe(Allocator.TempJob);
                    ColumnField f = b.BuildColumn(c, Allocator.TempJob, probe, HydroRef(b.ColumnCenter(c)));
                    VoxelChunkBuilder.BenchNoSeam = false;
                    int gx = (int)math.round((x - f.origin.x) / f.step), gz = (int)math.round((z - f.origin.y) / f.step);
                    int i = math.clamp(gz, 0, f.side - 1) * f.side + math.clamp(gx, 0, f.side - 1);
                    sb.AppendFormat(System.Globalization.CultureInfo.InvariantCulture,
                        " | L{0}{1} surf {2:0.00} river {3:0.00} gate {4:0.00} ov {5:0.00} slope {6:0.00} cliff {7:0.00}",
                        lod, pass == 1 ? " raw" : "", f.surfY[i], f.riverY[i], f.waterGate[i], f.overhang[i], f.slope[i], f.cliff[i]);
                    f.Dispose();
                    probe.Dispose();
                }
            }
            return sb.ToString();
        }

        /// <summary>Hrana LOD0 sloupce v metrech.</summary>
        public float Lod0Span => builders != null ? builders[0].ChunkSpan : 32f;

        /// <summary>
        /// Najde nejbližší místo daného typu vody. Hledá se jen v hydrologických regionech,
        /// které už stojí – jinak by každý vzorek musel region dostavět.
        /// </summary>
        /// <param name="kind">0 řeka, 1 jezero, 2 mořské pobřeží, 3 horská řeka (hladina 25 m+).</param>
        /// <param name="minDistance">Přeskočit místa blíž než tohle – pro „další" výskyt.</param>
        public bool FindWater(Vector3 from, int kind, float minDistance, float maxRadius, out Vector3 at)
        {
            at = from;
            if (builder == null) return false;

            GenParams gp = builder.Params;
            const float step = 24f;

            for (float r = Mathf.Max(0f, minDistance); r <= maxRadius; r += step)
            {
                float da = r < step ? 7f : step / r;
                for (float a = 0f; a < 6.2831853f; a += da)
                {
                    var p = new float2(from.x + r * Mathf.Cos(a), from.z + r * Mathf.Sin(a));
                    HydroRegionRef h = HydroRef(p);
                    bool hit = false;

                    // Cíl je vždycky BŘEH, ne voda: hráč má stát na souši a vidět na hladinu.
                    if (kind == 2)
                    {
                        float y = SurfaceHeight(p.x, p.y);
                        hit = y > gp.seaLevel + 1f && y < gp.seaLevel + 3f;
                    }
                    else if (h.IsValid)
                    {
                        HydroFlow f = HydroSampler.Nearest(p, h, gp.riverMinAccum, gp.riverWidthMin,
                                                           gp.riverWidthMax, gp.riverAccumFull);
                        if (kind == 4)
                        {
                            // vodopád / peřej: prudký spád toku, stojí se kousek vedle na břehu
                            float half4 = Mathf.Max(f.width * 0.5f, 1f);
                            float bank4 = Mathf.Max(half4 * gp.riverBankRatio, 2f);
                            hit = f.Found && f.waterSlope > 0.2f && f.lakeDepth <= 0.01f
                               && f.distance > half4 + bank4 + 2f && f.distance < half4 + bank4 + 10f;
                        }
                        else if (kind == 5)
                        {
                            // Kolo 22 (audit): ústí – řeka těsně nad mořem, stojí se na břehu.
                            float half5 = Mathf.Max(f.width * 0.5f, 1f);
                            float bank5 = Mathf.Max(half5 * gp.riverBankRatio, 2f);
                            hit = f.Found && f.distance > half5 + bank5 + 2f && f.distance < half5 + bank5 + 8f
                               && f.lakeDepth <= 0.01f && f.waterY > gp.seaLevel + 0.1f && f.waterY < gp.seaLevel + 2.5f;
                        }
                        else if (kind == 0 || kind == 3)
                        {
                            float half = Mathf.Max(f.width * 0.5f, 1f);
                            float bank = Mathf.Max(half * gp.riverBankRatio, 2f);
                            float minLevel = kind == 3 ? gp.seaLevel + 25f : gp.seaLevel + 4f;
                            hit = f.Found && f.distance > half + bank + 2f && f.distance < half + bank + 8f
                               && f.lakeDepth <= 0.01f && f.waterY > minLevel;
                        }
                        else
                            hit = f.lakeDepth > 0.05f && f.lakeDepth < 1.5f
                               && f.lakeLevel > gp.seaLevel + WorldGenMath.LakeMinAboveSea;
                    }

                    if (!hit) continue;
                    at = new Vector3(p.x, SurfaceHeight(p.x, p.y), p.y);
                    return true;
                }
            }
            return false;
        }

        // ── kopání ─────────────────────────────────────────────────────

        /// <summary>
        /// Vykope kouli do terénu. Vrací true, když se něco doopravdy změnilo.
        /// </summary>
        /// <param name="center">Střed ve světových souřadnicích.</param>
        /// <param name="radius">Poloměr v metrech.</param>
        /// <param name="strength">0–1. Jedna vykope naráz, menší hodnota po částech.</param>
        public bool Dig(Vector3 center, float radius, float strength = 1f)
            => Edit(center, radius, -Mathf.Clamp01(strength));

        /// <summary>Přisype kouli hmoty. Stejná matematika jako <see cref="Dig"/>, opačné znaménko.</summary>
        public bool Place(Vector3 center, float radius, float strength = 1f)
            => Edit(center, radius, Mathf.Clamp01(strength));

        private bool Edit(Vector3 center, float radius, float strength)
        {
            if (edits == null || builder == null) return false;

            // Delty čte běžící job. Než se do nich sáhne, musí dojet – jinak by se pole
            // měnilo pod rukama vlákna, které z něj právě čte.
            FlushInFlight();

            if (!edits.Apply(new float3(center.x, center.y, center.z), radius, strength)) return false;

            MarkEditedColumns();
            return true;
        }

        /// <summary>
        /// Ze změněných chunků udělá seznam sloupců k přestavění. Vlastní přestavba je
        /// jen zahození – sloupec i chunky zmizí a běžný streaming je postaví znovu,
        /// tentokrát už s deltou. Žádná zvláštní cesta kódem, a proto ani zvláštní chyby.
        /// </summary>
        private void MarkEditedColumns()
        {
            foreach (int3 c in edits.Dirty)
            {
                var key = new int3(c.x, c.z, 0);
                if (!dirtyColumns.Contains(key)) dirtyColumns.Add(key);
            }
            edits.Dirty.Clear();
        }

        private void RebuildEdited()
        {
            if (edits != null && edits.Dirty.Count > 0) MarkEditedColumns();

            for (int i = dirtyColumns.Count - 1; i >= 0; i--)
            {
                int3 key = dirtyColumns[i];
                if (!columns.TryGetValue(key, out ColumnEntry e))
                {
                    dirtyColumns.RemoveAt(i);        // ještě nevznikl nebo se odstěhoval z dohledu
                    continue;
                }

                if (e.pending > 0) continue;         // ještě z něj něco staví, počká se
                if (!TryTakeUnits(1)) break;          // společný rozpočet jobů – zkusí se příští snímek

                // Staré chunky se NESCHOVÁVAJÍ. Zůstanou stát, dokud nová verze sloupce
                // nedojede celá; teprve PublishColumn je prohodí. Právě tohle byl ten
                // problik po každém kopnutí: mezi zahozením a dostavěním uběhly snímky,
                // ve kterých v terénu nebylo nic, a čím větší chunk, tím víc to bylo vidět.
                //
                // resolvedEmpty téhle kolony se taky nechává být – platí pro starou verzi
                // a přepíše se až při prohození.
                //
                // Sloupec se NERUŠÍ, jen resetuje. Osazení tak zůstane stát: každý kop by
                // jinak znovu instancoval stovky stromů a trávy v celém sloupci a bylo by
                // to znát víc než samotná díra.
                RestartColumn(e);
                CommitLane(e.handle);

                dirtyColumns.RemoveAt(i);
            }
        }

        /// <summary>
        /// Postaví LOD0 sloupec znovu, staré chunky nechá stát do prohození (PublishColumn).
        /// Kolo 24: sdílí to kopání i přestavba švu po posunu prstence; maska švu se bere
        /// vždy aktuální z žádaného prstence.
        /// </summary>
        private void RestartColumn(ColumnEntry e)
        {
            DisposeColumn(e);

            foreach (var kv in e.staged) RecycleEntry(kv.Value);
            e.staged.Clear();
            e.stagedEmpty.Clear();

            e.ready = false;
            e.retired = false;
            e.rebuilding = true;
            e.surfaceChunks = null;
            e.seamFine = TerrainFineEdges(e.coord);
            e.probe = builder.AllocProbe(Allocator.Persistent);
            e.handle = builder.ScheduleColumn(e.coord, Allocator.Persistent, e.probe,
                                              HydroRef(builder.ColumnCenter(e.coord)), e.seamFine, out e.field);
        }

        /// <summary>
        /// Zahodí starý strom chunků, jakmile nový terén unese hráče. Práh je nízký –
        /// stačí, aby stálo okolí vieweru, ne celý dohled.
        /// </summary>
        private void DropLegacyRoot()
        {
            if (legacyRoot == null) return;
            if (chunks.Count < 12) return;

            Destroy(legacyRoot);
            legacyRoot = null;
        }

        /// <summary>Dojede všechny rozpracované joby. Používá se před sáhnutím do delt.</summary>
        private void FlushInFlight()
        {
            for (int i = 0; i < inFlight.Count; i++)
            {
                Pending p = inFlight[i];
                p.handle.Complete();

                var key = new int4(p.coord, p.lod);
                if (p.data.IsEmpty) p.column.stagedEmpty.Add(key);
                else ApplyChunk(key, p.data, p.column);

                p.data.Dispose();
                builders[p.lod].ReturnScratch(p.slot);
                p.column.pending--;
                building.Remove(key);
            }
            inFlight.Clear();
        }

        // ── kontrakt pro bootovací sekvenci ────────────────────────────

        private void CheckInitialComplete()
        {
            if (IsInitialLoadComplete) return;
            if (inFlight.Count > 0 || building.Count > 0) return;

            foreach (int3 c in desired)
                if (!columns.TryGetValue(c, out ColumnEntry e) || !e.published) return;

            IsInitialLoadComplete = true;
            streamedOnce = true;
            onInitialComplete?.Invoke();
            onInitialComplete = null;

            // Bez tohohle hlásí každý další načtený chunk průběh donekonečna
            // a konzole se zaplaví (v testu 15 520/496).
            onChunkLoaded = null;
        }

        /// <summary>Odhad počtu chunků pro ukazatel průběhu na loading screenu.</summary>
        public int GetTargetChunkCount()
        {
            int levels = Mathf.Clamp(lodCount, 1, 5);
            int cols = 0;

            for (int l = 0; l < levels; l++)
            {
                int r = l == 0 ? Mathf.Max(1, renderDistance) : Mathf.Max(2, lodRing);
                int side = 2 * r + 2;                       // po zaokrouhlení ven
                int hole = l > 0 ? (2 * (l == 1 ? Mathf.Max(1, renderDistance) : Mathf.Max(2, lodRing)) + 2) / 2 : 0;
                cols += side * side - hole * hole;
            }

            return Mathf.Max(1, cols * 3);   // typicky 2–4 povrchové chunky na sloupec
        }

        /// <summary>
        /// Spustí načítání okolí vieweru a hlásí průběh. Nahrazuje stejnojmennou metodu
        /// v EndlessTerrain, na kterou volá ChunkLoaderAPI.
        /// </summary>
        public void GenerateWorldChunks(Action chunkLoaded, Action complete)
        {
            onChunkLoaded = chunkLoaded;
            onInitialComplete = complete;
            desiredDirty = true;
            IsInitialLoadComplete = false;
        }

        /// <summary>Přenastaví dohled ze settings UI. Nahrazuje EndlessTerrain.UpdateRenderDistance.</summary>
        public void UpdateRenderDistance(int value)
        {
            renderDistance = Mathf.Clamp(value, 1, 16);
            desiredDirty = true;
        }
    }
}
