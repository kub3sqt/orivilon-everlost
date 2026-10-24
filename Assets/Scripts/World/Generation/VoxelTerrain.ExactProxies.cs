using System.Collections.Generic;
using Orivilon.World.Spawning;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;

namespace Orivilon.World.Generation
{
    /// <summary>
    /// Kolo 3 (review Codexu, bod 3): zástupci vzdálených stromů ze STEJNÉHO zdroje jako skutečné LOD0 osazení.
    ///
    /// <para>Dřív se zástupci LOD1/LOD2 sloupce počítali z hrubého pole toho sloupce (jiný krok mřížky →
    /// jiná výška, sklon a vzdálenost od vody), takže stejná buňka mřížky stromů dopadla jinak: strom
    /// chyběl/přebýval nebo měl jiný druh a po přiblížení „přeskočil“ o 8–14 m nebo zmizel.</para>
    ///
    /// <para>Teď se pro každý LOD0 sloupec pod hrubým sloupcem spočte (job, v rozpočtu jobů) jen 2D pole
    /// povrchu v rozlišení LOD0 – stejný počátek, krok, okraj, šev i <c>surfRaw</c> jako u skutečného
    /// LOD0 sloupce – a na něm vrstva stromů osazovače se stejným okrajem rezervací (5 m). Výsledek
    /// se drží v cache po LOD0 souřadnicích, takže LOD1 i LOD2 ji sdílejí a nic se nepočítá dvakrát.
    /// Skutečné osazení, save ani ID objektů se nemění.</para>
    ///
    /// <para>Zbývající rozdíl: kontroly proti LOD0 meshi (stopa, čistota povrchu) a hladiny sloupce
    /// zástupci znát nemohou, protože mesh vzdáleného sloupce neexistuje.</para>
    ///
    /// <para>Když přesná data při zveřejnění ještě nejsou (pole se objednávají už při naplánování
    /// hrubého sloupce, takže je to výjimka), postaví se hrubí zástupci jako dřív a jakmile přesná data
    /// dorazí, sloupec se přepíše.</para>
    /// </summary>
    public partial class VoxelTerrain
    {
        /// <summary>A/B (/perf exacttrees on|off).</summary>
        public static bool ExactTreeProxies = true;

        private sealed class ExactReq
        {
            public ColumnField field;
            public JobHandle handle;
            public bool scheduled;
        }

        private readonly Dictionary<int2, List<EcoPlacement>> exactTrees = new Dictionary<int2, List<EcoPlacement>>();
        private readonly Queue<int2> exactOrder = new Queue<int2>();
        private readonly Dictionary<int2, ExactReq> exactReqs = new Dictionary<int2, ExactReq>();
        private readonly List<int2> exactWanted = new List<int2>(64);
        private readonly List<int2> exactDone = new List<int2>(16);
        private readonly List<EcoPlacement> exactScratch = new List<EcoPlacement>(256);
        private const int ExactCacheMax = 6000;
        private const int ExactMaxInFlight = 24;
        private const double ExactFrameBudgetMs = 1.0;

        public static int StatExactFields, StatExactApprox, StatExactUpgraded;

        /// <summary>Objedná přesná data pro všechny LOD0 sloupce pod hrubým sloupcem (idempotentní).</summary>
        private void RequestExactTrees(int2 coord, int lod)
        {
            if (!ExactTreeProxies || lod < 1 || lod > TreeProxies.ExactMaxLod) return;
            int k = 1 << lod;
            for (int dz = 0; dz < k; dz++)
            for (int dx = 0; dx < k; dx++)
            {
                var c = new int2(coord.x * k + dx, coord.y * k + dz);
                if (exactTrees.ContainsKey(c) || exactReqs.ContainsKey(c)) continue;
                exactReqs[c] = new ExactReq();
                exactWanted.Add(c);
            }
        }

        /// <summary>V ScheduleWork (rozpočet jobů): naplánuje čekající pole, nejbližší první.</summary>
        private void ScheduleExactFields()
        {
            if (exactWanted.Count == 0 || builders == null || builders.Length == 0) return;
            VoxelChunkBuilder b0 = builders[0];
            int inFlight = 0;
            foreach (var kv in exactReqs) if (kv.Value.scheduled) inFlight++;
            if (inFlight >= ExactMaxInFlight) return;
            if (viewer != null && exactWanted.Count > 1)
            {
                float2 v = new float2(viewer.position.x, viewer.position.z);
                float span = b0.ChunkSpan;
                exactWanted.Sort((a, c) => math.lengthsq((new float2(a) + 0.5f) * span - v).CompareTo(math.lengthsq((new float2(c) + 0.5f) * span - v)));
            }
            int i = 0;
            for (; i < exactWanted.Count && inFlight < ExactMaxInFlight; i++)
            {
                int2 c = exactWanted[i];
                if (!exactReqs.TryGetValue(c, out ExactReq r) || r.scheduled) continue;
                float2 center = b0.ColumnCenter(c);
                if (hydro != null && !hydro.IsReady(center)) break;          // region si objedná sloupec sám
                if (!TryTakeUnits(1)) break;
                r.handle = b0.ScheduleSurfaceField(c, Allocator.Persistent, HydroRef(center), out r.field);
                CommitLane(r.handle);
                r.scheduled = true;
                inFlight++;
                StatExactFields++;
            }
            // Naplánované (a zastaralé) ze seznamu pryč.
            int w = 0;
            for (int j = 0; j < exactWanted.Count; j++)
                if (exactReqs.TryGetValue(exactWanted[j], out ExactReq r2) && !r2.scheduled) exactWanted[w++] = exactWanted[j];
            exactWanted.RemoveRange(w, exactWanted.Count - w);
        }

        /// <summary>Každý snímek: hotová pole → vrstva stromů do cache (časový strop, aspoň jedno).</summary>
        private void CollectExactFields()
        {
            if (exactReqs.Count == 0) return;
            var sp = VoxelProps.SpawnerAsset;
            long t0 = System.Diagnostics.Stopwatch.GetTimestamp();
            exactDone.Clear();
            foreach (var kv in exactReqs)
            {
                ExactReq r = kv.Value;
                if (!r.scheduled || !r.handle.IsCompleted) continue;
                if (exactDone.Count > 0 && (System.Diagnostics.Stopwatch.GetTimestamp() - t0) * 1000.0
                    / System.Diagnostics.Stopwatch.Frequency > ExactFrameBudgetMs) break;
                r.handle.Complete();
                if (sp != null)
                {
                    VoxelChunkBuilder b0 = builders[0];
                    var list = EcologyPlacer.BuildTreeProxies(sp, r.field, b0.ColumnPad, b0.voxelSize, b0.Params.seaLevel,
                                                              palette, b0.Params.Micro, ResolvedSeed, b0.ChunkSpan, 5f);
                    StoreExact(kv.Key, list);
                }
                r.field.Dispose();
                exactDone.Add(kv.Key);
            }
            for (int i = 0; i < exactDone.Count; i++) exactReqs.Remove(exactDone[i]);
        }

        private void StoreExact(int2 c, List<EcoPlacement> list)
        {
            if (!exactTrees.TryGetValue(c, out List<EcoPlacement> dst))
            {
                dst = new List<EcoPlacement>(list.Count);
                exactTrees[c] = dst;
                exactOrder.Enqueue(c);
                while (exactOrder.Count > ExactCacheMax) exactTrees.Remove(exactOrder.Dequeue());
            }
            dst.Clear();
            dst.AddRange(list);
        }

        /// <summary>Jsou přesná data celého hrubého sloupce? Pokud ano, vrátí je spojená (sdílený scratch).</summary>
        private bool TryExactTrees(int2 coord, int lod, out List<EcoPlacement> list)
        {
            list = null;
            if (!ExactTreeProxies || lod < 1 || lod > TreeProxies.ExactMaxLod) return false;
            int k = 1 << lod;
            for (int dz = 0; dz < k; dz++)
            for (int dx = 0; dx < k; dx++)
                if (!exactTrees.ContainsKey(new int2(coord.x * k + dx, coord.y * k + dz))) return false;
            exactScratch.Clear();
            for (int dz = 0; dz < k; dz++)
            for (int dx = 0; dx < k; dx++)
                exactScratch.AddRange(exactTrees[new int2(coord.x * k + dx, coord.y * k + dz)]);
            list = exactScratch;
            return true;
        }

        /// <summary>Hrubí zástupci postavení bez přesných dat se přepíšou, jakmile data dorazí (≤ 4 za snímek).</summary>
        private void UpgradeApproxProxies()
        {
            if (!ExactTreeProxies) return;
            int n = 0;
            foreach (var kv in columns)
            {
                if (n >= 4) break;
                ColumnEntry e = kv.Value;
                if (!e.proxyApprox || !e.published || e.lod < 1 || e.lod > TreeProxies.ExactMaxLod) continue;
                if (!TreeProxies.Has(TreeProxies.Key(e.coord.x, e.coord.y, e.lod))) { e.proxyApprox = false; continue; }
                if (!TryExactTrees(e.coord, e.lod, out List<EcoPlacement> list)) { RequestExactTrees(e.coord, e.lod); continue; }
                TreeProxies.Add(TreeProxies.Key(e.coord.x, e.coord.y, e.lod), VoxelProps.SpawnerAsset, list);
                e.proxyApprox = false;
                StatExactUpgraded++;
                n++;
            }
        }

        // ── Audit (/perf proxyaudit on): shoda zástupců se SKUTEČNÝM LOD0 osazením ──────────────
        // Při stavbě zástupců hrubého sloupce se uloží i dřívější (hrubé) varianta rozdělená po LOD0
        // sloupcích; až pak daný LOD0 sloupec dopočítá skutečné osazení, porovnají se obě varianty
        // se skutečnými stromy (stejný druh a vodorovná vzdálenost ≤ 0,75 m = shoda).
        public static bool ProxyAudit;
        public static int AuditColumns, AuditReal, AuditExactMatch, AuditExactExtra, AuditApproxMatch, AuditApproxExtra, AuditApproxColumns;
        private readonly Dictionary<int2, List<EcoPlacement>> approxAudit = new Dictionary<int2, List<EcoPlacement>>();

        public static void ResetAudit()
        {
            AuditColumns = AuditReal = AuditExactMatch = AuditExactExtra = AuditApproxMatch = AuditApproxExtra = AuditApproxColumns = 0;
        }

        private void AuditStoreApprox(ColumnEntry e, ObjectSpawner sp, VoxelChunkBuilder b)
        {
            var list = EcologyPlacer.BuildTreeProxies(sp, e.field, b.ColumnPad, b.voxelSize, b.Params.seaLevel,
                                                      palette, b.Params.Micro, ResolvedSeed, builders[0].ChunkSpan);
            float span = builders[0].ChunkSpan;
            int k = 1 << e.lod;
            for (int dz = 0; dz < k; dz++)
            for (int dx = 0; dx < k; dx++)
            {
                var c = new int2(e.coord.x * k + dx, e.coord.y * k + dz);
                if (!approxAudit.ContainsKey(c)) approxAudit[c] = new List<EcoPlacement>();
                else approxAudit[c].Clear();
            }
            for (int i = 0; i < list.Count; i++)
            {
                var c = new int2(Mathf.FloorToInt(list[i].position.x / span), Mathf.FloorToInt(list[i].position.z / span));
                if (approxAudit.TryGetValue(c, out List<EcoPlacement> l)) l.Add(list[i]);
            }
        }

        private static int CountMatches(List<EcoPlacement> real, List<EcoPlacement> proxies, out int extra)
        {
            int match = 0;
            var used = new bool[proxies.Count];
            for (int i = 0; i < real.Count; i++)
            {
                for (int j = 0; j < proxies.Count; j++)
                {
                    if (used[j] || proxies[j].spawnable != real[i].spawnable) continue;
                    float dx = proxies[j].position.x - real[i].position.x, dz = proxies[j].position.z - real[i].position.z;
                    if (dx * dx + dz * dz > 0.5625f) continue;
                    used[j] = true; match++; break;
                }
            }
            extra = proxies.Count - match;
            return match;
        }

        private readonly List<EcoPlacement> auditReal = new List<EcoPlacement>(64);

        private void AuditCompare(int2 coord, List<EcoPlacement> placements)
        {
            if (!exactTrees.TryGetValue(coord, out List<EcoPlacement> exact)) return;   // jen sloupce, které dřív kryl hrubý sloupec
            auditReal.Clear();
            for (int i = 0; i < placements.Count; i++) if (placements[i].layer == 0) auditReal.Add(placements[i]);
            AuditColumns++;
            AuditReal += auditReal.Count;
            AuditExactMatch += CountMatches(auditReal, exact, out int ex);
            AuditExactExtra += ex;
            if (approxAudit.TryGetValue(coord, out List<EcoPlacement> approx))
            {
                AuditApproxColumns++;
                AuditApproxMatch += CountMatches(auditReal, approx, out int ax);
                AuditApproxExtra += ax;
            }
        }

        // ── Kolo 4: latence osazení u hráče ──────────────────────────────────────────
        // Každý snímek: LOD0 sloupce v okruhu 3×3 kolem hráče, které ještě nemají dokončené osazení.
        // Měří se, jak dlouho sloupec u hráče čekal (od chvíle, kdy se do okruhu dostal) a v jakém
        // stavu: T = terén nezveřejněn, M = morph, Q = ve frontě osazení, P = výpočet rozmístění,
        // S = instancování objektů.
        private readonly Dictionary<int2, float> nearWaitSince = new Dictionary<int2, float>();
        private readonly List<int2> nearDone = new List<int2>();
        public static readonly int[] NearStateFrames = new int[5];
        public static float NearWaitMax, NearWaitSum; public static int NearWaitCount;
        public static void NearReset()
        {
            System.Array.Clear(NearStateFrames, 0, 5); System.Array.Clear(AllStateFrames, 0, 5); NearWaitMax = NearWaitSum = 0f; NearWaitCount = 0;
            System.Array.Clear(PubPropsCount, 0, 3); System.Array.Clear(PubPropsSlow, 0, 3); System.Array.Clear(PubPropsSum, 0, 3); System.Array.Clear(PubPropsMax, 0, 3);
            NearT0 = Time.realtimeSinceStartup;
        }

        /// <summary>Kolo 4: zveřejnění LOD0 sloupce → hotové osazení; koše podle vzdálenosti při zveřejnění (&lt;80, &lt;160, dál).</summary>
        public static readonly int[] PubPropsCount = new int[3], PubPropsSlow = new int[3];
        public static readonly float[] PubPropsSum = new float[3], PubPropsMax = new float[3];
        public static float NearT0 = float.MaxValue;
        public static readonly int[] AllStateFrames = new int[5];

        public int NearPropsProbe()
        {
            if (viewer == null || lastViewerColumn.x == int.MinValue || !PropsActive) return 0;
            float now = Time.realtimeSinceStartup;
            int waiting = 0;
            for (int dz = -1; dz <= 1; dz++)
            for (int dx = -1; dx <= 1; dx++)
            {
                var c = new int2(lastViewerColumn.x + dx, lastViewerColumn.y + dz);
                int st;
                if (!columns.TryGetValue(new int3(c.x, c.y, 0), out ColumnEntry e) || !e.published) st = 0;
                else if (PropsFinished(e) && e.propsRequested) st = -1;
                else if (Morphing(e)) st = 1;
                else if (!e.propsRequested) st = 2;
                else if (e.propsBuild != null) st = 3;
                else st = 4;
                if (st < 0)
                {
                    if (nearWaitSince.TryGetValue(c, out float t0))
                    {
                        float w = now - t0;
                        NearWaitMax = Mathf.Max(NearWaitMax, w); NearWaitSum += w; NearWaitCount++;
                        nearWaitSince.Remove(c);
                    }
                    continue;
                }
                if (st == 0 && IsWater(c)) continue;   // moře bez terénu nad hladinou nic nečeká
                NearStateFrames[st]++;
                waiting++;
                if (!nearWaitSince.ContainsKey(c)) nearWaitSince[c] = now;
            }
            // Kolo 4: totéž pro VŠECHNY zveřejněné LOD0 sloupce bez hotového osazení (kde visí fronta).
            foreach (var kv in columns)
            {
                ColumnEntry c = kv.Value;
                if (c.lod != 0 || !c.published || c.retired) continue;
                if (c.propsRequested && PropsFinished(c)) continue;
                int st2 = Morphing(c) ? 1 : !c.propsRequested ? 2 : c.propsBuild != null ? 3 : 4;
                AllStateFrames[st2]++;
            }
            // sloupce, které okruh opustily bez dokončení, se zapomenou
            nearDone.Clear();
            foreach (var kv in nearWaitSince)
                if (math.abs(kv.Key.x - lastViewerColumn.x) > 1 || math.abs(kv.Key.y - lastViewerColumn.y) > 1) nearDone.Add(kv.Key);
            for (int i = 0; i < nearDone.Count; i++) nearWaitSince.Remove(nearDone[i]);
            return waiting;
        }

        private bool IsWater(int2 c) => false;

        /// <summary>Diagnostika (/perf propslife): stav osazení LOD0 sloupce.</summary>
        public string PropsLifeState(int2 coord, out bool hasProps)
        {
            hasProps = false;
            if (!columns.TryGetValue(new int3(coord.x, coord.y, 0), out ColumnEntry e)) return "LOD0 sloupec neexistuje";
            hasProps = e.propsRequested && e.propsBuild == null && e.propsGo != null;
            return string.Format(System.Globalization.CultureInfo.InvariantCulture,
                "osazeno {0}, retired {1}, pole {2}, pole se počítá {3}, zástupci {4}, za stropem {5}",
                hasProps, e.retired, e.field.IsCreated, e.fieldPending, e.proxyRequested && !e.proxyDropped, BeyondPropsCap(coord, 0));
        }

        private void DisposeExactTrees()
        {
            foreach (var kv in exactReqs)
                if (kv.Value.scheduled) { kv.Value.handle.Complete(); kv.Value.field.Dispose(); }
            exactReqs.Clear();
            exactWanted.Clear();
            exactTrees.Clear();
            exactOrder.Clear();
            approxAudit.Clear();
        }
    }
}
