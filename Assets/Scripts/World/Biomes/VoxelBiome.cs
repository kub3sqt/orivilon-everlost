using System;
using Orivilon.World.Generation;
using static Unity.Mathematics.math;

namespace Orivilon.World.Biomes
{
    /// <summary>Biom voxelového světa. Jméno pro hráče, ne datová definice porostu.</summary>
    public enum VoxelBiome
    {
        Ocean,
        Beach,
        Desert,
        Savanna,
        Jungle,
        Plains,
        Forest,
        Taiga,
        Tundra,
        Mountains,
        SnowPeaks,
    }

    /// <summary>
    /// Pojmenování místa podle výšky a klimatu.
    ///
    /// <para><b>Prahy schválně kopírují <see cref="TerrainPalette"/>.</b> Kdyby si klasifikace
    /// držela vlastní čísla, hlásila by konzole „hory" na svahu, který je zjevně zasněžený –
    /// a hráč by hledal chybu v generátoru místo v tabulce jmen. Sněžná čára se proto počítá
    /// úplně stejným vzorcem včetně posunu podle teploty.</para>
    /// </summary>
    public static class VoxelBiomes
    {
        /// <summary>Všechny biomy v pořadí, v jakém je vypisuje nápověda.</summary>
        public static readonly VoxelBiome[] All = (VoxelBiome[])Enum.GetValues(typeof(VoxelBiome));

        public static VoxelBiome Classify(float surfaceY, float temperature, float humidity,
                                          float seaLevel, in TerrainPalette palette)
            => Classify(surfaceY, temperature, humidity, seaLevel, palette, 0f);

        /// <param name="snowLift">Kolo 20: zvednutí sněžné čáry podle biomového klimatu (BiomeMath.SnowLift), jinak 0.</param>
        public static VoxelBiome Classify(float surfaceY, float temperature, float humidity,
                                          float seaLevel, in TerrainPalette palette, float snowLift)
        {
            float t = saturate(temperature);
            float h = saturate(humidity);

            if (surfaceY < seaLevel - 1f) return VoxelBiome.Ocean;
            if (surfaceY < palette.sandTop) return VoxelBiome.Beach;

            float snowLine = palette.snowTop + palette.snowTempShift * (t - 0.5f) * 2f + snowLift;
            if (surfaceY >= snowLine) return VoxelBiome.SnowPeaks;
            if (surfaceY >= palette.grassTop) return VoxelBiome.Mountains;

            if (t >= 0.66f) return h < 0.34f ? VoxelBiome.Desert
                                 : (h < 0.62f ? VoxelBiome.Savanna : VoxelBiome.Jungle);

            if (t <= 0.34f) return h >= 0.5f ? VoxelBiome.Taiga : VoxelBiome.Tundra;

            return h >= 0.55f ? VoxelBiome.Forest : VoxelBiome.Plains;
        }

        /// <summary>
        /// Jméno biomu pro hráče. Anglicky, protože konzole je anglicky; české názvy
        /// zůstávají jako vstup v <see cref="TryParse"/>, aby se dosavadní psaní nerozbilo.
        /// </summary>
        public static string Name(VoxelBiome b) => b switch
        {
            VoxelBiome.Ocean     => "ocean",
            VoxelBiome.Beach     => "beach",
            VoxelBiome.Desert    => "desert",
            VoxelBiome.Savanna   => "savanna",
            VoxelBiome.Jungle    => "jungle",
            VoxelBiome.Plains    => "plains",
            VoxelBiome.Forest    => "forest",
            VoxelBiome.Taiga     => "taiga",
            VoxelBiome.Tundra    => "tundra",
            VoxelBiome.Mountains => "mountains",
            VoxelBiome.SnowPeaks => "snow peaks",
            _ => b.ToString(),
        };

        /// <summary>
        /// Rozpozná biom podle názvu. Bere česky i anglicky, s diakritikou i bez ní –
        /// hráč píše do konzole rychle a přepínat kvůli tomu klávesnici je otrava.
        /// </summary>
        public static bool TryParse(string s, out VoxelBiome biome)
        {
            biome = VoxelBiome.Plains;
            if (string.IsNullOrWhiteSpace(s)) return false;

            switch (Normalize(s))
            {
                case "ocean": case "more": case "sea":                 biome = VoxelBiome.Ocean; return true;
                case "plaz": case "beach": case "pobrezi":             biome = VoxelBiome.Beach; return true;
                case "poust": case "desert": case "pust":              biome = VoxelBiome.Desert; return true;
                case "savana": case "savanna": case "step":            biome = VoxelBiome.Savanna; return true;
                case "dzungle": case "jungle": case "prales":          biome = VoxelBiome.Jungle; return true;
                case "plane": case "plains": case "louka": case "pole":biome = VoxelBiome.Plains; return true;
                case "les": case "forest":                             biome = VoxelBiome.Forest; return true;
                case "tajga": case "taiga":                            biome = VoxelBiome.Taiga; return true;
                case "tundra":                                         biome = VoxelBiome.Tundra; return true;
                case "hory": case "mountains": case "hora": case "kopce": biome = VoxelBiome.Mountains; return true;
                case "snih": case "snow": case "stity": case "snowpeaks": case "vrcholy":
                                                                       biome = VoxelBiome.SnowPeaks; return true;
            }
            return false;
        }

        /// <summary>Malá písmena bez diakritiky – jediné, na čem se rozpoznávání zakládá.</summary>
        private static string Normalize(string s)
        {
            var sb = new System.Text.StringBuilder(s.Length);
            foreach (char c in s.Trim().ToLowerInvariant())
            {
                switch (c)
                {
                    case 'á': case 'à': case 'ä': sb.Append('a'); break;
                    case 'č': sb.Append('c'); break;
                    case 'ď': sb.Append('d'); break;
                    case 'é': case 'ě': sb.Append('e'); break;
                    case 'í': sb.Append('i'); break;
                    case 'ň': sb.Append('n'); break;
                    case 'ó': case 'ö': sb.Append('o'); break;
                    case 'ř': sb.Append('r'); break;
                    case 'š': sb.Append('s'); break;
                    case 'ť': sb.Append('t'); break;
                    case 'ú': case 'ů': case 'ü': sb.Append('u'); break;
                    case 'ý': sb.Append('y'); break;
                    case 'ž': sb.Append('z'); break;
                    default: sb.Append(c); break;
                }
            }
            return sb.ToString();
        }

        /// <summary>Čárkou oddělený výpis jmen pro nápovědu konzole.</summary>
        public static string ListNames()
        {
            var sb = new System.Text.StringBuilder();
            for (int i = 0; i < All.Length; i++)
            {
                if (i > 0) sb.Append(", ");
                sb.Append(Name(All[i]));
            }
            return sb.ToString();
        }
    }
}
