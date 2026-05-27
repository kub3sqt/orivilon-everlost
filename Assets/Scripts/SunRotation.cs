using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Orivilon
{
    /// <summary>
    /// Řídí cyklus dne a noci otáčením slunce a měsíce kolem osy X.
    /// Přepíná aktivitu světelných objektů, nastavuje stíny a sleduje herní čas.
    /// Herní čas je sdílen jako statická proměnná pro přístup z ostatních skriptů (např. DebugGUI).
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

        /// <summary>Násobitel rychlosti plynoucího času během dne (hodiny 5–19).</summary>
        [SerializeField] public float dayScale = 1f;

        /// <summary>Násobitel rychlosti plynoucího času během noci (mimo hodiny 5–19).</summary>
        [SerializeField] public float nightScale = 1f;

        /// <summary>
        /// Statická kopie herního času přístupná odkudkoli bez reference na instanci.
        /// Synchronizována s <see cref="timeOfDay"/> v metodě SetTime().
        /// </summary>
        public static float timeOfDayStatic = 12f;

        /// <summary>Příznak, zda je kamerové světlo (baterka) zapnuté.</summary>
        public bool isLightOpen = false;

        /// <summary>Pokud true, čas se nepohybuje (ruční pauza klávesou P).</summary>
        public bool paused = false;

        /// <summary>
        /// Interní příznak, zda byl čas již inicializován.
        /// Zabraňuje přepsání načteného času výchozí hodnotou při Start().
        /// </summary>
        private bool isTimeLoaded = false;

        /// <summary>
        /// V Unity editoru okamžitě aplikuje změnu timeOfDay na rotaci slunce a měsíce.
        /// </summary>
        private void OnValidate()
        {
            SetTime(timeOfDay);
        }

        /// <summary>
        /// Inicializuje stav kamerového světla při spuštění objektu.
        /// </summary>
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
        /// <param name="savedTime">Uložený herní čas (0–24).</param>
        /// <param name="savedDayScale">Uložená rychlost dne.</param>
        /// <param name="savedNightScale">Uložená rychlost noci.</param>
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

        /// <summary>
        /// Pokud nebyl čas načten externě, použije výchozí hodnotu a označí systém jako inicializovaný.
        /// </summary>
        private void Start()
        {
            if (!isTimeLoaded)
            {
                Debug.LogWarning("SunRotation: GameManager nenastavil čas, používám default");
                timeOfDayStatic = timeOfDay;
                SetTime(timeOfDay);
                isTimeLoaded = true;
            }
        }

        /// <summary>
        /// Každý snímek zpracovává vstup a pohybuje časem.
        /// L = přepnutí baterky, P = pauza/pokračování, Z = -1h, T = +1h.
        /// Během dne (5–19h) čas plyne rychlostí dayScale, jinak nightScale.
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

            if (timeOfDay >= 5 && timeOfDay < 19)
                timeOfDay += Time.deltaTime / 60f * dayScale;
            else
                timeOfDay += Time.deltaTime / 60f * nightScale;

            SetTime(timeOfDay);
        }

        /// <summary>
        /// Aplikuje zadaný čas na celý systém dne a noci.
        /// Vypočítá rotaci slunce a měsíce, přepne jejich aktivitu a upraví stíny.
        /// Čas 0–24 se mapuje na rotaci 0–360° kolem osy X.
        /// Slunce svítí od 5:00 do 19:00, měsíc od 18:00 do 6:00.
        /// </summary>
        /// <param name="time">Herní čas v rozsahu 0–24.</param>
        public void SetTime(float time)
        {
            timeOfDay = time;
            if (timeOfDay >= 24f)
                timeOfDay = 0f;

            timeOfDayStatic = timeOfDay;

            float sunRotation = timeOfDay / 24f * 360f - 90f;
            if (sunTransform != null)
                sunTransform.localRotation = Quaternion.Euler(sunRotation, 0, 0);

            float moonRotation = sunRotation + 180f;
            if (moonTransform != null)
                moonTransform.localRotation = Quaternion.Euler(moonRotation, 0, 0);

            bool sunActive = timeOfDay >= 5f && timeOfDay < 19f;
            bool moonActive = timeOfDay >= 18f || timeOfDay < 6f;

            if (sunTransform != null)
                sunTransform.gameObject.SetActive(sunActive);

            if (moonTransform != null)
                moonTransform.gameObject.SetActive(moonActive);

            if (sunTransform != null)
            {
                Light sunLight = sunTransform.GetComponent<Light>();
                if (sunLight != null)
                {
                    sunLight.shadows = (timeOfDay >= 18f || timeOfDay < 6f) ? LightShadows.None : LightShadows.Soft;
                }
            }

            if (moonTransform != null)
            {
                Light moonLight = moonTransform.GetComponent<Light>();
                if (moonLight != null)
                {
                    moonLight.shadows = (timeOfDay >= 18f || timeOfDay < 6f) ? LightShadows.Soft : LightShadows.None;
                }
            }
        }

        /// <summary>
        /// Vrátí aktuální herní čas jako textový řetězec ve formátu HH:MM.
        /// Například hodnota 14.5 vrátí "14:30".
        /// </summary>
        /// <returns>Textová reprezentace herního času.</returns>
        public string GetTimeString()
        {
            int hour = Mathf.FloorToInt(timeOfDay);
            int minute = Mathf.FloorToInt((timeOfDay - hour) * 60f);
            return $"{hour:D2}:{minute:D2}";
        }
    }
}