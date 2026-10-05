using System;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;
using static Unity.Mathematics.math;

namespace Orivilon.World.Generation
{
    /// <summary>Konstanty voxelového světa. Jediné místo, kde se mění rozměry.</summary>
    public static class VoxelWorld
    {
        /// <summary>Buněk na hranu chunku.</summary>
        public const int ChunkDim = 32;

        /// <summary>Vzorků hustoty na hranu. O jeden víc než buněk – poslední je sdílený se sousedem.</summary>
        public const int SampleDim = ChunkDim + 1;

        /// <summary>
        /// Podloží. Hlouběji než sem se nedostane ani propast, ani hráč.
        ///
        /// <para>Rozšíření dolů nic nestojí: pod <c>caveMaxDepth</c> je hustota zaručeně
        /// plná, takže <see cref="VoxelChunkBuilder.ColumnChunkRange"/> ty chunky ani
        /// neuvažuje. Bez toho stropu by hlubší svět stál lineárně víc.</para>
        /// </summary>
        public const float WorldMinY = -160f;

        /// <summary>
        /// Strop světa. Musí být nad nejvyšším možným vrcholem, tedy nad součtem
        /// <c>baseHeight</c> + <c>reliefAmplitude</c> × ampC + <c>upliftAmplitude</c>.
        /// Při výchozím nastavení je špička kolem 470 m (kolo 27: masivy do ~850 m, měkký strop
        /// massifTop 740 + 100 m v makru), takže je tu pořád rezerva
        /// pro ladění amplitudy pohoří.
        /// </summary>
        public const float WorldMaxY = 960f;

        /// <summary>
        /// Přes jakou svislou vzdálenost hustota přejde z +1 na -1.
        /// Zároveň definuje pás, ve kterém má 3D šum (fáze 2) vůbec šanci něco změnit.
        /// </summary>
        public const float Falloff = 12f;

        /// <summary>Světová pozice rohu chunku.</summary>
        public static float3 ChunkOrigin(int3 coord, float voxelSize)
            => new float3(coord) * (ChunkDim * voxelSize);

        /// <summary>Rozsah svislých chunků, do kterého se vejde celý svět.</summary>
        public static void ChunkYRange(float voxelSize, out int minY, out int maxY)
        {
            float h = ChunkDim * voxelSize;
            minY = (int)floor(WorldMinY / h);
            maxY = (int)floor((WorldMaxY - 0.001f) / h);
        }
    }

    /// <summary>Zda chunk vůbec obsahuje izoplochu. Prázdné a plné chunky se negenerují.</summary>
    public enum ChunkOccupancy : byte { Empty, Solid, Surface }

    /// <summary>Barevná škála terénu pro fázi 1. Ve fázi 4 ji nahradí biomy.</summary>
    [Serializable]
    public struct TerrainPalette
    {
        public float3 underwater, sand, grass, grassHigh, rock, snow;

        /// <summary>Barva ledovce – nejvyšší polohy. Světlejší a lehce do modra než sníh.</summary>
        public float3 ice;

        /// <summary>O kolik metrů nad sněžnou čarou přechází sníh v led.</summary>
        public float iceRise;

        public float sandTop, grassTop, rockTop, snowTop, seaLevel;
        public float slopeRock;

        /// <summary>
        /// Čtyři rohy klimatického čtverce: studeno/teplo × sucho/vlhko. Porost se mezi
        /// nimi bilineárně prolíná, takže mezi biomy není hrana – jen pozvolný přechod,
        /// který se v nízkopolygonovém terénu čte líp než ostré rozhraní.
        /// </summary>
        public float3 coldDry, coldWet, hotDry, hotWet;

        /// <summary>Síla biomového zabarvení. 0 = jednotná zelená jako dřív.</summary>
        public float biomeTint;

        /// <summary>
        /// O kolik metrů se posune sněžná čára mezi nejstudenějším a nejteplejším krajem.
        /// Bez tohohle leží sníh všude ve stejné výšce a svět působí placatě.
        /// </summary>
        public float snowTempShift;

        /// <summary>
        /// Síla mikro-variace odstínu na jednotlivé fasetě. 0 = plocha je jednolitá,
        /// nad ~0.25 se z trávy stane šum. Hodnota je relativní změna jasu.
        /// </summary>
        public float microStrength;

        /// <summary>Perioda hladké složky variace v metrech – velikost barevných skvrn.</summary>
        public float microPeriod;

        /// <summary>
        /// Podíl čistě per-faseta jitteru na variaci, 0–1.
        ///
        /// <para>Bez něj plocha zůstane jednolitá, i když je šum zapnutý: sousední
        /// trojúhelníky mají těžiště pár decimetrů od sebe, takže ze šumu s periodou
        /// 17 m dostanou prakticky tutéž hodnotu. Rozdíl mezi FASETAMI musí přijít
        /// z hashe těžiště, ne ze šumu.</para>
        /// </summary>
        public float microFacet;

        /// <summary>
        /// Kolik metrů nad hladinou sahá břehový písek. 0 = břehy se nebarví.
        ///
        /// <para>Je to výška, ne vodorovná šířka – a to je celý trik. Na plochém břehu
        /// z toho vyjde široká pláž, na strmém jen úzký proužek u vody, přesně jak to
        /// dělá skutečná voda. Vodorovná šířka by musela znát sklon a stejně by na
        /// srázu vyšla nesmyslně.</para>
        /// </summary>
        public float shoreSandRise;

        /// <summary>O kolik metrů pod hladinu písek pokračuje, než přejde do dna.</summary>
        public float shoreSandDrop;

        /// <summary>
        /// Nejmenší |normála.y|, na které písek ještě drží. Svislá stěna nad vodou je
        /// skála, ne pláž – jinak se každý útes u moře natře béžově.
        /// </summary>
        public float shoreSandFlat;

        /// <summary>
        /// Kolo 8: umělecká paleta biomů (louka, niva, hory, pobřeží). Nastavuje se jen za běhu
        /// z <c>WorldGenSettings.ArtPassEnabled</c>, v uložené scéně není – proto
        /// <see cref="NonSerializedAttribute"/>. 0 = původní <see cref="Evaluate(float,float,float,float,float)"/>.
        /// </summary>
        [NonSerialized] public float art;

        /// <summary>
        /// Kolo 12, jen diagnostika (<c>/pruhy diag N</c>): 0 = normálně, 1 = bez barvy strmých
        /// ploch, 2 = jednotná šedá (jen geometrie a světlo), 3 = váha strmé barvy červeně,
        /// 4 = terasování modře + strmá barva červeně, 5 = bez mikro-variace.
        /// </summary>
        [NonSerialized] public float diag;

        /// <summary>
        /// Kolo 14: klimatické regiony (jehličnatý les, březový háj, step, poušť, mesa, mokřad,
        /// tundra) v barvě povrchu – viz <see cref="BiomeMath"/>. Jen za běhu z
        /// <c>WorldGenSettings.BiomesEnabled</c>; 0 = barvy kola 13.
        /// </summary>
        [NonSerialized] public float biomes;

        /// <summary>Kolo 14: seed světa pro barvy vrstev mes (hash pásu).</summary>
        [NonSerialized] public int biomeSeed;

        /// <summary>
        /// Kolo 20, jen A/B diagnostika (<c>/biome snih off</c>): 1 = sněžná čára BEZ zvednutí podle biomového
        /// klimatu (chování kola 19). 0 (výchozí) = <see cref="BiomeMath.SnowLift"/> zapnuto.
        /// </summary>
        [NonSerialized] public float snowLiftOff;

        public static TerrainPalette Default => new TerrainPalette
        {
            underwater = new float3(0.31f, 0.29f, 0.22f),
            sand       = new float3(0.78f, 0.72f, 0.52f),
            grass      = new float3(0.34f, 0.49f, 0.26f),
            grassHigh  = new float3(0.27f, 0.41f, 0.22f),
            rock       = new float3(0.44f, 0.43f, 0.41f),
            snow       = new float3(0.92f, 0.93f, 0.95f),
            ice        = new float3(0.80f, 0.88f, 0.97f),
            iceRise    = 150f,
            seaLevel   = 0f,
            sandTop    = 2.5f,
            grassTop   = 120f,
            rockTop    = 240f,
            snowTop    = 300f,
            slopeRock  = 0.62f,

            coldDry    = new float3(0.44f, 0.44f, 0.36f),   // tundra, suchá tráva na kameni
            coldWet    = new float3(0.22f, 0.35f, 0.25f),   // tmavý jehličnatý porost
            hotDry     = new float3(0.66f, 0.58f, 0.31f),   // savana, vyprahlá step
            hotWet     = new float3(0.24f, 0.52f, 0.20f),   // sytá zeleň
            biomeTint  = 0.85f,
            snowTempShift = 110f,

            microStrength = 0.16f,
            microPeriod   = 17f,
            microFacet    = 0.55f,

            shoreSandRise = 3.5f,
            shoreSandDrop = 2.5f,
            shoreSandFlat = 0.72f,
        };

        /// <summary>
        /// Doplní klimatické barvy, pokud je paleta uložená ve scéně ještě nezná.
        ///
        /// <para>Unity při přidání pole do serializované struktury nechá novým polím
        /// hodnotu z inicializátoru, ale spoléhat se na to naslepo by znamenalo, že jediná
        /// odchylka udělá ze světa černou kouli. Čtyři černé rohy klimatického čtverce
        /// nemají smysl, takže jsou spolehlivá značka „nenastaveno".</para>
        /// </summary>
        public void EnsureClimate()
        {
            // Mikro-variace přibyla později než klima, takže se doplňuje ZVLÁŠŤ a před
            // návratem níž. Kdyby visela pod stejnou podmínkou, paleta uložená ve scéně
            // s vyplněným klimatem by měla microPeriod = 0 a variace by byla tiše mrtvá.
            // Led přibyl se stejnou revizí jako mikro-variace a doplňuje se stejně –
            // před návratem níž, aby ho dostala i paleta s už vyplněným klimatem.
            if (lengthsq(ice) < 1e-6f)
            {
                ice = Default.ice;
                if (iceRise <= 0.01f) iceRise = Default.iceRise;
            }

            // Břehový písek přibyl později než led. Doplňuje se stejným způsobem a rovněž
            // PŘED návratem níž – paleta s vyplněným klimatem by jinak měla shoreSandRise = 0
            // a barvení břehů by bylo tiše mrtvé, aniž by na to cokoli upozornilo.
            if (shoreSandRise <= 0.01f)
            {
                TerrainPalette sd = Default;
                shoreSandRise = sd.shoreSandRise;
                if (shoreSandDrop <= 0.01f) shoreSandDrop = sd.shoreSandDrop;
                if (shoreSandFlat <= 0.01f) shoreSandFlat = sd.shoreSandFlat;
            }

            if (microPeriod <= 0.01f)
            {
                TerrainPalette md = Default;
                microPeriod = md.microPeriod;
                if (microStrength <= 0f) microStrength = md.microStrength;
                if (microFacet <= 0f) microFacet = md.microFacet;
            }

            float sum = lengthsq(coldDry) + lengthsq(coldWet) + lengthsq(hotDry) + lengthsq(hotWet);
            if (sum > 1e-6f) return;

            TerrainPalette d = Default;
            coldDry = d.coldDry; coldWet = d.coldWet;
            hotDry = d.hotDry;   hotWet = d.hotWet;
            biomeTint = d.biomeTint;
            if (snowTempShift == 0f) snowTempShift = d.snowTempShift;
        }

        /// <summary>Barva fasety bez klimatu – střed škály. Zachováno pro jednorázové dotazy.</summary>
        public float3 Evaluate(float y, float normalY) => Evaluate(y, normalY, 0.5f, 0.5f);

        /// <summary>
        /// Barva jedné fasety podle výšky těžiště, sklonu normály a místního klimatu.
        ///
        /// <para>Klima nemění jen odstín porostu, ale i sněžnou čáru. Tím se z jedné
        /// nadmořské výšky stane v chladném kraji zasněžený hřeben a v teplém pořád ještě
        /// louka – což je většina toho, proč krajina působí rozmanitě.</para>
        /// </summary>
        /// <param name="temperature">0–1, ColumnField.temp.</param>
        /// <param name="humidity">0–1, ColumnField.hum.</param>
        public float3 Evaluate(float y, float normalY, float temperature, float humidity)
            => Evaluate(y, normalY, temperature, humidity, NoShore);

        /// <summary>Hladina, která znamená „tady žádná voda není". Viz ColumnField.shoreY.</summary>
        public const float NoShore = -1e9f;

        /// <summary>
        /// Barva fasety včetně břehového písku kolem moře, řek a jezer.
        ///
        /// <para>Písek se nerozhoduje podle nadmořské výšky, ale podle výšky NAD MÍSTNÍ
        /// HLADINOU. To je jediný způsob, jak dostat pláž i k horské řece ve čtyřech stech
        /// metrech – prahová výška <see cref="sandTop"/> tam nikdy nemůže sáhnout, protože
        /// je vztažená k moři.</para>
        /// </summary>
        /// <param name="shoreY">Hladina nejbližší vody, nebo <see cref="NoShore"/>.</param>
        public float3 Evaluate(float y, float normalY, float temperature, float humidity, float shoreY)
        {
            float t = saturate(temperature), h = saturate(humidity);

            float3 climate = lerp(lerp(coldDry, coldWet, h), lerp(hotDry, hotWet, h), t);
            float3 gLow  = lerp(grass, climate, biomeTint);
            float3 gHigh = lerp(grassHigh, climate * 0.78f, biomeTint);

            // Teplo tlačí sníh nahoru, chlad dolů.
            float snowLine = snowTop + snowTempShift * (t - 0.5f) * 2f;
            float rockLine = min(rockTop, snowLine - 10f);

            float3 c;
            if (y < seaLevel - 1f)      c = lerp(underwater, sand, smoothstep(seaLevel - 14f, seaLevel, y));
            else if (y < sandTop)       c = sand;
            else if (y < grassTop)      c = lerp(gLow, gHigh, smoothstep(sandTop, grassTop, y));
            else if (y < snowLine)      c = lerp(gHigh, rock, smoothstep(grassTop, rockLine, y));
            else
            {
                c = lerp(rock, snow, smoothstep(snowLine, snowLine + 45f, y));

                // Nejvyšší polohy jsou led, ne sníh. Bez toho vypadá pětisetmetrový štít
                // úplně stejně jako první zasněžený hřeben nad lesem a hora ztratí měřítko.
                c = lerp(c, ice, smoothstep(snowLine + iceRise * 0.45f, snowLine + iceRise, y));
            }

            // ── břehový písek ─────────────────────────────────────────
            //
            // Až TADY, tedy nad hotovou výškovou barvou, ale JEŠTĚ PŘED skálou na svahu.
            // Pořadí není libovolné: kdyby se písek nanášel až nakonec, přebarvil by
            // i útes spadající do vody a z pobřežních skal by byly duny.
            if (shoreSandRise > 0.01f && shoreY > NoShore * 0.5f)
            {
                float d = y - shoreY;

                // Nahoru se pás vytrácí přes celou svou výšku, dolů končí rychleji –
                // pod vodou barvu stejně přebíjí `underwater` a dlouhý přechod by
                // z mělčiny udělal mléčný pruh.
                float up = 1f - smoothstep(0f, shoreSandRise, d);
                float down = smoothstep(-shoreSandDrop - 1.5f, -shoreSandDrop, d);
                float flat = smoothstep(shoreSandFlat - 0.12f, shoreSandFlat + 0.10f, abs(normalY));

                c = lerp(c, sand, saturate(up * down * flat));
            }

            // Strmé plochy jsou skála bez ohledu na výšku – tohle dělá útesy čitelné.
            // Nad sněžnou čarou se ale i skála přisype: jinak je vrchol rozčísnutý na bílé
            // čepičky a černé stěny a vypadá, jako by tál.
            float3 steep = y >= snowLine ? lerp(rock, snow, 0.30f) : rock;

            float rockAmt = 1f - smoothstep(slopeRock - 0.14f, slopeRock + 0.14f, abs(normalY));
            return lerp(c, steep, rockAmt);
        }

        /// <summary>
        /// Kolo 8: barva fasety podle čtyř čitelných biomů – louka, niva, hory, pobřeží.
        ///
        /// <para>Váhy biomů jsou tytéž jako v osazovači (<c>EcologyPlacer.Describe</c>): niva =
        /// výška nad hladinou řeky/jezera, pobřeží = výška nad mořem mimo řeky, hory = výška
        /// vůči <see cref="grassTop"/> a sněžné čáře. Prahy se ale NEMĚNÍ – mění se jen barvy,
        /// takže klasifikace biomů, osazení i determinismus zůstávají stejné.</para>
        ///
        /// <para>Každá hranice pásu je rozvlněná dvěma šumy (140 m a 45 m). Bez nich by sněžná
        /// čára i okraj alpínské louky ležely přesně na vrstevnici a z dálky by to byly
        /// vodorovné pruhy.</para>
        /// </summary>
        public float3 EvaluateArt(float3 p, float normalY, float temperature, float humidity,
                                  float shoreY, float3 off)
            => EvaluateArt(p, normalY, temperature, humidity, shoreY, off, 0f);

        /// <param name="cliff">Síla terasování sloupce (ColumnField.cliff) – jen diagnostika a kolo 12.</param>
        public float3 EvaluateArt(float3 p, float normalY, float temperature, float humidity,
                                  float shoreY, float3 off, float cliff)
        {
            float t = saturate(temperature), h = saturate(humidity);
            float y = p.y;
            float ny = abs(normalY);

            float nBig = GenNoise.Fbm2(p.xz * (1f / 140f), 2, off.xz + new float2(17.3f, -41.9f));
            float nMid = GenNoise.Fbm2(p.xz * (1f / 45f), 2, off.xz + new float2(-63.1f, 29.7f));

            bool hasShore = shoreY > NoShore * 0.5f;
            bool riverish = hasShore && shoreY > seaLevel + 0.3f;
            float hW = hasShore ? y - shoreY : 1e4f;

            // ── louka: teplá, otevřená ──
            float3 mFresh = new float3(0.21f, 0.43f, 0.13f);
            float3 mWarm  = new float3(0.33f, 0.47f, 0.14f);
            float3 mCool  = new float3(0.20f, 0.36f, 0.18f);
            float3 mDry   = new float3(0.44f, 0.46f, 0.18f);
            float3 meadow = lerp(mFresh, mWarm, smoothstep(0.35f, 0.8f, t) * (1f - 0.6f * h));
            meadow = lerp(meadow, mCool, 1f - smoothstep(0.18f, 0.45f, t));
            meadow = lerp(meadow, mDry, 0.6f * smoothstep(0.62f, 0.88f, t) * (1f - smoothstep(0.22f, 0.45f, h)));
            // záplaty zlatavé trávy a stinnějších míst – velké a měkké, žádná šachovnice
            meadow = lerp(meadow, mWarm, saturate(nMid * 0.6f) * 0.35f);
            meadow *= 1f + 0.06f * nBig;

            // ── kolo 14: klimatické regiony ──
            RegionWeights rw = default;
            ClimateSample cs = default;
            bool regions = biomes > 0.5f;
            if (regions)
            {
                rw = BiomeMath.WeightsC(p.xz, y, t, h, cliff, hW, hasShore, seaLevel, off, out cs);
                meadow = BiomeMath.Ground(rw, meadow, p, nBig, nMid);
            }
            float aridLand = regions ? rw.desert + rw.mesa + 0.6f * rw.steppe + rw.volcanic + rw.petrified + 0.6f * rw.burnt : 0f;   // kolo 19: bez zeleného pobřežního pásu
            // kolo 20: nové biomy mají vlastní břeh (závěje, bahno, sůl, led) – bez zeleného pobřežního pásu a travnatých stupňů
            float cold20 = regions ? saturate(rw.snowPeaks + rw.glacier + rw.frozenOcean) : 0f;
            float own20 = regions ? saturate(cold20 + rw.mangrove + rw.saltFlat + rw.geothermal) : 0f;
            // kolo 28: černý písek čediče, holá obsidiánová a alabastrová pláň – také bez zeleného pobřežního pásu
            float w28 = regions ? rw.basalt + rw.obsidian + rw.alabaster : 0f;
            if (w28 > 0.002f) own20 = saturate(own20 + w28);
            aridLand = saturate(aridLand + own20);
            if (regions && diag > 5.5f && diag < 6.5f) return BiomeMath.DebugColor(rw);

            // ── hory ──
            float snowLine = snowTop + snowTempShift * (t - 0.5f) * 2f + 28f * nBig + 10f * nMid;
            // Kolo 20: sníh jen tam, kde je chladno i podle biomového klimatu – v teplém a horkém pásmu
            // čára stoupne nad vrcholy (pod ní zůstává skála a suť). Štíty a ledovec mají sníh níž.
            if (regions)
            {
                if (snowLiftOff < 0.5f) snowLine += BiomeMath.SnowLift(cs.t);
                snowLine -= 70f * rw.snowPeaks + 45f * rw.glacier;
            }
            float rockLine = min(rockTop, snowLine - 10f);
            float3 alpMeadow = regions ? lerp(new float3(0.34f, 0.40f, 0.22f), BiomeMath.TundraColor(nMid), 0.7f)
                                       : new float3(0.34f, 0.40f, 0.22f);
            // kolo 20: na štítech a ledovci je i alpínské pásmo sněhové/ledové (barva regionu), ne lišejníková louka
            if (regions && cold20 > 0.002f) alpMeadow = lerp(alpMeadow, meadow, saturate((rw.snowPeaks + rw.glacier) * 1.2f));
            float3 scree = new float3(0.46f, 0.44f, 0.40f);
            float3 rockLo = new float3(0.44f, 0.39f, 0.33f);
            float3 rockHi = new float3(0.38f, 0.39f, 0.43f);
            float3 rockC = lerp(rockLo, rockHi, smoothstep(grassTop, snowLine, y));
            // jemné vrstvení skály (perioda ~9 m, rozvlněné), jen na skalních plochách níž
            float strata = 1f + 0.05f * sin((y + 7f * nMid) * 0.7f);

            float wAlp = smoothstep(grassTop - 30f + 18f * nMid, grassTop + 40f, y);
            float3 c = lerp(meadow, alpMeadow, wAlp);
            c = lerp(c, scree, smoothstep(rockLine - 70f + 15f * nMid, rockLine, y) * (regions ? 1f - saturate(rw.glacier * 1.3f) : 1f));
            // Kolo 14b: v tundře je výškové pásmo lišejníkové (šedozelené), ne béžová suť.
            if (regions) c = lerp(c, BiomeMath.TundraColor(nMid), rw.tundra * 0.85f * wAlp);

            // ── sníh: ploché fasety drží sníh níž, strmé výš – věrohodná sněžná linie ──
            float snowY = y + 60f * (ny - 0.8f);
            float snowAmt = smoothstep(snowLine - 12f, snowLine + 22f, snowY);
            float3 snowC = lerp(snow, ice, smoothstep(snowLine + iceRise * 0.45f, snowLine + iceRise, y));
            c = lerp(c, snowC, snowAmt);

            // ── niva: živá a vodní ──
            if (riverish)
            {
                float wN = 1f - smoothstep(3f + 2f * nMid, 10f, hW);
                float3 lush = lerp(new float3(0.17f, 0.42f, 0.16f), new float3(0.14f, 0.37f, 0.19f), h);
                // Kolo 15: v mokřadu je niva tmavá a vlhká, ne svěže zelená – odliší ho od běžného břehu.
                if (regions) lush = lerp(lush, BiomeMath.SwampColor(nMid), saturate(rw.swamp * 1.2f));
                c = lerp(c, lush, wN * (1f - snowAmt) * (1f - wAlp * 0.5f) * (1f - 0.85f * own20));
            }

            // ── pobřeží: čisté a čitelné ──
            float above = y - seaLevel;
            if (!riverish)
            {
                float wK = 1f - smoothstep(3f, 11f + 3f * nMid, above);
                c = lerp(c, new float3(0.40f, 0.50f, 0.26f), wK * 0.85f * (1f - saturate(aridLand)) * (1f - (regions ? rw.swamp : 0f)));
            }

            float3 sandC = riverish ? new float3(0.52f, 0.50f, 0.42f) : new float3(0.70f, 0.62f, 0.43f);
            // Kolo 15: břeh mokřadu je bahnitý (tmavá hlína), ne písčitý.
            if (regions) sandC = lerp(sandC, new float3(0.25f, 0.22f, 0.14f), saturate(rw.swamp * 1.2f));
            // Kolo 20: břeh a dno nových biomů. Voda se nemění – průzračná hladina jen ukáže jiné dno:
            // tyrkysová ledovcová a geotermální jezírka, bílé solné mělčiny, bahnité laguny, tmavé moře pod krami.
            float3 under = underwater, bed = sandC * 0.70f;
            // Kolo 21: v krystalové oblasti prosvítá existující průzračnou vodou fialovomodré minerální dno (jen barva dna,
            // žádná nová ani falešná hladina).
            if (regions && rw.crystal > 0.002f)
            {
                float3 crBed = new float3(0.26f, 0.22f, 0.52f);
                float cw21 = saturate(rw.crystal * 1.2f);
                under = lerp(under, crBed, cw21); bed = lerp(bed, crBed * 1.15f, cw21);
            }
            if (regions && own20 > 0.002f)
            {
                sandC = lerp(sandC, new float3(0.80f, 0.84f, 0.88f), saturate((rw.frozenOcean + rw.snowPeaks) * 1.2f));
                sandC = lerp(sandC, new float3(0.48f, 0.48f, 0.47f), saturate(rw.glacier * 1.2f));
                sandC = lerp(sandC, new float3(0.22f, 0.19f, 0.12f), saturate(rw.mangrove * 1.2f));
                sandC = lerp(sandC, new float3(0.80f, 0.78f, 0.72f), saturate(rw.saltFlat * 1.2f));
                sandC = lerp(sandC, new float3(0.70f, 0.66f, 0.55f), saturate(rw.geothermal * 1.2f));
                float3 bedC = new float3(0.16f, 0.50f, 0.52f) * saturate(rw.glacier + rw.geothermal)
                            + new float3(0.80f, 0.79f, 0.74f) * rw.saltFlat + new float3(0.20f, 0.19f, 0.11f) * rw.mangrove
                            + new float3(0.07f, 0.10f, 0.14f) * rw.frozenOcean;
                float bw = saturate(rw.glacier + rw.geothermal + rw.saltFlat + rw.mangrove + rw.frozenOcean);
                if (bw > 0.002f) { bedC /= bw; under = lerp(under, bedC, bw); bed = lerp(bed, bedC, bw); }
            }
            // Kolo 28: černý čedičový písek a tmavé dno pod existující vodou (hladina, pěna ani vodní mesh se nemění),
            // šedý obsidiánový a světlý alabastrový břeh řek.
            if (w28 > 0.002f)
            {
                sandC = lerp(sandC, new float3(0.025f, 0.025f, 0.028f), saturate(rw.basalt * 1.2f));
                sandC = lerp(sandC, new float3(0.045f, 0.042f, 0.05f), saturate(rw.obsidian * 1.2f));
                sandC = lerp(sandC, new float3(0.45f, 0.42f, 0.35f), saturate(rw.alabaster * 1.2f));
                float bw28 = saturate(rw.basalt * 1.2f);
                if (bw28 > 0.002f) { float3 bB = new float3(0.02f, 0.025f, 0.03f); under = lerp(under, bB, bw28 * 0.6f); bed = lerp(bed, bB, bw28); }
            }
            if (y < seaLevel - 1f) c = lerp(under, bed, smoothstep(seaLevel - 14f, seaLevel, y));
            else if (y < sandTop && !riverish) c = sandC;

            if (shoreSandRise > 0.01f && hasShore)
            {
                float d = hW;
                float up = 1f - smoothstep(0f, shoreSandRise, d);
                float down = smoothstep(-shoreSandDrop - 1.5f, -shoreSandDrop, d);
                float flat = smoothstep(shoreSandFlat - 0.12f, shoreSandFlat + 0.10f, ny);
                c = lerp(c, sandC, saturate(up * down * flat));
            }

            // Dno pod hladinou: mokrý, tmavší písek, s hloubkou do barvy dna. Suchý světlý
            // písek prosvítal mělkou vodou jako bledé plošky (čelo vodopádu, peřeje).
            if (hasShore && hW < 0f)
            {
                float3 wet = lerp(regions ? bed : sandC * 0.70f, under, smoothstep(0.3f, 3.0f, -hW));
                c = lerp(c, wet, smoothstep(0f, 0.25f, -hW));
            }

            // ── strmé plochy: skála podle místa ──
            float3 steep = rockC * strata;
            if (!riverish) steep = lerp(steep, new float3(0.55f, 0.48f, 0.39f), 1f - smoothstep(6f, 30f, above));
            else steep = lerp(steep, new float3(0.38f, 0.31f, 0.23f), 1f - smoothstep(2f, 8f, hW));
            // Nízké kopce v louce: strmé stupně terasy jsou hlína porostlá trávou, ne holá
            // skála – jinak se každý svah rozpadne na hnědé vodorovné pruhy.
            float lowland = (1f - wAlp) * smoothstep(8f, 20f, riverish ? hW : above);
            steep = lerp(steep, lerp(meadow * 0.78f, rockC, 0.35f), lowland * 0.7f * (1f - saturate(aridLand)));
            if (regions) steep = BiomeMath.Steep(rw, steep, y, nMid, biomeSeed, rw.cliffs > 0.002f ? BiomeMath.CliffDarkness(p.xz, off) : 0f, p.xz);
            steep = lerp(steep, lerp(rockC, snow, 0.30f), smoothstep(snowLine - 5f, snowLine + 30f, y));

            float sr = slopeRock + 0.04f * nMid;
            float rockAmt = 1f - smoothstep(sr - 0.14f, sr + 0.14f, ny);
            if (diag > 0.5f && diag < 1.5f) rockAmt = 0f;
            else if (diag > 2.5f && diag < 3.5f) return lerp(new float3(0.5f), new float3(0.9f, 0.1f, 0.1f), rockAmt);
            else if (diag > 3.5f && diag < 4.5f)
                return lerp(lerp(new float3(0.5f), new float3(0.1f, 0.2f, 0.9f), saturate(cliff * 2f)),
                            new float3(0.9f, 0.1f, 0.1f), rockAmt);
            return lerp(c, steep, rockAmt);
        }

        /// <summary>
        /// Mikro-variace odstínu jedné fasety. Volá se až po <see cref="Evaluate"/>.
        ///
        /// <para>Skládá se ze dvou složek s odlišným měřítkem. Hladký 3D šum s periodou
        /// <see cref="microPeriod"/> dělá velké barevné skvrny – tím krajina přestane být
        /// jednobarevná na dálku. Hash kvantovaného těžiště dělá rozdíl mezi sousedními
        /// trojúhelníky – tím přestane být jednobarevná zblízka. Jedno bez druhého
        /// nestačí a je to nejčastější důvod, proč „variace nic nedělá".</para>
        ///
        /// <para>Těžiště se kvantuje na osminu metru, aby tentýž trojúhelník vyšel po
        /// přestavbě chunku stejně. Bez kvantizace by se odstín po každém kopnutí
        /// v okolí nepatrně přebarvil, protože průsečík hrany se posune o zlomek voxelu.</para>
        /// </summary>
        /// <param name="offset">GenParams.offMicro – dekorelace kanálu vůči zbytku šumů.</param>
        public float3 Vary(float3 rgb, float3 centroid, float3 offset)
        {
            if (microStrength <= 0.0001f) return rgb;

            float freq = microPeriod > 0.01f ? 1f / microPeriod : 0f;
            float smoothPart = freq > 0f ? GenNoise.Fbm3(centroid * freq, 2, offset) : 0f;

            int3 q = (int3)round(centroid * 8f);
            uint h = GenNoise.Hash(unchecked((uint)(q.x * 73856093) ^ (uint)(q.y * 19349663)
                                           ^ (uint)(q.z * 83492791)));
            float facetPart = GenNoise.Hash01(h) * 2f - 1f;

            float f = saturate(microFacet);
            float v = 1f + microStrength * (smoothPart * (1f - f) + facetPart * f);

            // Samotná změna jasu vypadá jako šum v osvětlení. Malý posun kanálů proti sobě
            // z toho udělá rozdíl v ODSTÍNU – teplejší a chladnější místa téže plochy.
            float3 tinted = rgb * v;
            tinted.x += microStrength * 0.05f * facetPart;
            tinted.z -= microStrength * 0.05f * smoothPart;
            return tinted;
        }
    }

    /// <summary>
    /// Hotová geometrie jednoho chunku.
    ///
    /// Dva meshe z jednoho průchodu: <c>solid*</c> je svařený indexovaný mesh pro collider,
    /// <c>render*</c> je rozbalený na nesdílené vrcholy s normálou trojúhelníku – low-poly
    /// flat shading a barva na fasetu, ne na vrchol.
    /// </summary>
    public struct ChunkMeshData : IDisposable
    {
        public int3 coord;

        public NativeList<float3> solidVerts;
        public NativeList<int> solidTris;

        public NativeList<float3> renderVerts;
        public NativeList<float3> renderNormals;
        public NativeList<Color32> renderColors;
        public NativeList<int> renderTris;

        public bool IsCreated => solidVerts.IsCreated;
        public bool IsEmpty => !IsCreated || renderTris.Length == 0;

        public static ChunkMeshData Create(int3 coord, Allocator a) => new ChunkMeshData
        {
            coord = coord,
            solidVerts    = new NativeList<float3>(4096, a),
            solidTris     = new NativeList<int>(8192, a),
            renderVerts   = new NativeList<float3>(12288, a),
            renderNormals = new NativeList<float3>(12288, a),
            renderColors  = new NativeList<Color32>(12288, a),
            renderTris    = new NativeList<int>(12288, a),
        };

        /// <summary>Naplní Unity meshe. Hlavní vlákno.</summary>
        public void Apply(Mesh renderMesh, Mesh colliderMesh)
        {
            if (renderMesh != null)
            {
                renderMesh.Clear();
                if (renderTris.Length > 0)
                {
                    renderMesh.indexFormat = renderVerts.Length > 65000
                        ? UnityEngine.Rendering.IndexFormat.UInt32
                        : UnityEngine.Rendering.IndexFormat.UInt16;
                    renderMesh.SetVertices(renderVerts.AsArray());
                    renderMesh.SetNormals(renderNormals.AsArray());
                    renderMesh.SetColors(renderColors.AsArray());
                    renderMesh.SetIndices(renderTris.AsArray(), MeshTopology.Triangles, 0, false);
                    renderMesh.RecalculateBounds();

                    // Data se na GPU nahrají líně, až při prvním vykreslení. Pro prohození
                    // LOD je to pozdě: sloupec bychom zveřejnili, starý schovali – a nový by
                    // ten první snímek ještě nebyl nahraný. Tímhle se upload udělá tady,
                    // ve chvíli, kdy s ním rozpočet maxAppliesPerFrame počítá.
                    // Argument false = mesh zůstane čitelný z CPU.
                    renderMesh.UploadMeshData(false);
                }
            }

            if (colliderMesh != null)
            {
                colliderMesh.Clear();
                if (solidTris.Length > 0)
                {
                    colliderMesh.indexFormat = solidVerts.Length > 65000
                        ? UnityEngine.Rendering.IndexFormat.UInt32
                        : UnityEngine.Rendering.IndexFormat.UInt16;
                    colliderMesh.SetVertices(solidVerts.AsArray());
                    colliderMesh.SetIndices(solidTris.AsArray(), MeshTopology.Triangles, 0, false);
                    colliderMesh.RecalculateBounds();
                }
            }
        }

        public void Dispose()
        {
            if (solidVerts.IsCreated)    solidVerts.Dispose();
            if (solidTris.IsCreated)     solidTris.Dispose();
            if (renderVerts.IsCreated)   renderVerts.Dispose();
            if (renderNormals.IsCreated) renderNormals.Dispose();
            if (renderColors.IsCreated)  renderColors.Dispose();
            if (renderTris.IsCreated)    renderTris.Dispose();
        }
    }
}
