using System.Collections;
using System.Globalization;
using System.IO;
using System.Text;
using Orivilon.World.Generation;
using Unity.Mathematics;
using UnityEngine;

namespace Orivilon.Core
{
    /// <summary>
    /// Kolo 14: konzole pro klimatické regiony (BiomeMath).
    /// <c>/biomy</c> – váhy v místě hráče; <c>/biomy on|off</c>; <c>/biomy mapa [r] [px]</c> – PNG mapa
    /// regionů do _reports/kolo14; <c>/biomy tp &lt;region&gt; [maxR]</c> – nejbližší výrazná oblast;
    /// <c>/biomy stat</c> – osazené objekty podle regionu.
    /// </summary>
    public partial class GameConsole
    {
        private static readonly string[] RegionKeys = { "louka", "les", "briza", "step", "poust", "mesa", "mokrad", "tundra",
            "cerny", "sekvoje", "kvetouci", "vresoviste", "sakura", "bambus", "dzungle", "kapradiny",
            "vulkan", "spaleny", "kras", "zkamenely", "ruiny", "oaza",
            "zasnezene_stity", "ledovec", "zamrzly_ocean", "mangrovy", "solne_plane", "geotermal",   // kolo 20
            "krystaly", "houby", "utesy",   // kolo 21
            "cedicove_pobrezi", "obsidianova_plan", "alabastrove_plato" };   // kolo 28 (skrytá kompatibilita, kanonická id jsou anglická)

        /// <summary>
        /// Kolo 25: veřejná anglická ID biomů (index = <see cref="BiomeRegion"/>). Hráčský seznam,
        /// /biome tp i chybové zprávy používají jen tato. Staré české klíče (<see cref="RegionKeys"/>)
        /// zůstávají jen jako skrytá kompatibilita a pro interní diagnostiku.
        /// </summary>
        private static readonly string[] RegionIds = { "meadow", "conifer_forest", "birch_grove", "steppe", "desert", "mesa", "swamp", "tundra",
            "black_forest", "sequoia_forest", "flower_meadow", "heath", "sakura_valley", "bamboo_valley", "jungle", "fern_gorge",
            "volcanic", "burnt_forest", "karst", "petrified_forest", "ruins", "oasis",
            "snowy_peaks", "glacier_valley", "frozen_ocean", "mangroves", "salt_flats", "geothermal_springs",
            "crystal_caves", "mushroom_forest", "cliff_coast",
            "basalt_columns_coast", "obsidian_plain", "alabaster_plateau" };   // kolo 28

        private static readonly string[] RegionNamesEn = { "Meadow", "Conifer Forest", "Birch Grove", "Steppe & Savanna", "Desert", "Mesa Badlands", "Swamp", "Tundra",
            "Black Forest", "Giant Sequoia Forest", "Flower Meadow", "Windswept Heath", "Sakura Valley", "Bamboo Valley", "Tropical Jungle", "Primeval Fern Gorge",
            "Volcanic Lands", "Burnt Forest", "Karst Towers", "Petrified Forest", "Overgrown Ruins", "Oasis",
            "Snowy Peaks", "Glacier Valley", "Frozen Ocean", "Coastal Mangroves", "Salt Flats", "Geothermal Springs",
            "Crystal Caves", "Mushroom Forest", "Cliff Coast",
            "Basalt Columns Coast", "Obsidian Plain", "Alabaster Plateau" };

        private static string RegionId(int r) => r >= 0 && r < RegionIds.Length ? RegionIds[r] : "?";
        private static string RegionLabel(int r) => r >= 0 && r < RegionNamesEn.Length ? RegionNamesEn[r] + " [" + RegionIds[r] + "]" : "?";

        private static string BandEn(ClimateBand b) => b switch
        {
            ClimateBand.Hot => "hot", ClimateBand.Warm => "warm", ClimateBand.Mild => "temperate",
            ClimateBand.Cold => "cold", ClimateBand.Underground => "underground", _ => "coastal",
        };

        // ── kolo 25: stav letu – /biome tp ho nesmí změnit ─────────────
        private static readonly System.Reflection.FieldInfo FlightRequestedField = typeof(Orivilon.Player.FirstPersonController).GetField("flightRequested",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        private bool tpFlyKnown, tpFlyEnable, tpFlyReq;

        private static bool ReadFly(out bool enable, out bool requested, out string state)
        {
            enable = requested = false; state = "?";
            var f = UnityEngine.Object.FindFirstObjectByType<Orivilon.Player.FirstPersonController>();
            if (f == null || FlightRequestedField == null) return false;
            enable = f.enableFlight; requested = (bool)FlightRequestedField.GetValue(f); state = f.MoveState.ToString();
            return true;
        }

        private static string FlyText()
            => ReadFly(out bool e, out bool r, out string st) ? "flight " + (r ? "ON" : "OFF") + " (enabled " + e + ", requested " + r + ", state " + st + ")" : "flight unknown";

        /// <summary>Zapamatuje stav letu před /biome tp.</summary>
        private void SnapshotFly() => tpFlyKnown = ReadFly(out tpFlyEnable, out tpFlyReq, out _);

        /// <summary>Po /biome tp: stav letu musí být stejný; kdyby nebyl, vrátí se a nahlásí.</summary>
        private string CheckFly()
        {
            if (!tpFlyKnown || !ReadFly(out bool e, out bool r, out _)) return "flight state unknown";
            tpFlyKnown = false;
            if (e == tpFlyEnable && r == tpFlyReq) return "flight " + (r ? "ON" : "OFF") + " (unchanged)";
            var f = UnityEngine.Object.FindFirstObjectByType<Orivilon.Player.FirstPersonController>();
            f.enableFlight = tpFlyEnable; FlightRequestedField.SetValue(f, tpFlyReq);
            Debug.LogWarning("[biome tp] stav letu se změnil (" + e + "/" + r + "), vrácen na " + tpFlyEnable + "/" + tpFlyReq);
            return "flight " + (tpFlyReq ? "ON" : "OFF") + " (restored)";
        }

        /// <summary>Kolo 16: biomy vázané na vodu (hledání s hydrologií, dvoufázový teleport).</summary>
        private static bool WaterBiome(int r) => r == (int)BiomeRegion.Swamp || r == (int)BiomeRegion.FernGorge || r == (int)BiomeRegion.Oasis;

        private static readonly Color32[] RegionColors =
        {
            new Color32(96, 160, 64, 255), new Color32(30, 90, 50, 255), new Color32(200, 170, 60, 255), new Color32(200, 185, 110, 255),
            new Color32(240, 210, 140, 255), new Color32(200, 90, 50, 255), new Color32(70, 95, 60, 255), new Color32(150, 150, 120, 255),
            new Color32(15, 50, 30, 255), new Color32(120, 60, 40, 255), new Color32(220, 140, 200, 255), new Color32(140, 90, 150, 255),
            new Color32(245, 170, 195, 255), new Color32(140, 205, 80, 255), new Color32(25, 125, 40, 255), new Color32(60, 140, 115, 255),
            new Color32(35, 28, 28, 255), new Color32(95, 80, 70, 255), new Color32(200, 215, 190, 255), new Color32(160, 120, 170, 255),
            new Color32(220, 190, 90, 255), new Color32(40, 200, 180, 255),
            new Color32(250, 250, 255, 255), new Color32(150, 205, 240, 255), new Color32(190, 225, 245, 255),
            new Color32(60, 95, 40, 255), new Color32(255, 248, 230, 255), new Color32(240, 140, 40, 255),   // kolo 20
            new Color32(140, 90, 215, 255), new Color32(215, 60, 90, 255), new Color32(235, 235, 220, 255),  // kolo 21
            new Color32(55, 65, 85, 255), new Color32(95, 35, 120, 255), new Color32(255, 214, 160, 255),    // kolo 28
        };

        /// <summary>Kolo 14b: podpříkazy, které /biome předá regionům (bez nich zůstává původní /biome).</summary>
        private static bool IsRegionSub(string s)
        {
            switch ((s ?? "").ToLowerInvariant())
            {
                case "list": case "seznam": case "kde": case "where": case "tp": case "najdi": case "find": case "klima": case "climate":
                case "mapa": case "map": case "pohled": case "view": case "zeme": case "ground": case "hranice": case "border":
                case "hory": case "pobrezi": case "stat": case "on": case "off": case "info": case "nadhled": case "overhead": case "burst": case "burstcheck": case "barva": case "color": case "hydro":
                case "snih": case "snow": case "help": case "flystate":
                    return true;
            }
            return false;
        }

        private static int ParseRegion(string s)
        {
            s = (s ?? "").Trim().ToLowerInvariant().Replace('-', '_').Replace(' ', '_');
            for (int i = 0; i < RegionIds.Length; i++) if (s == RegionIds[i]) return i;
            // skrytá kompatibilita: staré české klíče a aliasy (nejsou v nápovědě ani v seznamu)
            for (int i = 0; i < RegionKeys.Length; i++) if (s == RegionKeys[i]) return i;
            switch (s)
            {
                case "meadow": return 0; case "boreal": case "jehlicnaty": case "tajga": case "taiga": return 1;
                case "birch": case "podzim": case "haj": return 2; case "steppe": case "savana": case "savanna": return 3;
                case "desert": return 4; case "badlands": return 5; case "swamp": case "bazina": return 6;
                case "blackforest": case "cernyles": return 8; case "sequoia": case "sekvojovy": return 9; case "flowers": case "kvetiny": return 10;
                case "heath": case "vres": return 11; case "bamboo": return 13; case "jungle": case "dzungle_": return 14; case "ferns": case "rokle": return 15;
                case "volcano": case "volcanic": case "lava": return 16; case "burnt": case "spaleniste": case "uhelny": return 17;
                case "karst": case "vapenec": return 18; case "petrified": case "zkameneliny": return 19; case "ruins": case "ruina": return 20;
                case "oasis": case "oazy": return 21;
                // kolo 20
                case "stity": case "snih": case "snowpeaks": case "stit": return 22;
                case "glacier": case "led": return 23;
                case "zamrzly": case "frozen": case "frozenocean": case "kry": return 24;
                case "mangrove": case "mangroves": case "mangrovnik": return 25;
                case "solne": case "salt": case "saltflat": case "sul": return 26;
                case "geothermal": case "geotermalni": case "prameny": case "gejzir": return 27;
                // kolo 21
                case "crystal": case "crystals": case "krystal": case "kristaly": case "krystalova": case "jeskyne": return 28;
                case "mushroom": case "mushrooms": case "houba": case "houbovy": case "houbarsky": return 29;
                case "cliffs": case "cliff": case "utes": case "utesove_pobrezi": case "pobrezi_utesy": return 30;
                // kolo 28
                case "basalt": case "basalt_coast": case "basalt_columns": case "cedic": case "cedicove": return 31;
                case "obsidian": case "obsidian_plains": case "obsidian_plane": case "obsidianova": return 32;
                case "alabaster": case "alabaster_plateaus": case "alabastr": case "alabastrove": return 33;
            }
            return -1;
        }

        private void CmdRegions(string[] a)
        {
            if (!RequireTerrain(out VoxelTerrain t, out Transform p)) return;
            string sub = a.Length > 1 ? a[1].ToLowerInvariant() : "info";
            Vector3 v = p.position;
            switch (sub)
            {
                case "burst":
                {
                    if (a.Length > 2) BiomeBurst.Enabled = a[2] != "off" && a[2] != "0";
                    EcologyPlacer.RegionTicks = 0; EcologyPlacer.RegionCalls = 0;
                    Ok("váhy v osazování přes Burst: " + (BiomeBurst.Enabled ? "ZAP" : "VYP") + " (měření vynulováno)");
                    Diag("[biome burst] " + (BiomeBurst.Enabled ? "ZAP" : "VYP"));
                    break;
                }
                case "snih": case "snow":
                {
                    // Kolo 20: A/B zvednutí sněžné čáry podle biomového klimatu (platí pro nově stavěné chunky).
                    if (a.Length > 2) t.SetSnowLift(a[2] != "off" && a[2] != "0");
                    string txt = "[biome snih] sněžná čára podle biomového klimatu: " + (t.SnowLiftOn ? "ZAP (kolo 20)" : "VYP (chování kola 19)") + " – platí pro nově stavěné chunky";
                    Diag(txt); Ok(txt);
                    break;
                }
                case "hydro":
                {
                    // Kolo 17: A/B – off = hledání vidí hydrologii jen v regionech streameru (chování kola 16).
                    if (a.Length > 2) VoxelTerrain.SearchHydroEnabled = a[2] != "off" && a[2] != "0";
                    string txt = "[biome hydro] hledání s hydrologií mimo streamer: " + (VoxelTerrain.SearchHydroEnabled ? "ZAP" : "VYP (kolo 16)");
                    Diag(txt); Ok(txt);
                    break;
                }
                case "barva": case "color":
                {
                    // Kolo 15: barva povrchu z palety v místě hráče (rovná faseta, bez břehu) – před světlem.
                    t.RegionAt(v.x, v.z, false, out float sf, out float clf);
                    t.ClimateAt(v.x, v.z, out float tt, out float hh);
                    TerrainPalette pal = t.Palette;
                    float3 rgb = pal.EvaluateArt(new float3(v.x, sf, v.z), 1f, tt, hh, TerrainPalette.NoShore, t.MicroOffset, clf);
                    float3 rgbS = pal.EvaluateArt(new float3(v.x, sf, v.z), 0.5f, tt, hh, TerrainPalette.NoShore, t.MicroOffset, clf);
                    string txt = string.Format(CultureInfo.InvariantCulture, "[biome barva] ({0:0},{1:0}) y {2:0.0}: rovina ({3:0.00}, {4:0.00}, {5:0.00}) svah ({6:0.00}, {7:0.00}, {8:0.00}) | grassTop {9:0} sandTop {10:0} snow {11:0}",
                        v.x, v.z, sf, rgb.x, rgb.y, rgb.z, rgbS.x, rgbS.y, rgbS.z, pal.grassTop, pal.sandTop, pal.snowTop);
                    Diag(txt); Ok(txt);
                    break;
                }
                case "burstcheck":
                {
                    int nChk = a.Length > 2 && TryNumber(a[2], out float nc) ? Mathf.Clamp((int)nc, 100, 200000) : 20000;
                    string txt = BurstCheck(t, v.x, v.z, nChk);
                    Diag(txt); Ok(txt);
                    break;
                }
                case "list": case "seznam":
                {
                    Diag(CatalogList());
                    PrintBiomeList(a.Length > 2 && (a[2] == "names" || a[2] == "full"));
                    break;
                }
                case "kde": case "where": case "info":
                {
                    Diag(WhereText(t, v.x, v.z));
                    Ok(WherePlayer(t, v.x, v.z));
                    break;
                }
                case "help":
                    BiomeHelp();
                    break;
                case "flystate":
                {
                    string txt = "[biome flystate] " + FlyText();
                    Diag(txt); Note(txt);
                    break;
                }
                case "klima": case "climate":
                {
                    float r = a.Length > 2 && TryNumber(a[2], out float kr) ? kr : 8000f;
                    float st = a.Length > 3 && TryNumber(a[3], out float ks) ? Mathf.Clamp(ks, 16f, 256f) : 64f;
                    string txt = ClimateCheck(t, v.x, v.z, r, st);
                    Diag(txt); Ok(txt);
                    break;
                }
                case "on": case "off":
                    t.SetBiomes(sub == "on");
                    Ok("Regiony " + (sub == "on" ? "ZAP" : "VYP") + " – platí pro nově stavěné sloupce (/tp pryč a zpět).");
                    break;
                case "mapa": case "map":
                {
                    float r = a.Length > 2 && TryNumber(a[2], out float rr) ? rr : 8000f;
                    int px = a.Length > 3 && TryNumber(a[3], out float pp) ? Mathf.Clamp((int)pp, 64, 1024) : 320;
                    Diag(RegionMap(t, v.x, v.z, r, px));
                    Ok("mapa hotová (_reports/kolo14)");
                    break;
                }
                case "tp": case "najdi": case "find":
                {
                    int want = a.Length > 2 ? ParseRegion(a[2]) : -1;
                    if (want < 0) { Error(UnknownRegion(a.Length > 2 ? JoinRest(a, 2) : "")); break; }
                    if (sub == "tp" && search != null) { Error("Something is already running (/stop)."); break; }
                    if (sub == "tp") SnapshotFly();
                    bool autoR = !(a.Length > 3 && TryNumber(a[3], out _));
                    float maxR = a.Length > 3 && TryNumber(a[3], out float mr) ? mr : 9000f;
                    t.ResetSearchHydroStats();
                    var swF = System.Diagnostics.Stopwatch.StartNew();
                    // Kolo 17: u /biome tp bez poloměru se synchronně hledá jen do 3 km (hra nezamrzne),
                    // zbytek po snímcích ve FarRegionTp – pořadí bodů je stejné, výsledek také.
                    bool asyncTp = sub == "tp" && autoR;
                    bool found = FindRegion(t, v.x, v.z, want, asyncTp ? NearSyncR : maxR, out Vector3 at, !asyncTp);
                    float findMs = (float)swF.Elapsed.TotalMilliseconds;
                    if (found)
                    {
                        Diag(string.Format(CultureInfo.InvariantCulture, "[biomy] {0}: nejbližší oblast ({1:0}, {2:0}), {3:0} m od hráče ({4:0.0},{5:0.0}) | hledání {6:0} ms, z toho hydrologie {7} regionů {8:0} ms",
                            BiomeMath.Name(want), at.x, at.z, Vector2.Distance(new Vector2(v.x, v.z), new Vector2(at.x, at.z)), v.x, v.z,
                            findMs, t.SearchHydroBuilt, t.SearchHydroMs));
                        if (sub == "tp")
                        {
                            if (search != null) { Error("Something is already running (/stop)."); break; }
                            search = StartCoroutine(SafeRegionTp(t, want, at, false, 0f));
                        }
                        Ok(string.Format(CultureInfo.InvariantCulture, sub == "tp" ? "Teleporting to {0} near ({1:0}, {2:0})..." : "{0}: nearest area at ({1:0}, {2:0}), {3:0} m away",
                            RegionLabel(want), at.x, at.z, Vector2.Distance(new Vector2(v.x, v.z), new Vector2(at.x, at.z))));
                    }
                    else if (asyncTp)
                    {
                        // Kolo 17: vzácný biom (mesa, sakura, džungle…) bývá dál než 9 km – hledání
                        // pokračuje po snímcích až do 30 km, hra nezamrzne.
                        if (search != null) { Error("Something is already running (/stop)."); break; }
                        Diag(string.Format(CultureInfo.InvariantCulture, "[biomy] {0}: do {1:0} m nenalezeno ({2:0} ms) – hledám dál po snímcích", BiomeMath.Name(want), NearSyncR, findMs));
                        search = StartCoroutine(FarRegionTp(t, want, v));
                        Ok(RegionLabel(want) + ": nothing within " + (NearSyncR / 1000f).ToString("0", CultureInfo.InvariantCulture) + " km, searching further (up to "
                           + (FarSearchR / 1000f).ToString("0", CultureInfo.InvariantCulture) + " km)...");
                    }
                    else { Diag("[biomy] " + BiomeMath.Name(want) + ": do " + maxR + " m nenalezeno"); Error("No " + RegionLabel(want) + " found within " + maxR.ToString("0", CultureInfo.InvariantCulture) + " m."); }
                    break;
                }
                case "pohled": case "view": case "zeme": case "ground":
                {
                    // Snímky: najde výraznou oblast regionu a postaví kameru 120 m jižně a 80 m nad ni
                    // (let zapnutý), nebo u „zeme" na zem s pohledem na sever.
                    int want = a.Length > 2 ? ParseRegion(a[2]) : -1;
                    if (want < 0) { Error(UnknownRegion(a.Length > 2 ? a[2] : "")); break; }
                    float maxR = a.Length > 3 && TryNumber(a[3], out float mr2) ? mr2 : 9000f;
                    float yawV = a.Length > 4 && TryNumber(a[4], out float yv) ? yv : 0f;
                    if (!FindRegion(t, v.x, v.z, want, maxR, out Vector3 at)) { Diag("[biomy] " + BiomeMath.Name(want) + ": nenalezeno"); Error("nenalezeno"); break; }
                    t.RegionAt(at.x, at.z, false, out float surfAt, out _);
                    bool ground = sub == "zeme" || sub == "ground";
                    float yr = yawV * Mathf.Deg2Rad;
                    Vector3 back = new Vector3(-Mathf.Sin(yr), 0f, -Mathf.Cos(yr));
                    t.RegionAt(at.x + back.x * 220f, at.z + back.z * 220f, false, out float surfCam, out _);
                    // kolo 16: vysoké stromy (sekvoje, džungle ~40 m) – kamera nad nejvyšším bodem trasy + 110 m
                    t.RegionAt(at.x + back.x * 110f, at.z + back.z * 110f, false, out float surfMid, out _);
                    Vector3 cam = ground ? at : at + back * 220f + Vector3.up * (Mathf.Max(surfAt, Mathf.Max(surfCam, surfMid)) + 190f);
                    if (ground)
                    {
                        if (search != null) { Error("Something is already running (/stop)."); break; }
                        search = StartCoroutine(SafeRegionTp(t, want, at, true, yawV));
                    }
                    else
                    {
                        CmdFly(new[] { "let", "on" });
                        pendingYaw = yawV; pendingPitch = 28f;
                        pendingLook = true; pendingSince = Time.unscaledTime;
                        t.Teleport(cam, false);
                    }
                    Diag(string.Format(CultureInfo.InvariantCulture, "[biomy pohled] s{0} {1}: cíl ({2:0},{3:0}) povrch {4:0} m, kamera ({5:0},{6:0},{7:0}) yaw {8:0}",
                        t.WorldSeed, BiomeMath.Name(want), at.x, at.z, surfAt, cam.x, cam.y, cam.z, yawV));
                    Ok(BiomeMath.Name(want));
                    break;
                }
                case "nadhled": case "overhead":
                {
                    // Letecký pohled na místo, kde hráč stojí (po /biome tp nebo zeme).
                    float yawN = a.Length > 2 && TryNumber(a[2], out float yn) ? yn : 0f;
                    float yrn = yawN * Mathf.Deg2Rad;
                    Vector3 backN = new Vector3(-Mathf.Sin(yrn), 0f, -Mathf.Cos(yrn));
                    t.RegionAt(v.x + backN.x * 140f, v.z + backN.z * 140f, false, out float sCam, out _);
                    Vector3 camN = new Vector3(v.x, 0f, v.z) + backN * 140f + Vector3.up * (Mathf.Max(v.y, sCam) + 90f);
                    CmdFly(new[] { "let", "on" });
                    pendingYaw = yawN; pendingPitch = 28f; pendingLook = true; pendingSince = Time.unscaledTime;
                    t.Teleport(camN, false);
                    Ok("nadhled");
                    break;
                }
                case "hranice": case "border": case "hory": case "pobrezi":
                {
                    // Přechody: /biomy hranice <A> <B> [maxR] – bod, kde se A a B potkávají (obě 30–70 %);
                    // /biomy hory [maxR] – vysoko nad hranicí trávy (tundra/sníh); /biomy pobrezi <A> [maxR] – břeh moře v regionu A.
                    int wa = -1, wb = -1; int ai = 2;
                    if (sub == "hranice" || sub == "border")
                    {
                        wa = a.Length > 2 ? ParseRegion(a[2]) : -1; wb = a.Length > 3 ? ParseRegion(a[3]) : -1; ai = 4;
                        if (wa < 0 || wb < 0) { Error("/biomy hranice <A> <B>: " + string.Join(", ", RegionKeys)); break; }
                    }
                    else if (sub == "pobrezi")
                    {
                        wa = a.Length > 2 ? ParseRegion(a[2]) : -1; ai = 3;
                        if (wa < 0) { Error("/biomy pobrezi <region>"); break; }
                    }
                    float maxR = a.Length > ai && TryNumber(a[ai], out float mr3) ? mr3 : 9000f;
                    if (!FindSpot(t, v.x, v.z, sub, wa, wb, maxR, out Vector3 at)) { Diag("[biomy " + sub + "] nenalezeno"); Error("nenalezeno"); break; }
                    t.RegionAt(at.x, at.z, false, out float sA, out _);
                    t.RegionAt(at.x, at.z - 220f, false, out float sC, out _);
                    Vector3 cam = at + new Vector3(0f, Mathf.Max(sA, sC) + 150f, -220f);
                    CmdFly(new[] { "let", "on" });
                    pendingYaw = 0f; pendingPitch = 26f; pendingLook = true; pendingSince = Time.unscaledTime;
                    t.Teleport(cam, false);
                    Diag(string.Format(CultureInfo.InvariantCulture, "[biomy {0}] s{1} {2}{3}: cíl ({4:0},{5:0}) povrch {6:0} m",
                        sub, t.WorldSeed, wa >= 0 ? BiomeMath.Name(wa) : "", wb >= 0 ? "/" + BiomeMath.Name(wb) : "", at.x, at.z, sA));
                    Ok(sub);
                    break;
                }
                case "stat":
                {
                    var sb = new StringBuilder("[biomy stat] osazeno podle regionu:");
                    for (int i = 0; i < BiomeMath.Count; i++)
                    {
                        sb.Append(" | ").Append(BiomeMath.Name(i)).Append(' ').Append(EcologyPlacer.RegionPlaced[i]).Append(" [");
                        for (int l = 0; l < 7; l++) sb.Append(l == 0 ? "" : "/").Append(EcologyPlacer.RegionLayerPlaced[i * 8 + l]);
                        sb.Append(']');
                    }
                    sb.AppendFormat(CultureInfo.InvariantCulture, " | váhy: {0} volání, {1:0.0} ms ({2:0.00} µs/volání) z osazení {3:0.0} ms ve {4} sloupcích",
                        EcologyPlacer.RegionCalls, EcologyPlacer.RegionMs, EcologyPlacer.RegionCalls > 0 ? EcologyPlacer.RegionMs * 1000.0 / EcologyPlacer.RegionCalls : 0.0,
                        EcologyPlacer.TotalMs, EcologyPlacer.Columns);
                    Diag(sb.ToString()); Ok(sb.ToString());
                    break;
                }
                default:
                {
                    string txt = WhereText(t, v.x, v.z);
                    Diag(txt); Ok(txt);
                    break;
                }
            }
        }

        /// <summary>Mapa převládajícího regionu (bez hydrologie): moře modře, pláž béžově, sníh bíle, skalnaté hory šedě.</summary>
        private static string RegionMap(VoxelTerrain t, float cx, float cz, float r, int px)
        {
            var tex = new Texture2D(px, px, TextureFormat.RGB24, false);
            var counts = new int[BiomeMath.Count];
            int land = 0;
            float sea = t.SeaLevel;
            TerrainPalette pal = t.Palette;
            for (int j = 0; j < px; j++)
            for (int i = 0; i < px; i++)
            {
                float x = cx - r + (i + 0.5f) / px * 2f * r;
                float z = cz - r + (j + 0.5f) / px * 2f * r;
                RegionWeights w = t.RegionAt(x, z, false, out float surf, out float cliff);
                t.ClimateAt(x, z, out float temp, out _);
                float snow = pal.snowTop + pal.snowTempShift * (temp - 0.5f) * 2f;
                Color32 c;
                if (surf < sea - 1f) c = new Color32(40, 80, 150, 255);
                else if (surf < sea + 2.5f) c = new Color32(225, 210, 160, 255);
                else
                {
                    int d = w.Dominant();
                    counts[d]++; land++;
                    c = RegionColors[d];
                    if (surf > snow) c = new Color32(245, 245, 250, 255);
                    else if (surf > pal.grassTop + 20f) c = Color32.Lerp(c, new Color32(130, 130, 130, 255), 0.45f);
                    float shade = Mathf.Clamp01(0.85f + surf / 1500f);
                    c = new Color32((byte)(c.r * shade), (byte)(c.g * shade), (byte)(c.b * shade), 255);
                }
                tex.SetPixel(i, j, c);
            }
            // hráč uprostřed
            for (int k = -2; k <= 2; k++) { tex.SetPixel(px / 2 + k, px / 2, Color.red); tex.SetPixel(px / 2, px / 2 + k, Color.red); }
            tex.Apply();
            string dir = Path.Combine(Path.GetDirectoryName(Application.dataPath), "_reports", "kolo14");
            Directory.CreateDirectory(dir);
            string path = Path.Combine(dir, string.Format(CultureInfo.InvariantCulture, "mapa_s{0}_{1:0}_{2:0}.png", t.WorldSeed, cx, cz));
            File.WriteAllBytes(path, tex.EncodeToPNG());
            Object.Destroy(tex);
            var sb = new StringBuilder();
            sb.AppendFormat(CultureInfo.InvariantCulture, "[biomy mapa] s{0} střed ({1:0},{2:0}) r {3:0} m, {4} px → {5} | souš podle regionu:",
                t.WorldSeed, cx, cz, r, px, Path.GetFileName(path));
            for (int i = 0; i < BiomeMath.Count; i++)
                sb.AppendFormat(CultureInfo.InvariantCulture, " {0} {1:0.0}%", BiomeMath.Name(i), land > 0 ? 100f * counts[i] / land : 0f);
            return sb.ToString();
        }

        // ── kolo 14b: katalog, klima, bezpečný teleport ─────────────────────

        /// <summary>
        /// Kolo 15: porovná váhy Mono a Burst na N deterministických bodech v okruhu 4 km
        /// (náhodný povrch, klima, terasa, voda) – největší rozdíl váhy a kolik losů PickSharp
        /// (4 losy na bod) by dopadlo jinak. Měří i čas obou cest.
        /// </summary>
        private static string BurstCheck(VoxelTerrain t, float cx, float cz, int n)
        {
            float3 off = t.MicroOffset;
            float sea = t.SeaLevel;
            uint h = 0x9E3779B9u;
            float maxD = 0f; int flips = 0, picks = 0;
            long tm = 0, tb = 0;
            for (int i = 0; i < n; i++)
            {
                h = h * 1664525u + 1013904223u; float rx = (h >> 8) / 16777216f;
                h = h * 1664525u + 1013904223u; float rz = (h >> 8) / 16777216f;
                h = h * 1664525u + 1013904223u; float ry = (h >> 8) / 16777216f;
                h = h * 1664525u + 1013904223u; float rw = (h >> 8) / 16777216f;
                var xz = new float2(cx + (rx - 0.5f) * 8000f, cz + (rz - 0.5f) * 8000f);
                float temp = 0.2f + 0.6f * ry, hum = 0.2f + 0.6f * rw;
                float y = sea + 300f * ry * rw, cliff = rx * 0.6f;
                bool water = (i & 3) == 0; float hW = water ? 12f * rz : 1e4f;
                long t0 = System.Diagnostics.Stopwatch.GetTimestamp();
                RegionWeights wm = BiomeMath.Weights(xz, y, temp, hum, cliff, hW, water, sea, off);
                long t1 = System.Diagnostics.Stopwatch.GetTimestamp();
                BiomeBurst.Weights(in xz, y, temp, hum, cliff, hW, water ? 1 : 0, sea, in off, out RegionWeights wb);
                long t2 = System.Diagnostics.Stopwatch.GetTimestamp();
                tm += t1 - t0; tb += t2 - t1;
                for (int k = 0; k < BiomeMath.Count; k++) maxD = Mathf.Max(maxD, Mathf.Abs(wm.Get(k) - wb.Get(k)));
                for (int q = 0; q < 4; q++)
                {
                    h = h * 1664525u + 1013904223u; float u = (h >> 8) / 16777216f;
                    picks++; if (wm.PickSharp(u) != wb.PickSharp(u)) flips++;
                }
            }
            double f = 1e6 / System.Diagnostics.Stopwatch.Frequency;
            return string.Format(CultureInfo.InvariantCulture,
                "[biome burstcheck] {0} bodů: max |Δváha| {1:E2}, jiný los {2} z {3} | Mono {4:0.00} µs, Burst {5:0.00} µs na volání",
                n, maxD, flips, picks, tm * f / n, tb * f / n);
        }

        private static string UnknownRegion(string id)
        {
            if (string.IsNullOrWhiteSpace(id))
                return "Usage: /biome tp <biome-id>, e.g. /biome tp snowy_peaks. Type /biome list to see all IDs.";
            return "Unknown biome ID '" + id + "'. Type /biome list to see all IDs. Available: " + string.Join(", ", RegionIds);
        }

        private static string JoinRest(string[] a, int from) => from >= a.Length ? "" : string.Join("_", a, from, a.Length - from);

        /// <summary>Kolo 25: hráčský seznam – všechny teleportovatelné biomy s přesným anglickým ID.</summary>
        private void PrintBiomeList(bool names)
        {
            Ok("Teleportable biomes (" + RegionIds.Length + "). Use the exact ID: /biome tp <biome-id>");
            if (names)
            {
                // Plné názvy: řádek na biom (konzole ukáže naráz jen konec – proto je výchozí výpis kompaktní).
                for (int i = 0; i < RegionIds.Length; i++)
                {
                    int ci = BiomeCatalog.OfRegion(i);
                    Note("  " + RegionIds[i] + "  -  " + RegionNamesEn[i] + (ci >= 0 ? " (" + BandEn(BiomeCatalog.All[ci].band) + ")" : ""));
                }
            }
            else
            {
                // Kompaktně po klimatických pásmech, ať se celý seznam vejde do 12 viditelných řádků.
                ClimateBand[] order = { ClimateBand.Hot, ClimateBand.Warm, ClimateBand.Mild, ClimateBand.Cold, ClimateBand.Coastal, ClimateBand.Underground };
                int shown = 0;
                foreach (ClimateBand b in order)
                {
                    var sb = new StringBuilder();
                    for (int i = 0; i < RegionIds.Length; i++)
                    {
                        int ci = BiomeCatalog.OfRegion(i);
                        if (ci < 0 || BiomeCatalog.All[ci].band != b) continue;
                        sb.Append(sb.Length == 0 ? "" : ", ").Append(RegionIds[i]); shown++;
                    }
                    if (sb.Length > 0) Note("  " + BandEn(b) + ": " + sb);
                }
                for (int i = 0; i < RegionIds.Length; i++)
                    if (BiomeCatalog.OfRegion(i) < 0) { Note("  other: " + RegionIds[i]); shown++; }
                if (shown != RegionIds.Length) Debug.LogWarning("[biome list] vypsáno " + shown + " z " + RegionIds.Length);
            }
            Note("Example: /biome tp snowy_peaks  |  /biome list names - full names  |  /biome where");
        }

        private void BiomeHelp()
        {
            Note("/biome list [names]       - all teleportable biomes and their IDs");
            Note("/biome tp <biome-id>      - teleport to a safe spot in the nearest area of that biome");
            Note("                            (e.g. /biome tp snowy_peaks; flight stays as it is)");
            Note("/biome where              - which biome you are standing in (/biome alone does the same)");
            Note("/biome find <biome-id>    - print the nearest area's coordinates, no teleport");
            Note("/stop                     - cancel a running biome search");
        }

        /// <summary>Kolo 25: hráčská (anglická) odpověď na /biome where.</summary>
        private static string WherePlayer(VoxelTerrain t, float x, float z)
        {
            RegionWeights w = t.RegionAt(x, z, true, out float surf, out _, out ClimateSample c);
            int d = w.Dominant();
            var sb = new StringBuilder();
            sb.AppendFormat(CultureInfo.InvariantCulture, "You are in {0} at ({1:0}, {2:0}), surface {3:0.0} m, climate {4} (temp {5:0.00}, humidity {6:0.00}). Mix:",
                RegionLabel(d), x, z, surf, BandEn(c.Band), c.t, c.h);
            for (int i = 0; i < BiomeMath.Count; i++)
                if (w.Get(i) > 0.01f) sb.AppendFormat(CultureInfo.InvariantCulture, " {0} {1:0}%", RegionId(i), w.Get(i) * 100f);
            return sb.ToString();
        }

        private static string CatalogList()
        {
            var sb = new StringBuilder("[biome list] implementováno (anglická ID):");
            var planned = new StringBuilder();
            int np = 0;
            for (int i = 0; i < BiomeCatalog.Count; i++)
            {
                BiomeEntry e = BiomeCatalog.All[i];
                if (e.IsImplemented) sb.Append(' ').Append(RegionId(e.region)).Append(" (").Append(BandEn(e.band)).Append(')');
                else { planned.Append(np == 0 ? " " : ", ").Append(e.id).Append(" (").Append(BiomeCatalog.BandName(e.band)).Append(')'); np++; }
            }
            sb.Append(" | plánováno ").Append(np).Append(':').Append(planned);
            sb.Append(" | /biome tp <biome-id>, /biome where");
            return sb.ToString();
        }

        private static string WhereText(VoxelTerrain t, float x, float z)
        {
            RegionWeights w = t.RegionAt(x, z, true, out float surf, out float cliff, out ClimateSample c);
            t.ClimateAt(x, z, out float rawT, out float rawH);
            int d = w.Dominant();
            int ci = BiomeCatalog.OfRegion(d);
            var sb = new StringBuilder();
            sb.AppendFormat(CultureInfo.InvariantCulture,
                "[biome kde] ({0:0}, {1:0}) povrch {2:0.0} m, terasa {3:0.00} | biom {4} ({5}) | klima: pásmo {6}, t {7:0.000} (terén {8:0.000}, základ {16:0.000}, výškové −{9:0.000}), h {10:0.000} (makro {11:0.000}) | pásma horké {12:0}% teplé {13:0}% mírné {14:0}% chladné {15:0}% | váhy:",
                x, z, surf, cliff, ci >= 0 ? BiomeCatalog.All[ci].id : "?", BiomeMath.Name(d), BiomeCatalog.BandName(c.Band),
                c.t, rawT, c.cooling, c.h, rawH, c.hot * 100f, c.warm * 100f, c.mild * 100f, c.cold * 100f, c.baseT);
            for (int i = 0; i < BiomeMath.Count; i++)
                if (w.Get(i) > 0.01f) sb.AppendFormat(CultureInfo.InvariantCulture, " {0} {1:0}%", RegionKeys[i], w.Get(i) * 100f);
            return sb.ToString();
        }

        private static bool FlatEnough(VoxelTerrain t, float x, float z, float maxDiff)
        {
            float e = t.SurfaceHeight(x + 3f, z), w = t.SurfaceHeight(x - 3f, z);
            float n = t.SurfaceHeight(x, z + 3f), s = t.SurfaceHeight(x, z - 3f);
            float lo = Mathf.Min(Mathf.Min(e, w), Mathf.Min(n, s)), hi = Mathf.Max(Mathf.Max(e, w), Mathf.Max(n, s));
            return hi - lo <= maxDiff;
        }

        /// <summary>
        /// Bezpečný povrchový bod regionu: ne moře ani řeka/jezero, ne sráz, a paprsek shora musí
        /// dopadnout na terén ve výšce analytického povrchu (jinak je tam jeskyně, převis nebo objekt).
        /// </summary>
        private static bool SafeSpot(VoxelTerrain t, int want, float x, float z, out float groundY, out string why)
        {
            groundY = 0f; why = "";
            RegionWeights w = t.RegionAt(x, z, WaterBiome(want), out float surf, out _);
            RegionWeights wHydro = w;   // kolo 17: odhad z hydrologie – tentýž, který ukazuje /biome kde a používá hledání
            if (WaterBiome(want) && w.Get(want) < 0.5f)
            {
                // Hydrologie nemusí být pro bod postavená – hladinu vezmeme z publikovaných sloupců.
                if (t.WaterLevelAt(x, z, out _)) { why = "water"; return false; }
                float best = float.MaxValue;
                for (int d = 0; d < 8; d++)
                {
                    float ang = d * Mathf.PI * 0.25f;
                    for (float rr = 3f; rr <= 12f; rr += 3f)
                        if (t.WaterLevelAt(x + Mathf.Cos(ang) * rr, z + Mathf.Sin(ang) * rr, out float lv)) { best = Mathf.Min(best, t.SurfaceHeight(x, z) - lv); break; }
                }
                if (best < float.MaxValue && best >= 0.4f) w = t.RegionWithShore(x, z, best, out surf);
            }
            // Kolo 16: rokle je úzký pás svahů nad řekou – stačí převaha 40 % a mírně větší sklon.
            bool gorgeTp = want == (int)BiomeRegion.FernGorge;
            if (w.Get(want) < (gorgeTp ? 0.4f : 0.5f)) { why = "other biome"; return false; }
            // Kolo 17: u vodních biomů (nižší práh 40 %) musí biom v bodě převládat v obou odhadech (břeh
            // z publikované hladiny i hydrologie) – jinak hráč stál v sousedním mokřadu (s90210 rokle ze (7000, 4000)).
            if (WaterBiome(want) && (w.Dominant() != want || wHydro.Dominant() != want)) { why = "other biome"; return false; }
            // Kolo 20: cílový biom musí převládat i s hydrologií – stejný odhad, jaký ukáže /biome kde v cíli
            // (regrese s2026: „poušť" přistála u vody, kde /biome kde správně hlásí oázu).
            if (!WaterBiome(want) && t.RegionAt(x, z, true, out _, out _).Dominant() != want) { why = "other biome (hydrology)"; return false; }
            // …a i 4 m kolem: na hraně jezera přepíná váhy skokově a hráč po dopadu stojí o 1–2 m vedle.
            if (WaterBiome(want))
                for (int d = 0; d < 4; d++)
                {
                    float ox = d == 0 ? 4f : d == 1 ? -4f : 0f, oz = d == 2 ? 4f : d == 3 ? -4f : 0f;
                    if (t.RegionAt(x + ox, z + oz, true, out _, out _).Dominant() != want) { why = "other biome"; return false; }
                }
            if (surf < t.SeaLevel + 2f) { why = "sea"; return false; }
            // kolo 20: mesa je terasovitá – stejná tolerance jako rokle (3,5 j. na ±3 j.), jinak oblast celá „sráz"
            if (!FlatEnough(t, x, z, gorgeTp || want == (int)BiomeRegion.Mesa ? 3.5f : 2.5f)) { why = "steep slope"; return false; }
            float rc = t.RiverClearanceSearch(x, z, out float lakeDepth);
            if (lakeDepth > 0.05f || rc < 4f) { why = "water"; return false; }
            if (t.WaterLevelAt(x, z, out float lvl) && lvl > surf - 0.6f) { why = "water"; return false; }
            if (!Physics.Raycast(new Vector3(x, surf + 40f, z), Vector3.down, out RaycastHit hit, 90f, ~0, QueryTriggerInteraction.Ignore))
            { why = "no ground collider"; return false; }
            if (Mathf.Abs(hit.point.y - surf) > 1.6f || hit.normal.y < 0.8f) { why = "cave/overhang/object"; return false; }
            groundY = hit.point.y;
            return true;
        }

        /// <summary>Kolo 17: dosah synchronního hledání /biome tp (m) – do 3 km stojí nejvýš ~0,3 s.</summary>
        private const float NearSyncR = 3000f;

        /// <summary>Kolo 17: hranice jemného hledání (krok 96 m jako dřív) a dosah hrubého hledání (m).</summary>
        private const float NearR = 9000f, FarSearchR = 30000f;

        /// <summary>
        /// Kolo 17: zbytek hledání /biome tp po snímcích (nejvýš ~10 ms na snímek), pak bezpečný teleport.
        /// Do 9 km tytéž body jako dřív synchronní FindRegion (krok 96 m). Dál hrubě po 192 m: kde je biom
        /// v okolí (váha ≥ 0,25; u rokle a mokřadu klima + řeka do 170 m), dohledá se bod mřížkou 5×5 po 48 m.
        /// Pořadí bodů je pevné, takže výsledek je pro danou výchozí pozici deterministický.
        /// </summary>
        private IEnumerator FarRegionTp(VoxelTerrain t, int want, Vector3 v)
        {
            var total = System.Diagnostics.Stopwatch.StartNew();
            var slice = System.Diagnostics.Stopwatch.StartNew();
            t.ResetSearchHydroStats();
            bool water = WaterBiome(want);
            Vector3 at = default; bool hit = false; int frames = 0, refined = 0;
            // Navazuje přesně na prstence synchronního FindRegion (násobky 96 m), takže body jsou tytéž.
            for (float r = (Mathf.Floor(NearSyncR / 96f) + 1f) * 96f; r <= NearR && !hit; r += 96f)
            {
                int n = Mathf.CeilToInt(2f * Mathf.PI * r / 96f);
                for (int k = 0; k < n && !hit; k++)
                {
                    float ang = k * 2f * Mathf.PI / n;
                    hit = RegionPoint(t, want, v.x + Mathf.Cos(ang) * r, v.z + Mathf.Sin(ang) * r, out at);
                    if (!hit && slice.Elapsed.TotalMilliseconds > 10.0) { frames++; yield return null; slice.Restart(); }
                }
            }
            const float step = 192f;
            for (float r = NearR + step; r <= FarSearchR && !hit; r += step)
            {
                int n = Mathf.CeilToInt(2f * Mathf.PI * r / step);
                for (int k = 0; k < n && !hit; k++)
                {
                    float ang = k * 2f * Mathf.PI / n;
                    float cx = v.x + Mathf.Cos(ang) * r, cz = v.z + Mathf.Sin(ang) * r;
                    bool near;
                    if (water)
                    {
                        t.RegionAt(cx, cz, false, out float sd, out _, out ClimateSample c);
                        near = sd > t.SeaLevel + 1.5f && (want == (int)BiomeRegion.FernGorge
                            ? c.above >= 18f && c.h >= 0.54f && c.mild + c.warm * c.wet >= 0.05f
                            : want == (int)BiomeRegion.Oasis ? c.hot >= 0.3f && c.above >= 14f && c.h <= 0.62f
                            : c.above <= 30f && c.h >= 0.42f && c.cold <= 0.95f && c.hot <= 0.95f)
                            && t.RiverClearanceSearch(cx, cz, out _) < 170f;
                    }
                    else near = t.RegionAt(cx, cz, false, out _, out _).Get(want) >= 0.25f;
                    if (near)
                    {
                        refined++;
                        for (int q = 0; q < 25 && !hit; q++)
                            hit = RegionPoint(t, want, cx + (q % 5 - 2) * 48f, cz + (q / 5 - 2) * 48f, out at);
                    }
                    if (!hit && slice.Elapsed.TotalMilliseconds > 10.0) { frames++; yield return null; slice.Restart(); }
                }
            }
            string txt = string.Format(CultureInfo.InvariantCulture,
                "[biomy] {0}: hledání po snímcích {1} – {2:0} ms ve {3} snímcích, dohledáno {4} okolí, hydrologie {5} regionů {6:0} ms",
                BiomeMath.Name(want), hit ? string.Format(CultureInfo.InvariantCulture, "({0:0}, {1:0}), {2:0} m od hráče", at.x, at.z,
                Vector2.Distance(new Vector2(v.x, v.z), new Vector2(at.x, at.z))) : "do " + FarSearchR.ToString("0", CultureInfo.InvariantCulture) + " m nenalezeno",
                total.Elapsed.TotalMilliseconds, frames, refined, t.SearchHydroBuilt, t.SearchHydroMs);
            Diag(txt);
            if (!hit)
            {
                // Poslední záchrana pro vodní biomy: vlhká nížina do 9 km (chování kola 16) + druhá fáze teleportu.
                if (water && FindRegion(t, v.x, v.z, want, NearR, out at)) { yield return SafeRegionTp(t, want, at, false, 0f); yield break; }
                t.ReleaseSearchHydro(); tpFlyKnown = false;
                Error("No " + RegionLabel(want) + " found within " + (FarSearchR / 1000f).ToString("0", CultureInfo.InvariantCulture) + " km of you.");
                search = null; yield break;
            }
            yield return SafeRegionTp(t, want, at, false, 0f);
        }

        private IEnumerator SafeRegionTp(VoxelTerrain t, int want, Vector3 at, bool look, float yaw, int retry = 3)
        {
            // Kolo 25: veřejný /biome tp let nevypíná (stav ověřuje CheckFly na konci). Jen interní
            // snímkový „/biome zeme" dál přistává pěšky, jako dřív.
            if (look) CmdFly(new[] { "let", "off" });
            t.Teleport(at, true);
            yield return null;
            float until = Time.unscaledTime + 30f;
            while ((t.IsTeleporting || !t.IsSettled) && Time.unscaledTime < until) yield return null;

            // Kolo 15: mokřad leží u vody, kterou zná až postavená hydrologie. Po dorazu (region
            // hydrologie už stojí) se proto hledá skutečný mokřad do 3 km – spirála po 32 m.
            if (WaterBiome(want) && t.RegionAt(at.x, at.z, true, out _, out _).Get(want) < 0.6f)
            {
                bool hit = false;
                float phase2R = want == (int)BiomeRegion.FernGorge ? 5000f : 3000f;
                for (float r = 32f; r <= phase2R && !hit; r += 32f)
                {
                    int n = Mathf.CeilToInt(2f * Mathf.PI * r / 32f);
                    for (int k = 0; k < n && !hit; k++)
                    {
                        float ang = k * 2f * Mathf.PI / n;
                        float x = at.x + Mathf.Cos(ang) * r, z = at.z + Mathf.Sin(ang) * r;
                        RegionWeights w = t.RegionAt(x, z, true, out float sf, out float cl);
                        if (w.Get(want) < 0.6f || sf < t.SeaLevel + 1.5f || cl > 0.2f) continue;
                        if (!FlatEnough(t, x, z, 2.5f)) continue;
                        // kolo 16: rovnou mimo koryto a jezero (rokle leží u řeky)
                        float rcl = t.RiverClearanceSearch(x, z, out float ld);
                        if (ld > 0.05f || rcl < 6f) continue;
                        at = new Vector3(x, 0f, z); hit = true;
                    }
                    if ((int)(r / 32f) % 8 == 0) yield return null;   // nezamrznout snímek
                }
                if (hit)
                {
                    t.Teleport(at, true);
                    yield return null;
                    until = Time.unscaledTime + 30f;
                    while ((t.IsTeleporting || !t.IsSettled) && Time.unscaledTime < until) yield return null;
                }
            }

            // Deterministicky: spirála od cíle po 8 m, první bod, který projde všemi kontrolami.
            Vector3 best = at; bool ok = false; int tries = 0;
            string lastWhy = "";
            var whyCount = new System.Collections.Generic.Dictionary<string, int>();
            float maxLocal = WaterBiome(want) ? 480f : 260f;
            for (float r = 0f; r <= maxLocal && !ok; r += r < 160f ? 8f : 12f)
            {
                int n = r < 1f ? 1 : Mathf.CeilToInt(2f * Mathf.PI * r / (r < 160f ? 8f : 12f));
                for (int k = 0; k < n && !ok; k++)
                {
                    float ang = k * 2f * Mathf.PI / n;
                    float x = at.x + Mathf.Cos(ang) * r, z = at.z + Mathf.Sin(ang) * r;
                    tries++;
                    if (SafeSpot(t, want, x, z, out float gy, out string why)) { ok = true; best = new Vector3(x, gy, z); }
                    else { lastWhy = why; whyCount.TryGetValue(why, out int wc); whyCount[why] = wc + 1; }
                }
            }
            // Kolo 16: oblast bez bezpečného bodu (celá u vody / na srázu) – zkusí se jiná oblast
            // téhož biomu (start 1,5 km stranou, ve čtyřech směrech), nejvýš třikrát.
            if (!ok && retry > 0)
            {
                for (int d = 0; d < 4; d++)
                {
                    float ang = (d * 90f + 45f) * Mathf.Deg2Rad;
                    float sx = at.x + Mathf.Cos(ang) * 1500f, sz = at.z + Mathf.Sin(ang) * 1500f;
                    if (FindRegion(t, sx, sz, want, 8000f, out Vector3 at2)
                        && (new Vector2(at2.x - at.x, at2.z - at.z)).sqrMagnitude > 600f * 600f)
                    {
                        Diag(string.Format(CultureInfo.InvariantCulture, "[biome tp] s{0} {1}: u ({2:0},{3:0}) není bezpečný bod ({4}) – zkouším oblast ({5:0},{6:0})",
                            t.WorldSeed, RegionKeys[want], at.x, at.z, lastWhy, at2.x, at2.z));
                        yield return SafeRegionTp(t, want, at2, look, yaw, retry - 1);
                        yield break;
                    }
                }
            }
            if (ok && (new Vector2(best.x - at.x, best.z - at.z)).sqrMagnitude > 0.25f)
            {
                t.Teleport(best, true);
                yield return null;
                until = Time.unscaledTime + 20f;
                while ((t.IsTeleporting || !t.IsSettled) && Time.unscaledTime < until) yield return null;
            }
            if (look)
            {
                pendingYaw = yaw; pendingPitch = 4f;
                pendingLook = true; pendingSince = Time.unscaledTime;
            }
            if (!look) { yield return null; yield return null; }   // ať FirstPersonController přepočte stav pohybu
            string fly = look ? "" : CheckFly();
            string msg = ok
                ? string.Format(CultureInfo.InvariantCulture, "[biome tp] s{0} {1}: bezpečný bod ({2:0},{3:0}) země {4:0.0} m, {5} zkoušek, {6:0} m od cíle",
                    t.WorldSeed, RegionKeys[want], best.x, best.z, best.y, tries, Vector2.Distance(new Vector2(best.x, best.z), new Vector2(at.x, at.z)))
                : string.Format(CultureInfo.InvariantCulture, "[biome tp] s{0} {1}: v okruhu " + maxLocal.ToString("0", CultureInfo.InvariantCulture) + " m od ({2:0},{3:0}) není bezpečný bod (naposled: {4}; důvody: {5}) – zůstávám na cíli",
                    t.WorldSeed, RegionKeys[want], at.x, at.z, lastWhy, string.Join(", ", System.Linq.Enumerable.Select(whyCount, kv => kv.Key + " " + kv.Value)));
            t.ReleaseSearchHydro();
            Diag(msg + (look ? "" : " | " + FlyText() + " | " + fly));
            if (!ok) Error(string.Format(CultureInfo.InvariantCulture, "No safe spot found within {0:0} m of ({1:0}, {2:0}) in {3} (last reason: {4}). You are at the target area; {5}.",
                maxLocal, at.x, at.z, RegionLabel(want), lastWhy, fly));
            else if (!look) Ok(string.Format(CultureInfo.InvariantCulture, "Arrived in {0} at ({1:0}, {2:0}), ground {3:0.0} m; {4}.",
                RegionLabel(want), best.x, best.z, best.y, fly));   // u snímkového „zeme" jen do diag.log – ať konzole nepřekrývá záběr
            search = null;
        }

        /// <summary>
        /// Kontrola pravidla „horké a chladné nikdy nesousedí": mřížka r × r, pásmo převládajícího
        /// regionu a klimatické t; vzdálenostní transformace od chladných buněk. Uloží i PNG mapu pásem.
        /// </summary>
        private static string ClimateCheck(VoxelTerrain t, float cx, float cz, float r, float step)
        {
            int n = Mathf.Clamp(Mathf.CeilToInt(2f * r / step), 16, 512);
            var band = new sbyte[n * n];      // -1 voda, jinak ClimateBand převládajícího regionu
            var cb = new sbyte[n * n];        // pásmo podle samotného klimatu (ClimateSample.Band)
            var k19 = new sbyte[n * n];       // kolo 19: 1 = převládá nový biom, 2 = zamrzlé (chladné pásmo / sníh)
            var k19cnt = new int[6];
            var kDom = new byte[n * n];
            var frozenSnow = new bool[n * n];
            var oldHot = new bool[n * n];
            TerrainPalette palC = t.Palette;
            var tex = new Texture2D(n, n, TextureFormat.RGB24, false);
            float sea = t.SeaLevel;
            int[] cnt = new int[6];   // kolo 21: index = ClimateBand (útesy mají pásmo Coastal = 5)
            int land = 0;
            for (int j = 0; j < n; j++)
            for (int i = 0; i < n; i++)
            {
                float x = cx - r + (i + 0.5f) * step, z = cz - r + (j + 0.5f) * step;
                RegionWeights w = t.RegionAt(x, z, false, out float surf, out _, out ClimateSample c);
                int k = j * n + i;
                if (surf < sea + 0.5f) { band[k] = -1; cb[k] = -1; tex.SetPixel(i, j, new Color(0.16f, 0.30f, 0.58f)); continue; }
                ClimateBand b = BiomeCatalog.BandOfRegion(w.Dominant());
                band[k] = (sbyte)b; cb[k] = (sbyte)c.Band; cnt[(int)b]++; land++;
                int dom = w.Dominant();
                t.ClimateAt(x, z, out float tRaw, out _);
                bool frozen = b == ClimateBand.Cold || surf > palC.snowTop + palC.snowTempShift * (tRaw - 0.5f) * 2f - 25f;
                bool isK19 = dom >= (int)BiomeRegion.Volcanic && dom <= (int)BiomeRegion.Oasis;   // kolo 20: jen balík kola 19
                k19[k] = (sbyte)(isK19 ? 1 : frozen ? 2 : 0);
                kDom[k] = (byte)dom;
                frozenSnow[k] = b != ClimateBand.Cold && frozen;
                oldHot[k] = dom < (int)BiomeRegion.Volcanic && b == ClimateBand.Hot && !frozen;
                if (isK19) k19cnt[dom - (int)BiomeRegion.Volcanic]++;
                Color col = c.hot * new Color(0.86f, 0.30f, 0.14f) + c.warm * new Color(0.93f, 0.68f, 0.26f)
                          + c.mild * new Color(0.36f, 0.66f, 0.30f) + c.cold * new Color(0.42f, 0.62f, 0.90f);
                float shade = Mathf.Clamp01(0.85f + surf / 1500f);
                tex.SetPixel(i, j, col * shade);
            }
            for (int q = -2; q <= 2; q++) { tex.SetPixel(n / 2 + q, n / 2, Color.black); tex.SetPixel(n / 2, n / 2 + q, Color.black); }
            tex.Apply();
            string dir = Path.Combine(Path.GetDirectoryName(Application.dataPath), "_reports", "kolo14");
            Directory.CreateDirectory(dir);
            string file = string.Format(CultureInfo.InvariantCulture, "klima_s{0}_{1:0}_{2:0}.png", t.WorldSeed, cx, cz);
            File.WriteAllBytes(Path.Combine(dir, file), tex.EncodeToPNG());
            Object.Destroy(tex);

            // Kolo 19: odstup nových (horkých a teplých) biomů od zamrzlých míst – chladné pásmo nebo sníh.
            float dK19Cold = MinDist(k19, n, 1, 2, out int kAt) * step;
            // kde: nejbližší dvojice (nový biom → zamrzlé) a čím je zamrzlé (chladné pásmo / sníh)
            for (int k = 0; k < k19.Length; k++) if (k19[k] == 2 && frozenSnow[k]) k19[k] = 3;
            float dK19Snow = MinDist(k19, n, 1, 3, out _) * step;
            // srovnání: dosavadní horké regiony (poušť, step v horkém pásmu, mesa) ↔ sníh na štítech
            for (int k = 0; k < k19.Length; k++) if (k19[k] == 0 && oldHot[k]) k19[k] = 4;
            float dOldHotSnow = MinDist(k19, n, 4, 3, out _) * step;
            for (int k = 0; k < k19.Length; k++) if (k19[k] == 4) k19[k] = 0;
            for (int k = 0; k < k19.Length; k++) if (k19[k] == 3) k19[k] = 0;   // pásmo bez sněhových štítů
            float dK19Band = MinDist(k19, n, 1, 2, out _) * step;
            // jen horké nové biomy (vulkan, zkamenely, oaza) ↔ chladné pásmo
            for (int k = 0; k < k19.Length; k++) if (k19[k] == 1 && (kDom[k] == (int)BiomeRegion.Volcanic || kDom[k] == (int)BiomeRegion.Petrified || kDom[k] == (int)BiomeRegion.Oasis)) k19[k] = 5;
            float dK19HotBand = MinDist(k19, n, 5, 2, out _) * step;
            for (int k = 0; k < k19.Length; k++) if (frozenSnow[k] && k19[k] == 0) k19[k] = 3;
            float dK19HotSnow = MinDist(k19, n, 5, 3, out _) * step;
            for (int k = 0; k < k19.Length; k++) { if (k19[k] == 5) k19[k] = 1; if (k19[k] == 3) k19[k] = 2; }
            string kWhere = kAt >= 0 ? string.Format(CultureInfo.InvariantCulture, "{0} u ({1:0},{2:0})", RegionKeys[kDom[kAt]],
                cx - r + (kAt % n + 0.5f) * step, cz - r + (kAt / n + 0.5f) * step) : "—";
            float dRegHotCold = MinDist(band, n, (sbyte)ClimateBand.Hot, (sbyte)ClimateBand.Cold, out int hcAt) * step;
            // kolo 20 (diagnostika): nejbližší dvojice horký↔chladný region – které regiony to jsou
            string hcWhere = "—";
            if (hcAt >= 0)
            {
                int bi = -1; float bd = float.MaxValue; int ai = hcAt % n, aj = hcAt / n;
                for (int k = 0; k < band.Length; k++)
                    if (band[k] == (sbyte)ClimateBand.Cold) { float dd = (k % n - ai) * (k % n - ai) + (k / n - aj) * (k / n - aj); if (dd < bd) { bd = dd; bi = k; } }
                if (bi >= 0) hcWhere = string.Format(CultureInfo.InvariantCulture, "{0} u ({1:0},{2:0}) ↔ {3} u ({4:0},{5:0})",
                    RegionKeys[kDom[hcAt]], cx - r + (ai + 0.5f) * step, cz - r + (aj + 0.5f) * step,
                    RegionKeys[kDom[bi]], cx - r + (bi % n + 0.5f) * step, cz - r + (bi / n + 0.5f) * step);
            }
            float dRegWarmCold = MinDist(band, n, (sbyte)ClimateBand.Warm, (sbyte)ClimateBand.Cold) * step;
            float dCliHotCold = MinDist(cb, n, (sbyte)ClimateBand.Hot, (sbyte)ClimateBand.Cold) * step;
            string k20 = Climate20(t, cx, cz, r, step, n);
            var sb = new StringBuilder();
            sb.AppendFormat(CultureInfo.InvariantCulture,
                "[biome klima] s{0} střed ({1:0},{2:0}) r {3:0} m, krok {4:0} m → {5} | souš: horké {6:0.0}% teplé {7:0.0}% mírné {8:0.0}% chladné {9:0.0}%"
                + " | nejmenší vzdálenost horký↔chladný region {10} [" + hcWhere + "], teplý↔chladný {11}, klima horké↔chladné {12} | pravidlo {13}"
                + " | kolo 19: nové biomy ↔ zamrzlé (chladné pásmo/sníh) {14} [chladné pásmo {21}, sníh na štítech {22} (dosavadní horké regiony ↔ sníh {24}); jen horké nové (vulkan/zkamenely/oaza): chladné pásmo {25}, sníh {26}; nejblíž {23}]; podíl souše vulkan {15:0.00}% spaleny {16:0.00}% kras {17:0.00}% zkamenely {18:0.00}% ruiny {19:0.00}% oaza {20:0.00}%",
                t.WorldSeed, cx, cz, r, step, file,
                land > 0 ? 100f * cnt[0] / land : 0f, land > 0 ? 100f * cnt[1] / land : 0f,
                land > 0 ? 100f * cnt[2] / land : 0f, land > 0 ? 100f * cnt[3] / land : 0f,
                Fmt(dRegHotCold), Fmt(dRegWarmCold), Fmt(dCliHotCold),
                dRegHotCold >= 1000f && dCliHotCold >= 1000f ? "OK (≥ 1 km)" : "PORUŠENO",
                Fmt(dK19Cold), Pct(k19cnt[0], land), Pct(k19cnt[1], land), Pct(k19cnt[2], land), Pct(k19cnt[3], land), Pct(k19cnt[4], land), Pct(k19cnt[5], land), Fmt(dK19Band), Fmt(dK19Snow), kWhere, Fmt(dOldHotSnow), Fmt(dK19HotBand), Fmt(dK19HotSnow));
            sb.Append(k20);
            return sb.ToString();
        }

        /// <summary>
        /// Kolo 20: oprava klimatu a nové biomy. Na téže mřížce porovná sníh BEZ zvednutí čáry (kolo 19) a
        /// S ním (kolo 20, stejná funkce jako paleta, bez šumu ±38 m, a s rezervou 25 m dolů):
        /// vodorovná vzdálenost horkých/teplých regionů (všech, starých i nových) od sněhu, svislý odstup
        /// (o kolik leží pod sněžnou čarou – nejmenší hodnota) a vzdálenost nových chladných biomů od teplých.
        /// </summary>
        private static string Climate20(VoxelTerrain t, float cx, float cz, float r, float step, int n)
        {
            TerrainPalette pal = t.Palette;
            float sea = t.SeaLevel;
            var hwSnow = new sbyte[n * n]; var hwOld = new sbyte[n * n]; var hwNew = new sbyte[n * n]; var nw = new sbyte[n * n]; var nw2 = new sbyte[n * n]; var cw = new sbyte[n * n];
            var cnt = new int[BiomeMath.Count - (int)BiomeRegion.SnowPeaks];   // kolo 21: i nové regiony (dřív jen 6 → mimo rozsah)
            int land = 0, seaAll = 0, seaFrozen = 0;
            float vOld = float.MaxValue, vNew = float.MaxValue, vNewK = float.MaxValue, maxHW = 0f;
            Vector2 vOldAt = default, vNewAt = default;
            for (int j = 0; j < n; j++)
            for (int i = 0; i < n; i++)
            {
                float x = cx - r + (i + 0.5f) * step, z = cz - r + (j + 0.5f) * step;
                RegionWeights w = t.RegionAt(x, z, false, out float surf, out _, out ClimateSample c);
                int k = j * n + i;
                int dom = w.Dominant();
                if (surf < sea + 0.5f)
                {
                    if (dom == (int)BiomeRegion.FrozenOcean) seaFrozen++;
                    seaAll++;
                    // moře: kry zamrzlého oceánu jsou „led" pro odstup teplých biomů
                    sbyte frozenSea = (sbyte)(dom == (int)BiomeRegion.FrozenOcean ? 2 : 0);
                    hwOld[k] = frozenSea; hwNew[k] = frozenSea; hwSnow[k] = 0; nw[k] = frozenSea; nw2[k] = frozenSea; cw[k] = 0;
                    continue;
                }
                land++;
                if (dom >= (int)BiomeRegion.SnowPeaks) cnt[dom - (int)BiomeRegion.SnowPeaks]++;
                t.ClimateAt(x, z, out float tRaw, out _);
                float baseLine = pal.snowTop + pal.snowTempShift * (tRaw - 0.5f) * 2f;
                float lineNew = baseLine + BiomeMath.SnowLift(c.t) - 70f * w.snowPeaks - 45f * w.glacier;
                bool snowOld = surf > baseLine - 25f, snowNew = surf > lineNew - 25f;
                ClimateBand b = BiomeCatalog.BandOfRegion(dom);
                bool hw = b == ClimateBand.Hot || b == ClimateBand.Warm;
                bool newHW = hw && dom >= (int)BiomeRegion.Volcanic;   // nové horké/teplé (kolo 19 + mangrovy, sůl, geotermál)
                bool icy20 = dom == (int)BiomeRegion.SnowPeaks || dom == (int)BiomeRegion.Glacier || dom == (int)BiomeRegion.FrozenOcean;
                hwOld[k] = (sbyte)(hw ? 1 : snowOld ? 2 : 0);
                hwSnow[k] = (sbyte)(hw ? 1 : snowNew ? 2 : 0);   // jen sníh palety (bez nových chladných biomů)
                hwNew[k] = (sbyte)(hw ? 1 : snowNew || icy20 ? 2 : 0);
                nw[k] = (sbyte)(newHW ? 1 : snowNew || icy20 ? 2 : 0);
                nw2[k] = (sbyte)(newHW ? 1 : snowOld ? 2 : 0);
                cw[k] = (sbyte)(icy20 ? 1 : hw ? 2 : 0);
                if (hw)
                {
                    maxHW = Mathf.Max(maxHW, surf);
                    if (baseLine - surf < vOld) { vOld = baseLine - surf; vOldAt = new Vector2(x, z); }
                    if (lineNew - surf < vNew) { vNew = lineNew - surf; vNewAt = new Vector2(x, z); }
                    if (newHW) vNewK = Mathf.Min(vNewK, lineNew - surf);
                }
            }
            float dOld = MinDist(hwOld, n, 1, 2) * step, dNew = MinDist(hwNew, n, 1, 2) * step, dSnow = MinDist(hwSnow, n, 1, 2) * step;
            float dNK = MinDist(nw, n, 1, 2) * step, dNKold = MinDist(nw2, n, 1, 2) * step;
            float dCold = MinDist(cw, n, 1, 2) * step;
            return string.Format(CultureInfo.InvariantCulture,
                " | KOLO 20 klima: horké/teplé regiony ↔ sníh/led vodorovně: před {0} → po {1} (jen sníh palety po: {20}); nové horké/teplé ↔ sníh/led: před {2} → po {3};"
                + " svisle (nejmenší výška pod sněžnou čarou, horké/teplé): před {4:0} m u ({5:0},{6:0}) → po {7:0} m u ({8:0},{9:0}), nové {10:0} m; nejvyšší horký/teplý povrch {11:0} m;"
                + " nové chladné (štíty/ledovec/zamrzlý) ↔ horké/teplé regiony {12} | podíl souše: zasnezene_stity {13:0.00}% ledovec {14:0.00}% zamrzly_ocean {15:0.00}% (zamrzlé moře {19:0.0}% moře) mangrovy {16:0.00}% solne_plane {17:0.00}% geotermal {18:0.00}%"
                + " | kolo 21 podíl souše: krystaly {21:0.00}% houby {22:0.00}% utesy {23:0.00}%"
                + " | kolo 28 podíl souše: basalt_columns_coast {24:0.00}% obsidian_plain {25:0.00}% alabaster_plateau {26:0.00}%",
                Fmt(dOld), Fmt(dNew), Fmt(dNKold), Fmt(dNK), vOld, vOldAt.x, vOldAt.y, vNew, vNewAt.x, vNewAt.y, vNewK, maxHW, Fmt(dCold),
                Pct(cnt[0], land), Pct(cnt[1], land), Pct(cnt[2], land), Pct(cnt[3], land), Pct(cnt[4], land), Pct(cnt[5], land), Pct(seaFrozen, seaAll), Fmt(dSnow),
                Pct(cnt[6], land), Pct(cnt[7], land), Pct(cnt[8], land), Pct(cnt[9], land), Pct(cnt[10], land), Pct(cnt[11], land));
        }

        private static float Pct(int a, int b) => b > 0 ? 100f * a / b : 0f;

        private static string Fmt(float d) => d > 1e6f ? "—" : d.ToString("0", CultureInfo.InvariantCulture) + " m";

        /// <summary>Nejmenší vzdálenost (v buňkách) mezi buňkami pásma A a B – dvouprůchodová chamfer transformace od B.</summary>
        private static float MinDist(sbyte[] band, int n, sbyte a, sbyte b) => MinDist(band, n, a, b, out _);

        private static float MinDist(sbyte[] band, int n, sbyte a, sbyte b, out int at)
        {
            at = -1;
            const float INF = 1e9f, D1 = 1f, D2 = 1.41421356f;
            var dist = new float[n * n];
            bool anyB = false, anyA = false;
            for (int k = 0; k < dist.Length; k++) { dist[k] = band[k] == b ? 0f : INF; anyB |= band[k] == b; anyA |= band[k] == a; }
            if (!anyA || !anyB) return INF * 10f;
            for (int j = 0; j < n; j++)
            for (int i = 0; i < n; i++)
            {
                int k = j * n + i; float d = dist[k];
                if (i > 0) d = Mathf.Min(d, dist[k - 1] + D1);
                if (j > 0)
                {
                    d = Mathf.Min(d, dist[k - n] + D1);
                    if (i > 0) d = Mathf.Min(d, dist[k - n - 1] + D2);
                    if (i < n - 1) d = Mathf.Min(d, dist[k - n + 1] + D2);
                }
                dist[k] = d;
            }
            for (int j = n - 1; j >= 0; j--)
            for (int i = n - 1; i >= 0; i--)
            {
                int k = j * n + i; float d = dist[k];
                if (i < n - 1) d = Mathf.Min(d, dist[k + 1] + D1);
                if (j < n - 1)
                {
                    d = Mathf.Min(d, dist[k + n] + D1);
                    if (i < n - 1) d = Mathf.Min(d, dist[k + n + 1] + D2);
                    if (i > 0) d = Mathf.Min(d, dist[k + n - 1] + D2);
                }
                dist[k] = d;
            }
            float best = INF;
            for (int k = 0; k < dist.Length; k++) if (band[k] == a && dist[k] < best) { best = dist[k]; at = k; }
            return best;
        }

        private static bool FindSpot(VoxelTerrain t, float x0, float z0, string kind, int wa, int wb, float maxR, out Vector3 at)
        {
            at = default;
            const float step = 96f;
            float sea = t.SeaLevel;
            TerrainPalette pal = t.Palette;
            for (float r = 0f; r <= maxR; r += step)
            {
                int n = Mathf.Max(1, Mathf.CeilToInt(2f * Mathf.PI * r / step));
                for (int k = 0; k < n; k++)
                {
                    float ang = k * 2f * Mathf.PI / n;
                    float x = x0 + Mathf.Cos(ang) * r, z = z0 + Mathf.Sin(ang) * r;
                    RegionWeights w = t.RegionAt(x, z, WaterBiome(wa) || WaterBiome(wb), out float surf, out float cl);
                    if (kind == "hory")
                    {
                        if (surf < pal.grassTop + 70f || surf > pal.grassTop + 220f || cl > 0.3f) continue;
                    }
                    else if (kind == "pobrezi")
                    {
                        if (surf < sea + 3f || surf > sea + 9f || w.Get(wa) < 0.7f) continue;
                        t.RegionAt(x, z + 120f, false, out float sN, out _);
                        if (sN > sea - 3f) continue; // moře na sever (do záběru)
                    }
                    else
                    {
                        if (surf < sea + 4f || surf > pal.grassTop + (wa == 7 || wb == 7 ? 120f : 20f)) continue;
                        float A = w.Get(wa), B = w.Get(wb);
                        if (A < 0.3f || B < 0.3f || A + B < 0.85f) continue;
                        // ať je vidět oba celky: kus na jednu stranu A, na druhou B
                        bool ok = false;
                        for (int d = 0; d < 4 && !ok; d++)
                        {
                            float dx = d == 0 ? 150f : d == 1 ? -150f : 0f, dz = d == 2 ? 150f : d == 3 ? -150f : 0f;
                            RegionWeights p1 = t.RegionAt(x + dx, z + dz, WaterBiome(wa) || WaterBiome(wb), out _, out _);
                            RegionWeights p2 = t.RegionAt(x - dx, z - dz, WaterBiome(wa) || WaterBiome(wb), out _, out _);
                            ok = p1.Get(wa) > 0.6f && p2.Get(wb) > 0.6f;
                        }
                        if (!ok) continue;
                    }
                    at = new Vector3(x, 0f, z);
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// Kolo 17: skutečný bod vodního biomu (rokle, mokřad) – stejná kritéria jako druhá fáze
        /// bezpečného teleportu: převaha ≥ 60 %, souš, ne terasa, rovina, mimo koryto a jezero.
        /// </summary>
        private static bool WaterCandidate(VoxelTerrain t, int want, float x, float z, out Vector3 at)
        {
            at = default;
            float sea = t.SeaLevel;
            t.RegionAt(x, z, false, out float sd, out float cd, out ClimateSample c);
            if (cd > 0.2f || sd < sea + 1.5f) return false;
            if (want == (int)BiomeRegion.FernGorge)
            {
                // rokle: gorge > 0 jen 18+ m nad mořem, vlhkost nad 0,54, mírné nebo teplé vlhké pásmo
                if (c.above < 18f || c.h < 0.54f || c.mild + c.warm * c.wet < 0.05f) return false;
            }
            else if (want == (int)BiomeRegion.Oasis)
            {
                // kolo 19: oáza – horké pásmo, suché/střední, 6+ m nad mořem (ne mořský břeh)
                if (c.hot < 0.3f || c.above < 14f || c.h > 0.62f) return false;
            }
            else if (c.above > 30f || c.h < 0.42f || c.cold > 0.95f || c.hot > 0.95f) return false;   // mokřad: nížina, vlhko
            RegionWeights w = t.RegionAt(x, z, true, out float sf, out float cl);
            if (w.Dominant() != want || w.Get(want) < 0.6f || sf < sea + 1.5f || cl > 0.2f) return false;
            if (!FlatEnough(t, x, z, 2.5f)) return false;
            float rcl = t.RiverClearanceSearch(x, z, out float ld);
            if (ld > 0.05f || rcl < 6f) return false;
            at = new Vector3(x, 0f, z);
            return true;
        }

        /// <summary>
        /// Kolo 17: jeden bod hledání oblasti (vyňato z FindRegion beze změny kritérií, aby ho mohlo
        /// použít i vzdálené hledání po snímcích – <see cref="FarRegionTp"/>).
        /// </summary>
        private static bool RegionPoint(VoxelTerrain t, int want, float x, float z, out Vector3 at)
        {
            // Reprezentativní vnitřek oblasti (kolo 14b): ne pobřeží (stromy tam pravidla nepustí), ne
            // terasovitý svah (u mesy naopak terasy), převládající ≥ 80 % a ≥ 70 % i 120 m do všech stran.
            at = default;
            float sea = t.SeaLevel;
            TerrainPalette pal = t.Palette;
            bool water = WaterBiome(want);
            if (water)
            {
                // Kolo 17: vodní biom se hledá rovnou s hydrologií (dostaví se i daleko od
                // hráče, viz VoxelTerrain.SearchHydroRef). Levné klimatické síto napřed, ať se
                // hydrologie staví jen tam, kde biom vůbec může vzniknout (podmínky z BiomeMath.Weights).
                if (WaterCandidate(t, want, x, z, out Vector3 wat)) { at = wat; return true; }
                return false;
            }
            RegionWeights w = t.RegionAt(x, z, water, out float surf, out float cl);
            // Kolo 20: pobřežní biomy (mangrovy, zamrzlý oceán) jsou úzký pás nízkého břehu – vlastní kritéria.
            bool coast20 = want == (int)BiomeRegion.Mangrove || want == (int)BiomeRegion.FrozenOcean || want == (int)BiomeRegion.Cliffs
                        || want == (int)BiomeRegion.BasaltCoast;   // kolo 21: útesy, kolo 28: čedičové pobřeží
            bool high20 = want == (int)BiomeRegion.SnowPeaks || want == (int)BiomeRegion.Glacier || want == (int)BiomeRegion.Crystal;   // kolo 21: krystaly na pahorkatinách
            if (coast20) return CoastPoint20(t, want, x, z, w, surf, cl, out at);
            float lo = sea + (water ? 1f : 12f);
            float hi = pal.grassTop + (want == 7 ? 30f : want == 5 ? 40f : -15f);
            if (want == (int)BiomeRegion.SnowPeaks) hi = 5000f;
            else if (want == (int)BiomeRegion.Glacier) hi = 450f;
            else if (want == (int)BiomeRegion.Geothermal) hi = sea + 160f;
            else if (want == (int)BiomeRegion.Crystal) hi = sea + 230f;   // kolo 21
            else if (want == (int)BiomeRegion.ObsidianPlain) hi = sea + 175f;   // kolo 28
            else if (want == (int)BiomeRegion.AlabasterPlateau) hi = sea + 420f;   // kolo 28: plošiny a teplé masivy
            if (surf < lo || surf > hi) return false;
            if (want == 5 ? cl < 0.25f : cl > (high20 ? 0.5f : 0.2f)) return false;
            float need = want == 5 ? 0.65f : high20 ? 0.7f : 0.8f;
            if (w.Dominant() != want || w.Get(want) < need) return false;
            if (!FlatEnough(t, x, z, want == 5 ? 6f : 3.5f)) return false;
            // kolo 16: suchozemský cíl ne těsně u řeky/jezera (spirála bezpečného bodu je jen 260 m)
            // kolo 17: s hydrologií i daleko od hráče – výsledek nezávisí na výchozí pozici
            if (!water && t.RiverClearanceSearch(x, z, out float ldF) < 14f) return false;
            bool ok = true;
            // kolo 20: geotermální pole a solné pláně jsou menší ostrůvky – okolí 70 m
            float ring = want == (int)BiomeRegion.Geothermal || want == (int)BiomeRegion.SaltFlat ? 70f : 120f;
            for (int d = 0; d < 4 && ok; d++)
            {
                float dx = d == 0 ? ring : d == 1 ? -ring : 0f, dz = d == 2 ? ring : d == 3 ? -ring : 0f;
                ok = t.RegionAt(x + dx, z + dz, water, out _, out _).Get(want) >= need - 0.15f;
            }
            if (!ok) return false;
            at = new Vector3(x, 0f, z);
            return true;
        }

        /// <summary>
        /// Kolo 20: bod pobřežního biomu – nízký břeh 2–7 m nad mořem, biom převládá ≥ 60 % a do 160 m je
        /// moře téhož biomu (zamrzlé moře s krami / laguna mangrov). Rovina, mimo koryto a jezero.
        /// </summary>
        private static bool CoastPoint20(VoxelTerrain t, int want, float x, float z, RegionWeights w, float surf, float cl, out Vector3 at)
        {
            at = default;
            float sea = t.SeaLevel;
            if (surf < sea + 2f || surf > sea + 7f || cl > 0.25f) return false;
            if (w.Dominant() != want || w.Get(want) < 0.6f) return false;
            if (!FlatEnough(t, x, z, 2.5f)) return false;
            bool seaNear = false;
            // kolo 21: útesy jen u skutečného moře (dno ≥ 3 m aspoň ve 2 směrech) – ne u řeky v nízkém údolí
            bool cliffs21 = want == (int)BiomeRegion.Cliffs || want == (int)BiomeRegion.BasaltCoast;   // kolo 28: čedič také jen u skutečného moře
            int seaDirs = 0;
            for (int d = 0; d < 8 && (cliffs21 ? seaDirs < 2 : !seaNear); d++)
            {
                float ang = d * Mathf.PI * 0.25f;
                bool dirSea = false;
                for (float rr = 40f; rr <= 160f && !dirSea; rr += 40f)
                {
                    RegionWeights ws = t.RegionAt(x + Mathf.Cos(ang) * rr, z + Mathf.Sin(ang) * rr, false, out float sS, out _);
                    dirSea = sS < sea - (want == (int)BiomeRegion.FrozenOcean || cliffs21 ? 3f : 0.5f) && ws.Get(want) >= (cliffs21 ? 0.3f : 0.5f);
                }
                if (dirSea) { seaDirs++; seaNear = !cliffs21 || seaDirs >= 2; }
            }
            if (!seaNear) return false;
            if (t.RiverClearanceSearch(x, z, out float ld) < 14f || ld > 0.05f) return false;
            at = new Vector3(x, 0f, z);
            return true;
        }

        private static bool FindRegion(VoxelTerrain t, float x0, float z0, int want, float maxR, out Vector3 at, bool waterFallback = true)
        {
            at = default;
            const float step = 96f;
            float sea = t.SeaLevel;
            bool water = WaterBiome(want);
            for (float r = 0f; r <= maxR; r += step)
            {
                int n = Mathf.Max(1, Mathf.CeilToInt(2f * Mathf.PI * r / step));
                for (int k = 0; k < n; k++)
                {
                    float ang = k * 2f * Mathf.PI / n;
                    float x = x0 + Mathf.Cos(ang) * r, z = z0 + Mathf.Sin(ang) * r;
                    if (RegionPoint(t, want, x, z, out at)) return true;
                }
            }
            if (water && waterFallback)
            {
                // Mokřad leží u vody, kterou zná až postavená hydrologie. Daleko od hráče proto
                // hledáme vlhkou mírnou/teplou nížinu; přesný bod u vody dohledá bezpečný teleport.
                for (float r = 0f; r <= maxR; r += step)
                {
                    int n = Mathf.Max(1, Mathf.CeilToInt(2f * Mathf.PI * r / step));
                    for (int k = 0; k < n; k++)
                    {
                        float ang = k * 2f * Mathf.PI / n;
                        float x = x0 + Mathf.Cos(ang) * r, z = z0 + Mathf.Sin(ang) * r;
                        t.RegionAt(x, z, false, out float surf, out float cl, out ClimateSample c);
                        bool gorge = want == (int)BiomeRegion.FernGorge;
                        if (want == (int)BiomeRegion.Oasis)
                        {
                            // kolo 19: horká suchá nížina u řeky/jezera (oáza nevzniká bez existující vody)
                            if (surf < sea + 14f || surf > sea + 70f || cl > 0.2f || c.hot < 0.5f || c.h > 0.6f) continue;
                            if (t.RiverClearanceSearch(x, z, out _) > 60f) continue;
                        }
                        else
                        {
                        if (surf < sea + (gorge ? 18f : 2f) || surf > sea + (gorge ? 90f : 22f) || cl > 0.2f) continue;
                        if (c.h < (gorge ? 0.58f : 0.55f) || c.cold > 0.1f || c.hot > 0.1f) continue;
                        }
                        if (!FlatEnough(t, x, z, 2.5f)) continue;
                        at = new Vector3(x, 0f, z);
                        return true;
                    }
                }
            }
            return false;
        }
    }
}
