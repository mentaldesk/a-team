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
  local dir="$STATE/$team/$role" now pid started triggers reasons creative last what
  local sweep='' prefix='' task='' number=''
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
  jq -e --arg role "$role" '.dispatch.hold // [] | index($role)' "$config" >/dev/null 2>&1 && return

  # A missing last-sweep reads as never, so a fresh install sweeps on its first pass.
  [ $((now - $(cat "$dir/${prefix}last-sweep" 2>/dev/null || echo 0))) \
    -ge $(($(cfg '.dispatch.sweepEvery // 30') * 60)) ] && sweep=--sweep

  if ! triggers=$("$ROOT/bin/a-team" board "$team" triggers "$role" ${sweep:+"$sweep"} 2>&1); then
    log "$team $role: triggers failed: $(tr '\n' ' ' <<<"$triggers")"
    return
  fi
  [ -z "$sweep" ] || echo "$now" >"$dir/${prefix}last-sweep"
  last=$(cat "$dir/${prefix}last-start" 2>/dev/null || echo 0)

  if [ "$role" = dev ]; then
    task=$(pick_task "$team" "$dir" "$triggers") || return
    [ -n "$task" ] || return
    reasons=$(jq -r '.reasons[]' <<<"$task"; jq -r '.chores[]?' <<<"$triggers")
    task=$(jq -c '{number, title}' <<<"$task")
    number=$(jq -r .number <<<"$task")
  else
    reasons=$(jq -r '.reasons[]' <<<"$triggers")
    creative=$(jq -r .creative <<<"$triggers")
    if [ -z "$reasons" ]; then
      [ "$creative" = true ] && [ $((now - last)) -ge $(($(cfg '.dispatch.creativeEvery // 180') * 60)) ] ||
        return
      reasons="it's been a while: time to pitch or discover"
    fi
    fresh "$dir/${prefix}fingerprint" "$reasons" || return
  fi
  echo "$now" >"$dir/${prefix}last-start"
  printf '%s\n' "$reasons" >"$dir/${prefix}last-reasons"
  if [ -n "$task" ]; then echo "$task" >"$dir/${prefix}task"; else rm -f "$dir/${prefix}task"; fi
  what=$(paste -sd ';' - <<<"$reasons")
  [ -z "$number" ] || what="#$number: $what"

  if $DRY_RUN; then
    log "$team $role: would start: $what"
    return
  fi

  local workdir logfile prompt settings
  workdir=$(cfg .workdir)
  workdir=${workdir/#\~/$HOME}
  logfile="$dir/logs/$(date -u +%Y%m%dT%H%M%SZ).jsonl"
  settings="$dir/settings.json"
  sed "s|{{root}}|/$ROOT|g" "$ROOT/settings/agents.json" >"$settings"
  prompt="$("$ROOT/bin/a-team" task-prompt "$team" "$role")
${number:+
This run is for #$number $(jq -r .title <<<"$task"), and only that task.
}
This run was started because:
$(sed 's/^/- /' <<<"$reasons")"

  (
    cd "$workdir" || exit 1
    PATH="$ROOT/bin:$PATH" A_TEAM_RUN_TEAM="$team" A_TEAM_RUN_TASK="$number" A_TEAM_RUN_STARTED="$(iso "$now")" nohup claude -p "$prompt" \
      --permission-mode auto --permission-prompts none \
      --settings "$settings" \
      --name "a-team · $team · $role" \
      --output-format stream-json --verbose \
      >"$logfile" 2>&1 &
    echo $! >"$dir/pid"
  )
  ln -sf "$logfile" "$dir/latest.jsonl"
  log "$team $role: started $(cat "$dir/pid"): $what"
}

# fresh <file> <reasons>: whether these reasons are worth a run. The same ones as last time wait
# retryAfter, unless that run died before finishing. <file> holds "<fingerprint> <started>".
fresh() {
  local fingerprint tried at
  fingerprint=$(shasum <<<"$2" | cut -c1-12)
  read -r tried at 2>/dev/null <"$1"
  if [ "$fingerprint" = "${tried:-}" ] && [ $((now - ${at:-0})) -lt $(($(cfg '.dispatch.retryAfter // 60') * 60)) ] &&
    { $DRY_RUN || grep -q '"type":"result"' "$dir/latest.jsonl" 2>/dev/null; }; then
    return 1
  fi
  echo "$fingerprint $now" >"$1"
}

# pick_task <team> <dir> <triggers>: the one task a Dev run is for, as {number, title, reasons}.
# A task already in hand comes first; only then is a Ready task claimed, before the run starts.
pick_task() {
  local row claimed
  mkdir -p "$2/${prefix}tried"
  while IFS= read -r row; do
    fresh "$2/${prefix}tried/$(jq -r .number <<<"$row")" "$(jq -r '.reasons[]' <<<"$row")" &&
      { echo "$row"; return; }
  done < <(jq -c '.tasks[]?' <<<"$3")
  [ "$(jq -r '.ready // empty' <<<"$3")" != "" ] || return 0
  if ! claimed=$("$ROOT/bin/a-team" board ${prefix:+--dry-run} "$1" claim dev 2>"$2/claim.err"); then
    log "$1 dev: claim failed: $(tr '\n' ' ' <"$2/claim.err")"
    return 1
  fi
  [ "$claimed" = null ] || jq -c '. + {reasons: ["Ready task #\(.number) to build"]}' <<<"$claimed"
}

# cannot_run <team> <config>: why the team can't run, if it can't. It runs only as its own App.
cannot_run() {
  local why
  jq -e '.app.id' "$2" >/dev/null 2>&1 ||
    { echo "no GitHub App: run a-team app create $1, then install it"; return; }
  why=$("$ROOT/bin/a-team" token "$1" 2>&1 >/dev/null) || echo "${why#a-team token: }"
}

for team in $(team_names); do
  config=$(team_config "$team")
  jq -e '.dispatch.enabled == true' "$config" >/dev/null 2>&1 || continue
  why=$(cannot_run "$team" "$config")
  if [ -n "$why" ]; then
    mkdir -p "$STATE/$team"
    [ "$why" = "$(cat "$STATE/$team/cannot-run" 2>/dev/null)" ] || log "$team: stopped: $why"
    echo "$why" >"$STATE/$team/cannot-run"
    continue
  fi
  rm -f "$STATE/$team/cannot-run"
  for role in lead dev; do
    dispatch "$team" "$role" "$config"
  done
done
exit 0
}
