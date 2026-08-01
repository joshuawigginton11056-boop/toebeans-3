using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Toebeans.ScaleTest.EditorTools
{
    /// <summary>
    /// Dumps everything that determines what the Game view shows, in one Console entry. Works in
    /// edit mode and in play mode; run it in play mode to capture live state.
    /// </summary>
    public static class PlayerSetupDiagnostics
    {
        [MenuItem("Tools/Toebeans/Diagnose Player Setup", false, 21)]
        public static void Diagnose()
        {
            var report = new StringBuilder();
            report.AppendLine("===== ScaleTest diagnostics =====");
            report.AppendLine($"Scene: {SceneManager.GetActiveScene().name}   Play mode: {Application.isPlaying}");
            report.AppendLine();

            ReportCameras(report);
            ReportPlayers(report);

            report.AppendLine("===== end =====");
            Debug.Log(report.ToString());
        }

        static void ReportCameras(StringBuilder report)
        {
            Camera[] cameras = Object.FindObjectsByType<Camera>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            report.AppendLine($"CAMERAS ({cameras.Length})");
            report.AppendLine($"  Camera.main = {(Camera.main == null ? "NONE — nothing is tagged MainCamera" : Camera.main.name)}");

            foreach (Camera camera in cameras)
            {
                var rig = camera.GetComponent<PlayerCameraRig>();
                report.AppendLine();
                report.AppendLine($"  '{camera.name}'");
                report.AppendLine($"    active={camera.gameObject.activeInHierarchy}  enabled={camera.enabled}  " +
                                  $"tag={camera.tag}  depth={camera.depth}");
                report.AppendLine($"    position={camera.transform.position}  euler={camera.transform.eulerAngles}");
                report.AppendLine($"    targetTexture={(camera.targetTexture == null ? "none (renders to screen)" : camera.targetTexture.name)}");
                report.AppendLine($"    near={camera.nearClipPlane}  far={camera.farClipPlane}  cullingMask=0x{camera.cullingMask:X}");

                if (rig == null)
                {
                    report.AppendLine("    PlayerCameraRig: NOT PRESENT");
                    continue;
                }

                report.AppendLine($"    PlayerCameraRig: present  enabled={rig.enabled}  " +
                                  $"target={(rig.target == null ? "NULL" : rig.target.name)}");
                report.AppendLine($"      distance={rig.distance}  minDistance={rig.minDistance}  " +
                                  $"pivotHeightFraction={rig.pivotHeightFraction}  firstPerson={rig.IsFirstPerson}");
            }

            report.AppendLine();
        }

        static void ReportPlayers(StringBuilder report)
        {
            ThirdPersonController[] players =
                Object.FindObjectsByType<ThirdPersonController>(FindObjectsInactive.Include, FindObjectsSortMode.None);

            report.AppendLine($"PLAYERS ({players.Length})");
            if (players.Length == 0)
                report.AppendLine("  NONE — run Tools > Toebeans > Set Up Playable Character.");

            foreach (ThirdPersonController player in players)
            {
                report.AppendLine();
                report.AppendLine($"  '{player.name}'  active={player.gameObject.activeInHierarchy}  " +
                                  $"enabled={player.enabled}");
                report.AppendLine($"    position={player.transform.position}");
                report.AppendLine($"    inputActions={(player.inputActions == null ? "NULL (raw device fallback)" : player.inputActions.name)}");

                var characterController = player.GetComponent<CharacterController>();
                if (characterController != null)
                {
                    report.AppendLine($"    CharacterController height={characterController.height} " +
                                      $"radius={characterController.radius} center={characterController.center}");
                }

                ReportModel(report, player);
                ReportAnimator(report, player);
            }

            report.AppendLine();
        }

        static void ReportModel(StringBuilder report, ThirdPersonController player)
        {
            Transform model = player.model;
            if (model == null)
            {
                report.AppendLine("    model: NULL — nothing visual is attached.");
                return;
            }

            report.AppendLine($"    model '{model.name}'  localScale={model.localScale}  " +
                              $"localPosition={model.localPosition}");

            Renderer[] renderers = model.GetComponentsInChildren<Renderer>(includeInactive: true);
            report.AppendLine($"      renderers={renderers.Length}");
            if (renderers.Length == 0)
            {
                report.AppendLine("      NO RENDERERS — the character will be invisible.");
                return;
            }

            Bounds bounds = renderers[0].bounds;
            foreach (Renderer renderer in renderers.Skip(1))
                bounds.Encapsulate(renderer.bounds);

            report.AppendLine($"      world bounds size={bounds.size} (this is the on-screen size in metres)");
            report.AppendLine($"      world bounds centre={bounds.center}");

            float heightAboveFeet = bounds.max.y - player.transform.position.y;
            report.AppendLine($"      top of model sits {heightAboveFeet:0.00} m above the player origin " +
                              "(should be about 1.80)");
        }

        static void ReportAnimator(StringBuilder report, ThirdPersonController player)
        {
            var animator = player.GetComponentInChildren<Animator>();
            if (animator == null)
            {
                report.AppendLine("    Animator: NONE (character will stand in its bind pose)");
                return;
            }

            report.AppendLine($"    Animator on '{animator.name}'  " +
                              $"controller={(animator.runtimeAnimatorController == null ? "NULL" : animator.runtimeAnimatorController.name)}  " +
                              $"avatar={(animator.avatar == null ? "NULL" : animator.avatar.name)}  " +
                              $"humanoid={animator.isHuman}");

            if (animator.runtimeAnimatorController == null)
                return;

            string clips = string.Join(", ", animator.runtimeAnimatorController.animationClips
                .Select(c => c.name)
                .Distinct());
            report.AppendLine($"      clips in controller: {clips}");
        }
    }
}
