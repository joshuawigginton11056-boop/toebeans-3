# Volcano Generator

A procedural low-poly volcano for Unity: a faceted cone with a crater, gullies raked down the
flanks, notches for lava to pour out of, and a passage cut through the base for a track to run
through. All of it is one height field, rebuilt from a seed, so it is editable geometry rather than
a baked model.

## Requirements

- Unity 2021.3 or newer (developed on Unity 6000.5).
- Any render pipeline. The generator builds a plain mesh; the materials are written on first use
  against whatever pipeline is active, which is why nothing is shipped as an asset.

## Getting started

`GameObject > 3D Object > Volcano (Low Poly)`

That makes the object with a MeshFilter, MeshRenderer, MeshCollider and the generator, and creates
the four materials it needs. Press **Snap To Ground**, then **Build Everything** on the inspector to
hang the lava, the rivers, the smoke and the passage lights off it.

**The object's origin is the middle of the foot of the cone, standing on the ground.** Not the
summit, not the centre of the bounding box. That is what makes Snap To Ground mean something and
what puts the passage floor at the same level as the ground around it.

## Submeshes

`0 rock`, `1 ash`, `2 ember`, `3 molten`. Four materials on the Mesh Renderer in that order. Only
slot 3 wants an emissive material — it is the fissures, the spillway notches and the seam glowing
along the wall of the passage.

## The crater and the lava

Nothing here draws lava. `Lava Depth Below Rim` says where the lava *stands*, and everything else is
derived from it:

- **Add Crater Lava** puts a Lava Pond at that level, sized to the pool the crater actually holds at
  that height (solved off the crater profile, not guessed), with its vent up so there is visibly
  something feeding the mountain.
- **Spillways** are notches cut through the rim to `Notch Drop` below the crest, each carrying a
  channel down the flank. **`Notch Drop` has to be greater than `Lava Depth Below Rim`** or the
  notch is above the lava and nothing ever comes out of it. The inspector says so if it is not.
- **Add Spillway Rivers** drops a Lava Flow into each channel, routed from the same maths that cut
  it rather than released at the top and left to find its own way down.

The channel is real geometry — a downhill solve would probably follow it — but "probably" is not
something to build a set piece on.

## Gullies scour, channels do not

Gullies and surface roughness are suppressed inside a spillway channel. That is how a lava channel
really looks, and it is also load-bearing: roughness at the default amplitude is easily steep enough
to put a lip across the channel near the foot, where the cone itself has flattened off, and a lava
flow ponds up and stops at a lip. With the channel scoured, the only things shaping its floor are
the cone and the channel's own taper, and it descends all the way.

## The passage

Three modes:

| Mode | Gives you |
| --- | --- |
| None | Solid mountain |
| Portals Only | The two mouths, nothing else — for running the Cave Tunnel generator through |
| Bore | The mouths, plus rock walls, a flat drivable floor and an apron at each end |

The hole is the intersection of one half-space per face of the arch, and the mountain's surface is
clipped against those planes, so the cut edge follows the arch outline rather than stepping around
whole triangles.

Two things about it are deliberate and worth not undoing:

- **The hole is cut slightly smaller than the tunnel that fills it** (`Mouth Overlap`). The two
  surfaces are solved separately and meet at the mouth; a small overlap is what guarantees there is
  no hairline of daylight around the arch. Raise it if you ever see one.
- **The floor plane of the cut is not inset**, unlike every other face. Insetting it lifts it, and
  any hillside crossing floor level inside the passage then survives as a slab lying on the road —
  measured at exactly the inset, which at kart speed is a ramp.

The floor is never roughened and never varies: this mesh is the collider and it is a road. Walls are
only ever pushed outwards, so the passage is at least as wide as it says it is whatever the
roughness is set to.

**Snap the passage clear of the ground under it.** Unity Terrain is a heightfield that knows nothing
about the tunnel and carries on straight underneath, so wherever it rises above the floor it comes
up through the road. Two ways out: raise `Bore Floor Height` above the highest ground in the
corridor and let the aprons ramp down to meet it outside, or punch Unity terrain holes along the
bore. This project uses the first — it is self-contained and does not write to the shared
TerrainData.

`Add Passage Lights` puts warm point lights down it. The seam in the wall is emissive, and emission
does not light anything, so without them the passage is a black hole in the middle of the map.

## Smoke and mist

Both use **mesh particles, not billboards**. A soft cloud texture next to a mountain made of flat
triangles reads as a photograph pasted onto a model; a lumpy faceted solid is the same silhouette
language as everything else, and it tumbles properly because it is genuinely three dimensional.

`LavaMist` emits from the **triangles of a lava mesh, restricted to its molten submesh**, so the fog
sits on the glowing parts and follows a river wherever it was routed. Move the lava, retune it,
reroute it — the mist comes with it and there is nothing to keep in sync. Lava Pond and Lava Flow
put molten in slot 2; the Volcano puts it in slot 3.

### Small and many, not big and few

**Width and Start Size are measured across a puff, in metres.** The puff mesh is a unit-*radius*
blob, so the particle size is its radius and every number here is halved on the way in. Getting that
wrong is what makes fog look like fog or like cellophane: the pond's mist was set to 12.5 m and
arriving 32 m across, dying at 75 m on a pool barely wider than that, so two wisps covered the whole
thing and there was no cloud shape left in it at any distance.

The instinct is to reach for fewer, bigger puffs because they are cheaper. They are, and they do not
read. A puff as wide as the thing it is rising off has no silhouette of its own — you see a
translucent sheet lying across the view, and it does not get better with more of them, only thicker.

So: keep a wisp to well under a third of whatever it comes off, and carry the density on the rate
instead. **The rate has to go up by roughly the square of however much the size came down** — what
you see through is the puff area a sightline crosses, not the number of puffs — so halving the width
takes four times as many to look equally thick. At those sizes drop `Puff Detail` back to 0: the
80-face blob was worth paying for when a wisp was 32 m across and its outline was most of what you
saw, and at a third of that the facets are finer than the softness of the material.

On LobbyIsland the mist sits at 5–7 m wisps dying under 12 m, and the columns at 8–12 m puffs. That
is about 7,900 particles across all ten emitters at 20 faces each — 157k triangles worst case. Rate
is the dial: it trades directly against how thick the fog looks and costs the count linearly, and
per-puff opacity is the cheaper half of the same knob, which is why the gradients here sit a little
stronger than they did when the puffs were three times the size.

Particle systems do not run in edit mode. `ParticleSystem.Simulate` steps them by hand if you want
to see them without entering play mode.

## Keeping fog off the road

Fog coming off a lava pool does not know there is a bridge over it. It rises through the deck, and
the driver spends the crossing inside a cloud — on the one part of the map with a drop down either
side. `MistShelter` is what tells it. Put one on the bridge (`GameObject > Effects > Keep Fog Under
This`) and the fog underneath is held against the soffit and pushed out to the nearest edge, so it
spills over the sides of the span instead of welling up through the road.

The footprint is baked from **the triangles of the deck submesh**, the same trick the mist uses for
where it is born, so one component follows a curved deck of changing width and height and rebakes
itself when the bridge is rebuilt. It is not bridge-specific and does not know what a bridge is: the
same component works on a tunnel mouth, a viaduct or a stretch of track.

Three things happen to a wisp that would come up through the deck, and the order matters:

1. **It levels off.** A lump tumbling end over end needs its widest measurement of headroom; a flat
   one needs its thinnest. This is what lets the fog stay wide instead of being shrunk to nothing.
2. **It shrinks**, but only as far as the gap makes it and only so fast, so nothing pops.
3. **Its position is clamped**, which is the part that actually guarantees nothing is ever drawn
   above the road while the other two catch up.

`Clearance` is measured down from the *top* of the deck, so it has to be more than the thickness of
the slab. `Margin` and `Release` shape the billow coming off the sides: the lid keeps reaching past
the edge and climbs as it goes, which is what lets a wisp out gradually rather than snapping back
to full size the moment it clears the deck.

A plume that is **already above** a deck is left alone. Blocking it would cut a notch out of a smoke
column that happens to rise beside a bridge; only the puffs that would pass *through* the deck are
caught.

### Three ways a lid leaks, all of them found the same way

Every one of these was invisible in the numbers the shelter reports about itself and obvious the
moment the test stopped asking "is the fog under my lid?" and started asking "**is the fog above the
road?**" — raycasting each puff's top onto the bridge's own collider and reading the material slot
it landed on. Anything measuring a system against its own model will agree with itself.

1. **Take the lowest deck corner in a cell, not the highest.** A cell covers a few metres of a deck
   that is climbing and banking at once, so the road inside one cell spans close to a metre, and the
   triangle splat drags a value a cell further again. Keeping the highest corner puts the lid over
   the road somewhere in every cell. This is what was still showing on the roundabout, 0.70 m proud.
2. **Cover the whole top of the crossing, not the driving lane.** The footprint was baked from the
   deck slot alone, so the verge and parapet strip outside it had no lid — and fog let up through
   *that* comes out at the driver's elbow. Hence `Deck Slots`: Rock Bridge is deck, verge, parapet,
   rock, so 3 takes everything you can stand on and leaves the legs out, which they have to be or
   the lid lands on the lava they are standing in.
3. **Release a puff against the road, not against the lid.** The lid sits a clearance below the deck
   and climbs away from the edges, so a puff that wandered in from the side is routinely above the
   lid while still buried in the deck. The "already clear, let it rise" test has to use the surface
   itself or it hands those straight back.

Measured on LobbyIsland after all three: **120 s simulated, sampled 120 times, both crossings, all
nine emitters, nothing came up through either.** Before the shelter existed the pond bridge alone
had 22 wisps through its deck at once, the worst 59 m above it.

## Colour on a night map

Albedo wants to be lighter than volcanic rock really is, because under a night rig it gets
multiplied by a dim light and physically dark basalt renders black, taking the facets with it. Only
a little lighter, though — twice the ground's own values reads as a snowy mountain dropped into a
lava field.

Emission wants red just over 1 and the other channels well under. Two channels over 1 both clip to
full, and the glow comes out yellow however orange the ramp under it is. `VLC_River_Lava` exists for
exactly that reason: the Lava Flow package's shipped molten colour has green at 1.5, which barely
shows on gentle terrain where most of the surface has crusted over, and turns a cascade gold.

## Files

    Scripts/   VolcanoNoise, VolcanoSettings, VolcanoMeshBuffer, VolcanoShape,
               VolcanoMeshBuilder, VolcanoGenerator, LowPolyPuff, VolcanoSmoke, LavaMist,
               MistShelter
    Editor/    VolcanoGeneratorEditor  (inspector, menu item, materials, dressing)
               MistShelterEditor       (inspector, menu item)

`VolcanoShape` and `VolcanoMeshBuilder` are pure maths — no scene objects, no asset loading, no
global state, and none of Unity's native calls — so they compile and run outside the Editor and can
be asserted against.

`Save Mesh Asset` bakes the current mountain to a .asset if you would rather ship static geometry.
