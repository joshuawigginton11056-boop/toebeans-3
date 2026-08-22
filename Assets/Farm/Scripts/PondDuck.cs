using UnityEngine;

namespace Farm
{
    /// <summary>
    /// A duck floating on a pond: drifting, bobbing, paddling, and quacking as a kart goes past.
    ///
    /// Four things it does that a floating prop does not:
    ///
    /// **It sits at the right depth.** A duck floats low — roughly the bottom third of its body is
    /// under water — and one perched on the surface reads as a bath toy instantly. The depth is not
    /// a number guessed in the inspector: <see cref="waterline"/> is measured off the model by the
    /// Blender build and carried through the manifest, so if the duck is remodelled the depth
    /// follows it.
    ///
    /// **It drifts rather than sits.** The drift, the bob and the heading are all pure functions of
    /// (seed, <see cref="FarmClock"/>), so a pond of ducks needs no state on the wire and every
    /// client draws the same pond. Same arrangement as <see cref="FarmAnimal"/>, and the same
    /// single line ties it to session time.
    ///
    /// **It paddles.** The feet turn over under the body — out of sight below the surface, which is
    /// exactly why it matters: the visible tell of paddling is the way the body surges very
    /// slightly with each stroke, and that is driven off the same phase.
    ///
    /// **It quacks at karts.** Not on a timer: on something actually going past, above a speed
    /// threshold, with a cooldown so a kart parked alongside does not machine-gun it. The bill opens
    /// while the sound plays, because a quack out of a shut bill is a sound effect near a duck.
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("Toebeans/Farm/Pond Duck")]
    [RequireComponent(typeof(AudioSource))]
    public sealed class PondDuck : MonoBehaviour
    {
        [Header("Floating")]
        [Tooltip("Height of the pond surface, in world Y. Set this to the water plane's Y.")]
        public float waterLevel;

        [Tooltip("Measured off the model by the Blender build: how far above the prop's origin the " +
                 "surface should cross it. Do not guess this — re-export instead.")]
        public float waterline = 0.175f;

        [Tooltip("How far the duck rides up and down, in metres.")]
        public float bobHeight = 0.022f;

        [Tooltip("Degrees of roll and pitch as it rides.")]
        public float rockDegrees = 4.5f;

        [Header("Drifting")]
        [Tooltip("Radius of the loop it drifts around, in metres. 0 holds station.")]
        public float driftRadius = 1.8f;

        [Tooltip("Seconds for one full loop. Long: a drifting duck is barely moving.")]
        public float driftPeriod = 46f;

        [Tooltip("0 derives a stable seed from the placed position.")]
        public int seed;

        [Header("Quacking")]
        [Tooltip("Clips to choose between. The setup tool fills this from the baked quacks.")]
        public AudioClip[] quacks;

        [Tooltip("How close a kart has to come, in metres.")]
        public float hearingRadius = 9f;

        [Tooltip("Metres per second a passer-by must be doing. A parked kart is not an event.")]
        public float minPasserSpeed = 3.5f;

        [Tooltip("Seconds before this duck will quack again.")]
        public float quackCooldown = 3.2f;

        [Tooltip("Layers a passing kart is on. Leave as Everything to react to any rigidbody.")]
        public LayerMask passerMask = ~0;

        [Range(0f, 1f)]
        [Tooltip("Chance a duck reacts at all when something passes. Below 1 a raft of ducks " +
                 "answers raggedly instead of in unison, which is the whole charm of it.")]
        public float reactChance = 0.7f;

        FarmJoint _body, _head, _jaw, _tail, _wingL, _wingR, _legL, _legR;
        AudioSource _audio;
        Vector3 _home;
        bool _bound;

        double _nextQuackAllowed;
        double _quackStarted = -99.0;
        float _quackLength;
        readonly Collider[] _hits = new Collider[8];
        double _nextScan;

        void Awake()
        {
            _home = transform.position;
            if (seed == 0) seed = FarmClock.SeedFrom(_home);

            _audio = GetComponent<AudioSource>();
            _audio.playOnAwake = false;
            _audio.spatialBlend = 1f;          // fully 3D, or every duck is in the player's ear
            _audio.rolloffMode = AudioRolloffMode.Linear;
            _audio.minDistance = 4f;
            _audio.maxDistance = 34f;

            Bind();
        }

        void Bind()
        {
            Transform t = transform;
            _body = FarmJoint.Bind(t, "Body");
            _head = FarmJoint.Bind(t, "Head");
            _jaw = FarmJoint.Bind(t, "Jaw");
            _tail = FarmJoint.Bind(t, "Tail");
            _wingL = FarmJoint.Bind(t, "Wing_L");
            _wingR = FarmJoint.Bind(t, "Wing_R");
            _legL = FarmJoint.Bind(t, "Leg_FL");
            _legR = FarmJoint.Bind(t, "Leg_FR");
            _bound = true;
        }

        void LateUpdate()
        {
            if (!_bound) Bind();

            double now = FarmClock.Now;
            float t = (float)now;
            float wobble = seed * 0.013f;

            // ---- drift: a slow loop, squashed into an ellipse and turned, so a raft of ducks
            // does not read as several objects orbiting the same circle at the same rate.
            var rng = new FarmRandom(seed);
            float turn = rng.Value * Mathf.PI * 2f;
            float squash = rng.Range(0.45f, 0.95f);
            float rate = rng.Range(0.72f, 1.35f);

            float u = (t / Mathf.Max(1f, driftPeriod)) * rate * Mathf.PI * 2f + wobble;
            float lx = Mathf.Cos(u) * driftRadius;
            float lz = Mathf.Sin(u) * driftRadius * squash;
            Vector3 offset = Quaternion.Euler(0f, turn * Mathf.Rad2Deg, 0f) * new Vector3(lx, 0f, lz);

            // Heading is the derivative of the drift, so the duck faces where it is going without
            // anything being integrated. A duck that drifts sideways is a decoy.
            float dx = -Mathf.Sin(u) * driftRadius;
            float dz = Mathf.Cos(u) * driftRadius * squash;
            Vector3 heading = Quaternion.Euler(0f, turn * Mathf.Rad2Deg, 0f) * new Vector3(dx, 0f, dz);
            if (heading.sqrMagnitude < 1e-5f) heading = transform.forward;

            // ---- float: the origin sits `waterline` below the surface, plus the bob.
            float bob = Mathf.Sin(t * 1.15f + wobble) * bobHeight
                      + Mathf.Sin(t * 2.7f + wobble * 3f) * bobHeight * 0.35f;

            Vector3 place = _home + offset;
            place.y = waterLevel - waterline + bob;
            transform.position = place;

            float roll = Mathf.Sin(t * 1.4f + wobble * 2f) * rockDegrees;
            float pitch = Mathf.Sin(t * 0.95f + wobble) * rockDegrees * 0.6f;
            transform.rotation = Quaternion.LookRotation(heading.normalized, Vector3.up)
                               * Quaternion.Euler(pitch, 0f, roll);

            // ---- paddling. Under the water and invisible, but the surge it drives is not.
            float paddle = t * 2.6f + wobble;
            if (_legL.Ok) _legL.Pose(Mathf.Sin(paddle) * 26f, 0f, 0f);
            if (_legR.Ok) _legR.Pose(Mathf.Sin(paddle + Mathf.PI) * 26f, 0f, 0f);
            if (_body.Ok) _body.Offset(new Vector3(0f, 0f, Mathf.Sin(paddle * 2f) * 0.006f));

            // ---- head: slow turns, plus a preen every so often.
            float preenDrive = Mathf.Sin(t * 0.31f + wobble * 5f);
            float preen = Mathf.Max(0f, preenDrive - 0.94f) * 240f;
            if (_head.Ok)
            {
                _head.Pose(preen * 0.6f,
                           Mathf.Sin(t * 0.42f + wobble * 2f) * 26f,
                           0f);
            }
            if (_tail.Ok) _tail.Pose(Mathf.Sin(t * 1.1f + wobble) * 5f, 0f, 0f);

            // ---- quack: the bill opens for as long as the clip lasts.
            float open = 0f;
            if (now - _quackStarted < _quackLength)
            {
                float k = (float)((now - _quackStarted) / Mathf.Max(0.05f, _quackLength));
                // Two syllables' worth of opening, tailing off. Cheap, and it matches the shape
                // of the baked clip closely enough that the bill lands with the sound.
                open = Mathf.Abs(Mathf.Sin(k * Mathf.PI * 2f)) * (1f - k * 0.4f);
            }
            if (_jaw.Ok) _jaw.Pose(open * 17f, 0f, 0f);

            float flap = open > 0.35f ? open * 22f : 0f;
            if (_wingL.Ok) _wingL.Pose(0f, 0f, -flap);
            if (_wingR.Ok) _wingR.Pose(0f, 0f, flap);

            Scan(now);
        }

        /// <summary>
        /// Looks for something fast going past, a few times a second rather than every frame.
        ///
        /// A pond can hold a dozen ducks and an overlap query per duck per frame is a lot of
        /// broadphase for an ambience effect. Scanning on a stagger — offset per duck by its seed,
        /// so they do not all scan on the same frame — costs a fraction of that and cannot be told
        /// apart at kart speed.
        /// </summary>
        void Scan(double now)
        {
            if (now < _nextScan) return;
            _nextScan = now + 0.2 + (seed & 7) * 0.01;

            if (now < _nextQuackAllowed) return;
            if (quacks == null || quacks.Length == 0) return;

            int found = Physics.OverlapSphereNonAlloc(
                transform.position, hearingRadius, _hits, passerMask,
                QueryTriggerInteraction.Ignore);

            for (int i = 0; i < found; i++)
            {
                Rigidbody rb = _hits[i].attachedRigidbody;
                if (rb == null || rb.isKinematic) continue;
                if (rb.transform.IsChildOf(transform)) continue;
                if (rb.linearVelocity.sqrMagnitude < minPasserSpeed * minPasserSpeed) continue;

                _nextQuackAllowed = now + quackCooldown;

                // Not every duck answers. A raft that all quacks on the same frame sounds like one
                // very loud duck; a ragged answer sounds like a pond.
                var rng = new FarmRandom(seed ^ (int)(now * 7.0));
                if (rng.Value > reactChance) return;

                Quack(now);
                return;
            }
        }

        public void Quack(double now)
        {
            AudioClip clip = quacks[Mathf.Abs(seed + (int)(now * 3.0)) % quacks.Length];
            if (clip == null) return;

            _audio.pitch = 0.88f + (float)((now * 13.0) % 1.0) * 0.3f;
            _audio.PlayOneShot(clip);
            _quackStarted = now;
            _quackLength = clip.length / Mathf.Max(0.1f, _audio.pitch);
        }

        void OnDrawGizmosSelected()
        {
            Vector3 home = Application.isPlaying ? _home : transform.position;

            Gizmos.color = new Color(0.35f, 0.65f, 0.9f, 0.7f);
            Gizmos.DrawWireSphere(home, driftRadius);

            Gizmos.color = new Color(0.95f, 0.75f, 0.25f, 0.35f);
            Gizmos.DrawWireSphere(home, hearingRadius);

            // The water plane, so the depth can be judged rather than trusted.
            Gizmos.color = new Color(0.35f, 0.65f, 0.9f, 0.9f);
            Vector3 surface = new Vector3(home.x, waterLevel, home.z);
            Gizmos.DrawLine(surface + Vector3.left * 0.6f, surface + Vector3.right * 0.6f);
            Gizmos.DrawLine(surface + Vector3.back * 0.6f, surface + Vector3.forward * 0.6f);
        }
    }
}
