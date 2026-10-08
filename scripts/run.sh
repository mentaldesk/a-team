#!/usr/bin/env bash
#
# run.sh <team> <role> — prints the brief a scheduled run starts from.
# Keep the scheduled task's command constant: approvals are stored as literal strings.
#
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
TEAM=${1:?usage: run.sh <team> <role>}
ROLE=${2:?usage: run.sh <team> <role>}
source "$ROOT/scripts/common.sh"
CONFIG=$(team_config "$TEAM")

[ -f "$CONFIG" ] || { echo "run.sh: no team config at $CONFIG" >&2; exit 3; }
[ -f "$ROOT/roles/$ROLE.md" ] || { echo "run.sh: no role '$ROLE'" >&2; exit 3; }
command -v jq >/dev/null || { echo "run.sh: jq is not installed" >&2; exit 3; }
team_roles "$CONFIG" | grep -qx "$ROLE" || { echo "run.sh: $TEAM has no $ROLE turned on" >&2; exit 3; }
gh auth status >/dev/null 2>&1 || { echo "run.sh: gh is not authenticated" >&2; exit 3; }

# check exits 2 for what's missing but leaves the team able to run, like a vision the Lead is yet to draft.
health=$("$ROOT/bin/a-team" board "$TEAM" check 2>&1) || healthy=$?
if [ "${healthy:-0}" -ne 0 ] && [ "$healthy" -ne 2 ]; then
  printf 'run.sh: the board is not usable, so this run must stop and report:\n%s\n' "$health" >&2
  exit 3
fi

# Nothing else updates the product checkout, and a run reads the vision and the code from it.
CHECKOUT=$(jq -r '.checkout // ((.workdir // "" | rtrimstr("/")) + "/main")' "$CONFIG")
CHECKOUT=${CHECKOUT/#\~/$HOME}
DEFAULT=origin/main
stale=1
if git -C "$CHECKOUT" fetch --quiet origin 2>/dev/null; then
  DEFAULT=$(git -C "$CHECKOUT" symbolic-ref --short refs/remotes/origin/HEAD 2>/dev/null) || DEFAULT=origin/main
  if [ "origin/$(git -C "$CHECKOUT" branch --show-current)" = "$DEFAULT" ] &&
    git -C "$CHECKOUT" merge --ff-only --quiet "$DEFAULT" >/dev/null 2>&1; then
    stale=''
  fi
fi

cat <<EOF
# a-team run: $ROLE for team '$TEAM'

Board: a-team board $TEAM <command> ...
Version: $("$ROOT/bin/a-team" version)

Run this first, so a comment that arrives while you work isn't marked answered:

    export A_TEAM_RUN_STARTED=$(iso "$(date +%s)")

## Team config

\`\`\`json
$(cat "$CONFIG")
\`\`\`

## Board right now

\`\`\`json
$("$ROOT/bin/a-team" board "$TEAM" wip)
\`\`\`

EOF
if [ -n "$stale" ]; then
  printf '## Product checkout\n\n%s could not be brought up to date with %s, so read the product repo from %s (`git -C %s show %s:<path>`), not from its files.\n\n' \
    "$CHECKOUT" "$DEFAULT" "$DEFAULT" "$CHECKOUT" "$DEFAULT"
fi
if [ -n "${A_TEAM_RUN_TASK:-}" ]; then
  printf '## Your task\n\nThis run is for #%s and nothing else. The dispatcher picked it, and claimed it if it was Ready.\n\n' \
    "$A_TEAM_RUN_TASK"
fi
sed "s/{{team}}/$TEAM/g" "$ROOT/process.md"
echo
sed "s/{{team}}/$TEAM/g" "$ROOT/roles/$ROLE.md"
