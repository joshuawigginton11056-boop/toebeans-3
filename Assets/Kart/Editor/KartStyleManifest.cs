using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace Toebeans.Karting.EditorTools
{
    /// <summary>
    /// Reads a kart style's palette out of the manifest its Blender script wrote.
    ///
    /// This exists for the reason the farm pack's manifests do, and the pipeline README already
    /// names the anti-pattern: <c>BarrierAssetSetup.cs</c> carries a hand-written copy of the palette
    /// its Blender script produces, under a comment saying the two have to match, and nothing checks
    /// that they do. A kart style is six slots of colour, metallic and smoothness; nine styles is
    /// getting on for fifty numbers that would have to be kept in step by hand across a language
    /// boundary, and the failure mode is silent — the kart just comes out the wrong colour, and
    /// nothing says whether Blender or Unity is the one that is wrong.
    ///
    /// So <c>Tools/blender/kartworks.py</c> writes the numbers and this reads them. What stays
    /// hand-written in <see cref="KartStyle.All"/> is the style's name, its mesh names and its lamp
    /// flags — because a wrong mesh name fails loudly ("no model at ...") the moment you build the
    /// kart, where a wrong colour does not fail at all.
    ///
    /// A missing manifest is not an error. The style simply keeps the shared default palette and
    /// says so once, which is the correct behaviour for a style whose Blender script has not been run
    /// yet — the same principle as a missing mesh falling back to primitives.
    /// </summary>
    public static class KartStyleManifest
    {
        const string ManifestFolder = "Assets/GeneratedModels/Manifests";

        // Read once per domain reload. Reloading per style would re-read the same files for every
        // menu click, and these only change when Blender runs, which reloads the domain anyway.
        static readonly Dictionary<string, Dictionary<KartSkin, KartSkinColour>> Cache =
            new Dictionary<string, Dictionary<KartSkin, KartSkinColour>>();

        /// <summary>
        /// Which <see cref="KartSkin"/> each manifest slot name maps to. The names are the ones baked
        /// into the FBX by the Blender palette, so this is the same contract
        /// <c>KartSetup.SkinsByMaterialName</c> matches on — deliberately spelled out again rather
        /// than shared, because these two are matching different things that merely agree today.
        /// </summary>
        static readonly Dictionary<string, KartSkin> SkinsBySlotName =
            new Dictionary<string, KartSkin>
            {
                ["KartFrame"] = KartSkin.Frame,
                ["KartBody"] = KartSkin.Body,
                ["KartSeat"] = KartSkin.Seat,
                ["KartRim"] = KartSkin.Rim,
                ["KartRubber"] = KartSkin.Rubber,
                ["KartLens"] = KartSkin.Lens,
            };

        /// <summary>
        /// Fills in <see cref="KartStyle.palette"/> from disk if it is not already populated. Safe to
        /// call repeatedly; safe to call for a style that has no manifest.
        /// </summary>
        public static void Apply(KartStyle style)
        {
            if (style == null || style.palette != null || string.IsNullOrEmpty(style.key))
                return;

            style.palette = Load(style.key);
        }

        static Dictionary<KartSkin, KartSkinColour> Load(string key)
        {
            if (Cache.TryGetValue(key, out Dictionary<KartSkin, KartSkinColour> cached))
                return cached;

            Dictionary<KartSkin, KartSkinColour> palette = ReadFile(key);
            Cache[key] = palette;
            return palette;
        }

        static Dictionary<KartSkin, KartSkinColour> ReadFile(string key)
        {
            string path = $"{ManifestFolder}/kart_{key}.json";
            if (!File.Exists(path))
                return null;

            ManifestJson parsed;
            try
            {
                parsed = JsonUtility.FromJson<ManifestJson>(File.ReadAllText(path));
            }
            catch (Exception e)
            {
                Debug.LogWarning(
                    $"[Kart] Could not read the style manifest at '{path}' ({e.Message}), so the " +
                    $"'{key}' style falls back to the shared kart palette. Rebuild it with " +
                    ".\\Tools\\blender\\build-models.ps1.");
                return null;
            }

            if (parsed?.slots == null || parsed.slots.Length == 0)
            {
                Debug.LogWarning($"[Kart] The style manifest at '{path}' has no slots in it.");
                return null;
            }

            var palette = new Dictionary<KartSkin, KartSkinColour>();
            foreach (SlotJson slot in parsed.slots)
            {
                if (slot == null || !SkinsBySlotName.TryGetValue(slot.slot ?? "", out KartSkin skin))
                {
                    Debug.LogWarning(
                        $"[Kart] '{path}' names a slot '{slot?.slot ?? "(none)"}' that is not one of " +
                        "the kart skins, so it was ignored. The slot names come from " +
                        "kartworks.SLOT_NAMES.");
                    continue;
                }

                palette[skin] = new KartSkinColour
                {
                    color = ToColor(slot.color, Color.magenta),
                    metallic = slot.metallic,
                    smoothness = slot.smoothness,
                    emission = ToColor(slot.emission, Color.black),
                };
            }

            return palette.Count > 0 ? palette : null;
        }

        static Color ToColor(float[] rgb, Color fallback)
        {
            // Alpha is never in the manifest — a kart has no transparent slot, and the lamp glass
            // fakes its own with emission rather than with alpha.
            return rgb != null && rgb.Length >= 3 ? new Color(rgb[0], rgb[1], rgb[2], 1f) : fallback;
        }

        // JsonUtility needs concrete serializable types with fields named exactly as in the file.
        // It assigns them by reflection, which the compiler cannot see — hence CS0649 on every one
        // of them, and hence the suppression.
#pragma warning disable 649
        [Serializable]
        class ManifestJson
        {
            public string key;
            public bool noseLamps;
            public bool roofBar;
            public string[] meshes;
            public SlotJson[] slots;
        }

        [Serializable]
        class SlotJson
        {
            public string slot;
            public float[] color;
            public float metallic;
            public float smoothness;
            public float[] emission;
        }
#pragma warning restore 649
    }
}
