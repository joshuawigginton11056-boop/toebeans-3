"""
Death tower - the landmark on the lava flats.

A wide, squat gothic tower: a lighthouse silhouette that has gone wrong. The reference it
started from is a slender 6:1 lighthouse; this is 3:1, because a landmark seen across the
map wants mass rather than height, and because everything deathly about it - the fangs
under the eaves, the ribs, the horns - needs a body wide enough to hang off.

    blender --background --factory-startup --python Tools/blender/models/tower.py

Built the way the cabins are built, and for the same reason: solid boxes, prisms and
lathed shells, one chamfer pass over the finished mesh. A tower mitred to razor edges
would stand next to Cabin_A looking like it came from a different game.

Three things carry the theme, and each is a rule rather than a decoration:

**The silhouette is one lathe.**  `PROFILE` is a list of (radius, z, slot) rows revolved
into a shell, so the base flare, both roofs, the gallery and the spire are one continuous
surface with no seams to crack open. Widening the barrel or raising a roof is editing one
number in that list, not re-deriving the parts that sit on it.

**Shingle courses are warped, not machined.**  Every ring on a roof slot gets a small
per-vertex wobble off SEED. Without it the tiers read as turned on a lathe, which is
exactly what they are, and a decayed tower cannot look machined.

**Openings are decomposed, not cut.**  Same rule as the cabins. A window here is a lit
panel with a stone surround standing proud of the wall, never a boolean hole - so there is
nothing for the skybox to show through and every face stays quad and bevellable.
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

NAME = "DeathTower"

# The roof warp comes off this, so the same seed is the same tower every time.
SEED = 20260818

# Same chamfer as the cabins, so the two read as one art style standing side by side.
CHAMFER = 0.03
CHAMFER_SEGMENTS = 1
MIN_PART = 0.07        # thinner than this and the chamfer eats the part entirely

SIDES = 12             # sides of the barrel; the facets are the style
PHASE = math.radians(15.0)   # puts a flat facet centre on every 30 deg, incl. 0 and 270

# --------------------------------------------------------------------------------------
# Material slots.
#
# Slot order is the contract anything indexing these assumes - append to it, never
# reorder it. Slot 3 is "the part that glows", the same job CabinGlass does on a cabin
# and the slot the scene's own lava material belongs on.
# --------------------------------------------------------------------------------------

SKIN_STONE = 0      # barrel, base, ribs, window surrounds, rock spurs
SKIN_ROOF = 1       # shingle tiers and the porch gable
SKIN_BONE = 2       # fangs, horns, skulls, spire, the plates down each rib
SKIN_GLOW = 3       # windows, doorway, eye sockets

PALETTE = [
    ("TowerStone", (0.26, 0.25, 0.29), 0.00, 0.90),
    ("TowerShingle", (0.13, 0.07, 0.08), 0.00, 0.85),
    ("TowerBone", (0.72, 0.69, 0.58), 0.00, 0.70),
    ("TowerGlow", (0.35, 1.00, 0.45), 0.00, 0.30),
]


# --------------------------------------------------------------------------------------
# Solids
#
# tb.cuboid/slab/tube cover a tube frame. A tower wants a lathe for the silhouette and a
# convex outline extruded into a solid for everything applied to it.
# --------------------------------------------------------------------------------------

def _paint(faces, skin):
    for f in faces:
        f.material_index = skin


@contextmanager
def moved(bm, matrix):
    """Everything built inside the block gets `matrix` applied to it afterwards.

    Every applied part here - a rib, a fang, a skull - is authored once at +X and then
    put where it goes. Authoring each of the twelve fangs pre-rotated would mean writing
    out its endpoints in a frame nobody can read, and worse, nobody can adjust.
    """
    before = set(bm.verts)
    yield
    fresh = [v for v in bm.verts if v not in before]
    if fresh:
        bmesh.ops.transform(bm, matrix=matrix, verts=fresh)


def lathe(bm, profile, sides=SIDES, phase=PHASE, warp_skins=(), warp_r=0.0, warp_z=0.0,
          rng=None):
    """Revolve (radius, z, slot) rows into a closed shell, capped where radius is 0.

    Rings on a slot in `warp_skins` get a per-vertex wobble, which is what stops the
    shingle tiers reading as turned stock. The wobble is drawn in ring order from `rng`,
    so it is reproducible as long as the profile is.
    """
    rings = []
    for i, (radius, z, skin) in enumerate(profile):
        if radius <= 1e-6:
            rings.append([bm.verts.new((0.0, 0.0, z))])
            continue
        wobble = skin in warp_skins
        ring = []
        for k in range(sides):
            a = phase + math.tau * k / sides
            r = radius + rng.uniform(-warp_r, warp_r) * 0.5 if wobble else radius
            zz = z + rng.uniform(-warp_z, warp_z) * 0.5 if wobble else z
            ring.append(bm.verts.new((r * math.cos(a), r * math.sin(a), zz)))
        rings.append(ring)

    for i in range(len(profile) - 1):
        lo, hi, skin = rings[i], rings[i + 1], profile[i][2]
        if len(lo) == 1 and len(hi) == 1:
            continue
        made = []
        if len(lo) == 1:
            made = [bm.faces.new((lo[0], hi[(k + 1) % sides], hi[k])) for k in range(sides)]
        elif len(hi) == 1:
            made = [bm.faces.new((lo[k], lo[(k + 1) % sides], hi[0])) for k in range(sides)]
        else:
            for k in range(sides):
                k2 = (k + 1) % sides
                made.append(bm.faces.new((lo[k], lo[k2], hi[k2], hi[k])))
        _paint(made, skin)


def prism(bm, points, lo, hi, plane="xz", skin=0):
    """A convex outline extruded into a solid.

    plane "xz": points are (x, z), swept along Y from lo..hi - ribs, the porch block.
    plane "yz": points are (y, z), swept along X from lo..hi - anything facing outward
                off the barrel, which after `moved` is every window and every skull.
    """
    if abs(hi - lo) < MIN_PART - 1e-9:
        raise ValueError(f"prism {abs(hi - lo):.3f} m thick is under MIN_PART")
    rings = []
    for a in (lo, hi):
        rings.append([bm.verts.new((p, a, q) if plane == "xz" else (a, p, q))
                      for (p, q) in points])
    n = len(points)
    made = [bm.faces.new((rings[0][k], rings[0][(k + 1) % n],
                          rings[1][(k + 1) % n], rings[1][k])) for k in range(n)]
    made.append(bm.faces.new(rings[0]))
    made.append(bm.faces.new(list(reversed(rings[1]))))
    _paint(made, skin)


def spike(bm, radius, height, sides=5, skin=SKIN_BONE):
    """A capped cone on +Z. Fangs, horns, thorns, rock spurs - the whole vocabulary."""
    if radius * 2.0 < MIN_PART - 1e-9:
        raise ValueError(f"spike {radius * 2.0:.3f} m across is under MIN_PART")
    base = [bm.verts.new((radius * math.cos(math.tau * k / sides),
                          radius * math.sin(math.tau * k / sides), 0.0))
            for k in range(sides)]
    tip = bm.verts.new((0.0, 0.0, height))
    made = [bm.faces.new((base[k], base[(k + 1) % sides], tip)) for k in range(sides)]
    made.append(bm.faces.new(list(reversed(base))))
    _paint(made, skin)


def box(bm, centre, size, skin=0):
    """An axis-aligned box given its centre and full size."""
    if min(size) < MIN_PART - 1e-9:
        raise ValueError(f"box {min(size):.3f} m on its thinnest axis is under MIN_PART")
    made = bmesh.ops.create_cube(bm, size=1.0, matrix=(
        Matrix.Translation(Vector(centre)) @ Matrix.Diagonal(Vector(size)).to_4x4()))
    for v in made["verts"]:
        for f in v.link_faces:
            f.material_index = skin


def turn(degrees):
    return Matrix.Rotation(math.radians(degrees), 4, "Z")


def to(x, y, z):
    return Matrix.Translation((x, y, z))


def tip(degrees, axis="Y"):
    return Matrix.Rotation(math.radians(degrees), 4, axis)


# --------------------------------------------------------------------------------------
# The silhouette
#
# One lathe, bottom to spire. Radii are measured to the corners of the twelve-sided
# barrel, so the flat of a facet sits at radius * cos(15 deg) - which is what every
# applied part measures its own stand-off from.
# --------------------------------------------------------------------------------------

PROFILE = [
    (0.00,  0.00, SKIN_STONE),
    (5.10,  0.00, SKIN_STONE),   # rock foot
    (5.10,  0.32, SKIN_STONE),
    (4.55,  0.60, SKIN_STONE),
    (3.85,  1.50, SKIN_STONE),   # flared base
    (3.10,  1.80, SKIN_STONE),
    (2.95,  2.05, SKIN_STONE),   # lower barrel
    (2.82,  4.60, SKIN_STONE),
    (2.74,  6.15, SKIN_STONE),
    (2.92,  6.30, SKIN_STONE),   # corbel
    (2.78,  6.55, SKIN_STONE),
    (2.64,  6.80, SKIN_STONE),
    (4.75,  6.90, SKIN_ROOF),    # mid roof, tier 1
    (4.48,  7.28, SKIN_ROOF),
    (4.10,  7.28, SKIN_ROOF),    # tier 2
    (3.72,  7.66, SKIN_ROOF),
    (3.34,  7.66, SKIN_ROOF),    # tier 3
    (2.90,  8.05, SKIN_ROOF),
    (2.42,  8.05, SKIN_STONE),   # upper barrel
    (2.33, 10.45, SKIN_STONE),
    (2.58, 10.45, SKIN_STONE),   # gallery
    (3.20, 10.66, SKIN_STONE),
    (3.15, 11.02, SKIN_STONE),
    (2.66, 11.14, SKIN_STONE),
    (2.20, 11.14, SKIN_STONE),   # lantern drum
    (2.06, 13.10, SKIN_STONE),
    (3.55, 13.22, SKIN_ROOF),    # crown roof, tier 1
    (3.28, 13.64, SKIN_ROOF),
    (2.92, 13.64, SKIN_ROOF),    # tier 2
    (2.55, 14.08, SKIN_ROOF),
    (2.18, 14.08, SKIN_ROOF),    # tier 3
    (1.76, 14.54, SKIN_ROOF),
    (1.40, 14.54, SKIN_ROOF),    # tier 4
    (0.95, 15.02, SKIN_ROOF),
    (0.44, 15.02, SKIN_BONE),    # finial
    (0.52, 15.26, SKIN_BONE),
    (0.34, 15.50, SKIN_BONE),
    (0.26, 16.80, SKIN_BONE),    # spire
    (0.14, 17.70, SKIN_BONE),
    (0.00, 18.50, SKIN_BONE),
]


def facet(radius):
    """The flat of a facet on a twelve-sided ring of this radius."""
    return radius * math.cos(math.pi / SIDES)


# --------------------------------------------------------------------------------------
# Applied parts
# --------------------------------------------------------------------------------------

def add_ribs(bm):
    """Six buttresses, each with bone plates strapped down its outer edge.

    On the six facets that carry no window, so a rib can never land across one however
    the windows move - the same reason `add_frame` puts the cabins' braces in the piers.
    """
    outline = [(2.25, 1.55), (4.25, 1.72), (3.98, 2.75), (3.52, 4.45), (3.12, 6.32),
               (2.25, 6.38)]
    plates = [(4.00, 2.35, 0.26), (3.70, 3.70, 0.22), (3.34, 5.25, 0.18)]
    for k in range(6):
        with moved(bm, turn(k * 60.0)):
            prism(bm, outline, -0.26, 0.26, "xz", SKIN_STONE)
            for (x, z, half) in plates:
                box(bm, (x, 0.0, z), (0.20, half * 2.0, 0.18), SKIN_BONE)
            with moved(bm, to(4.05, 0.0, 2.05) @ tip(72)):
                spike(bm, 0.19, 0.80)


def add_window(bm, angle, wall, z0, z1, half_w, apex, mullions=0):
    """A lit pointed arch standing proud of the wall, with a stone surround.

    half_w plus the surround's margin has to stay inside the flat of the facet, or the
    surround's corners poke out through the two facets either side of it - which reads as
    a modelling error from every angle except straight on.
    """
    margin = 0.19
    # `wall` is the flat of the facet, so the facet reaches wall * tan(180/sides) either
    # side of its centre line. Anything wider than that is hanging over the fold.
    if half_w + margin > wall * math.tan(math.pi / SIDES):
        raise ValueError(
            f"window at {angle:.0f} deg is {2 * (half_w + margin):.2f} m wide, "
            f"wider than the {2 * wall * math.tan(math.pi / SIDES):.2f} m facet it sits on")
    lit = [(-half_w, z0), (half_w, z0), (half_w, z1), (0.0, z1 + apex), (-half_w, z1)]
    surround = [(-half_w - margin, z0 - 0.16), (half_w + margin, z0 - 0.16),
                (half_w + margin, z1), (0.0, z1 + apex + 0.28), (-half_w - margin, z1)]
    with moved(bm, turn(angle)):
        prism(bm, surround, wall - 0.30, wall + 0.13, "yz", SKIN_STONE)
        prism(bm, lit, wall - 0.20, wall + 0.17, "yz", SKIN_GLOW)
        for i in range(mullions):
            y = -half_w + half_w * 2.0 * (i + 1) / (mullions + 1)
            height = z1 + apex * 0.55 - z0
            box(bm, (wall + 0.16, y, (z0 + z1 + apex * 0.55) * 0.5),
                (0.10, 0.14, height), SKIN_STONE)


def add_skull(bm, matrix, scale=1.0):
    """The motif, facing +X before `matrix` puts it where it goes.

    Teeth are dropped below full size rather than scaled with the rest: at 0.62 they come
    out under MIN_PART, and a chamfer that eats a part leaves the zero-area faces that
    validate() rejects.
    """
    s = scale
    with moved(bm, matrix @ Matrix.Scale(s, 4)):
        prism(bm, [(-0.42, -0.28), (-0.31, 0.32), (0.31, 0.32), (0.42, -0.28),
                   (0.25, -0.44), (-0.25, -0.44)], -0.34, 0.30, "yz", SKIN_BONE)
        prism(bm, [(-0.20, -0.42), (0.20, -0.42), (0.17, -0.62), (-0.17, -0.62)],
              -0.22, 0.24, "yz", SKIN_BONE)
        for sy in (-1, 1):
            prism(bm, [(sy * 0.30, -0.02), (sy * 0.09, -0.02),
                       (sy * 0.09, 0.17), (sy * 0.30, 0.17)], 0.22, 0.32, "yz", SKIN_GLOW)
        prism(bm, [(-0.07, -0.24), (0.07, -0.24), (0.0, -0.06)], 0.22, 0.31, "yz",
              SKIN_GLOW)
    if scale >= 0.8:
        for i in range(3):
            with moved(bm, matrix @ to(0.20, -0.12 + i * 0.12, -0.42) @ tip(180)):
                spike(bm, 0.06, 0.18)


def add_porch(bm):
    """The entrance, on the facet centred at 270 deg - Blender -Y, which is +Z in Unity.

    A 2.1 m doorway on a 2.55 m wall, the same architectural sizing the cabins use, so a
    tower and a house in one shot agree about how big a door is.
    """
    with moved(bm, turn(270.0)):
        prism(bm, [(2.30, 0.00), (4.60, 0.00), (4.60, 2.50), (2.30, 2.50)],
              -1.15, 1.15, "xz", SKIN_STONE)
        prism(bm, [(-1.38, 2.42), (1.38, 2.42), (0.0, 3.80)], 2.30, 4.78, "yz", SKIN_ROOF)
        prism(bm, [(-0.95, 0.0), (0.95, 0.0), (0.95, 1.55), (0.0, 2.35), (-0.95, 1.55)],
              4.47, 4.64, "yz", SKIN_BONE)
        prism(bm, [(-0.72, 0.0), (0.72, 0.0), (0.72, 1.45), (0.0, 2.10), (-0.72, 1.45)],
              4.53, 4.70, "yz", SKIN_GLOW)
        add_skull(bm, to(4.72, 0.0, 2.95))
        for s in (-1, 1):
            with moved(bm, to(4.42, s * 1.28, 2.18) @ tip(s * -22, "X")):
                spike(bm, 0.24, 1.30)


def add_teeth(bm):
    """Fangs under both eaves, thorns round the gallery, barbs up the spire."""
    for k in range(SIDES):
        a = math.degrees(PHASE) + k * 30.0
        with moved(bm, turn(a) @ to(4.72, 0.0, 6.93) @ tip(180)):
            spike(bm, 0.17, 0.78)
        with moved(bm, turn(a) @ to(3.52, 0.0, 13.25) @ tip(180)):
            spike(bm, 0.15, 0.62)
        with moved(bm, turn(a + 15.0) @ to(2.96, 0.0, 11.04)):
            spike(bm, 0.10, 0.50)
        with moved(bm, turn(a + 15.0) @ to(0.24, 0.0, 15.60 + 0.55 * (k % 3)) @ tip(115)):
            spike(bm, 0.09, 0.34)


def add_horns(bm):
    """Four tusks off the gallery, each rising from a skull.

    Thick and short rather than long and thin: at 3 m and a 0.30 m base they read as
    needles from across the map, which is the wrong animal entirely.
    """
    for k in range(4):
        base = turn(45.0 + k * 90.0)
        with moved(bm, base @ to(2.98, 0.0, 10.90) @ tip(26)):
            spike(bm, 0.46, 2.30, sides=6)
        add_skull(bm, base @ to(3.16, 0.0, 10.62), scale=0.62)


def add_rocks(bm, rng):
    """Spurs round the foot, and masonry proud of the barrel.

    Both are what stops the base reading as a cylinder set on the ground. The spurs lean
    outward, so a kart that clips one is turned away from the wall rather than stopped by
    a step - the mesh is the collider here as everywhere.
    """
    for k in range(8):
        with moved(bm, turn(22.5 + k * 45.0) @ to(4.55, 0.0, 0.05)
                   @ tip(10 + 6 * (k % 3))):
            spike(bm, 0.55 + 0.12 * (k % 3), 1.5 + 0.45 * (k % 4))

    for (a, z, half) in [(15, 2.4, .18), (75, 3.3, .15), (105, 5.1, .20), (165, 2.2, .16),
                         (195, 4.2, .19), (255, 5.4, .15), (285, 3.6, .17),
                         (345, 4.8, .18), (45, 8.9, .14), (135, 9.6, .13),
                         (225, 8.4, .15), (315, 9.9, .14)]:
        with moved(bm, turn(a)):
            box(bm, (2.76 if z < 7 else 2.31, 0.0, z + rng.uniform(-0.05, 0.05)),
                (0.48, half * 2.0, 0.48), SKIN_STONE)


# --------------------------------------------------------------------------------------
# Assembly
# --------------------------------------------------------------------------------------

def build_tower():
    """One tower, one mesh, chamfered once at the end."""
    rng = random.Random(SEED)
    bm = bmesh.new()

    lathe(bm, PROFILE, warp_skins=(SKIN_ROOF,), warp_r=0.16, warp_z=0.10, rng=rng)

    add_ribs(bm)
    # Windows go on facet centres. Three low, three high on the opposite alternation, six
    # round the lantern - the lit band that makes it read as a lighthouse gone wrong.
    for a in (270.0, 30.0, 150.0):
        add_window(bm, a, facet(2.82), 2.95, 4.55, 0.50, 0.80, mullions=2)
    for a in (90.0, 210.0, 330.0):
        add_window(bm, a, facet(2.37), 8.80, 9.70, 0.33, 0.55, mullions=1)
    for a in range(0, 360, 60):
        add_window(bm, float(a), facet(2.13), 11.45, 12.55, 0.30, 0.46, mullions=1)

    add_porch(bm)
    add_teeth(bm)
    add_horns(bm)
    add_rocks(bm, rng)

    # The chamfer, applied once to everything, for the reason build_cabin gives: doing it
    # per part gives a fang meeting an eave two different corner widths, which is the tell
    # that a thing was assembled out of solids.
    bmesh.ops.bevel(
        bm,
        geom=list(bm.verts) + list(bm.edges) + list(bm.faces),
        offset=CHAMFER,
        offset_type="OFFSET",
        segments=CHAMFER_SEGMENTS,
        profile=0.5,
        affect="EDGES",
        clamp_overlap=True,
        loop_slide=True,
        material=-1,          # bevel faces inherit their neighbour's slot, so the split holds
    )
    bmesh.ops.dissolve_degenerate(bm, dist=1e-5, edges=bm.edges)

    obj = tb.mesh_from_bmesh(bm, NAME)
    tb.assign_materials(obj, PALETTE)
    return obj


if __name__ == "__main__":
    # A landmark, not a scattered prop: one per map, filling the screen from a long way
    # off, so it carries more than a cabin does. Still the collider, so still budgeted.
    tb.fresh_scene()
    tb.build(build_tower(), NAME, max_tris=13500, max_size_m=20.0)
