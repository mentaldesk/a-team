#!/usr/bin/env bash
#
# status.sh — what each team role is doing, and how its last run went.
#
set -uo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
source "$ROOT/scripts/common.sh"

span() { local s=$(($(date +%s) - $1)); printf '%dh%02dm' $((s / 3600)) $((s % 3600 / 60)); }
ago() { echo "$(span "$1") ago"; }

# runs <dir>: "<started> <pid> #<task> <title>" for each of the Dev's runs still going, oldest first.
runs() {
  local file pid seen=' '
  for file in "$1/pid" "$1"/runs/*/pid; do
    pid=$(cat "$file" 2>/dev/null) && kill -0 "$pid" 2>/dev/null || continue
    [[ $seen == *" $pid "* ]] && continue
    seen+="$pid "
    printf '%s %s %s\n' "$(cat "$(dirname "$file")/last-start" 2>/dev/null || date +%s)" "$pid" \
      "$(jq -r '"#\(.number) \(.title)"' "$(dirname "$file")/task" 2>/dev/null)"
  done | sort -n
}

for team in $(team_names); do
  if [ -f "$STATE/$team/cannot-run" ]; then
    printf '\n%s: stopped: %s\n' "$team" "$(cat "$STATE/$team/cannot-run")"
  fi
  for role in lead dev; do
    dir="$STATE/$team/$role"
    printf '\n%s %s: ' "$team" "$role"
    live=$([ "$role" = lead ] || runs "$dir")
    if [ -n "$live" ]; then
      echo "running"
      while read -r at pid task; do
        echo "  ${task:-a run}: running $(span "$at") (pid $pid)"
      done <<<"$live"
    elif pid=$(cat "$dir/pid" 2>/dev/null) && kill -0 "$pid" 2>/dev/null; then
      echo "running (pid $pid, started $(ago "$(cat "$dir/last-start")"))"
    elif [ -f "$dir/last-start" ]; then
      echo "idle, last started $(ago "$(cat "$dir/last-start")")"
    elif [ -f "$dir/dry-last-start" ]; then
      echo "dry run: would have started $(ago "$(cat "$dir/dry-last-start")")"
      sed 's/^/  why: /' "$dir/dry-last-reasons" 2>/dev/null
      continue
    else
      echo "never run"
      continue
    fi
    sed 's/^/  why: /' "$dir/last-reasons" 2>/dev/null
    if [ -f "$dir/latest.jsonl" ]; then
      jq -r 'select(.type == "result")
             | "  result: \(if .is_error then "error" else "ok" end), \(.num_turns) turns, $\(.total_cost_usd * 100 | round / 100)",
               (.result // "" | split("\n") | map(select(length > 0)) | .[:6][] | "    \(.)")' \
        "$dir/latest.jsonl" 2>/dev/null
      echo "  log: $(readlink "$dir/latest.jsonl")"
    fi
  done
done

echo
echo "Recent dispatches:"
tail -n 10 "$STATE/dispatch.log" 2>/dev/null | sed 's/^/  /'
