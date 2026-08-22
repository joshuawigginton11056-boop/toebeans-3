"""
Renders a whole pack of built props into one picture, laid out on a grid.

    blender --background --factory-startup --python Tools/blender/contact_sheet.py -- farm_props
    blender --background --factory-startup --python Tools/blender/contact_sheet.py -- farm_props farm_fences

Takes manifest names (the files `farmyard.Manifest` writes under
Assets/GeneratedModels/Manifests) and writes Tools/blender/previews/sheet_<name>.png.

`preview.py` answers "is this prop right". This answers the question that only shows up
once a pack exists: **do these look like they came from the same place?** A palette drifts
one prop at a time and it is invisible while you are looking at one prop at a time - the
barn's timber and the cart's timber being two different browns is obvious here and
essentially undetectable in a row of individual renders.

Props are laid out biggest first, each on its own tile with a one-metre reference cube, so
the grid also reads as a size comparison. Everything is placed on a common ground plane
and lit by one sun, because a per-prop light rig would hide exactly the tonal differences
this is for.
"""

import json
import math
import os
import sys

import bpy
from mathutils import Vector

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import toebeans_blender as tb  # noqa: E402
import farmyard as fy  # noqa: E402

argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
if not argv:
    print("CONTACT_FAILED name a manifest, e.g. farm_props")
    sys.exit(1)


def flat_material(name, rgb, roughness=0.85):
    mat = bpy.data.materials.new(name)
    mat.use_nodes = True
    bsdf = mat.node_tree.nodes["Principled BSDF"]
    bsdf.inputs["Base Color"].default_value = (*rgb, 1.0)
    bsdf.inputs["Roughness"].default_value = roughness
    return mat


models = []
for script in argv:
    path = os.path.join(fy.MANIFEST_DIR, f"{script}.json")
    if not os.path.exists(path):
        print(f"CONTACT_FAILED no manifest at {path}")
        sys.exit(1)
    with open(path, encoding="utf-8") as fh:
        models.extend(json.load(fh)["models"])

if not models:
    print("CONTACT_FAILED nothing in those manifests")
    sys.exit(1)

# Biggest first, so the grid steps down in scale rather than jumping about.
models.sort(key=lambda m: -max(m["dims"]))

tb.fresh_scene()

# One tile per prop, square enough that the sheet is roughly as wide as it is tall.
columns = max(1, int(math.ceil(math.sqrt(len(models)))))
rows = int(math.ceil(len(models) / columns))
tile = max(max(m["dims"]) for m in models) * 1.30 + 1.4

placed = []
for index, model in enumerate(models):
    fbx = os.path.join(tb.EXPORT_DIR, f"{model['name']}.fbx")
    if not os.path.exists(fbx):
        print(f"CONTACT_SKIP {model['name']} (not built)")
        continue

    before = set(bpy.data.objects)
    bpy.ops.import_scene.fbx(filepath=fbx)
    fresh = [o for o in bpy.data.objects if o not in before and o.type == "MESH"]
    if not fresh:
        continue

    col = index % columns
    row = index // columns
    at = Vector(((col - (columns - 1) / 2.0) * tile,
                 ((rows - 1) / 2.0 - row) * tile, 0.0))

    # Move only the roots. A child moved directly would be moved again by its parent.
    for o in fresh:
        if o.parent is None:
            o.location += at
    placed.append((model, at))

if not placed:
    print("CONTACT_FAILED none of those props are built")
    sys.exit(1)

bpy.context.view_layer.update()

# One ground plane under everything, and one reference cube per tile.
span = tile * max(columns, rows) + tile
bpy.ops.mesh.primitive_plane_add(size=span * 1.4, location=(0, 0, 0))
bpy.context.active_object.data.materials.append(
    flat_material("SheetGround", (0.15, 0.15, 0.17)))

ref_mat = flat_material("SheetRef", (0.86, 0.73, 0.20))
for model, at in placed:
    bpy.ops.mesh.primitive_cube_add(
        size=1.0, location=(at.x + tile * 0.36, at.y - tile * 0.30, 0.5))
    bpy.context.active_object.data.materials.append(ref_mat)

bpy.ops.object.light_add(type="SUN", location=(0, 0, 40))
key = bpy.context.active_object
key.data.energy = 4.2
key.data.angle = math.radians(12.0)
key.rotation_euler = (math.radians(46), 0, math.radians(35))

bpy.ops.object.light_add(type="AREA", location=(-span * 0.4, span * 0.3, span * 0.4))
fill = bpy.context.active_object
fill.data.energy = span * span * 0.9
fill.data.size = span * 0.6

# One camera over the lot, looking down the same three-quarter angle preview.py uses, so
# a prop looks the same here as it does in its own render.
d = span * 1.5
bpy.ops.object.camera_add(location=(d * 0.55, -d * 0.55, d * 0.62))
cam = bpy.context.active_object
bpy.context.scene.camera = cam
cam.data.type = "ORTHO"
cam.data.ortho_scale = span * 1.05

target = bpy.data.objects.new("SheetTarget", None)
bpy.context.collection.objects.link(target)
target.location = (0.0, 0.0, tile * 0.10)
track = cam.constraints.new(type="TRACK_TO")
track.target = target
track.track_axis = "TRACK_NEGATIVE_Z"
track.up_axis = "UP_Y"

scene = bpy.context.scene
scene.render.engine = "BLENDER_EEVEE"
scene.render.resolution_x = 1500
scene.render.resolution_y = 1100
scene.render.film_transparent = False

out_dir = os.path.join(os.path.dirname(os.path.abspath(__file__)), "previews")
os.makedirs(out_dir, exist_ok=True)
scene.render.filepath = os.path.join(out_dir, f"sheet_{'_'.join(argv)}.png")
bpy.ops.render.render(write_still=True)

print(f"CONTACT_OK {scene.render.filepath}")
print("CONTACT_ORDER " + ", ".join(m["name"] for m, _ in placed))
