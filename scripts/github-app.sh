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
# bearer goes in through a file descriptor, so it never shows in the process list. On an error it
# prints GitHub's message and the HTTP status instead, and fails.
github() {
  local method=$1 path=$2 bearer=${3:-} response status message
  response=$(curl -s -w '\n%{http_code}' -X "$method" \
    -H "Accept: application/vnd.github+json" -H "X-GitHub-Api-Version: 2022-11-28" \
    ${bearer:+--header @<(printf 'Authorization: Bearer %s\n' "$bearer")} \
    "https://api.github.com/$path") || { echo "couldn't reach GitHub"; return 1; }
  status=${response##*$'\n'}
  response=${response%$'\n'*}
  case $status in
    2??) printf '%s\n' "$response" ;;
    *)
      message=$(jq -r '.message // empty' <<<"$response" 2>/dev/null) || message=''
      echo "${message:-no message} (HTTP $status)"
      return 1 ;;
  esac
}

# installation <app id> <owner> <repo> <jwt>: the App's installation on the repo. Fails with why,
# with status 2 when the App just isn't installed there.
installation() {
  local answer
  answer=$(github GET "repos/$3/installation" "$4") && { printf '%s\n' "$answer"; return; }
  case $answer in
    *"(HTTP 404)") echo "app $1 isn't installed on $3"; return 2 ;;
    *"(HTTP 401)") echo "GitHub turned down app $1's key: the one in the Keychain under account $2 isn't that App's ($answer)" ;;
    *) echo "GitHub wouldn't say whether app $1 is installed on $3: $answer" ;;
  esac
  return 1
}

token_cache() { echo "$STATE/$1/token.json"; }
