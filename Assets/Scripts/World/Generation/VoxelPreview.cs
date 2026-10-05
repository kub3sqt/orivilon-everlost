using System.Collections.Generic;
using System.Diagnostics;
using Orivilon.World.Generation.Hydro;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace Orivilon.World.Generation
{
    /// <summary>
    /// Zkušební scéna pro fázi 1: vygeneruje čtvercové okolí chunků kolem sebe a postaví
    /// z nich GameObjecty. Není to streamer – ten přijde ve fázi 3 jako náhrada EndlessTerrain.
    /// Slouží k tomu, aby šlo po voxelovém terénu chodit a podívat se na něj.
    ///
    /// Generuje se přes kontextové menu komponenty (ozubené kolečko → Generovat).
    /// </summary>
    [AddComponentMenu("Orivilon/Voxel Preview (fáze 1)")]
    public class VoxelPreview : MonoBehaviour
    {
        [Header("Generátor")]
        public WorldGenSettings settings;
        public int seed = 1337;

        [Tooltip("Velikost voxelu v metrech. Hlavní páka low-poly vzhledu – větší = větší fasety.")]
        [Range(0.5f, 4f)] public float voxelSize = 1f;

        [Tooltip("Přichycení průsečíku ke krajům hrany. Ruší tenké trojúhelníky, které s flat shadingem blikají.")]
        [Range(0f, 0.24f)] public float edgeSnap = 0.06f;

        [Header("Rozsah")]
        [Tooltip("Poloměr v chuncích. 4 = 9x9 sloupců, tedy 288 m při voxelu 1 m. Makro pole mají periodu 1–4 km, takže na posouzení rázu světa slouž 2D náhled – tady se kouká na tvarosloví zblízka.")]
        [Range(1, 10)] public int radiusChunks = 4;

        public Vector2Int centerChunk = Vector2Int.zero;

        [Header("Zobrazení")]
        [Tooltip("Materiál terénu. Musí číst vertex color, jinak bude terén jednobarevný.")]
        public Material terrainMaterial;

        public bool generateColliders = true;
        public bool castShadows = true;

        [Header("Voda (jen pro měření)")]
        [Range(0.05f, 0.95f)] public float waterRiverMin = 0.2f;
        [Range(0f, 2f)] public float waterMinDepth = 0.45f;

        [Header("Barvy")]
        public TerrainPalette palette = TerrainPalette.Default;

        /// <summary>Paleta s doplněnými klimatickými barvami, kdyby ji scéna měla ještě bez nich.</summary>
        private TerrainPalette Palette
        {
            get { TerrainPalette p = palette; p.EnsureClimate(); return p; }
        }

        private readonly List<GameObject> spawned = new List<GameObject>();

        [ContextMenu("Generovat")]
        public void Generate()
        {
            if (settings == null)
            {
                Debug.LogError("[VoxelPreview] Chybí WorldGenSettings.", this);
                return;
            }

            ClearChunks();

            var sw = Stopwatch.StartNew();
            int chunks = 0, tris = 0, skipped = 0, empty = 0;

            using (var builder = new VoxelChunkBuilder(settings, seed, voxelSize)
                   { edgeSnap = edgeSnap, palette = Palette })
            using (var hydro = new HydroMap(builder.Params, builder.Splines))
            {
                for (int cz = -radiusChunks; cz <= radiusChunks; cz++)
                for (int cx = -radiusChunks; cx <= radiusChunks; cx++)
                {
                    int2 columnCoord = new int2(centerChunk.x + cx, centerChunk.y + cz);
                    NativeArray<float> probe = builder.AllocProbe(Allocator.Persistent);
                    ColumnField column = builder.BuildColumn(columnCoord, Allocator.Persistent, probe,
                                                             hydro.GetRefBlocking(builder.ColumnCenter(columnCoord)));

                    builder.ColumnChunkRange(columnCoord, column, out int minY, out int maxY);

                    for (int cy = minY; cy <= maxY; cy++)
                    {
                        if (builder.Classify(columnCoord, column, cy, probe) != ChunkOccupancy.Surface) { skipped++; continue; }

                        var coord = new int3(columnCoord.x, cy, columnCoord.y);
                        ChunkMeshData data = builder.BuildChunk(coord, column, Allocator.Persistent);

                        if (!data.IsEmpty)
                        {
                            CreateChunkObject(coord, data);
                            chunks++;
                            tris += data.renderTris.Length / 3;
                        }
                        else empty++;
                        data.Dispose();
                    }

                    column.Dispose();
                    probe.Dispose();
                }
            }

            sw.Stop();
            Debug.Log($"[VoxelPreview] {chunks} chunků, {tris:N0} trojúhelníků, " +
                      $"{skipped} prořezáno, {empty} naprázdno, {sw.ElapsedMilliseconds} ms " +
                      $"({(chunks > 0 ? sw.Elapsed.TotalMilliseconds / chunks : 0):0.0} ms/chunk)", this);
        }

        /// <summary>
        /// Změří skutečnou průchodnost jeskyní na několika chuncích pod povrchem.
        /// Šířka se nedá spolehlivě odvodit z prahu v jednotkách šumu – tohle čte výsledek.
        /// </summary>
        [ContextMenu("Změřit jeskyně")]
        public void MeasureCaves()
        {
            if (settings == null)
            {
                Debug.LogError("[VoxelPreview] Chybí WorldGenSettings.", this);
                return;
            }

            float openSum = 0f, hSum = 0f, vSum = 0f, p90Sum = 0f;
            float tSum = 0f, hallSum = 0f, rSum = 0f;
            int samples = 0;

            using (var builder = new VoxelChunkBuilder(settings, seed, voxelSize)
                   { edgeSnap = edgeSnap, palette = Palette })
            using (var hydro = new HydroMap(builder.Params, builder.Splines))
            {
                for (int cz = -1; cz <= 1; cz++)
                for (int cx = -1; cx <= 1; cx++)
                {
                    int2 col = new int2(centerChunk.x + cx * 2, centerChunk.y + cz * 2);
                    NativeArray<float> probe = builder.AllocProbe(Allocator.Persistent);
                    ColumnField column = builder.BuildColumn(col, Allocator.Persistent, probe,
                                                             hydro.GetRefBlocking(builder.ColumnCenter(col)));

                    // chunk zhruba 40 m pod nejnižším povrchem sloupce
                    float span = VoxelWorld.ChunkDim * voxelSize;
                    int cy = Mathf.FloorToInt((column.MinHeight - 40f) / span);
                    VoxelWorld.ChunkYRange(voxelSize, out int wMin, out int wMax);
                    cy = Mathf.Clamp(cy, wMin, wMax);

                    var st = builder.MeasureCaves(new int3(col.x, cy, col.y), column);
                    if (st.runs > 0)
                    {
                        openSum += st.openFraction; hSum += st.meanHorizontal;
                        vSum += st.meanVertical; p90Sum += st.p90Horizontal;
                        tSum += st.shareTunnel; hallSum += st.shareHall; rSum += st.shareRavine;
                        samples++;
                    }

                    column.Dispose();
                    probe.Dispose();
                }
            }

            if (samples == 0)
            {
                Debug.LogWarning("[VoxelPreview] Pod povrchem se nenašla ani jedna dutina – " +
                                 "zkontroluj caveStrength.", this);
                return;
            }

            Debug.Log($"[VoxelPreview] Jeskyně z {samples} chunků: " +
                      $"vzduchu {openSum / samples * 100f:0.0} % objemu, " +
                      $"vodorovná světlost ⌀ {hSum / samples:0.0} m (p90 {p90Sum / samples:0.0} m), " +
                      $"svislá světlost ⌀ {vSum / samples:0.0} m | " +
                      $"z toho tunely {tSum / samples * 100f:0} %, síně {hallSum / samples * 100f:0} %, " +
                      $"propasti {rSum / samples * 100f:0} %", this);
        }

        /// <summary>
        /// Změří pokrytí hladinou po typech a její roztrhanost.
        ///
        /// Poměr obvodu k ploše je nejrychlejší způsob, jak z čísla poznat, že se voda
        /// rozpadla na pruhy: kompaktní jezero má hluboko pod 1, hřeben tenkých pruhů se
        /// blíží 2. Bez toho se hádá, který ze tří druhů vody dělá nepořádek.
        /// </summary>
        [ContextMenu("Změřit vodu")]
        public void MeasureWater()
        {
            if (settings == null)
            {
                Debug.LogError("[VoxelPreview] Chybí WorldGenSettings.", this);
                return;
            }

            int sea = 0, lake = 0, river = 0, cells = 0, edges = 0, cols = 0;
            var mesh = new Mesh { name = "water_measure" };

            using (var builder = new VoxelChunkBuilder(settings, seed, voxelSize)
                   { edgeSnap = edgeSnap, palette = Palette })
            using (var hydro = new HydroMap(builder.Params, builder.Splines))
            {
                for (int cz = -2; cz <= 2; cz++)
                for (int cx = -2; cx <= 2; cx++)
                {
                    int2 col = new int2(centerChunk.x + cx, centerChunk.y + cz);
                    NativeArray<float> probe = builder.AllocProbe(Allocator.Persistent);
                    ColumnField column = builder.BuildColumn(col, Allocator.Persistent, probe,
                                                             hydro.GetRefBlocking(builder.ColumnCenter(col)));

                    WaterSurface.Build(column, builder.ColumnPad, voxelSize,
                                       builder.Params.seaLevel, waterRiverMin, waterMinDepth, mesh);

                    WaterSurface.Stats st = WaterSurface.LastStats;
                    sea += st.sea; lake += st.lake; river += st.river;
                    cells += st.waterCells; edges += st.boundaryEdges;
                    cols++;

                    column.Dispose();
                    probe.Dispose();
                }
            }

            DestroyImmediate(mesh);

            int totalCells = cols * (VoxelWorld.ChunkDim * VoxelWorld.ChunkDim);
            float ratio = cells > 0 ? edges / (float)cells : 0f;

            Debug.Log($"[VoxelPreview] Voda z {cols} sloupců: {cells * 100f / totalCells:0.0} % plochy " +
                      $"(moře {sea * 100f / Mathf.Max(cells, 1):0} %, jezera {lake * 100f / Mathf.Max(cells, 1):0} %, " +
                      $"řeky {river * 100f / Mathf.Max(cells, 1):0} %) | " +
                      $"obvod/plocha {ratio:0.00} — pod 1.0 je kompaktní, nad 1.5 roztrhané", this);
        }

        [ContextMenu("Smazat")]
        public void ClearChunks()
        {
            for (int i = spawned.Count - 1; i >= 0; i--)
            {
                if (spawned[i] == null) continue;
                if (Application.isPlaying) Destroy(spawned[i]);
                else DestroyImmediate(spawned[i]);
            }
            spawned.Clear();

            // Pojistka pro objekty, které v seznamu nezůstaly (např. po reloadu domény).
            for (int i = transform.childCount - 1; i >= 0; i--)
            {
                GameObject child = transform.GetChild(i).gameObject;
                if (!child.name.StartsWith("Chunk ")) continue;
                if (Application.isPlaying) Destroy(child);
                else DestroyImmediate(child);
            }
        }

        private void CreateChunkObject(int3 coord, in ChunkMeshData data)
        {
            // Vrcholy meshe jsou ve světových souřadnicích, takže chunk nesmí zdědit
            // posun rodiče – jinak by se celý terén posunul o transform této komponenty.
            var go = new GameObject($"Chunk {coord.x} {coord.y} {coord.z}");
            go.transform.SetParent(transform, false);
            go.transform.position = Vector3.zero;
            go.transform.rotation = Quaternion.identity;
            go.transform.localScale = Vector3.one;

            var renderMesh = new Mesh { name = $"chunk_r_{coord.x}_{coord.y}_{coord.z}" };
            Mesh colliderMesh = generateColliders
                ? new Mesh { name = $"chunk_c_{coord.x}_{coord.y}_{coord.z}" }
                : null;

            data.Apply(renderMesh, colliderMesh);

            go.AddComponent<MeshFilter>().sharedMesh = renderMesh;

            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = terrainMaterial != null ? terrainMaterial : FallbackMaterial();
            mr.shadowCastingMode = castShadows
                ? UnityEngine.Rendering.ShadowCastingMode.On
                : UnityEngine.Rendering.ShadowCastingMode.Off;

            if (colliderMesh != null && colliderMesh.vertexCount > 0)
                go.AddComponent<MeshCollider>().sharedMesh = colliderMesh;

            spawned.Add(go);
        }

        private static Material fallback;

        private static Material FallbackMaterial()
        {
            if (fallback != null) return fallback;

            Shader s = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            fallback = new Material(s) { name = "VoxelPreview Fallback" };
            Debug.LogWarning("[VoxelPreview] Není přiřazen materiál terénu – použit náhradní. " +
                             "Vertex colors nebudou vidět; přiřaď materiál z MapGenerator.terrainMaterial.");
            return fallback;
        }
    }
}
