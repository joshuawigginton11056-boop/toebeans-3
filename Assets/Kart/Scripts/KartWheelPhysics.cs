using UnityEngine;

namespace Toebeans.Karting
{
    /// <summary>
    /// One corner's wiring and the state carried between physics steps. <see cref="anchor"/> is the
    /// only field the setup tool needs to fill in — it is a fixed chassis-mounted point, positioned at
    /// <c>KartDimensions.WheelCentre(corner) + Vector3.up * suspensionDistance</c> (local space), which
    /// is where the ray for this wheel starts. Everything else is runtime-only and rebuilt every step.
    /// </summary>
    [System.Serializable]
    public class KartWheel
    {
        public Transform anchor;

        [System.NonSerialized] public bool grounded;
        [System.NonSerialized] public Vector3 contactPoint;
        [System.NonSerialized] public Vector3 contactNormal;
        [System.NonSerialized] public Collider contactCollider;
        [System.NonSerialized] public float compression;
        [System.NonSerialized] public float load;
        [System.NonSerialized] public float steerAngle;
        [System.NonSerialized] public float angularVelocity;
        [System.NonSerialized] public float spinAngleDegrees;
        /// <summary>This wheel's own slip ratio from the frame before, for per-wheel traction control.</summary>
        [System.NonSerialized] public float slipRatio;
    }

    /// <summary>
    /// The kart's own suspension and tyre model, replacing Unity's built-in WheelCollider. Not a
    /// tuning choice — see [[unity-wheelcollider-total-velocity-lock]] in project memory: WheelCollider
    /// was found, through more than twenty isolated live tests, to zero a grounded rigidbody's entire
    /// velocity every physics step in this Unity version (6000.5.5f1), in every axis, regardless of
    /// applied force. With every WheelCollider disabled the same rigidbody behaved perfectly (free-fell
    /// under gravity, launched normally from a force), which is what isolated the fault to the component
    /// itself rather than to anything in this project's tuning.
    ///
    /// Everything here is pure: no scene objects, no raycasting, no Unity native calls beyond
    /// Vector3/Mathf, so it runs and can be asserted on outside the Editor — see
    /// [[unity-headless-csharp-verification]]. The raycasting and force application live in
    /// KartController, which is the only place that needs the live scene.
    /// </summary>
    public static class KartWheelPhysics
    {
        /// <summary>Suspension result for one wheel this step.</summary>
        public struct SuspensionSample
        {
            public bool grounded;
            public Vector3 contactPoint;
            public Vector3 contactNormal;
            /// <summary>Metres, 0 at full droop to suspensionDistance fully compressed.</summary>
            public float compression;
            /// <summary>Newtons. Always >= 0 — a spring can only push the chassis away from the ground.</summary>
            public float load;
            /// <summary>load * suspensionAxis, in world space.</summary>
            public Vector3 force;
        }

        /// <summary>
        /// Spring-damper force from a raycast-style ground hit. The ray is understood to run from
        /// <paramref name="anchor"/> along <c>-suspensionAxis</c> for <c>suspensionDistance + radius</c> —
        /// the caller does the actual raycast and passes in what it found.
        /// </summary>
        public static SuspensionSample SolveSuspension(
            Vector3 suspensionAxis, float suspensionDistance, float radius,
            bool hit, Vector3 hitPoint, Vector3 hitNormal, float hitDistance,
            float spring, float damper, float suspensionAxisVelocity)
        {
            float maxRayDistance = suspensionDistance + radius;

            if (!hit || hitDistance > maxRayDistance)
                return new SuspensionSample { grounded = false };

            float compression = Mathf.Clamp(maxRayDistance - hitDistance, 0f, suspensionDistance);

            // F = kx - c·v. The damper resists motion along the suspension axis in either direction —
            // real dampers fight rebound as well as compression, which is what stops a stiff spring
            // making the kart pogo after every bump.
            float forceMagnitude = Mathf.Max(0f, spring * compression - damper * suspensionAxisVelocity);

            return new SuspensionSample
            {
                grounded = true,
                contactPoint = hitPoint,
                contactNormal = hitNormal,
                compression = compression,
                load = forceMagnitude,
                force = suspensionAxis * forceMagnitude,
            };
        }

        /// <summary>Forward and lateral tyre force for one wheel this step.</summary>
        public struct TyreForceResult
        {
            /// <summary>Newtons along the wheel's steered forward axis.</summary>
            public float forwardForce;
            /// <summary>Newtons along the wheel's steered right axis.</summary>
            public float lateralForce;
            /// <summary>
            /// How hard the demanded force pushed against the grip limit: 0 is well within grip,
            /// 1 is exactly at the limit, above 1 is how much demand was clamped away. Doubles as the
            /// wheelspin/lock-up indicator for the HUD and for the audio's engine-load response.
            /// </summary>
            public float slipRatio;
        }

        /// <summary>
        /// Converts a demanded forward force (drive plus brake plus rolling resistance, already
        /// combined and signed by the caller) and the wheel's own sideways slip into forces clamped to
        /// a friction ellipse. Clamping both axes together rather than independently is what makes
        /// accelerating hard mid-corner cost you some cornering grip for free, without a separate knob.
        /// </summary>
        /// <param name="extraLateralDemand">
        /// Sideways force the tyre must find on top of what the slip alone asks for — in practice
        /// gravity's share across the kart on a slope. Needed because the slip term is zero at a
        /// standstill, so without it nothing holds the kart sideways on a hill. Defaulted so existing
        /// callers and tests are unaffected.
        /// </param>
        /// <param name="lateralPriority">
        /// How much of the grip budget cornering gets first call on, 0 to 1.
        ///
        /// At 0 both axes are cut back together, which is honest tyre physics and is exactly what a
        /// racing simulator wants: open the throttle mid-corner and you pay for it in grip, and past
        /// the limit the back steps out. This game does not want that. Drifting here is meant to be a
        /// thing the player asks for with the handbrake, not a thing the kart does to them for
        /// accelerating out of a bend, so at 1 the sideways demand is served in full and drive takes
        /// whatever the ellipse has left over.
        ///
        /// That still costs something — it has to, the grip is finite — but it costs acceleration
        /// rather than control, and losing a little drive out of a corner is a fair trade a player can
        /// feel and work with. Surface still scales both limits, so snow is still slippier than rock.
        /// </param>
        public static TyreForceResult SolveTyreForce(
            float demandedForwardForce, float lateralVelocity, float lateralStiffness,
            float load, float forwardGripCoefficient, float lateralGripCoefficient,
            float extraLateralDemand = 0f, float lateralPriority = 0f)
        {
            float maxForward = Mathf.Max(forwardGripCoefficient * load, 0f);
            float maxLateral = Mathf.Max(lateralGripCoefficient * load, 0f);

            // A wheel carrying no load has no grip limit to divide by — and no grip to transmit any
            // force through, however small. Falling straight into the ratio maths below would default
            // each fraction to 0 for "no limit to speak of", read the combined demand as harmlessly
            // under 1, and pass the ENTIRE demanded force through unclamped: a wheel that has bounced
            // off the ground would still be shoving the kart around by whatever torque was asked of it.
            if (maxForward <= 1e-4f && maxLateral <= 1e-4f)
                return new TyreForceResult { forwardForce = 0f, lateralForce = 0f, slipRatio = 0f };

            float demandedLateralForce = -lateralVelocity * lateralStiffness + extraLateralDemand;

            float forwardFraction = maxForward > 1e-4f ? demandedForwardForce / maxForward : 0f;
            float lateralFraction = maxLateral > 1e-4f ? demandedLateralForce / maxLateral : 0f;
            float combined = Mathf.Sqrt(forwardFraction * forwardFraction + lateralFraction * lateralFraction);

            // Both axes cut back together — the symmetric clamp, and the honest one.
            float symmetric = combined > 1f ? 1f / combined : 1f;

            // Cornering served first, drive given the rest of the ellipse. The sideways demand is only
            // clamped by its own limit, and what is left over after it is spent —
            // sqrt(1 - lateral²) — is all the drive is allowed to ask for.
            float absLateral = Mathf.Abs(lateralFraction);
            float lateralFirst = absLateral > 1f ? 1f / absLateral : 1f;
            float budgetLeft = Mathf.Sqrt(Mathf.Max(0f, 1f - Mathf.Min(absLateral * absLateral, 1f)));
            float absForward = Mathf.Abs(forwardFraction);
            float forwardLast = absForward > budgetLeft ? budgetLeft / Mathf.Max(absForward, 1e-4f) : 1f;

            float priority = Mathf.Clamp01(lateralPriority);
            float lateralScale = Mathf.Lerp(symmetric, lateralFirst, priority);
            float forwardScale = Mathf.Lerp(symmetric, forwardLast, priority);

            return new TyreForceResult
            {
                forwardForce = demandedForwardForce * forwardScale,
                lateralForce = demandedLateralForce * lateralScale,
                // Still the raw combined demand, whichever way it was clamped: this is what traction
                // control reads and what flares the engine note, and both want to know the tyre was
                // asked for more than it had — not which axis ended up conceding.
                slipRatio = combined,
            };
        }

        /// <summary>
        /// The force a tyre must put down to stay stuck to the ground rather than slide along it.
        ///
        /// This is the static half of friction, and without it a tyre model has no rubber in it at
        /// all. Both of the demands above are proportional to how fast the contact patch is already
        /// slipping, so both are zero when it is not slipping — which means nothing whatsoever holds
        /// a stationary kart against gravity. Parked on an 8 degree slope, this kart had 348 N
        /// pulling it downhill and 55 N of rolling resistance opposing it, so it crept away and never
        /// stopped: 0.74 m in forty seconds, still moving, on its tyres the whole time. Real rubber
        /// resists up to mu*N with no motion at all, which is why a real kart just sits there.
        ///
        /// Two terms, and both are needed:
        ///
        /// <paramref name="slipVelocity"/> is cancelled over one step, which removes slip that has
        /// already happened. On its own that still leaves the kart creeping, because gravity puts a
        /// fresh step's worth of speed back in every step — the residual works out at
        /// g*sin(slope)*dt, about 3 cm/s here, small but never zero and never settling.
        ///
        /// So <paramref name="tangentialForce"/> — whatever is pulling the contact patch along the
        /// ground, gravity's share of it above all — is opposed directly as well. That is what makes
        /// the difference between creeping slowly and genuinely standing still.
        ///
        /// The result is only a <em>demand</em>. It goes through the same friction ellipse as
        /// everything else, so a tyre can only hold what its load allows: under the limit it sticks
        /// absolutely, over it, it slides. A slope steeper than atan(mu) still slides the kart away,
        /// which is correct — this is grip, not glue.
        /// </summary>
        /// <param name="slipVelocity">Contact patch speed along this axis, m/s.</param>
        /// <param name="tangentialForce">External force along this axis at the contact patch, N.</param>
        /// <param name="massShare">Mass this tyre is carrying, kg. Normally load / gravity.</param>
        public static float SolveHoldingForce(
            float slipVelocity, float tangentialForce, float massShare, float deltaTime)
        {
            if (deltaTime <= 1e-6f || massShare <= 0f) return 0f;

            return -slipVelocity * (massShare / deltaTime) - tangentialForce;
        }

        /// <summary>
        /// The most sideways stiffness a fixed timestep can actually integrate, and the ceiling this
        /// kart's <c>lateralStiffness</c> is held to.
        ///
        /// A damper of <c>k</c> newtons per (m/s) on <c>m</c> kilograms changes the slip velocity by
        /// <c>-k·v·dt/m</c> each step. Once <c>k·dt/m</c> passes 1 the correction overshoots zero;
        /// past 2 it overshoots by more than it started with and the tyre rings instead of settling,
        /// bounded only by the friction clamp. This kart shipped at 9000 N per (m/s) per wheel, which
        /// on 255 kg at 50 Hz works out at <c>k·dt/m</c> ≈ 2.8 — comfortably unstable, and the reason
        /// a kart parked across a slope shuffled sideways forever instead of sitting still.
        ///
        /// <c>m/dt</c> is the stiffness that removes exactly the slip present and no more, so it is
        /// both the stablest choice and the stiffest one worth having. Anything softer is honoured as
        /// asked; anything harder is a request for a stiffer tyre than the timestep can represent.
        /// </summary>
        public static float StableLateralStiffness(float lateralStiffness, float massShare, float deltaTime)
        {
            if (deltaTime <= 1e-6f || massShare <= 0f) return lateralStiffness;

            return Mathf.Min(lateralStiffness, massShare / deltaTime);
        }

        /// <summary>
        /// How strongly the static hold applies at a given speed: full below
        /// <paramref name="holdSpeed"/>, gone above it, eased between.
        ///
        /// A tyre that resisted rolling at every speed would be a locked brake, so along the length of
        /// the kart the hold has to be confined to walking pace — fast enough to stop it creeping,
        /// slow enough that coasting still feels like coasting. Eased rather than switched so that
        /// rolling to a stop does not end with the kart snatching to a halt.
        /// </summary>
        public static float HoldBlend(float forwardSpeed, float holdSpeed)
        {
            if (holdSpeed <= 1e-4f) return 0f;

            float t = Mathf.Clamp01(Mathf.Abs(forwardSpeed) / holdSpeed);
            return 1f - (t * t * (3f - 2f * t)); // smoothstep, faded out rather than switched off
        }

        /// <summary>
        /// Wheel angular velocity for visuals and audio only — it never feeds back into the
        /// translational force above, which is what keeps this from ever reintroducing a coupled-force
        /// bug like the one that motivated replacing WheelCollider in the first place.
        ///
        /// Free-integrates from the applied torque like an unloaded flywheel, then relaxes toward the
        /// speed true rolling would imply. Under normal grip that relaxation is fast enough to read as
        /// smooth rolling; under a genuine torque surplus (more than the tyre could put down) the
        /// integration still visibly races ahead for a moment, which is real wheelspin, not decoration.
        /// </summary>
        public static float IntegrateWheelSpin(
            float currentAngularVelocity, float motorTorque, float brakeTorqueMagnitude,
            float groundAngularVelocity, bool grounded, float wheelInertia,
            float relaxationRate, float deltaTime)
        {
            float safeInertia = Mathf.Max(wheelInertia, 1e-4f);

            // Brakes oppose whichever way the wheel is already turning; at a dead stop there is nothing
            // to oppose, and Mathf.Sign(0) confusingly returns +1, so guard the zero case explicitly.
            float spinSign = currentAngularVelocity > 1e-3f ? 1f : currentAngularVelocity < -1e-3f ? -1f : 0f;
            float brakeTorque = -spinSign * brakeTorqueMagnitude;

            float angular = currentAngularVelocity + (motorTorque + brakeTorque) / safeInertia * deltaTime;

            if (grounded)
            {
                angular = Mathf.Lerp(
                    angular, groundAngularVelocity, 1f - Mathf.Exp(-relaxationRate * deltaTime));
            }

            return angular;
        }

        /// <summary>Moment of inertia of a solid-ish disc, close enough for a spin value that is cosmetic.</summary>
        public static float WheelInertia(float mass, float radius) => 0.5f * mass * radius * radius;
    }
}
