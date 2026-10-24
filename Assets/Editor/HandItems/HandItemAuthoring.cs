using System.Collections.Generic;
using System.IO;
using System.Text;
using Orivilon.Inventory.Inventory;
using Orivilon.Player;
using UnityEditor;
using UnityEngine;

namespace Orivilon.EditorTools
{
    /// <summary>
    /// První sada low poly předmětů do ruky (22.10.2026): procedurálně postavené flat-shaded meshe uložené jako
    /// assety, URP Lit materiály, prefaby s počátkem v úchopu (<see cref="HeldItemGrip"/>) a ikony 256×256
    /// vyrenderované z těch samých prefabů. Vše jde znovu vygenerovat se zachováním GUID (assety se přepisují
    /// na místě). Jen editor – do buildu nejde; běh hry nic nerenderuje.
    /// Prostor úchopu = kost prop.R postavy v004 (změřeno na Tool_Axe/Tool_Pickaxe): násada +Y, pěst v počátku,
    /// ostří −X, tloušťka Z, jednotky modelu (postava 1,6).
    /// </summary>
    internal static class HandItemAuthoring
    {
        private const string M = "Tools/Everlost/Hand Items/";
        private const string Root = "Assets/Models/HandItems";
        private static string ProjectRoot => Path.GetDirectoryName(Application.dataPath);
        private static string ReportDir => Path.Combine(ProjectRoot, "_reports", "hand-items");
        private static string OutLog => Path.Combine(ProjectRoot, "_reports", "remote", "out.log");
        private static void Out(string s) { try { File.AppendAllText(OutLog, System.DateTime.Now.ToString("HH:mm:ss ") + s + "\n"); } catch { } Debug.Log("[HandItems] " + s); }

        // ───────────────────────── definice ─────────────────────────

        private enum Mat
        {
            Wood, Bark, WoodEnd, Stone, Flint, Cord, Cloth, FlameCore, Flame,
            // sada 2 (22.10.2026): bobule a druhy dřeva – kůra / čelo (řez) / jádro, případně pásky
            Berry, BerryLight, Leaf, Stem,
            OakBark, OakCut, OakCore, BirchBark, BirchMark, BirchCut, BirchCore, FirBark, FirCut, FirCore,
            SpruceBark, SpruceCut, SpruceCore, AcaciaBark, AcaciaCut, AcaciaCore, RedwoodBark, RedwoodCut, RedwoodCore,
            CherryBark, CherryBand, CherryCut, CherryCore, BambooGreen, BambooNode, BambooCut, BambooHollow,
            JungleBark, JungleMoss, JungleCut, JungleCore, CharBark, CharCut, CharCore,
            PalmBark, PalmRing, PalmCut, PalmCore, MangroveBark, MangroveCut, MangroveCore,
            // sada 3: provaz
            CordDark,
        }

        private static readonly Dictionary<Mat, (Color c, float smooth, float emit)> Palette = new Dictionary<Mat, (Color, float, float)>
        {
            { Mat.Wood,      (new Color(0.56f, 0.38f, 0.23f), 0.15f, 0f) },
            { Mat.Bark,      (new Color(0.46f, 0.33f, 0.22f), 0.08f, 0f) },
            { Mat.WoodEnd,   (new Color(0.84f, 0.69f, 0.48f), 0.10f, 0f) },
            { Mat.Stone,     (new Color(0.63f, 0.64f, 0.65f), 0.12f, 0f) },
            { Mat.Flint,     (new Color(0.40f, 0.44f, 0.50f), 0.30f, 0f) },
            { Mat.Cord,      (new Color(0.80f, 0.63f, 0.38f), 0.05f, 0f) },
            { Mat.Cloth,     (new Color(0.33f, 0.25f, 0.19f), 0.05f, 0f) },
            { Mat.FlameCore, (new Color(1.00f, 0.86f, 0.42f), 0.00f, 2.2f) },
            { Mat.Flame,     (new Color(1.00f, 0.47f, 0.12f), 0.00f, 2.2f) },
            { Mat.Berry,        (new Color(0.78f, 0.10f, 0.18f), 0.45f, 0f) },
            { Mat.BerryLight,   (new Color(0.93f, 0.25f, 0.30f), 0.45f, 0f) },
            { Mat.Leaf,         (new Color(0.36f, 0.58f, 0.22f), 0.15f, 0f) },
            { Mat.Stem,         (new Color(0.30f, 0.40f, 0.18f), 0.10f, 0f) },
            { Mat.OakBark,      (new Color(0.33f, 0.29f, 0.25f), 0.06f, 0f) },
            { Mat.OakCut,       (new Color(0.86f, 0.64f, 0.36f), 0.10f, 0f) },
            { Mat.OakCore,      (new Color(0.60f, 0.41f, 0.24f), 0.10f, 0f) },
            { Mat.BirchBark,    (new Color(0.92f, 0.91f, 0.86f), 0.20f, 0f) },
            { Mat.BirchMark,    (new Color(0.16f, 0.15f, 0.15f), 0.10f, 0f) },
            { Mat.BirchCut,     (new Color(0.95f, 0.88f, 0.71f), 0.10f, 0f) },
            { Mat.BirchCore,    (new Color(0.84f, 0.72f, 0.52f), 0.10f, 0f) },
            { Mat.FirBark,      (new Color(0.56f, 0.53f, 0.49f), 0.15f, 0f) },
            { Mat.FirCut,       (new Color(0.93f, 0.82f, 0.58f), 0.10f, 0f) },
            { Mat.FirCore,      (new Color(0.82f, 0.64f, 0.40f), 0.10f, 0f) },
            { Mat.SpruceBark,   (new Color(0.27f, 0.24f, 0.22f), 0.08f, 0f) },
            { Mat.SpruceCut,    (new Color(0.94f, 0.89f, 0.74f), 0.10f, 0f) },
            { Mat.SpruceCore,   (new Color(0.80f, 0.71f, 0.55f), 0.10f, 0f) },
            { Mat.AcaciaBark,   (new Color(0.56f, 0.39f, 0.31f), 0.08f, 0f) },
            { Mat.AcaciaCut,    (new Color(0.89f, 0.52f, 0.27f), 0.10f, 0f) },
            { Mat.AcaciaCore,   (new Color(0.58f, 0.26f, 0.15f), 0.10f, 0f) },
            { Mat.RedwoodBark,  (new Color(0.56f, 0.27f, 0.17f), 0.06f, 0f) },
            { Mat.RedwoodCut,   (new Color(0.81f, 0.43f, 0.31f), 0.10f, 0f) },
            { Mat.RedwoodCore,  (new Color(0.60f, 0.25f, 0.18f), 0.10f, 0f) },
            { Mat.CherryBark,   (new Color(0.38f, 0.19f, 0.17f), 0.35f, 0f) },
            { Mat.CherryBand,   (new Color(0.63f, 0.46f, 0.41f), 0.20f, 0f) },
            { Mat.CherryCut,    (new Color(0.91f, 0.68f, 0.60f), 0.10f, 0f) },
            { Mat.CherryCore,   (new Color(0.72f, 0.42f, 0.36f), 0.10f, 0f) },
            { Mat.BambooGreen,  (new Color(0.55f, 0.68f, 0.30f), 0.35f, 0f) },
            { Mat.BambooNode,   (new Color(0.74f, 0.75f, 0.43f), 0.30f, 0f) },
            { Mat.BambooCut,    (new Color(0.90f, 0.86f, 0.62f), 0.10f, 0f) },
            { Mat.BambooHollow, (new Color(0.24f, 0.21f, 0.14f), 0.05f, 0f) },
            { Mat.JungleBark,   (new Color(0.42f, 0.38f, 0.27f), 0.08f, 0f) },
            { Mat.JungleMoss,   (new Color(0.38f, 0.55f, 0.22f), 0.05f, 0f) },
            { Mat.JungleCut,    (new Color(0.68f, 0.35f, 0.22f), 0.15f, 0f) },
            { Mat.JungleCore,   (new Color(0.46f, 0.20f, 0.13f), 0.15f, 0f) },
            { Mat.CharBark,     (new Color(0.11f, 0.10f, 0.10f), 0.20f, 0f) },
            { Mat.CharCut,      (new Color(0.30f, 0.23f, 0.19f), 0.10f, 0f) },
            { Mat.CharCore,     (new Color(0.50f, 0.37f, 0.26f), 0.10f, 0f) },
            { Mat.PalmBark,     (new Color(0.64f, 0.52f, 0.37f), 0.08f, 0f) },
            { Mat.PalmRing,     (new Color(0.42f, 0.32f, 0.22f), 0.08f, 0f) },
            { Mat.PalmCut,      (new Color(0.87f, 0.79f, 0.60f), 0.10f, 0f) },
            { Mat.PalmCore,     (new Color(0.74f, 0.64f, 0.46f), 0.10f, 0f) },
            { Mat.MangroveBark, (new Color(0.48f, 0.47f, 0.39f), 0.08f, 0f) },
            { Mat.MangroveCut,  (new Color(0.80f, 0.58f, 0.48f), 0.10f, 0f) },
            { Mat.MangroveCore, (new Color(0.62f, 0.35f, 0.22f), 0.10f, 0f) },
            { Mat.CordDark,     (new Color(0.62f, 0.47f, 0.27f), 0.05f, 0f) },
        };

        private sealed class Def
        {
            public string key, itemAsset;
            public System.Action<MB> build;
            public Vector3 iconEuler;      // rotace předmětu před kamerou ikony
            // Nový item (sada 2 – druhy dřeva): vytvoří se v Resources/Items, ID je stabilní a nikdy se nemění.
            public string newId, newName, newDesc;
            public bool woodVariant;       // countsAs = Log, stack jako Log
        }

        private static readonly Vector3 LogIcon = new Vector3(20f, -35f, 12f);   // stejný pohled jako Log ze sady 1

        private static Def Wood(string key, string id, string name, string tree, WoodSpec w) => new Def
        {
            key = key, itemAsset = "Assets/Resources/Items/" + key + ".asset", build = mb => BuildWood(mb, w), iconEuler = LogIcon,
            newId = id, newName = name, woodVariant = true,
            newDesc = "Wood from " + tree + ". Works as Log in building and research.",
        };

        private static readonly Def[] Defs =
        {
            new Def { key = "StoneAxe",     itemAsset = "Assets/Resources/Items/StoneAxe.asset",     build = BuildAxe,     iconEuler = new Vector3(-12f, 32f, -45f) },
            new Def { key = "StonePickaxe", itemAsset = "Assets/Resources/Items/StonePickaxe.asset", build = BuildPickaxe, iconEuler = new Vector3(-12f, 28f, -45f) },
            new Def { key = "StoneSword",   itemAsset = "Assets/Resources/Items/StoneSword.asset",   build = BuildSword,   iconEuler = new Vector3(-10f, 38f, -45f) },
            new Def { key = "Torch",        itemAsset = "Assets/Resources/Items/Torch.asset",        build = BuildTorch,   iconEuler = new Vector3(-12f, 30f, -35f) },
            new Def { key = "Stone",        itemAsset = "Assets/Resources/Items/Stone.asset",        build = BuildStone,   iconEuler = new Vector3(22f, 35f, 0f) },
            new Def { key = "Log",          itemAsset = "Assets/Resources/Items/Log.asset",          build = BuildLog,     iconEuler = new Vector3(20f, -35f, 12f) },
            // ── sada 2 ──
            new Def { key = "Berries",      itemAsset = "Assets/Resources/Items/Berries.asset",      build = BuildBerries, iconEuler = new Vector3(18f, 30f, 0f) },
            // ── sada 3 ──
            new Def { key = "Stick",        itemAsset = "Assets/Resources/Items/Stick.asset",        build = BuildStick,   iconEuler = new Vector3(-12f, 30f, -40f) },
            new Def { key = "Rope",         itemAsset = "Assets/Resources/Items/Rope.asset",         build = BuildRope,    iconEuler = new Vector3(62f, -20f, 0f) },
            Wood("OakLog",      "everlost:oak_log",      "Oak Log",      "an oak (broadleaf) tree",
                new WoodSpec { bark = Mat.OakBark, cut = Mat.OakCut, core = Mat.OakCore, r = .066f, sides = 8, ridge = .20f, jitter = .10f, bend = .006f, seed = 11,
                               stubs = new[] { new Vector4(.55f, 10f, .045f, .016f) } }),
            Wood("BirchLog",    "everlost:birch_log",    "Birch Log",    "a birch tree",
                new WoodSpec { bark = Mat.BirchBark, band = Mat.BirchMark, cut = Mat.BirchCut, core = Mat.BirchCore, r = .050f, len = .36f, sides = 7, jitter = .04f, seed = 12,
                               bands = new[] { .12f, .17f, .38f, .41f, .60f, .66f, .83f, .86f }, bandScale = 1.07f, stubs = new[] { new Vector4(.30f, -30f, .028f, .010f) } }),
            Wood("FirLog",      "everlost:fir_log",      "Fir Log",      "a fir tree",
                new WoodSpec { bark = Mat.FirBark, cut = Mat.FirCut, core = Mat.FirCore, r = .056f, len = .35f, sides = 7, jitter = .03f, seed = 13,
                               stubs = new[] { new Vector4(.18f, 60f, .010f, .009f), new Vector4(.42f, -40f, .010f, .008f), new Vector4(.64f, 100f, .010f, .009f), new Vector4(.85f, 20f, .010f, .008f) } }),
            Wood("SpruceLog",   "everlost:spruce_log",   "Spruce Log",   "a dark spruce tree",
                new WoodSpec { bark = Mat.SpruceBark, cut = Mat.SpruceCut, core = Mat.SpruceCore, r = .056f, sides = 7, jitter = .08f, seed = 14,
                               stubs = new[] { new Vector4(.20f, 0f, .022f, .012f), new Vector4(.50f, 140f, .022f, .012f), new Vector4(.80f, 260f, .022f, .012f) } }),
            Wood("AcaciaLog",   "everlost:acacia_log",   "Acacia Log",   "an acacia tree",
                new WoodSpec { bark = Mat.AcaciaBark, cut = Mat.AcaciaCut, core = Mat.AcaciaCore, r = .054f, sides = 6, jitter = .12f, bend = .036f, twist = 70f, seed = 15 }),
            Wood("RedwoodLog",  "everlost:redwood_log",  "Redwood Log",  "a sequoia (redwood) tree",
                new WoodSpec { bark = Mat.RedwoodBark, cut = Mat.RedwoodCut, core = Mat.RedwoodCore, r = .070f, sides = 9, ridge = .14f, jitter = .04f, seed = 16 }),
            Wood("CherryLog",   "everlost:cherry_log",   "Cherry Log",   "a sakura (cherry) tree",
                new WoodSpec { bark = Mat.CherryBark, band = Mat.CherryBand, cut = Mat.CherryCut, core = Mat.CherryCore, r = .054f, sides = 8, jitter = .04f, seed = 17,
                               bands = new[] { .20f, .24f, .47f, .51f, .74f, .78f }, bandScale = 1.06f }),
            new Def
            {
                key = "BambooStalk", itemAsset = "Assets/Resources/Items/BambooStalk.asset", build = BuildBamboo, iconEuler = LogIcon,
                newId = "everlost:bamboo_stalk", newName = "Bamboo Stalk", woodVariant = true,
                newDesc = "Wood from a bamboo clump. Works as Log in building and research.",
            },
            Wood("JungleLog",   "everlost:jungle_log",   "Jungle Log",   "a jungle tree",
                new WoodSpec { bark = Mat.JungleBark, band = Mat.JungleMoss, cut = Mat.JungleCut, core = Mat.JungleCore, r = .064f, sides = 7, jitter = .10f, flare = .22f, seed = 18,
                               bands = new[] { .04f, .30f }, bandScale = 1.12f }),
            Wood("CharredLog",  "everlost:charred_log",  "Charred Log",  "a burnt tree trunk",
                new WoodSpec { bark = Mat.CharBark, cut = Mat.CharCut, core = Mat.CharCore, r = .060f, sides = 7, jitter = .18f, taper = .18f, seed = 19 }),
            Wood("PalmLog",     "everlost:palm_log",     "Palm Log",     "a palm tree",
                new WoodSpec { bark = Mat.PalmBark, band = Mat.PalmRing, cut = Mat.PalmCut, core = Mat.PalmCore, r = .054f, sides = 8, jitter = .03f, seed = 20,
                               bands = new[] { .04f, .11f, .21f, .28f, .38f, .45f, .55f, .62f, .72f, .79f, .89f, .96f }, bandScale = 1.09f }),
            Wood("MangroveLog", "everlost:mangrove_log", "Mangrove Log", "a mangrove tree",
                new WoodSpec { bark = Mat.MangroveBark, cut = Mat.MangroveCut, core = Mat.MangroveCore, r = .050f, sides = 7, jitter = .08f, bend = .008f, seed = 21,
                               stubs = new[] { new Vector4(.12f, 205f, .060f, .012f), new Vector4(.30f, 150f, .055f, .011f), new Vector4(.50f, 195f, .050f, .010f), new Vector4(.70f, 170f, .045f, .010f) } }),
        };

        /// <summary>
        /// Druh stromu → dřevo. Podle jména prefabu skutečné definice stromu (spawnables / BiomeProps), ne podle biomu.
        /// Pořadí: delší prefixy dřív. Každý těžitelný strom s dropem Log musí mít záznam (jinak „4 Assign tree drops“ hlásí ERR).
        /// </summary>
        private static readonly (string prefix, string wood)[] TreeWood =
        {
            ("Tree_Broadleaf_", "OakLog"), ("SM_Strom_Listnaty_", "OakLog"),
            ("Tree_Birch_", "BirchLog"), ("SM_Strom_Briza", "BirchLog"),
            ("Tree_Fir_", "FirLog"),
            ("SM_Smrk_Tmavy", "SpruceLog"),
            ("SM_Strom_Akacie", "AcaciaLog"),
            ("SM_Sekvoje", "RedwoodLog"),
            ("SM_Sakura", "CherryLog"),
            ("SM_Bambus_", "BambooStalk"),
            ("SM_Strom_Dzungle", "JungleLog"),
            ("SM_Kmen_Ohoreny", "CharredLog"),
            ("SM_Palma", "PalmLog"),
            ("SM_Mangrovnik", "MangroveLog"),
        };

        private static string WoodForPrefab(string prefabName)
        {
            foreach (var (prefix, wood) in TreeWood) if (prefabName.StartsWith(prefix, System.StringComparison.Ordinal)) return wood;
            return null;
        }

        // ───────────────────────── geometrie ─────────────────────────

        /// <summary>Flat-shaded builder: každý trojúhelník vlastní vrcholy a normálu plochy, submesh = materiál.</summary>
        private sealed class MB
        {
            public readonly List<Vector3> v = new List<Vector3>();
            public readonly List<Vector3> n = new List<Vector3>();
            public readonly Dictionary<Mat, List<int>> subs = new Dictionary<Mat, List<int>>();

            /// <summary>Trojúhelník otočený tak, aby normála mířila od bodu <paramref name="inside"/>.</summary>
            public void Tri(Mat m, Vector3 a, Vector3 b, Vector3 c, Vector3 inside)
            {
                Vector3 nn = Vector3.Cross(b - a, c - a);
                if (nn.sqrMagnitude < 1e-12f) return;
                if (Vector3.Dot(nn, (a + b + c) / 3f - inside) < 0f) { var t = b; b = c; c = t; nn = -nn; }
                nn.Normalize();
                if (!subs.TryGetValue(m, out var list)) subs[m] = list = new List<int>();
                int i = v.Count;
                v.Add(a); v.Add(b); v.Add(c); n.Add(nn); n.Add(nn); n.Add(nn);
                list.Add(i); list.Add(i + 1); list.Add(i + 2);
            }

            /// <summary>Spojí po sobě jdoucí řezy (stejný počet bodů) pláštěm; volitelně zavře konce vějířem.</summary>
            public void Loft(Mat side, List<Vector3[]> secs, bool capStart = true, bool capEnd = true, Mat? capMat = null)
            {
                for (int s = 0; s + 1 < secs.Count; s++)
                {
                    var A = secs[s]; var B = secs[s + 1];
                    Vector3 inside = (Center(A) + Center(B)) * .5f;
                    for (int k = 0; k < A.Length; k++)
                    {
                        int k1 = (k + 1) % A.Length;
                        Tri(side, A[k], A[k1], B[k1], inside);
                        Tri(side, A[k], B[k1], B[k], inside);
                    }
                }
                Mat cm = capMat ?? side;
                if (capStart) Cap(cm, secs[0], Center(secs[1]));
                if (capEnd) Cap(cm, secs[secs.Count - 1], Center(secs[secs.Count - 2]));
            }

            public void Cap(Mat m, Vector3[] sec, Vector3 inside)
            {
                Vector3 c = Center(sec);
                for (int k = 0; k < sec.Length; k++) Tri(m, c, sec[k], sec[(k + 1) % sec.Length], inside);
            }

            public static Vector3 Center(Vector3[] s) { Vector3 c = Vector3.zero; foreach (var p in s) c += p; return c / s.Length; }

            public Mesh ToMesh(string name, out Mat[] order)
            {
                var mesh = new Mesh { name = name };
                mesh.SetVertices(v); mesh.SetNormals(n);
                var keys = new List<Mat>(subs.Keys); keys.Sort();
                mesh.subMeshCount = keys.Count;
                for (int i = 0; i < keys.Count; i++) mesh.SetTriangles(subs[keys[i]], i);
                mesh.RecalculateBounds();
                order = keys.ToArray();
                return mesh;
            }
        }

        /// <summary>Prstenec kolmo na osu Y (x = šířka, z = tloušťka).</summary>
        private static Vector3[] RingY(float y, float rx, float rz, int sides, float rot = 0f, float cx = 0f, float cz = 0f, int seed = 0, float jitter = 0f)
        {
            var r = new Vector3[sides];
            for (int k = 0; k < sides; k++)
            {
                float a = rot * Mathf.Deg2Rad + k * Mathf.PI * 2f / sides;
                float j = 1f + jitter * Hash(seed * 31 + k);
                r[k] = new Vector3(cx + Mathf.Cos(a) * rx * j, y, cz + Mathf.Sin(a) * rz * j);
            }
            return r;
        }

        /// <summary>Prstenec kolmo na osu X (y = výška, z = tloušťka).</summary>
        private static Vector3[] RingX(float x, float ry, float rz, int sides, float cy, float rot = 0f, int seed = 0, float jitter = 0f, float cz = 0f)
        {
            var r = new Vector3[sides];
            for (int k = 0; k < sides; k++)
            {
                float a = rot * Mathf.Deg2Rad + k * Mathf.PI * 2f / sides;
                float j = 1f + jitter * Hash(seed * 31 + k);
                r[k] = new Vector3(x, cy + Mathf.Cos(a) * ry * j, cz + Mathf.Sin(a) * rz * j);
            }
            return r;
        }

        /// <summary>Deterministický šum −1..1 (bez Random – meshe jsou při přegenerování stejné).</summary>
        private static float Hash(int i) { uint h = (uint)i * 2654435761u; h ^= h >> 13; h *= 0x5bd1e995; h ^= h >> 15; return (h & 0xffff) / 32767.5f - 1f; }

        /// <summary>Násada nástroje: šestiboká, dole hlavička, mírně zúžená.</summary>
        private static void Haft(MB mb, Mat m, float top, float rTop)
        {
            mb.Loft(m, new List<Vector3[]>
            {
                RingY(-.215f, .013f, .013f, 6, 30f),
                RingY(-.200f, .022f, .022f, 6, 30f),
                RingY(-.180f, .019f, .019f, 6, 30f),
                RingY(top * .45f, .018f, .018f, 6, 30f, .002f),
                RingY(top, rTop, rTop, 6, 30f),
            });
        }

        /// <summary>Omotávka (provázek) kolem násady.</summary>
        private static void WrapY(MB mb, float y0, float y1, float r, int bands)
        {
            float h = (y1 - y0) / bands;
            for (int b = 0; b < bands; b++)
            {
                float a = y0 + b * h;
                mb.Loft(Mat.Cord, new List<Vector3[]>
                {
                    RingY(a, r * .92f, r * .92f, 6, 30f + b * 12f),
                    RingY(a + h * .5f, r, r, 6, 30f + b * 12f),
                    RingY(a + h * .98f, r * .92f, r * .92f, 6, 30f + b * 12f),
                });
            }
        }

        /// <summary>Šestiúhelníkový řez kolmo na X: h = půlvýška (Y), t = půltloušťka (Z).</summary>
        private static Vector3[] HexX(float x, float cy, float h, float t, float cz = 0f) => new[]
        {
            new Vector3(x, cy + h, cz), new Vector3(x, cy + h * .5f, cz + t), new Vector3(x, cy - h * .5f, cz + t),
            new Vector3(x, cy - h, cz), new Vector3(x, cy - h * .5f, cz - t), new Vector3(x, cy + h * .5f, cz - t),
        };

        /// <summary>Šestiúhelníkový řez kolmo na Y (čepel): w = půlšířka (X), t = půltloušťka (Z).</summary>
        private static Vector3[] HexY(float y, float w, float t, float cx = 0f) => new[]
        {
            new Vector3(cx + w, y, 0f), new Vector3(cx + w * .35f, y, t), new Vector3(cx - w * .35f, y, t),
            new Vector3(cx - w, y, 0f), new Vector3(cx - w * .35f, y, -t), new Vector3(cx + w * .35f, y, -t),
        };

        private static void BuildAxe(MB mb)
        {
            Haft(mb, Mat.Wood, .47f, .019f);
            // Kamenná hlava: týl za násadou (+X), klín k ostří (−X), ostří se rozšiřuje.
            mb.Loft(Mat.Stone, new List<Vector3[]>
            {
                HexX(.058f, .398f, .036f, .028f),
                HexX(.036f, .398f, .046f, .040f),
                HexX(-.022f, .400f, .053f, .042f),
                HexX(-.090f, .405f, .072f, .029f),
                HexX(-.155f, .410f, .102f, .011f),
                HexX(-.175f, .410f, .109f, .003f),
            });
            WrapY(mb, .318f, .350f, .029f, 2);
            WrapY(mb, .448f, .478f, .028f, 2);
            // Páska přes hlavu vedle násady.
            mb.Loft(Mat.Cord, new List<Vector3[]>
            {
                HexX(-.030f, .400f, .059f, .047f), HexX(-.042f, .401f, .062f, .048f), HexX(-.054f, .402f, .060f, .045f),
            });
        }

        private static void BuildPickaxe(MB mb)
        {
            Haft(mb, Mat.Wood, .525f, .019f);
            // Hlava napříč (osa X), oba konce do špice a mírně dolů.
            var secs = new List<Vector3[]>();
            float[] xs = { -.215f, -.165f, -.100f, -.034f, .034f, .095f, .150f, .190f };
            float[] hs = { .005f, .019f, .030f, .039f, .039f, .029f, .018f, .005f };
            float[] ts = { .005f, .019f, .027f, .034f, .034f, .026f, .017f, .005f };
            for (int i = 0; i < xs.Length; i++)
            {
                float u = xs[i] / .21f;
                secs.Add(HexX(xs[i], .505f - .065f * u * u, hs[i], ts[i]));
            }
            mb.Loft(Mat.Stone, secs);
            WrapY(mb, .432f, .462f, .028f, 2);
            mb.Loft(Mat.Cord, new List<Vector3[]> { HexX(-.028f, .504f, .045f, .040f), HexX(-.040f, .503f, .047f, .041f), HexX(-.052f, .502f, .044f, .038f) });
            mb.Loft(Mat.Cord, new List<Vector3[]> { HexX(.028f, .504f, .045f, .040f), HexX(.040f, .503f, .047f, .041f), HexX(.052f, .502f, .044f, .038f) });
        }

        private static void BuildSword(MB mb)
        {
            // Hlavice
            mb.Loft(Mat.Wood, new List<Vector3[]> { RingY(-.122f, .009f, .009f, 6, 30f), RingY(-.106f, .029f, .027f, 6, 30f), RingY(-.088f, .020f, .020f, 6, 30f) });
            // Rukojeť omotaná provázkem
            mb.Loft(Mat.Wood, new List<Vector3[]> { RingY(-.090f, .018f, .018f, 6, 30f), RingY(.066f, .018f, .018f, 6, 30f) }, false, false);
            WrapY(mb, -.084f, .058f, .021f, 5);
            // Záštita (osa X)
            mb.Loft(Mat.Wood, new List<Vector3[]>
            {
                HexX(-.092f, .077f, .010f, .014f), HexX(-.070f, .075f, .017f, .023f), HexX(0f, .073f, .019f, .028f),
                HexX(.070f, .075f, .017f, .023f), HexX(.092f, .077f, .010f, .014f),
            });
            // Pazourková čepel: středová hrana, mírně nepravidelná šířka (štípaný kámen), hrot.
            var b = new List<Vector3[]>();
            float[] ys = { .085f, .150f, .240f, .330f, .420f, .495f, .550f };
            float[] ws = { .038f, .047f, .043f, .046f, .040f, .031f, .017f };
            for (int i = 0; i < ys.Length; i++) b.Add(HexY(ys[i], ws[i], .014f - i * .001f, Hash(i + 5) * .003f));
            var tip = new Vector3[6];
            for (int k = 0; k < 6; k++) tip[k] = new Vector3(0f, .605f, 0f);
            b.Add(tip);
            mb.Loft(Mat.Flint, b, true, false);
        }

        private static void BuildTorch(MB mb)
        {
            mb.Loft(Mat.Wood, new List<Vector3[]>
            {
                RingY(-.185f, .014f, .014f, 6, 30f), RingY(-.175f, .018f, .018f, 6, 30f),
                RingY(.05f, .020f, .020f, 6, 30f, .002f), RingY(.255f, .022f, .022f, 6, 30f),
            });
            // Omotaná hlavice (látka)
            mb.Loft(Mat.Cloth, new List<Vector3[]>
            {
                RingY(.205f, .023f, .023f, 7, 0f), RingY(.225f, .040f, .040f, 7, 10f, 0f, 0f, 1, .08f),
                RingY(.265f, .045f, .045f, 7, 20f, 0f, 0f, 2, .08f), RingY(.305f, .043f, .043f, 7, 5f, 0f, 0f, 3, .08f),
                RingY(.330f, .032f, .032f, 7, 15f),
            });
            // Jádro plamene (žluté) a tři jazyky (oranžové)
            mb.Loft(Mat.FlameCore, new List<Vector3[]>
            {
                RingY(.318f, .036f, .036f, 6, 0f), RingY(.355f, .044f, .044f, 6, 30f), RingY(.410f, .028f, .028f, 6, 0f),
                RingY(.450f, .0f, .0f, 6, 0f),
            }, true, false);
            Vector3[] tips = { new Vector3(-.016f, .560f, .008f), new Vector3(.022f, .505f, -.014f), new Vector3(.006f, .480f, .025f) };
            Vector3[] bases = { new Vector3(-.008f, .365f, .005f), new Vector3(.016f, .360f, -.010f), new Vector3(.003f, .356f, .016f) };
            float[] rads = { .027f, .022f, .019f };
            for (int i = 0; i < 3; i++)
            {
                Vector3 mid = Vector3.Lerp(bases[i], tips[i], .4f) + new Vector3(Hash(i + 20) * .008f, 0f, Hash(i + 40) * .008f);
                var tipRing = new Vector3[5]; for (int k = 0; k < 5; k++) tipRing[k] = tips[i];
                mb.Loft(Mat.Flame, new List<Vector3[]>
                {
                    RingY(bases[i].y, rads[i] * .8f, rads[i] * .8f, 5, i * 20f, bases[i].x, bases[i].z),
                    RingY(mid.y, rads[i], rads[i], 5, i * 20f + 15f, mid.x, mid.z),
                    tipRing,
                }, true, false);
            }
        }

        private static void BuildStone(MB mb)
        {
            // Ikosaedr jednou dělený (80 ploch), zploštělý, s deterministickým rozhozením vrcholů – kámen v dlani.
            float t = (1f + Mathf.Sqrt(5f)) / 2f;
            var p = new List<Vector3>
            {
                new Vector3(-1, t, 0), new Vector3(1, t, 0), new Vector3(-1, -t, 0), new Vector3(1, -t, 0),
                new Vector3(0, -1, t), new Vector3(0, 1, t), new Vector3(0, -1, -t), new Vector3(0, 1, -t),
                new Vector3(t, 0, -1), new Vector3(t, 0, 1), new Vector3(-t, 0, -1), new Vector3(-t, 0, 1),
            };
            for (int i = 0; i < p.Count; i++) p[i] = p[i].normalized;
            var f = new List<int>
            {
                0,11,5, 0,5,1, 0,1,7, 0,7,10, 0,10,11, 1,5,9, 5,11,4, 11,10,2, 10,7,6, 7,1,8,
                3,9,4, 3,4,2, 3,2,6, 3,6,8, 3,8,9, 4,9,5, 2,4,11, 6,2,10, 8,6,7, 9,8,1,
            };
            var midCache = new Dictionary<long, int>();
            int Mid(int a, int b)
            {
                long key = a < b ? ((long)a << 32) | (uint)b : ((long)b << 32) | (uint)a;
                if (midCache.TryGetValue(key, out int idx)) return idx;
                p.Add(((p[a] + p[b]) * .5f).normalized); midCache[key] = p.Count - 1; return p.Count - 1;
            }
            var f2 = new List<int>();
            for (int i = 0; i < f.Count; i += 3)
            {
                int a = f[i], b = f[i + 1], c = f[i + 2], ab = Mid(a, b), bc = Mid(b, c), ca = Mid(c, a);
                f2.AddRange(new[] { a, ab, ca, b, bc, ab, c, ca, bc, ab, bc, ca });
            }
            // Střed nad pěstí (v004 pěst obemkne počátek ~0,07 – níž by kámen zmizel v dlani; ověřeno v Play Mode).
            Vector3 center = new Vector3(0f, .105f, 0f);
            var scale = new Vector3(.080f, .056f, .066f);
            for (int i = 0; i < p.Count; i++)
            {
                float j = 1f + (i < 12 ? .16f : .07f) * Hash(i + 100);
                Vector3 q = p[i] * j;
                // Pár sražených ploch (štípaný kámen): ořez proti dvěma rovinám.
                if (q.x + .6f * q.y > .95f) q -= new Vector3(1f, .6f, 0f).normalized * ((q.x + .6f * q.y - .95f) * .7f);
                if (-q.z + .3f * q.x > .9f) q += new Vector3(-.3f, 0f, 1f).normalized * ((-q.z + .3f * q.x - .9f) * .7f);
                p[i] = center + Vector3.Scale(q, scale);
            }
            for (int i = 0; i < f2.Count; i += 3) mb.Tri(Mat.Stone, p[f2[i]], p[f2[i + 1]], p[f2[i + 2]], center);
        }

        private static void BuildLog(MB mb)
        {
            // Polínko napříč dlaní (osa X), kůra po obvodu, světlé čelo se tmavším jádrem.
            const int S = 7;
            float cy = .035f;
            var secs = new List<Vector3[]>
            {
                RingX(-.165f, .060f, .060f, S, cy, 0f, 1, .07f),
                RingX(-.060f, .064f, .064f, S, cy, 0f, 2, .07f),
                RingX(.060f, .061f, .061f, S, cy, 0f, 3, .07f),
                RingX(.165f, .058f, .058f, S, cy, 0f, 4, .07f),
            };
            mb.Loft(Mat.Bark, secs, false, false);
            foreach (int end in new[] { 0, secs.Count - 1 })
            {
                var outer = secs[end];
                Vector3 c = MB.Center(outer);
                float inset = end == 0 ? .004f : -.004f;
                var inner = new Vector3[S];
                for (int k = 0; k < S; k++) inner[k] = c + (outer[k] - c) * .55f + new Vector3(inset, 0f, 0f);
                Vector3 inside = new Vector3(0f, cy, 0f);
                for (int k = 0; k < S; k++)
                {
                    int k1 = (k + 1) % S;
                    mb.Tri(Mat.WoodEnd, outer[k], outer[k1], inner[k1], inside);
                    mb.Tri(Mat.WoodEnd, outer[k], inner[k1], inner[k], inside);
                }
                mb.Cap(Mat.Wood, inner, inside);
            }
            // Pahýl větve
            mb.Loft(Mat.Bark, new List<Vector3[]>
            {
                RingY(cy + .040f, .019f, .019f, 5, 0f, .035f, .010f),
                RingY(cy + .092f, .014f, .014f, 5, 20f, .050f, .018f),
            }, false, true, Mat.WoodEnd);
        }

        // ───────────────────────── sada 2: dřevo a bobule ─────────────────────────

        /// <summary>Parametry polena jednoho druhu dřeva (osa X jako Log ze sady 1, střed nad pěstí).</summary>
        private sealed class WoodSpec
        {
            public Mat bark, cut, core;
            public Mat? band;              // pásky na kůře (bříza, třešeň, palma, mech)
            public float r = .060f, len = .33f, jitter = .07f;
            public float bend, twist, ridge, flare, taper, bandScale = 1.08f;
            public int sides = 7, segs = 4, seed = 1;
            public float[] bands;          // dvojice u0, u1 (0..1 po délce)
            public Vector4[] stubs;        // u, úhel kolem osy (°, 0 = nahoru), délka, poloměr
            public bool hollow;            // dutý (bambus)
            public float cy = .035f, cz;
        }

        private static Vector3 WoodAxis(WoodSpec w, float u)
        {
            float t = 2f * u - 1f;
            return new Vector3(-w.len * .5f + u * w.len, w.cy + w.bend * (1f - t * t), w.cz);
        }

        /// <summary>Prstenec polena v místě u. idx &lt; 0 = bez rozhozu (pásky).</summary>
        private static Vector3[] WoodRing(WoodSpec w, float u, float scale, int idx)
        {
            int S = w.ridge > 0f ? w.sides * 2 : w.sides;
            float t = 2f * u - 1f;
            float radius = w.r * (1f + w.flare * t * t) * (1f - w.taper * u) * scale;
            var c = WoodAxis(w, u);
            var ring = new Vector3[S];
            for (int k = 0; k < S; k++)
            {
                float a = (w.twist * u + k * 360f / S) * Mathf.Deg2Rad;
                float rr = radius * (w.ridge > 0f && (k & 1) == 1 ? 1f - w.ridge : 1f) * (idx >= 0 ? 1f + w.jitter * Hash(w.seed * 97 + idx * 13 + k) : 1f);
                ring[k] = c + new Vector3(0f, Mathf.Cos(a) * rr, Mathf.Sin(a) * rr);
            }
            return ring;
        }

        /// <summary>Prstenec kolmo na libovolnou osu.</summary>
        private static Vector3[] RingAxis(Vector3 c, Vector3 dir, float r, int n, float rot = 0f)
        {
            dir.Normalize();
            Vector3 a = Vector3.Cross(dir, Mathf.Abs(dir.y) < .9f ? Vector3.up : Vector3.right).normalized, b = Vector3.Cross(dir, a);
            var ring = new Vector3[n];
            for (int k = 0; k < n; k++) { float t = rot * Mathf.Deg2Rad + k * Mathf.PI * 2f / n; ring[k] = c + (a * Mathf.Cos(t) + b * Mathf.Sin(t)) * r; }
            return ring;
        }

        private static void BuildWood(MB mb, WoodSpec w)
        {
            var secs = new List<Vector3[]>();
            for (int i = 0; i <= w.segs; i++) secs.Add(WoodRing(w, i / (float)w.segs, 1f, i));
            mb.Loft(w.bark, secs, false, false);

            if (w.band.HasValue && w.bands != null)
                for (int b = 0; b + 1 < w.bands.Length; b += 2)
                {
                    float u0 = w.bands[b], u1 = w.bands[b + 1], e = (u1 - u0) * .25f;
                    mb.Loft(w.band.Value, new List<Vector3[]>
                    {
                        WoodRing(w, u0, .97f, -1), WoodRing(w, u0 + e, w.bandScale, -1), WoodRing(w, u1 - e, w.bandScale, -1), WoodRing(w, u1, .97f, -1),
                    }, false, false);
                }

            // Čela: světlý řez (letokruhy) a jádro; dutý bambus má tmavý střed.
            Vector3 inside = WoodAxis(w, .5f);
            foreach (int end in new[] { 0, secs.Count - 1 })
            {
                var outer = secs[end];
                Vector3 c = MB.Center(outer);
                float inset = end == 0 ? .004f : -.004f;
                int S = outer.Length;
                var inner = new Vector3[S];
                for (int k = 0; k < S; k++) inner[k] = c + (outer[k] - c) * (w.hollow ? .72f : .55f) + new Vector3(inset, 0f, 0f);
                for (int k = 0; k < S; k++)
                {
                    int k1 = (k + 1) % S;
                    mb.Tri(w.cut, outer[k], outer[k1], inner[k1], inside);
                    mb.Tri(w.cut, outer[k], inner[k1], inner[k], inside);
                }
                mb.Cap(w.core, inner, inside);
            }

            if (w.stubs != null)
                foreach (var st in w.stubs)
                {
                    Vector3 axis = WoodAxis(w, st.x);
                    float a = st.y * Mathf.Deg2Rad;
                    Vector3 dir = new Vector3(0f, Mathf.Cos(a), Mathf.Sin(a));
                    mb.Loft(w.bark, new List<Vector3[]>
                    {
                        RingAxis(axis + dir * (w.r * .6f), dir, st.w, 5),
                        RingAxis(axis + dir * (w.r + st.z), dir, st.w * .7f, 5, 20f),
                    }, false, true, w.cut);
                }
        }

        private static void BuildBamboo(MB mb)
        {
            // Dva stvoly vedle sebe: zelené články se světlejšími uzly, duté čelo.
            var a = new WoodSpec { bark = Mat.BambooGreen, band = Mat.BambooNode, cut = Mat.BambooCut, core = Mat.BambooHollow, hollow = true,
                                   r = .030f, len = .40f, sides = 7, jitter = .02f, seed = 22, cz = .030f, bands = new[] { .28f, .33f, .64f, .69f }, bandScale = 1.16f };
            var b = new WoodSpec { bark = Mat.BambooGreen, band = Mat.BambooNode, cut = Mat.BambooCut, core = Mat.BambooHollow, hollow = true,
                                   r = .026f, len = .34f, sides = 7, jitter = .02f, seed = 23, cz = -.027f, cy = .040f, bands = new[] { .40f, .46f, .78f, .84f }, bandScale = 1.16f };
            BuildWood(mb, a);
            BuildWood(mb, b);
            // lísteček u uzlu
            Leaf(mb, Mat.Leaf, new Vector3(.03f, .055f, .040f), new Vector3(.5f, .75f, .45f), .065f, .016f, .003f);
        }

        /// <summary>Zaoblená kulička (ikosaedr jednou dělený, mírný rozhoz) – bobule.</summary>
        private static void Blob(MB mb, Mat m, Vector3 center, float r, int seed)
        {
            float t = (1f + Mathf.Sqrt(5f)) / 2f;
            var p = new List<Vector3>
            {
                new Vector3(-1, t, 0), new Vector3(1, t, 0), new Vector3(-1, -t, 0), new Vector3(1, -t, 0),
                new Vector3(0, -1, t), new Vector3(0, 1, t), new Vector3(0, -1, -t), new Vector3(0, 1, -t),
                new Vector3(t, 0, -1), new Vector3(t, 0, 1), new Vector3(-t, 0, -1), new Vector3(-t, 0, 1),
            };
            for (int i = 0; i < p.Count; i++) p[i] = p[i].normalized;
            int[] f =
            {
                0,11,5, 0,5,1, 0,1,7, 0,7,10, 0,10,11, 1,5,9, 5,11,4, 11,10,2, 10,7,6, 7,1,8,
                3,9,4, 3,4,2, 3,2,6, 3,6,8, 3,8,9, 4,9,5, 2,4,11, 6,2,10, 8,6,7, 9,8,1,
            };
            var cache = new Dictionary<long, int>();
            int Mid(int a, int b)
            {
                long key = a < b ? ((long)a << 32) | (uint)b : ((long)b << 32) | (uint)a;
                if (cache.TryGetValue(key, out int idx)) return idx;
                p.Add(((p[a] + p[b]) * .5f).normalized); cache[key] = p.Count - 1; return p.Count - 1;
            }
            var f2 = new List<int>();
            for (int i = 0; i < f.Length; i += 3)
            {
                int a = f[i], b = f[i + 1], c = f[i + 2], ab = Mid(a, b), bc = Mid(b, c), ca = Mid(c, a);
                f2.AddRange(new[] { a, ab, ca, b, bc, ab, c, ca, bc, ab, bc, ca });
            }
            for (int i = 0; i < p.Count; i++) p[i] = center + p[i] * r * (1f + .05f * Hash(seed * 53 + i));
            for (int i = 0; i < f2.Count; i += 3) mb.Tri(m, p[f2[i]], p[f2[i + 1]], p[f2[i + 2]], center);
        }

        /// <summary>Lístek: zploštělý kosočtverec s tloušťkou (viditelný z obou stran).</summary>
        private static void Leaf(MB mb, Mat m, Vector3 basePt, Vector3 dir, float len, float w, float t)
        {
            dir.Normalize();
            Vector3 side = Vector3.Cross(dir, Vector3.forward); if (side.sqrMagnitude < 1e-4f) side = Vector3.right; side.Normalize();
            Vector3 n = Vector3.Cross(dir, side).normalized;
            Vector3 mid = basePt + dir * len * .45f, tip = basePt + dir * len;
            mb.Loft(m, new List<Vector3[]>
            {
                new[] { basePt, basePt, basePt, basePt },
                new[] { mid + side * w, mid + n * t, mid - side * w, mid - n * t },
                new[] { tip, tip, tip, tip },
            }, false, false);
        }

        private static void BuildBerries(MB mb)
        {
            // Hrozníček bobulí nad pěstí (jako kámen), stopka a dva lístky – barvy podle původní ikony.
            Blob(mb, Mat.Berry,      new Vector3(-.022f, .098f,  .014f), .027f, 1);
            Blob(mb, Mat.Berry,      new Vector3( .024f, .094f, -.004f), .026f, 2);
            Blob(mb, Mat.BerryLight, new Vector3( .001f, .124f, -.006f), .025f, 3);
            Blob(mb, Mat.Berry,      new Vector3( .002f, .088f, -.032f), .023f, 4);
            mb.Loft(Mat.Stem, new List<Vector3[]>
            {
                RingAxis(new Vector3(0f, .135f, -.004f), new Vector3(.1f, 1f, .1f), .004f, 5),
                RingAxis(new Vector3(.005f, .172f, .002f), new Vector3(.1f, 1f, .1f), .003f, 5),
            }, true, true);
            Leaf(mb, Mat.Leaf, new Vector3(.002f, .160f, 0f), new Vector3(-.65f, .55f, .25f), .050f, .017f, .004f);
            Leaf(mb, Mat.Leaf, new Vector3(.004f, .165f, 0f), new Vector3(.70f, .50f, -.30f), .044f, .015f, .004f);
        }

        // ───────────────────────── sada 3: klacek a provaz ─────────────────────────

        /// <summary>Trubka podél lomené čáry (paralelní přenos rámce); poloměr na bod, materiál na úsek.</summary>
        private static void TubePath(MB mb, IList<Vector3> pts, IList<float> radii, int sides, System.Func<int, Mat> segMat, Mat? capStart, Mat? capEnd)
        {
            int n = pts.Count;
            var rings = new List<Vector3[]>(n);
            Vector3 prevN = Vector3.zero;
            for (int i = 0; i < n; i++)
            {
                Vector3 t = (i == 0 ? pts[1] - pts[0] : i == n - 1 ? pts[n - 1] - pts[n - 2] : pts[i + 1] - pts[i - 1]).normalized;
                Vector3 nn = i == 0 ? Vector3.Cross(t, Mathf.Abs(t.y) < .9f ? Vector3.up : Vector3.right).normalized
                                    : (prevN - Vector3.Dot(prevN, t) * t).normalized;
                prevN = nn;
                Vector3 b = Vector3.Cross(t, nn);
                var ring = new Vector3[sides];
                for (int k = 0; k < sides; k++) { float a = k * Mathf.PI * 2f / sides; ring[k] = pts[i] + (nn * Mathf.Cos(a) + b * Mathf.Sin(a)) * radii[i]; }
                rings.Add(ring);
            }
            for (int i = 0; i + 1 < n; i++) mb.Loft(segMat(i), new List<Vector3[]> { rings[i], rings[i + 1] }, false, false);
            if (capStart.HasValue) mb.Cap(capStart.Value, rings[0], pts[1]);
            if (capEnd.HasValue) mb.Cap(capEnd.Value, rings[n - 1], pts[n - 2]);
        }

        private static void BuildStick(MB mb)
        {
            // Větvička: hlavní prut v ose úchopu s mírnými ohyby, boční výhonek a jeden lístek.
            var main = new List<Vector3> { new Vector3(0f, -.215f, 0f), new Vector3(.004f, -.10f, .002f), new Vector3(-.005f, .02f, -.003f), new Vector3(.006f, .14f, .002f), new Vector3(.014f, .25f, -.002f), new Vector3(.020f, .32f, .002f) };
            var mr = new List<float> { .0185f, .0190f, .0180f, .0165f, .0145f, .0120f };
            TubePath(mb, main, mr, 6, i => Mat.Bark, Mat.WoodEnd, Mat.WoodEnd);
            var twig = new List<Vector3> { new Vector3(.002f, .10f, 0f), new Vector3(-.030f, .16f, .006f), new Vector3(-.058f, .215f, .010f), new Vector3(-.072f, .255f, .012f) };
            TubePath(mb, twig, new List<float> { .0110f, .0090f, .0068f, .0045f }, 5, i => Mat.Bark, null, Mat.WoodEnd);
            var twig2 = new List<Vector3> { new Vector3(.003f, -.02f, 0f), new Vector3(.030f, .02f, -.004f), new Vector3(.046f, .045f, -.006f) };
            TubePath(mb, twig2, new List<float> { .0085f, .0062f, .0040f }, 5, i => Mat.Bark, null, Mat.WoodEnd);
            Leaf(mb, Mat.Leaf, new Vector3(-.070f, .250f, .012f), new Vector3(-.4f, .8f, .25f), .045f, .015f, .003f);
            Leaf(mb, Mat.Leaf, new Vector3(.018f, .31f, .002f), new Vector3(.45f, .75f, -.2f), .040f, .013f, .003f);
        }

        private static void BuildRope(MB mb)
        {
            // Smotek: tři závity (střídavě světlejší/tmavší úseky = zkroucený provaz), dvě převázání a volný konec.
            const float R = .056f, y0 = .085f, rr = .0115f;
            var pts = new List<Vector3>(); var rad = new List<float>();
            int per = 12, loops = 3;
            for (int i = 0; i <= per * loops; i++)
            {
                float t = i * Mathf.PI * 2f / per;
                float r = R * (1f + .05f * Mathf.Sin(t * 1.5f + 1f)) + (i / per - 1) * .006f;
                pts.Add(new Vector3(Mathf.Cos(t) * r, y0 + t / (Mathf.PI * 2f) * .016f, Mathf.Sin(t) * r));
                rad.Add(rr);
            }
            // volný konec: ven ze smotku a dolů, roztřepený konec
            Vector3 e = pts[pts.Count - 1];
            Vector3 dir = new Vector3(e.x, 0f, e.z).normalized;
            Vector3 tan = Vector3.Cross(Vector3.up, dir);
            pts.Add(e + tan * .025f + dir * .012f + Vector3.up * .002f); rad.Add(rr);
            pts.Add(e + tan * .048f + dir * .030f - Vector3.up * .010f); rad.Add(rr);
            pts.Add(e + tan * .060f + dir * .046f - Vector3.up * .028f); rad.Add(rr * 1.1f);
            TubePath(mb, pts, rad, 6, i => (i & 1) == 0 ? Mat.Cord : Mat.CordDark, Mat.CordDark, Mat.CordDark);
            // roztřepení: tři krátké vlákna
            Vector3 tip = pts[pts.Count - 1];
            for (int k = 0; k < 3; k++)
            {
                float a = k * 2.1f;
                Vector3 off = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * .006f;
                TubePath(mb, new List<Vector3> { tip + off, tip + off * 1.8f + Vector3.down * .016f }, new List<float> { .0035f, .0015f }, 4, i => Mat.Cord, null, Mat.Cord);
            }
            // převázání smotku na dvou místech (malý prstenec kolem svazku závitů)
            foreach (float ang in new[] { .6f, 3.6f })
            {
                Vector3 c = new Vector3(Mathf.Cos(ang) * R, y0 + .016f, Mathf.Sin(ang) * R);
                Vector3 radial = new Vector3(Mathf.Cos(ang), 0f, Mathf.Sin(ang));
                var ring = new List<Vector3>(); var rr2 = new List<float>();
                for (int i = 0; i <= 10; i++)
                {
                    float a = i * Mathf.PI * 2f / 10f;
                    ring.Add(c + (radial * Mathf.Cos(a) * .026f) + Vector3.up * (Mathf.Sin(a) * .036f));
                    rr2.Add(.0055f);
                }
                TubePath(mb, ring, rr2, 5, i => Mat.CordDark, null, null);
            }
        }

        // ───────────────────────── assety ─────────────────────────

        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            string parent = Path.GetDirectoryName(path).Replace('\\', '/');
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
        }

        private static Material GetMaterial(Mat m)
        {
            string path = Root + "/Materials/HI_" + m + ".mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            bool create = mat == null;
            if (create) mat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            var (c, smooth, emit) = Palette[m];
            mat.SetColor("_BaseColor", c);
            mat.SetFloat("_Smoothness", smooth);
            mat.SetFloat("_Metallic", 0f);
            if (emit > 0f)
            {
                mat.EnableKeyword("_EMISSION");
                mat.SetColor("_EmissionColor", c * emit);
                mat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
            }
            mat.enableInstancing = true;
            if (create) AssetDatabase.CreateAsset(mat, path); else EditorUtility.SetDirty(mat);
            return mat;
        }

        private static GameObject BuildPrefab(Def d, StringBuilder log)
        {
            var mb = new MB();
            d.build(mb);
            var fresh = mb.ToMesh("HI_" + d.key, out var order);
            string meshPath = Root + "/Meshes/HI_" + d.key + ".asset";
            var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(meshPath);
            if (mesh == null) { AssetDatabase.CreateAsset(fresh, meshPath); mesh = fresh; }
            else { mesh.Clear(); EditorUtility.CopySerialized(fresh, mesh); mesh.name = "HI_" + d.key; Object.DestroyImmediate(fresh); EditorUtility.SetDirty(mesh); }

            var mats = new Material[order.Length];
            for (int i = 0; i < order.Length; i++) mats[i] = GetMaterial(order[i]);

            var go = new GameObject("HI_" + d.key);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterials = mats;
            go.AddComponent<HeldItemGrip>();
            string prefabPath = Root + "/Prefabs/HI_" + d.key + ".prefab";
            var prefab = PrefabUtility.SaveAsPrefabAsset(go, prefabPath);
            Object.DestroyImmediate(go);
            var b = mesh.bounds;
            log.Append(d.key).Append(": ").Append(mesh.vertexCount).Append(" vrcholů, ").Append(mesh.vertexCount / 3).Append(" trojúhelníků, submeshe ")
               .Append(string.Join("/", System.Array.ConvertAll(order, o => o.ToString()))).Append(", bounds min ").Append(b.min.ToString("F3")).Append(" max ").Append(b.max.ToString("F3")).Append('\n');
            return prefab;
        }

        [MenuItem(M + "1 Build models + prefabs")]
        public static void BuildAll()
        {
            foreach (var f in new[] { "Meshes", "Materials", "Prefabs", "Icons" }) EnsureFolder(Root + "/" + f);
            var log = new StringBuilder("hand items build\n");
            foreach (var d in Defs) BuildPrefab(d, log);
            AssetDatabase.SaveAssets();
            Directory.CreateDirectory(ReportDir);
            File.WriteAllText(Path.Combine(ReportDir, "meshes.txt"), log.ToString());
            Out(log.ToString());
        }

        // ───────────────────────── ikony ─────────────────────────

        private const int IconSize = 256, Super = 4;
        private const float Margin = .07f;   // okraj z každé strany (podíl rozměru)

        /// <summary>Vyrenderuje předmět ortograficky na průhledné pozadí, supersampling 4×, výřez podle skutečných vrcholů.</summary>
        private static Texture2D Render(GameObject prefab, Quaternion rot, int size, int super, float margin, bool perspective = false, float outline = 0f)
        {
            var pru = new PreviewRenderUtility();
            try
            {
                var cam = pru.camera;
                cam.orthographic = !perspective;
                cam.fieldOfView = 22f;
                cam.clearFlags = CameraClearFlags.SolidColor;
                cam.backgroundColor = new Color(0f, 0f, 0f, 0f);
                cam.allowHDR = false; cam.allowMSAA = false;
                cam.nearClipPlane = .01f; cam.farClipPlane = 20f;

                // Hlavní světlo zleva shora zepředu, studený doplněk zprava zezadu (kontura), měkké okolí.
                pru.lights[0].intensity = 1.15f;
                pru.lights[0].color = new Color(1f, .97f, .92f);
                pru.lights[0].transform.rotation = Quaternion.Euler(38f, 32f, 0f);
                pru.lights[1].intensity = .55f;
                pru.lights[1].color = new Color(.80f, .87f, 1f);
                pru.lights[1].transform.rotation = Quaternion.Euler(12f, -140f, 0f);
                pru.ambientColor = new Color(.42f, .43f, .47f);

                var go = pru.InstantiatePrefabInScene(prefab);
                go.transform.position = Vector3.zero;
                go.transform.rotation = rot;

                // Výřez podle promítnutých vrcholů (ne podle AABB – ta by nechávala prázdné rohy).
                var mf = go.GetComponentInChildren<MeshFilter>();
                var verts = mf.sharedMesh.vertices;
                Vector3 min = new Vector3(1e9f, 1e9f, 1e9f), max = -min;
                foreach (var v in verts) { var w = mf.transform.TransformPoint(v); min = Vector3.Min(min, w); max = Vector3.Max(max, w); }
                Vector3 mid = (min + max) * .5f;
                float half = Mathf.Max(max.x - min.x, max.y - min.y) * .5f / (1f - 2f * margin);
                if (perspective)
                {
                    float dist = half / Mathf.Tan(cam.fieldOfView * .5f * Mathf.Deg2Rad) + (max.z - min.z) * .5f;
                    cam.transform.position = new Vector3(mid.x, mid.y, min.z - dist + (max.z - min.z) * .5f);
                }
                else
                {
                    cam.orthographicSize = half;
                    cam.transform.position = new Vector3(mid.x, mid.y, min.z - 2f);
                }
                cam.transform.rotation = Quaternion.identity;

                int big = size * super;
                pru.BeginStaticPreview(new Rect(0, 0, big, big));
                pru.Render(true, false);
                var rt = cam.targetTexture;
                var prev = RenderTexture.active;
                RenderTexture.active = rt;
                var hi = new Texture2D(rt.width, rt.height, TextureFormat.RGBAFloat, false, false);
                hi.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
                hi.Apply();
                RenderTexture.active = prev;
                var tmp = pru.EndStaticPreview();
                if (tmp != null) Object.DestroyImmediate(tmp);

                var px = hi.GetPixels();
                int W = rt.width, H = rt.height;
                Object.DestroyImmediate(hi);
                // Box downsample s vahou alfy (bez tmavých lemů); RT může být lineární → převod do sRGB.
                bool linearRT = !rt.sRGB && QualitySettings.activeColorSpace == ColorSpace.Linear;
                var outTex = new Texture2D(size, size, TextureFormat.RGBA32, false);
                var o = new Color[size * size];
                int sx = W / size, sy = H / size;
                for (int y = 0; y < size; y++)
                    for (int x = 0; x < size; x++)
                    {
                        float r = 0, g = 0, bl = 0, a = 0;
                        for (int j = 0; j < sy; j++)
                            for (int i = 0; i < sx; i++)
                            {
                                var c = px[(y * sy + j) * W + x * sx + i];
                                float ca = Mathf.Clamp01(c.a);
                                r += c.r * ca; g += c.g * ca; bl += c.b * ca; a += ca;
                            }
                        Color res = a > 1e-5f ? new Color(r / a, g / a, bl / a, a / (sx * sy)) : new Color(0, 0, 0, 0);
                        if (linearRT) res = new Color(Mathf.LinearToGammaSpace(res.r), Mathf.LinearToGammaSpace(res.g), Mathf.LinearToGammaSpace(res.b), res.a);
                        o[y * size + x] = res;
                    }
                if (outline > 0f) o = Outline(o, size, outline);
                outTex.SetPixels(o);
                outTex.Apply();
                return outTex;
            }
            finally { pru.Cleanup(); }
        }

        /// <summary>
        /// Tmavý měkký obrys kolem siluety (ve stylu stávajících kreslených ikon s černou konturou): předmět
        /// zůstane čitelný na tmavém slotu hotbaru i na světlém panelu inventáře.
        /// </summary>
        private static Color[] Outline(Color[] src, int size, float radius)
        {
            var dst = new Color[src.Length];
            int R = Mathf.CeilToInt(radius + 1f);
            var ink = new Color(.10f, .08f, .07f);
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float m = 0f;
                    for (int j = -R; j <= R; j++)
                    {
                        int yy = y + j; if (yy < 0 || yy >= size) continue;
                        for (int i = -R; i <= R; i++)
                        {
                            int xx = x + i; if (xx < 0 || xx >= size) continue;
                            float d = Mathf.Sqrt(i * i + j * j);
                            float w = Mathf.Clamp01(radius + .5f - d);
                            if (w <= 0f) continue;
                            m = Mathf.Max(m, src[yy * size + xx].a * w);
                        }
                    }
                    var c = src[y * size + x];
                    float oa = m * .9f;
                    float a = c.a + oa * (1f - c.a);
                    dst[y * size + x] = a > 1e-5f ? new Color((c.r * c.a + ink.r * oa * (1f - c.a)) / a, (c.g * c.a + ink.g * oa * (1f - c.a)) / a, (c.b * c.a + ink.b * oa * (1f - c.a)) / a, a) : new Color(0, 0, 0, 0);
                }
            return dst;
        }

        private static string IconPath(Def d) => Root + "/Icons/HI_" + d.key + "_Icon.png";

        [MenuItem(M + "2 Render icons")]
        public static void RenderIcons()
        {
            EnsureFolder(Root + "/Icons");
            Directory.CreateDirectory(ReportDir);
            var log = new StringBuilder("icons\n");
            foreach (var d in Defs)
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(Root + "/Prefabs/HI_" + d.key + ".prefab");
                if (prefab == null) { log.Append("ERR chybí prefab ").Append(d.key).Append('\n'); continue; }
                var tex = Render(prefab, Quaternion.Euler(d.iconEuler), IconSize, Super, Margin, false, 2.2f);
                string path = IconPath(d);
                File.WriteAllBytes(Path.Combine(ProjectRoot, path), tex.EncodeToPNG());
                Object.DestroyImmediate(tex);
                AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
                var ti = (TextureImporter)AssetImporter.GetAtPath(path);
                // Stejné nastavení jako stávající ikony v Assets/GUI/Icons (Sprite, bez mipmap, nečitelné, bilinear).
                ti.textureType = TextureImporterType.Sprite;
                ti.spriteImportMode = SpriteImportMode.Single;
                ti.alphaIsTransparency = true;
                ti.mipmapEnabled = false;
                ti.isReadable = false;
                ti.wrapMode = TextureWrapMode.Clamp;
                ti.filterMode = FilterMode.Bilinear;
                ti.sRGBTexture = true;
                ti.spritePixelsPerUnit = 100;
                ti.textureCompression = TextureImporterCompression.Uncompressed;
                ti.SaveAndReimport();

                // Náhled modelu do přehledu: perspektivní tříčtvrteční pohled (větší) + boční pohled v prostoru úchopu.
                var big = Render(prefab, Quaternion.Euler(d.iconEuler), 512, 2, .06f, true);
                File.WriteAllBytes(Path.Combine(ReportDir, "model_" + d.key + ".png"), big.EncodeToPNG());
                Object.DestroyImmediate(big);
                var side = Render(prefab, Quaternion.identity, 512, 2, .06f);
                File.WriteAllBytes(Path.Combine(ReportDir, "side_" + d.key + ".png"), side.EncodeToPNG());
                Object.DestroyImmediate(side);
                log.Append(d.key).Append(" → ").Append(path).Append('\n');
            }
            AssetDatabase.Refresh();
            Out(log.ToString());
        }

        [MenuItem(M + "3 Assign to items")]
        public static void Assign()
        {
            var log = new StringBuilder("assign\n");
            var genericLog = AssetDatabase.LoadAssetAtPath<InventoryItemData>("Assets/Resources/Items/Log.asset");
            foreach (var d in Defs)
            {
                var item = AssetDatabase.LoadAssetAtPath<InventoryItemData>(d.itemAsset);
                if (item == null && d.newId != null) item = CreateItem(d, genericLog, log);
                else if (item != null && d.newId != null && item.id != d.newId) { log.Append("ERR ").Append(d.key).Append(": asset má ID ").Append(item.id).Append(", čekáno ").Append(d.newId).Append(" – nic neměním\n"); continue; }
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(Root + "/Prefabs/HI_" + d.key + ".prefab");
                var icon = AssetDatabase.LoadAssetAtPath<Sprite>(IconPath(d));
                if (item == null || prefab == null || icon == null) { log.Append("ERR ").Append(d.key).Append(item == null ? " item" : "").Append(prefab == null ? " prefab" : "").Append(icon == null ? " icon" : "").Append('\n'); continue; }
                log.Append(d.key).Append(" (").Append(item.id).Append(") handPrefab ")
                   .Append(item.handPrefab != null ? AssetDatabase.GetAssetPath(item.handPrefab) : "—").Append(" → ").Append(AssetDatabase.GetAssetPath(prefab))
                   .Append("; icon ").Append(item.icon != null ? AssetDatabase.GetAssetPath(item.icon) : "—").Append(" → ").Append(IconPath(d)).Append('\n');
                var so = new SerializedObject(item);
                so.FindProperty("handPrefab").objectReferenceValue = prefab;
                so.FindProperty("icon").objectReferenceValue = icon;
                if (d.woodVariant && genericLog != null)
                {
                    // Druh dřeva platí za Log (stavby, Arcanum) a stackuje se stejně jako Log.
                    so.FindProperty("countsAs").objectReferenceValue = genericLog;
                    so.FindProperty("stackable").boolValue = true;
                    so.FindProperty("maxStack").intValue = genericLog.maxStack;
                }
                so.ApplyModifiedPropertiesWithoutUndo();
                AssetDatabase.SaveAssetIfDirty(item);
            }
            Out(log.ToString());
        }

        /// <summary>Nový item přes existující InventoryItemData (stejně jako „Inventory/Inventory Item“) – .meta vytvoří Unity.</summary>
        private static InventoryItemData CreateItem(Def d, InventoryItemData genericLog, StringBuilder log)
        {
            foreach (var x in Resources.LoadAll<InventoryItemData>(""))
                if (x.id == d.newId) { log.Append("ERR ").Append(d.newId).Append(" už existuje v ").Append(AssetDatabase.GetAssetPath(x)).Append('\n'); return null; }
            var it = ScriptableObject.CreateInstance<InventoryItemData>();
            it.id = d.newId;
            it.itemName = d.newName;
            it.description = d.newDesc;
            it.stackable = true;
            it.maxStack = genericLog != null ? genericLog.maxStack : 8;
            it.countsAs = d.woodVariant ? genericLog : null;
            AssetDatabase.CreateAsset(it, d.itemAsset);
            log.Append("vytvořen item ").Append(d.newId).Append(" → ").Append(d.itemAsset).Append('\n');
            return it;
        }

        /// <summary>Skutečné definice stromů: základní spawnables + doplňková sada BiomeProps (stejné zdroje jako ObjectSpawner).</summary>
        private static List<(string source, Orivilon.World.Spawning.SpawnableObject s)> AllSpawnables()
        {
            var list = new List<(string, Orivilon.World.Spawning.SpawnableObject)>();
            var root = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Resources/Prefabs/SpawnableObjects.prefab");
            var sp = root != null ? root.GetComponentInChildren<Orivilon.World.Spawning.ObjectSpawner>(true) : null;
            if (sp != null) foreach (var e in sp.spawnables) list.Add(("spawnables", e));
            foreach (var guid in AssetDatabase.FindAssets("t:BiomePropSet", new[] { "Assets/Resources" }))
            {
                var set = AssetDatabase.LoadAssetAtPath<Orivilon.World.Spawning.BiomePropSet>(AssetDatabase.GUIDToAssetPath(guid));
                if (set != null) foreach (var e in set.entries) list.Add((set.name, e));
            }
            return list;
        }

        /// <summary>
        /// Druhy dřeva do odměn za těžbu: v HarvestableObject.drops každého těžitelného stromu nahradí Log dřevem jeho druhu
        /// (množství a šance beze změny). Mění jen data prefabu stromu – generování, seedy ani identity objektů ne.
        /// Host v MP čte stejné drops z definice (NetworkWorldSync.FinishLoot), klient dostane ID itemu.
        /// </summary>
        [MenuItem(M + "4 Assign tree drops")]
        public static void AssignTreeDrops()
        {
            var genericLog = AssetDatabase.LoadAssetAtPath<InventoryItemData>("Assets/Resources/Items/Log.asset");
            var woods = new Dictionary<string, InventoryItemData>();
            foreach (var d in Defs) if (d.woodVariant) woods[d.key] = AssetDatabase.LoadAssetAtPath<InventoryItemData>(d.itemAsset);
            var log = new StringBuilder("tree drops\n");
            var tsv = new StringBuilder("zdroj\tspawnable\tprefab\tdřevo\titem ID\tmnožství\tšance\n");
            var done = new HashSet<GameObject>();
            int changed = 0, errors = 0;
            foreach (var (source, e) in AllSpawnables())
            {
                if (e == null || e.prefab == null) continue;
                var hs = e.prefab.GetComponentsInChildren<Orivilon.World.Objects.HarvestableObject>(true);
                if (hs.Length == 0) continue;
                string woodKey = WoodForPrefab(e.prefab.name);
                bool any = false;
                foreach (var h in hs)
                {
                    var so = new SerializedObject(h);
                    var drops = so.FindProperty("drops");
                    for (int i = 0; i < drops.arraySize; i++)
                    {
                        var el = drops.GetArrayElementAtIndex(i);
                        var itemProp = el.FindPropertyRelative("item");
                        var cur = itemProp.objectReferenceValue as InventoryItemData;
                        bool isWood = cur == genericLog || (cur != null && cur.countsAs == genericLog);
                        if (!isWood) continue;
                        any = true;
                        if (woodKey == null || !woods.TryGetValue(woodKey, out var target) || target == null)
                        { log.Append("ERR bez druhu dřeva: ").Append(e.prefab.name).Append('\n'); errors++; continue; }
                        if (cur != target) { itemProp.objectReferenceValue = target; changed++; }
                        tsv.Append(source).Append('\t').Append(e.name).Append('\t').Append(AssetDatabase.GetAssetPath(e.prefab)).Append('\t').Append(woodKey).Append('\t').Append(target.id)
                           .Append('\t').Append(el.FindPropertyRelative("minAmount").intValue).Append('–').Append(el.FindPropertyRelative("maxAmount").intValue)
                           .Append('\t').Append(el.FindPropertyRelative("chance").floatValue.ToString(System.Globalization.CultureInfo.InvariantCulture)).Append('\n');
                    }
                    if (so.ApplyModifiedPropertiesWithoutUndo()) EditorUtility.SetDirty(h);
                }
                if (any && done.Add(e.prefab)) { PrefabUtility.SavePrefabAsset(e.prefab); }
                if (!any && hs[0].requiredTool == ToolType.Axe) log.Append("pozn.: těžitelný sekerou bez dřeva: ").Append(e.prefab.name).Append('\n');
            }
            Directory.CreateDirectory(ReportDir);
            File.WriteAllText(Path.Combine(ReportDir, "tree-wood-map.tsv"), tsv.ToString());
            log.Append("prefabů ").Append(done.Count).Append(", změněných dropů ").Append(changed).Append(", chyb ").Append(errors).Append('\n');
            Out(log.ToString());
        }

        // ───────────────────────── QA v Play Mode ─────────────────────────

        private const string Q = M + "QA/";

        private static void Select(string id)
        {
            var inv = InventoryData.Instance; var hot = Orivilon.Inventory.Hotbar.HotbarController.Instance;
            if (inv == null || hot == null) { Out("ERR inventář/hotbar chybí"); return; }
            InventoryItemData it = null;
            foreach (var x in Resources.LoadAll<InventoryItemData>("")) if (x.id == id) it = x;
            if (it == null) { Out("ERR item " + id); return; }
            int slot = -1;
            for (int i = 0; i < InventoryData.HotbarSize; i++) if (!inv.Slots[i].IsEmpty && inv.Slots[i].item == it) slot = i;
            if (slot < 0) { inv.AddItem(it, 1); for (int i = 0; i < InventoryData.HotbarSize; i++) if (!inv.Slots[i].IsEmpty && inv.Slots[i].item == it) slot = i; }
            if (slot < 0) { Out("ERR " + id + " není v hotbaru"); return; }
            hot.SetActiveSlot(slot);
            Out("vybráno " + id + " slot " + slot);
        }

        [MenuItem(Q + "Select axe")] public static void SAxe() => Select("everlost:stone_axe");
        [MenuItem(Q + "Select pickaxe")] public static void SPick() => Select("everlost:stone_pickaxe");
        [MenuItem(Q + "Select sword")] public static void SSword() => Select("everlost:stone_sword");
        [MenuItem(Q + "Select torch")] public static void STorch() => Select("everlost:torch");
        [MenuItem(Q + "Select stone")] public static void SStone() => Select("everlost:stone");
        [MenuItem(Q + "Select log")] public static void SLog() => Select("everlost:log");

        /// <summary>Výpis držených instancí: co je v ruce 1. osoby (HandItemRenderer) a na těle (3. osoba / vzdálený hráč).</summary>
        [MenuItem(Q + "Report held")]
        public static void ReportHeld()
        {
            var sb = new StringBuilder("held:");
            var h = Orivilon.Inventory.Hotbar.HandItemRenderer.Instance;
            sb.Append(" current=").Append(h != null && h.CurrentItem != null ? h.CurrentItem.id : "—");
            foreach (var t in Object.FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (!t.name.StartsWith("Held_")) continue;
                var mf = t.GetComponentInChildren<MeshFilter>(true);
                var r = t.GetComponentInChildren<Renderer>(true);
                sb.Append("\n  ").Append(t.name).Append(" parent=").Append(t.parent != null ? t.parent.name : "—")
                  .Append(" mesh=").Append(mf != null && mf.sharedMesh != null ? mf.sharedMesh.name : "—")
                  .Append(" grip=").Append(t.GetComponent<HeldItemGrip>() != null)
                  .Append(" lp=").Append(t.localPosition.ToString("F3")).Append(" ls=").Append(t.localScale.x.ToString("F3"))
                  .Append(" layer=").Append(LayerMask.LayerToName(t.gameObject.layer))
                  .Append(" visible=").Append(r != null && r.enabled && t.gameObject.activeInHierarchy)
                  .Append(" shadows=").Append(r != null ? r.shadowCastingMode.ToString() : "—")
                  .Append(" worldSize=").Append(r != null ? r.bounds.size.magnitude.ToString("F2") : "—");
            }
            Out(sb.ToString());
        }

        /// <summary>Vyhodí před hráče po kusu z každého předmětu sady – kontrola modelu vyhozeného předmětu (QA svět).</summary>
        [MenuItem(Q + "Drop test")]
        public static void DropTest()
        {
            var sb = new StringBuilder("droptest:");
            foreach (var d in Defs)
            {
                var item = AssetDatabase.LoadAssetAtPath<InventoryItemData>(d.itemAsset);
                bool ok = Orivilon.World.Objects.ItemDropService.TrySpawnInFrontOfPlayer(item, 1, Orivilon.World.Objects.ItemDropService.HarvestDropPickupDelay);
                sb.Append(' ').Append(d.key).Append('=').Append(ok);
            }
            Out(sb.ToString());
        }
        // ───────────────────────── QA sady 2: bobule a druhy dřeva ─────────────────────────

        private static List<string> WoodIdList() { var l = new List<string>(); foreach (var d in Defs) if (d.woodVariant) l.Add(d.newId); return l; }
        private static string WoodIdForPrefab(string prefabName)
        {
            string key = WoodForPrefab(prefabName);
            foreach (var d in Defs) if (d.key == key) return d.newId;
            return null;
        }
        private static InventoryItemData ItemById(string id) { foreach (var x in Resources.LoadAll<InventoryItemData>("")) if (x.id == id) return x; return null; }
        private static int CountInv(string id) { int n = 0; foreach (var s in InventoryData.Instance.Slots) if (!s.IsEmpty && s.item.id == id) n += s.amount; return n; }
        private static void ClearInv() { foreach (var s in InventoryData.Instance.Slots) s.Clear(); InventoryData.Instance.NotifyInventoryChanged(); }
        private static string InvDump()
        {
            var sb = new StringBuilder();
            var slots = InventoryData.Instance.Slots;
            for (int i = 0; i < slots.Length; i++) if (!slots[i].IsEmpty) sb.Append(" [").Append(i).Append(']').Append(slots[i].item.id).Append('×').Append(slots[i].amount);
            return sb.ToString();
        }

        private static int woodSel = -1;
        [MenuItem(Q + "Select berries")] public static void SBerries() => Select("everlost:berries");
        [MenuItem(Q + "Select next wood")]
        public static void SNextWood()
        {
            var ids = WoodIdList();
            woodSel = (woodSel + 1) % ids.Count;
            if (InventoryData.Instance != null) { foreach (var s in InventoryData.Instance.Slots) if (!s.IsEmpty && s.item.countsAs != null) s.Clear(); InventoryData.Instance.NotifyInventoryChanged(); }   // hotbar má 8 slotů
            Select(ids[woodSel]);
        }

        /// <summary>
        /// Každý těžitelný strom z runtime ObjectSpawner.spawnables (vč. BiomeProps): instance prefabu mimo hráče,
        /// skutečná cesta HarvestableObject.TryHarvest sekerou, odměna v inventáři porovnaná s očekávaným druhem dřeva.
        /// </summary>
        [MenuItem(Q + "Tree drop test")]
        public static void TreeDropTest()
        {
            var sp = Orivilon.World.Spawning.ObjectSpawner.Instance; var inv = InventoryData.Instance; var player = GameObject.FindWithTag("Player");
            if (sp == null || inv == null || player == null) { Out("ERR treedrop: hra neběží"); return; }
            var axe = ItemById("everlost:stone_axe");
            var sb = new StringBuilder("treedrop:");
            int ok = 0, bad = 0;
            var seen = new HashSet<GameObject>();
            foreach (var e in sp.spawnables)
            {
                if (e == null || e.prefab == null || !seen.Add(e.prefab)) continue;
                var h0 = e.prefab.GetComponentInChildren<Orivilon.World.Objects.HarvestableObject>(true);
                if (h0 == null || h0.requiredTool != ToolType.Axe) continue;
                string expect = WoodIdForPrefab(e.prefab.name);
                ClearInv();
                var go = Object.Instantiate(e.prefab, player.transform.position + Vector3.down * 400f, Quaternion.identity);
                foreach (var id in go.GetComponentsInChildren<Orivilon.World.Objects.DeterministicObjectId>(true)) Object.DestroyImmediate(id);   // test nic nezapíše do registru zničených
                var h = go.GetComponentInChildren<Orivilon.World.Objects.HarvestableObject>(true);
                for (int i = 0; i < h0.maxHealth; i++) h.TryHarvest(axe);
                var got = new SortedDictionary<string, int>();
                foreach (var s in inv.Slots) if (!s.IsEmpty) { got.TryGetValue(s.item.id, out int n); got[s.item.id] = n + s.amount; }
                if (go != null) Object.Destroy(go);
                bool pass = expect != null && got.ContainsKey(expect) && !got.ContainsKey("everlost:log");
                if (pass) ok++; else bad++;
                sb.Append("\n  ").Append(pass ? "PASS " : "FAIL ").Append(e.name).Append(" (").Append(e.prefab.name).Append(") →");
                foreach (var kv in got) { var it = ItemById(kv.Key); sb.Append(' ').Append(kv.Key).Append('×').Append(kv.Value).Append(" \"").Append(it != null ? it.itemName : "?").Append('"'); }
            }
            ClearInv();
            sb.Append("\n  PASS ").Append(ok).Append(" / FAIL ").Append(bad);
            Out(sb.ToString());
        }

        /// <summary>Stromy skutečně načtené ve světě (instance z chunků): druh → drop, porovnání s mapou druhů.</summary>
        [MenuItem(Q + "World tree census")]
        public static void WorldTreeCensus()
        {
            var groups = new SortedDictionary<string, (int n, string drops, bool pass)>();
            foreach (var h in Object.FindObjectsByType<Orivilon.World.Objects.HarvestableObject>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
            {
                if (h.requiredTool != ToolType.Axe) continue;
                string name = h.name.Replace("(Clone)", "").Trim();
                var ids = new List<string>();
                if (h.drops != null) foreach (var d in h.drops) if (d != null && d.item != null) ids.Add(d.item.id);
                string expect = WoodIdForPrefab(name);
                bool pass = expect != null && ids.Contains(expect) && !ids.Contains("everlost:log");
                groups.TryGetValue(name, out var g);
                groups[name] = (g.n + 1, string.Join(",", ids), (g.n == 0 || g.pass) && pass);
            }
            var sb = new StringBuilder("census:");
            foreach (var kv in groups) sb.Append("\n  ").Append(kv.Value.pass ? "PASS " : "FAIL ").Append(kv.Key).Append(" ×").Append(kv.Value.n).Append(" → ").Append(kv.Value.drops);
            if (groups.Count == 0) sb.Append(" žádný těžitelný strom v načtených chuncích");
            Out(sb.ToString());
        }

        /// <summary>Stavba LogWall (cena Log) z každého druhu zvlášť, ze směsi a s nedostatkem; Arcanum počítá druhy za Log.</summary>
        [MenuItem(Q + "Wood cost test")]
        public static void WoodCostTest()
        {
            var inv = InventoryData.Instance;
            var bt = Object.FindFirstObjectByType<Orivilon.Building.BuildingTool>(FindObjectsInactive.Include);
            var piece = Resources.Load<Orivilon.Building.BuildingPieceData>("Building/LogWall");
            if (inv == null || bt == null || piece == null) { Out("ERR woodcost: inventář/BuildingTool/LogWall chybí"); return; }
            var F = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public;
            var has = typeof(Orivilon.Building.BuildingTool).GetMethod("HasResources", F);
            var consume = typeof(Orivilon.Building.BuildingTool).GetMethod("ConsumeResources", F);
            var prev = bt.selectedPiece;
            bt.selectedPiece = piece;
            int cost = piece.costAmounts[0];
            var sb = new StringBuilder("woodcost LogWall ").Append(piece.costItems[0].id).Append('×').Append(cost).Append(':');
            int fails = 0;
            var ids = WoodIdList(); ids.Add("everlost:log");
            foreach (var id in ids)
            {
                ClearInv();
                inv.AddItem(ItemById(id), cost);
                bool h = (bool)has.Invoke(bt, null);
                consume.Invoke(bt, null);
                int left = CountInv(id);
                bool pass = h && left == 0;
                if (!pass) fails++;
                sb.Append("\n  ").Append(pass ? "PASS " : "FAIL ").Append(id).Append(" has=").Append(h).Append(" zbylo ").Append(left);
            }
            // směs 5 druhů → stačí, spotřebuje se vše
            ClearInv();
            string[] mix = { "everlost:oak_log", "everlost:birch_log", "everlost:palm_log", "everlost:log", "everlost:bamboo_stalk" };
            foreach (var id in mix) inv.AddItem(ItemById(id), 1);
            int arc = Orivilon.UI.Arcanum.ArcanumResearch.CountOwned("everlost:log");
            bool hm = (bool)has.Invoke(bt, null);
            consume.Invoke(bt, null);
            int leftMix = 0; foreach (var id in mix) leftMix += CountInv(id);
            bool pm = hm && leftMix == 0 && arc == 5;
            if (!pm) fails++;
            sb.Append("\n  ").Append(pm ? "PASS " : "FAIL ").Append("směs 5 druhů has=").Append(hm).Append(" zbylo ").Append(leftMix).Append(" Arcanum CountOwned(log)=").Append(arc);
            // nedostatek 4 → nestačí, nic se nespotřebuje
            ClearInv();
            for (int i = 0; i < 4; i++) inv.AddItem(ItemById(mix[i]), 1);
            bool h4 = (bool)has.Invoke(bt, null);
            bool p4 = !h4;
            if (!p4) fails++;
            sb.Append("\n  ").Append(p4 ? "PASS " : "FAIL ").Append("4 kusy has=").Append(h4);
            // Arcanum: kámen se za Log nepočítá
            inv.AddItem(ItemById("everlost:stone"), 3);
            int arc2 = Orivilon.UI.Arcanum.ArcanumResearch.CountOwned("everlost:log");
            sb.Append("\n  ").Append(arc2 == 4 ? "PASS " : "FAIL ").Append("Arcanum CountOwned(log) se 4 dřevy + 3 kameny = ").Append(arc2);
            if (arc2 != 4) fails++;
            ClearInv();
            bt.selectedPiece = prev;
            sb.Append("\n  chyb ").Append(fails);
            Out(sb.ToString());
        }

        [MenuItem(Q + "Wood stack test")]
        public static void WoodStackTest()
        {
            var inv = InventoryData.Instance;
            if (inv == null) { Out("ERR stack"); return; }
            ClearInv();
            inv.AddItem(ItemById("everlost:oak_log"), 10);
            inv.AddItem(ItemById("everlost:birch_log"), 3);
            inv.AddItem(ItemById("everlost:oak_log"), 2);
            inv.AddItem(ItemById("everlost:log"), 3);
            Out("stack:" + InvDump());
        }

        /// <summary>Do inventáře 3× každý druh, 3× obecný Log a 5× bobule (před uložením / po načtení porovnat „Count all wood“).</summary>
        [MenuItem(Q + "Give all wood")]
        public static void GiveAllWood()
        {
            var inv = InventoryData.Instance;
            if (inv == null) { Out("ERR give"); return; }
            ClearInv();
            foreach (var id in WoodIdList()) inv.AddItem(ItemById(id), 3);
            inv.AddItem(ItemById("everlost:log"), 3);
            inv.AddItem(ItemById("everlost:berries"), 5);
            inv.AddItem(ItemById("everlost:stone_axe"), 1);
            InventoryActions.Commit();
            Out("give all:" + InvDump());
        }

        [MenuItem(Q + "Count all wood")]
        public static void CountAllWood()
        {
            if (InventoryData.Instance == null) { Out("ERR count"); return; }
            var sb = new StringBuilder("count:");
            foreach (var id in WoodIdList()) sb.Append(' ').Append(id).Append('=').Append(CountInv(id));
            sb.Append(" everlost:log=").Append(CountInv("everlost:log")).Append(" everlost:berries=").Append(CountInv("everlost:berries"));
            Out(sb.ToString());
        }

        /// <summary>Sběr bobulí z keře (PickupItem.PickUp, skutečný keř ze spawnables) a snězení (PlayerItemUse, pravé tlačítko).</summary>
        [MenuItem(Q + "Berry pickup + eat")]
        public static void BerryPickupEat()
        {
            var inv = InventoryData.Instance; var player = GameObject.FindWithTag("Player"); var sp = Orivilon.World.Spawning.ObjectSpawner.Instance;
            if (inv == null || player == null || sp == null) { Out("ERR berry"); return; }
            var sb = new StringBuilder("berry:");
            Orivilon.World.Objects.PickupItem best = null; float bd = 400f * 400f;
            foreach (var p in Object.FindObjectsByType<Orivilon.World.Objects.PickupItem>(FindObjectsSortMode.None))
            {
                if (p.itemData == null || p.itemData.id != "everlost:berries" || p.GetComponent<Orivilon.World.Objects.DroppedItem>() != null) continue;
                float d = (p.transform.position - player.transform.position).sqrMagnitude;
                if (d < bd) { bd = d; best = p; }
            }
            if (best != null) sb.Append(" keř ve světě ").Append(best.name).Append(' ').Append(Mathf.Sqrt(bd).ToString("0")).Append(" m");
            else
            {
                foreach (var e in sp.spawnables)
                    if (e != null && e.prefab != null && e.name.StartsWith("Flower Bush"))
                    {
                        var go = Object.Instantiate(e.prefab, player.transform.position + player.transform.forward * 3f, Quaternion.identity);
                        foreach (var id in go.GetComponentsInChildren<Orivilon.World.Objects.DeterministicObjectId>(true)) Object.DestroyImmediate(id);
                        best = go.GetComponentInChildren<Orivilon.World.Objects.PickupItem>(true);
                        sb.Append(" keř instancován z ").Append(e.name);
                        break;
                    }
            }
            if (best == null) { Out(sb.Append(" ERR keř s bobulemi chybí").ToString()); return; }
            int before = CountInv("everlost:berries");
            best.PickUp();
            int after = CountInv("everlost:berries");
            sb.Append(" | sebráno ").Append(after - before).Append(" (").Append(before).Append("→").Append(after).Append(')');

            Select("everlost:berries");
            var use = Object.FindFirstObjectByType<PlayerItemUse>();
            var needs = use != null ? use.playerNeeds : null;
            if (use == null || needs == null) { Out(sb.Append(" ERR PlayerItemUse").ToString()); return; }
            needs.food = needs.maxFood * .5f;
            float f0 = needs.food; int c0 = CountInv("everlost:berries");
            var mi = typeof(PlayerItemUse).GetMethod("TryUseItem", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            mi.Invoke(use, null);
            sb.Append(" | snědeno: jídlo ").Append(f0.ToString("0")).Append("→").Append(needs.food.ToString("0")).Append(", bobule ").Append(c0).Append("→").Append(CountInv("everlost:berries"));
            Out(sb.ToString());
        }
        // ───────────────────────── sada 3: keř s bobulemi ─────────────────────────

        private static readonly string[] BerryBushPrefabs =
        {
            "Assets/Models/Prefabs/Environment/Foliage/Bushes/Bush_Flower_Short.prefab",
            "Assets/Models/Prefabs/Environment/Foliage/Bushes/Bush_Flower_Tall.prefab",
        };

        /// <summary>Komponenty (květy) meshe – sjednocení trojúhelníků přes vrcholy se stejnou polohou.</summary>
        private static List<List<int>> MeshComponents(Mesh m)
        {
            var v = m.vertices; var t = m.triangles;
            var key = new Dictionary<Vector3Int, int>(); var rep = new int[v.Length];
            for (int i = 0; i < v.Length; i++)
            {
                var k = new Vector3Int(Mathf.RoundToInt(v[i].x * 1e4f), Mathf.RoundToInt(v[i].y * 1e4f), Mathf.RoundToInt(v[i].z * 1e4f));
                if (!key.TryGetValue(k, out int r)) key[k] = r = i;
                rep[i] = r;
            }
            var parent = new int[v.Length]; for (int i = 0; i < v.Length; i++) parent[i] = i;
            int Find(int x) { while (parent[x] != x) { parent[x] = parent[parent[x]]; x = parent[x]; } return x; }
            for (int i = 0; i < t.Length; i += 3)
            {
                int a = Find(rep[t[i]]), b = Find(rep[t[i + 1]]), c = Find(rep[t[i + 2]]);
                parent[b] = a; parent[Find(c)] = a;
            }
            var groups = new Dictionary<int, List<int>>();
            for (int i = 0; i < t.Length; i++)
            {
                int r = Find(rep[t[i]]);
                if (!groups.TryGetValue(r, out var g)) groups[r] = g = new List<int>();
                g.Add(t[i]);
            }
            return new List<List<int>>(groups.Values);
        }

        [MenuItem(M + "Measure berry bush")]
        public static void MeasureBerryBush()
        {
            var sb = new StringBuilder("bush measure\n");
            foreach (var path in BerryBushPrefabs)
            {
                var root = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                sb.Append(path).Append('\n');
                var lg = root.GetComponent<LODGroup>();
                if (lg != null) foreach (var l in lg.GetLODs()) sb.Append("  LOD h=").Append(l.screenRelativeTransitionHeight.ToString("F3")).Append(" r=").Append(string.Join(",", System.Array.ConvertAll(l.renderers, r => r != null ? r.name : "null"))).Append('\n');
                foreach (var mf in root.GetComponentsInChildren<MeshFilter>(true))
                {
                    var m = mf.sharedMesh; var mr = mf.GetComponent<MeshRenderer>();
                    var toRoot = root.transform.worldToLocalMatrix * mf.transform.localToWorldMatrix;
                    var b = new Bounds(toRoot.MultiplyPoint3x4(m.vertices[0]), Vector3.zero);
                    foreach (var x in m.vertices) b.Encapsulate(toRoot.MultiplyPoint3x4(x));
                    sb.Append("  ").Append(mf.name).Append(" mesh=").Append(m.name).Append(" @").Append(AssetDatabase.GetAssetPath(m)).Append(" v").Append(m.vertexCount).Append(" t").Append(m.triangles.Length / 3)
                      .Append(" ls=").Append(mf.transform.lossyScale.ToString("F2")).Append(" lr=").Append(mf.transform.localEulerAngles.ToString("F0"))
                      .Append(" rootBounds ").Append(b.min.ToString("F2")).Append("..").Append(b.max.ToString("F2"))
                      .Append(" mat=").Append(mr.sharedMaterial != null ? mr.sharedMaterial.name : "-").Append('\n');
                    if (mf.name.Contains("Flowers"))
                    {
                        var comps = MeshComponents(m);
                        float smin = 1e9f, smax = 0f, ssum = 0f;
                        foreach (var c in comps)
                        {
                            var cb = new Bounds(toRoot.MultiplyPoint3x4(m.vertices[c[0]]), Vector3.zero);
                            foreach (int i in c) cb.Encapsulate(toRoot.MultiplyPoint3x4(m.vertices[i]));
                            float s = cb.size.magnitude; smin = Mathf.Min(smin, s); smax = Mathf.Max(smax, s); ssum += s;
                        }
                        sb.Append("    komponent ").Append(comps.Count).Append(" velikost ").Append(smin.ToString("F3")).Append("–").Append(smax.ToString("F3")).Append(" průměr ").Append((ssum / Mathf.Max(1, comps.Count)).ToString("F3")).Append('\n');
                    }
                }
            }
            var fm = AssetDatabase.LoadAssetAtPath<Material>("Assets/Models/Materials/Environment/Foliage/Flowers/Bush_Flower.mat");
            foreach (var pn in new[] { "_MainColor", "_Smoothness", "_Translucency", "_ColorVariation", "_Desaturate", "_EnableWind", "_SecureFoliageBase", "_Force", "_AlphaCutoff" })
                if (fm.HasProperty(pn)) sb.Append(pn).Append('=').Append(pn == "_MainColor" ? fm.GetColor(pn).ToString() : fm.GetFloat(pn).ToString("F2")).Append(' ');
            sb.Append(" _Texture=").Append(fm.GetTexture("_Texture") != null ? fm.GetTexture("_Texture").name : "null");
            Out(sb.ToString());
        }
        /// <summary>Oktaedr / ikosaedr bobule pro keř (méně ploch než bobule v ruce – keřů je ve světě hodně).</summary>
        private static void BushBerry(MB mb, Mat m, Vector3 c, float r, bool ico, int seed)
        {
            if (!ico)
            {
                var p = new[] { Vector3.right, Vector3.left, Vector3.up, Vector3.down, Vector3.forward, Vector3.back };
                for (int i = 0; i < 6; i++) p[i] = c + p[i] * r * (1f + .08f * Hash(seed * 7 + i));
                int[] f = { 0, 2, 4, 2, 1, 4, 1, 3, 4, 3, 0, 4, 2, 0, 5, 1, 2, 5, 3, 1, 5, 0, 3, 5 };
                for (int i = 0; i < f.Length; i += 3) mb.Tri(m, p[f[i]], p[f[i + 1]], p[f[i + 2]], c);
                return;
            }
            float t = (1f + Mathf.Sqrt(5f)) / 2f;
            var q = new[]
            {
                new Vector3(-1, t, 0), new Vector3(1, t, 0), new Vector3(-1, -t, 0), new Vector3(1, -t, 0),
                new Vector3(0, -1, t), new Vector3(0, 1, t), new Vector3(0, -1, -t), new Vector3(0, 1, -t),
                new Vector3(t, 0, -1), new Vector3(t, 0, 1), new Vector3(-t, 0, -1), new Vector3(-t, 0, 1),
            };
            for (int i = 0; i < q.Length; i++) q[i] = c + q[i].normalized * r * (1f + .06f * Hash(seed * 11 + i));
            int[] g =
            {
                0,11,5, 0,5,1, 0,1,7, 0,7,10, 0,10,11, 1,5,9, 5,11,4, 11,10,2, 10,7,6, 7,1,8,
                3,9,4, 3,4,2, 3,2,6, 3,6,8, 3,8,9, 4,9,5, 2,4,11, 6,2,10, 8,6,7, 9,8,1,
            };
            for (int i = 0; i < g.Length; i += 3) mb.Tri(m, q[g[i]], q[g[i + 1]], q[g[i + 2]], c);
        }

        /// <summary>Materiál bobulí keře: kopie Bush_Flower.mat (stejný shader UNP/Vegetation a vítr jako dosavadní květy), jen barva bez textury.</summary>
        private static Material BushBerryMaterial(string name, Color c)
        {
            string path = Root + "/Materials/" + name + ".mat";
            var src = AssetDatabase.LoadAssetAtPath<Material>("Assets/Models/Materials/Environment/Foliage/Flowers/Bush_Flower.mat");
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            bool create = mat == null;
            if (create) mat = new Material(src);
            else mat.CopyPropertiesFromMaterial(src);
            mat.name = name;
            mat.SetTexture("_Texture", null);          // výchozí bílá → čistá barva bobule
            mat.SetColor("_MainColor", c);
            mat.SetFloat("_Smoothness", .45f);
            mat.SetFloat("_Translucency", .15f);
            mat.SetFloat("_ColorVariation", .03f);
            mat.SetFloat("_Desaturate", 0f);
            if (create) AssetDatabase.CreateAsset(mat, path); else EditorUtility.SetDirty(mat);
            return mat;
        }

        /// <summary>
        /// Keř s bobulemi (prefaby Flower Bush 01/02 – jediný zdroj itemu Berries): ve všech LOD nahradí dětem
        /// „…_Flowers_LODn“ mesh květů shluky low poly bobulí na stejných místech (16 květů = 16 shluků) a materiál
        /// kopií květového (stejný vítr). Zdrojové květy se čtou vždy z FBX, takže opakované spuštění dá totéž
        /// a reimport FBX bobule nevrátí na květy. Collider, PickupItem (Berries ×5), LODGroup, listy, větve beze změny.
        /// </summary>
        [MenuItem(M + "5 Berry bush (replace flowers)")]
        public static void BuildBerryBush()
        {
            EnsureFolder(Root + "/Bush");
            var mats = new[]
            {
                BushBerryMaterial("HI_BushBerry", Palette[Mat.Berry].c),
                BushBerryMaterial("HI_BushBerryLight", Palette[Mat.BerryLight].c),
            };
            var log = new StringBuilder("berry bush\n");
            foreach (var path in BerryBushPrefabs)
            {
                var root = PrefabUtility.LoadPrefabContents(path);
                try
                {
                    foreach (var mf in root.GetComponentsInChildren<MeshFilter>(true))
                    {
                        if (!mf.name.Contains("_Flowers_LOD")) continue;
                        int lod = mf.name[mf.name.Length - 1] - '0';
                        // zdroj: mesh květů z FBX (podle jména), ne aktuální mesh – kvůli opakovanému spuštění
                        Mesh flower = null;
                        string fbx = path.Contains("Short") ? "Assets/Models/Meshes/Environment/Foliage/Bushes/Bush_Flower_Short.fbx" : "Assets/Models/Meshes/Environment/Foliage/Bushes/Bush_Flower_Tall.fbx";
                        foreach (var o in AssetDatabase.LoadAllAssetsAtPath(fbx)) if (o is Mesh mm && mm.name == mf.name) flower = mm;
                        if (flower == null) { log.Append("ERR zdrojový mesh ").Append(mf.name).Append('\n'); continue; }

                        var toRoot = root.transform.worldToLocalMatrix * mf.transform.localToWorldMatrix;
                        var fromRoot = toRoot.inverse;
                        var v = flower.vertices;
                        var mb = new MB();
                        int n = 0;
                        foreach (var comp in MeshComponents(flower))
                        {
                            Vector3 c = Vector3.zero; foreach (int i in comp) c += toRoot.MultiplyPoint3x4(v[i]); c /= comp.Count;
                            var bb = new Bounds(c, Vector3.zero); foreach (int i in comp) bb.Encapsulate(toRoot.MultiplyPoint3x4(v[i]));
                            float size = Mathf.Clamp(bb.size.magnitude, .18f, .30f);
                            Vector3 outDir = (c - new Vector3(0f, bb.center.y * .6f, 0f)).normalized;
                            Vector3 a = Vector3.Cross(outDir, Vector3.up); if (a.sqrMagnitude < 1e-4f) a = Vector3.right; a.Normalize();
                            Vector3 b = Vector3.Cross(outDir, a);
                            int count = lod == 0 ? 4 : lod == 1 ? 3 : lod == 2 ? 2 : 1;
                            float r = size * (lod == 0 ? .21f : lod == 1 ? .23f : lod == 2 ? .26f : .32f);
                            for (int k = 0; k < count; k++)
                            {
                                Vector3 p;
                                if (count == 1) p = c + outDir * r * .3f;
                                else if (k == count - 1 && count == 4) p = c + outDir * r * .9f;                 // horní bobule vpředu
                                else { float ang = k * Mathf.PI * 2f / Mathf.Min(count, 3) + n; p = c + (a * Mathf.Cos(ang) + b * Mathf.Sin(ang)) * r * 1.05f + outDir * r * .2f; }
                                Mat m = ((n + k) % 4 == 1) ? Mat.BerryLight : Mat.Berry;
                                BushBerry(mb, m, fromRoot.MultiplyPoint3x4(p), r * (1f + .1f * Hash(n * 5 + k)) / mf.transform.lossyScale.x, lod <= 0, n * 13 + k);
                            }
                            n++;
                        }
                        // MB pracuje v prostoru dítěte (body převedené fromRoot); bobule nejsou deformované, měřítko dítěte je 1.
                        var fresh = mb.ToMesh("HI_BerryBush_" + mf.name, out var order);
                        string meshPath = Root + "/Bush/HI_BerryBush_" + mf.name + ".asset";
                        var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(meshPath);
                        if (mesh == null) { AssetDatabase.CreateAsset(fresh, meshPath); mesh = fresh; }
                        else { mesh.Clear(); EditorUtility.CopySerialized(fresh, mesh); mesh.name = "HI_BerryBush_" + mf.name; Object.DestroyImmediate(fresh); EditorUtility.SetDirty(mesh); }
                        mf.sharedMesh = mesh;
                        var rmats = new Material[order.Length];
                        for (int i = 0; i < order.Length; i++) rmats[i] = order[i] == Mat.BerryLight ? mats[1] : mats[0];
                        mf.GetComponent<MeshRenderer>().sharedMaterials = rmats;
                        log.Append(System.IO.Path.GetFileName(path)).Append(' ').Append(mf.name).Append(": květů ").Append(n).Append(" → bobule, ").Append(mesh.vertexCount / 3).Append(" trojúhelníků (dřív ").Append(flower.triangles.Length / 3).Append(")\n");
                    }
                    PrefabUtility.SaveAsPrefabAsset(root, path);
                }
                finally { PrefabUtility.UnloadPrefabContents(root); }
            }
            AssetDatabase.SaveAssets();
            Out(log.ToString());
        }

        /// <summary>Náhled keře (prefab) pro přehled: zblízka a z dálky (LOD0 vs. LOD3) – render jako u ikon.</summary>
        [MenuItem(M + "Render berry bush preview")]
        public static void RenderBushPreview()
        {
            Directory.CreateDirectory(ReportDir);
            foreach (var path in BerryBushPrefabs)
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                var tex = Render(prefab, Quaternion.Euler(-15f, 25f, 0f), 512, 2, .05f, true);
                File.WriteAllBytes(Path.Combine(ReportDir, "bush_" + prefab.name + ".png"), tex.EncodeToPNG());
                Object.DestroyImmediate(tex);
            }
            Out("bush preview → " + ReportDir);
        }

        [MenuItem(Q + "Select stick")] public static void SStick() => Select("everlost:stick");
        [MenuItem(Q + "Select rope")] public static void SRope() => Select("everlost:rope");

        /// <summary>Keře s bobulemi ve světě: počet, mesh a materiály v LOD, aktuální LOD, odměna.</summary>
        [MenuItem(Q + "Berry bush census")]
        public static void BerryBushCensus()
        {
            var player = GameObject.FindWithTag("Player");
            var sb = new StringBuilder("bushcensus:");
            int n = 0, ok = 0; float nearest = 1e9f; Orivilon.World.Objects.PickupItem near = null;
            foreach (var p in Object.FindObjectsByType<Orivilon.World.Objects.PickupItem>(FindObjectsSortMode.None))
            {
                if (p.itemData == null || p.itemData.id != "everlost:berries" || p.GetComponent<Orivilon.World.Objects.DroppedItem>() != null) continue;
                n++;
                bool good = true;
                foreach (var mf in p.GetComponentsInChildren<MeshFilter>(true))
                    if (mf.name.Contains("_Flowers_LOD") && (mf.sharedMesh == null || !mf.sharedMesh.name.StartsWith("HI_BerryBush_"))) good = false;
                if (good) ok++;
                if (player != null) { float d = Vector3.Distance(p.transform.position, player.transform.position); if (d < nearest) { nearest = d; near = p; } }
            }
            sb.Append(" keřů ").Append(n).Append(", s bobulemi ve všech LOD ").Append(ok);
            if (near != null)
            {
                sb.Append(" | nejbližší ").Append(near.name).Append(' ').Append(nearest.ToString("0")).Append(" m, odměna ").Append(near.itemData.id).Append('×').Append(near.itemCount)
                  .Append(", collider ").Append(near.GetComponentInChildren<Collider>() != null).Append(", id ").Append(near.GetComponent<Orivilon.World.Objects.DeterministicObjectId>() != null);
                foreach (var mf in near.GetComponentsInChildren<MeshFilter>(true))
                    if (mf.name.Contains("_Flowers_LOD")) sb.Append("\n  ").Append(mf.name).Append(" → ").Append(mf.sharedMesh.name).Append(" [").Append(string.Join(",", System.Array.ConvertAll(mf.GetComponent<MeshRenderer>().sharedMaterials, m => m.name))).Append("] vidět=").Append(mf.GetComponent<MeshRenderer>().isVisible);
            }
            Out(sb.ToString());
        }

        /// <summary>Postaví hráče ~d m od nejbližšího keře s bobulemi a natočí kameru na něj (pro snímky zblízka / z dálky).</summary>
        private static void FaceBush(float d)
        {
            var player = GameObject.FindWithTag("Player");
            if (player == null) { Out("ERR facebush"); return; }
            Orivilon.World.Objects.PickupItem best = null; float bd = 1e9f;
            foreach (var p in Object.FindObjectsByType<Orivilon.World.Objects.PickupItem>(FindObjectsSortMode.None))
            {
                if (p.itemData == null || p.itemData.id != "everlost:berries" || p.GetComponent<Orivilon.World.Objects.DroppedItem>() != null) continue;
                float dd = (p.transform.position - player.transform.position).sqrMagnitude;
                if (dd < bd) { bd = dd; best = p; }
            }
            if (best == null) { Out("facebush: žádný keř"); return; }
            Vector3 bp = best.transform.position;
            Vector3 dir = player.transform.position - bp; dir.y = 0f; if (dir.sqrMagnitude < 1e-3f) dir = Vector3.forward; dir.Normalize();
            Vector3 to = bp + dir * d;
            Orivilon.World.Generation.VoxelTerrain.instance?.Teleport(to, true);
            Out("facebush " + best.name + " → " + d + " m, keř " + bp.ToString("F1"));
            lastBush = best;
        }
        private static Orivilon.World.Objects.PickupItem lastBush;
        [MenuItem(Q + "Bush goto 9 m")] public static void Bush9() => FaceBush(9f);
        [MenuItem(Q + "Bush goto 70 m")] public static void Bush70() => FaceBush(70f);

        /// <summary>Natočí hráče a kameru na posledně vybraný keř (po teleportu).</summary>
        [MenuItem(Q + "Bush look")]
        public static void BushLook()
        {
            var player = GameObject.FindWithTag("Player");
            if (player == null || lastBush == null) { Out("ERR bushlook"); return; }
            var fpc = Object.FindFirstObjectByType<Orivilon.Player.FirstPersonController>();
            Vector3 target = lastBush.transform.position + Vector3.up * lastBush.transform.lossyScale.y * .8f;
            Vector3 eye = fpc != null && fpc.playerCamera != null ? fpc.playerCamera.transform.position : player.transform.position;
            Vector3 dir = (target - eye).normalized;
            float yaw = Mathf.Atan2(dir.x, dir.z) * Mathf.Rad2Deg, pitch = -Mathf.Asin(dir.y) * Mathf.Rad2Deg;
            if (fpc != null) fpc.SetLook(yaw, pitch);
            Out("bushlook " + lastBush.name + " dist " + Vector3.Distance(player.transform.position, lastBush.transform.position).ToString("0.0") + " m, scale " + lastBush.transform.lossyScale.y.ToString("F1"));
        }

        /// <summary>Sebere posledně vybraný keř skutečnou cestou PickupItem.PickUp a vypíše odměnu.</summary>
        [MenuItem(Q + "Bush pick")]
        public static void BushPick()
        {
            if (lastBush == null || InventoryData.Instance == null) { Out("ERR bushpick"); return; }
            int before = CountInv("everlost:berries");
            var id = lastBush.GetComponent<Orivilon.World.Objects.DeterministicObjectId>();
            string key = id != null && id.HasKey ? id.Hash.ToString() : "-";
            lastBush.PickUp();
            Out("bushpick hash " + key + " berries " + before + "→" + CountInv("everlost:berries") + ", destroyed " + (id != null && Orivilon.SaveSystem.SaveSystem.IsObjectDestroyed(id.Hash)));
        }
    }
}
