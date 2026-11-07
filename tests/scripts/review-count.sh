#!/usr/bin/env bash
# Review counts, waiting on other repos, and the release workflows.
# shellcheck source=tests/scripts/lib.sh
source "$(dirname "${BASH_SOURCE[0]}")/lib.sh"
# shellcheck disable=SC2034,SC2154 # helpers read what an area sets, and an area reads what lib.sh sets
app_fakes

case_ "the board counts each review fixture's Worth considering points as the dashboard reads them"
fixture <<'JSON'
{ "repo": "mentaldesk/demo", "reviewer": "reviewer", "app": { "id": 7, "slug": "demo-app" }, "project": { "owner": "mentaldesk", "number": 1 },
  "wip": { "worktrees": 1 } }
JSON
for fixture in "$ROOT"/tests/Dashboard.Tests/fixtures/reviews/*.md; do
  name=$(basename "$fixture" .md)
  review=$(awk 'NR > 1 { printf "\\n" } { printf "%s", $0 }' "$fixture")
  gh_items <<'ITEMS'
In_review 115 Reviewed already
ITEMS
  gh_talk <<TALK
115 body ${TODAY}T08:00:00Z demo-app[bot] The task\n<!-- a-team:lead -->
115 pr-body ${TODAY}T08:25:00Z demo-app[bot] Closes #115\n<!-- a-team:dev -->
115 pr-comment ${TODAY}T08:40:00Z demo-app[bot] $review\n\n<!-- a-team:reviewer -->
TALK
  waiting
  same "$name" "${name##*-}" "$(jq -c '.[0].consider' "$OUT")"
done


# Where each upstream item has got to, from lines of "<n> <Issue|PullRequest> <OPEN|CLOSED|MERGED> [<merge commit>|notplanned]".
# An issue closed by hand closed on 15 Oct, between the fixture releases v0.3.8 and v0.3.9.
upstream_items() {
  jq -R -s 'split("\n") | map(select(length > 0) | split(" ")) | map({key: "i\(.[0])", value:
    (if .[1] == "PullRequest"
     then {__typename: "PullRequest", state: .[2], merged: (.[2] == "MERGED"), mergeCommit: (if .[3] then {oid: .[3]} else null end)}
     else (.[3] == "notplanned") as $dropped | (if $dropped then null else .[3] end) as $sha
       | {__typename: "Issue", state: .[2], stateReason: (if $dropped then "NOT_PLANNED" elif .[2] == "CLOSED" then "COMPLETED" else null end),
          closedAt: (if .[2] == "CLOSED" then "2026-10-15T18:00:00Z" else null end), closedByPullRequestsReferences: {nodes: []},
          timelineItems: {nodes: (if $sha then [{closer: {__typename: "PullRequest", merged: true, mergeCommit: {oid: $sha}}}] else [] end)}} end)})
    | {data: {repository: from_entries}}' >"$UPSTREAM_ITEMS"
}

# The upstream repo's releases, from lines of "<tag> <published> [pre]", and which tags contain the merge commit.
releases() {
  jq -R -s 'split("\n") | map(select(length > 0) | split(" ")
    | {tag_name: .[0], published_at: .[1], prerelease: (.[2] == "pre"), draft: false})' >"$RELEASES"
}

waiting_upstream() {
  fixture <<'JSON'
{ "repo": "mentaldesk/demo", "reviewer": "reviewer", "app": { "id": 7, "slug": "demo-app" }, "project": { "owner": "mentaldesk", "number": 1 },
  "wip": { "worktrees": 3 } }
JSON
  gh_items <<'ITEMS'
Building 10 A pitch
Ready 11 Needs the Mac keys
ITEMS
  edit_item 11 '.labels.nodes = [{name: "a-team:upstream"}]'
  echo '[{"number": 11}]' >"$WAITING_ON"
}

case_ "a task can wait on another repo's issue until it's released: GitHub links them, and the task says so"
waiting_upstream
edit_item 11 '.labels.nodes = []'
echo '{"id": 9084, "number": 84}' >"$ISSUE"
run board demo depends dev 11 mentaldesk/style#84 released "needs the Mac word keys in MentalDesk.Tui"
same "exit" 0 "$STATUS"
same "said" "#11 now waits for mentaldesk/style#84 to be released, and said why on #11" "$(cat "$OUT")"
grep -q "^POST .*/issues/11/dependencies/blocked_by -F issue_id=9084" "$WRITES" || fail "depends upstream: no link in '$(cat "$WRITES")'"
same "depends upstream" '["mentaldesk/style#84"]' "$(marks 11 | jq -c .upstream)"
grep -q "label" "$WRITES" && fail "depends upstream: labelled in '$(cat "$WRITES")'"
grep -qx "Blocked by mentaldesk/style#84, until released: needs the Mac word keys in MentalDesk.Tui" "$POSTED" ||
  fail "depends upstream: '$(cat "$POSTED")'"
grep -qF '<!-- a-team:wait repo=mentaldesk/style n=84 until=released -->' "$POSTED" || fail "depends upstream: no wait in '$(cat "$POSTED")'"
run board demo depends dev 11 mentaldesk/style#84 released MentalDesk.Tui "needs the Mac word keys"
same "package exit" 0 "$STATUS"
grep -qx "Blocked by mentaldesk/style#84, until released: needs the Mac word keys" "$POSTED" || fail "package: '$(cat "$POSTED")'"
grep -qF '<!-- a-team:wait repo=mentaldesk/style n=84 until=released package=MentalDesk.Tui -->' "$POSTED" ||
  fail "package: no package in '$(cat "$POSTED")'"
grep -q "GitHub can't link" "$POSTED" && fail "depends upstream: said it couldn't link"
grep -q '<!-- a-team:dev -->' "$POSTED" || fail "depends upstream: no dev marker"
same "recorded" "dev blocked by mentaldesk/style#84" "$(history_table 11 | head -1)"

case_ "a wait on an upstream PR, or one GitHub won't link, is recorded on the task alone"
: >"$WRITES"
echo '{"id": 9085, "number": 85, "pull_request": {}}' >"$ISSUE"
run board demo depends lead 11 mentaldesk/style#85 closed "the guide's PR has the new rule"
same "exit" 0 "$STATUS"
grep -q "dependencies/blocked_by" "$WRITES" && fail "upstream PR: linked as a dependency"
grep -qx "Blocked by mentaldesk/style#85, until closed: the guide's PR has the new rule" "$POSTED" || fail "upstream PR: '$(cat "$POSTED")'"
grep -q "GitHub can't link mentaldesk/style#85 as a dependency, so the wait is recorded here." "$POSTED" || fail "upstream PR: no fallback in '$(cat "$POSTED")'"
grep -qF '<!-- a-team:wait repo=mentaldesk/style n=85 until=closed -->' "$POSTED" || fail "upstream PR: no wait"
: >"$WRITES"
echo '{"id": 9084, "number": 84}' >"$ISSUE"
touch "$BIN/link-refused"
run board demo depends dev 11 mentaldesk/style#84 closed "needs it"
same "refused link exit" 0 "$STATUS"
grep -q "GitHub can't link mentaldesk/style#84" "$POSTED" || fail "refused link: no fallback in '$(cat "$POSTED")'"
same "refused link" '["mentaldesk/style#84"]' "$(marks 11 | jq -c .upstream)"
rm "$BIN/link-refused"

case_ "an upstream wait says how long it waits, in another repo, and is refused in one line otherwise"
for args in "mentaldesk/style#84 why" "mentaldesk/style#84 someday why" "mentaldesk/demo#21 closed why" \
  "mentaldesk/style#84 closed MentalDesk.Tui why" "mentaldesk/style#84 released Mental/Desk why"; do
  : >"$WRITES"
  # shellcheck disable=SC2086
  run board demo depends dev 11 $args
  failed "depends $args"
  one_line "depends $args"
  same "writes for $args" "" "$(cat "$WRITES")"
done
run board demo depends dev 11 mentaldesk/style#84 someday "why"
grep -q "closed or released, not 'someday'" "$ERR" || fail "until: '$(cat "$ERR")'"
run board demo depends dev 11 mentaldesk/demo#21 closed "why"
grep -q "is in mentaldesk/demo itself" "$ERR" || fail "same repo: '$(cat "$ERR")'"

case_ "a task waiting on another repo isn't offered to the Dev"
waiting_upstream
run board demo next
same "next" null "$(cat "$OUT")"
run board demo claim dev 11
failed "claim"
same "writes" "" "$(cat "$WRITES")"

case_ "free leaves a task whose upstream change isn't there yet, whatever state it's in"
for state in "closed:84 Issue OPEN" "released:84 Issue CLOSED" "released:84 Issue CLOSED notplanned" "released:84 PullRequest CLOSED" \
  "released:84 PullRequest MERGED abc123" "released:84 Issue CLOSED abc123"; do
  waiting_upstream
  waits <<<"mentaldesk/style#84 ${state%%:*}"
  upstream_items <<<"${state#*:}"
  releases <<'RELEASES'
v0.3.9 2026-10-16T12:00:00Z pre
v0.3.8 2026-10-15T12:00:00Z
RELEASES
  contains v0.3.9
  run board demo free
  same "exit for $state" 0 "$STATUS"
  same "said for $state" "" "$(cat "$OUT")"
  same "writes for $state" "" "$(cat "$WRITES")"
  same "posted for $state" "" "$(cat "$POSTED")"
done

case_ "free frees a task once its upstream issue closes, or its change is in a published release, and says which"
waiting_upstream
waits <<<"mentaldesk/style#84 closed"
upstream_items <<<"84 Issue CLOSED"
run board demo free
same "exit" 0 "$STATUS"
same "said" "#11: no longer waiting on mentaldesk/style#84" "$(cat "$OUT")"
same "closed" null "$(marks 11 | jq -c .upstream)"
grep -qx "mentaldesk/style#84 has closed. No longer blocked." "$POSTED" || fail "closed: '$(cat "$POSTED")'"
grep -qF '<!-- a-team:unwait repo=mentaldesk/style n=84 -->' "$POSTED" || fail "closed: no unwait in '$(cat "$POSTED")'"
same "recorded" "dev no longer blocked by mentaldesk/style#84" "$(history_table 11 | head -1)"
for case in "v0.3.9:v0.3.9" "v0.3.9 v0.3.8:v0.3.8"; do
  waiting_upstream
  waits <<<"mentaldesk/style#84 released"
  upstream_items <<<"84 Issue CLOSED abc123"
  releases <<'RELEASES'
v0.3.9 2026-10-16T12:00:00Z
v0.4.0-beta 2026-10-17T12:00:00Z pre
v0.3.8 2026-10-15T12:00:00Z
v0.3.7 2026-10-14T12:00:00Z
RELEASES
  # shellcheck disable=SC2086
  contains ${case%%:*} v0.4.0-beta
  tag=${case#*:}
  run board demo free
  same "exit in $tag" 0 "$STATUS"
  day=$(jq -rn --arg t "$tag" '{"v0.3.9": "2026-10-16T12:00:00Z", "v0.3.8": "2026-10-15T12:00:00Z"}[$t] | fromdateiso8601 | strflocaltime("%d %b") | ltrimstr("0")')
  grep -qx "mentaldesk/style#84 is in $tag (released $day). No longer blocked: upgrade to ${tag#v} first." "$POSTED" ||
    fail "released in $tag: '$(cat "$POSTED")'"
done

case_ "free names the package to upgrade where the wait recorded one"
waiting_upstream
printf 'mentaldesk/style#84 released MentalDesk.Tui\n' | waits
upstream_items <<<"84 Issue CLOSED abc123"
releases <<<"v0.3.9 2026-10-16T12:00:00Z"
contains v0.3.9
run board demo free
day=$(jq -rn '"2026-10-16T12:00:00Z" | fromdateiso8601 | strflocaltime("%d %b") | ltrimstr("0")')
grep -qx "mentaldesk/style#84 is in v0.3.9 (released $day). No longer blocked: upgrade MentalDesk.Tui to 0.3.9 first." "$POSTED" ||
  fail "package: '$(cat "$POSTED")'"

case_ "an issue closed by hand frees a task waiting for a release once a published release follows it"
waiting_upstream
waits <<<"mentaldesk/style#84 released MentalDesk.Tui"
upstream_items <<<"84 Issue CLOSED"
releases <<'RELEASES'
v0.4.0 2026-10-17T12:00:00Z
v0.3.9 2026-10-16T12:00:00Z
v0.3.9-beta 2026-10-15T20:00:00Z pre
v0.3.8 2026-10-15T12:00:00Z
RELEASES
run board demo free
same "said" "#11: no longer waiting on mentaldesk/style#84" "$(cat "$OUT")"
closed=$(jq -rn '"2026-10-15T18:00:00Z" | fromdateiso8601 | strflocaltime("%d %b") | ltrimstr("0")')
grep -qx "mentaldesk/style#84 closed on $closed, and v0.3.9 (released $day) is the first release since. No longer blocked: upgrade MentalDesk.Tui to 0.3.9 first." "$POSTED" ||
  fail "closed by hand: '$(cat "$POSTED")'"

case_ "free says nothing again once it has freed a task, nor while one of its waits is still open"
waiting_upstream
waits <<<"mentaldesk/style#84 closed"
upstream_items <<<"84 Issue CLOSED"
run board demo free
{ jq -r '.[] | "comment \(.created_at) \(.user.login) 0 \(.body | gsub("\n"; "\\n"))"' "$THREAD"
  printf 'comment 2026-10-12T09:00:00Z demo-app[bot] 0 %s\n' "$(sed 's/$/\\n/' "$POSTED" | tr -d '\n')"; } | gh_thread
: >"$POSTED"
: >"$WRITES"
run board demo free
same "again" "" "$(cat "$OUT")$(cat "$POSTED")$(cat "$WRITES")"
waiting_upstream
printf 'mentaldesk/style#84 closed\nmentaldesk/lib#9 closed\n' | waits
upstream_items <<<"84 Issue CLOSED"
run board demo free
same "one still open" "" "$(cat "$OUT")$(cat "$POSTED")$(cat "$WRITES")"

case_ "--dry-run free says what it would free and changes nothing"
waiting_upstream
waits <<<"mentaldesk/style#84 closed"
upstream_items <<<"84 Issue CLOSED"
run board --dry-run demo free
same "said" "(dry run) #11: no longer waiting on mentaldesk/style#84" "$(cat "$OUT")"
same "writes" "" "$(cat "$WRITES")$(cat "$POSTED")"

case_ "undepend drops an upstream wait, its GitHub link, and the label once nothing's left"
waiting_upstream
waits <<<"mentaldesk/style#84 released"
echo '[{"number": 84, "id": 9084, "repository_url": "https://api.github.com/repos/mentaldesk/style"}]' >"$BLOCKED"
run board demo list
KEEP_BOARD=1 run board demo undepend dev 11 mentaldesk/style#84 "MentalDesk.Tui has it already"
same "exit" 0 "$STATUS"
same "said" "#11 no longer waits on mentaldesk/style#84, and said why on #11" "$(cat "$OUT")"
grep -q "DELETE .*/issues/11/dependencies/blocked_by/9084" "$WRITES" || fail "undepend upstream: '$(cat "$WRITES")'"
same "undepend upstream" null "$(marks 11 | jq -c .upstream)"
grep -qF '<!-- a-team:unwait repo=mentaldesk/style n=84 -->' "$POSTED" || fail "undepend upstream: no unwait"
run board demo undepend dev 11 mentaldesk/style#99 "why"
failed "not waiting"
one_line "not waiting"

case_ "waiting shows a task waiting on another repo with how far that's got, out of the questions"
waiting_upstream
waits <<<"mentaldesk/style#84 released"
upstream_items <<<"84 PullRequest MERGED abc123"
waiting
same "exit" 0 "$STATUS"
same "upstream" '[{"number":11,"turn":"upstream","upstream":"style#84: merged, not released","reason":"waiting on style#84: merged, not released"}]' \
  "$(jq -c 'map(select(.number == 11) | {number, turn, upstream, reason})' "$OUT")"

case_ "the Dev and the Lead record a wait on another repo's change, and the Dev doesn't hold it"
step=$(sed -n '/^3\. If it needs a change in another repo first/,/^4\./p' "$ROOT/roles/dev.md")
grep -qF 'depends dev <n> <owner/repo>#<pr> released <package> "<why>"' <<<"$step" || fail "dev.md: no upstream wait in '$step'"
grep -qF "Don't hold it" <<<"$step" || fail "dev.md: still holds it"
grep -qF 'depends lead <task> <owner/repo>#<n> <closed|released [<package>]> "<why>"' "$ROOT/roles/lead.md" || fail "lead.md: no upstream wait"

case_ "the Lead holds on another repo with depends, never blocked, and reads replies on the tasks it held"
grep -qF 'Never hold a task on another repo' "$ROOT/roles/lead.md" || fail "lead.md: still holds on another repo"
grep -qF 'feedback lead <task>' "$ROOT/roles/lead.md" || fail "lead.md: doesn't read replies on its holds"
grep -qF 'unblock lead <task>' "$ROOT/roles/lead.md" || fail "lead.md: doesn't clear its holds"
grep -qF 'unblock lead <n>' "$ROOT/tasks/lead.md" || fail "task prompt: Lead not authorised to clear its holds"

# other_team <name> <repo> <slug>: another team on this machine.
other_team() {
  jq -n --arg repo "$2" --arg slug "$3" '{repo: $repo, app: {slug: $slug}, project: {owner: "mentaldesk", number: 2}}' \
    >"$CONFIG/teams/$1.json"
}

case_ "waiting follows a held task's wait on an open issue to its pitch and PR, then the release and the bump"
waiting_upstream
other_team style mentaldesk/style style-app
waits <<<"mentaldesk/style#81 released MentalDesk.Tui"
upstream_items <<<"81 Issue OPEN"
jq -n '{data: {repository: {i81: {parent: {number: 71}, subIssues: {nodes: []},
  closedByPullRequestsReferences: {nodes: [{number: 84, state: "OPEN"}, {number: 80, state: "CLOSED"}]}}}}}' >"$REACH"
waiting
same "exit" 0 "$STATUS"
same "chains" '[[{"team":"style","refs":[71,81,84],"what":"style#84","shown":"open"},{"team":"style","refs":[],"what":"MentalDesk.Tui'"'"'s release","shown":"yours"},{"team":"demo","refs":[],"what":"demo'"'"'s bump","shown":"merges by itself"}]]' \
  "$(jq -c '.[] | select(.number == 11) | .chains' "$OUT")"
jq -n '{data: {repository: {i81: {parent: null, closedByPullRequestsReferences: {nodes: []}, subIssues: {nodes: [
  {number: 82, state: "OPEN", closedByPullRequestsReferences: {nodes: [{number: 85, state: "OPEN"}]}},
  {number: 83, state: "CLOSED", closedByPullRequestsReferences: {nodes: [{number: 86, state: "MERGED"}]}}]}}}}}' >"$REACH"
waiting
same "a pitch's task" '{"team":"style","refs":[81,82,85],"what":"style#85","shown":"open"}' "$(jq -c '.[] | select(.number == 11) | .chains[0][0]' "$OUT")"
rm "$CONFIG/teams/style.json"
waiting
same "no team" '{"team":null,"refs":[81,82,85],"what":"style#85","shown":"open"}' "$(jq -c '.[] | select(.number == 11) | .chains[0][0]' "$OUT")"

case_ "waiting: a merged change waits on its release, a released one on the open bump that brings it, and a met wait on nothing"
waiting_upstream
other_team style mentaldesk/style style-app
waits <<<"mentaldesk/style#84 released"
upstream_items <<<"84 PullRequest MERGED abc123"
waiting
same "merged" '[[{"team":"style","refs":[],"what":"style'"'"'s release","shown":"yours"}]]' "$(jq -c '.[] | select(.number == 11) | .chains' "$OUT")"
waits <<<"mentaldesk/style#84 released MentalDesk.Tui"
releases <<<"v0.3.9 2026-10-16T12:00:00Z"
contains v0.3.9
echo '[{"id": "PR_535", "number": 535, "title": "Bump MentalDesk.Tui from 0.3.8 to 0.3.9"},
       {"id": "PR_536", "number": 536, "title": "Bump xunit from 2.9 to 3.0"}]' >"$UPDATES"
waiting
same "bump" '[[{"team":"demo","refs":[535],"what":"#535","shown":"merges by itself"}]]' "$(jq -c '.[] | select(.number == 11) | .chains' "$OUT")"
echo '[]' >"$UPDATES"
waiting
same "met" '[]' "$(jq -c '.[] | select(.number == 11) | .chains' "$OUT")"

case_ "waiting: a bump that's been put to the stakeholder no longer says it merges by itself"
waiting_upstream
gh_items <<'ITEMS'
Building 10 A pitch
Ready 11 Needs the Mac keys
Ready 535 Bump MentalDesk.Tui from 0.3.8 to 0.3.9
ITEMS
edit_item 11 '.labels.nodes = [{name: "a-team:upstream"}]'
dependabot_pr 535
edit_item 535 '.labels.nodes = [{name: "a-team:dev"}, {name: "blocked"}]'
echo '[{"number": 11}]' >"$WAITING_ON"
other_team style mentaldesk/style style-app
waits <<<"mentaldesk/style#84 released MentalDesk.Tui"
upstream_items <<<"84 PullRequest MERGED abc123"
releases <<<"v0.3.9 2026-10-16T12:00:00Z"
contains v0.3.9
echo '[{"id": "PR_535", "number": 535, "title": "Bump MentalDesk.Tui from 0.3.8 to 0.3.9"}]' >"$UPDATES"
waiting
same "held" '[[{"team":"demo","refs":[535],"what":"#535","shown":"yours"}]]' "$(jq -c '.[] | select(.number == 11) | .chains' "$OUT")"
echo '[]' >"$UPDATES"

case_ "waiting gives a task In review that waits on another repo its chain, on its own card, and the upstream card frees it"
waiting_upstream
other_team style mentaldesk/style style-app
jq '(.data.organization.projectV2.items.nodes[] | select(.content.number == 11) | .fieldValueByName.name) = "In review"
  | (.data.organization.projectV2.items.nodes[].content | select(.number == 11) | .labels.nodes) = [{name: "a-team:dev"}, {name: "a-team:upstream"}]' \
  "$ITEMS" >"$ITEMS.new" && mv "$ITEMS.new" "$ITEMS"
waits <<<"mentaldesk/style#81 released MentalDesk.Tui"
upstream_items <<<"81 Issue OPEN"
jq -n '{data: {repository: {i81: {parent: null, subIssues: {nodes: []},
  closedByPullRequestsReferences: {nodes: [{number: 84, state: "OPEN"}]}}}}}' >"$REACH"
waiting
same "exit" 0 "$STATUS"
same "one card" '[{"status":"In review","turn":"you"}]' "$(jq -c 'map(select(.number == 11) | {status, turn})' "$OUT")"
same "chains" '[[{"team":"style","refs":[81,84],"what":"style#84","shown":"open"},{"team":"style","refs":[],"what":"MentalDesk.Tui'"'"'s release","shown":"yours"},{"team":"demo","refs":[],"what":"demo'"'"'s bump","shown":"merges by itself"}]]' \
  "$(jq -c '.[] | select(.number == 11) | .chains' "$OUT")"
upstream_items <<<"81 Issue CLOSED"
releases <<<"v0.3.9 2026-10-16T12:00:00Z"
contains v0.3.9
echo '[]' >"$UPDATES"
waiting
same "met" 'null' "$(jq -c '.[] | select(.number == 11) | .chains' "$OUT")"

case_ "free holds a released task while Dependabot's bump of its package is open, and frees it once that merges by itself"
waiting_upstream
waits <<<"mentaldesk/style#84 released MentalDesk.Tui"
upstream_items <<<"84 PullRequest MERGED abc123"
releases <<<"v0.3.9 2026-10-16T12:00:00Z"
contains v0.3.9
echo '[{"number": 534, "title": "Bump MentalDesk.Tui.Testing from 0.3.8 to 0.3.9"}]' >"$UPDATES"
run board demo free
same "another package" "#11: no longer waiting on mentaldesk/style#84" "$(cat "$OUT")"
waiting_upstream
waits <<<"mentaldesk/style#84 released MentalDesk.Tui"
upstream_items <<<"84 PullRequest MERGED abc123"
releases <<<"v0.3.9 2026-10-16T12:00:00Z"
contains v0.3.9
echo '[{"number": 535, "title": "Bump MentalDesk.Tui from 0.3.8 to 0.3.9"},
       {"number": 536, "title": "Bump xunit from 2.9 to 3.0"}]' >"$UPDATES"
: >"$WRITES"
run board demo free
same "exit" 0 "$STATUS"
same "said" "" "$(cat "$OUT")"
same "writes" "" "$(cat "$WRITES")"
same "posted" "" "$(cat "$POSTED")"
echo '[]' >"$UPDATES"
run board demo free
same "bump merged" "#11: no longer waiting on mentaldesk/style#84" "$(cat "$OUT")"

case_ "waiting tags a prerequisite with the Ready tasks on its own team waiting on it"
waiting_upstream
gh_items <<'ITEMS'
Ready 12 Waits on 13
In_review 13 The prerequisite
ITEMS
edit_item 12 '.issueDependenciesSummary.blockedBy = 1'
jq -n '{data: {repository: {i12: {blockedBy: {nodes: [{number: 13, state: "OPEN", repository: {nameWithOwner: "mentaldesk/demo"}},
  {number: 9, state: "OPEN", repository: {nameWithOwner: "mentaldesk/style"}}, {number: 8, state: "CLOSED", repository: {nameWithOwner: "mentaldesk/demo"}}]}}}}}' \
  >"$PREREQUISITES"
waiting
same "exit" 0 "$STATUS"
same "frees" '[{"number":13,"frees":[12]}]' "$(jq -c 'map(select(.frees) | {number, frees})' "$OUT")"

case_ "waiting tags a held task with the Ready tasks waiting on it"
edit_item 13 '.labels.nodes = [{name: "blocked"}]'
jq '(.data.organization.projectV2.items.nodes[] | select(.content.number == 13) | .fieldValueByName.name) = "Ready"' \
  "$ITEMS" >"$ITEMS.new" && mv "$ITEMS.new" "$ITEMS"
waiting
same "exit" 0 "$STATUS"
same "frees" '[{"number":13,"status":"Ready","frees":[12]}]' "$(jq -c 'map(select(.frees) | {number, status, frees})' "$OUT")"

# holding_team: demo, with a Ready task ranked High and one another team's task waits on.
holding_team() {
  fixture <<'JSON'
{ "repo": "mentaldesk/demo", "app": { "id": 7, "slug": "demo-app" }, "project": { "owner": "mentaldesk", "number": 1 },
  "wip": { "worktrees": 3, "pitched": 3, "exploring": 2, "ideas": 4 } }
JSON
  gh_items "$1" <<ITEMS
$2 20 Ranked high
$2 21 Unranked, and another team waits on it
ITEMS
  other_team app mentaldesk/app app-bot
  (source "$ROOT/scripts/common.sh"; history_sql "DELETE FROM board WHERE team = 'app';
    INSERT INTO board (team, item, node, status, since, content, marks) VALUES ('app', 5, 'I_5', 'Ready',
      '2026-10-10T09:00:00Z', '{\"type\": \"Issue\"}', '{\"upstream\": [\"mentaldesk/demo#21\"]}');" >/dev/null)
  gh_thread <<<'comment 2026-10-10T09:00:00Z app-bot[bot] 0 Blocked by mentaldesk/demo#21.\n\n<!-- a-team:wait repo=mentaldesk/demo n=21 until=released -->'
}

case_ "the Dev takes a task another team waits on before a higher Priority, and otherwise as before"
holding_team 20 Ready
run board demo next
same "next" 21 "$(jq -r .number "$OUT")"
claimable
run board demo claim dev
same "claimed" '{"number":21,"title":"Unranked, and another team waits on it"}' "$(cat "$OUT")"
rm "$CONFIG/teams/app.json"
run board demo next
same "nothing waits" 20 "$(jq -r .number "$OUT")"

case_ "the Dev takes a task its own team's Ready task waits on first"
holding_team 20 Ready
rm "$CONFIG/teams/app.json"
gh_items 20 <<'ITEMS'
Ready 20 Ranked high
Ready 21 The prerequisite
Ready 22 Waits on 21
ITEMS
edit_item 22 '.issueDependenciesSummary.blockedBy = 1'
jq -n '{data: {repository: {i22: {blockedBy: {nodes: [{number: 21, state: "OPEN", repository: {nameWithOwner: "mentaldesk/demo"}}]}}}}}' >"$PREREQUISITES"
run board demo next
same "next" 21 "$(jq -r .number "$OUT")"

case_ "the Lead pitches an Idea another team waits on before a higher Priority"
holding_team 20 Idea
run board demo lead-next
same "turn" pitch "$(jq -r .turn "$OUT")"
same "pitches" 21 "$(jq -r .item.number "$OUT")"
rm "$CONFIG/teams/app.json"
run board demo lead-next
same "nothing waits" 20 "$(jq -r .item.number "$OUT")"

# release-version.yml's "Next version" step, run as Actions runs it, against a repo of its own.
NEXT_VERSION="$WORK/next-version.sh" RELEASE_BIN="$WORK/release-bin"
awk '/id: next$/ { step = 1 } step && /run: \|$/ { body = 1; next }
  body && /^ *[^ ]/ && !/^          / { exit } body { sub(/^          /, ""); print }' \
  "$ROOT/.github/workflows/release-version.yml" >"$NEXT_VERSION"
mkdir -p "$RELEASE_BIN"
printf '#!/usr/bin/env bash\ncat "%s/merged.json"\n' "$RELEASE_BIN" | fake "$RELEASE_BIN/gh"
echo '[]' >"$RELEASE_BIN/merged.json"

release_repo() {
  RELEASE_REPO=$(mktemp -d "$WORK/release.XXXXXX")
  git -C "$RELEASE_REPO" init -q
  git -C "$RELEASE_REPO" -c user.name=t -c user.email=t@example.com -c commit.gpgsign=false \
    commit -q --allow-empty -m init
  for tag in "$@"; do git -C "$RELEASE_REPO" -c tag.gpgSign=false tag "$tag"; done
}

# next_version <version> <bump> [<pr>]: the step's outputs on stdout as "version tag".
next_version() {
  : >"$WORK/github-output"
  (cd "$RELEASE_REPO" && PATH="$RELEASE_BIN:$PATH" GITHUB_OUTPUT="$WORK/github-output" \
    GITHUB_REPOSITORY=mentaldesk/demo BASE=main VERSION=$1 BUMP=$2 PR=${3:-} \
    bash --noprofile --norc -eo pipefail "$NEXT_VERSION") >"$OUT" 2>"$ERR"
  STATUS=$?
}
picked() { echo "$(sed -n 's/^version=//p' "$WORK/github-output") $(sed -n 's/^tag=//p' "$WORK/github-output")"; }

case_ "an exact version above the last release is released as it is, whatever bump says"
release_repo v0.1.97 v0.1.98
next_version 0.9.0 major
same "exit" 0 "$STATUS"
same "released" "0.9.0 v0.9.0" "$(picked)"
next_version 0.1.100 auto
same "0.1.100" "0.1.100 v0.1.100" "$(picked)"

case_ "an exact version equal to the last release is refused, naming it"
next_version 0.1.98 auto
failed "equal"
grep -qF "version 0.1.98 isn't higher than the last release, v0.1.98." "$OUT" || fail "equal: '$(cat "$OUT")'"
same "nothing released" " " "$(picked)"

case_ "an exact version below the last release is refused, compared as numbers"
next_version 0.1.9 auto
failed "0.1.9"
grep -qF "version 0.1.9 isn't higher than the last release, v0.1.98." "$OUT" || fail "0.1.9: '$(cat "$OUT")'"
next_version 0.1.50 patch
failed "0.1.50"
same "nothing released" " " "$(picked)"

case_ "a version that isn't x.y.z is refused"
for version in 0.9 v0.9.0 1.0.0-rc.1 01.0.0 1.0.0.0; do
  next_version "$version" auto
  failed "$version"
  grep -qF "version must be x.y.z, like 1.0.0, not '$version'." "$OUT" || fail "$version: '$(cat "$OUT")'"
  same "$version released" " " "$(picked)"
done

case_ "with no version, bump picks it as before"
release_repo v0.1.98
for bump in "patch 0.1.99" "minor 0.2.0" "major 1.0.0"; do
  next_version '' "${bump% *}"
  same "${bump% *} exit" 0 "$STATUS"
  same "${bump% *}" "${bump#* } v${bump#* }" "$(picked)"
done
echo '[{"mergedAt": "2999-01-01T00:00:00Z", "labels": [{"name": "enhancement"}]}]' >"$RELEASE_BIN/merged.json"
next_version '' auto
same "auto" "0.2.0 v0.2.0" "$(picked)"
echo '[]' >"$RELEASE_BIN/merged.json"
next_version '' auto
same "auto, nothing labelled" "0.1.99 v0.1.99" "$(picked)"

case_ "with no releases yet, an exact version is released as it is"
release_repo
next_version 0.9.0 auto
same "exit" 0 "$STATUS"
same "released" "0.9.0 v0.9.0" "$(picked)"

case_ "a pull request builds 0.0.0-pr<n> and ignores the version"
release_repo v0.1.98
next_version 0.9.0 auto 42
same "exit" 0 "$STATUS"
same "released" "0.0.0-pr42 " "$(picked)"
next_version 0.1 auto 42
same "bad version on a PR" "0.0.0-pr42 " "$(picked)"

# release-publish.yml's "Publish release" and "Open tap PR" steps, run as Actions runs them.
publish_step() {
  awk -v name="- name: $1" 'index($0, name) { step = 1 } step && /run: \|$/ { body = 1; next }
    body && /^ *[^ ]/ && !/^          / { exit } body { sub(/^          /, ""); print }' \
    "$ROOT/.github/workflows/release-publish.yml"
}
PUBLISH_RELEASE="$WORK/publish-release.sh" OPEN_TAP_PR="$WORK/open-tap-pr.sh" PUBLISH_BIN="$WORK/publish-bin"
publish_step "Publish release" >"$PUBLISH_RELEASE"
publish_step "Open tap PR" >"$OPEN_TAP_PR"
mkdir -p "$PUBLISH_BIN"
fake "$PUBLISH_BIN/gh" <<'GH'
#!/usr/bin/env bash
echo "$*" >>"$FAKE_GITHUB/calls"
case "$1 $2" in
  "release list") [ ! -f "$FAKE_GITHUB/latest" ] || cat "$FAKE_GITHUB/latest" ;;
  "release view") [ -f "$FAKE_GITHUB/release" ] && cat "$FAKE_GITHUB/release" ;;
  "release create")
    if [ -f "$FAKE_GITHUB/release" ]; then echo "a release with the same tag name already exists" >&2; exit 1; fi
    echo false >"$FAKE_GITHUB/release"
    ;;
  "release upload") ;;
  "release edit") echo false >"$FAKE_GITHUB/release" ;;
  "pr list")
    case "$*" in
      *--head*) [ -f "$FAKE_GITHUB/open-pr" ] && echo 1 || echo 0 ;;
      *) [ ! -f "$FAKE_GITHUB/open-pr" ] || echo "7 $(cat "$FAKE_GITHUB/open-pr")" ;;
    esac
    ;;
  "pr create")
    if [ -f "$FAKE_GITHUB/502" ]; then rm "$FAKE_GITHUB/502"; echo "HTTP 502: 502 Bad Gateway" >&2; exit 1; fi
    [ ! -f "$FAKE_GITHUB/open-pr" ] || { echo "a pull request already exists" >&2; exit 1; }
    while [ "$1" != --head ]; do shift; done
    echo "$2" >"$FAKE_GITHUB/open-pr"
    ;;
  "pr merge") [ -f "$FAKE_GITHUB/open-pr" ] && touch "$FAKE_GITHUB/auto-merge" ;;
  "pr view") echo "https://github.com/mentaldesk/homebrew-tap/pull/7" ;;
  *) echo "gh stub: unexpected $*" >&2; exit 1 ;;
esac
GH

# A tap, a release job's folder and a GitHub with nothing published on them yet.
publish_world() {
  FAKE_GITHUB=$(mktemp -d "$WORK/publish.XXXXXX")
  local seed="$FAKE_GITHUB/seed"
  git init -q -b main "$seed"
  git -C "$seed" -c user.name=t -c user.email=t@example.com -c commit.gpgsign=false commit -q --allow-empty -m init
  git clone -q --bare "$seed" "$FAKE_GITHUB/tap.git"
  mkdir "$FAKE_GITHUB/job"
  mkdir "$FAKE_GITHUB/job/dist"
  touch "$FAKE_GITHUB/job/dist/demo-1.2.3-linux-x64.tar.gz"
  echo 'formula 1' >"$FAKE_GITHUB/job/formula.rb"
}

# publish_attempt: both steps from a fresh job, whose tap checkout tracks only the default branch.
publish_attempt() {
  rm -rf "$FAKE_GITHUB/job/tap"
  git clone -q --single-branch --branch main "file://$FAKE_GITHUB/tap.git" "$FAKE_GITHUB/job/tap"
  (cd "$FAKE_GITHUB/job" && export FAKE_GITHUB PATH="$PUBLISH_BIN:$PATH" TAG=v1.2.3 GITHUB_SHA=abc123 \
    NAME=demo VERSION=1.2.3 RELEASE_URL=https://example.com/v1.2.3 GIT_CONFIG_GLOBAL=/dev/null &&
    bash --noprofile --norc -e "$PUBLISH_RELEASE" && bash --noprofile --norc -e "$OPEN_TAP_PR") >"$OUT" 2>"$ERR"
  STATUS=$?
}
tap_branch() { git -C "$FAKE_GITHUB/tap.git" rev-parse -q --verify refs/heads/demo-1.2.3; }
tap_formula() { git -C "$FAKE_GITHUB/tap.git" show demo-1.2.3:Formula/demo.rb; }

case_ "a first publish creates the release and opens the tap PR with auto-merge on, as it always has"
publish_world
publish_attempt
same "exit" 0 "$STATUS"
grep -qxF 'release create v1.2.3 dist/demo-1.2.3-linux-x64.tar.gz --target abc123 --title v1.2.3 --generate-notes' "$FAKE_GITHUB/calls" ||
  fail "create: '$(cat "$FAKE_GITHUB/calls")'"
grep -q '^release upload' "$FAKE_GITHUB/calls" && fail "first publish uploaded again"
same "formula" "formula 1" "$(tap_formula)"
same "PR" demo-1.2.3 "$(cat "$FAKE_GITHUB/open-pr")"
[ -f "$FAKE_GITHUB/auto-merge" ] || fail "auto-merge isn't on"

case_ "re-running a publish that failed on opening the tap PR finishes it"
publish_world
touch "$FAKE_GITHUB/502"
publish_attempt
failed "first attempt"
[ -n "$(tap_branch)" ] || fail "first attempt: no tap branch"
[ ! -f "$FAKE_GITHUB/open-pr" ] || fail "first attempt: opened a PR"
first=$(tap_branch)
echo 'formula 2' >"$FAKE_GITHUB/job/formula.rb"
: >"$FAKE_GITHUB/calls"
publish_attempt
same "re-run exit" 0 "$STATUS"
grep -qxF 'release upload v1.2.3 dist/demo-1.2.3-linux-x64.tar.gz --clobber' "$FAKE_GITHUB/calls" ||
  fail "upload: '$(cat "$FAKE_GITHUB/calls")'"
grep -q '^release create' "$FAKE_GITHUB/calls" && fail "re-run tried to create the release"
grep -q '^release edit' "$FAKE_GITHUB/calls" && fail "re-run edited a published release"
[ "$(tap_branch)" != "$first" ] || fail "re-run left the tap branch as it was"
same "formula" "formula 2" "$(tap_formula)"
same "PR" demo-1.2.3 "$(cat "$FAKE_GITHUB/open-pr")"
[ -f "$FAKE_GITHUB/auto-merge" ] || fail "re-run: auto-merge isn't on"

case_ "re-running a publish whose tap PR is open reuses it"
publish_world
publish_attempt
same "exit" 0 "$STATUS"
rm "$FAKE_GITHUB/auto-merge"
: >"$FAKE_GITHUB/calls"
publish_attempt
same "re-run exit" 0 "$STATUS"
grep -q '^pr create' "$FAKE_GITHUB/calls" && fail "re-run opened a second PR"
[ -f "$FAKE_GITHUB/auto-merge" ] || fail "re-run: auto-merge isn't on"

case_ "a tap branch that moved after the job read it is still refused"
publish_world
touch "$FAKE_GITHUB/502"
publish_attempt
failed "first attempt"
sed 's/^git push /git -C "$FAKE_GITHUB\/tap.git" branch -f demo-1.2.3 main; git push /' "$OPEN_TAP_PR" >"$WORK/raced-tap-pr.sh"
(cd "$FAKE_GITHUB/job" && rm -rf tap && git clone -q --single-branch --branch main "file://$FAKE_GITHUB/tap.git" tap &&
  export FAKE_GITHUB PATH="$PUBLISH_BIN:$PATH" NAME=demo VERSION=1.2.3 RELEASE_URL=https://example.com/v1.2.3 GIT_CONFIG_GLOBAL=/dev/null &&
  bash --noprofile --norc -e "$WORK/raced-tap-pr.sh") >"$OUT" 2>"$ERR"
STATUS=$?
failed "raced push"
same "tap branch" "$(git -C "$FAKE_GITHUB/tap.git" rev-parse main)" "$(tap_branch)"

case_ "re-running a publish that left its release a draft publishes it, once its assets are up"
publish_world
echo true >"$FAKE_GITHUB/release"
publish_attempt
same "exit" 0 "$STATUS"
same "order" "upload v1.2.3
edit v1.2.3" "$(grep -E '^release (upload|edit)' "$FAKE_GITHUB/calls" | cut -d' ' -f2,3)"
grep -qxF 'release edit v1.2.3 --draft=false' "$FAKE_GITHUB/calls" || fail "edit: '$(cat "$FAKE_GITHUB/calls")'"
same "draft" false "$(cat "$FAKE_GITHUB/release")"
same "PR" demo-1.2.3 "$(cat "$FAKE_GITHUB/open-pr")"

case_ "publishing a version older than the latest release fails before it changes anything, re-run or not"
for existing in true false; do
  publish_world
  echo v1.10.0 >"$FAKE_GITHUB/latest"
  if [ "$existing" = true ]; then echo false >"$FAKE_GITHUB/release"; fi
  publish_attempt
  failed "older than latest"
  grep -qF '::error::1.2.3 is older than the latest release, 1.10.0' "$OUT" || fail "message: '$(cat "$OUT")'"
  grep -qE '^release (create|upload|edit)' "$FAKE_GITHUB/calls" && fail "changed the release: '$(cat "$FAKE_GITHUB/calls")'"
  [ -z "$(tap_branch)" ] || fail "pushed a tap branch"
done

case_ "publishing the latest release again, or one newer, isn't refused"
for latest in v1.2.3 v1.2.2; do
  publish_world
  echo "$latest" >"$FAKE_GITHUB/latest"
  publish_attempt
  same "exit with $latest latest" 0 "$STATUS"
done

# A copy of a-team at board format $2, as a release at version $1 or, with "", as a worktree. A checkout's core, and only
# that, reads A_TEAM_BOARD_FORMAT, and has a step for that format that adds the team to $STATE/format-$2-ran.
format_copy() {
  local copy
  copy=$(mktemp -d "$WORK/format.XXXXXX")
  cp -R "$ROOT/bin" "$ROOT/scripts" "$copy/"
  ln -s "$ROOT/core" "$copy/core"
  sed -i.bak "2i\\
export A_TEAM_BOARD_FORMAT=$2
" "$copy/bin/a-team"
  [ -z "$1" ] || echo "$1" >"$copy/VERSION"
  echo "$copy"
}
# format_home: a home of its own, whose state is the real state, with the dispatcher on a-team $1.
format_home() {
  FORMAT_HOME=$(mktemp -d "$WORK/home.XXXXXX")
  FORMAT_STATE="$FORMAT_HOME/.local/state/a-team"
  mkdir -p "$FORMAT_STATE"
  : >"$FORMAT_STATE/history.db"
  jq -n --arg bin "$1/bin/a-team" '{$bin}' >"$FORMAT_STATE/dispatcher.json"
}
# on_real <a-team> <args...>: that a-team against the real state, as a worktree's ./bin/a-team board runs it.
on_real() {
  env -u A_TEAM_STATE -u XDG_STATE_HOME HOME="$FORMAT_HOME" A_TEAM_CONFIG="$CONFIG" "$1/bin/a-team" board "${@:2}" >"$OUT" 2>"$ERR"
  STATUS=$?
}
set_stamp() {
  sqlite3 "$FORMAT_STATE/history.db" "INSERT OR REPLACE INTO formats (team, format, version, moved)
    VALUES ('demo', $1, '$2', strftime('%Y-%m-%dT%H:%M:%SZ', 'now'));"
}
format_board() {
  fixture <<'JSON'
{ "repo": "mentaldesk/demo", "stakeholders": ["reviewer"], "project": { "owner": "mentaldesk", "number": 1 } }
JSON
  gh_items <<'ITEMS'
Idea 7 A seed
ITEMS
}

INSTALLED=$(format_copy 0.3.0 1)

case_ "state with no stamp counts as today's format, and the installed a-team stamps it without stopping anything"
format_board
format_home "$INSTALLED"
on_real "$INSTALLED" demo list
same "first exit" 0 "$STATUS"
same "first stderr" "" "$(cat "$ERR")"
same "stamped" "1|0.3.0|0" "$(stamp)"
on_real "$INSTALLED" demo wip
same "second exit" 0 "$STATUS"
same "still stamped" "1|0.3.0|0" "$(stamp)"

case_ "installed commands that race to stamp a new state all carry on"
format_board
for round in 1 2 3 4 5; do
  format_home "$INSTALLED"
  pids=()
  for racer in 1 2 3 4; do
    env -u A_TEAM_STATE -u XDG_STATE_HOME HOME="$FORMAT_HOME" A_TEAM_CONFIG="$CONFIG" "$INSTALLED/bin/a-team" board demo format >"$OUT.$racer" 2>"$ERR.$racer" &
    pids+=($!)
  done
  for pid in "${pids[@]}"; do
    wait "$pid" || fail "round $round: $(cat "$ERR".*)"
  done
  same "round $round stamped" "1|0.3.0|0" "$(stamp)"
done

case_ "a worktree with no migration to do runs as today, and leaves the stamp to the installed a-team"
format_board
format_home "$INSTALLED"
SAME_FORMAT=$(format_copy "" 1)
on_real "$SAME_FORMAT" demo list
same "exit" 0 "$STATUS"
same "item" 7 "$(jq '.[0].number' "$OUT")"
same "no stamp" "" "$(stamp)"
set_stamp 1 0.3.0
on_real "$SAME_FORMAT" demo list
same "stamped exit" 0 "$STATUS"
same "stamp kept" "1|0.3.0|1" "$(stamp)"

case_ "a worktree whose a-team would migrate refuses every command, and points at --dry-run and A_TEAM_STATE"
format_board
format_home "$INSTALLED"
on_real "$INSTALLED" demo list
AHEAD=$(format_copy "" 2)
for command in list wip triggers\ dev; do
  # shellcheck disable=SC2086 # the command and its argument
  on_real "$AHEAD" demo $command
  failed "$command"
  one_line "$command"
  grep -q "would move the board from format 1 to 2, and only the installed one ($INSTALLED/bin/a-team) may: try it with --dry-run, or with A_TEAM_STATE set to a copy" "$ERR" ||
    fail "$command: '$(cat "$ERR")'"
done
same "stamp" "1|0.3.0|0" "$(stamp)"
[ ! -e "$FORMAT_STATE/format-2-ran" ] || fail "the step ran"
A_TEAM_RUN_TEAM=demo on_real "$AHEAD" demo list
grep -q "only the installed one .* may: try it with --dry-run" "$ERR" || fail "a run from a worktree: '$(cat "$ERR")'"
RELEASE_AHEAD=$(format_copy 0.2.0 2)
A_TEAM_RUN_TEAM=demo on_real "$RELEASE_AHEAD" demo list
same "a run on another release" "board.sh: this run is on 0.2.0, and only the installed a-team (0.3.0) may move the board from format 1 to 2: stopping" "$(cat "$ERR")"

case_ "--dry-run and a state of its own still work from that worktree, and the real state stays put"
on_real "$AHEAD" --dry-run demo list
same "dry run exit" 0 "$STATUS"
same "dry run item" 7 "$(jq '.[0].number' "$OUT")"
same "dry run stamp" "1|0.3.0|0" "$(stamp)"
OWN=$(mktemp -d "$WORK/state.XXXXXX")
cp "$FORMAT_STATE/history.db" "$OWN/"
A_TEAM_STATE=$OWN A_TEAM_CONFIG="$CONFIG" "$AHEAD/bin/a-team" board demo list >"$OUT" 2>"$ERR"
same "own state exit" 0 "$?"
same "own state moved" 2 "$(sqlite3 "$OWN/history.db" "SELECT format FROM formats WHERE team = 'demo';")"
same "own state stamp" "1|0.3.0|0" "$(stamp)"

case_ "the installed a-team moves it forward, once, and a run's copy of that release may too"
NEWER=$(format_copy 0.4.0 2)
format_home "$NEWER"
format_board
RUN_COPY=$(format_copy 0.4.0 2)
on_real "$NEWER" demo list
same "unstamped exit" 0 "$STATUS"
same "ran once" demo "$(cat "$FORMAT_STATE/format-2-ran")"
same "moved" "2|0.4.0|1" "$(stamp)"
on_real "$NEWER" demo list
same "ran once more" demo "$(cat "$FORMAT_STATE/format-2-ran")"
format_home "$NEWER"
on_real "$RUN_COPY" demo list
same "run copy exit" 0 "$STATUS"
same "run copy moved" "2|0.4.0|1" "$(stamp)"

case_ "commands that find the board behind take turns, so the step runs once"
for round in 1 2 3 4 5; do
  format_home "$NEWER"
  on_real "$INSTALLED" demo list
  set_stamp 1 0.3.0
  for racer in 1 2 3 4; do
    env -u A_TEAM_STATE -u XDG_STATE_HOME HOME="$FORMAT_HOME" A_TEAM_CONFIG="$CONFIG" "$NEWER/bin/a-team" board demo format >/dev/null 2>"$ERR.$racer" &
  done
  wait
  same "round $round ran" 1 "$(grep -c '' <"$FORMAT_STATE/format-2-ran")"
  same "round $round moved" "2|0.4.0|1" "$(stamp)"
  same "round $round left no turn behind" 0 "$(sqlite3 "$FORMAT_STATE/history.db" "SELECT COUNT(*) FROM format_moves;")"
  for racer in 1 2 3 4; do
    same "round $round stderr" "" "$(cat "$ERR.$racer")"
  done
done

case_ "a command still in board.sh passes the core's gate, the only one: it moves the board once, or refuses"
format_home "$NEWER"
on_real "$INSTALLED" demo list
set_stamp 1 0.3.0
on_real "$NEWER" demo overview
same "exit" 0 "$STATUS"
same "ran once" demo "$(cat "$FORMAT_STATE/format-2-ran")"
same "moved" "2|0.4.0|1" "$(stamp)"
on_real "$INSTALLED" demo overview
failed "refused"
same "words" "board.sh: the board is in format 2, from a-team 0.4.0, and this is 0.3.0: brew upgrade a-team" "$(cat "$ERR")"

case_ "a command that died moving the board doesn't hold up the next, which repeats its step"
format_home "$NEWER"
on_real "$INSTALLED" demo list
set_stamp 1 0.3.0
true &
DEAD=$!
wait "$DEAD"
sqlite3 "$FORMAT_STATE/history.db" "INSERT INTO format_moves (team, pid) VALUES ('demo', $DEAD);"
on_real "$NEWER" demo format
same "exit" 0 "$STATUS"
same "ran" demo "$(cat "$FORMAT_STATE/format-2-ran")"
same "moved" "2|0.4.0|1" "$(stamp)"

case_ "an a-team older than the state refuses every board command, read or write, whichever team moved it"
format_board
format_home "$NEWER"
on_real "$INSTALLED" demo list
sqlite3 "$FORMAT_STATE/history.db" "INSERT INTO formats (team, format, version) VALUES ('other', 2, '0.4.0');"
for command in "demo list" "demo wip" "demo move dev 7 Ready" "--dry-run demo list" "demo body 7"; do
  # shellcheck disable=SC2086 # the command and its arguments
  A_TEAM_RUN_TEAM=demo on_real "$INSTALLED" $command
  failed "$command"
  one_line "$command"
  same "$command" "board.sh: the board is in format 2, from a-team 0.4.0, and this run is on 0.3.0: stopping" "$(cat "$ERR")"
done
on_real "$INSTALLED" demo list
same "installed" "board.sh: the board is in format 2, from a-team 0.4.0, and this is 0.3.0: brew upgrade a-team" "$(cat "$ERR")"
on_real "$SAME_FORMAT" demo list
grep -q "is on unknown: merge the default branch" "$ERR" || fail "worktree: '$(cat "$ERR")'"

case_ "check reports a board problem while the a-team is behind the state, and runs its other checks"
on_real "$INSTALLED" demo check
same "exit" 1 "$STATUS"
grep -q '^problem  board     the board is in format 2, from a-team 0.4.0, and this is 0.3.0: brew upgrade a-team$' "$OUT" ||
  fail "check: '$(cat "$OUT")'"
grep -q '^problem  app ' "$OUT" || fail "check stopped early: '$(cat "$OUT")'"

case_ "the label adoption from #638 defines today's format: a worktree won't take labels off, and the installed a-team will"
format_board
format_home "$INSTALLED"
edit_item 7 '.labels.nodes = [{name: "rank:medium"}, {name: "mine"}]'
on_real "$SAME_FORMAT" --dry-run demo list
same "dry run exit" 0 "$STATUS"
[ ! -e "$BIN/unlabelled" ] || fail "dry run took labels off: '$(cat "$BIN/unlabelled")'"
on_real "$SAME_FORMAT" demo list
failed "worktree"
grep -q "would move the board forward, taking its old labels off the issues, and only the installed one ($INSTALLED/bin/a-team) may" "$ERR" ||
  fail "worktree: '$(cat "$ERR")'"
[ ! -e "$BIN/unlabelled" ] || fail "worktree took labels off: '$(cat "$BIN/unlabelled")'"
OLD_RUN=$(format_copy 0.2.0 1)
A_TEAM_RUN_TEAM=demo on_real "$OLD_RUN" demo list
failed "previous release"
one_line "previous release"
same "previous release" "board.sh: this run is on 0.2.0, and only the installed a-team (0.3.0) may move the board forward, taking its old labels off the issues: stopping" "$(cat "$ERR")"
[ ! -e "$BIN/unlabelled" ] || fail "previous release took labels off: '$(cat "$BIN/unlabelled")'"
on_real "$INSTALLED" demo list
same "installed exit" 0 "$STATUS"
same "taken off" "api -X DELETE repos/mentaldesk/demo/issues/7/labels/rank%3Amedium" "$(sort -u "$BIN/unlabelled")"

case_ "a worktree won't put cards back at their last moves on the real board, and the installed a-team will"
format_board
format_home "$INSTALLED"
on_real "$INSTALLED" demo list
real_status() { sqlite3 "$FORMAT_STATE/history.db" "SELECT status FROM board WHERE team = 'demo' AND item = 7;"; }
to_repair() { sqlite3 "$FORMAT_STATE/history.db" "SELECT COUNT(*) FROM boards WHERE team = 'demo' AND repaired IS NULL;"; }
sqlite3 "$FORMAT_STATE/history.db" "UPDATE boards SET repaired = NULL WHERE team = 'demo';"
on_real "$SAME_FORMAT" demo list
same "nothing to put back: exit" 0 "$STATUS"
same "nothing to put back: left for the installed one" 1 "$(to_repair)"
sqlite3 "$FORMAT_STATE/history.db" "INSERT INTO events (team, item, at, who, what)
  VALUES ('demo', 7, '2026-11-03T09:38:00Z', 'lead', 'Approved → Building');"
on_real "$SAME_FORMAT" demo list
failed "worktree"
one_line "worktree"
grep -q "would move the board forward, putting cards back at their last moves, and only the installed one ($INSTALLED/bin/a-team) may" "$ERR" ||
  fail "worktree: '$(cat "$ERR")'"
same "worktree moved nothing" Idea "$(real_status)"
same "worktree left it to do" 1 "$(to_repair)"
on_real "$INSTALLED" demo list
same "installed exit" 0 "$STATUS"
same "installed moved it" Building "$(real_status)"
same "installed finished it" 0 "$(to_repair)"

finish_suite
