using UnityEngine;

namespace Volcano
{
    /// <summary>
    /// Hangs a drifting fog off the surface of a lava mesh.
    ///
    /// The whole trick is where the particles are born. Rather than guessing at a box or a line, the
    /// system emits from the triangles of the lava mesh itself, restricted to the molten submesh, so
    /// the mist sits exactly on the glowing parts and follows the river wherever it was routed. Move
    /// the lava, retune it, reroute it: the mist comes with it and there is nothing to keep in sync.
    ///
    /// This writes settings onto the ParticleSystem on this object rather than creating anything, so
    /// it is safe running in edit mode.
    /// </summary>
    [ExecuteAlways]
    [AddComponentMenu("Volcano/Lava Mist")]
    [RequireComponent(typeof(ParticleSystem))]
    public class LavaMist : MonoBehaviour
    {
        [Header("Source")]
        [Tooltip("The lava the mist comes off. Its mesh is used as the emission shape.")]
        public MeshRenderer source;

        [Tooltip("Which material slot on that renderer is the molten one, so mist only comes off " +
                 "lava rather than off the cooled crust and the rock as well.\n\n" +
                 "Lava Pond and Lava Flow put molten in slot 2. The Volcano puts it in slot 3.")]
        [Range(0, 7)] public int moltenSubmesh = 2;

        [Tooltip("Emit from the whole mesh instead of one slot. Use this when the source has no " +
                 "separate molten submesh.")]
        public bool wholeMesh = false;

        [Header("Density")]
        [Tooltip("Wisps per second. This is a fog bank, not a smoke machine: a handful of big, " +
                 "long-lived, nearly transparent puffs costs almost nothing and reads better than " +
                 "a cloud of small ones.")]
        [Range(0.2f, 200f)] public float rate = 26f;

        [Tooltip("How long a wisp lasts, in seconds.")]
        [Range(0.5f, 40f)] public float lifetime = 9f;

        [Header("Motion")]
        [Tooltip("How fast the mist lifts off the surface, in metres per second. Keep it low: heat " +
                 "haze creeps, it does not billow.")]
        [Range(0f, 12f)] public float rise = 0.85f;

        [Tooltip("How fast it spreads sideways, in metres per second.")]
        [Range(0f, 12f)] public float spread = 1.3f;

        [Tooltip("How much the mist churns.")]
        [Range(0f, 4f)] public float turbulence = 0.5f;

        [Header("Size")]
        [Tooltip("How wide a wisp is, in metres.")]
        [Range(0.3f, 40f)] public float width = 7f;

        [Tooltip("How tall it is, as a fraction of its width. Well under 1: this is a layer lying " +
                 "on the lava, and round puffs read as steam from a kettle.")]
        [Range(0.05f, 1f)] public float flatness = 0.3f;

        [Tooltip("How much a wisp swells over its life.")]
        [Range(1f, 8f)] public float growth = 2.1f;

        [Header("Colour")]
        [Tooltip("Colour and opacity over a wisp's life. Starting warm and fading to grey is what " +
                 "makes the fog look lit by the lava under it rather than painted on top of it.")]
        [SerializeField] Gradient tint = new Gradient();

        // A fresh Gradient is not empty, it is two white keys, so "has anyone set this?" cannot be
        // answered by looking at the gradient. Hence the flag.
        [SerializeField, HideInInspector] bool tintSet;

        [Header("Detail")]
        [Tooltip("Material for the wisps. A URP particle shader with no texture.")]
        public Material material;

        [Range(0, 2)] public int puffDetail = 0;
        [Range(0f, 0.6f)] public float lumpiness = 0.34f;
        [SerializeField] int seed = 21;

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

        /// <summary>Resets the gradient to the built-in one.</summary>
        public void ApplyDefaultTint()
        {
            tint = DefaultTint();
            tintSet = true;
        }

        /// <summary>
        /// Sets the gradient, for a caller that wants a different look from the built-in one. Null
        /// puts the default back rather than leaving the system with no colour at all.
        /// </summary>
        public void SetTint(Gradient gradient)
        {
            tint = gradient ?? DefaultTint();
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

            if (_puff == null) _puff = LowPolyPuff.Build(seed, puffDetail, lumpiness);

            // Deliberately not setting main.duration: on a looping system it changes nothing worth
            // having, and writing it while the system is playing is an error every rebuild.
            var main = ps.main;
            main.loop = true;
            main.startLifetime = new ParticleSystem.MinMaxCurve(lifetime * 0.6f, lifetime * 1.25f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(rise * 0.4f, rise * 1.2f);
            main.startSize3D = true;
            main.startSizeX = new ParticleSystem.MinMaxCurve(width * 0.55f, width * 1.3f);
            main.startSizeY = new ParticleSystem.MinMaxCurve(width * flatness * 0.6f, width * flatness * 1.2f);
            main.startSizeZ = new ParticleSystem.MinMaxCurve(width * 0.55f, width * 1.3f);
            main.startRotation3D = true;
            main.startRotationX = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            main.startRotationY = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            main.startRotationZ = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            main.startColor = Color.white;
            main.gravityModifier = -0.01f;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = Mathf.Max(16, Mathf.CeilToInt(rate * lifetime * 1.5f));
            main.playOnAwake = true;

            // The emitter has to carry the same scale as the lava it emits from, or the shape lands
            // somewhere other than the mesh. Scaling the shape but not the particles is what lets it
            // do that while every setting above stays in metres: LobbyIsland's Lava Pond is at 4x,
            // and under the default mode a 7 m wisp on it would come out 28 m across.
            main.scalingMode = ParticleSystemScalingMode.Shape;

            var emission = ps.emission;
            emission.enabled = true;
            emission.rateOverTime = rate;

            var shape = ps.shape;
            shape.enabled = true;
            if (source != null)
            {
                shape.shapeType = ParticleSystemShapeType.MeshRenderer;
                shape.meshRenderer = source;
                shape.meshShapeType = ParticleSystemMeshShapeType.Triangle;

                // The one setting this component exists for: emit off the lava, not off the crust
                // and the banks around it.
                shape.useMeshMaterialIndex = !wholeMesh;
                shape.meshMaterialIndex = moltenSubmesh;
            }
            else
            {
                // No source yet. A small sphere is a visible "nothing is hooked up" rather than an
                // emitter that silently produces nothing.
                shape.shapeType = ParticleSystemShapeType.Sphere;
                shape.radius = 2f;
            }

            var vel = ps.velocityOverLifetime;
            vel.enabled = true;
            vel.space = ParticleSystemSimulationSpace.Local;
            vel.x = new ParticleSystem.MinMaxCurve(-spread, spread);
            vel.z = new ParticleSystem.MinMaxCurve(-spread, spread);
            vel.y = new ParticleSystem.MinMaxCurve(rise * 0.2f, rise * 0.7f);

            var noise = ps.noise;
            noise.enabled = turbulence > 0.001f;
            noise.strength = turbulence;
            noise.frequency = 0.2f;
            noise.scrollSpeed = 0.2f;
            noise.damping = true;
            noise.quality = ParticleSystemNoiseQuality.Low;

            var size = ps.sizeOverLifetime;
            size.enabled = true;
            var grow = new AnimationCurve();
            grow.AddKey(0f, 0.35f);
            grow.AddKey(0.4f, 0.8f);
            grow.AddKey(1f, 1f);
            size.size = new ParticleSystem.MinMaxCurve(Mathf.Max(1f, growth), grow);

            var rot = ps.rotationOverLifetime;
            rot.enabled = true;
            rot.separateAxes = true;
            rot.x = new ParticleSystem.MinMaxCurve(-0.12f, 0.12f);
            rot.y = new ParticleSystem.MinMaxCurve(-0.3f, 0.3f);
            rot.z = new ParticleSystem.MinMaxCurve(-0.12f, 0.12f);

            var col = ps.colorOverLifetime;
            col.enabled = true;
            col.color = new ParticleSystem.MinMaxGradient(
                tint != null && tint.colorKeys.Length > 0 ? tint : DefaultTint());

            var renderer = GetComponent<ParticleSystemRenderer>();
            if (renderer != null)
            {
                renderer.renderMode = ParticleSystemRenderMode.Mesh;
                renderer.mesh = _puff;
                renderer.alignment = ParticleSystemRenderSpace.World;
                renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                renderer.receiveShadows = false;
                renderer.sortMode = ParticleSystemSortMode.Distance;
                if (material != null) renderer.sharedMaterial = material;
            }
        }

        /// <summary>Glowing where it leaves the lava, grey and gone by the time it has drifted off.</summary>
        public static Gradient DefaultTint()
        {
            var g = new Gradient();
            g.SetKeys(
                new[]
                {
                    new GradientColorKey(new Color(1f, 0.46f, 0.13f), 0f),
                    new GradientColorKey(new Color(0.66f, 0.30f, 0.15f), 0.3f),
                    new GradientColorKey(new Color(0.32f, 0.26f, 0.26f), 0.75f),
                    new GradientColorKey(new Color(0.22f, 0.20f, 0.22f), 1f)
                },
                // Very low, and deliberately so. Mist is drawn as overlapping solids, so opacity
                // accumulates: a dozen wisps at 0.3 stack into an opaque orange sheet over the
                // whole mountain long before any single one of them looks too strong.
                new[]
                {
                    new GradientAlphaKey(0f, 0f),
                    new GradientAlphaKey(0.13f, 0.18f),
                    new GradientAlphaKey(0.08f, 0.6f),
                    new GradientAlphaKey(0f, 1f)
                });
            return g;
        }
    }
}
