using UnityEngine;
using UnityEngine.Rendering;

namespace Orivilon
{
    /// <summary>
    /// Vygeneruje jemně rozdělenou mřížkovou plochu (grid) v rovině XZ pro low-poly vodu.
    /// Bez dostatku trojúhelníků nejsou v shaderu vidět vlny ani fasety, proto tahle plocha.
    /// Přidej na prázdný GameObject (přidá si MeshFilter + MeshRenderer), nastav velikost a rozlišení,
    /// a přiřaď materiál s shaderem "Everlost/LowPolyWater". V editoru přegeneruješ přes pravé tlačítko → Regenerovat mesh.
    /// </summary>
    [RequireComponent(typeof(MeshFilter))]
    [RequireComponent(typeof(MeshRenderer))]
    public class LowPolyWater : MonoBehaviour
    {
        [Tooltip("Celková šířka i délka plochy v metrech.")]
        [SerializeField] private float size = 100f;

        [Tooltip("Počet dílků na stranu (víc = jemnější vlny, ale víc trojúhelníků). Pro low-poly vzhled stačí 40–80.")]
        [SerializeField, Range(2, 254)] private int resolution = 60;

        private void Awake()
        {
            GenerateMesh();
        }

        /// <summary>Vytvoří mřížkový mesh a přiřadí ho do MeshFilteru.</summary>
        [ContextMenu("Regenerovat mesh")]
        public void GenerateMesh()
        {
            int res = Mathf.Clamp(resolution, 2, 254);
            int side = res + 1;

            Vector3[] vertices = new Vector3[side * side];
            Vector2[] uv = new Vector2[side * side];
            int[] triangles = new int[res * res * 6];

            float half = size * 0.5f;
            float step = size / res;

            for (int z = 0; z < side; z++)
            {
                for (int x = 0; x < side; x++)
                {
                    int i = z * side + x;
                    vertices[i] = new Vector3(-half + x * step, 0f, -half + z * step);
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

            Mesh mesh = new Mesh();
            mesh.name = "LowPolyWater Grid";
            if (vertices.Length > 65000)
                mesh.indexFormat = IndexFormat.UInt32;
            mesh.vertices = vertices;
            mesh.uv = uv;
            mesh.triangles = triangles;
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();

            GetComponent<MeshFilter>().sharedMesh = mesh;
        }
    }
}
