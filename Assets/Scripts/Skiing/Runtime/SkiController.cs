using Toebeans.ScaleTest;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Toebeans.Skiing
{
    /// <summary>
    /// Skiing on real terrain. The mountain drives the speed: gravity is resolved along the
    /// surface under the skis, the edges bite sideways drift away, and everything the player
    /// does is aimed at deciding how much of the fall line they take.
    ///
    /// This deliberately does NOT port the old rail sim (distance-down-a-track + lateral
    /// offset + an authored steepness number). The behaviours that sim hand-coded — turning
    /// costing speed, landings sliding before the skis bite, steeps skiing faster — all fall
    /// out of the slope and the edge model here, so they are physics rather than special
    /// cases. The feel rules that survived playtesting are kept as rules:
    ///
    ///   * The heading is where you PUT it. Releasing the steer key does not straighten you.
    ///   * Steering authority builds with speed but never reaches zero — a stopped skier can
    ///     still pivot their skis, otherwise a full sideways stop is a softlock.
    ///   * Turning is braking. Here that is the edges scrubbing the sideways component.
    ///   * Jumping is hold-to-charge, and a landing locks the jump out for a beat.
    ///   * Riding switch is a stance, not a crash.
    /// </summary>
    [RequireComponent(typeof(CharacterController))]
    [DisallowMultipleComponent]
    public class SkiController : MonoBehaviour
    {
        [Header("Input")]
        [Tooltip("The project's InputSystem_Actions asset. Leave empty to fall back to raw keyboard/mouse/gamepad.")]
        public InputActionAsset inputActions;

        [Header("Body")]
        [Tooltip("Standing height in metres. 1.8 is an average adult.")]
        public float standingHeight = 1.8f;
        [Tooltip("Capsule radius in metres.")]
        public float radius = 0.35f;

        [Header("Gravity")]
        [Tooltip("Downward acceleration. Kept near real (-9.81) on purpose: running it hot forces " +
                 "the drag below to run hot too, and then every flat runout brakes like a wall. " +
                 "Raise it and you must raise the drag with it.")]
        public float gravity = -11f;
        [Tooltip("Hard ceiling on travel speed, m/s. 30 m/s is 108 km/h.")]
        public float maxSpeed = 30f;
        [Tooltip("How much speed is carried through a change of slope while still on the snow. " +
                 "1 = skis follow the terrain and keep their momentum; 0 = every rollover onto a " +
                 "flatter pitch costs you the vertical part of your velocity. Landings from the " +
                 "air are separate — see landingRetention.")]
        [Range(0f, 1f)] public float terrainFollowRetention = 1f;

        [Header("Edges")]
        [Tooltip("How fast sideways drift bleeds off, per second. This is the whole ski feel: high " +
                 "values carve on rails, low values slide around like a sled. It also sets how long " +
                 "a sideways landing slips before the skis bite.")]
        public float edgeGrip = 5.5f;
        [Tooltip("Fraction of scrubbed sideways speed that is redirected into forward speed. This is " +
                 "the carve — a good turn trades drift for pace instead of just losing it. 0 = every " +
                 "turn is pure braking.")]
        [Range(0f, 1f)] public float carveRedirect = 0.35f;
        [Tooltip("Extra grip while the carve key (Shift) is held — commit to the edge.")]
        public float carveGripMultiplier = 1.8f;
        [Tooltip("Extra grip while braking (S) — the snowplough digs in sideways as well as slowing you.")]
        public float brakeGripMultiplier = 2.2f;

        [Header("Drag")]
        // Drag and gravity are a pair, and the pairing is the reason flats can feel like walls.
        // Whatever speed the drag settles you at on a pitch, arriving on a flat AT that speed costs
        // you exactly the gravity you had been gaining — that is unavoidable, not a bug. The lever
        // is to stop the player living at terminal speed: keep gravity near real, keep the drag
        // gentle enough that a normal run sits well below the ceiling, and the flats open up.
        [Tooltip("Constant snow friction, m/s². Waxed skis on snow is roughly 0.3–0.6. This is what " +
                 "eventually stops you on a dead flat; keep it small or runouts die.")]
        public float snowFriction = 0.35f;
        [Tooltip("Quadratic air drag standing up. With gravity -11 this settles terminal speed near " +
                 "22 m/s on a 25° pitch — and only costs ~3 m/s² at speed on the flat.")]
        public float glideDrag = 0.009f;
        [Tooltip("Quadratic air drag while tucked (W). Less than half of glide, so tucking roughly " +
                 "doubles how far a flat runout carries you — which is what tucking is FOR.")]
        public float tuckDrag = 0.004f;
        [Tooltip("Braking deceleration along the skis (S), m/s², on top of the extra edge grip.")]
        public float brakeDecel = 12f;

        [Header("Steering")]
        [Tooltip("Degrees per second the skis rotate at full authority.")]
        public float turnRate = 115f;
        [Tooltip("Turn rate multiplier while the carve key (Shift) is held.")]
        public float carveTurnMultiplier = 1.45f;
        [Tooltip("Speed, m/s, at which steering reaches full authority.")]
        public float fullAuthoritySpeed = 6f;
        [Tooltip("Steering authority at a standstill. Never 0 — you must be able to pivot out of a " +
                 "sideways stop or the run is softlocked.")]
        [Range(0.05f, 1f)] public float standstillAuthority = 0.4f;
        [Tooltip("Degrees per second the body rotates in the air. A trick rate, not an edge carve, so " +
                 "it runs flat out regardless of speed. Flight itself stays ballistic.")]
        public float airSpinRate = 280f;

        [Header("Jumping")]
        // Sized against gravity: these clear ~0.6 m and ~2 m. Lowering gravity would make the same
        // numbers float, so the two were retuned together.
        [Tooltip("Launch speed from a tap, m/s. Clears about 0.6 m.")]
        public float minJumpSpeed = 3.6f;
        [Tooltip("Launch speed from a full charge, m/s. Clears about 2 m.")]
        public float maxJumpSpeed = 6.6f;
        [Tooltip("Seconds of held jump to reach a full charge.")]
        public float jumpChargeTime = 0.6f;
        [Tooltip("Seconds after touchdown during which the jump key does nothing — kills the pogo bounce.")]
        public float landingRecovery = 0.3f;
        [Tooltip("How far the launch tips away from world up toward the slope normal. 1 = straight off " +
                 "the lip, 0 = straight up.")]
        [Range(0f, 1f)] public float jumpFollowsSlope = 0.5f;
        [Tooltip("Fraction of the speed a landing would lose into the slope that is kept as forward " +
                 "speed instead. 0 = every landing is a wall.")]
        [Range(0f, 1f)] public float landingRetention = 0.4f;

        [Header("Ground contact")]
        public LayerMask groundLayers = ~0;
        [Tooltip("Downward pull applied while grounded so the skis follow terrain that rolls away " +
                 "beneath them. Raise it to stop popping off every bump; lower it to get air off lips.")]
        public float groundStick = 5f;
        [Tooltip("Gap to the surface, in metres, still counted as being on the snow.")]
        public float groundSnapDistance = 0.35f;

        [Header("Model")]
        [Tooltip("Transform holding the visual mesh. Yawed, banked and leaned to sell the skiing.")]
        public Transform model;
        [Tooltip("Degrees to add to the model's yaw if the imported character does not face +Z.")]
        public float modelYawOffset = 0f;
        [Tooltip("How far the body rolls into a full-speed turn, degrees.")]
        public float maxBankAngle = 26f;
        [Tooltip("Forward lean while tucked, degrees.")]
        public float maxTuckLean = 22f;
        [Tooltip("How much the body pitches to follow the slope. 1 = fully slope-aligned.")]
        [Range(0f, 1f)] public float slopeAlignment = 0.85f;
        [Tooltip("Seconds for lean and bank to catch up. Small = twitchy, large = floaty.")]
        public float poseSmoothing = 0.12f;

        [Header("Safety net")]
        [Tooltip("Falling below this world Y returns the skier to the spawn point.")]
        public float respawnBelowY = -100f;

        CharacterController _controller;
        PlayerInputReader _input;
        Vector3 _spawnPoint;
        float _spawnHeading;

        Vector3 _velocity;
        float _heading;              // world yaw of the ski tips, degrees
        float _jumpCharge;
        float _landingTimer;
        float _airTime;
        float _launchLockout;        // brief window after a launch where ground contact is ignored

        Vector3 _groundNormal = Vector3.up;
        bool _grounded;

        // Smoothed pose channels, kept separate from the sim so the look can be retuned freely.
        float _bank, _bankVelocity;
        float _lean, _leanVelocity;
        float _crouch, _crouchVelocity;

        public PlayerInputReader Input => _input;
        public bool IsGrounded => _grounded;
        /// <summary>Travel speed in metres per second — the number that reads as "how fast am I going".</summary>
        public float Speed => _velocity.magnitude;
        public Vector3 Velocity => _velocity;
        public Vector3 GroundNormal => _groundNormal;
        /// <summary>Degrees between where the skis point and where the body is actually travelling.</summary>
        public float SkidAngle { get; private set; }
        public float SlopeAngle => Vector3.Angle(_groundNormal, Vector3.up);
        public float JumpCharge01 => jumpChargeTime <= 0f ? 0f : _jumpCharge / jumpChargeTime;
        public float AirTime => _airTime;
        /// <summary>Which way the skis point, as a world direction on the horizontal plane.</summary>
        public Vector3 SkiForward => Quaternion.Euler(0f, _heading, 0f) * Vector3.forward;
        /// <summary>True when travel is tails-first — a stance, not a crash.</summary>
        public bool RidingSwitch { get; private set; }

        void Awake()
        {
            _controller = GetComponent<CharacterController>();
            _controller.height = standingHeight;
            _controller.radius = radius;
            _controller.center = new Vector3(0f, standingHeight * 0.5f, 0f);
            _controller.skinWidth = Mathf.Max(0.015f, radius * 0.06f);
            _controller.stepOffset = 0.3f;
            // The default 45° limit makes the controller refuse to move on exactly the terrain we
            // want to ski, and applies its own sliding on top of ours. Skiing owns the slope.
            _controller.slopeLimit = 89f;
            _controller.minMoveDistance = 0f;

            _input = new PlayerInputReader(inputActions);
            _spawnPoint = transform.position;
            _heading = transform.eulerAngles.y;
            _spawnHeading = _heading;
        }

        void OnEnable() => _input?.Enable();

        void OnDisable() => _input?.Disable();

        void Start()
        {
            ProbeGround(out _grounded, out _groundNormal);
            SetCursorLocked(true);
        }

        void Update()
        {
            HandleCursor();

            float dt = Time.deltaTime;
            if (dt <= 0f)
                return;

            Vector2 move = _input.Move;
            float steer = move.x;
            bool tucking = move.y > 0.3f;
            bool braking = move.y < -0.3f;
            bool carving = _input.SprintHeld;
            bool jumpHeld = _input.JumpHeld;

            _launchLockout = Mathf.Max(0f, _launchLockout - dt);
            bool wasGrounded = _grounded;
            ProbeGround(out bool onSnow, out Vector3 normal);
            _grounded = onSnow && _launchLockout <= 0f;
            if (_grounded)
                _groundNormal = normal;

            if (_grounded && !wasGrounded)
                Land();
            if (!_grounded && wasGrounded)
                _airTime = 0f;

            Steer(steer, carving, dt);

            if (_grounded)
            {
                _landingTimer = Mathf.Max(0f, _landingTimer - dt);
                GroundedStep(tucking, braking, carving, dt);
                HandleJump(jumpHeld, dt);
            }
            else
            {
                _airTime += dt;
                // Flight is ballistic. Spinning turns the body, not the path — you land carrying
                // the line you took off with, which is what makes a bad landing feel earned.
                _velocity.y += gravity * dt;
                _jumpCharge = 0f;
            }

            Vector3 step = _velocity;
            if (_grounded)
                step += -_groundNormal * groundStick;

            _controller.Move(step * dt);

            if (_grounded)
                SnapToSurface();

            UpdateReadouts();
            PoseModel(steer, tucking, dt);

            if (transform.position.y < respawnBelowY)
                Respawn();
        }

        // ---------------------------------------------------------------- steering

        void Steer(float steer, bool carving, float dt)
        {
            if (Mathf.Abs(steer) < 0.01f)
                return;

            float rate;
            if (_grounded)
            {
                // Authority builds with speed because carving comes from the skis biting, but it
                // floors above zero so a standstill pivot is always available.
                float authority = Mathf.Lerp(standstillAuthority, 1f,
                    Mathf.Clamp01(Speed / Mathf.Max(0.01f, fullAuthoritySpeed)));
                rate = turnRate * authority * (carving ? carveTurnMultiplier : 1f);
            }
            else
            {
                rate = airSpinRate;
            }

            _heading = Mathf.Repeat(_heading + steer * rate * dt + 180f, 360f) - 180f;
        }

        // ---------------------------------------------------------------- on the snow

        void GroundedStep(bool tucking, bool braking, bool carving, float dt)
        {
            Vector3 n = _groundNormal;

            // The ski axis and its perpendicular, both laid onto the surface under the skis.
            Vector3 forward = Vector3.ProjectOnPlane(SkiForward, n);
            if (forward.sqrMagnitude < 1e-6f)
                forward = Vector3.ProjectOnPlane(transform.forward, n);
            forward.Normalize();
            Vector3 side = Vector3.Cross(n, forward);

            // Anything pointing into or out of the slope is contact, not travel — but simply
            // projecting it away charges the skier for following the terrain. Rolling from a 25°
            // pitch onto the flat aims the velocity 25° into the ground, and the projection quietly
            // eats cos(25°) of it: a free 10% off the top of every runout. Skis do not slam into
            // the hill, they follow it, so the direction changes and the magnitude is kept. Real
            // impacts still cost — that is Land(), which only runs on an actual touchdown.
            Vector3 planar = Vector3.ProjectOnPlane(_velocity, n);
            float carried = _velocity.magnitude;
            if (planar.sqrMagnitude > 1e-6f && carried > 0.01f)
            {
                planar = planar.normalized
                         * Mathf.Lerp(planar.magnitude, carried, terrainFollowRetention);
            }

            float alongSkis = Vector3.Dot(planar, forward);
            float acrossSkis = Vector3.Dot(planar, side);

            // 1. Gravity, resolved along the surface. Pointed down the fall line this is the whole
            //    of it; traversing, most of it lands on the across-skis axis where the edges deal
            //    with it. No authored steepness number — the mountain is the steepness number.
            Vector3 slopeAccel = Vector3.ProjectOnPlane(new Vector3(0f, gravity, 0f), n);
            alongSkis += Vector3.Dot(slopeAccel, forward) * dt;
            acrossSkis += Vector3.Dot(slopeAccel, side) * dt;

            // 2. The edges bite. Sideways drift decays exponentially, and part of what is scrubbed
            //    is redirected along the skis — that is the carve, trading drift for pace. Because
            //    gravity keeps feeding the across axis, a hard traverse settles into a slow, honest
            //    side-slip instead of sticking to the hill; steeper slopes slip faster, for free.
            float grip = edgeGrip
                         * (carving ? carveGripMultiplier : 1f)
                         * (braking ? brakeGripMultiplier : 1f);
            float kept = Mathf.Exp(-grip * dt);
            float scrubbed = Mathf.Abs(acrossSkis) * (1f - kept);
            acrossSkis *= kept;
            // Redirect in the direction of travel, so riding switch carves the same way.
            float stance = alongSkis >= 0f ? 1f : -1f;
            alongSkis += stance * scrubbed * carveRedirect;

            // 3. Drag along the skis. Tucking cuts the quadratic term, which is what makes it the
            //    speed input: on a steep pitch it is worth several m/s of terminal speed.
            float drag = snowFriction + (tucking ? tuckDrag : glideDrag) * alongSkis * alongSkis;
            alongSkis = Mathf.MoveTowards(alongSkis, 0f, drag * dt);

            // 4. The brake. Deliberately a straight deceleration on top of the extra edge grip, so
            //    S both slows you and plants you rather than just scrubbing sideways.
            if (braking)
                alongSkis = Mathf.MoveTowards(alongSkis, 0f, brakeDecel * dt);

            _velocity = forward * alongSkis + side * acrossSkis;
            if (_velocity.magnitude > maxSpeed)
                _velocity = _velocity.normalized * maxSpeed;
        }

        void Land()
        {
            _landingTimer = landingRecovery;

            // The component heading into the slope is lost on impact — but throwing all of it away
            // makes every landing feel like hitting a wall, so part of it is paid back along the
            // surface. The skis are usually off the travel line at this point, and the edge model
            // above turns that into a visible slide before they bite. No landing timer needed.
            float intoSlope = Vector3.Dot(_velocity, _groundNormal);
            Vector3 planar = Vector3.ProjectOnPlane(_velocity, _groundNormal);
            if (intoSlope < 0f && planar.sqrMagnitude > 1e-6f)
                planar += planar.normalized * (-intoSlope * landingRetention);

            _velocity = planar;
        }

        // ---------------------------------------------------------------- jumping

        void HandleJump(bool jumpHeld, float dt)
        {
            if (_landingTimer > 0f)
            {
                // Fresh off a touchdown the legs are absorbing the hit: the key neither loads nor
                // launches. Short enough that hop-hop rhythm play still flows.
                _jumpCharge = 0f;
                return;
            }

            if (jumpHeld)
            {
                _jumpCharge = Mathf.Min(jumpChargeTime, _jumpCharge + dt);
                return;
            }

            if (_jumpCharge <= 0f)
                return;

            float launch = Mathf.Lerp(minJumpSpeed, maxJumpSpeed,
                jumpChargeTime <= 0f ? 1f : _jumpCharge / jumpChargeTime);
            Vector3 up = Vector3.Slerp(Vector3.up, _groundNormal, jumpFollowsSlope).normalized;
            _velocity += up * launch;
            _jumpCharge = 0f;
            _grounded = false;
            // Ground contact is ignored briefly, otherwise the probe still sees the surface on the
            // launch frame and swallows the jump.
            _launchLockout = 0.12f;
        }

        // ---------------------------------------------------------------- ground contact

        void ProbeGround(out bool grounded, out Vector3 normal)
        {
            grounded = false;
            normal = Vector3.up;

            Vector3 feet = transform.position;
            float probe = groundSnapDistance + 1f;

            // A straight ray reads the true surface orientation; the sphere cast is the fallback for
            // the frames where the ray slips through a seam between terrain and a mesh collider.
            if (Physics.Raycast(feet + Vector3.up * 0.6f, Vector3.down, out RaycastHit hit,
                    0.6f + probe, groundLayers, QueryTriggerInteraction.Ignore)
                && hit.collider.transform != transform)
            {
                normal = hit.normal;
                grounded = feet.y - hit.point.y <= groundSnapDistance;
                if (grounded)
                    return;
            }

            Vector3 origin = feet + Vector3.up * (radius + 0.1f);
            if (Physics.SphereCast(origin, radius * 0.95f, Vector3.down, out RaycastHit sphereHit,
                    radius + 0.1f + groundSnapDistance, groundLayers, QueryTriggerInteraction.Ignore)
                && sphereHit.collider.transform != transform)
            {
                grounded = true;
                normal = sphereHit.normal;
            }
        }

        /// <summary>
        /// Closes the last few centimetres to the surface after a move. Terrain that rolls away
        /// under a fast skier otherwise leaves a hairline gap that reads as chatter.
        /// </summary>
        void SnapToSurface()
        {
            if (!Physics.Raycast(transform.position + Vector3.up * 0.6f, Vector3.down,
                    out RaycastHit hit, 0.6f + groundSnapDistance, groundLayers,
                    QueryTriggerInteraction.Ignore))
                return;

            float gap = transform.position.y - hit.point.y;
            if (gap > 0.02f && gap <= groundSnapDistance)
                _controller.Move(Vector3.down * gap);
        }

        // ---------------------------------------------------------------- presentation

        void UpdateReadouts()
        {
            Vector3 travel = new Vector3(_velocity.x, 0f, _velocity.z);
            if (travel.sqrMagnitude < 0.04f)
            {
                SkidAngle = 0f;
                return;
            }

            float signed = Vector3.SignedAngle(SkiForward, travel.normalized, Vector3.up);
            RidingSwitch = Mathf.Abs(signed) > 90f;
            SkidAngle = RidingSwitch ? 180f - Mathf.Abs(signed) : Mathf.Abs(signed);
        }

        void PoseModel(float steer, bool tucking, float dt)
        {
            if (model == null)
                return;

            // Bank scales with speed as well as steer: a standstill pivot should not throw the body
            // over onto an edge it is not loading.
            float speedFactor = Mathf.Clamp01(Speed / Mathf.Max(1f, fullAuthoritySpeed * 1.5f));
            float targetBank = -steer * maxBankAngle * speedFactor;
            float targetLean = tucking ? maxTuckLean : (_grounded ? 6f : 10f);
            float targetCrouch = Mathf.Max(JumpCharge01, tucking ? 0.5f : 0f);

            _bank = Mathf.SmoothDamp(_bank, targetBank, ref _bankVelocity, poseSmoothing);
            _lean = Mathf.SmoothDamp(_lean, targetLean, ref _leanVelocity, poseSmoothing);
            _crouch = Mathf.SmoothDamp(_crouch, targetCrouch, ref _crouchVelocity, poseSmoothing);

            // Stand on the slope, point the skis, then lean and bank on top. Order matters: doing
            // the lean before the facing tips the body sideways instead of forward.
            Quaternion slope = Quaternion.Slerp(Quaternion.identity,
                Quaternion.FromToRotation(Vector3.up, _groundNormal), slopeAlignment);
            Quaternion facing = Quaternion.Euler(0f, _heading + modelYawOffset, 0f);
            Quaternion tilt = Quaternion.Euler(_lean, 0f, _bank);

            model.rotation = slope * facing * tilt;
            // A charged jump or a deep tuck drops the hips; without it the crouch reads as nothing.
            model.localPosition = new Vector3(0f, -_crouch * 0.28f, 0f);
        }

        // ---------------------------------------------------------------- housekeeping

        public void Respawn() => Teleport(_spawnPoint, _spawnHeading);

        public void Teleport(Vector3 position, float heading)
        {
            _controller.enabled = false;
            transform.position = position;
            _controller.enabled = true;
            _velocity = Vector3.zero;
            _heading = heading;
            _jumpCharge = 0f;
            _landingTimer = 0f;
            _airTime = 0f;
        }

        public void SetSpawn(Vector3 position, float heading)
        {
            _spawnPoint = position;
            _spawnHeading = heading;
        }

        /// <summary>Points the skis down the fall line of whatever they are standing on.</summary>
        public void FaceDownhill()
        {
            ProbeGround(out _, out Vector3 normal);
            Vector3 fall = Vector3.ProjectOnPlane(Vector3.down, normal);
            if (fall.sqrMagnitude < 1e-5f)
                return;
            _heading = Mathf.Atan2(fall.x, fall.z) * Mathf.Rad2Deg;
            _spawnHeading = _heading;
        }

        void HandleCursor()
        {
            Keyboard keyboard = Keyboard.current;
            if (keyboard != null && keyboard.escapeKey.wasPressedThisFrame)
                SetCursorLocked(false);
            if (keyboard != null && keyboard.rKey.wasPressedThisFrame)
                Respawn();

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
            Vector3 basePos = transform.position;
            Gizmos.color = new Color(0.3f, 0.8f, 1f, 0.9f);
            Gizmos.DrawWireSphere(basePos + Vector3.up * radius, radius);
            Gizmos.DrawWireSphere(basePos + Vector3.up * (standingHeight - radius), radius);

            if (!Application.isPlaying)
                return;

            // Ski axis in cyan, actual travel in yellow — the gap between them is the skid.
            Gizmos.color = Color.cyan;
            Gizmos.DrawRay(basePos + Vector3.up * 0.1f, SkiForward * 3f);
            Gizmos.color = Color.yellow;
            Gizmos.DrawRay(basePos + Vector3.up * 0.1f, _velocity * 0.2f);
        }
    }
}
