"""
Field marshal - the farm kart. A tractor faked out of fenders, because radius is fixed.

    blender --background --factory-startup --python Tools/blender/models/field_marshal.py

The whole design is one trick. A tractor reads as a tractor because its rear wheels are
enormous and its fronts are small, and this kart cannot have that: `KartDimensions` gives
every style the same 0.26 front and 0.30 rear, and the physics places the wheels there. So
the proportion is faked with the only thing that is free - the bodywork over them.

    rear    a huge fender arc floating well above the tyre, wrapping nearly half the
            circle, wider than the wheel. The eye reads the *arc* as the wheel's edge.
    front   a narrow shroud cut tight to the tyre and narrower than it, which makes the
            same-size front wheel read small.

Both fenders therefore break the rule the buggy's follow: they are not cut to hug the
wheel. The rear one is deliberately oversized and the front one deliberately mean. What
they still respect is the clearance floor - `kartworks.fender_arch` is fed a gap, never a
radius, so neither can end up closer to the tyre than the suspension travel allows.

The tall element is the exhaust stack with its flapper cap, and it is the only thing on the
kart allowed above the roll hoop. The hoop is short and plain on purpose so the stack owns
the skyline, the same way the plow owns the piste basher's front.
"""

import math
import os
import sys

import bmesh
from mathutils import Vector

sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
import kartworks as kw  # noqa: E402
import toebeans_blender as tb  # noqa: E402
from kartworks import u, usize, mirrored  # noqa: E402

BODY_NAME = "KartField_Body"
WHEEL_FRONT_NAME = "KartField_WheelFront"
WHEEL_REAR_NAME = "KartField_WheelRear"
STEERING_WHEEL_NAME = "KartField_SteeringWheel"

FRONT_WHEEL_RADIUS = kw.FRONT_WHEEL_RADIUS
REAR_WHEEL_RADIUS = kw.REAR_WHEEL_RADIUS
FRONT_TRACK = kw.FRONT_TRACK
REAR_TRACK = kw.REAR_TRACK
FRONT_AXLE_Z = kw.FRONT_AXLE_Z
REAR_AXLE_Z = kw.REAR_AXLE_Z
STEERING_HUB = kw.STEERING_HUB
STEERING_RACK = kw.STEERING_RACK

FRAME, BODY, SEAT, RIM, RUBBER, LENS = range(6)

CAST = FRAME        # cast iron: engine block, transmission housing, axle
GREEN = BODY        # the paint
STRAW = SEAT        # hay bale seat and its twine
YELLOW = RIM        # wheel centres, stack cap, grille surround - the second colour
TYRE = RUBBER

PALETTE = kw.palette(
    frame=((0.20, 0.22, 0.20), 0.30, 0.65),      # KartFrame  - dark cast iron
    # Deeper and more saturated than it looks on paper. Under the preview's key light a
    # mid green washes out to sage, and this pack's identity colour has to survive being
    # lit - the buggy's orange does, and the farm kart's green has to as well.
    # Roughness 0.78 rather than 0.55: at 0.55 the broad specular lobe washed the fender
    # tops out to a pale sage that had nothing to do with the albedo. Enamel on a farm
    # machine is chalky anyway, and a matt surface is what lets the colour survive the key
    # light - which is the whole job of a style's identity colour.
    body=((0.10, 0.28, 0.12), 0.05, 0.78),       # KartBody   - tractor green
    seat=((0.78, 0.66, 0.33), 0.00, 0.90),       # KartSeat   - baled straw
    rim=((0.90, 0.71, 0.11), 0.10, 0.45),        # KartRim    - implement yellow
    rubber=((0.07, 0.07, 0.08), 0.00, 0.82),     # KartRubber - farm lug tyre
)

# ---------------------------------------------------------------------------------------
# Layout
# ---------------------------------------------------------------------------------------

FLOOR_TOP_Y = 0.22
BONNET_X = 0.26        # the engine bay is narrow, which is half the tractor read
BONNET_TOP_Y = 0.72
BONNET_FRONT_Z = 1.22

# The fenders. Gaps rather than radii - see the module docstring.
#
# The rear gap started at 0.22, which sounded like "floating well above the tyre" and drew
# a 0.66 m arc sweeping 1.3 m from end to end: two green wings the length of the kart,
# reaching forward past the driver. The clearance floor already forces radius + 140 mm
# before any gap at all, so a rear fender is *inherently* half again the size of its wheel
# and only needs a little more to read as oversized. 0.10 is that little more.
REAR_FENDER_GAP = 0.10
REAR_FENDER_HALF_WIDTH = kw.REAR_WHEEL_WIDTH * 0.5 + 0.07
# The front shroud is narrower than its own tyre, which is the half of the trick that
# makes a 0.26 m front wheel read smaller than a 0.30 m rear one.
FRONT_SHROUD_GAP = 0.02
FRONT_SHROUD_HALF_WIDTH = kw.FRONT_WHEEL_WIDTH * 0.5 - 0.015

STACK_X = 0.30
STACK_Z = 0.72
STACK_TOP_Y = 1.56     # above the hoop, and the only thing that is

MAIN_TUBE = 0.044
BRACE_TUBE = 0.032
THIN_TUBE = 0.024


# ---------------------------------------------------------------------------------------
# Body
# ---------------------------------------------------------------------------------------

def add_frame(bm):
    """Floor pan and the cast spine a tractor is built along rather than on a chassis."""
    tb.cuboid(bm, u(0.0, FLOOR_TOP_Y - 0.025, 0.0), usize(0.72, 0.05, 1.52), CAST)
    # Transmission housing: the structural middle, between engine and back axle.
    tb.cuboid(bm, u(0.0, 0.40, -0.30), usize(0.40, 0.34, 0.70), CAST)
    tb.cuboid(bm, u(0.0, 0.46, -0.74), usize(0.52, 0.40, 0.30), CAST)


def add_bonnet(bm):
    """The long narrow engine bay, grille and the pipe running back from it.

    Narrow and tall rather than wide and low, which is the other half of the tractor read:
    everything forward of the driver has to be slimmer than the machine behind them.
    """
    tb.cuboid(bm, u(0.0, 0.52, 0.86), usize(BONNET_X * 2, 0.34, 0.72), GREEN)
    tb.cuboid(bm, u(0.0, BONNET_TOP_Y - 0.03, 0.86), usize(BONNET_X * 2 - 0.06, 0.08, 0.66),
              GREEN)
    # Nose taper down to the grille.
    tb.beam(bm, u(0.0, 0.56, 1.22), u(0.0, 0.50, BONNET_FRONT_Z + 0.06),
            BONNET_X * 2 - 0.04, 0.30, GREEN)

    # Grille: a yellow surround with vertical bars behind it.
    #
    # A surround, not a plate. Drawn as one 0.48 x 0.34 yellow slab it was the brightest
    # object on the kart and read as a warning sign bolted to the nose; four thin edges
    # frame the dark bars instead and let the green nose keep the front.
    for dx, dy, sx, sy in ((0.0, 0.17, 0.48, 0.05), (0.0, -0.17, 0.48, 0.05),
                           (0.235, 0.0, 0.05, 0.34), (-0.235, 0.0, 0.05, 0.34)):
        tb.cuboid(bm, u(dx, 0.50 + dy, BONNET_FRONT_Z + 0.085), usize(sx, sy, 0.05),
                  YELLOW)
    for x in (-0.15, -0.05, 0.05, 0.15):
        tb.cuboid(bm, u(x, 0.50, BONNET_FRONT_Z + 0.06), usize(0.035, 0.28, 0.04), CAST)

    # Side louvres, so the bonnet is not a bare green box.
    for side, _ in mirrored((0.0, 0.0, 0.0)):
        for z in (0.70, 0.82, 0.94, 1.06):
            tb.cuboid(bm, u((BONNET_X + 0.008) * side, 0.52, z), usize(0.02, 0.20, 0.045),
                      CAST)


def add_stack(bm):
    """Vertical exhaust with a flapper cap - the tallest thing on the kart."""
    tb.tube(bm, u(STACK_X, BONNET_TOP_Y - 0.10, STACK_Z),
            u(STACK_X, STACK_TOP_Y - 0.06, STACK_Z), 0.045, CAST, segments=8)
    # Slight flare at the top, then the cap sitting above it on its hinge.
    tb.taper(bm, u(STACK_X, STACK_TOP_Y - 0.06, STACK_Z),
             u(STACK_X, STACK_TOP_Y, STACK_Z), 0.045, 0.058, YELLOW, segments=8)
    tb.slab(bm, u(STACK_X - 0.055, STACK_TOP_Y + 0.035, STACK_Z - 0.02),
            u(STACK_X + 0.055, STACK_TOP_Y + 0.055, STACK_Z + 0.02), 0.13, 0.022, YELLOW)
    tb.tube(bm, u(STACK_X - 0.05, STACK_TOP_Y + 0.02, STACK_Z),
            u(STACK_X - 0.05, STACK_TOP_Y + 0.05, STACK_Z), 0.012, CAST, segments=4)
    # Bracket back to the bonnet.
    tb.tube(bm, u(STACK_X, 0.86, STACK_Z), u(BONNET_X * 0.9, 0.80, STACK_Z + 0.04),
            THIN_TUBE, CAST)

    # Air intake stack on the other side, shorter - a pair reads as a tractor where one
    # reads as a hot rod, and the asymmetry of heights keeps the stack itself dominant.
    tb.tube(bm, u(-STACK_X, BONNET_TOP_Y - 0.10, STACK_Z),
            u(-STACK_X, 1.20, STACK_Z), 0.038, CAST, segments=8)
    tb.tube(bm, u(-STACK_X, 1.20, STACK_Z), u(-STACK_X, 1.26, STACK_Z), 0.050, YELLOW,
            segments=8)


def add_fenders(bm):
    """The whole trick, in two calls.

    Both go through `kartworks.fender_arch`, which takes the daylight over the tyre rather
    than an absolute radius - so however oversized the rear one looks, it cannot end up
    closer to the tyre than the 280 mm of travel needs.
    """
    for side in (-1, 1):
        # Rear: wide and generous. The arc is what the eye takes for the wheel's outline.
        kw.fender_arch(bm, side, front=False, skin=GREEN, gap=REAR_FENDER_GAP,
                       half_width=REAR_FENDER_HALF_WIDTH,
                       thickness=0.038, segments=6, start_deg=26.0, end_deg=154.0)
        # Front: tight and narrower than the tyre, so the wheel reads small.
        kw.fender_arch(bm, side, front=True, skin=GREEN, gap=FRONT_SHROUD_GAP,
                       half_width=FRONT_SHROUD_HALF_WIDTH,
                       thickness=0.024, segments=4, start_deg=52.0, end_deg=128.0)

    # Steps hanging off the rear fenders, which is where a tractor's mounting step goes and
    # gives the big arc something to land on instead of ending in the air.
    for side, _ in mirrored((0.0, 0.0, 0.0)):
        x = REAR_TRACK * 0.5 * side
        tb.cuboid(bm, u(x, 0.34, REAR_AXLE_Z + 0.30), usize(0.24, 0.035, 0.12), CAST)
        tb.tube(bm, u(x, 0.36, REAR_AXLE_Z + 0.30),
                u(x, REAR_WHEEL_RADIUS + kw.SUSPENSION_TRAVEL * 0.5 + REAR_FENDER_GAP,
                  REAR_AXLE_Z + 0.24), THIN_TUBE, CAST)


def add_narrow_front_axle(bm):
    """A tractor's front end: the axle gathers to a narrow pivot under the bonnet."""
    for side, _ in mirrored((0.0, 0.0, 0.0)):
        tb.tube(bm, u(0.05 * side, 0.40, FRONT_AXLE_Z),
                u(0.52 * side, FRONT_WHEEL_RADIUS, FRONT_AXLE_Z), BRACE_TUBE, CAST)
        tb.tube(bm, u(0.52 * side, FRONT_WHEEL_RADIUS, FRONT_AXLE_Z),
                u(0.52 * side, FRONT_WHEEL_RADIUS + 0.16, FRONT_AXLE_Z), THIN_TUBE, CAST)
    tb.tube(bm, u(0.0, 0.40, FRONT_AXLE_Z - 0.10), u(0.0, 0.40, FRONT_AXLE_Z + 0.10),
            0.055, CAST, segments=6)


def add_cockpit(bm):
    """Hay bale seat, and the steering column raked the way a tractor's is."""
    # The bale: a straw block with twine round it and cut ends showing.
    tb.cuboid(bm, u(0.0, 0.44, -0.44), usize(0.50, 0.30, 0.42), STRAW)
    for z in (-0.56, -0.32):
        tb.cuboid(bm, u(0.0, 0.44, z), usize(0.52, 0.32, 0.03), STRAW)
    for x in (-0.16, 0.16):
        tb.cuboid(bm, u(x, 0.44, -0.44), usize(0.03, 0.32, 0.44), CAST)

    # Backrest bale, smaller, stood on end.
    tb.cuboid(bm, u(0.0, 0.72, -0.70), usize(0.46, 0.34, 0.24), STRAW)
    tb.cuboid(bm, u(0.0, 0.72, -0.70), usize(0.48, 0.05, 0.26), CAST)

    # Pan seat sitting on the bale, so the driver has something rigid under them.
    tb.slab(bm, u(0.0, 0.60, -0.28), u(0.0, 0.62, -0.56), 0.40, 0.045, CAST)

    # Column and rack.
    tb.tube(bm, u(*STEERING_RACK), u(*STEERING_HUB), 0.030, CAST)
    tb.tube(bm, u(-0.16, STEERING_RACK[1], STEERING_RACK[2]),
            u(0.16, STEERING_RACK[1], STEERING_RACK[2]), 0.028, CAST)
    for side, _ in mirrored((0.0, 0.0, 0.0)):
        tb.tube(bm, u(0.06 * side, 0.30, 0.32), u(0.50 * side, 0.28, 0.76), 0.016, CAST)
        tb.slab(bm, u(0.17 * side, 0.24, 0.68), u(0.17 * side, 0.36, 0.64), 0.10, 0.03, CAST)

    # Fuel tank slung under the bonnet's tail, where a tractor's sits.
    tb.tube(bm, u(-0.20, 0.66, 0.40), u(0.20, 0.66, 0.40), 0.13, GREEN, segments=8)
    tb.tube(bm, u(0.0, 0.79, 0.40), u(0.0, 0.83, 0.40), 0.035, YELLOW, segments=6)

    # Gear levers - two, because a tractor has a range lever as well.
    for x, lean in ((0.24, 0.04), (0.31, -0.02)):
        tb.tube(bm, u(x, 0.44, -0.02), u(x + lean, 0.74, -0.06), 0.017, CAST)
        tb.tube(bm, u(x + lean, 0.74, -0.06), u(x + lean, 0.78, -0.06), 0.028, YELLOW,
                segments=6)


def add_hoop(bm):
    """A short, plain hoop. It is not allowed to compete with the stack."""
    half = kw.ROLL_HOOP_HALF_WIDTH
    top = kw.ROLL_HOOP_TOP_Y
    z = kw.ROLL_HOOP_Z

    for side, _ in mirrored((0.0, 0.0, 0.0)):
        tb.tube(bm, u(half * side, 0.50, z), u(half * side, top, z), MAIN_TUBE, CAST)
        tb.tube(bm, u(half * side, top - 0.10, z), u((half - 0.06) * side, 0.56, z - 0.24),
                BRACE_TUBE, CAST)
    tb.tube(bm, u(-half, top, z), u(half, top, z), MAIN_TUBE, CAST)


def add_drawbar(bm):
    """A drawbar and top link off the back. Nothing tows anything, but a tractor without a
    hitch reads as a car with fenders."""
    tb.cuboid(bm, u(0.0, 0.32, -1.02), usize(0.14, 0.06, 0.30), CAST)
    tb.tube(bm, u(0.0, 0.32, -1.14), u(0.0, 0.32, -1.18), 0.045, YELLOW, segments=6)
    for side, _ in mirrored((0.0, 0.0, 0.0)):
        tb.tube(bm, u(0.20 * side, 0.56, -0.88), u(0.30 * side, 0.34, -1.08),
                BRACE_TUBE, CAST)
    tb.tube(bm, u(-0.60, REAR_WHEEL_RADIUS, REAR_AXLE_Z),
            u(0.60, REAR_WHEEL_RADIUS, REAR_AXLE_Z), 0.055, CAST)


def build_body():
    bm = bmesh.new()
    add_frame(bm)
    add_bonnet(bm)
    add_stack(bm)
    add_fenders(bm)
    add_narrow_front_axle(bm)
    add_cockpit(bm)
    add_hoop(bm)
    add_drawbar(bm)
    kw.lamps(bm, YELLOW, nose=True, roof=False)
    return kw.finish(bm, BODY_NAME, PALETTE)


# ---------------------------------------------------------------------------------------
# Wheels
# ---------------------------------------------------------------------------------------

def build_wheel(name, radius, width, front):
    """A farm tyre: a single bar tread in a shallow chevron, on a dished yellow centre.

    Farm lugs are big, few and widely spaced - the opposite of the piste basher's dense
    studded blocks - so the count is low and each bar is long. The yellow centre is what
    carries the style at distance, since the tyre itself is black like everyone else's.
    """
    bm = bmesh.new()
    bars = 7 if front else 8

    kw.wheel_carcass(bm, radius, width, TYRE, YELLOW, carcass=0.80, rim=0.52, hub=0.20,
                     spokes=0)

    # Dished centre with bolts, drawn here rather than by wheel_carcass's spokes because a
    # tractor wheel is a pressed dish, not a spoked one.
    tb.tube(bm, u(-width * 0.20, 0, 0), u(width * 0.28, 0, 0), radius * 0.54, YELLOW,
            segments=12)
    tb.tube(bm, u(width * 0.28, 0, 0), u(width * 0.34, 0, 0), radius * 0.30, CAST,
            segments=8)
    for _i, _theta, bolt in kw.around(6, radius * 0.40):
        tb.tube(bm, u(width * 0.28, bolt.y, bolt.z), u(width * 0.34, bolt.y, bolt.z),
                radius * 0.05, CAST, segments=4)

    # The bars. Each spans most of the width and leans, so consecutive bars read as a
    # coarse herringbone rather than a ladder.
    sweep = (2.0 * math.pi / bars) * 0.55
    for i, theta, _direction in kw.around(bars, 1.0):
        lean = sweep if i % 2 == 0 else -sweep
        kw.tread_block(bm, radius, theta, theta + lean,
                       -width * 0.34, width * 0.34, radius * 0.15, radius * 0.20, TYRE)

    return kw.finish(bm, name, PALETTE)


def build_steering_wheel(name=STEERING_WHEEL_NAME):
    """A big thin-rimmed wheel with a spinner knob, straight off a tractor.

    Thin rim and slender spokes: the concept calls for it, and it is the clearest possible
    contrast with the mine cart's cast capstan at the same radius. Authored in the
    "Steering" pivot's local space - rim in local XZ, column up local Y.
    """
    bm = bmesh.new()
    radius = kw.STEERING_WHEEL_RADIUS
    segments = kw.STEERING_RIM_SEGMENTS

    ring = [(radius * math.cos(2.0 * math.pi * i / segments), 0.0,
             radius * math.sin(2.0 * math.pi * i / segments)) for i in range(segments)]
    for i, point in enumerate(ring):
        tb.tube(bm, u(*point), u(*ring[(i + 1) % segments]), 0.014, CAST, segments=5)

    tb.tube(bm, u(0.0, -0.02, 0.0), u(0.0, 0.03, 0.0), 0.042, YELLOW, segments=8)

    for i in range(3):
        phi = math.radians(90.0 + i * 120.0)
        spoke = (radius * math.cos(phi), 0.0, radius * math.sin(phi))
        tb.slab(bm, u(0.0, 0.0, 0.0), u(*spoke), 0.022, 0.012, CAST)

    # The spinner knob. Off the rim, on its own little collar.
    phi = math.radians(215.0)
    knob = (radius * 0.88 * math.cos(phi), 0.0, radius * 0.88 * math.sin(phi))
    tb.tube(bm, u(knob[0], 0.0, knob[2]), u(knob[0], 0.042, knob[2]), 0.016, CAST,
            segments=6)
    tb.tube(bm, u(knob[0], 0.042, knob[2]), u(knob[0], 0.075, knob[2]), 0.030, YELLOW,
            segments=6)

    return kw.finish(bm, name, PALETTE)


if __name__ == "__main__":
    kw.check_against_blueprint("field_marshal.py")
    kw.write_manifest(
        "Field", PALETTE, nose_lamps=True, roof_bar=False,
        meshes=[BODY_NAME, WHEEL_FRONT_NAME, WHEEL_REAR_NAME, STEERING_WHEEL_NAME],
    )

    kw.emit(build_body, BODY_NAME, max_tris=3200, max_size_m=3.0)
    kw.emit(lambda: build_wheel(WHEEL_FRONT_NAME, FRONT_WHEEL_RADIUS,
                                kw.FRONT_WHEEL_WIDTH, True),
            WHEEL_FRONT_NAME, max_tris=820, max_size_m=1.0,
            tread_radius=FRONT_WHEEL_RADIUS)
    kw.emit(lambda: build_wheel(WHEEL_REAR_NAME, REAR_WHEEL_RADIUS,
                                kw.REAR_WHEEL_WIDTH, False),
            WHEEL_REAR_NAME, max_tris=820, max_size_m=1.0,
            tread_radius=REAR_WHEEL_RADIUS)
    kw.emit(build_steering_wheel, STEERING_WHEEL_NAME, max_tris=480, max_size_m=0.5)
