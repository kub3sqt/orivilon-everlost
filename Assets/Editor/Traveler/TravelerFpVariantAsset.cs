using Orivilon.Player;
using UnityEditor;
using UnityEngine;

namespace Orivilon.EditorTools
{
    /// <summary>
    /// Materiály paží z 1. osoby vznikají za běhu (kopie s Transparent One/Zero bez SSAO a bez příjmu stínu). Aby
    /// build obsahoval tuto variantu shaderu Lit, leží v Resources jeden materiál se stejnými klíčovými slovy.
    /// </summary>
    internal static class TravelerFpVariantAsset
    {
        public const string Path = "Assets/Resources/Characters/TravelerV004/M_FP_ViewModel_Variants.mat";

        [MenuItem("Tools/Everlost/Character v004/Create FP shader variant material")]
        public static void Create()
        {
            var shader = Shader.Find("Universal Render Pipeline/Lit");
            var m = AssetDatabase.LoadAssetAtPath<Material>(Path);
            bool created = m == null;
            if (created) m = new Material(shader) { name = "M_FP_ViewModel_Variants" };
            m.SetFloat("_ReceiveShadows", 0f);
            m.EnableKeyword("_RECEIVE_SHADOWS_OFF");
            TravelerHeldItem.MakeIgnoreScreenSpaceAO(m);
            if (created) AssetDatabase.CreateAsset(m, Path); else EditorUtility.SetDirty(m);
            AssetDatabase.SaveAssets();
            Debug.Log("[FP] variantní materiál " + Path + " kw [" + string.Join(",", m.shaderKeywords) + "]");
        }
    }
}
