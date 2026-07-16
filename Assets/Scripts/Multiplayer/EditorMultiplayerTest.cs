// Tento skript přidává do Inspektoru tlačítka pro rychlé testování multiplayeru
// bez Steamu. Tlačítka vykresluje vlastní editor (viz EditorMultiplayerTestEditor
// na konci souboru) – žádná externí knihovna (EasyButtons apod.) není potřeba.

#if UNITY_EDITOR
using UnityEditor;
#endif

using UnityEngine;
using Orivilon.SaveSystem;

namespace Orivilon.Multiplayer
{
    /// <summary>
    /// Editor helper pro testování multiplayeru přímo v Unity.
    ///
    /// POUŽITÍ:
    ///   1. Přidejte tento skript na stejný GameObject jako MultiplayerManager.
    ///   2. V Inspektoru uvidíte tlačítka Host / Připojit / Odpojit.
    ///   3. Pro test dvou instancí použijte:
    ///      - Multiplayer Play Mode (Window → Multiplayer → Multiplayer Play Mode)
    ///      - nebo build hry (druhá instance) + Editor jako host.
    ///
    /// Testování:
    ///   - Klikni "Start jako HOST" → Editor běží jako server+klient.
    ///   - V druhé instanci klikni "Připojit se na localhost".
    /// </summary>
    public class EditorMultiplayerTest : MonoBehaviour
    {
        [Header("Editor Test Nastavení")]
        [Tooltip("IP pro test připojení (výchozí localhost)")]
        public string testIP   = "127.0.0.1";
        [Tooltip("Port pro test připojení")]
        public ushort testPort = 7777;

        [Tooltip("Testovací svět (volitelný – pokud null, vytvoří se dummy svět)")]
        public WorldData testWorld;

        /// <summary>Spustí Editor jako hostitele (server + klient).</summary>
        public void EditorStartHost()
        {
            if (MultiplayerManager.Instance == null)
            {
                Debug.LogError("[EditorTest] MultiplayerManager.Instance je null. Je skript na správném GO?");
                return;
            }

            var world = testWorld ?? CreateDummyWorld();
            MultiplayerManager.Instance.StartHost(world);
            Debug.Log("[EditorTest] Host spuštěn.");
        }

        /// <summary>Připojí Editor jako klienta na testIP:testPort.</summary>
        public void EditorStartClient()
        {
            if (MultiplayerManager.Instance == null)
            {
                Debug.LogError("[EditorTest] MultiplayerManager.Instance je null.");
                return;
            }

            MultiplayerManager.Instance.serverIP   = testIP;
            MultiplayerManager.Instance.serverPort = testPort;
            MultiplayerManager.Instance.StartClient(testIP, testPort);
            Debug.Log($"[EditorTest] Klient se připojuje na {testIP}:{testPort}");
        }

        /// <summary>Ukončí síťovou session.</summary>
        public void EditorShutdown()
        {
            MultiplayerManager.Instance?.Shutdown();
            Debug.Log("[EditorTest] Session ukončena.");
        }

        // ── Privátní ───────────────────────────────────────────────────────────

        private static WorldData CreateDummyWorld()
        {
            string folder = System.IO.Path.Combine(
                Application.persistentDataPath, "worlds", "editor_test");
            System.IO.Directory.CreateDirectory(folder);
            System.IO.Directory.CreateDirectory(System.IO.Path.Combine(folder, "player"));
            System.IO.Directory.CreateDirectory(System.IO.Path.Combine(folder, "chunks"));

            return new WorldData("EditorTest", "test_seed")
            {
                folderPath = folder,
                lastPlayed = System.DateTime.Now.ToString("o")
            };
        }
    }

#if UNITY_EDITOR
    /// <summary>
    /// Vlastní editor – vykreslí testovací tlačítka pod standardním inspektorem.
    /// Nahrazuje EasyButtons, aby projekt nezávisel na externí knihovně.
    /// </summary>
    [CustomEditor(typeof(EditorMultiplayerTest))]
    public class EditorMultiplayerTestEditor : Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();

            var t = (EditorMultiplayerTest)target;

            GUILayout.Space(10);
            EditorGUILayout.LabelField("Multiplayer test (jen v editoru)", EditorStyles.boldLabel);

            using (new EditorGUI.DisabledScope(!Application.isPlaying))
            {
                if (!Application.isPlaying)
                    EditorGUILayout.HelpBox("Tlačítka fungují až po stisku Play.", MessageType.Info);

                if (GUILayout.Button("▶ Start jako HOST (Editor)"))
                    t.EditorStartHost();

                if (GUILayout.Button("🔌 Připojit se na localhost (Editor)"))
                    t.EditorStartClient();

                if (GUILayout.Button("■ Odpojit (Editor)"))
                    t.EditorShutdown();
            }
        }
    }
#endif
}
