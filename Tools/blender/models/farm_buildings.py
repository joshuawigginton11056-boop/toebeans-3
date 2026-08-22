"""
Farm buildings - barn, machine shed, silo, chicken coop, and a windpump that turns.

    blender --background --factory-startup --python Tools/blender/models/farm_buildings.py

Same principles as models/cabin.py, which is worth reading first: solid boxes and prisms
never planes, openings decomposed rather than booleaned, and one chamfer pass over the
finished mesh so an assembly of boxes reads as one carved object. What is new here is the
scale, and one decision that everything else bends around.

**The barn is drivable through.** Both gable ends carry a 4.2 x 4.0 m opening on the
centre line with nothing between them - no threshold, no sill, no floor slab, no tie beam
below the wall plate. A kart is 1.24 m across its front track, so that is three abreast
with room to spare, and it makes the barn a piece of track rather than scenery to steer
around. Everything else about the building protects that: the concrete plinth stops short
of both doorways instead of running past them as a kerb, the sliding doors are modelled
parked open against the *outside* of the wall, and the hay hoist projects from the gable
well above a kart's roll hoop.

The machine shed is open-fronted for the same reason. The silo, the coop and the windpump
are meant to be obstacles, and are shaped so a kart glances off rather than catches:
round, or plinthed, or standing on legs set inboard of the body above them.

Axes, since a building has two that matter and swapping them is the classic way to spend
an afternoon: **the ridge runs along Y**. The roof slopes in X, so the eave walls are at
+-half_x and carry the windows, and the gable walls are at +-half_y and carry the big
doors. You drive through along Y, down the length of the building.

Dimensions are agricultural rather than arbitrary - a 4.4 m wall plate, a 9 m span - and
everything else is derived from them, so changing the span moves the roof, the gables, the
doors and the hay hoist together instead of moving most of them.
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
# The barn
# --------------------------------------------------------------------------------------

class Barn:
    """Every dimension of a barn. Derived heights are computed once, here.

    The roof is a gambrel - two pitches a side, steep then shallow - because that single
    profile is what makes a shape read as "barn" from across a map at speed. A plain gable
    on the same footprint reads as a very large cabin, which is the one thing this pack
    cannot afford standing next to models/cabin.py.
    """

    def __init__(self, name, span=9.0, length=13.0, wall_h=4.4,
                 lower_rise=2.0, upper_rise=2.5, knuckle_frac=0.52,
                 door_w=4.2, door_h=4.0, wall_t=0.30, plinth_h=0.34,
                 eave=0.55, rake=0.45, windows=4, cupola=True):
        self.name = name
        self.span = span            # across the ridge (X), eave wall to eave wall
        self.length = length        # along the ridge (Y), gable to gable
        self.wall_h = wall_h        # plinth top to wall plate top
        self.wall_t = wall_t
        self.plinth_h = plinth_h
        self.eave = eave            # roof overhang past the eave walls, in X
        self.rake = rake            # roof overhang past the gable walls, in Y
        self.door_w = door_w
        self.door_h = door_h
        self.windows = windows
        self.cupola = cupola

        self.half_x = span / 2.0
        self.half_y = length / 2.0
        self.wall_top = plinth_h + wall_h

        # The gambrel, as three heights and one inflection. Everything that touches the
        # roof - the gable pieces, the loft opening, the barge boards, the cupola - is
        # placed by asking `roof_z` rather than by repeating these numbers.
        self.knuckle_x = self.half_x * knuckle_frac
        self.knuckle_z = self.wall_top + lower_rise
        self.ridge_z = self.knuckle_z + upper_rise

        # How fast the lower, steep pitch climbs per metre inboard. The eave overhang
        # drops by this, which is what keeps the fascia parallel to the roof it hangs off.
        self.lower_slope = lower_rise / (self.half_x - self.knuckle_x)
        self.eave_x = self.half_x + eave
        self.eave_z = self.wall_top - eave * self.lower_slope

        # Hay loft door, centred in the gable above the main opening.
        self.loft_hw = 0.85
        self.loft_top = self.wall_top + 1.9
        self.hoist_out = 1.15       # how far the hoist beam projects past the gable
        self.door_hw = door_w / 2.0

        if self.loft_hw >= self.knuckle_x:
            raise ValueError("the loft opening has to sit inside the upper pitch, or the "
                             "gable pieces around it stop being convex")
        if self.loft_top >= self.roof_z(self.loft_hw):
            raise ValueError("the loft opening reaches through the roof")

    def roof_z(self, x):
        """Height of the roof line `x` off the ridge. Ridge at x=0, eave at +-half_x."""
        x = abs(x)
        if x >= self.knuckle_x:
            t = (x - self.knuckle_x) / (self.half_x - self.knuckle_x)
            return self.knuckle_z + (self.wall_top - self.knuckle_z) * t
        t = x / self.knuckle_x
        return self.ridge_z + (self.knuckle_z - self.ridge_z) * t


def add_barn_plinth(bm, s):
    """A concrete footing under the walls - and deliberately not under the doorways.

    A kerb across a 4.2 m opening the track runs through is exactly the step the project's
    geometry rules exist to forbid: it would stop a kart dead or launch it, depending on
    the approach. So the plinth is four runs with two gaps in it, not a ring.
    """
    over = 0.14
    x, y, h = s.half_x + over, s.half_y + over, s.plinth_h
    inset = s.half_y - s.wall_t - over

    # The eave walls get a continuous footing, run right to the corners.
    for sign in (-1, 1):
        box(bm, (sign * (s.half_x - s.wall_t - over), -y, 0.0), (sign * x, y, h),
            fy.SKIN_DIRT)

    # The gable ends get one either side of the opening, and nothing across it.
    for sign in (-1, 1):
        for side in (-1, 1):
            lo, hi = sorted((side * s.door_hw, side * (s.half_x - s.wall_t - over)))
            box(bm, (lo, sign * inset, 0.0), (hi, sign * y, h), fy.SKIN_DIRT)


def add_barn_walls(bm, s):
    """Board-and-batten in barn red, with the two big openings decomposed around.

    Openings are made by filling the wall with boxes around them rather than by boolean,
    for cabin.py's reason: a boolean leaves n-gons and coplanar slivers, and the chamfer
    pass turns both into shading artefacts you only find once it is in Unity.
    """
    t = s.wall_t
    z0, z1 = s.plinth_h, s.wall_top

    # Eave walls, full height, boarded along Y.
    for sign in (-1, 1):
        x = sign * (s.half_x - t * 0.5)
        fy.planks(bm, -s.half_y, s.half_y, x, t, z0, z1,
                  max(2, int(s.length / 0.62)), skin=fy.SKIN_PAINT, axis="y",
                  batten_skin=fy.SKIN_PAINT)

    # Gable walls: a pier either side of the opening, then a header over the top.
    for sign in (-1, 1):
        y = sign * (s.half_y - t * 0.5)
        for side in (-1, 1):
            lo, hi = sorted((side * s.door_hw, side * s.half_x))
            fy.planks(bm, lo, hi, y, t, z0, z1, max(2, int((hi - lo) / 0.62)),
                      skin=fy.SKIN_PAINT, batten_skin=fy.SKIN_PAINT)
        box(bm, (-s.door_hw, y - t * 0.5, z0 + s.door_h), (s.door_hw, y + t * 0.5, z1),
            fy.SKIN_PAINT)
        # Jambs, so the opening has an edge rather than a raw plank end.
        for side in (-1, 1):
            box(bm, (side * s.door_hw - 0.09, y - t * 0.62, z0),
                (side * s.door_hw + 0.09, y + t * 0.62, z0 + s.door_h + 0.10),
                fy.SKIN_TRIM)
        box(bm, (-s.door_hw - 0.09, y - t * 0.62, z0 + s.door_h),
            (s.door_hw + 0.09, y + t * 0.62, z0 + s.door_h + 0.10), fy.SKIN_TRIM)

    # Corner boards and a wall plate in trim white. These are what stop nine metres of red
    # from reading as one flat slab - the pack's only real use of the trim colour.
    for sx in (-1, 1):
        for sy in (-1, 1):
            box(bm, (sx * (s.half_x - 0.17), sy * (s.half_y - 0.02), z0),
                (sx * (s.half_x + 0.05), sy * (s.half_y + 0.07), z1), fy.SKIN_TRIM)
    for sign in (-1, 1):
        box(bm, (sign * (s.half_x - 0.02), -s.half_y - 0.05, z1 - 0.20),
            (sign * (s.half_x + 0.07), s.half_y + 0.05, z1), fy.SKIN_TRIM)
        box(bm, (-s.half_x - 0.05, sign * (s.half_y - 0.02), z1 - 0.20),
            (s.half_x + 0.05, sign * (s.half_y + 0.07), z1), fy.SKIN_TRIM)


def add_barn_windows(bm, s):
    """Four-pane windows down each eave wall, glazed and backed.

    Every opening has something behind it. An unbacked hole shows the skybox through the
    far wall the moment the camera drops below the eaves - the cabin's `CabinInterior`
    lesson, and a barn has a great deal more wall to get it wrong on.
    """
    w, h = 1.05, 0.95
    sill = s.plinth_h + 2.05
    t = s.wall_t
    pitch = s.length / (s.windows + 1)

    for sign in (-1, 1):
        x = sign * (s.half_x - t * 0.5)
        for i in range(s.windows):
            y = -s.half_y + pitch * (i + 1)
            # Backing first, so the glass and frame sit in front of it.
            box(bm, (x - t * 0.45, y - w * 0.5, sill), (x + t * 0.45, y + w * 0.5, sill + h),
                fy.SKIN_DARK)
            box(bm, (x - t * 0.20, y - w * 0.5, sill), (x + t * 0.20, y + w * 0.5, sill + h),
                fy.SKIN_GLASS)
            # Frame: two jambs, a head and a sill, plus one glazing bar each way.
            for u in (-1, 1):
                box(bm, (x - t * 0.62, y + u * w * 0.5 - 0.055, sill - 0.06),
                    (x + t * 0.62, y + u * w * 0.5 + 0.055, sill + h + 0.06), fy.SKIN_TRIM)
            for v in (0.0, 1.0):
                box(bm, (x - t * 0.62, y - w * 0.5 - 0.055, sill + h * v - 0.055),
                    (x + t * 0.62, y + w * 0.5 + 0.055, sill + h * v + 0.055), fy.SKIN_TRIM)
            box(bm, (x - t * 0.32, y - 0.035, sill), (x + t * 0.32, y + 0.035, sill + h),
                fy.SKIN_TRIM)
            box(bm, (x - t * 0.32, y - w * 0.5, sill + h * 0.5 - 0.035),
                (x + t * 0.32, y + w * 0.5, sill + h * 0.5 + 0.035), fy.SKIN_TRIM)


def add_barn_gable(bm, s, sign):
    """The wall above the wall plate, in convex pieces around the hay loft opening.

    Cut into five rather than made as one outline with a rectangle taken out of it,
    because every piece has to stay convex: a gambrel gable with a hole in it is a concave
    n-gon, and a concave n-gon is what the chamfer pass shades wrong.
    """
    t = s.wall_t
    lo_y = sign * (s.half_y - t * 0.5) - t * 0.5
    extrude = (0.0, t, 0.0)

    def piece(points, skin=fy.SKIN_PAINT):
        prism(bm, [(px, lo_y, pz) for px, pz in points], extrude, skin)

    for side in (-1, 1):
        outer = side * s.half_x
        knuckle = side * s.knuckle_x
        loft = side * s.loft_hw

        # Under the lower pitch this is a triangle, not a trapezoid: the roof line meets
        # the wall plate exactly at the eave, so the outboard edge has no height.
        piece([(outer, s.wall_top), (knuckle, s.wall_top), (knuckle, s.knuckle_z)])
        # Under the upper pitch: knuckle in to the edge of the loft opening.
        piece([(knuckle, s.wall_top), (loft, s.wall_top),
               (loft, s.roof_z(loft)), (knuckle, s.knuckle_z)])

    # The cap over the loft opening, up to the ridge.
    piece([(-s.loft_hw, s.loft_top), (s.loft_hw, s.loft_top),
           (s.loft_hw, s.roof_z(s.loft_hw)), (0.0, s.ridge_z),
           (-s.loft_hw, s.roof_z(-s.loft_hw))])

    # Backing behind the loft opening, and a frame round it.
    y = sign * (s.half_y - t * 0.5)
    box(bm, (-s.loft_hw, y - t * 0.45, s.wall_top), (s.loft_hw, y + t * 0.45, s.loft_top),
        fy.SKIN_DARK)
    for u in (-1, 1):
        box(bm, (u * s.loft_hw - 0.07, y - t * 0.62, s.wall_top),
            (u * s.loft_hw + 0.07, y + t * 0.62, s.loft_top + 0.07), fy.SKIN_TRIM)
    box(bm, (-s.loft_hw - 0.07, y - t * 0.62, s.loft_top - 0.07),
        (s.loft_hw + 0.07, y + t * 0.62, s.loft_top + 0.07), fy.SKIN_TRIM)


def add_barn_roof(bm, s):
    """Four gambrel slabs, plus the fascia, rafter tails and barge boards that edge them.

    A roof drawn as four bare slabs reads as cardboard however good the pitch is. What
    fixes it is that the edges are thick and stepped: a fascia at the eave, a barge board
    up the rake, and a ridge cap standing proud of both slopes.
    """
    thick = 0.16
    run_len = s.length + 2.0 * s.rake

    def slope(inner, outer):
        """One pitch, as a box laid along the run with its depth along the roof normal."""
        (x0, z0), (x1, z1) = inner, outer
        dx, dz = x1 - x0, z1 - z0
        mag = math.hypot(dx, dz)
        nx, nz = -dz / mag, dx / mag
        for sign in (-1, 1):
            beam(bm, (sign * x0, 0.0, z0), (sign * x1, 0.0, z1),
                 run_len, thick, fy.SKIN_ROOF, up=(sign * nx, 0.0, nz))

    # The two pitches, each run a touch past its neighbour so the slabs interpenetrate
    # rather than meeting on an exact line the chamfer would open into a seam.
    slope((-0.04, s.ridge_z + 0.02), (s.knuckle_x + 0.02, s.knuckle_z - 0.01))
    slope((s.knuckle_x - 0.03, s.knuckle_z + 0.02), (s.eave_x, s.eave_z))

    box(bm, (-0.17, -run_len * 0.5, s.ridge_z + 0.02),
        (0.17, run_len * 0.5, s.ridge_z + 0.17), fy.SKIN_ROOF)

    # Fascia along each eave, with rafter tails poking out under it.
    for sign in (-1, 1):
        box(bm, (sign * s.eave_x - 0.09, -run_len * 0.5, s.eave_z - 0.30),
            (sign * s.eave_x + 0.09, run_len * 0.5, s.eave_z + 0.06), fy.SKIN_TRIM)
        count = int(s.length / 0.95)
        for i in range(count):
            y = -s.half_y + (i + 0.5) * (s.length / count)
            box(bm, (sign * (s.half_x - 0.10), y - 0.05, s.eave_z + 0.02),
                (sign * (s.eave_x - 0.10), y + 0.05, s.eave_z + 0.20), fy.SKIN_WOOD)

    # Barge boards down both rakes, following the gambrel in two runs a side.
    for sy in (-1, 1):
        y = sy * (s.half_y + s.rake)
        for sx in (-1, 1):
            for (x0, z0), (x1, z1) in (((0.0, s.ridge_z), (s.knuckle_x, s.knuckle_z)),
                                       ((s.knuckle_x, s.knuckle_z), (s.eave_x, s.eave_z))):
                beam(bm, (sx * x0, y, z0 + 0.02), (sx * x1, y, z1 + 0.02),
                     0.10, 0.26, fy.SKIN_TRIM, up=(0.0, 0.0, 1.0))


def add_barn_doors(bm, s):
    """Two sliding doors, parked open against the outside of each gable.

    Modelled open rather than closed, and outside the wall rather than inside it. Closed
    doors would block the one thing the building is for, and doors hung inside the opening
    would narrow it by their own thickness at exactly a kart's shoulder height.
    """
    t = s.wall_t
    leaf_w = s.door_w * 0.56
    leaf_h = s.door_h + 0.18

    for sign in (-1, 1):
        y = sign * (s.half_y + 0.10)
        # The track the doors hang from.
        box(bm, (-s.half_x + 0.1, y - 0.07, s.plinth_h + leaf_h + 0.04),
            (s.half_x - 0.1, y + 0.07, s.plinth_h + leaf_h + 0.20), fy.SKIN_METAL)

        for side in (-1, 1):
            # Slid clear of the opening: inner edge just past the jamb.
            inner = side * (s.door_hw + 0.14)
            lo, hi = sorted((inner, inner + side * leaf_w))
            fy.planks(bm, lo, hi, y, 0.09, s.plinth_h, s.plinth_h + leaf_h,
                      max(2, int(leaf_w / 0.42)), skin=fy.SKIN_PAINT)
            # The Z-brace that makes a plank door a door.
            for z in (s.plinth_h + 0.12, s.plinth_h + leaf_h - 0.12):
                box(bm, (lo, y + sign * 0.02, z - 0.06),
                    (hi, y + sign * 0.10, z + 0.06), fy.SKIN_TRIM)
            beam(bm, (lo + 0.06, y + sign * 0.06, s.plinth_h + 0.18),
                 (hi - 0.06, y + sign * 0.06, s.plinth_h + leaf_h - 0.18),
                 0.10, 0.13, fy.SKIN_TRIM, up=(0.0, 1.0, 0.0))
            # Hangers up to the track.
            for u in (0.25, 0.75):
                hx = lo + (hi - lo) * u
                box(bm, (hx - 0.045, y - 0.05, s.plinth_h + leaf_h - 0.05),
                    (hx + 0.045, y + 0.05, s.plinth_h + leaf_h + 0.14), fy.SKIN_METAL)


def add_barn_hoist(bm, s):
    """The hay hoist over each loft door: a projecting beam, a knee brace and a pulley.

    The barn's tallest projecting detail, and the reason the loft door reads as a working
    opening rather than as a window. Hung at the ridge, so it clears everything below.
    """
    for sign in (-1, 1):
        y0 = sign * (s.half_y + s.rake)
        y1 = sign * (s.half_y + s.rake + s.hoist_out)
        z = s.ridge_z - 0.42
        lo_y, hi_y = sorted((y0, y1))
        box(bm, (-0.13, lo_y, z - 0.13), (0.13, hi_y, z + 0.13), fy.SKIN_WOOD)
        beam(bm, (0.0, sign * (s.half_y - 0.05), z - 1.05),
             (0.0, y0 + sign * 0.28, z - 0.10), 0.20, 0.14, fy.SKIN_WOOD,
             up=(0.0, 0.0, 1.0))
        # Pulley block on the end.
        fy.ring(bm, (0.0, y1 - sign * 0.14, z - 0.32), (1.0, 0.0, 0.0),
                0.07, 0.15, 0.09, skin=fy.SKIN_RUST, segments=8)
        box(bm, (-0.05, y1 - sign * 0.20, z - 0.32), (0.05, y1 - sign * 0.08, z),
            fy.SKIN_RUST)


def add_cupola(bm, s):
    """A vented cupola on the ridge with a weathervane - the barn's tallest read.

    Small, and worth every triangle: it breaks the ridge line, which is otherwise the one
    perfectly straight thing on the whole silhouette.
    """
    half = 0.62
    base = s.ridge_z + 0.10
    body_h = 1.05

    box(bm, (-half, -half, base), (half, half, base + body_h), fy.SKIN_PAINT)
    # Louvres on all four sides, as shallow bars rather than as slots.
    for i in range(4):
        z = base + 0.16 + i * 0.21
        for sign in (-1, 1):
            box(bm, (-half - 0.03, sign * half - 0.02, z),
                (half + 0.03, sign * half + 0.05, z + 0.11), fy.SKIN_TRIM)
            box(bm, (sign * half - 0.02, -half - 0.03, z),
                (sign * half + 0.05, half + 0.03, z + 0.11), fy.SKIN_TRIM)

    # A little pyramid roof, as two crossed prisms rather than a stack of shrinking boxes.
    roof = half + 0.16
    top = base + body_h + 0.52
    for turn in (0, 90):
        with fy.rotated(bm, turn, (0.0, 0.0, 1.0)):
            prism(bm, [(-roof, -roof, base + body_h), (roof, -roof, base + body_h),
                       (0.0, -roof, top)], (0.0, roof * 2.0, 0.0), fy.SKIN_ROOF)

    # Weathervane: a spindle, the N/S bar, and a cockerel cut from plate.
    box(bm, (-0.035, -0.035, top - 0.05), (0.035, 0.035, top + 0.78), fy.SKIN_RUST)
    box(bm, (-0.30, -0.03, top + 0.30), (0.30, 0.03, top + 0.36), fy.SKIN_RUST)
    box(bm, (-0.03, -0.30, top + 0.30), (0.03, 0.30, top + 0.36), fy.SKIN_RUST)
    box(bm, (-0.02, -0.26, top + 0.52), (0.02, 0.20, top + 0.74), fy.SKIN_RUST)
    box(bm, (-0.02, 0.14, top + 0.66), (0.02, 0.34, top + 0.90), fy.SKIN_RUST)


def build_barn(s):
    bm = bmesh.new()
    add_barn_plinth(bm, s)
    add_barn_walls(bm, s)
    add_barn_windows(bm, s)
    for sign in (-1, 1):
        add_barn_gable(bm, s, sign)
    add_barn_roof(bm, s)
    add_barn_doors(bm, s)
    add_barn_hoist(bm, s)
    if s.cupola:
        add_cupola(bm, s)
    return fy.finish(bm, s.name)


# --------------------------------------------------------------------------------------
# The machine shed
# --------------------------------------------------------------------------------------

def build_shed(name="Farm_Shed", bays=3, bay_w=3.4, depth=5.2, front_h=3.6, back_h=4.5):
    """An open-fronted implement shed: a mono-pitch roof on posts, walled on three sides.

    Open at the front on purpose. It is the pack's cheap piece of drivable cover - park a
    tractor in one bay and a track still runs through the other two - and an open front
    means the posts are the only thing a kart can hit, so they stand on the plinth where a
    glancing blow meets the chamfered corner of the slab first.
    """
    bm = bmesh.new()
    width = bays * bay_w
    half_x = width / 2.0
    half_y = depth / 2.0
    t = 0.22
    plinth = 0.22

    box(bm, (-half_x - 0.16, -half_y - 0.16, 0.0), (half_x + 0.16, half_y + 0.16, plinth),
        fy.SKIN_DIRT)

    # Back wall in bare weathered timber - this one was never painted.
    fy.planks(bm, -half_x, half_x, half_y - t * 0.5, t, plinth, back_h,
              int(width / 0.55), skin=fy.SKIN_WOOD, batten_skin=fy.SKIN_WOOD_DARK)

    # The side walls follow the roof slope, so each is a prism rather than a box.
    for sign in (-1, 1):
        x = sign * (half_x - t * 0.5)
        prism(bm, [(x - t * 0.5, -half_y, plinth), (x - t * 0.5, half_y, plinth),
                   (x - t * 0.5, half_y, back_h), (x - t * 0.5, -half_y, front_h)],
              (t, 0.0, 0.0), fy.SKIN_WOOD)

    # Front posts, one per bay division, with knee braces into the head beam.
    for i in range(bays + 1):
        x = max(-half_x + 0.16, min(half_x - 0.16, -half_x + i * bay_w))
        box(bm, (x - 0.11, -half_y + 0.10, plinth), (x + 0.11, -half_y + 0.32, front_h),
            fy.SKIN_WOOD_DARK)
        for side in (-1, 1):
            inner = x + side * 0.10
            outer = x + side * 0.62
            if abs(outer) > half_x:
                continue
            beam(bm, (inner, -half_y + 0.21, front_h - 0.75),
                 (outer, -half_y + 0.21, front_h - 0.13),
                 0.09, 0.12, fy.SKIN_WOOD_DARK, up=(0.0, 1.0, 0.0))

    box(bm, (-half_x, -half_y + 0.08, front_h - 0.22), (half_x, -half_y + 0.34, front_h),
        fy.SKIN_WOOD_DARK)

    # Corrugated mono-pitch roof, running from the low front to the high back. Built in
    # the roof's own frame so the ribs lie on the slab rather than through it.
    dy, dz = depth + 0.9, back_h - front_h
    mag = math.hypot(dy, dz)
    ny, nz = -dz / mag, dy / mag
    a = (0.0, -half_y - 0.45, front_h + 0.02)
    b = (0.0, half_y + 0.45, back_h + 0.02)
    beam(bm, a, b, width + 0.5, 0.10, fy.SKIN_METAL, up=(0.0, ny, nz))
    ribs = int(width / 0.55)
    for i in range(ribs):
        x = -half_x - 0.25 + (width + 0.5) * (i + 0.5) / ribs
        beam(bm, (x, a[1] + 0.055 * ny, a[2] + 0.055 * nz),
             (x, b[1] + 0.055 * ny, b[2] + 0.055 * nz),
             0.09, 0.05, fy.SKIN_METAL, up=(0.0, ny, nz))

    return fy.finish(bm, name)


# --------------------------------------------------------------------------------------
# The silo
# --------------------------------------------------------------------------------------

def build_silo(name="Farm_Silo", radius=1.95, height=10.5):
    """A galvanised grain silo: corrugated courses, a domed roof, a ladder and a chute.

    Round on purpose. It is the tallest thing in the pack after the windpump and the most
    likely to be clipped at speed, and a cylinder is the one shape that cannot present a
    kart with a corner. Twelve sides, because the facets are the style.
    """
    bm = bmesh.new()
    sides = 12
    plinth = 0.30

    fy.lathe(bm, [(radius + 0.22, 0.0), (radius + 0.22, plinth)], 0.0,
             skin=fy.SKIN_DIRT, segments=sides)

    # The barrel, as stacked courses. Alternate courses swell very slightly, which is what
    # a corrugated silo actually does and what stops ten metres of cylinder reading as one
    # untextured tube.
    courses = 7
    course_h = (height - plinth - 0.9) / courses
    for i in range(courses):
        z = plinth + i * course_h
        swell = 0.022 if i % 2 == 0 else 0.0
        fy.lathe(bm, [(radius + swell, 0.0), (radius + swell, course_h)], z,
                 skin=fy.SKIN_METAL, segments=sides,
                 close_bottom=(i == 0), close_top=False)
        fy.ring(bm, (0.0, 0.0, z + course_h), (0.0, 0.0, 1.0),
                radius - 0.02, radius + 0.05, 0.06, skin=fy.SKIN_METAL, segments=sides)

    top = plinth + courses * course_h
    # Domed roof, as a three-step lathe rather than a cone: a cone reads as a funnel.
    fy.lathe(bm, [(radius + 0.06, 0.0), (radius * 0.82, 0.34),
                  (radius * 0.48, 0.62), (0.16, 0.80)], top,
             skin=fy.SKIN_METAL, segments=sides)
    fy.lathe(bm, [(0.24, 0.0), (0.24, 0.22), (0.16, 0.30)], top + 0.78,
             skin=fy.SKIN_RUST, segments=8)

    # Ladder up the side. Its stringers are set proud of the barrel so the rungs read.
    y = -(radius + 0.12)
    for side in (-1, 1):
        box(bm, (side * 0.22, y - 0.06, plinth), (side * 0.30, y + 0.02, top + 0.30),
            fy.SKIN_RUST)
    rungs = int((top - plinth) / 0.34)
    for i in range(rungs):
        z = plinth + 0.30 + i * 0.34
        box(bm, (-0.26, y - 0.05, z), (0.26, y + 0.01, z + 0.045), fy.SKIN_RUST)

    # Discharge chute at the foot - the part that says "grain" rather than "water tank".
    box(bm, (-0.42, y - 0.02, plinth + 0.24), (0.42, y + 0.40, plinth + 1.30),
        fy.SKIN_METAL)
    prism(bm, [(-0.42, y + 0.02, plinth + 0.24), (0.42, y + 0.02, plinth + 0.24),
               (0.0, y + 0.02, plinth - 0.02)], (0.0, 0.34, 0.0), fy.SKIN_RUST)

    return fy.finish(bm, name)


# --------------------------------------------------------------------------------------
# The chicken coop
# --------------------------------------------------------------------------------------

def build_coop(name="Farm_ChickenCoop"):
    """A raised hen house with a ramp, nest boxes and a short fenced run.

    Raised on legs, which is both what a coop is and what keeps it from being a wheel trap:
    the legs stand inboard of the body, so a kart clipping the corner meets the
    overhanging floor pan and is deflected instead of caught between two posts.
    """
    bm = bmesh.new()
    rng = random.Random(SEED + 3)

    w, d = 1.85, 1.45
    leg_h = 0.62
    body_h = 1.15
    half_x, half_y = w / 2.0, d / 2.0

    for sx in (-1, 1):
        for sy in (-1, 1):
            x, y = sx * (half_x - 0.26), sy * (half_y - 0.24)
            box(bm, (x - 0.075, y - 0.075, 0.0), (x + 0.075, y + 0.075, leg_h + 0.10),
                fy.SKIN_WOOD_DARK)

    box(bm, (-half_x, -half_y, leg_h), (half_x, half_y, leg_h + 0.12), fy.SKIN_WOOD)

    # Walls in the pack's second paint colour, so the coop is not a small red barn.
    z0, z1 = leg_h + 0.12, leg_h + 0.12 + body_h
    for sign in (-1, 1):
        fy.planks(bm, -half_x, half_x, sign * (half_y - 0.04), 0.08, z0, z1,
                  5, skin=fy.SKIN_PAINT_B)
        fy.planks(bm, -half_y, half_y, sign * (half_x - 0.04), 0.08, z0, z1,
                  4, skin=fy.SKIN_PAINT_B, axis="y")

    # Pitched roof with a good overhang, ridge along X.
    ridge = z1 + 0.46
    for sign in (-1, 1):
        prism(bm, [(-half_x - 0.14, sign * (half_y + 0.16), z1),
                   (-half_x - 0.14, 0.0, ridge),
                   (-half_x - 0.14, 0.0, ridge - 0.13),
                   (-half_x - 0.14, sign * (half_y + 0.16), z1 - 0.13)],
              (w + 0.28, 0.0, 0.0), fy.SKIN_ROOF)
    box(bm, (-half_x - 0.16, -0.10, ridge - 0.10), (half_x + 0.16, 0.10, ridge + 0.04),
        fy.SKIN_ROOF)

    # Pop hole and its ramp on the +Y face. The cleats are what make it read as a ramp
    # rather than as a plank leaning on a box.
    hole_w = 0.34
    box(bm, (-hole_w * 0.5, half_y - 0.10, z0), (hole_w * 0.5, half_y + 0.02, z0 + 0.42),
        fy.SKIN_DARK)
    for u in (-1, 1):
        box(bm, (u * hole_w * 0.5 - 0.04, half_y - 0.02, z0),
            (u * hole_w * 0.5 + 0.04, half_y + 0.06, z0 + 0.46), fy.SKIN_TRIM)
    box(bm, (-hole_w * 0.5 - 0.04, half_y - 0.02, z0 + 0.42),
        (hole_w * 0.5 + 0.04, half_y + 0.06, z0 + 0.50), fy.SKIN_TRIM)

    ramp_a = (0.0, half_y - 0.02, z0 + 0.03)
    ramp_b = (0.0, half_y + 0.98, 0.03)
    beam(bm, ramp_a, ramp_b, 0.36, 0.06, fy.SKIN_WOOD, up=(0.0, 0.0, 1.0))
    for i in range(5):
        t = 0.14 + i * 0.18
        y = ramp_a[1] * (1 - t) + ramp_b[1] * t
        z = ramp_a[2] * (1 - t) + ramp_b[2] * t
        box(bm, (-0.17, y - 0.03, z + 0.02), (0.17, y + 0.03, z + 0.07), fy.SKIN_WOOD_DARK)

    # Nest boxes hung off the -Y end, under a lift-up lid.
    box(bm, (-half_x + 0.10, -half_y - 0.46, z0 + 0.10),
        (half_x - 0.10, -half_y + 0.02, z0 + 0.58), fy.SKIN_PAINT_B)
    box(bm, (-half_x + 0.06, -half_y - 0.52, z0 + 0.56),
        (half_x - 0.06, -half_y + 0.04, z0 + 0.66), fy.SKIN_ROOF)

    # A short run of wire fencing off the ramp end.
    run_x, y0 = half_x + 0.30, half_y + 0.14
    y1 = y0 + 1.30
    for sx in (-1, 1):
        for y in (y0, y1):
            box(bm, (sx * run_x - 0.05, y - 0.05, 0.0), (sx * run_x + 0.05, y + 0.05, 0.78),
                fy.SKIN_WOOD_DARK)
    for z in (0.20, 0.70):
        for sx in (-1, 1):
            box(bm, (sx * run_x - 0.03, y0, z), (sx * run_x + 0.03, y1, z + 0.05),
                fy.SKIN_METAL)
        box(bm, (-run_x, y1 - 0.03, z), (run_x, y1 + 0.03, z + 0.05), fy.SKIN_METAL)
    # Uprights standing in for the mesh. Sparse on purpose: a real wire count would cost
    # more triangles than the rest of the coop put together.
    for i in range(7):
        x = -run_x + run_x * 2.0 * (i + 0.5) / 7.0
        box(bm, (x - 0.015, y1 - 0.02, 0.02), (x + 0.015, y1 + 0.02, 0.76), fy.SKIN_METAL)

    # Spilled straw under the ramp, so the coop meets the ground on an uneven line.
    fy.scatter_seed(bm, (0.0, half_y + 0.75, 0.0), 0.55, 7, 0.09, fy.SKIN_STRAW, rng)

    return fy.finish(bm, name)


# --------------------------------------------------------------------------------------
# The windpump
#
# The one building here with a moving part, so it is a hierarchy rather than a single
# mesh:
#
#   Tower   the lattice legs and platform - the root, origin on the ground
#     Head  the gearbox, yaws about its own Z to face the wind
#       Rotor  the fan, spins about its own Y
#       Vane   the tail that does the aiming
#
# Part names are the contract FarmWindpump.cs reads. See toebeans_blender.build_hierarchy
# for why the parts ship as one file rather than as four.
# --------------------------------------------------------------------------------------

def build_windpump(name="Farm_Windpump", height=6.4, base_half=1.15, top_half=0.34):
    tower, head, rotor, vane = bmesh.new(), bmesh.new(), bmesh.new(), bmesh.new()

    def leg_xy(t, sx, sy):
        half = base_half + (top_half - base_half) * t
        return sx * half, sy * half

    # Four battered legs, each standing on an anchor pad.
    #
    # The pads are not decoration. A `beam` caps its ends square to its own run, so a leg
    # that leans - and every leg on a battered tower leans - has one corner of its foot
    # below the point it was told to start at. Starting the legs at ground level put the
    # assembly 5 mm under Z=0 and the origin convention rightly rejected it. A tower of
    # this kind is bolted to concrete anyway, so the pad is what the prop wanted.
    foot = 0.055
    for sx in (-1, 1):
        for sy in (-1, 1):
            a = leg_xy(0.0, sx, sy)
            b = leg_xy(1.0, sx, sy)
            box(tower, (a[0] - 0.13, a[1] - 0.13, 0.0), (a[0] + 0.13, a[1] + 0.13, foot),
                fy.SKIN_DIRT)
            beam(tower, (a[0], a[1], foot + 0.02), (b[0], b[1], height), 0.09, 0.09,
                 fy.SKIN_METAL, up=(0.0, 0.0, 1.0))

    # Braces every level, alternating direction so the lattice reads as a lattice rather
    # than as a ladder. Four levels: more triples the triangle count for no extra read.
    levels = 4
    corners = [(-1, -1), (1, -1), (1, 1), (-1, 1)]
    for i in range(levels):
        t0, t1 = i / levels, (i + 1) / levels
        # Held off the ground for the same reason the legs are: a steeply raked brace has
        # a steeply raked end cap, and the bottom corner of one would go under the pads.
        z0, z1 = max(height * t0, foot + 0.08), height * t1
        for face in range(4):
            sx0, sy0 = corners[face]
            sx1, sy1 = corners[(face + 1) % 4]
            if i % 2 == 0:
                a, b = leg_xy(t0, sx0, sy0), leg_xy(t1, sx1, sy1)
            else:
                a, b = leg_xy(t0, sx1, sy1), leg_xy(t1, sx0, sy0)
            beam(tower, (a[0], a[1], z0), (b[0], b[1], z1), 0.05, 0.05,
                 fy.SKIN_METAL, up=(0.0, 0.0, 1.0))
        h = base_half + (top_half - base_half) * t1
        for sign in (-1, 1):
            box(tower, (-h, sign * h - 0.035, z1 - 0.035),
                (h, sign * h + 0.035, z1 + 0.035), fy.SKIN_METAL)
            box(tower, (sign * h - 0.035, -h, z1 - 0.035),
                (sign * h + 0.035, h, z1 + 0.035), fy.SKIN_METAL)

    box(tower, (-top_half - 0.16, -top_half - 0.16, height),
        (top_half + 0.16, top_half + 0.16, height + 0.07), fy.SKIN_METAL)
    # The pump rod down the middle, and a well head at the foot so the tower is pumping
    # something rather than standing over nothing.
    box(tower, (-0.045, -0.045, 0.30), (0.045, 0.045, height), fy.SKIN_RUST)
    fy.lathe(tower, [(0.34, 0.0), (0.34, 0.34), (0.24, 0.44)], 0.0,
             skin=fy.SKIN_RUST, segments=8)

    hub_z = height + 0.62

    # The head: the gearbox everything else hangs off.
    box(head, (-0.20, -0.30, hub_z - 0.22), (0.20, 0.34, hub_z + 0.22), fy.SKIN_RUST)
    box(head, (-0.13, -0.13, height + 0.05), (0.13, 0.13, hub_z - 0.16), fy.SKIN_METAL)

    # The rotor: a multi-blade fan, which is the American windpump read. Authored about
    # the hub, then lifted into place below.
    blades = 14
    r_out, r_in = 1.35, 0.34
    fy.lathe(rotor, [(0.16, -0.08), (0.16, 0.08)], 0.0, skin=fy.SKIN_RUST, segments=8)
    for i in range(blades):
        a = 2.0 * math.pi * i / blades
        ux, uz = math.cos(a), math.sin(a)
        # Each blade is pitched, so the fan catches the light unevenly as it turns. A fan
        # of untwisted plates strobes instead of spinning.
        with fy.rotated(rotor, 22.0, (ux, 0.0, uz), (0.0, 0.0, 0.0)):
            beam(rotor, (r_in * ux, 0.0, r_in * uz), (r_out * ux, 0.0, r_out * uz),
                 0.28, 0.03, fy.SKIN_METAL, up=(0.0, 1.0, 0.0))
    fy.ring(rotor, (0.0, 0.0, 0.0), (0.0, 1.0, 0.0), r_out - 0.04, r_out + 0.02, 0.05,
            skin=fy.SKIN_METAL, segments=blades)

    # The vane: a tail on a spar, also authored about the hub.
    beam(vane, (0.0, 0.05, 0.0), (0.0, 1.30, 0.06), 0.06, 0.06, fy.SKIN_RUST,
         up=(0.0, 0.0, 1.0))
    prism(vane, [(0.0, 0.92, -0.40), (0.0, 1.66, -0.58),
                 (0.0, 1.66, 0.58), (0.0, 0.92, 0.40)], (0.035, 0.0, 0.0), fy.SKIN_METAL)

    # Lift the two hub-authored parts into the assembly's own space, so build_hierarchy
    # can slide each pivot back onto its object origin.
    rotor_hub = (0.0, -0.34, hub_z)
    vane_hub = (0.0, 0.30, hub_z)
    bmesh.ops.translate(rotor, verts=list(rotor.verts), vec=rotor_hub)
    bmesh.ops.translate(vane, verts=list(vane.verts), vec=vane_hub)

    parts = [
        tb.Part("Tower", tower, (0.0, 0.0, 0.0)),
        tb.Part("Head", head, (0.0, 0.0, hub_z), parent="Tower"),
        tb.Part("Rotor", rotor, rotor_hub, parent="Head"),
        tb.Part("Vane", vane, vane_hub, parent="Head"),
    ]
    return parts, fy.finish_parts(parts)


# --------------------------------------------------------------------------------------

def spec_large():
    return Barn("Farm_Barn")


def spec_small():
    """A second, squarer barn for the far side of the map. Same drivable opening."""
    return Barn("Farm_BarnSmall", span=7.0, length=8.6, wall_h=3.7,
                lower_rise=1.6, upper_rise=1.9, door_w=3.6, door_h=3.4,
                eave=0.45, rake=0.38, windows=2, cupola=False)


if __name__ == "__main__":
    manifest = fy.Manifest("farm_buildings")

    tb.fresh_scene()
    obj, palette = build_barn(spec_large())
    manifest.add(tb.build(obj, obj.name, max_tris=16500, max_size_m=17.0), palette,
                 tag="barn", note="drivable: 4.2 x 4.0 m opening through both gables")

    tb.fresh_scene()
    obj, palette = build_barn(spec_small())
    manifest.add(tb.build(obj, obj.name, max_tris=11500, max_size_m=12.0), palette,
                 tag="barn", note="drivable: 3.6 x 3.4 m opening through both gables")

    tb.fresh_scene()
    obj, palette = build_shed()
    manifest.add(tb.build(obj, obj.name, max_tris=3400, max_size_m=13.0), palette,
                 tag="shed", note="open front, drivable into every bay")

    tb.fresh_scene()
    obj, palette = build_silo()
    manifest.add(tb.build(obj, obj.name, max_tris=5600, max_size_m=12.0), palette,
                 tag="silo")

    tb.fresh_scene()
    obj, palette = build_coop()
    manifest.add(tb.build(obj, obj.name, max_tris=3200, max_size_m=6.0), palette,
                 tag="coop")

    tb.fresh_scene()
    parts, palette = build_windpump()
    stats = tb.build_hierarchy(parts, "Farm_Windpump", palette,
                               chamfer_m=fy.CHAMFER, max_tris=3300, max_size_m=9.0,
                               plan_tolerance=0.45)
    manifest.add(stats, palette, tag="windpump",
                 note="Rotor spins about its local Y; Head yaws about its local Z")

    manifest.write()
