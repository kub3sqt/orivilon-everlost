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
        [MenuItem("Tools/Everlost/Validate long view")]
        internal static void RunLongView()
        {
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
            var root = new GameObject("Long view check") { hideFlags = HideFlags.HideAndDontSave };
            root.SetActive(false);
            var previous = VoxelTerrain.instance;
            float fadeEnd = Orivilon.World.Spawning.ObjectSpawner.TreeFadeEnd;
            try
            {
                var sun = root.AddComponent<Orivilon.SunRotation>();
                var resolve = typeof(Orivilon.SunRotation).GetMethod("ResolveBaseFogDistances", flags);
                VoxelTerrain.instance = null;
                object[] args = { 0f, 0f };
                resolve.Invoke(sun, args); // Slunce vzniklo ještě před terénem.
                var terrain = root.AddComponent<VoxelTerrain>();
                terrain.voxelSize = 1f;
                terrain.lodRing = 4;
                terrain.lodCount = 5;
                VoxelTerrain.instance = terrain;
                resolve.Invoke(sun, args);
                if (Mathf.Abs((float)args[1] - terrain.ViewDistance * 0.88f) > 0.01f)
                    throw new Exception("Mlha se po pozdním vzniku terénu nenavázala na dohled.");
                Orivilon.World.Spawning.ObjectSpawner.TreeFadeEnd = 1000f;
                for (int levels = 1; levels <= 5; levels++)
                for (int ring = 2; ring <= 8; ring++)
                {
                    terrain.lodCount = levels;
                    terrain.lodRing = ring;
                    int lod = Mathf.Min(levels - 1, Orivilon.World.Spawning.TreeProxies.MaxLod);
                    float reach = terrain.ChunkSpan * (lod == 0 ? terrain.renderDistance : (1 << lod) * ring);
                    float end = Orivilon.World.Spawning.ObjectSpawner.EffectiveTreeFadeEnd;
                    if (end <= 0f || end >= reach || end > 1000f)
                        throw new Exception("Stromy přesahují pokrytí při LOD " + levels + ", ring " + ring);
                }
                TestReport.Write("LongViewChecks", "PASS: pozdní terén naváže mlhu; 35 nastavení skryje stromy před hranicí pokrytí.");
                Debug.Log("[LongViewChecks] PASS");
            }
            catch (Exception ex)
            {
                TestReport.Write("LongViewChecks", "FAIL: " + (ex.InnerException ?? ex).Message);
                Debug.LogError("[LongViewChecks] FAIL: " + (ex.InnerException ?? ex).Message);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
                VoxelTerrain.instance = previous;
                Orivilon.World.Spawning.ObjectSpawner.TreeFadeEnd = fadeEnd;
            }
        }

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

    // Izolace časových příčin stínového shimmeru v testovacím Play světě.
    [InitializeOnLoad]
    internal static class ShadowMotionChecks
    {
        private static Orivilon.SunRotation sun;
        private static Orivilon.Player.FirstPersonController player;
        private static Rigidbody body;
        private static bool prepared, pausedBefore, playerEnabledBefore, kinematicBefore;
        private static readonly int WindClock = Shader.PropertyToID("_FoliageWindClock");

        static ShadowMotionChecks()
        {
            EditorApplication.playModeStateChanged += state =>
            {
                if (state == PlayModeStateChange.ExitingPlayMode) Restore();
            };
        }

        private static void Apply(bool freezeSun, bool freezeWind)
        {
            if (!Application.isPlaying) throw new InvalidOperationException("Shadow QA vyžaduje Play v testovacím světě.");
            if (!prepared)
            {
                sun = UnityEngine.Object.FindFirstObjectByType<Orivilon.SunRotation>();
                player = UnityEngine.Object.FindFirstObjectByType<Orivilon.Player.FirstPersonController>();
                if (sun == null || player == null) throw new InvalidOperationException("Chybí slunce nebo hráč.");
                pausedBefore = sun.paused;
                playerEnabledBefore = player.enabled;
                body = player.rb;
                if (body != null) { kinematicBefore = body.isKinematic; body.isKinematic = true; }
                player.enabled = false;
                prepared = true;
            }
            sun.paused = freezeSun;
            Shader.SetGlobalVector(WindClock, freezeWind ? new Vector4(1f, Time.time, 0f, 0f) : Vector4.zero);
        }

        // Cílená úprava aktivního URP assetu přes Editor, bez přepisu YAML.
        [MenuItem("Tools/Everlost/Rendering/Use detailed soft shadows")]
        private static void ApplyDetailedSoftShadows()
        {
            if (Application.isPlaying) throw new InvalidOperationException("Nastavení stínů se ukládá pouze mimo Play.");
            // Editor může mít jinou kvalitu než profil načítaný SettingsManagerem ve hře.
            string settingsPath = System.IO.Path.Combine(Application.persistentDataPath, "settings.json");
            var settings = System.IO.File.Exists(settingsPath)
                ? JsonUtility.FromJson<Orivilon.UI.Settings.SettingsData>(System.IO.File.ReadAllText(settingsPath))
                : new Orivilon.UI.Settings.SettingsData();
            int level = Mathf.Clamp(settings.qualityLevel, 0, QualitySettings.names.Length - 1);
            var pipeline = QualitySettings.GetRenderPipelineAssetAt(level) as UnityEngine.Rendering.Universal.UniversalRenderPipelineAsset;
            if (pipeline == null) throw new InvalidOperationException("Herní profil nemá URP asset.");
            var serialized = new SerializedObject(pipeline);
            serialized.Update();
            var quality = serialized.FindProperty("m_SoftShadowQuality");
            int high = Array.IndexOf(quality.enumNames, "High");
            if (high < 0) throw new InvalidOperationException("URP nenabízí kvalitu měkkých stínů High.");
            Undo.RecordObject(pipeline, "Detailní měkké stíny vegetace");
            serialized.FindProperty("m_ShadowCascadeCount").intValue = 4;
            serialized.FindProperty("m_Cascade4Split").vector3Value = new Vector3(0.05f, 0.20f, 0.50f);
            quality.enumValueIndex = high;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(pipeline);
            AssetDatabase.SaveAssetIfDirty(pipeline);
            string report = AssetDatabase.GetAssetPath(pipeline) + ": cascades 4, splits 0.05/0.20/0.50, soft High, resolution "
                + pipeline.mainLightShadowmapResolution + ", distance " + pipeline.shadowDistance;
            TestReport.Write("ShadowQualitySettings", report);
            Debug.Log("[ShadowQualitySettings] " + report);
        }

        [MenuItem("Tools/Everlost/Shadow QA/Normal")]
        private static void Normal() => Apply(false, false);
        [MenuItem("Tools/Everlost/Shadow QA/Freeze sun")]
        private static void FreezeSun() => Apply(true, false);
        [MenuItem("Tools/Everlost/Shadow QA/Freeze wind")]
        private static void FreezeWind() => Apply(false, true);
        [MenuItem("Tools/Everlost/Shadow QA/Freeze both")]
        private static void FreezeBoth() => Apply(true, true);
        [MenuItem("Tools/Everlost/Shadow QA/Restore")]
        private static void Restore()
        {
            Shader.SetGlobalVector(WindClock, Vector4.zero);
            if (!prepared) return;
            if (sun != null) sun.paused = pausedBefore;
            if (body != null) body.isKinematic = kinematicBefore;
            if (player != null) player.enabled = playerEnabledBefore;
            prepared = false;
        }
    }

}
