using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace Farm.EditorTools
{
    /// <summary>
    /// What the Blender build wrote about the farm pack: every model, its colours, its part
    /// hierarchy, and how it should collide.
    ///
    /// This exists because of a drift hazard the barrier pack has and this one is five times too
    /// big to survive. <c>BarrierAssetSetup</c> carries a hand-written copy of the palette its
    /// Blender script produces, under a comment saying the two have to match — and nothing checks
    /// that they do. With six model scripts, forty models and twenty-five colours, a copy would be
    /// wrong within a week and wrong in a way that only shows up as a slightly different brown.
    ///
    /// So <c>Tools/blender/farmyard.py</c> writes one of these per model script and the setup tool
    /// reads them. Adding a prop in Blender needs no C# change at all: build it, focus Unity, run
    /// the menu item.
    /// </summary>
    public static class FarmManifest
    {
        public const string Dir = "Assets/GeneratedModels/Manifests";

        /// <summary>Collider the prefab should get. Mirrors the COLLIDER_* constants in farmyard.py.</summary>
        public const string ColliderMesh = "mesh";
        public const string ColliderBox = "box";
        public const string ColliderCapsule = "capsule";
        public const string ColliderNone = "none";

        [System.Serializable]
        public sealed class Material
        {
            public string name;
            public float[] rgb;
            public float metallic;
            public float roughness;
            public bool emissive;

            /// <summary>
            /// Blender authors roughness; URP wants smoothness. They are opposite ends of the same
            /// number, and getting the conversion backwards makes every matte surface in the pack
            /// look like wet plastic — which is the kind of wrong that reads as "the art is bad"
            /// rather than as "one line is inverted".
            /// </summary>
            public float Smoothness { get { return Mathf.Clamp01(1f - roughness); } }

            public Color Colour
            {
                get
                {
                    if (rgb == null || rgb.Length < 3) return Color.magenta;
                    // Blender's Base Color is linear. Unity's colour pickers and _BaseColor are
                    // linear too in a linear-space project, so these pass straight through.
                    return new Color(rgb[0], rgb[1], rgb[2], 1f);
                }
            }
        }

        [System.Serializable]
        public sealed class Part
        {
            public string name;
            public string parent;
            public float[] pivot;
        }

        [System.Serializable]
        public sealed class Model
        {
            public string name;
            public string kind;          // "prop" or "hierarchy"
            public int tris;
            public float[] dims;
            public string[] materials;
            public Part[] parts;
            public string collider;
            public string tag;
            public string note;
            public float waterline;

            public bool IsRig { get { return kind == "hierarchy" && parts != null && parts.Length > 0; } }

            public Vector3 Size
            {
                get
                {
                    if (dims == null || dims.Length < 3) return Vector3.one;
                    // Blender is Z-up and Unity is Y-up, and the export convention swaps the two.
                    // The manifest records Blender's numbers, so anything measuring a prefab out of
                    // it has to swap them back or every collider in the pack is on its side.
                    return new Vector3(dims[0], dims[2], dims[1]);
                }
            }
        }

        [System.Serializable]
        public sealed class Pack
        {
            public string script;
            public Model[] models;
            public Material[] materials;
        }

        /// <summary>Every manifest on disk, newest read wins for a duplicated material name.</summary>
        public static bool TryLoadAll(out List<Model> models, out Dictionary<string, Material> materials)
        {
            models = new List<Model>();
            materials = new Dictionary<string, Material>();

            if (!Directory.Exists(Dir))
            {
                Debug.LogError($"Farm: no manifests at {Dir}. Run Tools\\blender\\build-models.ps1 first.");
                return false;
            }

            string[] files = Directory.GetFiles(Dir, "*.json");
            foreach (string file in files)
            {
                Pack pack;
                try
                {
                    pack = JsonUtility.FromJson<Pack>(File.ReadAllText(file));
                }
                catch (System.Exception e)
                {
                    Debug.LogError($"Farm: {Path.GetFileName(file)} is not readable — {e.Message}");
                    continue;
                }

                if (pack == null || pack.models == null) continue;
                models.AddRange(pack.models);

                if (pack.materials == null) continue;
                foreach (Material m in pack.materials)
                {
                    if (m != null && !string.IsNullOrEmpty(m.name)) materials[m.name] = m;
                }
            }

            if (models.Count == 0)
            {
                Debug.LogError($"Farm: {files.Length} manifest(s) at {Dir} but no models in them.");
                return false;
            }

            return true;
        }
    }
}
