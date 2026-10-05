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
}
