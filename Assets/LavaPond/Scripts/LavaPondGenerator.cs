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
        LavaPondMeshBuilder.PondShore _shore;
        int _shoreSeed = int.MinValue;
        float _shoreRadius = -1f;
        float _shoreIrregularity = -1f;

        public LavaPondSettings Settings { get { return settings; } }

        /// <summary>
        /// The pond's outline, re-rolled only when a setting that shapes it changes. Everything
        /// asking where the edge of the lava is goes through here rather than assuming a circle of
        /// <c>radius</c>: at the default irregularity the two are metres apart.
        /// </summary>
        public LavaPondMeshBuilder.PondShore Shore
        {
            get
            {
                if (_shore == null || _shoreSeed != settings.seed ||
                    _shoreRadius != settings.radius || _shoreIrregularity != settings.shoreIrregularity)
                {
                    _shore = LavaPondMeshBuilder.CreateShore(settings);
                    _shoreSeed = settings.seed;
                    _shoreRadius = settings.radius;
                    _shoreIrregularity = settings.shoreIrregularity;
                }
                return _shore;
            }
        }

        /// <summary>Metres in the world per metre in the pond's own space.</summary>
        public float WorldScale
        {
            get
            {
                Vector3 s = transform.lossyScale;
                return Mathf.Max(0.0001f, Mathf.Max(Mathf.Abs(s.x), Mathf.Abs(s.z)));
            }
        }

        /// <summary>True when a world-space point is over the lava rather than outside the shore.</summary>
        public bool ContainsWorld(Vector3 worldPoint)
        {
            return LavaPondMeshBuilder.Contains(Shore, transform.InverseTransformPoint(worldPoint));
        }

        /// <summary>
        /// Where a straight run from <paramref name="from"/> heading <paramref name="direction"/>
        /// crosses the edge of the lava, and the height of the lava surface there. This is what a
        /// river ending in the pond aims its last stretch at.
        /// </summary>
        public bool TryGetShoreCrossing(Vector3 from, Vector3 direction, out Vector3 point, out float surfaceY)
        {
            point = from;
            surfaceY = transform.position.y;

            Vector3 a = transform.InverseTransformPoint(from);
            Vector3 dir = transform.InverseTransformDirection(direction);

            Vector3 hit;
            if (!LavaPondMeshBuilder.TryCrossShore(Shore, a, dir, out hit)) return false;

            hit.y = LavaPondMeshBuilder.CrustSurfaceY(settings, hit.x, hit.z);
            point = transform.TransformPoint(hit);
            surfaceY = LavaSurfaceWorldY(point);
            return true;
        }

        /// <summary>World height of the molten surface under a world-space point.</summary>
        public float LavaSurfaceWorldY(Vector3 worldPoint)
        {
            Vector3 p = transform.InverseTransformPoint(worldPoint);
            p.y = LavaPondMeshBuilder.LavaSurfaceY(settings, p.x, p.z);
            return transform.TransformPoint(p).y;
        }

        /// <summary>
        /// Records where a river pours in, in the pond's own terms. Each flow owns one entry, keyed
        /// by <paramref name="owner"/>, so several rivers can feed one pool and each updates only
        /// its own.
        ///
        /// Returns true when something actually changed, and leaves rebuilding to the caller: a
        /// river re-solves on every inspector keystroke, and a pond that rebuilt each time whether
        /// it needed to would drag the scene down and leave it permanently dirty.
        /// </summary>
        public bool SetInlet(int owner, Vector3 worldMouth, float worldHalfWidth, float worldReach)
        {
            Vector3 local = transform.InverseTransformPoint(worldMouth);
            float scale = WorldScale;

            var wanted = new PondInlet
            {
                owner = owner,
                angleDeg = Mathf.Atan2(local.z, local.x) * Mathf.Rad2Deg,
                halfWidth = Mathf.Max(0f, worldHalfWidth) / scale,
                reach = Mathf.Max(0f, worldReach) / scale
            };

            if (settings.inlets == null) settings.inlets = new List<PondInlet>();

            for (int i = 0; i < settings.inlets.Count; i++)
            {
                if (settings.inlets[i].owner != owner) continue;
                if (settings.inlets[i].Matches(wanted)) return false;

                settings.inlets[i] = wanted;
                return true;
            }

            settings.inlets.Add(wanted);
            return true;
        }

        /// <summary>Forgets a river's inlet. Returns true when there was one; the caller rebuilds.</summary>
        public bool ClearInlet(int owner)
        {
            if (settings.inlets == null) return false;

            for (int i = 0; i < settings.inlets.Count; i++)
            {
                if (settings.inlets[i].owner != owner) continue;
                settings.inlets.RemoveAt(i);
                return true;
            }
            return false;
        }

        /// <summary>
        /// Points the pond's lava along <paramref name="worldDirection"/> — the way the river
        /// feeding it runs — so the pool reads as carrying on from the river. Returns true when it
        /// moved.
        /// </summary>
        public bool SetFlowDirection(Vector3 worldDirection)
        {
            Vector3 local = transform.InverseTransformDirection(worldDirection);
            local.y = 0f;
            if (local.sqrMagnitude < 1e-8f) return false;

            // The molten projection runs its V axis at this angle clockwise from the pond's own +Z,
            // so the direction has to be read in the pond's space. Lava Pond on LobbyIsland is
            // turned 115 degrees; taking it off the world axes instead puts the pool's lava that
            // far out from the river feeding it.
            float angle = Mathf.Atan2(local.x, local.z) * Mathf.Rad2Deg;
            if (angle < 0f) angle += 360f;

            if (Mathf.Abs(Mathf.DeltaAngle(settings.flowAngle, angle)) < 0.25f) return false;

            settings.flowAngle = angle;
            return true;
        }

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
