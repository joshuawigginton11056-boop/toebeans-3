# Snow Trees

Three snow-laden conifers for Toebeans 3, built procedurally in C# rather than
imported as binary models — no `.fbx`/`.obj` in Git LFS, and the shape of every
tree is a diff you can read.

| Prefab | Silhouette | Size | Triangles |
| --- | --- | --- | --- |
| `SnowSpruce_A` | full spruce, broad skirt | 6.8 m tall, 4.0 m wide | ~34k |
| `SnowSpruce_B` | narrow steeple | 8.4 m tall, 3.3 m wide | ~41k |
| `SnowSpruce_C` | slim spire | 9.4 m tall, 2.6 m wide | ~51k |

Each tree is one mesh with three submeshes — `0` bark, `1` foliage, `2` snow —
matched by the three materials in `Materials/`. Trunks stand at the local
origin and grow up +Y, so a prefab drops straight onto terrain.

## Using them

* **Drag a prefab** from `Prefabs/` into a scene. The `SnowTree` component
  builds its mesh on enable, in edit mode and at runtime.
* **Reshape one** by ticking `Custom Settings` on the component and dragging
  the values — height, radius, tier count, bough count, snow scale, seed. The
  mesh rebuilds as you drag. `Randomise Seed` gives a fresh tree of the same
  species, which is how you fill a forest without the trees repeating.
* **Freeze them** with `Tools ▸ Toebeans ▸ Snow Trees ▸ Bake Meshes and
  Prefabs`. This writes real mesh assets plus prefabs (with a trunk capsule
  collider) into `Baked/`, so scenes carry static geometry with no component
  and no build cost. Re-baking keeps the existing mesh GUIDs.
* **New tree from scratch**: `GameObject ▸ 3D Object ▸ Toebeans ▸ Snow Tree`.

## How the geometry is put together

**Wood** — swept tubes: a tapered trunk, and roots splaying out of the ground
at the base.

**Needles** — each bough is a thin twig carrying a flat frond: a near-horizontal
pad of small needle blades, widest at the outer end so green fringes past the
snow rim. Every blade is emitted twice with opposing windings and normals so it
lights correctly from both sides under backface culling. Extra tufts push up
through the snow at each tier.

**Snow** — a flattened shelf lying along each bough, with a rim curling down off
its outer end. Neither is a separate object: both are pushed into a single
signed distance field (`SnowField`), smooth-unioned with a polynomial `smin`,
then meshed in one pass with naive surface nets, so shelves that touch fuse
with a soft fillet rather than intersecting as two shells. Shelf height is
capped against tier spacing and shelf width against the bough it sits on —
without those caps the tiers merge into one smooth column and the tree stops
reading as layered at all. Vertex normals come from the field gradient, so the
surface shades smoothly no matter how coarse the voxels are.

Tiers of boughs are placed up the trunk, their reach set by a per-species
`Profile` curve (the silhouette), with ±18% jitter per tier so the profile
stays irregular. Snow thins toward the crown, and above the top tier shrinking
lumps catch on a needle spire up to the point.

Randomness comes from a small deterministic LCG seeded per tree, so the same
settings always produce the same mesh — on any machine, in any Unity session.

## Cost

Smooth snow is the expensive part: `Snow Cell Scale` sets the voxel size as a
fraction of the tree's radius, and drives both build time and triangle count.
`0.055` is the authored look; `0.08`–`0.1` roughly halves the mesh for
background trees, at the cost of rounder, softer drifts. Bake the results if a
scene places many of them.
