using System.Collections.Generic;
using UnityEngine;

namespace Volcano
{
    /// <summary>
    /// Holds drifting fog underneath a surface that gets driven on.
    ///
    /// A bridge over lava has fog coming off the lava below it, and by default that fog has no idea
    /// the bridge is there: it rises straight through the deck, and the driver is looking at the
    /// inside of a cloud on the one part of the map with a drop down either side. This is what
    /// tells it. Put it on the bridge and the fog underneath gets pressed flat against the soffit
    /// and pushed out towards the nearest edge, so it spills over the sides of the span instead of
    /// welling up through the road.
    ///
    /// The footprint is baked from the triangles of the surface's own mesh rather than from a box,
    /// which is what lets one component follow a curved deck of changing height without any of it
    /// being described twice. Rebuild the bridge and it rebakes itself.
    ///
    /// This does nothing on its own: the emitters read it. <see cref="LavaMist"/> and
    /// <see cref="VolcanoSmoke"/> each have a "Duck Under Shelters" toggle, on by default.
    /// </summary>
    [ExecuteAlways]
    [AddComponentMenu("Volcano/Mist Shelter")]
    public class MistShelter : MonoBehaviour
    {
        [Header("Surface")]
        [Tooltip("The mesh fog has to stay under. Defaults to the Mesh Filter on this object.")]
        public MeshFilter surface;

        [Tooltip("Use the whole mesh instead of one slot. Leave this off on a bridge: the deck is " +
                 "the only part fog has to stay under, and taking the legs in as well would put a " +
                 "lid over the lava they stand in.")]
        public bool wholeMesh = false;

        [Tooltip("First material slot of the surface fog has to stay under.\n\nRock Bridge puts the " +
                 "deck in slot 0.")]
        [Range(0, 7)] public int deckSubmesh = 0;

        [Tooltip("How many slots to take, counting on from that one.\n\nThis wants to cover " +
                 "everything along the top of the crossing, not just the lane: the strip outside " +
                 "the deck is still bridge, and fog let up through it comes out at the driver's " +
                 "elbow. Rock Bridge is deck, verge, parapet, rock - so 3 takes the whole top and " +
                 "leaves out the legs, which have to stay uncovered or the lid lands on the lava " +
                 "they are standing in.")]
        [Range(1, 8)] public int deckSlots = 3;

        [Header("Headroom")]
        [Tooltip("How far below the surface the fog is held, in metres. This wants to be at least " +
                 "the thickness of the slab, or the fog is held inside the deck rather than under " +
                 "it and shows through the road.")]
        [Range(0.1f, 20f)] public float clearance = 3f;

        [Tooltip("How far past the edge of the deck the lid still reaches, in metres. Fog is let " +
                 "go gradually across this band rather than at the edge itself, which is what " +
                 "keeps a wisp sliding out from underneath from snapping back to full size.")]
        [Range(0f, 60f)] public float margin = 14f;

        [Tooltip("How fast the lid climbs across that band, in metres of headroom per metre out. " +
                 "This is the shape of the billow coming off the sides.")]
        [Range(0f, 6f)] public float release = 1.1f;

        [Header("Flow")]
        [Tooltip("How hard fog under the deck is pushed towards the nearest edge, in metres per " +
                 "second. Zero leaves it to pool underneath and find its own way out.")]
        [Range(0f, 20f)] public float push = 3.5f;

        [Tooltip("How thin a wisp is allowed to be squashed, in metres. Only bites where there is " +
                 "almost no gap at all - under the ramps, where the deck comes down onto the ground.")]
        [Range(0.2f, 20f)] public float thinnest = 1.5f;

        [Header("Detail")]
        [Tooltip("Size of a footprint cell, in metres. Smaller follows the deck more closely and " +
                 "costs memory, and it is the deck's own shape that sets what is small enough: a " +
                 "cell has to be short enough that the road does not move much across one, which " +
                 "on a span that climbs and banks at the same time means a couple of metres.")]
        [Range(0.5f, 20f)] public float cellSize = 2f;

        [Tooltip("Draw the baked lid when this object is selected.")]
        public bool showFootprint = true;

        // How fast a caught wisp flattens out and shrinks. Both are per second, and both exist to
        // stop a wisp that drifts in from the side popping in one frame; the position clamp is not
        // rate limited, so nothing pokes through the deck while these two catch up.
        const float LevelRate = 70f;
        const float SquashRate = 1.5f;

        // How far below the lid the shelter still claims, for the broad-phase test only.
        const float Reach = 400f;

        // A grid this big is already far past useful. Past it the cell size is doubled rather than
        // the bake refused: a coarse lid beats no lid.
        const int MaxCells = 262144;

        // The baked lid, one entry per cell. NaN means "no lid here".
        float[] _ceiling;
        Vector2[] _outward;
        float[] _hold;

        float _originX, _originZ, _cell;
        int _nx, _nz;
        bool _ready;
        Bounds _bounds;

        // What the last bake was made from, so a rebuilt bridge is noticed without anything having
        // to tell us about it.
        Mesh _bakedMesh;
        int _bakedVertexCount;
        Matrix4x4 _bakedFrame;

        static readonly List<MistShelter> Active = new List<MistShelter>();

        /// <summary>True once there is a lid to query.</summary>
        public bool Ready { get { return _ready; } }

        /// <summary>World box the lid covers, generous downwards. Broad-phase only.</summary>
        public Bounds Bounds { get { return _bounds; } }

        /// <summary>Cells in the baked footprint, for the inspector's report.</summary>
        public int CellCount { get { return _ceiling != null ? _ceiling.Length : 0; } }

        /// <summary>Metres per cell as baked, which is not the setting when the grid was capped.</summary>
        public float BakedCellSize { get { return _cell; } }

        void OnEnable()
        {
            if (!Active.Contains(this)) Active.Add(this);
            Rebake();
        }

        void OnDisable()
        {
            Active.Remove(this);
        }

        void OnValidate()
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.delayCall += () =>
            {
                if (this == null) return;
                Rebake();
            };
#else
            Rebake();
#endif
        }

        /// <summary>Notices a rebuilt or moved surface and rebakes off it.</summary>
        void LateUpdate()
        {
            MeshFilter filter = Surface;
            Mesh mesh = filter != null ? filter.sharedMesh : null;

            if (mesh == _bakedMesh &&
                (mesh == null || mesh.vertexCount == _bakedVertexCount) &&
                (filter == null || filter.transform.localToWorldMatrix == _bakedFrame))
                return;

            Rebake();
        }

        MeshFilter Surface
        {
            get { return surface != null ? surface : GetComponent<MeshFilter>(); }
        }

        // ------------------------------------------------------------------ baking

        /// <summary>Rebuilds the lid from the surface mesh as it stands now.</summary>
        [ContextMenu("Rebake")]
        public void Rebake()
        {
            MeshFilter filter = Surface;
            Mesh mesh = filter != null ? filter.sharedMesh : null;

            // Recorded before any of the ways out below, so a surface that cannot be baked is not
            // retried on every frame for the rest of the session.
            _bakedMesh = mesh;
            _bakedVertexCount = mesh != null ? mesh.vertexCount : 0;
            _bakedFrame = filter != null ? filter.transform.localToWorldMatrix : Matrix4x4.identity;
            _ready = false;

            if (mesh == null) return;
            if (!mesh.isReadable)
            {
                Debug.LogWarning("Mist Shelter: the mesh on '" + name + "' is not readable, so its " +
                                 "footprint cannot be measured. Tick Read/Write on the model, or " +
                                 "point this at a generated mesh.", this);
                return;
            }

            int[] tris = Triangles(mesh);
            if (tris == null || tris.Length < 3) return;

            Vector3[] local = mesh.vertices;
            Transform frame = filter.transform;
            var world = new Vector3[local.Length];
            for (int i = 0; i < local.Length; i++) world[i] = frame.TransformPoint(local[i]);

            // Only the vertices this submesh actually uses: on a bridge the rest of the mesh is
            // legs and landings running the full height of the crossing.
            float minX = float.MaxValue, minZ = float.MaxValue;
            float maxX = float.MinValue, maxZ = float.MinValue;
            for (int i = 0; i < tris.Length; i++)
            {
                Vector3 v = world[tris[i]];
                if (v.x < minX) minX = v.x;
                if (v.x > maxX) maxX = v.x;
                if (v.z < minZ) minZ = v.z;
                if (v.z > maxZ) maxZ = v.z;
            }

            float cell = Mathf.Max(0.5f, cellSize);
            float pad = Mathf.Max(0f, margin) + cell;
            int nx = Mathf.CeilToInt((maxX - minX + pad * 2f) / cell) + 1;
            int nz = Mathf.CeilToInt((maxZ - minZ + pad * 2f) / cell) + 1;

            while ((long)nx * nz > MaxCells)
            {
                cell *= 2f;
                pad = Mathf.Max(0f, margin) + cell;
                nx = Mathf.CeilToInt((maxX - minX + pad * 2f) / cell) + 1;
                nz = Mathf.CeilToInt((maxZ - minZ + pad * 2f) / cell) + 1;
            }

            _cell = cell;
            _nx = nx;
            _nz = nz;
            _originX = minX - pad;
            _originZ = minZ - pad;

            int n = nx * nz;
            var deck = new float[n];
            var covered = new bool[n];
            for (int i = 0; i < n; i++) deck[i] = float.PositiveInfinity;

            // Each triangle is splatted over the cells its footprint touches. Deliberately the
            // whole footprint rather than a proper rasterisation: erring outwards puts the edge of
            // the lid a cell past the edge of the deck, and fog held a little wide of a bridge is
            // invisible where fog let through a little short of one is a cloud on the road.
            //
            // What each cell keeps is the LOWEST corner of every triangle that reached it, and
            // that direction is not arbitrary. A cell covers a few metres of a deck that is
            // climbing and banked at the same time, so the road inside one cell spans close to a
            // metre of height, and a splat drags a triangle's value a cell further again. Keeping
            // the highest corner puts the lid above the road somewhere in every one of those cells,
            // which is exactly where fog was still coming through: measured 0.70 m proud of the
            // roundabout's deck and 0.31 m proud of the pond bridge's. Keeping the lowest costs a
            // little headroom under the ramps, where there is no lava anyway.
            for (int t = 0; t + 2 < tris.Length; t += 3)
            {
                Vector3 a = world[tris[t]];
                Vector3 b = world[tris[t + 1]];
                Vector3 c = world[tris[t + 2]];

                float low = Mathf.Min(a.y, Mathf.Min(b.y, c.y));
                int x0 = ClampX(CellX(Mathf.Min(a.x, Mathf.Min(b.x, c.x))));
                int x1 = ClampX(CellX(Mathf.Max(a.x, Mathf.Max(b.x, c.x))));
                int z0 = ClampZ(CellZ(Mathf.Min(a.z, Mathf.Min(b.z, c.z))));
                int z1 = ClampZ(CellZ(Mathf.Max(a.z, Mathf.Max(b.z, c.z))));

                for (int z = z0; z <= z1; z++)
                {
                    int row = z * nx;
                    for (int x = x0; x <= x1; x++)
                    {
                        int i = row + x;
                        covered[i] = true;
                        if (low < deck[i]) deck[i] = low;
                    }
                }
            }

            _ceiling = new float[n];
            _outward = new Vector2[n];
            _hold = new float[n];
            for (int i = 0; i < n; i++) _ceiling[i] = float.NaN;

            var source = new int[n];
            var steps = new int[n];
            var queue = new int[n];

            // Inwards from the open ground just outside the deck. What each covered cell wants out
            // of this is which way is out: that is the direction the fog under it gets pushed.
            Reset(source, steps);
            int tail = Seed(queue, source, steps, covered, false);
            Flood(queue, source, steps, covered, true, tail, int.MaxValue);

            for (int i = 0; i < n; i++)
            {
                if (!covered[i]) continue;
                _ceiling[i] = deck[i] - clearance;
                _hold[i] = 1f;
                _outward[i] = source[i] >= 0 ? Away(i, source[i]) : Vector2.zero;
            }

            // Outwards from the edge of the deck, as far as the margin, lifting the lid as it goes.
            if (margin > 0.001f)
            {
                Reset(source, steps);
                tail = Seed(queue, source, steps, covered, true);
                Flood(queue, source, steps, covered, false, tail, Mathf.CeilToInt(margin / cell));

                for (int i = 0; i < n; i++)
                {
                    if (covered[i] || source[i] < 0) continue;
                    float d = steps[i] * cell;
                    _ceiling[i] = deck[source[i]] - clearance + release * d;
                    _hold[i] = Mathf.Clamp01(1f - d / Mathf.Max(0.001f, margin));
                    _outward[i] = Away(source[i], i);
                }
            }

            float lowest = float.MaxValue, highest = float.MinValue;
            for (int i = 0; i < n; i++)
            {
                if (float.IsNaN(_ceiling[i])) continue;
                if (_ceiling[i] < lowest) lowest = _ceiling[i];
                if (_ceiling[i] > highest) highest = _ceiling[i];
            }
            if (lowest > highest) return;

            _bounds = new Bounds();
            _bounds.SetMinMax(new Vector3(_originX, lowest - Reach, _originZ),
                              new Vector3(_originX + nx * cell, highest, _originZ + nz * cell));
            _ready = true;
        }

        readonly List<int> _tris = new List<int>();
        readonly List<int> _slot = new List<int>();

        int[] Triangles(Mesh mesh)
        {
            if (wholeMesh || mesh.subMeshCount <= 1) return mesh.triangles;

            int first = Mathf.Clamp(deckSubmesh, 0, mesh.subMeshCount - 1);
            int last = Mathf.Min(first + Mathf.Max(1, deckSlots) - 1, mesh.subMeshCount - 1);

            _tris.Clear();
            for (int s = first; s <= last; s++)
            {
                _slot.Clear();
                mesh.GetTriangles(_slot, s);
                _tris.AddRange(_slot);
            }

            return _tris.ToArray();
        }

        static void Reset(int[] source, int[] steps)
        {
            for (int i = 0; i < source.Length; i++)
            {
                source[i] = -1;
                steps[i] = int.MaxValue;
            }
        }

        /// <summary>
        /// Queues every cell on its side of the boundary. Each one is its own source, so a cell
        /// reached later carries the boundary cell it was reached from.
        /// </summary>
        int Seed(int[] queue, int[] source, int[] steps, bool[] covered, bool wantCovered)
        {
            int tail = 0;
            for (int z = 0; z < _nz; z++)
            {
                for (int x = 0; x < _nx; x++)
                {
                    int i = z * _nx + x;
                    if (covered[i] != wantCovered) continue;
                    if (!Borders(covered, x, z, !wantCovered)) continue;

                    source[i] = i;
                    steps[i] = 0;
                    queue[tail++] = i;
                }
            }
            return tail;
        }

        bool Borders(bool[] covered, int x, int z, bool value)
        {
            if (x > 0 && covered[z * _nx + x - 1] == value) return true;
            if (x + 1 < _nx && covered[z * _nx + x + 1] == value) return true;
            if (z > 0 && covered[(z - 1) * _nx + x] == value) return true;
            if (z + 1 < _nz && covered[(z + 1) * _nx + x] == value) return true;
            return false;
        }

        /// <summary>Breadth first, so the first visit to a cell is the shortest way to it.</summary>
        void Flood(int[] queue, int[] source, int[] steps, bool[] covered, bool into, int tail,
                   int maxSteps)
        {
            int head = 0;
            while (head < tail)
            {
                int i = queue[head++];
                if (steps[i] >= maxSteps) continue;

                int x = i % _nx;
                int z = i / _nx;

                if (x > 0) tail = Visit(queue, source, steps, covered, into, i, i - 1, tail);
                if (x + 1 < _nx) tail = Visit(queue, source, steps, covered, into, i, i + 1, tail);
                if (z > 0) tail = Visit(queue, source, steps, covered, into, i, i - _nx, tail);
                if (z + 1 < _nz) tail = Visit(queue, source, steps, covered, into, i, i + _nx, tail);
            }
        }

        static int Visit(int[] queue, int[] source, int[] steps, bool[] covered, bool into,
                         int from, int to, int tail)
        {
            if (covered[to] != into) return tail;
            if (steps[to] != int.MaxValue) return tail;

            steps[to] = steps[from] + 1;
            source[to] = source[from];
            queue[tail++] = to;
            return tail;
        }

        /// <summary>Unit direction from one cell to another, on the XZ plane.</summary>
        Vector2 Away(int from, int to)
        {
            var d = new Vector2((to % _nx) - (from % _nx), (to / _nx) - (from / _nx));
            return d.sqrMagnitude > 1e-6f ? d.normalized : Vector2.zero;
        }

        int CellX(float x) { return Mathf.FloorToInt((x - _originX) / _cell); }
        int CellZ(float z) { return Mathf.FloorToInt((z - _originZ) / _cell); }
        int ClampX(int x) { return Mathf.Clamp(x, 0, _nx - 1); }
        int ClampZ(int z) { return Mathf.Clamp(z, 0, _nz - 1); }

        Vector3 CellCentre(int i, float y)
        {
            return new Vector3(_originX + ((i % _nx) + 0.5f) * _cell, y,
                               _originZ + ((i / _nx) + 0.5f) * _cell);
        }

        // ------------------------------------------------------------------ querying

        /// <summary>
        /// The lid over a point, if there is one. <paramref name="outward"/> is the way out from
        /// under the deck on the XZ plane, and <paramref name="hold"/> falls from 1 under the deck
        /// to 0 at the far side of the margin.
        /// </summary>
        public bool TryQuery(float x, float z, out float ceiling, out Vector2 outward, out float hold)
        {
            ceiling = 0f;
            outward = Vector2.zero;
            hold = 0f;
            if (!_ready) return false;

            int cx = CellX(x);
            if (cx < 0 || cx >= _nx) return false;
            int cz = CellZ(z);
            if (cz < 0 || cz >= _nz) return false;

            int i = cz * _nx + cx;
            float c = _ceiling[i];
            if (float.IsNaN(c)) return false;

            ceiling = c;
            outward = _outward[i];
            hold = _hold[i];
            return true;
        }

        /// <summary>
        /// The lowest lid over a point across every shelter in the scene, and the shelter it
        /// belongs to. Two overlapping bridges are not a case anyone has built, but the lower deck
        /// is the one the fog would come up through.
        /// </summary>
        public static MistShelter Sample(Vector3 point, out float ceiling, out Vector2 outward,
                                         out float hold)
        {
            ceiling = 0f;
            outward = Vector2.zero;
            hold = 0f;
            MistShelter found = null;

            for (int i = 0; i < Active.Count; i++)
            {
                MistShelter s = Active[i];
                if (s == null) continue;

                float c;
                Vector2 o;
                float h;
                if (!s.TryQuery(point.x, point.z, out c, out o, out h)) continue;
                if (found != null && c >= ceiling) continue;

                ceiling = c;
                outward = o;
                hold = h;
                found = s;
            }

            return found;
        }

        // ------------------------------------------------------------------ confining

        /// <summary>
        /// Holds a system's particles under whatever shelters they have drifted into, and pushes
        /// the caught ones out sideways. Call it once a frame, after the system has moved.
        ///
        /// Three things happen to a wisp that would otherwise come up through a deck, in order: it
        /// levels off, because a lump tumbling end over end needs its widest measurement of
        /// headroom where a flat one needs its thinnest; it shrinks, but only as far as it has to
        /// and only so fast; and its position is clamped outright, which is the part that actually
        /// guarantees nothing is ever drawn above the road while the other two catch up.
        /// </summary>
        public static void Confine(ParticleSystem system, ParticleSystemRenderer view,
                                   ref ParticleSystem.Particle[] buffer, float lumpiness,
                                   float deltaTime)
        {
            if (system == null || Active.Count == 0 || deltaTime <= 0f) return;

            int alive = system.particleCount;
            if (alive == 0) return;

            // Nearly every system on a map is nowhere near a bridge, and this is what keeps them
            // from paying for one: no particle is read back at all unless the cloud overlaps a lid.
            if (view != null)
            {
                Bounds cloud = view.bounds;
                bool near = false;
                for (int i = 0; i < Active.Count && !near; i++)
                    near = Active[i] != null && Active[i]._ready && Active[i]._bounds.Intersects(cloud);
                if (!near) return;
            }

            if (buffer == null || buffer.Length < alive)
                buffer = new ParticleSystem.Particle[Mathf.Max(alive, system.main.maxParticles)];

            int count = system.GetParticles(buffer);
            float lump = 1f + Mathf.Max(0f, lumpiness);

            // A system whose sizes are one number per particle rather than three cannot be
            // flattened - writing a thin Y onto it does nothing - so those shrink all over instead.
            bool perAxis = system.main.startSize3D;

            // Everything below is compared against a lid in world metres, and a particle's size is
            // not in world metres until the system's scaling mode has been taken off it. The Lava
            // Pond's mist is the case that catches this: the emitter sits on a pond at 4x.
            float scale = SizeScale(system);
            bool touched = false;

            for (int i = 0; i < count; i++)
            {
                ParticleSystem.Particle p = buffer[i];
                Vector3 pos = p.position;

                float ceiling;
                Vector2 outward;
                float hold;
                MistShelter shelter = Sample(pos, out ceiling, out outward, out hold);
                if (shelter == null) continue;

                Vector3 semi = p.GetCurrentSize3D(system) * scale;
                Quaternion spin = Quaternion.Euler(p.rotation3D);
                float half = VerticalHalf(spin, semi, lump);

                // Already clear of the crossing. A plume passing a bridge on its way up is not what
                // this is for, and dragging it back down would cut a notch out of the column.
                //
                // Measured against the surface itself, not against the lid, and the difference is
                // not academic. The lid sits a clearance below the road and climbs away from the
                // edges, so a puff that wandered in from the side is routinely above the lid while
                // still buried in the deck - and letting that one go is what was still putting fog
                // through the parapet of the roundabout and through the pond bridge's lane.
                if (pos.y - half >= ceiling + shelter.clearance) continue;
                if (pos.y + half <= ceiling) continue;

                if (perAxis) p.rotation3D = Level(p.rotation3D, LevelRate * deltaTime);

                float allowed = Mathf.Max(shelter.thinnest, (ceiling - (pos.y - half)) * 0.5f);
                if (allowed < half)
                {
                    float target = Mathf.Max(allowed, half * (1f - SquashRate * deltaTime));
                    if (perAxis) p.startSize3D = Squash(spin, semi, p.startSize3D, target, lump);
                    else p.startSize *= target / half;
                    half = target;
                }

                pos.y = Mathf.Min(pos.y, ceiling - half);
                p.position = pos;

                float speed = shelter.push * hold;
                Vector3 v = p.velocity;
                float step = speed * 3f * deltaTime + 0.05f;
                v.x = Mathf.MoveTowards(v.x, outward.x * speed, step);
                v.z = Mathf.MoveTowards(v.z, outward.y * speed, step);
                if (v.y > 0f) v.y = 0f;
                p.velocity = v;

                buffer[i] = p;
                touched = true;
            }

            if (touched) system.SetParticles(buffer, count);
        }

        /// <summary>
        /// What a particle's size has to be multiplied by to be in metres. Shape mode already is;
        /// the other two carry the emitter's scale, which is exactly why Lava Mist asks for Shape.
        /// </summary>
        static float SizeScale(ParticleSystem system)
        {
            Vector3 s;
            switch (system.main.scalingMode)
            {
                case ParticleSystemScalingMode.Hierarchy: s = system.transform.lossyScale; break;
                case ParticleSystemScalingMode.Local: s = system.transform.localScale; break;
                default: return 1f;
            }

            return Mathf.Max(Mathf.Abs(s.x), Mathf.Max(Mathf.Abs(s.y), Mathf.Abs(s.z)));
        }

        /// <summary>
        /// Half the height of a puff standing the way it is standing now. The mesh is a unit blob,
        /// so its semi-axes are the particle's size, and the reach of a rotated ellipsoid along one
        /// axis is the length of that row of the scaled rotation - not the largest semi-axis, which
        /// is what a bounding sphere would say and is up to three times more room than it needs.
        /// </summary>
        static float VerticalHalf(Quaternion spin, Vector3 semi, float lump)
        {
            float ax = (spin * new Vector3(semi.x, 0f, 0f)).y;
            float ay = (spin * new Vector3(0f, semi.y, 0f)).y;
            float az = (spin * new Vector3(0f, 0f, semi.z)).y;
            return lump * Mathf.Sqrt(ax * ax + ay * ay + az * az);
        }

        /// <summary>
        /// Scales a puff down until it is <paramref name="target"/> tall: off its own thin axis
        /// where that reaches, and off all three where it does not.
        ///
        /// Which of the two it is matters more than it sounds. A wisp is a pancake, so once it has
        /// levelled off nearly all of its height is its own thin axis, and taking that alone costs
        /// nothing anyone can see. Shrinking all three is what a lid over a bridge would otherwise
        /// do to the fog bank - thinning it exactly where it is meant to be piling up and spilling
        /// out of the sides.
        /// </summary>
        static Vector3 Squash(Quaternion spin, Vector3 semi, Vector3 startSize, float target,
                              float lump)
        {
            float ax = (spin * new Vector3(semi.x, 0f, 0f)).y;
            float ay = (spin * new Vector3(0f, semi.y, 0f)).y;
            float az = (spin * new Vector3(0f, 0f, semi.z)).y;

            float want = target / lump;
            float spare = want * want - ax * ax - az * az;

            if (spare > 0.0001f && Mathf.Abs(ay) > 1e-4f)
            {
                float k = Mathf.Clamp01(Mathf.Sqrt(spare) / Mathf.Abs(ay));
                return new Vector3(startSize.x, startSize.y * k, startSize.z);
            }

            float uniform = Mathf.Clamp01(target / Mathf.Max(1e-4f, VerticalHalf(spin, semi, lump)));
            return startSize * uniform;
        }

        /// <summary>
        /// Rolls a puff level. Towards the nearest half turn rather than towards zero, because the
        /// blob is symmetrical enough that flipping it over to get there would be a visible tumble
        /// for no gain.
        /// </summary>
        static Vector3 Level(Vector3 euler, float step)
        {
            return new Vector3(Flatten(euler.x, step), euler.y, Flatten(euler.z, step));
        }

        static float Flatten(float angle, float step)
        {
            return Mathf.MoveTowards(angle, Mathf.Round(angle / 180f) * 180f, step);
        }

        // ------------------------------------------------------------------ gizmo

        void OnDrawGizmosSelected()
        {
            if (!showFootprint || !_ready) return;

            var solid = new Color(1f, 0.55f, 0.2f, 0.5f);
            var band = new Color(0.4f, 0.7f, 1f, 0.25f);
            var size = new Vector3(_cell * 0.9f, 0.02f, _cell * 0.9f);

            for (int i = 0; i < _ceiling.Length; i++)
            {
                float c = _ceiling[i];
                if (float.IsNaN(c)) continue;

                Gizmos.color = _hold[i] >= 0.999f ? solid : band;
                Gizmos.DrawWireCube(CellCentre(i, c), size);
            }
        }
    }
}
