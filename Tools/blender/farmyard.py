"""
The farm pack's shared vocabulary: one palette, the wheels, and the build manifest.

Everything under `models/farm_*.py` is one set. A barn, a plough and a duck have almost
no geometry in common, but they have to look like they came from the same place, and the
thing that decides that is not the modelling - it is that a rusted bolt on the plough is
literally the same colour as a rusted hinge on the barn door. So the colours live here and
nowhere else, as one numbered palette every farm script indexes into.

Three things this module exists to do:

  One palette, per-prop slots.  A prop that declared all fifteen pack colours would arrive
                    in Unity with fifteen submeshes, most of them empty, and empty
                    submeshes are renderer material entries that cost draw calls for
                    nothing. So scripts tag faces with pack-wide `SKIN_*` numbers and
                    `finish`/`finish_parts` compacts them at the end: the mesh keeps only
                    the colours it actually uses, while the source still reads
                    `SKIN_RUST` everywhere.

  A build manifest.  BarrierAssetSetup.cs carries a hand-written copy of the palette its
                    Blender script produces, with a comment noting the two have to match.
                    That is a drift waiting to happen, and this pack is five times the
                    size. Instead each script writes what it built - names, sizes, the
                    colours, which slot each mesh wears - and the Unity side reads that.
                    Adding a prop needs no C# change at all.

  Wheels.           A tractor, a truck, a trailer and a plough all roll on the same three
                    or four wheels. Modelling them once is obvious; the reason it is in
                    this module rather than in whichever script needed one first is that a
                    wheel is also the pack's clearest scale reference, and every script
                    should be measuring against the same one.
"""

import json
import math
import os

import bmesh
from mathutils import Matrix, Vector

import toebeans_blender as tb

# --------------------------------------------------------------------------------------
# Look
# --------------------------------------------------------------------------------------

#: One pass over the finished mesh, same as the cabins. Wide enough to catch a highlight
#: on every corner at kart speed, narrow enough that a 0.07 m batten keeps a flat in the
#: middle. Everything in this pack respects MIN_PART for that reason.
CHAMFER = 0.03
CHAMFER_SEGMENTS = 1
MIN_PART = 0.07

# --------------------------------------------------------------------------------------
# The palette
#
# Indices are the contract between a model script and this list. Append, never reorder -
# a renumber silently repaints every prop in the pack, and the compaction below means it
# would not even change the slot count, so nothing would look obviously broken.
#
# Colours are deliberately pulled toward the map's existing ones: the barn is oxide red
# rather than pillarbox, the tin is a cold grey rather than silver, and the straw is dulled
# off pure yellow. A farm rendered at postcard saturation would read as a different game
# next to LavaWorld's terrain.
# --------------------------------------------------------------------------------------

SKIN_PAINT = 0       # barn red - the pack's hero colour, used sparingly
SKIN_PAINT_B = 1     # the second painted colour: coop, truck cab, cart bed
SKIN_TRIM = 2        # white-ish trim: door battens, window frames, fence pickets
SKIN_WOOD = 3        # weathered bare timber: posts, rails, planks, crates
SKIN_WOOD_DARK = 4   # creosoted or old timber: sleepers, gate posts, sills
SKIN_ROOF = 5        # dark roofing: shingle and painted tin
SKIN_METAL = 6       # galvanised steel: silo, hardware, implement frames
SKIN_RUST = 7        # oxidised iron: shares, blades, old tools, hinges
SKIN_STRAW = 8       # hay, straw, thatch, bale, nesting
SKIN_DIRT = 9        # earth, mud, spilled feed
SKIN_GLASS = 10      # window glass and lamp lenses - the pack's "glows" slot
SKIN_RUBBER = 11     # tyres
SKIN_DARK = 12       # what you see through an opening; never a hole to the skybox
SKIN_GREEN = 13      # foliage, crop, moss
SKIN_WATER = 14      # trough and pond water

# The livestock half of the palette. These live in the same list as the buildings rather
# than in one of their own for the reason the compaction above makes possible: a prop only
# ever carries the colours it uses, so a barn is no more expensive for the existence of a
# duck. One list means one set of Unity materials for the whole pack, which is what keeps
# a cow's hoof the same colour as a horse's, and keeps every prop in one batch.

SKIN_HIDE = 15       # the main hide: warm brown
SKIN_HIDE_ALT = 16   # the marking colour: near black. Cow patches, sheep face and legs
SKIN_WOOL = 17       # off-white: fleece, cow patches, duck breast
SKIN_FLESH = 18      # pink: snout, udder, inside of an ear
SKIN_COMB = 19       # bright red: comb and wattle
SKIN_BEAK = 20       # orange: beak, bill, bird legs and feet
SKIN_PLUME = 21      # copper: chicken body and tail feathers
SKIN_DRAKE = 22      # mallard green: a drake's head
SKIN_HOOF = 23       # dark horn: hooves and horns
SKIN_EYE = 24        # the eye. Its own slot rather than SKIN_DARK, because an eye wants
                     # a little sheen and a doorway behind an opening emphatically does not

#: (name, rgb, metallic, roughness). Names are what the FBX carries and what the Unity
#: side matches on, so they are prefixed to keep them out of the way of the store packs.
PALETTE = [
    ("Farm_Paint",    (0.412, 0.118, 0.098), 0.00, 0.86),
    ("Farm_PaintB",   (0.278, 0.345, 0.325), 0.00, 0.84),
    ("Farm_Trim",     (0.842, 0.822, 0.760), 0.00, 0.80),
    ("Farm_Wood",     (0.353, 0.255, 0.169), 0.00, 0.86),
    ("Farm_WoodDark", (0.180, 0.133, 0.102), 0.00, 0.82),
    ("Farm_Roof",     (0.224, 0.235, 0.251), 0.05, 0.72),
    ("Farm_Metal",    (0.545, 0.569, 0.600), 0.65, 0.42),
    ("Farm_Rust",     (0.404, 0.208, 0.125), 0.25, 0.78),
    ("Farm_Straw",    (0.757, 0.620, 0.286), 0.00, 0.90),
    ("Farm_Dirt",     (0.286, 0.224, 0.165), 0.00, 0.94),
    ("Farm_Glass",    (0.596, 0.714, 0.757), 0.00, 0.22),
    ("Farm_Rubber",   (0.078, 0.078, 0.086), 0.00, 0.68),
    ("Farm_Dark",     (0.043, 0.039, 0.039), 0.00, 0.96),
    ("Farm_Green",    (0.267, 0.384, 0.192), 0.00, 0.88),
    ("Farm_Water",    (0.196, 0.353, 0.408), 0.00, 0.18),

    ("Farm_Hide",     (0.408, 0.290, 0.204), 0.00, 0.92),
    ("Farm_HideAlt",  (0.129, 0.118, 0.114), 0.00, 0.90),
    ("Farm_Wool",     (0.855, 0.839, 0.796), 0.00, 0.94),
    ("Farm_Flesh",    (0.843, 0.596, 0.573), 0.00, 0.86),
    ("Farm_Comb",     (0.702, 0.153, 0.129), 0.00, 0.80),
    ("Farm_Beak",     (0.878, 0.612, 0.176), 0.00, 0.62),
    ("Farm_Plume",    (0.494, 0.259, 0.125), 0.00, 0.88),
    ("Farm_Drake",    (0.145, 0.325, 0.251), 0.10, 0.48),
    ("Farm_Hoof",     (0.216, 0.196, 0.184), 0.00, 0.70),
    ("Farm_Eye",      (0.043, 0.043, 0.055), 0.00, 0.30),
]

#: Slots whose material should be emissive-capable in Unity. Glass on a farm at dusk is
#: the same job CabinGlass does on a cabin: the one part of the prop that is a light
#: source rather than a surface.
EMISSIVE = {"Farm_Glass"}

assert len(PALETTE) == 25, "palette length is part of the slot contract"
assert PALETTE[SKIN_EYE][0] == "Farm_Eye", "SKIN_* indices must match PALETTE order"


# --------------------------------------------------------------------------------------
# Finishing
# --------------------------------------------------------------------------------------

def _used_slots(bms):
    used = set()
    for bm in bms:
        for f in bm.faces:
            used.add(f.material_index)
    return sorted(used)


def _compact(bms):
    """Renumber faces onto a palette holding only the colours these meshes actually use.

    Returns the sub-palette and the list of pack indices it was built from. Done across
    every bmesh handed in at once, so all the parts of one prop end up on one slot list -
    Unity's material remap is per file, not per object, and a leg that numbered its slots
    differently from the body would come out of the remap wearing the body's colours.
    """
    used = _used_slots(bms)
    if not used:
        raise ValueError("no faces to compact")
    if max(used) >= len(PALETTE):
        raise ValueError(f"face tagged with slot {max(used)}, past the end of PALETTE")

    remap = {old: new for new, old in enumerate(used)}
    for bm in bms:
        for f in bm.faces:
            f.material_index = remap[f.material_index]
    return [PALETTE[i] for i in used], used


# --------------------------------------------------------------------------------------
# Facing
#
# The export convention lands a prop in Unity yawed 180 degrees, and this is where the
# pack turns it back.
#
# Measured rather than assumed, twice from opposite ends. The FBX stores Blender (x, y, z)
# as file (x, z, -y); Unity then negates X on import, because FBX is right-handed and
# Unity is not. The two compose to Blender (x, y, z) -> Unity (-x, z, -y), which is the
# clean mapping with a half turn about up on top of it.
#
# `verify_axes.py` never caught it because it only ever compared *dimensions*, and a
# dimension has no sign - a half turn is invisible to it. What made it visible was a
# tractor: symmetric props do not care which way round they are, and the first prop in
# this project that does is the first one to notice.
#
# `kart_buggy.py` already knew, and solved it by authoring every point in Unity space and
# converting at each call site through its `u()` helper. That is not available here, where
# the numbers are Blender numbers throughout and there are far more of them - so the whole
# prop is turned once, at the end, which comes to the same thing.
#
# Negating two axes is a rotation, not a mirror: the determinant is +1, so nothing comes
# out inside-out and no faces need reversing. Written as a diagonal rather than as
# Matrix.Rotation(pi) because sin(pi) is not zero in floating point, and a prop that picks
# up a 1e-16 shear every build is a file that never diffs clean.
# --------------------------------------------------------------------------------------

_FACE_UNITY = Matrix.Diagonal(Vector((-1.0, -1.0, 1.0, 1.0)))


def face_unity(bm):
    """Turn a finished prop so its Blender +Y arrives as Unity +Z, which is forward."""
    bmesh.ops.transform(bm, matrix=_FACE_UNITY, verts=list(bm.verts))


def finish(bm, name, chamfer_m=CHAMFER):
    """Chamfer, compact the palette, and hand back an object ready for `tb.build`.

    The tail of every single-mesh farm script. Returns (obj, palette) - the palette so the
    caller can put it in the manifest, because what the mesh wears is the thing the Unity
    side cannot work out for itself.
    """
    face_unity(bm)
    if chamfer_m:
        tb.chamfer(bm, chamfer_m, CHAMFER_SEGMENTS)
    palette, _ = _compact([bm])
    obj = tb.mesh_from_bmesh(bm, name)
    tb.assign_materials(obj, palette)
    return obj, palette


def finish_parts(parts, chamfer_m=CHAMFER):
    """The same for a multi-part prop: compact once across every part, then hand back
    the palette for `tb.build_hierarchy` to assign and the manifest to record.

    The chamfer is left to `build_hierarchy`, which applies it per part at one offset.

    Pivots turn with the geometry. A joint is a point in the same space the vertices are
    in, and turning one without the other puts every limb's axis of rotation somewhere the
    limb is not.
    """
    for p in parts:
        face_unity(p.bm)
        p.pivot = Vector((-p.pivot.x, -p.pivot.y, p.pivot.z))

    palette, _ = _compact([p.bm for p in parts])
    return palette


# --------------------------------------------------------------------------------------
# Shapes the pack keeps needing
# --------------------------------------------------------------------------------------

def ring(bm, centre, axis, r_inner, r_outer, width, skin=0, segments=12):
    """A flat annulus of `width` along `axis` - a tyre carcass, a rim, a barrel hoop.

    Built as a strip of quads rather than as a boolean of two cylinders, because a boolean
    leaves coplanar slivers that the chamfer pass turns into shading artefacts, and because
    an annulus with a genuine inner wall is watertight and a difference is not always.
    """
    centre = Vector(centre)
    axis = Vector(axis).normalized()
    # Any two vectors perpendicular to the axle. `orthogonal` picks one deterministically,
    # which matters: a wheel whose facets start at a different angle each build is a
    # diffable file that never diffs clean.
    u = axis.orthogonal().normalized()
    v = axis.cross(u).normalized()
    half = axis * (width * 0.5)

    def at(radius, i, side):
        a = 2.0 * math.pi * i / segments
        return centre + (u * math.cos(a) + v * math.sin(a)) * radius + half * side

    made = []
    mine = []
    for i in range(segments):
        j = (i + 1) % segments
        quads = (
            (at(r_outer, i, 1), at(r_outer, j, 1), at(r_outer, j, -1), at(r_outer, i, -1)),
            (at(r_inner, i, -1), at(r_inner, j, -1), at(r_inner, j, 1), at(r_inner, i, 1)),
            (at(r_inner, i, 1), at(r_inner, j, 1), at(r_outer, j, 1), at(r_outer, i, 1)),
            (at(r_outer, i, -1), at(r_outer, j, -1), at(r_inner, j, -1), at(r_inner, i, -1)),
        )
        for corners in quads:
            verts = [bm.verts.new(c) for c in corners]
            mine.extend(verts)
            made.append(bm.faces.new(verts))

    for f in made:
        f.material_index = skin
    # The quads were built corner by corner, so every shared edge is currently two
    # coincident vertices. Weld them or the annulus is a pile of loose plates: validate
    # would pass it and Unity would light it as though every facet were an island.
    #
    # Only this ring's vertices, never the whole mesh. A weld over `bm.verts` would also
    # fuse whatever the caller built earlier wherever two parts happen to touch exactly,
    # which is how a wheel silently merges into the fender it is parked against.
    bmesh.ops.remove_doubles(bm, verts=mine, dist=1e-5)


def wheel(bm, hub, axis, radius, width, lugs=0, rim_fraction=0.62,
          skin_tyre=SKIN_RUBBER, skin_rim=SKIN_METAL, segments=12, lug_depth=0.045):
    """A wheel centred on `hub` with its axle along `axis`.

    `lugs` is the count of angled tread bars; 0 leaves a smooth tyre. They are carved
    *inward* from `radius` rather than stuck on top of it, which is the rule the kart
    README states and the reason is the same here: anything modelled prouder than the
    nominal radius sinks into the road, because the thing holding the hub up is the radius.

    `rim_fraction` is where the metal ends and the rubber starts. A tractor rear wheel
    wants a small rim in a tall tyre; a truck wheel is nearly all rim.
    """
    hub = Vector(hub)
    axis = Vector(axis).normalized()
    r_rim = radius * rim_fraction

    ring(bm, hub, axis, r_rim, radius, width, skin=skin_tyre, segments=segments)
    ring(bm, hub, axis, radius * 0.14, r_rim, width * 0.72, skin=skin_rim, segments=segments)

    # The hub cap closes the middle. Without it the wheel is a tube you can see through
    # from the inside of the arch, which reads as a hole rather than as a wheel.
    u = axis.orthogonal().normalized()
    v = axis.cross(u).normalized()
    tb.tube(bm, hub - axis * width * 0.40, hub + axis * width * 0.40,
            radius * 0.16, skin=skin_rim, segments=segments // 2 or 3)

    if lugs:
        # Tread bars, alternating side to side and leaning against the direction of
        # travel, which is what makes a tractor tyre read as a tractor tyre at distance.
        for i in range(lugs):
            a = 2.0 * math.pi * i / lugs
            out = (u * math.cos(a) + v * math.sin(a))
            side = 1 if i % 2 else -1
            near = hub + out * (radius - lug_depth) + axis * (width * 0.05 * side)
            far = hub + out * (radius - lug_depth * 0.15) + axis * (width * 0.34 * side)
            tb.beam(bm, near, far, width * 0.30, lug_depth * 1.5,
                    skin=skin_tyre, up=out)


def corrugate(bm, x0, x1, y0, y1, z, thickness, ribs, skin=SKIN_ROOF, rib_rise=0.035):
    """A flat roof or wall panel with ribs running along Y - stylised corrugated tin.

    Modelled as a slab plus raised bars rather than as a folded sheet. A real fold doubles
    the face count of the largest surfaces in the pack for a read that survives about ten
    metres, and a folded sheet has no thickness, which is the one thing the chamfer needs.
    """
    tb.box(bm, (x0, y0, z), (x1, y1, z + thickness), skin)
    if ribs < 1:
        return
    step = (x1 - x0) / ribs
    for i in range(ribs):
        cx = x0 + step * (i + 0.5)
        tb.box(bm, (cx - step * 0.16, y0, z + thickness),
               (cx + step * 0.16, y1, z + thickness + rib_rise), skin)


def planks(bm, x0, x1, y, thickness, z0, z1, count, skin=SKIN_WOOD, gap=0.012,
           axis="x", batten_skin=None, batten_w=0.075):
    """A run of vertical boards filling a rectangle, optionally with battens over the joins.

    Board-and-batten is the barn read, and it is worth the geometry: a barn wall as one
    flat box is the single most obvious way a stylised building looks unfinished, because
    nothing breaks the light across six metres of it.
    """
    span = x1 - x0
    pitch = span / count
    for i in range(count):
        a = x0 + pitch * i + gap * 0.5
        b = a + pitch - gap
        if axis == "x":
            tb.box(bm, (a, y - thickness * 0.5, z0), (b, y + thickness * 0.5, z1), skin)
        else:
            tb.box(bm, (y - thickness * 0.5, a, z0), (y + thickness * 0.5, b, z1), skin)

    if batten_skin is None:
        return
    for i in range(1, count):
        c = x0 + pitch * i
        out = thickness * 0.5 + 0.016
        if axis == "x":
            tb.box(bm, (c - batten_w * 0.5, y - out - 0.018, z0),
                   (c + batten_w * 0.5, y - out, z1), batten_skin)
        else:
            tb.box(bm, (y - out - 0.018, c - batten_w * 0.5, z0),
                   (y - out, c + batten_w * 0.5, z1), batten_skin)


def lathe(bm, profile, axis_z0, skin=0, segments=12, close_bottom=True, close_top=True,
          centre=(0.0, 0.0)):
    """Revolve a (radius, z) profile about a vertical axis through `centre`.

    Silos, churns, barrels, water butts and the round bale caps are all this shape, and
    doing them as stacked cylinders leaves a visible step at every joint that the chamfer
    then highlights rather than hides.

    `centre` is in X and Y only. Everything this makes stands up, and a lathe that could
    also be tilted would need a full frame - which is what `tube` already is.
    """
    if len(profile) < 2:
        raise ValueError("a profile needs at least two points")

    cx, cy = centre
    rings = []
    for radius, z in profile:
        rings.append([
            bm.verts.new(Vector((
                cx + math.cos(2.0 * math.pi * i / segments) * radius,
                cy + math.sin(2.0 * math.pi * i / segments) * radius,
                axis_z0 + z)))
            for i in range(segments)])

    made = []
    for lower, upper in zip(rings, rings[1:]):
        for i in range(segments):
            j = (i + 1) % segments
            quad = [lower[i], lower[j], upper[j], upper[i]]
            # A profile that touches the axis makes a triangle, not a quad. Two of the
            # four corners are then the same vertex and bm.faces.new refuses it.
            unique = []
            for v in quad:
                if v not in unique:
                    unique.append(v)
            if len(unique) >= 3:
                made.append(bm.faces.new(unique))

    if close_bottom and profile[0][0] > 1e-5:
        made.append(bm.faces.new(list(reversed(rings[0]))))
    if close_top and profile[-1][0] > 1e-5:
        made.append(bm.faces.new(rings[-1]))

    for f in made:
        f.material_index = skin


def loft(bm, rings, skin=0, sides=8, cap_start=True, cap_end=True, phase=0.5):
    """A tube of elliptical cross-sections strung along Y. The animals are made of this.

    Each ring is `(y, cx, cz, rx, rz)`: where along the body, where the centre of that
    slice sits in X and Z, and how wide and how deep it is. Give it four rings and you
    have a body that is narrow at the shoulder, deep at the belly and tapers to the tail -
    which no arrangement of boxes gets to for the same triangle count.

    Boxes are what the buildings are made of, and they are right there. An animal is the
    one thing in this pack that has no flat faces at all, and a cow assembled from cuboids
    is a cow-shaped crate however carefully the cuboids are placed.

    `phase` offsets the first vertex by that fraction of a facet. The default half-step
    puts flats on the top and bottom of the section rather than points, which is what a
    back and a belly want; a leg, wanting a point forward, passes 0.
    """
    if len(rings) < 2:
        raise ValueError("a loft needs at least two rings")

    made_rings = []
    for y, cx, cz, rx, rz in rings:
        made_rings.append([
            bm.verts.new(Vector((
                cx + math.cos(math.tau * (i + phase) / sides) * rx,
                y,
                cz + math.sin(math.tau * (i + phase) / sides) * rz)))
            for i in range(sides)])

    made = []
    for lower, upper in zip(made_rings, made_rings[1:]):
        for i in range(sides):
            j = (i + 1) % sides
            quad = [lower[i], lower[j], upper[j], upper[i]]
            unique = []
            for v in quad:
                if v not in unique:
                    unique.append(v)
            if len(unique) >= 3:
                made.append(bm.faces.new(unique))

    # A ring that has collapsed to a point needs no cap, and asking for one is a face with
    # no area - which validate rejects, correctly.
    if cap_start and (rings[0][3] > 1e-5 and rings[0][4] > 1e-5):
        made.append(bm.faces.new(list(reversed(made_rings[0]))))
    if cap_end and (rings[-1][3] > 1e-5 and rings[-1][4] > 1e-5):
        made.append(bm.faces.new(made_rings[-1]))

    for f in made:
        f.material_index = skin

    # Handed back so a caller can repaint part of the surface afterwards - a cow's patches
    # are faces of the body, not lumps stuck on it, and that is only reachable from here.
    return made


def bale_round(bm, centre, axis, radius, width, skin=SKIN_STRAW, segments=12):
    """A round bale: a cylinder on its side with the wrap lines showing on the ends."""
    centre = Vector(centre)
    axis = Vector(axis).normalized()
    tb.tube(bm, centre - axis * width * 0.5, centre + axis * width * 0.5,
            radius, skin=skin, segments=segments)
    # Two shallow bands round the barrel. On a faceted cylinder these catch the light
    # differently from the body and read as the netting without costing an unwrap.
    for t in (-0.24, 0.24):
        ring(bm, centre + axis * width * t, axis,
             radius * 0.985, radius * 1.02, width * 0.10, skin=skin, segments=segments)


def scatter_seed(bm, centre, radius, count, size, skin, rng, flatten=0.55):
    """A handful of small chamfered lumps around a point - spilled feed, straw, muck.

    Nothing here is a particle system; this is the cheap way to stop a prop's foot meeting
    the ground on a perfectly clean line, which is what makes a placed prop look dropped in
    rather than grown there.
    """
    centre = Vector(centre)
    for _ in range(count):
        a = rng.uniform(0.0, math.tau)
        d = radius * math.sqrt(rng.uniform(0.0, 1.0))
        s = size * rng.uniform(0.6, 1.4)
        p = centre + Vector((math.cos(a) * d, math.sin(a) * d, 0.0))
        tb.box(bm, (p.x - s, p.y - s * 0.8, p.z),
               (p.x + s, p.y + s * 0.8, p.z + s * flatten), skin)


def rotated(bm, degrees, axis=(0.0, 0.0, 1.0), pivot=(0.0, 0.0, 0.0)):
    """`with rotated(bm, 30): ...` - build it square, then turn it. Reads better than
    writing every endpoint pre-rotated, and stays adjustable afterwards."""
    return tb.moved(bm, tb.spin(pivot, axis, degrees))


def mirrored_x(bm):
    """Everything built inside the block is duplicated across X=0.

    Farm props are overwhelmingly symmetric and writing both halves by hand is how a
    left-hand fender ends up 5 mm off its partner - a difference nobody can see and
    everybody can feel.

    Build only the +X half inside the block. Anything crossing X=0 gets a mirrored copy
    laid over itself, and two coincident solids are interior faces the chamfer will cut
    into and the collider will keep.
    """
    return _MirrorX(bm)


class _MirrorX:
    def __init__(self, bm):
        self.bm = bm

    def __enter__(self):
        self.before = set(self.bm.verts)
        return self

    def __exit__(self, *exc):
        if exc[0] is not None:
            return False
        fresh = [v for v in self.bm.verts if v not in self.before]
        if not fresh:
            return False
        faces = {f for v in fresh for f in v.link_faces}
        edges = {e for v in fresh for e in v.link_edges}
        # Verts and edges go in alongside the faces. Handing duplicate() faces alone
        # works for closed solids and quietly drops anything the block left as a
        # standalone edge, which is a difference that only shows up on some props.
        copy = bmesh.ops.duplicate(self.bm, geom=fresh + list(edges) + list(faces))
        moved_verts = [g for g in copy["geom"] if isinstance(g, bmesh.types.BMVert)]
        bmesh.ops.transform(
            self.bm, matrix=Matrix.Diagonal(Vector((-1.0, 1.0, 1.0))).to_4x4(),
            verts=moved_verts)
        # Mirroring turns every duplicated face inside out. Left alone, half the prop is
        # lit from behind and, since the mesh is the collider, half of it is a one-way
        # surface.
        bmesh.ops.reverse_faces(
            self.bm, faces=[g for g in copy["geom"] if isinstance(g, bmesh.types.BMFace)])
        return False


# --------------------------------------------------------------------------------------
# The manifest
#
# What the Unity side reads instead of a hand-copied table. One file per model script
# rather than one for the pack, because each script runs in its own headless Blender and
# two of them writing the same file is a race the build runner would lose about a third of
# the time.
# --------------------------------------------------------------------------------------

MANIFEST_DIR = os.path.join(tb.EXPORT_DIR, "Manifests")

#: How the Unity side should give a prop collision. The mesh *is* the collider for most of
#: this pack, same as the rest of the project - but a chicken is not something a kart
#: should catch on, and a fence section belongs to BarrierLine's swept wall rather than
#: colliding on its own. See FarmAssetSetup.cs.
COLLIDER_MESH = "mesh"      # a MeshCollider on the imported mesh
COLLIDER_BOX = "box"        # a fitted BoxCollider: cheaper, and no wheel-catching corners
COLLIDER_CAPSULE = "capsule"  # animals: an upright capsule around the body
COLLIDER_NONE = "none"      # decoration, or something a swept wall already covers


class Manifest:
    """Collects what one model script built, and writes it where Unity can find it."""

    def __init__(self, script):
        self.script = script
        self.models = []
        self.materials = {}

    def add(self, stats, palette, collider=COLLIDER_MESH, tag="", note="", waterline=0.0):
        for name, rgb, metallic, roughness in palette:
            self.materials[name] = {
                "name": name,
                "rgb": [round(c, 4) for c in rgb],
                "metallic": round(metallic, 3),
                "roughness": round(roughness, 3),
                "emissive": name in EMISSIVE,
            }

        # A multi-part prop ships flat - see toebeans_blender.build_hierarchy for why the
        # FBX cannot carry a rig deeper than one level - so the intended parenting rides
        # here instead, and Unity rebuilds it when it makes the prefab.
        parts = [
            {"name": p["name"], "parent": p["parent"] or "", "pivot": p["pivot"]}
            for p in stats.get("parts", [])
        ]

        self.models.append({
            "name": stats["name"],
            "kind": "hierarchy" if parts else "prop",
            "tris": stats["tris"],
            "dims": [round(d, 4) for d in stats["dims_m"]],
            "materials": [p[0] for p in palette],
            "parts": parts,
            "collider": collider,
            "tag": tag,
            "note": note,
            # How far above the prop's origin the model floats, for anything that sits in
            # water. The duck is authored standing on its feet like every other prop, so
            # that "drop it on the terrain" still means something; this is the number
            # PondDuck sinks it by to put it on a pond instead. Measured off the model
            # rather than guessed in the inspector, because it is a property of the duck.
            "waterline": round(waterline, 4),
        })

    def write(self):
        os.makedirs(MANIFEST_DIR, exist_ok=True)
        path = os.path.join(MANIFEST_DIR, f"{self.script}.json")
        payload = {
            "script": self.script,
            "models": self.models,
            "materials": [self.materials[k] for k in sorted(self.materials)],
        }
        with open(path, "w", encoding="utf-8", newline="\n") as fh:
            json.dump(payload, fh, indent=2)
            fh.write("\n")
        print(f"MANIFEST {os.path.relpath(path, tb.REPO_ROOT)} "
              f"({len(self.models)} models, {len(self.materials)} materials)")
        return path
