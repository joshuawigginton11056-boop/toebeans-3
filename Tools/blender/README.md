# Blender model pipeline

Scripted props for toebeans-3. Each prop is a Python script that builds a mesh from a
seed and exports an FBX into `Assets/GeneratedModels`, on the same principle as the
terrain, the track and the volcano: geometry you can rebuild and diff, not a binary you
can only replace.

## Requirements

- Blender 5.2 or newer. Developed against the Steam install at
  `C:\Program Files (x86)\Steam\steamapps\common\Blender\blender.exe`.
- `BLENDER_PATH` pointing at `blender.exe`. The build script falls back to the path above,
  but the Blender MCP server reads the same variable, so setting it keeps both routes
  aimed at one install.

Nothing here needs the Blender GUI, the MCP add-on, or Unity to be open.

## Getting started

```powershell
.\Tools\blender\build-models.ps1
```

That verifies the export orientation, then builds every script in `models\`. Unity picks
up the new FBX files on next focus.

```powershell
.\Tools\blender\build-models.ps1 -Model volcanic_rock   # one prop
.\Tools\blender\build-models.ps1 -SkipVerify            # while iterating
```

To look at a prop without opening Blender:

```powershell
& $env:BLENDER_PATH --background --factory-startup --python .\Tools\blender\preview.py -- VolcanicRock_A
```

Writes `Tools\blender\previews\<name>.png`: an orthographic three-quarter view with a row
of one-metre cubes beside the prop. Orthographic on purpose — under perspective the
reference cubes render at a size that depends on depth, which defeats the point of them.

A kart style ships as three FBX files rather than one, so it has its own previewer that
assembles them:

```powershell
& $env:BLENDER_PATH --background --factory-startup --python .\Tools\blender\preview_kart.py -- kart_buggy
```

Both previewers read the exported FBX rather than re-running the builders, so an export
that quietly loses its origin or its material split shows up in the picture.

## Writing a new prop

Copy `models\volcanic_rock.py`. The shape is always the same:

```python
sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
import toebeans_blender as tb

tb.fresh_scene()
# ... build a mesh ...
tb.build(obj, "MyProp", max_tris=400, max_size_m=4.0)
```

`tb.build` applies transforms, fixes normals, sets flat shading, moves the origin, unwraps
UVs if there are none, validates, and exports. Any script named `_something.py` is skipped
by the runner, which is how you park a work in progress.

## The origin convention

**A prop's origin is the centre of its footprint, on its lowest point** — the middle of the
foot, standing on the ground. Same rule as the Volcano generator. It is what makes "drop it
on the terrain" mean something instead of leaving you to eyeball a Y offset per instance,
and `validate` fails a mesh whose base is not on Z=0.

The exception is anything authored around a mount point instead of a footprint, which is
what `tb.build(..., origin="keep")` is for. A kart body is authored around the kart's own
origin — on the ground between the wheels, below the floor pan — and a wheel around its
hub, a radius up. Re-centring either on its lowest vertex would slide it off the anchor the
runtime places it at, so `origin="keep"` leaves the pivot on the world origin and the base
check is skipped with it.

## Cabins

`models\cabin.py` builds three houses from one script, the way `kart_buggy.py` builds four
kart parts from one:

| File | What it is |
|---|---|
| `Cabin_A.fbx` | the full house - porch, two dormers, chimney, shuttered windows |
| `Cabin_B.fbx` | a smaller one-room cabin with a woodshed lean-to, no dormers |
| `Cabin_Burnt.fbx` | `Cabin_A` after the fire: roof mostly gone, walls charred, embers left |

They exist because the medieval-town buildings already in the scene are mitred to razor
edges, and everything this project makes itself - terrain, volcanic rock, karts - is a
faceted solid with **chamfered** corners. Standing next to each other the difference reads
as two art styles in one map.

So the rule for anything building-shaped here is: model it as solid boxes and prisms,
never as planes or cut-outs, and let `build_cabin` put one bevel pass over the finished
mesh. One pass and not per-part, because a post meeting a rail has to chamfer to the same
width as the rail - that is what makes an assembly of boxes read as one carved object.
`MIN_PART` (0.07 m) is the floor on any dimension; go under it and the chamfer eats the
part, and `validate` fails the build on the zero-area faces that result.

Three things follow from that rule and are easy to get wrong:

**Openings are decomposed, not cut.** `panels` fills a wall with boxes around its holes and
`piers` returns the stretches left between them. Booleans here produce n-gons and coplanar
slivers, and the bevel turns both into shading artefacts you only see once it is in Unity.
`add_frame` takes the same openings and puts its braces in the piers, so a brace can never
land across a window however the windows move.

**Shingle courses are tilted, not stacked.** Courses laid parallel to the deck all sit at
one height and merge into a single flat slab no matter how far they overlap. Each course
here rides up on the tail of the one below and lies almost flat at its head, which steps
every course line by about one shingle without the roof thickening as it climbs.

**Every opening has something behind it.** An unbacked hole shows the skybox through the
far wall the moment the camera drops below the eaves. That is the `CabinInterior` slot.

Dimensions are architectural rather than arbitrary - a 2.55 m wall, a 2.03 m door, a sill
at 1.30 m - and the elevations are derived from the spec, not written out, so `Cabin_B` is
a different building at a different width rather than `Cabin_A` with its windows hanging
off the corners. A kart is 1.24 m across its front track, so the doors are deliberately too
small to drive through and the porch posts stand inside the eaves, where a kart clipping
the corner glances off the stone plinth instead of catching a post.

The seven material slots are the same on all three, so a variant's materials match by slot
as well as by name. Slot 4 is "the part that glows": `CabinGlass` on a standing cabin,
`BurntEmber` on the ruin - the slot the scene's own lava material belongs on.

`Cabin_Burnt` is the same spec as `Cabin_A` carrying a `Damage`, not a second model that
merely resembles it, so the two can stand side by side in a scene. `Damage` says which
shingle courses survived and how much of each gable is left; everything else - exposed
rafters, the burnt bay, the door off one hinge, the debris around the foot - follows from
it. Its bounding box is wider than the house, because the debris is part of the prop.

## Kart styles

A prop is one mesh. A kart is not, and cannot be: the wheels steer and spin, so they have
to be their own meshes with their own origins. `models\kart_buggy.py` is the worked
example, and it exports three files:

| File | Origin | Notes |
|---|---|---|
| `KartBuggy_Body.fbx` | kart origin, on the ground | chassis, cockpit and bodywork |
| `KartBuggy_WheelFront.fbx` | wheel hub | axle along local X |
| `KartBuggy_WheelRear.fbx` | wheel hub | wider, per `KartDimensions` |
| `KartBuggy_SteeringWheel.fbx` | steering hub | rim in local XZ, column up local Y |

Three rules that are easy to get wrong:

**Dimensions come from `KartDimensions.Default`, not from taste.** The wheel arches are cut
for the wheels the physics actually places. The model script mirrors those numbers and
asserts them against `Assets\Kart\Scripts\KartBlueprint.cs` at build time, so a change on
the C# side fails the build instead of producing a tyre through a fender. The assertion
covers the steering constants too, since the steering wheel parents onto a C# pivot.

**Anything that moves is its own mesh.** The road wheels spin and the steering wheel turns,
so all three are separate files authored around their own pivot. The steering wheel in
particular is authored in the `Steering` pivot's local space — rim in the local XZ plane,
column up local Y — because `KartBlueprint` spins that pivot about its own Y and hangs the
driver's hands off it. Author it in any other frame and it turns like a tabletop. The
column, rack and tie rods stay in the body, because those parts are static on the C# side.

**The driver stays out of the mesh.** `KartDriverRig` re-aims the arms at the wheel every
frame, and geometry baked into a static mesh cannot do that.

**Tread peaks at the nominal wheel radius.** `KartSuspension` holds the hub exactly
`radius` above the contact point, so lugs modelled any prouder than that sink into the
road. The carcass is drawn under the radius and the tread blocks come back out to meet it.

Kart meshes carry six material slots named for `KartSetup`'s skins — `KartFrame`,
`KartBody`, `KartSeat`, `KartRim`, `KartRubber`, `KartLens` — so one mesh keeps the palette
split instead of arriving in Unity as a single flat colour. The slot order is the contract
the skin constants index against: append to it, never reorder it.

**Lamp glass gets its own slot.** `KartLights` switches the headlights on by swapping the
material on the `KartLens` submesh for an emissive one, so the glass has to be modelled as
its own boxes in their own slot — glass sharing a slot with the housing lights the whole
pod up with it. The lamp positions are in `KartBlueprint` and asserted like the wheel
dimensions are, because Unity hangs a real `Light` on the front face of each nose lamp:
move a lamp in Blender alone and the beam comes out of the bodywork.

## Getting a style into Unity

`Assets/Kart/Scripts/KartStyle.cs` is the list of styles. An entry names the four meshes;
a style with no meshes builds the old primitive kart instead, which needs no imported
assets and is the fallback when a model is missing.

```
Tools > Toebeans > Set Up Drivable Kart        (Ctrl+Shift+K — builds KartStyle.Default)
Tools > Toebeans > Kart Style > Buggy
Tools > Toebeans > Kart Style > Primitives     (no imported assets)
```

`KartSetup` hangs each mesh on the transform it was authored about — the body on the kart
root, the rim on the `Steering` pivot, each wheel on its corner — and repaints the
submeshes with the project's own kart materials, matched by the **material name** baked
into the FBX rather than by slot order. Unity has to have imported the FBX first, so
after a Blender build, focus the Editor once before running the tool.

Adding a style is: write `models\<style>.py`, build it, add an entry to `KartStyle.All`.
No other C# changes.

A style whose model has lamps sets `headlights = true` on its entry, and `KartSetup` hangs
the Lights and the switch on it — **L** toggles them while driving. Leave it off for a
model with no lamp housings, or the kart drives around with beams coming out of nothing.

`kart-style-concepts.md` is the shortlist of styles worth building, one per biome, with
the reasoning behind each and the order to build them in.

## Why these export settings

Blender is Z-up, Unity is Y-up, and the FBX exporter offers several combinations that all
look plausible. The ones in `toebeans_blender.export_for_unity` were picked by measuring an
asset the scene already uses correctly — BOKI's `cliff_1.fbx` — and matching its signature:
Y-up geometry, which round-trips back into Blender at `rot=[90, 0, 0]`.

Turn `bake_space_transform` off and the geometry stays Z-up. That looks fine in Blender and
arrives in Unity rotated -90 on X, which is the single most common way a Blender-to-Unity
prop goes wrong.

`verify_axes.py` re-asserts all of that. It runs before every build, so if a Blender upgrade
changes an exporter default you find out from a failing build rather than from a prop lying
on its side in the map.

## Validation is fatal, not advisory

`validate` raises rather than warns, because a warning in a build log is a warning nobody
reads until the prop is already scattered across the map a thousand times. It rejects:

| Check | Why it matters here |
|---|---|
| Unapplied rotation or scale | Right size in Blender, wrong size in Unity |
| Missing UVs | Lava materials read UV density; a bad unwrap reads as flat dead lava |
| Zero-area faces | Collider artefacts a kart catches on |
| Loose vertices or edges | Nothing visible, and they break collider generation |
| Triangle budget | Prop-mode scatter puts hundreds of these on screen |
| Base not on Z=0 | Breaks the origin convention above |

All six are covered by negative tests — each was confirmed to fail validation when
deliberately broken, so the checks are known to bite rather than merely to exist.

## What still needs a human eye

The axis convention is verified against a reference asset, but only inside Blender. The
first time you drag a new prop into a scene, confirm the transform reads `0, 0, 0` and the
prop stands upright. If it does, the convention holds for every prop this pipeline makes.

This is a kart racer and the mesh is the collider, so anything a kart can touch also has to
clear the bar the rest of the project's geometry does: no steps, no bumps, nothing that
catches a wheel.
