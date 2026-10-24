using System.IO;
using System.Text;
using Orivilon.Player;
using Orivilon.UI.Settings;
using UnityEditor;
using UnityEngine;

namespace Orivilon.EditorTools
{
    /// <summary>
    /// QA přemapování kláves: záloha uživatelského settings.json → uložení testovacích kláves přes SettingsManager
    /// (stejná cesta jako menu) → načtení ze souboru → kontrola konzumentů. „restore“ vrátí zálohu beze změny.
    /// Výstup _reports/traveler-v004/kolo4/rebind_qa.txt (přidává se).
    /// </summary>
    internal static class TravelerRebindQA
    {
        private static string File0 => Path.Combine(Application.persistentDataPath, "settings.json");
        private static string Backup => File0 + ".qa-kolo4-zaloha";
        private static string Out => Path.Combine(TravelerV004Dump.OutDir, "kolo4", "rebind_qa.txt");

        private static void Log(string s) { Directory.CreateDirectory(Path.GetDirectoryName(Out)); File.AppendAllText(Out, System.DateTime.Now.ToString("HH:mm:ss ") + s + "\n"); }

        private static string State()
        {
            var m = SettingsManager.Instance; var fpc = Object.FindFirstObjectByType<FirstPersonController>();
            var c = m != null ? m.Current : null;
            return (c != null ? $"settings v{c.version} [{m.LoadInfo}] sprint {c.sprintKey} jump {c.jumpKey} crouch {c.crouchKey} interact {c.interactKey} inventory {c.inventoryKey} zoom {c.zoomKey} flight {c.flightKey} quickSave {c.quickSaveKey}" : "bez SettingsManageru")
                + (fpc != null ? $" | FPC sprint {fpc.sprintKey} jump {fpc.jumpKey} crouch {fpc.crouchKey} zoom {fpc.zoomKey} flight {fpc.flightKey}" : "")
                + $" | KeyBindings interact {KeyBindings.Interact} inventory {KeyBindings.Inventory} quickSave {KeyBindings.QuickSave}";
        }

        [MenuItem("Tools/Everlost/Character v004/QA/Keys rebind: state")]
        public static void Dump() => Log("STAV " + State());

        [MenuItem("Tools/Everlost/Character v004/QA/Keys rebind: set test keys (backup + save + reload)")]
        public static void Set()
        {
            var m = SettingsManager.Instance; if (m == null) { Log("ERR SettingsManager"); return; }
            if (File.Exists(File0) && !File.Exists(Backup)) File.Copy(File0, Backup);
            Log("záloha " + Backup + " (" + File.Exists(Backup) + "), před: " + State());
            // validace (bez aplikace): rezervovaná a duplicitní klávesa
            var v = SettingsManager.Clone(m.Current); v.zoomKey = "F5"; v.interactKey = "Space"; v.quickSaveKey = "Alpha2"; v.inventoryKey = "Nesmysl";
            SettingsManager.Validate(v);
            Log($"validace: zoom F5 → {v.zoomKey}, interact Space (=skok) → {v.interactKey}, quickSave Alpha2 (hotbar) → {v.quickSaveKey}, inventory 'Nesmysl' → {v.inventoryKey}");
            var d = SettingsManager.Clone(m.Current);
            d.jumpKey = "J"; d.interactKey = "G"; d.inventoryKey = "I"; d.quickSaveKey = "F7"; d.sprintKey = "LeftShift";
            m.Apply(d);         // ukládá (saveOnApply)
            m.Load(); m.Apply(m.Current);   // načtení ze souboru a znovu aplikace
            Log("po uložení a načtení: " + State());
        }

        [MenuItem("Tools/Everlost/Character v004/QA/Keys rebind: restore backup")]
        public static void Restore()
        {
            var m = SettingsManager.Instance;
            if (!File.Exists(Backup)) { Log("ERR záloha neexistuje"); return; }
            File.Copy(Backup, File0, true);
            File.Move(Backup, Backup + ".obnoveno-" + System.DateTime.Now.ToString("HHmmss"));
            if (m != null) { m.Load(); m.Apply(m.Current); }
            Log("obnoveno ze zálohy: " + State());
        }

        [MenuItem("Tools/Everlost/Character v004/QA/Keys rebind: simulate old v2 settings")]
        public static void SimulateV2()
        {
            // Migrace v2 → v3 nad kopií souboru (uživatelský soubor se nemění): uložená hodnota sprint LeftShift z v2.
            string tmp = Path.Combine(Application.temporaryCachePath, "settings_v2_test.json");
            var d = new SettingsData { version = 2, sprintKey = "LeftShift", quickSaveKey = "F5", mouseSensitivity = 3.3f, fieldOfView = 77f };
            File.WriteAllText(tmp, JsonUtility.ToJson(d));
            var raw = new SettingsData(); JsonUtility.FromJsonOverwrite(File.ReadAllText(tmp), raw);
            Log("v2 soubor: sprint " + raw.sprintKey + " quickSave " + raw.quickSaveKey + " citlivost " + raw.mouseSensitivity + " FOV " + raw.fieldOfView + " (migrace se ověřuje přes Load uživatelského souboru, viz stav)");
        }
    }
}
