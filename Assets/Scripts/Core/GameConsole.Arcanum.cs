using Orivilon.UI.Arcanum;
using UnityEngine;

namespace Orivilon.Core
{
    /// <summary>
    /// Příkaz /arcanum – stav a QA ovládání panelu výzkumu (otevření jde stejnou cestou jako klávesa C).
    /// Přidání testovacích surovin (/arcanum testkit) existuje jen v editoru.
    /// </summary>
    public partial class GameConsole
    {
        private void CmdArcanum(string[] a)
        {
            var gm = GameManager.instance;
            string sub = a.Length > 1 ? a[1].ToLowerInvariant() : "state";
            var ui = ArcanumUI.Instance;
            switch (sub)
            {
                case "open": if (gm != null) gm.OpenArcanum(); break;
                case "close": if (gm != null) gm.CloseArcanum(); break;
                case "focus": if (ui != null && a.Length > 2) ui.FocusCategory(int.Parse(a[2])); break;
                case "hover": if (ui != null && a.Length > 2) ui.DebugHover(int.Parse(a[2]), false); break;
                case "pin": if (ui != null && a.Length > 2) ui.DebugHover(int.Parse(a[2]), true); break;
                case "research": if (ui != null && a.Length > 2) Print("[Arcanum] research " + a[2] + ": " + ui.DebugResearch(int.Parse(a[2]))); break;
                case "pan": if (ui != null && a.Length > 3) ui.DebugPan(new Vector2(float.Parse(a[2], System.Globalization.CultureInfo.InvariantCulture), float.Parse(a[3], System.Globalization.CultureInfo.InvariantCulture))); break;
                case "zoom": if (ui != null && a.Length > 2) ui.DebugZoom(float.Parse(a[2], System.Globalization.CultureInfo.InvariantCulture), new Vector2(Screen.width * 0.5f, Screen.height * 0.5f)); break;
                case "home": if (ui != null) ui.ResetView(); break;
                case "esc": if (gm != null) gm.HandleEscapeKey(); break;
                case "toggle": if (gm != null) { if (gm.IsArcanumOpen) gm.CloseArcanum(); else gm.OpenArcanum(); } break;
#if UNITY_EDITOR
                case "testkit":
                    ArcanumResearch.EditorGiveTestMaterials(a.Length > 2 ? int.Parse(a[2]) : 10);
                    Print("[Arcanum] editor test materials added");
                    break;
#endif
            }
            Print("[Arcanum] " + (ui != null ? ui.DebugState() : "not created") +
                  " cursor=" + Cursor.lockState + "/" + Cursor.visible + " menu=" + (gm != null && gm.isMenuOpen) +
                  " screen=" + Screen.width + "x" + Screen.height);
        }
    }
}
