"""
Shared helpers for building toebeans-3 props in Blender.

Every prop in this folder is a script rather than a .blend, for the same reason the
terrain, the track and the volcano are generators rather than saved meshes: a script is
re-runnable and diffable, and a binary .blend is a dead end the moment you want to change
one number. Run a model script and you get the same mesh you got last time.

The two things this module exists to stop:

  Orientation drift.  Blender is Z-up, Unity is Y-up, and the FBX exporter has several
                      combinations of settings that all look plausible and only one that
                      is right. The settings in `export_for_unity` were chosen by
                      measuring a store asset the scene already uses correctly
                      (BOKI cliff_1.fbx): it re-imports into Blender at rot=[90,0,0] with
                      Y-up geometry, and these settings reproduce that exactly. Turning
                      `bake_space_transform` off leaves the geometry Z-up, which is what
                      puts a -90 X rotation on the prefab in Unity.

  Geometry a kart cannot drive on.  See the checks in `validate`. This is a kart racer,
                      the mesh is the collider, and a prop with a stray vertex or an
                      unapplied scale becomes someone's crash report later.

Import it from a model script with the sibling-path preamble in `models/volcanic_rock.py`.
"""

import math
import os

import bmesh
import bpy
from mathutils import Vector

# Tools/blender/toebeans_blender.py -> repo root is two levels up.
REPO_ROOT = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
EXPORT_DIR = os.path.join(REPO_ROOT, "Assets", "GeneratedModels")


# --------------------------------------------------------------------------------------
# Scene setup
# --------------------------------------------------------------------------------------

def fresh_scene():
    """Empty metric scene. Factory settings, so a stray preference cannot change a build."""
    bpy.ops.wm.read_factory_settings(use_empty=True)
    units = bpy.context.scene.unit_settings
    units.system = "METRIC"
    units.scale_length = 1.0


# --------------------------------------------------------------------------------------
# Finishing
# --------------------------------------------------------------------------------------

def set_origin_to_base(obj):
    """Put the origin at the centre of the object's footprint, on its lowest point.

    Same convention as the Volcano generator: the origin is the middle of the foot,
    standing on the ground. That is what makes "drop it on the terrain" mean something
    rather than leaving you to eyeball a Y offset per instance.
    """
    bpy.context.view_layer.update()
    corners = [obj.matrix_world @ Vector(c) for c in obj.bound_box]
    base = Vector((
        sum(c.x for c in corners) / 8.0,
        sum(c.y for c in corners) / 8.0,
        min(c.z for c in corners),
    ))
    obj.data.transform(_translation(-base))
    obj.matrix_world.translation += base
    obj.location = (0.0, 0.0, 0.0)
    bpy.context.view_layer.update()


def _translation(v):
    from mathutils import Matrix
    return Matrix.Translation(v)


def finalise(obj, faceted=True):
    """Apply transforms, fix normals, and set shading. Call before validate/export.

    Applying the transform matters more than it looks: an unapplied scale of 0.01 exports
    a prop that is the right size in the viewport and the wrong size in Unity.
    """
    bpy.ops.object.select_all(action="DESELECT")
    obj.select_set(True)
    bpy.context.view_layer.objects.active = obj

    bpy.ops.object.transform_apply(location=False, rotation=True, scale=True)

    me = obj.data
    bm = bmesh.new()
    bm.from_mesh(me)
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    bm.to_mesh(me)
    bm.free()

    # Flat shading is the whole look here - the facets are the style, not a limitation.
    # See the LowPolyTerrain generator: planarity is what reads as low poly, not low
    # vertex count.
    for poly in me.polygons:
        poly.use_smooth = not faceted

    set_origin_to_base(obj)
    me.update()


def ensure_uvs(obj, angle_limit_deg=66.0):
    """Smart-project a UV set if the mesh has none.

    Cheap UVs are fine for rock and metal, but anything that will wear a lava material
    needs its UVs looked at properly - MoltenLava reads UV density, and a bad unwrap
    reads as flat dead lava rather than as a UV problem.
    """
    if len(obj.data.uv_layers) > 0:
        return False

    bpy.ops.object.select_all(action="DESELECT")
    obj.select_set(True)
    bpy.context.view_layer.objects.active = obj
    bpy.ops.object.mode_set(mode="EDIT")
    bpy.ops.mesh.select_all(action="SELECT")
    bpy.ops.uv.smart_project(angle_limit=math.radians(angle_limit_deg), island_margin=0.02)
    bpy.ops.object.mode_set(mode="OBJECT")
    return True


# --------------------------------------------------------------------------------------
# Validation
# --------------------------------------------------------------------------------------

def validate(obj, max_tris=None, require_uvs=True, max_size_m=None):
    """Raise if the mesh would misbehave in Unity. Returns a stats dict when it passes.

    These are failures, not warnings, on purpose. A warning in a build log is a warning
    nobody reads until the prop is already scattered across the map a thousand times.
    """
    problems = []
    me = obj.data

    rot = [math.degrees(a) for a in obj.rotation_euler]
    if any(abs(a) > 1e-4 for a in rot):
        problems.append(f"rotation not applied: {[round(a, 4) for a in rot]}")
    if any(abs(s - 1.0) > 1e-6 for s in obj.scale):
        problems.append(f"scale not applied: {[round(s, 6) for s in obj.scale]}")

    if require_uvs and len(me.uv_layers) == 0:
        problems.append("no UV layer (call ensure_uvs, or pass require_uvs=False)")

    bm = bmesh.new()
    bm.from_mesh(me)
    bm.faces.ensure_lookup_table()

    degenerate = sum(1 for f in bm.faces if f.calc_area() < 1e-9)
    if degenerate:
        problems.append(f"{degenerate} zero-area face(s)")

    loose_verts = sum(1 for v in bm.verts if not v.link_faces)
    if loose_verts:
        problems.append(f"{loose_verts} loose vertex/vertices")

    loose_edges = sum(1 for e in bm.edges if not e.link_faces)
    if loose_edges:
        problems.append(f"{loose_edges} loose edge(s)")

    tris = sum(len(f.verts) - 2 for f in bm.faces)
    ngons = sum(1 for f in bm.faces if len(f.verts) > 4)
    bm.free()

    if max_tris is not None and tris > max_tris:
        problems.append(f"{tris} triangles exceeds budget of {max_tris}")

    dims = list(obj.dimensions)
    if max_size_m is not None and max(dims) > max_size_m:
        problems.append(f"largest dimension {max(dims):.2f} m exceeds {max_size_m} m")

    # The origin convention from set_origin_to_base: sitting on Z=0, centred in plan.
    bpy.context.view_layer.update()
    corners = [obj.matrix_world @ Vector(c) for c in obj.bound_box]
    if abs(min(c.z for c in corners)) > 1e-4:
        problems.append(f"base is not on Z=0 (lowest point {min(c.z for c in corners):.5f})")

    if problems:
        raise AssertionError(
            "{} failed validation:\n  - {}".format(obj.name, "\n  - ".join(problems))
        )

    return {
        "name": obj.name,
        "verts": len(me.vertices),
        "faces": len(me.polygons),
        "tris": tris,
        "ngons": ngons,
        "uv_layers": len(me.uv_layers),
        "dims_m": [round(d, 4) for d in dims],
    }


# --------------------------------------------------------------------------------------
# Export
# --------------------------------------------------------------------------------------

def export_for_unity(obj, name, out_dir=None, also_glb=False):
    """Write <name>.fbx into Assets/GeneratedModels, oriented for Unity.

    Do not change axis_up/axis_forward/bake_space_transform without re-running
    Tools/blender/verify_axes.py. Those three are a set; changing one silently rotates
    every prop this pipeline has ever produced.
    """
    out_dir = out_dir or EXPORT_DIR
    os.makedirs(out_dir, exist_ok=True)

    bpy.ops.object.select_all(action="DESELECT")
    obj.select_set(True)
    bpy.context.view_layer.objects.active = obj

    fbx_path = os.path.join(out_dir, f"{name}.fbx")
    bpy.ops.export_scene.fbx(
        filepath=fbx_path,
        use_selection=True,
        object_types={"MESH"},
        apply_unit_scale=True,
        apply_scale_options="FBX_SCALE_ALL",
        bake_space_transform=True,   # bakes geometry to Y-up; see module docstring
        axis_forward="-Z",
        axis_up="Y",
        use_mesh_modifiers=True,
        mesh_smooth_type="FACE",
        path_mode="COPY",
    )

    written = {"fbx": fbx_path}

    if also_glb:
        glb_path = os.path.join(out_dir, f"{name}.glb")
        bpy.ops.export_scene.gltf(
            filepath=glb_path, export_format="GLB", use_selection=True
        )
        written["glb"] = glb_path

    return written


def build(obj, name, max_tris=None, require_uvs=True, max_size_m=None, faceted=True,
          also_glb=False):
    """finalise -> ensure_uvs -> validate -> export. The whole tail of a model script."""
    finalise(obj, faceted=faceted)
    if require_uvs:
        ensure_uvs(obj)
    stats = validate(obj, max_tris=max_tris, require_uvs=require_uvs, max_size_m=max_size_m)
    written = export_for_unity(obj, name, also_glb=also_glb)

    stats["written"] = {k: os.path.relpath(v, REPO_ROOT) for k, v in written.items()}
    print("BUILT " + repr(stats))
    return stats
