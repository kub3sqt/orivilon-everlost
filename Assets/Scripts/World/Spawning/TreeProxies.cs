using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Orivilon.World.Spawning
{
    /// <summary>
    /// Vzdálené stromy (kolo 6): v LOD1 a LOD2 prstenci (od ~190 m do ~500 m, kde končí mlha)
    /// se kreslí nejnižší LOD existujících prefabů stromů instancovaně – bez GameObjectů,
    /// colliderů a bez nových assetů.
    ///
    /// <para><b>Proč.</b> Skutečné stromy se osazují jen na LOD0 sloupcích (do ~190 m), protože
    /// sedají na přesný mesh a nesou collider, save ID a interakci. Mlha ale končí ~450 m,
    /// takže stromy (70–120 m vysoké) na hraně LOD0 viditelně vyskakovaly a za ní byla
    /// holá krajina.</para>
    ///
    /// <para><b>Jak.</b> Pozice počítá týž osazovač (<see cref="Orivilon.World.Generation.EcologyPlacer"/>)
    /// týmž hashem buňky, jen z pole hrubšího sloupce a bez meshových kontrol. Druh, natočení
    /// a velikost stromu jsou proto stejné jako u skutečného stromu na témž místě; liší se
    /// jen výška o desítky centimetrů a vzácně to, jestli strom na hraně pravidla vůbec stojí.
    /// Všechny sloupce se slévají do jedné dávky na (mesh, materiál), takže je to jen pár
    /// desítek volání kreslení, ne tisíce GameObjectů. Stíny se u nich nekreslí.</para>
    ///
    /// <para>Při uvolnění sloupce se jeho stromy drží ještě <see cref="ReleaseDelay"/> s – tolik
    /// potřebují jemnější sloupce, aby osadily skutečné stromy, takže na přechodu nevznikne díra.</para>
    /// </summary>
    public static class TreeProxies
    {
        /// <summary>Zapnuto (přepíná <c>/props daleko on|off</c>).</summary>
        public static bool Enabled = true;

        /// <summary>
        /// Kolo 31: vzdálené jehličnany. Jejich nejnižší LOD je jeden mesh se dvěma submeshi (kmen + jehličí),
        /// který se dřív přeskakoval – jehličnany proto končily s LODGroup (~320 m), listnáče až v pásmu
        /// 400–500 m. Oba submeshe mají stejnou matici i stejné pásmo mizení, takže kmen a koruna mizí
        /// současně. A/B: <c>/props daleko jehlic on|off</c>.
        /// </summary>
        public static bool ConiferProxies = true;

        /// <summary>Do kterého LOD prstence se vzdálené stromy kreslí (LOD2 = do ~500 m).</summary>
        public const int MaxLod = 2;

        /// <summary>Jak dlouho zůstanou stromy uvolněného sloupce, s.</summary>
        public const float ReleaseDelay = 1.5f;

        private struct Part
        {
            public Mesh mesh;
            public Material mat, autumn;
            public Matrix4x4 offset;
            public int layer;
            public int sub;      // kolo 31: submesh
            public bool multi;   // kolo 31: díl víc-materiálového rendereru (jehličnan)
        }

        private struct Inst
        {
            public Matrix4x4 m;
            public int si;
            public bool autumn;
        }

        /// <summary>
        /// Kolo 31: dávka jednoho sloupce pro jeden (mesh, materiál, submesh). Každý sloupec se registruje
        /// zvlášť se svým AABB a dosahem, takže VegetationRenderer ořeže sloupce mimo záběr a za koncem
        /// pásma mizení (<see cref="ObjectSpawner.TreeFadeEnd"/>) – dřív se kreslilo celé prstencové okolí
        /// (i za kamerou a za 500 m, kde shader strom stejně celý zahodí). Viditelné sloupce VegetationRenderer
        /// dál slévá do plných dávek po 1023, počet volání kreslení se nemění.
        /// </summary>
        private sealed class Slot
        {
            public Mesh mesh;
            public Material mat;
            public int layer, sub;
            public Matrix4x4[] arr = new Matrix4x4[32];
            public int count, handle;
        }

        private static readonly Dictionary<int, Part[]> parts = new Dictionary<int, Part[]>(16);
        private static readonly Dictionary<Material, Material> instanced = new Dictionary<Material, Material>(16);
        private static readonly Dictionary<long, List<Inst>> columns = new Dictionary<long, List<Inst>>(256);
        private static readonly Dictionary<long, List<Slot>> colSlots = new Dictionary<long, List<Slot>>(256);
        private static readonly Dictionary<long, float> releaseAt = new Dictionary<long, float>(64);
        private static readonly Stack<List<Inst>> pool = new Stack<List<Inst>>(64);
        private static readonly Stack<List<Slot>> slotListPool = new Stack<List<Slot>>(64);
        private static readonly Stack<Slot> slotPool = new Stack<Slot>(256);
        private static readonly List<long> scratchKeys = new List<long>(64);
        private static bool dirty;
        private static float lastFadeEnd = -1f;

        /// <summary>Kolo 31: ořez sloupců podle záběru a dosahu (A/B <c>/props daleko orez on|off</c>; off = stav kola 30).</summary>
        public static bool Cull = true;

        /// <summary>Počet vzdálených stromů a sloupců (pro /props stat a report).</summary>
        public static int Instances { get { Recount(); return instCount; } }
        public static int Columns => columns.Count;
        public static int DrawGroups { get { Recount(); return groupCount; } }
        private static int instCount, groupCount;

        public static long Key(int x, int z, int lod)
            => ((long)(x & 0xFFFFFF) << 32) | ((long)(z & 0xFFFFFF) << 8) | (long)(lod & 0xFF);

        /// <summary>Přidá (nebo přepíše) stromy sloupce.</summary>
        public static void Add(long key, ObjectSpawner sp, List<Orivilon.World.Spawning.EcoPlacement> list)
        {
            releaseAt.Remove(key);
            if (!columns.TryGetValue(key, out List<Inst> dst))
            {
                dst = pool.Count > 0 ? pool.Pop() : new List<Inst>(64);
                columns[key] = dst;
            }
            dst.Clear();
            for (int i = 0; i < list.Count; i++)
            {
                EcoPlacement p = list[i];
                if (GetParts(sp, p.spawnable).Length == 0) continue;
                dst.Add(new Inst { m = Matrix4x4.TRS(p.position, p.rotation, p.scale), si = p.spawnable, autumn = (p.flags & 1) != 0 });
            }
            RegisterColumn(key, dst);
        }

        /// <summary>Kolo 31: znovu zaregistruje všechny sloupce (po přepnutí jehličnanů nebo ořezu).</summary>
        public static void MarkDirty() => dirty = true;

        /// <summary>Uvolní stromy sloupce – hned, nebo se zpožděním (přechod mezi prstenci).</summary>
        public static void Remove(long key, float delay)
        {
            if (!columns.ContainsKey(key)) return;
            if (delay <= 0f) { Drop(key); return; }
            releaseAt[key] = Time.unscaledTime + delay;
        }

        private static void Drop(long key)
        {
            if (!columns.TryGetValue(key, out List<Inst> l)) return;
            UnregisterColumn(key);
            l.Clear();
            pool.Push(l);
            columns.Remove(key);
            releaseAt.Remove(key);
        }

        public static void Clear()
        {
            foreach (var kv in columns) { UnregisterColumn(kv.Key); kv.Value.Clear(); pool.Push(kv.Value); }
            columns.Clear();
            releaseAt.Clear();
            dirty = false;
        }

        /// <summary>Volá streamer jednou za snímek: odložené uvolnění a přeregistrace po přepnutí.</summary>
        public static void Tick()
        {
            if (releaseAt.Count > 0)
            {
                scratchKeys.Clear();
                float now = Time.unscaledTime;
                foreach (var kv in releaseAt) if (now >= kv.Value) scratchKeys.Add(kv.Key);
                for (int i = 0; i < scratchKeys.Count; i++) Drop(scratchKeys[i]);
            }
            // Dosah ořezu visí na pásmu mizení – když ho /atmo stromy změní, přeregistrovat.
            if (ObjectSpawner.TreeFadeEnd != lastFadeEnd) dirty = true;
            if (dirty)
            {
                dirty = false;
                foreach (var kv in columns) RegisterColumn(kv.Key, kv.Value);
            }
        }

        private static void UnregisterColumn(long key)
        {
            if (!colSlots.TryGetValue(key, out List<Slot> sl)) return;
            VegetationRenderer vr = VegetationRenderer.Instance;
            for (int i = 0; i < sl.Count; i++)
            {
                if (vr != null && sl[i].handle != 0) vr.Unregister(sl[i].handle);
                sl[i].handle = 0; sl[i].count = 0;
                slotPool.Push(sl[i]);
            }
            sl.Clear();
            slotListPool.Push(sl);
            colSlots.Remove(key);
        }

        /// <summary>
        /// Zaregistruje stromy jednoho sloupce: dávka na (mesh, materiál, submesh), AABB sloupce
        /// a dosah = konec pásma mizení. Bez alokací v ustáleném stavu (sloty i pole se recyklují).
        /// </summary>
        private static void RegisterColumn(long key, List<Inst> l)
        {
            UnregisterColumn(key);
            VegetationRenderer vr = VegetationRenderer.Instance;
            if (vr == null) { dirty = true; return; }
            List<Slot> sl = slotListPool.Count > 0 ? slotListPool.Pop() : new List<Slot>(8);
            colSlots[key] = sl;
            Vector3 lo = new Vector3(float.MaxValue, float.MaxValue, float.MaxValue), hi = -lo;
            for (int i = 0; i < l.Count; i++)
            {
                Inst it = l[i];
                if (!parts.TryGetValue(it.si, out Part[] ps) || ps.Length == 0) continue;
                if (ps[0].multi && !ConiferProxies) continue;
                Vector3 pos = it.m.GetColumn(3);
                lo = Vector3.Min(lo, pos); hi = Vector3.Max(hi, pos);
                for (int k = 0; k < ps.Length; k++)
                {
                    Material m = it.autumn && ps[k].autumn != null ? ps[k].autumn : ps[k].mat;
                    Slot s = null;
                    for (int q = 0; q < sl.Count; q++)
                        if (sl[q].mesh == ps[k].mesh && sl[q].mat == m && sl[q].sub == ps[k].sub) { s = sl[q]; break; }
                    if (s == null)
                    {
                        s = slotPool.Count > 0 ? slotPool.Pop() : new Slot();
                        s.mesh = ps[k].mesh; s.mat = m; s.sub = ps[k].sub; s.layer = ps[k].layer; s.count = 0; s.handle = 0;
                        sl.Add(s);
                    }
                    if (s.count == s.arr.Length) System.Array.Resize(ref s.arr, s.arr.Length * 2);
                    s.arr[s.count++] = it.m * ps[k].offset;
                }
            }
            lastFadeEnd = ObjectSpawner.TreeFadeEnd;
            Bounds b;
            float maxD = 0f;
            if (Cull && sl.Count > 0)
            {
                // Rezerva na korunu (stromy jsou až ~120 m vysoké a široké desítky metrů).
                b = new Bounds((lo + hi) * 0.5f, Vector3.zero);
                b.SetMinMax(lo - new Vector3(60f, 10f, 60f), hi + new Vector3(60f, 160f, 60f));
                if (lastFadeEnd > 0f) maxD = lastFadeEnd + 10f;
            }
            else b = new Bounds(Vector3.zero, new Vector3(1e6f, 1e5f, 1e6f));
            for (int q = 0; q < sl.Count; q++)
                sl[q].handle = vr.Register(sl[q].mesh, sl[q].mat, sl[q].arr, sl[q].count, b, ShadowCastingMode.Off, sl[q].layer, maxD, sl[q].sub);
        }

        private static void Recount()
        {
            int n = 0, g = 0;
            foreach (var kv in columns)
                for (int i = 0; i < kv.Value.Count; i++)
                    if (parts.TryGetValue(kv.Value[i].si, out Part[] ps) && ps.Length > 0 && (!ps[0].multi || ConiferProxies)) n++;
            foreach (var kv in colSlots) g += kv.Value.Count;
            instCount = n;
            groupCount = g;
        }

        /// <summary>
        /// Díly nejnižšího LOD prefabu (každý renderer = mesh + materiál + poloha v prefabu).
        /// Prefab bez LODGroup nebo s víc materiály na rendereru se přeskočí.
        /// </summary>
        private static Part[] GetParts(ObjectSpawner sp, int si)
        {
            if (parts.TryGetValue(si, out Part[] cached)) return cached;
            var list = new List<Part>(2);
            SpawnableObject so = sp.GetSpawnable(si);
            GameObject prefab = so != null ? so.prefab : null;
            LODGroup lg = prefab != null ? prefab.GetComponentInChildren<LODGroup>(true) : null;
            if (lg != null)
            {
                LOD[] lods = lg.GetLODs();
                Renderer[] rs = lods.Length > 0 ? lods[lods.Length - 1].renderers : null;
                Matrix4x4 toRoot = prefab.transform.worldToLocalMatrix;
                if (rs != null)
                    foreach (Renderer r in rs)
                    {
                        if (!(r is MeshRenderer mr) || mr == null) continue;
                        MeshFilter f = mr.GetComponent<MeshFilter>();
                        if (f == null || f.sharedMesh == null) continue;
                        Material[] mats = mr.sharedMaterials;
                        // Kolo 31: jehličnan = jeden mesh, submesh na materiál (kmen + jehličí). Bere se jen celý –
                        // chybí-li jediný díl, nekreslí se nic (žádná koruna bez kmene ani kmen bez koruny).
                        bool multi = mats.Length > 1;
                        if (mats.Length == 0 || (multi && mats.Length != f.sharedMesh.subMeshCount)) continue;
                        bool complete = true;
                        for (int s = 0; s < mats.Length; s++) if (mats[s] == null) complete = false;
                        if (!complete) continue;
                        for (int s = 0; s < mats.Length; s++)
                        {
                            Material src = mats[s];
                            Material leaf = ObjectSpawner.AutumnLeafVariant(src);
                            list.Add(new Part
                            {
                                mesh = f.sharedMesh,
                                mat = Instanced(src),
                                autumn = leaf != src ? Instanced(leaf) : null,
                                offset = toRoot * mr.transform.localToWorldMatrix,
                                layer = mr.gameObject.layer,
                                sub = s,
                                multi = multi,
                            });
                        }
                    }
            }
            Part[] arr = list.ToArray();
            parts[si] = arr;
            if (arr.Length == 0 && prefab != null)
                Debug.LogWarning($"[TreeProxies] '{prefab.name}' nemá použitelný nejnižší LOD – vzdálený strom se nekreslí.", prefab);
            return arr;
        }

        private static Material Instanced(Material src)
        {
            if (instanced.TryGetValue(src, out Material m)) return m;
            m = new Material(src) { enableInstancing = true, name = src.name + " (far)" };
            // Kolo 11: vzdálený kmen má stejný doplněk oblohy jako blízký (jinak by se na hranici LOD lišil).
            if (ObjectSpawner.IsTrunkMaterial(src)) ObjectSpawner.RegisterTrunkMaterial(m);
            instanced[src] = m;
            return m;
        }
    }
}
