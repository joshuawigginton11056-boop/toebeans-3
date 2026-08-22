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

Writes `Tools\blender\previews\<name>.png`: an orthographic three-quarter view on a
one-metre floor grid, with a single one-metre cube beside the prop for height.
Orthographic on purpose — under perspective the reference geometry renders at a size that
depends on depth, which defeats the point of it.

A second argument turns the camera: `front`, `back`, `left`, `right`, or degrees. A prop
with a door on one side and a chute on the other cannot be judged from one fixed corner.

```powershell
& $env:BLENDER_PATH --background --factory-startup --python .\Tools\blender\preview.py -- Farm_Barn back
```

It used to draw a row of three one-metre cubes instead of the grid. That was fine for a
boulder and useless for a duck: at a framing tight enough to see a 0.4 m prop, a
one-metre cube fills half the picture and stands in front of the thing being judged. A
floor grid cannot occlude anything and reads at any prop size.

To compare a whole pack rather than one prop:

```powershell
& $env:BLENDER_PATH --background --factory-startup --python .\Tools\blender\contact_sheet.py -- farm_props
```

`preview.py` answers "is this prop right". `contact_sheet.py` answers the question that
only appears once a pack exists: **do these look like they came from the same place?** A
palette drifts one prop at a time, and the barn's timber being a different brown from the
cart's timber is obvious on a contact sheet and essentially undetectable in a row of
individual renders.

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

## The farm pack

`models\farm_*.py` build thirty-six models — buildings, fencing, yard clutter, vehicles,
implements and livestock — sharing one module, `farmyard.py`. Read `Assets\Farm\README.md`
for what they are and how to lay a track through them; this section is about what the
pipeline gained to make them.

**One palette, per-prop slots.** `farmyard.PALETTE` is twenty-five colours for the whole
pack, indexed by `SKIN_*` constants. A prop that declared all twenty-five would arrive in
Unity with twenty-five submeshes, most empty, and an empty submesh is a renderer material
entry that costs a draw call for nothing — so `finish`/`finish_parts` compacts each mesh
down to the colours it actually uses, while the source still reads `SKIN_RUST` everywhere.
That is also why the livestock colours live in the same list as the buildings: one list
means one set of Unity materials, so a cow's hoof is the same colour as a horse's.

**A build manifest instead of a copied table.** `BarrierAssetSetup.cs` carries a
hand-written copy of the palette its Blender script produces, under a comment saying the
two have to match — and nothing checks that they do. At five times the size that is a
guarantee of drift, so each farm script writes what it built into
`Assets\GeneratedModels\Manifests\<script>.json`: names, sizes, palettes, part hierarchies
and how each prop should collide. `FarmAssetSetup.cs` reads that and has no list of model
names in it at all. **Adding a prop needs no C# change.**

One manifest per script rather than one per pack, because each script runs in its own
headless Blender and two of them writing the same file is a race the runner would lose.

## Props with moving parts

A prop is one mesh. A tractor, a windpump and a cow are not. `toebeans_blender.Part` and
`build_hierarchy` are for those: each part is authored *where it belongs in the finished
prop* and given the joint it turns about, which becomes its object origin. A shoulder
written at `(0.21, 0.40, 0.68)` is the point the leg swings around in Unity with no offset
to remember.

This is a different answer from the kart's, and the difference is worth stating. A kart
exports a file per part because the runtime places each part itself from dimensions it
already owns. A cow's leg is not like that — it only means anything in the arrangement it
was authored in, and a file per leg would leave Unity reassembling a cow from eight FBXs
and a table of offsets. So an animal is one file with its parts parented.

**The hierarchy can only be one level deep in the FBX, and that was measured.** Bake Space
Transform is what puts the geometry in the Y-up frame Unity wants, and the Blender manual
notes it is unsupported for parented objects. At one level it is in fact fine —
`verify_axes.py` asserts it, and the child comes back unrotated and exactly where it was
authored. At two levels it is not: a windpump exported as `Tower > Head > Rotor` came back
with the rotor eight metres out and rotated 90°, because the axis conversion was baked
into the root and the grandchild but not into the object between them.

So `build_hierarchy` exports everything as a direct child of the root and puts the
intended rig in the manifest instead. `FarmAssetSetup.RebuildRig` re-parents in Unity with
`worldPositionStays`, and because every part's origin is already its joint in world space,
every joint lands exactly where Blender put it.

`drop_to_ground=True` settles an assembly onto Z=0 after chamfering. Reach for it when a
prop stands on something round — a wheel's lowest point is a chamfered facet corner, an
output of the bevel rather than a number anybody can write down. Leave it off for anything
on a flat foot, where the base check earns its keep: it is what caught the windpump's
battered legs poking five millimetres through the ground.

## Kart styles

A prop is one mesh. A kart is not, and cannot be: the wheels steer and spin, so they have
to be their own meshes with their own origins.

There are nine styles, one script each under `models\`, and they all share `kartworks.py`
the way the farm pack shares `farmyard.py`:

| Script | Style | Biome | The hook |
|---|---|---|---|
| `kart_buggy.py` | Buggy | universal | tube space frame, long-travel coil-overs |
| `cinder_hauler.py` | Cinder hauler | lava | horns and twin stacks, glowing fissures |
| `overgrowth.py` | Overgrowth | jungle | bamboo culms, one enormous leaf wing |
| `piste_basher.py` | Piste basher | snow | plow blade, sled runners, studded chevrons |
| `mine_cart.py` | Mine cart | cave | riveted tub, flanged rail wheels, carbide lamp |
| `field_marshal.py` | Field marshal | farm | huge rear fender arcs, exhaust stack, hay bale |
| `log_racer.py` | Log racer | woodland | hollowed log, antler hoop, cross-cut wheels |
| `bone_chariot.py` | Bone chariot | hell, alt | ribcage bodywork, skull nose cone |
| `pit_rat.py` | Pit rat | unlock | mismatched panels, bare engine, jerry can |

`kart-style-concepts.md` is where each of these was designed and why.

### kartworks.py

Everything a second style would otherwise copy from the first. **The dimensions live here
and nowhere else** — wheel radii, track, axles, hoop and lamp reference points, all
mirrored from `KartDimensions.Default` and asserted against `KartBlueprint.cs` and
`KartController.cs` on every build of every style. That is what makes the pack adjustable:
when the driving mechanics move, one edit in `kartworks.py` follows into all nine, and the
build fails loudly until it is made.

It also owns the parts whose shape the physics fixes rather than taste: `fender_arch`
(which takes daylight over the tyre, never a radius, so no style can cut inside the
`2R + T` clearance), `wheel_carcass`, `lamps`, `coilover`, and `tread_block`.

**Use `tread_block` for anything on a tyre.** `KartSuspension` holds the hub exactly
`radius` above the contact point, so a lug modelled proud of the nominal radius does not
ride high — it drives buried in the road, at every corner, silently. The obvious
construction gets this wrong: `tb.slab` measures its `thickness` perpendicular to its run,
and a chevron's run is mostly *along the axle*, so the thickness leaks straight out past
the radius. The first cut of the piste basher's tread peaked at 1.12 × radius that way.
`tread_block` places all eight corners at their own exact radii, so the outer face is *on*
the radius whatever the block's span, width or lean. `kw.emit(..., tread_radius=R)` then
measures the built mesh and fails the build if anything exceeds it — pass it for every
wheel.

### The worked example

`models\kart_buggy.py` is still the reference, and it exports four files:

| File | Origin | Notes |
|---|---|---|
| `KartBuggy_Body.fbx` | kart origin, on the ground | chassis, cockpit and bodywork |
| `KartBuggy_WheelFront.fbx` | wheel hub | axle along local X |
| `KartBuggy_WheelRear.fbx` | wheel hub | wider, per `KartDimensions` |
| `KartBuggy_SteeringWheel.fbx` | steering hub | rim in local XZ, column up local Y |

Three rules that are easy to get wrong:

**Dimensions come from `KartDimensions.Default`, not from taste.** The wheel arches are cut
for the wheels the physics actually places. `kartworks.py` mirrors those numbers and
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
the skin constants index against: append to it, never reorder it. Build a style's palette
through `kartworks.palette()`, which fixes the names; `kartworks.finish()` then drops the
slots a given mesh turns out not to use, so declaring one costs nothing.

**A style reinterprets the slots, it does not just recolour them.** Log racer is the
clearest case — its `KartRubber` is bark, not tyre, because the wheels are cross-cut rounds
of the same timber as the body. Cinder hauler and bone chariot spend `KartLens` on glowing
rock and embers rather than on lamp glass, following the same "slot 4 is the part that
glows" convention the cabins use. **A style that does that must leave `headlights` off** —
`KartLights` switches the lamps on by swapping the material on *every* `KartLens` face, so
a cinder hauler with headlights would flare its whole body on the L key.

### Palettes are generated, not copied

Each style writes its palette to `Assets\GeneratedModels\Manifests\kart_<Key>.json`, and
`KartStyleManifest.cs` reads it. This is the farm pack's answer applied to karts, for the
reason this README already gives about `BarrierAssetSetup.cs`: nine styles is fifty-odd
colour, metallic and roughness numbers, and a hand-copied palette drifts silently — the
kart just comes out the wrong colour, with nothing to say whether Blender or Unity is
wrong. Roughness is inverted to smoothness on the way out, so the JSON is already in
Unity's units.

Mesh names and lamp flags stay hand-written in `KartStyle.All`, because Unity's `MenuItem`
is an attribute and every style needs a hand-written menu entry regardless — and a wrong
mesh name fails loudly the moment you click it, where a wrong colour does not fail at all.

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
Tools > Toebeans > Kart Style > Cinder hauler (lava)
Tools > Toebeans > Kart Style > Overgrowth (jungle)
Tools > Toebeans > Kart Style > Piste basher (snow)
Tools > Toebeans > Kart Style > Mine cart (cave)
Tools > Toebeans > Kart Style > Field marshal (farm)
Tools > Toebeans > Kart Style > Log racer (woodland)
Tools > Toebeans > Kart Style > Bone chariot (hell)
Tools > Toebeans > Kart Style > Pit rat (unlock)
Tools > Toebeans > Kart Style > Primitives     (no imported assets)
```

`KartSetup` hangs each mesh on the transform it was authored about — the body on the kart
root, the rim on the `Steering` pivot, each wheel on its corner — and repaints the
submeshes with the project's own kart materials, matched by the **material name** baked
into the FBX rather than by slot order. Unity has to have imported the FBX first, so
after a Blender build, focus the Editor once before running the tool.

Adding a style is: write `models\<style>.py` on `kartworks`, build it (which writes its
manifest), add an entry to `KartStyle.All` and a `[MenuItem]` beside the others. The
palette needs no C# change at all.

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

### The half turn

Getting up and scale right still leaves which way round, and these settings land a prop
**yawed 180°**:

```
Blender (x, y, z)  ->  Unity (-x, z, -y)
```

The FBX stores Blender +Y at file -Z, and Unity negates X again on import because FBX is
right-handed and Unity is not. `verify_axes.py` section 3 asserts each of those signs.

It went unmeasured for a long time because sections 1 and 2 compare **dimensions**, and a
dimension has no sign — a half turn is invisible to them. Every prop the project had was
either symmetric or already compensating, so nothing surfaced it until a tractor arrived in
Unity pointing backwards.

Two places correct for it, and a new model script has to do one or the other or it faces
the wrong way:

- `farmyard.face_unity()` turns a whole prop once at the end, for scripts authored in
  Blender coordinates. This is the cheap route; `finish`/`finish_parts` call it for you.
- `kart_buggy.u()` converts each point on the way in, for a script authored in Unity
  coordinates so its numbers can be read against the C#.

Do **not** fix it inside `export_for_unity`. That would silently rotate every asset the
project has ever produced, including the two that already compensate.

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
