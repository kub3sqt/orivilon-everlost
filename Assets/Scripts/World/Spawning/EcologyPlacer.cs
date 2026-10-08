using System.Collections.Generic;
using Orivilon.World.Spawning;
using Unity.Mathematics;
using UnityEngine;

namespace Orivilon.World.Generation
{
    /// <summary>Čtyři krajinné celky, které osazovač rozlišuje. Mezi nimi se plynule přechází.</summary>
    public enum EcoBiome : byte
    {
        Meadow = 0,     // teplé otevřené louky a mírné svahy s háji
        Riparian = 1,   // nivy a břehy řek a jezer
        Alpine = 2,     // skalnaté horské polohy
        Coast = 3,      // mořské pobřeží, pláže a útesy
    }

    /// <summary>
    /// Ekologické osazení jednoho LOD0 sloupce: z výšky, sklonu, vzdálenosti a výšky nad vodou,
    /// klimatu a seedu spočte seznam pozic pro <see cref="ObjectSpawner.SpawnPlacements"/>.
    ///
    /// <para><b>Determinismus.</b> Kandidáti leží na světové mřížce (každá vrstva má vlastní krok)
    /// a los je hash (seed, buňka, vrstva) – žádný System.Random ani stav mezi sloupci. Sloupec
    /// vyjde stejně bez ohledu na pořadí načtení. Stromy a velké kameny se kvůli rozestupům
    /// vyhodnocují i v okraji za hranou sloupce (jen z pole, bez výstupu), takže o nich ví
    /// i sousední sloupec a keř ani kámen nevyroste do kmene přes hranici.</para>
    ///
    /// <para><b>Výška.</b> Pozice se usazuje na skutečný mesh LOD0 (kolizní mesh chunků sloupce),
    /// ne jen na výškové pole – převisy, boule a vstupy jeskyní ho mohou lokálně posunout.
    /// Kde se mesh od pole liší nebo má nad sebou další vrstvu, se nesází vůbec.</para>
    /// </summary>
    public static class EcologyPlacer
    {
        // ── druhy (jména položek ve spawnables) ─────────────────────────
        private static readonly string[] OakNames = { "Oak Tree 01", "Oak Tree 02", "Oak Tree 03", "Oak Tree 04", "Oak Tree 05" };
        private static readonly string[] BirchNames = { "Birch Tree 01", "Birch Tree 02", "Birch Tree 03", "Birch Tree 04", "Birch Tree 05" };
        private static readonly string[] FirNames = { "Fir Tree 01", "Fir Tree 02", "Fir Tree 03" };
        private static readonly string[] FirYoungNames = { "Fir Sapling" };
        private static readonly string[] FirDeadNames = { "Dead Fir" };
        private static readonly string[] RockLargeNames = { "Large Rock 01", "Large Rock 02", "Large Rock 03", "Stone 09", "Stone 13", "Stone 21" };
        private static readonly string[] RockCliffNames = { "Rock Cliff 01", "Rock Cliff 02", "Rock Cliff 03", "Stone 16", "Stone 18", "Stone 20" };
        // Jen kamínky do ~3 m. Velké kusy ze sady (09, 13, 16, 18, 20, 21) jsou ve vrstvě balvanů.
        private static readonly string[] StoneNames = {
            "Stone 02", "Stone 03", "Stone 04", "Stone 05", "Stone 06", "Stone 07", "Stone 08", "Stone 10", "Stone 11",
            "Stone 12", "Stone 14", "Stone 15", "Stone 17", "Stone 19" };
        // Meziúkol: sebratelný kamínek (prefab „Stone 1" s PickupItem). Vlastní vrstva až po
        // trávě se samostatným seedem, takže rozmístění všech ostatních vrstev zůstává stejné.
        private static readonly string[] PebbleNames = { "Stone 01" };
        // Meziúkol: zelené keře „Bush 01/02" (kmen + listí, kreslí se celé jako GameObject – viz
        // GetInstancedSource) a jen občas sebratelný mrtvý keř (DeadBushNames, PickupItem → Stick).
        private static readonly string[] BushNames = { "Bush 01", "Bush 02" };
        private static readonly string[] BushLowNames = { "Bush 01" };
        private static readonly string[] FlowerBushNames = { "Flower Bush 01", "Flower Bush 02" };
        private static readonly string[] DeadBushNames = { "Dead Bush Short", "Dead Bush Tall" };
        private static readonly string[] LogNames = { "Birch Log", "Oak Log", "Fir Log" };
        private static readonly string[] StumpNames = { "Birch Stump", "Broadleaf Stump", "Fir Stump" };
        private static readonly string[] FlowerNames = { "Flower Cherry Blossom", "Flower Lavender" };
        private static readonly string[] MushroomNames = { "Mushroom Amanita", "Mushroom Liberty Cap" };
        private static readonly string[] GrassHighNames = { "Grass_High" };
        private static readonly string[] GrassMedNames = { "Grass_Medium" };
        private static readonly string[] GrassLowNames = { "Grass_Low" };
        private static readonly string[] Grass2Names = { "Grass02" };
        private static readonly string[] CloverNames = { "Clovers" };
        private static readonly string[] FernNames = { "Fern High", "Fern Low" };
        private static readonly string[] ReedNames = { "Reed Grass", "Swamp Reeds", "Reed 1 [F]", "Reed 2 [F]", "Reed 3 [F]" };

        // ── kolo 14: druhy pro klimatické regiony (existující položky + schválená sada StyleMatched,
        //    kterou do spawnables přidává ObjectSpawner.EnsureBiomeProps z Resources/BiomeProps) ──
        private static readonly string[] DeadTreeNames = { "Dead Tree A", "Dead Tree B" };
        private static readonly string[] DeadBroadNames = { "Dead Broadleaf 01", "Dead Broadleaf 02", "Dead Broadleaf 03", "Dead Broadleaf 04", "Dead Broadleaf 05" };
        private static readonly string[] DeadBirchNames = { "Dead Birch 01", "Dead Birch 02", "Dead Birch 03", "Dead Birch 04", "Dead Birch 05" };
        private static readonly string[] CactusNames = { "Cactus 1", "Cactus 2", "Cactus 3", "Cactus 4", "Cactus 5" };
        private static readonly string[] DryShrubNames = { "Dead Bush 1", "Dead Bush 2", "Dead Bush 3", "SM_Ker_Suchy" };
        private static readonly string[] SwampReedNames = { "Swamp Reeds", "Reed Grass" };
        private static readonly string[] FernAllNames = { "Fern High", "Fern Low", "SM_Kapradi", "SM_Kapradi_Male" };
        private static readonly string[] SmBirchNames = { "SM_Strom_Briza", "SM_Strom_Briza_Dvojita" };
        private static readonly string[] SmBroadNames = { "SM_Strom_Listnaty_Kulaty", "SM_Strom_Listnaty_Stihly", "SM_Strom_Listnaty_Rozlozity", "SM_Strom_Listnaty_Mlady", "SM_Strom_Listnaty_Krivy" };
        private static readonly string[] SmAcaciaNames = { "SM_Strom_Akacie", "SM_Strom_Akacie_Siroka" };
        private static readonly string[] SmBoulderNames = { "SM_Balvan_Mechovy", "SM_Balvan_Kulaty", "SM_Balvan_Velky" };
        private static readonly string[] SmMesaRockNames = { "SM_Balvan_Mesa", "SM_Skala_Mesa_Blok", "SM_Skala_Mesa_Vychoz" };
        private static readonly string[] SmMesaStoneNames = { "SM_Kamen_Mesa", "SM_Kamen_Mesa_Plochy" };
        private static readonly string[] SmDesertRockNames = { "SM_Balvan_Pouste", "SM_Skala_Pouste" };
        private static readonly string[] SmDesertStoneNames = { "SM_Kamen_Pouste", "SM_Oblazky_Pouste" };
        private static readonly string[] SmStoneNames = { "SM_Kamen_Maly_A", "SM_Kamen_Maly_B", "SM_Kamen_Plochy", "SM_Oblazky_Skupina" };
        private static readonly string[] SmTundraShrubNames = { "SM_Ker_Tundra", "SM_Ker_Tundra_Nizky" };
        private static readonly string[] SmBushNames = { "SM_Ker_Kulaty", "SM_Ker_Nizky_Siroky", "SM_Ker_Vysoky" };
        private static readonly string[] SmGrassDryNames = { "SM_Trava_Sucha" };
        private static readonly string[] SmGrassNames = { "SM_Trava_Trs", "SM_Trava_Vysoka", "SM_Trava_Louka_Siroka" };
        private static readonly string[] SmWoodNames = { "SM_Parez", "SM_Kmen_Padly", "SM_Kmen_Briza", "SM_Vetev_Spadla" };
        // kolo 16
        private static readonly string[] SmDarkFirNames = { "SM_Smrk_Tmavy", "SM_Smrk_Tmavy_2" };
        private static readonly string[] SmDarkFirLowNames = { "SM_Smrk_Tmavy_Nizky" };
        private static readonly string[] SmSequoiaNames = { "SM_Sekvoje" };
        private static readonly string[] SmSequoiaYoungNames = { "SM_Sekvoje_Mlada" };
        private static readonly string[] SmSakuraNames = { "SM_Sakura", "SM_Sakura_Mlada" };
        private static readonly string[] SmBambooNames = { "SM_Bambus_Shluk" };
        private static readonly string[] SmBambooLowNames = { "SM_Bambus_Nizky" };
        private static readonly string[] SmJungleNames = { "SM_Strom_Dzungle", "SM_Strom_Dzungle_Velky" };
        private static readonly string[] SmTreeFernNames = { "SM_Kapradi_Stromova", "SM_Kapradi_Stromova_Mala" };
        private static readonly string[] SmBigLeafNames = { "SM_Rostlina_Velkolista" };
        private static readonly string[] SmHorsetailNames = { "SM_Preslicky" };
        private static readonly string[] SmHeatherNames = { "SM_Vres", "SM_Vres_Nizky" };
        private static readonly string[] SmMonolithNames = { "SM_Monolit", "SM_Monolit_Skupina" };
        private static readonly string[] SmLightStoneNames = { "SM_Kamen_Svetly" };
        private static readonly string[] SmLightBoulderNames = { "SM_Balvan_Svetly" };
        // kolo 19 – třetí balík (Biomy3)
        private static readonly string[] SmLavaSpireNames = { "SM_Lava_Jehla", "SM_Lava_Jehla_Skupina" };
        private static readonly string[] SmLavaRockNames = { "SM_Lava_Balvan" };
        private static readonly string[] SmLavaStoneNames = { "SM_Lava_Kamen", "SM_Lava_Sut", "SM_Lava_Kamen", "SM_Lava_Sut", "SM_Kamen_Rezavy" };   // rez jen jako akcent (1/5)
        private static readonly string[] SmCharredNames = { "SM_Kmen_Ohoreny" };
        private static readonly string[] SmCharredLowNames = { "SM_Kmen_Ohoreny_Nizky" };
        private static readonly string[] SmCharredWoodNames = { "SM_Parez_Ohoreny", "SM_Kmen_Ohoreny_Padly" };
        private static readonly string[] SmLimeTowerNames = { "SM_Vapenec_Vez", "SM_Vapenec_Jehla", "SM_Vapenec_Portal" };
        private static readonly string[] SmLimeRockNames = { "SM_Vapenec_Balvan" };
        private static readonly string[] SmLimeStoneNames = { "SM_Vapenec_Kamen" };
        private static readonly string[] SmPetLogNames = { "SM_Kmen_Zkamenely", "SM_Kmen_Zkamenely_Kratky" };
        private static readonly string[] SmPetStumpNames = { "SM_Parez_Zkamenely" };
        private static readonly string[] SmAgateNames = { "SM_Achat_Rez", "SM_Mineraly" };
        private static readonly string[] SmRuinNames = { "SM_Ruina_Zed", "SM_Ruina_Zed_Nizka", "SM_Ruina_Sloup", "SM_Ruina_Sloup_Zlomeny", "SM_Ruina_Oblouk" };
        private static readonly string[] SmRuinStoneNames = { "SM_Ruina_Kvadr" };
        private static readonly string[] SmPalmNames = { "SM_Palma" };
        private static readonly string[] SmPalmYoungNames = { "SM_Palma_Mlada" };
        private static readonly string[] SmPalmBushNames = { "SM_Palma_Ker" };
        private static readonly string[] SmPalmLowNames = { "SM_Palma_Nizka" };
        // kolo 20 – čtvrtý balík (Biomy4)
        private static readonly string[] SmSnowRockNames = { "SM_Snih_Balvan" };
        private static readonly string[] SmSnowRidgeNames = { "SM_Snih_Hreben" };
        private static readonly string[] SmSnowScreeNames = { "SM_Sut_Snih", "SM_Sut_Snih", "SM_Led_Kus" };
        private static readonly string[] SmIceWallNames = { "SM_Led_Stena", "SM_Led_Serak" };
        private static readonly string[] SmErraticNames = { "SM_Bludny_Balvan" };
        private static readonly string[] SmIceStoneNames = { "SM_Led_Kus", "SM_Sut_Snih" };
        private static readonly string[] SmFloeNames = { "SM_Kra_Velka", "SM_Kra_Stredni", "SM_Kra_Zavej" };
        private static readonly string[] SmFloeSmallNames = { "SM_Kra_Mala" };
        private static readonly string[] SmDriftNames = { "SM_Zavej" };
        private static readonly string[] SmMangroveNames = { "SM_Mangrovnik" };
        private static readonly string[] SmMangroveYoungNames = { "SM_Mangrovnik_Mlady" };
        private static readonly string[] SmMangroveRootNames = { "SM_Mangrove_Koreny" };
        private static readonly string[] SmSaltCrustNames = { "SM_Sul_Krusta" };
        private static readonly string[] SmSaltMoundNames = { "SM_Sul_Kopa", "SM_Sul_Krystaly" };
        private static readonly string[] SmTerraceNames = { "SM_Travertin_Terasa", "SM_Travertin_Terasa_Mala" };
        private static readonly string[] SmVentNames = { "SM_Vyduch_Sirny", "SM_Gejzir_Kuzel" };
        private static readonly string[] SmGeoCrustNames = { "SM_Krusta_Sirna" };
        // kolo 21 – pátý balík (Biomy5); „SM_Mineraly" je existující asset kola 19 (reuse)
        private static readonly string[] SmCrystalNames = { "SM_Krystal_Shluk", "SM_Krystal_Velky" };
        private static readonly string[] SmCrystalSmallNames = { "SM_Krystal_Maly", "SM_Krystal_Maly", "SM_Mineraly" };
        private static readonly string[] SmMineralWallNames = { "SM_Mineral_Stena" };
        private static readonly string[] SmCrystalCaveNames = { "SM_Krystal_Jeskyne" };
        private static readonly string[] SmGiantShroomNames = { "SM_Houba_Obri", "SM_Houba_Obri_Fialova" };
        private static readonly string[] SmShroomClusterNames = { "SM_Houba_Shluk" };
        private static readonly string[] SmGlowShroomNames = { "SM_Houba_Svitici" };
        private static readonly string[] SmChalkCliffNames = { "SM_Utes_Kridovy", "SM_Utes_Kridovy", "SM_Utes_Pilir" };
        private static readonly string[] SmSeaStackNames = { "SM_Utes_Pilir" };
        private static readonly string[] SmBasaltNames = { "SM_Utes_Cedic" };
        private static readonly string[] SmSeaArchNames = { "SM_Utes_Brana" };
        private static readonly string[] SmChalkBoulderNames = { "SM_Utes_Balvan" };
        // kolo 28 – šestý balík (Biomy6); „SM_Utes_Cedic" (kolo 21) a lávové kameny kola 19 jsou reuse
        private static readonly string[] SmBasaltColNames = { "SM_Cedic_Kolonada", "SM_Cedic_Kolonada", "SM_Utes_Cedic" };
        private static readonly string[] SmBasaltStepNames = { "SM_Cedic_Schody" };
        private static readonly string[] SmBasaltPaveNames = { "SM_Cedic_Dlazba" };
        private static readonly string[] SmBasaltSeaNames = { "SM_Cedic_Sloupy_More" };
        private static readonly string[] SmNestNames = { "SM_Hnizdo_Ptaci" };
        private static readonly string[] SmObsSpireNames = { "SM_Obsidian_Strep", "SM_Obsidian_Hreben" };
        private static readonly string[] SmObsRockNames = { "SM_Obsidian_Balvan" };
        private static readonly string[] SmObsStoneNames = { "SM_Obsidian_Ulomky", "SM_Obsidian_Ulomky", "SM_Lava_Kamen" };
        private static readonly string[] SmAlaArchNames = { "SM_Alabastr_Oblouk" };
        private static readonly string[] SmAlaTowerNames = { "SM_Alabastr_Vez", "SM_Alabastr_Plotna" };
        private static readonly string[] SmAlaRockNames = { "SM_Alabastr_Balvan" };
        private static readonly string[] SmAlaStoneNames = { "SM_Alabastr_Kamen" };
        // kolo 29 – sedmý balík (Biomy7); ohořelé kmeny (kolo 19), sírová krusta (kolo 20) a lávové kameny jsou reuse
        private static readonly string[] SmMeteorCoreNames = { "SM_Meteorit_Jadro" };
        private static readonly string[] SmCraterRockNames = { "SM_Kraterovy_Balvan" };
        private static readonly string[] SmTektGlassNames = { "SM_Tektit_Sklo" };
        private static readonly string[] SmOreNames = { "SM_Ruda_Shluk" };
        private static readonly string[] SmTektStoneNames = { "SM_Tektit_Strepy", "SM_Tektit_Strepy", "SM_Lava_Kamen" };
        private static readonly string[] SmCoralBigNames = { "SM_Koral_Vetevnaty", "SM_Koral_Vetevnaty", "SM_Koral_Stolovy" };
        private static readonly string[] SmCoralGateNames = { "SM_Koral_Brana", "SM_Koral_Kostra" };
        private static readonly string[] SmCoralSmallNames = { "SM_Koral_Mozkovy", "SM_Koral_Vetevnaty", "SM_Koral_Stolovy", "SM_Koral_Vetevnaty" };   // pilot 2: útes byl řídký – větevnaté i na malé mřížce
        private static readonly string[] SmCoralStoneNames = { "SM_Koral_Ulomky" };
        private static readonly string[] SmMudConeNames = { "SM_Bahenni_Kuzel" };
        private static readonly string[] SmMudConeBigNames = { "SM_Bahenni_Kuzel_Velky" };
        private static readonly string[] SmSulfurNames = { "SM_Sirne_Krystaly" };
        private static readonly string[] SmMudCrustNames = { "SM_Bahenni_Krusta", "SM_Bahenni_Krusta", "SM_Krusta_Sirna" };

        private sealed class Table
        {
            public int[] oak, birch, fir, firYoung, firDead, rockLarge, rockCliff, stone, bush, bushLow,
                         flowerBush, deadBush, log, stump, flower, mushroom, grassHigh, grassMed, grassLow,
                         grass2, clover, fern, reed, pebble;
            // kolo 14
            public int[] deadTree, deadBroad, deadBirch, cactus, dryShrub, swampReed, fernAll, smBirch, smBroad, smAcacia,
                         smBoulder, smMesaRock, smMesaStone, smDesertRock, smDesertStone, smStone, smTundraShrub,
                         smBush, smGrassDry, smGrass, smWood;
            // kolo 16
            public int[] smDarkFir, smDarkFirLow, smSequoia, smSequoiaYoung, smSakura, smBamboo, smBambooLow, smJungle,
                         smTreeFern, smBigLeaf, smHorsetail, smHeather, smMonolith, smLightStone, smLightBoulder;
            // kolo 19
            public int[] smLavaSpire, smLavaRock, smLavaStone, smCharred, smCharredLow, smCharredWood, smLimeTower, smLimeRock,
                         smLimeStone, smPetLog, smPetStump, smAgate, smRuin, smRuinStone, smPalm, smPalmYoung, smPalmBush, smPalmLow;
            // kolo 20
            public int[] smSnowRock, smSnowRidge, smSnowScree, smIceWall, smErratic, smIceStone, smFloe, smFloeSmall, smDrift,
                         smMangrove, smMangroveYoung, smMangroveRoot, smSaltCrust, smSaltMound, smTerrace, smVent, smGeoCrust;
            // kolo 21
            public int[] smCrystal, smCrystalSmall, smMineralWall, smCrystalCave, smGiantShroom, smShroomCluster, smGlowShroom,
                         smChalkCliff, smSeaStack, smBasalt, smSeaArch, smChalkBoulder;
            // kolo 28
            public int[] smBasaltCol, smBasaltStep, smBasaltPave, smBasaltSea, smNest, smObsSpire, smObsRock, smObsStone,
                         smAlaArch, smAlaTower, smAlaRock, smAlaStone;
            // kolo 29
            public int[] smMeteorCore, smCraterRock, smTektGlass, smOre, smTektStone, smCoralBig, smCoralGate, smCoralSmall, smCoralStone,
                         smMudCone, smMudConeBig, smSulfur, smMudCrust;
        }

        private static Table table;

        private static int[] Resolve(ObjectSpawner sp, string[] names)
        {
            var list = new List<int>(names.Length);
            for (int i = 0; i < names.Length; i++)
            {
                int idx = sp.FindSpawnable(names[i]);
                if (idx >= 0) list.Add(idx);
                else Debug.LogWarning($"[EcologyPlacer] Položka '{names[i]}' ve spawnables chybí – druh se vynechá.");
            }
            return list.ToArray();
        }

        private static Table GetTable(ObjectSpawner sp)
        {
            if (table != null) return table;
            sp.EnsureBiomeProps();
            table = new Table
            {
                oak = Resolve(sp, OakNames), birch = Resolve(sp, BirchNames), fir = Resolve(sp, FirNames),
                firYoung = Resolve(sp, FirYoungNames), firDead = Resolve(sp, FirDeadNames),
                rockLarge = Resolve(sp, RockLargeNames), rockCliff = Resolve(sp, RockCliffNames), stone = Resolve(sp, StoneNames),
                bush = Resolve(sp, BushNames), bushLow = Resolve(sp, BushLowNames), flowerBush = Resolve(sp, FlowerBushNames),
                deadBush = Resolve(sp, DeadBushNames), log = Resolve(sp, LogNames), stump = Resolve(sp, StumpNames),
                flower = Resolve(sp, FlowerNames), mushroom = Resolve(sp, MushroomNames),
                grassHigh = Resolve(sp, GrassHighNames), grassMed = Resolve(sp, GrassMedNames), grassLow = Resolve(sp, GrassLowNames),
                grass2 = Resolve(sp, Grass2Names), clover = Resolve(sp, CloverNames), fern = Resolve(sp, FernNames),
                reed = Resolve(sp, ReedNames), pebble = Resolve(sp, PebbleNames),
                deadTree = Resolve(sp, DeadTreeNames), deadBroad = Resolve(sp, DeadBroadNames), deadBirch = Resolve(sp, DeadBirchNames),
                cactus = Resolve(sp, CactusNames), dryShrub = Resolve(sp, DryShrubNames), swampReed = Resolve(sp, SwampReedNames),
                fernAll = Resolve(sp, FernAllNames), smBirch = Resolve(sp, SmBirchNames), smBroad = Resolve(sp, SmBroadNames),
                smAcacia = Resolve(sp, SmAcaciaNames), smBoulder = Resolve(sp, SmBoulderNames), smMesaRock = Resolve(sp, SmMesaRockNames),
                smMesaStone = Resolve(sp, SmMesaStoneNames), smDesertRock = Resolve(sp, SmDesertRockNames),
                smDesertStone = Resolve(sp, SmDesertStoneNames), smStone = Resolve(sp, SmStoneNames),
                smTundraShrub = Resolve(sp, SmTundraShrubNames), smBush = Resolve(sp, SmBushNames),
                smGrassDry = Resolve(sp, SmGrassDryNames), smGrass = Resolve(sp, SmGrassNames), smWood = Resolve(sp, SmWoodNames),
                smDarkFir = Resolve(sp, SmDarkFirNames), smDarkFirLow = Resolve(sp, SmDarkFirLowNames),
                smSequoia = Resolve(sp, SmSequoiaNames), smSequoiaYoung = Resolve(sp, SmSequoiaYoungNames),
                smSakura = Resolve(sp, SmSakuraNames), smBamboo = Resolve(sp, SmBambooNames), smBambooLow = Resolve(sp, SmBambooLowNames),
                smJungle = Resolve(sp, SmJungleNames), smTreeFern = Resolve(sp, SmTreeFernNames), smBigLeaf = Resolve(sp, SmBigLeafNames),
                smHorsetail = Resolve(sp, SmHorsetailNames), smHeather = Resolve(sp, SmHeatherNames), smMonolith = Resolve(sp, SmMonolithNames),
                smLightStone = Resolve(sp, SmLightStoneNames), smLightBoulder = Resolve(sp, SmLightBoulderNames),
                smLavaSpire = Resolve(sp, SmLavaSpireNames), smLavaRock = Resolve(sp, SmLavaRockNames), smLavaStone = Resolve(sp, SmLavaStoneNames),
                smCharred = Resolve(sp, SmCharredNames), smCharredLow = Resolve(sp, SmCharredLowNames), smCharredWood = Resolve(sp, SmCharredWoodNames),
                smLimeTower = Resolve(sp, SmLimeTowerNames), smLimeRock = Resolve(sp, SmLimeRockNames), smLimeStone = Resolve(sp, SmLimeStoneNames),
                smPetLog = Resolve(sp, SmPetLogNames), smPetStump = Resolve(sp, SmPetStumpNames), smAgate = Resolve(sp, SmAgateNames), smRuin = Resolve(sp, SmRuinNames),
                smRuinStone = Resolve(sp, SmRuinStoneNames), smPalm = Resolve(sp, SmPalmNames), smPalmYoung = Resolve(sp, SmPalmYoungNames),
                smPalmBush = Resolve(sp, SmPalmBushNames), smPalmLow = Resolve(sp, SmPalmLowNames),
                smSnowRock = Resolve(sp, SmSnowRockNames), smSnowRidge = Resolve(sp, SmSnowRidgeNames), smSnowScree = Resolve(sp, SmSnowScreeNames),
                smIceWall = Resolve(sp, SmIceWallNames), smErratic = Resolve(sp, SmErraticNames), smIceStone = Resolve(sp, SmIceStoneNames),
                smFloe = Resolve(sp, SmFloeNames), smFloeSmall = Resolve(sp, SmFloeSmallNames), smDrift = Resolve(sp, SmDriftNames),
                smMangrove = Resolve(sp, SmMangroveNames), smMangroveYoung = Resolve(sp, SmMangroveYoungNames), smMangroveRoot = Resolve(sp, SmMangroveRootNames),
                smSaltCrust = Resolve(sp, SmSaltCrustNames), smSaltMound = Resolve(sp, SmSaltMoundNames), smTerrace = Resolve(sp, SmTerraceNames),
                smVent = Resolve(sp, SmVentNames), smGeoCrust = Resolve(sp, SmGeoCrustNames),
                smCrystal = Resolve(sp, SmCrystalNames), smCrystalSmall = Resolve(sp, SmCrystalSmallNames), smMineralWall = Resolve(sp, SmMineralWallNames),
                smCrystalCave = Resolve(sp, SmCrystalCaveNames), smGiantShroom = Resolve(sp, SmGiantShroomNames), smShroomCluster = Resolve(sp, SmShroomClusterNames),
                smGlowShroom = Resolve(sp, SmGlowShroomNames), smChalkCliff = Resolve(sp, SmChalkCliffNames), smSeaStack = Resolve(sp, SmSeaStackNames),
                smBasalt = Resolve(sp, SmBasaltNames), smSeaArch = Resolve(sp, SmSeaArchNames), smChalkBoulder = Resolve(sp, SmChalkBoulderNames),
                smBasaltCol = Resolve(sp, SmBasaltColNames), smBasaltStep = Resolve(sp, SmBasaltStepNames), smBasaltPave = Resolve(sp, SmBasaltPaveNames),
                smBasaltSea = Resolve(sp, SmBasaltSeaNames), smNest = Resolve(sp, SmNestNames), smObsSpire = Resolve(sp, SmObsSpireNames),
                smObsRock = Resolve(sp, SmObsRockNames), smObsStone = Resolve(sp, SmObsStoneNames), smAlaArch = Resolve(sp, SmAlaArchNames),
                smAlaTower = Resolve(sp, SmAlaTowerNames), smAlaRock = Resolve(sp, SmAlaRockNames), smAlaStone = Resolve(sp, SmAlaStoneNames),
                smMeteorCore = Resolve(sp, SmMeteorCoreNames), smCraterRock = Resolve(sp, SmCraterRockNames), smTektGlass = Resolve(sp, SmTektGlassNames),
                smOre = Resolve(sp, SmOreNames), smTektStone = Resolve(sp, SmTektStoneNames), smCoralBig = Resolve(sp, SmCoralBigNames),
                smCoralGate = Resolve(sp, SmCoralGateNames), smCoralSmall = Resolve(sp, SmCoralSmallNames), smCoralStone = Resolve(sp, SmCoralStoneNames),
                smMudCone = Resolve(sp, SmMudConeNames), smMudConeBig = Resolve(sp, SmMudConeBigNames), smSulfur = Resolve(sp, SmSulfurNames),
                smMudCrust = Resolve(sp, SmMudCrustNames),
            };
            return table;
        }

        // ── vrstvy kandidátů ─────────────────────────────────────────
        private const int LTree = 0, LRock = 1, LBush = 2, LStone = 3, LSmall = 4, LGrass = 5, LPebble = 6;
        /// <summary>Vrstva sebratelných kamínků – spawner je drží jako blízké detaily (dosah trávy).</summary>
        public const int LayerPebble = LPebble;
        private static readonly float[] CellSize = { 16f, 14f, 7f, 4.5f, 3f, 1.3f, 5f };
        private static readonly float[] Jitter = { 0.32f, 0.35f, 0.38f, 0.42f, 0.45f, 0.45f, 0.45f };
        /// <summary>Sebratelné kamínky zapnuté (A/B: <c>/props kaminky off</c>).</summary>
        public static bool Pebbles = true;

        // ── pravidla (metry, stupně) ─────────────────────────────────
        public const float TreeMaxSlope = 22f;
        public const float GroveLo = 0.44f, GroveHi = 0.58f;
        public const float TreeMinAboveWater = 2.5f;
        public const float TreeMinWaterDistance = 10f;      // k mokrému vzorku v poli sloupce
        public const float TreeMinRiverClearance = 26f;     // od břehu koryta (hydrologie) – koruna má r ~20 m
        public const float TreeLakeProbe = 22f;
        public const float TreeMinAboveSea = 14f;
        public const float TreeRiverViewRise = 10f;
        public const float BushMaxSlope = 22f;
        public const float StoneMaxSlope = 38f;
        public const float RockMaxSlope = 35f;
        public const float CliffRockMaxSlope = 50f;
        public const float GrassMaxSlope = 38f;

        // ── kolo 6: pravidla sladěná s auditem (/props audit) ──────────
        /// <summary>
        /// Pevný objekt (strom, balvan, keř, kámen) musí mít stopu 0,6·r celou ve SVÉM sloupci.
        /// Dva objekty z různých sloupců se pak nemohou překrýt (audit „nakupení" měří právě
        /// 0,6·(r1 + r2)) a nic nezávisí na tom, co soused rozhodne podle svého meshe.
        /// </summary>
        public const float FootprintFactor = 0.6f;
        /// <summary>Keř smí být do svahu zapuštěný nejvýš tolik (audit: 0,9 m).</summary>
        public const float BushMaxBury = 0.75f;
        /// <summary>Strom smí být do svahu zapuštěný nejvýš tolik (audit: 1,0 m).</summary>
        public const float TreeMaxBury = 0.85f;
        /// <summary>Pravidla kola 6 zapnutá (jen pro A/B srovnání: <c>/props pravidla off</c>).</summary>
        public static bool Rules6 = true;
        /// <summary>Kolo 8: kompozice – mýtiny v hájích a výhledy k vodě (<c>/props kompozice off</c> pro A/B).</summary>
        public static bool Compose8 = true;

        // ── scratch (osazuje se synchronně na hlavním vlákně) ───────────
        private static float[] gSurf, gDist, gShore;
        private static byte[] gWet;
        private static int gSide;
        private static readonly List<EcoPlacement> scratchOut = new List<EcoPlacement>(1024);
        private static readonly List<Vector4> reserved = new List<Vector4>(256);   // x, z, poloměr, vrstva

        // pole sloupce pro rychlé čtení
        private static ColumnField col;
        private static float2 fieldOrigin;
        private static float vox, sea, colX0, colZ0, colSpan;
        private static TerrainPalette pal;
        private static MicroParams micro;
        private static int seed;
        private static float2 offGrove, offGrove2, offOutcrop, offFlower, offGap, offClump;
        private static float2 offAutumn;
        private static float3 offRegion;
        private static CraterCell craterCol;   // kolo 29

        /// <summary>Kolo 14: počet osazených objektů podle regionu (BiomeRegion) – pro /biomy stat.</summary>
        public static readonly int[] RegionPlaced = new int[BiomeMath.Count];
        /// <summary>Diagnostika kola 14: [region * 8 + vrstva] (strom, skála, keř, kámen, drobné, tráva, kamínek).</summary>
        public static readonly int[] RegionLayerPlaced = new int[BiomeMath.Count * 8];

        /// <summary>Z prázdného nebo menšího pole vybere náhodnou položku; jinak záložní pole.</summary>
        private static int PickOr(ref Rng rng, int[] a, int[] fallback) => a != null && a.Length > 0 ? rng.Pick(a) : rng.Pick(fallback);

        /// <summary>Kumulovaný čas stavby (ms) a počet sloupců – pro /props stat.</summary>
        public static double TotalMs;
        /// <summary>Kolo 15: měření nákladu vah regionů v osazování (/biome stat).</summary>
        public static long RegionTicks, RegionCalls;
        public static double RegionMs => RegionTicks * 1000.0 / System.Diagnostics.Stopwatch.Frequency;
        public static int Columns;
        public static double WorstMs;

        /// <summary>Čas po vrstvách (stromy, kameny, keře, kamínky, drobné, tráva, příprava) – součet.</summary>
        public static readonly double[] LayerMs = new double[7];

        private static readonly System.Diagnostics.Stopwatch sw = new System.Diagnostics.Stopwatch();

        /// <summary>
        /// Spočte pozice pro jeden sloupec. <paramref name="raster"/> musí být postavený z LOD0
        /// meshů téhož sloupce; <paramref name="waterLevels"/> jsou hladiny sloupce (33×33) nebo null.
        /// </summary>
        public static List<EcoPlacement> Build(ObjectSpawner sp, in ColumnField column, int pad, float voxelSize,
                                               float seaLevel, int2 coord, in TerrainPalette palette,
                                               in MicroParams microParams, int worldSeed, float[] waterLevels)
        {
            sw.Restart();
            Table t = GetTable(sp);

            col = column;
            vox = voxelSize;
            sea = seaLevel;
            pal = palette;
            micro = microParams;
            seed = worldSeed;
            fieldOrigin = column.origin;
            colSpan = VoxelWorld.ChunkDim * voxelSize;
            colX0 = column.origin.x + pad * voxelSize;
            colZ0 = column.origin.y + pad * voxelSize;

            offGrove = Offset(11); offGrove2 = Offset(12); offOutcrop = Offset(13);
            offFlower = Offset(14); offGap = Offset(15); offClump = Offset(16);
            offAutumn = Offset(17); offRegion = GenNoise.ChannelOffset3(seed, 28);
            craterCol = VoxelTerrain.instance != null   // kolo 29: kráter sloupce (stejný jako u terénu a barvy)
                ? VoxelTerrain.instance.CraterFor(new float2(colX0 - 70f, colZ0 - 70f), new float2(colX0 + colSpan + 70f, colZ0 + colSpan + 70f)) : default;

            PrepareGrids(pad, waterLevels);
            reserved.Clear();
            LayerMs[6] += sw.Elapsed.TotalMilliseconds;

            // Sdílený scratch: SpawnPlacements si z něj hned zkopíruje, co potřebuje.
            var output = scratchOut;
            output.Clear();

            // Kolo 28: velké formace nových biomů na vlastních světových mřížkách – PŘED stromy a balvany, aby se jim ostatní
            // vrstvy vyhnuly (rezervace i z okraje sousedních sloupců). Mimo masky K28 nic nerezervuje → ostatní biomy beze změny.
            if (Formations28 && !proxyMode && t.smBasaltCol.Length > 0)
            {
                double t0 = sw.Elapsed.TotalMilliseconds;
                // Kolo 29: kužely bahenních sopek přesně na výduchech (BiomeMath.MudVent – na týchž bodech jsou sírové prstence
                // v barvě terénu) a jádro meteoritu ve středu kráteru – před formacemi, ty se jim vyhnou přes rezervace.
                if (t.smMudCone.Length > 0) { RunMudVents(sp, t, output); RunCraterCores(sp, t, output); }
                RunFormations(sp, t, output, true);
                RunFormations(sp, t, output, false);
                LayerMs[LRock] += sw.Elapsed.TotalMilliseconds - t0;
            }

            for (int layer = 0; layer <= LGrass; layer++)
            {
                double t0 = sw.Elapsed.TotalMilliseconds;
                RunLayer(sp, t, layer, layer <= LRock ? 5f : 0f, output);
                LayerMs[layer] += sw.Elapsed.TotalMilliseconds - t0;
            }

            // Sebratelné kamínky až nakonec: vlastní seed buněk, nic před nimi neposouvají.
            if (Pebbles && t.pebble.Length > 0)
            {
                double t0 = sw.Elapsed.TotalMilliseconds;
                RunLayer(sp, t, LPebble, 0f, output);
                LayerMs[LStone] += sw.Elapsed.TotalMilliseconds - t0;
            }

            // Kolo 20: kry na zamrzlém moři – vlastní mřížka a seed až po všech vrstvách (nic před nimi neposouvají).
            if (Floes && t.smFloe.Length > 0)
            {
                double t0 = sw.Elapsed.TotalMilliseconds;
                RunFloes(sp, t, output);
                LayerMs[LRock] += sw.Elapsed.TotalMilliseconds - t0;
            }

            // Kolo 28: čedičové sloupy v mělčině čedičového pobřeží – vlastní mřížka a seed až po všech vrstvách.
            if (SeaColumns && t.smBasaltSea.Length > 0)
            {
                double t0 = sw.Elapsed.TotalMilliseconds;
                RunSeaColumns(sp, t, output);
                LayerMs[LRock] += sw.Elapsed.TotalMilliseconds - t0;
            }

            sw.Stop();
            double ms = sw.Elapsed.TotalMilliseconds;
            TotalMs += ms;
            Columns++;
            if (ms > WorstMs) WorstMs = ms;
            return output;
        }

        // ── kolo 6: vzdálené stromy ─────────────────────────────────
        private static bool proxyMode;
        private static float proxyGrid;
        private static readonly List<EcoPlacement> proxyOut = new List<EcoPlacement>(256);
        private static readonly int[] rejectBackup = new int[16];

        /// <summary>
        /// Jen vrstva stromů pro hrubší sloupec (LOD1/LOD2) – pro vzdálené stromy
        /// (<see cref="Orivilon.World.Spawning.TreeProxies"/>). Tytéž kandidáty a tentýž los jako
        /// skutečné stromy (hash buňky), jen z pole hrubšího sloupce a bez kontrol proti LOD0
        /// meshi, který tu není. Pravidlo stopy u hrany se počítá k hranám LOD0 sloupců
        /// (<paramref name="lod0Span"/>), protože tam ho počítají i skutečné stromy.
        /// Statistiku osazovače to neovlivní.
        /// </summary>
        public static List<EcoPlacement> BuildTreeProxies(ObjectSpawner sp, in ColumnField column, int pad, float voxelSize,
                                                          float seaLevel, in TerrainPalette palette, in MicroParams microParams,
                                                          int worldSeed, float lod0Span)
        {
            Table t = GetTable(sp);
            col = column;
            vox = voxelSize;
            sea = seaLevel;
            pal = palette;
            micro = microParams;
            seed = worldSeed;
            fieldOrigin = column.origin;
            colSpan = VoxelWorld.ChunkDim * voxelSize;
            colX0 = column.origin.x + pad * voxelSize;
            colZ0 = column.origin.y + pad * voxelSize;
            offGrove = Offset(11); offGrove2 = Offset(12); offOutcrop = Offset(13);
            offFlower = Offset(14); offGap = Offset(15); offClump = Offset(16);
            offAutumn = Offset(17); offRegion = GenNoise.ChannelOffset3(seed, 28);
            craterCol = VoxelTerrain.instance != null   // kolo 29: kráter sloupce (stejný jako u terénu a barvy)
                ? VoxelTerrain.instance.CraterFor(new float2(colX0 - 70f, colZ0 - 70f), new float2(colX0 + colSpan + 70f, colZ0 + colSpan + 70f)) : default;

            PrepareGrids(pad, null);
            reserved.Clear();
            proxyOut.Clear();
            System.Array.Copy(TreeReject, rejectBackup, TreeReject.Length);
            int borderBackup = BorderRejects;
            proxyMode = true;
            proxyGrid = lod0Span;
            try { RunLayer(sp, t, LTree, 0f, proxyOut); }
            finally
            {
                proxyMode = false;
                System.Array.Copy(rejectBackup, TreeReject, TreeReject.Length);
                BorderRejects = borderBackup;
            }
            return proxyOut;
        }

        /// <summary>Vzdálenost k nejbližší hraně LOD0 sloupce (mřížka po <paramref name="span"/> m).</summary>
        private static float GridBorderDistance(float x, float z, float span)
        {
            float fx = x - Mathf.Floor(x / span) * span, fz = z - Mathf.Floor(z / span) * span;
            return Mathf.Min(Mathf.Min(fx, span - fx), Mathf.Min(fz, span - fz));
        }

        private static float2 Offset(int k)
        {
            uint h = Hash(k, k * 7 + 3, 0x51ED, seed);
            return new float2((h & 0xFFFF) * 0.061f + 100f, (h >> 16) * 0.061f + 100f);
        }

        // ── mřížky pole ─────────────────────────────────────────────

        private static void PrepareGrids(int pad, float[] waterLevels)
        {
            int side = col.side;
            int n = side * side;
            if (gSurf == null || gSurf.Length < n)
            {
                gSurf = new float[n]; gDist = new float[n]; gShore = new float[n]; gWet = new byte[n];
            }
            gSide = side;

            int dim = VoxelWorld.SampleDim;
            // Kolo 24: výška před šitím LOD švu (u LOD0 vždy k dispozici) – osazení tak nezávisí
            // na tom, jestli sloupec zrovna sousedil s hrubším LOD (determinismus, viz SetSeamDelta).
            var surfSrc = col.surfRaw.IsCreated && col.surfRaw.Length == n ? col.surfRaw : col.surfY;
            for (int i = 0; i < n; i++)
            {
                float s = surfSrc[i];
                gSurf[i] = s;
                gShore[i] = col.shoreY[i];

                bool wet = s <= sea + 0.05f;
                float lake = col.lakeY[i];
                if (lake > ColumnField.NoLake && s < lake + 0.05f) wet = true;
                if (col.riverCore[i] > 0.02f && s < col.riverY[i] + 0.05f) wet = true;

                int x = i % side - pad, z = i / side - pad;
                if (waterLevels != null && x >= 0 && z >= 0 && x < dim && z < dim)
                {
                    float lv = waterLevels[z * dim + x];
                    if (lv > WaterSurface.NoWater * 0.5f && lv > s - 0.3f) wet = true;
                }
                gWet[i] = wet ? (byte)1 : (byte)0;
                gDist[i] = wet ? 0f : 1e4f;
            }

            // Chamfer 3-4: vzdálenost k nejbližšímu mokrému vzorku v metrech (jen v rámci
            // sloupce s okrajem – dál pole nesahá; řeky mimo něj pokrývá shoreY).
            float a = vox, b = vox * 1.41421356f;
            for (int z = 0; z < side; z++)
            for (int x = 0; x < side; x++)
            {
                int i = z * side + x;
                float d = gDist[i];
                if (x > 0) d = Mathf.Min(d, gDist[i - 1] + a);
                if (z > 0)
                {
                    d = Mathf.Min(d, gDist[i - side] + a);
                    if (x > 0) d = Mathf.Min(d, gDist[i - side - 1] + b);
                    if (x < side - 1) d = Mathf.Min(d, gDist[i - side + 1] + b);
                }
                gDist[i] = d;
            }
            for (int z = side - 1; z >= 0; z--)
            for (int x = side - 1; x >= 0; x--)
            {
                int i = z * side + x;
                float d = gDist[i];
                if (x < side - 1) d = Mathf.Min(d, gDist[i + 1] + a);
                if (z < side - 1)
                {
                    d = Mathf.Min(d, gDist[i + side] + a);
                    if (x < side - 1) d = Mathf.Min(d, gDist[i + side + 1] + b);
                    if (x > 0) d = Mathf.Min(d, gDist[i + side - 1] + b);
                }
                gDist[i] = d;
            }
        }

        private static bool InField(float x, float z)
        {
            float gx = (x - fieldOrigin.x) / vox, gz = (z - fieldOrigin.y) / vox;
            return gx >= 0f && gz >= 0f && gx <= gSide - 1.001f && gz <= gSide - 1.001f;
        }

        private static float Bilinear(float[] g, float x, float z)
        {
            float gx = Mathf.Clamp((x - fieldOrigin.x) / vox, 0f, gSide - 1.001f);
            float gz = Mathf.Clamp((z - fieldOrigin.y) / vox, 0f, gSide - 1.001f);
            int x0 = (int)gx, z0 = (int)gz;
            float fx = gx - x0, fz = gz - z0;
            int i = z0 * gSide + x0;
            float a = g[i], b = g[i + 1], c = g[i + gSide], d = g[i + gSide + 1];
            return Mathf.Lerp(Mathf.Lerp(a, b, fx), Mathf.Lerp(c, d, fx), fz);
        }

        private static int Nearest(float x, float z)
        {
            int gx = Mathf.Clamp(Mathf.RoundToInt((x - fieldOrigin.x) / vox), 0, gSide - 1);
            int gz = Mathf.Clamp(Mathf.RoundToInt((z - fieldOrigin.y) / vox), 0, gSide - 1);
            return gz * gSide + gx;
        }

        private static float FieldSurf(float x, float z) => Bilinear(gSurf, x, z);

        private static bool InColumn(float x, float z)
            => x >= colX0 && z >= colZ0 && x < colX0 + colSpan && z < colZ0 + colSpan;

        /// <summary>Vzdálenost bodu k nejbližší hraně vlastního sloupce (uvnitř sloupce kladná).</summary>
        private static float BorderDistance(float x, float z)
            => Mathf.Min(Mathf.Min(x - colX0, colX0 + colSpan - x), Mathf.Min(z - colZ0, colZ0 + colSpan - z));

        /// <summary>Kolo 6: kolik kandidátů pravidlo stopy u hrany sloupce odmítlo (pro /props stat).</summary>
        public static int BorderRejects;

        /// <summary>Výška země: přesně z meshe, kde ho sloupec má, jinak z pole.</summary>
        private static float Ground(float x, float z, bool useMesh)
        {
            if (useMesh && InColumn(x, z) && SurfaceRaster.Top(x, z, out float y, out _)) return y;
            return FieldSurf(x, z);
        }

        private static float SlopeDeg(float x, float z)
        {
            const float h = 1f;
            float dx = FieldSurf(x + h, z) - FieldSurf(x - h, z);
            float dz = FieldSurf(x, z + h) - FieldSurf(x, z - h);
            float g = Mathf.Sqrt(dx * dx + dz * dz) / (2f * h);
            return Mathf.Atan(g) * Mathf.Rad2Deg;
        }

        /// <summary>Sklon skutečného meshe (gradient ±0,75 m) – fazeta umí být strmější než pole.</summary>
        private static float MeshSlope(float x, float z) => MeshSlope(x, z, out _);

        /// <summary>
        /// Kolo 6: u hrany sloupce leží jeden z bodů ±0,75 m u souseda, jehož mesh tu není –
        /// dřív se tam vzala výška z pole, které je hladší než fazeta, a strmá fazeta za hranou
        /// prošla (audit: tráva na 48°). Teď se vezme jednostranná diference z bodů uvnitř
        /// sloupce a volající u hrany použije přísnější mez (<paramref name="nearBorder"/>).
        /// </summary>
        private static float MeshSlope(float x, float z, out bool nearBorder)
        {
            const float h = 0.75f;
            float c = Ground(x, z, true);
            bool e = InColumn(x + h, z), w = InColumn(x - h, z), n = InColumn(x, z + h), so = InColumn(x, z - h);
            nearBorder = !(e && w && n && so);
            if (!Rules6) { e = w = n = so = true; nearBorder = false; }
            float gx = e && w ? (Ground(x + h, z, true) - Ground(x - h, z, true)) / (2f * h)
                     : e ? (Ground(x + h, z, true) - c) / h
                     : w ? (c - Ground(x - h, z, true)) / h : 0f;
            float gz = n && so ? (Ground(x, z + h, true) - Ground(x, z - h, true)) / (2f * h)
                     : n ? (Ground(x, z + h, true) - c) / h
                     : so ? (c - Ground(x, z - h, true)) / h : 0f;
            return Mathf.Atan(Mathf.Sqrt(gx * gx + gz * gz)) * Mathf.Rad2Deg;
        }

        private static Vector3 Normal(float x, float z, float h)
        {
            float dx = FieldSurf(x + h, z) - FieldSurf(x - h, z);
            float dz = FieldSurf(x, z + h) - FieldSurf(x, z - h);
            return new Vector3(-dx, 2f * h, -dz).normalized;
        }

        // ── šum a hash ──────────────────────────────────────────────

        private static float Noise(float x, float z, float scale, float2 off)
        {
            float a = Mathf.PerlinNoise(x / scale + off.x, z / scale + off.y);
            float b = Mathf.PerlinNoise(x / (scale * 0.43f) + off.y, z / (scale * 0.43f) + off.x);
            return a * 0.72f + b * 0.28f;
        }

        public static uint Hash(int x, int z, int salt, int s)
        {
            unchecked
            {
                uint h = (uint)s * 0x9E3779B1u ^ (uint)salt * 0x85EBCA77u;
                h ^= (uint)x * 0xC2B2AE3Du; h = (h << 13) | (h >> 19); h = h * 5u + 0xE6546B64u;
                h ^= (uint)z * 0x27D4EB2Fu; h = (h << 13) | (h >> 19); h = h * 5u + 0xE6546B64u;
                return Mix(h);
            }
        }

        private static uint Mix(uint h)
        {
            unchecked
            {
                h ^= h >> 16; h *= 0x85EBCA6Bu;
                h ^= h >> 13; h *= 0xC2B2AE35u;
                h ^= h >> 16;
                return h;
            }
        }

        private struct Rng
        {
            private uint s;
            public Rng(uint seed) { s = seed; }
            public float Next()
            {
                unchecked { s = Mix(s + 0x9E3779B9u); }
                return (s >> 8) * (1f / 16777216f);
            }
            public float Range(float a, float b) => a + (b - a) * Next();
            public int Pick(int[] arr) => arr.Length == 0 ? -1 : arr[Mathf.Min((int)(Next() * arr.Length), arr.Length - 1)];
        }

        // ── klima a biom ────────────────────────────────────────────

        private struct Site
        {
            public float x, z, surf, slope, hW, dW, temp, hum, snowLine, grove, grove2, outcrop;
            public float wR, wK, wA;
            public bool riverish;
            public EcoBiome biome;
            public int micro;
            public float microW;
            /// <summary>Kolo 14: klimatický region (BiomeRegion) vybraný rozptýleně podle vah.</summary>
            public int region;
            public RegionWeights rw;
        }

        private static void Describe(float x, float z, float dither, int layer, out Site s)
        {
            s = default;
            s.x = x; s.z = z;
            s.surf = FieldSurf(x, z);
            s.slope = SlopeDeg(x, z);
            float shore = Bilinear(gShore, x, z);
            s.hW = s.surf - shore;
            s.dW = Bilinear(gDist, x, z);
            int ni = Nearest(x, z);
            s.temp = Mathf.Clamp01(col.temp[ni]);
            s.hum = Mathf.Clamp01(col.hum[ni]);
            s.snowLine = pal.snowTop + pal.snowTempShift * (s.temp - 0.5f) * 2f;
            s.riverish = shore > sea + 0.3f;

            s.grove = Noise(x, z, 115f, offGrove);
            s.grove2 = layer == LTree ? Noise(x, z, 70f, offGrove2) : 0f;
            s.outcrop = layer == LRock || layer == LStone ? Noise(x, z, 55f, offOutcrop) : 0f;

            s.wR = s.riverish ? 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(3f, 9f, s.hW)) : 0f;
            // Voda v dosahu pole, která není moře, dělá nivu taky (malé tůně a jezírka).
            if (!s.riverish && s.dW < 12f && s.surf - sea > 6f)
                s.wR = Mathf.Max(s.wR, (1f - Mathf.InverseLerp(4f, 12f, s.dW)) * (1f - Mathf.InverseLerp(2f, 6f, s.hW)));
            s.wK = s.riverish ? 0f : 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(4f, 14f, s.surf - sea));
            s.wA = Mathf.Max(Mathf.Max(
                        Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(pal.grassTop - 30f, pal.grassTop + 40f, s.surf)),
                        Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(s.snowLine - 80f, s.snowLine - 25f, s.surf))),
                        Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(30f, 42f, s.slope)));

            // Plynulý přechod: biom se vybírá rozptýleně podle vah, takže na hranici se
            // oba celky prolínají po jednotlivých rostlinách, ne po čáře.
            float r = s.wR;
            float k = s.wK * (1f - r);
            float a = s.wA * (1f - r) * (1f - s.wK);
            if (dither < r) s.biome = EcoBiome.Riparian;
            else if (dither < r + k) s.biome = EcoBiome.Coast;
            else if (dither < r + k + a) s.biome = EcoBiome.Alpine;
            else s.biome = EcoBiome.Meadow;

            if (micro.Enabled)
                MicroBiomeMath.SampleSurface(new float2(x, z), micro, s.surf, sea, out s.micro, out s.microW);

            // Kolo 14: klimatický region. Tytéž váhy počítá barva terénu (TerrainPalette.EvaluateArt).
            // Výběr má vlastní hash (ne rng kandidáta), takže v čisté louce (váha 1) zůstává los
            // i osazení přesně jako v kole 13.
            s.region = 0;
            if (WorldGenSettings.BiomesEnabled)
            {
                bool water = s.riverish || s.dW < 20f;
                long rt0 = System.Diagnostics.Stopwatch.GetTimestamp();
                var rxz = new float2(x, z);
                if (BiomeBurst.Enabled)
                    BiomeBurst.Weights(in rxz, s.surf, s.temp, s.hum, Mathf.Clamp01(col.cliff[ni]),
                                       water ? s.hW : 1e4f, water ? 1 : 0, sea, in offRegion, in craterCol, out s.rw);
                else
                    s.rw = BiomeMath.Weights(rxz, s.surf, s.temp, s.hum, Mathf.Clamp01(col.cliff[ni]),
                                             water ? s.hW : 1e4f, water, sea, offRegion, craterCol);
                RegionTicks += System.Diagnostics.Stopwatch.GetTimestamp() - rt0;
                RegionCalls++;
                float d2 = (Hash(Mathf.FloorToInt(x * 2f), Mathf.FloorToInt(z * 2f), 0x5E61 + layer, seed) & 0xFFFFFF) / 16777216f;
                s.region = s.rw.PickSharp(d2);
            }
            else s.rw.meadow = 1f;
        }

        private static bool R(in Site s, BiomeRegion r) => s.region == (int)r;

        /// <summary>Kolo 21: úsek černého čedičového pobřeží (jinak bílá křída) – stejná funkce jako barva svahů.</summary>
        private static bool CliffDark(in Site s) => BiomeMath.CliffDarkness(new float2(s.x, s.z), offRegion) > 0.5f;

        /// <summary>Kolo 19: holé geologické biomy (láva, zkamenělý les) – bez zeleného podrostu i na svazích.</summary>
        private static bool Barren(in Site s) => s.region == (int)BiomeRegion.Volcanic || s.region == (int)BiomeRegion.Petrified
                                                  || s.region == (int)BiomeRegion.SaltFlat || s.region == (int)BiomeRegion.Geothermal   // kolo 20
                                                  || s.region == (int)BiomeRegion.ObsidianPlain || s.region == (int)BiomeRegion.AlabasterPlateau   // kolo 28
                                                  || s.region == (int)BiomeRegion.MeteorCrater || s.region == (int)BiomeRegion.MudVolcanoes;   // kolo 29

        /// <summary>Kolo 20: sněhové a ledové biomy (štíty, ledovec, zamrzlý oceán) – skoro bez trávy.</summary>
        private static bool Snowy(in Site s) => s.region == (int)BiomeRegion.SnowPeaks || s.region == (int)BiomeRegion.Glacier || s.region == (int)BiomeRegion.FrozenOcean;

        // ── společné tvrdé kontroly ─────────────────────────────────

        /// <summary>Stopa: nejnižší a nejvyšší země ve středu a ve čtyřech bodech na poloměru r.</summary>
        private static void Footprint(float x, float z, float r, bool useMesh, out float min, out float max, out float center)
        {
            center = Ground(x, z, useMesh);
            min = center; max = center;
            for (int k = 0; k < 4; k++)
            {
                float ax = k == 0 ? r : k == 1 ? -r : 0f;
                float az = k == 2 ? r : k == 3 ? -r : 0f;
                float g = Ground(x + ax, z + az, useMesh);
                if (g < min) min = g;
                if (g > max) max = g;
            }
        }

        /// <summary>
        /// Mesh v bodě sedí na výškovém poli a nemá nad ani těsně pod sebou další vrstvu
        /// (převis, brána, strop mělké jeskyně). Jinak by objekt visel nebo prorůstal skálou.
        /// </summary>
        private static bool CleanMesh(float x, float z, float tolerance)
        {
            if (!SurfaceRaster.Top(x, z, out float top, out int layers)) return false;
            if (layers > 1) return false;
            return Mathf.Abs(top - FieldSurf(x, z)) <= tolerance;
        }

        /// <summary>Kolo 12c: přepínač kontroly vstupu jeskyně za hranou sloupce (jen pro A/B).</summary>
        public static bool CaveLipCheck = true;
        private static GenParams caveGp;
        private static bool caveGpOk;
        public static int CaveLipRejects;

        /// <summary>Kolo 12c: parametry LOD0 builderu pro <see cref="CaveLipAcross"/>.</summary>
        public static void SetCaveParams(in GenParams gp) { caveGp = gp; caveGpOk = true; }

        /// <summary>
        /// Kolo 12c: osazovač vidí jen mesh vlastního sloupce. Tráva těsně u hrany sloupce tak
        /// mohla stát na lemu jeskynního vstupu, který leží už v sousedním sloupci (audit: trs
        /// 0,3 m od 19 m hluboké díry). Za hranou se proto zeptá přímo jeskynního pole –
        /// stejná čistá funkce, ze které vzniká hustota, takže je to deterministické a nezávisí
        /// na tom, jestli soused už má mesh. Body uvnitř sloupce kontroluje mesh jako dřív.
        /// </summary>
        private static bool CaveLipAcross(float x, float z, float g)
        {
            if (!CaveLipCheck || !caveGpOk || caveGp.caveStrength <= 0.001f) return false;
            for (int k = 0; k < 4; k++)
            for (int r = 0; r < 2; r++)
            {
                float d = r == 0 ? 0.4f : 0.75f;
                float px = x + (k == 0 ? d : k == 1 ? -d : 0f), pz = z + (k == 2 ? d : k == 3 ? -d : 0f);
                if (InColumn(px, pz)) continue;
                for (int j = 0; j < 2; j++)
                {
                    float depth = j == 0 ? 0.5f : 1.5f;
                    // Dutina se otevře zhruba od carve 0,2 (viz DensityJob: 0,9 − 4,5·carve).
                    float c = Density3D.CaveCarve(new float3(px, g - depth, pz), caveGp, depth) * caveGp.caveStrength;
                    if (c > 0.2f) { CaveLipRejects++; return true; }
                }
            }
            return false;
        }

        private static float MaxOverhang(float x, float z, float r)
        {
            float m = col.overhang[Nearest(x, z)];
            m = Mathf.Max(m, col.overhang[Nearest(x + r, z)]);
            m = Mathf.Max(m, col.overhang[Nearest(x - r, z)]);
            m = Mathf.Max(m, col.overhang[Nearest(x, z + r)]);
            m = Mathf.Max(m, col.overhang[Nearest(x, z - r)]);
            return m;
        }

        private static float RiverCoreAt(float x, float z) => col.riverCore[Nearest(x, z)];

        /// <summary>
        /// Strom daleko od koryta a jezera i mimo pole sloupce: koruna má poloměr ~20 m, takže
        /// by jinak přes břeh zakryla řeku, ústí nebo vodopád. Dotaz do hydrologie je drahý,
        /// proto se volá až po všech levných pravidlech.
        /// </summary>
        private static bool FarFromWater(float x, float z)
        {
            VoxelTerrain vt = VoxelTerrain.instance;
            if (vt == null) return true;
            if (vt.RiverClearance(x, z, out float lake) < TreeMinRiverClearance) return false;
            if (lake > 1.0f) return false;
            for (int k = 0; k < 4; k++)
            {
                float a = k * (Mathf.PI * 0.5f) + 0.785f;
                vt.RiverClearance(x + Mathf.Cos(a) * TreeLakeProbe, z + Mathf.Sin(a) * TreeLakeProbe, out float lk);
                if (lk > 1.0f) return false;
            }
            return true;
        }

        /// <summary>
        /// Hrana srázu nebo úpatí stěny v dosahu. Na hraně útesu strom rozbije siluetu a
        /// visí nad srázem; u úpatí prorůstá korunou do skály.
        /// </summary>
        private static bool NearDrop(float x, float z, float surf, float reach, float drop, float wall)
        {
            for (int k = 0; k < 8; k++)
            {
                float ang = k * (Mathf.PI / 4f);
                float cx = Mathf.Cos(ang), cz = Mathf.Sin(ang);
                for (int st = 1; st <= 2; st++)
                {
                    float d = reach * st * 0.5f;
                    float qx = x + cx * d, qz = z + cz * d;
                    if (!InField(qx, qz)) continue;
                    float q = FieldSurf(qx, qz);
                    if (surf - q > drop * st * 0.5f) return true;
                    if (q - surf > wall * st * 0.5f) return true;
                }
            }
            return false;
        }

        private static bool Blocked(float x, float z, float r, int layer)
        {
            for (int i = 0; i < reserved.Count; i++)
            {
                Vector4 v = reserved[i];
                float need;
                int other = (int)v.w;
                if (layer == LGrass)
                    need = other == LTree ? 0.9f : other == LRock ? v.z * 0.85f : other == LBush ? v.z * 0.55f : v.z * 0.8f;
                else if (layer == LSmall)
                    need = other == LTree ? 1.2f : v.z + r * 0.5f;
                else if (other == LTree && layer != LTree)
                    need = layer == LRock ? v.z + r + 1.0f : layer == LBush ? 2.4f + r * 0.5f : 1.3f + r * 0.5f;
                else
                    need = v.z + r;
                // Kolo 6: mezi pevnými objekty nikdy méně, než audit považuje za překryv.
                if (Rules6 && layer <= LStone && other <= LStone) need = Mathf.Max(need, 0.62f * (v.z + r));
                float dx = v.x - x, dz = v.y - z;
                if (dx * dx + dz * dz < need * need) return true;
            }
            return false;
        }

        // ── vrstvy ─────────────────────────────────────────────────

        private static void RunLayer(ObjectSpawner sp, Table t, int layer, float margin, List<EcoPlacement> output)
        {
            float cell = CellSize[layer];
            float jit = Jitter[layer];
            int ix0 = Mathf.FloorToInt((colX0 - margin) / cell) - 1;
            int ix1 = Mathf.FloorToInt((colX0 + colSpan + margin) / cell) + 1;
            int iz0 = Mathf.FloorToInt((colZ0 - margin) / cell) - 1;
            int iz1 = Mathf.FloorToInt((colZ0 + colSpan + margin) / cell) + 1;

            for (int iz = iz0; iz <= iz1; iz++)
            for (int ix = ix0; ix <= ix1; ix++)
            {
                var rng = new Rng(Hash(ix, iz, 0x100 + layer, seed));
                float x = (ix + 0.5f + (rng.Next() - 0.5f) * 2f * jit) * cell;
                float z = (iz + 0.5f + (rng.Next() - 0.5f) * 2f * jit) * cell;

                bool inside = InColumn(x, z);
                if (!inside)
                {
                    if (margin <= 0f) continue;
                    if (x < colX0 - margin || z < colZ0 - margin || x >= colX0 + colSpan + margin || z >= colZ0 + colSpan + margin) continue;
                    if (!InField(x - 3f, z - 3f) || !InField(x + 3f, z + 3f)) continue;
                }

                float dither = rng.Next();
                Describe(x, z, dither, layer, out Site s);

                bool ok;
                EcoPlacement p = default;
                switch (layer)
                {
                    case LTree: ok = TryTree(sp, t, ref rng, in s, inside && !proxyMode, ref p); break;
                    case LRock: ok = TryRock(sp, t, ref rng, in s, inside, ref p); break;
                    case LBush: ok = TryBush(sp, t, ref rng, in s, ref p); break;
                    case LStone: ok = TryStone(sp, t, ref rng, in s, ref p); break;
                    case LSmall: ok = TrySmall(sp, t, ref rng, in s, ref p); break;
                    case LPebble: ok = TryPebble(sp, t, ref rng, in s, ref p); break;
                    default: ok = TryGrass(sp, t, ref rng, in s, ref p); break;
                }
                if (!ok) continue;

                // Kolo 6: stopa pevného objektu celá ve vlastním sloupci (viz FootprintFactor).
                // Kandidáti za hranou (okraj pro rezervace) se dál rezervují – jen opatrně navíc.
                if (inside && layer <= LStone && Rules6
                    && (proxyMode ? GridBorderDistance(x, z, proxyGrid) : BorderDistance(x, z)) < FootprintFactor * p.radius + 0.05f)
                {
                    BorderRejects++;
                    if (layer == LRock && (InPool(t.smRuin, p.spawnable) || InPool(t.smLimeTower, p.spawnable) || InPool(t.smLavaSpire, p.spawnable))) K19Big[14]++;
                    continue;
                }

                if (layer != LGrass)
                    reserved.Add(new Vector4(x, z, p.radius, layer));

                if (!inside) continue;

                p.cellX = ix; p.cellZ = iz; p.layer = layer;
                p.biome = (byte)s.biome;
                output.Add(p);
                if (!proxyMode) { RegionPlaced[s.region]++; RegionLayerPlaced[s.region * 8 + layer]++; }
            }
        }

        private static float BaseScale(ObjectSpawner sp, int si, ref Rng rng)
        {
            SpawnableObject so = sp.GetSpawnable(si);
            Vector2 r = so != null ? so.scaleRange : new Vector2(1f, 1f);
            return Mathf.Lerp(r.x, r.y, rng.Next()) * sp.GlobalScale;
        }

        /// <summary>Kolikrát které pravidlo odmítlo strom (pořadí jako v TryTree) – pro /props stat.</summary>
        public static readonly int[] TreeReject = new int[16];
        public static readonly string[] TreeRejectName = {
            "niva/pobřeží", "u moře", "sníh", "sklon", "voda v poli", "výhled na řeku", "čedič",
            "hustota háje", "převis", "hrana/stěna", "koryto/jezero", "sklon stopy", "nad vodou", "mesh", "zapadlý kmen" };
        private static bool Rej(int i) { TreeReject[i]++; if (curRegion == (int)BiomeRegion.Oasis) OasisTreeReject[i]++; return false; }
        /// <summary>Kolo 19 (diagnostika): odmítnutí stromů v oáze podle pravidla (pořadí jako TreeRejectName).</summary>
        public static readonly int[] OasisTreeReject = new int[16];
        private static int curRegion;

        private static bool TryTree(ObjectSpawner sp, Table t, ref Rng rng, in Site s, bool inside, ref EcoPlacement p)
        {
            curRegion = s.region;
            // ── tvrdá pravidla ──
            if (s.biome == EcoBiome.Riparian || s.biome == EcoBiome.Coast) return Rej(0);
            if (s.surf - sea < TreeMinAboveSea) return Rej(1);
            if (s.surf > s.snowLine - 40f) return Rej(2);
            if (s.slope > TreeMaxSlope) return Rej(3);
            if (s.dW < TreeMinWaterDistance) return Rej(4);
            // Kolo 21: klobouk obří houby má poloměr ~10 j. – v houbovém lese drží stromová vrstva od vody dvojnásobný odstup
            // (s777 (536, 1401): houba 12,5 m od jezírka, které hydrologie stromového pravidla nevidí).
            if (s.region == (int)BiomeRegion.Mushroom && s.dW < 2f * TreeMinWaterDistance + 4f) return Rej(4);
            if (s.riverish && s.hW < TreeRiverViewRise) return Rej(5);
            if (s.micro == (int)MicroBiome.BasaltField && s.microW > 0.35f) return Rej(6);

            float prob;
            if (s.biome == EcoBiome.Alpine)
                prob = 0.45f * Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.45f, 0.62f, s.grove2)) * (1f - Mathf.InverseLerp(0.3f, 0.75f, s.wA));
            else
            {
                prob = 0.85f * Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(GroveLo, GroveHi, s.grove)) + 0.03f;
                if (Compose8)
                {
                    // Kolo 8: mýtiny uvnitř hájů (druhý, jemnější šum) a řidší stromy na
                    // prvních desítkách metrů od vody – háj pak nezakryje řeku ani jezero.
                    prob *= Mathf.Lerp(0.25f, 1f, Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.33f, 0.43f, s.grove2)));
                    if (s.dW < 60f) prob *= Mathf.Lerp(0.35f, 1f, Mathf.InverseLerp(26f, 60f, s.dW));
                }
            }
            if (s.biome != EcoBiome.Alpine) prob = RegionTreeProb(in s, prob);
            else if (R(s, BiomeRegion.Desert) || R(s, BiomeRegion.Mesa) || R(s, BiomeRegion.Steppe)) prob *= 0.3f;
            else if (Barren(in s)) prob *= 0.04f;          // kolo 19: na lávových svazích jen ojedinělý ohořelý kmen
            else if (R(s, BiomeRegion.SnowPeaks) || R(s, BiomeRegion.Glacier)) prob *= 0.35f;   // kolo 20: řídké jehličnany jen v nižší části
            else if (R(s, BiomeRegion.Burnt)) prob *= 0.5f;
            if (rng.Next() >= prob) return Rej(7);
            // Kolo 29: stromy jsou první vrstva a rezervace nečtou – formace K28/K29 ale běží před nimi (audit: ohořelý kmen 2,5–5,4 m
            // od kráterového balvanu). Jen v regionech K29, ostatní biomy beze změny.
            if ((s.region == (int)BiomeRegion.MeteorCrater || s.region == (int)BiomeRegion.FossilReef || s.region == (int)BiomeRegion.MudVolcanoes)
                && Blocked(s.x, s.z, 2f, LTree)) return Rej(7);

            if (MaxOverhang(s.x, s.z, 2f) > 0.02f) return Rej(8);
            if (NearDrop(s.x, s.z, s.surf, 14f, 7f, 12f)) return Rej(9);
            if (!FarFromWater(s.x, s.z)) return Rej(10);

            // Sklon na šíři kořenového talíře (r = 1,2 m), usazení na šíři kmene (r = 0,6 m):
            // kmen sedí 0,15 m pod nejnižším bodem, do svahu tedy zajede nejvýš ~0,65 m.
            Footprint(s.x, s.z, 2.5f, inside, out float fmin, out float fmax, out float fc);
            if (Mathf.Atan((fmax - fmin) / 5f) * Mathf.Rad2Deg > TreeMaxSlope) return Rej(11);
            if (fmin - (s.surf - s.hW) < TreeMinAboveWater) return Rej(12);
            Footprint(s.x, s.z, 1.0f, inside, out fmin, out fmax, out fc);
            if (inside && !CleanMesh(s.x, s.z, 0.35f)) return Rej(13);
            // Kolo 6: kmen sedí 0,2 m pod nejnižším bodem stopy r = 1 m; nejvyšší bod téže stopy
            // nad ním smí být nejvýš TreeMaxBury (audit měří totéž, mez 1,0 m).
            if (Rules6 && fmax - (fmin - 0.2f) > TreeMaxBury) return Rej(14);

            // ── druh ──
            int si;
            bool golden = s.micro == (int)MicroBiome.GoldenGrove && s.microW > 0.35f;
            bool autumn = false;
            float regScale = 1f;
            if (golden) si = rng.Pick(t.birch);
            else if (s.biome == EcoBiome.Alpine && s.region < (int)BiomeRegion.Volcanic)
            {
                float u = rng.Next();
                si = u < 0.05f ? rng.Pick(t.firDead) : u < 0.17f ? rng.Pick(t.firYoung) : rng.Pick(t.fir);
            }
            else if (s.region != 0) si = RegionTree(t, ref rng, in s, out autumn, out regScale);
            else
            {
                float cold = Mathf.Max(1f - Mathf.InverseLerp(0.22f, 0.42f, s.temp),
                                       Mathf.InverseLerp(pal.grassTop - 40f, pal.grassTop + 20f, s.surf));
                float warm = Mathf.InverseLerp(0.45f, 0.72f, s.temp) * (1f - cold);
                float birch = (1f - warm) * (1f - cold) * (0.6f + 0.6f * s.hum) + 0.15f * s.hum;
                float sum = cold + warm + birch;
                float u = rng.Next() * sum;
                si = u < warm ? rng.Pick(t.oak) : u < warm + birch ? rng.Pick(t.birch) : rng.Pick(t.fir);
            }
            if (si < 0) return false;

            // Kolo 16: obří sekvoje jen na rovné stopě 4 m (široké kořenové náběhy) a jen v
            // každé druhé buňce mřížky (rozestup ≥ ~20 m); jinak mladá sekvoje / tmavý smrk.
            bool giant = t.smSequoia.Length > 0 && si == t.smSequoia[0];
            if (giant)
            {
                Footprint(s.x, s.z, 4f, inside, out float gmin, out float gmax, out _);
                int gx = Mathf.FloorToInt(s.x / 16f), gz = Mathf.FloorToInt(s.z / 16f);
                if (gmax - gmin > 1.4f || ((gx + gz) & 1) != 0 || ((gx ^ gz) & 2) != 0)
                { giant = false; si = PickOr(ref rng, t.smSequoiaYoung, t.fir); regScale = rng.Range(0.85f, 1.05f); }
            }

            float sc = BaseScale(sp, si, ref rng);
            float groveCore = Mathf.InverseLerp(0.52f, 0.7f, s.biome == EcoBiome.Alpine ? s.grove2 : s.grove);
            sc *= Mathf.Lerp(0.82f, 1.08f, groveCore) * rng.Range(0.92f, 1.08f);
            if (s.biome == EcoBiome.Alpine) sc *= Mathf.Lerp(1f, 0.72f, s.wA);
            sc *= regScale;

            p.spawnable = si;
            p.position = new Vector3(s.x, fmin - 0.2f, s.z);
            p.rotation = Quaternion.AngleAxis(rng.Next() * 360f, Vector3.up);
            p.scale = new Vector3(sc, sc * 1.3f, sc);
            p.radius = giant ? 3.5f : 2f;
            p.flags = golden || autumn ? (byte)1 : (byte)0;
            return true;
        }

        // ── kolo 14: regiony ──────────────────────────────────────────

        private static float RegionTreeProb(in Site s, float prob)
        {
            switch ((BiomeRegion)s.region)
            {
                case BiomeRegion.Birch: return prob * 1.05f;
                case BiomeRegion.Boreal:
                {
                    // Hustší jehličnaté háje se světlinami (stejný druhý šum jako mýtiny kola 8).
                    float b = 0.80f * Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.30f, 0.46f, s.grove)) + 0.18f;
                    if (Compose8)
                    {
                        b *= Mathf.Lerp(0.2f, 1f, Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.33f, 0.43f, s.grove2)));
                        if (s.dW < 60f) b *= Mathf.Lerp(0.35f, 1f, Mathf.InverseLerp(26f, 60f, s.dW));
                    }
                    return b;
                }
                case BiomeRegion.Steppe: return 0.05f * Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.52f, 0.64f, s.grove)) + 0.008f;
                case BiomeRegion.Desert: return 0.004f;
                case BiomeRegion.Mesa: return 0.004f;
                case BiomeRegion.Swamp: return 0.10f * Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.45f, 0.60f, s.grove)) + 0.015f;
                case BiomeRegion.Tundra: return 0.03f * Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.50f, 0.62f, s.grove2)) + 0.004f;
                // ── kolo 16 ──
                case BiomeRegion.BlackForest:
                {
                    // Velmi hustý tmavý les, ale s mýtinami (druhý šum) – ne jednolitá stěna.
                    float b = 0.85f * Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.15f, 0.30f, s.grove)) + 0.14f;
                    b *= Mathf.Lerp(0.05f, 1f, Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.32f, 0.42f, s.grove2)));
                    if (s.dW < 60f) b *= Mathf.Lerp(0.5f, 1f, Mathf.InverseLerp(26f, 60f, s.dW));
                    return b;
                }
                case BiomeRegion.Sequoia:
                {
                    float b = 0.62f * Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.25f, 0.42f, s.grove)) + 0.06f;
                    b *= Mathf.Lerp(0.3f, 1f, Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.33f, 0.43f, s.grove2)));
                    if (s.dW < 60f) b *= Mathf.Lerp(0.35f, 1f, Mathf.InverseLerp(26f, 60f, s.dW));
                    return b;
                }
                case BiomeRegion.Flowers: return 0.12f * Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.58f, 0.70f, s.grove)) + 0.006f;
                case BiomeRegion.Heath: return 0.015f * Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.60f, 0.72f, s.grove)) + 0.002f;
                case BiomeRegion.Sakura:
                {
                    // Sakurové háje: hustší jádra (grove), mezi nimi rozvolněné solitéry.
                    float b = 0.55f * Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.30f, 0.50f, s.grove)) + 0.12f;
                    if (s.dW < 60f) b *= Mathf.Lerp(0.55f, 1f, Mathf.InverseLerp(26f, 60f, s.dW));
                    return b;
                }
                case BiomeRegion.Bamboo:
                {
                    float b = 0.55f * Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.30f, 0.46f, s.grove)) + 0.05f;
                    b *= Mathf.Lerp(0.25f, 1f, Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.33f, 0.43f, s.grove2)));
                    if (s.dW < 60f) b *= Mathf.Lerp(0.4f, 1f, Mathf.InverseLerp(26f, 60f, s.dW));
                    return b;
                }
                case BiomeRegion.Jungle:
                {
                    float b = 0.55f * Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.15f, 0.32f, s.grove)) + 0.15f;
                    b *= Mathf.Lerp(0.35f, 1f, Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.33f, 0.43f, s.grove2)));
                    if (s.dW < 60f) b *= Mathf.Lerp(0.6f, 1f, Mathf.InverseLerp(26f, 60f, s.dW));
                    return b;
                }
                case BiomeRegion.FernGorge: return 0.45f * Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.30f, 0.50f, s.grove)) + 0.15f;
                // ── kolo 19 ──
                case BiomeRegion.Volcanic: return 0.006f;
                case BiomeRegion.Burnt:
                {
                    // Spáleniště: husté stojící ohořelé kmeny v bývalých hájích, mezi nimi holé mýtiny (grove2).
                    float b = 0.55f * Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.28f, 0.46f, s.grove)) + 0.06f;
                    b *= Mathf.Lerp(0.15f, 1f, Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.33f, 0.43f, s.grove2)));
                    return b;
                }
                case BiomeRegion.Karst: return 0.12f * Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.44f, 0.58f, s.grove)) + 0.012f;   // řídké háje – siluetu dělají věže
                case BiomeRegion.Petrified: return 0.004f;
                case BiomeRegion.Ruins: return 0.07f * Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.46f, 0.58f, s.grove)) + 0.012f;
                case BiomeRegion.Oasis:
                    // Palmy v hloučcích (grove2) – oáza je úzký pás nad vodou, stromy až za odstupem od břehu.
                    return 0.30f * Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.36f, 0.50f, s.grove2)) + 0.22f;
                // ── kolo 20 ──
                case BiomeRegion.SnowPeaks: return 0.06f * Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.48f, 0.62f, s.grove)) + 0.006f;
                case BiomeRegion.Glacier: return 0.10f * Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.50f, 0.62f, s.grove)) + 0.006f;   // řídké smrky
                case BiomeRegion.FrozenOcean: return 0f;
                case BiomeRegion.Mangrove: return 0f;      // stromy mangrov rostou ve vrstvě keřů (smí k vodě, ne do ní)
                case BiomeRegion.SaltFlat: return 0f;
                case BiomeRegion.Geothermal: return 0.004f;
                // ── kolo 21 ──
                case BiomeRegion.Crystal: return 0.05f * Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.48f, 0.62f, s.grove)) + 0.006f;   // řídké nízké smrky
                case BiomeRegion.Mushroom:
                {
                    // Obří houby v hájích, mezi nimi průchozí mýtiny (grove2) – hustota jako les, ne stěna.
                    float b = 0.40f * Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.30f, 0.46f, s.grove)) + 0.10f;
                    b *= Mathf.Lerp(0.35f, 1f, Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.33f, 0.43f, s.grove2)));
                    return b;
                }
                case BiomeRegion.Cliffs: return 0.03f * Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.46f, 0.60f, s.grove)) + 0.004f;   // větrem ohnuté stromy
                // ── kolo 28 ──
                case BiomeRegion.BasaltCoast: return 0.02f * Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.50f, 0.62f, s.grove)) + 0.002f;   // ojedinělé zakrslé smrky
                case BiomeRegion.ObsidianPlain: return 0f;
                case BiomeRegion.AlabasterPlateau: return 0.012f;
                // ── kolo 29 ──
                case BiomeRegion.MeteorCrater:
                {
                    // jen spálený prstenec za valem (1,15–1,9 R): ohořelé kmeny, uvnitř kráteru nic
                    float cx = CraterMath.X(new float2(s.x, s.z), craterCol);
                    if (cx >= CraterMath.ReachX) return 0f;
                    return 0.10f * Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(1.12f, 1.3f, cx)) * (1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(1.7f, 2.0f, cx)));
                }
                case BiomeRegion.FossilReef: return 0.008f;
                case BiomeRegion.MudVolcanoes: return 0f;
                default: return prob;
            }
        }

        private static int RegionTree(Table t, ref Rng rng, in Site s, out bool autumn, out float scale)
        {
            autumn = false;
            scale = 1f;
            float u = rng.Next();
            switch ((BiomeRegion)s.region)
            {
                case BiomeRegion.Boreal:
                    scale = rng.Range(0.95f, 1.12f);
                    return u < 0.04f ? PickOr(ref rng, t.firDead, t.fir)
                         : u < 0.16f ? PickOr(ref rng, t.firYoung, t.fir)
                         : u < 0.21f ? rng.Pick(t.birch)
                         : rng.Pick(t.fir);
                case BiomeRegion.Birch:
                {
                    // Podzim ve shlucích (šum 95 m), jinde jen ojediněle – žádná plošně oranžová plocha.
                    float an = Noise(s.x, s.z, 95f, offAutumn);
                    autumn = rng.Next() < (an > 0.56f ? 0.85f : 0.12f);
                    return u < 0.36f ? rng.Pick(t.birch)
                         : u < 0.68f ? PickOr(ref rng, t.smBirch, t.birch)
                         : u < 0.86f ? PickOr(ref rng, t.smBroad, t.oak)
                         : u < 0.95f ? rng.Pick(t.oak)
                         : PickOr(ref rng, t.deadBirch, t.birch);
                }
                case BiomeRegion.Steppe:
                    scale = rng.Range(0.85f, 1.05f);
                    return u < 0.62f ? PickOr(ref rng, t.smAcacia, t.oak)
                         : u < 0.86f ? PickOr(ref rng, t.smBroad, t.oak)
                         : rng.Pick(t.oak);
                case BiomeRegion.Desert:
                case BiomeRegion.Mesa:
                    return PickOr(ref rng, t.deadTree, t.firDead);
                case BiomeRegion.Swamp:
                    scale = rng.Range(0.75f, 0.95f);
                    return u < 0.28f ? PickOr(ref rng, t.deadBroad, t.oak)
                         : u < 0.42f ? PickOr(ref rng, t.deadBirch, t.birch)
                         : u < 0.75f ? rng.Pick(t.birch)
                         : PickOr(ref rng, t.smBroad, t.oak);
                case BiomeRegion.Tundra:
                    scale = rng.Range(0.6f, 0.8f);
                    return u < 0.55f ? PickOr(ref rng, t.firYoung, t.fir) : PickOr(ref rng, t.firDead, t.fir);
                // ── kolo 16 ──
                case BiomeRegion.BlackForest:
                    scale = rng.Range(1.0f, 1.18f);
                    return u < 0.03f ? PickOr(ref rng, t.firDead, t.fir)
                         : u < 0.17f ? PickOr(ref rng, t.smDarkFirLow, t.firYoung)
                         : PickOr(ref rng, t.smDarkFir, t.fir);
                case BiomeRegion.Sequoia:
                    scale = rng.Range(0.82f, 0.95f);
                    return u < 0.40f ? PickOr(ref rng, t.smSequoia, t.fir)
                         : u < 0.75f ? PickOr(ref rng, t.smSequoiaYoung, t.fir)
                         : PickOr(ref rng, t.smDarkFir, t.fir);
                case BiomeRegion.Flowers:
                    scale = rng.Range(0.85f, 1.0f);
                    return u < 0.45f ? PickOr(ref rng, t.smBroad, t.oak) : u < 0.75f ? rng.Pick(t.birch) : PickOr(ref rng, t.smSakura, t.oak);
                case BiomeRegion.Heath:
                    scale = rng.Range(0.6f, 0.8f);
                    return u < 0.6f ? PickOr(ref rng, t.smBirch, t.birch) : PickOr(ref rng, t.deadBirch, t.birch);
                case BiomeRegion.Sakura:
                    scale = rng.Range(0.9f, 1.1f);
                    return u < 0.80f ? PickOr(ref rng, t.smSakura, t.oak) : PickOr(ref rng, t.smBroad, t.oak);
                case BiomeRegion.Bamboo:
                    scale = rng.Range(0.9f, 1.1f);
                    return u < 0.72f ? PickOr(ref rng, t.smBamboo, t.birch)
                         : u < 0.92f ? PickOr(ref rng, t.smBambooLow, t.birch)
                         : PickOr(ref rng, t.smBroad, t.oak);
                case BiomeRegion.Jungle:
                    // Kolo 16 (výkon): obří kmen jen ~25 %, menší měřítko – hustota z podrostu, ne z obrů.
                    scale = rng.Range(0.72f, 0.88f);
                    if (t.smJungle.Length > 1 && u < 0.25f) return t.smJungle[1];
                    return u < 0.88f ? PickOr(ref rng, t.smJungle, t.oak) : PickOr(ref rng, t.smBroad, t.oak);
                case BiomeRegion.FernGorge:
                    // Pravěká rokle: převládají stromové kapradiny (ve stromové vrstvě kvůli rozestupu),
                    // mezi nimi řídké vysoké stromy.
                    if (u < 0.70f) { scale = rng.Range(1.15f, 1.5f); return PickOr(ref rng, t.smTreeFern, t.fern); }
                    scale = rng.Range(0.85f, 1.05f);
                    return u < 0.85f ? PickOr(ref rng, t.smJungle, t.oak) : PickOr(ref rng, t.smDarkFir, t.fir);
                // ── kolo 19 ──
                case BiomeRegion.Volcanic:
                    scale = rng.Range(0.8f, 1.0f);
                    return u < 0.7f ? PickOr(ref rng, t.smCharredLow, t.deadTree) : PickOr(ref rng, t.deadTree, t.firDead);
                case BiomeRegion.Burnt:
                    // Silueta: holé svislé kmeny bez korun; jen ~8 % přeživších akácií.
                    scale = rng.Range(0.88f, 1.15f);
                    return u < 0.60f ? PickOr(ref rng, t.smCharred, t.deadTree)
                         : u < 0.92f ? PickOr(ref rng, t.smCharredLow, t.deadTree)
                         : PickOr(ref rng, t.smAcacia, t.oak);
                case BiomeRegion.Karst:
                    scale = rng.Range(0.8f, 1.0f);
                    return u < 0.55f ? PickOr(ref rng, t.smBroad, t.oak) : u < 0.80f ? PickOr(ref rng, t.smBambooLow, t.birch) : PickOr(ref rng, t.smAcacia, t.oak);
                case BiomeRegion.Petrified:
                    return PickOr(ref rng, t.deadTree, t.firDead);
                case BiomeRegion.Ruins:
                    scale = rng.Range(0.85f, 1.05f);
                    return u < 0.45f ? PickOr(ref rng, t.smBroad, t.oak) : u < 0.75f ? PickOr(ref rng, t.smAcacia, t.oak) : PickOr(ref rng, t.smJungle, t.oak);
                case BiomeRegion.Oasis:
                    scale = rng.Range(0.9f, 1.15f);
                    return u < 0.72f ? PickOr(ref rng, t.smPalm, t.oak) : PickOr(ref rng, t.smPalmYoung, t.oak);
                // ── kolo 20 ──
                case BiomeRegion.SnowPeaks:
                    scale = rng.Range(0.6f, 0.82f);
                    return u < 0.45f ? PickOr(ref rng, t.firYoung, t.fir) : u < 0.80f ? PickOr(ref rng, t.smDarkFirLow, t.fir) : PickOr(ref rng, t.firDead, t.fir);
                case BiomeRegion.Glacier:
                    scale = rng.Range(0.82f, 1.0f);
                    return u < 0.50f ? PickOr(ref rng, t.smDarkFir, t.fir) : u < 0.85f ? rng.Pick(t.fir) : PickOr(ref rng, t.firDead, t.fir);
                case BiomeRegion.Geothermal:
                    return PickOr(ref rng, t.deadTree, t.firDead);
                // ── kolo 21 ──
                case BiomeRegion.Crystal:
                    scale = rng.Range(0.62f, 0.82f);
                    return u < 0.65f ? PickOr(ref rng, t.smDarkFirLow, t.fir) : PickOr(ref rng, t.firDead, t.fir);
                case BiomeRegion.Mushroom:
                    // Silueta: široké klobouky obřích hub nad nízkým podrostem; jen třetina běžných stromů.
                    scale = rng.Range(0.8f, 1.15f);
                    return u < 0.64f ? PickOr(ref rng, t.smGiantShroom, t.oak) : u < 0.86f ? PickOr(ref rng, t.smBroad, t.oak) : PickOr(ref rng, t.smDarkFir, t.fir);
                case BiomeRegion.Cliffs:
                    scale = rng.Range(0.62f, 0.82f);
                    return PickOr(ref rng, t.smBroad, t.oak);
                // ── kolo 28 ──
                case BiomeRegion.BasaltCoast:
                    scale = rng.Range(0.58f, 0.78f);
                    return PickOr(ref rng, t.smDarkFirLow, t.fir);
                case BiomeRegion.ObsidianPlain:
                    return PickOr(ref rng, t.deadTree, t.firDead);
                case BiomeRegion.AlabasterPlateau:
                    scale = rng.Range(0.7f, 0.9f);
                    return u < 0.6f ? PickOr(ref rng, t.smAcacia, t.oak) : PickOr(ref rng, t.deadTree, t.oak);
                // ── kolo 29 ──
                case BiomeRegion.MeteorCrater:
                    scale = rng.Range(0.75f, 1.0f);
                    return u < 0.55f ? PickOr(ref rng, t.smCharred, t.deadTree) : PickOr(ref rng, t.smCharredLow, t.deadTree);
                case BiomeRegion.FossilReef:
                    scale = rng.Range(0.7f, 0.9f);
                    return u < 0.5f ? PickOr(ref rng, t.smAcacia, t.oak) : PickOr(ref rng, t.deadTree, t.oak);
                default:
                    return rng.Pick(t.oak);
            }
        }

        public static readonly int[] RockReject = new int[12];
        public static readonly string[] RockRejectName = {
            "hustota", "sklon", "koryto", "voda v poli", "sníh", "druh", "rozestup", "převis",
            "reliéf stopy", "trčí nad svahem", "nad vodou", "mesh" };
        private static bool RejR(int i) { RockReject[i]++; return false; }

        private static bool TryRock(ObjectSpawner sp, Table t, ref Rng rng, in Site s, bool inside, ref EcoPlacement p)
        {
            if (s.micro == (int)MicroBiome.BasaltField && s.microW > 0.35f)
            {
                // Čedič: jen svislé skalní útvary, jinak nic (zachovává vzhled mikro-biomu).
            }
            float o = s.outcrop;
            float prob;
            bool cliff = false;
            switch (s.biome)
            {
                case EcoBiome.Alpine:
                    prob = 0.42f + 0.3f * Mathf.InverseLerp(0.55f, 0.70f, o);
                    cliff = s.slope > 24f;
                    break;
                case EcoBiome.Riparian:
                    prob = s.hW > 1.0f ? 0.03f : 0f; break;
                case EcoBiome.Coast:
                    prob = s.surf - sea > 4.5f && s.slope < 30f ? 0.05f : 0f; break;
                default:
                    prob = (0.55f * Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.62f, 0.76f, o)) + 0.012f)
                           * (0.6f + 0.4f * Mathf.InverseLerp(6f, 20f, s.slope));
                    cliff = s.slope > 26f;
                    break;
            }
            int[] rockPool = null, cliffPool = null;
            float poolShare = 1f;
            if (s.biome == EcoBiome.Meadow || s.biome == EcoBiome.Alpine)
            {
                switch ((BiomeRegion)s.region)
                {
                    case BiomeRegion.Boreal: prob *= 1.35f; rockPool = t.smBoulder; poolShare = 0.55f; break;
                    case BiomeRegion.Birch: rockPool = t.smBoulder; poolShare = 0.3f; break;
                    case BiomeRegion.Steppe: prob *= 0.75f; rockPool = t.smBoulder; poolShare = 0.35f; break;
                    case BiomeRegion.Desert: prob = prob * 0.8f + 0.01f; rockPool = t.smDesertRock; cliffPool = t.smDesertRock; poolShare = 0.85f; break;
                    case BiomeRegion.Mesa: prob = prob * 1.2f + 0.05f; rockPool = t.smMesaRock; cliffPool = t.smMesaRock; poolShare = 0.9f; cliff = cliff || s.slope > 22f; break;
                    case BiomeRegion.Swamp: prob *= 0.4f; break;
                    case BiomeRegion.Tundra: prob *= 1.3f; break;
                    // kolo 16
                    case BiomeRegion.Heath:
                        // Kamenné monolity: řídké, jen na mírných svazích a ve vlastních ohniscích (šum 220 m).
                        prob = prob * 0.8f + 0.035f * Mathf.InverseLerp(0.55f, 0.68f, Noise(s.x, s.z, 220f, offFlower));
                        rockPool = t.smMonolith; poolShare = 0.6f; break;
                    case BiomeRegion.Sakura: rockPool = t.smLightBoulder; poolShare = 0.75f; break;
                    case BiomeRegion.BlackForest: case BiomeRegion.Sequoia: rockPool = t.smBoulder; poolShare = 0.6f; break;
                    case BiomeRegion.Flowers: prob *= 0.6f; rockPool = t.smBoulder; poolShare = 0.4f; break;
                    case BiomeRegion.Bamboo: case BiomeRegion.Jungle: case BiomeRegion.FernGorge: rockPool = t.smBoulder; poolShare = 0.7f; break;
                    // ── kolo 19: siluetová pravidla ──
                    case BiomeRegion.Volcanic:
                        // čedičové jehly ve shlucích (outcrop), jinde lávové balvany
                        prob = prob * 1.3f + 0.06f + 0.10f * Mathf.InverseLerp(0.50f, 0.66f, o);
                        rockPool = Noise(s.x, s.z, 150f, offGap) > 0.47f ? t.smLavaSpire : t.smLavaRock; cliffPool = t.smLavaRock; poolShare = 0.9f; break;   // pole čedičových jehel
                    case BiomeRegion.Burnt: prob *= 0.5f; rockPool = t.smBoulder; poolShare = 0.4f; break;
                    case BiomeRegion.Karst:
                        // vápencové věže v řídkých skupinách (šum 180 m), mezi nimi travnatá dna
                        prob = 0.10f + 0.25f * Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.42f, 0.58f, Noise(s.x, s.z, 180f, offFlower)));
                        rockPool = t.smLimeTower; cliffPool = t.smLimeRock; poolShare = 0.85f; break;
                    case BiomeRegion.Petrified:
                        // silueta: vodorovné ležící kamenné kmeny v ohniscích (šum 120 m) – ve vrstvě balvanů (stopa 0,7 r)
                        prob = 0.10f + 0.25f * Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.40f, 0.56f, Noise(s.x, s.z, 120f, offClump)));
                        rockPool = t.smPetLog; cliffPool = t.smMesaRock; poolShare = 0.85f; break;
                    case BiomeRegion.Ruins:
                        // vzácná naleziště zdí, sloupů a oblouků (šum 90 m)
                        prob = 0.06f + 0.26f * Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.44f, 0.60f, Noise(s.x, s.z, 90f, offClump)));
                        rockPool = t.smRuin; cliffPool = null; poolShare = 0.92f; cliff = false; break;
                    case BiomeRegion.Oasis: prob *= 0.4f; rockPool = t.smDesertRock; poolShare = 0.7f; break;
                    // ── kolo 20: siluetová pravidla ──
                    case BiomeRegion.SnowPeaks:
                        // zubaté zasněžené hřebeny na hranách a svazích (šum 170 m), jinde zasněžené balvany
                        prob = prob * 1.15f + 0.05f;
                        rockPool = Noise(s.x, s.z, 170f, offGap) > 0.48f ? t.smSnowRidge : t.smSnowRock; cliffPool = t.smSnowRidge; poolShare = 0.85f; break;
                    case BiomeRegion.Glacier:
                        // ledové stěny a séraky ve skupinách (šum 160 m), mezi nimi bludné balvany na morénách
                        prob = 0.08f + 0.22f * Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.42f, 0.58f, Noise(s.x, s.z, 160f, offFlower)));
                        rockPool = Noise(s.x, s.z, 160f, offFlower) > 0.47f ? t.smIceWall : t.smErratic; cliffPool = t.smErratic; poolShare = 0.85f; break;
                    case BiomeRegion.FrozenOcean: prob *= 0.6f; rockPool = t.smErratic; poolShare = 0.6f; break;
                    case BiomeRegion.Mangrove: prob *= 0.3f; break;
                    case BiomeRegion.SaltFlat:
                        // solné kupy a krystalové shluky v řídkých ohniscích (šum 140 m), mezi nimi prázdná pláň
                        prob = 0.015f + 0.10f * Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.50f, 0.64f, Noise(s.x, s.z, 140f, offClump)));
                        rockPool = t.smSaltMound; cliffPool = null; poolShare = 0.95f; cliff = false; break;
                    case BiomeRegion.Geothermal:
                        // travertinové terasy na mírných svazích a výduchy/kužely gejzírů v ohniscích (šum 110 m)
                        prob = 0.06f + 0.24f * Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.44f, 0.60f, Noise(s.x, s.z, 110f, offClump)));
                        rockPool = Noise(s.x, s.z, 60f, offGap) > 0.5f ? t.smTerrace : t.smVent; cliffPool = null; poolShare = 0.9f; cliff = false; break;
                    // ── kolo 21: siluetová pravidla ──
                    case BiomeRegion.Crystal:
                        // krystalové shluky a obří krystaly v ohniscích (šum 150 m), vzácně jeskynní brána, na svazích minerální stěny
                        prob = 0.07f + 0.22f * Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.44f, 0.60f, Noise(s.x, s.z, 150f, offClump)));
                        rockPool = Noise(s.x, s.z, 90f, offGap) > 0.63f ? PickPool(t.smCrystalCave, t.smCrystal) : t.smCrystal;
                        cliffPool = PickPool(t.smMineralWall, t.smCrystal); poolShare = 0.9f; cliff = cliff || s.slope > 18f; break;
                    case BiomeRegion.Mushroom: prob *= 0.5f; rockPool = t.smBoulder; poolShare = 0.6f; break;
                    case BiomeRegion.Cliffs:
                    {
                        // silueta: útesové bloky, pilíře a čedičové varhany v ohniscích (šum 130 m) i na rovině nad pobřežím,
                        // vzácně průchozí brána; mezi ohnisky křídové balvany
                        bool dk = CliffDark(in s);
                        float fc21 = Noise(s.x, s.z, 130f, offClump), pk21 = Noise(s.x, s.z, 70f, offGap);
                        prob = 0.10f + 0.24f * Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.40f, 0.56f, fc21));
                        rockPool = pk21 > 0.66f ? PickPool(t.smSeaArch, t.smChalkCliff) : fc21 < 0.42f ? t.smChalkBoulder : dk ? PickPool(t.smBasalt, t.smChalkCliff) : t.smChalkCliff;
                        cliffPool = dk ? PickPool(t.smBasalt, t.smChalkCliff) : t.smChalkCliff; poolShare = 0.9f; cliff = cliff || s.slope > 20f; break;
                    }
                    // ── kolo 28: siluetová pravidla ──
                    case BiomeRegion.BasaltCoast:
                    {
                        // kolonády sloupů v ohniscích (šum 130 m), mezi nimi schodiště a dlažba sloupů (pochozí, skákací pasáže)
                        float fc28 = Noise(s.x, s.z, 130f, offClump), pk28 = Noise(s.x, s.z, 70f, offGap);
                        prob = 0.16f + 0.32f * Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.40f, 0.56f, fc28));   // vyšší než útesy – odstup od hrany sloupce část kandidátů odmítne
                        rockPool = pk28 > 0.52f ? PickPool(t.smBasaltStep, t.smBasaltCol) : fc28 < 0.44f ? PickPool(t.smBasaltPave, t.smBasaltCol) : t.smBasaltCol;
                        cliffPool = PickPool(t.smBasaltCol, t.rockCliff); poolShare = 0.92f; cliff = cliff || s.slope > 20f; break;
                    }
                    case BiomeRegion.ObsidianPlain:
                        // ostré skelné střepy a hřebeny v ohniscích (šum 140 m), mezi nimi holá rozpraskaná pláň s balvany
                        prob = 0.08f + 0.36f * Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.42f, 0.58f, Noise(s.x, s.z, 140f, offClump)));
                        rockPool = Noise(s.x, s.z, 80f, offGap) > 0.45f ? PickPool(t.smObsSpire, t.smLavaRock) : PickPool(t.smObsRock, t.smLavaRock);
                        cliffPool = PickPool(t.smObsRock, t.smLavaRock); poolShare = 0.92f; break;
                    case BiomeRegion.AlabasterPlateau:
                    {
                        // hladké věže a stolové desky v ohniscích (šum 160 m), vzácně průchozí oblouk, jinde oblé balvany
                        float fa28 = Noise(s.x, s.z, 160f, offClump);
                        prob = 0.09f + 0.33f * Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.42f, 0.58f, fa28));
                        rockPool = Noise(s.x, s.z, 90f, offGap) > 0.64f ? PickPool(t.smAlaArch, t.smAlaTower) : fa28 > 0.5f ? PickPool(t.smAlaTower, t.smAlaRock) : PickPool(t.smAlaRock, t.rockLarge);
                        cliffPool = PickPool(t.smAlaRock, t.rockCliff); poolShare = 0.9f; break;
                    }
                }
            }
            // Kolo 28: formace (sloupy, schodiště, střepy, oblouky, věže, balvany) staví vlastní mřížky RunFormations – ve vrstvě
            // balvanů by se přes hranu sloupce nevyhnuly (škvíry) a odstup od hrany je skoro všechny vyřadil. Tady nic.
            if (Formations28 && (R(s, BiomeRegion.BasaltCoast) || R(s, BiomeRegion.ObsidianPlain) || R(s, BiomeRegion.AlabasterPlateau)
                                 || R(s, BiomeRegion.MeteorCrater) || R(s, BiomeRegion.FossilReef) || R(s, BiomeRegion.MudVolcanoes))) return RejR(0);   // kolo 29 také jen formace
            if (false && (s.biome == EcoBiome.Coast || s.biome == EcoBiome.Riparian) && R(s, BiomeRegion.BasaltCoast))
            {
                float fc28 = Noise(s.x, s.z, 130f, offClump);
                prob = Mathf.Min(s.surf - sea, s.hW) > 1.2f && s.slope < 42f ? 0.16f + 0.26f * Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.40f, 0.56f, fc28)) : 0f;
                rockPool = Noise(s.x, s.z, 70f, offGap) > 0.52f ? PickPool(t.smBasaltStep, t.smBasaltCol) : fc28 < 0.44f ? PickPool(t.smBasaltPave, t.smBasaltCol) : t.smBasaltCol;
                cliffPool = PickPool(t.smBasaltCol, t.rockCliff); poolShare = 0.92f; cliff = s.slope > 22f;
            }
            // Kolo 21: útesové pobřeží – útesy, pilíře, čedičové sloupy a průchozí brány na pevném břehu (nikdy ve vodě – pravidla níž)
            if ((s.biome == EcoBiome.Coast || s.biome == EcoBiome.Riparian) && R(s, BiomeRegion.Cliffs))   // i zátoky a ústí (niva)
            {
                bool dk = CliffDark(in s);
                prob = Mathf.Min(s.surf - sea, s.hW) > 1.2f && s.slope < 42f
                    ? 0.16f + 0.24f * Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.42f, 0.58f, Noise(s.x, s.z, 130f, offClump))) : 0f;
                rockPool = Noise(s.x, s.z, 70f, offGap) > 0.64f ? PickPool(t.smSeaArch, t.smChalkCliff) : dk ? PickPool(t.smBasalt, t.smChalkCliff) : t.smChalkCliff;
                cliffPool = dk ? PickPool(t.smBasalt, t.smChalkCliff) : t.smChalkCliff; poolShare = 0.9f; cliff = s.slope > 22f;
            }
            // Kolo 20: břeh zamrzlého moře – ledové bloky vyvržené na břeh (pobřeží osazuje jen málo)
            if (s.biome == EcoBiome.Coast && R(s, BiomeRegion.FrozenOcean)) { prob = s.surf - sea > 1.2f && s.slope < 28f ? 0.06f : 0f; rockPool = t.smErratic; poolShare = 0.7f; }
            if (s.micro == (int)MicroBiome.BasaltField && s.microW > 0.35f) { prob = 0.35f; cliff = true; rockPool = cliffPool = null; }
            if (rng.Next() >= prob) return RejR(0);

            float maxSlope = cliff ? CliffRockMaxSlope : RockMaxSlope;
            if (s.slope > maxSlope) return RejR(1);
            if (RiverCoreAt(s.x, s.z) > 0.02f) return RejR(2);
            if (s.dW < 1.5f) return RejR(3);
            if (s.surf > s.snowLine + 120f) return RejR(4);

            int si;
            if (rockPool != null && rockPool.Length > 0 && rng.Next() < poolShare)
                si = cliff && cliffPool != null && cliffPool.Length > 0 ? rng.Pick(cliffPool) : rng.Pick(rockPool);
            else si = cliff ? rng.Pick(t.rockCliff) : rng.Pick(t.rockLarge);
            if (si < 0) return RejR(5);
            PrefabShape sh = sp.GetShape(si);

            float sc = BaseScale(sp, si, ref rng);
            if (s.biome == EcoBiome.Alpine) sc *= rng.Range(1.0f, 1.35f);
            else if (s.biome == EcoBiome.Riparian) sc *= rng.Range(0.6f, 0.85f);
            else sc *= rng.Range(0.8f, 1.15f);

            float rad = sh.valid ? sh.radius * sc : 2f;
            float hgt = sh.valid ? sh.height * sc : 2f;
            // Kolo 19: stavby a věže stojí svisle, jen na rovině a s vlastním odstupem: ruiny ≥ 2,5 m volna
            // mezi kusy (žádné uzavřené kapsy pro hráče), vápencové věže ≥ 8 m, lávové jehly ≥ 3 m.
            bool ruin = InPool(t.smRuin, si), tower = InPool(t.smLimeTower, si), spire = InPool(t.smLavaSpire, si) || InPool(t.smSnowRidge, si);
            // Kolo 21: krystalová jeskyně a útesová brána stojí jako stavba (rovná stopa, odstup); krystaly, pilíře a čedičové sloupy jako jehly.
            ruin = ruin || InPool(t.smCrystalCave, si) || InPool(t.smSeaArch, si);
            spire = spire || InPool(t.smCrystal, si) || InPool(t.smSeaStack, si) || InPool(t.smBasalt, si);
            // Kolo 28: oblouk jako stavba (rovná stopa, odstup), schodiště a dlažba sloupů a stolové desky jako terasa,
            // kolonády, střepy a věže jako jehly.
            ruin = ruin || InPool(t.smAlaArch, si);
            spire = spire || InPool(t.smBasaltCol, si) || InPool(t.smObsSpire, si) || InPool(t.smAlaTower, si)
                    || InPool(t.smAlaRock, si) || InPool(t.smObsRock, si);   // balvany s velkým colliderem drží odstup ≥ průměr hráče (pilot s1337: škvíra 2,05 j.)
            // Kolo 20: ledové stěny a travertinové terasy stojí svisle na rovné stopě jako stavby (ruin), kupy soli taky.
            bool ice20 = InPool(t.smIceWall, si), terrace = InPool(t.smTerrace, si) || InPool(t.smSaltMound, si) || InPool(t.smVent, si)
                         ;   // kolo 28: schodiště a dlažba sloupů nejsou terasa – vystupují ze svahu (níže „stair“)
            ruin = ruin || ice20 || terrace;
            bool big19 = ruin || tower || spire;
            bool petLog = InPool(t.smPetLog, si);   // ležící kamenný kmen: dlouhá nízká stopa
            if (big19) K19Big[0]++;
            float extra = ice20 ? 4f : terrace ? 2.5f : ruin ? 2.5f : tower ? 8f : spire ? 3f : 0f;
            if ((ruin || tower) && (s.slope > (tower ? 22f : 18f) || MeshSlope(s.x, s.z) > (tower ? 26f : 22f))) return RejK(big19, 1);
            if (Blocked(s.x, s.z, rad + extra, LRock)) return RejK(big19, 6);
            // Převisová maska je v horách skoro všude; skutečný převis nad kamenem hlídá CleanMesh.
            if (MaxOverhang(s.x, s.z, rad * 0.7f) > 0.6f) return RejK(big19, 7);

            Footprint(s.x, s.z, Mathf.Max(0.5f, rad * 0.7f), inside, out float fmin, out float fmax, out float fc);
            // Skalní blok ve svahu smí být zapuštěný víc (je to výchoz, ne položený kámen).
            // Kolo 28: schodiště/dlažba sloupů smí ze svahu vystupovat (horní strana zapuštěná až 0,8 výšky) – s rovnou stopou
            // se na pobřežních svazích neosadily nikdy (doověření K28: 0 kusů na 3 seedech).
            bool stair = InPool(t.smBasaltStep, si) || InPool(t.smBasaltPave, si);
            if (stair && fmax - fmin > 0.8f * hgt) return RejK(big19, 8);
            if (!stair && fmax - fmin > (cliff || petLog ? 1.3f : 0.9f) * hgt) return RejK(big19, 8);   // visel by přes hranu
            if ((ruin || tower) && fmax - fmin > Mathf.Max(0.5f, (tower ? 0.35f : 0.15f) * rad)) return RejK(big19, 8);   // kolo 19: stavba jen na rovné stopě
            if (!stair && fc - fmin > (cliff || petLog ? 0.6f : 0.35f) * hgt) return RejK(big19, 9);    // spodní hrana by trčela nad svahem
            if (fmin - (s.surf - s.hW) < 0.6f) return RejK(big19, 10);          // do vody ani na hladinu
            if (inside && !CleanMesh(s.x, s.z, 2.0f)) return RejK(big19, 11);
            // Kolo 6: i balvan hlídá sklon skutečného meshe (audit: skalní blok na fazetě 60°).
            if (Rules6 && inside)
            {
                float rs = MeshSlope(s.x, s.z, out bool redge);
                if (rs > maxSlope + (redge ? 0f : 4f)) return RejK(big19, 1);
            }

            Vector3 n = Normal(s.x, s.z, Mathf.Max(1f, rad * 0.6f));
            // Kolo 16: monolity stojí svisle (jen nepatrný náklon).
            bool upright = InPool(t.smMonolith, si) || ruin || tower || spire;
            Quaternion tilt = Quaternion.Slerp(Quaternion.identity, Quaternion.FromToRotation(Vector3.up, n), upright ? 0.1f : 0.55f);

            p.spawnable = si;
            float minY = sh.valid ? sh.minY * sc : 0f;
            p.position = new Vector3(s.x, Mathf.Min(fc - (ruin ? 0.04f : cliff ? 0.3f : 0.18f) * hgt, fmin) - minY, s.z);
            // Kolo 16: stejná kontrola zapadnutí jako audit (mesh LOD0, střed + 8 bodů na 0,7 r) –
            // pole stopy mohlo reliéf malého balvanu podcenit (audit: SM_Balvan_Velky −0,8 m).
            if (Rules6 && inside)
            {
                float bottom = p.position.y + minY, tolB = 0.5f * hgt + 0.1f;
                float g0 = Ground(s.x, s.z, true);
                if (g0 - bottom > tolB)
                {
                    float wr = Mathf.Max(0.5f, rad * 0.7f), gw = g0;
                    for (int k = 0; k < 8; k++)
                    {
                        float a = k * Mathf.PI * 0.25f;
                        gw = Mathf.Min(gw, Ground(s.x + Mathf.Cos(a) * wr, s.z + Mathf.Sin(a) * wr, true));
                    }
                    if (gw - bottom > tolB || g0 > bottom + hgt - 0.35f) return RejK(big19, 8);
                }
            }
            p.rotation = (ruin || tower ? Quaternion.identity : tilt) * Quaternion.AngleAxis(rng.Next() * 360f, Vector3.up);
            p.scale = new Vector3(sc, sc * (ruin || tower ? 1f : rng.Range(0.85f, 1.1f)), sc);
            p.radius = Mathf.Max(0.8f, rad);
            if (big19) K19Big[1]++;
            return true;
        }

        /// <summary>Kolo 19 (diagnostika /props stat): velké kusy (ruiny, věže, jehly) – [0] vybráno, [1] osazeno, [2+i] odmítnuto pravidlem i, [14] stopa u hrany sloupce.</summary>
        public static readonly int[] K19Big = new int[16];
        private static bool RejK(bool big, int i) { if (big) K19Big[2 + i]++; return RejR(i); }

        private static bool InPool(int[] pool, int si) => pool != null && pool.Length > 0 && System.Array.IndexOf(pool, si) >= 0;

        /// <summary>Kolo 19 (diagnostika): odmítnutí keřové vrstvy v nových biomech [region − 16][pravidlo].</summary>
        public static readonly int[,] K19BushReject = new int[BiomeMath.Count - (int)BiomeRegion.Volcanic, 16];   // kolo 20: i nové biomy (22–27)
        private static bool RejB(in Site s, int i) { if (s.region >= (int)BiomeRegion.Volcanic && s.region < BiomeMath.Count && i < 16) K19BushReject[s.region - (int)BiomeRegion.Volcanic, i]++; return false; }

        private static bool TryBush(ObjectSpawner sp, Table t, ref Rng rng, in Site s, ref EcoPlacement p)
        {
            if (s.surf > s.snowLine - 20f) return false;
            if (s.micro == (int)MicroBiome.BasaltField && s.microW > 0.35f) return false;

            float gs = Mathf.InverseLerp(GroveLo, GroveHi, s.grove);
            float edge = Mathf.Clamp01(1f - Mathf.Abs(s.grove - (GroveLo + 0.03f)) / 0.09f);
            int[] pool;
            float prob;
            bool isLog = false;

            switch (s.biome)
            {
                case EcoBiome.Riparian:
                    if (s.hW < 1.8f || s.dW < 7f) return false;
                    prob = 0.06f; pool = rng.Next() < 0.1f ? t.deadBush : t.bushLow;
                    // Kolo 19: oáza – břeh lemují nízké palmy a palmové keře (stromy sem pravidla nepustí)
                    if (R(s, BiomeRegion.Oasis)) { float uo = rng.Next(); prob = 0.60f; pool = uo < 0.45f ? t.smPalmLow : uo < 0.8f ? t.smPalmBush : (t.smBush.Length > 0 ? t.smBush : t.bush); }
                    break;
                case EcoBiome.Coast:
                    // Kolo 20: mangrovy – stromy s kořeny na nízkém břehu laguny (nad vodou, ne do ní; voda hlídají pravidla níž)
                    if (R(s, BiomeRegion.Mangrove) && t.smMangrove.Length > 0)
                    {
                        if (s.surf - sea < 1.2f) return RejB(in s, 7);
                        float uM = rng.Next();
                        prob = 0.30f + 0.40f * Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.36f, 0.52f, s.grove));
                        pool = uM < 0.55f ? t.smMangrove : uM < 0.80f ? PickPool(t.smMangroveYoung, t.smMangrove) : PickPool(t.smMangroveRoot, t.smMangrove);
                        break;
                    }
                    if (Snowy(in s) || Barren(in s)) return false;   // kolo 20: zamrzlý břeh, solné a geotermální pobřeží bez keřů
                    if (s.surf - sea < 5f) return false;
                    prob = 0.03f; pool = rng.Next() < 0.25f ? t.deadBush : t.bushLow; break;
                case EcoBiome.Alpine:
                    prob = 0.05f * (1f - s.wA) + 0.02f;
                    pool = s.wA > 0.6f ? t.deadBush : t.bush;
                    // Kolo 14: výšková tundra – zakrslé keříky na mírných svazích pod sněhem.
                    if ((R(s, BiomeRegion.Tundra) || R(s, BiomeRegion.Boreal)) && s.slope < 25f && t.smTundraShrub.Length > 0)
                    { prob += 0.05f; pool = t.smTundraShrub; }
                    if (Barren(in s)) { prob *= 0.2f; pool = t.dryShrub; }   // kolo 19
                    else if (R(s, BiomeRegion.Burnt)) pool = t.dryShrub;
                    else if (Snowy(in s)) { prob *= 0.35f; if (t.smTundraShrub.Length > 0) pool = t.smTundraShrub; }   // kolo 20
                    break;
                default:
                {
                    if (s.region != 0) { RegionBush(t, ref rng, in s, gs, edge, out pool, out prob, out isLog); break; }
                    float u = rng.Next();
                    if (gs > 0.4f && u < 0.10f) { pool = rng.Next() < 0.5f ? t.log : t.stump; prob = 0.5f * gs; isLog = true; }
                    else
                    {
                        prob = 0.42f * edge + 0.035f;
                        bool dry = s.hum < 0.28f && s.temp > 0.55f;
                        // Mrtvý keř: v suchu častěji, jinak (louky, les) jen občas.
                        pool = rng.Next() < (dry ? 0.45f : 0.15f) ? t.deadBush
                             : s.hum > 0.45f && s.temp > 0.4f && rng.Next() < 0.35f ? t.flowerBush : t.bush;
                    }
                    break;
                }
            }
            if (rng.Next() >= prob) return RejB(in s, 0);
            if (s.slope > (isLog ? 12f : BushMaxSlope)) return RejB(in s, 1);
            if (RiverCoreAt(s.x, s.z) > 0.02f) return RejB(in s, 2);

            int si = rng.Pick(pool);
            if (si < 0) return RejB(in s, 3);
            PrefabShape sh = sp.GetShape(si);
            float sc = BaseScale(sp, si, ref rng) * rng.Range(0.85f, 1.1f);
            float rad = sh.valid ? sh.radius * sc : 1f;

            if (Blocked(s.x, s.z, rad, LBush)) return RejB(in s, 4);
            if (MaxOverhang(s.x, s.z, rad) > 0.05f) return RejB(in s, 5);

            Footprint(s.x, s.z, Mathf.Clamp(rad * 0.5f, 0.3f, 2f), true, out float fmin, out float fmax, out float fc);
            if (Mathf.Atan((fmax - fmin) / Mathf.Max(0.6f, rad)) * Mathf.Rad2Deg > (isLog ? 14f : BushMaxSlope)) return RejB(in s, 6);
            if (fmin - (s.surf - s.hW) < 1.2f) return RejB(in s, 7);
            if (MeshSlope(s.x, s.z) > (isLog ? 14f : BushMaxSlope) + 4f) return RejB(in s, 8);
            if (!CleanMesh(s.x, s.z, 0.35f)) return RejB(in s, 9);
            // Kolo 19: dlouhé padlé kmeny (ohořelé, zkamenělé) – oba konce musí ležet na zemi.
            float logR = isLog && rad > 3f ? rad : 0f;
            if (logR > 0f)
            {
                Footprint(s.x, s.z, logR * 0.8f, true, out float lmin, out float lmax, out _);
                if (lmax - lmin > Mathf.Max(0.6f, 0.15f * logR)) return RejB(in s, 10);
            }

            Quaternion yaw = Quaternion.AngleAxis(rng.Next() * 360f, Vector3.up);
            Quaternion rot = isLog
                ? Quaternion.FromToRotation(Vector3.up, Normal(s.x, s.z, Mathf.Max(1.5f, logR * 0.5f))) * yaw
                : yaw;

            // Keř je oblý: sedí mezi nejnižším bodem a středem, takže do svahu nezajede celou
            // výškou stopy a po spádnici se nevznáší.
            float y = isLog ? fmin - 0.05f : Mathf.Lerp(fmin, fc, 0.5f) - 0.1f;
            // Kolo 16: velkolistá rostlina je v kategorii trávy (audit: tolerance 0,3 m) – spodek
            // meshe se posadí na zem ve středu, jen na rovné stopě.
            if (pool == t.smBigLeaf)
            {
                if (fmax - fmin > 0.3f) return RejB(in s, 11);
                y = Ground(s.x, s.z, true) - 0.06f;   // pivot u země (audit: vznáší ≤ 0,12, zapadlé ≤ 0,30)
            }

            // Kolo 6: zapadnutí se měří na stopě 0,6·r (stejně jako audit) – nejvyšší bod
            // té stopy nad spodkem keře smí být nejvýš BushMaxBury. Dřív se hlídal jen sklon
            // na stopě 0,5·r, takže keř r = 2 m na svahu 20° zapadl až o 1 m.
            float auditR = Mathf.Clamp(Mathf.Max(0.5f, rad) * FootprintFactor, 0.2f, 2f);
            Footprint(s.x, s.z, auditR, true, out _, out float amax, out _);
            if (Rules6 && amax - y > BushMaxBury) return RejB(in s, 12);

            p.spawnable = si;
            p.position = new Vector3(s.x, y, s.z);
            p.rotation = rot;
            p.scale = new Vector3(sc, sc * (isLog ? 1f : 1.3f), sc);
            p.radius = Mathf.Max(0.5f, rad);
            return true;
        }

        private static void RegionBush(Table t, ref Rng rng, in Site s, float gs, float edge, out int[] pool, out float prob, out bool isLog)
        {
            isLog = false;
            float u = rng.Next();
            switch ((BiomeRegion)s.region)
            {
                case BiomeRegion.Boreal:
                    if (u < 0.24f) { pool = rng.Next() < 0.5f ? t.log : t.stump; prob = 0.35f * gs + 0.04f; isLog = true; }
                    else { pool = t.bush; prob = 0.18f * edge + 0.02f; }
                    break;
                case BiomeRegion.Birch:
                    if (gs > 0.3f && u < 0.12f) { pool = t.smWood.Length > 0 ? t.smWood : t.log; prob = 0.4f * gs; isLog = true; }
                    else { pool = u < 0.5f ? t.flowerBush : (t.smBush.Length > 0 ? t.smBush : t.bush); prob = 0.38f * edge + 0.04f; }
                    break;
                case BiomeRegion.Steppe:
                    prob = 0.045f;
                    pool = u < 0.35f ? t.dryShrub : u < 0.7f && t.smBush.Length > 0 ? t.smBush : t.bushLow;
                    break;
                case BiomeRegion.Desert:
                    prob = 0.03f; pool = u < 0.55f ? t.dryShrub : t.cactus; break;
                case BiomeRegion.Mesa:
                    prob = 0.022f; pool = u < 0.7f ? t.dryShrub : t.cactus; break;
                case BiomeRegion.Swamp:
                    prob = 0.09f; pool = u < 0.25f ? t.deadBush : u < 0.6f && t.smBush.Length > 0 ? t.smBush : t.bush; break;
                case BiomeRegion.Tundra:
                    prob = 0.07f; pool = u < 0.75f && t.smTundraShrub.Length > 0 ? t.smTundraShrub : t.deadBush; break;
                // ── kolo 16 ──
                case BiomeRegion.BlackForest:
                    // Podrost z nízkých tmavých smrků zahušťuje les; mýtiny (grove2) zůstávají volné.
                    if (u < 0.20f) { pool = rng.Next() < 0.5f ? t.log : t.stump; prob = 0.30f * gs + 0.04f; isLog = true; }
                    else if (u < 0.70f) { pool = t.smDarkFirLow; prob = 0.42f * Mathf.InverseLerp(0.36f, 0.46f, s.grove2); }
                    else { pool = u < 0.85f ? t.smTreeFern : t.bushLow; prob = 0.10f * gs + 0.03f; }
                    break;
                case BiomeRegion.Sequoia:
                    if (u < 0.30f) { pool = t.smWood.Length > 0 ? t.smWood : t.log; prob = 0.25f * gs + 0.03f; isLog = true; }
                    else { pool = u < 0.65f ? t.smTreeFern : t.bushLow; prob = 0.08f + 0.06f * gs; }
                    break;
                case BiomeRegion.Flowers:
                    prob = 0.05f + 0.25f * edge + 0.25f * Mathf.InverseLerp(0.55f, 0.70f, Noise(s.x, s.z, 75f, offGap));
                    pool = u < 0.75f ? t.flowerBush : (t.smBush.Length > 0 ? t.smBush : t.bush); break;
                case BiomeRegion.Heath:
                    prob = 0.38f + 0.30f * Mathf.InverseLerp(0.4f, 0.6f, Noise(s.x, s.z, 40f, offClump));
                    pool = u < 0.85f ? t.smHeather : t.bushLow; break;
                case BiomeRegion.Sakura:
                    prob = 0.06f + 0.18f * edge; pool = u < 0.5f ? t.flowerBush : (t.smBush.Length > 0 ? t.smBush : t.bush); break;
                case BiomeRegion.Bamboo:
                    prob = 0.10f; pool = u < 0.45f ? t.smBambooLow : u < 0.75f ? t.smBigLeaf : (t.smBush.Length > 0 ? t.smBush : t.bush); break;
                case BiomeRegion.Jungle:
                    prob = 0.24f + 0.10f * gs;
                    pool = u < 0.45f ? t.smBigLeaf : u < 0.75f ? t.smTreeFern : u < 0.88f ? (t.smBush.Length > 0 ? t.smBush : t.bush) : t.log;
                    isLog = pool == t.log; break;
                case BiomeRegion.FernGorge:
                    prob = 0.50f + 0.15f * gs; pool = u < 0.70f ? t.smTreeFern : u < 0.88f ? t.smBigLeaf : t.log;
                    isLog = pool == t.log; break;
                // ── kolo 19 ──
                case BiomeRegion.Volcanic:
                    prob = 0.012f; pool = t.dryShrub; break;
                case BiomeRegion.Burnt:
                    // ohořelé pařezy a padlé kmeny v hájích, na mýtinách první zelené keříky
                    if (u < 0.40f) { pool = t.smCharredWood; prob = 0.16f * gs + 0.03f; isLog = true; }
                    else { pool = u < 0.7f ? t.dryShrub : (t.smBush.Length > 0 ? t.smBush : t.bushLow); prob = 0.035f; }
                    break;
                case BiomeRegion.Karst:
                    prob = 0.10f + 0.16f * edge; pool = u < 0.45f ? (t.smBush.Length > 0 ? t.smBush : t.bush) : u < 0.7f ? t.flowerBush : u < 0.88f ? t.smBigLeaf : t.bushLow; break;
                case BiomeRegion.Petrified:
                    // silueta: vodorovné kamenné kmeny v ohniscích (šum 120 m), jinde suché keříky
                    if (u < 0.4f) { pool = t.smPetStump; prob = 0.04f + 0.12f * Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.48f, 0.64f, Noise(s.x, s.z, 120f, offClump))); isLog = true; }
                    else { prob = 0.025f; pool = u < 0.85f ? t.dryShrub : t.cactus; }
                    break;
                case BiomeRegion.Ruins:
                    // přerostlé: keře a velkolisté rostliny u zdí
                    prob = 0.09f + 0.14f * edge; pool = u < 0.4f ? (t.smBush.Length > 0 ? t.smBush : t.bush) : u < 0.7f ? t.smBigLeaf : u < 0.9f ? t.bushLow : t.log;
                    isLog = pool == t.log; break;
                case BiomeRegion.Oasis:
                    prob = 0.40f + 0.15f * edge; pool = u < 0.35f ? t.smPalmLow : u < 0.65f ? t.smPalmBush : u < 0.85f ? (t.smBush.Length > 0 ? t.smBush : t.bush) : t.flowerBush; break;
                // ── kolo 20 ──
                case BiomeRegion.SnowPeaks: case BiomeRegion.Glacier:
                    prob = 0.025f; pool = t.smTundraShrub.Length > 0 ? t.smTundraShrub : t.deadBush; break;
                case BiomeRegion.FrozenOcean: prob = 0f; pool = t.deadBush; break;
                case BiomeRegion.Mangrove:
                    prob = 0.30f + 0.30f * gs;
                    pool = u < 0.50f ? PickPool(t.smMangrove, t.bush) : u < 0.75f ? PickPool(t.smMangroveYoung, t.bush) : u < 0.90f ? PickPool(t.smMangroveRoot, t.bush) : t.smBigLeaf; break;
                case BiomeRegion.SaltFlat: prob = 0.006f; pool = t.dryShrub; break;
                case BiomeRegion.Geothermal: prob = 0.012f; pool = t.dryShrub; break;
                // ── kolo 21 ──
                case BiomeRegion.Crystal: prob = 0.03f; pool = t.smTundraShrub.Length > 0 ? t.smTundraShrub : t.deadBush; break;
                case BiomeRegion.Mushroom:
                    // shluky středních hub, stromové kapradiny a padlé kmeny; mýtiny zůstávají průchozí
                    prob = 0.20f + 0.16f * gs;
                    pool = u < 0.55f ? PickPool(t.smShroomCluster, t.bushLow) : u < 0.80f ? t.smTreeFern : u < 0.92f ? t.log : t.bushLow;
                    isLog = pool == t.log; break;
                case BiomeRegion.Cliffs: prob = 0.05f; pool = u < 0.6f ? t.bushLow : (t.smBush.Length > 0 ? t.smBush : t.bush); break;
                // ── kolo 28 ──
                case BiomeRegion.BasaltCoast: prob = 0.03f; pool = u < 0.6f ? (t.smTundraShrub.Length > 0 ? t.smTundraShrub : t.bushLow) : t.bushLow; break;
                case BiomeRegion.ObsidianPlain: prob = 0.004f; pool = t.deadBush; break;
                case BiomeRegion.AlabasterPlateau: prob = 0.016f; pool = u < 0.8f ? t.dryShrub : t.deadBush; break;
                // ── kolo 29 ──
                case BiomeRegion.MeteorCrater: prob = 0.004f; pool = t.deadBush; break;
                case BiomeRegion.FossilReef: prob = 0.02f; pool = u < 0.7f ? t.dryShrub : t.deadBush; break;
                case BiomeRegion.MudVolcanoes: prob = 0.004f; pool = t.deadBush; break;
                default:
                    prob = 0.035f; pool = t.bush; break;
            }
            if (pool == null || pool.Length == 0) { pool = t.bush; }
        }

        /// <summary>Kolo 20: pole druhu, nebo záloha, když druh v registru chybí.</summary>
        private static int[] PickPool(int[] a, int[] fallback) => a != null && a.Length > 0 ? a : fallback;

        private static bool TryStone(ObjectSpawner sp, Table t, ref Rng rng, in Site s, ref EcoPlacement p)
        {
            float prob;
            switch (s.biome)
            {
                case EcoBiome.Alpine: prob = 0.22f + 0.1f * Mathf.InverseLerp(15f, 35f, s.slope); break;
                case EcoBiome.Riparian: prob = s.hW > 0.3f && s.hW < 2.5f ? 0.10f : 0.02f; break;
                case EcoBiome.Coast: prob = s.surf - sea < 3.5f ? 0.015f : 0.04f; break;
                default: prob = 0.02f + 0.3f * Mathf.InverseLerp(0.58f, 0.70f, s.outcrop); break;
            }
            int[] stonePool = t.stone;
            float stoneShare = 0f;
            if (s.biome == EcoBiome.Meadow || s.biome == EcoBiome.Alpine)
            {
                switch ((BiomeRegion)s.region)
                {
                    case BiomeRegion.Desert: prob = prob * 1.3f + 0.03f; stonePool = t.smDesertStone; stoneShare = 0.85f; break;
                    case BiomeRegion.Mesa: prob = prob * 1.4f + 0.04f; stonePool = t.smMesaStone; stoneShare = 0.9f; break;
                    case BiomeRegion.Tundra: prob *= 1.5f; stonePool = t.smStone; stoneShare = 0.4f; break;
                    case BiomeRegion.Steppe: prob *= 1.2f; stonePool = t.smStone; stoneShare = 0.4f; break;
                    case BiomeRegion.Boreal: prob *= 1.1f; stonePool = t.smStone; stoneShare = 0.3f; break;
                    case BiomeRegion.Swamp: prob *= 0.5f; break;
                    // kolo 16
                    case BiomeRegion.Sakura: prob = prob * 1.2f + 0.02f; stonePool = t.smLightStone; stoneShare = 0.8f; break;
                    case BiomeRegion.Heath: prob = prob * 1.3f + 0.02f; stonePool = t.smStone; stoneShare = 0.5f; break;
                    case BiomeRegion.BlackForest: case BiomeRegion.Sequoia: stonePool = t.smStone; stoneShare = 0.4f; break;
                    case BiomeRegion.FernGorge: prob *= 1.2f; stonePool = t.smStone; stoneShare = 0.5f; break;
                    // ── kolo 19 ──
                    case BiomeRegion.Volcanic: prob = prob * 1.6f + 0.06f; stonePool = t.smLavaStone; stoneShare = 0.9f; break;
                    case BiomeRegion.Burnt: prob *= 0.8f; stonePool = t.smStone; stoneShare = 0.4f; break;
                    case BiomeRegion.Karst: prob = prob * 1.2f + 0.02f; stonePool = t.smLimeStone; stoneShare = 0.85f; break;
                    case BiomeRegion.Petrified: prob = prob * 1.1f + 0.025f; stonePool = t.smAgate; stoneShare = 0.35f; break;
                    case BiomeRegion.Ruins: prob = prob * 1.3f + 0.03f; stonePool = t.smRuinStone; stoneShare = 0.8f; break;
                    case BiomeRegion.Oasis: stonePool = t.smDesertStone; stoneShare = 0.6f; break;
                    // ── kolo 20 ──
                    case BiomeRegion.SnowPeaks: prob = prob * 1.4f + 0.05f; stonePool = t.smSnowScree; stoneShare = 0.85f; break;   // suť
                    case BiomeRegion.Glacier: prob = prob * 1.2f + 0.04f; stonePool = t.smIceStone; stoneShare = 0.8f; break;      // morény a kusy ledu
                    case BiomeRegion.FrozenOcean: prob = 0.08f; stonePool = t.smDrift; stoneShare = 0.85f; break;
                    case BiomeRegion.Mangrove: prob *= 0.4f; break;
                    case BiomeRegion.SaltFlat:
                        // solné krusty (ploché polygony) – hustě, ale v ohniscích (šum 60 m), mezi nimi holá pláň
                        prob = 0.04f + 0.20f * Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.42f, 0.58f, Noise(s.x, s.z, 60f, offFlower)));
                        stonePool = t.smSaltCrust; stoneShare = 0.95f; break;
                    case BiomeRegion.Geothermal:
                        prob = prob * 1.2f + 0.06f; stonePool = rngPick(t.smGeoCrust, t.smLavaStone, s); stoneShare = 0.85f; break;
                    // ── kolo 21 ──
                    case BiomeRegion.Crystal: prob = prob * 1.3f + 0.05f; stonePool = t.smCrystalSmall; stoneShare = 0.8f; break;
                    case BiomeRegion.Mushroom: stonePool = t.smStone; stoneShare = 0.4f; break;
                    case BiomeRegion.Cliffs: prob = prob * 1.2f + 0.02f; stonePool = CliffDark(in s) ? t.smLavaStone : t.smStone; stoneShare = 0.6f; break;
                    // ── kolo 28 ──
                    case BiomeRegion.BasaltCoast: prob = prob * 1.3f + 0.04f; stonePool = t.smLavaStone; stoneShare = 0.85f; break;
                    case BiomeRegion.ObsidianPlain: prob = prob * 1.5f + 0.06f; stonePool = PickPool(t.smObsStone, t.smLavaStone); stoneShare = 0.92f; break;
                    case BiomeRegion.AlabasterPlateau: prob = prob * 1.3f + 0.04f; stonePool = PickPool(t.smAlaStone, t.smLightStone); stoneShare = 0.85f; break;
                    // ── kolo 29 ──
                    case BiomeRegion.MeteorCrater: prob = prob * 1.6f + 0.07f; stonePool = PickPool(t.smTektStone, t.smLavaStone); stoneShare = 0.92f; break;   // tektity a černé sklo
                    case BiomeRegion.FossilReef: prob = prob * 1.3f + 0.05f; stonePool = PickPool(t.smCoralStone, t.smLightStone); stoneShare = 0.88f; break;   // úlomky korálů a schránek
                    case BiomeRegion.MudVolcanoes: prob = prob * 1.2f + 0.06f; stonePool = PickPool(t.smMudCrust, t.smGeoCrust); stoneShare = 0.9f; break;     // praskající krusty
                }
            }
            // Kolo 21: oblázky na pláži pod útesy (bílé u křídy, černé u čediče)
            if (s.biome == EcoBiome.Coast && R(s, BiomeRegion.Cliffs)) { prob = s.surf - sea > 0.4f ? 0.08f : 0f; stonePool = CliffDark(in s) ? t.smLavaStone : t.smStone; stoneShare = 0.8f; }
            // Kolo 28: černé čedičové oblázky na pláži
            if (s.biome == EcoBiome.Coast && R(s, BiomeRegion.BasaltCoast)) { prob = s.surf - sea > 0.4f ? 0.09f : 0f; stonePool = t.smLavaStone; stoneShare = 0.85f; }
            // Kolo 20: závěje na břehu zamrzlého moře
            if (s.biome == EcoBiome.Coast && R(s, BiomeRegion.FrozenOcean)) { prob = s.surf - sea > 0.6f ? 0.12f : 0f; stonePool = t.smDrift; stoneShare = 0.9f; }
            if (rng.Next() >= prob) return false;
            if (s.slope > StoneMaxSlope) return false;
            if (s.surf > s.snowLine + 60f) return false;

            int si = stoneShare > 0f && stonePool != null && stonePool.Length > 0 && rng.Next() < stoneShare
                ? rng.Pick(stonePool) : rng.Pick(t.stone);
            if (si < 0) return false;
            PrefabShape sh = sp.GetShape(si);
            float sc = BaseScale(sp, si, ref rng);
            if (s.biome == EcoBiome.Riparian) sc *= 0.7f;
            else if (s.biome == EcoBiome.Alpine) sc *= rng.Range(1.1f, 1.7f);
            float rad = sh.valid ? sh.radius * sc : 0.5f;
            float hgt = sh.valid ? sh.height * sc : 0.5f;

            // Kolo 19: kvádry ruin drží od ostatních pevných kusů aspoň průměr hráče – žádné škvíry, kde by uvízl.
            // Kolo 20: kvádr ruin drží od ostatních kusů aspoň průměr hráče i rohem (poloměr × 1,45 = úhlopříčka kvádru),
            // a protože vrstva kamenů nevidí do sousedního sloupce, drží i od jeho hrany polovinu té mezery
            // (s31415 a s1337 zůstaly škvíry 1,04–1,97 j. mezi kvádry/sloupem přes hranu sloupce; hráč má průměr 2,1).
            bool kvadr = InPool(t.smRuinStone, si);
            if (kvadr && BorderDistance(s.x, s.z) < rad * 1.45f + 1.3f) return false;
            if (Blocked(s.x, s.z, kvadr ? rad * 1.45f + 2.6f : rad, LStone)) return false;
            if (RiverCoreAt(s.x, s.z) > 0.02f && s.hW < 0.3f) return false;

            Footprint(s.x, s.z, Mathf.Clamp(rad * 0.7f, 0.2f, 2f), true, out float fmin, out float fmax, out float fc);
            if (fmax - fmin > 0.8f * hgt + 0.1f) return false;
            if (fc - fmin > 0.3f * hgt) return false;
            if (MeshSlope(s.x, s.z) > StoneMaxSlope) return false;
            if (fmin - (s.surf - s.hW) < 0.25f) return false;
            if (!CleanMesh(s.x, s.z, 1.0f)) return false;

            Vector3 n = Normal(s.x, s.z, Mathf.Max(0.75f, rad));
            Quaternion tilt = Quaternion.FromToRotation(Vector3.up, n)
                              * Quaternion.Euler(rng.Range(-12f, 12f), 0f, rng.Range(-12f, 12f));
            float minY = sh.valid ? sh.minY * sc : 0f;

            p.spawnable = si;
            p.position = new Vector3(s.x, Mathf.Min(fc - 0.25f * hgt, fmin) - minY, s.z);
            p.rotation = tilt * Quaternion.AngleAxis(rng.Next() * 360f, Vector3.up);
            p.scale = new Vector3(sc, sc * rng.Range(0.8f, 1.1f), sc);
            p.radius = Mathf.Max(0.25f, rad);
            return true;
        }

        /// <summary>Kolo 20: geotermální kameny – sirné krusty u pramenů, jinak tmavé lávové kameny (deterministicky podle místa).</summary>
        private static int[] rngPick(int[] crust, int[] lava, in Site s)
            => crust != null && crust.Length > 0 && (Noise(s.x, s.z, 45f, offClump) > 0.42f || lava == null || lava.Length == 0) ? crust : lava;

        /// <summary>
        /// Sebratelný kamínek (meziúkol). Drobný, leží na zemi, hlavně u vody a na kamenitých
        /// místech, v lese a na louce řídce. Nikdy pod vodou, pod sněhem ani ve svahu.
        /// </summary>
        private static bool TryPebble(ObjectSpawner sp, Table t, ref Rng rng, in Site s, ref EcoPlacement p)
        {
            float prob;
            switch (s.biome)
            {
                case EcoBiome.Riparian: prob = s.hW > 0.25f && s.hW < 3f ? 0.34f : 0.10f; break;
                case EcoBiome.Coast: prob = 0.14f; break;
                case EcoBiome.Alpine: prob = 0.16f; break;
                default: prob = 0.09f + 0.2f * Mathf.InverseLerp(0.5f, 0.7f, s.outcrop); break;
            }
            if (rng.Next() >= prob) return false;
            if (s.surf > s.snowLine) return false;
            if (s.slope > 30f) return false;
            if (s.hW < 0.2f) return false;

            int si = rng.Pick(t.pebble);
            if (si < 0) return false;
            PrefabShape sh = sp.GetShape(si);
            // Vlastní měřítko, ne scaleRange × globální násobek: ten dává z prefabu (mesh ~1 m)
            // balvany 1,3–2,8 m. Kamínek do ruky má mít zhruba 0,3–0,55 m.
            float sc = rng.Range(0.3f, 0.58f);
            float rad = sh.valid ? sh.radius * sc : 0.15f;
            float hgt = sh.valid ? sh.height * sc : 0.12f;

            if (Blocked(s.x, s.z, Mathf.Max(0.2f, rad), LPebble)) return false;
            if (RiverCoreAt(s.x, s.z) > 0.02f && s.hW < 0.4f) return false;
            if (!CleanMesh(s.x, s.z, 0.35f)) return false;
            if (MeshSlope(s.x, s.z) > 30f) return false;

            float g = Ground(s.x, s.z, true);
            Vector3 n = Normal(s.x, s.z, 0.75f);
            float minY = sh.valid ? sh.minY * sc : 0f;

            if (CaveLipAcross(s.x, s.z, g)) return false;   // kolo 12c; rng je pro každé místo vlastní
            p.spawnable = si;
            p.position = new Vector3(s.x, g - minY - 0.2f * hgt, s.z);
            p.rotation = Quaternion.FromToRotation(Vector3.up, n) * Quaternion.AngleAxis(rng.Next() * 360f, Vector3.up);
            p.scale = new Vector3(sc, sc * rng.Range(0.8f, 1.1f), sc);
            p.radius = Mathf.Max(0.15f, rad);
            return true;
        }

        private static bool TrySmall(ObjectSpawner sp, Table t, ref Rng rng, in Site s, ref EcoPlacement p)
        {
            if (s.surf > s.snowLine - 30f) return false;
            if (s.micro == (int)MicroBiome.BasaltField && s.microW > 0.35f) return false;
            float patch = Noise(s.x, s.z, 28f, offFlower);
            float gs = Mathf.InverseLerp(GroveLo, GroveHi, s.grove);
            int[] pool;
            float prob;
            switch (s.biome)
            {
                case EcoBiome.Meadow:
                    if (s.region != 0)
                    {
                        switch ((BiomeRegion)s.region)
                        {
                            case BiomeRegion.Birch:
                                if (gs > 0.4f) { pool = t.mushroom; prob = 0.08f * gs; }
                                else { pool = t.flower; prob = 0.45f * Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.60f, 0.72f, patch)); }
                                break;
                            case BiomeRegion.Boreal: pool = t.mushroom; prob = 0.06f * gs + 0.01f; break;
                            case BiomeRegion.Steppe: pool = t.flower; prob = 0.08f * Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.62f, 0.74f, patch)); break;
                            // kolo 16: nepravidelné květinové skvrny (dva šumy), mezi nimi skoro nic
                            case BiomeRegion.Flowers:
                            {
                                float big = Noise(s.x, s.z, 75f, offGap);
                                float m = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.44f, 0.60f, 0.6f * patch + 0.4f * big));
                                pool = t.flower; prob = 0.85f * m + 0.03f; break;
                            }
                            case BiomeRegion.Sakura: pool = t.flower; prob = 0.20f * Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.58f, 0.72f, patch)); break;
                            case BiomeRegion.Heath: pool = t.flower; prob = 0.04f * Mathf.InverseLerp(0.6f, 0.75f, patch); break;
                            case BiomeRegion.BlackForest: case BiomeRegion.Sequoia: pool = t.mushroom; prob = 0.07f * gs + 0.015f; break;
                            case BiomeRegion.FernGorge: case BiomeRegion.Jungle: pool = t.mushroom; prob = 0.03f * gs; break;
                            // kolo 19
                            case BiomeRegion.Burnt: pool = t.mushroom; prob = 0.02f * gs; break;
                            case BiomeRegion.Karst: case BiomeRegion.Oasis: pool = t.flower; prob = 0.14f * Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.58f, 0.72f, patch)); break;
                            case BiomeRegion.Ruins: pool = gs > 0.4f ? t.mushroom : t.flower; prob = 0.10f * Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.58f, 0.72f, patch)); break;
                            // kolo 21: drobné houby ve skupinách, mezi nimi svítící akcenty
                            case BiomeRegion.Mushroom:
                            {
                                float cl21 = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.45f, 0.62f, patch));
                                pool = rng.Next() < 0.45f ? PickPool(t.smGlowShroom, t.mushroom) : t.mushroom; prob = 0.10f + 0.35f * cl21; break;
                            }
                            // kolo 28: statická hnízda mořských ptáků na travnatém čedičovém břehu (ve skupinách, žádná AI)
                            case BiomeRegion.BasaltCoast:
                                if (t.smNest.Length == 0) return false;
                                pool = t.smNest; prob = 0.05f * Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.58f, 0.72f, patch)); break;
                            default: return false;
                        }
                        break;
                    }
                    if (gs > 0.5f) { pool = t.mushroom; prob = 0.07f * gs; }
                    else { pool = t.flower; prob = 0.55f * Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.60f, 0.72f, patch)); }
                    break;
                case EcoBiome.Riparian:
                    pool = t.flower; prob = s.hW > 1.5f ? 0.12f * Mathf.InverseLerp(0.55f, 0.7f, patch) : 0f; break;
                default: return false;
            }
            if (rng.Next() >= prob) return false;
            if (s.slope > 30f) return false;
            if (s.hW < 0.8f) return false;

            int si = rng.Pick(pool);
            if (si < 0) return false;
            float sc = BaseScale(sp, si, ref rng);
            if (Blocked(s.x, s.z, 0.3f, LSmall)) return false;
            if (!CleanMesh(s.x, s.z, 0.35f)) return false;
            // Kolo 6: drobné (květiny, houby) hlídají sklon meshe jako tráva (audit: květina na 60°).
            if (Rules6)
            {
                float ms = MeshSlope(s.x, s.z, out bool edge);
                if (ms > (edge ? GrassMaxSlope - 4f : GrassMaxSlope + 4f)) return false;
            }
            float g = Ground(s.x, s.z, true);

            if (CaveLipAcross(s.x, s.z, g)) return false;   // kolo 12c; rng je pro každé místo vlastní
            p.spawnable = si;
            p.position = new Vector3(s.x, g - 0.03f, s.z);
            p.rotation = Quaternion.AngleAxis(rng.Next() * 360f, Vector3.up);
            p.scale = new Vector3(sc, sc * 1.3f, sc);
            p.radius = 0.3f;
            return true;
        }

        private static bool TryGrass(ObjectSpawner sp, Table t, ref Rng rng, in Site s, ref EcoPlacement p)
        {
            if (s.micro == (int)MicroBiome.BasaltField && s.microW > 0.35f) return false;
            if (s.surf > s.snowLine - 12f) return false;
            if (s.slope > GrassMaxSlope) return false;

            // Písčitý pás u každé vody (paleta ho barví do 3,5 m nad hladinu) – tráva až nad ním.
            float sand = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(1.3f, 3.2f, s.hW));
            float gap = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.26f, 0.44f, Noise(s.x, s.z, 16f, offGap)));
            float gs = Mathf.InverseLerp(GroveLo, GroveHi, s.grove);

            bool reed = false;
            float dens;
            switch (s.biome)
            {
                case EcoBiome.Riparian:
                    if (s.hW >= 0.12f && s.hW <= (R(s, BiomeRegion.Swamp) ? 1.8f : 1.1f)
                        && s.dW <= (R(s, BiomeRegion.Swamp) ? 6f : 3.5f) && s.slope <= 18f)
                    {
                        float clump = Noise(s.x, s.z, 9f, offClump);
                        dens = 0.6f * Mathf.InverseLerp(0.42f, 0.62f, clump);
                        reed = true;
                    }
                    else dens = 0.82f * sand * (0.55f + 0.45f * gap);
                    // Kolo 14: v suchých regionech jen řídký pás u vody, ne souvislý koberec.
                    if (R(s, BiomeRegion.Desert) || R(s, BiomeRegion.Mesa) || Barren(in s)) dens *= reed ? 0.5f : (s.hW < 2.5f ? 0.15f : 0.035f);
                    else if (R(s, BiomeRegion.Steppe)) dens *= reed ? 1f : 0.6f * (1f - 0.85f * s.rw.desert);
                    else if (R(s, BiomeRegion.Oasis)) dens *= reed ? 1.25f : 1f;   // kolo 19: hustší rákos a zeleň u vody oázy
                    else if (Snowy(in s)) dens *= 0.1f;   // kolo 20: ledovcové jezero bez rákosí a zeleně
                    break;
                case EcoBiome.Coast:
                    dens = 0.28f * Mathf.InverseLerp(3.2f, 6f, s.surf - sea) * gap;
                    if (R(s, BiomeRegion.Desert) || R(s, BiomeRegion.Mesa) || Barren(in s)) dens *= 0.15f;
                    // Kolo 20: rákos v mělké laguně mangrov (jen na břehu nad hladinou, ne ve vodě), závěje bez trávy
                    if (R(s, BiomeRegion.Mangrove))
                    {
                        if (s.hW >= 0.12f && s.hW <= 1.4f && s.slope <= 16f)
                        { dens = 0.55f * Mathf.InverseLerp(0.38f, 0.58f, Noise(s.x, s.z, 9f, offClump)); reed = true; }
                        else dens = (0.10f + 0.12f * gap) * sand;   // bahnitý břeh – řídká tráva, ne louka
                    }
                    else if (Snowy(in s)) dens *= 0.03f;
                    break;
                case EcoBiome.Alpine:
                    dens = 0.32f * (1f - Mathf.InverseLerp(s.snowLine - 50f, s.snowLine - 12f, s.surf))
                                 * (1f - Mathf.InverseLerp(25f, 38f, s.slope)) * sand;
                    if (R(s, BiomeRegion.Tundra)) dens *= 0.6f; // kolo 14: řidší, nízký porost tundry
                    if (Barren(in s)) dens *= 0.08f; else if (R(s, BiomeRegion.Burnt)) dens *= 0.4f;   // kolo 19
                    else if (Snowy(in s)) dens *= 0.2f;   // kolo 20: sníh a led
                    break;
                default:
                    dens = (0.35f + 0.35f * gap) * sand * (gs > 0.4f ? 0.75f : 1f);
                    if (s.region != 0)
                    {
                        switch ((BiomeRegion)s.region)
                        {
                            case BiomeRegion.Boreal: dens = (0.22f + 0.25f * gap) * sand; break;
                            case BiomeRegion.Birch: dens = (0.38f + 0.32f * gap) * sand * (gs > 0.4f ? 0.8f : 1f); break;
                            case BiomeRegion.Steppe: dens = (0.36f + 0.30f * gap) * sand * (1f - 0.85f * s.rw.desert); break; // k poušti řídne
                            case BiomeRegion.Desert: dens = 0.03f * gap * sand; break;
                            case BiomeRegion.Mesa: dens = 0.07f * sand * (1f - Mathf.InverseLerp(10f, 25f, s.slope)); break;
                            case BiomeRegion.Swamp:
                                // Kolo 15: hustší rákosí u vody a rákosové ostrůvky v celé mokré nížině.
                                if (s.hW >= 0.12f && s.hW <= 2.5f && s.dW <= 14f && s.slope <= 14f)
                                { dens = 0.72f * Mathf.InverseLerp(0.34f, 0.56f, Noise(s.x, s.z, 9f, offClump)); reed = true; }
                                else if (s.hW >= 0.4f && s.slope <= 12f && Noise(s.x, s.z, 26f, offClump) > 0.52f)
                                { dens = 0.5f; reed = true; } // rákosové ostrůvky v mokré louce
                                else dens = (0.45f + 0.30f * gap) * sand;
                                break;
                            case BiomeRegion.Tundra: dens = (0.10f + 0.12f * gap) * sand; break;
                            // kolo 16
                            case BiomeRegion.BlackForest: dens = (0.20f + 0.28f * gap) * sand; break;
                            case BiomeRegion.Sequoia: dens = (0.16f + 0.20f * gap) * sand; break;   // nižší podrost
                            case BiomeRegion.Flowers: dens = (0.40f + 0.30f * gap) * sand; break;
                            case BiomeRegion.Heath: dens = (0.18f + 0.18f * gap) * sand; break;
                            case BiomeRegion.Sakura: dens = (0.36f + 0.30f * gap) * sand; break;
                            case BiomeRegion.Bamboo: dens = (0.30f + 0.25f * gap) * sand; break;
                            case BiomeRegion.Jungle: dens = (0.34f + 0.26f * gap) * sand; break;
                            case BiomeRegion.FernGorge: dens = (0.40f + 0.26f * gap) * sand; break;
                            // kolo 19
                            case BiomeRegion.Volcanic: dens = 0.02f * gap * sand; break;
                            case BiomeRegion.Burnt: dens = (0.06f + 0.30f * Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.50f, 0.66f, Noise(s.x, s.z, 24f, offClump)))) * sand; break;
                            case BiomeRegion.Karst: dens = (0.40f + 0.30f * gap) * sand; break;
                            case BiomeRegion.Petrified: dens = 0.04f * gap * sand; break;
                            case BiomeRegion.Ruins: dens = (0.32f + 0.30f * gap) * sand; break;
                            case BiomeRegion.Oasis: dens = (0.45f + 0.25f * gap) * sand; break;
                            // kolo 20
                            case BiomeRegion.SnowPeaks: dens = 0.03f * gap * sand; break;
                            case BiomeRegion.Glacier: dens = (0.04f + 0.08f * gap) * sand; break;
                            case BiomeRegion.FrozenOcean: dens = 0.01f * sand; break;
                            case BiomeRegion.Mangrove: dens = (0.12f + 0.14f * gap) * sand; break;
                            case BiomeRegion.SaltFlat: dens = 0.002f * sand; break;   // skoro holá pláň
                            case BiomeRegion.Geothermal: dens = 0.05f * gap * sand; break;
                            // kolo 21
                            case BiomeRegion.Crystal: dens = (0.06f + 0.10f * gap) * sand; break;
                            case BiomeRegion.Mushroom: dens = (0.26f + 0.24f * gap) * sand; break;
                            case BiomeRegion.Cliffs: dens = (0.30f + 0.26f * gap) * sand; break;
                            // kolo 28
                            case BiomeRegion.BasaltCoast: dens = (0.12f + 0.16f * gap) * sand; break;
                            case BiomeRegion.ObsidianPlain: dens = 0.004f * sand; break;   // skoro holá skelná pláň
                            case BiomeRegion.AlabasterPlateau: dens = 0.015f * gap * sand; break;
                            // kolo 29
                            case BiomeRegion.MeteorCrater: dens = 0.006f * gap * sand; break;   // spálená a sklovitá půda
                            case BiomeRegion.FossilReef: dens = (0.015f + 0.04f * gap) * sand; break;   // pilot 2: tráva přebíjela korály
                            case BiomeRegion.MudVolcanoes: dens = 0.01f * gap * sand; break;
                        }
                    }
                    break;
            }
            if (rng.Next() >= dens) return false;
            if (!reed && s.hW < 0.35f) return false;

            int si;
            float u = rng.Next();
            if (reed) si = R(s, BiomeRegion.Swamp) && u < 0.5f ? PickOr(ref rng, t.swampReed, t.reed) : rng.Pick(t.reed);
            else if (s.region != 0 && (s.biome == EcoBiome.Meadow
                     || (s.biome == EcoBiome.Riparian && (R(s, BiomeRegion.Desert) || R(s, BiomeRegion.Mesa) || R(s, BiomeRegion.Steppe)
                         || s.region >= (int)BiomeRegion.Volcanic))
                     || (s.biome == EcoBiome.Coast && s.region >= (int)BiomeRegion.SnowPeaks)))
                si = RegionGrass(t, ref rng, in s, gs, u);
            else if (s.biome == EcoBiome.Coast) si = u < 0.7f ? rng.Pick(t.grass2) : rng.Pick(t.grassLow);
            else if (s.biome == EcoBiome.Alpine) si = u < 0.75f ? rng.Pick(t.grassLow) : rng.Pick(t.grassMed);
            else if (s.biome == EcoBiome.Meadow && gs > 0.5f && s.hum > 0.35f && u < 0.35f) si = rng.Pick(t.fern);
            else
            {
                float lush = s.biome == EcoBiome.Riparian ? 0.2f : 0f;
                si = u < 0.22f + lush ? rng.Pick(t.grassHigh)
                   : u < 0.58f + lush * 0.5f ? rng.Pick(t.grassMed)
                   : u < 0.80f ? rng.Pick(t.grassLow)
                   : u < 0.92f ? rng.Pick(t.grass2)
                   : rng.Pick(t.clover);
            }
            if (si < 0) return false;

            if (Blocked(s.x, s.z, 0.3f, LGrass)) return false;
            if (!SurfaceRaster.Top(s.x, s.z, out float g, out int layers) || layers > 1) return false;
            if (Mathf.Abs(g - s.surf) > 0.5f) return false;
            // U hrany sloupce (část gradientu jednostranně) přísněji – viz MeshSlope.
            float ms = MeshSlope(s.x, s.z, out bool edge);
            if (ms > (edge ? GrassMaxSlope - 4f : GrassMaxSlope + 4f)) return false;
            float hW = g - (s.surf - s.hW);
            if (reed ? hW < 0.1f : hW < 0.35f) return false;

            float sc = BaseScale(sp, si, ref rng);
            Vector3 n = Normal(s.x, s.z, 0.75f);
            Quaternion tilt = reed ? Quaternion.identity
                                   : Quaternion.Slerp(Quaternion.identity, Quaternion.FromToRotation(Vector3.up, n), 0.4f);

            if (CaveLipAcross(s.x, s.z, g)) return false;   // kolo 12c; rng je pro každé místo vlastní
            p.spawnable = si;
            p.position = new Vector3(s.x, g - 0.04f, s.z);
            p.rotation = tilt * Quaternion.AngleAxis(rng.Next() * 360f, Vector3.up);
            p.scale = new Vector3(sc, sc * 1.3f, sc);
            p.radius = 0.3f;
            return true;
        }

        // ── kolo 20: kry na zamrzlém moři ──────────────────────────────
        private const int LFloe = 7;
        /// <summary>Kolo 20: vrstva ker (audit je kontroluje jako plovoucí, ne jako kámen na zemi).</summary>
        public const int LayerFloe = LFloe;
        /// <summary>Mřížka ker: buňka 48 j. (hráč ≈ 8,5 j.), posun středu ±12 % – sousední kry se nikdy nepřekryjí (pukliny mezi nimi).</summary>
        public const float FloeCell = 48f, FloeJitter = 0.12f;
        /// <summary>Kra jen nad mořem aspoň takhle hlubokým (dno pod hladinou, m) a nikdy nesedí na dně.</summary>
        public const float FloeMinDepth = 2.5f;
        /// <summary>A/B (<c>/props kry off</c>).</summary>
        public static bool Floes = true;
        /// <summary>Diagnostika: [0] kandidát, [1] jiný region, [2] mělko/dno, [3] koryto, [4] kanál/hustota, [5] osazeno.</summary>
        public static readonly int[] FloeStat = new int[6];

        /// <summary>
        /// Kolo 20: kry nad EXISTUJÍCÍM mořem zamrzlého oceánu. Hladina, vodní mesh, LOD ani kolize vody se nemění –
        /// kra je obyčejný pevný prop s plochým colliderem, který plave ve výšce hladiny moře. Každá buňka mřížky má
        /// nejvýš jednu kru menší než půl buňky, takže se kry nepřekrývají ani přes hranu sloupce (nezávisí na meshi
        /// ani na sousedovi) a mezi nimi zůstávají tmavé pukliny. Velké kanály otevřené vody dělá šum 300 j.
        /// </summary>
        private static void RunFloes(ObjectSpawner sp, Table t, List<EcoPlacement> output)
        {
            float cell = FloeCell;
            int ix0 = Mathf.FloorToInt(colX0 / cell) - 1, ix1 = Mathf.FloorToInt((colX0 + colSpan) / cell) + 1;
            int iz0 = Mathf.FloorToInt(colZ0 / cell) - 1, iz1 = Mathf.FloorToInt((colZ0 + colSpan) / cell) + 1;
            float maxR = 0.5f * cell * (1f - 2f * FloeJitter) / 1.12f - 0.25f;   // i natočená kra se vejde (1,12 = rezerva na roh obálky)
            for (int iz = iz0; iz <= iz1; iz++)
            for (int ix = ix0; ix <= ix1; ix++)
            {
                var rng = new Rng(Hash(ix, iz, 0x7F10, seed));
                float x = (ix + 0.5f + (rng.Next() - 0.5f) * 2f * FloeJitter) * cell;
                float z = (iz + 0.5f + (rng.Next() - 0.5f) * 2f * FloeJitter) * cell;
                if (!InColumn(x, z)) continue;
                FloeStat[0]++;
                if (FieldSurf(x, z) > sea - FloeMinDepth) { FloeStat[2]++; continue; }   // levné síto: jen hlubší moře
                Describe(x, z, rng.Next(), LFloe, out Site s);
                if (!R(s, BiomeRegion.FrozenOcean)) { FloeStat[1]++; continue; }
                if (s.riverish || RiverCoreAt(x, z) > 0.02f) { FloeStat[3]++; continue; }
                float lane = Noise(x, z, 300f, offGap);
                float prob = (0.90f * Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.30f, 0.42f, lane)) + 0.05f) * Mathf.Clamp01(s.rw.frozenOcean * 1.5f);
                if (rng.Next() >= prob) { FloeStat[4]++; continue; }
                int si = rng.Next() < 0.8f ? rng.Pick(t.smFloe) : PickOr(ref rng, t.smFloeSmall, t.smFloe);
                if (si < 0) continue;
                float y = sea + (rng.Next() - 0.5f) * 0.06f;
                float pick = rng.Next();
                // velká kra, a když by v mělčině/úzké zátoce sedla na dno, malá kra (deterministicky, stejný los)
                if (!FloeFits(sp, si, x, z, y, maxR, pick, out float sc, out float rad))
                {
                    si = t.smFloeSmall.Length > 0 ? t.smFloeSmall[0] : -1;
                    if (si < 0 || !FloeFits(sp, si, x, z, y, maxR, pick, out sc, out rad)) { FloeStat[2]++; continue; }
                }
                var p = new EcoPlacement
                {
                    spawnable = si,
                    position = new Vector3(x, y, z),
                    rotation = Quaternion.AngleAxis(rng.Next() * 360f, Vector3.up),
                    scale = new Vector3(sc, sc, sc),
                    radius = rad,
                    cellX = ix, cellZ = iz, layer = LFloe, biome = (byte)s.biome,
                };
                output.Add(p);
                FloeStat[5]++;
                if (!proxyMode) { RegionPlaced[s.region]++; RegionLayerPlaced[s.region * 8 + LFloe]++; }
            }
        }

        /// <summary>Kolo 20: měřítko kry a kontrola, že pod celou krou je dno aspoň 0,4 j. pod jejím spodkem (pole sloupce).</summary>
        private static bool FloeFits(ObjectSpawner sp, int si, float x, float z, float y, float maxR, float pick, out float sc, out float rad)
        {
            PrefabShape sh = sp.GetShape(si);
            SpawnableObject so = sp.GetSpawnable(si);
            Vector2 sr = so != null ? so.scaleRange : Vector2.one;
            sc = Mathf.Lerp(sr.x, sr.y, pick) * sp.GlobalScale;
            rad = sh.valid ? sh.radius * sc : 4f;
            if (rad > maxR) { sc *= maxR / rad; rad = maxR; }
            float minY = sh.valid ? sh.minY * sc : -0.4f;
            Footprint(x, z, rad * 0.85f, false, out _, out float fmax, out _);
            return fmax <= y + minY - 0.4f;
        }

        // ── kolo 28: čedičové sloupy v mělčině ─────────────────────────
        private const int LSeaCol = 8;
        /// <summary>Kolo 28: vrstva mořských čedičových sloupů (audit je kontroluje jako „stojí na dně, čnějí nad hladinu“).</summary>
        public const int LayerSeaColumn = LSeaCol;
        /// <summary>Mřížka sloupů v moři (buňka 40 j., posun ±15 %), jen v mělčině 1,5–7 j. pod hladinou.</summary>
        public const float SeaColCell = 40f, SeaColJitter = 0.15f, SeaColMinDepth = 1.5f, SeaColMaxDepth = 7f;
        /// <summary>Kolo 28: přepínač vrstvy (A/B v kódu/diagnostice).</summary>
        public static bool SeaColumns = true;
        /// <summary>Diagnostika: [0] kandidát, [1] hloubka, [2] jiný region, [3] koryto, [4] hustota, [5] dno/sklon, [6] osazeno.</summary>
        public static readonly int[] SeaColStat = new int[7];

        /// <summary>
        /// Kolo 28: šestiboké čedičové sloupy stojící na dně EXISTUJÍCÍ mělčiny čedičového pobřeží. Hladina, vodní mesh, pěna,
        /// LOD ani kolize vody se nemění – sloup je obyčejný pevný prop, pata zapuštěná do dna, vršek vysoko nad hladinou.
        /// Jedna buňka = nejvýš jeden shluk, takže se shluky nepřekrývají ani přes hranu sloupce terénu.
        /// </summary>
        private static void RunSeaColumns(ObjectSpawner sp, Table t, List<EcoPlacement> output)
        {
            float cell = SeaColCell;
            int ix0 = Mathf.FloorToInt(colX0 / cell) - 1, ix1 = Mathf.FloorToInt((colX0 + colSpan) / cell) + 1;
            int iz0 = Mathf.FloorToInt(colZ0 / cell) - 1, iz1 = Mathf.FloorToInt((colZ0 + colSpan) / cell) + 1;
            float maxR = 0.5f * cell * (1f - 2f * SeaColJitter) - 1f;
            for (int iz = iz0; iz <= iz1; iz++)
            for (int ix = ix0; ix <= ix1; ix++)
            {
                var rng = new Rng(Hash(ix, iz, 0x5C28, seed));
                float x = (ix + 0.5f + (rng.Next() - 0.5f) * 2f * SeaColJitter) * cell;
                float z = (iz + 0.5f + (rng.Next() - 0.5f) * 2f * SeaColJitter) * cell;
                if (!InColumn(x, z)) continue;
                SeaColStat[0]++;
                float bed = FieldSurf(x, z);
                if (bed > sea - SeaColMinDepth || bed < sea - SeaColMaxDepth) { SeaColStat[1]++; continue; }
                Describe(x, z, rng.Next(), LSeaCol, out Site s);
                if (!R(s, BiomeRegion.BasaltCoast)) { SeaColStat[2]++; continue; }
                if (s.riverish || RiverCoreAt(x, z) > 0.02f) { SeaColStat[3]++; continue; }
                float prob = (0.75f * Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.38f, 0.54f, Noise(x, z, 110f, offClump))) + 0.08f) * Mathf.Clamp01(s.rw.basalt * 1.5f);
                if (rng.Next() >= prob) { SeaColStat[4]++; continue; }
                int si = rng.Pick(t.smBasaltSea);
                if (si < 0) continue;
                PrefabShape sh = sp.GetShape(si);
                float sc = BaseScale(sp, si, ref rng);
                float rad = sh.valid ? sh.radius * sc : 3f;
                if (rad > maxR) { sc *= maxR / rad; rad = maxR; }
                float hgt = sh.valid ? sh.height * sc : 10f;
                Footprint(x, z, Mathf.Max(0.5f, rad * 0.7f), false, out float fmin, out float fmax, out _);
                float minY = sh.valid ? sh.minY * sc : 0f;
                // pata v nejnižším bodě dna – sklon dna pod stopou nejvýš 3 j. (jinak by sloup visel nad svahem), vršek ≥ 4 j. nad hladinou
                if (fmax - fmin > 3f || fmin - minY - 0.3f + hgt < sea + 4f) { SeaColStat[5]++; continue; }
                var p = new EcoPlacement
                {
                    spawnable = si,
                    position = new Vector3(x, fmin - minY - 0.3f, z),
                    rotation = Quaternion.AngleAxis(rng.Next() * 360f, Vector3.up),
                    scale = new Vector3(sc, sc, sc),
                    radius = rad,
                    cellX = ix, cellZ = iz, layer = LSeaCol, biome = (byte)s.biome,
                };
                output.Add(p);
                SeaColStat[6]++;
                if (!proxyMode) { RegionPlaced[s.region]++; RegionLayerPlaced[s.region * 8 + LRock]++; }
            }
        }

        // ── kolo 28: formace na světových mřížkách ───────────────────
        /// <summary>A/B přepínač formací kola 28 (false = chování první dávky: formace ve vrstvě balvanů).</summary>
        public static bool Formations28 = true;
        /// <summary>Velké formace: buňka 72 j., posun ±15 % → středy ≥ 50 j. od sebe; poloměr ≤ 24 j. ⇒ mezera mezi kusy ≥ 2,2 j. (průměr hráče 2,1).</summary>
        public const float FormBigCell = 72f, FormSmallCell = 34f, FormJitter = 0.15f;
        /// <summary>Diagnostika: [0] kandidát, [1] region/souš, [2] hustota, [3] terén, [4] osazeno, [5] rezervace z okraje souseda.</summary>
        public static readonly int[] FormStat = new int[6];
        private const int LForm = 9;

        /// <summary>
        /// Kolo 28: formace čediče, obsidiánu a alabastru. Každá buňka světové mřížky má nejvýš jednu formaci, poloměr je omezený
        /// tak, že mezi formacemi zůstane vždy ≥ 2,2 j. – nezávisle na sloupci, sousedovi i pořadí stavby. Kandidáty z okraje
        /// sousedních sloupců (do 64 j. / 24 j.) jen rezervuje: o jejich přijetí rozhodují čistě pole terénu a šum, takže soused
        /// vidí nadmnožinu skutečně osazených formací a jeho stromy, balvany a tráva se jim vyhnou. Velké formace jdou první,
        /// malé (dlažba, balvany, desky) se vyhnou velkým přes stejné rezervace.
        /// </summary>
        private static void RunFormations(ObjectSpawner sp, Table t, List<EcoPlacement> output, bool big)
        {
            float cell = big ? FormBigCell : FormSmallCell, margin = big ? 64f : 24f;
            float maxR = 0.5f * (cell * (1f - 2f * FormJitter) - 2.2f);
            int ix0 = Mathf.FloorToInt((colX0 - margin) / cell) - 1, ix1 = Mathf.FloorToInt((colX0 + colSpan + margin) / cell) + 1;
            int iz0 = Mathf.FloorToInt((colZ0 - margin) / cell) - 1, iz1 = Mathf.FloorToInt((colZ0 + colSpan + margin) / cell) + 1;
            for (int iz = iz0; iz <= iz1; iz++)
            for (int ix = ix0; ix <= ix1; ix++)
            {
                var rng = new Rng(Hash(ix, iz, big ? 0x2F28 : 0x3F28, seed));
                float x = (ix + 0.5f + (rng.Next() - 0.5f) * 2f * FormJitter) * cell;
                float z = (iz + 0.5f + (rng.Next() - 0.5f) * 2f * FormJitter) * cell;
                bool inside = InColumn(x, z);
                if (!inside && (x < colX0 - margin || z < colZ0 - margin || x >= colX0 + colSpan + margin || z >= colZ0 + colSpan + margin)) continue;
                if (!inside)
                {
                    // Okraj souseda: pole sloupce sem nesahá (pad je jen pár voxelů), proto analytický region bez hydrologie a
                    // NEJVĚTŠÍ možný poloměr – rezervuje se nadmnožina toho, co soused může osadit (audit: škvíry 0,61/1,88 j.).
                    float dm = rng.Next();
                    var vt = VoxelTerrain.instance;
                    if (vt == null) continue;
                    RegionWeights wm = vt.RegionAt(x, z, false, out float sfm, out _);
                    if (wm.basalt + wm.obsidian + wm.alabaster + wm.meteor + wm.coral + wm.mud < 0.05f || sfm < sea + 0.5f) continue;   // kolo 29: i nové regiony
                    if (big && wm.mud > 0.5f && wm.basalt + wm.obsidian + wm.alabaster + wm.meteor + wm.coral < 0.05f) continue;   // kolo 29: velká mřížka v bahenních sopkách nic nestaví
                    float clm = Noise(x, z, wm.basalt >= wm.obsidian && wm.basalt >= wm.alabaster ? 130f : wm.obsidian >= wm.alabaster ? 140f : 160f, offClump);
                    float prm = big ? 0.22f + 0.58f * Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.40f, 0.56f, clm))
                                    : 0.30f + 0.40f * Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.36f, 0.54f, clm));
                    if (rng.Next() >= Mathf.Max(prm, big ? 0.80f : 0.70f)) continue;   // horní mez – hustotní šum se může lišit podle regionu
                    reserved.Add(new Vector4(x, z, maxR, LRock)); FormStat[5]++;
                    continue;
                }
                if (!InField(x, z)) continue;
                FormStat[0]++;
                float dither = rng.Next();
                Describe(x, z, dither, LRock, out Site s);
                bool bas = R(s, BiomeRegion.BasaltCoast), obs = R(s, BiomeRegion.ObsidianPlain), ala = R(s, BiomeRegion.AlabasterPlateau);
                bool met = R(s, BiomeRegion.MeteorCrater), cor = R(s, BiomeRegion.FossilReef), mdv = R(s, BiomeRegion.MudVolcanoes);   // kolo 29
                bool k29 = met || cor || mdv;
                if ((!bas && !obs && !ala && !k29) || s.riverish || s.surf - sea < 1.2f || s.hW < 1.2f || s.surf > s.snowLine) { FormStat[1]++; continue; }
                float clump, pick = Noise(x, z, 70f, offGap), prob;
                int[] pool;
                if (k29)
                {
                    // Kolo 29 (pravděpodobnosti ≤ 0,8 / 0,7 – soused rezervuje nadmnožinu, viz okraj výše).
                    if (met)
                    {
                        // podle polohy v kráteru: dno = rudy a tektitové sklo, stěny a val = vyvržené balvany, spálený prstenec = řídké balvany
                        float cx = CraterMath.X(new float2(x, z), craterCol);
                        if (big)
                        {
                            prob = cx < 0.5f ? 0.55f : cx < 1.15f ? 0.5f : cx < 1.9f ? 0.25f : 0f;
                            pool = cx < 0.5f ? PickPool(t.smOre, t.smTektGlass) : t.smCraterRock;
                        }
                        else
                        {
                            prob = cx < 0.95f ? 0.6f : cx < 1.6f ? 0.4f : 0.12f;
                            // výběr podle šumu, ne losu: los před kontrolou hustoty by posunul sekvenci proti rezervaci souseda (nadmnožina)
                            pool = cx < 0.95f ? (pick < 0.5f ? t.smTektGlass : PickPool(t.smOre, t.smTektGlass)) : cx < 1.6f ? PickPool(t.smCraterRock, t.smTektGlass) : t.smCraterRock;
                        }
                    }
                    else if (cor)
                    {
                        clump = Noise(x, z, 150f, offClump);
                        prob = big ? 0.40f + 0.38f * Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.40f, 0.56f, clump))
                                   : 0.42f + 0.26f * Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.36f, 0.54f, clump));   // pilot: útes musí být hustý
                        pool = big ? (pick > 0.60f ? PickPool(t.smCoralGate, t.smCoralBig) : t.smCoralBig) : t.smCoralSmall;
                    }
                    else
                    {
                        // bahenní sopky: velké kužely stojí na výduchech (RunMudVents), formace jen sírové krystaly na malé mřížce
                        prob = big ? 0f : 0.4f;
                        pool = t.smSulfur;
                    }
                    if (rng.Next() >= prob) { FormStat[2]++; continue; }
                }
                else
                {
                clump = Noise(x, z, bas ? 130f : obs ? 140f : 160f, offClump);
                prob = big ? 0.22f + 0.58f * Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.40f, 0.56f, clump))
                                 : 0.30f + 0.40f * Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.36f, 0.54f, clump));
                if (rng.Next() >= prob) { FormStat[2]++; continue; }
                pool = big
                    ? (bas ? (pick > 0.55f ? PickPool(t.smBasaltStep, t.smBasaltCol) : rng.Next() < 0.75f ? t.smBasaltCol : PickPool(t.smBasalt, t.smBasaltCol))
                       : obs ? t.smObsSpire
                       : (pick > 0.62f ? PickPool(t.smAlaArch, t.smAlaTower) : t.smAlaTower))
                    : (bas ? t.smBasaltPave : obs ? PickPool(t.smObsRock, t.smLavaRock) : (rng.Next() < 0.5f ? PickPool(t.smAlaRock, t.rockLarge) : PickPool(t.smAlaTower, t.smAlaRock)));
                }
                if (pool == null || pool.Length == 0) continue;
                int si = rng.Pick(pool);
                if (si < 0) continue;
                if (!big && InPool(t.smAlaTower, si)) si = t.smAlaTower.Length > 1 ? t.smAlaTower[1] : si;   // malá mřížka: jen stolová deska (věž je velká)
                PrefabShape sh = sp.GetShape(si);
                float sc = BaseScale(sp, si, ref rng);
                // poloosa obálky je pro nenatočený kus – natočený roh sahá dál (audit: škvíry 0,61/1,73 j.), proto rezerva ×1,25
                float rad = sh.valid ? sh.radius * sc : 4f;
                if (rad * 1.25f > maxR) { sc *= maxR / (rad * 1.25f); rad = maxR / 1.25f; }
                float rr = rad * 1.25f;
                float hgt = sh.valid ? sh.height * sc : 4f;
                float yaw = rng.Next() * 360f;
                // malá formace drží od velkých (rezervace vč. okraje sousedů) ≥ průměr hráče; pak rezervace jako u souseda
                if ((!big || k29) && Blocked(x, z, rr + 2.3f, LRock)) { FormStat[3]++; continue; }   // kolo 29: i velké se vyhnou kuželům a jádru meteoritu
                reserved.Add(new Vector4(x, z, rr, LRock));
                bool stair = InPool(t.smBasaltStep, si) || InPool(t.smBasaltPave, si);
                bool arch = InPool(t.smAlaArch, si) || InPool(t.smCoralGate, si);   // kolo 29: korálová brána a kostra jako oblouk (rovná stopa)
                Footprint(x, z, Mathf.Max(0.5f, rad * 0.7f), true, out float fmin, out float fmax, out float fc);
                bool okT = s.slope <= (stair ? 30f : arch ? 16f : 34f)
                        && fmax - fmin <= (stair ? 0.8f : arch ? 0.15f : 0.9f) * hgt
                        && (stair || fc - fmin <= 0.45f * hgt)
                        && fmin - (s.surf - s.hW) >= 0.6f
                        && RiverCoreAt(x, z) <= 0.02f
                        && MaxOverhang(x, z, rad * 0.7f) <= 0.6f
                        && CleanMesh(x, z, 2.0f)
                        && MeshSlope(x, z) <= (arch ? 22f : 50f);
                if (!okT) { FormStat[3]++; continue; }   // rezervace zůstává (soused ji má taky) – jen prázdné místo
                float minY = sh.valid ? sh.minY * sc : 0f;
                var p = new EcoPlacement
                {
                    spawnable = si,
                    position = new Vector3(x, Mathf.Min(fc - (arch ? 0.04f : 0.18f) * hgt, fmin) - minY, z),
                    rotation = Quaternion.AngleAxis(yaw, Vector3.up),
                    scale = new Vector3(sc, sc, sc),
                    radius = rr,
                    cellX = ix, cellZ = iz, layer = big ? LForm : LForm + 1, biome = (byte)s.biome,
                };
                output.Add(p);
                FormStat[4]++;
                RegionPlaced[s.region]++; RegionLayerPlaced[s.region * 8 + LRock]++;
            }
        }

        // ── kolo 29: výduchy bahenních sopek a jádra meteoritů ─────────────
        /// <summary>Diagnostika kola 29: [0] výduch v poli, [1] region/souš, [2] terén, [3] rezervace z okraje souseda, [4] kuželů osazeno,
        /// [5] jader v poli, [6] jader osazeno, [7] jader odmítnuto terénem.</summary>
        public static readonly int[] K29Stat = new int[8];
        private const int LVent = 11, LCore = 12;
        private const float VentBigR = 9f, VentSmallR = 6f, CoreR = 8f;

        /// <summary>
        /// Kolo 29: kužel na každém výduchu bahenních sopek (BiomeMath.MudVentInCell – tytéž body mají v barvě terénu sírový
        /// prstenec). Výduchy jsou analytické, takže okraj souseda se rezervuje bez pole sloupce (nadmnožina, jako formace).
        /// Mimo region bahenních sopek se nic nestaví ani nerezervuje.
        /// </summary>
        private static void RunMudVents(ObjectSpawner sp, Table t, List<EcoPlacement> output)
        {
            float cell = BiomeMath.MudVentCell, margin = 24f;
            int ix0 = Mathf.FloorToInt((colX0 - margin) / cell) - 1, ix1 = Mathf.FloorToInt((colX0 + colSpan + margin) / cell) + 1;
            int iz0 = Mathf.FloorToInt((colZ0 - margin) / cell) - 1, iz1 = Mathf.FloorToInt((colZ0 + colSpan + margin) / cell) + 1;
            for (int iz = iz0; iz <= iz1; iz++)
            for (int ix = ix0; ix <= ix1; ix++)
            {
                if (!BiomeMath.MudVentInCell(new int2(ix, iz), offRegion, out float2 c, out float size)) continue;
                float x = c.x, z = c.y;
                bool big = size > 1.08f;
                float rMax = big ? VentBigR : VentSmallR;
                bool inside = InColumn(x, z);
                if (!inside && (x < colX0 - margin || z < colZ0 - margin || x >= colX0 + colSpan + margin || z >= colZ0 + colSpan + margin)) continue;
                if (!inside)
                {
                    var vt = VoxelTerrain.instance;
                    if (vt == null) continue;
                    RegionWeights wm = vt.RegionAt(x, z, false, out float sfm, out _);
                    if (wm.mud < 0.3f || sfm < sea + 0.5f) continue;
                    reserved.Add(new Vector4(x, z, rMax, LRock)); K29Stat[3]++;
                    continue;
                }
                if (!InField(x, z)) continue;
                K29Stat[0]++;
                var rng = new Rng(Hash(ix, iz, 0x4D29, seed));
                Describe(x, z, rng.Next(), LRock, out Site s);
                if (!R(s, BiomeRegion.MudVolcanoes) || s.riverish || s.surf - sea < 1.2f || s.hW < 1.2f) { K29Stat[1]++; continue; }
                int[] pool = big ? PickPool(t.smMudConeBig, t.smMudCone) : t.smMudCone;
                if (pool == null || pool.Length == 0) continue;
                int si = rng.Pick(pool);
                if (si < 0) continue;
                PrefabShape sh = sp.GetShape(si);
                float sc = BaseScale(sp, si, ref rng);
                float rad = sh.valid ? sh.radius * sc : 4f;
                if (rad * 1.25f > rMax) { sc *= rMax / (rad * 1.25f); rad = rMax / 1.25f; }
                float rr = rad * 1.25f, hgt = sh.valid ? sh.height * sc : 4f;
                reserved.Add(new Vector4(x, z, rr, LRock));
                Footprint(x, z, Mathf.Max(0.5f, rad * 0.7f), true, out float fmin, out float fmax, out float fc);
                bool okT = s.slope <= 16f && fmax - fmin <= 0.5f * hgt && fmin - (s.surf - s.hW) >= 0.6f && RiverCoreAt(x, z) <= 0.02f
                        && MaxOverhang(x, z, rad * 0.7f) <= 0.6f && CleanMesh(x, z, 2.0f) && MeshSlope(x, z) <= 30f;
                if (!okT) { K29Stat[2]++; continue; }
                float minY = sh.valid ? sh.minY * sc : 0f;
                output.Add(new EcoPlacement
                {
                    spawnable = si, position = new Vector3(x, Mathf.Min(fc - 0.12f * hgt, fmin) - minY, z),
                    rotation = Quaternion.AngleAxis(rng.Next() * 360f, Vector3.up), scale = new Vector3(sc, sc, sc), radius = rr,
                    cellX = ix, cellZ = iz, layer = LVent, biome = (byte)s.biome,
                });
                K29Stat[4]++;
                RegionPlaced[s.region]++; RegionLayerPlaced[s.region * 8 + LRock]++;
            }
        }

        /// <summary>Kolo 29: velké tmavé jádro meteoritu ve středu kráteru (jediný kus na kráter, analytický střed CraterMath).</summary>
        private static void RunCraterCores(ObjectSpawner sp, Table t, List<EcoPlacement> output)
        {
            if (t.smMeteorCore.Length == 0 || craterCol.valid == 0 || craterCol.gate < 0.3f) return;
            float margin = 24f;
            float x = craterCol.center.x, z = craterCol.center.y;
            int ix = Mathf.FloorToInt(x / CraterMath.Cell), iz = Mathf.FloorToInt(z / CraterMath.Cell);
            bool inside = InColumn(x, z);
            if (!inside)
            {
                // okraj souseda: jádro tam stojí (stejný kráter, stejná brána) – rezervace, ať se mu formace vyhnou
                if (x >= colX0 - margin && z >= colZ0 - margin && x < colX0 + colSpan + margin && z < colZ0 + colSpan + margin)
                    reserved.Add(new Vector4(x, z, CoreR, LRock));
                return;
            }
            if (!InField(x, z)) return;
            K29Stat[5]++;
            var rng = new Rng(Hash(ix, iz, 0x6D29, seed));
            Describe(x, z, rng.Next(), LRock, out Site s);
            if (!R(s, BiomeRegion.MeteorCrater) || s.riverish || s.surf - sea < 1.2f || s.hW < 1.2f) { K29Stat[7]++; return; }
            int si = t.smMeteorCore[0];
            PrefabShape sh = sp.GetShape(si);
            float sc = BaseScale(sp, si, ref rng);
            float rad = sh.valid ? sh.radius * sc : 4f;
            if (rad * 1.25f > CoreR) { sc *= CoreR / (rad * 1.25f); rad = CoreR / 1.25f; }
            float rr = rad * 1.25f, hgt = sh.valid ? sh.height * sc : 4f;
            reserved.Add(new Vector4(x, z, rr, LRock));
            Footprint(x, z, Mathf.Max(0.5f, rad * 0.7f), true, out float fmin, out float fmax, out float fc);
            // sklon skutečného meshe (s.slope je sklon původního makra – dno vyrovnaného kráteru je ploché i na mírném svahu; finál s42/s31415: jádro chybělo)
            if (MeshSlope(x, z) > 20f || fmax - fmin > 0.6f * hgt || RiverCoreAt(x, z) > 0.02f || !CleanMesh(x, z, 2.0f)) { K29Stat[7]++; return; }
            float minY = sh.valid ? sh.minY * sc : 0f;
            output.Add(new EcoPlacement
            {
                spawnable = si, position = new Vector3(x, Mathf.Min(fc - 0.25f * hgt, fmin) - minY, z),
                rotation = Quaternion.AngleAxis(rng.Next() * 360f, Vector3.up), scale = new Vector3(sc, sc, sc), radius = rr,
                cellX = ix, cellZ = iz, layer = LCore, biome = (byte)s.biome,
            });
            K29Stat[6]++;
            RegionPlaced[s.region]++; RegionLayerPlaced[s.region * 8 + LRock]++;
        }

        private static int RegionGrass(Table t, ref Rng rng, in Site s, float gs, float u)
        {
            switch ((BiomeRegion)s.region)
            {
                case BiomeRegion.Boreal:
                    return u < 0.42f ? PickOr(ref rng, t.fernAll, t.fern) : u < 0.75f ? rng.Pick(t.grassLow) : rng.Pick(t.grassMed);
                case BiomeRegion.Birch:
                    return u < 0.20f ? rng.Pick(t.grassHigh) : u < 0.45f ? rng.Pick(t.grassMed)
                         : u < 0.60f ? PickOr(ref rng, t.smGrass, t.grassMed) : u < 0.75f ? rng.Pick(t.grassLow)
                         : u < 0.85f && gs > 0.4f ? PickOr(ref rng, t.fernAll, t.fern) : rng.Pick(t.clover);
                case BiomeRegion.Steppe:
                    return u < 0.45f ? PickOr(ref rng, t.smGrassDry, t.grass2) : u < 0.70f ? rng.Pick(t.grass2)
                         : u < 0.85f ? rng.Pick(t.grassLow) : rng.Pick(t.grassMed);
                case BiomeRegion.Desert:
                case BiomeRegion.Mesa:
                    return PickOr(ref rng, t.smGrassDry, t.grass2);
                case BiomeRegion.Swamp:
                    return u < 0.45f ? rng.Pick(t.grassHigh) : u < 0.70f ? PickOr(ref rng, t.smGrass, t.grassHigh)
                         : u < 0.85f ? rng.Pick(t.grassMed) : PickOr(ref rng, t.fernAll, t.fern);
                case BiomeRegion.Tundra:
                    return u < 0.72f ? rng.Pick(t.grassLow) : u < 0.90f ? rng.Pick(t.clover) : PickOr(ref rng, t.smGrassDry, t.grass2);
                // ── kolo 16 ──
                case BiomeRegion.BlackForest:
                case BiomeRegion.Sequoia:
                    return u < 0.55f ? PickOr(ref rng, t.fernAll, t.fern) : u < 0.85f ? rng.Pick(t.grassLow) : rng.Pick(t.clover);
                case BiomeRegion.Flowers:
                    return u < 0.20f ? rng.Pick(t.grassHigh) : u < 0.45f ? rng.Pick(t.grassMed)
                         : u < 0.60f ? PickOr(ref rng, t.smGrass, t.grassMed) : u < 0.80f ? rng.Pick(t.clover) : rng.Pick(t.grassLow);
                case BiomeRegion.Heath:
                    return u < 0.55f ? rng.Pick(t.grassLow) : u < 0.80f ? PickOr(ref rng, t.smGrassDry, t.grass2) : rng.Pick(t.grass2);
                case BiomeRegion.Sakura:
                    return u < 0.35f ? rng.Pick(t.grassMed) : u < 0.60f ? rng.Pick(t.clover) : u < 0.80f ? PickOr(ref rng, t.smGrass, t.grassMed) : rng.Pick(t.grassLow);
                case BiomeRegion.Bamboo:
                    return u < 0.40f ? rng.Pick(t.grassHigh) : u < 0.70f ? rng.Pick(t.grassMed) : PickOr(ref rng, t.fernAll, t.fern);
                case BiomeRegion.Jungle:
                    return u < 0.50f ? PickOr(ref rng, t.fernAll, t.fern) : rng.Pick(t.grassHigh);
                case BiomeRegion.FernGorge:
                    return u < 0.55f ? PickOr(ref rng, t.fernAll, t.fern) : u < 0.80f ? PickOr(ref rng, t.smHorsetail, t.grassHigh) : rng.Pick(t.grassMed);
                // ── kolo 19 ──
                case BiomeRegion.Volcanic:
                case BiomeRegion.Petrified:
                    return PickOr(ref rng, t.smGrassDry, t.grass2);
                case BiomeRegion.Burnt:
                    return u < 0.45f ? rng.Pick(t.grassLow) : u < 0.70f ? PickOr(ref rng, t.smGrass, t.grassMed) : PickOr(ref rng, t.smGrassDry, t.grass2);
                case BiomeRegion.Karst:
                    return u < 0.30f ? rng.Pick(t.grassMed) : u < 0.55f ? rng.Pick(t.grassHigh) : u < 0.75f ? PickOr(ref rng, t.smGrass, t.grassMed) : u < 0.90f ? PickOr(ref rng, t.fernAll, t.fern) : rng.Pick(t.clover);
                case BiomeRegion.Ruins:
                    return u < 0.30f ? rng.Pick(t.grassHigh) : u < 0.55f ? PickOr(ref rng, t.smGrass, t.grassMed) : u < 0.75f ? PickOr(ref rng, t.smGrassDry, t.grass2) : PickOr(ref rng, t.fernAll, t.fern);
                case BiomeRegion.Oasis:
                    return u < 0.35f ? rng.Pick(t.grassHigh) : u < 0.65f ? rng.Pick(t.grassMed) : u < 0.85f ? PickOr(ref rng, t.smGrass, t.grassMed) : PickOr(ref rng, t.fernAll, t.fern);
                // ── kolo 20 ──
                case BiomeRegion.SnowPeaks: case BiomeRegion.Glacier: case BiomeRegion.FrozenOcean:
                    return u < 0.70f ? rng.Pick(t.grassLow) : PickOr(ref rng, t.smGrassDry, t.grass2);
                case BiomeRegion.Mangrove:
                    return u < 0.40f ? rng.Pick(t.grassHigh) : u < 0.70f ? PickOr(ref rng, t.fernAll, t.fern) : rng.Pick(t.grassMed);
                case BiomeRegion.SaltFlat: case BiomeRegion.Geothermal:
                    return PickOr(ref rng, t.smGrassDry, t.grass2);
                // ── kolo 21 ──
                case BiomeRegion.Crystal:
                    return u < 0.6f ? rng.Pick(t.grassLow) : PickOr(ref rng, t.smGrassDry, t.grass2);
                case BiomeRegion.Mushroom:
                    return u < 0.45f ? PickOr(ref rng, t.fernAll, t.fern) : u < 0.75f ? rng.Pick(t.grassMed) : rng.Pick(t.clover);
                case BiomeRegion.Cliffs:
                    return u < 0.45f ? rng.Pick(t.grass2) : u < 0.75f ? rng.Pick(t.grassLow) : PickOr(ref rng, t.smGrass, t.grassMed);
                // ── kolo 28 ──
                case BiomeRegion.BasaltCoast:
                    return u < 0.6f ? rng.Pick(t.grassLow) : rng.Pick(t.grass2);
                case BiomeRegion.ObsidianPlain: case BiomeRegion.AlabasterPlateau:
                    return PickOr(ref rng, t.smGrassDry, t.grass2);
                // ── kolo 29 ──
                case BiomeRegion.MeteorCrater: case BiomeRegion.FossilReef: case BiomeRegion.MudVolcanoes:
                    return PickOr(ref rng, t.smGrassDry, t.grass2);
                default:
                    return rng.Pick(t.grassMed);
            }
        }
    }

    /// <summary>
    /// Přesná výška povrchu z LOD0 meshů jednoho sloupce: trojúhelníky roztříděné do přihrádek
    /// po 1 m, dotaz barycentricky. Staví se jednou na sloupec do sdílených polí – bez alokací
    /// po zahřátí.
    /// </summary>
    public static class SurfaceRaster
    {
        private static readonly List<Vector3> verts = new List<Vector3>(8192);
        private static readonly List<int> tris = new List<int>(16384);
        private static Vector3[] tv = new Vector3[16384];
        private static int triCount;
        private static int[] binStart, binItems;
        private static int n;
        private static float x0, z0, cell;
        private static bool built;

        // Kolo 24: rozdíl „přirozená výška − výška po šití LOD švu" v mřížce sloupce. Šev se
        // u LOD0 šije jen k hrubšímu sousedovi a po posunu prstence se sloupec přestaví bez něj;
        // osazení proto sedá na přirozený povrch, aby nezáviselo na tom, kde zrovna byl prstenec.
        private static float[] seamDelta;
        private static bool hasSeamDelta;
        private static float sdX0, sdZ0, sdStep;
        private static int sdSide;

        /// <summary>Kolo 24: nastaví korekci výšky z pole sloupce (volat po <see cref="Build"/>).</summary>
        public static void SetSeamDelta(Unity.Collections.NativeArray<float> raw, Unity.Collections.NativeArray<float> surf,
                                        float originX, float originZ, float step, int side)
        {
            hasSeamDelta = false;
            if (!raw.IsCreated || !surf.IsCreated || raw.Length != side * side || surf.Length != raw.Length) return;
            if (seamDelta == null || seamDelta.Length < raw.Length) seamDelta = new float[raw.Length];
            bool any = false;
            for (int i = 0; i < raw.Length; i++)
            {
                float d = raw[i] - surf[i];
                seamDelta[i] = d;
                if (d > 1e-4f || d < -1e-4f) any = true;
            }
            hasSeamDelta = any;
            sdX0 = originX; sdZ0 = originZ; sdStep = step; sdSide = side;
        }

        private static float SeamDeltaAt(float x, float z)
        {
            float fx = (x - sdX0) / sdStep, fz = (z - sdZ0) / sdStep;
            int ix = Mathf.FloorToInt(fx), iz = Mathf.FloorToInt(fz);
            if (ix < 0 || iz < 0 || ix + 1 >= sdSide || iz + 1 >= sdSide) return 0f;
            float tx = fx - ix, tz = fz - iz;
            int i = iz * sdSide + ix;
            float a = Mathf.Lerp(seamDelta[i], seamDelta[i + 1], tx);
            float b = Mathf.Lerp(seamDelta[i + sdSide], seamDelta[i + sdSide + 1], tx);
            return Mathf.Lerp(a, b, tz);
        }

        public static void Build(List<Mesh> meshes, List<Matrix4x4> toWorld, float originX, float originZ, float span, float cellSize)
        {
            built = false;
            hasSeamDelta = false;
            triCount = 0;
            x0 = originX; z0 = originZ; cell = cellSize;
            n = Mathf.CeilToInt(span / cellSize);
            int bins = n * n;
            if (binStart == null || binStart.Length < bins + 1) binStart = new int[bins + 1];
            System.Array.Clear(binStart, 0, bins + 1);

            for (int m = 0; m < meshes.Count; m++)
            {
                Mesh mesh = meshes[m];
                if (mesh == null || mesh.vertexCount == 0) continue;
                Matrix4x4 w = toWorld[m];
                mesh.GetVertices(verts);
                mesh.GetTriangles(tris, 0);
                int need = (triCount + tris.Count / 3) * 3;
                if (tv.Length < need) System.Array.Resize(ref tv, Mathf.NextPowerOfTwo(need));
                for (int k = 0; k < tris.Count; k += 3)
                {
                    Vector3 a = w.MultiplyPoint3x4(verts[tris[k]]);
                    Vector3 b = w.MultiplyPoint3x4(verts[tris[k + 1]]);
                    Vector3 c = w.MultiplyPoint3x4(verts[tris[k + 2]]);
                    float det = (b.z - c.z) * (a.x - c.x) + (c.x - b.x) * (a.z - c.z);
                    if (Mathf.Abs(det) < 1e-6f) continue;   // svislý trojúhelník není povrch
                    int t = triCount * 3;
                    tv[t] = a; tv[t + 1] = b; tv[t + 2] = c;
                    triCount++;
                }
            }

            // CSR: počty → offsety → položky
            for (int t = 0; t < triCount; t++) ForBins(t, 0);
            int sum = 0;
            for (int i = 0; i < bins; i++) { int c = binStart[i]; binStart[i] = sum; sum += c; }
            binStart[bins] = sum;
            if (binItems == null || binItems.Length < sum) binItems = new int[Mathf.NextPowerOfTwo(Mathf.Max(sum, 64))];
            if (fill == null || fill.Length < bins) fill = new int[bins];
            System.Array.Copy(binStart, fill, bins);
            for (int t = 0; t < triCount; t++) ForBins(t, 1);
            built = true;
        }

        private static int[] fill;

        private static void ForBins(int t, int mode)
        {
            Vector3 a = tv[t * 3], b = tv[t * 3 + 1], c = tv[t * 3 + 2];
            float minX = Mathf.Min(a.x, Mathf.Min(b.x, c.x)), maxX = Mathf.Max(a.x, Mathf.Max(b.x, c.x));
            float minZ = Mathf.Min(a.z, Mathf.Min(b.z, c.z)), maxZ = Mathf.Max(a.z, Mathf.Max(b.z, c.z));
            int ix0 = Mathf.Max(0, Mathf.FloorToInt((minX - 1e-3f - x0) / cell));
            int ix1 = Mathf.Min(n - 1, Mathf.FloorToInt((maxX + 1e-3f - x0) / cell));
            int iz0 = Mathf.Max(0, Mathf.FloorToInt((minZ - 1e-3f - z0) / cell));
            int iz1 = Mathf.Min(n - 1, Mathf.FloorToInt((maxZ + 1e-3f - z0) / cell));
            for (int iz = iz0; iz <= iz1; iz++)
            for (int ix = ix0; ix <= ix1; ix++)
            {
                int i = iz * n + ix;
                if (mode == 0) binStart[i]++;
                else binItems[fill[i]++] = t;
            }
        }

        private static readonly float[] hits = new float[16];

        /// <summary>
        /// Nejvyšší průsečík svislice s meshem. <paramref name="layers"/> = kolik různých ploch
        /// (odstup &gt; 0,15 m) leží v pásu 6 m pod vrcholem – 1 znamená čistý povrch.
        /// </summary>
        public static bool Top(float x, float z, out float top, out int layers)
        {
            top = float.NegativeInfinity;
            layers = 0;
            if (!built) return false;
            int ix = Mathf.FloorToInt((x - x0) / cell), iz = Mathf.FloorToInt((z - z0) / cell);
            if (ix < 0 || iz < 0 || ix >= n || iz >= n) return false;
            int bin = iz * n + ix;
            int count = 0;
            for (int k = binStart[bin]; k < binStart[bin + 1]; k++)
            {
                int t = binItems[k] * 3;
                Vector3 a = tv[t], b = tv[t + 1], c = tv[t + 2];
                float det = (b.z - c.z) * (a.x - c.x) + (c.x - b.x) * (a.z - c.z);
                float l1 = ((b.z - c.z) * (x - c.x) + (c.x - b.x) * (z - c.z)) / det;
                float l2 = ((c.z - a.z) * (x - c.x) + (a.x - c.x) * (z - c.z)) / det;
                float l3 = 1f - l1 - l2;
                if (l1 < -1e-4f || l2 < -1e-4f || l3 < -1e-4f) continue;
                float y = l1 * a.y + l2 * b.y + l3 * c.y;
                if (count < hits.Length) hits[count++] = y;
                if (y > top) top = y;
            }
            if (count == 0) return false;

            // Různé plochy v pásu pod vrcholem (sdílené hrany dávají duplicitní průsečíky).
            for (int i = 0; i < count; i++)
            {
                float y = hits[i];
                if (top - y > 6f) continue;
                bool dup = false;
                for (int j = 0; j < i; j++)
                    if (top - hits[j] <= 6f && Mathf.Abs(hits[j] - y) < 0.15f) { dup = true; break; }
                if (!dup) layers++;
            }
            if (hasSeamDelta) top += SeamDeltaAt(x, z);
            return true;
        }
    }
}
