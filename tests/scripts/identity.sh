#!/usr/bin/env bash
# Whose login a command acts as.
# shellcheck source=tests/scripts/lib.sh
source "$(dirname "${BASH_SOURCE[0]}")/lib.sh"
# shellcheck disable=SC2034,SC2154 # helpers read what an area sets, and an area reads what lib.sh sets
app_fakes

# A role's board command binds itself to the App, whatever shell runs it.
# as_shell: records the token each gh call used, "shell" for none.
as_shell() {
  mv "$BIN/gh" "$BIN/real-gh"
  mv "$BIN/gh.body" "$BIN/real-gh.body"
  fake "$BIN/gh" <<SH
#!/usr/bin/env bash
echo "\${GH_TOKEN:-shell}" >>"$BIN/tokens"
exec "$BIN/real-gh" "\$@"
SH
  : >"$BIN/tokens"
}
tokens_used() { sort -u "$BIN/tokens" | tr '\n' ' '; }
identity_fixture() {
  fixture <<'JSON'
{ "repo": "mentaldesk/demo", "stakeholders": ["reviewer"], "project": { "owner": "mentaldesk", "number": 1 },
  "wip": { "pitched": 2, "exploring": 2, "worktrees": 2, "devs": 1, "ideas": 2, "readyFloor": 0 },
  "app": { "id": 7, "slug": "demo-app" } }
JSON
}
echo reply >"$WORK/identity-reply"

case_ "a role's comment, move and 👀 from your own terminal use the App's token, not your login"
identity_fixture
A_TEAM_STATE=$(mktemp -d "$WORK/state.XXXXXX")
gh_items <<'ITEMS'
Exploring 7 A pitch
Ready 8 A task
ITEMS
as_shell
GH_TOKEN=ghp_yours run board demo comment lead 7 "$WORK/identity-reply"
same "comment exit" 0 "$STATUS"
same "comment token" "ghs_app " "$(tokens_used)"
: >"$BIN/tokens"
GH_TOKEN=ghp_yours run board demo claim dev 8
same "claim exit" 0 "$STATUS"
same "claim token" "ghs_app " "$(tokens_used)"
: >"$BIN/tokens"
GH_TOKEN=ghp_yours run board demo mine dev
same "read token" "ghs_app " "$(tokens_used)"

case_ "lead-next, whose role is in its name, uses the App's token too"
: >"$BIN/tokens"
GH_TOKEN=ghp_yours run board demo lead-next
same "lead-next token" "ghs_app " "$(tokens_used)"

case_ "with the flag first, triggers still finds its role"
: >"$BIN/tokens"
GH_TOKEN=ghp_yours run board demo triggers --sweep dev
same "triggers token" "ghs_app " "$(tokens_used)"

case_ "in a dispatcher's run, a role's command still acts as the App"
: >"$BIN/tokens"
A_TEAM_RUN_TEAM=demo run board demo comment lead 7 "$WORK/identity-reply"
same "run exit" 0 "$STATUS"
same "run token" "ghs_app " "$(tokens_used)"

case_ "a stakeholder's command, outside a run, keeps your own login"
: >"$BIN/tokens"
run board demo comment you 7 "$WORK/identity-reply"
same "you exit" 0 "$STATUS"
same "you token" "shell " "$(tokens_used)"

case_ "a role with no App stops, as status words it, and reaches nothing as you"
NO_APP=1 fixture <<'JSON'
{ "repo": "mentaldesk/demo", "stakeholders": ["reviewer"], "project": { "owner": "mentaldesk", "number": 1 } }
JSON
: >"$BIN/tokens"
NO_TOKEN=1 run board demo comment lead 7 "$WORK/identity-reply"
failed "no app"
same "no app says" "board.sh: lead acts as the App, which can't get a token: no GitHub App: run a-team app create demo, then install it" "$(cat "$ERR")"
same "no app gh calls" 0 "$(grep -c '' <"$BIN/tokens")"

case_ "a role whose App has no key stops, naming the App and what to do, and reaches nothing as you"
identity_fixture
A_TEAM_STATE=$(mktemp -d "$WORK/state.XXXXXX")
: >"$BIN/tokens"
NO_TOKEN=1 NO_KEY=1 run board demo comment lead 7 "$WORK/identity-reply"
failed "no key"
grep -q "^board.sh: lead acts as demo-app\[bot\], which can't get a token: no private key .*: generate a new private key on the App's settings page on GitHub, then run: security add-generic-password -U -s a-team-app -a mentaldesk -w " "$ERR" ||
  fail "no key: '$(cat "$ERR")'"
grep -q 'app create' "$ERR" && fail "no key: sent to app create, which needs the key: '$(cat "$ERR")'"
same "no key gh calls" 0 "$(grep -c '' <"$BIN/tokens")"

case_ "a role whose App isn't installed stops, saying where to install it, and reaches nothing as you"
: >"$BIN/tokens"
NO_TOKEN=1 NOT_INSTALLED=1 run board demo move lead 7 Pitched
failed "not installed"
grep -q "^board.sh: lead acts as demo-app\[bot\], which can't get a token: app 7 isn't installed on mentaldesk/demo: install it at https://github.com/apps/demo-app/installations/new$" "$ERR" ||
  fail "not installed: '$(cat "$ERR")'"
same "not installed gh calls" 0 "$(grep -c '' <"$BIN/tokens")"

case_ "every you command refuses inside a team's run, and reaches nothing"
while read -r command rest; do
  : >"$BIN/tokens"
  # shellcheck disable=SC2086 # the words are the command's arguments
  A_TEAM_RUN_TEAM=demo run board demo "$command" you $rest
  failed "$command you"
  same "$command you says" "board.sh: only the stakeholder can $command as you, and this is a team's run" "$(cat "$ERR")"
  same "$command you gh calls" 0 "$(grep -c '' <"$BIN/tokens")"
done <<'YOURS'
approve 7
accept 8
priority 7 high
decline 7 file
shelve 7 2 file
comment 7 file
hold 8 file
release 8
new file
attach file
add 7 Idea
move 7 Idea
edit 7 path sha file
decision 1 retire
vision-review
spent - 2026-01-01T00:00:00Z 60 Review read
YOURS

finish_suite
