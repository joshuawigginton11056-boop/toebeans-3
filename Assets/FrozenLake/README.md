# Frozen Lake (Low Poly)

A flat-shaded frozen lake built for stylised/low-poly scenes: a cracked ice sheet ringed by a snow
berm, with heaved ice shards, snow drifts and boulders on top, sitting on a solid tapered block so
it reads as a chunk of terrain rather than a flat plane.

Roughly **2,100 triangles** at the default settings, and about **29 x 32 m** across. It can also be
generated with a smashed hole through the middle that a character can fall through.

Two prefabs ship with it:

| Prefab | Use |
|---|---|
| `FrozenLake.prefab` | intact ice, nothing to fall through |
| `FrozenLake_Holed.prefab` | same lake with a smashed hole and an open shaft |

## Using it

Drag either prefab into a scene, or use **GameObject → 3D Object → Frozen Lake (Low Poly)**.

The mesh is generated procedurally from a seed rather than shipped as a binary model, so:

* it rebuilds automatically whenever the scene loads or scripts recompile — nothing to import;
* every setting in the inspector is a live preview;
* one prefab covers an unlimited number of lakes. Drop several in a scene and give each a different
  seed and they will all look different.

Press **Randomise Seed** in the inspector until you like one. Press **Save Mesh Asset…** if you would
rather bake that particular lake down to a static `.asset` and drop the generator component.

## The ice material

`Materials/FL_Ice_Surface.mat` is the dark, smooth, cracked ice — the look that came out of the
mockup round. It is a plain material: **drop it on any flat surface**, including a stock Unity
Plane. It does not need the generated mesh.

Everything is procedural, computed from world-space position:

* **no textures**, so nothing here goes near Git LFS;
* **no tiling and no UVs** — the pattern never repeats and does not stretch when the surface is
  scaled or moved;
* cracks fade out once they are thinner than a pixel, so they do not crawl or shimmer in the
  distance.

The dark-underfoot, bright-at-the-horizon falloff is Fresnel, and it comes from Unity's own PBR
rather than anything hand-rolled. Reflections come from whatever **Reflection Probe** covers the
surface; with none in range it falls back to the skybox.

### Tuning it

| Property | What it does |
|---|---|
| `Main Spacing (m)` | Distance between the big cracks, in metres. The main knob. |
| `Main Width` / `Main Strength` | How thick and how visible those cracks are. |
| `Detail Spacing (m)` | The finer network laid over the top. |
| `Edge Sharpness` | Higher gives tighter, harder-edged lines. |
| `Wander (m)` | How far cracks stray from a straight Voronoi edge. 0 gives clean polygons. |
| `Ice Smoothness` | 0.97 is a mirror. Drop it for a duller, more weathered surface. |
| `Crack Relief` | How much cracks catch the light as grooves. |
| `Seed` | Reshuffles the whole pattern. |

Crack spacing is in **metres**, so the right value depends on how close the camera gets. The
shipped defaults were tuned for a mid-distance view; from a low camera you will probably want
`Main Spacing` lower, or you will go a long way between cracks.

### Cost

Each pixel evaluates two Voronoi layers three times (once for colour, twice more for the relief
normal) plus noise for the wander and mottling. That is fine for a surface on desktop, but it is
not a cheap shader. If it shows up in a profile, the first thing to drop is `Crack Relief` to 0 —
that removes two thirds of the work.

## Materials

The renderer takes four materials, in submesh order. All four are plain URP/Lit, so retint them or
swap them for your own without touching the mesh.

| # | Material       | Where it lands                                   |
|---|----------------|--------------------------------------------------|
| 0 | `FL_Ice_Pale`  | most of the ice sheet, plus some shards           |
| 1 | `FL_Ice_Deep`  | darker clear ice toward the middle, plus shards   |
| 2 | `FL_Snow`      | the berm, snow-covered plates, drifts on the ice  |
| 3 | `FL_Rock`      | boulders, exposed rock on the berm, the underside |

The mesh also carries **vertex colours** matching those four tints, so a vertex-colour shader can
render the whole thing in a single draw call if you would rather not use four materials.

UVs are planar, projected on whichever axis each face points along most strongly, at
`uvScale` world units per tile.

## The hole

Tick **hole** and the ice sheet loses every facet whose centre falls inside a ragged outline, which
leaves a torn edge along the existing triangles rather than a cut circle. The exposed edge gets a
broken face hanging off it so the sheet shows its thickness, and slabs are thrown clear onto the
surrounding ice.

With **holeOpensThrough** on (the default) the floor of the block is cut to match and a shaft is
built down to it, so the hole is something a character can fall through rather than just look into.

### Dropping something through it

Anything inside the clear column falls straight through — no debris, overhanging slab or shaft wall
is allowed to intrude on it:

```csharp
Vector3 center;
float radius;
if (lake.TryGetDropPoint(out center, out radius))
{
    // center is the mouth of the hole in world space; radius is the safe column around it.
    player.position = center + Vector3.up * 2f;
}
```

Select the object in the scene and the column is drawn as a gizmo, so you can see where to put a
trigger volume without guessing. `lake.Hole` gives the same numbers in local space, plus the shaft
depth.

Note this is authored geometry, not runtime destruction. To show ice breaking during play, put both
prefabs in the scene and swap which one is active behind a burst of particles — far cheaper than
cutting a mesh live, and it behaves identically every time.

## Settings worth knowing

| Setting                | Effect                                                                 |
|------------------------|------------------------------------------------------------------------|
| `seed`                 | Everything. Same seed always gives the same lake, on every platform.    |
| `radius`               | Overall size. `angularSegments` / `radialRings` drive the poly budget.  |
| `shoreIrregularity`    | 0 gives a round pond; 0.45 gives a rambling shoreline.                  |
| `plateCount`           | How many plates the ice cracks into. Fewer = bigger, flatter slabs.     |
| `plateHeightVariation` | Step between neighbouring plates. This is what reads as cracks.         |
| `bankWidth` / `bankHeight` | The snow berm. Set both to 0 for bare ice with no shore.            |
| `depth`                | Thickness of the solid block underneath. 0 leaves just the surface.     |
| `hole`                 | Smash a hole through the ice. See above.                                |
| `holeRadius`           | Size of the opening. Capped at 62% of the lake radius.                  |
| `holeOffsetX/Z`        | Move it off centre, as a fraction of the lake radius. Auto-clamped so the rim stays on ice. |
| `iceThickness`         | How thick the sheet looks where it broke.                               |

Turning `shardCount`, `snowPatchCount` and `rockCount` down to 0 leaves a clean ice sheet, which is
a good starting point if you want to scatter your own props instead.

## Performance notes

* The whole thing is one mesh with four submeshes — four draw calls, or one with a vertex-colour
  shader. Enable GPU instancing on the materials if you place many lakes.
* Generation is cheap (a few ms at default settings) but it is not free. It runs on load, not per
  frame. If you are spawning lakes at runtime, call `FrozenLakeGenerator.Create(settings)` once and
  reuse the mesh.
* The `MeshCollider` uses the full mesh. For a lake you only ever walk on, a flat box or a baked
  simplified collider will be considerably cheaper.

## Files

```
Shaders/StylisedIce.shader     the dark cracked ice, URP
Shaders/IceCracks.hlsl         the crack pattern; no Unity dependencies, compiles standalone
Shaders/StylisedIceInput.hlsl  material properties and the surface function
Materials/FL_Ice_Surface.mat   ready to drop on a Plane
FrozenLake.prefab              intact lake, materials wired up
FrozenLake_Holed.prefab        same lake with the hole punched through
Scripts/FrozenLakeGenerator.cs the MonoBehaviour: builds the mesh, owns its lifetime
Scripts/FrozenLakeSettings.cs  every tunable, in one serializable class
Scripts/FrozenLakeMeshBuilder.cs  the geometry itself: maths in, triangles out
Scripts/MeshBuffer.cs          flat-shaded triangle accumulator, planar UVs, vertex colours
Scripts/LakeNoise.cs           deterministic rng and value noise
Editor/                        inspector buttons and the GameObject menu entry
Materials/                     the four URP/Lit materials
```

`FrozenLakeMeshBuilder` has no dependency on the scene, on assets, or on Unity's global random
state, so it can be driven from a test or a build script as easily as from the inspector.
