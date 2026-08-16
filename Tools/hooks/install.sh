#!/bin/sh
#
# Install this project's git hooks into .git/hooks.
#
# Run once per clone (Git Bash):   sh Tools/hooks/install.sh
#
# We copy rather than set core.hooksPath on purpose. core.hooksPath would point
# git at this directory and stop it looking in .git/hooks at all, which would
# disable the four Git LFS hooks (post-checkout, post-commit, post-merge,
# pre-push) that live there. This repo stores textures, models and audio in LFS,
# so silently losing those hooks would break checkouts.

set -e

root=$(git rev-parse --show-toplevel)
dest="$root/.git/hooks"

[ -d "$dest" ] || { echo "no .git/hooks at $dest" >&2; exit 1; }

for hook in pre-commit; do
	cp "$root/Tools/hooks/$hook" "$dest/$hook"
	chmod +x "$dest/$hook"
	echo "installed $hook -> $dest/$hook"
done

echo
echo "Note: the unityyamlmerge merge driver is per-clone git config, not a hook."
echo "It also has to be set on each machine. Current setting:"
git config --get merge.unityyamlmerge.driver || echo "  NOT CONFIGURED - see Tools/hooks/README.md"
