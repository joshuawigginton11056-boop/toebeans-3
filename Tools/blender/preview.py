"""
Renders a built prop to a PNG so you can look at it without opening Blender.

    blender --background --factory-startup --python Tools/blender/preview.py -- VolcanicRock_A
    blender --background --factory-startup --python Tools/blender/preview.py -- Farm_Barn back

Writes Tools/blender/previews/<name>.png. Three-quarter view on a one-metre floor grid,
because "is this the right size" is the question a render usually fails to answer and a
printed dimension answers too slowly to be useful while you are looking at a shape.

A second argument turns the camera: `front`, `back`, `left`, `right`, or a number of
degrees. A prop with a door on one side and a chute on the other cannot be judged from a
single fixed corner, and re-rendering from another angle is cheaper than guessing.

Two things it deliberately does *not* do:

  It does not re-run the builder.  It reads the exported FBX, so an export that quietly
                    lost its origin, its material split or a child object shows up in the
                    picture rather than surviving to Unity.

  It does not flatten a prop that has a palette.  A single grey is right for a rock,
                    where the only question is the silhouette. It is wrong for anything
                    from the farm pack, where the material split *is* the read and a flat
                    render would hide a part tagged with the wrong slot.
"""

import math
import os
import sys

import bpy
from mathutils import Vector

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import toebeans_blender as tb  # noqa: E402

argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
name = argv[0] if argv else "VolcanicRock_A"
view = argv[1] if len(argv) > 1 else "front"

TURNS = {"front": 0.0, "right": 90.0, "back": 180.0, "left": 270.0}
if view in TURNS:
    turn = TURNS[view]
else:
    try:
        turn = float(view)
    except ValueError:
        print(f"PREVIEW_FAILED unknown view {view!r}; use {sorted(TURNS)} or degrees")
        sys.exit(1)

fbx = os.path.join(tb.EXPORT_DIR, f"{name}.fbx")
if not os.path.exists(fbx):
    print(f"PREVIEW_FAILED no such model: {fbx}")
    sys.exit(1)


def flat_material(mat_name, rgb):
    """A plain coloured material. Untextured grey renders hide exactly the shape
    problems a preview is supposed to reveal."""
    mat = bpy.data.materials.new(mat_name)
    mat.use_nodes = True
    bsdf = mat.node_tree.nodes["Principled BSDF"]
    bsdf.inputs["Base Color"].default_value = (*rgb, 1.0)
    bsdf.inputs["Roughness"].default_value = 0.85
    return mat


tb.fresh_scene()
bpy.ops.import_scene.fbx(filepath=fbx)
meshes = [o for o in bpy.data.objects if o.type == "MESH"]
if not meshes:
    print(f"PREVIEW_FAILED no mesh in {fbx}")
    sys.exit(1)

bpy.context.view_layer.update()

# World bounds across every part. A multi-part prop's root object knows only its own
# extent, and framing a windpump on its tower alone crops the rotor out of the shot.
corners = [o.matrix_world @ Vector(c) for o in meshes for c in o.bound_box]
lo = Vector((min(c.x for c in corners), min(c.y for c in corners), min(c.z for c in corners)))
hi = Vector((max(c.x for c in corners), max(c.y for c in corners), max(c.z for c in corners)))
extent = hi - lo
size = max(extent)
centre = (lo + hi) * 0.5

# Only repaint props that arrived with nothing worth showing. Anything carrying a real
# palette keeps it - see the module docstring.
slots = sum(len(o.data.materials) for o in meshes)
if slots <= 1:
    grey = flat_material("PreviewProp", (0.44, 0.20, 0.16))
    for o in meshes:
        o.data.materials.clear()
        o.data.materials.append(grey)

# Ground plane, so the prop is resting on something rather than floating in a void.
bpy.ops.mesh.primitive_plane_add(size=size * 8, location=(centre.x, centre.y, lo.z))
ground = bpy.context.active_object
ground.name = "PreviewGround"
ground.data.materials.append(flat_material("PreviewGround", (0.16, 0.16, 0.18)))

# Scale reference. A one-metre grid on the floor plus a single one-metre cube off to the
# side, rather than the row of three cubes this used to draw.
#
# The row was fine for a boulder and useless for a duck: a 0.4 m prop framed tightly
# enough to see is framed tightly enough that a one-metre cube fills half the picture and
# stands in front of the thing being judged. A floor grid cannot occlude anything, reads
# at any prop size, and answers "how long is that" better than a cube ever did. The cube
# stays, once, because height is the one question a flat grid cannot answer.
ref_mat = flat_material("PreviewScaleRef", (0.85, 0.72, 0.20))
grid_mat = flat_material("PreviewGrid", (0.22, 0.22, 0.25))
theta = math.radians(turn)
right = Vector((math.cos(theta), math.sin(theta), 0.0))

# Tiles land on whole-metre world coordinates rather than on the prop, so the grid is an
# absolute ruler and not a pattern that shifts with whatever is standing on it.
reach = int(math.ceil(size * 0.9)) + 2
for i in range(-reach, reach + 1):
    for j in range(-reach, reach + 1):
        if (i + j) % 2:
            continue
        bpy.ops.mesh.primitive_plane_add(
            size=1.0, location=(math.floor(centre.x) + i + 0.5,
                                math.floor(centre.y) + j + 0.5, lo.z + 0.004))
        bpy.context.active_object.data.materials.append(grid_mat)

cube_at = centre + right * (size * 0.62 + 1.0)
bpy.ops.mesh.primitive_cube_add(size=1.0, location=(cube_at.x, cube_at.y, lo.z + 0.5))
bpy.context.active_object.name = "ScaleRef_1m"
bpy.context.active_object.data.materials.append(ref_mat)

bpy.ops.object.light_add(type="SUN", location=(4, -6, 8))
key = bpy.context.active_object
key.data.energy = 4.0
key.rotation_euler = (math.radians(50), 0, math.radians(35 + turn))

bpy.ops.object.light_add(type="AREA", location=(-5, 3, 3))
fill = bpy.context.active_object
fill.data.energy = 120.0
fill.data.size = 6.0

# Low-ish three-quarter view. Looking down too steeply flattens a prop's silhouette,
# which is the one thing you are trying to judge.
d = size * 3.4
eye = centre + Vector((
    d * (math.cos(theta) - math.sin(theta)) * 0.5,
    d * (math.sin(theta) + math.cos(theta)) * -0.5,
    d * 0.42,
))
bpy.ops.object.camera_add(location=eye)
cam = bpy.context.active_object
bpy.context.scene.camera = cam

target = bpy.data.objects.new("PreviewTarget", None)
bpy.context.collection.objects.link(target)
target.location = centre

# Orthographic on purpose. Under perspective, two objects the same size render at
# different sizes depending on depth, which defeats the point of a scale reference.
cam.data.type = "ORTHO"
cam.data.ortho_scale = size * 1.55 + 2.0
track = cam.constraints.new(type="TRACK_TO")
track.target = target
track.track_axis = "TRACK_NEGATIVE_Z"
track.up_axis = "UP_Y"

scene = bpy.context.scene
scene.render.engine = "BLENDER_EEVEE"
scene.render.resolution_x = 900
scene.render.resolution_y = 640
scene.render.film_transparent = False

out_dir = os.path.join(os.path.dirname(os.path.abspath(__file__)), "previews")
os.makedirs(out_dir, exist_ok=True)
suffix = "" if view == "front" else f"_{view}"
scene.render.filepath = os.path.join(out_dir, f"{name}{suffix}.png")
bpy.ops.render.render(write_still=True)

print(f"PREVIEW_OK {scene.render.filepath}")
print(f"PREVIEW_DIMS {[round(v, 3) for v in extent]} parts={len(meshes)} slots={slots}")
