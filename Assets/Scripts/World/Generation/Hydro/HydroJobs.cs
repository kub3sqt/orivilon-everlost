using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using static Unity.Mathematics.math;

namespace Orivilon.World.Generation.Hydro
{
    /// <summary>Makro výška na celém pracovním poli regionu. Jediný drahý krok celé stavby.</summary>
    [BurstCompile]
    public struct HydroHeightJob : IJobParallelFor
    {
        public float2 origin;
        public float cell;
        public int side;
        public GenParams gp;
        public SplineSet spl;

        [WriteOnly] public NativeArray<float> raw;

        public void Execute(int i)
        {
            float2 p = origin + new float2(i % side, i / side) * cell;
            WorldGenMath.EvalMacro(p, gp, spl, out float my, out _, out _, out _, out _);
            raw[i] = my;
        }
    }

    /// <summary>
    /// Priority-Flood (Barnes a spol.) – zaplní bezodtoké pánve po úroveň přelivu
    /// a zároveň z toho vypadne všechno ostatní, co je dál potřeba.
    ///
    /// <para>Princip: do haldy se vloží celý okraj pole, pak se opakovaně vyzvedne
    /// NEJNIŽŠÍ buňka a jejím dosud nenavštíveným sousedům se přiřadí
    /// <c>filled = max(vlastní výška, výška vyzvednuté)</c>. Protože se vyzvedává
    /// v neklesajícím pořadí, je první dosažená hodnota rovnou ta nejnižší možná – žádné
    /// snižování klíče v haldě není potřeba a halda nikdy nedrží víc než n prvků.</para>
    ///
    /// <para>Vedle výšek job vrací dvě věci, které jinak stály celý další průchod:</para>
    /// <list type="bullet">
    /// <item><b>parent</b> – soused, ze kterého byla buňka dosažena. Ten je vždycky po proudu,
    /// takže se z něj dá odvodit odtok i uprostřed zaplněné pánve, kde žádný nižší soused není.
    /// Bez toho se voda v jezeře „zasekne" a řeka pod ním nemá odkud téct.</item>
    /// <item><b>order</b> – pořadí vyzvedávání. Obrácené je to topologické pořadí pro akumulaci,
    /// takže se nemusí nic třídit.</item>
    /// </list>
    /// </summary>
    [BurstCompile]
    public struct HydroFloodJob : IJob
    {
        public int side;

        /// <summary>
        /// Hladina moře. Buňka pod ní je výtok stejně jako okraj pole – viz Execute.
        /// </summary>
        public float seaLevel;

        [ReadOnly] public NativeArray<float> raw;

        [WriteOnly] public NativeArray<int> order;
        public NativeArray<float> filled;
        public NativeArray<int> parent;

        /// <summary>Pracovní: 0 = nedotčeno, 1 = v haldě, 2 = vyzvednuto.</summary>
        public NativeArray<byte> state;
        public NativeArray<float> heapKey;
        public NativeArray<int> heapVal;

        private int count;

        public void Execute()
        {
            int n = side * side;
            count = 0;

            for (int i = 0; i < n; i++) { state[i] = 0; parent[i] = -1; }

            // celý okraj pole je „moře" – odtud se zaplavuje dovnitř
            for (int x = 0; x < side; x++)
            {
                Seed(x);
                Seed((side - 1) * side + x);
            }
            for (int y = 1; y < side - 1; y++)
            {
                Seed(y * side);
                Seed(y * side + side - 1);
            }

            // A každá buňka pod hladinou moře je taky výtok. Bez toho se moře uvnitř
            // regionu zaplavovalo jen od okraje pole: mořská pánev se „zaplnila" až na
            // nejnižší přeliv po obvodu regionu, klidně metry až desítky metrů NAD mořem.
            // Řeka pak končila u pobřeží v té výšce (vodopád do moře, hladina visící nad
            // útesem) a nad mořem vznikala falešná jezera s hladinou nad hladinou moře.
            // Rozhoduje jen surová výška buňky, takže se na tom shodnou všechny regiony.
            for (int i = 0; i < n; i++)
                if (raw[i] <= seaLevel) Seed(i);

            int popped = 0;

            while (count > 0)
            {
                int i = Pop();
                state[i] = 2;
                order[popped++] = i;

                int x = i % side, y = i / side;

                for (int d = 0; d < 8; d++)
                {
                    int2 o = HydroWorld.DirOffset(d);
                    int nx = x + o.x, ny = y + o.y;
                    if (nx < 0 || ny < 0 || nx >= side || ny >= side) continue;

                    int ni = ny * side + nx;
                    if (state[ni] != 0) continue;

                    filled[ni] = max(raw[ni], filled[i]);
                    parent[ni] = i;
                    state[ni] = 1;
                    Push(filled[ni], ni);
                }
            }

            // Pojistka: kdyby halda z jakéhokoli důvodu nedojela celé pole, zbylé buňky
            // dostanou vlastní výšku. Lepší mírně jiná hydrologie než nedefinovaná paměť.
            for (int i = 0; i < n; i++)
                if (state[i] != 2) { filled[i] = raw[i]; order[popped++] = i; }
        }

        private void Seed(int i)
        {
            if (state[i] != 0) return;
            filled[i] = raw[i];
            state[i] = 1;
            Push(raw[i], i);
        }

        private void Push(float key, int val)
        {
            int i = count++;
            heapKey[i] = key;
            heapVal[i] = val;

            while (i > 0)
            {
                int p = (i - 1) >> 1;
                if (heapKey[p] <= heapKey[i]) break;
                Swap(p, i);
                i = p;
            }
        }

        private int Pop()
        {
            int top = heapVal[0];
            count--;

            if (count > 0)
            {
                heapKey[0] = heapKey[count];
                heapVal[0] = heapVal[count];

                int i = 0;
                while (true)
                {
                    int l = 2 * i + 1, r = l + 1, s = i;
                    if (l < count && heapKey[l] < heapKey[s]) s = l;
                    if (r < count && heapKey[r] < heapKey[s]) s = r;
                    if (s == i) break;
                    Swap(s, i);
                    i = s;
                }
            }
            return top;
        }

        private void Swap(int a, int b)
        {
            float k = heapKey[a]; heapKey[a] = heapKey[b]; heapKey[b] = k;
            int v = heapVal[a]; heapVal[a] = heapVal[b]; heapVal[b] = v;
        }
    }

    /// <summary>
    /// Směr odtoku D8 nad zaplněnou výškou.
    ///
    /// <para>Kde existuje ostře nižší soused, jde se nejstrmějším spádem – to je to, co
    /// vypadá přirozeně na otevřeném svahu. Kde neexistuje (rovina zaplněné pánve), použije
    /// se rodič ze stromu zaplavování. Cyklus tím vzniknout nemůže: po spádu výška ostře
    /// klesá, a uvnitř roviny pořadí vyzvednutí ostře klesá.</para>
    /// </summary>
    [BurstCompile]
    public struct HydroDirJob : IJobParallelFor
    {
        public int side;

        [ReadOnly] public NativeArray<float> filled;
        [ReadOnly] public NativeArray<int> parent;
        [WriteOnly] public NativeArray<byte> dir;

        public void Execute(int i)
        {
            int x = i % side, y = i / side;
            float h = filled[i];

            float best = 0f;
            int bestD = -1;

            for (int d = 0; d < 8; d++)
            {
                int2 o = HydroWorld.DirOffset(d);
                int nx = x + o.x, ny = y + o.y;
                if (nx < 0 || ny < 0 || nx >= side || ny >= side) continue;

                float len = (o.x != 0 && o.y != 0) ? 1.41421356f : 1f;
                float drop = (h - filled[ny * side + nx]) / len;

                if (drop > best) { best = drop; bestD = d; }
            }

            if (bestD < 0)
            {
                int p = parent[i];
                if (p < 0) { dir[i] = HydroWorld.NoFlow; return; }

                dir[i] = HydroWorld.DirFromDelta(p % side - x, p / side - y);
                return;
            }

            dir[i] = (byte)bestD;
        }
    }

    /// <summary>
    /// Akumulace toku. Každá buňka přispívá sama sebou a pak předává součet po proudu.
    ///
    /// <para>Jde to jedním průchodem, protože obrácené pořadí vyzvedávání z Priority-Flood
    /// JE topologické pořadí: cíl odtoku byl vždycky vyzvednut dřív než buňka sama, ať už
    /// proto, že je níž, nebo proto, že je jejím rodičem ve stromu zaplavování.</para>
    /// </summary>
    [BurstCompile]
    public struct HydroAccumJob : IJob
    {
        public int side;

        [ReadOnly] public NativeArray<int> order;
        [ReadOnly] public NativeArray<byte> dir;
        public NativeArray<float> accum;

        public void Execute()
        {
            for (int i = 0; i < accum.Length; i++) accum[i] = 1f;

            for (int k = order.Length - 1; k >= 0; k--)
            {
                int i = order[k];
                byte d = dir[i];
                if (d > 7) continue;

                int2 o = HydroWorld.DirOffset(d);
                int nx = (i % side) + o.x, ny = (i / side) + o.y;
                if (nx < 0 || ny < 0 || nx >= side || ny >= side) continue;

                accum[ny * side + nx] += accum[i];
            }
        }
    }

    /// <summary>
    /// Vyřízne z pracovního pole region i s prstencem. Halo se dál nedrží – je to 2,25×
    /// víc paměti než užitečná data a po výpočtu už z něj nikdo nečte.
    /// </summary>
    [BurstCompile]
    public struct HydroCopyJob : IJobParallelFor
    {
        public int srcSide, dstSide, offset;

        [ReadOnly] public NativeArray<float> srcFilled, srcRaw, srcAccum;
        [ReadOnly] public NativeArray<byte> srcDir;

        [WriteOnly] public NativeArray<float> dstFilled, dstRaw, dstAccum;
        [WriteOnly] public NativeArray<byte> dstDir;

        public void Execute(int i)
        {
            int x = i % dstSide, y = i / dstSide;
            int s = (y + offset) * srcSide + (x + offset);

            dstFilled[i] = srcFilled[s];
            dstRaw[i] = srcRaw[s];
            dstAccum[i] = srcAccum[s];
            dstDir[i] = srcDir[s];
        }
    }
}
