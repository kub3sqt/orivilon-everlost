using Orivilon.SaveSystem;
using TMPro;
using UnityEngine;

namespace Orivilon.UI.Refresh
{
    /// <summary>
    /// On the world-list row prefab: fills the small "last played" label on the right.
    /// MainMenuUI sets the row's name right after instantiating it, so Start can look it up.
    /// </summary>
    public class WorldRowMeta : MonoBehaviour
    {
        public TMP_Text nameText;
        public TMP_Text metaText;

        private void Start()
        {
            if (metaText == null) return;
            var wm = WorldSaveManager.instance;
            string n = nameText != null ? nameText.text : null;
            var w = wm != null && n != null ? wm.worlds.Find(x => x.worldName == n) : null;
            metaText.text = w != null ? UiKit.When(w.lastPlayed) : "";
        }
    }
}
