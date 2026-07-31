using UnityEngine;
using UnityEngine.InputSystem;

namespace Toebeans.ScaleTest
{
    /// <summary>
    /// Orbiting follow camera. Pivots around the character's shoulders so the horizon sits at a
    /// believable eye level, and can drop into first person to read scale from the character's view.
    /// </summary>
    [RequireComponent(typeof(Camera))]
    [DisallowMultipleComponent]
    public class PlayerCameraRig : MonoBehaviour
    {
        [Header("Target")]
        public ThirdPersonController target;
        [Tooltip("Pivot height as a fraction of the character's current height. 0.9 sits at the shoulders.")]
        public float pivotHeightFraction = 0.9f;

        [Header("Look")]
        [Tooltip("Degrees of rotation per pixel of mouse movement.")]
        public float mouseSensitivity = 0.12f;
        [Tooltip("Degrees of rotation per second at full stick deflection.")]
        public float stickSensitivity = 180f;
        public float minPitch = -35f;
        public float maxPitch = 70f;
        public bool invertY = false;

        [Header("Distance")]
        public float distance = 3.5f;
        public float minDistance = 1.5f;
        public float maxDistance = 10f;
        public float zoomStep = 0.5f;
        [Tooltip("Radius of the sphere cast used to keep the camera out of geometry.")]
        public float collisionRadius = 0.25f;
        public LayerMask collisionLayers = ~0;

        [Header("First person")]
        [Tooltip("Key that toggles between third and first person.")]
        public Key firstPersonToggleKey = Key.V;

        float _yaw;
        float _pitch = 12f;
        float _currentDistance;
        bool _firstPerson;
        PlayerInputReader _input;
        Renderer[] _targetRenderers = System.Array.Empty<Renderer>();

        public bool IsFirstPerson => _firstPerson;

        void Start()
        {
            if (target == null)
                target = FindAnyObjectByType<ThirdPersonController>();

            _input = target != null ? target.Input : new PlayerInputReader(null);
            _input ??= new PlayerInputReader(null);

            _currentDistance = distance;
            Vector3 euler = transform.eulerAngles;
            _yaw = euler.y;
            _pitch = NormalisePitch(euler.x);

            if (target != null)
                _targetRenderers = target.GetComponentsInChildren<Renderer>(includeInactive: true);
        }

        void LateUpdate()
        {
            if (target == null)
                return;

            // The reader is created in the controller's Awake, so pick it up lazily if Start raced it.
            _input ??= target.Input;

            HandleToggles();
            HandleLook();

            Vector3 pivot = target.transform.position + Vector3.up * (target.CurrentHeight * pivotHeightFraction);
            Quaternion rotation = Quaternion.Euler(_pitch, _yaw, 0f);

            float desiredDistance = _firstPerson ? 0f : distance;
            Vector3 direction = rotation * Vector3.back;

            if (desiredDistance > 0f
                && Physics.SphereCast(pivot, collisionRadius, direction, out RaycastHit hit,
                    desiredDistance, collisionLayers, QueryTriggerInteraction.Ignore)
                && !IsPartOfTarget(hit.collider.transform))
            {
                desiredDistance = Mathf.Max(minDistance * 0.5f, hit.distance - collisionRadius);
            }

            // Snap in instantly, ease out, so walking into a wall never clips through the character.
            _currentDistance = desiredDistance < _currentDistance
                ? desiredDistance
                : Mathf.Lerp(_currentDistance, desiredDistance, 1f - Mathf.Exp(-8f * Time.deltaTime));

            Vector3 eyeOffset = _firstPerson
                ? rotation * Vector3.forward * (target.radius + 0.05f)
                : Vector3.zero;

            transform.SetPositionAndRotation(pivot + direction * _currentDistance + eyeOffset, rotation);
        }

        void HandleLook()
        {
            if (Cursor.lockState != CursorLockMode.Locked && Gamepad.current == null)
                return;

            Vector2 look = _input.LookDegrees(mouseSensitivity, stickSensitivity, Time.deltaTime);
            _yaw += look.x;
            _pitch += invertY ? look.y : -look.y;
            _pitch = Mathf.Clamp(_pitch, minPitch, maxPitch);

            float zoom = _input.ZoomDelta;
            if (!Mathf.Approximately(zoom, 0f))
                distance = Mathf.Clamp(distance - zoom * zoomStep, minDistance, maxDistance);
        }

        void HandleToggles()
        {
            Keyboard keyboard = Keyboard.current;
            if (keyboard == null || !keyboard[firstPersonToggleKey].wasPressedThisFrame)
                return;

            _firstPerson = !_firstPerson;
            // Keep the character casting shadows in first person; seeing your own shadow is one of
            // the better cues for whether the world is sized right.
            foreach (Renderer renderer in _targetRenderers)
            {
                if (renderer == null)
                    continue;
                renderer.shadowCastingMode = _firstPerson
                    ? UnityEngine.Rendering.ShadowCastingMode.ShadowsOnly
                    : UnityEngine.Rendering.ShadowCastingMode.On;
            }
        }

        bool IsPartOfTarget(Transform other)
        {
            return target != null && other != null && other.IsChildOf(target.transform);
        }

        static float NormalisePitch(float pitch)
        {
            return pitch > 180f ? pitch - 360f : pitch;
        }
    }
}
