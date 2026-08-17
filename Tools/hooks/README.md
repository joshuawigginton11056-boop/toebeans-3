# Git guardrails for this project

Unity projects break git's assumptions: scenes are 14 MB of YAML, terrain data is
binary, and asset packs are gigabytes. Three separate incidents here came from that
mismatch, and none of them were careless commits — all three were missing configuration.

Everything below is **per-clone**. None of it travels with the repository, so it has to
be redone on every machine — `Tools/second-machine-setup.md` is the full checklist.

## 1. The Unity merge driver — do this first

`.gitattributes` asks for `merge=unityyamlmerge` on every `.unity`, `.prefab`, `.mat` and
`.asset` file. If no driver by that name is configured, **git does not warn you** — it
silently falls back to its default line-by-line text merge and writes `<<<<<<<` conflict
markers into the middle of a Unity scene. Unity then refuses to parse the scene, and the
map reads as gone.

That is exactly what happened on 2026-08-16 to `Assets/Scenes/LobbyIsland.unity`.

Configure it (adjust the Unity version if the Editor is upgraded):

```bash
UYM="C:/Program Files/Unity/Hub/Editor/6000.5.5f1/Editor/Data/Tools/UnityYAMLMerge.exe"
git config merge.unityyamlmerge.name "Unity SmartMerge (UnityYAMLMerge)"
git config merge.unityyamlmerge.driver "\"$UYM\" merge -p %O %B %A %A"
git config merge.unityyamlmerge.recursive binary
```

Verify it is live:

```bash
git config --get merge.unityyamlmerge.driver
```

Re-run this after upgrading the Unity Editor — the path contains the version number, and a
stale path fails the same silent way as no driver at all.

Tested against the real 2026-08-16 conflict: the default text merge produced 9 conflict
markers and an unopenable scene, the driver produced a clean 95-object scene.

## 2. The pre-commit hook

```bash
sh Tools/hooks/install.sh
```

Refuses any commit whose staged content contains conflict markers. Second line of defence
only — the merge driver above is what actually prevents the problem.

Installed by copying into `.git/hooks/`, deliberately **not** by setting `core.hooksPath`,
because that would stop git reading `.git/hooks` and disable the four Git LFS hooks that
live there. This repo keeps textures, models and audio in LFS.

## 3. Things not to do in GitHub Desktop

- **Stash changes** — the operation that caused the 2026-08-16 incident.
- **Discard changes** on a scene or terrain asset.
- Switching branches with unsaved scene edits open in Unity.
- "Select all" when staging — it will happily offer several GB of store assets.

Committing and pushing from Desktop is fine, and committing often is the cheapest
protection available. Hand anything that says *conflict* to a git-literate pair of hands.

## Related

- `Tools/backup-toebeans.ps1` — hourly mirror + dated snapshots to `E:\Backups`, run by
  the "Toebeans-3 Backup" scheduled task. Git is not a backup; that script is.
- `.gitattributes` — carries a long comment about why `*.asset` must not get `eol=lf`.
  A terrain heightmap was destroyed that way on 2026-08-15.
