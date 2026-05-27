using System.IO;
using UnityEngine;
using UnityEngine.Audio;
using Orivilon.Player;
using Orivilon.World.Terrain;

namespace Orivilon.UI.Settings
{
    /// <summary>
    /// Centrální singleton spravující veškerá herní nastavení.
    /// Načítá a ukládá SettingsData jako JSON do persistentDataPath/settings.json.
    /// Aplikuje nastavení na všechny relevantní systémy (grafika, zvuk, hráč).
    /// Přežívá přechody mezi scénami (DontDestroyOnLoad).
    ///
    /// Použití:
    ///   SettingsManager.Instance.Current.mouseSensitivity = 3f;
    ///   SettingsManager.Instance.Apply();
    ///   SettingsManager.Instance.Save();
    /// </summary>
    public class SettingsManager : MonoBehaviour
    {
        // ─── Singleton ───────────────────────────────────────────────────────────
        public static SettingsManager Instance { get; private set; }

        // ─── Inspector reference ─────────────────────────────────────────────────
        [Header("Audio")]
        [Tooltip("AudioMixer pro kontrolu hlasitosti kanálů. Musí mít exposed parametry: Master, Music, SFX, Ambient, UI")]
        public AudioMixer audioMixer;

        [Header("Auto-save")]
        [Tooltip("Zda se nastavení automaticky uloží po každém Apply().")]
        public bool saveOnApply = true;

        // ─── Data ─────────────────────────────────────────────────────────────────
        /// <summary>Aktuálně aktivní nastavení.</summary>
        public SettingsData Current { get; private set; } = new SettingsData();

        // ─── Cesta souboru ────────────────────────────────────────────────────────
        private static string SettingsFilePath =>
            Path.Combine(Application.persistentDataPath, "settings.json");

        // ─── Auto-save timer ──────────────────────────────────────────────────────
        private float _autoSaveTimer;

        // ─── Unity lifecycle ──────────────────────────────────────────────────────
        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(this);
                return;
            }
            Instance = this;
            DontDestroyOnLoad(gameObject);

            Load();
            Apply();
        }

        private void Update()
        {
            HandleAutoSave();
        }

        // ─── Veřejné API ──────────────────────────────────────────────────────────

        /// <summary>
        /// Aplikuje všechna aktuální nastavení na herní systémy.
        /// Volat po každé změně hodnot v Current.
        /// </summary>
        public void Apply()
        {
            ApplyGraphics();
            ApplyAudio();
            ApplyPlayer();
            ApplyGameplay();

            if (saveOnApply)
                Save();

            Debug.Log("[SettingsManager] Nastavení aplikována.");
        }

        /// <summary>Uloží aktuální nastavení do JSON souboru.</summary>
        public void Save()
        {
            string json = JsonUtility.ToJson(Current, true);
            File.WriteAllText(SettingsFilePath, json);
            Debug.Log($"[SettingsManager] Uloženo do: {SettingsFilePath}");
        }

        /// <summary>Načte nastavení ze souboru. Pokud soubor neexistuje, použije výchozí hodnoty.</summary>
        public void Load()
        {
            if (File.Exists(SettingsFilePath))
            {
                string json = File.ReadAllText(SettingsFilePath);
                try
                {
                    JsonUtility.FromJsonOverwrite(json, Current);
                    Debug.Log("[SettingsManager] Nastavení načtena ze souboru.");
                }
                catch (System.Exception ex)
                {
                    Debug.LogWarning($"[SettingsManager] Chyba při načítání: {ex.Message}. Použijí se výchozí hodnoty.");
                    Current = new SettingsData();
                }
            }
            else
            {
                Debug.Log("[SettingsManager] Soubor nastavení nenalezen – použijí se výchozí hodnoty.");
                Current = new SettingsData();
            }
        }

        /// <summary>Resetuje veškerá nastavení na výchozí hodnoty a aplikuje je.</summary>
        public void ResetToDefaults()
        {
            Current = new SettingsData();
            Apply();
        }

        // ─── Grafika ─────────────────────────────────────────────────────────────

        private void ApplyGraphics()
        {
            QualitySettings.SetQualityLevel(Current.qualityLevel, true);
            QualitySettings.vSyncCount = Current.vSync;
            Application.targetFrameRate = Current.targetFrameRate;
            QualitySettings.shadowDistance = Current.shadowDistance;
            QualitySettings.lodBias = Current.lodBias;
            QualitySettings.antiAliasing = Current.antiAliasing;
            QualitySettings.anisotropicFiltering =
                Current.anisotropicFiltering > 0 ? AnisotropicFiltering.Enable : AnisotropicFiltering.Disable;

            switch (Current.shadowQuality)
            {
                case 0: QualitySettings.shadows = ShadowQuality.Disable; break;
                case 1: QualitySettings.shadows = ShadowQuality.HardOnly; break;
                default: QualitySettings.shadows = ShadowQuality.All; break;
            }

            ApplyResolution();
        }

        private void ApplyResolution()
        {
            FullScreenMode mode;
            switch (Current.fullscreenMode)
            {
                case 1: mode = FullScreenMode.Windowed; break;
                case 2: mode = FullScreenMode.FullScreenWindow; break;
                default: mode = FullScreenMode.ExclusiveFullScreen; break;
            }

            Resolution[] resolutions = Screen.resolutions;
            if (Current.resolutionIndex >= 0 && Current.resolutionIndex < resolutions.Length)
            {
                Resolution r = resolutions[Current.resolutionIndex];
                Screen.SetResolution(r.width, r.height, mode, r.refreshRateRatio);
            }
            else
            {
                Screen.fullScreenMode = mode;
            }
        }

        // ─── Zvuk ────────────────────────────────────────────────────────────────

        private void ApplyAudio()
        {
            if (audioMixer == null) return;

            SetMixerVolume("Master", Current.masterVolume);
            SetMixerVolume("Music", Current.musicVolume);
            SetMixerVolume("SFX", Current.sfxVolume);
            SetMixerVolume("Ambient", Current.ambientVolume);
            SetMixerVolume("UI", Current.uiVolume);
        }

        private void SetMixerVolume(string paramName, float linear)
        {
            float db = linear <= 0.0001f ? -80f : Mathf.Log10(linear) * 20f;
            audioMixer.SetFloat(paramName, db);
        }

        // ─── Hráč ────────────────────────────────────────────────────────────────

        private void ApplyPlayer()
        {
#pragma warning disable CS0618
            FirstPersonController player = FindObjectOfType<FirstPersonController>();
#pragma warning restore CS0618
            if (player == null) return;

            player.mouseSensitivity = Current.mouseSensitivity;
            player.invertCamera = Current.invertMouseY;
            player.fov = Current.fieldOfView;
            player.enableHeadBob = Current.headBob;
            player.sprintSpeed = 7f * Current.sprintSpeedMultiplier;
            player.jumpPower = 5f * Current.jumpPowerMultiplier;
            player.useSprintBar = Current.showSprintBar;
            player.crosshair = Current.showCrosshair;

            if (player.playerCamera != null)
                player.playerCamera.fieldOfView = Current.fieldOfView;

            if (TryParseKey(Current.sprintKey, out KeyCode sprint)) player.sprintKey = sprint;
            if (TryParseKey(Current.jumpKey, out KeyCode jump)) player.jumpKey = jump;
            if (TryParseKey(Current.crouchKey, out KeyCode crouch)) player.crouchKey = crouch;
            if (TryParseKey(Current.zoomKey, out KeyCode zoom)) player.zoomKey = zoom;
            if (TryParseKey(Current.flightKey, out KeyCode flight)) player.flightKey = flight;
        }

        // ─── Gameplay ────────────────────────────────────────────────────────────

        private void ApplyGameplay()
        {
#pragma warning disable CS0618
            PlayerNeeds needs = FindObjectOfType<PlayerNeeds>();
#pragma warning restore CS0618
            if (needs != null)
            {
                needs.foodDrainPerSecond = 0.5f * Current.hungerRate;
                needs.waterDrainPerSecond = 0.7f * Current.thirstRate;
            }

#pragma warning disable CS0618
            EndlessTerrain terrain = FindObjectOfType<EndlessTerrain>();
#pragma warning restore CS0618
            if (terrain != null)
            {
                terrain.renderDistance = Current.renderDistance;
            }
        }

        // ─── Auto-save ────────────────────────────────────────────────────────────

        private void HandleAutoSave()
        {
            if (Current.autoSaveInterval <= 0) return;

            _autoSaveTimer += Time.unscaledDeltaTime;
            if (_autoSaveTimer >= Current.autoSaveInterval * 60f)
            {
                _autoSaveTimer = 0f;
                Save();
            }
        }

        // ─── Utilita ─────────────────────────────────────────────────────────────

        private static bool TryParseKey(string keyName, out KeyCode keyCode)
        {
            if (System.Enum.TryParse(keyName, true, out keyCode))
                return true;

            keyCode = KeyCode.None;
            Debug.LogWarning($"[SettingsManager] Nelze parsovat klávesou: '{keyName}'");
            return false;
        }

        private void OnApplicationFocus(bool hasFocus)
        {
            if (!Current.muteWhenUnfocused) return;
            AudioListener.volume = hasFocus ? 1f : 0f;
        }
    }
}
