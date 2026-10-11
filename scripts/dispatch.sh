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
# A pass run off schedule, from the app, leaves the scheduled one's countdown alone.
if [ -z "${A_TEAM_UNSCHEDULED:-}" ]; then
  # While this holds a live pid, an overdue next-pass is a slow pass, not a stopped dispatcher.
  PASS="$STATE/$($DRY_RUN && echo dry-)pass"
  NEXT="$STATE/$($DRY_RUN && echo dry-)next-pass"
  echo $$ >"$PASS"
  # launchd counts StartInterval from when a pass ends, not from when it started.
  trap '[ "$(cat "$PASS" 2>/dev/null)" = $$ ] && rm -f "$PASS"; echo $(($(date +%s) + ${A_TEAM_INTERVAL:-120})) >"$NEXT"' EXIT
fi

log() { printf '%s %s\n' "$(date -u +%FT%TZ)" "$*" >>"$STATE/dispatch.log"; }

# failing <shown> <once> <why> <label>: one failed pass is often a blip, so only a second in a row is logged.
failing() {
  if [ -f "$1" ] || [ -f "$2" ]; then
    [ "$3" = "$(cat "$1" 2>/dev/null)" ] || log "$4: $3"
    echo "$3" >"$1"
    rm -f "$2"
  else
    echo "$3" >"$2"
  fi
}

# passing <shown> <once> <message>: logs <message> if a failure was shown.
passing() {
  [ -f "$1" ] && log "$3"
  rm -f "$1" "$2"
}

dispatch() {
  local team=$1 role=$2 config=$3
  local dir="$STATE/$team/$role" now triggers reasons creative last live limit pid at
  local sweep='' prefix='' task='' number='' started=0 picked=' ' chores='' items='' card=''
  $DRY_RUN && prefix="dry-"
  mkdir -p "$dir/logs"
  now=$(date +%s)
  cfg() { jq -r "$1" "$config"; }

  $DRY_RUN || prune_releases "$dir"
  live=$(live_runs "$dir")
  while read -r pid at _; do
    [ -n "$pid" ] || continue
    [ $((now - at)) -gt $(($(cfg '.dispatch.maxRuntime // 120') * 60)) ] && kill "$pid" &&
      log "$team $role: killed run $pid after $(((now - at) / 60)) minutes" && run_outcome "$team" "$role" "$pid" killed
  done <<<"$live"
  $over_budget && return
  limit=1
  [ "$role" = dev ] && limit=$(cfg '.wip.devs // 1')
  [ "$(grep -c . <<<"$live")" -lt "$limit" ] || return
  jq -e --arg role "$role" '.dispatch.hold // [] | index($role)' "$config" >/dev/null 2>&1 && return

  # A missing last-sweep reads as never, so a fresh install sweeps on its first pass.
  [ $((now - $(cat "$dir/${prefix}last-sweep" 2>/dev/null || echo 0))) \
    -ge $(($(cfg '.dispatch.sweepEvery // 30') * 60)) ] && sweep=--sweep

  if ! triggers=$("$ROOT/bin/a-team" board "$team" triggers "$role" ${sweep:+"$sweep"} 2>&1); then
    failing "$dir/${prefix}triggers-failing" "$dir/${prefix}triggers-failed-once" \
      "${triggers//$'\n'/ }" "$team $role: triggers failed"
    return
  fi
  passing "$dir/${prefix}triggers-failing" "$dir/${prefix}triggers-failed-once" "$team $role: triggers working again"
  [ -z "$sweep" ] || echo "$now" >"$dir/${prefix}last-sweep"
  last=$(cat "$dir/${prefix}last-start" 2>/dev/null || echo 0)

  if [ "$role" != dev ] && [ "$role" != reviewer ]; then
    reasons=$(jq -r '.reasons[]' <<<"$triggers")
    creative=$(jq -r .creative <<<"$triggers")
    if [ -z "$reasons" ]; then
      [ "$creative" = true ] && [ $((now - last)) -ge $(($(cfg '.dispatch.creativeEvery // 180') * 60)) ] ||
        return
      reasons="it's been a while: time to pitch or discover"
    fi
    fresh "$dir/${prefix}fingerprint" "$reasons" "$dir/latest.jsonl" || return
    items=$(jq -r '.items[]?' <<<"$triggers")
    card=$(jq -c 'select(.card) | {number: .card}' <<<"$triggers")
    launch
    return
  fi

  # Tasks that already have a live run never get a second one.
  picked+="$(cut -d' ' -f3 <<<"$live" | tr '\n' ' ')"
  chores=$(jq -r '.chores[]?' <<<"$triggers")
  while [ $(($(grep -c . <<<"$live") + started)) -lt "$limit" ]; do
    task=$(pick_task "$team" "$dir" "$triggers") || return
    [ -n "$task" ] || return
    number=$(jq -r .number <<<"$task")
    [[ $picked == *" $number "* ]] && return
    picked+="$number "
    # Chores ride along with the first run of the pass.
    reasons=$(jq -r '.reasons[]' <<<"$task"; [ "$started" -gt 0 ] || printf '%s' "$chores")
    reasons=$(sed '/^$/d' <<<"$reasons")
    task=$(jq -c '{number, title}' <<<"$task")
    items=$number
    launch || return
    started=$((started + 1))
  done
}

# live_runs <dir>: "<pid> <started> <task>" for each of the role's runs still going, oldest first.
live_runs() {
  local file pid seen=' ' at
  for file in "$1/pid" "$1"/runs/*/pid; do
    pid=$(cat "$file" 2>/dev/null) && kill -0 "$pid" 2>/dev/null || continue
    [[ $seen == *" $pid "* ]] && continue
    seen+="$pid "
    at=$(cat "$(dirname "$file")/last-start" 2>/dev/null || echo "$now")
    echo "$pid $at $(jq -r '.number // empty' "$(dirname "$file")/task" 2>/dev/null)"
  done | sort -k2n
}

# prune_releases <dir>: removes the release copy of each of the role's runs that has finished.
prune_releases() {
  local release
  for release in "$1/release" "$1"/runs/*/release; do
    [ -d "$release" ] || continue
    kill -0 "$(cat "$(dirname "$release")/pid" 2>/dev/null)" 2>/dev/null || rm -rf "$release"
  done
}

# copy_release <dest>: this release, without the dashboard, for one run to keep to its end.
copy_release() {
  local item
  rm -rf "$1"
  mkdir -p "$1" || return
  for item in bin scripts roles tasks settings examples process.md; do
    [ ! -e "$ROOT/$item" ] || cp -R "$ROOT/$item" "$1/" || return
  done
  "$ROOT/bin/a-team" version >"$1/VERSION"
}

# launch: starts a run for $reasons (and $task, for the Dev or Reviewer). Such a run keeps its state under
# runs/<task>, and the role's own files follow the run started last. Each run runs on its own copy
# of the release, so an upgrade mid-run can't change it.
launch() {
  local what run logfile prompt settings workdir pid release version own
  workdir=$(cfg .workdir)
  if ! $DRY_RUN && [ ! -d "${workdir/#\~/$HOME}" ]; then
    log "$team $role: cannot start: workdir $workdir: no such directory"
    return 1
  fi
  workdir=${workdir/#\~/$HOME}
  echo "$now" >"$dir/${prefix}last-start"
  printf '%s\n' "$reasons" >"$dir/${prefix}last-reasons"
  if [ -n "$task" ]; then echo "$task" >"$dir/${prefix}task"; else rm -f "$dir/${prefix}task"; fi
  if [ -n "$card" ]; then echo "$card" >"$dir/${prefix}card"; else rm -f "$dir/${prefix}card"; fi
  what=$(paste -sd ';' - <<<"$reasons")
  [ -z "$number" ] || what="#$number: $what"

  if $DRY_RUN; then
    log "$team $role: would start: $what"
    return
  fi

  logfile="$dir/logs/$(date -u +%Y%m%dT%H%M%SZ)${number:+-$number}.jsonl"
  release="$dir${number:+/runs/$number}/release"
  if ! copy_release "$release"; then
    log "$team $role: couldn't copy the release to $release"
    return
  fi
  version=$(cat "$release/VERSION")
  settings="$release/settings.json"
  # A role's own settings/<role>.json adds to the rules every run has.
  own="$release/settings/$role.json"
  [ -f "$own" ] || own=/dev/null
  jq -s '(.[1].permissions // {}) as $own | .[0]
    | .permissions.allow += ($own.allow // []) | .permissions.deny += ($own.deny // [])' \
    "$release/settings/agents.json" "$own" | sed "s|{{root}}|/$release|g" >"$settings"
  prompt="$("$release/bin/a-team" task-prompt "$team" "$role")
${number:+
This run is for #$number $(jq -r .title <<<"$task"), and only that task.
}
This run was started because:
$(sed 's/^/- /' <<<"$reasons")"

  pid=$(
    cd "$workdir" || exit 1
    PATH="$release/bin:$PATH" A_TEAM_RUN_TEAM="$team" A_TEAM_RUN_TASK="$number" A_TEAM_RUN_STARTED="$(iso "$now")" nohup claude -p "$prompt" \
      --permission-mode auto --permission-prompts none \
      --settings "$settings" \
      --name "a-team · $team · $role${number:+ · #$number}" \
      --output-format stream-json --verbose \
      >"$logfile" 2>&1 &
    echo $!
  )
  echo "$pid" >"$dir/pid"
  ln -sf "$logfile" "$dir/latest.jsonl"
  record_run "$pid" "$logfile"
  if [ -n "$number" ]; then
    run="$dir/runs/$number"
    mkdir -p "$run"
    cp "$dir/pid" "$dir/last-start" "$dir/last-reasons" "$dir/task" "$run/"
    ln -sf "$logfile" "$run/latest.jsonl"
  fi
  log "$team $role: started $pid on $version: $what"
}

# record_run <pid> <log>: the run in the history record, on each of $items.
record_run() {
  local values='' item
  for item in $items; do values+="${values:+, }((SELECT MAX(id) FROM runs WHERE log = $(sql "$2")), $item)"; done
  history_sql "INSERT INTO runs (team, role, pid, log, started) VALUES
    ($(sql "$team"), $(sql "$role"), $1, $(sql "$2"), $(sql "$(iso "$now")"));
    ${values:+INSERT INTO run_items (run, item) VALUES $values;}" >/dev/null ||
    log "$team $role: couldn't record run $1 in the history"
}

# settle_runs: records the end of each of the team's runs that has finished since the last pass, with
# its cost if its log got as far as a result.
settle_runs() {
  local id pid log result ended cost outcome
  [ -f "$STATE/history.db" ] || return 0
  while IFS='|' read -r id pid log; do
    kill -0 "$pid" 2>/dev/null && continue
    result=$(jq -cR 'fromjson? | select(.type == "result")' "$log" 2>/dev/null | tail -n 1)
    ended=$(stat -c %Y "$log" 2>/dev/null || stat -f %m "$log" 2>/dev/null || echo "$now")
    if [ -n "$result" ]; then
      cost=$(jq -r '.total_cost_usd // "NULL"' <<<"$result")
      outcome=$(jq -r 'if .is_error == true then "error" else "" end' <<<"$result")
    else
      cost=NULL outcome=died
    fi
    history_sql "UPDATE runs SET ended = $(sql "$(iso "$ended")"), cost = $cost,
      outcome = COALESCE(outcome, $([ -n "$outcome" ] && sql "$outcome" || echo NULL)) WHERE id = $id;" >/dev/null
  done < <(history_sql "SELECT id, pid, log FROM runs WHERE team = $(sql "$team") AND ended IS NULL;")
}

# over_budget <team> <config>: whether the team's runs today have reached its daily budget, which the log says once a day.
over_budget() {
  local today said file
  file="$STATE/$1/$($DRY_RUN && echo dry-)budget-said"
  today=$(budget_today "$1" "$2" "$now") && jq -e .reached <<<"$today" >/dev/null || return 1
  said=$(date +%F)
  [ "$said" = "$(cat "$file" 2>/dev/null)" ] && return
  mkdir -p "$STATE/$1"
  echo "$said" >"$file"
  log "$1: daily budget \$$(jq .budget <<<"$today") reached (\$$(printf '%.2f' "$(jq .cost <<<"$today")") today);\
 $($DRY_RUN && echo "would start ")no new runs until midnight"
}

# fresh <file> <reasons> <log>: whether these reasons are worth a run. The same ones as last time
# wait retryAfter, unless that run, logged to <log>, died before finishing. <file> holds
# "<fingerprint> <started>".
fresh() {
  local fingerprint tried at
  fingerprint=$(shasum <<<"$2" | cut -c1-12)
  read -r tried at 2>/dev/null <"$1"
  if [ "$fingerprint" = "${tried:-}" ] && [ $((now - ${at:-0})) -lt $(($(cfg '.dispatch.retryAfter // 60') * 60)) ] &&
    { $DRY_RUN || grep -q '"type":"result"' "$3" 2>/dev/null; }; then
    return 1
  fi
  echo "$fingerprint $now" >"$1"
}

# pick_task <team> <dir> <triggers>: the next task a Dev run is for, as {number, title, reasons},
# passing over the tasks in $picked and any stopped by task. A task already in hand comes first;
# only then is a Ready task claimed, before the run starts.
pick_task() {
  local row n claimed
  mkdir -p "$2/${prefix}tried"
  while IFS= read -r row; do
    n=$(jq -r .number <<<"$row")
    [[ $picked == *" $n "* ]] && continue
    [ -e "$2/runs/$n/held" ] && continue
    fresh "$2/${prefix}tried/$n" "$(jq -r '.reasons[]' <<<"$row")" "$2/runs/$n/latest.jsonl" &&
      { echo "$row"; return; }
  done < <(jq -c '.tasks[]?' <<<"$3")
  [ "$role" = dev ] && [ "$(jq -r '.ready // empty' <<<"$3")" != "" ] || return 0
  if ! claimed=$("$ROOT/bin/a-team" board ${prefix:+--dry-run} "$1" claim dev 2>"$2/claim.err"); then
    # Once a run has started this pass, the worktrees running out is expected.
    [ "$started" -gt 0 ] && grep -q 'no free worktree' "$2/claim.err" ||
      log "$1 dev: claim failed: $(tr '\n' ' ' <"$2/claim.err")"
    return 1
  fi
  [ "$claimed" = null ] || jq -c '. + {reasons: ["Ready task #\(.number) to build"]}' <<<"$claimed"
}

# run_release <team> <config>: release.sh, for a team that releases by itself, logging what it says when that
# changes.
run_release() {
  local said file="$STATE/$1/release-said" args=(release)
  jq -e '(.release // "never") != "never"' "$2" >/dev/null 2>&1 || return 0
  $DRY_RUN && args+=(--dry-run) && file="$STATE/$1/dry-release-said"
  said=$("$ROOT/bin/a-team" "${args[@]}" "$1" 2>&1) || said="failed: ${said#a-team release: }"
  [ "$said" = "$(cat "$file" 2>/dev/null)" ] && return
  log "$1 release: $said"
  mkdir -p "$STATE/$1"
  printf '%s\n' "$said" >"$file"
}

# cannot_run <team> <config>: why the team can't run, if it can't. It runs only as its own App.
cannot_run() {
  local why
  jq -e '.app.id' "$2" >/dev/null 2>&1 ||
    { echo "no GitHub App: run a-team app create $1, then install it"; return; }
  why=$("$ROOT/bin/a-team" token "$1" 2>&1 >/dev/null) || echo "${why#a-team token: }"
}

# keep_record: launchd's own pass keeps dispatcher.json true: its version after an upgrade, and all of it for a
# dispatcher installed before there was one.
keep_record() {
  local bin=${A_TEAM_BIN:-$ROOT/bin/a-team} record="$STATE/dispatcher.json" at want
  grep -qF "<array><string>$bin</string><string>dispatch</string>$($DRY_RUN && echo '<string>--dry-run</string>')</array>" \
    "$PLIST" 2>/dev/null || return 0
  at=$(jq -e .installedAt "$record" 2>/dev/null) || at=$(date +%s)
  want=$(dispatcher_record "$bin" "$DRY_RUN" "${A_TEAM_INTERVAL:-120}" "$at" | jq -c .)
  [ "$want" = "$(jq -c . "$record" 2>/dev/null)" ] || { jq . <<<"$want" >"$record.$$" && mv "$record.$$" "$record"; }
}

[ -n "${A_TEAM_UNSCHEDULED:-}" ] || keep_record

for team in $(team_names); do
  config=$(team_config "$team")
  run_release "$team" "$config"
  jq -e '.dispatch.enabled == true' "$config" >/dev/null 2>&1 || continue
  why=$(cannot_run "$team" "$config")
  if [ -n "$why" ]; then
    mkdir -p "$STATE/$team"
    failing "$STATE/$team/cannot-run" "$STATE/$team/failed-check" "$why" "$team: stopped"
    continue
  fi
  passing "$STATE/$team/cannot-run" "$STATE/$team/failed-check" "$team: running again"
  now=$(date +%s)
  $DRY_RUN || settle_runs
  over_budget=false
  over_budget "$team" "$config" && over_budget=true
  for role in $(team_roles "$config"); do
    dispatch "$team" "$role" "$config"
  done
done
exit 0
}
