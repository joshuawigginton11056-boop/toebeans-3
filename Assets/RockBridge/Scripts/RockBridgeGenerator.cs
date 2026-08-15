using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace RockBridge
{
    /// <summary>
    /// Builds a rock bridge onto this GameObject's MeshFilter and MeshCollider — a drivable deck
    /// carried over whatever is below it on rock legs that find their own length.
    ///
    /// The crossing is a cross-section swept along a curve through draggable nodes. Unlike a race
    /// circuit, the height is not in the nodes: the deck holds one level across the span and eases
    /// down onto the real ground at both ends, so
    /// <see cref="BridgeSettings.deckHeight"/> is a single slider that lifts the whole bridge and
    /// grows every leg to match, while the landings stay tied to the shore.
    ///
    /// The renderer expects four materials, in submesh order: deck, verge, parapet, rock.
    ///
    /// The mesh is built procedurally rather than shipped as a model, so every value you change is
    /// a live preview. Use "Save Mesh Asset" on the inspector to bake a finished bridge to a .asset.
    /// </summary>
    [ExecuteAlways]
    [AddComponentMenu("Rock Bridge/Rock Bridge Generator")]
    [RequireComponent(typeof(MeshFilter))]
    [RequireComponent(typeof(MeshRenderer))]
    public class RockBridgeGenerator : MonoBehaviour
    {
        [SerializeField] List<BridgeNode> nodes = DefaultCrossing();

        [SerializeField] BridgeSettings settings = new BridgeSettings();

        [Tooltip("Push the generated mesh onto a MeshCollider on this object, if there is one. " +
                 "Without this you can see the bridge but not drive on it.")]
        [SerializeField] bool updateCollider = true;

        [Tooltip("Rebuild as soon as a value changes in the inspector.")]
        [SerializeField] bool liveUpdate = true;

        Mesh _mesh;
        BridgePath _path;
        BridgeMeshBuffer _stats;

        public List<BridgeNode> Nodes { get { return nodes; } }
        public BridgeSettings Settings { get { return settings; } }

        /// <summary>The mesh currently on the filter, or null if nothing has been generated yet.</summary>
        public Mesh Mesh { get { return _mesh; } }

        /// <summary>Length of the crossing in metres, as of the last build.</summary>
        public float Length { get { return _path != null ? _path.Length : 0f; } }

        /// <summary>
        /// The solved crossing: positions, frames, heights, banking and widths.
        ///
        /// Rebuilt on demand. A domain reload leaves the mesh in place but drops this, and a caller
        /// that took the null at face value would conclude there is no bridge.
        /// </summary>
        public BridgePath Path
        {
            get
            {
                if (_path == null) Generate();
                return _path;
            }
        }

        /// <summary>Measurements taken off the last mesh actually built.</summary>
        public BridgeMeshBuffer LastBuild { get { return _stats; } }

        /// <summary>
        /// Two nodes, 140 m apart, which is a straight crossing wide enough to be worth bridging.
        /// Sized against this project's pools rather than against a lap time — the lava pond on
        /// LobbyIsland is about 95 x 114 m, so this reaches across it with a landing at each end.
        /// </summary>
        static List<BridgeNode> DefaultCrossing()
        {
            return new List<BridgeNode>
            {
                new BridgeNode(new Vector3(0f, 0f, -70f), DefaultWidth),
                new BridgeNode(new Vector3(0f, 0f, 0f), DefaultWidth),
                new BridgeNode(new Vector3(0f, 0f, 70f), DefaultWidth)
            };
        }

        /// <summary>
        /// Starting width of the driving surface, in metres.
        ///
        /// A kart in this project measures 1.65 m across, so this is nearly ten abreast. A bridge is
        /// a pinch point by nature and a twelve-kart field arrives at one all at once, so it is set
        /// wider than the 14 m a plain circuit runs.
        /// </summary>
        public const float DefaultWidth = 16f;

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

        // ------------------------------------------------------------------- ground

        /// <summary>
        /// Builds the sampler the height solver and the legs read the world through.
        ///
        /// Our own colliders are excluded, and that is not a nicety: a bridge that could see itself
        /// would measure the deck it just built as the ground, hold Deck Height above <em>that</em>,
        /// and climb by that much again on every single rebuild.
        /// </summary>
        public IBridgeGround BuildGroundSampler()
        {
            if (settings.groundMode == BridgeGroundMode.Flat)
                return new FlatBridgeGround(settings.flatGroundHeight);

            var terrain = new TerrainBridgeGround(FindTerrainUnderBridge());
            var colliders = new ColliderBridgeGround(settings.groundMask, ProbeUp, ProbeDown,
                                                     GetComponentsInChildren<Collider>(true));

            switch (settings.groundMode)
            {
                case BridgeGroundMode.Terrain:
                    return terrain.IsValid ? (IBridgeGround)terrain : new FlatBridgeGround(settings.flatGroundHeight);
                case BridgeGroundMode.Colliders:
                    return colliders;
                default:
                    return terrain.IsValid ? (IBridgeGround)new CompositeBridgeGround(terrain, colliders) : colliders;
            }
        }

        const float ProbeUp = 400f;
        const float ProbeDown = 2000f;


        /// <summary>
        /// The terrain the crossing sits over. Takes the one whose footprint actually contains the
        /// middle of the bridge, so a scene with several tiles picks the right one rather than
        /// whichever happens to be first in the array.
        /// </summary>
        public Terrain FindTerrainUnderBridge()
        {
            Terrain[] all = Terrain.activeTerrains;
            if (all == null || all.Length == 0) return null;
            if (all.Length == 1) return all[0];

            Vector3 middle = transform.TransformPoint(nodes.Count > 0
                ? nodes[nodes.Count / 2].position
                : Vector3.zero);

            foreach (Terrain t in all)
            {
                if (t == null || t.terrainData == null) continue;

                Vector3 origin = t.transform.position;
                Vector3 size = t.terrainData.size;
                if (middle.x < origin.x || middle.x > origin.x + size.x) continue;
                if (middle.z < origin.z || middle.z > origin.z + size.z) continue;
                return t;
            }
            return all[0];
        }

        /// <summary>
        /// True when this object has been tilted off world up.
        ///
        /// Worth checking rather than silently coping: the height automation carries a world height
        /// into local space as a plain Y, which is exact while the object's own up is world up and
        /// steadily less so as it is rolled over. Yaw and position are fine, and so is uniform scale.
        /// </summary>
        public bool IsTilted { get { return Vector3.Angle(transform.up, Vector3.up) > 0.5f; } }

        // ----------------------------------------------------------------- generate

        /// <summary>Rebuilds the bridge and assigns it to this object's filter and collider.</summary>
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
            target.name = "RockBridge_" + name;

            IBridgeGround ground = BuildGroundSampler();
            Matrix4x4 toWorld = transform.localToWorldMatrix;
            Matrix4x4 toLocal = transform.worldToLocalMatrix;

            // Switch our own colliders off for the duration of the probe.
            //
            // The sampler already skips them by reference, and that works — but it only has to fail
            // once. A bridge that reads its own deck as the ground holds Deck Height above *that*,
            // and the next rebuild reads the new deck and climbs again: it is a runaway, not a
            // wobble. It bit this project's own bridge, which arrived 10 m high with 22-degree ramps
            // and a crest that would have launched the field, and by the time it was noticed the
            // evidence was a stale path nobody could reproduce. Disabling the collider outright
            // removes the whole class of failure — no reference list to get stale, no ordering to
            // get wrong — for the cost of two lines.
            var ownColliders = GetComponentsInChildren<Collider>(true);
            var wasEnabled = new bool[ownColliders.Length];
            for (int i = 0; i < ownColliders.Length; i++)
            {
                wasEnabled[i] = ownColliders[i].enabled;
                ownColliders[i].enabled = false;
            }

            try
            {
                _path = BridgePath.Build(nodes, settings, ground, toWorld, toLocal);
                _stats = BridgeMeshBuilder.Build(_path, settings, new GroundProbe
                {
                    Ground = ground,
                    ToWorld = toWorld,
                    ToLocal = toLocal
                });
            }
            finally
            {
                for (int i = 0; i < ownColliders.Length; i++)
                    if (ownColliders[i] != null) ownColliders[i].enabled = wasEnabled[i];
            }

            Fill(target, _stats);

            _mesh = target;
            filter.sharedMesh = target;

            if (!updateCollider) return;

            var collider = GetComponent<MeshCollider>();
            if (collider == null) return;

            // Reassigning the same mesh instance does not always rebuild the physics shape, so
            // clear it first. Without this you drive on last build's bridge.
            collider.sharedMesh = null;
            collider.sharedMesh = target;
        }

        /// <summary>Builds a standalone mesh, for baking to an asset or for pooling at runtime.</summary>
        public static Mesh Create(IList<BridgeNode> nodes, BridgeSettings settings)
        {
            var mesh = new Mesh { name = "RockBridge" };
            Fill(mesh, BridgeMeshBuilder.Build(nodes, settings));
            return mesh;
        }

        static void Fill(Mesh mesh, BridgeMeshBuffer buf)
        {
            mesh.Clear();
            // A long bridge with faceted legs passes the 16-bit vertex limit easily.
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

        /// <summary>Half-width of the widest part of the section at a node — the outside of the parapet.</summary>
        public float OuterHalfWidthAt(int index)
        {
            float full = settings.uniformWidth ? settings.deckWidth : nodes[index].width;
            return settings.OuterHalfWidth(Mathf.Max(1.5f, full) * 0.5f);
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
                // Compared flat: on the automatic height modes the node's own Y is not where the
                // deck ended up, so counting it would pick the wrong node on a tall bridge.
                Vector3 d = nodes[i].position - localPosition;
                d.y = 0f;
                if (d.sqrMagnitude >= best) continue;
                best = d.sqrMagnitude;
                nearest = i;
            }
            return nearest;
        }

        /// <summary>
        /// The node nearest the tightest place on the solved curve, with that radius. Returns -1
        /// when the crossing has no bends at all.
        ///
        /// The curve is measured, not the node polygon, because those two disagree — a curve through
        /// unevenly spaced nodes bends harder between them than the circle through three nodes
        /// suggests. This is the measurement the warnings are built on.
        /// </summary>
        public int TightestSectionNode(out float radius)
        {
            radius = float.PositiveInfinity;
            BridgePath path = Path;
            if (path == null || path.Samples.Count == 0) return -1;

            int section;
            radius = path.TightestRadius(out section);
            if (section < 0) return -1;

            return NearestNodeTo(path.Samples[section].Position);
        }

        // ------------------------------------------------------------------ editing

        /// <summary>
        /// Respaces the nodes evenly along the current curve without changing its shape. Uneven node
        /// spacing is the usual cause of a corner that bulges or flicks, so this is the first thing
        /// to reach for when a bend looks wrong. Pass 0 to keep the present node count.
        /// </summary>
        public void RedistributeNodes(int count = 0)
        {
            if (count <= 0) count = nodes.Count;

            List<BridgeNode> spread = BridgePath.Redistribute(nodes, settings, BuildGroundSampler(),
                                                              transform.localToWorldMatrix,
                                                              transform.worldToLocalMatrix, count);
            if (spread.Count < 2) return;

            nodes = spread;
            Generate();
        }

        /// <summary>Moves the whole crossing, nodes and all, without touching the transform.</summary>
        public void MoveAllBy(Vector3 delta)
        {
            for (int i = 0; i < nodes.Count; i++) nodes[i].position += delta;
            Generate();
        }

        /// <summary>
        /// Drops every node onto the ground below it. Only useful on
        /// <see cref="BridgeHeightMode.Free"/> — the automatic modes work their own heights out and
        /// ignore the node's Y — so the inspector only offers it there.
        /// </summary>
        public int SnapNodesToGround(float clearance)
        {
            IBridgeGround ground = BuildGroundSampler();
            Matrix4x4 toWorld = transform.localToWorldMatrix;
            Matrix4x4 toLocal = transform.worldToLocalMatrix;
            int hits = 0;

            for (int i = 0; i < nodes.Count; i++)
            {
                Vector3 world = toWorld.MultiplyPoint3x4(nodes[i].position);

                GroundSample g;
                if (!ground.Sample(world, out g) || !g.Found) continue;

                Vector3 seated = toLocal.MultiplyPoint3x4(new Vector3(world.x, g.Surface, world.z));
                nodes[i].position = new Vector3(nodes[i].position.x, seated.y + clearance, nodes[i].position.z);
                hits++;
            }

            Generate();
            return hits;
        }


        void OnDrawGizmosSelected()
        {
            if (nodes == null || nodes.Count < 2) return;

            // The line through the nodes, so the layout is readable even where the mesh is hidden
            // behind terrain. Drawn flat at the nodes' own height, which on the automatic modes is
            // not where the deck is — the editor draws the real edges on top of this.
            Gizmos.color = new Color(0.9f, 0.65f, 0.35f, 0.5f);
            for (int i = 0; i < nodes.Count - 1; i++)
            {
                Gizmos.DrawLine(transform.TransformPoint(nodes[i].position),
                                transform.TransformPoint(nodes[i + 1].position));
            }
        }
    }
}
