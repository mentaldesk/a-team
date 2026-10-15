#!/usr/bin/env bash
#
# task-prompt.sh <team> <role> — prints the scheduled task prompt for one role of a team.
#
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
TEAM=${1:?usage: task-prompt.sh <team> <role>}
ROLE=${2:?usage: task-prompt.sh <team> <role>}
source "$ROOT/scripts/common.sh"
CONFIG=$(team_config "$TEAM")

contributes=$(jq -r '.contributesTo // [] | join(", ")' "$CONFIG")
[ -z "$contributes" ] ||
  contributes="- The same on the other repos this team contributes to, $contributes: clone them
  under {{workdir}}, push branches other than the default branch, open draft PRs, mark your own PRs ready
  for review once CI is green, and comment on issues and PRs there."

CONTRIBUTES=$contributes awk '$0 != "{{contributes}}" { print; next } ENVIRON["CONTRIBUTES"] != "" { print ENVIRON["CONTRIBUTES"] }' \
  "$ROOT/tasks/$ROLE.md" |
  sed -e "s|{{team}}|$TEAM|g" \
    -e "s|{{repo}}|$(jq -r .repo "$CONFIG")|g" \
    -e "s|{{workdir}}|$(jq -r .workdir "$CONFIG")|g"
