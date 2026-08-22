using UnityEngine;

namespace Farm
{
    /// <summary>
    /// Spins a windpump's rotor and yaws its head to face the wind.
    ///
    /// The tallest thing on the map after the silo, and the only one that moves on its own, so it
    /// is doing a job beyond decoration: a turning rotor on the skyline is a landmark a driver can
    /// navigate by, and one that has visibly stopped reads as a dead map.
    ///
    /// Both angles are pure functions of (seed, <see cref="FarmClock"/>) — see FarmClock for why
    /// nothing on this farm integrates. The gusting in particular is worth keeping deterministic:
    /// two clients whose windmills gust out of step is exactly the sort of thing that looks like a
    /// rendering bug rather than an intentional variation.
    ///
    /// Part names come from <c>Tools/blender/models/farm_buildings.py</c>: Tower is the root, Head
    /// yaws about its own Y, and Rotor spins about its own Y with Vane hanging off the back.
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("Toebeans/Farm/Farm Windpump")]
    public sealed class FarmWindpump : MonoBehaviour
    {
        [Header("Wind")]
        [Tooltip("Compass direction the wind blows towards, in degrees. Point every windpump and " +
                 "weathervane on a map at the same value and the whole map agrees about the wind.")]
        public float windHeading = 135f;

        [Tooltip("How far the head wanders either side of the wind, in degrees.")]
        public float veer = 22f;

        [Tooltip("Seconds for one full wander. Slow — a vane that hunts looks broken.")]
        public float veerPeriod = 26f;

        [Header("Rotor")]
        [Tooltip("Revolutions per minute in a steady breeze.")]
        public float rpm = 34f;

        [Range(0f, 1f)]
        [Tooltip("How much the gusts vary the speed. 0 is a metronome.")]
        public float gustiness = 0.4f;

        [Tooltip("0 derives a stable seed from the placed position, so two windpumps on one map " +
                 "gust independently.")]
        public int seed;

        FarmJoint _head, _rotor;
        bool _bound;

        // The rotor's angle *is* integrated, unlike everything else here, because the alternative
        // is worse. Deriving it as rpm * time means a change of speed retimes the whole history:
        // turn the gust up and the blades jump to wherever the new rate says they should have got
        // to by now. Integrating a deterministic speed keeps the angle continuous and still leaves
        // every client within a fraction of a turn of every other, which on a fourteen-blade fan is
        // not a difference anybody can see.
        float _spin;

        void Awake()
        {
            if (seed == 0) seed = FarmClock.SeedFrom(transform.position);
            Bind();
        }

        void Bind()
        {
            _head = FarmJoint.Bind(transform, "Head");
            _rotor = FarmJoint.Bind(transform, "Rotor");
            _bound = true;
        }

        void LateUpdate()
        {
            if (!_bound) Bind();

            float t = (float)FarmClock.Now;
            float wobble = seed * 0.019f;

            // Two sines at unrelated rates rather than one, so the gust never settles into an
            // obvious loop. Clamped positive: a windpump that runs backwards is a windmill.
            float gust = 1f + gustiness * (
                Mathf.Sin(t * 0.23f + wobble) * 0.6f +
                Mathf.Sin(t * 0.61f + wobble * 2.3f) * 0.4f);
            gust = Mathf.Max(0.08f, gust);

            _spin += rpm * 6f * gust * Time.deltaTime;   // rpm -> degrees per second
            if (_spin > 360f) _spin -= 360f;
            if (_rotor.Ok) _rotor.Pose(0f, _spin, 0f);

            if (_head.Ok)
            {
                float wander = Mathf.Sin(t * (Mathf.PI * 2f / Mathf.Max(1f, veerPeriod)) + wobble) * veer;
                // The head yaws in its own space, so the value is relative to however the prop was
                // placed. A windpump rotated in the scene still faces the map's wind, not its own.
                float local = Mathf.DeltaAngle(transform.eulerAngles.y, windHeading + wander);
                _head.Pose(0f, local, 0f);
            }
        }
    }
}
