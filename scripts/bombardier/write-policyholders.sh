#!/usr/bin/env bash
# bombardier example: write load (policyholder creation).
# Install: go install github.com/codesenberg/bombardier@latest
#
# Each request reuses the same body file, so expect 409s after the first
# request — the unique email index rejects the duplicate. This example is for
# raw throughput measurement, not valid intake. For varied valid writes use the
# k6 load suite or scripts/addPolicyHolders.ts.
#
# The API is behind bearer authentication, so a token is required. Either
# export API_TOKEN="$(node ../mint-token.ts)" beforehand, or set JWT_SIGNING_KEY
# so it can be minted from the API's Jwt:SigningKey.
set -euo pipefail

BASE_URL="${BASE_URL:-http://localhost:8080}"
BODY="$(dirname "$0")/policyholder-body.json"

if [[ -z "${API_TOKEN:-}" ]]; then
  echo "No API_TOKEN. Export one, or set JWT_SIGNING_KEY to the API's Jwt:SigningKey so this" >&2
  echo "script can mint one: API_TOKEN=\"\$(node $(dirname "$0")/../mint-token.ts)\" bash $0" >&2
  exit 1
fi

bombardier -c 10 -n 1000 -m POST \
  -H "Content-Type: application/json" \
  -H "Authorization: Bearer ${API_TOKEN}" \
  -b "$BODY" \
  "${BASE_URL}/api/policyholders"
