using System;
using UnityEngine;

namespace Orivilon.UI.Settings
{
    /// <summary>
    /// Serializovatelná datová třída obsahující veškerá uživatelská nastavení hry.
    /// Ukládá se jako JSON do Application.persistentDataPath/settings.json.
    /// Rozdělena do kategorií: grafika, zvuk, ovládání, gameplay, přístupnost.
    /// </summary>
    [Serializable]
    public class SettingsData
    {
        // ─── GRAFIKA ────────────────────────────────────────────────────────────
        /// <summary>Index kvality grafiky (0 = nejnižší, 5 = Ultra). Viz QualitySettings.</summary>
        public int qualityLevel = 2;

        /// <summary>Vzdálenost renderu terénu (počet chunků). Ovlivňuje výkon.</summary>
        public int renderDistance = 4;

        /// <summary>Vzdálenost LOD přechodu objektů.</summary>
        public float lodBias = 1f;

        /// <summary>Index rozlišení z dostupných Screen.resolutions.</summary>
        public int resolutionIndex = -1; // -1 = výchozí (aktuální)

        /// <summary>Fullscreen mód (0=Fullscreen, 1=Windowed, 2=Borderless).</summary>
        public int fullscreenMode = 0;

        /// <summary>Vertikální synchronizace (0 = vypnuto, 1 = zapnuto).</summary>
        public int vSync = 1;

        /// <summary>Limit snímků za sekundu (-1 = neomezeno).</summary>
        public int targetFrameRate = -1;

        /// <summary>Vzdálenost vykreslování stínů.</summary>
        public float shadowDistance = 150f;

        /// <summary>Kvalita stínů (0=vypnuto, 1=Hard, 2=Soft).</summary>
        public int shadowQuality = 2;

        /// <summary>Úroveň anizotropního filtrování textur (0–9).</summary>
        public int anisotropicFiltering = 4;

        /// <summary>Anti-aliasing (0=vypnuto, 2=2x, 4=4x, 8=8x).</summary>
        public int antiAliasing = 2;

        /// <summary>Hustota vegetace/trávy (0.0–1.0).</summary>
        public float grassDensity = 1f;

        /// <summary>Vzdálenost renderu vegetace.</summary>
        public float vegetationDistance = 200f;

        /// <summary>Zapnutí/vypnutí ambientního okluze.</summary>
        public bool ambientOcclusion = true;

        /// <summary>Zapnutí/vypnutí bloom efektu.</summary>
        public bool bloom = true;

        /// <summary>Zapnutí/vypnutí depth of field.</summary>
        public bool depthOfField = false;

        /// <summary>Zapnutí/vypnutí motion blur.</summary>
        public bool motionBlur = false;

        /// <summary>Jas obrazovky (0.5–2.0).</summary>
        public float brightness = 1f;

        /// <summary>Gamma korekce (0.5–2.0).</summary>
        public float gamma = 1f;


        // ─── ZVUK ────────────────────────────────────────────────────────────────
        /// <summary>Hlavní hlasitost (0.0–1.0).</summary>
        public float masterVolume = 1f;

        /// <summary>Hlasitost hudby (0.0–1.0).</summary>
        public float musicVolume = 0.7f;

        /// <summary>Hlasitost zvukových efektů (0.0–1.0).</summary>
        public float sfxVolume = 1f;

        /// <summary>Hlasitost prostředí/ambientu (0.0–1.0).</summary>
        public float ambientVolume = 0.8f;

        /// <summary>Hlasitost UI zvuků (0.0–1.0).</summary>
        public float uiVolume = 1f;

        /// <summary>Zapnout/vypnout zvuk při minimalizaci okna.</summary>
        public bool muteWhenUnfocused = true;


        // ─── OVLÁDÁNÍ ────────────────────────────────────────────────────────────
        /// <summary>Citlivost myši (0.1–10.0).</summary>
        public float mouseSensitivity = 2f;

        /// <summary>Invertovat svislou osu kamery.</summary>
        public bool invertMouseY = false;

        /// <summary>Invertovat vodorovnou osu kamery.</summary>
        public bool invertMouseX = false;

        /// <summary>Klávesa pro sprint (jako string, např. "LeftShift").</summary>
        public string sprintKey = "LeftShift";

        /// <summary>Klávesa pro skok.</summary>
        public string jumpKey = "Space";

        /// <summary>Klávesa pro dřep.</summary>
        public string crouchKey = "LeftControl";

        /// <summary>Klávesa pro interakci.</summary>
        public string interactKey = "E";

        /// <summary>Klávesa pro inventář.</summary>
        public string inventoryKey = "Tab";

        /// <summary>Klávesa pro zoom/zaměřování.</summary>
        public string zoomKey = "Mouse1";

        /// <summary>Klávesa pro létání (debug/kreativní mód).</summary>
        public string flightKey = "F";

        /// <summary>Klávesa pro rychlé uložení.</summary>
        public string quickSaveKey = "F5";

        /// <summary>Přepnout sprint na toggle (true) nebo hold (false).</summary>
        public bool sprintToggle = false;

        /// <summary>Přepnout dřep na toggle (true) nebo hold (false).</summary>
        public bool crouchToggle = false;


        // ─── GAMEPLAY ────────────────────────────────────────────────────────────
        /// <summary>Obtížnost (0=Mírná, 1=Normální, 2=Těžká, 3=Hardcore).</summary>
        public int difficulty = 1;

        /// <summary>Rychlost odebírání hladu (násobitel výchozí hodnoty 0.1–3.0).</summary>
        public float hungerRate = 1f;

        /// <summary>Rychlost odebírání žízně (násobitel 0.1–3.0).</summary>
        public float thirstRate = 1f;

        /// <summary>Zapnout head bob efekt při chůzi.</summary>
        public bool headBob = true;

        /// <summary>Rychlost sprintu (násobitel výchozí hodnoty 0.5–2.0).</summary>
        public float sprintSpeedMultiplier = 1f;

        /// <summary>Síla skoku (násobitel 0.5–2.0).</summary>
        public float jumpPowerMultiplier = 1f;

        /// <summary>Zobrazovat HUD (zdraví, hlad, žízeň).</summary>
        public bool showHUD = true;

        /// <summary>Zobrazovat sprint bar.</summary>
        public bool showSprintBar = true;

        /// <summary>Zobrazovat crosshair.</summary>
        public bool showCrosshair = true;

        /// <summary>Automatické ukládání (minuty, 0 = vypnuto).</summary>
        public int autoSaveInterval = 5;

        /// <summary>Zobrazit FPS čítač.</summary>
        public bool showFPS = false;

        /// <summary>Zobrazit pozici hráče (debug info).</summary>
        public bool showCoordinates = false;

        /// <summary>FOV kamery (60–120).</summary>
        public float fieldOfView = 60f;


        // ─── PŘÍSTUPNOST ─────────────────────────────────────────────────────────
        /// <summary>Velikost písma UI (0.5–2.0, násobitel).</summary>
        public float uiFontScale = 1f;

        /// <summary>Barevný filtr pro barvoslepost (0=vypnuto, 1=Deuteranopia, 2=Protanopia, 3=Tritanopia).</summary>
        public int colorBlindMode = 0;

        /// <summary>Snížit pohyb kamery / motion sickness prevence.</summary>
        public bool reducedMotion = false;

        /// <summary>Zobrazit titulky (pokud existují).</summary>
        public bool showSubtitles = false;

        /// <summary>Jazyk hry (kód locale, např. "cs", "en").</summary>
        public string language = "en";
    }
}
