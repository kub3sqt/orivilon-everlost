using UnityEngine;
using UnityEngine.UI;

namespace Orivilon.UI.Refresh
{
    /// <summary>
    /// Pause screen: applies the frame layout (the Settings tab is fitted to its text) and keeps
    /// the collapsed settings background from peeking out on 16:10 screens. Visuals are authored
    /// in the Game scene under PauseMenu.
    /// </summary>
    public class PauseController : MonoBehaviour
    {
        public UiFrame frame;
        [Tooltip("Original settings background: in the scene it is collapsed (negative height) until settings open.")]
        public Image settingsBackground;
        public float settingsMinHeight = 150f;

        private void OnEnable()
        {
            if (frame != null) frame.SetState("Default", 0f);
        }

        private void Update()
        {
            if (settingsBackground != null)
                settingsBackground.enabled = settingsBackground.rectTransform.rect.height > settingsMinHeight;
        }
    }
}
