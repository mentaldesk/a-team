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

sed -e "s|{{team}}|$TEAM|g" \
  -e "s|{{repo}}|$(jq -r .repo "$CONFIG")|g" \
  -e "s|{{workdir}}|$(jq -r .workdir "$CONFIG")|g" \
  "$ROOT/tasks/$ROLE.md"
