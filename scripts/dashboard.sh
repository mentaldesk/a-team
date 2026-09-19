#!/usr/bin/env bash
#
# dashboard.sh [team...] — a terminal dashboard of what each agent is doing.
#
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
dotnet build "$ROOT/dashboard/Dashboard.csproj" --nologo -v quiet -clp:ErrorsOnly >/dev/null
exec "$ROOT/dashboard/bin/Debug/net10.0/a-team-dashboard" "$@"
