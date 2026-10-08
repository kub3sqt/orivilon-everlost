using Orivilon.World.Generation.Hydro;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using static Unity.Mathematics.math;

namespace Orivilon.World.Generation
{
    /// <summary>
    /// Fasáda sloupcové vrstvy generátoru.
    ///
    /// Jedna a tatáž funkce obsluhuje runtime i editorový náhled – liší se jen měřítko:
    /// runtime chce side = 49, step = 1 (chunk 32 + okraj 8), náhled třeba side = 512, step = 8.
    ///
    /// Hydrologie sem přichází HOTOVÁ zvenčí jako <see cref="HydroRegionRef"/>. Sloupec si ji
    /// nepočítá – to je celý rozdíl proti staré údolní metodě, která si okolní výšky sháněla
    /// sama a proto neuměla nic, co potřebuje vědět, co se děje o kilometry dál.
    /// </summary>
    public static class WorldGen
    {
        /// <summary>
        /// Naplánuje celý sloupcový průchod. Volající musí handle dokončit (Complete)
        /// a výsledek Dispose()nout.
        /// </summary>
        /// <param name="hydro">
        /// Region, ve kterém sloupec leží. Neplatný pohled = terén bez řek; streamer to
        /// nesmí dopustit, ale editorové cesty s tím musí umět žít.
        /// </param>
        public static ColumnField Schedule(float2 origin, float step, int side,
                                           in GenParams gp, in SplineSet spl,
                                           in HydroRegionRef hydro,
                                           Allocator allocator, out JobHandle handle)
            => Schedule(origin, step, side, gp, spl, hydro, allocator, 0f, out handle);

        /// <param name="seamLine">
        /// Rozteč hranic LOD0 sloupců v metrech (32 při voxelu 1 m). Nula = bez šití švů
        /// (editorové náhledy). Viz <see cref="LodSeamJob"/>.
        /// </param>
        public static ColumnField Schedule(float2 origin, float step, int side,
                                           in GenParams gp, in SplineSet spl,
                                           in HydroRegionRef hydro,
                                           Allocator allocator, float seamLine, out JobHandle handle)
            => Schedule(origin, step, side, gp, spl, hydro, allocator, seamLine, 0, default, false, out handle);

        /// <param name="fineMask">Kolo 24: hrany s LOD0 sousedem, na kterých se nešije (viz LodSeamJob.fineMask).</param>
        /// <param name="keepRaw">Kolo 24: ponechat výšku před šitím v <c>field.surfRaw</c> (osazení LOD0).</param>
        public static ColumnField Schedule(float2 origin, float step, int side,
                                           in GenParams gp, in SplineSet spl,
                                           in HydroRegionRef hydro,
                                           Allocator allocator, float seamLine,
                                           int fineMask, int2 fineLine0, bool keepRaw, out JobHandle handle)
        {
            side = max(side, 3);
            int n = side * side;

            var field = new ColumnField
            {
                origin = origin,
                step = step,
                side = side,
                macroY    = Alloc(n, allocator),
                surfY     = Alloc(n, allocator),
                shape     = Alloc(n, allocator),
                cont      = Alloc(n, allocator),
                eros      = Alloc(n, allocator),
                pv        = Alloc(n, allocator),
                temp      = Alloc(n, allocator),
                hum       = Alloc(n, allocator),
                slope     = Alloc(n, allocator),
                cliff     = Alloc(n, allocator),
                riverCore = Alloc(n, allocator),
                riverY    = Alloc(n, allocator),
                flowDir   = new NativeArray<float2>(n, allocator, NativeArrayOptions.UninitializedMemory),
                flowSlope = Alloc(n, allocator),
                lakeY     = Alloc(n, allocator),
                shoreY    = Alloc(n, allocator),
                waterGate = Alloc(n, allocator),
                overhang  = Alloc(n, allocator),
                bounds    = new NativeArray<float>(2, allocator),
            };

            // Rozsah Worley buněk, které mohou do výřezu zasáhnout, včetně přesahu o poloměr jezera.
            float extent = (side - 1) * step;
            float reach = max(gp.lakeReachMax, gp.lakeRadiusMax);
            float2 mn = origin - reach;
            float2 mx = origin + extent + reach;
            int2 cellMin = (int2)floor(mn / gp.lakeCell);
            int2 cellMax = (int2)floor(mx / gp.lakeCell);
            int cellsX = cellMax.x - cellMin.x + 1;
            int cellsZ = cellMax.y - cellMin.y + 1;

            field.lakes = new NativeArray<LakeBody>(cellsX * cellsZ, allocator);

            // Buňky skalních bran překrývající výřez, včetně dosahu tělesa brány.
            float archReach = gp.archRadius * 1.35f + gp.archTube * 1.35f + 14f;
            int2 aMin = (int2)floor((origin - archReach) / gp.archCell);
            int2 aMax = (int2)floor((origin + extent + archReach) / gp.archCell);
            field.archCellMin = aMin;
            field.archCellsX = aMax.x - aMin.x + 1;
            field.archCellsZ = aMax.y - aMin.y + 1;
            field.arches = new NativeArray<ArchBody>(field.archCellsX * field.archCellsZ, allocator);

            var macroJob = new MacroFieldJob
            {
                origin = origin, step = step, side = side, gp = gp, spl = spl,
                macroY = field.macroY, shape = field.shape, cont = field.cont,
                eros = field.eros, pv = field.pv, temp = field.temp, hum = field.hum,
            };

            var lakeJob = new LakeJob
            {
                cellMin = cellMin, cellsX = cellsX, gp = gp, spl = spl,
                lakes = field.lakes,
            };

            var surfJob = new SurfaceFieldJob
            {
                origin = origin, step = step, side = side, gp = gp, spl = spl,
                macroY = field.macroY, shape = field.shape, cont = field.cont,
                eros = field.eros, pv = field.pv,
                temp = field.temp, hum = field.hum,
                hydro = hydro,
                lakes = field.lakes, lakeCellMin = cellMin,
                lakeCellsX = cellsX, lakeCellsZ = cellsZ,
                surfY = field.surfY, slope = field.slope, cliff = field.cliff,
                riverCore = field.riverCore, riverY = field.riverY, lakeY = field.lakeY,
                flowDir = field.flowDir, flowSlope = field.flowSlope,
                shoreY = field.shoreY,
                waterGate = field.waterGate,
                overhang = field.overhang,
                seamLine = seamLine,
                crater = CraterMath.ForBox(origin, origin + extent, gp, spl),   // kolo 29: jednou na výřez
            };

            var archJob = new ArchJob
            {
                cellMin = aMin, cellsX = field.archCellsX, gp = gp, spl = spl,
                arches = field.arches,
            };

            var boundsJob = new BoundsJob { surfY = field.surfY, bounds = field.bounds };

            // Makro a jezera čtou stejné spline LUT. Řetězí se za sebe záměrně:
            // paralelní běh dvou jobů nad týmiž NativeArray je sice read-only legální,
            // ale safety systém to hlídá přes vnořené struktury a zbytečně se s tím pere.
            JobHandle hMacro = macroJob.Schedule(n, 64);
            JobHandle hLakes = lakeJob.Schedule(cellsX * cellsZ, 8, hMacro);
            JobHandle hArch  = archJob.Schedule(field.archCellsX * field.archCellsZ, 8, hLakes);
            JobHandle hSurf  = surfJob.Schedule(n, 64, hArch);

            // Šití LOD švů: výška na hranicích LOD0 sloupců se srovná na lomenou čáru
            // s uzly po LodSeamJob.KnotCells metrech – tu umí přesně zopakovat každý LOD
            // do LOD2, takže se terén na hranici prstenců potká bez schodu.
            if (seamLine > 0f)
            {
                var src = new NativeArray<float>(n, allocator == Allocator.Temp ? Allocator.TempJob : allocator,
                                                NativeArrayOptions.UninitializedMemory);
                var copy = new CopyFloatJob { src = field.surfY, dst = src }.Schedule(hSurf);
                hSurf = new LodSeamJob
                {
                    origin = origin, step = step, side = side, seamLine = seamLine,
                    fineMask = fineMask, fineLine0 = fineLine0,
                    src = src, surfY = field.surfY,
                }.Schedule(n, 64, copy);
                // Kolo 24: LOD0 si nechá výšku před šitím – osazení se pak drží přirozeného
                // povrchu, i když sloupec zrovna sousedí s hrubším LOD (viz SurfaceRaster.SetSeamDelta).
                if (keepRaw) field.surfRaw = src;
                else hSurf = src.Dispose(hSurf);
            }

            handle = boundsJob.Schedule(hSurf);

            return field;
        }

        /// <summary>Synchronní varianta – naplánuje a rovnou dokončí. Pro editor a jednorázové dotazy.</summary>
        public static ColumnField Generate(float2 origin, float step, int side,
                                           in GenParams gp, in SplineSet spl,
                                           in HydroRegionRef hydro, Allocator allocator)
        {
            var field = Schedule(origin, step, side, gp, spl, hydro, allocator, out JobHandle handle);
            handle.Complete();
            return field;
        }

        /// <summary>
        /// Výška povrchu v jednom bodě bez generování celého sloupce.
        /// Používá stejnou matematiku jako joby, jen sklon a jezera počítá analyticky.
        /// Určeno pro spawn hráče a osazení objektů, ne pro hromadné dotazy.
        ///
        /// <para><b>Bez platného regionu vrací terén BEZ řek.</b> Je to schválně: tahle cesta
        /// se volá i ve spirálách o stovkách tisíc vzorků (hledání souše, hledání biomu) a
        /// dostavovat kvůli každému vzorku region by znamenalo vteřinové záseky. Rozdíl je
        /// nanejvýš hloubka koryta, což pro spawn ani osazení nic neznamená.</para>
        /// </summary>
        public static float SampleSurfaceY(float2 p, in GenParams gp, in SplineSet spl,
                                           in HydroRegionRef hydro)
        {
            WorldGenMath.EvalMacro(p, gp, spl, out float my, out float sh,
                                   out float c, out float e, out float v);

            float slope = WorldGenMath.MacroSlopeAt(p, max(1f, gp.lakeCell * 0.01f), gp, spl);

            float basin = 0f, waterY = ColumnField.NoLake;
            int2 cc = (int2)floor(p / gp.lakeCell);
            for (int oz = -1; oz <= 1; oz++)
            for (int ox = -1; ox <= 1; ox++)
            {
                LakeBody lb = WorldGenMath.EvalLakeCell(cc + new int2(ox, oz), gp, spl);
                float b = WorldGenMath.LakeBasin(p, lb, gp);
                if (b > basin) { basin = b; waterY = lb.waterY; }
            }

            HydroFlow flow = HydroSampler.Nearest(p, hydro, gp.riverMinAccum,
                                                  gp.riverWidthMin, gp.riverWidthMax,
                                                  gp.riverAccumFull);

            WorldGenMath.Climate(p, gp, out float temp, out float hum);

            // Bodový dotaz je vždycky v plném detailu – používá se pro spawn a osazení,
            // kde je potřeba ta výška, na které hráč a objekty opravdu stojí.
            return WorldGenMath.EvalSurface(p, gp, my, sh, c, e, v, slope, temp, hum, 1f,
                                            flow, waterY, basin,
                                            out _, out _, out _, out _, CraterMath.ForPoint(p, gp, spl));
        }

        private static NativeArray<float> Alloc(int n, Allocator a)
            => new NativeArray<float>(n, a, NativeArrayOptions.UninitializedMemory);
    }
}
