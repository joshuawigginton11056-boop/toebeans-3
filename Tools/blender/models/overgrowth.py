"""
Overgrowth - the jungle kart. A bamboo frame the jungle took back, and one enormous leaf.

    blender --background --factory-startup --python Tools/blender/models/overgrowth.py

The concept's line is that "the frame *is* the design", and the consequence is that this
style has almost no bodywork to hide behind: every tube is on show from every angle, so
the frame has to be worth looking at rather than merely present. Two things do that work.

**Bamboo is not tube.** A bamboo pole is a stack of segments with a swollen node between
each, and that is what separates it from the buggy's steel at any distance. `culm` below
draws a run as a chain of slightly tapering segments with a collar at every joint, which
costs about three times a plain `tb.tube` and is the single most important detail here.

**The leaf is the entire read from behind.** It is one enormous blade on a stalk, cambered
so it is not a flat card, with a midrib and veins on the upper surface. It sits where a
rear wing goes and it is deliberately the only large flat surface on the kart.

The seat and floor are woven rattan - a lattice of crossed strips rather than a panel,
because a solid pan here would be the one opaque thing on an open kart and would read as
a mistake. Weaving it costs triangles, which is why the frame is bamboo-detailed and the
suspension is left plain: the budget goes where the design does.
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

BODY_NAME = "KartOvergrowth_Body"
WHEEL_FRONT_NAME = "KartOvergrowth_WheelFront"
WHEEL_REAR_NAME = "KartOvergrowth_WheelRear"
STEERING_WHEEL_NAME = "KartOvergrowth_SteeringWheel"

FRONT_WHEEL_RADIUS = kw.FRONT_WHEEL_RADIUS
REAR_WHEEL_RADIUS = kw.REAR_WHEEL_RADIUS
FRONT_TRACK = kw.FRONT_TRACK
REAR_TRACK = kw.REAR_TRACK
FRONT_AXLE_Z = kw.FRONT_AXLE_Z
REAR_AXLE_Z = kw.REAR_AXLE_Z
STEERING_HUB = kw.STEERING_HUB
STEERING_RACK = kw.STEERING_RACK

FRAME, BODY, SEAT, RIM, RUBBER, LENS = range(6)

BAMBOO = FRAME      # the culms - this style's structural colour and most of its surface
LEAF = BODY         # the wing, and every other leaf on the kart
RATTAN = SEAT       # woven floor and seat
LASH = RIM          # vine lashings and the cord binding the joints
TYRE = RUBBER

PALETTE = kw.palette(
    frame=((0.72, 0.68, 0.34), 0.00, 0.70),      # KartFrame  - dry bamboo
    body=((0.20, 0.46, 0.16), 0.00, 0.65),       # KartBody   - living leaf
    seat=((0.62, 0.45, 0.24), 0.00, 0.85),       # KartSeat   - woven rattan
    rim=((0.30, 0.40, 0.20), 0.00, 0.80),        # KartRim    - vine and cord
    rubber=((0.10, 0.10, 0.09), 0.00, 0.85),     # KartRubber - hand-cut tread
)

FLOOR_TOP_Y = 0.22
RAIL_X = 0.40
RAIL_Y = 0.26
CAGE_X = 0.46
TAIL_Z = -1.02
NOSE_Z = 1.22

CULM = 0.046        # a bamboo pole is fatter than the steel it replaces, and has to be
THIN_CULM = 0.032
NODE = 0.010        # how far a node collar stands proud of its culm

# The leaf, sized against the 3 m budget rather than by eye.
#
# It started 0.86 m long hung off z -1.02, which put its tip at -1.88 and the whole body at
# 3.31 m - `validate` refused it. Length was the wrong axis to spend on anyway: from the
# chase camera behind the kart, a leaf reads as big by being *wide*, and every centimetre
# of length is spent where the camera sees it foreshortened to nothing.
LEAF_Z = -0.96
LEAF_TOP_Y = 1.40
LEAF_HALF_WIDTH = 0.78
LEAF_LENGTH = 0.60


# ---------------------------------------------------------------------------------------
# Bamboo
# ---------------------------------------------------------------------------------------

def culm(bm, a, b, radius, skin=BAMBOO, node_every=0.26, segments=6):
    """A bamboo pole: segments separated by swollen nodes.

    The whole style rests on this, so it is worth stating what it does differently from
    `tb.tube`. A tube is one cylinder; a culm is `n` cylinders that taper very slightly
    towards the far end, with a wider collar at each joint. The taper matters as much as
    the collars - a stack of identical cylinders with rings on it reads as pipe with
    clamps, and the 4% narrowing per segment is what makes it read as grown.
    """
    a, b = Vector(a), Vector(b)
    run = b - a
    length = run.length
    count = max(1, int(round(length / node_every)))

    for i in range(count):
        t0, t1 = i / count, (i + 1) / count
        r0 = radius * (1.0 - 0.04 * i)
        r1 = radius * (1.0 - 0.04 * (i + 1))
        tb.taper(bm, a + run * t0, a + run * t1, r0, r1, skin, segments=segments)
        if i < count - 1:
            joint = a + run * t1
            direction = run.normalized() * 0.018
            tb.tube(bm, joint - direction, joint + direction, r1 + NODE, skin,
                    segments=segments)


def lashing(bm, point, direction, radius):
    """A vine wrap where two culms meet. Bamboo is tied, not welded, and the joints are
    the places a viewer looks for that."""
    direction = Vector(direction).normalized() * 0.026
    tb.tube(bm, Vector(point) - direction, Vector(point) + direction, radius + 0.016,
            LASH, segments=6)


def add_frame(bm):
    """Lower rails, the prow, and the cross ties - all bamboo, all lashed."""
    for side, _ in mirrored((0.0, 0.0, 0.0)):
        rail_a = u(RAIL_X * side, RAIL_Y, 0.86)
        rail_b = u(RAIL_X * side, RAIL_Y, TAIL_Z)
        culm(bm, rail_a, rail_b, CULM)
        culm(bm, rail_a, u(0.28 * side, 0.42, NOSE_Z), CULM)
        culm(bm, rail_b, u(0.46 * side, 0.46, TAIL_Z), THIN_CULM)
        lashing(bm, rail_a, (0.0, 0.0, 1.0), CULM)

    for z in (0.84, 0.10, -0.56, TAIL_Z):
        culm(bm, u(-RAIL_X, RAIL_Y, z), u(RAIL_X, RAIL_Y, z), THIN_CULM)
    culm(bm, u(-0.28, 0.42, NOSE_Z), u(0.28, 0.42, NOSE_Z), CULM)
    # Prow: two culms crossing ahead of the nose bar, tied where they meet.
    culm(bm, u(-0.28, 0.42, NOSE_Z), u(0.10, 0.30, NOSE_Z + 0.18), THIN_CULM)
    culm(bm, u(0.28, 0.42, NOSE_Z), u(-0.10, 0.30, NOSE_Z + 0.18), THIN_CULM)
    lashing(bm, u(0.0, 0.36, NOSE_Z + 0.09), (1.0, 0.0, 0.0), THIN_CULM)


def add_hoop(bm):
    """The hoop, with vines wrapping it. Bamboo poles, tied at every crossing."""
    half = kw.ROLL_HOOP_HALF_WIDTH
    top = kw.ROLL_HOOP_TOP_Y
    z = kw.ROLL_HOOP_Z

    for side, _ in mirrored((0.0, 0.0, 0.0)):
        foot = u(CAGE_X * side, 0.28, z + 0.04)
        head = u(half * side, top, z)
        culm(bm, foot, head, CULM)
        culm(bm, head, u((half - 0.04) * side, 0.46, TAIL_Z), THIN_CULM)
        lashing(bm, head, (0.0, 1.0, 0.0), CULM)
        # A-pillar forward, so the cockpit has a frame around it.
        culm(bm, u(CAGE_X * side, 0.28, 0.42), u((half + 0.02) * side, 1.16, 0.24), CULM)
        culm(bm, u((half + 0.02) * side, 1.16, 0.24), head, THIN_CULM)

        # Vines spiralling up the upright: short chords stepping round the pole.
        for i in range(6):
            t0, t1 = 0.12 + i * 0.13, 0.20 + i * 0.13
            phase = i * 1.9
            for t, ph in ((t0, phase), (t1, phase + 1.2)):
                pass
            base = foot.lerp(head, t0)
            tip = foot.lerp(head, t1)
            off0 = Vector((math.cos(phase), math.sin(phase), 0.0)) * (CULM + 0.014)
            off1 = Vector((math.cos(phase + 1.9), math.sin(phase + 1.9), 0.0)) * (CULM + 0.014)
            tb.tube(bm, base + off0, tip + off1, 0.012, LASH, segments=4)

    culm(bm, u(-half, top, z), u(half, top, z), CULM)
    culm(bm, u(-half - 0.02, 1.16, 0.24), u(half + 0.02, 1.16, 0.24), THIN_CULM)


def add_weave(bm, centre, size, skin, strips=5, thickness=0.016):
    """A woven panel: strips one way, strips the other, alternating which is on top.

    Authored as a real lattice rather than a textured plane. This pipeline has no way to
    author a weave texture, and on an open kart the floor pan is seen from underneath as
    often as from above.
    """
    cx, cy, cz = centre
    sx, sy, sz = size
    for i in range(strips):
        t = (i + 0.5) / strips - 0.5
        lift = thickness * (0.35 if i % 2 else -0.35)
        tb.cuboid(bm, u(cx + sx * t, cy + lift, cz), usize(sx / strips * 0.62, sy, sz),
                  skin)
    across = max(2, int(strips * sz / max(sx, 1e-6)) + 2)
    for i in range(across):
        t = (i + 0.5) / across - 0.5
        lift = thickness * (-0.35 if i % 2 else 0.35)
        tb.cuboid(bm, u(cx, cy + lift, cz + sz * t), usize(sx, sy, sz / across * 0.62),
                  skin)


def add_cockpit(bm):
    """Woven floor pan, woven seat, and a rattan-bound dash."""
    add_weave(bm, (0.0, FLOOR_TOP_Y, 0.10), (0.72, 0.030, 1.34), RATTAN, strips=6)

    # Seat: squab and back, both woven, on a bamboo subframe.
    add_weave(bm, (0.0, 0.37, -0.38), (0.46, 0.028, 0.44), RATTAN, strips=5)
    for side, _ in mirrored((0.0, 0.0, 0.0)):
        culm(bm, u(0.23 * side, 0.34, -0.16), u(0.23 * side, 0.34, -0.60), THIN_CULM,
             node_every=0.22)
        culm(bm, u(0.21 * side, 0.36, -0.62), u(0.21 * side, 1.00, -0.78), THIN_CULM,
             node_every=0.22)
    # The seat back, woven between those two culms.
    for i in range(6):
        y = 0.44 + i * 0.10
        z = -0.63 - i * 0.026
        tb.cuboid(bm, u(0.0, y, z), usize(0.42, 0.026, 0.030), RATTAN)
    for x in (-0.13, 0.0, 0.13):
        tb.beam(bm, u(x, 0.42, -0.625), u(x, 1.00, -0.775), 0.030, 0.022, RATTAN,
                up=u(0.0, 0.0, 1.0))

    tb.cuboid(bm, u(0.0, 0.56, 0.42), usize(0.40, 0.06, 0.16), RATTAN)
    culm(bm, u(*STEERING_RACK), u(*STEERING_HUB), 0.028, node_every=0.20)
    tb.tube(bm, u(-0.16, STEERING_RACK[1], STEERING_RACK[2]),
            u(0.16, STEERING_RACK[1], STEERING_RACK[2]), 0.026, BAMBOO)
    for side, _ in mirrored((0.0, 0.0, 0.0)):
        tb.tube(bm, u(0.06 * side, 0.30, 0.32), u(0.50 * side, 0.28, 0.76), 0.015, LASH)
        tb.slab(bm, u(0.17 * side, 0.24, 0.68), u(0.17 * side, 0.36, 0.64), 0.10, 0.03,
                BAMBOO)


def add_leaf_wing(bm):
    """The rear wing: one enormous leaf on a stalk.

    Cambered rather than flat - the blade rises from the stalk to a ridge along the midrib
    and falls away to the edges - because a flat blade at this size is a card and reads as
    one. Built as a fan of quads from the midrib out to the margin, so the outline can be
    a leaf shape rather than a rectangle.
    """
    stalk_base = u(0.0, 0.50, TAIL_Z + 0.08)
    stalk_top = u(0.0, LEAF_TOP_Y - 0.14, LEAF_Z - 0.10)
    culm(bm, stalk_base, stalk_top, 0.034, node_every=0.24)
    lashing(bm, stalk_base, (0.0, 1.0, 0.0), 0.034)

    # Midrib, running back and up from the top of the stalk.
    tip = u(0.0, LEAF_TOP_Y + 0.06, LEAF_Z - LEAF_LENGTH)
    root = u(0.0, LEAF_TOP_Y - 0.16, LEAF_Z + 0.06)
    tb.taper(bm, root, tip, 0.030, 0.010, LEAF, segments=6)

    # The blade. Stations along the midrib, each with a half-width and a camber height.
    stations = ((0.00, 0.10, 0.000), (0.18, 0.34, 0.030), (0.40, 0.50, 0.042),
                (0.62, 0.46, 0.036), (0.82, 0.30, 0.020), (1.00, 0.03, 0.000))
    rib_points = [root.lerp(tip, s[0]) for s in stations]

    for side in (-1, 1):
        rows = []
        for (t, half, camber), spine in zip(stations, rib_points):
            edge = spine + Vector((half * LEAF_HALF_WIDTH / 0.50 * side, 0.0, 0.0))
            edge = edge - Vector((0.0, 0.0, camber))
            mid = spine.lerp(edge, 0.55) + Vector((0.0, 0.0, camber))
            rows.append((spine, mid, edge))

        for (a_s, a_m, a_e), (b_s, b_m, b_e) in zip(rows, rows[1:]):
            for quad in ((a_s, a_m, b_m, b_s), (a_m, a_e, b_e, b_m)):
                verts = [bm.verts.new(p) for p in quad]
                face = bm.faces.new(verts)
                face.material_index = LEAF

        # Veins, running from the midrib out to the margin at a swept-back angle.
        for (t, half, camber), spine in list(zip(stations, rib_points))[1:-1]:
            edge = spine + Vector((half * LEAF_HALF_WIDTH / 0.50 * side, 0.0, -camber))
            back = edge.lerp(tip, 0.18)
            tb.tube(bm, spine, back, 0.010, LEAF, segments=4)


def add_foliage(bm):
    """Smaller leaves sprouting where the vines are thickest, and a shoot off the nose.

    Few and asymmetric. A kart with leaves distributed evenly over it reads as decorated;
    a kart with three clumps reads as one the jungle is actually taking.
    """
    for x, y, z, length, half, yaw in ((0.44, 0.72, -0.30, 0.26, 0.10, 0.5),
                                       (-0.47, 0.94, 0.10, 0.22, 0.09, -0.9),
                                       (0.30, 0.44, NOSE_Z - 0.06, 0.20, 0.08, 0.2)):
        base = u(x, y, z)
        tip = u(x + math.sin(yaw) * length, y + length * 0.5, z + math.cos(yaw) * length)
        tb.taper(bm, base, tip, 0.012, 0.006, LEAF, segments=4)
        mid = base.lerp(tip, 0.55)
        for side in (-1, 1):
            wide = mid + Vector((half * side, 0.0, 0.0))
            verts = [bm.verts.new(p) for p in (base, wide, tip)]
            face = bm.faces.new(verts)
            face.material_index = LEAF


def add_drivetrain(bm):
    """An engine bound in under the seat, wrapped in vine so it is barely a machine."""
    tb.cuboid(bm, u(0.0, 0.50, -0.88), usize(0.44, 0.32, 0.30), LASH)
    for y in (0.40, 0.56):
        tb.cuboid(bm, u(0.0, y, -0.88), usize(0.48, 0.035, 0.34), BAMBOO)
    culm(bm, u(0.20, 0.66, -0.90), u(0.24, 0.98, -1.00), 0.030, node_every=0.18)
    tb.tube(bm, u(-0.60, REAR_WHEEL_RADIUS, REAR_AXLE_Z),
            u(0.60, REAR_WHEEL_RADIUS, REAR_AXLE_Z), 0.038, BAMBOO)


def add_suspension(bm):
    """Plain, and plain on purpose - the triangle budget went to the culms and the leaf."""
    for side, _ in mirrored((0.0, 0.0, 0.0)):
        tb.tube(bm, u(0.28 * side, FLOOR_TOP_Y, FRONT_AXLE_Z),
                u(0.52 * side, FRONT_WHEEL_RADIUS, FRONT_AXLE_Z), 0.030, BAMBOO)
        kw.coilover(bm, u(0.30 * side, 0.32, 0.70), u(0.52 * side, 0.74, 0.62),
                    LASH, BAMBOO)
        tb.tube(bm, u(0.30 * side, 0.30, -0.48),
                u(0.56 * side, REAR_WHEEL_RADIUS, REAR_AXLE_Z), 0.032, BAMBOO)
        kw.coilover(bm, u(0.32 * side, 0.36, -0.78), u(0.56 * side, 0.90, -0.70),
                    LASH, BAMBOO)


def build_body():
    bm = bmesh.new()
    add_frame(bm)
    add_hoop(bm)
    add_cockpit(bm)
    add_leaf_wing(bm)
    add_foliage(bm)
    add_drivetrain(bm)
    add_suspension(bm)
    return kw.finish(bm, BODY_NAME, PALETTE)


# ---------------------------------------------------------------------------------------
# Wheels
# ---------------------------------------------------------------------------------------

def build_wheel(name, radius, width, blocks=9):
    """A hand-cut tyre on a bamboo-spoked rim, with vine lashing round the hub."""
    bm = bmesh.new()
    kw.wheel_carcass(bm, radius, width, TYRE, LASH, carcass=0.86, rim=0.48, hub=0.18,
                     spokes=0)

    # Bamboo spokes: short culms from the hub out to the rim.
    for _i, _theta, spoke in kw.around(6, radius * 0.62):
        culm(bm, u(width * 0.16, 0.0, 0.0), u(width * 0.16, spoke.y, spoke.z),
             radius * 0.045, skin=BAMBOO, node_every=0.09, segments=5)
    tb.tube(bm, u(-width * 0.24, 0, 0), u(width * 0.28, 0, 0), radius * 0.22, LASH,
            segments=8)

    # Tread: chunky hand-cut blocks, alternating in length so the cut looks hand-made.
    sweep = (2.0 * math.pi / blocks) * 0.44
    for i, theta, _direction in kw.around(blocks, 1.0):
        span = sweep * (1.0 if i % 2 else 0.62)
        kw.tread_block(bm, radius, theta - span * 0.5, theta + span * 0.5,
                       -width * 0.34, width * 0.34, radius * 0.10, radius * 0.26, TYRE)

    return kw.finish(bm, name, PALETTE)


def build_steering_wheel(name=STEERING_WHEEL_NAME):
    """A bent branch loop bound with vine, exactly as the concept asks.

    Irregular by construction: the rim radius wobbles a few per cent per segment, which is
    what separates a bent branch from a hoop. Authored in the "Steering" pivot's local
    space - rim in local XZ, column up local Y.
    """
    bm = bmesh.new()
    radius = kw.STEERING_WHEEL_RADIUS
    segments = kw.STEERING_RIM_SEGMENTS

    wobble = (1.00, 1.06, 0.97, 1.04, 0.95, 1.02, 0.98, 1.05, 0.96, 1.03)
    ring = []
    for i in range(segments):
        phi = 2.0 * math.pi * i / segments
        r = radius * wobble[i % len(wobble)]
        ring.append((r * math.cos(phi), 0.0, r * math.sin(phi)))

    for i, point in enumerate(ring):
        nxt = ring[(i + 1) % segments]
        tb.tube(bm, u(*point), u(*nxt), 0.020 + 0.004 * (i % 3), BAMBOO, segments=5)
        # Vine binding at every other joint.
        if i % 2 == 0:
            lashing(bm, u(*point), (0.0, 1.0, 0.0), 0.020)

    tb.tube(bm, u(0.0, -0.024, 0.0), u(0.0, 0.026, 0.0), 0.046, LASH, segments=8)

    # Three branch spokes, each with a slight kink rather than running straight.
    for i in range(3):
        phi = math.radians(90.0 + i * 120.0)
        elbow = (radius * 0.52 * math.cos(phi + 0.16), 0.0, radius * 0.52 * math.sin(phi + 0.16))
        rim = (radius * math.cos(phi), 0.0, radius * math.sin(phi))
        tb.taper(bm, u(0.0, 0.0, 0.0), u(*elbow), 0.022, 0.017, BAMBOO, segments=5)
        tb.taper(bm, u(*elbow), u(*rim), 0.017, 0.014, BAMBOO, segments=5)

    return kw.finish(bm, name, PALETTE)


if __name__ == "__main__":
    kw.check_against_blueprint("overgrowth.py")
    kw.write_manifest(
        "Overgrowth", PALETTE, nose_lamps=False, roof_bar=False,
        meshes=[BODY_NAME, WHEEL_FRONT_NAME, WHEEL_REAR_NAME, STEERING_WHEEL_NAME],
    )

    kw.emit(build_body, BODY_NAME, max_tris=6200, max_size_m=3.0)
    kw.emit(lambda: build_wheel(WHEEL_FRONT_NAME, FRONT_WHEEL_RADIUS, kw.FRONT_WHEEL_WIDTH),
            WHEEL_FRONT_NAME, max_tris=1500, max_size_m=1.0,
            tread_radius=FRONT_WHEEL_RADIUS)
    kw.emit(lambda: build_wheel(WHEEL_REAR_NAME, REAR_WHEEL_RADIUS, kw.REAR_WHEEL_WIDTH),
            WHEEL_REAR_NAME, max_tris=1500, max_size_m=1.0,
            tread_radius=REAR_WHEEL_RADIUS)
    kw.emit(build_steering_wheel, STEERING_WHEEL_NAME, max_tris=700, max_size_m=0.5)
