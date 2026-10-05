using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine;

namespace Orivilon.World.Generation
{
    /// <summary>
    /// Staví hladinu jednoho sloupce.
    ///
    /// Voda je 2D plocha, takže patří sloupci, ne 3D chunku – jeden mesh na 32×32 m.
    ///
    /// KLÍČOVÉ: hladina se vzorkuje jako pole, stejně jako terén. Každý roh buňky dostane
    /// výšku ze spojitého pole svého typu, takže se plocha může NAKLONIT. Dřív tu byl
    /// jeden vodorovný čtverec na buňku a z klesající řeky se stalo schodiště plošek –
    /// mezi sousedními ploškami různé výšky nebyla svislá stěna, takže tam byly vidět
    /// mezery. To je ten hřeben pruhů; žádný práh ho spravit nemohl, protože chyba byla
    /// v geometrii, ne v rozhodování o vodě.
    ///
    /// Typ se volí na buňku a všechny čtyři rohy pak čtou z pole TÉHOŽ typu. Kdyby se
    /// míchaly, sousedily by na styku moře a jezera výšky vzdálené desítky metrů a vznikla
    /// by svislá vodní stěna.
    ///
    /// <para><b>Břehová čára je izolinie, ne mřížka.</b> Dřív se buňka nakreslila celá, nebo
    /// vůbec – hranice vody proto přesně kopírovala voxelovou mřížku a byla vidět jako zuby.
    /// Navíc se buňka zahazovala, když hloubka klesla pod práh, takže voda končila kus PŘED
    /// břehem: na pláži se sklonem 1:20 znamenal práh 45 cm devět metrů chybějící vody.</para>
    ///
    /// <para>Teď se hraniční buňka ořízne izolinií pole <c>hladina − terén</c>, přesně jako
    /// marching cubes řeže terén – jen o dimenzi níž (marching squares). Hranice tím dostane
    /// subvoxelovou přesnost a je hladká i v hrubém LOD, kde je buňka klidně 16 m velká.</para>
    ///
    /// <para><b>Zanoření (overdraw).</b> Neřeže se na nule, ale o <c>shoreSink</c> metrů POD
    /// terénem. Hraniční vrchol pak leží právě tolik pod povrchem svahu – voda se zanoří pod
    /// břeh a neprůhledný terén ji sám ořízne (shader je v Transparent frontě se ZWrite Off,
    /// takže se korektně schová za už vykreslený terén). Tím zmizí i blikání o hloubkový
    /// buffer, kvůli kterému tu ten hloubkový práh původně byl.</para>
    ///
    /// <para>Vedlejší, ale nejcennější důsledek: jakmile břeh určuje průsečík s terénem,
    /// nemusí být maska typu vody opatrná. Proto smí být <c>riverCoreMin</c> nízké – voda,
    /// která by se ocitla nad terénem, se prostě ořízne.</para>
    /// </summary>
    ///
    /// <para><b>26.9.2026 – jedno spojité pole hladiny místo „druhu vody na buňku".</b>
    /// Dřív si každá buňka vybrala jeden druh (moře / jezero / řeka) podle nejhlubšího rohu
    /// a všechny rohy četly jeho pole. Na styku dvou těles s různou hladinou (řeka u moře,
    /// jezero u moře) proto sousední buňky ležely v jiné výšce: schody hladiny a svislé
    /// mezery, přes které byl vidět terén. A kde těleso končilo maskou (síla koryta, oblast
    /// jezera), a ne terénem, visela hrana vody ve vzduchu.</para>
    ///
    /// <para>Teď má každý ROH jednu hladinu <c>W = max(moře, jezero, řeka)</c> a řez vede
    /// izolinií <c>W − terén</c>. Rohy sdílené sousedními buňkami mají tutéž hodnotu, takže
    /// je hladina spojitá přes buňky i přes druhy vody. Řeka mimo koryto (síla pod prahem)
    /// hladinu nesesekne, ale spojitě ji stáhne pod moře – kde terén vedle koryta leží níž,
    /// voda k němu sklesne, místo aby visela. O to, aby tam terén níž neležel, se stará
    /// hráz v <c>WorldGenMath.EvalSurface</c>; stažení je jen pojistka.</para>
    public static class WaterSurface
    {
        /// <summary>Hodnota, která v mřížce hladin znamená „tady voda není".</summary>
        public const float NoWater = -100000f;

        private static readonly List<Vector3> verts = new List<Vector3>(4096);
        private static readonly List<int> tris = new List<int>(6144);

        // Kolo 10: atributy vody pro shader (UV0): xy = vektor toku po proudu (směr × rychlost
        // v m/s), z = váha jezera, w = váha řeky. Moře = (0,0,0,0) – shader pak kreslí původní
        // mořské vlny, takže materiál na meshi bez atributů vypadá přesně jako dřív.
        private static readonly List<Vector4> uvs = new List<Vector4>(4096);
        private static Vector4[] cornerAttr = new Vector4[0];
        private static readonly Vector4[] ringA = new Vector4[4];
        private static readonly Vector4[] polyA = new Vector4[8];

        /// <summary>Kolo 10: zapisovat atributy toku (vypnutí = mesh bez UV0 = původní vzhled).</summary>
        public static bool FlowAttributes = true;

        private static float[] level = new float[0];     // W v rohu: max(moře, jezero, řeka)
        private static byte[] cornerKind = new byte[0];  // kdo W určil: 1 moře, 2 jezero, 3 řeka
        private static float[] ground = new float[0];
        private static byte[] cellKind = new byte[0];    // 0 nic, jinak druh nejhlubšího rohu



        /// <summary>
        /// Nejmenší hloubka v nejhlubším rohu, aby buňka vůbec dostala vodu.
        ///
        /// <para>Není to ten starý hloubkový práh, který ukrajoval metry od břehu – řez
        /// vede dál izolinií. Je to jen pojistka proti tomu, aby rovina ležící náhodou
        /// přesně na hladině dostala pár milimetrů vody a rozsypala se na skvrnitý film.
        /// Dva centimetry jsou pod rozlišením, ve kterém se dá voda od terénu rozeznat —
        /// a hlavně tak nízko, že se z téhle pojistky nemůže stát to, co rozhoduje o břehu.</para>
        /// </summary>
        private const float MinBodyDepth = 0.02f;

        // Pracovní pole jedné buňky. Ring je v pořadí 00 → 01 → 11 → 10, ve kterém vychází
        // normála nahoru; klipování pak jde po obvodu a výsledek se triangularizuje vějířem.
        private static readonly float[] ringG = new float[4];
        private static readonly Vector3[] ringV = new Vector3[4];
        private static readonly Vector3[] poly = new Vector3[8];

        // Vertex colors tu bývaly: hloubkový přechod mělká→hluboká se počítal na každý roh
        // každé vodní buňky. Shader LowPolyWater je ale nečte – hloubku si bere z depth
        // textury (SampleSceneDepth), takže výsledek je přesnější a funguje i tam, kde
        // hladina přesahuje přes okraj sloupce. Byl to tedy výpočet a 4 kB na sloupec
        // pro data, která se nikam nedostala.

        /// <summary>
        /// Statistika poslední postavené hladiny. <c>PerimeterRatio</c> je poměr obvodu
        /// k ploše: kompaktní jezero má hluboko pod 1, hřeben tenkých pruhů se blíží 2.
        /// </summary>
        public struct Stats
        {
            public int sea, lake, river, waterCells, boundaryEdges;

            /// <summary>Buňky vzaté celé (mask 15) a buňky skutečně oříznuté izolinií.</summary>
            public int full, cut;

            /// <summary>Vodní buňky mělčí než 30 cm i v nejhlubším rohu – kandidáti na „blánu".</summary>
            public int shallow;

            /// <summary>Součet hloubek v nejhlubším rohu, pro průměr.</summary>
            public float depthSum;

            /// <summary>
            /// Říční buňky mělčí než metr. Vyřezané koryto má dno nejmíň
            /// <c>riverDepthMin</c> = 1,2 m pod hladinou, takže mělká říční voda znamená
            /// hladinu ležící na neupraveném terénu – „levitující" řeku.
            /// </summary>
            public int riverShallow;

            /// <summary>
            /// Součet rozdílu nejhlubšího a nejmělčího rohu. Na vyrovnaném dně je skoro
            /// nula, na svahu roste – rozliší koryto od blány rozlité po kopci.
            /// </summary>
            public float spreadSum;

            public float PerimeterRatio => waterCells > 0 ? boundaryEdges / (float)waterCells : 0f;
            public float MeanDepth => waterCells > 0 ? depthSum / waterCells : 0f;
            public float MeanSpread => waterCells > 0 ? spreadSum / waterCells : 0f;
            public float RiverShallowRatio => river > 0 ? riverShallow / (float)river : 0f;
            public float ShallowRatio => waterCells > 0 ? shallow / (float)waterCells : 0f;
        }

        /// <summary>
        /// Otisk verze téhle funkce. Slouží k jedinému: ověřit v běžícím editoru, že se
        /// opravdu překládá a volá kód, který má člověk před sebou v souboru.
        /// </summary>
        public const string Version = "2026-09-26 spojita-hladina";

        public static Stats LastStats;

        /// <param name="riverCoreMin">
        /// Od jaké síly koryta se řeka počítá za vodní plochu. Smí být nízké – co vyjde nad
        /// terén, ořízne izolinie.
        /// </param>
        /// <param name="shoreSink">
        /// O kolik metrů se hladina zanoří pod terén, než se uřízne. Nula = řez přesně na
        /// břehové čáře (a tedy riziko blikání), typicky 0,3–0,8 m.
        /// </param>
        /// <param name="levels">
        /// Volitelná mřížka <c>SampleDim×SampleDim</c>, do které se zapíše hladina v každé
        /// buňce (jinak <see cref="NoWater"/>).
        ///
        /// <para>Je to jediný způsob, jak se pak dá zjistit, jestli je kamera pod vodou.
        /// Sloupcové pole, ze kterého se hladina počítá, se po dostavění chunků uvolňuje –
        /// šetří to desítky megabajtů, ale znamená to, že se na ně za běhu nedá zeptat.
        /// Mřížka hladin je proti němu drobek: 4 kB, a jen u sloupců, kde voda opravdu je.</para>
        ///
        /// <para>Zapisuje se jen tam, kde je roh SKUTEČNĚ pod hladinou – ne do zanořeného
        /// lemu. Hráč stojící na pláži nemá být podle téhle mřížky ve vodě.</para>
        /// </param>
        public static bool Build(in ColumnField column, int pad, float voxelSize,
                                 float seaLevel, float riverCoreMin, float shoreSink, Mesh mesh,
                                 float[] levels = null, int fineEdges = 0, SeamSnapshot keep = null)
        {
            int dim = VoxelWorld.SampleDim;          // 33 bodů = 32 buněk
            int n = dim * dim;
            int cd = dim - 1;

            if (level.Length != n)
            {
                level = new float[n]; cornerKind = new byte[n];
                cornerAttr = new Vector4[n];
                ground = new float[n];
                cellKind = new byte[cd * cd];
            }
            LastStats = default;

            if (levels != null)
                for (int i = 0; i < levels.Length; i++) levels[i] = NoWater;

            // Zanoření pod břeh. POZOR na to, co se stane, když se o tuhle hodnotu posune
            // PRÁH: buňka se pak počítá za celou zatopenou, jakmile je terén ve všech čtyřech
            // rozích míň než `sink` NAD vodou – a na mírném břehu to nastane dřív, než se
            // stihne cokoli uříznout. Proto se řeže na NULE (skutečná břehová čára) a zanořuje
            // se až POLOHA hraničního vrcholu – posune se po hraně dál do svahu, přesně tam,
            // kde terén vystoupá `sink` nad hladinu.
            float sink = Mathf.Max(0f, shoreSink);

            // ── 1) jedna hladina v každém rohu ──────────────────────────
            //
            // O jezeře a řece rozhoduje pole, které terén vyřezalo (`lakeY` je vyplněné jen
            // v pánvi, `riverCore` je síla koryta) – ne porovnání s povrchem, který je v korytě
            // vyřezaný jen zčásti. Moře je kandidátem všude. Z kandidátů vyhrává nejvyšší:
            // max spojitých funkcí je spojitý, takže se na styku těles nemůže otevřít schod.
            bool any = false;
            for (int z = 0; z < dim; z++)
            for (int x = 0; x < dim; x++)
            {
                int src = (z + pad) * column.side + (x + pad);
                int dst = z * dim + x;

                float g = column.surfY[src];
                ground[dst] = g;

                float w = seaLevel;
                byte k = 1;

                float lk = column.lakeY[src];
                if (lk > ColumnField.NoLake && lk > w) { w = lk; k = 2; }

                float core = column.riverCore[src];
                if (core > 0f)
                {
                    // Nad prahem plná hladina, pod ním se spojitě stahuje až POD moře – takže
                    // v bodě, kde síla koryta dojde na nulu, už řeka nic neurčuje a W je
                    // spojité i tam, kde říční kandidát zmizí.
                    float rv = column.riverY[src];
                    if (core < riverCoreMin)
                        rv = Mathf.Lerp(seaLevel - 0.01f, rv, core / Mathf.Max(riverCoreMin, 1e-3f));
                    if (rv > w) { w = rv; k = 3; }
                }

                level[dst] = w;
                cornerKind[dst] = k;
                if (k == 3)
                {
                    // Rychlost z podélného spádu hladiny: rovinná řeka se sotva posouvá,
                    // peřej a vodopád tečou rychle. Směr je hydrologický (po proudu).
                    Unity.Mathematics.float2 fd = column.flowDir[src];
                    float spd = Mathf.Clamp(0.35f + 7f * column.flowSlope[src], 0.35f, 3f);
                    cornerAttr[dst] = new Vector4(fd.x * spd, fd.y * spd, 0f, 1f);
                }
                else cornerAttr[dst] = k == 2 ? new Vector4(0f, 0f, 1f, 0f) : Vector4.zero;
                if (w - g > -sink) any = true;
            }

            // Kolo 6: mělký okraj horního úseku na skoku hladiny (viz DryLips).
            lipVox = voxelSize;
            if (LipFix && voxelSize <= 1.01f) DryLips(column, pad, dim, sink);

            float2 corner0 = column.origin + pad * voxelSize;
            curSnap = keep;
            if (keep != null)
            {
                keep.Save(corner0, voxelSize, sink, any, n);
                if (pad >= 1) MarkKeepRaw(keep, column, pad, dim, cd, seaLevel, riverCoreMin);
            }
            return Finish(corner0, voxelSize, sink, any, dim, cd, mesh, levels, fineEdges);
        }

        /// <summary>
        /// Kolo 22: znovu postaví hladinu sloupce LOD0 ze snímku (bez sloupcového pole) s jinou
        /// maskou hran, za kterými leží LOD0 soused. Volá streamer, když se posune prstenec.
        /// </summary>
        public static bool Rebuild(SeamSnapshot s, int fineEdges, Mesh mesh, float[] levels)
        {
            int dim = VoxelWorld.SampleDim, n = dim * dim, cd = dim - 1;
            if (level.Length != n)
            {
                level = new float[n]; cornerKind = new byte[n];
                cornerAttr = new Vector4[n];
                ground = new float[n];
                cellKind = new byte[cd * cd];
            }
            LastStats = default;
            if (levels != null)
                for (int i = 0; i < levels.Length; i++) levels[i] = NoWater;
            System.Array.Copy(s.level, level, n);
            System.Array.Copy(s.ground, ground, n);
            System.Array.Copy(s.kind, cornerKind, n);
            System.Array.Copy(s.attr, cornerAttr, n);
            lipVox = s.voxel;
            curSnap = s;
            return Finish(s.origin, s.voxel, s.sink, s.any, dim, cd, mesh, levels, fineEdges);
        }

        // Kolo 22: snímek, jehož příznaky „ponechat surovou hladinu" platí pro právě šitou hranu.
        private static SeamSnapshot curSnap, curKeep;

        /// <summary>
        /// Kolo 22: bod hrany se na hraně k LOD0 sousedovi ponechá se surovou hladinou jen tehdy, když
        /// je mokrý on a aspoň jeden kolmý soused (vnitřní řada nebo řada za hranou z okraje pole). Rozhodnutí
        /// stojí jen na bodech přímky a ±1 m kolem ní, které obě strany vzorkují stejně, takže ho obě
        /// udělají shodně. Vodopád s1337: koryto přes šev je mokré → bez rýhy. s2026
        /// (919, 64): bod je „mokrý" jen proto, že terén na přímce je lomená čára níž → zůstane lomená čára.
        /// </summary>
        private static void MarkKeepRaw(SeamSnapshot s, in ColumnField column, int pad, int dim, int cd,
                                        float seaLevel, float riverCoreMin)
        {
            if (s.keepRaw == null || s.keepRaw.Length != 4 * dim) s.keepRaw = new bool[4 * dim];
            for (int e = 0; e < dim; e++)
            {
                // z = 0: vnitřní řada z = 1, venku z = −1
                s.keepRaw[0 * dim + e] = Wet(e) && (Wet(dim + e) || WetField(column, pad, e, -1, seaLevel, riverCoreMin));
                // z = cd
                s.keepRaw[1 * dim + e] = Wet(cd * dim + e) && (Wet((cd - 1) * dim + e) || WetField(column, pad, e, cd + 1, seaLevel, riverCoreMin));
                // x = 0
                s.keepRaw[2 * dim + e] = Wet(e * dim) && (Wet(e * dim + 1) || WetField(column, pad, -1, e, seaLevel, riverCoreMin));
                // x = cd
                s.keepRaw[3 * dim + e] = Wet(e * dim + cd) && (Wet(e * dim + cd - 1) || WetField(column, pad, cd + 1, e, seaLevel, riverCoreMin));
            }
        }

        private static bool Wet(int i) => level[i] - ground[i] > 0f;

        private static bool WetField(in ColumnField column, int pad, int x, int z, float seaLevel, float riverCoreMin)
        {
            int src = (z + pad) * column.side + (x + pad);
            float w = seaLevel;
            float lk = column.lakeY[src];
            if (lk > ColumnField.NoLake && lk > w) w = lk;
            float core = column.riverCore[src];
            if (core > 0f)
            {
                float rv = column.riverY[src];
                if (core < riverCoreMin)
                    rv = Mathf.Lerp(seaLevel - 0.01f, rv, core / Mathf.Max(riverCoreMin, 1e-3f));
                if (rv > w) w = rv;
            }
            return w - column.surfY[src] > 0f;
        }

        private static bool Finish(float2 corner0, float voxelSize, float sink, bool any, int dim, int cd,
                                   Mesh mesh, float[] levels, int fineEdges)
        {
            if (!SeamByNeighbor) fineEdges = 0;
            curKeep = fineEdges != 0 ? curSnap : null;

            // Šev mezi LOD: na hranici sloupce se hladina srovná na lomenou čáru s uzly
            // v krocích hrubšího souseda – stejně jako terén v LodSeamJob. Hrubší soused má
            // vzorky jen v uzlech a mezi nimi hladinu lineárně protáhne; jemná strana teď na
            // hranici spočítá totéž, takže se okraje vody potkají bez schodu a mezery.
            // Kolo 22: hrana, za kterou je SKUTEČNĚ zveřejněný LOD0 soused (fineEdges), se nešije –
            // obě strany vzorkují po 1 m tytéž body, takže se potkají i bez lomené čáry (jako na
            // přímkách po 32 m). Lomená čára s uzly až po 16 m stahovala na vodopádu s1337 hladinu
            // pod terén (suchá rýha). Za hrubším sousedem zůstává lomená čára beze změny.
            {
                float2 corner = corner0;
                seamKeep = (fineEdges & EdgeZ0) != 0 ? 0 : -1;
                SeamEdge(corner.y, true, 0, dim, cd, voxelSize);          // z = 0
                seamKeep = (fineEdges & EdgeZ1) != 0 ? 1 : -1;
                SeamEdge(corner.y + cd * voxelSize, true, cd, dim, cd, voxelSize);
                seamKeep = (fineEdges & EdgeX0) != 0 ? 2 : -1;
                SeamEdge(corner.x, false, 0, dim, cd, voxelSize);         // x = 0
                seamKeep = (fineEdges & EdgeX1) != 0 ? 3 : -1;
                SeamEdge(corner.x + cd * voxelSize, false, cd, dim, cd, voxelSize);
                seamKeep = -1;
                // Kolo 7: na přímce, kde se může potkat jiný LOD, se vodorovný lem schodu
                // nepoužije – jeho váha závisí na délce hrany (rozdíl hladin mezi rohy), takže
                // jemná a hrubá strana by řízly jinak a na švu by vznikla mezera. Lineární řez
                // je na lomené čáře švu stejný pro obě strany.
                seamZ0 = LodSeamJob.LineLevel(corner.y, SeamLine) > 0;
                seamZ1 = LodSeamJob.LineLevel(corner.y + cd * voxelSize, SeamLine) > 0;
                seamX0 = LodSeamJob.LineLevel(corner.x, SeamLine) > 0;
                seamX1 = LodSeamJob.LineLevel(corner.x + cd * voxelSize, SeamLine) > 0;
            }

            mesh.Clear();
            if (!any) return false;

            verts.Clear(); tris.Clear(); uvs.Clear();
            float2 origin = corner0;

            // ── 2) buňka: marching squares na poli W − terén ─────────────
            for (int z = 0; z < cd; z++)
            for (int x = 0; x < cd; x++)
            {
                int c00 = z * dim + x, c10 = c00 + 1, c01 = c00 + dim, c11 = c01 + 1;
                cellKind[z * cd + x] = 0;

                // Pořadí ringu (00, 01, 11, 10) je to, ve kterém vějíř vyjde lícem nahoru.
                float wx = origin.x + x * voxelSize;
                float wz = origin.y + z * voxelSize;

                Corner(0, c00, wx, wz);
                Corner(1, c01, wx, wz + voxelSize);
                Corner(2, c11, wx + voxelSize, wz + voxelSize);
                Corner(3, c10, wx + voxelSize, wz);

                int mask = 0, deepest = 0;
                for (int k = 0; k < 4; k++)
                {
                    if (ringG[k] > 0f) mask |= 1 << k;
                    if (ringG[k] > ringG[deepest]) deepest = k;
                }
                float bestDepth = ringG[deepest];

                // Pojistka proti filmu na rovině ležící přesně na hladině – viz MinBodyDepth.
                if (mask == 0 || bestDepth < MinBodyDepth) continue;

                int dc = deepest == 0 ? c00 : deepest == 1 ? c01 : deepest == 2 ? c11 : c10;
                byte bestKind = cornerKind[dc];

                cellKind[z * cd + x] = bestKind;
                if (bestKind == 1) LastStats.sea++;
                else if (bestKind == 2) LastStats.lake++;
                else LastStats.river++;
                LastStats.waterCells++;
                LastStats.depthSum += bestDepth;
                if (bestDepth < 0.30f) LastStats.shallow++;
                if (bestKind == 3 && bestDepth < 1.0f) LastStats.riverShallow++;

                float lo = Mathf.Min(Mathf.Min(ringG[0], ringG[1]), Mathf.Min(ringG[2], ringG[3]));
                LastStats.spreadSum += bestDepth - lo;

                // Hladina se zapíše jen do rohů, které jsou opravdu pod vodou.
                if (levels != null)
                {
                    if (ringG[0] > 0f) WriteLevel(levels, c00, ringV[0].y);
                    if (ringG[1] > 0f) WriteLevel(levels, c01, ringV[1].y);
                    if (ringG[2] > 0f) WriteLevel(levels, c11, ringV[2].y);
                    if (ringG[3] > 0f) WriteLevel(levels, c10, ringV[3].y);
                }

                int count;
                if (mask == 15)
                {
                    LastStats.full++;
                    // Celá buňka pod vodou – žádné řezání, jen dva trojúhelníky.
                    poly[0] = ringV[0]; poly[1] = ringV[1];
                    poly[2] = ringV[2]; poly[3] = ringV[3];
                    polyA[0] = ringA[0]; polyA[1] = ringA[1];
                    polyA[2] = ringA[2]; polyA[3] = ringA[3];
                    count = 4;
                }
                else
                {
                    LastStats.cut++;

                    // Marching squares: po obvodu se vezmou rohy uvnitř a k nim průsečíky
                    // hran s izolinií. Vrchol nese i výšku, takže se hladina interpoluje
                    // spolu s polohou a na řezu přesně navazuje na sousední buňku.
                    count = 0;
                    for (int k = 0; k < 4; k++)
                    {
                        int k2 = (k + 1) & 3;
                        bool inA = ringG[k] > 0f, inB = ringG[k2] > 0f;

                        if (inA) { polyA[count] = ringA[k]; poly[count++] = ringV[k]; }

                        // Hrana, na které voda končí. Tvar mnohoúhelníku určuje NULOVÁ
                        // izolinie, ale vrchol se položí až tam, kde terén vystoupá `sink`
                        // nad hladinu – tedy pod svah. Hladina tím zůstane dokonale
                        // vodorovná (zanořuje se POLOHA, ne výška) a okraj je schovaný pod
                        // terénem. Ořez na hranici buňky: dál si to převezme soused.
                        //
                        // Počítá se vždycky OD MOKRÉHO konce, ať už je to k, nebo k2 –
                        // jinak by se lem přidával jen na jedné straně každé buňky a hrana
                        // by se klikatila.
                        if (inA != inB)
                        {
                            // hrana leží na hranici sloupce, kterou může sdílet jiný LOD?
                            bool seam = k == 0 ? (x == 0 && seamX0)
                                      : k == 1 ? (z == cd - 1 && seamZ1)
                                      : k == 2 ? (x == cd - 1 && seamX1)
                                      : (z == 0 && seamZ0);
                            polyA[count] = inA ? ringA[k] : ringA[k2];
                            poly[count++] = inA ? Submerged(k, k2, sink, !seam) : Submerged(k2, k, sink, !seam);
                        }
                    }
                    if (count < 3) continue;
                }

                int b = verts.Count;
                for (int k = 0; k < count; k++) { verts.Add(poly[k]); uvs.Add(polyA[k]); }
                for (int k = 1; k < count - 1; k++)
                {
                    tris.Add(b); tris.Add(b + k); tris.Add(b + k + 1);
                }
            }

            for (int z = 0; z < cd; z++)
            for (int x = 0; x < cd; x++)
            {
                if (cellKind[z * cd + x] == 0) continue;
                if (x == 0 || cellKind[z * cd + x - 1] == 0) LastStats.boundaryEdges++;
                if (x == cd - 1 || cellKind[z * cd + x + 1] == 0) LastStats.boundaryEdges++;
                if (z == 0 || cellKind[(z - 1) * cd + x] == 0) LastStats.boundaryEdges++;
                if (z == cd - 1 || cellKind[(z + 1) * cd + x] == 0) LastStats.boundaryEdges++;
            }

            if (tris.Count == 0) return false;

            mesh.indexFormat = verts.Count > 65000
                ? UnityEngine.Rendering.IndexFormat.UInt32
                : UnityEngine.Rendering.IndexFormat.UInt16;
            mesh.SetVertices(verts);
            if (FlowAttributes) mesh.SetUVs(0, uvs);
            mesh.SetTriangles(tris, 0);

            // Normály se počítají z hotové geometrie. Nakloněná říční hladina tak dostane
            // nakloněnou normálu; zapisovat sem předem samé „nahoru" byla jen práce navíc,
            // protože RecalculateNormals to stejně přepsalo.
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return true;
        }

        /// <summary>
        /// Kolo 6: oprava mělkého okraje na schodu hladiny (<see cref="DryLips"/>). VE VÝCHOZÍM
        /// STAVU VYPNUTÁ: na vodopádu seed 90210 (1405, 853) střepy odstraní, ale A/B audit na
        /// 13 místech 5 seedů ukázal, že jinde (peřeje 90210 −960/1264, 777 2742/−99 …) přidává
        /// malé strmé plošky a na jednom místě zubatý břeh. Zapíná se jen pro měření:
        /// <c>/voda lip on</c> a <c>/voda lipab</c>. Správná oprava je v hydrologii – viz report kola 6.
        /// </summary>
        public static bool LipFix = false;

        /// <summary>Kolo 7: vodorovný lem na schodu hladiny (Submerged). Přepíná se s prahem (<c>/voda prah</c>).</summary>
        public static bool StepAwareCut = true;

        /// <summary>Kolik uzlů oprava osušila (kumulativně, pro diagnostiku).</summary>
        public static int LipNodes;

        /// <summary>Kde oprava zasáhla (světové x, z; shluky po 24 m) – pro A/B audit /voda lipab.</summary>
        public static readonly List<Vector2> LipSites = new List<Vector2>(64);

        /// <summary>Skok hladiny řeky mezi sousedními uzly, od kterého jde o schod, ne o svah (m).</summary>
        private const float LipJump = 1.8f;

        /// <summary>Jen tak mělký uzel horního úseku se osuší (m) – hlubší voda je čelo vodopádu.</summary>
        private const float LipDepth = 0.5f;

        /// <summary>
        /// Osuší mělký okraj horního úseku řeky tam, kde hladina v poli skáče.
        ///
        /// <para><b>Proč.</b> Hladina řeky se v sampleru míchá jen mezi úsečkami s podobnou
        /// výškou (HydroSampler: dva různé úseky toku se míchat nesmějí, jinak by vznikl hrb
        /// vody). Kde vedou dva úseky s rozdílem hladiny přes metr těsně vedle sebe (ohyb
        /// nad vodopádem, seed 90210 u 1405, 853), přepne se nejbližší úsečka na Voronoiově
        /// hranici skokem: na jedné straně 10,0 m, na druhé 7,7 m. Mělký okraj horního úseku
        /// (hloubka pár centimetrů) pak na té hranici sousedí s uzlem o dva metry níž a
        /// marching squares mezi nimi natáhnou hladinu šikmo dolů – malé nakloněné střepy
        /// vody na břehu nad řekou (krystaly v Play Mode) a body, které audit hlásí jako
        /// visící hranu.</para>
        ///
        /// <para><b>Co přesně.</b> Uzel se osuší, jen když: hladinu určuje řeka, je mělčí než
        /// <see cref="LipDepth"/>, a mezi ním a některým z 8 sousedů klesne <c>riverY</c> o víc než
        /// <see cref="LipJump"/> na JEDNOM kroku, zatímco o krok před a za se skoro nemění
        /// (schod, ne rampa), a ten soused leží na SUCHU (terén nad jeho hladinou). Strmé peřeje a vodopády mají rampu – tu to nezasáhne. Hlubší
        /// voda na hraně zůstává, to je čelo vodopádu. Rozhoduje se z původních hodnot, takže
        /// se osušení neřetězí. Jen LOD0 (krok 1 m): v hrubém LOD je mezi uzly i přirozený
        /// spád přes metr.</para>
        /// </summary>
        private static float lipVox = 1f;

        private static void DryLips(in ColumnField column, int pad, int dim, float sink)
        {
            if (pad < 2) return;
            int side = column.side;
            for (int z = 0; z < dim; z++)
            for (int x = 0; x < dim; x++)
            {
                int dst = z * dim + x;
                if (cornerKind[dst] != 3) continue;
                float depth = level[dst] - ground[dst];
                if (depth <= 0f || depth >= LipDepth) continue;

                int src = (z + pad) * side + (x + pad);
                float rA = column.riverY[src];
                bool lip = false;
                float lower = float.MaxValue;
                for (int d = 0; d < 8; d++)
                {
                    int dx = d == 0 ? 1 : d == 1 ? -1 : d == 2 ? 0 : d == 3 ? 0 : d == 4 ? 1 : d == 5 ? 1 : d == 6 ? -1 : -1;
                    int dz = d == 0 ? 0 : d == 1 ? 0 : d == 2 ? 1 : d == 3 ? -1 : d == 4 ? 1 : d == 5 ? -1 : d == 6 ? 1 : -1;
                    int o = dz * side + dx;
                    if (column.riverCore[src + o] <= 0f) continue;
                    float j = rA - column.riverY[src + o];
                    if (j <= LipJump) continue;
                    // Dolní strana je pod vlastní hladinou = dvě hladiny nad sebou (peřej,
                    // čelo vodopádu) – tam šikmá plocha mezi nimi JE ten spád a nesahá se na ni.
                    // Osouší se jen tam, kde pod schodem leží suchý břeh.
                    if (column.surfY[src + o] <= column.riverY[src + o]) continue;
                    float before = Mathf.Abs(column.riverY[src - o] - rA);
                    float after = Mathf.Abs(column.riverY[src + o] - column.riverY[src + 2 * o]);
                    if (before < 0.35f * j && after < 0.35f * j)
                    {
                        lip = true;
                        lower = Mathf.Min(lower, column.riverY[src + o]);
                    }
                }
                if (!lip) continue;
                // Uzel dostane hladinu DOLNÍHO úseku (nejvýš pod terén o celé zanoření). Řez
                // mezi ním a mokrým horním sousedem pak díky prudkému poklesu W − terén padne
                // těsně k hornímu sousedovi a pod terén – z horní hladiny nezbude šikmý střep
                // ani lem visící nad srázem, který na schodu vede MC terén níž než pole.
                level[dst] = Mathf.Min(ground[dst] - (sink + 0.1f), lower);
                LipNodes++;
                var at = new Vector2(column.origin.x + (x + pad) * lipVox, column.origin.y + (z + pad) * lipVox);
                bool known = false;
                for (int i = 0; i < LipSites.Count && !known; i++) known = (LipSites[i] - at).sqrMagnitude < 24f * 24f;
                if (!known && LipSites.Count < 64) LipSites.Add(at);
            }
        }

        /// <summary>
        /// Bod na hraně <paramref name="wet"/> → <paramref name="dry"/>, posunutý za
        /// břehovou čáru tak hluboko pod terén, jak dovolí <paramref name="sink"/>.
        ///
        /// <para>Nikdy se nevrátí před nulovou izolinii (to by voda ubývala) a nikdy se
        /// nepřeteče přes konec hrany – za hranicí buňky si lem dodělá soused.</para>
        /// </summary>
        private static bool seamX0, seamX1, seamZ0, seamZ1;

        private static Vector3 Submerged(int wet, int dry, float sink, bool stepCut = true)
        {
            float dg = ringG[wet] - ringG[dry];
            if (dg <= 1e-6f) return ringV[dry];

            // Kolo 7: schod hladiny mezi rohy (hydrologický schod, práh). Hladina suchého
            // rohu tu s vodou mokrého nesouvisí a lineární W by lem natáhlo šikmo nahoru po
            // stěně prahu nebo dolů přes hranu. Když terén suchého rohu leží nad mokrou
            // hladinou, je břeh jednoznačný: lem zůstane VODOROVNĚ ve výšce mokré hladiny
            // a skončí tam, kde (lineární) terén vystoupá o `sink` nad ni. Jinak (terén pod
            // mokrou hladinou = voda přetéká dolů) zůstává původní chování – čelo spádu.
            //     Přechod je plynulý (váha podle velikosti schodu a výšky suchého terénu nad
            //     mokrou hladinou), jinak by se sousední hrany řezaly jednou tak a jednou tak
            //     a břeh by se lámal do zubů.
            float ww = ringV[wet].y, wd = ringV[dry].y;
            float gw = ww - ringG[wet], gd = wd - ringG[dry];
            float stepW = StepAwareCut && stepCut && gd > gw + 1e-4f
                ? Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.3f, 1.0f, Mathf.Abs(wd - ww)))
                  * Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0f, 0.3f, gd - (ww + sink)))
                : 0f;
            if (stepW > 0f)
            {
                float s0o = ringG[wet] / dg;
                float so = Mathf.Min(1f, (ringG[wet] + sink) / dg);
                Vector3 oldV = Vector3.LerpUnclamped(ringV[wet], ringV[dry], Mathf.Max(s0o, so));
                float sh = Mathf.Clamp01((ww + sink - gw) / (gd - gw));
                Vector3 pw = ringV[wet], pd = ringV[dry];
                var flat = new Vector3(Mathf.Lerp(pw.x, pd.x, sh), ww, Mathf.Lerp(pw.z, pd.z, sh));
                return Vector3.Lerp(oldV, flat, stepW);
            }

            float s0 = ringG[wet] / dg;                       // skutečná břehová čára
            float s = Mathf.Min(1f, (ringG[wet] + sink) / dg); // o `sink` hlouběji ve svahu
            return Vector3.LerpUnclamped(ringV[wet], ringV[dry], Mathf.Max(s0, s));
        }

        /// <summary>
        /// Kolo 21: šev hladiny nesmí osušit mokrý uzel (A/B: <c>/voda sevcap on|off</c>). VE VÝCHOZÍM STAVU VYPNUTO:
        /// na vodopádu s1337 (7695, −534) rýhu odstraní (visící hrana 16 → 0), ale regrese na 6 seedech ukázala,
        /// že bez znalosti LOD souseda porušuje sdílenou lomenou čáru švu – mezery vody na hranici LOD (s1337 3×,
        /// s31415 7×) a nové visící hrany u řeky (s2026 3×). Správná oprava potřebuje šev podle skutečného LOD
        /// souseda – viz report kola 21.
        /// </summary>
        public static bool SeamKnotCap = false;

        /// <summary>Kolo 21: o kolik smí lomená čára švu srazit mokrý uzel, než se ponechá jeho vlastní hladina (m).</summary>
        private const float SeamKeepDrop = 0.25f;

        /// <summary>Kolo 21: kolikrát šev ponechal vlastní hladinu uzlu (kumulativně, diagnostika).</summary>
        public static int SeamKept;

        /// <summary>
        /// Kolo 22: šev hladiny podle skutečného LOD souseda (A/B: <c>/voda sevlod on|off</c>). Zapnuto:
        /// hrana LOD0 sloupce, za kterou je zveřejněný LOD0 soused, zůstává bez lomené čáry; streamer
        /// při posunu prstence přestaví ze snímku jen hladinu dotčených LOD0 sloupců.
        /// </summary>
        public static bool SeamByNeighbor = true;

        /// <summary>Bity masky hran s LOD0 sousedem: z = 0, z = max, x = 0, x = max.</summary>
        public const int EdgeZ0 = 1, EdgeZ1 = 2, EdgeX0 = 4, EdgeX1 = 8;

        /// <summary>
        /// Kolo 22: vstup hladiny LOD0 sloupce před šitím švu (W, terén, druh, proud) – ~27 kB.
        /// Drží se jen u sloupců s vodou, aby šlo hladinu přestavět bez sloupcového pole.
        /// </summary>
        public sealed class SeamSnapshot
        {
            internal float[] level, ground;
            internal byte[] kind;
            internal Vector4[] attr;
            internal float2 origin;
            internal float voxel, sink;
            internal bool any;
            internal bool[] keepRaw;

            /// <summary>Je v sloupci vůbec nějaká voda (i jen zanořený lem)?</summary>
            public bool Any => any;

            internal void Save(float2 o, float v, float sk, bool a, int n)
            {
                if (level == null || level.Length != n)
                {
                    level = new float[n]; ground = new float[n]; kind = new byte[n]; attr = new Vector4[n];
                }
                System.Array.Copy(WaterSurface.level, level, n);
                System.Array.Copy(WaterSurface.ground, ground, n);
                System.Array.Copy(cornerKind, kind, n);
                System.Array.Copy(cornerAttr, attr, n);
                origin = o; voxel = v; sink = sk; any = a;
            }
        }

        private static int seamKeep = -1;

        private static readonly Stack<SeamSnapshot> snapshotPool = new Stack<SeamSnapshot>(64);
        public static SeamSnapshot RentSnapshot() => snapshotPool.Count > 0 ? snapshotPool.Pop() : new SeamSnapshot();
        public static void ReturnSnapshot(SeamSnapshot s) { if (s != null) snapshotPool.Push(s); }

        /// <summary>Hrana LOD0 sloupce v metrech – základ roztečí švových přímek.</summary>
        public static float SeamLine = 32f;

        /// <summary>
        /// Srovná hladinu na jedné hraně sloupce na lomenou čáru s uzly, které tam mají
        /// všechny LOD, jež se na té přímce můžou potkat (LodSeamJob.LineLevel).
        /// </summary>
        private static void SeamEdge(float lineCoord, bool alongX, int fixedIdx, int dim, int cd, float voxelSize)
        {
            int m = LodSeamJob.LineLevel(lineCoord, SeamLine);
            if (m <= 0) return;
            // Kolo 24: terén na hraně k LOD0 sousedovi už nešije (LodSeamJob.fineMask), takže ani
            // hladina – taková hrana je pak stejná jako vnitřek sloupce: surová voda nad surovým
            // terénem. keepRaw z kola 22 zůstává pro A/B /voda sevteren off (terén s pásem).
            if (seamKeep >= 0 && VoxelTerrain.TerrainSeamByNeighbor) return;
            int knot = Mathf.RoundToInt(SeamLine / 32f * (1 << m) / voxelSize);
            if (knot <= 1 || cd % knot != 0) return;
            for (int e = 0; e < dim; e++)
            {
                int o = e % knot;
                if (o == 0) continue;
                int e0 = e - o, e1 = e0 + knot;
                float t = o / (float)knot;
                // Kolo 22: hrana k LOD0 sousedovi – mokrý bod s mokrým kolmým sousedem zůstává surový.
                if (seamKeep >= 0 && curKeep != null && curKeep.keepRaw != null && curKeep.keepRaw[seamKeep * dim + e])
                { SeamKept++; continue; }
                if (alongX) SeamLerp(e, fixedIdx, e0, fixedIdx, e1, fixedIdx, t, dim);
                else SeamLerp(fixedIdx, e, fixedIdx, e0, fixedIdx, e1, t, dim);
            }
        }

        /// <summary>Hladina bodu (x, z) jako lineární mezihodnota dvou uzlů na téže hraně.</summary>
        private static void SeamLerp(int x, int z, int ax, int az, int bx, int bz, float t, int dim)
        {
            int i = z * dim + x, a = az * dim + ax, b = bz * dim + bx;
            float forced = Mathf.Lerp(level[a], level[b], t);
            // Kolo 21: uzel švu, který je ve vlastním poli pod vodou, lomená čára nesmí osušit
            // ani srazit o víc než SeamKeepDrop. Uzel švu na suchu (kolo 20: s1337 7693/−512,
            // uzly po 16 m) protahoval nízkou hladinu přes celé koryto – suchá rýha se svislými
            // vodními stěnami i u hráče. Jinde zůstává lomená čára beze změny, takže LOD švy
            // se potkají stejně jako dřív; liší se jen tam, kde by vznikla rýha.
            float raw = level[i];
            if (SeamKnotCap && raw - ground[i] > MinBodyDepth
                && (forced - ground[i] <= MinBodyDepth || raw - forced > SeamKeepDrop))
            {
                SeamKept++;
                return;
            }
            level[i] = forced;
            // druh vody se bere z bližšího uzlu – jen pro statistiku
            cornerKind[i] = t < 0.5f ? cornerKind[a] : cornerKind[b];
        }

        private static void WriteLevel(float[] levels, int i, float lv)
        {
            if (lv > levels[i]) levels[i] = lv;
        }

        /// <summary>
        /// Připraví jeden roh buňky: světový vrchol s výškou hladiny W a hodnotu pole
        /// <c>W − terén</c>, na kterém se řeže. Obě hodnoty závisí jen na rohu, takže je
        /// sousední buňky vidí stejně a řez na společné hraně se potká přesně.
        /// </summary>
        private static void Corner(int k, int corner, float wx, float wz)
        {
            float y = level[corner];
            ringV[k] = new Vector3(wx, y, wz);
            ringA[k] = cornerAttr[corner];
            ringG[k] = y - ground[corner];
        }
    }
}
