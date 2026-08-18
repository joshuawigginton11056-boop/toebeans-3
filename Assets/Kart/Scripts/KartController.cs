using UnityEngine;
using UnityEngine.InputSystem;

namespace Toebeans.Karting
{
    public enum KartDrive { RearWheel, AllWheel }

    /// <summary>
    /// Drives the kart on real numbers: masses in kilograms, torques in newton-metres, spring rates
    /// derived from the ride frequency you actually want. Everything the tyres do is scaled by the
    /// surface underneath them, and everything the body does in the air is governed by gravity rather
    /// than by a scripted arc.
    ///
    /// The suspension and tyres are the kart's own raycast model (<see cref="KartWheelPhysics"/>), not
    /// Unity's built-in WheelCollider — see [[unity-wheelcollider-total-velocity-lock]] in project
    /// memory. WheelCollider was found, through more than twenty isolated live tests on 2026-08-08, to
    /// zero a grounded rigidbody's entire velocity every physics step in this Unity version, in every
    /// axis, regardless of applied force; disabling every WheelCollider left the same rigidbody behaving
    /// perfectly under gravity and force. This is the replacement.
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    [DisallowMultipleComponent]
    public class KartController : MonoBehaviour
    {
        [Header("Wiring")]
        [Tooltip("Front left, front right, rear left, rear right — in that order.")]
        public KartWheel[] wheels = new KartWheel[4];
        [Tooltip("Visual wheels, matching the order above.")]
        public Transform[] wheelVisuals = new Transform[4];
        [Tooltip("The steering wheel's pivot. Rotated about its own axis as you steer.")]
        public Transform steeringWheel;
        public InputActionAsset inputActions;
        [Tooltip("What the wheel raycasts are allowed to hit.")]
        public LayerMask groundLayers = ~0;

        [Header("Mass")]
        [Tooltip("Kart without the driver, in kilograms. A racing kart is about 80; this one is a heavier buggy.")]
        public float kerbMass = 130f;
        [Tooltip("Driver plus kit, in kilograms.")]
        public float driverMass = 80f;
        [Tooltip("Centre of mass, in local metres. Low and slightly rearward is what stops it tripping over its nose.")]
        public Vector3 centreOfMass = new Vector3(0f, 0.28f, -0.10f);

        [Header("Drive")]
        public KartDrive drive = KartDrive.AllWheel;
        [Tooltip("Total drive torque at the wheels, in newton-metres, shared between the driven wheels.")]
        public float maxDriveTorque = 620f;
        [Tooltip("Metres per second. Drive torque fades to nothing here, and aero drag holds it.")]
        public float topSpeed = 26f;
        public float reverseTopSpeed = 8f;
        [Range(0.2f, 1f)]
        [Tooltip("Fraction of top speed the engine pulls flat out to before it begins tapering off. " +
                 "High is what keeps the kart feeling strong everywhere a player actually spends " +
                 "their time, rather than only off the line.")]
        public float powerBandEnd = 0.75f;
        [Range(0f, 1.2f)]
        [Tooltip("How much of a slope's pull the engine cancels out for you. 1 climbs a hill as hard " +
                 "as it pulls on the flat; 0 is honest physics, and honest physics is what made this " +
                 "kart die on every incline on the map.")]
        public float gradeAssist = 0.85f;
        [Tooltip("Drivetrain braking past top speed, in newton-metres per (m/s) over it. Stops the " +
                 "kart running away downhill, where the engine has faded out and aero drag alone is " +
                 "not enough to hold it.")]
        public float overspeedBraking = 150f;
        [Tooltip("Total braking torque at the wheels, in newton-metres.")]
        public float maxBrakeTorque = 2400f;
        [Tooltip("Extra torque the handbrake puts through the rear wheels. Deliberately slight. The " +
                 "drift here is a grip mechanic, not a braking one — handbrakeGripLoss is what breaks " +
                 "the back away, and this is only the nudge that starts the rotation. At the 2600 it " +
                 "shipped with it was thirty times the drive torque reaching one wheel, so entering a " +
                 "drift meant throwing an anchor out and watching the engine lose the argument.")]
        public float handbrakeTorque = 420f;
        [Range(0.05f, 1f)]
        [Tooltip("How much sideways grip the rear tyres keep under handbrake. Lower slides more. This " +
                 "is the only door to a slide now that lateralPriority holds the back end in line " +
                 "everywhere else, so it is what the whole drift mechanic is tuned on.")]
        public float handbrakeGripLoss = 0.22f;

        [Header("Steering")]
        [Tooltip("Degrees of lock at a standstill.")]
        public float maxSteerAngle = 28f;
        [Tooltip("Degrees of lock per second — how fast the wheels reach full lock.")]
        public float steerRate = 280f;
        [Range(0.1f, 1f)]
        [Tooltip("Fraction of full lock still available at top speed. Keeps it from spearing off at " +
                 "pace, and keeps the steering proportional: lock far beyond what the tyres can use " +
                 "makes the first fifth of the key travel do everything and the rest do nothing.")]
        public float steerAtTopSpeed = 0.5f;
        [Tooltip("Turn the inside wheel tighter than the outside one, as a real steering rack does.")]
        public bool ackermann = true;
        [Tooltip("How firmly the kart is turned toward the heading its steering geometry implies, in " +
                 "newton-metres per (rad/s) of error per kilogram. This is what makes the kart go " +
                 "where it is pointed the instant the key goes down instead of waiting for the tyres " +
                 "to argue it round. It works both ways — it turns the kart in and it stops the kart " +
                 "rotating past where the driver asked for, which is the other half of never spinning " +
                 "out by accident. Switched off entirely while the handbrake is held, because a slide " +
                 "is precisely the kart NOT following its front wheels. 0 disables it.")]
        public float yawAssist = 2.5f;

        [Header("Suspension")]
        [Tooltip("Total suspension travel, in metres. Long, because this is an offroad kart and travel " +
                 "is what lets it swallow a bump instead of being thrown off it. Changing this needs " +
                 "a prefab rebuild — the wheel anchors are placed from it.")]
        public float suspensionDistance = 0.28f;
        [Tooltip("Ride frequency in hertz. About 1.5 is a road car, 2.5 is stiff and sporty. Soft is " +
                 "the offroad answer: it rides the terrain rather than skittering across it, and it " +
                 "is what makes the kart visibly squat, pitch and lean as it works.")]
        public float rideFrequency = 1.6f;
        [Range(0.1f, 1.5f)]
        [Tooltip("1 is critically damped. Low enough to let the springs move visibly, high enough that " +
                 "it settles instead of pogoing.")]
        public float dampingRatio = 0.38f;
        [Tooltip("Anti-roll bar rate, newtons per unit of travel difference across an axle. Deliberately " +
                 "soft — an anti-roll bar's whole job is to stop the body leaning, and the body leaning " +
                 "is the thing the player is supposed to see when they throw it into a corner.")]
        public float antiRollStiffness = 3000f;

        [Header("Traction")]
        [Tooltip("Eases off a wheel that is already at its grip limit. Without it a wheel that goes " +
                 "light demands its full share of the engine regardless of whether it can use it.")]
        public bool tractionControl = true;
        [Range(0.05f, 2f)]
        [Tooltip("How much of the grip limit can be demanded before the drive is eased off next frame. " +
                 "This is a wheelspin guard, not a power limit — set near the limit so the kart gets " +
                 "to use the grip it has. At 0.35 it was throttling back any time the driver asked " +
                 "for more than a third of it, which reads as the engine going soft for no reason.")]
        public float allowedSlip = 0.9f;
        [Tooltip("Braking torque applied to a wheel that is off the ground, so it does not windmill.")]
        public float airborneWheelDrag = 40f;

        [Header("Engine speed")]
        [Tooltip("Wheel rpm to engine rpm. A kart has no gearbox, so this is a single fixed reduction.")]
        public float finalDriveRatio = 9.7f;
        public float idleEngineRpm = 2400f;
        [Tooltip("The limiter. With KartAudio's four-stroke firing count this puts full song near 67 Hz " +
                 "and idle near 20 — a chest-height rumble rather than the 158 Hz buzz it shipped with.")]
        public float maxEngineRpm = 8000f;
        [Tooltip("Extra engine gearing while reversing. Reverse tops out at under a third of forward " +
                 "speed, so on the same reduction it never lifts off idle and sounds like the kart has " +
                 "given up. Gearing it separately is what a real machine with a reverse gear does, and " +
                 "it is the whole difference between reverse feeling strong and feeling broken.")]
        public float reverseGearing = 3f;
        [Tooltip("How fast the revs climb, and fall. Falling slower than rising is what gives the " +
                 "engine its weight.")]
        public float engineRevUpRate = 7f;
        public float engineRevDownRate = 4f;

        [Header("Grip")]
        [Tooltip("Forward tyre grip coefficient, before the surface multiplier. Roughly how many g's of " +
                 "acceleration the tyre can put down at this load.")]
        public float forwardGrip = 1.6f;
        [Tooltip("Sideways tyre grip coefficient, before the surface multiplier.")]
        public float sidewaysGrip = 2.4f;
        [Range(0f, 1f)]
        [Tooltip("How much of the grip budget cornering gets first call on. 0 is simulator behaviour — " +
                 "throttle mid-corner costs you grip and the back steps out. 1 protects cornering " +
                 "entirely and makes hard acceleration cost drive instead, so the kart only slides " +
                 "when the player asks it to with the handbrake.")]
        public float lateralPriority = 0.85f;
        [Tooltip("How hard the tyre resists sideways slip, in newtons per (m/s) of slip. Higher plants " +
                 "the kart harder into a turn before it starts to slide.")]
        public float lateralStiffness = 9000f;

        [Tooltip("Below this speed, in m/s, a coasting kart's tyres grip the ground hard enough to " +
                 "hold it still on a slope instead of rolling away. This is the static half of tyre " +
                 "friction — without it nothing at all resists gravity at a standstill, because every " +
                 "other force here is proportional to how fast the tyre is already slipping. It is " +
                 "still limited by grip, so a slope steeper than the tyres can hold slides anyway. " +
                 "0 turns it off and the kart freewheels down anything with a gradient.")]
        [Range(0f, 8f)]
        public float holdSpeed = 2f;

        [Tooltip("Speed below which a coasting, grounded kart has its last residual drift bled away, " +
                 "in m/s. The tyres do the real holding; this only mops up the jitter left over from " +
                 "the suspension breathing against a triangulated heightmap. Switched off entirely by " +
                 "any input, and gravity beats it within a fifth of a second on a slope steep enough " +
                 "to genuinely slide down. 0 turns it off.")]
        [Range(0f, 2f)]
        public float settleSpeed = 0.3f;

        [Tooltip("How fast that residual drift is bled away, per second. Eased rather than zeroed, so " +
                 "a real force always wins.")]
        [Range(1f, 60f)]
        public float settleRate = 25f;

        [Header("Gravity and air")]
        [Tooltip("Extra gravity once every wheel has left the ground. 1 is honest physics; a little more " +
                 "stops jumps floating, which is what usually reads as 'wrong weight'.")]
        [Range(1f, 3f)]
        public float airborneGravityMultiplier = 1.5f;
        [Tooltip("Angular damping while airborne. Higher settles the kart down instead of letting it tumble.")]
        public float airAngularDamping = 1.2f;
        [Tooltip("How firmly the kart rights itself in the air. Keep it low or landings feel driven by a magnet.")]
        public float airLevelStrength = 1.6f;
        [Tooltip("Downforce at top speed, in newtons. Holds it down over crests.")]
        public float downforceAtTopSpeed = 220f;
        [Tooltip("Drag coefficient times frontal area, in square metres. About 1.0 for an open buggy.")]
        public float dragArea = 1.0f;

        [Header("Debug")]
        [Range(0f, 1f)]
        [Tooltip("Forces the throttle open regardless of input. Use it to prove the kart drives when " +
                 "you suspect the keyboard is not reaching the game. Leave at 0 to play normally.")]
        public float debugAutoThrottle = 0f;
        [Range(-1f, 1f)]
        [Tooltip("Forces a steering input regardless of the keyboard, the same way debugAutoThrottle " +
                 "forces the throttle. Leave at 0 to play normally.")]
        public float debugAutoSteer = 0f;
        [Tooltip("Logs a line of wheel state to the Console every few physics steps. Snapshots taken " +
                 "after the kart has already stopped explain nothing; this shows what happens during.")]
        public bool debugTrace = false;

        const float AirDensity = 1.225f;
        /// <summary>How fast a wheel's cosmetic spin relaxes onto true rolling speed once grounded.</summary>
        const float SpinRelaxationRate = 18f;
        const float WheelMass = 12f;

        Rigidbody _rigidbody;
        KartInputReader _input;
        KartSurfaceSampler _surfaces;
        KartDimensions _dimensions = KartDimensions.Default;
        KartSuspensionSetup _suspension;
        float _steerAngle;
        float _groundAngularDamping;
        int _groundedWheels;
        Vector3 _spawnPosition;
        Quaternion _spawnRotation;

        // Self-healing rather than a bare field read: Awake is the only place that normally sets
        // _input, but relying on exact Unity lifecycle ordering here bit us once already — an edit-mode
        // prefab build followed by a fast "Enter Play Mode" (domain/scene reload disabled) can leave a
        // scene instance's Awake never invoked, and _input null every frame after. Constructing it here
        // on first read means a missed Awake degrades to raw-device input instead of an NRE that trips
        // the Editor's Error Pause and looks exactly like the kart being stuck.
        public KartInputReader Input => _input ??= new KartInputReader(inputActions);

        /// <summary>Signed speed along the kart's nose, in metres per second.</summary>
        public float ForwardSpeed { get; private set; }

        public float SpeedKph => _rigidbody != null ? _rigidbody.linearVelocity.magnitude * 3.6f : 0f;

        public bool IsAirborne => _groundedWheels == 0;

        public int GroundedWheels => _groundedWheels;

        /// <summary>The surface under whichever wheel is best placed to describe what we are driving on.</summary>
        public KartSurface CurrentSurface { get; private set; } = KartSurface.Default;

        /// <summary>Engine speed in rpm, derived from the driven wheels. Drives the audio.</summary>
        public float EngineRpm { get; private set; }

        /// <summary>Engine speed from idle (0) to the limiter (1).</summary>
        public float EngineLoad => Mathf.InverseLerp(idleEngineRpm, maxEngineRpm, EngineRpm);

        /// <summary>Worst tyre slip ratio across the driven wheels, for audio and traction control.</summary>
        public float DriveSlip { get; private set; }

        // What the driver is actually asking for this frame. Surfaced so the readout can show whether
        // input is arriving at all, which is otherwise indistinguishable from the kart being stuck.
        public float ThrottleInput { get; private set; }
        public float ReverseInput { get; private set; }
        public float SteerInput { get; private set; }
        public bool HandbrakeInput { get; private set; }

        public float TotalMass => kerbMass + driverMass;

        bool _initialized;

        void Awake() => EnsureInitialized();

        /// <summary>
        /// Everything Awake needs to have done before the kart can drive, guarded so it is safe to
        /// call again. Exists because relying on Unity to call Awake exactly once, before FixedUpdate,
        /// bit this project once already: an edit-mode prefab build followed by a fast "Enter Play
        /// Mode" (domain/scene reload disabled) left a scene instance's Awake never invoked, _rigidbody
        /// null, and FixedUpdate silently returning early forever — no exception, no console output,
        /// just a kart that never moves. Calling this from FixedUpdate as well closes that hole instead
        /// of trusting the lifecycle guarantee a second time.
        /// </summary>
        void EnsureInitialized()
        {
            if (_initialized)
                return;

            _rigidbody = GetComponent<Rigidbody>();
            if (_rigidbody == null)
                return; // Not on this GameObject yet — nothing to do until it is.

            _input ??= new KartInputReader(inputActions);
            _surfaces ??= new KartSurfaceSampler(wheels.Length);

            _rigidbody.mass = TotalMass;
            _rigidbody.centerOfMass = centreOfMass;
            // Aero drag is applied explicitly below because it goes with the square of speed, which is
            // what gives a real top speed rather than a linear crawl toward one.
            _rigidbody.linearDamping = 0f;
            _rigidbody.interpolation = RigidbodyInterpolation.Interpolate;
            _rigidbody.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            _groundAngularDamping = _rigidbody.angularDamping;

            _spawnPosition = transform.position;
            _spawnRotation = transform.rotation;

            ApplySuspension();

            _initialized = true;
            _input.Enable();
        }

        void OnEnable() => _input?.Enable();

        void OnDisable() => _input?.Disable();

        void Update()
        {
            if (!_initialized)
                EnsureInitialized();

            if (_input != null && _input.ResetPressedThisFrame)
                Recover();

            UpdateWheelVisuals();
            UpdateSteeringWheel();
        }

        void FixedUpdate()
        {
            if (!_initialized)
                EnsureInitialized();
            if (_rigidbody == null)
                return;

            ForwardSpeed = Vector3.Dot(_rigidbody.linearVelocity, transform.forward);

            float throttle = Mathf.Max(Input.Throttle, debugAutoThrottle);
            float reverse = Input.Reverse;
            bool handbrake = Input.HandbrakeHeld;

            ThrottleInput = throttle;
            ReverseInput = reverse;
            SteerInput = Mathf.Abs(debugAutoSteer) > 0.001f ? debugAutoSteer : Input.Steer;
            HandbrakeInput = handbrake;

            ApplySteering();
            UpdateSuspension();
            ReadSurfaces();
            ApplyDriveAndBrakes(throttle, reverse, handbrake);
            ApplyYawAssist(handbrake);
            ApplyAntiRoll();
            ApplyAerodynamics();
            ApplyAirBehaviour();
            SettleWhenParked();

            if (debugTrace)
                LogTrace();
        }

        /// <summary>
        /// Bleeds away the last millimetres per second of drift once the kart is parked.
        ///
        /// The tyre forces do the real work — with static friction added they cancel gravity almost
        /// exactly, taking this kart from 85 mm/s of creep down to 13. What is left is not a missing
        /// force but jitter: the terrain is a triangulated heightmap, the suspension breathes against
        /// it, and every time the load dips the grip limit dips with it, so the hold is clipped for
        /// part of each oscillation while gravity is not. Averaged out, that ratchets the kart
        /// downhill a fraction of a millimetre at a time. It is a rounding error with a direction.
        ///
        /// So this bleeds residual velocity rather than trying to out-argue the oscillation.
        ///
        /// It is emphatically NOT the velocity lock that made WheelCollider unusable here (see
        /// [[unity-wheelcollider-total-velocity-lock]] in project memory, where a grounded rigidbody
        /// had its entire velocity zeroed every step in every axis regardless of applied force).
        /// Three things keep it honest: it only runs while coasting, so any input at all switches it
        /// off; it only runs below <see cref="settleSpeed"/>, which gravity beats within a fifth of a
        /// second on a slope steep enough to genuinely slide down; and it eases velocity toward zero
        /// at a finite rate instead of assigning it, so a real force always wins.
        /// </summary>
        void SettleWhenParked()
        {
            if (!_coasting || _groundedWheels < 3 || settleSpeed <= 0f)
                return;

            Vector3 velocity = _rigidbody.linearVelocity;
            if (velocity.sqrMagnitude > settleSpeed * settleSpeed)
                return;

            // Only across the ground, never into or out of it. Friction acts in the contact plane and
            // nowhere else, and the distinction is not academic: damping the vertical component too
            // takes away the downward motion the chassis needs to settle onto its springs. Measured,
            // that left the kart hanging near full droop with two wheels kissing the ground and 198 N
            // of a 2502 N kart on its tyres — and grip is proportional to load, so a kart barely
            // touching the ground has barely any grip and slides exactly as if it had no rubber on it
            // at all. Damping the wrong axis reproduced the original complaint almost perfectly.
            Vector3 up = AverageContactNormal();
            Vector3 into = Vector3.Dot(velocity, up) * up;
            Vector3 across = velocity - into;

            float keep = Mathf.Exp(-settleRate * Time.fixedDeltaTime);
            _rigidbody.linearVelocity = into + across * keep;
        }

        /// <summary>Mean of the grounded wheels' contact normals — which way is "up" to the tyres.</summary>
        Vector3 AverageContactNormal()
        {
            Vector3 sum = Vector3.zero;
            for (int i = 0; i < wheels.Length; i++)
            {
                KartWheel wheel = wheels[i];
                if (wheel != null && wheel.grounded) sum += wheel.contactNormal;
            }
            return sum.sqrMagnitude > 1e-6f ? sum.normalized : transform.up;
        }

        bool _coasting;
        int _traceStep;

        void LogTrace()
        {
            // Every eighth step: often enough to see a launch, sparse enough to read.
            if (++_traceStep % 8 != 0)
                return;

            var line = new System.Text.StringBuilder();
            line.Append($"[KartTrace] t={Time.fixedTime:0.00} spd={ForwardSpeed:0.00}m/s " +
                        $"thr={ThrottleInput:0.00} steer={_steerAngle:0.0}deg ");

            for (int i = 0; i < wheels.Length; i++)
            {
                KartWheel wheel = wheels[i];
                if (wheel == null)
                    continue;

                line.Append($"| {(KartCorner)i} rpm={WheelRpm(wheel):0} ");
                line.Append(wheel.grounded
                    ? $"N={wheel.load:0} comp={wheel.compression:0.00} "
                    : "AIR ");
            }

            Debug.Log(line.ToString());
        }

        // ------------------------------------------------------------------ suspension

        /// <summary>
        /// Turns the ride frequency you asked for into the spring and damper rates that produce it.
        /// A spring is only meaningful next to the mass it carries, so deriving it from mass is what
        /// keeps the kart sitting right after you change how heavy it is.
        /// </summary>
        public void ApplySuspension()
        {
            int count = 0;
            foreach (KartWheel wheel in wheels)
                if (wheel != null) count++;
            if (count == 0)
                return;

            _suspension = KartSuspension.Solve(TotalMass / count, rideFrequency, dampingRatio, Physics.gravity.y);
        }

        /// <summary>
        /// Casts a ray for every wheel and resolves its suspension force. Done as one pass, ahead of
        /// everything else, so the rest of FixedUpdate reads cached per-wheel state instead of each
        /// raycasting the ground again on its own account.
        /// </summary>
        void UpdateSuspension()
        {
            _groundedWheels = 0;

            for (int i = 0; i < wheels.Length; i++)
            {
                KartWheel wheel = wheels[i];
                if (wheel == null || wheel.anchor == null)
                    continue;

                var corner = (KartCorner)i;
                float radius = _dimensions.Radius(corner);
                Vector3 origin = wheel.anchor.position;
                Vector3 up = transform.up;

                bool hit = Physics.Raycast(origin, -up, out RaycastHit rh, suspensionDistance + radius,
                    groundLayers, QueryTriggerInteraction.Ignore);

                float axisVelocity = Vector3.Dot(_rigidbody.GetPointVelocity(origin), up);

                KartWheelPhysics.SuspensionSample sample = KartWheelPhysics.SolveSuspension(
                    up, suspensionDistance, radius,
                    hit, hit ? rh.point : default, hit ? rh.normal : Vector3.up, hit ? rh.distance : float.MaxValue,
                    _suspension.spring, _suspension.damper, axisVelocity);

                wheel.grounded = sample.grounded;
                wheel.contactPoint = sample.contactPoint;
                wheel.contactNormal = sample.contactNormal;
                wheel.contactCollider = hit ? rh.collider : null;
                wheel.compression = sample.compression;
                wheel.load = sample.load;

                if (sample.grounded)
                {
                    _groundedWheels++;
                    _rigidbody.AddForceAtPosition(sample.force, sample.contactPoint);
                }
            }
        }

        // ------------------------------------------------------------------ per-frame physics

        void ReadSurfaces()
        {
            float bestLoad = 0f;
            KartSurface best = CurrentSurface;
            bool any = false;

            for (int i = 0; i < wheels.Length; i++)
            {
                KartWheel wheel = wheels[i];
                if (wheel == null || !wheel.grounded)
                    continue;

                KartSurface surface = _surfaces.Sample(i, wheel.contactCollider, wheel.contactPoint, Time.time);

                // Report the surface carrying the most weight: on a cambered edge that is the one
                // actually deciding whether the kart grips.
                if (wheel.load >= bestLoad)
                {
                    bestLoad = wheel.load;
                    best = surface;
                }
                any = true;
            }

            if (any)
                CurrentSurface = best;
        }

        void ApplySteering()
        {
            float speedFactor = Mathf.Lerp(
                1f, steerAtTopSpeed, Mathf.Clamp01(Mathf.Abs(ForwardSpeed) / Mathf.Max(topSpeed, 0.01f)));
            // SteerInput is resolved once in FixedUpdate, ahead of this call, so the debug override and
            // the real reading are not decided twice.
            float target = SteerInput * maxSteerAngle * speedFactor;

            _steerAngle = Mathf.MoveTowards(_steerAngle, target, steerRate * Time.fixedDeltaTime);

            float inner = _steerAngle;
            float outer = _steerAngle;

            if (ackermann && Mathf.Abs(_steerAngle) > 0.01f)
            {
                // Both front wheels have to turn about the same centre, so the inner one needs more
                // lock than the outer. Without this the tyres scrub against each other in slow corners.
                float wheelbase = _dimensions.Wheelbase;
                float halfTrack = _dimensions.frontTrack * 0.5f;
                float radius = wheelbase / Mathf.Tan(Mathf.Abs(_steerAngle) * Mathf.Deg2Rad);
                float sign = Mathf.Sign(_steerAngle);

                inner = sign * Mathf.Atan(wheelbase / Mathf.Max(radius - halfTrack, 0.05f)) * Mathf.Rad2Deg;
                outer = sign * Mathf.Atan(wheelbase / (radius + halfTrack)) * Mathf.Rad2Deg;
            }

            bool turningRight = _steerAngle > 0f;
            SetSteer(KartCorner.FrontLeft, turningRight ? outer : inner);
            SetSteer(KartCorner.FrontRight, turningRight ? inner : outer);
        }

        void SetSteer(KartCorner corner, float angle)
        {
            KartWheel wheel = Wheel(corner);
            if (wheel != null)
                wheel.steerAngle = angle;
        }

        void ApplyDriveAndBrakes(float throttle, float reverse, bool handbrake)
        {
            bool rollingForward = ForwardSpeed > 0.5f;
            bool rollingBackward = ForwardSpeed < -0.5f;

            float drivePedal = 0f;
            float brakePedal = 0f;

            if (throttle > 0.01f)
            {
                // Pressing forward while rolling backwards is braking, not a gearchange.
                if (rollingBackward) brakePedal = throttle;
                else drivePedal = throttle;
            }

            if (reverse > 0.01f)
            {
                if (rollingForward) brakePedal = Mathf.Max(brakePedal, reverse);
                else drivePedal = -reverse;
            }

            float limit = drivePedal >= 0f ? topSpeed : reverseTopSpeed;
            float speedFraction = Mathf.Abs(ForwardSpeed) / Mathf.Max(limit, 0.01f);

            // Full torque right through the power band, easing away only over the last stretch of it.
            //
            // This was a squared fade across the whole range, and squaring it cost the kart three
            // quarters of its engine by half speed — 130 Nm of 520 at 13 m/s, about 1.8 m/s² of
            // thrust, against the 1.7 m/s² a ten degree slope takes straight back off it. That single
            // line is why the kart puttered on the flat, crawled up anything and had nothing left for
            // the lip of a bridge. Holding the band flat is an arcade choice and a deliberate one:
            // this is an offroad derby, and the engine has to feel like it is holding something in
            // reserve everywhere a player actually drives, not only from a standstill.
            float taper = Mathf.Clamp01(
                (speedFraction - powerBandEnd) / Mathf.Max(1f - powerBandEnd, 0.01f));
            float fade = 1f - taper * taper;

            // Gravity's pull along the way the driver is asking to go, handed back to them. A climb
            // still reads as a climb — the suspension squats, the nose comes up, momentum carried into
            // it still matters — but it no longer quietly eats the whole engine on the way up.
            // Resolved against the drive direction rather than the nose, so reversing up a slope gets
            // the same help going backwards that it would going forwards.
            float climbTorque = 0f;
            if (gradeAssist > 0f && Mathf.Abs(drivePedal) > 0.01f)
            {
                Vector3 driveDirection = transform.forward * Mathf.Sign(drivePedal);
                float slopeAcceleration = -Vector3.Dot(Physics.gravity, driveDirection);
                if (slopeAcceleration > 0f)
                    climbTorque = gradeAssist * slopeAcceleration * _rigidbody.mass
                                  * _dimensions.rearWheelRadius;
            }

            float totalTorque = (maxDriveTorque * fade + climbTorque)
                                * drivePedal * CurrentSurface.driveEfficiency;
            float brakeTorquePerWheel = maxBrakeTorque / 4f * brakePedal;

            // Downhill the engine has faded out and only aero drag opposes gravity — about 1.6 m/s² at
            // full tilt against the 2.5 a fifteen degree descent hands out, so the kart simply ran
            // away. This is the drivetrain holding it back the way a real one does once the wheels are
            // driving the engine instead of the other way round, and it applies whether or not the
            // driver is on the throttle. It opposes travel through the same rolling sign as the
            // brakes, so it can never shove a stationary kart anywhere.
            float overspeed = Mathf.Abs(ForwardSpeed) - limit;
            if (overspeed > 0f)
                brakeTorquePerWheel += overspeed * overspeedBraking / 4f;

            // Share the drive out by how much weight each wheel is carrying, the way a limited-slip
            // differential does. Splitting it evenly instead sends a full quarter of the engine to a
            // wheel that has gone light over a crest, and it spins uselessly instead of driving.
            float totalLoad = 0f;
            for (int i = 0; i < wheels.Length; i++)
            {
                KartWheel wheel = wheels[i];
                if (wheel == null || !wheel.grounded || !IsDriven((KartCorner)i))
                    continue;
                totalLoad += wheel.load;
            }

            DriveSlip = 0f;

            // Every grounded wheel, driven or not, carries part of the kart and therefore part of the
            // job of holding it on a slope — so this is a separate sum from the driven-wheel load
            // above, which exists to split engine torque.
            float groundedLoad = 0f;
            for (int i = 0; i < wheels.Length; i++)
            {
                KartWheel wheel = wheels[i];
                if (wheel != null && wheel.grounded) groundedLoad += wheel.load;
            }

            // Whether the driver is asking for anything at all. Recorded because the static hold and
            // the settle both have to stand aside the instant an input arrives.
            _coasting = drivePedal == 0f && brakePedal == 0f && !handbrake;

            // The whole weight of the kart, as a force. Each tyre answers for its share by load, so
            // the four shares sum to exactly the kart's mass however the weight is distributed.
            //
            // Deliberately not load / gravity: the normal load is only m·g·cos(slope), so shares
            // sized that way between them account for m·cos(slope) and fall short by precisely the
            // factor that bites hardest on the steep slopes where holding on matters most. On a 46
            // degree face that is a 30% shortfall — enough to slide on ground the tyres could
            // otherwise hold.
            Vector3 weight = Physics.gravity * _rigidbody.mass;

            for (int i = 0; i < wheels.Length; i++)
            {
                KartWheel wheel = wheels[i];
                if (wheel == null)
                    continue;

                var corner = (KartCorner)i;
                float radius = _dimensions.Radius(corner);
                bool driven = IsDriven(corner);
                bool rear = !_dimensions.IsFront(corner);
                float wheelInertia = KartWheelPhysics.WheelInertia(WheelMass, radius);

                float motorTorque = 0f;
                if (driven && wheel.grounded && totalLoad > 1f)
                {
                    motorTorque = totalTorque * (wheel.load / totalLoad);

                    // Eases off this wheel's OWN demand once it ended up pushing against its grip limit
                    // last frame, rather than trying to solve the clamp and the demand simultaneously.
                    // Reading each wheel's own slipRatio (not a running frame-wide value) is what keeps
                    // one wheel's wheelspin from wrongly throttling back a wheel that never slipped.
                    //
                    // Stood down entirely under handbrake, and this was the other half of the engine
                    // dying mid-drift. slipRatio is the combined demand, lateral included, so a
                    // deliberate slide reads as enormous slip and traction control dutifully shut the
                    // engine off for it — correcting the exact thing the driver just asked for. There
                    // is nothing to protect here: a sliding tyre is the mechanic, not a fault.
                    if (tractionControl && !handbrake && wheel.slipRatio > allowedSlip)
                        motorTorque *= Mathf.Clamp01(allowedSlip / wheel.slipRatio);
                }

                float brakeTorque = brakeTorquePerWheel;
                if (handbrake && rear)
                {
                    // Faded out by whatever the driver is asking of the engine. On the throttle
                    // through a drift there is no handbrake braking left at all — only the grip loss,
                    // which is the part that actually makes the kart rotate. Holding both should feel
                    // like steering with the back end, not like fighting the brakes for the corner.
                    brakeTorque += handbrakeTorque * 0.5f
                                   * (1f - Mathf.Clamp01(Mathf.Abs(drivePedal)));
                }

                if (!wheel.grounded)
                {
                    // Stale slip from before it left the ground must not throttle it back the instant
                    // it lands — there is nothing to have slipped against while airborne.
                    wheel.slipRatio = 0f;
                    wheel.angularVelocity = KartWheelPhysics.IntegrateWheelSpin(
                        wheel.angularVelocity, motorTorque, airborneWheelDrag, 0f, false,
                        wheelInertia, SpinRelaxationRate, Time.fixedDeltaTime);
                    continue;
                }

                // Rolling resistance, as a real fraction of the load the tyre is carrying. This is what
                // makes snow feel like it is dragging at you and rock feel like it is not.
                KartSurface surface = _surfaces.Cached(i);
                brakeTorque += surface.rollingResistance * wheel.load * radius;

                Quaternion steerRotation = Quaternion.AngleAxis(wheel.steerAngle, transform.up);
                Vector3 wheelForward = steerRotation * transform.forward;
                Vector3 wheelRight = steerRotation * transform.right;

                Vector3 contactVelocity = _rigidbody.GetPointVelocity(wheel.contactPoint);
                float forwardVel = Vector3.Dot(contactVelocity, wheelForward);
                float lateralVel = Vector3.Dot(contactVelocity, wheelRight);

                // Brakes and rolling resistance oppose whichever way the wheel is already travelling.
                // Mathf.Sign(0) confusingly returns +1 in Unity, so guard the near-stationary case
                // explicitly rather than applying a phantom brake force at a dead stop.
                float rollingSign = forwardVel > 0.05f ? 1f : forwardVel < -0.05f ? -1f : 0f;
                float demandedForwardForce = motorTorque / radius - rollingSign * (brakeTorque / radius);

                // Static friction — the half of grip that acts without any slip at all. Everything
                // above is proportional to slip that has already happened, so at a standstill the
                // tyre asks for nothing and gravity simply wins. This kart would creep off an 8
                // degree slope and never stop. See KartWheelPhysics.SolveHoldingForce.
                float loadShare = groundedLoad > 1f ? wheel.load / groundedLoad : 0.25f;
                float massShare = _rigidbody.mass * loadShare;

                // The part of this tyre's share of the kart's weight that is actually trying to drag
                // it along the ground — resolved in the CONTACT plane, using the ground's own normal.
                //
                // Resolving it along the chassis axes instead looks equivalent and is not. The
                // chassis hangs on springs and does not sit parallel to the ground: measured on this
                // terrain it was tilted 6.5 degrees away from the surface normal while the ground
                // under the wheels was nearly flat. Decomposed against the chassis that produced a
                // 279 N "downhill" pull where the real one was 76 N — the hold shoved the kart
                // sideways across level ground, in a direction gravity was not pulling. Whatever is
                // perpendicular to the ground is the suspension's job, and only what is parallel to
                // it belongs to the tyre.
                Vector3 pull = weight * loadShare;
                pull -= Vector3.Dot(pull, wheel.contactNormal) * wheel.contactNormal;

                // Sideways needs gravity's share across the kart opposed. On the flat this is exactly
                // zero, which is what keeps cornering untouched.
                float lateralHold = -Vector3.Dot(pull, wheelRight);

                // ...and the slip term it is added to has to be a stiffness the timestep can actually
                // integrate, or the tyre rings instead of settling and the kart shuffles sideways for
                // ever. See KartWheelPhysics.StableLateralStiffness.
                float stableStiffness = KartWheelPhysics.StableLateralStiffness(
                    lateralStiffness, massShare, Time.fixedDeltaTime);

                // Along the kart's length there is no such term at all when coasting, so the hold has
                // to supply both halves. It is confined to walking pace, because a tyre that fought
                // rolling at any speed would be a permanently applied handbrake, and it stands aside
                // the instant the driver asks for drive or brake so it can never fight an input.
                float holdBlend = _coasting ? KartWheelPhysics.HoldBlend(ForwardSpeed, holdSpeed) : 0f;
                float forwardHold = holdBlend <= 0f ? 0f : holdBlend * KartWheelPhysics.SolveHoldingForce(
                    forwardVel, Vector3.Dot(pull, wheelForward), massShare, Time.fixedDeltaTime);

                // Cornering keeps its priority claim on the grip everywhere except a rear wheel under
                // handbrake, which is the one place the kart is supposed to let go — protecting the
                // back end there would make the drift button do nothing at all.
                float priority = handbrake && rear ? 0f : lateralPriority;

                KartWheelPhysics.TyreForceResult tyre = KartWheelPhysics.SolveTyreForce(
                    demandedForwardForce + forwardHold, lateralVel, stableStiffness, wheel.load,
                    forwardGrip * surface.forwardGrip,
                    sidewaysGrip * surface.sidewaysGrip * (handbrake && rear ? handbrakeGripLoss : 1f),
                    lateralHold, priority);

                // A tyre holding the kart still is not slipping, whatever the demand ratio says. Left
                // raw, a kart parked on a hill would read as wheelspin on the HUD and in the engine
                // note, and the stale value would throttle back the first frame of drive.
                wheel.slipRatio = tyre.slipRatio * (1f - holdBlend);
                if (wheel.slipRatio > DriveSlip)
                    DriveSlip = wheel.slipRatio;

                Vector3 totalForce = wheelForward * tyre.forwardForce + wheelRight * tyre.lateralForce;
                _rigidbody.AddForceAtPosition(totalForce, wheel.contactPoint);

                float groundAngular = forwardVel / Mathf.Max(radius, 0.01f);
                wheel.angularVelocity = KartWheelPhysics.IntegrateWheelSpin(
                    wheel.angularVelocity, motorTorque, brakeTorque, groundAngular, true,
                    wheelInertia, SpinRelaxationRate, Time.fixedDeltaTime);
            }

            UpdateEngineSpeed(drivePedal);
        }

        /// <summary>
        /// Turns the kart toward the heading its own steering geometry implies, and holds it there.
        ///
        /// Tyres alone get there eventually, but "eventually" is the complaint: the kart rotates when
        /// the rubber has finished arguing about it, which reads as vague on the way in and as a spin
        /// on the way out. This closes the loop directly on yaw rate instead. The target is the honest
        /// one — v·tan(steer)/wheelbase is the rate a vehicle on that lock is geometrically going
        /// round at — so it is not inventing a heading, only arriving at the real one immediately.
        ///
        /// Being an error term it corrects in both directions, which is what makes it an anti-spin
        /// device as much as a steering one: rotating faster than the front wheels asked for is the
        /// definition of the back coming round, and this pulls it straight back.
        ///
        /// Three things stop it becoming an autopilot. It does nothing airborne, where there is no
        /// contact patch to justify a yaw force. It does nothing under handbrake, so a deliberate
        /// drift is never fought. And it does nothing below walking pace, where tan(steer) implies
        /// large rates from tiny speeds and a parked kart would spin on the spot.
        /// </summary>
        void ApplyYawAssist(bool handbrake)
        {
            if (yawAssist <= 0f || handbrake || _groundedWheels == 0)
                return;

            float speed = ForwardSpeed;
            if (Mathf.Abs(speed) < 1.5f)
                return;

            float geometric = speed * Mathf.Tan(_steerAngle * Mathf.Deg2Rad)
                              / Mathf.Max(_dimensions.Wheelbase, 0.01f);

            // Clamped to the rate the tyres could actually hold at this speed, and this is the whole
            // reason the kart still span at pace.
            //
            // Turn radius from steering geometry does not care how fast you are going, but the
            // cornering it implies very much does: a corner of radius r needs v²/r of lateral
            // acceleration and turns the kart at v/r, so the hardest a kart can be turned without
            // simply sliding off the line is a_lat/v. At full lock at top speed the geometry asks for
            // 5.6 rad/s and the tyres can hold 0.9 — six times over. The assist was faithfully
            // rotating the chassis to a heading the velocity could not follow, which is not a
            // steering input, it is a spin, and the assist was the thing causing it.
            //
            // Reading the surface here too, so ice asks for less of the kart than rock does.
            float gripLimitedRate = sidewaysGrip * CurrentSurface.sidewaysGrip
                                    * Physics.gravity.magnitude / Mathf.Max(Mathf.Abs(speed), 0.01f);
            float desired = Mathf.Clamp(geometric, -gripLimitedRate, gripLimitedRate);

            float actual = Vector3.Dot(_rigidbody.angularVelocity, transform.up);

            // Scaled by mass so the feel survives a change of kerb weight, and faded in with how many
            // wheels are actually down — a kart on two wheels over a crest has no business being
            // steered by anything but gravity.
            float authority = yawAssist * _rigidbody.mass * (_groundedWheels / 4f);
            _rigidbody.AddTorque(transform.up * ((desired - actual) * authority), ForceMode.Force);
        }

        bool IsDriven(KartCorner corner) =>
            drive == KartDrive.AllWheel || !_dimensions.IsFront(corner);

        static float WheelRpm(KartWheel wheel) => wheel.angularVelocity * 60f / (2f * Mathf.PI);

        /// <summary>
        /// A kart has no gearbox, so engine speed is just wheel speed through a fixed reduction. Idle
        /// sets the floor, and wheelspin genuinely flares the revs — which is exactly what you hear
        /// when a real one breaks traction.
        /// </summary>
        void UpdateEngineSpeed(float drivePedal)
        {
            float wheelRpm = 0f;
            int counted = 0;

            for (int i = 0; i < wheels.Length; i++)
            {
                KartWheel wheel = wheels[i];
                if (wheel == null || !IsDriven((KartCorner)i))
                    continue;
                wheelRpm += Mathf.Abs(WheelRpm(wheel));
                counted++;
            }

            if (counted > 0)
                wheelRpm /= counted;

            // Reverse is geared up so it revs out across its own much shorter speed range instead of
            // sitting just off idle all the way to its limit, which is what made it sound broken.
            float ratio = finalDriveRatio;
            if (drivePedal < 0f || ForwardSpeed < -0.5f)
                ratio *= Mathf.Max(reverseGearing, 1f);

            float fromWheels = wheelRpm * ratio;
            float target = Mathf.Clamp(fromWheels, idleEngineRpm, maxEngineRpm);

            // Blipping the throttle at a standstill should still rev it, or the kart sounds dead on the
            // line. Held back below what the wheels ask for, so it never drowns out real acceleration.
            if (Mathf.Abs(ForwardSpeed) < 2f)
            {
                float blip = idleEngineRpm
                             + Mathf.Abs(drivePedal) * (maxEngineRpm - idleEngineRpm) * 0.45f;
                target = Mathf.Max(target, blip);
            }

            float rate = target > EngineRpm ? engineRevUpRate : engineRevDownRate;
            EngineRpm = Mathf.Lerp(EngineRpm, target, 1f - Mathf.Exp(-rate * Time.fixedDeltaTime));
        }

        void ApplyAntiRoll()
        {
            ApplyAntiRollAxle(KartCorner.FrontLeft, KartCorner.FrontRight);
            ApplyAntiRollAxle(KartCorner.RearLeft, KartCorner.RearRight);
        }

        /// <summary>
        /// Transfers load across an axle in proportion to how unevenly the suspension is compressed.
        /// Without it a light, tall vehicle rolls onto two wheels in the first quick direction change.
        /// </summary>
        void ApplyAntiRollAxle(KartCorner leftCorner, KartCorner rightCorner)
        {
            KartWheel left = Wheel(leftCorner);
            KartWheel right = Wheel(rightCorner);
            if (left == null || right == null)
                return;

            // More compressed means more heavily loaded. The bar pushes load from the heavily loaded
            // side onto the lightly loaded one, which is what resists the body rolling.
            float force = (right.compression - left.compression) * (antiRollStiffness / Mathf.Max(suspensionDistance, 0.001f));

            if (left.grounded)
                _rigidbody.AddForceAtPosition(transform.up * -force, left.contactPoint);
            if (right.grounded)
                _rigidbody.AddForceAtPosition(transform.up * force, right.contactPoint);
        }

        void ApplyAerodynamics()
        {
            Vector3 velocity = _rigidbody.linearVelocity;
            float speed = velocity.magnitude;
            if (speed < 0.1f)
                return;

            // Real quadratic drag: F = ½ρ·Cd·A·v². This, not a linear damping value, is what makes the
            // kart accelerate hard at low speed and grind toward a genuine top speed.
            float dragForce = 0.5f * AirDensity * dragArea * speed * speed;
            _rigidbody.AddForce(-velocity.normalized * dragForce);

            float speedFraction = Mathf.Clamp01(speed / Mathf.Max(topSpeed, 0.01f));
            _rigidbody.AddForce(-transform.up * (downforceAtTopSpeed * speedFraction * speedFraction));
        }

        void ApplyAirBehaviour()
        {
            if (!IsAirborne)
            {
                _rigidbody.angularDamping = _groundAngularDamping;
                return;
            }

            _rigidbody.angularDamping = airAngularDamping;

            // Gravity is already pulling; this tops it up. Applied as an acceleration so it is
            // independent of how heavy the kart is, exactly as gravity itself is.
            if (airborneGravityMultiplier > 1f)
                _rigidbody.AddForce(Physics.gravity * (airborneGravityMultiplier - 1f), ForceMode.Acceleration);

            // A gentle nudge back toward level. Enough to stop a small jump ending on the roof,
            // far too weak to hold the kart up if it genuinely deserves to land upside down.
            if (airLevelStrength > 0f)
            {
                Vector3 correction = Vector3.Cross(transform.up, Vector3.up);
                _rigidbody.AddTorque(correction * airLevelStrength, ForceMode.Acceleration);
            }
        }

        // ------------------------------------------------------------------ visuals

        void UpdateWheelVisuals()
        {
            for (int i = 0; i < wheelVisuals.Length && i < wheels.Length; i++)
            {
                KartWheel wheel = wheels[i];
                if (wheel == null || wheel.anchor == null || wheelVisuals[i] == null)
                    continue;

                float radius = _dimensions.Radius((KartCorner)i);

                // Hub height from full droop (compression 0) up to the anchor itself (compression at
                // its maximum), matching how UpdateSuspension measured compression from the same anchor.
                Vector3 hub = wheel.anchor.position
                              - transform.up * (suspensionDistance - wheel.compression);

                wheel.spinAngleDegrees += wheel.angularVelocity * Mathf.Rad2Deg * Time.deltaTime;
                wheel.spinAngleDegrees %= 360f;

                Quaternion yaw = transform.rotation * Quaternion.AngleAxis(wheel.steerAngle, Vector3.up);
                // Cylinders in the wheel model run along local X — see KartBlueprint.BuildWheel — so
                // rolling spin turns about the wheel's own right/axle axis.
                Quaternion spin = Quaternion.AngleAxis(wheel.spinAngleDegrees, Vector3.right);

                wheelVisuals[i].SetPositionAndRotation(hub, yaw * spin);
            }
        }

        void UpdateSteeringWheel()
        {
            if (steeringWheel == null)
                return;

            // Several turns of the wheel for the lock the front wheels actually take, as a real rack is
            // geared. Rotating about local Y spins it in its own plane whatever angle it is mounted at.
            float wheelRotation = -_steerAngle / Mathf.Max(maxSteerAngle, 0.01f) * 120f;
            steeringWheel.localRotation = Quaternion.Euler(0f, wheelRotation, 0f);
        }

        // ------------------------------------------------------------------ recovery

        /// <summary>Sets the kart back down, upright and stationary, where it last was.</summary>
        public void Recover()
        {
            Vector3 position = transform.position + Vector3.up * 0.5f;
            Vector3 forward = transform.forward;
            forward.y = 0f;
            if (forward.sqrMagnitude < 0.001f)
                forward = Vector3.forward;

            if (Physics.Raycast(position + Vector3.up * 20f, Vector3.down, out RaycastHit hit, 200f,
                    ~0, QueryTriggerInteraction.Ignore))
            {
                position = hit.point + Vector3.up * 0.6f;
            }

            _rigidbody.position = position;
            _rigidbody.rotation = Quaternion.LookRotation(forward.normalized, Vector3.up);
            _rigidbody.linearVelocity = Vector3.zero;
            _rigidbody.angularVelocity = Vector3.zero;
            transform.SetPositionAndRotation(_rigidbody.position, _rigidbody.rotation);
        }

        /// <summary>Back to where the kart was placed in the scene.</summary>
        public void ReturnToSpawn()
        {
            _rigidbody.linearVelocity = Vector3.zero;
            _rigidbody.angularVelocity = Vector3.zero;
            _rigidbody.position = _spawnPosition;
            _rigidbody.rotation = _spawnRotation;
            transform.SetPositionAndRotation(_spawnPosition, _spawnRotation);
        }

        KartWheel Wheel(KartCorner corner)
        {
            int index = (int)corner;
            return index < wheels.Length ? wheels[index] : null;
        }

        void OnValidate()
        {
            if (!Application.isPlaying || _rigidbody == null)
                return;

            _rigidbody.mass = TotalMass;
            _rigidbody.centerOfMass = centreOfMass;
            ApplySuspension();
        }
    }
}
