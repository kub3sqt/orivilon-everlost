using System.Globalization;
using System.IO;
using System.Text;
using Orivilon.Player;
using UnityEditor;
using UnityEngine;

namespace Orivilon.EditorTools
{
    /// <summary>QA multiplayeru: 40 s záznam vzdáleného avatara (rychlost, stav, fáze kroku, váhy chůze/vzduchu, výška) na hostu.</summary>
    internal static class TravelerRemoteQA
    {
        [MenuItem("Tools/Everlost/Character v004/QA/Remote gait log 40s")]
        public static void Run()
        {
            var go = new GameObject("RemoteGaitQA");
            go.AddComponent<RemoteGaitRunner>();
        }
    }

    internal sealed class RemoteGaitRunner : MonoBehaviour
    {
        private float lastSlow = -10f, dipStart = -1f, dipSpeed0, prevSpeed; private int dropouts; private readonly StringBuilder episodes = new StringBuilder();
        private float t; private int frames, moving, back, airPose, dips, actionFrames; private float lastPhase = -1f, maxSpeed, minY = 1e9f, maxY = -1e9f;
        private PlayerMoveState lastState; private readonly StringBuilder log = new StringBuilder(); private string who = "-"; private float nextLog;
        private static readonly CultureInfo C = CultureInfo.InvariantCulture;

        private void Update()
        {
            t += Time.unscaledDeltaTime;
            TravelerAvatar r = null;
            foreach (var a in Object.FindObjectsByType<TravelerAvatar>(FindObjectsSortMode.None)) if (!a.IsLocal) { r = a; break; }
            if (r != null && r.Body != null)
            {
                who = r.name; frames++;
                if (r.Speed > 3f) moving++;
                maxSpeed = Mathf.Max(maxSpeed, r.Speed);
                minY = Mathf.Min(minY, r.transform.position.y); maxY = Mathf.Max(maxY, r.transform.position.y);
                if (r.Body.AirWeight > .3f) airPose++;
                bool dip = r.Speed > 3f && r.MoveState == PlayerMoveState.Grounded && r.Body.GaitWeight < .6f;
                if (dip) dips++;
                // epizody propadu chůze: začátek, délka, rychlost na začátku a na konci (rozjezd/zastavení vs. výpadek)
                if (dip && dipStart < 0f) { dipStart = t; dipSpeed0 = prevSpeed; }
                if (!dip && dipStart >= 0f)
                {
                    float len = t - dipStart; string kind = dipStart - lastSlow < .6f ? "rozjezd (do 0,6 s od stání)" : r.Speed < 3f ? "zastavení" : "VÝPADEK (v pohybu)";
                    episodes.AppendLine(string.Format(C, "  propad {0:0.00} s od {1:0.00} s, rychlost před {2:0.0} → po {3:0.0}: {4}", len, dipStart, dipSpeed0, r.Speed, kind));
                    if (kind.StartsWith("VÝPADEK")) dropouts++;
                    dipStart = -1f;
                }
                prevSpeed = r.Speed; if (r.Speed < 1f) lastSlow = t;
                if (r.Action != TravelerAction.None) actionFrames++;
                if (lastPhase >= 0f) { float d = r.Phase - lastPhase; if (d < -.5f) d += 1f; if (d < -.02f) back++; }
                lastPhase = r.Phase;
                if (r.MoveState != lastState) { log.AppendLine(string.Format(C, "  {0:0.00} s stav {1} → {2}", t, lastState, r.MoveState)); lastState = r.MoveState; }
                if (t >= nextLog)
                {
                    nextLog = t + 1f;
                    log.AppendLine(string.Format(C, "  {0:0.0} s rychlost {1:0.0} stav {2} chůze {3:0.00} vzduch {4:0.00} akce {5} předmět {6} y {7:0.0}", t, r.Speed, r.MoveState, r.Body.GaitWeight, r.Body.AirWeight, r.Action, r.HeldItem != null ? r.HeldItem.id : "-", r.transform.position.y));
                }
            }
            if (t < 40f) return;
            var sb = new StringBuilder();
            sb.AppendLine(string.Format(C, "vzdálený avatar {0}: snímků {1}, v pohybu (>3 m/s) {2}, max rychlost {3:0.0}, couvnutí fáze {4}, pádová póza {5} snímků, propady chůze na zemi {6}, akce {7} snímků, výška {8:0.0}–{9:0.0}",
                who, frames, moving, maxSpeed, back, airPose, dips, actionFrames, minY, maxY));
            sb.AppendLine("výpadky chůze v pohybu: " + dropouts + "; epizody propadu:");
            sb.Append(episodes);
            sb.Append(log);
            string dir = Path.Combine(Directory.GetCurrentDirectory(), "_reports/traveler-v004");
            File.WriteAllText(Path.Combine(dir, "remote_gait.txt"), sb.ToString());
            Debug.Log("[RemoteQA]\n" + sb);
            Destroy(gameObject);
        }
    }
}
