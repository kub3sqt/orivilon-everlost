using Unity.Mathematics;
using static Unity.Mathematics.math;

namespace Orivilon.World.Generation
{
    /// <summary>
    /// Burst-kompatibilní šumové funkce pro generování světa.
    /// Vše jsou statické čisté funkce nad Unity.Mathematics – žádný stav, žádné managed typy,
    /// takže je lze volat přímo z jobů.
    ///
    /// FastNoiseLite se záměrně nepoužívá: je to class s instančním stavem a switchem nad enumy,
    /// což Burst neumí zkompilovat. snoise z Unity.Mathematics je Burst-ready a na fBm i ridged stačí.
    /// </summary>
    public static class GenNoise
    {
        /// <summary>
        /// Lacunarity záměrně NENÍ 2.0. Přesná dvojka zarovná mřížky oktáv na sebe
        /// a na dlouhých hřebenech vznikají viditelné pravidelné artefakty.
        /// </summary>
        public const float Lacunarity = 2.1f;

        /// <summary>Standardní pokles amplitudy mezi oktávami.</summary>
        public const float Gain = 0.5f;

        /// <summary>Rozptýlení bitů 32bitového hashe (varianta Wang hash).</summary>
        public static uint Hash(uint x)
        {
            x ^= 2747636419u; x *= 2654435769u;
            x ^= x >> 16;     x *= 2654435769u;
            x ^= x >> 16;     x *= 2654435769u;
            return x;
        }

        /// <summary>Hash souřadnice buňky se seedem. Používá se pro jezera a skalní brány.</summary>
        public static uint Hash(int2 cell, int seed)
        {
            unchecked
            {
                return Hash((uint)(cell.x * 73856093) ^ (uint)(cell.y * 19349663) ^ (uint)seed);
            }
        }

        /// <summary>Hash na rozsah 0–1.</summary>
        public static float Hash01(uint h) => (h & 0x00FFFFFFu) * (1f / 16777216f);

        /// <summary>
        /// Deterministický offset jednoho kanálu šumu.
        ///
        /// POZOR NA ROZSAH. Offset se přičítá k <c>pozice * frekvence</c>, což je u makro polí
        /// velmi malé číslo – přes výřez 224 m se continentalness změní jen o 0.055. Když má
        /// offset velikost 8192, je krok float32 na téhle magnitudě 9.8e-4, takže se z těch
        /// 0.055 stane 57 diskrétních hodnot: terén se rozpadne na vodorovné plošiny po ~4 m
        /// a v kosočtvercové mřížce (kvantují se obě osy). Offset ±120 dává krok 3 cm.
        ///
        /// Dekorelaci kanálů to neohrozí – po vynásobení frekvencí je 120 jednotek
        /// stejně 120 period šumu daleko.
        /// </summary>
        public static float2 ChannelOffset(int seed, int channel)
        {
            uint a = Hash((uint)seed * 2654435761u + (uint)channel * 40503u);
            uint b = Hash(a ^ 0x9E3779B9u);
            return new float2(Hash01(a), Hash01(b)) * 240f - 120f;
        }

        /// <summary>
        /// Trojrozměrná varianta. Stejný rozsah ±120 a ze stejného důvodu – viz ChannelOffset.
        /// </summary>
        public static float3 ChannelOffset3(int seed, int channel)
        {
            uint a = Hash((uint)seed * 2654435761u + (uint)channel * 40503u);
            uint b = Hash(a ^ 0x9E3779B9u);
            uint c = Hash(b ^ 0x85EBCA6Bu);
            return new float3(Hash01(a), Hash01(b), Hash01(c)) * 240f - 120f;
        }

        /// <summary>Fractal Brownian motion, výstup přibližně ⟨-1, 1⟩.</summary>
        public static float Fbm2(float2 p, int octaves, float2 offset)
        {
            float a = 1f, f = 1f, sum = 0f, norm = 0f;
            for (int i = 0; i < octaves; i++)
            {
                float2 q = p * f + offset + new float2(i * 3.17f, i * 7.31f);
                sum  += a * noise.snoise(q);
                norm += a;
                a *= Gain; f *= Lacunarity;
            }
            return norm > 0f ? sum / norm : 0f;
        }

        /// <summary>Fractal Brownian motion ve 3D, výstup přibližně ⟨-1, 1⟩.</summary>
        public static float Fbm3(float3 p, int octaves, float3 offset)
        {
            float a = 1f, f = 1f, sum = 0f, norm = 0f;
            for (int i = 0; i < octaves; i++)
            {
                float3 q = p * f + offset + new float3(i * 3.17f, i * 5.41f, i * 7.31f);
                sum  += a * noise.snoise(q);
                norm += a;
                a *= Gain; f *= Lacunarity;
            }
            return norm > 0f ? sum / norm : 0f;
        }

        /// <summary>
        /// Ridged multifractal (Musgrave) – výstup ⟨0, 1⟩ s ostrými hřbety místo hladkých kopců.
        /// Každá oktáva je vážená předchozí, takže detail vzniká hlavně na už existujících hřebenech.
        /// </summary>
        public static float Ridged2(float2 p, int octaves, float2 offset)
        {
            float a = 1f, f = 1f, sum = 0f, norm = 0f, prev = 1f;
            for (int i = 0; i < octaves; i++)
            {
                float2 q = p * f + offset + new float2(i * 3.17f, i * 7.31f);
                float n = 1f - abs(noise.snoise(q));
                n = n * n;
                sum  += a * n * prev;
                norm += a;
                prev = n;
                a *= Gain; f *= Lacunarity;
            }
            return norm > 0f ? saturate(sum / norm) : 0f;
        }

        /// <summary>
        /// Střed jittrované Worley buňky ve světových souřadnicích.
        /// Jitter je omezen na 0.2–0.8 buňky, aby se středy sousedních buněk nemohly přiblížit
        /// natolik, že by dvě jezera splynula.
        /// </summary>
        public static float2 CellCenter(int2 cell, float cellSize, int seed)
        {
            uint h = Hash(cell, seed);
            float2 j = new float2(Hash01(h), Hash01(Hash(h ^ 0x85EBCA6Bu)));
            return ((float2)cell + 0.2f + 0.6f * j) * cellSize;
        }

        /// <summary>Hladké minimum (polynomiální, bez exp). V naší konvenci = odečtení hmoty.</summary>
        public static float SMin(float a, float b, float k)
        {
            float h = saturate(0.5f + 0.5f * (b - a) / k);
            return lerp(b, a, h) - k * h * (1f - h);
        }

        /// <summary>Hladké maximum. V naší konvenci (d &gt; 0 = kámen) = sjednocení hmoty.</summary>
        public static float SMax(float a, float b, float k) => -SMin(-a, -b, k);
    }
}
