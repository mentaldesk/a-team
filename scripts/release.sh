#!/usr/bin/env bash
#
# release.sh [--dry-run] <team> — runs the team repo's release.yml workflow when the team config's
# `release` says one is due: `continuous` once the default branch has moved past the latest release,
# `daily` once it has and that release is a day old, `never` (the default) never. The dispatcher
# runs it every pass, and logs the one line it prints whenever that changes.
#
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
source "$ROOT/scripts/common.sh"
WORKFLOW=release.yml

die() { echo "a-team release: $*" >&2; exit 1; }

DRY_RUN=${A_TEAM_DRY_RUN:-}
if [ "${1:-}" = --dry-run ]; then
  DRY_RUN=1
  shift
fi
[ $# -eq 1 ] || die "usage: a-team release [--dry-run] <team>"
TEAM=$1
CONFIG=$(team_config "$TEAM")
[ -f "$CONFIG" ] || die "no config for team '$TEAM' at $CONFIG"
REPO=$(jq -r '.repo // empty' "$CONFIG")
CADENCE=$(jq -r '.release // "never"' "$CONFIG")
case "$CADENCE" in
  never) echo "release is never: nothing to do"; exit 0 ;;
  daily | continuous) ;;
  *) die "release is '$CADENCE' in $TEAM.json: expected never, daily or continuous" ;;
esac
[ -n "$REPO" ] || die "$TEAM.json names no repo"

repo=$(gh api graphql -F owner="${REPO%/*}" -F name="${REPO#*/}" -f query='
  query($owner: String!, $name: String!) { repository(owner: $owner, name: $name) {
    defaultBranchRef { name target { oid } }
    latestRelease { tagName createdAt tagCommit { oid } } } }' --jq .data.repository 2>&1) ||
  die "can't read $REPO: ${repo#gh: }"
branch=$(jq -r .defaultBranchRef.name <<<"$repo")
head=$(jq -r .defaultBranchRef.target.oid <<<"$repo")
latest=$(jq -r '.latestRelease.tagName // "none"' <<<"$repo")

if [ "$head" = "$(jq -r '.latestRelease.tagCommit.oid // empty' <<<"$repo")" ]; then
  echo "nothing to release: $branch is at $latest"
  exit 0
fi
if [ "$CADENCE" = daily ] && due=$(jq -er '.latestRelease.createdAt // empty | fromdateiso8601 + 86400
    | select(. > now) | strftime("%Y-%m-%dT%H:%MZ")' <<<"$repo"); then
  echo "next release due at $due: $latest is under a day old"
  exit 0
fi

STARTED="$STATE/$TEAM/release-started"
if [ "$(cat "$STARTED" 2>/dev/null)" = "$head" ]; then
  echo "ran $WORKFLOW for $branch at ${head:0:7} already: if no release follows, see https://github.com/$REPO/actions/workflows/$WORKFLOW"
  exit 0
fi
running=$(gh api "repos/$REPO/actions/workflows/$WORKFLOW/runs?branch=$branch&per_page=1" \
  --jq '.workflow_runs[0] | select(.status != "completed") | .html_url' 2>&1) ||
  die "can't read $REPO's $WORKFLOW runs: ${running#gh: }"
if [ -n "$running" ]; then
  echo "waiting for the $WORKFLOW run going now to finish: $running"
  exit 0
fi

if [ -n "$DRY_RUN" ]; then
  echo "would run $WORKFLOW on $branch at ${head:0:7} (latest release: $latest)"
  exit 0
fi
refused=$(gh api -X POST "repos/$REPO/actions/workflows/$WORKFLOW/dispatches" -f ref="$branch" 2>&1) ||
  die "couldn't run $REPO's $WORKFLOW: ${refused#gh: }"
mkdir -p "$(dirname "$STARTED")"
echo "$head" >"$STARTED"
echo "ran $WORKFLOW on $branch at ${head:0:7} (latest release: $latest)"
