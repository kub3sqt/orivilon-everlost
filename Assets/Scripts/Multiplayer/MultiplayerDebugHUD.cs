using UnityEngine;
using UnityEngine.SceneManagement;
using Orivilon.Core;
using Orivilon.SaveSystem;

namespace Orivilon.Multiplayer
{
    /// <summary>
    /// Jednoduchý překryvný HUD (IMGUI) pro hostování a připojení PŘÍMO V BUILDU.
    /// Slouží k testování multiplayeru mezi dvěma počítači (např. Mac ↔ Windows)
    /// bez nutnosti drátovat UI tlačítka v MainMenu.
    ///
    /// SETUP:
    ///   Přidejte tento komponent na GameObject "MultiplayerManager" v MainMenu scéně
    ///   (stejný GO, kde je MultiplayerManager). Nic víc není potřeba.
    ///
    /// OVLÁDÁNÍ:
    ///   - HUD se zobrazí v levém horním rohu v MainMenu scéně.
    ///   - Klávesa F9 HUD skryje/zobrazí.
    ///   - HOST: zadej jméno → klikni "HOSTOVAT" → načte se svět a čeká na hráče.
    ///   - JOIN: zadej IP hosta + jméno → klikni "PŘIPOJIT" → po přijetí světa
    ///           se hra načte automaticky.
    /// </summary>
    [DefaultExecutionOrder(-80)]
    public class MultiplayerDebugHUD : MonoBehaviour
    {
        [Header("Zobrazení")]
        [Tooltip("Zobrazovat HUD i ve finálním buildu (ne jen v editoru)?")]
        public bool showInBuild = true;

        [Tooltip("Název herní scény, která se načte po startu.")]
        public string gameSceneName = "Game";

        [Tooltip("Seed použitý při hostování (textový).")]
        public string hostSeed = "everlost";

        // ── Stav UI ────────────────────────────────────────────────────────────
        private string ip         = "127.0.0.1";
        private string port       = "7777";
        private string playerName = "Hráč";
        private string status     = "Připraveno.";
        private bool   visible     = true;
        private bool   joining     = false;

#if FACEPUNCH_STEAMWORKS
        private string steamLobbyIdInput = "";
        private bool   showFriends       = false;
        private Vector2 friendsScroll;
#endif

        private void Update()
        {
            // Přepínání viditelnosti klávesou F9
            if (Input.GetKeyDown(KeyCode.F9))
                visible = !visible;

            // Po připojení klienta: jakmile host pošle stav světa, nastaví se
            // GameManager.selectedWorld (v NetworkWorldSync.OnWorldStateResponse).
            // Pak načteme herní scénu.
            if (joining &&
                GameManager.selectedWorld != null &&
                SceneManager.GetActiveScene().name != gameSceneName)
            {
                joining = false;
                status = "Svět přijat, načítám hru…";
                SceneManager.LoadScene(gameSceneName);
            }
        }

        private void OnGUI()
        {
#if !UNITY_EDITOR
            if (!showInBuild) return;
#endif
            if (!visible) return;

            // HUD kreslíme jen v MainMenu (v herní scéně by překážel).
            // Když už jsme ve hře, ukážeme jen malý stavový řádek.
            bool inMenu = SceneManager.GetActiveScene().name != gameSceneName;

            const int w = 300;
            int h = inMenu ? 210 : 60;
#if FACEPUNCH_STEAMWORKS
            if (inMenu) h += 200;                       // Steam sekce v menu
            else        h += showFriends ? 290 : 130;   // Steam info + pozvánky za běhu
#endif
            GUILayout.BeginArea(new Rect(10, 10, w, h), GUI.skin.box);

            GUILayout.Label("<b>MULTIPLAYER TEST</b> (F9 skryje)");

            if (MultiplayerManager.IsActive)
            {
                string role = MultiplayerManager.IsHost ? "HOST" : "KLIENT";
                GUILayout.Label($"Stav: připojen jako {role}");
#if FACEPUNCH_STEAMWORKS
                DrawSteamConnectedSection();
#endif
                if (GUILayout.Button("Odpojit"))
                {
                    MultiplayerManager.Instance?.Shutdown();
                    SteamLobbyManager.Instance?.LeaveLobby();
                    status = "Odpojeno.";
                }
                GUILayout.Label(status);
                GUILayout.EndArea();
                return;
            }

            if (!inMenu)
            {
                GUILayout.Label(status);
                GUILayout.EndArea();
                return;
            }

            GUILayout.BeginHorizontal();
            GUILayout.Label("Jméno:", GUILayout.Width(70));
            playerName = GUILayout.TextField(playerName);
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            GUILayout.Label("IP hosta:", GUILayout.Width(70));
            ip = GUILayout.TextField(ip);
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            GUILayout.Label("Port:", GUILayout.Width(70));
            port = GUILayout.TextField(port);
            GUILayout.EndHorizontal();

            GUILayout.Space(6);

            if (GUILayout.Button("▶ HOSTOVAT (start serveru)"))
                StartHosting();

            if (GUILayout.Button("🔌 PŘIPOJIT SE na IP"))
                StartJoining();

#if FACEPUNCH_STEAMWORKS
            DrawSteamMenuSection();
#endif

            GUILayout.Space(4);
            GUILayout.Label(status);

            GUILayout.EndArea();
        }

#if FACEPUNCH_STEAMWORKS
        // ── Steam sekce (menu) ─────────────────────────────────────────────────

        private void DrawSteamMenuSection()
        {
            var steam = SteamLobbyManager.Instance;

            GUILayout.Space(8);
            GUILayout.Label("<b>— STEAM (přes internet) —</b>");

            if (steam == null)
            {
                GUILayout.Label("SteamLobbyManager není ve scéně.");
                return;
            }

            if (MultiplayerManager.Instance == null)
            {
                GUILayout.Label("MultiplayerManager není ve scéně.");
                return;
            }

            if (!steam.SteamReady)
            {
                GUILayout.Label(steam.Status);
                return;
            }

            GUILayout.Label($"Přihlášen: {steam.LocalSteamName}");

            if (GUILayout.Button("▶ HOSTOVAT přes STEAM"))
            {
                MultiplayerManager.Instance.localPlayerName = playerName;
                steam.HostLobby(BuildHostWorld());
                joining = false;
            }

            GUILayout.BeginHorizontal();
            GUILayout.Label("Lobby ID:", GUILayout.Width(70));
            steamLobbyIdInput = GUILayout.TextField(steamLobbyIdInput);
            GUILayout.EndHorizontal();

            if (GUILayout.Button("🔌 PŘIPOJIT přes STEAM (Lobby ID)"))
            {
                if (ulong.TryParse(steamLobbyIdInput.Trim(), out ulong lobbyId) && lobbyId != 0)
                {
                    MultiplayerManager.Instance.localPlayerName = playerName;
                    steam.JoinLobby(lobbyId);
                    joining = false; // scénu načte SteamLobbyManager po přijetí světa
                }
                else
                {
                    status = "Neplatné Lobby ID.";
                }
            }

            GUILayout.Label("<i>Pozvánka od kamaráda funguje i bez ID –\npřijmi ji ve Steamu, hra se připojí sama.</i>");
            GUILayout.Label(steam.Status);
        }

        /// <summary>Steam info + pozvánky, když už session běží (host).</summary>
        private void DrawSteamConnectedSection()
        {
            var steam = SteamLobbyManager.Instance;
            if (steam == null || !MultiplayerManager.IsHost) return;

            string lobbyId = steam.CurrentLobbyIdString;
            if (lobbyId == null) return;

            GUILayout.Label($"Steam Lobby: {lobbyId}");

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Kopírovat ID"))
            {
                GUIUtility.systemCopyBuffer = lobbyId;
                status = "Lobby ID zkopírováno.";
            }
            if (GUILayout.Button("Pozvat (overlay)"))
                steam.OpenInviteOverlay();
            GUILayout.EndHorizontal();

            if (GUILayout.Button(showFriends ? "Skrýt přátele" : "Pozvat ze seznamu přátel"))
                showFriends = !showFriends;

            if (showFriends)
                DrawFriendsList(steam);
        }

        /// <summary>Seznam online přátel s tlačítkem Pozvat (funguje i v editoru bez overlay).</summary>
        private void DrawFriendsList(SteamLobbyManager steam)
        {
            friendsScroll = GUILayout.BeginScrollView(friendsScroll, GUILayout.Height(140));
            foreach (var friend in Steamworks.SteamFriends.GetFriends())
            {
                if (!friend.IsOnline) continue;

                GUILayout.BeginHorizontal();
                GUILayout.Label(friend.Name, GUILayout.Width(160));
                if (GUILayout.Button("Pozvat", GUILayout.Width(70)))
                    steam.InviteFriend(friend.Id.Value);
                GUILayout.EndHorizontal();
            }
            GUILayout.EndScrollView();
        }
#endif

        // ── Akce ─────────────────────────────────────────────────────────────

        private void StartHosting()
        {
            if (MultiplayerManager.Instance == null)
            {
                status = "CHYBA: MultiplayerManager není ve scéně.";
                return;
            }

            var world = BuildHostWorld();
            MultiplayerManager.Instance.localPlayerName = playerName;
            MultiplayerManager.Instance.StartHost(world);

            status = $"Hostuji na portu {MultiplayerManager.Instance.serverPort}. Načítám hru…";
            SceneManager.LoadScene(gameSceneName);
        }

        private void StartJoining()
        {
            if (MultiplayerManager.Instance == null)
            {
                status = "CHYBA: MultiplayerManager není ve scéně.";
                return;
            }

            if (!ushort.TryParse(port, out ushort p))
                p = 7777;

            MultiplayerManager.Instance.localPlayerName = playerName;
            MultiplayerManager.Instance.StartClient(string.IsNullOrWhiteSpace(ip) ? "127.0.0.1" : ip.Trim(), p);

            joining = true;
            status = $"Připojuji se na {ip}:{p}… čekám na svět od hosta.";
        }

        /// <summary>Vytvoří lokální WorldData pro hostování (složka v persistentDataPath).</summary>
        private WorldData BuildHostWorld()
        {
            var world = new WorldData("MP_Host", hostSeed);

            string folder = System.IO.Path.Combine(
                Application.persistentDataPath, "worlds", "mp_host");
            System.IO.Directory.CreateDirectory(folder);
            System.IO.Directory.CreateDirectory(System.IO.Path.Combine(folder, "player"));
            System.IO.Directory.CreateDirectory(System.IO.Path.Combine(folder, "chunks"));

            world.folderPath = folder;
            world.lastPlayed = System.DateTime.Now.ToString("o");
            return world;
        }
    }
}
