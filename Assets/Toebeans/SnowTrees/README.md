# Snow Trees

Three snow-laden conifers for Toebeans 3, built procedurally in C# rather than
imported as binary models — no `.fbx`/`.obj` in Git LFS, and the shape of every
tree is a diff you can read.

| Prefab | Silhouette | Size | Triangles |
| --- | --- | --- | --- |
| `SnowSpruce_A` | full spruce, broad skirt | 6.7 m tall, 3.4 m wide | ~5.8k |
| `SnowSpruce_B` | narrow steeple | 8.3 m tall, 2.7 m wide | ~6.8k |
| `SnowSpruce_C` | slim spire | 9.3 m tall, 1.6 m wide | ~6.3k |

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

`SnowTreeMeshBuilder` grows a tree from two primitives:

* **Swept tube** — a ring of vertices carried along a path. Used for the
  tapered trunk, the exposed roots splaying out of the ground, every drooping
  bough (elliptical cross-section, so boughs read flat) and the needle sprigs
  that poke up between snow lumps.
* **Snow blob** — a squashed, randomly jittered dome with its rim tucked
  under, so it reads as a pillow overhanging a branch rather than a ball
  resting on one.

Tiers of boughs are placed up the trunk, their reach set by a per-species
`Profile` curve (the silhouette). Snow rides the inner half of each bough and
leaves the tip bare, which is what keeps green visible against all that white.
Above the top tier a single continuous needle spire carries three shrinking
snow caps to the point.

Randomness comes from a small deterministic LCG seeded per tree, so the same
settings always produce the same mesh — on any machine, in any Unity session.

`Flat Shading` (on by default) splits triangles so every face keeps a hard
normal, which is the faceted look the stylised kit uses. Turning it off welds
normals and roughly thirds the vertex count.
