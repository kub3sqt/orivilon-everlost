using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Orivilon
{
    /// <summary>
    /// Řídí cyklus dne a noci otáčením slunce a měsíce a plynulým přechodem osvětlení podle ČASU.
    /// Den/noc se přepíná v nastavitelných hodinových oknech (svítání ráno, soumrak večer) – vše
    /// (intenzita a barva slunce/měsíce, ambient i mlha) se mění plynule, s golden hour uprostřed přechodu.
    /// Denní stav (intenzity světel + barvy ambientu/mlhy) si skript vezme z toho, co je nastavené ve scéně.
    /// Herní čas a míra noci jsou sdílené staticky pro ostatní skripty (DebugGUI, PolyverseDayNight).
    /// </summary>
    public class SunRotation : MonoBehaviour
    {
        /// <summary>Transform slunečního světla.</summary>
        [SerializeField] public Transform sunTransform;

        /// <summary>Transform měsíčního světla.</summary>
        [SerializeField] public Transform moonTransform;

        /// <summary>Světlo připevněné ke kameře (ruční baterka). Zapíná/vypíná se klávesou L.</summary>
        [SerializeField] public Transform cameraLight;

        /// <summary>
        /// Aktuální herní čas ve formátu 0–24 (např. 12.5 = 12:30).
        /// Mění se každý snímek podle aktuálního měřítka (dayScale nebo nightScale).
        /// </summary>
        [Range(0, 24)]
        [SerializeField] public float timeOfDay = 12f;

        /// <summary>Násobitel rychlosti plynoucího času během dne.</summary>
        [SerializeField] public float dayScale = 1f;

        /// <summary>Násobitel rychlosti plynoucího času během noci.</summary>
        [SerializeField] public float nightScale = 1f;

        [Header("Časy přechodu den/noc (herní hodiny)")]
        [Tooltip("Začátek svítání – tady se začne rozednívat (noc → den).")]
        [SerializeField, Range(0f, 12f)] private float morningStart = 6f;
        [Tooltip("Konec svítání – od téhle hodiny je plný den.")]
        [SerializeField, Range(0f, 12f)] private float morningEnd = 7f;
        [Tooltip("Začátek soumraku – tady se začne stmívat (den → noc).")]
        [SerializeField, Range(12f, 24f)] private float eveningStart = 20f;
        [Tooltip("Konec soumraku – od téhle hodiny je plná noc.")]
        [SerializeField, Range(12f, 24f)] private float eveningEnd = 21f;

        [Header("Cinematické osvětlení (plynulý přechod)")]
        [Tooltip("Plynule řídit intenzitu/barvu slunce a měsíce, ambient a barvu mlhy. Vypnutím zůstane jen otáčení.")]
        [SerializeField] private bool driveLighting = true;

        [Tooltip("Barva slunce ve dne – teple bílá.")]
        [SerializeField] private Color sunDayColor = new Color(1f, 0.96f, 0.88f, 1f);
        [Tooltip("Barva slunce za svítání/soumraku (golden hour) – teplá oranžová.")]
        [SerializeField] private Color sunHorizonColor = new Color(1f, 0.45f, 0.18f, 1f);
        [Tooltip("Barva měsíčního světla – studená modrá.")]
        [SerializeField] private Color moonColor = new Color(0.5f, 0.62f, 0.92f, 1f);

        [Tooltip("Řídit ambient (barvy Gradient/Trilight) podle času.")]
        [SerializeField] private bool driveAmbient = true;
        [Tooltip("Denní barvy ambientu (default = hodnoty z Game scény). Noc se z nich odvozuje – nezávisí na tom, jak scénu spustíš (menu/přímo).")]
        [SerializeField] private Color dayAmbientSky = new Color(0.56f, 0.61f, 0.70f, 1f);
        [SerializeField] private Color dayAmbientEquator = new Color(0.44f, 0.46f, 0.48f, 1f);
        [SerializeField] private Color dayAmbientGround = new Color(0.30f, 0.29f, 0.27f, 1f);
        [Tooltip("Jak světlá je noc vůči dni (0 = skoro černá, 1 = jako den). Zvyš, když je noc moc tmavá.")]
        [SerializeField, Range(0f, 1f)] private float nightLevel = 0.3f;
        [Tooltip("Chladný nádech noci (modrá).")]
        [SerializeField] private Color nightTint = new Color(0.7f, 0.82f, 1.1f, 1f);
        [Tooltip("Teplý nádech ambientu za svítání/soumraku (golden hour).")]
        [SerializeField] private Color duskAmbientTint = new Color(0.55f, 0.28f, 0.12f, 1f);

        [Tooltip("Plynule měnit barvu mlhy k noci (jen barva; hustotu ani zapnutí nemění).")]
        [SerializeField] private bool driveFog = true;
        [Tooltip("Denní barva mlhy (default = z Game scény).")]
        [SerializeField] private Color dayFogColor = new Color(0.74f, 0.81f, 0.89f, 1f);
        [SerializeField] private Color nightFogColor = new Color(0.04f, 0.06f, 0.11f, 1f);
        [Tooltip("Barva mlhy za svítání/soumraku (golden hour).")]
        [SerializeField] private Color duskFogColor = new Color(0.60f, 0.32f, 0.18f, 1f);
        [Tooltip("Odkud mlha začíná (dál od kamery = víc vidíš). Platí pro lineární mlhu.")]
        [SerializeField] private float fogStartDistance = 150f;
        [Tooltip("Kde je mlha úplně plná. Vyšší = mlha dál.")]
        [SerializeField] private float fogEndDistance = 450f;

        [Header("Stíny (dosah do mlhy)")]
        [Tooltip("Automaticky nastavit dosah stínů (URP Max Distance) až k mlze.")]
        [SerializeField] private bool driveShadowDistance = true;
        [Tooltip("O kolik dál za začátek mlhy stíny dosáhnou (m). Stíny pak mizí do mlhy.")]
        [SerializeField] private float shadowsBeyondFogStart = 40f;

        /// <summary>Statická kopie herního času (0–24) přístupná odkudkoli.</summary>
        public static float timeOfDayStatic = 12f;

        /// <summary>Míra noci: 0 = plný den, 1 = plná noc. Hlavní ovladač, sdílený s PolyverseDayNight.</summary>
        public static float nightBlendStatic = 0f;

        /// <summary>Výška slunce nad obzorem, -1..1 (0 = obzor). Ponecháno pro případnou kompatibilitu.</summary>
        public static float sunHeightStatic = 0f;

        /// <summary>Příznak, zda je kamerové světlo (baterka) zapnuté.</summary>
        public bool isLightOpen = false;

        /// <summary>Pokud true, čas se nepohybuje (ruční pauza klávesou P).</summary>
        public bool paused = false;

        /// <summary>Interní příznak, zda byl čas již inicializován (brání přepsání načteného času při Start()).</summary>
        private bool isTimeLoaded = false;

        // Cache světel a denní referenční hodnoty (zachytí se jednou, ještě před první úpravou osvětlení).
        private Light _sunLight;
        private Light _moonLight;
        private float _sunBaseIntensity = 1f;
        private float _moonBaseIntensity = 1f;
        private bool _baselineCaptured = false;

        /// <summary>V Unity editoru okamžitě aplikuje změnu timeOfDay na rotaci slunce a měsíce.</summary>
        private void OnValidate()
        {
            SetTime(timeOfDay);
        }

        /// <summary>Inicializuje stav kamerového světla při spuštění objektu.</summary>
        private void Awake()
        {
            if (cameraLight != null)
            {
                cameraLight.gameObject.SetActive(isLightOpen);
            }

            Debug.Log("SunRotation: Awake - čeká na načtení času z GameManager");
        }

        /// <summary>
        /// Okamžitě nastaví herní čas a měřítka bez čekání na Start().
        /// Volá se z GameManageru hned po načtení světa, aby byl čas správný ještě před prvním Update().
        /// </summary>
        public void LoadTimeImmediately(float savedTime, float savedDayScale, float savedNightScale)
        {
            timeOfDay = savedTime;
            timeOfDayStatic = savedTime;
            dayScale = savedDayScale;
            nightScale = savedNightScale;

            SetTime(timeOfDay);
            isTimeLoaded = true;

            Debug.Log($"SunRotation: OKAMŽITĚ načten čas {timeOfDay:F2} ({GetTimeString()})");
        }

        /// <summary>Pokud nebyl čas načten externě, použije výchozí hodnotu a označí systém jako inicializovaný.</summary>
        private void Start()
        {
            if (!isTimeLoaded)
            {
                Debug.LogWarning("SunRotation: GameManager nenastavil čas, používám default");
                timeOfDayStatic = timeOfDay;
                SetTime(timeOfDay);
                isTimeLoaded = true;
            }

            ApplyShadowDistance();
        }

        /// <summary>
        /// Nastaví dosah stínů (URP Max Distance) tak, aby stíny sahaly kousek za začátek mlhy a mizely do ní.
        /// URP nemá funkční QualitySettings.shadowDistance – nastavuje se na URP assetu (přes reflexi, bez tvrdé závislosti na URP).
        /// </summary>
        private void ApplyShadowDistance()
        {
            if (!driveShadowDistance) return;

            var rp = UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline;
            if (rp == null) return;

            var prop = rp.GetType().GetProperty("shadowDistance");
            if (prop == null || !prop.CanWrite) return;

            try
            {
                prop.SetValue(rp, fogStartDistance + shadowsBeyondFogStart);
            }
            catch (System.Exception e)
            {
                Debug.LogWarning($"SunRotation: nelze nastavit dosah stínů: {e.Message}");
            }
        }

        /// <summary>
        /// Každý snímek zpracovává vstup a pohybuje časem.
        /// L = přepnutí baterky, P = pauza/pokračování, Z = -1h, T = +1h.
        /// Během dne (mezi svítáním a soumrakem) čas plyne rychlostí dayScale, jinak nightScale.
        /// </summary>
        private void Update()
        {
            if (!isTimeLoaded) return;

            if (Input.GetKeyDown(KeyCode.L))
            {
                isLightOpen = !isLightOpen;
                if (cameraLight != null)
                {
                    cameraLight.gameObject.SetActive(isLightOpen);
                }
            }

            if (Input.GetKeyDown(KeyCode.P))
            {
                paused = !paused;
            }
            if (paused) return;

            if (Input.GetKeyDown(KeyCode.Z))
                timeOfDay -= 1f;

            if (Input.GetKeyDown(KeyCode.T))
                timeOfDay += 1f;

            bool isDaytime = timeOfDay >= morningStart && timeOfDay < eveningEnd;
            timeOfDay += Time.deltaTime / 60f * (isDaytime ? dayScale : nightScale);

            SetTime(timeOfDay);
        }

        /// <summary>
        /// Aplikuje zadaný čas: spočítá míru noci (0–1), otočí slunce/měsíc a (v play mode) plynule nastaví osvětlení.
        /// </summary>
        /// <param name="time">Herní čas v rozsahu 0–24.</param>
        public void SetTime(float time)
        {
            timeOfDay = time;
            if (timeOfDay >= 24f) timeOfDay -= 24f;
            if (timeOfDay < 0f) timeOfDay += 24f;
            timeOfDayStatic = timeOfDay;

            // Míra noci 0..1 podle hodinových oken (hlavní ovladač den/noc).
            float nightBlend = ComputeNightBlend(timeOfDay);
            nightBlendStatic = nightBlend;

            // Rotace: slunce vychází na začátku svítání a zapadá na konci soumraku (oblouk nad obzorem).
            float sunAngle = ComputeSunAngle(timeOfDay);
            sunHeightStatic = Mathf.Sin(sunAngle * Mathf.Deg2Rad);

            if (sunTransform != null)
                sunTransform.localRotation = Quaternion.Euler(sunAngle, 0f, 0f);
            if (moonTransform != null)
                moonTransform.localRotation = Quaternion.Euler(sunAngle + 180f, 0f, 0f);

            if (driveLighting && Application.isPlaying)
            {
                // Slunce i měsíc necháváme stále aktivní; viditelnost řídí POUZE plynulá intenzita (žádné blikání).
                if (sunTransform != null && !sunTransform.gameObject.activeSelf)
                    sunTransform.gameObject.SetActive(true);
                if (moonTransform != null && !moonTransform.gameObject.activeSelf)
                    moonTransform.gameObject.SetActive(true);

                ApplyCinematicLighting(nightBlend);
            }
            else
            {
                ApplyLegacyShadows(nightBlend < 0.5f);
            }
        }

        /// <summary>
        /// Míra noci podle času: 0 mezi koncem svítání a začátkem soumraku (den), 1 v noci,
        /// plynulý náběh během svítání (morningStart→morningEnd) a soumraku (eveningStart→eveningEnd).
        /// </summary>
        private float ComputeNightBlend(float t)
        {
            if (t >= morningStart && t < morningEnd)
                return 1f - SmoothRamp(morningStart, morningEnd, t); // svítání: noc(1) → den(0)
            if (t >= morningEnd && t < eveningStart)
                return 0f;                                           // den
            if (t >= eveningStart && t < eveningEnd)
                return SmoothRamp(eveningStart, eveningEnd, t);      // soumrak: den(0) → noc(1)
            return 1f;                                               // noc
        }

        /// <summary>
        /// Úhel slunce kolem osy X. Slunce je nad obzorem (oblouk 0°→180°) od svítání do soumraku,
        /// jinak pod obzorem (180°→360°). Tím kotouč slunce, stíny i obloha sedí na stejné časy.
        /// </summary>
        private float ComputeSunAngle(float t)
        {
            float rise = morningStart;
            float set = eveningEnd;
            if (t >= rise && t < set)
                return Mathf.InverseLerp(rise, set, t) * 180f;

            float tt = t < rise ? t + 24f : t;
            return 180f + Mathf.InverseLerp(set, rise + 24f, tt) * 180f;
        }

        /// <summary>
        /// Plynule nastaví intenzitu/barvu slunce a měsíce, barvy ambientu a barvu mlhy podle míry noci.
        /// Golden hour vzniká uprostřed přechodu (twilight vrchol).
        /// </summary>
        /// <param name="nightBlend">0 = den, 1 = noc.</param>
        private void ApplyCinematicLighting(float nightBlend)
        {
            CaptureBaseline();

            float dayFactor = 1f - nightBlend;
            float twilight = 1f - Mathf.Abs(nightBlend * 2f - 1f); // 0 v plném dni i noci, 1 uprostřed přechodu

            // --- SLUNCE ---
            if (_sunLight != null)
            {
                _sunLight.intensity = _sunBaseIntensity * dayFactor;
                _sunLight.color = Color.Lerp(sunHorizonColor, sunDayColor, dayFactor);
                _sunLight.shadows = dayFactor > 0.05f ? LightShadows.Soft : LightShadows.None;
            }

            // --- MĚSÍC ---
            if (_moonLight != null)
            {
                _moonLight.enabled = true;
                _moonLight.intensity = _moonBaseIntensity * nightBlend;
                _moonLight.color = moonColor;
                _moonLight.shadows = nightBlend > 0.6f ? LightShadows.Soft : LightShadows.None;
            }

            // --- AMBIENT (režim Gradient/Trilight zůstává, měníme jen barvy) ---
            if (driveAmbient)
            {
                // Noc = denní ambient ztlumený na nightLevel s chladným nádechem → vždy viditelné, ne černá.
                Color nSky = ScaleTint(dayAmbientSky, nightLevel, nightTint);
                Color nEq = ScaleTint(dayAmbientEquator, nightLevel, nightTint);
                Color nGr = ScaleTint(dayAmbientGround, nightLevel, nightTint);
                Color sky = Color.Lerp(nSky, dayAmbientSky, dayFactor);
                Color equator = Color.Lerp(nEq, dayAmbientEquator, dayFactor);
                Color ground = Color.Lerp(nGr, dayAmbientGround, dayFactor);

                sky = Color.Lerp(sky, duskAmbientTint, twilight);
                equator = Color.Lerp(equator, duskAmbientTint, twilight * 0.6f);

                RenderSettings.ambientSkyColor = sky;
                RenderSettings.ambientEquatorColor = equator;
                RenderSettings.ambientGroundColor = ground;
            }

            // --- MLHA (barva podle času + vzdálenost; režim ani zapnutí neměníme) ---
            if (driveFog && RenderSettings.fog)
            {
                Color fog = Color.Lerp(nightFogColor, dayFogColor, dayFactor);
                fog = Color.Lerp(fog, duskFogColor, twilight);
                RenderSettings.fogColor = fog;

                // Posun mlhy dál od kamery (platí pro lineární režim mlhy).
                RenderSettings.fogStartDistance = fogStartDistance;
                RenderSettings.fogEndDistance = fogEndDistance;
            }
        }

        /// <summary>
        /// Jednorázově zachytí denní intenzity slunce a měsíce z jejich Light komponent (než je začneme měnit),
        /// aby denní jas světel zůstal takový, jaký je nastavený ve scéně. Denní barvy ambientu/mlhy se neberou
        /// z RenderSettings (to je při startu přes menu nespolehlivé), ale z polí dayAmbient*/dayFogColor.
        /// </summary>
        private void CaptureBaseline()
        {
            if (_baselineCaptured) return;

            if (_sunLight == null && sunTransform != null) _sunLight = sunTransform.GetComponent<Light>();
            if (_moonLight == null && moonTransform != null) _moonLight = moonTransform.GetComponent<Light>();

            if (_sunLight != null) _sunBaseIntensity = _sunLight.intensity;
            if (_moonLight != null) _moonBaseIntensity = _moonLight.intensity;

            _baselineCaptured = true;
        }

        /// <summary>
        /// Plynulý náběh 0→1 podle toho, kde leží x mezi edge0 a edge1 (jako smoothstep v shaderech).
        /// POZOR: Unity Mathf.SmoothStep(a,b,t) dělá něco jiného (interpoluje a→b), proto vlastní funkce.
        /// </summary>
        private static float SmoothRamp(float edge0, float edge1, float x)
        {
            float t = Mathf.Clamp01((x - edge0) / (edge1 - edge0));
            return t * t * (3f - 2f * t);
        }

        /// <summary>Ztlumí barvu na daný podíl (level) a přebarví ji nádechem (tint), po složkách. Alfa = 1.</summary>
        private static Color ScaleTint(Color baseColor, float level, Color tint)
        {
            return new Color(baseColor.r * level * tint.r,
                             baseColor.g * level * tint.g,
                             baseColor.b * level * tint.b, 1f);
        }

        /// <summary>Jednoduché přepnutí stínů (fallback pro editor / vypnuté cinematické osvětlení).</summary>
        private void ApplyLegacyShadows(bool dayish)
        {
            if (sunTransform != null)
            {
                Light sunLight = sunTransform.GetComponent<Light>();
                if (sunLight != null)
                    sunLight.shadows = dayish ? LightShadows.Soft : LightShadows.None;
            }

            if (moonTransform != null)
            {
                Light moonLight = moonTransform.GetComponent<Light>();
                if (moonLight != null)
                    moonLight.shadows = dayish ? LightShadows.None : LightShadows.Soft;
            }
        }

        /// <summary>Vrátí aktuální herní čas jako text ve formátu HH:MM (např. 14.5 → "14:30").</summary>
        public string GetTimeString()
        {
            int hour = Mathf.FloorToInt(timeOfDay);
            int minute = Mathf.FloorToInt((timeOfDay - hour) * 60f);
            return $"{hour:D2}:{minute:D2}";
        }
    }
}
