#!/usr/bin/env bash
#
# dispatch.sh [--dry-run] — starts a headless session for each team role that has work.
# launchd runs it every couple of minutes (see install.sh). Deciding whether there is work is
# a plain command (a-team board <team> triggers); Claude only starts when there is.
#
set -uo pipefail

# Everything is inside one { } block, so bash reads the whole file before running any of it and
# a release that replaces this file mid-pass can't mix old and new lines.
{

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
source "$ROOT/scripts/common.sh"
DRY_RUN=false dry=''
[ "${1:-}" = --dry-run ] && DRY_RUN=true dry=--dry-run
mkdir -p "$STATE"
# While this holds a live pid, no other pass starts, and an overdue next-pass is a slow pass, not a stopped dispatcher.
PASS="$STATE/$($DRY_RUN && echo dry-)pass"
NEXT="$STATE/$($DRY_RUN && echo dry-)next-pass"
# launchd counts StartInterval from when a pass ends, not from when it started. A pass run off schedule, from the app,
# leaves the scheduled one's countdown alone.
pass_ended() {
  release_pid "$PASS"
  [ -n "${A_TEAM_UNSCHEDULED:-}" ] || echo $(($(date +%s) + ${A_TEAM_INTERVAL:-120})) >"$NEXT"
}
trap pass_ended EXIT
if ! running=$(claim_pid "$PASS") && [ -n "$running" ]; then
  # Only the app asks: the app reads any scheduled output as a broken pass (DispatcherState.Broke).
  [ -z "${A_TEAM_UNSCHEDULED:-}" ] || echo "A pass is already running."
  exit 0
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

# short_read <output> <team role>: logs the board's read short twice in one line; fails on any other output.
short_read() {
  [[ $1 =~ incomplete\ \(([0-9]+\ of\ [0-9]+)\ items\) ]] || return 1
  log "$2: board read incomplete (${BASH_REMATCH[1]} items), twice; trying again next check"
}

dispatch() {
  local team=$1 role=$2 config=$3
  local dir="$STATE/$team/$role" now triggers reasons creative last live limit pid at tries
  local sweep='' prefix='' task='' number='' started=0 picked=' ' chores='' items='' card=''
  $DRY_RUN && prefix="dry-"
  mkdir -p "$dir/logs"
  now=$(date +%s)
  cfg() { jq -r "$1" "$config"; }

  $DRY_RUN || prune_releases "$dir"
  live=$(live_runs "$dir")
  limit=1
  [ "$role" = dev ] && limit=$(cfg '.wip.devs // 1')
  check_runs
  $held && return
  [ "$(grep -c . <<<"$live")" -lt "$limit" ] || return
  jq -e --arg role "$role" '.dispatch.hold // [] | index($role)' "$config" >/dev/null 2>&1 && return

  # A missing last-sweep reads as never, so a fresh install sweeps on its first pass.
  [ $((now - $(cat "$dir/${prefix}last-sweep" 2>/dev/null || echo 0))) \
    -ge $(($(cfg '.dispatch.sweepEvery // 30') * 60)) ] && sweep=--sweep

  if ! triggers=$("$ROOT/bin/a-team" board "$team" triggers "$role" ${sweep:+"$sweep"} 2>&1); then
    short_read "$triggers" "$team $role" && return
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
      rm -f "$dir/${prefix}gave-up"
      forget_tries "$dir/${prefix}fingerprint"
      creative_due || return
      reasons=$SCHEDULED
    fi
    items=$(jq -r '.items[]?' <<<"$triggers")
    fresh "$dir/${prefix}fingerprint" "$reasons" "$dir/latest.jsonl"
    case $? in
      1) return ;;
      2)
        give_up_role
        [ -f "$dir/${prefix}gave-up" ] && creative_due || return
        reasons=$SCHEDULED items=''
        launch
        return
        ;;
    esac
    card=$(jq -c 'select(.card) | {number: .card}' <<<"$triggers")
    launch
    return
  fi

  # Tasks that already have a live run never get a second one.
  picked+="$(cut -d' ' -f4 <<<"$live" | tr '\n' ' ')"
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

# check_runs: sends the kill to each of the role's runs past its limit, logging that once, and keeps <dir>/stuck: the
# runs still alive on the pass after their kill, or on a task finished for more than 30 minutes. It only reports.
check_runs() {
  local maximum killed was pid at file number closed why says over waiting holds
  local sent='' stuck='[]' count=0 said="$dir/${prefix}killed" marker="$dir/${prefix}stuck"
  maximum=$(($(cfg '.dispatch.maxRuntime // 120') * 60))
  killed=" $(cat "$said" 2>/dev/null | paste -sd ' ' -) "
  was=$(jq -r '.[]?.pid' "$marker" 2>/dev/null)
  while read -r pid at file number; do
    [ -n "$pid" ] || continue
    why=''
    if [ $((now - at)) -gt "$maximum" ]; then
      if kill "$pid" && [[ $killed != *" $pid "* ]]; then
        log "$team $role: killed run $pid after $(((now - at) / 60)) minutes"
        run_outcome "$team" "$role" "$pid" killed
      fi
      sent+="$pid"$'\n'
      if [[ $killed == *" $pid "* ]]; then
        over=$((now - at - maximum))
        why="outlived its $(minutes_said $((maximum / 60))) limit"
        says="run $pid $why by $(age_said "$over")"
      fi
    fi
    # Past its limit, only a run already stuck on its task stays so, through the pass that sends its kill.
    if [ -z "$why" ] && { [ $((now - at)) -le "$maximum" ] || grep -qx "$pid" <<<"$was"; } &&
      [ -n "$number" ] && closed=$(finished_at "$team" "$number") && [ $((now - (closed > at ? closed : at))) -gt 1800 ]; then
      why="on #$number, which closed $(day_said "$closed")"
      says="run $pid is on #$number, closed $(day_said "$closed")"
    fi
    [ -n "$why" ] || continue
    count=$((count + 1))
    stuck=$(jq -c --argjson pid "$pid" --arg file "$file" --arg says "$says" --arg why "$why" '. + [{$pid, $file, $says, $why}]' <<<"$stuck")
  done <<<"$live"
  if [ -n "$sent" ]; then printf '%s' "$sent" >"$said"; else rm -f "$said"; fi

  while read -r pid; do
    [ -z "$pid" ] || jq -e --argjson pid "$pid" 'any(.[]; .pid == $pid)' <<<"$stuck" >/dev/null ||
      log "$team $role: run $pid no longer stuck"
  done <<<"$was"
  if [ "$count" -eq 0 ]; then
    rm -f "$marker"
    return
  fi
  while IFS=$'\t' read -r pid why; do
    grep -qx "$pid" <<<"$was" || log "$team $role: run $pid is stuck: $why"
  done < <(jq -r '.[] | [.pid, .why] | @tsv' <<<"$stuck")

  if [ "$limit" -eq 1 ]; then holds="holds its only slot"; else holds="holds $count of $limit slots"; fi
  if [ "$(grep -c . <<<"$live")" -ge "$limit" ] && waiting=$(waiting_said); then holds+=", $waiting"; fi
  jq -c --arg holds "$holds" 'map({pid, file, says: "\(.says) · \($holds)"})' <<<"$stuck" >"$marker.$$" && mv "$marker.$$" "$marker"
}

# waiting_said: how much work is waiting for this role's slots, as "3 PRs waiting". Fails if none is, or it can't tell.
waiting_said() {
  local triggers n noun=thing
  triggers=$("$ROOT/bin/a-team" board "$team" triggers "$role" 2>/dev/null) || return 1
  case $role in
    reviewer) noun=PR ;;
    dev) noun=task ;;
  esac
  n=$(jq -r --arg live "$(cut -d' ' -f4 <<<"$live" | paste -sd ' ' -)" '($live | split(" ")) as $on
    | if has("tasks") then ([.tasks[]? | select((.number | tostring) as $n | $on | index($n) | not)] | length) + (.readyCount // 0)
      else .reasons | length end' <<<"$triggers") || return 1
  [ "${n:-0}" -gt 0 ] || return 1
  [ "$n" -eq 1 ] || noun+=s
  echo "$n $noun waiting"
}

# finished_at <team> <n>: when the board's record says #<n> closed or went Done, in epoch seconds. Fails if it doesn't say so.
finished_at() {
  local at
  [ -f "$STATE/history.db" ] || return 1
  at=$(history_sql "SELECT strftime('%s', since) FROM board WHERE team = $(sql "$1") AND item = $2
    AND (status = 'Done' OR json_extract(content, '$.shut'));" 2>/dev/null) || return 1
  [[ $at =~ ^[0-9]+$ ]] && echo "$at"
}

# minutes_said <minutes>: 2h, 1h30m or 45m.
minutes_said() {
  if [ $(($1 % 60)) -ne 0 ] && [ "$1" -gt 60 ]; then echo "$(($1 / 60))h$(($1 % 60))m"
  elif [ "$1" -ge 60 ]; then echo "$(($1 / 60))h"
  else echo "$1m"
  fi
}

# age_said <seconds>: in the largest unit, as Overseer's chips say it: 12m, 3h, 19d.
age_said() {
  if [ "$1" -lt 3600 ]; then echo "$(($1 / 60))m"
  elif [ "$1" -lt 86400 ]; then echo "$(($1 / 3600))h"
  else echo "$(($1 / 86400))d"
  fi
}

# day_said <epoch seconds>: that day, as 29 Oct.
day_said() { LC_ALL=C date -r "$1" '+%-d %b' 2>/dev/null || LC_ALL=C date -d "@$1" '+%-d %b'; }

# live_runs <dir>: "<pid> <started> <its pid file, under dir> <task or card>" for each of the role's runs still going, oldest
# first. Removes the pid file of each run that has ended, before the OS gives its pid to another process. A run's own
# pid file comes first: the role's follows whichever run started last.
live_runs() {
  local file pid seen=' ' at from
  for file in "$1"/runs/*/pid "$1/pid"; do
    if ! pid=$(live_pid "$file"); then
      [ -s "$file" ] && ! $DRY_RUN && rm -f "$file"
      continue
    fi
    [[ $seen == *" $pid "* ]] && continue
    seen+="$pid "
    from=$(dirname "$file")
    at=$(cat "$from/last-start" 2>/dev/null || echo "$now")
    echo "$pid $at ${file#"$1"/} $({ cat "$from/task" || cat "$from/card"; } 2>/dev/null | jq -r '.number // empty' 2>/dev/null)"
  done | sort -k2n
}

# prune_releases <dir>: removes the release copy of each of the role's runs that has finished.
prune_releases() {
  local release
  for release in "$1/release" "$1"/runs/*/release; do
    [ -d "$release" ] || continue
    live_pid "$(dirname "$release")/pid" >/dev/null || rm -rf "$release"
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
  if [ -x "$ROOT/libexec/a-team-core" ]; then
    mkdir -p "$1/libexec" && cp "$ROOT/libexec/a-team-core" "$1/libexec/" || return
  fi
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
    alive "$pid" "$log" && continue
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

# over_budget <team> <config>: whether the team's runs today have reached its daily budget, which the log says once a
# day. A budget that isn't a whole number, or a history it can't read, holds nothing back, and the log says that too.
over_budget() {
  local today said why='' file
  file="$STATE/$1/$($DRY_RUN && echo dry-)budget"
  mkdir -p "$STATE/$1"
  if jq -e '.dispatch.budget | numbers | . != floor' "$2" >/dev/null; then
    why="\$$(jq .dispatch.budget "$2") isn't a whole number of dollars"
  elif ! today=$(budget_today "$1" "$2" "$now" 2>&1); then
    why="couldn't read today's cost: $(tr '\n' ' ' <<<"$today")"
  fi
  if [ -n "$why" ]; then
    failing "$file-unusable" "$file-unusable-once" "$why" "$1: daily budget ignored"
    return 1
  fi
  passing "$file-unusable" "$file-unusable-once" "$1: daily budget in use again"
  jq -e .reached <<<"$today" >/dev/null || return 1
  said=$(date +%F)
  [ "$said" = "$(cat "$file-said" 2>/dev/null)" ] && return
  echo "$said" >"$file-said"
  log "$1: daily budget \$$(jq .budget <<<"$today") reached (\$$(printf '%.2f' "$(jq .cost <<<"$today")") today);\
 $($DRY_RUN && echo "would start ")no new runs until midnight"
}

# The Lead's run on a timer, the same words each time however healthy, which is never counted as a try.
SCHEDULED="it's been a while: time to pitch or discover"

# creative_due: whether it's time for the role's run on a timer.
creative_due() { [ "$creative" = true ] && [ $((now - last)) -ge $(($(cfg '.dispatch.creativeEvery // 180') * 60)) ]; }

# ended_in_error <log>: whether the run logged there ended in an API failure or a usage limit, so it never got to try.
ended_in_error() { [ "$(jq -R 'fromjson? | select(.type == "result") | .is_error == true' "$1" 2>/dev/null | tail -n 1)" = true ]; }

# retry_wait <tries>: seconds before the next try, 15 minutes after the first and an hour after the rest, never above retryAfter.
retry_wait() {
  local wait=60 longest
  longest=$(cfg '.dispatch.retryAfter // 60')
  [ "$1" -gt 1 ] || wait=15
  echo $(((wait < longest ? wait : longest) * 60))
}

# fresh <file> <reasons> <log> [<head>]: whether these reasons are worth a run: 0 if so, 1 to wait, 2 if the tries have run out, with
# $tries saying how many they were. A run that finished and left the reasons as they were is a try; one logged to <log> that
# died before finishing, ended in an error, or started before the board last moved to a new format, which stopped it, isn't.
# Whether other checks are still running doesn't make the reasons new; a new <head> of the task's PR does, whatever they say.
# <file> holds "<fingerprint> <started> <tries> <head>".
fresh() {
  local fingerprint tried at head
  fingerprint=$(sed 's/, other checks still running$//' <<<"$2" | shasum | cut -c1-12)
  tries=''
  read -r tried at tries head 2>/dev/null <"$1"
  tries=${tries:-1}
  if [ "$fingerprint" != "${tried:-}" ] || { [ -n "${4:-}" ] && [ -n "${head:-}" ] && [ "$4" != "$head" ]; }; then
    tries=1
  elif [ -f "$dir/${prefix}gave-up" ]; then
    return 2
  elif [ "${at:-0}" -gt "$moved" ] && { $DRY_RUN || grep -q '"type":"result"' "$3" 2>/dev/null; }; then
    if [ "$2" = "$SCHEDULED" ] || ended_in_error "$3"; then
      [ $((now - ${at:-0})) -ge $(($(cfg '.dispatch.retryAfter // 60') * 60)) ] || return 1
    else
      [ "$tries" -lt "$(cfg '.dispatch.maxRuns // 3')" ] || return 2
      [ $((now - ${at:-0})) -ge "$(retry_wait "$tries")" ] || return 1
      tries=$((tries + 1))
    fi
  fi
  [ "$2" != "$SCHEDULED" ] || tries=0
  rm -f "$dir/${prefix}gave-up"
  echo "$fingerprint $now $tries ${4:-}" >"$1"
}

# forget_tries <file>: these reasons have gone, so if they come back they start from nothing.
forget_tries() {
  local tried at
  read -r tried at _ 2>/dev/null <"$1" && echo "$tried $at 0" >"$1"
}

# give_up <n> <reasons>: holds #<n> for the stakeholder, as the team's App, saying what its $tries runs were for.
give_up() {
  local said file
  file=$(mktemp "$dir/${prefix}give-up.XXXXXX")
  printf '%s\n' "$2" >"$file"
  if said=$(PATH="$ROOT/bin:$PATH" A_TEAM_RUN_TEAM=$team "$ROOT/bin/a-team" board ${dry:+"$dry"} "$team" give-up "$role" "$1" "$tries" "$file" 2>&1); then
    rm -f "$file"
    passing "$dir/${prefix}give-up-failing" "$dir/${prefix}give-up-failed-once" "$team $role: giving up working again"
    log "$team $role: ${said//$'\n'/; }"
  else
    rm -f "$file"
    failing "$dir/${prefix}give-up-failing" "$dir/${prefix}give-up-failed-once" "${said//$'\n'/ }" "$team $role: couldn't give up on #$1"
    return 1
  fi
}

# give_up_role: holds each item the role's reasons name. Reasons that name none can't be held, so the role says it gave up.
give_up_role() {
  local item failed=false
  for item in $(sort -un <<<"$items"); do
    give_up "$item" "$reasons" || failed=true
  done
  if [ -n "$items" ] && ! $failed; then
    $DRY_RUN || rm -f "$dir/${prefix}fingerprint"
    return
  fi
  [ -f "$dir/${prefix}gave-up" ] || log "$team $role: gave up after $tries runs: $(paste -sd ';' - <<<"$reasons")"
  printf '%s\n%s\n' "$tries" "$reasons" >"$dir/${prefix}gave-up"
}

# pick_task <team> <dir> <triggers>: the next task a Dev run is for, as {number, title, reasons},
# passing over the tasks in $picked and any stopped by task. A task already in hand comes first;
# only then is a Ready task claimed, before the run starts.
pick_task() {
  local row n claimed why
  mkdir -p "$2/${prefix}tried"
  while IFS= read -r row; do
    n=$(jq -r .number <<<"$row")
    [[ $picked == *" $n "* ]] && continue
    [ -e "$2/runs/$n/held" ] && continue
    why=$(jq -r '.reasons[]' <<<"$row")
    fresh "$2/${prefix}tried/$n" "$why" "$2/runs/$n/latest.jsonl" "$(jq -r '.head // empty' <<<"$row")"
    case $? in
      0) echo "$row"; return ;;
      2) give_up "$n" "$why" && { $DRY_RUN || rm -f "$2/tried/$n"; } ;;
    esac
  done < <(jq -c '.tasks[]?' <<<"$3")
  [ "$role" = dev ] && [ "$(jq -r '.ready // empty' <<<"$3")" != "" ] || return 0
  if ! claimed=$("$ROOT/bin/a-team" board ${prefix:+--dry-run} "$1" claim dev 2>"$2/claim.err"); then
    # Once a run has started this pass, the worktrees running out is expected.
    short_read "$(<"$2/claim.err")" "$1 dev" || { [ "$started" -gt 0 ] && grep -q 'no free worktree' "$2/claim.err"; } ||
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

# settle_bumps <team>: merges the team's green Dependabot PRs and puts its red ones with the Dev, as its App, so each
# stands as the rest of the pass reads it. A merge GitHub keeps refusing is logged once.
settle_bumps() {
  local args=(board) said
  $DRY_RUN && args+=(--dry-run)
  mkdir -p "$STATE/$1"
  if said=$(PATH="$ROOT/bin:$PATH" A_TEAM_RUN_TEAM=$1 "$ROOT/bin/a-team" "${args[@]}" "$1" bumps 2>"$STATE/$1/bumps.err"); then
    passing "$STATE/$1/bumps-failing" "$STATE/$1/bumps-failed-once" "$1: settling dependency bumps working again"
  else
    failing "$STATE/$1/bumps-failing" "$STATE/$1/bumps-failed-once" "$(tr '\n' ' ' <"$STATE/$1/bumps.err")" "$1: settling dependency bumps failed"
  fi
  [ -z "$said" ] || log "$1: ${said//$'\n'/; }"
}

# free_waits <team>: frees the team's tasks whose wait on another repo is met, commenting as its App.
free_waits() {
  local args=(board) said
  $DRY_RUN && args+=(--dry-run)
  mkdir -p "$STATE/$1"
  if said=$(PATH="$ROOT/bin:$PATH" A_TEAM_RUN_TEAM=$1 "$ROOT/bin/a-team" "${args[@]}" "$1" free 2>"$STATE/$1/free.err"); then
    passing "$STATE/$1/free-failing" "$STATE/$1/free-failed-once" "$1: freeing tasks working again"
    [ -z "$said" ] || log "$1: ${said//$'\n'/; }"
  else
    failing "$STATE/$1/free-failing" "$STATE/$1/free-failed-once" "$(tr '\n' ' ' <"$STATE/$1/free.err")" "$1: freeing tasks failed"
  fi
}

# update_branches <team>: merges the base into each of the team's PRs In review that's behind it, as its App so CI runs.
update_branches() {
  local args=(board) said
  $DRY_RUN && args+=(--dry-run)
  mkdir -p "$STATE/$1"
  if said=$(PATH="$ROOT/bin:$PATH" A_TEAM_RUN_TEAM=$1 "$ROOT/bin/a-team" "${args[@]}" "$1" update 2>&1); then
    passing "$STATE/$1/update-failing" "$STATE/$1/update-failed-once" "$1: updating branches working again"
    [ -z "$said" ] || log "$1: ${said//$'\n'/; }"
  else
    failing "$STATE/$1/update-failing" "$STATE/$1/update-failed-once" "${said//$'\n'/ }" "$1: updating branches failed"
  fi
}

# finish_tasks <team> <config>: moves the team's finished tasks to In review, as its App, but for one a Dev run or a stopped run has.
finish_tasks() {
  local dir="$STATE/$1/dev" args=(board) said n file
  jq -e '.dispatch.hold // [] | index("dev")' "$2" >/dev/null 2>&1 && return
  $DRY_RUN && args+=(--dry-run)
  args+=("$1" finished)
  for n in $(live_runs "$dir" | cut -d' ' -f4); do args+=("$n"); done
  for file in "$dir"/runs/*/held; do
    [ ! -e "$file" ] || args+=("$(basename "$(dirname "$file")")")
  done
  mkdir -p "$STATE/$1"
  if said=$(PATH="$ROOT/bin:$PATH" A_TEAM_RUN_TEAM=$1 "$ROOT/bin/a-team" "${args[@]}" 2>"$STATE/$1/finished.err"); then
    passing "$STATE/$1/finished-failing" "$STATE/$1/finished-failed-once" "$1: moving finished tasks working again"
    [ -z "$said" ] || log "$1: ${said//$'\n'/; }"
  else
    failing "$STATE/$1/finished-failing" "$STATE/$1/finished-failed-once" "$(tr '\n' ' ' <"$STATE/$1/finished.err")" "$1: moving finished tasks failed"
  fi
}

# sync_board <team>: brings the team's record up to date with GitHub, the cards' detail too, so the app's is a pass old at most.
sync_board() {
  local said
  mkdir -p "$STATE/$1"
  if said=$("$ROOT/bin/a-team" board ${dry:+"$dry"} "$1" sync 2>&1); then
    passing "$STATE/$1/sync-failing" "$STATE/$1/sync-failed-once" "$1: syncing the board working again"
  else
    failing "$STATE/$1/sync-failing" "$STATE/$1/sync-failed-once" "${said//$'\n'/ }" "$1: syncing the board failed"
  fi
}

# cannot_run <team> <config>: why the team can't run, if it can't. It runs only as its own App.
cannot_run() {
  local why
  jq -e '.app.id' "$2" >/dev/null 2>&1 ||
    { echo "no GitHub App: run a-team app create $1, then install it"; return; }
  why=$("$ROOT/bin/a-team" token "$1" 2>&1 >/dev/null) || { echo "${why#a-team token: }"; return; }
  why=$("$ROOT/bin/a-team" board ${dry:+"$dry"} "$1" format 2>&1 >/dev/null) || echo "${why#board.sh: }"
}

# format_moved <team>: when its board last moved to a new format, in epoch seconds; 0 if it never has.
format_moved() {
  [ -f "$STATE/history.db" ] || { echo 0; return; }
  history_sql "SELECT COALESCE((SELECT strftime('%s', moved) FROM formats WHERE team = $(sql "$1")), 0);" || echo 0
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
  moved=$(format_moved "$team")
  settle_bumps "$team"
  free_waits "$team"
  if adopted=$("$ROOT/bin/a-team" board ${dry:+"$dry"} "$team" adopt 2>&1); then
    passing "$STATE/$team/adopt-failing" "$STATE/$team/adopt-failed-once" "$team: adopting upstream PRs again"
    [ -z "$adopted" ] || log "$team: ${adopted//$'\n'/; }"
  else
    failing "$STATE/$team/adopt-failing" "$STATE/$team/adopt-failed-once" "${adopted//$'\n'/ }" "$team: adopt failed"
  fi
  update_branches "$team"
  sync_board "$team"
  now=$(date +%s)
  $DRY_RUN || settle_runs
  finish_tasks "$team" "$config"
  held=false
  over_budget "$team" "$config" && held=true
  for role in $(team_roles "$config"); do
    dispatch "$team" "$role" "$config"
  done
done
exit 0
}
