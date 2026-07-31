# Characters

Drop character models in here. Anything imported under `Assets/Characters/` is automatically set up
as a Humanoid rig with looping locomotion clips by `CharacterModelPostprocessor`.

## Adding the Quaternius Ultimate Animated Character Pack

The pack is CC0 (public domain — no attribution required, commercial use allowed) but it has to be
downloaded by hand; it is not redistributable through this repository and the build environment
cannot reach quaternius.com.

1. Download it from <https://quaternius.com/packs/ultimatedanimatedcharacter.html>.
2. Unzip it and copy the **FBX** folder into `Assets/Characters/Quaternius/`.
   The `.blend` and `.obj` copies are not needed — `.obj` carries no rig at all, and Unity only reads
   `.blend` files if Blender is installed on the machine doing the import.
3. Back in Unity, wait for the import to finish, then run
   **Tools ▸ Toebeans ▸ Set Up Playable Character** (`Ctrl`/`Cmd` + `Shift` + `P`).

That menu item picks the best character it can find, generates
`Assets/Characters/Generated/PlayerLocomotion.controller` from the pack's idle/walk/run/jump clips,
rebuilds `Assets/Prefabs/Player.prefab`, and drops it into the open scene with the camera attached.

To use a specific character instead of the auto-picked one, select its `.fbx` in the Project window
and run **Tools ▸ Toebeans ▸ Set Up Playable Character From Selection**.

## Scale

Every character is rescaled on import into the prefab so it stands exactly **1.80 m** tall, measured
from its renderer bounds. That number is the reference the whole map should be judged against — it is
set by `TargetHeight` in `Assets/Scripts/ScaleTest/Editor/PlayableCharacterSetup.cs`.

Quaternius characters are stylised and slightly chunky, so a 1.8 m one looks a little stockier than a
real person. That is expected; the height is what matters for judging doorways, steps and props.

## Generated/

`Assets/Characters/Generated/` holds assets this tooling creates (the animator controller and the
stand-in mannequin's materials). It is safe to delete — the menu item regenerates it.
