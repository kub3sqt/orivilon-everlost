namespace Orivilon.World.Generation
{
    /// <summary>Stav položky katalogu biomů.</summary>
    public enum BiomeStatus : byte
    {
        /// <summary>Ve hře: má váhu v <see cref="BiomeMath.Weights"/>, barvu terénu i porost.</summary>
        Implemented = 0,
        /// <summary>Jen v katalogu: pásmo a klimatický cíl jsou určené, ve světě zatím není.</summary>
        Planned = 1,
    }

    /// <summary>Jedna položka katalogu (čistá data, žádné odkazy na assety).</summary>
    public readonly struct BiomeEntry
    {
        public readonly string id;          // ASCII klíč pro konzoli (/biome tp &lt;id&gt;)
        public readonly string name;        // český název
        public readonly ClimateBand band;   // klimatické pásmo (horké/teplé/mírné/chladné/podzemí/pobřeží)
        public readonly BiomeStatus status;
        public readonly int region;         // BiomeRegion u implementovaných, jinak -1
        public readonly float tMin, tMax, hMin, hMax; // klimatický cíl (posunutá t/h) pro budoucí váhy
        public readonly string note;

        public BiomeEntry(string id, string name, ClimateBand band, BiomeStatus status, int region,
                          float tMin, float tMax, float hMin, float hMax, string note)
        {
            this.id = id; this.name = name; this.band = band; this.status = status; this.region = region;
            this.tMin = tMin; this.tMax = tMax; this.hMin = hMin; this.hMax = hMax; this.note = note;
        }

        public bool IsImplemented => status == BiomeStatus.Implemented && region >= 0;
    }

    /// <summary>
    /// Kolo 14b: dlouhodobý katalog biomů schválený uživatelem. Implementované položky odkazují
    /// na <see cref="BiomeRegion"/>; plánované nesou jen pásmo a klimatický cíl, aby se daly
    /// později přidat jako další váha ve <see cref="BiomeMath.Weights"/> bez změny rozhraní.
    ///
    /// <para><b>Pravidlo sousedství:</b> horké a chladné pásmo nikdy přímo nesousedí. Zajišťují to
    /// prahy <see cref="BiomeMath.HotLo"/> … <see cref="BiomeMath.ColdHi"/> (mezi nimi vždy teplé
    /// a mírné pásmo) a strop výškového ochlazení <see cref="BiomeMath.LapseMax"/>. Ověřuje se
    /// konzolí <c>/biome klima</c>. Nový biom se smí přidat jen do svého pásma.</para>
    ///
    /// <para><b>Podzemí</b> (krystalové jeskyně) je samostatná geologická vrstva – nikdy povrchový
    /// soused; povrchový region nad ní se nemění.</para>
    ///
    /// <para><b>Pořadí položek se nesmí měnit</b> (index = stabilní číslo pro diagnostiku); nové jen na konec.</para>
    /// </summary>
    public static class BiomeCatalog
    {
        private const BiomeStatus I = BiomeStatus.Implemented, P = BiomeStatus.Planned;
        private const ClimateBand Hot = ClimateBand.Hot, Warm = ClimateBand.Warm, Mild = ClimateBand.Mild,
                                  Cold = ClimateBand.Cold, Under = ClimateBand.Underground, Coast = ClimateBand.Coastal;

        public static readonly BiomeEntry[] All =
        {
            // ── implementováno (kolo 14) ──
            new BiomeEntry("louka",   "louka s háji",          Mild, I, (int)BiomeRegion.Meadow, 0.42f, 0.58f, 0.38f, 1f,  "dosavadní krajina"),
            new BiomeEntry("les",     "jehličnatý (boreální) les", Cold, I, (int)BiomeRegion.Boreal, 0f, 0.42f, 0.42f, 1f, "smrky, pařezy, světliny"),
            new BiomeEntry("briza",   "březový / podzimní háj", Mild, I, (int)BiomeRegion.Birch,  0.42f, 0.58f, 0.38f, 1f,  "podzim ve shlucích"),
            new BiomeEntry("step",    "suchá step a savana",   Warm, I, (int)BiomeRegion.Steppe, 0.50f, 1f, 0f, 0.65f,    "akácie, suchá tráva"),
            new BiomeEntry("poust",   "poušť",                 Hot,  I, (int)BiomeRegion.Desert, 0.60f, 1f, 0f, 0.48f,    "duny, kaktusy, kameny"),
            new BiomeEntry("mesa",    "mesa / badlands",       Hot,  I, (int)BiomeRegion.Mesa,   0.54f, 1f, 0f, 0.55f,    "terasy jen tam, kde terén terasuje v suchu"),
            new BiomeEntry("mokrad",  "mokřad",                Mild, I, (int)BiomeRegion.Swamp,  0.42f, 0.60f, 0.47f, 1f, "nížiny u vody, rákos"),
            new BiomeEntry("tundra",  "tundra (a výškové pásmo)", Cold, I, (int)BiomeRegion.Tundra, 0f, 0.42f, 0f, 0.48f, "zakrslé keře, suť; sníh kreslí paleta"),

            // ── plánováno (katalog uživatele); kolo 16 z něj implementovalo 8 položek (I), kolo 19 dalších 6 ──
            new BiomeEntry("vulkan",      "vulkanická oblast",          Hot,   I, (int)BiomeRegion.Volcanic, 0.60f, 1f, 0f, 0.55f, "kolo 19: horké suché vysočiny, láva, popel"),
            new BiomeEntry("kvetouci",    "kvetoucí louky",             Mild,  I, (int)BiomeRegion.Flowers, 0.45f, 0.58f, 0.45f, 0.75f, "podtyp louky"),
            new BiomeEntry("bambus",      "bambusové údolí",            Warm,  I, (int)BiomeRegion.Bamboo, 0.52f, 0.66f, 0.6f, 1f, "údolí, vlhko"),
            new BiomeEntry("krystaly",    "křišťálové jeskyně s podzemními jezery", Mild, I, (int)BiomeRegion.Crystal, 0.42f, 0.50f, 0.38f, 1f, "kolo 21: první fáze na povrchu (krystaly, minerální stěny, jeskynní brány, fialové dno existující vody); podzemní vrstva zatím ne"),
            new BiomeEntry("sekvoje",     "obří sekvojový les",         Mild,  I, (int)BiomeRegion.Sequoia, 0.44f, 0.56f, 0.6f, 1f, "vlhké mírné pásmo"),
            new BiomeEntry("vresoviste",  "větrné vřesoviště",          Mild,  I, (int)BiomeRegion.Heath, 0.42f, 0.50f, 0.4f, 0.7f, "mírné, blízko chladnému"),
            new BiomeEntry("mangrovy",    "pobřežní mangrovy",          Warm,  I, (int)BiomeRegion.Mangrove, 0.54f, 0.75f, 0.6f, 1f, "kolo 20: nízký břeh a mělčiny teplého/horkého vlhkého pobřeží"),
            new BiomeEntry("ledovec",     "ledovcové údolí",            Cold,  I, (int)BiomeRegion.Glacier, 0f, 0.30f, 0f, 1f,     "kolo 20: údolí hluboko v chladném pásmu, led a morény"),
            new BiomeEntry("houby",       "houbařský fantasy les",      Mild,  I, (int)BiomeRegion.Mushroom, 0.42f, 0.50f, 0.56f, 1f, "kolo 21: vlhké jádro mírného pásma, obří houby, svítící drobné houby, výtrusy"),
            new BiomeEntry("solne_plane", "solné pláně",                Hot,   I, (int)BiomeRegion.SaltFlat, 0.62f, 1f, 0f, 0.3f,   "kolo 20: ploché velmi suché nížiny (id dříve „solne“)"),
            new BiomeEntry("cerny",       "černý les",                  Mild,  I, (int)BiomeRegion.BlackForest, 0.42f, 0.52f, 0.5f, 1f, "temný mírný les"),
            new BiomeEntry("sakura",      "sakurové údolí",             Warm,  I, (int)BiomeRegion.Sakura, 0.50f, 0.62f, 0.5f, 0.8f, "údolí teplého pásma"),
            new BiomeEntry("spaleny",     "spálený / uhelný les",       Warm,  I, (int)BiomeRegion.Burnt, 0.50f, 0.68f, 0f, 0.65f, "kolo 19: teplé suché pahorkatiny, ohořelé kmeny"),
            new BiomeEntry("kras",        "krasová krajina s vápencovými věžemi", Warm, I, (int)BiomeRegion.Karst, 0.50f, 0.68f, 0.38f, 1f, "kolo 19: teplé vlhčí kopce, věže z propů (terén beze změny)"),
            new BiomeEntry("zkamenely",   "zkamenělý les",              Hot,   I, (int)BiomeRegion.Petrified, 0.60f, 1f, 0f, 0.55f, "kolo 19: horké suché ploché nížiny"),
            new BiomeEntry("dzungle",     "tropická džungle",           Warm,  I, (int)BiomeRegion.Jungle, 0.56f, 0.75f, 0.65f, 1f, "teplé, vlhké"),
            new BiomeEntry("utesy",       "útesové pobřeží",            Coast, I, (int)BiomeRegion.Cliffs, 0.34f, 0.68f, 0f, 1f,  "kolo 21: nízký břeh mírného a teplého pásma (ne vlhké tropy), křídové/čedičové útesy, pláž; oceán beze změny"),
            new BiomeEntry("geotermal",   "geotermální prameny",        Hot,   I, (int)BiomeRegion.Geothermal, 0.60f, 1f, 0.3f, 0.8f, "kolo 20: okraj masky vulkanické oblasti"),
            new BiomeEntry("ruiny",       "zarostlé ruiny",             Warm,  I, (int)BiomeRegion.Ruins, 0.50f, 1f, 0.3f, 1f, "kolo 19: vzácné ostrůvky na rovině teplého pásma"),
            new BiomeEntry("zamrzly_ocean", "zamrzlý oceán",            Cold,  I, (int)BiomeRegion.FrozenOcean, 0f, 0.32f, 0f, 1f,     "kolo 20: moře a nízký břeh hluboko v chladném pásmu (id dříve „zamrzly“)"),
            new BiomeEntry("kapradiny",   "pravěká kapradinová rokle",  Mild,  I, (int)BiomeRegion.FernGorge, 0.46f, 0.58f, 0.65f, 1f, "rokle, vlhko"),
            new BiomeEntry("oaza",        "oáza",                       Hot,   I, (int)BiomeRegion.Oasis, 0.60f, 1f, 0f, 0.55f, "kolo 19: jen u existující vody horkého pásma"),
            // ── kolo 20: nová položka jen na konec ──
            new BiomeEntry("zasnezene_stity", "zasněžené štíty",        Cold,  I, (int)BiomeRegion.SnowPeaks, 0f, 0.42f, 0f, 1f, "kolo 20: vysoké polohy chladného pásma, sníh a led"),
            // ── kolo 28: šestý balík (geologické), anglická kanonická id; jen na konec ──
            new BiomeEntry("basalt_columns_coast", "čedičové pobřeží",  Coast, I, (int)BiomeRegion.BasaltCoast, 0.38f, 0.66f, 0f, 1f, "kolo 28: chladnější mírné a teplé ne-vlhké pobřeží, šestiboké sloupy i v mělčině, černý písek; oceán beze změny"),
            new BiomeEntry("obsidian_plain", "obsidiánová pláň",         Hot,   I, (int)BiomeRegion.ObsidianPlain, 0.62f, 1f, 0f, 0.5f, "kolo 28: jádro horkého pásma, suché roviny 16–170 m; jen vizuální (bez poškození výbavy)"),
            new BiomeEntry("alabaster_plateau", "alabastrové plato",     Warm,  I, (int)BiomeRegion.AlabasterPlateau, 0.55f, 0.68f, 0f, 0.55f, "kolo 28: suché jádro teplého pásma, plošiny 40–420 m, hladké formace a oblouky"),
            // ── kolo 29: sedmý balík (geologické 2), anglická kanonická id; jen na konec ──
            new BiomeEntry("meteor_crater", "meteorický kráter",         Warm,  I, (int)BiomeRegion.MeteorCrater, 0.55f, 1f, 0f, 0.6f, "kolo 29: vzácné kruhové krátery (2,4 km buňky) v jádru teplého/horkého pásma, val R 74–112 m, tektity, černé sklo, rudy, spálený prstenec; jen vizuální"),
            new BiomeEntry("fossilized_coral_reef", "zkamenělý korálový útes", Warm, I, (int)BiomeRegion.FossilReef, 0.55f, 0.68f, 0.3f, 0.7f, "kolo 29: vyschlé dávné mořské dno v teplých nížinách 14–120 m, kalcitové korály, porézní vápenec, kostry, průchozí brány"),
            new BiomeEntry("mud_volcanoes", "bahenní sopky a solfatary",  Warm,  I, (int)BiomeRegion.MudVolcanoes, 0.55f, 1f, 0f, 0.6f, "kolo 29: ploché teplé/horké nížiny 10–100 m, šedé kužely, praskající krusty, sírové krystaly, střídmá pára; jen vizuální"),
        };

        public static int Count => All.Length;

        public static int Find(string id)
        {
            string s = (id ?? "").Trim().ToLowerInvariant();
            for (int i = 0; i < All.Length; i++) if (All[i].id == s) return i;
            // kolo 20: dřívější krátká id
            if (s == "solne") return Find("solne_plane");
            if (s == "zamrzly") return Find("zamrzly_ocean");
            return -1;
        }

        /// <summary>Položka implementovaného regionu.</summary>
        public static int OfRegion(int region)
        {
            for (int i = 0; i < All.Length; i++) if (All[i].region == region && All[i].status == BiomeStatus.Implemented) return i;
            return -1;
        }

        public static string BandName(ClimateBand b) => b switch
        {
            ClimateBand.Hot => "horké", ClimateBand.Warm => "teplé", ClimateBand.Mild => "mírné",
            ClimateBand.Cold => "chladné", ClimateBand.Underground => "podzemí", _ => "pobřeží",
        };

        /// <summary>Pásmo implementovaného regionu (pro kontrolu sousedství).</summary>
        public static ClimateBand BandOfRegion(int region)
        {
            int i = OfRegion(region);
            return i >= 0 ? All[i].band : ClimateBand.Mild;
        }
    }
}
