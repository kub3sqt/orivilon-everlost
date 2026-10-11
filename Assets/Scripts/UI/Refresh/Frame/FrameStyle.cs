using UnityEngine;

namespace Orivilon.UI.Refresh
{
    /// <summary>
    /// Look of the continuous UI frame (corner radius, rim, glow, colours) plus the sprites and
    /// materials generated from it. Change the values in the Inspector and press
    /// "Regenerate sprites" – every <see cref="FramePiece"/> and <see cref="FrameJoin"/> that
    /// references this asset picks the new art up.
    ///
    /// <para>How the silhouette works: each panel is two sliced images – an Edge (rim + glow,
    /// drawn first) and a Fill (drawn on top). Fills write a stencil bit; edges are only drawn
    /// where no fill and no earlier edge has drawn, so overlapping panels merge into one shape
    /// without doubled rims. Concave (inward) corners are separate <see cref="FrameJoin"/> pieces
    /// whose fill sprite really is transparent inside the rounded corner.</para>
    /// </summary>
    [CreateAssetMenu(menuName = "Everlost/UI/Frame Style", fileName = "FrameStyle")]
    public class FrameStyle : ScriptableObject
    {
        [Header("Shape (reference px)")]
        [Min(2f)] public float radius = 18f;
        [Min(0f)] public float rimWidth = 2.2f;
        [Tooltip("Width of the soft glow stroke centred on the contour (half of it is visible outside).")]
        [Min(0f)] public float glowWidth = 8f;

        [Header("Colours")]
        public Color fillColor = Color.black;
        public Color rimColor = new Color(175f / 255f, 180f / 255f, 172f / 255f, 0.55f);
        public Color glowColor = new Color(1f, 1f, 1f, 0.05f);

        [Header("Generated art (do not edit by hand)")]
        public Sprite panelFill;
        public Sprite panelEdge;
        public Sprite joinFill;
        public Sprite joinEdge;
        public Material fillMaterial;
        public Material edgeMaterial;
        public Material resetMaterial;

        /// <summary>How far the Edge image reaches outside its panel.</summary>
        public float EdgeExtent => Mathf.Ceil(rimWidth * 0.5f + glowWidth * 0.5f + 1.5f);

        /// <summary>How far a join reaches into the two panels it connects (hides their straight rims there).</summary>
        public const float JoinMargin = 3f;
    }
}
