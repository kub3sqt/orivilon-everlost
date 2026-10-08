using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Orivilon.World.Spawning
{
    /// <summary>
    /// Kreslí trávu a drobnou vegetaci přes GPU instancing místo jednotlivých GameObjectů.
    ///
    /// <para><b>Proč.</b> Tráva byla nejpočetnější věc ve scéně – na sloupec jich je až
    /// <c>maxGrassPerChunk</c> (2500) proti stovce ostatních objektů. Každé stéblo bylo
    /// vlastní GameObject s Transformem a Rendererem, takže les stál přes 15 000 batchů
    /// a držel snímkování na 74 FPS. Přitom tráva nemá collider, nedá se s ní nijak
    /// pracovat a její transform je deterministicky spočítaný dopředu – GameObject na ní
    /// nedělá vůbec nic užitečného.</para>
    ///
    /// <para><b>Jak.</b> Jedna dávka = jeden sloupec × jeden druh trávy. Matice se spočítají
    /// jednou při vstupu sloupce do dosahu a zahodí se při odchodu – tedy přesně tam, kde
    /// se dřív instancovalo a ničilo. Každý snímek se dávka jen ořízne podle vzdálenosti
    /// a frustumu (jeden AABB na celý sloupec, ne na stéblo) a pošle se po 1023 kusech.</para>
    ///
    /// <para><b>Zakládá se sám</b> přes <see cref="RuntimeInitializeOnLoadMethod"/>, takže
    /// do scény není co přidávat a nemá se co rozbít přejmenováním objektu.</para>
    /// </summary>
    [AddComponentMenu("")]
    public sealed class VegetationRenderer : MonoBehaviour
    {
        /// <summary>Kolik matic vezme jedno volání <c>DrawMeshInstanced</c>. Limit Unity.</summary>
        private const int BatchLimit = 1023;

        public static VegetationRenderer Instance { get; private set; }

        /// <summary>Jedna registrovaná dávka: jeden druh vegetace v jednom sloupci.</summary>
        private sealed class Batch
        {
            public Mesh mesh;
            public Material material;
            public Matrix4x4[] matrices;
            public int count;
            public Bounds bounds;
            public ShadowCastingMode shadows;
            public int layer;

            /// <summary>Kolo 31: submesh (jehličnan = kmen i jehličí v jednom meshi). Běžně 0.</summary>
            public int subMesh;

            /// <summary>Za jak daleko se dávka přestane kreslit. 0 = bez omezení.</summary>
            public float maxDistance;
        }

        private readonly Dictionary<int, Batch> batches = new Dictionary<int, Batch>(256);
        private readonly List<int> deadHandles = new List<int>(16);
        private int nextHandle = 1;

        /// <summary>
        /// Sdílený výřez pro jedno volání. Je jeden na celou hru schválně: kopie 1023 matic
        /// je obyčejný memcpy, kdežto alokace pole na každou dávku a snímek by byla práce
        /// pro GC v každém snímku.
        /// </summary>
        private readonly Matrix4x4[] slice = new Matrix4x4[BatchLimit];

        private readonly Plane[] frustum = new Plane[6];

        /// <summary>
        /// Viditelné dávky seskupené podle (mesh, materiál) pro tenhle snímek.
        ///
        /// <para>Bez seskupení vychází jedno volání na dávku, a dávka je jeden druh v jednom
        /// sloupci – u čtyřiceti druhů kamene to dělá pár instancí na volání a instancing
        /// ztrácí smysl. Naměřeno: 724 volání na 2089 instancí. Sloučením přes sloupce se
        /// z toho stane několik plných dávek po 1023.</para>
        ///
        /// <para>Slovník i seznamy se drží mezi snímky a jen čistí – alokovat je každý
        /// snímek by byla práce pro GC přesně tam, kde se šetří.</para>
        /// </summary>
        private readonly Dictionary<(Mesh, Material, int), List<Batch>> groups =
            new Dictionary<(Mesh, Material, int), List<Batch>>(64);

        private readonly Stack<List<Batch>> groupPool = new Stack<List<Batch>>(64);

        /// <summary>Statistika posledního snímku – čte ji konzolový příkaz.</summary>
        public int LastDrawCalls { get; private set; }

        /// <inheritdoc cref="LastDrawCalls"/>
        public int LastInstances { get; private set; }

        /// <summary>Kolik dávek je registrovaných celkem (i těch oříznutých).</summary>
        public int BatchCount => batches.Count;

        /// <summary>Kolo 10 diagnostika: vypne vrhání stínů instancované vegetace (/folaz vrhani off).</summary>
        public static bool DiagNoShadows;

        /// <summary>Kolo 10 diagnostika: materiály všech registrovaných dávek a počet instancí.</summary>
        public void CollectMaterials(Dictionary<Material, int> into)
        {
            foreach (var kv in batches)
            {
                Batch b = kv.Value;
                if (b == null || b.material == null) continue;
                into.TryGetValue(b.material, out int n);
                into[b.material] = n + b.count;
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
            if (Instance != null) return;

            var go = new GameObject("Vegetation Renderer") { hideFlags = HideFlags.DontSave };
            go.AddComponent<VegetationRenderer>();
            DontDestroyOnLoad(go);
        }

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        /// <summary>
        /// Zaregistruje dávku ke kreslení. Vrací úchyt pro <see cref="Unregister"/>,
        /// nebo 0, když se dávka nedá kreslit (chybí mesh nebo materiál).
        /// </summary>
        /// <param name="matrices">
        /// Pole se PŘEBÍRÁ, nekopíruje – volající ho po předání už nesmí měnit.
        /// Kopie by při desítkách sloupců znamenala megabajty práce navíc pro nic.
        /// </param>
        public int Register(Mesh mesh, Material material, Matrix4x4[] matrices, int count,
                            Bounds bounds, ShadowCastingMode shadows, int layer, float maxDistance, int subMesh = 0)
        {
            if (mesh == null || material == null || matrices == null || count <= 0) return 0;

            int handle = nextHandle++;
            batches[handle] = new Batch
            {
                mesh = mesh,
                material = material,
                matrices = matrices,
                count = Mathf.Min(count, matrices.Length),
                bounds = bounds,
                shadows = shadows,
                layer = layer,
                maxDistance = maxDistance,
                subMesh = subMesh,
            };
            return handle;
        }

        /// <summary>
        /// Kolo 6: vymění matice už registrované dávky (pole se opět PŘEBÍRÁ). Vrací false,
        /// když úchyt neexistuje – volající pak registruje znovu. Bez nové alokace záznamu.
        /// </summary>
        public bool Replace(int handle, Matrix4x4[] matrices, int count)
        {
            if (handle == 0 || matrices == null || count <= 0) return false;
            if (!batches.TryGetValue(handle, out Batch b)) return false;
            b.matrices = matrices;
            b.count = Mathf.Min(count, matrices.Length);
            return true;
        }

        /// <summary>Zruší dávku. Neplatný úchyt je bez následku – volá se i z OnDestroy.</summary>
        public void Unregister(int handle)
        {
            if (handle != 0) batches.Remove(handle);
        }

        /// <summary>
        /// Kreslení. V <c>LateUpdate</c> schválně: kamera už je na své finální pozici po
        /// pohybu hráče, takže se frustum neořezává podle polohy z minulého snímku.
        /// </summary>
        private void LateUpdate()
        {
            LastDrawCalls = 0;
            LastInstances = 0;
            if (batches.Count == 0) return;

            Camera cam = Camera.main;
            if (cam == null) return;

            GeometryUtility.CalculateFrustumPlanes(cam, frustum);
            Vector3 eye = cam.transform.position;

            deadHandles.Clear();
            ClearGroups();

            // ── 1) co je vidět, rozdělit podle meshe a materiálu ──────
            foreach (KeyValuePair<int, Batch> kv in batches)
            {
                Batch b = kv.Value;

                // Mesh nebo materiál mohl zmizet s načtením jiné scény.
                if (b.mesh == null || b.material == null) { deadHandles.Add(kv.Key); continue; }

                if (b.maxDistance > 0f)
                {
                    float d = Vector3.Distance(eye, b.bounds.center) - b.bounds.extents.magnitude;
                    if (d > b.maxDistance) continue;
                }

                if (!GeometryUtility.TestPlanesAABB(frustum, b.bounds)) continue;

                var key = (b.mesh, b.material, b.subMesh);
                if (!groups.TryGetValue(key, out List<Batch> list))
                {
                    list = groupPool.Count > 0 ? groupPool.Pop() : new List<Batch>(16);
                    groups[key] = list;
                }
                list.Add(b);
            }

            // ── 2) každou skupinu poslat plnými dávkami napříč sloupci ──
            foreach (KeyValuePair<(Mesh, Material, int), List<Batch>> g in groups)
            {
                List<Batch> list = g.Value;
                int filled = 0;

                // Stín a vrstva se berou z prvního v řadě. Tytéž mesh a materiál znamenají
                // tentýž druh vegetace, takže se ve skupině stejně lišit nemohou.
                ShadowCastingMode shadows = DiagNoShadows ? ShadowCastingMode.Off : list[0].shadows;
                int layer = list[0].layer;

                for (int i = 0; i < list.Count; i++)
                {
                    Batch b = list[i];
                    int copied = 0;

                    while (copied < b.count)
                    {
                        int n = Mathf.Min(BatchLimit - filled, b.count - copied);
                        System.Array.Copy(b.matrices, copied, slice, filled, n);
                        filled += n;
                        copied += n;

                        if (filled == BatchLimit)
                        {
                            Graphics.DrawMeshInstanced(g.Key.Item1, g.Key.Item3, g.Key.Item2, slice, filled,
                                                       null, shadows, true, layer);
                            LastDrawCalls++;
                            LastInstances += filled;
                            filled = 0;
                        }
                    }
                }

                if (filled > 0)
                {
                    Graphics.DrawMeshInstanced(g.Key.Item1, g.Key.Item3, g.Key.Item2, slice, filled,
                                               null, shadows, true, layer);
                    LastDrawCalls++;
                    LastInstances += filled;
                }
            }

            for (int i = 0; i < deadHandles.Count; i++)
                batches.Remove(deadHandles[i]);
        }

        /// <summary>Vyprázdní skupiny a vrátí seznamy do zásobníku k dalšímu použití.</summary>
        private void ClearGroups()
        {
            foreach (KeyValuePair<(Mesh, Material, int), List<Batch>> g in groups)
            {
                g.Value.Clear();
                groupPool.Push(g.Value);
            }
            groups.Clear();
        }
    }
}
