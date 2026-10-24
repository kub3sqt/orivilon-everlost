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

        /// <summary>Pohled zepředu (F5, TravelerCameraView) – kříž by mířil opačně, proto se skrývá.</summary>
        public static bool HideCrosshair;

        /// <summary>Aktuálně cílený pickup objekt (null = žádný).</summary>
        private PickupItem currentPickup;

        /// <summary>Aktuálně cílený těžitelný objekt (null = žádný).</summary>
        private HarvestableObject currentHarvest;

        /// <summary>Aktuálně cílené instancované stéblo trávy (bez GameObjectu).</summary>
        private Orivilon.World.Spawning.ObjectSpawner.InstancedPickup currentInstanced;

        /// <summary>Singleton inicializace.</summary>
        private void Awake()
        {
            Instance = this;
        }

        /// <summary>
        /// Každý snímek provede raycast a zkontroluje interakce:
        /// E = sebrat PickupItem, LMB dolů = zahájit animaci těžení, LMB nahoru = ukončit animaci.
        /// </summary>
        /// <summary>Výzva podle klávesy z nastavení (KeyBindings.Interact), ne pevné „E“.</summary>
        private static string InteractKeyLabel => Orivilon.UI.Settings.KeyBindings.Interact.ToString();

        private void Update()
        {
            CheckForInteraction();

            if (currentPickup != null && Input.GetKeyDown(Orivilon.UI.Settings.KeyBindings.Interact))
            {
                TravelerAvatar.Local?.PlayAction(TravelerAction.Pickup);
                currentPickup.PickUp();
                ClearUI();
            }
            else if (currentInstanced.Valid && Input.GetKeyDown(Orivilon.UI.Settings.KeyBindings.Interact))
            {
                TravelerAvatar.Local?.PlayAction(TravelerAction.Pickup);
                Orivilon.World.Spawning.ObjectSpawner.PickUpInstanced(currentInstanced);
                ClearUI();
            }

            if (Input.GetMouseButtonDown(0))
            {
                if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject())
                    return;

                // Postava v004: držené LMB = smyčka Chop/Mine s 1,0 s cyklem jako původní HitLoop (zásah v kontaktním snímku).
                if (TravelerAvatar.Local != null)
                    TravelerAvatar.Local.SetHarvesting(true);
                else
                    toolAnimator?.SetHolding(true);
            }

            if (Input.GetMouseButtonUp(0))
            {
                TravelerAvatar.Local?.SetHarvesting(false);
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
            float grassReach = interactionDistance;

            if (Physics.Raycast(ray, out hit, interactionDistance))
            {
                // Tráva kreslená instancovaně nemá collider; smí být nejvýš kousek za zásahem
                // (paprsek do trsu obvykle skončí v zemi hned za ním).
                grassReach = Mathf.Min(interactionDistance, hit.distance + 0.6f);

                if (hit.collider.TryGetComponent(out PickupItem pickup))
                {
                    currentPickup = pickup;
                    currentHarvest = null;
                    currentInstanced = default;
                    ShowUI($"Press {InteractKeyLabel} to pick up {pickup.itemData.itemName}");
                    return;
                }

                if (hit.collider.TryGetComponent(out HarvestableObject harvest))
                {
                    currentHarvest = harvest;
                    currentPickup = null;
                    currentInstanced = default;

                    var equippedTool = PlayerEquipment.Instance?.EquippedTool;
                    ShowUI(harvest.GetInteractionText(equippedTool));
                    return;
                }
            }

            // Vyhozený předmět u nohou: Q ho odhodí ~4–5 m před hráče, ale oči jsou 7,7 m nad zemí a pohled
            // dolů je omezený (maxLookAngle 50°) – nejbližší zaměřitelné místo na zemi je ~6,5 m daleko, takže
            // paprsek předmět míjel o ~1 m a E nešlo (QA: sekera 5,3 m od hráče, paprsek o 6,4° vedle).
            // Když přímý paprsek nic sebratelného netrefil, zkusí se tenká koule podél téhož paprsku
            // jen na vrstvě Pickup. Sebrání jde dál stejnou cestou (PickUp, síťová autorita beze změny).
            if (TryPickupAssist(ray, grassReach + pickupAssistRadius, out var assisted))
            {
                currentPickup = assisted;
                currentHarvest = null;
                currentInstanced = default;
                ShowUI($"Press {InteractKeyLabel} to pick up {assisted.itemData.itemName}");
                return;
            }

            if (Orivilon.World.Spawning.ObjectSpawner.FindInstancedPickup(ray, grassReach, out var grass))
            {
                currentPickup = null;
                currentHarvest = null;
                currentInstanced = grass;
                ShowUI($"Press {InteractKeyLabel} to pick up {grass.template.itemData.itemName}");
                return;
            }

            ClearUI();
        }

        /// <summary>Poloměr pomocné koule pro sebrání (svět). Hráč je 8 m vysoký, 1,5 m ≈ dlaň.</summary>
        public float pickupAssistRadius = 1.5f;

        /// <param name="maxDistance">Jen do místa, kde přímý paprsek narazil (+ poloměr) – nesbírá se skrz terén.</param>
        private bool TryPickupAssist(Ray ray, float maxDistance, out PickupItem pickup)
        {
            pickup = null;
            int mask = pickupLayer.value != 0 ? pickupLayer.value : LayerMask.GetMask("Pickup");
            if (mask == 0 || pickupAssistRadius <= 0f) return false;
            if (!Physics.SphereCast(ray, pickupAssistRadius, out RaycastHit hit, Mathf.Min(interactionDistance, maxDistance), mask, QueryTriggerInteraction.Collide))
                return false;
            return hit.collider.TryGetComponent(out pickup) && pickup.itemData != null;
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
            crosshairPickup.SetActive(!HideCrosshair);
        }

        /// <summary>
        /// Skryje interakční UI, resetuje reference na cílené objekty a obnoví výchozí crosshair.
        /// </summary>
        private void ClearUI()
        {
            currentPickup = null;
            currentHarvest = null;
            currentInstanced = default;

            pickupText.SetActive(false);
            crosshairDefault.SetActive(!HideCrosshair);
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