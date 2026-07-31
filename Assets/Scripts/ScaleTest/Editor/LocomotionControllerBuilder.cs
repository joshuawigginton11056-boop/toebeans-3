using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace Toebeans.ScaleTest.EditorTools
{
    /// <summary>
    /// Generates a minimal locomotion AnimatorController (idle → walk → run blend, plus an airborne
    /// state) from whatever animation clips a character pack happens to ship with.
    /// </summary>
    public static class LocomotionControllerBuilder
    {
        public const string SpeedParameter = "Speed";
        public const string GroundedParameter = "Grounded";
        public const string JumpParameter = "Jump";

        /// <summary>
        /// Builds the controller at <paramref name="assetPath"/>. Returns null when there is not a
        /// single usable clip, in which case the caller should leave the Animator unconfigured.
        /// </summary>
        public static AnimatorController Build(string assetPath, IReadOnlyList<AnimationClip> clips,
            float walkSpeed, float runSpeed)
        {
            AnimationClip idle = BestMatch(clips, "idle");
            AnimationClip walk = BestMatch(clips, "walk");
            AnimationClip run = BestMatch(clips, "run", "jog", "sprint");
            AnimationClip jump = BestMatch(clips, "jump");
            AnimationClip fall = BestMatch(clips, "fall", "falling") ?? jump;

            if (idle == null && walk == null && run == null)
                return null;

            string directory = System.IO.Path.GetDirectoryName(assetPath).Replace('\\', '/');
            if (!AssetDatabase.IsValidFolder(directory))
                CreateFolders(directory);

            AssetDatabase.DeleteAsset(assetPath);
            AnimatorController controller = AnimatorController.CreateAnimatorControllerAtPath(assetPath);
            controller.AddParameter(SpeedParameter, AnimatorControllerParameterType.Float);
            controller.AddParameter(GroundedParameter, AnimatorControllerParameterType.Bool);
            controller.AddParameter(JumpParameter, AnimatorControllerParameterType.Trigger);

            // Default Grounded to true so the character does not start in the airborne state.
            // The parameters getter rebuilds its array every call, so mutate one copy and write it
            // straight back rather than reading it twice.
            AnimatorControllerParameter[] parameters = controller.parameters;
            foreach (AnimatorControllerParameter parameter in parameters)
            {
                if (parameter.name == GroundedParameter)
                    parameter.defaultBool = true;
            }
            controller.parameters = parameters;

            AnimatorState locomotion = BuildLocomotionState(controller, idle, walk, run, walkSpeed, runSpeed);

            AnimatorStateMachine stateMachine = controller.layers[0].stateMachine;
            stateMachine.defaultState = locomotion;

            if (fall != null)
            {
                AnimatorState airborne = stateMachine.AddState("Airborne");
                airborne.motion = fall;

                AnimatorStateTransition toAir = locomotion.AddTransition(airborne);
                toAir.hasExitTime = false;
                toAir.duration = 0.1f;
                toAir.AddCondition(AnimatorConditionMode.IfNot, 0f, GroundedParameter);

                AnimatorStateTransition toGround = airborne.AddTransition(locomotion);
                toGround.hasExitTime = false;
                toGround.duration = 0.12f;
                toGround.AddCondition(AnimatorConditionMode.If, 0f, GroundedParameter);
            }

            EditorUtility.SetDirty(controller);
            AssetDatabase.SaveAssets();
            return controller;
        }

        static AnimatorState BuildLocomotionState(AnimatorController controller, AnimationClip idle,
            AnimationClip walk, AnimationClip run, float walkSpeed, float runSpeed)
        {
            var entries = new List<(AnimationClip clip, float threshold)>();
            if (idle != null) entries.Add((idle, 0f));
            if (walk != null) entries.Add((walk, walkSpeed));
            if (run != null) entries.Add((run, runSpeed));

            if (entries.Count == 1)
            {
                AnimatorState single = controller.layers[0].stateMachine.AddState("Locomotion");
                single.motion = entries[0].clip;
                return single;
            }

            AnimatorState state = controller.CreateBlendTreeInController("Locomotion", out BlendTree tree);
            tree.blendType = BlendTreeType.Simple1D;
            tree.blendParameter = SpeedParameter;
            tree.useAutomaticThresholds = false;
            foreach ((AnimationClip clip, float threshold) in entries)
                tree.AddChild(clip, threshold);
            return state;
        }

        /// <summary>
        /// Picks the clip whose name best matches one of the keywords. Shorter names win, because
        /// packs tend to name the plain variant "Walk" and the specialised ones "Walk_Backwards".
        /// </summary>
        static AnimationClip BestMatch(IReadOnlyList<AnimationClip> clips, params string[] keywords)
        {
            AnimationClip best = null;
            int bestScore = int.MinValue;

            foreach (AnimationClip clip in clips)
            {
                if (clip == null)
                    continue;

                string name = Normalise(clip.name);
                for (int i = 0; i < keywords.Length; i++)
                {
                    if (!name.Contains(keywords[i]))
                        continue;

                    // Earlier keywords are preferred, exact matches most of all, then brevity.
                    int score = -i * 100;
                    if (name == keywords[i]) score += 1000;
                    score -= name.Length;

                    if (score > bestScore)
                    {
                        bestScore = score;
                        best = clip;
                    }
                    break;
                }
            }

            return best;
        }

        static string Normalise(string value)
        {
            var builder = new System.Text.StringBuilder(value.Length);
            foreach (char c in value)
            {
                if (char.IsLetterOrDigit(c))
                    builder.Append(char.ToLowerInvariant(c));
            }
            return builder.ToString();
        }

        static void CreateFolders(string path)
        {
            string[] parts = path.Replace('\\', '/').Split('/');
            string current = parts[0];
            for (int i = 1; i < parts.Length; i++)
            {
                string next = current + "/" + parts[i];
                if (!AssetDatabase.IsValidFolder(next))
                    AssetDatabase.CreateFolder(current, parts[i]);
                current = next;
            }
        }
    }
}
