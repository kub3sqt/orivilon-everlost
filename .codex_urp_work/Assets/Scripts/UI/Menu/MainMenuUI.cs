using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using Orivilon.SaveSystem;
using Orivilon.Core;

namespace Orivilon.UI.Menu
{
    /// <summary>
    /// Řídí seznam uložených světů v hlavním menu.
    /// Při aktivaci panelu asynchronně čeká na inicializaci WorldSaveManager
    /// a pak naplní seznam světů. Pokud žádný svět neexistuje, zobrazí formulář pro vytvoření.
    /// Každá položka seznamu má vizuál přizpůsobený podle pozice (první/prostřední/poslední).
    /// </summary>
    public class MainMenuUI : MonoBehaviour
    {
        /// <summary>Prefab UI elementu jednoho světa v seznamu.</summary>
        public GameObject listElementPrefab;

        /// <summary>Transform kontejneru, do kterého se instantiují položky seznamu.</summary>
        public Transform listContent;

        /// <summary>Panel formuláře pro vytvoření nového světa.</summary>
        public GameObject createMenu;

        /// <summary>ScrollView se seznamem existujících světů.</summary>
        public GameObject worldScrollView;

        /// <summary>Manuálně vynutí okamžitý refresh seznamu (volatelné z externího tlačítka).</summary>
        public void RefreshNow() => RefreshList();

        /// <summary>
        /// Při aktivaci panelu spustí coroutinu čekající na dostupnost WorldSaveManager.
        /// </summary>
        private void OnEnable()
        {
            StartCoroutine(DelayedRefresh());
        }

        /// <summary>
        /// Čeká až do 2 sekund na dostupnost WorldSaveManager, listContent a listElementPrefab.
        /// Po splnění podmínek nebo vypršení timeoutu zavolá RefreshList().
        /// </summary>
        private IEnumerator DelayedRefresh()
        {
            float timeout = 2f;
            float timer = 0f;

            while (timer < timeout)
            {
                if (WorldSaveManager.instance != null &&
                    listContent != null &&
                    listElementPrefab != null)
                    break;

                timer += Time.unscaledDeltaTime;
                yield return null;
            }

            RefreshList();
        }

        /// <summary>
        /// Vyčistí a znovu naplní seznam světů.
        /// Pokud WorldSaveManager neexistuje nebo jsou světy prázdné, zobrazí formulář pro vytvoření.
        /// Jinak zobrazí ScrollView se seznamem a pro každý svět vytvoří položku.
        /// </summary>
        public void RefreshList()
        {
            ClearWorldList();

            if (WorldSaveManager.instance == null)
            {
                Debug.LogError("[MainMenuUI] WorldSaveManager instance not found.");
                ShowCreateWorldMenu();
                return;
            }

            var worlds = WorldSaveManager.instance.worlds;

            if (worlds == null || worlds.Count == 0)
            {
                ShowCreateWorldMenu();
                return;
            }

            ShowWorldsList();

            int totalCount = worlds.Count;

            for (int i = 0; i < totalCount; i++)
            {
                CreateWorldListItem(worlds[i], i, totalCount);
            }
        }

        /// <summary>
        /// Odstraní všechny dynamicky vytvořené položky ze seznamu.
        /// Zachovává speciální objekty "TopSpace" a "BottomSpace" pro layout padding.
        /// </summary>
        private void ClearWorldList()
        {
            if (listContent == null) return;

            for (int i = listContent.childCount - 1; i >= 0; i--)
            {
                Transform child = listContent.GetChild(i);
                if (child.name == "TopSpace" || child.name == "BottomSpace")
                    continue;

                Destroy(child.gameObject);
            }
        }

        /// <summary>
        /// Zobrazí formulář pro vytvoření světa a skryje ScrollView se seznamem.
        /// Volá se pokud neexistuje žádný uložený svět.
        /// </summary>
        private void ShowCreateWorldMenu()
        {
            if (createMenu != null)
                createMenu.SetActive(true);

            if (worldScrollView != null)
                worldScrollView.SetActive(false);
        }

        /// <summary>
        /// Skryje formulář a zobrazí ScrollView se seznamem světů.
        /// </summary>
        private void ShowWorldsList()
        {
            if (createMenu != null)
                createMenu.SetActive(false);

            if (worldScrollView != null)
                worldScrollView.SetActive(true);
        }

        /// <summary>
        /// Vytvoří jednu položku seznamu světů a nakonfiguruje ji.
        /// Nastaví název světa jako text, připojí onClick callback a předá WorldListElement
        /// informaci o pozici v seznamu pro správné nastavení vizuálu (sprite pozadí).
        /// Nová položka se vkládá před BottomSpace zachovávající layout padding.
        /// </summary>
        /// <param name="worldData">Data světa pro tuto položku.</param>
        /// <param name="index">Index světa v seznamu (0 = první).</param>
        /// <param name="totalCount">Celkový počet světů (pro určení pozice poslední).</param>
        private void CreateWorldListItem(WorldData worldData, int index, int totalCount)
        {
            if (listElementPrefab == null || listContent == null)
            {
                Debug.LogError("[MainMenuUI] Missing prefab or content!");
                return;
            }

            GameObject entry = Instantiate(listElementPrefab, listContent);

            TMP_Text nameLabel = entry.GetComponentInChildren<TMP_Text>();
            if (nameLabel != null)
            {
                nameLabel.text = worldData.worldName;
                nameLabel.color = Color.white;
            }

            Button btn = entry.GetComponentInChildren<Button>();
            if (btn != null)
            {
                btn.onClick.AddListener(() => OnWorldSelected(worldData));
            }

            WorldListElement visual = entry.GetComponent<WorldListElement>();
            if (visual != null)
            {
                visual.SetVisual(index, totalCount);
            }

            Transform bottomSpace = listContent.Find("BottomSpace");
            if (bottomSpace != null)
            {
                entry.transform.SetSiblingIndex(bottomSpace.GetSiblingIndex());
            }
        }

        /// <summary>
        /// Callback volaný po kliknutí na položku světa v seznamu.
        /// Nastaví vybraný svět v GameManager a spustí načítání Game scény.
        /// </summary>
        /// <param name="worldData">Data vybraného světa.</param>
        private void OnWorldSelected(WorldData worldData)
        {
            Debug.Log($"Selected world: {worldData.worldName}");

            GameManager.selectedWorld = worldData;

            if (SceneLoader.Instance != null)
            {
                SceneLoader.Instance.LoadGameDelayed();
            }
            else
            {
                UnityEngine.SceneManagement.SceneManager.LoadScene("Game");
            }
        }
    }
}