#!/usr/bin/env bash
#
# dispatch.sh [--dry-run] — starts a headless session for each team role that has work.
# launchd runs it every couple of minutes (see install.sh). Deciding whether there is work is
# a plain script (board.sh triggers); Claude only starts when there is.
#
set -uo pipefail

# Everything is inside one { } block, so bash reads the whole file before running any of it and
# a release that replaces this file mid-pass can't mix old and new lines.
{

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
source "$ROOT/scripts/common.sh"
DRY_RUN=false
[ "${1:-}" = --dry-run ] && DRY_RUN=true
mkdir -p "$STATE"
$DRY_RUN || echo $(($(date +%s) + ${A_TEAM_INTERVAL:-120})) >"$STATE/next-pass"

log() { printf '%s %s\n' "$(date -u +%FT%TZ)" "$*" >>"$STATE/dispatch.log"; }

dispatch() {
  local team=$1 role=$2 config=$3
  local dir="$STATE/$team/$role" now pid started triggers reasons creative last fingerprint
  local sweep='' prefix=''
  $DRY_RUN && prefix="dry-"
  mkdir -p "$dir/logs"
  now=$(date +%s)
  cfg() { jq -r "$1" "$config"; }

  if pid=$(cat "$dir/pid" 2>/dev/null) && kill -0 "$pid" 2>/dev/null; then
    started=$(cat "$dir/last-start" 2>/dev/null || echo "$now")
    if [ $((now - started)) -gt $(($(cfg '.dispatch.maxRuntime // 120') * 60)) ]; then
      kill "$pid" && log "$team $role: killed run $pid after $(((now - started) / 60)) minutes"
    fi
    return
  fi

  # A missing last-sweep reads as never, so the first pass after an install or a reboot sweeps.
  [ $((now - $(cat "$dir/${prefix}last-sweep" 2>/dev/null || echo 0))) \
    -ge $(($(cfg '.dispatch.sweepEvery // 30') * 60)) ] && sweep=--sweep

  if ! triggers=$("$ROOT/bin/a-team" board "$team" triggers "$role" ${sweep:+"$sweep"} 2>&1); then
    log "$team $role: triggers failed: $(tr '\n' ' ' <<<"$triggers")"
    return
  fi
  [ -z "$sweep" ] || echo "$now" >"$dir/${prefix}last-sweep"
  reasons=$(jq -r '.reasons[]' <<<"$triggers")
  creative=$(jq -r .creative <<<"$triggers")
  last=$(cat "$dir/${prefix}last-start" 2>/dev/null || echo 0)

  if [ -z "$reasons" ]; then
    [ "$creative" = true ] && [ $((now - last)) -ge $(($(cfg '.dispatch.creativeEvery // 180') * 60)) ] ||
      return
    reasons="it's been a while: time to pitch or discover"
  fi

  fingerprint=$(shasum <<<"$reasons" | cut -c1-12)
  if [ "$fingerprint" = "$(cat "$dir/${prefix}fingerprint" 2>/dev/null)" ] &&
    [ $((now - last)) -lt $(($(cfg '.dispatch.retryAfter // 60') * 60)) ] &&
    { $DRY_RUN || grep -q '"type":"result"' "$dir/latest.jsonl" 2>/dev/null; }; then
    return
  fi
  echo "$now" >"$dir/${prefix}last-start"
  echo "$fingerprint" >"$dir/${prefix}fingerprint"
  printf '%s\n' "$reasons" >"$dir/${prefix}last-reasons"

  if $DRY_RUN; then
    log "$team $role: would start: $(paste -sd ';' - <<<"$reasons")"
    return
  fi

  local workdir logfile prompt settings
  workdir=$(cfg .workdir)
  workdir=${workdir/#\~/$HOME}
  logfile="$dir/logs/$(date -u +%Y%m%dT%H%M%SZ).jsonl"
  settings="$dir/settings.json"
  sed "s|{{root}}|/$ROOT|g" "$ROOT/settings/agents.json" >"$settings"
  prompt="$("$ROOT/bin/a-team" task-prompt "$team" "$role")

This run was started because:
$(sed 's/^/- /' <<<"$reasons")"

  (
    cd "$workdir" || exit 1
    PATH="$ROOT/bin:$PATH" nohup claude -p "$prompt" \
      --permission-mode auto --permission-prompts none \
      --settings "$settings" \
      --name "a-team · $team · $role" \
      --output-format stream-json --verbose \
      >"$logfile" 2>&1 &
    echo $! >"$dir/pid"
  )
  ln -sf "$logfile" "$dir/latest.jsonl"
  log "$team $role: started $(cat "$dir/pid"): $(paste -sd ';' - <<<"$reasons")"
}

for team in $(team_names); do
  config=$(team_config "$team")
  jq -e '.dispatch.enabled == true' "$config" >/dev/null 2>&1 || continue
  for role in lead dev; do
    dispatch "$team" "$role" "$config"
  done
done
exit 0
}
