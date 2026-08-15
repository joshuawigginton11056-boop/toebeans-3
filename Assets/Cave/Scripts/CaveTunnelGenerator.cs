using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace CaveTunnel
{
    /// <summary>
    /// Drops a generated low-poly cave onto this GameObject's MeshFilter.
    ///
    /// The mesh is built procedurally rather than shipped as a binary model, so every node you drag
    /// in the scene view is a live preview. Use "Save Mesh Asset" on the inspector to bake a
    /// particular cave down to a .asset once you are happy with the shape.
    ///
    /// The renderer expects two materials, in submesh order: rock, floor.
    ///
    /// The walls face inwards, since you are meant to be inside them. Looking at the cave from
    /// outside in the scene view therefore shows you straight through it — that is the geometry
    /// working, not a bug. Bury the mouths in a hillside or a mountain mesh and only the openings
    /// read from outside.
    /// </summary>
    [ExecuteAlways]
    [AddComponentMenu("Cave/Cave Tunnel Generator")]
    [RequireComponent(typeof(MeshFilter))]
    [RequireComponent(typeof(MeshRenderer))]
    public class CaveTunnelGenerator : MonoBehaviour
    {
        [SerializeField]
        List<CaveNode> nodes = new List<CaveNode>
        {
            new CaveNode(new Vector3(0f, 0f, 0f), 5f, 4.5f),
            new CaveNode(new Vector3(0f, 0f, 20f), 5f, 4.5f),
            new CaveNode(new Vector3(0f, 0f, 40f), 5f, 4.5f)
        };

        [SerializeField] CaveSettings settings = new CaveSettings();

        [Tooltip("Push the generated mesh onto a MeshCollider on this object, if there is one. " +
                 "Without this you can see the cave but not drive through it.")]
        [SerializeField] bool updateCollider = true;

        [Tooltip("Rebuild as soon as a value changes in the inspector.")]
        [SerializeField] bool liveUpdate = true;

        Mesh _mesh;
        float _length;
        CaveVolume _volume;

        public List<CaveNode> Nodes { get { return nodes; } }
        public CaveSettings Settings { get { return settings; } }

        /// <summary>
        /// Solid-body query for the cave interior, rebuilt with the mesh. This is what the terrain
        /// hole puncher tests the hillside against.
        ///
        /// Builds on demand. A domain reload leaves the mesh in place but drops the volume, and a
        /// caller that took the null at face value would quietly conclude the cave is empty — which
        /// looks exactly like a clear tunnel rather than like a missing answer.
        /// </summary>
        public CaveVolume Volume
        {
            get
            {
                if (_volume == null) Generate();
                return _volume;
            }
        }

        /// <summary>The mesh currently on the filter, or null if nothing has been generated yet.</summary>
        public Mesh Mesh { get { return _mesh; } }

        /// <summary>Length of the cave's centreline in metres, as of the last build.</summary>
        public float Length { get { return _length; } }

        void OnEnable()
        {
            // Procedural meshes are not serialised with the scene, so rebuild after every load,
            // domain reload and play-mode transition. The volume is checked as well as the mesh:
            // a reload can leave the mesh behind while dropping the volume, and keying only off the
            // mesh would skip the rebuild and leave the volume null for good.
            if (_mesh == null || _volume == null) Generate();
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

        /// <summary>Rebuilds the cave and assigns it to this object's filter and collider.</summary>
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
            target.name = "CaveTunnel_" + settings.seed;
            _length = Fill(target, nodes, settings, out _volume);

            _mesh = target;
            filter.sharedMesh = target;

            if (updateCollider)
            {
                var collider = GetComponent<MeshCollider>();
                if (collider != null)
                {
                    // Reassigning the same mesh instance does not always rebuild the physics shape,
                    // so clear it first. Without this you drive through last build's walls.
                    collider.sharedMesh = null;
                    collider.sharedMesh = target;
                }
            }
        }

        /// <summary>Builds a standalone mesh, for baking to an asset or for pooling at runtime.</summary>
        public static Mesh Create(IList<CaveNode> nodes, CaveSettings settings)
        {
            var mesh = new Mesh();
            mesh.name = "CaveTunnel_" + (settings != null ? settings.seed : 0);
            CaveVolume ignored;
            Fill(mesh, nodes, settings, out ignored);
            return mesh;
        }

        static float Fill(Mesh mesh, IList<CaveNode> nodes, CaveSettings settings, out CaveVolume volume)
        {
            CaveMeshBuffer buf = CaveMeshBuilder.Build(nodes, settings);

            mesh.Clear();
            // A long or finely sampled cave passes the 16-bit vertex limit easily.
            mesh.indexFormat = buf.Vertices.Count > 65535 ? IndexFormat.UInt32 : IndexFormat.UInt16;

            mesh.SetVertices(buf.Vertices);
            mesh.SetNormals(buf.Normals);
            mesh.SetUVs(0, buf.UVs);
            mesh.SetColors(buf.Colors);

            mesh.subMeshCount = buf.Submeshes.Length;
            for (int i = 0; i < buf.Submeshes.Length; i++)
                mesh.SetTriangles(buf.Submeshes[i], i, false);

            mesh.RecalculateBounds();
            mesh.RecalculateTangents();
            volume = buf.Volume;
            return buf.Length;
        }

        /// <summary>
        /// Radius of the circle through a node and its two neighbours, in metres. Infinity on a
        /// straight run.
        ///
        /// This is the number that decides whether a turn can be built at all. A swept tunnel is a
        /// section carried along a path, and on the inside of a bend that section sweeps backwards
        /// once the turn radius drops below the half-width — the inner wall passes through itself
        /// and the mesh crumples. No amount of smoothing fixes it, because the geometry being asked
        /// for does not exist: the corridor would have to overlap itself.
        /// </summary>
        public float TurnRadiusAt(int index)
        {
            if (index <= 0 || index >= nodes.Count - 1) return float.PositiveInfinity;

            Vector3 a = nodes[index - 1].position;
            Vector3 b = nodes[index].position;
            Vector3 c = nodes[index + 1].position;

            float ab = Vector3.Distance(a, b);
            float bc = Vector3.Distance(b, c);
            float ca = Vector3.Distance(c, a);

            float area = Vector3.Cross(b - a, c - a).magnitude * 0.5f;
            if (area < 1e-5f) return float.PositiveInfinity; // collinear

            return (ab * bc * ca) / (4f * area);
        }

        /// <summary>
        /// Nodes whose turn is too tight for the passage width there. Ratio is turn radius over
        /// half-width: below 1 the mesh must fold, and below about 2 it is buildable but pinched.
        /// </summary>
        public List<KeyValuePair<int, float>> FindTightTurns(float ratioThreshold = 2f)
        {
            var tight = new List<KeyValuePair<int, float>>();

            for (int i = 1; i < nodes.Count - 1; i++)
            {
                float radius = TurnRadiusAt(i);
                if (float.IsInfinity(radius)) continue;

                float ratio = radius / Mathf.Max(0.01f, nodes[i].width);
                if (ratio < ratioThreshold) tight.Add(new KeyValuePair<int, float>(i, ratio));
            }
            return tight;
        }

        /// <summary>
        /// Gap the segment between node <paramref name="index"/> and the next must have before a
        /// node may be inserted into it.
        ///
        /// Inserting lands the new node midway, so each resulting half has to clear the minimum on
        /// its own — hence the doubling. The measure is the local half-width because that is what
        /// the fold threshold is measured against: three nodes spaced d apart turning through an
        /// angle have a radius of d / (2 sin(angle/2)), so holding spacing to a fraction of the
        /// half-width is what keeps that radius buildable once the corner is actually bent.
        /// </summary>
        public float MinimumGapBefore(int index)
        {
            if (index < 0 || index >= nodes.Count - 1) return 0f;

            float halfWidth = (nodes[index].width + nodes[index + 1].width) * 0.5f;
            return halfWidth * settings.minNodeSpacing * 2f;
        }

        /// <summary>False when inserting here would pack the nodes tighter than the guard allows.</summary>
        public bool CanInsertBefore(int index)
        {
            if (settings.minNodeSpacing <= 0f) return true;
            if (index < 0 || index >= nodes.Count - 1) return false;

            float gap = Vector3.Distance(nodes[index].position, nodes[index + 1].position);
            return gap >= MinimumGapBefore(index);
        }

        /// <summary>
        /// Respaces the nodes evenly along the current curve without changing its shape.
        /// Pass 0 to keep the present node count.
        /// </summary>
        public void RedistributeNodes(int count = 0)
        {
            if (count <= 0) count = nodes.Count;

            List<CaveNode> spread = CaveMeshBuilder.Redistribute(nodes, settings, count);
            if (spread.Count < 2) return;

            nodes = spread;
            Generate();
        }

        /// <summary>
        /// Ratio of turn radius to half-width at the tightest corner on the path — the single
        /// number that says whether the cave can be built. Below 1 the mesh must fold; below about
        /// 2 it is buildable but pinched. Infinity on a path with no bends at all.
        /// </summary>
        public float TightestTurnRatio()
        {
            float worst = float.PositiveInfinity;

            for (int i = 1; i < nodes.Count - 1; i++)
            {
                float radius = TurnRadiusAt(i);
                if (float.IsInfinity(radius)) continue;
                worst = Mathf.Min(worst, radius / Mathf.Max(0.01f, nodes[i].width));
            }
            return worst;
        }

        /// <summary>
        /// Eases corners that are too tight to build, leaving everything already within tolerance
        /// alone. Straightening the offending node towards the line between its neighbours is the
        /// direct lever on turn radius. Note this is the operation that actually prevents folding —
        /// respacing nodes evenly does not, since evenly spaced nodes can still describe a hairpin
        /// far tighter than the passage is wide.
        ///
        /// Two things keep it from making the cave worse, both learned the hard way from a hairpin
        /// that went 0.78x, 1.43x, 0.51x, 0.41x over three runs of the original:
        ///
        /// The node is moved only <em>across</em> the chord between its neighbours, never along it.
        /// Turn radius scales with node spacing while the passage width does not, so the along-chord
        /// half of a move towards the midpoint shrinks the run and tightens the ratio it was called
        /// on to loosen — which is why repeated passes used to diverge instead of settle.
        ///
        /// And the best arrangement seen is kept rather than the last one reached. Easing a corner
        /// necessarily sharpens its two neighbours until they are eased in their turn, so the
        /// tightest ratio dips before it climbs and a pass-by-pass accept/reject rule refuses the
        /// very first move. Recording the best and restoring it at the end lets the search work
        /// through that dip while still guaranteeing the result is never worse than the input —
        /// which is what makes this safe to press repeatedly.
        ///
        /// The end nodes never move: they are the mouths.
        /// Returns the number of nodes actually shifted.
        /// </summary>
        public int RelaxTightTurns(float targetRatio = 2f, int iterations = 40, float strength = 0.25f)
        {
            var current = new Vector3[nodes.Count];
            var original = new Vector3[nodes.Count];
            var best = new Vector3[nodes.Count];

            for (int i = 0; i < nodes.Count; i++) original[i] = best[i] = nodes[i].position;
            float bestRatio = TightestTurnRatio();

            for (int pass = 0; pass < iterations; pass++)
            {
                List<KeyValuePair<int, float>> tight = FindTightTurns(targetRatio);
                if (tight.Count == 0) break;

                for (int i = 0; i < nodes.Count; i++) current[i] = nodes[i].position;

                foreach (KeyValuePair<int, float> t in tight)
                {
                    int i = t.Key;
                    if (i <= 0 || i >= nodes.Count - 1) continue;

                    Vector3 a = current[i - 1];
                    Vector3 b = current[i + 1];
                    Vector3 chord = b - a;
                    float span = chord.magnitude;
                    if (span < 1e-5f) continue;
                    chord /= span;

                    Vector3 toMiddle = (a + b) * 0.5f - current[i];
                    Vector3 across = toMiddle - Vector3.Dot(toMiddle, chord) * chord;

                    // How far short of the target the corner is decides how hard it is pulled, so
                    // a corner that only just fails barely moves.
                    float deficit = Mathf.Clamp01(1f - t.Value / targetRatio);
                    nodes[i].position = current[i] + across * (strength * deficit);
                }

                float ratio = TightestTurnRatio();
                if (ratio > bestRatio)
                {
                    bestRatio = ratio;
                    for (int i = 0; i < nodes.Count; i++) best[i] = nodes[i].position;
                }
            }

            int moved = 0;
            for (int i = 0; i < nodes.Count; i++)
            {
                nodes[i].position = best[i];
                // Counted against where the nodes started, not against the last pass tried, so a
                // search that wandered and came back reports honestly as having changed nothing.
                if (best[i] != original[i]) moved++;
            }

            if (moved > 0) Generate();
            return moved;
        }

        /// <summary>
        /// Drops every node onto whatever solid ground is under it. Node positions are floor
        /// centres, so this seats the cave floor on the terrain rather than burying it.
        /// Nodes are moved in local space; the transform is left alone.
        /// </summary>
        public void SnapNodesToGround(float searchAbove = 500f, float searchDistance = 1000f)
        {
            for (int i = 0; i < nodes.Count; i++)
            {
                Vector3 world = transform.TransformPoint(nodes[i].position);
                var start = new Vector3(world.x, world.y + searchAbove, world.z);

                RaycastHit hit;
                if (!Physics.Raycast(start, Vector3.down, out hit, searchAbove + searchDistance)) continue;

                // Ignore our own collider, or the cave would snap onto itself.
                if (hit.collider != null && hit.collider.gameObject == gameObject) continue;

                nodes[i].position = transform.InverseTransformPoint(hit.point);
            }
            Generate();
        }

        void OnDrawGizmosSelected()
        {
            if (nodes == null || nodes.Count < 2) return;

            // The path itself, so the shape is readable even when the mesh is hidden behind terrain.
            Gizmos.color = new Color(1f, 0.75f, 0.2f, 0.9f);
            for (int i = 0; i < nodes.Count - 1; i++)
            {
                Gizmos.DrawLine(transform.TransformPoint(nodes[i].position),
                                transform.TransformPoint(nodes[i + 1].position));
            }
        }
    }
}
