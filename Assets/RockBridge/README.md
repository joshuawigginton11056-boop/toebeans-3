# Rock Bridge

A drivable rock crossing that carries the track over a lava pool, a river or a chasm, on legs that
find their own length.

`GameObject > 3D Object > Rock Bridge Across Selection` — select the pool first and it measures the
crossing itself. `GameObject > 3D Object > Rock Bridge` makes a bare one to place by hand.

Submeshes, in order: **0 deck, 1 verge, 2 parapet, 3 rock**. The rock slot is the legs, the
underside and the landing fill together, because it is all the same stone.

## The one idea

**Height is a profile, not a set of node heights.** This is the only structural difference from
`RaceTrack`, which this package otherwise mirrors file for file.

The deck holds one level across the span and eases down onto the real ground at both ends. So
**Deck Height** is a single slider: raise it and the whole span lifts, every leg grows downwards to
meet the ground that is now further away, and *the landings do not move*. Nothing else has to be
touched, and nothing else changes.

The level is measured above the highest thing the bridge actually crosses — the lava, not the lake
bed under it. The legs foot on the bed, so they rise *out* of the lava rather than standing on it.
That is what `GroundSample` keeps `Surface` and `Floor` apart for; using one number for both puts
either the deck through the lava or the legs on top of it.

## What to set, in order

1. **Deck Height** — clearance over what you are crossing.
2. **Approach Length** — how far the ramps run. See below; this is the one that bites.
3. **Deck Width** — 16 m is nearly ten karts abreast. A bridge is a pinch point and a twelve-kart
   field arrives at one all at once, so it is set wider than a plain circuit's 14 m.
4. **Pier Spacing**, **Pier Batter** — how the legs read. Batter is per metre of leg height, so a
   tall leg is visibly fatter at the foot than a short one without anything being set twice.
5. **Landing Fill Depth** — where the bank stops and the viaduct starts. It is bounded by a *depth*,
   not a distance, so on a bridge that never flies higher than that depth the boundary never
   triggers and the two landings meet in the middle as a pair of walls running the whole deck. That
   reads as a trough, not a bridge. Keep it near **Shortest Leg Worth Building** so the fill hands
   over to the legs, or turn the fill off and let the legs carry everything. The inspector reports
   how far it actually ran and warns past half the crossing.
6. **Landing Sink** — leave it at 0. The deck lands on the solid ground at both ends, so the join
   is already flush; measured on LobbyIsland's bridge the ground meets the deck to within 21 mm.

There is deliberately **no "reshape the terrain to fit the bridge" button**. One was written and
removed, and the reason is worth knowing before anyone adds it back: the landing height is read off
the ground, so anything that moves the ground moves the landing, which asks for the ground to move
again. Three different targets were tried — a hair under the deck, flush with it, and back at the
original floor — and all three grew their own footprint on every press, one of them building a 12 m
mesa with 81-degree sides under an approach ramp. It is not a tuning problem; it is a feedback loop,
and the fix is that the deck already lands where the ground is.

## Approach Length is not about the gradient

A ramp can be gentle in degrees and still throw the whole field into the air, because what a
suspension feels at the crest is `v² / R` — how tightly the ramp flattens out, not how steep it was
on the way up. The inspector reports that as **Ramp crest**: a vertical radius and the load in g at
**Crossing Speed**.

Past about 1 g the wheels leave the deck, with a parapet and a drop on either side. Under about
0.4 g it is unnoticeable. The relationship is `R ≈ L² / (6H)` for a climb `H` over an approach `L`,
so **doubling the approach quarters the load** — it is much the stronger lever than lowering the
deck. Measured on a 12 m climb:

| Approach | Steepest | Crest radius | g at 22 m/s |
|---------:|---------:|-------------:|------------:|
|     50 m | 20.1°    |         39 m |        1.08 |
|     70 m | 14.7°    |         73 m |        0.58 |
|     90 m | 11.5°    |        118 m |        0.36 |
|    120 m |  8.7°    |        206 m |        0.21 |

90 m is the default for that reason.

## Things that are the way they are on purpose

- **The deck welds and smooths; the rock facets.** A faceted driving surface is not a style choice
  at racing speed — the mesh is the collider, so a facet is a bump. The legs and the landing fill
  use the flat-shaded look the rest of the map is built in, and nothing drives on them.
- **Nothing rock ever lands exactly on another surface.** Legs push up into the slab by
  `Pier Top Embed` and down into the ground by `Footing Depth`. Two surfaces in the same plane is
  what flickers as the camera moves, and no amount of tuning elsewhere fixes it.
- **A leg's cap is cut parallel to the deck, not level**, so it stays buried inside the slab at both
  edges through a banked corner instead of poking out of the low side.
- **A leg's foot follows the ground under each of its own corners**, not the height at its centre —
  otherwise a wide leg on a slope floats on one side and buries itself on the other.
- **End caps are a triangulation of the section's own outline.** The obvious shortcut — a strip from
  the soffit up to deck level — tessellates the shared edge differently from the sweep, and a
  T-junction is the classic hairline crack: watertight on paper, visible in the game at some angles
  and not others.
- **The bridge never reads its own colliders.** One that could would measure the deck it just built
  as the ground, hold Deck Height above *that*, and climb by that much again on every rebuild.
- **Uniform Width means literally the same number at every cross-section**, not "interpolated
  smoothly", which is what makes "the deck never narrows" a guarantee rather than a tolerance.

## Warnings worth believing

Everything user-facing is measured on the solved curve and on the mesh that was emitted, never on
the node polygon. The two genuinely disagree — a curve through unevenly spaced nodes bends harder
between them — and that gap is where a fold hides. **Spread Nodes Evenly** is the fix for that
specific disagreement, and the inspector says so when it is the cause.

If what you are crossing has no collider — a lava river, for instance — the probe cannot see it and
the deck will happily fly through it. Switch on **Use Fixed Datum** and type the surface height in.

## Verified

57 headless assertions in the harness described by the project's `unity-headless-csharp-verification`
note, run against a synthetic version of LobbyIsland's pond: deck smoothness (worst bump 0.04 m at
3 m section spacing), constant width across 120 random configurations (worst drift 0.00001 m),
closed-manifold edge test across 24 configurations (no open or over-shared edges), triangle facing,
leg behaviour under changing deck height, landing flushness at every height, determinism, and 300
random configurations without a throw or a bad number.
