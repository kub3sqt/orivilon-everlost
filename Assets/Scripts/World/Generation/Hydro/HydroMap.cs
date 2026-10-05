using System;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;

namespace Orivilon.World.Generation.Hydro
{
    /// <summary>
    /// Správce regionálních hydrologických map. Drží hotové regiony, staví chybějící
    /// na pozadí a odpovídá na dotazy „kde je odsud koryto".
    ///
    /// <para>Není to MonoBehaviour – vlastní ho streamer terénu, protože jeho životnost
    /// se musí krýt s životností nativních polí generátoru, ne s objektem ve scéně.</para>
    ///
    /// <para><b>Předgenerování.</b> Region se vyžádá, jakmile se hráč přiblíží k jeho hraně
    /// na <see cref="prefetchDistance"/>. Stavba trvá desítky milisekund a jde přes joby
    /// napříč snímky, takže hráč nikdy nečeká – při 10 m/s má náskok přes dvě minuty.</para>
    /// </summary>
    public sealed class HydroMap : IDisposable
    {
        /// <summary>Jak daleko před hranou regionu se začne stavět soused, v metrech.</summary>
        public float prefetchDistance = 1500f;

        /// <summary>Kolik hotových regionů se drží. Devět = hráč uprostřed a všech osm sousedů.</summary>
        public int maxRegions = 9;

        /// <summary>
        /// Kolik regionů se smí stavět naráz. Pracovní pole jednoho staveniště zabírají
        /// kolem 4,4 MB, takže jedno naráz je záměrné – ne úspora řádků.
        /// </summary>
        public int maxConcurrent = 1;

        private sealed class Site
        {
            public int2 coord;
            public JobHandle handle;

            public NativeArray<float> raw, filled, accum, heapKey;
            public NativeArray<int> parent, order, heapVal;
            public NativeArray<byte> state, dir;

            public HydroRegion result;
        }

        private readonly Dictionary<int2, HydroRegion> regions = new Dictionary<int2, HydroRegion>();
        private readonly List<Site> sites = new List<Site>();
        private readonly List<int2> scratch = new List<int2>();

        private GenParams gp;
        private SplineSet spl;
        private bool disposed;

        /// <summary>Kolik regionů je hotových. Pro měření.</summary>
        public int ReadyCount => regions.Count;

        /// <summary>Kolik se jich právě staví.</summary>
        public int BuildingCount => sites.Count;

        public HydroMap(GenParams genParams, SplineSet splines)
        {
            gp = genParams;
            spl = splines;
        }

        // ── údržba ─────────────────────────────────────────────────────

        /// <summary>
        /// Volá se jednou za snímek. Sbírá hotové, zadává chybějící, zahazuje daleké.
        /// </summary>
        /// <param name="allowEvict">
        /// Smí se uvolňovat? <b>Musí být false, dokud běží jakýkoli job, který z regionů čte.</b>
        /// Pole regionu drží desítky sloupcových jobů naráz a <c>Dispose</c> nad polem, které
        /// právě čte naplánovaný job, Unity odmítne výjimkou. Streamer to pozná podle toho,
        /// jestli má nějaký sloupec rozpracovaný.
        /// </param>
        public void Update(float2 viewer, bool allowEvict = true)
        {
            CollectFinished();
            RequestAround(viewer);
            if (allowEvict) EvictFar(viewer);
        }

        private void CollectFinished()
        {
            for (int i = sites.Count - 1; i >= 0; i--)
            {
                Site s = sites[i];
                if (!s.handle.IsCompleted) continue;

                s.handle.Complete();

                // Kdyby se region mezitím objevil jinou cestou, ten nový se zahodí –
                // dvě kopie téhož regionu by byly jen dvojí paměť, obsah je shodný.
                if (regions.ContainsKey(s.coord)) s.result.Dispose();
                else regions[s.coord] = s.result;

                ReleaseScratch(s);
                sites.RemoveAt(i);
            }
        }

        private void RequestAround(float2 viewer)
        {
            int2 home = HydroWorld.RegionOf(viewer);

            for (int dy = -1; dy <= 1; dy++)
            for (int dx = -1; dx <= 1; dx++)
            {
                if (sites.Count >= maxConcurrent) return;

                var c = home + new int2(dx, dy);
                if (regions.ContainsKey(c) || IsBuilding(c)) continue;

                // Vlastní region se staví vždycky, sousedi až když je hráč blízko hraně.
                if ((dx != 0 || dy != 0) && DistanceTo(c, viewer) > prefetchDistance) continue;

                Begin(c);
            }
        }

        /// <summary>
        /// Vyžádá region pro daný bod, pokud ještě není a je místo ve frontě.
        ///
        /// <para>Předgenerování kolem hráče samo nestačí: dohled terénu může být větší než
        /// jeden region a čekat na sloupec, o jehož region si nikdo neřekl, by znamenalo
        /// loading screen, který nikdy neskončí. Streamer proto při každém přeskočeném
        /// sloupci region rovnou vyžádá – a protože jde přes frontu žádaných sloupců
        /// seřazenou podle vzdálenosti, staví se regiony od hráče ven.</para>
        /// </summary>
        public void Request(float2 world)
        {
            int2 c = HydroWorld.RegionOf(world);
            if (regions.ContainsKey(c) || IsBuilding(c)) return;
            if (sites.Count >= maxConcurrent) return;

            Begin(c);
        }

        private void EvictFar(float2 viewer)
        {
            if (regions.Count <= maxRegions) return;

            int2 home = HydroWorld.RegionOf(viewer);

            scratch.Clear();
            foreach (var kv in regions)
            {
                int2 c = kv.Key;
                if (abs(c.x - home.x) <= 1 && abs(c.y - home.y) <= 1) continue;
                scratch.Add(c);
            }

            for (int i = 0; i < scratch.Count && regions.Count > maxRegions; i++)
            {
                regions[scratch[i]].Dispose();
                regions.Remove(scratch[i]);
            }
        }

        private static int abs(int v) => v < 0 ? -v : v;

        private bool IsBuilding(int2 c)
        {
            for (int i = 0; i < sites.Count; i++) if (sites[i].coord.Equals(c)) return true;
            return false;
        }

        /// <summary>Vzdálenost bodu od obdélníku regionu v metrech. Nula uvnitř.</summary>
        private static float DistanceTo(int2 region, float2 p)
        {
            float2 lo = (float2)region * HydroWorld.RegionSize;
            float2 hi = lo + HydroWorld.RegionSize;
            float2 d = math.max(math.max(lo - p, p - hi), 0f);
            return math.length(d);
        }

        // ── stavba ─────────────────────────────────────────────────────

        private void Begin(int2 coord)
        {
            var s = new Site { coord = coord };

            int side = HydroWorld.ArrayCells;
            int n = side * side;
            int m = HydroWorld.StoredCells * HydroWorld.StoredCells;

            var un = NativeArrayOptions.UninitializedMemory;

            s.raw     = new NativeArray<float>(n, Allocator.Persistent, un);
            s.filled  = new NativeArray<float>(n, Allocator.Persistent, un);
            s.accum   = new NativeArray<float>(n, Allocator.Persistent, un);
            s.heapKey = new NativeArray<float>(n, Allocator.Persistent, un);
            s.parent  = new NativeArray<int>(n, Allocator.Persistent, un);
            s.order   = new NativeArray<int>(n, Allocator.Persistent, un);
            s.heapVal = new NativeArray<int>(n, Allocator.Persistent, un);
            s.state   = new NativeArray<byte>(n, Allocator.Persistent, un);
            s.dir     = new NativeArray<byte>(n, Allocator.Persistent, un);

            s.result = new HydroRegion
            {
                coord = coord,
                origin = HydroWorld.StoredOrigin(coord),
                filled = new NativeArray<float>(m, Allocator.Persistent, un),
                raw    = new NativeArray<float>(m, Allocator.Persistent, un),
                accum  = new NativeArray<float>(m, Allocator.Persistent, un),
                dir    = new NativeArray<byte>(m, Allocator.Persistent, un),
            };

            JobHandle h = new HydroHeightJob
            {
                origin = HydroWorld.ArrayOrigin(coord),
                cell = HydroWorld.CellSize,
                side = side,
                gp = gp,
                spl = spl,
                raw = s.raw,
            }.Schedule(n, 256);

            h = new HydroFloodJob
            {
                side = side,
                seaLevel = gp.seaLevel,
                raw = s.raw,
                filled = s.filled,
                parent = s.parent,
                order = s.order,
                state = s.state,
                heapKey = s.heapKey,
                heapVal = s.heapVal,
            }.Schedule(h);

            h = new HydroDirJob
            {
                side = side,
                filled = s.filled,
                parent = s.parent,
                dir = s.dir,
            }.Schedule(n, 256, h);

            h = new HydroAccumJob
            {
                side = side,
                order = s.order,
                dir = s.dir,
                accum = s.accum,
            }.Schedule(h);

            h = new HydroCopyJob
            {
                srcSide = side,
                dstSide = HydroWorld.StoredCells,
                offset = HydroWorld.Halo - HydroWorld.StoredRing,
                srcFilled = s.filled, srcRaw = s.raw, srcAccum = s.accum, srcDir = s.dir,
                dstFilled = s.result.filled, dstRaw = s.result.raw,
                dstAccum = s.result.accum, dstDir = s.result.dir,
            }.Schedule(m, 256, h);

            s.handle = h;
            sites.Add(s);
        }

        /// <summary>Postaví region synchronně. Jen pro měření a editorové nástroje.</summary>
        public HydroRegion BuildNow(int2 coord)
        {
            if (regions.TryGetValue(coord, out HydroRegion existing)) return existing;

            Begin(coord);
            Site s = sites[sites.Count - 1];
            s.handle.Complete();

            regions[coord] = s.result;
            ReleaseScratch(s);
            sites.RemoveAt(sites.Count - 1);

            return regions[coord];
        }

        /// <summary>
        /// Kolo 17: postaví region synchronně, ale NEVLOŽÍ ho mezi regiony streameru – vlastníkem
        /// je volající a musí ho uvolnit (<see cref="HydroRegion.Dispose"/>). Pro hledání daleko
        /// od hráče (konzolový teleport): obsah je bit po bitu stejný jako u regionu streameru
        /// (viz <see cref="HydroRegion"/>), ale streamer ho nevidí, takže mu nemění eviction ani paměť.
        /// </summary>
        public HydroRegion BuildDetached(int2 coord)
        {
            Begin(coord);
            Site s = sites[sites.Count - 1];
            s.handle.Complete();
            HydroRegion r = s.result;
            ReleaseScratch(s);
            sites.RemoveAt(sites.Count - 1);
            return r;
        }

        /// <summary>Kolo 17: blittable pohled na libovolný hotový region (i odpojený).</summary>
        public static HydroRegionRef RefOf(in HydroRegion r)
        {
            if (!r.IsCreated) return default;
            return new HydroRegionRef
            {
                origin = r.origin,
                side = HydroWorld.StoredCells,
                valid = 1,
                filled = r.filled,
                raw = r.raw,
                accum = r.accum,
                dir = r.dir,
            };
        }

        private static void ReleaseScratch(Site s)
        {
            if (s.raw.IsCreated) s.raw.Dispose();
            if (s.filled.IsCreated) s.filled.Dispose();
            if (s.accum.IsCreated) s.accum.Dispose();
            if (s.heapKey.IsCreated) s.heapKey.Dispose();
            if (s.parent.IsCreated) s.parent.Dispose();
            if (s.order.IsCreated) s.order.Dispose();
            if (s.heapVal.IsCreated) s.heapVal.Dispose();
            if (s.state.IsCreated) s.state.Dispose();
            if (s.dir.IsCreated) s.dir.Dispose();
        }

        // ── dotazy ─────────────────────────────────────────────────────

        /// <summary>Je region pro daný bod hotový? Sloupec se bez toho nesmí plánovat.</summary>
        public bool IsReady(float2 world) => regions.ContainsKey(HydroWorld.RegionOf(world));

        /// <summary>
        /// Blittable pohled na region obsahující daný bod. Vrací neplatný, když ještě není.
        ///
        /// <para>Uložený prstenec <see cref="HydroWorld.StoredRing"/> je právě proto, aby si
        /// jeden sloupec vystačil s jedním regionem – okno dotazu i okraj sloupcové cache
        /// se do prstence vejdou a nikdo nemusí řešit dotaz přes hranici.</para>
        /// </summary>
        public HydroRegionRef GetRef(float2 world)
        {
            if (!regions.TryGetValue(HydroWorld.RegionOf(world), out HydroRegion r))
                return default;

            return new HydroRegionRef
            {
                origin = r.origin,
                side = HydroWorld.StoredCells,
                valid = 1,
                filled = r.filled,
                raw = r.raw,
                accum = r.accum,
                dir = r.dir,
            };
        }

        /// <summary>
        /// Pohled na region daného bodu; když ještě není, dostaví ho a POČKÁ.
        /// Jen pro editorové cesty a náhled – ve streameru by to byl zásek na desítky ms.
        /// </summary>
        public HydroRegionRef GetRefBlocking(float2 world)
        {
            BuildNow(HydroWorld.RegionOf(world));
            return GetRef(world);
        }

        /// <summary>Jednorázový dotaz z hlavního vlákna. Region si v případě potřeby dostaví.</summary>
        public HydroFlow SampleBlocking(float2 world, float minAccum, float widthMin,
                                        float widthMax, float accumFull)
        {
            BuildNow(HydroWorld.RegionOf(world));
            return HydroSampler.Nearest(world, GetRef(world), minAccum, widthMin, widthMax, accumFull);
        }

        /// <summary>
        /// Sešije z regionů jeden souvislý pohled na obdélník, i když ho pokrývá víc regionů.
        ///
        /// <para>Jen pro editorové náhledy. Ve hře to není potřeba: sloupec se vždycky vejde
        /// do jednoho regionu i s prstencem, a právě proto má prstenec 20 buněk. Náhled je
        /// ale klidně osm kilometrů široký, takže by mu jeden region nestačil a řeky by
        /// v půlce mapy zmizely – což by se snadno spletlo s chybou v generátoru.</para>
        ///
        /// <para>Chybějící regiony si dostaví a POČKÁ. Výsledek musí volající uvolnit
        /// přes <see cref="DisposeStitched"/>.</para>
        /// </summary>
        public HydroRegionRef BuildStitched(float2 min, float2 max)
        {
            // Okno dotazu sahá dvě buňky od bodu, takže se přidávají tři pro jistotu.
            int2 c0 = (int2)math.floor(min / HydroWorld.CellSize) - 3;
            int2 c1 = (int2)math.floor(max / HydroWorld.CellSize) + 3;

            int side = math.max(c1.x - c0.x + 1, c1.y - c0.y + 1);
            int n = side * side;

            var r = new HydroRegionRef
            {
                origin = (float2)c0 * HydroWorld.CellSize,
                side = side,
                valid = 1,
                filled = new NativeArray<float>(n, Allocator.Persistent),
                raw = new NativeArray<float>(n, Allocator.Persistent),
                accum = new NativeArray<float>(n, Allocator.Persistent),
                dir = new NativeArray<byte>(n, Allocator.Persistent),
            };

            int stored = HydroWorld.StoredCells;

            for (int y = 0; y < side; y++)
            for (int x = 0; x < side; x++)
            {
                int2 cell = c0 + new int2(x, y);
                float2 w = ((float2)cell + 0.5f) * HydroWorld.CellSize;

                int2 rc = HydroWorld.RegionOf(w);
                if (!regions.TryGetValue(rc, out HydroRegion src)) src = BuildNow(rc);

                int2 local = cell - rc * HydroWorld.RegionCells + HydroWorld.StoredRing;
                local = math.clamp(local, 0, stored - 1);

                int si = local.y * stored + local.x;
                int di = y * side + x;

                r.filled[di] = src.filled[si];
                r.raw[di] = src.raw[si];
                r.accum[di] = src.accum[si];
                r.dir[di] = src.dir[si];
            }

            return r;
        }

        /// <summary>Uvolní pohled z <see cref="BuildStitched"/>.</summary>
        public static void DisposeStitched(ref HydroRegionRef r)
        {
            if (r.filled.IsCreated) r.filled.Dispose();
            if (r.raw.IsCreated) r.raw.Dispose();
            if (r.accum.IsCreated) r.accum.Dispose();
            if (r.dir.IsCreated) r.dir.Dispose();
            r.valid = 0;
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;

            for (int i = 0; i < sites.Count; i++)
            {
                sites[i].handle.Complete();
                sites[i].result.Dispose();
                ReleaseScratch(sites[i]);
            }
            sites.Clear();

            foreach (var kv in regions) kv.Value.Dispose();
            regions.Clear();
        }

        // ── měření ─────────────────────────────────────────────────────

        /// <summary>
        /// Změří jeden region a vrátí čitelný výpis. Bez tohohle se prahy ladí naslepo –
        /// „kolik procent souše je řeka" je jediné číslo, které o síti něco vypovídá.
        /// </summary>
        public string Measure(int2 coord, float minAccum, float widthMin, float widthMax, float accumFull)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            bool had = regions.ContainsKey(coord);
            HydroRegion r = BuildNow(coord);
            double ms = sw.Elapsed.TotalMilliseconds;

            int side = HydroWorld.StoredCells;
            int ring = HydroWorld.StoredRing;

            int land = 0, channel = 0, lake = 0, pits = 0;
            float maxAcc = 0f, sumWidth = 0f;

            for (int y = ring; y < side - ring; y++)
            for (int x = ring; x < side - ring; x++)
            {
                int i = y * side + x;
                if (r.raw[i] <= gp.seaLevel) continue;

                land++;
                if (r.filled[i] - r.raw[i] > 0.5f) lake++;
                if (r.dir[i] > 7) pits++;

                float a = r.accum[i];
                if (a > maxAcc) maxAcc = a;

                if (a >= minAccum)
                {
                    channel++;
                    sumWidth += HydroSampler.WidthFromAccum(a, widthMin, widthMax, accumFull);
                }
            }

            float pct = land > 0 ? 100f * channel / land : 0f;
            float avgW = channel > 0 ? sumWidth / channel : 0f;

            return $"[Hydro] region {coord.x}/{coord.y} ({HydroWorld.RegionSize:0} m), "
                 + $"{(had ? "z cache" : $"postaven za {ms:0} ms")}\n"
                 + $"  souš {land} buněk, koryto {channel} ({pct:0.00} %), jezerní dno {lake}, bezodtoké {pits}\n"
                 + $"  max akumulace {maxAcc:0} buněk (= {maxAcc * HydroWorld.CellSize * HydroWorld.CellSize / 1e6f:0.00} km² povodí), "
                 + $"průměrná šířka koryta {avgW:0.0} m\n"
                 + $"  paměť regionu {r.ApproxBytes / 1048576f:0.00} MB, drženo {regions.Count}";
        }
    }
}
