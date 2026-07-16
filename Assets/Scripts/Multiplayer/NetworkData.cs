using System.Collections.Generic;
using UnityEngine;

namespace Orivilon.Multiplayer
{
    /// <summary>
    /// Konstanty názvů zpráv zasílaných přes NGO CustomMessagingManager.
    /// Všechny zprávy používají prefix "Everlost." aby se předešlo kolizím.
    /// </summary>
    public static class NetworkMessages
    {
        // Klient → Server
        public const string PlayerPosition      = "Everlost.PlayerPos";
        public const string ObjectDestroyed     = "Everlost.ObjDestroyed";
        public const string BuildingPlaced      = "Everlost.BuildPlaced";
        public const string WorldStateRequest   = "Everlost.WorldStateReq";

        // Server → Klient(i)
        public const string PlayerPositionBcast = "Everlost.PlayerPosBcast";
        public const string ObjectDestroyedBcast= "Everlost.ObjDestroyedBcast";
        public const string BuildingPlacedBcast = "Everlost.BuildPlacedBcast";
        public const string WorldStateResponse  = "Everlost.WorldStateResp";
        public const string PlayerConnected     = "Everlost.PlayerConn";
        public const string PlayerDisconnected  = "Everlost.PlayerDisc";
        public const string PlayerName          = "Everlost.PlayerName";
    }

    /// <summary>
    /// Záznam o jednom umístěném stavebním dílu – uložen v paměti na serveru.
    /// </summary>
    public class BuildingEntry
    {
        /// <summary>BuildingPieceData.id umístěného dílu.</summary>
        public string pieceId;

        /// <summary>Světová pozice dílu.</summary>
        public Vector3 position;

        /// <summary>Světová rotace dílu.</summary>
        public Quaternion rotation;
    }

    /// <summary>
    /// Stav světa přijatý od hosta při joinu.
    /// Ukládá se v NetworkWorldSync dokud není herní scéna načtena.
    /// </summary>
    public class ReceivedWorldState
    {
        public string worldName;
        public string seed;
        public int    worldSeed;
        /// <summary>Herní čas hosta v momentě joinu (0–24).</summary>
        public float  timeOfDay;
        /// <summary>Spawn pozice hosta (pro první join klienta bez save dat).</summary>
        public Vector3 hostSpawnPosition;
        public List<long>          destroyedHashes  = new List<long>();
        public List<BuildingEntry> placedBuildings  = new List<BuildingEntry>();
    }
}
