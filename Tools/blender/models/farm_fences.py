"""
Farm fencing, authored as BarrierLine sections rather than as standalone props.

    blender --background --factory-startup --python Tools/blender/models/farm_fences.py

The project already has a system for putting a repeating thing along a drawn route -
`Assets/Barriers` - and it does the hard parts: spacing, mitring, corner fitting, and one
welded zero-friction wall swept down the whole run so a kart slides along a fence instead
of snagging on each post. Fencing that ignored it would be a second, worse version of all
of that. So these are sections, and they follow the two rules that system imposes:

**A section runs down its local Z, which is Blender's +Y.** The export convention maps
Blender X to Unity X, Blender Y to Unity Z and Blender Z to Unity Y (verify_axes.py
asserts exactly that). `BarrierSectionSource.Measure` looks for the model's long axis and
reports a model authored sideways as an error rather than laying it across the line, so
every section here is built along Y.

**A section's extent along Y is its tiling length, so nothing may stick out past it.**
Each section carries its post at the *start* of the run with the post's outer face on
-L/2, and its rails span the full L. Put a post on the centre line at each end instead and
every section overlaps its neighbour by a post thickness, which on a hundred-metre run is
a visible stagger and a lot of coincident geometry inside the swept wall.

The sections carry no colliders in Unity, deliberately - see BarrierLine's Blocking Wall
and the note in Assets/Barriers. Do not helpfully add them.
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


def post(bm, y, height, half=0.075, skin=fy.SKIN_WOOD_DARK, taper=0.0, lean=0.0):
    """One fence post. `lean` tilts it in the YZ plane, which is what stops a long run of
    identical sections reading as a printed pattern."""
    top = (lean, y + lean, height)
    beam(bm, (0.0, y, 0.0), top, half * 2.0, half * 2.0 - taper, skin, up=(0.0, 0.0, 1.0))
    # A weathered cap, so the post end is not a bare square.
    box(bm, (-half - 0.012, y + lean - half - 0.012, height - 0.05),
        (half + 0.012, y + lean + half + 0.012, height + 0.02), skin)


def build_post_rail(name="Farm_FencePostRail", length=2.60, height=1.28, rails=3):
    """Three-rail post and rail - the pack's default paddock fence.

    Rails run the full section length so consecutive sections butt into a continuous line.
    They are also set alternately to either side of the post, which is how a real
    post-and-rail is built and what gives the run its slight zigzag in plan.
    """
    bm = bmesh.new()
    rng = random.Random(SEED)
    half_l = length / 2.0
    p = 0.075

    post(bm, -half_l + p, height, half=p)

    for i in range(rails):
        z = 0.34 + i * ((height - 0.50) / max(1, rails - 1))
        side = 0.055 if i % 2 == 0 else -0.055
        sag = rng.uniform(-0.012, 0.012)
        beam(bm, (side, -half_l, z), (side, half_l, z + sag),
             0.055, 0.135, fy.SKIN_WOOD, up=(0.0, 0.0, 1.0))

    return fy.finish(bm, name)


def build_picket(name="Farm_FencePicket", length=2.00, height=1.02):
    """White picket - the fence that goes round the farmhouse, not round the field.

    Pickets are pointed by a prism rather than left square, because the point is the whole
    read: a run of square-topped slats is a hoarding.
    """
    bm = bmesh.new()
    rng = random.Random(SEED + 1)
    half_l = length / 2.0
    p = 0.06

    post(bm, -half_l + p, height + 0.10, half=p, skin=fy.SKIN_TRIM)

    for z in (0.30, height - 0.24):
        beam(bm, (0.0, -half_l, z), (0.0, half_l, z), 0.045, 0.09, fy.SKIN_TRIM,
             up=(0.0, 0.0, 1.0))

    count = int(length / 0.17)
    for i in range(count):
        y = -half_l + 0.13 + (length - 0.20) * i / max(1, count - 1)
        h = height + rng.uniform(-0.02, 0.02)
        w = 0.048
        box(bm, (-0.028, y - w, 0.10), (0.028, y + w, h - 0.10), fy.SKIN_TRIM)
        prism(bm, [(-0.028, y - w, h - 0.10), (-0.028, y + w, h - 0.10), (-0.028, y, h)],
              (0.056, 0.0, 0.0), fy.SKIN_TRIM)

    return fy.finish(bm, name)


def build_wire(name="Farm_FenceWire", length=3.20, height=1.18):
    """A wire fence on a leaning timber post - the cheap fence that goes round everything.

    The strands are square bars, not cylinders. At this scale a six-sided tube costs six
    times the triangles to render the same two-pixel line, and the pack has a lot of fence.
    """
    bm = bmesh.new()
    rng = random.Random(SEED + 2)
    half_l = length / 2.0
    p = 0.065

    post(bm, -half_l + p, height, half=p, lean=rng.uniform(-0.03, 0.03))
    # A stay leaning back off the post, which is what a strained wire fence needs and what
    # makes the section read as more than a stick with lines on it.
    beam(bm, (0.0, -half_l + p, height - 0.30), (0.0, -half_l + 0.66, 0.04),
         0.05, 0.05, fy.SKIN_WOOD_DARK, up=(0.0, 1.0, 0.0))

    for i in range(4):
        z = 0.26 + i * 0.28
        sag = 0.02 + i * 0.006
        # Two segments with a dip in the middle: one straight bar between posts reads as
        # a taut cable, and no farm fence in the world is taut.
        beam(bm, (0.0, -half_l, z), (0.0, 0.0, z - sag), 0.018, 0.018, fy.SKIN_METAL,
             up=(0.0, 0.0, 1.0))
        beam(bm, (0.0, 0.0, z - sag), (0.0, half_l, z), 0.018, 0.018, fy.SKIN_METAL,
             up=(0.0, 0.0, 1.0))

    return fy.finish(bm, name)


def build_corral(name="Farm_FenceCorral", length=2.80, height=1.55):
    """Heavy corral rails on a squared post - the fence round the stock pen.

    Taller and thicker than the paddock fence, and the one section a kart is most likely
    to meet at speed, so the rails are deep in section: a deep rail chamfers to a visible
    flat, and a visible flat is what a wheel glances off.
    """
    bm = bmesh.new()
    half_l = length / 2.0
    p = 0.105

    post(bm, -half_l + p, height, half=p, skin=fy.SKIN_WOOD_DARK)
    # A capping rail across the post tops ties the run together along the top line.
    beam(bm, (0.0, -half_l, height + 0.02), (0.0, half_l, height + 0.02),
         0.20, 0.07, fy.SKIN_WOOD, up=(0.0, 0.0, 1.0))

    for i in range(3):
        z = 0.40 + i * 0.38
        beam(bm, (0.075, -half_l, z), (0.075, half_l, z), 0.07, 0.17, fy.SKIN_WOOD,
             up=(0.0, 0.0, 1.0))
        # Bolt heads where the rail crosses the post.
        box(bm, (0.10, -half_l + p - 0.04, z - 0.04), (0.16, -half_l + p + 0.04, z + 0.04),
            fy.SKIN_RUST)

    return fy.finish(bm, name)


def build_gate(name="Farm_FenceGate", length=3.40, height=1.32):
    """A five-bar gate, hung open against its post.

    Hung open for the same reason the barn doors are: a closed gate across a section that
    a track might run through is a wall the map maker then has to notice and delete.

    Swung back along the fence line rather than out across it, at 34 degrees. That is what
    an open field gate actually does, and it is also what keeps this section measurable:
    `BarrierSectionSource.Measure` decides which way a section runs by finding its longest
    axis, and a gate standing square to the run was 3.34 m across against a 3.42 m length.
    Eight centimetres from being placed sideways down every fence line on the map.
    """
    bm = bmesh.new()
    half_l = length / 2.0

    # The hanging post is heavier than a fence post, and the shutting post stands at the
    # far end so the opening reads as an opening rather than as a missing section.
    post(bm, -half_l + 0.11, height + 0.34, half=0.11, skin=fy.SKIN_WOOD_DARK)
    post(bm, half_l - 0.09, height + 0.16, half=0.09, skin=fy.SKIN_WOOD_DARK)

    # The gate itself is authored square, spanning the opening, then swung about the
    # hanging post. Writing it pre-rotated would be unreadable and unadjustable.
    hinge = (0.0, -half_l + 0.11, 0.0)
    leaf = length - 0.30
    with fy.rotated(bm, 34.0, (0.0, 0.0, 1.0), hinge):
        y0 = -half_l + 0.20
        y1 = y0 + leaf
        for i in range(5):
            z = 0.22 + i * ((height - 0.34) / 4.0)
            beam(bm, (0.0, y0, z), (0.0, y1, z), 0.05, 0.085, fy.SKIN_WOOD,
                 up=(0.0, 0.0, 1.0))
        for y in (y0, y1):
            beam(bm, (0.0, y, 0.16), (0.0, y, height), 0.06, 0.10, fy.SKIN_WOOD,
                 up=(0.0, 1.0, 0.0))
        # The diagonal brace, rising from the hanging stile - the detail that makes it a
        # five-bar gate rather than a ladder.
        beam(bm, (0.0, y0 + 0.06, 0.22), (0.0, y1 - 0.06, height - 0.14),
             0.05, 0.09, fy.SKIN_WOOD, up=(0.0, 0.0, 1.0))
        # Hinge straps and a latch.
        for z in (0.30, height - 0.22):
            box(bm, (-0.035, y0 - 0.06, z - 0.035), (0.035, y0 + 0.34, z + 0.035),
                fy.SKIN_RUST)
        box(bm, (-0.03, y1 - 0.02, height * 0.55), (0.03, y1 + 0.16, height * 0.55 + 0.06),
            fy.SKIN_RUST)

    return fy.finish(bm, name)


def build_trough_fence(name="Farm_FenceHurdle", length=1.90, height=1.05):
    """A moveable hurdle - the light panel that pens sheep in a corner of a field.

    Its feet stand on the ground rather than in it, so a run of hurdles can be dropped on
    any surface without sinking. That also means it is the one section here that reads
    correctly on rock or concrete as well as on grass.
    """
    bm = bmesh.new()
    half_l = length / 2.0

    for y in (-half_l + 0.06, half_l - 0.06):
        beam(bm, (0.0, y, 0.0), (0.0, y, height), 0.055, 0.055, fy.SKIN_WOOD,
             up=(0.0, 1.0, 0.0))
        # Feet, splayed across the run so the panel stands up on its own.
        box(bm, (-0.24, y - 0.045, 0.0), (0.24, y + 0.045, 0.055), fy.SKIN_WOOD_DARK)

    for i in range(4):
        z = 0.16 + i * ((height - 0.24) / 3.0)
        beam(bm, (0.0, -half_l, z), (0.0, half_l, z), 0.04, 0.07, fy.SKIN_WOOD,
             up=(0.0, 0.0, 1.0))
    beam(bm, (0.0, -half_l + 0.08, 0.18), (0.0, half_l - 0.08, height - 0.10),
         0.035, 0.06, fy.SKIN_WOOD, up=(0.0, 0.0, 1.0))

    return fy.finish(bm, name)


if __name__ == "__main__":
    manifest = fy.Manifest("farm_fences")

    # Budgets sit about a fifth above what each section costs now. These are the highest
    # instance counts in the pack by a wide margin - a field boundary is thirty of them -
    # so the ceiling matters more here than on any building.
    for builder, budget in (
        (build_post_rail, 1400),
        (build_picket, 3000),
        (build_wire, 1500),
        (build_corral, 1500),
        (build_gate, 3000),
        (build_trough_fence, 1500),
    ):
        tb.fresh_scene()
        obj, palette = builder()
        stats = tb.build(obj, obj.name, max_tris=budget, max_size_m=4.5)
        # No collider: BarrierLine's swept Blocking Wall is the only thing a kart touches
        # on a barrier run, and a collider per section is exactly the saw of corners that
        # system exists to avoid.
        manifest.add(stats, palette, collider=fy.COLLIDER_NONE, tag="fence",
                     note="BarrierLine section; runs down local Z")

    manifest.write()
