#!/usr/bin/env bash
# Comments, acks and whose words they are.
# shellcheck source=tests/scripts/lib.sh
source "$(dirname "${BASH_SOURCE[0]}")/lib.sh"
# shellcheck disable=SC2034,SC2154 # helpers read what an area sets, and an area reads what lib.sh sets

case_ "comment you posts your words as they are: no marker, and no 👀 on anything"
fixture <<'JSON'
{ "repo": "mentaldesk/demo", "reviewer": "reviewer", "app": { "id": 7, "slug": "demo-app" }, "project": { "owner": "mentaldesk", "number": 1 } }
JSON
gh_items <<'ITEMS'
Pitched 7 A pitch in front of me
ITEMS
gh_thread <<TALK
body ${TODAY}T02:10:00Z demo-app[bot] 0 The pitch\n<!-- a-team:lead -->
comment ${TODAY}T02:20:00Z reviewer 0 Needs a second option.
TALK
echo "Not yet: the second option is still missing." >"$WORK/mine"
run board demo comment you 7 "$WORK/mine"
same "exit" 0 "$STATUS"
same "posted" "Not yet: the second option is still missing." "$(cat "$POSTED")"
same "acked" "" "$(cat "$ACKED")"

case_ "feedback reports it as yours and unanswered, so it starts a run"
gh_thread <<TALK
body ${TODAY}T02:10:00Z demo-app[bot] 0 The pitch\n<!-- a-team:lead -->
comment ${TODAY}T02:30:00Z reviewer 0 $(cat "$POSTED")
TALK
run board demo feedback lead 7
same "exit" 0 "$STATUS"
same "unanswered" '["Not yet: the second option is still missing."]' "$(jq -c '[.[].body]' "$OUT")"

case_ "comment still refuses a role it doesn't know"
: >"$POSTED"
run board demo comment nobody 7 "$WORK/mine"
failed "unknown role"
one_line "unknown role"
grep -q "unknown role 'nobody' (lead | dev | customer | you)" "$ERR" || fail "unknown role: '$(cat "$ERR")'"
same "posted" "" "$(cat "$POSTED")"

case_ "--dry-run shows the comment you'd post and posts nothing"
run board --dry-run demo comment you 7 "$WORK/mine"
same "exit" 0 "$STATUS"
same "posted" "" "$(cat "$POSTED")"
grep -qF "  | Not yet: the second option is still missing." "$ERR" || fail "comment you dry run: '$(cat "$ERR")'"
grep -q "would comment on #7" "$ERR" || fail "comment you dry run: '$(cat "$ERR")'"

case_ "the agents' settings deny comment you, beside accept"
grep -qF '"Bash(a-team board * comment you *)"' "$ROOT/settings/agents.json" || fail "no comment you deny rule"


case_ "new you opens an issue as you, titled by the file's first line, and prints its number"
printf 'Remember the lane I was on\n\nIt always starts on the first team.\n' >"$WORK/idea"
run board demo new you "$WORK/idea"
same "exit" 0 "$STATUS"
same "number" 77 "$(cat "$OUT")"
grep -qF -- "--title Remember the lane I was on" "$WRITES" || fail "new: no title in '$(cat "$WRITES")'"
grep -qF -- "-R mentaldesk/demo" "$WRITES" || fail "new: not the team's repo in '$(cat "$WRITES")'"
same "body" "$(printf '\nIt always starts on the first team.')" "$(cat "$POSTED")"
grep -q "a-team:" "$POSTED" && fail "new: a marker in your issue"

case_ "new refuses an empty title, an agent, and opens nothing"
: >"$WRITES"
printf '   \nA body with no title\n' >"$WORK/untitled"
run board demo new you "$WORK/untitled"
failed "untitled"
one_line "untitled"
grep -q "an issue needs a title" "$ERR" || fail "untitled: '$(cat "$ERR")'"
for role in lead dev customer; do
  run board demo new "$role" "$WORK/idea"
  failed "$role new"
  grep -q "$role may not open an issue as you" "$ERR" || fail "$role new: '$(cat "$ERR")'"
done
same "writes" "" "$(cat "$WRITES")"

case_ "new that GitHub refuses says why"
touch "$BIN/create-fails"
run board demo new you "$WORK/idea"
failed "refused"
grep -q "Could not resolve to a Repository" "$ERR" || fail "refused: '$(cat "$ERR")'"
rm "$BIN/create-fails"

case_ "new that GitHub refuses before it reads a long body still says why"
touch "$BIN/create-fails"
{ echo 'A long one'; head -c 1000000 /dev/zero | tr '\0' x; echo; } >"$WORK/long"
run board demo new you "$WORK/long"
failed "refused"
grep -q "Could not resolve to a Repository" "$ERR" || fail "refused: '$(cut -c1-200 "$ERR")'"
rm "$BIN/create-fails"

case_ "--dry-run shows the issue you'd open and opens nothing"
run board --dry-run demo new you "$WORK/idea"
same "exit" 0 "$STATUS"
same "writes" "" "$(cat "$WRITES")"
grep -q "would open an issue titled 'Remember the lane I was on'" "$ERR" || fail "new dry run: '$(cat "$ERR")'"

printf 'not really a png' >"$WORK/Shot.PNG"
SHOT_HASH=$(shasum -a 256 <"$WORK/Shot.PNG" | cut -c1-16)
SHOT_LINK="https://github.com/mentaldesk/demo/blob/a-team/attachments/$MONTH/$SHOT_HASH.png?raw=true"

case_ "attach you starts the attachments branch with the image, named by its content, and prints its link"
: >"$WRITES"
run board demo attach you "$WORK/Shot.PNG"
same "exit" 0 "$STATUS"
same "link" "$SHOT_LINK" "$(cat "$OUT")"
same "objects" "blobs trees commits" "$(jq -r .kind "$BIN/git.json" | tr '\n' ' ' | sed 's/ $//')"
same "content" "$(base64 <"$WORK/Shot.PNG" | tr -d '\n')" "$(jq -r 'select(.kind == "blobs") | .content' "$BIN/git.json")"
same "path" "$MONTH/$SHOT_HASH.png" "$(jq -r 'select(.kind == "trees") | .tree[0].path' "$BIN/git.json")"
same "orphan" '[]' "$(jq -c 'select(.kind == "commits") | .parents' "$BIN/git.json")"
grep -q "REF.*-X POST repos/mentaldesk/demo/git/refs.*ref=refs/heads/a-team/attachments" "$WRITES" ||
  fail "attach: no new branch in '$(cat "$WRITES")'"

case_ "attach you adds to the branch once it's there"
: >"$WRITES"
rm -f "$BIN/git.json"
touch "$BIN/branched"
run board demo attach you "$WORK/Shot.PNG"
same "exit" 0 "$STATUS"
same "base" "TREE1" "$(jq -r 'select(.kind == "trees") | .base_tree' "$BIN/git.json")"
same "parent" '["HEAD1"]' "$(jq -c 'select(.kind == "commits") | .parents' "$BIN/git.json")"
grep -q "REF.*-X PATCH repos/mentaldesk/demo/git/refs/heads/a-team/attachments" "$WRITES" ||
  fail "attach: branch not moved on in '$(cat "$WRITES")'"

case_ "the same image attached again is stored once"
: >"$WRITES"
rm -f "$BIN/git.json"
touch "$BIN/stored"
run board demo attach you "$WORK/Shot.PNG"
same "exit" 0 "$STATUS"
same "link" "$SHOT_LINK" "$(cat "$OUT")"
[ ! -e "$BIN/git.json" ] || fail "attach again: stored a second time"
same "writes" "" "$(cat "$WRITES")"
rm "$BIN/stored" "$BIN/branched"

case_ "attach refuses what isn't an image, one over 10 MB, and an agent, and stores nothing"
printf 'notes' >"$WORK/notes.txt"
run board demo attach you "$WORK/notes.txt"
failed "not an image"
one_line "not an image"
grep -q "notes.txt isn't a .png, .jpg, .gif or .webp" "$ERR" || fail "not an image: '$(cat "$ERR")'"
head -c $((10 * 1024 * 1024 + 1)) /dev/zero >"$WORK/huge.png"
run board demo attach you "$WORK/huge.png"
failed "over 10 MB"
grep -q "huge.png is over 10 MB" "$ERR" || fail "over 10 MB: '$(cat "$ERR")'"
for role in lead dev customer reviewer; do
  run board demo attach "$role" "$WORK/Shot.PNG"
  failed "$role attach"
  grep -q "$role may not attach an image" "$ERR" || fail "$role attach: '$(cat "$ERR")'"
done
[ ! -e "$BIN/git.json" ] || fail "refused attach: stored something"

case_ "--dry-run says what it would store and stores nothing"
run board --dry-run demo attach you "$WORK/Shot.PNG"
same "exit" 0 "$STATUS"
same "link" "$SHOT_LINK" "$(cat "$OUT")"
[ ! -e "$BIN/git.json" ] || fail "attach dry run: stored something"
grep -q "would commit Shot.PNG to $MONTH/$SHOT_HASH.png on a-team/attachments" "$ERR" || fail "attach dry run: '$(cat "$ERR")'"

case_ "the agents' settings deny attach"
grep -qF '"Bash(a-team board * attach you *)"' "$ROOT/settings/agents.json" || fail "no attach deny rule"

case_ "body and feedback bring a local copy of each attached image, for the agent to look at"
export TMPDIR="$WORK/tmp"
mkdir -p "$TMPDIR"
gh_thread <<TALK
body ${TODAY}T08:00:00Z reviewer 0 Look: ![Shot]($SHOT_LINK)
comment ${TODAY}T08:10:00Z reviewer 0 And again ![Shot]($SHOT_LINK) with nothing else
comment ${TODAY}T08:20:00Z reviewer 0 Plain words, no picture
TALK
COPY="$WORK/tmp/a-team-attachments/mentaldesk/demo/$MONTH/$SHOT_HASH.png"
: >"$WRITES"
run board demo body 7
same "exit" 0 "$STATUS"
same "body images" "[\"$COPY\"]" "$(jq -c .images "$OUT")"
same "copy" PNG "$(cat "$COPY")"
run board demo feedback lead 7
same "exit" 0 "$STATUS"
same "feedback images" "[[\"$COPY\"],null]" "$(jq -c '[.[] | .images]' "$OUT")"
same "fetched once" 1 "$(grep -c RAW "$WRITES")"
unset TMPDIR

case_ "add you puts your own issue on the board as an Idea, with no label"
jq '.data.organization.projectV2.fields.nodes[0].options += [{id: "OPT_idea", name: "Idea"}]' "$META" >"$META.new" && mv "$META.new" "$META"
gh_child 41 - - "It always starts on the first team."
run board demo add you 41 Idea
same "exit" 0 "$STATUS"
grep -q "OPT_idea" "$WRITES" || fail "add you: no move to Idea in '$(cat "$WRITES")'"
grep -q "labels" "$WRITES" && fail "add you: labelled '$(cat "$WRITES")'"

case_ "add you adds only an Idea"
for to in Ready Pitched; do
  run board demo add you 41 "$to"
  failed "add you as $to"
  grep -qF "you may not add items as '$to'" "$ERR" || fail "add you as $to: '$(cat "$ERR")'"
done

case_ "the agents' settings deny new you and add you"
grep -qF '"Bash(a-team board * new you *)"' "$ROOT/settings/agents.json" || fail "no new you deny rule"
grep -qF '"Bash(a-team board * add you *)"' "$ROOT/settings/agents.json" || fail "no add you deny rule"

# The 👀: a reviewer comment is answered once a run has left one on it, and a run leaves one only
# on what it could have seen. The races replayed here are the ones in pitch #3. $TODAY is on or
# after board.sh's ACK_FROM, so these cases see the 👀 rule and the dated ones below the old.
case_ "comment acks what the run could have seen, and not a comment that arrived while it worked"
fixture <<'JSON'
{ "repo": "mentaldesk/demo", "reviewer": "reviewer", "app": { "id": 7, "slug": "demo-app" },
  "project": { "owner": "mentaldesk", "number": 1 },
  "wip": { "pitched": 3, "exploring": 4, "ideas": 4 } }
JSON
gh_items <<'ITEMS'
Pitched 7 A pitch in front of me
ITEMS
gh_thread <<TALK
body ${TODAY}T02:10:00Z demo-app[bot] 0 The pitch\n<!-- a-team:lead -->
comment ${TODAY}T02:20:00Z reviewer 0 Needs a second option.
comment ${TODAY}T02:28:46Z reviewer 0 And do the same for subissue links.
TALK
echo "Added one." >"$WORK/reply"
export A_TEAM_RUN_STARTED=${TODAY}T02:21:49Z
run board demo comment lead 7 "$WORK/reply"
same "exit" 0 "$STATUS"
same "acked" "IC_1" "$(cat "$ACKED")"
grep -q '<!-- a-team:lead -->' "$POSTED" || fail "reply: no marker in '$(cat "$POSTED")'"
unset A_TEAM_RUN_STARTED

case_ "the comment that arrived mid-run is still unanswered, however recently the role replied"
gh_thread <<TALK
body ${TODAY}T02:10:00Z demo-app[bot] 0 The pitch\n<!-- a-team:lead -->
comment ${TODAY}T02:20:00Z reviewer 1 Needs a second option.
comment ${TODAY}T02:28:46Z reviewer 0 And do the same for subissue links.
comment ${TODAY}T02:33:03Z demo-app[bot] 0 Added one.\n<!-- a-team:lead -->
TALK
run board demo feedback lead 7
same "exit" 0 "$STATUS"
same "unanswered" '["And do the same for subissue links."]' "$(jq -c '[.[].body]' "$OUT")"

case_ "triggers names it too, so it gets a run of its own, recorded on #7"
gh_recent <<RECENT
7 ${TODAY}T02:20:00Z reviewer 1 Needs a second option.
7 ${TODAY}T02:28:46Z reviewer 0 And do the same for subissue links.
7 ${TODAY}T02:33:03Z demo-app[bot] 0 Added one.\n<!-- a-team:lead -->
RECENT
run board demo triggers lead
same "exit" 0 "$STATUS"
same "reasons" "[\"stakeholder feedback on #7 (${TODAY}T02:28:46Z)\"]" "$(jq -c .reasons "$OUT")"
same "items" "[7]" "$(jq -c .items "$OUT")"
same "card" 7 "$(jq -c .card "$OUT")"

case_ "waiting says the same: the gate is the Lead's until the 👀 is there"
gh_talk <<TALK
7 body ${TODAY}T02:10:00Z demo-app[bot] The pitch\n<!-- a-team:lead -->
7 comment+seen ${TODAY}T02:20:00Z reviewer Needs a second option.
7 comment ${TODAY}T02:28:46Z reviewer And do the same for subissue links.
7 comment ${TODAY}T02:33:03Z demo-app[bot] Added one.\n<!-- a-team:lead -->
TALK
waiting
same "exit" 0 "$STATUS"
same "turn" '"lead"' "$(jq -c '.[0].turn' "$OUT")"
same "reason" '"answering your feedback since 02:28"' "$(jq -c '.[0].reason' "$OUT")"

case_ "and hands it back once the 👀 is"
gh_talk <<TALK
7 body ${TODAY}T02:10:00Z demo-app[bot] The pitch\n<!-- a-team:lead -->
7 comment+seen ${TODAY}T02:20:00Z reviewer Needs a second option.
7 comment+seen ${TODAY}T02:28:46Z reviewer And do the same for subissue links.
7 comment ${TODAY}T02:33:03Z demo-app[bot] Added one.\n<!-- a-team:lead -->
TALK
waiting
same "exit" 0 "$STATUS"
same "turn" '"you"' "$(jq -c '.[0].turn' "$OUT")"
same "reason" '"awaiting your approval since 02:33"' "$(jq -c '.[0].reason' "$OUT")"

# Before ACK_FROM a marker counted wherever it appeared, so these threads carry it mid-line as the
# team used to leave it, and have to keep reading exactly as they did.
case_ "a comment older than ACK_FROM with no 👀 keeps the watermark rule, so an upgrade reopens nothing"
gh_thread <<'TALK'
body 2026-09-20T02:10:00Z reviewer 0 The pitch <!-- a-team:lead -->
comment 2026-09-20T02:28:46Z reviewer 0 And do the same for subissue links.
comment 2026-09-20T02:33:03Z reviewer 0 Added one. <!-- a-team:lead -->
TALK
run board demo feedback lead 7
same "exit" 0 "$STATUS"
same "unanswered" '[]' "$(jq -c . "$OUT")"

case_ "an older comment the role never answered is still returned"
gh_thread <<'TALK'
body 2026-09-20T02:10:00Z reviewer 0 The pitch <!-- a-team:lead -->
comment 2026-09-20T02:33:03Z reviewer 0 Drafted. <!-- a-team:lead -->
comment 2026-09-20T02:40:00Z reviewer 0 Still needs a second option.
TALK
run board demo feedback lead 7
same "exit" 0 "$STATUS"
same "unanswered" '["Still needs a second option."]' "$(jq -c '[.[].body]' "$OUT")"

# Before APP_FROM the team spoke as the reviewer, and the marker counted only alone on the body's
# last line. GitHub's Quote reply copies a team comment's source, marker and all, into your own words.
case_ "a Quote reply carrying the team's marker is your feedback"
gh_thread <<TALK
body ${TODAY}T02:10:00Z demo-app[bot] 0 The pitch\n<!-- a-team:lead -->
comment ${TODAY}T02:20:00Z reviewer 1 Needs a second option.
comment ${TODAY}T02:28:46Z reviewer 0 > The pitch\n> <!-- a-team:lead -->\n\nAnd include the path.
TALK
run board demo feedback lead 7
same "exit" 0 "$STATUS"
same "unanswered" "[\"${TODAY}T02:28:46Z\"]" "$(jq -c '[.[].at]' "$OUT")"

case_ "triggers names the Quote reply too, so a run starts for it"
gh_recent <<TALK
7 ${TODAY}T02:20:00Z reviewer 1 Needs a second option.
7 ${TODAY}T02:28:46Z reviewer 0 > The pitch\n> <!-- a-team:lead -->\n\nAnd include the path.
TALK
run board demo triggers lead
same "exit" 0 "$STATUS"
same "reasons" "[\"stakeholder feedback on #7 (${TODAY}T02:28:46Z)\"]" "$(jq -c .reasons "$OUT")"

case_ "waiting says the same: the Quote reply makes the gate the Lead's"
gh_talk <<TALK
7 body ${TODAY}T02:10:00Z demo-app[bot] The pitch\n<!-- a-team:lead -->
7 comment+seen ${TODAY}T02:20:00Z reviewer Needs a second option.
7 comment ${TODAY}T02:28:46Z reviewer > The pitch\n> <!-- a-team:lead -->\n\nAnd include the path.
TALK
waiting
same "exit" 0 "$STATUS"
same "turn" '"lead"' "$(jq -c '.[0].turn' "$OUT")"
same "reason" '"answering your feedback since 02:28"' "$(jq -c '.[0].reason' "$OUT")"

case_ "a Quote reply that is your only comment is returned, not dropped"
gh_thread <<'TALK'
body 2026-09-25T02:10:00Z reviewer 0 The pitch\n<!-- a-team:lead -->
comment 2026-09-25T02:28:46Z reviewer 0 > The pitch\n> <!-- a-team:lead -->\n\nAnd include the path.
TALK
run board demo feedback lead 7
same "exit" 0 "$STATUS"
same "unanswered" '["2026-09-25T02:28:46Z"]' "$(jq -c '[.[].at]' "$OUT")"

case_ "comment acks a Quote reply, so it doesn't come back once answered"
: >"$ACKED"
export A_TEAM_RUN_STARTED=2026-09-25T03:00:00Z
run board demo comment lead 7 "$WORK/reply"
same "exit" 0 "$STATUS"
same "acked" "IC_1" "$(cat "$ACKED")"
unset A_TEAM_RUN_STARTED

case_ "before APP_FROM, a marker mid-sentence or in a fenced code block is your feedback"
gh_thread <<'TALK'
body 2026-09-25T02:10:00Z reviewer 0 The pitch\n<!-- a-team:lead -->
comment 2026-09-25T02:20:00Z reviewer 0 Does <!-- a-team:lead --> have to be last?
comment 2026-09-25T02:28:46Z reviewer 0 Every body ends with\n```\n<!-- a-team:lead -->\n```
TALK
run board demo feedback lead 7
same "exit" 0 "$STATUS"
same "unanswered" '["2026-09-25T02:20:00Z","2026-09-25T02:28:46Z"]' "$(jq -c '[.[].at]' "$OUT")"

case_ "before APP_FROM, the team's own comment under your login is not feedback, so an upgrade reopens nothing"
gh_thread <<'TALK'
body 2026-09-25T02:10:00Z reviewer 0 The pitch\n<!-- a-team:lead -->
comment 2026-09-25T02:33:03Z reviewer 0 Added one.\n<!-- a-team:lead -->
TALK
run board demo feedback lead 7
same "exit" 0 "$STATUS"
same "unanswered" '[]' "$(jq -c . "$OUT")"

# Since APP_FROM the team speaks only as its App, so the author alone says whose words a comment is.
case_ "after APP_FROM, the reviewer's comment is feedback whatever its body, marker on its last line or not"
gh_thread <<'TALK'
body 2026-10-01T02:10:00Z demo-app[bot] 0 The pitch\n<!-- a-team:lead -->
comment 2026-10-01T02:20:00Z reviewer 0 Pasted from the other thread:\n\nDrafted.\n<!-- a-team:lead -->
comment 2026-10-01T02:28:46Z reviewer 0 Does <!-- a-team:dev --> have to be last?
TALK
run board demo feedback lead 7
same "exit" 0 "$STATUS"
same "unanswered" '["2026-10-01T02:20:00Z","2026-10-01T02:28:46Z"]' "$(jq -c '[.[].at]' "$OUT")"

case_ "comment acks it too"
: >"$ACKED"
export A_TEAM_RUN_STARTED=2026-10-01T03:00:00Z
run board demo comment lead 7 "$WORK/reply"
same "exit" 0 "$STATUS"
same "acked" "IC_1
IC_2" "$(cat "$ACKED")"
unset A_TEAM_RUN_STARTED

case_ "the App's comments and reviews are never feedback, however GitHub spells its login"
gh_thread pull <<'TALK'
body 2026-10-01T02:10:00Z demo-app[bot] 0 Closes #7\n<!-- a-team:dev -->
comment 2026-10-01T02:20:00Z demo-app[bot] 0 A comment that lost its marker.
review 2026-10-01T02:25:00Z demo-app[bot] 0 A review that lost its marker.
line 2026-10-01T02:28:46Z demo-app[bot] 0 A line comment that lost its marker.
TALK
run board demo feedback dev 7
same "exit" 0 "$STATUS"
same "unanswered" '[]' "$(jq -c . "$OUT")"

case_ "triggers doesn't start a run for the App's own words either"
gh_recent <<'RECENT'
7 2026-10-01T02:20:00Z demo-app[bot] 0 A comment that lost its marker.
RECENT
run board demo triggers lead
same "exit" 0 "$STATUS"
same "reasons" '[]' "$(jq -c .reasons "$OUT")"

case_ "waiting reads the App from GraphQL's spelling, and each role from its own marker"
gh_items <<'ITEMS'
Pitched 106 Both gates are mine
In_review 115 I can change any of the keys
ITEMS
gh_talk <<TALK
106 body ${YESTERDAY}T08:00:00Z demo-app[bot] The pitch\n<!-- a-team:lead -->
106 comment ${YESTERDAY}T09:00:00Z demo-app[bot] Wrong thread.\n<!-- a-team:dev -->
106 comment ${YESTERDAY}T09:30:00Z demo-app[bot] A comment that lost its marker.
115 body ${YESTERDAY}T08:00:00Z demo-app[bot] The task\n<!-- a-team:lead -->
115 comment ${YESTERDAY}T09:00:00Z reviewer > Drafted.\n<!-- a-team:dev -->
TALK
waiting
same "exit" 0 "$STATUS"
same "pitch turn" '"you"' "$(jq -c '.[0].turn' "$OUT")"
same "pitch reason" "\"awaiting your approval since $(jq -rn 'now - 86400 | strftime("%d %b")') 08:00\"" "$(jq -c '.[0].reason' "$OUT")"
same "task turn" '"dev"' "$(jq -c '.[1].turn' "$OUT")"
gh_items <<'ITEMS'
Pitched 7 A pitch in front of me
ITEMS

# Stakeholders: whoever the config lists, not one login, is who the team answers.
KEPT_CONFIG=$CONFIG KEPT_TEAM=$TEAM
case_ "a second stakeholder's comment is feedback, and a stranger's isn't"
fixture <<'JSON'
{ "repo": "mentaldesk/demo", "stakeholders": ["reviewer", "second"], "app": { "id": 7, "slug": "demo-app" },
  "project": { "owner": "mentaldesk", "number": 1 } }
JSON
gh_thread <<'TALK'
body 2026-10-01T02:10:00Z demo-app[bot] 0 The pitch\n<!-- a-team:lead -->
comment 2026-10-01T02:20:00Z second 0 Needs a second option.
comment 2026-10-01T02:28:46Z stranger 0 Ship it now.
TALK
run board demo feedback lead 7
same "exit" 0 "$STATUS"
same "unanswered" '["Needs a second option."]' "$(jq -c '[.[].body]' "$OUT")"

case_ "comment acks the second stakeholder's, and leaves the stranger's alone"
: >"$ACKED"
export A_TEAM_RUN_STARTED=2026-10-01T03:00:00Z
run board demo comment lead 7 "$WORK/reply"
same "exit" 0 "$STATUS"
same "acked" "IC_1" "$(cat "$ACKED")"
unset A_TEAM_RUN_STARTED

case_ "triggers starts a run for the second stakeholder, and not the stranger"
gh_recent <<RECENT
7 ${TODAY}T02:20:00Z second 0 Needs a second option.
7 ${TODAY}T02:28:46Z stranger 0 Ship it now.
RECENT
run board demo triggers lead
same "exit" 0 "$STATUS"
same "reasons" "[\"stakeholder feedback on #7 (${TODAY}T02:20:00Z)\"]" "$(jq -c .reasons "$OUT")"

case_ "waiting hands the gate to the Lead for the second stakeholder, and not for the stranger"
gh_items <<'ITEMS'
Pitched 106 Both gates are mine
Pitched 107 Someone else's opinion
ITEMS
gh_talk <<'TALK'
106 body 2026-10-01T08:00:00Z demo-app[bot] The pitch\n<!-- a-team:lead -->
106 comment 2026-10-01T09:30:00Z second What about the second gate?
107 body 2026-10-01T08:00:00Z demo-app[bot] The pitch\n<!-- a-team:lead -->
107 comment 2026-10-01T09:30:00Z stranger What about the second gate?
TALK
waiting
same "exit" 0 "$STATUS"
same "turns" '["lead","you"]' "$(jq -c '[.[].turn]' "$OUT")"

case_ "a config that still says reviewer answers that one person, as before"
fixture <<'JSON'
{ "repo": "mentaldesk/demo", "reviewer": "reviewer", "app": { "id": 7, "slug": "demo-app" },
  "project": { "owner": "mentaldesk", "number": 1 } }
JSON
gh_thread <<'TALK'
body 2026-10-01T02:10:00Z demo-app[bot] 0 The pitch\n<!-- a-team:lead -->
comment 2026-10-01T02:20:00Z reviewer 0 Needs a second option.
comment 2026-10-01T02:28:46Z second 0 Ship it now.
TALK
run board demo feedback lead 7
same "exit" 0 "$STATUS"
same "unanswered" '["Needs a second option."]' "$(jq -c '[.[].body]' "$OUT")"
CONFIG=$KEPT_CONFIG TEAM=$KEPT_TEAM
gh_items <<'ITEMS'
Pitched 7 A pitch in front of me
ITEMS

# Unset, A_TEAM_RUN_STARTED means now, so these threads are dated back from it, not from an hour of
# the day the suite could be running before.
OPENED=$(ago 90) ASKED=$(ago 60) ASKED_AGAIN=$(ago 50)

case_ "a review and a line comment on a PR are acked like any other comment"
gh_thread pull <<TALK
body $OPENED demo-app[bot] 0 Closes #7\n<!-- a-team:dev -->
review $ASKED reviewer 0 Nearly there.
line $ASKED_AGAIN reviewer 0 This name reads oddly.
TALK
: >"$ACKED"
A_TEAM_RUN_STARTED=$(ago 30)
export A_TEAM_RUN_STARTED
run board demo comment dev 7 "$WORK/reply"
same "exit" 0 "$STATUS"
same "acked" "IC_1 IC_2" "$(tr '\n' ' ' <"$ACKED" | sed 's/ $//')"
unset A_TEAM_RUN_STARTED

case_ "A_TEAM_RUN_STARTED unset acks the whole thread: whoever ran it by hand has just read it"
: >"$ACKED"
run board demo comment dev 7 "$WORK/reply"
same "exit" 0 "$STATUS"
same "acked" "IC_1 IC_2" "$(tr '\n' ' ' <"$ACKED" | sed 's/ $//')"

case_ "a comment already carrying a 👀 is not acked again"
gh_thread pull <<TALK
body $OPENED demo-app[bot] 0 Closes #7\n<!-- a-team:dev -->
review $ASKED reviewer 1 Nearly there.
line $ASKED_AGAIN reviewer 0 This name reads oddly.
TALK
: >"$ACKED"
run board demo comment dev 7 "$WORK/reply"
same "exit" 0 "$STATUS"
same "acked" "IC_2" "$(cat "$ACKED")"

case_ "a comment from anyone but the reviewer is not acked"
gh_thread <<TALK
body $OPENED demo-app[bot] 0 The pitch\n<!-- a-team:lead -->
comment $ASKED passer-by 0 Have you considered doing it differently?
TALK
: >"$ACKED"
run board demo comment lead 7 "$WORK/reply"
same "exit" 0 "$STATUS"
same "acked" "" "$(cat "$ACKED")"

case_ "skip acks too, or the Idea it just passed over would be back in the running at once"
gh_items <<'ITEMS'
Idea 7 An Idea with nothing to pitch in it
ITEMS
gh_thread <<TALK
body $OPENED demo-app[bot] 0 The idea\n<!-- a-team:lead -->
comment $ASKED reviewer 0 Worth a look.
TALK
: >"$ACKED"
run board demo skip lead 7 "$WORK/reply"
same "exit" 0 "$STATUS"
same "acked" "IC_1" "$(cat "$ACKED")"

case_ "--dry-run says which reactions it would add and adds none"
gh_thread <<TALK
body $OPENED demo-app[bot] 0 The pitch\n<!-- a-team:lead -->
comment $ASKED reviewer 0 Needs a second option.
TALK
: >"$ACKED"
: >"$POSTED"
run board --dry-run demo comment lead 7 "$WORK/reply"
same "exit" 0 "$STATUS"
same "acked" "" "$(cat "$ACKED")"
same "posted" "" "$(cat "$POSTED")"
grep -q "would add 👀 to your comment of $ASKED on #7" "$ERR" || fail "dry run: nothing about the 👀 in '$(cat "$ERR")'"

finish_suite
