using UnityEngine;

namespace Farm
{
    /// <summary>
    /// Walks, grazes and idles one of the generated farm animals by posing its parts directly.
    ///
    /// There is no Animator, no clip and no state machine, and that is the design rather than a
    /// shortcut. Three things fall out of it:
    ///
    /// **The gait is always right.** The legs swing off <em>distance travelled</em>, not off a
    /// clock, so one stride is one <see cref="strideLength"/> of ground at any speed. A clip has to
    /// be authored at one speed and blended or time-scaled at every other, and the classic failure
    /// — feet skating because the playback rate and the movement rate disagree — cannot happen
    /// here, because there is only one number.
    ///
    /// **A herd is free.** Forty of these are forty transform writes a frame. Forty Animators with
    /// blend trees are not.
    ///
    /// **It is the same on every machine.** The animal's whole route is baked from its seed at
    /// Awake and then sampled as a pure function of <see cref="FarmClock"/>. Nothing accumulates,
    /// so nothing drifts, and there is no animation state to put on the wire. See FarmClock for the
    /// one line that ties it to session time.
    ///
    /// The part names it looks for are the contract in <c>Tools/blender/models/farm_animals.py</c>.
    /// Missing parts are simply not posed, so this is safe on a model that has no tail or no ears.
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("Toebeans/Farm/Farm Animal")]
    public sealed class FarmAnimal : MonoBehaviour
    {
        public enum Habit
        {
            /// <summary>Wander a closed route around where it was placed, pausing to graze.</summary>
            Wander = 0,

            /// <summary>Stay put. Still breathes, chews and flicks its tail.</summary>
            Stand = 1,
        }

        [Header("Behaviour")]
        public Habit habit = Habit.Wander;

        [Tooltip("How far from where it was placed the animal will roam, in metres.")]
        public float roamRadius = 5f;

        [Tooltip("Metres per second while walking. A cow ambles at about 0.7.")]
        public float walkSpeed = 0.7f;

        [Range(2, 12)]
        [Tooltip("Waypoints on the closed route. More is a wider wander, not a longer one.")]
        public int waypoints = 5;

        [Tooltip("Roughly how long it stops at each waypoint, in seconds.")]
        public float pauseSeconds = 7f;

        [Tooltip("0 derives a stable seed from the placed position, so two animals side by side " +
                 "behave differently and the same animal behaves the same on every machine.")]
        public int seed;

        [Header("Gait")]
        [Tooltip("Ground covered by one full leg cycle. Too short and the animal minces; too long " +
                 "and it moonwalks.")]
        public float strideLength = 0.95f;

        [Tooltip("Peak swing of a leg, in degrees either side of rest.")]
        public float legSwing = 22f;

        [Tooltip("How far the body rises and falls over a stride, in metres.")]
        public float bodyBob = 0.025f;

        [Header("Idling")]
        [Tooltip("How far the head drops to graze, in degrees.")]
        public float grazePitch = 52f;

        [Tooltip("Degrees the tail swings either side.")]
        public float tailSwish = 13f;

        [Header("Ground")]
        public bool alignToGround = true;

        [Tooltip("How far the animal tips to follow a slope. 0 keeps it upright.")]
        [Range(0f, 1f)]
        public float slopeFollow = 0.6f;

        public LayerMask groundMask = ~0;

        // ---------------------------------------------------------------- baked route

        Vector3[] _points;
        float[] _arrive;        // when the animal reaches waypoint i
        float[] _depart;        // when it leaves again
        float[] _legTime;       // seconds to walk from i to i+1
        float[] _distanceAt;    // ground covered by the time it reaches i
        bool[] _grazesAt;
        float _period;

        // ---------------------------------------------------------------- rig
        FarmJoint _body, _head, _jaw, _tail;
        FarmJoint[] _legs = new FarmJoint[0];
        float[] _legPhase = new float[0];
        FarmJoint _earL, _earR, _wingL, _wingR;
        bool _bound;

        Vector3 _home;

        void Awake()
        {
            _home = transform.position;
            if (seed == 0) seed = FarmClock.SeedFrom(_home);
            Bind();
            BakeRoute();
        }

        void OnValidate()
        {
            roamRadius = Mathf.Max(0f, roamRadius);
            walkSpeed = Mathf.Max(0.02f, walkSpeed);
            strideLength = Mathf.Max(0.05f, strideLength);
            pauseSeconds = Mathf.Max(0f, pauseSeconds);
        }

        void Bind()
        {
            Transform t = transform;
            _body = FarmJoint.Bind(t, "Body");
            _head = FarmJoint.Bind(t, "Head");
            _jaw = FarmJoint.Bind(t, "Jaw");
            _tail = FarmJoint.Bind(t, "Tail");
            _earL = FarmJoint.Bind(t, "Ear_L");
            _earR = FarmJoint.Bind(t, "Ear_R");
            _wingL = FarmJoint.Bind(t, "Wing_L");
            _wingR = FarmJoint.Bind(t, "Wing_R");

            // Four legs or two. The phase offsets are the difference between a walk and a hop:
            // a quadruped's four-beat walk moves one foot at a time in the order near-hind,
            // near-fore, off-hind, off-fore, which is the quarter-cycle spacing below. A bird
            // simply alternates.
            var quad = new[] { "Leg_FL", "Leg_BR", "Leg_FR", "Leg_BL" };
            var biped = new[] { "Leg_FL", "Leg_FR" };

            bool isQuad = FarmRig.Find(t, "Leg_BL") != null;
            string[] names = isQuad ? quad : biped;

            _legs = new FarmJoint[names.Length];
            _legPhase = new float[names.Length];
            for (int i = 0; i < names.Length; i++)
            {
                _legs[i] = FarmJoint.Bind(t, names[i]);
                _legPhase[i] = i / (float)names.Length;
            }

            _bound = true;
        }

        /// <summary>
        /// Lays out the closed route and its timetable once, so sampling it later is arithmetic.
        ///
        /// Waypoints are spread by angle rather than picked at random in a disc. Random points
        /// clump, and a clumped route makes an animal shuffle back and forth in one corner of its
        /// range while never visiting the rest of it.
        /// </summary>
        void BakeRoute()
        {
            int n = Mathf.Max(2, waypoints);
            var rng = new FarmRandom(seed);

            _points = new Vector3[n];
            if (habit == Habit.Stand || roamRadius <= 0.01f)
            {
                for (int i = 0; i < n; i++) _points[i] = _home;
            }
            else
            {
                float spin = rng.Value * Mathf.PI * 2f;
                for (int i = 0; i < n; i++)
                {
                    float a = spin + (i / (float)n) * Mathf.PI * 2f + rng.Range(-0.35f, 0.35f);
                    float r = roamRadius * Mathf.Sqrt(rng.Range(0.25f, 1f));
                    _points[i] = _home + new Vector3(Mathf.Cos(a) * r, 0f, Mathf.Sin(a) * r);
                }
            }

            _arrive = new float[n];
            _depart = new float[n];
            _legTime = new float[n];
            _distanceAt = new float[n];
            _grazesAt = new bool[n];

            float clock = 0f;
            float covered = 0f;
            for (int i = 0; i < n; i++)
            {
                _arrive[i] = clock;
                _distanceAt[i] = covered;
                _grazesAt[i] = rng.Value < 0.55f;

                float dwell = pauseSeconds * rng.Range(0.55f, 1.6f);
                _depart[i] = clock + dwell;

                float span = Vector3.Distance(_points[i], _points[(i + 1) % n]);
                _legTime[i] = span / walkSpeed;

                clock = _depart[i] + _legTime[i];
                covered += span;
            }

            // A route that is a single point still needs a period, or sampling divides by zero.
            _period = Mathf.Max(0.5f, clock);
        }

        void LateUpdate()
        {
            if (!_bound) Bind();
            if (_points == null || _points.Length == 0) BakeRoute();

            float t = (float)(FarmClock.Now % _period);

            int leg;
            float walkU;
            bool moving;
            FindSegment(t, out leg, out walkU, out moving);

            int n = _points.Length;
            Vector3 from = _points[leg];
            Vector3 to = _points[(leg + 1) % n];

            Vector3 flat = moving ? Vector3.Lerp(from, to, walkU) : from;
            float travelled = _distanceAt[leg] + (moving ? walkU * Vector3.Distance(from, to) : 0f);

            PlaceOnGround(flat, moving, leg, walkU);
            Pose(travelled, moving, leg, t);
        }

        void FindSegment(float t, out int leg, out float u, out bool moving)
        {
            int n = _points.Length;
            for (int i = 0; i < n; i++)
            {
                if (t < _depart[i])
                {
                    leg = i;
                    moving = false;
                    float dwell = Mathf.Max(0.0001f, _depart[i] - _arrive[i]);
                    u = Mathf.Clamp01((t - _arrive[i]) / dwell);
                    return;
                }

                float walkEnd = _depart[i] + _legTime[i];
                if (t < walkEnd)
                {
                    leg = i;
                    moving = true;
                    u = Mathf.Clamp01((t - _depart[i]) / Mathf.Max(0.0001f, _legTime[i]));
                    return;
                }
            }

            // Floating point can leave t a hair past the last segment's end.
            leg = n - 1;
            moving = false;
            u = 1f;
        }

        void PlaceOnGround(Vector3 flat, bool moving, int leg, float u)
        {
            int n = _points.Length;
            Vector3 outgoing = _points[(leg + 1) % n] - _points[leg];
            Vector3 incoming = _points[leg] - _points[(leg + n - 1) % n];

            Vector3 facing;
            if (moving)
            {
                facing = outgoing;
            }
            else
            {
                // Turn on the spot during the pause, rather than snapping to the new heading the
                // instant the animal starts walking again. Interpolated over the dwell so it is
                // still a pure function of time.
                float turn = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.45f, 0.95f, u));
                facing = Vector3.Slerp(incoming.normalized, outgoing.normalized, turn);
            }

            if (facing.sqrMagnitude < 1e-6f) facing = transform.forward;
            facing.y = 0f;
            facing.Normalize();

            Vector3 up = Vector3.up;
            Vector3 place = flat;

            if (alignToGround)
            {
                RaycastHit hit;
                // Cast from well above and well below, so an animal wandering onto a bank or a
                // ditch still finds the ground rather than falling through the world.
                if (Physics.Raycast(flat + Vector3.up * 4f, Vector3.down, out hit, 12f,
                                    groundMask, QueryTriggerInteraction.Ignore))
                {
                    place = hit.point;
                    up = Vector3.Slerp(Vector3.up, hit.normal, slopeFollow);
                }
                else
                {
                    place.y = _home.y;
                }
            }
            else
            {
                place.y = _home.y;
            }

            transform.position = place;
            transform.rotation = Quaternion.LookRotation(
                Vector3.ProjectOnPlane(facing, up).normalized, up);

        }

        void Pose(float travelled, bool moving, int leg, float t)
        {
            const float Tau = Mathf.PI * 2f;

            // The one number the whole gait comes off. Distance, not time.
            float stride = travelled / Mathf.Max(0.05f, strideLength);
            float swing = moving ? 1f : 0f;

            for (int i = 0; i < _legs.Length; i++)
            {
                if (!_legs[i].Ok) continue;
                float phase = (stride + _legPhase[i]) * Tau;
                // A walking leg swings further forward than back — the back half of a stride is
                // the leg planted, and a symmetric sine reads as wading rather than walking.
                float s = Mathf.Sin(phase);
                float shaped = s > 0f ? s : s * 0.7f;
                _legs[i].Pose(shaped * legSwing * swing, 0f, 0f);
            }

            // Standing animals breathe: a slow rise and fall, so a still herd is not a still image.
            float breathe = Mathf.Sin((float)FarmClock.Now * 0.9f + seed * 0.017f) * 0.006f;
            float bob = moving ? Mathf.Abs(Mathf.Sin(stride * Tau)) * bodyBob : 0f;
            if (_body.Ok) _body.Offset(new Vector3(0f, bob + breathe, 0f));

            bool grazing = !moving && _grazesAt[leg];

            // Ease the head down rather than dropping it. `u` runs over the whole dwell, so the
            // ease has to be driven off the same value to stay a pure function of time.
            float dwell = Mathf.Max(0.0001f, _depart[leg] - _arrive[leg]);
            float u = Mathf.Clamp01((t - _arrive[leg]) / dwell);
            float down = grazing ? Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.05f, 0.30f, u)) *
                                   (1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.80f, 0.98f, u)))
                                 : 0f;

            float chew = grazing ? Mathf.Sin((float)FarmClock.Now * 7.5f) * 0.5f + 0.5f : 0f;
            float lookAbout = (!moving && !grazing)
                ? Mathf.Sin((float)FarmClock.Now * 0.55f + seed * 0.031f) * 22f
                : 0f;

            if (_head.Ok)
            {
                float nod = moving ? Mathf.Sin(stride * Tau * 2f) * 3.5f : 0f;
                _head.Pose(grazePitch * down + nod, lookAbout, 0f);
            }
            if (_jaw.Ok) _jaw.Pose(chew * 9f, 0f, 0f);

            if (_tail.Ok)
            {
                // Faster when standing. A tail is for flies, and an animal that has stopped to
                // graze is the one being bitten.
                float rate = moving ? 1.4f : 2.3f;
                float swish = Mathf.Sin((float)FarmClock.Now * rate + seed * 0.011f);
                _tail.Pose(Mathf.Abs(swish) * 6f, 0f, swish * tailSwish);
            }

            // Ears flick on their own short cycle, offset from each other, because two ears moving
            // together read as a mechanism and two ears moving apart read as an animal.
            PoseEar(_earL, 0f);
            PoseEar(_earR, 1.7f);

            if (_wingL.Ok || _wingR.Ok)
            {
                float flutter = moving ? Mathf.Sin(stride * Tau) * 7f : 0f;
                float settle = Mathf.Max(0f, Mathf.Sin((float)FarmClock.Now * 0.8f + seed * 0.07f) - 0.93f) * 260f;
                if (_wingL.Ok) _wingL.Pose(0f, 0f, -(flutter + settle));
                if (_wingR.Ok) _wingR.Pose(0f, 0f, flutter + settle);
            }
        }

        void PoseEar(FarmJoint ear, float offset)
        {
            if (!ear.Ok) return;
            float drive = Mathf.Sin((float)FarmClock.Now * 1.6f + seed * 0.023f + offset);
            float flick = Mathf.Max(0f, drive - 0.86f) * 130f;
            ear.Pose(0f, flick, drive * 3f);
        }

        void OnDrawGizmosSelected()
        {
            Vector3 home = Application.isPlaying ? _home : transform.position;

            Gizmos.color = new Color(0.4f, 0.8f, 0.5f, 0.5f);
            Gizmos.DrawWireSphere(home, roamRadius);

            if (_points == null || _points.Length < 2) return;
            Gizmos.color = new Color(0.9f, 0.8f, 0.3f, 0.9f);
            for (int i = 0; i < _points.Length; i++)
            {
                Vector3 a = _points[i];
                Vector3 b = _points[(i + 1) % _points.Length];
                Gizmos.DrawLine(a, b);
                Gizmos.DrawWireCube(a, Vector3.one * 0.18f);
            }
        }
    }
}
