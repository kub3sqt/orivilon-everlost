using System;
using Unity.Collections;
using Unity.Mathematics;

namespace Orivilon.World.Generation
{
    /// <summary>
    /// Blittable kopie WorldGenSettings pro joby. Žádné managed typy, žádné křivky –
    /// ty jdou zvlášť v SplineSet. Offsety kanálů jsou předpočítané, aby se v jobu nehashovalo.
    /// </summary>
    [Serializable]
    public struct GenParams
    {
        public int seed;
        public float seaLevel;

        // ── domain warp ────────────────────────────────────────────────
        public float warpFreq, warpAmp;

        /// <summary>
        /// Základní členitost v METRECH. Spliny ampE a ampC jsou bezrozměrné násobiče –
        /// teprve tohle z nich dělá výšku. Bez toho by peaks &amp; valleys přidávaly
        /// nejvýš pár metrů a celý reliéf by dělala jen continentalness.
        /// </summary>
        public float reliefAmp;

        // ── tektonický zdvih (pohoří) ──────────────────────────────────
        /// <summary>Amplituda pohoří v METRECH. Přičítá se k makro výšce, nenásobí ji.</summary>
        public float upliftAmp;
        public float upliftFreq; public int upliftOct;

        /// <summary>Mocnina, kterou se zužují hřebenová pásma. 1 = široké kupy, 3 = ostré řetězy.</summary>
        public float upliftSharp;

        /// <summary>Od jaké continentalness se pohoří vůbec smí zvedat. Drží hory ve vnitrozemí.</summary>
        public float upliftContMin;

        /// <summary>O kolik zdvih zvětšuje bezrozměrný shape, tedy hrubost povrchového detailu.</summary>
        public float upliftShape;

        // ── kolo 27: monumentální masivy ──────────────────────────────
        /// <summary>
        /// Kolo 27: výška masivu v METRECH (0 = vypnuto, generace bitově jako kolo 26). Není v assetu –
        /// z <see cref="WorldGenSettings.Massif"/>. Masiv = hladká regionální maska × vlastní hřebenové pole.
        /// </summary>
        public float massifAmp;
        /// <summary>Frekvence regionální masky (1/perioda) a hřebenů uvnitř masivu.</summary>
        public float massifFreq, massifRidgeFreq; public int massifRidgeOct;
        /// <summary>Práh masky (fbm −1..1): od Lo začíná náběh, od Hi plná síla.</summary>
        public float massifLo, massifHi;
        /// <summary>Mocnina hřebenů masivu; podíl „kořene“ (zdvih celé plochy masivu, údolí zůstávají vysoko).</summary>
        public float massifSharp, massifBase;
        /// <summary>Násobič klasického zdvihu mimo masiv (vzácnější hory) a jeho posílení uvnitř.</summary>
        public float massifOutside, massifUpliftBoost;
        /// <summary>O kolik masiv zhrubne povrchový detail (jako upliftShape).</summary>
        public float massifShape;
        /// <summary>Měkký strop makro výšky masivu (m) a pásmo, přes které se k němu přimyká.</summary>
        public float massifTop, massifTopRange;

        // ── makro pole ─────────────────────────────────────────────────
        public float contFreq;  public int contOct;
        public float erosFreq;  public int erosOct;
        public float weirdFreq; public int weirdOct;
        public float tempFreq, humFreq; public int climateOct;

        // ── detail povrchu ─────────────────────────────────────────────
        public float ridgeFreq; public int ridgeOct; public float ridgeAmp;
        public float grainFreq; public int grainOct; public float grainAmp;

        // ── terasování / útesy ─────────────────────────────────────────
        public float stepHeight, cliffSharp, cliffSlopeMin, cliffSlopeMax, cliffStrength;

        // ── klimatická identita terénu ─────────────────────────────────
        //
        // Teplota a vlhkost se do TVARU terénu doteď nepromítaly vůbec – četla je jen
        // barva a osazení. Poušť proto měla přesně stejný reliéf jako les a lišila se
        // jen odstínem. Tyhle čtyři hodnoty dávají klimatu vliv na tvar: v suchu se
        // terasy zesílí a zvýší (mesy a kaňony), ve vlhku se potlačí (měkké zalesněné
        // svahy) a v horkých vlhkých nížinách vznikne bažinatá plošina.

        /// <summary>Násobič síly teras v plně suchém a horkém kraji.</summary>
        public float aridCliffBoost;

        /// <summary>Násobič síly teras v plně vlhkém kraji. Pod 1 = měkké svahy.</summary>
        public float humidCliffDamp;

        /// <summary>Násobič výšky stupně v suchu. Vyšší stupeň = mesa místo schůdků.</summary>
        public float aridStepBoost;

        /// <summary>Jak silně se horké vlhké nížiny srovnají do bažiny. 0 = vypnuto.</summary>
        public float swampStrength;

        /// <summary>Násobič hloubky zářezu koryta v suchu. Z řeky v plató dělá kaňon.</summary>
        public float aridIncision;

        // ── mikro-biomy ────────────────────────────────────────────────
        public float microCell, microRadius, microBlend, microChance;

        /// <summary>Nastavení mikro-biomů pohromadě. Stejná data, jen v podobě pro Sample.</summary>
        public MicroParams Micro => new MicroParams
        {
            cell = microCell,
            radius = microRadius,
            blend = microBlend,
            chance = microChance,
            seed = seed,
        };

        // ── řeky (regionální D8 síť) ───────────────────────────────────
        //
        // Prahy „o kolik jsem pod okolím" jsou pryč i s celou údolní metodou. Koryto se
        // teď bere z akumulace toku na makro-mapě, takže se nezadává, KDE řeka je,
        // ale JAK velký tok už se počítá za řeku.

        /// <summary>Od jaké akumulace (počtu buněk povodí) je buňka koryto.</summary>
        public float riverMinAccum;

        /// <summary>Šířka pramene a veletoku v metrech.</summary>
        public float riverWidthMin, riverWidthMax;

        /// <summary>
        /// Plocha povodí v buňkách, při které je šířka plná. Zároveň to je nasycení,
        /// které drží síť bez švů mezi regiony – musí být řádově méně, než kolik buněk
        /// leží uvnitř hala.
        /// </summary>
        public float riverAccumFull;

        /// <summary>Šířka břehového náběhu jako podíl poloviny koryta. Dělá U profil místo V.</summary>
        public float riverBankRatio;

        /// <summary>Hloubka koryta jako podíl šířky. Skutečná řeka má kolem 0,1.</summary>
        public float riverDepthRatio;

        /// <summary>Mez hloubky koryta v metrech, ať potok není propast a veletok kaluž.</summary>
        public float riverDepthMin, riverDepthMax;

        /// <summary>Šířka údolní mísy jako násobek šířky koryta.</summary>
        public float riverValleyWidth;

        /// <summary>Jak moc se údolní mísa srovnává k hladině.</summary>
        public float riverValleyFlatten;

        /// <summary>O kolik metrů nad hladinou leží dno údolní mísy.</summary>
        public float riverBankHeight;

        /// <summary>
        /// Hloubka koryta v ústí, v metrech. Zároveň strop toho, jak hluboko smí řeka
        /// zaříznout mořské dno – bez něj by se do šelfu vyryly příkopy, protože akumulace
        /// toku se u pobřeží sbíhá do nejsilnějších hodnot celé mapy.
        /// </summary>
        public float riverMouthDepth;

        /// <summary>
        /// Kolo 7: práh na horní straně hydrologického schodu (1 = zapnuto, 0 = stav kola 6).
        /// Není v assetu – nastavuje se z <see cref="WorldGenSettings.HydroSillEnabled"/>, za běhu
        /// jen pro A/B měření (<c>/voda prah on|off</c>).
        /// </summary>
        public float hydroSill;

        /// <summary>
        /// Kolo 11: 1 = směr toku (jen pro vzor na hladině) se u soutoků a ohybů stáčí plynule
        /// přes pás podle šířky koryta, nezávisle na tom, která úsečka je nejbližší
        /// (<see cref="Hydro.HydroSampler"/>.BlendDir). Hladina ani terén se nemění. Není v assetu.
        /// </summary>
        public float flowBlend;

        /// <summary>
        /// Kolo 12: 1 = slabé terasování má měkký náběh stupně (bez ostrých vrstevnic na loukách
        /// a svazích), ostrý zůstává jen u silného (mesy). Viz WorldGenMath.EvalSurface, krok 8.
        /// Není v assetu – z WorldGenSettings.SoftTerraceEnabled.
        /// </summary>
        public float softTerrace;

        /// <summary>
        /// Kolo 12b: 1 = klidný povrchový 3D detail. Převisový warp má jen jednu oktávu a ve
        /// svislém směru delší periodu (overhangYStretch), povrchové boule mají squishOct oktáv.
        /// Bez toho warp na běžném svahu skládá vrstevnice do sítě tenkých vrásek a rýh
        /// (svislá změna warpu je na svahu 1 : sklon zkrácená). Není v assetu –
        /// z WorldGenSettings.CalmDetailEnabled.
        /// </summary>
        public float calmDetail;
        /// <summary>Kolo 12b: násobič svislé frekvence převisového warpu v klidném režimu (&lt;1 = delší).</summary>
        public float overhangYStretch;
        /// <summary>Kolo 12b: počet oktáv převisového warpu a povrchových boulí v klidném režimu.</summary>
        public int overhangOct, squishOct;
        /// <summary>
        /// Kolo 12b: které části klidného režimu platí (bity, jen pro A/B): 1 warp a boule,
        /// 4 jeskyně u povrchu, 8 3D vrstva u švů LOD, 32 měkká terasa. Výchozí 45 = vše.
        /// (Bity 2 a 16 jsou volné: zaoblený ridged a širší brána u vody se zkoušely
        /// a vyřazeným – ridged nebyl příčinou a měnil šev LOD3|LOD4.)
        /// </summary>
        public int calmMask;
        public bool Calm(int bit) => calmDetail > 0.5f && (calmMask & bit) != 0;

        // ── jezera z bezodtokých pánví ─────────────────────────────────
        /// <summary>Od jaké hloubky zaplnění se pánev počítá za jezero (metry).</summary>
        public float hydroLakeMin;

        /// <summary>Přes kolik metrů zaplnění navíc dosáhne srovnání dna plné síly.</summary>
        public float hydroLakeFade;

        // ── jezera ─────────────────────────────────────────────────────
        public float lakeCell, lakeChance, lakeMinY, lakeMaxY, lakeFlatMax;
        public float lakeRadiusMin, lakeRadiusMax, lakeDepth, lakeShoreOffset;

        /// <summary>Amplituda deformace břehu jako podíl poloměru. 0 = kruh.</summary>
        public float lakeWarpAmp;

        /// <summary>Frekvence deformace relativně k poloměru. Vyšší = členitější břeh.</summary>
        public float lakeWarpDetail;

        /// <summary>Maximální protažení oválu. 1 = kruh.</summary>
        public float lakeStretchMax;

        /// <summary>O kolik metrů nad hladinou přestává pánev ořezávat terén (§ organický břeh).</summary>
        public float lakeShoreRise;

        /// <summary>Nejzazší dosah jezera včetně deformace – pro rozsah prohledávaných buněk.</summary>
        public float lakeReachMax;

        // ── 3D tvarosloví (fáze 2) ─────────────────────────────────────
        /// <summary>Amplituda 3D warpu v metrech. Určuje, jak hluboký může být převis.</summary>
        public float overhangAmp;
        public float overhangFreq;

        /// <summary>Síla nevýškopisných boulí v pásu kolem povrchu.</summary>
        public float squishAmp, squishFreq, surfaceBand;

        // jeskyně – poloměry jsou v jednotkách šumu, počítá je WorldGenSettings z metrů
        public float tunnelFreqXZ, tunnelFreqY, tunnelRadius, tunnelStrength;

        /// <summary>Druhá, řidší a mnohem širší rodina chodeb – hlavní tahy podzemí.</summary>
        public float tunnelBigFreqXZ, tunnelBigFreqY, tunnelBigRadius, tunnelBigStrength;

        public float hallFreqXZ, hallFreqY, hallThreshold, hallStrength;

        /// <summary>Od jaké hloubky pod povrchem se síně vůbec otevírají (metry).</summary>
        public float hallTop;

        // ── otevírání podzemí s hloubkou ───────────────────────────────
        /// <summary>Hloubkové okno, ve kterém dutiny přecházejí z chodeb na síně (metry).</summary>
        public float deepStart, deepFull;

        /// <summary>Relativní rozšíření chodeb v plné hloubce. 1 = dvojnásobek.</summary>
        public float deepWiden;

        /// <summary>O kolik klesne práh síní v plné hloubce. Nižší práh = síní víc a jsou větší.</summary>
        public float deepHallDrop;

        /// <summary>
        /// Hloubka, pod kterou se už neřeže nic. Pod ní je zaručeně plný kámen, takže se
        /// ty chunky vůbec nemusí meshovat – hluboký svět tím nic nestojí.
        /// </summary>
        public float caveMaxDepth;
        public float ravineFreq, ravineRadius, ravineTop, ravineBottom, ravineStrength;
        public float caveStrength, caveSmooth;
        public float entranceFreq, entranceMin, entranceDepth;

        // skalní brány
        public float archCell, archChance, archRadius, archTube;
        public float archSlopeMin, archMinY, archMaxY, archNoise, archSmooth;

        // ── předpočítané offsety kanálů ────────────────────────────────
        public float2 offWarpX, offWarpZ, offCont, offEros, offWeird;
        public float2 offTemp, offHum, offRidge, offGrain;
        public float2 offLakeWarpX, offLakeWarpZ;
        public float2 offRavine, offEntrance, offUplift, offMassif, offMassifRidge;
        public float3 offWarpX3, offWarpY3, offWarpZ3, offSquish, offCave1, offCave2, offHall, offArch;
        public float3 offCave3, offCave4;

        /// <summary>Offset šumu pro mikro-variace barvy fasety. Čte ho jen marching cubes.</summary>
        public float3 offMicro;

        /// <summary>Kolo 29: meteorický kráter – 0 vyp (jen osazení), 1 v EvalMacro (pilotní A/B), 2 na povrchu po terasách (výchozí).</summary>
        public int craterMode;
    }

    /// <summary>
    /// Jedno jezero. Hladina i poloměr závisí VÝHRADNĚ na ID Worley buňky,
    /// takže se na nich dva sousední chunky shodnou bez jakékoli komunikace.
    /// </summary>
    public struct LakeBody
    {
        public float2 center;
        public float radius;
        public float waterY;

        /// <summary>Natočení oválu v radiánech.</summary>
        public float angle;

        /// <summary>Protažení oválu. Vždy &gt;= 1, aby deformace nikdy nezvětšila dosah jezera.</summary>
        public float stretch;

        public int active;
    }

    /// <summary>
    /// Jedna skalní brána. Umístění i tvar závisí výhradně na ID buňky pevné mřížky,
    /// takže se na ní shodne libovolný chunk bez komunikace – stejný princip jako u jezer.
    /// </summary>
    public struct ArchBody
    {
        public float2 center;
        /// <summary>Vodorovná osa otvoru – směr, kterým se branou prochází.</summary>
        public float2 axis;
        public float baseY;
        public float radius;
        public float tube;
        public int active;
    }

    /// <summary>
    /// Výstup sloupcového (2D) průchodu. Sdílí ho všechny svislé chunky jednoho sloupce.
    /// Indexace je vždy <c>i = z * side + x</c>, světová pozice <c>origin + (x, z) * step</c>.
    /// </summary>
    public struct ColumnField : IDisposable
    {
        public float2 origin;
        public float step;
        public int side;

        /// <summary>Low-pass výška – jen spliny, bez detailu. Řeky i jezera se řežou vůči ní.</summary>
        public NativeArray<float> macroY;

        /// <summary>Finální výška povrchu po detailu, řekách, jezerech a terasování.</summary>
        public NativeArray<float> surfY;

        /// <summary>Kolo 24: výška před šitím LOD švu (jen LOD0, jinak nevytvořeno).</summary>
        public NativeArray<float> surfRaw;

        /// <summary>
        /// Bezrozměrný násobič členitosti (ampE × ampC). NENÍ to výška v metrech –
        /// tou se stane až po vynásobení <see cref="GenParams.reliefAmp"/>.
        /// Detail povrchu se škáluje právě tímhle.
        /// </summary>
        public NativeArray<float> shape;

        public NativeArray<float> cont;
        public NativeArray<float> eros;
        public NativeArray<float> pv;
        public NativeArray<float> temp;
        public NativeArray<float> hum;

        /// <summary>Sklon makro terénu (bezrozměrný, |grad macroY|).</summary>
        public NativeArray<float> slope;

        /// <summary>Síla terasování v bodě – pro pozdější maskování 3D vrstvy.</summary>
        public NativeArray<float> cliff;

        /// <summary>Síla koryta 0–1 po aplikaci bran.</summary>
        public NativeArray<float> riverCore;

        /// <summary>Hladina řeky v metrech (platná jen tam, kde riverCore &gt; 0).</summary>
        public NativeArray<float> riverY;

        /// <summary>Kolo 10: směr toku řeky po proudu (xz, jednotkový; nula mimo řeku).</summary>
        public NativeArray<float2> flowDir;

        /// <summary>Kolo 10: spád hladiny toku (m/m) – síla proudu pro vizuál vody.</summary>
        public NativeArray<float> flowSlope;

        /// <summary>Hladina jezera v metrech, nebo <see cref="NoLake"/> mimo jezero.</summary>
        public NativeArray<float> lakeY;

        /// <summary>
        /// Hladina NEJBLIZSI vody v metrech - moře, koryto nebo jezero. Slouží jen k barvení
        /// břehů pískem, ne k tvarování terénu.
        ///
        /// <para>Mimo dosah vody se sem NEZAPISUJE sentinel, ale <c>surfY - 1000</c>. Pole se
        /// totiž v barvicím jobu čte bilineárně a sentinel typu -100000 by se s platnou
        /// hladinou smíchal na nesmyslné mezihodnoty, které by přesně v pásu písku vytvořily
        /// prstenec kolem každé vodní plochy. Konečná hodnota hluboko pod terénem se
        /// interpoluje bez artefaktu a dá vždycky "vysoko nad vodou", tedy žádný písek.</para>
        /// </summary>
        public NativeArray<float> shoreY;

        /// <summary>
        /// Jak daleko je bod od vody, 0–1: 0 pod hladinou a těsně nad ní, 1 od ~6 m nad
        /// nejbližší vodou dál. SPOJITÁ funkce polohy (na rozdíl od <see cref="shoreY"/>,
        /// které na hranici dosahu řeky skáče) – smí se jí proto násobit 3D vrstva hustoty.
        /// Vypíná u vody převisy, boule a jeskyně, aby terén přesně seděl na výškovém poli,
        /// ze kterého se staví hladina.
        /// </summary>
        public NativeArray<float> waterGate;

        /// <summary>Jezera překrývající tento výřez (včetně okrajových buněk).</summary>
        public NativeArray<LakeBody> lakes;

        /// <summary>
        /// Maska pro 3D vrstvu: kde smí vzniknout převis a nevýškopisná boule.
        /// Nízká eroze a strmý svah. Počítá se ve 2D průchodu, aby ji 3D job jen četl.
        /// </summary>
        public NativeArray<float> overhang;

        /// <summary>Skalní brány překrývající výřez.</summary>
        public NativeArray<ArchBody> arches;
        public int2 archCellMin;
        public int archCellsX, archCellsZ;

        /// <summary>[0] = min, [1] = max výšky povrchu. Slouží k prořezávání svislých chunků.</summary>
        public NativeArray<float> bounds;

        /// <summary>Sentinel pro "tady není jezero".</summary>
        public const float NoLake = -100000f;

        public float MinHeight => bounds.IsCreated ? bounds[0] : 0f;
        public float MaxHeight => bounds.IsCreated ? bounds[1] : 0f;

        public bool IsCreated => surfY.IsCreated;

        /// <summary>
        /// Kolik nativní paměti tenhle sloupec drží. Sloupcová pole jsou největší skrytá
        /// položka rozpočtu, takže se hodí je umět změřit, ne odhadovat.
        /// </summary>
        public long ApproxBytes
        {
            get
            {
                if (!surfY.IsCreated) return 0;

                long n = surfY.Length;
                long floats = n * 16;                                 // pole na mřížce sloupce
                if (bounds.IsCreated) floats += bounds.Length;
                if (surfRaw.IsCreated) floats += surfRaw.Length;

                long bytes = floats * sizeof(float);
                if (lakes.IsCreated) bytes += lakes.Length * 32L;
                if (arches.IsCreated) bytes += arches.Length * 48L;
                return bytes;
            }
        }

        /// <summary>Světová pozice mřížkového bodu.</summary>
        public float2 WorldAt(int x, int z) => origin + new float2(x, z) * step;

        public void Dispose()
        {
            if (macroY.IsCreated)    macroY.Dispose();
            if (surfY.IsCreated)     surfY.Dispose();
            if (surfRaw.IsCreated)   surfRaw.Dispose();
            if (shape.IsCreated)     shape.Dispose();
            if (cont.IsCreated)      cont.Dispose();
            if (eros.IsCreated)      eros.Dispose();
            if (pv.IsCreated)        pv.Dispose();
            if (temp.IsCreated)      temp.Dispose();
            if (hum.IsCreated)       hum.Dispose();
            if (slope.IsCreated)     slope.Dispose();
            if (cliff.IsCreated)     cliff.Dispose();
            if (riverCore.IsCreated) riverCore.Dispose();
            if (riverY.IsCreated)    riverY.Dispose();
            if (flowDir.IsCreated)   flowDir.Dispose();
            if (flowSlope.IsCreated) flowSlope.Dispose();
            if (lakeY.IsCreated)     lakeY.Dispose();
            if (shoreY.IsCreated)    shoreY.Dispose();
            if (waterGate.IsCreated) waterGate.Dispose();
            if (lakes.IsCreated)     lakes.Dispose();
            if (overhang.IsCreated)  overhang.Dispose();
            if (arches.IsCreated)    arches.Dispose();
            if (bounds.IsCreated)    bounds.Dispose();
        }
    }
}
