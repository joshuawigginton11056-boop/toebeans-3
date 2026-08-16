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
