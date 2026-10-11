using UnityEngine;

namespace Orivilon.UI.Refresh
{
    /// <summary>
    /// Shared hover look for every UI button (asset: Assets/UI/Refresh/HoverStyle.asset).
    /// Text links turn their text this colour, icon buttons tint their icon, filled buttons fill
    /// their background with it (using the flat sprites below so the colour is exact).
    /// </summary>
    [CreateAssetMenu(menuName = "Everlost/UI/Hover Style", fileName = "HoverStyle")]
    public class HoverStyle : ScriptableObject
    {
        public Color hoverColor = new Color32(0x56, 0x56, 0x56, 0xff);
        [Tooltip("Labels on a hovered filled button switch to this colour when they would be too dark to read.")]
        public Color lightLabel = new Color32(0xe9, 0xec, 0xe6, 0xff);
        [Tooltip("Labels darker than this (luminance 0–1) are switched to the light colour on hover.")]
        [Range(0f, 1f)] public float darkLabelLuminance = 0.45f;
        [Tooltip("Flat white rounded sprites used as hover fills (by corner radius).")]
        public Sprite fillSmall;
        public Sprite fillLarge;

        [Header("Item slots (inventory, hotbar, crafting, output)")]
        [Tooltip("Slots show a thin light outline on hover instead of the grey fill. Ring sprite used by the slot's HoverOutline child.")]
        public Sprite slotOutline;
        [Tooltip("Colour of the slot hover outline (the child Image colour is set from this when authored).")]
        public Color slotOutlineColor = new Color32(0xe9, 0xec, 0xe6, 0xff);
    }
}
