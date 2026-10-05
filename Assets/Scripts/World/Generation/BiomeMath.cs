using Unity.Burst;
using Unity.Mathematics;
using static Unity.Mathematics.math;

namespace Orivilon.World.Generation
{
    /// <summary>
    /// Kolo 14: klimatické regiony světa. Doplňují čtyři „terénní" třídy kola 8 (louka, niva,
    /// hory, pobřeží), které dál rozhoduje výška nad vodou/mořem a sklon. Region říká, JAKÁ
    /// krajina to je (les, step, poušť…); třída říká, jestli je to břeh, pobřeží nebo hora.
    /// </summary>
    public enum BiomeRegion : byte
    {
        Meadow = 0,   // mírná louka s háji (dosavadní stav)
        Boreal = 1,   // jehličnatý les
        Birch = 2,    // březový / podzimní háj
        Steppe = 3,   // suchá step a savana
        Desert = 4,   // poušť
        Mesa = 5,     // mesy a badlands (silné terasy v suchu)
        Swamp = 6,    // mokřad v nížinách u vody
        Tundra = 7,   // chladná suchá tundra (a výškové pásmo pod sněhem)
        // kolo 16 – druhý balík (bujné mírné a teplé krajiny)
        BlackForest = 8,  // černý les: hustý tmavý jehličnatý les (mírné, chladnější a vlhké)
        Sequoia = 9,      // obří sekvojový les (mírné, velmi vlhké, vzácné)
        Flowers = 10,     // kvetoucí louky (mírné, nížina)
        Heath = 11,       // větrné vřesoviště (mírné, sušší kopce)
        Sakura = 12,      // sakurové údolí (přechod mírné → teplé, chráněné údolí)
        Bamboo = 13,      // bambusové údolí (teplé, vlhké, svahy)
        Jungle = 14,      // tropická džungle (teplé, velmi vlhké)
        FernGorge = 15,   // pravěká kapradinová rokle (vlhké údolí řeky nad nížinou)
        // kolo 19 – třetí balík (teplé a geologické krajiny); jen horké a teplé pásmo
        Volcanic = 16,    // vulkanická oblast: černá lávová skála, popel, tmavá suť (horké suché vysočiny)
        Burnt = 17,       // spálený les: ohořelé kmeny a pařezy na popelu (teplé suché pahorkatiny)
        Karst = 18,       // kras: vápencové věže, jehly a portály (teplé vlhčí kopce)
        Petrified = 19,   // zkamenělý les: kamenné kmeny, achátové řezy, minerály (horké ploché nížiny)
        Ruins = 20,       // zarostlé ruiny: vzácné ostrůvky zdí, sloupů a oblouků (teplé pásmo, rovina)
        Oasis = 21,       // oáza: palmy, keře, rákos – jen u existující vody v horkém pásmu
        // kolo 20 – čtvrtý balík (chladné, pobřežní a geotermální krajiny); jen na konec
        SnowPeaks = 22,   // zasněžené štíty: sněhové a ledové hřebeny, suť, níže řídké jehličnany (chladné pásmo, vysoko)
        Glacier = 23,     // ledovcové údolí: led, morény, ledové stěny, řídké smrky (hluboko v chladném pásmu)
        FrozenOcean = 24, // zamrzlý oceán: kry nad existujícím mořem, závěje na nízkém břehu (hluboko v chladném pásmu)
        Mangrove = 25,    // mangrovy: bahnité laguny a mělčiny nízkého pobřeží (teplé/horké vlhké pásmo)
        SaltFlat = 26,    // solné pláně: popraskaná solná krusta v horkých velmi suchých plochých nížinách
        Geothermal = 27,  // geotermální pole: travertinové terasy, barevné krusty, sirné výduchy (horké pásmo, okraj vulkánu)
        // kolo 21 – pátý balík; jen na konec
        Crystal = 28,     // krystalová oblast: fialové/modré krystaly, minerální stěny, jeskynní brány (jádro mírného pásma, pahorkatiny)
        Mushroom = 29,    // houbový les: obří barevné houby, svítící drobné houby, výtrusy (jádro mírného pásma, vlhko)
        Cliffs = 30,      // útesové pobřeží: křídové/čedičové útesy, pilíře, brány, pláž (mírné a teplé pobřeží, ne vlhké tropy)
        // kolo 28 – šestý balík (geologické); jen na konec
        BasaltCoast = 31,      // čedičové pobřeží: šestiboké čedičové sloupy, černý písek, sloupy i v mělčině (chladnější mírné pobřeží)
        ObsidianPlain = 32,    // obsidiánová pláň: leskle černé ostré formace, pukliny, skoro bez porostu (jádro horkého pásma, suché roviny)
        AlabasterPlateau = 33, // alabastrové plato: světlé erodované plato, hladké formace, oblouky, bílý prach (suché jádro teplého pásma)
    }

    /// <summary>Spojité váhy regionů v bodě, součet 1.</summary>
    public struct RegionWeights
    {
        public float meadow, boreal, birch, steppe, desert, mesa, swamp, tundra;
        public float blackForest, sequoia, flowers, heath, sakura, bamboo, jungle, fernGorge;   // kolo 16
        public float volcanic, burnt, karst, petrified, ruins, oasis;                          // kolo 19
        public float snowPeaks, glacier, frozenOcean, mangrove, saltFlat, geothermal;          // kolo 20
        public float crystal, mushroom, cliffs;                                                // kolo 21
        public float basalt, obsidian, alabaster;                                              // kolo 28

        public float Get(int i)
        {
            switch (i)
            {
                case 0: return meadow; case 1: return boreal; case 2: return birch; case 3: return steppe;
                case 4: return desert; case 5: return mesa; case 6: return swamp; case 7: return tundra;
                case 8: return blackForest; case 9: return sequoia; case 10: return flowers; case 11: return heath;
                case 12: return sakura; case 13: return bamboo; case 14: return jungle; case 15: return fernGorge;
                case 16: return volcanic; case 17: return burnt; case 18: return karst; case 19: return petrified;
                case 20: return ruins; case 21: return oasis;
                case 22: return snowPeaks; case 23: return glacier; case 24: return frozenOcean; case 25: return mangrove;
                case 26: return saltFlat; case 27: return geothermal;
                case 28: return crystal; case 29: return mushroom; case 30: return cliffs;
                case 31: return basalt; case 32: return obsidian; case 33: return alabaster; default: return 0f;
            }
        }

        /// <summary>Region vybraný rozptýleně podle vah (u = 0–1). Na hranici se celky prolínají po kusech.</summary>
        public int Pick(float u)
        {
            float acc = 0f;
            for (int i = 0; i < BiomeMath.Count; i++)
            {
                acc += Get(i);
                if (u < acc) return i;
            }
            return 0;
        }

        /// <summary>
        /// Jako <see cref="Pick"/>, ale s váhami na třetí (normalizovanými): uvnitř oblasti
        /// (≥ 65 %) vyhraje převládající region v ~85 % los, na hranici 50/50 zůstává 50/50.
        /// Hustý region (louka s háji) tak nepřebije řídký (mokřad, step) už v jeho jádru.
        /// </summary>
        public int PickSharp(float u)
        {
            float sum = 0f;
            for (int i = 0; i < BiomeMath.Count; i++) { float w = Get(i); sum += w * w * w; }
            if (sum < 1e-6f) return Dominant();
            float acc = 0f, target = u * sum;
            for (int i = 0; i < BiomeMath.Count; i++)
            {
                float w = Get(i); acc += w * w * w;
                if (target < acc) return i;
            }
            return Dominant();
        }

        public int Dominant()
        {
            int best = 0; float bw = meadow;
            for (int i = 1; i < BiomeMath.Count; i++) { float w = Get(i); if (w > bw) { bw = w; best = i; } }
            return best;
        }
    }

    /// <summary>Klimatické pásmo katalogu biomů (kolo 14b). Horké a chladné nikdy nesousedí.</summary>
    public enum ClimateBand : byte { Hot = 0, Warm = 1, Mild = 2, Cold = 3, Underground = 4, Coastal = 5 }

    /// <summary>Klima v bodě: posunutá teplota/vlhkost a podíly pásem (součty 1).</summary>
    public struct ClimateSample
    {
        public float t, h, above, cooling, baseT;
        public float hot, warm, mild, cold;
        public float dry, mid, wet;

        public ClimateBand Band => hot >= warm && hot >= mild && hot >= cold ? ClimateBand.Hot
                                 : warm >= mild && warm >= cold ? ClimateBand.Warm
                                 : mild >= cold ? ClimateBand.Mild : ClimateBand.Cold;
    }

    /// <summary>
    /// Kolo 14: čisté funkce regionů – stejné volání používá barvení terénu (job, Burst) i osazovač,
    /// takže se barva půdy a porost nikdy nerozejdou.
    ///
    /// <para><b>Klima terénu se nemění.</b> Teplota a vlhkost z <see cref="WorldGenMath.Climate"/>
    /// (perioda 8–11 km) tvarují reliéf – terasy v suchu, bažinné plošiny, kaňony. Ty zůstávají
    /// bitově stejné, takže voda, hydrologie, LOD švy a opravy pruhů se nedotknou. Regiony
    /// jen čtou toto makro klima a přidávají k němu regionální posun (perioda 2,6 km) a
    /// výškový gradient teploty – z jednoho klimatického pásma tak vznikne několik čitelných
    /// oblastí místo jedné pětikilometrové.</para>
    ///
    /// <para><b>Mesy a bažiny se řídí geometrií:</b> mesa je tam, kde terén skutečně terasuje
    /// v suchu (suchost terénu bez posunu × výška nad mořem × síla terasy), mokřad tam, kde se
    /// vlhká nížina srovnala k vodě (stejný vzorec jako krok 7b v EvalSurface) nebo leží těsně
    /// nad hladinou řeky. Barevné pruhy mes proto nikdy nevzniknou na běžném svahu.</para>
    /// </summary>
    public static class BiomeMath
    {
        public const int Count = 34;

        /// <summary>Síla regionálního posunu teploty a vlhkosti (kolo 14b: menší a delší vlna → širší pásma).</summary>
        public const float ShiftT = 0.04f, ShiftH = 0.15f;

        /// <summary>Perioda regionálního posunu klimatu (m).</summary>
        public const float RegionalPeriod = 3400f;

        /// <summary>
        /// Kolo 14b: vlastní hladké teplotní pole biomů (perioda 15 km). Teplota terénu
        /// (<see cref="WorldGenMath.Climate"/>) má místy strmé přechody – samotná dala mezi horkým
        /// a chladným pásmem jen ~0,8–1,2 km. Biomová teplota je proto z 30 % terénní a ze 70 %
        /// tohoto pole; na 3 seedech to dalo nejmenší odstup horké↔chladné 1,5–2 km.
        /// Terén (reliéf, voda) se nemění – je to jen vstup barvy a porostu.
        /// </summary>
        public const float BandPeriod = 15000f, BandAmp = 0.45f, BandOwn = 0.7f;

        /// <summary>Pokles teploty na metr nad 60 m n. m. (hory jsou chladnější → jehličiny a tundra).</summary>
        public const float Lapse = 0.0009f;

        /// <summary>
        /// Strop výškového ochlazení. Platí plně jen v chladném a mírném základu (t &lt; 0,44) a do
        /// 0,52 mizí: hora v teplém či horkém pásmu se výškou neochladí do chladného pásma,
        /// takže pod pouští nikdy nevznikne tundra. Sníh na štítech dál kreslí paleta
        /// (sněžná čára terénu), to není klimatický region.
        /// </summary>
        public const float LapseMax = 0.09f;

        /// <summary>
        /// Kolo 20 – systémová oprava klimatu: sněžná čára palety stoupá s BIOMOVOU teplotou.
        /// Dřív ji určovala jen teplota terénu, takže i hora v teplém či horkém pásmu měla sněhový
        /// štít (k19: vulkán 64 m od sněhu). Teď se nad t ≈ 0,44 čára plynule zvedá a od t ≈ 0,52
        /// (teplé pásmo) leží o 900 m výš – nad každým vrcholem. Pod ní zůstává skalní a sutinové
        /// pásmo (rockTop, scree), takže přechod je přirozený: zasněžený štít v mírném pásmu → holá
        /// skála a suť → teplé a horké biomy. Tvar terénu se nemění, jen barva a klasifikace sněhu.
        /// </summary>
        public const float SnowLiftLo = 0.44f, SnowLiftHi = 0.52f, SnowLiftMax = 900f;

        /// <summary>Kolo 20: o kolik metrů se v bodě zvedá sněžná čára (biomová teplota c.t).</summary>
        public static float SnowLift(float biomeT) => SnowLiftMax * smoothstep(SnowLiftLo, SnowLiftHi, biomeT);

        /// <summary>
        /// Kolo 20: referenční sněžná čára herní palety (TerrainPalette: snowTop 300, snowTempShift 110) – jen
        /// pro masky regionů (štíty, ledovec); barvu sněhu dál kreslí paleta se svým šumem.
        /// </summary>
        public const float RefSnowTop = 300f, RefSnowShift = 110f;

        // Prahy klimatických pásem (t = biomová teplota 0–1). Rampy se nepřekrývají, takže
        // mezi horkým a chladným pásmem vždy leží teplé a mírné (Δt ≥ 0,18).
        public const float HotLo = 0.60f, HotHi = 0.68f, WarmLo = 0.50f, WarmHi = 0.58f, ColdLo = 0.34f, ColdHi = 0.42f;

        /// <summary>
        /// Kolo 14b: spojité deterministické makro klima v bodě. Jediný zdroj pravdy pro
        /// barvu terénu, osazování, mapy i konzoli (/biome kde). Budoucí biomy z katalogu
        /// (<see cref="BiomeCatalog"/>) dostanou váhu z téhož vzorku – jen přes své pásmo.
        /// </summary>
        public static ClimateSample Climate(float2 xz, float y, float temp, float hum, float sea, float3 off)
        {
            float2 o = off.xz;
            float n1 = GenNoise.Fbm2(xz * (1f / RegionalPeriod), 2, o + new float2(91.7f, -13.3f));
            float n2 = GenNoise.Fbm2(xz * (1f / RegionalPeriod), 2, o + new float2(-57.1f, 44.9f));
            float nw = GenNoise.Fbm2(xz * (1f / 380f), 2, o + new float2(-11.3f, -92.5f));

            float own = 0.5f + BandAmp * GenNoise.Fbm2(xz * (1f / BandPeriod), 2, o + new float2(-37.7f, 18.1f));

            ClimateSample c;
            c.above = max(0f, y - sea);
            c.baseT = lerp(temp, own, BandOwn) + ShiftT * n1;
            float cap = LapseMax * (1f - smoothstep(0.44f, 0.52f, c.baseT));
            c.cooling = min(Lapse * max(0f, c.above - 60f), cap);
            c.t = saturate(c.baseT - c.cooling);
            c.h = saturate(hum + ShiftH * n2 - 0.03f * nw);

            float warmUp = smoothstep(WarmLo, WarmHi, c.t);
            c.hot = smoothstep(HotLo, HotHi, c.t);
            c.warm = saturate(warmUp - c.hot);
            c.cold = 1f - smoothstep(ColdLo, ColdHi, c.t);
            c.mild = saturate(1f - warmUp - c.cold);

            c.dry = 1f - smoothstep(0.38f, 0.48f, c.h);
            c.wet = smoothstep(0.55f, 0.65f, c.h);
            c.mid = saturate(1f - c.dry - c.wet);
            return c;
        }

        public static RegionWeights Weights(float2 xz, float y, float temp, float hum, float cliff,
                                            float hW, bool hasWater, float sea, float3 off)
            => WeightsC(xz, y, temp, hum, cliff, hW, hasWater, sea, off, out _);

        /// <summary>Kolo 20: váhy i s klimatickým vzorkem (barva terénu potřebuje c.t pro sněžnou čáru).</summary>
        public static RegionWeights WeightsC(float2 xz, float y, float temp, float hum, float cliff,
                                             float hW, bool hasWater, float sea, float3 off, out ClimateSample c)
        {
            c = Climate(xz, y, temp, hum, sea, off);
            float n3 = GenNoise.Fbm2(xz * (1f / 1700f), 2, off.xz + new float2(23.9f, 77.1f));
            float above = c.above;

            // ── geometrií řízené regiony (neposunuté klima terénu) ──
            // Mesa jen v teplém a horkém pásmu – terasy terénu tvaruje suchost, ne teplota.
            float aridT = WorldGenMath.Aridity(temp, hum);
            float mesa = smoothstep(0.30f, 0.60f, aridT) * smoothstep(12f, 35f, above)
                       * saturate(0.45f + 0.55f * smoothstep(0.15f, 0.45f, cliff) + 0.35f * n3)
                       * smoothstep(0.58f, 0.66f, c.t);
            mesa = saturate(mesa);

            float wetT = WorldGenMath.Wetness(hum);
            float swampGeo = wetT * saturate((temp - 0.50f) * 2.5f) * (1f - smoothstep(5f, 28f, above));
            // Kolo 15: mokřad sahá dál od vody (do 12 m nad hladinou) a jen na rovině – klidné okraje
            // nížin, ne svahy nad řekou. Na terase (cliff) ani ve svahu nevzniká.
            float nearWater = hasWater ? 1f - smoothstep(4f, 12f, hW) : 0f;
            nearWater *= 1f - smoothstep(0.10f, 0.30f, cliff);
            float wetS = smoothstep(0.50f, 0.62f, c.h);   // u vody stačí o něco menší vlhkost než pro vlhký region
            // Geometrický mokřad (srovnaná vlhká nížina) jen ve vlhkém biomovém klimatu – jinak by
            // u řeky ve stepi vznikl „mokřad" se suchou trávou.
            float lowland = 1f - smoothstep(14f, 30f, above);
            float swamp = saturate(max(swampGeo * 1.25f * smoothstep(0.42f, 0.55f, c.h), wetS * nearWater * lowland * 1.1f))
                        * (1f - c.cold) * (1f - c.hot) * (1f - mesa);

            // ── klimatické regiony podle pásem ──
            // horké: poušť (sucho), savana/step (jinak) · teplé: step (sucho, střed), louka/háj (vlhko)
            // mírné: louka, březový háj, mokřad · chladné: jehličnatý les, tundra (sucho)
            float rest = (1f - mesa) * (1f - swamp);
            float desert = c.hot * c.dry * rest;
            float steppe = (c.hot * (c.mid + c.wet) + c.warm * (c.dry + c.mid)) * rest;
            float boreal = c.cold * (c.mid + c.wet) * rest;
            float tundra = c.cold * c.dry * rest;
            float birchMask = smoothstep(0.05f, 0.30f, n3);
            float lush = (c.warm * c.wet + c.mild) * rest;

            // ── kolo 16: druhý balík dělí bujné pásmo (mírné + teplé vlhké) ──
            // Každý biom = podíl pásma × vlhkost × tvar terénu × velká maska (2,3 km / 1,5 km).
            // Pásma se nemění, takže klimatická osa horké → teplé → mírné → chladné drží.
            float nA = GenNoise.Fbm2(xz * (1f / 2300f), 2, off.xz + new float2(-71.3f, 12.7f));
            float nB = GenNoise.Fbm2(xz * (1f / 1500f), 2, off.xz + new float2(33.1f, -58.9f));
            float mt = smoothstep(ColdHi, WarmLo, c.t);                     // 0 = chladnější mírné, 1 = teplejší
            float sqMask = smoothstep(0.30f, 0.48f, nB);
            float sequoia = c.mild * smoothstep(0.54f, 0.66f, c.h) * sqMask * (1f - smoothstep(60f, 140f, above)) * smoothstep(12f, 22f, above);
            float blackForest = c.mild * (1f - mt) * smoothstep(0.48f, 0.60f, c.h) * smoothstep(-0.05f, 0.20f, nA) * (1f - 0.85f * sqMask) * smoothstep(12f, 22f, above);
            float heath = c.mild * (c.dry + 0.7f * c.mid) * smoothstep(22f, 50f, above) * smoothstep(0.05f, 0.30f, -nA);
            float flowers = c.mild * c.mid * (1f - smoothstep(25f, 55f, above)) * smoothstep(0.15f, 0.35f, -nB);
            float gorge = hasWater ? (1f - smoothstep(8f, 18f, hW)) * smoothstep(18f, 30f, above) : 0f;   // dno vnitrozemského říčního údolí
            float fernGorge = saturate((c.mild + c.warm * c.wet) * smoothstep(0.54f, 0.64f, c.h) * gorge * 2.5f);
            float sakT = smoothstep(0.46f, 0.51f, c.t) * (1f - smoothstep(0.55f, 0.60f, c.t));
            float sakura = sakT * (c.mid + c.wet) * (1f - smoothstep(50f, 90f, above)) * smoothstep(10f, 20f, above) * smoothstep(0.10f, 0.30f, -n3);
            float hill = smoothstep(14f, 36f, above) * smoothstep(-0.05f, 0.20f, nA);
            float bamboo = c.warm * smoothstep(0.55f, 0.65f, c.h) * hill;
            // Stromy rostou až od 14 m nad mořem – džungle i černý les proto začínají nad pobřežní nížinou.
            float jungle = c.warm * smoothstep(0.58f, 0.68f, c.h) * (1f - 0.85f * hill) * smoothstep(12f, 22f, above);
            // Rokle (údolí řeky hluboko pod okolím) má přednost před ostatními bujnými biomy, jinak
            // by se v úzkém pásu u vody rozpadla do mozaiky se sousedy.
            float og = 1f - 0.9f * fernGorge;
            sequoia *= og; blackForest *= og; heath *= og; flowers *= og; sakura *= og; bamboo *= og; jungle *= og;
            float S = sequoia + blackForest + heath + flowers + fernGorge + sakura + bamboo + jungle;
            float k16 = S > lush && S > 1e-5f ? lush / S : 1f;
            sequoia *= k16; blackForest *= k16; heath *= k16; flowers *= k16; fernGorge *= k16; sakura *= k16; bamboo *= k16; jungle *= k16;
            float lushLeft = max(0f, lush - S * k16);
            float birch = lushLeft * birchMask;
            float meadow = lushLeft * (1f - birchMask);

            // ── kolo 19: třetí balík – teplé a geologické krajiny ──
            // Každý biom má vlastní niku v reliéfu (vysočina / pahorkatina / kopce / plochá nížina / rovina /
            // dno u vody) a velkou masku. Váha existuje jen v horkém a teplém pásmu (c.hot + c.warm > 0, tj.
            // t > WarmLo), takže od chladného pásma je vždy dělí celé mírné pásmo. Nové biomy si berou podíl
            // K ≤ (horké + teplé); ostatní regiony se násobí (1 − K). Kde jsou všechny nulové (mírné a chladné
            // pásmo, mimo masky), zůstávají váhy kola 16 bitově stejné. Terén (tvar, voda) se nemění.
            float volcanic = 0f, burnt = 0f, karst = 0f, petrified = 0f, ruins = 0f, oasis = 0f, K19 = 0f;
            float mangrove = 0f, saltFlat = 0f, geothermal = 0f;   // kolo 20 (horké/teplé – sdílí podíl K19)
            float hw = c.hot + c.warm;
            if (hw > 1e-4f)
            {
                float2 o19 = off.xz;
                float nV = GenNoise.Fbm2(xz * (1f / 2600f), 2, o19 + new float2(48.3f, -27.9f));
                float nK = GenNoise.Fbm2(xz * (1f / 2100f), 2, o19 + new float2(-19.7f, 63.1f));
                float nP = GenNoise.Fbm2(xz * (1f / 1900f), 2, o19 + new float2(81.9f, 5.3f));
                float nR = GenNoise.Fbm2(xz * (1f / 900f), 2, o19 + new float2(-44.4f, -88.8f));
                float flat = 1f - smoothstep(0.15f, 0.35f, cliff);
                // vulkán: horké suché vysočiny (od 30–60 m nad mořem výš), mesy si nechávají své terasy
                volcanic = c.hot * (c.dry + 0.6f * c.mid) * smoothstep(30f, 60f, above) * smoothstep(0.10f, 0.28f, nV) * (1f - mesa);
                // spálený les: teplé suché/střední pahorkatiny 20–120 m (opačná strana masky vulkánu)
                burnt = c.warm * (c.dry + c.mid) * smoothstep(18f, 30f, above) * (1f - smoothstep(80f, 120f, above)) * smoothstep(0.10f, 0.28f, -nV);
                // kras: teplé střední až vlhké roviny a nízké pahorky 14–110 m – věže z propů rostou z plochého dna (Guilin)
                karst = c.warm * (c.mid + 0.7f * c.wet) * smoothstep(14f, 22f, above) * (1f - smoothstep(70f, 110f, above))
                      * (1f - 0.7f * smoothstep(0.15f, 0.35f, cliff)) * smoothstep(0.12f, 0.30f, nK);
                // zkamenělý les: horké suché ploché nížiny 12–75 m, bez teras
                petrified = c.hot * (c.dry + 0.5f * c.mid) * smoothstep(12f, 20f, above) * (1f - smoothstep(45f, 75f, above))
                          * smoothstep(0.10f, 0.28f, nP) * flat * (1f - mesa);
                // ruiny: vzácné ostrůvky (~0,4 km) na rovině teplého pásma
                // ruiny: vzácné ostrůvky (~0,5 km) na rovině teplého pásma; v jádru mají přednost (jinak by se
                // rozpadly do mozaiky se sousedy jako dřív rokle)
                float rm = smoothstep(0.36f, 0.46f, nR) * flat;
                ruins = saturate(1.6f * (c.warm + 0.5f * c.hot) * (c.mid + c.wet + 0.4f * c.dry) * smoothstep(16f, 26f, above) * (1f - smoothstep(90f, 140f, above)) * rm);
                float orm = 1f - 0.85f * ruins;
                volcanic *= orm; burnt *= orm; karst *= orm; petrified *= orm;
                // oáza: jen u existující vody (stejný odhad výšky nad hladinou jako mokřad), ne mořské pobřeží
                float nearW = hasWater ? 1f - smoothstep(24f, 40f, hW) : 0f;   // pás nad břehem, kam smějí palmy (≥ 10 m nad hladinou)
                oasis = saturate(2.5f * c.hot * (c.dry + 0.6f * c.mid) * nearW * smoothstep(14f, 20f, above) * flat) * (1f - 0.5f * mesa);   // od 14 m n. m. (pravidlo stromů) – palmy
                // Odstup od zamrzlých míst: teplé biomy jen hlouběji v teplém pásmu (t ≥ 0,55–0,60, od chladného
                // pásma Δt ≥ 0,13), všechny pod výškovým stropem – sníh na štítech kreslí paleta nad ~300 m.
                float deepWarm = smoothstep(0.55f, 0.60f, c.t);
                float lowAlt = 1f - smoothstep(120f, 165f, above);
                burnt *= deepWarm * lowAlt; karst *= deepWarm * lowAlt; ruins *= deepWarm * lowAlt;
                volcanic *= lowAlt; petrified *= lowAlt; oasis *= lowAlt;

                // ── kolo 20: pobřežní a geotermální biomy horkého/teplého pásma ──
                // Terén ani voda se nemění: mangrovy a solné pláně čtou jen výšku nad/pod hladinou moře
                // (stejně v barvě, osazení i hledání), geotermální pole leží na okraji masky vulkánu.
                float depth = max(0f, sea - y);
                float flat20 = 1f - smoothstep(0.12f, 0.30f, cliff);
                float nMg = GenNoise.Fbm2(xz * (1f / 1600f), 2, o19 + new float2(-61.3f, 17.9f));
                float nSl = GenNoise.Fbm2(xz * (1f / 2200f), 2, o19 + new float2(12.7f, -66.1f));
                float nGt = GenNoise.Fbm2(xz * (1f / 1400f), 2, o19 + new float2(5.5f, 31.7f));
                // mangrovy: nízký břeh (≤ 8 m n. m.) a mělčina (dno ≤ 5 m pod hladinou) vlhkého teplého/horkého pásma
                mangrove = (c.warm + 0.8f * c.hot) * smoothstep(0.55f, 0.65f, c.h) * (1f - smoothstep(4f, 8f, above))
                         * (1f - smoothstep(2.5f, 5f, depth)) * flat20 * smoothstep(-0.20f, 0.05f, nMg) * deepWarm;
                // solné pláně: horké, velmi suché, ploché nížiny 6–85 m bez teras
                saltFlat = c.hot * (1f - smoothstep(0.28f, 0.40f, c.h)) * smoothstep(6f, 12f, above) * (1f - smoothstep(55f, 85f, above))
                         * flat20 * smoothstep(0.05f, 0.22f, nSl) * (1f - mesa);
                // geotermální pole: horké pásmo na okraji masky vulkánu (nV těsně pod jádrem vulkánu), 14–150 m
                float rim = smoothstep(-0.10f, 0.02f, nV) * (1f - smoothstep(0.24f, 0.34f, nV));
                geothermal = c.hot * (c.mid + 0.6f * c.dry + 0.6f * c.wet) * rim * smoothstep(-0.02f, 0.14f, nGt)
                           * smoothstep(14f, 22f, above) * (1f - smoothstep(110f, 150f, above)) * (1f - 0.6f * smoothstep(0.15f, 0.35f, cliff)) * (1f - mesa);
                oasis *= 1f - saturate(2f * geothermal);   // u pramenů geotermálního pole není oáza (prameny mají přednost)
                // Jen v jádru horkého pásma: na okraji by nový horký biom převládl nad teplými a horké pásmo
                // (podle převládajícího regionu) by se přiblížilo chladnému – odstup horké↔chladné se nesmí zhoršit.
                float hotCore = smoothstep(0.55f, 0.85f, c.hot);
                saltFlat *= hotCore;
                // geotermál je v horkém pásmu se střední vlhkostí, kde dřív převládala step (teplé pásmo) – jen hluboko
                // v horkém pásmu (t ≥ 0,66–0,72), aby horké pásmo nepřirostlo k chladnému (s31415: 1717 → 1333 m bez toho)
                geothermal *= hotCore * smoothstep(0.66f, 0.72f, c.t);
                float s19 = volcanic + burnt + karst + petrified + ruins + oasis + mangrove + saltFlat + geothermal;
                if (s19 > 1e-5f)
                {
                    K19 = min(s19, hw);
                    float k19 = K19 / s19;
                    volcanic *= k19; burnt *= k19; karst *= k19; petrified *= k19; ruins *= k19; oasis *= k19;
                    mangrove *= k19; saltFlat *= k19; geothermal *= k19;
                    float keep = 1f - K19;
                    meadow *= keep; boreal *= keep; birch *= keep; steppe *= keep; desert *= keep; mesa *= keep; swamp *= keep; tundra *= keep;
                    sequoia *= keep; blackForest *= keep; heath *= keep; flowers *= keep; fernGorge *= keep; sakura *= keep; bamboo *= keep; jungle *= keep;
                }
            }

            // ── kolo 20: chladné biomy (jen v chladném pásmu – od teplého je dělí celé mírné pásmo) ──
            // Podíl K20 ≤ c.cold, ostatní regiony × (1 − K20). Kde jsou nulové, zůstávají váhy kola 19 bitově stejné.
            float snowPeaks = 0f, glacier = 0f, frozenOcean = 0f;
            if (c.cold > 1e-4f)
            {
                float2 o20 = off.xz;
                float refSnow = RefSnowTop + RefSnowShift * (saturate(temp) - 0.5f) * 2f;   // sněžná čára palety (bez šumu)
                float nPk = GenNoise.Fbm2(xz * (1f / 1800f), 2, o20 + new float2(27.3f, 91.1f));
                float nGl = GenNoise.Fbm2(xz * (1f / 2400f), 2, o20 + new float2(-83.7f, -21.9f));
                float deepCold = 1f - smoothstep(0.30f, 0.38f, c.t);
                // štíty: vysoké polohy chladného pásma od ~150 m pod sněžnou čarou (níž řídké jehličnany)
                // (jen hlouběji v chladném pásmu, t < 0,36–0,41 – od teplého pásma je dělí i okraj chladného)
                snowPeaks = c.cold * (1f - smoothstep(0.36f, 0.41f, c.t)) * smoothstep(refSnow - 150f, refSnow - 90f, y) * smoothstep(-0.30f, -0.10f, nPk);
                // ledovec: údolí 25 m nad mořem až pod štíty, hluboko v chladném pásmu, velká maska
                glacier = c.cold * deepCold * smoothstep(25f, 45f, above) * (1f - smoothstep(refSnow - 110f, refSnow - 50f, y))
                        * smoothstep(0.06f, 0.22f, nGl) * (1f - 0.5f * smoothstep(0.20f, 0.40f, cliff));
                // zamrzlý oceán: moře (dno pod hladinou) a nízký břeh ≤ 6 m, hluboko v chladném pásmu
                // (na břehu ×2: úzký pás nízkého břehu má převahu – závěje, ne les až k vodě)
                frozenOcean = 2f * c.cold * (1f - smoothstep(0.32f, 0.40f, c.t)) * (1f - smoothstep(5f, 9f, above));
                // jen v jádru chladného pásma – jinak by chladné pásmo (podle převládajícího regionu) přerostlo do mírného
                float coldCore = smoothstep(0.55f, 0.85f, c.cold);
                snowPeaks *= coldCore; glacier *= coldCore; frozenOcean *= coldCore;
                float s20 = snowPeaks + glacier + frozenOcean;
                if (s20 > 1e-5f)
                {
                    float K20 = min(s20, c.cold);
                    float k20 = K20 / s20;
                    snowPeaks *= k20; glacier *= k20; frozenOcean *= k20;
                    float keep = 1f - K20;
                    meadow *= keep; boreal *= keep; birch *= keep; steppe *= keep; desert *= keep; mesa *= keep; swamp *= keep; tundra *= keep;
                    sequoia *= keep; blackForest *= keep; heath *= keep; flowers *= keep; fernGorge *= keep; sakura *= keep; bamboo *= keep; jungle *= keep;
                    volcanic *= keep; burnt *= keep; karst *= keep; petrified *= keep; ruins *= keep; oasis *= keep;
                    mangrove *= keep; saltFlat *= keep; geothermal *= keep;
                }
            }

            // ── kolo 21: krystaly a houby (jádro mírného pásma), útesové pobřeží (mírné a teplé, ne vlhké tropy ani horké/chladné) ──
            // Podíl K21 ≤ mírné + teplé, ostatní regiony × (1 − K21). Kde jsou nulové, zůstávají váhy kola 20 bitově stejné.
            // Horké ani chladné pásmo se nemění (krystaly/houby jen v jádru mírného, útesy × (1 − horké) a bez chladného),
            // takže odstup horké ↔ chladné zůstává.
            float crystal = 0f, mushroom = 0f, cliffs = 0f;
            float mw21 = c.mild + c.warm;
            if (mw21 > 1e-4f)
            {
                float2 o21 = off.xz;
                float nCr = GenNoise.Fbm2(xz * (1f / 2000f), 2, o21 + new float2(71.9f, -38.3f));
                float nMu = GenNoise.Fbm2(xz * (1f / 1600f), 2, o21 + new float2(-26.1f, 54.7f));
                float nCf = GenNoise.Fbm2(xz * (1f / 1800f), 2, o21 + new float2(39.5f, 88.1f));
                float mildCore = smoothstep(0.55f, 0.85f, c.mild);
                // krystaly: skalnaté pahorkatiny 35–220 m n. m., velká maska; terasy (cliff) je posilují
                // (×1,6 jako útesy: v jádru masky musí převládnout i na rovině, jinak by se rozpadly do mozaiky se sousedy)
                crystal = 1.6f * c.mild * mildCore * (c.mid + 0.8f * c.wet + 0.5f * c.dry) * smoothstep(35f, 60f, above) * (1f - smoothstep(170f, 220f, above))
                        * smoothstep(0.14f, 0.30f, nCr) * (0.85f + 0.15f * smoothstep(0.10f, 0.35f, cliff));
                // houby: vlhké lesní nížiny a pahorky 14–115 m (stromy až od 14 m), vzácnější maska, ne terasy
                mushroom = c.mild * mildCore * smoothstep(0.56f, 0.66f, c.h) * smoothstep(14f, 22f, above) * (1f - smoothstep(80f, 115f, above))
                         * smoothstep(0.12f, 0.28f, nMu) * (1f - 0.7f * smoothstep(0.15f, 0.35f, cliff));
                // útesy: nízký břeh ≤ 9–14 m n. m. a mělčina ≤ 4–7 m (oceán se nemění – jen barva, osazení a hledání)
                float depth21 = max(0f, sea - y);
                cliffs = 1.6f * (c.mild + c.warm * (1f - c.wet)) * (1f - c.hot) * (1f - smoothstep(9f, 14f, above)) * (1f - smoothstep(4f, 7f, depth21))
                       * smoothstep(-0.12f, 0.08f, nCf) * (1f - mesa);
                float s21 = crystal + mushroom + cliffs;
                if (s21 > 1e-5f)
                {
                    float K21 = min(s21, mw21);
                    float k21 = K21 / s21;
                    crystal *= k21; mushroom *= k21; cliffs *= k21;
                    float keep = 1f - K21;
                    meadow *= keep; boreal *= keep; birch *= keep; steppe *= keep; desert *= keep; mesa *= keep; swamp *= keep; tundra *= keep;
                    sequoia *= keep; blackForest *= keep; heath *= keep; flowers *= keep; fernGorge *= keep; sakura *= keep; bamboo *= keep; jungle *= keep;
                    volcanic *= keep; burnt *= keep; karst *= keep; petrified *= keep; ruins *= keep; oasis *= keep;
                    snowPeaks *= keep; glacier *= keep; frozenOcean *= keep; mangrove *= keep; saltFlat *= keep; geothermal *= keep;
                }
            }

            // ── kolo 28: šestý balík – čedičové pobřeží (mírné a teplé ne-vlhké pobřeží, spíš chladnější), obsidiánová pláň
            // (jen jádro horkého pásma) a alabastrové plato (suché jádro teplého pásma). Každá váha je oříznutá svým pásmem,
            // podíl K28 ≤ mírné + teplé + horké, ostatní regiony × (1 − K28). Kde jsou nové váhy nulové, zůstávají váhy
            // kola 21 bitově stejné. Makroterén, voda, hydrologie a LOD se nemění – jen barva, osazení a hledání.
            float basalt = 0f, obsidian = 0f, alabaster = 0f;
            float band28 = c.mild + c.warm + c.hot;
            if (band28 > 1e-4f)
            {
                float2 o28 = off.xz;
                float nBs = GenNoise.Fbm2(xz * (1f / 1900f), 2, o28 + new float2(-52.7f, -14.9f));
                float nOb = GenNoise.Fbm2(xz * (1f / 2200f), 2, o28 + new float2(66.3f, 41.7f));
                float nAl = GenNoise.Fbm2(xz * (1f / 2100f), 2, o28 + new float2(-8.9f, 73.3f));
                float depth28 = max(0f, sea - y);
                // čedič: břeh a pobřežní kopce ≤ 18–30 m n. m. (audit 6 seedů: pás ≤ 16 m byl jen úzká linka u vody) a mělčina ≤ 6–9 m; chladnější část mírného pobřeží převažuje (Island, Staffa);
                // ×2 – v jádru masky převládne i nad útesy (jinak mozaika)
                basalt = 2f * (c.mild * (1f - 0.5f * mt) + 0.6f * c.warm * (1f - c.wet)) * (1f - c.hot)
                       * (1f - smoothstep(18f, 30f, above)) * (1f - smoothstep(6f, 9f, depth28)) * smoothstep(0.10f, 0.26f, nBs) * (1f - mesa);
                basalt = min(basalt, c.mild + c.warm);
                if (c.hot > 1e-4f)
                {
                    // Podíl horkých regionů před kolem 28: obsidián vzniká jen tam, kde už převládá horký region (poušť, mesa, vulkán…),
                    // nikdy místo stepi/savany, která v horkém pásmu patří k teplým – mapa horkých regionů se tak nezvětší a odstup
                    // horký↔chladný region zůstane jako v kole 27 (audit 6 seedů: s777 1828 → 1406 m bez této podmínky).
                    float hotPrev = desert + mesa + volcanic + petrified + saltFlat + geothermal + oasis;
                    float allPrev = meadow + boreal + birch + steppe + desert + mesa + swamp + tundra
                                  + sequoia + blackForest + heath + flowers + fernGorge + sakura + bamboo + jungle
                                  + volcanic + burnt + karst + petrified + ruins + oasis
                                  + snowPeaks + glacier + frozenOcean + mangrove + saltFlat + geothermal + crystal + mushroom + cliffs;
                    float hotDom = smoothstep(0.45f, 0.65f, hotPrev / max(allPrev, 1e-5f));
                    // obsidián: jádro horkého pásma (jako solné pláně – horké pásmo se nepřiblíží chladnému), suché roviny a nízké
                    // plošiny 16–170 m bez teras; vlastní maska – od vulkánu (vysočiny, láva, jehly) ho dělí reliéf i paleta;
                    // ×2,6: černá barva se lineárním mícháním vah snadno přebije (pilot s1337: 80 % → hnědá 0,13), v jádru proto převládne celá
                    float flat28 = 1f - smoothstep(0.15f, 0.35f, cliff);
                    obsidian = 2.6f * c.hot * smoothstep(0.55f, 0.85f, c.hot) * (c.dry + 0.5f * c.mid) * smoothstep(16f, 26f, above) * (1f - smoothstep(130f, 170f, above))
                             * flat28 * smoothstep(0.10f, 0.26f, nOb) * (1f - mesa)
                             * smoothstep(0.64f, 0.70f, c.t) * hotDom;   // jako geotermál: jen hluboko v horkém pásmu – audit 6 seedů bez toho zkrátil odstup horký↔chladný region (s777 1828 → 1380 m)
                    obsidian = min(obsidian, c.hot);
                }
                if (c.warm > 1e-4f)
                {
                    // alabastr: suché jádro teplého pásma (t ≥ 0,55–0,60 jako ostatní teplé biomy), plošiny a vysočiny 40–420 m
                    // (i teplé masivy K27 – sníh tam není, SnowLift); terasy ho mírně posilují (erodované hrany plata)
                    alabaster = 2.0f * c.warm * smoothstep(0.55f, 0.60f, c.t) * (c.dry + 0.6f * c.mid) * smoothstep(40f, 70f, above) * (1f - smoothstep(360f, 420f, above))
                              * smoothstep(0.12f, 0.28f, nAl) * (0.85f + 0.15f * smoothstep(0.10f, 0.35f, cliff)) * (1f - mesa);
                    alabaster = min(alabaster, c.warm);
                }
                float s28 = basalt + obsidian + alabaster;
                if (s28 > 1e-5f)
                {
                    float K28 = min(s28, band28);
                    float k28 = K28 / s28;
                    basalt *= k28; obsidian *= k28; alabaster *= k28;
                    float keep = 1f - K28;
                    meadow *= keep; boreal *= keep; birch *= keep; steppe *= keep; desert *= keep; mesa *= keep; swamp *= keep; tundra *= keep;
                    sequoia *= keep; blackForest *= keep; heath *= keep; flowers *= keep; fernGorge *= keep; sakura *= keep; bamboo *= keep; jungle *= keep;
                    volcanic *= keep; burnt *= keep; karst *= keep; petrified *= keep; ruins *= keep; oasis *= keep;
                    snowPeaks *= keep; glacier *= keep; frozenOcean *= keep; mangrove *= keep; saltFlat *= keep; geothermal *= keep;
                    crystal *= keep; mushroom *= keep; cliffs *= keep;
                }
            }

            RegionWeights w;
            float sum = meadow + boreal + birch + steppe + desert + mesa + swamp + tundra
                      + sequoia + blackForest + heath + flowers + fernGorge + sakura + bamboo + jungle
                      + volcanic + burnt + karst + petrified + ruins + oasis
                      + snowPeaks + glacier + frozenOcean + mangrove + saltFlat + geothermal
                      + crystal + mushroom + cliffs
                      + basalt + obsidian + alabaster;
            float inv = sum > 1e-5f ? 1f / sum : 0f;
            w.meadow = sum > 1e-5f ? meadow * inv : 1f;
            w.boreal = boreal * inv; w.birch = birch * inv; w.steppe = steppe * inv; w.desert = desert * inv;
            w.mesa = mesa * inv; w.swamp = swamp * inv; w.tundra = tundra * inv;
            w.blackForest = blackForest * inv; w.sequoia = sequoia * inv; w.flowers = flowers * inv; w.heath = heath * inv;
            w.sakura = sakura * inv; w.bamboo = bamboo * inv; w.jungle = jungle * inv; w.fernGorge = fernGorge * inv;
            w.volcanic = volcanic * inv; w.burnt = burnt * inv; w.karst = karst * inv; w.petrified = petrified * inv;
            w.ruins = ruins * inv; w.oasis = oasis * inv;
            w.snowPeaks = snowPeaks * inv; w.glacier = glacier * inv; w.frozenOcean = frozenOcean * inv;
            w.mangrove = mangrove * inv; w.saltFlat = saltFlat * inv; w.geothermal = geothermal * inv;
            w.crystal = crystal * inv; w.mushroom = mushroom * inv; w.cliffs = cliffs * inv;
            w.basalt = basalt * inv; w.obsidian = obsidian * inv; w.alabaster = alabaster * inv;
            return w;
        }

        /// <summary>Barva povrchu podle regionů. Louku (dosavadní barvu) dostává hotovou.</summary>
        public static float3 Ground(in RegionWeights w, float3 meadow, float3 p, float nBig, float nMid)
        {
            float pm = saturate(0.5f + 0.9f * nMid);

            float3 boreal = lerp(new float3(0.13f, 0.29f, 0.15f), new float3(0.25f, 0.28f, 0.15f), pm * 0.45f);
            float3 birch = lerp(new float3(0.30f, 0.45f, 0.15f), new float3(0.50f, 0.45f, 0.16f), saturate(nMid * 0.9f + 0.15f) * 0.45f);
            float3 steppe = lerp(new float3(0.55f, 0.52f, 0.23f), new float3(0.44f, 0.49f, 0.21f), pm);

            // poušť: světlé a tmavší duny (zvlněné vlnky po větru) a narudlé plochy
            float2 dir = new float2(0.8f, 0.6f);
            float rip = sin(dot(p.xz, dir) * (6.2831853f / 34f) + 5f * nMid + 3f * nBig);
            // Kolo 15: sytější a tmavší písek – vertex barvy se na plném slunci zesvětlí (0,69 → téměř bílá), proto
            // je hodnota v paletě posunutá do tmavší, oranžovější oblasti; jas a kontrast se vyrovnají až ve hře.
            float3 desert = lerp(new float3(0.66f, 0.45f, 0.20f), new float3(0.57f, 0.37f, 0.16f), 0.5f + 0.5f * rip);
            desert = lerp(desert, new float3(0.62f, 0.33f, 0.15f), smoothstep(0.15f, 0.6f, nBig) * 0.55f);

            float3 mesa = lerp(new float3(0.74f, 0.49f, 0.30f), new float3(0.81f, 0.62f, 0.40f), pm);
            float3 swamp = SwampColor(nMid);
            float3 tundra = TundraColor(nMid);

            // kolo 16
            float3 blackForest = lerp(new float3(0.07f, 0.14f, 0.07f), new float3(0.13f, 0.11f, 0.06f), pm * 0.6f);
            float3 sequoia = lerp(new float3(0.20f, 0.15f, 0.08f), new float3(0.14f, 0.20f, 0.09f), pm * 0.6f);
            float fp = smoothstep(0.25f, 0.65f, nBig);   // velké, měkké skvrny květů – barvu nesou hlavně květiny
            float3 flowers = lerp(new float3(0.24f, 0.44f, 0.13f), new float3(0.40f, 0.42f, 0.16f), fp * 0.5f);
            float3 heath = lerp(new float3(0.27f, 0.26f, 0.16f), new float3(0.31f, 0.17f, 0.28f), saturate(0.55f + 0.9f * nMid) * 0.85f);
            float3 sakura = lerp(new float3(0.28f, 0.44f, 0.16f), new float3(0.46f, 0.33f, 0.31f), smoothstep(0.35f, 0.75f, nMid) * 0.4f);
            float3 bamboo = lerp(new float3(0.15f, 0.36f, 0.10f), new float3(0.24f, 0.38f, 0.11f), pm * 0.5f);
            float3 jungle = lerp(new float3(0.08f, 0.27f, 0.07f), new float3(0.15f, 0.22f, 0.08f), pm * 0.5f);
            float3 fernGorge = lerp(new float3(0.11f, 0.27f, 0.11f), new float3(0.18f, 0.24f, 0.10f), pm * 0.5f);

            // kolo 19 (tóny tmavší – vertex barvy se na plném slunci zesvětlí, viz poušť)
            // Na slunci se vertex barvy výrazně zesvětlí – láva a uhel proto skoro černé, jinak vyjdou hnědé.
            float3 volcanic = lerp(new float3(0.030f, 0.028f, 0.028f), new float3(0.085f, 0.080f, 0.076f), pm * 0.7f);   // čerstvá láva → popel
            volcanic = lerp(volcanic, new float3(0.22f, 0.08f, 0.035f), smoothstep(0.35f, 0.75f, nBig) * 0.35f);          // rezavé oxidy
            float3 burnt = lerp(new float3(0.055f, 0.050f, 0.046f), new float3(0.13f, 0.12f, 0.105f), pm);                // uhel a popel
            burnt = lerp(burnt, new float3(0.20f, 0.27f, 0.10f), smoothstep(0.40f, 0.75f, nBig) * 0.45f);                // první zelené výhonky
            float3 karst = lerp(new float3(0.17f, 0.36f, 0.12f), new float3(0.27f, 0.39f, 0.15f), pm * 0.6f);             // svěží travnatá dna
            float3 petrified = lerp(new float3(0.33f, 0.30f, 0.33f), new float3(0.42f, 0.30f, 0.24f), smoothstep(0.2f, 0.8f, pm)); // šedofialový bentonit / rez
            float3 ruins = lerp(new float3(0.33f, 0.38f, 0.16f), new float3(0.46f, 0.41f, 0.27f), smoothstep(0.35f, 0.75f, nMid) * 0.6f); // suchá tráva a prašné cesty
            float3 oasis = lerp(new float3(0.15f, 0.36f, 0.10f), new float3(0.56f, 0.44f, 0.24f), smoothstep(0.25f, 0.70f, nMid) * 0.45f); // zeleň a písek

            float3 g = meadow * w.meadow + boreal * w.boreal + birch * w.birch + steppe * w.steppe
                 + desert * w.desert + mesa * w.mesa + swamp * w.swamp + tundra * w.tundra
                 + blackForest * w.blackForest + sequoia * w.sequoia + flowers * w.flowers + heath * w.heath
                 + sakura * w.sakura + bamboo * w.bamboo + jungle * w.jungle + fernGorge * w.fernGorge
                 + volcanic * w.volcanic + burnt * w.burnt + karst * w.karst + petrified * w.petrified + ruins * w.ruins + oasis * w.oasis;
            float w20 = w.snowPeaks + w.glacier + w.frozenOcean + w.mangrove + w.saltFlat + w.geothermal;
            float w21 = w.crystal + w.mushroom + w.cliffs;
            if (w21 >= 0.002f) g += Ground21(w, p, nBig, nMid, pm);   // kolo 21 (mimo nové biomy beze změny)
            float w28 = w.basalt + w.obsidian + w.alabaster;
            if (w28 >= 0.002f) g += Ground28(w, p, nBig, nMid, pm);   // kolo 28 (mimo nové biomy beze změny)
            if (w20 < 0.002f) return g;   // kolo 20: mimo nové biomy beze změny (a bez nákladu)

            // kolo 20 (tóny jako kolo 19 – sníh a sůl o stupeň tmavší, na slunci se zesvětlí)
            float3 snowPk = lerp(new float3(0.80f, 0.82f, 0.86f), new float3(0.40f, 0.41f, 0.44f), smoothstep(0.30f, 0.70f, nMid) * 0.55f);  // sníh a skalní žebra
            float3 glacier = lerp(new float3(0.70f, 0.80f, 0.88f), new float3(0.78f, 0.82f, 0.86f), pm);                                       // modravý led a firn
            glacier = lerp(glacier, new float3(0.42f, 0.41f, 0.39f), smoothstep(0.45f, 0.80f, nBig) * 0.55f);                                 // šedé morény
            float3 frozen = lerp(new float3(0.82f, 0.85f, 0.89f), new float3(0.62f, 0.66f, 0.70f), smoothstep(0.30f, 0.75f, nMid) * 0.6f);  // závěje a ušlapaný sníh
            float3 mangrove = lerp(new float3(0.17f, 0.14f, 0.09f), new float3(0.11f, 0.20f, 0.07f), smoothstep(0.40f, 0.75f, nMid) * 0.6f); // bahno a zeleň
            float3 salt = lerp(new float3(0.90f, 0.89f, 0.86f), new float3(0.80f, 0.77f, 0.70f), smoothstep(0.35f, 0.80f, nBig) * 0.5f);     // solná krusta
            if (w.saltFlat > 0.002f) salt = lerp(salt, new float3(0.46f, 0.44f, 0.40f), SaltCrack(p.xz) * 0.85f);                              // popraskané polygony
            float3 geo = lerp(new float3(0.66f, 0.62f, 0.52f), new float3(0.55f, 0.50f, 0.42f), pm);                                         // travertin a sintr
            if (w.geothermal > 0.002f) geo = GeoRings(p.xz, geo, nMid);                                                                       // barevné prstence pramenů
            return g + snowPk * w.snowPeaks + glacier * w.glacier + frozen * w.frozenOcean + mangrove * w.mangrove + salt * w.saltFlat + geo * w.geothermal;
        }

        /// <summary>
        /// Kolo 21: barvy povrchu nových biomů (vlastní paleta, ne přebarvení): šedofialová minerální suť s lišejníkem,
        /// tmavá mechová lesní půda s fialovými skvrnami výtrusů, krátká pobřežní tráva s křídovou hlínou.
        /// </summary>
        private static float3 Ground21(in RegionWeights w, float3 p, float nBig, float nMid, float pm)
        {
            float3 crystal = lerp(new float3(0.27f, 0.25f, 0.31f), new float3(0.20f, 0.22f, 0.30f), pm);
            crystal = lerp(crystal, new float3(0.25f, 0.30f, 0.19f), smoothstep(0.30f, 0.70f, nBig) * 0.45f);   // lišejník a mech mezi sutí
            float3 mushroom = lerp(new float3(0.10f, 0.17f, 0.08f), new float3(0.16f, 0.13f, 0.10f), pm * 0.6f);
            mushroom = lerp(mushroom, new float3(0.22f, 0.14f, 0.24f), smoothstep(0.40f, 0.80f, nMid) * 0.5f);  // fialové skvrny výtrusů
            float3 cliffs = lerp(new float3(0.32f, 0.44f, 0.20f), new float3(0.44f, 0.46f, 0.26f), pm * 0.7f);
            cliffs = lerp(cliffs, new float3(0.62f, 0.60f, 0.52f), smoothstep(0.45f, 0.85f, nBig) * 0.4f);       // vyšlapaná křídová hlína
            return crystal * w.crystal + mushroom * w.mushroom + cliffs * w.cliffs;
        }

        /// <summary>
        /// Kolo 28: barvy povrchu – tmavá čedičová drť s mechem, leskle černá skelná pláň rozpraskaná na desky (pukliny
        /// s šedým popelem) a slonovinové prašné plato. Tóny jsou lineární – na obrazovce vyjdou o hodně světlejší (pilot 3 seedů:
        /// 0,66 → přepálená bílá, 0,075 → hnědošedá), proto čedič a obsidián ~0,01–0,04 a alabastr ≤ 0,50.
        /// </summary>
        private static float3 Ground28(in RegionWeights w, float3 p, float nBig, float nMid, float pm)
        {
            float3 basalt = lerp(new float3(0.022f, 0.022f, 0.025f), new float3(0.040f, 0.040f, 0.038f), pm * 0.8f);
            basalt = lerp(basalt, new float3(0.05f, 0.09f, 0.03f), smoothstep(0.40f, 0.80f, nBig) * 0.35f);   // mech a nízká tráva
            float3 obs = lerp(new float3(0.010f, 0.009f, 0.014f), new float3(0.018f, 0.016f, 0.024f), pm);
            if (w.obsidian > 0.002f) obs = lerp(obs, new float3(0.075f, 0.07f, 0.075f), ObsidianCrack(p.xz) * 0.8f);
            float3 ala = lerp(new float3(0.44f, 0.41f, 0.34f), new float3(0.39f, 0.36f, 0.29f), pm * 0.7f);
            ala = lerp(ala, new float3(0.50f, 0.47f, 0.40f), smoothstep(0.35f, 0.80f, nBig) * 0.5f);         // jemný bílý prach
            return basalt * w.basalt + obs * w.obsidian + ala * w.alabaster;
        }

        /// <summary>Kolo 28: 1 na puklině obsidiánové pláně (Voronoi desky ~22 j., spára ~1,5–3,5 j.), 0 uvnitř desky.</summary>
        public static float ObsidianCrack(float2 xz)
        {
            float2 g = xz * (1f / 22f);
            float2 gf = floor(g), f = g - gf;
            int2 gi = (int2)gf;
            float d1 = 9f, d2 = 9f;
            for (int j = -1; j <= 1; j++)
            for (int i = -1; i <= 1; i++)
            {
                uint h = GenNoise.Hash(unchecked((uint)(gi.x + i) * 50331653u ^ (uint)(gi.y + j) * 12582917u ^ 0x0B51u));
                float2 o = new float2(GenNoise.Hash01(h), GenNoise.Hash01(GenNoise.Hash(h)));
                float2 r = new float2(i, j) + o - f;
                float d = dot(r, r);
                if (d < d1) { d2 = d1; d1 = d; } else if (d < d2) d2 = d;
            }
            return 1f - smoothstep(0.06f, 0.14f, sqrt(d2) - sqrt(d1));
        }

        /// <summary>
        /// Kolo 21: 0 = bílé křídové útesy, 1 = černé čedičové (velké úseky pobřeží ~700 m). Stejně v barvě svahů
        /// (VoxelWorld) i v osazení (EcologyPlacer) – silueta a barva skály se nerozejdou.
        /// </summary>
        public static float CliffDarkness(float2 xz, float3 off)
            => smoothstep(0.08f, 0.18f, GenNoise.Fbm2(xz * (1f / 700f), 2, off.xz + new float2(13.7f, -71.3f)));

        /// <summary>Kolo 20: 1 na hraně solného polygonu (Voronoi buňky ~12 j., hrana ~1–2 j. – širší než faseta terénu), 0 uvnitř.</summary>
        public static float SaltCrack(float2 xz)
        {
            float2 g = xz * (1f / 12f);
            float2 gf = floor(g), f = g - gf;
            int2 gi = (int2)gf;
            float d1 = 9f, d2 = 9f;
            for (int j = -1; j <= 1; j++)
            for (int i = -1; i <= 1; i++)
            {
                uint h = GenNoise.Hash(unchecked((uint)(gi.x + i) * 73856093u ^ (uint)(gi.y + j) * 19349663u ^ 0x5A17u));
                float2 o = new float2(GenNoise.Hash01(h), GenNoise.Hash01(GenNoise.Hash(h)));
                float2 r = new float2(i, j) + o - f;
                float d = dot(r, r);
                if (d < d1) { d2 = d1; d1 = d; } else if (d < d2) d2 = d;
            }
            return 1f - smoothstep(0.07f, 0.17f, sqrt(d2) - sqrt(d1));
        }

        /// <summary>
        /// Kolo 20: barevné minerální prstence kolem pramenů (buňky 70 m, pramen v ~60 % buněk): tyrkysová
        /// krusta → světlý sintr → sírová žlutá → oranžová → rez, dál travertin. Žádná voda – jen barva povrchu.
        /// </summary>
        public static float3 GeoRings(float2 xz, float3 baseC, float nMid)
        {
            float2 g = xz * (1f / 70f);
            float2 gf = floor(g), f = g - gf;
            int2 gi = (int2)gf;
            float best = 1e9f;
            for (int j = -1; j <= 1; j++)
            for (int i = -1; i <= 1; i++)
            {
                uint h = GenNoise.Hash(unchecked((uint)(gi.x + i) * 83492791u ^ (uint)(gi.y + j) * 2654435761u ^ 0x6E07u));
                if (GenNoise.Hash01(h) > 0.6f) continue;
                uint h2 = GenNoise.Hash(h);
                float2 o = new float2(0.2f + 0.6f * GenNoise.Hash01(h2), 0.2f + 0.6f * GenNoise.Hash01(GenNoise.Hash(h2)));
                float2 r = (new float2(i, j) + o - f) * 70f;
                best = min(best, dot(r, r));
            }
            if (best > 1e8f) return baseC;
            float d = sqrt(best) * (1f + 0.25f * nMid);
            float3 c = baseC;
            c = lerp(c, new float3(0.42f, 0.25f, 0.14f), 1f - smoothstep(26f, 36f, d));   // rez
            c = lerp(c, new float3(0.66f, 0.32f, 0.08f), 1f - smoothstep(16f, 22f, d));   // oranžová
            c = lerp(c, new float3(0.80f, 0.66f, 0.16f), 1f - smoothstep(10f, 14f, d));   // síra
            c = lerp(c, new float3(0.80f, 0.79f, 0.70f), 1f - smoothstep(6f, 8.5f, d));    // sintr
            c = lerp(c, new float3(0.16f, 0.52f, 0.52f), 1f - smoothstep(3.0f, 4.5f, d));  // tyrkysová krusta
            return c;
        }

        /// <summary>Lišejníková tundra – také barva výškového pásma pod sněžnou čarou.</summary>
        public static float3 TundraColor(float nMid)
            => lerp(new float3(0.21f, 0.26f, 0.15f), new float3(0.32f, 0.21f, 0.13f), saturate(0.35f + 0.9f * nMid) * 0.6f);

        /// <summary>Kolo 15: tmavá vlhká půda mokřadu (olivová s rašelinově hnědými skvrnami).</summary>
        public static float3 SwampColor(float nMid)
            => lerp(new float3(0.17f, 0.23f, 0.11f), new float3(0.25f, 0.21f, 0.12f), saturate(0.5f + 0.9f * nMid) * 0.7f);

        /// <summary>
        /// Barva strmých ploch v suchých regionech: vodorovné vrstvy mes (terakota, oranžová,
        /// krémová, cihlová), pískovec pouště. Váha = mesa + poušť; jinde se nic nemění.
        /// </summary>
        public static float3 Steep(in RegionWeights w, float3 rock, float y, float nMid, int seed, float cliffDark = 0f, float2 xz28 = default)
        {
            // Kolo 15: v tundře je skála šedozelená s lišejníkem, ne béžová suť.
            rock = lerp(rock, new float3(0.27f, 0.29f, 0.24f), w.tundra * 0.7f);
            // Kolo 16: mechové stěny rokle a zarostlé svahy džungle a bambusu; vřesoviště bez holé skály
            // (svah je porostlý), žádné nové vrstvy ani pruhy.
            float moss = saturate(w.fernGorge * 0.9f + w.jungle * 0.6f + w.bamboo * 0.5f + w.blackForest * 0.35f);
            rock = lerp(rock, new float3(0.14f, 0.24f, 0.11f), moss * 0.75f);
            rock = lerp(rock, new float3(0.29f, 0.26f, 0.22f), w.heath * 0.5f);
            // Kolo 19: černá láva, ohořelé svahy, světlý vápenec, pruhovaný bentonit zkamenělého lesa.
            rock = lerp(rock, new float3(0.06f, 0.055f, 0.055f), w.volcanic * 0.92f);
            rock = lerp(rock, new float3(0.14f, 0.12f, 0.11f), w.burnt * 0.6f);
            rock = lerp(rock, new float3(0.64f, 0.63f, 0.58f), w.karst * 0.85f);
            if (w.petrified > 0.002f)
            {
                float yp = y + 2f * nMid;
                uint hp = GenNoise.Hash(unchecked((uint)((int)floor(yp / 3.2f) * 4567 + seed * 17 + 101)));
                float up = GenNoise.Hash01(hp);
                float3 pb = up < 0.3f ? new float3(0.50f, 0.48f, 0.55f) : up < 0.55f ? new float3(0.66f, 0.60f, 0.52f)
                          : up < 0.8f ? new float3(0.58f, 0.38f, 0.29f) : new float3(0.42f, 0.42f, 0.48f);
                rock = lerp(rock, pb, saturate(w.petrified * 1.1f));
            }
            // Kolo 20: zasněžené skalní stěny štítů, modré ledové stěny ledovce, světlé svahy solných plání,
            // páskované travertinové svahy geotermálních polí, bahnité břehy mangrov.
            rock = lerp(rock, new float3(0.34f, 0.35f, 0.38f), w.snowPeaks * 0.7f);
            rock = lerp(rock, new float3(0.50f, 0.68f, 0.80f), w.glacier * 0.85f);
            rock = lerp(rock, new float3(0.55f, 0.58f, 0.62f), w.frozenOcean * 0.6f);
            rock = lerp(rock, new float3(0.26f, 0.22f, 0.15f), w.mangrove * 0.8f);
            rock = lerp(rock, new float3(0.70f, 0.67f, 0.60f), w.saltFlat * 0.8f);
            if (w.geothermal > 0.002f)
            {
                float yg = y + 1.5f * nMid;
                float3 tb = frac(yg / 1.6f) < 0.5f ? new float3(0.74f, 0.66f, 0.50f) : new float3(0.66f, 0.40f, 0.16f);
                rock = lerp(rock, tb, w.geothermal * 0.85f);
            }
            // Kolo 21: minerální stěny krystalových oblastí (šedofialové vrstvy s modrými žilami), mechové svahy
            // houbového lesa, bílé křídové nebo černé čedičové útesy (podle úseku pobřeží).
            if (w.crystal > 0.002f)
            {
                float yc = y + 1.8f * nMid;
                float vein = 1f - smoothstep(0.0f, 0.12f, abs(frac(yc / 2.3f) - 0.5f) - 0.38f);
                float3 cb = lerp(new float3(0.30f, 0.27f, 0.36f), new float3(0.22f, 0.21f, 0.30f), frac(yc / 4.6f) < 0.5f ? 0f : 1f);
                cb = lerp(cb, new float3(0.38f, 0.44f, 0.78f), vein * 0.6f);
                rock = lerp(rock, cb, saturate(w.crystal * 1.1f));
            }
            rock = lerp(rock, new float3(0.13f, 0.20f, 0.10f), w.mushroom * 0.75f);
            if (w.cliffs > 0.002f)
            {
                float yk = y + 1.2f * nMid;
                float3 chalk = lerp(new float3(0.86f, 0.85f, 0.80f), new float3(0.74f, 0.73f, 0.68f), frac(yk / 3.1f) < 0.12f ? 1f : 0f);   // pásky pazourku
                float3 basalt = lerp(new float3(0.07f, 0.07f, 0.075f), new float3(0.13f, 0.13f, 0.14f), frac(yk / 1.4f));
                rock = lerp(rock, lerp(chalk, basalt, cliffDark), saturate(w.cliffs * 1.15f));
            }
            // Kolo 28: čedičové stěny se svislými spárami sloupů (faseta podle polohy), skelný obsidián s fialovým leskem,
            // měkké slonovinové vrstvy alabastru (nízký kontrast – žádné pruhy jako u mes).
            if (w.basalt > 0.002f)
            {
                float a1 = frac(xz28.x / 2.8f), a2 = frac(dot(xz28, new float2(0.5f, 0.866f)) / 2.8f);
                float3 bs = lerp(new float3(0.022f, 0.022f, 0.025f), new float3(0.042f, 0.042f, 0.046f), frac(a1 * 2f + a2) < 0.5f ? 0f : 1f);
                bs = lerp(bs, new float3(0.010f, 0.010f, 0.012f), max(a1, a2) > 0.86f ? 0.7f : 0f);
                rock = lerp(rock, bs, saturate(w.basalt * 1.15f));
            }
            if (w.obsidian > 0.002f)
            {
                float3 ob = lerp(new float3(0.009f, 0.008f, 0.012f), new float3(0.035f, 0.030f, 0.055f), frac((y + 1.5f * nMid) / 3.7f) < 0.15f ? 1f : 0f);
                rock = lerp(rock, ob, saturate(w.obsidian * 1.15f));
            }
            if (w.alabaster > 0.002f)
            {
                float3 al = lerp(new float3(0.48f, 0.45f, 0.38f), new float3(0.41f, 0.38f, 0.31f), frac((y + 1.6f * nMid) / 4.4f) < 0.5f ? 0f : 1f);
                rock = lerp(rock, al, saturate(w.alabaster * 1.15f));
            }
            float a = w.mesa + w.desert;
            if (a < 0.002f) return rock;
            float yy = y + 3f * nMid;
            float band = floor(yy / 5.5f);
            uint hsh = GenNoise.Hash(unchecked((uint)((int)band * 7919 + seed * 31)));
            float u = GenNoise.Hash01(hsh);
            float3 c0 = new float3(0.70f, 0.33f, 0.19f), c1 = new float3(0.80f, 0.50f, 0.27f);
            float3 c2 = new float3(0.86f, 0.72f, 0.54f), c3 = new float3(0.58f, 0.28f, 0.18f), c4 = new float3(0.75f, 0.42f, 0.24f);
            float3 bandC = u < 0.22f ? c0 : u < 0.44f ? c1 : u < 0.58f ? c2 : u < 0.78f ? c3 : c4;
            bandC *= 0.94f + 0.12f * frac(yy / 5.5f);
            float3 sandstone = new float3(0.60f, 0.40f, 0.23f);
            float3 dryRock = (bandC * w.mesa + sandstone * w.desert) / a;
            return lerp(rock, dryRock, saturate(a * 1.15f));
        }

        /// <summary>Diagnostika (/pruhy diag 6): plné barvy regionů jako na mapě /biomy mapa.</summary>
        public static float3 DebugColor(in RegionWeights w)
        {
            return w.meadow * new float3(0.38f, 0.63f, 0.25f) + w.boreal * new float3(0.12f, 0.35f, 0.20f)
                 + w.birch * new float3(0.78f, 0.67f, 0.24f) + w.steppe * new float3(0.78f, 0.73f, 0.43f)
                 + w.desert * new float3(0.94f, 0.82f, 0.55f) + w.mesa * new float3(0.78f, 0.35f, 0.20f)
                 + w.swamp * new float3(0.27f, 0.37f, 0.24f) + w.tundra * new float3(0.59f, 0.59f, 0.47f)
                 + w.blackForest * new float3(0.05f, 0.18f, 0.10f) + w.sequoia * new float3(0.45f, 0.22f, 0.15f)
                 + w.flowers * new float3(0.85f, 0.55f, 0.80f) + w.heath * new float3(0.55f, 0.35f, 0.60f)
                 + w.sakura * new float3(0.95f, 0.65f, 0.75f) + w.bamboo * new float3(0.55f, 0.80f, 0.30f)
                 + w.jungle * new float3(0.10f, 0.50f, 0.15f) + w.fernGorge * new float3(0.25f, 0.55f, 0.45f)
                 + w.volcanic * new float3(0.15f, 0.12f, 0.12f) + w.burnt * new float3(0.35f, 0.30f, 0.26f)
                 + w.karst * new float3(0.75f, 0.80f, 0.70f) + w.petrified * new float3(0.62f, 0.50f, 0.62f)
                 + w.ruins * new float3(0.70f, 0.62f, 0.40f) + w.oasis * new float3(0.20f, 0.75f, 0.70f)
                 + w.snowPeaks * new float3(0.95f, 0.95f, 1.00f) + w.glacier * new float3(0.55f, 0.80f, 0.95f)
                 + w.frozenOcean * new float3(0.75f, 0.88f, 0.95f) + w.mangrove * new float3(0.25f, 0.35f, 0.15f)
                 + w.saltFlat * new float3(1.00f, 0.97f, 0.90f) + w.geothermal * new float3(0.95f, 0.55f, 0.15f)
                 + w.crystal * new float3(0.55f, 0.35f, 0.85f) + w.mushroom * new float3(0.85f, 0.25f, 0.35f) + w.cliffs * new float3(0.92f, 0.92f, 0.85f)
                 + w.basalt * new float3(0.22f, 0.25f, 0.33f) + w.obsidian * new float3(0.37f, 0.14f, 0.47f) + w.alabaster * new float3(1.00f, 0.84f, 0.63f);
        }

        public static string Name(int r) => r switch
        {
            0 => "louka", 1 => "jehličnatý les", 2 => "březový háj", 3 => "step", 4 => "poušť",
            5 => "mesa", 6 => "mokřad", 7 => "tundra",
            8 => "černý les", 9 => "sekvojový les", 10 => "kvetoucí louka", 11 => "vřesoviště",
            12 => "sakurové údolí", 13 => "bambusové údolí", 14 => "tropická džungle", 15 => "kapradinová rokle",
            16 => "vulkanická oblast", 17 => "spálený les", 18 => "kras", 19 => "zkamenělý les", 20 => "ruiny", 21 => "oáza",
            22 => "zasněžené štíty", 23 => "ledovcové údolí", 24 => "zamrzlý oceán", 25 => "mangrovy", 26 => "solné pláně", 27 => "geotermální pole",
            28 => "krystalová oblast", 29 => "houbový les", 30 => "útesové pobřeží",
            31 => "čedičové pobřeží", 32 => "obsidiánová pláň", 33 => "alabastrové plato", _ => "?",
        };
    }

    /// <summary>
    /// Kolo 15: <see cref="BiomeMath.Weights"/> přeložené Burstem pro osazovač (přímé volání
    /// z hlavního vlákna). Je to TÁŽ funkce, kterou už teď v Burstu volá barva terénu – osazení
    /// a barva tak počítají bit po bitu stejně. Mono verze byla ~7,6 µs na kandidáta
    /// (69 % času osazování); rozdíl Mono/Burst měří <c>/biome burstcheck</c>.
    /// </summary>
    [BurstCompile]
    public static class BiomeBurst
    {
        /// <summary>Přepínač pro A/B (konzole <c>/biome burst on|off</c>).</summary>
        public static bool Enabled = true;

        [BurstCompile]
        public static void Weights(in float2 xz, float y, float temp, float hum, float cliff,
                                   float hW, int hasWater, float sea, in float3 off, out RegionWeights w)
        {
            w = BiomeMath.Weights(xz, y, temp, hum, cliff, hW, hasWater != 0, sea, off);
        }
    }
}
