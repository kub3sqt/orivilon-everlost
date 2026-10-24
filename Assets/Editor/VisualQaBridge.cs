using System;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Orivilon.EditorTools
{
    /// <summary>
    /// Vizuální QA příkazy pro RemoteBridge (jen editor, do buildu nejde, hru nemění):
    /// <c>clean on|off</c> – F1 čistý pohled bez HUD pro snímky;
    /// <c>visdiag jméno</c> – stav osvětlení, mlhy, oblohy, stínů a post-processu do _reports/remote/jméno.txt;
    /// <c>sf Typ pole hodnota</c> / <c>gf Typ pole</c> – čtení/zápis statického pole (ladění za běhu, nic se neukládá);
    /// <c>relief jméno poloměr krok</c> – výšková mapa okolí kamery a nejlepší vyhlídky nad údolím.
    /// </summary>
    internal static class VisualQaBridge
    {
        private static readonly string Dir =
            Path.Combine(Path.GetDirectoryName(Application.dataPath), "_reports", "remote");
        private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
        private static Volume qaVolume;

        public static bool TryRun(string line, Action<string> outFn)
        {
            if (line == "clean on" || line == "clean off")
            {
                var cv = Orivilon.UI.Refresh.CleanView.Instance;
                if (cv == null) { outFn("ERR clean: CleanView chybí"); return true; }
                if (line.EndsWith("on")) outFn("clean on " + cv.Enter()); else { cv.Exit(); outFn("clean off"); }
                return true;
            }
            if (line.StartsWith("visdiag"))
            {
                string name = line.Length > 8 ? line.Substring(8).Trim() : "visdiag";
                string text = Diag();
                File.WriteAllText(Path.Combine(Dir, name + ".txt"), text);
                outFn("visdiag " + name + " (" + text.Length + " B)");
                return true;
            }
            if (line.StartsWith("sf ") || line.StartsWith("gf "))
            {
                var p = line.Substring(3).Trim().Split(new[] { ' ' }, 3);
                outFn(StaticField(p[0], p[1], line.StartsWith("sf ") && p.Length > 2 ? p[2] : null));
                return true;
            }
            if (line.StartsWith("gfloat "))
            {
                var p = line.Substring(7).Trim().Split(' ');
                Shader.SetGlobalFloat(p[0], float.Parse(p[1], Inv));
                outFn("gfloat " + p[0] + " = " + Shader.GetGlobalFloat(p[0]).ToString(Inv));
                return true;
            }
            if (line == "camhdr on" || line == "camhdr off")
            {
                var cam = Camera.main;
                if (cam == null) { outFn("ERR camhdr"); return true; }
                cam.allowHDR = line.EndsWith("on");
                outFn("camhdr " + cam.allowHDR);
                return true;
            }
            if (line.StartsWith("qavol "))
            {
                // Dočasný QA volume s nejvyšší prioritou (jen tento běh): qavol tonemap aces|neutral|none, qavol off
                string a = line.Substring(6).Trim();
                if (qaVolume == null && a != "off")
                {
                    var go = new GameObject("QA Volume") { hideFlags = HideFlags.DontSave };
                    qaVolume = go.AddComponent<Volume>();
                    qaVolume.isGlobal = true; qaVolume.priority = 1000f;
                    qaVolume.profile = ScriptableObject.CreateInstance<VolumeProfile>();
                }
                if (a == "off") { if (qaVolume != null) UnityEngine.Object.Destroy(qaVolume.gameObject); qaVolume = null; outFn("qavol off"); return true; }
                var pa = a.Split(' ');
                if (pa[0] == "tonemap")
                {
                    if (!qaVolume.profile.TryGet(out Tonemapping tm)) tm = qaVolume.profile.Add<Tonemapping>(true);
                    tm.mode.Override(pa[1] == "aces" ? TonemappingMode.ACES : pa[1] == "neutral" ? TonemappingMode.Neutral : TonemappingMode.None);
                }
                else if (pa[0] == "exposure" || pa[0] == "sat" || pa[0] == "contrast")
                {
                    if (!qaVolume.profile.TryGet(out ColorAdjustments ca)) ca = qaVolume.profile.Add<ColorAdjustments>(true);
                    float v = float.Parse(pa[1], Inv);
                    if (pa[0] == "exposure") ca.postExposure.Override(v); else if (pa[0] == "sat") ca.saturation.Override(v); else ca.contrast.Override(v);
                }
                else if (pa[0] == "temp")
                {
                    if (!qaVolume.profile.TryGet(out WhiteBalance wb)) wb = qaVolume.profile.Add<WhiteBalance>(true);
                    wb.temperature.Override(float.Parse(pa[1], Inv));
                }
                outFn("qavol " + a);
                return true;
            }
            if (line == "mistrebuild")
            {
                var atmo = Orivilon.World.Biomes.BiomeAtmosphere.Instance;
                var fi = typeof(Orivilon.World.Biomes.BiomeAtmosphere).GetField("mistField", BindingFlags.Instance | BindingFlags.NonPublic);
                (atmo != null && fi != null ? fi.GetValue(atmo) as Orivilon.World.Biomes.ValleyMistField : null)?.Invalidate();
                outFn("mistrebuild");
                return true;
            }
            if (line == "mistdump")
            {
                // Mapa údolní mlhy: síla údolí (0–9) a dno; hledání míst s mlhou pro snímky.
                var atmo = Orivilon.World.Biomes.BiomeAtmosphere.Instance;
                var fi = typeof(Orivilon.World.Biomes.BiomeAtmosphere).GetField("mistField", BindingFlags.Instance | BindingFlags.NonPublic);
                var field = atmo != null && fi != null ? fi.GetValue(atmo) as Orivilon.World.Biomes.ValleyMistField : null;
                if (field == null || !field.Ready) { outFn("ERR mistdump: mapa není"); return true; }
                var px = field.Texture.GetPixels();
                var sb = new StringBuilder();
                sb.AppendLine(string.Format(Inv, "origin {0:0} {1:0} cell {2:0}", field.Origin.x, field.Origin.y, Orivilon.World.Biomes.ValleyMistField.Cell));
                var best = new System.Collections.Generic.List<(float v, string s)>();
                for (int z = Orivilon.World.Biomes.ValleyMistField.Size - 1; z >= 0; z--)
                {
                    for (int x = 0; x < Orivilon.World.Biomes.ValleyMistField.Size; x++)
                    {
                        var c = px[z * Orivilon.World.Biomes.ValleyMistField.Size + x];
                        sb.Append((char)('0' + Mathf.Clamp(Mathf.RoundToInt(c.g * 9f), 0, 9)));
                        best.Add((c.g * c.a, string.Format(Inv, "{0:0} {1:0} floor {2:0} valley {3:0.00} relief {4:0}",
                            field.Origin.x + (x + 0.5f) * Orivilon.World.Biomes.ValleyMistField.Cell, field.Origin.y + (z + 0.5f) * Orivilon.World.Biomes.ValleyMistField.Cell, c.r, c.g, c.a)));
                    }
                    sb.AppendLine();
                }
                best.Sort((a, b) => b.v.CompareTo(a.v));
                for (int k = 0; k < Mathf.Min(12, best.Count); k++) sb.AppendLine(best[k].s);
                File.WriteAllText(Path.Combine(Dir, "mistdump.txt"), sb.ToString());
                outFn("mistdump " + (best.Count > 0 ? best[0].s : ""));
                return true;
            }
            if (line.StartsWith("relief "))
            {
                var p = line.Substring(7).Trim().Split(' ');
                outFn(Relief(p[0], float.Parse(p[1], Inv), float.Parse(p[2], Inv)));
                return true;
            }
            return false;
        }

        private static Type FindType(string name)
        {
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                var t = asm.GetType(name, false);
                if (t != null) return t;
            }
            return null;
        }

        private static string StaticField(string typeName, string field, string value)
        {
            var t = FindType(typeName);
            if (t == null) return "ERR typ " + typeName;
            const BindingFlags F = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
            var fi = t.GetField(field, F);
            var pi = fi == null ? t.GetProperty(field, F) : null;
            if (fi == null && pi == null) return "ERR pole " + field;
            Type ft = fi != null ? fi.FieldType : pi.PropertyType;
            if (value != null)
            {
                object v;
                if (ft == typeof(float)) v = float.Parse(value, Inv);
                else if (ft == typeof(int)) v = int.Parse(value, Inv);
                else if (ft == typeof(bool)) v = value == "1" || value == "true" || value == "on";
                else if (ft == typeof(Color))
                {
                    var c = value.Split(',');
                    v = new Color(float.Parse(c[0], Inv), float.Parse(c[1], Inv), float.Parse(c[2], Inv), c.Length > 3 ? float.Parse(c[3], Inv) : 1f);
                }
                else return "ERR typ pole " + ft.Name;
                if (fi != null) fi.SetValue(null, v); else pi.SetValue(null, v);
            }
            object cur = fi != null ? fi.GetValue(null) : pi.GetValue(null);
            return (value != null ? "sf " : "gf ") + typeName + "." + field + " = " + Convert.ToString(cur, Inv);
        }

        private static string Diag()
        {
            var sb = new StringBuilder();
            sb.AppendLine("quality " + QualitySettings.GetQualityLevel() + " " + QualitySettings.names[QualitySettings.GetQualityLevel()]
                + " | pipeline " + (GraphicsSettings.currentRenderPipeline != null ? GraphicsSettings.currentRenderPipeline.name : "-")
                + " | colorspace " + QualitySettings.activeColorSpace);
            if (GraphicsSettings.currentRenderPipeline is UniversalRenderPipelineAsset u)
                sb.AppendLine(string.Format(Inv, "shadows dist {0:0} cascades {1} split {2} res {3} depthBias {5} normalBias {6} hdr {7} msaa {8} renderScale {9}",
                    u.shadowDistance, u.shadowCascadeCount, u.cascade4Split, u.mainLightShadowmapResolution, 0,
                    u.shadowDepthBias, u.shadowNormalBias, u.supportsHDR, u.msaaSampleCount, u.renderScale));
            foreach (var l in UnityEngine.Object.FindObjectsByType<Light>(FindObjectsSortMode.None))
                if (l.type == LightType.Directional)
                    sb.AppendLine(string.Format(Inv, "light {0} en {1} int {2:0.000} col {3} rot {4} shadows {5} str {6:0.00} bias {7:0.00}/{8:0.00} near {9:0.0}",
                        l.name, l.enabled, l.intensity, l.color, l.transform.eulerAngles, l.shadows, l.shadowStrength, l.shadowBias, l.shadowNormalBias, l.shadowNearPlane));
            sb.AppendLine("ambient " + RenderSettings.ambientMode + " sky " + RenderSettings.ambientSkyColor + " eq " + RenderSettings.ambientEquatorColor
                + " gr " + RenderSettings.ambientGroundColor + " intensity " + RenderSettings.ambientIntensity);
            sb.AppendLine(string.Format(Inv, "fog {0} {1} {2:0}-{3:0} col {4}", RenderSettings.fog, RenderSettings.fogMode,
                RenderSettings.fogStartDistance, RenderSettings.fogEndDistance, RenderSettings.fogColor));
            var sky = RenderSettings.skybox;
            if (sky != null)
            {
                sb.Append("skybox " + sky.name + " shader " + sky.shader.name);
                foreach (var prop in new[] { "_SkyColor", "_EquatorColor", "_GroundColor", "_EquatorHeight", "_EquatorSmoothness", "_SunColor", "_SunSize", "_CloudsColor" })
                    if (sky.HasProperty(prop))
                        sb.Append(" " + prop + "=" + (prop.Contains("Color") ? sky.GetColor(prop).ToString() : sky.GetFloat(prop).ToString(Inv)));
                sb.AppendLine();
            }
            sb.AppendLine("mist " + Shader.GetGlobalVector("_EverlostValleyMist").ToString("F5") + " col " + Shader.GetGlobalColor("_EverlostValleyMistColor"));
            var cam = Camera.main;
            if (cam != null)
            {
                var ad = cam.GetUniversalAdditionalCameraData();
                sb.AppendLine(string.Format(Inv, "camera {0} pos {1} rot {2} fov {3:0} far {4:0} hdr {5} post {6} aa {7} renderer {8} depthTex {9}",
                    cam.name, cam.transform.position, cam.transform.eulerAngles, cam.fieldOfView, cam.farClipPlane, cam.allowHDR,
                    ad.renderPostProcessing, ad.antialiasing, ad.scriptableRenderer != null ? ad.scriptableRenderer.GetType().Name : "-", ad.requiresDepthTexture));
            }
            foreach (var c in UnityEngine.Object.FindObjectsByType<Camera>(FindObjectsSortMode.None))
                if (c != cam) sb.AppendLine("camera+ " + c.name + " en " + c.enabled + " depth " + c.depth + " type " + c.GetUniversalAdditionalCameraData().renderType);
            var stack = VolumeManager.instance.stack;
            void Comp<T>(string label, Func<T, string> f) where T : VolumeComponent
            {
                var c = stack.GetComponent<T>();
                sb.AppendLine(label + ": " + (c == null ? "-" : f(c)));
            }
            Comp<Tonemapping>("tonemap", c => c.mode.value + " active " + c.IsActive());
            Comp<ColorAdjustments>("coloradj", c => string.Format(Inv, "exp {0:0.00} contrast {1:0.0} sat {2:0.0} filter {3} hue {4:0}", c.postExposure.value, c.contrast.value, c.saturation.value, c.colorFilter.value, c.hueShift.value));
            Comp<WhiteBalance>("whitebal", c => string.Format(Inv, "temp {0:0.0} tint {1:0.0}", c.temperature.value, c.tint.value));
            Comp<Bloom>("bloom", c => string.Format(Inv, "int {0:0.00} thr {1:0.00} scatter {2:0.00} hq {3}", c.intensity.value, c.threshold.value, c.scatter.value, c.highQualityFiltering.value));
            Comp<Vignette>("vignette", c => string.Format(Inv, "int {0:0.00}", c.intensity.value));
            Comp<SplitToning>("split", c => c.shadows.value + " / " + c.highlights.value + " bal " + c.balance.value);
            Comp<ShadowsMidtonesHighlights>("smh", c => c.shadows.value + " / " + c.midtones.value + " / " + c.highlights.value);
            foreach (var v in UnityEngine.Object.FindObjectsByType<Volume>(FindObjectsSortMode.None))
                sb.AppendLine("volume " + v.name + " global " + v.isGlobal + " prio " + v.priority + " w " + v.weight + " profile " + (v.sharedProfile != null ? v.sharedProfile.name : v.profile != null ? v.profile.name : "-"));
            var sun = UnityEngine.Object.FindFirstObjectByType<SunRotation>();
            if (sun != null) sb.AppendLine(string.Format(Inv, "time {0:0.00} night {1:0.00} sunH {2:0.00} vista {3:0.00}", SunRotation.timeOfDayStatic, SunRotation.nightBlendStatic, SunRotation.sunHeightStatic, SunRotation.VistaFactor));
            var atmo = Orivilon.World.Biomes.BiomeAtmosphere.Instance;
            if (atmo != null) sb.AppendLine(atmo.MistInfo + " | " + atmo.Describe());
            sb.AppendLine("pocket " + Shader.GetGlobalVector("_EverlostValleyPocket").ToString("F4") + " / " + Shader.GetGlobalVector("_EverlostValleyPocket2").ToString("F5")
                + " sun " + Shader.GetGlobalVector("_EverlostMistSun").ToString("F3") + " grassShadow " + Shader.GetGlobalVector("_EverlostGrassShadow"));
            return sb.ToString();
        }

        /// <summary>Výšková mapa kolem kamery a body s největším převýšením nad údolím pod nimi.</summary>
        private static string Relief(string name, float radius, float step)
        {
            var t = Orivilon.World.Generation.VoxelTerrain.instance;
            var cam = Camera.main;
            if (t == null || cam == null) return "ERR relief: terén/kamera chybí";
            Vector3 o = cam.transform.position;
            int n = Mathf.CeilToInt(radius * 2f / step) + 1;
            var h = new float[n, n];
            float sea = t.SeaLevel;
            var sb = new StringBuilder();
            sb.AppendLine(string.Format(Inv, "střed {0:0} {1:0}, poloměr {2:0}, krok {3:0}, moře {4:0.0}", o.x, o.z, radius, step, sea));
            for (int j = 0; j < n; j++)
            {
                for (int i = 0; i < n; i++)
                {
                    h[i, j] = t.SurfaceHeight(o.x - radius + i * step, o.z - radius + j * step);
                    sb.Append(Mathf.RoundToInt(h[i, j]).ToString(Inv).PadLeft(5));
                }
                sb.AppendLine();
            }
            // vyhlídka: bod nad mořem, kde v okruhu 3 kroků klesne terén nejvíc (údolí pod hřebenem)
            sb.AppendLine("vyhlídky (x z výška pokles směr):");
            var list = new System.Collections.Generic.List<(float score, string s)>();
            for (int j = 3; j < n - 3; j++)
            for (int i = 3; i < n - 3; i++)
            {
                if (h[i, j] < sea + 20f) continue;
                float best = 0f; int bd = 0;
                for (int d = 0; d < 8; d++)
                {
                    int dx = (int)Mathf.Round(Mathf.Sin(d * Mathf.PI / 4f) * 3f), dz = (int)Mathf.Round(Mathf.Cos(d * Mathf.PI / 4f) * 3f);
                    float drop = h[i, j] - Mathf.Max(sea, h[i + dx, j + dz]);
                    if (drop > best) { best = drop; bd = d * 45; }
                }
                list.Add((best, string.Format(Inv, "{0:0} {1:0} {2:0} {3:0} {4}", o.x - radius + i * step, o.z - radius + j * step, h[i, j], best, bd)));
            }
            list.Sort((a, b) => b.score.CompareTo(a.score));
            for (int k = 0; k < Mathf.Min(15, list.Count); k++) sb.AppendLine(list[k].s);
            File.WriteAllText(Path.Combine(Dir, name + ".txt"), sb.ToString());
            return "relief " + name + " " + n + "x" + n + (list.Count > 0 ? " nejlepší " + list[0].s : "");
        }
    }
}
