using System;
using Unity.Collections;
using Unity.Mathematics;
using static Unity.Mathematics.math;

namespace Orivilon.World.Generation.Hydro
{
    /// <summary>
    /// Rozměry a konstanty regionální hydrologie.
    ///
    /// <para><b>Proč zrovna tyhle hodnoty.</b> Rozteč buňky 32 m je shodná se šířkou chunku
    /// při voxelu 1 m – jeden chunkový sloupec tak padne přesně do jedné buňky a odpadá celá
    /// třída chyb o jedničku. Menší buňka by neúměrně zdražila makro průchod (cena roste
    /// s druhou mocninou), větší by neuměla rozlišit koryto: skutečná řeka je 15–40 m široká.</para>
    ///
    /// <para>Region 256 buněk = 8192 m a halo 64 buněk = 2048 m je kompromis mezi režií
    /// a velikostí jedné dávky. Režie je poměr plochy pole k ploše regionu:
    /// 384²/256² = 2,25×. Při regionu 2 km by to bylo 6,25× (skoro všechno by bylo halo),
    /// při 32 km jen 1,56×, ale jeden region by měl milion buněk, trval skoro půl vteřiny
    /// a držel 13 MB. Osm kilometrů je koleno té křivky.</para>
    ///
    /// <para>Rychlost hráče: 8 km se sprintem (10 m/s) přejde za 14 minut, region se staví
    /// desítky milisekund. Předgenerování začne 1500 m před hranicí, což je i při ladicím
    /// letu 200 m/s ještě 7 vteřin náskoku.</para>
    /// </summary>
    public static class HydroWorld
    {
        /// <summary>Rozteč makro mřížky v metrech. Shodná se šířkou chunku při voxelu 1 m.</summary>
        public const float CellSize = 32f;

        /// <summary>Buněk na hranu regionu bez hala.</summary>
        public const int RegionCells = 256;

        /// <summary>
        /// Šířka hala v buňkách. <b>Tohle je poloměr závislosti L celého systému.</b>
        /// Každá veličina se počítá tak, aby závisela jen na okolí do L buněk – proto
        /// vyjde v každém regionu stejně a švy nevznikají. Viz <see cref="HydroRegion"/>.
        /// </summary>
        public const int Halo = 64;

        /// <summary>Hrana pracovního pole včetně hala.</summary>
        public const int ArrayCells = RegionCells + 2 * Halo;      // 384

        /// <summary>
        /// Prstenec, který se kolem regionu ukládá navíc.
        ///
        /// <para>Sloupec dostane JEDEN region – ten, ve kterém leží jeho střed – a všechny
        /// jeho vzorky se pak musí do uloženého pole vejít. Nejširší sloupec je nejhrubší
        /// LOD: 32 m × 2⁴ = 512 m, tedy 8 buněk od středu na každou stranu, plus okraj
        /// sloupcové cache a okno dotazu 5×5. Dvacet buněk (640 m) to pokrývá s rezervou.</para>
        ///
        /// <para>Stojí to málo: 296² × 13 B ≈ 1,1 MB na region. Kdyby byl prstenec těsný,
        /// projevilo by se to jako řeky mizející na hranici regionu – chyba, která se hledá
        /// zle, protože v jednom sloupci vypadá jako náhoda.</para>
        /// </summary>
        public const int StoredRing = 20;

        /// <summary>Hrana uloženého pole.</summary>
        public const int StoredCells = RegionCells + 2 * StoredRing;   // 296

        /// <summary>Hrana regionu v metrech.</summary>
        public const float RegionSize = RegionCells * CellSize;        // 8192

        /// <summary>Značka „tady neteče nic".</summary>
        public const byte NoFlow = 255;

        /// <summary>Region, do kterého bod patří.</summary>
        public static int2 RegionOf(float2 world)
            => (int2)floor(world / RegionSize);

        /// <summary>Světová pozice buňky (0,0) PRACOVNÍHO pole daného regionu.</summary>
        public static float2 ArrayOrigin(int2 region)
            => (float2)region * RegionSize - Halo * CellSize;

        /// <summary>Světová pozice buňky (0,0) ULOŽENÉHO pole daného regionu.</summary>
        public static float2 StoredOrigin(int2 region)
            => (float2)region * RegionSize - StoredRing * CellSize;

        /// <summary>Posun jednoho z osmi směrů D8.</summary>
        public static int2 DirOffset(int d)
        {
            switch (d)
            {
                case 0:  return new int2( 1,  0);
                case 1:  return new int2( 1,  1);
                case 2:  return new int2( 0,  1);
                case 3:  return new int2(-1,  1);
                case 4:  return new int2(-1,  0);
                case 5:  return new int2(-1, -1);
                case 6:  return new int2( 0, -1);
                default: return new int2( 1, -1);
            }
        }

        /// <summary>Opačný převod – z posunu na index směru. Mimo osm sousedů vrací NoFlow.</summary>
        public static byte DirFromDelta(int dx, int dy)
        {
            if (dx ==  1 && dy ==  0) return 0;
            if (dx ==  1 && dy ==  1) return 1;
            if (dx ==  0 && dy ==  1) return 2;
            if (dx == -1 && dy ==  1) return 3;
            if (dx == -1 && dy ==  0) return 4;
            if (dx == -1 && dy == -1) return 5;
            if (dx ==  0 && dy == -1) return 6;
            if (dx ==  1 && dy == -1) return 7;
            return NoFlow;
        }
    }

    /// <summary>
    /// Hotová hydrologie jednoho regionu. Uložené pole je region plus prstenec
    /// <see cref="HydroWorld.StoredRing"/>; halo se po výpočtu zahazuje.
    ///
    /// <para><b>Proč tu nejsou švy.</b> Každá veličina je funkcí makro výšky v okolí do
    /// poloměru L = <see cref="HydroWorld.Halo"/>. Makro výška je čistá funkce světové
    /// pozice a seedu, tedy všude stejná. Region se počítá nad polem region + halo L,
    /// takže každá jeho vnitřní buňka má kolem sebe plných L platných dat. Dva regiony
    /// proto pro tutéž buňku dostanou bit po bitu shodný výsledek – <b>není žádná okrajová
    /// podmínka, na které by se musely dohodnout, a tedy ani co sešívat.</b></para>
    ///
    /// <para>Jediné, co poloměr L neuzavírá, je akumulace: povodí může být větší než halo.
    /// Řeší se nasycením – šířka koryta se nasytí při <c>accumFull</c> buňkách povodí,
    /// což je řádově míň, než kolik jich leží uvnitř hala, takže každý region napočítá
    /// na skutečné řece nasycenou hodnotu. Rozejít se mohou jen pramínky pod prahem,
    /// kde je rozdíl jedné buňky neviditelný.</para>
    /// </summary>
    public struct HydroRegion : IDisposable
    {
        public int2 coord;

        /// <summary>Světová pozice buňky (0,0) uloženého pole.</summary>
        public float2 origin;

        /// <summary>Výška po zaplnění bezodtokých pánví. Podél koryta je to hladina vody.</summary>
        public NativeArray<float> filled;

        /// <summary>Surová makro výška – dno, na které se voda posadí.</summary>
        public NativeArray<float> raw;

        /// <summary>Akumulace toku v BUŇKÁCH povodí. Násobením plochou buňky vyjdou m².</summary>
        public NativeArray<float> accum;

        /// <summary>Směr odtoku 0–7, nebo <see cref="HydroWorld.NoFlow"/>.</summary>
        public NativeArray<byte> dir;

        public bool IsCreated => filled.IsCreated;

        public long ApproxBytes => IsCreated
            ? filled.Length * (4L + 4L + 4L + 1L)
            : 0L;

        public void Dispose()
        {
            if (filled.IsCreated) filled.Dispose();
            if (raw.IsCreated) raw.Dispose();
            if (accum.IsCreated) accum.Dispose();
            if (dir.IsCreated) dir.Dispose();
        }
    }

    /// <summary>Výsledek dotazu na nejbližší koryto.</summary>
    public struct HydroFlow
    {
        /// <summary>Vzdálenost od osy koryta v metrech. Když se nic nenašlo, je to velké číslo.</summary>
        public float distance;

        /// <summary>Šířka koryta v metrech v tom místě.</summary>
        public float width;

        /// <summary>Hladina vody v metrech, interpolovaná PO PROUDU – nikdy neteče do kopce.</summary>
        public float waterY;

        /// <summary>Akumulace toku v buňkách povodí. Pro ladění a pro volbu šířky.</summary>
        public float accum;

        /// <summary>Hloubka zaplnění pánve v tom bodě. Nad nulou je to jezero, ne řeka.</summary>
        public float lakeDepth;

        /// <summary>Hladina zaplněné pánve. Platná jen tam, kde <see cref="lakeDepth"/> &gt; 0.</summary>
        public float lakeSurface;

        /// <summary>
        /// Hladina jezera, do jehož zaplněné pánve bod patří (nejvyšší zaplněná ze čtyř buněk bilineárního čtení) –
        /// na rozdíl od <see cref="lakeSurface"/> je to KONSTANTA přes celé jezero.
        /// Bilineární <see cref="lakeSurface"/> se na okraji pánve míchá se surovou výškou
        /// břehových buněk, takže by hladina u břehu stoupala.
        /// </summary>
        public float lakeLevel;

        /// <summary>Surová (nezaplněná) makro výška pod bodem, bilineárně.</summary>
        public float lakeBed;

        /// <summary>
        /// Spád hladiny toku podél osy (m na m). Na peřejích a vodopádech je velký – břehy
        /// tam musí být vyšší, jinak prudká hladina a stejně prudký břeh vedou izolinii
        /// skoro souběžně a okraj vody se roztřepí do zubů.
        /// </summary>
        public float waterSlope;

        /// <summary>
        /// Hladina, šířka a vzdálenost toku smíchané přes VŠECHNY blízké úsečky – jen pro
        /// údolní mísu, kde nesmí skákat. Koryto a hladina používají waterY/width/distance.
        /// </summary>
        public float valleyY, valleyWidth, valleyDistance;

        /// <summary>
        /// Kolo 10: směr toku po proudu (jednotkový, xz). Průměr směrů úseček téhož toku vážený
        /// stejně jako hladina, takže se v ohybech otáčí plynule. Nulový, když se nic nenašlo.
        /// </summary>
        public float2 dir;

        /// <summary>
        /// Kolo 11: spád pro rychlost vzoru na hladině. Bez plynulého směru = <see cref="waterSlope"/>;
        /// s ním vážený stejně symetricky jako směr, takže rychlost u soutoku a na hraně peřeje
        /// neskáče. Terén a hladina ho nečtou (ty dál používají waterSlope).
        /// </summary>
        public float flowSlope;

        public bool Found => distance < 1e8f;

        public static HydroFlow None => new HydroFlow { distance = 1e9f, lakeLevel = -100000f };
    }
}
