"""
Pit rat - the scrapyard kart. Mismatched panels, bare engine, and one wheel that is wrong.

    blender --background --factory-startup --python Tools/blender/models/pit_rat.py

"Cheapest to build, because wrong is the aesthetic" is the concept's own line, and it is
worth taking literally: this style is the one place in the set where a panel at the wrong
angle, a colour that does not match and a bolt-on that overhangs its bracket are all
*correct*. Everything here is deliberately off by a few degrees or a few centimetres.

Two things that being scrap does not excuse.

**The mismatch has to be authored, not random.** A style built by jittering every number
reads as a modelling error rather than as a scrapyard kart - and worse, it cannot be edited
afterwards, because nothing in it means anything. So each panel here is placed at a stated
lean with a stated colour, and the ones that clash do so on purpose.

**One wheel obviously not matching is a per-*axle* trick, not per-corner.** `KartStyle`
names one front mesh and one rear mesh, and `KartSetup` hangs the same front mesh on both
front corners - there is no way to make a single corner odd without a fifth asset and a C#
change to place it. So the mismatch runs front-to-rear instead: the fronts keep a battered
hubcap, the rears are bare rims with the nuts showing. Read from any angle that sees both
axles, which is every angle the race camera uses.
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

BODY_NAME = "KartPitRat_Body"
WHEEL_FRONT_NAME = "KartPitRat_WheelFront"
WHEEL_REAR_NAME = "KartPitRat_WheelRear"
STEERING_WHEEL_NAME = "KartPitRat_SteeringWheel"

FRONT_WHEEL_RADIUS = kw.FRONT_WHEEL_RADIUS
REAR_WHEEL_RADIUS = kw.REAR_WHEEL_RADIUS
FRONT_TRACK = kw.FRONT_TRACK
REAR_TRACK = kw.REAR_TRACK
FRONT_AXLE_Z = kw.FRONT_AXLE_Z
REAR_AXLE_Z = kw.REAR_AXLE_Z
STEERING_HUB = kw.STEERING_HUB
STEERING_RACK = kw.STEERING_RACK

FRAME, BODY, SEAT, RIM, RUBBER, LENS = range(6)

RUST = FRAME        # the frame and everything unpainted: rusted bare steel
BLUE = BODY         # the one panel colour that turns up more than once
VINYL = SEAT        # split tan seat vinyl, and the jerry can strap
BARE = RIM          # galvanised patches, hose clips, the odd new part
TYRE = RUBBER

PALETTE = kw.palette(
    frame=((0.36, 0.22, 0.15), 0.25, 0.85),      # KartFrame  - rust
    body=((0.22, 0.38, 0.52), 0.05, 0.70),       # KartBody   - faded works blue
    seat=((0.56, 0.44, 0.28), 0.00, 0.85),       # KartSeat   - split tan vinyl
    rim=((0.60, 0.60, 0.58), 0.45, 0.55),        # KartRim    - bare galvanised
    rubber=((0.08, 0.08, 0.09), 0.00, 0.82),     # KartRubber - bald and mismatched
)

FLOOR_TOP_Y = 0.22
RAIL_X = 0.40
RAIL_Y = 0.26
TAIL_Z = -1.00
NOSE_Z = 1.18

MAIN_TUBE = 0.040
BRACE_TUBE = 0.030
THIN_TUBE = 0.022


# ---------------------------------------------------------------------------------------
# Frame
# ---------------------------------------------------------------------------------------

def add_frame(bm):
    """A scrap tube frame, welded up out of whatever was long enough.

    Rails at slightly different heights left and right - 15 mm - which is under the
    threshold where it reads as a mistake and over the one where the kart looks
    factory-built.
    """
    tb.cuboid(bm, u(0.0, FLOOR_TOP_Y - 0.025, 0.05), usize(0.76, 0.05, 1.50), RUST)

    for side, _ in mirrored((0.0, 0.0, 0.0)):
        lean = 0.015 * side
        tb.tube(bm, u(RAIL_X * side, RAIL_Y + lean, 0.88),
                u(RAIL_X * side, RAIL_Y + lean, TAIL_Z), MAIN_TUBE, RUST)
        tb.tube(bm, u(RAIL_X * side, RAIL_Y + lean, 0.88),
                u(0.30 * side, 0.40, NOSE_Z), MAIN_TUBE, RUST)
        tb.tube(bm, u(RAIL_X * side, RAIL_Y + lean, TAIL_Z),
                u(0.44 * side, 0.46, TAIL_Z), BRACE_TUBE, RUST)

    for z in (0.86, 0.10, -0.56, TAIL_Z):
        tb.tube(bm, u(-RAIL_X, RAIL_Y, z), u(RAIL_X, RAIL_Y, z), BRACE_TUBE, RUST)
    tb.tube(bm, u(-0.30, 0.40, NOSE_Z), u(0.30, 0.40, NOSE_Z), MAIN_TUBE, RUST)

    # A brace that was clearly added later, at an angle that fits nothing.
    tb.tube(bm, u(-0.38, 0.28, 0.30), u(0.34, 0.58, -0.42), THIN_TUBE, BARE)


def add_hoop(bm):
    """A bent hoop with a splint welded over the bend.

    Leans back 4 degrees from the blueprint's upright and the splice plate sits proud of
    the tube - a repaired hoop rather than a made one. The top still lands on
    ROLL_HOOP_TOP_Y, because that is the point of the hoop and the one thing scrap does
    not get to change.
    """
    half = kw.ROLL_HOOP_HALF_WIDTH
    top = kw.ROLL_HOOP_TOP_Y
    z = kw.ROLL_HOOP_Z

    for side, _ in mirrored((0.0, 0.0, 0.0)):
        foot = u(half * side, 0.30, z + 0.06)
        head = u((half - 0.02) * side, top, z - 0.04)
        tb.tube(bm, foot, head, MAIN_TUBE, RUST)
        # Splice plate over the middle, bolted.
        joint = foot.lerp(head, 0.55)
        tb.tube(bm, foot.lerp(head, 0.44), foot.lerp(head, 0.66), MAIN_TUBE + 0.016, BARE)
        tb.cuboid(bm, joint, usize(0.11, 0.05, 0.05), BARE)
        tb.tube(bm, head, u((half - 0.08) * side, 0.44, TAIL_Z), BRACE_TUBE, RUST)

    tb.tube(bm, u(-half + 0.02, top, z - 0.04), u(half - 0.02, top, z - 0.04),
            MAIN_TUBE, RUST)


def add_panels(bm):
    """The scrap bodywork: four panels, three colours, none of them square.

    Each is a slab given an explicit lean. The blue pair are off the same donor and match;
    the galvanised one obviously is not, and the tan one is a seat-vinyl offcut riveted
    over a hole. Rivets are drawn along the edges that would need them and nowhere else.
    """
    specs = (
        # (side, z_low, z_high, y_low, y_high, x, skin, half_height)
        (-1, -0.62, 0.10, 0.30, 0.44, RAIL_X + 0.02, BLUE, 0.17),
        (1, -0.50, 0.24, 0.28, 0.40, RAIL_X + 0.02, BLUE, 0.16),
        (1, 0.26, 0.86, 0.34, 0.30, RAIL_X + 0.01, BARE, 0.13),
        (-1, 0.16, 0.80, 0.32, 0.42, RAIL_X + 0.03, VINYL, 0.12),
    )
    for side, z_a, z_b, y_a, y_b, x, skin, half_h in specs:
        a = u(x * side, y_a, z_a)
        b = u(x * side, y_b, z_b)
        # 22 mm across, `half_h * 2` tall. tb.beam measures `h` along `up` and `w` across
        # both, so passing these the other way round laid every panel down flat: the kart
        # wore four horizontal shelves instead of four bolted-on side panels.
        tb.beam(bm, a, b, 0.022, half_h * 2, skin, up=u(0.0, 1.0, 0.0))
        # Rivets down the top edge, spaced by eye rather than evenly.
        run = (b - a)
        for t in (0.12, 0.34, 0.52, 0.78, 0.93):
            point = a + run * t
            tb.tube(bm, point + u(0.0, half_h - 0.02, 0.0),
                    point + u(0.012 * side, half_h - 0.02, 0.0), 0.014, BARE, segments=4)

    # Nose panel, bent and short of covering the frame.
    tb.beam(bm, u(-0.02, 0.44, 0.92), u(0.06, 0.36, NOSE_Z), 0.46, 0.025, BLUE,
            up=u(0.0, 1.0, 0.0))


def add_engine(bm):
    """A bare engine with no cover, which is most of the read from behind.

    Exposed on purpose and detailed accordingly: block, head, cam cover, a carb with a
    filter, headers going nowhere tidy, and a radiator hose with two clips.
    """
    tb.cuboid(bm, u(0.0, 0.50, -0.88), usize(0.42, 0.32, 0.32), RUST)
    tb.cuboid(bm, u(0.0, 0.68, -0.88), usize(0.36, 0.10, 0.28), BARE)
    tb.cuboid(bm, u(0.0, 0.76, -0.88), usize(0.30, 0.07, 0.24), BLUE)

    # Carb and air filter, standing up out of the vee.
    tb.tube(bm, u(0.0, 0.80, -0.80), u(0.0, 0.90, -0.78), 0.045, BARE, segments=6)
    tb.tube(bm, u(0.0, 0.90, -0.78), u(0.0, 0.96, -0.78), 0.085, RUST, segments=8)

    # Headers, four of them, at four slightly different angles.
    for i, (x, lean) in enumerate(((-0.20, -0.06), (-0.08, -0.02), (0.08, 0.03),
                                   (0.20, 0.07))):
        tb.tube(bm, u(x, 0.52, -0.74), u(x + lean, 0.66, -0.62), 0.026, BARE)
        tb.tube(bm, u(x + lean, 0.66, -0.62), u(x + lean * 1.6, 0.60, -0.50), 0.026, BARE)
    # They collect into one pipe that exits low on the driver's right.
    tb.tube(bm, u(0.06, 0.58, -0.48), u(0.34, 0.40, -0.40), 0.040, RUST)
    tb.tube(bm, u(0.34, 0.40, -0.40), u(0.40, 0.36, -0.96), 0.040, RUST)
    tb.tube(bm, u(0.40, 0.36, -0.96), u(0.40, 0.36, -1.06), 0.052, BARE, segments=6)

    # Radiator hose with clips - a soft part among hard ones.
    tb.tube(bm, u(-0.12, 0.66, -0.70), u(-0.24, 0.60, -0.36), 0.030, RUST, segments=6)
    for t in (0.15, 0.85):
        point = u(-0.12, 0.66, -0.70).lerp(u(-0.24, 0.60, -0.36), t)
        tb.tube(bm, point, point + Vector((0.02, 0.0, 0.01)), 0.036, BARE, segments=6)

    tb.tube(bm, u(-0.60, REAR_WHEEL_RADIUS, REAR_AXLE_Z),
            u(0.60, REAR_WHEEL_RADIUS, REAR_AXLE_Z), 0.040, RUST)


def add_jerry_can(bm):
    """The jerry can where a side pod should be, strapped on and slightly crooked."""
    centre = u(0.46, 0.50, 0.16)
    tb.cuboid(bm, centre, usize(0.17, 0.34, 0.44), BLUE)
    # The pressed X on the face.
    tb.slab(bm, u(0.55, 0.38, 0.00), u(0.55, 0.62, 0.32), 0.02, 0.03, RUST)
    tb.slab(bm, u(0.55, 0.62, 0.00), u(0.55, 0.38, 0.32), 0.02, 0.03, RUST)
    # Cap and handle.
    tb.tube(bm, u(0.46, 0.68, 0.30), u(0.46, 0.72, 0.30), 0.035, RUST, segments=6)
    tb.tube(bm, u(0.40, 0.70, 0.10), u(0.52, 0.70, 0.10), 0.016, RUST, segments=4)
    # Two straps holding it to the rail.
    for z in (0.02, 0.30):
        tb.beam(bm, u(0.36, 0.50, z), u(0.56, 0.50, z), 0.05, 0.022, VINYL,
                up=u(0.0, 1.0, 0.0))


def add_cockpit(bm):
    """A split vinyl seat with the foam showing, and a mismatched control set."""
    tb.slab(bm, u(0.0, 0.34, -0.18), u(0.0, 0.36, -0.58), 0.44, 0.10, VINYL)
    tb.slab(bm, u(0.0, 0.40, -0.58), u(0.0, 0.98, -0.74), 0.40, 0.10, VINYL)
    # The split, with foam behind it.
    tb.cuboid(bm, u(0.06, 0.70, -0.66), usize(0.04, 0.26, 0.05), BARE)
    tb.cuboid(bm, u(0.0, 1.02, -0.76), usize(0.22, 0.12, 0.09), RUST)

    tb.cuboid(bm, u(0.0, 0.56, 0.42), usize(0.42, 0.09, 0.20), BARE)
    # One gauge, hanging off the dash by its wire.
    tb.tube(bm, u(-0.14, 0.62, 0.40), u(-0.14, 0.66, 0.40), 0.045, RUST, segments=6)
    tb.tube(bm, u(-0.14, 0.58, 0.40), u(-0.16, 0.46, 0.36), 0.010, RUST, segments=4)

    tb.tube(bm, u(*STEERING_RACK), u(*STEERING_HUB), THIN_TUBE, RUST)
    tb.tube(bm, u(-0.16, STEERING_RACK[1], STEERING_RACK[2]),
            u(0.16, STEERING_RACK[1], STEERING_RACK[2]), 0.028, RUST)
    for side, _ in mirrored((0.0, 0.0, 0.0)):
        tb.tube(bm, u(0.06 * side, 0.30, 0.32), u(0.50 * side, 0.28, 0.76), 0.016, RUST)
        tb.slab(bm, u(0.17 * side, 0.24, 0.68), u(0.17 * side, 0.36, 0.64), 0.10, 0.03, BARE)

    # Gear lever: a bent rod with a door knob on it.
    tb.tube(bm, u(0.28, 0.36, -0.16), u(0.24, 0.62, -0.24), 0.016, RUST)
    tb.tube(bm, u(0.24, 0.62, -0.24), u(0.24, 0.68, -0.24), 0.038, VINYL, segments=6)


def add_suspension(bm):
    """Three of the four corners match."""
    for side, _ in mirrored((0.0, 0.0, 0.0)):
        tb.tube(bm, u(0.28 * side, FLOOR_TOP_Y, FRONT_AXLE_Z),
                u(0.52 * side, FRONT_WHEEL_RADIUS, FRONT_AXLE_Z), BRACE_TUBE, RUST)
        kw.coilover(bm, u(0.30 * side, 0.32, 0.70), u(0.52 * side, 0.74, 0.62), RUST, BARE)
        tb.tube(bm, u(0.30 * side, 0.30, -0.48),
                u(0.56 * side, REAR_WHEEL_RADIUS, REAR_AXLE_Z), 0.032, RUST)

    # The rear left shock is a different, longer unit with a bare spring. One corner, and
    # only one, so it reads as a replacement rather than as a design.
    kw.coilover(bm, u(-0.32, 0.34, -0.78), u(-0.56, 0.96, -0.68), BARE, BARE,
                rod=0.022, spring=0.062)
    kw.coilover(bm, u(0.32, 0.36, -0.78), u(0.56, 0.90, -0.70), RUST, BARE)


def build_body():
    bm = bmesh.new()
    add_frame(bm)
    add_hoop(bm)
    add_panels(bm)
    add_engine(bm)
    add_jerry_can(bm)
    add_cockpit(bm)
    add_suspension(bm)
    kw.lamps(bm, BARE, nose=True, roof=False)
    return kw.finish(bm, BODY_NAME, PALETTE)


# ---------------------------------------------------------------------------------------
# Wheels
# ---------------------------------------------------------------------------------------

def build_wheel(name, radius, width, capped):
    """A bald mismatched tyre. `capped` puts a battered hubcap on it; the other axle has
    none, which is how the "one wheel obviously not matching" read is delivered - see the
    module docstring for why it cannot be per-corner."""
    bm = bmesh.new()
    kw.wheel_carcass(bm, radius, width, TYRE, BARE, carcass=0.90, rim=0.54, hub=0.20,
                     spokes=0)

    if capped:
        # Hubcap: a shallow dish, dented on one side so it is not a clean disc.
        tb.tube(bm, u(width * 0.30, 0, 0), u(width * 0.40, 0, 0), radius * 0.56, BARE,
                segments=10)
        tb.tube(bm, u(width * 0.40, 0, 0), u(width * 0.44, 0, 0), radius * 0.30, BARE,
                segments=8)
        dent = Vector((0.0, math.sin(1.1), math.cos(1.1))) * (radius * 0.40)
        tb.cuboid(bm, u(width * 0.34, dent.y, dent.z), usize(0.03, radius * 0.24,
                                                             radius * 0.20), TYRE)
    else:
        # Bare rim: the nuts showing, and one of them missing.
        tb.tube(bm, u(width * 0.26, 0, 0), u(width * 0.34, 0, 0), radius * 0.34, BARE,
                segments=8)
        for i, _theta, nut in kw.around(5, radius * 0.24):
            if i == 3:
                continue
            tb.tube(bm, u(width * 0.34, nut.y, nut.z), u(width * 0.40, nut.y, nut.z),
                    radius * 0.055, RUST, segments=6)

    # Worn-out tread: shallow blocks, and a bald quarter with none at all.
    blocks = 9
    sweep = (2.0 * math.pi / blocks) * 0.5
    for i, theta, _direction in kw.around(blocks, 1.0):
        if i in (2, 3):
            continue
        height = radius * (0.05 if i % 2 else 0.035)
        kw.tread_block(bm, radius, theta - sweep * 0.5, theta + sweep * 0.5,
                       -width * 0.32, width * 0.32, height, radius * 0.22, TYRE)

    return kw.finish(bm, name, PALETTE)


def build_steering_wheel(name=STEERING_WHEEL_NAME):
    """A rim with the covering worn off one side and tape wrapped round the other."""
    bm = bmesh.new()
    radius = kw.STEERING_WHEEL_RADIUS
    segments = kw.STEERING_RIM_SEGMENTS

    ring = [(radius * math.cos(2.0 * math.pi * i / segments), 0.0,
             radius * math.sin(2.0 * math.pi * i / segments)) for i in range(segments)]
    for i, point in enumerate(ring):
        nxt = ring[(i + 1) % segments]
        # Taped stretch, worn stretch, bare stretch.
        skin = VINYL if i < 4 else (RUST if i < 7 else BARE)
        tb.tube(bm, u(*point), u(*nxt), 0.024 if i < 4 else 0.019, skin, segments=5)

    tb.tube(bm, u(0.0, -0.022, 0.0), u(0.0, 0.024, 0.0), 0.048, RUST, segments=8)

    # Two spokes, not three. The third one broke off and the stub is still there.
    for phi_deg in (90.0, 210.0):
        phi = math.radians(phi_deg)
        spoke = (radius * math.cos(phi), 0.0, radius * math.sin(phi))
        tb.slab(bm, u(0.0, 0.0, 0.0), u(*spoke), 0.030, 0.016, BARE)
    stub = (radius * 0.34 * math.cos(math.radians(330.0)), 0.0,
            radius * 0.34 * math.sin(math.radians(330.0)))
    tb.slab(bm, u(0.0, 0.0, 0.0), u(*stub), 0.028, 0.015, RUST)

    return kw.finish(bm, name, PALETTE)


if __name__ == "__main__":
    kw.check_against_blueprint("pit_rat.py")
    kw.write_manifest(
        "PitRat", PALETTE, nose_lamps=True, roof_bar=False,
        meshes=[BODY_NAME, WHEEL_FRONT_NAME, WHEEL_REAR_NAME, STEERING_WHEEL_NAME],
    )

    kw.emit(build_body, BODY_NAME, max_tris=3600, max_size_m=3.0)
    kw.emit(lambda: build_wheel(WHEEL_FRONT_NAME, FRONT_WHEEL_RADIUS,
                                kw.FRONT_WHEEL_WIDTH, True),
            WHEEL_FRONT_NAME, max_tris=880, max_size_m=1.0,
            tread_radius=FRONT_WHEEL_RADIUS)
    kw.emit(lambda: build_wheel(WHEEL_REAR_NAME, REAR_WHEEL_RADIUS,
                                kw.REAR_WHEEL_WIDTH, False),
            WHEEL_REAR_NAME, max_tris=880, max_size_m=1.0,
            tread_radius=REAR_WHEEL_RADIUS)
    kw.emit(build_steering_wheel, STEERING_WHEEL_NAME, max_tris=500, max_size_m=0.5)
