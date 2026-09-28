#!/usr/bin/env bash
# bombardier example: read-heavy load against the paged list endpoints.
# Install: go install github.com/codesenberg/bombardier@latest
# The API must be running and BASE_URL must point at it.
set -euo pipefail
BASE_URL="${BASE_URL:-http://localhost:8080}"

bombardier -c 50 -n 10000 "${BASE_URL}/api/policyholders?page=1&pageSize=25"
bombardier -c 50 -n 10000 "${BASE_URL}/api/policies?status=Active&page=1&pageSize=25"
bombardier -c 50 -n 10000 "${BASE_URL}/api/claims?page=1&pageSize=25"
