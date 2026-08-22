"""
Cabins - stylised timber-framed houses, and the burnt-out shell of one.

    blender --background --factory-startup --python Tools/blender/models/cabin.py

Exports three meshes from one script, the way kart_buggy.py does, because they are one
family sharing every dimension and every helper below:

    Cabin_A       the full house: porch, two dormers, chimney, shuttered windows
    Cabin_B       a smaller one-room cabin with a woodshed lean-to, no dormers
    Cabin_Burnt   Cabin_A after the fire: roof mostly gone, walls charred, embers left

The look is the point. The medieval-town buildings already in the scene are mitred to
razor edges, which reads as a different art style standing next to the terrain, the
volcanic rocks and the karts - all of which are faceted solids with *chamfered* corners.
So every part here is a solid box or prism and the whole mesh gets one bevel pass at the
end (`CHAMFER`). One pass rather than per-part, so a post meeting a rail chamfers to the
same width as the rail: that is what makes a building read as one carved object instead
of an assembly of separate boxes.

It is also why nothing here is modelled as a plane or a cut-out - a chamfer needs
thickness to bite into. Wall openings are made by decomposing the wall into boxes around
them rather than by boolean, which keeps every face a quad and every edge bevellable.

Numbers are metres and they are architectural rather than arbitrary: a 2.55 m wall, a
2.03 m door, a sill at 1.30 m. A kart is 1.24 m across its front track, so the door is
deliberately too small to drive through, and the porch posts stand inside the eaves where
a kart clipping the corner glances off the stone plinth rather than catching a post.
"""

import math
import os
import random
import sys
from contextlib import contextmanager

import bmesh
import bpy
from mathutils import Matrix, Vector

sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
import toebeans_blender as tb  # noqa: E402

NAME_A = "Cabin_A"
NAME_B = "Cabin_B"
NAME_BURNT = "Cabin_Burnt"

# Everything random here - the quoin sizes, the course jitter, where the debris fell -
# comes off this, so the same seed is the same cabin every time. Same rule as the rock.
SEED = 20260818

# The chamfer applied once to the finished mesh. 0.03 m is wide enough to catch a
# highlight on every corner at kart speed and narrow enough that a 0.07 m plank still has
# a flat left in the middle - hence MIN_PART, which every part below respects.
CHAMFER = 0.03
CHAMFER_SEGMENTS = 1   # one segment: a cut corner, not a rounded one. Same as the rocks.
MIN_PART = 0.07        # thinner than this and the chamfer eats the part entirely

# --------------------------------------------------------------------------------------
# Material slots.
#
# Slot order is the same for every cabin in the family, so a variant's materials can be
# matched by slot as well as by name. Slot 4 is "the part that glows": lit glass on a
# standing cabin, embers on a burnt one. Same slot, same job, different material.
# --------------------------------------------------------------------------------------

SKIN_STONE = 0      # plinth, chimney, threshold
SKIN_WALL = 1       # the plaster/daub infill panels
SKIN_TIMBER = 2     # posts, rails, braces, rafters, planks, frames
SKIN_ROOF = 3       # shingles and roof deck
SKIN_GLOW = 4       # window glass / embers
SKIN_IRON = 5       # hinges, handles, straps
SKIN_INTERIOR = 6   # what you see through an opening - never a hole to the skybox

PALETTE_INTACT = [
    ("CabinStone", (0.40, 0.39, 0.38), 0.00, 0.85),
    ("CabinPlaster", (0.74, 0.70, 0.62), 0.00, 0.90),
    ("CabinTimber", (0.20, 0.13, 0.09), 0.00, 0.75),
    ("CabinShingle", (0.29, 0.22, 0.20), 0.00, 0.80),
    ("CabinGlass", (0.95, 0.72, 0.34), 0.00, 0.25),
    ("CabinIron", (0.13, 0.13, 0.14), 0.80, 0.45),
    ("CabinInterior", (0.05, 0.04, 0.04), 0.00, 0.95),
]

PALETTE_BURNT = [
    ("BurntStone", (0.22, 0.21, 0.20), 0.00, 0.88),
    ("BurntPlaster", (0.26, 0.23, 0.21), 0.00, 0.92),
    ("BurntTimber", (0.07, 0.06, 0.06), 0.00, 0.88),
    ("BurntShingle", (0.11, 0.09, 0.09), 0.00, 0.88),
    ("BurntEmber", (1.00, 0.38, 0.06), 0.00, 0.40),
    ("BurntIron", (0.10, 0.09, 0.09), 0.70, 0.60),
    ("BurntInterior", (0.03, 0.02, 0.02), 0.00, 0.96),
]
# --------------------------------------------------------------------------------------
# Solids
#
# These moved into toebeans_blender when the farm pack needed the same four. Kept as
# module-level names here rather than spelled `tb.box` at every call site, because this
# file places several hundred parts and the prefix at each of them earns nothing.
# --------------------------------------------------------------------------------------

box = tb.box            # an axis-aligned box given opposite corners
beam = tb.beam          # a box whose long axis runs a->b, cross-section w x h
prism = tb.prism        # a convex polygon extruded into a solid - gable ends, mostly
moved = tb.moved        # build upright inside the block, then knock it over
spin = tb.spin          # a rotation about an axis through a pivot, for use with moved



# --------------------------------------------------------------------------------------
# Elevations
# --------------------------------------------------------------------------------------

class Wall:
    """One face of the building, so wall furniture is written in that wall's own frame.

    `u` runs along the wall, `z` is world height, `w` is outward from the wall's centre
    plane. A window authored at u=1.95 lands in the same place whichever wall it is hung
    on, which is what stops four elevations quietly drifting apart from each other.
    """

    def __init__(self, axis, sign, plane, thickness):
        if axis not in ("x", "y"):
            raise ValueError(f"wall normal axis must be 'x' or 'y', got {axis!r}")
        self.axis = axis        # the axis the wall's normal lies on
        self.sign = sign        # which way along it is outward
        self.plane = plane      # where the wall's centre plane sits on that axis
        self.t = thickness

    @property
    def out(self):
        """The `w` of the outside face - what trim and framing sit against."""
        return self.t * 0.5

    def p(self, u, z, w=0.0):
        """A point in the wall's frame, as world coordinates."""
        n = self.plane + self.sign * w
        return Vector((u, n, z)) if self.axis == "y" else Vector((n, u, z))

    def box(self, bm, u0, u1, z0, z1, w0, w1, skin=0):
        box(bm, self.p(u0, z0, w0), self.p(u1, z1, w1), skin)

    def beam(self, bm, ua, za, ub, zb, w, thick, depth, skin=0):
        """A member lying in the wall plane, `thick` through the wall, `depth` across it."""
        a = self.p(ua, za, w)
        b = self.p(ub, zb, w)
        up = Vector((0.0, 1.0, 0.0)) if self.axis == "y" else Vector((1.0, 0.0, 0.0))
        beam(bm, a, b, depth, thick, skin, up=up)

    def spin_axis(self):
        """The vertical axis to swing a shutter or a door about, in world terms."""
        return Vector((0.0, 0.0, 1.0))


def panels(bm, wall, u0, u1, z0, z1, openings, skin=SKIN_WALL):
    """A wall with holes in it, decomposed into boxes rather than cut with a boolean.

    Booleans on this geometry produce n-gons and coplanar slivers, and the bevel pass at
    the end turns both into the kind of shading artefact you only notice in Unity. The
    decomposition is: a full-height pier either side of each opening, then a panel under
    the opening and one over it. Openings must not overlap along `u`.
    """
    w0, w1 = -wall.out, wall.out
    for (ou0, ou1, oz0, oz1) in sorted(openings, key=lambda o: o[0]):
        if oz0 > z0 + 1e-4:
            wall.box(bm, ou0, ou1, z0, oz0, w0, w1, skin)
        if z1 > oz1 + 1e-4:
            wall.box(bm, ou0, ou1, oz1, z1, w0, w1, skin)
    for (pu0, pu1) in piers(u0, u1, openings):
        wall.box(bm, pu0, pu1, z0, z1, w0, w1, skin)


def runs(indices):
    """Consecutive stretches in a set of course numbers, as (first, last) pairs.

    An intact roof is one run and needs one rake board; a burnt one is several and needs
    a board over each, which is what stops the rake spanning a hole in the roof.
    """
    out = []
    for i in sorted(indices):
        if out and i == out[-1][1] + 1:
            out[-1][1] = i
        else:
            out.append([i, i])
    return [tuple(r) for r in out]


def piers(u0, u1, openings):
    """The full-height stretches of wall left between the openings.

    Shared by `panels`, which fills them with plaster, and `add_frame`, which puts the
    braces in them. Deriving both from one function is what guarantees a brace never
    lands across a window, however the openings are moved.
    """
    out = []
    cursor = u0
    for (ou0, ou1, _z0, _z1) in sorted(openings, key=lambda o: o[0]):
        if ou0 < cursor - 1e-4:
            raise ValueError(f"overlapping wall openings at u={ou0:.3f}")
        if ou0 > cursor + 1e-4:
            out.append((cursor, ou0))
        cursor = ou1
    if u1 > cursor + 1e-4:
        out.append((cursor, u1))
    return out


# --------------------------------------------------------------------------------------
# The specification
# --------------------------------------------------------------------------------------

class Damage:
    """What the fire took. Only Cabin_Burnt carries one; everything else gets None.

    Kept as data rather than as a separate builder so the ruin is demonstrably the same
    house - same footprint, same frame, same chimney - rather than a second model that
    merely resembles it. A player should be able to stand one beside the other.
    """

    def __init__(self, keep_courses, gable_left, seed):
        self.keep_courses = keep_courses    # {slope sign: set of surviving shingle rows}
        self.gable_left = gable_left        # fraction of the gable triangle still standing
        self.seed = seed


class Spec:
    """Every dimension of one cabin. Derived heights are computed once, here."""

    def __init__(self, name, palette, width, depth, wall_h, ridge_rise,
                 plinth_h=0.42, wall_t=0.28, eave=0.50, rake=0.42, courses=8,
                 win_w=1.00, win_h=1.05, sill=1.30, door_w=1.24, door_h=2.03,
                 dormers=(), porch=False, chimney=None, chimney_rise=0.75,
                 lean_to=False, damage=None):
        self.name = name
        self.palette = palette
        self.width = width              # along the ridge (X)
        self.depth = depth              # gable to gable (Y)
        self.wall_h = wall_h            # plinth top to wall plate top
        self.ridge_rise = ridge_rise    # wall plate to ridge
        self.plinth_h = plinth_h
        self.wall_t = wall_t
        self.eave = eave                # roof overhang past the long walls
        self.rake = rake                # roof overhang past the gable walls
        self.courses = courses          # shingle rows per slope
        self.win_w = win_w
        self.win_h = win_h
        self.sill = sill                # height of the window sill above the ground
        self.door_w = door_w
        self.door_h = door_h
        self.dormers = tuple(dormers)   # X positions on the front slope
        self.porch = porch
        self.chimney = chimney          # +1 or -1 for which gable, None for no chimney
        self.chimney_rise = chimney_rise  # how far the stack clears the ridge
        self.lean_to = lean_to
        self.damage = damage

        self.wall_top = plinth_h + wall_h
        self.ridge_z = self.wall_top + ridge_rise
        self.half_w = width / 2.0
        self.half_d = depth / 2.0
        # How fast the roof climbs per metre inboard. Drives the eave drop and the rake.
        self.pitch = ridge_rise / self.half_d
        self.roof_w = width + 2.0 * rake
        # Where a front bay sits: midway between the door jamb and the corner. The front
        # windows and the dormers over them both come off this, so a change of width
        # moves all four together instead of three of them.
        self.bay_x = (door_w / 2.0 + self.half_w) / 2.0

    def wall(self, axis, sign):
        plane = self.sign_plane(axis, sign)
        return Wall(axis, sign, plane, self.wall_t)

    def sign_plane(self, axis, sign):
        return sign * (self.half_w if axis == "x" else self.half_d)


class Slope:
    """One pitch of the main roof, as a frame you can lay courses along.

    `t` runs up the slope from the eave, `lift` is clear of the deck's centre plane, and
    the deck, the shingles, the rafters and the barge boards are all placed in it - so a
    change of pitch moves every one of them together instead of most of them.
    """

    def __init__(self, s, sign):
        self.sign = sign                            # -1 falls toward -Y (the front)
        eave_y = sign * (s.half_d + s.eave)
        eave_z = s.wall_top - s.eave * s.pitch
        self.eave = Vector((0.0, eave_y, eave_z))
        run = Vector((0.0, -eave_y, s.ridge_z - eave_z))
        self.length = run.length
        self.d = run / self.length
        # Outward roof normal: the slope direction turned a quarter turn away from the
        # building. The -sign is what keeps "outward" outward on both pitches.
        self.n = Vector((0.0, -self.d.z, self.d.y)) * -sign

    def at(self, t, lift=0.0, x=0.0):
        p = self.eave + self.d * t + self.n * lift
        return Vector((x, p.y, p.z))


# --------------------------------------------------------------------------------------
# Parts
# --------------------------------------------------------------------------------------

def add_plinth(bm, s, rng):
    """A stone footing, oversailing the walls so rain leaves the plaster alone.

    Also the part a kart actually hits. It is a single chamfered ring rather than
    individual stones for that reason: a wheel sliding along it should be deflected, not
    caught between two boulders.
    """
    over = 0.16
    x, y, h = s.half_w + over, s.half_d + over, s.plinth_h
    box(bm, (-x, -y, 0.0), (x, y, h * 0.62), SKIN_STONE)
    # A second, slightly narrower course, so the plinth steps in rather than reading as
    # one slab. The step is 0.05 - deep enough to see, too shallow to trip a wheel.
    box(bm, (-x + 0.05, -y + 0.05, h * 0.55), (x - 0.05, y - 0.05, h), SKIN_STONE)

    # Quoins: bigger corner stones, the one place hand-laid stonework shows at distance.
    for sx in (-1, 1):
        for sy in (-1, 1):
            d = rng.uniform(0.30, 0.42)
            box(bm,
                (sx * (x - d), sy * (y - 0.06), 0.02),
                (sx * (x + 0.07), sy * (y + 0.07), h * rng.uniform(0.72, 0.98)),
                SKIN_STONE)


def add_window(bm, s, wall, u0, u1, z0, z1, shutters=True, cross=True, glazed=True,
               skin=SKIN_TIMBER):
    """A framed opening: reveal, frame, sill, glazing bars, glass and shutters.

    The interior backing is not optional. An unbacked opening in a low-poly building shows
    the skybox through the far wall the moment the camera drops below the eaves, and it is
    the single thing that most makes a scripted building look unfinished.
    """
    out = wall.out
    wall.box(bm, u0 - 0.05, u1 + 0.05, z0 - 0.05, z1 + 0.05,
             -out - 0.10, -out + 0.06, SKIN_INTERIOR)

    fw = 0.11                     # how far the frame laps over the opening
    fa, fb = out - 0.05, out + 0.09
    wall.box(bm, u0 - fw, u0, z0 - fw, z1 + fw, fa, fb, skin)     # left jamb
    wall.box(bm, u1, u1 + fw, z0 - fw, z1 + fw, fa, fb, skin)     # right jamb
    wall.box(bm, u0, u1, z1, z1 + fw, fa, fb, skin)               # head
    wall.box(bm, u0, u1, z0 - fw, z0, fa, fb, skin)               # apron

    # Sill, proud of the frame and wider than the opening, so water leaves the wall.
    wall.box(bm, u0 - 0.22, u1 + 0.22, z0 - 0.21, z0 - 0.07, out - 0.07, out + 0.21, skin)

    if glazed:
        wall.box(bm, u0, u1, z0, z1, -0.03, 0.05, SKIN_GLOW)

    if cross:
        um, zm = (u0 + u1) * 0.5, (z0 + z1) * 0.5
        wall.box(bm, um - 0.04, um + 0.04, z0, z1, out - 0.06, out + 0.05, skin)
        wall.box(bm, u0, u1, zm - 0.04, zm + 0.04, out - 0.06, out + 0.05, skin)

    if not shutters:
        return

    leaf = (u1 - u0) * 0.56
    for side in (-1, 1):
        # Hinged at the outer edge and swung back against the wall. The swing is what
        # stops a pair of shutters reading as two panels painted on the plaster.
        hinge_u = (u0 - 0.13) if side < 0 else (u1 + 0.13)
        # The leaf swings *away* from the opening and its free edge comes off the wall.
        # Rotating the other way folds a shutter across the window it is meant to frame.
        with moved(bm, spin(wall.p(hinge_u, z0, out), wall.spin_axis(),
                            side * wall.sign * 12.0)):
            a = hinge_u
            b = hinge_u + side * leaf
            lo, hi = min(a, b), max(a, b)
            plank = (hi - lo) / 2.0
            for i in range(2):
                wall.box(bm, lo + i * plank + 0.012, lo + (i + 1) * plank - 0.012,
                         z0 - 0.04, z1 + 0.04, out + 0.05, out + 0.13, skin)
            # Diagonal ledger across the planks, the way a real board shutter is held.
            wall.beam(bm, lo + 0.05, z0 + 0.02, hi - 0.05, z1 + 0.02,
                      out + 0.14, 0.09, 0.10, skin)
            wall.box(bm, hinge_u - 0.09, hinge_u + 0.09, z1 - 0.16, z1 - 0.02,
                     out + 0.04, out + 0.16, SKIN_IRON)


def add_door(bm, s, wall, u0, u1, z0, z1, hanging=False, skin=SKIN_TIMBER):
    """A ledged and braced plank door in a heavy frame, with a stone threshold."""
    out = wall.out
    wall.box(bm, u0 - 0.06, u1 + 0.06, z0, z1 + 0.06,
             -out - 0.12, -out + 0.06, SKIN_INTERIOR)

    fw = 0.14
    fa, fb = out - 0.05, out + 0.10
    wall.box(bm, u0 - fw, u0, z0, z1 + fw, fa, fb, skin)
    wall.box(bm, u1, u1 + fw, z0, z1 + fw, fa, fb, skin)
    wall.box(bm, u0 - fw, u1 + fw, z1, z1 + fw, fa, fb, skin)

    # Threshold. Stone, because the doorstep is the one part of a timber house that gets
    # walked on, and a timber one would read as unweathered next to the plinth.
    wall.box(bm, u0 - 0.24, u1 + 0.24, z0 - 0.06, z0 + 0.06,
             out - 0.10, out + 0.34, SKIN_STONE)

    hinge = wall.p(u0, z0, out)
    swing = Matrix.Identity(4)
    if hanging:
        # Off the bottom hinge and leaning out of its frame. Rotated about the jamb, then
        # tipped along the wall, because a door that has burnt off one hinge does both.
        swing = (spin(hinge, wall.spin_axis(), -wall.sign * 24.0)
                 @ spin(hinge, (1.0, 0.0, 0.0) if wall.axis == "y" else (0.0, 1.0, 0.0),
                        -6.0))

    with moved(bm, swing):
        planks = 5
        span = (u1 - u0) - 0.06
        pw = span / planks
        for i in range(planks):
            a = u0 + 0.03 + i * pw
            wall.box(bm, a + 0.012, a + pw - 0.012, z0 + 0.02, z1 - 0.02,
                     out - 0.12, out - 0.035, skin)
        for zb in (z0 + 0.28, z1 - 0.30):
            wall.box(bm, u0 + 0.03, u1 - 0.03, zb - 0.07, zb + 0.07,
                     out - 0.14, out - 0.10, skin)
        wall.beam(bm, u0 + 0.10, z0 + 0.33, u1 - 0.10, z1 - 0.35,
                  out - 0.12, 0.05, 0.13, skin)
        for zb in (z0 + 0.28, z1 - 0.30):
            wall.box(bm, u0 - 0.02, u0 + 0.44, zb - 0.055, zb + 0.055,
                     out - 0.16, out - 0.11, SKIN_IRON)
        wall.box(bm, u1 - 0.24, u1 - 0.10, z0 + 1.02, z0 + 1.16,
                 out - 0.20, out - 0.10, SKIN_IRON)


def add_frame(bm, s, wall, u0, u1, openings=(), braces=True, skin=SKIN_TIMBER):
    """The exposed timber frame over an elevation: sill, mid rail, plate and braces.

    Proud of the plaster by 0.08, which is what the chamfer needs in order to read the
    frame as standing off the panel rather than as a stripe painted on it.

    The braces go in the piers between openings and nowhere else, alternating direction
    bay to bay. That zig-zag is the thing the eye actually reads as "timber framed", and
    keeping it out of the openings is free because `piers` already knows where they are.
    """
    out = wall.out
    a, b = out - 0.04, out + 0.08
    top = s.wall_top
    mid = s.plinth_h + s.wall_h * 0.52

    wall.box(bm, u0, u1, s.plinth_h, s.plinth_h + 0.19, a, b, skin)     # sill beam
    wall.box(bm, u0, u1, mid - 0.09, mid + 0.09, a, b, skin)            # mid rail
    wall.box(bm, u0, u1, top - 0.21, top, a, b, skin)                   # wall plate

    if not braces:
        return

    # A blind wall is one enormous pier, and one brace across the whole of it reads as a
    # scaffold pole rather than as framing. Studs break it into bays no wider than a man
    # can reach, which is what sets the spacing on a real frame too.
    bays = []
    for (pu0, pu1) in piers(u0, u1, openings):
        count = max(1, int(math.ceil((pu1 - pu0) / 1.45)))
        span = (pu1 - pu0) / count
        for i in range(count):
            bays.append((pu0 + i * span, pu0 + (i + 1) * span))
            if i:
                u = pu0 + i * span
                wall.box(bm, u - 0.08, u + 0.08, s.plinth_h, top, a, b, skin)

    # 0.24 of clearance either side: enough to clear a window sill, which oversails its
    # opening further than the frame does.
    for i, (pu0, pu1) in enumerate(bays):
        pu0, pu1 = pu0 + 0.24, pu1 - 0.24
        if pu1 - pu0 < 0.26:
            continue
        lean = 1 if i % 2 == 0 else -1
        lo, hi = (pu0, pu1) if lean > 0 else (pu1, pu0)
        wall.beam(bm, lo, s.plinth_h + 0.20, hi, mid - 0.10, out + 0.02, 0.10, 0.16, skin)
        if top - 0.22 - (mid + 0.10) > 0.30:
            wall.beam(bm, hi, mid + 0.10, lo, top - 0.22, out + 0.02, 0.10, 0.16, skin)


def add_posts(bm, s, skin=SKIN_TIMBER):
    """Corner posts. Slightly fatter than the rails, and standing proud of both walls."""
    p = 0.15
    for sx in (-1, 1):
        for sy in (-1, 1):
            box(bm,
                (sx * (s.half_w + 0.09), sy * (s.half_d + 0.09), s.plinth_h - 0.02),
                (sx * (s.half_w - p), sy * (s.half_d - p), s.wall_top + 0.04),
                skin)


def add_gable(bm, s, sign, skin=SKIN_WALL, timber=SKIN_TIMBER):
    """The triangle above the wall plate, plus the framing that holds it up.

    Truncated to a trapezoid when the fire took the top of it, which is what `gable_left`
    on Damage means. A collapsed gable is the clearest read that a roof is gone.
    """
    plane = sign * s.half_w
    face = plane + sign * s.wall_t * 0.5
    push = (-sign * s.wall_t, 0.0, 0.0)
    top = s.wall_top
    left = 1.0 if s.damage is None else s.damage.gable_left
    peak_z = top + s.ridge_rise * left

    if left >= 0.999:
        pts = [(face, -s.half_d, top), (face, s.half_d, top), (face, 0.0, peak_z)]
    else:
        # Where the pitch has reached `peak_z`, measured back from each eave.
        inset = s.half_d * left
        pts = [(face, -s.half_d, top), (face, s.half_d, top),
               (face, inset, peak_z), (face, -inset, peak_z)]
    prism(bm, pts, push, skin)

    # King post, struts and tie beam, on the outside face where they can be seen. The
    # 0.05 offset with a 0.14 depth leaves them buried 0.02 into the plaster - a member
    # that only touches its wall leaves a hairline of daylight along its whole length.
    w = face + sign * 0.05
    up = (1.0, 0.0, 0.0)
    beam(bm, (w, 0.0, top), (w, 0.0, peak_z - 0.05), 0.17, 0.14, timber, up=up)
    for sy in (-1, 1):
        beam(bm, (w, sy * (s.half_d - 0.25), top + 0.06),
             (w, sy * 0.17, peak_z - 0.44), 0.15, 0.14, timber, up=up)
    beam(bm, (w, -s.half_d, top + 0.03), (w, s.half_d, top + 0.03), 0.22, 0.14, timber,
         up=up)


def add_roof(bm, s, rng, skin=SKIN_ROOF, timber=SKIN_TIMBER):
    """Deck, shingle courses, ridge cap, barge boards - and rafters where the deck is gone.

    Shingles are modelled as overlapping courses rather than as individual tiles. At kart
    distance the silhouette of the course edge is the whole read, and eight stepped rows
    per pitch cost a twentieth of what four hundred tiles would.
    """
    dmg = s.damage
    deck_t, course_t = 0.14, 0.105
    half_x = s.roof_w * 0.5

    # Each course is *tilted*: its butt rides up on the tail of the course below it and
    # its head lies almost flat on the deck, where the next course covers it. That tilt
    # is the whole trick. Courses laid parallel to the deck all sit at one height and
    # merge into a single flat slab no matter how much they overlap - which is exactly
    # what a shingled roof must not look like. Tilting them steps every course line by
    # about one shingle thickness without the roof getting thicker as it climbs.
    lift_butt = deck_t * 0.5 + course_t * 1.05
    lift_head = deck_t * 0.5 + course_t * 0.10

    for sign in (-1, 1):
        sl = Slope(s, sign)
        step = sl.length / s.courses
        keep = set(range(s.courses)) if dmg is None else dmg.keep_courses[sign]

        # Rafters and purlins. Only worth building where the deck is missing, since
        # otherwise they are enclosed geometry nobody will ever see.
        if dmg is not None:
            for x in (-s.half_w + 0.2, -s.half_w * 0.5, 0.0, s.half_w * 0.5,
                      s.half_w - 0.2):
                stop = sl.length * (0.55 if abs(x) < 0.1 else rng.uniform(0.7, 1.0))
                beam(bm, sl.at(0.02, -deck_t * 0.5, x), sl.at(stop, -deck_t * 0.5, x),
                     0.12, 0.22, timber, up=sl.n)
            for t in (sl.length * 0.34, sl.length * 0.72):
                beam(bm, sl.at(t, -deck_t * 0.9, -half_x + 0.2),
                     sl.at(t, -deck_t * 0.9, half_x - 0.2), 0.14, 0.14, timber, up=sl.n)

        if dmg is None:
            # One clean deck under an intact roof: cheaper, and never seen anyway.
            beam(bm, sl.at(-0.05), sl.at(sl.length + 0.06), s.roof_w, deck_t, skin,
                 up=sl.n)
        else:
            for k in sorted(keep):
                beam(bm, sl.at(k * step - 0.02), sl.at((k + 1) * step + 0.02),
                     s.roof_w, deck_t, skin, up=sl.n)

        # Each course is laid in three panels with a hair of daylight between them and a
        # little jitter on each. One unbroken slab per course gives the roof a set of
        # printed stripes; three that do not quite line up give it a roof that somebody
        # laid by hand, which is the difference the rest of the map is already making.
        for k in sorted(keep):
            t0 = k * step
            lip = 0.10 if k == 0 else 0.0
            panel = s.roof_w / 3.0
            for i in range(3):
                cx = -half_x + panel * (i + 0.5)
                nudge = rng.uniform(-0.014, 0.014)
                beam(bm, sl.at(t0 - lip, lift_butt + nudge, cx),
                     sl.at(t0 + step + rng.uniform(0.16, 0.26), lift_head + nudge, cx),
                     panel - rng.uniform(0.012, 0.045), course_t, skin, up=sl.n)

        # Rafter tails under the overhang. The underside of an eave is at eye level from
        # a kart, and a bare soffit is the giveaway that a roof is one extruded slab.
        if dmg is None:
            for i in range(7):
                x = -s.half_w + 0.25 + i * (s.width - 0.5) / 6.0
                beam(bm, sl.at(-0.03, -deck_t * 0.5 - 0.07, x),
                     sl.at(0.62, -deck_t * 0.5 - 0.07, x), 0.11, 0.15, timber, up=sl.n)

        # Barge boards down the rake, closing the end grain of the courses. Deep enough
        # to cover the deck underneath and the tilted courses on top of it, and cut to
        # the stretches of roof that are still there - a rake board spanning a hole is
        # the thing that makes a burnt roof read as intact from the gable end.
        for (k0, k1) in runs(keep):
            t_a = k0 * step - (0.12 if k0 == 0 else 0.0)
            t_b = (k1 + 1) * step + 0.02
            for sx in (-1, 1):
                beam(bm, sl.at(t_a, deck_t * 0.28, sx * (half_x - 0.06)),
                     sl.at(t_b, deck_t * 0.28, sx * (half_x - 0.06)),
                     0.13, 0.44, timber, up=sl.n)

    ridge_ok = dmg is None or (s.courses - 1 in dmg.keep_courses[-1]
                               and s.courses - 1 in dmg.keep_courses[1])
    if ridge_ok:
        box(bm, (-half_x, -0.20, s.ridge_z + 0.04), (half_x, 0.20, s.ridge_z + 0.22),
            skin)
    elif dmg is not None:
        # The ridge beam survives the boards it carried. It is the last thing standing on
        # a burnt roof and the strongest silhouette the ruin has.
        beam(bm, (-s.half_w - 0.1, 0.0, s.ridge_z - 0.10),
             (s.half_w + 0.1, 0.0, s.ridge_z - 0.10), 0.16, 0.20, timber)


def add_dormer(bm, s, xc, rng, wreck=0):
    """A gabled dormer: front wall with its own window, cheeks, roof and barge boards.

    Its face sits 0.30 m back from the wall below so the main eave still overhangs it,
    and it buries into the main pitch at the back rather than being fitted to it - the
    intersection is hidden under the dormer's own roof either way.

    `wreck` is what the fire left: 0 whole, 1 burnt down to a stub with no roof at all,
    2 still standing with one pitch gone. Two dormers damaged two different ways is what
    stops the ruin looking like the same asset stamped twice.
    """
    dw, y_f = 1.28, -s.half_d + 0.30
    z_bot = s.wall_top - 0.55
    z_eave = 3.86 if wreck != 1 else 3.34
    z_ridge = 4.44
    y_back = -0.70
    glazed = s.damage is None
    face = Wall("y", -1, y_f, 0.20)

    win = (xc - 0.42, xc + 0.42, 3.04, min(3.70, z_eave))
    panels(bm, face, xc - dw / 2.0, xc + dw / 2.0, z_bot, z_eave, [win])
    add_window(bm, s, face, *win, shutters=False, cross=glazed, glazed=glazed)

    for sx in (-1, 1):
        cheek = xc + sx * dw / 2.0
        box(bm, (cheek - sx * 0.10, y_f, z_bot), (cheek, y_back, z_eave), SKIN_WALL)

    if wreck == 1:
        # Charred stubs of the studs, and nothing above them.
        for u in (xc - dw / 2.0 + 0.09, xc, xc + dw / 2.0 - 0.09):
            face.box(bm, u - 0.08, u + 0.08, z_eave - 0.05,
                     z_eave + rng.uniform(0.14, 0.42), -0.10, 0.14, SKIN_TIMBER)
        return

    # Gable triangle over the window, on the outer face of the front wall.
    fy = y_f - 0.10
    prism(bm, [(xc - dw / 2.0, fy, z_eave), (xc + dw / 2.0, fy, z_eave),
               (xc, fy, z_ridge)], (0.0, 0.20, 0.0), SKIN_WALL)

    # Roof: two pitches ridged along Y, so it sheds across the main pitch, not into it.
    over = 0.20
    run = dw / 2.0 + over
    rise = z_ridge - z_eave
    y_mid = (y_f - 0.16 + y_back) * 0.5
    length = abs(y_back - (y_f - 0.16))
    for sx in (-1, 1):
        d = Vector((-sx * run, 0.0, rise))
        d.normalize()
        n = Vector((sx * abs(d.z), 0.0, abs(d.x)))
        a = Vector((xc + sx * run, y_mid, z_eave - 0.16))
        b = Vector((xc, y_mid, z_ridge))
        if wreck == 2 and sx < 0:
            # That pitch went; its rafters did not. Leave them stepping into thin air.
            for i in range(3):
                off = Vector((0.0, -0.45 + i * 0.45, 0.0))
                beam(bm, a + off, b + off, 0.09, 0.12, SKIN_TIMBER, up=n)
            continue
        beam(bm, a + n * 0.05, b + n * 0.05, length, 0.11, SKIN_ROOF, up=n)
        beam(bm, a - n * 0.03, b - n * 0.03, length - 0.06, 0.09, SKIN_ROOF, up=n)
        # Barge board on the front end of that pitch only - the back end is buried.
        beam(bm, Vector((xc + sx * run, y_f - 0.20, z_eave - 0.16)),
             Vector((xc, y_f - 0.20, z_ridge)), 0.12, 0.22, SKIN_TIMBER, up=n)
    box(bm, (xc - 0.13, y_f - 0.22, z_ridge - 0.04), (xc + 0.13, y_back, z_ridge + 0.14),
        SKIN_ROOF)


def add_porch(bm, s, rng):
    """A gabled entrance porch on two posts, ridged along Y like the dormers.

    The posts stand 1.05 m off the wall and 2.04 m apart - clear of the door swing, and
    close enough in that a kart glancing the front of the house meets the plinth first.
    """
    y_out = -s.half_d - 1.10
    z_head = 2.44
    z_ridge = 3.06
    px = 1.02

    for sx in (-1, 1):
        box(bm, (sx * px - 0.10, y_out - 0.10, 0.0), (sx * px + 0.10, y_out + 0.10, 0.30),
            SKIN_STONE)
        box(bm, (sx * px - 0.085, y_out - 0.085, 0.24),
            (sx * px + 0.085, y_out + 0.085, z_head), SKIN_TIMBER)
        # Knee brace from post to head beam, the joint that stops a porch racking.
        beam(bm, (sx * px, y_out, z_head - 0.62), (sx * px - sx * 0.52, y_out, z_head - 0.10),
             0.09, 0.13, SKIN_TIMBER, up=(0.0, 1.0, 0.0))
        beam(bm, (sx * px, y_out, z_head - 0.10), (sx * px, -s.half_d + 0.05, z_head - 0.10),
             0.11, 0.15, SKIN_TIMBER)

    box(bm, (-px - 0.22, y_out - 0.11, z_head - 0.10), (px + 0.22, y_out + 0.11, z_head + 0.06),
        SKIN_TIMBER)
    prism(bm, [(-px - 0.14, y_out - 0.02, z_head + 0.04),
               (px + 0.14, y_out - 0.02, z_head + 0.04),
               (0.0, y_out - 0.02, z_ridge)], (0.0, 0.18, 0.0), SKIN_WALL)

    run, rise = px + 0.36, z_ridge - z_head - 0.10
    y_mid = (y_out - 0.22 + (-s.half_d + 0.02)) * 0.5
    length = abs(-s.half_d + 0.02 - (y_out - 0.22))
    for sx in (-1, 1):
        d = Vector((-sx * run, 0.0, rise))
        d.normalize()
        n = Vector((sx * abs(d.z), 0.0, abs(d.x)))
        a = Vector((sx * run, y_mid, z_head - 0.06))
        b = Vector((0.0, y_mid, z_ridge))
        beam(bm, a, b, length, 0.12, SKIN_ROOF, up=n)
        beam(bm, a + n * 0.10, b + n * 0.10, length + 0.04, 0.09, SKIN_ROOF, up=n)
    box(bm, (-0.14, y_out - 0.24, z_ridge - 0.02), (0.14, -s.half_d + 0.02, z_ridge + 0.15),
        SKIN_ROOF)

    # Flagstone at the foot of the step, worn into the ground.
    box(bm, (-0.75, y_out + 0.18, 0.0), (0.75, -s.half_d - 0.10, 0.11), SKIN_STONE)


def add_chimney(bm, s, sign, rng, sooty=False):
    """A stone stack against a gable, stepping in as it climbs.

    It runs past the ridge because a stack stopping level with it reads as a buttress,
    and because on the ruin it is the one thing left standing. How far past is a spec
    dimension: the same 0.75 m over a smaller cabin reads as a factory flue.
    """
    y0 = 0.20
    top = s.ridge_z + s.chimney_rise
    steps = [
        (0.00, 0.52, 0.86, 0.54),
        (0.52, 2.35, 0.78, 0.48),
        (2.35, 4.05, 0.68, 0.42),
        (4.05, top, 0.60, 0.36),
    ]
    for z0, z1, depth, reach in steps:
        box(bm, (sign * (s.half_w - 0.18), y0 - depth / 2.0, z0),
            (sign * (s.half_w + reach), y0 + depth / 2.0, z1), SKIN_STONE)

    # Cap, oversailing on all four sides so the stack has a lip to catch the light.
    box(bm, (sign * (s.half_w - 0.20), y0 - 0.40, top),
        (sign * (s.half_w + 0.46), y0 + 0.40, top + 0.17), SKIN_STONE)
    if not sooty:
        box(bm, (sign * (s.half_w - 0.02), y0 - 0.24, top + 0.17),
            (sign * (s.half_w + 0.36), y0 + 0.24, top + 0.30), SKIN_INTERIOR)
    else:
        # One cap stone knocked off and left leaning against the stack.
        with moved(bm, spin((sign * s.half_w, y0, top), (0.0, 1.0, 0.0), 26.0)):
            box(bm, (sign * (s.half_w - 0.10), y0 - 0.40, top + 0.18),
                (sign * (s.half_w + 0.48), y0 + 0.40, top + 0.33), SKIN_STONE)

    # Loose stones proud of the face, the same trick as the quoins on the plinth.
    for _ in range(4):
        z = rng.uniform(0.7, top - 0.9)
        d = rng.uniform(0.16, 0.26)
        y = rng.uniform(-0.22, 0.22) + y0
        box(bm, (sign * (s.half_w + 0.30), y - d, z),
            (sign * (s.half_w + 0.30 + rng.uniform(0.09, 0.15)), y + d, z + d * 1.1),
            SKIN_STONE)


def add_lean_to(bm, s, sign, rng):
    """A woodshed against a gable: shed roof, two posts, and a stack of split logs.

    Cheap variety. It changes the silhouette more than any amount of re-proportioning
    does, which is the whole reason Cabin_B is not simply Cabin_A at 80%.
    """
    x_wall = sign * s.half_w
    x_out = sign * (s.half_w + 1.55)
    z_high, z_low = 2.10, 1.52
    y0, y1 = -s.half_d + 0.30, s.half_d - 0.30

    for y in (y0, y1):
        box(bm, (x_out - sign * 0.09, y - 0.09, 0.0), (x_out + sign * 0.03, y + 0.09, z_low),
            SKIN_TIMBER)
    beam(bm, (x_out, y0, z_low + 0.06), (x_out, y1, z_low + 0.06), 0.14, 0.12, SKIN_TIMBER)
    beam(bm, (x_wall, y0, z_high + 0.06), (x_wall, y1, z_high + 0.06), 0.14, 0.12,
         SKIN_TIMBER)

    d = Vector((float(sign) * 1.55, 0.0, z_low - z_high))
    d.normalize()
    n = Vector((-d.z * sign, 0.0, abs(d.x)))
    n.normalize()
    a = Vector((x_wall, (y0 + y1) / 2.0, z_high))
    b = Vector((x_out + sign * 0.16, (y0 + y1) / 2.0, z_low - 0.06))
    length = (y1 - y0) + 0.36
    beam(bm, a, b, length, 0.15, SKIN_ROOF, up=n)

    # Three courses down the shed roof, laid the same tilted way as the main pitches, so
    # the woodshed is roofed in the same material as the house rather than in a plank.
    for i in range(3):
        t0, t1 = i / 3.0, (i + 1) / 3.0 + 0.09
        beam(bm, b.lerp(a, t0) + n * 0.185, b.lerp(a, min(t1, 1.0)) + n * 0.095,
             length - 0.06, 0.10, SKIN_ROOF, up=n)

    # Split logs, stacked. Six sides each: the facets are the style, same as tb.tube says.
    for row in range(3):
        z = 0.20 + row * 0.34
        for i in range(4 - row):
            x = x_wall + sign * (0.42 + i * 0.30 + row * 0.14)
            r = rng.uniform(0.13, 0.16)
            tb.tube(bm, (x, y0 + 0.16, z + r), (x, y1 - 0.16, z + r), r, SKIN_TIMBER,
                    segments=6)
    for i in range(2):
        x = x_wall + sign * (0.55 + i * 0.42)
        box(bm, (x, y1 - 0.55, 0.0), (x + sign * 0.19, y1 - 0.24, rng.uniform(0.7, 0.95)),
            SKIN_TIMBER)


def add_debris(bm, s, rng):
    """What a burnt house leaves on the ground: fallen timbers, ash, and live embers.

    Everything sits inside the eaves or just outside them. Scatter that wanders further
    stops the prop dropping cleanly onto terrain, which is the whole point of the origin
    convention.
    """
    def outside(spread):
        """A point in the apron around the walls rather than anywhere in the footprint.

        Debris dropped uniformly over the plan mostly lands *inside* the house, where the
        walls hide it and it buys nothing. Scattering around the perimeter puts every
        piece where the camera is, for the same count of triangles.
        """
        ang = rng.uniform(0.0, math.tau)
        r = rng.uniform(1.02, spread)
        return (math.cos(ang) * (s.half_w + 0.30) * r,
                math.sin(ang) * (s.half_d + 0.30) * r)

    for _ in range(9):
        # Fallen rafters and plate timbers, lying where they came down.
        x, y = outside(1.26)
        ang = rng.uniform(0.0, math.pi)
        length = rng.uniform(1.2, 2.8)
        dx, dy = math.cos(ang) * length / 2.0, math.sin(ang) * length / 2.0
        beam(bm, (x - dx, y - dy, 0.09), (x + dx, y + dy, 0.09 + rng.uniform(0.0, 0.6)),
             rng.uniform(0.12, 0.18), rng.uniform(0.12, 0.17), SKIN_TIMBER)

    for _ in range(7):
        # Ash and fallen shingle, as low flat mounds rather than as rubble balls: a
        # chamfered slab reads as a drift of debris and costs a third of a boulder.
        x, y = outside(1.25)
        w, d = rng.uniform(0.45, 1.0), rng.uniform(0.4, 0.85)
        box(bm, (x - w, y - d, 0.0), (x + w, y + d, rng.uniform(0.10, 0.24)), SKIN_ROOF)

    # Sheets of fallen roof, come off in one piece. The clearest single read that what is
    # missing overhead is lying down here. Kept close to flat rather than propped up at
    # an angle: a leaning slab is a ramp, and this is a kart racer where the mesh is the
    # collider - the last thing the map needs is a launch pad hidden in the scenery.
    for i in range(4):
        x = -s.half_w + 0.6 + i * (s.width - 1.2) / 3.0
        y = -s.half_d - rng.uniform(0.45, 0.85)
        with moved(bm, spin((x, y, 0.0), (1.0, 0.0, 0.0), rng.uniform(-13.0, -5.0))):
            box(bm, (x - 0.66, y - 0.55, 0.03), (x + 0.66, y + 0.55, 0.17), SKIN_ROOF)

    for _ in range(9):
        # Embers, kept small and low. Slot 4 wants the scene's own lava material on it,
        # so these are the parts that light the ruin from underneath at night.
        x, y = outside(1.12)
        w = rng.uniform(0.16, 0.34)
        box(bm, (x - w, y - w * 0.7, 0.02), (x + w, y + w * 0.7, rng.uniform(0.12, 0.24)),
            SKIN_GLOW)
    # Seams of embers still burning in the two openings the fire came out of: along the
    # front sill, and in the burnt bay. Openings that glow is what makes a ruin read as
    # hours old rather than years.
    box(bm, (-1.5, -s.half_d + 0.14, 0.42), (1.5, -s.half_d + 0.36, 0.58), SKIN_GLOW)
    box(bm, (-s.half_w + 0.55, -s.half_d + 0.10, 0.42),
        (-0.85, -s.half_d + 0.40, 0.62), SKIN_GLOW)

    # Charred stumps of the porch posts, snapped off low.
    for sx in (-1, 1):
        box(bm, (sx * 1.02 - 0.10, -s.half_d - 1.20, 0.0),
            (sx * 1.02 + 0.10, -s.half_d - 1.00, rng.uniform(0.35, 0.62)), SKIN_TIMBER)


# --------------------------------------------------------------------------------------
# Assembly
# --------------------------------------------------------------------------------------

def elevations(bm, s, rng):
    """The four walls, their openings and their framing.

    Every opening is derived from the spec rather than written out, so Cabin_B is a
    genuinely different building at a different width rather than Cabin_A with its
    windows hanging off the corners.
    """
    burnt = s.damage is not None
    glazed = not burnt
    base, top = s.plinth_h, s.wall_top
    hw, hd = s.half_w, s.half_d

    front = s.wall("y", -1)
    back = s.wall("y", 1)

    dh = s.door_w / 2.0
    # On the ruin the lintel burnt out with the wall above it, so the door opening runs
    # to the plate. The leaf is still only door_h tall - it is hanging, not taller.
    door = (-dh, dh, base, top if burnt else base + s.door_h)
    win = (s.bay_x - s.win_w / 2.0, s.bay_x + s.win_w / 2.0, s.sill, s.sill + s.win_h)

    if burnt:
        # The left bay burnt through from the plinth to the plate.
        left = [(-hw + 0.48, -dh - 0.56, base, top)]
    else:
        left = [(-win[1], -win[0], win[2], win[3])]
    holes = left + [door, (win[0], win[1], win[2], win[3])]

    panels(bm, front, -hw, hw, base, top, holes)
    add_frame(bm, s, front, -hw, hw, holes, braces=not burnt)
    add_door(bm, s, front, -dh, dh, base, base + s.door_h, hanging=burnt)
    add_window(bm, s, front, *holes[2], shutters=not burnt, glazed=glazed)

    if burnt:
        # Charred stubs of the studs that used to hold the burnt bay up.
        lo, hi = left[0][0], left[0][1]
        for u, h in ((lo, 1.35), (hi, 0.95), ((lo + hi) / 2.0, 0.55)):
            front.box(bm, u - 0.09, u + 0.09, base, base + h, -0.10, 0.16, SKIN_TIMBER)
        front.box(bm, -dh - 0.10, dh + 0.10, base + s.door_h - 0.07,
                  base + s.door_h + 0.07, front.out - 0.06, front.out + 0.08, SKIN_TIMBER)
    else:
        add_window(bm, s, front, *left[0], shutters=True, glazed=glazed)

    bw = s.win_w * 0.92
    back_holes = [(c - bw / 2.0, c + bw / 2.0, s.sill, s.sill + s.win_h * 0.9)
                  for c in (-hw * 0.55, hw * 0.55)]
    panels(bm, back, -hw, hw, base, top, back_holes)
    add_frame(bm, s, back, -hw, hw, back_holes, braces=not burnt)
    for hole in back_holes:
        add_window(bm, s, back, *hole, shutters=False, glazed=glazed)

    gw = s.win_w * 0.52
    for sign in (-1, 1):
        side = s.wall("x", sign)
        # A blind gable behind the chimney: a window nobody can see is a window nobody
        # should pay for, and the lean-to covers the other one on Cabin_B.
        blind = sign == s.chimney or (s.lean_to and sign < 0)
        side_holes = [] if blind else [(-gw, gw, s.sill, s.sill + s.win_h * 0.93)]
        panels(bm, side, -hd, hd, base, top, side_holes)
        add_frame(bm, s, side, -hd, hd, side_holes, braces=not burnt)
        for hole in side_holes:
            add_window(bm, s, side, *hole, shutters=False, glazed=glazed)


def build_cabin(s):
    """One cabin, one mesh, chamfered once at the end."""
    rng = random.Random(SEED if s.damage is None else s.damage.seed)
    bm = bmesh.new()

    add_plinth(bm, s, rng)
    elevations(bm, s, rng)
    add_posts(bm, s)
    for sign in (-1, 1):
        add_gable(bm, s, sign)
    add_roof(bm, s, rng)

    for i, x in enumerate(s.dormers):
        add_dormer(bm, s, x, rng, wreck=0 if s.damage is None else (i % 2) + 1)
    if s.porch:
        add_porch(bm, s, rng)
    if s.chimney is not None:
        add_chimney(bm, s, s.chimney, rng, sooty=s.damage is not None)
    if s.lean_to:
        add_lean_to(bm, s, -1, rng)
    if s.damage is not None:
        add_debris(bm, s, rng)

    # The chamfer, applied once to everything. Doing it per part instead would give a
    # rail meeting a post two different corner widths, which is the exact tell that a
    # building was assembled from boxes.
    tb.chamfer(bm, CHAMFER, CHAMFER_SEGMENTS)

    obj = tb.mesh_from_bmesh(bm, s.name)
    tb.assign_materials(obj, s.palette)
    return obj


def spec_a(name=NAME_A, palette=None, damage=None):
    s = Spec(
        name, palette or PALETTE_INTACT,
        width=6.4, depth=4.8, wall_h=2.55, ridge_rise=2.15,
        courses=8, porch=damage is None, chimney=1, damage=damage)
    # Dormers over the front windows rather than at some x of their own. A dormer that
    # does not line up with the window under it is the first thing that reads as wrong
    # about a house, and it is the kind of wrong nobody can name when they see it.
    s.dormers = (-s.bay_x, s.bay_x)
    return s


def spec_b():
    return Spec(
        NAME_B, PALETTE_INTACT,
        width=4.9, depth=4.0, wall_h=2.35, ridge_rise=2.30,
        eave=0.44, rake=0.36, courses=7, win_w=0.88, win_h=0.98,
        chimney=1, chimney_rise=0.48, lean_to=True)


def spec_burnt():
    # Which shingle rows the fire left. The front pitch keeps its eave and a scrap at the
    # ridge; the back keeps more, because a roof burns from the side the fire started on.
    damage = Damage(
        keep_courses={-1: {0, 1}, 1: {0, 1, 2, 6}},
        gable_left=0.52,
        seed=SEED + 7,
    )
    return spec_a(NAME_BURNT, PALETTE_BURNT, damage)


if __name__ == "__main__":
    # Budgets sit about a fifth above what each mesh currently costs. A building is not a
    # scattered prop - there are a handful per map, each filling a good part of the screen
    # - so it can carry more than VolcanicRock_A's 400. It is still the collider, though,
    # so "hero" is not "unbudgeted".
    tb.fresh_scene()
    tb.build(build_cabin(spec_a()), NAME_A, max_tris=16500, max_size_m=9.0)

    tb.fresh_scene()
    tb.build(build_cabin(spec_b()), NAME_B, max_tris=12500, max_size_m=8.5)

    # The ruin's box is wider than the house that made it, because the debris around the
    # foot is part of the prop. Placing it is still "drop it on the terrain" - the origin
    # convention holds - but it wants more clearance from a wall than the other two.
    tb.fresh_scene()
    tb.build(build_cabin(spec_burnt()), NAME_BURNT, max_tris=12500, max_size_m=10.5)
