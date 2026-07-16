using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Orivilon.Core;
using Orivilon.SaveSystem;

namespace Orivilon.Multiplayer
{
    /// <summary>
    /// UI panel pro multiplayer – hostování a připojení ke hře.
    /// Přidejte jako komponent na libovolný panel v MainMenu scéně.
    ///
    /// SETUP V UNITY EDITORU:
    ///   1. Vytvořte nový Panel v MainMenu Canvas.
    ///   2. Přidejte tento skript na panel.
    ///   3. Přiřaďte reference (inputy, tlačítka, stavový text) v Inspektoru.
    ///   4. Panel se aktivuje/deaktivuje tlačítkem "Multiplayer" z hlavního menu.
    ///
    /// Flow pro hosta:
    ///   Vyber svět ze seznamu → klikni Host → hra se spustí normálně + čeká na připojení.
    ///
    /// Flow pro klienta:
    ///   Zadej IP:port → klikni Připojit → čekej na WorldState → klikni Hrát.
    /// </summary>
    public class MultiplayerMenuUI : MonoBehaviour
    {
        // ── Singleton ──────────────────────────────────────────────────────────
        public static MultiplayerMenuUI Instance { get; private set; }

        // ── Inspector references ───────────────────────────────────────────────
        [Header("Sekce Host")]
        [Tooltip("Panel se seznamem světů pro hostování (stejný jako single-player nebo samostatný)")]
        public GameObject hostPanel;

        [Header("Sekce Připojení")]
        public GameObject joinPanel;
        [Tooltip("Input pole pro IP adresu serveru")]
        public TMP_InputField ipInputField;
        [Tooltip("Input pole pro port (výchozí 7777)")]
        public TMP_InputField portInputField;

        [Header("Stavové UI")]
        [Tooltip("Text zobrazující aktuální stav připojení")]
        public TMP_Text statusText;
        [Tooltip("Tlačítko pro start hry po přijetí WorldState")]
        public Button playButton;

        [Header("Jméno hráče")]
        public TMP_InputField playerNameInput;

        // ── Privátní stav ──────────────────────────────────────────────────────
        private WorldData selectedHostWorld;
        private WorldData receivedGuestWorld;
        private bool      worldStateReceived;

        // ══════════════════════════════════════════════════════════════════════
        // Unity lifecycle
        // ══════════════════════════════════════════════════════════════════════

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;

            // Inicializuj UI
            if (portInputField != null)
                portInputField.text = "7777";

            if (playButton != null)
                playButton.interactable = false;
        }

        private void OnDestroy()
        {
            if (Instance == this)
                Instance = null;
        }

        // ══════════════════════════════════════════════════════════════════════
        // Tlačítka (připojují se přes Inspector nebo kódem)
        // ══════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Zobrazí sekci pro hostování.
        /// Tlačítko "Host" v hlavním menu.
        /// </summary>
        public void ShowHostPanel()
        {
            if (hostPanel != null) hostPanel.SetActive(true);
            if (joinPanel  != null) joinPanel .SetActive(false);
            SetStatus("Vyber svět a klikni 'Hostovat'.");
        }

        /// <summary>
        /// Zobrazí sekci pro připojení ke hře.
        /// Tlačítko "Připojit se" v hlavním menu.
        /// </summary>
        public void ShowJoinPanel()
        {
            if (hostPanel != null) hostPanel.SetActive(false);
            if (joinPanel  != null) joinPanel .SetActive(true);
            SetStatus("Zadej IP adresu hosta a klikni 'Připojit'.");
        }

        /// <summary>
        /// Spustí hru jako hostitel se světem vybraným v UI.
        /// Volá se tlačítkem "Hostovat" po výběru světa.
        /// </summary>
        public void OnHostButtonClicked()
        {
            if (MultiplayerManager.Instance == null)
            {
                SetStatus("CHYBA: MultiplayerManager nenalezen ve scéně!");
                return;
            }

            if (selectedHostWorld == null)
            {
                SetStatus("Nejprve vyber svět ze seznamu.");
                return;
            }

            ApplyPlayerName();
            MultiplayerManager.Instance.StartHost(selectedHostWorld);
            SetStatus($"Hostování na portu {MultiplayerManager.Instance.serverPort}. Čekám na hráče…");

            // Načti herní scénu
            LoadGame(selectedHostWorld);
        }

        /// <summary>
        /// Připojí se k hostu podle zadané IP.
        /// Volá se tlačítkem "Připojit".
        /// </summary>
        public void OnJoinButtonClicked()
        {
            if (MultiplayerManager.Instance == null)
            {
                SetStatus("CHYBA: MultiplayerManager nenalezen ve scéně!");
                return;
            }

            string ip   = ipInputField   != null ? ipInputField.text.Trim()   : "127.0.0.1";
            string portStr = portInputField != null ? portInputField.text.Trim() : "7777";

            if (string.IsNullOrEmpty(ip))
                ip = "127.0.0.1";

            if (!ushort.TryParse(portStr, out ushort port))
                port = 7777;

            ApplyPlayerName();
            MultiplayerManager.Instance.serverIP   = ip;
            MultiplayerManager.Instance.serverPort = port;
            MultiplayerManager.Instance.StartClient(ip, port);

            SetStatus($"Připojuji se na {ip}:{port}… Čekám na stav světa od hosta.");

            // POZNÁMKA: O stav světa NEžádáme ručně tady – v tuto chvíli spojení
            // ještě není navázané. Host pošle stav světa automaticky ve chvíli,
            // kdy se klient připojí (MultiplayerManager.OnClientConnected →
            // NetworkWorldSync.SendWorldStateTo). Klient ho přijme v
            // OnWorldStateResponse a aktivuje tlačítko „Hrát".
        }

        /// <summary>
        /// Spustí hru po přijetí WorldState od hosta.
        /// Tlačítko "Hrát" se aktivuje automaticky v OnWorldStateReceived().
        /// </summary>
        public void OnPlayButtonClicked()
        {
            if (receivedGuestWorld == null)
            {
                SetStatus("Stav světa ještě nebyl přijat.");
                return;
            }
            LoadGame(receivedGuestWorld);
        }

        /// <summary>
        /// Ukončí multiplayer session a vrátí se do offline main menu.
        /// </summary>
        public void OnDisconnectButtonClicked()
        {
            MultiplayerManager.Instance?.Shutdown();
            SetStatus("Odpojen.");

            if (playButton != null)
                playButton.interactable = false;

            worldStateReceived = false;
            receivedGuestWorld = null;
        }

        /// <summary>
        /// Nastaví svět vybraný pro hostování (voláno z WorldListElement nebo jiného UI).
        /// </summary>
        public void SelectWorldForHost(WorldData world)
        {
            selectedHostWorld = world;
            SetStatus($"Vybrán svět: {world.worldName}. Klikni 'Hostovat'.");
        }

        // ══════════════════════════════════════════════════════════════════════
        // Callback z NetworkWorldSync
        // ══════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Voláno z NetworkWorldSync po přijetí WorldState od hosta.
        /// Aktivuje tlačítko "Hrát".
        /// </summary>
        public void OnWorldStateReceived(WorldData world)
        {
            receivedGuestWorld = world;
            worldStateReceived = true;

            SetStatus($"Svět '{world.worldName}' přijat. Připraven ke hře!");

            if (playButton != null)
                playButton.interactable = true;
        }

        // ══════════════════════════════════════════════════════════════════════
        // Pomocné metody
        // ══════════════════════════════════════════════════════════════════════

        private void SetStatus(string msg)
        {
            if (statusText != null)
                statusText.text = msg;
            Debug.Log($"[MultiplayerMenuUI] {msg}");
        }

        private void ApplyPlayerName()
        {
            if (MultiplayerManager.Instance == null) return;
            if (playerNameInput != null && !string.IsNullOrWhiteSpace(playerNameInput.text))
                MultiplayerManager.Instance.localPlayerName = playerNameInput.text.Trim();
        }

        private void LoadGame(WorldData world)
        {
            GameManager.selectedWorld = world;

            var sceneLoader = FindFirstObjectByType<SceneLoader>();
            if (sceneLoader != null)
                sceneLoader.LoadGameDelayed();
            else
                UnityEngine.SceneManagement.SceneManager.LoadScene("Game");
        }
    }
}
