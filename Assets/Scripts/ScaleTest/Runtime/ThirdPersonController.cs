using UnityEngine;
using UnityEngine.InputSystem;

namespace Toebeans.ScaleTest
{
    /// <summary>
    /// Character-controller driven third person movement, sized in real world units so the
    /// surrounding art can be judged against a believable human.
    /// </summary>
    [RequireComponent(typeof(CharacterController))]
    [DisallowMultipleComponent]
    public class ThirdPersonController : MonoBehaviour
    {
        [Header("Input")]
        [Tooltip("The project's InputSystem_Actions asset. Leave empty to fall back to raw keyboard/mouse/gamepad.")]
        public InputActionAsset inputActions;

        [Header("Scale")]
        [Tooltip("Standing height of the character in metres. 1.8 is an average adult male.")]
        public float standingHeight = 1.8f;
        [Tooltip("Height of the capsule while crouched, in metres.")]
        public float crouchHeight = 1.25f;
        [Tooltip("Capsule radius in metres. ~0.3 covers adult shoulder width closely enough for collision.")]
        public float radius = 0.3f;

        [Header("Speeds (metres / second)")]
        public float walkSpeed = 2.0f;
        public float runSpeed = 5.5f;
        public float crouchSpeed = 1.1f;
        [Tooltip("How fast horizontal velocity converges on the target velocity while grounded.")]
        public float groundAcceleration = 22f;
        [Tooltip("How fast horizontal velocity converges on the target velocity while airborne.")]
        public float airAcceleration = 6f;
        [Tooltip("Seconds taken to turn to face the movement direction.")]
        public float turnSmoothTime = 0.08f;

        [Header("Jumping and gravity")]
        [Tooltip("Apex of a standing jump in metres.")]
        public float jumpHeight = 1.1f;
        public float gravity = -18f;
        [Tooltip("Grace period after walking off a ledge during which a jump still registers.")]
        public float coyoteTime = 0.15f;
        public LayerMask groundLayers = ~0;

        [Header("Model")]
        [Tooltip("Transform holding the visual mesh. Rotated to face the movement direction.")]
        public Transform model;
        [Tooltip("Degrees to add to the model's yaw if the imported character does not face +Z.")]
        public float modelYawOffset = 0f;

        [Header("Safety net")]
        [Tooltip("Falling below this world Y teleports the character back to its spawn point.")]
        public float respawnBelowY = -50f;

        CharacterController _controller;
        Animator _animator;
        Transform _camera;
        PlayerInputReader _input;

        Vector3 _horizontalVelocity;
        float _verticalVelocity;
        float _turnVelocity;
        float _lastGroundedTime;
        float _lastJumpTime = -999f;
        bool _crouching;
        Vector3 _spawnPoint;

        const float JumpCooldown = 0.2f;

        // Animator parameters are looked up defensively: the controller may have been authored by
        // hand or generated from a pack that is missing some of the clips.
        static readonly int SpeedHash = Animator.StringToHash("Speed");
        static readonly int GroundedHash = Animator.StringToHash("Grounded");
        static readonly int JumpHash = Animator.StringToHash("Jump");
        static readonly int CrouchHash = Animator.StringToHash("Crouch");
        bool _hasSpeed, _hasGrounded, _hasJump, _hasCrouch;

        public PlayerInputReader Input => _input;
        /// <summary>Raw movement intent this frame, surfaced so the readout can prove input is arriving.</summary>
        public Vector2 MoveInput { get; private set; }
        public bool IsGrounded { get; private set; }
        public bool IsCrouching => _crouching;
        /// <summary>Current planar speed in metres per second.</summary>
        public float PlanarSpeed => _horizontalVelocity.magnitude;
        /// <summary>Approximate eye height above the character's feet, in metres.</summary>
        public float EyeHeight => _controller != null ? _controller.height * 0.94f : standingHeight * 0.94f;
        public float CurrentHeight => _controller != null ? _controller.height : standingHeight;

        void Awake()
        {
            _controller = GetComponent<CharacterController>();
            ApplyCapsule(standingHeight);
            _controller.radius = radius;
            _controller.skinWidth = Mathf.Max(0.015f, radius * 0.06f);
            _controller.stepOffset = Mathf.Min(0.4f, standingHeight * 0.25f);
            _controller.slopeLimit = 50f;

            _animator = GetComponentInChildren<Animator>();
            CacheAnimatorParameters();

            _input = new PlayerInputReader(inputActions);
            _spawnPoint = transform.position;
        }

        void OnEnable() => _input?.Enable();

        void OnDisable() => _input?.Disable();

        void Start()
        {
            if (Camera.main != null)
            {
                _camera = Camera.main.transform;
            }
            else
            {
                Debug.LogWarning("[ScaleTest] No camera is tagged MainCamera, so movement will follow world " +
                                 "axes instead of the camera, and the scale readout cannot measure anything.", this);
            }

            SetCursorLocked(true);
        }

        void Update()
        {
            HandleCursor();

            float dt = Time.deltaTime;
            UpdateGrounded();
            HandleCrouch();
            HandleMovement(dt);
            HandleJumpAndGravity(dt);

            _controller.Move((_horizontalVelocity + Vector3.up * _verticalVelocity) * dt);

            UpdateAnimator();

            if (transform.position.y < respawnBelowY)
                Respawn();
        }

        void UpdateGrounded()
        {
            bool grounded = _controller.isGrounded;

            if (!grounded)
            {
                // isGrounded is unreliable on terrain seams, so back it up with a short sphere cast.
                Vector3 origin = transform.position + Vector3.up * (_controller.radius + 0.05f);
                float castDistance = _controller.radius + 0.15f;
                if (Physics.SphereCast(origin, _controller.radius * 0.95f, Vector3.down, out RaycastHit hit,
                        castDistance, groundLayers, QueryTriggerInteraction.Ignore)
                    && hit.collider.transform != transform)
                {
                    grounded = true;
                }
            }

            IsGrounded = grounded;
            if (grounded)
                _lastGroundedTime = Time.time;
        }

        void HandleCrouch()
        {
            if (!_input.CrouchPressedThisFrame)
                return;

            if (_crouching)
            {
                // Only stand back up if there is headroom for the full capsule.
                Vector3 head = transform.position + Vector3.up * (standingHeight - _controller.radius);
                if (Physics.CheckSphere(head, _controller.radius * 0.95f, groundLayers, QueryTriggerInteraction.Ignore))
                    return;
                _crouching = false;
                ApplyCapsule(standingHeight);
            }
            else
            {
                _crouching = true;
                ApplyCapsule(crouchHeight);
            }
        }

        void ApplyCapsule(float height)
        {
            _controller.height = height;
            _controller.center = new Vector3(0f, height * 0.5f, 0f);
        }

        void HandleMovement(float dt)
        {
            Vector2 move = _input.Move;
            MoveInput = move;

            Vector3 forward = Vector3.forward;
            Vector3 right = Vector3.right;
            if (_camera != null)
            {
                forward = Vector3.ProjectOnPlane(_camera.forward, Vector3.up).normalized;
                right = Vector3.ProjectOnPlane(_camera.right, Vector3.up).normalized;
                if (forward.sqrMagnitude < 0.001f)
                    forward = Vector3.ProjectOnPlane(_camera.up, Vector3.up).normalized;
            }

            Vector3 desiredDirection = (forward * move.y + right * move.x);
            if (desiredDirection.sqrMagnitude > 1f)
                desiredDirection.Normalize();

            float targetSpeed = _crouching ? crouchSpeed : (_input.SprintHeld ? runSpeed : walkSpeed);
            Vector3 targetVelocity = desiredDirection * targetSpeed;

            float acceleration = IsGrounded ? groundAcceleration : airAcceleration;
            _horizontalVelocity = Vector3.MoveTowards(_horizontalVelocity, targetVelocity, acceleration * dt);

            if (model != null && desiredDirection.sqrMagnitude > 0.0001f)
            {
                float targetYaw = Mathf.Atan2(desiredDirection.x, desiredDirection.z) * Mathf.Rad2Deg + modelYawOffset;
                float yaw = Mathf.SmoothDampAngle(model.eulerAngles.y, targetYaw, ref _turnVelocity, turnSmoothTime);
                model.rotation = Quaternion.Euler(0f, yaw, 0f);
            }
        }

        void HandleJumpAndGravity(float dt)
        {
            // isGrounded can stay true for a frame or two after take-off, so gate on the cooldown
            // as well as the coyote window to keep a held jump from firing twice.
            bool canJump = Time.time - _lastGroundedTime <= coyoteTime
                           && Time.time - _lastJumpTime > JumpCooldown;

            if (IsGrounded && _verticalVelocity < 0f)
                _verticalVelocity = -2f; // keep the controller pinned to the ground

            if (canJump && !_crouching && _input.JumpPressedThisFrame)
            {
                _verticalVelocity = Mathf.Sqrt(2f * jumpHeight * -gravity);
                _lastJumpTime = Time.time;
                if (_hasJump)
                    _animator.SetTrigger(JumpHash);
            }

            _verticalVelocity += gravity * dt;
            _verticalVelocity = Mathf.Max(_verticalVelocity, -80f); // terminal velocity clamp
        }

        void UpdateAnimator()
        {
            if (_animator == null)
                return;
            if (_hasSpeed) _animator.SetFloat(SpeedHash, PlanarSpeed, 0.08f, Time.deltaTime);
            if (_hasGrounded) _animator.SetBool(GroundedHash, IsGrounded);
            if (_hasCrouch) _animator.SetBool(CrouchHash, _crouching);
        }

        void CacheAnimatorParameters()
        {
            if (_animator == null || _animator.runtimeAnimatorController == null)
                return;

            foreach (AnimatorControllerParameter parameter in _animator.parameters)
            {
                if (parameter.nameHash == SpeedHash) _hasSpeed = true;
                else if (parameter.nameHash == GroundedHash) _hasGrounded = true;
                else if (parameter.nameHash == JumpHash) _hasJump = true;
                else if (parameter.nameHash == CrouchHash) _hasCrouch = true;
            }
        }

        public void Respawn()
        {
            Teleport(_spawnPoint);
        }

        public void Teleport(Vector3 position)
        {
            _controller.enabled = false;
            transform.position = position;
            _controller.enabled = true;
            _horizontalVelocity = Vector3.zero;
            _verticalVelocity = 0f;
        }

        /// <summary>Sets the point the character returns to when it falls out of the world.</summary>
        public void SetSpawnPoint(Vector3 position) => _spawnPoint = position;

        void HandleCursor()
        {
            Keyboard keyboard = Keyboard.current;
            if (keyboard != null && keyboard.escapeKey.wasPressedThisFrame)
                SetCursorLocked(false);

            Mouse mouse = Mouse.current;
            if (mouse != null && mouse.leftButton.wasPressedThisFrame && Cursor.lockState != CursorLockMode.Locked)
                SetCursorLocked(true);
        }

        static void SetCursorLocked(bool locked)
        {
            Cursor.lockState = locked ? CursorLockMode.Locked : CursorLockMode.None;
            Cursor.visible = !locked;
        }

        void OnDrawGizmosSelected()
        {
            // Draw the capsule at authored size even when not playing, so the character can be
            // eyeballed against nearby props directly in the scene view.
            float height = Application.isPlaying && _controller != null ? _controller.height : standingHeight;
            Gizmos.color = new Color(0.2f, 0.9f, 1f, 0.9f);
            Vector3 basePos = transform.position;
            Gizmos.DrawWireSphere(basePos + Vector3.up * radius, radius);
            Gizmos.DrawWireSphere(basePos + Vector3.up * (height - radius), radius);
            Gizmos.DrawLine(basePos + Vector3.up * radius + Vector3.right * radius,
                basePos + Vector3.up * (height - radius) + Vector3.right * radius);
            Gizmos.DrawLine(basePos + Vector3.up * radius - Vector3.right * radius,
                basePos + Vector3.up * (height - radius) - Vector3.right * radius);
        }
    }
}
