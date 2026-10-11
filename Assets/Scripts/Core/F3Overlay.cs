using TMPro;
using UnityEngine;

namespace Orivilon.Core
{
    /// <summary>
    /// References of the authored F3 diagnostics overlay prefab
    /// (Assets/Resources/UI/F3Diagnostics.prefab, also placed in the Game scene).
    /// <see cref="DebugGUI"/> fills the body text and animates the panel; edit fonts, colours,
    /// padding and panel art directly in the prefab.
    /// </summary>
    public class F3Overlay : MonoBehaviour
    {
        public const string PrefabPath = "UI/F3Diagnostics";

        public Canvas canvas;
        public RectTransform panel;
        public CanvasGroup group;
        public TMP_Text header;
        public TMP_Text body;
    }
}
