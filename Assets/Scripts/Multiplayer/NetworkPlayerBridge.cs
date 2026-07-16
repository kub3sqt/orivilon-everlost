using UnityEngine;

namespace Orivilon.Multiplayer
{
    /// <summary>
    /// Komponent přidávaný na hráčský prefab v herní scéně.
    /// Každých ~50 ms odesílá pozici a rotaci lokálního hráče přes síť.
    ///
    /// SETUP:
    ///   Přidejte tento skript na hráčský GameObject ve scéně Game.
    ///   Není potřeba žádná konfigurace – skript najde síťové systémy automaticky.
    ///   V single-playeru je kompletně neaktivní (nulová zátěž).
    /// </summary>
    public class NetworkPlayerBridge : MonoBehaviour
    {
        [Tooltip("Jak často (v sekundách) se posílá pozice hráče. Výchozí = 0.05 s = 20 Hz.")]
        public float sendInterval = 0.05f;

        private float timer;

        private void Update()
        {
            // V single-playeru nebo pokud multiplayer není aktivní – nedělej nic
            if (!MultiplayerManager.IsActive) return;

            timer += Time.deltaTime;
            if (timer < sendInterval) return;
            timer = 0f;

            NetworkWorldSync.Instance?.SendPlayerPositionToServer(
                transform.position,
                transform.rotation);
        }
    }
}
