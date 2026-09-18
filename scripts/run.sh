#!/usr/bin/env bash
#
# run.sh <team> <role> — prints the brief a scheduled run starts from.
# Keep the scheduled task's command constant: approvals are stored as literal strings.
#
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
TEAM=${1:?usage: run.sh <team> <role>}
ROLE=${2:?usage: run.sh <team> <role>}
CONFIG="$ROOT/teams/$TEAM/team.json"

[ -f "$CONFIG" ] || { echo "run.sh: no team config at $CONFIG" >&2; exit 3; }
[ -f "$ROOT/roles/$ROLE.md" ] || { echo "run.sh: no role '$ROLE'" >&2; exit 3; }
command -v jq >/dev/null || { echo "run.sh: jq is not installed" >&2; exit 3; }
gh auth status >/dev/null 2>&1 || { echo "run.sh: gh is not authenticated" >&2; exit 3; }

if ! health=$(bash "$ROOT/scripts/board.sh" "$TEAM" check 2>&1); then
  printf 'run.sh: the board is not usable, so this run must stop and report:\n%s\n' "$health" >&2
  exit 3
fi

cat <<EOF
# a-team run: $ROLE for team '$TEAM'

Board script: bash $ROOT/scripts/board.sh $TEAM <command> ...
Revision: $(git -C "$ROOT" log -1 --format='%h %s' 2>/dev/null || echo unknown)

## Team config

\`\`\`json
$(cat "$CONFIG")
\`\`\`

## Board right now

\`\`\`json
$(bash "$ROOT/scripts/board.sh" "$TEAM" wip)
\`\`\`

EOF
cat "$ROOT/process.md"
echo
cat "$ROOT/roles/$ROLE.md"
