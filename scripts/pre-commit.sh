#!/bin/sh
# Style gate for commits: format the STAGED C# files, then let the commit proceed.
# Installed into .git/hooks/pre-commit by scripts/install-git-hooks.sh.
#
# Scoped with --include to the staged, git-tracked files. NEVER plain `--folder .`: that descends into
# .claude/worktrees/ — gitignored sibling checkouts that a commit here must not rewrite.
#
# A formatter that cannot run is not a reason to block a commit; the code is not what failed.
root=$(git rev-parse --show-toplevel) || exit 0
staged=$(git diff --cached --name-only --diff-filter=ACMR -- '*.cs')
[ -z "$staged" ] && exit 0

# Files that ALSO have unstaged changes: formatting rewrites the whole file, so re-adding it would
# sweep those hunks into the commit. Format them, leave staging alone, and say so.
partial=$(git diff --name-only -- $staged)

if ! dotnet format whitespace --folder "$root" --include $staged; then
  echo "  pre-commit: dotnet format could not run — commit allowed, formatting skipped" >&2
  exit 0
fi

for f in $staged; do
  case " $partial " in *" $f "*) continue ;; esac
  git add -- "$f"
done

if [ -n "$partial" ]; then
  echo "  pre-commit: formatted but NOT re-staged (they have unstaged changes too):" >&2
  echo "$partial" | sed 's/^/    /' >&2
fi
exit 0
