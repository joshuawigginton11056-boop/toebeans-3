using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace LavaPond
{
    /// <summary>
    /// Drops a generated low-poly lava pond onto this GameObject's MeshFilter.
    ///
    /// The mesh is built procedurally rather than shipped as a binary model, so it regenerates from
    /// the seed whenever the scene loads and every tweak in the inspector is a live preview. Use
    /// "Save Mesh Asset" on the inspector to bake a particular pond down to a .asset if you would
    /// rather ship static geometry.
    ///
    /// The renderer expects four materials, in submesh order: dark crust, warm crust, molten lava,
    /// rock. Only the molten slot needs to be emissive, and it is the one to put a scrolling lava
    /// shader on if you have one.
    /// </summary>
    [ExecuteAlways]
    [AddComponentMenu("Lava Pond/Lava Pond Generator")]
    [RequireComponent(typeof(MeshFilter))]
    [RequireComponent(typeof(MeshRenderer))]
    public class LavaPondGenerator : MonoBehaviour
    {
        [SerializeField] LavaPondSettings settings = new LavaPondSettings();

        [Tooltip("Push the generated mesh onto a MeshCollider on this object, if there is one.")]
        [SerializeField] bool updateCollider = true;

        [Tooltip("Rebuild as soon as a value changes in the inspector.")]
        [SerializeField] bool liveUpdate = true;

        Mesh _mesh;
        VentInfo _vent;
        float _crustCoverage;

        public LavaPondSettings Settings { get { return settings; } }

        /// <summary>
        /// How much of the pond the crust plates cover, 0 to 1, as last built. Measured during the
        /// build rather than read back off the mesh: the molten sheet runs unbroken underneath the
        /// crust, so from outside there is no telling the lava you can see from the lava a plate is
        /// sitting on.
        /// </summary>
        public float CrustCoverage { get { return _crustCoverage; } }

        /// <summary>
        /// Where the vent ended up, in local space. <c>Exists</c> is false on a pond without one.
        /// Use <see cref="TryGetVentPoint"/> for the world-space version.
        /// </summary>
        public VentInfo Vent { get { return _vent; } }

        /// <summary>
        /// World-space centre and radius of the lava standing in the vent's mouth. This is where a
        /// particle system, a point light or a damage volume wants to sit. Returns false when the
        /// pond has no vent.
        /// </summary>
        public bool TryGetVentPoint(out Vector3 center, out float radius)
        {
            if (!_vent.Exists)
            {
                center = transform.position;
                radius = 0f;
                return false;
            }

            center = transform.TransformPoint(_vent.Mouth);
            Vector3 scale = transform.lossyScale;
            radius = _vent.Radius * Mathf.Max(Mathf.Abs(scale.x), Mathf.Abs(scale.z));
            return true;
        }

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

        /// <summary>Rebuilds the pond and assigns it to this object's filter and collider.</summary>
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
            target.name = "LavaPond_" + settings.seed;

            MeshBuffer buf = Fill(target, settings);
            _vent = buf.Vent;
            _crustCoverage = buf.CrustCoverage;

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
        public static Mesh Create(LavaPondSettings settings)
        {
            var mesh = new Mesh();
            mesh.name = "LavaPond_" + (settings != null ? settings.seed : 0);
            Fill(mesh, settings);
            return mesh;
        }

        static MeshBuffer Fill(Mesh mesh, LavaPondSettings settings)
        {
            MeshBuffer buf = LavaPondMeshBuilder.Build(settings);

            mesh.Clear();
            // A dense pond can pass the 16-bit vertex limit, so widen the index buffer when needed.
            mesh.indexFormat = buf.Vertices.Count > 65535 ? IndexFormat.UInt32 : IndexFormat.UInt16;

            mesh.SetVertices(buf.Vertices);
            mesh.SetNormals(buf.Normals);
            mesh.SetUVs(0, buf.UVs);
            // UV1 is how far each vertex is from the edge of the lava. A shader that does not read
            // TEXCOORD1 ignores it; one that does — the Lava Flow package's molten shader — needs
            // it, or its bank crust has no way to tell the middle of the pond from the shoreline
            // and films over the lot.
            mesh.SetUVs(1, buf.UV1);
            mesh.SetColors(buf.Colors);

            mesh.subMeshCount = buf.Submeshes.Length;
            for (int i = 0; i < buf.Submeshes.Length; i++)
            {
                List<int> tris = buf.Submeshes[i];
                mesh.SetTriangles(tris, i, false);
            }

            mesh.RecalculateBounds();
            mesh.RecalculateTangents();
            return buf;
        }

        void OnDrawGizmosSelected()
        {
            if (!_vent.Exists) return;

            Vector3 center;
            float radius;
            if (!TryGetVentPoint(out center, out radius)) return;

            // The mouth anything spawned by the vent should come out of.
            Gizmos.color = new Color(1f, 0.45f, 0.1f, 0.9f);
            Gizmos.DrawWireSphere(center, radius);
        }
    }
}
