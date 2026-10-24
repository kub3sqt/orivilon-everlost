using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Orivilon.EditorTools
{
    /// <summary>
    /// Import nastavení pro schválenou postavu Cestovatel v004 (Character/export → Assets/Resources/Characters/TravelerV004).
    /// Generic rig (pomocné kosti twist/helper nejdou do Humanoid avataru), klipy přejmenované na krátká jména
    /// (Idle/Walk/Run/Chop/Mine/Swim), smyčky pro lokomoci, bez root motion (klipy jsou na místě).
    /// Nástroje jen jako statické meshe.
    /// </summary>
    public sealed class TravelerV004Import : AssetPostprocessor
    {
        public const string Folder = "Assets/Resources/Characters/TravelerV004/";
        private static readonly string[] Loops = { "Idle", "Walk", "Run", "Swim" };

        private bool Ours => assetPath.StartsWith(Folder) && assetPath.EndsWith(".fbx");
        private bool IsCharacter => assetPath.EndsWith("Everlost_Traveler_Default.fbx");

        private void OnPreprocessModel()
        {
            if (!Ours) return;
            var mi = (ModelImporter)assetImporter;
            mi.globalScale = 1f;
            mi.useFileScale = true;
            mi.importBlendShapes = false;
            mi.importCameras = false;
            mi.importLights = false;
            mi.isReadable = true;            // pohled z první osoby si staví mesh jen s pažemi
            mi.meshCompression = ModelImporterMeshCompression.Off;
            mi.importNormals = ModelImporterNormals.Import;
            mi.materialImportMode = ModelImporterMaterialImportMode.ImportViaMaterialDescription;
            mi.materialLocation = ModelImporterMaterialLocation.InPrefab;
            mi.addCollider = false;
            if (IsCharacter)
            {
                mi.animationType = ModelImporterAnimationType.Generic;
                mi.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
                mi.optimizeGameObjects = false;   // kost prop.R musí zůstat v hierarchii pro nástroj
                mi.importAnimation = true;
                mi.animationCompression = ModelImporterAnimationCompression.KeyframeReduction;
                mi.resampleCurves = true;
            }
            else
            {
                mi.animationType = ModelImporterAnimationType.None;
                mi.importAnimation = false;
            }
        }

        private void OnPreprocessAnimation()
        {
            if (!Ours || !IsCharacter) return;
            var mi = (ModelImporter)assetImporter;
            var src = mi.defaultClipAnimations;
            if (src == null || src.Length == 0) return;
            var clips = src.Select(c =>
            {
                string shortName = c.takeName;
                int bar = shortName.LastIndexOf('|');
                if (bar >= 0) shortName = shortName.Substring(bar + 1);
                c.name = shortName;
                bool loop = Loops.Contains(shortName);
                c.loopTime = loop;
                c.loopPose = false;           // smyčky jsou zapečené tak, aby poslední snímek = první
                c.lockRootRotation = true; c.lockRootHeightY = true; c.lockRootPositionXZ = true;
                c.keepOriginalOrientation = true; c.keepOriginalPositionY = true; c.keepOriginalPositionXZ = true;
                return c;
            }).ToArray();
            mi.clipAnimations = clips;
        }
    }
}
