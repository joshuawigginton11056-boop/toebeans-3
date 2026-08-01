using UnityEngine;
using UnityEngine.InputSystem;

namespace Toebeans.Skiing
{
    /// <summary>
    /// Chase camera for skiing. Half the feeling of speed is here rather than in the sim: the
    /// camera trails the direction of TRAVEL (not the skis, so a skid reads as a skid), widens
    /// its field of view as the run builds, and rolls a little into the turns.
    ///
    /// It also pitches down with the terrain, because a horizontal camera on a steep pitch fills
    /// the screen with sky instead of the run ahead.
    /// </summary>
    [RequireComponent(typeof(Camera))]
    [DisallowMultipleComponent]
    public class SkiCameraRig : MonoBehaviour
    {
        [Header("Target")]
        public SkiController target;
        [Tooltip("Height up the skier the camera aims at, in metres.")]
        public float pivotHeight = 1.2f;

        [Header("Framing")]
        [Tooltip("Distance behind the skier at low speed, metres.")]
        public float distance = 6f;
        [Tooltip("Extra distance added at top speed — the world pulls away as you accelerate.")]
        public float distanceAtSpeed = 2.5f;
        [Tooltip("Height above the pivot, metres.")]
        public float height = 1.8f;
        [Tooltip("Degrees the camera looks down at the skier.")]
        public float pitch = 8f;
        [Tooltip("Fraction of the terrain's slope angle the camera pitches with. 0 keeps it level " +
                 "(sky-heavy on steeps); 1 fully follows the hill.")]
        [Range(0f, 1f)] public float slopeFollow = 0.55f;

        [Header("Speed cues")]
        [Tooltip("Field of view at a standstill.")]
        public float baseFov = 60f;
        [Tooltip("Field of view at fovSpeed and above. The single biggest speed cue there is.")]
        public float maxFov = 82f;
        [Tooltip("Speed, m/s, at which the field of view reaches its maximum.")]
        public float fovSpeed = 22f;
        [Tooltip("Degrees the camera rolls into a full-speed turn. Small numbers do a lot here.")]
        public float maxRoll = 5f;

        [Header("Smoothing")]
        [Tooltip("Seconds for the camera position to catch up. Larger = laggier, more dramatic.")]
        public float followSmoothing = 0.16f;
        [Tooltip("Seconds for the camera's yaw to catch up with the travel direction.")]
        public float yawSmoothing = 0.22f;
        [Tooltip("Below this speed the camera parks behind the skis instead of chasing travel, so a " +
                 "standstill does not leave it pointing at nothing.")]
        public float travelYawMinSpeed = 2f;

        [Header("Look")]
        [Tooltip("Mouse free-look sensitivity, degrees per pixel. Set 0 to lock the camera to travel.")]
        public float lookSensitivity = 0.12f;
        [Tooltip("Seconds of no mouse movement before free-look recentres behind the skier.")]
        public float lookRecentreDelay = 1.5f;

        [Header("Collision")]
        public LayerMask collisionLayers = ~0;
        public float collisionRadius = 0.3f;

        Camera _camera;
        float _yaw;
        float _yawVelocity;
        float _pitch;
        float _roll;
        float _rollVelocity;
        Vector3 _position;
        Vector3 _positionVelocity;
        float _lookYawOffset;
        float _lookPitchOffset;
        float _lastLookTime = -99f;
        bool _initialised;

        void Awake()
        {
            _camera = GetComponent<Camera>();
            if (_camera.farClipPlane < 500f)
                _camera.farClipPlane = 2000f;
        }

        void OnEnable()
        {
            _initialised = false;
        }

        void LateUpdate()
        {
            if (target == null)
            {
                target = Object.FindAnyObjectByType<SkiController>();
                if (target == null)
                    return;
            }

            float dt = Time.deltaTime;
            if (dt <= 0f)
                return;

            ReadFreeLook(dt);

            Vector3 pivot = target.transform.position + Vector3.up * pivotHeight;
            float speed = target.Speed;

            // Chase travel, not the skis. When the two disagree you are sideways, and seeing the
            // skis crossed in front of the camera is exactly what a skid should look like.
            Vector3 travel = new Vector3(target.Velocity.x, 0f, target.Velocity.z);
            Vector3 aim = speed > travelYawMinSpeed && travel.sqrMagnitude > 0.01f
                ? travel.normalized
                : target.SkiForward;
            float targetYaw = Mathf.Atan2(aim.x, aim.z) * Mathf.Rad2Deg;

            if (!_initialised)
            {
                _yaw = targetYaw;
                _pitch = pitch;
                _position = pivot;
                _initialised = true;
            }

            _yaw = Mathf.SmoothDampAngle(_yaw, targetYaw, ref _yawVelocity, yawSmoothing);

            // Terrain pitch, signed: the fall line under the skier tilts the whole frame so the run
            // ahead stays in shot on a steep face.
            float slopePitch = 0f;
            if (slopeFollow > 0f)
            {
                Vector3 fall = Vector3.ProjectOnPlane(Vector3.down, target.GroundNormal);
                if (fall.sqrMagnitude > 1e-5f)
                {
                    Vector3 forward = Quaternion.Euler(0f, _yaw, 0f) * Vector3.forward;
                    slopePitch = Vector3.Angle(target.GroundNormal, Vector3.up)
                                 * Mathf.Clamp01(Vector3.Dot(fall.normalized, forward))
                                 * slopeFollow;
                }
            }
            _pitch = Mathf.Lerp(_pitch, pitch + slopePitch, 1f - Mathf.Exp(-6f * dt));

            float speed01 = Mathf.Clamp01(speed / Mathf.Max(1f, fovSpeed));

            // Roll off the SIGNED skid — which side of the travel line the skis are on. Flip the
            // sign here if the lean reads backwards; a handful of degrees is all it takes.
            float signedSkid = Vector3.SignedAngle(aim, target.SkiForward, Vector3.up);
            float targetRoll = -Mathf.Clamp(signedSkid / 45f, -1f, 1f) * maxRoll * speed01;
            _roll = Mathf.SmoothDamp(_roll, targetRoll, ref _rollVelocity, 0.25f);

            // Placement is roll-free: rolling the offset would swing the camera sideways instead of
            // tilting the horizon.
            Quaternion placement = Quaternion.Euler(_pitch + _lookPitchOffset, _yaw + _lookYawOffset, 0f);

            float back = distance + distanceAtSpeed * speed01;
            Vector3 desired = pivot + Vector3.up * height + placement * Vector3.back * back;
            desired = ResolveCollision(pivot, desired);

            _position = Vector3.SmoothDamp(_position, desired, ref _positionVelocity, followSmoothing);

            Vector3 toPivot = pivot - _position;
            Quaternion look = toPivot.sqrMagnitude > 1e-4f
                ? Quaternion.LookRotation(toPivot, Vector3.up)
                : transform.rotation;
            transform.SetPositionAndRotation(_position, look * Quaternion.Euler(0f, 0f, _roll));

            _camera.fieldOfView = Mathf.Lerp(_camera.fieldOfView, Mathf.Lerp(baseFov, maxFov, speed01),
                1f - Mathf.Exp(-3f * dt));
        }

        void ReadFreeLook(float dt)
        {
            if (lookSensitivity <= 0f)
                return;

            Mouse mouse = Mouse.current;
            Vector2 delta = mouse != null && Cursor.lockState == CursorLockMode.Locked
                ? mouse.delta.ReadValue()
                : Vector2.zero;

            if (delta.sqrMagnitude > 0.01f)
            {
                _lookYawOffset = Mathf.Clamp(_lookYawOffset + delta.x * lookSensitivity, -140f, 140f);
                _lookPitchOffset = Mathf.Clamp(_lookPitchOffset - delta.y * lookSensitivity, -35f, 45f);
                _lastLookTime = Time.time;
                return;
            }

            // Hands off the mouse and the camera settles back behind the run on its own — skiing
            // wants both hands on the steering, not on the framing.
            if (Time.time - _lastLookTime < lookRecentreDelay)
                return;

            float t = 1f - Mathf.Exp(-3f * dt);
            _lookYawOffset = Mathf.Lerp(_lookYawOffset, 0f, t);
            _lookPitchOffset = Mathf.Lerp(_lookPitchOffset, 0f, t);
        }

        Vector3 ResolveCollision(Vector3 pivot, Vector3 desired)
        {
            Vector3 direction = desired - pivot;
            float length = direction.magnitude;
            if (length < 0.01f)
                return desired;

            if (Physics.SphereCast(pivot, collisionRadius, direction / length, out RaycastHit hit,
                    length, collisionLayers, QueryTriggerInteraction.Ignore)
                && (target == null || !hit.collider.transform.IsChildOf(target.transform)))
            {
                return pivot + direction / length * Mathf.Max(1f, hit.distance - collisionRadius * 0.5f);
            }

            return desired;
        }
    }
}
