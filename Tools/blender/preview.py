"""
Renders a built prop to a PNG so you can look at it without opening Blender.

    blender --background --factory-startup --python Tools/blender/preview.py -- VolcanicRock_A

Writes Tools/blender/previews/<name>.png. Three-quarter view with a ground plane and a
metre grid of reference cubes along the base, because "is this the right size" is the
question a render usually fails to answer.
"""

import math
import os
import sys

import bpy

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import toebeans_blender as tb  # noqa: E402

argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
name = argv[0] if argv else "VolcanicRock_A"

fbx = os.path.join(tb.EXPORT_DIR, f"{name}.fbx")
if not os.path.exists(fbx):
    print(f"PREVIEW_FAILED no such model: {fbx}")
    sys.exit(1)

def flat_material(name, rgb):
    """A plain coloured material. Untextured grey renders hide exactly the shape
    problems a preview is supposed to reveal."""
    mat = bpy.data.materials.new(name)
    mat.use_nodes = True
    bsdf = mat.node_tree.nodes["Principled BSDF"]
    bsdf.inputs["Base Color"].default_value = (*rgb, 1.0)
    bsdf.inputs["Roughness"].default_value = 0.85
    return mat


tb.fresh_scene()
bpy.ops.import_scene.fbx(filepath=fbx)
obj = next(o for o in bpy.data.objects if o.type == "MESH")

# The FBX carries Y-up geometry by design, so it arrives rotated. Stand it back up for
# the render only - this scene is thrown away.
bpy.ops.object.select_all(action="DESELECT")
obj.select_set(True)
bpy.context.view_layer.objects.active = obj
bpy.ops.object.transform_apply(location=False, rotation=True, scale=True)
bpy.context.view_layer.update()

size = max(obj.dimensions)

obj.data.materials.clear()
obj.data.materials.append(flat_material("PreviewProp", (0.44, 0.20, 0.16)))

# Ground plane, so the prop is resting on something rather than floating in a void.
bpy.ops.mesh.primitive_plane_add(size=size * 8, location=(0, 0, 0))
ground = bpy.context.active_object
ground.name = "PreviewGround"
ground.data.materials.append(flat_material("PreviewGround", (0.16, 0.16, 0.18)))

# One-metre reference cubes in a row beside it. Scale is the thing renders lie about,
# and a cube you can recognise as one metre answers it faster than a printed dimension.
ref_mat = flat_material("PreviewScaleRef", (0.85, 0.72, 0.20))
for i in range(3):
    # Laid out along the camera's right, so they sit at the same depth as the prop.
    # Put them nearer the lens and perspective inflates them into a lie.
    t = size * 0.75 + 0.8 + i * 1.15
    bpy.ops.mesh.primitive_cube_add(size=1.0, location=(t * 0.707, t * 0.707, 0.5))
    ref = bpy.context.active_object
    ref.name = f"ScaleRef_1m_{i}"
    ref.data.materials.append(ref_mat)

bpy.ops.object.light_add(type="SUN", location=(4, -6, 8))
key = bpy.context.active_object
key.data.energy = 4.0
key.rotation_euler = (math.radians(50), 0, math.radians(35))

bpy.ops.object.light_add(type="AREA", location=(-5, 3, 3))
fill = bpy.context.active_object
fill.data.energy = 120.0
fill.data.size = 6.0

# Low-ish three-quarter view. Looking down too steeply flattens a prop's silhouette,
# which is the one thing you are trying to judge.
d = size * 3.4
bpy.ops.object.camera_add(location=(d, -d, d * 0.5))
cam = bpy.context.active_object
bpy.context.scene.camera = cam

# Orthographic on purpose. Under perspective, two objects the same size render at
# different sizes depending on depth, which defeats the point of a scale reference.
cam.data.type = "ORTHO"
cam.data.ortho_scale = size * 5.6
track = cam.constraints.new(type="TRACK_TO")
track.target = obj
track.track_axis = "TRACK_NEGATIVE_Z"
track.up_axis = "UP_Y"

scene = bpy.context.scene
scene.render.engine = "BLENDER_EEVEE"
scene.render.resolution_x = 900
scene.render.resolution_y = 640
scene.render.film_transparent = False

out_dir = os.path.join(os.path.dirname(os.path.abspath(__file__)), "previews")
os.makedirs(out_dir, exist_ok=True)
scene.render.filepath = os.path.join(out_dir, f"{name}.png")
bpy.ops.render.render(write_still=True)

print(f"PREVIEW_OK {scene.render.filepath}")
print(f"PREVIEW_DIMS {[round(v, 3) for v in obj.dimensions]}")
