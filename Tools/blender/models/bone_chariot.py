"""
Bone chariot - the second hell kart. A ribcage with the seat slung inside it.

    blender --background --factory-startup --python Tools/blender/models/bone_chariot.py

The concept's claim for this one is unusual and worth preserving exactly: "its body is
negative space; you see the track through it, and nothing else in the lineup does that, so
it never competes for silhouette space." Everything below follows from that.

**The ribs must not close up.** The temptation with a ribcage is to add ribs until it reads
as solid, and the moment it does, the design is gone - it becomes a dark barrel and the
cinder hauler already owns "dark and jagged". Eleven pairs with real daylight between them,
and nothing behind them: no floor plate above the pan, no side panels, no engine cover.

**It is the hardest of the set to build to the collider rules, and this file does not
pretend to solve that.** The concept says so itself: ribs are exactly the geometry a wheel
catches on, and it calls for "a hidden simplified collision shell". That shell is a Unity
concern - it is a collider, not a mesh in this pipeline - so what this file does is keep the
ribs *inside* a clean convex envelope, so that a capsule or box hull fitted round the body
in Unity never has a rib poking through it. Nothing here sticks out sideways past the
staves' outer line.

The skull is a nose cone, not a decoration: it is the front of the kart, it carries the
headlamps in its eye sockets, and its jaw is the lowest bodywork.
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

BODY_NAME = "KartBone_Body"
WHEEL_FRONT_NAME = "KartBone_WheelFront"
WHEEL_REAR_NAME = "KartBone_WheelRear"
STEERING_WHEEL_NAME = "KartBone_SteeringWheel"

FRONT_WHEEL_RADIUS = kw.FRONT_WHEEL_RADIUS
REAR_WHEEL_RADIUS = kw.REAR_WHEEL_RADIUS
FRONT_TRACK = kw.FRONT_TRACK
REAR_TRACK = kw.REAR_TRACK
FRONT_AXLE_Z = kw.FRONT_AXLE_Z
REAR_AXLE_Z = kw.REAR_AXLE_Z
STEERING_HUB = kw.STEERING_HUB
STEERING_RACK = kw.STEERING_RACK

FRAME, BODY, SEAT, RIM, RUBBER, LENS = range(6)

BONE = BODY         # ribs, spine, skull - the style's whole surface
SINEW = FRAME       # dark dried sinew binding the joints, and the axles
HIDE = SEAT         # the sling the driver sits in
IRON = RIM          # the few iron parts: hub bands, bit, buckles
TYRE = RUBBER
EMBER = LENS        # the glow in the eye sockets

PALETTE = kw.palette(
    frame=((0.20, 0.17, 0.15), 0.15, 0.85),      # KartFrame  - dried sinew
    body=((0.84, 0.80, 0.70), 0.00, 0.72),       # KartBody   - bone
    seat=((0.26, 0.17, 0.14), 0.00, 0.88),       # KartSeat   - hide sling
    rim=((0.34, 0.31, 0.30), 0.45, 0.55),        # KartRim    - blackened iron
    rubber=((0.09, 0.08, 0.08), 0.00, 0.88),     # KartRubber - tyre
    lens=((1.00, 0.34, 0.10), 0.00, 0.40),       # KartLens   - the ember in the sockets
)

FLOOR_TOP_Y = 0.22
SPINE_Y = 1.02          # the spine runs along the top, above the driver's shoulders
SPINE_FRONT_Z = 0.72
SPINE_BACK_Z = -1.02
# Eight pairs, not eleven. At eleven the ribs very nearly touched and the cage read as a
# closed shell - which is the one failure this design cannot survive, because "you see the
# track through it" is the entire concept. Fewer and thinner ribs, more daylight.
RIB_COUNT = 8
RIB_HALF_WIDTH = 0.46   # the convex envelope: nothing goes outside this
STERNUM_Y = 0.26

SKULL_Z = 1.10
SKULL_Y = 0.46


# ---------------------------------------------------------------------------------------
# Skeleton
# ---------------------------------------------------------------------------------------

def add_spine(bm):
    """The spine, as vertebrae rather than a rod.

    A rod would be the cheap way and it would cost the whole read: a spine is *segmented*,
    and the repeated bump along its length is what the eye uses to identify it as bone from
    a distance where the ribs are just gaps.
    """
    count = 13
    for i in range(count):
        t = i / (count - 1)
        z = SPINE_FRONT_Z + (SPINE_BACK_Z - SPINE_FRONT_Z) * t
        # The spine arches: highest over the driver, dropping fore and aft.
        y = SPINE_Y + math.sin(t * math.pi) * 0.10
        radius = 0.052 - abs(t - 0.5) * 0.020

        tb.tube(bm, u(0.0, y, z + 0.045), u(0.0, y, z - 0.045), radius, BONE, segments=6)
        # Neural spine standing up off each vertebra, tallest at the shoulder.
        height = 0.05 + math.sin(t * math.pi) * 0.07
        tb.taper(bm, u(0.0, y + radius, z), u(0.0, y + radius + height, z - 0.02),
                 0.026, 0.012, BONE, segments=5)
        # Sinew wrap between vertebrae.
        if i < count - 1:
            tb.tube(bm, u(0.0, y, z - 0.048), u(0.0, y, z - 0.062), radius * 0.72, SINEW,
                    segments=6)


def rib_profile(t):
    """Where one rib's knots sit, as a fraction `t` along the cage front to back.

    Returned rather than inlined because both `add_ribs` and `add_sternum` walk the same
    curve and they have to agree about it - a sternum that does not land on the rib tips is
    the one error that turns a ribcage back into a pile of hoops.
    """
    # Cage is widest and deepest around the driver, tapering at both ends.
    spread = math.sin(0.15 + t * 2.70)
    half = RIB_HALF_WIDTH * (0.52 + 0.48 * spread)
    depth = 0.30 + 0.44 * spread
    z = SPINE_FRONT_Z + (SPINE_BACK_Z - SPINE_FRONT_Z) * t
    top_y = SPINE_Y + math.sin(t * math.pi) * 0.10
    return half, depth, z, top_y


def add_ribs(bm):
    """Eleven pairs, each a taper sweeping down and out from the spine and back in again.

    Four knots per rib rather than a smooth curve: at this budget four straight tapers with
    falling radius read as a rib, and a swept curve costs three times as much to say the
    same thing. The rib thins as it descends, which is what keeps the cage from looking
    like a set of identical staves.
    """
    for i in range(RIB_COUNT):
        t = (i + 0.5) / RIB_COUNT
        half, depth, z, top_y = rib_profile(t)
        drop = z + 0.10 * math.sin(t * math.pi)   # ribs sweep backwards as they descend

        for side, _ in mirrored((0.0, 0.0, 0.0)):
            knots = [
                (0.05 * side, top_y - 0.02, z),
                (half * 0.72 * side, top_y - depth * 0.30, z + 0.02),
                (half * side, top_y - depth * 0.74, drop),
                (half * 0.56 * side, STERNUM_Y + 0.04, drop + 0.03),
            ]
            radii = (0.030, 0.024, 0.019, 0.013)
            for k in range(3):
                tb.taper(bm, u(*knots[k]), u(*knots[k + 1]), radii[k], radii[k + 1],
                         BONE, segments=5)
            # Sinew binding where the rib meets the spine.
            tb.tube(bm, u(0.03 * side, top_y - 0.02, z),
                    u(0.10 * side, top_y - 0.05, z), 0.040, SINEW, segments=5)


def add_sternum(bm):
    """The breastbone the ribs land on, low down the middle of the cage.

    Plated rather than a single bar, and it is the only thing tying the two sides together
    below the spine - so the cage is a closed structure without ever being a closed surface.
    """
    for i in range(5):
        t = 0.14 + i * 0.16
        _half, _depth, z, _top = rib_profile(t)
        tb.cuboid(bm, u(0.0, STERNUM_Y, z + 0.04), usize(0.15, 0.055, 0.16), BONE)
        if i < 4:
            tb.tube(bm, u(0.0, STERNUM_Y, z + 0.04), u(0.0, STERNUM_Y, z - 0.12),
                    0.022, SINEW, segments=5)

    # Costal cartilage: short links from the lowest rib knots in to the sternum.
    for i in range(RIB_COUNT):
        t = (i + 0.5) / RIB_COUNT
        half, depth, z, top_y = rib_profile(t)
        drop = z + 0.10 * math.sin(t * math.pi)
        for side, _ in mirrored((0.0, 0.0, 0.0)):
            tb.taper(bm, u(half * 0.56 * side, STERNUM_Y + 0.04, drop + 0.03),
                     u(0.10 * side, STERNUM_Y + 0.01, drop + 0.06), 0.017, 0.012,
                     BONE, segments=4)


def add_skull(bm):
    """The nose cone. Cranium, brow, muzzle, jaw - and the lamps in the eye sockets.

    The lamp glass sits on KartBlueprint's own headlamp points, because KartLights hangs a
    real Unity Light on the front face of it. That decided where the eyes go, and the skull
    was then built around them rather than the other way round: the sockets are at
    y 0.47, z 1.30 because that is where the beams come from.
    """
    # Cranium and brow.
    tb.tube(bm, u(0.0, SKULL_Y + 0.06, SKULL_Z - 0.06), u(0.0, SKULL_Y + 0.06, SKULL_Z + 0.16),
            0.21, BONE, segments=8)
    tb.cuboid(bm, u(0.0, SKULL_Y + 0.16, SKULL_Z + 0.14), usize(0.36, 0.10, 0.22), BONE)
    # Cheekbones, flaring out and back.
    for side, _ in mirrored((0.0, 0.0, 0.0)):
        tb.taper(bm, u(0.20 * side, SKULL_Y + 0.02, SKULL_Z + 0.16),
                 u(0.13 * side, SKULL_Y - 0.06, SKULL_Z + 0.40), 0.070, 0.045, BONE,
                 segments=6)
        # Horn stubs off the brow - small, because the cinder hauler owns real horns.
        tb.taper(bm, u(0.16 * side, SKULL_Y + 0.20, SKULL_Z + 0.10),
                 u(0.24 * side, SKULL_Y + 0.34, SKULL_Z - 0.02), 0.038, 0.014, BONE,
                 segments=5)

    # Muzzle and jaw.
    #
    # Short. The eye sockets are pinned at z 1.30 by KartBlueprint, so every millimetre of
    # muzzle is added *in front of* a point that is already 500 mm ahead of the front axle.
    # At 0.52 long it took the body to 3.13 m and `validate` refused it; a blunt muzzle also
    # reads more like a bull's skull than a long one does, so this costs nothing.
    tb.taper(bm, u(0.0, SKULL_Y - 0.02, SKULL_Z + 0.28), u(0.0, SKULL_Y - 0.06, SKULL_Z + 0.42),
             0.135, 0.100, BONE, segments=8)
    tb.cuboid(bm, u(0.0, SKULL_Y - 0.15, SKULL_Z + 0.32), usize(0.22, 0.07, 0.24), BONE)
    # Teeth.
    for x in (-0.07, 0.0, 0.07):
        tb.taper(bm, u(x, SKULL_Y - 0.11, SKULL_Z + 0.38), u(x, SKULL_Y - 0.19, SKULL_Z + 0.40),
                 0.020, 0.008, BONE, segments=4)

    # Eye sockets, and the lamps in them. `kw.lamps` puts the glass exactly where the C#
    # expects; the sockets are rings drawn round it.
    for side, _ in mirrored((0.0, 0.0, 0.0)):
        tb.tube(bm, u(kw.HEADLAMP_HALF_SPACING * side, kw.HEADLAMP_Y, kw.HEADLAMP_Z - 0.03),
                u(kw.HEADLAMP_HALF_SPACING * side, kw.HEADLAMP_Y, kw.HEADLAMP_Z + 0.035),
                0.085, BONE, segments=8)
    kw.lamps(bm, SINEW, nose=True, roof=False)

    # Neck: the skull joins the cage at the front of the spine.
    for i, t in enumerate((0.25, 0.55, 0.85)):
        y = SKULL_Y + 0.06 + (SPINE_Y - SKULL_Y - 0.06) * t
        z = SKULL_Z - 0.06 + (SPINE_FRONT_Z - SKULL_Z + 0.06) * t
        tb.tube(bm, u(0.0, y, z + 0.035), u(0.0, y, z - 0.035), 0.055 - i * 0.004, BONE,
                segments=6)
        tb.tube(bm, u(0.0, y, z - 0.038), u(0.0, y, z - 0.052), 0.040, SINEW, segments=5)


def add_pelvis_and_tail(bm):
    """The back end: a pelvis over the rear axle and a short tail off it."""
    tb.cuboid(bm, u(0.0, 0.62, SPINE_BACK_Z + 0.04), usize(0.44, 0.30, 0.13), BONE)
    for side, _ in mirrored((0.0, 0.0, 0.0)):
        tb.taper(bm, u(0.20 * side, 0.74, SPINE_BACK_Z + 0.04),
                 u(0.34 * side, 0.44, SPINE_BACK_Z - 0.02), 0.055, 0.034, BONE, segments=6)
        # Femur stubs down to the rear hubs, so the cage stands on something.
        tb.taper(bm, u(0.26 * side, 0.50, SPINE_BACK_Z + 0.02),
                 u(0.54 * side, REAR_WHEEL_RADIUS, REAR_AXLE_Z), 0.048, 0.030, BONE,
                 segments=6)

    # A stub tail, three vertebrae. The fourth was what pushed the body past 3 m together
    # with the muzzle, and a tail is the cheapest thing on the kart to give up: it points
    # away from every camera the game uses.
    for t, radius in ((0.0, 0.045), (1.0, 0.035), (2.0, 0.025)):
        z = SPINE_BACK_Z - 0.10 - t * 0.11
        y = 0.62 - t * 0.05
        tb.tube(bm, u(0.0, y, z + 0.04), u(0.0, y, z - 0.04), radius, BONE, segments=6)


def add_sling(bm):
    """The seat: a hide sling slung *inside* the ribs, hanging off the spine.

    Hung rather than mounted. A seat on a pedestal would fill the negative space the whole
    design is made of; a sling reads as suspended in the cage and leaves the daylight
    around it intact.
    """
    tb.slab(bm, u(0.0, 0.34, -0.16), u(0.0, 0.36, -0.58), 0.42, 0.05, HIDE)
    tb.slab(bm, u(0.0, 0.40, -0.58), u(0.0, 0.96, -0.74), 0.38, 0.05, HIDE)
    # Straps from the sling up to the spine.
    for side, _ in mirrored((0.0, 0.0, 0.0)):
        for z_low, z_high in ((-0.18, -0.10), (-0.56, -0.62)):
            tb.beam(bm, u(0.20 * side, 0.36, z_low), u(0.06 * side, SPINE_Y - 0.02, z_high),
                    0.045, 0.015, HIDE, up=u(1.0 * side, 0.0, 0.0))
        tb.cuboid(bm, u(0.20 * side, 0.38, -0.18), usize(0.05, 0.05, 0.05), IRON)


def add_cockpit(bm):
    """Reins for a steering column, and bone pedals."""
    tb.taper(bm, u(*STEERING_RACK), u(*STEERING_HUB), 0.030, 0.024, BONE, segments=6)
    tb.tube(bm, u(-0.16, STEERING_RACK[1], STEERING_RACK[2]),
            u(0.16, STEERING_RACK[1], STEERING_RACK[2]), 0.026, BONE)
    for side, _ in mirrored((0.0, 0.0, 0.0)):
        tb.tube(bm, u(0.06 * side, 0.30, 0.32), u(0.50 * side, 0.28, 0.76), 0.015, SINEW)
        tb.slab(bm, u(0.17 * side, 0.24, 0.68), u(0.17 * side, 0.36, 0.64), 0.10, 0.03, BONE)
    # The floor pan - the one solid plate on the kart, and the lowest thing on it.
    tb.cuboid(bm, u(0.0, FLOOR_TOP_Y - 0.025, 0.0), usize(0.62, 0.05, 1.36), SINEW)


def add_suspension(bm):
    """Bone wishbones with sinew-bound coil-overs."""
    for side, _ in mirrored((0.0, 0.0, 0.0)):
        tb.taper(bm, u(0.24 * side, FLOOR_TOP_Y + 0.04, FRONT_AXLE_Z),
                 u(0.52 * side, FRONT_WHEEL_RADIUS, FRONT_AXLE_Z), 0.038, 0.024, BONE)
        kw.coilover(bm, u(0.28 * side, 0.32, 0.70), u(0.48 * side, 0.72, 0.62), SINEW, IRON)
        kw.coilover(bm, u(0.30 * side, 0.36, -0.78), u(0.52 * side, 0.86, -0.70),
                    SINEW, IRON)
    tb.tube(bm, u(-0.60, REAR_WHEEL_RADIUS, REAR_AXLE_Z),
            u(0.60, REAR_WHEEL_RADIUS, REAR_AXLE_Z), 0.038, SINEW)


def build_body():
    bm = bmesh.new()
    add_spine(bm)
    add_ribs(bm)
    add_sternum(bm)
    add_skull(bm)
    add_pelvis_and_tail(bm)
    add_sling(bm)
    add_cockpit(bm)
    add_suspension(bm)
    return kw.finish(bm, BODY_NAME, PALETTE)


# ---------------------------------------------------------------------------------------
# Wheels
# ---------------------------------------------------------------------------------------

def build_wheel(name, radius, width, spokes=6):
    """A cart wheel with bone spokes and an iron tyre band.

    Spoked and open, to match a body made of negative space - a solid disc here would be
    the heaviest thing on a kart whose whole point is that you can see through it.
    """
    bm = bmesh.new()

    # Widths matter more than radii here, because `tb.tube` is a solid capped cylinder.
    #
    # The first cut drew the tyre at 0.90 x radius across 80% of the width and the bone
    # felloe and spokes *inside* it. Every one of them was buried: a solid disc at the
    # larger radius hides everything behind it, so the wheel rendered as a plain dark
    # circle and the whole spoked read was lost. The dark parts are narrow bands now and
    # the bone runs wider, so the timber shows on both faces where the camera sees it.
    tb.tube(bm, u(-width * 0.26, 0, 0), u(width * 0.26, 0, 0), radius * 0.90, TYRE,
            segments=14)
    # Iron band round the felloe, narrower still.
    tb.tube(bm, u(-width * 0.10, 0, 0), u(width * 0.10, 0, 0), radius * 0.915, IRON,
            segments=14)
    # Felloe and hub in bone, standing proud of the tyre on both sides.
    tb.tube(bm, u(-width * 0.44, 0, 0), u(width * 0.44, 0, 0), radius * 0.74, BONE,
            segments=14)
    tb.tube(bm, u(-width * 0.56, 0, 0), u(width * 0.56, 0, 0), radius * 0.24, BONE,
            segments=8)
    tb.tube(bm, u(-width * 0.60, 0, 0), u(width * 0.48, 0, 0), radius * 0.15, IRON,
            segments=8)

    # Bone spokes, on the outside of the felloe where they can actually be seen.
    for face in (-1, 1):
        for _i, _theta, spoke in kw.around(spokes, radius * 0.70):
            tb.taper(bm, u(width * 0.48 * face, 0.0, 0.0),
                     u(width * 0.48 * face, spoke.y, spoke.z),
                     radius * 0.070, radius * 0.045, BONE, segments=5)

    # Tread: shallow blocks, since the felloe does most of the work visually.
    blocks = 10
    sweep = (2.0 * math.pi / blocks) * 0.52
    for _i, theta, _direction in kw.around(blocks, 1.0):
        kw.tread_block(bm, radius, theta - sweep * 0.5, theta + sweep * 0.5,
                       -width * 0.34, width * 0.34, radius * 0.08, radius * 0.24, TYRE)

    return kw.finish(bm, name, PALETTE)


def build_steering_wheel(name=STEERING_WHEEL_NAME):
    """A ring of vertebrae on a bone boss, with an iron bit across the middle."""
    bm = bmesh.new()
    radius = kw.STEERING_WHEEL_RADIUS
    segments = kw.STEERING_RIM_SEGMENTS

    ring = [(radius * math.cos(2.0 * math.pi * i / segments), 0.0,
             radius * math.sin(2.0 * math.pi * i / segments)) for i in range(segments)]
    for i, point in enumerate(ring):
        nxt = ring[(i + 1) % segments]
        tb.tube(bm, u(*point), u(*nxt), 0.018, SINEW, segments=5)
        # A vertebra threaded on at each station.
        along = (Vector(u(*nxt)) - Vector(u(*point))).normalized() * 0.030
        centre = Vector(u(*point))
        tb.tube(bm, centre - along, centre + along, 0.038, BONE, segments=6)

    tb.tube(bm, u(0.0, -0.022, 0.0), u(0.0, 0.024, 0.0), 0.048, BONE, segments=8)
    tb.cuboid(bm, u(0.0, 0.028, 0.0), usize(0.10, 0.014, 0.024), IRON)

    for i in range(3):
        phi = math.radians(90.0 + i * 120.0)
        rim = (radius * math.cos(phi), 0.0, radius * math.sin(phi))
        tb.taper(bm, u(0.0, 0.0, 0.0), u(*rim), 0.024, 0.014, BONE, segments=5)

    return kw.finish(bm, name, PALETTE)


if __name__ == "__main__":
    kw.check_against_blueprint("bone_chariot.py")
    kw.write_manifest(
        "Bone", PALETTE, nose_lamps=True, roof_bar=False,
        meshes=[BODY_NAME, WHEEL_FRONT_NAME, WHEEL_REAR_NAME, STEERING_WHEEL_NAME],
            emissive={"KartLens": [2.6, 0.8, 0.18]},
    )

    kw.emit(build_body, BODY_NAME, max_tris=6000, max_size_m=3.0)
    kw.emit(lambda: build_wheel(WHEEL_FRONT_NAME, FRONT_WHEEL_RADIUS, kw.FRONT_WHEEL_WIDTH),
            WHEEL_FRONT_NAME, max_tris=1300, max_size_m=1.0,
            tread_radius=FRONT_WHEEL_RADIUS)
    kw.emit(lambda: build_wheel(WHEEL_REAR_NAME, REAR_WHEEL_RADIUS, kw.REAR_WHEEL_WIDTH),
            WHEEL_REAR_NAME, max_tris=1300, max_size_m=1.0,
            tread_radius=REAR_WHEEL_RADIUS)
    kw.emit(build_steering_wheel, STEERING_WHEEL_NAME, max_tris=760, max_size_m=0.5)
