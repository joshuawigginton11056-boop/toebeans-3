"""
Cinder hauler - the lava kart. Basalt crust that glows along the fissures.

    blender --background --factory-startup --python Tools/blender/models/cinder_hauler.py

The concept asks for the tallest and most jagged silhouette in the set, and gives it two
things to do that with: a roll hoop that splits into outward-curving horns, and an exhaust
that becomes twin chimney stacks. Both are here and nothing else is allowed above the seat
back, because a jagged skyline stops being a silhouette the moment it has four competing
peaks in it.

**The fissures are on the KartLens slot, and that is deliberate.** The concept says the
body "wants an emissive channel for the fissures", and this pipeline has exactly one slot
whose Unity material is allowed to glow - the lamp glass. `Assets\\Farm`'s cabins already
use their slot 4 the same way ("the part that glows": CabinGlass on a standing cabin,
BurntEmber on the ruin), so this is the established answer rather than a new mechanism.

The consequence is a hard constraint: **this style must never set `headlights`.** KartLights
switches the lamps on by swapping the material on every KartLens face, so a cinder hauler
with headlights would flare its entire body crust on the L key. It has no lamp housings and
wants none - a kart made of hot rock does not need headlights, which is what makes the slot
free to be used for this.

The tyres are heat-cracked slabs rather than lugs: few, wide, deep-grooved, with the fissure
material showing in the grooves so the wheels glow too. Same radius discipline as everything
else - the slabs come from `kartworks.tread_block` and peak on the nominal radius.
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

BODY_NAME = "KartCinder_Body"
WHEEL_FRONT_NAME = "KartCinder_WheelFront"
WHEEL_REAR_NAME = "KartCinder_WheelRear"
STEERING_WHEEL_NAME = "KartCinder_SteeringWheel"

FRONT_WHEEL_RADIUS = kw.FRONT_WHEEL_RADIUS
REAR_WHEEL_RADIUS = kw.REAR_WHEEL_RADIUS
FRONT_TRACK = kw.FRONT_TRACK
REAR_TRACK = kw.REAR_TRACK
FRONT_AXLE_Z = kw.FRONT_AXLE_Z
REAR_AXLE_Z = kw.REAR_AXLE_Z
STEERING_HUB = kw.STEERING_HUB
STEERING_RACK = kw.STEERING_RACK

FRAME, BODY, SEAT, RIM, RUBBER, LENS = range(6)

BLUED = FRAME       # heat-blued steel: the frame, links, horn cores
CRUST = BODY        # volcanic crust - the shell
CHAR = SEAT         # charred hide seat
IRON = RIM          # dull iron: stacks, hubs, hardware
SLAB = RUBBER       # heat-cracked tyre
FISSURE = LENS      # the glow. See the module docstring before using this slot.

PALETTE = kw.palette(
    frame=((0.16, 0.15, 0.18), 0.55, 0.50),      # KartFrame  - heat-blued steel
    body=((0.15, 0.13, 0.13), 0.05, 0.85),       # KartBody   - basalt crust
    seat=((0.11, 0.09, 0.09), 0.00, 0.85),       # KartSeat   - charred hide
    rim=((0.27, 0.25, 0.25), 0.45, 0.60),        # KartRim    - dull iron
    rubber=((0.09, 0.08, 0.08), 0.00, 0.90),     # KartRubber - cracked slab
    # The fissures. Painted hot here so the Blender preview shows them; Unity gives this
    # slot a real emissive material through the style's palette.
    lens=((1.00, 0.42, 0.06), 0.00, 0.40),       # KartLens   - molten
)

# ---------------------------------------------------------------------------------------
# Layout
# ---------------------------------------------------------------------------------------

FLOOR_TOP_Y = 0.22
SHELL_X = 0.44
SHELL_TOP_Y = 0.66
NOSE_Z = 1.26
TAIL_Z = -1.00

HORN_Z = kw.ROLL_HOOP_Z
HORN_ROOT_Y = 0.52
HORN_TIP_Y = 1.52          # above the nominal hoop: the horns are the tall element
HORN_TIP_X = 0.74          # curving outward, which is what makes them read as horns

STACK_X = 0.26
STACK_Z = -0.94
STACK_TOP_Y = 1.34

MAIN_TUBE = 0.044
BRACE_TUBE = 0.032
THIN_TUBE = 0.024

FISSURE_T = 0.028          # how proud a glowing seam sits in its crust


# ---------------------------------------------------------------------------------------
# Shell
# ---------------------------------------------------------------------------------------

def add_floor(bm):
    tb.cuboid(bm, u(0.0, FLOOR_TOP_Y - 0.025, 0.05), usize(0.80, 0.05, 1.52), BLUED)


def add_shell(bm):
    """The basalt crust: slabs at broken angles rather than one smooth body.

    Each flank is three plates that do not quite line up, because a cooled crust is
    fractured and a single tapering side would read as sheet metal painted black. The
    misalignment is small - 20 to 40 mm - and it is the whole texture of the style.
    """
    for side, _ in mirrored((0.0, 0.0, 0.0)):
        for z_a, z_b, y_a, y_b, x_a, x_b in (
                (TAIL_Z, -0.30, 0.30, 0.62, SHELL_X, SHELL_X - 0.02),
                (-0.34, 0.44, 0.32, 0.66, SHELL_X - 0.04, SHELL_X),
                (0.40, NOSE_Z, 0.34, 0.44, SHELL_X - 0.02, 0.26)):
            # `up` is vertical, not lateral. tb.beam measures `h` along `up` and `w` across
            # both - so with a lateral `up` these plates came out 340 mm thick sideways and
            # 70 mm tall: horizontal shelves sticking out of the kart rather than a flank.
            tb.beam(bm, u(x_a * side, y_a, z_a), u(x_b * side, y_b, z_b), 0.07, 0.34,
                    CRUST, up=u(0.0, 1.0, 0.0))

    # Deck plates over the nose and the tail, again at slightly different heights.
    tb.slab(bm, u(0.0, 0.50, NOSE_Z - 0.06), u(0.0, 0.58, 0.46), 0.50, 0.06, CRUST)
    tb.cuboid(bm, u(0.0, 0.40, NOSE_Z), usize(0.54, 0.30, 0.10), CRUST)
    tb.cuboid(bm, u(0.0, 0.46, TAIL_Z + 0.02), usize(0.78, 0.36, 0.10), CRUST)

    # Broken shoulders: chunks standing off the top edge, uneven on purpose.
    for side, _ in mirrored((0.0, 0.0, 0.0)):
        for z, height in ((-0.62, 0.10), (-0.18, 0.07), (0.22, 0.09), (0.60, 0.06)):
            tb.cuboid(bm, u((SHELL_X - 0.03) * side, SHELL_TOP_Y + height * 0.5, z),
                      usize(0.10, height, 0.16), CRUST)


def add_fissures(bm):
    """The glow. Seams of molten rock running through the crust.

    Drawn as thin bars standing just proud of the shell rather than inset into it: this
    pipeline has no booleans, and a seam sunk into a plate would need one. Proud also
    renders better - an emissive surface facing outward catches the camera from the low
    angle this game uses, where a recessed one is edge-on and dark.
    """
    for side, _ in mirrored((0.0, 0.0, 0.0)):
        # Long seams down each flank, kinked at the plate joins.
        for z_a, z_b, y_a, y_b in ((-0.92, -0.36, 0.36, 0.50),
                                   (-0.30, 0.30, 0.54, 0.44),
                                   (0.36, 0.98, 0.40, 0.36)):
            # w is how far the seam stands out of the flank, h how tall it is - not the
            # other way round. Reversed, a 28 mm crack became a 28 mm-tall bar standing
            # 50 mm out of the bodywork.
            tb.beam(bm, u((SHELL_X + 0.035) * side, y_a, z_a),
                    u((SHELL_X + 0.035) * side, y_b, z_b), FISSURE_T, 0.05, FISSURE,
                    up=u(0.0, 1.0, 0.0))
        # Short cross-cracks off them.
        for z, y in ((-0.66, 0.42), (0.06, 0.50), (0.66, 0.38)):
            tb.beam(bm, u((SHELL_X + 0.035) * side, y - 0.09, z),
                    u((SHELL_X + 0.035) * side, y + 0.09, z + 0.10), FISSURE_T, 0.035,
                    FISSURE, up=u(0.0, 1.0, 0.0))

    # A seam up the centre of the nose deck and one across the tail.
    tb.beam(bm, u(0.0, 0.61, 0.44), u(0.0, 0.53, NOSE_Z - 0.08), 0.06, FISSURE_T, FISSURE,
            up=u(0.0, 1.0, 0.0))
    tb.cuboid(bm, u(0.0, 0.58, TAIL_Z + 0.02), usize(0.52, 0.05, 0.06), FISSURE)
    # The glow inside the nose intake. Three slots rather than one 0.34 x 0.12 panel: at
    # that size it stopped being a crack in rock and became a lit sign bolted to the nose.
    for x in (-0.11, 0.0, 0.11):
        tb.cuboid(bm, u(x, 0.40, NOSE_Z + 0.045), usize(0.06, 0.11, 0.03), FISSURE)


def add_horns(bm):
    """The hoop, split into two outward-curving horns.

    Built as three straight segments per horn with increasing outward lean rather than a
    real curve: at this triangle budget a swept tube costs more than the shape is worth,
    and three facets with a rising angle read as a curve from any distance the game shows.
    The horns carry a blued-steel core with the crust broken away at the tip.
    """
    half = kw.ROLL_HOOP_HALF_WIDTH

    for side, _ in mirrored((0.0, 0.0, 0.0)):
        # Heavy at the root and tapering hard to the tip. The first cut ran 58 mm to 16 mm,
        # which is a tube with a point on it - from any distance the pair read as radio
        # antennae, not horns. A horn is mostly its base: the mass has to be down where it
        # leaves the bodywork, and the taper has to be steep enough to see over its length.
        knots = [
            (half * side, HORN_ROOT_Y, HORN_Z),
            ((half + 0.07) * side, 0.94, HORN_Z + 0.02),
            ((half + 0.20) * side, 1.26, HORN_Z + 0.06),
            (HORN_TIP_X * side, HORN_TIP_Y, HORN_Z + 0.16),
        ]
        radii = (0.105, 0.072, 0.036, 0.012)
        for i in range(len(knots) - 1):
            tb.taper(bm, u(*knots[i]), u(*knots[i + 1]), radii[i], radii[i + 1],
                     CRUST if i < 2 else BLUED)
        # A glowing band where the crust breaks over the core.
        tb.tube(bm, u(*knots[2]), u(*knots[2]).lerp(u(*knots[3]), 0.22), 0.030, FISSURE)

        # Rear stay off each horn, so they are braced rather than cantilevered.
        tb.tube(bm, u((half + 0.04) * side, 0.98, HORN_Z + 0.02),
                u((half - 0.06) * side, 0.48, TAIL_Z + 0.06), BRACE_TUBE, BLUED)

    # Cross tie low between the horn roots - the part that is still a roll hoop.
    tb.tube(bm, u(-half, HORN_ROOT_Y + 0.30, HORN_Z), u(half, HORN_ROOT_Y + 0.30, HORN_Z),
            MAIN_TUBE, BLUED)
    tb.cuboid(bm, u(0.0, HORN_ROOT_Y + 0.30, HORN_Z - 0.03), usize(0.34, 0.05, 0.04),
              FISSURE)


def add_stacks(bm):
    """Twin chimney stacks off the engine bay - the second half of the jagged skyline.

    Kept below the horns. Two tall elements at the same height fight each other and the
    silhouette turns into a fence; stepped, they read as one shape with a peak.
    """
    for side, _ in mirrored((0.0, 0.0, 0.0)):
        base = u(STACK_X * side, 0.58, STACK_Z)
        mid = u((STACK_X + 0.03) * side, 1.00, STACK_Z - 0.02)
        top = u((STACK_X + 0.05) * side, STACK_TOP_Y, STACK_Z - 0.03)
        tb.taper(bm, base, mid, 0.062, 0.050, IRON, segments=8)
        tb.taper(bm, mid, top, 0.050, 0.042, IRON, segments=8)
        # Crust creeping up the outside of each stack, and the heat glowing at the mouth.
        tb.tube(bm, base, base.lerp(mid, 0.45), 0.070, CRUST, segments=8)
        tb.tube(bm, top.lerp(mid, 0.10), top, 0.030, FISSURE, segments=6)
        # Iron straps round the stacks.
        for t in (0.30, 0.72):
            point = base.lerp(top, t)
            tb.tube(bm, point, point + (top - base).normalized() * 0.035, 0.058, BLUED,
                    segments=8)


def add_cockpit(bm):
    """Charred hide seat slung low in the shell, and the controls."""
    tb.slab(bm, u(0.0, 0.34, -0.18), u(0.0, 0.36, -0.60), 0.44, 0.09, CHAR)
    tb.slab(bm, u(0.0, 0.40, -0.60), u(0.0, 1.00, -0.76), 0.40, 0.09, CHAR)
    for _side, (low, high) in mirrored((0.22, 0.38, -0.26), (0.22, 0.90, -0.72)):
        tb.slab(bm, low, high, 0.06, 0.15, CHAR)
    # Head rest with a hot seam behind it.
    tb.cuboid(bm, u(0.0, 1.04, -0.78), usize(0.24, 0.13, 0.09), CHAR)
    tb.cuboid(bm, u(0.0, 1.04, -0.83), usize(0.16, 0.04, 0.03), FISSURE)

    tb.cuboid(bm, u(0.0, 0.60, 0.42), usize(0.46, 0.10, 0.22), CRUST)
    # Cracks across the cowl, not a panel of light on it - same mistake as the nose intake.
    for x in (-0.13, 0.02):
        tb.cuboid(bm, u(x, 0.65, 0.40), usize(0.14, 0.03, 0.035), FISSURE)

    tb.tube(bm, u(*STEERING_RACK), u(*STEERING_HUB), THIN_TUBE, BLUED)
    tb.tube(bm, u(-0.16, STEERING_RACK[1], STEERING_RACK[2]),
            u(0.16, STEERING_RACK[1], STEERING_RACK[2]), 0.028, BLUED)
    for side, _ in mirrored((0.0, 0.0, 0.0)):
        tb.tube(bm, u(0.06 * side, 0.30, 0.32), u(0.50 * side, 0.28, 0.76), 0.016, BLUED)
        tb.slab(bm, u(0.17 * side, 0.24, 0.68), u(0.17 * side, 0.36, 0.64), 0.10, 0.03, IRON)


def add_drivetrain(bm):
    """Engine block behind the seat, glowing through its own cracks."""
    tb.cuboid(bm, u(0.0, 0.56, -0.90), usize(0.50, 0.38, 0.34), BLUED)
    tb.cuboid(bm, u(0.0, 0.56, -0.90), usize(0.52, 0.05, 0.24), FISSURE)
    for side, _ in mirrored((0.0, 0.0, 0.0)):
        tb.cuboid(bm, u(0.27 * side, 0.62, -0.90), usize(0.05, 0.20, 0.28), IRON)
    tb.tube(bm, u(-0.60, REAR_WHEEL_RADIUS, REAR_AXLE_Z),
            u(0.60, REAR_WHEEL_RADIUS, REAR_AXLE_Z), 0.042, BLUED)


def add_suspension(bm):
    for side, _ in mirrored((0.0, 0.0, 0.0)):
        tb.tube(bm, u(0.28 * side, FLOOR_TOP_Y, FRONT_AXLE_Z),
                u(0.52 * side, FRONT_WHEEL_RADIUS, FRONT_AXLE_Z), BRACE_TUBE, BLUED)
        kw.coilover(bm, u(0.30 * side, 0.32, 0.70), u(0.54 * side, 0.76, 0.62), BLUED, IRON)
        tb.tube(bm, u(0.30 * side, 0.30, -0.48),
                u(0.56 * side, REAR_WHEEL_RADIUS, REAR_AXLE_Z), 0.034, BLUED)
        kw.coilover(bm, u(0.32 * side, 0.36, -0.78), u(0.56 * side, 0.90, -0.70),
                    BLUED, IRON)


def build_body():
    bm = bmesh.new()
    add_floor(bm)
    add_shell(bm)
    add_fissures(bm)
    add_horns(bm)
    add_stacks(bm)
    add_cockpit(bm)
    add_drivetrain(bm)
    add_suspension(bm)
    return kw.finish(bm, BODY_NAME, PALETTE)


# ---------------------------------------------------------------------------------------
# Wheels
# ---------------------------------------------------------------------------------------

def build_wheel(name, radius, width, slabs=8):
    """A heat-cracked tyre: few wide slabs with molten grooves between them.

    The opposite construction from the piste basher's dense studded chevrons, and
    deliberately so - the two snow-and-rock karts would otherwise share a tread language.
    Wide slabs with visible gaps also let the fissure material show between them, which is
    what makes the wheels part of the style rather than four black rings under it.
    """
    bm = bmesh.new()
    kw.wheel_carcass(bm, radius, width, SLAB, IRON, carcass=0.84, rim=0.50, hub=0.20,
                     spokes=0)

    # A glowing ring buried under the slabs, showing in the grooves between them.
    tb.tube(bm, u(-width * 0.30, 0, 0), u(width * 0.30, 0, 0), radius * 0.88, FISSURE,
            segments=14)

    # Iron centre with cast spokes.
    tb.tube(bm, u(-width * 0.18, 0, 0), u(width * 0.30, 0, 0), radius * 0.50, IRON,
            segments=12)
    for _i, _theta, spoke in kw.around(5, radius * 0.42):
        tb.slab(bm, u(width * 0.28, 0.0, 0.0), u(width * 0.28, spoke.y, spoke.z),
                width * 0.12, radius * 0.14, IRON)

    sweep = (2.0 * math.pi / slabs) * 0.66
    for _i, theta, _direction in kw.around(slabs, 1.0):
        kw.tread_block(bm, radius, theta - sweep * 0.5, theta + sweep * 0.5,
                       -width * 0.36, width * 0.36, radius * 0.14, radius * 0.30, SLAB)

    return kw.finish(bm, name, PALETTE)


def build_steering_wheel(name=STEERING_WHEEL_NAME):
    """A broken ring of crust on a blued core, with a hot seam across the boss."""
    bm = bmesh.new()
    radius = kw.STEERING_WHEEL_RADIUS
    segments = kw.STEERING_RIM_SEGMENTS

    ring = [(radius * math.cos(2.0 * math.pi * i / segments), 0.0,
             radius * math.sin(2.0 * math.pi * i / segments)) for i in range(segments)]
    for i, point in enumerate(ring):
        nxt = ring[(i + 1) % segments]
        # Alternating thickness, so the rim reads as broken rock rather than as tube.
        tb.tube(bm, u(*point), u(*nxt), 0.030 if i % 2 else 0.022,
                CRUST if i % 3 else BLUED, segments=5)

    tb.tube(bm, u(0.0, -0.026, 0.0), u(0.0, 0.026, 0.0), 0.052, BLUED, segments=8)
    tb.cuboid(bm, u(0.0, 0.030, 0.0), usize(0.075, 0.012, 0.020), FISSURE)

    for i in range(3):
        phi = math.radians(90.0 + i * 120.0)
        spoke = (radius * math.cos(phi), 0.0, radius * math.sin(phi))
        tb.slab(bm, u(0.0, 0.0, 0.0), u(*spoke), 0.034, 0.017, BLUED)

    return kw.finish(bm, name, PALETTE)


if __name__ == "__main__":
    kw.check_against_blueprint("cinder_hauler.py")
    kw.write_manifest(
        "Cinder", PALETTE, nose_lamps=False, roof_bar=False,
        meshes=[BODY_NAME, WHEEL_FRONT_NAME, WHEEL_REAR_NAME, STEERING_WHEEL_NAME],
            emissive={"KartLens": [3.4, 1.15, 0.14]},
    )

    kw.emit(build_body, BODY_NAME, max_tris=3600, max_size_m=3.0)
    kw.emit(lambda: build_wheel(WHEEL_FRONT_NAME, FRONT_WHEEL_RADIUS, kw.FRONT_WHEEL_WIDTH),
            WHEEL_FRONT_NAME, max_tris=820, max_size_m=1.0,
            tread_radius=FRONT_WHEEL_RADIUS)
    kw.emit(lambda: build_wheel(WHEEL_REAR_NAME, REAR_WHEEL_RADIUS, kw.REAR_WHEEL_WIDTH),
            WHEEL_REAR_NAME, max_tris=820, max_size_m=1.0,
            tread_radius=REAR_WHEEL_RADIUS)
    kw.emit(build_steering_wheel, STEERING_WHEEL_NAME, max_tris=500, max_size_m=0.5)
