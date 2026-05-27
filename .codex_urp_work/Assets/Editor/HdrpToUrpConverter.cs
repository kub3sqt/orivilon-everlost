using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEditor.Rendering;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

namespace Everlost.Editor
{
    public static class HdrpToUrpConverter
    {
        private const string SettingsFolder = "Assets/Settings/URP";
        private const string WaterMaterialPath = "Assets/Models/Materials/Environment/Water/Water.mat";
        private const string RequestPath = "Temp/HdrpToUrpConversion.request";
        private const string LogPath = "Logs/HdrpToUrpConversion.log";

        [InitializeOnLoadMethod]
        private static void RunWhenRequested()
        {
            if (!File.Exists(RequestPath))
                return;

            File.Delete(RequestPath);
            EditorApplication.delayCall += () =>
            {
                try
                {
                    Run();
                    WriteLog("HDRP to URP conversion completed.");
                }
                catch (Exception exception)
                {
                    WriteLog(exception.ToString());
                    throw;
                }
            };
        }

        public static void Run()
        {
            PlayerSettings.colorSpace = ColorSpace.Linear;

            AssetDatabase.StartAssetEditing();
            try
            {
                Directory.CreateDirectory(SettingsFolder);
            }
            finally
            {
                AssetDatabase.StopAssetEditing();
            }

            var high = CreatePipelineAsset("URP High Fidelity", 4096, true, 8, true);
            var balanced = CreatePipelineAsset("URP Balanced", 2048, true, 4, true);
            var performant = CreatePipelineAsset("URP Performant", 1024, true, 2, false);

            GraphicsSettings.defaultRenderPipeline = balanced;
            EnsureUniversalGlobalSettings();
            AssignQualityAsset("High Fidelity", high);
            AssignQualityAsset("Balanced", balanced);
            AssignQualityAsset("Performant", performant);

            UpgradeBuiltInMaterialsToUrp();
            ConvertHdrpMaterials();
            ConvertScenes();

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("HDRP to URP conversion complete.");
        }

        private static void WriteLog(string message)
        {
            Directory.CreateDirectory("Logs");
            File.AppendAllText(
                LogPath,
                $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {message}{Environment.NewLine}");
        }

        private static UniversalRenderPipelineAsset CreatePipelineAsset(
            string name,
            int shadowResolution,
            bool additionalLightShadows,
            int additionalLights,
            bool hdr)
        {
            var assetPath = $"{SettingsFolder}/{name}.asset";
            var rendererPath = $"{SettingsFolder}/{name} Renderer.asset";

            var pipeline = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(assetPath);
            if (pipeline == null)
            {
                var renderer = CreateRendererData(rendererPath);
                pipeline = UniversalRenderPipelineAsset.Create(renderer);
                AssetDatabase.CreateAsset(pipeline, assetPath);
            }

            pipeline.supportsHDR = hdr;
            pipeline.supportsCameraDepthTexture = true;
            pipeline.supportsCameraOpaqueTexture = true;
            pipeline.msaaSampleCount = 4;
            pipeline.renderScale = 1f;
            pipeline.mainLightRenderingMode = LightRenderingMode.PerPixel;
            pipeline.supportsMainLightShadows = true;
            pipeline.mainLightShadowmapResolution = shadowResolution;
            pipeline.additionalLightsRenderingMode = LightRenderingMode.PerPixel;
            pipeline.maxAdditionalLightsCount = additionalLights;
            pipeline.supportsAdditionalLightShadows = additionalLightShadows;
            pipeline.additionalLightsShadowmapResolution = shadowResolution;
            pipeline.shadowDistance = 80f;
            pipeline.shadowCascadeCount = 4;
            pipeline.supportsSoftShadows = true;
            pipeline.reflectionProbeBlending = true;
            pipeline.reflectionProbeBoxProjection = true;

            EditorUtility.SetDirty(pipeline);
            return pipeline;
        }

        private static ScriptableRendererData CreateRendererData(string rendererPath)
        {
            var existing = AssetDatabase.LoadAssetAtPath<ScriptableRendererData>(rendererPath);
            if (existing != null)
                return existing;

            var method = typeof(UniversalRenderPipelineAsset).GetMethod(
                "CreateRendererAsset",
                BindingFlags.Static | BindingFlags.NonPublic,
                null,
                new[] { typeof(string), typeof(RendererType), typeof(bool), typeof(string) },
                null);

            if (method == null)
                throw new MissingMethodException("UniversalRenderPipelineAsset.CreateRendererAsset was not found.");

            return (ScriptableRendererData)method.Invoke(
                null,
                new object[] { rendererPath, RendererType.UniversalRenderer, false, "Renderer" });
        }

        private static void EnsureUniversalGlobalSettings()
        {
            var settingsType = typeof(UniversalRenderPipeline).Assembly.GetType(
                "UnityEngine.Rendering.Universal.UniversalRenderPipelineGlobalSettings");
            var ensure = settingsType?.GetMethod("Ensure", BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);
            ensure?.Invoke(null, new object[] { true });
        }

        private static void AssignQualityAsset(string qualityName, UniversalRenderPipelineAsset asset)
        {
            var previous = QualitySettings.GetQualityLevel();
            var names = QualitySettings.names;

            for (var i = 0; i < names.Length; i++)
            {
                if (!string.Equals(names[i], qualityName, StringComparison.OrdinalIgnoreCase))
                    continue;

                QualitySettings.SetQualityLevel(i, false);
                QualitySettings.renderPipeline = asset;
            }

            QualitySettings.SetQualityLevel(previous, false);
        }

        private static void UpgradeBuiltInMaterialsToUrp()
        {
            var upgraders = MaterialUpgrader.FetchAllUpgradersForPipeline(typeof(UniversalRenderPipelineAsset));
            MaterialUpgrader.UpgradeProjectFolder(
                upgraders,
                "Upgrade Materials to URP",
                MaterialUpgrader.UpgradeFlags.LogMessageWhenNoUpgraderFound);
        }

        private static void ConvertHdrpMaterials()
        {
            var lit = Shader.Find("Universal Render Pipeline/Lit");
            if (lit == null)
                throw new InvalidOperationException("URP Lit shader was not found.");

            foreach (var guid in AssetDatabase.FindAssets("t:Material", new[] { "Assets" }))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var material = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (material == null || material.shader == null)
                    continue;

                var shaderName = material.shader.name;
                var shaderPath = AssetDatabase.GetAssetPath(material.shader);
                if (!shaderName.StartsWith("HDRP/", StringComparison.Ordinal) &&
                    !shaderPath.Contains("HDRP", StringComparison.OrdinalIgnoreCase) &&
                    !shaderPath.Contains("HDLit", StringComparison.OrdinalIgnoreCase))
                    continue;

                var color = material.HasProperty("_BaseColor") ? material.GetColor("_BaseColor") :
                    material.HasProperty("_Color") ? material.GetColor("_Color") : Color.white;
                var baseMap = material.HasProperty("_BaseMap") ? material.GetTexture("_BaseMap") :
                    material.HasProperty("_MainTex") ? material.GetTexture("_MainTex") : null;
                var normal = material.HasProperty("_BumpMap") ? material.GetTexture("_BumpMap") : null;
                var metallic = material.HasProperty("_Metallic") ? material.GetFloat("_Metallic") : 0f;
                var smoothness = material.HasProperty("_Smoothness") ? material.GetFloat("_Smoothness") : 0.5f;
                var emission = material.HasProperty("_EmissionColor") ? material.GetColor("_EmissionColor") : Color.black;

                material.shader = lit;
                material.SetColor("_BaseColor", color);
                material.SetTexture("_BaseMap", baseMap);
                material.SetTexture("_BumpMap", normal);
                material.SetFloat("_Metallic", metallic);
                material.SetFloat("_Smoothness", smoothness);
                material.SetColor("_EmissionColor", emission);
                EditorUtility.SetDirty(material);
            }
        }

        private static void ConvertScenes()
        {
            foreach (var guid in AssetDatabase.FindAssets("t:Scene", new[] { "Assets" }))
            {
                var scenePath = AssetDatabase.GUIDToAssetPath(guid);
                var scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);

                var changed = false;
                foreach (var camera in UnityEngine.Object.FindObjectsByType<Camera>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                {
                    if (camera.GetComponent<UniversalAdditionalCameraData>() == null)
                    {
                        camera.gameObject.AddComponent<UniversalAdditionalCameraData>();
                        changed = true;
                    }
                }

                foreach (var light in UnityEngine.Object.FindObjectsByType<Light>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                {
                    if (light.GetComponent<UniversalAdditionalLightData>() == null)
                    {
                        light.gameObject.AddComponent<UniversalAdditionalLightData>();
                        changed = true;
                    }
                }

                foreach (var behaviour in UnityEngine.Object.FindObjectsByType<MonoBehaviour>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                {
                    if (behaviour == null)
                        continue;

                    var typeName = behaviour.GetType().FullName;
                    if (typeName == "UnityEngine.Rendering.HighDefinition.WaterSurface")
                    {
                        CreateUrpWaterMesh(behaviour.gameObject);
                        UnityEngine.Object.DestroyImmediate(behaviour);
                        changed = true;
                    }
                    else if (typeName == "UnityEngine.Rendering.HighDefinition.HDAdditionalCameraData" ||
                             typeName == "UnityEngine.Rendering.HighDefinition.HDAdditionalLightData")
                    {
                        UnityEngine.Object.DestroyImmediate(behaviour);
                        changed = true;
                    }
                }

                if (changed)
                {
                    EditorSceneManager.MarkSceneDirty(scene);
                    EditorSceneManager.SaveScene(scene);
                }
            }
        }

        private static void CreateUrpWaterMesh(GameObject waterObject)
        {
            if (waterObject.GetComponentInChildren<MeshRenderer>() != null)
                return;

            var material = AssetDatabase.LoadAssetAtPath<Material>(WaterMaterialPath);
            var waterMesh = GameObject.CreatePrimitive(PrimitiveType.Plane);
            waterMesh.name = "URP Water Surface Mesh";
            waterMesh.transform.SetParent(waterObject.transform, false);
            waterMesh.transform.localPosition = Vector3.zero;
            waterMesh.transform.localRotation = Quaternion.identity;
            waterMesh.transform.localScale = new Vector3(100f, 1f, 100f);

            var collider = waterMesh.GetComponent<Collider>();
            if (collider != null)
                UnityEngine.Object.DestroyImmediate(collider);

            var renderer = waterMesh.GetComponent<MeshRenderer>();
            if (material != null)
                renderer.sharedMaterial = material;
        }
    }
}
