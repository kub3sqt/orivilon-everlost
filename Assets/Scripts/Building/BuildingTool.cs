using Orivilon.Inventory.Hotbar;
using Orivilon.Inventory.Inventory;
using Orivilon.Core;
using UnityEngine;

namespace Orivilon.Building
{
    /// <summary>
    /// Hlavní nástroj stavebního systému.
    /// Spravuje celý cyklus stavby: otevření menu, zobrazení průhledného náhledu,
    /// rotaci dílu klávesou R, vyhledání kompatibilního socketu a samotné umístění.
    /// Otevírání a zavírání menu deleguje na GameManager – stejně jako inventář.
    /// Nástroj je aktivní pouze pokud hráč drží kladivo v aktivním slotu.
    /// </summary>
    public class BuildingTool : MonoBehaviour
    {
        /// <summary>
        /// Kamera hráče používaná pro výpočet raycastu směrem k socketům.
        /// </summary>
        [Header("Camera")]
        public Camera playerCamera;

        /// <summary>
        /// Datová definice kladiva. Hráč musí mít tento item v aktivním slotu,
        /// aby mohl stavební nástroj používat. Bez kladiva se stavba deaktivuje.
        /// </summary>
        [Header("Required Tool")]
        public InventoryItemData hammerItem;

        /// <summary>
        /// Reference na HotbarController pro zjištění aktuálně drženého itemu.
        /// </summary>
        [Header("Hotbar")]
        public HotbarController hotbar;

        /// <summary>
        /// Kořenový GameObject stavebního menu (panel s výběrem dílů).
        /// Přiřazuje se v inspektoru, ale zpřístupňuje se přes property <see cref="BuildMenuRoot"/>.
        /// </summary>
        [Header("UI")]
        [SerializeField] private GameObject buildMenuObject;

        /// <summary>
        /// Zpřístupňuje kořenový objekt stavebního menu pro GameManager.
        /// GameManager si tuto referenci čte v metodě FindUIElements() hned po načtení scény.
        /// Musí být dostupná okamžitě – proto je property, ne statické pole nastavované v Start().
        /// </summary>
        public GameObject BuildMenuRoot => buildMenuObject;

        /// <summary>
        /// Aktuálně vybraný stavební díl (jeho datová definice ze ScriptableObject).
        /// </summary>
        [Header("Selected Piece")]
        public BuildingPieceData selectedPiece;

        /// <summary>
        /// Instance průhledného náhledu dílu zobrazená ve scéně před umístěním.
        /// </summary>
        private GameObject preview;

        /// <summary>
        /// Aktuální úhel rotace náhledu kolem osy Y (v násobcích 90°, klávesa R).
        /// </summary>
        private float rotation;

        /// <summary>
        /// Příznak, zda je stavební režim aktivní – hráč vybral díl a přesunuje náhled.
        /// </summary>
        private bool buildMode;

        /// <summary>
        /// Při startu skryje stavební menu, pokud je přiřazeno.
        /// </summary>
        void Start()
        {
            if (buildMenuObject != null)
                buildMenuObject.SetActive(false);
        }

        /// <summary>
        /// Každý snímek kontroluje blokování UI, přítomnost kladiva, přepínání menu,
        /// aktualizaci náhledu, rotaci a pokus o umístění dílu.
        /// Pokud je hra pozastavena nebo hráč nemá kladivo, vše se přeskočí.
        /// </summary>
        void Update()
        {
            if (IsUIBlockingGameplay())
                return;

            if (!PlayerHasHammer())
            {
                DisableBuilding();
                return;
            }

            HandleMenuToggle();

            if (!buildMode)
                return;

            UpdatePreview();
            HandleRotation();

            if (Input.GetMouseButtonDown(0))
                TryBuild();
        }

        /// <summary>
        /// Zjistí, zda je hra momentálně pozastavena (pause menu apod.).
        /// Pokud ano, stavební logika se nespouští.
        /// </summary>
        /// <returns>True, pokud je hra ve stavu pauzy.</returns>
        bool IsUIBlockingGameplay()
        {
            if (GameManager.instance == null) return false;
            return GameManager.instance.IsPaused;
        }

        /// <summary>
        /// Zpracuje stisknutí pravého tlačítka myši pro otevření nebo zavření stavebního menu.
        /// Otevření a zavření deleguje na GameManager, aby stav kurzoru a pohyb
        /// byly konzistentní s ostatními menu (inventář, pauza).
        /// </summary>
        void HandleMenuToggle()
        {
            if (!Input.GetMouseButtonDown(1)) return;
            if (!PlayerHasHammer()) return;

            if (buildMenuObject != null && buildMenuObject.activeSelf)
            {
                GameManager.instance.CloseBuildingMenu();
            }
            else
            {
                buildMode = false;
                GameManager.instance.OpenBuildingMenu();
            }

            if (preview != null)
                preview.SetActive(buildMenuObject == null || !buildMenuObject.activeSelf);
        }

        /// <summary>
        /// Deaktivuje stavební režim a skryje náhled i menu.
        /// Volá se, když hráč přestane držet kladivo nebo stavba skončí.
        /// Pokud je vše již deaktivované, metoda neudělá nic.
        /// </summary>
        void DisableBuilding()
        {
            if (!buildMode && (buildMenuObject == null || !buildMenuObject.activeSelf))
                return;

            buildMode = false;

            if (preview != null)
                preview.SetActive(false);

            if (buildMenuObject != null && buildMenuObject.activeSelf)
                GameManager.instance.CloseBuildingMenu();
        }

        /// <summary>
        /// Zkontroluje, zda má hráč kladivo v aktuálně aktivním slotu hotbaru.
        /// </summary>
        /// <returns>True, pokud je aktivní item roven <see cref="hammerItem"/>.</returns>
        bool PlayerHasHammer()
        {
            if (HandItemRenderer.Instance == null) return false;
            var item = HandItemRenderer.Instance.CurrentItem;
            return item != null && item == hammerItem;
        }

        /// <summary>
        /// Nastaví vybraný stavební díl z UI tlačítka v menu.
        /// Zavře stavební menu, aktivuje stavební režim a vytvoří průhledný náhled.
        /// Volá se přímo z UI tlačítek v build menu přes Inspector.
        /// </summary>
        /// <param name="piece">Datová definice dílu, který hráč vybral.</param>
        public void SetPiece(BuildingPieceData piece)
        {
            selectedPiece = piece;
            buildMode = true;

            GameManager.instance.CloseBuildingMenu();

            CreatePreview();
        }

        /// <summary>
        /// Vytvoří nový průhledný náhled vybraného dílu ve scéně.
        /// Pokud existuje starý náhled, nejprve ho zničí.
        /// </summary>
        void CreatePreview()
        {
            if (preview != null)
                Destroy(preview);

            preview = Instantiate(selectedPiece.previewPrefab);
        }

        /// <summary>
        /// Každý snímek aktualizuje pozici a rotaci náhledu.
        /// Pokud raycast zasáhne kompatibilní socket, náhled se přichytí k němu.
        /// Pokud díl povoluje umístění na terén, náhled sleduje bod dopadu raycastu.
        /// Jinak se náhled skryje, protože umístění není možné.
        /// </summary>
        void UpdatePreview()
        {
            Ray ray = playerCamera.ScreenPointToRay(Input.mousePosition);

            if (!Physics.Raycast(ray, out RaycastHit hit, 25))
                return;

            var socket = GetSocketFromRay();

            if (socket != null)
            {
                preview.SetActive(true);

                preview.transform.position =
                    socket.GetBuildPosition() +
                    socket.transform.rotation * selectedPiece.positionOffset;

                preview.transform.rotation =
                    socket.GetRotation() *
                    Quaternion.Euler(selectedPiece.rotationOffset) *
                    Quaternion.Euler(0, rotation, 0);

                return;
            }

            if (selectedPiece.allowTerrainPlacement)
            {
                preview.SetActive(true);
                preview.transform.position = hit.point;
                preview.transform.rotation = Quaternion.Euler(0, rotation, 0);
                return;
            }

            preview.SetActive(false);
        }

        /// <summary>
        /// Zpracuje stisk klávesy R a otočí náhled dílu o 90° kolem osy Y.
        /// </summary>
        void HandleRotation()
        {
            if (Input.GetKeyDown(KeyCode.R))
                rotation += 90f;
        }

        /// <summary>
        /// Hledá nejbližší volný a kompatibilní socket pomocí RaycastAll ze středu obrazovky.
        /// Z nalezených hitů filtruje pouze sockety, jejichž typ je povolen vybraným dílem,
        /// a vrátí ten, který je nejblíže kameře.
        /// </summary>
        /// <returns>Nejbližší kompatibilní volný socket, nebo null pokud žádný neexistuje.</returns>
        BuildingSocket GetSocketFromRay()
        {
            Ray ray = playerCamera.ScreenPointToRay(Input.mousePosition);
            var hits = Physics.RaycastAll(ray, 10f);

            BuildingSocket best = null;
            float bestDist = float.MaxValue;

            foreach (var hit in hits)
            {
                var socket = hit.collider.GetComponent<BuildingSocket>();

                if (socket == null) continue;
                if (!selectedPiece.allowedSockets.Contains(socket.socketType)) continue;
                if (!socket.CanAttach()) continue;

                if (hit.distance < bestDist)
                {
                    best = socket;
                    bestDist = hit.distance;
                }
            }

            return best;
        }

        /// <summary>
        /// Pokusí se umístit stavební díl na aktuální pozici náhledu.
        /// Nejprve zkontroluje dostupnost materiálů, pak vytvoří skutečný GameObject,
        /// propojí ho se sousedními díly, označí sockety jako obsazené
        /// a odečte použité materiály z inventáře.
        /// </summary>
        void TryBuild()
        {
            if (!HasResources()) return;

            BuildingSocket socket = GetSocketFromRay();

            if (socket != null && !selectedPiece.allowedSockets.Contains(socket.socketType))
                return;

            Vector3 buildPos = preview.transform.position;
            Quaternion buildRot = preview.transform.rotation;

            GameObject obj = Instantiate(selectedPiece.prefab, buildPos, buildRot);
            BuildingPiece piece = obj.GetComponent<BuildingPiece>();

            if (socket != null)
            {
                socket.SetOccupied(true);

                BuildingPiece parent = socket.GetComponentInParent<BuildingPiece>();
                if (parent != null && piece != null)
                    piece.AttachTo(parent);

                var newSockets = obj.GetComponentsInChildren<BuildingSocket>();

                foreach (var s in newSockets)
                {
                    if (s.socketType != socket.socketType) continue;

                    float dist = Vector3.Distance(s.transform.position, socket.transform.position);
                    if (dist < 0.05f)
                    {
                        s.SetOccupied(true);
                        break;
                    }
                }
            }

            var placedSockets = obj.GetComponentsInChildren<BuildingSocket>();
            var allSockets = FindObjectsOfType<BuildingSocket>();

            foreach (var s1 in placedSockets)
            {
                foreach (var s2 in allSockets)
                {
                    if (s2.transform.root == obj.transform) continue;
                    if (!s2.CanAttach()) continue;
                    if (s1.socketType != s2.socketType) continue;

                    Vector3 dir1 = (s1.transform.position - obj.transform.position).normalized;
                    Vector3 dir2 = (s2.transform.position - s2.transform.root.position).normalized;

                    if (Vector3.Dot(dir1, dir2) < -0.9f)
                    {
                        float dist = Vector3.Distance(s1.transform.position, s2.transform.position);
                        if (dist < 0.2f)
                        {
                            s1.SetOccupied(true);
                            s2.SetOccupied(true);
                        }
                    }
                }
            }

            ConsumeResources();
            Orivilon.Multiplayer.NetworkWorldSync.Instance?.BroadcastBuildingPlaced(selectedPiece, buildPos, buildRot);
        }

        /// <summary>
        /// Zkontroluje, zda má hráč v inventáři dostatek materiálů pro stavbu.
        /// Prochází všechny požadované itemy a porovnává počet s obsahem inventáře.
        /// </summary>
        /// <returns>True, pokud jsou všechny potřebné materiály dostupné.</returns>
        bool HasResources()
        {
            for (int i = 0; i < selectedPiece.costItems.Length; i++)
            {
                if (CountItem(selectedPiece.costItems[i]) < selectedPiece.costAmounts[i])
                    return false;
            }
            return true;
        }

        /// <summary>
        /// Odečte spotřebované materiály z inventáře po úspěšném umístění dílu.
        /// Po každém odebraném itemu obnoví UI inventáře.
        /// </summary>
        void ConsumeResources()
        {
            for (int i = 0; i < selectedPiece.costItems.Length; i++)
            {
                RemoveItem(selectedPiece.costItems[i], selectedPiece.costAmounts[i]);
                InventoryUI.RefreshAll();
            }
        }

        /// <summary>
        /// Spočítá celkové množství daného itemu v inventáři hráče.
        /// Prochází všechny sloty a sčítá jejich obsah.
        /// </summary>
        /// <param name="item">Item, jehož počet se zjišťuje.</param>
        /// <returns>Celkový počet kusů daného itemu v inventáři.</returns>
        int CountItem(InventoryItemData item)
        {
            int count = 0;
            foreach (var slot in InventoryData.Instance.Slots)
            {
                if (!slot.IsEmpty && slot.item.id == item.id)
                    count += slot.amount;
            }
            return count;
        }

        /// <summary>
        /// Odebere zadaný počet kusů itemu z inventáře.
        /// Prochází sloty a postupně odečítá, dokud není požadované množství odebráno.
        /// Po dokončení upozorní inventář na změnu dat.
        /// </summary>
        /// <param name="item">Item, který se má odebrat.</param>
        /// <param name="amount">Počet kusů k odebrání.</param>
        void RemoveItem(InventoryItemData item, int amount)
        {
            foreach (var slot in InventoryData.Instance.Slots)
            {
                if (slot.IsEmpty || slot.item.id != item.id) continue;

                int remove = Mathf.Min(slot.amount, amount);
                slot.amount -= remove;
                amount -= remove;

                if (slot.amount <= 0) slot.Clear();
                if (amount <= 0) break;
            }

            InventoryData.Instance.NotifyInventoryChanged();
        }
    }
}