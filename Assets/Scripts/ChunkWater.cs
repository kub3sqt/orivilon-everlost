using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using Orivilon.World.Terrain;

namespace Orivilon
{
    /// <summary>
    /// Pokládá vodní dlaždice na aktivní terénní chunky (EndlessTerrain) na úrovni hladiny.
    /// Voda je tak všude, kde má být: kde je terén nad hladinou, schová se pod něj; kde je terén pod hladinou, ukáže se.
    /// Všechny dlaždice sdílejí jeden mesh i materiál (levné, stejně velké fasety) a recyklují se poolingem.
    /// Nesahá na generování terénu – jen čte veřejný seznam chunků z EndlessTerrain.
    /// Použití: přidej na prázdný GameObject ve scéně, přiřaď materiál s shaderem Everlost/LowPolyWater.
    /// </summary>
    [DisallowMultipleComponent]
    public class ChunkWater : MonoBehaviour
    {
        [Tooltip("Materiál s shaderem Everlost/LowPolyWater.")]
        [SerializeField] private Material waterMaterial;

        [Tooltip("Světová výška hladiny (Y). Pro tento svět je hladina na 0.")]
        [SerializeField] private float waterY = 0f;

        [Tooltip("Rezerva u břehů (m). Chunk dostane vodu, když jeho nejnižší bod terénu je pod hladinou + tahle rezerva.")]
        [SerializeField] private float shoreMargin = 1f;

        [Tooltip("Počet dílků na stranu dlaždice (víc = jemnější vlny, ale víc trojúhelníků). Pro low-poly stačí 12–24.")]
        [SerializeField, Range(2, 64)] private int resolution = 20;

        [Tooltip("Jak často (v sekundách) srovnat vodní dlaždice s terénními chunky.")]
        [SerializeField] private float updateInterval = 0.3f;

        private EndlessTerrain terrain;
        private Mesh sharedGrid;
        private float chunkSize;
        private float timer;

        private readonly Dictionary<Vector2Int, GameObject> tiles = new Dictionary<Vector2Int, GameObject>();
        private readonly Stack<GameObject> pool = new Stack<GameObject>();
        private readonly List<Vector2Int> toRemove = new List<Vector2Int>();
        private readonly HashSet<Vector2Int> decidedDry = new HashSet<Vector2Int>();

        private void Start()
        {
            terrain = EndlessTerrain.instance;
            if (terrain == null)
            {
                Debug.LogWarning("ChunkWater: EndlessTerrain.instance nenalezen – komponenta se vypíná.");
                enabled = false;
                return;
            }
            if (waterMaterial == null)
            {
                Debug.LogWarning("ChunkWater: chybí waterMaterial – komponenta se vypíná.");
                enabled = false;
                return;
            }

            chunkSize = Mathf.Max(1, terrain.chunkSize);
            sharedGrid = BuildGrid(chunkSize, resolution);
        }

        private void Update()
        {
            if (terrain == null || sharedGrid == null) return;

            timer -= Time.deltaTime;
            if (timer > 0f) return;
            timer = updateInterval;

            Sync();
        }

        /// <summary>
        /// Srovná vodní dlaždice s terénními chunky. Vodu dá jen tam, kde chunk sahá pod hladinu
        /// (nejnižší bod meshe < waterY + shoreMargin). Suché chunky si zapamatuje a přeskakuje.
        /// </summary>
        private void Sync()
        {
            var chunks = terrain.terrainChunks;
            if (chunks == null) return;

            // Přidat vodu jen tam, kde je potřeba.
            foreach (var kv in chunks)
            {
                Vector2Int coord = kv.Key;
                if (tiles.ContainsKey(coord) || decidedDry.Contains(coord)) continue;

                TerrainChunk chunk = kv.Value;
                if (chunk == null || !chunk.IsInitialized) continue; // mesh ještě není hotový → rozhodneme příště

                // MinTerrainHeight nahrazuje čtení bounds z lodMeshes[0] –
                // LOD0 mesh se teď vytváří líně a nemusí existovat.
                float minY = chunk.MinTerrainHeight;
                if (minY == float.MaxValue) continue;

                if (minY < waterY + shoreMargin)
                    tiles[coord] = AcquireTile(coord);   // chunk sahá pod hladinu → voda
                else
                    decidedDry.Add(coord);               // celý nad hladinou → žádná voda
            }

            // Uvolnit dlaždice pro chunky, které už nejsou aktivní.
            toRemove.Clear();
            foreach (var kv in tiles)
                if (!chunks.ContainsKey(kv.Key)) toRemove.Add(kv.Key);
            for (int i = 0; i < toRemove.Count; i++)
            {
                ReleaseTile(tiles[toRemove[i]]);
                tiles.Remove(toRemove[i]);
            }

            // Zapomenout rozhodnutí pro odešlé chunky (kdyby se vrátily, rozhodne se znovu).
            toRemove.Clear();
            foreach (var coord in decidedDry)
                if (!chunks.ContainsKey(coord)) toRemove.Add(coord);
            for (int i = 0; i < toRemove.Count; i++)
                decidedDry.Remove(toRemove[i]);
        }

        private GameObject AcquireTile(Vector2Int coord)
        {
            GameObject go = pool.Count > 0 ? pool.Pop() : CreateTile();
            go.transform.position = new Vector3(coord.x * chunkSize, waterY, coord.y * chunkSize);
            go.SetActive(true);
            return go;
        }

        private GameObject CreateTile()
        {
            GameObject go = new GameObject("Water Tile");
            go.transform.SetParent(transform, true);

            MeshFilter mf = go.AddComponent<MeshFilter>();
            mf.sharedMesh = sharedGrid;

            MeshRenderer mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = waterMaterial;
            mr.shadowCastingMode = ShadowCastingMode.Off;
            mr.receiveShadows = false;

            return go;
        }

        private void ReleaseTile(GameObject go)
        {
            if (go == null) return;
            go.SetActive(false);
            pool.Push(go);
        }

        /// <summary>Vytvoří rovnou mřížku [0,size] v XZ (Y=0) o daném rozlišení. Bounds mírně zvětšíme kvůli vlnám.</summary>
        private static Mesh BuildGrid(float size, int res)
        {
            res = Mathf.Clamp(res, 2, 200);
            int side = res + 1;

            Vector3[] vertices = new Vector3[side * side];
            Vector2[] uv = new Vector2[side * side];
            int[] triangles = new int[res * res * 6];
            float step = size / res;

            for (int z = 0; z < side; z++)
            {
                for (int x = 0; x < side; x++)
                {
                    int i = z * side + x;
                    vertices[i] = new Vector3(x * step, 0f, z * step);
                    uv[i] = new Vector2((float)x / res, (float)z / res);
                }
            }

            int t = 0;
            for (int z = 0; z < res; z++)
            {
                for (int x = 0; x < res; x++)
                {
                    int i = z * side + x;
                    triangles[t++] = i;
                    triangles[t++] = i + side;
                    triangles[t++] = i + side + 1;
                    triangles[t++] = i;
                    triangles[t++] = i + side + 1;
                    triangles[t++] = i + 1;
                }
            }

            Mesh mesh = new Mesh { name = "ChunkWater Grid" };
            if (vertices.Length > 65000) mesh.indexFormat = IndexFormat.UInt32;
            mesh.vertices = vertices;
            mesh.uv = uv;
            mesh.triangles = triangles;
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();

            // Bounds zvětšíme ve výšce, aby se dlaždice neořezávala kvůli vlnám.
            Bounds b = mesh.bounds;
            b.Expand(new Vector3(0f, 2f, 0f));
            mesh.bounds = b;

            return mesh;
        }

        private void OnDestroy()
        {
            if (sharedGrid != null)
                Destroy(sharedGrid);
        }
    }
}
