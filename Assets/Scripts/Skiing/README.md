# Ski mechanics

A playable skier for feeling out the mechanics on the real terrain. No skis, no animation, no
track — just the movement, so it can be judged on its own.

## Getting in

Run **Tools ▸ Toebeans ▸ Set Up Skier** (`Ctrl`/`Cmd` + `Shift` + `K`) with the scene open, then
press Play.

The skier drops onto whatever is under the scene view's focus point and spawns pointing straight
down the fall line. Re-running the menu item moves it — **frame a different pitch and run it
again**. That is the whole tuning loop: the mechanics are driven by the actual slope you are
standing on, so which face you test decides the answer.

It borrows the character from `Assets/Prefabs/Player.prefab` if the scale-test rig has already
built one, and falls back to the grey mannequin otherwise. The walking player is disabled rather
than deleted; re-enable it in the Hierarchy to go back to the scale test.

## Controls

| Input | Action |
| --- | --- |
| `A` / `D` | Carve — rotates the skis. The heading stays where you put it |
| `W` | Tuck — cuts drag, so it is the "go faster" input |
| `S` | Brake — snowplough: slows you and plants the edges |
| `Shift` | Set the edge — sharper turn, more grip, more speed scrubbed |
| `Space` | Jump — hold to charge, release to launch |
| `Mouse` | Free look. Let go and it recentres behind the run |
| `R` | Respawn at the drop point |
| `H` | Show / hide the readout |
| `Esc` | Release the cursor (click to re-capture) |

## How it works

The mountain drives the speed. Every frame the controller finds the surface under the skis,
resolves gravity along it, and hands the result to the edges:

1. **Gravity along the slope.** Pointed down the fall line you get all of it. Traversing, most of
   it lands on the across-the-skis axis instead.
2. **The edges bite.** Sideways drift decays exponentially (`edgeGrip`), and a fraction of what is
   scrubbed is redirected into forward speed (`carveRedirect`) — that is the carve, trading drift
   for pace rather than just losing it.
3. **Drag.** Constant snow friction plus a quadratic term that tucking cuts, which is what makes
   `W` worth several m/s of terminal speed on a steep pitch.

Everything else falls out of those three:

- **Turning is braking** — point across the fall line and the speed goes onto an axis the edges
  eat. No skid-scrub curve to author.
- **Landings slide before they bite** — you touch down with the skis off your travel line, and the
  same edge decay grips them back over a few tenths of a second. No landing timer.
- **Steeps ski faster** — because they are steeper. No per-segment steepness number.
- **Traverses side-slip** — gravity keeps feeding the across axis, so a hard traverse settles into
  a slow honest slip instead of sticking to the hill, and steeper faces slip faster.

Two rules are kept as rules, because they are decisions rather than physics: steering authority
scales with speed but floors above zero (otherwise a sideways stop is a softlock), and the jump is
hold-to-charge with a lockout on touchdown (otherwise it pogos).

### What was deliberately dropped

The old TypeScript sim modelled the run as `distance` down a rail plus a `lateral` offset, with a
hand-authored grade factor per segment. Its stance flips, heading saturation clamps and lane
half-width pinches all existed to keep that rail honest. On real terrain they are unnecessary, so
none of them are here. Riding switch, spinning and turning uphill are all just consequences of
where you point.

## Tuning

Everything is on the `SkiController` component, in the order you will want to reach for it:

| Field | What it changes |
| --- | --- |
| `gravity` | Overall pace. Running hotter than -9.81 makes the mountain feel steeper without re-sculpting it |
| `edgeGrip` | The whole ski feel. High carves on rails, low slides around like a sled. Also sets how long a sideways landing slips |
| `carveRedirect` | How rewarding a good turn is. 0 makes every turn pure braking |
| `glideDrag` / `tuckDrag` | Terminal speed standing up vs tucked — how much `W` is worth |
| `turnRate` | How quickly the skis come round |
| `groundStick` | Raise to stop popping off every bump, lower to get air off lips |
| `maxSpeed` | Hard ceiling, m/s. 26 is about 94 km/h |

Camera feel lives on `SkiCameraRig`. `baseFov`/`maxFov` is the single biggest speed cue there is —
if the run feels slow, widen the FOV before touching the physics.

The readout (`H`) reports speed, top speed, the pitch you are on and how many degrees sideways you
are, which is what makes "that felt fast" actionable.

## Files

| File | Role |
| --- | --- |
| `Runtime/SkiController.cs` | The sim: slope, edges, drag, steering, jumping, and the body pose |
| `Runtime/SkiCameraRig.cs` | Chase camera — trails travel, widens FOV with speed, rolls into turns |
| `Runtime/SkiHud.cs` | Speed / pitch / skid readout for tuning passes |
| `Editor/SkiTestSetup.cs` | The menu item: spawn, fall-line facing, camera wiring |

## Not here yet

Skis and a ski pose, crashes, chasms, checkpoints, a finish line, audio. All of that hangs off a
feel that works first.
