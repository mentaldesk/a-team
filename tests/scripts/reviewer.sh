#!/usr/bin/env bash
# The Reviewer.
# shellcheck source=tests/scripts/lib.sh
source "$(dirname "${BASH_SOURCE[0]}")/lib.sh"
# shellcheck disable=SC2034,SC2154 # helpers read what an area sets, and an area reads what lib.sh sets

case_ "with the Reviewer on, a green draft Dev PR starts the Reviewer, not the Dev"
fixture <<'JSON'
{ "repo": "mentaldesk/demo", "reviewer": "reviewer", "app": { "id": 7, "slug": "demo-app" }, "project": { "owner": "mentaldesk", "number": 1 },
  "wip": { "worktrees": 1 }, "roles": { "reviewer": true } }
JSON
gh_items <<'ITEMS'
In_progress 12 A task with its draft PR up
ITEMS
gh_pr 912 true
run board demo triggers reviewer
same "exit" 0 "$STATUS"
same "reasons" '["PR #912 is green and waiting for its review"]' "$(jq -c .reasons "$OUT")"
same "tasks" '[12]' "$(jq -c '[.tasks[].number]' "$OUT")"
same "ready" null "$(jq -c .ready "$OUT")"
run board demo triggers dev
same "dev reasons" '[]' "$(jq -c .reasons "$OUT")"
run board demo pr 12
same "review" '"waiting"' "$(jq -c .review "$OUT")"

case_ "the Reviewer isn't started for a red PR, a ready one, or one that isn't the Dev's"
gh_runs <<'RUNS'
completed failure 2025-09-19T09:00:00Z build
RUNS
run board demo triggers reviewer
same "red" '[]' "$(jq -c .reasons "$OUT")"
gh_runs <<'RUNS'
completed success 2025-09-19T09:00:00Z build
RUNS
gh_pr 912 false
run board demo triggers reviewer
same "ready" '[]' "$(jq -c .reasons "$OUT")"
gh_pr 912 true
edit_item 12 '.labels.nodes = []'
run board demo triggers reviewer
same "not the Dev's" '[]' "$(jq -c .reasons "$OUT")"

case_ "once the review is posted, the Dev acts on it and the Reviewer never starts again"
edit_item 12 '.labels.nodes = [{name: "a-team:dev"}]'
gh_reviewed
run board demo triggers reviewer
same "reviewer reasons" '[]' "$(jq -c .reasons "$OUT")"
run board demo triggers dev
same "dev reasons" '["PR #912 has its review: act on it"]' "$(jq -c .reasons "$OUT")"
run board demo pr 12
same "review" '"posted"' "$(jq -c .review "$OUT")"

case_ "once the Dev answers the review, its next green run is told to mark the PR ready"
# `dev_said <before|after>`: a Dev comment from the team's App, added before or after the review.
dev_said() {
  jq --arg where "$1" '(.. | objects | select(has("isDraft"))).comments.nodes |=
      ([{body: "Done.\n\n<!-- a-team:dev -->", author: {__typename: "Bot", login: "demo-app"}}] as $c
       | if $where == "before" then $c + . else . + $c end)' "$PRS" >"$PRS.new" && mv "$PRS.new" "$PRS"
  gh_reviewing
}
dev_said before
run board demo pr 12
same "a Dev comment before the review" '"posted"' "$(jq -c .review "$OUT")"
dev_said after
run board demo pr 12
same "review" '"answered"' "$(jq -c .review "$OUT")"
run board demo triggers dev
same "dev reasons" '["PR #912 has its review answered and is green: mark it ready"]' "$(jq -c .reasons "$OUT")"

case_ "a review marker pasted by anyone but the team's App doesn't count"
gh_pr 912 true
gh_reviewed someone
run board demo pr 12
same "review" '"waiting"' "$(jq -c .review "$OUT")"

case_ "the Reviewer posts one review on a green draft Dev PR, with its marker"
gh_pr 912 true
printf 'Nothing needs changing.\n' >"$WORK/review"
run board demo review reviewer 912 "$WORK/review"
same "exit" 0 "$STATUS"
same "said" "#912: reviewed" "$(cat "$OUT")"
grep -q '^PR COMMENT pr comment 912 ' "$WRITES" || fail "review: no PR comment in '$(cat "$WRITES")'"
same "first line" "Nothing needs changing." "$(head -1 "$POSTED")"
same "marker" "<!-- a-team:reviewer -->" "$(tail -1 "$POSTED")"

case_ "a second review, a red or ready PR, and anything but the Dev's open task PR are refused"
gh_reviewed
refused_review "second review" "#912 has its review already: a PR is reviewed once"
gh_pr 912 false
refused_review "ready PR" "#912 is ready for review already"
gh_pr 912 true
gh_reviewing '.labels.nodes = [{name: "pitch"}]'
refused_review "pitch" "reviewer reviews only the Dev's open task PRs"
gh_reviewing
docs_pr_on_board on
refused_review "docs PR" "reviewer reviews only the Dev's open task PRs"
docs_pr_on_board off
gh_reviewing '.body = "My own change"'
refused_review "stakeholder's PR" "reviewer reviews only the Dev's open task PRs"
gh_reviewing '.state = "CLOSED"'
refused_review "closed PR" "reviewer reviews only the Dev's open task PRs"
gh_reviewing
gh_runs <<'RUNS'
completed failure 2025-09-19T09:00:00Z build
RUNS
refused_review "red PR" "#912 isn't green"

case_ "the Reviewer can't comment, move, claim or change what a task waits on, and no one else reviews"
gh_runs <<'RUNS'
completed success 2025-09-19T09:00:00Z build
RUNS
: >"$WRITES"
run board demo comment reviewer 12 "$WORK/review"
failed "comment"
grep -qF "reviewer posts only its review" "$ERR" || fail "comment: '$(cat "$ERR")'"
run board demo move reviewer 12 "In review"
failed "move"
run board demo claim reviewer
failed "claim"
run board demo depends reviewer 12 13 "why"
failed "depends"
run board demo review dev 912 "$WORK/review"
failed "dev review"
grep -qF "only reviewer reviews a task's PR" "$ERR" || fail "dev review: '$(cat "$ERR")'"
same "writes" "" "$(cat "$WRITES")"

case_ "dev pictures stores each image and puts it under What it looks like, captioned, above the try line"
printf 'first screen' >"$WORK/save.png"
printf 'second screen' >"$WORK/remove.png"
gh_pr 912 true
gh_reviewing '.body = "Settings buttons sit left.\n\nCloses #12\n\na-team try demo 912\n\n<!-- a-team:dev -->"'
: >"$WRITES"
rm -f "$BIN/git.json" "$BIN/pr-body"
run board demo pictures dev 912 "$WORK/save.png" "Save is left-aligned, in the accent colour" "$WORK/remove.png" "Remove team is in the danger colour"
same "exit" 0 "$STATUS"
same "said" "#912: 2 picture(s) under What it looks like" "$(cat "$OUT")"
same "stored" 2 "$(jq -r 'select(.kind == "blobs") | .content' "$BIN/git.json" | grep -c '')"
grep -q "^PR EDIT pr edit 912 -R mentaldesk/demo --body-file -" "$WRITES" || fail "pictures: no PR edit in '$(cat "$WRITES")'"
same "body" "Settings buttons sit left.

Closes #12

## What it looks like

Save is left-aligned, in the accent colour

![Save is left-aligned, in the accent colour]($(picture_link "$WORK/save.png"))

Remove team is in the danger colour

![Remove team is in the danger colour]($(picture_link "$WORK/remove.png"))

a-team try demo 912

<!-- a-team:dev -->" "$(cat "$BIN/pr-body")"

case_ "pictures again replaces the section rather than adding to it"
printf 'fixed screen' >"$WORK/fixed.png"
gh_reviewing "$(jq -Rrs '".body = \(tojson)"' "$BIN/pr-body")"
run board demo pictures dev 912 "$WORK/fixed.png" "Save is left-aligned, in the accent colour"
same "exit" 0 "$STATUS"
same "one section" 1 "$(grep -c '^## What it looks like$' "$BIN/pr-body")"
same "pictures" "![Save is left-aligned, in the accent colour]($(picture_link "$WORK/fixed.png"))" "$(grep '^!\[' "$BIN/pr-body")"
same "try line kept" "a-team try demo 912" "$(grep '^a-team try' "$BIN/pr-body")"
same "marker last" "<!-- a-team:dev -->" "$(tail -1 "$BIN/pr-body")"

case_ "a body with no try line gets the section just above the marker"
gh_reviewing '.body = "Closes #12\n\n<!-- a-team:dev -->"'
run board demo pictures dev 912 "$WORK/save.png" "Save is left-aligned"
same "exit" 0 "$STATUS"
same "body" "Closes #12

## What it looks like

Save is left-aligned

![Save is left-aligned]($(picture_link "$WORK/save.png"))

<!-- a-team:dev -->" "$(cat "$BIN/pr-body")"

case_ "pictures leaves blank lines elsewhere in the body as the Dev wrote them"
gh_reviewing '.body = "Run:\n\n```\na\n\n\nb\n```\n\nCloses #12\n\n\n\na-team try demo 912\n\n<!-- a-team:dev -->"'
run board demo pictures dev 912 "$WORK/save.png" "Save is left-aligned"
same "exit" 0 "$STATUS"
same "body" "Run:

\`\`\`
a


b
\`\`\`

Closes #12

## What it looks like

Save is left-aligned

![Save is left-aligned]($(picture_link "$WORK/save.png"))

a-team try demo 912

<!-- a-team:dev -->" "$(cat "$BIN/pr-body")"

case_ "pictures is the Dev's alone, on its own open task PR, for images of a size GitHub takes"
refused_pictures() {
  : >"$WRITES"
  rm -f "$BIN/git.json"
  run board demo pictures "${@:3}"
  failed "$1"
  one_line "$1"
  grep -qF "$2" "$ERR" || fail "$1: '$(cat "$ERR")'"
  same "$1 writes" "" "$(cat "$WRITES")"
  [ ! -e "$BIN/git.json" ] || fail "$1: stored something"
}
gh_reviewing
for role in customer reviewer you; do
  refused_pictures "$role pictures" "$role may not put pictures on anything; only dev does, on its own task PR, and lead, on its own pitch" \
    "$role" 912 "$WORK/save.png" "Save"
done
printf 'notes' >"$WORK/notes.txt"
refused_pictures "not an image" "notes.txt isn't a .png, .jpg, .gif or .webp" dev 912 "$WORK/notes.txt" "Save"
head -c $((10 * 1024 * 1024 + 1)) /dev/zero >"$WORK/huge.png"
refused_pictures "over 10 MB" "huge.png is over 10 MB" dev 912 "$WORK/huge.png" "Save"
refused_pictures "no caption" "save.png needs a caption" dev 912 "$WORK/save.png" " "
gh_reviewing '.body = "My own change"'
refused_pictures "stakeholder's PR" "dev puts pictures only on its own open task PRs" dev 912 "$WORK/save.png" "Save"
gh_reviewing '.state = "MERGED"'
refused_pictures "merged PR" "dev puts pictures only on its own open task PRs" dev 912 "$WORK/save.png" "Save"
gh_reviewing
docs_pr_on_board on
refused_pictures "docs PR" "dev puts pictures only on its own open task PRs" dev 912 "$WORK/save.png" "Save"
docs_pr_on_board off
gh_reviewing

case_ "pictures --dry-run shows the body it would write and writes nothing"
: >"$WRITES"
rm -f "$BIN/git.json" "$BIN/pr-body"
run board --dry-run demo pictures dev 912 "$WORK/save.png" "Save is left-aligned"
same "exit" 0 "$STATUS"
same "writes" "" "$(cat "$WRITES")"
[ ! -e "$BIN/git.json" ] || fail "pictures dry run: stored something"
[ ! -e "$BIN/pr-body" ] || fail "pictures dry run: edited the PR"
grep -q "would put 1 picture(s) on #912" "$ERR" || fail "pictures dry run: '$(cat "$ERR")'"
grep -qF "  | ![Save is left-aligned]" "$ERR" || fail "pictures dry run: no body in '$(cat "$ERR")'"

# The criteria: what the reader lists, what the Dev records and what you tick.

case_ "body on a PR brings a local copy of each picture under What it looks like, for the Reviewer"
export TMPDIR="$WORK/tmp"
mkdir -p "$TMPDIR"
SHOT_LINK=$(picture_link "$WORK/save.png")
gh_thread pull <<TALK
body ${TODAY}T08:00:00Z demo-app[bot] 0 ## What it looks like\\n\\nSave is left-aligned\\n\\n![Save is left-aligned]($SHOT_LINK)
TALK
run board demo body 912
same "exit" 0 "$STATUS"
same "images" "[\"$WORK/tmp/a-team-attachments/mentaldesk/demo/$MONTH/$(shasum -a 256 <"$WORK/save.png" | cut -c1-16).png\"]" "$(jq -c .images "$OUT")"
unset TMPDIR

case_ "with the Reviewer off, a green draft goes to the Dev as before, and review is refused"
fixture <<'JSON'
{ "repo": "mentaldesk/demo", "reviewer": "reviewer", "app": { "id": 7, "slug": "demo-app" }, "project": { "owner": "mentaldesk", "number": 1 },
  "wip": { "worktrees": 1 } }
JSON
run board demo triggers dev
same "dev reasons" '["PR #912 is green but still a draft"]' "$(jq -c .reasons "$OUT")"
run board demo triggers reviewer
same "reviewer reasons" '[]' "$(jq -c .reasons "$OUT")"
run board demo pr 12
same "review" '"off"' "$(jq -c .review "$OUT")"
refused_review "reviewer off" "demo has no Reviewer"

case_ "waiting marks a reviewed task, with how many points the review left to consider"
gh_items <<'ITEMS'
In_review 115 Reviewed already
ITEMS
gh_talk <<TALK
115 body ${TODAY}T08:00:00Z demo-app[bot] The task\n<!-- a-team:lead -->
115 pr-body ${TODAY}T08:25:00Z demo-app[bot] Closes #115\n<!-- a-team:dev -->
115 pr-comment ${TODAY}T08:40:00Z demo-app[bot] ## Needs changing\n- a.sh:1 off by one.\n\n## Worth considering\n- Rename it.\n- Split it.\n\n<!-- a-team:reviewer -->
TALK
waiting
same "exit" 0 "$STATUS"
same "reviewed" true "$(jq -c '.[0].reviewed' "$OUT")"
same "consider" 2 "$(jq -c '.[0].consider' "$OUT")"
gh_talk <<TALK
115 body ${TODAY}T08:00:00Z demo-app[bot] The task\n<!-- a-team:lead -->
115 pr-body ${TODAY}T08:25:00Z demo-app[bot] Closes #115\n<!-- a-team:dev -->
115 pr-comment ${TODAY}T08:40:00Z demo-app[bot] Nothing needs changing.\n\n<!-- a-team:reviewer -->
TALK
waiting
same "clean review" '[true,0]' "$(jq -c '[.[0].reviewed, .[0].consider]' "$OUT")"
gh_talk <<TALK
115 body ${TODAY}T08:00:00Z demo-app[bot] The task\n<!-- a-team:lead -->
115 pr-body ${TODAY}T08:25:00Z demo-app[bot] Closes #115\n<!-- a-team:dev -->
TALK
waiting
same "unreviewed" null "$(jq -c '.[0].reviewed' "$OUT")"

case_ "lead pictures go above a pitch's text mockup, once, only on its own pitch and only with Mockups on"
pitch_issue() {
  jq -n --arg body "$1" '{number: 30, title: "A pitch", body: $body}' >"$ISSUE"
}
lead_refused() {
  : >"$WRITES"
  rm -f "$BIN/git.json"
  run board demo pictures "${@:3}"
  failed "$1"
  one_line "$1"
  grep -qF "$2" "$ERR" || fail "$1: '$(cat "$ERR")'"
  same "$1 writes" "" "$(cat "$WRITES")"
  [ ! -e "$BIN/git.json" ] || fail "$1: stored something"
}
fixture <<'JSON'
{ "repo": "mentaldesk/demo", "reviewer": "reviewer", "app": { "id": 7, "slug": "demo-app" }, "project": { "owner": "mentaldesk", "number": 1 }, "mockups": true }
JSON
printf 'a screen' >"$WORK/form.png"
printf 'another screen' >"$WORK/form2.png"
gh_items <<'ITEMS'
Exploring 30 A pitch
Done 31 A finished pitch
Ready 32 A task
Idea 33 An idea
ITEMS
pitch_issue "$(printf '%s\n' '## Proposal' '' 'Do it' '' '## Mockup' '' 'text' '' '<!-- a-team:lead -->')"
: >"$WRITES"
rm -f "$BIN/git.json" "$EDITED"
run board demo pictures lead 30 "$WORK/form.png" "The Teams form, with the new tick"
same "exit" 0 "$STATUS"
same "said" "#30: 1 picture(s) under What it'll look like" "$(cat "$OUT")"
same "stored" 1 "$(jq -r 'select(.kind == "blobs") | .content' "$BIN/git.json" | grep -c '')"
same "body" "## Proposal

Do it

## What it'll look like

The Teams form, with the new tick

![The Teams form, with the new tick]($(picture_link "$WORK/form.png"))

## Mockup

text

<!-- a-team:lead -->" "$(cat "$EDITED")"
pitch_issue "$(cat "$EDITED")"
run board demo pictures lead 30 "$WORK/form2.png" "The form again"
same "again exit" 0 "$STATUS"
same "one section" 1 "$(grep -c "^## What it'll look like\$" "$EDITED")"
same "replaced" "![The form again]($(picture_link "$WORK/form2.png"))" "$(grep '^!\[' "$EDITED")"
same "marker last" "<!-- a-team:lead -->" "$(tail -1 "$EDITED")"
lead_refused "no caption" "form.png needs a caption: what it shows" lead 30 "$WORK/form.png" " "
run board demo pictures lead 30 "$WORK/form.png" "One
Two"
failed "two-line caption"
grep -qF "a caption is one line" "$ERR" || fail "two-line caption: '$(cat "$ERR")'"
for n in 31 32 33; do
  lead_refused "item $n" "lead puts pictures only on its own pitches" lead "$n" "$WORK/form.png" "Cap"
done
pitch_issue "No mockup here"
lead_refused "no mockup" "#30 has no ## Mockup section" lead 30 "$WORK/form.png" "Cap"
for role in customer reviewer you; do
  lead_refused "$role on a pitch" "$role may not put pictures on anything" "$role" 30 "$WORK/form.png" "Cap"
done
lead_refused "dev on a pitch" "#30" dev 30 "$WORK/form.png" "Cap"
jq 'del(.mockups)' "$TEAM" >"$TEAM.off" && mv "$TEAM.off" "$TEAM"
pitch_issue "$(printf '%s\n' '## Mockup' '' 'text')"
lead_refused "setting missing" "Mockups box is off" lead 30 "$WORK/form.png" "Cap"
jq '.mockups = false' "$TEAM" >"$TEAM.off" && mv "$TEAM.off" "$TEAM"
lead_refused "setting off" "Mockups box is off" lead 30 "$WORK/form.png" "Cap"

finish_suite
