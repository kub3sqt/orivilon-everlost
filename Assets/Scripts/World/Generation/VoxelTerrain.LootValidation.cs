using System.Collections.Generic;
using Orivilon.World.Spawning;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;

namespace Orivilon.World.Generation
{
    /// <summary>
    /// Kolo MP 3 (17.10.2026): hostovo ověření nároku na loot proti SKUTEČNÉMU osazení sloupce.
    ///
    /// <para>Klient nárokuje objekt klíčem (sloupec, buňka a/b, kód = druh + 4096·(vrstva+1)). Host si
    /// sloupec postaví stejnou cestou jako streamer pro LOD0 osazení – sloupcové pole
    /// (<see cref="VoxelChunkBuilder.ScheduleColumn(int2, Allocator, NativeArray{float}, in Hydro.HydroRegionRef, int, out ColumnField)"/>
    /// s hydrologií včetně odpojených regionů daleko od hosta a se šumem LOD0 švu), povrchové chunky
    /// (<see cref="VoxelChunkBuilder.ScheduleChunk"/> s hostovými terénními úpravami), raster kolizních meshů
    /// a mřížku hladin (<see cref="WaterSurface.Build"/>) – a spustí na tom TENTÝŽ osazovač
    /// <see cref="EcologyPlacer.Build"/> se seedem světa. Nevzniká druhý generátor: mění se jen to, že
    /// výsledek nejde do GameObjectů, ale do množiny klíčů. Nárok projde jen na existující umístění
    /// (všechny vrstvy osazovače včetně útvarů, ker, průduchů a drobností/pickupů).</para>
    ///
    /// <para>Cache drží posledních <see cref="LootCacheMax"/> sloupců (nároky přicházejí v dávkách z jednoho
    /// místa). Stavba sloupce běží synchronně v jednom snímku hosta, nanejvýš jeden sloupec za snímek
    /// (volající ostatní nároky odloží).</para>
    /// </summary>
    public partial class VoxelTerrain
    {
        public struct LootPlacement
        {
            public Vector3 position; public int spawnable, layer;
            /// <summary>Druh z instance spawneru, která osazení postavila (včetně doplňkových druhů EnsureBiomeProps) –
            /// nezávislé na statickém ObjectSpawner.Instance, které v buildu nemusí existovat.</summary>
            public SpawnableObject def;
        }

        private sealed class LootColumn { public Dictionary<long, LootPlacement> byHash; public int frame; public float ms; public int edits; }

        private const int LootCacheMax = 24;
        private readonly Dictionary<int2, LootColumn> lootCache = new Dictionary<int2, LootColumn>();
        private int lootBuildFrame = -1;
        private Transform lootRoot;
        private VoxelChunkBuilder lootBuilder;

        /// <summary>Diagnostika: kolik sloupců host pro ověření postavil a kolik to stálo.</summary>
        public static int StatLootColumns; public static float StatLootMsMax, StatLootMsSum;

        /// <summary>Výsledek dotazu: Ready = známe odpověď; Busy = sloupec se teď stavět nesmí (zkusit příští snímek).</summary>
        public enum LootQuery { Ready, Busy }

        /// <summary>
        /// Existuje v osazení sloupce <paramref name="column"/> objekt s klíčem (a, b, code)? Při úspěchu vrátí jeho
        /// skutečnou polohu a druh. Zničení (registr) se tu neřeší – to kontroluje volající.
        /// </summary>
        public LootQuery QueryLootPlacement(Vector2Int column, int a, int b, int code, out bool exists, out LootPlacement placement)
        {
            exists = false; placement = default;
            var key = new int2(column.x, column.y);
            if (!lootCache.TryGetValue(key, out LootColumn col) || col.edits != EditsRevision(key))
            {
                if (lootBuildFrame == Time.frameCount) return LootQuery.Busy;
                if (!BuildLootColumn(key, out col)) return LootQuery.Busy;
                lootBuildFrame = Time.frameCount;
            }
            col.frame = Time.frameCount;
            long hash = ObjectSpawner.HashForKey(column, a, b, code);
            if (col.byHash.TryGetValue(hash, out placement))
                exists = placement.spawnable == code % 4096 && placement.layer == code / 4096 - 1;
            return LootQuery.Ready;
        }

        /// <summary>QA: klíče osazení sloupce podle ověřovací cesty (porovnání se živým osazením).</summary>
        public bool QaLootColumn(Vector2Int column, out Dictionary<long, LootPlacement> byHash, out float ms)
        {
            byHash = null; ms = 0f;
            var key = new int2(column.x, column.y);
            lootCache.Remove(key);
            if (!BuildLootColumn(key, out LootColumn col)) return false;
            byHash = col.byHash; ms = col.ms;
            return true;
        }

        /// <summary>Živé osazení LOD0 sloupců (objekty + detaily) – pro QA porovnání s ověřovací cestou.</summary>
        public List<KeyValuePair<Vector2Int, List<EcoPlacement>>> QaLivePlacements()
        {
            var res = new List<KeyValuePair<Vector2Int, List<EcoPlacement>>>();
            foreach (var kv in columns)
            {
                ColumnEntry e = kv.Value;
                if (e.lod != 0 || e.spawner == null || e.spawner.EcoPlaced == null || e.propsBuild != null) continue;
                var list = new List<EcoPlacement>(e.spawner.EcoPlaced);
                e.spawner.CollectDetailKeys(list);
                res.Add(new KeyValuePair<Vector2Int, List<EcoPlacement>>(new Vector2Int(e.coord.x, e.coord.y), list));
            }
            return res;
        }

        /// <summary>Revize terénních úprav (globální): změna úprav znamená jiný mesh, tedy možná i jiné osazení.</summary>
        private int EditsRevision(int2 column)
        {
            var b = builders != null ? builders[0] : null;
            return b != null && b.edits != null ? b.edits.Revision : 0;
        }

        private bool BuildLootColumn(int2 coord, out LootColumn col)
        {
            col = null;
            if (builders == null || builders[0] == null || !EcoProps || settings == null) return false;
            // Vlastní builder (1 scratch slot): streamer může mít všechny své sloty rozpracované a sdílený slot by
            // přepsal data běžícího jobu. Parametry, paleta i úpravy se převezmou z builderu LOD0 před každou stavbou.
            if (lootBuilder == null)
                lootBuilder = new VoxelChunkBuilder(settings, ResolvedSeed, builders[0].voxelSize, 1, 0);
            VoxelChunkBuilder b = lootBuilder;
            b.CopyStateFrom(builders[0]);
            int slot = b.RentScratch();
            if (slot < 0) return false;

            var sw = System.Diagnostics.Stopwatch.StartNew();
            NativeArray<float> probe = b.AllocProbe(Allocator.Persistent);
            ColumnField field = default;
            var meshes = new List<Mesh>(8);
            var mats = new List<Matrix4x4>(8);
            ObjectSpawner sp = null;
            try
            {
                // Stejné pole jako LOD0 sloupec streameru. Šev: osazení na šev nezávisí (kolo 22/24, raster se
                // vrací na přirozený povrch přes surfRaw), hráč u objektu má navíc sousedy vždy v LOD0.
                b.ScheduleColumn(coord, Allocator.Persistent, probe, SearchHydroRef(b.ColumnCenter(coord)),
                                 LootFineEdges(coord), out field).Complete();
                b.ColumnChunkRange(coord, field, out int minY, out int maxY);
                for (int y = minY; y <= maxY; y++)
                {
                    if (b.Classify(coord, field, y, probe) != ChunkOccupancy.Surface) continue;
                    var c3 = new int3(coord.x, y, coord.y);
                    b.ScheduleChunk(c3, field, slot, Allocator.TempJob, out ChunkMeshData data, default,
                                    !b.IsUnderground(field, y)).Complete();
                    var m = new Mesh { name = "loot_validation" };
                    data.Apply(null, m);
                    data.Dispose();
                    if (m.vertexCount == 0) { Object.Destroy(m); continue; }
                    meshes.Add(m);
                    mats.Add(chunkRoot != null ? chunkRoot.localToWorldMatrix : Matrix4x4.identity);
                }

                float2 corner = b.ColumnCorner(coord);
                SurfaceRaster.Build(meshes, mats, corner.x, corner.y, b.ChunkSpan, 1f);
                if (field.surfRaw.IsCreated)
                    SurfaceRaster.SetSeamDelta(field.surfRaw, field.surfY, field.origin.x, field.origin.y, field.step, field.side);

                float[] levels = new float[VoxelWorld.SampleDim * VoxelWorld.SampleDim];
                var waterMesh = new Mesh { name = "loot_water" };
                bool hasWater = WaterSurface.Build(field, b.ColumnPad, b.voxelSize, b.Params.seaLevel,
                                                   waterRiverMin, waterMinDepth, waterMesh, levels, 0, null);
                Object.Destroy(waterMesh);
                if (!hasWater) levels = null;

                if (lootRoot == null)
                {
                    var go = new GameObject("Loot validation (host)");
                    go.SetActive(false);
                    go.transform.SetParent(transform, false);
                    lootRoot = go.transform;
                }
                EcologyPlacer.SetCaveParams(b.Params);
                sp = VoxelProps.AcquireEcoSpawner(lootRoot, coord);
                if (sp == null) return false;
                var placements = EcologyPlacer.Build(sp, field, b.ColumnPad, b.voxelSize, b.Params.seaLevel, coord,
                                                     palette, b.Params.Micro, ResolvedSeed, levels);
                var cv = new Vector2Int(coord.x, coord.y);
                var map = new Dictionary<long, LootPlacement>(placements.Count);
                foreach (var p in placements)
                    map[ObjectSpawner.HashForKey(cv, p.cellX, p.cellZ, p.spawnable + 4096 * (p.layer + 1))] =
                        new LootPlacement { position = p.position, spawnable = p.spawnable, layer = p.layer,
                                            def = sp.GetSpawnable(p.spawnable) };
                sw.Stop();
                col = new LootColumn { byHash = map, frame = Time.frameCount, ms = (float)sw.Elapsed.TotalMilliseconds, edits = EditsRevision(coord) };
                StatLootColumns++; StatLootMsSum += col.ms; StatLootMsMax = Mathf.Max(StatLootMsMax, col.ms);
                if (lootCache.Count >= LootCacheMax)
                {
                    int2 oldest = default; int of = int.MaxValue;
                    foreach (var kv in lootCache) if (kv.Value.frame < of) { of = kv.Value.frame; oldest = kv.Key; }
                    lootCache.Remove(oldest);
                }
                lootCache[coord] = col;
                return true;
            }
            finally
            {
                b.ReturnScratch(slot);
                if (field.IsCreated) field.Dispose();
                if (probe.IsCreated) probe.Dispose();
                foreach (var m in meshes) Object.Destroy(m);
                if (sp != null && !VoxelProps.Release(sp)) Object.Destroy(sp.gameObject);
            }
        }

        /// <summary>Šev sloupce tak, jako by všichni sousedé byli LOD0 (hráč stojí u objektu).</summary>
        private int LootFineEdges(int2 c)
        {
            if (!TerrainSeamByNeighbor) return 0;
            float span = Lod0Span;
            int m = 0;
            if (LodSeamJob.LineLevel(c.y * span, WaterSurface.SeamLine) > 0) m |= WaterSurface.EdgeZ0;
            if (LodSeamJob.LineLevel((c.y + 1) * span, WaterSurface.SeamLine) > 0) m |= WaterSurface.EdgeZ1;
            if (LodSeamJob.LineLevel(c.x * span, WaterSurface.SeamLine) > 0) m |= WaterSurface.EdgeX0;
            if (LodSeamJob.LineLevel((c.x + 1) * span, WaterSurface.SeamLine) > 0) m |= WaterSurface.EdgeX1;
            return m;
        }
    }
}
