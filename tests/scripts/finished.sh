#!/usr/bin/env bash
# A finished task reaching In review by itself.
# shellcheck source=tests/scripts/lib.sh
source "$(dirname "${BASH_SOURCE[0]}")/lib.sh"
# shellcheck disable=SC2034,SC2154 # helpers read what an area sets, and an area reads what lib.sh sets

KEEP_BOARD=1

team() {
  fixture <<JSON
{ "repo": "mentaldesk/demo", "reviewer": "reviewer", "app": { "id": 7, "slug": "demo-app" }, "project": { "owner": "mentaldesk", "number": 1 },
  "wip": { "worktrees": 3 }, "roles": { "reviewer": $1 } }
JSON
}

# `task [<draft>] [<status>]`: task #12 at <status> on a fresh board, its PR #912 green, out of draft unless <draft> is true.
task() {
  A_TEAM_STATE=$(mktemp -d "$WORK/state.XXXXXX")
  gh_items <<ITEMS
${2:-In_progress} 12 A finished task
ITEMS
  gh_runs <<'RUNS'
completed success 2025-09-19T09:00:00Z build
RUNS
  gh_pr 912 "${1:-false}"
}

status_of() { sqlite3 "$A_TEAM_STATE/history.db" "SELECT status FROM board WHERE team = 'demo' AND item = 12;"; }

# `finishes <what> <status> [<task>...]`: runs `finished`, and #12 should end at <status>.
finishes() {
  local what=$1 expected=$2
  shift 2
  run board demo finished "$@"
  same "$what: exit" 0 "$STATUS"
  same "$what: status" "$expected" "$(status_of)"
}

said() { jq --arg body "$1" --arg login "$2" '(.. | objects | select(has("isDraft"))).comments.nodes += [{body: $body, author: {__typename: "Bot", login: $login}}]' "$PRS" >"$PRS.new" && mv "$PRS.new" "$PRS"; gh_reviewing; }
dev_answers() { said "Done.

<!-- a-team:dev -->" demo-app; }

UNREVIEWED="This left draft before the Reviewer read it, so it's reached you unreviewed.

<!-- a-team:dispatch -->"

case_ "a green task out of draft that the Reviewer never read moves to In review, saying so once"
team true
task
finishes "waiting" "In review"
same "said" "#12: PR #912 is finished, In progress → In review" "$(cat "$OUT")"
same "comment" "$UNREVIEWED" "$(cat "$POSTED")"
: >"$POSTED"
finishes "second pass" "In review"
same "second comment" "" "$(cat "$POSTED")"

case_ "a task moved this way waits on the stakeholder like any other finished task, and can be accepted"
task
finishes "moved" "In review"
gh_talk <<TALK
12 body ${TODAY}T08:14:00Z demo-app[bot] The task\n<!-- a-team:lead -->
TALK
waiting
same "waiting" '[[12,"In review"]]' "$(jq -c '[.[] | select(has("number")) | [.number, .status]]' "$OUT")"
echo '{"head": {"sha": "deadbeefcafe", "ref": "feat/task", "repo": {"full_name": "mentaldesk/demo"}}}' >"$PULL"
run board demo accept you 12
same "accepted" "#12: merged PR #912" "$(cat "$OUT")"

case_ "a pass after the comment went up and the move didn't doesn't say it again"
task
said "$UNREVIEWED" demo-app
finishes "said already" "In review"
same "comment" "" "$(cat "$POSTED")"

case_ "the same words from anyone but the dispatcher don't count as said"
task
said "This left draft before the Reviewer read it, so it's reached you unreviewed." demo-app
finishes "no marker" "In review"
same "comment" "$UNREVIEWED" "$(cat "$POSTED")"

case_ "with no Reviewer, the task reaches In review with nothing said"
team false
task
finishes "off" "In review"
same "comment" "" "$(cat "$POSTED")"

case_ "a posted review keeps the task In progress and starts the Dev, until it's answered and green"
team true
task
gh_reviewed
finishes "posted" "In progress"
same "comment" "" "$(cat "$POSTED")"
run board demo triggers dev
same "dev reasons" '["PR #912 has its review: act on it"]' "$(jq -c .reasons "$OUT")"
dev_answers
run board demo triggers dev
same "answered, no Dev run" '[]' "$(jq -c .reasons "$OUT")"
gh_runs <<'RUNS'
in_progress - - build
RUNS
finishes "answered, CI running" "In progress"
gh_runs <<'RUNS'
completed success 2025-09-19T09:00:00Z build
RUNS
finishes "answered" "In review"
same "comment" "" "$(cat "$POSTED")"

case_ "a posted review on a task already In review starts no Dev run"
task false In_review
gh_reviewed
run board demo triggers dev
same "dev reasons" '[]' "$(jq -c .reasons "$OUT")"

case_ "a failing or running PR stays In progress, whatever its review"
for runs in "completed failure 2025-09-19T09:00:00Z build" "in_progress - - build"; do
  task
  gh_runs <<<"$runs"
  finishes "$runs" "In progress"
  same "$runs: comment" "" "$(cat "$POSTED")"
done

case_ "a PR that conflicts, or that GitHub hasn't worked out yet, stays In progress"
for mergeable in CONFLICTING UNKNOWN; do
  task
  jq --arg m "$mergeable" '(.. | objects | select(has("isDraft"))).mergeable = $m' "$PRS" >"$PRS.new" && mv "$PRS.new" "$PRS"
  finishes "$mergeable" "In progress"
done

case_ "a task a Dev run is on, or that's held, stays until that's over"
task
finishes "live" "In progress" 11 12
same "comment" "" "$(cat "$POSTED")"
printf 'Which one?\n' >"$WORK/question"
run board demo hold dev 12 "$WORK/question"
same "hold" 0 "$STATUS"
: >"$POSTED"
finishes "held" "In progress"
same "held comment" "" "$(cat "$POSTED")"

case_ "a draft is left as it is"
task true
finishes "draft" "In progress"
same "comment" "" "$(cat "$POSTED")"
run board demo triggers reviewer
same "reviewer reasons" '["PR #912 is green and waiting for its review"]' "$(jq -c .reasons "$OUT")"

case_ "--dry-run says what it would do and changes nothing"
task
run board demo sync
: >"$POSTED"
run board --dry-run demo finished
same "exit" 0 "$STATUS"
same "said" "(dry run) #12: PR #912 is finished, In progress → In review" "$(cat "$OUT")"
grep -qF "would set #12 to 'In review'" "$ERR" || fail "dry run: '$(cat "$ERR")'"
grep -qF "would comment on #912" "$ERR" || fail "dry run: '$(cat "$ERR")'"
same "status" "In progress" "$(status_of)"
same "comment" "" "$(cat "$POSTED")"

case_ "a PR on the board, and an In progress task that isn't the Dev's, are left alone"
task
edit_item 12 '.__typename = "PullRequest"'
finishes "PR card" "In progress"
same "PR card comment" "" "$(cat "$POSTED")"
task
edit_item 12 '.labels.nodes = []'
finishes "not the Dev's" "In progress"
same "not the Dev's comment" "" "$(cat "$POSTED")"

case_ "anything but task numbers is refused"
run board demo finished twelve
failed "words"
grep -qF "usage: board.sh demo finished" "$ERR" || fail "usage: '$(cat "$ERR")'"

case_ "the process names the dispatcher for this move, and the Dev's role says what a posted review out of draft needs"
grep -q '^| In review ⛔ .*the dispatcher for a finished task' "$ROOT/process.md" || fail "process.md doesn't name the dispatcher"
grep -q 'On a PR already out of draft, do the same' "$ROOT/roles/dev.md" || fail "roles/dev.md doesn't say"

finish_suite
