using System;
using Orivilon.World.Generation.Hydro;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using static Unity.Mathematics.math;

namespace Orivilon.World.Generation
{
    /// <summary>
    /// Staví geometrii chunků ze sloupcových polí.
    ///
    /// Rozhraní je rozdělené na tři kroky – sloupec, klasifikace, chunk – a každý má
    /// asynchronní i synchronní variantu. Streamer používá ty asynchronní a job handly
    /// dokončuje až v dalším snímku; <see cref="VoxelPreview"/> a jednorázové dotazy
    /// používají synchronní obaly.
    ///
    /// Pracovní pole (hustota a mapa hran) jsou v poolu slotů. Bez toho by se na každý
    /// chunk alokovalo 575 kB nativní paměti, což je největší skrytá cena celého systému.
    /// Počet slotů zároveň omezuje, kolik chunků může být rozpracovaných naráz.
    /// </summary>
    public sealed class VoxelChunkBuilder : IDisposable
    {
        public float voxelSize;
        public float edgeSnap = 0.06f;
        public TerrainPalette palette = TerrainPalette.Default;

        /// <summary>Hloubka sukně v buňkách tohoto LOD. Nula = bez sukní.</summary>
        public float skirtCells = 3f;

        /// <summary>
        /// Úpravy hráče. Přiřazuje se jen nejjemnějšímu LOD – v hrubších by se tříметrová
        /// díra beztak neprojevila a mipmapování delt by stálo víc, než přinese.
        /// </summary>
        public VoxelEdits edits;

        /// <summary>Úroveň detailu. 0 = plné rozlišení, každá další má dvojnásobný voxel.</summary>
        public int Lod { get; }

        private GenParams gp;
        private SplineSet spl;
        private NativeArray<float>[] density;
        private NativeArray<int>[] edgeVertex;
        private bool[] slotBusy;
        private int probeMinChunkY, probeCount;
        private bool disposed;

        /// <summary>
        /// Okraj sloupcové mřížky ve voxelech. 3D warp se ptá na výšku až overhangAmp metrů
        /// mimo chunk, takže bez okraje by u hranic chunků vznikaly škvíry.
        /// </summary>
        public int ColumnPad { get; private set; }

        /// <summary>Hrana sloupcové mřížky včetně okraje.</summary>
        public int ColumnSide => VoxelWorld.SampleDim + 2 * ColumnPad;

        /// <summary>Délka pole hrubého jeskynního testu = počet svislých chunků světa.</summary>
        public int ProbeCount => probeCount;

        public int ScratchSlots => slotBusy.Length;
        public GenParams Params => gp;

        /// <summary>Kolo 7: jen pro A/B měření – zapne/vypne práh u hydrologického schodu pro další stavby.</summary>
        public void SetHydroSill(bool on) => gp.hydroSill = on ? 1f : 0f;

        /// <summary>Kolo 11: jen pro A/B – plynulý směr toku u soutoků pro další stavby.</summary>
        public void SetFlowBlend(bool on) => gp.flowBlend = on ? 1f : 0f;

        /// <summary>Kolo 12: jen pro A/B – měkké stupně slabého terasování pro další stavby.</summary>
        public void SetSoftTerrace(bool on) => gp.softTerrace = on ? 1f : 0f;

        /// <summary>Kolo 12b: jen pro A/B – klidný povrchový 3D detail pro další stavby (volitelně ladění).</summary>
        public void SetCalmDetail(bool on, float yStretch = -1f, int ovOct = -1, int sqOct = -1, int mask = -1)
        {
            gp.calmDetail = on ? 1f : 0f;
            if (mask >= 0) gp.calmMask = mask;
            if (yStretch > 0f) gp.overhangYStretch = yStretch;
            if (ovOct > 0) gp.overhangOct = ovOct;
            if (sqOct > 0) gp.squishOct = sqOct;
        }

        private bool detailDiagInit;
        private float baseRidgeAmp, baseGrainAmp, baseOverhangAmp, baseSquishAmp, baseCave, baseCliff;

        /// <summary>
        /// Kolo 12b, jen diagnostika (<c>/pruhy detail r g o s</c>): násobiče povrchových
        /// deformací – ridged detail, grain, převisový warp, povrchové boule. 1 = beze změny.
        /// </summary>
        public void SetDetailDiag(float ridge, float grain, float overhang, float squish, float cave = 1f, float terrace = 1f)
        {
            if (!detailDiagInit)
            {
                baseRidgeAmp = gp.ridgeAmp; baseGrainAmp = gp.grainAmp;
                baseOverhangAmp = gp.overhangAmp; baseSquishAmp = gp.squishAmp;
                baseCave = gp.caveStrength; baseCliff = gp.cliffStrength;
                detailDiagInit = true;
            }
            gp.ridgeAmp = baseRidgeAmp * ridge; gp.grainAmp = baseGrainAmp * grain;
            gp.overhangAmp = baseOverhangAmp * overhang; gp.squishAmp = baseSquishAmp * squish;
            gp.caveStrength = baseCave * cave; gp.cliffStrength = baseCliff * terrace;
        }
        public SplineSet Splines => spl;

        /// <summary>Šířka chunku ve světových jednotkách.</summary>
        public float ChunkSpan => VoxelWorld.ChunkDim * voxelSize;

        public VoxelChunkBuilder(WorldGenSettings settings, int seed, float voxelSize,
                                 int scratchSlots = 4, int lod = 0)
        {
            if (settings == null) throw new ArgumentNullException(nameof(settings));

            Lod = max(0, lod);
            this.voxelSize = max(0.05f, voxelSize);
            gp = settings.ToParams(seed);
            ApplyLodAttenuation(ref gp, Lod);
            spl = settings.BuildSplines(Allocator.Persistent);

            MarchingCubesTables.EnsureLoaded();

            ColumnPad = (int)ceil(gp.overhangAmp / this.voxelSize) + 1;

            VoxelWorld.ChunkYRange(this.voxelSize, out int wMin, out int wMax);
            probeMinChunkY = wMin;
            probeCount = wMax - wMin + 1;

            int slots = clamp(scratchSlots, 1, 16);
            int n = VoxelWorld.SampleDim * VoxelWorld.SampleDim * VoxelWorld.SampleDim;

            density = new NativeArray<float>[slots];
            edgeVertex = new NativeArray<int>[slots];
            slotBusy = new bool[slots];

            for (int i = 0; i < slots; i++)
            {
                density[i] = new NativeArray<float>(n, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
                edgeVertex[i] = new NativeArray<int>(n * 3, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
            }
        }

        /// <summary>
        /// Zeslabí členy, které v hrubém LOD nedávají smysl.
        ///
        /// <para>Výškové pole se NEDOTÝKÁ: <c>surfY</c> je tatáž analytická funkce a jen se
        /// vzorkuje řidčeji, takže mezi LODy vyjde skoro totéž. Problém dělají 3D členy –
        /// povrchový detail má periodu 26 m a při osmimetrovém voxelu se z něj stane šum,
        /// který mezi prstenci bliká.</para>
        ///
        /// <para>Jeskyně padají hned od LOD1. Nejde o vzhled, ale o cenu: se zapnutými
        /// jeskyněmi přestane prořezávání označovat podzemní chunky za plné a musely by se
        /// meshovat celé sloupce hmoty, kterou stejně není zvenčí vidět.</para>
        /// </summary>
        private static void ApplyLodAttenuation(ref GenParams gp, int lod)
        {
            if (lod <= 0) return;

            gp.caveStrength = 0f;

            if (lod >= 2)
            {
                gp.squishAmp *= 0.35f;
                gp.overhangAmp *= 0.35f;
                gp.archNoise *= 0.35f;
            }

            if (lod >= 3)
            {
                gp.squishAmp = 0f;
                gp.overhangAmp = 0f;
                gp.archChance = 0f;
            }
        }

        // ── sloupec ────────────────────────────────────────────────────

        /// <summary>Světová pozice rohu chunkového sloupce (bez okraje).</summary>
        public float2 ColumnCorner(int2 columnCoord) => new float2(columnCoord) * ChunkSpan;

        /// <summary>Světová pozice sloupcového vzorku (0,0), tedy včetně okraje.</summary>
        public float2 ColumnOrigin(int2 columnCoord) => ColumnCorner(columnCoord) - ColumnPad * voxelSize;

        /// <summary>Pole pro hrubý jeskynní test. Jedno na sloupec, volající ho Dispose()uje.</summary>
        public NativeArray<float> AllocProbe(Allocator allocator)
            => new NativeArray<float>(probeCount, allocator);

        /// <summary>
        /// Naplánuje sloupcové pole i hrubý jeskynní test. Vrácený handle pokrývá obojí;
        /// obsah polí je platný až po jeho dokončení, ale <c>field.origin</c> a <c>field.side</c>
        /// jsou vyplněné hned.
        /// </summary>
        public JobHandle ScheduleColumn(int2 columnCoord, Allocator allocator,
                                        NativeArray<float> probe, in HydroRegionRef hydro,
                                        out ColumnField field)
            => ScheduleColumn(columnCoord, allocator, probe, hydro, 0, out field);

        /// <param name="fineEdges">Kolo 24: jen LOD0 – hrany s LOD0 sousedem (WaterSurface.Edge*),
        /// na kterých se terén nešije. LOD0 sloupec je široký přesně jednu rozteč švu, takže
        /// index přímky jeho hrany X0/Z0 je přímo souřadnice sloupce.</param>
        public JobHandle ScheduleColumn(int2 columnCoord, Allocator allocator,
                                        NativeArray<float> probe, in HydroRegionRef hydro,
                                        int fineEdges, out ColumnField field)
        {
            // Hranice LOD0 sloupců: ChunkDim vzorků nejjemnějšího voxelu.
            float seamLine = BenchNoSeam ? 0f : VoxelWorld.ChunkDim * voxelSize / (1 << Lod);
            bool lod0 = Lod == 0 && seamLine > 0f && allocator == Allocator.Persistent;
            field = WorldGen.Schedule(ColumnOrigin(columnCoord), voxelSize, ColumnSide,
                                      gp, spl, hydro, allocator, seamLine,
                                      lod0 ? fineEdges : 0, columnCoord, lod0, out JobHandle handle);

            return new CaveProbeJob
            {
                steps = 9,
                minChunkY = probeMinChunkY,
                chunkSpan = ChunkSpan,
                chunkOriginXZ = ColumnCorner(columnCoord),
                columnOrigin = field.origin,
                columnSide = field.side,
                voxelSize = voxelSize,
                gp = gp,
                surfY = field.surfY,
                maxCarve = probe,
            }.Schedule(probeCount, 2, handle);
        }

        /// <summary>Jen pro měření (/teren bench): vypne dorovnání LOD švu.</summary>
        public static bool BenchNoSeam;

        /// <summary>Jen pro měření (/teren snap legacy): přichytávání vrcholů i na stěnách chunku.</summary>
        public static bool SnapWallsLegacy;

        /// <summary>Jen pro měření (/teren sevfall off): bez rozšířeného pásu hustoty na švech.</summary>
        public static bool NoSeamFalloff;

        /// <summary>Synchronní varianta pro editor a jednorázové dotazy.</summary>
        public ColumnField BuildColumn(int2 columnCoord, Allocator allocator,
                                       NativeArray<float> probe, in HydroRegionRef hydro)
        {
            ScheduleColumn(columnCoord, allocator, probe, hydro, out ColumnField field).Complete();
            return field;
        }

        /// <summary>Světový střed sloupce – tím se vybírá hydrologický region.</summary>
        public float2 ColumnCenter(int2 columnCoord)
            => (new float2(columnCoord) + 0.5f) * ChunkSpan;

        /// <summary>
        /// Svislý rozsah chunků, které má smysl vůbec uvážit. Sahá až k podloží, protože
        /// jeskyně jsou i hluboko pod povrchem – teprve Classify z toho vybere ty duté.
        /// </summary>
        public void ColumnChunkRange(int2 columnCoord, in ColumnField column,
                                     out int minChunkY, out int maxChunkY)
        {
            VoxelWorld.ChunkYRange(voxelSize, out int worldMin, out int worldMax);

            // Pod caveMaxDepth se už nic neřeže (viz Density3D.CaveCarve), takže tam
            // nemůže vzniknout izoplocha a ty chunky se nemusí ani uvažovat. Bez tohohle
            // by prořezávání muselo projít každý svislý chunk až k podloží a hlubší svět
            // by stál lineárně víc za geometrii, kterou nikdo neuvidí.
            float caveFloor = column.MinHeight - gp.caveMaxDepth - ChunkSpan;
            minChunkY = clamp((int)floor(caveFloor / ChunkSpan), worldMin, worldMax);
            maxChunkY = clamp((int)floor((column.MaxHeight + AboveMargin) / ChunkSpan), worldMin, worldMax);

            // Přisypaný kopec může sahat nad původní terén – bez tohohle by se ořízl.
            if (edits != null && edits.ColumnRange(columnCoord, out int eMin, out int eMax))
            {
                minChunkY = clamp(min(minChunkY, eMin), worldMin, worldMax);
                maxChunkY = clamp(max(maxChunkY, eMax), worldMin, worldMax);
            }
        }

        /// <summary>
        /// Nad povrchem může ještě něco být: převis posune hmotu nahoru a skalní brána
        /// stojí sama o sobě. Bez téhle rezervy by se jim uřízl vršek.
        /// </summary>
        private float AboveMargin => gp.overhangAmp + gp.archRadius * 1.35f + gp.archTube * 1.35f + 14f;

        /// <summary>
        /// Levné rozhodnutí, jestli chunk vůbec obsahuje izoplochu – bez generování hustoty.
        /// Tohle prořezání je důvod, proč je plný voxel únosný.
        /// </summary>
        /// <summary>
        /// Leží chunk celý pod povrchem? Takový je vidět jen zevnitř jeskyně, takže se mu
        /// sukně nedává – visela by ze stropu jako záclona přes chodbu.
        /// </summary>
        public bool IsUnderground(in ColumnField column, int chunkY)
            => (chunkY + 1) * ChunkSpan < column.MinHeight - voxelSize;

        public ChunkOccupancy Classify(int2 columnCoord, in ColumnField column, int chunkY,
                                       NativeArray<float> probe)
        {
            // Upravený chunk se nikdy neprořezává. Odkopaná dutina pod povrchem by se jinak
            // označila za plný kámen a nikdy by se nezmeshovala.
            if (edits != null && edits.Has(new int3(columnCoord.x, chunkY, columnCoord.y)))
                return ChunkOccupancy.Surface;

            float minY = chunkY * ChunkSpan;
            float maxY = minY + ChunkSpan;

            if (minY > column.MaxHeight + AboveMargin) return ChunkOccupancy.Empty;

            if (maxY < column.MinHeight - voxelSize)
            {
                // Práh musí odpovídat tomu, kolik řezu vůbec otevře dutinu: odečítá se
                // SMin(d, 0.9 - carve*2.2), takže hustota zajde do záporu až od carve ≈ 0.41.
                int i = chunkY - probeMinChunkY;
                float carve = (probe.IsCreated && i >= 0 && i < probeCount) ? probe[i] : 1f;
                if (carve < 0.30f) return ChunkOccupancy.Solid;
            }

            return ChunkOccupancy.Surface;
        }

        // ── pracovní sloty ─────────────────────────────────────────────

        /// <summary>Zabere pracovní slot, nebo vrátí -1 když jsou všechny obsazené.</summary>
        public int RentScratch()
        {
            for (int i = 0; i < slotBusy.Length; i++)
                if (!slotBusy[i]) { slotBusy[i] = true; return i; }
            return -1;
        }

        /// <summary>Uvolní slot. Volat až po dokončení jobu, který ho používal.</summary>
        public void ReturnScratch(int slot)
        {
            if (slot >= 0 && slot < slotBusy.Length) slotBusy[slot] = false;
        }

        // ── chunk ──────────────────────────────────────────────────────

        /// <summary>
        /// Naplánuje hustotu a marching cubes. Slot musí být zabraný přes RentScratch
        /// a smí se vrátit až po dokončení vráceného handle.
        /// </summary>
        public JobHandle ScheduleChunk(int3 coord, in ColumnField column, int slot,
                                       Allocator allocator, out ChunkMeshData mesh, JobHandle dependency,
                                       bool skirt = true)
        {
            mesh = ChunkMeshData.Create(coord, allocator);

            int dim = VoxelWorld.SampleDim;
            int n = dim * dim * dim;
            float3 origin = VoxelWorld.ChunkOrigin(coord, voxelSize);

            var densityJob = MakeDensityJob(coord, column, slot);

            var resetJob = new ResetEdgeMapJob { edgeVertex = edgeVertex[slot] };

            var mcJob = new MarchingCubesJob
            {
                dim = dim,
                voxelSize = voxelSize,
                chunkOrigin = origin,
                edgeSnap = clamp(edgeSnap, 0f, 0.24f),
                snapWalls = SnapWallsLegacy,
                skirtDepth = skirt ? max(0f, skirtCells) * voxelSize : 0f,
                palette = palette,
                microOffset = gp.offMicro,
                micro = gp.Micro,
                seaLevel = gp.seaLevel,
                columnTemp = column.temp,
                columnHum = column.hum,
                columnShoreY = column.shoreY,
                columnCliff = column.cliff,
                columnOrigin = column.origin,
                columnStep = voxelSize,
                columnSide = column.side,
                density = density[slot],
                triTable = MarchingCubesTables.Tri,
                edgeOwner = MarchingCubesTables.Owner,
                edgeCorners = MarchingCubesTables.Corners,
                cornerOffset = MarchingCubesTables.CornerOffsets,
                edgeVertex = edgeVertex[slot],
                solidVerts = mesh.solidVerts,
                solidTris = mesh.solidTris,
                renderVerts = mesh.renderVerts,
                renderNormals = mesh.renderNormals,
                renderColors = mesh.renderColors,
                renderTris = mesh.renderTris,
            };

            JobHandle hDensity = densityJob.Schedule(n, 256, dependency);
            JobHandle hReset = resetJob.Schedule(n * 3, 1024, dependency);
            JobHandle hMc = mcJob.Schedule(JobHandle.CombineDependencies(hDensity, hReset));
            MarchingCubesTables.TrackReader(hMc);   // kolo 25: Dispose při Stop Play počká na tento job
            return hMc;
        }

        private DensityJob MakeDensityJob(int3 coord, in ColumnField column, int slot)
        {
            NativeArray<sbyte> delta;
            bool hasDelta;

            if (edits != null) delta = edits.Get(coord, out hasDelta);
            else delta = NoEdits(out hasDelta);

            return MakeDensityJob(coord, column, slot, delta, hasDelta);
        }

        /// <summary>Jednoprvkové pole pro buildery bez úprav. Job musí dostat platnou kolekci.</summary>
        private NativeArray<sbyte> noEdits;

        private NativeArray<sbyte> NoEdits(out bool has)
        {
            has = false;
            if (!noEdits.IsCreated) noEdits = new NativeArray<sbyte>(1, Allocator.Persistent);
            return noEdits;
        }

        private DensityJob MakeDensityJob(int3 coord, in ColumnField column, int slot,
                                          NativeArray<sbyte> delta, bool hasDelta) => new DensityJob
        {
            dim = VoxelWorld.SampleDim,
            columnSide = column.side,
            columnPad = ColumnPad,
            columnOrigin = column.origin,
            voxelSize = voxelSize,
            chunkOrigin = VoxelWorld.ChunkOrigin(coord, voxelSize),
            falloff = VoxelWorld.Falloff,
            seamLine = BenchNoSeam || NoSeamFalloff ? 0f : VoxelWorld.ChunkDim * voxelSize / (1 << Lod),
            worldMinY = VoxelWorld.WorldMinY,
            worldMaxY = VoxelWorld.WorldMaxY,
            gp = gp,
            surfY = column.surfY,
            overhang = column.overhang,
            waterGate = column.waterGate,
            arches = column.arches,
            archCellMin = column.archCellMin,
            archCellsX = column.archCellsX,
            archCellsZ = column.archCellsZ,
            delta = delta,
            hasDelta = hasDelta,
            density = density[slot],
        };

        /// <summary>Statistika průchodnosti dutin, změřená na skutečném poli hustoty.</summary>
        public struct CaveStats
        {
            /// <summary>Podíl vzduchu v objemu chunku.</summary>
            public float openFraction;

            /// <summary>Průměrná a devadesátý percentil souvislého vzduchu vodorovně, v metrech.</summary>
            public float meanHorizontal, p90Horizontal;

            /// <summary>Průměrná svislá světlost – zhruba výška stropu, v metrech.</summary>
            public float meanVertical;

            public int runs;

            /// <summary>Podíl vzduchu, za který může daný generátor. Součet je 1.</summary>
            public float shareTunnel, shareHall, shareRavine;
        }

        /// <summary>
        /// Změří, jak široké dutiny doopravdy jsou.
        ///
        /// Odhadovat šířku z prahu v jednotkách šumu je nespolehlivé – závisí na gradientu
        /// fbm, počtu oktáv i na tom, od jaké hodnoty řezu se hustota vůbec překlopí do
        /// záporu. Tohle měří výsledek: projde hotové pole hustoty a spočítá souvislé úseky
        /// vzduchu. Úseky dotýkající se okraje chunku se nezapočítávají, aby se šířka
        /// nepodstřelila oříznutím.
        /// </summary>
        public CaveStats MeasureCaves(int3 coord, in ColumnField column)
        {
            int slot = RentScratch();
            if (slot < 0) slot = 0;

            int dim = VoxelWorld.SampleDim;
            MakeDensityJob(coord, column, slot).Schedule(dim * dim * dim, 256).Complete();

            NativeArray<float> d = density[slot];
            var hist = new NativeArray<int>(dim + 1, Allocator.Temp);

            int open = 0, runs = 0, sumH = 0, runsV = 0, sumV = 0;

            for (int z = 0; z < dim; z++)
            for (int y = 0; y < dim; y++)
            {
                int run = 0;
                for (int x = 0; x < dim; x++)
                {
                    bool air = d[(z * dim + y) * dim + x] < 0f;
                    if (air) { open++; run++; }
                    if ((!air || x == dim - 1) && run > 0)
                    {
                        bool touchesEdge = (x - run) < 0 || (!air ? false : x == dim - 1);
                        if (!touchesEdge) { hist[run]++; runs++; sumH += run; }
                        run = 0;
                    }
                }
            }

            for (int z = 0; z < dim; z++)
            for (int x = 0; x < dim; x++)
            {
                int run = 0;
                for (int y = 0; y < dim; y++)
                {
                    bool air = d[(z * dim + y) * dim + x] < 0f;
                    if (air) run++;
                    if ((!air || y == dim - 1) && run > 0)
                    {
                        bool touchesEdge = (y - run) < 0 || (!air ? false : y == dim - 1);
                        if (!touchesEdge) { runsV++; sumV += run; }
                        run = 0;
                    }
                }
            }

            // Rozpad vzduchu po generátorech – bez toho se dá ladit jen naslepo.
            float3 chunkOrigin = VoxelWorld.ChunkOrigin(coord, voxelSize);
            int nt = 0, nh = 0, nr = 0;

            for (int z = 0; z < dim; z++)
            for (int y = 0; y < dim; y++)
            for (int x = 0; x < dim; x++)
            {
                if (d[(z * dim + y) * dim + x] >= 0f) continue;

                float3 w = chunkOrigin + new float3(x, y, z) * voxelSize;
                float surf = column.surfY[(z + ColumnPad) * column.side + (x + ColumnPad)];
                Density3D.CaveComponents(w, gp, surf - w.y, out float t, out float h, out float r);

                if (t >= h && t >= r) nt++;
                else if (h >= r) nh++;
                else nr++;
            }

            int attributed = max(1, nt + nh + nr);

            int target = (int)(runs * 0.9f);
            int cum = 0, p90 = 0;
            for (int i = 0; i <= dim; i++)
            {
                cum += hist[i];
                if (cum >= target) { p90 = i; break; }
            }

            hist.Dispose();
            ReturnScratch(slot);

            int n = dim * dim * dim;
            return new CaveStats
            {
                openFraction = open / (float)n,
                meanHorizontal = runs > 0 ? sumH / (float)runs * voxelSize : 0f,
                p90Horizontal = p90 * voxelSize,
                meanVertical = runsV > 0 ? sumV / (float)runsV * voxelSize : 0f,
                runs = runs,
                shareTunnel = nt / (float)attributed,
                shareHall = nh / (float)attributed,
                shareRavine = nr / (float)attributed,
            };
        }

        /// <summary>Synchronní varianta pro editor. Sama si zabere a vrátí slot.</summary>
        public ChunkMeshData BuildChunk(int3 coord, in ColumnField column, Allocator allocator)
        {
            int slot = RentScratch();
            if (slot < 0) slot = 0;   // v synchronní cestě nikdy nic neběží paralelně

            ScheduleChunk(coord, column, slot, allocator, out ChunkMeshData mesh, default).Complete();
            ReturnScratch(slot);
            return mesh;
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;

            spl.Dispose();
            if (noEdits.IsCreated) noEdits.Dispose();

            for (int i = 0; i < density.Length; i++)
            {
                if (density[i].IsCreated) density[i].Dispose();
                if (edgeVertex[i].IsCreated) edgeVertex[i].Dispose();
            }
        }
    }
}
