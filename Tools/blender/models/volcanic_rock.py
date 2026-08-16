"""
Volcanic rock - a faceted boulder for scattering across LobbyIsland.

Sized and shaped for TreeScatter's prop mode: small enough to read as set dressing, low
enough that a kart clipping one is a nudge rather than a wall, and flat on the bottom so
it sits on the terrain instead of hovering with one corner in the dirt.

    blender --background --factory-startup --python Tools/blender/models/volcanic_rock.py

Everything is driven from SEED, so the same seed is the same rock every time. Change SEED
for a different boulder; change SIZE_M for a bigger one.
"""

import os
import random
import sys

import bmesh
import bpy
from mathutils import Vector

sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
import toebeans_blender as tb  # noqa: E402

NAME = "VolcanicRock_A"
SEED = 20260816
SIZE_M = 1.6          # rough diameter across the base
ROUGHNESS = 0.34      # 0 = smooth blob, 0.5 = shattered
SUBDIVISIONS = 2      # icosphere subdivisions; 2 is ~80 faces before the cuts
FLATTEN_AT = -0.18    # everything below this fraction of the radius becomes the base


def build_rock():
    tb.fresh_scene()
    rng = random.Random(SEED)

    bpy.ops.mesh.primitive_ico_sphere_add(subdivisions=SUBDIVISIONS, radius=SIZE_M / 2.0)
    obj = bpy.context.active_object
    obj.name = NAME

    me = obj.data
    bm = bmesh.new()
    bm.from_mesh(me)

    # Push each vertex along its own direction. Per-vertex rather than per-face keeps the
    # surface closed - a kart racer wants no cracks in something it might drive into.
    radius = SIZE_M / 2.0
    for v in bm.verts:
        jitter = 1.0 + rng.uniform(-ROUGHNESS, ROUGHNESS)
        v.co *= jitter
        # A little horizontal shear so it is not obviously a sphere.
        v.co.x += rng.uniform(-0.06, 0.06) * radius
        v.co.y += rng.uniform(-0.06, 0.06) * radius

    # Flatten the underside. Clamping Z rather than cutting keeps the mesh watertight,
    # which matters because this mesh is also the collider.
    floor = FLATTEN_AT * radius
    for v in bm.verts:
        if v.co.z < floor:
            v.co.z = floor

    # Squash slightly so it reads as a boulder resting, not a ball about to roll.
    for v in bm.verts:
        v.co.z *= 0.82

    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    bm.to_mesh(me)
    bm.free()
    me.update()

    return obj


if __name__ == "__main__":
    rock = build_rock()
    tb.build(
        rock,
        NAME,
        max_tris=400,      # prop-mode scatter puts a lot of these on screen at once
        max_size_m=4.0,
        require_uvs=True,
        faceted=True,
    )
