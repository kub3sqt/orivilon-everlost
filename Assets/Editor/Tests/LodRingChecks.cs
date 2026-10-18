using System;
using System.Collections.Generic;
using System.Reflection;
using Orivilon.World.Generation;
using Unity.Mathematics;
using UnityEditor;
using UnityEngine;

namespace Orivilon.EditorTools
{
    /// <summary>
    /// Výkonové kolo 14.10.2026: kontrola prstenců LOD bez scény. Pro detailní dosah 1–16,
    /// lodRing 2–8 a řadu poloh hráče ověří, že každé místo uvnitř nejhrubšího prstence kryje
    /// PRÁVĚ JEDNA žádaná úroveň (0 = díra, 2 = překryv). Před opravou selhávalo pro dosah ≥ 8.
    /// </summary>
    internal static class LodRingChecks
    {
        private static int LegacyFirstBad(int ring, float span0)
        {
            for (int rd = 1; rd <= 16; rd++) if (LegacyHasOverlap(5, ring, rd, span0)) return rd;
            return -1;
        }

        /// <summary>Původní UpdateDesired: prstenec každé úrovně jen podle vlastního poloměru.</summary>
        private static bool LegacyHasOverlap(int levels, int ring, int rd, float span0)
        {
            for (int px = 0; px < 64; px += 7)
            {
                var desired = new HashSet<int3>();
                var lo = new int2[levels]; var hi = new int2[levels];
                Vector2 p = new Vector2(px * 5.3f, px * 3.1f);
                for (int l = 0; l < levels; l++)
                {
                    float span = span0 * (1 << l);
                    int cx = Mathf.FloorToInt(p.x / span), cz = Mathf.FloorToInt(p.y / span);
                    int r = l == 0 ? Mathf.Max(1, rd) : Mathf.Max(2, ring);
                    lo[l] = new int2(((cx - r) >> 1) << 1, ((cz - r) >> 1) << 1);
                    hi[l] = new int2((((cx + r) >> 1) << 1) + 1, (((cz + r) >> 1) << 1) + 1);
                }
                for (int l = 0; l < levels; l++)
                {
                    int2 hlo = l > 0 ? lo[l - 1] / 2 : default, hhi = l > 0 ? (hi[l - 1] - 1) / 2 : default;
                    for (int z = lo[l].y; z <= hi[l].y; z++)
                    for (int x = lo[l].x; x <= hi[l].x; x++)
                    {
                        if (l > 0 && x >= hlo.x && x <= hhi.x && z >= hlo.y && z <= hhi.y) continue;
                        desired.Add(new int3(x, z, l));
                    }
                }
                foreach (int3 k in desired)
                    for (int m = k.z + 1; m < levels; m++)
                        if (desired.Contains(new int3(k.x >> (m - k.z), k.y >> (m - k.z), m))) return true;
            }
            return false;
        }

        [MenuItem("Tools/Everlost/Validate LOD rings")]
        internal static void Run()
        {
            const BindingFlags F = BindingFlags.Instance | BindingFlags.NonPublic;
            var go = new GameObject("LOD ring check") { hideFlags = HideFlags.HideAndDontSave };
            go.SetActive(false);
            var viewer = new GameObject("Viewer") { hideFlags = HideFlags.HideAndDontSave };
            try
            {
                var t = go.AddComponent<VoxelTerrain>();
                t.viewer = viewer.transform;
                t.voxelSize = 1f;
                FieldInfo fBuilders = typeof(VoxelTerrain).GetField("builders", F);
                FieldInfo fDirty = typeof(VoxelTerrain).GetField("desiredDirty", F);
                var desired = (HashSet<int3>)typeof(VoxelTerrain).GetField("desired", F).GetValue(t);
                MethodInfo update = typeof(VoxelTerrain).GetMethod("UpdateDesired", F);
                float span0 = t.ChunkSpan;
                var rng = new System.Random(1337);
                int cases = 0, checkedPts = 0;
                for (int levels = 3; levels <= 5; levels++)
                for (int ring = 2; ring <= 8; ring++)
                for (int rd = 1; rd <= 16; rd++)
                for (int rep = 0; rep < 4; rep++)
                {
                    fBuilders.SetValue(t, Array.CreateInstance(typeof(VoxelChunkBuilder), levels));
                    t.lodRing = ring;
                    t.renderDistance = rd;
                    viewer.transform.position = new Vector3((float)(rng.NextDouble() * 4000 - 2000), 0, (float)(rng.NextDouble() * 4000 - 2000));
                    fDirty.SetValue(t, true);
                    update.Invoke(t, null);
                    cases++;
                    // hranice nejhrubšího žádaného prstence
                    float top = span0 * (1 << (levels - 1));
                    float minX = float.MaxValue, maxX = float.MinValue, minZ = float.MaxValue, maxZ = float.MinValue;
                    foreach (int3 k in desired)
                    {
                        if (k.z != levels - 1) continue;
                        minX = Mathf.Min(minX, k.x * top); maxX = Mathf.Max(maxX, (k.x + 1) * top);
                        minZ = Mathf.Min(minZ, k.y * top); maxZ = Mathf.Max(maxZ, (k.y + 1) * top);
                    }
                    float step = span0 * 0.5f;
                    for (float z = minZ + step * 0.5f; z < maxZ; z += step * 3f)
                    for (float x = minX + step * 0.5f; x < maxX; x += step * 3f)
                    {
                        int cover = 0;
                        for (int l = 0; l < levels; l++)
                        {
                            float s = span0 * (1 << l);
                            if (desired.Contains(new int3(Mathf.FloorToInt(x / s), Mathf.FloorToInt(z / s), l))) cover++;
                        }
                        checkedPts++;
                        if (cover != 1)
                            throw new Exception($"levels {levels} lodRing {ring} detail {rd} @({x:0},{z:0}): pokrytí {cover} (0 = díra, 2+ = překryv)");
                    }
                }
                // Pro srovnání původní výpočet prstenců (bez rozšíření hrubšího prstence).
                int legacyBad = 0, legacyMinRd = int.MaxValue;
                for (int ring = 2; ring <= 8; ring++)
                for (int rd = 1; rd <= 16; rd++)
                    if (LegacyHasOverlap(5, ring, rd, span0)) { legacyBad++; legacyMinRd = Math.Min(legacyMinRd, rd); }
                string ok = $"PASS: {cases} kombinací, {checkedPts} bodů – každé místo kryje právě jedna úroveň LOD. " +
                            $"Původní výpočet (5 úrovní) by selhal u {legacyBad} ze 112 kombinací lodRing×dosah" +
                            (legacyBad > 0 ? $" (při lodRing 4 od dosahu {LegacyFirstBad(4, span0)})." : ".");
                Debug.Log("[LodRingChecks] " + ok);
                TestReport.Write("LodRingChecks", ok);
            }
            catch (Exception ex)
            {
                Debug.LogError("[LodRingChecks] FAIL: " + (ex.InnerException ?? ex).Message);
                TestReport.Write("LodRingChecks", "FAIL: " + (ex.InnerException ?? ex).Message);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(go);
                UnityEngine.Object.DestroyImmediate(viewer);
            }
        }
    }

    internal static class TestReport
    {
        /// <summary>Výsledek testu do _reports/tests/&lt;jméno&gt;.txt (čitelné i mimo Unity, např. pro vzdálené ověření).</summary>
        internal static void Write(string name, string text)
        {
            try
            {
                string dir = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(UnityEngine.Application.dataPath), "_reports", "tests");
                System.IO.Directory.CreateDirectory(dir);
                System.IO.File.WriteAllText(System.IO.Path.Combine(dir, name + ".txt"),
                    System.DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss ") + text + "\n");
            }
            catch (System.Exception) { }
        }
    }
}
