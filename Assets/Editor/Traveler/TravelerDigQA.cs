using System.IO;
using System.Text;
using Orivilon.Player;
using Orivilon.World.Generation;
using UnityEditor;
using UnityEngine;

namespace Orivilon.EditorTools
{
    /// <summary>QA: krumpáč v ruce, stejný paprsek jako VoxelDigTool (kamera, reach, radius, strength), 8 úderů; hloubka díry a snímky před/po.</summary>
    internal static class TravelerDigQA
    {
        [MenuItem("Tools/Everlost/Character v004/QA/Dig test (pickaxe)")]
        public static void Run()
        {
            var fpc = Object.FindFirstObjectByType<FirstPersonController>();
            if (fpc != null) fpc.gameObject.AddComponent<DigQARunner>();
        }
    }

    internal sealed class DigQARunner : MonoBehaviour
    {
        private float t; private int hits; private readonly StringBuilder sb = new StringBuilder();
        private VoxelDigTool tool; private Vector3 firstPoint, firstNormal; private bool haveFirst;
        private static string Dir => Path.Combine(Directory.GetCurrentDirectory(), "_reports/traveler-v004");

        private void Start()
        {
            TravelerPlayQA.Pick();
            tool = Object.FindFirstObjectByType<VoxelDigTool>();
            Directory.CreateDirectory(Dir);
            ScreenCapture.CaptureScreenshot(Path.Combine(Dir, "dig_qa_before.png"));
        }

        private void Update()
        {
            t += Time.deltaTime;
            if (t < 1f) return;
            if (hits < 8 && t > 1f + hits * 0.35f)
            {
                hits++;
                var cam = Camera.main ? Camera.main.transform : null;
                if (hits == 1 && cam != null && Physics.Raycast(cam.position, Vector3.down, out var gd, 50f, ~0, QueryTriggerInteraction.Ignore))
                    sb.AppendLine($"oko nad zemí {gd.distance:F2} m, sklon {cam.eulerAngles.x:F0}°, potřebná vzdálenost paprsku ≈ {(gd.distance / Mathf.Max(0.05f, Mathf.Sin(cam.eulerAngles.x * Mathf.Deg2Rad))):F1} m, reach {(tool ? tool.reach : 0f)}");
                sb.AppendLine($"úder {hits}: CanUse {(tool ? tool.CanUse : false)} krumpáč {PlayerEquipment.HoldingPickaxe} kamera {(cam ? cam.name : "-")}");
                if (tool == null || cam == null || !tool.CanUse) return;
                if (!Physics.Raycast(cam.position, cam.forward, out var hit, tool.reach, tool.terrainMask, QueryTriggerInteraction.Ignore))
                { sb.AppendLine("  paprsek nic nezasáhl v dosahu " + tool.reach); return; }
                if (!haveFirst) { firstPoint = hit.point; firstNormal = hit.normal; haveFirst = true; }
                Vector3 c = hit.point + hit.normal * (-tool.radius * 0.35f);
                bool changed = VoxelTerrain.instance.Dig(c, tool.radius, tool.strength);
                sb.AppendLine($"  zásah {hit.collider.name} vzdál. {hit.distance:F2} změna {changed}");
            }
            if (hits >= 8 && t > 1f + 8 * 0.35f + 1.5f)
            {
                var cam = Camera.main.transform;
                if (haveFirst && Physics.Raycast(firstPoint + firstNormal * 3f, -firstNormal, out var h2, 20f, tool.terrainMask, QueryTriggerInteraction.Ignore))
                    sb.AppendLine($"hloubka díry podél normály: {(h2.distance - 3f):F2} m (povrch před = 0)");
                else sb.AppendLine("hloubka díry: paprsek prošel (díra hlubší než 17 m nebo bez kolize)");
                ScreenCapture.CaptureScreenshot(Path.Combine(Dir, "dig_qa_after.png"));
                File.WriteAllText(Path.Combine(Dir, "dig_qa.txt"), sb.ToString());
                Debug.Log("[DigQA] hotovo\n" + sb);
                Destroy(this);
            }
        }
    }
}
