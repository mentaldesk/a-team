#!/usr/bin/env bash
# Filing, linking and unlinking tasks, and claiming them.
# shellcheck source=tests/scripts/lib.sh
source "$(dirname "${BASH_SOURCE[0]}")/lib.sh"
# shellcheck disable=SC2034,SC2154 # helpers read what an area sets, and an area reads what lib.sh sets

case_ "link refuses to file an idea under a pitch, in one line, and writes nothing"
fixture <<'JSON'
{ "repo": "mentaldesk/demo", "reviewer": "reviewer", "app": { "id": 7, "slug": "demo-app" }, "project": { "owner": "mentaldesk", "number": 1 } }
JSON
gh_items <<'ITEMS'
Building 10 A pitch
Idea 30 An idea
Exploring 31 A draft
Pitched 32 A pitch in front of the stakeholder
Approved 33 An approved pitch
Building 34 A pitch being built
Ready 40 A task
ITEMS
gh_child 30 - -
run board demo link 10 30
failed "link #30"
one_line "link #30"
grep -qF "#30 is in Idea: if it's an idea, say \"Follow-up from #10\" in its body instead of linking it; if it's a task, add it to Ready first" "$ERR" ||
  fail "link #30: '$(cat "$ERR")'"
same "link #30 writes" "" "$(cat "$WRITES")"
for n in 31 32 33 34; do
  gh_child "$n" - -
  run board demo link 10 "$n"
  failed "link #$n"
  one_line "link #$n"
  grep -qF "#$n is an idea, not a task: say \"Follow-up from #10\" in its body instead of linking it" "$ERR" ||
    fail "link #$n: '$(cat "$ERR")'"
  same "link #$n writes" "" "$(cat "$WRITES")"
done
gh_child 50 - pitch
run board demo link 10 50
failed "link labelled pitch"
grep -qF "#50 is an idea, not a task" "$ERR" || fail "link labelled pitch: '$(cat "$ERR")'"
same "link labelled pitch writes" "" "$(cat "$WRITES")"

case_ "link still files a task that's Ready, or not on the board yet"
for n in 40 41; do
  gh_child "$n" - -
  run board demo link 10 "$n"
  same "link #$n exit" 0 "$STATUS"
  same "link #$n said" "#$n is now a sub-issue of #10" "$(cat "$OUT")"
  grep -q "^POST .*/issues/10/sub_issues .*sub_issue_id=90$n" "$WRITES" || fail "link #$n: '$(cat "$WRITES")'"
done

case_ "task files a new issue, makes it a sub-issue of the pitch, then puts it in Ready, and prints its number"
fixture <<'JSON'
{ "repo": "mentaldesk/demo", "app": { "id": 7, "slug": "demo-app" }, "project": { "owner": "mentaldesk", "number": 1 } }
JSON
gh_items <<'ITEMS'
Approved 10 An approved pitch
Building 11 A pitch being built
Pitched 12 A pitch in front of the stakeholder
Idea 13 An idea
Ready 40 A task
ITEMS
jq '.data.organization.projectV2.fields.nodes[0].options += [{id: "OPT_ready", name: "Ready"}]' "$META" >"$META.new" && mv "$META.new" "$META"
printf '## Context\n\nFor #10.\n' >"$WORK/task"
for pitch in 10 11; do
  gh_child 77 - -
  run board demo task lead "$pitch" "b opens the board on GitHub" "$WORK/task"
  same "task under #$pitch exit" 0 "$STATUS"
  same "task under #$pitch said" "#77: filed as Ready under #$pitch" "$(cat "$OUT")"
  same "task under #$pitch order" "CREATE POST ADD api" "$(awk '{print $1}' "$WRITES" | paste -sd ' ' -)"
  grep -qF -- "--title b opens the board on GitHub" "$WRITES" || fail "task under #$pitch: no title in '$(cat "$WRITES")'"
  grep -q 'item=PVTI_new .*option=OPT_ready' "$WRITES" || fail "task under #$pitch: not Ready in '$(cat "$WRITES")'"
  grep -q "^POST .*/issues/$pitch/sub_issues .*sub_issue_id=9077" "$WRITES" || fail "task under #$pitch: no link in '$(cat "$WRITES")'"
  same "task under #$pitch body" "$(printf '## Context\n\nFor #10.\n\n<!-- a-team:lead -->')" "$(cat "$POSTED")"
done

case_ "task refuses any role but the lead, and a pitch that isn't Approved or Building, and files nothing"
for role in dev customer you; do
  gh_child 77 - -
  run board demo task "$role" 10 "A task" "$WORK/task"
  failed "$role task"
  one_line "$role task"
  same "$role task writes" "" "$(cat "$WRITES")"
done
for refused in "12:#12 is 'Pitched'" "13:#13 is 'Idea'" "40:#40 is 'Ready'" "99:#99 is not on the board"; do
  pitch=${refused%%:*}
  gh_child 77 - -
  run board demo task lead "$pitch" "A task" "$WORK/task"
  failed "task under #$pitch"
  one_line "task under #$pitch"
  grep -qF "${refused#*:}" "$ERR" || fail "task under #$pitch: '$(cat "$ERR")'"
  same "task under #$pitch writes" "" "$(cat "$WRITES")"
done

case_ "task --dry-run shows the whole step and changes nothing"
gh_child 77 - -
run board --dry-run demo task lead 11 "A task" "$WORK/task"
same "exit" 0 "$STATUS"
same "said" "(dry run) #new: filed as Ready under #11" "$(cat "$OUT")"
for step in "open an issue titled 'A task'" "make #new a sub-issue of #11" "add #new to the board" "to 'Ready'"; do
  grep -qF "$step" "$ERR" || fail "dry run: no '$step' in '$(cat "$ERR")'"
done
grep -qF "  | <!-- a-team:lead -->" "$ERR" || fail "dry run: no body in '$(cat "$ERR")'"
same "writes" "" "$(cat "$WRITES")"
same "posted" "" "$(cat "$POSTED")"

case_ "the lead adding a task to Ready that isn't under an Approved or Building pitch is refused, pointing at task"
for parent in - 12 13; do
  gh_child 41 "$parent" -
  run board demo add lead 41 Ready
  failed "add under $parent"
  one_line "add under $parent"
  grep -qF "#41 isn't a sub-issue of an Approved or Building pitch: file a task with: board.sh demo task lead" "$ERR" ||
    fail "add under $parent: '$(cat "$ERR")'"
  same "add under $parent writes" "" "$(cat "$WRITES")"
done
gh_child 41 11 -
run board demo add lead 41 Ready
same "add under a Building pitch" 0 "$STATUS"

case_ "the lead's task reaches Ready from the record's parent, with no parent lookup on GitHub, whatever REST says"
for pitch in 10 11; do
  gh_items <<'ITEMS'
Approved 10 An approved pitch
Building 11 A pitch being built
Idea 41 A task
ITEMS
  gh_child 41 "$pitch" - "A task. <!-- a-team:lead -->"
  run board demo add lead 41 Ready
  same "under #$pitch" 0 "$STATUS"
  grep -q 'parent_issue_url\|ParentOf' "$BIN/args" && fail "under #$pitch: asked GitHub for the parent"
  KEEP_BOARD=1 run board demo list Ready
  same "under #$pitch list" "[{\"number\":41,\"parent\":$pitch}]" "$(jq -c 'map({number, parent})' "$OUT")"
done

case_ "the lead's task stays out of Ready when the record has it under no pitch or an unapproved one"
for pitch in - 12 13; do
  gh_items <<'ITEMS'
Pitched 12 A pitch in front of the stakeholder
Idea 13 An idea
Idea 41 A task
ITEMS
  gh_child 41 "$pitch" - "A task. <!-- a-team:lead -->"
  jq -n '{data: {repository: {issueOrPullRequest: {parent: {number: 11}}}}}' >"$BIN/parent.json"
  run board demo add lead 41 Ready
  failed "under $pitch"
  grep -qF "#41 isn't a sub-issue of an Approved or Building pitch" "$ERR" || fail "under $pitch: '$(cat "$ERR")'"
  same "under $pitch writes" "" "$(cat "$WRITES")"
done

case_ "a task the record hasn't seen yet takes its parent from GraphQL, accepted or refused"
for pitch in 10 11; do
  gh_items <<'ITEMS'
Approved 10 An approved pitch
Building 11 A pitch being built
ITEMS
  gh_child 41 "$pitch" -
  run board demo add lead 41 Ready
  same "under #$pitch" 0 "$STATUS"
  grep -q ParentOf "$BIN/args" || fail "under #$pitch: GraphQL wasn't asked"
  grep -q parent_issue_url "$BIN/args" && fail "under #$pitch: asked REST for the parent"
done
for pitch in - 12 13; do
  gh_items <<'ITEMS'
Pitched 12 A pitch in front of the stakeholder
Idea 13 An idea
ITEMS
  gh_child 41 "$pitch" -
  run board demo add lead 41 Ready
  failed "under $pitch"
  grep -qF "#41 isn't a sub-issue of an Approved or Building pitch" "$ERR" || fail "under $pitch: '$(cat "$ERR")'"
  same "under $pitch writes" "" "$(cat "$WRITES")"
done

case_ "the Lead's breakdown and the command list file tasks with task, not gh issue create, link and add"
step=$(sed -n '/^### 2\. Break down approved pitches/,/^### 3\./p' "$ROOT/roles/lead.md")
grep -qF 'task lead <pitch> "<title>" <file>' <<<"$step" || fail "lead.md breakdown: no task command"
grep -qE 'gh issue create|board \{\{team\}\} (link|add lead)' <<<"$step" && fail "lead.md breakdown: still files tasks by hand"
grep -qF 'a-team board {{team}} task lead <pitch> "<title>" <file>' "$ROOT/process.md" || fail "process.md: no task command"

case_ "unlink takes an idea off a pitch in any status before Done, says so on the pitch, and names the pitch on the idea"
for parent in Pitched Approved Building In_review; do
  gh_items <<ITEMS
$parent 10 A pitch
Building 30 The follow-up
ITEMS
  edit_item 10 '.labels.nodes = [{name: "pitch"}]'
  gh_child 30 10 pitch
  run board demo unlink lead 10 30
  same "$parent exit" 0 "$STATUS"
  same "$parent said" "#30 is no longer a sub-issue of #10; said so on #10, and #30 names #10 as where it came from" "$(cat "$OUT")"
  grep -q "^DELETE .*/issues/10/sub_issue .*sub_issue_id=9030" "$WRITES" || fail "$parent: no unlink in '$(cat "$WRITES")'"
  grep -q "^#30 (The follow-up) grew out of this pitch and is its own item now, so it no longer holds this one up" "$POSTED" ||
    fail "$parent: comment '$(cat "$POSTED")'"
  grep -q '<!-- a-team:lead -->' "$POSTED" || fail "$parent: no lead marker in '$(cat "$POSTED")'"
  same "$parent body" "Follow-up from #10

Spotted while reviewing." "$(cat "$EDITED")"
done

case_ "an idea not labelled pitch yet is taken off too, by its board status"
gh_items <<'ITEMS'
Building 10 A pitch
Idea 30 The follow-up
ITEMS
gh_child 30 10 -
run board demo unlink lead 10 30
same "exit" 0 "$STATUS"
grep -q "^DELETE " "$WRITES" || fail "idea: no unlink in '$(cat "$WRITES")'"

case_ "unlink leaves the idea's body alone when it already names the pitch"
gh_child 30 10 - "Follow-up from #10, where the stakeholder asked for it."
run board demo unlink lead 10 30
same "exit" 0 "$STATUS"
same "edited" "" "$(cat "$EDITED")"
grep -q "^EDIT " "$WRITES" && fail "named: body edited anyway"
gh_child 30 10 - "Follow-up from #100."
run board demo unlink lead 10 30
grep -q "^Follow-up from #10$" "$EDITED" || fail "#100 isn't #10: '$(cat "$EDITED")'"

case_ "unlink refuses an idea under a Done pitch, or one that isn't under it, and writes nothing"
gh_items <<'ITEMS'
Done 10 A pitch
Building 20 Another pitch
Building 30 The follow-up
ITEMS
edit_item 10 '.labels.nodes = [{name: "pitch"}]'
gh_child 30 10 pitch
run board demo unlink lead 10 30
failed "done"
one_line "done"
grep -q "may not change a Done pitch (#10)" "$ERR" || fail "done: '$(cat "$ERR")'"
same "done writes" "" "$(cat "$WRITES")"
run board demo unlink lead 20 30
failed "not a sub-issue"
one_line "not a sub-issue"
grep -q "#30 is not a sub-issue of #20" "$ERR" || fail "not a sub-issue: '$(cat "$ERR")'"
same "not a sub-issue writes" "" "$(cat "$WRITES")"
same "posted" "" "$(cat "$POSTED")"

case_ "a task still unlinks only when Ready, from a Building pitch, with nothing said"
gh_items <<'ITEMS'
Building 10 A pitch
Ready 40 A task
In_progress 41 A started task
In_review 42 A task in review
Done 43 A done task
ITEMS
gh_child 40 10 -
run board demo unlink lead 10 40
same "ready exit" 0 "$STATUS"
same "ready said" "#40 is no longer a sub-issue of #10" "$(cat "$OUT")"
grep -q "^DELETE " "$WRITES" || fail "ready: no unlink in '$(cat "$WRITES")'"
same "ready posted" "" "$(cat "$POSTED")"
same "ready edited" "" "$(cat "$EDITED")"
for n in 41 42 43; do
  gh_child "$n" 10 -
  run board demo unlink lead 10 "$n"
  failed "task #$n"
  grep -q "may only take a Ready task off a pitch" "$ERR" || fail "task #$n: '$(cat "$ERR")'"
  same "task #$n writes" "" "$(cat "$WRITES")"
done
gh_child 40 10 -
run board demo unlink dev 10 40
failed "dev"
grep -q "dev may not take a task off a pitch" "$ERR" || fail "dev: '$(cat "$ERR")'"

case_ "unlink trusts the record's parent over REST, and the record no longer has it afterwards"
gh_items <<'ITEMS'
Building 10 A pitch
Building 20 Another pitch
Ready 40 A task
ITEMS
edit_item 10 '.labels.nodes = [{name: "pitch"}]'
edit_item 20 '.labels.nodes = [{name: "pitch"}]'
gh_child 40 10 -
run board demo unlink lead 20 40
failed "wrong parent"
grep -q "#40 is not a sub-issue of #20" "$ERR" || fail "wrong parent: '$(cat "$ERR")'"
same "wrong parent writes" "" "$(cat "$WRITES")"
KEEP_BOARD=1 run board --dry-run demo unlink lead 10 40
same "dry run exit" 0 "$STATUS"
same "dry run keeps the parent" 10 "$(sqlite3 "$A_TEAM_STATE/history.db" "SELECT json_extract(content, '\$.parent') FROM board WHERE item = 40")"
KEEP_BOARD=1 run board demo unlink lead 10 40
same "exit" 0 "$STATUS"
grep -q 'parent_issue_url\|ParentOf' "$BIN/args" && fail "asked GitHub for the parent"
same "parent gone" "" "$(sqlite3 "$A_TEAM_STATE/history.db" "SELECT json_extract(content, '\$.parent') FROM board WHERE item = 40")"

case_ "unlink takes its parent from GraphQL for an idea the record hasn't seen yet"
gh_items <<'ITEMS'
Building 10 A pitch
Building 20 Another pitch
ITEMS
edit_item 10 '.labels.nodes = [{name: "pitch"}]'
edit_item 20 '.labels.nodes = [{name: "pitch"}]'
gh_child 30 10 pitch
run board demo unlink lead 20 30
failed "wrong parent"
grep -q "#30 is not a sub-issue of #20" "$ERR" || fail "wrong parent: '$(cat "$ERR")'"
run board demo unlink lead 10 30
same "exit" 0 "$STATUS"
grep -q ParentOf "$BIN/args" || fail "GraphQL wasn't asked"

case_ "children shows each child's board status, null when it isn't on the board"
gh_items <<'ITEMS'
Building 10 A pitch
Ready 40 A task
Building 30 The follow-up
ITEMS
jq -n '[{number: 40, title: "A task", state: "open", labels: []},
        {number: 30, title: "The follow-up", state: "open", labels: [{name: "pitch"}]},
        {number: 44, title: "Not on the board", state: "closed", labels: []}]' >"$SUBS"
run board demo children 10
same "exit" 0 "$STATUS"
same "children" '[{"number":40,"status":"Ready"},{"number":30,"status":"Building"},{"number":44,"status":null}]' \
  "$(jq -c 'map({number, status})' "$OUT")"
same "labels" '["pitch"]' "$(jq -c '.[1].labels' "$OUT")"

case_ "children tells a task the Lead filed that was left in Idea from an idea, and the Lead's role acts on it"
gh_items <<'ITEMS'
Building 10 A pitch
Idea 41 A task
Idea 30 A follow-up from a stakeholder
Idea 31 The Lead's own idea
Idea 32 A pitch in Idea
Ready 42 A task in Ready
ITEMS
lead='A thing. <!-- a-team:lead -->'
jq -n --arg lead "$lead" '[{number: 41, title: "A task", state: "open", labels: [], body: $lead},
        {number: 30, title: "A follow-up", state: "open", labels: [], body: "Spotted."},
        {number: 31, title: "An idea", state: "open", labels: [], body: $lead},
        {number: 32, title: "A pitch", state: "open", labels: [{name: "pitch"}], body: $lead},
        {number: 42, title: "A task", state: "open", labels: [], body: $lead}]' >"$SUBS"
run board demo list
sqlite3 "$A_TEAM_STATE/history.db" "UPDATE board SET marks = '{\"idea\":true}' WHERE team = 'demo' AND item = 31;"
KEEP_BOARD=1 run board demo children 10
same "exit" 0 "$STATUS"
same "left in idea" '[41]' "$(jq -c 'map(select(.leftInIdea) | .number)' "$OUT")"
step=$(sed -n '/^### 3\. Tend pitches in Building/,/^### 4\./p' "$ROOT/roles/lead.md")
grep -qF 'add lead <child> Ready' <<<"$step" || fail "lead.md: doesn't put a task left in Idea in Ready"

case_ "unlink refuses a task the Lead filed that was left in Idea, whichever fileable pitch it's under, and it still reaches Ready"
for parent in Approved Building; do
  gh_items <<ITEMS
$parent 10 A pitch
Idea 41 A task
ITEMS
  edit_item 10 '.labels.nodes = [{name: "pitch"}]'
  gh_child 41 10 - "A task. <!-- a-team:lead -->"
  run board demo unlink lead 10 41
  failed "$parent"
  grep -qF "#41 is a task left in Idea, not an idea" "$ERR" || fail "$parent: '$(cat "$ERR")'"
  same "$parent writes" "" "$(cat "$WRITES")"
  same "$parent posted" "" "$(cat "$POSTED")"
  KEEP_BOARD=1 run board demo add lead 41 Ready
  same "$parent add" 0 "$STATUS"
done

case_ "unlink still takes off an idea of the Lead's, which is marked, and a stray child of a pitch that isn't fileable"
gh_items <<'ITEMS'
Building 10 A pitch
Pitched 20 A pitch not approved yet
Idea 31 The Lead's own idea
Idea 41 A stray
ITEMS
edit_item 10 '.labels.nodes = [{name: "pitch"}]'
edit_item 20 '.labels.nodes = [{name: "pitch"}]'
gh_child 41 20 - "A stray. <!-- a-team:lead -->"
run board demo unlink lead 20 41
same "unfileable pitch" 0 "$STATUS"
gh_child 31 10 - "An idea. <!-- a-team:lead -->"
run board demo list
sqlite3 "$A_TEAM_STATE/history.db" "UPDATE board SET marks = '{\"idea\":true}' WHERE team = 'demo' AND item = 31;"
KEEP_BOARD=1 run board demo unlink lead 10 31
same "marked idea" 0 "$STATUS"

case_ "--dry-run says every write unlink would make, and makes none"
gh_items <<'ITEMS'
Building 10 A pitch
Building 30 The follow-up
ITEMS
gh_child 30 10 pitch
run board --dry-run demo unlink lead 10 30
same "exit" 0 "$STATUS"
grep -q "would take #30 off #10" "$ERR" || fail "dry run: no unlink in '$(cat "$ERR")'"
grep -q "would comment on #10" "$ERR" || fail "dry run: no comment in '$(cat "$ERR")'"
grep -q "would add 'Follow-up from #10' to #30" "$ERR" || fail "dry run: no body edit in '$(cat "$ERR")'"
same "writes" "" "$(cat "$WRITES")"
same "posted" "" "$(cat "$POSTED")"
same "edited" "" "$(cat "$EDITED")"

case_ "the Dev puts a follow-up it opened on the board as an Idea, without the Lead's label"
gh_items <<'ITEMS'
In_progress 12 A task
ITEMS
jq '.data.organization.projectV2.fields.nodes[0].options += [{id: "OPT_idea", name: "Idea"}]' "$META" >"$META.new" && mv "$META.new" "$META"
gh_child 41 - - "Follow-up from #12.

<!-- a-team:dev -->"
A_TEAM_RUN_TASK=12 run board demo add dev 41 Idea
same "exit" 0 "$STATUS"
grep -q "OPT_idea" "$WRITES" || fail "no move to Idea in '$(cat "$WRITES")'"
grep -q "a-team:idea" "$WRITES" && fail "labelled as the Lead's discovery: '$(cat "$WRITES")'"

case_ "the Dev adds no issue it didn't open, and nothing but an Idea"
gh_child 41 - - "Follow-up from #12.

<!-- a-team:lead -->"
run board demo add dev 41 Idea
failed "someone else's issue"
one_line "someone else's issue"
grep -qF "#41 has no dev marker: dev adds only the follow-ups it opened" "$ERR" ||
  fail "someone else's issue: '$(cat "$ERR")'"
gh_child 41 - - "Follow-up from #12.

<!-- a-team:dev -->"
for to in Ready Exploring; do
  run board demo add dev 41 "$to"
  failed "add as $to"
  grep -qF "dev may not add items as '$to'" "$ERR" || fail "add as $to: '$(cat "$ERR")'"
done
same "writes" "" "$(cat "$WRITES")"

finish_suite
