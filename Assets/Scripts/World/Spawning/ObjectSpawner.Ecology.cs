using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using Orivilon.World.Objects;

namespace Orivilon.World.Spawning
{
    /// <summary>
    /// Jedna hotová pozice z <see cref="Orivilon.World.Generation.EcologyPlacer"/>.
    /// Transform je spočtený předem, spawner už jen instancuje.
    /// </summary>
    public struct EcoPlacement
    {
        public int spawnable;
        public Vector3 position;
        public Quaternion rotation;
        public Vector3 scale;

        /// <summary>Světová buňka kandidáta a vrstva – z nich vzniká hash pro save (zničené objekty).</summary>
        public int cellX, cellZ, layer;

        /// <summary><see cref="Orivilon.World.Generation.EcoBiome"/> v bodě (pro statistiku a audit).</summary>
        public byte biome;

        /// <summary>Bit 1 = podzimní listí (mikro-biom zlatý háj).</summary>
        public byte flags;

        /// <summary>Poloměr stopy v metrech (už se započteným měřítkem) – pro audit.</summary>
        public float radius;
    }

    /// <summary>
    /// Rozměry prefabu v jeho vlastních jednotkách (měřítko 1), změřené z rendererů LOD0.
    /// </summary>
    public struct PrefabShape
    {
        public float radius;   // vodorovná poloosa obálky
        public float minY;     // spodek obálky vůči pivotu
        public float height;   // výška obálky
        public float centerX, centerZ;
        public bool valid;
    }

    public partial class ObjectSpawner
    {
        [System.NonSerialized] private List<EcoPlacement> ecoPlaced;

        /// <summary>Co tenhle sloupec osadil ekologickým osazovačem (null = starou cestou nebo nic).</summary>
        public List<EcoPlacement> EcoPlaced => ecoPlaced;

        private static Dictionary<string, int> nameIndex;
        private static readonly Dictionary<int, PrefabShape> shapes = new Dictionary<int, PrefabShape>(64);

        /// <summary>Index položky ve spawnables podle jejího jména, nebo -1.</summary>
        public int FindSpawnable(string spawnableName)
        {
            if (nameIndex == null)
            {
                nameIndex = new Dictionary<string, int>(spawnables.Count);
                for (int i = 0; i < spawnables.Count; i++)
                {
                    SpawnableObject s = spawnables[i];
                    if (s == null || s.prefab == null || string.IsNullOrEmpty(s.name)) continue;
                    if (!nameIndex.ContainsKey(s.name)) nameIndex[s.name] = i;
                }
            }
            return nameIndex.TryGetValue(spawnableName, out int idx) ? idx : -1;
        }

        [System.NonSerialized] private bool biomePropsAdded;

        /// <summary>Kolo 14: cesta k doplňkovým spawnables v Resources.</summary>
        public const string BiomePropsPath = "BiomeProps/EverlostBiomeProps";

        /// <summary>
        /// Kolo 14: připojí doplňkové druhy pro klimatické regiony (BiomePropSet) na konec
        /// <see cref="spawnables"/>. Jen v paměti – prefab SpawnableObjects se nemění a původní
        /// indexy (hash uložených objektů) zůstávají. Volá se jednou před stavbou tabulky osazovače.
        /// </summary>
        public void EnsureBiomeProps()
        {
            if (biomePropsAdded) return;
            // Nikdy neměnit asset prefabu (v editoru by se změna seznamu zapsala do souboru).
            if (!gameObject.scene.IsValid()) { Debug.LogError("[ObjectSpawner] EnsureBiomeProps na assetu – přeskočeno."); return; }
            biomePropsAdded = true;
            BiomePropSet set = Resources.Load<BiomePropSet>(BiomePropsPath);
            if (set == null) { Debug.LogWarning("[ObjectSpawner] Resources/" + BiomePropsPath + " chybí – regiony použijí jen původní druhy."); return; }
            int added = 0;
            for (int i = 0; i < set.entries.Count; i++)
            {
                SpawnableObject e = set.entries[i];
                if (e == null || e.prefab == null || string.IsNullOrEmpty(e.name)) continue;
                if (FindSpawnable(e.name) >= 0) continue;
                spawnables.Add(e);
                nameIndex = null;
                added++;
            }
            nameIndex = null;
            Debug.Log($"[ObjectSpawner] Kolo 14: přidáno {added} druhů pro biomy ({spawnables.Count} spawnables celkem).");
        }

        public SpawnableObject GetSpawnable(int si)
            => si >= 0 && si < spawnables.Count ? spawnables[si] : null;

        public float GlobalScale => globalScaleMultiplier;

        /// <summary>
        /// Obálka prefabu z rendererů první úrovně detailu. Počítá se jednou na druh.
        /// Čte jen sdílená data prefabu (mesh bounds a transformy), nic neinstancuje.
        /// </summary>
        public PrefabShape GetShape(int si)
        {
            if (shapes.TryGetValue(si, out PrefabShape cached)) return cached;

            PrefabShape sh = default;
            SpawnableObject s = GetSpawnable(si);
            if (s != null && s.prefab != null)
            {
                GameObject p = s.prefab;
                Matrix4x4 toRoot = p.transform.worldToLocalMatrix;
                Renderer[] list = null;

                LODGroup g = p.GetComponentInChildren<LODGroup>(true);
                if (g != null)
                {
                    LOD[] lods = g.GetLODs();
                    if (lods.Length > 0) list = lods[0].renderers;
                }
                if (list == null || list.Length == 0) list = p.GetComponentsInChildren<MeshRenderer>(true);

                bool any = false;
                Bounds b = default;
                for (int i = 0; i < list.Length; i++)
                {
                    if (!(list[i] is MeshRenderer mr) || mr == null) continue;
                    MeshFilter f = mr.GetComponent<MeshFilter>();
                    if (f == null || f.sharedMesh == null) continue;

                    Bounds mb = f.sharedMesh.bounds;
                    Matrix4x4 m = toRoot * mr.transform.localToWorldMatrix;
                    for (int c = 0; c < 8; c++)
                    {
                        Vector3 corner = new Vector3(
                            (c & 1) == 0 ? mb.min.x : mb.max.x,
                            (c & 2) == 0 ? mb.min.y : mb.max.y,
                            (c & 4) == 0 ? mb.min.z : mb.max.z);
                        Vector3 w = m.MultiplyPoint3x4(corner);
                        if (!any) { b = new Bounds(w, Vector3.zero); any = true; }
                        else b.Encapsulate(w);
                    }
                }

                if (any)
                {
                    sh.valid = true;
                    sh.radius = Mathf.Max(b.extents.x, b.extents.z);
                    sh.minY = b.min.y;
                    sh.height = b.size.y;
                    sh.centerX = b.center.x;
                    sh.centerZ = b.center.z;
                }
            }

            shapes[si] = sh;
            return sh;
        }

        /// <summary>
        /// Osadí sloupec hotovým seznamem pozic z ekologického osazovače.
        ///
        /// <para>Větve instancování jsou tytéž jako ve <see cref="SpawnObjects"/>: tráva a objekty
        /// s omezeným dohledem jdou do pending (tráva se kreslí instancovaně), pasivní propy
        /// do dávek VegetationRenderer, interaktivní (stromy) jako GameObjecty s
        /// <see cref="DeterministicObjectId"/>. Save si dál ukládá jen počty – formát se nemění.</para>
        /// </summary>
        public void SpawnPlacements(Transform parent, List<EcoPlacement> placements, Vector2Int coord)
        {
            if (placements == null) return;
            if (spawnedChunks.TryGetValue(coord, out ChunkSpawnState existing) && !existing.isLoadedFromSave) return;
            if (spawnRoutineRunning) return;

            if (detailApplyRoutine != null)
            {
                StopCoroutine(detailApplyRoutine);
                detailApplyRoutine = null;
            }
            DespawnAllDetails();
            pendingDetails.Clear();
            spawnedDetails.Clear();

            // Tráva a drobnosti s omezeným dohledem jdou rovnou do pending (nic se neinstancuje,
            // instancovaná tráva se staví až v ApplyDetailVisibility). Do vlastního seznamu
            // sloupce se kopírují jen pevné objekty – vstupní seznam je sdílený scratch
            // osazovače a další sloupec ho přepíše dřív, než tahle coroutina doběhne.
            ecoGrass = 0; ecoOther = 0;
            int solid = 0, detail = 0;
            for (int i = 0; i < placements.Count; i++)
            {
                SpawnableObject s = GetSpawnable(placements[i].spawnable);
                if (s == null || s.prefab == null) continue;
                if (s.category == SpawnCategory.Grass || s.maxViewDistanceChunks > 0
                    || placements[i].layer == Orivilon.World.Generation.EcologyPlacer.LayerPebble) detail++; else solid++;
            }
            if (pendingDetails.Capacity < detail) pendingDetails.Capacity = detail;
            // Kolo 6: spawner z poolu si seznamy nechává – žádná nová alokace na sloupec.
            if (ecoPlaced == null) ecoPlaced = new List<EcoPlacement>(Mathf.Max(solid, 16));
            else { ecoPlaced.Clear(); if (ecoPlaced.Capacity < solid) ecoPlaced.Capacity = solid; }
            if (ecoDetailBiome == null) ecoDetailBiome = new int[4];
            else System.Array.Clear(ecoDetailBiome, 0, 4);

            for (int i = 0; i < placements.Count; i++)
            {
                EcoPlacement p = placements[i];
                SpawnableObject s = GetSpawnable(p.spawnable);
                if (s == null || s.prefab == null) continue;
                bool pebble = p.layer == Orivilon.World.Generation.EcologyPlacer.LayerPebble;
                if (s.category == SpawnCategory.Grass || s.maxViewDistanceChunks > 0 || pebble)
                {
                    long hash = GetDeterministicObjectHash(coord, p.cellX, p.cellZ, p.spawnable + 4096 * (p.layer + 1));
                    if (SaveSystem.SaveSystem.IsObjectDestroyed(hash)) continue;
                    pendingDetails.Add(new PendingDetailData
                    {
                        spawnableIndex = p.spawnable,
                        position = p.position,
                        rotation = p.rotation,
                        scale = p.scale,
                        objectHash = hash,
                        chunkCoord = coord,
                        nearOnly = pebble
                    });
                    ecoDetailBiome[p.biome & 3]++;
                    if (s.category == SpawnCategory.Grass) ecoGrass++; else ecoOther++;
                }
                else ecoPlaced.Add(p);
            }

            spawnRoutineRunning = true;
            StartCoroutine(SpawnPlacementsRoutine(parent, ecoPlaced, coord));
        }

        [System.NonSerialized] private int[] ecoDetailBiome;
        [System.NonSerialized] private int ecoGrass, ecoOther;

        /// <summary>Počty trávy a drobností (pending) podle biomu – pro /props stat.</summary>
        public int[] EcoDetailBiome => ecoDetailBiome;

        /// <summary>
        /// Tráva a drobnosti sloupce jako pozice pro audit (drží je pending seznam, ne
        /// <see cref="EcoPlaced"/>, aby se data nedržela dvakrát).
        /// </summary>
        public void CollectDetails(List<EcoPlacement> into)
        {
            for (int i = 0; i < pendingDetails.Count; i++)
            {
                PendingDetailData d = pendingDetails[i];
                into.Add(new EcoPlacement
                {
                    spawnable = d.spawnableIndex, position = d.position, rotation = d.rotation,
                    scale = d.scale, layer = 5, biome = 255, radius = 0.3f
                });
            }
        }

        // Společný rozpočet těžkých instancí (stromy s LODGroup, collidery) napříč všemi sloupci:
        // každý sloupec má vlastní coroutinu a rozpočet na spawner by se při průjezdu sčítal.
        private static int heavyFrame = -1, heavyCount;
        private const int HeavyPerFrame = 8;

        /// <summary>Kolo 17 (diagnostika /teren spike): těžké instance v daném snímku (jen poslední dva snímky).</summary>
        public static int HeavyInFrame(int frame)
            => frame == heavyFrame ? heavyCount : frame == heavyPrevFrame ? heavyPrevCount : 0;
        private static int heavyPrevFrame = -1, heavyPrevCount;

        private static bool HeavyBudgetLeft()
        {
            if (heavyFrame != Time.frameCount) { heavyPrevFrame = heavyFrame; heavyPrevCount = heavyCount; heavyFrame = Time.frameCount; heavyCount = 0; }
            return heavyCount < HeavyPerFrame;
        }

        private IEnumerator SpawnPlacementsRoutine(Transform parent, List<EcoPlacement> placements, Vector2Int coord)
        {
            int grass = ecoGrass, other = ecoOther;

            for (int i = 0; i < placements.Count; i++)
            {
                EcoPlacement p = placements[i];
                SpawnableObject s = GetSpawnable(p.spawnable);
                if (s == null || s.prefab == null) continue;

                long hash = GetDeterministicObjectHash(coord, p.cellX, p.cellZ, p.spawnable + 4096 * (p.layer + 1));
                if (SaveSystem.SaveSystem.IsObjectDestroyed(hash)) continue;

                while (!HeavyBudgetLeft()) yield return null;

                {
                    InstancedSource src = instancePassiveProps ? GetInstancedSource(s.prefab, s.category) : default;
                    if (src.Valid && src.passive)
                    {
                        if (!propMatrices.TryGetValue(p.spawnable, out List<Matrix4x4> list))
                        {
                            list = new List<Matrix4x4>(32);
                            propMatrices[p.spawnable] = list;
                        }
                        list.Add(Matrix4x4.TRS(p.position, p.rotation, p.scale) * src.offset);

                        if (src.hasCollider)
                        {
                            GameObject shell = Instantiate(s.prefab, p.position, p.rotation, parent);
                            shell.transform.localScale = p.scale;
                            StripRenderers(shell);
                            DeterministicObjectId sid = shell.GetComponent<DeterministicObjectId>();
                            if (sid == null) sid = shell.AddComponent<DeterministicObjectId>();
                            sid.Initialize(hash, coord);
                            heavyCount++;
                        }
                    }
                    else
                    {
                        GameObject go = Instantiate(s.prefab, p.position, p.rotation, parent);
                        go.transform.localScale = p.scale;
                        ApplySmallDecorationTweaks(go, s.category);
                        if ((p.flags & 1) != 0) ApplyAutumnLeaves(go);
                        ApplyTrunkLook(go, s.category);
                        if (s.category == SpawnCategory.Trees) ApplyCanopyShadow(go);

                        DeterministicObjectId id = go.GetComponent<DeterministicObjectId>();
                        if (id == null) id = go.AddComponent<DeterministicObjectId>();
                        id.Initialize(hash, coord);
                        heavyCount++;
                    }
                }

                other++;
            }

            spawnedChunks[coord] = new ChunkSpawnState
            {
                chunkCoord = coord,
                totalObjects = grass + other,
                grassCount = grass,
                isLoadedFromSave = false
            };
            objectsSpawned = true;
            spawnRoutineRunning = false;

            PublishPropBatches();
            ApplyDetailVisibility(parent);
        }

        /// <summary>
        /// Kolo 6: podzimní varianta materiálu listí (sdílená kopie), pro nelistový materiál
        /// vrací tentýž. Potřebují ji vzdálené stromy, aby zlatý háj zůstal zlatý i v dálce.
        /// </summary>
        public static Material AutumnLeafVariant(Material m)
            => m != null && IsLeafMaterial(m) ? AutumnVariant(m) : m;

        /// <summary>
        /// Kolo 6: uklidí spawner před vrácením do poolu (<see cref="Orivilon.World.Generation.VoxelProps.Release"/>).
        /// Stejný stav jako po zničení: žádné coroutiny, dávky, detaily ani záznam sloupce.
        /// Objekty sloupce (stromy, kameny) visí na objektu sloupce a zničí se s ním.
        /// </summary>
        public void PrepareForPool()
        {
            StopAllCoroutines();
            spawnRoutineRunning = false;
            detailApplyRoutine = null;
            DespawnAllDetails();
            pendingDetails.Clear();
            spawnedChunks.Clear();
            ecoPlaced?.Clear();
            lastDetailDistance = int.MaxValue;
            objectsSpawned = false;
        }

        // ------------------------------------------------------------------ kolo 18: stín koruny
        /// <summary>
        /// Kolo 18: zapnutý stín koruny ze zjednodušené kopie (přepíná <c>/props stinkoruny on|off</c>, platí pro nově osazené stromy).
        /// </summary>
        public static bool CanopyShadowProxy = true;

        /// <summary>Kolik karet nejnižšího LOD listí zůstane ve stínové kopii (0–1).</summary>
        public static float CanopyShadowKeep = 0.5f;

        /// <summary>Kolik stromů dostalo stínovou kopii (diagnostika).</summary>
        public static int CanopyShadowCount;

        private static readonly Dictionary<(Mesh, int), Mesh> canopyShadowMeshes = new Dictionary<(Mesh, int), Mesh>();

        /// <summary>
        /// Kolo 18: stín koruny kreslí jen zjednodušená kopie nejnižšího LOD listí.
        ///
        /// <para><b>Proč.</b> Měření v husté džungli (s2026, 2813/1472): stínový průchod listí stál
        /// ~11 ms z ~19 ms snímku. Karty koruny jsou alfa-ořezané (clip) a ve stínových kaskádách
        /// se kreslí v LOD0/LOD1 hustotě a s ~15násobným překryvem – GPU stínuje každý fragment.
        /// LOD1/LOD2 mají stejnou celkovou plochu karet, jen méně vrcholů, takže LOD samo nepomůže.</para>
        ///
        /// <para><b>Jak.</b> Listí všech LOD úrovní stín nevrhá. Místo něj přibude jeden renderer
        /// <c>ShadowsOnly</c> s kopií nejnižšího LOD listí, ve které zůstane jen část karet
        /// (<see cref="CanopyShadowKeep"/>). Je ve všech úrovních LODGroup, takže zmizí přesně se stromem.
        /// Kopie sdílí materiál i prostor objektu, takže vítr i ořez alfy jsou stejné. Vzhled ve
        /// hře (listí, kmeny, liány) se nemění, prefab ani asset se nemění – kopie meshe vzniká
        /// za běhu jednou na druh.</para>
        /// </summary>
        public static bool ApplyCanopyShadow(GameObject go)
        {
            if (!CanopyShadowProxy || go == null) return false;
            LODGroup g = go.GetComponent<LODGroup>();
            if (g == null) return false;
            LOD[] lods = g.GetLODs();
            if (lods.Length < 2) return false;

            MeshRenderer src = null;
            Renderer[] low = lods[lods.Length - 1].renderers;
            for (int i = 0; low != null && i < low.Length; i++)
                if (low[i] is MeshRenderer mr && mr != null && mr.gameObject.name.Contains("Leaves_LOD")) src = mr;
            if (src == null || src.shadowCastingMode == ShadowCastingMode.Off) return false;
            MeshFilter mf = src.GetComponent<MeshFilter>();
            Mesh shadowMesh = mf != null ? CanopyShadowMesh(mf.sharedMesh, CanopyShadowKeep) : null;
            if (shadowMesh == null) return false;

            for (int l = 0; l < lods.Length; l++)
            {
                Renderer[] rs = lods[l].renderers;
                for (int i = 0; rs != null && i < rs.Length; i++)
                    if (rs[i] != null && rs[i].gameObject.name.Contains("Leaves_LOD"))
                        rs[i].shadowCastingMode = ShadowCastingMode.Off;
            }

            var child = new GameObject("Leaves_Shadow");
            child.layer = src.gameObject.layer;
            Transform ct = child.transform, st = src.transform;
            ct.SetParent(st.parent, false);
            ct.localPosition = st.localPosition;
            ct.localRotation = st.localRotation;
            ct.localScale = st.localScale;
            child.AddComponent<MeshFilter>().sharedMesh = shadowMesh;
            var r = child.AddComponent<MeshRenderer>();
            r.sharedMaterial = src.sharedMaterial;
            r.shadowCastingMode = ShadowCastingMode.ShadowsOnly;
            r.receiveShadows = false;
            r.lightProbeUsage = LightProbeUsage.Off;
            r.reflectionProbeUsage = ReflectionProbeUsage.Off;

            for (int l = 0; l < lods.Length; l++)
            {
                Renderer[] rs = lods[l].renderers ?? new Renderer[0];
                var nr = new Renderer[rs.Length + 1];
                rs.CopyTo(nr, 0);
                nr[rs.Length] = r;
                lods[l].renderers = nr;
            }
            g.SetLODs(lods);
            CanopyShadowCount++;
            return true;
        }

        /// <summary>Kolo 18 (A/B v konzoli): vrátí strom do původního stavu – listí zase vrhá stín, kopie zmizí.</summary>
        public static bool RemoveCanopyShadow(GameObject go)
        {
            LODGroup g = go != null ? go.GetComponent<LODGroup>() : null;
            if (g == null) return false;
            LOD[] lods = g.GetLODs();
            Renderer proxy = null;
            for (int l = 0; l < lods.Length; l++)
            {
                var keepList = new List<Renderer>();
                Renderer[] rs = lods[l].renderers;
                for (int i = 0; rs != null && i < rs.Length; i++)
                {
                    if (rs[i] == null) continue;
                    if (rs[i].gameObject.name == "Leaves_Shadow") { proxy = rs[i]; continue; }
                    if (rs[i].gameObject.name.Contains("Leaves_LOD")) rs[i].shadowCastingMode = ShadowCastingMode.On;
                    keepList.Add(rs[i]);
                }
                lods[l].renderers = keepList.ToArray();
            }
            if (proxy == null) return false;
            g.SetLODs(lods);
            Destroy(proxy.gameObject);
            CanopyShadowCount--;
            return true;
        }

        /// <summary>
        /// Kopie meshe karet, ve které zůstane podíl <paramref name="keep"/> karet (karta = trojúhelníky
        /// se společnými vrcholy). Výběr je deterministický a rovnoměrný (zlatý řez), takže tvar
        /// koruny i rozložení karet zůstává; jen řídne překryv. Na druh a podíl jen jednou.
        /// </summary>
        private static Mesh CanopyShadowMesh(Mesh src, float keep)
        {
            if (src == null) return null;
            int kk = Mathf.RoundToInt(Mathf.Clamp01(keep) * 1000f);
            if (canopyShadowMeshes.TryGetValue((src, kk), out Mesh cached) && cached != null) return cached;
            if (!src.isReadable) return null;
            if (kk >= 1000) { canopyShadowMeshes[(src, kk)] = src; return src; }

            Vector3[] v = src.vertices;
            Vector3[] n = src.normals;
            Vector2[] uv = src.uv;
            int[] t = src.triangles;
            int cards = 0;
            var cardOf = new int[v.Length];
            for (int i = 0; i < cardOf.Length; i++) cardOf[i] = -1;
            // karta = souvislá skupina vrcholů sdílených trojúhelníky (Card() dává 4 vrcholy a 2 trojúhelníky)
            for (int i = 0; i + 2 < t.Length; i += 3)
            {
                int c = cardOf[t[i]] >= 0 ? cardOf[t[i]] : cardOf[t[i + 1]] >= 0 ? cardOf[t[i + 1]] : cardOf[t[i + 2]] >= 0 ? cardOf[t[i + 2]] : cards++;
                cardOf[t[i]] = cardOf[t[i + 1]] = cardOf[t[i + 2]] = c;
            }
            var remap = new int[v.Length];
            for (int i = 0; i < remap.Length; i++) remap[i] = -1;
            var nv = new List<Vector3>(v.Length);
            var nn = new List<Vector3>(v.Length);
            var nuv = new List<Vector2>(v.Length);
            var nt = new List<int>(t.Length);
            float k = kk / 1000f;
            for (int i = 0; i + 2 < t.Length; i += 3)
            {
                int c = cardOf[t[i]];
                if (Mathf.Repeat(c * 0.6180339887f, 1f) >= k) continue;
                for (int j = 0; j < 3; j++)
                {
                    int o = t[i + j];
                    if (remap[o] < 0)
                    {
                        remap[o] = nv.Count;
                        nv.Add(v[o]);
                        nn.Add(n != null && n.Length == v.Length ? n[o] : Vector3.up);
                        nuv.Add(uv != null && uv.Length == v.Length ? uv[o] : Vector2.zero);
                    }
                    nt.Add(remap[o]);
                }
            }
            var m = new Mesh { name = src.name + "_Stin" };
            m.SetVertices(nv);
            m.SetNormals(nn);
            m.SetUVs(0, nuv);
            m.SetTriangles(nt, 0);
            m.bounds = src.bounds;   // stejná obálka jako zdroj – culling stínu se nezmění
            m.UploadMeshData(true);
            canopyShadowMeshes[(src, kk)] = m;
            return m;
        }

        /// <summary>Podzimní listí jako v <see cref="ApplyMicroLook"/>, jen bez dotazu na terén.</summary>
        private void ApplyAutumnLeaves(GameObject go)
        {
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
                if (changed) renderers[i].sharedMaterials = mats;
            }
        }
    }
}
