"""
Farm livestock - cow, pig, sheep, chicken and duck, built to be animated.

    blender --background --factory-startup --python Tools/blender/models/farm_animals.py

Every animal here is a rigid puppet: a body with its head, legs, tail and wings hung off
it as separate meshes, each pivoted on the joint it actually turns about. Nothing is
skinned and nothing carries a baked clip.

That is a decision worth defending, because "rig it and export animations" is the obvious
alternative. Three reasons it is not what this project wants:

**It is the rule the project already follows.** The kart README states it outright:
anything that moves is its own mesh. A skinned cow would be the only deforming thing in a
project where the terrain, the track, the volcano, the barn and the kart are all faceted
solids - and rigid limbs are what this art style looks like anyway. A low-poly animal that
bends smoothly reads as a different game's asset.

**Procedural beats clips for a racer.** `FarmAnimal.cs` swings the legs off the distance
the animal has walked, so the gait is right at any speed with no blend tree, and a herd of
forty costs no Animator components. It also sidesteps the multiplayer problem: a gait
derived from a shared clock and a position everyone already agrees on needs no animation
state synchronised at all.

**It stays open.** Every joint is a real GameObject with a sensible pivot, so anyone who
does want to key an animation by hand in Unity can - the parts are exactly what a
Legacy or Animator clip would need to address. Nothing here forecloses that.

The part-name contract `FarmAnimal.cs` reads:

    Body      the root; origin between the feet, on the ground
    Head      pivoted at the base of the neck
    Jaw       child of Head, pivoted at the hinge - this is what a quack opens
    Leg_FL    front left, pivoted at the shoulder; likewise FR, BL, BR
    Wing_L    pivoted at the shoulder; likewise R.  Birds only
    Tail      pivoted where it leaves the body
    Ear_L     child of Head; likewise R.  Mammals only

Left is -X and forward is +Y, which the export convention turns into Unity's -X and +Z.
An animal placed at rotation zero faces the way its transform says it does.

The duck is authored standing on its feet like everything else, so the origin convention
holds. Floating is a runtime concern: the manifest carries the model's `waterline`, and
`PondDuck` sinks it by exactly that to sit it on a pond.
"""

import math
import os
import random
import sys

import bmesh
from mathutils import Vector

sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
import toebeans_blender as tb  # noqa: E402
import farmyard as fy  # noqa: E402

SEED = 20260820

box = tb.box
beam = tb.beam
prism = tb.prism
taper = tb.taper

#: Animals get a finer chamfer than the buildings. The pack's 0.03 m is a third of a
#: chicken's leg, and a bevel that wide on a bird eats the part it is meant to soften.
CHAMFER = 0.012


# --------------------------------------------------------------------------------------
# Shared anatomy
# --------------------------------------------------------------------------------------

def leg(bm, hip, knee, foot, r_top, r_mid, r_bot, skin, hoof_skin=None, hoof=0.0,
        segments=6):
    """A two-segment tapered limb with an optional hoof or foot block on the end.

    Two segments rather than one because the bend is the read: a straight leg swinging
    from the hip is a pendulum, and a leg with a knee in it is a walk even when both are
    driven by exactly the same sine.
    """
    taper(bm, hip, knee, r_top, r_mid, skin, segments)
    taper(bm, knee, foot, r_mid, r_bot, skin, segments)
    if hoof > 0.0 and hoof_skin is not None:
        box(bm, (foot[0] - r_bot * 1.25, foot[1] - r_bot * 1.5, foot[2]),
            (foot[0] + r_bot * 1.25, foot[1] + r_bot * 1.9, foot[2] + hoof), hoof_skin)


def eyes(bm, y, x, z, r, skin=fy.SKIN_EYE, depth=None):
    """A pair of eyes, one each side. Twelve triangles that do most of the character work.

    Set proud of the head rather than sunk into it. A sunken eye on a faceted head
    disappears into the shading of the facet it sits on; a proud one always catches a
    highlight, which is what makes an animal look alive at twenty metres.
    """
    depth = depth if depth is not None else r * 0.9
    for sx in (-1, 1):
        box(bm, (sx * x - depth, y - r, z - r), (sx * x + depth, y + r, z + r), skin)


def patches(faces, centres, radius, skin):
    """Repaint the faces of a lofted body whose centres fall near any of `centres`.

    A Holstein's markings are the surface of the cow, not lumps stuck onto it, so this
    recolours faces rather than adding geometry - which costs no triangles at all.
    """
    for f in faces:
        c = f.calc_center_median()
        for at in centres:
            if (c - Vector(at)).length < radius:
                f.material_index = skin
                break


def wing(bm, shoulder, tip, chord, skin, plume_skin):
    """A folded wing: a tapered plate with two flight feathers laid over the trailing edge.

    Folded, not spread. A bird standing in a farmyard has its wings shut, and a spread
    wing would also double the animal's footprint - which matters when the collider is
    fitted to the bounding box.
    """
    a, b = Vector(shoulder), Vector(tip)
    beam(bm, a, b, 0.035, chord, skin, up=(0.0, 0.0, 1.0))
    for i, t in enumerate((0.52, 0.74)):
        at = a.lerp(b, t)
        end = a.lerp(b, t + 0.34)
        beam(bm, (at.x, at.y, at.z - chord * 0.28 - i * 0.012),
             (end.x, end.y, end.z - chord * 0.34 - i * 0.012),
             0.028, chord * 0.42, plume_skin, up=(0.0, 0.0, 1.0))


# --------------------------------------------------------------------------------------
# The cow
# --------------------------------------------------------------------------------------

def build_cow(name="Farm_Cow"):
    """A Holstein. The pack's largest animal and the one whose gait is most visible.

    Real proportions: 1.35 m at the withers, 2.2 m nose to tail. That is bigger than a
    kart is wide, which is deliberate - a cow standing in the road should read as
    something to go around rather than something to drive through.
    """

    body = bmesh.new()

    # A cow is deeper through the barrel than it is long in the leg - roughly 0.66 m of
    # body over 0.64 m of leg. Getting that the wrong way round is what makes a modelled
    # cow read as a large dog, and it is entirely a matter of these two numbers.
    back, belly = 1.30, 0.64
    cz = (back + belly) / 2.0
    rz = (back - belly) / 2.0

    hide = fy.loft(body, [
        (-0.66, 0.0, cz + 0.02, 0.17, rz * 0.72),      # rump
        (-0.44, 0.0, cz + 0.04, 0.28, rz * 0.98),      # hips
        (-0.10, 0.0, cz - 0.02, 0.30, rz * 1.06),      # barrel, deepest at the belly
        (0.30, 0.0, cz + 0.01, 0.29, rz * 1.00),       # chest
        (0.58, 0.0, cz + 0.06, 0.23, rz * 0.80),       # shoulder
        (0.70, 0.0, cz + 0.10, 0.16, rz * 0.58),       # neck root
    ], skin=fy.SKIN_WOOL, sides=8)
    # Black over white, in blotches rather than per face - see `patches`.
    patches(hide, [(0.0, -0.50, 1.20), (0.20, -0.16, 0.86), (-0.22, 0.16, 1.16),
                   (0.0, 0.52, 1.10), (0.26, -0.60, 0.94)], 0.17, fy.SKIN_HIDE_ALT)

    # Udder, slung under the belly between the hind legs.
    fy.loft(body, [(-0.34, 0.0, belly + 0.06, 0.12, 0.06),
                   (-0.16, 0.0, belly - 0.02, 0.15, 0.10),
                   (0.02, 0.0, belly + 0.04, 0.10, 0.05)],
            skin=fy.SKIN_FLESH, sides=6)
    for sx in (-1, 1):
        for ty in (-0.28, -0.12):
            taper(body, (sx * 0.07, ty, belly - 0.04), (sx * 0.08, ty, belly - 0.13),
                  0.022, 0.014, fy.SKIN_FLESH, 5)

    # ---- head, on its own pivot at the neck root
    neck = (0.0, 0.70, cz + 0.10)
    head = bmesh.new()
    fy.loft(head, [
        (0.00, 0.0, 0.00, 0.15, 0.15),
        (0.22, 0.0, 0.02, 0.16, 0.17),      # poll
        (0.40, 0.0, -0.04, 0.13, 0.13),     # brow
        (0.56, 0.0, -0.10, 0.10, 0.09),     # muzzle
    ], skin=fy.SKIN_HIDE_ALT, sides=8)
    # Muzzle, which on a Holstein is the pale part of an otherwise dark face.
    fy.loft(head, [(0.52, 0.0, -0.10, 0.105, 0.095), (0.62, 0.0, -0.115, 0.095, 0.08)],
            skin=fy.SKIN_FLESH, sides=8)
    for sx in (-1, 1):
        box(head, (sx * 0.045 - 0.018, 0.615, -0.13), (sx * 0.045 + 0.018, 0.64, -0.10),
            fy.SKIN_HIDE_ALT)
    eyes(head, 0.40, 0.115, 0.02, 0.028)
    # Horns, curving up and forward off the poll.
    for sx in (-1, 1):
        taper(head, (sx * 0.11, 0.22, 0.10), (sx * 0.19, 0.26, 0.19), 0.026, 0.016,
              fy.SKIN_HOOF, 5)
        taper(head, (sx * 0.19, 0.26, 0.19), (sx * 0.20, 0.34, 0.24), 0.016, 0.006,
              fy.SKIN_HOOF, 5)
    bmesh.ops.translate(head, verts=list(head.verts), vec=neck)

    jaw = bmesh.new()
    fy.loft(jaw, [(0.30, 0.0, -0.10, 0.10, 0.035), (0.58, 0.0, -0.14, 0.085, 0.030)],
            skin=fy.SKIN_HIDE_ALT, sides=6)
    bmesh.ops.translate(jaw, verts=list(jaw.verts), vec=neck)

    ear_parts = []
    for side, sx in (("L", -1), ("R", 1)):
        ear = bmesh.new()
        hinge = (sx * 0.13, 0.24, 0.03)
        prism(ear, [(hinge[0], hinge[1] - 0.05, hinge[2] - 0.03),
                    (hinge[0], hinge[1] + 0.05, hinge[2] - 0.03),
                    (hinge[0] + sx * 0.16, hinge[1] + 0.02, hinge[2] - 0.06)],
              (0.0, 0.0, 0.045), fy.SKIN_HIDE_ALT)
        world = (hinge[0] + neck[0], hinge[1] + neck[1], hinge[2] + neck[2])
        bmesh.ops.translate(ear, verts=list(ear.verts), vec=neck)
        ear_parts.append(tb.Part(f"Ear_{side}", ear, world, parent="Head"))

    # ---- tail
    tail = bmesh.new()
    root = (0.0, -0.68, back - 0.02)
    taper(tail, root, (0.0, -0.76, back - 0.46), 0.035, 0.018, fy.SKIN_HIDE_ALT, 5)
    fy.loft(tail, [(-0.80, 0.0, back - 0.46, 0.035, 0.035),
                   (-0.78, 0.0, back - 0.62, 0.055, 0.055),
                   (-0.76, 0.0, back - 0.72, 0.02, 0.02)],
            skin=fy.SKIN_HIDE_ALT, sides=6)

    # ---- legs. Front pair under the shoulder, back pair under the hip, knees forward at
    # the front and back at the rear, which is what a hind leg actually does.
    legs = []
    for tag, sx, hy, knee_dy in (("FL", -1, 0.40, 0.05), ("FR", 1, 0.40, 0.05),
                                 ("BL", -1, -0.42, -0.07), ("BR", 1, -0.42, -0.07)):
        bm = bmesh.new()
        hip = (sx * 0.21, hy, belly + 0.04)
        knee = (sx * 0.22, hy + knee_dy, 0.40)
        foot = (sx * 0.22, hy + knee_dy * 0.3, 0.09)
        leg(bm, hip, knee, foot, 0.10, 0.062, 0.05, fy.SKIN_HIDE_ALT,
            hoof_skin=fy.SKIN_HOOF, hoof=0.09)
        legs.append(tb.Part(f"Leg_{tag}", bm, hip, parent="Body"))

    parts = [
        tb.Part("Body", body, (0.0, 0.0, 0.0)),
        tb.Part("Head", head, neck, parent="Body"),
        tb.Part("Jaw", jaw, neck, parent="Head"),
        tb.Part("Tail", tail, root, parent="Body"),
    ] + ear_parts + legs
    return parts, fy.finish_parts(parts)


# --------------------------------------------------------------------------------------
# The pig
# --------------------------------------------------------------------------------------

def build_pig(name="Farm_Pig"):
    """A pig: barrel body, no neck to speak of, and a snout that arrives before it does.

    The head is a separate part despite the pig having no visible neck, because rooting
    about is the animal's entire behaviour and the head has to be able to go down.
    """
    body = bmesh.new()
    back, belly = 0.62, 0.24
    cz = (back + belly) / 2.0
    rz = (back - belly) / 2.0

    hide = fy.loft(body, [
        (-0.44, 0.0, cz - 0.02, 0.13, rz * 0.66),
        (-0.28, 0.0, cz + 0.01, 0.21, rz * 0.94),
        (0.00, 0.0, cz - 0.01, 0.23, rz * 1.04),
        (0.26, 0.0, cz + 0.01, 0.21, rz * 0.98),
        (0.42, 0.0, cz + 0.02, 0.15, rz * 0.78),
    ], skin=fy.SKIN_FLESH, sides=8)
    # A saddleback's black band, and a muddy underside from lying in it.
    patches(hide, [(0.0, -0.16, 0.62), (0.0, -0.16, 0.30), (0.24, -0.16, 0.44),
                   (-0.24, -0.16, 0.44)], 0.22, fy.SKIN_HIDE_ALT)
    patches(hide, [(0.0, -0.10, 0.22), (0.0, 0.18, 0.24)], 0.16, fy.SKIN_DIRT)

    neck = (0.0, 0.42, cz + 0.02)
    head = bmesh.new()
    fy.loft(head, [(0.00, 0.0, 0.00, 0.14, 0.13), (0.14, 0.0, -0.01, 0.12, 0.11),
                   (0.24, 0.0, -0.03, 0.08, 0.07)],
            skin=fy.SKIN_FLESH, sides=8)
    # The snout disc, and the two nostrils that make it a snout rather than a stump.
    fy.loft(head, [(0.24, 0.0, -0.03, 0.075, 0.065), (0.29, 0.0, -0.035, 0.08, 0.07)],
            skin=fy.SKIN_FLESH, sides=8)
    for sx in (-1, 1):
        box(head, (sx * 0.028 - 0.012, 0.288, -0.05), (sx * 0.028 + 0.012, 0.30, -0.02),
            fy.SKIN_HIDE_ALT)
    eyes(head, 0.13, 0.105, 0.05, 0.022)
    bmesh.ops.translate(head, verts=list(head.verts), vec=neck)

    jaw = bmesh.new()
    fy.loft(jaw, [(0.13, 0.0, -0.06, 0.085, 0.028), (0.27, 0.0, -0.06, 0.06, 0.022)],
            skin=fy.SKIN_FLESH, sides=6)
    bmesh.ops.translate(jaw, verts=list(jaw.verts), vec=neck)

    ear_parts = []
    for side, sx in (("L", -1), ("R", 1)):
        ear = bmesh.new()
        hinge = (sx * 0.09, 0.05, 0.10)
        # Forward-flopping, the way a saddleback's are - it also keeps the ear out of the
        # silhouette's outline, where a triangle sticking sideways reads as a horn.
        prism(ear, [(hinge[0] - 0.05, hinge[1] - 0.03, hinge[2]),
                    (hinge[0] + 0.05, hinge[1] - 0.03, hinge[2]),
                    (hinge[0] + sx * 0.01, hinge[1] + 0.10, hinge[2] - 0.10)],
              (0.0, 0.0, 0.022), fy.SKIN_FLESH)
        world = (hinge[0] + neck[0], hinge[1] + neck[1], hinge[2] + neck[2])
        bmesh.ops.translate(ear, verts=list(ear.verts), vec=neck)
        ear_parts.append(tb.Part(f"Ear_{side}", ear, world, parent="Head"))

    # The curly tail, as three short segments turning about each other. Worth the dozen
    # triangles: it is the only part of a pig nobody forgets.
    tail = bmesh.new()
    root = (0.0, -0.46, back - 0.08)
    prev = root
    for i, (dy, dz, dx) in enumerate(((-0.05, 0.06, 0.0), (-0.02, 0.06, 0.05),
                                      (0.03, 0.02, 0.02), (0.02, -0.05, -0.03))):
        nxt = (prev[0] + dx, prev[1] + dy, prev[2] + dz)
        taper(tail, prev, nxt, 0.022 - i * 0.003, 0.019 - i * 0.003, fy.SKIN_FLESH, 5)
        prev = nxt

    legs = []
    for tag, sx, hy in (("FL", -1, 0.26), ("FR", 1, 0.26),
                        ("BL", -1, -0.28), ("BR", 1, -0.28)):
        bm = bmesh.new()
        hip = (sx * 0.15, hy, belly + 0.03)
        knee = (sx * 0.155, hy, 0.14)
        foot = (sx * 0.155, hy, 0.05)
        leg(bm, hip, knee, foot, 0.062, 0.038, 0.032, fy.SKIN_FLESH,
            hoof_skin=fy.SKIN_HOOF, hoof=0.05)
        legs.append(tb.Part(f"Leg_{tag}", bm, hip, parent="Body"))

    parts = [
        tb.Part("Body", body, (0.0, 0.0, 0.0)),
        tb.Part("Head", head, neck, parent="Body"),
        tb.Part("Jaw", jaw, neck, parent="Head"),
        tb.Part("Tail", tail, root, parent="Body"),
    ] + ear_parts + legs
    return parts, fy.finish_parts(parts)


# --------------------------------------------------------------------------------------
# The sheep
# --------------------------------------------------------------------------------------

def build_sheep(name="Farm_Sheep"):
    """A sheep: a fleece with a dark face and four dark legs poking out of it.

    The fleece is the whole animal, so it gets the one thing nothing else in the pack has
    - a deliberately lumpy surface. Ten small blocks laid over the body loft break the
    silhouette everywhere, which is what separates wool from a painted white barrel.
    """
    rng = random.Random(SEED + 32)
    body = bmesh.new()
    back, belly = 0.78, 0.42
    cz = (back + belly) / 2.0
    rz = (back - belly) / 2.0

    fy.loft(body, [
        (-0.38, 0.0, cz, 0.15, rz * 0.78),
        (-0.22, 0.0, cz + 0.01, 0.23, rz * 1.02),
        (0.04, 0.0, cz, 0.24, rz * 1.08),
        (0.28, 0.0, cz + 0.02, 0.21, rz * 0.94),
        (0.40, 0.0, cz + 0.04, 0.14, rz * 0.66),
    ], skin=fy.SKIN_WOOL, sides=8)

    for _ in range(14):
        a = rng.uniform(0.0, math.tau)
        y = rng.uniform(-0.34, 0.34)
        s = rng.uniform(0.05, 0.085)
        r = 0.22
        at = (math.cos(a) * r * 0.92, y, cz + math.sin(a) * rz * 1.02)
        box(body, (at[0] - s, at[1] - s, at[2] - s), (at[0] + s, at[1] + s, at[2] + s),
            fy.SKIN_WOOL)

    neck = (0.0, 0.40, cz + 0.04)
    head = bmesh.new()
    fy.loft(head, [(0.00, 0.0, 0.00, 0.075, 0.075), (0.10, 0.0, 0.02, 0.085, 0.085),
                   (0.22, 0.0, -0.02, 0.065, 0.060), (0.30, 0.0, -0.05, 0.045, 0.042)],
            skin=fy.SKIN_HIDE_ALT, sides=8)
    eyes(head, 0.17, 0.072, 0.02, 0.02)
    bmesh.ops.translate(head, verts=list(head.verts), vec=neck)

    jaw = bmesh.new()
    fy.loft(jaw, [(0.16, 0.0, -0.05, 0.055, 0.02), (0.30, 0.0, -0.075, 0.04, 0.018)],
            skin=fy.SKIN_HIDE_ALT, sides=6)
    bmesh.ops.translate(jaw, verts=list(jaw.verts), vec=neck)

    ear_parts = []
    for side, sx in (("L", -1), ("R", 1)):
        ear = bmesh.new()
        hinge = (sx * 0.07, 0.10, 0.05)
        prism(ear, [(hinge[0], hinge[1] - 0.03, hinge[2]),
                    (hinge[0], hinge[1] + 0.03, hinge[2]),
                    (hinge[0] + sx * 0.10, hinge[1] - 0.01, hinge[2] - 0.03)],
              (0.0, 0.0, 0.022), fy.SKIN_HIDE_ALT)
        world = (hinge[0] + neck[0], hinge[1] + neck[1], hinge[2] + neck[2])
        bmesh.ops.translate(ear, verts=list(ear.verts), vec=neck)
        ear_parts.append(tb.Part(f"Ear_{side}", ear, world, parent="Head"))

    tail = bmesh.new()
    root = (0.0, -0.40, back - 0.10)
    fy.loft(tail, [(-0.40, 0.0, back - 0.10, 0.05, 0.05),
                   (-0.42, 0.0, back - 0.22, 0.055, 0.055),
                   (-0.42, 0.0, back - 0.30, 0.02, 0.02)],
            skin=fy.SKIN_WOOL, sides=6)

    legs = []
    for tag, sx, hy in (("FL", -1, 0.24), ("FR", 1, 0.24),
                        ("BL", -1, -0.24), ("BR", 1, -0.24)):
        bm = bmesh.new()
        hip = (sx * 0.15, hy, belly + 0.02)
        knee = (sx * 0.15, hy + (0.03 if hy > 0 else -0.04), 0.22)
        foot = (sx * 0.15, hy, 0.05)
        leg(bm, hip, knee, foot, 0.045, 0.032, 0.026, fy.SKIN_HIDE_ALT,
            hoof_skin=fy.SKIN_HOOF, hoof=0.05)
        legs.append(tb.Part(f"Leg_{tag}", bm, hip, parent="Body"))

    parts = [
        tb.Part("Body", body, (0.0, 0.0, 0.0)),
        tb.Part("Head", head, neck, parent="Body"),
        tb.Part("Jaw", jaw, neck, parent="Head"),
        tb.Part("Tail", tail, root, parent="Body"),
    ] + ear_parts + legs
    return parts, fy.finish_parts(parts)


# --------------------------------------------------------------------------------------
# Birds
# --------------------------------------------------------------------------------------

def build_chicken(name="Farm_Chicken"):
    """A hen: upright body, comb, wattle, and a tail carried high.

    The smallest thing in the pack by a wide margin, which sets the chamfer: 0.012 m,
    because the pack's usual 0.03 would remove the wattle entirely.
    """
    body = bmesh.new()
    fy.loft(body, [
        (-0.14, 0.0, 0.30, 0.05, 0.05),        # tail root, high
        (-0.06, 0.0, 0.26, 0.10, 0.11),
        (0.06, 0.0, 0.24, 0.11, 0.12),         # breast, the deepest part
        (0.15, 0.0, 0.27, 0.075, 0.08),
    ], skin=fy.SKIN_PLUME, sides=8)

    neck = (0.0, 0.14, 0.31)
    head = bmesh.new()
    fy.loft(head, [(0.00, 0.0, 0.00, 0.035, 0.035), (0.02, 0.0, 0.07, 0.036, 0.036),
                   (0.04, 0.0, 0.12, 0.048, 0.048), (0.06, 0.0, 0.16, 0.030, 0.030)],
            skin=fy.SKIN_PLUME, sides=7)
    # Comb: five blades along the crown. Wattle under the chin. Between them they are
    # what makes a small brown lump read as a chicken.
    for i in range(5):
        t = i / 4.0
        h = 0.020 + math.sin(t * math.pi) * 0.016
        box(head, (-0.008, 0.018 + t * 0.048, 0.150), (0.008, 0.040 + t * 0.048,
            0.150 + h), fy.SKIN_COMB)
    box(head, (-0.012, 0.052, 0.082), (0.012, 0.076, 0.116), fy.SKIN_COMB)
    # Beak: two short wedges meeting at a point.
    prism(head, [(-0.016, 0.062, 0.126), (0.016, 0.062, 0.126), (0.0, 0.104, 0.120)],
          (0.0, 0.0, 0.018), fy.SKIN_BEAK)
    eyes(head, 0.052, 0.038, 0.132, 0.011)
    bmesh.ops.translate(head, verts=list(head.verts), vec=neck)

    jaw = bmesh.new()
    prism(jaw, [(-0.014, 0.062, 0.124), (0.014, 0.062, 0.124), (0.0, 0.100, 0.116)],
          (0.0, 0.0, 0.011), fy.SKIN_BEAK)
    bmesh.ops.translate(jaw, verts=list(jaw.verts), vec=neck)

    tail = bmesh.new()
    root = (0.0, -0.14, 0.32)
    for i, (dx, tilt) in enumerate(((-0.028, 46.0), (0.0, 58.0), (0.028, 46.0))):
        with fy.rotated(tail, tilt, (1.0, 0.0, 0.0), root):
            beam(tail, (dx, root[1], root[2]), (dx, root[1] - 0.16, root[2]),
                 0.022, 0.075 - abs(i - 1) * 0.012, fy.SKIN_HIDE_ALT, up=(0.0, 0.0, 1.0))

    wings = []
    for side, sx in (("L", -1), ("R", 1)):
        bm = bmesh.new()
        shoulder = (sx * 0.095, 0.06, 0.29)
        wing(bm, shoulder, (sx * 0.085, -0.07, 0.26), 0.085, fy.SKIN_PLUME,
             fy.SKIN_HIDE_ALT)
        wings.append(tb.Part(f"Wing_{side}", bm, shoulder, parent="Body"))

    legs = []
    for tag, sx in (("FL", -1), ("FR", 1)):
        bm = bmesh.new()
        hip = (sx * 0.05, 0.01, 0.19)
        knee = (sx * 0.055, -0.01, 0.10)
        foot = (sx * 0.055, 0.01, 0.018)
        taper(bm, hip, knee, 0.020, 0.013, fy.SKIN_BEAK, 5)
        taper(bm, knee, foot, 0.013, 0.011, fy.SKIN_BEAK, 5)
        # Three toes forward, one back. Cheap, and a bird without feet reads as a toy.
        for a in (-32.0, 0.0, 32.0):
            r = math.radians(a)
            beam(bm, foot, (foot[0] + math.sin(r) * 0.05, foot[1] + math.cos(r) * 0.055,
                 0.010), 0.014, 0.014, fy.SKIN_BEAK, up=(0.0, 0.0, 1.0))
        beam(bm, foot, (foot[0], foot[1] - 0.030, 0.010), 0.012, 0.012, fy.SKIN_BEAK,
             up=(0.0, 0.0, 1.0))
        legs.append(tb.Part(f"Leg_{tag}", bm, hip, parent="Body"))

    parts = [
        tb.Part("Body", body, (0.0, 0.0, 0.0)),
        tb.Part("Head", head, neck, parent="Body"),
        tb.Part("Jaw", jaw, neck, parent="Head"),
        tb.Part("Tail", tail, root, parent="Body"),
    ] + wings + legs
    return parts, fy.finish_parts(parts)


#: Where the water surface crosses a floating duck, measured above the prop's origin.
#: Roughly the bottom third of the body: a duck floats low, and one sitting on the surface
#: like a bath toy is the single most obvious way to get this wrong.
DUCK_WATERLINE = 0.175


def build_duck(name="Farm_Duck"):
    """A mallard drake, authored standing.

    Two things it has that the chicken does not. The bill is split into a fixed upper and
    a hinged `Jaw`, because the whole point of these is that they quack as you drive past
    and a quack with a shut bill is a sound effect playing near a duck. And the body is
    long and shallow rather than upright, so that when `PondDuck` sinks it to
    DUCK_WATERLINE it sits on the water the way a duck does rather than perching on it.
    """
    body = bmesh.new()
    keel = 0.135
    hide = fy.loft(body, [
        (-0.20, 0.0, keel + 0.11, 0.035, 0.035),     # tail root
        (-0.13, 0.0, keel + 0.08, 0.085, 0.075),
        (-0.02, 0.0, keel + 0.06, 0.105, 0.095),     # the float line, widest here
        (0.10, 0.0, keel + 0.07, 0.095, 0.085),
        (0.19, 0.0, keel + 0.10, 0.055, 0.050),      # breast into the neck
    ], skin=fy.SKIN_HIDE, sides=8)
    # A drake's pale flanks and dark back, painted onto the loft rather than added.
    patches(hide, [(0.0, -0.02, keel - 0.02), (0.0, 0.10, keel - 0.01)], 0.11,
            fy.SKIN_WOOL)
    patches(hide, [(0.0, -0.08, keel + 0.15), (0.0, 0.06, keel + 0.15)], 0.10,
            fy.SKIN_HIDE_ALT)
    # The white neck ring sits on the body side of the joint so it cannot slide when the
    # head turns.
    fy.loft(body, [(0.185, 0.0, keel + 0.10, 0.052, 0.050),
                   (0.205, 0.0, keel + 0.11, 0.050, 0.048)],
            skin=fy.SKIN_WOOL, sides=8)

    neck = (0.0, 0.20, keel + 0.11)
    head = bmesh.new()
    fy.loft(head, [(0.00, 0.0, 0.00, 0.038, 0.038), (0.01, 0.0, 0.06, 0.036, 0.036),
                   (0.03, 0.0, 0.11, 0.050, 0.048), (0.05, 0.0, 0.15, 0.032, 0.030)],
            skin=fy.SKIN_DRAKE, sides=7)
    # Upper bill: a flat wedge with a nail on the end.
    prism(head, [(-0.026, 0.052, 0.108), (0.026, 0.052, 0.108), (0.020, 0.116, 0.100),
                 (-0.020, 0.116, 0.100)], (0.0, 0.0, 0.016), fy.SKIN_BEAK)
    eyes(head, 0.046, 0.040, 0.122, 0.011)
    bmesh.ops.translate(head, verts=list(head.verts), vec=neck)

    # Lower bill on its own pivot at the hinge - this is the part a quack opens.
    hinge = (0.0, 0.052, 0.104)
    jaw = bmesh.new()
    prism(jaw, [(-0.023, 0.054, 0.104), (0.023, 0.054, 0.104), (0.018, 0.112, 0.097),
                (-0.018, 0.112, 0.097)], (0.0, 0.0, 0.009), fy.SKIN_BEAK)
    bmesh.ops.translate(jaw, verts=list(jaw.verts), vec=neck)
    jaw_pivot = (neck[0] + hinge[0], neck[1] + hinge[1], neck[2] + hinge[2])

    tail = bmesh.new()
    root = (0.0, -0.20, keel + 0.11)
    prism(tail, [(-0.045, -0.20, keel + 0.10), (0.045, -0.20, keel + 0.10),
                 (0.020, -0.30, keel + 0.15), (-0.020, -0.30, keel + 0.15)],
          (0.0, 0.0, 0.020), fy.SKIN_HIDE_ALT)

    wings = []
    for side, sx in (("L", -1), ("R", 1)):
        bm = bmesh.new()
        shoulder = (sx * 0.092, 0.06, keel + 0.09)
        wing(bm, shoulder, (sx * 0.080, -0.10, keel + 0.07), 0.080, fy.SKIN_HIDE,
             fy.SKIN_DRAKE)
        wings.append(tb.Part(f"Wing_{side}", bm, shoulder, parent="Body"))

    legs = []
    for tag, sx in (("FL", -1), ("FR", 1)):
        bm = bmesh.new()
        hip = (sx * 0.045, -0.02, keel - 0.01)
        foot = (sx * 0.048, -0.01, 0.014)
        taper(bm, hip, foot, 0.017, 0.013, fy.SKIN_BEAK, 5)
        # Webbed foot: one plate rather than toes, which is the difference that reads.
        prism(bm, [(sx * 0.048 - 0.030, -0.02, 0.0), (sx * 0.048 + 0.030, -0.02, 0.0),
                   (sx * 0.048 + 0.036, 0.055, 0.0), (sx * 0.048 - 0.036, 0.055, 0.0)],
              (0.0, 0.0, 0.014), fy.SKIN_BEAK)
        legs.append(tb.Part(f"Leg_{tag}", bm, hip, parent="Body"))

    parts = [
        tb.Part("Body", body, (0.0, 0.0, 0.0)),
        tb.Part("Head", head, neck, parent="Body"),
        tb.Part("Jaw", jaw, jaw_pivot, parent="Head"),
        tb.Part("Tail", tail, root, parent="Body"),
    ] + wings + legs
    return parts, fy.finish_parts(parts)


if __name__ == "__main__":
    manifest = fy.Manifest("farm_animals")

    # An animal gets a capsule rather than its own mesh. Two reasons, and they point the
    # same way: a kart catching on a chicken's leg is a bug report, and a herd is the
    # highest instance count in the pack after the fencing.
    jobs = (
        (build_cow, "Farm_Cow", 2950, 0.0),
        (build_pig, "Farm_Pig", 2100, 0.0),
        (build_sheep, "Farm_Sheep", 2600, 0.0),
        (build_chicken, "Farm_Chicken", 2050, 0.0),
        (build_duck, "Farm_Duck", 1450, DUCK_WATERLINE),
    )

    for builder, model, budget, waterline in jobs:
        tb.fresh_scene()
        parts, palette = builder()
        stats = tb.build_hierarchy(parts, model, palette, chamfer_m=CHAMFER,
                                   max_tris=budget, max_size_m=3.0,
                                   plan_tolerance=0.40, drop_to_ground=True)
        manifest.add(stats, palette, collider=fy.COLLIDER_CAPSULE, tag="animal",
                     waterline=waterline,
                     note="rigid puppet; see FarmAnimal.cs for the part-name contract")

    manifest.write()
