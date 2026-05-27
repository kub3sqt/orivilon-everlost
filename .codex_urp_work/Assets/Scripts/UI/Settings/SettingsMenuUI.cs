using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Orivilon.UI.Settings
{
    /// <summary>
    /// UI Controller pro menu nastavení hry.
    /// Pracuje se záložkami (grafika, zvuk, ovládání, gameplay, přístupnost).
    /// Načítá hodnoty ze SettingsManager.Instance.Current při otevření,
    /// aplikuje a ukládá je po stisku tlačítka Uložit.
    ///
    /// Přiřaď reference přes Inspector, pak zavolej ShowSettings() / HideSettings().
    ///
    /// STRUKTURA UI (Canvas):
    ///   SettingsPanel
    ///     TabButtons (Graphics, Audio, Controls, Gameplay, Accessibility)
    ///     TabPanels
    ///       GraphicsPanel
    ///       AudioPanel
    ///       ControlsPanel
    ///       GameplayPanel
    ///       AccessibilityPanel
    ///     Footer
    ///       SaveButton
    ///       ResetButton
    ///       CloseButton
    /// </summary>
    public class SettingsMenuUI : MonoBehaviour
    {
        // ─── Kořen panelu ────────────────────────────────────────────────────────
        [Header("Root")]
        public GameObject settingsPanel;

        // ─── Záložky ─────────────────────────────────────────────────────────────
        [Header("Tab Panels")]
        public GameObject graphicsPanel;
        public GameObject audioPanel;
        public GameObject controlsPanel;
        public GameObject gameplayPanel;
        public GameObject accessibilityPanel;

        [Header("Tab Buttons")]
        public Button graphicsTabBtn;
        public Button audioTabBtn;
        public Button controlsTabBtn;
        public Button gameplayTabBtn;
        public Button accessibilityTabBtn;

        // ─── Footer tlačítka ─────────────────────────────────────────────────────
        [Header("Footer Buttons")]
        public Button saveButton;
        public Button resetButton;
        public Button closeButton;

        // ══════════════════════════════════════════════════════════════════════════
        // GRAFIKA
        // ══════════════════════════════════════════════════════════════════════════
        [Header("Graphics – UI Elements")]
        public TMP_Dropdown qualityDropdown;       // Very Low / Low / Medium / High / Very High / Ultra
        public Slider renderDistanceSlider;
        public TextMeshProUGUI renderDistanceLabel;
        public TMP_Dropdown resolutionDropdown;
        public TMP_Dropdown fullscreenDropdown;    // Fullscreen / Windowed / Borderless
        public Toggle vSyncToggle;
        public TMP_Dropdown fpsLimitDropdown;      // Unlimited / 30 / 60 / 120 / 144 / 165 / 240
        public Slider shadowDistanceSlider;
        public TextMeshProUGUI shadowDistanceLabel;
        public TMP_Dropdown shadowQualityDropdown; // Vypnuto / Hard / Soft
        public TMP_Dropdown antiAliasingDropdown;  // Off / 2x / 4x / 8x
        public Slider lodBiasSlider;
        public TextMeshProUGUI lodBiasLabel;
        public Slider grassDensitySlider;
        public TextMeshProUGUI grassDensityLabel;
        public Toggle ambientOcclusionToggle;
        public Toggle bloomToggle;
        public Toggle depthOfFieldToggle;
        public Toggle motionBlurToggle;
        public Slider brightnessSlider;
        public TextMeshProUGUI brightnessLabel;
        public Slider fovSlider;
        public TextMeshProUGUI fovLabel;

        // ══════════════════════════════════════════════════════════════════════════
        // ZVUK
        // ══════════════════════════════════════════════════════════════════════════
        [Header("Audio – UI Elements")]
        public Slider masterVolumeSlider;
        public TextMeshProUGUI masterVolumeLabel;
        public Slider musicVolumeSlider;
        public TextMeshProUGUI musicVolumeLabel;
        public Slider sfxVolumeSlider;
        public TextMeshProUGUI sfxVolumeLabel;
        public Slider ambientVolumeSlider;
        public TextMeshProUGUI ambientVolumeLabel;
        public Slider uiVolumeSlider;
        public TextMeshProUGUI uiVolumeLabel;
        public Toggle muteUnfocusedToggle;

        // ══════════════════════════════════════════════════════════════════════════
        // OVLÁDÁNÍ
        // ══════════════════════════════════════════════════════════════════════════
        [Header("Controls – UI Elements")]
        public Slider sensitivitySlider;
        public TextMeshProUGUI sensitivityLabel;
        public Toggle invertYToggle;
        public Toggle invertXToggle;
        public Toggle sprintToggleToggle;   // toggle vs hold
        public Toggle crouchToggleToggle;   // toggle vs hold
        // Klávesové mapování – každý řádek = label + button pro rebind
        public TextMeshProUGUI sprintKeyLabel;
        public Button sprintKeyBtn;
        public TextMeshProUGUI jumpKeyLabel;
        public Button jumpKeyBtn;
        public TextMeshProUGUI crouchKeyLabel;
        public Button crouchKeyBtn;
        public TextMeshProUGUI interactKeyLabel;
        public Button interactKeyBtn;
        public TextMeshProUGUI inventoryKeyLabel;
        public Button inventoryKeyBtn;
        public TextMeshProUGUI zoomKeyLabel;
        public Button zoomKeyBtn;

        // ══════════════════════════════════════════════════════════════════════════
        // GAMEPLAY
        // ══════════════════════════════════════════════════════════════════════════
        [Header("Gameplay – UI Elements")]
        public TMP_Dropdown difficultyDropdown;   // Mírná / Normální / Těžká / Hardcore
        public Slider hungerRateSlider;
        public TextMeshProUGUI hungerRateLabel;
        public Slider thirstRateSlider;
        public TextMeshProUGUI thirstRateLabel;
        public Toggle headBobToggle;
        public Toggle showHUDToggle;
        public Toggle showSprintBarToggle;
        public Toggle showCrosshairToggle;
        public Toggle showFPSToggle;
        public Toggle showCoordsToggle;
        public TMP_Dropdown autoSaveDropdown;     // Vypnuto / 1min / 5min / 10min / 15min / 30min

        // ══════════════════════════════════════════════════════════════════════════
        // PŘÍSTUPNOST
        // ══════════════════════════════════════════════════════════════════════════
        [Header("Accessibility – UI Elements")]
        public Slider fontScaleSlider;
        public TextMeshProUGUI fontScaleLabel;
        public TMP_Dropdown colorBlindDropdown;   // Vypnuto / Deuteranopia / Protanopia / Tritanopia
        public Toggle reducedMotionToggle;
        public Toggle subtitlesToggle;
        public TMP_Dropdown languageDropdown;

        // ─── Privátní stav ────────────────────────────────────────────────────────
        private bool _isRebinding;
        private string _rebindTarget;
        private SettingsData _working; // pracovní kopie, aplikuje se jen po Save

        // ─── Dostupná rozlišení ───────────────────────────────────────────────────
        private Resolution[] _resolutions;

        // ─── FPS limity ───────────────────────────────────────────────────────────
        private static readonly int[] FpsLimits = { -1, 30, 60, 120, 144, 165, 240 };

        // ─── Auto-save intervaly (minuty) ─────────────────────────────────────────
        private static readonly int[] AutoSaveMinutes = { 0, 1, 5, 10, 15, 30 };

        // ══════════════════════════════════════════════════════════════════════════
        // Unity lifecycle
        // ══════════════════════════════════════════════════════════════════════════

        private void Awake()
        {
            settingsPanel?.SetActive(false);
            WireTabButtons();
            WireFooterButtons();
            WireRebindButtons();
        }

        // ══════════════════════════════════════════════════════════════════════════
        // Veřejné API
        // ══════════════════════════════════════════════════════════════════════════

        /// <summary>Otevře menu nastavení a načte aktuální hodnoty.</summary>
        public void ShowSettings()
        {
            // Pracovní kopie – změny se projeví až po uložení
            string json = JsonUtility.ToJson(SettingsManager.Instance.Current);
            _working = JsonUtility.FromJson<SettingsData>(json);

            settingsPanel?.SetActive(true);
            PopulateDropdowns();
            RefreshUI();
            OpenTab(graphicsPanel);
        }

        /// <summary>Zavře menu bez uložení změn.</summary>
        public void HideSettings()
        {
            settingsPanel?.SetActive(false);
        }

        // ══════════════════════════════════════════════════════════════════════════
        // Záložky
        // ══════════════════════════════════════════════════════════════════════════

        private void WireTabButtons()
        {
            graphicsTabBtn?.onClick.AddListener(() => OpenTab(graphicsPanel));
            audioTabBtn?.onClick.AddListener(() => OpenTab(audioPanel));
            controlsTabBtn?.onClick.AddListener(() => OpenTab(controlsPanel));
            gameplayTabBtn?.onClick.AddListener(() => OpenTab(gameplayPanel));
            accessibilityTabBtn?.onClick.AddListener(() => OpenTab(accessibilityPanel));
        }

        private void OpenTab(GameObject panel)
        {
            graphicsPanel?.SetActive(false);
            audioPanel?.SetActive(false);
            controlsPanel?.SetActive(false);
            gameplayPanel?.SetActive(false);
            accessibilityPanel?.SetActive(false);
            panel?.SetActive(true);
        }

        // ══════════════════════════════════════════════════════════════════════════
        // Footer
        // ══════════════════════════════════════════════════════════════════════════

        private void WireFooterButtons()
        {
            saveButton?.onClick.AddListener(OnSave);
            resetButton?.onClick.AddListener(OnReset);
            closeButton?.onClick.AddListener(HideSettings);
        }

        private void OnSave()
        {
            ReadUIIntoWorking();
            // Přepíše Current pracovní kopií
            JsonUtility.FromJsonOverwrite(JsonUtility.ToJson(_working), SettingsManager.Instance.Current);
            SettingsManager.Instance.Apply();
            HideSettings();
        }

        private void OnReset()
        {
            _working = new SettingsData();
            RefreshUI();
        }

        // ══════════════════════════════════════════════════════════════════════════
        // Naplnění dropdownů (jednou při otevření)
        // ══════════════════════════════════════════════════════════════════════════

        private void PopulateDropdowns()
        {
            // Rozlišení
            if (resolutionDropdown != null)
            {
                _resolutions = Screen.resolutions;
                resolutionDropdown.ClearOptions();
                var opts = new List<string>();
                foreach (var r in _resolutions)
                    opts.Add($"{r.width}×{r.height} ({r.refreshRateRatio.value:F0} Hz)");
                resolutionDropdown.AddOptions(opts);
            }

            // Kvalita
            if (qualityDropdown != null)
            {
                qualityDropdown.ClearOptions();
                qualityDropdown.AddOptions(new List<string>(QualitySettings.names));
            }

            // Fullscreen
            fullscreenDropdown?.ClearOptions();
            fullscreenDropdown?.AddOptions(new List<string> { "Fullscreen", "Windowed", "Borderless" });

            // FPS limit
            if (fpsLimitDropdown != null)
            {
                fpsLimitDropdown.ClearOptions();
                var opts = new List<string>();
                foreach (int f in FpsLimits)
                    opts.Add(f < 0 ? "Neomezeno" : $"{f} FPS");
                fpsLimitDropdown.AddOptions(opts);
            }

            // Stíny
            shadowQualityDropdown?.ClearOptions();
            shadowQualityDropdown?.AddOptions(new List<string> { "Vypnuto", "Hard", "Soft" });

            // AA
            antiAliasingDropdown?.ClearOptions();
            antiAliasingDropdown?.AddOptions(new List<string> { "Vypnuto", "2×MSAA", "4×MSAA", "8×MSAA" });

            // Obtížnost
            difficultyDropdown?.ClearOptions();
            difficultyDropdown?.AddOptions(new List<string> { "Mírná", "Normální", "Těžká", "Hardcore" });

            // Auto-save
            if (autoSaveDropdown != null)
            {
                autoSaveDropdown.ClearOptions();
                var opts = new List<string>();
                foreach (int m in AutoSaveMinutes)
                    opts.Add(m == 0 ? "Vypnuto" : $"{m} min");
                autoSaveDropdown.AddOptions(opts);
            }

            // Barvoslepost
            colorBlindDropdown?.ClearOptions();
            colorBlindDropdown?.AddOptions(new List<string> { "Vypnuto", "Deuteranopia", "Protanopia", "Tritanopia" });

            // Jazyk
            languageDropdown?.ClearOptions();
            languageDropdown?.AddOptions(new List<string> { "English", "Čeština", "Deutsch", "Español", "Français" });
        }

        // ══════════════════════════════════════════════════════════════════════════
        // Načtení hodnot z _working do UI
        // ══════════════════════════════════════════════════════════════════════════

        private void RefreshUI()
        {
            if (_working == null) return;

            // ── Grafika ──
            if (qualityDropdown != null) qualityDropdown.value = _working.qualityLevel;
            SetSlider(renderDistanceSlider, renderDistanceLabel, _working.renderDistance, "Vzdálenost renderu", 0);
            if (resolutionDropdown != null) resolutionDropdown.value = Mathf.Max(0, _working.resolutionIndex);
            if (fullscreenDropdown != null) fullscreenDropdown.value = _working.fullscreenMode;
            if (vSyncToggle != null) vSyncToggle.isOn = _working.vSync > 0;
            if (fpsLimitDropdown != null) fpsLimitDropdown.value = FpsLimitIndex(_working.targetFrameRate);
            SetSlider(shadowDistanceSlider, shadowDistanceLabel, _working.shadowDistance, "Stíny", 0);
            if (shadowQualityDropdown != null) shadowQualityDropdown.value = _working.shadowQuality;
            if (antiAliasingDropdown != null) antiAliasingDropdown.value = AaIndex(_working.antiAliasing);
            SetSlider(lodBiasSlider, lodBiasLabel, _working.lodBias, "LOD", 2);
            SetSlider(grassDensitySlider, grassDensityLabel, _working.grassDensity, "Vegetace", 2);
            if (ambientOcclusionToggle != null) ambientOcclusionToggle.isOn = _working.ambientOcclusion;
            if (bloomToggle != null) bloomToggle.isOn = _working.bloom;
            if (depthOfFieldToggle != null) depthOfFieldToggle.isOn = _working.depthOfField;
            if (motionBlurToggle != null) motionBlurToggle.isOn = _working.motionBlur;
            SetSlider(brightnessSlider, brightnessLabel, _working.brightness, "Jas", 2);
            SetSlider(fovSlider, fovLabel, _working.fieldOfView, "FOV", 0);

            // ── Zvuk ──
            SetSlider(masterVolumeSlider, masterVolumeLabel, _working.masterVolume * 100f, "Hlavní", 0);
            SetSlider(musicVolumeSlider, musicVolumeLabel, _working.musicVolume * 100f, "Hudba", 0);
            SetSlider(sfxVolumeSlider, sfxVolumeLabel, _working.sfxVolume * 100f, "Efekty", 0);
            SetSlider(ambientVolumeSlider, ambientVolumeLabel, _working.ambientVolume * 100f, "Ambient", 0);
            SetSlider(uiVolumeSlider, uiVolumeLabel, _working.uiVolume * 100f, "UI", 0);
            if (muteUnfocusedToggle != null) muteUnfocusedToggle.isOn = _working.muteWhenUnfocused;

            // ── Ovládání ──
            SetSlider(sensitivitySlider, sensitivityLabel, _working.mouseSensitivity, "Citlivost", 1);
            if (invertYToggle != null) invertYToggle.isOn = _working.invertMouseY;
            if (invertXToggle != null) invertXToggle.isOn = _working.invertMouseX;
            if (sprintToggleToggle != null) sprintToggleToggle.isOn = _working.sprintToggle;
            if (crouchToggleToggle != null) crouchToggleToggle.isOn = _working.crouchToggle;
            RefreshKeyLabels();

            // ── Gameplay ──
            if (difficultyDropdown != null) difficultyDropdown.value = _working.difficulty;
            SetSlider(hungerRateSlider, hungerRateLabel, _working.hungerRate, "Hlad ×", 2);
            SetSlider(thirstRateSlider, thirstRateLabel, _working.thirstRate, "Žízeň ×", 2);
            if (headBobToggle != null) headBobToggle.isOn = _working.headBob;
            if (showHUDToggle != null) showHUDToggle.isOn = _working.showHUD;
            if (showSprintBarToggle != null) showSprintBarToggle.isOn = _working.showSprintBar;
            if (showCrosshairToggle != null) showCrosshairToggle.isOn = _working.showCrosshair;
            if (showFPSToggle != null) showFPSToggle.isOn = _working.showFPS;
            if (showCoordsToggle != null) showCoordsToggle.isOn = _working.showCoordinates;
            if (autoSaveDropdown != null) autoSaveDropdown.value = AutoSaveIndex(_working.autoSaveInterval);

            // ── Přístupnost ──
            SetSlider(fontScaleSlider, fontScaleLabel, _working.uiFontScale, "Písmo ×", 2);
            if (colorBlindDropdown != null) colorBlindDropdown.value = _working.colorBlindMode;
            if (reducedMotionToggle != null) reducedMotionToggle.isOn = _working.reducedMotion;
            if (subtitlesToggle != null) subtitlesToggle.isOn = _working.showSubtitles;
        }

        // ══════════════════════════════════════════════════════════════════════════
        // Čtení UI do _working (před uložením)
        // ══════════════════════════════════════════════════════════════════════════

        private void ReadUIIntoWorking()
        {
            // ── Grafika ──
            if (qualityDropdown != null) _working.qualityLevel = qualityDropdown.value;
            if (renderDistanceSlider != null) _working.renderDistance = (int)renderDistanceSlider.value;
            if (resolutionDropdown != null) _working.resolutionIndex = resolutionDropdown.value;
            if (fullscreenDropdown != null) _working.fullscreenMode = fullscreenDropdown.value;
            if (vSyncToggle != null) _working.vSync = vSyncToggle.isOn ? 1 : 0;
            if (fpsLimitDropdown != null) _working.targetFrameRate = FpsLimits[fpsLimitDropdown.value];
            if (shadowDistanceSlider != null) _working.shadowDistance = shadowDistanceSlider.value;
            if (shadowQualityDropdown != null) _working.shadowQuality = shadowQualityDropdown.value;
            if (antiAliasingDropdown != null) _working.antiAliasing = AaValue(antiAliasingDropdown.value);
            if (lodBiasSlider != null) _working.lodBias = lodBiasSlider.value;
            if (grassDensitySlider != null) _working.grassDensity = grassDensitySlider.value;
            if (ambientOcclusionToggle != null) _working.ambientOcclusion = ambientOcclusionToggle.isOn;
            if (bloomToggle != null) _working.bloom = bloomToggle.isOn;
            if (depthOfFieldToggle != null) _working.depthOfField = depthOfFieldToggle.isOn;
            if (motionBlurToggle != null) _working.motionBlur = motionBlurToggle.isOn;
            if (brightnessSlider != null) _working.brightness = brightnessSlider.value;
            if (fovSlider != null) _working.fieldOfView = fovSlider.value;

            // ── Zvuk ──
            if (masterVolumeSlider != null) _working.masterVolume = masterVolumeSlider.value / 100f;
            if (musicVolumeSlider != null) _working.musicVolume = musicVolumeSlider.value / 100f;
            if (sfxVolumeSlider != null) _working.sfxVolume = sfxVolumeSlider.value / 100f;
            if (ambientVolumeSlider != null) _working.ambientVolume = ambientVolumeSlider.value / 100f;
            if (uiVolumeSlider != null) _working.uiVolume = uiVolumeSlider.value / 100f;
            if (muteUnfocusedToggle != null) _working.muteWhenUnfocused = muteUnfocusedToggle.isOn;

            // ── Ovládání ──
            if (sensitivitySlider != null) _working.mouseSensitivity = sensitivitySlider.value;
            if (invertYToggle != null) _working.invertMouseY = invertYToggle.isOn;
            if (invertXToggle != null) _working.invertMouseX = invertXToggle.isOn;
            if (sprintToggleToggle != null) _working.sprintToggle = sprintToggleToggle.isOn;
            if (crouchToggleToggle != null) _working.crouchToggle = crouchToggleToggle.isOn;
            // Klávesy se ukládají přímo při rebindu (viz OnRebindComplete)

            // ── Gameplay ──
            if (difficultyDropdown != null) _working.difficulty = difficultyDropdown.value;
            if (hungerRateSlider != null) _working.hungerRate = hungerRateSlider.value;
            if (thirstRateSlider != null) _working.thirstRate = thirstRateSlider.value;
            if (headBobToggle != null) _working.headBob = headBobToggle.isOn;
            if (showHUDToggle != null) _working.showHUD = showHUDToggle.isOn;
            if (showSprintBarToggle != null) _working.showSprintBar = showSprintBarToggle.isOn;
            if (showCrosshairToggle != null) _working.showCrosshair = showCrosshairToggle.isOn;
            if (showFPSToggle != null) _working.showFPS = showFPSToggle.isOn;
            if (showCoordsToggle != null) _working.showCoordinates = showCoordsToggle.isOn;
            if (autoSaveDropdown != null) _working.autoSaveInterval = AutoSaveMinutes[autoSaveDropdown.value];

            // ── Přístupnost ──
            if (fontScaleSlider != null) _working.uiFontScale = fontScaleSlider.value;
            if (colorBlindDropdown != null) _working.colorBlindMode = colorBlindDropdown.value;
            if (reducedMotionToggle != null) _working.reducedMotion = reducedMotionToggle.isOn;
            if (subtitlesToggle != null) _working.showSubtitles = subtitlesToggle.isOn;
        }

        // ══════════════════════════════════════════════════════════════════════════
        // Rebind kláves
        // ══════════════════════════════════════════════════════════════════════════

        private void WireRebindButtons()
        {
            sprintKeyBtn?.onClick.AddListener(() => StartRebind("sprintKey", sprintKeyLabel));
            jumpKeyBtn?.onClick.AddListener(() => StartRebind("jumpKey", jumpKeyLabel));
            crouchKeyBtn?.onClick.AddListener(() => StartRebind("crouchKey", crouchKeyLabel));
            interactKeyBtn?.onClick.AddListener(() => StartRebind("interactKey", interactKeyLabel));
            inventoryKeyBtn?.onClick.AddListener(() => StartRebind("inventoryKey", inventoryKeyLabel));
            zoomKeyBtn?.onClick.AddListener(() => StartRebind("zoomKey", zoomKeyLabel));
        }

        private TextMeshProUGUI _rebindLabel;

        private void StartRebind(string target, TextMeshProUGUI label)
        {
            if (_isRebinding) return;
            _isRebinding = true;
            _rebindTarget = target;
            _rebindLabel = label;
            if (label != null) label.text = "Stiskni klávesu...";
        }

        private void OnGUI()
        {
            if (!_isRebinding) return;

            Event e = Event.current;
            if (e.isKey && e.keyCode != KeyCode.None && e.type == EventType.KeyDown)
            {
                OnRebindComplete(e.keyCode.ToString());
            }
            else if (e.isMouse && e.type == EventType.MouseDown)
            {
                OnRebindComplete("Mouse" + e.button);
            }
        }

        private void OnRebindComplete(string keyName)
        {
            _isRebinding = false;

            switch (_rebindTarget)
            {
                case "sprintKey": _working.sprintKey = keyName; break;
                case "jumpKey": _working.jumpKey = keyName; break;
                case "crouchKey": _working.crouchKey = keyName; break;
                case "interactKey": _working.interactKey = keyName; break;
                case "inventoryKey": _working.inventoryKey = keyName; break;
                case "zoomKey": _working.zoomKey = keyName; break;
            }

            RefreshKeyLabels();
        }

        private void RefreshKeyLabels()
        {
            SetKeyLabel(sprintKeyLabel, "Sprint", _working.sprintKey);
            SetKeyLabel(jumpKeyLabel, "Skok", _working.jumpKey);
            SetKeyLabel(crouchKeyLabel, "Dřep", _working.crouchKey);
            SetKeyLabel(interactKeyLabel, "Interakce", _working.interactKey);
            SetKeyLabel(inventoryKeyLabel, "Inventář", _working.inventoryKey);
            SetKeyLabel(zoomKeyLabel, "Zoom", _working.zoomKey);
        }

        private static void SetKeyLabel(TextMeshProUGUI label, string action, string keyName)
        {
            if (label != null) label.text = $"{action}: [{keyName}]";
        }

        // ══════════════════════════════════════════════════════════════════════════
        // Utility
        // ══════════════════════════════════════════════════════════════════════════

        private static void SetSlider(Slider slider, TextMeshProUGUI label, float value, string name, int decimals)
        {
            if (slider != null) slider.value = value;
            if (label != null) label.text = $"{name}: {value.ToString($"F{decimals}")}";
        }

        private static int FpsLimitIndex(int fps)
        {
            for (int i = 0; i < FpsLimits.Length; i++)
                if (FpsLimits[i] == fps) return i;
            return 0;
        }

        private static int AutoSaveIndex(int minutes)
        {
            for (int i = 0; i < AutoSaveMinutes.Length; i++)
                if (AutoSaveMinutes[i] == minutes) return i;
            return 2; // default 5 min
        }

        private static int AaIndex(int aa) => aa switch { 2 => 1, 4 => 2, 8 => 3, _ => 0 };
        private static int AaValue(int idx) => idx switch { 1 => 2, 2 => 4, 3 => 8, _ => 0 };
    }
}
