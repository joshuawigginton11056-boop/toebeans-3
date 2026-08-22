"""
The parts of a kart style that are not the style.

`models\\kart_buggy.py` was the only kart in the project, so it carried the kart's shared
facts itself: the dimensions mirrored from `KartDimensions.Default`, the axis conversion,
the material slot contract, the wheel-arch maths and the cross-language drift check. With
one kart that is the right place for them. With nine it is nine copies of a table that has
to agree with the C#, which is the failure the farm pack's manifest exists to prevent and
the same one `BarrierAssetSetup.cs` is called out for in the README.

So the numbers live here now, once, and every style imports them. When the driving
mechanics change - and they will - `KartDimensions` moves, `check_against_blueprint` fails
every style's build on the next run, and one edit in this file puts all of them right.
That is the whole reason this module exists; it is not a convenience layer.

What belongs here is anything a *second* style would otherwise copy:

    dimensions and the blueprint check   every style is cut for the same wheels
    u / usize / mirrored                 the export convention, which is not negotiable
    the six material slots               KartSetup matches on these names
    arch / coilover / lamps              parts whose shape is fixed by the physics
    wheel_carcass                        the part of a wheel that is radius and width

What does not belong here is anything that makes a style *look* like itself. A tread
pattern, a body panel, a roll hoop's shape: those go in the style script, and a helper
that grew a `style=` flag to serve two of them has escaped into the wrong file.
"""

import json
import math
import os
import re
import sys

import bmesh
from mathutils import Vector

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import toebeans_blender as tb  # noqa: E402


# ---------------------------------------------------------------------------------------
# The hard numbers, mirrored from KartDimensions.Default and asserted against it by
# `check_against_blueprint`.
#
# Mirrored rather than scraped so this file can be read on its own and so a change on the
# C# side is a deliberate act here rather than a silent reshaping of nine models. The
# assertion is what makes the mirror safe; without it this is just a stale copy.
# ---------------------------------------------------------------------------------------

FRONT_AXLE_Z = 0.80
REAR_AXLE_Z = -0.85
FRONT_TRACK = 1.24
REAR_TRACK = 1.34
FRONT_WHEEL_RADIUS = 0.26
REAR_WHEEL_RADIUS = 0.30
FRONT_WHEEL_WIDTH = 0.20
REAR_WHEEL_WIDTH = 0.28

# Mirrored from KartController.suspensionDistance. The arches are cut to clear a wheel
# across its whole travel rather than where it happens to sit parked.
SUSPENSION_TRAVEL = 0.28

# Daylight between tyre and arch at the two ends of the travel.
ARCH_GAP = 0.04

# Matched to KartBlueprint's own reference points.
ROLL_HOOP_TOP_Y = 1.40
ROLL_HOOP_Z = -0.72
ROLL_HOOP_HALF_WIDTH = 0.40
STEERING_RACK = (0.0, 0.30, 0.30)
STEERING_HUB = (0.0, 0.76, -0.02)
STEERING_WHEEL_RADIUS = 0.16
STEERING_RIM_SEGMENTS = 10

# Where the driver ends up, from KartBlueprint. A style has to leave room for these: the
# driver is primitives placed by the C# and cannot dodge a seat back modelled through them.
SEAT_BASE_TOP = (0.0, 0.37, -0.42)
SHOULDER = (0.21, 0.97, -0.50)
HELMET_CENTRE = (0.0, 1.18, -0.55)
HELMET_RADIUS = 0.125

# Lamps. KartLights hangs a real Unity Light on the front face of the glass a style builds
# here, so a style with `headlights` on has to put its glass on these points.
HEADLAMP_Y = 0.47
HEADLAMP_Z = 1.30
HEADLAMP_HALF_SPACING = 0.15
HEADLAMP_SIZE = (0.14, 0.10, 0.06)

ROOF_POD_Y = 1.33
ROOF_POD_Z = 0.22
ROOF_POD_INNER_X = 0.16
ROOF_POD_OUTER_X = 0.34
ROOF_POD_SIZE = (0.12, 0.10, 0.09)

LENS_THICKNESS = 0.018
LENS_INSET = 0.022


# ---------------------------------------------------------------------------------------
# Axis convention
# ---------------------------------------------------------------------------------------

def u(x, y, z):
    """A point in Unity kart space (X right, Y up, Z forward) -> Blender space.

    Authoring through this rather than converting at the end is what lets every number in
    a style script be compared directly against the C#. The export convention, asserted by
    verify_axes.py section 3:

        Blender (bx, by, bz)  ->  Unity (-bx, bz, -by)

    Requiring the round trip to be the identity pins this function down completely: there is
    one answer, and it is the one below.

    **This used to return `(x, -z, y)`,** which got the Y and Z halves right and missed the
    X flip, so every kart came out of the exporter mirrored left to right. That was invisible
    for a long time because almost all of a kart is built through `mirrored()` and a mirror
    maps a symmetric object onto itself - and because verify_axes only compared *dimensions*
    until 2026-08-21, and a dimension has no sign. What it was actually doing: putting the
    exhaust and the gear lever on the driver's left when the source said right.

    One consequence worth knowing: Blender's +X is Unity's -X, so a kart appears mirrored if
    you ever open a build in the Blender GUI. Nothing looks at it there - every build is
    headless and preview_kart.py renders the exported FBX - but do not "correct" it by eye
    in the viewport.
    """
    return Vector((-x, -z, y))


def usize(x, y, z):
    """A full size in Unity kart space -> Blender space.

    The same axis swap as `u` with neither of its negations, because an extent has no
    direction. Separate from `u` so the missing signs are deliberate and visible.
    """
    return Vector((x, z, y))


def mirrored(*points):
    """Yield (side, converted points) for the left and right of the kart."""
    for side in (-1, 1):
        yield side, [u(p[0] * side, p[1], p[2]) for p in points]


# ---------------------------------------------------------------------------------------
# Material slots
#
# The order is the contract with tb.assign_materials, and the names are the keys
# KartSetup.SkinsByMaterialName matches on. Append to this list, never reorder it.
# ---------------------------------------------------------------------------------------

FRAME, BODY, SEAT, RIM, RUBBER, LENS = range(6)

SLOT_NAMES = ("KartFrame", "KartBody", "KartSeat", "KartRim", "KartRubber", "KartLens")

# What a lamp lens looks like switched off. Shared because it is not a style decision:
# Unity repaints this slot with KartLens and swaps it for the emissive KartLensLit, so the
# colour here only has to survive the Blender preview.
COLD_GLASS = ((0.62, 0.64, 0.62), 0.20, 0.05)


def palette(frame, body, seat, rim, rubber, lens=COLD_GLASS):
    """Build a style's six-slot palette from six (rgb, metallic, roughness) triples.

    A style names its own colours - that is most of what a style is - but it does not get
    to name its own *slots*, because Unity matches them by name to repaint them. Going
    through here means a style cannot accidentally ship a slot called "Bamboo" that
    KartSetup then warns about and paints body-orange.
    """
    slots = (frame, body, seat, rim, rubber, lens)
    out = []
    for name, spec in zip(SLOT_NAMES, slots):
        rgb, metallic, roughness = spec
        out.append((name, tuple(rgb), metallic, roughness))
    return out


def _used_slots(bms):
    used = set()
    for bm in bms:
        for face in bm.faces:
            used.add(face.material_index)
    return sorted(used)


def _compact(bms, full_palette):
    """Renumber faces onto a palette holding only the colours these meshes actually use.

    Same reasoning as farmyard._compact: an empty submesh is a renderer material entry
    that costs a draw call for nothing, and a kart puts a body plus four wheels on screen
    per player. It matters more here than on a scattered prop, not less.

    A style still writes SEAT or LENS wherever it means them; the compaction is what makes
    declaring a slot you turn out not to use free.
    """
    used = _used_slots(bms)
    if not used:
        raise ValueError("no faces to compact")
    if max(used) >= len(full_palette):
        raise ValueError(
            f"face tagged with slot {max(used)}, past the end of a {len(full_palette)}-slot palette")

    remap = {old: new for new, old in enumerate(used)}
    for bm in bms:
        for face in bm.faces:
            face.material_index = remap[face.material_index]
    return [full_palette[i] for i in used]


def finish(bm, name, style_palette, chamfer_m=None):
    """Chamfer, compact the palette, and hand back an object ready for `tb.build`.

    The chamfer is optional here and mandatory in farmyard, because the two packs are
    different shapes of model. A farm prop is boxes and needs the one bevel pass to read
    as a carved solid. A tube-frame kart is already all cylinders, and bevelling a 26 mm
    tube mostly eats it - hence `MIN_PART`-style care in the box-heavy styles and no
    chamfer at all in the frame-heavy ones.
    """
    if chamfer_m:
        tb.chamfer(bm, chamfer_m)
    compacted = _compact([bm], style_palette)
    obj = tb.mesh_from_bmesh(bm, name)
    tb.assign_materials(obj, compacted)
    return obj


# ---------------------------------------------------------------------------------------
# Parts whose shape the physics fixes
# ---------------------------------------------------------------------------------------

def arch(bm, centre, radius, half_width, skin, thickness=0.03, segments=5,
         start_deg=32.0, end_deg=148.0):
    """A closed curved shell over the top of a wheel.

    Built as a solid rather than a strip of single-sided quads: Unity culls back faces, so
    a one-sided arch vanishes from exactly the low camera angle this game uses.
    """
    rings = []
    for i in range(segments + 1):
        theta = math.radians(start_deg + (end_deg - start_deg) * i / segments)
        # Measured off +Z (forward) towards +Y (up), so the arch sits over the tyre.
        direction = Vector((0.0, math.sin(theta), math.cos(theta)))
        ring = []
        for offset in (-half_width, half_width):
            for r in (radius, radius + thickness):
                point = Vector(centre) + direction * r
                ring.append(bm.verts.new(u(point.x + offset, point.y, point.z)))
        rings.append(ring)

    loops = [[r[0], r[1], r[3], r[2]] for r in rings]
    made = []
    for a, b in zip(loops, loops[1:]):
        for j in range(4):
            k = (j + 1) % 4
            made.append(bm.faces.new((a[j], a[k], b[k], b[j])))
    made.append(bm.faces.new(loops[0]))
    made.append(bm.faces.new(loops[-1][::-1]))
    for face in made:
        face.material_index = skin


def fender_arch(bm, corner_x_sign, front, skin, gap=ARCH_GAP, thickness=0.03,
                half_width=None, **kw):
    """An arch over one corner, cut to clear that wheel across its whole travel.

    The wheel is not where a naive arch thinks it is. KartController hangs each wheel
    visual at `WheelCentre + up * compression`, so in the body's frame the wheel climbs the
    entire SUSPENSION_TRAVEL between full droop and full bump. A tyre of radius R sweeping
    T needs its arch's inner surface at 2R + T; there is no way to dodge that cost, and
    centring the arch on the middle of the travel is what decides where the cost lands.
    Split evenly, it reads as a long-travel offroad arch rather than a fender left hanging
    over a wheel that ducked.

    `gap` is the daylight and `half_width` how far the arch reaches across the tyre. Both
    are style decisions, and the field marshal leans on that hard: its whole tractor read
    is one oversized rear arch and one deliberately mean front shroud. What a style cannot
    do is set the radius directly, because the `2R + T` clearance below is not negotiable.
    """
    lift = SUSPENSION_TRAVEL * 0.5
    radius = FRONT_WHEEL_RADIUS if front else REAR_WHEEL_RADIUS
    width = FRONT_WHEEL_WIDTH if front else REAR_WHEEL_WIDTH
    track = FRONT_TRACK if front else REAR_TRACK
    axle_z = FRONT_AXLE_Z if front else REAR_AXLE_Z

    if half_width is None:
        half_width = width * 0.5 + 0.03

    arch(bm, (track * 0.5 * corner_x_sign, radius + lift, axle_z),
         radius + lift + gap, half_width, skin, thickness=thickness, **kw)


def coilover(bm, lower, upper, rod_skin, spring_skin, rod=0.026, spring=0.055):
    """A damper rod with a fatter spring over its middle."""
    tb.tube(bm, lower, upper, rod, rod_skin)
    tb.tube(bm, lower.lerp(upper, 0.18), lower.lerp(upper, 0.82), spring, spring_skin,
            segments=8)


def lamp_mounts(nose=True, roof=True):
    """Every lamp position as (centre, housing size), nose pair first.

    Unity walks the same list on its side - KartBlueprint.Lamps() is this function - so a
    style that builds its glass anywhere else gets beams coming out of its bodywork. The
    two flags exist because not every style has a roof bar to hang pods on; a style that
    turns `roof` off must also leave `roofPods` off in its KartStyle entry.
    """
    mounts = []
    if nose:
        mounts += [((x, HEADLAMP_Y, HEADLAMP_Z), HEADLAMP_SIZE)
                   for x in (-HEADLAMP_HALF_SPACING, HEADLAMP_HALF_SPACING)]
    if roof:
        mounts += [((x, ROOF_POD_Y, ROOF_POD_Z), ROOF_POD_SIZE)
                   for x in (-ROOF_POD_OUTER_X, -ROOF_POD_INNER_X,
                             ROOF_POD_INNER_X, ROOF_POD_OUTER_X)]
    return mounts


def lamps(bm, housing_skin, nose=True, roof=True):
    """Lamp housings and their glass.

    The glass is a separate box in its own slot rather than the front face of the housing.
    It has to be: Unity switches the headlights on by swapping the material on exactly
    these faces, and a lens sharing a slot with the pod lights the whole pod up with it.
    """
    for (x, y, z), size in lamp_mounts(nose=nose, roof=roof):
        tb.cuboid(bm, u(x, y, z), usize(*size), housing_skin)

        # Proud of the housing's front face by half its own thickness, which is where
        # KartLamp.LensCentre puts it on the C# side and where the Light is hung.
        lens_z = z + (size[2] + LENS_THICKNESS) * 0.5
        tb.cuboid(bm, u(x, y, lens_z),
                  usize(size[0] - LENS_INSET * 2, size[1] - LENS_INSET * 2, LENS_THICKNESS),
                  LENS)


def wheel_carcass(bm, radius, width, rubber_skin, rim_skin, carcass=0.86, rim=0.56,
                  hub=0.22, segments=12, spokes=3):
    """The part of a wheel that is just radius and width, coaxial along Unity X.

    Centred on the hub rather than sitting on the ground because this mesh spins: an origin
    anywhere else turns the wheel into a cam. Matches the axis convention in
    KartBlueprint.BuildWheel, whose comment is the other half of this contract.

    The carcass is drawn *under* the nominal radius so a style's own tread is what actually
    meets the ground - lugs flush with the casing just read as a smooth tyre. Tread then
    peaks at exactly `radius`, which is the radius KartSuspension holds the hub above the
    contact point: prouder than that and the tread sinks into the road.
    """
    tb.tube(bm, u(-width * 0.5, 0, 0), u(width * 0.5, 0, 0), radius * carcass,
            rubber_skin, segments=segments)
    tb.tube(bm, u(-width * 0.54, 0, 0), u(width * 0.54, 0, 0), radius * rim,
            rim_skin, segments=segments)
    tb.tube(bm, u(-width * 0.60, 0, 0), u(width * 0.60, 0, 0), radius * hub,
            rim_skin, segments=6)

    # Spokes across the outer face, so the wheel is not a blank disc side-on.
    for i in range(spokes):
        phi = 2.0 * math.pi * i / spokes
        spoke = Vector((0.0, math.sin(phi), math.cos(phi))) * (radius * 0.50)
        tb.slab(bm, u(width * 0.55, 0.0, 0.0), u(width * 0.55, spoke.y, spoke.z),
                width * 0.10, radius * 0.16, rim_skin)


def tread_block(bm, radius, theta_a, theta_b, x_a, x_b, height, width, skin):
    """One tread block, guaranteed to peak at exactly `radius` and never above it.

    This exists because getting it wrong is silent and systematic. The obvious way to draw
    a lug is `tb.slab` from an inner point to a point on the radius - but slab measures its
    `thickness` perpendicular to the run, and a chevron's run is mostly *along the axle*,
    not radial. The thickness then leaks straight out past the radius: the first cut of the
    piste basher's chevrons peaked at 1.12 x radius, which is a 30 mm sink into the road on
    every wheel, and nothing about the render says so.

    The block is built as an explicit eight-corner solid rather than through `tb.beam`,
    and that is the second iteration of this function. Routing it through `beam` with `up`
    set to the *midpoint* radial was close but not exact: at a wide angular span the two
    ends' own radials diverge from the midpoint's, and the cross-section - measured along
    one fixed axis for the whole run - pushes the end corners out past the radius. The farm
    tyre's 28-degree bars overshot by 5 mm that way, which `assert_tread` caught.

    Placing all eight corners at their own exact radii removes the approximation instead of
    tuning a fudge factor onto it: the four outer corners are *on* `radius` by definition,
    whatever the span, the width or the lean.
    """
    if height >= radius:
        raise ValueError(f"tread block {height} m tall on a {radius} m wheel")

    half_angle = width * 0.5 / radius
    corner = {}
    for end, (theta, x) in enumerate(((theta_a, x_a), (theta_b, x_b))):
        for ri, rad in enumerate((radius - height, radius)):
            for ti, sweep in enumerate((-half_angle, half_angle)):
                angle = theta + sweep
                corner[(end, ri, ti)] = bm.verts.new(
                    u(x, math.sin(angle) * rad, math.cos(angle) * rad))

    for loop in (
            ((0, 0, 0), (0, 0, 1), (0, 1, 1), (0, 1, 0)),   # the theta_a end
            ((1, 1, 0), (1, 1, 1), (1, 0, 1), (1, 0, 0)),   # the theta_b end
            ((0, 1, 0), (0, 1, 1), (1, 1, 1), (1, 1, 0)),   # outer face, on the radius
            ((0, 0, 1), (0, 0, 0), (1, 0, 0), (1, 0, 1)),   # inner face
            ((0, 0, 0), (0, 1, 0), (1, 1, 0), (1, 0, 0)),   # the two flanks
            ((0, 1, 1), (0, 0, 1), (1, 0, 1), (1, 1, 1)),
    ):
        face = bm.faces.new([corner[key] for key in loop])
        face.material_index = skin


def around(count, radius, phase=0.0):
    """Yield (index, theta, unit direction) around the wheel's axis.

    Every tread pattern in the pack is a loop of this shape, and writing the
    `Vector((0, sin, cos))` by hand each time is how one style ends up with its lugs in a
    different plane from the rest.
    """
    for i in range(count):
        theta = 2.0 * math.pi * i / count + phase
        yield i, theta, Vector((0.0, math.sin(theta), math.cos(theta))) * radius


# ---------------------------------------------------------------------------------------
# Cross-language check
# ---------------------------------------------------------------------------------------

def _read(*parts):
    path = os.path.join(tb.REPO_ROOT, *parts)
    if not os.path.exists(path):
        return None, path
    with open(path, "r", encoding="utf-8") as handle:
        return handle.read(), path


def check_against_blueprint(who="kartworks.py"):
    """Fail the build if KartDimensions.Default has moved out from under this file.

    A plain text scrape rather than anything clever: it only has to notice that a number
    changed, and it has to keep working without Unity or a C# toolchain present.

    This used to live in kart_buggy.py and guard one model. It guards every style now, and
    that is the point - a wheel radius change should fail nine builds at once rather than
    leave eight models cut for a wheel the physics no longer places.
    """
    text, path = _read("Assets", "Kart", "Scripts", "KartBlueprint.cs")
    if text is None:
        print(f"  skip  KartBlueprint.cs not found at {path}")
        return

    scalars = {
        "frontAxleZ": FRONT_AXLE_Z, "rearAxleZ": REAR_AXLE_Z,
        "frontTrack": FRONT_TRACK, "rearTrack": REAR_TRACK,
        "frontWheelRadius": FRONT_WHEEL_RADIUS, "rearWheelRadius": REAR_WHEEL_RADIUS,
        "frontWheelWidth": FRONT_WHEEL_WIDTH, "rearWheelWidth": REAR_WHEEL_WIDTH,
        "RollHoopTopY": ROLL_HOOP_TOP_Y, "RollHoopZ": ROLL_HOOP_Z,
        "RollHoopHalfWidth": ROLL_HOOP_HALF_WIDTH,
        "SteeringWheelRadius": STEERING_WHEEL_RADIUS,
        "SteeringRimSegments": STEERING_RIM_SEGMENTS,
        "HelmetRadius": HELMET_RADIUS,
        "HeadlampY": HEADLAMP_Y, "HeadlampZ": HEADLAMP_Z,
        "HeadlampHalfSpacing": HEADLAMP_HALF_SPACING,
        "RoofPodY": ROOF_POD_Y, "RoofPodZ": ROOF_POD_Z,
        "RoofPodInnerX": ROOF_POD_INNER_X, "RoofPodOuterX": ROOF_POD_OUTER_X,
        "LensThickness": LENS_THICKNESS, "LensInset": LENS_INSET,
    }
    vectors = {
        "SteeringHub": STEERING_HUB, "SteeringRack": STEERING_RACK,
        "HeadlampSize": HEADLAMP_SIZE, "RoofPodSize": ROOF_POD_SIZE,
        "SeatBaseTop": SEAT_BASE_TOP, "Shoulder": SHOULDER,
        "HelmetCentre": HELMET_CENTRE,
    }

    drifted = []
    for field, ours in scalars.items():
        # Terminator is a comma inside KartDimensions' initialiser and a semicolon on the
        # standalone consts; anchoring on either stops a prefix match on a longer name.
        match = re.search(rf"\b{field}\s*=\s*(-?[\d.]+)f?\s*[;,]", text)
        if not match:
            drifted.append(f"{field}: not found in KartBlueprint.cs")
        elif abs(float(match.group(1)) - ours) > 1e-6:
            drifted.append(f"{field}: C# has {match.group(1)}, kartworks.py has {ours}")

    for field, ours in vectors.items():
        match = re.search(rf"\b{field}\s*=\s*new Vector3\(([^)]*)\)", text)
        if not match:
            drifted.append(f"{field}: not found in KartBlueprint.cs")
            continue
        theirs = [float(part.strip().rstrip("f")) for part in match.group(1).split(",")]
        if any(abs(a - b) > 1e-6 for a, b in zip(theirs, ours)):
            drifted.append(f"{field}: C# has {theirs}, kartworks.py has {list(ours)}")

    if drifted:
        raise AssertionError(
            f"kartworks.py disagrees with KartBlueprint.cs (building {who}):\n  - "
            + "\n  - ".join(drifted))
    print("  ok    dimensions agree with KartBlueprint.cs")

    _check_suspension_travel()


def _check_suspension_travel():
    """Fail the build if the arches are cut for a travel the controller no longer uses.

    A second file to scrape, but the same class of mistake as the wheel radii: cut an arch
    for 200 mm of travel on a kart that has 280 and the tyre comes through the top of the
    fender - while driving, which is the hardest place to notice a modelling error.
    """
    text, path = _read("Assets", "Kart", "Scripts", "KartController.cs")
    if text is None:
        print(f"  skip  KartController.cs not found at {path}")
        return

    match = re.search(r"\bpublic\s+float\s+suspensionDistance\s*=\s*(-?[\d.]+)f?\s*;", text)
    if not match:
        print("  skip  suspensionDistance not found in KartController.cs")
        return

    theirs = float(match.group(1))
    if abs(theirs - SUSPENSION_TRAVEL) > 1e-6:
        raise AssertionError(
            f"kartworks.py cuts every style's arches for {SUSPENSION_TRAVEL} m of suspension "
            f"travel, but KartController.suspensionDistance is {theirs}. The tyres will come "
            f"through the fenders - update SUSPENSION_TRAVEL and rebuild every style.")
    print(f"  ok    arches cut for {SUSPENSION_TRAVEL} m of travel, matching KartController")


# ---------------------------------------------------------------------------------------
# Emitting a style
# ---------------------------------------------------------------------------------------

def assert_tread(obj, radius, tolerance=1.5e-3):
    """Fail the build if anything on a wheel stands proud of its nominal radius.

    KartSuspension holds the hub exactly `radius` above the contact point, so a wheel whose
    outermost geometry is at 1.12 x radius does not ride 12% high - it drives with 30 mm of
    tread buried in the road, at every corner, on every kart of that style.

    Worth a fatal check rather than an eyeball for the same reason `validate` is fatal: the
    three-quarter preview renders a buried tread and a flush one almost identically, and the
    error is introduced by a helper's perpendicular axis rather than by a number anyone
    wrote down. It is measured on the built mesh, so it catches the mistake whatever drew it.

    The axle is Blender X by the authoring convention, so radial distance is in the YZ plane.
    """
    worst = 0.0
    for vert in obj.data.vertices:
        worst = max(worst, math.hypot(vert.co.y, vert.co.z))

    if worst > radius + tolerance:
        raise AssertionError(
            f"{obj.name}: tread peaks at {worst:.4f} m but the nominal radius is {radius:.4f} m "
            f"({worst / radius:.3f}x). KartSuspension holds the hub at the nominal radius, so "
            f"this wheel would drive {(worst - radius) * 1000:.0f} mm sunk into the road. Build "
            f"lugs with kartworks.tread_block, which cannot exceed the radius by construction.")
    print(f"  ok    {obj.name} tread peaks at {worst:.4f} m of {radius:.4f} m")


def write_manifest(key, style_palette, nose_lamps=False, roof_bar=False, meshes=None,
                   emissive=None):
    """Write a style's palette and lamp flags to Assets/GeneratedModels/Manifests.

    This exists for exactly the reason the farm pack's manifests do, and the README already
    names the anti-pattern it avoids: `BarrierAssetSetup.cs` carries a hand-written copy of
    the palette its Blender script produces, under a comment saying the two have to match,
    and nothing checks that they do.

    A kart style is forty-odd colour, metallic and roughness numbers. Copying those into C#
    nine times over is the same mistake at nine times the size, and the failure is quiet:
    the kart simply comes out the wrong colour and nobody can tell whether Blender or Unity
    is the one that is wrong. So Blender writes them and `KartStyleManifest` reads them.

    What is *not* here is the style's mesh names and its display name. Those stay hand-
    written in `KartStyle.All`, because Unity's `MenuItem` attributes are static and a style
    needs a hand-written menu entry regardless - and a wrong mesh name fails loudly at setup
    with "no model at ...", where a wrong colour fails silently. The generated half is the
    half that drifts undetectably.

    One file per style rather than one for the pack, because each style builds in its own
    headless Blender and two of them writing the same file is a race the runner would lose.

    Roughness is inverted to smoothness on the way out. Blender and Unity disagree about
    which end of that scale is which, and doing the flip here means neither side has to
    remember - the JSON is in Unity's units because Unity is what reads it.
    """
    out_dir = os.path.join(tb.EXPORT_DIR, "Manifests")
    os.makedirs(out_dir, exist_ok=True)
    path = os.path.join(out_dir, f"kart_{key}.json")

    emissive = emissive or {}
    slots = []
    for name, rgb, metallic, roughness in style_palette:
        slot = {
            "slot": name,
            "color": [round(c, 4) for c in rgb],
            "metallic": round(metallic, 4),
            "smoothness": round(1.0 - roughness, 4),
            "emission": [0.0, 0.0, 0.0],
        }
        if name in emissive:
            # Emission is written past 1.0 on purpose, the way KartLensLit is: a lamp or a
            # fissure that only reaches white reads as pale paint, not as something hot.
            slot["emission"] = [round(c, 4) for c in emissive[name]]
        slots.append(slot)

    payload = {
        "key": key,
        "noseLamps": bool(nose_lamps),
        "roofBar": bool(roof_bar),
        "meshes": list(meshes or []),
        "slots": slots,
    }

    with open(path, "w", encoding="utf-8") as handle:
        json.dump(payload, handle, indent=2)
        handle.write("\n")
    print(f"  ok    manifest -> {os.path.relpath(path, tb.REPO_ROOT)}")


def emit(build_fn, name, max_tris, max_size_m, tread_radius=None):
    """Build one of a style's meshes into its own fresh scene and export it.

    `origin="keep"` on all four, because every kart mesh is authored around a mount point
    rather than a footprint - the body around the kart origin, a wheel around its hub, the
    rim around the steering pivot. Re-centring any of them on its lowest vertex would slide
    it off the anchor the runtime places it at.

    fresh_scene is called here rather than inside a builder so the builders can also be
    driven from a live Blender session over MCP, where resetting to factory settings is
    blocked.
    """
    tb.fresh_scene()
    obj = build_fn()
    if tread_radius is not None:
        assert_tread(obj, tread_radius)
    tb.build(obj, name, max_tris=max_tris, max_size_m=max_size_m, origin="keep")
