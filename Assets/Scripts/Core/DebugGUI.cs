using System.Globalization;
using System.Text;
using Orivilon.Data;
using Orivilon.Inventory.Inventory;
using Orivilon.UI.Arcanum;
using Orivilon.World.Generation;
using Orivilon.World.Spawning;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Orivilon.Core
{
    /// <summary>
    /// F3 diagnostický overlay (UI refresh, 10/2026). Nahrazuje starý F3 panel (DebugMenu + Sliders).
    ///
    /// <para>Overlay je autorský prefab F3Diagnostics (vlastní canvas, pořadí 70): herní font v nadpisu,
    /// Space Mono v těle, černý panel přichycený k hornímu levému rohu. Tento skript jen plní text
    /// a animuje. Nezachytává myš ani klávesy (žádný GraphicRaycaster).</para>
    ///
    /// <para>Biom se čte ze stejné autoritativní cesty jako /biome where:
    /// <see cref="VoxelTerrain.RegionAt(float, float, bool, out float, out float, out ClimateSample)"/>
    /// v poloze hráče (kořen vieweru), názvy a ID z tabulek GameConsole.Regions.</para>
    ///
    /// <para>Skrytý overlay nic nepočítá; zobrazený obnovuje text 4× za sekundu a biom 2× za sekundu.
    /// Během načítání, pauzy a otevřených menu (inventář, stavba, Arcanum) je pozastavený.</para>
    /// </summary>
    public class DebugGUI : MonoBehaviour
    {
        // ── Starý F3 panel. Pole zůstávají kvůli serializovaným referencím ve scéně Game;
        //    panely se jen trvale vypnou a nic do nich nepíše. ──
        [Header("Legacy F3 (kept for scene references, never shown)")]
        [SerializeField] Canvas debugCanvas;
        [SerializeField] GameObject debugPanel;
        [SerializeField] GameObject slidersPanel;
        [SerializeField] TextMeshProUGUI avgFPSText;
        [SerializeField] TextMeshProUGUI playerXText;
        [SerializeField] TextMeshProUGUI playerYText;
        [SerializeField] TextMeshProUGUI playerZText;
        [SerializeField] TextMeshProUGUI currentBiomeText;
        [SerializeField] TextMeshProUGUI timeText;

        private const float RefH = 1080f;
        private const float PanelW = 600f;
        private const float TextRefresh = 0.25f;     // 4 Hz
        private const float BiomeRefresh = 0.5f;     // 2 Hz
        private const float AnimIn = 0.22f, AnimOut = 0.16f;

        private static readonly Color PanelTint = Color.white;

        /// <summary>QA: poslední zobrazené hodnoty biomu (čte editorový most).</summary>
        public static int LastRegion { get; private set; } = -1;
        public static Vector3 LastSamplePos { get; private set; }
        public static float LastBiomeQueryMs { get; private set; }
        public static string LastText { get; private set; } = "";

        private bool toggled;
        private float anim;            // 0 = skryto, 1 = zobrazeno
        private bool built;

        [Header("F3 overlay (prefab Assets/Resources/UI/F3Diagnostics)")]
        [SerializeField] private F3Overlay overlay;

        private Canvas canvas;
        private RectTransform panel;
        private CanvasGroup group;
        private TMP_Text header, body;

        private Transform player;
        private Camera playerCam;

        private int frames;
        private float frameAccum, frameMax, nextText, nextBiome;
        private float fps = -1f, frameAvgMs, frameMaxMs;

        // poslední biom
        private bool biomeValid;
        private int domRegion = -1, secRegion = -1;
        private float domW, secW, surfY, temp, hum, microW;
        private ClimateBand band;
        private MicroBiome micro;
        private Vector3 biomeAt = new Vector3(float.NaN, 0, 0);
        private float lastBiomeQuery = -10f;

        private readonly StringBuilder sb = new StringBuilder(1024);
        private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        private void Awake()
        {
            HideLegacy();
        }

        private void Start()
        {
            if (debugCanvas != null) debugCanvas.enabled = true;   // hlavní herní Canvas – původní chování
            HideLegacy();
        }

        private void HideLegacy()
        {
            if (debugPanel != null) debugPanel.SetActive(false);
            if (slidersPanel != null) slidersPanel.SetActive(false);
        }

        /// <summary>QA/most: zapne nebo vypne overlay stejně jako F3.</summary>
        public void SetToggled(bool on)
        {
            if (toggled == on) return;
            toggled = on;
            if (on) { frames = 0; frameAccum = frameMax = 0f; nextText = 0f; nextBiome = 0f; biomeValid = false; }
        }

        public bool IsToggled => toggled;

        /// <summary>Overlay se ukazuje jen při běžné hře – ne při načítání, v pauze ani v menu.</summary>
        private static bool GameplayVisible()
        {
            if (SceneLoader.IsLoading) return false;
            var gm = GameManager.instance;
            if (gm == null || !gm.IsLoadingComplete || gm.IsPaused || gm.isMenuOpen) return false;
            return true;
        }

        private void Update()
        {
            if (!SceneLoader.IsLoading && Input.GetKeyDown(KeyCode.F3))
                SetToggled(!toggled);

            bool want = toggled && GameplayVisible();
            if (!want && anim <= 0f) { if (built && canvas != null && canvas.enabled) canvas.enabled = false; return; }

            if (!built) Build();
            if (canvas == null) return;
            if (!canvas.enabled) canvas.enabled = true;

            float dt = Time.unscaledDeltaTime;
            if (want)
            {
                frames++;
                frameAccum += dt;
                if (dt > frameMax) frameMax = dt;
                float now = Time.unscaledTime;
                if (now >= nextBiome) { nextBiome = now + BiomeRefresh; RefreshBiome(); }
                if (now >= nextText || anim <= 0f) { nextText = now + TextRefresh; RefreshText(); }
            }

            if (want && anim < 1f) anim = Mathf.Min(1f, anim + dt / AnimIn);
            else if (!want && anim > 0f) anim = Mathf.Max(0f, anim - dt / AnimOut);
            float e = want ? 1f - (1f - anim) * (1f - anim) * (1f - anim) : anim * anim;   // ease-out / ease-in
            group.alpha = e;
            panel.anchoredPosition = new Vector2(-6f - (1f - e) * 24f, 6f);
            if (anim <= 0f) canvas.enabled = false;
        }

        // =====================================================================
        // Data
        // =====================================================================

        private bool ResolvePlayer()
        {
            if (player != null && playerCam != null) return true;
            VoxelTerrain t = VoxelTerrain.instance;
            // Stejně jako GameConsole.PlayerRoot: kořen vieweru (kamera hráče), jinak tag Player.
            if (t != null && t.viewer != null) player = t.viewer.root != null ? t.viewer.root : t.viewer;
            if (player == null)
            {
                var go = GameObject.FindGameObjectWithTag("Player");
                if (go != null) player = go.transform;
            }
            if (player != null && playerCam == null) playerCam = player.GetComponentInChildren<Camera>();
            return player != null;
        }

        private void RefreshBiome()
        {
            VoxelTerrain t = VoxelTerrain.instance;
            if (t == null || !ResolvePlayer()) { biomeValid = false; return; }
            Vector3 p = player.position;
            // stojící hráč: dotaz jen jednou za 2 s
            if (biomeValid && (new Vector2(p.x - biomeAt.x, p.z - biomeAt.z)).sqrMagnitude < 0.25f && Time.unscaledTime - lastBiomeQuery < 2f)
                return;
            lastBiomeQuery = Time.unscaledTime;

            float t0 = Time.realtimeSinceStartup;
            RegionWeights w = t.RegionAt(p.x, p.z, true, out surfY, out _, out ClimateSample c);
            micro = t.MicroAt(p.x, p.z, out microW);
            LastBiomeQueryMs = (Time.realtimeSinceStartup - t0) * 1000f;

            domRegion = w.Dominant();
            domW = domRegion >= 0 ? w.Get(domRegion) : 0f;
            secRegion = -1; secW = 0f;
            for (int i = 0; i < BiomeMath.Count; i++)
            {
                if (i == domRegion) continue;
                float v = w.Get(i);
                if (v > secW) { secW = v; secRegion = i; }
            }
            if (secW < 0.05f) secRegion = -1;
            band = c.Band; temp = c.t; hum = c.h;
            biomeAt = p;
            biomeValid = domRegion >= 0;
            LastRegion = domRegion;
            LastSamplePos = p;
        }

        private void RefreshText()
        {
            if (frames > 0 && frameAccum > 0f)
            {
                fps = frames / frameAccum;
                frameAvgMs = frameAccum / frames * 1000f;
                frameMaxMs = frameMax * 1000f;
            }
            frames = 0; frameAccum = 0f; frameMax = 0f;

            VoxelTerrain t = VoxelTerrain.instance;
            bool hasPlayer = ResolvePlayer();
            sb.Clear();

            Section("PERFORMANCE");
            Row("FPS");
            if (fps > 0f) sb.Append(Mono(fps.ToString("0", Inv), 4)).Append("<pos=50%>").Append(Mono(frameAvgMs.ToString("0.0", Inv), 5))
                            .Append(" ms<pos=75%><color=#a8b0a6>max </color>").Append(Mono(frameMaxMs.ToString("0.0", Inv), 4)).Append("<color=#a8b0a6>ms</color>");
            else sb.Append(Na);
            sb.Append('\n');

            Section("POSITION");
            if (hasPlayer)
            {
                Vector3 p = player.position;
                Row("XYZ");
                sb.Append(Mono(p.x.ToString("0.0", Inv), 8)).Append("<pos=50%>").Append(Mono(p.y.ToString("0.0", Inv), 7))
                  .Append("<pos=75%>").Append(Mono(p.z.ToString("0.0", Inv), 8)).Append('\n');
                Row("Chunk");
                if (t != null && t.ChunkSpan > 0f)
                {
                    int cx = Mathf.FloorToInt(p.x / t.ChunkSpan), cz = Mathf.FloorToInt(p.z / t.ChunkSpan);
                    sb.Append(Mono(cx.ToString(Inv), 8)).Append("<pos=50%>").Append(Mono(cz.ToString(Inv), 7))
                      .Append("<pos=75%><color=#a8b0a6>").Append(t.ChunkSpan.ToString("0", Inv)).Append(" m</color>\n");
                }
                else sb.Append(Na).Append('\n');
                Row("Facing");
                Transform f = playerCam != null ? playerCam.transform : player;
                Vector3 fw = f.forward;
                float yaw = Mathf.Atan2(fw.x, fw.z) * Mathf.Rad2Deg; if (yaw < 0f) yaw += 360f;
                float pitch = -Mathf.Asin(Mathf.Clamp(fw.y, -1f, 1f)) * Mathf.Rad2Deg;
                sb.Append(Cardinal(yaw)).Append("<pos=50%><color=#a8b0a6>yaw </color>").Append(Mono(yaw.ToString("0.0", Inv), 5))
                  .Append("<pos=75%><color=#a8b0a6>pitch </color>").Append(Mono(pitch.ToString("0", Inv), 3)).Append('\n');
            }
            else { Row("XYZ"); sb.Append(Na).Append('\n'); Row("Chunk"); sb.Append(Na).Append('\n'); Row("Facing"); sb.Append(Na).Append('\n'); }

            Section("WORLD");
            Row("Biome");
            if (biomeValid)
            {
                sb.Append("<color=#ffffff>").Append(GameConsole.RegionDisplayName(domRegion)).Append("</color>\n");
                Row("ID");
                sb.Append("<color=#c9e986>").Append(GameConsole.RegionCanonicalId(domRegion)).Append("</color>")
                  .Append("<pos=84%>").Append(Mono((domW * 100f).ToString("0", Inv), 3)).Append("%\n");
                Row("Blend");
                if (secRegion >= 0)
                    sb.Append("<color=#a8b0a6>").Append(GameConsole.RegionCanonicalId(secRegion)).Append("</color><pos=84%>")
                      .Append(Mono((secW * 100f).ToString("0", Inv), 3)).Append("%\n");
                else sb.Append("<color=#838b82>none</color>\n");
                Row("Micro");
                if (micro != MicroBiome.None && microW > 0.01f)
                    sb.Append(MicroName(micro)).Append(' ').Append((microW * 100f).ToString("0", Inv)).Append("%\n");
                else sb.Append("<color=#838b82>none</color>\n");
                Row("Climate");
                sb.Append(Capital(GameConsole.ClimateBandEn(band))).Append("<pos=50%><color=#a8b0a6>temp ").Append(temp.ToString("0.00", Inv))
                  .Append("<pos=75%>hum ").Append(hum.ToString("0.00", Inv)).Append("</color>\n");
                Row("Surface");
                float sea = t != null ? t.SeaLevel : 0f;
                sb.Append(Mono(surfY.ToString("0.0", Inv), 7)).Append(" m<pos=50%><color=#a8b0a6>");
                float rel = surfY - sea;
                if (rel < 0f) sb.Append("below sea ").Append((-rel).ToString("0.0", Inv)).Append(" m");
                else sb.Append("above sea ").Append(rel.ToString("0.0", Inv)).Append(" m");
                sb.Append("</color>\n");
            }
            else
            {
                sb.Append(Na).Append('\n');
                Row("ID"); sb.Append(Na).Append('\n');
                Row("Blend"); sb.Append(Na).Append('\n');
                Row("Micro"); sb.Append(Na).Append('\n');
                Row("Climate"); sb.Append(Na).Append('\n');
                Row("Surface"); sb.Append(Na).Append('\n');
            }
            Row("Seed");
            if (t != null) sb.Append(Mono(t.WorldSeed.ToString(Inv), 11)); else sb.Append(Na);
            sb.Append('\n');
            Row("Time");
            float tod = SunRotation.timeOfDayStatic;
            int hh = Mathf.FloorToInt(tod) % 24, mm = Mathf.FloorToInt((tod - Mathf.Floor(tod)) * 60f);
            sb.Append(Mono(hh.ToString("00", Inv) + ":" + mm.ToString("00", Inv), 5))
              .Append("<pos=50%><color=#a8b0a6>").Append(SunRotation.nightBlendStatic > 0.5f ? "night" : "day").Append("</color>\n");

            Section("MEMORY");
            Row("Unity");
            long alloc = UnityEngine.Profiling.Profiler.GetTotalAllocatedMemoryLong();
            if (alloc > 0) sb.Append(Mono((alloc / 1048576L).ToString(Inv), 5)).Append(" MB<pos=50%><color=#a8b0a6>allocated</color>\n");
            else sb.Append(Na).Append('\n');
            Row("Managed");
            sb.Append(Mono((System.GC.GetTotalMemory(false) / 1048576L).ToString(Inv), 5)).Append(" MB<pos=50%><color=#a8b0a6>heap in use</color>");

            body.SetText(sb);
            LastText = sb.ToString();
            float h = body.preferredHeight;
            panel.sizeDelta = new Vector2(panel.sizeDelta.x, Mathf.Ceil(h) + 86f);
        }

        private const string Na = "<color=#838b82>unavailable</color>";

        private void Section(string title)
        {
            sb.Append("<size=72%><color=#838b82><cspace=0.08em>").Append(title).Append("</cspace></color></size>\n");
        }

        private void Row(string label)
        {
            sb.Append("<color=#a8b0a6>").Append(label).Append("</color><pos=20%>");
        }

        /// <summary>Číslo v pevné šířce znaku a zarovnané doprava, aby sloupce při změně neposkakovaly.</summary>
        private static string Mono(string value, int width)
        {
            if (value.Length < width) value = new string(' ', width - value.Length) + value;   // v mspace má mezera šířku číslice
            return "<mspace=0.7em>" + value + "</mspace>";
        }

        private static string Cardinal(float yaw)
        {
            string[] n = { "N", "NE", "E", "SE", "S", "SW", "W", "NW" };
            string[] axis = { "+Z", "+X +Z", "+X", "+X -Z", "-Z", "-X -Z", "-X", "-X +Z" };
            int i = Mathf.RoundToInt(yaw / 45f) % 8;
            return n[i] + " <color=#838b82>(" + axis[i] + ")</color>";
        }

        private static string MicroName(MicroBiome m) => m switch
        {
            MicroBiome.GoldenGrove => "Golden Grove",
            MicroBiome.BasaltField => "Basalt Field",
            _ => m.ToString(),
        };

        private static string Capital(string s) => string.IsNullOrEmpty(s) ? s : char.ToUpperInvariant(s[0]) + s.Substring(1);

        // =====================================================================
        // Stavba UI (jen jednou, při prvním zobrazení)
        // =====================================================================

        /// <summary>Uses the authored overlay prefab (F3Diagnostics in the Game scene).</summary>
        private void Build()
        {
            built = true;
            if (overlay == null) overlay = FindFirstObjectByType<F3Overlay>(FindObjectsInactive.Include);
            if (overlay == null)
            {
                var prefab = Resources.Load<F3Overlay>(F3Overlay.PrefabPath);
                if (prefab != null) overlay = Instantiate(prefab);
            }
            if (overlay == null) { Debug.LogError("[F3] F3Diagnostics prefab is missing."); return; }
            canvas = overlay.canvas; panel = overlay.panel; group = overlay.group; header = overlay.header; body = overlay.body;
            header.text = "DIAGNOSTICS <size=60%><color=#838b82>  BETA " + Application.version + "</color></size>";
            group.alpha = 0f;
            canvas.enabled = false;
        }

        // =====================================================================
        // Staré veřejné API – bez volajících; ponecháno jako neškodný adaptér.
        // =====================================================================

        [System.Obsolete("Starý F3 panel byl odstraněn; F3 overlay čte data sám.")]
        public void UpdateBiome(BiomeData biome) { }

        [System.Obsolete("Starý F3 panel byl odstraněn; F3 overlay čte data sám.")]
        public void UpdateBiome(BiomeType biome) { }

        [System.Obsolete("Starý F3 panel byl odstraněn; F3 overlay čte data sám.")]
        public void UpdateFPS(float avgFPS) { }
    }
}
