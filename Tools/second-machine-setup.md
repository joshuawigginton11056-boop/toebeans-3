# Working on a second machine

Everything needed to take this project from a fresh clone to a working Unity + Blender
setup, and the routine that keeps two machines from diverging.

The important thing to understand up front: **a clone does not carry the configuration
that protects this repo.** The merge driver and the pre-commit hook both live in
`.git/`, which is not versioned. A machine that skips steps 3 and 4 below will look
completely fine right up until the first merge, and then it will silently write conflict
markers into a scene. That is not hypothetical — see `Tools/hooks/README.md`.

## Prerequisites

| Tool | Version | Notes |
|---|---|---|
| Git | 2.5x | |
| Git LFS | 3.x | `git lfs install` once per machine, before cloning |
| Unity | 6000.5.5f1 | Version matters — the merge driver path contains it |
| Blender | 5.2+ | Only needed to rebuild models, not to run the game |

Set `BLENDER_PATH` to `blender.exe`. Both `Tools/blender/build-models.ps1` and the
Blender MCP server read it, so setting it once aims both at the same install.

## 1. Install Git LFS first

```bash
git lfs install
```

Do this **before** cloning. Clone first and the LFS-tracked files (`.fbx`, `.png`,
`.psd`, `.tga`, `.wav`, `.mp3`) arrive as one-line pointer text files instead of real
assets, and Unity imports a project full of broken models.

If that already happened: `git lfs pull` fixes it in place.

## 2. Clone

```bash
git clone https://github.com/joshuawigginton11056-boop/toebeans-3.git
```

## 3. Configure the Unity merge driver

**Do this before the first pull that could merge anything.** `.gitattributes` asks for
`merge=unityyamlmerge` on every `.unity`, `.prefab`, `.mat` and `.asset`. If no driver by
that name exists, git does not warn — it falls back to a line-by-line text merge and
writes `<<<<<<<` into a 14 MB scene, which Unity then cannot open.

```bash
UYM="C:/Program Files/Unity/Hub/Editor/6000.5.5f1/Editor/Data/Tools/UnityYAMLMerge.exe"
git config merge.unityyamlmerge.name "Unity SmartMerge (UnityYAMLMerge)"
git config merge.unityyamlmerge.driver "\"$UYM\" merge -p %O %B %A %A"
git config merge.unityyamlmerge.recursive binary
```

Verify, and check the path actually exists on this machine:

```bash
git config --get merge.unityyamlmerge.driver
```

The laptop's Unity may be installed somewhere other than `C:\Program Files\Unity\Hub`.
Adjust the path to match. A wrong path fails exactly as silently as no driver at all.

## 4. Install the pre-commit hook

```bash
sh Tools/hooks/install.sh
```

Second line of defence: refuses to commit staged content containing conflict markers.

## 5. Store-bought asset packages

Large Unity Asset Store packages are **not** in the repository. Re-download them on each
machine from `Window > Package Manager > My Assets`, signed in with the same Unity
account. Asset Store packages ship their own `.meta` files, so the GUIDs match across
machines and scene references survive.

Currently outside the repo:

| Package | Approx size |
|---|---|
| BOKI — Low Poly Nature | 700 MB |
| POLY Mountain | 380 MB |

Once a package's assets are actually placed in a scene, the specific files that scene
references have to be committed, or the scene breaks on the other machine. Committing an
unused package is wasted bandwidth; committing a used one is mandatory.

## 6. Verify the setup

```bash
git lfs pull
git config --get merge.unityyamlmerge.driver
ls .git/hooks/pre-commit
```

Then open `Assets/Scenes/LobbyIsland.unity` in Unity. If it opens with its objects
intact, LFS and the clone are healthy.

To confirm the Blender side:

```powershell
.\Tools\blender\build-models.ps1 -Model kart_buggy
```

## The swap routine

Divergence between two machines is the thing that actually costs time here, and Unity
projects punish it harder than most — a scene is not mergeable by hand.

**Before leaving a machine:**

```bash
git add -A
git commit -m "wip: <what you were doing>"
git push
```

Commit even when the work is unfinished. A `wip:` commit that gets amended later is far
cheaper than a merge between two machines that both edited `LobbyIsland.unity`.

**On arriving at the other machine:**

```bash
git pull
```

Then let Unity finish importing before touching anything. A pull that lands new `.fbx`
or `.meta` files while Unity is mid-import can leave the Library cache inconsistent.

**Never** have uncommitted scene edits on two machines at once. There is no good merge
for that, only a choice about which machine's work to throw away.

## What is not in git

`Tools/backup-toebeans.ps1` mirrors the project hourly to `E:\Backups` with dated
snapshots, driven by the "Toebeans-3 Backup" scheduled task. That is machine-local and
does not follow a clone — the laptop has no equivalent unless one is set up there too.

Git is version control, not backup. The distinction matters most on the machine that has
the only copy of something.
