using System;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;
using static Unity.Mathematics.math;

namespace Orivilon.World.Generation
{
    /// <summary>
    /// Úpravy terénu hráčem – kopání a přisypávání.
    ///
    /// <para><b>Ukládá se rozdíl, ne výsledek.</b> Základní hustota je čistá funkce
    /// <c>(seed, pozice)</c>, takže se nikam neukládat nemusí – vygeneruje se pokaždé znovu.
    /// Do savu jde jen to, co hráč skutečně změnil. Chunk, kterého se nikdo nedotkl, nemá
    /// v savu ani bajt a save roste s aktivitou hráče, ne s velikostí prozkoumaného světa.</para>
    ///
    /// <para><b>Štětec je aditivní, ne smooth-min.</b> Návrh počítal s <c>SMin(dStaré, koule)</c>,
    /// jenže k tomu je potřeba znát <c>dStaré</c>, tedy vygenerovat hustotu chunku – a její
    /// vstup, sloupcové pole, se po dostavění chunků uvolňuje. Přičtení hladkého záporného
    /// hrbolu dá stejný tvar, nepotřebuje nic číst a navíc z něj vypadne postupné kopání
    /// zadarmo: každý úder ubere kus, po dosažení −1 je díra hotová. Operace je celočíselná
    /// nad <c>sbyte</c>, takže dva klienti ze stejné události spočítají bit po bitu totéž.</para>
    /// </summary>
    public sealed class VoxelEdits : IDisposable
    {
        /// <summary>Krok kvantizace. −127…127 pokrývá celý rozsah hustoty −1…1.</summary>
        public const float Scale = 1f / 127f;

        private readonly Dictionary<int3, NativeArray<sbyte>> deltas = new Dictionary<int3, NativeArray<sbyte>>();

        /// <summary>
        /// Svislý rozsah upravených chunků v každém sloupci. Bez něj by prořezávání
        /// odkopanou dutinu pod povrchem prohlásilo za plný kámen a nikdy ji nezmeshovalo,
        /// a přisypaný kopec nad původním terénem by se ořízl.
        /// </summary>
        private readonly Dictionary<int2, int2> columnRange = new Dictionary<int2, int2>();

        /// <summary>Prázdné pole pro chunky bez úprav. Job musí dostat platnou kolekci.</summary>
        private NativeArray<sbyte> empty;

        private readonly float voxelSize;
        private readonly float span;
        private bool disposed;

        /// <summary>Chunky, jejichž delta se od posledního dotazu změnila.</summary>
        public readonly HashSet<int3> Dirty = new HashSet<int3>();

        public int EditedChunks => deltas.Count;

        public VoxelEdits(float voxelSize)
        {
            this.voxelSize = voxelSize;
            span = VoxelWorld.ChunkDim * voxelSize;
            empty = new NativeArray<sbyte>(1, Allocator.Persistent);
        }

        /// <summary>Má tenhle chunk nějakou úpravu?</summary>
        public bool Has(int3 coord) => deltas.ContainsKey(coord);

        /// <summary>Svislý rozsah upravených chunků sloupce. False, když sloupec nikdo neupravil.</summary>
        public bool ColumnRange(int2 column, out int minChunkY, out int maxChunkY)
        {
            if (columnRange.TryGetValue(column, out int2 r))
            {
                minChunkY = r.x; maxChunkY = r.y;
                return true;
            }
            minChunkY = maxChunkY = 0;
            return false;
        }

        /// <summary>Delta chunku, nebo prázdné pole. Vždy platná kolekce pro job.</summary>
        public NativeArray<sbyte> Get(int3 coord, out bool has)
        {
            has = deltas.TryGetValue(coord, out NativeArray<sbyte> d);
            return has ? d : empty;
        }

        private NativeArray<sbyte> Ensure(int3 coord)
        {
            if (deltas.TryGetValue(coord, out NativeArray<sbyte> d)) return d;

            int n = VoxelWorld.SampleDim;
            d = new NativeArray<sbyte>(n * n * n, Allocator.Persistent);   // vynulované
            deltas[coord] = d;

            var col = new int2(coord.x, coord.z);
            columnRange[col] = columnRange.TryGetValue(col, out int2 r)
                ? new int2(min(r.x, coord.y), max(r.y, coord.y))
                : new int2(coord.y, coord.y);

            return d;
        }

        // ── štětec ─────────────────────────────────────────────────────

        /// <summary>
        /// Přičte kouli do delty. Kladná síla přidává hmotu, záporná ubírá.
        ///
        /// <para>Sousední chunky sdílejí hraniční vzorek. Protože se příspěvek počítá
        /// analyticky ze světové pozice, vyjde v obou chuncích stejný a povrch na hranici
        /// zůstane spojitý – i když se každý z nich meshuje jindy.</para>
        /// </summary>
        /// <returns>Chunky, kterých se úprava dotkla.</returns>
        public bool Apply(float3 center, float radius, float strength)
        {
            if (radius <= 0f || abs(strength) < 1e-3f) return false;

            int dim = VoxelWorld.SampleDim;
            int cells = VoxelWorld.ChunkDim;

            // Chunk k pokrývá vzorky ⟨k·span, (k+1)·span⟩ včetně obou konců, takže se
            // sousedé o jednu řadu překrývají a hraniční vzorek se zapíše do obou.
            int3 lo = (int3)floor((center - radius) / span) - 1;
            int3 hi = (int3)floor((center + radius) / span);

            VoxelWorld.ChunkYRange(voxelSize, out int wMinY, out int wMaxY);
            lo.y = max(lo.y, wMinY);
            hi.y = min(hi.y, wMaxY);

            bool touched = false;
            float r2 = radius * radius;

            for (int cz = lo.z; cz <= hi.z; cz++)
            for (int cy = lo.y; cy <= hi.y; cy++)
            for (int cx = lo.x; cx <= hi.x; cx++)
            {
                var coord = new int3(cx, cy, cz);
                float3 origin = VoxelWorld.ChunkOrigin(coord, voxelSize);

                // Rozsah vzorků, které vůbec můžou být v kouli.
                int3 sLo = (int3)max(floor((center - radius - origin) / voxelSize), 0f);
                int3 sHi = (int3)min(ceil((center + radius - origin) / voxelSize), cells);
                if (any(sLo > sHi)) continue;

                NativeArray<sbyte> d = default;
                bool created = false;

                for (int z = sLo.z; z <= sHi.z; z++)
                for (int y = sLo.y; y <= sHi.y; y++)
                for (int x = sLo.x; x <= sHi.x; x++)
                {
                    float3 w = origin + new float3(x, y, z) * voxelSize;
                    float3 v = w - center;
                    float dist2 = dot(v, v);
                    if (dist2 >= r2) continue;

                    // Plný účinek uvnitř 60 % poloměru, pak měkký okraj – ostrá hrana
                    // by na flat shadingu udělala schod z jednotlivých faset.
                    float t = sqrt(dist2) / radius;
                    float amt = strength * (1f - smoothstep(0.6f, 1f, t));
                    int add = (int)round(amt * 127f);
                    if (add == 0) continue;

                    if (!created) { d = Ensure(coord); created = true; }

                    int i = (z * dim + y) * dim + x;
                    d[i] = (sbyte)clamp(d[i] + add, -127, 127);
                }

                if (created) { Dirty.Add(coord); touched = true; }
            }

            return touched;
        }

        // ── ukládání ───────────────────────────────────────────────────

        /// <summary>
        /// Jeden upravený chunk v podobě, kterou zvládne <c>JsonUtility</c>.
        /// Base64 blob je tam proto, že <c>sbyte[]</c> se serializuje nespolehlivě a
        /// <c>byte[]</c> by se vypsalo jako seznam čísel – u tisíců voxelů zbytečně nafouklý.
        /// </summary>
        [Serializable]
        public class ChunkDiff
        {
            public int cx, cy, cz;
            public int generatorVersion;
            public string packed;
        }

        /// <summary>
        /// Verze generátoru. Delta sedí na konkrétní základní hustotě – když se změní šum,
        /// popisuje stará delta díru někde ve vzduchu. Při nesouladu se diff zahodí.
        /// </summary>
        public const int GeneratorVersion = 1;

        public List<ChunkDiff> Export()
        {
            var list = new List<ChunkDiff>(deltas.Count);

            foreach (var kv in deltas)
            {
                NativeArray<sbyte> d = kv.Value;

                int count = 0;
                for (int i = 0; i < d.Length; i++) if (d[i] != 0) count++;
                if (count == 0) continue;

                // [n int32][indexy int32 × n][hodnoty sbyte × n]
                var bytes = new byte[4 + count * 5];
                Buffer.BlockCopy(BitConverter.GetBytes(count), 0, bytes, 0, 4);

                int p = 4, q = 4 + count * 4;
                for (int i = 0; i < d.Length; i++)
                {
                    if (d[i] == 0) continue;
                    Buffer.BlockCopy(BitConverter.GetBytes(i), 0, bytes, p, 4);
                    p += 4;
                    bytes[q++] = unchecked((byte)d[i]);
                }

                list.Add(new ChunkDiff
                {
                    cx = kv.Key.x, cy = kv.Key.y, cz = kv.Key.z,
                    generatorVersion = GeneratorVersion,
                    packed = Convert.ToBase64String(bytes),
                });
            }

            return list;
        }

        /// <summary>Načte uložené úpravy. Vrací počet zahozených diffů z jiné verze generátoru.</summary>
        public int Import(List<ChunkDiff> list)
        {
            if (list == null) return 0;

            int skipped = 0;
            foreach (ChunkDiff diff in list)
            {
                if (diff == null || string.IsNullOrEmpty(diff.packed)) continue;
                if (diff.generatorVersion != GeneratorVersion) { skipped++; continue; }

                byte[] bytes;
                try { bytes = Convert.FromBase64String(diff.packed); }
                catch (FormatException) { skipped++; continue; }

                if (bytes.Length < 4) { skipped++; continue; }

                int count = BitConverter.ToInt32(bytes, 0);
                if (count <= 0 || bytes.Length < 4 + count * 5) { skipped++; continue; }

                var coord = new int3(diff.cx, diff.cy, diff.cz);
                NativeArray<sbyte> d = Ensure(coord);

                int p = 4, q = 4 + count * 4;
                for (int k = 0; k < count; k++)
                {
                    int i = BitConverter.ToInt32(bytes, p);
                    p += 4;
                    sbyte v = unchecked((sbyte)bytes[q++]);
                    if (i >= 0 && i < d.Length) d[i] = v;
                }

                Dirty.Add(coord);
            }

            return skipped;
        }

        public void Clear()
        {
            foreach (var kv in deltas)
            {
                Dirty.Add(kv.Key);
                if (kv.Value.IsCreated) kv.Value.Dispose();
            }
            deltas.Clear();
            columnRange.Clear();
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;

            foreach (var kv in deltas) if (kv.Value.IsCreated) kv.Value.Dispose();
            deltas.Clear();
            columnRange.Clear();
            if (empty.IsCreated) empty.Dispose();
        }
    }
}
