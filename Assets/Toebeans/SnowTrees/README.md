# Snow Trees

Three snow-laden conifers for Toebeans 3, built procedurally in C# rather than
imported as binary models — no `.fbx`/`.obj` in Git LFS, and the shape of every
tree is a diff you can read.

| Prefab | Silhouette | Size | Triangles |
| --- | --- | --- | --- |
| `SnowSpruce_A` | full spruce, broad skirt | 6.6 m tall, 3.6 m wide | ~27k |
| `SnowSpruce_B` | narrow steeple | 8.3 m tall, 3.0 m wide | ~30k |
| `SnowSpruce_C` | slim spire | 9.3 m tall, 2.2 m wide | ~34k |

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

**Needles** — each bough is a thin twig carrying a feathered spray of small
needle blades, every blade emitted twice with opposing windings and normals so
it lights correctly from both sides under backface culling. Extra tufts push up
through the snow at each tier, which is what keeps green visible against all
that white.

**Snow** — not modelled as lumps at all. Each drift is a squashed sphere or a
tapered capsule pushed into a single signed distance field
(`SnowField`), smooth-unioned with a polynomial `smin`, then meshed in one pass
with naive surface nets. Two overlapping drifts therefore come out as *one*
continuous rounded mantle with a soft fillet between them, rather than two
intersecting shells — the difference between snow and a pile of boulders.
Vertex normals come from the field gradient, so the surface shades smoothly no
matter how coarse the voxels are.

Tiers of boughs are placed up the trunk, their reach set by a per-species
`Profile` curve (the silhouette). Drift size is the larger of "a fraction of
the bough" and "a fraction of the tier spacing", so tall narrow trees still
merge vertically into a cascade instead of stacking separate plates. Above the
top tier, one continuous drift tapers along a needle spire to the point.

Randomness comes from a small deterministic LCG seeded per tree, so the same
settings always produce the same mesh — on any machine, in any Unity session.

## Cost

Smooth snow is the expensive part: `Snow Cell Scale` sets the voxel size as a
fraction of the tree's radius, and drives both build time and triangle count.
`0.055` is the authored look; `0.08`–`0.1` roughly halves the mesh for
background trees, at the cost of rounder, softer drifts. Bake the results if a
scene places many of them.
