#!/usr/bin/env bash
# The team's GitHub App: tokens, wrappers, check, app create.
# shellcheck source=tests/scripts/lib.sh
source "$(dirname "${BASH_SOURCE[0]}")/lib.sh"
# shellcheck disable=SC2034,SC2154 # helpers read what an area sets, and an area reads what lib.sh sets

# --- the team's GitHub App -------------------------------------------------------------------
app_fakes
export A_TEAM_STATE
A_TEAM_STATE=$(mktemp -d "$WORK/state.XXXXXX")
CACHE="$A_TEAM_STATE/demo/token.json"
mints() { grep -c '' "$MINTS" 2>/dev/null || echo 0; }
# cached <token> <seconds left>
cached() {
  mkdir -p "$(dirname "$CACHE")"
  jq -n --arg t "$1" --argjson left "$2" --slurpfile p "$APP_BIN/perms.json" \
    '{token: $t, expires_at: (now + $left | floor | todate), permissions: $p[0]}' >"$CACHE"
}
b64url_decode() {
  local s
  s=$(tr '_-' '/+')
  while [ $((${#s} % 4)) -ne 0 ]; do s="$s="; done
  printf '%s' "$s" | openssl base64 -d -A
}
mkdir -p "$APP_BIN/work/main"
app_fixture() {
  fixture <<JSON
{ "repo": "mentaldesk/demo", "reviewer": "reviewer", "project": { "owner": "mentaldesk", "number": 1 },
  "vision": "docs/vision.md", "workdir": "$APP_BIN/work", "app": { "id": 7, "slug": "demo-app" } }
JSON
  rm -f "$CACHE" "$MINTS"
}

case_ "token mints one when nothing is cached, with a JWT the App's key signed"
app_fixture
run token demo
same "exit" 0 "$STATUS"
same "mints" 1 "$(mints)"
same "cached" "$(cat "$OUT")" "$(jq -r .token "$CACHE")"
IFS=. read -r jwt_header jwt_payload jwt_signature <"$APP_BIN/jwt"
same "issuer" '"7"' "$(b64url_decode <<<"$jwt_payload" | jq -c .iss)"
same "backdated" 600 "$(b64url_decode <<<"$jwt_payload" | jq '.exp - .iat')"
b64url_decode <<<"$jwt_signature" >"$APP_BIN/signature"
printf '%s.%s' "$jwt_header" "$jwt_payload" |
  openssl dgst -sha256 -verify "$APP_BIN/pub.pem" -signature "$APP_BIN/signature" >/dev/null ||
  fail "jwt: the signature doesn't verify against the App's key"

case_ "run again straight away, it prints the same token and mints nothing"
first=$(cat "$OUT")
run token demo
same "token" "$first" "$(cat "$OUT")"
same "mints" 1 "$(mints)"

case_ "with under five minutes left, it mints a new one"
cached ghs_expiring 280
run token demo
same "exit" 0 "$STATUS"
same "mints" 2 "$(mints)"
[ "$(cat "$OUT")" != ghs_expiring ] || fail "expiring: the old token was passed on"

case_ "with five minutes left, it's still reused"
cached ghs_fresh 330
run token demo
same "token" ghs_fresh "$(cat "$OUT")"

case_ "a malformed or empty cache is re-minted rather than passed on"
for broken in 'not json' '' '{"token": "", "expires_at": "2099-01-01T00:00:00Z"}' '{"token": "ghs_x"}'; do
  rm -f "$MINTS"
  printf '%s' "$broken" >"$CACHE"
  run token demo
  same "exit ($broken)" 0 "$STATUS"
  same "mints ($broken)" 1 "$(mints)"
  same "token ($broken)" "$(jq -r .token "$CACHE")" "$(cat "$OUT")"
done

case_ "two concurrent mints leave a readable cache holding one of their tokens"
rm -f "$CACHE"
A_TEAM_CONFIG="$CONFIG" "$A_TEAM" token demo >"$WORK/mint1" 2>&1 &
A_TEAM_CONFIG="$CONFIG" "$A_TEAM" token demo >"$WORK/mint2" 2>&1 &
wait
winner=$(jq -r .token "$CACHE")
{ [ "$winner" = "$(cat "$WORK/mint1")" ] || [ "$winner" = "$(cat "$WORK/mint2")" ]; } ||
  fail "concurrent: cache holds '$winner', not either run's token"
same "leftovers" 1 "$(find "$(dirname "$CACHE")" -name 'token.json*' | grep -c '')"

case_ "a key GitHub turns down is blamed on the key, in a-team's words, not on the install"
rm -f "$CACHE"
MINT_FAILS=1 run token demo
failed "wrong key"
grep -q "the one in the Keychain under account mentaldesk isn't that App's (Bad credentials (HTTP 401))" "$ERR" ||
  fail "wrong key: '$(cat "$ERR")'"
grep -q 'install' "$ERR" && fail "wrong key: told to install it: '$(cat "$ERR")'"
grep -q 'curl\|{' "$ERR" && fail "wrong key: curl's own output got through: '$(cat "$ERR")'"

case_ "an App that isn't installed on the repo is told where to install it"
NOT_INSTALLED=1 run token demo
failed "not installed"
grep -q "app 7 isn't installed on mentaldesk/demo: install it at https://github.com/apps/demo-app/installations/new" "$ERR" ||
  fail "not installed: '$(cat "$ERR")'"

case_ "a team with no app key has no token, and is told how to get one"
NO_APP=1 fixture <<'JSON'
{ "repo": "mentaldesk/demo", "reviewer": "reviewer", "project": { "owner": "mentaldesk", "number": 1 } }
JSON
run token demo
failed "no app"
grep -q 'a-team app create demo' "$ERR" || fail "no app: '$(cat "$ERR")'"

# The real gh the wrapper stands in front of: prints GH_TOKEN, then each argument on its own line.
mkdir -p "$APP_BIN/real"
printf '#!/usr/bin/env bash\necho "GH_TOKEN=${GH_TOKEN:-}"\nprintf "[%%s]\\n" "$@"\n' | fake "$APP_BIN/real/gh"
wrapped() {
  A_TEAM_CONFIG="$CONFIG" PATH="$ROOT/bin:$APP_BIN/real:$PATH" gh "$@" >"$OUT" 2>"$ERR"
  STATUS=$?
}

case_ "the gh wrapper passes arguments through unchanged, as the run's team's App"
app_fixture
cached ghs_cached 3600
A_TEAM_RUN_TEAM=demo wrapped api 'a b' '' '$x' "it's"
same "exit" 0 "$STATUS"
same "output" 'GH_TOKEN=ghs_cached
[api]
[a b]
[]
[$x]
[it'"'"'s]' "$(cat "$OUT")"
same "caller's GH_TOKEN" '' "${GH_TOKEN:-}"

case_ "outside a run, it's the real gh as it was"
A_TEAM_RUN_TEAM='' wrapped pr list
same "no run" 'GH_TOKEN=
[pr]
[list]' "$(cat "$OUT")"

case_ "in a run of a team with no app key, it fails and names the command, rather than running gh as you"
NO_APP=1 fixture <<'JSON'
{ "repo": "mentaldesk/demo" }
JSON
A_TEAM_RUN_TEAM=demo wrapped pr list
failed "no app"
grep -q 'GH_TOKEN' "$OUT" && fail "no app: the real gh ran"
grep -q 'a-team app create demo, then install it' "$ERR" || fail "no app: '$(cat "$ERR")'"

case_ "a team whose token won't mint fails, rather than running gh as you"
app_fixture
A_TEAM_RUN_TEAM=demo NO_KEY=1 wrapped pr list
failed "no key"
grep -q 'GH_TOKEN' "$OUT" && fail "no key: the real gh ran"

# A second copy of a-team, like an installed one alongside a worktree.
COPY=$(mktemp -d "$WORK/copy.XXXXXX")
cp -R "$ROOT/bin" "$ROOT/scripts" "$COPY/"
# Runs "$@" into $OUT and $ERR, killing it after 10 seconds so a loop fails instead of hanging.
within() {
  "$@" >"$OUT" 2>"$ERR" &
  local pid=$! watchdog
  (sleep 10; kill "$pid") >/dev/null 2>&1 &
  watchdog=$!
  wait "$pid"
  STATUS=$?
  kill "$watchdog" 2>/dev/null
}

for order in "$ROOT/bin:$COPY/bin" "$COPY/bin:$ROOT/bin"; do
  case_ "with two copies of the wrappers on PATH ($order), gh and git return, as the App in a run"
  app_fixture
  cached ghs_cached 3600
  wrappers="$order:$APP_BIN/real:$PATH"
  A_TEAM_RUN_TEAM='' A_TEAM_CONFIG="$CONFIG" PATH="$wrappers" within gh --version
  same "gh outside a run" 'GH_TOKEN=
[--version]' "$(cat "$OUT")"
  A_TEAM_RUN_TEAM=demo A_TEAM_CONFIG="$CONFIG" PATH="$wrappers" within gh --version
  same "gh in a run" 'GH_TOKEN=ghs_cached
[--version]' "$(cat "$OUT")"
  A_TEAM_RUN_TEAM='' A_TEAM_CONFIG="$CONFIG" PATH="$wrappers" within git --version
  same "git outside a run exit" 0 "$STATUS"
  grep -q '^git version' "$OUT" || fail "git outside a run: '$(cat "$OUT" "$ERR")'"
  A_TEAM_RUN_TEAM=demo A_TEAM_CONFIG="$CONFIG" PATH="$wrappers" within git config user.name
  same "git in a run" 'demo-app[bot]' "$(cat "$OUT")"
done

# board.sh's check against a board that's fine, with the App's own view of it broken by
# PROJECT_UNREADABLE or PRIORITY_UNREADABLE when it asks with the cached token, and the board itself
# by REPO_GONE, NO_VISION, LEAD_VISION, NO_PROJECT, NO_FIELD, and the options in meta.json, labels in labels.json
# and the project's enabled workflows in workflows.json.
mkdir -p "$APP_BIN/board"
board_options() {
  jq -n --args '{data: {organization: {projectV2: {id: "PVT_1", fields: {nodes: [{id: "PVTSSF_status", name: "Status", options:
    ($ARGS.positional | map({id: ., name: .}))}]}}}}}' "$@" >"$APP_BIN/board/meta.json"
}
board_options Idea Exploring Pitched Approved Building Ready "In progress" "In review" Done
board_labels() { jq -n --args '$ARGS.positional | map({name: .})' "$@" >"$APP_BIN/board/labels.json"; }
board_labels pitch a-team:dev a-team:idea a-team:skipped a-team:displaced a-team:shelved blocked
board_workflows() {
  jq -n --args '{data: {organization: {projectV2: {workflows: {nodes:
    ($ARGS.positional | map({name: ., enabled: true}) + [{name: "Pull request merged", enabled: false}])}}}}}' \
    "$@" >"$APP_BIN/board/workflows.json"
}
board_workflows "Auto-add to project" "Item added to project" "Item closed"
echo '{"data": {"organization": {"projectV2": {"items": {"totalCount": 0,
  "pageInfo": {"hasNextPage": false, "endCursor": null}, "nodes": []}}}}}' >"$APP_BIN/board/items.json"
echo '{"data": {"organization": {"issueFields": {"nodes": [{"id": "IF_priority", "name": "Priority",
  "options": []}]}}}}' >"$APP_BIN/board/fields.json"
fake "$APP_BIN/board/gh" <<SH
#!/usr/bin/env bash
refused() { echo "gh: Resource not accessible by integration" >&2; exit 1; }
case " \$* " in
  *" repos/mentaldesk/demo/contents/\${CONTENTS_DOWN:-none} "*) echo "gh: Bad Gateway (HTTP 502)" >&2; exit 1 ;;
  *" repos/mentaldesk/demo/contents/"*) [ -z "\${NO_VISION:-}" ] || { echo "gh: Not Found (HTTP 404)" >&2; exit 1; }
    [ -z "\${LEAD_VISION:-}" ] || printf '# Vision\n\nWho it is for.\n\n<!-- a-team:lead -->\n'; exit 0 ;;
  *" repos/mentaldesk/demo "*) [ -z "\${REPO_GONE:-}" ] || { echo "gh: Not Found (HTTP 404)" >&2; exit 1; }; exit 0 ;;
  *"/actions/workflows/release.yml "*) [ -z "\${RELEASE_DOWN:-}" ] || { echo "error connecting to api.github.com" >&2; exit 1; }
    [ -z "\${NO_RELEASE_WORKFLOW:-}" ] || { echo "gh: Not Found (HTTP 404)" >&2; exit 1; }; exit 0 ;;
  *"label list"*) page="$APP_BIN/board/labels.json" ;;
  *"workflows(first"*) page="$APP_BIN/board/workflows.json" ;;
  *ProjectV2SingleSelectField*)
    [ -z "\${NO_PROJECT:-}" ] || { echo '{"data": {"organization": {"projectV2": null}}}'
      echo "gh: Could not resolve to a ProjectV2 with the number 1." >&2; exit 1; }
    [ -z "\${NO_FIELD:-}" ] || { echo '{"data": {"organization": {"projectV2": {"id": "PVT_1", "fields": {"nodes": []}}}}}'; exit 0; }
    [ -z "\${PARTIAL_READ:-}" ] || { cat "$APP_BIN/board/meta.json"; echo "gh: Resource not accessible by integration" >&2; exit 1; }
    page="$APP_BIN/board/meta.json" ;;
  *"items(first: 1) { totalCount }"*) [ "\${GH_TOKEN:-}" = ghs_cached ] && [ -z "\${PROJECT_UNREADABLE:-}" ] || refused
                page="$APP_BIN/board/items.json" ;;
  *issueFields*) [ "\${GH_TOKEN:-}" = ghs_cached ] && [ -z "\${PRIORITY_UNREADABLE:-}" ] || refused
                 page="$APP_BIN/board/fields.json" ;;
  *) page="$APP_BIN/board/items.json" ;;
esac
filter=
while [ \$# -gt 0 ]; do
  [ "\$1" = --jq ] && { filter=\$2; break; }
  shift
done
if [ -n "\$filter" ]; then jq -r "\$filter" "\$page"; else cat "\$page"; fi
SH
grants() { jq "$1" "$CACHE" >"$CACHE.new" && mv "$CACHE.new" "$CACHE"; }

case_ "check fails a team with no app key, and names the command"
fixture <<'JSON'
{ "repo": "mentaldesk/demo", "reviewer": "reviewer", "project": { "owner": "mentaldesk", "number": 1 } }
JSON
checked
same "no app exit" 1 "$STATUS"
grep -q '^app       demo has no GitHub App: run a-team app create demo, then install it$' "$OUT" ||
  fail "no app: '$(cat "$OUT")'"

case_ "check reports the App's identity when every part of it works"
app_fixture
cached ghs_cached 3600
checked
same "exit" 0 "$STATUS"
same "identity" 'identity: demo-app[bot] · token ok · project 1 read+write ok · Priority readable · push access to mentaldesk/demo ok' \
  "$(grep '^identity' "$OUT")"

case_ "check says which part of the identity is wrong, and what to do about it"
NO_KEY=1 checked
says 1 "app       demo-app[bot] has no key in the login Keychain (service 'a-team-app', account 'mentaldesk'): generate a new private key on the App's settings page on GitHub, then run: security add-generic-password -U -s a-team-app -a mentaldesk -w \"\$(openssl base64 -A -in <the .pem you downloaded>)\""
rm -f "$CACHE"
MINT_FAILS=1 checked
same "no token exit" 1 "$STATUS"
grep -q "^app       demo-app\[bot\] can't get a token: " "$OUT" || fail "no token: '$(cat "$OUT")'"
cached ghs_cached 3600
PROJECT_UNREADABLE=1 checked
says 1 "app       demo-app[bot] can't read project 1: grant the organisation's \"Projects: read and write\", and install the App on mentaldesk/demo"
grants '.permissions.organization_projects = "read"'
checked
says 1 "app       demo-app[bot] can only read project 1: grant the organisation's \"Projects: read and write\""
cached ghs_cached 3600
PRIORITY_UNREADABLE=1 checked
same "priority exit" 1 "$STATUS"
grep -q "^app       demo-app\[bot\] can't read the Priority field: grant the organisation's \"Issue Fields: read\"" "$OUT" ||
  fail "priority: '$(cat "$OUT")'"
grants '.permissions.contents = "read"'
checked
says 1 "app       demo-app[bot] can't push to mentaldesk/demo: grant the repository's \"Contents: read and write\""

case_ "check names each problem with the board in a line of its own"
cached ghs_cached 3600
checked
says 0 ""
REPO_GONE=1 checked
says 1 "repo      can't reach mentaldesk/demo: Not Found (HTTP 404)"
NO_PROJECT=1 checked
says 1 "project   can't read mentaldesk project 1: Could not resolve to a ProjectV2 with the number 1."
grep -q '^identity: demo-app\[bot\] · token ok · Priority readable' "$OUT" || fail "no project: the App's view in '$(cat "$OUT")'"
NO_FIELD=1 checked
says 1 "project   no single-select field 'Status' on mentaldesk project 1"
PARTIAL_READ=1 checked
says 1 "project   can't read mentaldesk project 1: Resource not accessible by integration"
board_options Idea Approved Building Ready "In progress" "In review" Done
checked
says 1 "status    2 of 9 options missing from 'Status': Exploring, Pitched"
board_options Idea Exploring Pitched Approved Building Ready "In progress" "In review" Done

case_ "check names what's missing from the machine"
rm -rf "$APP_BIN/work/main"
checked
says 1 "checkout  $APP_BIN/work/main isn't there: gh repo clone mentaldesk/demo $APP_BIN/work/main"
rm -rf "$APP_BIN/work"
checked
says 1 "workdir   $APP_BIN/work isn't there, so the agents would have nothing to work in"
mkdir -p "$APP_BIN/work/main"

case_ "a missing or Lead-drafted vision, or missing labels, are problems the team can still run with, so check exits 2"
NO_VISION=1 checked
says 2 "vision    docs/vision.md isn't in mentaldesk/demo yet: the Lead will draft one and open it as a draft PR"
LEAD_VISION=1 checked
says 2 "vision    drafted by the Lead: Write the vision with me replaces it"
CONTENTS_DOWN=docs/vision.md checked
says 2 "vision    GitHub didn't answer for docs/vision.md: checking again next pass"
board_labels
checked
says 2 "labels    no 'pitch' label, so the team can't tell its pitches from tasks"
board_labels pitch

case_ "with a Customer lead, a docs page that's missing or not named is a problem the team can still run with"
jq '.roles.customer = true' "$TEAM" >"$TEAM.new" && mv "$TEAM.new" "$TEAM"
board_labels pitch a-team:dev a-team:idea a-team:skipped a-team:displaced a-team:shelved blocked a-team:customer
checked
says 2 "docs      demo.json names no docs page, so the Customer lead has nothing to keep right"
jq '.docs = "docs/index.md"' "$TEAM" >"$TEAM.new" && mv "$TEAM.new" "$TEAM"
checked
says 0 ""
NO_VISION=1 checked
says 2 "vision    docs/vision.md isn't in mentaldesk/demo yet: the Lead will draft one and open it as a draft PR
docs      docs/index.md isn't in mentaldesk/demo yet: the Customer lead will propose the docs as a draft PR"
CONTENTS_DOWN=docs/index.md checked
says 2 "docs      GitHub didn't answer for docs/index.md: checking again next pass"
jq 'del(.roles, .docs)' "$TEAM" >"$TEAM.new" && mv "$TEAM.new" "$TEAM"
board_labels pitch a-team:dev a-team:idea a-team:skipped a-team:displaced a-team:shelved blocked
NO_VISION=1 checked
says 2 "vision    docs/vision.md isn't in mentaldesk/demo yet: the Lead will draft one and open it as a draft PR"

case_ "a release setting check can't act on is a problem the team can still run with"
jq '.release = "continuous"' "$TEAM" >"$TEAM.new" && mv "$TEAM.new" "$TEAM"
checked
says 0 ""
NO_RELEASE_WORKFLOW=1 checked
says 2 "release   release is continuous, but mentaldesk/demo has no release.yml workflow for it to run"
RELEASE_DOWN=1 checked
says 2 "release   GitHub didn't answer for release.yml: checking again next pass"
jq '.release = "weekly"' "$TEAM" >"$TEAM.new" && mv "$TEAM.new" "$TEAM"
checked
says 2 "release   release is 'weekly': expected never, daily or continuous"
jq 'del(.release)' "$TEAM" >"$TEAM.new" && mv "$TEAM.new" "$TEAM"

case_ "a project that won't put new issues in Triage is a problem the team can still run with"
board_workflows "Item closed"
checked
says 2 "workflow  \"Auto-add to project\" is off on mentaldesk project 1, so an issue opened on mentaldesk/demo, by you or another team, never reaches Triage
workflow  \"Item added to project\" is off on mentaldesk project 1, so an issue auto-added there gets no Status and never reaches Triage: turn it on, setting Status to Idea"
board_workflows "Auto-add to project (2)" "Item added to project"
checked
says 0 ""
NO_PROJECT=1 checked
says 1 "project   can't read mentaldesk project 1: Could not resolve to a ProjectV2 with the number 1."
echo '{"data": {"organization": {"projectV2": null}}}' >"$APP_BIN/board/workflows.json"
checked
says 0 ""
board_workflows "Auto-add to project" "Item added to project" "Item closed"

case_ "check reports every problem it finds, not just the first"
board_labels
NO_VISION=1 NO_FIELD=1 checked
says 1 "vision    docs/vision.md isn't in mentaldesk/demo yet: the Lead will draft one and open it as a draft PR
project   no single-select field 'Status' on mentaldesk project 1
labels    no 'pitch' label, so the team can't tell its pitches from tasks"
board_labels pitch

case_ "check reports a config it can't read, or one with no repo or project, as problems"
before=$(cat "$TEAM")
printf '{\n "repo": "mentaldesk/demo",\n}\n' >"$TEAM"
checked
same "unparsable exit" 1 "$STATUS"
grep -q "^config    can't read demo.json: .*line 3" "$OUT" || fail "unparsable: '$(cat "$OUT")'"
echo '["not a team"]' >"$TEAM"
checked
says 1 "config    demo.json isn't a JSON object"
echo "$before" | jq 'del(.repo, .project.number)' >"$TEAM"
checked
same "no repo exit" 1 "$STATUS"
grep -q '^repo      demo.json names no repo$' "$OUT" || fail "no repo: '$(cat "$OUT")'"
grep -q '^project   demo.json names no GitHub Project to copy the board to' "$OUT" || fail "no project number: '$(cat "$OUT")'"
echo "$before" >"$TEAM"

case_ "any other command refuses a config it can't read, in one line"
echo '["not a team"]' >"$TEAM"
PATH="$APP_BIN/board:$PATH" run board demo wip
failed "unreadable wip"
same "unreadable wip" "board.sh: demo.json isn't a JSON object" "$(cat "$ERR")"
echo "$before" >"$TEAM"

case_ "run brings the product checkout up to date before it prints the brief"
try_fixture
app_fixture
jq --arg c "$CHECKOUT" '.checkout = $c' "$TEAM" >"$TEAM.new" && mv "$TEAM.new" "$TEAM"
cached ghs_cached 3600
land main
PATH="$APP_BIN/board:$PATH" run run demo lead
same "exit" 0 "$STATUS"
same "checkout" "$TRY_LANDED" "$(git -C "$CHECKOUT" rev-parse HEAD)"
grep -q '^## Product checkout' "$OUT" && fail "up to date: told to read around the checkout"

case_ "run says to read the default branch when the checkout can't be fast-forwarded to it"
git -C "$CHECKOUT" -c user.email=test@example.com -c user.name=Test commit -q --allow-empty -m local
land main
PATH="$APP_BIN/board:$PATH" run run demo lead
same "exit" 0 "$STATUS"
grep -q "^$CHECKOUT could not be brought up to date with origin/main, so read the product repo from origin/main" "$OUT" ||
  fail "diverged: '$(cat "$OUT")'"

case_ "run starts the Lead and the Dev on a team with the Customer lead turned on"
jq '.roles.customer = true' "$TEAM" >"$TEAM.new" && mv "$TEAM.new" "$TEAM"
for role in lead dev; do
  PATH="$APP_BIN/board:$PATH" run run demo "$role"
  same "$role exit" 0 "$STATUS"
  grep -q "turned on" "$ERR" && fail "$role: '$(cat "$ERR")'"
done

case_ "examples/team.json carries the app key and stays valid"
jq -e 'has("app")' "$ROOT/examples/team.json" >/dev/null || fail "example: no app key"

case_ "app create reuses the App another team under the same owner already has"
NO_APP=1 fixture <<'JSON'
{ "repo": "mentaldesk/demo", "reviewer": "reviewer" }
JSON
echo '{ "repo": "mentaldesk/other", "app": { "id": 9, "slug": "shared-app" } }' >"$CONFIG/teams/other.json"
: >"$OPENED"
NOT_INSTALLED=1 run app create demo
same "exit" 0 "$STATUS"
same "app" '{"id":9,"slug":"shared-app"}' "$(jq -c .app "$TEAM")"
same "reviewer" '"reviewer"' "$(jq -c .reviewer "$TEAM")"
same "opened" 'https://github.com/apps/shared-app/installations/new' "$(cat "$OPENED")"

case_ "app create --no-open fails rather than opening the install page while the App isn't installed"
: >"$OPENED"
NOT_INSTALLED=1 run app create demo --no-open
failed "not installed"
grep -q "shared-app isn't installed on mentaldesk/demo yet" "$ERR" || fail "not installed: '$(cat "$ERR")'"
same "opened" '' "$(cat "$OPENED")"

case_ "app create again on a team whose App is installed doesn't reopen the install page"
: >"$OPENED"
run app create demo
same "exit" 0 "$STATUS"
grep -q 'installed    on mentaldesk/demo already' "$OUT" || fail "installed: '$(cat "$OUT")'"
same "opened" '' "$(cat "$OPENED")"

case_ "app create on a team whose App has lost its key says how to restore it, not to run app create"
app_fixture
NO_KEY=1 run app create demo
failed "lost key"
grep -q "no private key .*: generate a new private key on the App's settings page" "$ERR" || fail "lost key: '$(cat "$ERR")'"
grep -q 'app create' "$ERR" && fail "lost key: sent back to app create: '$(cat "$ERR")'"

case_ "app create again with a key that isn't the App's says so, rather than opening the install page"
MINT_FAILS=1 run app create demo
failed "wrong key"
grep -q "isn't that App's" "$ERR" || fail "wrong key: '$(cat "$ERR")'"
same "opened" '' "$(cat "$OPENED")"

case_ "app create with the owner's key in the Keychain but no team naming it needs the App's id"
NO_APP=1 fixture <<'JSON'
{ "repo": "mentaldesk/demo" }
JSON
run app create demo
failed "no id"
grep -q -- '--id' "$ERR" || fail "no id: '$(cat "$ERR")'"
run app create demo --id 7
same "exit" 0 "$STATUS"
same "app" '{"id":7,"slug":"demo-app"}' "$(jq -c .app "$TEAM")"

# credential <action> <host> [<path>]: asks the helper as git would.
credential() {
  printf 'protocol=https\nhost=%s\n%s\n' "$2" "${3:+path=$3}" |
    A_TEAM_CONFIG="$CONFIG" bash "$ROOT/scripts/credential.sh" demo "$1" >"$OUT" 2>"$ERR"
  STATUS=$?
}

case_ "the credential helper answers github.com with the App's token, in git's format"
app_fixture
cached ghs_cached 3600
credential get github.com mentaldesk/demo.git
same "exit" 0 "$STATUS"
same "answer" 'username=x-access-token
password=ghs_cached' "$(cat "$OUT")"

case_ "it says nothing to any other host, or to store and erase"
credential get gitlab.com mentaldesk/demo.git
same "other host" '' "$(cat "$OUT")"
credential store github.com mentaldesk/demo.git
same "store" '' "$(cat "$OUT")"

case_ "a repo the App can't reach makes git give up at once, saying where to install it"
credential get github.com mentaldesk/elsewhere.git
same "answer" 'quit=1' "$(cat "$OUT")"
grep -q "demo-app\[bot\] has no access to mentaldesk/elsewhere: install the App there at https://github.com/apps/demo-app/installations/new" "$ERR" ||
  fail "no access: '$(cat "$ERR")'"

case_ "a token that won't mint makes git give up at once, saying why"
rm -f "$CACHE"
NO_KEY=1 credential get github.com mentaldesk/demo.git
same "answer" 'quit=1' "$(cat "$OUT")"
grep -q 'no private key in the login Keychain' "$ERR" || fail "no key: '$(cat "$ERR")'"

REPO_DIR=$(mktemp -d "$WORK/repo.XXXXXX")
export GIT_CONFIG_GLOBAL="$APP_BIN/gitconfig" GIT_CONFIG_NOSYSTEM=1
printf '[user]\n\tname = Reviewer\n\temail = reviewer@example.com\n' >"$GIT_CONFIG_GLOBAL"
git -C "$REPO_DIR" init -q
# git as a run would find it: bin/ first on PATH.
run_git() {
  A_TEAM_CONFIG="$CONFIG" PATH="$ROOT/bin:$PATH" git -C "$REPO_DIR" "$@" >"$OUT" 2>"$ERR"
  STATUS=$?
}
last_commit() { git -C "$REPO_DIR" log -1 --format='%an <%ae> / %cn <%ce>'; }

case_ "in a run, a commit is the App's bot's, and outside one it's yours"
app_fixture
cached ghs_cached 3600
rm -f "$APP_BIN/lookups" "$A_TEAM_STATE/demo/bot.json"
global=$(cat "$GIT_CONFIG_GLOBAL")
A_TEAM_RUN_TEAM=demo run_git commit -q --allow-empty -m bot
same "exit" 0 "$STATUS"
same "in a run" 'demo-app[bot] <99+demo-app[bot]@users.noreply.github.com> / demo-app[bot] <99+demo-app[bot]@users.noreply.github.com>' \
  "$(last_commit)"
run_git commit -q --allow-empty -m me
same "outside" 'Reviewer <reviewer@example.com> / Reviewer <reviewer@example.com>' "$(last_commit)"
A_TEAM_RUN_TEAM=demo run_git commit -q --allow-empty -m again
same "bot looked up once" 1 "$(grep -c '' "$APP_BIN/lookups")"
same "global config" "$global" "$(cat "$GIT_CONFIG_GLOBAL")"
same "repo config" '' "$(git -C "$REPO_DIR" config --local --get-regexp '^(user|credential)\.')"
same "caller's GIT_CONFIG_COUNT" '' "${GIT_CONFIG_COUNT:-}"

case_ "in a run, git asks only the App's helper, and gets the token"
A_TEAM_RUN_TEAM=demo run_git config --get-all credential.helper
same "helpers" "
!bash '$ROOT/bin/../scripts/credential.sh' 'demo'" "$(cat "$OUT")"
printf 'protocol=https\nhost=github.com\npath=mentaldesk/demo.git\n\n' |
  A_TEAM_RUN_TEAM=demo run_git credential fill
grep -qx 'password=ghs_cached' "$OUT" || fail "fill: '$(cat "$OUT")'"

case_ "config the caller already passes in GIT_CONFIG_COUNT still counts"
GIT_CONFIG_COUNT=1 GIT_CONFIG_KEY_0=a-team.test GIT_CONFIG_VALUE_0=kept A_TEAM_RUN_TEAM=demo run_git config a-team.test
same "kept" kept "$(cat "$OUT")"

case_ "a team with no app key gets no helper and no identity"
fixture <<'JSON'
{ "repo": "mentaldesk/demo" }
JSON
A_TEAM_RUN_TEAM=demo run_git config --get-all credential.helper
same "helpers" '' "$(cat "$OUT")"
A_TEAM_RUN_TEAM=demo run_git commit -q --allow-empty -m no-app
same "no app" 'Reviewer <reviewer@example.com> / Reviewer <reviewer@example.com>' "$(last_commit)"
unset GIT_CONFIG_GLOBAL GIT_CONFIG_NOSYSTEM
unset A_TEAM_STATE


export A_TEAM_STATE
A_TEAM_STATE=$(mktemp -d "$WORK/state.XXXXXX")
fixture <<'JSON'
{ "repo": "mentaldesk/demo", "reviewer": "reviewer", "app": { "id": 7, "slug": "demo-app" }, "project": { "owner": "mentaldesk", "number": 1 },
  "wip": { "worktrees": 2 } }
JSON

case_ "a card with nothing recorded reads as empty, with no record file to read"
run board demo history 7
same "exit" 0 "$STATUS"
same "empty" '{"since":null,"events":[]}' "$(jq -c . "$OUT")"
unrecorded "history"

case_ "each move the board makes is one event, saying who and from where to where"
gh_items <<'ITEMS'
Ready 12 A task
Pitched 7 A pitch
In_review 8 A task in front of me
Idea 6 An idea
ITEMS
claimable
jq '.data.organization.projectV2.fields.nodes[0].options += [{id: "OPT_review", name: "In review"}]' "$META" >"$META.new" && mv "$META.new" "$META"
run board demo move dev 12 "In progress"
recorded "move" 12 "dev Ready → In progress"
run board demo claim dev 12
recorded "claim" 12 "dev Ready → In progress"
run board demo approve you 7
recorded "approve" 7 "you Pitched → Approved"
run board demo priority you 6 High
recorded "priority" 6 "you Priority set to High"
run board demo priority you 6 none
recorded "priority cleared" 6 "you Priority cleared"
gh_pr 908 false
echo '{"head": {"sha": "deadbeefcafe", "ref": "feat/task", "repo": {"full_name": "mentaldesk/demo"}}}' >"$PULL"
run board demo accept you 8
recorded "accept" 8 "you accepted · PR #908 merged"

case_ "putting an item on the board reads as added as its status"
gh_items <<'ITEMS'
Pitched 7 A pitch
Idea 6 An idea
Building 10 A pitch
ITEMS
claimable
gh_child 41 10 -
run board demo add lead 41 Ready
recorded "add" 41 "lead added as Ready"

case_ "comments, skips, links and dependencies are each one event"
echo "A reply." >"$WORK/reply"
run board demo comment lead 7 "$WORK/reply"
recorded "comment" 7 "lead commented"
run board demo comment you 7 "$WORK/reply"
recorded "comment you" 7 "you commented"
run board demo skip lead 6 "$WORK/reply"
recorded "skip" 6 "lead skipped"
gh_items <<'ITEMS'
Building 7 A pitch whose tasks have all merged
ITEMS
echo '[{"number": 11, "state": "closed"}]' >"$SUBS"
run board demo finish lead 7 "$WORK/reply"
recorded "finish" 7 "lead closed as done"
echo '[]' >"$SUBS"
echo '{"merged": true}' >"$PULL"
run board demo finish lead 7 "$WORK/reply" 532
recorded "finish built by" 7 "lead closed as done · built by #532"
gh_items <<'ITEMS'
Building 10 A pitch
Ready 40 A task
ITEMS
gh_child 40 - -
run board demo link 10 40
recorded "link" 40 "lead made a sub-issue of #10"
claimable
gh_child 77 - -
run board demo task lead 10 "A task" "$WORK/reply"
recorded "task" 77 "lead filed as Ready under #10"
gh_child 40 10 -
run board demo unlink lead 10 40
recorded "unlink" 40 "lead taken off #10"
run board demo depends lead 40 10 "same view"
recorded "depends" 40 "lead blocked by #10"
gh_blocked <<'DEPS'
10
DEPS
run board demo undepend lead 40 10 "not after all"
recorded "undepend" 40 "lead no longer blocked by #10"

case_ "reads, refusals, failures and --dry-run record nothing"
gh_items <<'ITEMS'
Pitched 7 A pitch
Exploring 8 A draft
ITEMS
for read in "list" "wip" "next" "body 7" "children 7" "pr 7" "feedback lead 7"; do
  # shellcheck disable=SC2086 # each read is its own words
  run board demo $read
  unrecorded "$read"
done
run board demo approve you 8
failed "approve a draft"
unrecorded "refused"
run board demo approve lead 7
unrecorded "lead approving"
gh_items <<'ITEMS'
In_review 8 A task in front of me
ITEMS
gh_pr 908 false
touch "$BIN/merge-fails"
run board demo accept you 8
failed "merge fails"
unrecorded "failed merge"
rm "$BIN/merge-fails"
run board --dry-run demo accept you 8
same "dry-run exit" 0 "$STATUS"
unrecorded "dry run"

case_ "history is newest first, and kept per team"
gh_items <<'ITEMS'
Pitched 7 A pitch
ITEMS
run board demo comment lead 7 "$WORK/reply"
run board demo approve you 7
cp "$TEAM" "$CONFIG/teams/other.json"
A_TEAM_CONFIG="$CONFIG" "$A_TEAM" board other comment dev 7 "$WORK/reply" >/dev/null 2>&1
same "order" "you Pitched → Approved
lead commented" "$(history_table 7)"
same "other team" "dev commented" \
  "$(A_TEAM_CONFIG="$CONFIG" "$A_TEAM" board other history 7 | jq -r '.events[] | "\(.who) \(.what)"')"
run board demo history 7
same "since" "$(jq -r '.events[-1].at' "$OUT")" "$(jq -r .since "$OUT")"

# A record begun at $1 by the Lead commenting on #7.
record_from() {
  rm -f "$A_TEAM_STATE/history.db"
  run board demo comment lead 7 "$WORK/reply"
  sqlite3 "$A_TEAM_STATE/history.db" "UPDATE events SET at = '$1'"
}
caught_calls() { if [ -f "$BIN/caught-calls" ]; then grep -c '' "$BIN/caught-calls"; else echo 0; fi; }
caught_arg() { sed -n "s/^$1=//p" "$BIN/caught-args"; }

case_ "the read catches up on what was done on GitHub directly, each as its own kind of event, in one query"
gh_items <<'ITEMS'
Pitched 7 A pitch
In_review 8 A task
ITEMS
record_from "$(ago 120)"
said=$(ago 40)
gh_caught <<CAUGHT
8 closed $(ago 50) reviewer 908
9 closed $(ago 49) reviewer COMPLETED
10 closed $(ago 48) reviewer NOT_PLANNED
7 comment $said reviewer
7 moved $(ago 30) reviewer Pitched>Approved
7 moved $(ago 29) reviewer >Idea
7 comment $(ago 20) octocat
911 merged $(ago 10) reviewer
CAUGHT
waiting
same "exit" 0 "$STATUS"
same "one query" 1 "$(caught_calls)"
same "comments, and no moves: the board reads those itself" "octocat commented
you commented
lead commented" "$(history_table 7)"
same "merged" "you accepted · PR #908 merged" "$(history_table 8)"
same "closed" "you accepted · closed" "$(history_table 9)"
same "not planned" "you closed as not planned" "$(history_table 10)"
same "merged PR" "you accepted · PR #911 merged" "$(history_table 911)"
same "when" "$said" "$(A_TEAM_CONFIG="$CONFIG" "$A_TEAM" board demo history 7 | jq -r '.events[-2].at')"

case_ "catching up again records nothing new"
waiting
same "exit" 0 "$STATUS"
same "twice" 3 "$(history_table 7 | grep -c '')"

case_ "the catch-up leaves out bots, other projects, and what happened before the record began"
record_from "$(ago 120)"
gh_caught <<CAUGHT
7 comment $(ago 40) demo-app[bot]
7 moved $(ago 39) github-project-automation[bot] In_review>Done
7 moved $(ago 38) reviewer Idea>Ready elsewhere/1
7 moved $(ago 37) reviewer Idea>Ready mentaldesk/2
7 comment $(ago 200) reviewer
CAUGHT
waiting
same "exit" 0 "$STATUS"
same "left out" "lead commented" "$(history_table 7)"

case_ "what you did from the dashboard isn't recorded twice when the catch-up sees it on GitHub"
record_from "$(ago 120)"
run board demo approve you 7
run board demo comment you 7 "$WORK/reply"
gh_pr 908 false
echo '{"head": {"sha": "deadbeefcafe", "ref": "feat/task", "repo": {"full_name": "mentaldesk/demo"}}}' >"$PULL"
run board demo accept you 8
gh_caught <<CAUGHT
7 moved $(ago 0) reviewer Pitched>Approved
7 comment $(ago 0) reviewer
7 comment $(ago 1) reviewer
8 closed $(ago 0) reviewer 908
CAUGHT
waiting
same "exit" 0 "$STATUS"
same "once each" "you commented
you Pitched → Approved
you commented
lead commented" "$(history_table 7)"
same "accepted once" "you accepted · PR #908 merged" "$(history_table 8)"

case_ "a decline from the dashboard stays a decline when the catch-up sees the close on GitHub"
gh_items <<'ITEMS'
Idea 9 An Idea
ITEMS
record_from "$(ago 120)"
run board demo decline you 9 "$WORK/reply"
gh_caught <<CAUGHT
9 comment $(ago 0) reviewer
9 closed $(ago 0) reviewer NOT_PLANNED
CAUGHT
waiting
same "exit" 0 "$STATUS"
same "declined once" "you declined
you commented" "$(history_table 9)"

case_ "the catch-up reaches back to the start of the record, then asks after the items whose Status moved"
gh_items <<'ITEMS'
Pitched 7 A pitch
In_review 8 A task
Ready 12 A task
ITEMS
start=$(ago 120)
record_from "$start"
waiting
same "from the start" "$start" "$(caught_arg since)"
same "search" "repo:mentaldesk/demo updated:>=$start" "$(caught_arg q)"
gh_items <<'ITEMS'
Approved 7 A pitch
Ready 12 A task
ITEMS
waiting
grep -q 'nodes(ids: \["I_7","I_8"\])' "$BIN/caught-args" || fail "moved: $(grep 'nodes(ids' "$BIN/caught-args")"
[[ $(caught_arg since) > $start ]] || fail "since: $(caught_arg since) is still the start of the record"

case_ "a fresh install begins its record on its first read, without asking GitHub"
rm -f "$A_TEAM_STATE/history.db"
gh_items <<'ITEMS'
Pitched 7 A pitch
ITEMS
waiting
same "exit" 0 "$STATUS"
same "no query" 0 "$(caught_calls)"
waiting
same "then one" 1 "$(caught_calls)"

case_ "--dry-run doesn't catch up"
gh_items <<'ITEMS'
Pitched 7 A pitch
ITEMS
run board --dry-run demo waiting
same "exit" 0 "$STATUS"
same "no query" 0 "$(caught_calls)"


case_ "with nothing recorded, trend has no start, no week ago and nothing accepted, and makes no record file"
rm -f "$A_TEAM_STATE/history.db"
same "empty" '{"since":null,"weekAgo":null,"accepted":0,"spentSince":null,"spent":0}' "$(trend)"
unrecorded "trend"

case_ "waiting records how many items it returned, at most once an hour"
gh_items <<'ITEMS'
Pitched 7 A pitch
In_review 8 A task
Ready 12 Not waiting
ITEMS
waiting
same "first" 2 "$(queue)"
gh_items <<'ITEMS'
Pitched 7 A pitch
ITEMS
waiting
same "within the hour" 2 "$(queue)"
sqlite3 "$A_TEAM_STATE/history.db" "UPDATE queue SET at = '$(ago 61)'"
waiting
same "an hour on" "2
1" "$(queue)"
run board --dry-run demo waiting
sqlite3 "$A_TEAM_STATE/history.db" "UPDATE queue SET at = '$(ago 61)'"
run board --dry-run demo waiting
same "dry run" "2
1" "$(queue)"

finish_suite
