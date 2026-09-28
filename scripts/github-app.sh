# shellcheck shell=bash
# Sourced by token.sh, app.sh and board.sh: the team's GitHub App, as seen from this Mac.

# The private key is a base64-encoded PEM in the login Keychain, one item per account the App is
# registered under.
KEYCHAIN_SERVICE=a-team-app

has_app_key() { security find-generic-password -s "$KEYCHAIN_SERVICE" -a "$1" >/dev/null 2>&1; }

b64url() { openssl base64 -A | tr '+/' '-_' | tr -d '='; }

# app_jwt <app id> <owner>: a JWT the App signs to speak as itself, valid for nine minutes and
# backdated one for clock drift. The key never touches the disk.
app_jwt() {
  local id=$1 owner=$2 key now header payload signature
  key=$(security find-generic-password -s "$KEYCHAIN_SERVICE" -a "$owner" -w 2>/dev/null) || {
    echo "no private key in the login Keychain under service '$KEYCHAIN_SERVICE', account '$owner'" >&2
    return 1
  }
  now=$(date +%s)
  header=$(printf '{"alg":"RS256","typ":"JWT"}' | b64url)
  payload=$(printf '{"iat":%d,"exp":%d,"iss":"%s"}' $((now - 60)) $((now + 540)) "$id" | b64url)
  signature=$(printf '%s.%s' "$header" "$payload" |
    openssl dgst -sha256 -sign <(printf '%s' "$key" | base64 -d) | b64url) || return 1
  printf '%s.%s.%s\n' "$header" "$payload" "$signature"
}

# github <method> <path> [<bearer>]: GitHub's REST API with curl, since gh won't send a JWT. The
# bearer goes in through a file descriptor, so it never shows in the process list.
github() {
  local method=$1 path=$2 bearer=${3:-}
  curl -sS --fail-with-body -X "$method" \
    -H "Accept: application/vnd.github+json" -H "X-GitHub-Api-Version: 2022-11-28" \
    ${bearer:+--header @<(printf 'Authorization: Bearer %s\n' "$bearer")} \
    "https://api.github.com/$path"
}

token_cache() { echo "$STATE/$1/token.json"; }
