using System;
using UnityEngine;

namespace Orivilon.UI.Settings
{
    /// <summary>
    /// Klávesy z nastavení (SettingsData) pro konzumenty, kteří nemají vlastní pole KeyCode (interakce, inventář,
    /// rychlé uložení). FirstPersonController dostává své klávesy přímo do svých polí (SettingsManager.ApplyLive).
    /// Není to nový vstupní systém – jen převod uložených názvů kláves na KeyCode s pevnými výchozími hodnotami.
    /// </summary>
    public static class KeyBindings
    {
        public static KeyCode Interact = KeyCode.E;
        public static KeyCode Inventory = KeyCode.Tab;
        public static KeyCode QuickSave = KeyCode.F6;

        /// <summary>
        /// Klávesy pevně obsazené jinými funkcemi hry (F5 kamera, Esc pauza, C Arcanum, Q zahození, T konzole,
        /// / příkaz, R stavění, F3 ladění, P a L den/noc, 1–8 hotbar, levé tlačítko akce). Na ně se přemapovat nedá.
        /// </summary>
        public static bool IsReserved(KeyCode k) => ReservedFor(k) != null;

        /// <summary>Co klávesu pevně používá (text pro hlášku v Nastavení), null = volná.</summary>
        public static string ReservedFor(KeyCode k)
        {
            switch (k)
            {
                case KeyCode.None: return "nothing";
                case KeyCode.F5: return "camera view";
                case KeyCode.Escape: return "the pause menu";
                case KeyCode.C: return "the Arcanum";
                case KeyCode.Q: return "dropping items";
                case KeyCode.T: return "chat";
                case KeyCode.Slash: return "commands";
                case KeyCode.R: return "rotating buildings";
                case KeyCode.F3: return "the debug overlay";
                case KeyCode.P: case KeyCode.L: return "time of day";
                case KeyCode.Mouse0: return "use / attack";
            }
            if (k >= KeyCode.Alpha1 && k <= KeyCode.Alpha8) return "the hotbar";
            if (k >= KeyCode.Keypad1 && k <= KeyCode.Keypad8) return "the hotbar";
            return null;
        }

        /// <summary>Přemapovatelná akce z SettingsData (klíč řádku v Nastavení, název, čtení a zápis).</summary>
        public struct BindAction
        {
            public string key, name;
            public Func<SettingsData, string> get;
            public System.Action<SettingsData, string> set;
        }

        /// <summary>
        /// Akce, které hra opravdu čte (FirstPersonController + KeyBindings), v pořadí řádků Nastavení.
        /// Dřep smí sdílet klávesu se sprintem (výchozí stav: obojí LeftControl – dřep je ve scéně vypnutý
        /// a ve vodě potápí); jinak má každá akce vlastní klávesu.
        /// </summary>
        public static readonly BindAction[] Actions =
        {
            new BindAction { key = "key.sprint", name = "Sprint", get = d => d.sprintKey, set = (d, v) => d.sprintKey = v },
            new BindAction { key = "key.jump", name = "Jump", get = d => d.jumpKey, set = (d, v) => d.jumpKey = v },
            new BindAction { key = "key.crouch", name = "Crouch / dive", get = d => d.crouchKey, set = (d, v) => d.crouchKey = v },
            new BindAction { key = "key.interact", name = "Interact / pick up", get = d => d.interactKey, set = (d, v) => d.interactKey = v },
            new BindAction { key = "key.inventory", name = "Inventory", get = d => d.inventoryKey, set = (d, v) => d.inventoryKey = v },
            new BindAction { key = "key.zoom", name = "Zoom", get = d => d.zoomKey, set = (d, v) => d.zoomKey = v },
            new BindAction { key = "key.flight", name = "Fly (creative)", get = d => d.flightKey, set = (d, v) => d.flightKey = v },
            new BindAction { key = "key.quickSave", name = "Quick save", get = d => d.quickSaveKey, set = (d, v) => d.quickSaveKey = v },
        };

        public static int ActionIndex(string key)
        {
            for (int i = 0; i < Actions.Length; i++) if (Actions[i].key == key) return i;
            return -1;
        }

        /// <summary>Smí dvě akce sdílet klávesu? Jen sprint a dřep (záměrné výchozí sdílení LeftControl).</summary>
        public static bool MayShare(string a, string b) =>
            (a == "key.sprint" && b == "key.crouch") || (a == "key.crouch" && b == "key.sprint");

        /// <summary>Čitelný název klávesy pro UI („Left Ctrl“, „Right Mouse“, „5“).</summary>
        public static string Label(KeyCode k)
        {
            switch (k)
            {
                case KeyCode.LeftControl: return "Left Ctrl";
                case KeyCode.RightControl: return "Right Ctrl";
                case KeyCode.LeftShift: return "Left Shift";
                case KeyCode.RightShift: return "Right Shift";
                case KeyCode.LeftAlt: return "Left Alt";
                case KeyCode.RightAlt: return "Right Alt";
                case KeyCode.LeftCommand: return "Left Cmd";
                case KeyCode.RightCommand: return "Right Cmd";
                case KeyCode.Mouse0: return "Left Mouse";
                case KeyCode.Mouse1: return "Right Mouse";
                case KeyCode.Mouse2: return "Middle Mouse";
                case KeyCode.Mouse3: return "Mouse 4";
                case KeyCode.Mouse4: return "Mouse 5";
                case KeyCode.Mouse5: return "Mouse 6";
                case KeyCode.Mouse6: return "Mouse 7";
                case KeyCode.Return: return "Enter";
                case KeyCode.KeypadEnter: return "Num Enter";
                case KeyCode.BackQuote: return "`";
                case KeyCode.Minus: return "-";
                case KeyCode.Equals: return "=";
                case KeyCode.LeftBracket: return "[";
                case KeyCode.RightBracket: return "]";
                case KeyCode.Semicolon: return ";";
                case KeyCode.Quote: return "'";
                case KeyCode.Backslash: return "\\";
                case KeyCode.Comma: return ",";
                case KeyCode.Period: return ".";
                case KeyCode.UpArrow: return "Up";
                case KeyCode.DownArrow: return "Down";
                case KeyCode.LeftArrow: return "Left";
                case KeyCode.RightArrow: return "Right";
            }
            if (k >= KeyCode.Alpha0 && k <= KeyCode.Alpha9) return ((int)(k - KeyCode.Alpha0)).ToString();
            if (k >= KeyCode.Keypad0 && k <= KeyCode.Keypad9) return "Num " + (int)(k - KeyCode.Keypad0);
            // CamelCase → slova (PageDown → Page Down)
            string n = k.ToString();
            var sb = new System.Text.StringBuilder(n.Length + 4);
            for (int i = 0; i < n.Length; i++)
            {
                if (i > 0 && char.IsUpper(n[i]) && !char.IsUpper(n[i - 1])) sb.Append(' ');
                sb.Append(n[i]);
            }
            return sb.ToString();
        }

        public static string Label(string name) => TryParse(name, out var k) ? Label(k) : "-";

        public static bool TryParse(string name, out KeyCode key)
        {
            key = KeyCode.None;
            return !string.IsNullOrEmpty(name) && Enum.TryParse(name.Trim(), true, out key) && Enum.IsDefined(typeof(KeyCode), key) && key != KeyCode.None;
        }

        public static KeyCode Parse(string name, KeyCode fallback) => TryParse(name, out var k) ? k : fallback;

        public static void Apply(SettingsData d)
        {
            Interact = Parse(d.interactKey, KeyCode.E);
            Inventory = Parse(d.inventoryKey, KeyCode.Tab);
            QuickSave = Parse(d.quickSaveKey, KeyCode.F6);
        }
    }
}
