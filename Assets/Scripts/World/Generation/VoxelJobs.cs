using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;
using static Unity.Mathematics.math;

namespace Orivilon.World.Generation
{
    /// <summary>
    /// Kroky 11–16 pipeline – převod výškové mapy na 3D hustotu.
    ///
    /// Základ je normalizovaná svislá vzdálenost od povrchu; teprve deformace vzorkovací
    /// souřadnice, jeskyně a brány z toho dělají něco, co výšková mapa neumí.
    /// </summary>
    [BurstCompile]
    public struct DensityJob : IJobParallelFor
    {
        public int dim;                 // vzorků na hranu (33)
        public int columnSide;          // hrana sloupcové mřížky včetně okraje
        public int columnPad;           // okraj sloupcové mřížky ve voxelech
        public float2 columnOrigin;     // světová pozice sloupcového vzorku (0,0)
        public float voxelSize;
        public float3 chunkOrigin;      // světová pozice vzorku (0,0,0)
        public float falloff;

        /// <summary>Rozteč hranic LOD0 sloupců (viz LodSeamJob). Nula = bez rozšíření pásu na švech.</summary>
        public float seamLine;
        public float worldMinY, worldMaxY;
        public GenParams gp;

        [ReadOnly] public NativeArray<float> surfY;
        [ReadOnly] public NativeArray<float> overhang;

        /// <summary>Spojitá vzdálenost od vody 0–1 (ColumnField.waterGate) – u vody se jeskyně nesmí otevřít.</summary>
        [ReadOnly] public NativeArray<float> waterGate;
        [ReadOnly] public NativeArray<ArchBody> arches;
        public int2 archCellMin;
        public int archCellsX, archCellsZ;

        /// <summary>
        /// Úpravy hráče pro tenhle chunk, kvantované na sbyte. Když chunk nikdo neupravil,
        /// je to jednoprvkové prázdné pole a <see cref="hasDelta"/> je false – job musí
        /// dostat platnou kolekci, i když z ní nic nečte.
        /// </summary>
        [ReadOnly] public NativeArray<sbyte> delta;
        public bool hasDelta;

        [WriteOnly] public NativeArray<float> density;

        /// <summary>
        /// Bilineární čtení výšky z paddované sloupcové mřížky. Kvůli tomuhle ten okraj je:
        /// 3D warp se ptá na výšku až overhangAmp metrů mimo chunk.
        /// </summary>
        private float SampleSurf(float2 world)
        {
            float2 g = (world - columnOrigin) / voxelSize;
            g = clamp(g, 0f, columnSide - 1.001f);

            int x0 = (int)g.x, z0 = (int)g.y;
            int x1 = min(x0 + 1, columnSide - 1), z1 = min(z0 + 1, columnSide - 1);
            float fx = g.x - x0, fz = g.y - z0;

            float a = surfY[z0 * columnSide + x0];
            float b = surfY[z0 * columnSide + x1];
            float c = surfY[z1 * columnSide + x0];
            float e = surfY[z1 * columnSide + x1];
            return lerp(lerp(a, b, fx), lerp(c, e, fx), fz);
        }

        /// <summary>
        /// Mez oříznutí hustoty, na přímkách švu LOD rozšířená.
        ///
        /// <para>Hustota je (povrch − y) / falloff oříznutá na ⟨−1, 1⟩. Marching cubes hledá
        /// průsečík hrany lineárně mezi dvěma vzorky, takže je přesný, jen když ani jeden
        /// konec hrany není oříznutý. Ve stěně švu leží vodorovné hrany délky kroku LOD –
        /// v LOD4 16 m. Na útesu se sklonem 4 se výška na takové hraně změní o 64 m, víc
        /// než falloff 12 m, a průsečík ujede: LOD3 a LOD4 pak našly na téže přímce povrch
        /// až 20 m od sebe, přestože výškové pole tam mají shodné.</para>
        ///
        /// <para>Sklon hustoty (1/falloff) zůstává všude stejný – jen se v pásu kolem přímky
        /// švu posune mez oříznutí tak, aby hrana vlastního LOD nebyla oříznutá do sklonu 4.
        /// Stačí to na každé straně zvlášť (průsečík nelineárního pole je pak přesný na obou),
        /// takže mez smí záviset na vlastním kroku. LOD0 a LOD1 zůstávají na ⟨−1, 1⟩, a tedy
        /// i kopání u hráče se nemění. Zkouška s rozšířeným falloff místo meze švy zhoršila:
        /// každá drobná 3D odchylka hustoty se tím v metrech zvětšila několikrát.</para>
        /// </summary>
        private float SeamLimit(float2 p)
        {
            float own = max(1f, 4f * voxelSize / falloff);
            if (seamLine <= 0f || own <= 1f) return 1f;
            float2 ln = round(p / seamLine) * seamLine;
            float2 dd = abs(p - ln);
            float w = 0f;
            int mx = LodSeamJob.LineLevel(ln.x, seamLine), mz = LodSeamJob.LineLevel(ln.y, seamLine);
            if (mx > 0) w = max(w, 1f - smoothstep(0f, max(LodSeamJob.Band(mx, seamLine), voxelSize), dd.x));
            if (mz > 0) w = max(w, 1f - smoothstep(0f, max(LodSeamJob.Band(mz, seamLine), voxelSize), dd.y));
            return lerp(1f, own, w);
        }

        public void Execute(int i)
        {
            int x = i % dim;
            int y = (i / dim) % dim;
            int z = i / (dim * dim);

            float3 w = chunkOrigin + new float3(x, y, z) * voxelSize;
            int ci = (z + columnPad) * columnSide + (x + columnPad);

            float surf = surfY[ci];
            float mask = overhang[ci];

            // 10) deformace vzorkovací souřadnice → převisy
            float3 warp = Density3D.OverhangWarp(w, gp, mask);

            // 11) základní hustota; kladná pod povrchem, nasycená za pásem falloff
            float sw = mask > 0.001f ? SampleSurf(w.xz + warp.xz) : surf;
            float lim = SeamLimit(w.xz);
            float d = clamp((sw - (w.y + warp.y)) / falloff, -lim, lim);

            // 12) nevýškopisný detail v pásu kolem povrchu
            d += Density3D.SurfaceDetail(w, gp, mask, surf);

            // 13, 14) jeskyně a vstupy
            // Přímka 0.9 - carve*4.5 protne nulu při carve = 0.2, takže se dutina otevře
            // brzy a průchozí šířka odpovídá zadané. Dřív tu bylo *2.2, tedy práh 0.41 –
            // z nominálních 6.5 m zbývalo sotva 3.8 m skutečného průchodu.
            // Úsek musí zůstat kladný při carve = 0, jinak by smin srazil hustotu i v kameni.
            // Kolo 12b: hloubka pod SKUTEČNÝM povrchem. Převisový warp posouvá povrch na
            // strmém svahu až o ~10 m proti nedeformovanému sloupci; brána jeskyní u povrchu
            // (vstupy, strop u vody) se ale počítala od nedeformovaného `surf`. Tunel, který
            // měl být 8 m pod povrchem, tak prorážel svah úzkými štěrbinami po celé jeho
            // délce. Bez warpu (mask 0) je to přesně surf − y jako dřív.
            float below = surf - w.y;
            if (gp.Calm(4) && mask > 0.001f) below = sw - (w.y + warp.y);
            float carve = Density3D.CaveCarve(w, gp, below) * gp.caveStrength;

            // Pod vodou a u břehu se jeskyně nesmí otevřít k povrchu: dno řeky, jezera
            // i moře by dostalo díru, přes kterou je průhlednou hladinou vidět do tmy,
            // a hladina by nad ní visela. Horních ~10 m pod povrchem je tam proto plných;
            // hlouběji jeskyně běží dál a výš na souši (8 m nad vodou) se nic nemění.
            if (carve > 0.001f)
            {
                float dry = waterGate[ci];
                float roof = smoothstep(6f, 14f, below);
                carve *= lerp(roof, 1f, dry);
            }
            if (carve > 0.001f)
                d = GenNoise.SMin(d, 0.9f - carve * 4.5f, gp.caveSmooth);

            // 15) skalní brány z okolních buněk
            for (int oz = -1; oz <= 1; oz++)
            for (int ox = -1; ox <= 1; ox++)
            {
                int ax = (int)floor(w.x / gp.archCell) + ox - archCellMin.x;
                int az = (int)floor(w.z / gp.archCell) + oz - archCellMin.y;
                if (ax < 0 || az < 0 || ax >= archCellsX || az >= archCellsZ) continue;
                d = Density3D.ApplyArch(d, w, gp, arches[az * archCellsX + ax]);
            }

            // 16) podloží a strop světa.
            //
            // POZOR na saturate. Nad podložím je (worldMinY + 4 - y) záporné a saturate
            // by ho ořízlo na 0, takže by max(d, 0) udělalo z celého světa kámen s hustotou
            // nikdy zápornou. Vzduch by měl přesně nulu, marching cubes bere za kámen d > 0,
            // a průsečík hrany by vyšel t = da/(da-0) = 1 – každý vrchol by skočil na uzel
            // mřížky a povrch by se změnil na blokové schody. Musí to být clamp na ⟨-1, 1⟩.
            // 16½) úpravy hráče. Přičítá se PŘED podložím a stropem, takže se do podloží
            //      nedá prokopat a nad strop světa nedá přisypat.
            if (hasDelta) d += delta[i] * VoxelEdits.Scale;

            d = max(d, clamp((worldMinY + 4f - w.y) * 0.5f, -lim, lim));
            d = min(d, clamp((worldMaxY - w.y) * 0.5f, -lim, lim));

            density[i] = clamp(d, -lim, lim);
        }
    }

    /// <summary>
    /// Hrubý test, jestli chunky pod povrchem vůbec obsahují jeskyni.
    ///
    /// Bez něj by se musel generovat každý svislý chunk až k podloží, protože jeskyně sahají
    /// všude. Vzorkuje se řídce (9³ = 729 bodů místo 35 937), což stačí – jeskynní pole má
    /// nízkou frekvenci a tunel široký deset metrů se na mřížce po čtyřech metrech neschová.
    ///
    /// Běží pro celý svislý sloupec naráz, jeden index = jeden chunk.
    /// </summary>
    [BurstCompile]
    public struct CaveProbeJob : IJobParallelFor
    {
        public int steps;
        public int minChunkY;
        public float chunkSpan;
        public float2 chunkOriginXZ;
        public float2 columnOrigin;
        public int columnSide;
        public float voxelSize;
        public GenParams gp;

        [ReadOnly] public NativeArray<float> surfY;
        [WriteOnly] public NativeArray<float> maxCarve;

        public void Execute(int i)
        {
            float baseY = (minChunkY + i) * chunkSpan;
            float step = chunkSpan / (steps - 1);
            float best = 0f;

            for (int zi = 0; zi < steps; zi++)
            for (int xi = 0; xi < steps; xi++)
            {
                float2 xz = chunkOriginXZ + new float2(xi, zi) * step;
                float2 g = clamp((xz - columnOrigin) / voxelSize, 0f, columnSide - 1.001f);
                float s = surfY[(int)g.y * columnSide + (int)g.x];

                for (int yi = 0; yi < steps; yi++)
                {
                    float wy = baseY + yi * step;
                    float c = Density3D.CaveCarve(new float3(xz.x, wy, xz.y), gp, s - wy);
                    if (c > best) best = c;
                }
            }

            maxCarve[i] = best * gp.caveStrength;
        }
    }

    /// <summary>Vyplní pole indexů vrcholů hodnotou -1 před každým chunkem.</summary>
    [BurstCompile]
    public struct ResetEdgeMapJob : IJobParallelFor
    {
        [WriteOnly] public NativeArray<int> edgeVertex;
        public void Execute(int i) => edgeVertex[i] = -1;
    }

    /// <summary>
    /// Krok 17 – marching cubes.
    ///
    /// Běží jako jeden IJob na chunk: buňky sdílejí vrcholy přes pole vlastnictví hran,
    /// což paralelizaci uvnitř chunku komplikuje víc, než kolik by přinesla. Paralelismus
    /// je na úrovni chunků.
    ///
    /// Výstupem jsou dva meshe – viz <see cref="ChunkMeshData"/>.
    /// </summary>
    [BurstCompile]
    public struct MarchingCubesJob : IJob
    {
        public int dim;
        public float voxelSize;
        public float3 chunkOrigin;

        /// <summary>
        /// Přichycení průsečíku ke krajům hrany. Bez něj vznikají velmi tenké trojúhelníky,
        /// které s flat shadingem blikají jako tmavé střepy. Se snapem se dva vrcholy slijí,
        /// trojúhelník zdegeneruje a zahodí se – fasety se tím zvětší, což low-poly svědčí.
        /// </summary>
        public float edgeSnap;

        /// <summary>Jen pro měření: přichytávat i na stěnách chunku (chování do kola 3).</summary>
        public bool snapWalls;

        /// <summary>
        /// Jak hluboko visí sukně na svislých stěnách chunku, ve světových jednotkách.
        /// Nula = žádná sukně.
        ///
        /// <para>Sousedí-li dva chunky v různém LOD, popisuje každý tutéž plochu jinak
        /// jemně a na hranici zůstane škvíra, kterou je vidět skrz svět. Sukně je pás
        /// geometrie svěšený z okrajové hrany dolů: škvíru zakryje, aniž by bylo potřeba
        /// řešit přechodové buňky (Transvoxel). Uvnitř stejného LOD leží celá v kameni,
        /// takže tam nic nestojí ani nekazí.</para>
        /// </summary>
        public float skirtDepth;

        public TerrainPalette palette;

        /// <summary>Offset šumu mikro-variace barvy. Z GenParams.offMicro.</summary>
        public float3 microOffset;

        /// <summary>
        /// Nastavení mikro-biomů. Barva se z nich počítá analyticky z polohy trojúhelníku,
        /// ne ze sloupcového pole – skvrna má měkký okraj a ten by se ve sloupcích po metru
        /// zaokrouhlil na schody.
        /// </summary>
        public MicroParams micro;

        /// <summary>Hladina moře. Zlatý háj pod ní nevzniká.</summary>
        public float seaLevel;

        /// <summary>Klima sloupce. Barva fasety se z něj čte bilineárně v jejím těžišti.</summary>
        [ReadOnly] public NativeArray<float> columnTemp;
        [ReadOnly] public NativeArray<float> columnHum;

        /// <summary>Hladina nejbližší vody pro břehový písek. Viz ColumnField.shoreY.</summary>
        [ReadOnly] public NativeArray<float> columnShoreY;

        /// <summary>Kolo 12: síla terasování sloupce (ColumnField.cliff) pro barvení stupňů.</summary>
        [ReadOnly] public NativeArray<float> columnCliff;
        public float2 columnOrigin;
        public float columnStep;
        public int columnSide;

        [ReadOnly] public NativeArray<float> density;
        [ReadOnly] public NativeArray<sbyte> triTable;
        [ReadOnly] public NativeArray<sbyte> edgeOwner;
        [ReadOnly] public NativeArray<sbyte> edgeCorners;
        [ReadOnly] public NativeArray<sbyte> cornerOffset;

        public NativeArray<int> edgeVertex;   // dim^3 * 3, předvyplněno -1

        public NativeList<float3> solidVerts;
        public NativeList<int> solidTris;
        public NativeList<float3> renderVerts;
        public NativeList<float3> renderNormals;
        public NativeList<Color32> renderColors;
        public NativeList<int> renderTris;

        private int SampleIndex(int x, int y, int z) => (z * dim + y) * dim + x;

        /// <summary>Bilineární čtení sloupcového pole. Klima je hladké, takže to stačí.</summary>
        private float SampleColumn(in NativeArray<float> field, float2 world)
        {
            float2 g = clamp((world - columnOrigin) / columnStep, 0f, columnSide - 1.001f);
            int x0 = (int)g.x, z0 = (int)g.y;
            int x1 = min(x0 + 1, columnSide - 1), z1 = min(z0 + 1, columnSide - 1);
            float2 f = g - new float2(x0, z0);

            float a = field[z0 * columnSide + x0], b = field[z0 * columnSide + x1];
            float c = field[z1 * columnSide + x0], d = field[z1 * columnSide + x1];
            return lerp(lerp(a, b, f.x), lerp(c, d, f.x), f.y);
        }

        public void Execute()
        {
            int cells = dim - 1;

            for (int z = 0; z < cells; z++)
            for (int y = 0; y < cells; y++)
            for (int x = 0; x < cells; x++)
            {
                int caseIndex = 0;
                for (int c = 0; c < 8; c++)
                {
                    int o = c * 3;
                    float d = density[SampleIndex(x + cornerOffset[o], y + cornerOffset[o + 1],
                                                  z + cornerOffset[o + 2])];
                    if (d > 0f) caseIndex |= 1 << c;
                }

                if (caseIndex == 0 || caseIndex == 255) continue;

                int row = caseIndex * 16;
                for (int k = 0; k < 15; k += 3)
                {
                    sbyte e0 = triTable[row + k];
                    if (e0 < 0) break;

                    int i0 = GetOrCreateVertex(x, y, z, e0);
                    int i1 = GetOrCreateVertex(x, y, z, triTable[row + k + 1]);
                    int i2 = GetOrCreateVertex(x, y, z, triTable[row + k + 2]);

                    // Po přichycení mohou dva vrcholy splynout – takový trojúhelník zahodíme.
                    if (i0 == i1 || i1 == i2 || i0 == i2) continue;

                    solidTris.Add(i0);
                    solidTris.Add(i1);
                    solidTris.Add(i2);
                }
            }

            BuildFlatShadedMesh();
            BuildSkirts();
        }

        private int GetOrCreateVertex(int x, int y, int z, int edge)
        {
            int o = edge * 4;
            int ex = x + edgeOwner[o];
            int ey = y + edgeOwner[o + 1];
            int ez = z + edgeOwner[o + 2];
            int axis = edgeOwner[o + 3];

            int key = ((ez * dim + ey) * dim + ex) * 3 + axis;
            int existing = edgeVertex[key];
            if (existing >= 0) return existing;

            // Kanonická dvojice rohů (vždy ve směru rostoucí osy) zaručí, že sousední buňka
            // spočítá pro tutéž světovou hranu bit po bitu stejnou pozici → žádné škvíry.
            int ca = edgeCorners[edge * 2];
            int cb = edgeCorners[edge * 2 + 1];
            int oa = ca * 3, ob = cb * 3;

            int3 pa = new int3(x + cornerOffset[oa], y + cornerOffset[oa + 1], z + cornerOffset[oa + 2]);
            int3 pb = new int3(x + cornerOffset[ob], y + cornerOffset[ob + 1], z + cornerOffset[ob + 2]);

            float da = density[SampleIndex(pa.x, pa.y, pa.z)];
            float db = density[SampleIndex(pb.x, pb.y, pb.z)];

            float denom = da - db;
            float t = abs(denom) < 1e-7f ? 0.5f : da / denom;

            // Na svislé stěně chunku se nepřichytává. Stěna je místo, kde se může potkat
            // jiný LOD, a přichycení posune vrchol o edgeSnap · délka hrany – v LOD3 (hrana
            // 8 m) o 0,48 m, v LOD2 o 0,24 m. Obě strany hranice tak našly tentýž povrch
            // až o 0,7 m jinde (naměřeno na přímce z = −512, seed 1337). K rohu na stěně se
            // proto nepřichytává ani hrana, která ze stěny jen vychází: vrchol by jinak
            // ležel ve stěně ve výšce uzlu mřížky místo skutečného průsečíku.
            bool aWall = !snapWalls && (pa.x == 0 || pa.x == dim - 1 || pa.z == 0 || pa.z == dim - 1);
            bool bWall = !snapWalls && (pb.x == 0 || pb.x == dim - 1 || pb.z == 0 || pb.z == dim - 1);
            if (t < edgeSnap) { if (!aWall) t = 0f; }
            else if (t > 1f - edgeSnap) { if (!bWall) t = 1f; }

            float3 world = chunkOrigin + (lerp((float3)pa, (float3)pb, t)) * voxelSize;

            int index = solidVerts.Length;
            solidVerts.Add(world);
            edgeVertex[key] = index;
            return index;
        }

        /// <summary>
        /// Rozbalení indexovaného meshe na nesdílené vrcholy. Každá faseta dostane vlastní
        /// normálu a vlastní barvu – bez interpolace mezi sousedy, což je přesně ten low-poly vzhled.
        /// </summary>
        private void BuildFlatShadedMesh()
        {
            for (int t = 0; t < solidTris.Length; t += 3)
            {
                float3 p0 = solidVerts[solidTris[t]];
                float3 p1 = solidVerts[solidTris[t + 1]];
                float3 p2 = solidVerts[solidTris[t + 2]];

                float3 n = cross(p1 - p0, p2 - p0);
                float len = length(n);
                if (len < 1e-9f) continue;     // zbylá degenerace
                n /= len;

                float3 centroid = (p0 + p1 + p2) * (1f / 3f);
                Color32 col = ColorAt(centroid, n.y);

                int b = renderVerts.Length;
                renderVerts.Add(p0);   renderVerts.Add(p1);   renderVerts.Add(p2);
                renderNormals.Add(n);  renderNormals.Add(n);  renderNormals.Add(n);
                renderColors.Add(col); renderColors.Add(col); renderColors.Add(col);
                renderTris.Add(b);     renderTris.Add(b + 1); renderTris.Add(b + 2);
            }
        }

        /// <summary>
        /// Svěsí sukni z každé hrany, která leží celá ve svislé stěně chunku.
        ///
        /// <para>Hrana meshe na stěně chunku patří uvnitř chunku právě jednomu trojúhelníku –
        /// je to okraj plochy. Stačí tedy projít trojúhelníky a u každého se podívat na jeho
        /// tři hrany; ta, jejíž oba konce leží v jedné stěně, dostane pás dolů. Není potřeba
        /// nic evidovat ani třídit.</para>
        ///
        /// <para>Vinutí se neodvozuje z pořadí vrcholů, ale porovnáním normály pásu s vnější
        /// normálou stěny. Případů je osm (čtyři stěny × dvě orientace plochy) a rozebírat je
        /// ručně je zbytečně křehké – skalární součin je vyřeší všechny naráz.</para>
        /// </summary>
        private void BuildSkirts()
        {
            if (skirtDepth <= 0f) return;

            float cells = dim - 1;
            float eps = voxelSize * 1e-3f;
            float minX = chunkOrigin.x, maxX = chunkOrigin.x + cells * voxelSize;
            float minZ = chunkOrigin.z, maxZ = chunkOrigin.z + cells * voxelSize;

            for (int t = 0; t < solidTris.Length; t += 3)
            {
                float3 p0 = solidVerts[solidTris[t]];
                float3 p1 = solidVerts[solidTris[t + 1]];
                float3 p2 = solidVerts[solidTris[t + 2]];

                float3 n = cross(p1 - p0, p2 - p0);
                float len = length(n);
                if (len < 1e-9f) continue;
                n /= len;

                float3 centroid = (p0 + p1 + p2) * (1f / 3f);
                Color32 col = ColorAt(centroid, n.y);

                SkirtEdge(p0, p1, col, minX, maxX, minZ, maxZ, eps);
                SkirtEdge(p1, p2, col, minX, maxX, minZ, maxZ, eps);
                SkirtEdge(p2, p0, col, minX, maxX, minZ, maxZ, eps);
            }
        }

        private Color32 ColorAt(float3 centroid, float normalY)
        {
            float temp = SampleColumn(columnTemp, centroid.xz);
            float hum = SampleColumn(columnHum, centroid.xz);

            float shoreY = columnShoreY.IsCreated
                ? SampleColumn(columnShoreY, centroid.xz)
                : TerrainPalette.NoShore;

            float cliffW = columnCliff.IsCreated ? SampleColumn(columnCliff, centroid.xz) : 0f;

            float3 rgb = palette.art > 0.5f
                ? palette.EvaluateArt(centroid, normalY, temp, hum, shoreY, microOffset, cliffW)
                : palette.Evaluate(centroid.y, normalY, temp, hum, shoreY);
            if (palette.diag > 1.5f && palette.diag < 2.5f) rgb = new float3(0.5f);
            if (palette.diag > 1.5f && palette.diag < 4.5f)
                return new Color32((byte)(rgb.x * 255f), (byte)(rgb.y * 255f), (byte)(rgb.z * 255f), 255);
            if (!(palette.diag > 4.5f && palette.diag < 5.5f))
                rgb = saturate(palette.Vary(rgb, centroid, microOffset));

            // Mikro-biom až po mikro-variaci: ta má rozbíjet plochu, ne odstín místa.
            // Opačné pořadí by zlatý háj i čedič zase rozostřilo zpátky do okolní barvy.
            MicroBiomeMath.SampleSurface(centroid.xz, micro, centroid.y, seaLevel,
                                         out int mk, out float mw);
            rgb = saturate(MicroBiomeMath.Tint(rgb, mk, mw, normalY));
            return new Color32((byte)(rgb.x * 255f), (byte)(rgb.y * 255f), (byte)(rgb.z * 255f), 255);
        }

        private void SkirtEdge(float3 a, float3 b, Color32 col,
                               float minX, float maxX, float minZ, float maxZ, float eps)
        {
            float3 outward;
            if      (abs(a.x - minX) < eps && abs(b.x - minX) < eps) outward = new float3(-1f, 0f, 0f);
            else if (abs(a.x - maxX) < eps && abs(b.x - maxX) < eps) outward = new float3( 1f, 0f, 0f);
            else if (abs(a.z - minZ) < eps && abs(b.z - minZ) < eps) outward = new float3(0f, 0f, -1f);
            else if (abs(a.z - maxZ) < eps && abs(b.z - maxZ) < eps) outward = new float3(0f, 0f,  1f);
            else return;

            float3 drop = new float3(0f, -skirtDepth, 0f);
            float3 ad = a + drop, bd = b + drop;

            // Normála pásu vinutého (a, b, bd). Svislý posun je kolmý na hranu, takže
            // stačí součin hrany a posunu.
            float3 n = cross(b - a, drop);
            float l = length(n);
            if (l < 1e-9f) return;          // hrana je svislá, pás by byl nulový
            n /= l;

            // Pás musí koukat ven z chunku, jinak ho odřízne backface culling a škvíra zůstane.
            bool flip = dot(n, outward) < 0f;
            if (flip) n = -n;

            AddSkirtTri(a, b, bd, n, col, flip);
            AddSkirtTri(a, bd, ad, n, col, flip);
        }

        private void AddSkirtTri(float3 v0, float3 v1, float3 v2, float3 n, Color32 col, bool flip)
        {
            if (flip) { float3 tmp = v1; v1 = v2; v2 = tmp; }

            int i = renderVerts.Length;
            renderVerts.Add(v0);   renderVerts.Add(v1);   renderVerts.Add(v2);
            renderNormals.Add(n);  renderNormals.Add(n);  renderNormals.Add(n);
            renderColors.Add(col); renderColors.Add(col); renderColors.Add(col);
            renderTris.Add(i);     renderTris.Add(i + 1); renderTris.Add(i + 2);
        }
    }
}
