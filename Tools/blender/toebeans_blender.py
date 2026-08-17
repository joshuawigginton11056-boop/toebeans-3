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
from mathutils import Matrix, Vector

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
# Mesh primitives
#
# Enough to build a tube frame out of. Everything here writes into a bmesh you own and
# tags what it made with a material slot index, so one mesh can still carry the kart's
# body/frame/seat/rim/rubber split instead of arriving in Unity as one flat colour.
#
# Parts are specified as endpoints rather than centre-plus-rotation, the same way
# KartBlueprint.Segment does it on the C# side: when a dimension changes, a frame built
# from endpoints stays joined up and a frame built from centres quietly comes apart.
# --------------------------------------------------------------------------------------

def _tag(bm_verts, skin):
    """Stamp a material slot onto everything a bmesh.ops call just created.

    Reached through the new vertices rather than by diffing the face list, which keeps it
    proportional to the part rather than to the mesh so far. Safe because nothing here
    merges geometry - each part's vertices are its own.
    """
    for v in bm_verts:
        for f in v.link_faces:
            f.material_index = skin


def _aim(a, b):
    """Transform placing a primitive's local +Z along a->b, centred between them.

    to_track_quat keeps local X on world X for any direction in the YZ plane, which is
    what stops a tread lug or a seat back from arriving twisted about its own axis.
    """
    a, b = Vector(a), Vector(b)
    delta = b - a
    length = delta.length
    if length < 1e-7:
        raise ValueError(f"endpoints coincide at {tuple(round(c, 4) for c in a)}")
    matrix = (Matrix.Translation((a + b) * 0.5)
              @ delta.to_track_quat("Z", "Y").to_matrix().to_4x4())
    return matrix, length


def tube(bm, a, b, radius, skin=0, segments=6):
    """A capped cylinder spanning a->b. Six sides by default: the facets are the style."""
    matrix, length = _aim(a, b)
    made = bmesh.ops.create_cone(
        bm, cap_ends=True, cap_tris=False, segments=segments,
        radius1=radius, radius2=radius, depth=length, matrix=matrix)
    _tag(made["verts"], skin)


def slab(bm, a, b, width, thickness, skin=0):
    """A box whose long axis spans a->b, `width` across world X, `thickness` the rest."""
    matrix, length = _aim(a, b)
    made = bmesh.ops.create_cube(bm, size=1.0, matrix=(
        matrix @ Matrix.Diagonal(Vector((width, thickness, length))).to_4x4()))
    _tag(made["verts"], skin)


def cuboid(bm, centre, size, skin=0):
    """An axis-aligned box, given its centre and full size."""
    made = bmesh.ops.create_cube(bm, size=1.0, matrix=(
        Matrix.Translation(Vector(centre)) @ Matrix.Diagonal(Vector(size)).to_4x4()))
    _tag(made["verts"], skin)


def mesh_from_bmesh(bm, name):
    """Hand a finished bmesh over to a real object, and hold on to the bmesh no longer."""
    me = bpy.data.meshes.new(name)
    bm.to_mesh(me)
    bm.free()
    obj = bpy.data.objects.new(name, me)
    bpy.context.collection.objects.link(obj)
    return obj


def assign_materials(obj, palette):
    """Create the object's material slots, in the order the skin indices assume.

    `palette` is a list of (name, (r, g, b), metallic, roughness). Slot order is the
    contract between this and the skin constants a model script indexes with, so append
    to it rather than reordering it.

    Rebuilding the slot list resets every polygon's material_index, so the assignment the
    mesh is already carrying is saved and put back. That reset is silent - the mesh still
    renders, just entirely in slot zero - which is a tedious thing to find by eye.
    """
    me = obj.data
    saved = [p.material_index for p in me.polygons]

    me.materials.clear()
    for name, rgb, metallic, roughness in palette:
        mat = bpy.data.materials.get(name) or bpy.data.materials.new(name)
        mat.use_nodes = True
        bsdf = mat.node_tree.nodes["Principled BSDF"]
        bsdf.inputs["Base Color"].default_value = (*rgb, 1.0)
        bsdf.inputs["Metallic"].default_value = metallic
        bsdf.inputs["Roughness"].default_value = roughness
        me.materials.append(mat)

    for poly, index in zip(me.polygons, saved):
        poly.material_index = index


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
    return Matrix.Translation(v)


def finalise(obj, faceted=True, origin="base"):
    """Apply transforms, fix normals, and set shading. Call before validate/export.

    Applying the transform matters more than it looks: an unapplied scale of 0.01 exports
    a prop that is the right size in the viewport and the wrong size in Unity.

    `origin` picks where the object's pivot ends up:

      "base"  the prop convention above - centre of the footprint, on the lowest point.
      "keep"  leave the pivot on the world origin, because the mesh was authored around
              its own mount point. A kart body is authored around the kart's origin, which
              is on the ground between the wheels and *below* the floor pan; a wheel is
              authored around its hub, which is a radius up from the ground. Re-centring
              either one on its lowest vertex would slide it away from the anchor the
              runtime positions it at.
    """
    if origin not in ("base", "keep"):
        raise ValueError(f"origin must be 'base' or 'keep', got {origin!r}")

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

    if origin == "base":
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

def validate(obj, max_tris=None, require_uvs=True, max_size_m=None,
             require_base_on_ground=True):
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
    # Off for anything authored around a mount point rather than a footprint - see the
    # `origin` argument on finalise.
    if require_base_on_ground:
        bpy.context.view_layer.update()
        corners = [obj.matrix_world @ Vector(c) for c in obj.bound_box]
        lowest = min(c.z for c in corners)
        if abs(lowest) > 1e-4:
            problems.append(f"base is not on Z=0 (lowest point {lowest:.5f})")

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
          also_glb=False, origin="base"):
    """finalise -> ensure_uvs -> validate -> export. The whole tail of a model script."""
    finalise(obj, faceted=faceted, origin=origin)
    if require_uvs:
        ensure_uvs(obj)
    # An origin="keep" mesh is authored around its mount point, so the base check that
    # enforces the prop convention would be asserting the wrong thing about it.
    stats = validate(obj, max_tris=max_tris, require_uvs=require_uvs, max_size_m=max_size_m,
                     require_base_on_ground=(origin == "base"))
    written = export_for_unity(obj, name, also_glb=also_glb)

    stats["written"] = {k: os.path.relpath(v, REPO_ROOT) for k, v in written.items()}
    print("BUILT " + repr(stats))
    return stats
