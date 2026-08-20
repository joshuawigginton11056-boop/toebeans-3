using System.Collections.Generic;
using UnityEngine;

namespace Barriers
{
    /// <summary>
    /// Recolours a barrier section without touching its material asset.
    ///
    /// The generated barrier models are built around two paintable slots — Barrier_Paint for the
    /// large painted surface, Barrier_Accent for the reflector strips — plus whatever structural
    /// material each section uses. This drives all three through a MaterialPropertyBlock, so a
    /// hundred sections in six colours are still six materials and still instance together. Nothing
    /// here creates a material at runtime, and nothing here leaks a material into the project.
    ///
    /// Put it on the prefab root and every renderer under it is covered.
    /// </summary>
    [ExecuteAlways]
    [AddComponentMenu("Barriers/Barrier Tint")]
    [DisallowMultipleComponent]
    public class BarrierTint : MonoBehaviour
    {
        /// <summary>Material name fragments treated as the structural slot.</summary>
        static readonly string[] StructureNames =
            { "Barrier_Metal", "Barrier_Wood", "Barrier_Concrete", "Barrier_Rubber" };

        static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        static readonly int ColorId = Shader.PropertyToID("_Color");
        static readonly int EmissionId = Shader.PropertyToID("_EmissionColor");

        [Header("Paint")]
        [Tooltip("Recolour the large painted surface — the rail face, the painted tyres, the top " +
                 "cap of the concrete block.")]
        public bool tintPaint = true;

        [ColorUsage(false)] public Color paint = new Color(0.60f, 0.26f, 0.09f, 1f);

        [Header("Accent")]
        [Tooltip("Recolour the reflector strips and marker plates.")]
        public bool tintAccent = false;

        [ColorUsage(false)] public Color accent = new Color(0.72f, 0.70f, 0.64f, 1f);

        [Tooltip("How hard the accent glows. The scene is dark and the reflectors are the part a " +
                 "driver actually reads at speed, so a little goes a long way. 0 is unlit paint.")]
        [Range(0f, 8f)] public float accentGlow = 0f;

        [Header("Structure")]
        [Tooltip("Recolour the posts, timber, concrete and rubber together. Off by default — the " +
                 "structural colours are what tie the set to the rest of the scene.")]
        public bool tintStructure = false;

        [ColorUsage(false)] public Color structure = new Color(0.20f, 0.21f, 0.24f, 1f);

        [Header("Variation")]
        [Tooltip("Brightness wobble between one placed section and the next, so a long run does " +
                 "not read as one flat colour. Derived from the position, so it is stable — the " +
                 "same section in the same place is always the same shade.")]
        [Range(0f, 0.4f)] public float variation = 0.06f;

        static MaterialPropertyBlock _block;
        readonly List<Renderer> _renderers = new List<Renderer>();

        void OnEnable() { Apply(); }

        void OnValidate()
        {
            // OnValidate can land mid-deserialisation, where touching renderers is not allowed.
            if (!isActiveAndEnabled) return;
            Apply();
        }

        void OnTransformParentChanged() { Apply(); }

        /// <summary>
        /// Pushes the current colours onto every renderer underneath. Safe to call as often as you
        /// like; it allocates nothing after the first call.
        /// </summary>
        public void Apply()
        {
            _block ??= new MaterialPropertyBlock();

            GetComponentsInChildren(true, _renderers);
            float shade = Shade();

            for (int r = 0; r < _renderers.Count; r++)
            {
                Renderer rend = _renderers[r];
                if (rend == null) continue;

                Material[] mats = rend.sharedMaterials;
                for (int i = 0; i < mats.Length; i++)
                {
                    Material m = mats[i];
                    if (m == null) continue;

                    if (!Resolve(m, out Color colour, out float glow)) continue;

                    colour = Scale(colour, shade);

                    rend.GetPropertyBlock(_block, i);
                    _block.SetColor(BaseColorId, colour);
                    _block.SetColor(ColorId, colour);
                    _block.SetColor(EmissionId, colour * glow);
                    rend.SetPropertyBlock(_block, i);
                }
            }

            _renderers.Clear();
        }

        /// <summary>Which slot this material belongs to, and what it should become.</summary>
        bool Resolve(Material m, out Color colour, out float glow)
        {
            colour = Color.white;
            glow = 0f;
            string name = m.name;

            if (name.Contains("Barrier_Paint"))
            {
                if (!tintPaint) return false;
                colour = paint;
                return true;
            }

            if (name.Contains("Barrier_Accent"))
            {
                if (!tintAccent && accentGlow <= 0f) return false;
                // Glowing an untinted accent must not flatten it to white, so fall back to the
                // material's own colour and drive only the emission.
                colour = tintAccent ? accent : BaseOf(m);
                glow = accentGlow;
                return true;
            }

            if (tintStructure)
            {
                for (int i = 0; i < StructureNames.Length; i++)
                {
                    if (name.Contains(StructureNames[i]))
                    {
                        colour = structure;
                        return true;
                    }
                }
            }

            return false;
        }

        static Color BaseOf(Material m)
        {
            if (m.HasProperty(BaseColorId)) return m.GetColor(BaseColorId);
            if (m.HasProperty(ColorId)) return m.GetColor(ColorId);
            return Color.white;
        }

        /// <summary>
        /// A stable per-section brightness multiplier. Hashed off the rounded world position so it
        /// survives a rebuild of the line, and so two sections that end up in the same spot match.
        /// </summary>
        float Shade()
        {
            if (variation <= 0f) return 1f;

            Vector3 p = transform.position;
            int h = Mathf.RoundToInt(p.x * 4f) * 73856093
                  ^ Mathf.RoundToInt(p.y * 4f) * 19349663
                  ^ Mathf.RoundToInt(p.z * 4f) * 83492791;
            float t = ((h & 0x7FFFFFFF) % 10000) / 10000f;      // 0..1
            return 1f + (t * 2f - 1f) * variation;
        }

        static Color Scale(Color c, float k)
        {
            return new Color(c.r * k, c.g * k, c.b * k, c.a);
        }
    }
}
