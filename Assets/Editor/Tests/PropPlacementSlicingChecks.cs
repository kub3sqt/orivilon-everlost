using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Orivilon.World.Generation;
using Orivilon.World.Spawning;
using Unity.Collections;
using Unity.Mathematics;
using UnityEditor;
using UnityEngine;

namespace Orivilon.EditorTools
{
    // Runs without a scene, player, or save. The temporary flag is only for local verification.
    [InitializeOnLoad]
    internal static class PropPlacementSlicingChecks
    {
        private const string Trigger = "/tmp/everlost-placement-check.request";
        private const string Result = "/tmp/everlost-placement-check.result";
        static PropPlacementSlicingChecks()
        {
            if (File.Exists(Trigger)) EditorApplication.delayCall += Run;
        }

        [MenuItem("Tools/Everlost/Validate placement slicing")]
        internal static void Run()
        {
            if (EditorApplication.isPlaying) throw new InvalidOperationException("Run outside Play mode.");
            if (File.Exists(Trigger)) File.Delete(Trigger);
            var holder = new GameObject("Placement slicing validation");
            holder.SetActive(false);
            ColumnField a = default, b = default;
            var mesh = new Mesh();
            try
            {
                var prefab = Resources.Load<GameObject>("Prefabs/SpawnableObjects");
                var go = UnityEngine.Object.Instantiate(prefab, holder.transform);
                var sp = go.GetComponent<ObjectSpawner>();
                sp.EnsureBiomeProps();
                a = MakeField(0, 20f);
                b = MakeField(32, 28f);
                int cases = 0, slices = 0;
                double maxSlice = 0;
                foreach (int seed in new[] { 42, 1337, -1867725313 })
                {
                    Raster(mesh, 0, 20f);
                    var expected = new List<EcoPlacement>(EcologyPlacer.Build(sp, a, 16, 1f, 0f,
                        new int2(0, 0), TerrainPalette.Default, default, seed, null));
                    if (expected.Count == 0) throw new Exception("Fixture must produce placements.");
                    Raster(mesh, 0, 20f);
                    using (var session = EcologyPlacer.BeginBuild(sp, a, 16, 1f, 0f, new int2(0, 0),
                        TerrainPalette.Default, default, seed, null))
                    {
                        int count = 0;
                        while (!session.Step(0.2))
                        {
                            if (++count > 20000) throw new Exception("Calculation did not finish.");
                            // Other columns overwrite every shared scratch array and the surface raster.
                            Raster(mesh, 32, 28f);
                            EcologyPlacer.BuildTreeProxies(sp, b, 16, 1f, 0f,
                                TerrainPalette.Default, default, seed + 1, 32f);
                            if (count == 1)
                                EcologyPlacer.Build(sp, b, 16, 1f, 0f, new int2(1, 0),
                                    TerrainPalette.Default, default, seed + 1, null);
                        }
                        Equal(expected, session.Placements);
                        slices += count + 1;
                        maxSlice = Math.Max(maxSlice, session.MaxSliceMs);
                    }
                    // Cancellation must not dispose the field or corrupt a later reused session.
                    Raster(mesh, 0, 20f);
                    var cancelled = EcologyPlacer.BeginBuild(sp, a, 16, 1f, 0f, new int2(0, 0),
                        TerrainPalette.Default, default, seed, null);
                    cancelled.Step(0.01);
                    cancelled.Dispose();
                    cancelled.Dispose();
                    if (!a.IsCreated || a.surfY[0] != 20f) throw new Exception("Cancellation damaged field.");
                    Raster(mesh, 0, 20f);
                    using (var reused = EcologyPlacer.BeginBuild(sp, a, 16, 1f, 0f, new int2(0, 0),
                        TerrainPalette.Default, default, seed, null))
                    {
                        while (!reused.Step(2.0)) { }
                        Equal(expected, reused.Placements);
                        maxSlice = Math.Max(maxSlice, reused.MaxSliceMs);
                    }
                    cases++;
                }
                string report = $"PASS: {cases} seeds, {slices} slices; exact placements with interleaved proxy/synchronous builds, raster replacement, cancellation and pool reuse. Max measured slice {maxSlice:F2} ms.";
                File.WriteAllText(Result, report);
                TestReport.Write("PropPlacementSlicingChecks", report);
                Debug.Log(report);
            }
            catch (Exception ex)
            {
                File.WriteAllText(Result, "FAIL: " + ex);
                TestReport.Write("PropPlacementSlicingChecks", "FAIL: " + ex);
                Debug.LogException(ex);
            }
            finally
            {
                a.Dispose(); b.Dispose();
                UnityEngine.Object.DestroyImmediate(mesh);
                UnityEngine.Object.DestroyImmediate(holder);
                // Leave no raster pointing at the destroyed fixture.
                SurfaceRaster.Build(new List<Mesh>(), new List<Matrix4x4>(), 0, 0, 32, 1);
            }
        }

        private static ColumnField MakeField(float x, float height)
        {
            object boxed = new ColumnField { origin = new float2(x - 16, -16), step = 1f, side = 65 };
            foreach (FieldInfo field in typeof(ColumnField).GetFields())
            {
                if (field.FieldType != typeof(NativeArray<float>)) continue;
                var array = new NativeArray<float>(65 * 65, Allocator.Persistent);
                float value = field.Name == "surfY" || field.Name == "surfRaw" || field.Name == "macroY" ? height
                    : field.Name == "temp" || field.Name == "hum" ? 0.5f
                    : field.Name == "lakeY" ? ColumnField.NoLake
                    : field.Name == "shoreY" ? height - 1000 : 0;
                for (int i = 0; i < array.Length; i++) array[i] = value;
                field.SetValue(boxed, array);
            }
            return (ColumnField)boxed;
        }

        private static void Raster(Mesh mesh, float x, float height)
        {
            mesh.vertices = new[] { new Vector3(x, height, 0), new Vector3(x + 32, height, 0),
                new Vector3(x, height, 32), new Vector3(x + 32, height, 32) };
            mesh.triangles = new[] { 0, 2, 1, 1, 2, 3 };
            SurfaceRaster.Build(new List<Mesh> { mesh }, new List<Matrix4x4> { Matrix4x4.identity }, x, 0, 32, 1);
        }

        private static void Equal(List<EcoPlacement> expected, List<EcoPlacement> actual)
        {
            if (expected.Count != actual.Count) throw new Exception($"Count differs: {expected.Count} vs {actual.Count}");
            for (int i = 0; i < expected.Count; i++)
                if (!expected[i].Equals(actual[i])) throw new Exception("Placement differs at " + i);
        }
    }
}
