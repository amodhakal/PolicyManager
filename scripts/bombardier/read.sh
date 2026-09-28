#!/usr/bin/env bash
# bombardier example: read-heavy load against the paged list endpoints.
# Install: go install github.com/codesenberg/bombardier@latest
#
# The API is behind bearer authentication, so a token is required. Either
# export API_TOKEN="$(node ../mint-token.ts)" beforehand, or let this script
# mint one from JWT_SIGNING_KEY (the API's Jwt:SigningKey).
#
# Note the rate limit: the API rejects a caller over RateLimiting__PermitLimit
# (100/min by default) with a 429, which bombardier counts as a failure. 50
# connections making 10,000 requests will hit that long before it finishes, so
# raise the quota on the server (RateLimiting__PermitLimit) for the run.
set -euo pipefail

BASE_URL="${BASE_URL:-http://localhost:8080}"

if [[ -z "${API_TOKEN:-}" ]]; then
  echo "No API_TOKEN. Export one, or set JWT_SIGNING_KEY to the API's Jwt:SigningKey so this" >&2
  echo "script can mint one: API_TOKEN=\"\$(node $(dirname "$0")/../mint-token.ts)\" bash $0" >&2
  exit 1
fi

AUTH=(-H "Authorization: Bearer ${API_TOKEN}")

bombardier -c 50 -n 10000 "${AUTH[@]}" "${BASE_URL}/api/policyholders?page=1&pageSize=25"
bombardier -c 50 -n 10000 "${AUTH[@]}" "${BASE_URL}/api/policies?status=Active&page=1&pageSize=25"
bombardier -c 50 -n 10000 "${AUTH[@]}" "${BASE_URL}/api/claims?page=1&pageSize=25"
