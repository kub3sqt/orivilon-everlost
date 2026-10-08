using Unity.Collections;
using UnityEngine;

namespace Orivilon.World.Generation
{
    /// <summary>
    /// Autorovatelné nastavení generátoru světa. Vytváří se přes menu "Everlost/World Gen Settings".
    ///
    /// Frekvence se zadávají jako PERIODA v metrech (čitelné), do GenParams se převádí na 1/perioda.
    /// Křivky se v jobech nepoužívají přímo – převádí je BuildSplines() na SplineLUT.
    /// </summary>
    [CreateAssetMenu(fileName = "World Gen Settings", menuName = "Everlost/World Gen Settings")]
    public class WorldGenSettings : ScriptableObject
    {
        [Header("Svět")]
        [Tooltip("Referenční hladina moře v metrech. Doporučeno nechat na 0 – spliny na tom stojí.")]
        public float seaLevel = 0f;

        [Header("Domain warp (makro)")]
        [Tooltip("Perioda deformace vstupní souřadnice. Drž ji blízko periodě continentalness.")]
        public float warpPeriod = 2048f;
        [Tooltip("Amplituda warpu v metrech. Nad ~25 % periody se pole skládá do sebe a pobřeží dělá smyčky.")]
        public float warpAmp = 420f;

        [Header("Členitost")]
        [Tooltip("Základní členitost v metrech. Spliny níž jsou jen bezrozměrné násobiče – tohle je to, co dělá z hor hory. 0 = svět má reliéf jen z continentalness.")]
        public float reliefAmplitude = 60f;

        [Header("Pohoří – tektonický zdvih")]
        [Tooltip("Výška pohoří v METRECH nad okolní krajinu. 0 = žádná pohoří, jen zvlněný reliéf. " +
                 "Přičítá se k makro výšce, takže hory rostou nad krajinou, ne z ní.")]
        public float upliftAmplitude = 450f;
        [Tooltip("Délka jednoho horského řetězu. Vyšší = delší a vzácnější pásma.")]
        public float upliftPeriod = 7200f;
        [Range(1, 8)] public int upliftOctaves = 4;
        [Tooltip("Jak úzká jsou pásma. 1 = široké kupy přes celou krajinu, 3 = ostré řetězy s nížinami mezi nimi.")]
        [Range(0.5f, 5f)] public float upliftSharpness = 1.8f;
        [Tooltip("Od jaké continentalness se pohoří smí zvedat. Drží hory ve vnitrozemí, ať nevystupují z moře.")]
        public float upliftContinentMin = -0.30f;
        [Tooltip("O kolik zdvih zhrubne povrchový detail. Bez toho vyjde obří masiv hladký jako kopeček.")]
        [Range(0f, 2f)] public float upliftShapeBoost = 0.8f;

        [Header("Continentalness – oceán vs. pevnina")]
        public float contPeriod = 4096f;
        [Range(1, 8)] public int contOctaves = 5;

        [Header("Erosion – stáří reliéfu")]
        public float erosPeriod = 2048f;
        [Range(1, 8)] public int erosOctaves = 4;

        [Header("Weirdness – surovina pro peaks & valleys")]
        public float weirdPeriod = 1024f;
        [Range(1, 8)] public int weirdOctaves = 4;

        [Header("Klima (jen barva a osazení, NE tvar terénu)")]
        public float tempPeriod = 3072f;
        public float humPeriod = 2560f;
        [Range(1, 6)] public int climateOctaves = 3;

        [Header("Detail povrchu")]
        [Tooltip("Ridged multifractal – dělá ostré hřbety.")]
        public float ridgePeriod = 220f;
        [Range(1, 8)] public int ridgeOctaves = 5;
        public float ridgeAmp = 16f;
        [Tooltip("Jemné zrno, aby svahy nebyly hladké jako sklo.")]
        public float grainPeriod = 70f;
        [Range(1, 6)] public int grainOctaves = 3;
        public float grainAmp = 4.5f;

        [Header("Terasování / útesy")]
        [Tooltip("Výška jednoho stupně v metrech.")]
        public float stepHeight = 7f;
        [Tooltip("Šířka náběhu stupně. Malá = svislá stěna, velká = schod.")]
        [Range(0.01f, 0.45f)] public float cliffSharpness = 0.04f;
        [Tooltip("Kolik terasování se vůbec smí projevit. 0 = vypnuto, 1 = plná síla. Akcent, ne hlavní rys.")]
        [Range(0f, 1f)] public float cliffStrength = 0.5f;
        [Tooltip("Sklon makro terénu, od kterého terasování začíná. Nízká hodnota zteraskuje i běžný svah.")]
        public float cliffSlopeMin = 0.55f;
        public float cliffSlopeMax = 1.20f;

        [Header("Klimatická identita terénu")]
        [Tooltip("Násobič teras v suchém horkém kraji. Nad 1 = mesy a kaňonové stěny v poušti.")]
        [Range(1f, 4f)] public float aridCliffBoost = 2.1f;
        [Tooltip("Násobič teras ve vlhkém kraji. Pod 1 = měkké zalesněné svahy bez schodů.")]
        [Range(0.05f, 1f)] public float humidCliffDamp = 0.3f;
        [Tooltip("Násobič výšky jednoho stupně v suchu. Vyšší = méně, ale mohutnějších stupňů.")]
        [Range(1f, 4f)] public float aridStepBoost = 2.4f;
        [Tooltip("Jak silně se horké vlhké nížiny srovnají do bažinaté plošiny. 0 = vypnuto.")]
        [Range(0f, 1f)] public float swampStrength = 0.55f;
        [Tooltip("Násobič hloubky zářezu řeky v suchu. Nad 1 = kaňon místo údolí.")]
        [Range(1f, 4f)] public float aridIncision = 2.3f;

        [Header("Mikro-biomy (zlatý háj, čedič)")]
        [Tooltip("Rozteč mřížky buněk v metrech. Jedna buňka = jeden možný výskyt.")]
        public float microCell = 1200f;
        [Tooltip("Poloměr skvrny jako podíl buňky. 0,3 = skvrna přes zhruba 700 m.")]
        [Range(0.05f, 0.45f)] public float microRadius = 0.3f;
        [Tooltip("Šířka měkkého okraje v metrech.")]
        public float microBlend = 60f;
        [Tooltip("Podíl obsazených buněk. 0,033 = zhruba jedna ze třiceti.")]
        [Range(0f, 0.5f)] public float microChance = 0.033f;

        [Header("Řeky – regionální síť")]
        [Tooltip("Od jaké akumulace (počtu buněk povodí) je buňka koryto. Buňka je 32×32 m. " +
                 "Nižší = hustší síť pramínků, vyšší = jen velké toky.")]
        public float riverMinAccum = 250f;
        [Tooltip("Šířka pramene v metrech – nejmenší koryto, které ještě vznikne.")]
        public float riverWidthMin = 6f;
        [Tooltip("Šířka veletoku v metrech, kam se šířka nasytí.")]
        public float riverWidthMax = 42f;
        [Tooltip("Při jaké ploše povodí (v buňkách) je šířka plná. Tohle nasycení zároveň drží " +
                 "síť bez švů mezi regiony – musí být řádově méně, než kolik buněk leží uvnitř hala.")]
        public float riverAccumFull = 4000f;
        [Tooltip("Šířka břehového náběhu jako podíl poloviny koryta. Tohle dělá U profil místo ostrého V.")]
        [Range(0.1f, 3f)] public float riverBankRatio = 0.9f;
        [Tooltip("Hloubka koryta jako podíl šířky. Skutečná řeka má kolem 0,1.")]
        [Range(0.02f, 0.4f)] public float riverDepthRatio = 0.11f;
        [Tooltip("Mez hloubky koryta v metrech, ať potok není propast a veletok kaluž.")]
        public float riverDepthMin = 1.2f;
        public float riverDepthMax = 6f;
        [Tooltip("Šířka údolní mísy jako násobek šířky koryta. Mísa je mělká – jen aby řeka neležela v rovině.")]
        [Range(0f, 8f)] public float riverValleyWidth = 3f;
        [Range(0f, 1f)] public float riverValleyFlatten = 0.45f;
        [Tooltip("O kolik metrů nad hladinou leží dno údolní mísy.")]
        public float riverBankHeight = 3f;
        [Tooltip("Hloubka koryta v ústí do moře (m). Zároveň strop, jak hluboko smí řeka " +
                 "zaříznout mořské dno – drží ústí jako mělčinu místo kanálu.")]
        /// <summary>Kolo 7: práh na horní straně hydrologického schodu (kód, ne asset). Viz GenParams.hydroSill.</summary>
        public static bool HydroSillEnabled = true;

        /// <summary>Kolo 11: plynulý směr toku u soutoků (kód, ne asset). Viz GenParams.flowBlend.</summary>
        public static bool FlowBlendEnabled = true;

        /// <summary>Kolo 12: měkké stupně slabého terasování (kód, ne asset). Viz GenParams.softTerrace.</summary>
        public static bool SoftTerraceEnabled = true;

        /// <summary>Kolo 12b: klidný povrchový 3D detail (kód, ne asset). Viz GenParams.calmDetail.</summary>
        public static bool CalmDetailEnabled = true;
        /// <summary>Kolo 12b: parametry klidného detailu (kód, ne asset).</summary>
        public const float CalmOverhangYStretch = 0.4f;
        public const int CalmOverhangOct = 1, CalmSquishOct = 2;

        /// <summary>
        /// Kolo 27: monumentální horské masivy (kód, ne asset – starý asset zůstává beze změny).
        /// Hladká regionální maska (perioda ~km) vybere vzácné vnitrozemské oblasti; uvnitř zvedne
        /// celý masiv (kořen) a vlastní ridged pole na něm udělá hřebeny, sedla a údolí. Mimo masivy
        /// se klasický zdvih ztlumí, takže nížiny, pláně a pobřeží zůstanou čitelné.
        /// </summary>
        [System.Serializable]
        public struct MassifConfig
        {
            public float amp, period, lo, hi, ridgePeriod;
            public int ridgeOct;
            public float sharp, basePart, outside, upliftBoost, shape, top, topRange;
        }

        /// <summary>Kolo 29: tvar meteorického kráteru (0 = jen osazení a barva, 1 = v EvalMacro – A/B, 2 = povrch po terasách). Mění jen masku kráteru.</summary>
        public static int CraterMode = 2;

        /// <summary>Kolo 27: masivy zap/vyp (vyp = generace bitově jako kolo 26). Mění svět – nový seed vypadá jinak než v K26.</summary>
        public static bool MassifEnabled = true;

        /// <summary>Kolo 27: parametry masivů (ladí se A/B v editoru, viz RemoteBridge massifab).</summary>
        public static MassifConfig Massif = new MassifConfig
        {
            // A/B kola 27 (varianta v3, 6 seedů × 32 km): vrcholy 770–850 m místo 410–480 m, souš > 300 m 1,4–4,3 %
            // místo 0,1 %, nížiny < 40 m dál 39–47 % souše, sníh v teplém pásmu 0, moře beze změny pobřeží.
            amp = 650f, period = 11000f, lo = -0.05f, hi = 0.35f, ridgePeriod = 4800f, ridgeOct = 4,
            sharp = 1.5f, basePart = 0.30f, outside = 1.0f, upliftBoost = 0.30f, shape = 0.4f, top = 740f, topRange = 100f,
        };

        /// <summary>Kolo 27: zapíše parametry masivu do GenParams (offsety kanálů 32 a 33).</summary>
        public static void ApplyMassif(ref GenParams p, bool on, in MassifConfig c, int seed)
        {
            p.massifAmp = on ? Mathf.Max(0f, c.amp) : 0f;
            p.massifFreq = Inv(c.period);
            p.massifRidgeFreq = Inv(c.ridgePeriod);
            p.massifRidgeOct = Mathf.Clamp(c.ridgeOct, 1, 8);
            p.massifLo = c.lo;
            p.massifHi = Mathf.Max(c.hi, c.lo + 0.01f);
            p.massifSharp = Mathf.Max(0.2f, c.sharp);
            p.massifBase = Mathf.Clamp01(c.basePart);
            p.massifOutside = Mathf.Clamp01(c.outside);
            p.massifUpliftBoost = Mathf.Max(0f, c.upliftBoost);
            p.massifShape = Mathf.Max(0f, c.shape);
            p.massifTop = c.top;
            p.massifTopRange = Mathf.Max(10f, c.topRange);
            p.offMassif = GenNoise.ChannelOffset(seed, 32);
            p.offMassifRidge = GenNoise.ChannelOffset(seed, 33);
        }

        /// <summary>Kolo 8: umělecká paleta biomů (TerrainPalette.EvaluateArt). Jen za běhu, ne v assetu.</summary>
        public static bool ArtPassEnabled = true;

        /// <summary>
        /// Kolo 14: klimatické regiony (BiomeMath) v barvě terénu i v osazení. Jen za běhu.
        /// Tvar terénu, voda ani hydrologie na tom nezávisí – off vrací barvy a porost kola 13.
        /// </summary>
        public static bool BiomesEnabled = true;

        /// <summary>Kolo 9: měkčí odlesky na vodě a hloubka horské mlhy (jen za běhu).</summary>
        public static bool Art9Enabled = true;

        [Range(0.5f, 8f)] public float riverMouthDepth = 1.5f;

        [Header("Jezera z bezodtokých pánví")]
        [Tooltip("Od jaké hloubky zaplnění se pánev počítá za jezero (metry).")]
        public float hydroLakeMin = 6f;
        [Tooltip("Přes kolik metrů zaplnění navíc dosáhne srovnání dna plné síly. " +
                 "Bez srovnání by z mělkého jezera byly kaluže – detail povrchu kolísá o víc metrů, než je pánev hluboká.")]
        public float hydroLakeFade = 3f;

        [Header("Horská jezera")]
        [Tooltip("Velikost Worley buňky. Musí být větší než 2× maximální poloměr jezera.")]
        public float lakeCellSize = 380f;
        [Range(0f, 1f)] public float lakeChance = 0.18f;
        public float lakeMinY = 42f;
        public float lakeMaxY = 210f;
        [Tooltip("Maximální sklon makro terénu, na kterém ještě jezero vznikne.")]
        public float lakeMaxSlope = 0.14f;
        public float lakeRadiusMin = 28f;
        public float lakeRadiusMax = 74f;
        public float lakeDepth = 9f;
        public float lakeShoreOffset = 1.5f;

        [Header("Tvar jezera")]
        [Tooltip("Deformace břehu jako podíl poloměru. 0 = dokonalý kruh, 0.3 = členité zálivy.")]
        [Range(0f, 0.6f)] public float lakeShapeWarp = 0.30f;
        [Tooltip("Členitost břehu. Vyšší = víc menších zálivů, nižší = pár velkých laloků.")]
        [Range(0.5f, 6f)] public float lakeShapeDetail = 2.2f;
        [Tooltip("Maximální protažení oválu. 1 = kulaté, 2 = výrazně podlouhlé.")]
        [Range(1f, 3f)] public float lakeStretchMax = 1.8f;
        [Tooltip("O kolik metrů nad hladinu smí pánev nechat terén vystoupat. Nula = plochý prstenec přesně na hladině (ten dělá ten kruh).")]
        public float lakeShoreRise = 4f;

        [Header("Převisy a 3D detail (fáze 2)")]
        [Tooltip("Amplituda deformace vzorkovací souřadnice v metrech. Určuje, jak hluboký může být převis. Nesmí přesáhnout okraj sloupcové cache – ten se z toho počítá.")]
        [Range(0f, 12f)] public float overhangAmp = 7f;
        [Tooltip("Perioda deformace. Menší = drobnější, členitější převisy.")]
        public float overhangPeriod = 38f;
        [Tooltip("Síla nevýškopisných boulí v pásu kolem povrchu.")]
        [Range(0f, 1f)] public float squishAmp = 0.35f;
        public float squishPeriod = 26f;
        [Tooltip("Svislý dosah povrchového 3D detailu v metrech.")]
        public float surfaceBand = 9f;

        [Header("Jeskyně – tunely")]
        [Tooltip("Celková síla vyřezávání. 0 = žádné jeskyně.")]
        [Range(0f, 1.5f)] public float caveStrength = 1f;
        [Tooltip("Zaoblení spoje jeskyně a skály. Nízké = ostrá dutina.")]
        [Range(0.05f, 1.5f)] public float caveSmooth = 0.45f;

        [Tooltip("Šířka chodby v METRECH – jmenovitá. Skutečně průchozí je zhruba 85 % z ní. Ověř si to přes „Změřit jeskyně\" na Voxel Preview.")]
        public float tunnelWidth = 11f;
        [Tooltip("Rozteč chodeb. Objem dutin klesá s druhou mocninou – zdvojnásobení periody uvolní podzemí čtyřikrát, šířka zůstane.")]
        public float tunnelPeriod = 300f;
        [Tooltip("Svislá perioda. Vyšší než vodorovná = chodby vedou spíš vodorovně než jako komíny.")]
        public float tunnelPeriodY = 400f;
        [Range(0f, 1.5f)] public float tunnelStrength = 1f;

        [Header("Jeskyně – hlavní tahy")]
        [Tooltip("Šířka široké galerie v METRECH. Druhá, řidší rodina chodeb – protíná tu hustou " +
                 "sama od sebe, takže z úzkých trubek vznikne hierarchie chodba/galerie.")]
        public float tunnelBigWidth = 26f;
        [Tooltip("Rozteč galerií. Musí být řádově delší než u chodeb, jinak sežerou podzemí.")]
        public float tunnelBigPeriod = 620f;
        public float tunnelBigPeriodY = 820f;
        [Range(0f, 1.5f)] public float tunnelBigStrength = 1f;

        [Header("Jeskyně – hloubka")]
        [Tooltip("Od jaké hloubky se dutiny začínají otevírat (metry).")]
        public float caveDeepStart = 30f;
        [Tooltip("V jaké hloubce je otevírání plné (metry).")]
        public float caveDeepFull = 150f;
        [Tooltip("O kolik se v plné hloubce rozšíří chodby. 1 = dvojnásobná šířka.")]
        [Range(0f, 2f)] public float caveDeepWiden = 0.9f;
        [Tooltip("O kolik v plné hloubce klesne práh síní. Nižší práh = síní víc a jsou větší.")]
        [Range(0f, 0.4f)] public float caveDeepHallOpen = 0.16f;
        [Tooltip("Hloubka, pod kterou se už nic neřeže. Zároveň to šetří svislé chunky – " +
                 "pod ní je zaručeně plný kámen, takže se nemusí meshovat.")]
        public float caveMaxDepth = 220f;

        [Header("Jeskyně – síně")]
        [Tooltip("Rozteč velkých dutin. Tunely jimi procházejí samy od sebe, protože žijí ve stejném prostoru.")]
        public float hallPeriod = 260f;
        public float hallPeriodY = 160f;
        [Tooltip("Vyšší práh = síní méně, ale jsou větší.")]
        [Range(0.2f, 0.95f)] public float hallThreshold = 0.74f;
        [Tooltip("Nad 1 se dutina otevře doopravdy doširoka.")]
        [Range(0f, 2f)] public float hallStrength = 1.6f;
        [Tooltip("Od jaké hloubky pod povrchem síně vůbec začínají (metry). Nízká hodnota " +
                 "znamená obří dutinu hned pod loukou a propadlý strop.")]
        public float hallTop = 28f;

        [Header("Jeskyně – propasti")]
        [Tooltip("Šířka průrvy v METRECH.")]
        public float ravineWidth = 16f;
        [Tooltip("Propast je 2D pás protažený svisle, takže její objem klesá jen s první mocninou periody. Musí být proto řádově delší než u tunelů, jinak sežere podzemí sama.")]
        public float ravinePeriod = 2200f;
        [Tooltip("Od jaké hloubky pod povrchem propast začíná. Pod ~15 m se otevře až na povrch a v terénu z ní jsou tenké černé praskliny, ne kaňon.")]
        public float ravineTop = 26f;
        public float ravineBottom = 70f;
        [Range(0f, 1.5f)] public float ravineStrength = 1.1f;

        [Header("Vstupy do jeskyní")]
        [Tooltip("Perioda masky vstupů. Určuje, jak daleko od sebe jsou portály.")]
        public float entrancePeriod = 260f;
        [Tooltip("Jak silně se řeže u povrchu mimo vstupy. 0 = jeskyně jsou úplně uzavřené, 1 = povrch je děravý.")]
        [Range(0f, 1f)] public float entranceMin = 0.05f;
        [Tooltip("Hloubka, od které maska vstupů přestává působit (metry).")]
        public float entranceDepth = 22f;

        [Header("Skalní brány")]
        [Tooltip("Rozteč mřížky umístění. Větší = vzácnější brány.")]
        public float archCellSize = 384f;
        [Range(0f, 0.4f)] public float archChance = 0.06f;
        [Tooltip("Poloměr oblouku a tloušťka jeho trubky v metrech.")]
        public float archRadius = 9f;
        public float archTube = 5f;
        [Tooltip("Minimální sklon makro terénu. Brána potřebuje stěnu, do které se dá prorazit otvor.")]
        public float archSlopeMin = 0.45f;
        public float archMinY = 18f;
        public float archMaxY = 150f;
        [Tooltip("Rozbití CSG hrany šumem, ať brána nevypadá modelovaně.")]
        [Range(0f, 0.5f)] public float archNoise = 0.14f;
        [Tooltip("Zaoblení spoje brány se skálou v metrech. Pod 1 to vypadá jako booleovská operace v modeláři.")]
        [Range(0.3f, 8f)] public float archSmooth = 4f;

        [Header("Spliny")]
        [Tooltip("Continentalness (-1..1) → základní výška v metrech.")]
        public AnimationCurve baseHeight = DefaultBaseHeight();
        [Tooltip("Erosion (-1..1) → násobič amplitudy.")]
        public AnimationCurve ampFromErosion = DefaultAmpErosion();
        [Tooltip("Continentalness (-1..1) → násobič amplitudy. Drží vrcholy ve vnitrozemí.")]
        public AnimationCurve ampFromContinent = DefaultAmpContinent();
        [Tooltip("Peaks & valleys (-1..1) → tvar hřebene.")]
        public AnimationCurve peaksValleys = DefaultPeaksValleys();

        [Tooltip("Počet vzorků LUT na spline. 256 je pod hranicí viditelnosti.")]
        [Range(32, 1024)] public int splineResolution = 256;

        private static float Inv(float period) => period > 0.0001f ? 1f / period : 0f;

        /// <summary>Převod šířky v metrech na práh v jednotkách šumu při dané periodě.</summary>
        private static float NoiseWidth(float widthMeters, float period)
            => period > 0.0001f ? Mathf.Max(0.002f, widthMeters * 2f / period) : 0.05f;

        /// <summary>Sestaví blittable parametry pro joby. Volat z hlavního vlákna.</summary>
        public GenParams ToParams(int seed)
        {
            var p = new GenParams
            {
                seed = seed,
                seaLevel = seaLevel,
                reliefAmp = reliefAmplitude,

                warpFreq = Inv(warpPeriod),
                warpAmp = warpAmp,

                upliftAmp = Mathf.Max(0f, upliftAmplitude),
                upliftFreq = Inv(upliftPeriod),
                upliftOct = upliftOctaves,
                upliftSharp = Mathf.Max(0.2f, upliftSharpness),
                upliftContMin = upliftContinentMin,
                upliftShape = upliftShapeBoost,

                contFreq = Inv(contPeriod),   contOct = contOctaves,
                erosFreq = Inv(erosPeriod),   erosOct = erosOctaves,
                weirdFreq = Inv(weirdPeriod), weirdOct = weirdOctaves,
                tempFreq = Inv(tempPeriod),   humFreq = Inv(humPeriod),
                climateOct = climateOctaves,

                ridgeFreq = Inv(ridgePeriod), ridgeOct = ridgeOctaves, ridgeAmp = ridgeAmp,
                grainFreq = Inv(grainPeriod), grainOct = grainOctaves, grainAmp = grainAmp,

                stepHeight = Mathf.Max(0.5f, stepHeight),
                cliffSharp = cliffSharpness,
                cliffStrength = cliffStrength,
                cliffSlopeMin = cliffSlopeMin,
                cliffSlopeMax = Mathf.Max(cliffSlopeMax, cliffSlopeMin + 0.01f),

                aridCliffBoost = Mathf.Max(1f, aridCliffBoost),
                humidCliffDamp = Mathf.Clamp(humidCliffDamp, 0.05f, 1f),
                aridStepBoost = Mathf.Max(1f, aridStepBoost),
                swampStrength = Mathf.Clamp01(swampStrength),
                aridIncision = Mathf.Max(1f, aridIncision),

                microCell = Mathf.Max(50f, microCell),
                microRadius = Mathf.Clamp(microRadius, 0.05f, 0.45f),
                microBlend = Mathf.Max(1f, microBlend),
                microChance = Mathf.Clamp01(microChance),

                riverMinAccum = Mathf.Max(1f, riverMinAccum),
                riverWidthMin = Mathf.Max(1f, riverWidthMin),
                riverWidthMax = Mathf.Max(riverWidthMax, riverWidthMin),
                riverAccumFull = Mathf.Max(4f, riverAccumFull),
                riverBankRatio = riverBankRatio,
                riverDepthRatio = riverDepthRatio,
                riverDepthMin = riverDepthMin,
                riverDepthMax = Mathf.Max(riverDepthMax, riverDepthMin),
                riverValleyWidth = riverValleyWidth,
                riverValleyFlatten = riverValleyFlatten,
                riverBankHeight = riverBankHeight,
                // Asset uložený před přidáním tohohle pole nemá v YAML klíč, a Unity pak
                // NEPOUŽIJE inicializátor z kódu – dosadí nulu. Nulová hloubka ústí by
                // znamenala, že se koryto u moře vůbec nevyřeže, tedy přesně ta chyba,
                // kterou to má opravit. Proto se nula bere jako "nenastaveno".
                riverMouthDepth = riverMouthDepth <= 0.01f ? 1.5f : riverMouthDepth,
                hydroSill = HydroSillEnabled ? 1f : 0f,
                flowBlend = FlowBlendEnabled ? 1f : 0f,
                softTerrace = SoftTerraceEnabled ? 1f : 0f,
                calmDetail = CalmDetailEnabled ? 1f : 0f,
                overhangYStretch = CalmOverhangYStretch,
                overhangOct = CalmOverhangOct,
                squishOct = CalmSquishOct,
                calmMask = 45,
                hydroLakeMin = Mathf.Max(0f, hydroLakeMin),
                hydroLakeFade = Mathf.Max(0.5f, hydroLakeFade),

                lakeCell = Mathf.Max(32f, lakeCellSize),
                lakeChance = lakeChance,
                lakeMinY = lakeMinY,
                lakeMaxY = lakeMaxY,
                lakeFlatMax = lakeMaxSlope,
                lakeRadiusMin = lakeRadiusMin,
                lakeDepth = lakeDepth,
                lakeShoreOffset = lakeShoreOffset,
                lakeWarpAmp = lakeShapeWarp,
                lakeWarpDetail = lakeShapeDetail,
                lakeStretchMax = Mathf.Max(1f, lakeStretchMax),
                lakeShoreRise = Mathf.Max(0f, lakeShoreRise),

                overhangAmp = overhangAmp,
                overhangFreq = Inv(overhangPeriod),
                squishAmp = squishAmp,
                squishFreq = Inv(squishPeriod),
                surfaceBand = Mathf.Max(0.5f, surfaceBand),

                // Šířky se zadávají v metrech a tady se převádějí na jednotky šumu.
                // fbm se s periodou P mění o ~1 na ~P/4 metrech, takže plná šířka W
                // odpovídá prahu W*2/P.
                tunnelFreqXZ = Inv(tunnelPeriod),
                tunnelFreqY = Inv(tunnelPeriodY),
                tunnelRadius = NoiseWidth(tunnelWidth, tunnelPeriod),
                tunnelStrength = tunnelStrength,
                tunnelBigFreqXZ = Inv(tunnelBigPeriod),
                tunnelBigFreqY = Inv(tunnelBigPeriodY),
                tunnelBigRadius = NoiseWidth(tunnelBigWidth, tunnelBigPeriod),
                tunnelBigStrength = tunnelBigStrength,
                hallFreqXZ = Inv(hallPeriod),
                hallFreqY = Inv(hallPeriodY),
                hallThreshold = hallThreshold,
                hallStrength = hallStrength,
                hallTop = hallTop,
                deepStart = caveDeepStart,
                deepFull = Mathf.Max(caveDeepFull, caveDeepStart + 10f),
                deepWiden = caveDeepWiden,
                deepHallDrop = caveDeepHallOpen,
                caveMaxDepth = Mathf.Max(60f, caveMaxDepth),
                ravineFreq = Inv(ravinePeriod),
                ravineRadius = NoiseWidth(ravineWidth, ravinePeriod),
                ravineTop = ravineTop,
                ravineBottom = Mathf.Max(ravineBottom, ravineTop + 15f),
                ravineStrength = ravineStrength,
                caveStrength = caveStrength,
                caveSmooth = caveSmooth,
                entranceFreq = Inv(entrancePeriod),
                entranceMin = entranceMin,
                entranceDepth = Mathf.Max(8f, entranceDepth),

                archCell = Mathf.Max(64f, archCellSize),
                archChance = archChance,
                archRadius = archRadius,
                archTube = archTube,
                archSlopeMin = archSlopeMin,
                archMinY = archMinY,
                archMaxY = Mathf.Max(archMaxY, archMinY + 10f),
                archNoise = archNoise,
                archSmooth = archSmooth,
            };

            // Deformace břehu posouvá hranici až o lakeShapeWarp poloměru ven, takže do limitu
            // půlky buňky se počítá až tento zvětšený dosah. Jinak by dvě jezera mohla splynout
            // a test 3x3 sousedních buněk by přestal stačit.
            float reachFactor = Mathf.Sqrt(p.lakeStretchMax) * (1f + 1.5f * p.lakeWarpAmp);
            p.lakeRadiusMax = Mathf.Min(lakeRadiusMax, p.lakeCell * 0.45f / reachFactor);
            p.lakeRadiusMin = Mathf.Min(lakeRadiusMin, p.lakeRadiusMax);
            p.lakeReachMax = p.lakeRadiusMax * reachFactor;

            p.offWarpX      = GenNoise.ChannelOffset(seed, 1);
            p.offWarpZ      = GenNoise.ChannelOffset(seed, 2);
            p.offCont       = GenNoise.ChannelOffset(seed, 3);
            p.offEros       = GenNoise.ChannelOffset(seed, 4);
            p.offWeird      = GenNoise.ChannelOffset(seed, 5);
            p.offTemp       = GenNoise.ChannelOffset(seed, 6);
            p.offHum        = GenNoise.ChannelOffset(seed, 7);
            p.offRidge      = GenNoise.ChannelOffset(seed, 8);
            p.offGrain      = GenNoise.ChannelOffset(seed, 9);
            p.offLakeWarpX  = GenNoise.ChannelOffset(seed, 13);
            p.offLakeWarpZ  = GenNoise.ChannelOffset(seed, 14);
            p.offRavine     = GenNoise.ChannelOffset(seed, 15);
            p.offEntrance   = GenNoise.ChannelOffset(seed, 16);
            p.offWarpX3     = GenNoise.ChannelOffset3(seed, 20);
            p.offWarpY3     = GenNoise.ChannelOffset3(seed, 21);
            p.offWarpZ3     = GenNoise.ChannelOffset3(seed, 22);
            p.offSquish     = GenNoise.ChannelOffset3(seed, 23);
            p.offCave1      = GenNoise.ChannelOffset3(seed, 24);
            p.offCave2      = GenNoise.ChannelOffset3(seed, 25);
            p.offHall       = GenNoise.ChannelOffset3(seed, 26);
            p.offArch       = GenNoise.ChannelOffset3(seed, 27);
            p.offMicro      = GenNoise.ChannelOffset3(seed, 28);
            p.offUplift     = GenNoise.ChannelOffset(seed, 29);
            p.offCave3      = GenNoise.ChannelOffset3(seed, 30);
            p.offCave4      = GenNoise.ChannelOffset3(seed, 31);
            ApplyMassif(ref p, MassifEnabled, Massif, seed);   // kolo 27
            p.craterMode = CraterMode;   // kolo 29

            return p;
        }

        /// <summary>Převzorkuje křivky do LUT. Volat z hlavního vlákna, výsledek Dispose()nout.</summary>
        public SplineSet BuildSplines(Allocator allocator)
        {
            int res = Mathf.Clamp(splineResolution, 32, 1024);
            return new SplineSet
            {
                baseC = SplineLUT.FromCurve(baseHeight ?? DefaultBaseHeight(), -1f, 1f, res, allocator),
                ampE  = SplineLUT.FromCurve(ampFromErosion ?? DefaultAmpErosion(), -1f, 1f, res, allocator),
                ampC  = SplineLUT.FromCurve(ampFromContinent ?? DefaultAmpContinent(), -1f, 1f, res, allocator),
                pv    = SplineLUT.FromCurve(peaksValleys ?? DefaultPeaksValleys(), -1f, 1f, res, allocator),
            };
        }

        private void Reset()
        {
            baseHeight = DefaultBaseHeight();
            ampFromErosion = DefaultAmpErosion();
            ampFromContinent = DefaultAmpContinent();
            peaksValleys = DefaultPeaksValleys();
        }

        private static AnimationCurve Linear(params float[] pairs)
        {
            var keys = new Keyframe[pairs.Length / 2];
            for (int i = 0; i < keys.Length; i++)
                keys[i] = new Keyframe(pairs[i * 2], pairs[i * 2 + 1]);

            var c = new AnimationCurve(keys);
            for (int i = 0; i < keys.Length; i++)
            {
                c.SmoothTangents(i, 0f);
            }
            return c;
        }

        /// <summary>Strmý úsek kolem C = -0.11 dělá pláž úzkou. To je hlavní páka rázu pobřeží.</summary>
        public static AnimationCurve DefaultBaseHeight() => Linear(
            -1.00f, -58f,
            -0.45f, -30f,
            -0.19f, -7f,
            -0.11f, 2f,
             0.05f, 11f,
             0.32f, 30f,
             0.62f, 64f,
             1.00f, 118f);

        public static AnimationCurve DefaultAmpErosion() => Linear(
            -1.0f, 1.00f,
            -0.4f, 0.80f,
             0.0f, 0.42f,
             0.4f, 0.16f,
             1.0f, 0.05f);

        public static AnimationCurve DefaultAmpContinent() => Linear(
            -1.00f, 0.15f,
            -0.10f, 0.25f,
             0.15f, 0.70f,
             0.50f, 1.40f,
             1.00f, 1.90f);

        public static AnimationCurve DefaultPeaksValleys() => Linear(
            -1.00f, -1.00f,
            -0.60f, -0.55f,
             0.00f,  0.08f,
             0.55f,  0.55f,
             1.00f,  1.00f);
    }
}
