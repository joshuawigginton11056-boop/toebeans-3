using UnityEditor;
using UnityEngine;

namespace Toebeans.ScaleTest.EditorTools
{
    /// <summary>
    /// Applies sane defaults to character models dropped into Assets/Characters/, so a freshly
    /// downloaded pack is rigged and looping without anyone having to click through the importer.
    /// Only runs on first import; hand-tuned settings are never overwritten.
    /// </summary>
    public class CharacterModelPostprocessor : AssetPostprocessor
    {
        const string CharacterFolder = "assets/characters/";

        bool IsCharacterAsset => assetPath.ToLowerInvariant().StartsWith(CharacterFolder);

        void OnPreprocessModel()
        {
            if (!IsCharacterAsset)
                return;

            var importer = (ModelImporter)assetImporter;
            if (!importer.importSettingsMissing)
                return; // already imported once; respect whatever the user set

            importer.animationType = ModelImporterAnimationType.Human;
            importer.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
            importer.importAnimation = true;
            importer.importCameras = false;
            importer.importLights = false;
            importer.importBlendShapes = false;
            importer.importVisibility = false;
            importer.animationCompression = ModelImporterAnimationCompression.KeyframeReduction;
        }

        void OnPreprocessAnimation()
        {
            if (!IsCharacterAsset)
                return;

            var importer = (ModelImporter)assetImporter;
            if (!importer.importSettingsMissing)
                return;

            ModelImporterClipAnimation[] clips = importer.clipAnimations;
            if (clips == null || clips.Length == 0)
                clips = importer.defaultClipAnimations;
            if (clips == null || clips.Length == 0)
                return;

            bool changed = false;
            foreach (ModelImporterClipAnimation clip in clips)
            {
                if (!ShouldLoop(clip.name) || clip.loopTime)
                    continue;
                clip.loopTime = true;
                changed = true;
            }

            if (changed)
                importer.clipAnimations = clips;
        }

        static bool ShouldLoop(string clipName)
        {
            string name = clipName.ToLowerInvariant();
            return name.Contains("idle") || name.Contains("walk") || name.Contains("run")
                   || name.Contains("jog") || name.Contains("sprint") || name.Contains("fall")
                   || name.Contains("crouch") || name.Contains("swim") || name.Contains("climb");
        }
    }
}
