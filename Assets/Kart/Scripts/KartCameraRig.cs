using UnityEngine;
using UnityEngine.InputSystem;

namespace Toebeans.Karting
{
    /// <summary>
    /// Chase camera for the kart. Trails behind the direction of travel, leans back and widens as speed
    /// builds, and stays level with the world however the kart is pitched.
    ///
    /// That last part matters more than it sounds: if the camera rolls with the chassis, the horizon
    /// stops being a reference and you lose all sense of which way is down. Keeping it world-up is what
    /// lets you read a crest, a landing and a slope for what they are.
    /// </summary>
    [RequireComponent(typeof(Camera))]
    [DisallowMultipleComponent]
    public class KartCameraRig : MonoBehaviour
    {
        [Header("Target")]
        public KartController target;
        [Tooltip("Height above the kart's origin that the camera aims at, in metres.")]
        public float pivotHeight = 0.85f;

        [Header("Framing")]
        public float distance = 6.0f;
        public float minDistance = 2.5f;
        public float maxDistance = 14f;
        public float zoomStep = 0.6f;
        [Tooltip("Height of the camera above the pivot, in metres.")]
        public float height = 2.2f;
        [Tooltip("Extra distance at top speed, in metres. Pulling back as you speed up sells the pace.")]
        public float speedDistance = 1.6f;

        [Header("Field of view")]
        public float baseFieldOfView = 60f;
        [Tooltip("Field of view at top speed. The widening is most of what makes speed feel like speed.")]
        public float topSpeedFieldOfView = 76f;

        [Header("Responsiveness")]
        [Tooltip("How quickly the camera swings around to sit behind the kart. Lower is lazier.")]
        public float yawFollowSpeed = 4f;
        [Tooltip("How quickly the camera catches up in position. Lower trails further behind.")]
        public float positionFollowSpeed = 9f;

        [Header("Look")]
        public float mouseSensitivity = 0.12f;
        public float stickSensitivity = 180f;
        public float minPitch = -10f;
        public float maxPitch = 60f;
        [Tooltip("Degrees per second the manual look drifts back behind the kart once you let go.")]
        public float lookRecentreSpeed = 45f;
        public bool invertY = false;

        [Header("Collision")]
        public float collisionRadius = 0.35f;
        public LayerMask collisionLayers = ~0;

        [Header("Keys")]
        public Key lookBackKey = Key.C;

        const float MinimumFramingDistance = 1.2f;

        float _yaw;
        float _pitch = 12f;
        float _yawOffset;
        float _currentDistance;
        KartInputReader _input;
        Transform _targetTransform;

        void Start()
        {
            if (target == null)
                target = FindAnyObjectByType<KartController>();

            if (target == null)
            {
                Debug.LogError($"[Kart] {name} has no KartController to follow, so the camera will not " +
                               "move. Re-run Tools > Toebeans > Set Up Drivable Kart.", this);
                enabled = false;
                return;
            }

            _targetTransform = target.transform;
            _input = target.Input ?? new KartInputReader(null);

            // The camera ships as a child of the kart so the prefab is self-contained, but it must not
            // stay one. A child inherits the chassis's roll and pitch, and a camera that tips with the
            // kart destroys the horizon — which is the only reference telling you which way is down.
            // Detaching costs nothing and makes the rig's world-space maths below unconditional.
            if (transform.parent != null && transform.IsChildOf(_targetTransform))
                transform.SetParent(null, worldPositionStays: true);
            _currentDistance = distance;
            _yaw = _targetTransform.eulerAngles.y;

            Camera self = GetComponent<Camera>();
            if (Camera.main != self)
            {
                Debug.LogWarning($"[Kart] The chase rig is on '{name}', but Camera.main is " +
                                 $"'{(Camera.main == null ? "none" : Camera.main.name)}'. If the Game view does " +
                                 "not follow the kart, that other camera is the one rendering.", this);
            }

            UpdateCamera(1f);
        }

        void LateUpdate()
        {
            if (target == null)
                return;

            _input ??= target.Input;
            HandleCursor();
            HandleLook();
            UpdateCamera(Time.deltaTime);
        }

        /// <summary>
        /// Mouse orbit only works while the cursor is captured, so capture it on the first click into
        /// the Game view and hand it back on Escape.
        /// </summary>
        static void HandleCursor()
        {
            if (Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame)
            {
                Cursor.lockState = CursorLockMode.None;
                Cursor.visible = true;
            }
            else if (Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame)
            {
                Cursor.lockState = CursorLockMode.Locked;
                Cursor.visible = false;
            }
        }

        void HandleLook()
        {
            bool allowPointer = Cursor.lockState == CursorLockMode.Locked;
            Vector2 look = _input.LookDegrees(mouseSensitivity, stickSensitivity, Time.deltaTime, allowPointer);

            _yawOffset += look.x;
            _pitch += invertY ? look.y : -look.y;
            _pitch = Mathf.Clamp(_pitch, minPitch, maxPitch);

            if (Mathf.Approximately(look.x, 0f))
                _yawOffset = Mathf.MoveTowards(_yawOffset, 0f, lookRecentreSpeed * Time.deltaTime);

            float zoom = _input.ZoomDelta;
            if (!Mathf.Approximately(zoom, 0f))
                distance = Mathf.Clamp(distance - zoom * zoomStep, minDistance, maxDistance);
        }

        void UpdateCamera(float deltaTime)
        {
            Vector3 pivot = _targetTransform.position + Vector3.up * pivotHeight;

            // Trail the direction of travel once moving quickly enough for it to mean anything, and
            // the nose otherwise. Sitting behind the velocity is what keeps a drift readable — you see
            // where the kart is going, not only where it is pointing.
            Vector3 heading = target.ForwardSpeed > 2f
                ? Vector3.Slerp(_targetTransform.forward, FlatVelocity(), 0.35f)
                : _targetTransform.forward;

            heading.y = 0f;
            if (heading.sqrMagnitude < 0.0001f)
                heading = Vector3.forward;

            float desiredYaw = Mathf.Atan2(heading.x, heading.z) * Mathf.Rad2Deg;
            _yaw = Mathf.LerpAngle(_yaw, desiredYaw, 1f - Mathf.Exp(-yawFollowSpeed * deltaTime));

            bool lookingBack = Keyboard.current != null && Keyboard.current[lookBackKey].isPressed;
            float yaw = _yaw + _yawOffset + (lookingBack ? 180f : 0f);

            float speedFraction = Mathf.Clamp01(Mathf.Abs(target.ForwardSpeed) / Mathf.Max(target.topSpeed, 0.01f));
            float desiredDistance = distance + speedDistance * speedFraction;

            Quaternion flatRotation = Quaternion.Euler(0f, yaw, 0f);
            Vector3 back = flatRotation * Vector3.back;
            Vector3 desiredPosition = pivot + back * desiredDistance + Vector3.up * height;

            // Keep the camera out of the hillside, but never let a cast that starts already inside
            // something yank it into the kart.
            Vector3 toCamera = desiredPosition - pivot;
            float castDistance = toCamera.magnitude;
            if (castDistance > 0.01f
                && Physics.SphereCast(pivot, collisionRadius, toCamera / castDistance, out RaycastHit hit,
                    castDistance, collisionLayers, QueryTriggerInteraction.Ignore)
                && hit.distance > 0.01f
                && !hit.collider.transform.IsChildOf(_targetTransform))
            {
                float allowed = Mathf.Max(MinimumFramingDistance, hit.distance - collisionRadius);
                desiredPosition = pivot + toCamera / castDistance * allowed;
            }

            // Snap in, ease out: never let terrain push the camera through the kart, but do not jerk
            // back out the instant it clears.
            float follow = 1f - Mathf.Exp(-positionFollowSpeed * deltaTime);
            transform.position = Vector3.Distance(desiredPosition, pivot) < _currentDistance
                ? desiredPosition
                : Vector3.Lerp(transform.position, desiredPosition, follow);
            _currentDistance = Vector3.Distance(transform.position, pivot);

            Vector3 lookTarget = pivot + Vector3.up * (Mathf.Tan(-_pitch * Mathf.Deg2Rad) * _currentDistance);
            transform.rotation = Quaternion.LookRotation(lookTarget - transform.position, Vector3.up);

            var camera = GetComponent<Camera>();
            camera.fieldOfView = Mathf.Lerp(
                camera.fieldOfView,
                Mathf.Lerp(baseFieldOfView, topSpeedFieldOfView, speedFraction),
                1f - Mathf.Exp(-3f * deltaTime));
        }

        Vector3 FlatVelocity()
        {
            var body = target.GetComponent<Rigidbody>();
            if (body == null)
                return _targetTransform.forward;

            Vector3 velocity = body.linearVelocity;
            velocity.y = 0f;
            return velocity.sqrMagnitude < 0.01f ? _targetTransform.forward : velocity.normalized;
        }
    }
}
