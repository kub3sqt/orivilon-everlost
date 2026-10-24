using System.Collections.Generic;
using System.IO;
using System.Text;
using Orivilon.Player;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace Orivilon.EditorTools
{
    /// <summary>
    /// QA jasu světa: stejné místo a směr, rychlé přepínání pohledu (1. osoba / 3. zezadu) a izolační varianty
    /// overlay kamery paží. Výstup _reports/traveler-v004/kolo4/bright_&lt;tag&gt;_&lt;krok&gt;.png + bright_&lt;tag&gt;.txt
    /// (průměrný jas oblohy a terénu se počítá z PNG mimo Unity).
    /// </summary>
    internal static class TravelerBrightQA
    {
        [MenuItem("Tools/Everlost/Character v004/QA/Brightness FP vs TP (isolation)")]
        public static void RunIso() => Start("iso", true);

        [MenuItem("Tools/Everlost/Character v004/QA/Brightness FP vs TP")]
        public static void Run() => Start("now", false);

        private static void Start(string tag, bool iso)
        {
            var fpc = Object.FindFirstObjectByType<FirstPersonController>();
            if (fpc == null) return;
            var r = fpc.gameObject.AddComponent<BrightQARunner>(); r.tag = tag; r.iso = iso;
        }
    }

    internal sealed class BrightQARunner : MonoBehaviour
    {
        public string tag; public bool iso;
        private readonly List<string> steps = new List<string>();
        private int i = -1; private float t; private readonly StringBuilder sb = new StringBuilder();
        private Camera viewCam; private UniversalAdditionalCameraData vd, bd; private bool vPost0, bPost0;
        private static string Dir => Path.Combine(TravelerV004Dump.OutDir, "kolo4");

        private void Start()
        {
            Directory.CreateDirectory(Dir);
            var fpc = GetComponent<FirstPersonController>();
            viewCam = fpc.playerCamera.transform.Find("TravelerViewCamera")?.GetComponent<Camera>();
            bd = fpc.playerCamera.GetUniversalAdditionalCameraData();
            if (viewCam != null) vd = viewCam.GetUniversalAdditionalCameraData();
            vPost0 = vd != null && vd.renderPostProcessing; bPost0 = bd.renderPostProcessing;
            sb.AppendLine("výchozí: základní kamera post " + bPost0 + ", overlay paží " + (viewCam != null) + " post " + vPost0 + ", HDR " + fpc.playerCamera.allowHDR + ", MSAA " + fpc.playerCamera.allowMSAA);
            if (iso) { steps.Add("fp_pred"); steps.Add("tp_pred"); }
            for (int k = 0; k < 3; k++) { steps.Add("fp"); steps.Add("tp"); }
            sb.AppendLine("čas " + Time.time.ToString("F1"));
        }

        private void Restore()
        {
            TravelerAvatar.QaDoublePost = false;
        }

        private void Update()
        {
            t += Time.unscaledDeltaTime;
            if (i >= 0 && t < 1.2f) return;
            // Snímek se zapíše až na konci snímku – další stav se nastaví až o snímek později.
            if (i >= 0 && !captured)
            {
                captured = true;
                string name = "bright_" + tag + "_" + i.ToString("00") + "_" + steps[i];
                ScreenCapture.CaptureScreenshot(Path.Combine(Dir, name + ".png"));
                sb.AppendLine(name + " mode " + TravelerCameraView.Mode + " overlay " + (viewCam != null && viewCam.enabled) + " overlayPost " + (vd != null && vd.renderPostProcessing) + " basePost " + bd.renderPostProcessing);
            }
            if (i >= 0 && t < 1.5f) return;
            captured = false;
            i++; t = 0f;
            if (i >= steps.Count) { Restore(); File.WriteAllText(Path.Combine(Dir, "bright_" + tag + ".txt"), sb.ToString()); Destroy(this); return; }
            Restore();
            var view = Object.FindFirstObjectByType<TravelerCameraView>();
            string s = steps[i];
            TravelerAvatar.QaDoublePost = s.EndsWith("_pred");
            view.SetMode(s.StartsWith("tp") ? TravelerViewMode.ThirdBack : TravelerViewMode.FirstPerson);
        }

        private bool forceOverlayOff, captured;
        private void OnDestroy() { Restore(); }
    }
}
