using Orivilon.World.Generation.Hydro;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using static Unity.Mathematics.math;

namespace Orivilon.World.Generation
{
    /// <summary>
    /// Sdílená matematika sloupcové (2D) vrstvy.
    ///
    /// Všechno jsou statické čisté funkce, takže je volají jak Burst joby (dávkové generování),
    /// tak synchronní dotazy typu SampleSurfaceY (spawn hráče, osazení objektů).
    /// Jediná implementace = žádné riziko, že se dávková a bodová cesta rozejdou.
    /// </summary>
    public static class WorldGenMath
    {
        /// <summary>O kolik nad hladinu řeky se zvedne břeh za korytem (hráz), v metrech.</summary>
        public const float RiverLeveeRise = 0.8f;

        /// <summary>Sklon valu od břehu ven (m výšky na m vzdálenosti), před vlněním šumem.</summary>
        public const float RiverLeveeSlope = 0.33f;

        /// <summary>
        /// Kolo 7: o kolik nad vlastní hladinu se zvedne břeh horního úseku u hydrologického
        /// schodu (práh), v metrech. Musí být nad zanořením hladiny pod břeh (0,45 m), jinak
        /// by okraj horní vody vyšel zase jako mělký film.
        /// </summary>
        public const float HydroSillRise = 0.6f;

        /// <summary>Kolo 7: od jakého rozdílu (hladina koryta − smíchaná hladina údolí) práh začíná a kde je plný (m).</summary>
        public const float HydroSillStart = 0.35f, HydroSillFull = 0.8f;

        /// <summary>Kolo 7 (ladění): 1 = práh slábne nad hlubší vodou, 0 = jen podle osy koryta.</summary>
        public const float HydroSillDepthGate = 0f;

        /// <summary>Hloubka mělčiny u okraje koryta pod hladinou, v metrech.</summary>
        public const float RiverShelfDepth = 0.45f;

        /// <summary>O kolik nad hladinu jezera se zvedne okraj pánve, který by jinak ležel pod ní.</summary>
        public const float LakeRimRise = 0.25f;

        /// <summary>Zaplněná pánev s hladinou níž než tohle nad mořem se bere jako moře, ne jezero.</summary>
        public const float LakeMinAboveSea = 2f;

        /// <summary>
        /// Síla Worley pánve, od které terén klesá pod hladinu – tam začíná voda.
        /// Cíl ořezu je <c>hladina + shoreRise·(1−b) − depth·b</c>, pod hladinu jde od
        /// <c>b = shoreRise/(shoreRise+depth)</c>. Hladina se kreslí o kousek dřív a okraj
        /// pánve je od téhle hodnoty zvednutý nad vodu, takže mezi nimi není mezera.
        /// </summary>
        public static float WorleyWetBasin(in GenParams gp)
            => max(0.05f, saturate(gp.lakeShoreRise / max(gp.lakeShoreRise + gp.lakeDepth, 0.001f)) - 0.01f);

        /// <summary>
        /// Kroky 1–4: domain warp, continentalness / erosion / weirdness,
        /// složení weirdness na peaks &amp; valleys a spline mapování na metry.
        /// </summary>
        public static void EvalMacro(float2 p, in GenParams gp, in SplineSet spl,
                                     out float macroY, out float shape,
                                     out float cont, out float eros, out float pv)
        {
            // 1) sdílený warp – jeden pro všechna makro pole, aby zůstala vzájemně korelovaná
            float2 w = p + gp.warpAmp * new float2(
                GenNoise.Fbm2(p * gp.warpFreq, 3, gp.offWarpX),
                GenNoise.Fbm2(p * gp.warpFreq, 3, gp.offWarpZ));

            // 2) tři nezávislá pole
            cont = GenNoise.Fbm2(w * gp.contFreq,  gp.contOct,  gp.offCont);
            eros = GenNoise.Fbm2(w * gp.erosFreq,  gp.erosOct,  gp.offEros);
            float weird = GenNoise.Fbm2(w * gp.weirdFreq, gp.weirdOct, gp.offWeird);

            // 3) složení trojúhelníkovou funkcí: hřebeny se stanou souvislými pásy
            //    a extrémy jsou stejně pravděpodobné jako střed
            pv = 1f - abs(3f * abs(weird) - 2f);

            // 4) spliny – tady se rozhoduje o rázu světa, ne ve frekvencích.
            //    shape je BEZROZMĚRNÝ násobič členitosti, teprve reliefAmp z něj dělá metry.
            shape = spl.ampE.Eval(eros) * spl.ampC.Eval(cont);
            macroY = spl.baseC.Eval(cont) + gp.reliefAmp * shape * spl.pv.Eval(pv);

            // 4b) tektonický zdvih – masivní pohoří
            //
            // Zvednout reliefAmp by NEUDĚLALO hory: zvedl by se rovnoměrně celý svět a
            // z krajiny by byla jedna velká vrásnitá deka bez nížin. Ridged šum má hřebeny
            // jako souvislé čáry, ne kupy; umocněním se pásma zúží a mezi nimi zůstane
            // rovina, takže pohoří je UDÁLOST v krajině, ne její průměr.
            if (gp.upliftAmp > 0.01f)
            {
                float ridge = pow(saturate(GenNoise.Ridged2(w * gp.upliftFreq, gp.upliftOct, gp.offUplift)),
                                  gp.upliftSharp);

                // Jen ve vnitrozemí – řetěz vystupující rovnou z moře by rozbil pobřeží.
                // A jen na málo erodovaném terénu, ať staré kraje zůstanou ploché.
                float inland = smoothstep(gp.upliftContMin, gp.upliftContMin + 0.35f, cont);
                float young = 1f - smoothstep(0.10f, 0.75f, eros * 0.5f + 0.5f);

                float uplift = ridge * inland * lerp(0.30f, 1f, young);

                // 4c) kolo 27: monumentální masivy. Vzácná hladká regionální maska (fbm se 2
                // oktávami, perioda ~11 km) má plynulý náběh i ústup, takže masiv nikde nezačíná
                // hranou. Uvnitř se zvedne celá plocha (kořen – i údolí masivu leží vysoko) a vlastní
                // ridged pole na ní dělá hřebeny, sedla a údolí. Mimo masivy se klasický zdvih ztlumí:
                // hory jsou událost v krajině a nížiny, pláně a pobřeží zůstávají čitelné.
                // massifAmp = 0 → přesně původní výpočet (kolo 26).
                float massif = 0f;
                if (gp.massifAmp > 0.01f)
                {
                    float region = GenNoise.Fbm2(w * gp.massifFreq, 2, gp.offMassif);
                    float mask = smoothstep(gp.massifLo, gp.massifHi, region)
                               * smoothstep(gp.upliftContMin + 0.25f, gp.upliftContMin + 0.70f, cont);   // jen vnitrozemí – pobřeží beze změny
                    float mr = pow(saturate(GenNoise.Ridged2(w * gp.massifRidgeFreq, gp.massifRidgeOct, gp.offMassifRidge)),
                                   gp.massifSharp);
                    massif = mask * lerp(gp.massifBase, 1f, mr) * lerp(0.55f, 1f, young);
                    uplift *= lerp(gp.massifOutside, 1f + gp.massifUpliftBoost, mask);
                    macroY += gp.massifAmp * massif;
                }

                macroY += gp.upliftAmp * uplift;

                // Kolo 27: měkký strop – vzácný souběh všech polí nesmí narazit na strop světa
                // (VoxelWorld.WorldMaxY), jinak by vrchol uřízl chunk. Pod massifTop je výška beze změny,
                // nad ním se plynule přimyká k massifTop + massifTopRange (žádná plošina, jen zaoblení).
                if (gp.massifAmp > 0.01f && macroY > gp.massifTop)
                    macroY = gp.massifTop + gp.massifTopRange * tanh((macroY - gp.massifTop) / gp.massifTopRange);

                // Na horách je hrubší i povrchový detail – jinak by obří masiv vyšel hladký
                // jako kopeček, protože detail se škáluje právě přes shape.
                shape *= 1f + gp.upliftShape * uplift;
                if (massif > 0f) shape *= 1f + gp.massifShape * massif;
            }

            // Kolo 29, A/B varianta B1 (craterMode 1): kráter přímo v makru. Hydrologie (HydroHeightJob) ho pak vidí jako
            // bezodtokou pánev a terasy suchého pásma ho zasáhnou – jen pro pilotní měření, výchozí je craterMode 2 (krok 9b).
            if (gp.craterMode == 1 && CraterMath.Near(p, gp.offMicro, out _, out _, out _))
            {
                Climate(p, gp, out float cT, out float cH);
                macroY += CraterMath.TerrainDelta(p, macroY, cT, cH, gp.seaLevel, gp.offMicro, 1f);
            }
        }

        /// <summary>Sklon makro terénu spočtený analyticky (4 vzorky). Pro body mimo mřížku.</summary>
        public static float MacroSlopeAt(float2 p, float h, in GenParams gp, in SplineSet spl)
        {
            EvalMacro(p + new float2(h, 0f), gp, spl, out float e,  out _, out _, out _, out _);
            EvalMacro(p - new float2(h, 0f), gp, spl, out float wv, out _, out _, out _, out _);
            EvalMacro(p + new float2(0f, h), gp, spl, out float n,  out _, out _, out _, out _);
            EvalMacro(p - new float2(0f, h), gp, spl, out float s,  out _, out _, out _, out _);
            return length(new float2((e - wv) / (2f * h), (n - s) / (2f * h)));
        }

        /// <summary>
        /// Rozhodne, jestli v dané Worley buňce je jezero, a s jakou hladinou.
        /// Vše závisí VÝHRADNĚ na ID buňky, takže se na tom shodne libovolný chunk bez komunikace.
        /// </summary>
        public static LakeBody EvalLakeCell(int2 cell, in GenParams gp, in SplineSet spl)
        {
            LakeBody lb = default;
            int lakeSeed = gp.seed ^ 0x5A17E;
            uint id = GenNoise.Hash(cell, lakeSeed);
            if (GenNoise.Hash01(id) >= gp.lakeChance) return lb;

            float2 c = GenNoise.CellCenter(cell, gp.lakeCell, lakeSeed);
            EvalMacro(c, gp, spl, out float my, out _, out _, out _, out _);
            if (my <= gp.lakeMinY || my >= gp.lakeMaxY) return lb;
            if (MacroSlopeAt(c, 6f, gp, spl) >= gp.lakeFlatMax) return lb;

            uint h2 = GenNoise.Hash(id ^ 0x9E3779B9u);
            uint h3 = GenNoise.Hash(h2 ^ 0xC2B2AE35u);
            uint h4 = GenNoise.Hash(h3 ^ 0x27D4EB2Fu);

            lb.active = 1;
            lb.center = c;
            lb.radius = lerp(gp.lakeRadiusMin, gp.lakeRadiusMax, GenNoise.Hash01(h2));
            lb.waterY = my + gp.lakeShoreOffset;   // konstanta na buňku → žádné švy mezi chunky
            lb.angle = GenNoise.Hash01(h3) * 6.2831853f;
            lb.stretch = lerp(1f, gp.lakeStretchMax, GenNoise.Hash01(h4));
            return lb;
        }

        /// <summary>
        /// Vliv jednoho jezera na daný bod. Vrací 0, pokud bod leží mimo pánev.
        ///
        /// Tvar NENÍ kruh. Skládá se ze tří věcí:
        ///  1. natočený ovál – protažení se aplikuje jen jako ZÚŽENÍ (stretch &gt;= 1),
        ///     takže deformace nikdy nezvětší dosah jezera a limit půlky Worley buňky drží;
        ///  2. domain warp lokální souřadnice – zálivy, poloostrovy, členitý břeh;
        ///  3. samotná linie břehu vzniká až v EvalSurface průnikem terénu s hladinou,
        ///     takže ji dokresluje reliéf, ne tahle funkce.
        /// </summary>
        public static float LakeBasin(float2 p, in LakeBody lb, in GenParams gp)
        {
            if (lb.active == 0) return 0f;

            float2 v = p - lb.center;

            // Poloosy oválu jsou radius/sqrt(stretch) a radius*sqrt(stretch),
            // takže protažení mění tvar, ale NE plochu jezera.
            float rr = lb.radius * sqrt(lb.stretch);

            // Hrubý ořez, ať se warp nepočítá pro celý chunk. Faktor 1.5 pokrývá to,
            // že warp je vektor – jeho velikost může být až sqrt(2)x složka.
            float rough = rr * (1f + 1.5f * gp.lakeWarpAmp);
            if (v.x * v.x + v.y * v.y >= rough * rough) return 0f;

            // 1) natočení a protažení
            float ca = cos(lb.angle), sa = sin(lb.angle);
            float2 loc = new float2(v.x * ca + v.y * sa, -v.x * sa + v.y * ca);
            loc.x *= lb.stretch;

            // 2) deformace břehu; frekvence se škáluje poloměrem, aby malá jezera
            //    nebyla rozdrobená a velká nebyla hladká
            if (gp.lakeWarpAmp > 0.0001f)
            {
                float wf = gp.lakeWarpDetail / max(rr, 1f);
                loc += rr * gp.lakeWarpAmp * new float2(
                    GenNoise.Fbm2(p * wf, 3, gp.offLakeWarpX),
                    GenNoise.Fbm2(p * wf, 3, gp.offLakeWarpZ));
            }

            float d = length(loc);
            if (d >= rr) return 0f;
            return 1f - smoothstep(0.55f * rr, rr, d);
        }

        /// <summary>
        /// Kroky 5–8: detail, řeky, jezera, terasování.
        ///
        /// <para>Sklon, vliv Worley jezera a hydrologie přicházejí zvenčí. Dávková cesta je
        /// bere z mřížky a z regionu, bodová cesta analyticky – ale sama matematika je
        /// jedna, takže se obě cesty nemohou rozejít.</para>
        /// </summary>
        /// <param name="flow">Nejbližší koryto z regionální makro-mapy. Neplatný = bez řek.</param>
        /// <param name="hydroLakeY">Hladina bezodtoké pánve, nebo <see cref="ColumnField.NoLake"/>.</param>
        /// <summary>
        /// Klima v bodě: teplota a vlhkost, obě 0–1.
        ///
        /// <para>Je to jedno jediné místo schválně. Tentýž vzorec potřebuje mřížkový job,
        /// bodový dotaz pro spawn i <c>BiomeAt</c> pro konzoli – kdyby si ho každý psal sám,
        /// stačilo by jedno opomenuté <c>* 0.5f + 0.5f</c> a hráč by stál v jiném biomu,
        /// než jaký by mu hlásila konzole. Klima nezávisí na tvaru terénu, jen na poloze.</para>
        /// </summary>
        public static void Climate(float2 p, in GenParams gp, out float temp, out float hum)
        {
            temp = GenNoise.Fbm2(p * gp.tempFreq, gp.climateOct, gp.offTemp) * 0.5f + 0.5f;
            hum  = GenNoise.Fbm2(p * gp.humFreq,  gp.climateOct, gp.offHum)  * 0.5f + 0.5f;
        }

        /// <summary>
        /// Suchost kraje, 0–1: horko A zároveň sucho. Tvoří mesy, kaňony a holé stěny.
        /// </summary>
        /// <para>Rampy jsou schválně úzké kolem středu: klima je fbm šum, takže se drží
        /// kolem 0,5 a hodnoty pod 0,2 nebo nad 0,8 jsou vzácné. S širokou rampou by suchost
        /// nikdy nedosáhla plné síly a rozdíl mezi pouští a lesem by zůstal jen v barvě.</para>
        public static float Aridity(float temp, float hum)
            => saturate((temp - 0.50f) * 4f) * saturate((0.50f - hum) * 4f);

        /// <summary>Vlhkost kraje, 0–1, jako protipól suchosti. Měkčí svahy, bažiny.</summary>
        public static float Wetness(float hum) => saturate((hum - 0.52f) * 4f);

        /// <summary>Kolo 12: polovina šířky náběhu stupně u slabého terasování (0,5 = bez stupně).</summary>
        public const float SoftTerraceSharp = 0.42f;

        /// <summary>Kolo 12: rozsah síly terasování, přes který stupeň nabývá plné výšky i ostrosti (mesy).</summary>
        public const float SoftTerraceFrom = 0.30f, SoftTerraceTo = 0.75f;

        public static float EvalSurface(float2 p, in GenParams gp,
                                        float macroY, float shape, float cont, float eros, float pv,
                                        float slope, float temp, float hum, float sampleStep,
                                        in HydroFlow flow,
                                        float lakeWaterY, float lakeBasin,
                                        out float riverCore, out float riverY, out float cliff,
                                        out float hydroLakeY, in CraterCell crater)
        {
            float e01 = eros * 0.5f + 0.5f;

            // ── 5) detail ──────────────────────────────────────────────
            float rock  = GenNoise.Ridged2(p * gp.ridgeFreq, gp.ridgeOct, gp.offRidge);
            float grain = GenNoise.Fbm2(p * gp.grainFreq, gp.grainOct, gp.offGrain);
            // Detail se škáluje bezrozměrným shape – ridgeAmp a grainAmp jsou už v metrech.
            float y = macroY + shape * (gp.ridgeAmp * rock + gp.grainAmp * grain);
            float detail29 = y - macroY;   // kolo 29: jemný povrchový detail pro dno kráteru (bez teras a koryt)

            // ── 6) řeky ────────────────────────────────────────────────
            //
            // Koryto přichází z regionální makro-mapy: D8 směry odtoku a akumulace toku nad
            // reliéfem se zaplněnými pánvemi. Sampler už vrátil vzdálenost k ose nejbližšího
            // toku, jeho šířku podle plochy povodí a hladinu interpolovanou PO PROUDU.
            //
            // Proti staré údolní metodě z toho padá zadarmo přesně to, co jí chybělo: tok má
            // SMĚR, takže se přítoky slévají do jednoho koryta místo aby se křížily, řeka
            // nikdy nevede do kopce, a šířka odpovídá tomu, kolik vody nese – ne tomu, jak
            // hluboké je zrovna údolí.
            // Suchost se počítá už tady: kaňon NENÍ terasa, je to řeka zaříznutá do suchého
            // plató. V suchu chybí porost i půda, které jinde svah zaoblí, takže se koryto
            // zařízne hlouběji a údolí zůstane úzké. Ve vlhku je to obráceně – široká měkká
            // niva. Tohle je ten rozdíl, kvůli kterému vypadá poušť jinak než les i tvarem.
            float aridity = Aridity(temp, hum);

            riverCore = 0f;
            riverY = gp.seaLevel;

            // Jak moc je bod jezerem z hydrologie, 0–1. Potřebuje to už řeka (hráz se do
            // jezera nestaví), takže se to počítá tady; samotné jezero je až v kroku 6c.
            //
            // VŠECHNO tu musí být spojitá funkce polohy: místní hloubka zaplnění je
            // bilineární, hladina S je v rámci jednoho jezera konstanta. Jakýkoli přepínač
            // odvozený z okna buněk kreslil v terénu pravoúhlé schody po 32 m.
            float lakeS = flow.lakeLevel;
            bool inBasin = flow.lakeDepth > 0f && lakeS > gp.seaLevel + LakeMinAboveSea;
            float lakeBand = max(gp.hydroLakeFade, 1f);
            float lakeT = inBasin
                ? smoothstep(gp.hydroLakeMin - lakeBand, gp.hydroLakeMin + lakeBand, flow.lakeDepth)
                : 0f;

            // Ústí. Akumulace toku pokračuje i po mořském dně – všechno se sbíhá k pobřeží,
            // takže nejsilnější koryta vycházejí právě u ústí a bez pojistky by se v šelfu
            // vyryly příkopy.
            //
            // Dřív tu byla brána `land`, která pod pobřežím vypnula CELÝ blok. Jenže tím se
            // nevypnuly jen příkopy, ale i koryto a hladina: `riverCore` se násobilo `land`,
            // takže už kolem tří metrů nad mořem kleslo pod práh, od kterého se kreslí voda.
            // Řeka se proto usekla kus před pobřežím a poslední úsek zůstal jako suchá rýha.
            //
            // Teď se odděluje TVAR od HLOUBKY. Koryto se řeže plnou silou až pod hladinu,
            // ale cíl ořezu se u moře zvedá k `riverMouthDepth` a nikdy neklesne pod
            // `seaLevel - riverMouthDepth`. Z ústí je tím rozevřená mělčina, ne kanál,
            // a v hlubokém šelfu se nestane vůbec nic – ořez je přes min(), takže dno
            // hlubší než ústí zůstává, jaké bylo.
            float submerge = smoothstep(gp.seaLevel + 4f, gp.seaLevel - 2f, macroY);

            // Hráz a koryto se jen PŘIPRAVÍ tady a aplikují se až úplně na konci (krok 10).
            // Terasování, bažiny i čedič po nich ještě terén posouvají a terasa na břehu ho
            // uměla srazit o celý stupeň pod hladinu – řeka pak zase visela nad srázem.
            float levee = 0f, crest = 0f, bed = 0f, shelfBed = 0f, leveeFrom = 0f, leveeSlope = 0f;
            float sill = 0f, sillY = 0f;
            float shelf = 0f;

            if (flow.Found)
            {
                // Koryto užší než krok vzorkování by se mezi vzorky propadlo: v hrubém LOD
                // (voxel 4–16 m) padnou všechny vzorky mimo osu, takže by řeka zmizela –
                // ale údolní mísa, která je řádově širší, by zůstala. Odtud „vyhloubené
                // koryto bez vody" v dálce. Šířka se proto zdola drží nad krokem mřížky;
                // dno i hladina se rozšíří SPOLEČNĚ, takže voda zůstane nad dnem.
                float half = max(max(flow.width * 0.5f, 1f), 0.55f * sampleStep);
                float bank = max(half * gp.riverBankRatio, 2f);

                // Břeh se po délce toku vlní – jinak je hrana koryta přesně rovnoběžná s osou
                // a na svahu z ní je rovná rýha jako od bagru. Šířka koryta se mění jen málo
                // (±12 %), šířka náběhu břehu víc (±45 %): střídají se strmé a pozvolné
                // úseky jako u skutečné řeky. Šum je nízkofrekvenční a nezávisí na LOD.
                float bn = GenNoise.Fbm2(p * 0.035f, 2, gp.offGrain + new float2(71.3f, 19.9f));
                half *= 1f + 0.12f * bn;
                bank *= 1f + 0.45f * bn;

                // U profil místo V: plné dno do poloviny šířky, pak náběh na břeh. Náběh je
                // široký úměrně korytu, takže se veletok nezařízne jako příkop.
                riverCore = 1f - smoothstep(half, half + bank, flow.distance);

                // Hladina nikdy pod moře. Je to zaplněná makro výška v ose toku, která je po
                // proudu neklesající z definice Priority-Flood – voda proto nemůže téct do
                // kopce ani se naklonit do stran. To byla bolest obou předchozích metod.
                //
                // U ústí vyjde přesně seaLevel, takže říční a mořská hladina jsou tatáž
                // rovina a napojení nemá šev, ať se buňka klasifikuje jako řeka, nebo moře.
                riverY = max(gp.seaLevel, flow.waterY);

                // U ústí se hladina řeky SPOJITĚ stáhne na hladinu moře. Zaplněná makro
                // výška v ose je na 32m buňkách a těsně u moře vychází o půl metru až metr nad
                // ním – na styku s mořem pak vznikaly schody hladiny a svislé stěny vody.
                // Váží se vlastní hladinou toku v ose, ne makro výškou bodu: ta je napříč
                // korytem stejná, takže se řeka nenakloní do strany, jen po proudu.
                riverY = lerp(riverY, gp.seaLevel,
                              smoothstep(gp.seaLevel + 6f, gp.seaLevel + 1f, flow.waterY));

                float depth = clamp(flow.width * gp.riverDepthRatio,
                                    gp.riverDepthMin, gp.riverDepthMax)
                            * lerp(1f, gp.aridIncision, aridity);

                // Pod hladinou se koryto změlčí na hloubku ústí a dno se zarazí o mořské dno.
                depth = lerp(depth, gp.riverMouthDepth, submerge);
                bed = max(riverY - depth, gp.seaLevel - gp.riverMouthDepth);

                // 6a) Údolní mísa.
                //
                // Cíl ořezu NENÍ konstantní výška nad hladinou – to by z každého horského
                // toku udělalo plochý žlab vysekaný do masivu. Okraj mísy se zvedá se
                // vzdáleností, takže z toho je průřez údolí: dno u řeky, stoupající svahy,
                // a nahoře terén nedotčený.
                //
                // Do moře se mísa nepromítá: pod hladinou by z ní byla plochá vana vysekaná
                // do šelfu, a právě to `land` kdysi hlídalo. Tady tu úlohu přebírá submerge.
                // Dosah mísy je shora omezený tím, kam sampler umí hladinu spojitě míchat
                // (HydroSampler.BlendReach). Dál se nejbližší úsečka toku přepíná skokem
                // a s ní i výška, ke které se mísa srovnává – ve svahu z toho byla souvislá
                // stěna od řeky pryč (na vnitřní straně každého ohybu).
                //
                // Mísa čte SMÍCHANÉ hodnoty toku (valley*), ne ty pro koryto: daleko od osy
                // se nejbližší úsečka přepíná a s ní by skákala výška, ke které se srovnává.
                float vHalf = max(max(flow.valleyWidth * 0.5f, 1f), 0.55f * sampleStep);
                float vBank = max(vHalf * gp.riverBankRatio, 2f);
                float vLevel = max(gp.seaLevel, flow.valleyY);
                vLevel = lerp(vLevel, gp.seaLevel, smoothstep(gp.seaLevel + 6f, gp.seaLevel + 1f, flow.valleyY));
                float valleyR = min(vHalf + vBank
                                    + flow.valleyWidth * gp.riverValleyWidth * lerp(1f, 0.40f, aridity),
                                    HydroSampler.BlendReach - 4f);
                float valley = (1f - smoothstep(vHalf + vBank, max(valleyR, vHalf + vBank + 1f), flow.valleyDistance))
                             * (1f - submerge);

                if (valley > 0.001f)
                {
                    float rim = (1f - valley) * flow.valleyWidth * gp.riverValleyWidth * 0.35f;
                    // V suchu se mísa jen naznačí – stěna kaňonu má zůstat stát, ne se rozlít
                    // do nivy. Terasování ji pak rozseká na vodorovné lavice.
                    y = lerp(y, min(y, vLevel + gp.riverBankHeight + rim),
                             valley * gp.riverValleyFlatten * lerp(1f, 0.45f, aridity));
                }

                // 6a½) Hráz. Břeh těsně za korytem nesmí ležet POD hladinou řeky – jinak
                //      řeka „visí": voda je ohraničená silou koryta, ne terénem, a její boční
                //      hrana plave metry nad okolím. Stává se to tam, kde osa toku vede přes
                //      zaplněnou mělkou prohlubeň (hladina = přeliv, okolí je níž). Terén se
                //      proto do šířky břehu jen ZVEDNE na hladinu + rezervu; kde je výš, nic
                //      se nemění. Koryto se pak zařízne do hráze krokem 6b, takže břehová čára
                //      leží na svahu hráze a řeže ji izolinie, ne maska.
                //
                //      Ne u ústí (hladina řeky tam splývá s mořem, takže nemá co viset
                //      a ústí musí zůstat otevřené) a ne v jezerech – tam by z řeky vedly
                //      přes hladinu dvě dlouhé hráze. Řeka tekoucí podél pobřeží výš než
                //      moře hráz dostane i na mořské straně; bez ní by stékala do moře stěnou.
                // Hráz NENÍ plochý pás se strmým náběhem (tak vypadala jako umělý násep).
                // Je to mírný svah od břehu ven se sklonem kolem 1:3 a vlnitým hřebenem –
                // naplavený val, jaký řeka staví sama. Kde je okolí jen o kousek níž, splyne
                // se svahem za pár metrů; kde je hodně níž, protáhne se dál, ale nikdy
                // nevznikne stěna. Sklon i výška hřebene se mírně vlní nízkofrekvenčním
                // šumem, aby val nebyl pravítkem rovný. Nezávisí na kroku vzorkování, takže
                // ho všechny LOD staví stejně.
                levee = smoothstep(0.5f, 2.5f, riverY - gp.seaLevel)
                      * (1f - saturate(lakeBasin * 4f))
                      * (1f - lakeT);
                float wob = GenNoise.Fbm2(p * 0.021f, 2, gp.offGrain + new float2(311.7f, 97.3f));
                // Na peřejích a vodopádech stoupá hladina i břeh stejně prudce a izolinie
                // mezi nimi se třepí do zubů – břeh se tam zvedá úměrně spádu toku.
                crest = riverY + RiverLeveeRise + 0.35f * wob + min(4f, flow.waterSlope * 6f);
                leveeFrom = half + bank;
                leveeSlope = RiverLeveeSlope * (1f + 0.35f * wob);

                // Mělčina u břehu: dno se k okraji koryta zvedá k hladině − ShelfDepth.
                // Průřez pak není vana (plné dno a pak rovný svah), ale hluboká osa, mělký
                // okraj a pozvolný břeh – čitelné i v low-poly.
                shelf = smoothstep(half * 0.55f, half * 1.05f, flow.distance);
                shelfBed = max(bed, riverY - RiverShelfDepth);

                // 6a¾) Kolo 7 – práh na horní straně hydrologického schodu.
                //
                //      Sampler míchá hladinu koryta jen mezi úsečkami s podobnou výškou, údolí
                //      mezi všemi. Kde vedou dva úseky s rozdílem hladiny přes metr těsně vedle
                //      sebe (ohyb nad vodopádem, peřej), přepne se hladina koryta na Voronoiově
                //      hranici skokem, ale smíchaná hladina údolí ne. Rozdíl riverY − vLevel je
                //      proto SPOJITÝ signál: kladný jen na horní straně schodu, u hranice
                //      ~polovina skoku, do ~8 m od ní zpátky na nulu; na dolní straně záporný.
                //
                //      Mělký okraj horního úseku tam dřív ležel skoro v úrovni vlastní hladiny
                //      hned vedle terénu o dva metry níž – a hladina mezi nimi se natáhla do
                //      šikmých střepů. Teď se břeh horního úseku zvedne nad jeho hladinu
                //      (práh), takže horní voda končí u terénu, ne na hraně schodu. Osa koryta
                //      zůstává otevřená (spád vodopádu/peřeje teče dál korytem), dolní strana,
                //      ústí (submerge) a jezera se nemění. Jen zvedá.
                float jumpUp = riverY - vLevel;
                sill = gp.hydroSill * smoothstep(HydroSillStart, HydroSillFull, jumpUp)
                     * smoothstep(half * 0.35f, half * 0.85f, flow.distance)
                     * smoothstep(0f, 0.2f, riverCore)   // jen koryto a břeh, ne suchá souš za ním
                     * (1f - submerge) * (1f - saturate(lakeBasin * 4f)) * (1f - lakeT);
                sillY = riverY + HydroSillRise;
            }

            hydroLakeY = ColumnField.NoLake;

            // ── 7) jezero ──────────────────────────────────────────────
            //     Cíl ořezu klesá od (hladina + lakeShoreRise) na okraji pánve
            //     k (hladina - hloubka) uprostřed. Díky tomu se terén u břehu NEsrovná
            //     přesně na hladinu – břehová čára vzniká až tam, kde reliéf sám protne
            //     vodu, a je proto nepravidelná. S lakeShoreRise = 0 se vrátí plochý prstenec.
            if (lakeBasin > 0f)
            {
                // Okraj pánve se nejdřív zvedne nad hladinu tam, kde by okolní terén ležel
                // níž – jezero na svahu by jinak přetékalo přes nižší stranu a hladina by tam
                // končila ve vzduchu. Plná síla od WorleyWetBasin (≈ 0,3), tedy právě tam, odkud
                // se voda kreslí; dál ven plynule odeznívá. Jen zvedá.
                float rimW = smoothstep(0f, WorleyWetBasin(gp), lakeBasin);
                if (y < lakeWaterY + LakeRimRise)
                    y = lerp(y, lakeWaterY + LakeRimRise, rimW);

                float target = lakeWaterY + gp.lakeShoreRise * (1f - lakeBasin)
                                          - gp.lakeDepth * lakeBasin;
                // Na samém okraji pánve se neořezává vůbec, uvnitř plnou silou.
                y = lerp(y, min(y, target), saturate(lakeBasin * 2.5f));
            }

            // ── 7b) bažiny ─────────────────────────────────────────────
            //
            // Horká vlhká NÍŽINA se srovná těsně nad hladinu. Není to nový biom, je to
            // tvar: mokřad je plochý, protože voda nemá kam odtéct. Bez toho měla džungle
            // úplně stejný kopcovitý reliéf jako les o kus dál a lišila se jen barvou.
            //
            // Výška se nesrovná na jednu rovinu, ale zbytek převýšení se zmáčkne na čtvrtinu:
            // plocha zůstane mírně zvlněná, takže z ní nevznikne betonová deska a mezi
            // vyvýšeninami se udrží voda z řek a pánví.
            float wet = Wetness(hum);
            float swamp = gp.swampStrength * wet * saturate((temp - 0.50f) * 2.5f)
                        * (1f - smoothstep(gp.seaLevel + 5f, gp.seaLevel + 28f, y))
                        * (1f - riverCore) * (1f - saturate(lakeBasin * 4f));

            if (swamp > 0.001f)
            {
                float flat = gp.seaLevel + 2.5f + (y - gp.seaLevel) * 0.25f;
                y = lerp(y, flat, swamp);
            }

            // ── 8) terasování → plošiny se svislými stupni ─────────────
            //     Do koryta ani do jezera se neterasuje, jinak by voda skákala po schodech.
            //
            //     Sílu i výšku stupně řídí KLIMA. Bez toho mělo terasování jednu globální
            //     hodnotu, takže poušť i hvozd měly stejné schody a rozdíl mezi biomy končil
            //     u barvy. V suchu skála není ničím krytá a odpadává v deskách – stupně jsou
            //     vyšší a výraznější, což dělá mesy a kaňonové stěny. Ve vlhku je svah pokrytý
            //     půdou a porostem, takže se stupně mažou do plynulého svahu.
            float climateCliff = lerp(1f, gp.aridCliffBoost, aridity) * lerp(1f, gp.humidCliffDamp, wet);

            cliff = saturate(gp.cliffStrength * climateCliff)
                  * saturate((1f - e01) * smoothstep(gp.cliffSlopeMin, gp.cliffSlopeMax, slope))
                  * (1f - riverCore) * (1f - saturate(lakeBasin * 4f));

            if (cliff > 0.001f)
            {
                float stepH = gp.stepHeight * lerp(1f, gp.aridStepBoost, aridity);
                float t = y / stepH;
                // Kolo 12: ostrý stupeň (náběh cliffSharp = 8 % výšky) patří jen tam, kde je
                // terasování silné – mesy a kaňony v suchu. Slabé terasování (běžné louky, svahy,
                // pobřeží, síla 0,05–0,4) dělalo lerp(y, stepped, cliff) úzký strmý pás každých
                // 7 m výšky: na svahu 0,3 měl sklon ~1,3, přes práh skály dostal hnědou barvu
                // a z dálky z toho byly soustředné vrstevnice přes celý kopec. Slabá terasa se
                // proto plynule vytrácí (váha w) a zbytek má měkký náběh; silná zůstává, jak byla.
                float w = gp.softTerrace > 0.5f ? smoothstep(SoftTerraceFrom, SoftTerraceTo, cliff) : 1f;
                float sharp = lerp(SoftTerraceSharp, gp.cliffSharp, w);
                float stepped = (floor(t) + smoothstep(0.5f - sharp, 0.5f + sharp, frac(t)))
                                * stepH;
                // Kolo 12b: v měkkém pásu (w < 1) dělal zbylý měkký stupeň pořád periodické
                // zvlnění sklonu (na kupce se silou 0,5 sklon 0,8× až 1,4× každých ~5 m) –
                // z dálky rovnoběžné vodorovné pruhy přes celý svah. Váha se proto v měkkém
                // pásu bere w² (sklon ±10 %), silné mesy (w → 1) zůstávají beze změny.
                float wt = gp.Calm(32) && gp.softTerrace > 0.5f ? w * w : w;
                y = lerp(y, stepped, cliff * wt);
            }

            // ── 9) mikro-biom: čedičové pole ───────────────────────────
            //
            // Až tady, na hotovém povrchu: sloupcová odlučnost je POVRCHOVÝ jev, popraskaná
            // vychladlá láva. Kdyby se počítala dřív, přejelo by ji terasování a řeky by se
            // do ní zařízly hladkým korytem, jako by tam žádné sloupy nebyly.
            //
            // Do vody se nesahá ze stejného důvodu jako u teras: schody pod hladinou nikdo
            // neuvidí a jen by rozházely dno.
            MicroBiomeMath.Sample(p, gp.Micro, out int microKind, out float microW);

            if (microKind == (int)MicroBiome.BasaltField && microW > 0.002f)
            {
                float shield = (1f - riverCore) * (1f - saturate(lakeBasin * 4f));
                y = MicroBiomeMath.BasaltColumns(p, y, microW * shield, sampleStep,
                                                 gp.seaLevel, gp.seed);
            }

            // ── 9b) kolo 29: meteorický kráter (craterMode 2) ──────────────
            //
            // Až na hotovém povrchu po terasách (jako čedičové pole): v suchu by terasování z valu udělalo schody a
            // pruhy. Hydrologie čte jen EvalMacro, takže řeky, jezera a jejich povodí zůstávají bitově stejné – kráter
            // je čistě lokální (mimo 2,35 R přesně 0). Do koryta a jezera se nesahá (shield).
            if (gp.craterMode == 2 && crater.valid != 0)
            {
                // u řeky se kráter plynule vytrácí (osa ± pól šířky + 6 → 40 m): koryto, břehy i hladina zůstanou přesně jako dřív,
                // řeka teče přirozeným průlomem ve valu
                float riverGap = flow.width > 0.01f ? smoothstep(0.5f * flow.width + 6f, 0.5f * flow.width + 40f, flow.distance) : 1f;
                float cShield = (1f - riverCore) * riverGap * (1f - saturate(lakeBasin * 4f)) * (1f - smoothstep(0f, 1.5f, flow.lakeDepth));
                float bw29 = CraterMath.Shape(ref y, p, detail29, crater, cShield);
                cliff *= 1f - bw29;   // vyrovnaný kráter nemá terasy – ani v barvě
            }

            // ── 10a) jezera z bezodtokých pánví ────────────────────────
            //
            // Priority-Flood je vrací zadarmo: kde je zaplněná výška nad surovou, tam by se
            // voda skutečně držela. Dno se ale musí SROVNAT – povrchový detail kolísá o víc
            // metrů, než je mělká pánev hluboká, a bez srovnání by z jezera byly kaluže
            // rozseté po dně. Je to tentýž důvod, proč se srovnává i koryto.
            // Pánev pod hladinou moře (nebo těsně nad ní) není jezero, ale moře. Bez téhle
            // podmínky se do zálivu posadila druhá hladina o půl metru výš než moře a na jejím
            // okraji byl v otevřené vodě schod.
            //
            // Profil pánve podle místní hloubky zaplnění d (lakeT je 0 na mělčině, 1 hluboko):
            //   cíl T(d) = S + LakeRimRise   na mělčině  →  S − 1,5 m   hluboko
            //   mělčina se k cíli jen ZVEDÁ, hlubina jen SNIŽUJE, přechod je hladký.
            //
            // Dřív se hladina kreslila až od hloubky hydroLakeMin + fade/2, ale okraj pánve
            // pod tím ležel pořád metry POD hladinou (makro terén je v celé pánvi pod
            // přelivem). Hladina tak končila svislou hranou nad jámou. Teď je okraj, kde
            // voda není, zvednutý nad hladinu, a břehová čára vychází tam, kde cíl hladinu
            // protne – na hladké vrstevnici bilineární hloubky, ne na mřížce.
            //
            // Zvedá se úměrně hloubce (rimW), takže mělká pánev, která jezerem není, se
            // jen trochu vyrovná, ne zasype do roviny.
            if (inBasin)
            {
                float target = lakeS + LakeRimRise - (LakeRimRise + 1.5f) * lakeT;

                // Zvedá se jen úzký prstenec těsně před vodou (poslední ~1,5 m hloubky
                // zaplnění), ne celý mělký okraj pánve. Dřív se zvedal celý – z mělkých pánví
                // pak byly rovné písčité plošiny v úrovni přelivu s maličkou louží uprostřed
                // a pánve, které jezerem vůbec nejsou, se zbytečně srovnávaly. Kladný detail
                // terénu se do prstence propisuje, aby nebyl jako vylitý beton.
                float dStart = max(gp.hydroLakeMin - lakeBand, 0.5f);
                float rimW = smoothstep(max(dStart - 1.5f, 0f), dStart, flow.lakeDepth);
                float bump = 0.5f * max(0f, y - macroY);
                float raised = lerp(y, max(y, target + bump * (1f - lakeT)), rimW);
                y = lerp(raised, min(y, target), lakeT);

                // Voda tam, kde cíl klesá pod hladinu (lakeT > ~0,1); dál ven je terén
                // zvednutý nad ni, takže se hladina nerozlije. Řez vede izolinie.
                if (lakeT > 0.02f) hydroLakeY = lakeS;
            }

            // ── 10b) hráz a koryto řeky ────────────────────────────────
            //
            // Až po jezeru: koryto odtékající z jezera se tak prořízne i zvednutým okrajem
            // pánve, místo aby ho okraj zahradil.
            //
            // 6a½) Hráz. Břeh těsně za korytem nesmí ležet POD hladinou řeky – jinak řeka
            //      „visí": voda je ohraničená silou koryta, ne terénem, a její boční hrana
            //      plave metry nad okolím. Terén se proto do šířky břehu jen ZVEDNE.
            if (levee > 0.001f)
            {
                float over = max(0f, flow.distance - leveeFrom);
                float target = crest - leveeSlope * over;
                // Dál než ~40 m od břehu už val nesahá – jinak by zasypal sousední údolí.
                float reach = 1f - smoothstep(30f, 50f, over);
                if (y < target) y = lerp(y, target, levee * reach);
            }

            // 6b) Koryto se PŘEPÍŠE hladkým dnem, neodečte se od členitého terénu.
            //     Odečítání nechávalo v korytě detail, který často převýšil hloubku –
            //     dno pak vyšlo nad hladinu a řeka zůstala suchá. min() zaručí, že se
            //     terén jen zařezává, nikdy nezvedá.
            if (riverCore > 0.001f)
                y = lerp(y, min(y, lerp(bed, shelfBed, shelf)), riverCore);

            // 6c) Práh u hydrologického schodu (kolo 7, viz 6a¾) – až po korytu, jinak by
            //     ho koryto zase zařízlo zpátky do mělkého filmu.
            //     Jen mělký okraj: kde je horní voda hlubší (čelo vodopádu v korytě), práh
            //     slábne – hloubka vody se tím nemění skokem. (Aby se hladina dolního úseku
            //     netáhla šikmo nahoru po stěně prahu, řeší WaterSurface.Submerged.)
            if (sill > 0.001f)
            {
                sill *= 1f - HydroSillDepthGate * smoothstep(0.6f, 1.2f, sillY - HydroSillRise - y);
                if (y < sillY) y = lerp(y, sillY, sill);
            }

            return y;
        }
    }

    /// <summary>Kroky 1–4 na celé mřížce. Low-pass výšku dál potřebují řeky, jezera i sklon.</summary>
    [BurstCompile]
    public struct MacroFieldJob : IJobParallelFor
    {
        public float2 origin;
        public float step;
        public int side;
        public GenParams gp;
        public SplineSet spl;

        [WriteOnly] public NativeArray<float> macroY;
        [WriteOnly] public NativeArray<float> shape;
        [WriteOnly] public NativeArray<float> cont;
        [WriteOnly] public NativeArray<float> eros;
        [WriteOnly] public NativeArray<float> pv;
        [WriteOnly] public NativeArray<float> temp;
        [WriteOnly] public NativeArray<float> hum;

        public void Execute(int i)
        {
            int x = i % side;
            int z = i / side;
            float2 p = origin + new float2(x, z) * step;

            WorldGenMath.EvalMacro(p, gp, spl, out float my, out float sh,
                                   out float c, out float e, out float v);

            macroY[i] = my;
            shape[i] = sh;
            cont[i] = c;
            eros[i] = e;
            pv[i] = v;

            // Klima je nezávislé na tvaru terénu – biom už výšku neurčuje.
            WorldGenMath.Climate(p, gp, out float tC, out float hC);
            temp[i] = tC;
            hum[i]  = hC;
        }
    }

    /// <summary>
    /// Krok 7 nad Worley buňkami překrývajícími výřez. Buněk je řádově méně než bodů,
    /// takže se pět vyhodnocení makro pole na buňku vyplatí.
    /// </summary>
    [BurstCompile]
    public struct LakeJob : IJobParallelFor
    {
        public int2 cellMin;
        public int cellsX;
        public GenParams gp;
        public SplineSet spl;

        [WriteOnly] public NativeArray<LakeBody> lakes;

        public void Execute(int i)
        {
            int2 cell = cellMin + new int2(i % cellsX, i / cellsX);
            lakes[i] = WorldGenMath.EvalLakeCell(cell, gp, spl);
        }
    }

    /// <summary>Kroky 5–9 na celé mřížce.</summary>
    [BurstCompile]
    public struct SurfaceFieldJob : IJobParallelFor
    {
        /// <summary>Kolo 29: meteorický kráter zasahující do výřezu (nejvýš jeden; valid = 0 → žádný). Počítá se jednou na výřez.</summary>
        public CraterCell crater;

        public float2 origin;
        public float step;
        public int side;
        public GenParams gp;

        /// <summary>Spline LUT makra – pro analytický sklon na přímkách švu LOD.</summary>
        public SplineSet spl;

        [ReadOnly] public NativeArray<float> macroY;
        [ReadOnly] public NativeArray<float> shape;
        [ReadOnly] public NativeArray<float> cont;
        [ReadOnly] public NativeArray<float> eros;
        [ReadOnly] public NativeArray<float> pv;

        /// <summary>Klima z předchozího jobu. Počítat ho tady znovu by byl druhý šum navíc.</summary>
        [ReadOnly] public NativeArray<float> temp;

        /// <inheritdoc cref="temp"/>
        [ReadOnly] public NativeArray<float> hum;

        /// <summary>
        /// Hotová hydrologie regionu, ve kterém sloupec leží. Neplatný pohled znamená
        /// „region ještě není" – terén se pak vygeneruje bez řek. Streamer to nesmí
        /// dopustit (sloupec se neplánuje, dokud region nestojí), ale editorové cesty
        /// a náhled se s tím musí umět vyrovnat.
        /// </summary>
        public HydroRegionRef hydro;

        [ReadOnly] public NativeArray<LakeBody> lakes;
        public int2 lakeCellMin;
        public int lakeCellsX;
        public int lakeCellsZ;

        [WriteOnly] public NativeArray<float> surfY;
        [WriteOnly] public NativeArray<float> slope;
        [WriteOnly] public NativeArray<float> cliff;
        [WriteOnly] public NativeArray<float> riverCore;
        [WriteOnly] public NativeArray<float> riverY;
        [WriteOnly] public NativeArray<float2> flowDir;
        [WriteOnly] public NativeArray<float> flowSlope;
        [WriteOnly] public NativeArray<float> lakeY;

        /// <summary>Hladina nejbližší vody pro barvení břehů. Mimo dosah <c>surfY - 1000</c>.</summary>
        [WriteOnly] public NativeArray<float> shoreY;

        /// <summary>Spojitá vzdálenost od vody 0–1. Viz ColumnField.waterGate.</summary>
        [WriteOnly] public NativeArray<float> waterGate;

        /// <summary>Rozteč hranic LOD0 sloupců (viz LodSeamJob). Nula = bez šití.</summary>
        public float seamLine;

        /// <summary>Maska pro 3D vrstvu – kde smí vzniknout převis a boule.</summary>
        [WriteOnly] public NativeArray<float> overhang;

        /// <summary>
        /// Jak daleko za břeh koryta ještě sahá znalost hladiny, v metrech. Není to šířka
        /// pásu písku – ta plyne z <c>TerrainPalette.shoreSandRise</c> a ze sklonu svahu.
        /// Je to jen dosah, ve kterém se řeka vůbec počítá za „nejbližší vodu".
        /// </summary>
        private const float ShoreSandReach = 10f;

        /// <summary>Kolo 12b: šířka pásu, ve kterém 3D vrstva doznívá u přímek švu úrovně ≥ 1 (m); ≤ 16, aby navazovala v půlce mezi přímkami.</summary>
        public const float SeamGate3DBand = 14f;

        private int Idx(int x, int z) => clamp(z, 0, side - 1) * side + clamp(x, 0, side - 1);

        public void Execute(int i)
        {
            int x = i % side;
            int z = i / side;
            float2 p = origin + new float2(x, z) * step;

            // sklon z centrálních diferencí makro pole (u okraje se index ořízne)
            float dx = (macroY[Idx(x + 1, z)] - macroY[Idx(x - 1, z)]) / (2f * step);
            float dz = (macroY[Idx(x, z + 1)] - macroY[Idx(x, z - 1)]) / (2f * step);
            float sl = length(new float2(dx, dz));

            // Na přímce švu LOD musí obě strany spočítat výšku ze stejných vstupů. Sklon
            // z diferencí po vlastním kroku a minimální šířka koryta (0,55 · krok) ale
            // závisí na LOD – uzly švu pak v LOD0 a LOD1 vyšly o 0,64 m jinak (sklon 1,08
            // proti 0,67 → jiná síla útesu). Na přímce úrovně m se proto obojí počítá
            // s krokem LOD m, který mají obě sousední strany k dispozici, a v pásu kolem
            // přímky se plynule vrací k vlastnímu kroku.
            float effStep = step;
            if (seamLine > 0f)
            {
                float2 sln = round(p / seamLine) * seamLine;
                float2 sdd = abs(p - sln);
                int smx = LodSeamJob.LineLevel(sln.x, seamLine), smz = LodSeamJob.LineLevel(sln.y, seamLine);
                float swx = smx > 0 ? 1f - smoothstep(0f, LodSeamJob.Band(smx, seamLine), sdd.x) : 0f;
                float swz = smz > 0 ? 1f - smoothstep(0f, LodSeamJob.Band(smz, seamLine), sdd.y) : 0f;
                float voxel0 = seamLine / 32f;
                float hLine = max(swx > 0f ? voxel0 * (1 << smx) : 0f, swz > 0f ? voxel0 * (1 << smz) : 0f);
                float sw = max(swx, swz);
                if (sw > 0f && hLine > step * 1.5f)
                {
                    // Z mřížky, když na to okraj sloupce stačí (makro v uzlu mřížky je tatáž
                    // funkce ve stejném bodě); jinak analyticky – jemný LOD by na přímce
                    // vysoké úrovně potřeboval vzorky dál, než sahá jeho okraj sloupce.
                    int k = (int)round(hLine / step);
                    float hs;
                    if (x - k >= 0 && x + k < side && z - k >= 0 && z + k < side)
                        hs = length(new float2(macroY[z * side + x + k] - macroY[z * side + x - k],
                                               macroY[(z + k) * side + x] - macroY[(z - k) * side + x])) / (2f * k * step);
                    else
                        hs = WorldGenMath.MacroSlopeAt(p, hLine, gp, spl);
                    sl = lerp(sl, hs, sw);
                    effStep = lerp(step, hLine, sw);
                }
            }

            // nejsilnější jezero z 3x3 okolních buněk
            float basin = 0f, waterY = ColumnField.NoLake;
            int2 cc = (int2)floor(p / gp.lakeCell);

            for (int oz = -1; oz <= 1; oz++)
            for (int ox = -1; ox <= 1; ox++)
            {
                int ix = cc.x + ox - lakeCellMin.x;
                int iz = cc.y + oz - lakeCellMin.y;
                if (ix < 0 || iz < 0 || ix >= lakeCellsX || iz >= lakeCellsZ) continue;

                LakeBody lb = lakes[iz * lakeCellsX + ix];
                float b = WorldGenMath.LakeBasin(p, lb, gp);
                if (b > basin) { basin = b; waterY = lb.waterY; }
            }

            HydroFlow flow = HydroSampler.Nearest(p, hydro, gp.riverMinAccum,
                                                  gp.riverWidthMin, gp.riverWidthMax,
                                                  gp.riverAccumFull, gp.flowBlend);

            float y = WorldGenMath.EvalSurface(p, gp, macroY[i], shape[i], cont[i], eros[i], pv[i],
                                               sl, temp[i], hum[i], effStep, flow, waterY, basin,
                                               out float rc, out float rY, out float cl,
                                               out float hydroLakeY, crater);

            // Maska 3D vrstvy: nízká eroze a strmý svah. Do koryta ani do jezera převisy nechceme.
            // Okno sklonu je pevné – síla se ladí přes overhangAmp, ne dalším prahem.
            float erosion01 = eros[i] * 0.5f + 0.5f;
            float ov = saturate((1f - erosion01) * smoothstep(0.35f, 0.95f, sl)
                                   * (1f - rc) * (1f - saturate(basin * 4f)));

            surfY[i] = y;
            slope[i] = sl;
            cliff[i] = cl;
            riverCore[i] = rc;
            riverY[i] = rY;
            flowDir[i] = flow.Found ? flow.dir : new float2(0f);
            flowSlope[i] = flow.Found ? (gp.flowBlend > 0.5f ? flow.flowSlope : flow.waterSlope) : 0f;

            // Za jezero se počítá jen skutečně zatopený bod. Okraj pánve, kde terén vystoupá
            // nad hladinu, je souš – jinak by hladina vody měla zase tvar pánve. Nesmí se
            // ptát „je terén pod hladinou": ten test je zašuměný o metry, protože povrchový
            // detail kolísá víc, než je voda hluboká, a na svahu hladinu opakovaně překříží.
            // Ptáme se „chtěl tu ořez mít vodu", což je hladké.
            //
            // Cíl ořezu je waterY + shoreRise*(1-basin) - depth*basin. Pod hladinu klesne
            // od basin = shoreRise/(shoreRise+depth) a ořez je plný od basin = 0.4.
            //
            // Práh je teď přesně bod, kde ořez klesne pod hladinu (dřív o 0,09 dál, a pás mezi
            // tím byl pod hladinou, ale bez vody). Okraj pánve před ním EvalSurface zvedá nad
            // hladinu, takže se voda nerozlije na souš.
            float lakeWetMin = WorldGenMath.WorleyWetBasin(gp);

            // Worley jezero má přednost – je to autorovaný prvek s vlastním tvarem břehu.
            // Bezodtoká pánev z hydrologie doplňuje zbytek krajiny.
            lakeY[i] = basin >= lakeWetMin ? waterY : hydroLakeY;

            // ── hladina pro barvení břehů ──────────────────────────────
            //
            // Není to totéž co lakeY ani riverY. Ty odpovídají na otázku „stojí tady voda",
            // a proto končí přesně na břehové čáře. Písek má ležet AŽ NAD hladinou, takže
            // potřebuje hladinu znát i tam, kde voda není – jinak by pás písku vyšel
            // nulově široký a nebyl by vidět vůbec.
            //
            // Řeka se bere jen do vzdálenosti (koryto + břeh + dosah písku); bez toho by
            // se nejbližší tok počítal i pár kilometrů daleko a jeho hladina by posypala
            // pískem každou náhodnou terasu ve správné výšce.
            float sy = gp.seaLevel;

            if (flow.Found)
            {
                // Tatáž šířka jako u koryta výš, včetně dorovnání na krok mřížky –
                // jinak by v hrubém LOD lemoval pískem jiný pruh, než kde je voda.
                float sHalf = max(max(flow.width * 0.5f, 1f), 0.55f * effStep);
                float sBank = max(sHalf * gp.riverBankRatio, 2f);
                if (flow.distance < sHalf + sBank + ShoreSandReach)
                    sy = max(sy, rY);
            }

            // Jezerní břeh: hloubka zaplnění je kladná i kousek nad vodou (makro terén je
            // pod přelivem, nad hladinu ho vytáhne až povrchový detail), takže prstenec
            // kolem jezera hladinu zná. Práh drží písek mimo suchou pánev bez vody.
            // Hladinu zná i zvednutý prstenec kolem jezera (tam voda není, ale pláž ano).
            // Práh v hloubce zaplnění drží písek mimo mělké pánve, které jezerem nejsou.
            if (hydroLakeY > ColumnField.NoLake
                || (flow.lakeDepth > max(0.5f, gp.hydroLakeMin - max(gp.hydroLakeFade, 1f) - 1.5f)
                    && flow.lakeLevel > gp.seaLevel + WorldGenMath.LakeMinAboveSea))
                sy = max(sy, flow.lakeLevel);

            if (basin > 0.01f && waterY > ColumnField.NoLake)
                sy = max(sy, waterY);

            shoreY[i] = sy;

            // 3D vrstva (převisy, boule, jeskyně) se u vody vypíná. Hladina se staví
            // z výškového pole surfY, ale terén je izoplocha hustoty – převis nebo boule ho
            // u břehu posunou o metry. Právě z toho byly ostrůvky a pruhy terénu trčící
            // z vody a břehy, pod kterými je vidět do mezery.
            //
            // POZOR: brána musí být SPOJITÁ. První verze brala shoreY, jenže to na hranici
            // dosahu řeky skáče z hladiny řeky na hladinu moře – síla převisů tam skočila
            // a v terénu vznikla souvislá svislá stěna podél celé řeky. Proto se tu každý
            // druh vody váží plynulou blízkostí a skládá se přes min().
            float gate = smoothstep(gp.seaLevel + 1.5f, gp.seaLevel + 6f, y);

            if (flow.Found)
            {
                float gHalf = max(max(flow.width * 0.5f, 1f), 0.55f * effStep);
                float gBank = max(gHalf * gp.riverBankRatio, 2f);
                float near = 1f - smoothstep(gHalf + gBank, gHalf + gBank + ShoreSandReach, flow.distance);
                gate = min(gate, lerp(1f, smoothstep(rY + 1.5f, rY + 6f, y), near));

                // Peřej / vodopád: hladina tu klesá o metry na pár krocích, takže břeh vedle
                // spodní části spádu leží vysoko nad místní hladinou a brána výše by ho
                // pustila. Převisy a boule pak na hraně spádu dělaly zuby. Podél strmé
                // vody se proto 3D vrstva vypne celá (čistý low-poly svah z výškového pole).
                float steep = smoothstep(0.08f, 0.25f, flow.waterSlope);
                gate = min(gate, 1f - near * steep);
            }

            if (flow.lakeDepth > 0f && flow.lakeLevel > gp.seaLevel + WorldGenMath.LakeMinAboveSea)
            {
                float near = smoothstep(0f, 2f, flow.lakeDepth);
                gate = min(gate, lerp(1f, smoothstep(flow.lakeLevel + 1.5f, flow.lakeLevel + 6f, y), near));
            }

            if (basin > 0f && waterY > ColumnField.NoLake)
                gate = min(gate, lerp(1f, smoothstep(waterY + 1.5f, waterY + 6f, y), saturate(basin * 10f)));

            // U hranic LOD0 sloupců taky žádná 3D vrstva: hrubší LOD ji má zeslabenou
            // nebo vůbec, a šev by se jinak rozešel ve výšce, kterou LodSeamJob nesrovná.
            float gate3D = gate;
            if (seamLine > 0f)
            {
                float2 ln = round(p / seamLine) * seamLine;
                float2 dd = abs(p - ln);
                int lx = LodSeamJob.LineLevel(ln.x, seamLine), lz = LodSeamJob.LineLevel(ln.y, seamLine);
                float bx = LodSeamJob.Band(lx, seamLine);
                float bz = LodSeamJob.Band(lz, seamLine);
                float sOld = (bx > 0f ? smoothstep(0f, bx, dd.x) : 1f) * (bz > 0f ? smoothstep(0f, bz, dd.y) : 1f);
                gate *= sOld;

                if (gp.Calm(8))
                {
                    // Kolo 12b: převisový warp posouvá povrch strmého svahu o metry (naměřeno
                    // až 6–10 m). Vypnutý v pásu 1,5–5 m kolem přímky švu z něj dělal schod
                    // tam a zpět – rovnou rýhu nebo val podél každé přímky po 64 m přes celý
                    // svah. Na přímce samé musí zůstat vypnutý (LOD se tam potkávají a 3D vrstvu
                    // mají různě silnou nebo různě jemně vzorkovanou), ale doznívá v pásu
                    // SeamGate3DBand, takže z toho je nízkofrekvenční přechod, ne čára.
                    gate3D *= (lx >= 1 ? smoothstep(0f, SeamGate3DBand, dd.x) : 1f)
                            * (lz >= 1 ? smoothstep(0f, SeamGate3DBand, dd.y) : 1f);
                }
                else gate3D *= sOld;
            }

            waterGate[i] = gate;
            overhang[i] = ov * gate3D;
        }
    }

    /// <summary>
    /// Krok 15 nad buňkami skalních bran. Stejný princip jako LakeJob – rozhodnutí závisí
    /// jen na ID buňky, takže se na bráně shodne libovolný chunk bez komunikace.
    /// </summary>
    [BurstCompile]
    public struct ArchJob : IJobParallelFor
    {
        public int2 cellMin;
        public int cellsX;
        public GenParams gp;
        public SplineSet spl;

        [WriteOnly] public NativeArray<ArchBody> arches;

        public void Execute(int i)
            => arches[i] = Density3D.EvalArchCell(cellMin + new int2(i % cellsX, i / cellsX), gp, spl);
    }

    /// <summary>Kopie pole – vstup pro <see cref="LodSeamJob"/>, který čte sousedy a nesmí číst svůj výstup.</summary>
    [BurstCompile]
    public struct CopyFloatJob : IJob
    {
        [ReadOnly] public NativeArray<float> src;
        [WriteOnly] public NativeArray<float> dst;
        public void Execute() => dst.CopyFrom(src);
    }

    /// <summary>
    /// Šití švů mezi prstenci LOD.
    ///
    /// <para><b>Problém:</b> sousední sloupce různého LOD vzorkují tutéž funkci výšky
    /// s různým krokem (1, 2, 4… m). Na společné hranici tak jemná strana ukazuje detail,
    /// který hrubá strana mezi svými vzorky jen lineárně protáhne – na hranici prstence
    /// vznikne schod (naměřeno až 3,4 m), kryje ho jen sukně a při pohybu je vidět
    /// prstenec, který se posouvá s hráčem.</para>
    ///
    /// <para><b>Řešení bez závislosti na sousedech:</b> hranice mezi LOD l a l+1 leží vždy
    /// na přímce, která je násobkem délky sloupce LOD l+1. Na každé takové přímce se výška
    /// nahradí lomenou čarou s uzly v krocích LOD l+1 (2, 4, 8 m – viz LineLevel). Každý
    /// LOD, který na přímce může mít hranu, pak spočítá přesně tutéž lomenou čáru a MC
    /// z obou stran projde stejnými body. Protože to platí pro všechny přímky, nezáleží na
    /// tom, jaký LOD má soused zrovna teď – nic se nemusí přestavovat, když se prstence
    /// posunou s hráčem. Přímky jen po 32 m (hranice LOD0|LOD0) zůstávají netknuté.</para>
    ///
    /// <para>Aby nevznikla rýha, přechází se k lomené čáře plynule v úzkém pásu (Band).
    /// Šije se až po hranici LOD3|LOD4 (uzly po 16 m). Dřív končil šev na LOD2|LOD3
    /// a na LOD3|LOD4 audit naměřil až 13,7 m.</para>
    /// </summary>
    [BurstCompile]
    public struct LodSeamJob : IJobParallelFor
    {
        /// <summary>Nejvyšší úroveň, kterou šev šije: LOD3|LOD4 (uzly po 16 m) – nejhrubší
        /// prstenec, který streamer staví (lodCount 5). Uzly po 16 m leží jen na přímkách
        /// po 512 m, tedy na hranici LOD3|LOD4 daleko od hráče.</summary>
        public const int MaxLevel = 4;

        public float2 origin;
        public float step;
        public int side;
        public float seamLine;          // hrana LOD0 sloupce (32 m)

        /// <summary>
        /// Kolo 24: hrany LOD0 sloupce, za kterými je (podle žádaného prstence) taky LOD0 –
        /// bity <see cref="WaterSurface.EdgeX0"/>…<see cref="WaterSurface.EdgeZ1"/>. Na takové
        /// hraně se nešije: obě strany vzorkují stejně jemně, pás 1,5–5 m by jen zbytečně
        /// pokřivil povrch proti hladině. Nula = šije se všude (hrubší LOD, editorové náhledy).
        /// <see cref="fineLine0"/> je index přímky (v násobcích seamLine) hrany X0/Z0 sloupce.
        /// </summary>
        public int fineMask;
        public int2 fineLine0;

        [ReadOnly] public NativeArray<float> src;
        [WriteOnly] public NativeArray<float> surfY;

        /// <summary>
        /// Úroveň hranice, na které leží přímka: m, kde 2^m·seamLine je nejdelší rozteč, které
        /// je přímka násobkem (nejvýš MaxLevel). Na takové přímce se můžou potkat LOD m−1
        /// a LOD m, takže uzly musí ležet v krocích LOD m: 2^m vzorků nejjemnějšího voxelu.
        /// Přímka jen po 32 m (m = 0) je hranicí dvou LOD0 sloupců – tam není co šít.
        /// </summary>
        public static int LineLevel(float lineCoord, float seamLine)
        {
            int idx = (int)round(lineCoord / seamLine);
            int m = 0;
            while (m < MaxLevel && idx != 0 && (idx & 1) == 0) { idx >>= 1; m++; }
            if (idx == 0) m = MaxLevel;
            return m;
        }

        /// <summary>Šířka přechodového pásu kolem přímky dané úrovně, v metrech.</summary>
        public static float Band(int m, float seamLine)
            => m <= 0 ? 0f : clamp(0.75f * (seamLine / 32f) * (1 << m), 1.5f, 5f);

        public void Execute(int i)
        {
            int x = i % side, z = i / side;
            float v = src[i];
            float2 p = origin + new float2(x, z) * step;

            float lx = round(p.x / seamLine) * seamLine;   // nejbližší svislá přímka
            float lz = round(p.y / seamLine) * seamLine;   // nejbližší vodorovná přímka
            float dx = abs(p.x - lx), dz = abs(p.y - lz);

            int mx = LineLevel(lx, seamLine), mz = LineLevel(lz, seamLine);
            if (fineMask != 0)
            {
                int ix = (int)round(lx / seamLine), iz = (int)round(lz / seamLine);
                if (((fineMask & WaterSurface.EdgeX0) != 0 && ix == fineLine0.x)
                    || ((fineMask & WaterSurface.EdgeX1) != 0 && ix == fineLine0.x + 1)) mx = 0;
                if (((fineMask & WaterSurface.EdgeZ0) != 0 && iz == fineLine0.y)
                    || ((fineMask & WaterSurface.EdgeZ1) != 0 && iz == fineLine0.y + 1)) mz = 0;
            }
            float bx = Band(mx, seamLine), bz = Band(mz, seamLine);
            bool nearX = mx > 0 && dx <= bx, nearZ = mz > 0 && dz <= bz;
            if (!nearX && !nearZ) { surfY[i] = v; return; }

            float voxel0 = seamLine / 32f;
            int kx = (int)round(voxel0 * (1 << mx) / step);    // uzlů podél svislé přímky (v z)
            int kz = (int)round(voxel0 * (1 << mz) / step);

            float vz = nearX && kx > 1 ? AlongZ(x, z, p.y, kx, v) : v;
            float vx = nearZ && kz > 1 ? AlongX(x, z, p.x, kz, v) : v;

            float o;
            if (nearX && dx < 1e-3f) o = vz;
            else if (nearZ && dz < 1e-3f) o = vx;
            else
            {
                o = v;
                if (nearX) o = lerp(o, vz, 1f - smoothstep(0f, bx, dx));
                if (nearZ) o = lerp(o, vx, 1f - smoothstep(0f, bz, dz));
            }
            surfY[i] = o;
        }

        private float AlongZ(int x, int z, float pz, int k, float fallback)
        {
            int g = (int)round(pz / step);
            int off = ((g % k) + k) % k;
            if (off == 0) return fallback;
            int z0 = z - off, z1 = z0 + k;
            if (z0 < 0 || z1 >= side) return fallback;
            return lerp(src[z0 * side + x], src[z1 * side + x], off / (float)k);
        }

        private float AlongX(int x, int z, float px, int k, float fallback)
        {
            int g = (int)round(px / step);
            int off = ((g % k) + k) % k;
            if (off == 0) return fallback;
            int x0 = x - off, x1 = x0 + k;
            if (x0 < 0 || x1 >= side) return fallback;
            return lerp(src[z * side + x0], src[z * side + x1], off / (float)k);
        }
    }

    /// <summary>Min/max výšky výřezu – vstup pro prořezávání svislých chunků ve fázi 3.</summary>
    [BurstCompile]
    public struct BoundsJob : IJob
    {
        [ReadOnly] public NativeArray<float> surfY;
        [WriteOnly] public NativeArray<float> bounds;

        public void Execute()
        {
            float mn = float.MaxValue, mx = float.MinValue;
            for (int i = 0; i < surfY.Length; i++)
            {
                float v = surfY[i];
                if (v < mn) mn = v;
                if (v > mx) mx = v;
            }
            bounds[0] = mn;
            bounds[1] = mx;
        }
    }
}
