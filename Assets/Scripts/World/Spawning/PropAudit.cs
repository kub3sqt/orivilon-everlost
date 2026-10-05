using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Orivilon.World.Spawning;
using Unity.Mathematics;
using UnityEngine;

namespace Orivilon.World.Generation
{
    /// <summary>
    /// Audit osazení proti SKUTEČNÉ geometrii: terén se měří paprskem do LOD0 colliderů
    /// (vynucených v okruhu jako u auditu vody), hladina rastrem vodních meshů. Nic z pole
    /// ani z osazovače se nepoužívá – audit je nezávislý na tom, co kontroluje.
    /// </summary>
    public static class PropAudit
    {
        private const float Cell = 0.5f;
        private const float OffX = 0.173f, OffZ = 0.131f;

        private static readonly string[] CatName = { "stromy", "kameny", "trava", "male" };
        private static readonly string[] BiomeName = { "louka", "niva", "hory", "pobrezi" };

        public static string Stat(VoxelTerrain t, Vector3 center, float radius)
        {
            var spawners = new List<ObjectSpawner>();
            t.CollectPropSpawners(spawners);
            var counts = new int[4, 4];
            int columns = 0, placements = 0;
            var perKind = new Dictionary<string, int>(64);

            var detailBiome = new int[4];
            foreach (ObjectSpawner sp in spawners)
            {
                List<EcoPlacement> list = sp.EcoPlaced;
                if (list == null) continue;
                bool any = false;
                int[] db = sp.EcoDetailBiome;
                if (db != null)
                {
                    Vector3 c0 = sp.transform.parent != null && sp.transform.parent.childCount > 0 ? sp.transform.position : Vector3.zero;
                    if (list.Count > 0) c0 = list[0].position;
                    float ddx = c0.x - center.x, ddz = c0.z - center.z;
                    if (list.Count == 0 || ddx * ddx + ddz * ddz <= radius * radius)
                        for (int b = 0; b < 4; b++) detailBiome[b] += db[b];
                }
                foreach (EcoPlacement p in list)
                {
                    float dx = p.position.x - center.x, dz = p.position.z - center.z;
                    if (dx * dx + dz * dz > radius * radius) continue;
                    SpawnableObject so = sp.GetSpawnable(p.spawnable);
                    if (so == null) continue;
                    counts[p.biome & 3, (int)so.category]++;
                    placements++;
                    any = true;
                    if (so.category != SpawnCategory.Grass)
                    {
                        perKind.TryGetValue(so.name, out int k);
                        perKind[so.name] = k + 1;
                    }
                }
                if (any) columns++;
            }

            var sb = new StringBuilder();
            sb.AppendFormat(CultureInfo.InvariantCulture, "[props stat] r={0:0} m kolem ({1:0},{2:0}): {3} sloupců, {4} objektů\n",
                radius, center.x, center.z, columns, placements);
            sb.Append("  biom      stromy kameny   keře  tráva+drobné (přibližně, podle sloupců)\n");
            for (int b = 0; b < 4; b++)
                sb.AppendFormat(CultureInfo.InvariantCulture, "  {0,-8} {1,7} {2,6} {3,6} {4,8}\n",
                    BiomeName[b], counts[b, 0], counts[b, 1], counts[b, 3], detailBiome[b]);
            var kinds = new List<KeyValuePair<string, int>>(perKind);
            kinds.Sort((a, b) => b.Value.CompareTo(a.Value));
            sb.Append("  druhy: ");
            for (int i = 0; i < kinds.Count && i < 14; i++)
                sb.AppendFormat(CultureInfo.InvariantCulture, "{0} {1}; ", kinds[i].Key, kinds[i].Value);
            sb.AppendFormat(CultureInfo.InvariantCulture,
                "\n  osazovač: {0} sloupců, průměr {1:0.00} ms, nejhorší {2:0.00} ms",
                EcologyPlacer.Columns, EcologyPlacer.TotalMs / Mathf.Max(1, EcologyPlacer.Columns), EcologyPlacer.WorstMs);
            sb.Append("\n  čas vrstev (ms/sloupec): ");
            string[] ln = { "stromy", "kameny", "keře", "kamínky", "drobné", "tráva", "příprava" };
            for (int i = 0; i < 7; i++)
                sb.AppendFormat(CultureInfo.InvariantCulture, "{0} {1:0.00}; ", ln[i], EcologyPlacer.LayerMs[i] / Mathf.Max(1, EcologyPlacer.Columns));
            sb.AppendFormat(CultureInfo.InvariantCulture, "\n  kolo 6: odmítnuto pravidlem stopy u hrany sloupce {0}; vzdálené stromy {1} ve {2} sloupcích ({3} dávek)",
                EcologyPlacer.BorderRejects, TreeProxies.Instances, TreeProxies.Columns, TreeProxies.DrawGroups);
            sb.Append("\n  stromy odmítnuty: ");
            for (int i = 0; i < EcologyPlacer.TreeRejectName.Length; i++)
                sb.AppendFormat(CultureInfo.InvariantCulture, "{0} {1}; ", EcologyPlacer.TreeRejectName[i], EcologyPlacer.TreeReject[i]);
            sb.Append("\n  kolo 19 velké kusy (ruiny/věže/jehly): vybráno ").Append(EcologyPlacer.K19Big[0]).Append(", osazeno ").Append(EcologyPlacer.K19Big[1])
              .Append(", u hrany sloupce ").Append(EcologyPlacer.K19Big[14]).Append(", odmítnuto:");
            for (int i = 0; i < EcologyPlacer.RockRejectName.Length; i++) sb.Append(' ').Append(EcologyPlacer.RockRejectName[i]).Append(' ').Append(EcologyPlacer.K19Big[2 + i]).Append(';');
            sb.Append("\n  kolo 19 stromy v oáze odmítnuty:");
            for (int i = 0; i < EcologyPlacer.TreeRejectName.Length; i++) sb.Append(' ').Append(EcologyPlacer.TreeRejectName[i]).Append(' ').Append(EcologyPlacer.OasisTreeReject[i]).Append(';');
            sb.Append("\n  kolo 19+20 keře odmítnuty [vulkan, spaleny, kras, zkamenely, ruiny, oaza, stity, ledovec, zamrzly, mangrovy, solne, geotermal] (prob,sklon,koryto,druh,rozestup,převis,sklon stopy,nad vodou,mesh sklon,mesh,kmen konce,velkolistá,zapadlý):");
            for (int r = 0; r < EcologyPlacer.K19BushReject.GetLength(0); r++) { sb.Append(" |"); for (int i = 0; i < 13; i++) sb.Append(' ').Append(EcologyPlacer.K19BushReject[r, i]); }
            sb.Append("\n  kolo 20 kry: kandidátů ").Append(EcologyPlacer.FloeStat[0]).Append(", jiný region ").Append(EcologyPlacer.FloeStat[1])
              .Append(", mělko/dno ").Append(EcologyPlacer.FloeStat[2]).Append(", koryto ").Append(EcologyPlacer.FloeStat[3])
              .Append(", kanál/hustota ").Append(EcologyPlacer.FloeStat[4]).Append(", osazeno ").Append(EcologyPlacer.FloeStat[5]);
            sb.Append("\n  kolo 28 mořské sloupy: kandidátů ").Append(EcologyPlacer.SeaColStat[0]).Append(", hloubka ").Append(EcologyPlacer.SeaColStat[1])
              .Append(", jiný region ").Append(EcologyPlacer.SeaColStat[2]).Append(", koryto ").Append(EcologyPlacer.SeaColStat[3])
              .Append(", hustota ").Append(EcologyPlacer.SeaColStat[4]).Append(", dno/výška ").Append(EcologyPlacer.SeaColStat[5]).Append(", osazeno ").Append(EcologyPlacer.SeaColStat[6]);
            sb.Append("\n  kolo 28 formace (mřížky 72/34 j.): kandidátů ").Append(EcologyPlacer.FormStat[0]).Append(", region/souš ").Append(EcologyPlacer.FormStat[1])
              .Append(", hustota ").Append(EcologyPlacer.FormStat[2]).Append(", terén/odstup ").Append(EcologyPlacer.FormStat[3]).Append(", osazeno ").Append(EcologyPlacer.FormStat[4])
              .Append(", rezervace z okraje sousedů ").Append(EcologyPlacer.FormStat[5]);
            sb.Append("\n  balvany odmítnuty: ");
            for (int i = 0; i < EcologyPlacer.RockRejectName.Length; i++)
                sb.AppendFormat(CultureInfo.InvariantCulture, "{0} {1}; ", EcologyPlacer.RockRejectName[i], EcologyPlacer.RockReject[i]);
            return sb.ToString();
        }

        private struct Item
        {
            public Vector3 pos;
            public float r;
            public int cat;
            public int layer;
            public string name;
        }

        /// <summary>
        /// Kolo 12c, diagnostika jednoho místa (/props bod x z): všechny vrstvy colliderů pod
        /// svislicí, mřížka nejvyšší země ±2 m po 0,5 m a objekty osazené do 2 m.
        /// </summary>
        public static string Probe(VoxelTerrain t, float x, float z, float step = 0.5f)
        {
            var meshes = new List<Mesh>();
            var cols = new HashSet<Collider>();
            var colColumns = new HashSet<int2>();
            t.SetAuditArea(new Vector3(x, 0f, z), 12f);
            t.CollectAuditData(meshes, cols, colColumns);
            var sb = new StringBuilder();
            sb.AppendFormat(CultureInfo.InvariantCulture, "[props bod] ({0:0.0},{1:0.0}) collidery {2}\n", x, z, cols.Count);
            var hits = new RaycastHit[32];
            int c = Physics.RaycastNonAlloc(new Vector3(x, 2000f, z), Vector3.down, hits, 4000f, ~0, QueryTriggerInteraction.Ignore);
            System.Array.Sort(hits, 0, c, Comparer<RaycastHit>.Create((a, b) => b.point.y.CompareTo(a.point.y)));
            sb.Append("  vrstvy shora:");
            for (int h = 0; h < c; h++)
                sb.AppendFormat(CultureInfo.InvariantCulture, " {0:0.00}{1}(n.y {2:0.00})", hits[h].point.y,
                    cols.Contains(hits[h].collider) ? "T" : "o:" + hits[h].collider.name, hits[h].normal.y);
            // také zdola nahoru – odhalí strop/převis nad nižší plochou
            int cu = Physics.RaycastNonAlloc(new Vector3(x, -500f, z), Vector3.up, hits, 4000f, ~0, QueryTriggerInteraction.Ignore);
            sb.Append("\n  zdola:");
            for (int h = 0; h < cu; h++)
                if (cols.Contains(hits[h].collider))
                    sb.AppendFormat(CultureInfo.InvariantCulture, " {0:0.00}(n.y {1:0.00})", hits[h].point.y, hits[h].normal.y);
            sb.AppendFormat(CultureInfo.InvariantCulture, "\n  nejvyšší země (krok {0:0.00} m, řádky shora z+4k..z-4k, sloupce x-4k..x+4k):\n", step);
            for (int iz = 4; iz >= -4; iz--)
            {
                float dz = iz * step;
                sb.Append("   ");
                for (int ix = -4; ix <= 4; ix++)
                {
                    float dx = ix * step;
                    float best = float.NegativeInfinity;
                    int k = Physics.RaycastNonAlloc(new Vector3(x + dx, 2000f, z + dz), Vector3.down, hits, 4000f, ~0, QueryTriggerInteraction.Ignore);
                    for (int h = 0; h < k; h++) if (cols.Contains(hits[h].collider) && hits[h].point.y > best) best = hits[h].point.y;
                    sb.AppendFormat(CultureInfo.InvariantCulture, " {0,7:0.00}", best);
                }
                sb.Append('\n');
            }
            var spawners = new List<ObjectSpawner>();
            t.CollectPropSpawners(spawners);
            var list = new List<EcoPlacement>(256);
            foreach (ObjectSpawner sp in spawners)
            {
                if (sp.EcoPlaced == null) continue;
                list.Clear(); list.AddRange(sp.EcoPlaced); sp.CollectDetails(list);
                foreach (EcoPlacement p in list)
                {
                    float dx = p.position.x - x, dz = p.position.z - z;
                    if (dx * dx + dz * dz > 4f) continue;
                    SpawnableObject so = sp.GetSpawnable(p.spawnable);
                    PrefabShape sh = sp.GetShape(p.spawnable);
                    sb.AppendFormat(CultureInfo.InvariantCulture, "  objekt {0} vrstva {1} ({2:0.00},{3:0.00},{4:0.00}) měřítko {5:0.00} r {6:0.00} minY {7:0.00} h {8:0.00} spawner {9}\n",
                        so != null ? so.name : "?", p.layer, p.position.x, p.position.y, p.position.z, p.scale.y, p.radius,
                        sh.valid ? sh.minY * p.scale.y : 0f, sh.valid ? sh.height * p.scale.y : 0f, sp.name);
                }
            }
            return sb.ToString();
        }

        public static string Run(VoxelTerrain t, Vector3 center, float radius, out int problems)
        {
            problems = 0;
            var spawners = new List<ObjectSpawner>();
            t.CollectPropSpawners(spawners);

            var meshes = new List<Mesh>();
            var cols = new HashSet<Collider>();
            var colColumns = new HashSet<int2>();
            t.SetAuditArea(center, radius + 4f);
            t.CollectAuditData(meshes, cols, colColumns);

            // ── hladina ──
            float r2 = radius + 16f;
            int n = Mathf.CeilToInt(r2 * 2f / Cell) + 1;
            float x0 = center.x - r2, z0 = center.z - r2;
            var water = new float[n * n];
            for (int i = 0; i < water.Length; i++) water[i] = float.NegativeInfinity;
            var v = new List<Vector3>();
            var tri = new List<int>();
            foreach (Mesh m in meshes)
            {
                Bounds b = m.bounds;
                if (b.max.x < x0 || b.min.x > x0 + r2 * 2f || b.max.z < z0 || b.min.z > z0 + r2 * 2f) continue;
                m.GetVertices(v);
                m.GetTriangles(tri, 0);
                for (int k = 0; k < tri.Count; k += 3)
                    Raster(v[tri[k]], v[tri[k + 1]], v[tri[k + 2]], water, n, x0, z0);
            }

            float WaterAt(float x, float z)
            {
                int ix = Mathf.RoundToInt((x - x0 - OffX) / Cell), iz = Mathf.RoundToInt((z - z0 - OffZ) / Cell);
                if (ix < 0 || iz < 0 || ix >= n || iz >= n) return float.NegativeInfinity;
                return water[iz * n + ix];
            }

            var hits = new RaycastHit[16];
            bool Ground(float x, float z, out float y, out Vector3 normal)
            {
                y = float.NegativeInfinity; normal = Vector3.up;
                int c = Physics.RaycastNonAlloc(new Vector3(x, 2000f, z), Vector3.down, hits, 4000f, ~0, QueryTriggerInteraction.Ignore);
                bool ok = false;
                for (int h = 0; h < c; h++)
                {
                    if (!cols.Contains(hits[h].collider)) continue;
                    if (hits[h].point.y > y) { y = hits[h].point.y; normal = hits[h].normal; ok = true; }
                }
                return ok;
            }

            var items = new List<Item>(2048);
            int total = 0, unchecked_ = 0;
            int floating = 0, hanging = 0, buried = 0, inWater = 0, steep = 0, treeNearWater = 0, roofed = 0, pile = 0;
            // Kolo 12c: jen informativní, nepočítá se do problémů (viz níže, body 3 a 5).
            int edgeOk = 0, outcrop = 0;
            // Kolo 20: kry plavou na hladině moře – kontrolují se zvlášť (hladina mezi spodkem a vrškem, dno pod krou).
            int floeOk = 0, floeBad = 0; var exFloe = new List<string>();
            int seaColOk = 0, seaColBad = 0; var exSeaCol = new List<string>();   // kolo 28
            var exEdge = new List<string>(); var exOutcrop = new List<string>();
            float worstFloat = 0f, worstBury = 0f, worstSteep = 0f, nearestTreeWater = 999f;
            var exFloat = new List<string>(); var exHang = new List<string>(); var exBury = new List<string>();
            var exWater = new List<string>(); var exSteep = new List<string>(); var exNear = new List<string>();
            var exRoof = new List<string>(); var exPile = new List<string>();
            var perCat = new int[4];

            var list = new List<EcoPlacement>(2048);
            foreach (ObjectSpawner sp in spawners)
            {
                if (sp.EcoPlaced == null) continue;
                list.Clear();
                list.AddRange(sp.EcoPlaced);
                sp.CollectDetails(list);
                foreach (EcoPlacement p in list)
                {
                    float dx = p.position.x - center.x, dz = p.position.z - center.z;
                    if (dx * dx + dz * dz > radius * radius) continue;
                    SpawnableObject so = sp.GetSpawnable(p.spawnable);
                    if (so == null) continue;
                    int cat = (int)so.category;

                    float x = p.position.x, z = p.position.z;
                    var colKey = new int2(Mathf.FloorToInt(x / t.Lod0Span), Mathf.FloorToInt(z / t.Lod0Span));
                    if (!colColumns.Contains(colKey) || !Ground(x, z, out float g0, out Vector3 nrm)) { unchecked_++; continue; }
                    total++;
                    perCat[cat]++;

                    PrefabShape sh = sp.GetShape(p.spawnable);
                    float sy = p.scale.y;
                    int lay = p.layer >= 9 ? 1 : p.layer;   // kolo 28: formace (vrstvy 9–10) se měří jako balvany
                    bool rockish = lay == 1 || lay == 3;
                    float bottom = p.position.y + (rockish && sh.valid ? sh.minY * sy : 0f);
                    float hgt = sh.valid ? sh.height * sy : 1f;
                    string at = string.Format(CultureInfo.InvariantCulture, "{0} ({1:0.0},{2:0.0},{3:0.0})", so.name, x, p.position.y, z);

                    if (p.layer == EcologyPlacer.LayerSeaColumn)
                    {
                        // Kolo 28: čedičový sloup v mělčině – pata pod dnem (ne nad ním), vršek ≥ 2 j. nad hladinou, hladina nad dnem.
                        float cb = p.position.y + (sh.valid ? sh.minY * sy : 0f), ct = cb + hgt, wl2 = WaterAt(x, z);
                        string why2 = float.IsNegativeInfinity(wl2) ? "bez hladiny" : cb > g0 + 0.2f ? "vznáší se nad dnem" : ct < wl2 + 2f ? "neční nad hladinu"
                                    : wl2 < g0 ? "na souši" : null;
                        if (why2 == null) seaColOk++;
                        else { seaColBad++; Keep(exSeaCol, at + " " + why2 + string.Format(CultureInfo.InvariantCulture, " [pata {0:0.00} vršek {1:0.00} hladina {2:0.00} dno {3:0.00}]", cb, ct, wl2, g0)); }
                        items.Add(new Item { pos = p.position, r = p.radius, cat = cat, layer = 1, name = so.name });
                        continue;
                    }
                    if (p.layer == EcologyPlacer.LayerFloe)
                    {
                        float fb = p.position.y + (sh.valid ? sh.minY * sy : -0.4f), ft = fb + hgt;
                        float fr = Mathf.Clamp(p.radius * 0.8f, 0.5f, 8f), seabed = g0, wl = WaterAt(x, z);
                        for (int k = 0; k < 8; k++)
                        {
                            float a = k * Mathf.PI * 0.25f;
                            if (Ground(x + Mathf.Cos(a) * fr, z + Mathf.Sin(a) * fr, out float g, out _)) seabed = Mathf.Max(seabed, g);
                        }
                        string why = float.IsNegativeInfinity(wl) ? "bez hladiny" : wl > ft - 0.05f ? "pod hladinou" : wl < fb + 0.05f ? "nad hladinou" : seabed > fb - 0.2f ? "na dně/břehu" : null;
                        if (why == null) floeOk++;
                        else { floeBad++; Keep(exFloe, at + " " + why + string.Format(CultureInfo.InvariantCulture, " [spodek {0:0.00} vršek {1:0.00} hladina {2:0.00} dno {3:0.00}]", fb, ft, wl, seabed)); }
                        items.Add(new Item { pos = p.position, r = p.radius, cat = cat, layer = p.layer, name = so.name });
                        continue;
                    }

                    // Stopa: čtyři body na poloměru podle druhu.
                    float rf = lay == 0 ? 1.0f : lay == 5 || lay == 4 ? 0f : Mathf.Clamp(p.radius * 0.6f, 0.2f, 2f);
                    float gmin = g0, gmax = g0;
                    if (rf > 0f)
                        for (int k = 0; k < 4; k++)
                        {
                            float ax = k == 0 ? rf : k == 1 ? -rf : 0f, az = k == 2 ? rf : k == 3 ? -rf : 0f;
                            if (Ground(x + ax, z + az, out float g, out _)) { gmin = Mathf.Min(gmin, g); gmax = Mathf.Max(gmax, g); }
                        }

                    // 1) vznáší se: spodek nad zemí ve středu
                    float fl = bottom - g0;
                    float flTol = cat == (int)SpawnCategory.Grass ? 0.12f : 0.2f;
                    if (fl > flTol) { floating++; Keep(exFloat, at + string.Format(CultureInfo.InvariantCulture, " +{0:0.00}", fl)); }
                    worstFloat = Mathf.Max(worstFloat, fl);

                    // 2) visí přes hranu: část stopy hluboko pod spodkem
                    if (lay == 0 || lay == 2)
                    {
                        float hang = bottom - gmin;
                        if (hang > 0.9f) { hanging++; Keep(exHang, at + string.Format(CultureInfo.InvariantCulture, " {0:0.00}", hang)); }
                    }

                    // 3) zapadlý: země vysoko nad spodkem
                    // Stromy a keře: nejvyšší bod stopy (prorůstání do svahu). Kameny: střed –
                    // kámen do svahu zapuštěný z horní strany je přirozený, ne chyba.
                    float bury = (rockish ? g0 : gmax) - bottom;
                    float buryTol = lay == 0 ? 1.0f : lay == 2 ? 0.9f : lay == 5 || lay == 4 ? 0.3f : 0.5f * hgt + 0.15f;
                    // Kolo 12c: velký kámen na svahu osazovač záměrně posadí spodkem na NEJNIŽŠÍ
                    // zem stopy (0,7 poloměru) – z horní strany je pak zapuštěný o sklon × poloměr,
                    // u skalního bloku r 11 m na svahu 33° ~5 m. To je výchoz, ne chyba. Zapadlý je
                    // kámen až tehdy, když je pod zemí i na nejnižší straně stopy, nebo když je
                    // ve středu zasypaný celý (vršek pod zemí).
                    if (rockish && bury > buryTol)
                    {
                        float wr = Mathf.Max(0.5f, p.radius * 0.7f), gw = g0;
                        for (int k = 0; k < 8; k++)
                        {
                            float a = k * Mathf.PI * 0.25f;
                            if (Ground(x + Mathf.Cos(a) * wr, z + Mathf.Sin(a) * wr, out float g, out _)) gw = Mathf.Min(gw, g);
                        }
                        bool sunkLow = gw - bottom > buryTol;
                        bool covered = g0 > bottom + hgt - 0.3f;
                        if (!sunkLow && !covered)
                        {
                            outcrop++;
                            Keep(exOutcrop, at + string.Format(CultureInfo.InvariantCulture, " střed -{0:0.00}, nejnižší strana stopy {1:+0.00;-0.00}, nad zemí {2:0.00} m", bury, bottom - gw, bottom + hgt - g0));
                            bury = 0f;
                        }
                    }
                    if (bury > buryTol) { buried++; Keep(exBury, at + string.Format(CultureInfo.InvariantCulture, " -{0:0.00} [tol {1:0.00} h {2:0.00} stopa {3:0.00}..{4:0.00}]", bury, buryTol, hgt, gmin, gmax)); }
                    worstBury = Mathf.Max(worstBury, bury - buryTol);

                    // 4) ve vodě / na hladině
                    bool reed = so.name.IndexOf("Reed", System.StringComparison.OrdinalIgnoreCase) >= 0;
                    float wmax = WaterAt(x, z);
                    if (rf > 0f)
                        for (int k = 0; k < 4; k++)
                        {
                            float ax = k == 0 ? rf : k == 1 ? -rf : 0f, az = k == 2 ? rf : k == 3 ? -rf : 0f;
                            wmax = Mathf.Max(wmax, WaterAt(x + ax, z + az));
                        }
                    if (!float.IsNegativeInfinity(wmax) && wmax > bottom - (reed ? -0.05f : 0.05f) && wmax > g0 - 0.3f)
                    { inWater++; Keep(exWater, at + string.Format(CultureInfo.InvariantCulture, " hladina {0:0.00}", wmax)); }

                    // 5) sklon pod objektem (normála trojúhelníku ve středu)
                    // Sklon z gradientu ±0,75 m (jedna fazeta low-poly meshe umí být strmější než svah).
                    float ang = Vector3.Angle(nrm, Vector3.up);
                    float gE = 0f, gW = 0f, gN = 0f, gS = 0f;
                    if (Ground(x + 0.75f, z, out gE, out _) && Ground(x - 0.75f, z, out gW, out _)
                        && Ground(x, z + 0.75f, out gN, out _) && Ground(x, z - 0.75f, out gS, out _))
                    {
                        float gx = (gE - gW) / 1.5f, gz = (gN - gS) / 1.5f;
                        ang = Mathf.Atan(Mathf.Sqrt(gx * gx + gz * gz)) * Mathf.Rad2Deg;
                    }
                    float lim = lay == 0 ? EcologyPlacer.TreeMaxSlope + 8f
                              : lay == 1 ? EcologyPlacer.CliffRockMaxSlope + 8f
                              : lay == 2 ? EcologyPlacer.BushMaxSlope + 8f
                              : lay == 3 ? EcologyPlacer.StoneMaxSlope + 8f
                              : EcologyPlacer.GrassMaxSlope + 10f;
                    // Kolo 12c: tráva/drobné na rovině těsně u hrany srázu (lem jeskyně, převis):
                    // bod ±0,75 m už spadne přes hranu a gradient vyjde ~85°, i když stopa objektu
                    // (r 0,3 m) leží celá na rovné zemi. Rozliší se sklonem vlastní stopy: když je
                    // stopa v mezích a skok > 1 m je jen na jedné straně, je to hrana, ne svah.
                    // Skutečně strmá fazeta má strmou i stopu, takže se počítá dál.
                    if (ang > lim && (lay == 4 || lay == 5) && nrm.y > 0.9f)
                    {
                        float fr = Mathf.Max(0.3f, p.radius);
                        if (Ground(x + fr, z, out float fE, out _) && Ground(x - fr, z, out float fW, out _)
                            && Ground(x, z + fr, out float fN, out _) && Ground(x, z - fr, out float fS, out _))
                        {
                            float fx = (fE - fW) / (2f * fr), fz = (fN - fS) / (2f * fr);
                            float fang = Mathf.Atan(Mathf.Sqrt(fx * fx + fz * fz)) * Mathf.Rad2Deg;
                            int jumps = (Mathf.Abs(gE - g0) > 1f ? 1 : 0) + (Mathf.Abs(gW - g0) > 1f ? 1 : 0)
                                      + (Mathf.Abs(gN - g0) > 1f ? 1 : 0) + (Mathf.Abs(gS - g0) > 1f ? 1 : 0);
                            bool oneSideX = Mathf.Abs(gE - g0) > 1f != Mathf.Abs(gW - g0) > 1f || Mathf.Abs(gE - g0) <= 1f;
                            bool oneSideZ = Mathf.Abs(gN - g0) > 1f != Mathf.Abs(gS - g0) > 1f || Mathf.Abs(gN - g0) <= 1f;
                            if (fang <= lim && jumps >= 1 && oneSideX && oneSideZ)
                            {
                                edgeOk++;
                                Keep(exEdge, at + string.Format(CultureInfo.InvariantCulture, " stopa {0:0}°, ±0,75 m {1:0}°", fang, ang));
                                ang = fang;
                            }
                        }
                    }
                    if (ang > lim) { steep++; Keep(exSteep, at + string.Format(CultureInfo.InvariantCulture, " {0:0}° [g {1:0.00} E {2:0.00} W {3:0.00} N {4:0.00} S {5:0.00} n.y {6:0.00}]", ang, g0, gE, gW, gN, gS, nrm.y)); }
                    worstSteep = Mathf.Max(worstSteep, ang - lim);

                    // 6) stromy u vody (výhled na řeku, ústí, vodopády)
                    if (lay == 0)
                    {
                        float near = 999f;
                        for (int ring = 1; ring <= 24; ring++)
                        for (int k = 0; k < 16; k++)
                        {
                            float d = ring * 2.5f, a = k * Mathf.PI / 8f;
                            float qx = x + Mathf.Cos(a) * d, qz = z + Mathf.Sin(a) * d;
                            float w = WaterAt(qx, qz);
                            if (float.IsNegativeInfinity(w)) continue;
                            if (Ground(qx, qz, out float gq, out _) && w > gq + 0.02f) near = Mathf.Min(near, d);
                        }
                        nearestTreeWater = Mathf.Min(nearestTreeWater, near);
                        if (near < 20f) { treeNearWater++; Keep(exNear, at + string.Format(CultureInfo.InvariantCulture, " voda {0:0.0} m", near)); }

                        // 7) strop nad kmenem (převis, brána)
                        if (Physics.Raycast(new Vector3(x, g0 + 0.3f, z), Vector3.up, out RaycastHit up, Mathf.Max(3f, hgt), ~0, QueryTriggerInteraction.Ignore)
                            && cols.Contains(up.collider))
                        { roofed++; Keep(exRoof, at); }
                    }

                    if (lay <= 3)
                        items.Add(new Item { pos = p.position, r = p.radius, cat = cat, layer = lay, name = so.name });
                }
            }

            // 8) nakupení (překryv stop pevných objektů)
            for (int i = 0; i < items.Count; i++)
            for (int j = i + 1; j < items.Count; j++)
            {
                Item a = items[i], b = items[j];
                float dx = a.pos.x - b.pos.x, dz = a.pos.z - b.pos.z;
                float need = (a.r + b.r) * 0.6f;
                if (dx * dx + dz * dz < need * need)
                {
                    pile++;
                    Keep(exPile, string.Format(CultureInfo.InvariantCulture, "{0} + {1} ({2:0.0},{3:0.0}) {4:0.00} m",
                        a.name, b.name, a.pos.x, a.pos.z, Mathf.Sqrt(dx * dx + dz * dz)));
                }
            }

            problems = floating + hanging + buried + inWater + steep + treeNearWater + roofed + pile + floeBad + seaColBad;

            var sb = new StringBuilder();
            sb.AppendFormat(CultureInfo.InvariantCulture,
                "[props audit] r={0:0} m kolem ({1:0},{2:0}): {3} objektů (stromy {4}, kameny {5}, tráva {6}, malé {7}), mimo collidery {8}\n",
                radius, center.x, center.z, total, perCat[0], perCat[1], perCat[2], perCat[3], unchecked_);
            sb.AppendFormat(CultureInfo.InvariantCulture,
                "  vznáší se {0} (max {1:0.00} m) | visí přes hranu {2} | zapadlé {3} | ve vodě/na hladině {4} | příliš strmě {5} | strom <20 m od vody {6} (nejblíž {7:0.0} m) | pod převisem {8} | nakupení {9}\n",
                floating, worstFloat, hanging, buried, inWater, steep, treeNearWater, nearestTreeWater, roofed, pile);
            sb.AppendFormat(CultureInfo.InvariantCulture,
                "  info (nepočítá se): tráva/drobné na rovině u hrany srázu {0} | skalní výchoz zapuštěný do svahu {1}\n", edgeOk, outcrop);
            if (floeOk + floeBad > 0)
                sb.AppendFormat(CultureInfo.InvariantCulture, "  kolo 20 kry: plavou správně {0}, chybně {1} (počítá se do problémů)\n", floeOk, floeBad);
            Dump(sb, "kry", exFloe);
            if (seaColOk + seaColBad > 0)
                sb.AppendFormat(CultureInfo.InvariantCulture, "  kolo 28 mořské čedičové sloupy: správně {0}, chybně {1} (počítá se do problémů)\n", seaColOk, seaColBad);
            Dump(sb, "mořské sloupy", exSeaCol);
            Dump(sb, "u hrany", exEdge); Dump(sb, "výchoz", exOutcrop);
            Dump(sb, "vznáší", exFloat); Dump(sb, "visí", exHang); Dump(sb, "zapadlé", exBury); Dump(sb, "voda", exWater);
            Dump(sb, "strmě", exSteep); Dump(sb, "u vody", exNear); Dump(sb, "převis", exRoof); Dump(sb, "nakupení", exPile);
            return sb.ToString();
        }

        private static void Keep(List<string> list, string s) { if (list.Count < 5) list.Add(s); }

        private static void Dump(StringBuilder sb, string label, List<string> list)
        {
            if (list.Count == 0) return;
            sb.Append("  ").Append(label).Append(": ").Append(string.Join("; ", list)).Append('\n');
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
    }
}
