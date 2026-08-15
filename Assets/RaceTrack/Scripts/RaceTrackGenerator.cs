using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace RaceTrack
{
    /// <summary>
    /// Drops a generated racing circuit onto this GameObject's MeshFilter and MeshCollider.
    ///
    /// The track is a banked ribbon swept along a curve through draggable nodes. It is not attached
    /// to anything: nodes carry their own height, so the circuit can run along the ground, climb a
    /// mountain, or hang in mid-air over open water, and nothing needs to be underneath it. Close the
    /// node list into a loop and it becomes a lap, joined seamlessly at the start line.
    ///
    /// The renderer expects four materials, in submesh order: road, kerb, wall, underside.
    ///
    /// The mesh is built procedurally rather than shipped as a model, so every node you drag is a
    /// live preview. Use "Save Mesh Asset" on the inspector to bake a finished circuit to a .asset.
    /// </summary>
    [ExecuteAlways]
    [AddComponentMenu("Race Track/Race Track Generator")]
    [RequireComponent(typeof(MeshFilter))]
    [RequireComponent(typeof(MeshRenderer))]
    public class RaceTrackGenerator : MonoBehaviour
    {
        [SerializeField] List<TrackNode> nodes = DefaultOval();

        [SerializeField] TrackSettings settings = new TrackSettings();

        [Tooltip("Push the generated mesh onto a MeshCollider on this object, if there is one. " +
                 "Without this you can see the track but not drive on it.")]
        [SerializeField] bool updateCollider = true;

        [Tooltip("Rebuild as soon as a value changes in the inspector.")]
        [SerializeField] bool liveUpdate = true;

        Mesh _mesh;
        TrackPath _path;
        TrackMeshBuffer _stats;

        public List<TrackNode> Nodes { get { return nodes; } }
        public TrackSettings Settings { get { return settings; } }

        /// <summary>The mesh currently on the filter, or null if nothing has been generated yet.</summary>
        public Mesh Mesh { get { return _mesh; } }

        /// <summary>Length of the racing line in metres, as of the last build.</summary>
        public float Length { get { return _path != null ? _path.Length : 0f; } }

        /// <summary>
        /// The solved racing line: positions, frames, banking and widths along the whole circuit.
        /// Useful well beyond the mesh — a start grid, checkpoints, lap progress and respawn points
        /// all come off <see cref="TrackPath.SampleAt"/> rather than out of hand-placed markers.
        ///
        /// Rebuilt on demand. A domain reload leaves the mesh in place but drops this, and a caller
        /// that took the null at face value would conclude there is no track.
        /// </summary>
        public TrackPath Path
        {
            get
            {
                if (_path == null) Generate();
                return _path;
            }
        }

        /// <summary>Measurements taken off the last mesh actually built.</summary>
        public TrackMeshBuffer LastBuild { get { return _stats; } }

        /// <summary>
        /// A closed circuit to start from, so a new track is already a lap you can drive.
        ///
        /// Sized to land inside a 250 m terrain with room to spare — 160 x 120 m of node ellipse,
        /// about 178 x 138 m once the track's own width is counted. The first version of this was
        /// 240 x 160 m, picked so the lap would take a Mario-Kart-ish half minute, and on this
        /// project's 250 m island that came out covering the entire world. Lap time is the wrong
        /// thing to size a starting shape by: a circuit gets its length from winding, not from being
        /// enormous, and winding is what the author does next.
        ///
        /// Twelve evenly spaced nodes keep every corner clear of the 25 m minimum radius, so a new
        /// track never warns about itself.
        /// </summary>
        static List<TrackNode> DefaultOval()
        {
            var list = new List<TrackNode>();
            const int count = 12;
            for (int i = 0; i < count; i++)
            {
                float a = Mathf.PI * 2f * i / count;
                list.Add(new TrackNode(new Vector3(Mathf.Cos(a) * 80f, 0f, Mathf.Sin(a) * 60f),
                                       DefaultWidth));
            }
            return list;
        }

        /// <summary>
        /// Starting width of the racing surface, in metres.
        ///
        /// A kart in this project measures 1.65 m across, so this is eight and a half abreast — the
        /// eight-up racing asked for, with room to overtake, and close to the eight-to-ten karts a
        /// Mario Kart circuit actually runs. The first version was 20 m, which is over twelve
        /// abreast and reads as a runway rather than a road.
        /// </summary>
        public const float DefaultWidth = 14f;

        void OnEnable()
        {
            // Procedural meshes are not serialised with the scene, so rebuild after every load,
            // domain reload and play-mode transition.
            if (_mesh == null || _path == null) Generate();
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

        /// <summary>Rebuilds the track and assigns it to this object's filter and collider.</summary>
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
            target.name = "RaceTrack_" + name;

            _path = TrackPath.Build(nodes, settings);
            _stats = TrackMeshBuilder.Build(_path, settings);
            Fill(target, _stats);

            _mesh = target;
            filter.sharedMesh = target;

            if (updateCollider)
            {
                var collider = GetComponent<MeshCollider>();
                if (collider != null)
                {
                    // Reassigning the same mesh instance does not always rebuild the physics shape,
                    // so clear it first. Without this you drive on last build's track.
                    collider.sharedMesh = null;
                    collider.sharedMesh = target;
                }
            }
        }

        /// <summary>Builds a standalone mesh, for baking to an asset or for pooling at runtime.</summary>
        public static Mesh Create(IList<TrackNode> nodes, TrackSettings settings)
        {
            var mesh = new Mesh { name = "RaceTrack" };
            Fill(mesh, TrackMeshBuilder.Build(nodes, settings));
            return mesh;
        }

        static void Fill(Mesh mesh, TrackMeshBuffer buf)
        {
            mesh.Clear();
            // A long circuit passes the 16-bit vertex limit easily.
            mesh.indexFormat = buf.Vertices.Count > 65535 ? IndexFormat.UInt32 : IndexFormat.UInt16;

            mesh.SetVertices(buf.Vertices);
            mesh.SetNormals(buf.Normals);
            mesh.SetUVs(0, buf.UVs);

            mesh.subMeshCount = buf.Submeshes.Length;
            for (int i = 0; i < buf.Submeshes.Length; i++)
                mesh.SetTriangles(buf.Submeshes[i], i, false);

            mesh.RecalculateBounds();
            mesh.RecalculateTangents();
        }

        // ------------------------------------------------------------------ corners

        /// <summary>
        /// Radius of the circle through a node and its two neighbours, in metres. Infinity on a
        /// straight. Wraps around a closed loop, so every node on a lap has a radius.
        ///
        /// Two different limits are measured against this number and they are worth keeping apart.
        /// The geometric one is that a swept ribbon folds through itself once the radius drops below
        /// its own outermost half-width — around 12 m on a default 20 m track — and no setting can
        /// build that. The driving one is much stricter: a kart at 15 m/s wants 20-25 m, so a corner
        /// can be perfectly buildable and still be unraceable. Nothing enforces either; the corner
        /// radius is the author's to choose.
        /// </summary>
        public float TurnRadiusAt(int index)
        {
            return TrackLayout.TurnRadiusAt(nodes, index, IsClosed);
        }

        /// <summary>True when the settings ask for a loop and there are enough nodes to make one.</summary>
        public bool IsClosed { get { return settings.closedLoop && nodes.Count >= 3; } }

        /// <summary>Half-width of the widest part of the section at a node — the outside of the barrier.</summary>
        public float OuterHalfWidthAt(int index)
        {
            float full = settings.uniformWidth ? settings.trackWidth : nodes[index].width;
            return settings.OuterHalfWidth(Mathf.Max(1f, full) * 0.5f);
        }

        /// <summary>
        /// Corners tighter than <see cref="TrackSettings.minCornerRadius"/>, as (node index, radius
        /// in metres).
        /// </summary>
        public List<KeyValuePair<int, float>> FindTightCorners()
        {
            return TrackLayout.FindTightCorners(nodes, IsClosed, settings.minCornerRadius);
        }

        /// <summary>True when this corner is tight enough that the ribbon must fold through itself.</summary>
        public bool CornerFolds(int index)
        {
            return TurnRadiusAt(index) < OuterHalfWidthAt(index);
        }

        /// <summary>
        /// The node nearest a point on the solved path, so a problem measured on the curve can be
        /// reported against something the author can actually grab and move.
        /// </summary>
        public int NearestNodeTo(Vector3 localPosition)
        {
            int nearest = 0;
            float best = float.MaxValue;

            for (int i = 0; i < nodes.Count; i++)
            {
                float d = (nodes[i].position - localPosition).sqrMagnitude;
                if (d >= best) continue;
                best = d;
                nearest = i;
            }
            return nearest;
        }

        /// <summary>
        /// The node nearest the tightest place on the solved curve, with that radius. Returns -1 when
        /// the circuit has no bends at all.
        ///
        /// The curve is measured, not the node polygon, because those two disagree — a curve through
        /// unevenly spaced nodes bends harder between them than the circle through three nodes
        /// suggests. This is the measurement the warnings are built on.
        /// </summary>
        public int TightestSectionNode(out float radius)
        {
            radius = float.PositiveInfinity;
            TrackPath path = Path;
            if (path == null || path.Samples.Count == 0) return -1;

            int section;
            radius = path.TightestRadius(out section);
            if (section < 0) return -1;

            return NearestNodeTo(path.Samples[section].Position);
        }

        /// <summary>Tightest corner on the node polygon, in metres. Infinity when nothing bends.</summary>
        public float TightestNodeRadius()
        {
            return TrackLayout.TightestRadius(nodes, IsClosed);
        }

        /// <summary>
        /// Opens out corners tighter than <see cref="TrackSettings.minCornerRadius"/>, leaving
        /// everything already within tolerance alone. Safe to press repeatedly: it can never return a
        /// layout worse than the one it was given. See <see cref="TrackLayout.RelaxTightCorners"/>
        /// for why that is harder than it sounds.
        ///
        /// Returns the number of nodes actually shifted.
        /// </summary>
        public int RelaxTightCorners(int iterations = 60, float strength = 0.25f)
        {
            int moved = TrackLayout.RelaxTightCorners(nodes, IsClosed, settings.minCornerRadius,
                                                      iterations, strength);
            if (moved > 0) Generate();
            return moved;
        }

        // ------------------------------------------------------------------ editing

        /// <summary>
        /// Gap the segment starting at node <paramref name="index"/> must have before a node may be
        /// inserted into it. Inserting lands the new node midway, so each resulting half has to clear
        /// the minimum on its own — hence the doubling.
        /// </summary>
        public float MinimumGapBefore(int index)
        {
            int next = TrackPath.Next(index, nodes.Count, IsClosed);
            if (next == index) return 0f;

            float half = (OuterHalfWidthAt(index) + OuterHalfWidthAt(next)) * 0.5f;
            return half * settings.minNodeSpacing * 2f;
        }

        /// <summary>False when inserting here would pack the nodes tighter than the guard allows.</summary>
        public bool CanInsertBefore(int index)
        {
            if (settings.minNodeSpacing <= 0f) return true;

            int next = TrackPath.Next(index, nodes.Count, IsClosed);
            if (next == index) return false;

            float gap = Vector3.Distance(nodes[index].position, nodes[next].position);
            return gap >= MinimumGapBefore(index);
        }

        /// <summary>
        /// Respaces the nodes evenly along the current curve without changing its shape. Uneven node
        /// spacing is the usual cause of a corner that bulges or flicks, so this is the first thing
        /// to reach for when a bend looks wrong. Pass 0 to keep the present node count.
        /// </summary>
        public void RedistributeNodes(int count = 0)
        {
            if (count <= 0) count = nodes.Count;

            List<TrackNode> spread = TrackPath.Redistribute(nodes, settings, count);
            if (spread.Count < 2) return;

            nodes = spread;
            Generate();
        }

        /// <summary>Moves the whole circuit, nodes and all, without touching the transform.</summary>
        public void MoveAllBy(Vector3 delta)
        {
            for (int i = 0; i < nodes.Count; i++) nodes[i].position += delta;
            Generate();
        }

        /// <summary>Flattens the circuit to one height, in the generator's local space.</summary>
        public void SetAllHeights(float y)
        {
            for (int i = 0; i < nodes.Count; i++)
            {
                Vector3 p = nodes[i].position;
                nodes[i].position = new Vector3(p.x, y, p.z);
            }
            Generate();
        }

        /// <summary>
        /// Drops every node onto whatever solid ground is under it and then lifts it by
        /// <paramref name="clearance"/>. Optional: the track has no need of ground at all, and this
        /// is only here for when you want one that hugs the landscape.
        ///
        /// Nodes that find nothing beneath them are left where they are, so a circuit running out
        /// over open space keeps the height you gave it rather than dropping to the world floor.
        /// </summary>
        public int SnapNodesToGround(float clearance = 0.5f, float searchAbove = 500f, float searchBelow = 2000f)
        {
            int hits = 0;
            for (int i = 0; i < nodes.Count; i++)
            {
                Vector3 world = transform.TransformPoint(nodes[i].position);
                var start = new Vector3(world.x, world.y + searchAbove, world.z);

                RaycastHit hit;
                if (!Physics.Raycast(start, Vector3.down, out hit, searchAbove + searchBelow)) continue;

                // Ignore our own collider, or the track would snap onto itself.
                if (hit.collider != null && hit.collider.gameObject == gameObject) continue;

                nodes[i].position = transform.InverseTransformPoint(hit.point + Vector3.up * clearance);
                hits++;
            }
            Generate();
            return hits;
        }

        void OnDrawGizmosSelected()
        {
            if (nodes == null || nodes.Count < 2) return;

            // The racing line itself, so the layout is readable even where the mesh is hidden behind
            // terrain or behind another part of the circuit.
            Gizmos.color = new Color(1f, 0.75f, 0.2f, 0.9f);
            int spans = IsClosed ? nodes.Count : nodes.Count - 1;
            for (int i = 0; i < spans; i++)
            {
                Gizmos.DrawLine(transform.TransformPoint(nodes[i].position),
                                transform.TransformPoint(nodes[(i + 1) % nodes.Count].position));
            }
        }
    }
}
