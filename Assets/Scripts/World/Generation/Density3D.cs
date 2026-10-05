using Unity.Mathematics;
using static Unity.Mathematics.math;

namespace Orivilon.World.Generation
{
    /// <summary>
    /// Kroky 10–16 pipeline: všechno, co dělá terén skutečně trojrozměrným.
    ///
    /// Statické čisté funkce, aby je mohl volat jak <see cref="DensityJob"/>, tak hrubý
    /// jeskynní test při prořezávání chunků. Jedna implementace = obojí se nemůže rozejít.
    ///
    /// Znaménková konvence celého souboru: <b>d &gt; 0 = kámen, d &lt; 0 = vzduch</b>.
    /// Sjednocení hmoty je <c>SMax</c>, odečtení dutiny <c>SMin</c>.
    /// </summary>
    public static class Density3D
    {
        // ── 10) převisy ────────────────────────────────────────────────

        /// <summary>
        /// Deformace vzorkovací souřadnice.
        ///
        /// Tohle je celý trik převisů: posunutím XZ o vektor, který závisí na Y, se svislá
        /// přímka ve světě mapuje na šikmou křivku ve výškovém poli. Skála se nakloní a tam,
        /// kde je posun větší než místní vodorovný rozestup vrstevnic, vznikne oblast,
        /// kde je nad vzduchem zase kámen. Přičítání šumu k výšce tohle udělat neumí.
        /// </summary>
        public static float3 OverhangWarp(float3 p, in GenParams gp, float mask)
        {
            // Pozor: pod `using static math` se `float3.zero` rozřeší na metodu math.float3(),
            // ne na typ. Musí to být konstruktor.
            if (mask <= 0.001f || gp.overhangAmp <= 0.001f) return new float3(0f);

            float3 q = p * gp.overhangFreq;
            int oct = 2;
            if (gp.Calm(1))
            {
                // Kolo 12b: na svahu se sklonem s se warp mění podél spádnice (1 + s)krát
                // rychleji, než je jeho perioda, protože povrch s výškou stoupá. Druhá oktáva
                // (19 m) a stejná svislá perioda proto na běžném svahu vrásnily vrstevnice do
                // husté sítě tenkých rýh. Jedna oktáva a delší svislá perioda nechají velké
                // převisy na útesech (tam je sklon dost velký na přehyb), ale svah nevrásní.
                q.y *= gp.overhangYStretch;
                oct = max(1, gp.overhangOct);
            }
            return gp.overhangAmp * mask * new float3(
                GenNoise.Fbm3(q, oct, gp.offWarpX3),
                GenNoise.Fbm3(q, oct, gp.offWarpY3) * 0.45f,   // svisle méně, jinak se terén "vaří"
                GenNoise.Fbm3(q, oct, gp.offWarpZ3));
        }

        /// <summary>Nevýškopisné boule v úzkém pásu kolem povrchu.</summary>
        public static float SurfaceDetail(float3 p, in GenParams gp, float mask, float surfY)
        {
            if (mask <= 0.001f || gp.squishAmp <= 0.001f) return 0f;

            float t = (p.y - surfY) / max(gp.surfaceBand, 0.5f);
            float band = exp(-t * t);
            if (band < 0.01f) return 0f;

            int oct = gp.Calm(1) ? max(1, gp.squishOct) : 3;
            return gp.squishAmp * mask * band * GenNoise.Fbm3(p * gp.squishFreq, oct, gp.offSquish);
        }

        // ── 13, 14) jeskyně ────────────────────────────────────────────

        /// <summary>
        /// Kolik vzduchu je v tomto bodě, 0–1.
        ///
        /// Tři typy dutin s odlišnou topologií, skládané přes <c>max</c> (každý reprezentuje
        /// totéž „jak moc je tu vzduch", takže sčítat by nedávalo smysl):
        ///
        ///  • <b>tunely</b> – průnik nulových hladin dvou 3D šumů. Nulová hladina jednoho je
        ///    plocha, průnik dvou je křivka; ztloustnutím vznikne chodba.
        ///  • <b>síně</b> – prahovaný 3D šum. Vysoký práh = vzácné, ale velké dutiny.
        ///    Tunely jimi procházejí samy od sebe, protože žijí ve stejném prostoru.
        ///  • <b>propasti</b> – 2D hřeben protažený svisle, sahající až k povrchu.
        ///
        /// <paramref name="below"/> je hloubka pod povrchem (surfY − y).
        /// </summary>
        /// <summary>Kolo 12b: minimální výška dutiny, aby se u povrchu otevřela (m).</summary>
        public const float CaveRoofLift = 3f;

        public static float CaveCarve(float3 p, in GenParams gp, float below)
        {
            // Nahoře brána proti děravému povrchu (plná síla už dva metry pod ním – dřív
            // tu bylo smoothstep(-1.5, 7), což propouštělo 8 % a proto nešel najít vchod),
            // dole strop podzemí.
            //
            // Ten spodní strop není kosmetika: pod caveMaxDepth je hustota zaručeně plná,
            // takže se tamní chunky vůbec nemusí meshovat (viz ColumnChunkRange). Bez toho
            // by cena za hlubší svět rostla lineárně, aniž by to hráč kdy uviděl.
            float depth = smoothstep(-6f, 2f, below)
                        * (1f - smoothstep(gp.caveMaxDepth - 40f, gp.caveMaxDepth, below));
            if (depth <= 0.0005f) return 0f;

            CaveComponents(p, gp, below, out float tunnel, out float hall, out float ravine);

            float carve = max(max(tunnel, hall), ravine);
            if (carve <= 0f) return 0f;

            // Kolo 12b: u povrchu se dutina otevře jen tam, kde je aspoň CaveRoofLift metrů
            // vysoká (min se vzorkem o kus výš). Tunel, který se povrchu jen dotkne – typicky
            // podél vrstevnice svahu – jinak prorazil dlouhou štěrbinu širokou metr, která
            // je z dálky vidět jako tenká tmavá čára. Skutečné vstupy (dutina vyšší než
            // CaveRoofLift) zůstávají, jen se okraj otvoru zaoblí. Pod 10 m beze změny.
            if (gp.Calm(4) && below < 10f)
            {
                CaveComponents(p + new float3(0f, CaveRoofLift, 0f), gp, below - CaveRoofLift,
                               out float t2, out float h2, out float r2);
                float up = max(max(t2, h2), r2);
                carve = lerp(min(carve, up), carve, smoothstep(6f, 10f, below));
                if (carve <= 0f) return 0f;
            }

            // ── vstupy ────────────────────────────────────────────────
            // Hluboko se neomezuje nic. U povrchu propustí plnou sílu jen tam, kde to dovolí
            // nízkofrekvenční maska – jinde je strop zavřený. Bez toho je povrch buď
            // neprodyšný, nebo děravý jako ementál.
            float mask = smoothstep(0.30f, 0.62f, GenNoise.Fbm2(p.xz * gp.entranceFreq, 2, gp.offEntrance));
            float mouth = lerp(gp.entranceMin, 1f, mask);
            float nearSurface = 1f - smoothstep(4f, gp.entranceDepth, below);

            return carve * depth * lerp(1f, mouth, nearSurface);
        }

        /// <summary>
        /// Samotné tvary dutin bez hloubkové brány a bez masky vstupů.
        ///
        /// Oddělené kvůli měření: <c>VoxelChunkBuilder.MeasureCaves</c> potřebuje vědět,
        /// který generátor za daný kus vzduchu může. Objemový podíl se totiž u každého
        /// škáluje jinak — u tunelů jako 1/P², u propastí jen jako 1/P, protože propast
        /// je 2D pás protažený svisle. Bez rozpadu po typech se ladí naslepo.
        /// </summary>
        public static void CaveComponents(float3 p, in GenParams gp, float below,
                                          out float tunnel, out float hall, out float ravine)
        {
            // Míra hloubky, 0 u povrchu a 1 v plné hloubce. Nahoře chodby, dole síně –
            // tím se podzemí s klesáním „otevírá" místo aby vypadalo ve všech patrech
            // stejně. Právě tenhle rozdíl dělá z jeskyní systém, ne jen síť trubek.
            float deep = smoothstep(gp.deepStart, gp.deepFull, below);
            float widen = 1f + gp.deepWiden * deep;

            // ── 1) hustá síť užších chodeb ────────────────────────────
            float3 ts = new float3(gp.tunnelFreqXZ, gp.tunnelFreqY, gp.tunnelFreqXZ);
            float n1 = GenNoise.Fbm3(p * ts, 2, gp.offCave1);
            float n2 = GenNoise.Fbm3(p * ts, 2, gp.offCave2);
            tunnel = Plateau(max(abs(n1), abs(n2)) / (gp.tunnelRadius * widen)) * gp.tunnelStrength;

            // ── 2) hlavní tahy: druhá rodina s delší periodou ──────────
            //
            // Jedna rodina chodeb dá vždycky jen jedno měřítko – buď hustou síť úzkých
            // trubek, nebo pár osamělých širokých. Dvě nezávislá pole ve stejném prostoru
            // se protínají sama od sebe, takže vznikne hierarchie: široká galerie, ze které
            // odbočují úzké chodby. Sčítat je nelze, obě říkají „kolik je tu vzduchu",
            // takže se skládají přes max stejně jako zbytek.
            if (gp.tunnelBigStrength > 0.001f)
            {
                float3 bs = new float3(gp.tunnelBigFreqXZ, gp.tunnelBigFreqY, gp.tunnelBigFreqXZ);
                float m1 = GenNoise.Fbm3(p * bs, 2, gp.offCave3);
                float m2 = GenNoise.Fbm3(p * bs, 2, gp.offCave4);

                tunnel = max(tunnel, Plateau(max(abs(m1), abs(m2)) / (gp.tunnelBigRadius * widen))
                                     * gp.tunnelBigStrength);
            }

            // ── 3) síně (cheese) ──────────────────────────────────────
            //
            // Práh s hloubkou KLESÁ, takže hluboko je síní víc a jsou větší. Nahoře je
            // naopak vzácný, jinak by se obří dutina otevřela hned pod loukou a strop by
            // se propadl. Vlastní hloubková brána navíc drží síně pod hallTop.
            float nh = GenNoise.Fbm3(p * new float3(gp.hallFreqXZ, gp.hallFreqY, gp.hallFreqXZ),
                                     3, gp.offHall);
            float thr = gp.hallThreshold - gp.deepHallDrop * deep;

            hall = smoothstep(thr, thr + 0.10f, nh) * gp.hallStrength
                 * smoothstep(gp.hallTop - 10f, gp.hallTop + 20f, below);

            // ── 4) propasti ───────────────────────────────────────────
            float nr = abs(GenNoise.Fbm2(p.xz * gp.ravineFreq, 3, gp.offRavine));
            ravine = Plateau(nr / gp.ravineRadius)
                   * smoothstep(gp.ravineTop - 8f, gp.ravineTop + 12f, below)
                   * (1f - smoothstep(gp.ravineBottom, gp.ravineBottom + 25f, below))
                   * gp.ravineStrength;
        }

        /// <summary>
        /// Profil dutiny napříč průřezem. <paramref name="t"/> je 0 v ose a 1 na jmenovitém
        /// okraji. Plná síla do 35 % poloměru, pak náběh na nulu – dutina je tak skoro celá
        /// průchozí, místo aby se otevřelo jen jádro.
        /// </summary>
        private static float Plateau(float t) => 1f - smoothstep(0.35f, 1f, t);

        // ── 15) skalní brány ───────────────────────────────────────────

        /// <summary>
        /// Rozhodne, jestli v dané buňce vzniká brána. Podmínkou je strmý terén – brána
        /// potřebuje stěnu, do které se dá prorazit otvor.
        /// </summary>
        public static ArchBody EvalArchCell(int2 cell, in GenParams gp, in SplineSet spl)
        {
            ArchBody a = default;
            int archSeed = gp.seed ^ 0x0A2C4;
            uint id = GenNoise.Hash(cell, archSeed);
            if (GenNoise.Hash01(id) >= gp.archChance) return a;

            float2 c = GenNoise.CellCenter(cell, gp.archCell, archSeed);
            WorldGenMath.EvalMacro(c, gp, spl, out float my, out _, out _, out _, out _);
            if (my <= gp.archMinY || my >= gp.archMaxY) return a;

            // Gradient makro výšky dává rám: osa otvoru vede po spádnici, tedy skrz stěnu.
            const float h = 8f;
            WorldGenMath.EvalMacro(c + new float2(h, 0f), gp, spl, out float e, out _, out _, out _, out _);
            WorldGenMath.EvalMacro(c - new float2(h, 0f), gp, spl, out float w, out _, out _, out _, out _);
            WorldGenMath.EvalMacro(c + new float2(0f, h), gp, spl, out float n, out _, out _, out _, out _);
            WorldGenMath.EvalMacro(c - new float2(0f, h), gp, spl, out float s, out _, out _, out _, out _);

            float2 g = new float2((e - w) / (2f * h), (n - s) / (2f * h));
            float slope = length(g);
            if (slope < gp.archSlopeMin) return a;

            uint h2 = GenNoise.Hash(id ^ 0x27D4EB2Fu);
            float scale = lerp(0.75f, 1.35f, GenNoise.Hash01(h2));

            a.active = 1;
            a.center = c;
            a.axis = g / slope;
            a.radius = gp.archRadius * scale;
            a.tube = gp.archTube * scale;
            // Oblouk se posadí nad terén tak, aby otvor stál na zemi, ne v ní.
            a.baseY = my + a.tube * 0.35f;
            return a;
        }

        /// <summary>
        /// Přidá masiv brány a odečte z něj otvor. Vrací upravenou hustotu.
        ///
        /// Nejdřív se přisadí kvádr skály (jinak by brána stála v ničem), pak se z něj
        /// odečte půltorus. <c>max(q.y, 0)</c> dělá z torusu půltorus – spodní polovina
        /// oblouku se zploští do dvou svislých pilířů místo aby pokračovala pod zem.
        /// </summary>
        public static float ApplyArch(float d, float3 p, in GenParams gp, in ArchBody a)
        {
            if (a.active == 0) return d;

            float2 v = p.xz - a.center;
            float dy = p.y - a.baseY;

            // Útlum musí být SPOJITÝ. Dřív se tady jen tvrdě vyskočilo z funkce, jenže
            // o kus níž se k hustotě přičítá šum – na kouli kolem každé brány tak vznikl
            // skok v poli a v terénu díra. Brány stojí na strmých svazích, takže se to
            // projevovalo právě v kopcích.
            float3 rel = new float3(v.x, dy, v.y);
            float reach = a.radius + a.tube + 14f;
            float r2 = dot(rel, rel);
            if (r2 >= reach * reach) return d;

            float fall = 1f - smoothstep(reach * 0.55f, reach, sqrt(r2));

            // lokální rám: x podél stěny, z skrz otvor
            float2 t = new float2(-a.axis.y, a.axis.x);
            float3 q = new float3(dot(v, t), dy, dot(v, a.axis));

            // 1) masiv
            float3 half = new float3(a.radius + a.tube + 5f, a.radius + a.tube + 8f, 6f);
            float3 e = abs(q) - half;
            float dSlab = length(max(e, 0f)) + min(max(e.x, max(e.y, e.z)), 0f) - 3f;
            d = GenNoise.SMax(d, -dSlab, gp.archSmooth);

            // 2) otvor
            float2 c2 = new float2(length(new float2(q.x, max(q.y, 0f))) - a.radius, q.z);
            float dHole = length(c2) - a.tube;
            d = GenNoise.SMin(d, dHole, gp.archSmooth * 0.75f);

            // 3) rozbití CSG hrany, ať to nevypadá modelovaně – násobeno útlumem,
            //    aby člen na hranici dosahu plynule zmizel
            d += gp.archNoise * fall * GenNoise.Fbm3(p * 0.11f, 3, gp.offArch);
            return d;
        }
    }
}
