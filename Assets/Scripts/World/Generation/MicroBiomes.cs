using Unity.Mathematics;
using static Unity.Mathematics.math;

namespace Orivilon.World.Generation
{
    /// <summary>
    /// Vzácná místa, která se nedají odvodit z klimatu.
    ///
    /// <para><b>Proč zvlášť od biomů.</b> Klimatické biomy jsou spojité – teplota a vlhkost
    /// se mění plynule, takže z nich nikdy nevznikne „tady a nikde jinde". Objevování ale
    /// stojí přesně na tom: na místě, které se nedá předpovědět z okolí. Proto to nejsou
    /// biomy, ale Worley buňky posazené NAD hotový svět.</para>
    /// </summary>
    public enum MicroBiome
    {
        /// <summary>Běžný svět.</summary>
        None = 0,

        /// <summary>Zlatý háj – jantarové listí, břízy, hřejivé světlo. Terén beze změny.</summary>
        GoldenGrove = 1,

        /// <summary>Čedičové pole – černá skála, sloupcová odlučnost, chladné světlo.</summary>
        BasaltField = 2,
    }

    /// <summary>Nastavení mikro-biomů v podobě, kterou unese Burst (žádné třídy, jen čísla).</summary>
    public struct MicroParams
    {
        /// <summary>Rozteč mřížky buněk v metrech. Jedna buňka = jeden možný výskyt.</summary>
        public float cell;

        /// <summary>Poloměr skvrny jako podíl buňky.</summary>
        public float radius;

        /// <summary>Šířka měkkého okraje v metrech.</summary>
        public float blend;

        /// <summary>Jaký podíl buněk je obsazený. 0,033 = zhruba jedna ze třiceti.</summary>
        public float chance;

        /// <summary>Seed světa. Mikro-biomy mají vlastní odvozený kanál.</summary>
        public int seed;

        public bool Enabled => cell > 1f && chance > 0.0001f;
    }

    /// <summary>
    /// Výpočet mikro-biomu v bodě. Čistě analytický a bez stavu, takže tutéž odpověď dá
    /// job na mřížce, bodový dotaz pro osazení i konzole – žádná mapa se nikde nedrží.
    /// </summary>
    public static class MicroBiomeMath
    {
        /// <summary>Vlastní kanál hashe, aby se rozmístění netrefovalo do jezer ani do bran.</summary>
        private const int Channel = 0x5EED1;

        /// <summary>
        /// Nejsilnější mikro-biom v bodě.
        /// </summary>
        /// <param name="kind">Hodnota <see cref="MicroBiome"/> jako int – enum přes Burst neprojde.</param>
        /// <param name="weight">Síla 0–1. Na okraji skvrny klesá k nule, takže hrana není kružnice.</param>
        public static void Sample(float2 p, in MicroParams mp, out int kind, out float weight)
        {
            kind = 0;
            weight = 0f;
            if (!mp.Enabled) return;

            int2 home = (int2)floor(p / mp.cell);
            float r = mp.cell * mp.radius;

            // Prohledávají se i sousední buňky: střed skvrny je uvnitř buňky rozhozený,
            // takže dosah může přesáhnout hranici. Bez okolí by skvrny u hranic mizely.
            for (int oz = -1; oz <= 1; oz++)
            for (int ox = -1; ox <= 1; ox++)
            {
                int2 c = home + new int2(ox, oz);
                uint h = GenNoise.Hash(c, mp.seed ^ Channel);

                // Obsazenost buňky. Práh je jediné místo, kde se rozhoduje o vzácnosti.
                if (GenNoise.Hash01(h) >= mp.chance) continue;

                // Druh: druhý hash, ne ten samý. Kdyby se použil týž, byla by vzácnost
                // a druh na sobě závislé a jeden z druhů by prakticky nevznikal.
                uint h2 = GenNoise.Hash(h ^ 0x9E3779B9u);
                int k = GenNoise.Hash01(h2) < 0.5f ? 1 : 2;

                // Střed skvrny uvnitř buňky, aby mřížka nebyla poznat.
                float2 center = GenNoise.CellCenter(c, mp.cell, mp.seed ^ Channel);

                // Poloměr se liší o ±25 %, jinak by všechny skvrny byly stejně velké.
                float rr = r * (0.75f + 0.5f * GenNoise.Hash01(GenNoise.Hash(h2 ^ 0x85EBCA6Bu)));

                float d = distance(p, center);
                float w = 1f - smoothstep(max(rr - mp.blend, 0f), rr, d);

                if (w > weight) { weight = w; kind = k; }
            }
        }

        /// <summary>
        /// Mikro-biom s ohledem na moře.
        ///
        /// <para><b>Proč se netestuje střed buňky.</b> Zjistit výšku terénu ve středu buňky
        /// znamená celé makro vyhodnocení včetně domain warpu – ten body posouvá až o kilometr,
        /// takže samotný šum kontinentality na souřadnici středu je vedle. A to všechno by
        /// se muselo počítat při KAŽDÉM dotazu, tedy i na každý trojúhelník při barvení.</para>
        ///
        /// <para>Test v bodě je zadarmo (výšku už volající má) a vyjde z něj i lepší tvar:
        /// háj nezmizí celý kvůli tomu, že mu půlka spadla do zálivu – prostě skončí na
        /// břehu. Čediči se naopak nechává i podmořská část, sopečná lavice pod hladinou
        /// je správně.</para>
        /// </summary>
        public static void SampleSurface(float2 p, in MicroParams mp, float surfaceY, float seaLevel,
                                         out int kind, out float weight)
        {
            Sample(p, mp, out kind, out weight);

            if (kind == (int)MicroBiome.GoldenGrove)
            {
                weight *= LandFade(surfaceY, seaLevel);
                if (weight < 0.001f) kind = 0;
            }
        }

        /// <summary>Kolik ze souše zbývá: 0 pod hladinou, 1 od šesti metrů nad ní.</summary>
        public static float LandFade(float y, float seaLevel)
            => smoothstep(seaLevel + 1f, seaLevel + 7f, y);

        /// <summary>Jen druh, bez síly – pro konzoli a hledání.</summary>
        public static int KindAt(float2 p, in MicroParams mp)
        {
            Sample(p, mp, out int k, out float w);
            return w > 0.35f ? k : 0;
        }

        /// <summary>
        /// Sloupcová odlučnost čediče.
        ///
        /// <para>Čedič chladne a praská do svislých pěti- až šestibokých sloupů. Napodobuje
        /// se to tak, že se povrch rozdělí na malé Worley buňky a KAŽDÁ se posadí na vlastní
        /// vodorovnou úroveň. Mezi sousedními buňkami tím vznikne svislá hrana – přesně to,
        /// co dělá ten low-poly kontrast. Jde to jen proto, že marching cubes svislou stěnu
        /// zvládne; výšková mapa by v ní měla schod přes celý voxel.</para>
        ///
        /// <para><b>Mizí s hrubším vzorkováním.</b> Sloup je 3–4 m široký, takže při kroku
        /// mřížky 8 nebo 16 m (vzdálené LOD prstence) by z něj nezůstal tvar, ale šum –
        /// terén by se v dálce roztřásl. Proto efekt do dálky vyhasne a zblízka naskočí.</para>
        /// </summary>
        /// <param name="y">Výška povrchu před zásahem.</param>
        /// <param name="sampleStep">Rozteč mřížky v metrech, na které se zrovna počítá.</param>
        /// <param name="seaLevel">Hladina moře – u ní se sloupce potlačí.</param>
        public static float BasaltColumns(float2 p, float y, float weight, float sampleStep,
                                          float seaLevel, int seed)
        {
            float fade = saturate(1.6f - sampleStep * 0.3f);

            // Útlum u hladiny. Sloup vysoký dva metry, který ji protne, neudělá ostrov –
            // udělá skvrnu drobných ostrůvků a v každém z nich si stavitel hladiny musí
            // znovu rozhodnout, jestli tam voda je. Pobřeží by se z toho rozpadlo na kaši.
            // Pod vodou i nad ní jsou sloupy v pořádku, problém je jen ten úzký pás mezi.
            float shore = smoothstep(0f, 4.5f, abs(y - seaLevel));

            float w = weight * fade * shore;
            if (w < 0.002f) return y;

            const float cellSize = 3.4f;   // šířka sloupu
            const float stepH = 2.3f;      // výška jedné odlučné lavice

            int2 home = (int2)floor(p / cellSize);
            float best = 1e9f;
            uint bestHash = 0u;

            for (int oz = -1; oz <= 1; oz++)
            for (int ox = -1; ox <= 1; ox++)
            {
                int2 c = home + new int2(ox, oz);
                uint h = GenNoise.Hash(c, seed ^ 0xBA5A17);
                float2 center = GenNoise.CellCenter(c, cellSize, seed ^ 0xBA5A17);

                float d = distancesq(p, center);
                if (d < best) { best = d; bestHash = h; }
            }

            // Každý sloup má vlastní posun v rámci lavice. Bez něj by všechny sloupy
            // skončily na téže úrovni a z pole by byla jedna rovná deska.
            float jitter = (GenNoise.Hash01(bestHash) - 0.5f) * 2f * stepH;
            float target = floor((y + jitter) / stepH) * stepH;

            return lerp(y, target, w);
        }

        /// <summary>
        /// Barevný zásah do palety. Vrací upravenou barvu; mimo mikro-biom se nic nemění.
        /// </summary>
        /// <param name="normalY">Svislá složka normály – plochý povrch 1, svislá stěna 0.</param>
        public static float3 Tint(float3 rgb, int kind, float weight, float normalY)
        {
            if (weight < 0.001f || kind == 0) return rgb;

            if (kind == (int)MicroBiome.GoldenGrove)
            {
                // Zlato se nepřebarvuje natvrdo, ale posouvá: skála i písek uvnitř háje
                // mají zůstat skálou a pískem, jen v teplejším světle. Tráva se posune
                // nejvíc, protože právě ona nese barvu místa.
                float3 amber = new float3(0.70f, 0.50f, 0.20f);
                float grassy = saturate(rgb.y * 1.6f - rgb.z * 0.9f);
                return lerp(rgb, lerp(rgb * new float3(1.14f, 0.99f, 0.70f), amber, grassy),
                            weight * 0.72f);
            }

            // Čedič: skoro černá s modrým nádechem, svislé stěny ještě tmavší – tam je
            // lom čerstvý. Bez rozlišení podle normály vyjde pole jako plochá šedá skvrna.
            float3 dark = new float3(0.125f, 0.126f, 0.134f);
            float3 face = new float3(0.078f, 0.079f, 0.088f);
            float3 basalt = lerp(face, dark, saturate(normalY));
            return lerp(rgb, basalt, weight * 0.92f);
        }
    }

    /// <summary>Kolo 29: jeden kráter s parametry z makro terénu (počítá se jednou na kráter / výřez, ne na vzorek).</summary>
    public struct CraterCell
    {
        public float2 center;
        public float radius;
        /// <summary>Referenční výška (makro terén ve středu a v okolí) – kráter se k ní vyrovná, takže je i na mírném svahu čitelný.</summary>
        public float refY;
        /// <summary>0–1: klima (jádro teplého/horkého pásma, sucho), výška (ne pobřeží, ne masivy) a reliéf okolí (ne svah kopce).</summary>
        public float gate;
        public int valid;
    }

    /// <summary>
    /// Kolo 29: meteorické krátery – vzácné kruhové impaktní zóny. Analytické Worley buňky 2,4 km, nejvýš jeden kráter v buňce,
    /// střed ve vnitřní polovině buňky: dva krátery jsou od sebe ≥ 1,2 km a dosah 2,47 R ≤ 335 m, takže výřez terénu (≤ 512 m)
    /// zasáhne nejvýš jeden. Parametry (CraterCell) se počítají jednou na výřez/sloupec z EvalMacro – tvar terénu (ColumnJobs
    /// krok 9b), váha biomu, barva, osazení i konzole čtou tytéž, takže se nerozejdou.
    /// <para><b>Profil</b> (vůči referenční výšce, x = d / R): ploché dno x &lt; 0,38 (−0,21 R), hladký val smoothstep do hřebene x = 1
    /// (+0,10 R), vně plynulé vyvrženiny do x = 2,3; mezi x = 1 a 2,3 se terén plynule vrací k původnímu. Profil je C1 – žádná hrana,
    /// pruh ani terasa; nejstrmější vnitřní stěna ≈ 37° (pochozí). Mimo x &gt; 2,35 je změna přesně 0.</para>
    /// </summary>
    public static class CraterMath
    {
        public const float Cell = 2400f, Chance = 0.9f, RMin = 90f, RMax = 135f;   // pilot 2: brána reliéfu/výšky propustí ~1 z 10 buněk → téměř každá buňka má kandidáta
        public const float DepthK = 0.21f, RimK = 0.10f, FloorX = 0.38f, ApronX = 2.3f, ReachX = 2.35f;

        private static uint Key(float3 off) => unchecked(asuint(off.x) * 0x9E3779B1u ^ asuint(off.z) * 0x85EBCA77u ^ 0x00C4A7E5u);

        /// <summary>Kráter buňky (střed, poloměr hřebene). false = buňka bez kráteru.</summary>
        public static bool CellCrater(int2 cell, float3 off, out float2 center, out float radius)
        {
            uint h = GenNoise.Hash(cell, (int)Key(off));
            center = 0f; radius = 0f;
            if (GenNoise.Hash01(h) >= Chance) return false;
            uint h2 = GenNoise.Hash(h ^ 0x27D4EB2Fu);
            center = ((float2)cell + 0.25f + 0.5f * new float2(GenNoise.Hash01(h2), GenNoise.Hash01(GenNoise.Hash(h2)))) * Cell;
            radius = lerp(RMin, RMax, GenNoise.Hash01(GenNoise.Hash(h2 ^ 0x165667B1u)));
            return true;
        }

        /// <summary>x = vzdálenost / R s mírným nekruhovým zvlněním valu (±6 %, 3. a 5. harmonická).</summary>
        public static float X(float2 p, float2 center, float radius)
        {
            float2 d = p - center;
            float dd = length(d);
            if (dd > radius * (ReachX + 0.15f)) return 99f;
            float ph = frac(center.x * 0.00123f + center.y * 0.00071f) * 6.2831853f;
            float a = atan2(d.y, d.x);
            float wob = 1f + 0.04f * sin(3f * a + ph) + 0.025f * sin(5f * a - 1.7f * ph);
            return dd / (radius * lerp(1f, wob, smoothstep(0.1f, 0.6f, dd / radius)));
        }

        /// <summary>x vůči kráteru CraterCell (99 = mimo / žádný).</summary>
        public static float X(float2 p, in CraterCell cc) => cc.valid != 0 ? X(p, cc.center, cc.radius) : 99f;

        /// <summary>Nejbližší kráter v dosahu (jen analytická poloha, bez terénu). false = mimo dosah.</summary>
        public static bool Near(float2 p, float3 off, out float2 center, out float radius, out float x)
        {
            x = 99f;
            if (!CellCrater((int2)floor(p / Cell), off, out center, out radius)) return false;
            x = X(p, center, radius);
            return x < ReachX;
        }

        /// <summary>Profil kráteru v metrech vůči referenční výšce (x = d / R).</summary>
        public static float Profile(float x, float radius)
        {
            float depth = DepthK * radius, rim = RimK * radius;
            if (x <= 1f) return -depth + (depth + rim) * smoothstep(FloorX, 1f, x);
            return rim * (1f - smoothstep(1f, ApronX, x));
        }

        /// <summary>Klimatická nika: jádro teplého a horkého pásma (t ≥ 0,55–0,60), suché až střední. Nezávisí na výšce.</summary>
        public static float Gate(in ClimateSample c) => smoothstep(0.55f, 0.60f, c.t) * saturate(c.dry + 0.7f * c.mid);

        /// <summary>Výšková brána (referenční výška nad mořem): dno ≥ ~8 m nad mořem (ne pobřeží), ne vysoké masivy.</summary>
        public static float HeightGate(float refY, float sea) => smoothstep(sea + 24f, sea + 36f, refY) * (1f - smoothstep(sea + 200f, sea + 260f, refY));

        /// <summary>Parametry kráteru buňky z makro terénu: referenční výška, brána klimatu, výšky a reliéfu okolí (9 × EvalMacro).</summary>
        public static CraterCell Info(int2 cell, in GenParams gp, in SplineSet spl)
        {
            CraterCell cc = default;
            if (!CellCrater(cell, gp.offMicro, out float2 c, out float r)) return cc;
            WorldGenMath.EvalMacro(c, gp, spl, out float m0, out _, out _, out _, out _);
            float mn = m0, mx = m0, sum = 0f;
            for (int i = 0; i < 8; i++)
            {
                float a = i * 0.78539816f, rr = (i % 2 == 0 ? 1.0f : 1.8f) * r;
                WorldGenMath.EvalMacro(c + new float2(cos(a), sin(a)) * rr, gp, spl, out float mi, out _, out _, out _, out _);
                mn = min(mn, mi); mx = max(mx, mi); sum += mi;
            }
            float refY = 0.5f * m0 + 0.5f * (sum / 8f);
            WorldGenMath.Climate(c, gp, out float t, out float h);
            ClimateSample cs = BiomeMath.Climate(c, refY, t, h, gp.seaLevel, gp.offMicro);
            cc.center = c; cc.radius = r; cc.refY = refY; cc.valid = 1;
            // reliéf: vyrovnaný kráter snese mírný svah (pilot 2: 30–55 m odmítlo 98 % buněk), ne svah kopce/masivu
            cc.gate = Gate(cs) * HeightGate(refY, gp.seaLevel) * (1f - smoothstep(50f, 85f, mx - mn));
            return cc;
        }

        /// <summary>Kráter pro bodový dotaz (rychle prázdný mimo dosah).</summary>
        public static CraterCell ForPoint(float2 p, in GenParams gp, in SplineSet spl)
        {
            int2 cell = (int2)floor(p / Cell);
            if (!CellCrater(cell, gp.offMicro, out float2 c, out float r) || distance(p, c) > r * (ReachX + 0.15f)) return default;
            return Info(cell, gp, spl);
        }

        /// <summary>Kráter, jehož dosah zasahuje do obdélníku (výřez terénu / sloupec osazení). Nejvýš jeden (viz rozestup).</summary>
        public static CraterCell ForBox(float2 mn, float2 mx, in GenParams gp, in SplineSet spl)
        {
            float reach = RMax * (ReachX + 0.15f);
            int2 c0 = (int2)floor((mn - reach) / Cell), c1 = (int2)floor((mx + reach) / Cell);
            for (int j = c0.y; j <= c1.y; j++)
            for (int i = c0.x; i <= c1.x; i++)
            {
                if (!CellCrater(new int2(i, j), gp.offMicro, out float2 c, out float r)) continue;
                float rr = r * (ReachX + 0.15f);
                float2 q = clamp(c, mn, mx);
                if (distancesq(q, c) > rr * rr) continue;
                return Info(new int2(i, j), gp, spl);
            }
            return default;
        }

        /// <summary>
        /// Kolo 29: tvar povrchu v kráteru (craterMode 2). Uvnitř valu se terén vyrovná k profilu kolem referenční výšky
        /// (zůstane jen 35 % jemného povrchového detailu – žádné terasy ani koryta), mezi valem a 2,3 R se plynule vrací
        /// k původnímu. shield = 0 u řeky/jezera (do vody se nesahá). Vrací váhu vyrovnání (pro potlačení teras v barvě).
        /// </summary>
        public static float Shape(ref float y, float2 p, float detail, in CraterCell cc, float shield)
        {
            if (cc.valid == 0 || cc.gate < 0.001f || shield < 0.001f) return 0f;
            float x = X(p, cc.center, cc.radius);
            if (x >= ReachX) return 0f;
            float bw = (1f - smoothstep(1f, ApronX, x)) * cc.gate * shield;
            float target = cc.refY + Profile(x, cc.radius) + 0.35f * detail;
            y = lerp(y, target, bw);
            return bw;
        }

        /// <summary>A/B varianta B1 (craterMode 1, v EvalMacro): relativní profil k místní výšce, klima v bodě. Jen pro pilotní měření.</summary>
        public static float TerrainDelta(float2 p, float yBase, float temp, float hum, float sea, float3 off, float shield)
        {
            if (shield < 0.001f || !Near(p, off, out _, out float r, out float x)) return 0f;
            float hg = smoothstep(sea + 14f, sea + 26f, yBase) * (1f - smoothstep(sea + 200f, sea + 260f, yBase));
            if (hg < 0.001f) return 0f;
            ClimateSample c = BiomeMath.Climate(p, yBase, temp, hum, sea, off);
            float g = Gate(c);
            if (g < 0.001f) return 0f;
            return Profile(x, r) * g * hg * shield;
        }
    }
}
