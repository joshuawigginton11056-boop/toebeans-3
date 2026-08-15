using UnityEngine;

namespace Toebeans.Karting
{
    /// <summary>Spring and damper rates for one corner.</summary>
    public struct KartSuspensionSetup
    {
        public float spring;
        public float damper;
        /// <summary>How far the spring sags under the weight it carries, in metres.</summary>
        public float staticSag;
    }

    /// <summary>
    /// Turns the ride you want into the numbers the physics engine needs.
    ///
    /// Spring rates are meaningless on their own — 15,000 N/m is soft under a lorry and rigid under a
    /// kart. Deriving them from the mass each corner carries and the frequency that mass should bounce
    /// at is what makes the kart feel like it weighs what it says it weighs, and keeps it feeling that
    /// way after the mass is changed.
    ///
    /// The kart's own raycast suspension (see [[unity-wheelcollider-total-velocity-lock]] for why it
    /// isn't Unity's WheelCollider) measures compression from full droop, so unlike the old WheelCollider
    /// setup there is no separate "rest position" to solve for — the equilibrium ride height falls out
    /// on its own from spring force balancing weight. staticSag is kept only as a number worth showing
    /// on the HUD or checking against suspensionDistance while tuning.
    ///
    /// Pure and static, so the numbers can be checked outside the Editor.
    /// </summary>
    public static class KartSuspension
    {
        public static KartSuspensionSetup Solve(float sprungMass, float rideFrequency, float dampingRatio,
            float gravity = 9.81f)
        {
            sprungMass = Mathf.Max(sprungMass, 0.001f);

            // k = m·ω², the spring rate that makes this mass bounce at the frequency asked for.
            float omega = 2f * Mathf.PI * Mathf.Max(rideFrequency, 0.01f);
            float spring = sprungMass * omega * omega;

            // c = 2ζ√(km): ζ = 1 is critical damping, below that it oscillates.
            float damper = 2f * dampingRatio * Mathf.Sqrt(spring * sprungMass);

            // The spring sags by mg/k just holding the kart up — the equilibrium compression, measured
            // from full droop.
            float staticSag = sprungMass * Mathf.Abs(gravity) / spring;

            return new KartSuspensionSetup { spring = spring, damper = damper, staticSag = staticSag };
        }
    }
}
