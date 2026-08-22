"""
Piste basher - the snow kart. A plow blade for a front bumper and sled runners at the nose.

    blender --background --factory-startup --python Tools/blender/models/piste_basher.py

The concept's claim is that this is "the only kart in the lineup that reads as a wedge
head-on", and that is the whole design brief: every other style in the set is judged from
the side or the three-quarter, and this one has to win the head-on view specifically. So
the blade is the widest thing on the kart, it is angled rather than upright, and the tub
behind it tapers *in* towards the nose so the blade reads as wider than the machine
pushing it.

Where the numbers come from:

    kartworks.py    wheels, axles, hoop, lamps - shared with every other style
    this file       everything that makes it a snow kart and not a buggy

Two constraints shaped this more than taste did.

**The blade cannot own the lamp positions.** KartBlueprint puts the nose lamps at y 0.47,
z 1.30, and KartLights hangs a real Unity Light on the front face of their glass. A plow
blade tall enough to look like a plow blade sits exactly there and buries them. So the
blade is a low, wide one - 0.10 to 0.40 - and the lamps ride on the cowl just above its
top edge, which is where a real machine puts them anyway and clears the glass by 20 mm.

**Studs are carved inward, never stuck on.** KartSuspension holds the hub exactly `radius`
above the contact point, so a stud modelled proud of the nominal radius drives through the
road. The carcass is drawn under the radius and the studs come back out to meet it.
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

BODY_NAME = "KartPiste_Body"
WHEEL_FRONT_NAME = "KartPiste_WheelFront"
WHEEL_REAR_NAME = "KartPiste_WheelRear"
STEERING_WHEEL_NAME = "KartPiste_SteeringWheel"

# preview_kart.py reads the kart's dimensions off the style module, so a style re-exports
# what it uses. Re-exported rather than duplicated: these are kartworks' values.
FRONT_WHEEL_RADIUS = kw.FRONT_WHEEL_RADIUS
REAR_WHEEL_RADIUS = kw.REAR_WHEEL_RADIUS
FRONT_TRACK = kw.FRONT_TRACK
REAR_TRACK = kw.REAR_TRACK
FRONT_AXLE_Z = kw.FRONT_AXLE_Z
REAR_AXLE_Z = kw.REAR_AXLE_Z
STEERING_HUB = kw.STEERING_HUB
STEERING_RACK = kw.STEERING_RACK

FRAME, BODY, SEAT, RIM, RUBBER, LENS = range(6)

# What each slot carries here. The concept asks for "enamel paint chipped through to bare
# metal", which needs two slots to say: BODY is the enamel and RIM is what shows through,
# so the chips are geometry in the metal slot rather than a texture this pipeline has no
# way to author.
ENAMEL = BODY
STEEL = FRAME       # chassis, blade arms, roll hoop
BARE = RIM          # blade face, runners, chipped patches - galvanised, unpainted
QUILT = SEAT

PALETTE = kw.palette(
    frame=((0.20, 0.21, 0.24), 0.65, 0.55),      # KartFrame  - dark painted steel
    body=((0.79, 0.16, 0.13), 0.05, 0.45),       # KartBody   - enamel red
    seat=((0.16, 0.17, 0.21), 0.00, 0.75),       # KartSeat   - quilted vinyl
    rim=((0.70, 0.72, 0.76), 0.85, 0.35),        # KartRim    - galvanised, scuffed bright
    rubber=((0.07, 0.07, 0.08), 0.00, 0.80),     # KartRubber - winter compound
)

# ---------------------------------------------------------------------------------------
# Layout
# ---------------------------------------------------------------------------------------

FLOOR_TOP_Y = 0.22
TUB_X = 0.40           # outside of the tub side, clear of both tyres' inner faces
TUB_TOP_Y = 0.62
TAIL_Z = -1.00

# The taper. Wide at the cockpit, narrow at the nose, which is what leaves the blade
# standing proud of the bodywork in the head-on view the concept is aimed at.
NOSE_X = 0.24
NOSE_TOP_Y = 0.44
NOSE_Z = 1.16

# Blade. Held below the lamps - see the module docstring.
BLADE_Z = 1.34
BLADE_HALF_WIDTH = 0.72
BLADE_LOW_Y = 0.09
BLADE_HIGH_Y = 0.40
BLADE_RAKE = 0.13     # how far the top edge leans back over the bottom one
BLADE_THICK = 0.035

RUNNER_X = 0.44
RUNNER_Y = 0.085
RUNNER_BACK_Z = 0.92
RUNNER_FRONT_Z = 1.30
RUNNER_TIP_Y = 0.30    # the curl at the front of a sled runner

MAIN_TUBE = 0.040
BRACE_TUBE = 0.030
THIN_TUBE = 0.022


# ---------------------------------------------------------------------------------------
# Chassis
# ---------------------------------------------------------------------------------------

def add_floor(bm):
    """Floor pan. The lowest thing on the kart, so it is what grounds out."""
    tb.cuboid(bm, u(0.0, FLOOR_TOP_Y - 0.025, 0.05), usize(0.80, 0.05, 1.56), STEEL)


def add_tub(bm):
    """The painted monocoque: two tapering sides, a spine and a tail.

    Built as solid sides rather than a tube frame because this is the one style in the set
    with real bodywork - the buggy is the open-frame answer and repeating it here would
    make two karts that read the same at distance.
    """
    for side, _ in mirrored((0.0, 0.0, 0.0)):
        # Cockpit section: full width, full height.
        tb.beam(bm, u(TUB_X * side, 0.42, -0.86), u(TUB_X * side, 0.42, 0.62),
                0.09, 0.40, ENAMEL)
        # Nose section: tapering in and down towards the prow.
        tb.beam(bm, u(TUB_X * side, 0.42, 0.62), u(NOSE_X * side, 0.33, NOSE_Z),
                0.09, 0.34, ENAMEL)
        # Shoulder rail along the top of the cockpit side, so the tub has an edge to it.
        tb.beam(bm, u((TUB_X - 0.01) * side, TUB_TOP_Y, -0.88),
                u((TUB_X - 0.01) * side, TUB_TOP_Y, 0.60), 0.12, 0.07, ENAMEL)

    # Prow cap across the front of the two nose rails.
    tb.cuboid(bm, u(0.0, 0.36, NOSE_Z + 0.03), usize(NOSE_X * 2 + 0.08, 0.30, 0.10), ENAMEL)
    # Deck over the nose, so the front is a closed wedge rather than an open trough.
    tb.slab(bm, u(0.0, NOSE_TOP_Y, NOSE_Z), u(0.0, 0.56, 0.58), 0.50, 0.05, ENAMEL)
    # Tail panel.
    tb.cuboid(bm, u(0.0, 0.44, TAIL_Z), usize(0.86, 0.44, 0.09), ENAMEL)


def add_chips(bm):
    """Bare metal showing through the enamel on the corners that take the hits.

    Small, few, and only on leading edges - the nose cap, the shoulder rail ahead of the
    driver, the tail corners. Chips scattered evenly over the bodywork read as noise; chips
    on the edges that would actually get hit read as wear.
    """
    for side, _ in mirrored((0.0, 0.0, 0.0)):
        tb.cuboid(bm, u(NOSE_X * side, 0.36, NOSE_Z + 0.035),
                  usize(0.09, 0.11, 0.05), BARE)
        tb.cuboid(bm, u((TUB_X - 0.01) * side, TUB_TOP_Y + 0.005, 0.44),
                  usize(0.13, 0.08, 0.16), BARE)
        tb.cuboid(bm, u(0.36 * side, 0.44, TAIL_Z - 0.02),
                  usize(0.12, 0.16, 0.06), BARE)


def add_blade(bm):
    """The plow. A raked steel plate on two arms, with a wear strip along its bottom edge.

    Raked rather than upright for two reasons: an upright plate is a wall in the head-on
    view and kills the wedge the whole style is built on, and a raked one catches the key
    light across its face instead of going flat black in shadow.
    """
    low = u(0.0, BLADE_LOW_Y, BLADE_Z + BLADE_RAKE * 0.5)
    high = u(0.0, BLADE_HIGH_Y, BLADE_Z - BLADE_RAKE * 0.5)

    tb.slab(bm, low, high, BLADE_HALF_WIDTH * 2, BLADE_THICK, BARE)

    # Wear strip: the cutting edge, darker steel and slightly proud below the face.
    tb.cuboid(bm, u(0.0, BLADE_LOW_Y - 0.015, BLADE_Z + BLADE_RAKE * 0.5),
              usize(BLADE_HALF_WIDTH * 2 - 0.04, 0.05, BLADE_THICK + 0.02), STEEL)

    for side, _ in mirrored((0.0, 0.0, 0.0)):
        # Ribs up the *back* of the blade, which is what stops it reading as a flat card.
        # Offset behind the plate rather than centred on it: drawn on the blade's own plane
        # they punched through the front face and read as two dark arrows painted on it.
        tb.slab(bm, u(0.34 * side, BLADE_LOW_Y + 0.03, BLADE_Z + BLADE_RAKE * 0.4 - 0.05),
                u(0.34 * side, BLADE_HIGH_Y - 0.02, BLADE_Z - BLADE_RAKE * 0.4 - 0.05),
                0.05, 0.05, STEEL)
        # Push arms back to the chassis. They run under the nose deck and land on the floor
        # pan rail, clear of the front wheels' sweep.
        #
        # Started behind the plate rather than on it: a tube ending on the blade's own plane
        # puts its end cap through the front face, and a 40 mm cap reads as a dark arrow
        # painted on the blade - which is exactly what the first render showed.
        tb.tube(bm, u(0.30 * side, BLADE_LOW_Y + 0.10, BLADE_Z - 0.07),
                u(0.30 * side, FLOOR_TOP_Y + 0.02, 0.86), MAIN_TUBE, STEEL)
        # Short stub from the arm onto the back of the blade, so the two are visibly joined.
        tb.tube(bm, u(0.30 * side, BLADE_LOW_Y + 0.10, BLADE_Z - 0.07),
                u(0.30 * side, BLADE_LOW_Y + 0.08, BLADE_Z - 0.025), BRACE_TUBE, STEEL)
        # A-frame tie from the arm up to the nose deck.
        tb.tube(bm, u(0.30 * side, BLADE_HIGH_Y - 0.02, BLADE_Z - BLADE_RAKE * 0.5),
                u(0.16 * side, NOSE_TOP_Y + 0.02, NOSE_Z - 0.10), BRACE_TUBE, STEEL)


def add_runners(bm):
    """Short sled runners flanking the nose, curling up at the front.

    Deliberately short. A full-length runner under a kart with 280 mm of suspension travel
    is either dragging or floating depending on load, and neither reads as intentional.
    These are nose skids: they sit ahead of the front axle where the body has no travel to
    speak of, and they say "snow" without pretending to carry the machine.
    """
    for side, _ in mirrored((0.0, 0.0, 0.0)):
        tb.beam(bm, u(RUNNER_X * side, RUNNER_Y, RUNNER_BACK_Z),
                u(RUNNER_X * side, RUNNER_Y, RUNNER_FRONT_Z), 0.07, 0.045, BARE)
        # The curl.
        tb.beam(bm, u(RUNNER_X * side, RUNNER_Y, RUNNER_FRONT_Z),
                u(RUNNER_X * side, RUNNER_TIP_Y, RUNNER_FRONT_Z + 0.12), 0.07, 0.045, BARE)
        # Two stays up into the nose rail.
        for z in (RUNNER_BACK_Z + 0.06, RUNNER_FRONT_Z - 0.10):
            tb.tube(bm, u(RUNNER_X * side, RUNNER_Y + 0.02, z),
                    u((NOSE_X + 0.04) * side, 0.34, z - 0.04), THIN_TUBE, STEEL)


def add_hoop(bm):
    """A single thin hoop at the blueprint's roll hoop, plus its rear stays.

    Thin on purpose. This style's silhouette hook is at the *front*, and a heavy cage
    behind the driver would compete with the blade for it - the concept explicitly gives
    the tall-element slot to the plow, not to the hoop.
    """
    half = kw.ROLL_HOOP_HALF_WIDTH
    top = kw.ROLL_HOOP_TOP_Y
    z = kw.ROLL_HOOP_Z

    for side, _ in mirrored((0.0, 0.0, 0.0)):
        tb.tube(bm, u(half * side, 0.36, z), u(half * side, top, z), MAIN_TUBE, STEEL)
        tb.tube(bm, u(half * side, top - 0.06, z), u((half - 0.06) * side, 0.44, TAIL_Z),
                BRACE_TUBE, STEEL)

    tb.tube(bm, u(-half, top, z), u(half, top, z), MAIN_TUBE, STEEL)
    # Cross tie, low, so the hoop is triangulated rather than a bare staple.
    tb.tube(bm, u(-half, 0.80, z), u(half, 0.80, z), THIN_TUBE, STEEL)


def add_cockpit(bm):
    """Quilted bucket seat, dash cowl and the wraparound screen.

    The quilting is horizontal ribs on the seat back rather than a texture: this pipeline
    has no way to author one, and at the distance a chase camera sits the ribs read as
    padding where a flat panel reads as a board.
    """
    tb.slab(bm, u(0.0, 0.34, -0.20), u(0.0, 0.36, -0.60), 0.46, 0.10, QUILT)
    tb.slab(bm, u(0.0, 0.40, -0.60), u(0.0, 1.00, -0.76), 0.42, 0.10, QUILT)
    tb.cuboid(bm, u(0.0, 1.04, -0.78), usize(0.26, 0.13, 0.10), QUILT)

    # Quilting: ribs across the squab and up the back.
    for z in (-0.28, -0.40, -0.52):
        tb.cuboid(bm, u(0.0, 0.40, z), usize(0.44, 0.03, 0.05), QUILT)
    for y in (0.52, 0.66, 0.80, 0.94):
        tb.cuboid(bm, u(0.0, y, -0.665), usize(0.40, 0.045, 0.03), QUILT)

    for _side, (wing_low, wing_high) in mirrored((0.23, 0.38, -0.28), (0.23, 0.90, -0.70)):
        tb.slab(bm, wing_low, wing_high, 0.07, 0.16, QUILT)

    # Dash cowl and the wind deflector standing off it.
    #
    # The deflector is dark and small on purpose. Cut wide in bare metal it renders as a
    # bright flat card square-on to the chase camera, which is the brightest thing on the
    # kart and sits exactly where the driver should be - it read as a windscreen made of
    # paper. Tinted and narrower, it frames the driver instead of hiding them.
    tb.cuboid(bm, u(0.0, 0.62, 0.40), usize(0.52, 0.10, 0.24), ENAMEL)
    tb.slab(bm, u(0.0, 0.66, 0.36), u(0.0, 0.90, 0.26), 0.38, 0.025, QUILT)
    # Frame around it, so the tint has an edge rather than floating.
    for side, _ in mirrored((0.0, 0.0, 0.0)):
        tb.tube(bm, u(0.19 * side, 0.64, 0.37), u(0.19 * side, 0.91, 0.255), 0.018, BARE,
                segments=4)
    tb.tube(bm, u(-0.19, 0.91, 0.255), u(0.19, 0.91, 0.255), 0.018, BARE, segments=4)

    tb.tube(bm, u(*STEERING_RACK), u(*STEERING_HUB), THIN_TUBE, STEEL)
    tb.tube(bm, u(-0.16, STEERING_RACK[1], STEERING_RACK[2]),
            u(0.16, STEERING_RACK[1], STEERING_RACK[2]), 0.028, STEEL)
    for side, _ in mirrored((0.0, 0.0, 0.0)):
        tb.tube(bm, u(0.06 * side, 0.30, 0.32), u(0.50 * side, 0.28, 0.76), 0.016, STEEL)
        tb.slab(bm, u(0.17 * side, 0.24, 0.68), u(0.17 * side, 0.36, 0.64), 0.10, 0.03, BARE)


def add_drivetrain(bm):
    """Engine bay behind the seat and a stubby side-exit exhaust."""
    tb.cuboid(bm, u(0.0, 0.54, -0.92), usize(0.52, 0.36, 0.34), STEEL)
    tb.cuboid(bm, u(0.0, 0.74, -0.90), usize(0.34, 0.08, 0.24), ENAMEL)
    tb.tube(bm, u(0.22, 0.50, -0.94), u(0.40, 0.44, -1.04), 0.032, BARE)
    tb.tube(bm, u(-0.60, REAR_WHEEL_RADIUS, REAR_AXLE_Z),
            u(0.60, REAR_WHEEL_RADIUS, REAR_AXLE_Z), 0.038, STEEL)


def add_suspension(bm):
    """Coil-overs at all four corners, on the shared geometry."""
    for side, _ in mirrored((0.0, 0.0, 0.0)):
        tb.tube(bm, u(0.28 * side, FLOOR_TOP_Y, FRONT_AXLE_Z),
                u(0.52 * side, FRONT_WHEEL_RADIUS, FRONT_AXLE_Z), BRACE_TUBE, STEEL)
        kw.coilover(bm, u(0.30 * side, 0.32, 0.70), u(0.52 * side, 0.74, 0.62), STEEL, BARE)

        tb.tube(bm, u(0.30 * side, 0.30, -0.48),
                u(0.56 * side, REAR_WHEEL_RADIUS, REAR_AXLE_Z), 0.032, STEEL)
        kw.coilover(bm, u(0.32 * side, 0.36, -0.78), u(0.54 * side, 0.88, -0.70), STEEL, BARE)


def add_mirror(bm):
    """One stalk mirror, driver's side, clear of the front tyre at full lock."""
    tb.tube(bm, u(0.38, 0.86, 0.30), u(0.50, 0.94, 0.36), 0.014, STEEL, segments=4)
    tb.cuboid(bm, u(0.53, 0.95, 0.37), usize(0.03, 0.10, 0.13), BARE)


def build_body():
    bm = bmesh.new()
    add_floor(bm)
    add_tub(bm)
    add_chips(bm)
    add_blade(bm)
    add_runners(bm)
    add_hoop(bm)
    add_cockpit(bm)
    add_drivetrain(bm)
    add_suspension(bm)
    add_mirror(bm)
    kw.lamps(bm, BARE, nose=True, roof=False)
    return kw.finish(bm, BODY_NAME, PALETTE)


# ---------------------------------------------------------------------------------------
# Wheels
# ---------------------------------------------------------------------------------------

def build_wheel(name, radius, width, blocks=10):
    """A studded winter tyre: chevron blocks with a metal stud proud of each shoulder.

    The chevron is two slabs meeting at the centreline rather than one angled block,
    because a single angled block reads as a smear at speed while a V catches the light
    on one face and not the other, which is what makes the tread legible when the wheel
    is turning.
    """
    bm = bmesh.new()
    kw.wheel_carcass(bm, radius, width, RUBBER, RIM, carcass=0.88, spokes=4)

    # The chevron sweeps back this far from apex to shoulder. A fraction of the block
    # pitch rather than a fixed angle, so changing `blocks` keeps the V's proportions.
    sweep = (2.0 * math.pi / blocks) * 0.42
    block_height = radius * 0.09

    for i, theta, _direction in kw.around(blocks, 1.0):
        # The two halves of the chevron: each runs from the centreline out to a shoulder,
        # falling back in theta as it goes, so they meet in a V pointing the way the wheel
        # turns. Blocks come from kw.tread_block, which holds the outer face on the radius.
        for sign in (-1, 1):
            kw.tread_block(bm, radius * 0.97, theta, theta - sweep,
                           width * 0.03 * sign, width * 0.36 * sign,
                           block_height, radius * 0.17, RUBBER)

        # Studs, standing proud of the blocks and peaking at exactly the nominal radius.
        # Purely radial, so the tube's own girth goes tangentially and cannot leak outward.
        if i % 2 == 0:
            for sign in (-1, 1):
                base = Vector((0.0, math.sin(theta), math.cos(theta))) * (radius * 0.86)
                tip = Vector((0.0, math.sin(theta), math.cos(theta))) * radius
                tb.tube(bm, u(width * 0.22 * sign, base.y, base.z),
                        u(width * 0.22 * sign, tip.y, tip.z), radius * 0.05, RIM,
                        segments=4)

    return kw.finish(bm, name, PALETTE)


def build_steering_wheel(name=STEERING_WHEEL_NAME):
    """A thick-rimmed wheel with a grab handle - gloves, not fingertips.

    Authored in the "Steering" pivot's local space: rim in local XZ, column up local Y.
    KartBlueprint spins that pivot about its own Y, so author it in any other frame and it
    turns like a tabletop.
    """
    bm = bmesh.new()
    radius = kw.STEERING_WHEEL_RADIUS
    segments = kw.STEERING_RIM_SEGMENTS

    ring = [(radius * math.cos(2.0 * math.pi * i / segments), 0.0,
             radius * math.sin(2.0 * math.pi * i / segments)) for i in range(segments)]
    for i, point in enumerate(ring):
        tb.tube(bm, u(*point), u(*ring[(i + 1) % segments]), 0.026, QUILT, segments=5)

    tb.tube(bm, u(0.0, -0.024, 0.0), u(0.0, 0.026, 0.0), 0.055, BARE, segments=8)

    for i in range(3):
        phi = math.radians(90.0 + i * 120.0)
        spoke = (radius * math.cos(phi), 0.0, radius * math.sin(phi))
        tb.slab(bm, u(0.0, 0.0, 0.0), u(*spoke), 0.040, 0.020, BARE)

    # Spinner knob, offset off the rim - the winter-machine tell, and it gives the wheel a
    # visible straight-ahead from the chase camera.
    knob = (radius * math.cos(math.radians(210.0)), 0.0, radius * math.sin(math.radians(210.0)))
    tb.tube(bm, u(knob[0], -0.01, knob[2]), u(knob[0], 0.055, knob[2]), 0.030, BARE,
            segments=6)

    return kw.finish(bm, name, PALETTE)


if __name__ == "__main__":
    kw.check_against_blueprint("piste_basher.py")
    kw.write_manifest(
        "Piste", PALETTE, nose_lamps=True, roof_bar=False,
        meshes=[BODY_NAME, WHEEL_FRONT_NAME, WHEEL_REAR_NAME, STEERING_WHEEL_NAME],
    )

    kw.emit(build_body, BODY_NAME, max_tris=2900, max_size_m=3.0)
    kw.emit(lambda: build_wheel(WHEEL_FRONT_NAME, FRONT_WHEEL_RADIUS, kw.FRONT_WHEEL_WIDTH),
            WHEEL_FRONT_NAME, max_tris=760, max_size_m=1.0,
            tread_radius=FRONT_WHEEL_RADIUS)
    kw.emit(lambda: build_wheel(WHEEL_REAR_NAME, REAR_WHEEL_RADIUS, kw.REAR_WHEEL_WIDTH),
            WHEEL_REAR_NAME, max_tris=760, max_size_m=1.0,
            tread_radius=REAR_WHEEL_RADIUS)
    kw.emit(build_steering_wheel, STEERING_WHEEL_NAME, max_tris=460, max_size_m=0.5)
