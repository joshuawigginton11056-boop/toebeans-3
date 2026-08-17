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

Kart meshes carry five material slots named for `KartSetup`'s skins — `KartFrame`,
`KartBody`, `KartSeat`, `KartRim`, `KartRubber` — so one mesh keeps the palette split
instead of arriving in Unity as a single flat colour. The slot order is the contract the
skin constants index against: append to it, never reorder it.

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
