#!/bin/sh
#
# setup-machine.sh - configure a fresh clone of this project on a new machine.
#
# Run once per clone, from Git Bash, anywhere inside the repo:
#
#   sh Tools/setup-machine.sh
#
# Everything it does is per-clone configuration that does NOT travel with the
# repository, because it lives in .git/ - the unityyamlmerge driver and the
# pre-commit hook. A machine that skips this looks completely healthy right up
# until its first merge, and then it silently writes conflict markers into a
# 14 MB scene. See Tools/hooks/README.md for the incident that motivated it.
#
# Safe to re-run. Re-run it after upgrading the Unity Editor, because the merge
# driver path contains the version number and a stale path fails exactly as
# silently as no driver at all.
#
# If Unity is installed somewhere this cannot find, pass the path:
#
#   sh Tools/setup-machine.sh "/d/Unity/Hub/Editor/6000.5.5f1/Editor/Data/Tools/UnityYAMLMerge.exe"

set -u

fail=0
warn=0

say()  { printf '%s\n' "$*"; }
ok()   { printf '  OK    %s\n' "$*"; }
bad()  { printf '  FAIL  %s\n' "$*"; fail=$((fail + 1)); }
note() { printf '  WARN  %s\n' "$*"; warn=$((warn + 1)); }

root=$(git rev-parse --show-toplevel 2>/dev/null) || {
	say "Not inside a git repository."
	say "cd into the toebeans-3 folder first, then re-run."
	exit 1
}
cd "$root" || exit 1

say ""
say "Setting up $(basename "$root") on $(hostname)"
say ""

# ---------------------------------------------------------------- Unity merge driver

say "1. Unity merge driver"

want=$(sed -n 's/^m_EditorVersion: *//p' ProjectSettings/ProjectVersion.txt 2>/dev/null | tr -d '\r')
[ -n "$want" ] || want="6000.5.5f1"

uym=${1:-}

if [ -z "$uym" ]; then
	# The project's own Unity version first, then any other version installed.
	for candidate in \
		"/c/Program Files/Unity/Hub/Editor/$want/Editor/Data/Tools/UnityYAMLMerge.exe" \
		"/d/Program Files/Unity/Hub/Editor/$want/Editor/Data/Tools/UnityYAMLMerge.exe" \
		"/c/Program Files/Unity/Hub/Editor"/*/Editor/Data/Tools/UnityYAMLMerge.exe \
		"/d/Program Files/Unity/Hub/Editor"/*/Editor/Data/Tools/UnityYAMLMerge.exe \
		"/c/Program Files/Unity/Editor/Data/Tools/UnityYAMLMerge.exe" \
		"/c/Program Files (x86)/Unity/Editor/Data/Tools/UnityYAMLMerge.exe"
	do
		if [ -f "$candidate" ]; then uym=$candidate; break; fi
	done
fi

if [ -z "$uym" ] || [ ! -f "$uym" ]; then
	bad "UnityYAMLMerge.exe not found."
	say ""
	say "        This project pins Unity $want. Install that version through"
	say "        Unity Hub, or if it is installed somewhere unusual, find it:"
	say ""
	say "          find /c /d -name UnityYAMLMerge.exe 2>/dev/null"
	say ""
	say "        then re-run this script with the path as an argument."
else
	# Git wants a Windows-style path here, not a Git Bash one.
	win=$(printf '%s' "$uym" | sed -E 's#^/([a-zA-Z])/#\U\1:/#')

	git config merge.unityyamlmerge.name "Unity SmartMerge (UnityYAMLMerge)"
	git config merge.unityyamlmerge.driver "\"$win\" merge -p %O %B %A %A"
	git config merge.unityyamlmerge.recursive binary

	if [ -n "$(git config --get merge.unityyamlmerge.driver)" ]; then
		ok "configured: $win"
		case "$win" in
			*"$want"*) : ;;
			*) note "that is not Unity $want, which this project pins." ;;
		esac
	else
		bad "git config did not take."
	fi
fi

# ---------------------------------------------------------------- pre-commit hook

say ""
say "2. Pre-commit hook"

if [ -f Tools/hooks/pre-commit ]; then
	if sh Tools/hooks/install.sh >/dev/null 2>&1 && [ -f .git/hooks/pre-commit ]; then
		ok "installed to .git/hooks/pre-commit"
	else
		bad "could not install - try: sh Tools/hooks/install.sh"
	fi
else
	bad "Tools/hooks/pre-commit is missing from the clone."
fi

# ---------------------------------------------------------------- Git LFS

say ""
say "3. Git LFS"

if ! command -v git-lfs >/dev/null 2>&1 && ! git lfs version >/dev/null 2>&1; then
	bad "git-lfs is not installed. Get Git for Windows from https://gitforwindows.org"
else
	git lfs install --local >/dev/null 2>&1
	say "  ...  fetching LFS content, this can take a minute"
	if git lfs pull >/dev/null 2>&1; then
		ok "git lfs pull completed"
	else
		note "git lfs pull reported a problem - check your network and GitHub sign-in"
	fi

	# The real test: a pointer stub is ~130 bytes, a genuine FBX is far larger.
	probe=Assets/GeneratedModels/KartBuggy_Body.fbx
	if [ -f "$probe" ]; then
		size=$(wc -c < "$probe" | tr -d ' ')
		if [ "$size" -gt 5000 ]; then
			ok "binary assets are real files (${size} bytes)"
		else
			bad "$probe is only ${size} bytes - still an LFS pointer, not a model."
			say "        Unity will import a broken project. Fix with:  git lfs pull"
		fi
	else
		note "$probe not found - cannot verify LFS delivered real files"
	fi
fi

# ---------------------------------------------------------------- summary

say ""
say "----------------------------------------------------------------"

if [ "$fail" -eq 0 ]; then
	say "Setup complete${warn:+}."
	[ "$warn" -eq 0 ] || say "$warn warning(s) above - readable, but worth a look."
	say ""
	say "Still to do by hand, in Unity:"
	say "  - Window > Package Manager > My Assets, re-download BOKI Low Poly"
	say "    Nature and POLY Mountain. They are deliberately not in the repo."
	say "  - Open Assets/Scenes/LobbyIsland.unity. If it comes up intact, done."
	say ""
	say "Day to day: pull before you start, push before you stop. Two machines"
	say "with unpushed edits to the same scene has no good resolution."
else
	say "$fail problem(s) above must be fixed before using this clone."
	say "Full explanation: Tools/second-machine-setup.md"
fi

say ""
exit "$fail"
