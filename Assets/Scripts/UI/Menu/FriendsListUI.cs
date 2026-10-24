using Orivilon.Core;
using Orivilon.Multiplayer;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Orivilon.UI.Menu
{
    /// <summary>
    /// Seznam Steam kamarádů, kteří hostují (nebo hrají v) svět Everlostu – kolo MP 17.10.2026, druhé kolo.
    /// Nemá vlastní panel: plní existující Play menu (<see cref="MainMenuUI.FriendsMode"/>) – stejný rám, řádky
    /// seznamu (prefab světa), detail vpravo, PLAY jako JOIN, odkaz vpravo nahoře jako Refresh, křížek/ESC = zpět.
    /// Join jde přes existující SteamLobbyManager.JoinLobby (loading, ClientJoinStep, cleanup).
    /// Stavy: načítání, prázdný seznam, Steam offline, nepřipojitelná lobby, chyba joinu po návratu z loadingu.
    /// Zamčená lobby → MultiplayerDialog s heslem; odmítnuté heslo → po návratu do menu znovu.
    /// </summary>
    public class FriendsListUI : MonoBehaviour
    {
        // Starší pole (původní samostatný panel) – zachována kvůli serializaci, nové menu je nepoužívá.
        [SerializeField, HideInInspector] private GameObject panelRoot;
        [SerializeField, HideInInspector] private Transform listContent;
        [SerializeField, HideInInspector] private GameObject entryPrefab;
        [SerializeField, HideInInspector] private Button closeButton;
        [SerializeField, HideInInspector] private Button refreshButton;

        [Tooltip("Play menu, které se přepne do režimu Multiplayer")]
        [SerializeField] private MainMenuUI menu;

        [Tooltip("Stavový text v oblasti seznamu (načítání / prázdné / offline / chyba)")]
        [SerializeField] private TMP_Text statusText;

        [Tooltip("Dialog s heslem")]
        [SerializeField] private MultiplayerDialog dialog;

        /// <summary>Aktivní instance (MainMenuController z ní čte detail vybraného kamaráda).</summary>
        public static FriendsListUI Current { get; private set; }
        /// <summary>Mění se s každou změnou výběru/stavu – detail se překreslí jen při změně.</summary>
        public static int Version { get; private set; }

        public FriendLobbyInfo? Selected { get; private set; }

        private bool active, refreshing, showLiveStatus;
        private Coroutine refreshRoutine;
        private WorldListElement selectedRow;
        private string pendingError;
        // 23.10.2026: chyba posledního joinu zůstává vidět, dokud hráč nezačne další akci. Dřív ji hned přepsalo
        // doběhnutí obnovy seznamu („No friends…“, prázdný text) nebo „Steam isn't running“ – Facepunch po
        // neúspěšném joinu na chvíli vypne Steam – a hráč po zavření loadingu neviděl žádné vysvětlení.
        private string stickyError;
        private static bool joinStartedHere;

        private void Awake()
        {
            Current = this;
            SteamLobbyManager.PasswordRequired += OnPasswordRequired;
            SteamLobbyManager.JoinMessage += OnJoinMessage;
            if (statusText != null) statusText.gameObject.SetActive(false);

            // Návrat z neúspěšného joinu: chyba patří do seznamu kamarádů (jinak by se otevřel výběr světů).
            if (joinStartedHere && !string.IsNullOrEmpty(SceneLoader.PendingMenuError))
            {
                pendingError = SceneLoader.PendingMenuError;
                SceneLoader.PendingMenuError = null;
            }
            joinStartedHere = false;
        }

        private void Start()
        {
            ulong retry = SteamLobbyManager.RetryLobbyId;
            if (retry != 0)
            {
                string err = SteamLobbyManager.RetryError, host = SteamLobbyManager.RetryHost;
                SteamLobbyManager.ConsumeRetry();
                Open();
                if (retry != ulong.MaxValue) AskPassword(retry, host, err); else { stickyError = err; SetStatus(err); }
                pendingError = null;
                return;
            }
            if (pendingError != null)
            {
                Open();
                stickyError = pendingError;
                SetStatus(pendingError);
                pendingError = null;
            }
        }

        private void OnDestroy()
        {
            SteamLobbyManager.PasswordRequired -= OnPasswordRequired;
            SteamLobbyManager.JoinMessage -= OnJoinMessage;
            if (Current == this) Current = null;
        }

        private void Update()
        {
            if (active && showLiveStatus && SteamLobbyManager.Instance != null)
                SetStatus(SteamLobbyManager.Instance.Status);
        }

        public bool IsOpen => active;

        /// <summary>Otevře Play menu v režimu Multiplayer (stejná cesta jako tlačítko Multiplayer).</summary>
        public void Open()
        {
            if (menu == null) { Debug.LogWarning("[FriendsListUI] MainMenuUI není přiřazen."); return; }
            if (menu.FriendsMode && active) { Refresh(); return; }
            menu.OpenMultiplayerMenu();
        }

        /// <summary>Volá MainMenuUI při vstupu do režimu Multiplayer.</summary>
        public void BeginMenu(MainMenuUI owner)
        {
            menu = owner;
            active = true;
            Select(null, null);
            Refresh();
        }

        /// <summary>Volá MainMenuUI při odchodu z režimu (zpět, ESC, světy).</summary>
        public void EndMenu()
        {
            stickyError = null;
            active = false;
            showLiveStatus = false;
            if (refreshRoutine != null) { StopCoroutine(refreshRoutine); refreshRoutine = null; }
            refreshing = false;
            Select(null, null);
            SetStatus("");
        }

        /// <summary>Znovu načte seznam (odkaz Refresh vpravo nahoře).</summary>
        public void Refresh()
        {
            if (!active) return;
            showLiveStatus = false;
            menu.ClearListRows();
            Select(null, null);
            var manager = SteamLobbyManager.Instance;
            if (manager == null) { SetStatus("Steam support is missing from this build."); return; }
            if (!manager.SteamReady) { SetStatus("Steam isn't running or you're offline. Start Steam, then press Refresh."); return; }
            if (refreshing) return;
            refreshing = true;
            SetStatus("Looking for friends playing Everlost...");
            refreshRoutine = StartCoroutine(manager.RefreshFriendLobbies(Fill));
        }

        /// <summary>QA/diagnostika: naplní seznam danými položkami (stejná cesta jako výsledek ze Steamu).</summary>
        public void Fill(List<FriendLobbyInfo> lobbies)
        {
            refreshing = false;
            refreshRoutine = null;
            if (!active) return;
            menu.ClearListRows();
            Select(null, null);
            if (lobbies.Count == 0)
            {
                var m = SteamLobbyManager.Instance;
                SetStatus(m != null && !m.SteamReady
                    ? "Steam isn't running or you're offline. Start Steam, then press Refresh."
                    : "No friends are hosting an Everlost world right now.\nAsk a friend to host a world in Multiplayer mode, then press Refresh.");
                return;
            }
            int joinable = 0;
            foreach (var info in lobbies) { CreateRow(info); if (info.joinable) joinable++; }
            SetStatus(joinable == 0 ? "None of these worlds can be joined right now." : "");
        }

        private static string Host(FriendLobbyInfo info) => string.IsNullOrEmpty(info.hostName) ? info.friendName : info.hostName;

        private void CreateRow(FriendLobbyInfo info)
        {
            string title = string.IsNullOrEmpty(info.worldName) ? Host(info) : info.worldName + "  ·  " + Host(info);
            if (!info.joinable) title += "  (" + info.blockedReason + ")";
            var row = menu.CreateListRow(title, out Button btn, out WorldListElement visual);
            if (row == null) return;
            row.name = "Friend_" + info.lobbyId;
            if (btn != null) btn.onClick.AddListener(() => Select(info, visual));
        }

        private void Select(FriendLobbyInfo? info, WorldListElement row)
        {
            if (selectedRow != null) selectedRow.SetSelected(false);
            selectedRow = row;
            if (selectedRow != null) selectedRow.SetSelected(true);
            Selected = info;
            Version++;
        }

        /// <summary>JOIN (tlačítko PLAY v režimu Multiplayer).</summary>
        public void JoinSelected()
        {
            if (!Selected.HasValue || SteamLobbyManager.Instance == null) return;
            var info = Selected.Value;
            if (!info.joinable) { SetStatus(info.blockedReason); return; }
            // 23.10.2026: nejdřív čerstvá data lobby (zamčení se mohlo od obnovení seznamu změnit) – bez loadingu;
            // zamčený svět → dialog hesla, jinak rovnou skutečný join (SteamLobbyManager.BeginJoin).
            stickyError = null;
            joinStartedHere = true;
            showLiveStatus = true;
            SteamLobbyManager.Instance.BeginJoin(info.lobbyId, Host(info));
        }

        private void OnJoinMessage(string text)
        {
            if (!active) Open();
            showLiveStatus = false;
            joinStartedHere = false;
            stickyError = text;
            SetStatus(text);
        }

        private void OnPasswordRequired(ulong lobbyId, string host, string error)
        {
            if (!active) Open();
            showLiveStatus = false;
            AskPassword(lobbyId, host, error);
        }

        private void AskPassword(ulong lobbyId, string host, string error)
        {
            if (dialog == null) { StartJoin(lobbyId, "", host); return; }
            dialog.Show(string.IsNullOrEmpty(host) ? "Join world" : "Join " + host,
                "This world is password protected. Enter the password the host gave you.",
                "JOIN", error, true,
                pw => StartJoin(lobbyId, pw, host),
                () => SetStatus("Join cancelled."));
        }

        private void StartJoin(ulong lobbyId, string password, string host)
        {
            stickyError = null;
            showLiveStatus = true;
            joinStartedHere = true;
            SteamLobbyManager.Instance.JoinLobby(lobbyId, password, host);
        }

        private void SetStatus(string text)
        {
            // Probíhající hledání / prázdný výsledek nepřepíše chybu posledního joinu.
            if (stickyError != null && text != stickyError)
                text = string.IsNullOrEmpty(text) ? stickyError : stickyError + "\n" + text;
            if (statusText == null) return;
            statusText.text = text ?? "";
            statusText.gameObject.SetActive(active && !string.IsNullOrEmpty(text));
        }
    }
}
