"""
Farmyard props - bales, troughs, churns, crates, and the clutter that sells a working farm.

    blender --background --factory-startup --python Tools/blender/models/farm_props.py

These are the pack's scatter. A barn is placed once and looked at; a bale is placed forty
times and glanced at, so the constraints are different in three ways worth stating:

**Triangle budgets are tight and they bite.** Nothing here costs more than a barn window.
Where a shape wanted a cylinder it got eight sides, and where it wanted a wire it got a
square bar - at these sizes a six-sided tube spends six times the triangles to draw the
same two-pixel line.

**Everything is a kart obstacle, so nothing has a lip.** A trough is a solid block with a
dish in the top, not a shell with a rim to catch a wheel; a bale is round; a crate is
chamfered like everything else. The one rule from the project README that matters most
here is the last one: the mesh is the collider.

**Nothing meets the ground on a clean line.** A prop whose base is a perfect rectangle
reads as dropped onto the terrain rather than as standing on it, so most of these carry a
little spilled straw, chaff or muck at the foot. It costs a handful of triangles and it is
the single cheapest thing that makes a scatter look placed.

Sizes are real: a round bale is 1.5 m across, a churn is knee high, a pallet crate is
0.8 m. A kart is 1.24 m across its front track, which is the scale reference every one of
these was checked against.
"""

import math
import os
import random
import sys

import bmesh

sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
import toebeans_blender as tb  # noqa: E402
import farmyard as fy  # noqa: E402

SEED = 20260820

box = tb.box
beam = tb.beam
prism = tb.prism


# --------------------------------------------------------------------------------------
# Hay
# --------------------------------------------------------------------------------------

def build_bale_round(name="Farm_HayBaleRound", radius=0.75, width=1.20):
    """A round bale on its side. The pack's best obstacle: it cannot present a corner.

    Twelve sides rather than eight, because this is the one prop a kart is most likely to
    hit square on, and a facet that wide turns a glancing blow into a flat impact.
    """
    bm = bmesh.new()
    rng = random.Random(SEED)
    fy.bale_round(bm, (0.0, 0.0, radius), (1.0, 0.0, 0.0), radius, width,
                  skin=fy.SKIN_STRAW, segments=12)
    # Loose straw round the foot, and a couple of tufts pulled out of the barrel.
    fy.scatter_seed(bm, (0.0, 0.0, 0.0), radius * 1.15, 8, 0.07, fy.SKIN_STRAW, rng)
    for _ in range(4):
        a = rng.uniform(0.0, math.tau)
        x = rng.uniform(-width * 0.45, width * 0.45)
        at = (x, math.cos(a) * radius * 0.92, radius + math.sin(a) * radius * 0.92)
        out = (x, math.cos(a) * (radius + 0.10), radius + math.sin(a) * (radius + 0.10))
        beam(bm, at, out, 0.05, 0.05, fy.SKIN_STRAW, up=(0.0, 0.0, 1.0))
    return fy.finish(bm, name)


def build_bale_square(name="Farm_HayBaleSquare", w=0.46, d=0.90, h=0.36):
    """A small rectangular bale. Cheap enough to scatter by the dozen."""
    bm = bmesh.new()
    rng = random.Random(SEED + 1)
    box(bm, (-w / 2, -d / 2, 0.0), (w / 2, d / 2, h), fy.SKIN_STRAW)
    # Two baler twines, and a slightly proud end so it is not a plain cuboid.
    for y in (-d * 0.26, d * 0.26):
        box(bm, (-w / 2 - 0.012, y - 0.018, 0.0), (w / 2 + 0.012, y + 0.018, h),
            fy.SKIN_DIRT)
    for sign in (-1, 1):
        box(bm, (-w * 0.38, sign * (d / 2 - 0.03), 0.05),
            (w * 0.38, sign * (d / 2 + 0.035), h - 0.05), fy.SKIN_STRAW)
    fy.scatter_seed(bm, (0.0, 0.0, 0.0), 0.45, 5, 0.05, fy.SKIN_STRAW, rng)
    return fy.finish(bm, name)


def build_hay_stack(name="Farm_HayStack"):
    """A stack of small bales, courses crossed the way a real stack is laid.

    Crossed courses are not decoration - a stack laid all one way is a wall, and a wall is
    what this reads as if the top course runs with the bottom one.
    """
    bm = bmesh.new()
    rng = random.Random(SEED + 2)
    w, d, h = 0.46, 0.90, 0.36

    def course(z, across):
        if across:
            for i in range(2):
                for j in range(4):
                    x = (i - 0.5) * (d + 0.02)
                    y = (j - 1.5) * (w + 0.02)
                    box(bm, (x - d / 2, y - w / 2, z), (x + d / 2, y + w / 2, z + h),
                        fy.SKIN_STRAW)
        else:
            for i in range(4):
                for j in range(2):
                    x = (i - 1.5) * (w + 0.02)
                    y = (j - 0.5) * (d + 0.02)
                    box(bm, (x - w / 2, y - d / 2, z), (x + w / 2, y + d / 2, z + h),
                        fy.SKIN_STRAW)

    for level in range(3):
        course(level * (h + 0.01), level % 2 == 1)
    # A pair of bales pulled off the top, so the stack is being used rather than stored.
    with fy.rotated(bm, 14.0, (0.0, 1.0, 0.0), (0.4, 0.0, 3 * h)):
        box(bm, (0.10, -0.45, 3 * h + 0.01), (0.10 + d, -0.45 + w, 3 * h + 0.01 + h),
            fy.SKIN_STRAW)
    fy.scatter_seed(bm, (0.0, 0.0, 0.0), 1.35, 14, 0.07, fy.SKIN_STRAW, rng)
    return fy.finish(bm, name)


# --------------------------------------------------------------------------------------
# Water and feed
# --------------------------------------------------------------------------------------

def build_water_trough(name="Farm_WaterTrough", length=2.10, width=0.66, height=0.54):
    """A galvanised trough with water in it.

    Built as a solid block with a dish sunk into the top rather than as four walls and a
    floor. A shell has a rim, a rim is a lip, and a lip is the thing a kart wheel climbs.
    The water is its own material slot so it can be given a moving shader later.
    """
    bm = bmesh.new()
    rng = random.Random(SEED + 3)
    hl, hw = length / 2.0, width / 2.0
    wall = 0.09

    # Four sides and a floor, made as boxes that overlap rather than as a hollow shell.
    box(bm, (-hl, -hw, 0.0), (hl, hw, wall), fy.SKIN_METAL)
    for sign in (-1, 1):
        box(bm, (-hl, sign * hw - sign * wall, 0.0), (hl, sign * hw, height),
            fy.SKIN_METAL)
        box(bm, (sign * hl - sign * wall, -hw, 0.0), (sign * hl, hw, height),
            fy.SKIN_METAL)
    # The water, a touch below the rim.
    box(bm, (-hl + wall * 0.6, -hw + wall * 0.6, wall),
        (hl - wall * 0.6, hw - wall * 0.6, height - 0.10), fy.SKIN_WATER)

    # A ball valve at one end, and rust down the seams.
    box(bm, (hl - 0.28, -0.06, height - 0.05), (hl - 0.06, 0.06, height + 0.10),
        fy.SKIN_RUST)
    fy.lathe(bm, [(0.09, 0.0), (0.09, 0.10)], height - 0.20, skin=fy.SKIN_RUST,
             segments=8, centre=(hl - 0.34, 0.0))
    for sign in (-1, 1):
        box(bm, (-hl - 0.02, sign * hw - 0.02, height - 0.09),
            (hl + 0.02, sign * hw + 0.02, height), fy.SKIN_RUST)

    fy.scatter_seed(bm, (0.0, 0.0, 0.0), hw * 1.7, 9, 0.08, fy.SKIN_DIRT, rng)
    return fy.finish(bm, name)


def build_feed_trough(name="Farm_FeedTrough", length=1.70, width=0.50, height=0.46):
    """A timber V-trough on splayed legs, with feed in the bottom.

    The V is two prisms rather than a boolean of a box - the difference matters, because a
    boolean here leaves a coplanar sliver right along the keel where the chamfer would
    then cut a crease that catches the light along the whole length.
    """
    bm = bmesh.new()
    rng = random.Random(SEED + 4)
    hl, hw = length / 2.0, width / 2.0
    keel = 0.20

    for sign in (-1, 1):
        prism(bm, [(-hl, 0.0, keel), (-hl, sign * hw, height),
                   (-hl, sign * (hw - 0.07), height), (-hl, 0.0, keel + 0.075)],
              (length, 0.0, 0.0), fy.SKIN_WOOD)
    # End boards close the V off, and the keel block gives it something to sit on.
    for sign in (-1, 1):
        prism(bm, [(sign * hl - sign * 0.05, 0.0, keel - 0.02),
                   (sign * hl - sign * 0.05, -hw, height),
                   (sign * hl - sign * 0.05, hw, height)],
              (sign * 0.05, 0.0, 0.0), fy.SKIN_WOOD)
    box(bm, (-hl, -0.05, keel - 0.02), (hl, 0.05, keel + 0.06), fy.SKIN_WOOD_DARK)

    # Splayed legs, in from the ends so a wheel meets the end board first.
    for sx in (-1, 1):
        for sy in (-1, 1):
            top = (sx * (hl - 0.22), sy * 0.06, keel + 0.04)
            foot = (sx * (hl - 0.30), sy * (hw - 0.02), 0.0)
            beam(bm, foot, top, 0.06, 0.06, fy.SKIN_WOOD_DARK, up=(0.0, 0.0, 1.0))

    # Feed lying in the bottom.
    box(bm, (-hl + 0.10, -0.16, keel + 0.05), (hl - 0.10, 0.16, keel + 0.13),
        fy.SKIN_STRAW)
    fy.scatter_seed(bm, (0.0, 0.0, 0.0), hw * 1.5, 7, 0.055, fy.SKIN_STRAW, rng)
    return fy.finish(bm, name)


def build_water_pump(name="Farm_WaterPump"):
    """A cast iron hand pump on a stone base, with a bucket under the spout."""
    bm = bmesh.new()
    rng = random.Random(SEED + 5)

    fy.lathe(bm, [(0.42, 0.0), (0.40, 0.16), (0.30, 0.22)], 0.0,
             skin=fy.SKIN_DIRT, segments=8)
    fy.lathe(bm, [(0.13, 0.0), (0.11, 0.72), (0.13, 0.80), (0.12, 0.92)], 0.20,
             skin=fy.SKIN_RUST, segments=8)
    # Spout and handle - between them they are the whole read.
    beam(bm, (0.0, 0.06, 0.98), (0.0, 0.42, 0.86), 0.10, 0.09, fy.SKIN_RUST,
         up=(0.0, 0.0, 1.0))
    beam(bm, (0.0, -0.05, 1.10), (0.0, -0.52, 0.86), 0.05, 0.07, fy.SKIN_RUST,
         up=(0.0, 0.0, 1.0))
    box(bm, (-0.06, -0.10, 1.02), (0.06, 0.02, 1.16), fy.SKIN_RUST)

    # A bucket under the spout, with water in it, and the puddle it has been making for
    # years. The bucket is what gives the prop its second silhouette at ground level -
    # without it the pump is a post with a spout.
    bucket = (0.0, 0.44)
    fy.lathe(bm, [(0.15, 0.0), (0.18, 0.30)], 0.0, skin=fy.SKIN_METAL, segments=8,
             centre=bucket)
    fy.lathe(bm, [(0.165, 0.0), (0.165, 0.025)], 0.24, skin=fy.SKIN_WATER, segments=8,
             centre=bucket)
    box(bm, (-0.19, bucket[1] - 0.03, 0.24), (0.19, bucket[1] + 0.03, 0.30), fy.SKIN_RUST)
    fy.scatter_seed(bm, (0.0, 0.34, 0.0), 0.46, 6, 0.07, fy.SKIN_DIRT, rng)
    return fy.finish(bm, name)


# --------------------------------------------------------------------------------------
# Yard clutter
# --------------------------------------------------------------------------------------

def build_churn(name="Farm_MilkChurn", height=0.78):
    """A milk churn. Knee high, and the pack's clearest small scale reference."""
    bm = bmesh.new()
    fy.lathe(bm, [(0.20, 0.0), (0.22, 0.06), (0.22, height * 0.62),
                  (0.15, height * 0.76), (0.15, height * 0.90), (0.17, height)],
             0.0, skin=fy.SKIN_METAL, segments=10)
    fy.ring(bm, (0.0, 0.0, height * 0.30), (0.0, 0.0, 1.0), 0.215, 0.245, 0.05,
            skin=fy.SKIN_RUST, segments=10)
    fy.lathe(bm, [(0.16, 0.0), (0.13, 0.06)], height, skin=fy.SKIN_RUST, segments=10)
    # Two lugs, which is what stops a churn reading as a bottle.
    for sign in (-1, 1):
        box(bm, (sign * 0.20, -0.05, height * 0.70), (sign * 0.27, 0.05, height * 0.80),
            fy.SKIN_RUST)
    return fy.finish(bm, name)


def build_barrel(name="Farm_Barrel", height=0.86, belly=0.32):
    """A timber barrel with iron hoops - a bulged lathe, not a cylinder.

    The bulge is the point. A straight-sided barrel is a bin, and the whole reason to
    spend a lathe on this rather than a six-sided tube is the three-radius profile.
    """
    bm = bmesh.new()
    fy.lathe(bm, [(belly * 0.84, 0.0), (belly, height * 0.30),
                  (belly, height * 0.70), (belly * 0.84, height)],
             0.0, skin=fy.SKIN_WOOD, segments=10)
    for t in (0.10, 0.46, 0.88):
        r = belly * (0.88 if t < 0.2 or t > 0.8 else 1.0)
        fy.ring(bm, (0.0, 0.0, height * t), (0.0, 0.0, 1.0), r - 0.005, r + 0.022, 0.06,
                skin=fy.SKIN_RUST, segments=10)
    return fy.finish(bm, name)


def build_crate(name="Farm_Crate", w=0.78, d=0.58, h=0.46):
    """A slatted produce crate with apples in it, stacked two high."""
    bm = bmesh.new()
    rng = random.Random(SEED + 6)

    def one(z, turn):
        with fy.rotated(bm, turn, (0.0, 0.0, 1.0), (0.0, 0.0, 0.0)):
            for sx in (-1, 1):
                fy.planks(bm, -d / 2, d / 2, sx * (w / 2 - 0.02), 0.04, z, z + h, 3,
                          skin=fy.SKIN_WOOD, axis="y")
            for sy in (-1, 1):
                fy.planks(bm, -w / 2, w / 2, sy * (d / 2 - 0.02), 0.04, z, z + h, 3,
                          skin=fy.SKIN_WOOD)
            box(bm, (-w / 2, -d / 2, z), (w / 2, d / 2, z + 0.05), fy.SKIN_WOOD_DARK)
            # Corner posts, which is what a slatted box needs to not read as a lantern.
            for sx in (-1, 1):
                for sy in (-1, 1):
                    box(bm, (sx * w / 2 - sx * 0.06, sy * d / 2 - sy * 0.06, z),
                        (sx * w / 2, sy * d / 2, z + h), fy.SKIN_WOOD_DARK)

    one(0.0, 0.0)
    one(h + 0.01, 6.0)
    # Apples in the top crate.
    for _ in range(9):
        x = rng.uniform(-w * 0.36, w * 0.36)
        y = rng.uniform(-d * 0.32, d * 0.32)
        r = rng.uniform(0.045, 0.062)
        z = 2 * h - 0.06
        box(bm, (x - r, y - r, z), (x + r, y + r, z + r * 1.7), fy.SKIN_GREEN)
    return fy.finish(bm, name)


def build_sacks(name="Farm_SackPile"):
    """A pile of feed sacks. Slumped, because a sack that keeps its corners is a box."""
    bm = bmesh.new()
    rng = random.Random(SEED + 7)

    def sack(centre, turn, w, d, h):
        with fy.rotated(bm, turn, (0.0, 0.0, 1.0), centre):
            cx, cy, cz = centre
            # Three stacked slabs, each a little smaller: a cheap slump that costs nothing
            # and reads far better than one tapered box.
            for i, (k, lift) in enumerate(((1.00, 0.0), (0.92, 0.34), (0.72, 0.70))):
                box(bm, (cx - w * k / 2, cy - d * k / 2, cz + h * lift),
                    (cx + w * k / 2, cy + d * k / 2, cz + h * min(1.0, lift + 0.40)),
                    fy.SKIN_DIRT if i == 2 else fy.SKIN_TRIM)
            # The ear where the sack is tied.
            box(bm, (cx - 0.05, cy - d * 0.30, cz + h * 0.96),
                (cx + 0.05, cy + d * 0.30, cz + h * 1.10), fy.SKIN_DIRT)

    sack((0.0, -0.20, 0.0), -8.0, 0.52, 0.34, 0.26)
    sack((0.06, 0.22, 0.0), 12.0, 0.52, 0.34, 0.26)
    sack((-0.02, 0.0, 0.27), 34.0, 0.50, 0.32, 0.24)
    fy.scatter_seed(bm, (0.0, 0.0, 0.0), 0.55, 7, 0.05, fy.SKIN_STRAW, rng)
    return fy.finish(bm, name)


def build_wheelbarrow(name="Farm_Wheelbarrow"):
    """A barrow parked on its legs, half full of muck.

    Tipped forward onto the wheel and the front of the pan, which is how one is actually
    left, and which puts the handles up where they read against the sky.
    """
    bm = bmesh.new()
    rng = random.Random(SEED + 8)
    r = 0.17

    # The pan: a trapezoid cross-section swept back along Y, so it is wide at the rim and
    # narrow at the floor. One prism rather than four walls - a wheelbarrow pan pressed
    # from a single sheet has no seams, and a shell here would give it a rim to catch on.
    prism(bm, [(-0.30, -0.34, 0.44), (0.30, -0.34, 0.44),
               (0.20, -0.34, 0.20), (-0.20, -0.34, 0.20)],
          (0.0, 0.88, 0.0), fy.SKIN_METAL)
    # A rolled lip round the top edge, which is the one place a pressed pan is thick.
    for sx in (-1, 1):
        box(bm, (sx * 0.30 - sx * 0.04, -0.34, 0.40), (sx * 0.30, 0.54, 0.46),
            fy.SKIN_METAL)
    for y in (-0.34, 0.50):
        box(bm, (-0.30, y, 0.40), (0.30, y + 0.04, 0.46), fy.SKIN_METAL)

    # Handles running back past the pan, and the legs they rest on.
    for sx in (-1, 1):
        beam(bm, (sx * 0.26, -0.46, 0.22), (sx * 0.30, 0.92, 0.50),
             0.05, 0.05, fy.SKIN_WOOD, up=(0.0, 0.0, 1.0))
        beam(bm, (sx * 0.27, 0.52, 0.24), (sx * 0.28, 0.56, 0.0),
             0.05, 0.05, fy.SKIN_WOOD_DARK, up=(0.0, 1.0, 0.0))
    # The wheel, and the fork it runs in.
    fy.wheel(bm, (0.0, -0.52, r), (1.0, 0.0, 0.0), r, 0.09, lugs=0,
             rim_fraction=0.45, skin_tyre=fy.SKIN_RUBBER, skin_rim=fy.SKIN_RUST,
             segments=10)
    for sx in (-1, 1):
        beam(bm, (sx * 0.08, -0.52, r), (sx * 0.22, -0.42, 0.24),
             0.04, 0.05, fy.SKIN_RUST, up=(0.0, 0.0, 1.0))

    # Muck in the pan, heaped over the front.
    box(bm, (-0.22, -0.30, 0.24), (0.22, 0.44, 0.36), fy.SKIN_DIRT)
    fy.scatter_seed(bm, (0.0, -0.10, 0.34), 0.20, 5, 0.06, fy.SKIN_DIRT, rng)
    return fy.finish(bm, name)


def build_log_pile(name="Farm_LogPile", length=1.90):
    """Split logs stacked against a pair of stakes. Firewood for the farmhouse."""
    bm = bmesh.new()
    rng = random.Random(SEED + 9)
    rows, per_row = 5, 6
    r = 0.085

    for row in range(rows):
        z = r + row * (r * 1.85)
        wobble = rng.uniform(-0.02, 0.02)
        count = per_row - (1 if row == rows - 1 else 0)
        for i in range(count):
            x = (i - (count - 1) / 2.0) * (r * 2.1) + wobble
            tb.tube(bm, (x, -length / 2, z), (x, length / 2, z), r,
                    skin=fy.SKIN_WOOD, segments=7)
            # The split face, lighter, turned differently on each log.
            if rng.random() < 0.5:
                box(bm, (x - r * 0.7, -length / 2, z + r * 0.4),
                    (x + r * 0.7, length / 2, z + r * 0.95), fy.SKIN_TRIM)

    for sy in (-1, 1):
        for sx in (-1, 1):
            beam(bm, (sx * (per_row * r * 1.08), sy * (length / 2 - 0.12), 0.0),
                 (sx * (per_row * r * 1.08 + 0.06), sy * (length / 2 - 0.12),
                  rows * r * 1.9), 0.06, 0.06, fy.SKIN_WOOD_DARK, up=(0.0, 0.0, 1.0))
    fy.scatter_seed(bm, (0.0, 0.0, 0.0), 0.75, 9, 0.055, fy.SKIN_WOOD, rng)
    return fy.finish(bm, name)


def build_scarecrow(name="Farm_Scarecrow", height=2.05):
    """A scarecrow: cross frame, stuffed shirt, sack head, hat, straw at every cuff.

    Leaned back a few degrees and turned slightly off square. A scarecrow standing plumb
    reads as a signpost with a shirt on, and the whole prop is worth having only because
    it is the one thing on the farm with a human silhouette.
    """
    bm = bmesh.new()
    rng = random.Random(SEED + 10)
    arm = 0.62
    shoulder = height * 0.72

    with fy.rotated(bm, 5.0, (1.0, 0.0, 0.0), (0.0, 0.0, 0.0)):
        beam(bm, (0.0, 0.0, 0.0), (0.0, 0.0, height), 0.075, 0.075, fy.SKIN_WOOD_DARK,
             up=(0.0, 1.0, 0.0))
        beam(bm, (-arm, 0.0, shoulder), (arm, 0.0, shoulder - 0.06),
             0.06, 0.06, fy.SKIN_WOOD_DARK, up=(0.0, 0.0, 1.0))

        # The shirt: a slumped torso, wider at the shoulders than the pole.
        box(bm, (-0.26, -0.15, shoulder - 0.60), (0.26, 0.15, shoulder + 0.06),
            fy.SKIN_PAINT)
        for sx in (-1, 1):
            beam(bm, (sx * 0.22, 0.0, shoulder - 0.01), (sx * (arm - 0.10), 0.0,
                 shoulder - 0.09), 0.14, 0.14, fy.SKIN_PAINT, up=(0.0, 0.0, 1.0))
            # Straw out of each cuff.
            for _ in range(3):
                a = rng.uniform(0.0, math.tau)
                tip = (sx * (arm + 0.11), math.cos(a) * 0.10, shoulder - 0.09 +
                       math.sin(a) * 0.10)
                beam(bm, (sx * (arm - 0.08), 0.0, shoulder - 0.09), tip,
                     0.03, 0.03, fy.SKIN_STRAW, up=(0.0, 0.0, 1.0))

        # Trousers hanging off the torso, and straw out of the bottom of them.
        for sx in (-1, 1):
            beam(bm, (sx * 0.12, 0.0, shoulder - 0.56), (sx * 0.17, 0.02,
                 shoulder - 1.06), 0.17, 0.17, fy.SKIN_WOOD, up=(0.0, 0.0, 1.0))
        fy.scatter_seed(bm, (0.0, 0.0, shoulder - 1.12), 0.22, 4, 0.05, fy.SKIN_STRAW, rng)

        # Sack head, tied at the neck, with a hat over it.
        box(bm, (-0.17, -0.16, shoulder + 0.06), (0.17, 0.16, shoulder + 0.40),
            fy.SKIN_TRIM)
        box(bm, (-0.10, -0.10, shoulder + 0.02), (0.10, 0.10, shoulder + 0.09),
            fy.SKIN_DIRT)
        # Two button eyes and a stitched mouth, which cost eighteen triangles and are the
        # difference between a sack and a face.
        for sx in (-1, 1):
            box(bm, (sx * 0.09 - 0.025, -0.18, shoulder + 0.28),
                (sx * 0.09 + 0.025, -0.15, shoulder + 0.33), fy.SKIN_DARK)
        box(bm, (-0.07, -0.18, shoulder + 0.16), (0.07, -0.15, shoulder + 0.19),
            fy.SKIN_DARK)
        fy.lathe(bm, [(0.30, 0.0), (0.28, 0.03), (0.17, 0.05), (0.17, 0.20),
                      (0.14, 0.23)], shoulder + 0.38, skin=fy.SKIN_STRAW, segments=10)

    fy.scatter_seed(bm, (0.0, 0.0, 0.0), 0.40, 6, 0.06, fy.SKIN_STRAW, rng)
    return fy.finish(bm, name)


def build_signpost(name="Farm_Signpost"):
    """A painted farm sign on two posts - the thing at the end of the drive."""
    bm = bmesh.new()
    for sx in (-1, 1):
        beam(bm, (sx * 0.62, 0.0, 0.0), (sx * 0.62, 0.0, 1.62), 0.09, 0.09,
             fy.SKIN_WOOD_DARK, up=(0.0, 1.0, 0.0))
    box(bm, (-0.86, -0.05, 1.06), (0.86, 0.05, 1.62), fy.SKIN_PAINT)
    box(bm, (-0.90, -0.07, 1.02), (0.90, 0.07, 1.10), fy.SKIN_TRIM)
    box(bm, (-0.90, -0.07, 1.58), (0.90, 0.07, 1.66), fy.SKIN_TRIM)
    # Lettering, as three sunk bars. Not readable and not meant to be - at kart speed a
    # sign reads as "there is writing on that", and modelled glyphs would cost 300 tris
    # to say the same thing.
    for x0, x1, z in ((-0.66, 0.30, 1.42), (-0.66, 0.58, 1.28), (-0.40, 0.20, 1.14)):
        box(bm, (x0, -0.08, z - 0.05), (x1, -0.05, z + 0.05), fy.SKIN_TRIM)
    # A gable cap, so it is a farm sign and not a billboard.
    prism(bm, [(-0.90, -0.07, 1.66), (0.90, -0.07, 1.66), (0.0, -0.07, 1.92)],
          (0.0, 0.14, 0.0), fy.SKIN_ROOF)
    return fy.finish(bm, name)


if __name__ == "__main__":
    manifest = fy.Manifest("farm_props")

    # (builder, triangle budget, collider). The budgets sit about a fifth above what each
    # prop costs today, in the project's usual style: a ceiling that bites, not a wish.
    #
    # Colliders: round and low things a kart is meant to shove past get a box, because a
    # mesh collider on a bale buys nothing at kart speed and costs a broadphase entry per
    # instance. Anything with a real profile a kart should meet honestly keeps its mesh.
    jobs = (
        (build_bale_round, 1500, fy.COLLIDER_BOX),
        (build_bale_square, 520, fy.COLLIDER_BOX),
        (build_hay_stack, 2100, fy.COLLIDER_BOX),
        (build_water_trough, 1050, fy.COLLIDER_MESH),
        (build_feed_trough, 900, fy.COLLIDER_MESH),
        (build_water_pump, 1200, fy.COLLIDER_MESH),
        (build_churn, 1060, fy.COLLIDER_BOX),
        (build_barrel, 1200, fy.COLLIDER_BOX),
        (build_crate, 1800, fy.COLLIDER_BOX),
        (build_sacks, 1000, fy.COLLIDER_BOX),
        (build_wheelbarrow, 1700, fy.COLLIDER_MESH),
        (build_log_pile, 4200, fy.COLLIDER_BOX),
        (build_scarecrow, 1900, fy.COLLIDER_NONE),
        (build_signpost, 420, fy.COLLIDER_MESH),
    )

    for builder, budget, collider in jobs:
        tb.fresh_scene()
        obj, palette = builder()
        stats = tb.build(obj, obj.name, max_tris=budget, max_size_m=3.0)
        manifest.add(stats, palette, collider=collider, tag="prop")

    manifest.write()
