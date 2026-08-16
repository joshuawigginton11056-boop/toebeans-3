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

print()
if failures:
    print(f"AXIS_VERIFY_FAILED ({len(failures)})")
    for f in failures:
        print("  - " + f)
    sys.exit(1)

print("AXIS_VERIFY_OK")
