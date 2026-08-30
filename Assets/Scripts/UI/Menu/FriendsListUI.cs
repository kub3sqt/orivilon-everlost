using Orivilon.Multiplayer;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Orivilon.UI.Menu
{
    /// <summary>
    /// Panel se seznamem kamarádů na Steamu, kteří právě hrají a mají otevřenou lobby.
    /// Otevírá ho MainMenuUI (multiplayer tlačítko bez označeného světa).
    /// Kliknutí na kamaráda se připojí do jeho lobby přes SteamLobbyManager –
    /// načtení herní scény pak řeší manager sám po přijetí světa od hosta.
    /// </summary>
    public class FriendsListUI : MonoBehaviour
    {
        [Tooltip("Kořenový objekt panelu (zapíná/vypíná se)")]
        [SerializeField] private GameObject panelRoot;

        [Tooltip("Kontejner, do kterého se instantiují položky kamarádů")]
        [SerializeField] private Transform listContent;

        [Tooltip("Prefab položky – stačí Button a TMP text kdekoliv uvnitř")]
        [SerializeField] private GameObject entryPrefab;

        [Tooltip("Text pro stav (prázdný seznam / průběh připojování)")]
        [SerializeField] private TMP_Text statusText;

        [Tooltip("Tlačítko zavření panelu (listener se napojí sám)")]
        [SerializeField] private Button closeButton;

        /// <summary>Po kliknutí na join se v statusText živě zobrazuje stav ze SteamLobbyManageru.</summary>
        private bool showLiveStatus;

        private void Awake()
        {
            if (closeButton != null)
                closeButton.onClick.AddListener(Close);
        }

        private void Update()
        {
            if (showLiveStatus && statusText != null && SteamLobbyManager.Instance != null)
                statusText.text = SteamLobbyManager.Instance.Status;
        }

        /// <summary>Otevře panel a načte aktuální seznam kamarádů. Volá MainMenuUI.</summary>
        public void Open()
        {
            if (panelRoot != null)
                panelRoot.SetActive(true);

            Refresh();
        }

        /// <summary>Zavře panel. Volatelné i z tlačítka.</summary>
        public void Close()
        {
            if (panelRoot != null)
                panelRoot.SetActive(false);
        }

        /// <summary>Znovu načte seznam kamarádů (volatelné i z refresh tlačítka).</summary>
        public void Refresh()
        {
            showLiveStatus = false;
            ClearList();

            var manager = SteamLobbyManager.Instance;

            if (manager == null)
            {
                SetStatus("Steam is not available.");
                return;
            }

            List<FriendLobbyInfo> lobbies = manager.GetFriendLobbies();

            if (lobbies.Count == 0)
            {
                SetStatus(manager.SteamReady
                    ? "No friends are currently playing."
                    : manager.Status);
                return;
            }

            SetStatus("");

            foreach (var info in lobbies)
                CreateEntry(info);
        }

        /// <summary>
        /// Smaže všechny položky seznamu. Zachovává "TopSpace" a "BottomSpace"
        /// (layout padding, stejný vzor jako seznam světů).
        /// </summary>
        private void ClearList()
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

        /// <summary>Vytvoří jednu položku kamaráda a napojí join na kliknutí.</summary>
        private void CreateEntry(FriendLobbyInfo info)
        {
            if (entryPrefab == null || listContent == null)
            {
                Debug.LogError("[FriendsListUI] Chybí entry prefab nebo list content!");
                return;
            }

            GameObject entry = Instantiate(entryPrefab, listContent);

            TMP_Text label = entry.GetComponentInChildren<TMP_Text>();
            if (label != null)
            {
                label.text = string.IsNullOrEmpty(info.worldName)
                    ? info.friendName
                    : $"{info.friendName} — {info.worldName}";
            }

            Button btn = entry.GetComponentInChildren<Button>();
            if (btn != null)
            {
                ulong lobbyId = info.lobbyId;
                btn.onClick.AddListener(() => JoinFriend(lobbyId));
            }

            Transform bottomSpace = listContent.Find("BottomSpace");
            if (bottomSpace != null)
                entry.transform.SetSiblingIndex(bottomSpace.GetSiblingIndex());
        }

        /// <summary>Připojí se do lobby kamaráda. Scénu načte SteamLobbyManager po přijetí světa.</summary>
        private void JoinFriend(ulong lobbyId)
        {
            if (SteamLobbyManager.Instance == null) return;

            showLiveStatus = true;
            SteamLobbyManager.Instance.JoinLobby(lobbyId);
        }

        /// <summary>Nastaví stavový text (pokud je přiřazen).</summary>
        private void SetStatus(string text)
        {
            if (statusText != null)
                statusText.text = text;
        }
    }
}
