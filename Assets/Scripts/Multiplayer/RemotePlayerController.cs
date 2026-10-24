using System;
using Orivilon.Player;
using TMPro;
using UnityEngine;

namespace Orivilon.Multiplayer
{
    /// <summary>
    /// Vizuální reprezentace vzdáleného hráče ve světě.
    /// Zobrazuje cestovatele (nebo vlastní model z prefabu) a jméno hráče nad hlavou.
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
            // První pozice nebo skok (teleport, respawn): přeskočit rovnou, jinak by Lerp táhl kapsli
            // napříč světem a marker na kompasu by ukazoval nesmysl.
            if (!HasNetworkPosition || (position - transform.position).sqrMagnitude > SnapDistance * SnapDistance)
            {
                transform.position = position;
                transform.rotation = rotation;
            }
            targetPosition = position;
            targetRotation = rotation;
            LastNetworkPosition = position;
            HasNetworkPosition = true;
        }

        /// <summary>Nad touto vzdáleností se pozice nepřelévá, ale přeskočí (m).</summary>
        public const float SnapDistance = 60f;

        /// <summary>Poslední pozice přijatá ze sítě (host podle ní validuje dropy, pickupy a /tp).</summary>
        public Vector3 LastNetworkPosition { get; private set; }

        /// <summary>Dorazila už aspoň jedna pozice?</summary>
        public bool HasNetworkPosition { get; private set; }

        // ══════════════════════════════════════════════════════════════════════
        // Stav pohybu (plavání, pád, let)
        // ══════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Poslední známý stav pohybu vzdáleného hráče.
        ///
        /// <para>Stav přichází s volitelným animačním dodatkem zprávy pozice.
        /// Starší klient bez dodatku používá výchozí Grounded.</para>
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
        /// Vytvoří výchozí vizuál vzdáleného hráče (cestovatel + jmenovka).
        /// Volá se z MultiplayerManager pokud remotePlayerPrefab není přiřazeno.
        /// </summary>
        public static GameObject CreateDefaultVisual()
        {
            var root = new GameObject("RemotePlayer");

            root.AddComponent<TravelerAvatar>().InitializeRemote();

            // Jmenovka (TextMeshPro – World Space)
            var tmp = CreateNameLabel(root.transform, true);

            var ctrl = root.AddComponent<RemotePlayerController>();
            ctrl.nameLabel = tmp;

            // Jmenovka vždy směrem ke kameře (Billboard)
            root.AddComponent<RemotePlayerBillboard>();

            return root;
        }

        /// <summary>
        /// Jmenovka vzdáleného hráče. <paramref name="styled"/> = obrys (mění materiál TMP, jen v Play Mode);
        /// editorová kontrola (MultiplayerChecks) ověřuje bez něj jen nastavení textu, aby v edit mode nevznikal
        /// materiál navíc.
        /// </summary>
        public static TextMeshPro CreateNameLabel(Transform parent, bool styled)
        {
            var labelGo = new GameObject("NameLabel");
            labelGo.transform.SetParent(parent, false);
            labelGo.transform.localPosition = new Vector3(0f, 2.4f, 0f);

            var tmp = labelGo.AddComponent<TextMeshPro>();
            tmp.text               = "Player";
            tmp.richText           = false;   // Steam jméno nesmí vložit TMP tagy
            tmp.fontSize           = 3f;
            tmp.alignment          = TextAlignmentOptions.Center;
            tmp.color              = Color.white;
            if (styled)
            {
                tmp.outlineColor   = Color.black;
                tmp.outlineWidth   = 0.2f;
            }
            return tmp;
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
