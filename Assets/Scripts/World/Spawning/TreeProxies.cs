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
        }

        private struct Inst
        {
            public Matrix4x4 m;
            public int si;
            public bool autumn;
        }

        private sealed class Group
        {
            public Mesh mesh;
            public Material mat;
            public int layer;
            public Matrix4x4[] arr = new Matrix4x4[256];
            public int count;
            public int handle;
        }

        private static readonly Dictionary<int, Part[]> parts = new Dictionary<int, Part[]>(16);
        private static readonly Dictionary<Material, Material> instanced = new Dictionary<Material, Material>(16);
        private static readonly Dictionary<(Mesh, Material), Group> groups = new Dictionary<(Mesh, Material), Group>(32);
        private static readonly Dictionary<long, List<Inst>> columns = new Dictionary<long, List<Inst>>(256);
        private static readonly Dictionary<long, float> releaseAt = new Dictionary<long, float>(64);
        private static readonly Stack<List<Inst>> pool = new Stack<List<Inst>>(64);
        private static readonly List<long> scratchKeys = new List<long>(64);
        private static bool dirty;

        /// <summary>Počet vzdálených stromů a sloupců (pro /props stat a report).</summary>
        public static int Instances { get; private set; }
        public static int Columns => columns.Count;
        public static int DrawGroups { get; private set; }

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
            dirty = true;
        }

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
            l.Clear();
            pool.Push(l);
            columns.Remove(key);
            releaseAt.Remove(key);
            dirty = true;
        }

        public static void Clear()
        {
            foreach (var kv in columns) { kv.Value.Clear(); pool.Push(kv.Value); }
            columns.Clear();
            releaseAt.Clear();
            dirty = true;
            Rebuild();
        }

        /// <summary>Volá streamer jednou za snímek: odložené uvolnění a přestavba dávek.</summary>
        public static void Tick()
        {
            if (releaseAt.Count > 0)
            {
                scratchKeys.Clear();
                float now = Time.unscaledTime;
                foreach (var kv in releaseAt) if (now >= kv.Value) scratchKeys.Add(kv.Key);
                for (int i = 0; i < scratchKeys.Count; i++) Drop(scratchKeys[i]);
            }
            if (dirty) Rebuild();
        }

        /// <summary>
        /// Přepočítá dávky. Pole matic se drží mezi přestavbami a jen rostou, takže běžná
        /// přestavba nic nealokuje (jen malé záznamy dávek ve VegetationRenderer).
        /// </summary>
        private static void Rebuild()
        {
            dirty = false;
            foreach (var g in groups.Values) g.count = 0;
            int n = 0;
            foreach (var kv in columns)
            {
                List<Inst> l = kv.Value;
                for (int i = 0; i < l.Count; i++)
                {
                    Inst it = l[i];
                    if (!parts.TryGetValue(it.si, out Part[] ps)) continue;
                    for (int k = 0; k < ps.Length; k++)
                    {
                        Material m = it.autumn && ps[k].autumn != null ? ps[k].autumn : ps[k].mat;
                        var key = (ps[k].mesh, m);
                        if (!groups.TryGetValue(key, out Group g))
                        {
                            g = new Group { mesh = ps[k].mesh, mat = m, layer = ps[k].layer };
                            groups[key] = g;
                        }
                        if (g.count == g.arr.Length) System.Array.Resize(ref g.arr, g.arr.Length * 2);
                        g.arr[g.count++] = it.m * ps[k].offset;
                    }
                    n++;
                }
            }
            Instances = n;

            VegetationRenderer vr = VegetationRenderer.Instance;
            int drawn = 0;
            var huge = new Bounds(Vector3.zero, new Vector3(1e6f, 1e5f, 1e6f));
            foreach (var g in groups.Values)
            {
                if (vr == null) { g.handle = 0; continue; }
                if (g.count == 0)
                {
                    if (g.handle != 0) vr.Unregister(g.handle);
                    g.handle = 0;
                    continue;
                }
                if (g.handle == 0 || !vr.Replace(g.handle, g.arr, g.count))
                    g.handle = vr.Register(g.mesh, g.mat, g.arr, g.count, huge, ShadowCastingMode.Off, g.layer, 0f);
                drawn++;
            }
            DrawGroups = drawn;
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
                        if (f == null || f.sharedMesh == null || mr.sharedMaterials.Length != 1 || mr.sharedMaterial == null) continue;
                        Material src = mr.sharedMaterial;
                        Material leaf = ObjectSpawner.AutumnLeafVariant(src);
                        list.Add(new Part
                        {
                            mesh = f.sharedMesh,
                            mat = Instanced(src),
                            autumn = leaf != src ? Instanced(leaf) : null,
                            offset = toRoot * mr.transform.localToWorldMatrix,
                            layer = mr.gameObject.layer,
                        });
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
