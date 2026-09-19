#!/usr/bin/env bash
#
# update.sh [--yes] — releases merged work to every team by fast-forwarding the live checkout
# to origin/main. Run it from the live checkout. Agents never run this; it's the reviewer's call.
#
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
STATE="${XDG_STATE_HOME:-$HOME/.local/state}/a-team"
die() { echo "update.sh: $*" >&2; exit 1; }

branch=$(git -C "$ROOT" rev-parse --abbrev-ref HEAD)
[ "$branch" = main ] || die "$ROOT is on '$branch', not main. Run this from the live checkout."
[ -z "$(git -C "$ROOT" status --porcelain)" ] ||
  die "$ROOT has local changes. The live checkout should only ever change by pulling merged work."

git -C "$ROOT" fetch -q origin
changes=$(git -C "$ROOT" log --oneline --no-decorate HEAD..origin/main)
if [ -z "$changes" ]; then
  echo "Up to date: nothing merged since the last release."
  exit 0
fi
git -C "$ROOT" merge-base --is-ancestor HEAD origin/main ||
  die "main has diverged from origin/main; sort that out by hand."

echo "These merged changes will go live for every team:"
sed 's/^/  /' <<<"$changes"
running=""
for pidfile in "$STATE"/*/*/pid; do
  [ -f "$pidfile" ] && kill -0 "$(cat "$pidfile")" 2>/dev/null || continue
  role_dir=$(dirname "$pidfile")
  running="$running $(basename "$(dirname "$role_dir")")/$(basename "$role_dir")"
done
[ -n "$running" ] && echo "Running now:$running. They carry on with what they've already read; their next run gets the new version."

if [ "${1:-}" != --yes ]; then
  read -r -p "Release? [y/N] " answer
  [[ "$answer" =~ ^[Yy]$ ]] || { echo "Nothing changed."; exit 0; }
fi

old=$(git -C "$ROOT" rev-parse HEAD)
git -C "$ROOT" merge -q --ff-only origin/main
echo "Released $(git -C "$ROOT" rev-parse --short HEAD)."
if ! git -C "$ROOT" diff --quiet "$old" HEAD -- scripts/install.sh; then
  echo "scripts/install.sh changed: run 'bash $ROOT/scripts/install.sh' to apply it."
fi
