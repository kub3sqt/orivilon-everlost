using Orivilon.Data;
using Orivilon.World.Spawning;
using Unity.Mathematics;
using UnityEngine;

namespace Orivilon.World.Generation
{
    /// <summary>
    /// Osazení sloupce stromy, trávou a kamením.
    ///
    /// <para><b>Proč adaptér a ne přepsaný spawner.</b> <see cref="ObjectSpawner"/> nese
    /// spoustu vyladěného chování – deterministický RNG na buňku, vazbu na save systém
    /// (zničené objekty se po načtení neobjeví), postupné instancování po snímcích
    /// a odkládané dekorace podle dohledu. Nic z toho nesouvisí s tím, jak vznikl terén.
    /// Souvisí jen VSTUP: dřív to byla výšková mapa z <c>MapGenerator</c>, teď je to
    /// hotový <see cref="ColumnField"/>. Tenhle soubor je právě ten překlad vstupu.</para>
    ///
    /// <para><b>Kontrakt determinismu zůstává.</b> Spawner losuje z
    /// <c>GetCellRandom(chunkCoord, x, z)</c>, takže rozmístění závisí jen na seedu
    /// a souřadnici buňky – ne na pořadí, v jakém se sloupce načtou. Sloupec smí
    /// vzniknout kdykoliv a vyjde stejně.</para>
    /// </summary>
    public static class VoxelProps
    {
        /// <summary>Rozsah nad hladinou, na který se výška normalizuje do 0–1.</summary>
        private const float HeightSpan = 160f;

        private static GameObject prefab;
        private static BiomeCollection biomes;
        private static bool resolved;

        /// <summary>
        /// Dohledá prefab spawneru a sbírku biomů. Obojí se čte z Resources, takže to
        /// nevyžaduje nic nastavit ve scéně – prefab spawneru se tak načítal i dřív.
        /// </summary>
        private static void Resolve()
        {
            if (resolved) return;
            resolved = true;

            prefab = Resources.Load<GameObject>("Prefabs/SpawnableObjects");

            // Sbírka biomů visí na prefabu MapGeneratoru. Čte se z něj jako z assetu,
            // nic se neinstancuje – stará scéna generátoru už neběží.
            GameObject mg = Resources.Load<GameObject>("Prefabs/Map Generator");
            if (mg != null)
            {
                var gen = mg.GetComponent<Orivilon.World.Terrain.MapGenerator>();
                if (gen != null) biomes = gen.biomeCollection;
            }

            if (prefab == null)
                Debug.LogWarning("[VoxelProps] Prefab 'Prefabs/SpawnableObjects' nenalezen – osazení vypnuto.");
        }

        public static bool Available
        {
            get { Resolve(); return prefab != null; }
        }

        /// <summary>
        /// Kolo 6: komponenta spawneru přímo na prefabu (asset, nic se neinstancuje). Čtou se
        /// z ní jen seznam druhů, měřítko a tvary – pro vzdálené stromy.
        /// </summary>
        public static ObjectSpawner SpawnerAsset
        {
            get
            {
                Resolve();
                if (prefab == null) return null;
                // Kolo 14: místo assetu běhová (neaktivní) kopie – seznam druhů se rozšiřuje
                // o doplňky pro biomy a asset prefabu se za běhu měnit nesmí.
                if (proxySpawner == null)
                {
                    var holder = new GameObject("BiomePropsSpawnerData");
                    holder.SetActive(false);
                    holder.hideFlags = HideFlags.HideInHierarchy | HideFlags.DontSave;
                    Object.DontDestroyOnLoad(holder);
                    GameObject go = Object.Instantiate(prefab, holder.transform);
                    proxySpawner = go.GetComponent<ObjectSpawner>();
                    if (proxySpawner != null) proxySpawner.EnsureBiomeProps();
                }
                return proxySpawner;
            }
        }

        private static ObjectSpawner proxySpawner;

        /// <summary>Sbírka biomů dohledaná z prefabu MapGeneratoru. Může být null.</summary>
        public static BiomeCollection Biomes
        {
            get { Resolve(); return biomes; }
        }

        /// <summary>
        /// Ekologické osazení (kolo 5): pozice spočte <see cref="EcologyPlacer"/> z výšky,
        /// sklonu, vody, klimatu a seedu, spawner je jen instancuje. Předpoklad: LOD0 meshe
        /// sloupce jsou už v <see cref="SurfaceRaster"/> (staví VoxelTerrain těsně před voláním).
        /// </summary>
        public static ObjectSpawner SpawnEco(Transform parent, in ColumnField column, int pad,
                                             float voxelSize, float seaLevel, int2 coord,
                                             in TerrainPalette palette, in MicroParams micro,
                                             int worldSeed, float[] waterLevels)
        {
            Resolve();
            if (prefab == null) return null;

            // Kolo 6: spawner z poolu. Instantiate prefabu kopíroval celý seznam 133 druhů
            // (serializovaná data) a nový spawner si znovu alokoval seznamy detailů –
            // na každý sloupec desítky až stovky KB pro GC.
            ObjectSpawner spawner = null;
            while (Pooling && pool.Count > 0 && spawner == null) spawner = pool.Pop();
            GameObject go;
            if (spawner != null)
            {
                go = spawner.gameObject;
                go.transform.SetParent(parent, false);
                go.SetActive(true);
                PooledReuses++;
            }
            else
            {
                go = Object.Instantiate(prefab, parent);
                spawner = go.GetComponent<ObjectSpawner>();
                if (spawner == null) { Object.Destroy(go); return null; }
            }
            go.transform.localPosition = Vector3.zero;
            go.transform.localRotation = Quaternion.identity;

            var chunkCoord = new Vector2Int(coord.x, coord.y);
            spawner.EnsureBiomeProps();   // kolo 14: doplňkové druhy (jednou na instanci)
            spawner.Initialize(0, chunkCoord);

            var placements = EcologyPlacer.Build(spawner, column, pad, voxelSize, seaLevel, coord,
                                                 palette, micro, worldSeed, waterLevels);
            spawner.SpawnPlacements(parent, placements, chunkCoord);
            return spawner;
        }

        // ── kolo 6: pool spawnerů ─────────────────────────────────────
        /// <summary>Vracet spawnery uvolněných sloupců do poolu (A/B: <c>/props pool off</c>).</summary>
        public static bool Pooling = true;
        public static int PooledReuses;
        private static readonly System.Collections.Generic.Stack<ObjectSpawner> pool =
            new System.Collections.Generic.Stack<ObjectSpawner>(64);

        /// <summary>
        /// Odpojí spawner od objektu sloupce (ten se pak zničí i s osazením) a schová ho do
        /// poolu. Vrací false, když se nepooluje – pak se zničí se sloupcem jako dřív.
        /// </summary>
        public static bool Release(ObjectSpawner sp)
        {
            if (!Pooling || sp == null || pool.Count >= 96) return false;
            sp.PrepareForPool();
            sp.transform.SetParent(null, false);
            sp.gameObject.SetActive(false);
            pool.Push(sp);
            return true;
        }

        /// <summary>
        /// Postaví vstupní mapy sloupce a spustí spawn. Vrací spawner, aby na něm šlo
        /// dál volat <see cref="ObjectSpawner.UpdateDetailVisibility"/>.
        /// </summary>
        /// <param name="parent">Prázdný objekt sloupce; všechno osazení se pod něj pověsí.</param>
        /// <param name="palette">
        /// Paleta terénu. Potřebuje se kvůli sněžné čáře: nad ní nemá růst nic, a čára
        /// se musí počítat TÝMŽ vzorcem jako barva, jinak by nad bílým svahem stály stromy
        /// a pod holým vrcholem tráva.
        /// </param>
        public static ObjectSpawner Spawn(Transform parent, in ColumnField column, int pad,
                                          float voxelSize, float seaLevel, int2 coord,
                                          in TerrainPalette palette, in MicroParams micro)
        {
            Resolve();
            if (prefab == null) return null;

            // Pole se alokují na každý sloupec. Sdílený buffer by nešel: spawner si je
            // drží přes coroutinu běžící několik snímků a mezitím by je přepsal další sloupec.
            int dim = VoxelWorld.SampleDim;
            var height = new float[dim, dim];
            var altitude = new float[dim, dim];
            var biomeMap = new BiomeType[dim, dim];

            for (int z = 0; z < dim; z++)
            for (int x = 0; x < dim; x++)
            {
                int src = (z + pad) * column.side + (x + pad);

                float surf = column.surfY[src];
                altitude[x, z] = surf;

                // Pod vodou se neosazuje. Kromě moře to platí i pro koryta a jezera –
                // hladina je jinde než nula, takže samotný test proti seaLevel nestačí.
                bool wet = surf <= seaLevel;
                float lake = column.lakeY[src];
                if (lake > ColumnField.NoLake && surf < lake) wet = true;
                if (column.riverCore[src] > 0.5f && surf < column.riverY[src]) wet = true;

                // Spawner porovnává výšku se svým seaLevel (0) a s minHeight/maxHeight
                // v rozsahu 0–1. Voda dostane zápornou hodnotu, souš normalizovanou.
                height[x, z] = wet ? -1f : math.saturate((surf - seaLevel) / HeightSpan);

                // Sněžná čára se posouvá s teplotou přesně jako v TerrainPalette.Evaluate.
                // Duplikovat vzorec je nepříjemné, ale mít dvě různá čísla je horší:
                // porost by se s barvou rozešel a projevilo by se to jako les na ledovci.
                float t01 = math.saturate(column.temp[src]);
                float snowLine = palette.snowTop + palette.snowTempShift * (t01 - 0.5f) * 2f;

                BiomeType land = biomes != null
                    ? biomes.ChooseBiomeType(column.temp[src], column.hum[src])
                    : BiomeType.Grasslands;

                // Nad sněžnou čarou a nad skalním pásmem se biom PŘEPÍŠE. Spawner si pak
                // sám vybere, co do ledu a sněhu patří – žádný zvláštní zákaz se do něj
                // nepřidává a los náhodných čísel zůstává bit po bitu stejný, což je
                // podmínka determinismu rozmístění.
                if (surf >= snowLine) land = BiomeType.IceLands;
                else if (surf >= snowLine - 30f) land = BiomeType.SnowyLands;
                else if (surf >= palette.grassTop) land = BiomeType.Coldlands;

                // Mikro-biom přebíjí i tohle – je to vzácné místo, ne odstín klimatu.
                // Coldlands je jediná sada, která nese břízy; Badlands je suť a souše.
                // Druh stromu se dolaďuje až ve spawneru, tady jde jen o to, co je vůbec
                // v nabídce. Los náhodných čísel se opět nemění.
                float2 wp = column.origin + new float2(x + pad, z + pad) * voxelSize;
                MicroBiomeMath.SampleSurface(wp, micro, surf, seaLevel, out int mk, out float mw);

                if (mw > 0.35f)
                {
                    if (mk == (int)MicroBiome.GoldenGrove) land = BiomeType.Coldlands;
                    else if (mk == (int)MicroBiome.BasaltField) land = BiomeType.Badlands;
                }

                biomeMap[x, z] = wet ? BiomeType.TemperateOcean : land;
            }

            GameObject go = Object.Instantiate(prefab, parent);
            go.transform.localPosition = Vector3.zero;
            go.transform.localRotation = Quaternion.identity;

            ObjectSpawner spawner = go.GetComponent<ObjectSpawner>();
            if (spawner == null) { Object.Destroy(go); return null; }

            var chunkCoord = new Vector2Int(coord.x, coord.y);
            spawner.Initialize(0, chunkCoord);

            float2 origin = column.origin + pad * voxelSize;
            var chunkOrigin = new Vector3(origin.x, 0f, origin.y);

            spawner.SpawnObjects(parent, chunkOrigin, height, biomeMap, altitude,
                                 voxelSize, VoxelWorld.ChunkDim, chunkCoord);
            return spawner;
        }
    }
}
