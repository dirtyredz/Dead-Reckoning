#!/bin/sh
# Installs this repo's git hooks. Run once per clone:  sh scripts/install-git-hooks.sh
# A copy, not a symlink — Windows needs a privilege for symlinks and git executes a copy just fine.
#
# Deliberately does NOT set core.hooksPath: the structure-review pre-push gate also lives in
# .git/hooks, and pointing hooksPath elsewhere would silently disable it.
root=$(git rev-parse --show-toplevel) || exit 1
cp "$root/scripts/pre-commit.sh" "$root/.git/hooks/pre-commit"
chmod +x "$root/.git/hooks/pre-commit"
echo "installed .git/hooks/pre-commit"
