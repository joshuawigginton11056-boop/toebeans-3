# Kart style concepts

A shortlist of kart styles to build through the Blender pipeline, one per biome, plus
the reasoning behind each. Read `README.md` first — this document assumes the pipeline,
the origin convention and the material slot contract described there.

**All eight are built.** Each has a script under `models\`, four exported meshes, a
generated palette manifest and an entry in `KartStyle.All` — see the table in `README.md`
for which script is which. This document stays as the record of *why* each looks the way
it does; the build notes that came out of actually making them are in each script's header.

What the build changed about the plan below:

- **Proportion turned out to be available after all, indirectly.** Field marshal fakes the
  tractor read entirely through fender geometry, and it works — an oversized rear arc and a
  deliberately mean front shroud are enough to make two wheels 40 mm apart in radius read
  as very different sizes.
- **The mine cart's lamp had to move.** It is on the roof-bar point, not the roll hoop,
  because that is where `KartLights` hangs a real Light. The gameplay payoff the concept
  wanted only exists at `KartBlueprint`'s own coordinates.
- **Pit rat's odd wheel is per-axle, not per-corner.** A style names one front mesh and one
  rear mesh, so the fronts keep a hubcap and the rears do not.
- **Bone chariot still needs its collision shell.** Nothing in the mesh sticks out past the
  ribs' envelope, so a convex hull fitted round the body in Unity will not have a rib
  through it — but that shell is not built.

## What a style can and cannot change

Every style shares `KartDimensions.Default`. Same wheelbase, same track, same wheel
radii, same seat, same roll hoop height, same steering hub. That is deliberate — the
physics and the wheel anchors come from those numbers, so a style that moved them would
put a visual wheel somewhere the colliding wheel is not.

The consequence for design is worth stating plainly: **proportion is not available as a
design tool.** Distinctiveness has to come from three places instead.

| Lever | Why it carries the weight |
|---|---|
| The tallest element above the seat back | It is what a player sees over the field in a pack. Horns, a leaf, a lamp, an exhaust stack, an antler. |
| Bodywork mass distribution | Where the bulk sits — nose-heavy, wide-shouldered, open-framed — reads at distance when detail does not. |
| Tyre and rim character | Radius is fixed, everything else about a wheel is free. Two of the concepts below are wheel-led rather than body-led. |

A style is stronger when it reinterprets all five material slots — `KartFrame`,
`KartBody`, `KartSeat`, `KartRim`, `KartRubber` — rather than recolouring `KartBody`
alone. Log racer is the clearest case: its `KartRubber` slot is bark, not tyre.

Two pipeline constraints bear directly on these designs:

- **Tread peaks at the nominal wheel radius.** `KartSuspension` holds the hub exactly
  `radius` above the contact point, so chunky snow and mud treads are carved *inward*
  from the radius. They are never stuck on top of it.
- **The mesh is the collider.** Anything a kart can touch has to clear the same bar as
  the rest of the project's geometry: no steps, no bumps, nothing that catches a wheel.
  This is what makes the open-frame concepts expensive.

## The concepts

### Cinder hauler — lava / hell

Cracked basalt shell with fissures that glow. The roll hoop splits into two outward
curving horns and the exhaust becomes twin chimney stacks, which together give it the
tallest and most jagged silhouette in the set. Tyres are heat-cracked slabs: deep
grooves, almost no visible tread block.

`KartBody` volcanic crust, `KartFrame` heat-blued steel, `KartRim` dull iron. Wants an
emissive channel on the body material for the fissures.

### Overgrowth — jungle

A kart the jungle reclaimed. Exposed tube frame in bamboo, woven rattan floor pan and
seat, vines wrapping the hoop, and one enormous single leaf as the rear wing — the leaf
is the entire read from behind. Almost no bodywork; the frame *is* the design.

`KartFrame` bamboo, `KartSeat` rattan. Steering wheel is a bent branch loop.

### Piste basher — snow / winter

Front bumper replaced by an angled steel plow blade, with short sled runners flanking
the nose. The only kart in the lineup that reads as a wedge head-on. Studded chevron
tyres carved inward from the radius.

`KartBody` enamel paint chipped through to bare metal, `KartSeat` quilted.

### Mine cart — cave

Riveted steel tub for a body, wooden slat sides, flanged rail-wheel rims. The tallest
thing on it is a carbide headlamp on the hoop, which can double as a real light source
on a dark map — the only concept here where the silhouette hook also earns gameplay.

`KartRim` flanged iron, `KartRubber` barely visible.

### Field marshal — farm

The tractor read, faked. Wheel radius is fixed, so the proportion comes from a huge
arcing rear fender floating well above the tyre and a narrow shroud over the front
wheels that makes them read small. Vertical exhaust stack with a flapper cap as the tall
element, hay-bale seat, big thin-rim steering wheel with a spinner knob.

### Log racer — woodland

A hollowed log with an axe-hewn cockpit, moss on the shoulders, antler roll hoop. Wheels
are cross-cut log rounds: bark sidewalls, growth rings on the face, carved chevron
tread. Wheel-led — it would still read at a distance where the bodywork is a smudge.

### Bone chariot — hell, alternate

Ribcage bodywork with the seat slung inside the ribs, a skull nose cone, spine running
back to the engine. Its body is negative space; you see the track through it, and
nothing else in the lineup does that, so it never competes for silhouette space.

Hardest of the set to build to the collider rules — ribs are exactly the geometry a
wheel catches on. Needs a hidden simplified collision shell.

### Pit rat — universal / unlock

Mismatched scrap panels, exposed engine with no cover, a jerry can strapped where the
side pod goes, one wheel obviously not matching. Cheapest to build, because wrong is the
aesthetic. Good as a starter or joke kart.

## Build order

**Piste basher or Mine cart first.** Both are mostly boxes and revolved shapes, both
have a single strong silhouette hook, and neither needs organic modelling — so they come
out of `toebeans_blender` cleanly and give the project a second real style next to Buggy
quickly.

Overgrowth and Log racer are the best-looking of the set and the most Blender work:
organic shapes fight the triangle budget and the no-bumps rule at the same time.

Two things that cut the cost:

- Wheels can be shared across styles suited to the same terrain — one snow set, one mud
  set — which takes a style from four meshes to two.
- The steering wheel is its own FBX and the player stares at it constantly in a chase
  cam. It is the cheapest character moment on the whole kart.

Do not tie karts one-to-one to maps. Players want to take the Bone chariot to the farm.

## What has not been checked

These are silhouette designs, worked out in side view. The real test is the
three-quarter render from `preview_kart.py` — the plow blade and the fender arcs in
particular will read very differently there than they do side-on.
