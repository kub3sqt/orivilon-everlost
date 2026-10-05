using System.Diagnostics;
using Orivilon.World.Generation;
using Orivilon.World.Generation.Hydro;
using Unity.Collections;
using Unity.Mathematics;
using UnityEditor;
using UnityEngine;

namespace Orivilon.EditorTools
{
    /// <summary>
    /// Náhled sloupcové vrstvy generátoru světa (fáze 0).
    ///
    /// Smysl: ráz světa – kolik je oceánu, jak vysoko sahají hory, kde tečou řeky –
    /// se ladí tady na 2D textuře, ne v běžící hře na voxelech. Smyčka "změň spline,
    /// podívej se" je tu v řádu desetin sekundy místo desítek.
    ///
    /// Otevře se přes menu Orivilon → World Gen Preview.
    /// </summary>
    public class WorldGenPreviewWindow : EditorWindow
    {
        private enum View
        {
            Hillshade,
            SurfaceHeight,
            MacroHeight,
            Continentalness,
            Erosion,
            PeaksValleys,
            Slope,
            Rivers,
            Lakes,
            Cliffs,
            Temperature,
            Humidity,
            TerrainColor,
        }

        [SerializeField] private WorldGenSettings settings;
        [SerializeField] private int seed = 1337;
        [SerializeField] private Vector2 center = Vector2.zero;
        [SerializeField] private float metersPerPixel = 8f;
        [SerializeField] private int resolution = 512;
        [SerializeField] private View view = View.Hillshade;
        [SerializeField] private bool autoRefresh = true;

        private Texture2D texture;
        private string stats = "";
        private double lastMs;

        // ── hydrologie ─────────────────────────────────────────────────
        //
        // Mapa se drží mezi překreslením: postavit region trvá desítky milisekund a osm
        // kilometrů náhledu jich potřebuje devět. Bez cache by každé pohnutí posuvníkem
        // stálo půl vteřiny.
        //
        // Vlastní spline LUT je tu proto, že mapa je přežije: Rebuild ty svoje uvolňuje
        // na konci, a mapa by pak četla z uvolněné paměti.
        private HydroMap hydro;
        private SplineSet hydroSpl;
        private int hydroSeed;
        private WorldGenSettings hydroSettings;

        [MenuItem("Orivilon/World Gen Preview")]
        private static void Open()
        {
            var w = GetWindow<WorldGenPreviewWindow>("World Gen");
            w.minSize = new Vector2(560f, 620f);
        }

        private void OnDisable()
        {
            if (texture != null) DestroyImmediate(texture);
            ReleaseHydro();
        }

        /// <summary>
        /// Zajistí hydrologickou mapu pro aktuální seed a nastavení.
        /// </summary>
        /// <param name="force">
        /// Přestavět, i když seed i asset sedí. Editace hodnot uvnitř assetu totiž tímhle
        /// oknem neprojde – proto se cache zahazuje na tlačítko „Vygenerovat".
        /// </param>
        private void EnsureHydro(bool force)
        {
            if (!force && hydro != null && hydroSeed == seed && hydroSettings == settings) return;

            ReleaseHydro();

            hydroSpl = settings.BuildSplines(Allocator.Persistent);
            hydro = new HydroMap(settings.ToParams(seed), hydroSpl);
            hydroSeed = seed;
            hydroSettings = settings;
        }

        private void ReleaseHydro()
        {
            hydro?.Dispose();
            hydro = null;

            if (hydroSpl.baseC.IsCreated) hydroSpl.Dispose();
            hydroSpl = default;
            hydroSettings = null;
        }

        private void OnGUI()
        {
            EditorGUI.BeginChangeCheck();

            settings = (WorldGenSettings)EditorGUILayout.ObjectField(
                "Nastavení", settings, typeof(WorldGenSettings), false);

            using (new EditorGUILayout.HorizontalScope())
            {
                seed = EditorGUILayout.IntField("Seed", seed);
                if (GUILayout.Button("Náhodný", GUILayout.Width(80f)))
                    seed = UnityEngine.Random.Range(int.MinValue, int.MaxValue);
            }

            center = EditorGUILayout.Vector2Field("Střed (m)", center);
            metersPerPixel = Mathf.Max(0.25f,
                EditorGUILayout.FloatField("Metrů na pixel", metersPerPixel));

            resolution = EditorGUILayout.IntPopup("Rozlišení", resolution,
                new[] { "128", "256", "512", "1024" }, new[] { 128, 256, 512, 1024 });

            view = (View)EditorGUILayout.EnumPopup("Pohled", view);

            bool changed = EditorGUI.EndChangeCheck();

            using (new EditorGUILayout.HorizontalScope())
            {
                autoRefresh = EditorGUILayout.ToggleLeft("Překreslovat automaticky", autoRefresh,
                    GUILayout.Width(200f));

                using (new EditorGUI.DisabledScope(settings == null))
                {
                    // Tlačítko zahazuje i hydrologii – jinak by se změna spliny nebo
                    // amplitudy pohoří v assetu na řekách neprojevila.
                    if (GUILayout.Button("Vygenerovat")) Rebuild(true);
                    if (GUILayout.Button("Uložit PNG")) SavePng();
                }
            }

            float span = resolution * metersPerPixel;
            EditorGUILayout.LabelField($"Výřez {span:0} × {span:0} m", EditorStyles.miniLabel);

            if (settings == null)
            {
                EditorGUILayout.HelpBox(
                    "Přiřaď WorldGenSettings. Nový asset: Create → Everlost → World Gen Settings.",
                    MessageType.Info);
                return;
            }

            // Rebuild se drží mimo Repaint: IMGUI si stěžuje, když se mezi Layout a Repaint
            // změní počet vykreslených prvků.
            if (changed && autoRefresh) Rebuild();
            if (texture == null && Event.current.type == EventType.Layout) Rebuild();

            DrawMap();

            EditorGUILayout.HelpBox(string.IsNullOrEmpty(stats) ? "—" : stats, MessageType.None);
        }

        private void DrawMap()
        {
            // Rect se rezervuje vždy, i bez textury – jinak by se rozešel počet prvků
            // mezi Layout a Repaint.
            float size = Mathf.Min(position.width - 24f, position.height - 250f);
            size = Mathf.Max(size, 128f);

            Rect r = GUILayoutUtility.GetRect(size, size, GUILayout.ExpandWidth(false));
            r.x = (position.width - size) * 0.5f;

            if (texture == null) return;
            EditorGUI.DrawPreviewTexture(r, texture, null, ScaleMode.ScaleToFit);

            // Klik do mapy přesune střed výřezu – rychlé procházení světa.
            Event e = Event.current;
            if (e.type == EventType.MouseDown && e.button == 0 && r.Contains(e.mousePosition))
            {
                Vector2 uv = (e.mousePosition - new Vector2(r.x, r.y)) / size;
                float span = resolution * metersPerPixel;
                center += new Vector2((uv.x - 0.5f) * span, (0.5f - uv.y) * span);
                Rebuild();
                e.Use();
            }

            EditorGUILayout.LabelField(
                $"Klik do mapy přesune střed · {lastMs:0.0} ms",
                EditorStyles.centeredGreyMiniLabel);
        }

        private void Rebuild(bool freshHydro = false)
        {
            if (settings == null) return;

            EnsureHydro(freshHydro);

            var sw = Stopwatch.StartNew();

            GenParams gp = settings.ToParams(seed);
            SplineSet spl = settings.BuildSplines(Allocator.Persistent);

            int side = resolution;
            float2 origin = new float2(center.x, center.y) - (side - 1) * metersPerPixel * 0.5f;
            float2 end = origin + (side - 1) * metersPerPixel;

            // Náhled je běžně širší než region, takže se pohled sešije z několika.
            // Chybějící regiony se přitom dostaví, což je poprvé vidět na čase.
            HydroRegionRef hydroRef = hydro.BuildStitched(origin, end);

            ColumnField f = WorldGen.Generate(origin, metersPerPixel, side, gp, spl,
                                              hydroRef, Allocator.Persistent);
            sw.Stop();
            lastMs = sw.Elapsed.TotalMilliseconds;

            BuildTexture(f, gp);
            BuildStats(f, gp);

            f.Dispose();
            spl.Dispose();
            HydroMap.DisposeStitched(ref hydroRef);

            Repaint();
        }

        private void BuildTexture(ColumnField f, GenParams gp)
        {
            int side = f.side;
            if (texture == null || texture.width != side)
            {
                if (texture != null) DestroyImmediate(texture);
                texture = new Texture2D(side, side, TextureFormat.RGBA32, false)
                {
                    filterMode = FilterMode.Point,
                    wrapMode = TextureWrapMode.Clamp,
                    hideFlags = HideFlags.HideAndDontSave,
                };
            }

            var px = new Color32[side * side];

            for (int z = 0; z < side; z++)
            for (int x = 0; x < side; x++)
            {
                int i = z * side + x;
                Color c;

                switch (view)
                {
                    case View.Hillshade:
                        c = Hypsometric(f.surfY[i], gp.seaLevel);
                        if (f.lakeY[i] > ColumnField.NoLake && f.surfY[i] < f.lakeY[i])
                            c = new Color(0.24f, 0.52f, 0.72f);
                        else if (f.riverCore[i] > 0.35f && f.surfY[i] > gp.seaLevel - 2f)
                            c = Color.Lerp(c, new Color(0.20f, 0.46f, 0.70f), f.riverCore[i]);
                        c *= Hillshade(f, x, z);
                        break;

                    case View.SurfaceHeight: c = Ramp01(Remap(f.surfY[i], -70f, 260f)); break;
                    case View.MacroHeight:   c = Ramp01(Remap(f.macroY[i], -70f, 260f)); break;
                    case View.Continentalness: c = Diverging(f.cont[i]); break;
                    case View.Erosion:         c = Diverging(f.eros[i]); break;
                    case View.PeaksValleys:    c = Diverging(f.pv[i]); break;
                    case View.Slope:           c = Ramp01(Mathf.Clamp01(f.slope[i] * 2f)); break;
                    case View.Cliffs:          c = Ramp01(f.cliff[i]); break;
                    case View.Temperature:     c = Diverging(f.temp[i] * 2f - 1f); break;
                    case View.Humidity:        c = Diverging(f.hum[i] * 2f - 1f); break;

                    case View.Rivers:
                        c = Color.Lerp(new Color(0.12f, 0.12f, 0.14f),
                                       new Color(0.35f, 0.72f, 1f), f.riverCore[i]);
                        if (f.surfY[i] < gp.seaLevel) c = new Color(0.06f, 0.10f, 0.18f);
                        break;

                    case View.Lakes:
                        bool lake = f.lakeY[i] > ColumnField.NoLake;
                        c = lake ? new Color(0.30f, 0.78f, 0.85f)
                                 : Hypsometric(f.surfY[i], gp.seaLevel) * 0.45f;
                        break;

                    // Barva terénu přesně tak, jak ji dostane mesh – včetně břehového písku,
                    // klimatického zabarvení a sněžné čáry. Je to jediný pohled, ve kterém
                    // jde paletu ladit bez spouštění hry, a hlavně jediný, kde je vidět,
                    // jestli se pásy písku vůbec někde objevily.
                    //
                    // Sklon se nedosazuje 1 (rovina): pak by zmizely útesy a paleta by
                    // vypadala mnohem monotónněji, než ve skutečnosti je. Skládá se
                    // z gradientu makro výšky, tedy z téhož čísla jako ve hře.
                    case View.TerrainColor:
                    {
                        // Výchozí paleta z kódu, ne ta ze scény: náhled má ukazovat, jak
                        // svět vypadá s aktuálním nastavením v kódu. Scéna má vlastní
                        // serializovanou kopii a ta se sem záměrně netahá.
                        TerrainPalette pal = TerrainPalette.Default;
                        pal.EnsureClimate();

                        float ny = 1f / Mathf.Sqrt(1f + f.slope[i] * f.slope[i]);
                        float3 rgb = pal.Evaluate(f.surfY[i], ny, f.temp[i], f.hum[i], f.shoreY[i]);
                        rgb = math.saturate(pal.Vary(rgb, new float3(f.WorldAt(x, z).x, f.surfY[i],
                                                                     f.WorldAt(x, z).y), gp.offMicro));
                        c = new Color(rgb.x, rgb.y, rgb.z);

                        // Voda se dokreslí navrch, jinak není poznat, kde písek začíná.
                        if (f.lakeY[i] > ColumnField.NoLake && f.surfY[i] < f.lakeY[i])
                            c = Color.Lerp(c, new Color(0.18f, 0.42f, 0.66f), 0.85f);
                        else if (f.surfY[i] < gp.seaLevel)
                            c = Color.Lerp(c, new Color(0.10f, 0.28f, 0.52f), 0.85f);
                        else if (f.riverCore[i] > 0.35f)
                            c = Color.Lerp(c, new Color(0.20f, 0.46f, 0.70f), f.riverCore[i] * 0.9f);

                        c *= Hillshade(f, x, z);
                        break;
                    }

                    default: c = Color.magenta; break;
                }

                px[z * side + x] = c;
            }

            texture.SetPixels32(px);
            texture.Apply(false);
        }

        private void BuildStats(ColumnField f, GenParams gp)
        {
            int n = f.side * f.side;
            int ocean = 0, river = 0, lake = 0, mountain = 0;

            for (int i = 0; i < n; i++)
            {
                if (f.surfY[i] < gp.seaLevel) ocean++;
                if (f.riverCore[i] > 0.35f && f.surfY[i] >= gp.seaLevel) river++;
                if (f.lakeY[i] > ColumnField.NoLake) lake++;
                if (f.surfY[i] > 120f) mountain++;
            }

            float inv = 100f / n;
            stats =
                $"výška {f.MinHeight:0.0} … {f.MaxHeight:0.0} m\n" +
                $"oceán {ocean * inv:0.0} %  ·  řeky {river * inv:0.00} %  ·  " +
                $"jezera {lake * inv:0.00} %  ·  nad 120 m {mountain * inv:0.0} %\n" +
                $"{f.side}² bodů za {lastMs:0.0} ms  ({n / System.Math.Max(1.0, lastMs) / 1000.0:0.0} M bodů/s)";
        }

        private float Hillshade(ColumnField f, int x, int z)
        {
            int side = f.side;
            int xm = Mathf.Max(x - 1, 0), xp = Mathf.Min(x + 1, side - 1);
            int zm = Mathf.Max(z - 1, 0), zp = Mathf.Min(z + 1, side - 1);

            float dx = (f.surfY[z * side + xp] - f.surfY[z * side + xm]) / (2f * f.step);
            float dz = (f.surfY[zp * side + x] - f.surfY[zm * side + x]) / (2f * f.step);

            Vector3 n = new Vector3(-dx, 1f, -dz).normalized;
            Vector3 l = new Vector3(-0.55f, 0.72f, -0.42f).normalized;
            return Mathf.Clamp01(0.42f + 0.78f * Mathf.Max(0f, Vector3.Dot(n, l)));
        }

        private static float Remap(float v, float a, float b) => Mathf.Clamp01((v - a) / (b - a));

        private static Color Ramp01(float t)
        {
            t = Mathf.Clamp01(t);
            return new Color(t, t, t);
        }

        private static Color Diverging(float v)
        {
            v = Mathf.Clamp(v, -1f, 1f);
            return v < 0f
                ? Color.Lerp(new Color(0.95f, 0.95f, 0.95f), new Color(0.10f, 0.36f, 0.62f), -v)
                : Color.Lerp(new Color(0.95f, 0.95f, 0.95f), new Color(0.70f, 0.34f, 0.06f), v);
        }

        /// <summary>Hypsometrická škála – standardní čtení mapy: modrá, písek, zeleň, skála, sníh.</summary>
        private static Color Hypsometric(float y, float sea)
        {
            float h = y - sea;
            if (h < -30f) return new Color(0.06f, 0.14f, 0.28f);
            if (h < -6f)  return Color.Lerp(new Color(0.06f, 0.14f, 0.28f), new Color(0.13f, 0.33f, 0.52f), Remap(h, -30f, -6f));
            if (h < 0f)   return Color.Lerp(new Color(0.13f, 0.33f, 0.52f), new Color(0.28f, 0.55f, 0.68f), Remap(h, -6f, 0f));
            if (h < 3f)   return new Color(0.80f, 0.74f, 0.54f);
            if (h < 40f)  return Color.Lerp(new Color(0.36f, 0.51f, 0.28f), new Color(0.29f, 0.44f, 0.23f), Remap(h, 3f, 40f));
            if (h < 95f)  return Color.Lerp(new Color(0.29f, 0.44f, 0.23f), new Color(0.44f, 0.40f, 0.28f), Remap(h, 40f, 95f));
            if (h < 165f) return Color.Lerp(new Color(0.44f, 0.40f, 0.28f), new Color(0.52f, 0.50f, 0.49f), Remap(h, 95f, 165f));
            return Color.Lerp(new Color(0.52f, 0.50f, 0.49f), Color.white, Remap(h, 165f, 240f));
        }

        private void SavePng()
        {
            if (texture == null) return;

            string path = EditorUtility.SaveFilePanel("Uložit náhled", "", "worldgen_preview.png", "png");
            if (string.IsNullOrEmpty(path)) return;

            System.IO.File.WriteAllBytes(path, texture.EncodeToPNG());
            UnityEngine.Debug.Log($"[WorldGenPreview] Uloženo: {path}");
        }
    }
}
