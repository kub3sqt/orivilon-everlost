using UnityEngine;

namespace Orivilon
{
    /// <summary>
    /// Řídí Polyverse Skies oblohu podle míry noci ze <see cref="SunRotation"/> (nightBlendStatic),
    /// takže obloha jde přesně v zákrytu s časem i osvětlením scény. Časy přechodu (svítání/soumrak)
    /// se nastavují na SunRotation, ne tady.
    /// Předává směr slunce a měsíce do skybox shaderu (jako oficiální Polyverse Manager) a plynule
    /// blenduje mezi denním, „golden" a nočním skybox materiálem. Slunce/měsíc si vezme automaticky ze SunRotation.
    /// Nezasahuje do žádných světel ani post-processingu – jen do oblohy.
    /// </summary>
    [DisallowMultipleComponent]
    public class PolyverseDayNight : MonoBehaviour
    {
        [Header("Reference (slunce/měsíc se doplní automaticky ze SunRotation)")]
        [Tooltip("Transform slunce. Když necháš prázdné, vezme se ze SunRotation.")]
        [SerializeField] private Transform sunTransform;

        [Tooltip("Transform měsíce. Když necháš prázdné, vezme se ze SunRotation.")]
        [SerializeField] private Transform moonTransform;

        [Header("Skybox materiály (Polyverse presety)")]
        [Tooltip("Polyverse skybox materiál pro den (PV_Sky_Day). Musí mít stejný shader jako ostatní.")]
        [SerializeField] private Material skyboxDay;

        [Tooltip("Polyverse skybox materiál pro noc (PV_Sky_Night).")]
        [SerializeField] private Material skyboxNight;

        [Tooltip("Volitelný skybox pro západ/východ – golden hour (PV_Sky_Golden). Přechází se přes něj.")]
        [SerializeField] private Material skyboxGolden;

        [Header("Stálost během přechodu")]
        [Tooltip("Držet velikost slunce konstantní (jinak slunce při západu naroste, protože se liší v materiálech).")]
        [SerializeField] private bool keepSunSizeConstant = true;
        [Tooltip("Držet rychlost mraků konstantní (jinak se mraky při západu zrychlí, protože se liší v materiálech).")]
        [SerializeField] private bool keepCloudSpeedConstant = true;

        /// <summary>Runtime kopie skyboxu, do které se blenduje (nemění zdrojové materiály na disku).</summary>
        private Material _skyboxInstance;

        // Polyverse property, které během blendu držíme konstantní (jinak roste slunce a zrychlují mraky).
        private static readonly int SkyColorID = Shader.PropertyToID("_SkyColor");
        private static readonly int EquatorColorID = Shader.PropertyToID("_EquatorColor");
        private static readonly int GroundColorID = Shader.PropertyToID("_GroundColor");
        private static readonly int EquatorHeightID = Shader.PropertyToID("_EquatorHeight");
        private static readonly int EquatorSmoothID = Shader.PropertyToID("_EquatorSmoothness");
        private static readonly int StarsID = Shader.PropertyToID("_StarsIntensity");
        private static readonly int WaterSkyID = Shader.PropertyToID("_EverlostWaterSky");
        private static readonly int WaterHorizonID = Shader.PropertyToID("_EverlostWaterHorizon");

        private static readonly int SunSizeID = Shader.PropertyToID("_SunSize");
        private static readonly int CloudSpeedID = Shader.PropertyToID("_CloudsRotationSpeed");
        private float _daySunSize = 0.3f;
        private float _dayCloudSpeed = 0.1f;

        private void Start()
        {
            ResolveSunMoon();

            if (skyboxDay != null)
            {
                _skyboxInstance = new Material(skyboxDay);
                _skyboxInstance.name = skyboxDay.name + " (runtime)";
                RenderSettings.skybox = _skyboxInstance;

                // Zapamatuj si denní velikost slunce a rychlost mraků, ať je můžeme držet konstantní.
                if (skyboxDay.HasProperty(SunSizeID)) _daySunSize = skyboxDay.GetFloat(SunSizeID);
                if (skyboxDay.HasProperty(CloudSpeedID)) _dayCloudSpeed = skyboxDay.GetFloat(CloudSpeedID);
            }
        }

        /// <summary>Doplní slunce/měsíc ze SunRotation, pokud nejsou přiřazené ručně.</summary>
        private void ResolveSunMoon()
        {
            if (sunTransform != null && moonTransform != null) return;

            SunRotation sr = GetComponent<SunRotation>();
            if (sr == null) sr = FindFirstObjectByType<SunRotation>();
            if (sr != null)
            {
                if (sunTransform == null) sunTransform = sr.sunTransform;
                if (moonTransform == null) moonTransform = sr.moonTransform;
            }
        }

        private void LateUpdate()
        {
            if (sunTransform == null || moonTransform == null)
                ResolveSunMoon();

            // Směr slunce/měsíce do shaderu (jako Polyverse Manager) – kreslí kotouč slunce a měsíce na obloze.
            // Plynulý směr ze SunRotation: stínové světlo se otáčí po krocích, kotouč na obloze ne.
            if (sunTransform != null)
                Shader.SetGlobalVector("PolyverseSunDir", SunRotation.SkySunForward);
            if (moonTransform != null)
                Shader.SetGlobalVector("PolyverseMoonDir", SunRotation.SkyMoonForward);

            if (_skyboxInstance == null || skyboxDay == null || skyboxNight == null)
                return;

            // Míra noci (0 = den, 1 = noc) ze SunRotation – stejná hodnota, jakou řídí osvětlení.
            float nightBlend = Mathf.Clamp01(SunRotation.nightBlendStatic);

            // Přechod přes zlatou hodinu: den → západ → noc (a obráceně za úsvitu),
            // aby obloha plynule tmavla přes teplé barvy, ne skokem z modré do tmavé.
            if (skyboxGolden != null)
            {
                if (nightBlend < 0.5f)
                    _skyboxInstance.Lerp(skyboxDay, skyboxGolden, nightBlend * 2f);
                else
                    _skyboxInstance.Lerp(skyboxGolden, skyboxNight, (nightBlend - 0.5f) * 2f);
            }
            else
            {
                _skyboxInstance.Lerp(skyboxDay, skyboxNight, nightBlend);
            }

            // Tlumenější denní modř nechá vyniknout terén a dálkový opar.
            // Upravujeme jen runtime kopii po Lerp, takže se korekce nehromadí a presety zůstávají původní.
            float daylight = 1f - Mathf.SmoothStep(0f, 1f, nightBlend * 2f);
            BlendDaySkyColor(SkyColorID, DayZenith, daylight * DayZenithAmount);
            BlendDaySkyColor(EquatorColorID, new Color(0.70f, 0.76f, 0.80f, 1f), daylight * 0.5f);
            // Zlatá hodina: preset má zenit tmavě fialový – zesvětlit do soumračné modrofialové.
            BlendDaySkyColor(SkyColorID, GoldenZenith, (1f - Mathf.Abs(nightBlend * 2f - 1f)) * GoldenZenithAmount);
            MatchHorizonToFog(nightBlend);
            PublishWaterSky();

            // Blend materiálů interpoluje i tyto property → přepíšeme je zpět na denní hodnotu,
            // aby slunce při západu nenarůstalo a mraky se nezrychlovaly.
            if (keepSunSizeConstant && _skyboxInstance.HasProperty(SunSizeID))
                _skyboxInstance.SetFloat(SunSizeID, _daySunSize);
            if (keepCloudSpeedConstant && _skyboxInstance.HasProperty(CloudSpeedID))
                _skyboxInstance.SetFloat(CloudSpeedID, _dayCloudSpeed);
        }
        /// <summary>
        /// 24.10.2026: obzor oblohy = barva mlhy. Vzdálený terén se v mlze rozpouští do stejné barvy,
        /// jakou má obloha těsně nad ním, takže hory splynou s oparem místo ostré hrany. Spodní polokoule
        /// (za okrajem světa) dřív zůstala černá z presetu a na obzoru moře tvořila tmavomodrý pruh.
        /// Hvězdy naběhnou až po zlaté hodině, ne při zapadajícím slunci.
        /// </summary>
        public static Color DayZenith = new Color(0.44f, 0.62f, 0.84f, 1f);
        public static float DayZenithAmount = 0.8f, HorizonFromFog = 1f;
        public static float GoldenEquatorHeight = 0.75f;
        public static Color GoldenZenith = new Color(0.40f, 0.36f, 0.56f, 1f);
        public static float GoldenZenithAmount = 0.5f;
        /// <summary>Denní přechod obzor → zenit: vyšší a pozvolnější (nižší smoothness = lineárnější náběh).</summary>
        public static float DayEquatorHeight = 0.62f, DayEquatorSmoothness = 0.25f;

        private void MatchHorizonToFog(float nightBlend)
        {
            if (!RenderSettings.fog) return;
            Color fog = RenderSettings.fogColor;
            fog.a = 1f;
            if (_skyboxInstance.HasProperty(GroundColorID)) _skyboxInstance.SetColor(GroundColorID, fog);
            BlendDaySkyColor(EquatorColorID, fog, HorizonFromFog);
            // Zlatá hodina: širší teplý pás nad obzorem (preset ho má jen těsně u hrany).
            // Ve dne pozvolný přechod, jinak je na obzoru ostrá hrana mezi oparem a modří.
            float golden = 1f - Mathf.Abs(nightBlend * 2f - 1f);
            float day = 1f - Mathf.SmoothStep(0f, 1f, nightBlend * 2f);
            if (_skyboxInstance.HasProperty(EquatorHeightID))
            {
                float h = Mathf.Lerp(_skyboxInstance.GetFloat(EquatorHeightID), DayEquatorHeight, day);
                _skyboxInstance.SetFloat(EquatorHeightID, Mathf.Lerp(h, GoldenEquatorHeight, golden));
            }
            if (_skyboxInstance.HasProperty(EquatorSmoothID))
                _skyboxInstance.SetFloat(EquatorSmoothID, Mathf.Lerp(_skyboxInstance.GetFloat(EquatorSmoothID), DayEquatorSmoothness, Mathf.Max(day, golden)));
            if (_skyboxInstance.HasProperty(StarsID) && skyboxNight != null && skyboxNight.HasProperty(StarsID))
                _skyboxInstance.SetFloat(StarsID, skyboxNight.GetFloat(StarsID) * Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.6f, 0.95f, nightBlend)));
        }

        private void BlendDaySkyColor(int id, Color target, float amount)
        {
            if (amount <= 0f || !_skyboxInstance.HasProperty(id)) return;
            Color source = _skyboxInstance.GetColor(id);
            target.a = source.a;
            _skyboxInstance.SetColor(id, Color.Lerp(source, target, amount));
        }

        private void PublishWaterSky()
        {
            if (!_skyboxInstance.HasProperty(SkyColorID) || !_skyboxInstance.HasProperty(EquatorColorID)) return;
            Color sky = _skyboxInstance.GetColor(SkyColorID);
            Color horizon = _skyboxInstance.GetColor(EquatorColorID);
            if (QualitySettings.activeColorSpace == ColorSpace.Linear)
            {
                sky = sky.linear;
                horizon = horizon.linear;
            }
            // W označuje platný zdroj; bez tohoto správce použije voda původní SH ambient.
            Shader.SetGlobalVector(WaterSkyID, new Vector4(sky.r, sky.g, sky.b, 1f));
            Shader.SetGlobalVector(WaterHorizonID, new Vector4(horizon.r, horizon.g, horizon.b, 1f));
        }

        private void OnDisable()
        {
            Shader.SetGlobalVector(WaterSkyID, Vector4.zero);
            Shader.SetGlobalVector(WaterHorizonID, Vector4.zero);
        }
    }
}
