using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace FrozenLake
{
    /// <summary>
    /// Drops a generated low-poly frozen lake onto this GameObject's MeshFilter.
    ///
    /// The mesh is built procedurally rather than shipped as a binary model, so it regenerates from
    /// the seed whenever the scene loads and every tweak in the inspector is a live preview. Use
    /// "Save Mesh Asset" on the inspector to bake a particular lake down to a .asset if you would
    /// rather ship static geometry.
    ///
    /// The renderer expects four materials, in submesh order: pale ice, deep ice, snow, rock.
    /// </summary>
    [ExecuteAlways]
    [AddComponentMenu("Frozen Lake/Frozen Lake Generator")]
    [RequireComponent(typeof(MeshFilter))]
    [RequireComponent(typeof(MeshRenderer))]
    public class FrozenLakeGenerator : MonoBehaviour
    {
        [SerializeField] FrozenLakeSettings settings = new FrozenLakeSettings();

        [Tooltip("Push the generated mesh onto a MeshCollider on this object, if there is one.")]
        [SerializeField] bool updateCollider = true;

        [Tooltip("Rebuild as soon as a value changes in the inspector.")]
        [SerializeField] bool liveUpdate = true;

        Mesh _mesh;

        public FrozenLakeSettings Settings { get { return settings; } }

        /// <summary>The mesh currently on the filter, or null if nothing has been generated yet.</summary>
        public Mesh Mesh { get { return _mesh; } }

        void OnEnable()
        {
            // Procedural meshes are not serialised with the scene, so rebuild after every load,
            // domain reload and play-mode transition.
            if (_mesh == null) Generate();
        }

        void OnValidate()
        {
            if (!liveUpdate) return;
#if UNITY_EDITOR
            // OnValidate runs during serialisation; defer so we are not touching objects mid-import.
            UnityEditor.EditorApplication.delayCall += () =>
            {
                if (this == null) return;
                Generate();
            };
#else
            Generate();
#endif
        }

        void OnDestroy()
        {
            // Only ever destroy the instance we made ourselves; a baked asset must survive.
            if (_mesh == null) return;
#if UNITY_EDITOR
            if (!UnityEditor.AssetDatabase.Contains(_mesh))
            {
                if (Application.isPlaying) Destroy(_mesh);
                else DestroyImmediate(_mesh);
            }
#else
            Destroy(_mesh);
#endif
            _mesh = null;
        }

        /// <summary>Rebuilds the lake and assigns it to this object's filter and collider.</summary>
        public void Generate()
        {
            var filter = GetComponent<MeshFilter>();
            if (filter == null) return;

#if UNITY_EDITOR
            bool ownsCurrent = _mesh != null && !UnityEditor.AssetDatabase.Contains(_mesh);
#else
            bool ownsCurrent = _mesh != null;
#endif
            // Refill the mesh we already own rather than leaking a new one on every keystroke.
            Mesh target = ownsCurrent ? _mesh : new Mesh();
            target.name = "FrozenLake_" + settings.seed;
            Fill(target, settings);

            _mesh = target;
            filter.sharedMesh = target;

            if (updateCollider)
            {
                var collider = GetComponent<MeshCollider>();
                if (collider != null) collider.sharedMesh = target;
            }
        }

        /// <summary>Rolls a new seed and rebuilds.</summary>
        public void Randomize()
        {
            settings.seed = Random.Range(int.MinValue, int.MaxValue);
            Generate();
        }

        /// <summary>Builds a standalone mesh, for baking to an asset or for pooling at runtime.</summary>
        public static Mesh Create(FrozenLakeSettings settings)
        {
            var mesh = new Mesh();
            mesh.name = "FrozenLake_" + (settings != null ? settings.seed : 0);
            Fill(mesh, settings);
            return mesh;
        }

        static void Fill(Mesh mesh, FrozenLakeSettings settings)
        {
            MeshBuffer buf = FrozenLakeMeshBuilder.Build(settings);

            mesh.Clear();
            // A dense lake can pass the 16-bit vertex limit, so widen the index buffer when needed.
            mesh.indexFormat = buf.Vertices.Count > 65535 ? IndexFormat.UInt32 : IndexFormat.UInt16;

            mesh.SetVertices(buf.Vertices);
            mesh.SetNormals(buf.Normals);
            mesh.SetUVs(0, buf.UVs);
            mesh.SetColors(buf.Colors);

            mesh.subMeshCount = buf.Submeshes.Length;
            for (int i = 0; i < buf.Submeshes.Length; i++)
            {
                List<int> tris = buf.Submeshes[i];
                mesh.SetTriangles(tris, i, false);
            }

            mesh.RecalculateBounds();
            mesh.RecalculateTangents();
        }
    }
}
