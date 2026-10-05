using Unity.Collections;
using Unity.Mathematics;
using static Unity.Mathematics.math;

namespace Orivilon.World.Generation.Hydro
{
    /// <summary>
    /// Blittable pohled na jeden hotový region. Tohle se předává do jobů – managed
    /// <see cref="HydroMap"/> se do Burstu dostat nemůže.
    /// </summary>
    public struct HydroRegionRef
    {
        public float2 origin;
        public int side;
        public int valid;

        /// <summary>
        /// Jen pro měření výkonu (<c>/teren bench</c>, <c>/teren legacy</c>): 1 = výběr koryta
        /// jako před 26.9.2026 – okno 5×5, jen nejbližší úsečka, bez míchání.
        /// </summary>
        public int legacy;

        [ReadOnly] public NativeArray<float> filled;
        [ReadOnly] public NativeArray<float> raw;
        [ReadOnly] public NativeArray<float> accum;
        [ReadOnly] public NativeArray<byte> dir;

        public bool IsValid => valid != 0 && filled.IsCreated;
    }

    /// <summary>
    /// Dotaz „jak daleko je odsud koryto a jak je široké".
    ///
    /// <para><b>Proč se nevzorkuje akumulace bilineárně.</b> Koryto je na makro mřížce
    /// jedna buňka široké. Bilineární čtení akumulace by ho rozmazalo přes 32 m a řeka
    /// by byla mělká vana bez břehů. Místo toho se z okolních buněk poskládají ÚSEČKY
    /// (střed buňky → střed buňky po proudu) a hledá se vzdálenost k nejbližší z nich.
    /// Osa řeky je tím spojitá lomená čára v plném rozlišení terénu, ne mřížka.</para>
    ///
    /// <para>Hladina se podél úsečky interpoluje mezi zaplněnými výškami obou konců.
    /// Ta je po proudu neklesající z definice Priority-Flood, takže voda nikdy neteče
    /// do kopce – to byla stará bolest šumové metody.</para>
    ///
    /// <para>Okno 5×5 buněk stačí: nejdelší úsečka je úhlopříčka buňky, takže z bodu
    /// uvnitř buňky nemůže být nejbližší koryto dál než dvě buňky.</para>
    /// </summary>
    public static class HydroSampler
    {
        /// <summary>Rozdíl vzdáleností, přes který se sousední koryta plynule míchají, v metrech.</summary>
        private const float BlendWidth = 8f;

        /// <summary>
        /// Kam až se míchá. Okno 9×9 buněk zaručeně obsahuje každou úsečku do 112 m;
        /// s rezervou 108 m. Stejný dosah shora omezuje údolní mísu (EvalSurface).
        /// </summary>
        public const float BlendReach = 108f;

        /// <summary>
        /// Kolo 11: šířka pásu, přes který se u soutoků a ohybů plynule stáčí SMĚR toku –
        /// v metrech, podle šířky koryta (řeka 6–42 m). Dřív se směr míchal přes stejných
        /// 8 m jako hladina, a to je u široké řeky ostrý šev napříč hladinou.
        /// </summary>
        private const float DirBlendMin = 14f, DirBlendPerWidth = 0.6f, DirBlendMax = 34f;

        /// <summary>Kolo 11: pás, přes který se míchá spád pro rychlost vzoru (m). Úzký – viz BlendDir.</summary>
        private const float SlopeBlend = 6f;

        public static HydroFlow Nearest(float2 p, in HydroRegionRef r,
                                        float minAccum, float widthMin, float widthMax, float accumFull,
                                        float dirBlend = 0f)
        {
            HydroFlow best = HydroFlow.None;
            if (!r.IsValid) return best;

            float cell = HydroWorld.CellSize;
            float2 g = (p - r.origin) / cell;

            int cx = (int)floor(g.x);
            int cy = (int)floor(g.y);

            // Hloubka zaplnění pod bodem – rozlišuje jezero od řeky. Čte se BILINEÁRNĚ:
            // z nejbližší buňky by hladina jezera skákala po 32 m schodech a břeh by byl
            // pilovitý, což je na klidné vodní ploše to nejnápadnější, co může být.
            float2 gb = clamp((p - r.origin) / cell - 0.5f, 0f, r.side - 1.001f);
            float surf = Bilinear(r.filled, r.side, gb);
            float bed = Bilinear(r.raw, r.side, gb);

            best.lakeSurface = surf;
            best.lakeDepth = max(0f, surf - bed);
            best.lakeBed = bed;
            best.lakeLevel = -100000f;

            // Hladina jezera, ke kterému bod patří: nejvyšší zaplněná (filled > raw) ze čtyř
            // buněk, ze kterých se právě čte bilineárně. Uvnitř jezera je to jeho hladina
            // a je KONSTANTNÍ; bilineární `surf` se na okraji míchá se surovou výškou břehu
            // a stoupal by. Záměrně jen tyhle čtyři buňky – širší okno by na hranici okna
            // přepínalo mezi pánvemi po celých 32m buňkách a kreslilo pravoúhlé schody.
            if (best.lakeDepth > 0f)
            {
                int bx0 = (int)gb.x, by0 = (int)gb.y;
                for (int oy = 0; oy <= 1; oy++)
                for (int ox = 0; ox <= 1; ox++)
                {
                    int li = min(by0 + oy, r.side - 1) * r.side + min(bx0 + ox, r.side - 1);
                    float fl = r.filled[li];
                    if (fl - r.raw[li] > 0.05f && fl > best.lakeLevel) best.lakeLevel = fl;
                }
                if (best.lakeLevel < -99999f) best.lakeLevel = surf;
            }

            // Výběr koryta: nejbližší osa (to drží hladinu spojitou podél jednoho toku –
            // sousední úsečky mají společný vrchol, takže se na hranici jejich Voronoiových
            // oblastí promítnou do téhož bodu). Mezi RŮZNÝMI toky ale hranice spojitá není:
            // u soutoků a v pobřežních rovinách vedou vedle sebe toky s jinou hladinou
            // a údolní mísa se pak na hranici skokem přepnula z jedné výšky na druhou.
            //
            // Proto se kandidáti do BlendWidth od nejbližšího váženě průměrují. Váha klesá
            // spojitě s rozdílem vzdáleností, takže při přepnutí nejbližšího mají oba váhu 1
            // z obou stran a výsledek nemá skok. Váha navíc odeznívá před BlendReach, kam
            // okno 9×9 buněk ještě spolehlivě dosáhne (≥ 112 m) – jinak by kandidát
            // vypadnutím z okna udělal skok sám.
            const int MaxCand = 30;
            float cd0 = 1e9f;
            var cDist = new FixedList128Bytes<float>();
            var cWater = new FixedList128Bytes<float>();
            var cWidth = new FixedList128Bytes<float>();
            var cAcc = new FixedList128Bytes<float>();
            var cSlope = new FixedList128Bytes<float>();
            var cDir = new FixedList512Bytes<float2>();

            // Okno 9×9 buněk: obsahuje zaručeně každou úsečku do 112 m (5×5 jen do 48 m).
            // Tolik potřebuje údolní mísa – s menším oknem v ní úsečka vypadla z okna
            // a hladina, ke které se mísa srovnává, skočila. Cena naměřená /teren bench.
            int win = r.legacy != 0 ? 2 : 4;
            for (int dy = -win; dy <= win; dy++)
            for (int dx = -win; dx <= win; dx++)
            {
                int ax = cx + dx, ay = cy + dy;
                if (ax < 0 || ay < 0 || ax >= r.side || ay >= r.side) continue;

                int ai = ay * r.side + ax;

                float aAcc = r.accum[ai];
                if (aAcc < minAccum) continue;

                byte d = r.dir[ai];
                if (d > 7) continue;

                int2 o = HydroWorld.DirOffset(d);
                int bx = ax + o.x, by = ay + o.y;
                if (bx < 0 || by < 0 || bx >= r.side || by >= r.side) continue;

                int bi = by * r.side + bx;

                float2 a = r.origin + (new float2(ax, ay) + 0.5f) * cell;
                float2 b = r.origin + (new float2(bx, by) + 0.5f) * cell;

                float2 ab = b - a;
                float len2 = dot(ab, ab);
                float t = len2 > 1e-6f ? saturate(dot(p - a, ab) / len2) : 0f;

                float2 q = a + ab * t;
                float dist = length(p - q);

                // Nasycení se ořezává UŽ TADY. Nad accumFull mají dva sousední regiony
                // zaručeně shodnou šířku, i když se jejich napočítaná povodí liší –
                // je to tentýž mechanismus, který drží síť bez švů.
                float acc = min(lerp(aAcc, r.accum[bi], t), accumFull);
                float wY = lerp(r.filled[ai], r.filled[bi], t);
                float wSlope = len2 > 1e-6f ? abs(r.filled[ai] - r.filled[bi]) / sqrt(len2) : 0f;
                float width = WidthFromAccum(acc, widthMin, widthMax, accumFull);

                if (dist < best.distance)
                {
                    best.distance = dist;
                    best.accum = acc;
                    best.width = width;
                    best.waterY = wY;
                    best.waterSlope = wSlope;
                    best.dir = len2 > 1e-6f ? ab * rsqrt(len2) : new float2(0f);
                }

                if (dist < BlendReach && cDist.Length < MaxCand)
                {
                    cDist.Add(dist); cWater.Add(wY); cWidth.Add(width); cAcc.Add(acc); cSlope.Add(wSlope);
                    cDir.Add(len2 > 1e-6f ? ab * rsqrt(len2) : new float2(0f));
                }
                if (dist < cd0) cd0 = dist;
            }

            best.valleyY = best.waterY;
            best.valleyWidth = best.width;
            best.valleyDistance = best.distance;
            best.flowSlope = best.waterSlope;

            if (best.Found && cDist.Length > 1 && r.legacy == 0)
            {
                // Dvě míchání:
                //  • ÚDOLÍ (terén daleko od koryta) – přes všechny kandidáty. Tam jde jen o to,
                //    aby výška, ke které se mísa srovnává, neskákala.
                //  • KORYTO A HLADINA – jen přes úsečky s podobnou hladinou (sousední úsečky
                //    téhož toku u ohybu). Míchat hladinu dvou různých toků (nebo dvou vzdálených
                //    částí meandru) by v korytě udělalo hrb vody, který nikde neteče.
                float wn = best.waterY;
                float sw = 0f, sY = 0f, sW = 0f, sD = 0f;
                float fw = 0f, fY = 0f, fW = 0f, fA = 0f, fD = 0f, fS = 0f;
                float2 fDir = new float2(0f);
                for (int i = 0; i < cDist.Length; i++)
                {
                    float w = saturate(1f - (cDist[i] - cd0) / BlendWidth)
                            * (1f - smoothstep(BlendReach - 8f, BlendReach, cDist[i]));
                    if (cDist[i] <= cd0) w = max(w, 1e-4f);
                    sw += w; sY += w * cWater[i]; sW += w * cWidth[i]; sD += w * cDist[i];

                    float same = 1f - smoothstep(0.75f, 2f, abs(cWater[i] - wn));
                    float w2 = w * same;
                    if (cDist[i] <= cd0) w2 = max(w2, 1e-4f);
                    fw += w2; fY += w2 * cWater[i]; fW += w2 * cWidth[i]; fA += w2 * cAcc[i];
                    fD += w2 * cDist[i]; fS += w2 * cSlope[i]; fDir += w2 * cDir[i];
                }
                if (sw > 1e-5f)
                {
                    best.valleyY = sY / sw;
                    best.valleyWidth = sW / sw;
                    best.valleyDistance = sD / sw;
                }
                if (fw > 1e-5f)
                {
                    best.waterY = fY / fw;
                    best.width = fW / fw;
                    best.accum = fA / fw;
                    best.distance = fD / fw;
                    best.waterSlope = fS / fw;
                    float fl = length(fDir);
                    if (fl > 1e-4f) best.dir = fDir / fl;
                }

                best.flowSlope = best.waterSlope;
                if (dirBlend > 0.5f)
                    best.dir = BlendDir(cDist, cAcc, cWidth, cDir, cSlope, cd0, best.dir, best.waterSlope, out best.flowSlope);
            }

            return best;
        }

        /// <summary>
        /// Kolo 11: směr toku pro vzor na hladině (nic jiného ho nečte – hladina, koryto ani terén
        /// se nemění).
        ///
        /// <para>Dřívější míchání mělo dvě vady. (1) Váha kandidáta závisela na hladině
        /// <b>nejbližší</b> úsečky; když se nejbližší přepnula mezi úsečkami s různou hladinou
        /// (ohyb D8 na stupni, přítok ústící stupněm), vypadla druhá z míchání skokem a směr se
        /// otočil o 45–90° (ve výjimce až 180°) na pár metrech. (2) Pás míchání byl 8 m i u 40 m
        /// široké řeky.</para>
        ///
        /// <para>Tady váha nezávisí na tom, která úsečka je nejbližší – jen na spojité veličině
        /// <c>vzdálenost − nejmenší vzdálenost</c> (hladký pás podle šířky koryta) a na průtoku
        /// (∜akumulace: hlavní tok převáží, přítok si u vlastní osy drží svůj směr). Míchají se
        /// i úsečky s jinou hladinou – u soutoku a na stupni jde o tutéž vodu. Míchá se
        /// <b>rychlost</b>, ne jen směr: kde vedle sebe tečou protisměrné pruhy téže vody (vlásenka
        /// D8 u soutoku, sevřený meandr), výsledná rychlost na dělicí čáře plynule klesne k nule
        /// místo ostrého švu. Vrací vektor délky ≤ 1 (WaterSurface ho násobí rychlostí).
        /// Všechny úsečky D8 míří z kopce, takže jejich kladná kombinace z kopce míří taky.</para>
        /// </summary>
        private static float2 BlendDir(in FixedList128Bytes<float> cDist, in FixedList128Bytes<float> cAcc,
                                       in FixedList128Bytes<float> cWidth, in FixedList512Bytes<float2> cDir,
                                       in FixedList128Bytes<float> cSlope,
                                       float cd0, float2 fallback, float slopeFallback, out float slopeOut)
        {
            slopeOut = slopeFallback;

            // Šířka koryta v místě: vážená přes úsečky do 4 m od nejbližší (spojitá).
            float ww = 0f, wsum = 0f;
            for (int i = 0; i < cDist.Length; i++)
            {
                float k = saturate(1f - (cDist[i] - cd0) / 4f);
                ww += k * cWidth[i]; wsum += k;
            }
            float width = wsum > 1e-5f ? ww / wsum : 6f;
            float band = clamp(DirBlendMin + DirBlendPerWidth * width, DirBlendMin, DirBlendMax);

            float2 sum = new float2(0f);
            float wt = 0f, ws = 0f, wsw = 0f;
            for (int i = 0; i < cDist.Length; i++)
            {
                float s = saturate(1f - (cDist[i] - cd0) / band);
                s = s * s * (3f - 2f * s)
                    * (1f - smoothstep(BlendReach - 8f, BlendReach, cDist[i]));
                if (s <= 0f) continue;
                float q = sqrt(sqrt(max(cAcc[i], 1f)));
                sum += s * q * cDir[i];
                wt += s * q;
                // Spád (rychlost vzoru) jen v úzkém pásu: peřej musí zůstat rychlá po celé délce
                // své úsečky, jen se na jejích koncích plynule rozjede a zpomalí.
                float s2 = saturate(1f - (cDist[i] - cd0) / SlopeBlend);
                ws += s2 * cSlope[i]; wsw += s2;
            }
            if (wt < 1e-5f) return fallback;
            if (wsw > 1e-5f) slopeOut = ws / wsw;
            float sl = length(sum);
            if (sl < 1e-5f) return new float2(0f);
            // Soudržnost: 1 = všichni stejným směrem; ohyb D8 o 135° má na ose ~0,38 a teče
            // plnou rychlostí, protisměrné pruhy (→ 0) plynule zpomalí až do klidu.
            float coh = sl / wt;
            return sum / sl * saturate(coh * 2.6f);
        }

        /// <summary>
        /// Kolo 11, jen diagnostika (<c>/folaz tokmapa</c>): kandidátní úsečky kolem bodu
        /// (buňka a→b, vzdálenost, akumulace, hladina, směr). Stejný výběr jako <see cref="Nearest"/>.
        /// </summary>
        public static string Describe(float2 p, in HydroRegionRef r, float minAccum, float reach)
        {
            if (!r.IsValid) return "region neplatný";
            var sb = new System.Text.StringBuilder();
            float cell = HydroWorld.CellSize;
            float2 g = (p - r.origin) / cell;
            int cx = (int)floor(g.x), cy = (int)floor(g.y);
            var inflow = new System.Collections.Generic.Dictionary<int, int>();
            var lines = new System.Collections.Generic.List<(float d, string s, int a, int b)>();
            for (int dy = -4; dy <= 4; dy++)
            for (int dx = -4; dx <= 4; dx++)
            {
                int ax = cx + dx, ay = cy + dy;
                if (ax < 0 || ay < 0 || ax >= r.side || ay >= r.side) continue;
                int ai = ay * r.side + ax;
                if (r.accum[ai] < minAccum) continue;
                byte d = r.dir[ai];
                if (d > 7) continue;
                int2 o = HydroWorld.DirOffset(d);
                int bx = ax + o.x, by = ay + o.y;
                if (bx < 0 || by < 0 || bx >= r.side || by >= r.side) continue;
                int bi = by * r.side + bx;
                float2 a = r.origin + (new float2(ax, ay) + 0.5f) * cell;
                float2 b = r.origin + (new float2(bx, by) + 0.5f) * cell;
                float2 ab = b - a;
                float t = saturate(dot(p - a, ab) / dot(ab, ab));
                float dist = length(p - (a + ab * t));
                if (dist > reach) continue;
                inflow.TryGetValue(bi, out int n); inflow[bi] = n + 1;
                lines.Add((dist, string.Format(System.Globalization.CultureInfo.InvariantCulture,
                    "[{0},{1}]→[{2},{3}] d {4:0.0} acc {5:0} hl {6:0.00}→{7:0.00} směr {8:0}°",
                    ax, ay, bx, by, dist, r.accum[ai], r.filled[ai], r.filled[bi],
                    degrees(atan2(ab.y, ab.x))), ai, bi));
            }
            lines.Sort((x, y) => x.d.CompareTo(y.d));
            int junction = 0;
            foreach (var kv in inflow) if (kv.Value >= 2) junction++;
            sb.Append("soutoků (≥2 přítoky do buňky) ").Append(junction).Append(": ");
            foreach (var l in lines) sb.Append(l.s).Append(inflow[l.b] >= 2 ? " (do soutoku)" : "").Append("; ");
            return sb.ToString();
        }

        /// <summary>
        /// Šířka koryta z plochy povodí.
        ///
        /// <para>Odmocnina není libovolná volba: průtok roste zhruba lineárně s plochou
        /// povodí a šířka koryta zhruba s odmocninou průtoku, takže <c>w ~ sqrt(A)</c>
        /// je hydraulická geometrie skutečných řek. Prakticky to znamená, že se přítoky
        /// slévají do znatelně širšího toku, ale řeka neroste donekonečna.</para>
        ///
        /// <para>Nasycení na <paramref name="accumFull"/> je zároveň to, co drží systém
        /// bez švů – viz poznámka u <see cref="HydroRegion"/>.</para>
        /// </summary>
        public static float WidthFromAccum(float accum, float widthMin, float widthMax, float accumFull)
        {
            float t = saturate(sqrt(max(accum, 0f) / max(accumFull, 1f)));
            return lerp(widthMin, widthMax, t);
        }

        /// <summary>Bilineární čtení buňkového pole. Souřadnice je ve STŘEDECH buněk.</summary>
        private static float Bilinear(in NativeArray<float> a, int side, float2 g)
        {
            int x0 = (int)g.x, y0 = (int)g.y;
            int x1 = min(x0 + 1, side - 1), y1 = min(y0 + 1, side - 1);
            float2 f = g - new float2(x0, y0);

            float v00 = a[y0 * side + x0], v10 = a[y0 * side + x1];
            float v01 = a[y1 * side + x0], v11 = a[y1 * side + x1];

            return lerp(lerp(v00, v10, f.x), lerp(v01, v11, f.x), f.y);
        }
    }
}
