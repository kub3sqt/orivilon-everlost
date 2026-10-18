using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine;

namespace Orivilon.World.Generation
{
    /// <summary>
    /// Kolo 5 (16.10.2026): atomická výměna LOD místo zapouštění odcházejícího sloupce.
    ///
    /// <para>Dřív se odcházející sloupec, přes který už byla ZČÁSTI zveřejněná jiná úroveň,
    /// zapustil o půl voxelu své úrovně (+0,25 m) – i tam, kde náhrada ještě chyběla. Na hranici
    /// se sousedy z toho byl schod (u LOD3 přes 8 m) a nový jemný terén pak geomorphem
    /// vyjel ze zapuštěné výšky nahoru: terén „klesl a zase se zvedl“.</para>
    ///
    /// <para>Teď se plocha odcházejícího sloupce převezme naráz: nový sloupec, který ji
    /// (i zčásti) přebírá, se zveřejní až ve snímku, kdy se dají zveřejnit VŠECHNY žádané
    /// sloupce, které ji kryjí. Odcházející sloupec se v témž snímku uvolní (UnloadFar) a do té
    /// doby zůstává vidět na své skutečné výšce. Překryv tedy nevzniká a není co zapouštět.</para>
    /// </summary>
    public partial class VoxelTerrain
    {
        /// <summary>Atomická výměna LOD (/perf lodswap on|off). Výchozí zapnuto.</summary>
        public static bool AtomicLodSwap = true;

        /// <summary>Původní zapouštění odcházejících sloupců – jen pro A/B měření (/perf sink on|off). Výchozí vypnuto.</summary>
        public static bool LegacySinkOutgoing = false;

        /// <summary>Pojistka: po tolika s čekání odcházejícího sloupce se náhrada zveřejní i po částech (počítá StatAtomicForced).</summary>
        public static float AtomicSwapMaxWait = 10f;

        /// <summary>Čítače pro /perf: kolikrát atomická výměna pozdržela skupinu, kolikrát ji pojistka pustila po částech, kolik sloupců se schovalo kvůli běžícím jobům.</summary>
        public static int StatAtomicHeld, StatAtomicForced, StatReplacedHidden, StatAtomicUrgent;
        public static float StatAtomicWaitMax;

        private readonly HashSet<int3> atomicCand = new HashSet<int3>();
        private readonly List<int3> atomicLeaving = new List<int3>(64);
        private readonly List<int3> atomicCover = new List<int3>(64);

        /// <summary>
        /// Z hotových sloupců vyřadí ty, jejichž zveřejnění by jen ZČÁSTI překrylo odcházející
        /// (stále viditelný) sloupec. Vrací true, když něco vyřadil.
        /// </summary>
        private bool FilterAtomicSwap(List<int3> list)
        {
            if (!AtomicLodSwap || list.Count == 0 || !streamedOnce || IsTeleporting) return false;

            atomicCand.Clear();
            for (int i = 0; i < list.Count; i++)
            {
                ColumnEntry ce = columns[list[i]];
                if (!ce.published) atomicCand.Add(list[i]);   // přestavba už zveřejněného plochu nemění
            }
            if (atomicCand.Count == 0) return false;

            atomicLeaving.Clear();
            foreach (var kv in columns)
                if (kv.Value.published && !kv.Value.replacedHidden && !desired.Contains(kv.Key)) atomicLeaving.Add(kv.Key);
            if (atomicLeaving.Count == 0) return false;

            float now = Time.unscaledTime;
            bool removedAny = false, changed = true;
            for (int guard = 0; changed && guard < 16; guard++)
            {
                changed = false;
                for (int li = 0; li < atomicLeaving.Count; li++)
                {
                    int3 lk = atomicLeaving[li];
                    ColumnEntry le = columns[lk];
                    atomicCover.Clear();
                    CollectDesiredCover(lk);
                    bool touched = false, complete = true, urgent = false;
                    float urgentSqr = UrgentDistSqr;
                    for (int i = 0; i < atomicCover.Count; i++)
                    {
                        int3 d = atomicCover[i];
                        if (atomicCand.Contains(d)) { touched = true; if (d.z == 0 && ColumnDist(d) < urgentSqr) urgent = true; continue; }
                        if (!ColumnPublished(d)) complete = false;
                    }
                    if (!touched || complete) continue;
                    // Podlaha u hráče má přednost (stejně jako u morphu): zveřejní se hned,
                    // překryv se starým sloupcem řeší maska (UpdateOverlapMask), ne zapouštění.
                    if (urgent) { StatAtomicUrgent++; continue; }

                    float waited = le.leavingSince >= 0f ? now - le.leavingSince : 0f;
                    if (waited > StatAtomicWaitMax) StatAtomicWaitMax = waited;
                    if (waited > AtomicSwapMaxWait)
                    {
                        if (!le.atomicForced) { le.atomicForced = true; StatAtomicForced++; }
                        continue;
                    }

                    for (int i = 0; i < atomicCover.Count; i++)
                        if (atomicCand.Remove(atomicCover[i])) { list.Remove(atomicCover[i]); changed = true; removedAny = true; }
                    StatAtomicHeld++;
                }
            }
            return removedAny;
        }

        /// <summary>Všechny žádané sloupce (libovolné úrovně), které se překrývají s plochou <paramref name="key"/>.</summary>
        private void CollectDesiredCover(int3 key)
        {
            int levels = builders != null ? builders.Length : 1;
            for (int m = key.z + 1; m < levels; m++)
            {
                int sh = m - key.z;
                var p = new int3(key.x >> sh, key.y >> sh, m);
                if (desired.Contains(p)) atomicCover.Add(p);
            }
            if (key.z > 0) CollectDesiredBelow(new int3(key.x * 2, key.y * 2, key.z - 1));
        }

        private void CollectDesiredBelow(int3 first)
        {
            for (int dz = 0; dz < 2; dz++)
            for (int dx = 0; dx < 2; dx++)
            {
                var c = new int3(first.x + dx, first.y + dz, first.z);
                if (desired.Contains(c)) { atomicCover.Add(c); continue; }   // žádané sloupce se nepřekrývají – hlouběji nic
                if (c.z > 0) CollectDesiredBelow(new int3(c.x * 2, c.y * 2, c.z - 1));
            }
        }

        /// <summary>
        /// Odcházející sloupec, jehož plochu už CELOU kryje zveřejněná náhrada, ale nemůže se
        /// uvolnit (běží mu job): schová se jen jeho vzhled a hladina, ať nevzniká dvojitá
        /// geometrie. Collider (kořen chunku) zůstává, dokud se sloupec neuvolní.
        /// </summary>
        private void HideReplaced(int3 key, ColumnEntry e, bool hide)
        {
            if (e.replacedHidden == hide) return;
            e.replacedHidden = hide;
            coverageVersion++;
            if (hide) StatReplacedHidden++;
            if (colChunks.TryGetValue(key, out List<int4> l))
                for (int i = 0; i < l.Count; i++)
                    if (chunks.TryGetValue(l[i], out ChunkEntry c) && c.renderTf != null) c.renderTf.gameObject.SetActive(!hide);
            if (e.waterGo != null) e.waterGo.SetActive(!hide && e.hasWater);
        }

        // ── maska překryvu (pojistka) ─────────────────────────────────────────────

        /// <summary>Zvyšuje se při každém zveřejnění/uvolnění sloupce – masky se přepočítají jen po změně.</summary>
        private int coverageVersion;
        private bool[] maskGrid = new bool[0];
        private readonly List<int> maskTris = new List<int>(1 << 16);
        public static int StatMaskApplied;

        /// <summary>
        /// Pojistka pro výjimečný překryv (pojistka atomické výměny, sloupec u hráče): ze starého
        /// sloupce se vyřadí přesně ty trojúhelníky, nad jejichž plochou už je vidět zveřejněná
        /// jiná úroveň. Hranice sloupců padají na hrany buněk hrubé mřížky, takže trojúhelník leží
        /// celý na jedné straně – maska je přesná, nic se neposouvá ani nezapouští. Jen render
        /// mesh; collider (LOD0) se nemění.
        /// </summary>
        private void UpdateOverlapMask(int3 key, ColumnEntry e)
        {
            if (e.maskVersion == coverageVersion) return;
            e.maskVersion = coverageVersion;
            long t0 = System.Diagnostics.Stopwatch.GetTimestamp();
            try { UpdateOverlapMaskCore(key, e); }
            finally
            {
                double ms = (System.Diagnostics.Stopwatch.GetTimestamp() - t0) * 1000.0 / System.Diagnostics.Stopwatch.Frequency;
                if (ms > StatMaskMsMax) StatMaskMsMax = ms;
            }
        }

        public static double StatMaskMsMax;

        private void UpdateOverlapMaskCore(int3 key, ColumnEntry e)
        {
            int l = key.z;
            int side = 1 << l;
            if (maskGrid.Length < side * side) maskGrid = new bool[side * side];
            int levels = builders != null ? builders.Length : 1;

            bool all = false;
            for (int m = l + 1; m < levels && !all; m++)
            {
                int sh = m - l;
                if (MasksOver(new int3(key.x >> sh, key.y >> sh, m), l)) all = true;
            }
            int covered = 0;
            for (int gz = 0; gz < side; gz++)
            for (int gx = 0; gx < side; gx++)
            {
                bool c = all;
                int x0 = key.x * side + gx, z0 = key.y * side + gz;
                for (int m = 0; m < l && !c; m++)
                    if (MasksOver(new int3(x0 >> m, z0 >> m, m), l)) c = true;
                maskGrid[gz * side + gx] = c;
                if (c) covered++;
            }

            if (covered == 0) { if (e.masked) ClearOverlapMask(key, e); return; }
            colChunks.TryGetValue(key, out List<int4> lst);
            for (int i = 0; lst != null && i < lst.Count; i++)
            {
                if (!chunks.TryGetValue(lst[i], out ChunkEntry c) || c.renderMesh == null) continue;
                EnsureMeshNotRead(c.renderMesh);
                if (c.maskOrig == null) c.maskOrig = c.renderMesh.GetTriangles(0);
                ApplyMask(c.renderMesh, c.maskOrig, key, side, covered);
            }
            // Hladina patří ke sloupci stejně jako chunky – nad převzatou plochou by ležela dvakrát.
            if (e.waterMesh != null && e.waterMesh.vertexCount > 0 && e.waterMesh.subMeshCount > 0)
            {
                if (e.waterMaskOrig == null) e.waterMaskOrig = e.waterMesh.GetTriangles(0);
                ApplyMask(e.waterMesh, e.waterMaskOrig, key, side, covered);
            }
            if (!e.masked) StatMaskApplied++;
            e.masked = true;
        }

        private void ApplyMask(Mesh m, int[] t, int3 key, int side, int covered)
        {
            float span0 = SpanOf(0);
            maskTris.Clear();
            if (covered < side * side)
            {
                var v = traceTmpVerts;
                v.Clear();
                m.GetVertices(v);
                for (int k = 0; k + 2 < t.Length; k += 3)
                {
                    Vector3 a = v[t[k]], b = v[t[k + 1]], d = v[t[k + 2]];
                    int gx = Mathf.Clamp(Mathf.FloorToInt((a.x + b.x + d.x) / (3f * span0)) - key.x * side, 0, side - 1);
                    int gz = Mathf.Clamp(Mathf.FloorToInt((a.z + b.z + d.z) / (3f * span0)) - key.y * side, 0, side - 1);
                    if (maskGrid[gz * side + gx]) continue;
                    maskTris.Add(t[k]); maskTris.Add(t[k + 1]); maskTris.Add(t[k + 2]);
                }
            }
            m.SetTriangles(maskTris, 0, false);
        }

        private readonly List<Vector3> traceTmpVerts = new List<Vector3>(1 << 16);

        /// <summary>Viditelný zveřejněný sloupec (ne schovaný kvůli běžícímu jobu).</summary>
        private bool VisiblyPublished(int3 k)
            => columns.TryGetValue(k, out ColumnEntry e) && e.published && !e.replacedHidden;

        /// <summary>
        /// Smí sloupec <paramref name="k"/> zakrýt odcházející sloupec úrovně <paramref name="lod"/>?
        /// Žádaný (náhrada) vždy; jiný odcházející jen jemnější. Vztah je nesouměrný, takže se dva
        /// odcházející sloupce nikdy nezamaskují navzájem (to by byla díra).
        /// </summary>
        private bool MasksOver(int3 k, int lod)
            => VisiblyPublished(k) && (desired.Contains(k) || k.z < lod);

        /// <summary>Kreslí zamaskovaný sloupec v bodě (x, z)? (pro sondy pokrytí)</summary>
        private bool MaskedAt(int3 key, ColumnEntry e, float x, float z)
        {
            if (!e.masked) return false;
            int levels = builders != null ? builders.Length : 1;
            float span0 = SpanOf(0);
            int x0 = Mathf.FloorToInt(x / span0), z0 = Mathf.FloorToInt(z / span0);
            for (int m = 0; m < levels; m++)
                if (m != key.z && MasksOver(new int3(x0 >> m, z0 >> m, m), key.z)) return true;
            return false;
        }

        private void ClearOverlapMask(int3 key, ColumnEntry e)
        {
            e.masked = false;
            e.maskVersion = -1;
            colChunks.TryGetValue(key, out List<int4> lst);
            for (int i = 0; lst != null && i < lst.Count; i++)
            {
                if (!chunks.TryGetValue(lst[i], out ChunkEntry c) || c.maskOrig == null || c.renderMesh == null) continue;
                EnsureMeshNotRead(c.renderMesh);
                c.renderMesh.SetTriangles(c.maskOrig, 0, false);
                c.maskOrig = null;
            }
            if (e.waterMaskOrig != null && e.waterMesh != null) e.waterMesh.SetTriangles(e.waterMaskOrig, 0, false);
            e.waterMaskOrig = null;
        }

        // ── diagnostika kola 5: výška VIDITELNÉHO povrchu v čase ──────────────────

        private readonly Dictionary<Mesh, int> traceMeshFrame = new Dictionary<Mesh, int>();
        private readonly Dictionary<Mesh, Vector3[]> traceVerts = new Dictionary<Mesh, Vector3[]>();
        private readonly Dictionary<Mesh, int[]> traceTris = new Dictionary<Mesh, int[]>();

        /// <summary>
        /// Nejvyšší viditelný povrch terénu v bodě (x, z) – ze skutečně vykreslovaných meshů
        /// včetně posunu render potomka (zapuštění) a rozběhnutého geomorphu. Do
        /// <paramref name="sig"/> zapíše úrovně, které bod kryjí: L{lod}:{posun}:{výška}.
        /// Jen diagnostika (/perf lodtrace) – čte meshe, na hlavním vlákně stojí ms.
        /// </summary>
        public float TraceVisibleSurface(float x, float z, System.Text.StringBuilder sig, out int covering, out bool morphing)
        {
            covering = 0;
            morphing = false;
            float best = float.NaN;
            if (builders == null) return best;
            for (int l = 0; l < builders.Length; l++)
            {
                float span = SpanOf(l);
                var key = new int3(Mathf.FloorToInt(x / span), Mathf.FloorToInt(z / span), l);
                if (!columns.TryGetValue(key, out ColumnEntry e) || !e.published) continue;
                if (!colChunks.TryGetValue(key, out List<int4> lst)) continue;
                float colBest = float.NaN, off = 0f;
                bool visible = false;
                for (int i = 0; i < lst.Count; i++)
                {
                    if (!chunks.TryGetValue(lst[i], out ChunkEntry c) || c.go == null || !c.go.activeSelf) continue;
                    if (!c.renderTf.gameObject.activeSelf || c.renderMesh == null || c.renderMesh.vertexCount == 0) continue;
                    visible = true;
                    if (c.morph != null) morphing = true;
                    off = c.renderTf.position.y;
                    float h = TraceSample(c.renderMesh, x, z);
                    if (!float.IsNaN(h) && (float.IsNaN(colBest) || h + off > colBest)) colBest = h + off;
                }
                if (!visible || float.IsNaN(colBest)) continue;   // zamaskovaný (nebo mimo mesh) v tomto bodě nekreslí
                covering++;
                sig.Append('L').Append(l).Append(':').Append(off.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture))
                   .Append(':').Append(float.IsNaN(colBest) ? "nan" : colBest.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture)).Append(' ');
                if (!float.IsNaN(colBest) && (float.IsNaN(best) || colBest > best)) best = colBest;
            }
            return best;
        }

        private float TraceSample(Mesh m, float x, float z)
        {
            Bounds b = m.bounds;
            if (x < b.min.x - 0.01f || x > b.max.x + 0.01f || z < b.min.z - 0.01f || z > b.max.z + 0.01f) return float.NaN;
            if (!traceMeshFrame.TryGetValue(m, out int f) || f != Time.frameCount)
            {
                traceVerts[m] = m.vertices;
                traceTris[m] = m.GetTriangles(0);
                traceMeshFrame[m] = Time.frameCount;
            }
            Vector3[] v = traceVerts[m];
            int[] tri = traceTris[m];
            float best = float.NaN;
            for (int k = 0; k + 2 < tri.Length; k += 3)
            {
                Vector3 a = v[tri[k]], bb = v[tri[k + 1]], cc = v[tri[k + 2]];
                if (x < Mathf.Min(a.x, Mathf.Min(bb.x, cc.x)) || x > Mathf.Max(a.x, Mathf.Max(bb.x, cc.x))) continue;
                if (z < Mathf.Min(a.z, Mathf.Min(bb.z, cc.z)) || z > Mathf.Max(a.z, Mathf.Max(bb.z, cc.z))) continue;
                float det = (bb.z - cc.z) * (a.x - cc.x) + (cc.x - bb.x) * (a.z - cc.z);
                if (math.abs(det) < 1e-9f) continue;
                float l1 = ((bb.z - cc.z) * (x - cc.x) + (cc.x - bb.x) * (z - cc.z)) / det;
                float l2 = ((cc.z - a.z) * (x - cc.x) + (a.x - cc.x) * (z - cc.z)) / det;
                float l3 = 1f - l1 - l2;
                if (l1 < -1e-4f || l2 < -1e-4f || l3 < -1e-4f) continue;
                float y = l1 * a.y + l2 * bb.y + l3 * cc.y;
                if (float.IsNaN(best) || y > best) best = y;
            }
            return best;
        }

        /// <summary>Uvolní cache čtení meshů po běhu /perf lodtrace.</summary>
        public void ClearTraceCache()
        {
            traceMeshFrame.Clear(); traceVerts.Clear(); traceTris.Clear();
        }

        /// <summary>
        /// Kolo 5: body, kde je VIDĚT jen zapuštěný sloupec (= schod proti sousedům), a body,
        /// kde se kreslí dvě úrovně naráz (skutečný překryv; zapuštěný se počítá taky).
        /// Doplněk CoverageProbe, který zapuštěné překryvy záměrně ignoruje.
        /// </summary>
        public void SinkStepProbe(float radius, float step, out int samples, out int sunkVisible, out int overlapsAll)
        {
            samples = sunkVisible = overlapsAll = 0;
            if (viewer == null || builders == null) return;
            int levels = builders.Length;
            Vector3 p = viewer.position;
            int n = Mathf.CeilToInt(radius / step);
            for (int iz = -n; iz <= n; iz++)
            for (int ix = -n; ix <= n; ix++)
            {
                float x = p.x + ix * step, z = p.z + iz * step;
                int cover = 0, sunkCover = 0;
                bool wanted = false;
                for (int l = 0; l < levels; l++)
                {
                    float span = SpanOf(l);
                    var key = new int3(Mathf.FloorToInt(x / span), Mathf.FloorToInt(z / span), l);
                    if (desired.Contains(key)) wanted = true;
                    if (columns.TryGetValue(key, out ColumnEntry e) && e.published && !e.replacedHidden && !MaskedAt(key, e, x, z))
                    {
                        if (e.sunk) sunkCover++; else cover++;
                    }
                }
                if (!wanted) continue;
                samples++;
                if (cover == 0 && sunkCover > 0) sunkVisible++;
                if (cover + sunkCover > 1) overlapsAll++;
            }
        }
    }
}
