using Orivilon.World.Generation;
using UnityEngine;

namespace Orivilon.World.Biomes
{
    /// <summary>
    /// Mapa údolní mlhy kolem kamery (24.10.2026). Malá textura ukotvená ve světě:
    /// r = výška dna údolí, g = jak moc je místo údolím (0–1), b = tloušťka mlhy (m).
    ///
    /// <para><b>Proč.</b> Původní vrstva ležela jen v pevné výšce nad mořem – ve vysoko položených
    /// údolích uvnitř pohoří mlha nebyla vůbec a na pláních u moře ležela všude stejně. Tady se
    /// mlha váže na tvar terénu: plní sníženiny od jejich vlastního dna a hřebeny i rovné pláně
    /// zůstávají čisté.</para>
    ///
    /// <para><b>Cena.</b> Výšky počítá Burst job generátoru mimo hlavní vlákno (<see cref="VoxelTerrain.TryScheduleCoarseSurface"/>,
    /// bez chunků a řek); hlavní vlákno jen filtruje 64×64 hodnot. Přepočet jen po posunu kamery
    /// o čtvrtinu mapy; mezitím zůstává platná stará mapa. Seed, generování ani savy se nemění.</para>
    /// </summary>
    public sealed class ValleyMistField
    {
        public const int Size = 64;
        public static float Cell = 64f;              // m na texel → mapa 4 km
        public static float ReliefLow = 15f, ReliefHigh = 100f;
        public static float ThicknessFactor = 0.4f, ThicknessMax = 80f, LiftFactor = 0.25f;

        public Texture2D Texture { get; private set; }
        public Vector2 Origin { get; private set; }
        public float Span => Size * Cell;
        public bool Ready { get; private set; }
        public double LastBuildMs { get; private set; }
        public int Builds { get; private set; }

        private readonly float[] heights = new float[Size * Size];
        private readonly float[] lo = new float[Size * Size];
        private readonly float[] tmp = new float[Size * Size];
        private readonly ushort[] pixels = new ushort[Size * Size * 4];
        private Vector2 pendingOrigin;
        private bool building;
        private static readonly float[] row = new float[Size * Size];
        private readonly System.Diagnostics.Stopwatch watch = new System.Diagnostics.Stopwatch();

        public ValleyMistField()
        {
            Texture = new Texture2D(Size, Size, TextureFormat.RGBAHalf, false, true)
            {
                name = "Valley Mist Field",
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
                hideFlags = HideFlags.DontSave,
            };
        }

        /// <summary>Vynutí nový přepočet (QA ladění parametrů).</summary>
        public void Invalidate() => forceRebuild = true;
        private bool forceRebuild;

        public void Release()
        {
            if (Texture != null) Object.Destroy(Texture);
            Texture = null;
        }

        /// <summary>Jednou za snímek: případně objedná novou mapu (Burst job terénu) a převezme hotovou.</summary>
        public void Tick(VoxelTerrain t, Vector3 cam)
        {
            if (Texture == null || t == null) return;
            if (building)
            {
                if (!t.TryTakeCoarseSurface(heights)) return;
                building = false;
                watch.Restart();
                Build();
                watch.Stop();
                LastBuildMs = watch.Elapsed.TotalMilliseconds;
                return;
            }
            Vector2 c = new Vector2(cam.x, cam.z);
            Vector2 center = Origin + new Vector2(Span, Span) * 0.5f;
            if (Ready && !forceRebuild && (c - center).sqrMagnitude < (Span * 0.25f) * (Span * 0.25f)) return;
            forceRebuild = false;
            // Počátek na mřížce 8 buněk: mapa se neposouvá s každým krokem (žádné „plavání“ mlhy).
            float snap = Cell * 8f;
            pendingOrigin = new Vector2(Mathf.Round((cam.x - Span * 0.5f) / snap) * snap,
                                        Mathf.Round((cam.z - Span * 0.5f) / snap) * snap);
            building = t.TryScheduleCoarseSurface(pendingOrigin, Cell, Size);
        }

        private void Build()
        {
            // Dolní obálka (minimum ±2 buňky) a vyhlazení → dno údolí; průměr ±6 buněk → okolí.
            Filter(heights, tmp, 2, true);
            Filter(tmp, lo, 2, false);
            Filter(heights, tmp, 6, false);
            for (int i = 0; i < Size * Size; i++)
            {
                int x = i % Size, z = i / Size;
                float relief = tmp[i] - lo[i];
                float valley = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(ReliefLow, ReliefHigh, relief));
                // Okraj mapy bez mlhy – clamp vzorkování pak za mapou vrací nulu.
                if (x == 0 || z == 0 || x == Size - 1 || z == Size - 1) valley = 0f;
                float thick = Mathf.Clamp(relief * ThicknessFactor, 12f, ThicknessMax);
                // Hlubší údolí má i výš položenou hladinu mlhy (dno + podíl převýšení okolí).
                pixels[i * 4 + 0] = Mathf.FloatToHalf(lo[i] + relief * LiftFactor);
                pixels[i * 4 + 1] = Mathf.FloatToHalf(valley);
                pixels[i * 4 + 2] = Mathf.FloatToHalf(thick);
                pixels[i * 4 + 3] = Mathf.FloatToHalf(relief);
            }
            Texture.SetPixelData(pixels, 0);
            Texture.Apply(false, false);
            Origin = pendingOrigin;
            Ready = true;
            Builds++;
        }

        /// <summary>Oddělitelný čtvercový filtr (minimum nebo průměr) s poloměrem r buněk.</summary>
        private static void Filter(float[] src, float[] dst, int r, bool min)
        {
            for (int z = 0; z < Size; z++)
            for (int x = 0; x < Size; x++)
            {
                float acc = min ? float.MaxValue : 0f; int n = 0;
                for (int k = -r; k <= r; k++)
                {
                    int xx = Mathf.Clamp(x + k, 0, Size - 1);
                    float v = src[z * Size + xx];
                    if (min) acc = Mathf.Min(acc, v); else { acc += v; n++; }
                }
                row[z * Size + x] = min ? acc : acc / n;
            }
            for (int z = 0; z < Size; z++)
            for (int x = 0; x < Size; x++)
            {
                float acc = min ? float.MaxValue : 0f; int n = 0;
                for (int k = -r; k <= r; k++)
                {
                    int zz = Mathf.Clamp(z + k, 0, Size - 1);
                    float v = row[zz * Size + x];
                    if (min) acc = Mathf.Min(acc, v); else { acc += v; n++; }
                }
                dst[z * Size + x] = min ? acc : acc / n;
            }
        }
    }
}
