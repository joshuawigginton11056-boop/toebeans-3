# Volcano Generator

A procedural low-poly volcano for Unity: a faceted cone with a crater, gullies raked down the
flanks, notches for lava to pour out of, and a passage cut through the base for a track to run
through. All of it is one height field, rebuilt from a seed, so it is editable geometry rather than
a baked model.

## Requirements

- Unity 2021.3 or newer (developed on Unity 6000.5).
- Any render pipeline. The generator builds a plain mesh; the materials are written on first use
  against whatever pipeline is active, which is why nothing is shipped as an asset.

## Getting started

`GameObject > 3D Object > Volcano (Low Poly)`

That makes the object with a MeshFilter, MeshRenderer, MeshCollider and the generator, and creates
the four materials it needs. Press **Snap To Ground**, then **Build Everything** on the inspector to
hang the lava, the rivers, the smoke and the passage lights off it.

**The object's origin is the middle of the foot of the cone, standing on the ground.** Not the
summit, not the centre of the bounding box. That is what makes Snap To Ground mean something and
what puts the passage floor at the same level as the ground around it.

## Submeshes

`0 rock`, `1 ash`, `2 ember`, `3 molten`. Four materials on the Mesh Renderer in that order. Only
slot 3 wants an emissive material — it is the fissures, the spillway notches and the seam glowing
along the wall of the passage.

## The crater and the lava

Nothing here draws lava. `Lava Depth Below Rim` says where the lava *stands*, and everything else is
derived from it:

- **Add Crater Lava** puts a Lava Pond at that level, sized to the pool the crater actually holds at
  that height (solved off the crater profile, not guessed), with its vent up so there is visibly
  something feeding the mountain.
- **Spillways** are notches cut through the rim to `Notch Drop` below the crest, each carrying a
  channel down the flank. **`Notch Drop` has to be greater than `Lava Depth Below Rim`** or the
  notch is above the lava and nothing ever comes out of it. The inspector says so if it is not.
- **Add Spillway Rivers** drops a Lava Flow into each channel, routed from the same maths that cut
  it rather than released at the top and left to find its own way down.

The channel is real geometry — a downhill solve would probably follow it — but "probably" is not
something to build a set piece on.

## Gullies scour, channels do not

Gullies and surface roughness are suppressed inside a spillway channel. That is how a lava channel
really looks, and it is also load-bearing: roughness at the default amplitude is easily steep enough
to put a lip across the channel near the foot, where the cone itself has flattened off, and a lava
flow ponds up and stops at a lip. With the channel scoured, the only things shaping its floor are
the cone and the channel's own taper, and it descends all the way.

## The passage

Three modes:

| Mode | Gives you |
| --- | --- |
| None | Solid mountain |
| Portals Only | The two mouths, nothing else — for running the Cave Tunnel generator through |
| Bore | The mouths, plus rock walls, a flat drivable floor and an apron at each end |

The hole is the intersection of one half-space per face of the arch, and the mountain's surface is
clipped against those planes, so the cut edge follows the arch outline rather than stepping around
whole triangles.

Two things about it are deliberate and worth not undoing:

- **The hole is cut slightly smaller than the tunnel that fills it** (`Mouth Overlap`). The two
  surfaces are solved separately and meet at the mouth; a small overlap is what guarantees there is
  no hairline of daylight around the arch. Raise it if you ever see one.
- **The floor plane of the cut is not inset**, unlike every other face. Insetting it lifts it, and
  any hillside crossing floor level inside the passage then survives as a slab lying on the road —
  measured at exactly the inset, which at kart speed is a ramp.

The floor is never roughened and never varies: this mesh is the collider and it is a road. Walls are
only ever pushed outwards, so the passage is at least as wide as it says it is whatever the
roughness is set to.

**Snap the passage clear of the ground under it.** Unity Terrain is a heightfield that knows nothing
about the tunnel and carries on straight underneath, so wherever it rises above the floor it comes
up through the road. Two ways out: raise `Bore Floor Height` above the highest ground in the
corridor and let the aprons ramp down to meet it outside, or punch Unity terrain holes along the
bore. This project uses the first — it is self-contained and does not write to the shared
TerrainData.

`Add Passage Lights` puts warm point lights down it. The seam in the wall is emissive, and emission
does not light anything, so without them the passage is a black hole in the middle of the map.

## Smoke and mist

Both use **mesh particles, not billboards**. A soft cloud texture next to a mountain made of flat
triangles reads as a photograph pasted onto a model; a lumpy faceted solid is the same silhouette
language as everything else, and it tumbles properly because it is genuinely three dimensional.

`LavaMist` emits from the **triangles of a lava mesh, restricted to its molten submesh**, so the fog
sits on the glowing parts and follows a river wherever it was routed. Move the lava, retune it,
reroute it — the mist comes with it and there is nothing to keep in sync. Lava Pond and Lava Flow
put molten in slot 2; the Volcano puts it in slot 3.

Particle systems do not run in edit mode. `ParticleSystem.Simulate` steps them by hand if you want
to see them without entering play mode.

## Colour on a night map

Albedo wants to be lighter than volcanic rock really is, because under a night rig it gets
multiplied by a dim light and physically dark basalt renders black, taking the facets with it. Only
a little lighter, though — twice the ground's own values reads as a snowy mountain dropped into a
lava field.

Emission wants red just over 1 and the other channels well under. Two channels over 1 both clip to
full, and the glow comes out yellow however orange the ramp under it is. `VLC_River_Lava` exists for
exactly that reason: the Lava Flow package's shipped molten colour has green at 1.5, which barely
shows on gentle terrain where most of the surface has crusted over, and turns a cascade gold.

## Files

    Scripts/   VolcanoNoise, VolcanoSettings, VolcanoMeshBuffer, VolcanoShape,
               VolcanoMeshBuilder, VolcanoGenerator, LowPolyPuff, VolcanoSmoke, LavaMist
    Editor/    VolcanoGeneratorEditor  (inspector, menu item, materials, dressing)

`VolcanoShape` and `VolcanoMeshBuilder` are pure maths — no scene objects, no asset loading, no
global state, and none of Unity's native calls — so they compile and run outside the Editor and can
be asserted against.

`Save Mesh Asset` bakes the current mountain to a .asset if you would rather ship static geometry.
