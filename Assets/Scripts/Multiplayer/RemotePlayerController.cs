using TMPro;
using UnityEngine;

namespace Orivilon.Multiplayer
{
    /// <summary>
    /// Vizuální reprezentace vzdáleného hráče ve světě.
    /// Zobrazuje kapsli (nebo vlastní model z prefabu) a jméno hráče nad hlavou.
    /// Plynule interpoluje k cílové pozici/rotaci přijaté ze sítě.
    /// </summary>
    public class RemotePlayerController : MonoBehaviour
    {
        // ── Identita ───────────────────────────────────────────────────────────
        /// <summary>NGO clientId tohoto vzdáleného hráče.</summary>
        public ulong ClientId { get; set; }

        // ── Cílová transformace (přijatá ze sítě) ─────────────────────────────
        private Vector3    targetPosition;
        private Quaternion targetRotation = Quaternion.identity;

        // ── Komponenty ─────────────────────────────────────────────────────────
        private TextMeshPro nameLabel;

        [Tooltip("Rychlost interpolace pohybu (vyšší = přesnější, nižší = plynulejší).")]
        public float interpolationSpeed = 15f;

        // ══════════════════════════════════════════════════════════════════════
        // Unity lifecycle
        // ══════════════════════════════════════════════════════════════════════

        private void Awake()
        {
            targetPosition = transform.position;
            targetRotation = transform.rotation;
        }

        private void Update()
        {
            // Plynulá interpolace k cílové transformaci
            float t = Time.deltaTime * interpolationSpeed;
            transform.position = Vector3.Lerp(transform.position, targetPosition, t);
            transform.rotation = Quaternion.Slerp(transform.rotation, targetRotation, t);
        }

        // ══════════════════════════════════════════════════════════════════════
        // Veřejné API
        // ══════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Nastaví cílovou transformaci přijatou ze sítě. Volá se z NetworkWorldSync.
        /// </summary>
        public void SetTargetTransform(Vector3 position, Quaternion rotation)
        {
            targetPosition = position;
            targetRotation = rotation;
        }

        /// <summary>
        /// Nastaví nebo změní jméno zobrazované nad hráčem.
        /// </summary>
        public void SetPlayerName(string playerName)
        {
            if (nameLabel != null)
                nameLabel.text = playerName;

            // Stejné jméno se ukazuje i u ikony na kompasu, pokud marker existuje.
            var marker = GetComponent<Orivilon.UI.HUD.CompassMarker>();
            if (marker != null)
                marker.Label = playerName;
        }

        // ══════════════════════════════════════════════════════════════════════
        // Tovární metoda – vytvoří výchozí vizuál za běhu
        // ══════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Vytvoří výchozí vizuál vzdáleného hráče (modrá kapsle + jmenovka).
        /// Volá se z MultiplayerManager pokud remotePlayerPrefab není přiřazeno.
        /// </summary>
        public static GameObject CreateDefaultVisual()
        {
            var root = new GameObject("RemotePlayer");

            // Kapsle (přibližné rozměry hráče)
            var capsule = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            capsule.transform.SetParent(root.transform, false);
            capsule.transform.localPosition = new Vector3(0f, 1f, 0f); // střed kapsle ve výšce 1

            // Odliš od terénu barvou
            var rend = capsule.GetComponent<Renderer>();
            if (rend != null)
            {
                rend.material = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                rend.material.color = new Color(0.2f, 0.5f, 1f); // modrá
            }

            // Odstraň případný collider aby neblokoval interakci
            var col = capsule.GetComponent<Collider>();
            if (col != null)
                Destroy(col);

            // Jmenovka (TextMeshPro – World Space)
            var labelGo = new GameObject("NameLabel");
            labelGo.transform.SetParent(root.transform, false);
            labelGo.transform.localPosition = new Vector3(0f, 2.4f, 0f);

            var tmp = labelGo.AddComponent<TextMeshPro>();
            tmp.text               = "Player";
            tmp.fontSize           = 3f;
            tmp.alignment          = TextAlignmentOptions.Center;
            tmp.color              = Color.white;
            tmp.outlineColor       = Color.black;
            tmp.outlineWidth       = 0.2f;

            var ctrl = root.AddComponent<RemotePlayerController>();
            ctrl.nameLabel = tmp;

            // Jmenovka vždy směrem ke kameře (Billboard)
            root.AddComponent<RemotePlayerBillboard>();

            return root;
        }
    }

    /// <summary>
    /// Jednoduchý billboard – otočí GameObject k hlavní kameře každý snímek.
    /// Slouží pro jmenovky vzdálených hráčů.
    /// </summary>
    internal class RemotePlayerBillboard : MonoBehaviour
    {
        private Transform nameLabel;

        private void Awake()
        {
            var tmp = GetComponentInChildren<TextMeshPro>();
            if (tmp != null)
                nameLabel = tmp.transform;
        }

        private void LateUpdate()
        {
            if (nameLabel == null || Camera.main == null) return;
            nameLabel.forward = Camera.main.transform.forward;
        }
    }
}
