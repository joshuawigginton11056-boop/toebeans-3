using UnityEngine;

namespace Volcano
{
    /// <summary>What this emitter is standing in for.</summary>
    public enum PlumeStyle
    {
        /// <summary>Big slow puffs climbing out of the crater and spreading as they cool.</summary>
        Smoke = 0,

        /// <summary>Small bright specks thrown up out of the lava and dying quickly.</summary>
        Embers = 1
    }

    /// <summary>
    /// Configures the ParticleSystem on this object as a volcanic plume, using faceted mesh puffs
    /// rather than soft billboards so the smoke belongs to the same low-poly world as the mountain.
    ///
    /// This writes settings onto the system rather than creating anything, so it is safe to have
    /// running in edit mode and every value is a live preview. Put it on a GameObject that already
    /// has a ParticleSystem — "Add Smoke And Mist" on the volcano's inspector does that for you.
    /// </summary>
    [ExecuteAlways]
    [AddComponentMenu("Volcano/Volcano Smoke")]
    [RequireComponent(typeof(ParticleSystem))]
    public class VolcanoSmoke : MonoBehaviour
    {
        [Header("Style")]
        public PlumeStyle style = PlumeStyle.Smoke;

        [Tooltip("Material for the puffs. A URP particle shader with no texture: the colour comes " +
                 "entirely from the gradient below.")]
        public Material material;

        [Header("Source")]
        [Tooltip("Radius of the ring the plume rises from, in metres. Match it to the lava in the " +
                 "crater, not to the crater itself.")]
        [Range(0.5f, 80f)] public float radius = 15f;

        [Tooltip("Puffs per second. Long-lived puffs at a low rate read far better than a fire hose " +
                 "of short ones, and cost a fraction as much.")]
        [Range(0.2f, 120f)] public float rate = 7f;

        [Header("Motion")]
        [Tooltip("How fast a puff leaves the vent, in metres per second.")]
        [Range(0.2f, 60f)] public float riseSpeed = 7f;

        [Tooltip("How long a puff lasts, in seconds. Together with the rise speed this is what sets " +
                 "how tall the column gets.")]
        [Range(0.5f, 60f)] public float lifetime = 15f;

        [Tooltip("How much the column is pushed sideways as it climbs, in metres per second. A dead " +
                 "vertical plume looks like a chimney.")]
        [Range(0f, 20f)] public float drift = 2.2f;

        [Tooltip("Direction the drift pushes, in degrees. Point it downwind of whatever else is " +
                 "moving in the scene.")]
        [Range(0f, 360f)] public float driftHeading = 200f;

        [Tooltip("How much the puffs churn on the way up.")]
        [Range(0f, 8f)] public float turbulence = 1.6f;

        [Header("Size")]
        [Tooltip("How wide a puff is when it appears, in metres.")]
        [Range(0.2f, 60f)] public float startSize = 9f;

        [Tooltip("How much it has swollen by the time it dies.")]
        [Range(1f, 12f)] public float growth = 3.4f;

        [Header("Colour")]
        [Tooltip("Colour and opacity over a puff's life. The first stop is the one glowing in the " +
                 "crater mouth and the last should fade to nothing, or the column ends on a hard edge.")]
        [SerializeField] Gradient tint = new Gradient();

        // A fresh Gradient is not empty, it is two white keys, so "has anyone set this?" cannot be
        // answered by looking at the gradient. Hence the flag.
        [SerializeField, HideInInspector] bool tintSet;

        [Header("Detail")]
        [Tooltip("How many facets a puff has. 0 is a bare 20-face lump; 1 is 80 and is as far as " +
                 "this is worth taking on something you can see through.")]
        [Range(0, 2)] public int puffDetail = 1;

        [Range(0f, 0.6f)] public float lumpiness = 0.3f;

        [SerializeField] int seed = 7;

        Mesh _puff;

        void Reset()
        {
            ApplyDefaultTint();
        }

        void OnEnable()
        {
            if (!tintSet) ApplyDefaultTint();
            Rebuild();
        }

        /// <summary>
        /// Resets the gradient to the one that suits the current style. The style is normally set
        /// after the component is added, by which point Reset has already run and picked the
        /// default for whatever style it started as, so anything changing the style has to call this.
        /// </summary>
        public void ApplyDefaultTint()
        {
            tint = DefaultTint(style);
            tintSet = true;
        }

        void OnValidate()
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.delayCall += () =>
            {
                if (this == null) return;
                Rebuild();
            };
#else
            Rebuild();
#endif
        }

        void OnDestroy()
        {
            if (_puff == null) return;
#if UNITY_EDITOR
            if (Application.isPlaying) Destroy(_puff); else DestroyImmediate(_puff);
#else
            Destroy(_puff);
#endif
            _puff = null;
        }

        /// <summary>Writes every setting onto the ParticleSystem on this object.</summary>
        public void Rebuild()
        {
            var ps = GetComponent<ParticleSystem>();
            if (ps == null) return;

            bool embers = style == PlumeStyle.Embers;

            if (_puff == null) _puff = LowPolyPuff.Build(seed, embers ? 0 : puffDetail, lumpiness);

            // Deliberately not setting main.duration: on a looping system it changes nothing worth
            // having, and writing it while the system is playing is an error every rebuild.
            var main = ps.main;
            main.loop = true;
            main.startLifetime = new ParticleSystem.MinMaxCurve(lifetime * 0.65f, lifetime * 1.15f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(riseSpeed * 0.6f, riseSpeed * 1.3f);
            main.startSize = new ParticleSystem.MinMaxCurve(startSize * 0.6f, startSize * 1.25f);
            main.startRotation3D = true;
            main.startRotationX = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            main.startRotationY = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            main.startRotationZ = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            main.startColor = Color.white;
            main.gravityModifier = embers ? 0.35f : -0.02f;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = Mathf.Max(16, Mathf.CeilToInt(rate * lifetime * 1.6f));
            main.playOnAwake = true;

            var emission = ps.emission;
            emission.enabled = true;
            emission.rateOverTime = rate;

            // A ring rather than a disc: the middle of a crater is where the lava is, and puffs
            // starting there would be born inside the pool mesh.
            var shape = ps.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = embers ? 22f : 9f;
            shape.radius = radius;
            shape.radiusThickness = embers ? 1f : 0.55f;
            shape.rotation = new Vector3(-90f, 0f, 0f); // cone points up
            shape.position = Vector3.zero;

            var vel = ps.velocityOverLifetime;
            vel.enabled = drift > 0.001f;
            float rad = driftHeading * Mathf.Deg2Rad;
            vel.space = ParticleSystemSimulationSpace.World;
            vel.x = new ParticleSystem.MinMaxCurve(Mathf.Cos(rad) * drift);
            vel.z = new ParticleSystem.MinMaxCurve(Mathf.Sin(rad) * drift);
            vel.y = new ParticleSystem.MinMaxCurve(0f);

            var noise = ps.noise;
            noise.enabled = turbulence > 0.001f;
            noise.strength = turbulence;
            noise.frequency = 0.12f;
            noise.scrollSpeed = 0.35f;
            noise.damping = true;
            noise.quality = ParticleSystemNoiseQuality.Medium;

            var size = ps.sizeOverLifetime;
            size.enabled = true;
            var grow = new AnimationCurve();
            if (embers)
            {
                // An ember is thrown out at full size and burns down to nothing.
                grow.AddKey(0f, 1f);
                grow.AddKey(1f, 0.1f);
            }
            else
            {
                grow.AddKey(0f, 1f / Mathf.Max(1f, growth));
                grow.AddKey(0.35f, 0.55f);
                grow.AddKey(1f, 1f);
            }
            size.size = new ParticleSystem.MinMaxCurve(Mathf.Max(1f, growth), grow);

            var rot = ps.rotationOverLifetime;
            rot.enabled = !embers;
            rot.separateAxes = true;
            rot.x = new ParticleSystem.MinMaxCurve(-0.25f, 0.25f);
            rot.y = new ParticleSystem.MinMaxCurve(-0.35f, 0.35f);
            rot.z = new ParticleSystem.MinMaxCurve(-0.25f, 0.25f);

            var col = ps.colorOverLifetime;
            col.enabled = true;
            col.color = new ParticleSystem.MinMaxGradient(tint != null && tint.colorKeys.Length > 0
                                                          ? tint : DefaultTint(style));

            var renderer = GetComponent<ParticleSystemRenderer>();
            if (renderer != null)
            {
                renderer.renderMode = ParticleSystemRenderMode.Mesh;
                renderer.mesh = _puff;
                renderer.alignment = ParticleSystemRenderSpace.World;
                renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                renderer.receiveShadows = false;
                if (material != null) renderer.sharedMaterial = material;

                // Drawn back to front against everything else transparent in the scene. Without it
                // the column pops in and out through the lava it is rising off.
                renderer.sortMode = ParticleSystemSortMode.Distance;
            }
        }

        /// <summary>
        /// Warm at the mouth, ash grey in the middle, gone by the end. The warm start is doing real
        /// work: it is what makes the base of the column read as lit from below by the crater
        /// without a single light being added.
        /// </summary>
        public static Gradient DefaultTint(PlumeStyle style)
        {
            var g = new Gradient();

            if (style == PlumeStyle.Embers)
            {
                g.SetKeys(
                    new[]
                    {
                        new GradientColorKey(new Color(1f, 0.85f, 0.45f), 0f),
                        new GradientColorKey(new Color(1f, 0.42f, 0.08f), 0.45f),
                        new GradientColorKey(new Color(0.45f, 0.09f, 0.02f), 1f)
                    },
                    new[]
                    {
                        new GradientAlphaKey(1f, 0f),
                        new GradientAlphaKey(0.85f, 0.6f),
                        new GradientAlphaKey(0f, 1f)
                    });
                return g;
            }

            g.SetKeys(
                new[]
                {
                    new GradientColorKey(new Color(0.85f, 0.34f, 0.10f), 0f),
                    new GradientColorKey(new Color(0.34f, 0.24f, 0.22f), 0.22f),
                    new GradientColorKey(new Color(0.19f, 0.17f, 0.18f), 0.6f),
                    new GradientColorKey(new Color(0.14f, 0.13f, 0.15f), 1f)
                },
                // Low, because puffs are overlapping solids and opacity accumulates. At 0.62 a
                // column this size is a grey lump sitting on the summit rather than smoke; the
                // depth comes from many nearly-clear layers, not from a few opaque ones.
                new[]
                {
                    new GradientAlphaKey(0f, 0f),
                    new GradientAlphaKey(0.30f, 0.12f),
                    new GradientAlphaKey(0.18f, 0.55f),
                    new GradientAlphaKey(0f, 1f)
                });
            return g;
        }
    }
}
