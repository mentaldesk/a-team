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
gh auth status >/dev/null 2>&1 || { echo "run.sh: gh is not authenticated" >&2; exit 3; }

if ! health=$("$ROOT/bin/a-team" board "$TEAM" check 2>&1); then
  printf 'run.sh: the board is not usable, so this run must stop and report:\n%s\n' "$health" >&2
  exit 3
fi

cat <<EOF
# a-team run: $ROLE for team '$TEAM'

Board: a-team board $TEAM <command> ...
Version: $("$ROOT/bin/a-team" version)

## Team config

\`\`\`json
$(cat "$CONFIG")
\`\`\`

## Board right now

\`\`\`json
$("$ROOT/bin/a-team" board "$TEAM" wip)
\`\`\`

EOF
sed "s/{{team}}/$TEAM/g" "$ROOT/process.md"
echo
sed "s/{{team}}/$TEAM/g" "$ROOT/roles/$ROLE.md"
