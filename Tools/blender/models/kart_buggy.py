"""
Off-road buggy - a kart style: tube space frame, single seat, long-travel coil-overs.

    blender --background --factory-startup --python Tools/blender/models/kart_buggy.py

Exports four meshes, because a kart is not a prop and cannot be one mesh:

    KartBuggy_Body           the chassis, authored around the kart's own origin
    KartBuggy_WheelFront     authored around its hub, axle along local X
    KartBuggy_WheelRear      ditto, and fatter - the rears are the wider pair
    KartBuggy_SteeringWheel  authored around its hub, column up local Y

Every dimension is expressed in Unity kart space and converted on the way out by `u`, so
the numbers below can be read straight against KartDimensions and KartBlueprint on the C#
side. The wheel arches are cut for the wheels KartDimensions actually places, which is the
whole point of not making up a second set of numbers here.

The steering wheel is its own mesh for the same reason the road wheels are: it turns.
KartBlueprint spins the "Steering" pivot about its local Y and hangs the driver's hands off
it, so this mesh is authored in that pivot's space and parents straight onto it. The
column, the rack and the tie rods stay in the body, because those parts are static on the
C# side too.

The driver is deliberately absent and should stay that way. KartDriverRig re-aims the arms
at the wheel every frame, and geometry baked into a static mesh cannot do that.

The lamps are in the body mesh, but their glass is its own material slot: KartLights
switches the headlights on by swapping the material on those faces and hanging a real Unity
Light on the front of the nose pair, so the positions here are KartBlueprint's and are
asserted against it like the wheel dimensions are.
"""

import math
import os
import re
import sys

import bmesh
import bpy
from mathutils import Vector

sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
import kartworks as kw  # noqa: E402
import toebeans_blender as tb  # noqa: E402
from kartworks import mirrored, u, usize  # noqa: E402

BODY_NAME = "KartBuggy_Body"
WHEEL_FRONT_NAME = "KartBuggy_WheelFront"
WHEEL_REAR_NAME = "KartBuggy_WheelRear"
STEERING_WHEEL_NAME = "KartBuggy_SteeringWheel"

# ---------------------------------------------------------------------------------------
# The hard numbers.
#
# These used to be written out here, mirrored by hand from KartDimensions.Default. They
# live in kartworks.py now and are re-exported below, because every kart style is cut for
# the same wheels and nine copies of one table is the drift the pipeline README warns
# about. kartworks asserts them against the C# at build time, for all styles at once.
#
# Re-exported rather than referenced as `kw.X` throughout so the geometry below still reads
# against the C#, and so preview_kart.py can keep reading a style module's own dimensions.
# ---------------------------------------------------------------------------------------

FRONT_AXLE_Z = kw.FRONT_AXLE_Z
REAR_AXLE_Z = kw.REAR_AXLE_Z
FRONT_TRACK = kw.FRONT_TRACK
REAR_TRACK = kw.REAR_TRACK
FRONT_WHEEL_RADIUS = kw.FRONT_WHEEL_RADIUS
REAR_WHEEL_RADIUS = kw.REAR_WHEEL_RADIUS
FRONT_WHEEL_WIDTH = kw.FRONT_WHEEL_WIDTH
REAR_WHEEL_WIDTH = kw.REAR_WHEEL_WIDTH

SUSPENSION_TRAVEL = kw.SUSPENSION_TRAVEL
ARCH_GAP = kw.ARCH_GAP

ROLL_HOOP_TOP_Y = kw.ROLL_HOOP_TOP_Y
ROLL_HOOP_Z = kw.ROLL_HOOP_Z
STEERING_RACK = kw.STEERING_RACK
STEERING_HUB = kw.STEERING_HUB
STEERING_WHEEL_RADIUS = kw.STEERING_WHEEL_RADIUS
STEERING_RIM_SEGMENTS = kw.STEERING_RIM_SEGMENTS

HEADLAMP_Y = kw.HEADLAMP_Y
HEADLAMP_Z = kw.HEADLAMP_Z
HEADLAMP_HALF_SPACING = kw.HEADLAMP_HALF_SPACING
HEADLAMP_SIZE = kw.HEADLAMP_SIZE

ROOF_POD_Y = kw.ROOF_POD_Y
ROOF_POD_Z = kw.ROOF_POD_Z
ROOF_POD_INNER_X = kw.ROOF_POD_INNER_X
ROOF_POD_OUTER_X = kw.ROOF_POD_OUTER_X
ROOF_POD_SIZE = kw.ROOF_POD_SIZE

LENS_THICKNESS = kw.LENS_THICKNESS
LENS_INSET = kw.LENS_INSET

# ---------------------------------------------------------------------------------------
# Frame layout
# ---------------------------------------------------------------------------------------

RAIL_X = 0.40          # lower longitudinal rails
# Roll cage uprights. Held at 0.48 rather than pushed wider because the side impact bar
# runs back to z = -0.70, where the rear tyre's inner face is at 0.53: at 0.48 plus the
# brace radius the bar clears it by 18 mm, and at 0.50 it does not.
CAGE_X = 0.48
RAIL_Y = 0.24
FLOOR_TOP_Y = 0.22

# The prow, as two corners per side. The cage's forward sweep lands on NOSE_HIGH, so the
# nose and the cage are one continuous line rather than two structures that happen to meet
# - which is what the reference does and what stops the front reading as an amputation.
NOSE_LOW = (0.36, 0.30, 1.16)
NOSE_HIGH = (0.28, 0.56, 1.30)

TAIL_Z = -1.02
CAGE_FRONT_Z = 0.40
CAGE_FRONT_TOP_Y = 1.24
CAGE_FRONT_TOP_Z = 0.22

MAIN_TUBE = 0.042      # cage and rails
BRACE_TUBE = 0.032     # bracing and suspension links
THIN_TUBE = 0.026

# Material slots. The order is the contract with tb.assign_materials, and the names and
# colours are KartSetup's, so the imported mesh lands on the palette the rest of the kart
# already wears. Unity smoothness is Blender roughness inverted.
FRAME, BODY, SEAT, RIM, RUBBER, GLASS = range(6)

# What each slot is used for here. In the reference the space frame itself is the painted
# part and the suspension hanging off it is black, so the tubes take the kart's identity
# colour and the links stay dark - which also stops the buggy reading as grey scaffolding
# from the low camera this game runs.
TUBE = BODY      # roll cage, rails, nose - the silhouette
LINK = FRAME     # wishbones, trailing arms, axle, steering column
PANEL = FRAME    # floor pan and engine, dark so the frame reads against them
TRIM = SEAT      # seat and fenders
METAL = RIM      # springs, light pod housings, exhaust
LENS = GLASS     # the glass in the front of each lamp, and nothing else

PALETTE = [
    ("KartFrame", (0.22, 0.23, 0.26), 0.65, 0.55),
    ("KartBody", (0.88, 0.33, 0.09), 0.10, 0.55),
    ("KartSeat", (0.13, 0.13, 0.15), 0.00, 0.70),
    ("KartRim", (0.72, 0.74, 0.78), 0.90, 0.30),
    ("KartRubber", (0.07, 0.07, 0.08), 0.00, 0.78),
    # Cold glass - what a lamp looks like switched off. Unity repaints this slot with KartLens and
    # swaps it for the emissive KartLensLit when the headlights come on, so the colour here only
    # has to survive the Blender preview.
    ("KartLens", (0.62, 0.64, 0.62), 0.20, 0.05),
]


# ---------------------------------------------------------------------------------------
# Chassis
# ---------------------------------------------------------------------------------------

def add_lower_frame(bm):
    """Floor pan, side rails and the nose that sweeps up off them."""
    # Floor pan. The lowest thing on the buggy, so it is what grounds out rather than a
    # suspension link - same rule the kart's own floor pan follows.
    tb.cuboid(bm, u(0.0, FLOOR_TOP_Y - 0.025, 0.10), usize(0.74, 0.05, 1.44), PANEL)

    for _side, (front, rear, nose_low, nose_high, tail_top) in mirrored(
            (RAIL_X, RAIL_Y, 0.88), (RAIL_X, RAIL_Y, TAIL_Z),
            NOSE_LOW, NOSE_HIGH, (0.50, 0.40, TAIL_Z)):
        tb.tube(bm, front, rear, MAIN_TUBE, TUBE)
        tb.tube(bm, front, nose_low, MAIN_TUBE, TUBE)   # lower nose rail, sweeping up
        tb.tube(bm, nose_low, nose_high, MAIN_TUBE, TUBE)  # prow riser
        tb.tube(bm, rear, tail_top, BRACE_TUBE, TUBE)   # closes the tail off the rail

    for z in (0.88, 0.10, -0.56, TAIL_Z):
        tb.tube(bm, u(-RAIL_X, RAIL_Y, z), u(RAIL_X, RAIL_Y, z), BRACE_TUBE, TUBE)

    for point in (NOSE_LOW, NOSE_HIGH):
        tb.tube(bm, u(-point[0], point[1], point[2]), u(*point), MAIN_TUBE, TUBE)

    # Plate across the prow. The frame alone leaves the front as an outline; this is the
    # panel the reference has there, and it gives the nose something to catch the light.
    tb.slab(bm, u(0.0, NOSE_LOW[1], NOSE_LOW[2]), u(0.0, NOSE_HIGH[1], NOSE_HIGH[2]),
            0.58, 0.03, PANEL)


def add_roll_cage(bm):
    """The cage. It is most of the silhouette, so it carries the thickest tubes."""
    hoop_top_z = ROLL_HOOP_Z + 0.06

    for side, (a_foot, a_top, hoop_foot, hoop_top, brace_foot) in mirrored(
            (CAGE_X, 0.26, CAGE_FRONT_Z), (CAGE_X, CAGE_FRONT_TOP_Y, CAGE_FRONT_TOP_Z),
            (CAGE_X, 0.26, ROLL_HOOP_Z - 0.02), (CAGE_X, ROLL_HOOP_TOP_Y, hoop_top_z),
            (0.50, 0.40, TAIL_Z)):
        tb.tube(bm, a_foot, a_top, MAIN_TUBE, TUBE)          # A-pillar
        tb.tube(bm, hoop_foot, hoop_top, MAIN_TUBE, TUBE)    # main hoop upright
        tb.tube(bm, a_top, hoop_top, MAIN_TUBE, TUBE)        # roof rail
        tb.tube(bm, hoop_top, brace_foot, BRACE_TUBE, TUBE)  # rear brace
        # Forward sweep from the cage onto the top of the prow - the line that makes this
        # read as a buggy rather than as a kart with a hoop on it.
        tb.tube(bm, a_top, u(NOSE_HIGH[0] * side, NOSE_HIGH[1], NOSE_HIGH[2]),
                BRACE_TUBE, TUBE)
        # Side impact bar, and the tie that lands the cage on the rail.
        tb.tube(bm, u(CAGE_X * side, 0.60, 0.34), u(CAGE_X * side, 0.66, -0.70),
                BRACE_TUBE, TUBE)
        tb.tube(bm, a_foot, u(RAIL_X * side, RAIL_Y, CAGE_FRONT_Z), THIN_TUBE, TUBE)

    tb.tube(bm, u(-CAGE_X, CAGE_FRONT_TOP_Y, CAGE_FRONT_TOP_Z),
            u(CAGE_X, CAGE_FRONT_TOP_Y, CAGE_FRONT_TOP_Z), MAIN_TUBE, TUBE)
    tb.tube(bm, u(-CAGE_X, ROLL_HOOP_TOP_Y, hoop_top_z),
            u(CAGE_X, ROLL_HOOP_TOP_Y, hoop_top_z), MAIN_TUBE, TUBE)
    tb.tube(bm, u(-0.50, 0.40, TAIL_Z), u(0.50, 0.40, TAIL_Z), MAIN_TUBE, TUBE)

    # The X behind the seat, where the reference has its cargo net. Kept aft of the main
    # hoop so it braces the engine bay instead of crossing the seat back.
    tb.tube(bm, u(-0.44, 1.36, -0.68), u(0.44, 0.36, -0.96), THIN_TUBE, TUBE)
    tb.tube(bm, u(0.44, 1.36, -0.68), u(-0.44, 0.36, -0.96), THIN_TUBE, TUBE)


def add_cockpit(bm):
    """Seat, dash and steering column."""
    tb.slab(bm, u(0.0, 0.34, -0.16), u(0.0, 0.36, -0.62), 0.44, 0.10, TRIM)
    tb.slab(bm, u(0.0, 0.40, -0.60), u(0.0, 1.02, -0.78), 0.40, 0.10, TRIM)
    tb.cuboid(bm, u(0.0, 1.06, -0.80), usize(0.24, 0.14, 0.09), TRIM)

    # Wings up either side of the back, so the seat reads as a bucket rather than as one
    # grey slab filling the middle of the cage.
    for _side, (wing_low, wing_high) in mirrored((0.22, 0.38, -0.26), (0.22, 0.92, -0.72)):
        tb.slab(bm, wing_low, wing_high, 0.07, 0.15, TRIM)

    tb.cuboid(bm, u(0.0, 0.58, 0.44), usize(0.44, 0.08, 0.22), PANEL)
    tb.tube(bm, u(*STEERING_RACK), u(*STEERING_HUB), THIN_TUBE, LINK)


def add_drivetrain(bm):
    """Engine behind the seat, and the exhaust climbing out of it."""
    tb.cuboid(bm, u(0.0, 0.56, -0.94), usize(0.48, 0.38, 0.36), PANEL)
    tb.tube(bm, u(0.16, 0.72, -0.96), u(0.16, 1.06, -1.06), 0.030, METAL)
    tb.tube(bm, u(-0.60, REAR_WHEEL_RADIUS, REAR_AXLE_Z),
            u(0.60, REAR_WHEEL_RADIUS, REAR_AXLE_Z), 0.040, LINK)


def add_suspension(bm):
    """Long-travel coil-overs at all four corners - the reference's loudest detail."""
    for side, _ in mirrored((0.0, 0.0, 0.0)):
        # Front: wishbones out to the hub, plus the shock leaning in over them.
        tb.tube(bm, u(0.28 * side, RAIL_Y, FRONT_AXLE_Z),
                u(0.52 * side, FRONT_WHEEL_RADIUS, FRONT_AXLE_Z), BRACE_TUBE, LINK)
        tb.tube(bm, u(0.30 * side, 0.48, FRONT_AXLE_Z - 0.04),
                u(0.52 * side, 0.42, FRONT_AXLE_Z), THIN_TUBE, LINK)
        add_coilover(bm, u(0.30 * side, 0.32, 0.70), u(0.54 * side, 0.78, 0.62))

        # Rear: trailing arm back to the axle, shock over the top of it.
        tb.tube(bm, u(0.30 * side, 0.30, -0.48),
                u(0.56 * side, REAR_WHEEL_RADIUS, REAR_AXLE_Z), 0.034, LINK)
        add_coilover(bm, u(0.32 * side, 0.36, -0.78), u(0.56 * side, 0.92, -0.68))


def add_coilover(bm, lower, upper):
    """A damper rod with a fatter spring over its middle."""
    tb.tube(bm, lower, upper, 0.026, LINK)
    tb.tube(bm, lower.lerp(upper, 0.18), lower.lerp(upper, 0.82), 0.055, METAL, segments=8)


def add_fenders(bm):
    """Plastic arches over each wheel, cut to clear the wheel across its whole travel.

    The wheel is not where the arch used to think it was. KartController hangs each wheel
    visual at `WheelCentre + up * compression`, so in the body's own frame a wheel climbs
    the entire SUSPENSION_TRAVEL between full droop and full bump. These arches were cut at
    `radius + 0.05` around the droop position, which the wheel leaves behind 50 mm into a
    280 mm range — it was already through the fender sitting still, because static sag alone
    is about 97 mm.

    Clearing the whole sweep costs height and there is no way to dodge that: a tyre of
    radius R sweeping T needs its arch's inner surface at 2R + T. What the centring buys is
    where that cost lands. Sitting the arch on the middle of the travel rather than on
    either end splits the daylight evenly above and below the tyre, so it reads as a
    long-travel offroad arch instead of a fender left hanging over a wheel that ducked.
    """
    lift = SUSPENSION_TRAVEL * 0.5
    for side in (-1, 1):
        add_arch(bm, (FRONT_TRACK * 0.5 * side, FRONT_WHEEL_RADIUS + lift, FRONT_AXLE_Z),
                 FRONT_WHEEL_RADIUS + lift + ARCH_GAP, FRONT_WHEEL_WIDTH * 0.5 + 0.03)
        add_arch(bm, (REAR_TRACK * 0.5 * side, REAR_WHEEL_RADIUS + lift, REAR_AXLE_Z),
                 REAR_WHEEL_RADIUS + lift + ARCH_GAP, REAR_WHEEL_WIDTH * 0.5 + 0.03)


def add_arch(bm, centre, radius, half_width, thickness=0.03, segments=5,
             start_deg=32.0, end_deg=148.0):
    """A closed curved shell over the top of a wheel.

    Built as a solid rather than as a strip of single-sided quads: Unity culls back faces,
    so a one-sided arch vanishes from exactly the low camera angle this game uses.
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

    # Ring vertex order is (near inner, near outer, far outer, far inner) once reordered,
    # which makes the four side faces a simple walk around it.
    loops = [[r[0], r[1], r[3], r[2]] for r in rings]
    made = []
    for a, b in zip(loops, loops[1:]):
        for j in range(4):
            k = (j + 1) % 4
            made.append(bm.faces.new((a[j], a[k], b[k], b[j])))
    made.append(bm.faces.new(loops[0]))
    made.append(bm.faces.new(loops[-1][::-1]))
    for face in made:
        face.material_index = TRIM


def lamp_mounts():
    """Every lamp on the buggy as (centre, housing size), nose pair first.

    One list rather than two builders because Unity walks the same list on its side -
    KartBlueprint.Lamps() is this function - and two lists that have to agree across a language
    boundary is one more than the drift check can keep honest.
    """
    mounts = [((x, HEADLAMP_Y, HEADLAMP_Z), HEADLAMP_SIZE)
              for x in (-HEADLAMP_HALF_SPACING, HEADLAMP_HALF_SPACING)]
    mounts += [((x, ROOF_POD_Y, ROOF_POD_Z), ROOF_POD_SIZE)
               for x in (-ROOF_POD_OUTER_X, -ROOF_POD_INNER_X,
                         ROOF_POD_INNER_X, ROOF_POD_OUTER_X)]
    return mounts


def add_lights(bm):
    """Lamp housings, their glass, and the stalks standing the roof pods off the screen rail.

    The glass is a separate box in its own material slot rather than the front face of the
    housing. It has to be: Unity switches the headlights on by swapping the material on exactly
    these faces, and a lens that shares a slot with the pod would light the whole pod up with it.
    """
    for (x, y, z), size in lamp_mounts():
        tb.cuboid(bm, u(x, y, z), usize(*size), METAL)

        # Proud of the housing's front face by half its own thickness, which is where
        # KartLamp.LensCentre puts it on the C# side and where the Light is hung.
        lens_z = z + (size[2] + LENS_THICKNESS) * 0.5
        tb.cuboid(bm, u(x, y, lens_z),
                  usize(size[0] - LENS_INSET * 2, size[1] - LENS_INSET * 2, LENS_THICKNESS),
                  LENS)

    # Only the roof pods need standing off anything - the nose pair sit on the prow plate.
    for x in (-ROOF_POD_OUTER_X, -ROOF_POD_INNER_X, ROOF_POD_INNER_X, ROOF_POD_OUTER_X):
        tb.tube(bm, u(x, CAGE_FRONT_TOP_Y + 0.01, ROOF_POD_Z),
                u(x, ROOF_POD_Y - ROOF_POD_SIZE[1] * 0.5 + 0.02, ROOF_POD_Z), 0.018, LINK)


def add_cage_padding(bm):
    """Foam sleeves on the uprights either side of the driver's head.

    Placed by lerping along the cage tubes rather than by absolute height, so they stay
    glued to the cage if its geometry moves.
    """
    for _side, (a_foot, a_top, hoop_foot, hoop_top) in mirrored(
            (CAGE_X, 0.26, CAGE_FRONT_Z), (CAGE_X, CAGE_FRONT_TOP_Y, CAGE_FRONT_TOP_Z),
            (CAGE_X, 0.26, ROLL_HOOP_Z - 0.02),
            (CAGE_X, ROLL_HOOP_TOP_Y, ROLL_HOOP_Z + 0.06)):
        tb.tube(bm, a_foot.lerp(a_top, 0.36), a_foot.lerp(a_top, 0.82),
                MAIN_TUBE + 0.014, TRIM)
        tb.tube(bm, hoop_foot.lerp(hoop_top, 0.38), hoop_foot.lerp(hoop_top, 0.84),
                MAIN_TUBE + 0.014, TRIM)


def add_side_nets(bm):
    """Webbing across the lower half of the door opening.

    Kept below the side impact bar rather than filling the whole opening as the reference
    does: this game shows you your own driver, and a net at torso height hides them.
    """
    x = CAGE_X - 0.012
    for side in (-1, 1):
        for z_low, z_high in ((0.32, 0.02), (0.02, -0.28), (-0.28, -0.58)):
            tb.tube(bm, u(x * side, 0.29, z_low), u(x * side, 0.60, z_high),
                    0.014, TRIM, segments=4)
        tb.tube(bm, u(x * side, 0.45, 0.32), u(x * side, 0.45, -0.60),
                0.014, TRIM, segments=4)


def add_controls(bm):
    """Pedals and the shift lever.

    The pedals sit where KartBlueprint puts them, because the driver's feet are posed onto
    that spot - Ankle is measured to rest on the floor pan just behind them.
    """
    for _side, (pedal_low, pedal_high) in mirrored((0.17, 0.24, 0.68), (0.17, 0.36, 0.64)):
        tb.slab(bm, pedal_low, pedal_high, 0.10, 0.03, METAL)

    # Outboard of the seat wings, inboard of the cage, so it misses both.
    tb.tube(bm, u(0.30, 0.36, -0.20), u(0.26, 0.66, -0.28), 0.018, LINK)
    tb.cuboid(bm, u(0.26, 0.70, -0.29), usize(0.07, 0.07, 0.07), TRIM)


def add_steering_linkage(bm):
    """Rack and tie rods out to the front hubs.

    Static, like the column and like KartBlueprint's own rack: only the wheel itself turns.
    """
    tb.tube(bm, u(-0.16, STEERING_RACK[1], STEERING_RACK[2]),
            u(0.16, STEERING_RACK[1], STEERING_RACK[2]), 0.030, LINK)
    for side, _ in mirrored((0.0, 0.0, 0.0)):
        tb.tube(bm, u(0.06 * side, 0.30, 0.32), u(0.50 * side, 0.28, 0.76), 0.016, LINK)


def add_mirrors(bm):
    """Stalk mirrors off the A-pillars, clear of the front tyres at full lock."""
    for side, _ in mirrored((0.0, 0.0, 0.0)):
        tb.tube(bm, u(CAGE_X * side, 0.98, 0.27), u((CAGE_X + 0.10) * side, 1.02, 0.33),
                0.014, LINK, segments=4)
        tb.cuboid(bm, u((CAGE_X + 0.13) * side, 1.03, 0.34), usize(0.03, 0.09, 0.13),
                  METAL)



def build_body():
    """Assemble the chassis. Assumes an empty scene; see __main__ for why it is separate."""
    bm = bmesh.new()
    add_lower_frame(bm)
    add_roll_cage(bm)
    add_cockpit(bm)
    add_drivetrain(bm)
    add_suspension(bm)
    add_fenders(bm)
    add_lights(bm)
    add_cage_padding(bm)
    add_side_nets(bm)
    add_controls(bm)
    add_steering_linkage(bm)
    add_mirrors(bm)

    obj = tb.mesh_from_bmesh(bm, BODY_NAME)
    tb.assign_materials(obj, PALETTE)
    return obj


# ---------------------------------------------------------------------------------------
# Wheels
# ---------------------------------------------------------------------------------------

def build_wheel(name, radius, width, lugs=12):
    """A knobby off-road wheel centred on its hub, axle along local X.

    Centred on the hub rather than sitting on the ground because this mesh spins: an origin
    anywhere else turns the wheel into a cam. Matches the axis convention in
    KartBlueprint.BuildWheel, whose comment is the other half of this contract.
    """
    bm = bmesh.new()

    # Carcass, rim face and hub, all coaxial along Unity X. The carcass is drawn under the
    # nominal radius so the tread blocks are what actually meets the ground - a knobby
    # tyre whose lugs sit flush with the casing just reads as a smooth one. The lugs then
    # peak at exactly `radius`, which is the radius KartSuspension holds the hub above the
    # contact point: let them stand any prouder and the tread sinks into the road.
    tb.tube(bm, u(-width * 0.5, 0, 0), u(width * 0.5, 0, 0), radius * 0.86, RUBBER,
            segments=12)
    tb.tube(bm, u(-width * 0.54, 0, 0), u(width * 0.54, 0, 0), radius * 0.56, RIM,
            segments=12)
    tb.tube(bm, u(-width * 0.60, 0, 0), u(width * 0.60, 0, 0), radius * 0.22, RIM,
            segments=6)

    # Rim spokes across the outer face, so the wheel is not a blank disc side-on.
    for i in range(3):
        phi = 2.0 * math.pi * i / 3.0
        rim = Vector((0.0, math.sin(phi), math.cos(phi))) * (radius * 0.50)
        tb.slab(bm, u(width * 0.55, 0.0, 0.0), u(width * 0.55, rim.y, rim.z),
                width * 0.10, radius * 0.16, RIM)

    # Tread blocks, staggered either side of the centreline and alternating in size, which
    # is what makes a tyre read as knobby rather than as a ring of identical teeth. They
    # run past the sidewall on purpose: the shoulder lugs are most of the off-road look
    # from the three-quarter angle the race camera actually sees.
    for i in range(lugs):
        theta = 2.0 * math.pi * i / lugs
        direction = Vector((0.0, math.sin(theta), math.cos(theta)))
        big = i % 2 == 0
        offset = width * (0.26 if big else -0.26)
        inner = direction * (radius * 0.80)
        outer = direction * (radius * (1.00 if big else 0.95))
        tb.slab(bm,
                u(offset, inner.y, inner.z), u(offset, outer.y, outer.z),
                width * 0.52, radius * (0.30 if big else 0.24), RUBBER)

    obj = tb.mesh_from_bmesh(bm, name)
    tb.assign_materials(obj, PALETTE)
    return obj


def build_steering_wheel(name=STEERING_WHEEL_NAME):
    """The steering wheel, authored in the "Steering" pivot's own local space.

    KartBlueprint spins that pivot about its local Y and hangs the driver's hands off it,
    so the rim has to lie in the pivot's local XZ plane with the column running up local
    Y. Author it in any other frame and the wheel turns like a tabletop. Because `u` maps
    Unity Y to Blender Z, building it here in Unity coordinates lands it correctly.
    """
    bm = bmesh.new()

    ring = []
    for i in range(STEERING_RIM_SEGMENTS):
        phi = 2.0 * math.pi * i / STEERING_RIM_SEGMENTS
        ring.append((STEERING_WHEEL_RADIUS * math.cos(phi), 0.0,
                     STEERING_WHEEL_RADIUS * math.sin(phi)))

    # Chords between consecutive points, so the rim closes on itself as a decagon - the
    # same faceted rim KartBlueprint builds, for the same reason.
    for i, point in enumerate(ring):
        tb.tube(bm, u(*point), u(*ring[(i + 1) % STEERING_RIM_SEGMENTS]), 0.022, TRIM,
                segments=5)

    tb.tube(bm, u(0.0, -0.022, 0.0), u(0.0, 0.022, 0.0), 0.052, METAL, segments=8)

    # Three spokes, the odd one pointing down so the wheel has a visible straight-ahead.
    for i in range(3):
        phi = math.radians(90.0 + i * 120.0)
        rim = (STEERING_WHEEL_RADIUS * math.cos(phi), 0.0,
               STEERING_WHEEL_RADIUS * math.sin(phi))
        tb.slab(bm, u(0.0, 0.0, 0.0), u(*rim), 0.038, 0.018, METAL)

    obj = tb.mesh_from_bmesh(bm, name)
    tb.assign_materials(obj, PALETTE)
    return obj


# ---------------------------------------------------------------------------------------
# Cross-language check
#
# `check_against_blueprint` and `check_suspension_travel` used to live here, scraping
# KartBlueprint.cs and KartController.cs for the numbers this file mirrored. They are in
# kartworks.py now and guard every style at once, which is the point: a wheel radius change
# should fail nine builds rather than leave eight models cut for a wheel the physics no
# longer places.
# ---------------------------------------------------------------------------------------


if __name__ == "__main__":
    # fresh_scene is called here rather than inside the builders so that the builders can
    # also be driven from a live Blender session over MCP, where resetting to factory
    # settings is blocked.
    kw.check_against_blueprint("kart_buggy.py")
    kw.write_manifest(
        "Buggy", PALETTE, nose_lamps=True, roof_bar=True,
        meshes=[BODY_NAME, WHEEL_FRONT_NAME, WHEEL_REAR_NAME, STEERING_WHEEL_NAME],
    )

    # Budgets sit about a quarter above what the model currently costs. This is the thing
    # the player looks at all race, so it can afford more than a scattered prop - but it
    # is also four wheels plus a body per kart on a grid, so "hero" is not "unbudgeted".
    tb.fresh_scene()
    tb.build(build_body(), BODY_NAME, max_tris=2600, max_size_m=3.0, origin="keep")

    tb.fresh_scene()
    tb.build(build_wheel(WHEEL_FRONT_NAME, FRONT_WHEEL_RADIUS, FRONT_WHEEL_WIDTH),
             WHEEL_FRONT_NAME, max_tris=520, max_size_m=1.0, origin="keep")

    tb.fresh_scene()
    tb.build(build_wheel(WHEEL_REAR_NAME, REAR_WHEEL_RADIUS, REAR_WHEEL_WIDTH),
             WHEEL_REAR_NAME, max_tris=520, max_size_m=1.0, origin="keep")

    tb.fresh_scene()
    tb.build(build_steering_wheel(), STEERING_WHEEL_NAME, max_tris=400, max_size_m=0.5,
             origin="keep")
