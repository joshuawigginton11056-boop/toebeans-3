using System.Collections.Generic;
using UnityEngine;

namespace Toebeans.Karting
{
    /// <summary>
    /// How one kind of ground behaves under a tyre. Grip values are multipliers on the wheel's
    /// friction stiffness, where 1.0 is dry hardpack.
    /// </summary>
    public struct KartSurface
    {
        public string name;
        /// <summary>Traction along the wheel — governs how hard it can accelerate and brake.</summary>
        public float forwardGrip;
        /// <summary>Traction across the wheel — governs how early it lets go into a slide.</summary>
        public float sidewaysGrip;
        /// <summary>Coefficient of rolling resistance, as a fraction of the load on the wheel.</summary>
        public float rollingResistance;
        /// <summary>Scales drive torque. Loose or deep going spins the wheels up instead of driving.</summary>
        public float driveEfficiency;

        public static KartSurface Default => new KartSurface
        {
            name = "Ground",
            forwardGrip = 0.85f,
            sidewaysGrip = 0.80f,
            rollingResistance = 0.022f,
            driveEfficiency = 0.95f,
        };
    }

    /// <summary>
    /// Maps a texture, material or object name onto a surface. The project's terrain layers are all
    /// called "NewLayer N", so the only thing left to read them by is the texture they paint with —
    /// which is why this classifies on names rather than on layer indices.
    ///
    /// Pure and static, so every layer in the scene can be run through it outside the Editor.
    /// </summary>
    public static class KartSurfaceLibrary
    {
        // Matched against whole words, earliest word in the name winning — so "SoilGravel02" reads as
        // soil and "GravelSoil" as gravel, each by whichever material it is named for first. Order here
        // only breaks ties within the same word, which is how "Cobblestone" reaches cobble before stone.
        static readonly (string keyword, KartSurface surface)[] Table =
        {
            ("ice", Make("Ice", 0.25f, 0.18f, 0.010f, 0.55f)),
            ("snow", Make("Snow", 0.45f, 0.38f, 0.038f, 0.70f)),
            ("water", Make("Water", 0.35f, 0.28f, 0.060f, 0.60f)),
            ("mud", Make("Mud", 0.50f, 0.42f, 0.055f, 0.65f)),

            ("gravel", Make("Gravel", 0.70f, 0.60f, 0.032f, 0.85f)),
            ("pebble", Make("Gravel", 0.70f, 0.60f, 0.032f, 0.85f)),
            ("sand", Make("Sand", 0.58f, 0.50f, 0.055f, 0.72f)),

            ("lava", Make("Lava Crust", 0.72f, 0.65f, 0.030f, 0.88f)),
            ("burnt", Make("Scorched Rock", 0.88f, 0.82f, 0.020f, 0.96f)),
            ("ash", Make("Ash", 0.60f, 0.52f, 0.040f, 0.78f)),

            ("asphalt", Make("Asphalt", 1.15f, 1.10f, 0.011f, 1.00f)),
            ("tarmac", Make("Asphalt", 1.15f, 1.10f, 0.011f, 1.00f)),
            ("road", Make("Road", 1.10f, 1.05f, 0.012f, 1.00f)),
            ("track", Make("Track", 1.10f, 1.05f, 0.012f, 1.00f)),
            ("concrete", Make("Concrete", 1.05f, 1.00f, 0.013f, 1.00f)),

            ("cobble", Make("Cobblestone", 0.92f, 0.86f, 0.020f, 0.97f)),
            ("flagstone", Make("Flagstone", 1.00f, 0.95f, 0.015f, 1.00f)),
            ("stone", Make("Stone", 1.00f, 0.94f, 0.016f, 1.00f)),
            ("rock", Make("Rock", 0.98f, 0.92f, 0.018f, 0.99f)),
            ("cliff", Make("Rock", 0.98f, 0.92f, 0.018f, 0.99f)),

            ("wood", Make("Wood", 0.90f, 0.84f, 0.017f, 0.98f)),
            ("plank", Make("Wood", 0.90f, 0.84f, 0.017f, 0.98f)),
            ("bridge", Make("Bridge Deck", 0.95f, 0.90f, 0.015f, 1.00f)),
            ("metal", Make("Metal", 0.88f, 0.80f, 0.012f, 0.96f)),

            ("moss", Make("Mossy Ground", 0.74f, 0.66f, 0.028f, 0.90f)),
            ("grass", Make("Grass", 0.66f, 0.58f, 0.030f, 0.85f)),
            ("weed", Make("Overgrown Dirt", 0.68f, 0.60f, 0.030f, 0.86f)),
            ("leaf", Make("Leaf Litter", 0.62f, 0.54f, 0.034f, 0.82f)),

            ("soil", Make("Soil", 0.76f, 0.68f, 0.028f, 0.92f)),
            ("dirt", Make("Dirt", 0.78f, 0.70f, 0.026f, 0.93f)),
            ("ground", Make("Hardpack", 0.85f, 0.78f, 0.022f, 0.95f)),
            ("dry", Make("Dry Hardpack", 0.82f, 0.74f, 0.024f, 0.95f)),
            ("desert", Make("Desert Hardpack", 0.80f, 0.72f, 0.026f, 0.94f)),
            ("terrain", Make("Hardpack", 0.85f, 0.78f, 0.022f, 0.95f)),
        };

        /// <summary>
        /// Classifies by name. Returns <see cref="KartSurface.Default"/> when nothing matches, so an
        /// unrecognised surface drives like ordinary ground rather than like ice.
        /// </summary>
        public static KartSurface Classify(string rawName)
        {
            if (string.IsNullOrEmpty(rawName))
                return KartSurface.Default;

            // Whole words, not substrings. A plain "contains" check reads "Pumice" as ice and hands the
            // driver a quarter of the grip they should have on a volcanic map — the kind of wrong that
            // gets blamed on the physics rather than on a string match.
            List<string> words = Tokenize(rawName);

            int bestWord = int.MaxValue;
            int bestRule = int.MaxValue;

            for (int w = 0; w < words.Count; w++)
            {
                for (int r = 0; r < Table.Length; r++)
                {
                    // StartsWith rather than equality, so plurals and suffixes still land: "rocks",
                    // "stones", "snowy", "grassy".
                    if (!words[w].StartsWith(Table[r].keyword, System.StringComparison.Ordinal))
                        continue;

                    if (w < bestWord || (w == bestWord && r < bestRule))
                    {
                        bestWord = w;
                        bestRule = r;
                    }
                }
            }

            return bestRule < Table.Length ? Table[bestRule].surface : KartSurface.Default;
        }

        /// <summary>
        /// Splits an asset name into lowercase words, breaking on punctuation, digits and camelCase —
        /// so "T_YFGM_SoilGravel02_s" comes apart into t, yfgm, soil, gravel, s.
        /// </summary>
        public static List<string> Tokenize(string name)
        {
            var words = new List<string>();
            var current = new System.Text.StringBuilder();

            void Flush()
            {
                if (current.Length == 0)
                    return;
                words.Add(current.ToString());
                current.Clear();
            }

            for (int i = 0; i < name.Length; i++)
            {
                char c = name[i];

                if (!char.IsLetter(c))
                {
                    Flush();
                    continue;
                }

                if (current.Length > 0 && char.IsUpper(c) && char.IsLower(name[i - 1]))
                    Flush();

                current.Append(char.ToLowerInvariant(c));
            }

            Flush();
            return words;
        }

        /// <summary>Blends two surfaces, for a wheel straddling a boundary between terrain layers.</summary>
        public static KartSurface Blend(KartSurface a, KartSurface b, float t)
        {
            return new KartSurface
            {
                name = t < 0.5f ? a.name : b.name,
                forwardGrip = Mathf.Lerp(a.forwardGrip, b.forwardGrip, t),
                sidewaysGrip = Mathf.Lerp(a.sidewaysGrip, b.sidewaysGrip, t),
                rollingResistance = Mathf.Lerp(a.rollingResistance, b.rollingResistance, t),
                driveEfficiency = Mathf.Lerp(a.driveEfficiency, b.driveEfficiency, t),
            };
        }

        static KartSurface Make(string name, float forward, float sideways, float rolling, float drive)
        {
            return new KartSurface
            {
                name = name,
                forwardGrip = forward,
                sidewaysGrip = sideways,
                rollingResistance = rolling,
                driveEfficiency = drive,
            };
        }
    }
}
