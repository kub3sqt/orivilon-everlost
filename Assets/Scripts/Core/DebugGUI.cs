using Orivilon.Data;
using Orivilon.World.Terrain;
using System.Collections;
using TMPro;
using UnityEngine;

namespace Orivilon.Core
{
    /// <summary>
    /// Zobrazuje debug panel s informacemi o hráči a světě.
    /// Panel se přepíná klávesou F3. Během načítání scény je automaticky skryt.
    /// Zobrazuje průměrné FPS, pozici hráče (X/Y/Z), aktuální biom a herní čas.
    /// </summary>
    public class DebugGUI : MonoBehaviour
    {
        /// <summary>Canvas, na kterém debug panel žije. Vždy zůstává aktivní.</summary>
        [Header("Main Canvas (NEvypínat)")]
        [SerializeField] Canvas debugCanvas;

        /// <summary>Panel s debug informacemi (pozice, FPS, biom, čas).</summary>
        [Header("Objects to toggle (F3)")]
        [SerializeField] GameObject debugPanel;

        /// <summary>Panel s ladicími slidery pro nastavení herních parametrů.</summary>
        [SerializeField] GameObject slidersPanel;

        /// <summary>Text zobrazující průměrné FPS.</summary>
        [Header("UI Texts")]
        [SerializeField] TextMeshProUGUI avgFPSText;

        /// <summary>Text zobrazující X souřadnici hráče.</summary>
        [SerializeField] TextMeshProUGUI playerXText;

        /// <summary>Text zobrazující Y souřadnici hráče.</summary>
        [SerializeField] TextMeshProUGUI playerYText;

        /// <summary>Text zobrazující Z souřadnici hráče.</summary>
        [SerializeField] TextMeshProUGUI playerZText;

        /// <summary>Text zobrazující název aktuálního biomu pod hráčem.</summary>
        [SerializeField] TextMeshProUGUI currentBiomeText;

        /// <summary>Text zobrazující aktuální herní čas ve formátu HH:MM.</summary>
        [SerializeField] TextMeshProUGUI timeText;

        /// <summary>Příznak, zda je debug panel momentálně otevřen (přepíná F3).</summary>
        private bool isOpen = false;

        /// <summary>
        /// Aktivuje canvas, skryje oba panely a spustí coroutinu pro pravidelnou aktualizaci FPS.
        /// </summary>
        private void Start()
        {
            debugCanvas.enabled = true;

            debugPanel.SetActive(false);
            slidersPanel.SetActive(false);

            StartCoroutine(UpdateFPS());
        }

        /// <summary>
        /// Coroutina aktualizující FPS a čas každých 0,5 sekundy.
        /// Výpočet probíhá pouze pokud je debug panel otevřen, aby zbytečně nezatěžoval CPU.
        /// </summary>
        IEnumerator UpdateFPS()
        {
            var wait = new WaitForSeconds(.5f);

            while (true)
            {
                if (isOpen)
                {
                    float avgFPS = 1f / Time.deltaTime;
                    UpdateFPS(avgFPS);
                    UpdateTimeFormatted();
                }

                yield return wait;
            }
        }

        /// <summary>
        /// Aktualizuje textové pole s herním časem přeformátovaným na HH:MM.
        /// Čerpá data ze statické proměnné SunRotation.timeOfDayStatic.
        /// </summary>
        private void UpdateTimeFormatted()
        {
            float time = SunRotation.timeOfDayStatic;

            int hours = Mathf.FloorToInt(time);
            int minutes = Mathf.FloorToInt((time - hours) * 60f);

            timeText.text = $"Time: {hours:00}:{minutes:00}";
        }

        /// <summary>
        /// Aktualizuje textové pole s názvem biomu pod hráčem.
        /// Volá se externě ze systému biomů při změně polohy.
        /// </summary>
        /// <param name="biome">Datová definice aktuálního biomu.</param>
        public void UpdateBiome(BiomeData biome)
        {
            currentBiomeText.text = "Biome: " + biome.name;
        }

        /// <summary>
        /// Aktualizuje textové pole s průměrnými FPS.
        /// </summary>
        /// <param name="avgFPS">Průměrný počet snímků za sekundu.</param>
        public void UpdateFPS(float avgFPS)
        {
            avgFPSText.text = "FPS: " + avgFPS.ToString("F0");
        }

        /// <summary>
        /// Každý snímek kontroluje stav načítání a vstup klávesy F3.
        /// Během načítání scény jsou oba panely skryty.
        /// Po stisknutí F3 se panely přepnou a začne/ukončí aktualizace GUI.
        /// </summary>
        private void Update()
        {
            if (SceneLoader.IsLoading)
            {
                debugPanel.SetActive(false);
                slidersPanel.SetActive(false);
                return;
            }

            if (Input.GetKeyDown(KeyCode.F3))
            {
                isOpen = !isOpen;

                debugPanel.SetActive(isOpen);
                slidersPanel.SetActive(isOpen);
            }

            if (isOpen)
                UpdateGUI();
        }

        /// <summary>
        /// Obnoví zobrazení pozice hráče a aktuálního biomu.
        /// Pozici čte přímo z transformace vieweru v EndlessTerrain.
        /// Biom určuje pomocí MapGenerator.biomeCollection na základě souřadnic hráče.
        /// </summary>
        void UpdateGUI()
        {
            var p = EndlessTerrain.instance.viewer.position;

            int px = Mathf.RoundToInt(p.x);
            int py = Mathf.RoundToInt(p.y);
            int pz = Mathf.RoundToInt(p.z);

            playerXText.text = "X: " + px;
            playerYText.text = "Y: " + py;
            playerZText.text = "Z: " + pz;

            BiomeData biome = MapGenerator.instance.biomeCollection
                .ChooseBiome(px, pz, Vector2Int.zero);

            UpdateBiome(biome);
        }
    }
}