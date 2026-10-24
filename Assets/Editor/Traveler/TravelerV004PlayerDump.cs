using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace Orivilon.EditorTools
{
    /// <summary>Play Mode diagnostika: hierarchie lokálního hráče, kamery, AudioListenery. Jen editor.</summary>
    internal static class TravelerV004PlayerDump
    {
        [MenuItem("Tools/Everlost/Character v004/Dump player (Play)")]
        public static void DumpPlayer()
        {
            var sb = new StringBuilder();
            var fpc = Object.FindFirstObjectByType<Orivilon.Player.FirstPersonController>();
            sb.AppendLine("FPC " + (fpc ? fpc.name : "null"));
            if (fpc)
                foreach (var t in fpc.GetComponentsInChildren<Transform>(true))
                {
                    string path = t.name; var p = t; while (p.parent && p != fpc.transform) { p = p.parent; path = p.name + "/" + path; }
                    sb.AppendLine(path + " active=" + t.gameObject.activeSelf + " layer=" + LayerMask.LayerToName(t.gameObject.layer) + " lp=" + t.localPosition.ToString("F3") + " lr=" + t.localEulerAngles.ToString("F1") + " ls=" + t.localScale.ToString("F3") + " wp=" + t.position.ToString("F2") +
                        " comps=" + string.Join(",", t.GetComponents<Component>().Where(c => c && !(c is Transform)).Select(c => c.GetType().Name + (c is Behaviour b ? (b.enabled ? "" : "(off)") : c is Renderer r ? (r.enabled ? "" : "(off)") : c is Collider cc ? (cc.enabled ? "" : "(off)") : ""))));
                }
            foreach (var c in Object.FindObjectsByType<Camera>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                sb.AppendLine("CAM " + c.name + " en=" + c.enabled + " act=" + c.gameObject.activeInHierarchy + " depth=" + c.depth + " near=" + c.nearClipPlane + " far=" + c.farClipPlane + " mask=" + c.cullingMask + " main=" + (Camera.main == c) + " fov=" + c.fieldOfView);
            foreach (var a in Object.FindObjectsByType<AudioListener>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                sb.AppendLine("LISTENER " + a.name + " en=" + a.enabled + " act=" + a.gameObject.activeInHierarchy);
            if (fpc)
            {
                foreach (var col in fpc.GetComponentsInChildren<Collider>(true)) sb.AppendLine("COL " + col.name + " " + col.GetType().Name + " b=" + col.bounds + " trig=" + col.isTrigger);
                sb.AppendLine("lossy=" + fpc.transform.lossyScale + " cam=" + (fpc.playerCamera ? fpc.playerCamera.transform.position.ToString("F2") : "-"));
            }
            for (int i = 0; i < 32; i++) { var n = LayerMask.LayerToName(i); if (n.Length > 0) sb.Append(i + ":" + n + " "); }
            Directory.CreateDirectory(TravelerV004Dump.OutDir);
            File.WriteAllText(Path.Combine(TravelerV004Dump.OutDir, "player.txt"), sb.ToString());
            Debug.Log("[TravelerV004Dump] player hotovo");
        }
    }
}
