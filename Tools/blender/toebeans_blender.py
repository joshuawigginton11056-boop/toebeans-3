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
from contextlib import contextmanager

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


def taper(bm, a, b, r0, r1, skin=0, segments=6):
    """A cone frustum from `a` at radius `r0` to `b` at radius `r1`.

    `tube` with two radii. Limbs want it: a leg that is the same thickness at the hock as
    at the hip is a table leg, and the taper is most of what separates the two.
    """
    matrix, length = _aim(a, b)
    made = bmesh.ops.create_cone(
        bm, cap_ends=True, cap_tris=False, segments=segments,
        radius1=r0, radius2=r1, depth=length, matrix=matrix)
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


# --------------------------------------------------------------------------------------
# Solids
#
# tube/slab/cuboid above are enough to build a tube frame out of. Anything
# building-shaped wants three more: boxes given as bounds, beams given as a run with a
# cross-section you control, and prisms for gable ends and wedges. These were written for
# models/cabin.py and live here because every prop that is "boxes plus one chamfer pass"
# needs the same three - a barn and a chicken coop are the cabin problem at other sizes.
# --------------------------------------------------------------------------------------

def box(bm, lo, hi, skin=0):
    """An axis-aligned box given opposite corners, in either order."""
    lo, hi = Vector(lo), Vector(hi)
    centre = (lo + hi) * 0.5
    size = Vector((abs(hi.x - lo.x), abs(hi.y - lo.y), abs(hi.z - lo.z)))
    if min(size) < 1e-5:
        raise ValueError(f"degenerate box, size {tuple(round(c, 4) for c in size)}")
    made = bmesh.ops.create_cube(bm, size=1.0, matrix=(
        Matrix.Translation(centre) @ Matrix.Diagonal(size).to_4x4()))
    _tag(made["verts"], skin)


def beam(bm, a, b, w, h, skin=0, up=(0.0, 0.0, 1.0)):
    """A box whose long axis runs a->b, `h` measured along `up`, `w` across both.

    Endpoints rather than centre-plus-rotation, for the reason KartBlueprint.Segment
    gives: when a dimension changes, a frame built from endpoints stays joined up and a
    frame built from centres quietly comes apart. `up` is stated rather than derived,
    because a rafter and a wall brace run in different planes and each wants its depth
    measured in a different direction - guessing that from the run alone twists the brace.
    """
    a, b = Vector(a), Vector(b)
    run = b - a
    length = run.length
    if length < 1e-6:
        raise ValueError(f"beam endpoints coincide at {tuple(round(c, 4) for c in a)}")
    z_axis = run / length

    up = Vector(up)
    y_axis = up - z_axis * up.dot(z_axis)
    if y_axis.length < 1e-4:
        # `up` is parallel to the run and so says nothing. Any perpendicular will do.
        alt = Vector((0.0, 0.0, 1.0)) if abs(z_axis.z) < 0.9 else Vector((0.0, 1.0, 0.0))
        y_axis = alt - z_axis * alt.dot(z_axis)
    y_axis.normalize()
    x_axis = y_axis.cross(z_axis)

    basis = Matrix((
        (x_axis.x, y_axis.x, z_axis.x, 0.0),
        (x_axis.y, y_axis.y, z_axis.y, 0.0),
        (x_axis.z, y_axis.z, z_axis.z, 0.0),
        (0.0, 0.0, 0.0, 1.0),
    ))
    basis.translation = (a + b) * 0.5
    made = bmesh.ops.create_cube(bm, size=1.0, matrix=(
        basis @ Matrix.Diagonal(Vector((w, h, length))).to_4x4()))
    _tag(made["verts"], skin)


def prism(bm, points, extrude, skin=0):
    """A convex polygon extruded into a solid. Gable ends, mostly.

    A gable is a triangle, and stacking boxes to fake one leaves a staircase under the
    rake - exactly the hard-cornered look these models exist to get away from.
    """
    verts = [bm.verts.new(Vector(p)) for p in points]
    bm.faces.new(verts)
    made = bmesh.ops.extrude_face_region(bm, geom=list(verts[0].link_faces))
    shifted = [g for g in made["geom"] if isinstance(g, bmesh.types.BMVert)]
    bmesh.ops.translate(bm, verts=shifted, vec=Vector(extrude))
    _tag(verts + shifted, skin)


@contextmanager
def moved(bm, matrix):
    """Everything built inside the block gets `matrix` applied to it afterwards.

    For parts easier to author upright and then knocked over: a ruin's door hanging off
    one hinge, a plough share tipped into the furrow, a cow's head lowered to graze.
    Authoring those in place means writing every endpoint pre-rotated, which is unreadable
    and, worse, unadjustable.
    """
    before = set(bm.verts)
    yield
    fresh = [v for v in bm.verts if v not in before]
    if fresh:
        bmesh.ops.transform(bm, matrix=matrix, verts=fresh)


def spin(pivot, axis, degrees):
    """A rotation of `degrees` about `axis` through `pivot`, for use with `moved`."""
    pivot = Vector(pivot)
    return (Matrix.Translation(pivot)
            @ Matrix.Rotation(math.radians(degrees), 4, Vector(axis))
            @ Matrix.Translation(-pivot))


def chamfer(bm, offset, segments=1):
    """The one bevel pass that makes an assembly of boxes read as one carved object.

    Once over the finished mesh, never per part: a post meeting a rail has to chamfer to
    the same width as the rail, and that is the whole difference between a stylised solid
    and a pile of cubes. `material=-1` lets each bevel face inherit its neighbour's slot,
    so the material split survives the pass.

    Clamped bevels on the thinnest parts can meet in the middle and leave a face with no
    area, which `validate` rejects and Unity ships as broken normals - hence the dissolve.
    """
    bmesh.ops.bevel(
        bm,
        geom=list(bm.verts) + list(bm.edges) + list(bm.faces),
        offset=offset,
        offset_type="OFFSET",
        segments=segments,
        profile=0.5,
        affect="EDGES",
        clamp_overlap=True,
        loop_slide=True,
        material=-1,
    )
    bmesh.ops.dissolve_degenerate(bm, dist=1e-5, edges=bm.edges)


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

#: The three settings that are actually a set. Changing one silently rotates every prop
#: this pipeline has ever produced, so they live in one place and verify_axes.py asserts
#: the signature they produce. Do not edit without re-running it.
_UNITY_AXES = dict(
    bake_space_transform=True,   # bakes geometry to Y-up; see module docstring
    axis_forward="-Z",
    axis_up="Y",
)


def _write_fbx(fbx_path):
    bpy.ops.export_scene.fbx(
        filepath=fbx_path,
        use_selection=True,
        object_types={"MESH"},
        apply_unit_scale=True,
        apply_scale_options="FBX_SCALE_ALL",
        use_mesh_modifiers=True,
        mesh_smooth_type="FACE",
        path_mode="COPY",
        **_UNITY_AXES,
    )


def export_hierarchy_for_unity(objs, name, out_dir=None):
    """Write a whole parented hierarchy to <name>.fbx as one file.

    A prop is one mesh; anything with a moving part is not. The kart solved that by
    exporting a file per part, which is right when the runtime places each part itself
    from dimensions it already owns. An animal is the other case: the parts only mean
    anything in the arrangement they were authored in, and a file per leg would leave
    Unity reassembling a cow from eight FBXs and a table of offsets.

    So the parts go out as one file with their parenting intact, and Unity imports a
    GameObject tree with a child per limb, each pivoted on its own joint. See
    `build_hierarchy` for the part-naming contract the C# side reads.

    `objs[0]` is the root. Selection order does not matter to the exporter, but the
    active object does for the operator's context, so it is set explicitly.
    """
    out_dir = out_dir or EXPORT_DIR
    os.makedirs(out_dir, exist_ok=True)

    bpy.ops.object.select_all(action="DESELECT")
    for ob in objs:
        ob.select_set(True)
    bpy.context.view_layer.objects.active = objs[0]

    fbx_path = os.path.join(out_dir, f"{name}.fbx")
    _write_fbx(fbx_path)
    return {"fbx": fbx_path}


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
    _write_fbx(fbx_path)

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


# --------------------------------------------------------------------------------------
# Multi-part props
#
# The rule the kart established is "anything that moves is its own mesh". A cow's legs
# move, so they are their own meshes - but unlike a kart wheel, a leg is not something the
# runtime can place from dimensions it already owns. It only means anything in the
# arrangement it was authored in. So the parts stay in one file, parented, each pivoted on
# its own joint, and Unity gets a GameObject tree it can drive directly.
# --------------------------------------------------------------------------------------

class Part:
    """One rigid piece of a multi-part prop, authored in the assembly's own space.

    `bm` holds the piece's geometry positioned where it belongs in the finished prop -
    not at the origin waiting to be placed. `pivot` is the joint it turns about, given in
    those same coordinates; that becomes the object's origin, so a shoulder written at
    (0.31, 0.62, 0.78) is the point the leg swings around in Unity with no offset to
    remember. `parent` names the Part this one hangs off, or None for the root.
    """

    def __init__(self, name, bm, pivot=(0.0, 0.0, 0.0), parent=None):
        self.name = name
        self.bm = bm
        self.pivot = Vector(pivot)
        self.parent = parent


def build_hierarchy(parts, name, palette=None, chamfer_m=None, chamfer_segments=1,
                    max_tris=None, max_size_m=None, faceted=True, require_uvs=True,
                    plan_tolerance=0.25, drop_to_ground=False):
    """finalise -> parent -> validate -> export, for a prop made of several parts.

    The root part's pivot is the prop's origin and follows the same convention as every
    other prop here: the centre of the footprint, on the ground. A cow's origin is between
    its four hooves, so dropping it on the terrain means what it means for a rock.

    `chamfer_m` is applied to each part separately but with the same offset, which is the
    part of cabin.py's one-pass rule that still holds across objects: what makes an
    assembly read as one carved object is that every corner is cut to the same width. A
    single pass over all of them is not available here and is not what mattered.

    `plan_tolerance` is how far the root pivot may sit from the middle of the assembly's
    footprint, as a fraction of the larger plan dimension. It exists because an origin
    that is not roughly under the prop turns "drop it on the terrain" back into eyeballing
    an offset per instance - the exact thing the convention is for. A lowered head or a
    long tail moves the footprint's centre, hence a fraction rather than a fixed metre.

    `drop_to_ground` settles the whole assembly onto Z=0 after chamfering, instead of
    demanding the author land it there. Reach for it when the prop stands on something
    round: a wheel's lowest point is a chamfered facet corner, so where it actually ends up
    is an output of the bevel rather than a number anybody can write down. Leave it off for
    anything standing on a flat foot - there, the base check earns its keep by catching
    real mistakes, and it has: it is what found the windpump's battered legs poking five
    millimetres through the ground.
    """
    if not parts:
        raise ValueError("build_hierarchy needs at least one part")

    by_name = {}
    for p in parts:
        if p.name in by_name:
            raise ValueError(f"duplicate part name {p.name!r}")
        by_name[p.name] = p

    roots = [p for p in parts if p.parent is None]
    if len(roots) != 1:
        raise ValueError(f"expected exactly one root part, got {[p.name for p in roots]}")
    for p in parts:
        if p.parent is not None and p.parent not in by_name:
            raise ValueError(f"part {p.name!r} names a parent {p.parent!r} that does not exist")

    root = roots[0]
    if abs(root.pivot.z) > 1e-6:
        raise AssertionError(
            f"root part {root.name!r} pivots at z={root.pivot.z:.4f}; the prop origin has "
            "to be on the ground - see the origin convention in the README")

    # ---------------------------------------------------------------- chamfer and settle
    for p in parts:
        if chamfer_m:
            chamfer(p.bm, chamfer_m, chamfer_segments)
        bmesh.ops.recalc_face_normals(p.bm, faces=p.bm.faces)

    if drop_to_ground:
        floor = min(v.co.z for p in parts for v in p.bm.verts)
        if abs(floor) > 1e-9:
            # Geometry and joints move together - a wheel that settles two centimetres
            # takes its axle down with it, or the hub stops being the middle of the wheel.
            #
            # The root's pivot is the exception, and stays put. It is not a joint on the
            # prop; it is the ground contact point the whole convention is written around,
            # and the ground has not moved.
            for p in parts:
                bmesh.ops.translate(p.bm, verts=list(p.bm.verts), vec=(0.0, 0.0, -floor))
                if p.parent is not None:
                    p.pivot = Vector((p.pivot.x, p.pivot.y, p.pivot.z - floor))

    # ---------------------------------------------------------------- objects
    objs = {}
    for p in parts:
        obj = mesh_from_bmesh(p.bm, p.name)
        if palette:
            assign_materials(obj, palette)

        # The geometry was authored in assembly space; slide it so the joint lands on the
        # object's own origin, then put the object back where it was. Same two-step as
        # set_origin_to_base, with the pivot stated instead of derived.
        obj.data.transform(Matrix.Translation(-p.pivot))
        obj.location = p.pivot

        for poly in obj.data.polygons:
            poly.use_smooth = not faceted
        obj.data.update()
        objs[p.name] = obj

    bpy.context.view_layer.update()

    # ---------------------------------------------------------------- parenting
    #
    # Every part is exported as a direct child of the root, whatever `Part.parent` says.
    # That is not a simplification, it is the exporter's limit, and it was measured:
    #
    #   Bake Space Transform is what puts the geometry in the Y-up frame Unity wants, and
    #   the Blender manual notes it is unsupported for parented objects. At one level deep
    #   it is in fact fine - verify_axes.py asserts that, and the child comes back
    #   unrotated and exactly where it was authored. At two levels deep it is not: a
    #   windpump exported as Tower > Head > Rotor came back with the rotor eight metres
    #   out and rotated 90 degrees, because the axis conversion was baked into the root and
    #   the grandchild but not into the object between them.
    #
    # So the deeper rig travels as data instead. Each part's origin is already its joint in
    # world space, so re-parenting in Unity with worldPositionStays keeps every joint
    # exactly where it was authored - see FarmAssetSetup.BuildRig.
    for p in parts:
        if p.parent is None:
            continue
        child = objs[p.name]
        child.parent = objs[root.name]
        child.matrix_parent_inverse = objs[root.name].matrix_world.inverted()
    bpy.context.view_layer.update()

    # The intended rig, depth-first from the root, for whoever rebuilds it downstream.
    seen = set()

    def walk(name):
        if name in seen:
            raise ValueError(f"the rig loops back on {name!r}")
        seen.add(name)
        for kid in (q for q in parts if q.parent == name):
            walk(kid.name)

    walk(root.name)
    if len(seen) != len(parts):
        raise ValueError(f"parts not reachable from the root: {sorted(set(by_name) - seen)}")

    # ---------------------------------------------------------------- checks
    stats = {"name": name, "parts": [], "tris": 0}
    lowest = None
    plan_lo = [None, None]
    plan_hi = [None, None]

    for p in parts:
        obj = objs[p.name]
        if require_uvs:
            ensure_uvs(obj)

        # Per part, minus the two whole-prop checks: a leg is not on the ground and its
        # origin is a joint, not a footprint.
        part_stats = validate(obj, require_uvs=require_uvs, require_base_on_ground=False)
        part_stats["pivot"] = [round(v, 4) for v in p.pivot]
        part_stats["parent"] = p.parent
        stats["parts"].append(part_stats)
        stats["tris"] += part_stats["tris"]

        corners = [obj.matrix_world @ Vector(c) for c in obj.bound_box]
        for c in corners:
            lowest = c.z if lowest is None else min(lowest, c.z)
            for axis, value in enumerate((c.x, c.y)):
                plan_lo[axis] = value if plan_lo[axis] is None else min(plan_lo[axis], value)
                plan_hi[axis] = value if plan_hi[axis] is None else max(plan_hi[axis], value)

    problems = []
    if abs(lowest) > 1e-4:
        problems.append(f"assembly base is not on Z=0 (lowest point {lowest:.5f})")

    span = [plan_hi[0] - plan_lo[0], plan_hi[1] - plan_lo[1]]
    allowed = max(span) * plan_tolerance
    for axis, label in enumerate("xy"):
        middle = (plan_lo[axis] + plan_hi[axis]) * 0.5
        drift = abs(middle - root.pivot[axis])
        if drift > allowed:
            problems.append(
                f"origin sits {drift:.2f} m off the footprint centre in {label} "
                f"(allowed {allowed:.2f} m)")

    dims = [round(span[0], 4), round(span[1], 4), 0.0]
    tallest = max(
        (objs[p.name].matrix_world @ Vector(c)).z
        for p in parts for c in objs[p.name].bound_box)
    dims[2] = round(tallest - (lowest or 0.0), 4)

    if max_size_m is not None and max(dims) > max_size_m:
        problems.append(f"largest dimension {max(dims):.2f} m exceeds {max_size_m} m")
    if max_tris is not None and stats["tris"] > max_tris:
        problems.append(f"{stats['tris']} triangles exceeds budget of {max_tris}")

    if problems:
        raise AssertionError(
            "{} failed validation:\n  - {}".format(name, "\n  - ".join(problems)))

    stats["dims_m"] = dims
    stats["part_count"] = len(parts)

    # ---------------------------------------------------------------- export
    ordered = [objs[root.name]] + [objs[p.name] for p in parts if p.parent is not None]
    written = export_hierarchy_for_unity(ordered, name)
    stats["written"] = {k: os.path.relpath(v, REPO_ROOT) for k, v in written.items()}
    print("BUILT " + repr({k: v for k, v in stats.items() if k != "parts"}))
    return stats
