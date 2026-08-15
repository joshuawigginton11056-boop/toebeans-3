# Race Track Generator

Mario-Kart-style racing circuits for Unity. A banked ribbon is swept along a curve through control
points you drag in the scene view, so the whole track is editable geometry rather than a baked model.

The track is **not attached to anything**. Nodes carry their own height, so a circuit can run along
the ground, climb a mountain, or hang in open sky, and one part of the lap can pass clean over
another. Nothing needs to be underneath it.

## Getting started

`GameObject > 3D Object > Race Track`

That creates a track with a MeshFilter, MeshRenderer, MeshCollider and the generator, and makes the
four materials it needs on first use. What you get is a closed oval with a 177 x 138 m footprint,
14 m wide — a 442 m lap, already drivable. Select it and drag the nodes.

### On sizing

14 m is **eight and a half karts abreast** at this project's kart size of 1.65 m, which is the
eight-up racing the track exists for and close to what a Mario Kart circuit actually runs. Past
about 20 m a track stops reading as a road and starts reading as a runway.

The starting oval is deliberately modest — it fits a 250 m terrain with real margin. **A circuit
gets its length from winding, not from being enormous.** The first version of this default was
240 x 160 m, chosen so the lap would take a Mario-Kart-ish half minute at the kart's 26 m/s top
speed; on this project's 250 m island it covered the entire world. If you want a longer lap, add
nodes and fold the circuit back through itself rather than scaling the oval up — that is what makes
a track interesting as well as long.

## Scene view

| Control | Does |
| --- | --- |
| Orange dots | Click to select a node |
| Move gizmo | Drag the selected node anywhere, at any height |
| White cube handle | Pull out to widen the track |
| Green cone handle | Lift or drop the right-hand edge to bank this node |
| Green dots between nodes | Insert a node there |
| Two blue lines | The real edges of the racing surface, as built |
| Yellow / red ring | The tightest corner on the lap, drawn where it actually is |

The two blue lines are taken off the solved path, not sketched from the nodes — banking and all. If
they turn red, the corner is tighter than the track is wide and the surface has torn.

## The four promises, and how each one is kept

**It never narrows.** *Uniform Width* is on by default, and it does not mean "interpolated smoothly"
— it means every cross-section is handed literally the same number. The width is measured back off
the emitted vertices and reported in the inspector, so the claim is checkable rather than asserted.
Turn it off and per-node widths come back; even then a widening is clamped to the two nodes it runs
between, so it can only ever widen.

**It never pinches.** A swept ribbon keeps its width by construction. The one way it can lose it is
by folding: turn tighter than the section is wide and the inside edge sweeps backwards through
itself. *Check Track* reports how much room the inside edge has left at its worst point — 100% on a
straight, 0% at the exact radius where the outside of the barrier stops advancing, negative once it
has torn.

**Corners are fluid.** Sections are laid down by whichever is most demanding: covering the distance,
resolving the total bend, or resolving the *sharpest part* of the bend. That last one matters — a
span that turns 40 degrees with nearly all of it in one place would otherwise get ten evenly spaced
sections and still step 10 degrees at the sharp bit, leaving a visible flat in the middle of the very
corner the setting exists to smooth.

**It is level to drive on.** The frame is parallel-transported so the ribbon carries no twist nobody
asked for, then rotated towards level by *Keep Level*. At 1 the surface is dead flat side to side
however the track climbs and turns. Below 1 it keeps some of its own twist, which is what a corkscrew
or a full vertical loop is made of.

## The one rule

A swept ribbon cannot turn tighter than it is wide. Once the corner radius drops below the half-width
of the widest part of the section — the outside of the barrier, not the edge of the tarmac — the
inner edge sweeps backwards and the mesh tears. On a default 14 m track that limit is about 9 m.

**The driving limit is much stricter than the geometric one.** A kart at 15 m/s wants 20-25 m of
radius, so *Min Corner Radius* defaults to 25 m and everything tighter is flagged. A corner can be
perfectly buildable and still be unraceable.

Nothing enforces either limit. Corner radius is the author's to choose.

### When the nodes look fine but the corner does not

The circle through three nodes is **not** the radius the curve actually turns at. A curve through
unevenly spaced nodes bends harder between them than the node polygon suggests, so a layout whose
every node looks legal can still have a corner that folds. Everything the inspector reports is
measured on the solved curve, and when the two disagree it says so and points at *Spread Nodes
Evenly*, which is the fix — not moving corners about.

## Buttons

| Button | Does |
| --- | --- |
| Spread Nodes Evenly | Respaces the nodes along the current curve without changing its shape |
| Ease Tight Corners | Opens out corners under the minimum radius. Safe to press repeatedly — it can never return a layout worse than the one it was given |
| Raise / Lower All By, Flatten To | Bulk height, in metres |
| Follow The Ground | Optional. Seats every node on whatever is under it, plus clearance. Nodes with nothing beneath them keep the height you gave them |
| Check Track | Measures the built mesh and prints width, tightest corner, room on the inside edge, gradient, bank and triangle counts |
| Save Mesh Asset | Bakes the current circuit to a `.asset` |

## Materials

Four submeshes, in this order: **0 road, 1 kerb, 2 wall, 3 underside.** Swapping the first material
is how you change what the track is made of.

The kerbs are **flush with the road**, never a lip. This is a kart game and the generated mesh is the
collider, so anything standing proud of the racing surface is a bump you feel through the wheels.
They are a separate submesh purely so they can take a rumble-strip texture.

Road UVs run in real metres by default, so a tile is the same physical size across the track as along
it. Switch *Road Uv Mode* to Normalised for a texture that paints the road itself — lane lines, a
start grid — since that stays registered to the edges however wide the track is. On a closed lap
*Match Seam Tiling* stretches the along-track tiling by under half a tile so a whole number fits the
lap; without it the texture arrives back at the start line out of step and draws a hard line across
the road.

New materials are built on whatever shader the active render pipeline lights with. The pipeline is
asked directly rather than by probing shader names, because under URP the built-in Standard shader is
still found and still reports itself supported while rendering magenta. **A track that arrives
magenta is a wrong material, not a broken generator.**

## Using the path from code

`RaceTrackGenerator.Path` is the solved racing line — position, direction, surface normal, width,
bank and distance, all the way round. `TrackPath.SampleAt(distance)` wraps on a loop, so a start
grid, checkpoints, lap progress and respawn points come off the track itself rather than out of
hand-placed markers.

## Verification

`TrackPath`, `TrackProfile`, `TrackMeshBuilder`, `TrackMeshBuffer`, `TrackLayout` and `TrackMath`
are free of scene objects, asset loading and Unity's native calls, so the whole thing — node list in,
triangle list out — runs outside the Editor. 179 assertions cover constant width across 300 random
layouts, triangle facing, closed-loop seam continuity, banking sign and magnitude, surface fluidity
through a chicane, fold detection, corner easing never regressing, end caps, seam tiling,
determinism, and that the default circuit fits a 250 m terrain with margin.

Keep those files that way. Triangle facing in particular is only ever caught by an explicit
assertion: a wrongly wound surface in Unity does not look wrong, it disappears.
