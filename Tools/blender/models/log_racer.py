"""
Log racer - the woodland kart. A hollowed log on cross-cut wheels, under an antler hoop.

    blender --background --factory-startup --python Tools/blender/models/log_racer.py

The concept calls this one wheel-led, and that is the thing to get right: it "would still
read at a distance where the bodywork is a smudge". So the wheels here are not a tread
pattern on a black ring - they are cross-cut rounds of the same timber the body is, with
bark round the edge and growth rings on the face.

That produces the strongest reinterpretation of the slot contract in the set. **KartRubber
is bark.** Not a tyre that happens to be brown - the slot that every other style spends on
a black tyre is spent here on the one material that makes the wheels read as cut wood. The
tread is chevrons carved *into* that bark, so it is the same material as the sidewall,
which is exactly what a cross-cut round would look like if you notched it.

The body is a hollowed half-log: a solid barrel of timber with the cockpit adzed out of the
top. Modelled as a ring of staves round an open middle rather than as a tube with a hole
cut in it, because this pipeline has no booleans and a boolean here would produce the
n-gons and coplanar slivers the cabin notes warn about.

The antler hoop is the tall element and the only non-timber shape on the kart.
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

BODY_NAME = "KartLog_Body"
WHEEL_FRONT_NAME = "KartLog_WheelFront"
WHEEL_REAR_NAME = "KartLog_WheelRear"
STEERING_WHEEL_NAME = "KartLog_SteeringWheel"

FRONT_WHEEL_RADIUS = kw.FRONT_WHEEL_RADIUS
REAR_WHEEL_RADIUS = kw.REAR_WHEEL_RADIUS
FRONT_TRACK = kw.FRONT_TRACK
REAR_TRACK = kw.REAR_TRACK
FRONT_AXLE_Z = kw.FRONT_AXLE_Z
REAR_AXLE_Z = kw.REAR_AXLE_Z
STEERING_HUB = kw.STEERING_HUB
STEERING_RACK = kw.STEERING_RACK

FRAME, BODY, SEAT, RIM, RUBBER, LENS = range(6)

BARK = RUBBER       # see the docstring: this slot is bark, not tyre
HEART = BODY        # the pale cut timber - hollowed cockpit, wheel faces
LIMB = FRAME        # antler and the darker branch structure
MOSS = RIM          # moss on the shoulders, and the green in the wheel hubs
HIDE = SEAT         # a pelt thrown over the seat

PALETTE = kw.palette(
    frame=((0.55, 0.48, 0.38), 0.00, 0.75),      # KartFrame  - antler
    body=((0.66, 0.48, 0.28), 0.00, 0.80),       # KartBody   - cut heartwood
    seat=((0.34, 0.24, 0.17), 0.00, 0.88),       # KartSeat   - pelt
    rim=((0.29, 0.42, 0.20), 0.00, 0.90),        # KartRim    - moss
    rubber=((0.27, 0.20, 0.14), 0.00, 0.92),     # KartRubber - BARK
)

FLOOR_TOP_Y = 0.22
LOG_RADIUS = 0.44
LOG_AXIS_Y = 0.44
LOG_FRONT_Z = 1.16
LOG_BACK_Z = -1.00
STAVES = 9          # staves round the lower half of the barrel

ANTLER_Z = kw.ROLL_HOOP_Z
ANTLER_TOP_Y = 1.46


# ---------------------------------------------------------------------------------------
# The log
# ---------------------------------------------------------------------------------------

def add_log(bm):
    """A hollowed barrel: staves round the bottom, open across the top.

    The staves run from about 200 degrees round to about 340 - the lower two thirds - so
    the top is genuinely open and the driver sits down inside it. Each stave is a beam laid
    along the log with its depth pointing at the axis, which is what makes the outside
    curve and the inside follow it.
    """
    for i in range(STAVES):
        # 195 to 345 degrees measured with 0 at the top, so the gap is over the cockpit.
        angle = math.radians(196.0 + (344.0 - 196.0) * i / (STAVES - 1))
        outward = Vector((math.sin(angle), math.cos(angle), 0.0))

        for z_a, z_b, r_a, r_b in ((LOG_BACK_Z, -0.10, LOG_RADIUS, LOG_RADIUS),
                                   (-0.10, 0.74, LOG_RADIUS, LOG_RADIUS * 0.98),
                                   (0.74, LOG_FRONT_Z, LOG_RADIUS * 0.98,
                                    LOG_RADIUS * 0.80)):
            a = u(outward.x * r_a, LOG_AXIS_Y + outward.y * r_a, z_a)
            b = u(outward.x * r_b, LOG_AXIS_Y + outward.y * r_b, z_b)
            # Depth points at the axis, width runs round the barrel.
            tb.beam(bm, a, b, 0.135, 0.085, BARK,
                    up=u(outward.x, outward.y, 0.0))

    # The adzed inner surface: pale heartwood just inside the staves, so the cockpit is a
    # hollow in a log rather than the inside of a dark shell.
    for i in range(STAVES):
        angle = math.radians(200.0 + (340.0 - 200.0) * i / (STAVES - 1))
        outward = Vector((math.sin(angle), math.cos(angle), 0.0))
        r = LOG_RADIUS - 0.075
        a = u(outward.x * r, LOG_AXIS_Y + outward.y * r, LOG_BACK_Z + 0.08)
        b = u(outward.x * r, LOG_AXIS_Y + outward.y * r, LOG_FRONT_Z - 0.14)
        tb.beam(bm, a, b, 0.120, 0.035, HEART, up=u(outward.x, outward.y, 0.0))

    # Cut ends: the growth-ring faces at each end of the log.
    for z, radius, sign in ((LOG_FRONT_Z, LOG_RADIUS * 0.80, 1), (LOG_BACK_Z, LOG_RADIUS, -1)):
        tb.tube(bm, u(0.0, LOG_AXIS_Y, z), u(0.0, LOG_AXIS_Y, z + 0.05 * sign),
                radius * 0.94, HEART, segments=12)
        for ring_r in (radius * 0.34, radius * 0.62):
            tb.tube(bm, u(0.0, LOG_AXIS_Y, z + 0.05 * sign),
                    u(0.0, LOG_AXIS_Y, z + 0.068 * sign), ring_r, BARK, segments=12)


def add_moss(bm):
    """Moss on the shoulders - the upper edges of the opening, where rain would sit.

    Only along the top rails and only in patches. Moss everywhere reads as a green log;
    moss on the two edges the light hits reads as a log that has been lying in a wood.
    """
    for side, _ in mirrored((0.0, 0.0, 0.0)):
        for z_a, z_b in ((-0.86, -0.42), (-0.30, 0.16), (0.34, 0.72)):
            angle = math.radians(196.0)
            outward = Vector((math.sin(angle) * side, math.cos(angle), 0.0))
            r = LOG_RADIUS + 0.035
            a = u(abs(outward.x) * r * side, LOG_AXIS_Y + outward.y * r, z_a)
            b = u(abs(outward.x) * r * side, LOG_AXIS_Y + outward.y * r, z_b)
            tb.beam(bm, a, b, 0.11, 0.030, MOSS, up=u(outward.x, outward.y, 0.0))

    # Clumps in the bark, asymmetric on purpose. Bigger than the first pass, which put
    # 70-100 mm specks on a 2.3 m log and lost them completely - moss that cannot be seen
    # is triangles spent on nothing.
    for x, y, z, size in ((0.42, 0.30, -0.60, 0.20), (-0.44, 0.40, 0.20, 0.17),
                          (0.44, 0.36, 0.58, 0.14), (-0.40, 0.24, -0.24, 0.15),
                          (0.30, 0.72, -0.88, 0.16)):
        tb.cuboid(bm, u(x, y, z), usize(size * 0.55, size, size * 1.7), MOSS)


def add_antlers(bm):
    """The roll hoop, as a pair of antlers.

    Each is a main beam with three tines coming off it, forking forward the way a real
    antler does. Built from tapers rather than tubes because an antler that does not narrow
    towards its points is a branch, and the whole shape only works if the tines are thin.
    """
    half = kw.ROLL_HOOP_HALF_WIDTH

    for side, _ in mirrored((0.0, 0.0, 0.0)):
        # The pedicle and main beam, sweeping up and outward.
        base = u(half * side, 0.62, ANTLER_Z + 0.06)
        knee = u((half + 0.09) * side, 1.02, ANTLER_Z - 0.02)
        crown = u((half + 0.14) * side, ANTLER_TOP_Y, ANTLER_Z - 0.10)
        tb.taper(bm, base, knee, 0.058, 0.042, LIMB, segments=6)
        tb.taper(bm, knee, crown, 0.042, 0.022, LIMB, segments=6)

        # Tines, each springing forward off the beam at a different height.
        for t, length, rise, out in ((0.22, 0.26, 0.16, 0.05),
                                     (0.62, 0.30, 0.20, 0.09),
                                     (0.92, 0.22, 0.14, 0.12)):
            root = base.lerp(crown, t)
            tip = root + u(out * side, rise, length)
            tb.taper(bm, root, tip, 0.030, 0.009, LIMB, segments=5)

        # Brow tine, pointing forward low down.
        tb.taper(bm, base.lerp(knee, 0.10), base + u(0.02 * side, 0.10, 0.30),
                 0.028, 0.008, LIMB, segments=5)

        # Rear stay down to the log, so the antlers are structural rather than stuck on.
        tb.taper(bm, knee, u((half - 0.10) * side, 0.52, LOG_BACK_Z + 0.10),
                 0.034, 0.026, LIMB, segments=5)

    # The cross beam between them - the part that is still a roll hoop, lashed with hide.
    tb.taper(bm, u(-half - 0.02, 1.06, ANTLER_Z - 0.02), u(half + 0.02, 1.06, ANTLER_Z - 0.02),
             0.036, 0.036, LIMB, segments=6)
    for x in (-0.18, 0.18):
        tb.tube(bm, u(x, 1.06, ANTLER_Z - 0.04), u(x, 1.06, ANTLER_Z), 0.046, HIDE,
                segments=6)


def add_cockpit(bm):
    """A pelt over an adzed seat, and controls whittled out of branch."""
    tb.slab(bm, u(0.0, 0.34, -0.16), u(0.0, 0.36, -0.58), 0.46, 0.09, HIDE)
    tb.slab(bm, u(0.0, 0.40, -0.58), u(0.0, 0.98, -0.74), 0.42, 0.09, HIDE)
    tb.cuboid(bm, u(0.0, 1.02, -0.76), usize(0.24, 0.12, 0.09), HIDE)
    # The pelt hanging over the sides of the opening.
    for side, _ in mirrored((0.0, 0.0, 0.0)):
        tb.beam(bm, u(0.34 * side, 0.52, -0.50), u(0.40 * side, 0.30, -0.34),
                0.22, 0.026, HIDE, up=u(0.0, 1.0, 0.0))

    # Dash: a slab of heartwood across the log, with a whittled lever through it.
    tb.beam(bm, u(0.0, 0.56, 0.42), u(0.0, 0.60, 0.30), 0.44, 0.06, HEART,
            up=u(0.0, 1.0, 0.0))

    tb.taper(bm, u(*STEERING_RACK), u(*STEERING_HUB), 0.032, 0.026, LIMB, segments=6)
    tb.tube(bm, u(-0.16, STEERING_RACK[1], STEERING_RACK[2]),
            u(0.16, STEERING_RACK[1], STEERING_RACK[2]), 0.028, LIMB)
    for side, _ in mirrored((0.0, 0.0, 0.0)):
        tb.tube(bm, u(0.06 * side, 0.30, 0.32), u(0.50 * side, 0.28, 0.76), 0.016, LIMB)
        tb.slab(bm, u(0.17 * side, 0.24, 0.68), u(0.17 * side, 0.36, 0.64), 0.10, 0.03,
                HEART)

    tb.taper(bm, u(0.28, 0.40, -0.14), u(0.25, 0.68, -0.22), 0.020, 0.014, LIMB, segments=5)
    tb.tube(bm, u(0.25, 0.68, -0.22), u(0.25, 0.72, -0.22), 0.032, HIDE, segments=6)


def add_drivetrain(bm):
    """A stump of engine wedged in behind the seat, and the axle."""
    tb.tube(bm, u(0.0, 0.52, -0.86), u(0.0, 0.52, -0.98), 0.20, HEART, segments=10)
    tb.tube(bm, u(0.0, 0.52, -0.98), u(0.0, 0.52, -1.02), 0.21, BARK, segments=10)
    # Two branch exhausts out of the top.
    for x, lean in ((0.14, 0.05), (-0.10, -0.04)):
        tb.taper(bm, u(x, 0.66, -0.88), u(x + lean, 0.96, -0.98), 0.030, 0.020, LIMB,
                 segments=5)
    tb.tube(bm, u(-0.60, REAR_WHEEL_RADIUS, REAR_AXLE_Z),
            u(0.60, REAR_WHEEL_RADIUS, REAR_AXLE_Z), 0.042, LIMB)


def add_suspension(bm):
    """Branch wishbones and coil-overs bound in hide."""
    for side, _ in mirrored((0.0, 0.0, 0.0)):
        tb.taper(bm, u(0.26 * side, FLOOR_TOP_Y + 0.04, FRONT_AXLE_Z),
                 u(0.52 * side, FRONT_WHEEL_RADIUS, FRONT_AXLE_Z), 0.036, 0.026, LIMB)
        kw.coilover(bm, u(0.30 * side, 0.34, 0.70), u(0.50 * side, 0.74, 0.62), LIMB, HIDE)
        tb.taper(bm, u(0.28 * side, 0.32, -0.48),
                 u(0.56 * side, REAR_WHEEL_RADIUS, REAR_AXLE_Z), 0.038, 0.028, LIMB)
        kw.coilover(bm, u(0.32 * side, 0.38, -0.78), u(0.54 * side, 0.88, -0.70),
                    LIMB, HIDE)


def build_body():
    bm = bmesh.new()
    add_log(bm)
    add_moss(bm)
    add_antlers(bm)
    add_cockpit(bm)
    add_drivetrain(bm)
    add_suspension(bm)
    return kw.finish(bm, BODY_NAME, PALETTE)


# ---------------------------------------------------------------------------------------
# Wheels - the point of the whole style
# ---------------------------------------------------------------------------------------

def build_wheel(name, radius, width, notches=10):
    """A cross-cut log round: bark sidewall, growth rings on the face, notched tread.

    The face is concentric rings of alternating tone rather than a flat disc, and they are
    off-centre - a real round's heart is never in the middle. The tread is notches cut into
    the bark, so tread and sidewall are one material, which is the whole reinterpretation
    the concept asks for.
    """
    bm = bmesh.new()

    # The round itself: bark on the outside, heartwood across the faces.
    tb.tube(bm, u(-width * 0.5, 0, 0), u(width * 0.5, 0, 0), radius * 0.93, BARK,
            segments=14)
    for face_x in (-1, 1):
        tb.tube(bm, u(width * 0.46 * face_x, 0, 0), u(width * 0.50 * face_x, 0, 0),
                radius * 0.90, HEART, segments=14)

    # Growth rings, off-centre towards the top of the round.
    heart_y, heart_z = radius * 0.10, radius * 0.06
    for i, ring_r in enumerate((0.24, 0.44, 0.62, 0.80)):
        for face_x in (-1, 1):
            tb.tube(bm, u(width * 0.50 * face_x, heart_y, heart_z),
                    u(width * 0.53 * face_x, heart_y, heart_z), radius * ring_r,
                    BARK if i % 2 else HEART, segments=12)
    # The heart itself, and a moss patch on one face.
    for face_x in (-1, 1):
        tb.tube(bm, u(width * 0.53 * face_x, heart_y, heart_z),
                u(width * 0.56 * face_x, heart_y, heart_z), radius * 0.12, LIMB,
                segments=8)
    moss_at = Vector((0.0, math.sin(2.2), math.cos(2.2))) * (radius * 0.55)
    tb.cuboid(bm, u(width * 0.54, moss_at.y, moss_at.z),
              usize(0.02, radius * 0.30, radius * 0.22), MOSS)

    # Notched chevron tread, carved into the bark and peaking on the nominal radius.
    sweep = (2.0 * math.pi / notches) * 0.40
    for i, theta, _direction in kw.around(notches, 1.0):
        for sign in (-1, 1):
            kw.tread_block(bm, radius, theta, theta - sweep * sign,
                           width * 0.02 * sign, width * 0.38 * sign,
                           radius * 0.09, radius * 0.22, BARK)

    return kw.finish(bm, name, PALETTE)


def build_steering_wheel(name=STEERING_WHEEL_NAME):
    """A bent sapling ring bound with hide, on a whittled boss."""
    bm = bmesh.new()
    radius = kw.STEERING_WHEEL_RADIUS
    segments = kw.STEERING_RIM_SEGMENTS

    ring = [(radius * math.cos(2.0 * math.pi * i / segments), 0.0,
             radius * math.sin(2.0 * math.pi * i / segments)) for i in range(segments)]
    for i, point in enumerate(ring):
        nxt = ring[(i + 1) % segments]
        tb.tube(bm, u(*point), u(*nxt), 0.023, BARK, segments=5)
        if i % 3 == 0:
            mid = Vector(u(*point)).lerp(Vector(u(*nxt)), 0.5)
            direction = (Vector(u(*nxt)) - Vector(u(*point))).normalized() * 0.022
            tb.tube(bm, mid - direction, mid + direction, 0.031, HIDE, segments=6)

    tb.tube(bm, u(0.0, -0.024, 0.0), u(0.0, 0.026, 0.0), 0.050, HEART, segments=8)
    tb.tube(bm, u(0.0, 0.026, 0.0), u(0.0, 0.034, 0.0), 0.020, LIMB, segments=6)

    for i in range(3):
        phi = math.radians(90.0 + i * 120.0)
        rim = (radius * math.cos(phi), 0.0, radius * math.sin(phi))
        tb.taper(bm, u(0.0, 0.0, 0.0), u(*rim), 0.026, 0.016, HEART, segments=5)

    return kw.finish(bm, name, PALETTE)


if __name__ == "__main__":
    kw.check_against_blueprint("log_racer.py")
    kw.write_manifest(
        "Log", PALETTE, nose_lamps=False, roof_bar=False,
        meshes=[BODY_NAME, WHEEL_FRONT_NAME, WHEEL_REAR_NAME, STEERING_WHEEL_NAME],
    )

    kw.emit(build_body, BODY_NAME, max_tris=5200, max_size_m=3.0)
    kw.emit(lambda: build_wheel(WHEEL_FRONT_NAME, FRONT_WHEEL_RADIUS, kw.FRONT_WHEEL_WIDTH),
            WHEEL_FRONT_NAME, max_tris=1400, max_size_m=1.0,
            tread_radius=FRONT_WHEEL_RADIUS)
    kw.emit(lambda: build_wheel(WHEEL_REAR_NAME, REAR_WHEEL_RADIUS, kw.REAR_WHEEL_WIDTH),
            WHEEL_REAR_NAME, max_tris=1400, max_size_m=1.0,
            tread_radius=REAR_WHEEL_RADIUS)
    kw.emit(build_steering_wheel, STEERING_WHEEL_NAME, max_tris=620, max_size_m=0.5)
