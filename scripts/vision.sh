#!/usr/bin/env bash
#
# vision.sh [--dry-run] <team> — hands the terminal to an interactive Claude session, briefed by
# tasks/vision.md, that interviews you and opens the team's vision as a document pitch. Waits for
# Enter once it ends, so its last words stay on screen.
#
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"

die() { echo "a-team vision: $*" >&2; exit 1; }

DRY_RUN=${A_TEAM_DRY_RUN:-}
if [ "${1:-}" = --dry-run ]; then
  DRY_RUN=1
  shift
fi
[ $# -eq 1 ] || die "usage: a-team vision [--dry-run] <team>"

source "$ROOT/scripts/common.sh"
TEAM=$1
CONFIG=$(team_config "$TEAM")
[ -f "$CONFIG" ] || die "no config for team '$TEAM' at $CONFIG"
REPO=$(jq -r '.repo // empty' "$CONFIG")
[ -n "$REPO" ] || die "$TEAM.json names no repo"
VISION=$(jq -r '.vision // "docs/vision.md"' "$CONFIG")
WORKDIR=$(jq -r '.workdir // empty' "$CONFIG")
[ -n "$WORKDIR" ] || die "$TEAM.json names no workdir"
CHECKOUT=$(jq -r '.checkout // ((.workdir | rtrimstr("/")) + "/main")' "$CONFIG")
CHECKOUT=${CHECKOUT/#\~/$HOME}
[ -d "$CHECKOUT" ] || die "$CHECKOUT isn't there: gh repo clone $REPO $CHECKOUT"

BRIEF=$(sed -e "s|{{team}}|$TEAM|g" -e "s|{{repo}}|$REPO|g" -e "s|{{vision}}|$VISION|g" \
  -e "s|{{workdir}}|$WORKDIR|g" -e "s|{{a_team}}|${A_TEAM_BIN:-a-team}|g" "$ROOT/tasks/vision.md")

if [ -n "$DRY_RUN" ]; then
  echo "(dry run) would run claude in $CHECKOUT, briefed by $ROOT/tasks/vision.md"
  exit 0
fi

cd "$CHECKOUT"
ended=0
claude --add-dir "${WORKDIR/#\~/$HOME}" --append-system-prompt "$BRIEF" "Write $TEAM's vision with me." || ended=$?
if [ -t 0 ]; then
  read -r -p "Press Enter to return to a-team. " _ || true
fi
exit "$ended"
