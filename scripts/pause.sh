#!/usr/bin/env bash
#
# pause.sh <pause|resume> [--dry-run] <team> — turns a team's dispatch off or on. The dispatcher
# reads dispatch.enabled on every pass, so there is nothing to restart either way.
#
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
CMD=${1:-}
[ $# -eq 0 ] || shift

die() { echo "a-team ${CMD:-pause}: $*" >&2; exit 1; }

case "$CMD" in
  pause) ENABLED=false ;;
  resume) ENABLED=true ;;
  *) die "expected pause or resume, not '$CMD'" ;;
esac

DRY_RUN=${A_TEAM_DRY_RUN:-}
if [ "${1:-}" = --dry-run ]; then
  DRY_RUN=1
  shift
fi
[ $# -eq 1 ] || die "usage: a-team $CMD [--dry-run] <team>"

source "$ROOT/scripts/common.sh"
TEAM=$1
CONFIG=$(team_config "$TEAM")
[ -f "$CONFIG" ] || die "no config for team '$TEAM' at $CONFIG"

UPDATED=$(jq -e --argjson enabled "$ENABLED" \
  'if type == "object" then .dispatch.enabled = $enabled else null end' "$CONFIG" 2>/dev/null) ||
  die "$CONFIG isn't a JSON object"

if [ -n "$DRY_RUN" ]; then
  echo "(dry run) would set dispatch.enabled to $ENABLED in $CONFIG"
  exit 0
fi

[ -w "$CONFIG" ] || die "can't write $CONFIG"
{ printf '%s\n' "$UPDATED" >"$CONFIG"; } 2>/dev/null || die "can't write $CONFIG"
echo "${CMD}d $TEAM"
