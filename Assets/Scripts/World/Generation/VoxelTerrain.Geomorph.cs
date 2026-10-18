using System.Collections.Generic;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;

namespace Orivilon.World.Generation
{
    /// <summary>
    /// Plynulá výměna LOD (výkonové kolo 14.10.2026) – svislý geomorph.
    ///
    /// <para>Když sloupec jiné úrovně přebírá plochu po viditelné geometrii (jemný po hrubém
    /// nebo naopak), jeho vrcholy začnou na VÝŠCE STARÉHO POVRCHU a během
    /// <see cref="GeomorphSeconds"/> se svezou do své skutečné polohy. Starý povrch se vezme
    /// z meshů, které se v tomtéž snímku schovávají (rasterizace horní plochy do mřížky).
    /// Bez úpravy shaderu a materiálu, bez průhlednosti – jen pozice vrcholů.</para>
    ///
    /// <para>Pojistky: okraj sloupce se nehýbe (náběh přes 4 buňky), takže se nesousedí
    /// s žádnou mezerou; vrchol daleko od starého povrchu (převis, jeskyně) se nehýbe;
    /// osazení a zástupci stromů čekají na konec morphu, aby nestály ve vzduchu.
    /// Paměť navíc jen pro chunky, které se právě morfují (konečné polohy + posun).</para>
    /// </summary>
    public partial class VoxelTerrain
    {
        /// <summary>Zapnutí plynulé výměny LOD (/perf morph on|off).</summary>
        public static bool GeomorphEnabled = true;

        /// <summary>Délka přechodu, s.</summary>
        public static float GeomorphSeconds = 0.6f;

        /// <summary>Čítače pro /perf: kolik chunků se morfovalo a kolik ms stál start morphů celkem.</summary>
        public static int StatMorphChunks;
        public static double StatMorphStartMs, StatMorphFrameMaxMs;
        private static int morphFrame = -1;
        private static double morphFrameMs;

        private static void ChargeMorph(long t0)
        {
            double ms = (System.Diagnostics.Stopwatch.GetTimestamp() - t0) * 1000.0 / System.Diagnostics.Stopwatch.Frequency;
            if (Time.frameCount != morphFrame) { morphFrame = Time.frameCount; morphFrameMs = 0; }
            morphFrameMs += ms;
            if (morphFrameMs > StatMorphFrameMaxMs) StatMorphFrameMaxMs = morphFrameMs;
        }

        private sealed class Morph
        {
            public ChunkEntry chunk;
            public NativeArray<float3> final;
            public NativeArray<float> delta;
            public NativeArray<float3> work;
            public float start;
        }

        private readonly List<Morph> morphs = new List<Morph>(32);

        /// <summary>Nejvýš tolik chunků se morfuje naráz (každý snímek se jim přepisují vrcholy).</summary>
        private const int MaxMorphChunks = 160;

        /// <summary>Kolik ms za snímek smí stát start morphů; další skupiny sloupců se zveřejní až příští snímek.</summary>
        private const double MorphFrameBudgetMs = 2.0;

        /// <summary>Paměťový strop: vrcholů ve všech morfujících chunkách naráz (~28 B/vrchol → ~17 MB).</summary>
        private const int MaxMorphVerts = 500000;

        /// <summary>Nad tolik indexů starých meshů (≈ cena rasterizace) se sloupec nemorfuje.</summary>
        private const long MaxMorphRasterIndices = 900000;

        private int morphVerts;
        public static int StatMorphSkipped;
        public static double StatMorphColumnMaxMs;

        private readonly List<Mesh> morphOld = new List<Mesh>(16);
        private readonly List<float> morphOldOffset = new List<float>(16);
        private static readonly MeshUpdateFlags MorphFlags =
            MeshUpdateFlags.DontRecalculateBounds | MeshUpdateFlags.DontValidateIndices | MeshUpdateFlags.DontNotifyMeshUsers;

        /// <summary>
        /// Oprava po review: příprava morphu běží ASYNCHRONNĚ, ještě před prohozením.
        /// Rasterizace starého povrchu i výpočet posunů jsou joby (jeden řetězec = jedna
        /// jednotka rozpočtu jobů); hlavní vlákno v okamžiku zveřejnění jen převezme hotová
        /// pole a nahraje vrcholy. Sloupec (celá skupina sourozenců) se zveřejní, až je příprava
        /// hotová – do té doby zůstává vidět starý terén.
        /// </summary>
        private sealed class MorphPrep
        {
            public Mesh.MeshDataArray oldData, newData;
            public NativeArray<float> height, offsets;
            public readonly List<ChunkEntry> sources = new List<ChunkEntry>(8);
            public readonly List<ChunkEntry> targets = new List<ChunkEntry>(8);
            public readonly List<NativeArray<float3>> final = new List<NativeArray<float3>>(8);
            public readonly List<NativeArray<float>> delta = new List<NativeArray<float>>(8);
            public readonly List<NativeArray<float3>> work = new List<NativeArray<float3>>(8);
            public readonly List<NativeArray<float>> range = new List<NativeArray<float>>(8);
            public JobHandle handle;
            public int verts;
            public bool reading;   // MeshDataArray drží meshe (zdroje i cíle) – nesmí se měnit
            public readonly List<Mesh> meshes = new List<Mesh>(16);
        }

        // Meshe, které právě čte rozpracovaná příprava. Kdo by je chtěl měnit (ApplyChunk při
        // znovupoužití z poolu, posun morphu), nejdřív čtení dokončí – Unity jinak hlásí chybu.
        private readonly HashSet<Mesh> prepMeshes = new HashSet<Mesh>();
        private readonly List<MorphPrep> activePreps = new List<MorphPrep>(8);

        private void ReleasePrepReads(MorphPrep prep)
        {
            if (!prep.reading) return;
            prep.handle.Complete();
            prep.oldData.Dispose();
            prep.newData.Dispose();
            prep.reading = false;
            for (int i = 0; i < prep.meshes.Count; i++) prepMeshes.Remove(prep.meshes[i]);
            prep.meshes.Clear();
            activePreps.Remove(prep);
        }

        /// <summary>Každý snímek: dokončené přípravy hned pustí meshe.</summary>
        private void ReleaseFinishedPrepReads()
        {
            for (int i = activePreps.Count - 1; i >= 0; i--)
                if (activePreps[i].handle.IsCompleted) ReleasePrepReads(activePreps[i]);
        }

        /// <summary>
        /// Mesh se bude měnit: čtení přípravy, která ho drží, musí doběhnout (vzácné).
        /// Kolo 3 (review Codexu): dokončí se JEN přípravy, které právě tento mesh čtou – dřív se
        /// synchronně dokončovaly všechny aktivní přípravy, i ty čekající v pruzích za jinou prací.
        /// Čas čekání se měří (/perf: StatPrepSyncMs).
        /// </summary>
        private void EnsureMeshNotRead(Mesh m)
        {
            if (m == null || !prepMeshes.Contains(m)) return;
            long t0 = System.Diagnostics.Stopwatch.GetTimestamp();
            for (int i = activePreps.Count - 1; i >= 0; i--)
            {
                MorphPrep prep = activePreps[i];
                if (!prep.meshes.Contains(m)) continue;
                if (!prep.handle.IsCompleted) StatPrepSyncWaits++;
                ReleasePrepReads(prep);
            }
            double ms = (System.Diagnostics.Stopwatch.GetTimestamp() - t0) * 1000.0 / System.Diagnostics.Stopwatch.Frequency;
            StatPrepSyncMs += ms;
            if (ms > StatPrepSyncMaxMs) StatPrepSyncMaxMs = ms;
        }

        public static int StatPrepSyncWaits;
        public static double StatPrepSyncMs, StatPrepSyncMaxMs;

        private readonly List<ChunkEntry> poolSkipped = new List<ChunkEntry>(8);

        /// <summary>
        /// Kolo 3: chunk z poolu, jehož mesh právě nečte žádná příprava morphu. Recyklace tak
        /// nemusí čekat na job; když je takových víc než pár, vznikne raději nový objekt.
        /// </summary>
        private ChunkEntry PopFreeChunkEntry()
        {
            ChunkEntry found = null;
            while (pool.Count > 0 && poolSkipped.Count < 8)
            {
                ChunkEntry c = pool.Pop();
                if (c.renderMesh != null && prepMeshes.Contains(c.renderMesh)) { poolSkipped.Add(c); continue; }
                found = c;
                break;
            }
            for (int i = poolSkipped.Count - 1; i >= 0; i--) pool.Push(poolSkipped[i]);
            poolSkipped.Clear();
            return found ?? CreateChunkObject();
        }

        private bool MorphWanted(ColumnEntry e)
            => GeomorphEnabled && GeomorphSeconds > 0f && !e.rebuilding && !e.seamRebuild && e.staged.Count > 0
               && streamedOnce && !IsTeleporting;

        /// <summary>
        /// Smí se sloupec zveřejnit? true = morph není potřeba / nevešel se do stropů
        /// (prohodí se naráz) / příprava je hotová. false = příprava běží nebo čeká na volnou
        /// jednotku rozpočtu – zkusí se příští snímek, starý terén zůstává.
        /// </summary>
        private bool EnsureMorphPrepared(ColumnEntry e)
        {
            if (e.morphPrep != null) return e.morphPrep.handle.IsCompleted;
            if (e.morphSkip || !MorphWanted(e)) return true;
            // Sloupec pod hráčem a těsně vedle se zveřejní hned (podlaha má přednost před morphem).
            if (e.lod == 0 && ColumnDist(new int3(e.coord.x, e.coord.y, 0)) < UrgentDistSqr) { e.morphSkip = true; return true; }
            long t0 = System.Diagnostics.Stopwatch.GetTimestamp();

            float span = SpanOf(e.lod);
            float2 lo = new float2(e.coord.x, e.coord.y) * span;

            morphOld.Clear();
            morphOldOffset.Clear();
            morphSources.Clear();
            int levels = builders != null ? builders.Length : 1;
            for (int m = 0; m < levels; m++)
            {
                if (m == e.lod) continue;
                if (m < e.lod)
                {
                    int k = 1 << (e.lod - m);
                    for (int dz = 0; dz < k; dz++)
                    for (int dx = 0; dx < k; dx++)
                        AddMorphSources(new int3(e.coord.x * k + dx, e.coord.y * k + dz, m));
                }
                else
                {
                    int sh = m - e.lod;
                    AddMorphSources(new int3(e.coord.x >> sh, e.coord.y >> sh, m));
                }
            }
            if (morphOld.Count == 0) { e.morphSkip = true; return true; }

            // Tvrdé stropy: celý sloupec, nebo nic (částečný morph by mezi chunky otevřel
            // škvíru). Počet chunků, vrcholy (paměť ~28 B/vrchol, včetně rozpracovaných
            // příprav) a odhad ceny rasterizace (indexy starých meshů).
            int newChunks = 0, newVerts = 0;
            foreach (var kv in e.staged)
            {
                Mesh m = kv.Value.renderMesh;
                if (m == null || m.vertexCount == 0) continue;
                newChunks++;
                newVerts += m.vertexCount;
            }
            long oldIndices = 0;
            for (int i = 0; i < morphOld.Count; i++) oldIndices += morphOld[i].GetIndexCount(0);
            if (newChunks == 0 || morphs.Count + morphPrepChunks + newChunks > MaxMorphChunks
                || morphVerts + newVerts > MaxMorphVerts || oldIndices > MaxMorphRasterIndices)
            {
                e.morphSkip = true;
                StatMorphSkipped++;
                return true;
            }
            if (!TryTakeUnits(1)) return false;   // příprava je job – bere ze společného rozpočtu

            var prep = new MorphPrep { verts = newVerts };
            prep.sources.AddRange(morphSources);
            float cell = Mathf.Max(voxelSize, voxelSize * (1 << e.lod));
            int side = Mathf.CeilToInt(span / cell) + 1;
            prep.height = new NativeArray<float>(side * side, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
            prep.offsets = new NativeArray<float>(morphOldOffset.Count, Allocator.Persistent);
            for (int i = 0; i < morphOldOffset.Count; i++) prep.offsets[i] = morphOldOffset[i];
            prep.oldData = Mesh.AcquireReadOnlyMeshData(morphOld);
            morphNew.Clear();
            foreach (var kv in e.staged)
            {
                Mesh m = kv.Value.renderMesh;
                if (m == null || m.vertexCount == 0) continue;
                morphNew.Add(m);
                prep.targets.Add(kv.Value);
            }
            prep.newData = Mesh.AcquireReadOnlyMeshData(morphNew);
            prep.reading = true;
            prep.meshes.AddRange(morphOld);
            prep.meshes.AddRange(morphNew);
            for (int i = 0; i < prep.meshes.Count; i++) prepMeshes.Add(prep.meshes[i]);
            activePreps.Add(prep);

            JobHandle h = new MorphRasterJob
            {
                meshes = prep.oldData, offsets = prep.offsets, origin = lo, cell = cell, side = side, height = prep.height,
            }.Schedule(JobBudget.Dependency);
            for (int i = 0; i < prep.targets.Count; i++)
            {
                int n = morphNew[i].vertexCount;
                var fin = new NativeArray<float3>(n, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
                var del = new NativeArray<float>(n, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
                var wrk = new NativeArray<float3>(n, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
                var rng = new NativeArray<float>(2, Allocator.Persistent);
                prep.final.Add(fin); prep.delta.Add(del); prep.work.Add(wrk); prep.range.Add(rng);
                // Řetězově za sebou – celá příprava drží nejvýš jedno vlákno.
                h = new MorphDeltaJob
                {
                    newMeshes = prep.newData, meshIndex = i, final = fin, height = prep.height, origin = lo, cell = cell,
                    side = side, span = span, delta = del, work = wrk, range = rng,
                }.Schedule(h);
            }
            prep.handle = h;
            CommitLane(h);
            JobHandle.ScheduleBatchedJobs();
            e.morphPrep = prep;
            morphPrepChunks += prep.targets.Count;
            morphVerts += newVerts;
            ChargeMorph(t0);
            return false;
        }

        private int morphPrepChunks;
        private readonly List<ChunkEntry> morphSources = new List<ChunkEntry>(16);
        private readonly List<Mesh> morphNew = new List<Mesh>(8);

        /// <summary>Volá PublishColumn těsně před prohozením: převezme hotovou přípravu.</summary>
        private void BeginColumnMorph(ColumnEntry e)
        {
            MorphPrep prep = e.morphPrep;
            e.morphPrep = null;
            e.morphSkip = false;
            if (prep == null) return;
            long t0 = System.Diagnostics.Stopwatch.GetTimestamp();
            ReleasePrepReads(prep);   // dokončí joby a pustí meshe dřív, než se do nich zapisuje
            morphPrepChunks -= prep.targets.Count;

            // Zdroje se mezitím nesměly změnit (uvolnění, přestavba): jinak prohodit bez morphu.
            bool valid = MorphWanted(e);
            for (int i = 0; valid && i < prep.sources.Count; i++)
            {
                ChunkEntry src = prep.sources[i];
                if (src.go == null || !src.go.activeSelf) valid = false;
            }
            for (int i = 0; valid && i < prep.targets.Count; i++)
                if (!e.staged.ContainsValue(prep.targets[i])) valid = false;

            if (valid)
            {
                for (int i = 0; i < prep.targets.Count; i++)
                {
                    ChunkEntry c = prep.targets[i];
                    Mesh m = c.renderMesh;
                    EndMorph(c, true);
                    var mo = new Morph { chunk = c, start = Time.unscaledTime, final = prep.final[i], delta = prep.delta[i], work = prep.work[i] };
                    Bounds b = m.bounds;
                    b.Encapsulate(new Vector3(b.center.x, prep.range[i][0], b.center.z));
                    b.Encapsulate(new Vector3(b.center.x, prep.range[i][1], b.center.z));
                    m.bounds = b;
                    m.SetVertices(mo.work, 0, mo.work.Length, MorphFlags);
                    c.morph = mo;
                    morphs.Add(mo);
                    StatMorphChunks++;
                }
                prep.final.Clear(); prep.delta.Clear(); prep.work.Clear();   // vlastnictví přešlo na Morph
                e.morphUntil = Time.unscaledTime + GeomorphSeconds;
            }
            else
            {
                morphVerts -= prep.verts;
            }
            DisposePrep(prep);
            double colMs = (System.Diagnostics.Stopwatch.GetTimestamp() - t0) * 1000.0 / System.Diagnostics.Stopwatch.Frequency;
            StatMorphStartMs += colMs;
            if (colMs > StatMorphColumnMaxMs) StatMorphColumnMaxMs = colMs;
            ChargeMorph(t0);
        }

        private void DisposePrep(MorphPrep prep)
        {
            ReleasePrepReads(prep);
            if (prep.height.IsCreated) prep.height.Dispose();
            if (prep.offsets.IsCreated) prep.offsets.Dispose();
            foreach (var a in prep.final) if (a.IsCreated) a.Dispose();
            foreach (var a in prep.delta) if (a.IsCreated) a.Dispose();
            foreach (var a in prep.work) if (a.IsCreated) a.Dispose();
            foreach (var a in prep.range) if (a.IsCreated) a.Dispose();
            prep.final.Clear(); prep.delta.Clear(); prep.work.Clear(); prep.range.Clear();
        }

        /// <summary>Zrušení rozpracované přípravy (uvolnění sloupce, přestavba, konec hry).</summary>
        private void CancelMorphPrep(ColumnEntry e)
        {
            e.morphSkip = false;
            MorphPrep prep = e.morphPrep;
            if (prep == null) return;
            e.morphPrep = null;
            morphPrepChunks -= prep.targets.Count;
            morphVerts -= prep.verts;
            DisposePrep(prep);
        }

        private void AddMorphSources(int3 column)
        {
            if (!colChunks.TryGetValue(column, out List<int4> l)) return;
            for (int i = 0; i < l.Count; i++)
            {
                if (!chunks.TryGetValue(l[i], out ChunkEntry c)) continue;
                if (c.go == null || !c.go.activeSelf || c.renderMesh == null || c.renderMesh.vertexCount == 0) continue;
                morphOld.Add(c.renderMesh);
                morphSources.Add(c);
                // Vrcholy jsou ve světových souřadnicích, ale zapuštěný sloupec je posunutý
                // potomkem – rasterizuje se povrch, který je opravdu VIDĚT.
                morphOldOffset.Add(c.renderTf.localPosition.y);
            }
        }

        /// <summary>Každý snímek: posune morfující chunky a dokončené uvolní.</summary>
        private void UpdateMorphs()
        {
            ReleaseFinishedPrepReads();
            if (morphs.Count == 0) return;
            long t0 = System.Diagnostics.Stopwatch.GetTimestamp();
            float now = Time.unscaledTime;
            for (int i = morphs.Count - 1; i >= 0; i--)
            {
                Morph mo = morphs[i];
                ChunkEntry c = mo.chunk;
                float t = (now - mo.start) / Mathf.Max(0.01f, GeomorphSeconds);
                if (t >= 1f || c.renderMesh == null) { EndMorph(c, true); continue; }
                if (prepMeshes.Contains(c.renderMesh)) continue;   // čte ho příprava jiného sloupce – posun příští snímek
                float k = 1f - Mathf.SmoothStep(0f, 1f, t);
                new MorphApplyJob { final = mo.final, delta = mo.delta, k = k, work = mo.work }.Run(mo.final.Length);
                c.renderMesh.SetVertices(mo.work, 0, mo.work.Length, MorphFlags);
            }
            ChargeMorph(t0);
        }

        /// <summary>Ukončí morph chunku. <paramref name="restore"/> = vrátit konečné polohy do meshe.</summary>
        private void EndMorph(ChunkEntry c, bool restore)
        {
            Morph mo = c.morph;
            if (mo == null) return;
            c.morph = null;
            if (restore && c.renderMesh != null && mo.final.IsCreated)
            {
                EnsureMeshNotRead(c.renderMesh);
                c.renderMesh.SetVertices(mo.final, 0, mo.final.Length, MorphFlags);
                c.renderMesh.RecalculateBounds();
            }
            morphVerts -= mo.final.IsCreated ? mo.final.Length : 0;
            DisposeMorph(mo);
            morphs.Remove(mo);
        }

        private static void DisposeMorph(Morph mo)
        {
            if (mo.final.IsCreated) mo.final.Dispose();
            if (mo.delta.IsCreated) mo.delta.Dispose();
            if (mo.work.IsCreated) mo.work.Dispose();
        }

        private void DisposeAllMorphs()
        {
            for (int i = 0; i < morphs.Count; i++) { DisposeMorph(morphs[i]); if (morphs[i].chunk != null) morphs[i].chunk.morph = null; }
            morphs.Clear();
            morphVerts = 0;
        }

        /// <summary>Sloupec ještě morfuje – osazení a zástupci stromů počkají.</summary>
        private static bool Morphing(ColumnEntry e) => Time.unscaledTime < e.morphUntil;

        /// <summary>Horní plocha starých meshů do mřížky (max výška v každém bodě).</summary>
        [BurstCompile(CompileSynchronously = true)]   // v editoru jinak první běhy jedou bez Burstu (desítky ms)
        private struct MorphRasterJob : IJob
        {
            [ReadOnly] public Mesh.MeshDataArray meshes;
            [ReadOnly] public NativeArray<float> offsets;
            public float2 origin;
            public float cell;
            public int side;
            public NativeArray<float> height;

            public void Execute()
            {
                for (int i = 0; i < height.Length; i++) height[i] = float.NegativeInfinity;
                for (int mi = 0; mi < meshes.Length; mi++)
                {
                    Mesh.MeshData md = meshes[mi];
                    int vc = md.vertexCount;
                    if (vc == 0 || md.subMeshCount == 0) continue;
                    var v = new NativeArray<Vector3>(vc, Allocator.Temp, NativeArrayOptions.UninitializedMemory);
                    md.GetVertices(v);
                    int ic = md.GetSubMesh(0).indexCount;
                    var idx = new NativeArray<int>(ic, Allocator.Temp, NativeArrayOptions.UninitializedMemory);
                    md.GetIndices(idx, 0);
                    float maxX = origin.x + (side - 1) * cell, maxZ = origin.y + (side - 1) * cell;
                    for (int t = 0; t + 2 < ic; t += 3)
                    {
                        float3 a = v[idx[t]], b = v[idx[t + 1]], c = v[idx[t + 2]];
                        float x0 = math.min(a.x, math.min(b.x, c.x)), x1 = math.max(a.x, math.max(b.x, c.x));
                        float z0 = math.min(a.z, math.min(b.z, c.z)), z1 = math.max(a.z, math.max(b.z, c.z));
                        if (x1 < origin.x || z1 < origin.y || x0 > maxX || z0 > maxZ) continue;
                        float det = (b.z - c.z) * (a.x - c.x) + (c.x - b.x) * (a.z - c.z);
                        if (math.abs(det) < 1e-6f) continue;   // svislý trojúhelník (stěna, sukně)
                        int gx0 = math.max(0, (int)math.ceil((x0 - origin.x) / cell));
                        int gx1 = math.min(side - 1, (int)math.floor((x1 - origin.x) / cell));
                        int gz0 = math.max(0, (int)math.ceil((z0 - origin.y) / cell));
                        int gz1 = math.min(side - 1, (int)math.floor((z1 - origin.y) / cell));
                        for (int gz = gz0; gz <= gz1; gz++)
                        for (int gx = gx0; gx <= gx1; gx++)
                        {
                            float px = origin.x + gx * cell, pz = origin.y + gz * cell;
                            float l1 = ((b.z - c.z) * (px - c.x) + (c.x - b.x) * (pz - c.z)) / det;
                            float l2 = ((c.z - a.z) * (px - c.x) + (a.x - c.x) * (pz - c.z)) / det;
                            float l3 = 1f - l1 - l2;
                            if (l1 < -1e-4f || l2 < -1e-4f || l3 < -1e-4f) continue;
                            float y = l1 * a.y + l2 * b.y + l3 * c.y + offsets[mi];
                            int gi = gz * side + gx;
                            if (y > height[gi]) height[gi] = y;
                        }
                    }
                    v.Dispose();
                    idx.Dispose();
                }
            }
        }

        /// <summary>Svislý posun vrcholu na starý povrch, s náběhem od okraje a mimo převisy.</summary>
        [BurstCompile(CompileSynchronously = true)]   // v editoru jinak první běhy jedou bez Burstu (desítky ms)
        private struct MorphDeltaJob : IJob
        {
            [ReadOnly] public Mesh.MeshDataArray newMeshes;
            public int meshIndex;
            public NativeArray<float3> final;
            [ReadOnly] public NativeArray<float> height;
            public float2 origin;
            public float cell, span;
            public int side;
            public NativeArray<float> delta;
            public NativeArray<float3> work;
            public NativeArray<float> range;

            public void Execute()
            {
                float edge = 4f * cell;
                float near = 4f * cell, far = 8f * cell;
                float lo = float.MaxValue, hi = float.MinValue;
                newMeshes[meshIndex].GetVertices(final.Reinterpret<Vector3>());
                for (int i = 0; i < final.Length; i++)
                {
                    float3 p = final[i];
                    float d = 0f;
                    float fx = (p.x - origin.x) / cell, fz = (p.z - origin.y) / cell;
                    int ix = (int)math.floor(fx), iz = (int)math.floor(fz);
                    if (ix >= 0 && iz >= 0 && ix < side - 1 && iz < side - 1)
                    {
                        float h00 = height[iz * side + ix], h10 = height[iz * side + ix + 1];
                        float h01 = height[(iz + 1) * side + ix], h11 = height[(iz + 1) * side + ix + 1];
                        if (!math.isinf(h00) && !math.isinf(h10) && !math.isinf(h01) && !math.isinf(h11))
                        {
                            float tx = fx - ix, tz = fz - iz;
                            float h = math.lerp(math.lerp(h00, h10, tx), math.lerp(h01, h11, tx), tz);
                            float dy = h - p.y;
                            // okraj sloupce stojí (žádná mezera k sousedům), převisy a jeskyně taky
                            float de = math.min(math.min(p.x - origin.x, origin.x + span - p.x),
                                                math.min(p.z - origin.y, origin.y + span - p.z));
                            float w = math.saturate((de - 0.25f * cell) / edge)
                                    * (1f - math.smoothstep(near, far, math.abs(dy)));
                            d = dy * w;
                        }
                    }
                    delta[i] = d;
                    float3 s = p; s.y += d;
                    work[i] = s;
                    lo = math.min(lo, s.y); hi = math.max(hi, s.y);
                }
                range[0] = lo; range[1] = hi;
            }
        }

        [BurstCompile(CompileSynchronously = true)]   // v editoru jinak první běhy jedou bez Burstu (desítky ms)
        private struct MorphApplyJob : IJobParallelFor
        {
            [ReadOnly] public NativeArray<float3> final;
            [ReadOnly] public NativeArray<float> delta;
            public float k;
            public NativeArray<float3> work;
            public void Execute(int i) { float3 p = final[i]; p.y += delta[i] * k; work[i] = p; }
        }
    }
}
