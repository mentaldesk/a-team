#!/usr/bin/env bash
#
# app.sh create <team> [--id <app id>] [--name <name>] — gives the team its own GitHub App. Registers
# one from a manifest under the team repo's owner, or reuses the one already registered there, then
# opens the page that installs it.
#
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
source "$ROOT/scripts/common.sh"
source "$ROOT/scripts/github-app.sh"

die() { echo "a-team app: $*" >&2; exit 1; }
usage="usage: a-team app create <team> [--id <app id>] [--name <name>]"

[ "${1:-}" = create ] || die "$usage"
TEAM=${2:?$usage}
shift 2
ID='' NAME=''
while [ $# -gt 0 ]; do
  case "$1" in
    --id) ID=${2:?$usage}; shift 2 ;;
    --name) NAME=${2:?$usage}; shift 2 ;;
    *) die "$usage" ;;
  esac
done

CONFIG=$(team_config "$TEAM")
[ -f "$CONFIG" ] || die "no config for team '$TEAM' at $CONFIG"
REPO=$(jq -r '.repo // empty' "$CONFIG")
[ -n "$REPO" ] || die "no repo in $CONFIG"
OWNER=${REPO%/*}
NAME=${NAME:-a-team-$OWNER}

set_app() {
  local updated
  updated=$(jq --argjson id "$1" --arg slug "$2" '.app = {id: $id, slug: $slug}' "$CONFIG")
  printf '%s\n' "$updated" >"$CONFIG"
  echo "  ✓ team config  app.id $1, app.slug $2 written to $CONFIG"
}

# install <app id> <slug>: opens the page that installs the App, unless it's on the repo already.
install() {
  local url="https://github.com/apps/$2/installations/new" jwt found
  jwt=$(app_jwt "$1" "$OWNER") || exit 1
  found=$(installation "$1" "$OWNER" "$REPO" "$jwt") && {
    echo "  ✓ installed    on $REPO already, so run: a-team board $TEAM check"
    return
  }
  [ $? -eq 2 ] || die "$found"
  echo "opening the install page: install it on $REPO, then run: a-team board $TEAM check"
  open "$url" 2>/dev/null || echo "  $url"
}

if jq -e '.app.id' "$CONFIG" >/dev/null 2>&1; then
  echo "$TEAM already has an App: $(jq -r .app.slug "$CONFIG") (app $(jq -r .app.id "$CONFIG"))"
  install "$(jq -r .app.id "$CONFIG")" "$(jq -r .app.slug "$CONFIG")"
  exit 0
fi

# One App per account: another team under the same owner already names it.
for other in $(team_names); do
  app=$(jq -c --arg owner "$OWNER" 'select((.repo // "") | startswith($owner + "/")) | .app // empty' \
    "$(team_config "$other")" 2>/dev/null) || continue
  [ -n "$app" ] || continue
  echo "reusing $OWNER's App from team $other"
  set_app "$(jq .id <<<"$app")" "$(jq -r .slug <<<"$app")"
  install "$(jq .id <<<"$app")" "$(jq -r .slug <<<"$app")"
  exit 0
done

if has_app_key "$OWNER"; then
  [ -n "$ID" ] || die "$OWNER already has an App's key in the Keychain (service $KEYCHAIN_SERVICE), so reuse it:
  rerun with --id <app id>, from the App's settings page on GitHub"
  jwt=$(app_jwt "$ID" "$OWNER") || exit 1
  app=$(github GET app "$jwt") || die "the Keychain key isn't app $ID's: $app"
  echo "reusing $OWNER's App from the Keychain"
  set_app "$ID" "$(jq -r .slug <<<"$app")"
  install "$ID" "$(jq -r .slug <<<"$app")"
  exit 0
fi

# Registering: a local page posts the manifest to GitHub, and GitHub sends the browser back to a
# listener here with a code that exchanges for the App's id, slug and key.
tmp=$(mktemp -d)
trap 'kill "${listener:-}" 2>/dev/null; rm -rf "$tmp"' EXIT
port=$((20000 + RANDOM % 20000))
state=$(openssl rand -hex 16)
if [ "$(gh api "users/$OWNER" --jq .type)" = Organization ]; then
  action="https://github.com/organizations/$OWNER/settings/apps/new?state=$state"
else
  action="https://github.com/settings/apps/new?state=$state"
fi
manifest=$(jq -n --arg name "$NAME" --arg redirect "http://127.0.0.1:$port/" '{
  name: $name, url: "https://github.com/mentaldesk/a-team", redirect_url: $redirect, public: false,
  hook_attributes: {url: "https://github.com/mentaldesk/a-team", active: false},
  default_events: [],
  default_permissions: {issues: "write", pull_requests: "write", contents: "write", actions: "write",
    workflows: "write", checks: "read", metadata: "read", organization_projects: "write",
    issue_fields: "read"}}')
jq -rn --arg action "$action" --arg manifest "$manifest" '"<!doctype html><title>a-team</title>
<form id=f method=post action=\"\($action | @html)\">
<input type=hidden name=manifest value=\"\($manifest | @html)\"></form>
<script>document.getElementById(\"f\").submit()</script>"' >"$tmp/register.html"

mkfifo "$tmp/response"
nc -l 127.0.0.1 "$port" <"$tmp/response" >"$tmp/request" &
listener=$!
exec 3>"$tmp/response"

echo "opening your browser to register $NAME under $OWNER…"
open "$tmp/register.html"
until grep -q $'\r' "$tmp/request" 2>/dev/null; do
  kill -0 "$listener" 2>/dev/null || die "the listener on port $port stopped before GitHub called back"
  sleep 0.2
done
page() {
  local body="<!doctype html><title>a-team</title><p>$1</p>"
  printf 'HTTP/1.1 200 OK\r\nContent-Type: text/html\r\nContent-Length: %d\r\nConnection: close\r\n\r\n%s' \
    "${#body}" "$body" >&3
  exec 3>&-
}
failed() { page "Registration failed: $1. The terminal says why."; die "$2"; }

query=$(head -1 "$tmp/request" | sed -E 's/^GET [^?]*\?([^ ]*) .*/\1/')
code=$(tr '&' '\n' <<<"$query" | sed -n 's/^code=//p')
[ "$(tr '&' '\n' <<<"$query" | sed -n 's/^state=//p')" = "$state" ] ||
  failed "this callback isn't from the registration a-team started" "GitHub's callback didn't carry this registration's state"
[ -n "$code" ] || failed "GitHub sent no code" "GitHub called back without a code: $(head -1 "$tmp/request")"

app=$(github POST "app-manifests/$code/conversions") ||
  failed "GitHub wouldn't convert the manifest" "GitHub wouldn't convert the manifest: $app"
id=$(jq -r .id <<<"$app")
slug=$(jq -r .slug <<<"$app")
echo "  ✓ registered   $slug (app $id)"
security add-generic-password -U -s "$KEYCHAIN_SERVICE" -a "$OWNER" -l "a-team app $id" \
  -w "$(jq -r .pem <<<"$app" | openssl base64 -A)" ||
  failed "the App's key couldn't be stored" "couldn't store $slug's private key in the login Keychain"
echo "  ✓ private key  stored in the login Keychain (service $KEYCHAIN_SERVICE, account $OWNER)"
set_app "$id" "$slug"
page "Registered $slug. Back to the terminal."
install "$id" "$slug"
