using UnityEditor;

namespace Orivilon.EditorTools
{
    /// <summary>Import only the generated traveler's portable colour and tangent-space normal atlas.</summary>
    public sealed class TravelerTextureImport : AssetPostprocessor
    {
        private void OnPreprocessTexture()
        {
            const string prefix = "Assets/Resources/Characters/Traveler";
            if (assetPath != prefix + "BaseColor.png" && assetPath != prefix + "Normal.png") return;
            var importer = (TextureImporter)assetImporter;
            bool normal = assetPath == prefix + "Normal.png";
            importer.textureType = normal ? TextureImporterType.NormalMap : TextureImporterType.Default;
            importer.sRGBTexture = !normal;
            importer.maxTextureSize = 2048;
            importer.mipmapEnabled = true;
        }
    }
}
