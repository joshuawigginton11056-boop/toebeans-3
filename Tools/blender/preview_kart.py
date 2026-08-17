"""
Renders a built kart style - body plus its four wheels - to a PNG.

    blender --background --factory-startup --python Tools/blender/preview_kart.py -- kart_buggy

Writes Tools/blender/previews/<style>.png.

preview.py renders one FBX, which is the right shape for a prop and the wrong shape for a
kart: a kart ships as a body and two wheel meshes, and the thing worth looking at is the
assembly. Previewing the exported FBX rather than re-running the builders is deliberate -
it is the file Unity will read, so an export that quietly loses its material split or its
origin shows up here rather than in the game.

Wheel placement comes from the model script's own constants, so this does not become a
third place the kart's dimensions are written down.
"""

import importlib
import math
import os
import sys

import bpy

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
sys.path.insert(0, os.path.join(os.path.dirname(os.path.abspath(__file__)), "models"))
import toebeans_blender as tb  # noqa: E402

argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
style = argv[0] if argv else "kart_buggy"

model = importlib.import_module(style)

tb.fresh_scene()


def bring_in(name):
    """Import one exported FBX and stand it back up.

    The FBX carries Y-up geometry by design, so it arrives rotated 90 on X. Applying that
    puts it back in Blender's frame for the render only; the file on disk is untouched.
    """
    path = os.path.join(tb.EXPORT_DIR, f"{name}.fbx")
    if not os.path.exists(path):
        print(f"PREVIEW_FAILED no such model: {path}")
        sys.exit(1)

    before = set(bpy.data.objects)
    bpy.ops.import_scene.fbx(filepath=path)
    obj = next(o for o in set(bpy.data.objects) - before if o.type == "MESH")

    bpy.ops.object.select_all(action="DESELECT")
    obj.select_set(True)
    bpy.context.view_layer.objects.active = obj
    bpy.ops.object.transform_apply(location=False, rotation=True, scale=True)
    return obj


body = bring_in(model.BODY_NAME)

for corner, sign, is_front in (("FL", -1, True), ("FR", 1, True),
                               ("RL", -1, False), ("RR", 1, False)):
    wheel = bring_in(model.WHEEL_FRONT_NAME if is_front else model.WHEEL_REAR_NAME)
    wheel.name = f"Preview_Wheel_{corner}"
    radius = model.FRONT_WHEEL_RADIUS if is_front else model.REAR_WHEEL_RADIUS
    track = model.FRONT_TRACK if is_front else model.REAR_TRACK
    axle_z = model.FRONT_AXLE_Z if is_front else model.REAR_AXLE_Z
    wheel.location = model.u(track * 0.5 * sign, radius, axle_z)

if hasattr(model, "STEERING_WHEEL_NAME"):
    rim = bring_in(model.STEERING_WHEEL_NAME)
    rim.name = "Preview_SteeringWheel"
    # Same placement KartBlueprint gives the "Steering" pivot: sitting on the hub, with
    # the column axis running back down to the rack. Authored about local +Z here, so
    # aiming that axis is the whole rotation.
    column = model.u(*model.STEERING_HUB) - model.u(*model.STEERING_RACK)
    rim.location = model.u(*model.STEERING_HUB)
    rim.rotation_euler = column.to_track_quat("Z", "Y").to_euler()

# Ground, so the kart is resting on something rather than floating in a void.
bpy.ops.mesh.primitive_plane_add(size=30, location=(0, 0, 0))
ground = bpy.context.active_object
ground.name = "PreviewGround"
ground_mat = bpy.data.materials.new("PreviewGround")
ground_mat.use_nodes = True
ground_bsdf = ground_mat.node_tree.nodes["Principled BSDF"]
ground_bsdf.inputs["Base Color"].default_value = (0.24, 0.23, 0.21, 1.0)
ground_bsdf.inputs["Roughness"].default_value = 0.95
ground.data.materials.append(ground_mat)

bpy.ops.object.light_add(type="SUN", location=(4, -6, 9))
key = bpy.context.active_object
key.data.energy = 4.0
key.rotation_euler = (math.radians(50), 0, math.radians(40))

bpy.ops.object.light_add(type="AREA", location=(-6, 4, 4))
fill = bpy.context.active_object
fill.data.energy = 600.0
fill.data.size = 10.0

# Three-quarter from the front and slightly above, which is roughly the race camera's
# angle - a kart that only looks right in plan is not much use here. The kart's nose is at
# Blender -Y, because Unity +Z forward maps there.
bpy.ops.object.empty_add(location=(0, 0, 0.68))
aim = bpy.context.active_object

bpy.ops.object.camera_add(location=(-3.4, -4.4, 2.1))
cam = bpy.context.active_object
bpy.context.scene.camera = cam
cam.data.lens = 62
track_to = cam.constraints.new(type="TRACK_TO")
track_to.target = aim
track_to.track_axis = "TRACK_NEGATIVE_Z"
track_to.up_axis = "UP_Y"

scene = bpy.context.scene
scene.render.engine = "BLENDER_EEVEE"
scene.render.resolution_x = 1200
scene.render.resolution_y = 800
scene.render.film_transparent = False

out_dir = os.path.join(os.path.dirname(os.path.abspath(__file__)), "previews")
os.makedirs(out_dir, exist_ok=True)
scene.render.filepath = os.path.join(out_dir, f"{style}.png")
bpy.ops.render.render(write_still=True)

print(f"PREVIEW_OK {scene.render.filepath}")
print(f"PREVIEW_DIMS body={[round(v, 3) for v in body.dimensions]}")
