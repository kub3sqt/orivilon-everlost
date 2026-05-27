using System.Collections.Generic;
using UnityEngine;

namespace Orivilon.Building
{
    /// <summary>
    /// Reprezentuje jeden fyzicky umístěný stavební díl ve světě hry.
    /// Každý díl si udržuje seznam sousedů a dovede pomocí BFS ověřit,
    /// zda má cestu k nejbližšímu základovému dílu (Foundation).
    /// </summary>
    public class BuildingPiece : MonoBehaviour
    {
        /// <summary>
        /// Datová definice tohoto stavebního dílu (ScriptableObject).
        /// Obsahuje typ, prefaby, naklady a veškerou konfiguraci.
        /// </summary>
        public BuildingPieceData data;

        /// <summary>
        /// Seznam sousedních dílů, ke kterým je tento díl přímo připojen.
        /// Propojení je vždy obousměrné – pokud A zná B jako souseda, pak i B zná A.
        /// </summary>
        public List<BuildingPiece> neighbours = new();

        /// <summary>
        /// Ověří, zda je tento díl podepřen – tj. zda existuje cesta k Foundation dílu.
        /// Algoritmus BFS (prohledávání do šířky) prochází sousedy díl po dílu.
        /// Jakmile narazí na Foundation nebo překročí povolenou vzdálenost, skončí.
        /// </summary>
        /// <param name="maxDistance">Maximální povolený počet kroků (dílů) k Foundation.</param>
        /// <returns>True, pokud je Foundation dosažitelná do zadaného počtu kroků.</returns>
        public bool IsSupported(int maxDistance)
        {
            Queue<(BuildingPiece piece, int distance)> queue = new();

            queue.Enqueue((this, 0));

            while (queue.Count > 0)
            {
                var (piece, dist) = queue.Dequeue();

                if (piece.data.type == BuildingPieceType.Foundation)
                    return true;

                if (dist >= maxDistance)
                    continue;

                foreach (var n in piece.neighbours)
                {
                    queue.Enqueue((n, dist + 1));
                }
            }

            return false;
        }

        /// <summary>
        /// Obousměrně propojí tento díl s jiným dílem jako sousedy.
        /// Každý z obou dílů přidá toho druhého do svého seznamu sousedů,
        /// pokud tam ještě není.
        /// </summary>
        /// <param name="other">Díl, ke kterému se má toto propojení vytvořit.</param>
        public void AttachTo(BuildingPiece other)
        {
            if (!neighbours.Contains(other))
                neighbours.Add(other);

            if (!other.neighbours.Contains(this))
                other.neighbours.Add(this);
        }
    }
}