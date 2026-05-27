using Orivilon.World.Objects;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;

namespace Orivilon.Player
{
    /// <summary>
    /// Singleton zajišťující interakci hráče s objekty ve světě pomocí raycastu ze středu obrazovky.
    /// Detekuje dva typy objektů: PickupItem (klávesa E) a HarvestableObject (levé tlačítko myši).
    /// Při najetí zobrazí interakční UI a při akci deleguje na příslušný objekt.
    /// Zásah nástroje při těžení je signalizován přes Animation Event z ToolAnimatoru.
    /// </summary>
    public class PlayerInteraction : MonoBehaviour
    {
        /// <summary>Globální instance singletonu.</summary>
        public static PlayerInteraction Instance;

        /// <summary>Maximální vzdálenost (v jednotkách) pro interakci s objekty.</summary>
        [Header("Raycast settings")]
        public float interactionDistance = 3f;

        /// <summary>Layer maska omezující raycast pouze na objekty určené pro pickup.</summary>
        public LayerMask pickupLayer;

        /// <summary>GameObject celého interakčního UI panelu (text s výzvou).</summary>
        [Header("UI")]
        public GameObject pickupText;

        /// <summary>Textový prvek zobrazující konkrétní výzvu k akci (např. "Press E to pick up...").</summary>
        public TextMeshProUGUI pickupTextLabel;

        /// <summary>Výchozí crosshair zobrazovaný při normálním pohledu bez interakce.</summary>
        public GameObject crosshairDefault;

        /// <summary>Crosshair zobrazovaný při najetí na interagovatelný objekt.</summary>
        public GameObject crosshairPickup;

        /// <summary>Reference na ToolAnimator pro zahájení animace těžení.</summary>
        [SerializeField] private ToolAnimator toolAnimator;

        /// <summary>Aktuálně cílený pickup objekt (null = žádný).</summary>
        private PickupItem currentPickup;

        /// <summary>Aktuálně cílený těžitelný objekt (null = žádný).</summary>
        private HarvestableObject currentHarvest;

        /// <summary>Singleton inicializace.</summary>
        private void Awake()
        {
            Instance = this;
        }

        /// <summary>
        /// Každý snímek provede raycast a zkontroluje interakce:
        /// E = sebrat PickupItem, LMB dolů = zahájit animaci těžení, LMB nahoru = ukončit animaci.
        /// </summary>
        private void Update()
        {
            CheckForInteraction();

            if (currentPickup != null && Input.GetKeyDown(KeyCode.E))
            {
                currentPickup.PickUp();
                ClearUI();
            }

            if (Input.GetMouseButtonDown(0))
            {
                if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject())
                    return;

                toolAnimator?.SetHolding(true);
            }

            if (Input.GetMouseButtonUp(0))
            {
                toolAnimator?.SetHolding(false);
            }
        }

        /// <summary>
        /// Provede raycast ze středu obrazovky a aktualizuje aktuálně cílený objekt.
        /// Pokud je zasažen PickupItem, uloží referenci a zobrazí výzvu k sebrání.
        /// Pokud je zasažen HarvestableObject, zobrazí výzvu podle drženého nástroje.
        /// Jinak vymaže UI a resetuje reference.
        /// </summary>
        private void CheckForInteraction()
        {
            Ray ray = Camera.main.ViewportPointToRay(new Vector3(0.5f, 0.5f));
            RaycastHit hit;

            if (Physics.Raycast(ray, out hit, interactionDistance))
            {
                if (hit.collider.TryGetComponent(out PickupItem pickup))
                {
                    currentPickup = pickup;
                    currentHarvest = null;
                    ShowUI($"Press E to pick up {pickup.itemData.itemName}");
                    return;
                }

                if (hit.collider.TryGetComponent(out HarvestableObject harvest))
                {
                    currentHarvest = harvest;
                    currentPickup = null;

                    var equippedTool = PlayerEquipment.Instance?.EquippedTool;
                    ShowUI(harvest.GetInteractionText(equippedTool));
                    return;
                }
            }

            ClearUI();
        }

        /// <summary>
        /// Zobrazí interakční UI s danou výzvou a přepne crosshair na "pickup" variantu.
        /// </summary>
        /// <param name="text">Text výzvy k akci zobrazený nad crosshairem.</param>
        private void ShowUI(string text)
        {
            pickupText.SetActive(true);
            pickupTextLabel.text = text;
            crosshairDefault.SetActive(false);
            crosshairPickup.SetActive(true);
        }

        /// <summary>
        /// Skryje interakční UI, resetuje reference na cílené objekty a obnoví výchozí crosshair.
        /// </summary>
        private void ClearUI()
        {
            currentPickup = null;
            currentHarvest = null;

            pickupText.SetActive(false);
            crosshairDefault.SetActive(true);
            crosshairPickup.SetActive(false);
        }

        /// <summary>
        /// Voláno z Animation Eventu v animaci ToolAnimatoru v momentu dopadu nástroje.
        /// Deleguje pokus o těžení na aktuálně cílený HarvestableObject s drženým nástrojem.
        /// </summary>
        public void OnToolHit()
        {
            if (currentHarvest == null)
                return;

            currentHarvest.TryHarvest(
                PlayerEquipment.Instance?.EquippedTool
            );
        }
    }
}