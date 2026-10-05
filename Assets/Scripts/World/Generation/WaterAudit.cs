using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Unity.Mathematics;
using UnityEngine;

namespace Orivilon.World.Generation
{
    /// <summary>
    /// Měřidlo souladu hladiny a SKUTEČNÉ geometrie terénu kolem hráče.
    ///
    /// <para>Proč existuje: hladina se staví z výškového pole <c>surfY</c>, ale terén je
    /// izoplocha 3D hustoty. Kde se ty dvě věci rozejdou, vznikají ostrůvky trčící z vody,
    /// břehy visící ve vzduchu a díry. Z obrázku se nedá říct, kolik jich je a jestli
    /// oprava pomohla – tohle to spočítá. Terén se čte paprsky do LOD0 colliderů (to je
    /// přesně ta geometrie, kterou hráč vidí v plném rozlišení), hladina rastrováním
    /// trojúhelníků vodních meshů.</para>
    ///
    /// <para>Jen diagnostika, nic nemění. Volá se z konzole: <c>/voda audit [poloměr]</c>.</para>
    /// </summary>
    public static class WaterAudit
    {
        public struct Result
        {
            public int samples, wet, visibleWet, holes;
            public int floatingEdges, steps, islands, islandCells;
            public float worstFloat, worstStep;
            public List<Vector3> floatAt, stepAt, islandAt, holeAt;

            /// <summary>
            /// Kolo 6: přesná kontrola volných hran vodního meshe – hrana, kterou sdílí jen jeden
            /// trojúhelník, se projde po 0,1 m a porovná s terénem (collider) přímo pod ní.
            /// Visí jen tehdy, když je hladina NA SAMOTNÉ HRANĚ nad terénem.
            /// </summary>
            public int freeEdges, edgeFloat;
            public float edgeWorst;
            public List<Vector3> edgeAt;

            /// <summary>
            /// Kolo 7: viditelné strmé vodní trojúhelníky (sklon hladiny nad 40°, těžiště nad
            /// terénem). Střepy u schodu hladiny i čela peřejí – plocha v m² a nejstrmější.
            /// </summary>
            public int steepTris;
            public float steepArea, steepMaxDeg;
            public List<Vector3> steepAt;
        }

        private const float Cell = 0.5f;
        // Mimo celá čísla: paprsek přesně po hraně trojúhelníku umí proklouznout a hlásil by díru, která není.
        private const float OffX = 0.173f, OffZ = 0.131f;
        private const float FloatTol = 0.15f;   // hrana vody víc než tohle nad sousední souší
        private const float StepTol = 0.5f;     // skok hladiny mezi sousedními body
        private const int IslandMaxCells = 160; // 40 m² – co je menší, je artefakt, ne ostrov

        /// <summary>
        /// Kolo 6: detail malého okna – v každém bodě hladina z vodních meshů (i s tím, kolik
        /// ploch ji kryje), všechny zásahy LOD0 colliderů a povrch z pole bodovou cestou.
        /// K hledání příčiny jednotlivé visící hrany (/voda detail x z [půlka] [krok]).
        /// </summary>
        public static string Detail(VoxelTerrain t, float cx, float cz, float half, float step)
        {
            var meshes = new List<Mesh>();
            var cols = new HashSet<Collider>();
            var colColumns = new HashSet<int2>();
            t.SetAuditArea(new Vector3(cx, 0f, cz), half + 8f);
            t.CollectAuditData(meshes, cols, colColumns);
            var sb = new StringBuilder();
            sb.AppendFormat(CultureInfo.InvariantCulture, "[Voda detail] ({0:0.00},{1:0.00}) ±{2} krok {3}\n", cx, cz, half, step);
            sb.Append("  x z | voda(meshů) | terén collider (všechny zásahy) | pole surf / riverCore / riverY / lakeY\n");
            var v = new List<Vector3>();
            var tri = new List<int>();
            var hits = new RaycastHit[16];
            // vodní trojúhelníky v okně (pro výpis vrcholů)
            var near = new List<Vector3>();
            foreach (Mesh m in meshes)
            {
                Bounds b = m.bounds;
                if (b.max.x < cx - half || b.min.x > cx + half || b.max.z < cz - half || b.min.z > cz + half) continue;
                m.GetVertices(v); m.GetTriangles(tri, 0);
                for (int k = 0; k < tri.Count; k += 3)
                {
                    Vector3 a = v[tri[k]], bb = v[tri[k + 1]], c = v[tri[k + 2]];
                    float mnx = Mathf.Min(a.x, Mathf.Min(bb.x, c.x)), mxx = Mathf.Max(a.x, Mathf.Max(bb.x, c.x));
                    float mnz = Mathf.Min(a.z, Mathf.Min(bb.z, c.z)), mxz = Mathf.Max(a.z, Mathf.Max(bb.z, c.z));
                    if (mxx < cx - half || mnx > cx + half || mxz < cz - half || mnz > cz + half) continue;
                    near.Add(a); near.Add(bb); near.Add(c);
                }
            }
            for (float z = cz - half; z <= cz + half + 1e-3f; z += step)
            for (float x = cx - half; x <= cx + half + 1e-3f; x += step)
            {
                float wTop = float.NegativeInfinity; int wc = 0;
                for (int k = 0; k < near.Count; k += 3)
                {
                    if (!Bary(near[k], near[k + 1], near[k + 2], x, z, out float y)) continue;
                    wc++; if (y > wTop) wTop = y;
                }
                int c = Physics.RaycastNonAlloc(new Vector3(x, 2000f, z), Vector3.down, hits, 4000f, ~0, QueryTriggerInteraction.Ignore);
                var ys = new List<float>();
                for (int h = 0; h < c; h++) if (cols.Contains(hits[h].collider)) ys.Add(hits[h].point.y);
                ys.Sort((p, q) => q.CompareTo(p));
                t.ProbeSurface(x, z, 1f, out float surf, out float rc, out float ry, out float ly);
                sb.AppendFormat(CultureInfo.InvariantCulture, "  {0:0.00} {1:0.00} | {2} ({3}) | ", x, z,
                    float.IsNegativeInfinity(wTop) ? "-" : wTop.ToString("0.00", CultureInfo.InvariantCulture), wc);
                for (int i = 0; i < ys.Count && i < 4; i++) sb.Append(ys[i].ToString("0.00", CultureInfo.InvariantCulture)).Append(' ');
                sb.AppendFormat(CultureInfo.InvariantCulture, "| {0:0.00} / {1:0.00} / {2:0.00} / {3:0.00}\n", surf, rc, ry, ly);
            }
            sb.Append("  vodní trojúhelníky v okně:\n");
            for (int k = 0; k < near.Count && k < 3 * 60; k += 3)
                sb.AppendFormat(CultureInfo.InvariantCulture, "   ({0:0.00},{1:0.00},{2:0.00}) ({3:0.00},{4:0.00},{5:0.00}) ({6:0.00},{7:0.00},{8:0.00})\n",
                    near[k].x, near[k].y, near[k].z, near[k + 1].x, near[k + 1].y, near[k + 1].z, near[k + 2].x, near[k + 2].y, near[k + 2].z);
            return sb.ToString();
        }

        /// <summary>
        /// Volné hrany hladiny (sdílí je jediný trojúhelník) a výška terénu přímo pod nimi.
        /// Hrany ležící na hranici sloupce se přeskakují – pokračuje za nimi mesh souseda
        /// (mezery mezi sloupci hlídá LOD audit).
        /// </summary>
        private static void SteepTris(List<Mesh> meshes, HashSet<Collider> cols, Vector3 center, float radius, ref Result r)
        {
            var v = new List<Vector3>();
            var tri = new List<int>();
            var hits = new RaycastHit[16];
            float cosLim = Mathf.Cos(40f * Mathf.Deg2Rad);
            foreach (Mesh m in meshes)
            {
                Bounds b = m.bounds;
                if (b.max.x < center.x - radius || b.min.x > center.x + radius || b.max.z < center.z - radius || b.min.z > center.z + radius) continue;
                m.GetVertices(v);
                m.GetTriangles(tri, 0);
                for (int k = 0; k < tri.Count; k += 3)
                {
                    Vector3 a = v[tri[k]], bb = v[tri[k + 1]], c = v[tri[k + 2]];
                    Vector3 n = Vector3.Cross(bb - a, c - a);
                    float len = n.magnitude;
                    if (len < 1e-6f) continue;
                    float ny = Mathf.Abs(n.y) / len;
                    if (ny >= cosLim) continue;
                    Vector3 g = (a + bb + c) / 3f;
                    float dx = g.x - center.x, dz = g.z - center.z;
                    if (dx * dx + dz * dz > radius * radius) continue;
                    int hc = Physics.RaycastNonAlloc(new Vector3(g.x, 2000f, g.z), Vector3.down, hits, 4000f, ~0, QueryTriggerInteraction.Ignore);
                    float gy = float.NegativeInfinity;
                    for (int h = 0; h < hc; h++) if (cols.Contains(hits[h].collider) && hits[h].point.y > gy) gy = hits[h].point.y;
                    if (!float.IsNegativeInfinity(gy) && g.y < gy + 0.02f) continue;   // schovaný pod terénem
                    float area = 0.5f * new Vector2(n.x, n.z).magnitude / Mathf.Max(1e-6f, len) * len; // průmět do vodorovné roviny ≈ plocha vidět shora
                    r.steepTris++;
                    r.steepArea += 0.5f * len;
                    float deg = Mathf.Acos(ny) * Mathf.Rad2Deg;
                    if (deg > r.steepMaxDeg) r.steepMaxDeg = deg;
                    if (r.steepAt.Count < 5) r.steepAt.Add(g);
                    _ = area;
                }
            }
        }

        /// <summary>Jen pro ověření citlivosti měřidla: hladina se v testu hran zvedne o tolik metrů.</summary>
        public static float EdgeTestLift;

        private static void FreeEdges(List<Mesh> meshes, HashSet<Collider> cols, Vector3 center, float radius, float span, ref Result r)
        {
            var v = new List<Vector3>();
            var tri = new List<int>();
            var count = new Dictionary<(long, long, long, long), int>(4096);
            var edges = new List<(Vector3, Vector3)>(1024);
            var hits = new RaycastHit[16];
            long Q(float f) => (long)Mathf.Round(f * 1000f);
            foreach (Mesh m in meshes)
            {
                Bounds b = m.bounds;
                if (b.max.x < center.x - radius || b.min.x > center.x + radius || b.max.z < center.z - radius || b.min.z > center.z + radius) continue;
                m.GetVertices(v);
                m.GetTriangles(tri, 0);
                count.Clear();
                for (int k = 0; k < tri.Count; k += 3)
                    for (int e = 0; e < 3; e++)
                    {
                        Vector3 a = v[tri[k + e]], c = v[tri[k + (e + 1) % 3]];
                        var ka = (Q(a.x), Q(a.z)); var kc = (Q(c.x), Q(c.z));
                        var key = ka.CompareTo(kc) < 0 ? (ka.Item1, ka.Item2, kc.Item1, kc.Item2) : (kc.Item1, kc.Item2, ka.Item1, ka.Item2);
                        count.TryGetValue(key, out int cnt);
                        count[key] = cnt + 1;
                    }
                for (int k = 0; k < tri.Count; k += 3)
                    for (int e = 0; e < 3; e++)
                    {
                        Vector3 a = v[tri[k + e]], c = v[tri[k + (e + 1) % 3]];
                        var ka = (Q(a.x), Q(a.z)); var kc = (Q(c.x), Q(c.z));
                        var key = ka.CompareTo(kc) < 0 ? (ka.Item1, ka.Item2, kc.Item1, kc.Item2) : (kc.Item1, kc.Item2, ka.Item1, ka.Item2);
                        if (count[key] != 1) continue;
                        bool onX = Mathf.Abs(a.x - Mathf.Round(a.x / span) * span) < 1e-3f && Mathf.Abs(c.x - Mathf.Round(c.x / span) * span) < 1e-3f && Mathf.Abs(a.x - c.x) < 1e-3f;
                        bool onZ = Mathf.Abs(a.z - Mathf.Round(a.z / span) * span) < 1e-3f && Mathf.Abs(c.z - Mathf.Round(c.z / span) * span) < 1e-3f && Mathf.Abs(a.z - c.z) < 1e-3f;
                        if (onX || onZ) continue;
                        edges.Add((a, c));
                    }
            }

            foreach (var (a, c) in edges)
            {
                float len = Vector2.Distance(new Vector2(a.x, a.z), new Vector2(c.x, c.z));
                int steps = Mathf.Max(2, Mathf.CeilToInt(len / 0.1f));
                bool counted = false, inside = false;
                float worst = 0f; Vector3 worstAt = default;
                for (int s = 0; s <= steps; s++)
                {
                    Vector3 p = Vector3.Lerp(a, c, s / (float)steps);
                    float dx = p.x - center.x, dz = p.z - center.z;
                    if (dx * dx + dz * dz > radius * radius) continue;
                    inside = true;
                    int hc = Physics.RaycastNonAlloc(new Vector3(p.x, 2000f, p.z), Vector3.down, hits, 4000f, ~0, QueryTriggerInteraction.Ignore);
                    float g = float.NegativeInfinity;
                    for (int h = 0; h < hc; h++) if (cols.Contains(hits[h].collider) && hits[h].point.y > g) g = hits[h].point.y;
                    if (float.IsNegativeInfinity(g)) continue;
                    float gap = p.y + EdgeTestLift - g;
                    if (gap > worst) { worst = gap; worstAt = p; }
                }
                if (!inside) continue;
                r.freeEdges++;
                if (worst > FloatTol)
                {
                    r.edgeFloat++;
                    if (r.edgeAt.Count < 5) r.edgeAt.Add(worstAt);
                    counted = true;
                }
                if (worst > r.edgeWorst) r.edgeWorst = worst;
                _ = counted;
            }
        }

        private static bool Bary(Vector3 a, Vector3 b, Vector3 c, float px, float pz, out float y)
        {
            y = 0f;
            float det = (b.z - c.z) * (a.x - c.x) + (c.x - b.x) * (a.z - c.z);
            if (Mathf.Abs(det) < 1e-8f) return false;
            float l1 = ((b.z - c.z) * (px - c.x) + (c.x - b.x) * (pz - c.z)) / det;
            float l2 = ((c.z - a.z) * (px - c.x) + (a.x - c.x) * (pz - c.z)) / det;
            float l3 = 1f - l1 - l2;
            if (l1 < -1e-4f || l2 < -1e-4f || l3 < -1e-4f) return false;
            y = l1 * a.y + l2 * b.y + l3 * c.y;
            return true;
        }

        public static Result Run(VoxelTerrain t, Vector3 center, float radius)
        {
            var r = new Result
            {
                floatAt = new List<Vector3>(), stepAt = new List<Vector3>(),
                islandAt = new List<Vector3>(), holeAt = new List<Vector3>(), edgeAt = new List<Vector3>(),
                steepAt = new List<Vector3>()
            };

            var meshes = new List<Mesh>();
            var cols = new HashSet<Collider>();
            var colColumns = new HashSet<int2>();
            t.SetAuditArea(center, radius);
            t.CollectAuditData(meshes, cols, colColumns);

            int n = Mathf.CeilToInt(radius * 2f / Cell) + 1;
            float x0 = center.x - radius, z0 = center.z - radius;

            var water = new float[n * n];
            var ground = new float[n * n];
            var valid = new bool[n * n];
            for (int i = 0; i < water.Length; i++) { water[i] = float.NegativeInfinity; ground[i] = float.NegativeInfinity; }

            // ── hladina: rastr trojúhelníků, v bodě se drží nejvyšší ──
            var v = new List<Vector3>();
            var tri = new List<int>();
            foreach (Mesh m in meshes)
            {
                Bounds b = m.bounds;
                if (b.max.x < x0 || b.min.x > x0 + radius * 2f || b.max.z < z0 || b.min.z > z0 + radius * 2f) continue;
                m.GetVertices(v);
                m.GetTriangles(tri, 0);
                for (int k = 0; k < tri.Count; k += 3)
                    Raster(v[tri[k]], v[tri[k + 1]], v[tri[k + 2]], water, n, x0, z0);
            }

            // ── terén: paprsek shora, nejvyšší zásah do LOD0 collideru ──
            var hits = new RaycastHit[16];
            float span = t.Lod0Span;
            for (int iz = 0; iz < n; iz++)
            for (int ix = 0; ix < n; ix++)
            {
                float x = x0 + ix * Cell + OffX, z = z0 + iz * Cell + OffZ;
                var dxz = new Vector2(x - center.x, z - center.z);
                if (dxz.sqrMagnitude > radius * radius) continue;
                var col = new int2(Mathf.FloorToInt(x / span), Mathf.FloorToInt(z / span));
                if (!colColumns.Contains(col)) continue;

                int i = iz * n + ix;
                valid[i] = true;
                r.samples++;

                int c = Physics.RaycastNonAlloc(new Vector3(x, 2000f, z), Vector3.down, hits, 4000f,
                                                ~0, QueryTriggerInteraction.Ignore);
                float best = float.NegativeInfinity;
                for (int h = 0; h < c; h++)
                    if (cols.Contains(hits[h].collider) && hits[h].point.y > best) best = hits[h].point.y;

                ground[i] = best;
                if (float.IsNegativeInfinity(best))
                {
                    r.holes++;
                    if (r.holeAt.Count < 5) r.holeAt.Add(new Vector3(x, 0f, z));
                }
            }

            FreeEdges(meshes, cols, center, radius, t.Lod0Span, ref r);
            SteepTris(meshes, cols, center, radius, ref r);

            // ── klasifikace ──
            var vis = new bool[n * n];
            for (int i = 0; i < vis.Length; i++)
            {
                if (!valid[i] || float.IsNegativeInfinity(water[i])) continue;
                r.wet++;
                if (water[i] > ground[i] + 0.02f) { vis[i] = true; r.visibleWet++; }
            }

            for (int iz = 1; iz < n - 1; iz++)
            for (int ix = 1; ix < n - 1; ix++)
            {
                int i = iz * n + ix;
                if (!valid[i] || !vis[i]) continue;
                float lv = water[i];

                for (int k = 0; k < 4; k++)
                {
                    int j = k == 0 ? i - 1 : k == 1 ? i + 1 : k == 2 ? i - n : i + n;
                    if (!valid[j] || float.IsNegativeInfinity(ground[j])) continue;

                    if (float.IsNegativeInfinity(water[j]))
                    {
                        // Soused nemá vodu vůbec a jeho terén je pod naší hladinou:
                        // hrana hladiny visí ve vzduchu a pod ní je vidět.
                        float gap = lv - ground[j];
                        if (gap > FloatTol)
                        {
                            r.floatingEdges++;
                            if (gap > r.worstFloat) r.worstFloat = gap;
                            if (r.floatAt.Count < 5 || gap > 1f) Keep(r.floatAt, new Vector3(x0 + ix * Cell, lv, z0 + iz * Cell));
                        }
                    }
                    else if (vis[j])
                    {
                        float d = Mathf.Abs(water[j] - lv);
                        if (d > StepTol && j > i)
                        {
                            r.steps++;
                            if (d > r.worstStep) r.worstStep = d;
                            Keep(r.stepAt, new Vector3(x0 + ix * Cell, lv, z0 + iz * Cell));
                        }
                    }
                }
            }

            // ── ostrůvky: suché komponenty celé obklopené viditelnou vodou ──
            var seen = new bool[n * n];
            var stack = new Stack<int>();
            for (int s = 0; s < vis.Length; s++)
            {
                if (!valid[s] || vis[s] || seen[s]) continue;
                bool enclosed = true;
                int size = 0;
                float sx = 0f, sz = 0f;
                stack.Push(s); seen[s] = true;
                while (stack.Count > 0)
                {
                    int i = stack.Pop();
                    size++;
                    int ix = i % n, iz = i / n;
                    sx += ix; sz += iz;
                    if (ix == 0 || iz == 0 || ix == n - 1 || iz == n - 1) { enclosed = false; continue; }
                    for (int k = 0; k < 4; k++)
                    {
                        int j = k == 0 ? i - 1 : k == 1 ? i + 1 : k == 2 ? i - n : i + n;
                        if (!valid[j]) { enclosed = false; continue; }
                        if (vis[j] || seen[j]) continue;
                        seen[j] = true;
                        stack.Push(j);
                    }
                }
                if (enclosed && size <= IslandMaxCells)
                {
                    r.islands++;
                    r.islandCells += size;
                    int ci = Mathf.RoundToInt(sz / size) * n + Mathf.RoundToInt(sx / size);
                    Keep(r.islandAt, new Vector3(x0 + sx / size * Cell, ground[Mathf.Clamp(ci, 0, ground.Length - 1)], z0 + sz / size * Cell));
                }
            }

            return r;
        }

        private static void Keep(List<Vector3> list, Vector3 p)
        {
            if (list.Count < 5) list.Add(p);
        }

        private static void Raster(Vector3 a, Vector3 b, Vector3 c, float[] grid, int n, float x0, float z0)
        {
            float minX = Mathf.Min(a.x, Mathf.Min(b.x, c.x)), maxX = Mathf.Max(a.x, Mathf.Max(b.x, c.x));
            float minZ = Mathf.Min(a.z, Mathf.Min(b.z, c.z)), maxZ = Mathf.Max(a.z, Mathf.Max(b.z, c.z));
            int ix0 = Mathf.Max(0, Mathf.CeilToInt((minX - x0 - OffX) / Cell)), ix1 = Mathf.Min(n - 1, Mathf.FloorToInt((maxX - x0 - OffX) / Cell));
            int iz0 = Mathf.Max(0, Mathf.CeilToInt((minZ - z0 - OffZ) / Cell)), iz1 = Mathf.Min(n - 1, Mathf.FloorToInt((maxZ - z0 - OffZ) / Cell));
            if (ix0 > ix1 || iz0 > iz1) return;

            float det = (b.z - c.z) * (a.x - c.x) + (c.x - b.x) * (a.z - c.z);
            if (Mathf.Abs(det) < 1e-8f) return;

            for (int iz = iz0; iz <= iz1; iz++)
            for (int ix = ix0; ix <= ix1; ix++)
            {
                float px = x0 + ix * Cell + OffX, pz = z0 + iz * Cell + OffZ;
                float l1 = ((b.z - c.z) * (px - c.x) + (c.x - b.x) * (pz - c.z)) / det;
                float l2 = ((c.z - a.z) * (px - c.x) + (a.x - c.x) * (pz - c.z)) / det;
                float l3 = 1f - l1 - l2;
                if (l1 < -1e-4f || l2 < -1e-4f || l3 < -1e-4f) continue;
                float y = l1 * a.y + l2 * b.y + l3 * c.y;
                int i = iz * n + ix;
                if (y > grid[i]) grid[i] = y;
            }
        }

        // ── audit hranic LOD ───────────────────────────────────────────

        /// <summary>
        /// Projde hranice mezi zveřejněnými sloupci RŮZNÉHO LOD a porovná, co je vidět
        /// z obou stran: výšku terénu (rastr trojúhelníků meshů chunků) a hladinu (rastr
        /// vodního meshe). Terén a voda se na hranici prstenců musí potkat, jinak je
        /// při pohybu vidět prstenec, schod nebo mezera. Vzorkuje se po 0,5 m podél
        /// hranice, 0,15 m od ní na každou stranu.
        /// </summary>
        /// <summary>Kolo 15: odsazení vzorku hladiny od švu (m). Dřív 0,02 jako u terénu.</summary>
        public static float WaterSeamOffset = 0.002f;

        private static int s0N, s0Gap, s0Step;
        private static float s0GapMax, s0StepMax;
        private static readonly StringBuilder s0At = new StringBuilder();

        /// <summary>Kolo 22: hladina na hraně dvou LOD0 sloupců – stejná kritéria jako mezi LOD.</summary>
        private static void SameLod0(Mesh ma, Mesh mb, float px, float pz, float nx, float nz, float ta, float tb)
        {
            float ax = px - nx * WaterSeamOffset, az = pz - nz * WaterSeamOffset;
            float bx = px + nx * WaterSeamOffset, bz = pz + nz * WaterSeamOffset;
            float wa = Brute(ma, ax, az), wb = Brute(mb, bx, bz);
            if (float.IsNaN(wa)) wa = float.NegativeInfinity;
            if (float.IsNaN(wb)) wb = float.NegativeInfinity;
            bool va = !float.IsNegativeInfinity(wa) && wa > ta + 0.02f;
            bool vb = !float.IsNegativeInfinity(wb) && wb > tb + 0.02f;
            if (!va && !vb) return;
            s0N++;
            if (va && vb)
            {
                float d = Mathf.Abs(wa - wb);
                if (d > 0.15f) { s0Step++; if (d > s0StepMax) s0StepMax = d; if (s0At.Length < 200) s0At.AppendFormat(CultureInfo.InvariantCulture, " schod({0:0.0},{1:0.0}) {2:0.00}", px, pz, d); }
            }
            else
            {
                float g = (va ? wa : wb) - (va ? tb : ta);
                if (g > 0.15f) { s0Gap++; if (g > s0GapMax) s0GapMax = g; if (s0At.Length < 200) s0At.AppendFormat(CultureInfo.InvariantCulture, " mezera({0:0.0},{1:0.0}) {2:0.00}", px, pz, g); }
            }
        }

        // Kolo 24: terén na hraně dvou LOD0 sloupců na šitých přímkách (LineLevel > 0).
        private static int t0N, t0Gap, t0Step, t0Mid;
        private static float t0Max;
        private static readonly StringBuilder t0At = new StringBuilder();

        private static void SameLod0Terrain(float ta, float tb, float px, float pz)
        {
            t0N++;
            float d = Mathf.Abs(ta - tb);
            if (d > t0Max) t0Max = d;
            if (d > 0.05f) t0Mid++;
            if (d > 0.15f) { t0Step++; if (t0At.Length < 200) t0At.AppendFormat(CultureInfo.InvariantCulture, " ({0:0.0},{1:0.0}) {2:0.00}", px, pz, d); }
        }

        public static string RunLod(VoxelTerrain t, Vector3 center, float radius)
        {
            var cols = new List<VoxelTerrain.LodColumnInfo>();
            t.CollectLodData(center, radius, cols);

            // Rychlé hledání majitele bodu: pro každý LOD slovník souřadnic.
            var owner = new Dictionary<int3, int>();
            for (int i = 0; i < cols.Count; i++)
                owner[new int3(cols[i].coord.x, cols[i].coord.y, cols[i].lod)] = i;

            var terrainGrid = new Dictionary<int, Grid>();
            var waterGrid = new Dictionary<int, Grid>();

            int borders = 0, samples = 0, tStep = 0, wGap = 0, wStep = 0;
            float tMax = 0f, wGapMax = 0f, wStepMax = 0f;
            double tSum = 0;
            var tAt = new List<Vector3>(); var gapAt = new List<Vector3>(); var stepAt = new List<Vector3>();
            const float off = 0.02f, step = 0.5f;
            var gapDetail = new StringBuilder();
            var stepDetail = new StringBuilder();
            var pairN = new Dictionary<int, int>(); var pairBad = new Dictionary<int, int>(); var pairMax = new Dictionary<int, float>(); var pairAt = new Dictionary<int, Vector3>(); var pairMid = new Dictionary<int, int>(); var pairTxt = new Dictionary<int, string>();

            for (int i = 0; i < cols.Count; i++)
            {
                VoxelTerrain.LodColumnInfo a = cols[i];
                float x0 = a.coord.x * a.span, z0 = a.coord.y * a.span;
                // čtyři strany sloupce; soused se hledá za hranou
                for (int side = 0; side < 4; side++)
                {
                    bool counted = false;
                    for (float u = step * 0.5f; u < a.span; u += step)
                    {
                        float px, pz, nx, nz;
                        if (side == 0) { px = x0; pz = z0 + u; nx = -1; nz = 0; }
                        else if (side == 1) { px = x0 + a.span; pz = z0 + u; nx = 1; nz = 0; }
                        else if (side == 2) { px = x0 + u; pz = z0; nx = 0; nz = -1; }
                        else { px = x0 + u; pz = z0 + a.span; nx = 0; nz = 1; }

                        if ((px - center.x) * (px - center.x) + (pz - center.z) * (pz - center.z) > radius * radius) continue;

                        float bx = px + nx * off, bz = pz + nz * off;
                        int j = FindOwner(owner, t.Lod0Span, bx, bz);
                        if (j < 0 || cols[j].lod < a.lod) continue;   // každou hranici jednou: z jemnější strany
                        // Kolo 22: i LOD0|LOD0 na přímkách, kde se hladina jinak šije (LineLevel > 0) –
                        // tam se teď šev vynechává. Každá hrana jednou (strana +x / +z), jen voda.
                        bool same = cols[j].lod == a.lod;
                        if (same && (a.lod != 0 || (side != 1 && side != 3)
                                     || LodSeamJob.LineLevel(side == 1 ? px : pz, t.Lod0Span) <= 0)) continue;
                        if (!same && !counted) { borders++; counted = true; }

                        float ax = px - nx * off, az = pz - nz * off;
                        float ta = HeightAt(terrainGrid, i, cols[i].terrain, ax, az, true);
                        float tb = HeightAt(terrainGrid, j, cols[j].terrain, bx, bz, true);
                        if (same && float.IsNegativeInfinity(ta) != float.IsNegativeInfinity(tb)) { t0N++; t0Gap++; continue; }   // kolo 24: díra jen na jedné straně
                        if (float.IsNegativeInfinity(ta) || float.IsNegativeInfinity(tb)) continue;
                        if (same) { SameLod0Terrain(ta, tb, px, pz); SameLod0(cols[i].water, cols[j].water, px, pz, nx, nz, ta, tb); continue; }
                        samples++;

                        float dt = Mathf.Abs(ta - tb);
                        tSum += dt;
                        int pk = a.lod * 10 + cols[j].lod;
                        pairN.TryGetValue(pk, out int pn); pairN[pk] = pn + 1;
                        if (dt > 0.5f) { pairBad.TryGetValue(pk, out int pb); pairBad[pk] = pb + 1; } if (dt > 0.15f) { pairMid.TryGetValue(pk, out int pq); pairMid[pk] = pq + 1; }
                        pairMax.TryGetValue(pk, out float pm); if (dt > pm) { pairMax[pk] = dt; pairAt[pk] = new Vector3(px, (ta + tb) * 0.5f, pz); pairTxt[pk] = string.Format(CultureInfo.InvariantCulture, " [L{0} {1:0.00} | L{2} {3:0.00}]", a.lod, ta, cols[j].lod, tb); }
                        if (dt > tMax) tMax = dt;
                        if (dt > 0.5f) { tStep++; if (tAt.Count < 5 || dt >= tMax) Keep5(tAt, new Vector3(px, dt, pz)); }

                        // Kolo 15: hladina se měří jen 2 mm od švu. Při 2 cm ukazoval audit „schod" tam,
                        // kde je hladina u švu strmá (jezero → suchý uzel lomené čáry, ~6 m/m): obě strany
                        // se na samotném švu potkají přesně, ale 2 × 2 cm × spád dalo 0,23 m (s1337 (−256; 121)).
                        float wax = px - nx * WaterSeamOffset, waz = pz - nz * WaterSeamOffset;
                        float wbx = px + nx * WaterSeamOffset, wbz = pz + nz * WaterSeamOffset;
                        ax = wax; az = waz;
                        float wa = HeightAt(waterGrid, i, Single(cols[i].water), wax, waz, false);
                        float wb = HeightAt(waterGrid, j, Single(cols[j].water), wbx, wbz, false);
                        bx = wbx; bz = wbz;
                        // Rastr umí u okraje meshe minout tenký trojúhelník – kde hlásí „bez vody",
                        // ověří se to přímým testem proti trojúhelníkům.
                        if (float.IsNegativeInfinity(wa)) wa = Brute(cols[i].water, ax, az);
                        if (float.IsNegativeInfinity(wb)) wb = Brute(cols[j].water, bx, bz);
                        if (float.IsNaN(wa)) wa = float.NegativeInfinity;
                        if (float.IsNaN(wb)) wb = float.NegativeInfinity;
                        bool va = !float.IsNegativeInfinity(wa) && wa > ta + 0.02f;
                        bool vb = !float.IsNegativeInfinity(wb) && wb > tb + 0.02f;

                        if (va && vb)
                        {
                            float d = Mathf.Abs(wa - wb);
                            if (d > 0.15f)
                            {
                                wStep++; if (d > wStepMax) wStepMax = d; Keep5(stepAt, new Vector3(px, d, pz));
                                // Kolo 15: detail schodu – oba sloupce, LOD, terén a hladina z rastru i přímo z trojúhelníků.
                                if (stepDetail.Length < 900)
                                    stepDetail.AppendFormat(CultureInfo.InvariantCulture,
                                        "    step@({0:0.0},{1:0.0}) L{2} col({3},{4}): t={5:0.00} w={6:0.00} brute={7:0.00} vtx={8} | L{9} col({10},{11}): t={12:0.00} w={13:0.00} brute={14:0.00} vtx={15}\n",
                                        px, pz, a.lod, a.coord.x, a.coord.y, ta, wa, Brute(cols[i].water, ax, az), cols[i].water != null ? cols[i].water.vertexCount : 0,
                                        cols[j].lod, cols[j].coord.x, cols[j].coord.y, tb, wb, Brute(cols[j].water, bx, bz), cols[j].water != null ? cols[j].water.vertexCount : 0);
                            }
                        }
                        else if (va != vb)
                        {
                            // Voda jen na jedné straně: vadí, když je hladina nad terénem druhé strany –
                            // pak je na hranici vidět svislý řez vodou nebo mezera pod ní.
                            float lv = va ? wa : wb, other = va ? tb : ta;
                            float g = lv - other;
                            if (g > 0.15f)
                            {
                                wGap++; if (g > wGapMax) wGapMax = g; Keep5(gapAt, new Vector3(px, g, pz));
                                if (gapDetail.Length < 600)
                                    gapDetail.AppendFormat(CultureInfo.InvariantCulture,
                                        "    gap@({0:0.0},{1:0.0}) L{2}: t={3:0.00} w={4:0.00} brute={8:0.00} | L{5}: t={6:0.00} w={7:0.00} brute={9:0.00} meshB={10}\n",
                                        px, pz, a.lod, ta, wa, cols[j].lod, tb, wb,
                                        Brute(cols[i].water, ax, az), Brute(cols[j].water, bx, bz),
                                        cols[j].water != null ? cols[j].water.bounds.ToString() : "null");
                            }
                        }
                    }
                }
            }

            var sb = new StringBuilder();
            string same0 = string.Format(CultureInfo.InvariantCulture,
                "  LOD0|LOD0 na šitých přímkách (kolo 22, jen voda): {0} vzorků s vodou, mezera {1}× (max {2:0.00} m), schod {3}× (max {4:0.00} m){5}\n",
                s0N, s0Gap, s0GapMax, s0Step, s0StepMax, s0At.Length > 0 ? " @" + s0At : "");
            s0N = s0Gap = s0Step = 0; s0GapMax = s0StepMax = 0f; s0At.Length = 0;
            same0 += string.Format(CultureInfo.InvariantCulture,
                "  LOD0|LOD0 na šitých přímkách (kolo 24, terén): {0} vzorků, mezera {1}×, schod >0,15 m {2}×, >0,05 m {3}×, max {4:0.000} m{5}\n",
                t0N, t0Gap, t0Step, t0Mid, t0Max, t0At.Length > 0 ? " @" + t0At : "");
            t0N = t0Gap = t0Step = t0Mid = 0; t0Max = 0f; t0At.Length = 0;
            if (VoxelTerrain.instance != null) same0 += "  " + VoxelTerrain.instance.TerrainSeamStatus() + "\n";
            sb.AppendFormat(CultureInfo.InvariantCulture,
                "[LodAudit] r={0:0} m: {1} sloupců, {2} hran mezi LOD, {3} vzorků | terén: průměr {4:0.00} m, max {5:0.00} m, >0,5 m {6}× | voda: mezera {7}× (max {8:0.00} m), schod {9}× (max {10:0.00} m)\n",
                radius, cols.Count, borders, samples, samples > 0 ? tSum / samples : 0, tMax, tStep, wGap, wGapMax, wStep, wStepMax);
            foreach (var kv in pairN)
            {
                pairBad.TryGetValue(kv.Key, out int pb); pairMax.TryGetValue(kv.Key, out float pm);
                pairAt.TryGetValue(kv.Key, out Vector3 pa);
                sb.AppendFormat(CultureInfo.InvariantCulture, "  LOD{0}|LOD{1}: {2} vzorků, >0,5 m {3}×, >0,15 m {6}×, max {4:0.00} m v ({5:0.0}, {7:0.0}, {8:0.0})\n",
                    kv.Key / 10, kv.Key % 10, kv.Value, pb, pm, pa.x, pairMid.TryGetValue(kv.Key, out int pmid) ? pmid : 0, pa.y, pa.z);
                if (pairTxt.TryGetValue(kv.Key, out string ptx)) { sb.Length--; sb.Append(ptx).Append('\n'); }
            }
            Where(sb, "  terén (x, rozdíl, z)", tAt);
            Where(sb, "  mezera vody", gapAt);
            sb.Append(gapDetail);
            Where(sb, "  schod vody", stepAt);
            sb.Append(stepDetail);
            sb.Append(same0);
            return sb.ToString();
        }

        private static float Brute(Mesh m, float x, float z)
        {
            if (m == null) return float.NaN;
            var v = new List<Vector3>(); var tri = new List<int>();
            m.GetVertices(v); m.GetTriangles(tri, 0);
            float best = float.NegativeInfinity;
            for (int k = 0; k < tri.Count; k += 3)
            {
                Vector3 a = v[tri[k]], b = v[tri[k + 1]], c = v[tri[k + 2]];
                float det = (b.z - c.z) * (a.x - c.x) + (c.x - b.x) * (a.z - c.z);
                if (Mathf.Abs(det) < 1e-9f) continue;
                float l1 = ((b.z - c.z) * (x - c.x) + (c.x - b.x) * (z - c.z)) / det;
                float l2 = ((c.z - a.z) * (x - c.x) + (a.x - c.x) * (z - c.z)) / det;
                float l3 = 1f - l1 - l2;
                if (l1 < -1e-4f || l2 < -1e-4f || l3 < -1e-4f) continue;
                best = Mathf.Max(best, l1 * a.y + l2 * b.y + l3 * c.y);
            }
            return best;
        }

        private static List<Mesh> Single(Mesh m)
        {
            var l = new List<Mesh>(1);
            if (m != null) l.Add(m);
            return l;
        }

        private static void Keep5(List<Vector3> list, Vector3 v)
        {
            if (list.Count < 5) list.Add(v);
            else
            {
                int mi = 0;
                for (int k = 1; k < list.Count; k++) if (list[k].y < list[mi].y) mi = k;
                if (v.y > list[mi].y) list[mi] = v;
            }
        }

        private static int FindOwner(Dictionary<int3, int> owner, float baseSpan, float x, float z)
        {
            for (int l = 0; l < 6; l++)
            {
                float span = baseSpan * (1 << l);
                var k = new int3(Mathf.FloorToInt(x / span), Mathf.FloorToInt(z / span), l);
                if (owner.TryGetValue(k, out int i)) return i;
            }
            return -1;
        }

        /// <summary>
        /// Přihrádky trojúhelníků jednoho sloupce. Výška se v dotazovaném bodě počítá
        /// PŘESNĚ z trojúhelníků (max přes ty, které bod obsahují). Dřívější rastr bral výšku
        /// ve středu buňky 0,125–0,25 m daleko, což na strmém svahu samo dělalo „šev" 0,3–0,5 m.
        /// </summary>
        private sealed class Grid
        {
            public float x0, z0, cell;
            public int n;
            public List<int>[] bins;
            public Vector3[] tv;
        }

        private static float HeightAt(Dictionary<int, Grid> cache, int key, List<Mesh> meshes, float x, float z, bool terrain)
        {
            if (!cache.TryGetValue(key, out Grid g))
            {
                g = BuildGrid(meshes);
                cache[key] = g;
            }
            if (g == null) return float.NegativeInfinity;
            int ix = Mathf.FloorToInt((x - g.x0) / g.cell), iz = Mathf.FloorToInt((z - g.z0) / g.cell);
            if (ix < 0 || iz < 0 || ix >= g.n || iz >= g.n) return float.NegativeInfinity;
            List<int> bin = g.bins[iz * g.n + ix];
            float best = float.NegativeInfinity;
            if (bin == null) return best;
            foreach (int t in bin)
            {
                Vector3 a = g.tv[t], b = g.tv[t + 1], c = g.tv[t + 2];
                float det = (b.z - c.z) * (a.x - c.x) + (c.x - b.x) * (a.z - c.z);
                float l1 = ((b.z - c.z) * (x - c.x) + (c.x - b.x) * (z - c.z)) / det;
                float l2 = ((c.z - a.z) * (x - c.x) + (a.x - c.x) * (z - c.z)) / det;
                float l3 = 1f - l1 - l2;
                if (l1 < -1e-4f || l2 < -1e-4f || l3 < -1e-4f) continue;
                float y = l1 * a.y + l2 * b.y + l3 * c.y;
                if (y > best) best = y;
            }
            return best;
        }

        private static Grid BuildGrid(List<Mesh> meshes)
        {
            if (meshes == null || meshes.Count == 0) return null;
            Bounds bb = meshes[0].bounds;
            for (int i = 1; i < meshes.Count; i++) bb.Encapsulate(meshes[i].bounds);
            float ext = Mathf.Max(bb.size.x, bb.size.z);
            var g = new Grid { cell = Mathf.Max(1f, ext / 128f) };
            g.n = Mathf.CeilToInt(ext / g.cell) + 2;
            g.x0 = bb.min.x - g.cell; g.z0 = bb.min.z - g.cell;
            g.bins = new List<int>[g.n * g.n];

            var all = new List<Vector3>();
            var v = new List<Vector3>();
            var tri = new List<int>();
            foreach (Mesh m in meshes)
            {
                m.GetVertices(v);
                m.GetTriangles(tri, 0);
                for (int k = 0; k < tri.Count; k += 3)
                {
                    Vector3 a = v[tri[k]], b = v[tri[k + 1]], c = v[tri[k + 2]];
                    // svislé trojúhelníky (sukně) přeskočit – nejsou povrch
                    float det = (b.z - c.z) * (a.x - c.x) + (c.x - b.x) * (a.z - c.z);
                    if (Mathf.Abs(det) < 1e-6f) continue;
                    int t = all.Count;
                    all.Add(a); all.Add(b); all.Add(c);
                    float minX = Mathf.Min(a.x, Mathf.Min(b.x, c.x)), maxX = Mathf.Max(a.x, Mathf.Max(b.x, c.x));
                    float minZ = Mathf.Min(a.z, Mathf.Min(b.z, c.z)), maxZ = Mathf.Max(a.z, Mathf.Max(b.z, c.z));
                    int ix0 = Mathf.Max(0, Mathf.FloorToInt((minX - 1e-3f - g.x0) / g.cell)), ix1 = Mathf.Min(g.n - 1, Mathf.FloorToInt((maxX + 1e-3f - g.x0) / g.cell));
                    int iz0 = Mathf.Max(0, Mathf.FloorToInt((minZ - 1e-3f - g.z0) / g.cell)), iz1 = Mathf.Min(g.n - 1, Mathf.FloorToInt((maxZ + 1e-3f - g.z0) / g.cell));
                    for (int iz = iz0; iz <= iz1; iz++)
                    for (int ix = ix0; ix <= ix1; ix++)
                    {
                        int i = iz * g.n + ix;
                        (g.bins[i] ??= new List<int>(8)).Add(t);
                    }
                }
            }
            g.tv = all.ToArray();
            return g;
        }

        public static string Describe(in Result r, float radius)
        {
            var sb = new StringBuilder();
            float wet = Mathf.Max(1, r.visibleWet);
            sb.AppendFormat(CultureInfo.InvariantCulture,
                "[WaterAudit] r={0:0} m, {1} bodů (0,5 m), voda {2} (viditelná {3}), díry v terénu {4}\n",
                radius, r.samples, r.wet, r.visibleWet, r.holes);
            sb.AppendFormat(CultureInfo.InvariantCulture,
                "  visící hrana {0} ({1:0.00}% vody, nejhorší {2:0.00} m) | skoky hladiny {3} (nejhorší {4:0.00} m) | ostrůvky {5} ({6:0.0} m²)\n",
                r.floatingEdges, 100f * r.floatingEdges / wet, r.worstFloat, r.steps, r.worstStep,
                r.islands, r.islandCells * Cell * Cell);
            sb.AppendFormat(CultureInfo.InvariantCulture,
                "  přesně na hranách: volných hran {0}, visí nad terénem {1} (nejvíc {2:0.00} m nad zemí přímo pod hranou)\n",
                r.freeEdges, r.edgeFloat, r.edgeWorst);
            Where(sb, "  hrana visí", r.edgeAt);
            sb.AppendFormat(CultureInfo.InvariantCulture,
                "  strmé vodní trojúhelníky (> 40°, nad terénem): {0} ({1:0.0} m², nejstrmější {2:0}°)\n",
                r.steepTris, r.steepArea, r.steepMaxDeg);
            Where(sb, "  strmý", r.steepAt);
            Where(sb, "  visící hrana", r.floatAt);
            Where(sb, "  skok", r.stepAt);
            Where(sb, "  ostrůvek", r.islandAt);
            Where(sb, "  díra", r.holeAt);
            return sb.ToString();
        }

        private static void Where(StringBuilder sb, string label, List<Vector3> at)
        {
            if (at.Count == 0) return;
            sb.Append(label).Append(": ");
            for (int i = 0; i < at.Count; i++)
                sb.AppendFormat(CultureInfo.InvariantCulture, "{0}({1:0} {2:0.0} {3:0})", i > 0 ? ", " : "", at[i].x, at[i].y, at[i].z);
            sb.Append('\n');
        }
    }
}
