using Orivilon.World.Generation;
using Orivilon.World.Generation.Hydro;
using Unity.Collections;
using Unity.Mathematics;
using UnityEditor;
using UnityEngine;

namespace Orivilon.EditorTools
{
    /// <summary>
    /// Změří hladinu v okolí hráče (nebo středu náhledu) a vypíše čísla do konzole.
    ///
    /// <para>Existuje kvůli jedné otázce, na kterou se ze screenshotu odpovědět nedá:
    /// <b>rozhoduje o břehu hloubka, nebo maska?</b> Buňka vzatá celá (mask 15) znamená,
    /// že se neřezalo nic a hranice vody kopíruje mřížku – to jsou ty schody. Buňka
    /// oříznutá izolinií znamená, že hranici určil terén. Poměr těch dvou čísel řekne,
    /// která z obou možností v daném kraji převládá.</para>
    ///
    /// <para>Druhá otázka je „levitující" voda: hladina namalovaná na svahu, kterou
    /// koryto nevyřezalo. Ta je vždycky MĚLKÁ – dno pod ní je přirozený terén, ne
    /// vyrovnané dno. Proto se počítá podíl buněk mělčích než 30 cm.</para>
    ///
    /// <para><c>WaterSurface.Version</c> ve výpisu je otisk zdrojáku. Když se neshoduje
    /// s tím, co je v souboru, běží stará přeložená verze a nemá smysl hledat chybu
    /// v logice.</para>
    /// </summary>
    public static class WaterDiagnostics
    {
        [MenuItem("Orivilon/Diagnostika vody")]
        public static void Run()
        {
            VoxelTerrain terrain = Object.FindFirstObjectByType<VoxelTerrain>();
            VoxelPreview preview = Object.FindFirstObjectByType<VoxelPreview>();

            WorldGenSettings settings = terrain != null ? terrain.settings
                                      : preview != null ? preview.settings : null;
            if (settings == null)
            {
                Debug.LogError("[Voda] Ve scéně není VoxelTerrain ani VoxelPreview s nastavením.");
                return;
            }

            int seed = terrain != null ? terrain.ResolvedSeed : preview.seed;
            float voxel = terrain != null ? terrain.voxelSize : preview.voxelSize;
            float coreMin = terrain != null ? terrain.waterRiverMin : preview.waterRiverMin;
            float sink = terrain != null ? terrain.waterMinDepth : preview.waterMinDepth;

            // Střed: kde stojí hráč, jinak střed náhledu. Levitující řeka je jev vázaný
            // na místo, takže měřit kilometry daleko od toho, co má člověk na obrazovce,
            // by nedávalo smysl.
            int2 center = int2.zero;
            GameObject player = GameObject.FindGameObjectWithTag("Player");
            float span = VoxelWorld.ChunkDim * voxel;

            if (Application.isPlaying && player != null)
            {
                Vector3 p = player.transform.position;
                center = (int2)math.floor(new float2(p.x, p.z) / span);
            }
            else if (preview != null)
            {
                center = new int2(preview.centerChunk.x, preview.centerChunk.y);
            }

            var total = new WaterSurface.Stats();
            int cols = 0;
            var mesh = new Mesh { name = "water_diag" };

            using (var builder = new VoxelChunkBuilder(settings, seed, voxel))
            using (var hydro = new HydroMap(builder.Params, builder.Splines))
            {
                // Měřit tam, kde voda opravdu je. Bez tohohle vyšlo měření z čistě suchého
                // kraje a čísla byla samá nula – což o hladině neříká vůbec nic. Ani poloha
                // hráče nestačí: na kopci kolem sebe vodu nemá, i když je řeka o dvě stě
                // metrů dál. Proto se ptáme rovnou hydrologické mapy, kde je největší tok.
                center = FindRiver(builder, hydro, voxel, center);

                for (int cz = -2; cz <= 2; cz++)
                for (int cx = -2; cx <= 2; cx++)
                {
                    int2 col = center + new int2(cx, cz);
                    NativeArray<float> probe = builder.AllocProbe(Allocator.Persistent);
                    ColumnField column = builder.BuildColumn(col, Allocator.Persistent, probe,
                                                             hydro.GetRefBlocking(builder.ColumnCenter(col)));

                    WaterSurface.Build(column, builder.ColumnPad, voxel,
                                       builder.Params.seaLevel, coreMin, sink, mesh);

                    WaterSurface.Stats s = WaterSurface.LastStats;
                    total.sea += s.sea; total.lake += s.lake; total.river += s.river;
                    total.waterCells += s.waterCells; total.boundaryEdges += s.boundaryEdges;
                    total.full += s.full; total.cut += s.cut; total.shallow += s.shallow;
                    total.depthSum += s.depthSum;
                    total.riverShallow += s.riverShallow; total.spreadSum += s.spreadSum;
                    cols++;

                    column.Dispose();
                    probe.Dispose();
                }
            }

            Object.DestroyImmediate(mesh);

            int totalCells = cols * VoxelWorld.ChunkDim * VoxelWorld.ChunkDim;
            int w = Mathf.Max(total.waterCells, 1);

            Debug.Log(
                $"[Voda] verze kódu: {WaterSurface.Version} | seed {seed}, voxel {voxel} m, " +
                $"střed sloupce ({center.x}, {center.y}), prah koryta {coreMin}, zanoření {sink} m\n" +
                $"  plocha: {total.waterCells * 100f / totalCells:0.0} % z {cols} sloupců " +
                $"(moře {total.sea * 100f / w:0} %, jezera {total.lake * 100f / w:0} %, řeky {total.river * 100f / w:0} %)\n" +
                $"  buňky: celé {total.full} ({total.full * 100f / w:0} %), oříznuté izolinií {total.cut} ({total.cut * 100f / w:0} %)\n" +
                $"  hloubka: průměr {total.MeanDepth:0.00} m, mělčích než 30 cm {total.ShallowRatio * 100f:0} %, " +
                $"rozdíl rohů {total.MeanSpread:0.00} m\n" +
                $"  řeka: mělčí než 1 m {total.RiverShallowRatio * 100f:0} % (vyřezané koryto má mít 1,2 m a víc)\n" +
                $"  obvod/plocha {total.PerimeterRatio:0.00}");
        }

        /// <summary>
        /// Najde nejsilnější tok v okolí – ne hledáním vody po sloupcích, ale přečtením
        /// akumulace přímo z hydrologické mapy.
        ///
        /// <para>Slepé prohledávání sloupců bylo k ničemu: 25 sloupců kolem počátku je
        /// 800 m a v tomhle seedu tam žádná voda není. Hydro mapa přitom drží akumulaci
        /// pro celý region 8 km a maximum v ní JE ta největší řeka široko daleko.</para>
        ///
        /// <para>Buňky pod hladinou moře se vynechávají: akumulace pokračuje i po dně a
        /// nejvyšší hodnoty celé mapy leží právě ve šelfu, takže bez téhle podmínky by
        /// měření vždycky skončilo v moři – tedy přesně tam, kde se o řekách nic nedozvím.</para>
        /// </summary>
        private static int2 FindRiver(VoxelChunkBuilder builder, HydroMap hydro, float voxel, int2 fallback)
        {
            float span0 = VoxelWorld.ChunkDim * voxel;
            HydroRegionRef r = hydro.GetRefBlocking((float2)fallback * span0);
            if (!r.IsValid) { Debug.LogWarning("[Voda] hydro region není platný."); return fallback; }

            float sea = builder.Params.seaLevel;
            float best = 0f;
            int bestIdx = -1;

            for (int i = 0; i < r.accum.Length; i++)
            {
                if (r.filled[i] < sea + 3f) continue;      // moře a šelf ne
                if (r.accum[i] <= best) continue;
                best = r.accum[i];
                bestIdx = i;
            }

            if (bestIdx < 0) { Debug.LogWarning("[Voda] v regionu není žádný tok nad hladinou."); return fallback; }

            float2 world = r.origin + (new float2(bestIdx % r.side, bestIdx / r.side) + 0.5f)
                                      * HydroWorld.CellSize;

            Debug.Log($"[Voda] nejsilnější tok: akumulace {best:0} buněk, hladina {r.filled[bestIdx]:0} m, " +
                      $"svět ({world.x:0}, {world.y:0})");

            float span = VoxelWorld.ChunkDim * voxel;
            return (int2)math.floor(world / span);
        }
    }
}
