using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Orivilon.World.Generation;

namespace Orivilon.EditorTools
{
    [InitializeOnLoad]
    internal static class ValleyMistSetup
    {
        private const string RendererPath = "Assets/Settings/URP/URP-Renderer.asset";
        private const string MaterialPath = "Assets/Settings/URP/ValleyMist.mat";

        static ValleyMistSetup()
        {
            EditorApplication.playModeStateChanged += state =>
            {
                if (state == PlayModeStateChange.ExitingPlayMode) SetPreview(true);
            };
        }

        [MenuItem("Tools/Everlost/Rendering/Mist preview on")]
        private static void PreviewOn() => SetPreview(true);

        [MenuItem("Tools/Everlost/Rendering/Mist preview off")]
        private static void PreviewOff() => SetPreview(false);

        private static void SetPreview(bool enabled)
        {
            Orivilon.World.Biomes.BiomeAtmosphere.ValleyMistEnabled = enabled;
            var renderer = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(RendererPath);
            if (renderer == null) return;
            foreach (var feature in renderer.rendererFeatures)
                if (feature != null && feature.name == "Valley Mist") feature.SetActive(enabled);
        }

        [MenuItem("Tools/Everlost/Rendering/Install valley mist")]
        private static void Install()
        {
            if (EditorApplication.isPlaying) throw new System.InvalidOperationException("Stop Play before installing mist.");
            var data = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(RendererPath);
            var shader = Shader.Find("Everlost/ValleyMist");
            if (data == null || shader == null) throw new System.InvalidOperationException("Missing renderer or mist shader.");
            var material = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
            if (material == null)
            {
                material = new Material(shader) { name = "ValleyMist" };
                AssetDatabase.CreateAsset(material, MaterialPath);
            }
            FullScreenPassRendererFeature feature = null;
            foreach (var item in data.rendererFeatures)
                if (item is FullScreenPassRendererFeature pass && pass.name == "Valley Mist") feature = pass;
            if (feature == null)
            {
                feature = ScriptableObject.CreateInstance<FullScreenPassRendererFeature>();
                feature.name = "Valley Mist";
                Undo.RegisterCreatedObjectUndo(feature, "Add valley mist");
                AssetDatabase.AddObjectToAsset(feature, data);
                AssetDatabase.TryGetGUIDAndLocalFileIdentifier(feature, out string guid, out long id);
                var serialized = new SerializedObject(data);
                var features = serialized.FindProperty("m_RendererFeatures");
                var map = serialized.FindProperty("m_RendererFeatureMap");
                features.arraySize++;
                features.GetArrayElementAtIndex(features.arraySize - 1).objectReferenceValue = feature;
                map.arraySize++;
                map.GetArrayElementAtIndex(map.arraySize - 1).longValue = id;
                serialized.ApplyModifiedProperties();
            }
            Undo.RecordObject(feature, "Configure valley mist");
            feature.passMaterial = material;
            feature.injectionPoint = FullScreenPassRendererFeature.InjectionPoint.BeforeRenderingTransparents;
            feature.requirements = ScriptableRenderPassInput.Depth;
            feature.fetchColorBuffer = false;
            feature.bindDepthStencilAttachment = false;
            feature.SetActive(true);
            feature.Create();
            data.SetDirty();
            EditorUtility.SetDirty(feature);
            EditorUtility.SetDirty(data);
            AssetDatabase.SaveAssetIfDirty(data);
            AssetDatabase.SaveAssetIfDirty(material);
            Report();
        }

        [MenuItem("Tools/Everlost/Rendering/Sample valley landscape")]
        private static void SampleLandscape()
        {
            var terrain = VoxelTerrain.instance;
            if (terrain == null) return;
            var text = new System.Text.StringBuilder();
            for (int z = -800; z <= 800; z += 200)
            {
                for (int x = -2000; x <= -400; x += 200)
                    text.Append(x).Append(',').Append(z).Append(':').Append(terrain.SurfaceHeight(x, z).ToString("F0")).Append(' ');
                text.AppendLine();
            }
            Directory.CreateDirectory("_reports/tests");
            File.WriteAllText("_reports/tests/ValleyLandscape.txt", text.ToString());
        }

        [MenuItem("Tools/Everlost/Rendering/Check valley mist")]
        private static void Report()
        {
            var shader = Shader.Find("Everlost/ValleyMist");
            var renderer = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(RendererPath);
            var report = new System.Text.StringBuilder();
            report.AppendLine("Valley mist: depth only, hardware alpha blend, before transparents; no color copy or ray marching.");
            report.AppendLine("Playing: " + EditorApplication.isPlaying);
            report.AppendLine("Parameters: " + Shader.GetGlobalVector("_EverlostValleyMist").ToString("F5"));
            report.AppendLine("Camera: " + (Camera.main == null ? "none" : Camera.main.transform.position.ToString()));
            foreach (var item in renderer.rendererFeatures)
                if (item != null) report.AppendLine(item.name + ": " + item.isActive);
            foreach (string shaderName in new[] { "Everlost/ValleyMist", "Everlost/LowPolyWater" })
            {
                var checkedShader = Shader.Find(shaderName);
                if (checkedShader == null) { report.AppendLine("MISSING " + shaderName); continue; }
                var messages = ShaderUtil.GetShaderMessages(checkedShader);
                report.AppendLine(shaderName + ": " + messages.Length + " shader messages");
                foreach (var message in messages) report.AppendLine(message.severity + ": " + message.message);
            }
            Directory.CreateDirectory("_reports/tests");
            File.WriteAllText("_reports/tests/ValleyMist.txt", report.ToString());
            Debug.Log(report.ToString());
        }
    }
}
