"""
Regression test for the FBX export orientation.

The export settings in toebeans_blender.py were not chosen from a forum post. They were
chosen by measuring an asset the scene already uses correctly - BOKI's cliff_1.fbx - and
matching its signature. This script re-runs that measurement, so that if a Blender upgrade
changes an exporter default, you find out here instead of finding out from a prop lying on
its side in the map.

    blender --background --factory-startup --python Tools/blender/verify_axes.py

The signature being asserted: an FBX destined for Unity carries Y-up geometry. Round-trip
it back into Blender and the importer stands it up again with a +90 degrees X rotation.
An export that comes back at rotation 0 with Z-up geometry is the failure case - it looks
fine here and arrives in Unity rotated -90.
"""

import math
import os
import sys
import tempfile

import bpy

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import toebeans_blender as tb  # noqa: E402

KNOWN_GOOD = os.path.join(
    tb.REPO_ROOT, "Assets", "BOKI", "LowPolyNature", "Models", "cliff_1.fbx"
)

failures = []


def check(condition, message):
    print(("  ok   " if condition else "  FAIL ") + message)
    if not condition:
        failures.append(message)


def rotation_deg(ob):
    return [round(math.degrees(a), 3) for a in ob.rotation_euler]


print("1. reference asset (the convention we are matching)")
if os.path.exists(KNOWN_GOOD):
    tb.fresh_scene()
    bpy.ops.import_scene.fbx(filepath=KNOWN_GOOD)
    ref = next(o for o in bpy.data.objects if o.type == "MESH")
    ref_rot = rotation_deg(ref)
    check(abs(ref_rot[0] - 90.0) < 0.01,
          f"{os.path.basename(KNOWN_GOOD)} round-trips at X=+90 (got {ref_rot})")
else:
    # This pack is committed, but it is a store asset - do not fail the build over it.
    print(f"  skip  reference not found: {KNOWN_GOOD}")

print("2. our export reproduces that signature")
tb.fresh_scene()
bpy.ops.mesh.primitive_cube_add(size=1)
marker = bpy.context.active_object
marker.name = "AxisMarker"
# Deliberately asymmetric on all three axes so any swap is visible in the dimensions.
marker.scale = (0.5, 2.0, 4.0)
tb.finalise(marker)

authored = [round(d, 4) for d in marker.dimensions]
check(authored == [0.5, 2.0, 4.0], f"authored dims are X=0.5 Y=2 Z=4 (got {authored})")

with tempfile.TemporaryDirectory() as tmp:
    tb.export_for_unity(marker, "AxisMarker", out_dir=tmp)
    tb.fresh_scene()
    bpy.ops.import_scene.fbx(filepath=os.path.join(tmp, "AxisMarker.fbx"))
    back = next(o for o in bpy.data.objects if o.type == "MESH")

    rot = rotation_deg(back)
    dims = [round(d, 4) for d in back.dimensions]
    scale = [round(s, 5) for s in back.scale]

    check(abs(rot[0] - 90.0) < 0.01, f"round-trips at X=+90, i.e. geometry is Y-up (got {rot})")
    check(dims == [0.5, 4.0, 2.0],
          f"tall axis moved Z->Y, so Unity reads it upright (got {dims})")
    check(all(abs(s - 1.0) < 1e-5 for s in scale),
          f"unit scale survives the trip, 1 Blender metre = 1 Unity unit (got {scale})")

print("3. which way round each axis comes out")
# Checks 1 and 2 compare *dimensions*, and a dimension has no sign - so a half turn about
# up was invisible to them, and sat in the pipeline undetected until the first prop that
# cared about facing (a tractor) arrived in Unity pointing backwards.
#
# The mesh-local coordinates of a re-imported FBX are the file's own frame, which is what
# Unity reads. Asserting them here is what pins the signs down.
tb.fresh_scene()
bpy.ops.mesh.primitive_cube_add(size=0.2, location=(0, 0, 0))
core = bpy.context.active_object
core.name = "SignMarker"

# One arm per axis, each a different length, so no two can be confused for each other.
for location, scale in (((0.7, 0.0, 0.0), (0.4, 0.05, 0.05)),
                        ((0.0, 0.9, 0.0), (0.05, 0.4, 0.05)),
                        ((0.0, 0.0, 1.1), (0.05, 0.05, 0.4))):
    bpy.ops.mesh.primitive_cube_add(size=1.0, location=location)
    arm = bpy.context.active_object
    arm.scale = scale
    bpy.ops.object.transform_apply(location=False, rotation=True, scale=True)
    arm.select_set(True)
    core.select_set(True)
    bpy.context.view_layer.objects.active = core
    bpy.ops.object.join()

tb.finalise(core, origin="keep")
tb.ensure_uvs(core)

with tempfile.TemporaryDirectory() as tmp:
    tb.export_for_unity(core, "SignMarker", out_dir=tmp)
    tb.fresh_scene()
    bpy.ops.import_scene.fbx(filepath=os.path.join(tmp, "SignMarker.fbx"))
    back = next(o for o in bpy.data.objects if o.type == "MESH")

    coords = [v.co for v in back.data.vertices]

    def reach(axis):
        """How far the geometry runs each way along one of the file's own axes."""
        return (round(min(c[axis] for c in coords), 3),
                round(max(c[axis] for c in coords), 3))

    x_reach, y_reach, z_reach = reach(0), reach(1), reach(2)

    # The 0.9 arm was built on Blender +Y and the 1.1 arm on Blender +Z.
    check(x_reach[1] > 0.85 and abs(x_reach[0]) < 0.2,
          f"Blender +X lands on file +X (got {x_reach})")
    check(y_reach[1] > 1.05 and abs(y_reach[0]) < 0.2,
          f"Blender +Z lands on file +Y, i.e. up stays up (got {y_reach})")
    check(z_reach[0] < -0.85 and abs(z_reach[1]) < 0.2,
          f"Blender +Y lands on file -Z (got {z_reach})")

# Unity negates X again on import, because FBX is right-handed and Unity is not. So the
# whole convention is Blender (x, y, z) -> Unity (-x, z, -y): the clean mapping with a half
# turn about up. Two things in the project already correct for it, and both should be
# suspected first if this check ever changes: kart_buggy.u(), which authors in Unity space
# and converts per point, and farmyard.face_unity(), which turns a whole prop once.
print("   -> Unity sees Blender (x, y, z) as (-x, z, -y): a half turn about up")

print("4. a parented hierarchy survives the same settings")
# The farm animals are rigid puppets - a body with a head, four legs and a tail hung off
# it as child objects - and the Blender manual warns that Bake Space Transform is not
# supported for parented objects. It demonstrably is, for this shape of hierarchy, and
# this check is here so a Blender upgrade that changes it fails the build rather than
# shipping a cow with its legs somewhere behind it.
tb.fresh_scene()
bpy.ops.mesh.primitive_cube_add(size=1, location=(0, 0, 1))
body = bpy.context.active_object
body.name = "HierBody"
bpy.ops.mesh.primitive_cube_add(size=0.4, location=(0, 0.8, 1.4))
head = bpy.context.active_object
head.name = "HierHead"

for ob in (body, head):
    bpy.ops.object.select_all(action="DESELECT")
    ob.select_set(True)
    bpy.context.view_layer.objects.active = ob
    bpy.ops.object.transform_apply(location=False, rotation=True, scale=True)

head.parent = body
head.matrix_parent_inverse = body.matrix_world.inverted()
bpy.context.view_layer.update()

with tempfile.TemporaryDirectory() as tmp:
    tb.export_hierarchy_for_unity([body, head], "HierMarker", out_dir=tmp)
    tb.fresh_scene()
    bpy.ops.import_scene.fbx(filepath=os.path.join(tmp, "HierMarker.fbx"))
    bpy.context.view_layer.update()

    parts = {o.name: o for o in bpy.data.objects if o.type == "MESH"}
    check(set(parts) == {"HierBody", "HierHead"},
          f"both parts survive the round trip (got {sorted(parts)})")

    if set(parts) == {"HierBody", "HierHead"}:
        root, child = parts["HierBody"], parts["HierHead"]
        check(child.parent is root, "the parent link survives the round trip")

        root_rot = rotation_deg(root)
        check(abs(root_rot[0] - 90.0) < 0.01,
              f"the root carries the +90, i.e. geometry is Y-up (got {root_rot})")

        # The child must not pick up a rotation of its own. One that does is a limb that
        # swings about the wrong axis the moment C# touches it.
        child_rot = rotation_deg(child)
        check(all(abs(a) < 0.01 for a in child_rot),
              f"the child stays unrotated, so its pivot axes are the root's ({child_rot})")

        world = [round(v, 4) for v in child.matrix_world.translation]
        check(world == [0.0, 0.8, 1.4],
              f"the child holds its authored position under the parent (got {world})")

        scales = [round(s, 5) for s in list(root.scale) + list(child.scale)]
        check(all(abs(s - 1.0) < 1e-5 for s in scales),
              f"no part picks up a scale (got {scales})")

print()
if failures:
    print(f"AXIS_VERIFY_FAILED ({len(failures)})")
    for f in failures:
        print("  - " + f)
    sys.exit(1)

print("AXIS_VERIFY_OK")
