# Scale test rig

A playable character for walking the map at human scale, so props, terrain and buildings can be
judged against something with a believable size.

## Getting in

Run **Tools ▸ Toebeans ▸ Set Up Playable Character** (`Ctrl`/`Cmd` + `Shift` + `P`) with the scene you
want to test open, then press Play.

The character spawns wherever the scene view is currently focused, dropped onto the first collider
below that point. Re-running the menu item moves it — reframe the scene view and run it again to test
a different corner of the map.

If no character model has been imported yet, the setup builds a grey stand-in mannequin at correct
1.8 m proportions so the map is still walkable. See `Assets/Characters/README.md` for how to add the
Quaternius pack and swap it in.

## Controls

| Input | Action |
| --- | --- |
| `WASD` / left stick | Move (camera relative) |
| Mouse / right stick | Look |
| `Shift` | Sprint |
| `Space` | Jump |
| `Ctrl` | Crouch |
| `V` | Toggle first / third person |
| `H` | Toggle the scale readout |
| `Esc` | Release the cursor (click to re-capture) |

Bindings come from `Assets/InputSystem_Actions.inputactions` (the `Player` map), so rebinding there
carries over. If that asset is missing the controller falls back to hardcoded keyboard/mouse/gamepad
input rather than going dead.

## Reading scale

The on-screen readout reports the character's height and eye height, its current speed in m/s and
km/h, and — for whatever is under the crosshair — the distance to it and its bounding size in metres.
Pointing at a tree and reading "8.40 m tall" is the fastest way to catch a prop that was authored at
the wrong scale.

Speeds are real-world values, which makes them a scale check in their own right: a walk is 2 m/s and
a sprint 5.5 m/s. If crossing a courtyard at a sprint feels absurdly quick, the courtyard is too
small; if walking anywhere feels like a trek, the map is too big.

First person (`V`) is worth using for interiors and doorways — third person quietly flatters
headroom. The character keeps casting a shadow in first person, which is another good size cue.

## Checking scale without pressing Play

**Tools ▸ Toebeans ▸ Add Scale Reference Marker** drops a 1.8 m human silhouette with a 10 m ruler
into the scene. It is gizmo-only, so it draws in the scene view and never renders in game. Park one
next to a building while modelling.

Selecting the player also draws its collision capsule as a gizmo at authored size.

## Files

| File | Role |
| --- | --- |
| `Runtime/ThirdPersonController.cs` | Movement, jumping, crouching, animator parameters |
| `Runtime/PlayerCameraRig.cs` | Orbit follow camera with collision and first person toggle |
| `Runtime/PlayerInputReader.cs` | Input System wrapper with a raw-device fallback |
| `Runtime/ScaleHud.cs` | On-screen readout and the crosshair measuring ray |
| `Runtime/ScaleReferenceMarker.cs` | Scene view human-sized ruler gizmo |
| `Editor/PlayableCharacterSetup.cs` | The menu items; model discovery, prefab and scene wiring |
| `Editor/LocomotionControllerBuilder.cs` | Generates the animator controller from a pack's clips |
| `Editor/ProxyMannequin.cs` | The stand-in figure used when no character is imported |
| `Editor/CharacterModelPostprocessor.cs` | Import defaults for anything under `Assets/Characters/` |

## Tuning

Sizes and speeds live on the `ThirdPersonController` component, so they can be tweaked on the prefab
without touching code. The two that matter most:

- `standingHeight` — the reference height, 1.8 m. Changing it does **not** rescale the model; re-run
  the setup menu item for that, after changing `TargetHeight` in `PlayableCharacterSetup.cs`.
- `modelYawOffset` — set to `180` if the imported character runs backwards. Some packs export facing
  −Z, and this is the one-field fix.
