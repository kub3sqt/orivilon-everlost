using System;
using System.Collections;
using System.IO;
using System.Reflection;
using Orivilon.World.Generation;
using Unity.Mathematics;
using UnityEditor;
using UnityEngine;

[InitializeOnLoad]
internal static class PhysicsStreamingChecks
{
    private const string Request = "/tmp/everlost-physics-check.request";
    static PhysicsStreamingChecks()
    {
        if (File.Exists(Request)) EditorApplication.update += RunWhenReady;
    }
    private static void RunWhenReady()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling) return;
        EditorApplication.update -= RunWhenReady;
        Run();
    }
    [MenuItem("Tools/Everlost/Validate collider streaming")]
    internal static void Run()
    {
        if (File.Exists(Request)) File.Delete(Request);
        if (EditorApplication.isPlaying) throw new Exception("Run outside Play mode.");
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        var root = new GameObject("Collider streaming check") { hideFlags = HideFlags.HideAndDontSave };
        root.SetActive(false);
        var viewer = new GameObject("Viewer") { hideFlags = HideFlags.HideAndDontSave };
        viewer.transform.SetParent(root.transform);
        try
        {
            var terrain = root.AddComponent<VoxelTerrain>();
            terrain.viewer = viewer.transform;
            terrain.voxelSize = 1;
            terrain.colliderDistance = 2;
            typeof(VoxelTerrain).GetField("lastViewerColumn", flags).SetValue(terrain, new int2(0,0));
            typeof(VoxelTerrain).GetField("chunkRoot", flags).SetValue(terrain, root.transform);
            var chunks = (IDictionary)typeof(VoxelTerrain).GetField("chunks", flags).GetValue(terrain);
            var create = typeof(VoxelTerrain).GetMethod("CreateChunkObject", flags);
            var update = typeof(VoxelTerrain).GetMethod("UpdateColliders", flags);
            object[] entries = new object[7];
            int4[] keys = { new int4(0,0,0,0), new int4(1,0,0,0), new int4(-1,0,0,0),
                new int4(0,1,0,0), new int4(0,-1,0,0), new int4(0,0,1,0), new int4(0,0,-1,0) };
            for (int i = 0; i < keys.Length; i++)
            {
                object entry = create.Invoke(terrain, null); entries[i] = entry;
                Type type = entry.GetType();
                var mesh = (Mesh)type.GetField("colliderMesh").GetValue(entry);
                mesh.vertices = new[] { Vector3.zero, new Vector3(32,0,0), new Vector3(0,0,32) };
                mesh.triangles = new[] { 0,2,1 };
                chunks.Add(keys[i], entry);
            }
            viewer.transform.position = new Vector3(31,16,16);
            update.Invoke(terrain, null);
            Require(Assigned(entries[0]) && Assigned(entries[1]), "Nearby ground must activate immediately.");
            int active = 0;
            foreach (object entry in entries) if (Assigned(entry)) active++;
            Require(active == 2, "Far activation must honor shared count budget.");
            for (int i = 0; i < 5; i++) update.Invoke(terrain, null);
            foreach (object entry in entries) Require(Assigned(entry), "Queued collider was starved.");
            var first = (MeshCollider)entries[0].GetType().GetField("meshCollider").GetValue(entries[0]);
            var meshBefore = first.sharedMesh;
            typeof(VoxelTerrain).GetField("lastViewerColumn", flags).SetValue(terrain, new int2(3,0));
            viewer.transform.position = new Vector3(100,16,16);
            update.Invoke(terrain, null);
            Require(Assigned(entries[0]), "Hysteresis must retain the boundary collider.");
            typeof(VoxelTerrain).GetField("lastViewerColumn", flags).SetValue(terrain, new int2(4,0));
            viewer.transform.position = new Vector3(132,16,16);
            update.Invoke(terrain, null);
            Require(!Assigned(entries[0]) && !first.enabled, "Distant collider must disable.");
            Require(first.sharedMesh == meshBefore, "Disabling must retain the cooked mesh reference.");
            typeof(VoxelTerrain).GetField("lastViewerColumn", flags).SetValue(terrain, new int2(0,0));
            viewer.transform.position = new Vector3(16,16,16);
            update.Invoke(terrain, null);
            Require(Assigned(entries[0]) && first.enabled && first.sharedMesh == meshBefore, "Re-entry must reuse the collider.");
            File.WriteAllText("/tmp/everlost-physics-check.result", "PASS: immediate nearby ground, bounded remote activation, queue completion, hysteresis, disabling and mesh reuse.");
            Debug.Log("[PhysicsStreamingChecks] PASS: immediate nearby ground, bounded remote activation, queue completion, hysteresis, disabling and mesh reuse.");
            Orivilon.EditorTools.TestReport.Write("PhysicsStreamingChecks", "PASS: immediate nearby ground, bounded remote activation, queue completion, hysteresis, disabling and mesh reuse.");
        }
        catch (Exception ex) { File.WriteAllText("/tmp/everlost-physics-check.result", "FAIL: " + ex); Orivilon.EditorTools.TestReport.Write("PhysicsStreamingChecks", "FAIL: " + ex); Debug.LogException(ex); }
        finally { UnityEngine.Object.DestroyImmediate(root); }
    }
    private static bool Assigned(object entry) => (bool)entry.GetType().GetField("colliderAssigned").GetValue(entry);
    private static void Require(bool condition, string message) { if (!condition) throw new Exception(message); }
}
