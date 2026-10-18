using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.Audio;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using Orivilon.Player;
using Orivilon.World.Generation;

namespace Orivilon.UI.Settings
{
    /// <summary>
    /// Per-user game settings: loads/saves <see cref="SettingsData"/> as JSON in
    /// persistentDataPath/settings.json (versioned, validated, written atomically) and applies them to the
    /// live systems. Survives scene changes (DontDestroyOnLoad) and re-applies to anything that is
    /// recreated – a new player controller, camera or terrain streamer is picked up automatically.
    ///
    /// <para>What is applied, and where:</para>
    /// <list type="bullet">
    /// <item>Display mode / resolution – Screen (only on startup and when the user changes them).</item>
    /// <item>VSync / frame limit – QualitySettings.vSyncCount, Application.targetFrameRate (VSync wins).</item>
    /// <item>Quality preset – QualitySettings level (each level has its own URP asset; no asset is edited).</item>
    /// <item>Shadows – per camera (UniversalAdditionalCameraData.renderShadows), not the shared URP asset.</item>
    /// <item>Detail distance – VoxelTerrain.UpdateRenderDistance (full-detail chunk radius).</item>
    /// <item>Master volume / mute when unfocused – AudioListener.volume (the project has no audio mixer).</item>
    /// <item>Mouse sensitivity, invert Y, field of view – FirstPersonController (sprint FOV keeps its offset).</item>
    /// </list>
    /// </summary>
    public class SettingsManager : MonoBehaviour
    {
        public const int CurrentVersion = 2;

        public static SettingsManager Instance { get; private set; }

        [Header("Audio")]
        [Tooltip("Optional AudioMixer with exposed Master/Music/SFX/Ambient/UI parameters. The project has none; master volume uses AudioListener.volume.")]
        public AudioMixer audioMixer;

        [Tooltip("Save to disk on every Apply().")]
        public bool saveOnApply = true;

        /// <summary>Active (applied) settings.</summary>
        public SettingsData Current { get; private set; } = new SettingsData();

        /// <summary>Last load result for QA/diagnostics ("defaults", "loaded", "migrated v0", "corrupt → defaults").</summary>
        public string LoadInfo { get; private set; } = "";

        public static readonly int[] FrameLimits = { -1, 30, 60, 120, 144, 165, 240 };
        public const float MinFov = 60f, MaxFov = 110f, MinSens = 0.1f, MaxSens = 10f;
        public const int MinDetail = 3, MaxDetail = 10;

        private static string SettingsFilePath => Path.Combine(Application.persistentDataPath, "settings.json");

        private bool focused = true;
        private Camera lastCamera;
        private FirstPersonController lastController;
        private VoxelTerrain lastTerrain;
        private float nextLiveCheck;
        private readonly Dictionary<FirstPersonController, float> sprintOffset = new Dictionary<FirstPersonController, float>();

        /// <summary>Creates the manager when a scene without one is played directly (e.g. Game in the editor).</summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void EnsureInstance()
        {
            if (Instance != null || FindFirstObjectByType<SettingsManager>(FindObjectsInactive.Include) != null) return;
            new GameObject("SettingsManager").AddComponent<SettingsManager>();
        }

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(this);
                return;
            }
            Instance = this;
            if (transform.parent != null) transform.SetParent(null, true);
            DontDestroyOnLoad(gameObject);

            Load();
            ApplyInternal(Current, applyDisplay: true, save: false);
            SceneManager.sceneLoaded += OnSceneLoaded;
        }

        private void OnDestroy()
        {
            if (Instance == this) { Instance = null; SceneManager.sceneLoaded -= OnSceneLoaded; }
        }

        private void OnSceneLoaded(Scene s, LoadSceneMode m) { nextLiveCheck = 0f; lastCamera = null; lastController = null; lastTerrain = null; }

        private void Update()
        {
            // re-apply to systems that were (re)created: new camera, player controller or terrain
            if (Time.unscaledTime < nextLiveCheck) return;
            nextLiveCheck = Time.unscaledTime + 0.5f;
            var cam = Camera.main;
            var fpc = FindFirstObjectByType<FirstPersonController>();
            var terrain = VoxelTerrain.instance;
            if (cam != lastCamera || fpc != lastController || terrain != lastTerrain)
            {
                lastCamera = cam; lastController = fpc; lastTerrain = terrain;
                ApplyLive(Current);
            }
        }

        // ─── public API ──────────────────────────────────────────────────────

        /// <summary>Applies <see cref="Current"/> to everything (and saves when <see cref="saveOnApply"/>).</summary>
        public void Apply() => ApplyInternal(Current, applyDisplay: true, save: saveOnApply);

        /// <summary>Makes <paramref name="data"/> the active settings: validates, applies, saves.
        /// Display mode/resolution are only touched when they differ from the active ones.</summary>
        public void Apply(SettingsData data) => Apply(data, false);

        /// <param name="holdDisplayUntilConfirmed">True: a changed display mode/resolution is applied now
        /// but saved only after <see cref="ConfirmDisplay"/> – quitting during the keep/revert countdown
        /// cannot leave an unusable display stored.</param>
        public void Apply(SettingsData data, bool holdDisplayUntilConfirmed)
        {
            var v = Clone(data); Validate(v);
            bool displayChanged = DisplayDiffers(v, Current);
            var previous = Current;
            Current = v;
            ApplyInternal(Current, displayChanged, false);
            if (!saveOnApply) return;
            if (holdDisplayUntilConfirmed && displayChanged)
            {
                var stored = Clone(Current);
                stored.fullscreenMode = previous.fullscreenMode; stored.resolutionWidth = previous.resolutionWidth; stored.resolutionHeight = previous.resolutionHeight;
                Write(stored);
            }
            else Save();
        }

        /// <summary>Stores the display settings that are already active (keep/revert confirmed).</summary>
        public void ConfirmDisplay() => Save();

        public void Save() => Write(Current);

        private static void Write(SettingsData data)
        {
            try
            {
                data.version = CurrentVersion;
                string json = JsonUtility.ToJson(data, true);
                string tmp = SettingsFilePath + ".tmp";
                File.WriteAllText(tmp, json);
                if (File.Exists(SettingsFilePath)) File.Replace(tmp, SettingsFilePath, null);
                else File.Move(tmp, SettingsFilePath);
            }
            catch (System.Exception e) { Debug.LogWarning("[SettingsManager] Saving settings failed: " + e.Message); }
        }

        public void Load()
        {
            var d = new SettingsData();
            LoadInfo = "defaults";
            if (File.Exists(SettingsFilePath))
            {
                try
                {
                    string json = File.ReadAllText(SettingsFilePath);
                    var raw = new SettingsData { version = 0 };
                    JsonUtility.FromJsonOverwrite(json, raw);
                    if (!json.Contains("\"version\"")) raw.version = 0;
                    if (raw.version < CurrentVersion)
                    {
                        // v0/v1: only what the old manager really applied is kept (display, sync, quality,
                        // master volume). Player/terrain values were never applied in game – the scene
                        // values stay the defaults.
                        d.qualityLevel = raw.qualityLevel; d.vSync = raw.vSync; d.targetFrameRate = raw.targetFrameRate;
                        d.fullscreenMode = raw.fullscreenMode; d.masterVolume = raw.masterVolume; d.muteWhenUnfocused = raw.muteWhenUnfocused;
                        LoadInfo = "migrated v" + raw.version;
                    }
                    else { d = raw; LoadInfo = "loaded"; }
                }
                catch (System.Exception ex)
                {
                    Debug.LogWarning("[SettingsManager] settings.json is unreadable (" + ex.Message + "). Using defaults.");
                    d = new SettingsData();
                    LoadInfo = "corrupt → defaults";
                }
            }
            Validate(d);
            Current = d;
        }

        /// <summary>Resets to defaults and applies (used by the old menu; the new screen resets its working copy).</summary>
        public void ResetToDefaults() => Apply(new SettingsData());

        public static SettingsData Clone(SettingsData d) => JsonUtility.FromJson<SettingsData>(JsonUtility.ToJson(d));

        /// <summary>Clamps everything into supported ranges; unknown values fall back to defaults.</summary>
        public static void Validate(SettingsData d)
        {
            var def = new SettingsData();
            d.version = CurrentVersion;
            int levels = Mathf.Max(1, QualitySettings.names.Length);
            if (d.qualityLevel < 0 || d.qualityLevel >= levels) d.qualityLevel = Mathf.Clamp(def.qualityLevel, 0, levels - 1);
            d.vSync = d.vSync > 0 ? 1 : 0;
            if (System.Array.IndexOf(FrameLimits, d.targetFrameRate) < 0) d.targetFrameRate = -1;
            if (d.fullscreenMode < 0 || d.fullscreenMode > 2) d.fullscreenMode = def.fullscreenMode;
            if (d.resolutionWidth != 0 || d.resolutionHeight != 0)
            {
                bool ok = false;
                foreach (var r in Screen.resolutions) if (r.width == d.resolutionWidth && r.height == d.resolutionHeight) { ok = true; break; }
                if (!ok || d.resolutionWidth < 640 || d.resolutionHeight < 360) { d.resolutionWidth = 0; d.resolutionHeight = 0; }
            }
            d.masterVolume = float.IsNaN(d.masterVolume) ? def.masterVolume : Mathf.Clamp01(d.masterVolume);
            d.mouseSensitivity = float.IsNaN(d.mouseSensitivity) ? def.mouseSensitivity : Mathf.Clamp(d.mouseSensitivity, MinSens, MaxSens);
            d.fieldOfView = float.IsNaN(d.fieldOfView) ? def.fieldOfView : Mathf.Clamp(d.fieldOfView, MinFov, MaxFov);
            d.renderDistance = Mathf.Clamp(d.renderDistance, MinDetail, MaxDetail);
            if (d.mtWheelSearchOrder < 0 || d.mtWheelSearchOrder > 1) d.mtWheelSearchOrder = def.mtWheelSearchOrder;
            if (d.mtWheelScrollDirection < 0 || d.mtWheelScrollDirection > 2) d.mtWheelScrollDirection = def.mtWheelScrollDirection;
            if (d.mtScrollItemScaling < 0 || d.mtScrollItemScaling > 1) d.mtScrollItemScaling = def.mtScrollItemScaling;
        }

        public static bool DisplayDiffers(SettingsData a, SettingsData b) =>
            a.fullscreenMode != b.fullscreenMode || a.resolutionWidth != b.resolutionWidth || a.resolutionHeight != b.resolutionHeight;

        public static FullScreenMode ModeOf(int code) =>
            code == 1 ? FullScreenMode.Windowed : code == 2 ? FullScreenMode.FullScreenWindow : FullScreenMode.ExclusiveFullScreen;

        // ─── applying ────────────────────────────────────────────────────────

        private void ApplyInternal(SettingsData d, bool applyDisplay, bool save)
        {
            QualitySettings.SetQualityLevel(d.qualityLevel, true);
            QualitySettings.vSyncCount = d.vSync;                       // after SetQualityLevel (it resets vSync)
            Application.targetFrameRate = d.vSync > 0 ? -1 : d.targetFrameRate;
            if (applyDisplay) ApplyDisplay(d);
            ApplyAudio(d);
            ApplyLive(d);
            if (save) Save();
        }

        private static void ApplyDisplay(SettingsData d)
        {
            var mode = ModeOf(d.fullscreenMode);
            if (d.resolutionWidth > 0 && d.resolutionHeight > 0)
                Screen.SetResolution(d.resolutionWidth, d.resolutionHeight, mode);
            else
                Screen.fullScreenMode = mode;
        }

        private void ApplyAudio(SettingsData d)
        {
            AudioListener.volume = (!focused && d.muteWhenUnfocused) ? 0f : d.masterVolume;
            if (audioMixer != null)
            {
                SetMixerVolume("Music", d.musicVolume); SetMixerVolume("SFX", d.sfxVolume);
                SetMixerVolume("Ambient", d.ambientVolume); SetMixerVolume("UI", d.uiVolume);
            }
        }

        private void SetMixerVolume(string param, float linear)
        {
            float db = linear <= 0.0001f ? -80f : Mathf.Log10(linear) * 20f;
            audioMixer.SetFloat(param, db);
        }

        /// <summary>Player controller, cameras and terrain – everything that lives in the game scene.</summary>
        private void ApplyLive(SettingsData d)
        {
            // only the view camera(s) – helper cameras (minimap, previews) keep their own shadow choice
            var fpc = FindFirstObjectByType<FirstPersonController>(FindObjectsInactive.Include);
            SetShadows(Camera.main, d.shadows);
            if (fpc != null && fpc.playerCamera != null && fpc.playerCamera != Camera.main) SetShadows(fpc.playerCamera, d.shadows);

            if (fpc != null)
            {
                if (!sprintOffset.ContainsKey(fpc)) sprintOffset[fpc] = fpc.sprintFOV - fpc.fov;
                fpc.mouseSensitivity = d.mouseSensitivity;
                fpc.invertCamera = d.invertMouseY;
                fpc.fov = d.fieldOfView;
                fpc.sprintFOV = d.fieldOfView + sprintOffset[fpc];
                if (fpc.playerCamera != null) fpc.playerCamera.fieldOfView = d.fieldOfView;
            }

            var terrain = VoxelTerrain.instance;
            if (terrain != null && terrain.renderDistance != d.renderDistance) terrain.UpdateRenderDistance(d.renderDistance);
        }

        private static void SetShadows(Camera cam, bool on)
        {
            if (cam == null) return;   // only the view camera is passed in (it may be rendering into the blur RT right now)
            var data = cam.GetComponent<UniversalAdditionalCameraData>();
            if (data != null) data.renderShadows = on;
        }

        private void OnApplicationFocus(bool hasFocus)
        {
            focused = hasFocus;
            ApplyAudio(Current);
        }
    }
}
