using System;
using Orivilon.Player;
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

        // ══════════════════════════════════════════════════════════════════════
        // Stav pohybu (plavání, pád, let)
        // ══════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Poslední známý stav pohybu vzdáleného hráče.
        ///
        /// <para>Zatím se po síti neposílá – <c>NetworkPlayerBridge</c> odesílá jen
        /// pozici a rotaci. Háček existuje proto, aby rozšíření drátového formátu
        /// byla změna JEN v <c>NetworkWorldSync</c>: lokální hráč už stav vystavuje
        /// jako <c>FirstPersonController.NetworkMoveState</c> a hlásí ho událostí
        /// <c>MoveStateChanged</c>, tady ho stačí přijmout.</para>
        ///
        /// <para>Proč to nejde odvodit z pozice: plavání není vidět na rychlosti.
        /// Hráč stojící po krk ve vodě a hráč stojící na břehu mají tutéž transformaci
        /// a mají se hýbat i znít jinak.</para>
        /// </summary>
        public PlayerMoveState MoveState { get; private set; } = PlayerMoveState.Grounded;

        /// <summary>Hlásí změnu stavu (starý, nový) – pro animace, zvuky a efekty.</summary>
        public event Action<PlayerMoveState, PlayerMoveState> MoveStateChanged;

        /// <summary>Plave tenhle vzdálený hráč?</summary>
        public bool IsSwimming => MoveState == PlayerMoveState.Swimming;

        /// <summary>
        /// Přijme stav pohybu ze sítě. Volá se ze stejného místa jako
        /// <see cref="SetTargetTransform"/>.
        /// </summary>
        public void SetMoveState(PlayerMoveState state)
        {
            if (state == MoveState) return;

            PlayerMoveState previous = MoveState;
            MoveState = state;
            MoveStateChanged?.Invoke(previous, state);
        }

        /// <summary>
        /// Přetížení pro syrovou hodnotu z drátu. Neznámé číslo (novější klient)
        /// se zahodí místo přetypování na nesmyslný stav.
        /// </summary>
        public void SetMoveState(byte raw)
        {
            if (!Enum.IsDefined(typeof(PlayerMoveState), raw)) return;
            SetMoveState((PlayerMoveState)raw);
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
