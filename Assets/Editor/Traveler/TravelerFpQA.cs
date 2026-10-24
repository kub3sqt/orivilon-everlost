using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using Orivilon.Player;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Orivilon.EditorTools
{
    /// <summary>
    /// QA „vyrážky" na pažích z 1. osoby: stejné záběry (proti slunci, z boku, po slunci) v izolačních variantách –
    /// výchozí stav, bez SSAO, bez doplňkového světla. Izolace je dočasná a po skončení se vrátí.
    /// Výstup: _reports/traveler-v004/fp5_&lt;tag&gt;_&lt;variant&gt;_&lt;směr&gt;.png + fp5.txt.
    /// </summary>
    internal static class TravelerFpQA
    {
        [MenuItem("Tools/Everlost/Character v004/QA/FP rash isolation shots")]
        public static void Run() { Start("now", true); }

        [MenuItem("Tools/Everlost/Character v004/QA/FP shots (no isolation)")]
        public static void RunPlain() { Start("final", false); }

        public static string Tag = "now";

        [MenuItem("Tools/Everlost/Character v004/QA/FP material dump")]
        public static void Dump()
        {
            var sb = new StringBuilder();
            foreach (var r in Object.FindObjectsByType<Renderer>(FindObjectsSortMode.None))
            {
                if (r.gameObject.layer != TravelerAvatar.ViewLayer || !r.enabled) continue;
                var mesh = r is SkinnedMeshRenderer smr ? smr.sharedMesh : r.GetComponent<MeshFilter>()?.sharedMesh;
                sb.AppendLine(r.name + " mesh " + (mesh ? mesh.name + " v" + mesh.vertexCount + " colors " + (mesh.colors32 != null && mesh.colors32.Length > 0) + " tangents " + (mesh.tangents.Length > 0) + " uv " + (mesh.uv.Length > 0) : "-") + " recvShadows " + r.receiveShadows + " shadowCast " + r.shadowCastingMode);
                foreach (var m in r.sharedMaterials)
                {
                    if (m == null) continue;
                    sb.Append("   mat " + m.name + " shader " + m.shader.name + " queue " + m.renderQueue);
                    foreach (var pn in new[] { "_BaseColor", "_Color", "_EmissionColor", "_SpecColor" }) if (m.HasProperty(pn)) sb.Append(" " + pn + " " + m.GetColor(pn));
                    foreach (var pn in new[] { "_Smoothness", "_Glossiness", "_Metallic", "_BumpScale", "_OcclusionStrength", "_Surface", "_Cull", "_ReceiveShadows", "_SpecularHighlights", "_EnvironmentReflections", "_WorkflowMode" }) if (m.HasProperty(pn)) sb.Append(" " + pn + " " + m.GetFloat(pn).ToString("0.###"));
                    foreach (var pn in new[] { "_BaseMap", "_MainTex", "_BumpMap", "_MetallicGlossMap", "_OcclusionMap", "_EmissionMap" }) if (m.HasProperty(pn) && m.GetTexture(pn) != null) sb.Append(" " + pn + "=" + m.GetTexture(pn).name);
                    sb.AppendLine(" kw [" + string.Join(",", m.shaderKeywords) + "]");
                }
            }
            var sun = RenderSettings.sun;
            sb.AppendLine("sun " + (sun ? sun.name + " " + sun.color + " i " + sun.intensity + " rot " + sun.transform.eulerAngles + " shadows " + sun.shadows : "-") + " ambient " + RenderSettings.ambientMode + " " + RenderSettings.ambientLight + " sky " + RenderSettings.ambientSkyColor + " refl " + RenderSettings.defaultReflectionMode + " int " + RenderSettings.reflectionIntensity);
            File.WriteAllText(Path.Combine(TravelerV004Dump.OutDir, "fp_materials.txt"), sb.ToString());
        }

        private static void Start(string tag, bool isolate)
        {
            var fpc = Object.FindFirstObjectByType<FirstPersonController>();
            if (fpc == null) return;
            var r = fpc.gameObject.AddComponent<FpQARunner>();
            r.tag = tag; r.isolate = isolate;
        }

        public static List<ScriptableRendererFeature> Features<T>() where T : ScriptableRendererFeature
        {
            var res = new List<ScriptableRendererFeature>();
            var asset = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
            if (asset == null) return res;
            var f = typeof(UniversalRenderPipelineAsset).GetField("m_RendererDataList", BindingFlags.NonPublic | BindingFlags.Instance);
            if (f?.GetValue(asset) is ScriptableRendererData[] list)
                foreach (var rd in list)
                    if (rd != null) foreach (var feat in rd.rendererFeatures) if (feat is T) res.Add(feat);
            return res;
        }
    }

    internal sealed class FpQARunner : MonoBehaviour
    {
        public string tag; public bool isolate;
        private readonly StringBuilder sb = new StringBuilder();
        private readonly List<(string variant, float yaw, string dir)> steps = new List<(string, float, string)>();
        private int i = -1; private float t; private float sunYaw; private float fill0;
        private List<ScriptableRendererFeature> ssao; private Light sunLight; private LightShadows sunShadows0;

        private void Start()
        {
            TravelerAvatar.QaIgnoreCursor = true;
            TravelerPlayQA.Axe();
            Object.FindFirstObjectByType<TravelerCameraView>()?.SetMode(TravelerViewMode.FirstPerson);
            var sun = RenderSettings.sun;
            if (sun == null) foreach (var l in Object.FindObjectsByType<Light>(FindObjectsSortMode.None)) if (l.type == LightType.Directional && l.isActiveAndEnabled) { sun = l; break; }
            sunLight = sun; if (sun != null) sunShadows0 = sun.shadows;
            Vector3 d = sun != null ? -sun.transform.forward : Vector3.forward;
            sunYaw = Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg;
            sb.AppendLine("slunce elevace " + (Mathf.Asin(d.y) * Mathf.Rad2Deg).ToString("F0") + "°, intenzita " + (sun != null ? sun.intensity.ToString("F2") : "-"));
            ssao = TravelerFpQA.Features<ScreenSpaceAmbientOcclusion>();
            sb.AppendLine("SSAO feature: " + ssao.Count);
            string[] variants = isolate ? new[] { "base", "noSSAO" } : new[] { "final" };
            foreach (var v in variants)
            {
                steps.Add((v, 0f, "protiSlunci"));
                steps.Add((v, 90f, "bok"));
                steps.Add((v, 180f, "poSlunci"));
                steps.Add((v, 270f, "bok2"));
                steps.Add((v, 1000f, "nahoru"));
                steps.Add((v, 2000f, "dolu"));
            }
        }

        private void Apply(string v)
        {
            bool noSsao = v.Contains("noSSAO"), noFill = v.Contains("noFill"), noSh = v.Contains("noSunShadow"), inBase = v == "inBase";
            foreach (var f in ssao) f.SetActive(!noSsao);
            if (sunLight != null) sunLight.shadows = noSh ? LightShadows.None : sunShadows0;
            // Paže v základní kameře místo overlay (near plane hlavní kamery je ořízne, ale ukáže stínování).
            var main = GetComponent<FirstPersonController>().playerCamera;
            var vc = main.transform.Find("TravelerViewCamera")?.GetComponent<Camera>();
            if (vc != null) vc.enabled = !inBase;
            main.cullingMask = inBase ? main.cullingMask | (1 << TravelerAvatar.ViewLayer) : main.cullingMask & ~(1 << TravelerAvatar.ViewLayer);
        }

        private void Update()
        {
            t += Time.unscaledDeltaTime;
            if (i >= 0 && t < 2.2f) return;
            if (i >= 0)
            {
                var s = steps[i];
                string name = "fp5_" + tag + "_" + s.variant + "_" + s.dir;
                ScreenCapture.CaptureScreenshot(Path.Combine(TravelerV004Dump.OutDir, name + ".png"));
                sb.AppendLine(name);
            }
            i++; t = 0f;
            if (i >= steps.Count) { Finish(); return; }
            var n = steps[i];
            Apply(n.variant);
            float pitch = n.yaw == 1000f ? -40f : n.yaw == 2000f ? 50f : 20f;
            GetComponent<FirstPersonController>().SetLook(sunYaw + (n.yaw >= 1000f ? 180f : n.yaw), pitch);
        }

        private void Finish()
        {
            Apply("restore");
            foreach (var f in ssao) f.SetActive(true);
            File.WriteAllText(Path.Combine(TravelerV004Dump.OutDir, "fp5_" + tag + ".txt"), sb.ToString());
            Destroy(this);
        }

        private void OnDestroy()
        {
            if (sunLight != null) sunLight.shadows = sunShadows0;
            if (ssao != null) foreach (var f in ssao) f.SetActive(true);
        }
    }
}
