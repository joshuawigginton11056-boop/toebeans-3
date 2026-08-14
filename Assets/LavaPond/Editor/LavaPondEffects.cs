using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using Volcano;

namespace LavaPond.EditorTools
{
    /// <summary>What to hang off a pond. Each one is a separate emitter, so they stack.</summary>
    public enum PondEffect
    {
        /// <summary>A thin shimmer lying on the lava. Barely there, and the cheapest of the four.</summary>
        HeatHaze = 0,

        /// <summary>Slow grey wisps rolling off the surface and spilling over the rim.</summary>
        SteamBank = 1,

        /// <summary>A column climbing off the pond, or out of the vent if it has one.</summary>
        SmokeColumn = 2,

        /// <summary>Bright specks spat up out of the lava, dying on the way down.</summary>
        Embers = 3
    }

    /// <summary>
    /// The particles that hang off a lava pond, built in place off the pond's own numbers.
    ///
    /// The emitters themselves are the Volcano package's <see cref="LavaMist"/> and
    /// <see cref="VolcanoSmoke"/>. They are not volcano-specific — the mist already emits from a
    /// mesh's molten submesh and documents the pond's slot 2 as one of its cases — so this sizes
    /// and colours them for a pond rather than growing a second copy that would drift out of step.
    ///
    /// Each effect is rebuilt in place rather than added again, so pressing a button twice tidies
    /// up instead of stacking a second copy.
    /// </summary>
    public static class LavaPondEffects
    {
        const string GroupName = "Pond Effects";

        /// <summary>Builds one effect on the pond and returns the object carrying it.</summary>
        public static GameObject Add(LavaPondGenerator pond, PondEffect effect)
        {
            if (pond == null) return null;

            // Everything is sized in world metres, so the pond's scale has to come into it: on
            // LobbyIsland the pond is a 12 m mesh at 4x, which is a 48 m pool.
            float scale = UniformScale(pond.transform);
            float radius = Mathf.Max(0.5f, pond.Settings.radius * scale);

            Vector3 ventMouth;
            float ventRadius;
            bool hasVent = pond.TryGetVentPoint(out ventMouth, out ventRadius) && ventRadius > 0.05f;

            Transform group = Child(pond.transform, GroupName).transform;

            switch (effect)
            {
                case PondEffect.HeatHaze:
                    return BuildMist(group, pond, "Heat Haze", radius, false);

                case PondEffect.SteamBank:
                    return BuildMist(group, pond, "Steam Bank", radius, true);

                case PondEffect.SmokeColumn:
                    return BuildPlume(group, pond, "Smoke Column", PlumeStyle.Smoke, radius, scale,
                                      hasVent, ventMouth, ventRadius);

                case PondEffect.Embers:
                    return BuildPlume(group, pond, "Embers", PlumeStyle.Embers, radius, scale,
                                      hasVent, ventMouth, ventRadius);
            }

            return null;
        }

        /// <summary>Removes every effect from the pond.</summary>
        public static void RemoveAll(LavaPondGenerator pond)
        {
            if (pond == null) return;
            Transform group = pond.transform.Find(GroupName);
            if (group == null) return;
            Undo.DestroyObjectImmediate(group.gameObject);
        }

        /// <summary>True when the pond already has this effect on it.</summary>
        public static bool Has(LavaPondGenerator pond, PondEffect effect)
        {
            if (pond == null) return false;
            Transform group = pond.transform.Find(GroupName);
            return group != null && group.Find(NameOf(effect)) != null;
        }

        public static string NameOf(PondEffect effect)
        {
            switch (effect)
            {
                case PondEffect.HeatHaze: return "Heat Haze";
                case PondEffect.SteamBank: return "Steam Bank";
                case PondEffect.SmokeColumn: return "Smoke Column";
                default: return "Embers";
            }
        }

        // ------------------------------------------------------------------ mist

        /// <summary>
        /// Fog coming off the lava itself. The emitter sits exactly on the pond's transform, scale
        /// included, because the shape module places particles from the pond's mesh: line the two
        /// up and the wisps land on the glowing parts wherever the pond has been moved or scaled.
        /// </summary>
        static GameObject BuildMist(Transform group, LavaPondGenerator pond, string name,
                                    float radius, bool billow)
        {
            var source = pond.GetComponent<MeshRenderer>();
            if (source == null)
            {
                Debug.LogWarning("The pond has no Mesh Renderer for the mist to emit from.", pond);
                return null;
            }

            GameObject go = Child(group, name);
            go.transform.SetPositionAndRotation(pond.transform.position, pond.transform.rotation);
            go.transform.localScale = Vector3.one;

            Ensure<ParticleSystem>(go);
            var mist = Ensure<LavaMist>(go);

            mist.source = source;
            mist.material = LavaPondParticles.MistMaterial();
            mist.moltenSubmesh = 2;      // 0 dark crust, 1 warm crust, 2 molten, 3 rock
            mist.wholeMesh = false;

            if (billow)
            {
                // Big, slow and long-lived. Rates stay low on purpose: wisps are overlapping solids,
                // so turning this up does not thicken the fog so much as replace the pond with it.
                //
                // Measured on LobbyIsland's 96 m pool, where 0.55 gave wisps averaging 44 m across:
                // two of them covered the pond and the bank read as a lid rather than as fog. A
                // quarter of the radius is enough of them to have depth.
                mist.width = radius * 0.26f;
                mist.flatness = 0.34f;
                mist.rate = 8f;
                mist.lifetime = 13f;
                mist.rise = 0.9f;
                mist.spread = 1.5f;
                mist.turbulence = 0.6f;
                mist.growth = 2.3f;
                mist.puffDetail = 1;
                mist.SetTint(SteamTint());
            }
            else
            {
                // Small, fast and nearly clear: the air over the lava moving, not smoke.
                mist.width = radius * 0.22f;
                mist.flatness = 0.16f;
                mist.rate = 7f;
                mist.lifetime = 5f;
                mist.rise = 1.4f;
                mist.spread = 0.5f;
                mist.turbulence = 0.35f;
                mist.growth = 1.5f;
                mist.puffDetail = 0;
                mist.SetTint(HazeTint());
            }

            mist.Rebuild();
            EditorUtility.SetDirty(mist);
            return go;
        }

        // ------------------------------------------------------------------ plumes

        /// <summary>
        /// A column or a shower of embers, out of the vent when the pond has one and off the whole
        /// surface when it does not.
        ///
        /// This one is deliberately <em>not</em> left at the pond's scale. It spawns from a cone
        /// rather than from the mesh, so nothing has to line up with the pond's geometry, and an
        /// unscaled emitter means every number below is the metres it says it is.
        /// </summary>
        static GameObject BuildPlume(Transform group, LavaPondGenerator pond, string name,
                                     PlumeStyle style, float radius, float scale,
                                     bool hasVent, Vector3 ventMouth, float ventRadius)
        {
            GameObject go = Child(group, name);
            go.transform.position = hasVent ? ventMouth : pond.transform.position;
            go.transform.rotation = Quaternion.identity;
            go.transform.localScale = Vector3.one / Mathf.Max(0.0001f, scale);

            Ensure<ParticleSystem>(go);
            var smoke = Ensure<VolcanoSmoke>(go);

            smoke.style = style;
            smoke.material = style == PlumeStyle.Embers
                ? LavaPondParticles.EmberMaterial()
                : LavaPondParticles.SmokeMaterial();
            smoke.ApplyDefaultTint();

            // A vent is a chimney and the open pond is a whole hot surface, so the source is a
            // narrow ring in one case and most of the pool in the other.
            float source = hasVent ? ventRadius * 1.1f : radius * 0.7f;

            if (style == PlumeStyle.Embers)
            {
                smoke.radius = source;
                smoke.rate = hasVent ? 20f : 12f;
                smoke.riseSpeed = Mathf.Clamp(radius * 0.5f, 6f, 18f);
                smoke.lifetime = 3.5f;
                smoke.startSize = Mathf.Clamp(radius * 0.02f, 0.25f, 1.2f);
                smoke.growth = 1f;
                smoke.drift = 1.2f;
                smoke.turbulence = 2.2f;
            }
            else
            {
                smoke.radius = source;
                smoke.rate = 5f;
                smoke.riseSpeed = Mathf.Clamp(radius * 0.28f, 3f, 12f);
                smoke.lifetime = 14f;
                // A puff has to be smaller than the column it is building, or the whole thing is
                // one lump: measured on the 96 m pool, half the radius put 38 m puffs inside a
                // 30 m column. The vent case can be smaller again — it is coming out of a hole.
                smoke.startSize = Mathf.Clamp(radius * (hasVent ? 0.22f : 0.28f), 1.5f, 16f);
                smoke.growth = 3f;
                smoke.drift = 2.2f;
                smoke.turbulence = 1.5f;
            }

            smoke.driftHeading = DriftHeading(pond.Settings.flowAngle);
            smoke.Rebuild();
            EditorUtility.SetDirty(smoke);
            return go;
        }

        /// <summary>
        /// Pushes the column the way the lava is already travelling, so the pond and its smoke
        /// agree about which way the air is moving.
        ///
        /// The two angles are measured differently and it is worth being explicit about it: the
        /// pond's flow angle is clockwise from world +Z, and the plume's heading is the ordinary
        /// atan2 angle of (x, z). Those meet at 90 - flow.
        /// </summary>
        static float DriftHeading(float flowAngleDegrees)
        {
            float h = 90f - flowAngleDegrees;
            h %= 360f;
            return h < 0f ? h + 360f : h;
        }

        // ------------------------------------------------------------------ tints

        /// <summary>Grey steam, lit orange only where it leaves the surface.</summary>
        static Gradient SteamTint()
        {
            var g = new Gradient();
            g.SetKeys(
                new[]
                {
                    new GradientColorKey(new Color(0.95f, 0.42f, 0.12f), 0f),
                    new GradientColorKey(new Color(0.52f, 0.31f, 0.24f), 0.28f),
                    new GradientColorKey(new Color(0.34f, 0.32f, 0.33f), 0.7f),
                    new GradientColorKey(new Color(0.26f, 0.25f, 0.27f), 1f)
                },
                // Opacity accumulates across overlapping wisps, so a bank this size is built out of
                // many nearly-clear layers rather than a few solid ones.
                new[]
                {
                    new GradientAlphaKey(0f, 0f),
                    new GradientAlphaKey(0.13f, 0.2f),
                    new GradientAlphaKey(0.08f, 0.62f),
                    new GradientAlphaKey(0f, 1f)
                });
            return g;
        }

        /// <summary>Hot air rather than smoke: orange throughout, and almost invisible.</summary>
        static Gradient HazeTint()
        {
            var g = new Gradient();
            g.SetKeys(
                new[]
                {
                    new GradientColorKey(new Color(1f, 0.52f, 0.16f), 0f),
                    new GradientColorKey(new Color(0.86f, 0.36f, 0.12f), 0.5f),
                    new GradientColorKey(new Color(0.55f, 0.27f, 0.16f), 1f)
                },
                new[]
                {
                    new GradientAlphaKey(0f, 0f),
                    new GradientAlphaKey(0.07f, 0.25f),
                    new GradientAlphaKey(0.05f, 0.6f),
                    new GradientAlphaKey(0f, 1f)
                });
            return g;
        }

        // ------------------------------------------------------------------ helpers

        static float UniformScale(Transform t)
        {
            Vector3 s = t.lossyScale;
            return Mathf.Max(0.0001f, Mathf.Max(Mathf.Abs(s.x), Mathf.Abs(s.z)));
        }

        static GameObject Child(Transform parent, string name)
        {
            Transform existing = parent.Find(name);
            if (existing != null) return existing.gameObject;

            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            Undo.RegisterCreatedObjectUndo(go, "Add " + name);
            return go;
        }

        static T Ensure<T>(GameObject go) where T : Component
        {
            T component = go.GetComponent<T>();
            return component != null ? component : go.AddComponent<T>();
        }
    }

    /// <summary>
    /// The pond's particle materials, written into the project on first use so the set always
    /// matches whichever render pipeline the project is actually on.
    ///
    /// Kept here rather than borrowed from the Volcano package: a pond standing on its own in some
    /// other scene should not pull VLC_ assets in behind it.
    /// </summary>
    public static class LavaPondParticles
    {
        const string RootFolder = "Assets/LavaPond";
        const string MaterialFolder = RootFolder + "/Materials";

        public static Material SmokeMaterial() { return Particle("LP_Smoke", false); }
        public static Material MistMaterial() { return Particle("LP_Mist", false); }
        public static Material EmberMaterial() { return Particle("LP_Ember_Particle", true); }

        /// <summary>
        /// A textureless transparent particle material. No texture is the point: the puffs are
        /// faceted geometry, and a cloud texture on top of them would put photographic detail on
        /// the one thing in the scene that is meant to read as facets.
        /// </summary>
        static Material Particle(string name, bool additive)
        {
            if (!AssetDatabase.IsValidFolder(RootFolder))
                AssetDatabase.CreateFolder("Assets", "LavaPond");
            if (!AssetDatabase.IsValidFolder(MaterialFolder))
                AssetDatabase.CreateFolder(RootFolder, "Materials");

            string path = MaterialFolder + "/" + name + ".mat";
            var existing = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (existing != null) return existing;

            Shader shader = Shader.Find("Universal Render Pipeline/Particles/Unlit");
            if (shader == null) shader = Shader.Find("Universal Render Pipeline/Unlit");
            if (shader == null) return null;

            var material = new Material(shader) { name = name };
            if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", Color.white);

            // Written out by hand because the material is being created from script: the shader's
            // own inspector is what normally sets these, and it never runs here.
            material.SetOverrideTag("RenderType", "Transparent");
            if (material.HasProperty("_Surface")) material.SetFloat("_Surface", 1f);
            if (material.HasProperty("_Blend")) material.SetFloat("_Blend", additive ? 2f : 0f);
            if (material.HasProperty("_ZWrite")) material.SetFloat("_ZWrite", 0f);
            if (material.HasProperty("_AlphaClip")) material.SetFloat("_AlphaClip", 0f);
            if (material.HasProperty("_Cull")) material.SetFloat("_Cull", (float)CullMode.Back);
            if (material.HasProperty("_ColorMode")) material.SetFloat("_ColorMode", 0f); // multiply

            material.SetInt("_SrcBlend", (int)BlendMode.SrcAlpha);
            material.SetInt("_DstBlend", additive ? (int)BlendMode.One : (int)BlendMode.OneMinusSrcAlpha);

            material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            material.DisableKeyword("_ALPHATEST_ON");
            material.DisableKeyword("_ALPHAPREMULTIPLY_ON");
            material.renderQueue = (int)RenderQueue.Transparent;

            AssetDatabase.CreateAsset(material, path);
            return material;
        }
    }
}
