# Cave Tunnel Generator

Procedural low-poly caves for Unity. A cross-section is swept along a curve through control points
you drag in the scene view, so the whole cave is editable geometry rather than a baked model.

## Requirements

- Unity 2021.3 or newer (developed on Unity 6000.5).
- Any render pipeline. The generator builds a plain mesh and does not care what lights it.

The two shaders are **optional**, and Built-in pipeline only. **They are not shipped in this
project**, which is on URP. Without them the cave still generates, edits and collides exactly the
same; new materials are built on whatever shader the active render pipeline lights with. What you
lose is the per-face shade variation the generator bakes into vertex colours, which those shaders
read and the stock ones discard.

The pipeline is asked directly rather than by probing shader names, because under URP or HDRP the
built-in Standard shader is still found and still reports itself supported while rendering magenta.
A cave that arrives magenta is a wrong material, not a broken generator — if you ever see one, the
fix is the shader on `Assets/Cave/Materials/Cave_Rock.mat` and `Cave_Floor.mat`.

The terrain hole shader is likewise only needed when a terrain has been put onto a non-terrain
shader. Unity's own terrain materials clip holes already, so on a default terrain that whole
question does not arise.

## Getting started

`GameObject > 3D Object > Cave Tunnel (Low Poly)`

That creates a cave with a MeshFilter, MeshRenderer, MeshCollider and the generator, and makes the
two materials it needs on first use. Select it and drag the nodes.

## Scene view

| Control | Does |
| --- | --- |
| Orange dots | Click to select a node |
| Move gizmo | Drag the selected node — bends the tunnel |
| Two white cube handles | Pull out for width, up for height |
| Green dots between nodes | Insert a node there |
| Blue outlines | Live cross-section at every node |
| Red outlines | This turn is too tight to build — see below |

Caverns are just a wide node: pull one node's handles out and the swelling eases in and out along
the curve by itself.

## The one rule

A swept tunnel cannot turn tighter than it is wide. Once the turn radius drops below the local
half-width, the inner wall sweeps backwards through itself and the mesh tears. Nothing in the
generator can fix that — the shape being asked for does not exist.

The inspector names any node this applies to, and those sections draw red in the scene view. Two
ways out: spread the turn over more distance, or narrow the cave through the corner. Aim for a
radius of at least twice the half-width.

**Spread Nodes Evenly** resamples the curve at equal spacing without changing its shape, and usually
fixes this on its own — nodes bunched closer together than the tunnel is wide are the usual cause.
**Relax Tight Turns** eases the offending corners directly. Both pin the end nodes.

Inserting is refused where it would pack nodes closer than `Min Node Spacing` × the local
half-width, because clicking `+` repeatedly to smooth a corner is what sharpens it. Set it to 0 to
turn the guard off.

## Terrain

A cave mesh alone is not enterable. Unity Terrain is a heightfield that knows nothing about the
tunnel, so near each mouth the ground passes through the bore and plugs it.

The **Terrain** section of the inspector punches Unity terrain holes wherever the hillside stands
inside the cave, which removes both the visible surface and the collider. Only the mouths end up
punched — deep inside a hill the surface is far above the crown, so it is not inside the cave.

Terrain holes need a material that clips them. The Standard shader does not, so holes would work for
physics and stay invisible; the inspector offers to swap the terrain onto the included
`Cave/Terrain Flat With Holes` shader, copying the look off whatever it was using. It writes a new
material rather than editing the existing one, since a terrain material is usually shared.

Holes live in the TerrainData asset, not the scene, so they survive a scene revert. Re-punch after
moving a mouth — old holes are not withdrawn. **Clear All Holes** fills everything back in.

Terrain holes only ever remove terrain. Scenery meshes crossing the bore still block it —
**Check Bore For Obstructions** lists them.

## Texture mapping

A swept tunnel has no honest UV layout, because U has to run around a ring whose size changes as you
shape the cave.

- **Proportional** (default) — a fixed whole number of tiles around every ring. Rings always agree,
  so shaping never shears and the seam meets. The texture stretches as the passage widens.
- **Arc Length** — constant physical tile size, but shears wherever the width changes.
- **World-space triplanar** — a toggle on the `Cave/Vertex Colored Rock` material. Samples by world
  position and uses no UVs at all, so it cannot distort. Fixed to world space, so the pattern slides
  through the cave if you move it.

The generator bakes a per-face brightness scatter into vertex colours. The included shader folds
that into albedo; the Standard shader discards it, which leaves a flat-shaded cave looking like a
grey pipe.

## Files

    Scripts/   CaveNode, CaveSettings, CaveNoise, CaveMeshBuffer,
               CaveMeshBuilder, CaveVolume, CaveTunnelGenerator
    Editor/    CaveTunnelGeneratorEditor, CaveTerrainHoles

The two optional Built-in-pipeline shaders (`Cave_VertexColor`, `Cave_TerrainHoles`) are not part of
this project. Nothing here needs them.

No dependencies outside UnityEngine and UnityEditor. Materials are generated on first use.

`Save Mesh Asset` bakes the current cave to a .asset if you would rather ship static geometry.
