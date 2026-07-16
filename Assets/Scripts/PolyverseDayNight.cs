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
            if (sunTransform != null)
                Shader.SetGlobalVector("PolyverseSunDir", sunTransform.forward);
            if (moonTransform != null)
                Shader.SetGlobalVector("PolyverseMoonDir", moonTransform.forward);

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

            // Blend materiálů interpoluje i tyto property → přepíšeme je zpět na denní hodnotu,
            // aby slunce při západu nenarůstalo a mraky se nezrychlovaly.
            if (keepSunSizeConstant && _skyboxInstance.HasProperty(SunSizeID))
                _skyboxInstance.SetFloat(SunSizeID, _daySunSize);
            if (keepCloudSpeedConstant && _skyboxInstance.HasProperty(CloudSpeedID))
                _skyboxInstance.SetFloat(CloudSpeedID, _dayCloudSpeed);
        }
    }
}
